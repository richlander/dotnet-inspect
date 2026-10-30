namespace NuGetFetch;

/// <summary>Acquires listing facts without completing unrelated version histories.</summary>
public interface IPackageVersionListingSource
{
    Task<PackageSourceOperationResult<PackageVersionListingSnapshot>>
        GetVersionListingSnapshotAsync(
            string packageId,
            CancellationToken cancellationToken = default,
            NuGetOperationContext? operationContext = null);

    Task<PackageSourceOperationResult<PackageVersionResult>>
        GetVersionListingsAsync(
            PackageVersionListingSnapshot snapshot,
            IReadOnlyList<string> versions,
            CancellationToken cancellationToken = default,
            NuGetOperationContext? operationContext = null);
}

/// <summary>
/// Complete version identities and observed listing facts, with source-owned
/// metadata for acquiring further facts. This does not authorize acquisition.
/// </summary>
public sealed class PackageVersionListingSnapshot
{
    internal PackageVersionListingSnapshot(
        string packageId,
        PackageVersionResult versionIdentities,
        IReadOnlySet<string> candidateVersions,
        NuGetGalleryRegistrationIndex? registrationIndex,
        NuGetGalleryRegistrationBudget budget)
    {
        PackageId = packageId;
        VersionIdentities = versionIdentities;
        CandidateVersions = candidateVersions;
        RegistrationIndex = registrationIndex;
        Budget = budget;
    }

    public string PackageId { get; }
    public PackageSourceResultIdentity Source => VersionIdentities.Source;
    public PackageVersionResult VersionIdentities { get; }

    internal IReadOnlySet<string> CandidateVersions { get; }
    internal NuGetGalleryRegistrationIndex? RegistrationIndex { get; }
    internal NuGetGalleryRegistrationBudget Budget { get; }
}
