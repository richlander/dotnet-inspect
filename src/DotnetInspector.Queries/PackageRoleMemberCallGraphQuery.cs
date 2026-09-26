using System.Collections.Immutable;

using DotnetInspector.Packages;
using DotnetInspector.SourceSelection;
using ILInspector.CallGraph;
using ILInspector.Metadata;
using Analysis = ILInspector.Analysis;

namespace DotnetInspector.Queries;

/// <summary>
/// Exact implementation member selected from one package-role projection.
/// </summary>
public sealed record PackageRoleMemberCallGraphFocus
{
    public PackageRoleMemberCallGraphFocus(
        PackageRootIdentity package,
        Guid moduleVersionId,
        int methodToken)
    {
        ArgumentNullException.ThrowIfNull(package);
        if (moduleVersionId == Guid.Empty)
        {
            throw new ArgumentException(
                "A package-role call-graph focus requires a module version identifier.",
                nameof(moduleVersionId));
        }

        Package = package;
        ModuleVersionId = moduleVersionId;
        MethodToken = methodToken;
    }

    public PackageRootIdentity Package { get; }

    public Guid ModuleVersionId { get; }

    public int MethodToken { get; }
}

public enum PackageRoleMemberCallGraphUnavailableReason
{
    FocusParticipantUnavailable,
    FocusParticipantAmbiguous,
    FocusMethodInvalid,
    FocusMethodBodyUnavailable,
}

/// <summary>
/// Expected reason an exact package implementation member cannot produce a
/// call graph.
/// </summary>
public sealed record PackageRoleMemberCallGraphFailure(
    PackageRoleMemberCallGraphUnavailableReason Reason,
    string Detail);

/// <summary>
/// Terminal result of one package-role external-focused member call graph.
/// </summary>
public abstract record PackageRoleMemberCallGraphOutcome
{
    private PackageRoleMemberCallGraphOutcome()
    {
    }

    public sealed record Available(
        InspectionGraphDocument Document,
        ImmutableArray<PackageRoleMemberCallGraphNodePackage> NodePackages,
        PackageIntrinsicCoreLibraryIneligibilityReceipt
            IntrinsicCoreLibraryIneligibility)
        : PackageRoleMemberCallGraphOutcome;

    public sealed record Unavailable(
        PackageRoleMemberCallGraphFailure Failure)
        : PackageRoleMemberCallGraphOutcome;
}

/// <summary>
/// Exact package Root that uniquely contributed one graph node.
/// </summary>
public sealed record PackageRoleMemberCallGraphNodePackage(
    int NodeId,
    PackageRootIdentity Package);

/// <summary>
/// Projects one exact implementation MethodDef through an existing
/// package-role context.
/// </summary>
public static class PackageRoleMemberCallGraphQuery
{
    public static InspectionQuery<PackageRoleMemberCallGraphOutcome>
        Definition
    { get; } =
        new(
            "Package-role external-focused member call graph",
            InspectionCost.Unbounded);

    public static PackageRoleMemberCallGraphOutcome Execute(
        PackageAssemblyContextProjection projection,
        PackageRoleMemberCallGraphFocus focus,
        MemberCallGraphCalleeNeighborhoodRequest request) =>
        ExecuteCore(
            projection,
            focus,
            request,
            PackageSupplyChainBaselinePolicy.CreateNothing(
                [focus.Package.PackageId]),
            CancellationToken.None);

    public static PackageRoleMemberCallGraphOutcome Execute(
        PackageAssemblyContextProjection projection,
        PackageRoleMemberCallGraphFocus focus,
        MemberCallGraphCalleeNeighborhoodRequest request,
        PackageSupplyChainBaselinePolicy baseline) =>
        ExecuteCore(
            projection,
            focus,
            request,
            baseline,
            CancellationToken.None);

    internal static PackageRoleMemberCallGraphOutcome
        ExecuteWithCancellation(
        PackageAssemblyContextProjection projection,
        PackageRoleMemberCallGraphFocus focus,
        MemberCallGraphCalleeNeighborhoodRequest request,
        PackageSupplyChainBaselinePolicy baseline,
        CancellationToken cancellationToken) =>
        ExecuteCore(
            projection,
            focus,
            request,
            baseline,
            cancellationToken);

