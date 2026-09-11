using NuGetFetch;

namespace DotnetInspector.Packages;

public sealed partial class DesktopPackageSourceComposition
{
    /// <summary>
    /// Acquires the exact manifest for one candidate-authorized coordinate from its
    /// admitted authorities, consulted in the same stable order payload acquisition
    /// uses. The candidate must have been issued by this composition; this is the
    /// package-owned exact manifest capability the package dependency traversal query
    /// composes for its recursive expansion, and it never downloads a package archive.
    /// </summary>
    public Task<ConfiguredPackageManifestResult> AcquireCandidateManifestAsync(
        PackageAcquisitionCandidate candidate,
        CancellationToken cancellationToken = default,
        NuGetOperationContext? operationContext = null) =>
        PackageSourceSettlementCompatibility.RunAsync(
            _sourceAuthorization, cancellationToken, operationContext,
            (generation, operation) => generation.AcquireCandidateManifestAsync(
                candidate, operationContext: operation),
            _options.RequestTimeout, _options.OperationTimeout);
}
