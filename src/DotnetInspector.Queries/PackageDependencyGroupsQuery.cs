using System.Collections.Immutable;
using DotnetInspector.Packages;
using DotnetInspector.Services;
using NuGet.Versioning;

namespace DotnetInspector.Queries;

/// <summary>The exact-target-framework selection outcome for declared dependency groups.</summary>
public enum PackageDependencyGroupSelectionStatus
{
    Selected,
    NoDependencyGroups,
    NoMatchingTargetFramework,
}

/// <summary>One dependency exactly as declared in a package manifest.</summary>
public sealed record DeclaredPackageDependency(
    string Id,
    string VersionRange);

/// <summary>One target-framework dependency group exactly as declared in a package manifest.</summary>
public sealed record DeclaredPackageDependencyGroup(
    string TargetFramework,
    ImmutableArray<DeclaredPackageDependency> Dependencies);

/// <summary>A package manifest's dependency groups and exact-framework selection outcome.</summary>
public sealed record PackageDependencyGroups(
    ImmutableArray<DeclaredPackageDependencyGroup> Groups,
    string? RequestedTargetFramework,
    string? SelectedTargetFramework,
    int? SelectedGroupIndex,
    PackageDependencyGroupSelectionStatus SelectionStatus);

/// <summary>NuGet-owned matching and selection for declared dependency version ranges.</summary>
public static class PackageDependencyVersionRange
{
    public static bool Satisfies(
        string packageVersion,
        string? declaredRange)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageVersion);

        if (!NuGetVersion.TryParse(packageVersion, out NuGetVersion? version))
            throw new ArgumentException("The package version is invalid.", nameof(packageVersion));

        VersionRange range = Parse(declaredRange);
        return Matches(range, version);
    }

    public static string? SelectBestSatisfying(
        IEnumerable<string> availableVersions,
        string? declaredRange)
    {
        ArgumentNullException.ThrowIfNull(availableVersions);

        VersionRange range = Parse(declaredRange);
        var candidates = new List<(NuGetVersion Version, string Text)>();
        foreach (string versionText in availableVersions)
        {
            if (!NuGetVersion.TryParse(versionText, out NuGetVersion? version))
            {
                throw new InvalidDataException(
                    "The package version index contains an invalid version.");
            }

            if (Matches(range, version))
                candidates.Add((version, versionText));
        }

        NuGetVersion? best = range.FindBestMatch(
            candidates.Select(candidate => candidate.Version));
        return best is null
            ? null
            : candidates.First(candidate => candidate.Version == best).Text;
    }

    internal static void Validate(string? declaredRange) =>
        _ = Parse(declaredRange);

    static bool Matches(VersionRange range, NuGetVersion version)
    {
        if (!range.Satisfies(version)
            || (range.IsFloating && !range.Float.Satisfies(version)))
        {
            return false;
        }

        return range.FindBestMatch([version]) == version;
    }

    static VersionRange Parse(string? declaredRange)
    {
        if (string.IsNullOrWhiteSpace(declaredRange))
            return VersionRange.All;

        if (!VersionRange.TryParse(declaredRange, out VersionRange? range))
            throw new InvalidDataException(
                "The declared dependency version range is invalid.");

        return range;
    }
}

/// <summary>The typed outcome of projecting declared dependency groups from package content.</summary>
public abstract record PackageDependencyGroupsResult
{
    private PackageDependencyGroupsResult()
    {
    }

    public sealed record Available(
        PackageDependencyGroups Value) : PackageDependencyGroupsResult;

    public sealed record NoManifest : PackageDependencyGroupsResult;

    public sealed record Failed(
        Exception Error) : PackageDependencyGroupsResult;
}

/// <summary>
/// Projects declared NuGet dependency groups from host-neutral package content.
/// </summary>
/// <remarks>
/// The query requires exactly one root manifest, validates its declared package identity against
/// the requested coordinate, and retains every dependency group in manifest order. The byte and
/// decoded-character limits are gated by
/// <c>ManifestBounds_AreEnforcedForEveryPackageStore</c> and
/// <c>ExecuteAsync_EnforcesDecodedCharacterLimit</c>;
/// DTD rejection is gated by <c>ExecuteAsync_RejectsDtdWithoutQuotingArtifactText</c>.
/// </remarks>
public static class PackageDependencyGroupsQuery
{
    internal const int MaxManifestBytes = 1024 * 1024;
    internal const int MaxManifestCharacters = 512 * 1024;

    public static InspectionQuery<PackageDependencyGroupsResult> Definition { get; } =
        new("Package dependency groups", InspectionCost.NetworkFree);

    public static async Task<PackageDependencyGroupsResult> ExecuteAsync(
        IPackageContent content,
        string packageId,
        string packageVersion,
        string? requestedTargetFramework = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId);
        ArgumentException.ThrowIfNullOrWhiteSpace(packageVersion);

