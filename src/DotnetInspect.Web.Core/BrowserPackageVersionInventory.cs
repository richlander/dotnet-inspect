using DotnetInspector.Packages;
using DotnetInspector.Sections;
using NuGet.Versioning;

namespace DotnetInspect.Web;

internal sealed record BrowserPackageVersionInventory(
    string[] Versions,
    string[] UnlistedVersions,
    int CurrentVersionInsertionIndex,
    string? PreviousVersion,
    string? PreviousVersionUnavailableReason,
    string[]? SupplyChainVersions,
    string[]? ApiEvolutionVersions)
{
    /// <summary>
    /// The deepest window a compare scenario offers
    /// (inspect-web-diff-targets.md#diff).
    /// </summary>
    public const int MaximumWindowDepth = 6;

    public static BrowserPackageVersionInventory Create(
        PackageVersionListingDocument document,
        string currentVersion)
    {
        ArgumentNullException.ThrowIfNull(document);
        NuGetVersion current = NuGetVersion.Parse(currentVersion);
        var candidates = document.Versions
            .Select(candidate => (
                Candidate: candidate,
                Version: NuGetVersion.Parse(candidate.Version)))
            .OrderByDescending(row => row.Version, VersionComparer.VersionRelease)
            .ThenBy(row => row.Candidate.Version, StringComparer.Ordinal)
            .ToArray();
        string[] versions =
            [.. candidates.Select(row => row.Candidate.Version)];
        string[] unlistedVersions = [.. candidates.Where(row => !row.Candidate.Listed)
            .Select(row => row.Candidate.Version)];
        int currentVersionInsertionIndex = candidates
            .TakeWhile(row => VersionComparer.VersionRelease.Compare(row.Version, current) > 0)
            .Count();
        if (document.Completeness
            != PackageVersionListingCompleteness.Authoritative)
        {
            return new(
                versions,
                unlistedVersions,
                currentVersionInsertionIndex,
                null,
                "Automatic selection is unavailable because authoritative listing state "
                + "could not be read. You can still select an exact version.",
                null,
                null);
        }

        string[] supplyChain = EarlierPoints(PackageVersionWindowPolicy.SupplyChain);
        string[] apiEvolution = EarlierPoints(PackageVersionWindowPolicy.ApiEvolution);
        return new(
            versions,
            unlistedVersions,
            currentVersionInsertionIndex,
            supplyChain.Length == 0 ? null : supplyChain[0],
            null,
            supplyChain,
            apiEvolution);

        // The window's earlier points, nearest first, so a depth-n window is
        // the first n.
        string[] EarlierPoints(PackageVersionWindowPolicy policy) =>
            PackageVersionWindow.TryProject(
                document.Request.PackageId,
                policy,
                MaximumWindowDepth,
                current,
                candidates.Select(row => (row.Version, row.Candidate.Listed)),
                listingIsAuthoritative: true,
                out PackageVersionWindow? window,
                out _)
                ? [.. window!.Points[..^1].Reverse().Select(point => Original(point.Version))]
                : [];

        string Original(NuGetVersion version) => candidates
            .First(row =>
                row.Candidate.Listed
                && VersionComparer.VersionRelease.Equals(row.Version, version))
            .Candidate.Version;
    }
}
