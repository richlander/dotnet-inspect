using System.Collections.Immutable;
using DotnetInspector.Packages;
using DotnetInspector.Services;
using NuGet.Versioning;
using NuGetFetch;

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
    ImmutableArray<DeclaredPackageDependency> Dependencies,
    bool IsImplicitManifestGroup = false);

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

    /// <summary>
    /// Returns the canonical version when the declaration names exactly one
    /// coordinate, or <see langword="null"/> for a range or floating declaration.
    /// </summary>
    public static string? GetExactVersion(string? declaredRange)
    {
        VersionRange range = Parse(declaredRange);
        return !range.IsFloating
            && range.MinVersion is { } minimum
            && range.MaxVersion is { } maximum
            && range.IsMinInclusive
            && range.IsMaxInclusive
            && minimum == maximum
                ? minimum.ToNormalizedString()
                : null;
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
        Exception Error,
        PackageManifestFailure? ManifestFailure = null) :
        PackageDependencyGroupsResult;
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
            string? manifestPath =
                PackageManifestContent.FindRootManifest(content);
            if (manifestPath is null)
                return new PackageDependencyGroupsResult.NoManifest();

            if (!content.TryOpenEntry(
                    manifestPath,
                    PackageManifestFactsQuery.MaxManifestBytes,
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
                        PackageManifestFactsQuery.MaxManifestBytes,
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
            }

            PackageSourceCoordinate coordinate =
                PackageSourceCoordinate.Create(
                    packageId,
                    packageVersion);
            PackageManifestFactsResult facts =
                PackageManifestFactsQuery.Execute(
                    manifestBytes,
                    coordinate);
            if (facts is PackageManifestFactsResult.Failed failed)
            {
                return new PackageDependencyGroupsResult.Failed(
                    new InvalidDataException(failed.Failure.Message),
                    failed.Failure);
            }

            string? requested = string.IsNullOrWhiteSpace(requestedTargetFramework)
                ? null
                : requestedTargetFramework;
            return new PackageDependencyGroupsResult.Available(
                ProjectDependencyGroups(
                    ((PackageManifestFactsResult.Available)facts).Value,
                    requested));
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

    internal static PackageDependencyGroups ProjectDependencyGroups(
        PackageManifestFacts facts,
        string? requestedTargetFramework)
    {
        List<DependencyGroup> mutableGroups =
        [
            .. facts.DependencyGroups.Select(group =>
                new DependencyGroup
                {
                    TargetFramework = group.TargetFramework,
                    IsImplicitManifestGroup =
                        group.IsImplicitManifestGroup,
                    Dependencies =
                    [
                        .. group.Dependencies.Select(dependency =>
                            new PackageDependency
                            {
                                Id = dependency.Id,
                                Version = dependency.VersionRange,
                            }),
                    ],
                }),
        ];
        DependencyResolutionService.DependencyGroupSelection selection =
            DependencyResolutionService.SelectDependencyGroup(
                mutableGroups,
                requestedTargetFramework,
                allowCompatibleFallbackForRequestedTfm: false);
        int? selectedGroupIndex = selection.Group is null
            ? null
            : mutableGroups?.IndexOf(selection.Group);
        if (selection.Group is not null && selectedGroupIndex is not >= 0)
        {
            throw new InvalidOperationException(
                "The selected dependency group does not belong to the manifest.");
        }

        return new PackageDependencyGroups(
            facts.DependencyGroups,
            requestedTargetFramework,
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
            });
    }
}
