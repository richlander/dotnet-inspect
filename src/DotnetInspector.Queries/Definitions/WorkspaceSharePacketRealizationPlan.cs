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
    /// Package acquisitions that the host realized.
    /// </summary>
    public WorkspaceDefinitionShareProjectionReceipt CreateShareProjection(
        WorkspaceDefinitionSnapshot definition,
        IReadOnlyList<PackageRootBinding> packageAcquisitions)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(packageAcquisitions);

        if (!ReferenceEquals(definition.Plan, Plan))
        {
            throw new ArgumentException(
                "The Workspace definition does not belong to this packet realization.",
                nameof(definition));
        }

        var declarations = Plan.Contexts
            .SelectMany(static context => context.Members
                .OfType<WorkspaceMemberCoordinate.PackageMember>()
                .Select(package => (Context: context, Package: package)))
            .ToArray();
        if (declarations.Length != packageAcquisitions.Count)
        {
            throw new ArgumentException(
                "The Package acquisitions do not match the packet's declarations.",
                nameof(packageAcquisitions));
        }

        for (int index = 0; index < declarations.Length; index++)
        {
            (WorkspaceContextInput context,
                WorkspaceMemberCoordinate.PackageMember package) =
                declarations[index];
            if (!MatchesDeclaration(
                    context,
                    package,
                    packageAcquisitions[index]))
            {
                throw new ArgumentException(
                    "A Package acquisition does not match its packet declaration.",
                    nameof(packageAcquisitions));
            }
        }

        PackageRootBinding[] packageRoots =
        [
            .. packageAcquisitions.DistinctBy(
                static root => root.CreateReacquisitionRequest()),
        ];
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

    static bool MatchesDeclaration(
        WorkspaceContextInput context,
        WorkspaceMemberCoordinate.PackageMember package,
        PackageRootBinding root)
    {
        var descriptor = new WorkspacePackageDescriptor(root);
        string? framework = package.Framework ?? context.Framework;
        string? runtimeIdentifier =
            package.RuntimeIdentifier ?? context.RuntimeIdentifier;
        return string.Equals(
                package.PackageId,
                descriptor.PackageId,
                StringComparison.OrdinalIgnoreCase)
            && (package.Version is null
                || NuGetVersion.Parse(package.Version)
                    == NuGetVersion.Parse(descriptor.PackageVersion))
            && string.Equals(
                framework,
                descriptor.RequestedTargetFramework,
                StringComparison.OrdinalIgnoreCase)
            && string.Equals(
                runtimeIdentifier,
                descriptor.RuntimeIdentifier,
                StringComparison.Ordinal);
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
