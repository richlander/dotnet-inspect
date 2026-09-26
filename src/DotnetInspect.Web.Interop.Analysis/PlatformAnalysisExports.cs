using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using System.Text.Json;
using DotnetInspector.Queries;
using DotnetInspector.Sections;

using DotnetInspect.Web;
using DotnetInspect.Web.Interop.Analysis;

namespace DotnetInspect.Web.Interop.Analysis;

/// <summary>
/// Platform Analysis results. Platform performance is an explicitly unavailable capability rather
/// than a missing binding: the operation stays exported and rejects with its existing error.
/// </summary>
/// <remarks>
/// <para>
/// These are product API gaps, not host shortcuts. Inspecting a participant requires a session or
/// its image snapshot, and <c>AssemblyContextGroup</c>'s access to both is internal to
/// <c>DotnetInspector.Queries</c> and its companion query assembly. A consumer therefore inspects
/// only through a public query that owns those lifetimes, and the unsupported operation below
/// waits for its own query rather than opening a session, a metadata source, an analysis index, or
/// a retained descriptor.
/// </para>
/// <para>
/// The exact queries required are listed in <c>inspect-web/README.md</c> under
/// "Required workspace queries", and each has a tracking issue named there.
/// </para>
/// </remarks>
[SupportedOSPlatform("browser")]
public static partial class AnalysisExports
{
    const string NoPlatformProjection =
        "no group-scoped product query projects this evidence from a platform participant";

    static NotSupportedException Unavailable(string operation, string capability) =>
        new($"{operation} is not available in this engine build: {capability}");

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
            LibraryMetricsResult result =
                resolution.Scope.UseParticipant(
                    resolution.Participant,
                    static (group, participant) =>
                        AssemblyContextLibraryMetricsQuery
                            .ExecuteParticipant(group, participant))
                    switch
                    {
                        AssemblyContextEntry<LibraryMetricsResult>.Available
                            available => available.Value,
                        AssemblyContextEntry<LibraryMetricsResult>.Rejected rejected =>
                            new LibraryMetricsResult.Failed(
                                new InvalidOperationException(
                                    $"{rejected.Subject.Identity.Name}: "
                                        + $"{rejected.Failure.Kind} "
                                        + $"({rejected.Failure.Detail})")),
                        AssemblyContextEntry<LibraryMetricsResult>.Failed failed =>
                            new LibraryMetricsResult.Failed(failed.Error),
                        _ => throw new InvalidOperationException(
                            "Unknown platform Library Metrics result."),
                    };
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
    public static Task<string> QueryPlatformPerformance(
        string targetFramework,
        string platformVersion,
        string assemblyFileName,
        string pack) =>
        throw Unavailable("Platform performance", NoPlatformProjection);
}
