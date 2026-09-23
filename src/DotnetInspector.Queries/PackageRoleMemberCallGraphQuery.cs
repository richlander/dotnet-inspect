using System.Collections.Immutable;

using DotnetInspector.Packages;
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
        ImmutableArray<PackageRoleMemberCallGraphNodePackage> NodePackages)
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
            CancellationToken.None);

    internal static PackageRoleMemberCallGraphOutcome
        ExecuteWithCancellation(
        PackageAssemblyContextProjection projection,
        PackageRoleMemberCallGraphFocus focus,
        MemberCallGraphCalleeNeighborhoodRequest request,
        CancellationToken cancellationToken) =>
        ExecuteCore(
            projection,
            focus,
            request,
            cancellationToken);

    private static PackageRoleMemberCallGraphOutcome ExecuteCore(
        PackageAssemblyContextProjection projection,
        PackageRoleMemberCallGraphFocus focus,
        MemberCallGraphCalleeNeighborhoodRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(projection);
        ArgumentNullException.ThrowIfNull(focus);
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        PackageAssemblyContextRoleProjection role =
            projection.ImplementationRole
            ?? projection.SurfaceRole;
        ImmutableArray<PackageAssemblyRoleParticipant> participants =
            role.Participants;

        return role.Use<PackageRoleMemberCallGraphOutcome>(group =>
        {
            var candidates =
                ImmutableArray.CreateBuilder<
                    PackageAssemblyRoleParticipant>();
            foreach (PackageAssemblyRoleParticipant participant
                in role.Participants)
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
            InspectionGraphDocument document =
                session.CrossLibraryCalleeNeighborhoodWithCancellation(
                    request,
                    node => ClassifyPackageMembership(
                        participants,
                        focus.Package,
                        node.Member),
                    cancellationToken);
            return new PackageRoleMemberCallGraphOutcome.Available(
                document,
                NodePackages(document, participants));
        });
    }

    static ImmutableArray<PackageRoleMemberCallGraphNodePackage>
        NodePackages(
            InspectionGraphDocument document,
            ImmutableArray<PackageAssemblyRoleParticipant> participants)
    {
        var result =
            ImmutableArray.CreateBuilder<
                PackageRoleMemberCallGraphNodePackage>();
        foreach (InspectionGraphNode node in document.Nodes)
        {
            Analysis.MemberRef member = node.Subject
                is InspectionGraphSubject.MemberSubject
                {
                    Identity:
                        InspectionGraphMemberIdentity.CallGraph callGraph,
                }
                    ? callGraph.Member
                    : throw new InvalidOperationException(
                        "A package-role member call graph contained a non-member node.");
            (PackageRootIdentity? package, bool ambiguous) =
                MatchPackage(participants, member);
            if (package is not null && !ambiguous)
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
        PackageRootIdentity root,
        Analysis.MemberRef member)
    {
        (PackageRootIdentity? package, bool ambiguous) =
            MatchPackage(participants, member);
        if (package is null || ambiguous)
            return MemberCallGraphExternalFocusMembership.Unknown;

        return ReferenceEquals(package, root)
            ? MemberCallGraphExternalFocusMembership.Hub
            : MemberCallGraphExternalFocusMembership.External;
    }

    internal static (PackageRootIdentity? Package, bool Ambiguous)
        MatchPackage(
        ImmutableArray<PackageAssemblyRoleParticipant> participants,
        Analysis.MemberRef member)
    {
        Analysis.TypeRef? definition =
            DeclaringTypeDefinition(member.DeclaringType);
        AssemblyReferenceIdentity? identity =
            definition?.Resolution?.Origin switch
            {
                Analysis.TypeReferenceOrigin.AssemblyReference
                    reference => reference.Assembly,
                Analysis.TypeReferenceOrigin.CurrentAssembly
                    current => current.Assembly,
                _ => null,
            };
        string? assemblyName =
            identity?.Name
            ?? definition?.Assembly
            ?? member.DeclaringType.Assembly;
        return string.IsNullOrWhiteSpace(assemblyName)
            ? (null, false)
            : MatchPackage(participants, identity, assemblyName);
    }

    internal static (PackageRootIdentity? Package, bool Ambiguous)
        MatchPackage(
            ImmutableArray<PackageAssemblyRoleParticipant> participants,
            AssemblyReferenceIdentity? identity,
            string assemblyName)
    {
        PackageRootIdentity? package = null;
        foreach (PackageAssemblyRoleParticipant participant
            in participants)
        {
            bool matches = identity is not null
                ? participant.Participant.Assembly.Identity
                    .IsEquivalentTo(identity)
                : participant.Participant.Assembly.Identity.Name.Equals(
                    assemblyName,
                    StringComparison.OrdinalIgnoreCase);
            if (!matches)
                continue;
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

    static Analysis.TypeRef? DeclaringTypeDefinition(
        Analysis.TypeRef type)
    {
        while (type.Kind == Analysis.TypeRefKind.GenericInstance
            && type.ElementType is not null)
        {
            type = type.ElementType;
        }
        return type.Kind == Analysis.TypeRefKind.Definition ? type : null;
    }

    private static PackageRoleMemberCallGraphOutcome.Unavailable Unavailable(
        PackageRoleMemberCallGraphUnavailableReason reason,
        string detail) =>
        new(new(reason, detail));
}
