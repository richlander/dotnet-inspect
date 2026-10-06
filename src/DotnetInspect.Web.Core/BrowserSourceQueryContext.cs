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

    internal static readonly SymbolAcquisitionLimits SourceSymbolLimits =
        new(
            maxSymbolPackageBytes: 24 * MiB,
            maxPortablePdbBytes: 8 * MiB,
            maxSymbolPackageEntries: 2048,
            maxExpandedPdbBytes: 24 * MiB);
    internal static InMemoryPdbStore PositivePdbStore { get; } =
        new(maxRetainedBytes: 24 * MiB);

    internal static AssemblyContextSourceQueryContext Create(
        SourceFetch? sourceFetch = null)
    {
        return new AssemblyContextSourceQueryContext(
            BrowserPackageWorkspace.NetworkClient,
            PositivePdbStore,
            BrowserPackageWorkspace.PackageSourceAuthorization,
            sourceFetch
                ?? new SourceFetch(
                    BrowserPackageWorkspace.NetworkClient,
                    new InMemorySourceContentStore(),
                    BrowserSourceFetchPolicy.Instance))
        {
            SymbolAcquisitionLimits = SourceSymbolLimits,
            PortablePdbSettlementCapability =
                BrowserPortablePdbSettlementCapability.Instance,
        };
    }

    internal static IReadOnlyList<ISourceHouseSourceCapability>
        CreateSourceCapabilities() =>
        AssemblyContextSourceCapabilities.Create(Create());
}
