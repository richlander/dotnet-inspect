using DotnetInspector.Packages;
using NuGetFetch;

namespace DotnetInspector.SourceSelection;

/// <summary>
/// Resolves validated source selections into package-owned acquisition
/// populations.
/// </summary>
public static class PackageAcquisitionPopulationResolver
{
    /// <summary>
    /// Freezes the latest eligible listed version of one exact package ID from
    /// the credential-free NuGet Gallery.
    /// </summary>
    public static Task<PackageAcquisitionPopulation>
        ResolveGalleryExactAsync(
        PackageSourceOperationLease operation,
        string packageId,
        PackageSourceAuthorization authorization,
        bool includePrerelease = false)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId);
        if (!PackageCoordinateResolver.IsCanonicalPackageId(packageId))
        {
            throw new ArgumentException(
                "An exact package population requires a canonical package ID.",
                nameof(packageId));
        }

        return operation.ResolveGalleryExactPopulationAsync(
            packageId,
            includePrerelease,
            authorization);
    }

    /// <summary>
    /// Freezes a bounded literal prefix from the credential-free NuGet Gallery
    /// as ordered authority-bearing candidates.
    /// </summary>
    public static Task<PackageAcquisitionPopulation>
        ResolveGalleryPrefixAsync(
        PackageSourceOperationLease operation,
        PackagePrefixDeclaration prefix,
        int maximumCandidates,
        PackageSourceAuthorization authorization,
        bool includePrerelease = false)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(prefix);
        return operation.ResolveGalleryPrefixPopulationAsync(
            prefix.Prefix,
            maximumCandidates,
            includePrerelease,
            authorization);
    }
}
