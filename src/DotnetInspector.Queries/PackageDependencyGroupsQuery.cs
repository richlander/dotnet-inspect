using System.Collections.Immutable;
using DotnetInspector.Packages;
using DotnetInspector.Services;
using NuGetFetch;

namespace DotnetInspector.Queries;

/// <summary>The target-framework selection outcome for declared dependency groups.</summary>
public enum PackageDependencyGroupSelectionStatus
{
    Selected,
    NoDependencyGroups,
    NoMatchingTargetFramework,
}

/// <summary>A package manifest's dependency groups and target-framework selection outcome.</summary>
public sealed record PackageDependencyGroups(
    ImmutableArray<DeclaredPackageDependencyGroup> Groups,
    string? RequestedTargetFramework,
    string? SelectedTargetFramework,
    int? SelectedGroupIndex,
    PackageDependencyGroupSelectionStatus SelectionStatus)
{
    /// <summary>
    /// The logical selected group, including coalesced implicit manifest runs.
    /// </summary>
    public DeclaredPackageDependencyGroup? SelectedGroup { get; init; }
}

/// <summary>The typed outcome of projecting declared dependency groups from package content.</summary>
public abstract record PackageDependencyGroupsResult
{
    private PackageDependencyGroupsResult()
    {
    }

    public sealed record Available(
        PackageManifestFacts Manifest,
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
        CancellationToken cancellationToken = default,
        bool allowCompatibleFallbackForRequestedTfm = false)
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
            PackageManifestFacts manifest =
                ((PackageManifestFactsResult.Available)facts).Value;
            return new PackageDependencyGroupsResult.Available(
                manifest,
                ProjectDependencyGroups(
                    manifest,
                    requested,
                    allowCompatibleFallbackForRequestedTfm));
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
        string? requestedTargetFramework,
        bool allowCompatibleFallbackForRequestedTfm = false)
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
                allowCompatibleFallbackForRequestedTfm);
        int? selectedGroupIndex =
            FindSelectedGroupIndex(mutableGroups, selection.Group);
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
            })
        {
            SelectedGroup = ProjectSelectedGroup(selection.Group),
        };
    }

    private static DeclaredPackageDependencyGroup? ProjectSelectedGroup(
        DependencyGroup? group) =>
        group is null
            ? null
            : new DeclaredPackageDependencyGroup(
                group.TargetFramework,
                [
                    .. group.Dependencies.Select(dependency =>
                        new DeclaredPackageDependency(
                            dependency.Id,
                            dependency.Version)),
                ],
                group.IsImplicitManifestGroup);

    private static int? FindSelectedGroupIndex(
        List<DependencyGroup> declaredGroups,
        DependencyGroup? selectedGroup)
    {
        if (selectedGroup is null)
            return null;

        int index = declaredGroups.IndexOf(selectedGroup);
        if (index >= 0 || !selectedGroup.IsImplicitManifestGroup)
            return index;

        return declaredGroups.FindIndex(group =>
            group.IsImplicitManifestGroup);
    }
}
