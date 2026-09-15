using System.Collections.Immutable;
using DotnetInspector.Queries;

namespace DotnetInspector.Ecosystems;

/// <summary>The result of selecting one retained lower Workspace declaration.</summary>
public abstract record EcosystemWorkspaceRegistrationSelectionResult
{
    private protected EcosystemWorkspaceRegistrationSelectionResult()
    {
    }

    public sealed record Known : EcosystemWorkspaceRegistrationSelectionResult
    {
        internal Known(WorkspaceEcosystemRegistrationDeclaration declaration) =>
            Declaration = declaration;

        public WorkspaceEcosystemRegistrationDeclaration Declaration { get; }
    }

    /// <summary>The pack is known but has no Workspace projection.</summary>
    public sealed record Unavailable : EcosystemWorkspaceRegistrationSelectionResult
    {
        internal Unavailable(EcosystemPackId id) => Id = id;

        public EcosystemPackId Id { get; }
    }

    public sealed record Unknown : EcosystemWorkspaceRegistrationSelectionResult
    {
        internal Unknown(EcosystemPackId id) => Id = id;

        public EcosystemPackId Id { get; }
    }
}

public static partial class EcosystemPackCatalog
{
    public static EcosystemWorkspaceRegistrationSelectionResult SelectWorkspaceRegistration(
        EcosystemPackId id) =>
        ProductEcosystemPacks.Registry.SelectWorkspaceRegistration(id);

    /// <summary>Returns a resource-free plan registering every shipped ecosystem.</summary>
    public static WorkspacePlan CreateWorkspacePlan() =>
        ProductEcosystemPacks.AllKnownWorkspacePlan;

    /// <summary>
    /// Returns a resource-free plan registering exactly the selected ecosystems
    /// in caller order.
    /// </summary>
    public static WorkspacePlan CreateWorkspacePlan(
        IEnumerable<EcosystemPackId> ecosystems) =>
        EcosystemWorkspacePlanFactory.Create(
            ProductEcosystemPacks.Registry,
            ecosystems);

    /// <summary>Returns a resource-free plan with platform-curated registrations.</summary>
    public static WorkspacePlan CreatePlatformWorkspacePlan() =>
        ProductEcosystemPacks.PlatformWorkspacePlan;
}

internal static class EcosystemWorkspacePlanFactory
{
    internal static WorkspacePlan Create(
        EcosystemPackRegistry registry,
        IEnumerable<EcosystemPackId> manifest,
        bool requireAllPacks = false)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(manifest);
        EcosystemPackId[] entries = [.. manifest];
        if (entries.Length == 0)
        {
            throw new ArgumentException(
                "An ecosystem Workspace manifest must contain at least one pack.",
                nameof(manifest));
        }

        var identities = new HashSet<EcosystemPackId>();
        var registrations = ImmutableArray.CreateBuilder<WorkspaceRegistration>(entries.Length);
        foreach (EcosystemPackId id in entries)
        {
            if (id is null || !identities.Add(id))
            {
                throw new ArgumentException(
                    "An ecosystem Workspace manifest cannot contain null or duplicate pack identities.",
                    nameof(manifest));
            }

            switch (registry.SelectWorkspaceRegistration(id))
            {
                case EcosystemWorkspaceRegistrationSelectionResult.Known known:
                    if (known.Declaration.CorePackages.IsEmpty
                        && known.Declaration.Populations.IsEmpty)
                    {
                        throw new ArgumentException(
                            $"Ecosystem pack '{id}' has no registered package"
                            + " or Workspace population contribution.",
                            nameof(manifest));
                    }

                    registrations.Add(new WorkspaceRegistration.Ecosystem(known.Declaration));
                    break;
                case EcosystemWorkspaceRegistrationSelectionResult.Unavailable:
                    throw new ArgumentException(
                        $"Ecosystem pack '{id}' has no Workspace projection.",
                        nameof(manifest));
                case EcosystemWorkspaceRegistrationSelectionResult.Unknown:
                    throw new ArgumentException(
                        $"Workspace manifest names unknown ecosystem pack '{id}'.",
                        nameof(manifest));
                default:
                    throw new InvalidOperationException("Unexpected Workspace projection outcome.");
            }
        }

        if (requireAllPacks && identities.Count != registry.Packs.Length)
        {
            throw new ArgumentException(
                "An all-known Workspace manifest must include every registered ecosystem pack.",
                nameof(manifest));
        }

        return new WorkspacePlan(registrations.MoveToImmutable());
    }
}
