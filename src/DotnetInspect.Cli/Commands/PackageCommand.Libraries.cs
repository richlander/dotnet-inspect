using DotnetInspect.Cli.Models;
using ILInspector.Metadata;
using System.Text.Json;
using System.Text.Json.Serialization;
using SemanticRowSelection =
    DotnetInspect.Cli.CommandLine.CliSemanticRowSelection;
using DotnetInspect.Cli.Inspectors;
using DotnetInspect.Cli.Options;
using DotnetInspect.Cli.Output;
using DotnetInspector.Packages;
using DotnetInspect.Cli.Planning;
using DotnetInspector.Queries;
using QuerySpace.Rows;
using NuGetFetch;
using PackageExtractor = DotnetInspector.Packages.PackageExtractor;
using DotnetInspector.Sections;
using DotnetInspect.Cli.Sections;
using DotnetInspector.Services;
using DotnetInspect.Cli.Services;
using DotnetInspect.Cli.Views;
using InertText;
using Inspector.Findings;
using Markout;
using System.Buffers;
using System.Globalization;
using System.Text;

namespace DotnetInspect.Cli.Commands;

public partial class PackageCommand
{

    private static bool IsNetworkUsingPackageSection(string section) =>
        section.Equals(PackageSections.Signals, StringComparison.OrdinalIgnoreCase)
        || section.Equals(
            PackageSections.AuditIdentifierConfusion,
            StringComparison.OrdinalIgnoreCase)
        || section.Equals(PackageSections.Statistics, StringComparison.OrdinalIgnoreCase)
        || section.Equals(PackageSections.Vulnerabilities, StringComparison.OrdinalIgnoreCase);

    internal static bool AllowsVulnerabilityTraffic(InspectionOptions options) =>
        options.Verbosity >= Verbosity.Detailed
        || options.IncludeSections?.Any(IsNetworkUsingPackageSection) == true;

    internal static OptionError? GetLibraryInspectionModeError(
        InspectionOptions options,
        bool allowStaticDiscovery = false)
    {
        if (options.AllLibraries)
            return GetPackageAllLibrariesModeError(options, allowStaticDiscovery);
        if (options.PackageLibrary is not null)
            return GetPackageLibraryModeError(options);
        return null;
    }

    private static OptionError? GetPackageLibraryModeError(InspectionOptions options)
    {
        if (options.Tree
            && !options.Count
            && options.Discover == null
            && (options.Format != OutputFormat.Markdown
                || options.Bare
                || options.Tabular
                || options.Tsv
                || options.Jsonl
                || options.JsonArray
                || options.NoHeader))
        {
            return new OptionError("--tree cannot be combined with row projections or non-Markdown formats.");
        }

        List<string> conflicts = [];
        if (options.AllLibraries) conflicts.Add("Library aggregate");
        if (options.ListLayout || options.ListLayoutExplicitlySet)
            conflicts.Add("--layout");
        if (HasPathFilter(options)) conflicts.Add("--path");
        if (options.ListTfms) conflicts.Add("--tfms");
        if (options.ListVersions) conflicts.Add("--versions/--version");
        if (options.Print) conflicts.Add("--print");
        if (options.Roots) conflicts.Add("--roots");
        if (options.ShowDependencies) conflicts.Add("--dependencies");
        if (string.Equals(options.Tfm, "all", StringComparison.OrdinalIgnoreCase)) conflicts.Add("--tfm all");

        if (conflicts.Count == 0)
            return null;

        return new OptionError($"--library cannot be combined with {string.Join(", ", conflicts)}.");
    }

    private static OptionError? GetPackageAllLibrariesModeError(
        InspectionOptions options,
        bool allowStaticDiscovery = false)
    {
        List<string> conflicts = [];
        if (options.PackageLibrary != null) conflicts.Add("--library");
        if (options.ListLayout || options.ListLayoutExplicitlySet)
            conflicts.Add("--layout");
        if (HasPathFilter(options)) conflicts.Add("--path");
        if (options.ListTfms) conflicts.Add("--tfms");
        if (options.ListVersions) conflicts.Add("--versions/--version");
        if (options.Print) conflicts.Add("--print");
        if (options.Roots) conflicts.Add("--roots");
        if (options.ShowDependencies) conflicts.Add("--dependencies");
        if (options.Trace) conflicts.Add("--trace");
        if (options.MetadataRoot != MetadataRootKind.Cli)
            conflicts.Add("--metadata-root");
        if (options.ExtractResources is not null)
            conflicts.Add("--extract-resources");
        if (options.ReferenceHierarchyDepth is not null)
            conflicts.Add("--depth");
        if (options.Discover != null
            && !allowStaticDiscovery)
        {
            conflicts.Add("-D/--discover");
        }
        if (options.Tree && options.Discover == null && !options.Count) conflicts.Add("--tree");
        if (options.Columns != null) conflicts.Add("--columns");
        if (options.Fields != null) conflicts.Add("--fields");

        if (conflicts.Count > 0)
        {
            return new OptionError(
                $"Library aggregate inspection cannot be combined with "
                    + $"{string.Join(", ", conflicts)}. Narrow with "
                    + "--library <dll> or --namesake-library.");
        }

        return null;
    }

    private static async Task<int> ExecutePackageLibraryAsync(
        string extractPath,
        bool isLocalFile,
        string packageArg,
        string packageName,
        string version,
        PackageExtractionResult extraction,
        InspectionOptions options)
    {
        var selected = ResolvePackageLibrary(extractPath, packageName, version, options);
        if (selected == null)
            return 1;

        var packageReference = isLocalFile
            ? packageArg
            : !string.IsNullOrWhiteSpace(version)
                ? $"{packageName}@{version}"
                : packageName;

        return await LibraryCommand.ExecuteResolvedPackageAsync(
            CreateLibraryOptions(
                assemblyName: Path.GetRelativePath(
                        extractPath,
                        selected.Path)
                    .Replace('\\', '/'),
                packageReference,
                options),
            extraction).ConfigureAwait(false);
    }

