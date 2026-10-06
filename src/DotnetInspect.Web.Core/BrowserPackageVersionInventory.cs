using DotnetInspector.Sections;
using NuGet.Versioning;

namespace DotnetInspect.Web;

internal sealed record BrowserPackageVersionInventory(
    string[] Versions,
    int CurrentVersionInsertionIndex,
    string? PreviousVersion,
    string? PreviousVersionUnavailableReason)
{
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
        int currentVersionInsertionIndex = candidates
            .TakeWhile(row => VersionComparer.VersionRelease.Compare(row.Version, current) > 0)
            .Count();
        if (document.Completeness
            != PackageVersionListingCompleteness.Authoritative)
        {
            return new(
                versions,
                currentVersionInsertionIndex,
                null,
                "Automatic selection is unavailable because authoritative listing state "
                + "could not be read. You can still select an exact version.");
        }

        string? previous = candidates
            .Where(row =>
                row.Candidate.Listed
                && (current.IsPrerelease || !row.Version.IsPrerelease)
                && VersionComparer.VersionRelease.Compare(row.Version, current) < 0)
            .Select(row => row.Candidate.Version)
            .FirstOrDefault();
        return new(versions, currentVersionInsertionIndex, previous, null);
    }
}
