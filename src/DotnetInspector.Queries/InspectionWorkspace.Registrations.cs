using System.Collections.Immutable;
using DotnetInspector.SourceSelection;

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
    /// revision. This in-memory operation supports both Workspace lifetime modes.
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
            if (ValidateRegistrations(registrations) is { } invalid)
                return new WorkspaceRegistrationOperationResult.Rejected(_registrationRevision, invalid);
            if (registrations.SequenceEqual(_registrationRevision.Registrations))
                return new WorkspaceRegistrationOperationResult.NoEffect(_registrationRevision);

            _registrationRevision = new(_identity, registrations);
            return new WorkspaceRegistrationOperationResult.Committed(_registrationRevision);
        }
    }

    static ImmutableArray<WorkspaceRegistration> ValidateInitialRegistrations(
        ImmutableArray<WorkspaceRegistration> registrations)
    {
        if (ValidateRegistrations(registrations) is { } invalid)
            throw new ArgumentException(
                $"The initial Workspace registration set is invalid ({invalid}).",
                nameof(registrations));
        return registrations;
    }

    static WorkspaceRegistrationRejection? ValidateRegistrations(
        ImmutableArray<WorkspaceRegistration> registrations)
    {
        if (registrations.IsDefault)
            return WorkspaceRegistrationRejection.Malformed;

        var libraries = new HashSet<ExactLibrarySourceCoordinate>();
        var prefixes = new HashSet<PackagePrefixDeclaration>();
        var ecosystems = new HashSet<WorkspaceEcosystemRegistrationId>();
        foreach (WorkspaceRegistration registration in registrations)
        {
            if (registration is null)
                return WorkspaceRegistrationRejection.Malformed;
            bool unique = registration switch
            {
                WorkspaceRegistration.ExactLibrary library => libraries.Add(library.Coordinate),
                WorkspaceRegistration.PackagePrefix prefix => prefixes.Add(prefix.Prefix),
                WorkspaceRegistration.Ecosystem ecosystem => ecosystems.Add(ecosystem.Declaration.Id),
                _ => throw new InvalidOperationException("Unknown Workspace registration arm."),
            };
            if (!unique)
                return WorkspaceRegistrationRejection.DuplicateIdentity;
        }
        return null;
    }
}
