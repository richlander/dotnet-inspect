using System.Collections.Immutable;

using DotnetInspector.Packages;

namespace DotnetInspector.Queries;

/// <summary>
/// Package and asset identity for one library in an Integration roll-up.
/// </summary>
public sealed record PackageWorkspaceIntegrationsSubject(
    string PackageId,
    string PackageVersion,
    PackageCompileAsset Asset);

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

    static PackageWorkspaceIntegrationsSubject Subject(
        PackageAssemblyRoleParticipant participant) =>
        new(
            participant.Package.PackageId,
            participant.Package.PackageVersion,
            participant.Asset);
}
