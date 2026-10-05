using System.Collections.Immutable;
using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using System.Text.Json;
using DotnetInspector.PackageQueries;
using DotnetInspector.Queries;
using DotnetInspector.ResearchSections;
using DotnetInspector.Sections;
using ILInspector.Metadata;
using ILInspector.Research;
using QuerySpace.Composition;
using QuerySpace.Rows;
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
    private const int BrowserLibraryStructuralSalienceSchemaVersion = 2;
    private const int BrowserDependencyStructureEdgeLimit = 64;

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
        if (implementationBodySelected)
        {
            await using BrowserMemberResolution.ScopedImplementationParticipant
                exact =
                    await BrowserMemberResolution
                        .ImplementationParticipantAsync(
                            packageId,
                            version,
                            targetFramework,
                            assemblyName);
            AssemblyMethodAnalysis exactAnalysis =
                BrowserSurfaceProjection.Require(
                    exact.Scope.UseImplementationParticipant(
                        exact.Participant,
                        (group, participant) =>
                            AssemblyContextMethodAnalysisQuery
                                .ExecuteParticipant(
                                    group,
                                    participant,
                                    metadataToken)),
                    $"Facts for '{typeIdentity}.{memberName}'");
            return ProjectMemberFacts(exactAnalysis);
        }

        await using BrowserMemberResolution.ScopedResolution resolved =
            await BrowserMemberResolution.ImplementationMemberAsync(
                packageId,
                version,
                targetFramework,
                assemblyName,
                typeIdentity,
                memberName,
                selectorKey,
                0);
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

        return ProjectMemberFacts(analysis);
    }

    internal static BrowserMemberFacts ProjectMemberFacts(
        AssemblyMethodAnalysis analysis) =>
        new(
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
    /// Ungraded unsafe findings for one exact package library. Analysis owns
    /// the finding categories and evidence; the query owns public-member
    /// attribution; this host only bounds and formats the wire result.
    /// </summary>
    [JSExport]
    public static async Task<string> QueryPackageUnsafeFindings(
        string packageId,
        string version,
        string targetFramework,
        string assemblyName)
    {
        BrowserPackageUnsafeFindings findings =
            await PackageUnsafeFindingsAsync(
                packageId, version, targetFramework, assemblyName);
        return JsonSerializer.Serialize(
            findings,
            BrowserAnalysisJsonContext.Default
                .BrowserPackageUnsafeFindings);
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
    /// Research-owned dependency topology for one exact package library. This
    /// operation runs only after an explicit Browser request.
    /// </summary>
    [JSExport]
    public static async Task<string> QueryPackageLibraryDependencyStructure(
        string packageId,
        string version,
        string targetFramework,
        string assemblyName)
    {
        BrowserLibraryDependencyStructure structure =
            await PackageLibraryDependencyStructureAsync(
                packageId, version, targetFramework, assemblyName);
        return JsonSerializer.Serialize(
            structure,
            BrowserAnalysisJsonContext.Default
                .BrowserLibraryDependencyStructure);
    }

    /// <summary>
    /// Signature-only namespace and Type leverage for one exact package
    /// Library. This returns every exact namespace shard without running
    /// implementation profiles or body analysis.
    /// </summary>
    [JSExport]
    public static async Task<string> QueryPackageLibraryStructuralSalience(
        string packageId,
        string version,
        string targetFramework,
        string assemblyName)
    {
        BrowserLibraryStructuralSalience salience =
            await PackageLibraryStructuralSalienceAsync(
                packageId,
                version,
                targetFramework,
                assemblyName);
        return JsonSerializer.Serialize(
            salience,
            BrowserAnalysisJsonContext.Default
                .BrowserLibraryStructuralSalience);
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

    /// <summary>
    /// Top Leverage designation for every method declared by one Type in a
    /// package implementation Library.
    /// </summary>
    [JSExport]
    public static async Task<string> QueryPackageTypeMethodLeverage(
        string packageId,
        string version,
        string targetFramework,
        string assemblyName,
        string typeDefinitionId)
    {
        BrowserTypeMethodLeverage leverage =
            await PackageTypeMethodLeverageAsync(
                packageId,
                version,
                targetFramework,
                assemblyName,
                typeDefinitionId);
        return JsonSerializer.Serialize(
            leverage,
            BrowserAnalysisJsonContext.Default
                .BrowserTypeMethodLeverage);
    }

    static async Task<BrowserTypeMethodLeverage>
        PackageTypeMethodLeverageAsync(
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
                BrowserCompileLibraryProjection.Project(
                    coordinate.Selection));
        if (!coordinate.Selection.IsSelected)
        {
            return BrowserImplementationProfileWireProjection
                .TypeMethodLeverageUnavailable(
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
                .TypeMethodLeverageUnavailable(
                    "NoImplementationAssembly",
                    "The selected library has no managed implementation "
                        + "assembly.",
                    compileLibrary);
        }

        InspectionEnvelope<
            AssemblyContextEntry<AssemblyTypeMethodLeverageInspection>>
                inspection =
                    scope.UseImplementationParticipant(
                        participant,
                        (group, selectedParticipant) =>
                            TypeMethodLeverageInspectionOperation.Execute(
                                group,
                                selectedParticipant,
                                typeDefinitionId));
        return BrowserImplementationProfileWireProjection
            .ProjectTypeMethodLeverage(
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
                AssemblyContextLibraryMetricsQuery
                    .ExecuteParticipant);
        return ProjectLibraryMetrics(entry, compileLibrary);
    }

    static async Task<BrowserLibraryDependencyStructure>
        PackageLibraryDependencyStructureAsync(
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
        if (!coordinate.Selection.IsSelected)
        {
            return UnavailableLibraryDependencyStructure(
                "unavailable",
                "The package has no selected compile library "
                    + $"({coordinate.Selection.Status}).");
        }

        BrowserWorkspaceParticipant participant =
            scope.LibraryParticipant(coordinate, assemblyName);
        if (!scope.ImplementationParticipants.Contains(participant))
        {
            return UnavailableLibraryDependencyStructure(
                "unavailable",
                "The selected library has no managed implementation assembly.");
        }

        AssemblyContextEntry<LibraryDependencyStructureResult> entry =
            scope.UseImplementationParticipant(
                participant,
                AssemblyContextLibraryDependencyStructureQuery
                    .ExecuteParticipant);
        return ProjectLibraryDependencyStructure(entry);
    }

    static async Task<BrowserLibraryStructuralSalience>
        PackageLibraryStructuralSalienceAsync(
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
            return UnavailableLibraryStructuralSalience(
                "unavailable",
                $"The package has no selected compile library ({compileLibrary.Status}).",
                compileLibrary);
        }

        BrowserWorkspaceParticipant participant =
            scope.LibraryParticipant(coordinate, assemblyName);
        if (scope.ImplementationParticipants.Contains(participant))
        {
            AssemblyContextEntry<LibraryTypeLeverageResult> entry =
                scope.UseImplementationParticipant(
                    participant,
                    static (group, selectedParticipant) =>
                        AssemblyContextLibraryTypeLeverageQuery
                            .ExecuteParticipant(
                                group,
                                selectedParticipant));
            return ProjectLibraryStructuralSalience(entry, compileLibrary);
        }

        AssemblyContextEntry<LibrarySurfaceLeverageResult> surface =
            scope.UseMetadataParticipant(
                participant,
                static (group, selectedParticipant) =>
                    AssemblyContextLibrarySurfaceLeverageQuery
                        .ExecuteExhaustiveParticipant(
                            group,
                            selectedParticipant));
        return ProjectLibraryStructuralSalience(
            surface,
            UnavailableTypeLeverageChannel(
                "body-use",
                "NoImplementationAssembly",
                "The selected library has no managed implementation assembly."),
            compileLibrary);
    }

    internal static BrowserLibraryStructuralSalience
        ProjectLibraryStructuralSalience(
            AssemblyContextEntry<LibrarySurfaceLeverageResult> entry,
            BrowserCompileLibraryAvailability compileLibrary) =>
        ProjectLibraryStructuralSalience(
            entry,
            UnavailableTypeLeverageChannel(
                "body-use",
                "NotRequested",
                "Implementation Type leverage was not requested."),
            compileLibrary);

    private static BrowserLibraryStructuralSalience
        ProjectLibraryStructuralSalience(
            AssemblyContextEntry<LibrarySurfaceLeverageResult> entry,
            BrowserLibraryTypeLeverageChannel implementation,
            BrowserCompileLibraryAvailability compileLibrary) =>
        entry switch
        {
            AssemblyContextEntry<LibrarySurfaceLeverageResult>.Available
                available => ProjectLibraryStructuralSalience(
                    available.Value,
                    implementation,
                    compileLibrary),
            AssemblyContextEntry<LibrarySurfaceLeverageResult>.Rejected
                rejected => UnavailableLibraryStructuralSalience(
                    "unavailable",
                    $"{rejected.Subject.Identity.Name}: "
                        + $"{rejected.Failure.Kind} "
                        + $"({rejected.Failure.Detail})",
                    compileLibrary),
            AssemblyContextEntry<LibrarySurfaceLeverageResult>.Failed failed =>
                UnavailableLibraryStructuralSalience(
                    "failed",
                    $"{failed.Subject.Identity.Name}: {failed.Error.Message}",
                    compileLibrary),
            _ => throw new InvalidOperationException(
                "Unknown structural salience assembly-context result."),
        };

    internal static BrowserLibraryStructuralSalience
        ProjectLibraryStructuralSalience(
            LibrarySurfaceLeverageResult result,
            BrowserLibraryTypeLeverageChannel implementation,
            BrowserCompileLibraryAvailability compileLibrary) =>
        result switch
        {
            LibrarySurfaceLeverageResult.AvailableExhaustive available =>
                ProjectLibraryStructuralSalience(
                    available.Document,
                    implementation,
                    compileLibrary),
            LibrarySurfaceLeverageResult.Rejected rejected =>
                UnavailableLibraryStructuralSalience(
                    "unavailable",
                    $"Signature-use acquisition was rejected "
                        + $"({rejected.Kind}): {rejected.Detail}",
                    compileLibrary),
            _ => throw new InvalidOperationException(
                "Expected exhaustive structural salience result."),
        };

    internal static BrowserLibraryStructuralSalience
        ProjectLibraryStructuralSalience(
            AssemblyContextEntry<LibraryTypeLeverageResult> entry,
            BrowserCompileLibraryAvailability compileLibrary)
        => entry switch
        {
            AssemblyContextEntry<LibraryTypeLeverageResult>.Available
                available => ProjectLibraryStructuralSalience(
                    available.Value,
                    compileLibrary),
            AssemblyContextEntry<LibraryTypeLeverageResult>.Rejected
                rejected => UnavailableLibraryStructuralSalience(
                    "unavailable",
                    $"{rejected.Subject.Identity.Name}: "
                        + $"{rejected.Failure.Kind} "
                        + $"({rejected.Failure.Detail})",
                    compileLibrary),
            AssemblyContextEntry<LibraryTypeLeverageResult>.Failed failed =>
                UnavailableLibraryStructuralSalience(
                    "failed",
                    $"{failed.Subject.Identity.Name}: {failed.Error.Message}",
                    compileLibrary),
            _ => throw new InvalidOperationException(
                "Unknown Library Type-leverage assembly-context result."),
        };

    internal static BrowserLibraryStructuralSalience
        ProjectLibraryStructuralSalience(
            LibraryTypeLeverageResult result,
            BrowserCompileLibraryAvailability compileLibrary) =>
        result switch
        {
            LibraryTypeLeverageResult.Available available => new(
                BrowserLibraryStructuralSalienceSchemaVersion,
                ProjectSurfaceTypeLeverageChannel(
                    available.Document.Surface),
                ProjectImplementationTypeLeverageChannel(
                    available.Document.Implementation),
                compileLibrary),
            LibraryTypeLeverageResult.Rejected rejected =>
                UnavailableLibraryStructuralSalience(
                    "unavailable",
                    $"Signature-use acquisition was rejected "
                        + $"({rejected.Kind}): {rejected.Detail}",
                    compileLibrary),
            _ => throw new InvalidOperationException(
                "Unknown Library Type-leverage result."),
        };

    private static BrowserLibraryStructuralSalience
        ProjectLibraryStructuralSalience(
            LibraryStructuralSalienceDocument document,
            BrowserLibraryTypeLeverageChannel implementation,
            BrowserCompileLibraryAvailability compileLibrary)
        => new(
            BrowserLibraryStructuralSalienceSchemaVersion,
            ProjectSurfaceTypeLeverageChannel(document),
            implementation,
            compileLibrary);

    private static BrowserLibraryTypeLeverageChannel
        ProjectSurfaceTypeLeverageChannel(
            LibraryStructuralSalienceDocument document) =>
        new(
            "available",
            document.MethodologyVersion,
            "signature",
            ProjectLibraryNamespaceLeverage(document.NamespaceIndex),
            [
                .. document.TypeLeverageShards.Select(
                    ProjectLibraryTypeLeverageShard),
            ],
            null,
            null);

    private static BrowserLibraryTypeLeverageChannel
        ProjectImplementationTypeLeverageChannel(
            LibraryBodyTypeLeverageResult result) =>
        result switch
        {
            LibraryBodyTypeLeverageResult.Available available => new(
                "available",
                LibraryStructuralSalience.CurrentMethodologyVersion,
                "body-use",
                null,
                [
                    .. available.Shards.Select(
                        ProjectLibraryBodyTypeLeverageShard),
                ],
                null,
                null),
            LibraryBodyTypeLeverageResult.Rejected rejected =>
                UnavailableTypeLeverageChannel(
                    "body-use",
                    rejected.Kind.ToString(),
                    rejected.Detail),
            _ => throw new InvalidOperationException(
                "Unknown body Type-leverage result."),
        };

    private static BrowserLibraryNamespaceLeverageIndex
        ProjectLibraryNamespaceLeverage(
            LibraryStructuralNamespaceLeverageIndex index) =>
        new(
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
            ]);

    private static BrowserLibraryTypeLeverageShard
        ProjectLibraryTypeLeverageShard(
            LibraryStructuralTypeLeverageShard document)
    {
        Dictionary<
            ILInspector.Metadata.MetadataTypeDefinitionAddress,
            string> ids =
                document.Rows.ToDictionary(
                    static row => row.Type,
                    static row => row.Name.ToEscapedFullName());
        return new(
            document.Namespace,
            document.RoleDisposition.ToString().ToLowerInvariant(),
            new(
                document.SignatureUse.Coverage.Considered,
                document.SignatureUse.Coverage.Examined,
                document.SignatureUse.Coverage.Unavailable,
                document.SignatureUse.Coverage.Limited),
            null,
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
            ]);
    }

    private static BrowserLibraryTypeLeverageShard
        ProjectLibraryBodyTypeLeverageShard(
            LibraryStructuralBodyTypeLeverageShard document)
    {
        Dictionary<
            ILInspector.Metadata.MetadataTypeDefinitionAddress,
            string> ids =
                document.Rows.ToDictionary(
                    static row => row.Type,
                    static row => row.Name.ToEscapedFullName());
        ILAnalysis.AnalysisLibraryBodyUseCoverage coverage =
            document.BodyUse.Coverage;
        return new(
            document.Namespace,
            document.RoleDisposition.ToString().ToLowerInvariant(),
            new(
                document.TypeInventory.Coverage.Considered,
                document.TypeInventory.Coverage.Examined,
                document.TypeInventory.Coverage.Unavailable,
                document.TypeInventory.Coverage.Limited),
            new(
                coverage.BodiesConsidered,
                coverage.BodiesExamined,
                coverage.BodiesPhysicalOnly,
                coverage.BodiesUnavailable,
                coverage.BodiesLimited,
                coverage.OperandsConsidered,
                coverage.OperandsExamined,
                coverage.OperandsUnavailable,
                coverage.OperandsLimited),
            [
                .. document.Rows.Select(row =>
                    new BrowserLibraryTypeLeverageRow(
                        ids[row.Type],
                        row.Name.ToMetadataFullName(),
                        row.DesignationEligible,
                        row.BodyIncomingDegree,
                        row.BodyOutgoingDegree,
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
                .. document.TypeInventory.Diagnostics
                    .Select(static diagnostic => diagnostic.Detail)
                    .Concat(
                        document.BodyUse.Diagnostics.Select(
                            static diagnostic => diagnostic.Detail)),
            ]);
    }

    private static BrowserLibraryTypeLeverageChannel
        UnavailableTypeLeverageChannel(
            string evidenceMode,
            string kind,
            string failure) =>
        new(
            "unavailable",
            null,
            evidenceMode,
            null,
            [],
            failure,
            kind);

    internal static BrowserLibraryStructuralSalience
        UnavailableLibraryStructuralSalience(
            string outcome,
            string failure,
            BrowserCompileLibraryAvailability compileLibrary) =>
        new(
            BrowserLibraryStructuralSalienceSchemaVersion,
            new(
                outcome,
                null,
                "signature",
                null,
                [],
                failure,
                "SurfaceUnavailable"),
            new(
                outcome,
                null,
                "body-use",
                null,
                [],
                failure,
                "SurfaceUnavailable"),
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

    static BrowserLibraryDependencyStructure
        ProjectLibraryDependencyStructure(
            AssemblyContextEntry<LibraryDependencyStructureResult> entry) =>
        entry switch
        {
            AssemblyContextEntry<
                LibraryDependencyStructureResult>.Available available =>
                ProjectLibraryDependencyStructure(available.Value),
            AssemblyContextEntry<
                LibraryDependencyStructureResult>.Rejected rejected =>
                UnavailableLibraryDependencyStructure(
                    "unavailable",
                    $"{rejected.Subject.Identity.Name}: "
                        + $"{rejected.Failure.Kind} "
                        + $"({rejected.Failure.Detail})"),
            AssemblyContextEntry<
                LibraryDependencyStructureResult>.Failed failed =>
                UnavailableLibraryDependencyStructure(
                    "failed",
                    $"{failed.Subject.Identity.Name}: {failed.Error.Message}"),
            _ => throw new InvalidOperationException(
                "Unknown Library Dependency Structure assembly-context "
                    + "result."),
        };

    static BrowserLibraryDependencyStructure
        ProjectLibraryDependencyStructure(
            LibraryDependencyStructureResult result)
    {
        LibraryDependencyStructureQueryResult selected =
            LibraryDependencyStructureInspection.Select(
                result,
                BrowserDependencyStructureRequest);
        return selected switch
        {
            LibraryDependencyStructureQueryResult.Available available =>
                ProjectLibraryDependencyStructure(available),
            LibraryDependencyStructureQueryResult.Unavailable unavailable =>
                UnavailableLibraryDependencyStructure(
                    "unavailable",
                    unavailable.Outcome.Message),
            LibraryDependencyStructureQueryResult.SelectionFailed failed =>
                UnavailableLibraryDependencyStructure(
                    "failed",
                    failed.Detail),
            LibraryDependencyStructureQueryResult.Failed failed =>
                UnavailableLibraryDependencyStructure(
                    "failed",
                    failed.Error.Message),
            _ => throw new InvalidOperationException(
                "Unknown Library Dependency Structure query result."),
        };
    }

    static BrowserLibraryDependencyStructure
        ProjectLibraryDependencyStructure(
            LibraryDependencyStructureQueryResult.Available available)
    {
        LibraryDependencyStructureDocument document = available.Document;
        IReadOnlyDictionary<string, LibraryDependencyTypeNode> types =
            document.Types.ToDictionary(
                static type => type.TypeKey,
                StringComparer.Ordinal);
        return new(
            "available",
            document.MethodologyVersion,
            document.Completeness.ToString(),
            new(
                document.Population.ExaminedCallCount,
                document.Population.InternalCallCount,
                document.Population.ExternalCallCount,
                document.Population.UnresolvedCallCount,
                document.Population.IncompleteBodyCount,
                document.Population.TypeCount,
                document.Population.NamespaceCount),
            [
                .. document.Namespaces.Select(
                    node => new BrowserLibraryDependencyNamespace(
                        node.Namespace,
                        node.IsGlobalNamespace,
                        node.TypeCount,
                        node.IntraNamespaceRelationshipCount,
                        node.CycleIndex,
                        node.Level)),
            ],
            [
                .. available.Rows.NamespaceEdges.Select(
                    edge => new BrowserLibraryDependencyNamespaceEdge(
                        edge.SourceNamespace,
                        edge.TargetNamespace,
                        ProjectLibraryDependencyCounts(edge.Counts),
                        edge.ContributingTypeEdgeCount,
                        [
                            .. edge.ExplainingTypeEdges.Select(
                                explanation =>
                                    new BrowserLibraryDependencyTypeEdge(
                                        explanation.SourceTypeKey,
                                        types[explanation.SourceTypeKey]
                                            .Type
                                            .ToQualifiedDisplayString(),
                                        explanation.TargetTypeKey,
                                        types[explanation.TargetTypeKey]
                                            .Type
                                            .ToQualifiedDisplayString(),
                                        ProjectLibraryDependencyCounts(
                                            explanation.Counts))),
                        ],
                        edge.RemainingContributorCount)),
            ],
            document.NamespaceEdges.Length,
            [
                .. document.Cycles.Select(
                    cycle => new BrowserLibraryDependencyCycle(
                        [.. cycle.Namespaces])),
            ],
            [
                .. document.Diagnostics.Select(
                    diagnostic => diagnostic.Message),
            ],
            null);
    }

    static BrowserLibraryDependencyCounts
        ProjectLibraryDependencyCounts(
            LibraryDependencyCounts counts) =>
        new(
            counts.Invocations,
            counts.FunctionReferences,
            counts.Total);

    static BrowserLibraryDependencyStructure
        UnavailableLibraryDependencyStructure(
            string outcome,
            string failure) =>
        new(
            outcome,
            null,
            null,
            null,
            [],
            [],
            0,
            [],
            [],
            failure);

    private static QuerySpaceRequest BrowserDependencyStructureRequest =>
        BrowserDependencyStructureRegistration.Request;

    private static class BrowserDependencyStructureRegistration
    {
        internal static QuerySpaceRequest Request { get; } =
            LibraryDependencyStructureQuery.CreateRequest(
                LibraryDependencyStructureQuery.NamespaceEdgesRowSet,
                RowSelectionIntent<string>.Create(
                [
                    RowSelectionIntentOperation<string>.Top(
                        BrowserDependencyStructureEdgeLimit),
                ]),
                QuerySpaceTerminalRequirement.Rows);
    }

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
        (
            BrowserWorkspaceParticipant? _,
            BrowserSurfaceProjection.Surface? surface,
            HashSet<(
                string Assembly,
                string Type,
                string Selector)> navigableMembers) =
                NavigableMembers(scope, participant);

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

    static async Task<BrowserPackageUnsafeFindings>
        PackageUnsafeFindingsAsync(
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
                BrowserCompileLibraryProjection.Project(
                    coordinate.Selection));
        if (!coordinate.Selection.IsSelected)
        {
            return new BrowserPackageUnsafeFindings(
                Findings: [],
                InspectionError: null,
                NonPublicFindings: 0,
                TotalFindings: 0,
                compileLibrary);
        }

        BrowserWorkspaceParticipant participant =
            scope.LibraryParticipant(coordinate, assemblyName);
        if (!scope.ImplementationParticipants.Contains(participant))
        {
            throw new InvalidOperationException(
                "The selected library has no managed implementation "
                    + "assembly, so Unsafe findings cannot open Member "
                    + "Safety Facts.");
        }

        (
            BrowserWorkspaceParticipant? surfaceParticipant,
            BrowserSurfaceProjection.Surface? surface,
            HashSet<(
                string Assembly,
                string Type,
                string Selector)> navigableMembers) =
                NavigableMembers(scope, participant);
        if (surfaceParticipant is null || surface is null)
        {
            throw new InvalidOperationException(
                "The selected implementation has no navigable API surface.");
        }

        AssemblyContextEntry<AssemblyUnsafeFindings> entry =
            scope.UseImplementationParticipant(
                participant,
                AssemblyContextUnsafeFindingsQuery
                    .ExecuteParticipant);
        return ProjectUnsafeFindings(
            entry,
            surfaceParticipant.Asset.AssemblyName,
            compileLibrary,
            navigableMembers,
            surface.InspectionError);
    }

    internal static BrowserPackageUnsafeFindings ProjectUnsafeFindings(
        AssemblyContextEntry<AssemblyUnsafeFindings> entry,
        string assemblyName,
        BrowserCompileLibraryAvailability compileLibrary,
        IReadOnlySet<(
            string Assembly,
            string Type,
            string Selector)>? navigableMembers = null,
        string? surfaceInspectionError = null)
    {
        var failures = new List<string>();
        if (!string.IsNullOrWhiteSpace(surfaceInspectionError))
            failures.Add($"API surface: {surfaceInspectionError}");

        AssemblyUnsafeFindings available = entry switch
        {
            AssemblyContextEntry<
                AssemblyUnsafeFindings>.Rejected rejected =>
                throw new InvalidOperationException(
                    "Unsafe findings inspection was rejected: "
                    + $"{rejected.Subject.Identity.Name}: "
                    + $"{rejected.Failure.Kind} "
                    + $"({rejected.Failure.Detail})"),
            AssemblyContextEntry<
                AssemblyUnsafeFindings>.Failed failed =>
                throw new InvalidOperationException(
                    "Unsafe findings inspection failed: "
                    + $"{failed.Subject.Identity.Name}: "
                    + failed.Error.Message,
                    failed.Error),
            AssemblyContextEntry<
                AssemblyUnsafeFindings>.Available result =>
                result.Value,
            _ => throw new InvalidOperationException(
                $"Unknown unsafe-finding entry "
                    + $"'{entry.GetType().Name}'."),
        };
        failures.AddRange(
            available.Diagnostics.Select(
                diagnostic =>
                    $"{entry.Subject.Identity.Name}: "
                    + $"unsafe analysis incomplete for "
                    + $"{diagnostic.Method}: "
                    + diagnostic.Message));
        failures.AddRange(
            available.ApiSurfaceInspectionFailures
                .Select(
                    failure =>
                        $"{entry.Subject.Identity.Name}: "
                        + $"{failure.Operation}: "
                        + failure.Detail));

        BrowserUnsafeFinding[] findings =
            ApplyUnsafeFindingLimit(
                available.Findings
                    .Where(finding =>
                        finding.PublicMember is not null)
                    .Where(finding =>
                    {
                        UnsafeFindingPublicMember publicMember =
                            finding.PublicMember!;
                        return navigableMembers is null
                            || navigableMembers.Contains((
                                assemblyName,
                                publicMember.TypeDefinitionId,
                                publicMember.StableSelector));
                    })
                    .Select(finding =>
                    {
                        UnsafeFindingPublicMember publicMember =
                            finding.PublicMember!;
                        return new BrowserUnsafeFinding(
                            assemblyName,
                            publicMember.TypeDefinitionId,
                            publicMember.Member,
                            publicMember.StableSelector,
                            publicMember.BodyMember,
                            publicMember.BodySelector,
                            publicMember.BodyToken,
                            finding.Finding.SafetyKind,
                            FormatSafetyLocation(
                                finding.Finding.Location),
                            finding.Finding.ILOffset is int offset
                                ? FormatOffset(offset)
                                : null,
                            finding.Finding.Operation,
                            finding.Finding.Evidence);
                    }),
                failures);
        return new BrowserPackageUnsafeFindings(
            findings,
            failures.Count == 0
                ? null
                : string.Join("; ", failures),
            available.NonPublicFindings,
            available.TotalFindings,
            compileLibrary);
    }

    static (
        BrowserWorkspaceParticipant? SurfaceParticipant,
        BrowserSurfaceProjection.Surface? Surface,
        HashSet<(
            string Assembly,
            string Type,
            string Selector)> Members)
        NavigableMembers(
            BrowserInspectionScope scope,
            BrowserWorkspaceParticipant participant)
    {
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
            string Selector)> members =
        [
            .. (surface?.Types ?? [])
                .SelectMany(type =>
                    type.Api.Select(member => (
                        type.Assembly,
                        type.DefinitionId,
                        member.StableSelector))),
        ];
        return (surfaceParticipant, surface, members);
    }

    static string FormatSafetyLocation(
        ILAnalysis.SafetyFactLocation location) =>
        location switch
        {
            ILAnalysis.SafetyFactLocation.Declaration =>
                "declaration",
            ILAnalysis.SafetyFactLocation.MethodBody =>
                "method body",
            _ => throw new InvalidOperationException(
                $"Unknown safety fact location '{location}'."),
        };

    internal static BrowserUnsafeFinding[] ApplyUnsafeFindingLimit(
        IEnumerable<BrowserUnsafeFinding> candidates,
        ICollection<string> failures)
    {
        const int FindingLimit = 500;
        var findings =
            new List<BrowserUnsafeFinding>(FindingLimit);
        foreach (BrowserUnsafeFinding candidate in candidates)
        {
            if (findings.Count == FindingLimit)
            {
                failures.Add(
                    $"Unsafe findings truncated after the first "
                    + $"{FindingLimit} navigable public findings.");
                break;
            }

            findings.Add(candidate);
        }

        return [.. findings];
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
