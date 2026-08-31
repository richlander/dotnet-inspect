using System.Collections.Immutable;

using DotnetInspector.Packages;
using DotnetInspector.Queries;

namespace DotnetInspector.PackageQueries;

/// <summary>
/// Package and asset identity for one library in an Integration roll-up.
/// </summary>
public sealed record PackageWorkspaceIntegrationsSubject(
    PackageRootIdentity Package,
    PackageCompileAsset Asset)
{
    public string PackageId => Package.PackageId;

    public string PackageVersion => Package.PackageVersion;
}

/// <summary>
/// One package library's Integration outcome in a realized package workspace.
/// </summary>
public sealed record PackageWorkspaceIntegrationsEntry(
    PackageWorkspaceIntegrationsSubject Subject,
    AssemblyIntegrationsEntry Integrations);

/// <summary>
/// Ordered Integration outcomes for every inspected library in a realized
/// package workspace.
/// </summary>
public sealed record PackageWorkspaceIntegrationsResult(
    ImmutableArray<PackageWorkspaceIntegrationsEntry> Libraries)
{
    public bool IsComplete =>
        Libraries.All(static entry =>
            entry.Integrations is AssemblyIntegrationsEntry.Available);
}

/// <summary>
/// Projects Integration evidence across the product-selected package workspace
/// roles without flattening their binding contexts.
/// </summary>
/// <remarks>
/// Implementation assets are inspected in product order. Surface assets
/// without an implementation correspondence follow in surface order, so
/// reference-only libraries remain visible without scanning a reference and
/// implementation image for the same library. Gated by
/// <c>PackageWorkspaceIntegrationsQuery_UsesImplementationRoleAndReferenceFallback</c>
/// and
/// <c>PackageWorkspaceIntegrationsQuery_SharedRoleDoesNotDuplicateLibraries</c>.
/// </remarks>
public static class PackageWorkspaceIntegrationsQuery
{
    public static InspectionQuery<PackageWorkspaceIntegrationsResult>
        Definition { get; } =
        new("Package workspace integrations", InspectionCost.Unbounded);

    public static PackageWorkspaceIntegrationsResult Execute(
        PackageAssemblyContextRealization realization)
    {
        ArgumentNullException.ThrowIfNull(realization);
        if (!realization.HasAssemblyContexts)
        {
            throw new InvalidOperationException(
                "Package workspace integration analysis requires at least one "
                + "selected compile asset.");
        }

        var entries =
            ImmutableArray.CreateBuilder<PackageWorkspaceIntegrationsEntry>();
        if (realization.ImplementationGroup is not null)
        {
            AppendGroup(
                entries,
                realization.ImplementationGroup,
                realization.ImplementationParticipants);
            foreach (PackageAssemblyRoleParticipant surface
                in realization.SurfaceParticipants)
            {
                if (realization.ImplementationParticipant(surface) is not null)
                    continue;

                entries.Add(
                    new PackageWorkspaceIntegrationsEntry(
                        Subject(surface),
                        AssemblyContextIntegrationsQuery.ExecuteParticipant(
                            realization.SurfaceGroup,
                            surface.Participant)));
            }
        }
        else
        {
            AppendGroup(
                entries,
                realization.SurfaceGroup,
                realization.SurfaceParticipants);
        }

        return new PackageWorkspaceIntegrationsResult(
            entries.ToImmutable());
    }

    public static PackageWorkspaceIntegrationsResult Execute(
        PackageAssemblyContextProjection projection)
    {
        ArgumentNullException.ThrowIfNull(projection);
        return projection.Use(() => ExecuteProjection(projection));
    }

    static PackageWorkspaceIntegrationsResult ExecuteProjection(
        PackageAssemblyContextProjection projection)
    {
        var entries =
            ImmutableArray.CreateBuilder<PackageWorkspaceIntegrationsEntry>();
        if (projection.ImplementationRole is not null)
        {
            AppendRole(
                entries,
                projection.ImplementationRole);
            foreach (PackageAssemblyRoleParticipant surface
                in projection.SurfaceParticipants)
            {
                if (projection.ImplementationParticipant(surface) is not null)
                    continue;

                entries.Add(
                    new PackageWorkspaceIntegrationsEntry(
                        Subject(surface),
                        AssemblyContextIntegrationsQuery.ExecuteParticipant(
                            projection.SurfaceRole,
                            surface)));
            }
        }
        else
        {
            AppendRole(
                entries,
                projection.SurfaceRole);
        }

        return new PackageWorkspaceIntegrationsResult(
            entries.ToImmutable());
    }

    static void AppendGroup(
        ImmutableArray<PackageWorkspaceIntegrationsEntry>.Builder entries,
        AssemblyContextGroup group,
        ImmutableArray<PackageAssemblyRoleParticipant> participants)
    {
        AssemblyContextIntegrationsResult result =
            AssemblyContextIntegrationsQuery.Execute(group);
        foreach (AssemblyIntegrationsEntry integration in result.Assemblies)
        {
            PackageAssemblyRoleParticipant participant =
                participants.FirstOrDefault(candidate =>
                    ReferenceEquals(
                        candidate.Participant.Assembly.Registration,
                        integration.Subject.Registration))
                ?? throw new InvalidOperationException(
                    "The Integration query returned a participant outside the package workspace role.");
            entries.Add(
                new PackageWorkspaceIntegrationsEntry(
                    Subject(participant),
                    integration));
        }
    }

    static void AppendRole(
        ImmutableArray<PackageWorkspaceIntegrationsEntry>.Builder entries,
        PackageAssemblyContextRoleProjection role)
    {
        AssemblyContextIntegrationsResult result =
            AssemblyContextIntegrationsQuery.Execute(role);
        foreach (AssemblyIntegrationsEntry integration in result.Assemblies)
        {
            PackageAssemblyRoleParticipant participant =
                role.Participants.FirstOrDefault(candidate =>
                    ReferenceEquals(
                        candidate.Participant.Assembly.Registration,
                        integration.Subject.Registration))
                ?? throw new InvalidOperationException(
                    "The Integration query returned a participant outside the package workspace role.");
            entries.Add(
                new PackageWorkspaceIntegrationsEntry(
                    Subject(participant),
                    integration));
        }
    }

    static PackageWorkspaceIntegrationsSubject Subject(
        PackageAssemblyRoleParticipant participant) =>
        new(
            participant.Package,
            participant.Asset);
}
