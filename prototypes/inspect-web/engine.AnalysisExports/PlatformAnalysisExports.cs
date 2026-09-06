using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using System.Text.Json;
using DotnetInspector.Queries;

using InspectWeb.Engine;
using InspectWeb.Engine.AnalysisFacade;

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
/// The exact queries required are listed in <c>prototypes/inspect-web/README.md</c> under
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
    public static Task<string> QueryPlatformPerformance(
        string targetFramework,
        string platformVersion,
        string assemblyFileName,
        string pack) =>
        throw Unavailable("Platform performance", NoPlatformProjection);
}