    private static async Task<int> ExecutePackageAllLibrariesAsync(
        HttpClient httpClient,
        string extractPath,
        bool isLocalFile,
        string packageArg,
        string packageName,
        string version,
        PackageExtractionResult extraction,
        string? nuspecPackageId,
        string? nuspecVersion,
        PackageRootBinding? admittedPackageRoot,
        InspectionOptions options)
    {
        PackageLibrarySelectionResult? selectionResult =
            ResolveAllPackageLibraries(
                extractPath,
                packageName,
                version,
                options);
        if (selectionResult == null)
            return 1;
        IReadOnlyList<PackageLibrarySelection> selected =
            selectionResult.Libraries;

        var packageReference = isLocalFile
            ? packageArg
            : !string.IsNullOrWhiteSpace(version)
                ? $"{packageName}@{version}"
                : packageName;

        var catalog = LibrarySections.CreateCatalog();
        var sectionCatalog = catalog.Sections;
        var pipeline = catalog.Pipeline;
        var queryCatalog = catalog.QueryCatalog;
        var libraryOptions = CreateLibraryOptions(assemblyName: null, packageReference, options);

        libraryOptions = LibraryCommand.NormalizeBareSelect(libraryOptions);
        libraryOptions = libraryOptions with
        {
            UserVerbosityOverride = libraryOptions.Verbosity,
        };

        var selectResult = SelectResolver.ResolveSelectAsSections(
            libraryOptions.Select,
            sectionCatalog.SelectableSectionNames,
            sectionCatalog.InfoSectionNames,
            sectionCatalog.SelectionCategoryMap,
            selectDefault: libraryOptions.SelectDefault);
        if (SelectOutput.WriteUnresolved(selectResult)) return 1;
        if (selectResult.Sections != null)
        {
            if (LibraryCommand.ApplyCoordinateSectionRequirements(
                    libraryOptions,
                    selectResult) is { } coordinateError)
            {
                CommandError.Write(coordinateError);
                return 1;
            }

            libraryOptions = libraryOptions with
            {
                IncludeSections = selectResult.Sections,
                ExactIncludeSectionsOverride = selectResult.ExactSections,
            };
        }
        if (libraryOptions.IncludeSections?.Contains(
                SectionNames.ReferenceHierarchy) == true)
        {
            CommandError.Write(
                "Reference Hierarchy requires one exact library. Use --library <assembly>.");
            return 1;
        }

        if (libraryOptions.Count)
        {
            if (!CountOutput.ValidateSectionsSelected(
                    libraryOptions.IncludeSections, libraryOptions.FixedOverview))
            {
                return 1;
            }

            var ordered = OutputFormatter.ResolveCountMapSections(
                pipeline, libraryOptions.IncludeSections, libraryOptions.FixedOverview);
            if (!CountOutput.ValidateMapFormat(
                    libraryOptions.Format, ordered, libraryOptions.Tree))
                return 1;
        }

        var requiredVerbosity = pipeline.GetRequiredVerbosity(libraryOptions.IncludeSections);
        if (requiredVerbosity > libraryOptions.Verbosity)
            libraryOptions = libraryOptions with { Verbosity = requiredVerbosity };

        var candidates = pipeline.GetCandidateSections(
            libraryOptions.Verbosity,
            libraryOptions.IncludeSections,
            libraryOptions.FixedOverview);
        libraryOptions = libraryOptions with
        {
            CollectIdentifierConfusionReferenceTree =
                candidates.Contains(SectionNames.IdentifierConfusion),
        };

        SectionQueryPlan sectionPlan = sectionCatalog.PlanQueries(
            libraryOptions.Verbosity,
            libraryOptions.IncludeSections,
            libraryOptions.FixedOverview);
        List<HostQueryDemand> commandQueryDemand = [];
        if (sectionPlan.Queries.Contains(BodyShapesQuery.Definition)
            && libraryOptions.BodyKindQuery.HasFilter
            && libraryOptions.PerformanceTriage.HasCandidateFilters)
        {
            commandQueryDemand.Add(
                new HostQueryDemand(
                    "Body Shapes performance predicates",
                    OptimizationOpportunitiesQuery.Definition));
        }
        HashSet<InspectionQueryDefinition> queries =
            sectionPlan.Activate(commandDemand: commandQueryDemand);
        var context = new CommandContext(options.Verbose);
        var logger = context.Logger;
        bool requiresGroupedIntegrations =
            RequiresGroupedIntegrations(
                queries,
                out bool includeIntegrationOpportunities);
        InspectionQueryPlan<InspectionQueryContext> queryPlan =
            queryCatalog.Plan(queries);
        PackageIntegrationAssembly[] integrationAssemblies =
        [
            .. selected.Select(selection =>
            {
                string relativePath = Path.GetRelativePath(
                        extractPath,
                        selection.Path)
                    .Replace('\\', '/');
                return CreatePackageIntegrationAssembly(
                    selection.Path,
                    relativePath);
            }),
        ];
        PackageIntegrationsWorkspace? createdIntegrationsWorkspace = null;
        if (requiresGroupedIntegrations)
        {
            PackageInspectionInput input;
            PackageRootBinding? packageRoot = null;
            if (admittedPackageRoot is not null)
            {
                packageRoot = admittedPackageRoot;
                input = PackageInspectionInput.CreateFromBinding(
                    admittedPackageRoot);
            }
            else if (isLocalFile)
            {
                input = PackageInspectionInput.CreateLocal(
                    new FileSystemPackageContent(
                        extractPath,
                        extraction.NupkgPath,
                        extraction.FromCache,
                        extraction.ProducerKey ?? "local-package"),
                    nuspecPackageId,
                    nuspecVersion);
            }
            else if (extraction.AcquiredPayload is { } acquired)
            {
                input = PackageInspectionInput.CreateFromPayload(acquired);
            }
            else
            {
                if (extraction.Authority is not null)
                {
                    CommandError.Write(
                        "The selected package authority did not retain its acquired payload.");
                    return 1;
                }
                (packageRoot, string? acquisitionFailure) =
                    await AcquirePackageRootBindingAsync(
                        httpClient,
                        packageName,
                        version,
                        selectionResult.TargetFramework,
                        extraction.ProducerKey,
                        options,
                        logger.Log);
                if (packageRoot is null)
                {
                    CommandError.Write(
                        acquisitionFailure
                        ?? "The package artifact workspace could not be acquired.");
                    return 1;
                }
                input = PackageInspectionInput.CreateFromBinding(packageRoot);
            }

            if (packageRoot is not null
                && ShouldUsePackageCompileRoles(
                    isLocalFile,
                    selectionResult.TargetFramework,
                    extraction.ProducerKey,
                    selected.Select(selection =>
                        Path.GetRelativePath(extractPath, selection.Path)
                            .Replace('\\', '/'))))
            {
                createdIntegrationsWorkspace =
                    await PackageIntegrationsWorkspace
                        .TryCreateArtifactBackedAsync(
                            integrationAssemblies,
                            extractPath,
                            packageRoot,
                            includeIntegrationOpportunities);
                if (createdIntegrationsWorkspace is not null)
                {
                    logger.Log(
                        $"Using artifact-backed package Integrations for {selectionResult.TargetFramework}.");
                }
            }
            if (createdIntegrationsWorkspace is null)
            {
                createdIntegrationsWorkspace =
                    await PackageIntegrationsWorkspace.CreateSelectedAsync(
                        integrationAssemblies,
                        extractPath,
                        input,
                        includeIntegrationOpportunities:
                            includeIntegrationOpportunities);
                if (createdIntegrationsWorkspace.ContextGroupCount > 0)
                    logger.Log("Using artifact-backed selected-entry package Integrations.");
            }
        }

        await using PackageIntegrationsWorkspace? integrationsWorkspace =
            createdIntegrationsWorkspace;
        List<LibraryInspection> inspections = [];
        List<(string FileName, string Reason)> groupedIntegrationsFailures = [];
        List<(string FileName, IdentifierConfusionAuditFailureKind FailureKind)>
            identifierAuditFailures = [];
        List<(string FileName, CandidateOpenFailure Failure)>
            descriptorSelectionFailures = [];
        foreach (var selection in selected)
        {
            string relativePath = Path.GetRelativePath(
                    extractPath,
                    selection.Path)
                .Replace('\\', '/');
            AssemblyResolutionProvenance provenance =
                string.IsNullOrWhiteSpace(packageName)
                || string.IsNullOrWhiteSpace(version)
                    ? AssemblyResolutionProvenance.Local(
                        "package Library aggregate")
                    : AssemblyResolutionProvenance.Package(
                        packageName,
                        version,
                        TfmResolver.ExtractTfmFromPath(relativePath),
                        rid: null);
            LibraryInspectionSubjectSelection subjectSelection =
                LibraryInspectionSubject.Select(
                    selection.Path,
                    provenance);
            if (subjectSelection
                is LibraryInspectionSubjectSelection.Rejected rejected)
            {
                descriptorSelectionFailures.Add(
                    (relativePath, rejected.Failure));
                CommandError.WriteWarning(
                    $"Could not select library descriptor for "
                    + $"'{selection.Path}': {rejected.Failure.Detail}");
                continue;
            }
            LibraryInspectionSubject subject =
                ((LibraryInspectionSubjectSelection.Ready)subjectSelection)
                    .Subject;

            Task<LibraryInspection?> InspectAsync(
                ResolvedAssemblyReference? assemblyReference,
                AssemblyIntegrationsEntry? integrations,
                AssemblyIntegrationOpportunitiesEntry? opportunities)
            {
                return LibraryMetadataService.InspectAsync(
                    selection.Path,
                    libraryOptions,
                    logger,
                    packageName,
                    version,
                    context.HttpClient,
                    queryPlan: queryPlan,
                    assemblyReference:
                        assemblyReference
                        ?? subject.AssemblyReference,
                    integrationsEntry: integrations,
                    integrationOpportunitiesEntry: opportunities);
            }

            LibraryInspection? inspection;
            try
            {
                inspection =
                    integrationsWorkspace is null
                        ? await InspectAsync(null, null, null)
                        : await InspectGroupedAssemblyAsync(
                            integrationsWorkspace,
                            selection.Path,
                            relativePath,
                            groupedIntegrationsFailures,
                            InspectAsync);
            }
            catch (LibraryMetadataService.IdentifierConfusionReferenceTraversalException ex)
            {
                identifierAuditFailures.Add(
                    (
                        relativePath,
                        ex.FailureKind));
                continue;
            }

            if (inspection == null)
            {
                logger.LogWarning($"Could not read library: {Path.GetFileName(selection.Path)}");
                continue;
            }

            inspection.FileName = relativePath;
            inspection.Tfm =
                TfmResolver.ExtractFrameworkFolderFromPath(relativePath);
            inspection.Source = SourceKind.NuGet;
            inspections.Add(inspection);
        }

        bool integrationsIncomplete =
            integrationsWorkspace is not null
            && WriteGroupedIntegrationsFailures(
                groupedIntegrationsFailures);
        identifierAuditFailures.AddRange(
            inspections
                .Where(
                    inspection =>
                        inspection.IdentifierConfusionFailure is not null)
                .Select(
                    inspection =>
                        (
                            inspection.FileName,
                            inspection.IdentifierConfusionFailure!.Value)));
        bool identifierAuditIncomplete =
            WriteIdentifierAuditFailures(identifierAuditFailures);
        int completionExitCode =
            AllLibrariesCompletionExitCode(
                integrationsIncomplete
                    || identifierAuditIncomplete
                    || descriptorSelectionFailures.Count > 0,
                libraryOptions,
                pipeline,
                [.. inspections]);

        if (inspections.Count == 0)
        {
            CommandError.Write($"No libraries could be read from package '{packageName}'.");
            return 1;
        }

        if (libraryOptions.JsonOutput && !libraryOptions.Count)
        {
            string json = JsonSerializer.Serialize(
                inspections.ToArray(),
                JsonContext.Default.LibraryInspectionArray);
            OutputDestination.Write(
                libraryOptions.OutputPath,
                libraryOptions.Rows,
                writer => writer.WriteLine(json));
            return completionExitCode;
        }

        var sections = GetAllLibrariesSections(inspections, libraryOptions, pipeline);
        bool tabularOutput =
            libraryOptions.TabularExplicitlySet
            && !libraryOptions.Count;
        if (tabularOutput
            && libraryOptions.Select?.Any(
                value => SelectResolver.TryResolveCategory(
                    value,
                    sectionCatalog.SelectionCategoryMap,
                    sectionCatalog.SelectableSectionNames,
                    out _,
                    out _)) == true)
        {
            CommandError.Write($"Library aggregate row output requires one concrete section; category selectors such as {SectionCategoryNames.Integrations} produce multi-section documents.");
            CommandError.WriteLine("Use Markdown output for categories, or select Integrations, Integration Opportunities, or Library Info.");
            return 1;
        }

        if (tabularOutput
            && libraryOptions.IncludeSections is { Count: > 0 })
        {
            var candidateSections = pipeline.GetCandidateSections(
                libraryOptions.Verbosity,
                libraryOptions.IncludeSections,
                libraryOptions.FixedOverview);
            string? unsupportedSection = candidateSections.FirstOrDefault(
                section => !SupportsAllLibrariesTableSection(section));
            if (unsupportedSection is not null)
            {
                CommandError.Write($"Library aggregate row output does not support section: {unsupportedSection}.");
                CommandError.WriteLine("Use Markdown output, or select Library Info, Switches, Integrations, or Integration Opportunities.");
                return 1;
            }
        }

        if (libraryOptions.Count)
        {
            if (sections.Count == 0)
                CommandError.WriteNote("matched sections have no data across all libraries.");

            var projection = CaptureAllLibrariesCounts(
                inspections,
                sections,
                libraryOptions,
                pipeline);
            var ordered = OutputFormatter.ResolveCountMapSections(
                pipeline, libraryOptions.IncludeSections, libraryOptions.FixedOverview);
            CountOutput.Write(
                projection,
                ordered,
                libraryOptions.Format,
                libraryOptions.NoHeader,
                libraryOptions.OutputPath,
                libraryOptions.Rows);
            return completionExitCode;
        }

        if (sections.Count == 0)
        {
            CommandError.WriteNote("matched sections have no data across all libraries.");
            OutputDestination.Write(
                libraryOptions.OutputPath,
                libraryOptions.Rows,
                static _ => { });
            return completionExitCode;
        }

        if (tabularOutput)
        {
            if (!WriteAllLibrariesTable(
                    packageName,
                    version,
                    inspections,
                    sections,
                    libraryOptions))
            {
                return 1;
            }
            return completionExitCode;
        }

        var markdown = RenderAllLibrariesMarkdown(packageName, version, inspections, sections, libraryOptions, pipeline);
        OutputDestination.Write(
            libraryOptions.OutputPath,
            libraryOptions.Rows,
            writer => OutputFormatter.WriteLfLine(writer, markdown));
        return completionExitCode;
    }

