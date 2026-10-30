using System.Collections.Immutable;
using DotnetInspector.SourceSelection;

namespace DotnetInspector.Queries;

public enum WorkspaceAssemblyDeclarationContextAdmissionRejection
{
    ForeignWorkspace,
    WorkspaceUnavailable,
    MemberCountMismatch,
    MemberOrderMismatch,
    DeclarationMismatch,
}

public abstract record WorkspaceAssemblyDeclarationContextAdmissionOutcome
{
    private WorkspaceAssemblyDeclarationContextAdmissionOutcome()
    {
    }

    public sealed record Admitted(WorkspaceDeclarationContext Context)
        : WorkspaceAssemblyDeclarationContextAdmissionOutcome;

    public sealed record Rejected(
        WorkspaceAssemblyDeclarationContextAdmissionRejection Reason)
        : WorkspaceAssemblyDeclarationContextAdmissionOutcome;
}

public sealed class WorkspaceAssemblyDeclarationContextMember
{
    public WorkspaceAssemblyDeclarationContextMember(
        AssemblyContextParticipant participant,
        ExactLibrarySourceCoordinate coordinate,
        WorkspaceMemberCoordinate declared,
        RealizedMemberCoordinate realized,
        FindPackageSourceRequest? packageRequest)
    {
        ArgumentNullException.ThrowIfNull(participant);
        ArgumentNullException.ThrowIfNull(coordinate);
        ArgumentNullException.ThrowIfNull(declared);
        ArgumentNullException.ThrowIfNull(realized);

        Participant = participant;
        Coordinate = coordinate;
        Declared = declared;
        Realized = realized;
        PackageRequest = packageRequest;
    }

    public AssemblyContextParticipant Participant { get; }
    public ExactLibrarySourceCoordinate Coordinate { get; }
    public WorkspaceMemberCoordinate Declared { get; }
    public RealizedMemberCoordinate Realized { get; }
    public FindPackageSourceRequest? PackageRequest { get; }
}

/// <summary>
/// Publishes one exact declaration context over a Workspace-owned live assembly
/// group without reacquiring its participants.
/// </summary>
public static class WorkspaceAssemblyDeclarationContextAdmission
{
    public static WorkspaceAssemblyDeclarationContextAdmissionOutcome Admit(
        InspectionWorkspace workspace,
        AssemblyContextGroup group,
        WorkspaceContextInput input,
        IReadOnlyList<WorkspaceAssemblyDeclarationContextMember> members)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(group);
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(members);

        if (!workspace.ContainsAssemblyContextGroup(group))
        {
            return new WorkspaceAssemblyDeclarationContextAdmissionOutcome
                .Rejected(
                    WorkspaceAssemblyDeclarationContextAdmissionRejection
                        .ForeignWorkspace);
        }
        if (members.Count != group.Participants.Length
            || members.Count != input.Members.Count)
        {
            return new WorkspaceAssemblyDeclarationContextAdmissionOutcome
                .Rejected(
                    WorkspaceAssemblyDeclarationContextAdmissionRejection
                        .MemberCountMismatch);
        }

        int order;
        try
        {
            order = workspace.BeginDeclarationContext();
        }
        catch (ObjectDisposedException)
        {
            return new WorkspaceAssemblyDeclarationContextAdmissionOutcome
                .Rejected(
                    WorkspaceAssemblyDeclarationContextAdmissionRejection
                        .WorkspaceUnavailable);
        }

        var contextMembers =
            ImmutableArray.CreateBuilder<WorkspaceContextMember>(
                members.Count);
        var declarations =
            ImmutableArray.CreateBuilder<WorkspaceDeclarationMember>(
                members.Count);
        var availablePlatformAssemblies =
            ImmutableArray.CreateBuilder<RealizedMemberCoordinate.Platform>();
        for (int index = 0; index < members.Count; index++)
        {
            WorkspaceAssemblyDeclarationContextMember member = members[index];
            if (!ReferenceEquals(
                    group.Participants[index],
                    member.Participant))
            {
                return new WorkspaceAssemblyDeclarationContextAdmissionOutcome
                    .Rejected(
                        WorkspaceAssemblyDeclarationContextAdmissionRejection
                            .MemberOrderMismatch);
            }
            if (!Equals(input.Members[index], member.Declared))
            {
                return new WorkspaceAssemblyDeclarationContextAdmissionOutcome
                    .Rejected(
                        WorkspaceAssemblyDeclarationContextAdmissionRejection
                            .DeclarationMismatch);
            }

            contextMembers.Add(
                new(
                    member.Declared,
                    member.Realized,
                    member.Participant));
            declarations.Add(
                new(
                    new(workspace.Identity, order, index),
                    member.Coordinate,
                    member.Participant.Assembly.Identity,
                    new WorkspaceDeclarationOrigin.ContextLoad(
                        member.Declared,
                        member.Realized),
                    member.Participant.Assembly.Provenance,
                    member.PackageRequest));
            if (member.Realized
                is RealizedMemberCoordinate.Platform platform)
            {
                availablePlatformAssemblies.Add(platform);
            }
        }

        var loaded =
            new WorkspaceContextLoadOutcome.Loaded(
                workspace.Identity,
                group,
                contextMembers.MoveToImmutable(),
                availablePlatformAssemblies.ToImmutable(),
                input.Framework,
                input.RuntimeIdentifier);
        var context =
            new WorkspaceDeclarationContext(
                new(
                    workspace.Identity,
                    order,
                    new WorkspaceDeclarationRequest.ContextLoad(input),
                    isRealized: true,
                    declarations.MoveToImmutable(),
                    []),
                loaded);
        try
        {
            return new WorkspaceAssemblyDeclarationContextAdmissionOutcome
                .Admitted(workspace.PublishDeclarationContext(context));
        }
        catch (ObjectDisposedException)
        {
            return new WorkspaceAssemblyDeclarationContextAdmissionOutcome
                .Rejected(
                    WorkspaceAssemblyDeclarationContextAdmissionRejection
                        .WorkspaceUnavailable);
        }
    }
}