    private static PackageRoleMemberCallGraphOutcome ExecuteCore(
        PackageAssemblyContextProjection projection,
        PackageRoleMemberCallGraphFocus focus,
        MemberCallGraphCalleeNeighborhoodRequest request,
        PackageSupplyChainBaselinePolicy baseline,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(projection);
        ArgumentNullException.ThrowIfNull(focus);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(baseline);
        if (!baseline.RootPackageIds.Contains(
                focus.Package.PackageId,
                StringComparer.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                "The package supply-chain baseline does not contain the focused root Package ID.",
                nameof(baseline));
        }
        cancellationToken.ThrowIfCancellationRequested();

        PackageAssemblyContextRoleProjection role =
            projection.ImplementationRole
            ?? projection.SurfaceRole;
        ImmutableArray<PackageAssemblyRoleParticipant> participants =
            role.Participants;
        PackageIntrinsicCoreLibraryIneligibilityReceipt
            intrinsicCoreLibraryIneligibility =
                role.IntrinsicCoreLibraryIneligibility;

        return role.Use<PackageRoleMemberCallGraphOutcome>(group =>
        {
            var candidates =
                ImmutableArray.CreateBuilder<
                    PackageAssemblyRoleParticipant>();
            foreach (PackageAssemblyRoleParticipant participant
                in participants)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!ReferenceEquals(
                        participant.Package,
                        focus.Package))
                {
                    continue;
                }

                AssemblyImageAccessResult<Guid> module =
                    group.UseAssemblySession(
                        participant.Participant.Assembly,
                        static session => session.ModuleVersionId());
                if (module is AssemblyImageAccessResult<Guid>.Available
                        available
                    && available.Value == focus.ModuleVersionId)
                {
                    candidates.Add(participant);
                }
            }
            if (candidates.Count == 0)
            {
                return Unavailable(
                    PackageRoleMemberCallGraphUnavailableReason
                        .FocusParticipantUnavailable,
                    "The exact root package implementation module is not present in the package-role context.");
            }
            if (candidates.Count != 1)
            {
                return Unavailable(
                    PackageRoleMemberCallGraphUnavailableReason
                        .FocusParticipantAmbiguous,
                    "The exact root package implementation module identifies more than one package-role participant.");
            }

            cancellationToken.ThrowIfCancellationRequested();
            PackageAssemblyRoleParticipant selected = candidates[0];
            bool? hasBody;
            try
            {
                using var metadata =
                    PdbContext.OpenMetadataOnly(
                        selected.Participant.Assembly);
                hasBody = metadata.MethodHasBody(focus.MethodToken);
            }
            catch (BadImageFormatException)
            {
                return Unavailable(
                    PackageRoleMemberCallGraphUnavailableReason
                        .FocusMethodInvalid,
                    "The selected implementation MethodDef could not be decoded.");
            }

            if (hasBody is null)
            {
                return Unavailable(
                    PackageRoleMemberCallGraphUnavailableReason
                        .FocusMethodInvalid,
                    "The selected implementation MethodDef is not valid in the exact implementation module.");
            }
            if (hasBody is false)
            {
                return Unavailable(
                    PackageRoleMemberCallGraphUnavailableReason
                        .FocusMethodBodyUnavailable,
                    "The selected implementation MethodDef has no managed body.");
            }

            cancellationToken.ThrowIfCancellationRequested();
            using var session = new MemberCallGraphSession(
                group,
                selected.Participant.Assembly,
                focus.MethodToken);
            var nodePackages =
                new Dictionary<
                    Analysis.GraphNodeIdentity,
                    PackageRootIdentity>();
            InspectionGraphDocument document =
                session.CrossLibraryCalleeNeighborhoodWithCancellation(
                    request,
                    node =>
                    {
                        MemberCallGraphExternalFocusMembership membership =
                            ClassifyPackageMembership(
                                participants,
                                node,
                                baseline,
                                out PackageRootIdentity? package);
                        if (package is not null)
                            nodePackages.Add(node.Identity, package);
                        return membership;
                    },
                    cancellationToken);
            return new PackageRoleMemberCallGraphOutcome.Available(
                document,
                NodePackages(document, nodePackages),
                intrinsicCoreLibraryIneligibility);
        });
    }

    static ImmutableArray<PackageRoleMemberCallGraphNodePackage>
        NodePackages(
            InspectionGraphDocument document,
            IReadOnlyDictionary<
                Analysis.GraphNodeIdentity,
                PackageRootIdentity> packages)
    {
        var result =
            ImmutableArray.CreateBuilder<
                PackageRoleMemberCallGraphNodePackage>();
        foreach (InspectionGraphNode node in document.Nodes)
        {
            Analysis.GraphNodeIdentity identity = node.Subject
                is InspectionGraphSubject.MemberSubject
            {
                Identity:
                        InspectionGraphMemberIdentity.CallGraph callGraph,
            }
                    ? callGraph.Identity
                    : throw new InvalidOperationException(
                        "A package-role member call graph contained a non-member node.");
            if (packages.TryGetValue(
                    identity,
                    out PackageRootIdentity? package))
            {
                result.Add(
                    new PackageRoleMemberCallGraphNodePackage(
                        node.Id,
                        package));
            }
        }

        return result.ToImmutable();
    }

    internal static MemberCallGraphExternalFocusMembership
        ClassifyPackageMembership(
        ImmutableArray<PackageAssemblyRoleParticipant> participants,
        CallGraphNode node,
        PackageSupplyChainBaselinePolicy baseline,
        out PackageRootIdentity? package)
    {
        AssemblyReferenceIdentity? identity =
            node.DefinitionAssemblyIdentity;
        if (identity is null)
        {
            AssemblyReferenceIdentity? reference =
                ReferencedAssemblyIdentity(node.Member);
            AssemblyReferenceIdentity? resolution =
                node.ResolutionAssemblyIdentity;
            if (reference is null
                || resolution is null
                || !reference.IsEquivalentTo(resolution))
            {
                package = null;
                return MemberCallGraphExternalFocusMembership.Unknown;
            }
            identity = reference;
        }

        (package, bool ambiguous) =
            MatchPackage(
                participants,
                identity);
        if (package is null || ambiguous)
        {
            package = null;
            return MemberCallGraphExternalFocusMembership.Unknown;
        }

        return baseline.Classify(package.PackageId)
                is PackageSupplyChainClassification.Baseline
            ? MemberCallGraphExternalFocusMembership.Hub
            : MemberCallGraphExternalFocusMembership.External;
    }

    internal static (PackageRootIdentity? Package, bool Ambiguous)
        MatchPackage(
        ImmutableArray<PackageAssemblyRoleParticipant> participants,
        AssemblyReferenceIdentity? definitionIdentity)
    {
        if (definitionIdentity is null)
            return (null, false);

        PackageRootIdentity? package = null;
        foreach (PackageAssemblyRoleParticipant participant
            in participants)
        {
            if (!participant.Participant.Assembly.Identity
                    .IsEquivalentTo(definitionIdentity))
            {
                continue;
            }
            if (package is null)
            {
                package = participant.Package;
            }
            else if (!ReferenceEquals(package, participant.Package))
            {
                return (null, true);
            }
        }

        return (package, false);
    }

    static AssemblyReferenceIdentity? ReferencedAssemblyIdentity(
        Analysis.MemberRef member)
    {
        Analysis.TypeRef type = member.DeclaringType;
        while (type.Kind == Analysis.TypeRefKind.GenericInstance
            && type.ElementType is not null)
        {
            type = type.ElementType;
        }
        if (type.Kind != Analysis.TypeRefKind.Definition)
            return null;

        return type.Resolution?.Origin switch
        {
            Analysis.TypeReferenceOrigin.AssemblyReference reference =>
                reference.Assembly,
            Analysis.TypeReferenceOrigin.CurrentAssembly current =>
                current.Assembly,
            _ => null,
        };
    }

    private static PackageRoleMemberCallGraphOutcome.Unavailable Unavailable(
        PackageRoleMemberCallGraphUnavailableReason reason,
        string detail) =>
        new(new(reason, detail));
}
