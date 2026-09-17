using System.Collections.Immutable;
using DotnetInspector.SourceSelection;

namespace DotnetInspector.Queries;

/// <summary>Complete, resource-free intent for constructing a live Workspace.</summary>
public sealed class WorkspacePlan
{
    public WorkspacePlan()
        : this(
            WorkspaceRootTargetFramework.Default,
            [],
            Array.Empty<WorkspaceContextInput>())
    {
    }

    public WorkspacePlan(ImmutableArray<WorkspaceRegistration> registrations)
        : this(
            WorkspaceRootTargetFramework.Default,
            registrations,
            Array.Empty<WorkspaceContextInput>())
    {
    }

    public WorkspacePlan(
        WorkspaceRootTargetFramework rootTargetFramework)
        : this(
            rootTargetFramework,
            [],
            Array.Empty<WorkspaceContextInput>())
    {
    }

    public WorkspacePlan(
        WorkspaceRootTargetFramework rootTargetFramework,
        ImmutableArray<WorkspaceRegistration> registrations)
        : this(
            rootTargetFramework,
            registrations,
            Array.Empty<WorkspaceContextInput>())
    {
    }

    public WorkspacePlan(
        ImmutableArray<WorkspaceRegistration> registrations,
        IReadOnlyList<WorkspaceContextInput> contexts)
        : this(
            WorkspaceRootTargetFramework.Default,
            registrations,
            contexts)
    {
    }

    public WorkspacePlan(
        WorkspaceRootTargetFramework rootTargetFramework,
        ImmutableArray<WorkspaceRegistration> registrations,
        IReadOnlyList<WorkspaceContextInput> contexts)
    {
        ArgumentNullException.ThrowIfNull(rootTargetFramework);
        if (ValidateRegistrations(registrations) is { } invalid)
            throw new ArgumentException(
                $"The Workspace plan registration set is invalid ({invalid}).",
                nameof(registrations));
        ArgumentNullException.ThrowIfNull(contexts);

        RootTargetFramework = rootTargetFramework;
        Registrations = registrations;
        Contexts = SnapshotContexts(contexts);
    }

    public static WorkspacePlan Empty { get; } = new();

    public WorkspaceRootTargetFramework RootTargetFramework { get; }

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
            RootTargetFramework,
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
        WorkspaceRootTargetFramework rootTargetFramework,
        ImmutableArray<WorkspaceRegistration> registrations,
        ImmutableArray<WorkspaceContextInput> contexts)
    {
        RootTargetFramework = rootTargetFramework;
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