        try
        {
            string? manifestPath = FindRootManifest(content);
            if (manifestPath is null)
                return new PackageDependencyGroupsResult.NoManifest();

            if (!content.TryOpenEntry(
                    manifestPath,
                    MaxManifestBytes,
                    out Stream? manifestStream))
            {
                return new PackageDependencyGroupsResult.Failed(
                    new InvalidDataException(
                        "The selected package manifest is no longer available."));
            }

            byte[] manifestBytes;
            using (manifestStream)
            {
                manifestBytes = await BoundedContentReader.ReadAllBytesAsync(
                        manifestStream,
                        MaxManifestBytes,
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
            }

            NuspecData nuspec;
            using (var buffer = new MemoryStream(manifestBytes, writable: false))
            {
                nuspec = NuspecParser.Parse(
                    buffer,
                    MaxManifestCharacters);
            }

            if (string.IsNullOrWhiteSpace(nuspec.PackageName)
                || !nuspec.PackageName.Equals(
                    packageId,
                    StringComparison.OrdinalIgnoreCase)
                || !VersionsEqual(nuspec.Version, packageVersion))
            {
                throw new InvalidDataException(
                    "The package manifest identity does not match the requested package.");
            }

            string? requested = string.IsNullOrWhiteSpace(requestedTargetFramework)
                ? null
                : requestedTargetFramework;
            List<DependencyGroup>? mutableGroups = nuspec.DependencyGroups;
            foreach (DependencyGroup group in mutableGroups ?? [])
            {
                foreach (PackageDependency dependency in group.Dependencies)
                    PackageDependencyVersionRange.Validate(dependency.Version);
            }

            DependencyResolutionService.DependencyGroupSelection selection =
                DependencyResolutionService.SelectDependencyGroup(
                    mutableGroups,
                    requested,
                    allowCompatibleFallbackForRequestedTfm: false);
            int? selectedGroupIndex = selection.Group is null
                ? null
                : mutableGroups?.IndexOf(selection.Group);
            if (selection.Group is not null && selectedGroupIndex is not >= 0)
            {
                throw new InvalidOperationException(
                    "The selected dependency group does not belong to the manifest.");
            }

            return new PackageDependencyGroupsResult.Available(
                new PackageDependencyGroups(
                    mutableGroups is null
                        ? []
                        :
                        [
                            .. mutableGroups.Select(group =>
                                new DeclaredPackageDependencyGroup(
                                    group.TargetFramework,
                                    [
                                        .. group.Dependencies.Select(dependency =>
                                            new DeclaredPackageDependency(
                                                dependency.Id,
                                                dependency.Version)),
                                    ])),
                        ],
                    requested,
                    selection.Group?.TargetFramework,
                    selectedGroupIndex,
                    selection.Status switch
                    {
                        DependencyResolutionService.DependencyGroupSelectionStatus.Selected =>
                            PackageDependencyGroupSelectionStatus.Selected,
                        DependencyResolutionService.DependencyGroupSelectionStatus
                            .NoDependencyGroups =>
                            PackageDependencyGroupSelectionStatus.NoDependencyGroups,
                        DependencyResolutionService.DependencyGroupSelectionStatus
                            .NoMatchingTargetFramework =>
                            PackageDependencyGroupSelectionStatus.NoMatchingTargetFramework,
                        _ => throw new InvalidOperationException(
                            "Unknown dependency-group selection status."),
                    }));
        }
        catch (Exception ex) when (
            ex is IOException
                or UnauthorizedAccessException
                or InvalidDataException
                or NuspecParseException)
        {
            return new PackageDependencyGroupsResult.Failed(ex);
        }
    }

    static string? FindRootManifest(IPackageContent content)
    {
        string[] manifests =
        [
            .. content.EnumerateEntries()
                .Where(path => path.EndsWith(
                    ".nuspec",
                    StringComparison.OrdinalIgnoreCase)),
        ];
        if (manifests.Any(path => path.Contains('\\')))
        {
            throw new InvalidDataException(
                "Package manifest paths must use package-root separators.");
        }

        string[][] manifestSegments =
        [
            .. manifests.Select(path => path.Split('/')),
        ];
        if (manifestSegments.Any(segments =>
            segments.Any(segment => !PackageEntryPath.IsSafeSegment(segment))))
        {
            throw new InvalidDataException(
                "Package manifest paths must contain safe package-entry segments.");
        }

        string[] roots =
        [
            .. manifestSegments
                .Where(segments => segments.Length == 1)
                .Select(segments => segments[0]),
        ];

        return roots.Length switch
        {
            0 => null,
            1 => roots[0],
            _ => throw new InvalidDataException(
                "Package content contains more than one root manifest."),
        };
    }

    static bool VersionsEqual(
        string? declaredVersion,
        string requestedVersion)
        => NuGetVersion.TryParse(declaredVersion, out NuGetVersion? declared)
            && NuGetVersion.TryParse(
                requestedVersion,
                out NuGetVersion? requested)
            && declared.ToNormalizedString().Equals(
                requested.ToNormalizedString(),
                StringComparison.OrdinalIgnoreCase);
}
