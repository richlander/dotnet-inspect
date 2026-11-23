using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using System.Text.Json;
using DotnetInspector.Queries;
using DotnetInspector.Sections;
using ILInspector.Research;

using DotnetInspect.Web;
using DotnetInspect.Web.Interop.Analysis;

namespace DotnetInspect.Web.Interop.Analysis;

/// <summary>
/// Platform Analysis results for one selected participant in the cumulative platform group.
/// </summary>
/// <remarks>
/// Inspecting a participant requires a session or its image snapshot, and
/// <c>AssemblyContextGroup</c>'s access to both is internal to <c>DotnetInspector.Queries</c> and
/// its companion query assembly. Each export therefore inspects only through a public
/// participant-scoped query that owns those lifetimes, never by opening a session, a metadata
/// source, an analysis index, or a retained descriptor itself.
/// </remarks>
[SupportedOSPlatform("browser")]
public static partial class AnalysisExports
{
    [JSExport]
    public static async Task<string> QueryPlatformIntegrations(
        string targetFramework,
        string platformVersion,
        string assemblyFileName,
        string pack)
    {
        BrowserPackageIntegrations integrations;
        await using (BrowserPlatformScopeResolution resolution =
            await BrowserPlatformWorkspace.OpenAssemblyAsync(
                targetFramework,
                platformVersion,
                assemblyFileName,
                pack))
        {
            AssemblyIntegrationsEntry result =
                resolution.Scope.UseParticipant(
                    resolution.Participant,
                    AssemblyContextIntegrationsQuery.ExecuteParticipant);
            integrations = CreateIntegrations(
                BrowserPlatformIdentity.PackageName,
                resolution.Coordinate.Version,
                resolution.Scope.Framework,
                [result]);
        }

        return JsonSerializer.Serialize(
            integrations,
            BrowserAnalysisJsonContext.Default.BrowserPackageIntegrations);
    }

    public static Task<string> QueryPlatformIntegrations(
        string targetFramework,
        string assemblyFileName,
        string pack) =>
        QueryPlatformIntegrations(
            targetFramework,
            "",
            assemblyFileName,
            pack);

    [JSExport]
    public static async Task<string> QueryPlatformOpportunities(
        string targetFramework,
        string platformVersion,
        string assemblyFileName,
        string pack)
    {
        BrowserPackageOpportunities opportunities;
        await using (BrowserPlatformScopeResolution resolution =
            await BrowserPlatformWorkspace.OpenAssemblyAsync(
                targetFramework,
                platformVersion,
                assemblyFileName,
                pack))
        {
            AssemblyIntegrationOpportunitiesEntry result =
                resolution.Scope.UseParticipant(
                    resolution.Participant,
                    AssemblyContextIntegrationOpportunitiesQuery
                        .ExecuteParticipant);
            opportunities = CreateOpportunities(
                BrowserPlatformIdentity.PackageName,
                resolution.Coordinate.Version,
                resolution.Scope.Framework,
                [result]);
        }

        return JsonSerializer.Serialize(
            opportunities,
            BrowserAnalysisJsonContext.Default.BrowserPackageOpportunities);
    }

    public static Task<string> QueryPlatformOpportunities(
        string targetFramework,
        string assemblyFileName,
        string pack) =>
        QueryPlatformOpportunities(
            targetFramework,
            "",
            assemblyFileName,
            pack);

    [JSExport]
    public static async Task<string> QueryPlatformLibraryMetrics(
        string targetFramework,
        string platformVersion,
        string assemblyFileName,
        string pack)
    {
        BrowserLibraryMetrics metrics;
        await using (BrowserPlatformScopeResolution resolution =
            await BrowserPlatformWorkspace.OpenAssemblyAsync(
                targetFramework,
                platformVersion,
                assemblyFileName,
                pack))
        {
            AssemblyContextEntry<LibraryMetricsResult> result =
                resolution.Scope.UseParticipant(
                    resolution.Participant,
                    static (group, participant) =>
                        AssemblyContextLibraryMetricsQuery
                            .ExecuteParticipant(group, participant));
            metrics = AnalysisExports.ProjectLibraryMetrics(
                result,
                new BrowserCompileLibraryAvailability(
                    BrowserCompileLibraryStatus.Selected,
                    resolution.Scope.Framework,
                    null));
        }

        return JsonSerializer.Serialize(
            metrics,
            BrowserAnalysisJsonContext.Default.BrowserLibraryMetrics);
    }

    public static Task<string> QueryPlatformLibraryMetrics(
        string targetFramework,
        string assemblyFileName,
        string pack) =>
        QueryPlatformLibraryMetrics(
            targetFramework,
            "",
            assemblyFileName,
            pack);

