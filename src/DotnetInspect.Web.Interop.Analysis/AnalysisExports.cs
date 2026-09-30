using System.Collections.Immutable;
using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using System.Text.Json;
using DotnetInspector.PackageQueries;
using DotnetInspector.Queries;
using DotnetInspector.Sections;
using ILInspector.Metadata;
using ILInspector.Research;
using ILAnalysis = ILInspector.Analysis;
using DotnetInspect.Web;
using DotnetInspect.Web.Interop.Analysis;

namespace DotnetInspect.Web.Interop.Analysis;

/// <summary>
/// Analysis, integration, opportunity, and performance results for one package or platform
/// workspace.
/// </summary>
/// <remarks>
/// Every export runs a public product query over a shared <see cref="BrowserInspectionScope"/>
/// that owns the session and the Analysis index. This facade composes no evidence and adapts no
/// call-graph topology; graph traversal has its own facade and product owner.
/// </remarks>
[SupportedOSPlatform("browser")]
public static partial class AnalysisExports
{
    private const int BrowserLibraryTypeLeverageSchemaVersion = 2;

    /// <summary>
    /// Exact method-body Analysis and metadata evidence for one implementation participant. The
    /// product query owns the retained snapshot and Analysis index; this adapter only resolves
    /// ref/lib identity and formats the wire model.
    /// </summary>
    [JSExport]
    public static async Task<string> QueryMemberFacts(
        string packageId,
        string version,
        string targetFramework,
        string assemblyName,
        string typeIdentity,
        string memberName,
        string memberSignature,
        string selectorKey,
        int metadataToken,
        bool implementationBodySelected)
    {
        BrowserMemberFacts facts = await MemberFactsAsync(
            packageId,
            version,
            targetFramework,
            assemblyName,
            typeIdentity,
            memberName,
            memberSignature,
            selectorKey,
            metadataToken,
            implementationBodySelected);
        return JsonSerializer.Serialize(
            facts,
            BrowserAnalysisJsonContext.Default.BrowserMemberFacts);
    }

