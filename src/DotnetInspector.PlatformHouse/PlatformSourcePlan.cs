namespace DotnetInspector.PlatformHouse;

/// <summary>The platform-source facet authorized by one plan selection.</summary>
public enum PlatformSourceFacet
{
    TargetDiscovery,
    Reference,
    Implementation,
    CompiledXml,
    SourceDerivedDocumentation,
}

/// <summary>How authorized capabilities participate in one source facet.</summary>
public enum PlatformSourceSelectionMode
{
    Precedence,
    Fallback,
    Aggregation,
}

/// <summary>An explicit source order and settlement mode for one facet.</summary>
public sealed class PlatformSourceSelection
{
    public PlatformSourceSelection(
        PlatformSourceFacet facet,
        PlatformSourceSelectionMode mode,
        IEnumerable<PlatformSourceCapabilityIdentity> capabilities)
    {
        if (!Enum.IsDefined(facet))
            throw new ArgumentOutOfRangeException(nameof(facet));
        if (!Enum.IsDefined(mode))
            throw new ArgumentOutOfRangeException(nameof(mode));
        ArgumentNullException.ThrowIfNull(capabilities);

        PlatformSourceCapabilityIdentity[] snapshot = [.. capabilities];
        if (snapshot.Length == 0)
        {
            throw new ArgumentException(
                "A source selection requires at least one capability.",
                nameof(capabilities));
        }

        var seen = new HashSet<PlatformSourceCapabilityIdentity>(
            ReferenceEqualityComparer.Instance);
        for (int index = 0; index < snapshot.Length; index++)
        {
            PlatformSourceCapabilityIdentity capability = snapshot[index];
            ArgumentNullException.ThrowIfNull(capability, nameof(capabilities));
            if (!seen.Add(capability))
            {
                throw new ArgumentException(
                    $"Capability at position {index} appears more than once in the source selection.",
                    nameof(capabilities));
            }
        }

        Facet = facet;
        Mode = mode;
        Capabilities = Array.AsReadOnly(snapshot);
    }

    public PlatformSourceFacet Facet { get; }
    public PlatformSourceSelectionMode Mode { get; }
    public IReadOnlyList<PlatformSourceCapabilityIdentity> Capabilities { get; }
}

/// <summary>
/// Immutable, resource-free authorization for the exact PlatformHouse operation.
/// </summary>
public sealed class PlatformSourcePlan
{
    public PlatformSourcePlan(
        PlatformSourcePlanIdentity identity,
        PlatformSourcePolicyGeneration generation,
        IEnumerable<PlatformSourceSelection> selections)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(generation);
        ArgumentNullException.ThrowIfNull(selections);

        PlatformSourceSelection[] snapshot = [.. selections];
        var seen = new HashSet<PlatformSourceFacet>();
        for (int index = 0; index < snapshot.Length; index++)
        {
            PlatformSourceSelection selection = snapshot[index];
            ArgumentNullException.ThrowIfNull(selection, nameof(selections));
            if (!seen.Add(selection.Facet))
            {
                throw new ArgumentException(
                    $"Source facet {selection.Facet} appears more than once in the plan.",
                    nameof(selections));
            }
        }

        Identity = identity;
        Generation = generation;
        Selections = Array.AsReadOnly(snapshot);
    }

    public PlatformSourcePlanIdentity Identity { get; }
    public PlatformSourcePolicyGeneration Generation { get; }
    public IReadOnlyList<PlatformSourceSelection> Selections { get; }

    public PlatformSourceSelection? SelectionFor(PlatformSourceFacet facet)
    {
        if (!Enum.IsDefined(facet))
            throw new ArgumentOutOfRangeException(nameof(facet));

        return Selections.FirstOrDefault(selection => selection.Facet == facet);
    }

    public bool Authorizes(
        PlatformSourceFacet facet,
        PlatformSourceCapabilityIdentity capability)
    {
        ArgumentNullException.ThrowIfNull(capability);
        PlatformSourceSelection? selection = SelectionFor(facet);
        return selection is not null
            && selection.Capabilities.Any(
                candidate => ReferenceEquals(candidate, capability));
    }
}
