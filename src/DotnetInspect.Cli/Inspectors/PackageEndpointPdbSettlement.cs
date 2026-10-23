using DotnetInspect.Cli.Output;
using DotnetInspect.Cli.Services;
using DotnetInspector.Packages;
using DotnetInspector.Services;
using ILInspector.Metadata;
using NuGetFetch;

namespace DotnetInspect.Cli.Inspectors;

/// <summary>
/// The desktop Portable PDB settlement for the participants of a pairwise
/// diff's package endpoint scopes
/// (docs/pdb-acquisition.md#host-neutral-portable-pdb-settlement). A
/// participant without an embedded PDB defers its package-local candidate:
/// settlement asks the House that opened the participant's endpoint for its
/// listed adjacent PDB as one exact File, from the producer that served that
/// endpoint, never a path beside an extracted assembly. Otherwise it
/// continues to the external providers its producer authorizes.
/// </summary>
internal sealed class PackageEndpointPdbSettlement(
    DesktopPackageSourceComposition composition,
    SearchPackageStores stores,
    NuGetSourceOptions? sourceOptions,
    IReadOnlyList<AcquiredPackageSourcePayload> endpoints,
    HttpClient httpClient,
    VerboseLogger logger)
    : IPortablePdbSettlementCapability
{
    public Task<PortablePdbSettlementResult> SettleAsync(
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
                } package
            && endpoints.FirstOrDefault(endpoint =>
                    string.Equals(endpoint.Coordinate.PackageId, package.PackageId, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(endpoint.Coordinate.Version, package.PackageVersion, StringComparison.OrdinalIgnoreCase))
                is { Producer: { } producer } payload)
        {
            packagePreparation =
                PortablePdbPackageComposition.DeferForAssembly(
                    assembly,
                    new EndpointContent(composition, stores, sourceOptions, logger, payload.ProducerKey),
                    producer);
        }

        var request =
            new PortablePdbSettlementRequest(
                target,
                assembly,
                httpClient,
                FileSystemPdbStore.CreateDefault(),
                new SourcePolicyPackageSourceAuthorization(sourceOptions))
            {
                PackagePreparation = packagePreparation,
                NuGetSourceOptions = sourceOptions,
                Timeout = TimeSpan.FromMinutes(5),
                Log = logger.Log,
            };
        return PortablePdbSettlement.SettleAsync(
            request,
            cancellationToken);
    }

    /// <summary>One endpoint's package content, from the producer that served it.</summary>
    sealed class EndpointContent(
        DesktopPackageSourceComposition composition,
        SearchPackageStores stores,
        NuGetSourceOptions? sourceOptions,
        VerboseLogger logger,
        string producerKey)
        : IPortablePdbPackageContentSource
    {
        public Task<PackageHouseSettlement> AcquireAsync(
            PackageSourceCoordinate coordinate,
            PackageHouseContentQuery query,
            CancellationToken cancellationToken = default) =>
            composition.AcquireContentAsync(
                coordinate,
                query,
                stores.GetLegacyCompatibleStore,
                sourceOptions,
                logger.Log,
                cancellationToken,
                requiredProducerKey: producerKey);
    }
}
