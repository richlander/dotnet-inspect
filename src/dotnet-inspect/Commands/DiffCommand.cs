using ILInspector.Metadata;
using DotnetInspector.Inspectors;
using DotnetInspector.Options;
using DotnetInspector.Output;
using DotnetInspector.Packages;
using DotnetInspector.Queries;
using DotnetInspector.Sections;
using DotnetInspector.Services;
using DotnetInspector.Views;
using ILInspector.Analysis;
using ILInspector.Decompiler;
using ILInspector.Decompiler.Pipeline;
using ILInspector.Findings;
using ILInspector.Instructions;
using ILInspector.Research;
using Markout;
using System.Collections.Immutable;
using System.Text.Json;

namespace DotnetInspector.Commands;

/// <summary>
/// Compares API surfaces or selected body-level evidence between two versions.
/// </summary>
public class DiffCommand
{
    public const string Name = "diff";
    public static async Task<int> ExecuteAsync(DiffOptions options)
    {
        DiffSectionCatalog catalog = DiffSections.CreateCatalog();
        SectionCatalog<DiffDiscoveryModel> sectionCatalog = catalog.Sections;
        var pipeline = catalog.Pipeline;
        var selectResult = SelectResolver.ResolveSelectAsSections(
            options.Select,
            sectionCatalog.SelectableSectionNames,
            sectionCatalog.InfoSectionNames,
            sectionCatalog.SelectionCategoryMap,
            selectDefault: options.SelectDefault);
        if (SelectOutput.WriteUnresolved(selectResult))
            return 1;
        if (selectResult.Sections != null)
            options = options with { IncludeSections = selectResult.Sections };
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

        // Discovery mode: -D/--discover lists schema
        if (options.Discover != null)
        {
            var schemaMap = DiffSections.CreateSchema();
            var discoverable = pipeline.GetDiscoverableSections(new DiffDiscoveryModel(), options.IncludeSections);
            return DiscoverOutput.ExecuteEffective(options.Discover, discoverable, schemaMap,
                tree: options.Tree, json: false, tsv: options.Tsv, jsonl: options.Jsonl, markdown: !options.Tabular,
                sectionCostAnnotations: pipeline.GetCostAnnotations(),
                sectionCategories: pipeline.GetCategoryMap());
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
                    "Finding Transitions must be selected by itself because it is a focused endpoint-confirmation lens; select comparison sections explicitly instead of using @All.");
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

        CompiledInspectionPlan<DiffQueryContext> queryPlan =
            GetRequestedQueryPlan(catalog, options);
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
                InspectionQueryResults queryResults = queryPlan.Run(
                    new DiffQueryContext(
                        inputs.FromSurface,
                        inputs.ToSurface,
                        () => CreateBodySignalComparisonInput(inputs, options),
                        () => CreateImplementationComparisonInput(
                            inputs,
                            options)));
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
                        inspectionFailures);
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

                if (SelectsImplementationDiff(options) && !SelectsAnalysisDiff(options))
                {
                    var implementation = await BuildImplementationDiffWithSourceAsync(
                        queryResults.Get(
                            ImplementationComparisonQuery.Definition),
                        inputs.FromPaths,
                        inputs.ToPaths,
                        options,
                        context.HttpClient,
                        logger,
                        inputs.FromSurface,
                        inputs.ToSurface,
                        inputs.From.AssemblySet.Assemblies.FirstOrDefault(),
                        inputs.To.AssemblySet.Assemblies.FirstOrDefault());
                    var view = DiffOutputFormatter.BuildImplementationDiffView(
                        inputs.Name,
                        implementation.Local,
                        inputs.FromVersion,
                        inputs.ToVersion,
                        implementation.SelectedSource);
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
                    return inspectionFailures.Count > 0 ? 1 : 0;
                }