    [JSExport]
    public static async Task<string> QueryPlatformLibraryDependencyStructure(
        string targetFramework,
        string platformVersion,
        string assemblyFileName,
        string pack)
    {
        BrowserLibraryDependencyStructure structure;
        await using (BrowserPlatformScopeResolution resolution =
            await BrowserPlatformWorkspace.OpenAssemblyAsync(
                targetFramework,
                platformVersion,
                assemblyFileName,
                pack))
        {
            AssemblyContextEntry<LibraryDependencyStructureResult> result =
                resolution.Scope.UseParticipant(
                    resolution.Participant,
                    static (group, participant) =>
                        AssemblyContextLibraryDependencyStructureQuery
                            .ExecuteParticipant(group, participant));
            structure = AnalysisExports.ProjectLibraryDependencyStructure(
                result);
        }

        return JsonSerializer.Serialize(
            structure,
            BrowserAnalysisJsonContext.Default
                .BrowserLibraryDependencyStructure);
    }

    public static Task<string> QueryPlatformLibraryDependencyStructure(
        string targetFramework,
        string assemblyFileName,
        string pack) =>
        QueryPlatformLibraryDependencyStructure(
            targetFramework,
            "",
            assemblyFileName,
            pack);

    [JSExport]
    public static async Task<string> QueryPlatformLibraryStructuralSalience(
        string targetFramework,
        string platformVersion,
        string assemblyFileName,
        string pack)
    {
        BrowserLibraryStructuralSalience salience;
        await using (BrowserPlatformScopeResolution resolution =
            await BrowserPlatformWorkspace.OpenAssemblyAsync(
                targetFramework,
                platformVersion,
                assemblyFileName,
                pack))
        {
            AssemblyContextEntry<LibraryTypeLeverageResult> entry =
                resolution.Scope.UseParticipant(
                    resolution.Participant,
                    static (group, selectedParticipant) =>
                        AssemblyContextLibraryTypeLeverageQuery
                            .ExecuteParticipant(
                                group,
                                selectedParticipant));
            salience = AnalysisExports.ProjectLibraryStructuralSalience(
                entry,
                new BrowserCompileLibraryAvailability(
                    BrowserCompileLibraryStatus.Selected,
                    resolution.Scope.Framework,
                    null));
        }

        return JsonSerializer.Serialize(
            salience,
            BrowserAnalysisJsonContext.Default
                .BrowserLibraryStructuralSalience);
    }

    public static Task<string> QueryPlatformLibraryStructuralSalience(
        string targetFramework,
        string assemblyFileName,
        string pack) =>
        QueryPlatformLibraryStructuralSalience(
            targetFramework,
            "",
            assemblyFileName,
            pack);

    [JSExport]
    public static async Task<string> QueryPlatformImplementationProfiles(
        string targetFramework,
        string platformVersion,
        string assemblyFileName,
        string pack,
        string typeDefinitionId,
        string[] stableSelectors)
    {
        var selection = new ImplementationProfileFamilySelection(
            typeDefinitionId,
            stableSelectors);
        BrowserImplementationProfiles profiles;
        await using (BrowserPlatformScopeResolution resolution =
            await BrowserPlatformWorkspace.OpenAssemblyAsync(
                targetFramework,
                platformVersion,
                assemblyFileName,
                pack))
        {
            InspectionEnvelope<
                AssemblyContextEntry<
                    AssemblyImplementationProfileFamilyInspection>>
                    inspection =
                        resolution.Scope.UseParticipant(
                            resolution.Participant,
                            (group, participant) =>
                                ImplementationProfileFamilyInspectionOperation
                                    .Execute(
                                        group,
                                        participant,
                                        selection));
            profiles = BrowserImplementationProfileWireProjection.Project(
                inspection,
                new BrowserCompileLibraryAvailability(
                    BrowserCompileLibraryStatus.Selected,
                    resolution.Scope.Framework,
                    null));
        }

        return JsonSerializer.Serialize(
            profiles,
            BrowserAnalysisJsonContext.Default
                .BrowserImplementationProfiles);
    }

    public static Task<string> QueryPlatformImplementationProfiles(
        string targetFramework,
        string assemblyFileName,
        string pack,
        string typeDefinitionId,
        string[] stableSelectors) =>
        QueryPlatformImplementationProfiles(
            targetFramework,
            "",
            assemblyFileName,
            pack,
            typeDefinitionId,
            stableSelectors);

