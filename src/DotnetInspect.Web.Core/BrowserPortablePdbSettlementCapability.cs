using System.Runtime.Versioning;

using DotnetInspector.Packages;
using DotnetInspector.Services;
using ILInspector.Metadata;
using NuGetFetch;

namespace DotnetInspect.Web;

[SupportedOSPlatform("browser")]
internal sealed class BrowserPortablePdbSettlementCapability :
    IPortablePdbSettlementCapability
{
    internal static BrowserPortablePdbSettlementCapability Instance
    {
        get;
    } = new();

    private BrowserPortablePdbSettlementCapability()
    {
    }

    public async Task<PortablePdbSettlementResult> SettleAsync(
        PortablePdbSettlementTarget target,
        ResolvedAssemblyReference assembly,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(assembly);

        PortablePdbPackagePreparation? packagePreparation = null;
        if (!target.HasEmbeddedPdb
            && assembly.Provenance
                is AssemblyResolutionProvenance.PackageAsset
                {
                    Tfm: not null,
                    AssetPath: not null,
                })
        {
            packagePreparation =
                PortablePdbPackageComposition.DeferForAssembly(
                    assembly,
                    BrowserPortablePdbPackageContentSource.Instance,
                    BrowserPackageWorkspace.PackageProducer);
        }

        var request =
            new PortablePdbSettlementRequest(
                target,
                assembly,
                BrowserPackageWorkspace.NetworkClient,
                BrowserSourceQueryContext.PositivePdbStore,
                BrowserPackageWorkspace.PackageSourceAuthorization)
            {
                Limits = BrowserSourceQueryContext.SourceSymbolLimits,
                PackagePreparation = packagePreparation,
                Timeout = BrowserPackageWorkspace.PackageOperationTimeout,
            };
        return await PortablePdbSettlement.SettleAsync(
                request,
                cancellationToken)
            .ConfigureAwait(false);
    }

    private sealed class BrowserPortablePdbPackageContentSource :
        IPortablePdbPackageContentSource
    {
        internal static BrowserPortablePdbPackageContentSource Instance
        {
            get;
        } = new();

        public Task<PackageHouseSettlement> AcquireAsync(
            PackageSourceCoordinate coordinate,
            PackageHouseContentQuery query,
            CancellationToken cancellationToken = default) =>
            BrowserPackageWorkspace.AcquireContentAsync(
                coordinate,
                query,
                cancellationToken);
    }
}
