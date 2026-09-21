using System.Collections.Immutable;

namespace DotnetInspector.Queries;

public sealed partial class InspectionWorkspace
{
    WorkspaceRegistrationRevision _registrationRevision;

    /// <summary>Reads the complete registration revision without realizing any population.</summary>
    public WorkspaceRegistrationReadResult GetRegistrationSnapshot()
    {
        lock (_gate)
        {
            if (RootWorkspaceFailure(_identity) is { } unavailable)
                return new WorkspaceRegistrationReadResult.Unavailable(_registrationRevision, unavailable);
            return new WorkspaceRegistrationReadResult.Available(_registrationRevision);
        }
    }

    /// <summary>
    /// Replaces the complete inert registration set against one exact current
    /// revision. This operation is synchronous and does not acquire resources.
    /// </summary>
    public WorkspaceRegistrationOperationResult ReplaceRegistrations(
        WorkspaceRegistrationRevision expectedRevision,
        ImmutableArray<WorkspaceRegistration> registrations)
    {
        lock (_gate)
        {
            if (RootWorkspaceFailure(_identity) is { } unavailable)
                return new WorkspaceRegistrationOperationResult.Unavailable(_registrationRevision, unavailable);
            if (expectedRevision is null)
                return new WorkspaceRegistrationOperationResult.Rejected(
                    _registrationRevision, WorkspaceRegistrationRejection.Malformed);
            if (!ReferenceEquals(expectedRevision.Workspace, _identity))
                return new WorkspaceRegistrationOperationResult.Rejected(
                    _registrationRevision, WorkspaceRegistrationRejection.ForeignWorkspace);
            if (!ReferenceEquals(expectedRevision.Identity, _registrationRevision.Identity))
                return new WorkspaceRegistrationOperationResult.Rejected(
                    _registrationRevision, WorkspaceRegistrationRejection.RevisionMismatch);
            if (WorkspacePlan.ValidateRegistrations(registrations) is { } invalid)
                return new WorkspaceRegistrationOperationResult.Rejected(_registrationRevision, invalid);
            if (registrations.SequenceEqual(_registrationRevision.Registrations))
                return new WorkspaceRegistrationOperationResult.NoEffect(_registrationRevision);

            _registrationRevision = new(
                _identity,
                _registrationRevision.Plan.WithRegistrations(registrations),
                _registrationRevision);
            return new WorkspaceRegistrationOperationResult.Committed(_registrationRevision);
        }
    }
}
