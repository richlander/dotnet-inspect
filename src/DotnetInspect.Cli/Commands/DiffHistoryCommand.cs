using System.Collections.Immutable;
using DotnetInspect.Cli.Options;
using DotnetInspect.Cli.Output;
using DotnetInspect.Cli.Sections;
using DotnetInspect.Cli.Views;
using DotnetInspector.PackageQueries;
using DotnetInspector.Packages;
using DotnetInspector.Queries;
using DotnetInspector.Sections;
using DotnetInspector.Services;
using ILInspector.Analysis;
using ILInspector.Metadata;
using Inspector.Findings;
using Markout;
using NuGetFetch;

namespace DotnetInspect.Cli.Commands;

internal static class DiffHistoryCommand
{
    const int MaximumHistoryEvaluations = 4_096;
    const int MaximumAssembliesPerVersion = 256;
    const long MaximumAssemblyEntryBytes = 512L * 1024 * 1024;
    const long MaximumRetainedImageBytes = 512L * 1024 * 1024;

    static readonly InspectionEnvelopeJsonContract<DiffHistoryOutcome>
        JsonContract =
            new(
                "diff-history",
                2,
                DiffHistoryJsonOutput.Write);

    internal static async Task<int> ExecuteAsync(
        DiffOptions options,
        CancellationToken cancellationToken)
    {
        if (!TryValidateMode(options, out string? error))
        {
            CommandError.Write(error!);
            return 1;
        }
        if (!TryResolveSections(
                options,
                out HashSet<string> selectedSections))
        {
            return 1;
        }

        SectionCatalog<DiffHistoryDocumentView> catalog =
            DiffHistorySections.Catalog;
        if (options.Discover is not null)
        {
            var model = new DiffHistoryDocumentView();
            var discoverable =
                catalog.Pipeline.GetDiscoverableSections(
                    model,
                    selectedSections);
            return DiscoverOutput.ExecuteEffective(
                options.Discover,
                discoverable,
                DiffHistorySections.CreateSchema(),
                DiscoveryOutputRequest.Create(
                    options.Jsonl
                        ? OutputFormat.Jsonl
                        : options.Tsv
                            ? OutputFormat.Tsv
                            : options.Tabular
                                ? OutputFormat.Table
                                : OutputFormat.Markdown,
                    options.Tree,
                    options.TabularExplicitlySet,
                    options.NoHeader),
                sectionCostAnnotations:
                    catalog.Pipeline.GetCostAnnotations(),
                sectionCategories: catalog.Pipeline.GetCategoryMap(),
                catalogHiddenSections:
                    options.Schema
                        ? null
                        : catalog.Pipeline.GetCatalogHiddenSections(),
                listedCategoryDoors:
                    catalog.Pipeline.GetListedCategoryDoors());
        }

        if (!TryValidateRequest(
                options,
                selectedSections,
                out PackageVersionRange? range,
                out string? type,
                out string? member,
                out string? finding,
                out error))
        {
            CommandError.Write(error!);
            return 1;
        }
        if (!TryCreateReplayContext(
                options.SourceOptions,
                options.IncludePrerelease,
                out DiffHistoryPackageReplayContext? replayContext,
                out error))
        {
            CommandError.Write(error!);
            return 1;
        }

        var context = new CommandContext(options.Verbose);
        try
        {
            await using DesktopPackageSourceComposition composition =
                context.CreatePackageSourceComposition();
            PackageHouseVersionPopulationResult populationResult =
                await composition.SettleVersionPopulationAsync(
                        new PackageHouseVersionPopulationRequest(
                            range!,
                            PackageHouseOperation.Create(
                                PackageHouseOperationProfile.Settle),
                            options.IncludePrerelease),
                        options.SourceOptions,
                        cancellationToken,
                        context.Logger.Log)
                    .ConfigureAwait(false);
            if (populationResult
                is not PackageHouseVersionPopulationResult.Available
                    population)
            {
                CommandError.Write(
                    DescribePopulationFailure(populationResult));
                return 1;
            }
            if (!TryCreateEvaluationPlan(
                    population.Vector,
                    options,
                    finding!,
                    out DiffHistoryEvaluationPlan? plan,
                    out error))
            {
                CommandError.Write(error!);
                return 1;
            }
            int maximumEvaluations =
                plan!.ResolveMaximumRealizableEvaluationCount(
                    population.Vector);
            if (maximumEvaluations > MaximumHistoryEvaluations)
            {
                CommandError.Write(
                    $"Diff History selected {maximumEvaluations} "
                    + $"evaluations, exceeding the "
                    + $"{MaximumHistoryEvaluations}-evaluation work limit. "
                    + "Narrow the package range or evaluation policy.");
                return 1;
            }

            using var executor = new DesktopHistoryCellExecutor(
                composition,
                options.SourceOptions,
                context.Logger.Log);
            InspectionEnvelope<DiffHistoryOutcome> envelope =
                await InspectAsync(
                        options,
                        population,
                        plan!,
                        type!,
                        member,
                        finding!,
                        replayContext,
                        executor,
                        cancellationToken)
                    .ConfigureAwait(false);

            WriteDiagnostics(envelope.Diagnostics);
            bool hasError = envelope.Diagnostics.Any(static diagnostic =>
                diagnostic.Severity
                    == InspectionDiagnosticSeverity.Error);
            bool completeJson =
                options.EnvelopeOutput
                || options.JsonOutput
                    && !HasHistoryPresentationProjection(options)
                    && !options.Count;
            if (completeJson)
            {
                if (options.Count)
                    ProjectionAudit.MarkHonored(ProjectionAudit.Count);
                if (!InspectionEnvelopeOutput.TryWrite(
                        envelope,
                        JsonContract,
                        options.EnvelopeOutput,
                        options.CompactJson))
                {
                    return 1;
                }
                return envelope.Content
                    is DiffHistoryOutcome.Available
                    && !hasError
                        ? 0
                        : 1;
            }

            if (envelope.Content
                is not DiffHistorySectionAvailable available)
            {
                return 1;
            }
            if (options.Count)
            {
                int countResult =
                    DiffHistoryOutput.WriteCount(available, options);
                return countResult == 0 && !hasError ? 0 : 1;
            }

            int outputResult = DiffHistoryOutput.WriteProjected(
                DiffHistoryOutput.Project(available),
                options,
                selectedSections);
            return outputResult == 0 && !hasError ? 0 : 1;
        }
        catch (Exception exception)
            when (exception is not OperationCanceledException)
        {
            CommandError.Write(exception);
            return 1;
        }
    }

