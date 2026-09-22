using System.Runtime.Versioning;

using DotnetInspector.Packages;
using DotnetInspector.Queries;
using DotnetInspector.Services;
using DotnetInspector.SourceHouse;

namespace DotnetInspect.Web;

[SupportedOSPlatform("browser")]
internal static class BrowserSourceQueryContext
{
    private const long MiB = 1024L * 1024;

    private static readonly SymbolAcquisitionLimits SourceSymbolLimits =
        new(
            maxSymbolPackageBytes: 24 * MiB,
            maxPortablePdbBytes: 8 * MiB,
            maxSymbolPackageEntries: 2048,
            maxExpandedPdbBytes: 24 * MiB);

    internal static AssemblyContextSourceQueryContext Create()
    {
        var sourceStore = new InMemorySourceContentStore();
        return new AssemblyContextSourceQueryContext(
            BrowserPackageWorkspace.NetworkClient,
            new InMemoryPdbStore(maxRetainedBytes: 24 * MiB),
            BrowserPackageWorkspace.PackageSourceAuthorization,
            new SourceFetch(
                BrowserPackageWorkspace.NetworkClient,
                sourceStore,
                BrowserSourceFetchPolicy.Instance))
        {
            SymbolAcquisitionLimits = SourceSymbolLimits,
        };
    }

    internal static IReadOnlyList<ISourceHouseSourceCapability>
        CreateSourceCapabilities() =>
        AssemblyContextSourceCapabilities.Create(Create());
}
