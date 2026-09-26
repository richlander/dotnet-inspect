using ILInspector.Metadata;
using DotnetInspect.Cli.Inspectors;
using DotnetInspect.Cli.Options;
using DotnetInspect.Cli.Output;
using DotnetInspector.Packages;
using DotnetInspector.Queries;
using DotnetInspector.ResearchSections;
using DotnetInspector.Sections;
using DotnetInspect.Cli.Sections;
using DotnetInspector.Services;
using DotnetInspect.Cli.Services;
using DotnetInspect.Cli.Views;
using ILInspector.Analysis;
using ILInspector.Decompiler;
using ILInspector.Decompiler.Pipeline;
using Inspector.Findings;
using ILInspector.Instructions;
using ILInspector.Research;
using Markout;
using System.Collections.Immutable;
using System.Text.Json;
using QuerySpace.Rows;

namespace DotnetInspect.Cli.Commands;

/// <summary>
/// Compares API surfaces or selected body-level evidence between two versions.
/// </summary>
public class DiffCommand
{
    public const string Name = "diff";
    public static async Task<int> ExecuteAsync(
        DiffOptions options,
        CancellationToken cancellationToken = default)
    {
        if (options.History)
        {
            return await DiffHistoryCommand.ExecuteAsync(
                options,
                cancellationToken);
        }
        if (options.At.Length > 0
            || options.MaxProbes is not null
            || options.SamplePercent is not null
            || options.MajorVersions
            || options.IncludePrerelease)
        {
            CommandError.Write(
                "--at, --max-probes, --sample-percent, --major-versions, and --preview require --history.");
            return 1;
        }
        if (options.Count)
        {
            CommandError.Write(
                "--count requires --history; pairwise Diff does not declare a countable cohort.");
            return 1;
        }
        string? transportOption = options.EnvelopeOutput ? "--envelope"
            : options.CompactJson ? "--compact" : null;
        bool implementationTransport =
            RequestsCompleteImplementationDiff(options);
        if (implementationTransport
            && HasIncompatibleImplementationTransportProjection(options))
        {
            CommandError.Write(
                "Complete Implementation Diff transport cannot be combined "
                    + "with presentation projection or another diff operation.");
            return 1;
        }
        if (transportOption is not null
            && ((!implementationTransport
                    && options.HasContentProjection)
                || implementationTransport
                    && HasIncompatibleImplementationTransportProjection(options)
                || options.EnvelopeOutput && options.JsonOutput
                || options.Discover is not null
                || !implementationTransport
                    && options.MemberFilter.Count > 0
                || options.Finding is not null
                || options.IncludePdbSource
                || options.SourceRepositories.Length > 0
                || options.ChangedOnly
                || options.AllocRegressionsOnly
                || options.Legend))
        {
            CommandError.Write(
                $"{transportOption} requires an unprojected Library API diff; "
                + "filters, selected sections, discovery, and other diff operations are not supported.");
            return 1;
        }
        if (options.EnvelopeOutput && options.HasRenderedLineWindow)
        {
            CommandError.Write(
                "--envelope cannot be combined with rendered-line clipping.");
            return 1;
        }
        if (options.CompactJson && !options.JsonOutput && !options.EnvelopeOutput)
        {
            CommandError.Write("--compact requires --json or --envelope.");
            return 1;
        }
        if (options.Schema && options.Discover is null)
        {
            CommandError.Write("--schema requires -D/--discover.");
            return 1;
        }

        DiffSectionCatalog catalog = DiffSections.CreateCatalog();
        SectionCatalog<DiffDiscoveryModel> sectionCatalog = catalog.Sections;
        var pipeline = catalog.Pipeline;
        var selectResult = SelectResolver.ResolveSelectAsSections(
            options.Select,
            sectionCatalog.SelectableSectionNames,
            sectionCatalog.InfoSectionNames,
            sectionCatalog.SelectionCategoryMap,
            selectDefault: options.SelectDefault,
            exactOnlySections: DiffSections.ExactOnlySections);
        if (SelectOutput.WriteUnresolved(selectResult))
            return 1;
        if (options.Select is { Length: > 0 } selectors
            && selectResult.Sections is null)
        {
            SelectOutput.WriteUnresolved(
                new SelectResult(
                    null,
                    selectors
                        .Select(selector =>
                            new SelectMiss(selector, [], IsGlob: true))
                        .ToArray()));
            return 1;
        }
        if (selectResult.Sections != null)
        {
            options = options with
            {
                IncludeSections = selectResult.Sections,
                ExactIncludeSectionsOverride = selectResult.ExactSections,
            };
        }
        implementationTransport =
            RequestsCompleteImplementationDiff(options);
        if (implementationTransport
            && HasIncompatibleImplementationTransportProjection(options))
        {
            CommandError.Write(
                "Complete Implementation Diff transport cannot be combined "
                    + "with presentation projection or another diff operation.");
            return 1;
        }
        if (options.Finding is not null && options.IncludeSections is null)
        {
            options = options with
            {
                IncludeSections = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    DiffSections.FindingTransitions.Name,
                }
            };
        }

        var hasPlatform = !string.IsNullOrEmpty(options.PlatformVersionRange);
        var hasPackage = !string.IsNullOrEmpty(options.PackageVersionRange);
        var hasLibrary = !string.IsNullOrEmpty(options.LibraryVersionRange);
        if (implementationTransport && !hasLibrary)
        {
            CommandError.Write(
                "Complete Implementation Diff transport currently requires "
                    + "one local --library pair.");
            return 1;
        }

        // Discovery mode: -D/--discover lists schema
        if (options.Discover != null)
        {
            var schemaMap = DiffSections.CreateSchema();
            var discoverable = pipeline.GetDiscoverableSections(new DiffDiscoveryModel(), options.IncludeSections);
            return DiscoverOutput.ExecuteEffective(options.Discover, discoverable, schemaMap,
                DiscoveryOutputRequest.Create(
                    options.Jsonl ? OutputFormat.Jsonl
                        : options.Tsv ? OutputFormat.Tsv
                        : options.Tabular ? OutputFormat.Table
                        : OutputFormat.Markdown,
                    options.Tree,
                    options.TabularExplicitlySet,
                    options.NoHeader),
                sectionCostAnnotations: pipeline.GetCostAnnotations(),
                sectionCategories: pipeline.GetCategoryMap(),
                catalogHiddenSections:
                    options.Schema ? null : pipeline.GetCatalogHiddenSections(),
                listedCategoryDoors: pipeline.GetListedCategoryDoors(),
                exactOnlySections: DiffSections.ExactOnlySections);
        }

        if (!OutputFormatResolver.ValidateSingleSectionForTabular(
                options.TabularExplicitlySet,
                options.IncludeSections))
            return 1;

        if (options.IncludePdbSource && !SelectsImplementationDiff(options))
        {
            CommandError.Write(
                "PDB source acquisition requires the Implementation Diff section.");
            return 1;
        }

        if (!hasPlatform && !hasPackage && !hasLibrary)
        {
            CommandError.Write("--package, --platform, or --library with version range required.");
            CommandError.WriteLine("Examples:");
            CommandError.WriteLine("  --package System.Text.Json@9.0.0..10.0.2");
            CommandError.WriteLine("  --platform System.Text.Json@8.0.23..10.0.2");
            CommandError.WriteLine("  --library old/Foo.dll..new/Foo.dll");
            return 1;
        }

        if ((hasPlatform ? 1 : 0) + (hasPackage ? 1 : 0) + (hasLibrary ? 1 : 0) > 1)
        {
            CommandError.Write("Cannot specify more than one of --package, --platform, and --library.");
            return 1;
        }

        if (SelectsFindingTransitions(options))
        {
            if (options.IncludeSections is { Count: > 0 } sections
                && (sections.Count != 1
                    || !sections.Contains(DiffSections.FindingTransitions.Name)))
            {
                CommandError.Write(
                    "Finding Transitions must be selected by itself because it is a " +
                    "focused endpoint-confirmation lens; use @Diff for composable " +
                    "comparison sections.");
                return 1;
            }
            if (options.Finding is null
                && options.TypeFilter.Count == 0
                && options.MemberFilter.Count == 0)
            {
                CommandError.Write("Finding Transitions requires --type or a type-qualified --member target.");
                return 1;
            }
            if (!TryResolveFindingDescriptor(options, out var findingDescriptor, out var findingError))
            {
                CommandError.Write($"{findingError}");
                return 1;
            }
            if (IsMemberBodyFindingDescriptor(findingDescriptor))
            {
                if (options.MemberFilter.Count != 1)
                {
                    CommandError.Write(
                        $"--finding {findingDescriptor} requires exactly one --member target.");
                    return 1;
                }
            }
            else if (findingDescriptor == MetadataFindings.TypeDescriptor.Id)
            {
                if (options.TypeFilter.Count == 0 || options.MemberFilter.Count > 0)
                {
                    CommandError.Write("--finding api.type requires --type and cannot be combined with --member.");
                    return 1;
                }
            }
            else if (findingDescriptor == MetadataFindings.AttributeDescriptor.Id)
            {
                if (options.TypeFilter.Count == 0 || options.MemberFilter.Count > 0)
                {
                    CommandError.Write("--finding api.attribute requires --type and cannot be combined with --member.");
                    return 1;
                }
            }
            else if (findingDescriptor == MetadataFindings.MemberDescriptor.Id
                && options.TypeFilter.Count == 0
                && options.MemberFilter.Count == 0)
            {
                CommandError.Write("--finding api.member requires --type or --member.");
                return 1;
            }
            if (options.Breaking || options.Additive || options.ChangedOnly
                || options.AllocRegressionsOnly || options.NameOnly)
            {
                CommandError.Write(
                    "Finding Transitions reports the exact PairFinding kind and cannot be combined with change-classification, analysis, or name-only filters.");
                return 1;
            }
        }

        if (SelectsComplexityContext(options)
            && options.IncludeSections is { Count: > 0 } complexitySections
            && (complexitySections.Count != 1
                || !complexitySections.Contains(
                    DiffSections.ComplexityContext.Name)))
        {
            CommandError.Write(
                "Complexity Context must be selected by itself because it is "
                + "a focused local-population view; use @Diff for composable "
                + "comparison sections.");
            return 1;
        }
        if (SelectsStructuralContext(options)
            && options.IncludeSections is { Count: > 0 } structuralSections
            && (structuralSections.Count != 1
                || !structuralSections.Contains(
                    DiffSections.StructuralContext.Name)))
        {
            CommandError.Write(
                "Structural Context must be selected by itself because it is "
                + "a focused local-cohort view; use @Diff for composable "
                + "comparison sections.");
            return 1;
        }

        var context = new CommandContext(options.Verbose);
        var logger = context.Logger;

