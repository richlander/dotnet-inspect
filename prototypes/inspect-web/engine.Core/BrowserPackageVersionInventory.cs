using NuGet.Versioning;
using NuGetFetch;

namespace InspectWeb.Engine;

internal sealed record BrowserPackageVersionInventory(
    string[] Versions,
    string? PreviousVersion,
    string? PreviousVersionUnavailableReason)
{
    public static BrowserPackageVersionInventory Create(
        PackageVersionResult result,
        string currentVersion)
    {
        NuGetVersion current = NuGetVersion.Parse(currentVersion);
        var candidates = result.Candidates
            .Select(candidate => (
                Candidate: candidate,
                Version: NuGetVersion.Parse(candidate.Coordinate.Version)))
            .OrderByDescending(row => row.Version, VersionComparer.VersionRelease)
            .ThenBy(row => row.Candidate.Coordinate.Version, StringComparer.Ordinal)
            .ToArray();
        string[] versions =
            [.. candidates.Select(row => row.Candidate.Coordinate.Version)];
        if (!result.HasAuthoritativeListingState)
        {
            return new(
                versions,
                null,
                "Automatic selection is unavailable because authoritative listing state "
                + "could not be read. You can still select an exact version.");
        }

        string? previous = candidates
            .Where(row =>
                row.Candidate.ListingState == PackageListingState.Listed
                && (current.IsPrerelease || !row.Version.IsPrerelease)
                && VersionComparer.VersionRelease.Compare(row.Version, current) < 0)
            .Select(row => row.Candidate.Coordinate.Version)
            .FirstOrDefault();
        return new(versions, previous, null);
    }
}
