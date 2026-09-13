using DotnetInspector.Packages;

namespace DotnetInspector.SourceSelection;

/// <summary>
/// Resolves validated source selections into package-owned acquisition
/// populations.
/// </summary>
public static class PackageAcquisitionPopulationResolver
{
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