        try
        {
            DiffInputs inputs;

            if (hasPackage)
            {
                var result = await ExecutePackageDiffAsync(options, logger, context.HttpClient);
                if (result.error != null)
                {
                    CommandError.Write(result.error);
                    return 1;
                }
                inputs = result.inputs!;
            }
            else if (hasPlatform)
            {
                var result = await ExecutePlatformDiffAsync(options, logger, context.HttpClient);
                if (result.error != null)
                {
                    CommandError.Write(result.error);
                    return 1;
                }
                inputs = result.inputs!;
            }
            else
            {
                var result = await ExecuteLibraryDiffAsync(options, logger, context.HttpClient);
                if (result.error != null)
                {
                    CommandError.Write(result.error);
                    return 1;
                }
                inputs = result.inputs!;
            }

            try
            {
                if (transportOption is not null
                    && !UsesSharedLibraryApiDiff(inputs, options)
                    && !implementationTransport)
                {
                    CommandError.Write(
                        $"{transportOption} currently requires exactly one Library at each API diff endpoint; "
                        + $"resolved {inputs.From.AssemblySet.Assemblies.Count} before and "
                        + $"{inputs.To.AssemblySet.Assemblies.Count} after.");
                    return 1;
                }
                if (implementationTransport)
                {
                    if (inputs.From.AssemblySet.Assemblies.Count != 1
                        || inputs.To.AssemblySet.Assemblies.Count != 1)
                    {
                        CommandError.Write(
                            "Complete Implementation Diff transport requires "
                                + "exactly one assembly on each endpoint.");
                        return 1;
                    }

                    InspectionEnvelope<ImplementationDiffDocument> envelope =
                        ImplementationDiffInspection.Execute(
                            CreateImplementationComparisonInput(
                                inputs,
                                options));
                    return ImplementationDiffOutput.Write(
                        envelope,
                        options);
                }
                if (UsesSharedLibraryApiDiff(inputs, options))
                {
                    if (options.IsContentJson && options.HasRenderedLineWindow)
                    {
                        CommandError.Write(
                            "Unprojected Library API diff --json cannot be combined with rendered-line clipping.");
                        return 1;
                    }
                    var comparison = await LibraryApiDiffRunner.ExecuteAsync(
                        inputs.From.AssemblySet.Assemblies[0],
                        inputs.To.AssemblySet.Assemblies[0],
                        options.IncludeAll);
                    return LibraryApiDiffOutput.Write(
                        comparison,
                        inputs.Name,
                        inputs.FromVersion,
                        inputs.ToVersion,
                        options);
                }
                WorkspaceImplementationTarget? workspaceTarget =
                    SelectsComplexityContext(options)
                        || SelectsStructuralContext(options)
                        ? null
                        : TryCreateWorkspaceImplementationTarget(
                            inputs,
                            options);
                CompiledInspectionPlan<DiffQueryContext> queryPlan =
                    GetRequestedQueryPlan(
                        catalog,
                        options,
                        workspaceTarget is not null);
                InspectionQueryResults queryResults = queryPlan.Run(
                    new DiffQueryContext(
                        inputs.FromSurface,
                        inputs.ToSurface,
                        () => CreateBodySignalComparisonInput(inputs, options),
                        () => CreateImplementationComparisonInput(
                            inputs,
                            options)));
                WorkspaceImplementationComparisonResult? workspaceImplementation =
                    workspaceTarget is null
                        ? null
                        : await WorkspaceImplementationComparisonRunner.ExecuteAsync(
                            inputs.From.AssemblySet,
                            inputs.To.AssemblySet,
                            inputs.Name,
                            workspaceTarget.DeclaringType,
                            workspaceTarget.Selector,
                            context.HttpClient,
                            options.SourceOptions,
                            context.CreatePackageSourceComposition,
                            logger.Log);
                ResearchComparison? workspaceAnalysis = null;
                string? workspaceAnalysisFailure = null;
                if (workspaceTarget is not null
                    && SelectsAnalysisDiff(options))
                {
                    try
                    {
                        workspaceAnalysis =
                            RequireBodySignalComparison(
                                BodySignalComparisonQuery.Execute(
                                    CreateBodySignalComparisonInput(
                                        inputs,
                                        options)),
                                options,
                                DiffSections.AnalysisDiff.Name);
                    }
                    catch (InvalidOperationException exception)
                        when (options.MemberFilter.Count > 0)
                    {
                        workspaceAnalysisFailure =
                            exception.Message;
                    }
                }
                IReadOnlyList<ApiDiffInspectionFailure>
                    inspectionFailures =
                        ApiDiffAnalyzer.ProjectInspectionFailures(
                            inputs.FromSurface,
                            inputs.ToSurface);

                if (options.JsonOutput || options.IncludeSections is { Count: > 1 })
                {
                    bool inspectionIncomplete =
                        await WriteSelectedDocumentAsync(
                        inputs,
                        options,
                        queryResults,
                        context.HttpClient,
                        logger,
                        inspectionFailures,
                        workspaceImplementation,
                        workspaceTarget?.Display,
                        workspaceAnalysis,
                        workspaceAnalysisFailure);
                    return inspectionIncomplete ? 1 : 0;
                }

                if (SelectsFindingTransitions(options))
                {
                    var rows = BuildSelectedFindingTransitions(inputs, options);
                    var view = DiffOutputFormatter.BuildFindingTransitionsView(
                        inputs.Name,
                        rows,
                        inputs.FromVersion,
                        inputs.ToVersion);
                    if (options.Tabular || options.Tsv || options.Jsonl)
                    {
                        OutputFormatter.WriteProjectedTable(Console.Out, !options.NoHeader, options.Tsv, options.Jsonl,
                            options.Columns, options.Fields,
                            (writer, formatter, writerOptions) =>
                                MarkoutSerializer.Serialize(view, writer, formatter, DiffViewContext.Default, writerOptions),
                            options.Rows);
                    }
                    else
                    {
                        var output =
                            inspectionFailures.Count == 0
                                || options.NameOnly
                                ? DiffOutputFormatter.RenderFindingTransitionsView(
                                    view,
                                    OutputFormatter.CreateWindowedOptions(
                                        options.Rows))
                                : DiffOutputFormatter.RenderDocumentView(
                                    DiffOutputFormatter.BuildDocumentView(
                                        inputs.Name,
                                        inputs.FromVersion,
                                        inputs.ToVersion,
                                        changes: null,
                                        analysisDiff: null,
                                        implementationDiff: null,
                                        findingTransitions: view,
                                        inspectionFailures),
                                    OutputFormatter.CreateWindowedOptions(
                                        options.Rows));
                        Console.WriteLine(output);
                    }
                    if (inspectionFailures.Count > 0
                        && (options.Tabular
                            || options.Tsv
                            || options.Jsonl
                            || options.NameOnly))
                    {
                        WriteIncompleteComparisonDiagnostic(
                            inspectionFailures);
                    }
                    return inspectionFailures.Count > 0 ? 1 : 0;
                }

                if (SelectsComplexityContext(options))
                {
                    ImplementationDiffResult result = queryResults.Get(
                        ImplementationComparisonQuery.Definition);
                    ComplexityContextView view =
                        DiffOutputFormatter.BuildComplexityContextView(
                            inputs.Name,
                            result.Complexity,
                            inputs.FromVersion,
                            inputs.ToVersion);
                    if (options.Tabular || options.Tsv || options.Jsonl)
                    {
                        OutputFormatter.WriteProjectedTable(
                            Console.Out,
                            !options.NoHeader,
                            options.Tsv,
                            options.Jsonl,
                            options.Columns,
                            options.Fields,
                            (writer, formatter, writerOptions) =>
                                MarkoutSerializer.Serialize(
                                    view,
                                    writer,
                                    formatter,
                                    DiffViewContext.Default,
                                    writerOptions),
                            options.Rows);
                    }
                    else
                    {
                        Console.WriteLine(
                            DiffOutputFormatter.RenderComplexityContextView(
                                view,
                                OutputFormatter.CreateWindowedOptions(
                                    options.Rows)));
                    }
                    return result.Complexity.IsAvailable ? 0 : 1;
                }
                if (SelectsStructuralContext(options))
                {
                    ImplementationDiffResult result = queryResults.Get(
                        ImplementationComparisonQuery.Definition);
                    StructuralContextView view =
                        DiffOutputFormatter.BuildStructuralContextView(
                            inputs.Name,
                            result.Complexity,
                            inputs.FromVersion,
                            inputs.ToVersion);
                    if (options.Tabular || options.Tsv || options.Jsonl)
                    {
                        OutputFormatter.WriteProjectedTable(
                            Console.Out,
                            !options.NoHeader,
                            options.Tsv,
                            options.Jsonl,
                            options.Columns,
                            options.Fields,
                            (writer, formatter, writerOptions) =>
                                MarkoutSerializer.Serialize(
                                    view,
                                    writer,
                                    formatter,
                                    DiffViewContext.Default,
                                    writerOptions),
                            options.Rows);
                    }
                    else
                    {
                        Console.WriteLine(
                            DiffOutputFormatter.RenderStructuralContextView(
                                view,
                                OutputFormatter.CreateWindowedOptions(
                                    options.Rows)));
                    }
                    return result.Complexity.IsAvailable ? 0 : 1;
                }

                if (SelectsImplementationDiff(options) && !SelectsAnalysisDiff(options))
                {
                    bool workspaceIncomplete =
                        workspaceImplementation is not null
                        && DiffOutputFormatter.IsIncomplete(
                            workspaceImplementation);
                    ImplementationDiffView view;
                    if (workspaceImplementation is not null)
                    {
                        view =
                            DiffOutputFormatter
                                .BuildWorkspaceImplementationDiffView(
                                    inputs.Name,
                                    workspaceTarget!.Display,
                                    workspaceImplementation,
                                    inputs.FromVersion,
                                    inputs.ToVersion);
                    }
                    else
                    {
                        var implementation =
                            await BuildImplementationDiffWithSourceAsync(
                                queryResults.Get(
                                    ImplementationComparisonQuery.Definition),
                                inputs.FromPaths,
                                inputs.ToPaths,
                                options,
                                context.HttpClient,
                                logger,
                                inputs.FromSurface,
                                inputs.ToSurface,
                                inputs.From.AssemblySet.Assemblies,
                                inputs.To.AssemblySet.Assemblies);
                        view = DiffOutputFormatter.BuildImplementationDiffView(
                            inputs.Name,
                            implementation.Local,
                            inputs.FromVersion,
                            inputs.ToVersion,
                            implementation.SelectedSource?.Content);
                    }
                    if (options.Tabular)
                    {
                        OutputFormatter.WriteProjectedTable(Console.Out, !options.NoHeader, options.Tsv, options.Jsonl,
                            options.Columns, options.Fields,
                            (writer, formatter, writerOptions) =>
                                MarkoutSerializer.Serialize(view, writer, formatter, DiffViewContext.Default, writerOptions),
                            options.Rows);
                    }
                    else
                    {
                        var output =
                            inspectionFailures.Count == 0
                                || options.NameOnly
                                ? DiffOutputFormatter.RenderImplementationDiffView(
                                    view,
                                    OutputFormatter.CreateWindowedOptions(
                                        options.Rows))
                                : DiffOutputFormatter.RenderDocumentView(
                                    DiffOutputFormatter.BuildDocumentView(
                                        inputs.Name,
                                        inputs.FromVersion,
                                        inputs.ToVersion,
                                        changes: null,
                                        analysisDiff: null,
                                        implementationDiff: view,
                                        findingTransitions: null,
                                        inspectionFailures),
                                    OutputFormatter.CreateWindowedOptions(
                                        options.Rows));
                        Console.WriteLine(output);
                    }
                    if (options.Tabular || options.NameOnly)
                    {
                        WriteIncompleteComparisonDiagnostic(
                            inspectionFailures);
                    }
                    return inspectionFailures.Count > 0
                        || workspaceIncomplete
                            ? 1
                            : 0;
                }

                if (SelectsAnalysisDiff(options))
                {
                    bool analysisIncomplete =
                        workspaceAnalysisFailure is not null;
                    AnalysisDiffView view;
                    if (workspaceAnalysisFailure is not null)
                    {
                        view =
                            DiffOutputFormatter.BuildAnalysisDiffFailureView(
                                inputs.Name,
                                inputs.FromVersion,
                                inputs.ToVersion,
                                workspaceAnalysisFailure);
                    }
                    else
                    {
                        var analysis = workspaceAnalysis is not null
                            ? BuildAnalysisDiff(workspaceAnalysis, options)
                            : BuildAnalysisDiff(
                                queryResults.Get(
                                    BodySignalComparisonQuery.Definition),
                                options);
                        view = DiffOutputFormatter.BuildAnalysisDiffView(
                            inputs.Name,
                            analysis.Rows,
                            analysis.Summary,
                            inputs.FromVersion,
                            inputs.ToVersion,
                            decorateMember: !options.Jsonl);
                    }
                    if (options.Tabular)
                    {
                        OutputFormatter.WriteProjectedTable(Console.Out, !options.NoHeader, options.Tsv, options.Jsonl,
                            options.Columns, options.Fields,
                            (writer, formatter, writerOptions) =>
                                MarkoutSerializer.Serialize(view, writer, formatter, DiffViewContext.Default, writerOptions),
                            options.Rows);
                    }
                    else
                    {
                        var output =
                            inspectionFailures.Count == 0
                                || options.NameOnly
                                ? DiffOutputFormatter.RenderAnalysisDiffView(
                                    view,
                                    OutputFormatter.CreateWindowedOptions(
                                        options.Rows))
                                : DiffOutputFormatter.RenderDocumentView(
                                    DiffOutputFormatter.BuildDocumentView(
                                        inputs.Name,
                                        inputs.FromVersion,
                                        inputs.ToVersion,
                                        changes: null,
                                        analysisDiff: view,
                                        implementationDiff: null,
                                        findingTransitions: null,
                                        inspectionFailures),
                                    OutputFormatter.CreateWindowedOptions(
                                        options.Rows));
                        Console.WriteLine(output);
                    }
                    if (options.Tabular
                        || options.NameOnly)
                    {
                        WriteIncompleteComparisonDiagnostic(
                            inspectionFailures);
                    }
                    return inspectionFailures.Count > 0
                        || analysisIncomplete
                            ? 1
                            : 0;
                }

                var diff = BuildApiDiff(
                    queryResults.Get(ApiComparisonQuery.Definition),
                    inputs.FromSurface,
                    inputs.ToSurface,
                    options);

                if (options.Tabular)
                {
                    var typeDiffs = ApplyFilters(diff, options);
                    if (SelectsDetailedChanges(options))
                    {
                        var view = DiffOutputFormatter.BuildDetailedChangesView(inputs.Name, typeDiffs, inputs.FromVersion, inputs.ToVersion);
                        OutputFormatter.WriteProjectedTable(Console.Out, !options.NoHeader, options.Tsv, options.Jsonl,
                            options.Columns, options.Fields,
                            (writer, formatter, writerOptions) =>
                                MarkoutSerializer.Serialize(view, writer, formatter, DiffViewContext.Default, writerOptions),
                            options.Rows);
                    }
                    else
                    {
                        var view = DiffOutputFormatter.BuildTableView(inputs.Name, typeDiffs, inputs.FromVersion, inputs.ToVersion);
                        OutputFormatter.WriteProjectedTable(Console.Out, !options.NoHeader, options.Tsv, options.Jsonl,
                            options.Columns, options.Fields,
                            (writer, formatter, writerOptions) =>
                                MarkoutSerializer.Serialize(view, writer, formatter, DiffViewContext.Default, writerOptions),
                            options.Rows);
                    }

                    WriteIncompleteComparisonDiagnostic(
                        diff.InspectionFailures);
                }
                else
                {
                    var output = RenderDiff(inputs.Name, diff, inputs.FromVersion, inputs.ToVersion, options);
                    Console.WriteLine(output);
                    if (options.NameOnly)
                    {
                        WriteIncompleteComparisonDiagnostic(
                            diff.InspectionFailures);
                    }
                }

                return diff.InspectionFailures.Count > 0 ? 1 : 0;
            }
            finally
            {
                inputs.Dispose();
            }
        }
        catch (Exception ex)
        {
            CommandError.Write(ex);
            return 1;
        }
    }

    sealed record DiffInputs(
        ApiSurfaceEndpoint From,
        ApiSurfaceEndpoint To,
        string FromVersion,
        string ToVersion,
        string Name) : IDisposable
    {
        public ApiSurface FromSurface => From.Surface;
        public ApiSurface ToSurface => To.Surface;
        public IReadOnlyList<string> FromPaths => From.Paths;
        public IReadOnlyList<string> ToPaths => To.Paths;

        public void Dispose()
        {
            From.Dispose();
            To.Dispose();
        }
    }

    static bool UsesSharedLibraryApiDiff(DiffInputs inputs, DiffOptions options)
        => inputs.From.AssemblySet.Assemblies.Count == 1
            && inputs.To.AssemblySet.Assemblies.Count == 1
            && options.MemberFilter.Count == 0
            && !SelectsAnalysisDiff(options)
            && !SelectsImplementationDiff(options)
            && !SelectsComplexityContext(options)
            && !SelectsFindingTransitions(options)
            && (options.IncludeSections is null
                || options.IncludeSections.SetEquals([DiffSections.Changes.Name]));

    private static async Task<(DiffInputs? inputs, string? error)>
        ExecutePackageDiffAsync(DiffOptions options, VerboseLogger logger, HttpClient httpClient)
    {
        var (packageName, fromVersion, toVersion) = ParseVersionRange(options.PackageVersionRange!);
        if (packageName == null || fromVersion == null || toVersion == null)
        {
            return (null, "Invalid version range. Use format: Package@v1..v2");
        }

        logger.Log($"Comparing {packageName} v{fromVersion} -> v{toVersion}");

        var from = await ResolveDiffEndpointAsync(
            httpClient,
            new AssemblySetRequest
            {
                Packages = [$"{packageName}@{fromVersion}"],
                Tfm = options.Tfm,
                SourceOptions = options.SourceOptions,
                TempDirPrefix = "inspect-diff",
                IncludePackageRuntimeAssemblies = true,
            },
            options.IncludeAll,
            logger);
        if (from.error is not null)
            return (null, $"Error resolving v{fromVersion}: {from.error}");
        (ApiSurfaceEndpoint? endpoint, string? error, bool assembliesResolved) to;
        try
        {
            to = await ResolveDiffEndpointAsync(
                httpClient,
                new AssemblySetRequest
                {
                    Packages = [$"{packageName}@{toVersion}"],
                    Tfm = options.Tfm,
                    SourceOptions = options.SourceOptions,
                    TempDirPrefix = "inspect-diff",
                    IncludePackageRuntimeAssemblies = true,
                },
                options.IncludeAll,
                logger);
        }
        catch
        {
            from.endpoint!.Dispose();
            throw;
        }
        if (to.error is not null)
        {
            from.endpoint!.Dispose();
            return (null, $"Error resolving v{toVersion}: {to.error}");
        }

        return (new DiffInputs(
            from.endpoint!, to.endpoint!, fromVersion, toVersion, packageName), null);
    }

    private static async Task<(DiffInputs? inputs, string? error)>
        ExecutePlatformDiffAsync(DiffOptions options, VerboseLogger logger, HttpClient httpClient)
    {
        var (assemblyName, fromVersion, toVersion) = ParseVersionRange(options.PlatformVersionRange!);
        if (assemblyName == null || fromVersion == null || toVersion == null)
        {
            return (null, "Invalid version range. Use format: Library@v1..v2");
        }

        var framework = options.Framework ?? "runtime";
        logger.Log($"Comparing {assemblyName} in {framework} v{fromVersion} -> v{toVersion}");

        var from = await ResolveDiffEndpointAsync(
            httpClient,
            new AssemblySetRequest
            {
                PlatformAssemblies = [assemblyName],
                PlatformAssemblyFrameworkHint = $"{framework}@{fromVersion}",
                TempDirPrefix = "inspect-diff",
            },
            options.IncludeAll,
            logger);
        if (from.error is not null)
            return (null, from.assembliesResolved
                ? "Failed to extract API surface from one or both versions."
                : $"Error resolving v{fromVersion}: {AsEndpointError(from.error)}");

        (ApiSurfaceEndpoint? endpoint, string? error, bool assembliesResolved) to;
        try
        {
            to = await ResolveDiffEndpointAsync(
                httpClient,
                new AssemblySetRequest
                {
                    PlatformAssemblies = [assemblyName],
                    PlatformAssemblyFrameworkHint = $"{framework}@{toVersion}",
                    TempDirPrefix = "inspect-diff",
                },
                options.IncludeAll,
                logger);
        }
        catch
        {
            from.endpoint!.Dispose();
            throw;
        }
        if (to.error is not null)
        {
            from.endpoint!.Dispose();
            return (null, to.assembliesResolved
                ? "Failed to extract API surface from one or both versions."
                : $"Error resolving v{toVersion}: {AsEndpointError(to.error)}");
        }

        return (new DiffInputs(
            from.endpoint!, to.endpoint!, fromVersion, toVersion, assemblyName), null);
    }

    private static async Task<(DiffInputs? inputs, string? error)> ExecuteLibraryDiffAsync(
        DiffOptions options,
        VerboseLogger logger,
        HttpClient httpClient)
    {
        var (fromPath, toPath) = ParsePathRange(options.LibraryVersionRange!);
        if (fromPath is null || toPath is null)
            return (null, "Invalid library range. Use format: old/Foo.dll..new/Foo.dll");

        var from = await ResolveDiffEndpointAsync(
            httpClient,
            new AssemblySetRequest
            {
                Assemblies = [fromPath],
                TempDirPrefix = "inspect-diff",
            },
            options.IncludeAll,
            logger);
        if (from.error is not null)
            return (null, from.assembliesResolved
                ? "Failed to extract API surface from one or both libraries."
                : $"File not found: {fromPath}");

        (ApiSurfaceEndpoint? endpoint, string? error, bool assembliesResolved) to;
        try
        {
            to = await ResolveDiffEndpointAsync(
                httpClient,
                new AssemblySetRequest
                {
                    Assemblies = [toPath],
                    TempDirPrefix = "inspect-diff",
                },
                options.IncludeAll,
                logger);
        }
        catch
        {
            from.endpoint!.Dispose();
            throw;
        }
        if (to.error is not null)
        {
            from.endpoint!.Dispose();
            return (null, to.assembliesResolved
                ? "Failed to extract API surface from one or both libraries."
                : $"File not found: {toPath}");
        }

        var name = Path.GetFileNameWithoutExtension(toPath);
        return (new DiffInputs(
            from.endpoint!, to.endpoint!,
            Path.GetFileName(fromPath), Path.GetFileName(toPath), name), null);
    }

    private static Task<(ApiSurfaceEndpoint? endpoint, string? error, bool assembliesResolved)> ResolveDiffEndpointAsync(
        HttpClient httpClient,
        AssemblySetRequest request,
        bool includeAll,
        VerboseLogger logger)
        => ApiSurfaceEndpointResolver.ResolveAsync(
            httpClient,
            request,
            includeAll,
            logger,
            deferSurfaceProjection: true);

    internal static string AsEndpointError(string error)
    {
        const string SkippingSuffix = ", skipping.";
        return error.EndsWith(SkippingSuffix, StringComparison.Ordinal)
            ? error[..^SkippingSuffix.Length]
            : error;
    }

    private static (string? fromPath, string? toPath) ParsePathRange(string input)
    {
        int dotDotIndex = input.IndexOf("..", StringComparison.Ordinal);
        if (dotDotIndex <= 0 || dotDotIndex + 2 >= input.Length)
            return (null, null);
        return (input[..dotDotIndex], input[(dotDotIndex + 2)..]);
    }

    private static bool SelectsAnalysisDiff(DiffOptions options)
        => options.AllocRegressionsOnly
            || options.IncludeSections?.Contains(DiffSections.AnalysisDiff.Name) == true;

    private static bool SelectsFindingTransitions(DiffOptions options)
        => options.Finding is not null
            || options.IncludeSections?.Contains(DiffSections.FindingTransitions.Name) == true;

    private static bool TryResolveFindingDescriptor(
        DiffOptions options,
        out string descriptor,
        out string? error)
    {
        descriptor = options.Finding
            ?? (options.MemberFilter.Count > 0
                ? MetadataFindings.MemberDescriptor.Id
                : MetadataFindings.TypeDescriptor.Id);
        if (string.Equals(descriptor, MetadataFindings.TypeDescriptor.Id, StringComparison.OrdinalIgnoreCase))
        {
            descriptor = MetadataFindings.TypeDescriptor.Id;
            error = null;
            return true;
        }
        if (string.Equals(descriptor, MetadataFindings.MemberDescriptor.Id, StringComparison.OrdinalIgnoreCase))
        {
            descriptor = MetadataFindings.MemberDescriptor.Id;
            error = null;
            return true;
        }
        if (string.Equals(descriptor, MetadataFindings.AttributeDescriptor.Id, StringComparison.OrdinalIgnoreCase))
        {
            descriptor = MetadataFindings.AttributeDescriptor.Id;
            error = null;
            return true;
        }
        if (string.Equals(descriptor, AnalysisFindings.AllocationDescriptor.Id, StringComparison.OrdinalIgnoreCase))
        {
            descriptor = AnalysisFindings.AllocationDescriptor.Id;
            error = null;
            return true;
        }
        if (string.Equals(descriptor, AnalysisFindings.CallSiteDescriptor.Id, StringComparison.OrdinalIgnoreCase))
        {
            descriptor = AnalysisFindings.CallSiteDescriptor.Id;
            error = null;
            return true;
        }
        if (string.Equals(descriptor, AnalysisFindings.UnsafetyDescriptor.Id, StringComparison.OrdinalIgnoreCase))
        {
            descriptor = AnalysisFindings.UnsafetyDescriptor.Id;
            error = null;
            return true;
        }
        if (string.Equals(descriptor, CSharpFindings.LineDescriptor.Id, StringComparison.OrdinalIgnoreCase))
        {
            descriptor = CSharpFindings.LineDescriptor.Id;
            error = null;
            return true;
        }
        if (string.Equals(descriptor, IlFindings.OperationDescriptor.Id, StringComparison.OrdinalIgnoreCase))
        {
            descriptor = IlFindings.OperationDescriptor.Id;
            error = null;
            return true;
        }

        error = $"Unsupported Finding descriptor '{descriptor}'. Supported descriptors: api.type, api.member, api.attribute, analysis.allocation, analysis.call-site, analysis.unsafety, csharp.line, il.op.";
        return false;
    }

    private static string ResolveFindingDescriptor(DiffOptions options)
    {
        if (TryResolveFindingDescriptor(options, out var descriptor, out var error))
            return descriptor;

        throw new InvalidOperationException(
            error ?? "Finding descriptor resolution failed.");
    }

    private static IReadOnlyList<FindingTransitionRow> BuildSelectedFindingTransitions(
        DiffInputs inputs,
        DiffOptions options)
        => ResolveFindingDescriptor(options) switch
        {
            var descriptor when descriptor == AnalysisFindings.AllocationDescriptor.Id =>
                BuildAllocationFindingTransitions(
                    inputs.FromPaths,
                    inputs.ToPaths,
                    inputs.FromSurface,
                    inputs.ToSurface,
                    inputs.FromVersion,
                    inputs.ToVersion,
                    options),
            var descriptor when descriptor == AnalysisFindings.CallSiteDescriptor.Id =>
                BuildCallSiteFindingTransitions(
                    inputs.FromPaths,
                    inputs.ToPaths,
                    inputs.FromSurface,
                    inputs.ToSurface,
                    inputs.FromVersion,
                    inputs.ToVersion,
                    options),
            var descriptor when descriptor == AnalysisFindings.UnsafetyDescriptor.Id =>
                BuildUnsafetyFindingTransitions(
                    inputs.FromPaths,
                    inputs.ToPaths,
                    inputs.FromSurface,
                    inputs.ToSurface,
                    inputs.FromVersion,
                    inputs.ToVersion,
                    options),
            var descriptor when descriptor == CSharpFindings.LineDescriptor.Id =>
                BuildCSharpFindingTransitions(
                    inputs.FromPaths,
                    inputs.ToPaths,
                    inputs.FromSurface,
                    inputs.ToSurface,
                    inputs.FromVersion,
                    inputs.ToVersion,
                    options),
            var descriptor when descriptor == IlFindings.OperationDescriptor.Id =>
                BuildIlFindingTransitions(
                    inputs.FromPaths,
                    inputs.ToPaths,
                    inputs.FromSurface,
                    inputs.ToSurface,
                    inputs.FromVersion,
                    inputs.ToVersion,
                    options),
            _ => BuildFindingTransitions(
                inputs.FromSurface,
                inputs.ToSurface,
                inputs.FromVersion,
                inputs.ToVersion,
                options),
        };

    static bool IsMemberBodyFindingDescriptor(string descriptor)
        => descriptor == AnalysisFindings.AllocationDescriptor.Id
            || descriptor == AnalysisFindings.CallSiteDescriptor.Id
            || descriptor == AnalysisFindings.UnsafetyDescriptor.Id
            || descriptor == CSharpFindings.LineDescriptor.Id
            || descriptor == IlFindings.OperationDescriptor.Id;

    private static bool SelectsImplementationDiff(DiffOptions options)
        => options.IncludeSections?.Contains(DiffSections.ImplementationDiff.Name) == true;

    private static bool SelectsComplexityContext(DiffOptions options)
        => options.IncludeSections?.Contains(DiffSections.ComplexityContext.Name) == true;

    private static bool RequestsCompleteImplementationDiff(
        DiffOptions options)
    {
        if (!options.JsonOutput && !options.EnvelopeOutput)
            return false;

        bool selectsOnlyImplementationDiff;
        if (options.IncludeSections is not null)
        {
            selectsOnlyImplementationDiff = options.IncludeSections.SetEquals(
                    [DiffSections.ImplementationDiff.Name])
                && options.ExactIncludeSections?.SetEquals(
                    [DiffSections.ImplementationDiff.Name]) == true;
        }
        else
        {
            selectsOnlyImplementationDiff =
                options.Select is { Length: 1 }
                && string.Equals(
                    options.Select[0],
                    DiffSections.ImplementationDiff.Name,
                    StringComparison.OrdinalIgnoreCase);
        }

        if (!selectsOnlyImplementationDiff)
            return false;

        // Exact non-Library JSON remains the existing rendered projection.
        return options.EnvelopeOutput
            || !string.IsNullOrEmpty(options.LibraryVersionRange);
    }

    private static bool HasIncompatibleImplementationTransportProjection(
        DiffOptions options)
        => options.Breaking
            || options.Additive
            || options.SelectDefault
            || options.Columns is not null
            || options.Fields is not null
            || options.Rows is not null
            || options.Tabular
            || options.Tsv
            || options.Jsonl
            || options.NoHeader
            || options.NameOnly
            || options.Tree
            || options.VerbosityExplicitlySet
            || options.Discover is not null
            || options.Schema
            || options.Finding is not null
            || options.IncludePdbSource
            || options.SourceRepositories.Length > 0
            || options.ChangedOnly
            || options.AllocRegressionsOnly
            || options.Legend
            || options.EnvelopeOutput && options.JsonOutput;

    private static bool SelectsStructuralContext(DiffOptions options)
        => options.IncludeSections?.Contains(DiffSections.StructuralContext.Name) == true;

    private static bool SelectsDetailedChanges(DiffOptions options)
        => options.IncludeSections?.Contains(DiffSections.Changes.Name) == true;

    internal static CompiledInspectionPlan<DiffQueryContext> GetRequestedQueryPlan(
        DiffSectionCatalog catalog,
        DiffOptions options,
        bool workspaceImplementation = false)
    {
        bool writesDocument =
            options.JsonOutput
            || options.IncludeSections is { Count: > 1 };
        HashSet<string>? querySections = options.IncludeSections;

        if (writesDocument)
        {
            if (querySections is null && options.AllocRegressionsOnly)
            {
                querySections = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    DiffSections.AnalysisDiff.Name,
                };
            }
        }
        else if (SelectsFindingTransitions(options))
        {
            querySections = new HashSet<string>(
                StringComparer.OrdinalIgnoreCase)
            {
                DiffSections.FindingTransitions.Name,
            };
        }
        else if (SelectsComplexityContext(options))
        {
            querySections = new HashSet<string>(
                StringComparer.OrdinalIgnoreCase)
            {
                DiffSections.ComplexityContext.Name,
            };
        }
        else if (SelectsStructuralContext(options))
        {
            querySections = new HashSet<string>(
                StringComparer.OrdinalIgnoreCase)
            {
                DiffSections.StructuralContext.Name,
            };
        }
        else if (SelectsImplementationDiff(options)
            && !SelectsAnalysisDiff(options))
        {
            querySections = new HashSet<string>(
                StringComparer.OrdinalIgnoreCase)
            {
                DiffSections.ImplementationDiff.Name,
            };
        }
        else if (SelectsAnalysisDiff(options))
        {
            querySections = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                DiffSections.AnalysisDiff.Name,
            };
        }

        CompiledInspectionPlan<DiffQueryContext> plan =
            catalog.Lens.Plan(
                Verbosity.Minimal,
                querySections);
        if (!workspaceImplementation)
            return plan;

        ImmutableArray<InspectionQueryDefinition> removedQueries =
            SelectsAnalysisDiff(options)
                ? [
                    ImplementationComparisonQuery.Definition,
                    BodySignalComparisonQuery.Definition,
                ]
                : [ImplementationComparisonQuery.Definition];
        ImmutableArray<InspectionQueryDefinition> queries =
            [
                .. plan.RequestedQueries.Where(
                    query => !removedQueries.Contains(query)),
            ];
        ImmutableArray<SectionQueryDemand> demands =
            [
                .. plan.SectionDemand.Where(
                    demand => !removedQueries.Contains(
                        demand.Query)),
            ];
        var sectionPlan = new SectionQueryPlan(
            queries,
            demands);
        return new(
            sectionPlan,
            plan.HostDemand,
            queries,
            catalog.QueryCatalog.Plan(queries));
    }

    private static async Task<bool> WriteSelectedDocumentAsync(
        DiffInputs inputs,
        DiffOptions options,
        InspectionQueryResults queryResults,
        HttpClient httpClient,
        VerboseLogger logger,
        IReadOnlyList<ApiDiffInspectionFailure>
            inspectionFailures,
        WorkspaceImplementationComparisonResult?
            workspaceImplementation = null,
        string? workspaceTargetDisplay = null,
        ResearchComparison? workspaceAnalysis = null,
        string? workspaceAnalysisFailure = null)
    {
        var selected = options.IncludeSections
            ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                options.AllocRegressionsOnly
                    ? DiffSections.AnalysisDiff.Name
                    : DiffSections.Changes.Name
            };

        ApiDiff? changesDiff = null;
        DiffDetailedChangesView? changesView = null;
        bool changesIncomplete = false;
        if (selected.Contains(DiffSections.Changes.Name))
        {
            try
            {
                changesDiff = BuildApiDiff(
                    queryResults.Get(ApiComparisonQuery.Definition),
                    inputs.FromSurface,
                    inputs.ToSurface,
                    options);
            }
            catch (InvalidOperationException exception)
                when (workspaceImplementation is not null
                    && options.MemberFilter.Count > 0)
            {
                changesIncomplete = true;
                changesView =
                    DiffOutputFormatter.BuildDetailedChangesFailureView(
                        inputs.Name,
                        inputs.FromVersion,
                        inputs.ToVersion,
                        exception.Message);
            }
            if (changesDiff is not null)
            {
                changesView = DiffOutputFormatter.BuildDetailedChangesView(
                    inputs.Name,
                    ApplyFilters(changesDiff, options),
                    inputs.FromVersion,
                    inputs.ToVersion);
            }
        }

        AnalysisDiffView? analysisView = null;
        bool analysisIncomplete = false;
        if (selected.Contains(DiffSections.AnalysisDiff.Name))
        {
            if (workspaceAnalysisFailure is not null)
            {
                analysisIncomplete = true;
                analysisView =
                    DiffOutputFormatter.BuildAnalysisDiffFailureView(
                        inputs.Name,
                        inputs.FromVersion,
                        inputs.ToVersion,
                        workspaceAnalysisFailure);
            }
            else
            {
                var analysis = workspaceAnalysis is not null
                    ? BuildAnalysisDiff(workspaceAnalysis, options)
                    : BuildAnalysisDiff(
                        queryResults.Get(
                            BodySignalComparisonQuery.Definition),
                        options);
                analysisView = DiffOutputFormatter.BuildAnalysisDiffView(
                    inputs.Name,
                    analysis.Rows,
                    analysis.Summary,
                    inputs.FromVersion,
                    inputs.ToVersion,
                    decorateMember: false);
            }
        }

        ImplementationDiffView? implementationView = null;
        if (selected.Contains(DiffSections.ImplementationDiff.Name))
        {
            if (workspaceImplementation is not null)
            {
                implementationView =
                    DiffOutputFormatter.BuildWorkspaceImplementationDiffView(
                        inputs.Name,
                        workspaceTargetDisplay!,
                        workspaceImplementation,
                        inputs.FromVersion,
                        inputs.ToVersion);
            }
            else
            {
                var implementation =
                    await BuildImplementationDiffWithSourceAsync(
                        queryResults.Get(
                            ImplementationComparisonQuery.Definition),
                        inputs.FromPaths,
                        inputs.ToPaths,
                        options,
                        httpClient,
                        logger,
                        inputs.FromSurface,
                        inputs.ToSurface,
                        inputs.From.AssemblySet.Assemblies,
                        inputs.To.AssemblySet.Assemblies);
                implementationView =
                    DiffOutputFormatter.BuildImplementationDiffView(
                        inputs.Name,
                        implementation.Local,
                        inputs.FromVersion,
                        inputs.ToVersion,
                        implementation.SelectedSource?.Content);
            }
        }

        ComplexityContextView? complexityContextView = null;
        if (selected.Contains(DiffSections.ComplexityContext.Name))
        {
            ImplementationDiffResult result = queryResults.Get(
                ImplementationComparisonQuery.Definition);
            complexityContextView =
                DiffOutputFormatter.BuildComplexityContextView(
                    inputs.Name,
                    result.Complexity,
                    inputs.FromVersion,
                    inputs.ToVersion);
        }

        StructuralContextView? structuralContextView = null;
        if (selected.Contains(DiffSections.StructuralContext.Name))
        {
            ImplementationDiffResult result = queryResults.Get(
                    ImplementationComparisonQuery.Definition);
            structuralContextView =
                    DiffOutputFormatter.BuildStructuralContextView(
                        inputs.Name,
                        result.Complexity,
                        inputs.FromVersion,
                        inputs.ToVersion);
        }

        FindingTransitionsView? findingTransitionsView = null;
        if (selected.Contains(DiffSections.FindingTransitions.Name))
        {
            var rows = BuildSelectedFindingTransitions(inputs, options);
            findingTransitionsView = DiffOutputFormatter.BuildFindingTransitionsView(
                inputs.Name,
                rows,
                inputs.FromVersion,
                inputs.ToVersion);
        }

        var view = DiffOutputFormatter.BuildDocumentView(
            inputs.Name,
            inputs.FromVersion,
            inputs.ToVersion,
            changesView,
            analysisView,
            implementationView,
            findingTransitionsView,
            inspectionFailures,
            complexityContextView,
            structuralContextView);

        bool workspaceIncomplete =
            workspaceImplementation is not null
            && DiffOutputFormatter.IsIncomplete(
                workspaceImplementation);
        if (options.JsonOutput)
        {
            Console.WriteLine(JsonSerializer.Serialize(view, DiffJsonContext.Default.DiffDocumentView));
            return inspectionFailures.Count > 0
                || workspaceIncomplete
                || changesIncomplete
                || analysisIncomplete;
        }

        Console.WriteLine(DiffOutputFormatter.RenderDocumentView(
            view, OutputFormatter.CreateWindowedOptions(options.Rows)));
        return inspectionFailures.Count > 0
            || workspaceIncomplete
            || changesIncomplete
            || analysisIncomplete;
    }

    internal sealed record AnalysisDiffResult(List<AnalysisDiffRow> Rows, string Summary);

    // A diff row plus the metadata used to rank and classify it. Magnitude is the
    // absolute numeric movement (for ordering); Direction is +1 regression (more
    // cost), -1 improvement (less cost), 0 neutral; InBoth is true when the member
    // is present in both versions (an in-place change vs an added/removed member).
    internal sealed record RankedAnalysisRow(AnalysisDiffRow Row, int Magnitude, int Direction, bool InBoth, bool InLoop = false);

    internal static AnalysisDiffResult BuildAnalysisDiff(
        IReadOnlyList<string> fromPaths,
        IReadOnlyList<string> toPaths,
        DiffOptions options,
        ApiSurface? fromSurface = null,
        ApiSurface? toSurface = null)
    {
        var comparisonInput = CreateBodySignalComparisonInput(
            fromPaths,
            toPaths,
            options,
            fromSurface,
            toSurface);
        return BuildAnalysisDiff(
            BodySignalComparisonQuery.Execute(comparisonInput),
            options);
    }

    internal static AnalysisDiffResult BuildAnalysisDiff(
        BodySignalComparisonResult result,
        DiffOptions options)
        => BuildAnalysisDiff(
            RequireBodySignalComparison(
                result,
                options,
                DiffSections.AnalysisDiff.Name),
            options);

    /// <summary>
    /// Returns the completed body-signal comparison, or throws one explicit
    /// message for every typed failure. A targeted comparison whose selection
    /// selected no endpoint on either side is a failure, never an empty
    /// comparison.
    /// </summary>
    internal static ResearchComparison RequireBodySignalComparison(
        BodySignalComparisonResult result,
        DiffOptions options,
        string sectionName)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(options);
        switch (result)
        {
            case BodySignalComparisonResult.Compared compared:
                if (compared.Resolution is { } resolution)
                    RequireSelectedEndpoints(resolution, compared.Correspondences, options);
                return compared.Comparison;
            case BodySignalComparisonResult.TargetFailed failed:
                throw new InvalidOperationException(
                    (failed.Failures.All(static failure => failure.Kind
                            == BodySignalTargetFailureKind.EndpointWithoutMethodAddress)
                        ? $"{sectionName} --member requires a method-like target: "
                        : $"{sectionName} --member target selected no comparable method: ")
                    + string.Join(
                        " ",
                        failed.Failures.Select(static failure => failure.Summary)));
            case BodySignalComparisonResult.PlanningRejected rejected:
                throw new InvalidOperationException(
                    $"{sectionName} --member target planning was rejected "
                    + $"({rejected.Rejection.Kind}): {rejected.Rejection.Summary}");
            case BodySignalComparisonResult.AdmissionRejected rejected:
                throw new InvalidOperationException(
                    $"{sectionName} body-signal inputs were not admitted "
                    + $"({rejected.Rejection.Kind}): {rejected.Rejection.Summary}");
            case BodySignalComparisonResult.ProjectionRejected rejected:
                throw new InvalidOperationException(
                    $"{sectionName} body-signal inputs could not be associated "
                    + $"with Research admission ({rejected.Reason}).");
            case BodySignalComparisonResult.PopulationRejected rejected:
                throw new InvalidOperationException(
                    $"{sectionName} body-signal inputs were rejected "
                    + $"({rejected.Rejection.Kind}).");
            default:
                throw new InvalidOperationException(
                    "Unknown body-signal comparison result.");
        }
    }

    // Every member selection must select an endpoint on at least one side.
    // Scopes follow selection-occurrence order, which is --member order.
    static void RequireSelectedEndpoints(
        ResearchTargetResolution resolution,
        ImmutableArray<ResearchTargetCorrespondenceOutcome> correspondences,
        DiffOptions options)
    {
        string[] rawTargets = [.. options.MemberFilter];
        for (int index = 0; index < resolution.Scopes.Length; index++)
        {
            ResearchTargetScope scope = resolution.Scopes[index];
            ResearchTargetCorrespondenceOutcome[] outcomes =
            [
                .. correspondences.Where(outcome =>
                    ReferenceEquals(outcome.Scope, scope.Id)),
            ];
            if (outcomes.Any(static outcome =>
                    outcome is ResearchTargetCorrespondenceOutcome.Paired
                        or ResearchTargetCorrespondenceOutcome.BeforeOnly
                        or ResearchTargetCorrespondenceOutcome.AfterOnly))
            {
                continue;
            }

            string rawTarget = index < rawTargets.Length
                ? rawTargets[index]
                : scope.Selector.RequestedText;
            string? diagnostic = outcomes
                .OfType<ResearchTargetCorrespondenceOutcome.Absent>()
                .SelectMany(static absent => new[]
                {
                    absent.BeforeAbsence.NotFoundAttempt,
                    absent.AfterAbsence.NotFoundAttempt,
                })
                .Select(static attempt =>
                    (attempt?.Outcome as ResearchTargetOutcome.NotFound)
                        ?.MetadataDiagnostic?.Message)
                .FirstOrDefault(static message => message is not null);
            throw new InvalidOperationException(
                diagnostic
                ?? $"Member target '{rawTarget}' did not resolve in either diff input.");
        }
    }

    internal static AnalysisDiffResult BuildAnalysisDiff(
        ResearchComparison research,
        DiffOptions options)
    {
        ArgumentNullException.ThrowIfNull(research);
        ArgumentNullException.ThrowIfNull(options);

        var ranked = research.Changes
            .Where(change => change.Category == ResearchChangeCategory.BodySignal
                && change.Descriptor.Id.StartsWith("analysis.", StringComparison.Ordinal)
                && change.Signal is { Length: > 0 })
            .Select(change => new RankedAnalysisRow(
                new AnalysisDiffRow(
                    change.Subject.Display,
                    change.Signal!,
                    change.OldValue ?? "",
                    change.NewValue ?? "",
                    change.Delta ?? "changed",
                    change.Shape,
                    change.Detail),
                change.Magnitude ?? 1,
                change.DirectionScore,
                change.SubjectInBoth,
                change.InLoop))
            .ToList();

        return RankAnalysisRows(ranked, options.ChangedOnly, options.AllocRegressionsOnly);
    }

    private static BodySignalComparisonInput CreateBodySignalComparisonInput(
        DiffInputs inputs,
        DiffOptions options)
        => CreateBodySignalComparisonInput(
            inputs.FromPaths,
            inputs.ToPaths,
            options,
            inputs.FromSurface,
            inputs.ToSurface);

    internal static BodySignalComparisonInput CreateBodySignalComparisonInput(
        IReadOnlyList<string> fromPaths,
        IReadOnlyList<string> toPaths,
        DiffOptions options,
        ApiSurface? fromSurface = null,
        ApiSurface? toSurface = null,
        IReadOnlyList<FindingDescriptor>? retainedComparisons = null)
    {
        IReadOnlyList<ComparisonMemberSelection>? selections =
            options.MemberFilter.Count == 0
                ? null
                : ResolveComparisonMemberSelections(
                    fromSurface ?? AssemblySetSurfaceBuilder.Build(fromPaths, includeAll: options.IncludeAll) ?? new ApiSurface(),
                    toSurface ?? AssemblySetSurfaceBuilder.Build(toPaths, includeAll: options.IncludeAll) ?? new ApiSurface(),
                    options);
        return new BodySignalComparisonInput(
            [.. fromPaths.Select(CreateBodySignalBinding)],
            [.. toPaths.Select(CreateBodySignalBinding)],
            options.TypeFilter,
            selections,
            retainedComparisons);
    }

    static BodySignalComparisonBinding CreateBodySignalBinding(string path)
    {
        var assembly = ResolvedAssemblyReference.CreateFromPath(
            path,
            AssemblyResolutionProvenance.Local(
                "diff body-signal comparison"));
        ILInspector.Analysis.LibraryBodyAnalysisExecution execution =
            MethodBodyInspectionSession.Open(assembly).AnalysisExecution;
        return new(
            assembly,
            MetadataSource.DefaultAssemblyReferenceResolver(path),
            new BodySignalAnalysisInput(
                execution.Allocations,
                execution.Safety,
                execution.CallGraph,
                execution.Optimization));
    }

    /// <summary>
    /// Lowers each --member target to typed selection intent: the Metadata
    /// type definition and the member selector. Research resolves the member.
    /// </summary>
    static IReadOnlyList<ComparisonMemberSelection> ResolveComparisonMemberSelections(
        ApiSurface fromSurface,
        ApiSurface toSurface,
        DiffOptions options)
    {
        List<ComparisonMemberSelection> selections = [];
        foreach (string rawTarget in options.MemberFilter)
        {
            ParsedDiffMemberTarget parsed = ParseDiffMemberTarget(
                rawTarget,
                fromSurface,
                toSurface,
                options.TypeFilter);
            WorkspaceImplementationTypeSelection type =
                ResolveWorkspaceImplementationTypeName(
                    fromSurface,
                    toSurface,
                    parsed.TypeName)
                ?? throw new InvalidOperationException(
                    $"Member target '{rawTarget}' names type "
                    + $"'{parsed.TypeName}', which has no single Metadata "
                    + "type definition in the diff inputs.");
            selections.Add(new(type.DefinitionName, parsed.Selector));
        }
        return selections;
    }

    internal static ImplementationDiffResult BuildImplementationDiff(
        IReadOnlyList<string> fromPaths,
        IReadOnlyList<string> toPaths,
        DiffOptions options,
        ApiSurface? fromSurface = null,
        ApiSurface? toSurface = null)
        => ImplementationComparisonQuery.Execute(
            CreateImplementationComparisonInput(
                fromPaths,
                toPaths,
                options,
                fromSurface,
                toSurface));

    private static ImplementationComparisonInput
        CreateImplementationComparisonInput(
            DiffInputs inputs,
            DiffOptions options)
        => CreateImplementationComparisonInput(
            inputs.FromPaths,
            inputs.ToPaths,
            options,
            inputs.FromSurface,
            inputs.ToSurface);

    internal static ImplementationComparisonInput
        CreateImplementationComparisonInput(
            IReadOnlyList<string> fromPaths,
            IReadOnlyList<string> toPaths,
            DiffOptions options,
            ApiSurface? fromSurface = null,
            ApiSurface? toSurface = null)
    {
        ImplementationAssemblyInput[] oldAssemblies =
        [
            .. fromPaths.Select(CreateImplementationAssemblyInput),
        ];
        ImplementationAssemblyInput[] newAssemblies =
        [
            .. toPaths.Select(CreateImplementationAssemblyInput),
        ];
        bool useExactBodyReturnIdentities =
            oldAssemblies.Length == 1
            && newAssemblies.Length == 1;
        ResolvedDiffMemberTargets? targets =
            options.MemberFilter.Count == 0
                ? null
                : ResolveMemberTargetIdentities(
                fromSurface ?? AssemblySetSurfaceBuilder.Build(fromPaths, includeAll: options.IncludeAll) ?? new ApiSurface(),
                toSurface ?? AssemblySetSurfaceBuilder.Build(toPaths, includeAll: options.IncludeAll) ?? new ApiSurface(),
                options.MemberFilter,
                options.TypeFilter,
                requireBodyTargets: true,
                includeReturnTypeBodyIdentities:
                    !useExactBodyReturnIdentities,
                bodySectionName: SelectsComplexityContext(options)
                    ? "Complexity Context"
                    : SelectsStructuralContext(options)
                        ? "Structural Context"
                        : "Implementation Diff");
        if (targets is not null && useExactBodyReturnIdentities)
        {
            AddReturnTypeTargetIdentities(
                oldAssemblies[0],
                targets.OldBodyMetadataTokens,
                targets.MemberIdentities);
            AddReturnTypeTargetIdentities(
                newAssemblies[0],
                targets.NewBodyMetadataTokens,
                targets.MemberIdentities);
        }

        return new ImplementationComparisonInput(
            oldAssemblies,
            newAssemblies,
            options.TypeFilter,
            targets?.MemberIdentities);
    }

    static void AddReturnTypeTargetIdentities(
        ImplementationAssemblyInput assembly,
        IReadOnlySet<int> metadataTokens,
        ISet<string> identities)
    {
        foreach (MethodIdentity method in assembly.MethodPopulation.DeclaredMethods)
        {
            if (metadataTokens.Contains(method.MetadataToken))
            {
                ResearchMemberIdentity.AddReturnTypeTargetIdentity(
                    method,
                    identities);
            }
        }
    }

    static ImplementationAssemblyInput CreateImplementationAssemblyInput(
        string path)
    {
        var assembly = ResolvedAssemblyReference.CreateFromPath(
            path,
            AssemblyResolutionProvenance.Local(
                "diff implementation comparison"));
        var session = MethodBodyInspectionSession.Open(
            assembly,
            includeImplementationProfiles: true);
        return new(
            assembly,
            MetadataSource.DefaultAssemblyReferenceResolver(path),
            session.CallGraphAnalysis,
            session.AnalysisExecution.ImplementationProfiles);
    }

    internal sealed record ImplementationDiffWithSource(
        ImplementationDiffResult Local,
        InspectionEnvelope<AssemblyMemberSourcePairResult>?
            SelectedSource = null);

    internal static async Task<ImplementationDiffWithSource> BuildImplementationDiffWithSourceAsync(
        IReadOnlyList<string> fromPaths,
        IReadOnlyList<string> toPaths,
        DiffOptions options,
        HttpClient httpClient,
        VerboseLogger logger,
        ApiSurface? fromSurface = null,
        ApiSurface? toSurface = null,
        IReadOnlyList<AssemblySetEntry>? fromEntries = null,
        IReadOnlyList<AssemblySetEntry>? toEntries = null)
    {
        var result = BuildImplementationDiff(
            fromPaths,
            toPaths,
            options,
            fromSurface,
            toSurface);
        return await BuildImplementationDiffWithSourceAsync(
            result,
            fromPaths,
            toPaths,
            options,
            httpClient,
            logger,
            fromSurface,
            toSurface,
            fromEntries,
            toEntries);
    }

    internal static async Task<ImplementationDiffWithSource>
        BuildImplementationDiffWithSourceAsync(
            ImplementationDiffResult result,
            IReadOnlyList<string> fromPaths,
            IReadOnlyList<string> toPaths,
            DiffOptions options,
            HttpClient httpClient,
            VerboseLogger logger,
            ApiSurface? fromSurface = null,
            ApiSurface? toSurface = null,
            IReadOnlyList<AssemblySetEntry>? fromEntries = null,
            IReadOnlyList<AssemblySetEntry>? toEntries = null)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (!options.IncludePdbSource)
            return new(result);

        AssemblyMemberSourcePairRequest? request = null;
        if (options.MemberFilter.Count == 1 && fromPaths.Count == 1 && toPaths.Count == 1)
        {
            request = TryResolveSelectedSourceRequest(
                fromSurface ?? AssemblySetSurfaceBuilder.Build(fromPaths, includeAll: options.IncludeAll)
                    ?? throw new InvalidOperationException("Failed to extract API surface from the old selected PDB Source endpoint."),
                toSurface ?? AssemblySetSurfaceBuilder.Build(toPaths, includeAll: options.IncludeAll)
                    ?? throw new InvalidOperationException("Failed to extract API surface from the new selected PDB Source endpoint."),
                options);
        }
        if (request is not null)
        {
            await using var workspace = new InspectionWorkspace();
            var before = CreateSourceParticipant(
                fromPaths[0],
                options,
                oldSide: true,
                FindAssemblySetEntry(fromPaths[0], fromEntries));
            var after = CreateSourceParticipant(
                toPaths[0],
                options,
                oldSide: false,
                FindAssemblySetEntry(toPaths[0], toEntries));
            using var beforeGroup = workspace.CreateAssemblyContextGroup([before]);
            using var afterGroup = workspace.CreateAssemblyContextGroup([after]);
            var sourceContext = CreateSourceQueryContext(
                options,
                httpClient,
                logger);
            InspectionEnvelope<AssemblyMemberSourcePairResult> inspection =
                await MemberSourcePairInspection.ExecuteAsync(
                beforeGroup, before, afterGroup, after, request, sourceContext);
            return new(result, inspection);
        }

        // Broader selections and targets without one exact MethodDef anchor
        // (including property/event accessor selections) use the batch route.
        if (result.Members.Count == 0)
            return new(result);

        var subjects = result.Members
            .Select(member => member.Subject)
            .ToDictionary(subject => subject.Id, StringComparer.Ordinal);
        PdbSourceEndpointIndex fromIndex =
            PdbSourceEndpointMethods(
                result,
                subjects,
                oldSide: true);
        PdbSourceEndpointIndex toIndex =
            PdbSourceEndpointMethods(
                result,
                subjects,
                oldSide: false);
        foreach (PdbSourceEndpointFailure failure
            in fromIndex.Failures.Values)
        {
            logger.Log(failure.Detail);
        }
        foreach (PdbSourceEndpointFailure failure
            in toIndex.Failures.Values)
        {
            logger.Log(failure.Detail);
        }
        var from = await AcquirePdbSourceInspectionsAsync(
            fromPaths,
            subjects,
            options,
            oldSide: true,
            httpClient,
            logger,
            fromEntries,
            fromIndex.Methods,
            fromIndex.Failures);
        var to = await AcquirePdbSourceInspectionsAsync(
            toPaths,
            subjects,
            options,
            oldSide: false,
            httpClient,
            logger,
            toEntries,
            toIndex.Methods,
            toIndex.Failures);
        var comparisons = subjects.Values.Select(subject =>
            new PdbSourceComparisonInput(
                subject,
                PdbSourceInspectionFor(from, subject, oldSide: true),
                PdbSourceInspectionFor(to, subject, oldSide: false)));
        return new(ImplementationDiff.WithPdbSourceComparisons(
            result,
            comparisons,
            new ImplementationDiffOptions(
                TypeFilters: options.TypeFilter,
                MemberTargetIdentities: subjects.Keys.ToHashSet(StringComparer.Ordinal))));
    }

    sealed record PdbSourceEndpointIndex(
        ImmutableDictionary<string, MethodIdentity> Methods,
        ImmutableDictionary<string, PdbSourceEndpointFailure> Failures);

    internal sealed record PdbSourceEndpointFailure(
        string Detail,
        ImmutableArray<MethodIdentity> AmbiguousMethods);

    static PdbSourceEndpointIndex
        PdbSourceEndpointMethods(
            ImplementationDiffResult result,
            IReadOnlyDictionary<string, ResearchSubjectKey> subjects,
            bool oldSide)
    {
        var methods =
            new Dictionary<string, MethodIdentity>(
                StringComparer.Ordinal);
        var ambiguous =
            new HashSet<string>(StringComparer.Ordinal);
        var failures =
            new Dictionary<string, PdbSourceEndpointFailure>(
                StringComparer.Ordinal);
        foreach (ImplementationComplexityChange change
            in result.Complexity.Changes)
        {
            if (!subjects.ContainsKey(change.Subject.Id))
                continue;
            MethodIdentity? method = oldSide
                ? change.OldProfile?.Method
                : change.NewProfile?.Method;
            if (method is null)
                continue;
            if (ambiguous.Contains(change.Subject.Id))
                continue;
            if (methods.TryGetValue(
                    change.Subject.Id,
                    out MethodIdentity? existing)
                && (existing.ModuleVersionId
                        != method.ModuleVersionId
                    || existing.MetadataToken
                        != method.MetadataToken))
            {
                methods.Remove(change.Subject.Id);
                ambiguous.Add(change.Subject.Id);
                failures[change.Subject.Id] = new(
                    $"PDB-source endpoint association remains unavailable "
                        + $"for Research subject '{change.Subject.Id}': "
                        + $"0x{existing.MetadataToken:X8} "
                        + $"'{existing.DeclaringType.ToQualifiedDisplayString()}."
                        + $"{existing.Name}' and "
                        + $"0x{method.MetadataToken:X8} "
                        + $"'{method.DeclaringType.ToQualifiedDisplayString()}."
                        + $"{method.Name}' share that Research identity.",
                    [existing, method]);
                continue;
            }

            methods[change.Subject.Id] = method;
        }
        return new(
            methods.ToImmutableDictionary(StringComparer.Ordinal),
            failures.ToImmutableDictionary(StringComparer.Ordinal));
    }

    static AssemblyMemberSourcePairRequest? TryResolveSelectedSourceRequest(
        ApiSurface fromSurface,
        ApiSurface toSurface,
        DiffOptions options)
    {
        var parsed = ParseDiffMemberTarget(
            options.MemberFilter.Single(), fromSurface, toSurface, options.TypeFilter);
        AssemblyMemberSourcePairRequest? request = null;
        bool unsupported = false;
        foreach (var surface in new[] { fromSurface, toSurface })
        {
            var type = FindSelectedType(surface, parsed.TypeName, out string? error);
            if (error is not null)
                throw new InvalidOperationException(error);
            if (type is null)
                continue;
            var resolution = MemberTargetResolver.Resolve(type, parsed.Selector);
            if (resolution.Target is not { } target)
            {
                if (resolution.Diagnostic is { } diagnostic
                    && IsFatalTargetDiagnostic(diagnostic.Kind))
                    throw new InvalidOperationException(diagnostic.Message);
                continue;
            }

            var member = target.ApiMember.Member;
            if (member.Kind is "property" or "event" or "field"
                || type.DefinitionName is null
                || member.MetadataToken is null
                || target.Body?.MetadataToken != member.MetadataToken)
            {
                unsupported = true;
                continue;
            }
            var candidate = AssemblyMemberSourcePairRequest.From(type, member);
            AssemblyMemberSourcePairEndpointRequest endpoint =
                candidate.Before
                ?? throw new InvalidOperationException(
                    "A same-member Source pair request has no Before endpoint.");
            if (endpoint.Member != target.Anchor
                || (request is not null
                    && request.Before != candidate.Before))
            {
                unsupported = true;
            }
            request = candidate;
        }
        if (unsupported)
            return null;
        return request ?? throw new InvalidOperationException(
            "The selected PDB Source member did not resolve in either diff input.");
    }

    static AssemblyContextParticipant CreateSourceParticipant(
        string path,
        DiffOptions options,
        bool oldSide,
        AssemblySetEntry? entry)
    {
        var (packageName, packageVersion) = DiffPackageIdentity(options, oldSide);
        var provenance = entry is { SourceKind: AssemblySetSourceKind.Package, Version: not null }
            ? AssemblyResolutionProvenance.Package(entry.Source, entry.Version, entry.Tfm, rid: null)
            : entry is { SourceKind: AssemblySetSourceKind.PlatformAssembly or AssemblySetSourceKind.PlatformFramework }
                ? AssemblyResolutionProvenance.Platform(entry.Source, entry.Version, "diff selected PDB source")
            : packageName is not null && packageVersion is not null
            ? AssemblyResolutionProvenance.Package(packageName, packageVersion, options.Tfm, rid: null)
            : options.PlatformVersionRange is { } platform
                ? AssemblyResolutionProvenance.Platform(
                    ParseVersionRange(platform).package ?? Path.GetFileNameWithoutExtension(path),
                    oldSide ? ParseVersionRange(platform).fromVersion : ParseVersionRange(platform).toVersion,
                    "diff selected PDB source")
                : AssemblyResolutionProvenance.Local("diff selected PDB source");
        return new(
            ResolvedAssemblyReference.CreateFromPath(path, provenance),
            new AssemblyDependencyResolver(new AssemblyDependencyResolutionOptions(path)));
    }

    static AssemblySetEntry? FindAssemblySetEntry(
        string path,
        IReadOnlyList<AssemblySetEntry>? entries)
    {
        if (entries is null)
            return null;
        string fullPath = Path.GetFullPath(path);
        return entries.FirstOrDefault(
            entry => Path.GetFullPath(entry.Path) == fullPath);
    }

    static AssemblyContextSourceQueryContext CreateSourceQueryContext(
        DiffOptions options,
        HttpClient httpClient,
        VerboseLogger logger)
        => new(
            httpClient,
            FileSystemPdbStore.CreateDefault(),
            new SourcePolicyPackageSourceAuthorization(options.SourceOptions),
            new SourceFetch(
                DotnetInspector.Networking.HttpClientFactory.SharedUntrustedFetch))
        {
            AllowAdjacentPdbReads = true,
            AllowLocalSourceReads = true,
            RepositoryPaths = options.SourceRepositories,
            NuGetSourceOptions = options.SourceOptions,
            Log = logger.Log,
        };

    internal sealed record PdbSourceInspectionBatch(
        ImmutableDictionary<string, FindingInspection<string>> Inspections,
        ImmutableArray<string> IndexingFailures);

    sealed record PdbSourceRequestIndex(
        ImmutableDictionary<int, AssemblyMemberSourceRequest> Requests,
        ImmutableDictionary<int, string> Failures);

    internal static FindingInspection<string> PdbSourceInspectionFor(
        PdbSourceInspectionBatch batch,
        ResearchSubjectKey subject,
        bool oldSide)
    {
        if (batch.Inspections.TryGetValue(subject.Id, out var inspection))
            return inspection;

        string side = oldSide ? "old" : "new";
        if (!batch.IndexingFailures.IsEmpty)
        {
            return new FindingInspection<string>.Failed(
                new InspectionError(
                    new FindingSubject(subject.Id, subject.Display),
                    Inspector.Text.TextFindings.LineDescriptor,
                    $"PDB-source target indexing failed for the {side} endpoint: "
                    + string.Join("; ", batch.IndexingFailures)));
        }

        return new FindingInspection<string>.Absent(
            FindingInspectionAbsenceKind.SubjectAbsent,
            $"The member is unavailable in the {side} endpoint.");
    }

    internal static async Task<PdbSourceInspectionBatch> AcquirePdbSourceInspectionsAsync(
        IReadOnlyList<string> paths,
        IReadOnlyDictionary<string, ResearchSubjectKey> subjects,
        DiffOptions options,
        bool oldSide,
        HttpClient httpClient,
        VerboseLogger logger,
        IReadOnlyList<AssemblySetEntry>? entries = null,
        IReadOnlyDictionary<string, MethodIdentity>?
            endpointMethods = null,
        IReadOnlyDictionary<string, PdbSourceEndpointFailure>?
            endpointFailures = null)
    {
        var results = new Dictionary<string, FindingInspection<string>>(StringComparer.Ordinal);
        if (endpointFailures is not null)
        {
            foreach ((string subjectId, PdbSourceEndpointFailure failure)
                in endpointFailures)
            {
                results[subjectId] =
                    PdbSourceFailure(
                        subjects[subjectId],
                        failure.Detail);
            }
        }
        var indexingFailures = ImmutableArray.CreateBuilder<string>();
        var indexed =
            new List<(
                string Path,
                LibraryCallGraphAnalysisResult MethodPopulation,
                AssemblyContextParticipant Participant)>();
        foreach (string path in paths)
        {
            try
            {
                LibraryCallGraphAnalysisResult methodPopulation = MethodBodyInspectionSession.Open(
                        path,
                        includeAllocations: false,
                        includeOpportunities: false)
                    .CallGraphAnalysis;
                AssemblyContextParticipant participant =
                    CreateSourceParticipant(
                        path,
                        options,
                        oldSide,
                        FindAssemblySetEntry(path, entries));
                indexed.Add((path, methodPopulation, participant));
            }
            catch (Exception ex) when (ex is IOException
                or UnauthorizedAccessException
                or BadImageFormatException
                or InvalidOperationException)
            {
                string failure =
                    $"Could not index PDB-source targets in '{path}' "
                    + $"({ex.GetType().Name}): {ex.Message}";
                logger.Log(failure);
                indexingFailures.Add(failure);
                continue;
            }
        }

        if (indexed.Count == 0)
        {
            return new PdbSourceInspectionBatch(
                results.ToImmutableDictionary(StringComparer.Ordinal),
                indexingFailures.ToImmutable());
        }

        var sourceContext = CreateSourceQueryContext(options, httpClient, logger);
        await using var workspace = new InspectionWorkspace();

        foreach ((string path, LibraryCallGraphAnalysisResult methodPopulation,
            AssemblyContextParticipant participant) in indexed)
        {
            foreach (string failure in PdbSourceDeclarationIndexFailures(
                path,
                methodPopulation.DeclaredMethods,
                methodPopulation.Diagnostics))
            {
                logger.Log(failure);
                indexingFailures.Add(failure);
            }

            var targets =
                new List<(
                    MethodIdentity Method,
                    ResearchSubjectKey Subject)>();
            var targetSubjects =
                new HashSet<string>(StringComparer.Ordinal);
            if (endpointMethods is not null)
            {
                foreach ((string subjectId, MethodIdentity method)
                    in endpointMethods)
                {
                    if (method.ModuleVersionId
                            != methodPopulation.ModuleIdentity.ModuleVersionId
                        || results.ContainsKey(subjectId))
                    {
                        continue;
                    }

                    targets.Add((method, subjects[subjectId]));
                    targetSubjects.Add(subjectId);
                }
            }
            foreach (MethodIdentity method in methodPopulation.DeclaredMethods)
            {
                ResearchSubjectKey derived =
                    ResearchMemberIdentity.SubjectFromMethod(method);
                if (!subjects.TryGetValue(
                        derived.Id,
                        out ResearchSubjectKey? subject)
                    || results.ContainsKey(subject.Id)
                    || !targetSubjects.Add(subject.Id))
                {
                    continue;
                }

                targets.Add((method, subject));
            }
            bool hasEndpointFailures =
                endpointFailures?.Values.Any(
                    failure => failure.AmbiguousMethods.Any(
                        method => method.ModuleVersionId
                            == methodPopulation.ModuleIdentity.ModuleVersionId))
                == true;
            if (targets.Count == 0 && !hasEndpointFailures)
                continue;

            PdbSourceRequestIndex requestIndex;
            try
            {
                requestIndex = BuildPdbSourceRequestIndex(path);
            }
            catch (Exception ex) when (ex is IOException
                or UnauthorizedAccessException
                or BadImageFormatException
                or InvalidOperationException)
            {
                foreach (var target in targets)
                {
                    var subject = subjects[target.Subject.Id];
                    results[subject.Id] = PdbSourceFailure(
                        subject,
                        $"PDB-source API target projection failed "
                        + $"({ex.GetType().Name}): {ex.Message}");
                }
                continue;
            }

            if (endpointFailures is not null)
            {
                ProjectPdbSourceEndpointFailures(
                    endpointFailures,
                    methodPopulation.ModuleIdentity.ModuleVersionId,
                    requestIndex,
                    subjects,
                    results);
            }
            if (targets.Count == 0)
                continue;

            using var group = workspace.CreateAssemblyContextGroup([participant]);
            PdbSourceRequestIndex? generatedRequestIndex = null;
            string? generatedRequestIndexFailure = null;
            foreach (var target in targets)
            {
                var subject = subjects[target.Subject.Id];
                if (!requestIndex.Requests.TryGetValue(
                        target.Method.MetadataToken,
                        out AssemblyMemberSourceRequest? request))
                {
                    if (generatedRequestIndex is null
                        && generatedRequestIndexFailure is null)
                    {
                        try
                        {
                            generatedRequestIndex =
                                BuildPdbSourceRequestIndex(
                                    path,
                                    includeCompilerGenerated: true);
                        }
                        catch (Exception ex) when (ex is IOException
                            or UnauthorizedAccessException
                            or BadImageFormatException
                            or InvalidOperationException)
                        {
                            generatedRequestIndexFailure =
                                $"PDB-source compiler-generated API target "
                                + $"projection failed "
                                + $"({ex.GetType().Name}): {ex.Message}";
                        }
                    }
                    generatedRequestIndex?.Requests.TryGetValue(
                        target.Method.MetadataToken,
                        out request);
                }
                if (request is null)
                {
                    requestIndex.Failures.TryGetValue(
                        target.Method.MetadataToken,
                        out string? apiFailure);
                    string? generatedFailure = null;
                    generatedRequestIndex?.Failures.TryGetValue(
                        target.Method.MetadataToken,
                        out generatedFailure);
                    results[subject.Id] = PdbSourceFailure(
                        subject,
                        string.Join(
                            " ",
                            new[]
                            {
                                apiFailure,
                                generatedFailure,
                                generatedRequestIndexFailure,
                                $"PDB-source API target projection did not "
                                    + $"retain MethodDef "
                                    + $"0x{target.Method.MetadataToken:X8}.",
                            }
                                .Where(static failure =>
                                    !string.IsNullOrWhiteSpace(failure))));
                    continue;
                }

                InspectionEnvelope<AssemblyMemberSourceEntry> inspection =
                    await MemberSourceInspection.ExecuteAsync(
                        group,
                        participant,
                        request,
                        sourceContext);
                results[subject.Id] =
                    ProjectPdbSourceInspection(
                        inspection.Content,
                        subject);
            }
        }

        return new PdbSourceInspectionBatch(
            results.ToImmutableDictionary(StringComparer.Ordinal),
            indexingFailures.ToImmutable());
    }

    static void ProjectPdbSourceEndpointFailures(
        IReadOnlyDictionary<
            string,
            PdbSourceEndpointFailure> failures,
        Guid moduleVersionId,
        PdbSourceRequestIndex requestIndex,
        IReadOnlyDictionary<string, ResearchSubjectKey> subjects,
        IDictionary<string, FindingInspection<string>> results)
    {
        foreach (PdbSourceEndpointFailure failure
            in failures.Values)
        {
            foreach (MethodIdentity method
                in failure.AmbiguousMethods)
            {
                if (method.ModuleVersionId != moduleVersionId
                    || !requestIndex.Requests.TryGetValue(
                        method.MetadataToken,
                        out AssemblyMemberSourceRequest? request)
                    || !subjects.TryGetValue(
                        request.Member.StableSelector,
                        out ResearchSubjectKey? subject)
                    || results.ContainsKey(subject.Id))
                {
                    continue;
                }

                results[subject.Id] =
                    PdbSourceFailure(
                        subject,
                        failure.Detail);
            }
        }
    }

    static PdbSourceRequestIndex BuildPdbSourceRequestIndex(
        string path,
        bool includeCompilerGenerated = false)
    {
        ApiSurface surface =
            AssemblyReader.ExtractApiSurface(
                path,
                includeAll: true,
                typesOnly: false,
                includeCompilerGenerated)
            ?? throw new InvalidOperationException(
                $"Could not extract the PDB-source API target surface from '{path}'.");
        var requests =
            new Dictionary<int, AssemblyMemberSourceRequest>();
        var failures = new Dictionary<int, string>();

        foreach (ApiType type in surface.Types)
        {
            foreach (ApiMember member in type.Members)
                AddPdbSourceRequest(type, member, requests, failures);
            foreach (ApiMember accessor in type.Members.SelectMany(
                owner => ApiMemberAccessors.Create(owner, type)))
            {
                AddPdbSourceRequest(type, accessor, requests, failures);
            }
        }

        return new(
            requests.ToImmutableDictionary(),
            failures.ToImmutableDictionary());
    }

    static void AddPdbSourceRequest(
        ApiType type,
        ApiMember member,
        IDictionary<int, AssemblyMemberSourceRequest> requests,
        IDictionary<int, string> failures)
    {
        if (member.MetadataToken is not { } metadataToken
            || System.Reflection.Metadata.Ecma335.MetadataTokens
                .EntityHandle(metadataToken).Kind
                != System.Reflection.Metadata.HandleKind.MethodDefinition)
        {
            return;
        }

        AssemblyMemberSourceRequest request;
        try
        {
            request = AssemblyMemberSourceRequest
                .From(type, member)
                .WithoutDecompiledFallback();
        }
        catch (ArgumentException ex)
        {
            failures[metadataToken] =
                $"PDB-source API target projection rejected MethodDef "
                + $"0x{metadataToken:X8}: {ex.Message}";
            return;
        }

        if (requests.TryGetValue(
                metadataToken,
                out AssemblyMemberSourceRequest? existing)
            && (existing.Type != request.Type
                || existing.Member != request.Member))
        {
            requests.Remove(metadataToken);
            failures[metadataToken] =
                $"PDB-source API target projection is ambiguous for "
                + $"MethodDef 0x{metadataToken:X8}.";
            return;
        }
        if (!failures.ContainsKey(metadataToken))
            requests[metadataToken] = request;
    }

    static FindingInspection<string> ProjectPdbSourceInspection(
        AssemblyMemberSourceEntry entry,
        ResearchSubjectKey subject)
        => entry switch
        {
            AssemblyMemberSourceEntry.Available
            {
                Source: AssemblyMemberSource.Pdb pdb,
            } => RebindPdbSourceInspection(pdb.Inspection, subject),
            AssemblyMemberSourceEntry.Available
            {
                Source: AssemblyMemberSource.Decompiled,
            } => PdbSourceFailure(
                subject,
                "Authored-only PDB Source inspection returned decompiled text."),
            AssemblyMemberSourceEntry.Unavailable
            {
                PdbAttempt: { } attempt,
            } => RebindPdbSourceInspection(attempt, subject),
            AssemblyMemberSourceEntry.Unavailable
            {
                Failure.Kind: AssemblySourceFailureKind.TargetNotFound,
            } unavailable => new FindingInspection<string>.Absent(
                FindingInspectionAbsenceKind.SubjectAbsent,
                $"{unavailable.Failure.Kind}: {unavailable.Failure.Detail}"),
            AssemblyMemberSourceEntry.Unavailable unavailable
                when unavailable.Failure.Kind
                    is AssemblySourceFailureKind.AuthoredMemberUnavailable
                    or AssemblySourceFailureKind.AuthoredMemberPartsUnavailable
                    or AssemblySourceFailureKind.AuthoredDocumentUnavailable
                    or AssemblySourceFailureKind.PdbAndDecompiledUnavailable
                => new FindingInspection<string>.Absent(
                    FindingInspectionAbsenceKind.NoApplicableInput,
                    $"{unavailable.Failure.Kind}: "
                    + unavailable.Failure.Detail),
            AssemblyMemberSourceEntry.Unavailable unavailable =>
                PdbSourceFailure(
                    subject,
                    $"{unavailable.Failure.Kind}: "
                    + unavailable.Failure.Detail),
            AssemblyMemberSourceEntry.Rejected rejected =>
                PdbSourceFailure(
                    subject,
                    $"PDB-source assembly candidate was rejected: "
                    + rejected.Failure),
            _ => throw new InvalidOperationException(
                "Unknown completed member source result."),
        };

    static FindingInspection<string> RebindPdbSourceInspection(
        PdbMemberSourceInspection inspection,
        ResearchSubjectKey subject)
    {
        var findingSubject =
            new FindingSubject(subject.Id, subject.Display);
        return inspection.Lines.Value switch
        {
            FindingInspection<string>.Complete complete =>
                new FindingInspection<string>.Complete(
                [
                    .. complete.Findings.Select(finding =>
                        new Finding<string>(
                            findingSubject,
                            finding.Descriptor,
                            finding.Key,
                            finding.Payload,
                            finding.Ordinal,
                            finding.Detail)),
                ]),
            FindingInspection<string>.Absent absent =>
                new FindingInspection<string>.Absent(
                    absent.Kind,
                    inspection.Outcome switch
                    {
                        PdbMemberSourceOutcome.NoVouchedDeclaration =>
                            "The selected member's PDB source range does not "
                            + "identify one declaration that can be shown.",
                        PdbMemberSourceOutcome.SourceMappingUnavailable =>
                            "The selected member has no portable-PDB source mapping.",
                        _ => absent.Detail,
                    }),
            FindingInspection<string>.Failed failed =>
                new FindingInspection<string>.Failed(
                    new InspectionError(
                        findingSubject,
                        failed.Error.Descriptor,
                        failed.Error.Reason)),
            _ => throw new InvalidOperationException(
                "Unknown PDB-source finding inspection."),
        };
    }

    static FindingInspection<string> PdbSourceFailure(
        ResearchSubjectKey subject,
        string reason)
        => new FindingInspection<string>.Failed(
            new InspectionError(
                new FindingSubject(subject.Id, subject.Display),
                Inspector.Text.TextFindings.LineDescriptor,
                reason));

    internal static ImmutableArray<string> PdbSourceDeclarationIndexFailures(
        string path,
        IEnumerable<MethodIdentity> declaredMethods,
        IEnumerable<AnalysisDiagnostic> diagnostics)
    {
        var declaredTokens = declaredMethods
            .Select(static method => method.MetadataToken)
            .ToHashSet();
        return
        [
            .. diagnostics
                .Where(diagnostic =>
                    !declaredTokens.Contains(diagnostic.MethodToken))
                .Select(diagnostic =>
                    $"Could not index PDB-source target in '{path}' "
                    + $"(method token 0x{diagnostic.MethodToken:X8}, "
                    + $"'{diagnostic.Method}'): {diagnostic.Message}"),
        ];
    }

    static (string? PackageName, string? PackageVersion) DiffPackageIdentity(
        DiffOptions options,
        bool oldSide)
    {
        if (options.PackageVersionRange is null)
            return (null, null);

        var (name, fromVersion, toVersion) = ParseVersionRange(options.PackageVersionRange);
        return (name, oldSide ? fromVersion : toVersion);
    }

    // Applies the changed-only / allocation-regression filters, ranks rows (in-place
    // changes first, then by descending movement magnitude), and builds the summary
    // line. Pure function over already-classified rows so it can be unit-tested
    // without assemblies. In allocation-regression focus mode, only in-place
    // allocation increases are kept and in-loop (hot) ones are surfaced first.
    internal static AnalysisDiffResult RankAnalysisRows(IReadOnlyList<RankedAnalysisRow> ranked, bool changedOnly, bool allocRegressionsOnly = false)
    {
        IEnumerable<RankedAnalysisRow> filtered = ranked;
        if (allocRegressionsOnly)
            filtered = filtered.Where(row => row.InBoth && row.Direction > 0 && row.Row.Signal == "allocations");
        else if (changedOnly)
            filtered = filtered.Where(row => row.InBoth);
        var selected = filtered.ToList();

        var regressions = selected.Count(row => row.InBoth && row.Direction > 0);
        var improvements = selected.Count(row => row.InBoth && row.Direction < 0);
        var addedRemoved = selected.Count(row => !row.InBoth);
        var inLoopRegressions = selected.Count(row => row.InLoop && row.Direction > 0);

        var rows = selected
            .OrderByDescending(row => allocRegressionsOnly && row.InLoop)
            .ThenByDescending(row => row.InBoth)
            .ThenByDescending(row => row.Magnitude)
            .ThenBy(row => row.Row.Member, StringComparer.Ordinal)
            .ThenBy(row => row.Row.Signal, StringComparer.Ordinal)
            .ThenBy(row => row.Row.Shape ?? "", StringComparer.Ordinal)
            .Select(row => row.Row)
            .ToList();

        var summary = allocRegressionsOnly
            ? BuildAllocRegressionSummary(rows.Count, inLoopRegressions)
            : BuildAnalysisSummary(rows.Count, regressions, improvements, addedRemoved, changedOnly);
        return new AnalysisDiffResult(rows, summary);
    }

    internal static string BuildAllocRegressionSummary(int total, int inLoop)
    {
        if (total == 0)
            return "No allocation regressions detected.";
        var hot = inLoop > 0 ? $", {inLoop} in loop" : "";
        return $"{total} allocation regression{(total == 1 ? "" : "s")}{hot} ({total} signal{(total == 1 ? "" : "s")}).";
    }

    internal static string BuildAnalysisSummary(int total, int regressions, int improvements, int addedRemoved, bool changedOnly)
    {
        if (total == 0)
            return changedOnly ? "No in-place analysis signal changes detected." : "No analysis signal changes detected.";
        var parts = new List<string>(3);
        if (regressions > 0) parts.Add($"{regressions} regression{(regressions == 1 ? "" : "s")}");
        if (improvements > 0) parts.Add($"{improvements} improvement{(improvements == 1 ? "" : "s")}");
        if (!changedOnly && addedRemoved > 0) parts.Add($"{addedRemoved} added/removed");
        var detail = parts.Count > 0 ? string.Join(", ", parts) : $"{total} changed signal{(total == 1 ? "" : "s")}";
        return $"{detail} ({total} signal{(total == 1 ? "" : "s")})";
    }

    internal static string MethodKey(MethodIdentity method)
        => $"{method.AssemblyName}|{GenericMemberIdentity.KeyFragment(method.DeclaringType)}|{method.Name}|{string.Join(",", method.ParameterTypes.Select(GenericMemberIdentity.KeyFragment))}|{GenericMemberIdentity.KeyFragment(method.ReturnType)}";

    private static (string? package, string? fromVersion, string? toVersion) ParseVersionRange(string input)
    {
        // Format: Package@v1..v2
        int atIndex = input.IndexOf('@');
        if (atIndex <= 0)
            return (null, null, null);

        string packageName = input[..atIndex];
        string versionPart = input[(atIndex + 1)..];

        int dotDotIndex = versionPart.IndexOf("..", StringComparison.Ordinal);
        if (dotDotIndex <= 0)
            return (null, null, null);

        string fromVersion = versionPart[..dotDotIndex];
        string toVersion = versionPart[(dotDotIndex + 2)..];

        if (string.IsNullOrEmpty(fromVersion) || string.IsNullOrEmpty(toVersion))
            return (null, null, null);

        return (packageName, fromVersion, toVersion);
    }

    internal static IReadOnlyList<TypeDiff> ApplyFilters(ApiDiff diff, DiffOptions options)
    {
        var typeDiffs = diff.TypeDiffs;
        var beforeTypeFilterCount = typeDiffs.Count;

        // Apply type filter post-Compare
        if (options.TypeFilter.Count > 0)
        {
            typeDiffs = typeDiffs
                .Where(td => MatchesAnyDiffTypeFilter(td.TypeFullName, options.TypeFilter))
                .ToList();

            if (typeDiffs.Count == 0 && beforeTypeFilterCount > 0 && options.MemberFilter.Count == 0)
                CommandError.WriteNote($"type filter matched no changed types: {string.Join(", ", options.TypeFilter)}.");
        }

        // Apply classification filter
        var filtered = FilterByClassification(typeDiffs, options);
        var classificationFilterActive = options.Breaking || options.Additive;
        if (filtered.Count == 0 && typeDiffs.Count > 0 && classificationFilterActive)
        {
            CommandError.WriteNote("classification filter removed all changes after type/member filters.");
        }

        return filtered;
    }

    private static IReadOnlyList<TypeDiff> ApplyTypeFilterOnly(IReadOnlyList<TypeDiff> typeDiffs, IReadOnlyCollection<string> typeFilters)
        => typeFilters.Count == 0
            ? typeDiffs
            : typeDiffs.Where(td => MatchesAnyDiffTypeFilter(td.TypeFullName, typeFilters)).ToList();

    internal static bool MatchesAnyDiffTypeFilter(string typeFullName, IEnumerable<string> filters)
    {
        foreach (var filter in filters)
        {
            if (MatchesDiffTypeFilter(typeFullName, filter))
                return true;
        }

        return false;
    }

    private static bool MatchesDiffTypeFilter(string typeFullName, string filter)
    {
        if (TypeMatcher.MatchesTypeFilter(typeFullName, filter))
            return true;

        if (filter.Contains('*') || filter.Contains('?'))
            return false;

        var normalizedFilter = FqnParser.NormalizeTypeName(filter);
        return typeFullName.StartsWith(normalizedFilter + ".", StringComparison.OrdinalIgnoreCase)
               || typeFullName.Contains("." + normalizedFilter + ".", StringComparison.OrdinalIgnoreCase);
    }

    internal static string RenderDiff(string name, ApiDiff diff, string fromVersion, string toVersion, DiffOptions options)
    {
        var typeDiffs = ApplyFilters(diff, options);

        if (options.NameOnly)
        {
            return OutputFormatter.RenderTable(showHeader: false, (writer, formatter) =>
            {
                var nameWriter = new Markout.MarkoutWriter(writer, formatter, OutputFormatter.CreateTableWriterOptions(options.Tsv, options.Jsonl));
                DiffOutputFormatter.RenderNameOnly(nameWriter, typeDiffs);
                nameWriter.Flush();
            });
        }

        return DiffOutputFormatter.RenderFullMarkdown(
            name,
            typeDiffs,
            diff.InspectionFailures,
            fromVersion,
            toVersion,
            OutputFormatter.CreateWindowedOptions(options.Rows));
    }

    internal static ApiDiff BuildApiDiff(ApiSurface fromSurface, ApiSurface toSurface, DiffOptions options)
        => BuildApiDiff(
            ApiComparisonQuery.Execute(fromSurface, toSurface),
            fromSurface,
            toSurface,
            options);

    private static ApiDiff BuildApiDiff(
        ApiFindingComparison comparison,
        ApiSurface fromSurface,
        ApiSurface toSurface,
        DiffOptions options)
    {
        var diff = comparison.ApiDiff;

        if (options.MemberFilter.Count == 0)
            return diff;

        var candidateTypeDiffs = ApplyTypeFilterOnly(diff.TypeDiffs, options.TypeFilter);
        if (candidateTypeDiffs.Count == 0 && diff.TypeDiffs.Count > 0 && options.TypeFilter.Count > 0)
            CommandError.WriteNote($"type filter matched no changed types: {string.Join(", ", options.TypeFilter)}.");

        var filtered = FilterApiDiffByMemberTargets(diff, fromSurface, toSurface, options);
        if (filtered.TypeDiffs.Count == 0 && candidateTypeDiffs.Count > 0)
            CommandError.WriteNote($"member filter matched no changed members after type filters: {string.Join(", ", options.MemberFilter)}.");

        return filtered;
    }

    internal static IReadOnlyList<FindingTransitionRow> BuildFindingTransitions(
        ApiSurface fromSurface,
        ApiSurface toSurface,
        string fromVersion,
        string toVersion,
        DiffOptions options)
    {
        if (options.TypeFilter.Count == 0 && options.MemberFilter.Count == 0)
            throw new InvalidOperationException("Finding Transitions requires --type or a type-qualified --member target.");

        var subject = new FindingSubject("api", "API surface");
        var diffOptions = new ApiDiffOptions(
            options.IncludeAll ? ApiDiffScope.All : ApiDiffScope.Signature);
        string descriptor = ResolveFindingDescriptor(options);
        IEnumerable<string> typeNames = ResolveFindingTypeNames(
            fromSurface,
            toSurface,
            options.TypeFilter);
        if (descriptor == MetadataFindings.TypeDescriptor.Id)
        {
            return typeNames
                .SelectMany(typeName => ComparisonRows(
                    MetadataFindings.CompareApiType(
                        fromSurface,
                        toSurface,
                        subject,
                        typeName,
                        diffOptions),
                    MetadataFindings.TypeDescriptor,
                    typeName,
                    fromVersion,
                    toVersion,
                    emitEmptyComparison: false,
                    pair => ToTypeTransitionRow(
                        pair,
                        fromVersion,
                        toVersion)))
                .OrderBy(row => row.Target, StringComparer.Ordinal)
                .ToList();
        }

        if (descriptor == MetadataFindings.AttributeDescriptor.Id)
        {
            return typeNames
                .SelectMany(typeName => ComparisonRows(
                    MetadataFindings.CompareApiAttributes(
                        fromSurface,
                        toSurface,
                        subject,
                        typeName),
                    MetadataFindings.AttributeDescriptor,
                    typeName,
                    fromVersion,
                    toVersion,
                    emitEmptyComparison: false,
                    pair => ToAttributeTransitionRow(
                        pair,
                        fromVersion,
                        toVersion)))
                .OrderBy(row => row.Target, StringComparer.Ordinal)
                .ToList();
        }

        ResolvedDiffMemberTargets? targets = null;
        if (options.MemberFilter.Count == 0)
        {
            targets = null;
        }
        else
        {
            targets = ResolveMemberTargetIdentities(
                fromSurface,
                toSurface,
                options.MemberFilter,
                options.TypeFilter);
            typeNames = targets.TypeNames;
        }

        return typeNames
            .SelectMany(typeName => ComparisonRows(
                MetadataFindings.CompareApiMembers(
                    fromSurface,
                    toSurface,
                    subject,
                    typeName,
                    diffOptions),
                MetadataFindings.MemberDescriptor,
                typeName,
                fromVersion,
                toVersion,
                emitEmptyComparison: false,
                pair => ToMemberTransitionRow(
                    pair,
                    fromVersion,
                    toVersion),
                targets is null
                    ? null
                    : pair => MatchesMemberPair(pair, targets)))
            .OrderBy(row => row.Target, StringComparer.Ordinal)
            .ToList();
    }

    internal static IReadOnlyList<FindingTransitionRow> BuildAllocationFindingTransitions(
        IReadOnlyList<string> fromPaths,
        IReadOnlyList<string> toPaths,
        ApiSurface fromSurface,
        ApiSurface toSurface,
        string fromVersion,
        string toVersion,
        DiffOptions options)
        => BuildBodySignalFindingTransitions<AllocationOccurrence>(
            fromPaths,
            toPaths,
            fromSurface,
            toSurface,
            fromVersion,
            toVersion,
            options,
            AnalysisFindings.AllocationDescriptor,
            ToAllocationTransitionRow);

    internal static IReadOnlyList<FindingTransitionRow> BuildCallSiteFindingTransitions(
        IReadOnlyList<string> fromPaths,
        IReadOnlyList<string> toPaths,
        ApiSurface fromSurface,
        ApiSurface toSurface,
        string fromVersion,
        string toVersion,
        DiffOptions options)
        => BuildBodySignalFindingTransitions<DirectCall>(
            fromPaths,
            toPaths,
            fromSurface,
            toSurface,
            fromVersion,
            toVersion,
            options,
            AnalysisFindings.CallSiteDescriptor,
            ToCallSiteTransitionRow);

    internal static IReadOnlyList<FindingTransitionRow> BuildUnsafetyFindingTransitions(
        IReadOnlyList<string> fromPaths,
        IReadOnlyList<string> toPaths,
        ApiSurface fromSurface,
        ApiSurface toSurface,
        string fromVersion,
        string toVersion,
        DiffOptions options)
        => BuildBodySignalFindingTransitions<UnsafetyOccurrence>(
            fromPaths,
            toPaths,
            fromSurface,
            toSurface,
            fromVersion,
            toVersion,
            options,
            AnalysisFindings.UnsafetyDescriptor,
            ToUnsafetyTransitionRow);

    internal static IReadOnlyList<FindingTransitionRow> BuildCSharpFindingTransitions(
        IReadOnlyList<string> fromPaths,
        IReadOnlyList<string> toPaths,
        ApiSurface fromSurface,
        ApiSurface toSurface,
        string fromVersion,
        string toVersion,
        DiffOptions options)
        => BuildRetainedFindingTransitions<CSharpCanonicalLine>(
            fromPaths,
            toPaths,
            fromSurface,
            toSurface,
            fromVersion,
            toVersion,
            options,
            ResearchChangeMechanism.CSharp,
            emitEmptyComparison: true,
            CSharpFindings.LineDescriptor,
            ToCSharpTransitionRow);

    internal static IReadOnlyList<FindingTransitionRow> BuildIlFindingTransitions(
        IReadOnlyList<string> fromPaths,
        IReadOnlyList<string> toPaths,
        ApiSurface fromSurface,
        ApiSurface toSurface,
        string fromVersion,
        string toVersion,
        DiffOptions options)
        => BuildRetainedFindingTransitions<CanonicalIlOperation>(
            fromPaths,
            toPaths,
            fromSurface,
            toSurface,
            fromVersion,
            toVersion,
            options,
            ResearchChangeMechanism.IlBody,
            emitEmptyComparison: true,
            IlFindings.OperationDescriptor,
            ToIlTransitionRow);

    // Analysis Findings select their endpoint methods only from Research
    // target correspondence over the admitted body-signal population.
    static IReadOnlyList<FindingTransitionRow> BuildBodySignalFindingTransitions<T>(
        IReadOnlyList<string> fromPaths,
        IReadOnlyList<string> toPaths,
        ApiSurface fromSurface,
        ApiSurface toSurface,
        string fromVersion,
        string toVersion,
        DiffOptions options,
        FindingDescriptor descriptor,
        Func<ResearchSubjectKey, PairFinding<T>, string, string, FindingTransitionRow>
            toTransitionRow)
        where T : notnull
    {
        RequireSingleFindingMember(options, descriptor);
        ResearchComparison research = RequireBodySignalComparison(
            BodySignalComparisonQuery.Execute(
                CreateBodySignalComparisonInput(
                    fromPaths,
                    toPaths,
                    options,
                    fromSurface,
                    toSurface,
                    [descriptor])),
            options,
            DiffSections.FindingTransitions.Name);
        return RetainedTransitionRows(
            research,
            fromVersion,
            toVersion,
            emitEmptyComparison: false,
            descriptor,
            toTransitionRow);
    }

    static void RequireSingleFindingMember(
        DiffOptions options,
        FindingDescriptor descriptor)
    {
        if (options.MemberFilter.Count != 1)
        {
            throw new InvalidOperationException(
                $"--finding {descriptor.Id} requires exactly one --member target.");
        }
    }

    static IReadOnlyList<FindingTransitionRow> RetainedTransitionRows<T>(
        ResearchComparison research,
        string fromVersion,
        string toVersion,
        bool emitEmptyComparison,
        FindingDescriptor descriptor,
        Func<ResearchSubjectKey, PairFinding<T>, string, string, FindingTransitionRow>
            toTransitionRow)
        where T : notnull
        => research.RetainedComparisons.Get<T>(descriptor)
            .SelectMany(comparison => RetainedComparisonRows(
                comparison,
                fromVersion,
                toVersion,
                emitEmptyComparison,
                toTransitionRow))
            .OrderBy(row => row.Target, StringComparer.Ordinal)
            .ThenBy(row => row.Transition, StringComparer.Ordinal)
            .ToList();

    static IReadOnlyList<FindingTransitionRow> BuildRetainedFindingTransitions<T>(
        IReadOnlyList<string> fromPaths,
        IReadOnlyList<string> toPaths,
        ApiSurface fromSurface,
        ApiSurface toSurface,
        string fromVersion,
        string toVersion,
        DiffOptions options,
        ResearchChangeMechanism mechanism,
        bool emitEmptyComparison,
        FindingDescriptor descriptor,
        Func<ResearchSubjectKey, PairFinding<T>, string, string, FindingTransitionRow>
            toTransitionRow)
        where T : notnull
    {
        RequireSingleFindingMember(options, descriptor);
        var targets = ResolveMemberTargetIdentities(
            fromSurface,
            toSurface,
            options.MemberFilter,
            options.TypeFilter,
            requireBodyTargets: true,
            bodySectionName: "Finding Transitions");
        var research = ResearchDiff.Compare(
            ResearchDiffInput.FromAssemblies(fromPaths),
            ResearchDiffInput.FromAssemblies(toPaths),
            new ResearchDiffOptions(
                mechanism,
                TypeFilters: options.TypeFilter,
                MemberTargetIdentities: targets.MemberIdentities)
            {
                RetainedComparisonDescriptorIds =
                    ImmutableHashSet.Create(StringComparer.Ordinal, descriptor.Id),
            });
        return RetainedTransitionRows(
            research,
            fromVersion,
            toVersion,
            emitEmptyComparison,
            descriptor,
            toTransitionRow);
    }

    internal static IEnumerable<FindingTransitionRow> RetainedComparisonRows<T>(
        RetainedFindingComparison<T> retained,
        string fromVersion,
        string toVersion,
        bool emitEmptyComparison,
        Func<ResearchSubjectKey, PairFinding<T>, string, string, FindingTransitionRow>
            toTransitionRow)
        where T : notnull
        => ComparisonRows(
            retained.Comparison,
            retained.Descriptor,
            retained.Subject.Display,
            fromVersion,
            toVersion,
            emitEmptyComparison,
            pair => toTransitionRow(
                retained.Subject,
                pair,
                fromVersion,
                toVersion));

    internal static IEnumerable<FindingTransitionRow> ComparisonRows<T>(
        FindingComparison<T> comparison,
        FindingDescriptor descriptor,
        string target,
        string fromVersion,
        string toVersion,
        bool emitEmptyComparison,
        Func<PairFinding<T>, FindingTransitionRow> toTransitionRow,
        Func<PairFinding<T>, bool>? includePair = null)
        where T : notnull
    {
        if (comparison.Value is FindingComparison<T>.Failed failed)
        {
            yield return new FindingTransitionRow(
                "FindingComparison.Failed",
                descriptor.Id,
                target,
                fromVersion,
                toVersion,
                InspectionState(failed.OldInspection),
                InspectionState(failed.NewInspection),
                failed.Failure)
                .WithInspectionStates(
                    InspectionState(failed.OldInspection),
                    InspectionState(failed.NewInspection));
            yield break;
        }

        var complete = (FindingComparison<T>.Complete)comparison.Value;
        PairFinding<T>[] pairs = includePair is null
            ? [.. complete.Pairs]
            : [.. complete.Pairs.Where(includePair)];
        if (pairs.Length == 0)
        {
            if (!emitEmptyComparison
                && complete.Transition.IsSameTopology)
            {
                yield break;
            }

            yield return new FindingTransitionRow(
                "FindingComparison.Complete",
                descriptor.Id,
                target,
                fromVersion,
                toVersion,
                InspectionState(complete.OldInspection),
                InspectionState(complete.NewInspection),
                null)
                .WithInspectionStates(
                    InspectionState(complete.OldInspection),
                    InspectionState(complete.NewInspection));
            yield break;
        }

        foreach (PairFinding<T> pair in pairs)
        {
            yield return toTransitionRow(pair)
                .WithInspectionStates(
                    InspectionState(complete.OldInspection),
                    InspectionState(complete.NewInspection));
        }
    }

    static string InspectionState<T>(FindingInspection<T> inspection)
        where T : notnull
        => inspection.Value switch
        {
            FindingInspection<T>.Complete => "complete",
            FindingInspection<T>.Absent
                {
                    Kind: FindingInspectionAbsenceKind.SubjectAbsent,
                } => "subject-absent",
            FindingInspection<T>.Absent
                {
                    Kind: FindingInspectionAbsenceKind.NoApplicableInput,
                } => "no-applicable-input",
            FindingInspection<T>.Absent absent => throw new InvalidOperationException(
                $"Unsupported Finding inspection absence kind '{absent.Kind}'."),
            FindingInspection<T>.Failed => "failed",
            _ => throw new InvalidOperationException(
                "Finding inspection returned an unknown outcome."),
        };

    static IReadOnlyList<PairFinding<T>> CompletePairs<T>(FindingComparison<T> comparison)
        where T : notnull
        => comparison switch
        {
            FindingComparison<T>.Complete
                => ((FindingComparison<T>.Complete)comparison.Value).Pairs,
            FindingComparison<T>.Failed => throw new InvalidOperationException("Finding comparison did not complete."),
        };

    static bool MatchesMemberPair(
        PairFinding<ApiMemberHandle> pair,
        ResolvedDiffMemberTargets targets)
    {
        var oldHandle = OldSide(pair)?.Payload;
        var newHandle = NewSide(pair)?.Payload;
        var typeName = newHandle?.TypeFullName ?? oldHandle?.TypeFullName;
        return typeName is not null
            && targets.TypeNames.Contains(typeName)
            && (MatchesHandle(oldHandle, targets.MemberIdentities)
                || MatchesHandle(newHandle, targets.MemberIdentities));
    }

    static FindingTransitionRow ToTypeTransitionRow(
        PairFinding<ApiTypeHandle> pair,
        string fromVersion,
        string toVersion)
        => new(
            $"PairFinding.{pair.Kind}",
            pair.Descriptor.Id,
            TypeTarget(pair),
            fromVersion,
            toVersion,
            OldSide(pair) is null ? "absent" : "present",
            NewSide(pair) is null ? "absent" : "present",
            pair.Detail);

    static FindingTransitionRow ToMemberTransitionRow(
        PairFinding<ApiMemberHandle> pair,
        string fromVersion,
        string toVersion)
        => new(
            $"PairFinding.{pair.Kind}",
            pair.Descriptor.Id,
            MemberTarget(pair),
            fromVersion,
            toVersion,
            OldSide(pair) is null ? "absent" : "present",
            NewSide(pair) is null ? "absent" : "present",
            pair.Detail);

    static FindingTransitionRow ToAttributeTransitionRow(
        PairFinding<ApiAttributeHandle> pair,
        string fromVersion,
        string toVersion)
        => new(
            $"PairFinding.{pair.Kind}",
            pair.Descriptor.Id,
            AttributeTarget(pair),
            fromVersion,
            toVersion,
            OldSide(pair) is null ? "absent" : "present",
            NewSide(pair) is null ? "absent" : "present",
            pair.Detail);

    static FindingTransitionRow ToAllocationTransitionRow(
        ResearchSubjectKey subject,
        PairFinding<AllocationOccurrence> pair,
        string fromVersion,
        string toVersion)
    {
        var oldFinding = OldSide(pair);
        var newFinding = NewSide(pair);
        return new FindingTransitionRow(
            $"PairFinding.{pair.Kind}",
            pair.Descriptor.Id,
            FindingTargetFormatter.Format(subject.Display, newFinding ?? oldFinding!),
            fromVersion,
            toVersion,
            oldFinding is null ? "absent" : "present",
            newFinding is null ? "absent" : "present",
            pair.Detail ?? newFinding?.Detail ?? oldFinding?.Detail);
    }

    static FindingTransitionRow ToCallSiteTransitionRow(
        ResearchSubjectKey subject,
        PairFinding<DirectCall> pair,
        string fromVersion,
        string toVersion)
    {
        var oldFinding = OldSide(pair);
        var newFinding = NewSide(pair);
        return new FindingTransitionRow(
            $"PairFinding.{pair.Kind}",
            pair.Descriptor.Id,
            FindingTargetFormatter.Format(subject.Display, newFinding ?? oldFinding!),
            fromVersion,
            toVersion,
            oldFinding is null ? "absent" : "present",
            newFinding is null ? "absent" : "present",
            pair.Detail);
    }

    static FindingTransitionRow ToUnsafetyTransitionRow(
        ResearchSubjectKey subject,
        PairFinding<UnsafetyOccurrence> pair,
        string fromVersion,
        string toVersion)
    {
        var oldFinding = OldSide(pair);
        var newFinding = NewSide(pair);
        return new FindingTransitionRow(
            $"PairFinding.{pair.Kind}",
            pair.Descriptor.Id,
            FindingTargetFormatter.Format(subject.Display, newFinding ?? oldFinding!),
            fromVersion,
            toVersion,
            oldFinding is null ? "absent" : "present",
            newFinding is null ? "absent" : "present",
            pair.Detail ?? newFinding?.Detail ?? oldFinding?.Detail);
    }

    static FindingTransitionRow ToCSharpTransitionRow(
        ResearchSubjectKey subject,
        PairFinding<CSharpCanonicalLine> pair,
        string fromVersion,
        string toVersion)
    {
        var oldFinding = OldSide(pair);
        var newFinding = NewSide(pair);
        return new FindingTransitionRow(
            $"PairFinding.{pair.Kind}",
            pair.Descriptor.Id,
            CSharpLineTarget(subject, newFinding ?? oldFinding!),
            fromVersion,
            toVersion,
            oldFinding?.Payload.Text ?? "absent",
            newFinding?.Payload.Text ?? "absent",
            pair.Detail);
    }

    static string CSharpLineTarget(
        ResearchSubjectKey subject,
        Finding<CSharpCanonicalLine> finding)
        => $"{subject.Display} :: line {finding.Payload.Line}";

    static FindingTransitionRow ToIlTransitionRow(
        ResearchSubjectKey subject,
        PairFinding<CanonicalIlOperation> pair,
        string fromVersion,
        string toVersion)
    {
        var oldFinding = OldSide(pair);
        var newFinding = NewSide(pair);
        return new FindingTransitionRow(
            $"PairFinding.{pair.Kind}",
            pair.Descriptor.Id,
            IlOperationTarget(subject, newFinding ?? oldFinding!),
            fromVersion,
            toVersion,
            FormatIlFinding(oldFinding),
            FormatIlFinding(newFinding),
            pair.Detail);
    }

    static string IlOperationTarget(
        ResearchSubjectKey subject,
        Finding<CanonicalIlOperation> finding)
        => $"{subject.Display} :: IL_{finding.Payload.Offset:X4}";

    static string FormatIlFinding(Finding<CanonicalIlOperation>? finding)
        => finding is null
            ? "absent"
            : $"IL_{finding.Payload.Offset:X4} {finding.Payload.Display}";

    static string TypeTarget(PairFinding<ApiTypeHandle> pair)
        => (NewSide(pair) ?? OldSide(pair))!.Payload.TypeFullName;

    static string MemberTarget(PairFinding<ApiMemberHandle> pair)
    {
        var handle = (NewSide(pair) ?? OldSide(pair))!.Payload;
        return $"{handle.TypeFullName}.{handle.StableSelector ?? handle.Identity}";
    }

    static string MemberTypeTarget(PairFinding<ApiMemberHandle> pair)
        => (NewSide(pair) ?? OldSide(pair))!.Payload.TypeFullName;

    static string AttributeTarget(PairFinding<ApiAttributeHandle> pair)
    {
        var handle = (NewSide(pair) ?? OldSide(pair))!.Payload;
        return $"{handle.TypeFullName} [{handle.Attribute}]";
    }

    static Finding<T>? OldSide<T>(PairFinding<T> pair)
        where T : notnull
        => pair switch
        {
            PairFinding<T>.Added => null,
            PairFinding<T>.Removed => ((PairFinding<T>.Removed)pair.Value!).Old,
            PairFinding<T>.Present => ((PairFinding<T>.Present)pair.Value!).Old,
            PairFinding<T>.Changed => ((PairFinding<T>.Changed)pair.Value!).Old,
        };

    static Finding<T>? NewSide<T>(PairFinding<T> pair)
        where T : notnull
        => pair switch
        {
            PairFinding<T>.Added => ((PairFinding<T>.Added)pair.Value!).New,
            PairFinding<T>.Removed => null,
            PairFinding<T>.Present => ((PairFinding<T>.Present)pair.Value!).New,
            PairFinding<T>.Changed => ((PairFinding<T>.Changed)pair.Value!).New,
        };

    internal static ApiDiff FilterApiDiffByMemberTargets(ApiDiff diff, ApiSurface fromSurface, ApiSurface toSurface, DiffOptions options)
    {
        if (options.MemberFilter.Count == 0)
            return diff;

        var targets = ResolveMemberTargetIdentities(fromSurface, toSurface, options.MemberFilter, options.TypeFilter);
        List<TypeDiff> filtered = [];
        foreach (var typeDiff in diff.TypeDiffs)
        {
            var changes = typeDiff.Changes
                .Where(change => MatchesMemberTarget(typeDiff.TypeFullName, change, targets))
                .ToList();
            if (changes.Count > 0)
                filtered.Add(new TypeDiff(typeDiff.TypeFullName, changes));
        }

        return new ApiDiff
        {
            TypeDiffs = filtered,
            InspectionFailures = diff.InspectionFailures,
            TotalBreaking = filtered.Sum(typeDiff => typeDiff.BreakingCount),
            TotalAdditive = filtered.Sum(typeDiff => typeDiff.AdditiveCount),
            TotalPotentiallyBreaking = filtered.Sum(typeDiff => typeDiff.PotentiallyBreakingCount)
        };
    }

    static void WriteIncompleteComparisonDiagnostic(
        IReadOnlyCollection<ApiDiffInspectionFailure>
            inspectionFailures)
    {
        if (inspectionFailures.Count == 0)
            return;

        CommandError.WriteWarning(
            "API comparison is incomplete because metadata inspection "
                + $"reported {inspectionFailures.Count} failure(s); "
                + "use Markdown or JSON output for failure details.");
    }

    sealed record ResolvedDiffMemberTargets(
        HashSet<string> MemberIdentities,
        HashSet<string> TypeNames,
        HashSet<int> OldBodyMetadataTokens,
        HashSet<int> NewBodyMetadataTokens);

    static bool MatchesMemberTarget(string typeFullName, ApiChange change, ResolvedDiffMemberTargets targets)
        => IsMemberChange(change.Kind)
            ? MatchesHandle(change.Subject?.OldMember, targets.MemberIdentities)
              || MatchesHandle(change.Subject?.NewMember, targets.MemberIdentities)
            : IsWholeTypeChange(change.Kind) && targets.TypeNames.Contains(typeFullName);

    static bool MatchesHandle(ApiMemberHandle? handle, IReadOnlySet<string> targetIdentities)
        => handle is not null
           && ((handle.StableSelector is { } stable && targetIdentities.Contains(stable))
               || (handle.CanonicalSignature is { } canonical && targetIdentities.Contains(canonical))
               || targetIdentities.Contains(handle.Identity));

    static ResolvedDiffMemberTargets ResolveMemberTargetIdentities(
        ApiSurface fromSurface,
        ApiSurface toSurface,
        IReadOnlyCollection<string> memberTargets,
        IReadOnlyCollection<string> typeFilters,
        bool requireBodyTargets = false,
        bool includeReturnTypeBodyIdentities = false,
        string bodySectionName = "Analysis Diff")
    {
        HashSet<string> identities = new(StringComparer.Ordinal);
        HashSet<string> typeNames = new(StringComparer.Ordinal);
        HashSet<int> oldBodyMetadataTokens = [];
        HashSet<int> newBodyMetadataTokens = [];
        foreach (var rawTarget in memberTargets)
        {
            var parsed = ParseDiffMemberTarget(rawTarget, fromSurface, toSurface, typeFilters);
            foreach (string typeName in ResolveFindingTypeNames(
                fromSurface,
                toSurface,
                [parsed.TypeName]))
            {
                typeNames.Add(typeName);
            }

            var found = false;
            var bodyFound = false;
            MemberTargetDiagnostic? diagnostic = null;
            MemberTargetDiagnostic? nonFatalDiagnostic = null;
            ApiType? oldType = FindSelectedType(
                fromSurface,
                parsed.TypeName,
                out string? oldTypeError);
            ApiType? newType = FindSelectedType(
                toSurface,
                parsed.TypeName,
                out string? newTypeError);
            if (oldTypeError is not null || newTypeError is not null)
            {
                throw new InvalidOperationException(
                    oldTypeError ?? newTypeError);
            }

            if (oldType is not null)
            {
                var oldResult = AddResolvedIdentities(
                    oldType,
                    parsed.Selector,
                    identities,
                    includeReturnTypeBodyIdentities);
                found |= oldResult.Found;
                bodyFound |= oldResult.BodyFound;
                if (oldResult.BodyMetadataToken is { } oldToken)
                    oldBodyMetadataTokens.Add(oldToken);
                if (oldResult.Diagnostic is { } oldDiagnostic)
                {
                    if (IsFatalTargetDiagnostic(oldDiagnostic.Kind))
                        diagnostic ??= oldDiagnostic;
                    else
                        nonFatalDiagnostic ??= oldDiagnostic;
                }
                if (oldResult.Found)
                    typeNames.Add(oldType.FullName);
            }
            if (newType is not null)
            {
                var newResult = AddResolvedIdentities(
                    newType,
                    parsed.Selector,
                    identities,
                    includeReturnTypeBodyIdentities);
                found |= newResult.Found;
                bodyFound |= newResult.BodyFound;
                if (newResult.BodyMetadataToken is { } newToken)
                    newBodyMetadataTokens.Add(newToken);
                if (newResult.Diagnostic is { } newDiagnostic)
                {
                    if (IsFatalTargetDiagnostic(newDiagnostic.Kind))
                        diagnostic ??= newDiagnostic;
                    else
                        nonFatalDiagnostic ??= newDiagnostic;
                }
                if (newResult.Found)
                    typeNames.Add(newType.FullName);
            }

            if (diagnostic is not null)
                throw new InvalidOperationException(diagnostic.Message);
            if (!found)
                throw new InvalidOperationException(nonFatalDiagnostic?.Message ?? $"Member target '{rawTarget}' did not resolve in either diff input.");
            if (requireBodyTargets && !bodyFound)
                throw new InvalidOperationException($"{bodySectionName} --member requires a method-like target; '{rawTarget}' resolved to a member with no method body.");
        }

        return new ResolvedDiffMemberTargets(
            identities,
            typeNames,
            oldBodyMetadataTokens,
            newBodyMetadataTokens);
    }

    static (
        bool Found,
        bool BodyFound,
        int? BodyMetadataToken,
        MemberTargetDiagnostic? Diagnostic)
        AddResolvedIdentities(
            ApiType type,
            MemberTargetSelector selector,
            HashSet<string> identities,
            bool includeReturnTypeBodyIdentity)
    {
        var resolution = MemberTargetResolver.Resolve(type, selector);
        if (!resolution.Found)
            return (false, false, null, resolution.Diagnostic);

        identities.Add(resolution.Target!.Anchor.StableSelector);
        identities.Add(resolution.Target.Anchor.CanonicalSignature);
        var bodyFound = AddResearchBodyIdentity(resolution.Target, identities);
        if (includeReturnTypeBodyIdentity)
        {
            ResearchMemberIdentity.TryAddReturnTypeTargetIdentity(
                resolution.Target,
                identities);
        }
        return (
            true,
            bodyFound,
            resolution.Target.Body?.MetadataToken,
            null);
    }

    internal static bool AddResearchBodyIdentity(ResolvedMemberTarget target, HashSet<string> identities)
        => ResearchMemberIdentity.TryAddTargetIdentity(target, identities);

    static WorkspaceImplementationTarget?
        TryCreateWorkspaceImplementationTarget(
            DiffInputs inputs,
            DiffOptions options)
    {
        if (options.PackageVersionRange is null
            || !SelectsImplementationDiff(options)
            || options.MemberFilter.Count != 1
            || options.TypeFilter.Count != 1
            || options.IncludePdbSource
            || !WorkspaceImplementationComparisonRunner.HasSinglePackageRoot(
                inputs.From.AssemblySet,
                inputs.Name)
            || !WorkspaceImplementationComparisonRunner.HasSinglePackageRoot(
                inputs.To.AssemblySet,
                inputs.Name))
        {
            return null;
        }

        string typeSelector = options.TypeFilter.Single();
        WorkspaceImplementationTypeSelection? selectedType =
            ResolveWorkspaceImplementationTypeName(
                inputs.FromSurface,
                inputs.ToSurface,
                typeSelector);
        string typeName = selectedType?.DisplayName
            ?? typeSelector;
        MetadataTypeDefinitionName definitionName;
        if (selectedType is not null)
        {
            definitionName = selectedType.DefinitionName;
        }
        else if (MetadataTypeDefinitionName.ParseSerialized(
                typeSelector)
            is MetadataTypeDefinitionNameResult.Valid valid
            && valid.Name.Namespace.Length > 0)
        {
            definitionName = valid.Name;
        }
        else
        {
            return null;
        }

        string memberSelector =
            LowerWorkspaceImplementationMemberSelector(
                options.MemberFilter.Single(),
                typeSelector,
                typeName);
        MemberTargetSelector selector = MemberTargetSelector.Parse(
            memberSelector);
        return new(
            definitionName,
            selector,
            $"{typeName}.{selector.RequestedText}");
    }

    internal static WorkspaceImplementationTypeSelection?
        ResolveWorkspaceImplementationTypeName(
            ApiSurface fromSurface,
            ApiSurface toSurface,
            string query)
    {
        WorkspaceImplementationTypeSelection? oldType =
            SelectWorkspaceImplementationTypeName(
                fromSurface,
                query,
                out string? oldError);
        WorkspaceImplementationTypeSelection? newType =
            SelectWorkspaceImplementationTypeName(
                toSurface,
                query,
                out string? newError);
        if (oldError is not null
            || newError is not null
            || oldType is not null
                && newType is not null
                && !oldType.DefinitionName.Equals(
                    newType.DefinitionName))
        {
            return null;
        }

        return oldType
            ?? newType;
    }

    static WorkspaceImplementationTypeSelection?
        SelectWorkspaceImplementationTypeName(
            ApiSurface surface,
            string query,
            out string? error)
    {
        WorkspaceImplementationTypeSelection[] matches =
        [
            .. EnumerateWorkspaceImplementationTypes(surface)
                .Where(candidate =>
                    TypeMatcher.MatchesTypeFilter(
                        candidate.MatchName,
                        query))
                .GroupBy(
                    candidate => candidate.DefinitionName)
                .Select(group =>
                    new WorkspaceImplementationTypeSelection(
                        group.Key,
                        group.Key.ToEscapedFullName()))
                .OrderBy(
                    candidate => candidate.DisplayName,
                    StringComparer.Ordinal),
        ];
        WorkspaceImplementationTypeSelection? exact =
            matches.FirstOrDefault(candidate =>
            candidate.DisplayName.Equals(
                query,
                StringComparison.Ordinal));
        if (exact is not null)
            matches = [exact];
        if (matches.Length > 1)
        {
            error =
                $"Type target '{query}' is ambiguous. Use one of: "
                + $"{string.Join(", ", matches.Select(
                    candidate => candidate.DisplayName))}.";
            return null;
        }

        error = null;
        return matches.SingleOrDefault();
    }

    static IEnumerable<WorkspaceImplementationTypeCandidate>
        EnumerateWorkspaceImplementationTypes(
            ApiSurface surface)
    {
        foreach (ApiType type in surface.Types)
        {
            MetadataTypeDefinitionName? definitionName =
                type.DefinitionName
                ?? ParseWorkspaceImplementationTypeName(
                    string.IsNullOrEmpty(type.Namespace)
                        ? type.MetadataName ?? type.Name
                        : $"{type.Namespace}."
                            + $"{type.MetadataName ?? type.Name}");
            if (definitionName is not null)
            {
                yield return new(
                    definitionName,
                    type.FullName);
                yield return new(
                    definitionName,
                    definitionName.ToEscapedFullName());
            }
        }

        foreach (ApiSurfaceInspectionFailure failure
            in surface.InspectionFailures)
        {
            if (failure.OwningTypeDefinition is { } owner)
            {
                yield return new(
                    owner,
                    owner.ToEscapedFullName());
            }
            if (failure.AffectedTypeDefinitions.IsDefaultOrEmpty)
                continue;
            foreach (MetadataTypeDefinitionName affected
                in failure.AffectedTypeDefinitions)
            {
                yield return new(
                    affected,
                    affected.ToEscapedFullName());
            }
        }

        foreach (TypeForwarder forwarder
            in surface.TypeForwarders)
        {
            MetadataTypeDefinitionName? definitionName =
                forwarder.DefinitionName
                ?? ParseWorkspaceImplementationTypeName(
                    forwarder.TypeName);
            if (definitionName is not null)
            {
                yield return new(
                    definitionName,
                    forwarder.TypeName);
                yield return new(
                    definitionName,
                    definitionName.ToEscapedFullName());
            }
        }
    }

    static MetadataTypeDefinitionName?
        ParseWorkspaceImplementationTypeName(
            string typeName)
        => MetadataTypeDefinitionName.ParseSerialized(
            typeName)
            is MetadataTypeDefinitionNameResult.Valid valid
                ? valid.Name
                : null;

    internal static string
        LowerWorkspaceImplementationMemberSelector(
            string memberSelector,
            string typeSelector,
            string selectedTypeName)
    {
        foreach (int boundary
            in TopLevelDotPositionsFromRight(
                memberSelector))
        {
            string qualifier =
                memberSelector[..boundary];
            if (qualifier.Equals(
                    typeSelector,
                    StringComparison.Ordinal)
                || TypeMatcher.MatchesTypeFilter(
                    selectedTypeName,
                    qualifier))
            {
                return memberSelector[
                    (boundary + 1)..];
            }
        }

        return memberSelector;
    }

    static bool IsFatalTargetDiagnostic(MemberTargetDiagnosticKind kind)
        => kind is MemberTargetDiagnosticKind.AmbiguousMember
            or MemberTargetDiagnosticKind.DigestAmbiguous
            or MemberTargetDiagnosticKind.ConflictingSelectors;

    sealed record WorkspaceImplementationTarget(
        MetadataTypeDefinitionName DeclaringType,
        MemberTargetSelector Selector,
        string Display);

    internal sealed record WorkspaceImplementationTypeSelection(
        MetadataTypeDefinitionName DefinitionName,
        string DisplayName);

    sealed record WorkspaceImplementationTypeCandidate(
        MetadataTypeDefinitionName DefinitionName,
        string MatchName);

    sealed record ParsedDiffMemberTarget(string TypeName, MemberTargetSelector Selector);

    static ParsedDiffMemberTarget ParseDiffMemberTarget(
        string rawTarget,
        ApiSurface fromSurface,
        ApiSurface toSurface,
        IReadOnlyCollection<string> typeFilters)
    {
        if (TrySplitTypeQualifiedMemberTarget(rawTarget, fromSurface, toSurface, out var typeName, out var memberSelector))
            return new ParsedDiffMemberTarget(typeName, MemberTargetSelector.Parse(memberSelector));

        var typeContext = ResolveTypeContext(fromSurface, toSurface, typeFilters, out var contextError);
        if (contextError is { Length: > 0 })
            throw new InvalidOperationException(contextError);
        if (typeContext is null)
            throw new InvalidOperationException($"--member '{rawTarget}' requires exactly one --type filter or a type-qualified selector.");

        return new ParsedDiffMemberTarget(typeContext, MemberTargetSelector.Parse(rawTarget));
    }

    static bool TrySplitTypeQualifiedMemberTarget(
        string rawTarget,
        ApiSurface fromSurface,
        ApiSurface toSurface,
        out string typeName,
        out string memberSelector)
    {
        foreach (var marker in (ReadOnlySpan<string>)[".operator:", ".explicit:", ".extension:"])
        {
            var markerIndex = rawTarget.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (markerIndex > 0)
            {
                var candidate = rawTarget[..markerIndex];
                if (TryFindSingleType(fromSurface, toSurface, candidate, out typeName, out _))
                {
                    memberSelector = rawTarget[(markerIndex + 1)..];
                    return true;
                }
            }
        }

        foreach (var dot in TopLevelDotPositionsFromRight(rawTarget))
        {
            var candidate = rawTarget[..dot];
            if (TryFindSingleType(fromSurface, toSurface, candidate, out typeName, out _))
            {
                memberSelector = rawTarget[(dot + 1)..];
                return true;
            }
        }

        typeName = "";
        memberSelector = rawTarget;
        return false;
    }

    static IEnumerable<int> TopLevelDotPositionsFromRight(string value)
    {
        var depth = 0;
        for (var i = value.Length - 1; i >= 0; i--)
        {
            var ch = value[i];
            if (ch == '>')
                depth++;
            else if (ch == '<')
                depth--;
            else if (ch == '.' && depth == 0)
                yield return i;
        }
    }

    static string? ResolveTypeContext(
        ApiSurface fromSurface,
        ApiSurface toSurface,
        IReadOnlyCollection<string> typeFilters,
        out string? error)
    {
        error = null;
        if (typeFilters.Count != 1)
            return null;

        var query = typeFilters.First();
        if (TryFindSingleType(fromSurface, toSurface, query, out var typeName, out error))
            return typeName;

        return null;
    }

    static bool TryFindSingleType(ApiSurface fromSurface, ApiSurface toSurface, string query, out string typeName, out string? error)
    {
        string? oldTypeName = SelectTypeName(
            fromSurface,
            query,
            out string? oldError);
        string? newTypeName = SelectTypeName(
            toSurface,
            query,
            out string? newError);
        error = oldError ?? newError;
        if (error is not null
            || oldTypeName is null && newTypeName is null)
        {
            typeName = "";
            return false;
        }

        typeName = query;
        return true;
    }

    static IReadOnlyList<string> ResolveFindingTypeNames(
        ApiSurface fromSurface,
        ApiSurface toSurface,
        IReadOnlyCollection<string> typeFilters)
    {
        var names = typeFilters
            .SelectMany(filter => ResolveFindingTypeNames(fromSurface, filter)
                .Concat(ResolveFindingTypeNames(toSurface, filter))
                .DefaultIfEmpty(filter))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        return names;
    }

    static IEnumerable<string> ResolveFindingTypeNames(
        ApiSurface surface,
        string filter)
    {
        string[] matches = FindingTypeNames.EnumerateResolvable(surface)
            .Where(typeName =>
                TypeMatcher.MatchesTypeFilter(typeName, filter))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        string? exact = matches.FirstOrDefault(typeName =>
            typeName.Equals(filter, StringComparison.Ordinal));
        return exact is null ? matches : [exact];
    }

    static ApiType? FindSelectedType(
        ApiSurface surface,
        string query,
        out string? error)
    {
        string? selectedName = SelectTypeName(
            surface,
            query,
            out error);
        if (selectedName is null)
            return null;

        return surface.Types.FirstOrDefault(type =>
            type.FullName.Equals(
                selectedName,
                StringComparison.Ordinal));
    }

    static string? SelectTypeName(
        ApiSurface surface,
        string query,
        out string? error)
    {
        string[] matches = ResolveFindingTypeNames(surface, query)
            .ToArray();
        if (matches.Length > 1)
        {
            error = $"Type target '{query}' is ambiguous. Use one of: "
                + $"{string.Join(", ", matches)}.";
            return null;
        }

        error = null;
        return matches.SingleOrDefault();
    }

    static bool IsMemberChange(ChangeKind kind)
        => kind is ChangeKind.MemberAdded or ChangeKind.MemberRemoved or ChangeKind.MemberSignatureChanged
            or ChangeKind.VirtualRemoved or ChangeKind.AbstractMemberAdded or ChangeKind.EnumValueChanged
            or ChangeKind.MemberAttributeAdded or ChangeKind.MemberAttributeRemoved;

    static bool IsWholeTypeChange(ChangeKind kind)
        => kind is ChangeKind.TypeAdded or ChangeKind.TypeRemoved;

    private static IReadOnlyList<TypeDiff> FilterByClassification(IReadOnlyList<TypeDiff> typeDiffs, DiffOptions options)
    {
        if (!options.Breaking && !options.Additive)
            return typeDiffs;

        HashSet<ChangeClassification> allowed = [];
        if (options.Breaking) allowed.Add(ChangeClassification.Breaking);
        if (options.Additive) allowed.Add(ChangeClassification.Additive);

        List<TypeDiff> filtered = [];
        foreach (var td in typeDiffs)
        {
            var changes = td.Changes.Where(c => allowed.Contains(c.Classification)).ToList();
            if (changes.Count > 0)
                filtered.Add(new TypeDiff(td.TypeFullName, changes));
        }
        return filtered;
    }
}