    static bool TryValidateMode(
        DiffOptions options,
        out string? error)
    {
        if (options.At.Length > 0
            && (options.MaxProbes is not null
                || options.SamplePercent is not null))
        {
            error =
                "--at cannot be combined with --max-probes or --sample-percent.";
            return false;
        }
        if (options.MaxProbes is < 2)
        {
            error = "--max-probes must be at least 2.";
            return false;
        }
        if (options.SamplePercent is < 1 or > 100)
        {
            error = "--sample-percent must be from 1 through 100.";
            return false;
        }
        if (options.Schema && options.Discover is null)
        {
            error = "--schema requires -D/--discover.";
            return false;
        }
        if (options.CompactJson
            && !options.JsonOutput
            && !options.EnvelopeOutput)
        {
            error = "--compact requires --json or --envelope.";
            return false;
        }
        if (options.Tree)
        {
            error = "Diff History does not support --tree.";
            return false;
        }
        if (options.CompactJson
            && (options.Count && !options.EnvelopeOutput
                || HasHistoryPresentationProjection(
                    options,
                    admitSemanticRowSelection:
                        options.Count && options.EnvelopeOutput)))
        {
            error =
                "--compact is supported only for complete Diff History JSON or envelope output.";
            return false;
        }
        if (options.EnvelopeOutput
            && (options.Select is not null
                || options.SelectDefault
                || options.Columns is not null
                || options.Fields is not null
                || options.Tabular
                || options.Tsv
                || options.Jsonl
                || options.NoHeader
                || options.Discover is not null
                || options.Schema
                || options.VerbosityExplicitlySet
                || options.HasRenderedLineWindow
                || !options.Count
                    && options.SemanticRowSelection
                        is { Operations.Count: > 0 }))
        {
            error =
                "--envelope carries complete Diff History content and cannot be combined with presentation projections.";
            return false;
        }
        if (options.Count
            && (options.Columns is not null
                || options.Fields is not null))
        {
            error =
                "Diff History Count cannot be combined with field or column projections.";
            return false;
        }

        error = null;
        return true;
    }

