using System.Collections.Immutable;
using DotnetInspector.SourceSelection;

namespace DotnetInspector.Queries;

/// <summary>Complete, resource-free intent for constructing a live Workspace.</summary>
public sealed class WorkspacePlan
{
    public WorkspacePlan()
        : this(
            WorkspaceTargetFrameworkPolicy.ProductDefault,
            [],
            Array.Empty<WorkspaceContextInput>())
    {
    }

    public WorkspacePlan(ImmutableArray<WorkspaceRegistration> registrations)
        : this(
            WorkspaceTargetFrameworkPolicy.ProductDefault,
            registrations,
            Array.Empty<WorkspaceContextInput>())
    {
    }

    public WorkspacePlan(
        WorkspaceTargetFrameworkPolicy targetFrameworkPolicy)
        : this(
            targetFrameworkPolicy,
            [],
            Array.Empty<WorkspaceContextInput>())
    {
    }

    public WorkspacePlan(
        WorkspaceTargetFrameworkPolicy targetFrameworkPolicy,
        ImmutableArray<WorkspaceRegistration> registrations)
        : this(
            targetFrameworkPolicy,
            registrations,
            Array.Empty<WorkspaceContextInput>())
    {
    }

    public WorkspacePlan(
        ImmutableArray<WorkspaceRegistration> registrations,
        IReadOnlyList<WorkspaceContextInput> contexts)
        : this(
            WorkspaceTargetFrameworkPolicy.ProductDefault,
            registrations,
            contexts)
    {
    }

    public WorkspacePlan(
        WorkspaceTargetFrameworkPolicy targetFrameworkPolicy,
        ImmutableArray<WorkspaceRegistration> registrations,
        IReadOnlyList<WorkspaceContextInput> contexts)
    {
        ArgumentNullException.ThrowIfNull(targetFrameworkPolicy);
        if (ValidateRegistrations(registrations) is { } invalid)
            throw new ArgumentException(
                $"The Workspace plan registration set is invalid ({invalid}).",
                nameof(registrations));
        ArgumentNullException.ThrowIfNull(contexts);

        TargetFrameworkPolicy = targetFrameworkPolicy;
        Registrations = registrations;
        Contexts = SnapshotContexts(contexts);
    }

    public static WorkspacePlan Empty { get; } = new();

    public WorkspaceTargetFrameworkPolicy TargetFrameworkPolicy { get; }

    public ImmutableArray<WorkspaceRegistration> Registrations { get; }

    public ImmutableArray<WorkspaceContextInput> Contexts { get; }

    internal WorkspacePlan WithRegistrations(
        ImmutableArray<WorkspaceRegistration> registrations)
    {
        if (ValidateRegistrations(registrations) is { } invalid)
            throw new ArgumentException(
                $"The Workspace plan registration set is invalid ({invalid}).",
                nameof(registrations));
        return new WorkspacePlan(
            TargetFrameworkPolicy,
            registrations,
            Contexts);
    }

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

    WorkspacePlan(
        WorkspaceTargetFrameworkPolicy targetFrameworkPolicy,
        ImmutableArray<WorkspaceRegistration> registrations,
        ImmutableArray<WorkspaceContextInput> contexts)
    {
        TargetFrameworkPolicy = targetFrameworkPolicy;
        Registrations = registrations;
        Contexts = contexts;
    }

    static ImmutableArray<WorkspaceContextInput> SnapshotContexts(
        IReadOnlyList<WorkspaceContextInput> contexts)
    {
        var snapshots =
            ImmutableArray.CreateBuilder<WorkspaceContextInput>(contexts.Count);
        for (int i = 0; i < contexts.Count; i++)
        {
            WorkspaceContextInput? context = contexts[i];
            if (context is null || context.Members is null)
            {
                throw new ArgumentException(
                    "Workspace plan contexts and their member collections cannot be null.",
                    nameof(contexts));
            }

            snapshots.Add(context with
            {
                Members = context.Members.ToImmutableArray(),
            });
        }
        return snapshots.MoveToImmutable();
    }
}
