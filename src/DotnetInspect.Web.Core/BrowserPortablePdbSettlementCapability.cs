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

        PortablePdbPackageCandidate? packageCandidate = null;
        PortablePdbPackageBindingFailureKind?
            packageBindingFailure = null;
        PackageProducerIdentity? packageProducer = null;
        if (!target.HasEmbeddedPdb
            && assembly.Provenance
                is AssemblyResolutionProvenance.PackageAsset
                {
                    Tfm: not null,
                    AssetPath: not null,
                })
        {
            PortablePdbPackageBindingResult binding =
                await PortablePdbPackageComposition
                    .PrepareForAssemblyAsync(
                        assembly,
                        BrowserPortablePdbPackageContentSource.Instance,
                        cancellationToken)
                    .ConfigureAwait(false);
            if (binding
                is PortablePdbPackageBindingResult.Bound bound)
            {
                packageCandidate = bound.Value.Candidate;
                packageProducer = packageCandidate.Producer;
            }
            else if (binding
                is PortablePdbPackageBindingResult.Terminal terminal)
            {
                packageBindingFailure = terminal.Failure;
            }
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
                PackageCandidate = packageCandidate,
                PackageBindingFailure = packageBindingFailure,
                PackageProducer = packageProducer,
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
