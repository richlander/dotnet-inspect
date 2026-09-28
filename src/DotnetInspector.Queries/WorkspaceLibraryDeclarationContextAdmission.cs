using System.Collections.Immutable;
using DotnetInspector.LibraryMetadata;
using DotnetInspector.SourceSelection;
using ILInspector.Metadata;

namespace DotnetInspector.Queries;

public enum WorkspaceLibraryDeclarationContextAdmissionRejection
{
    ForeignWorkspace,
    WorkspaceUnavailable,
    UnknownLibraryAdmission,
    MemberCountMismatch,
}

public abstract record WorkspaceLibraryDeclarationContextAdmissionOutcome
{
    private WorkspaceLibraryDeclarationContextAdmissionOutcome()
    {
    }

    public sealed record Admitted(WorkspaceDeclarationContext Context)
        : WorkspaceLibraryDeclarationContextAdmissionOutcome;

    public sealed record Rejected(
        WorkspaceLibraryDeclarationContextAdmissionRejection Reason)
        : WorkspaceLibraryDeclarationContextAdmissionOutcome;
}

public sealed class WorkspaceLibraryDeclarationContextMember
{
    public WorkspaceLibraryDeclarationContextMember(
        ExactLibrarySourceCoordinate coordinate,
        AssemblyReferenceIdentity assemblyIdentity,
        WorkspaceDeclarationOrigin origin,
        AssemblyResolutionProvenance selection)
    {
        ArgumentNullException.ThrowIfNull(coordinate);
        ArgumentNullException.ThrowIfNull(assemblyIdentity);
        ArgumentNullException.ThrowIfNull(origin);
        ArgumentNullException.ThrowIfNull(selection);

        Coordinate = coordinate;
        AssemblyIdentity = assemblyIdentity;
        Origin = origin;
        Selection = selection;
    }

    public ExactLibrarySourceCoordinate Coordinate { get; }
    public AssemblyReferenceIdentity AssemblyIdentity { get; }
    public WorkspaceDeclarationOrigin Origin { get; }
    public AssemblyResolutionProvenance Selection { get; }
}

/// <summary>
/// Publishes one declaration context over an already-admitted ordered Library
/// batch. Source-specific adapters validate and detach their evidence first.
/// </summary>
public static class WorkspaceLibraryDeclarationContextAdmission
{
    public static WorkspaceLibraryDeclarationContextAdmissionOutcome Admit(
        InspectionWorkspace workspace,
        WorkspaceLibraryAdmissionReceipt admission,
        WorkspaceDeclarationRequest request,
        IReadOnlyList<WorkspaceLibraryDeclarationContextMember> members,
        LibraryTypeDeclarationInventoryInspectionBounds bounds)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(admission);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(members);
        ArgumentNullException.ThrowIfNull(bounds);

        if (!ReferenceEquals(admission.Workspace, workspace.Identity))
        {
            return new WorkspaceLibraryDeclarationContextAdmissionOutcome
                .Rejected(
                    WorkspaceLibraryDeclarationContextAdmissionRejection
                        .ForeignWorkspace);
        }
        if (!workspace.ContainsLibraryAdmission(admission))
        {
            return new WorkspaceLibraryDeclarationContextAdmissionOutcome
                .Rejected(
                    WorkspaceLibraryDeclarationContextAdmissionRejection
                        .UnknownLibraryAdmission);
        }
        if (members.Count != admission.Occurrences.Length)
        {
            return new WorkspaceLibraryDeclarationContextAdmissionOutcome
                .Rejected(
                    WorkspaceLibraryDeclarationContextAdmissionRejection
                        .MemberCountMismatch);
        }

        int order;
        try
        {
            order = workspace.BeginDeclarationContext();
        }
        catch (ObjectDisposedException)
        {
            return new WorkspaceLibraryDeclarationContextAdmissionOutcome
                .Rejected(
                    WorkspaceLibraryDeclarationContextAdmissionRejection
                        .WorkspaceUnavailable);
        }

        var declarations =
            ImmutableArray.CreateBuilder<WorkspaceDeclarationMember>(
                members.Count);
        for (int index = 0; index < members.Count; index++)
        {
            WorkspaceLibraryDeclarationContextMember member = members[index];
            declarations.Add(
                new(
                    new(workspace.Identity, order, index),
                    member.Coordinate,
                    member.AssemblyIdentity,
                    member.Origin,
                    member.Selection));
        }

        WorkspaceDeclarationContext context = new(
            new(
                workspace.Identity,
                order,
                request,
                isRealized: true,
                declarations.MoveToImmutable(),
                []),
            admission.Occurrences,
            bounds);
        try
        {
            return new WorkspaceLibraryDeclarationContextAdmissionOutcome
                .Admitted(workspace.PublishDeclarationContext(context));
        }
        catch (ObjectDisposedException)
        {
            return new WorkspaceLibraryDeclarationContextAdmissionOutcome
                .Rejected(
                    WorkspaceLibraryDeclarationContextAdmissionRejection
                        .WorkspaceUnavailable);
        }
    }
}