/// <summary>
/// Options for the diff command.
/// </summary>
public record DiffOptions : IProjectionOptions
{
    public string? PackageVersionRange { get; init; }
    public string? PlatformVersionRange { get; init; }
    public string? LibraryVersionRange { get; init; }
    public string? Framework { get; init; }
    public string? Tfm { get; init; }
    public bool IncludeAll { get; init; }
    public bool History { get; init; }

    /// <summary>
    /// Debug-only destination for the complete enriched Diff History envelope.
    /// </summary>
    public string? EvidenceEnvelopePath { get; init; }
    public string[] At { get; init; } = [];
    public int? MaxProbes { get; init; }
    public int? SamplePercent { get; init; }
    public bool MajorVersions { get; init; }
    public bool IncludePrerelease { get; init; }
    public bool Count { get; init; }
    public RowSelectionIntent<string>? SemanticRowSelection { get; init; }
    public bool Verbose { get; init; }
    public HashSet<string> TypeFilter { get; init; } = [];
    public HashSet<string> MemberFilter { get; init; } = [];
    public bool Tabular { get; init; }
    public bool Tsv { get; init; }
    public bool Jsonl { get; init; }
    public bool JsonOutput { get; init; }
    public bool EnvelopeOutput { get; init; }
    public bool CompactJson { get; init; }
    public bool VerbosityExplicitlySet { get; init; }
    public bool HasRenderedLineWindow { get; init; }
    public bool TabularExplicitlySet { get; init; }
    public bool FormatExplicitlySet { get; init; }
    public bool NoHeader { get; init; }
    public bool NameOnly { get; init; }
    public bool Breaking { get; init; }
    public bool Additive { get; init; }
    public bool ChangedOnly { get; init; }
    public bool AllocRegressionsOnly { get; init; }
    public bool IncludePdbSource { get; init; }
    public string? Finding { get; init; }
    public bool Legend { get; init; }
    public string[]? Discover { get; init; }
    public bool Schema { get; init; }
    public bool Tree { get; init; }
    public string[]? Select { get; init; }

