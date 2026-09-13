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

    /// <summary>Creates an independent synchronous Workspace registering every shipped ecosystem.</summary>
    public static InspectionWorkspace CreateWorkspace() =>
        ProductEcosystemPacks.AllKnownWorkspace.Create();

    /// <summary>Creates the all-known Workspace with asynchronous lifetime management.</summary>
    public static InspectionWorkspace CreateWorkspaceAsynchronous() =>
        ProductEcosystemPacks.AllKnownWorkspace.CreateAsynchronous();

    /// <summary>Creates an independent synchronous Workspace with the platform-curated registrations.</summary>
    public static InspectionWorkspace CreatePlatformWorkspace() =>
        ProductEcosystemPacks.PlatformWorkspace.Create();

    /// <summary>Creates the platform-curated Workspace with asynchronous lifetime management.</summary>
    public static InspectionWorkspace CreatePlatformWorkspaceAsynchronous() =>
        ProductEcosystemPacks.PlatformWorkspace.CreateAsynchronous();
}

internal sealed class EcosystemWorkspaceFactory
{
    private readonly ImmutableArray<WorkspaceRegistration> _registrations;

    internal EcosystemWorkspaceFactory(
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
                    if (known.Declaration.Populations.IsEmpty)
                    {
                        throw new ArgumentException(
                            $"Ecosystem pack '{id}' has no Workspace population contribution.",
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

        _registrations = registrations.MoveToImmutable();
    }

    internal InspectionWorkspace Create() => new(_registrations);

    internal InspectionWorkspace CreateAsynchronous() =>
        InspectionWorkspace.CreateAsynchronous(_registrations);
}
