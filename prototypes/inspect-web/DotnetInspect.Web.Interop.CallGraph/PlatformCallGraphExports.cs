using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using System.Text.Json;

using DotnetInspect.Web;
using DotnetInspect.Web.Interop.CallGraph;

namespace DotnetInspect.Web.Interop.CallGraph;

/// <summary>
/// Platform call-graph expansion through the shared browser Platform workspace.
/// </summary>
[SupportedOSPlatform("browser")]
public static partial class CallGraphExports
{
    [JSExport]
    public static async Task<string> ExpandPlatformCallGraph(
        string targetFramework,
        string platformVersion,
        string assembly,
        string pack,
        string assemblyVersion,
        string? assemblyCulture,
        string? assemblyPublicKeyToken,
        string typeFullName,
        string memberName,
        string selectorKey,
        int metadataToken)
    {
        BrowserCallGraphInfo graph =
            await BrowserPlatformCallGraph.QueryAsync(
                targetFramework,
                platformVersion,
                assembly,
                pack,
                assemblyVersion,
                assemblyCulture,
                assemblyPublicKeyToken,
                typeFullName,
                memberName,
                selectorKey,
                metadataToken);
        return JsonSerializer.Serialize(
            BrowserCallGraphWireProjection.Project(graph),
            BrowserCallGraphJsonContext.Default.BrowserCallGraph);
    }

    public static Task<string> ExpandPlatformCallGraph(
        string targetFramework,
        string assembly,
        string pack,
        string assemblyVersion,
        string? assemblyCulture,
        string? assemblyPublicKeyToken,
        string typeFullName,
        string memberName,
        string selectorKey,
        int metadataToken) =>
        ExpandPlatformCallGraph(
            targetFramework,
            "",
            assembly,
            pack,
            assemblyVersion,
            assemblyCulture,
            assemblyPublicKeyToken,
            typeFullName,
            memberName,
            selectorKey,
            metadataToken);
}