                if (SelectsAnalysisDiff(options))
                {
                    var analysis = BuildAnalysisDiff(
                        queryResults.Get(BodySignalComparisonQuery.Definition),
                        options);
                    var view = DiffOutputFormatter.BuildAnalysisDiffView(
                        inputs.Name,
                        analysis.Rows,
                        analysis.Summary,
                        inputs.FromVersion,
                        inputs.ToVersion,
                        decorateMember: !options.Jsonl);
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
                    return inspectionFailures.Count > 0 ? 1 : 0;
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
            logger);

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

    private static bool SelectsDetailedChanges(DiffOptions options)
        => options.IncludeSections?.Contains(DiffSections.Changes.Name) == true;

    internal static CompiledInspectionPlan<DiffQueryContext> GetRequestedQueryPlan(
        DiffSectionCatalog catalog,
        DiffOptions options)
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

        return catalog.Lens.Plan(
            Verbosity.Minimal,
            querySections);
    }

    private static async Task<bool> WriteSelectedDocumentAsync(
        DiffInputs inputs,
        DiffOptions options,
        InspectionQueryResults queryResults,
        HttpClient httpClient,
        VerboseLogger logger,
        IReadOnlyList<ApiDiffInspectionFailure>
            inspectionFailures)
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
        if (selected.Contains(DiffSections.Changes.Name))
        {
            changesDiff = BuildApiDiff(
                queryResults.Get(ApiComparisonQuery.Definition),
                inputs.FromSurface,
                inputs.ToSurface,
                options);
            changesView = DiffOutputFormatter.BuildDetailedChangesView(
                inputs.Name,
                ApplyFilters(changesDiff, options),
                inputs.FromVersion,
                inputs.ToVersion);
        }

        AnalysisDiffView? analysisView = null;
        if (selected.Contains(DiffSections.AnalysisDiff.Name))
        {
            var analysis = BuildAnalysisDiff(
                queryResults.Get(BodySignalComparisonQuery.Definition),
                options);
            analysisView = DiffOutputFormatter.BuildAnalysisDiffView(
                inputs.Name,
                analysis.Rows,
                analysis.Summary,
                inputs.FromVersion,
                inputs.ToVersion,
                decorateMember: false);
        }

        ImplementationDiffView? implementationView = null;
        if (selected.Contains(DiffSections.ImplementationDiff.Name))
        {
            var implementation = await BuildImplementationDiffWithSourceAsync(
                queryResults.Get(ImplementationComparisonQuery.Definition),
                inputs.FromPaths,
                inputs.ToPaths,
                options,
                httpClient,
                logger,
                inputs.FromSurface,
                inputs.ToSurface,
                inputs.From.AssemblySet.Assemblies.FirstOrDefault(),
                inputs.To.AssemblySet.Assemblies.FirstOrDefault());
            implementationView = DiffOutputFormatter.BuildImplementationDiffView(
                inputs.Name,
                implementation.Local,
                inputs.FromVersion,
                inputs.ToVersion,
                implementation.SelectedSource);
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
            inspectionFailures);

        if (options.JsonOutput)
        {
            Console.WriteLine(JsonSerializer.Serialize(view, DiffJsonContext.Default.DiffDocumentView));
            return inspectionFailures.Count > 0;
        }

        Console.WriteLine(DiffOutputFormatter.RenderDocumentView(
            view, OutputFormatter.CreateWindowedOptions(options.Rows)));
        return inspectionFailures.Count > 0;
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
        ApiSurface? toSurface = null)
    {
        var memberTargetIdentities = options.MemberFilter.Count == 0
            ? null
            : ResolveMemberTargetIdentities(
                fromSurface ?? AssemblySetSurfaceBuilder.Build(fromPaths, includeAll: options.IncludeAll) ?? new ApiSurface(),
                toSurface ?? AssemblySetSurfaceBuilder.Build(toPaths, includeAll: options.IncludeAll) ?? new ApiSurface(),
                options.MemberFilter,
                options.TypeFilter,
                requireBodyTargets: true).MemberIdentities;
        return new BodySignalComparisonInput(
            fromPaths
                .Select(path =>
                    MethodBodyInspectionSession.Open(path).BodyIndex)
                .ToArray(),
            toPaths
                .Select(path =>
                    MethodBodyInspectionSession.Open(path).BodyIndex)
                .ToArray(),
            options.TypeFilter,
            memberTargetIdentities);
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
        var memberTargetIdentities = options.MemberFilter.Count == 0
            ? null
            : ResolveMemberTargetIdentities(
                fromSurface ?? AssemblySetSurfaceBuilder.Build(fromPaths, includeAll: options.IncludeAll) ?? new ApiSurface(),
                toSurface ?? AssemblySetSurfaceBuilder.Build(toPaths, includeAll: options.IncludeAll) ?? new ApiSurface(),
                options.MemberFilter,
                options.TypeFilter,
                requireBodyTargets: true,
                bodySectionName: "Implementation Diff").MemberIdentities;

        return new ImplementationComparisonInput(
            fromPaths.Select(CreateImplementationAssemblyInput).ToArray(),
            toPaths.Select(CreateImplementationAssemblyInput).ToArray(),
            options.TypeFilter,
            memberTargetIdentities);
    }

    static ImplementationAssemblyInput CreateImplementationAssemblyInput(
        string path)
    {
        var assembly = ResolvedAssemblyReference.CreateFromPath(
            path,
            AssemblyResolutionProvenance.Local(
                "diff implementation comparison"));
        var session = MethodBodyInspectionSession.Open(assembly);
        return new(
            assembly,
            MetadataSource.DefaultAssemblyReferenceResolver(path),
            session.BodyIndex);
    }

    internal sealed record ImplementationDiffWithSource(
        ImplementationDiffResult Local,
        AssemblyMemberSourcePairResult? SelectedSource = null);

    internal static async Task<ImplementationDiffWithSource> BuildImplementationDiffWithSourceAsync(
        IReadOnlyList<string> fromPaths,
        IReadOnlyList<string> toPaths,
        DiffOptions options,
        HttpClient httpClient,
        VerboseLogger logger,
        ApiSurface? fromSurface = null,
        ApiSurface? toSurface = null,
        AssemblySetEntry? fromEntry = null,
        AssemblySetEntry? toEntry = null)
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
            fromEntry,
            toEntry);
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
            AssemblySetEntry? fromEntry = null,
            AssemblySetEntry? toEntry = null)
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
            using var workspace = new InspectionWorkspace();
            var before = CreateSourceParticipant(fromPaths[0], options, oldSide: true, fromEntry);
            var after = CreateSourceParticipant(toPaths[0], options, oldSide: false, toEntry);
            using var beforeGroup = workspace.CreateAssemblyContextGroup([before]);
            using var afterGroup = workspace.CreateAssemblyContextGroup([after]);
            var sourceContext = new AssemblyContextSourceQueryContext(
                httpClient,
                FileSystemPdbStore.CreateDefault(),
                new SourcePolicyPackageSourceAuthorization(options.SourceOptions),
                new SourceFetcher(DotnetInspector.Core.HttpClientFactory.SharedUntrustedFetch))
            {
                AllowAdjacentPdbReads = true,
                AllowLocalSourceReads = true,
                RepositoryPaths = options.SourceRepositories,
                NuGetSourceOptions = options.SourceOptions,
                Log = logger.Log,
            };
            var pair = await AssemblyContextMemberSourcePairQuery.ExecuteAsync(
                beforeGroup, before, afterGroup, after, request, sourceContext);
            return new(result, pair);
        }

        // Broader selections and targets without one exact MethodDef anchor
        // (including property/event accessor selections) retain legacy enrichment.
        if (result.Members.Count == 0)
            return new(result);

        var subjects = result.Members
            .Select(member => member.Subject)
            .ToDictionary(subject => subject.Id, StringComparer.Ordinal);
        var from = await AcquirePdbSourceInspectionsAsync(
            fromPaths,
            subjects,
            options,
            oldSide: true,
            httpClient,
            logger);
        var to = await AcquirePdbSourceInspectionsAsync(
            toPaths,
            subjects,
            options,
            oldSide: false,
            httpClient,
            logger);
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
            if (candidate.Member != target.Anchor
                || (request is not null
                    && (!request.Type.Equals(candidate.Type) || request.Member != candidate.Member)))
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

    internal sealed record PdbSourceInspectionBatch(
        ImmutableDictionary<string, FindingInspection<string>> Inspections,
        ImmutableArray<string> IndexingFailures);

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
                    ILInspector.Text.TextFindings.LineDescriptor,
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
        VerboseLogger logger)
    {
        var results = new Dictionary<string, FindingInspection<string>>(StringComparer.Ordinal);
        var indexingFailures = ImmutableArray.CreateBuilder<string>();
        var fetcher = new SourceFetcher(DotnetInspector.Core.HttpClientFactory.SharedUntrustedFetch);
        var (packageName, packageVersion) = DiffPackageIdentity(options, oldSide);

        foreach (string path in paths)
        {
            LibraryBodyIndex index;
            try
            {
                index = MethodBodyInspectionSession.Open(
                        path,
                        includeAllocations: false,
                        includeOpportunities: false)
                    .BodyIndex;
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

            foreach (string failure in PdbSourceDeclarationIndexFailures(
                path,
                index.DeclaredMethods,
                index.Diagnostics))
            {
                logger.Log(failure);
                indexingFailures.Add(failure);
            }

            var targets = index.DeclaredMethods
                .Select(method => (
                    Method: method,
                    Subject: ResearchMemberIdentity.SubjectFromMethod(method)))
                .Where(item => subjects.ContainsKey(item.Subject.Id))
                .Where(item => !results.ContainsKey(item.Subject.Id))
                .ToArray();
            if (targets.Length == 0)
                continue;

            try
            {
                using var source = SourceLinkService.Open(path, logger.Log);
                if (source.Context.NeedsPdb)
                {
                    await SourceEnricher.AcquirePdbAsync(
                        source.Context,
                        httpClient,
                        packageName,
                        packageVersion,
                        isPlatformAssembly: options.PlatformVersionRange is not null,
                        logger.Log,
                        sourceOptions: options.SourceOptions);
                }

                foreach (var target in targets)
                {
                    var subject = subjects[target.Subject.Id];
                    var inspection = await PdbSourceAcquisition.AcquireMemberAsync(
                        source,
                        target.Method.MetadataToken,
                        target.Method.Name,
                        new FindingSubject(subject.Id, subject.Display),
                        fetcher,
                        options.SourceRepositories);
                    results[subject.Id] = inspection.Lines;
                }
            }
            catch (Exception ex) when (ex is IOException
                or UnauthorizedAccessException
                or BadImageFormatException
                or InvalidOperationException
                or HttpRequestException)
            {
                foreach (var target in targets)
                {
                    var subject = subjects[target.Subject.Id];
                    results[subject.Id] = new FindingInspection<string>.Failed(
                        new InspectionError(
                            new FindingSubject(subject.Id, subject.Display),
                            ILInspector.Text.TextFindings.LineDescriptor,
                            $"PDB-source acquisition failed ({ex.GetType().Name}): {ex.Message}"));
                }
            }
        }

        return new PdbSourceInspectionBatch(
            results.ToImmutableDictionary(StringComparer.Ordinal),
            indexingFailures.ToImmutable());
    }

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

    private static bool MatchesAnyDiffTypeFilter(string typeFullName, IEnumerable<string> filters)
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
        => BuildRetainedFindingTransitions<AllocationOccurrence>(
            fromPaths,
            toPaths,
            fromSurface,
            toSurface,
            fromVersion,
            toVersion,
            options,
            ResearchChangeMechanism.BodySignals,
            emitEmptyComparison: false,
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
        => BuildRetainedFindingTransitions<DirectCall>(
            fromPaths,
            toPaths,
            fromSurface,
            toSurface,
            fromVersion,
            toVersion,
            options,
            ResearchChangeMechanism.BodySignals,
            emitEmptyComparison: false,
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
        => BuildRetainedFindingTransitions<UnsafetyOccurrence>(
            fromPaths,
            toPaths,
            fromSurface,
            toSurface,
            fromVersion,
            toVersion,
            options,
            ResearchChangeMechanism.BodySignals,
            emitEmptyComparison: false,
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
        if (options.MemberFilter.Count != 1)
        {
            throw new InvalidOperationException(
                $"--finding {descriptor.Id} requires exactly one --member target.");
        }

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
        return research.RetainedComparisons.Get<T>(descriptor)
            .SelectMany(comparison => RetainedComparisonRows(
                comparison,
                fromVersion,
                toVersion,
                emitEmptyComparison,
                toTransitionRow))
            .OrderBy(row => row.Target, StringComparer.Ordinal)
            .ThenBy(row => row.Transition, StringComparer.Ordinal)
            .ToList();
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
        HashSet<string> TypeNames);

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
        string bodySectionName = "Analysis Diff")
    {
        HashSet<string> identities = new(StringComparer.Ordinal);
        HashSet<string> typeNames = new(StringComparer.Ordinal);
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
                var oldResult = AddResolvedIdentities(oldType, parsed.Selector, identities);
                found |= oldResult.Found;
                bodyFound |= oldResult.BodyFound;
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
                var newResult = AddResolvedIdentities(newType, parsed.Selector, identities);
                found |= newResult.Found;
                bodyFound |= newResult.BodyFound;
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

        return new ResolvedDiffMemberTargets(identities, typeNames);
    }

    static (bool Found, bool BodyFound, MemberTargetDiagnostic? Diagnostic) AddResolvedIdentities(ApiType type, MemberTargetSelector selector, HashSet<string> identities)
    {
        var resolution = MemberTargetResolver.Resolve(type, selector);
        if (!resolution.Found)
            return (false, false, resolution.Diagnostic);

        identities.Add(resolution.Target!.Anchor.StableSelector);
        identities.Add(resolution.Target.Anchor.CanonicalSignature);
        var bodyFound = AddResearchBodyIdentity(resolution.Target, identities);
        return (true, bodyFound, null);
    }

    internal static bool AddResearchBodyIdentity(ResolvedMemberTarget target, HashSet<string> identities)
        => ResearchMemberIdentity.TryAddTargetIdentity(target, identities);

    static bool IsFatalTargetDiagnostic(MemberTargetDiagnosticKind kind)
        => kind is MemberTargetDiagnosticKind.AmbiguousMember
            or MemberTargetDiagnosticKind.DigestAmbiguous
            or MemberTargetDiagnosticKind.ConflictingSelectors;

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
public record DiffOptions
{
    public string? PackageVersionRange { get; init; }
    public string? PlatformVersionRange { get; init; }
    public string? LibraryVersionRange { get; init; }
    public string? Framework { get; init; }
    public string? Tfm { get; init; }
    public bool IncludeAll { get; init; }
    public bool Verbose { get; init; }
    public HashSet<string> TypeFilter { get; init; } = [];
    public HashSet<string> MemberFilter { get; init; } = [];
    public bool Tabular { get; init; }
    public bool Tsv { get; init; }
    public bool Jsonl { get; init; }
    public bool JsonOutput { get; init; }
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
    public bool Tree { get; init; }
    public string[]? Select { get; init; }

    /// <summary>
    /// Bare <c>-S</c>: a request for this command's default preset rather than for any named
    /// section or category. Tracked separately from <see cref="Select"/> so the marker is never
    /// spellable as a selector value. See #3547.
    /// </summary>
    public bool SelectDefault { get; init; }
    public HashSet<string>? IncludeSections { get; init; }
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
    public bool IsRawOutput => Tabular || Jsonl || JsonOutput || NoHeader || NameOnly;
}