    static async Task<BrowserMemberFacts> MemberFactsAsync(
        string packageId,
        string version,
        string targetFramework,
        string assemblyName,
        string typeIdentity,
        string memberName,
        string memberSignature,
        string selectorKey,
        int metadataToken,
        bool implementationBodySelected)
    {
        _ = memberSignature;
        await using BrowserMemberResolution.ScopedResolution resolved =
            await BrowserMemberResolution.ImplementationMemberAsync(
                packageId,
                version,
                targetFramework,
                assemblyName,
                typeIdentity,
                memberName,
                selectorKey,
                implementationBodySelected ? metadataToken : 0);
        BrowserInspectionScope scope = resolved.Scope;
        BrowserWorkspaceParticipant participant = resolved.ImplementationParticipant;
        ILAnalysis.CallGraphMemberResolution resolution = resolved.Member;

        AssemblyMethodAnalysis analysis = BrowserSurfaceProjection.Require(
            scope.UseImplementationParticipant(
                participant,
                (group, member) =>
                    AssemblyContextMethodAnalysisQuery.ExecuteParticipant(
                        group,
                        member,
                        resolution.BodyToken)),
            $"Facts for '{typeIdentity}.{memberName}'");

        var result = new BrowserMemberFacts(
            analysis.Method.MetadataToken,
            new BrowserMethodSignals(
                analysis.Signals.Allocations,
                analysis.Signals.Copies,
                analysis.Signals.Unsafe,
                analysis.Signals.Reflection,
                analysis.Signals.Throws,
                analysis.Signals.Catches,
                analysis.Signals.Finallys,
                analysis.Signals.AllocInLoop,
                [.. analysis.Signals.Evidence.Select(FormatOffset)],
                [.. analysis.Signals.ExceptionTypes]),
            [
                .. analysis.Allocations.Select(
                    allocation => new BrowserAllocationFact(
                        allocation.Kind.ToString(),
                        allocation.AllocatedType?.ToDisplayString()
                            ?? allocation.RuntimeAllocationType,
                        FormatOffset(allocation.ILOffset),
                        allocation.CountsAsHeapAllocation,
                        allocation.Frequency.ToString(),
                        allocation.Multiplicity.ToString(),
                        allocation.PathContext.ToString(),
                        allocation.EscapeKind
                            != ILAnalysis.AllocationEscapeKind.None
                                ? allocation.EscapeKind.ToString()
                                : allocation.Escape.ToString(),
                        allocation.InLoop,
                        allocation.EstimatedSizeBytes,
                        allocation.Detail,
                        [
                            .. allocation.LifetimeEvidence.Uses.Select(
                                use =>
                                    new BrowserAllocationLifetimeUse(
                                        use.ILOffset,
                                        ILAnalysis.SemanticFactProjection
                                            .FormatLifetimeUseKind(
                                                use.Kind))),
                        ],
                        [
                            .. allocation.LifetimeEvidence.Limitations
                                .Select(
                                    limitation =>
                                        new BrowserAllocationLifetimeLimitation(
                                            ILAnalysis.SemanticFactProjection
                                                .FormatLifetimeLimitationKind(
                                                    limitation.Kind),
                                            limitation.ILOffset,
                                            limitation.Operation?
                                                .ToString())),
                        ])),
            ],
            [
                .. analysis.DirectCalls.Select(
                    call =>
                    {
                        string typeArguments =
                            call.Callee.TypeArguments.Length == 0
                                ? ""
                                : $"<{string.Join(
                                    ", ",
                                    call.Callee.TypeArguments.Select(
                                        argument =>
                                            argument
                                                .ToQualifiedDisplayString()))}>";
                        return new BrowserCallFact(
                            $"{call.Callee.DeclaringType.ToQualifiedDisplayString()}."
                            + $"{call.Callee.Name}{typeArguments}("
                            + string.Join(
                                ", ",
                                call.Callee.ParameterTypes.Select(
                                    parameter =>
                                        parameter
                                            .ToQualifiedDisplayString()))
                            + ")",
                            FormatOffset(call.ILOffset),
                            string.IsNullOrEmpty(call.Opcode)
                                ? FormatCallKind(call.Kind)
                                : call.Opcode,
                            call.Kind.ToString(),
                            call.Multiplicity.ToString(),
                            call.InLoop);
                    }),
            ],
            [
                .. ILAnalysis.SemanticFactProjection.SafetyFacts(
                    analysis.UnsafeEvidence,
                    analysis.UnsafetyOccurrences)
                    .Select(
                        fact => new BrowserSafetyFact(
                            fact.SafetyKind,
                            fact.ILOffset is int offset
                                ? FormatOffset(offset)
                                : null,
                            fact.Operation,
                            fact.Requirement,
                            fact.Evidence)),
            ],
            [
                .. analysis.ExceptionRegions.Select(
                    region => new BrowserExceptionRegion(
                        region.Region,
                        region.Clause,
                        FormatRange(region.TryStart, region.TryEnd),
                        FormatRange(
                            region.HandlerStart,
                            region.HandlerEnd),
                        region.FilterStart is int filterStart
                            && region.FilterEnd is int filterEnd
                                ? FormatRange(filterStart, filterEnd)
                                : null,
                        region.CaughtType)),
            ],
            [
                .. analysis.OptimizationOpportunities.Select(
                    opportunity =>
                        new BrowserPerformanceOpportunity(
                            opportunity.Shape,
                            opportunity.Evidence,
                            opportunity.SafeFixDirection,
                            opportunity.Confidence,
                            opportunity.ILOffset is int offset
                                ? FormatOffset(offset)
                                : null,
                            opportunity.InLoop,
                            opportunity.Caveat,
                            opportunity.SourceFinding,
                            opportunity.Provenance.ToString()
                                .ToLowerInvariant())),
            ],
            [
                .. analysis.Diagnostics.Select(
                    diagnostic =>
                        $"{diagnostic.Method}: {diagnostic.Message}"),
            ]);

        return result;
    }

    static string FormatOffset(int offset) => $"IL_{offset:X4}";

    static string FormatRange(int start, int end) =>
        $"{FormatOffset(start)}..{FormatOffset(end)}";

    static string FormatCallKind(ILAnalysis.CallKind kind) =>
        kind switch
        {
            ILAnalysis.CallKind.Call => "call",
            ILAnalysis.CallKind.CallVirtual => "callvirt",
            ILAnalysis.CallKind.NewObject => "newobj",
            ILAnalysis.CallKind.LoadFunction => "ldftn",
            ILAnalysis.CallKind.LoadVirtualFunction => "ldvirtftn",
            ILAnalysis.CallKind.CallIndirect => "calli",
            _ => throw new ArgumentOutOfRangeException(
                nameof(kind),
                kind,
                "Unknown direct-call kind."),
        };

    /// <summary>
    /// Ecosystem integration evidence for one exact package library.
    /// </summary>
    [JSExport]
    public static async Task<string> QueryPackageIntegrations(
        string packageId,
        string version,
        string targetFramework,
        string assemblyName)
    {
        BrowserPackageIntegrations integrations =
            await PackageIntegrationsAsync(
                packageId, version, targetFramework, assemblyName);
        return JsonSerializer.Serialize(
            integrations,
            BrowserAnalysisJsonContext.Default.BrowserPackageIntegrations);
    }

    static async Task<BrowserPackageIntegrations> PackageIntegrationsAsync(
        string packageId,
        string version,
        string targetFramework,
        string assemblyName)
    {
        await using BrowserScopeLease<BrowserInspectionScope> scopeLease =
            await BrowserPackageWorkspace.OpenScopeAsync(
                packageId,
                version,
                targetFramework);
        BrowserInspectionScope scope = scopeLease.Scope;
        BrowserPackageCoordinate coordinate = scope.Coordinates[0];
        BrowserCompileLibraryAvailability compileLibrary =
            BrowserAnalysisWireProjection.Project(
                BrowserCompileLibraryProjection.Project(coordinate.Selection));
        if (!coordinate.Selection.IsSelected)
        {
            return new BrowserPackageIntegrations(
                coordinate.PackageId,
                coordinate.Version,
                BrowserFrameworkText.Active(coordinate),
                Categories: [],
                TotalSignals: 0,
                IsComplete: false,
                InspectionError: null,
                compileLibrary);
        }

        BrowserWorkspaceParticipant participant =
            scope.LibraryParticipant(coordinate, assemblyName);
        InspectionEnvelope<AssemblyIntegrationsEntry> inspection =
            scope.UseMetadataParticipant(
                participant,
                AssemblyIntegrationsInspection.Execute);

        return CreateIntegrations(
                coordinate.PackageId,
                coordinate.Version,
                coordinate.Framework,
                [inspection.Content],
                compileLibrary)
            with
            {
                Inspection =
                    BrowserAnalysisInspectionProjection.Project(inspection),
            };
    }

    /// <summary>
    /// Missing ecosystem integration opportunities for one exact package library.
    /// The product query composes them from its typed Integrations prerequisite; the browser only
    /// groups and deduplicates the returned evidence for presentation.
    /// </summary>
    [JSExport]
    public static async Task<string> QueryPackageOpportunities(
        string packageId,
        string version,
        string targetFramework,
        string assemblyName)
    {
        BrowserPackageOpportunities opportunities =
            await PackageOpportunitiesAsync(
                packageId, version, targetFramework, assemblyName);
        return JsonSerializer.Serialize(
            opportunities,
            BrowserAnalysisJsonContext.Default.BrowserPackageOpportunities);
    }

    static async Task<BrowserPackageOpportunities> PackageOpportunitiesAsync(
        string packageId,
        string version,
        string targetFramework,
        string assemblyName)
    {
        await using BrowserScopeLease<BrowserInspectionScope> scopeLease =
            await BrowserPackageWorkspace.OpenScopeAsync(
                packageId,
                version,
                targetFramework);
        BrowserInspectionScope scope = scopeLease.Scope;
        BrowserPackageCoordinate coordinate = scope.Coordinates[0];
        BrowserCompileLibraryAvailability compileLibrary =
            BrowserAnalysisWireProjection.Project(
                BrowserCompileLibraryProjection.Project(coordinate.Selection));
        if (!coordinate.Selection.IsSelected)
        {
            return new BrowserPackageOpportunities(
                coordinate.PackageId,
                coordinate.Version,
                BrowserFrameworkText.Active(coordinate),
                Categories: [],
                TotalOpportunities: 0,
                IsComplete: false,
                InspectionError: null,
                compileLibrary);
        }

        BrowserWorkspaceParticipant participant =
            scope.LibraryParticipant(coordinate, assemblyName);
        InspectionEnvelope<
            AssemblyIntegrationOpportunitiesInspectionResult> inspection =
            scope.UseMetadataParticipant(
                participant,
                AssemblyIntegrationOpportunitiesInspection.Execute);

        return CreateOpportunities(
                coordinate.PackageId,
                coordinate.Version,
                coordinate.Framework,
                [inspection.Content.Opportunities],
                compileLibrary)
            with
            {
                Inspection =
                    BrowserAnalysisInspectionProjection.Project(inspection),
            };
    }

    internal static BrowserPackageIntegrations CreateIntegrations(
        string package,
        string version,
        string framework,
        IEnumerable<AssemblyIntegrationsEntry> entries,
        BrowserCompileLibraryAvailability? compileLibrary = null)
    {
        AssemblyIntegrationsEntry[] materialized = [.. entries];
        var failures = new List<string>();
        var signals = new List<EcosystemIntegrationSignalInfo>();
        foreach (AssemblyIntegrationsEntry entry in materialized)
        {
            switch (entry)
            {
                case AssemblyIntegrationsEntry.Available available:
                    signals.AddRange(available.EcosystemSignals);
                    break;
                case AssemblyIntegrationsEntry.Rejected rejected:
                    failures.Add(
                        BrowserSurfaceProjection.RejectedAssembly(
                            rejected.Failure));
                    break;
                case AssemblyIntegrationsEntry.Failed failed:
                    failures.Add(
                        BrowserSurfaceProjection.FailedAssembly(failed.Error));
                    break;
                default:
                    throw new InvalidOperationException(
                        $"Unknown assembly integrations entry '{entry.GetType().Name}'.");
            }
        }

        return new BrowserPackageIntegrations(
                package,
                version,
                BrowserFrameworkText.Require(framework),
                [
                    .. signals
                        .GroupBy(
                            signal => signal.Integration,
                            StringComparer.Ordinal)
                        .OrderBy(
                            group => group.Key,
                            StringComparer.OrdinalIgnoreCase)
                        .Select(group => new BrowserIntegrationCategory(
                            group.Key,
                            [
                                .. group
                                    .Select(signal =>
                                        new BrowserIntegrationSignal(
                                            signal.Kind,
                                            signal.Name,
                                            signal.Shape))
                                    .DistinctBy(signal =>
                                        (signal.Kind,
                                            signal.Name,
                                            signal.Shape))
                                    .OrderBy(
                                        signal => signal.Name,
                                        StringComparer.OrdinalIgnoreCase),
                            ])),
                ],
                signals.Count,
                materialized.All(
                    entry => entry
                        is AssemblyIntegrationsEntry.Available),
                failures.Count == 0
                    ? null
                    : string.Join("; ", failures),
                compileLibrary
                    ?? BrowserAnalysisWireProjection.Project(
                        BrowserCompileLibraryProjection.Selected(framework)));
    }

    internal static BrowserPackageOpportunities CreateOpportunities(
        string package,
        string version,
        string framework,
        IEnumerable<AssemblyIntegrationOpportunitiesEntry> entries,
        BrowserCompileLibraryAvailability? compileLibrary = null)
    {
        AssemblyIntegrationOpportunitiesEntry[] materialized =
            [.. entries];
        var failures = new List<string>();
        var opportunities =
            new List<(
                AssemblyReferenceIdentity Source,
                IntegrationOpportunityInfo Opportunity)>();
        foreach (AssemblyIntegrationOpportunitiesEntry entry in materialized)
        {
            switch (entry)
            {
                case AssemblyIntegrationOpportunitiesEntry.Available available:
                    opportunities.AddRange(
                        available.Opportunities.Select(
                            opportunity =>
                                (available.Subject.Identity, opportunity)));
                    break;
                case AssemblyIntegrationOpportunitiesEntry.Rejected rejected:
                    failures.Add(
                        BrowserSurfaceProjection.RejectedAssembly(
                            rejected.Failure));
                    break;
                case AssemblyIntegrationOpportunitiesEntry.Failed failed:
                    failures.Add(
                        BrowserSurfaceProjection.FailedAssembly(failed.Error));
                    break;
                default:
                    throw new InvalidOperationException(
                        "Unknown assembly integration-opportunities entry "
                        + $"'{entry.GetType().Name}'.");
            }
        }

        (AssemblyReferenceIdentity Source, IntegrationOpportunityInfo Opportunity)[]
            distinctOpportunities =
        [
            .. opportunities.DistinctBy(item =>
                (item.Source,
                    item.Opportunity.Integration,
                    item.Opportunity.Api,
                    item.Opportunity.IntegrationType)),
        ];
        return new BrowserPackageOpportunities(
                package,
                version,
                BrowserFrameworkText.Require(framework),
                [
                    .. distinctOpportunities
                        .GroupBy(
                            item => item.Opportunity.Integration,
                            StringComparer.Ordinal)
                        .OrderBy(
                            group => group.Key,
                            StringComparer.OrdinalIgnoreCase)
                        .Select(group => new BrowserOpportunityCategory(
                            group.Key,
                            [
                                .. group
                                    .OrderBy(
                                        item => item.Opportunity.Api,
                                        StringComparer.OrdinalIgnoreCase)
                                    .Select(item =>
                                        new BrowserOpportunityItem(
                                            item.Opportunity.Api,
                                            item.Opportunity.IntegrationType,
                                            item.Opportunity.LookFor,
                                            item.Opportunity
                                                .GetSourceTypeDefinition()?
                                                .ToEscapedFullName(),
                                            item.Source.Name,
                                            item.Source.Version?.ToString()
                                                ?? "",
                                            item.Source.Culture,
                                            item.Source.PublicKeyToken)),
                            ])),
                ],
                distinctOpportunities.Length,
                materialized.All(
                    entry => entry
                        is AssemblyIntegrationOpportunitiesEntry.Available),
                failures.Count == 0
                    ? null
                    : string.Join("; ", failures),
                compileLibrary
                    ?? BrowserAnalysisWireProjection.Project(
                        BrowserCompileLibraryProjection.Selected(framework)));
    }

    /// <summary>
    /// Product-ranked optimization-opportunity members for one exact package library. Analysis owns
    /// opportunity and member order; the query owns index lifetime and public-API attribution;
    /// this host only maps the typed rows to the existing browser wire contract.
    /// </summary>
    [JSExport]
    public static async Task<string> QueryPackagePerformance(
        string packageId,
        string version,
        string targetFramework,
        string assemblyName)
    {
        BrowserPackagePerformance performance =
            await PackagePerformanceAsync(
                packageId, version, targetFramework, assemblyName);
        return JsonSerializer.Serialize(
            performance,
            BrowserAnalysisJsonContext.Default.BrowserPackagePerformance);
    }

    /// <summary>
    /// Research-owned structural metrics for one exact package library. The
    /// Browser host receives the typed Research document projected into its
    /// local wire contract; it does not recompute distributions.
    /// </summary>
    [JSExport]
    public static async Task<string> QueryPackageLibraryMetrics(
        string packageId,
        string version,
        string targetFramework,
        string assemblyName)
    {
        BrowserLibraryMetrics metrics =
            await PackageLibraryMetricsAsync(
                packageId, version, targetFramework, assemblyName);
        return JsonSerializer.Serialize(
            metrics,
            BrowserAnalysisJsonContext.Default.BrowserLibraryMetrics);
    }

    /// <summary>
    /// Signature-only namespace leverage for one exact package Library. This
    /// path does not run implementation profiles or body analysis.
    /// </summary>
    [JSExport]
    public static async Task<string> QueryPackageLibraryNamespaceLeverage(
        string packageId,
        string version,
        string targetFramework,
        string assemblyName)
    {
        BrowserLibraryNamespaceLeverage leverage =
            await PackageLibraryNamespaceLeverageAsync(
                packageId,
                version,
                targetFramework,
                assemblyName);
        return JsonSerializer.Serialize(
            leverage,
            BrowserAnalysisJsonContext.Default
                .BrowserLibraryNamespaceLeverage);
    }

    /// <summary>
    /// Signature-only Type leverage for one exact namespace in one exact
    /// package Library.
    /// </summary>
    [JSExport]
    public static async Task<string> QueryPackageNamespaceTypeLeverage(
        string packageId,
        string version,
        string targetFramework,
        string assemblyName,
        string exactNamespace)
    {
        BrowserLibraryTypeLeverageShard leverage =
            await PackageNamespaceTypeLeverageAsync(
                packageId,
                version,
                targetFramework,
                assemblyName,
                exactNamespace);
        return JsonSerializer.Serialize(
            leverage,
            BrowserAnalysisJsonContext.Default
                .BrowserLibraryTypeLeverageShard);
    }

    /// <summary>
    /// Objective implementation profiles and exact overload relationships for
    /// one public overload family in a package implementation Library.
    /// </summary>
    [JSExport]
    public static async Task<string> QueryPackageImplementationProfiles(
        string packageId,
        string version,
        string targetFramework,
        string assemblyName,
        string typeDefinitionId,
        string[] stableSelectors)
    {
        BrowserImplementationProfiles profiles =
            await PackageImplementationProfilesAsync(
                packageId,
                version,
                targetFramework,
                assemblyName,
                typeDefinitionId,
                stableSelectors);
        return JsonSerializer.Serialize(
            profiles,
            BrowserAnalysisJsonContext.Default
                .BrowserImplementationProfiles);
    }

    static async Task<BrowserImplementationProfiles>
        PackageImplementationProfilesAsync(
            string packageId,
            string version,
            string targetFramework,
            string assemblyName,
            string typeDefinitionId,
            string[] stableSelectors)
    {
        var selection = new ImplementationProfileFamilySelection(
            typeDefinitionId,
            stableSelectors);
        await using BrowserScopeLease<BrowserInspectionScope> scopeLease =
            await BrowserPackageWorkspace.OpenScopeAsync(
                packageId,
                version,
                targetFramework);
        BrowserInspectionScope scope = scopeLease.Scope;
        BrowserPackageCoordinate coordinate = scope.Coordinates[0];
        BrowserCompileLibraryAvailability compileLibrary =
            BrowserAnalysisWireProjection.Project(
                BrowserCompileLibraryProjection.Project(coordinate.Selection));
        if (!coordinate.Selection.IsSelected)
        {
            return BrowserImplementationProfileWireProjection.Unavailable(
                compileLibrary.Status.ToString(),
                $"The package has no selected compile library "
                    + $"({compileLibrary.Status}).",
                compileLibrary);
        }

        BrowserWorkspaceParticipant participant =
            scope.LibraryParticipant(coordinate, assemblyName);
        if (!scope.ImplementationParticipants.Contains(participant))
        {
            return BrowserImplementationProfileWireProjection.Unavailable(
                "NoImplementationAssembly",
                "The selected library has no managed implementation assembly.",
                compileLibrary);
        }

        InspectionEnvelope<
            AssemblyContextEntry<
                AssemblyImplementationProfileFamilyInspection>>
                inspection =
                    scope.UseImplementationParticipant(
                        participant,
                        (group, selectedParticipant) =>
                            ImplementationProfileFamilyInspectionOperation
                                .Execute(
                                    group,
                                    selectedParticipant,
                                    selection));
        return BrowserImplementationProfileWireProjection.Project(
            inspection,
            compileLibrary);
    }

    /// <summary>
    /// Member-list heat for every eligible overload family on one Type in a
    /// package implementation Library, measured in one Analysis execution.
    /// </summary>
    [JSExport]
    public static async Task<string> QueryPackageTypeImplementationHeat(
        string packageId,
        string version,
        string targetFramework,
        string assemblyName,
        string typeDefinitionId)
    {
        BrowserTypeImplementationHeat heat =
            await PackageTypeImplementationHeatAsync(
                packageId,
                version,
                targetFramework,
                assemblyName,
                typeDefinitionId);
        return JsonSerializer.Serialize(
            heat,
            BrowserAnalysisJsonContext.Default
                .BrowserTypeImplementationHeat);
    }

    static async Task<BrowserTypeImplementationHeat>
        PackageTypeImplementationHeatAsync(
            string packageId,
            string version,
            string targetFramework,
            string assemblyName,
            string typeDefinitionId)
    {
        await using BrowserScopeLease<BrowserInspectionScope> scopeLease =
            await BrowserPackageWorkspace.OpenScopeAsync(
                packageId,
                version,
                targetFramework);
        BrowserInspectionScope scope = scopeLease.Scope;
        BrowserPackageCoordinate coordinate = scope.Coordinates[0];
        BrowserCompileLibraryAvailability compileLibrary =
            BrowserAnalysisWireProjection.Project(
                BrowserCompileLibraryProjection.Project(coordinate.Selection));
        if (!coordinate.Selection.IsSelected)
        {
            return BrowserImplementationProfileWireProjection
                .TypeHeatUnavailable(
                    compileLibrary.Status.ToString(),
                    $"The package has no selected compile library "
                        + $"({compileLibrary.Status}).",
                    compileLibrary);
        }

        BrowserWorkspaceParticipant participant =
            scope.LibraryParticipant(coordinate, assemblyName);
        if (!scope.ImplementationParticipants.Contains(participant))
        {
            return BrowserImplementationProfileWireProjection
                .TypeHeatUnavailable(
                    "NoImplementationAssembly",
                    "The selected library has no managed implementation "
                        + "assembly.",
                    compileLibrary);
        }

        InspectionEnvelope<
            AssemblyContextEntry<AssemblyTypeImplementationHeatInspection>>
                inspection =
                    scope.UseImplementationParticipant(
                        participant,
                        (group, selectedParticipant) =>
                            TypeImplementationHeatInspectionOperation.Execute(
                                group,
                                selectedParticipant,
                                typeDefinitionId));
        return BrowserImplementationProfileWireProjection.ProjectTypeHeat(
            inspection,
            compileLibrary);
    }

    static async Task<BrowserLibraryMetrics> PackageLibraryMetricsAsync(
        string packageId,
        string version,
        string targetFramework,
        string assemblyName)
    {
        await using BrowserScopeLease<BrowserInspectionScope> scopeLease =
            await BrowserPackageWorkspace.OpenScopeAsync(
                packageId,
                version,
                targetFramework);
        BrowserInspectionScope scope = scopeLease.Scope;
        BrowserPackageCoordinate coordinate = scope.Coordinates[0];
        BrowserCompileLibraryAvailability compileLibrary =
            BrowserAnalysisWireProjection.Project(
                BrowserCompileLibraryProjection.Project(coordinate.Selection));
        if (!coordinate.Selection.IsSelected)
        {
            return UnavailableLibraryMetrics(
                "unavailable",
                $"The package has no selected compile library ({compileLibrary.Status}).",
                compileLibrary);
        }

        BrowserWorkspaceParticipant participant =
            scope.LibraryParticipant(coordinate, assemblyName);
        if (!scope.ImplementationParticipants.Contains(participant))
        {
            return UnavailableLibraryMetrics(
                "unavailable",
                "The selected library has no managed implementation assembly.",
                compileLibrary);
        }

        AssemblyContextEntry<LibraryMetricsResult> entry =
            scope.UseImplementationParticipant(
                participant,
                AssemblyContextLibraryMetricsQuery.ExecuteParticipant);
        return ProjectLibraryMetrics(entry, compileLibrary);
    }

    static async Task<BrowserLibraryNamespaceLeverage>
        PackageLibraryNamespaceLeverageAsync(
            string packageId,
            string version,
            string targetFramework,
            string assemblyName)
    {
        await using BrowserScopeLease<BrowserInspectionScope> scopeLease =
            await BrowserPackageWorkspace.OpenScopeAsync(
                packageId,
                version,
                targetFramework);
        BrowserInspectionScope scope = scopeLease.Scope;
        BrowserPackageCoordinate coordinate = scope.Coordinates[0];
        BrowserCompileLibraryAvailability compileLibrary =
            BrowserAnalysisWireProjection.Project(
                BrowserCompileLibraryProjection.Project(coordinate.Selection));
        if (!coordinate.Selection.IsSelected)
        {
            return UnavailableLibraryNamespaceLeverage(
                "unavailable",
                $"The package has no selected compile library ({compileLibrary.Status}).",
                compileLibrary);
        }

        BrowserWorkspaceParticipant participant =
            scope.LibraryParticipant(coordinate, assemblyName);
        AssemblyContextEntry<LibrarySurfaceLeverageResult> entry =
            scope.UseMetadataParticipant(
                participant,
                static (group, selectedParticipant) =>
                    AssemblyContextLibrarySurfaceLeverageQuery
                        .ExecuteNamespaceIndexParticipant(
                            group,
                            selectedParticipant));
        return ProjectLibraryNamespaceLeverage(entry, compileLibrary);
    }

    static async Task<BrowserLibraryTypeLeverageShard>
        PackageNamespaceTypeLeverageAsync(
            string packageId,
            string version,
            string targetFramework,
            string assemblyName,
            string exactNamespace)
    {
        await using BrowserScopeLease<BrowserInspectionScope> scopeLease =
            await BrowserPackageWorkspace.OpenScopeAsync(
                packageId,
                version,
                targetFramework);
        BrowserInspectionScope scope = scopeLease.Scope;
        BrowserPackageCoordinate coordinate = scope.Coordinates[0];
        BrowserCompileLibraryAvailability compileLibrary =
            BrowserAnalysisWireProjection.Project(
                BrowserCompileLibraryProjection.Project(coordinate.Selection));
        if (!coordinate.Selection.IsSelected)
        {
            return UnavailableLibraryTypeLeverageShard(
                "unavailable",
                $"The package has no selected compile library ({compileLibrary.Status}).",
                compileLibrary);
        }

        BrowserWorkspaceParticipant participant =
            scope.LibraryParticipant(coordinate, assemblyName);
        AssemblyContextEntry<LibrarySurfaceLeverageResult> entry =
            scope.UseMetadataParticipant(
                participant,
                (group, selectedParticipant) =>
                    AssemblyContextLibrarySurfaceLeverageQuery
                        .ExecuteTypeShardParticipant(
                            group,
                            selectedParticipant,
                            exactNamespace));
        return ProjectLibraryTypeLeverageShard(entry, compileLibrary);
    }

    internal static BrowserLibraryNamespaceLeverage
        ProjectLibraryNamespaceLeverage(
            AssemblyContextEntry<LibrarySurfaceLeverageResult> entry,
            BrowserCompileLibraryAvailability compileLibrary) =>
        entry switch
        {
            AssemblyContextEntry<LibrarySurfaceLeverageResult>.Available
                available => ProjectLibraryNamespaceLeverage(
                    available.Value,
                    compileLibrary),
            AssemblyContextEntry<LibrarySurfaceLeverageResult>.Rejected
                rejected => UnavailableLibraryNamespaceLeverage(
                    "unavailable",
                    $"{rejected.Subject.Identity.Name}: "
                        + $"{rejected.Failure.Kind} "
                        + $"({rejected.Failure.Detail})",
                    compileLibrary),
            AssemblyContextEntry<LibrarySurfaceLeverageResult>.Failed failed =>
                UnavailableLibraryNamespaceLeverage(
                    "failed",
                    $"{failed.Subject.Identity.Name}: {failed.Error.Message}",
                    compileLibrary),
            _ => throw new InvalidOperationException(
                "Unknown Library surface leverage assembly-context result."),
        };

    internal static BrowserLibraryNamespaceLeverage
        ProjectLibraryNamespaceLeverage(
            LibrarySurfaceLeverageResult result,
            BrowserCompileLibraryAvailability compileLibrary) =>
        result switch
        {
            LibrarySurfaceLeverageResult.AvailableIndex available =>
                ProjectLibraryNamespaceLeverage(
                    available.Index,
                    compileLibrary),
            LibrarySurfaceLeverageResult.Rejected rejected =>
                UnavailableLibraryNamespaceLeverage(
                    "unavailable",
                    $"Signature-use acquisition was rejected "
                        + $"({rejected.Kind}): {rejected.Detail}",
                    compileLibrary),
            _ => throw new InvalidOperationException(
                "Unknown Library surface leverage result."),
        };

    private static BrowserLibraryNamespaceLeverage
        ProjectLibraryNamespaceLeverage(
            ILInspector.Research.LibraryStructuralNamespaceLeverageIndex index,
            BrowserCompileLibraryAvailability compileLibrary)
        => new(
            1,
            "available",
            index.MethodologyVersion,
            index.EvidenceMode.ToString().ToLowerInvariant(),
            index.Disposition.ToString().ToLowerInvariant(),
            new(
                index.SignatureUse.Coverage.Considered,
                index.SignatureUse.Coverage.Examined,
                index.SignatureUse.Coverage.Unavailable,
                index.SignatureUse.Coverage.Limited),
            [
                .. index.Rows.Select(row =>
                    new BrowserLibraryNamespaceLeverageRow(
                        row.Namespace,
                        row.TypeCount,
                        row.ExternalIncomingSourceTypeCount,
                        row.TopLeverage)),
            ],
            [
                .. index.SignatureUse.Diagnostics.Select(
                    static diagnostic => diagnostic.Detail),
            ],
            null,
            compileLibrary);

    internal static BrowserLibraryTypeLeverageShard
        ProjectLibraryTypeLeverageShard(
            AssemblyContextEntry<LibrarySurfaceLeverageResult> entry,
            BrowserCompileLibraryAvailability compileLibrary) =>
        entry switch
        {
            AssemblyContextEntry<LibrarySurfaceLeverageResult>.Available
                available => ProjectLibraryTypeLeverageShard(
                    available.Value,
                    compileLibrary),
            AssemblyContextEntry<LibrarySurfaceLeverageResult>.Rejected
                rejected => UnavailableLibraryTypeLeverageShard(
                    "unavailable",
                    $"{rejected.Subject.Identity.Name}: "
                        + $"{rejected.Failure.Kind} "
                        + $"({rejected.Failure.Detail})",
                    compileLibrary),
            AssemblyContextEntry<LibrarySurfaceLeverageResult>.Failed failed =>
                UnavailableLibraryTypeLeverageShard(
                    "failed",
                    $"{failed.Subject.Identity.Name}: {failed.Error.Message}",
                    compileLibrary),
            _ => throw new InvalidOperationException(
                "Unknown Library Type-leverage assembly-context result."),
        };

    internal static BrowserLibraryTypeLeverageShard
        ProjectLibraryTypeLeverageShard(
            LibrarySurfaceLeverageResult result,
            BrowserCompileLibraryAvailability compileLibrary) =>
        result switch
        {
            LibrarySurfaceLeverageResult.AvailableShard available =>
                ProjectLibraryTypeLeverageShard(
                    available.Shard,
                    compileLibrary),
            LibrarySurfaceLeverageResult.Rejected rejected =>
                UnavailableLibraryTypeLeverageShard(
                    "unavailable",
                    $"Signature-use acquisition was rejected "
                        + $"({rejected.Kind}): {rejected.Detail}",
                    compileLibrary),
            _ => throw new InvalidOperationException(
                "Unknown Library Type-leverage result."),
        };

    private static BrowserLibraryTypeLeverageShard
        ProjectLibraryTypeLeverageShard(
            ILInspector.Research.LibraryStructuralTypeLeverageShard document,
            BrowserCompileLibraryAvailability compileLibrary)
    {
        Dictionary<
            ILInspector.Metadata.MetadataTypeDefinitionAddress,
            string> ids =
                document.Rows.ToDictionary(
                    static row => row.Type,
                    static row => row.Name.ToEscapedFullName());
        return new(
            BrowserLibraryTypeLeverageSchemaVersion,
            "available",
            document.MethodologyVersion,
            document.EvidenceMode.ToString().ToLowerInvariant(),
            document.Namespace,
            document.RoleDisposition.ToString().ToLowerInvariant(),
            new(
                document.SignatureUse.Coverage.Considered,
                document.SignatureUse.Coverage.Examined,
                document.SignatureUse.Coverage.Unavailable,
                document.SignatureUse.Coverage.Limited),
            [
                .. document.Rows.Select(row =>
                    new BrowserLibraryTypeLeverageRow(
                        ids[row.Type],
                        row.Name.ToMetadataFullName(),
                        row.DesignationEligible,
                        row.SignatureIncomingDegree,
                        row.SignatureOutgoingDegree,
                        row.Role.ToString().ToLowerInvariant(),
                        row.Pole switch
                        {
                            LibraryStructuralTypePole.SeaLevel =>
                                BrowserLibraryStructuralTypePole.SeaLevel,
                            LibraryStructuralTypePole.MountainPeak =>
                                BrowserLibraryStructuralTypePole.MountainPeak,
                            null => null,
                            _ => throw new InvalidOperationException(
                                "Unknown structural Type pole."),
                        })),
            ],
            [
                .. document.SeaLevel.Types.Select(type => ids[type]),
            ],
            [
                .. document.MountainPeak.Types.Select(type => ids[type]),
            ],
            [
                .. document.SignatureUse.Diagnostics.Select(
                    static diagnostic => diagnostic.Detail),
            ],
            null,
            compileLibrary);
    }

    internal static BrowserLibraryNamespaceLeverage
        UnavailableLibraryNamespaceLeverage(
            string outcome,
            string failure,
            BrowserCompileLibraryAvailability compileLibrary) =>
        new(
            1,
            outcome,
            null,
            null,
            null,
            null,
            [],
            [],
            failure,
            compileLibrary);

    internal static BrowserLibraryTypeLeverageShard
        UnavailableLibraryTypeLeverageShard(
            string outcome,
            string failure,
            BrowserCompileLibraryAvailability compileLibrary) =>
        new(
            BrowserLibraryTypeLeverageSchemaVersion,
            outcome,
            null,
            null,
            null,
            null,
            null,
            [],
            [],
            [],
            [],
            failure,
            compileLibrary);

    static BrowserLibraryMetrics ProjectLibraryMetrics(
        AssemblyContextEntry<LibraryMetricsResult> entry,
        BrowserCompileLibraryAvailability compileLibrary) =>
        entry switch
        {
            AssemblyContextEntry<LibraryMetricsResult>.Available available =>
                ProjectLibraryMetrics(available.Value, compileLibrary),
            AssemblyContextEntry<LibraryMetricsResult>.Rejected rejected =>
                UnavailableLibraryMetrics(
                    "unavailable",
                    $"{rejected.Subject.Identity.Name}: "
                        + $"{rejected.Failure.Kind} ({rejected.Failure.Detail})",
                    compileLibrary),
            AssemblyContextEntry<LibraryMetricsResult>.Failed failed =>
                UnavailableLibraryMetrics(
                    "failed",
                    $"{failed.Subject.Identity.Name}: {failed.Error.Message}",
                    compileLibrary),
            _ => throw new InvalidOperationException(
                "Unknown Library Metrics assembly-context result."),
        };

    static BrowserLibraryMetrics ProjectLibraryMetrics(
        LibraryMetricsResult result,
        BrowserCompileLibraryAvailability compileLibrary) =>
        result switch
        {
            LibraryMetricsResult.Available available =>
                new(
                    "available",
                    available.Document.MethodologyVersion,
                    new(
                        available.Document.Population.PhysicalEvidenceBodyCount,
                        available.Document.Population.ProfiledPhysicalEvidenceBodyCount,
                        available.Document.Population.LogicalOwnerCount,
                        available.Document.Population.CompleteProfileCount,
                        available.Document.Population.IncompleteProfileCount),
                    [
                        .. available.Document.Distributions.Select(
                            distribution => new BrowserLibraryMetricsDistribution(
                                distribution.Metric.ToString(),
                                distribution.CompleteBodyCount,
                                distribution.Minimum,
                                distribution.P50,
                                distribution.P90,
                                distribution.P95,
                                distribution.P99,
                                distribution.Maximum)),
                    ],
                    new(
                        available.Document.AsyncStateMachinePresence.Name,
                        available.Document.AsyncStateMachinePresence.CompleteBodyCount,
                        available.Document.AsyncStateMachinePresence.PresentCount,
                        available.Document.AsyncStateMachinePresence.AbsentCount),
                    [
                        .. available.Document.TypeSummaries.Select(
                            summary => new BrowserLibraryMetricsType(
                                LibraryMetricsTypeKey(summary.Type),
                                summary.Type.ToQualifiedDisplayString(),
                                summary.Type.Namespace,
                                summary.Type.Name,
                                summary.BodyCount,
                                summary.InstructionCount,
                                summary.ComplexityTotal,
                                summary.LoopCount,
                                summary.DirectCallCount,
                                summary.AllocationCount)),
                    ],
                    [
                        .. available.Document.EntangledRelationships.Select(
                            relationship => new BrowserLibraryMetricsRelationship(
                                LibraryMetricsTypeKey(relationship.Source),
                                relationship.Source.ToQualifiedDisplayString(),
                                LibraryMetricsTypeKey(relationship.Target),
                                relationship.Target.ToQualifiedDisplayString(),
                                relationship.CallSiteCount,
                                relationship.SourceDegree,
                                relationship.TargetDegree)),
                    ],
                    [
                        .. available.Document.Diagnostics.Select(
                            diagnostic => diagnostic.Message),
                    ],
                    null,
                    compileLibrary),
            LibraryMetricsResult.Unavailable unavailable =>
                UnavailableLibraryMetrics(
                    "unavailable",
                    unavailable.Outcome.Message,
                    compileLibrary),
            LibraryMetricsResult.NoMetadata =>
                UnavailableLibraryMetrics(
                    "unavailable",
                    "The library contains no managed metadata.",
                    compileLibrary),
            LibraryMetricsResult.Failed failed =>
                UnavailableLibraryMetrics(
                    "failed",
                    failed.Error.Message,
                    compileLibrary),
            _ => throw new InvalidOperationException(
                "Unknown Library Metrics result."),
        };

    internal static string LibraryMetricsTypeKey(ILAnalysis.TypeRef type) =>
        LibraryStructuralReport.TypeKey(type);

    static BrowserLibraryMetrics UnavailableLibraryMetrics(
        string outcome,
        string failure,
        BrowserCompileLibraryAvailability compileLibrary) =>
        new(
            outcome,
            null,
            null,
            [],
            null,
            [],
            [],
            [],
            failure,
            compileLibrary);

    static async Task<BrowserPackagePerformance> PackagePerformanceAsync(
        string packageId,
        string version,
        string targetFramework,
        string assemblyName)
    {
        await using BrowserScopeLease<BrowserInspectionScope> scopeLease =
            await BrowserPackageWorkspace.OpenScopeAsync(
                packageId,
                version,
                targetFramework);
        BrowserInspectionScope scope = scopeLease.Scope;
        BrowserPackageCoordinate coordinate = scope.Coordinates[0];
        BrowserCompileLibraryAvailability compileLibrary =
            BrowserAnalysisWireProjection.Project(
                BrowserCompileLibraryProjection.Project(coordinate.Selection));
        if (!coordinate.Selection.IsSelected)
        {
            return new BrowserPackagePerformance(
                Members: [],
                InspectionError: null,
                NonPublicOpportunities: 0,
                TotalOpportunities: 0,
                compileLibrary);
        }

        BrowserWorkspaceParticipant participant =
            scope.LibraryParticipant(coordinate, assemblyName);
        ImmutableArray<BrowserWorkspaceParticipant> participants =
            [participant];

        AssemblyContextOptimizationOpportunitiesResult result =
            scope.UseMetadataParticipant(
                participant,
                AssemblyContextOptimizationOpportunitiesQuery.ExecuteParticipant);
        // The ranking only publishes members the site can navigate to, which is the same
        // browsable surface the package facade renders. The projection is DTO-neutral shared
        // mechanics in DotnetInspect.Web.Core; this facade never reaches for a sibling's wire
        // record to decide what is navigable.
        BrowserWorkspaceParticipant? surfaceParticipant =
            scope.TryGetSurfaceParticipant(participant);
        BrowserSurfaceProjection.Surface? surface = surfaceParticipant is null
            ? null
            : BrowserPackageSurfaceProjection.ProjectParticipantSurface(
                scope,
                surfaceParticipant);
        HashSet<(
            string Assembly,
            string Type,
            string Selector)> navigableMembers =
        [
            .. (surface?.Types ?? [])
                .SelectMany(type =>
                type.Api.Select(member => (
                    type.Assembly,
                    type.DefinitionId,
                    member.StableSelector))),
        ];

        var failures = new List<string>();
        if (!string.IsNullOrWhiteSpace(surface?.InspectionError))
            failures.Add($"API surface: {surface.InspectionError}");
        foreach (AssemblyContextEntry<
            AssemblyOptimizationOpportunityRanking> entry
            in result.Assemblies.Assemblies)
        {
            switch (entry)
            {
                case AssemblyContextEntry<
                    AssemblyOptimizationOpportunityRanking>.Rejected
                    rejected:
                    failures.Add(
                        $"{rejected.Subject.Identity.Name}: "
                        + $"{rejected.Failure.Kind} "
                        + $"({rejected.Failure.Detail})");
                    break;
                case AssemblyContextEntry<
                    AssemblyOptimizationOpportunityRanking>.Failed failed:
                    failures.Add(
                        $"{failed.Subject.Identity.Name}: "
                        + failed.Error.Message);
                    break;
                case AssemblyContextEntry<
                    AssemblyOptimizationOpportunityRanking>.Available
                    available:
                    failures.AddRange(
                        available.Value.Diagnostics.Select(
                            diagnostic =>
                                $"{available.Subject.Identity.Name}: "
                                + $"performance analysis incomplete for "
                                + $"{diagnostic.Method}: "
                                + diagnostic.Message));
                    failures.AddRange(
                        available.Value.ApiSurfaceInspectionFailures
                            .Select(
                                failure =>
                                    $"{available.Subject.Identity.Name}: "
                                    + $"{failure.Operation}: "
                                    + failure.Detail));
                    break;
            }
        }

        BrowserPerformanceMember[] members =
            ApplyPerformanceMemberLimit(
                PerformanceMembers(
                    result,
                    participants,
                    scope,
                    navigableMembers),
                failures);

        return new BrowserPackagePerformance(
            members,
            failures.Count == 0
                ? null
                : string.Join("; ", failures),
            result.NonPublicOpportunities,
            result.TotalOpportunities,
            compileLibrary);
    }

    static IEnumerable<BrowserPerformanceMember> PerformanceMembers(
        AssemblyContextOptimizationOpportunitiesResult result,
        ImmutableArray<BrowserWorkspaceParticipant> participants,
        BrowserInspectionScope scope,
        HashSet<(
            string Assembly,
            string Type,
            string Selector)> navigableMembers)
    {
        foreach (AssemblyContextOptimizationOpportunityMember member
            in result.RankedMembers)
        {
            if (member.Member.PublicMember is not { } publicMember)
                continue;

            BrowserWorkspaceParticipant analysisParticipant =
                participants.Single(candidate =>
                    ReferenceEquals(
                        candidate.Assembly.Registration,
                        member.Subject.Registration));
            BrowserWorkspaceParticipant? surfaceParticipant =
                scope.TryGetSurfaceParticipant(analysisParticipant);
            if (surfaceParticipant is null
                || !navigableMembers.Contains((
                    surfaceParticipant.Asset.AssemblyName,
                    publicMember.Type,
                    publicMember.StableSelector)))
            {
                continue;
            }

            yield return new BrowserPerformanceMember(
                surfaceParticipant.Asset.AssemblyName,
                publicMember.Type,
                publicMember.Member,
                publicMember.StableSelector,
                [.. publicMember.BodyTokens],
                member.Member.Ranking.Opportunities.Length,
                member.Member.Ranking.InLoopCount,
                [.. member.Member.Ranking.Shapes],
                member.Member.Ranking.Confidence);
        }
    }

    internal static BrowserPerformanceMember[] ApplyPerformanceMemberLimit(
        IEnumerable<BrowserPerformanceMember> candidates,
        ICollection<string> failures)
    {
        const int MemberLimit = 200;
        var members = new List<BrowserPerformanceMember>(MemberLimit);
        foreach (BrowserPerformanceMember candidate in candidates)
        {
            if (members.Count == MemberLimit)
            {
                failures.Add(
                    $"Performance ranking truncated after the top "
                    + $"{MemberLimit} navigable public members.");
                break;
            }

            members.Add(candidate);
        }

        return [.. members];
    }
}
