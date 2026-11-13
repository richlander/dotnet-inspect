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

    public static Task<PackageDependencyGroupsResult> ExecuteAsync(
        PackageInspectionInput input,
        string? requestedTargetFramework = null,
        CancellationToken cancellationToken = default,
        bool allowCompatibleFallbackForRequestedTfm = false)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (input.PackageId is null || input.PackageVersion is null)
        {
            return Task.FromResult<PackageDependencyGroupsResult>(
                new PackageDependencyGroupsResult.Failed(
                    new InvalidOperationException(
                        "Package dependency inspection requires an exact package identity.")));
        }

        return ExecuteAsync(
            input.Content,
            input.PackageId,
            input.PackageVersion,
            requestedTargetFramework,
            cancellationToken,
            allowCompatibleFallbackForRequestedTfm);
    }

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

            return Execute(
                manifestBytes,
                packageId,
                packageVersion,
                requestedTargetFramework,
                allowCompatibleFallbackForRequestedTfm);
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

    /// <summary>
    /// Projects dependency groups from one already selected bounded nuspec.
    /// </summary>
    public static PackageDependencyGroupsResult Execute(
        ReadOnlyMemory<byte> manifestBytes,
        string packageId,
        string packageVersion,
        string? requestedTargetFramework = null,
        bool allowCompatibleFallbackForRequestedTfm = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId);
        ArgumentException.ThrowIfNullOrWhiteSpace(packageVersion);

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

        PackageManifestFacts manifest =
            ((PackageManifestFactsResult.Available)facts).Value;
        return Execute(
            manifest,
            requestedTargetFramework,
            allowCompatibleFallbackForRequestedTfm);
    }

    public static PackageDependencyGroupsResult Execute(
        PackageManifestFacts manifest,
        string? requestedTargetFramework = null,
        bool allowCompatibleFallbackForRequestedTfm = false)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        if (manifest.DependencyDeclarations is
            PackageManifestDependencyFactsResult.Failed failed)
        {
            return new PackageDependencyGroupsResult.Failed(
                new InvalidDataException(failed.Failure.Message),
                failed.Failure);
        }

        string? requested = string.IsNullOrWhiteSpace(requestedTargetFramework)
            ? null
            : requestedTargetFramework;
        return new PackageDependencyGroupsResult.Available(
            manifest,
            ProjectDependencyGroups(
                manifest,
                requested,
                allowCompatibleFallbackForRequestedTfm));
    }

    internal static PackageDependencyGroups ProjectDependencyGroups(
        PackageManifestFacts facts,
        string? requestedTargetFramework,
        bool allowCompatibleFallbackForRequestedTfm = false)
    {
        DependencyResolutionService.DeclaredDependencyGroupSelection
            selection =
            DependencyResolutionService.SelectDependencyGroup(
                facts.DependencyGroups,
                requestedTargetFramework,
                allowCompatibleFallbackForRequestedTfm);

        return new PackageDependencyGroups(
            facts.DependencyGroups,
            requestedTargetFramework,
            selection.Group?.TargetFramework,
            selection.GroupIndex,
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
            SelectedGroup = selection.Group,
        };
    }
}
