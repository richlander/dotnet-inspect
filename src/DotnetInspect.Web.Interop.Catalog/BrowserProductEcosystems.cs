using System.Runtime.Versioning;
using DotnetInspector.Ecosystems;

namespace DotnetInspect.Web.Interop.Catalog;

[SupportedOSPlatform("browser")]
internal static class BrowserProductEcosystems
{
    internal static BrowserEcosystemCatalog ToCatalog(
        IReadOnlyList<EcosystemPackDescriptor> ecosystems) =>
        new(
        [
            .. ecosystems.Select(static ecosystem =>
                new BrowserEcosystemCatalogEntry(
                    ecosystem.Id.Value,
                    ecosystem.Title,
                    ecosystem.Summary,
                    ecosystem.CorePackages.Length,
                    ecosystem.NamespaceRoots.Length,
                    ecosystem.ToolPackages.Length,
                    ecosystem.Demos.Length,
                    ecosystem.PackageSet is not null,
                    ecosystem.HasScanner,
                    ecosystem.HasPopulationLoader,
                    ecosystem.HasWorkspaceRegistration)),
        ]);
}
