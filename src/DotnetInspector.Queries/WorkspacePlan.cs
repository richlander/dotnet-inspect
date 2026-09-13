using System.Collections.Immutable;
using DotnetInspector.SourceSelection;

namespace DotnetInspector.Queries;

/// <summary>Complete, resource-free registration data for constructing a live Workspace.</summary>
public sealed class WorkspacePlan
{
    public WorkspacePlan() : this([]) { }

    public WorkspacePlan(ImmutableArray<WorkspaceRegistration> registrations)
    {
        if (ValidateRegistrations(registrations) is { } invalid)
            throw new ArgumentException(
                $"The Workspace plan registration set is invalid ({invalid}).",
                nameof(registrations));
        Registrations = registrations;
    }

    public static WorkspacePlan Empty { get; } = new();

    public ImmutableArray<WorkspaceRegistration> Registrations { get; }

    internal static WorkspaceRegistrationRejection? ValidateRegistrations(
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