    [JSExport]
    public static async Task<string> QueryPlatformTypeImplementationHeat(
        string targetFramework,
        string platformVersion,
        string assemblyFileName,
        string pack,
        string typeDefinitionId)
    {
        BrowserTypeImplementationHeat heat;
        await using (BrowserPlatformScopeResolution resolution =
            await BrowserPlatformWorkspace.OpenAssemblyAsync(
                targetFramework,
                platformVersion,
                assemblyFileName,
                pack))
        {
            InspectionEnvelope<
                AssemblyContextEntry<
                    AssemblyTypeImplementationHeatInspection>>
                    inspection =
                        resolution.Scope.UseParticipant(
                            resolution.Participant,
                            (group, participant) =>
                                TypeImplementationHeatInspectionOperation
                                    .Execute(
                                        group,
                                        participant,
                                        typeDefinitionId));
            heat = BrowserImplementationProfileWireProjection.ProjectTypeHeat(
                inspection,
                new BrowserCompileLibraryAvailability(
                    BrowserCompileLibraryStatus.Selected,
                    resolution.Scope.Framework,
                    null));
        }

        return JsonSerializer.Serialize(
            heat,
            BrowserAnalysisJsonContext.Default
                .BrowserTypeImplementationHeat);
    }

    [JSExport]
    public static async Task<string> QueryPlatformTypeMethodLeverage(
        string targetFramework,
        string platformVersion,
        string assemblyFileName,
        string pack,
        string typeDefinitionId)
    {
        BrowserTypeMethodLeverage leverage;
        await using (BrowserPlatformScopeResolution resolution =
            await BrowserPlatformWorkspace.OpenAssemblyAsync(
                targetFramework,
                platformVersion,
                assemblyFileName,
                pack))
        {
            InspectionEnvelope<
                AssemblyContextEntry<
                    AssemblyTypeMethodLeverageInspection>>
                    inspection =
                        resolution.Scope.UseParticipant(
                            resolution.Participant,
                            (group, participant) =>
                                TypeMethodLeverageInspectionOperation.Execute(
                                    group,
                                    participant,
                                    typeDefinitionId));
            leverage = BrowserImplementationProfileWireProjection
                .ProjectTypeMethodLeverage(
                    inspection,
                    new BrowserCompileLibraryAvailability(
                        BrowserCompileLibraryStatus.Selected,
                        resolution.Scope.Framework,
                        null));
        }

        return JsonSerializer.Serialize(
            leverage,
            BrowserAnalysisJsonContext.Default
                .BrowserTypeMethodLeverage);
    }

    /// <summary>
    /// Product-ranked optimization-opportunity members for one selected
    /// platform participant, over the same participant-scoped query and
    /// Browser projection as <see cref="QueryPackagePerformance"/>. The
    /// ranking reads the implementation (the runtime-pack assembly); rows are
    /// named by the Library's surface participant, as the Platform Type list
    /// names its Types, so a listed Type opens and any other opens as a
    /// transient Type (<c>docs/design/inspect-web-platform-scope-roles.md</c>).
    /// </summary>
    [JSExport]
    public static async Task<string> QueryPlatformPerformance(
        string targetFramework,
        string platformVersion,
        string assemblyFileName,
        string pack)
    {
        BrowserPackagePerformance performance;
        await using (BrowserPlatformScopeResolution resolution =
            await BrowserPlatformWorkspace.OpenAssemblyAsync(
                targetFramework,
                platformVersion,
                assemblyFileName,
                pack))
        {
            string assembly =
                resolution.SurfaceParticipant.Participant.Assembly.Identity.Name;
            AssemblyContextOptimizationOpportunitiesResult result =
                resolution.Scope.UseParticipant(
                    resolution.Participant,
                    AssemblyContextOptimizationOpportunitiesQuery
                        .ExecuteParticipant);
            AssemblyContextEntry<SyncCallsInAsyncResult> syncCallsResult =
                resolution.Scope.UseParticipant(
                    resolution.Participant,
                    SyncCallsInAsyncParticipant);
            performance = ProjectPerformance(
                result,
                syncCallsResult,
                failures: [],
                new BrowserPerformanceSurfaceNaming(
                    assembly,
                    assembly,
                    assembly,
                    BrowserPlatformWorkspace.Pack(
                        resolution.Coordinate.Family)),
                () => resolution.Scope.UseParticipant(
                    resolution.Participant,
                    ImplementationApiSurface),
                BrowserAnalysisWireProjection.Project(
                    BrowserCompileLibraryProjection.Selected(
                        resolution.Scope.Framework)));
        }

        return JsonSerializer.Serialize(
            performance,
            BrowserAnalysisJsonContext.Default.BrowserPackagePerformance);
    }
}
