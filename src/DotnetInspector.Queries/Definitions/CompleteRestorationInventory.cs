using System.Collections.Immutable;
using DotnetInspector.Packages;
using ILInspector.Metadata;

namespace DotnetInspector.Queries.Definitions;

/// <summary>Detached inventory captured independently of restored focus.</summary>
public sealed record CompleteRestorationInventory(
    ImmutableArray<CompleteRestorationPackageInventory> Packages,
    ImmutableArray<CompleteRestorationPlatformInventory> Platforms);

public sealed record CompleteRestorationPackageInventory(
    string NavigationId,
    int ContextIndex,
    WorkspacePackageDescriptor Package,
    PackageCompileAssetSelection Selection,
    ImmutableArray<PackageContentEntry> Entries,
    ImmutableArray<CompleteRestorationPackageLibrary> Libraries,
    AssemblyContextApiSurfaceResult Surface);

public sealed record CompleteRestorationPackageLibrary(
    PackageCompileAsset Asset,
    AssemblyContextSubject Subject);

public sealed record CompleteRestorationPlatformInventory(
    string NavigationId,
    int ContextIndex,
    string Family,
    string Version,
    string Framework,
    string? RuntimeIdentifier,
    ImmutableArray<CompleteRestorationPlatformLibrary> Libraries,
    AssemblyContextApiSurfaceResult Surface);

public sealed record CompleteRestorationPlatformLibrary(
    RealizedMemberCoordinate.Platform Coordinate,
    AssemblyContextSubject Subject,
    string? AssetFileName);

internal sealed record CompleteRestorationInventoryCapture(
    CompleteRestorationInventory? Inventory,
    CompleteRestorationFailure? Failure);

internal static class CompleteRestorationInventories
{
    internal static CompleteRestorationInventoryCapture Capture(
        CompleteRestorationPlan plan,
        ImmutableArray<WorkspaceContextLoadOutcome.Loaded> contexts,
        ImmutableArray<PackageRootBinding> roots,
        IReadOnlyDictionary<string, PackageArtifactRootRequest> requests,
        IReadOnlyDictionary<string, NavigationPackageEvaluation> evaluations,
        ApiSurfaceProjectionLimits platformLimits,
        CancellationToken cancellationToken)
    {
        var packages =
            ImmutableArray.CreateBuilder<CompleteRestorationPackageInventory>();
        foreach ((string navigationId, PackageNavigationSource source)
            in plan.PackageSources)
        {
            cancellationToken.ThrowIfCancellationRequested();
            PackageRootBinding binding = roots.Single(
                binding => PackageArtifactRootRequest.From(binding)
                    == requests[navigationId]);
            if (binding.Root.Content is not IPackageContentEntryManifest manifest)
            {
                return new(
                    null,
                    new CompleteRestorationFailure.ProjectionFailed(
                        $"Navigation row '{navigationId}' has no retained "
                            + "Package entry manifest for inventory capture."));
            }

            NavigationPackageEvaluation evaluation = evaluations[navigationId];
            packages.Add(new(
                navigationId,
                source.ContextIndex,
                evaluation.Occurrence.Occurrence.Package,
                binding.Root.AssetSelection,
                [.. manifest.EnumerateEntriesWithLengths()],
                [
                    .. evaluation.Libraries.Select(static library =>
                        new CompleteRestorationPackageLibrary(
                            library.Asset,
                            new AssemblyContextSubject(
                                library.Library.Participant.Assembly))),
                ],
                evaluation.Surface));
        }

        var platforms =
            ImmutableArray.CreateBuilder<CompleteRestorationPlatformInventory>();
        foreach ((string navigationId, GroupNavigationSource source)
            in plan.GroupSources)
        {
            cancellationToken.ThrowIfCancellationRequested();
            WorkspaceContextLoadOutcome.Loaded context =
                contexts[source.ContextIndex];
            WorkspaceMemberCoordinate declaration =
                plan.WorkspacePlan.Contexts[source.ContextIndex]
                    .Members[source.MemberIndex];
            WorkspaceContextMember[] members =
            [
                .. context.Members.Where(member => member.Declared == declaration),
            ];
            if (members.Length == 0
                || members[0].Realized
                    is not RealizedMemberCoordinate.Platform coordinate)
            {
                return new(
                    null,
                    new CompleteRestorationFailure.ProjectionFailed(
                        $"Navigation row '{navigationId}' has no realized "
                            + "Platform members for inventory capture."));
            }

            AssemblyContextApiSurfaceResult surface =
                AssemblyContextApiSurfaceQuery.ExecuteBounded(
                    context.Group,
                    ApiSurfaceScope.PublicWithNonPublicTypes,
                    platformLimits,
                    [.. members.Select(static member => member.Participant)]);
            cancellationToken.ThrowIfCancellationRequested();
            platforms.Add(new(
                navigationId,
                source.ContextIndex,
                coordinate.Family,
                coordinate.Version,
                coordinate.Framework,
                source.RuntimeIdentifier,
                [
                    .. members.Select(static member =>
                        new CompleteRestorationPlatformLibrary(
                            (RealizedMemberCoordinate.Platform)member.Realized,
                            new AssemblyContextSubject(member.Participant.Assembly),
                            member.Participant.Assembly.AssetFileName)),
                ],
                surface));
        }

        return new(new(packages.ToImmutable(), platforms.ToImmutable()), null);
    }
}
