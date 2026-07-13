namespace SectionRegistrySpike.Capabilities;

/// <summary>
/// Registry of capability types keyed by <see cref="CapabilityKey"/>. Registration is manual and
/// ordered (<see cref="Register{TCapability}"/> per capability type) — there is no assembly
/// scanning. The registry stores a <c>Func&lt;ICapabilityWork{TContext}&gt;</c> factory per capability;
/// instances are created only when a resolved plan is executed by a
/// <see cref="CapabilitySession{TContext}"/>.
/// </summary>
/// <typeparam name="TContext">The execution context type shared by all capabilities in this registry.</typeparam>
public sealed class CapabilityRegistry<TContext>
{
    /// <summary>Declarative metadata plus construction factory for one registered capability.</summary>
    internal sealed record Registration(
        CapabilityKey Key,
        string Name,
        bool SafeToProbe,
        CapabilityKey[] DependsOn,
        Func<ICapabilityWork<TContext>> Factory);

    private readonly Dictionary<CapabilityKey, Registration> _registrations = [];

    /// <summary>Registers a capability type. Throws if the same capability type is registered twice.</summary>
    public CapabilityRegistry<TContext> Register<TCapability>()
        where TCapability : ICapability<TContext>, new()
    {
        var key = CapabilityKey.Of<TCapability>();
        var registration = new Registration(
            key, TCapability.Name, TCapability.SafeToProbe, [.. TCapability.DependsOn],
            static () => new TCapability());

        if (!_registrations.TryAdd(key, registration))
            throw new InvalidOperationException($"Capability '{TCapability.Name}' is already registered.");

        return this;
    }

    internal Registration GetRegistration(CapabilityKey key)
    {
        if (_registrations.TryGetValue(key, out var registration))
            return registration;

        throw new CapabilityNotRegisteredException(
            $"Capability '{key}' is not registered. Register it before it can appear as a " +
            "dependency or plan target.");
    }

    /// <summary>
    /// Resolves <paramref name="requested"/> into a deterministic, deduplicated, topologically
    /// ordered execution plan (dependencies before dependents). Throws
    /// <see cref="CapabilityNotRegisteredException"/> for an unregistered capability and
    /// <see cref="CapabilityCycleException"/> for a dependency cycle.
    /// </summary>
    public IReadOnlyList<CapabilityKey> ResolvePlan(IReadOnlyList<CapabilityKey> requested)
    {
        List<CapabilityKey> order = [];
        HashSet<CapabilityKey> visited = [];
        HashSet<CapabilityKey> visiting = [];
        List<CapabilityKey> path = [];

        foreach (var key in requested)
            Visit(key);

        return order;

        void Visit(CapabilityKey key)
        {
            if (visited.Contains(key))
                return;

            if (visiting.Contains(key))
            {
                var cycleNames = path.Skip(path.IndexOf(key)).Append(key).Select(k => k.ToString()).ToArray();
                throw new CapabilityCycleException(
                    $"Capability dependency cycle detected: {string.Join(" -> ", cycleNames)}.",
                    cycleNames);
            }

            var registration = GetRegistration(key);

            visiting.Add(key);
            path.Add(key);
            foreach (var dependency in registration.DependsOn)
                Visit(dependency);
            path.RemoveAt(path.Count - 1);
            visiting.Remove(key);

            visited.Add(key);
            order.Add(key);
        }
    }

    /// <summary>
    /// True when every capability in <paramref name="plan"/> is <see cref="ICapability{TContext}.SafeToProbe"/>.
    /// Effective discovery uses this to decide whether a section's whole capability closure —
    /// not just its own top-level flag — may run during probing.
    /// </summary>
    public bool IsClosureSafeToProbe(IReadOnlyList<CapabilityKey> plan)
    {
        foreach (var key in plan)
        {
            if (!GetRegistration(key).SafeToProbe)
                return false;
        }
        return true;
    }
}
