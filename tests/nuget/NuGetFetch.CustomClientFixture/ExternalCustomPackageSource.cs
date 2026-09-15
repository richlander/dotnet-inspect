using System.Text;
using NuGetFetch;

namespace NuGetFetch.CustomClientFixture;

public static class ExternalCustomPackageSource
{
    public static IPackageSourceClient Create(
        PackageSourceDescriptor descriptor,
        PackageSourceAssociation association) =>
        PackageSourceClientFactory.CreateCustom(
            descriptor,
            association,
            static factory => new Client(factory));

    private sealed class Client(
        PackageSourceResultFactory results)
        : IPackageSourceClient
    {
        public PackageSourceResultIdentity Source => results.Source;

        public PackageSourceCapabilities Capabilities =>
            PackageSourceCapabilities.Search
            | PackageSourceCapabilities.VersionEnumeration
            | PackageSourceCapabilities.Manifest
            | PackageSourceCapabilities.PackagePayload
            | PackageSourceCapabilities.SymbolPayload;

        public Task<PackageSourceOperationResult<PackageSearchResult>>
            SearchAsync(
                string query,
                int take = 20,
                bool prerelease = false,
                CancellationToken cancellationToken = default,
                NuGetOperationContext? operationContext = null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            PackageSearchResult value = results.Search(
                [new SearchResult("External.Package", "1.0.0")]);
            return Task.FromResult(results.SucceededSearch(value));
        }

        public Task<PackageSourceOperationResult<PackageSearchResult>>
            SearchByPrefixAsync(
                string prefix,
                int take = 100,
                bool prerelease = false,
                CancellationToken cancellationToken = default,
                NuGetOperationContext? operationContext = null) =>
            SearchAsync(
                prefix,
                take,
                prerelease,
                cancellationToken,
                operationContext);

        public Task<PackageSourceOperationResult<PackageVersionResult>>
            GetVersionsAsync(
                string packageId,
                CancellationToken cancellationToken = default,
                NuGetOperationContext? operationContext = null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            PackageCandidateObservation candidate = results.Candidate(
                PackageSourceCoordinate.Create(packageId, "1.0.0"),
                PackageDiscoveryContract.CompleteVersionEnumeration,
                PackageListingState.Listed);
            PackageVersionResult value = results.Versions(
                [candidate],
                hasAuthoritativeListingState: true);
            return Task.FromResult(results.SucceededVersions(value));
        }

        public Task<PackageSourceOperationResult<PackageSourceManifest>>
            GetManifestAsync(
                string packageId,
                string version,
                CancellationToken cancellationToken = default,
                NuGetOperationContext? operationContext = null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            PackageSourceCoordinate coordinate =
                PackageSourceCoordinate.Create(packageId, version);
            PackageSourceManifest value = results.Manifest(
                coordinate,
                Encoding.UTF8.GetBytes("<package />"));
            return Task.FromResult(
                results.SucceededManifest(coordinate, value));
        }

        public Task<PackageSourceOperationResult<PackageSourcePayload>>
            GetPackageAsync(
                string packageId,
                string version,
                CancellationToken cancellationToken = default,
                NuGetOperationContext? operationContext = null) =>
            Payload(
                packageId,
                version,
                PackageSourcePayloadKind.Package,
                cancellationToken);

        public Task<PackageSourceOperationResult<PackageSourcePayload>>
            TryGetSymbolsAsync(
                string packageId,
                string version,
                CancellationToken cancellationToken = default,
                NuGetOperationContext? operationContext = null) =>
            Payload(
                packageId,
                version,
                PackageSourcePayloadKind.Symbols,
                cancellationToken);

        public void Dispose()
        {
        }

        private Task<PackageSourceOperationResult<PackageSourcePayload>>
            Payload(
                string packageId,
                string version,
                PackageSourcePayloadKind kind,
                CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            PackageSourceCoordinate coordinate =
                PackageSourceCoordinate.Create(packageId, version);
            PackageSourcePayload value = results.Payload(
                coordinate,
                kind,
                new MemoryStream([1, 2, 3], writable: false),
                advertisedLength: 3);
            return Task.FromResult(
                kind == PackageSourcePayloadKind.Package
                    ? results.SucceededPackage(coordinate, value)
                    : results.SucceededSymbols(coordinate, value));
        }
    }
}