    /// <summary>
    /// Bare <c>-S</c>: a request for this command's default preset rather than for any named
    /// section or category. Tracked separately from <see cref="Select"/> so the marker is never
    /// spellable as a selector value. See #3547.
    /// </summary>
    public bool SelectDefault { get; init; }
    public HashSet<string>? IncludeSections { get; init; }

    /// <summary>
    /// Canonical sections reached through an exact selector or compatible legacy alias. An empty
    /// set records that selection came only through categories or globs. Null preserves
    /// exact-selection behavior for typed callers that supply <see cref="IncludeSections"/> directly.
    /// </summary>
    public HashSet<string>? ExactIncludeSectionsOverride { get; init; }

    /// <summary>The selected sections that retain exact-selector provenance.</summary>
    public HashSet<string>? ExactIncludeSections
        => ExactIncludeSectionsOverride ?? IncludeSections;

    public string[]? Columns { get; init; }
    public string[]? Fields { get; init; }
    public RowWindow? Rows { get; init; }
    public NuGetSourceOptions? SourceOptions { get; init; }

    /// <summary>
    /// Local git clone paths consulted for PDB source (Implementation Diff), by SourceLink
    /// commit + PDB checksum, before the network. Empty = network only. Set via <c>--repo</c>.
    /// </summary>
    public string[] SourceRepositories { get; init; } = [];

    /// <summary>
    /// True when output is raw text (not rendered markdown).
    /// </summary>
    public bool IsRawOutput => EnvelopeOutput || Tabular || Jsonl || JsonOutput || NoHeader || NameOnly;

    public bool IsContentJson => JsonOutput && !HasContentProjection;

    public bool HasContentProjection =>
        TypeFilter.Count > 0
        || Breaking || Additive
        || Select is not null || SelectDefault || IncludeSections is not null
        || Columns is not null || Fields is not null || Rows is not null
        || Tabular || Tsv || Jsonl || NoHeader || NameOnly || Tree
        || VerbosityExplicitlySet;
}
