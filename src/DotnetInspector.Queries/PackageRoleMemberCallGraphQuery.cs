using System.Collections.Immutable;

using DotnetInspector.Packages;
using ILInspector.Metadata;

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

    public sealed record Available(InspectionGraphDocument Document)
        : PackageRoleMemberCallGraphOutcome;

    public sealed record Unavailable(
        PackageRoleMemberCallGraphFailure Failure)
        : PackageRoleMemberCallGraphOutcome;
}

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
        MemberCallGraphCalleeNeighborhoodRequest request)
    {
        ArgumentNullException.ThrowIfNull(projection);
        ArgumentNullException.ThrowIfNull(focus);
        ArgumentNullException.ThrowIfNull(request);

        PackageAssemblyContextRoleProjection role =
            projection.ImplementationRole
            ?? projection.SurfaceRole;

        return role.Use<PackageRoleMemberCallGraphOutcome>(group =>
        {
            var candidates =
                ImmutableArray.CreateBuilder<
                    PackageAssemblyRoleParticipant>();
            foreach (PackageAssemblyRoleParticipant participant
                in role.Participants)
            {
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

            using var session = new MemberCallGraphSession(
                group,
                selected.Participant.Assembly,
                focus.MethodToken);
            return new PackageRoleMemberCallGraphOutcome.Available(
                session.CrossLibraryCalleeNeighborhood(request));
        });
    }

    private static PackageRoleMemberCallGraphOutcome.Unavailable Unavailable(
        PackageRoleMemberCallGraphUnavailableReason reason,
        string detail) =>
        new(new(reason, detail));
}
