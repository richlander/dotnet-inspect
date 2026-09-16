using System.Diagnostics;
using DotnetInspector.Packages;
using NuGet.Versioning;

namespace DotnetInspector.Queries.Definitions;

/// <summary>
/// Resource-free realization intent lowered from one canonical Workspace
/// packet.
/// </summary>
public sealed class WorkspaceSharePacketRealizationPlan
{
    readonly WorkspaceSharePacket _packet;

    internal WorkspaceSharePacketRealizationPlan(
        WorkspaceSharePacket packet,
        WorkspacePlan plan,
        WorkspaceContextInput selectedContext)
    {
        _packet = packet;
        Plan = plan;
        SelectedContext = selectedContext;
    }

    public WorkspacePlan Plan { get; }

    public WorkspaceContextInput SelectedContext { get; }

    /// <summary>
    /// Associates the retained packet with the exact Workspace definition and
    /// selected context that the host realized.
    /// </summary>
    public WorkspaceDefinitionShareProjectionReceipt CreateShareProjection(
        WorkspaceDefinitionSnapshot definition,
        IReadOnlyList<PackageRootBinding> packageRoots)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(packageRoots);

        if (!ReferenceEquals(definition.Plan, Plan))
        {
            throw new ArgumentException(
                "The Workspace definition does not belong to this packet realization.",
                nameof(definition));
        }

        if (!PackageRootsMatchPlan(packageRoots))
        {
            throw new ArgumentException(
                "The realized Package roots do not match the packet's Workspace plan.",
                nameof(packageRoots));
        }

        if (!ScopeMatches(definition.Scope, packageRoots))
        {
            throw new ArgumentException(
                "The Workspace Package Scope does not match the packet's realized Package roots.",
                nameof(definition));
        }

        return new WorkspaceDefinitionShareProjectionReceipt(
            definition,
            WorkspaceDefinitionShareProjectionSource.PacketInput,
            _packet);
    }

    bool PackageRootsMatchPlan(IReadOnlyList<PackageRootBinding> roots)
    {
        var expected = new List<ExpectedPackage>();
        foreach (WorkspaceContextInput context in Plan.Contexts)
        {
            foreach (WorkspaceMemberCoordinate.PackageMember package
                in context.Members.OfType<WorkspaceMemberCoordinate.PackageMember>())
            {
                var candidate = new ExpectedPackage(
                    package.PackageId,
                    package.Version,
                    package.Framework ?? context.Framework,
                    package.RuntimeIdentifier ?? context.RuntimeIdentifier);
                if (!expected.Any(existing => existing.EquivalentTo(candidate)))
                    expected.Add(candidate);
            }
        }

        if (expected.Count != roots.Count)
            return false;

        for (int index = 0; index < roots.Count; index++)
        {
            if (!expected[index].Matches(roots[index]))
                return false;
        }

        return true;
    }

    static bool ScopeMatches(
        WorkspaceScopeRevision scope,
        IReadOnlyList<PackageRootBinding> roots)
    {
        if (scope.Packages.Length != roots.Count)
            return false;

        for (int index = 0; index < roots.Count; index++)
        {
            WorkspacePackageDescriptor actual = scope.Packages[index].Package;
            var expected = new WorkspacePackageDescriptor(roots[index]);
            if (actual.Coordinate != expected.Coordinate
                || actual.PackageId != expected.PackageId
                || actual.PackageVersion != expected.PackageVersion
                || actual.RequestedTargetFramework
                    != expected.RequestedTargetFramework
                || actual.SelectedTargetFramework
                    != expected.SelectedTargetFramework
                || actual.RuntimeIdentifier != expected.RuntimeIdentifier
                || actual.SelectionStatus != expected.SelectionStatus)
            {
                return false;
            }
        }

        return true;
    }

    sealed record ExpectedPackage(
        string PackageId,
        string? Version,
        string? Framework,
        string? RuntimeIdentifier)
    {
        public bool EquivalentTo(ExpectedPackage other) =>
            string.Equals(
                PackageId,
                other.PackageId,
                StringComparison.OrdinalIgnoreCase)
            && VersionsEqual(Version, other.Version)
            && string.Equals(
                Framework,
                other.Framework,
                StringComparison.OrdinalIgnoreCase)
            && string.Equals(
                RuntimeIdentifier,
                other.RuntimeIdentifier,
                StringComparison.Ordinal);

        public bool Matches(PackageRootBinding root)
        {
            var descriptor = new WorkspacePackageDescriptor(root);
            return string.Equals(
                    PackageId,
                    descriptor.PackageId,
                    StringComparison.OrdinalIgnoreCase)
                && (Version is null
                    || VersionsEqual(Version, descriptor.PackageVersion))
                && string.Equals(
                    Framework,
                    descriptor.RequestedTargetFramework,
                    StringComparison.OrdinalIgnoreCase)
                && string.Equals(
                    RuntimeIdentifier,
                    descriptor.RuntimeIdentifier,
                    StringComparison.Ordinal);
        }

        static bool VersionsEqual(string? left, string? right) =>
            left is null
                ? right is null
                : right is not null
                    && NuGetVersion.Parse(left)
                        == NuGetVersion.Parse(right);
    }
}

/// <summary>
/// Lowers one canonical Workspace packet into resource-free realization
/// intent without acquiring or opening inspected content.
/// </summary>
public static class WorkspaceSharePacketRealization
{
    public static WorkspaceSharePacketRealizationPlan Prepare(
        string encoded,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(encoded);
        cancellationToken.ThrowIfCancellationRequested();

        WorkspaceSharePacket packet =
            WorkspaceSharePacketCodec.Decode(encoded, cancellationToken);
        WorkspaceDefinition workspace = packet.FormatVersion switch
        {
            WorkspaceSharePacketCodec.LegacyFormatVersion =>
                WorkspaceSharePacketTransposer.ToDefinitions(
                    packet,
                    cancellationToken).Workspace,
            WorkspaceSharePacketCodec.CurrentFormatVersion =>
                WorkspaceSharePacketTransposer.ToCommittedDefinitions(
                    packet,
                    cancellationToken).Workspace
                ?? throw new InspectionDefinitionException(
                    "A Workspace packet must resolve one Workspace definition."),
            _ => throw new UnreachableException(),
        };
        if (workspace.Contexts.Any(static context =>
                !string.IsNullOrWhiteSpace(context.Subscribe)
                || context.Members.Any(static member =>
                    member is not DefinitionMemberCoordinate
                        .PackageCoordinate)))
        {
            throw new InspectionDefinitionException(
                "Workspace inventory packet restoration currently supports direct Package context members only; group subscriptions and non-Package members require complete Workspace restoration.");
        }

        var plan = new WorkspacePlan(
            [],
            [
                .. workspace.Contexts.Select(static context =>
                    new WorkspaceContextInput
                    {
                        Framework = context.Framework,
                        RuntimeIdentifier = context.RuntimeIdentifier,
                        Members =
                        [
                            .. context.Members
                                .OfType<DefinitionMemberCoordinate
                                    .PackageCoordinate>()
                                .Select(static package =>
                                    WorkspaceMemberCoordinate.Package(
                                        package.Id,
                                        package.Version,
                                        package.Framework,
                                        package.RuntimeIdentifier)),
                        ],
                    }),
            ]);
        WorkspaceContextInput selectedContext =
            plan.Contexts[packet.SelectedContextIndex];
        return new(packet, plan, selectedContext);
    }
}