    static bool TryValidateRequest(
        DiffOptions options,
        HashSet<string> selectedSections,
        out PackageVersionRange? range,
        out string? type,
        out string? member,
        out string? finding,
        out string? error)
    {
        range = null;
        type = null;
        member = null;
        finding = NormalizeFinding(
            options.Finding
                ?? MetadataFindings.MemberDescriptor.Id);
        error = null;

        if (options.PlatformVersionRange is not null
            || options.LibraryVersionRange is not null)
        {
            error =
                "Diff History currently supports package version populations only.";
            return false;
        }
        if (options.PackageVersionRange is not { } packageReference
            || !PackageVersionRange.TryParse(
                packageReference,
                out range,
                out error))
        {
            error ??=
                "Diff History requires --package Package@A..B.";
            return false;
        }
        if (options.TypeFilter.Count != 1)
        {
            error =
                "Diff History requires exactly one Type focus.";
            return false;
        }
        type = options.TypeFilter.Single();
        if (options.MemberFilter.Count > 1)
        {
            error =
                "Diff History accepts at most one exact Member focus.";
            return false;
        }
        member = options.MemberFilter.SingleOrDefault();
        if (finding is null)
        {
            error =
                $"Unknown Finding '{options.Finding}'. Use api.type, "
                + "api.member, api.attribute, analysis.allocation, "
                + "analysis.call-site, or analysis.unsafety.";
            return false;
        }
        bool analysis = IsAnalysisFinding(finding);
        if (analysis && member is null)
        {
            error =
                $"--finding {finding} requires exactly one --member target.";
            return false;
        }
        if (member is not null
            && finding != MetadataFindings.MemberDescriptor.Id
            && !analysis)
        {
            error =
                $"--member is not supported with --finding {finding}.";
            return false;
        }
        if (options.Framework is not null
            || options.NameOnly
            || options.Breaking
            || options.Additive
            || options.ChangedOnly
            || options.AllocRegressionsOnly
            || options.IncludePdbSource
            || options.SourceRepositories.Length > 0
            || options.Legend)
        {
            error =
                "Diff History cannot be combined with pairwise-only Diff controls.";
            return false;
        }
        if ((options.Tabular || options.Tsv || options.Jsonl)
            && !options.Count
            && selectedSections.Count != 1)
        {
            error =
                "Table, TSV, and JSONL output require exactly one History section.";
            return false;
        }

        error = null;
        return true;
    }

    static bool TryResolveSections(
        DiffOptions options,
        out HashSet<string> sections)
    {
        SelectResult selection = SelectResolver.ResolveSelectAsSections(
            options.Select,
            DiffHistorySections.Catalog.SelectableSectionNames,
            infoSections: [],
            DiffHistorySections.Catalog.SelectionCategoryMap,
            selectDefault: false);
        if (SelectOutput.WriteUnresolved(selection))
        {
            sections = [];
            return false;
        }

        bool explicitSelection =
            options.Select is { Length: > 0 };
        sections = selection.Sections
            ?? new HashSet<string>(
                [
                    options.Count
                        ? DiffHistorySections.ChangedVersions
                        : options.MaxProbes is not null
                            && options.SamplePercent is null
                            ? DiffHistorySections.Outcome
                            : DiffHistorySections.Evaluations,
                ],
                StringComparer.OrdinalIgnoreCase);

        if (options.Count
            && (sections.Count != 1
                || !sections.Contains(
                    DiffHistorySections.ChangedVersions)))
        {
            CommandError.Write(
                "Diff History Count admits only the Changed Versions section.");
            return false;
        }
        if (options.SelectDefault && explicitSelection)
        {
            CommandError.Write(
                "Bare -S cannot be combined with named History selectors.");
            return false;
        }
        return true;
    }