    internal static Task<LibraryInspection?>
        InspectGroupedAssemblyAsync(
            PackageIntegrationsWorkspace workspace,
            string path,
            string relativePath,
            ICollection<(string FileName, string Reason)> failures,
            Func<
                ResolvedAssemblyReference?,
                AssemblyIntegrationsEntry?,
                AssemblyIntegrationOpportunitiesEntry?,
                Task<LibraryInspection?>> inspectAsync)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
        ArgumentNullException.ThrowIfNull(failures);
        ArgumentNullException.ThrowIfNull(inspectAsync);

        if (workspace.HasNoAssembly(path))
            // No Integration participant exists; ordinary native/metadata inspection still applies.
            return inspectAsync(null, null, null);

        if (workspace.TryGetPreflightFailure(
                path,
                out string preflightFailure))
        {
            failures.Add((relativePath, preflightFailure));
            return Task.FromResult<LibraryInspection?>(null);
        }

        return workspace.UseAssemblyAsync(
            path,
            async (retainedAssembly, integrations, opportunities) =>
            {
                switch (integrations)
                {
                    case AssemblyIntegrationsEntry.Rejected rejected:
                        failures.Add(
                            (relativePath, rejected.Failure.Detail));
                        if (retainedAssembly is null)
                            return null;
                        break;
                    case AssemblyIntegrationsEntry.Failed failed:
                        failures.Add(
                            (relativePath, failed.Error.Message));
                        break;
                }

                switch (opportunities)
                {
                    case AssemblyIntegrationOpportunitiesEntry.Rejected
                        rejected:
                        failures.Add(
                            (relativePath, rejected.Failure.Detail));
                        break;
                    case AssemblyIntegrationOpportunitiesEntry.Failed failed:
                        failures.Add(
                            (relativePath, failed.Error.Message));
                        break;
                }

                LibraryInspection? inspection =
                    await inspectAsync(
                            retainedAssembly,
                            integrations,
                            opportunities)
                        .ConfigureAwait(false);
                if (inspection is not null
                    && retainedAssembly?.Registration
                        .ArtifactRegistration is not null)
                {
                    inspection.LastModified =
                        File.GetLastWriteTimeUtc(path);
                }

                return inspection;
            });
    }

    internal static bool RequiresGroupedIntegrations(
        HashSet<InspectionQueryDefinition> queries,
        out bool includeIntegrationOpportunities)
    {
        includeIntegrationOpportunities = queries.Remove(
            AssemblyContextIntegrationOpportunitiesQuery.Definition);
        return queries.Remove(
                AssemblyContextIntegrationsQuery.Definition)
            || includeIntegrationOpportunities;
    }

    internal static PackageIntegrationAssembly
        CreatePackageIntegrationAssembly(
            string path,
            string relativePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
        return new PackageIntegrationAssembly(
            path,
            TfmResolver.ExtractFrameworkFolderFromPath(relativePath),
            TfmResolver.ExtractAssetDirectoryFromPath(relativePath));
    }

    internal static bool WriteGroupedIntegrationsFailures(
        IEnumerable<(string FileName, string Reason)> groupedFailures)
    {
        ArgumentNullException.ThrowIfNull(groupedFailures);

        var failures = groupedFailures
            .Distinct()
            .ToList();

        foreach (var (fileName, reason) in failures)
        {
            CommandError.WriteWarning(
                $"Integrations inspection failed for '{fileName}': {reason}");
        }

        return failures.Count > 0;
    }

    internal static bool WriteIdentifierAuditFailures(
        IEnumerable<(
            string FileName,
            IdentifierConfusionAuditFailureKind FailureKind)> auditFailures)
    {
        ArgumentNullException.ThrowIfNull(auditFailures);

        var failures = auditFailures
            .Distinct()
            .ToList();

        foreach (var (fileName, failureKind) in failures)
        {
            CommandError.WriteWarning(
                $"Identifier audit failed for '{fileName}': "
                + IdentifierConfusionAudit.DescribeFailure(failureKind));
        }

        return failures.Count > 0;
    }

    internal static int AllLibrariesCompletionExitCode(
        bool incomplete) =>
        incomplete ? 1 : 0;

    internal static int AllLibrariesCompletionExitCode(
        bool incomplete,
        LibraryOptions options,
        SectionPipeline<LibraryInspection> pipeline,
        params LibraryInspection[] inspections) =>
        Math.Max(
            AllLibrariesCompletionExitCode(incomplete),
            LibraryCommand.SelectedInspectionFailureExitCode(
                options,
                pipeline,
                inspections));

    internal static bool RequiresPackageMetadata(
        InspectionOptions options,
        SectionPipeline<InspectionResult> pipeline,
        bool includeSignals = true)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(pipeline);

        return RequiresIdentifierMetadata(
                   options,
                   pipeline,
                   includeSignals)
               || RequestsSelectedOrDiscoveredSection(
                   options,
                   PackageSections.Statistics,
                   pipeline)
               || RequestsSelectedOrDiscoveredSection(
                   options,
                   PackageSections.Vulnerabilities,
                   pipeline);
    }

    internal static bool RequiresIdentifierMetadata(
        InspectionOptions options,
        SectionPipeline<InspectionResult> pipeline,
        bool includeSignals = true)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(pipeline);

        return (includeSignals
                && RequestsSelectedOrDiscoveredSection(
                    options,
                    PackageSections.Signals,
                    pipeline))
               || RequestsSelectedOrDiscoveredSection(
                   options,
                   PackageSections.AuditIdentifierConfusion,
                   pipeline);
    }

    private static LibraryOptions CreateLibraryOptions(string? assemblyName, string packageReference, InspectionOptions options)
        => new()
        {
            AssemblyName = assemblyName,
            IncludeMetadata = true,
            PackagePath = packageReference,
            ReferenceHierarchyDepth = options.ReferenceHierarchyDepth,
            IncludePrerelease = options.IncludePrerelease,
            Tfm = options.Tfm,
            TypeFilter = options.TypeFilter,
            PreferRenderedUrls = options.PreferRenderedUrls,
            JsonOutput = options.JsonOutput,
            PlainText = options.Format == OutputFormat.PlainText,
            Tabular = options.Tabular,
            Tsv = options.Tsv,
            Jsonl = options.Jsonl,
            TabularExplicitlySet = options.TabularExplicitlySet,
            FormatExplicitlySet = options.FormatExplicitlySet,
            Format = options.Format,
            Verbose = options.Verbose,
            Trace = options.Trace,
            Verbosity = options.Verbosity,
            IncludeSections = options.IncludeSections,
            Discover = options.Discover,
            DiscoverDetails = options.DiscoverDetails,
            Effective = options.Effective,
            Tree = options.Tree,
            Select = options.Select,
            SelectDefault = options.SelectDefault,
            Columns = options.Columns,
            Fields = options.Fields,
            FieldsExplicitlySet = options.FieldsExplicitlySet,
            Schema = options.Schema,
            Count = options.Count,
            OutputPath = options.OutputPath,
            Print = options.Print,
            Value = options.Value,
            Urls = options.Urls,
            Paths = options.Paths,
            JsonArray = options.JsonArray,
            ProjectionRow = options.PrintRow,
            Rows = options.CloneCandidateRowSelection is null
                ? options.Rows
                : null,
            CloneCandidateRowSelection =
                options.CloneCandidateRowSelection,
            ReferenceRowSelection =
                options.ReferenceRowSelection,
            EcosystemDependencyRowSelection =
                options.EcosystemDependencyRowSelection,
            IntegrationQuery = options.IntegrationQuery,
            MetadataRoot = options.MetadataRoot,
            PerformanceTriage = options.PerformanceTriage,
            BodyKindQuery = options.BodyKindQuery,
            CloneCandidateQuery = options.CloneCandidateQuery,
            SourceOptions = options.SourceOptions,
            ExtractResources = options.ExtractResources,
            NoHeader = options.NoHeader,
            UserVerbosityOverride = options.Verbosity
        };

    private static PackageLibrarySelection? ResolvePackageLibrary(
        string extractPath,
        string packageName,
        string version,
        InspectionOptions options)
    {
        var requestedLibrary = options.PackageLibrary;
        if (requestedLibrary == null)
            return null;
        var packageId = PackageExtractor.ParsePackageReference(packageName).name;
        if (!TryResolveWorkspaceLibraryPaths(
                extractPath,
                options,
                out IReadOnlyList<string>? workspacePaths))
        {
            return null;
        }
        TfmSelector.PackageLibraryResolution resolution;
        if (options.NamesakeLibrary)
        {
            TfmSelector.PackageLibraryResolution candidates =
                workspacePaths is null
                    ? TfmSelector.SelectPackageLibraries(
                        extractPath,
                        options.Tfm)
                    : new TfmSelector.PackageLibraryResolution(
                        workspacePaths,
                        options.Tfm,
                        TfmSelector.PackageLibraryResolutionStatus.Selected,
                        workspacePaths);
            resolution = candidates.IsSelected
                ? TfmSelector.SelectPackageLibrary(
                    candidates.Paths,
                    extractPath,
                    packageId,
                    requestedLibrary: null,
                    tfm: candidates.Tfm)
                : candidates;
        }
        else
        {
            resolution =
                workspacePaths is null
                    ? TfmSelector.SelectPackageLibrary(
                        extractPath,
                        packageId,
                        requestedLibrary,
                        options.Tfm)
                    : TfmSelector.SelectPackageLibrary(
                        workspacePaths,
                        extractPath,
                        packageId,
                        requestedLibrary,
                        options.Tfm);
        }
        if (resolution.IsSelected)
            return new PackageLibrarySelection(resolution.Paths[0]);

        ReportPackageLibraryResolutionFailure(
            extractPath,
            packageName,
            version,
            options,
            requestedLibrary,
            resolution);
        return null;
    }

    private static void ReportPackageLibraryResolutionFailure(
        string extractPath,
        string packageName,
        string version,
        InspectionOptions options,
        string requestedLibrary,
        TfmSelector.PackageLibraryResolution resolution)
    {
        if (resolution.Status == TfmSelector.PackageLibraryResolutionStatus.RequestedLibraryNotFound)
            CommandError.Write($"Library '{requestedLibrary}' not found in package '{packageName}'.");
        else if (resolution.Status == TfmSelector.PackageLibraryResolutionStatus.NoAssemblies)
            CommandError.Write($"No DLLs found in package '{packageName}'.");
        else if (resolution.Status == TfmSelector.PackageLibraryResolutionStatus.NoMatchingTargetFramework)
            CommandError.Write($"No library found for TFM '{options.Tfm}' in package '{packageName}'.");
        else if (resolution.Status
            == TfmSelector.PackageLibraryResolutionStatus
                .NamesakeIdentityUnavailable)
        {
            CommandError.Write(
                $"Namesake Library identity is unavailable in package "
                    + $"'{packageName}'.");
            foreach (string path in resolution.IdentityFailurePaths ?? [])
            {
                CommandError.WriteLine(
                    $"  {Path.GetRelativePath(extractPath, path).Replace('\\', '/')}");
            }
        }
        else
            CommandError.Write(resolution.Tfm == null
                ? $"Package '{packageName}' contains multiple libraries."
                : $"Package '{packageName}' contains multiple libraries for {resolution.Tfm}.");

        if (resolution.Status
            is not (
                TfmSelector.PackageLibraryResolutionStatus.NoAssemblies
                or TfmSelector.PackageLibraryResolutionStatus
                    .NamesakeIdentityUnavailable))
            WritePackageLibraryCandidates(extractPath, packageName, version, resolution.Tfm ?? options.Tfm, resolution.CandidatePaths.ToList());
    }

    private static PackageLibrarySelectionResult? ResolveAllPackageLibraries(
        string extractPath,
        string packageName,
        string version,
        InspectionOptions options)
    {
        if (!TryResolveWorkspaceLibraryPaths(
                extractPath,
                options,
                out IReadOnlyList<string>? workspacePaths))
        {
            return null;
        }
        if (workspacePaths is not null)
        {
            return new PackageLibrarySelectionResult(
                [
                    .. workspacePaths.Select(
                        path => new PackageLibrarySelection(path)),
                ],
                options.Tfm);
        }

        var resolution = TfmSelector.SelectPackageLibraries(extractPath, options.Tfm);
        if (resolution.Status == TfmSelector.PackageLibraryResolutionStatus.NoAssemblies)
        {
            CommandError.Write($"No DLLs found in package '{packageName}'.");
            return null;
        }
        if (resolution.Status == TfmSelector.PackageLibraryResolutionStatus.NoMatchingTargetFramework)
        {
            CommandError.Write($"No libraries found for TFM '{options.Tfm}' in package '{packageName}'.");
            WritePackageLibraryCandidates(extractPath, packageName, version, options.Tfm, resolution.CandidatePaths.ToList());
            return null;
        }

        PackageLibrarySelection[] libraries =
        [
            .. resolution.Paths
                .Where(
                    path =>
                        TfmSelector.ClassifyPackageLibraryImage(path)
                        != TfmSelector.PackageLibraryImageKind.NonAssembly)
                .Select(path => new PackageLibrarySelection(path)),
        ];
        if (libraries.Length == 0)
        {
            CommandError.Write(
                $"No managed libraries found in package '{packageName}'.");
            return null;
        }

        return new PackageLibrarySelectionResult(
            libraries,
            resolution.Tfm);
    }

    private static bool TryResolveWorkspaceLibraryPaths(
        string extractPath,
        InspectionOptions options,
        out IReadOnlyList<string>? paths)
    {
        paths = null;
        if (options.WorkspaceLibraryAssetPaths is null)
            return true;

        var resolved = new List<string>(
            options.WorkspaceLibraryAssetPaths.Length);
        try
        {
            foreach (string assetPath in options.WorkspaceLibraryAssetPaths)
            {
                string path = ResolveContainedEntry(
                    extractPath,
                    assetPath);
                if (!File.Exists(path))
                {
                    CommandError.Write(
                        $"The admitted Package Library '{assetPath}' is "
                            + "missing from restored content.");
                    return false;
                }
                resolved.Add(path);
            }
        }
        catch (InvalidDataException exception)
        {
            CommandError.Write(exception.Message);
            return false;
        }

        if (resolved.Count == 0)
        {
            CommandError.Write(
                "The selected Package has no admitted compile Libraries.");
            return false;
        }

        paths = resolved;
        return true;
    }

    private sealed record PackageLibrarySelection(string Path);

    private sealed record PackageLibrarySelectionResult(
        IReadOnlyList<PackageLibrarySelection> Libraries,
        string? TargetFramework);

    internal static bool ShouldUsePackageCompileRoles(
        bool isLocalFile,
        string? selectedTargetFramework,
        string? selectedProducerKey,
        IEnumerable<string> selectedPackagePaths) =>
        !isLocalFile
        && PackageCoordinateResolver.IsAcquisitionTargetText(selectedTargetFramework)
        && !string.IsNullOrWhiteSpace(selectedProducerKey)
        && selectedPackagePaths.All(static path =>
            path.StartsWith("ref/", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith(
                "lib/",
                StringComparison.OrdinalIgnoreCase));

    static async Task<(PackageRootBinding? Binding, string? Failure)>
        AcquirePackageRootBindingAsync(
            HttpClient httpClient,
            string packageName,
            string version,
            string? targetFramework,
            string? selectedProducerKey,
            InspectionOptions options,
            Action<string>? log)
    {
        if (string.IsNullOrWhiteSpace(selectedProducerKey))
            return (null, "The selected package has no authorized content producer.");

        var sourceAuthorization =
            new SourcePolicyPackageSourceAuthorization(
                options.SourceOptions);
        PackageSourceAuthorization authorization =
            sourceAuthorization.AuthorizeSourcesFor(
                packageName.ToLowerInvariant());
        if (authorization.Sources.Count == 0)
        {
            return (
                null,
                authorization.DenialReason
                ?? $"No source is authorized to provide package '{packageName}'.");
        }
        IReadOnlyList<PackageSource> selectedSources =
        [
            .. authorization.Sources.Where(source =>
                NuGetCache.GetSourceKey(source.Url).Equals(
                    selectedProducerKey,
                    StringComparison.Ordinal)),
        ];
        if (selectedSources.Count == 0)
        {
            return (
                null,
                "The source that supplied the selected package is no longer authorized.");
        }

        PackageCoordinateResolution resolution =
            await PackageCoordinateResolver.ResolveAsync(
                httpClient,
                new PackageCoordinate(
                    packageName,
                    version,
                    PackageCoordinateResolver.IsAcquisitionTargetText(targetFramework)
                        ? targetFramework
                        : null,
                    RuntimeIdentifier: null),
                selectedSources,
                log,
                options.IncludePrerelease,
                useVersionCache: true,
                requireStableFloating: true);
        if (resolution is PackageCoordinateResolution.Invalid invalid)
            return (null, invalid.Message);
        if (resolution is PackageCoordinateResolution.Unavailable unavailable)
            return (null, unavailable.Message);

        PackagePayloadResult payload =
            await PackagePayloadAcquisition.AcquireAsync(
                httpClient,
                ((PackageCoordinateResolution.Resolved)resolution)
                    .Coordinate,
                new FileSystemPackageStore(),
                log);
        if (payload is PackagePayloadResult.Unavailable payloadFailure)
            return (null, payloadFailure.Message);

        return (
            PackageRootBinding.CreateFromResolved(
                ((PackagePayloadResult.Acquired)payload).Payload,
                targetFramework,
                packageName),
            null);
    }

    internal static List<string> GetAllLibrariesSections(
        List<LibraryInspection> inspections,
        LibraryOptions options,
        SectionPipeline<LibraryInspection> pipeline)
    {
        LibraryCommand.WarnEmptySections(
            inspections,
            options,
            pipeline,
            writeEmptyNote: false);

        bool selectAll = SelectResolver.IsActiveAllSelector(options.Select, options.IncludeSections);
        List<string> union = [];
        foreach (var inspection in inspections)
        {
            var sections = selectAll
                ? pipeline.GetAllSelectorSections(inspection)
                : pipeline.GetEffectiveSections(
                    inspection,
                    options.Verbosity,
                    options.IncludeSections,
                    options.FixedOverview);
            foreach (var section in sections)
            {
                if (!union.Contains(section, StringComparer.OrdinalIgnoreCase))
                    union.Add(section);
            }
        }

        var order = selectAll
            ? [.. pipeline.InfoSectionNames, .. pipeline.AllSectionNames.OrderBy(name => name, StringComparer.OrdinalIgnoreCase)]
            : pipeline.AllSectionNames;
        return order
            .Where(section => union.Contains(section, StringComparer.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static bool WriteAllLibrariesTable(
        string packageName,
        string version,
        List<LibraryInspection> inspections,
        List<string> sections,
        LibraryOptions options)
    {
        if (sections.Count != 1)
        {
            CommandError.Write($"Library aggregate row output requires exactly one section; matched {sections.Count}: {string.Join(", ", sections)}.");
            CommandError.WriteLine("Use Markdown output for multi-section selections, or select one concrete section.");
            return false;
        }

        string section = sections[0];
        var table = BuildAllLibrariesTable(
            packageName,
            version,
            inspections,
            section,
            options.Rows);
        if (table == null)
        {
            CommandError.Write($"Library aggregate row output does not support section: {section}.");
            CommandError.WriteLine("Use Markdown output, or select Library Info, Switches, Integrations, or Integration Opportunities.");
            return false;
        }

        if (!table.HasRowsBeforeWindow)
        {
            CommandError.WriteNote("matched section has no row data across all libraries.");
            OutputDestination.Write(
                options.OutputPath,
                options.Rows,
                static _ => { });
            return true;
        }

        OutputDestination.Write(options.OutputPath, options.Rows, output =>
        {
            OutputFormatter.WriteTable(output, !options.NoHeader, (writer, formatter) =>
            {
                var writerOptions = OutputFormatter.CreateTableWriterOptions(
                    options.Tsv,
                    options.Jsonl);
                var markoutWriter = new MarkoutWriter(
                    writer,
                    formatter,
                    writerOptions);
                markoutWriter.WriteTable(
                    table.Headers,
                    table.StableHeaders,
                    table.Rows);
                markoutWriter.Flush();
            });
        });
        return true;
    }

    private sealed record AllLibrariesTable(
        string[] Headers,
        string[] StableHeaders,
        string[][] Rows,
        bool HasRowsBeforeWindow);

    private static bool SupportsAllLibrariesTableSection(string section) =>
        FindAllLibrariesRowSchema(section) is not null;

    private static AllLibrariesTable? BuildAllLibrariesTable(
        string packageName,
        string version,
        List<LibraryInspection> inspections,
        string section,
        RowWindow? rowWindow)
    {
        AllLibrariesRowSchema? rowSchema =
            FindAllLibrariesRowSchema(section);
        if (rowSchema is null)
            return null;

        if (section.Equals("Library Info", StringComparison.OrdinalIgnoreCase))
        {
            var rowsByLibrary = inspections
                .Select(inspection =>
                    BuildLibraryInfoRows(packageName, version, inspection).ToArray())
                .ToArray();
            var libraryInfoRows = rowsByLibrary
                .SelectMany(rows => RowWindow.Apply(rowWindow, rows))
                .ToArray();
            return new(
                rowSchema.Headers,
                rowSchema.StableHeaders,
                libraryInfoRows,
                rowsByLibrary.Any(rows => rows.Length != 0));
        }

        if (section.Equals(IntegrationSectionNames.Opportunities, StringComparison.OrdinalIgnoreCase))
        {
            var opportunityRows = inspections
                .SelectMany(inspection => (inspection.IntegrationOpportunities ?? [])
                    .Select(opportunity => new
                    {
                        Inspection = inspection,
                        Opportunity = opportunity
                    }))
                .OrderBy(
                    row => row.Opportunity.Integration,
                    StringComparer.Ordinal)
                .ThenBy(
                    row => CodeCell(row.Opportunity.Api),
                    StringComparer.Ordinal)
                .Select(row => WithProvenance(
                    packageName,
                    version,
                    row.Inspection,
                    row.Opportunity.Integration,
                    row.Opportunity.Api,
                    row.Opportunity.IntegrationType,
                    row.Opportunity.LookFor))
                .ToArray();
            return new(
                rowSchema.Headers,
                rowSchema.StableHeaders,
                [.. RowWindow.Apply(rowWindow, opportunityRows)],
                opportunityRows.Length != 0);
        }

        if (section.Equals("Switches", StringComparison.OrdinalIgnoreCase))
        {
            var switchRows = inspections
                .SelectMany(inspection => inspection.SwitchInspection.PayloadsForRendering()
                    .Select(switchInfo => new
                    {
                        Inspection = inspection,
                        SwitchInfo = switchInfo
                    }))
                .OrderBy(
                    row => row.SwitchInfo.Kind,
                    StringComparer.Ordinal)
                .ThenBy(
                    row => CodeCell(row.SwitchInfo.Switch),
                    StringComparer.Ordinal)
                .Select(row => WithProvenance(
                    packageName,
                    version,
                    row.Inspection,
                    row.SwitchInfo.Kind,
                    row.SwitchInfo.Switch,
                    row.SwitchInfo.Api))
                .ToArray();
            return new(
                rowSchema.Headers,
                rowSchema.StableHeaders,
                [.. RowWindow.Apply(rowWindow, switchRows)],
                switchRows.Length != 0);
        }

        if (!section.Equals(
                IntegrationSectionNames.Integrations,
                StringComparison.OrdinalIgnoreCase))
            return null;

        var integrationRows = LibraryIntegrationCatalog.All
            .SelectMany(descriptor => inspections
                .SelectMany(inspection => descriptor
                    .RenderedSignals(descriptor.GetSignals(inspection))
                    .Select(signal => new
                    {
                        Descriptor = descriptor,
                        Inspection = inspection,
                        Signal = signal,
                    }))
                .OrderBy(row => row.Signal.Kind, StringComparer.Ordinal)
                .ThenBy(row => row.Signal.Name, StringComparer.Ordinal))
            .Select(row => WithProvenance(
                packageName,
                version,
                row.Inspection,
                row.Descriptor.Name,
                row.Signal.Kind,
                row.Signal.Shape == IntegrationSignalShape.Api
                    ? "API"
                    : "Type",
                row.Signal.Name))
            .ToArray();
        return new(
            rowSchema.Headers,
            rowSchema.StableHeaders,
            [.. RowWindow.Apply(rowWindow, integrationRows)],
            integrationRows.Length != 0);
    }

    private static AllLibrariesRowSchema?
        FindAllLibrariesRowSchema(string section) =>
        AllLibrariesRowSchemas.FirstOrDefault(schema =>
            schema.Section.Equals(
                section,
                StringComparison.OrdinalIgnoreCase));

    private static IEnumerable<string[]> BuildLibraryInfoRows(string packageName, string version, LibraryInspection inspection)
    {
        var info = new LibraryInspectionView(inspection).AssemblyInfoSection;
        if (info == null)
            yield break;

        foreach (var (field, value) in new (string Field, object? Value)[]
                 {
                     ("Architecture", info.Architecture),
                     ("Assembly Version", info.AssemblyVersion),
                     ("Async Methods", info.AsyncMethods),
                     ("Company", info.Company),
                     ("Compilation", info.Compilation),
                     ("Copyright", info.Copyright),
                     ("Custom Attributes", info.CustomAttributes),
                     ("Deterministic", info.Deterministic ? "Yes" : "No"),
                     ("Extension Methods", info.ExtensionMethods),
                     ("Facade", info.Facade switch
                     {
                         true => "Yes",
                         false => "No",
                         null => null
                     }),
                     ("File Size", info.FileSize),
                     ("Informational Version", info.InformationalVersion),
                     ("Integrations", info.Integrations),
                     ("Methods", info.Methods),
                     ("Modified", info.Modified),
                     ("Name", info.Name),
                     ("Product", info.Product),
                     ("Public Key Token", info.PublicKeyToken),
                     ("Reproducible", info.Reproducible ? "Yes" : "No"),
                     ("Resources", info.Resources),
                     ("Signed", info.Signed),
                     ("Source", info.Source),
                     ("Switches", info.Switches),
                     ("Target Framework", info.TargetFramework),
                     ("Type Forwarders", info.TypeForwarders),
                     ("Types", info.Types),
                     ("Union Types", info.UnionTypes),
                     ("Version", info.Version)
                 })
        {
            if (value == null)
                continue;

            yield return WithProvenance(
                packageName,
                version,
                inspection,
                field,
                value.ToString() ?? "");
        }
    }

    private static string[] WithProvenance(
        string packageName,
        string version,
        LibraryInspection inspection,
        params string[] values)
        =>
        [.. new[]
        {
            packageName,
            version,
            inspection.FileName,
            inspection.Tfm ?? ""
        }, .. values];

    private static string RenderAllLibrariesMarkdown(
        string packageName,
        string version,
        List<LibraryInspection> inspections,
        List<string> sections,
        LibraryOptions options,
        SectionPipeline<LibraryInspection> pipeline)
    {
        var sb = new StringBuilder();
        var title = string.IsNullOrWhiteSpace(version) ? packageName : $"{packageName} {version}";
        AppendBlock(sb, $"# {title}");

        foreach (var section in sections)
        {
            if (IsAggregatedAllLibrariesSection(section))
                AppendAggregatedSection(sb, section, inspections, options.Rows);
            else
                AppendPerLibrarySections(sb, section, inspections, options, pipeline);
        }

        return sb.ToString().TrimEnd();
    }

    /// <summary>
    /// Appends one rendered block, separated from whatever precedes it by exactly one blank line.
    /// </summary>
    /// <remarks>
    /// Written once rather than at each of the three call sites, which restated it and so had to
    /// keep three copies of the separator agreeing on a line ending. When a copy disagreed the
    /// result was silent: #3963 read a CRLF tail as "no blank line yet" and doubled the blank
    /// before every section on Windows.
    /// <para>
    /// Those call sites also guarded the trailing newlines with
    /// <c>!sb.ToString().EndsWith("\n\n")</c> -- the two section sites did; the title site, which
    /// runs first, never had it. That guard is unreachable and is not carried over.
    /// Every append to the buffer goes through this method, and this method always leaves exactly
    /// <c>"\n\n"</c> at the tail, so the guard was false for every block after the first and the
    /// length test excluded the first. It was load-bearing only while the tail could be CRLF,
    /// which is what #3981 removed. Keeping it would leave two mechanisms for one property and
    /// gate neither; the separation is asserted from the rendered document instead, by
    /// <c>PackageCommand_AllLibraries_AggregatedSection_SeparatesBlocksWithOneBlankLine</c>.
    /// </para>
    /// </remarks>
    private static void AppendBlock(StringBuilder sb, string rendered)
        => sb.Append(rendered).Append('\n').Append('\n');

    /// <summary>
    /// Renders one runtime-named, runtime-column section through the serializer so its rows reach
    /// the writer, which is what applies <c>--rows</c>.
    /// </summary>
    private static void AppendAggregatedSection(
        StringBuilder sb,
        string section,
        List<LibraryInspection> inspections,
        RowWindow? rows)
    {
        var document = BuildAggregatedSection(section, inspections);
        if (document is null)
            return;

        var output = new StringWriter { NewLine = "\n" };
        MarkoutSerializer.Serialize(
            document, output, InspectionContext.Default, OutputFormatter.CreateWindowedOptions(rows));
        var rendered = output.ToString().Trim();
        if (rendered.Length == 0)
            return;

        AppendBlock(sb, rendered);
    }

    private static bool IsAggregatedAllLibrariesSection(string section)
        => section.Equals(
               IntegrationSectionNames.Opportunities,
               StringComparison.OrdinalIgnoreCase)
           || section.Equals(
               IntegrationSectionNames.Integrations,
               StringComparison.OrdinalIgnoreCase)
           || section.Equals("Switches", StringComparison.OrdinalIgnoreCase);

    private static AggregatedSectionDocument? BuildAggregatedSection(
        string section,
        List<LibraryInspection> inspections)
    {
        if (section.Equals(IntegrationSectionNames.Opportunities, StringComparison.OrdinalIgnoreCase))
        {
            var opportunityRows = inspections
                .SelectMany(inspection => (inspection.IntegrationOpportunities ?? [])
                    .Select(row => new
                    {
                        Library = inspection.FileName,
                        Tfm = inspection.Tfm ?? "",
                        row.Integration,
                        Api = CodeCell(row.Api),
                        row.IntegrationType,
                        row.LookFor
                    }))
                .OrderBy(row => row.Integration, StringComparer.Ordinal)
                .ThenBy(row => row.Api, StringComparer.Ordinal)
                .ToList();
            if (opportunityRows.Count == 0)
                return null;

            return CreateAggregatedSection(section, new MarkoutTable(
                ["Library", "TFM", "Integration", "API", "Integration Type", "Look For"],
                opportunityRows.Select(row => new[]
                {
                    CodeCell(row.Library),
                    CodeCell(row.Tfm),
                    row.Integration,
                    row.Api,
                    row.IntegrationType,
                    row.LookFor
                }).ToList()));
        }

        if (section.Equals("Switches", StringComparison.OrdinalIgnoreCase))
        {
            var switchRows = inspections
                .SelectMany(inspection => inspection.SwitchInspection.PayloadsForRendering()
                    .Select(row => new
                    {
                        Library = inspection.FileName,
                        Tfm = inspection.Tfm ?? "",
                        row.Kind,
                        Switch = CodeCell(row.Switch),
                        Api = CodeCell(row.Api)
                    }))
                .OrderBy(row => row.Kind, StringComparer.Ordinal)
                .ThenBy(row => row.Switch, StringComparer.Ordinal)
                .ToList();
            if (switchRows.Count == 0)
                return null;

            return CreateAggregatedSection(section, new MarkoutTable(
                ["Library", "TFM", "Kind", "Switch", "API"],
                switchRows.Select(row => new[]
                {
                    CodeCell(row.Library),
                    CodeCell(row.Tfm),
                    row.Kind,
                    row.Switch,
                    row.Api
                }).ToList()));
        }

        if (!section.Equals(
                IntegrationSectionNames.Integrations,
                StringComparison.OrdinalIgnoreCase))
            return null;

        var integrationRows = LibraryIntegrationCatalog.All
            .SelectMany(descriptor => inspections
                .SelectMany(inspection => descriptor
                    .RenderedSignals(descriptor.GetSignals(inspection))
                    .Select(signal => new
                    {
                        Library = inspection.FileName,
                        Tfm = inspection.Tfm ?? "",
                        Integration = descriptor.Name,
                        Signal = signal,
                    }))
                .OrderBy(row => row.Signal.Kind, StringComparer.Ordinal)
                .ThenBy(row => row.Signal.Name, StringComparer.Ordinal))
            .ToList();
        if (integrationRows.Count == 0)
            return null;

        return CreateAggregatedSection(section, new MarkoutTable(
            ["Library", "TFM", "Integration", "Kind", "Shape", "Symbol"],
            integrationRows.Select(row => new[]
            {
                CodeCell(row.Library),
                CodeCell(row.Tfm),
                row.Integration,
                row.Signal.Kind,
                row.Signal.Shape == IntegrationSignalShape.Api
                    ? "API"
                    : "Type",
                CodeCell(row.Signal.Name),
            }).ToList()));
    }

    private static AggregatedSectionDocument CreateAggregatedSection(
        string section,
        MarkoutTable table)
        => new()
        {
            Sections = [new AggregatedSectionView { Name = section, Body = table }]
        };

    private static CountProjection CaptureAllLibrariesCounts(
        List<LibraryInspection> inspections,
        List<string> sections,
        LibraryOptions options,
        SectionPipeline<LibraryInspection> pipeline)
    {
        var projection = new CountProjection();

        foreach (var section in sections)
        {
            if (IsAggregatedAllLibrariesSection(section))
            {
                if (BuildAggregatedSection(section, inspections) is { } document)
                {
                    projection.Merge(CountProjectionFormatter.Capture(
                        document,
                        InspectionContext.Default,
                        OutputFormatter.CreateWindowedOptions(options.Rows)));
                }
                continue;
            }

            foreach (var inspection in inspections)
            {
                if (!pipeline.GetEffectiveSections(
                        inspection, options.Verbosity, options.IncludeSections, options.FixedOverview)
                    .Contains(section, StringComparer.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (MetadataSectionNames.IsMetadataSection(section))
                {
                    projection.Merge(MetadataLensRenderer.CaptureCounts(
                        inspection,
                        [section],
                        options.Rows));
                    continue;
                }

                projection.Merge(CountProjectionFormatter.Capture(
                    new LibraryInspectionView(inspection),
                    InspectionContext.Default,
                    CreateAllLibrariesWriterOptions(section, options)));
            }
        }

        return projection;
    }

    private static void AppendPerLibrarySections(
        StringBuilder sb,
        string section,
        List<LibraryInspection> inspections,
        LibraryOptions options,
        SectionPipeline<LibraryInspection> pipeline)
    {
        foreach (var inspection in inspections)
        {
            if (!pipeline.GetEffectiveSections(
                    inspection,
                    options.Verbosity,
                    options.IncludeSections,
                    options.FixedOverview)
                .Contains(section, StringComparer.OrdinalIgnoreCase))
                continue;

            var rendered = RenderLibrarySection(
                inspection,
                section,
                options,
                pipeline);
            if (rendered.Length == 0)
                continue;

            AppendBlock(sb, rendered);
        }
    }

    private static string RenderLibrarySection(
        LibraryInspection inspection,
        string section,
        LibraryOptions options,
        SectionPipeline<LibraryInspection> pipeline)
    {
        var view = new LibraryInspectionView(inspection);
        var writerOptions = new MarkoutWriterOptions
        {
            IncludeSections = [section],
            Projection = OutputFormatter.BuildProjection(options.Columns, options.Fields),
        };
        var markdown = OutputFormatter.SerializeLibraryMarkdown(
                view,
                inspection,
                writerOptions,
                pipeline,
                options.Rows)
            .Trim();
        if (markdown.Length == 0)
            return "";

        var lines = markdown.Split('\n').ToList();
        if (lines.Count > 0 && lines[0].StartsWith("# ", StringComparison.Ordinal))
        {
            lines.RemoveAt(0);
            if (lines.Count > 0 && string.IsNullOrWhiteSpace(lines[0]))
                lines.RemoveAt(0);
        }

        for (var i = 0; i < lines.Count; i++)
        {
            if (lines[i].Trim().Equals($"## {section}", StringComparison.Ordinal))
            {
                lines[i] = $"## {section} ({inspection.FileName})";
                break;
            }
        }

        return string.Join('\n', lines).Trim();
    }

    private static MarkoutWriterOptions CreateAllLibrariesWriterOptions(
        string section,
        LibraryOptions options)
        => new()
        {
            IncludeSections = [section],
            Projection = OutputFormatter.BuildProjection(options.Columns, options.Fields),
            // Windowed before both count reduction and the aggregate heading rewrite above.
            RowWindow = RowWindow.ToMarkout(options.Rows)
        };

    /// <summary>
    /// Marks a cell as code using markout's semantic inline tag rather than literal backticks, so
    /// the formatter owns the spelling.
    /// </summary>
    /// <remarks>
    /// This also changes two escapes that hand-written backticks got wrong. A pipe was written as
    /// <c>&amp;#124;</c>, which renders literally inside a code span; markout emits <c>\|</c>,
    /// which GFM unescapes while splitting rows, before code spans are parsed. A backtick was
    /// written as <c>\`</c>, but backslash escapes do not apply inside a code span; markout uses
    /// the doubled-delimiter form instead.
    ///
    /// Both corrections are unverified against real data: no package in the differential corpus
    /// produced a pipe or a backtick in these cells. They are reachable in principle — the
    /// integration scanner takes raw metadata names, which carry arity backticks such as
    /// <c>IEnumerable`1</c>, without the display-name normalization other scanners apply — but
    /// that path is not exercised by a test, so treat this as a latent fix rather than an
    /// observed one.
    /// </remarks>
    private static string CodeCell(string value) => MarkoutInline.Code(value);

    private static void WritePackageLibraryCandidates(
        string extractPath,
        string packageName,
        string version,
        string? tfm,
        List<string>? candidates = null)
    {
        candidates ??= string.IsNullOrWhiteSpace(tfm)
            ? TfmSelector.GetPackageAssemblies(extractPath)
            : TfmSelector.SelectHighestAssembliesFromPackage(extractPath, tfm).paths;

        if (candidates.Count > 0)
        {
            CommandError.WriteLine("Available libraries:");
            foreach (var candidate in candidates
                         .Select(path => Path.GetRelativePath(extractPath, path).Replace('\\', '/'))
                         .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
            {
                CommandError.WriteLine($"  {candidate}");
            }
        }

        var packageReference = !string.IsNullOrWhiteSpace(version)
            ? $"{packageName}@{version}"
            : packageName;
        CommandError.WriteBlankLine();
        CommandError.WriteLine("Use:");
        CommandError.WriteLine($"  dotnet-inspect package {packageReference} --library <dll>");
    }
}
