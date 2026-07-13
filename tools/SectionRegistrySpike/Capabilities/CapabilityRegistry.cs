namespace SectionRegistrySpike.Capabilities;

/// <summary>
/// Registry of capability types keyed by <see cref="CapabilityKey"/>. Registration captures static
/// executors into immutable plans; execution does not construct capability objects.
/// </summary>
/// <typeparam name="TContext">The execution context type shared by all capabilities in this registry.</typeparam>
public sealed class CapabilityRegistry<TContext>
{
    internal delegate ValueTask CapabilityExecutor(TContext context);

    internal sealed record Registration(
        CapabilityKey Key,
        string Name,
        CapabilityExecutionModes AllowedModes,
        CapabilityKey[] DependsOn,
        CapabilityExecutor Execute);

    private readonly Dictionary<CapabilityKey, Registration> _registrations = [];
    private readonly CapabilityPlan<TContext> _emptyPlan = new([]);

    public CapabilityRegistry<TContext> Register<TCapability>()
        where TCapability : ICapability<TContext>
    {
        var key = CapabilityKey.Of<TCapability>();
        var registration = new Registration(
            key,
            TCapability.Name,
            TCapability.AllowedModes,
            [.. TCapability.DependsOn],
            static context => TCapability.ExecuteAsync(context));

        if (!_registrations.TryAdd(key, registration))
            throw new InvalidOperationException($"Capability '{TCapability.Name}' is already registered.");

        return this;
    }

    private Registration GetRegistration(CapabilityKey key)
    {
        if (_registrations.TryGetValue(key, out var registration))
            return registration;

        throw new CapabilityNotRegisteredException(
            $"Capability '{key}' is not registered. Register it before it can appear as a " +
            "dependency or plan target.");
    }

    /// <summary>
    /// Compiles requested capabilities into a deterministic, deduplicated plan with dependencies
    /// before dependents.
    /// </summary>
    public CapabilityPlan<TContext> ResolvePlan(IReadOnlyList<CapabilityKey> requested)
    {
        if (requested.Count == 0)
            return _emptyPlan;

        List<Registration> order = [];
        HashSet<CapabilityKey> visited = [];
        HashSet<CapabilityKey> visiting = [];
        List<CapabilityKey> path = [];

        foreach (var key in requested)
            Visit(key);

        return new CapabilityPlan<TContext>([.. order]);

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
            order.Add(registration);
        }
    }
}
