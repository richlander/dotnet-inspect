using System.Runtime.Versioning;

using DotnetInspector.Ecosystems;

using DotnetInspect.Web;

namespace DotnetInspect.Web.Interop.Catalog;

/// <summary>
/// Product-curated Workspace policy shared by browser capability facades.
/// </summary>
[SupportedOSPlatform("browser")]
public static class BrowserProductWorkspacePlans
{
    /// <summary>
    /// Configures shared browser policy with the product platform Workspace
    /// plan.
    /// </summary>
    public static void ConfigurePlatform() =>
        BrowserPackageWorkspace.ConfigureProductWorkspacePlan(
            EcosystemPackCatalog.CreatePlatformWorkspacePlan());
}
