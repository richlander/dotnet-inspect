using System.Collections.Immutable;
using DotnetInspector.Packages;
using DotnetInspector.Services;

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
    PackageDependencyGroupSelectionStatus SelectionStatus);

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
        string? requestedTargetFramework = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId);

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
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    "The package manifest identity does not match the requested package.");
            }

            string? requested = string.IsNullOrWhiteSpace(requestedTargetFramework)
                ? null
                : requestedTargetFramework;
            List<DependencyGroup>? mutableGroups = nuspec.DependencyGroups;
            DependencyResolutionService.DependencyGroupSelection selection =
                DependencyResolutionService.SelectDependencyGroup(
                    mutableGroups,
                    requested,
                    allowCompatibleFallbackForRequestedTfm: false);

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

        string[] roots =
        [
            .. manifests.Where(path => !path.Contains('/')),
        ];
        return roots.Length switch
        {
            0 => null,
            1 => roots[0],
            _ => throw new InvalidDataException(
                "Package content contains more than one root manifest."),
        };
    }
}