    static async Task<InspectionEnvelope<DiffHistoryOutcome>> InspectAsync(
        DiffOptions options,
        PackageHouseVersionPopulationResult.Available population,
        DiffHistoryEvaluationPlan plan,
        string type,
        string? member,
        string finding,
        DiffHistoryPackageReplayContext? replayContext,
        IPackageHouseVersionPopulationCellExecutor executor,
        CancellationToken cancellationToken)
    {
        PackageHouseOperation operation = PackageHouseOperation.Create(
            PackageHouseOperationProfile.Realize);
        PackageHouseTargetContext targetContext =
            string.IsNullOrWhiteSpace(options.Tfm)
                ? PackageHouseTargetContext.OwnerDefault()
                : PackageHouseTargetContext.Exact(options.Tfm);
        int maximumEvaluations =
            plan.ResolveMaximumRealizableEvaluationCount(population.Vector);
        var evaluationLimits =
            new DiffHistoryEvaluationLimits(maximumEvaluations);
        var workspaceLimits = new PackageVersionCellWorkspaceLimits(
            MaximumAssembliesPerVersion,
            MaximumAssemblyEntryBytes,
            MaximumRetainedImageBytes);
        DateTimeOffset workspaceDeadline =
            DateTimeOffset.UtcNow.AddMinutes(30);
        DiffHistoryCountRequest? count = options.Count
            ? new(
                DiffHistoryCountCohort.ChangedVersions,
                options.SemanticRowSelection)
            : null;

        if (TryAnalysisFinding(
                finding,
                out PackageVersionCellAnalysisProducerKind producer))
        {
            var inspection = new DiffHistoryAnalysisInspectionRequest(
                producer,
                population,
                plan,
                operation,
                targetContext,
                evaluationLimits,
                workspaceLimits,
                workspaceDeadline,
                new PackageVersionCellMemberSelector(
                    type,
                    member!,
                    includeAll: options.IncludeAll),
                new FindingSubject(
                    $"diff-history:{type}:{member}",
                    $"{type}.{member}"),
                replayContext: replayContext);
            return await DiffHistoryInspection.InspectAnalysisAsync(
                    new DiffHistoryAnalysisOperationRequest(
                        inspection,
                        count),
                    executor,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        var apiInspection = new PackageVersionCellApiInspectionRequest(
            type,
            new ApiSurfaceProjectionLimits(
                256,
                100_000,
                1_000_000,
                10_000,
                100_000,
                10_000_000,
                64_000_000),
            options.IncludeAll
                ? ApiSurfaceScope.IncludeAll
                : ApiSurfaceScope.Public);
        var request = new DiffHistoryApiInspectionRequest(
            ApiFinding(finding),
            population,
            plan,
            operation,
            targetContext,
            evaluationLimits,
            workspaceLimits,
            workspaceDeadline,
            apiInspection,
            replayContext: replayContext,
            member: member is null
                ? null
                : MemberTargetSelector.Parse(member));
        return await DiffHistoryInspection.InspectApiAsync(
                new DiffHistoryApiOperationRequest(
                    request,
                    count),
                executor,
                cancellationToken)
            .ConfigureAwait(false);
    }

    static bool TryCreateEvaluationPlan(
        PackageVersionVector population,
        DiffOptions options,
        string finding,
        out DiffHistoryEvaluationPlan? plan,
        out string? error)
    {
        plan = null;
        if (options.SamplePercent is { } samplePercent)
        {
            plan = new DiffHistoryEvaluationPlan.RepresentativeSurvey(
                samplePercent,
                options.MaxProbes);
            error = null;
            return true;
        }
        if (options.MaxProbes is { } maximumProbes)
        {
            plan = new DiffHistoryEvaluationPlan.AdaptiveBisect(
                maximumProbes);
            error = null;
            return true;
        }
        if (options.At.Length == 0)
        {
            plan = new DiffHistoryEvaluationPlan.FullPopulation();
            error = null;
            return true;
        }

        ImmutableArray<PackageVersionAddress> addresses;
        if (options.At.Any(static selector =>
                selector.Equals(
                    "all",
                    StringComparison.OrdinalIgnoreCase)))
        {
            if (options.At.Length != 1)
            {
                error =
                    "--at all cannot be combined with another --at selector.";
                return false;
            }
            addresses = population.Addresses;
        }
        else
        {
            var selected =
                new Dictionary<int, PackageVersionAddress>();
            foreach (string selector in options.At)
            {
                if (selector.Equals(
                        "endpoints",
                        StringComparison.OrdinalIgnoreCase))
                {
                    selected[population.Addresses[0].Position] =
                        population.Addresses[0];
                    selected[population.Addresses[^1].Position] =
                        population.Addresses[^1];
                    continue;
                }
                if (selector.Equals(
                        "midpoint",
                        StringComparison.OrdinalIgnoreCase))
                {
                    PackageVersionAddress midpoint =
                        population.Addresses[
                            (population.Addresses.Length - 1) / 2];
                    selected[midpoint.Position] = midpoint;
                    continue;
                }
                if (!population.TrySelect(
                        selector,
                        out PackageVersionAddress? address,
                        out error))
                {
                    return false;
                }
                selected[address!.Position] = address;
            }
            addresses =
            [
                .. selected.Values.OrderBy(
                    static address => address.Position),
            ];
        }
        if (IsAnalysisFinding(finding)
            && !addresses.Contains(population.Addresses[0]))
        {
            error =
                "Exact-Member Analysis History checkpoints must include the first population version.";
            return false;
        }

        plan =
            new DiffHistoryEvaluationPlan.ExplicitCheckpoints(addresses);
        error = null;
        return true;
    }

    static bool HasHistoryPresentationProjection(
        DiffOptions options,
        bool admitSemanticRowSelection = false) =>
        options.Select is not null
        || options.SelectDefault
        || options.Columns is not null
        || options.Fields is not null
        || !admitSemanticRowSelection
            && options.SemanticRowSelection is { Operations.Count: > 0 }
        || options.Tabular
        || options.Tsv
        || options.Jsonl
        || options.NoHeader
        || options.Tree
        || options.VerbosityExplicitlySet;

    static string? NormalizeFinding(string finding) =>
        finding.ToLowerInvariant() switch
        {
            "api.type" => MetadataFindings.TypeDescriptor.Id,
            "api.member" => MetadataFindings.MemberDescriptor.Id,
            "api.attribute" => MetadataFindings.AttributeDescriptor.Id,
            "analysis.allocation" =>
                AnalysisFindings.AllocationDescriptor.Id,
            "analysis.call-site" =>
                AnalysisFindings.CallSiteDescriptor.Id,
            "analysis.unsafety" =>
                AnalysisFindings.UnsafetyDescriptor.Id,
            _ => null,
        };

    static bool IsAnalysisFinding(string finding) =>
        finding == AnalysisFindings.AllocationDescriptor.Id
        || finding == AnalysisFindings.CallSiteDescriptor.Id
        || finding == AnalysisFindings.UnsafetyDescriptor.Id;

    static bool TryAnalysisFinding(
        string finding,
        out PackageVersionCellAnalysisProducerKind producer)
    {
        if (finding == AnalysisFindings.AllocationDescriptor.Id)
        {
            producer = PackageVersionCellAnalysisProducerKind.Allocation;
            return true;
        }
        if (finding == AnalysisFindings.CallSiteDescriptor.Id)
        {
            producer = PackageVersionCellAnalysisProducerKind.CallSite;
            return true;
        }
        if (finding == AnalysisFindings.UnsafetyDescriptor.Id)
        {
            producer = PackageVersionCellAnalysisProducerKind.Unsafety;
            return true;
        }

        producer = default;
        return false;
    }

    static DiffHistoryApiFindingKind ApiFinding(string finding) =>
        finding switch
        {
            var value when value == MetadataFindings.TypeDescriptor.Id =>
                DiffHistoryApiFindingKind.Type,
            var value when value == MetadataFindings.MemberDescriptor.Id =>
                DiffHistoryApiFindingKind.Members,
            var value when value == MetadataFindings.AttributeDescriptor.Id =>
                DiffHistoryApiFindingKind.Attributes,
            _ => throw new InvalidOperationException(
                $"Finding '{finding}' is not an API History producer."),
        };

    static bool TryCreateReplayContext(
        NuGetSourceOptions? options,
        bool includePrerelease,
        out DiffHistoryPackageReplayContext? replayContext,
        out string? error)
    {
        replayContext = null;
        error = null;
        if (options is null
            || options.Sources.Length == 0
                && options.AdditionalSources.Length == 0
                && options.ConfigFile is null
                && options.ConfigDirectory is null)
        {
            replayContext = includePrerelease
                ? new(includePrerelease: true)
                : null;
            return true;
        }

        NuGetSourceOptions replayOptions = options;
        if (replayOptions.ConfigFile is null
            && replayOptions.ConfigDirectory is null)
        {
            replayOptions = replayOptions with
            {
                ConfigDirectory = Directory.GetCurrentDirectory(),
            };
        }
        if (!PackageReplaySourceArguments.TryCreate(
                replayOptions,
                "diff --history",
                out PackageReplaySources? sources,
                out error))
        {
            return false;
        }

        replayContext = sources is null
            ? null
            : new(
                sources.Sources,
                sources.AdditionalSources,
                sources.ConfigFile,
                sources.ConfigDirectory,
                includePrerelease);
        return true;
    }

    static string DescribePopulationFailure(
        PackageHouseVersionPopulationResult result) =>
        result switch
        {
            PackageHouseVersionPopulationResult.NotFound value =>
                value.Reason.ToString(),
            PackageHouseVersionPopulationResult.NoMatch value =>
                value.Reason.ToString(),
            PackageHouseVersionPopulationResult.Incomplete value =>
                value.Reason.ToString(),
            PackageHouseVersionPopulationResult.Rejected value =>
                value.Reason.ToString(),
            PackageHouseVersionPopulationResult.Unavailable value =>
                value.Reason.ToString(),
            PackageHouseVersionPopulationResult.Failed value =>
                value.Reason.ToString(),
            _ => "Package version population settlement did not complete.",
        };

    static void WriteDiagnostics(
        IEnumerable<InspectionDiagnostic> diagnostics)
    {
        foreach (InspectionDiagnostic diagnostic in diagnostics)
            CommandError.WriteNote(diagnostic.Summary.ToString());
    }

    sealed class DesktopHistoryCellExecutor :
        IPackageHouseVersionPopulationCellExecutor,
        IDisposable
    {
        readonly DesktopPackageSourceComposition _composition;
        readonly NuGetSourceOptions? _sourceOptions;
        readonly PackagePayloadAcquisitionPlan _payloadAcquisition;
        readonly Dictionary<
            ConfiguredPackageAuthority,
            IPackageStore> _stores =
                new(ReferenceEqualityComparer.Instance);
        string? _temporaryRoot;

        internal DesktopHistoryCellExecutor(
            DesktopPackageSourceComposition composition,
            NuGetSourceOptions? sourceOptions,
            Action<string>? log)
        {
            _composition = composition;
            _sourceOptions = sourceOptions;
            _payloadAcquisition =
                new PackagePayloadAcquisitionPlan(
                    GetStore,
                    log: log);
        }

        public Task<PackageHouseSettlement> ExecuteAsync(
            PackageHouseVersionPopulationCellExecution execution,
            CancellationToken cancellationToken = default) =>
            _composition.ExecuteVersionPopulationCellAsync(
                execution,
                _payloadAcquisition,
                _sourceOptions,
                cancellationToken);

        IPackageStore GetStore(
            ConfiguredPackageAuthority authority,
            PackageProducerIdentity producer)
        {
            if (!_stores.TryGetValue(
                    authority,
                    out IPackageStore? store))
            {
                store = new AuthorityScopedFileSystemPackageStore(
                    authority,
                    producer,
                    () =>
                        _temporaryRoot ??=
                            Directory.CreateTempSubdirectory(
                                "inspect-diff-history").FullName);
                _stores.Add(authority, store);
            }
            return store;
        }

        public void Dispose() =>
            DotnetInspector.Packages.PackageExtractor.Cleanup(
                _temporaryRoot);
    }
}
