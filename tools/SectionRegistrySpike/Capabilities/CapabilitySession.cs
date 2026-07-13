namespace SectionRegistrySpike.Capabilities;

/// <summary>
/// A per-run execution session. Memoizes each capability instance (created and executed at most
/// once per session) and records an ordered trace of "create"/"execute" events plus creation and
/// execution counters, so laziness and de-duplication are measured rather than inferred.
/// </summary>
/// <typeparam name="TContext">The execution context type shared by all capabilities in this session.</typeparam>
public sealed class CapabilitySession<TContext>
{
    private readonly CapabilityRegistry<TContext> _registry;
    private readonly Dictionary<CapabilityKey, ICapabilityWork<TContext>> _instances = [];
    private readonly HashSet<CapabilityKey> _executed = [];
    private readonly List<string> _trace = [];

    public CapabilitySession(CapabilityRegistry<TContext> registry) => _registry = registry;

    public int CreatedCount { get; private set; }

    public int ExecutedCount { get; private set; }

    /// <summary>Ordered "create"/"execute" trace, one pair of entries per capability that ran in this session.</summary>
    public IReadOnlyList<string> Trace => _trace;

    /// <summary>
    /// Executes every capability in <paramref name="plan"/> that has not already run in this
    /// session, in the given (topologically sorted) order. Safe to call multiple times with
    /// overlapping plans — already-executed capabilities are skipped, which is how two selected
    /// sections that share a dependency end up creating/executing it exactly once.
    /// </summary>
    public async ValueTask ExecutePlanAsync(IReadOnlyList<CapabilityKey> plan, TContext context)
    {
        foreach (var key in plan)
        {
            if (_executed.Contains(key))
                continue;

            var registration = _registry.GetRegistration(key);
            if (!_instances.TryGetValue(key, out var instance))
            {
                instance = registration.Factory();
                _instances.Add(key, instance);
                CreatedCount++;
                _trace.Add($"create {registration.Name}");
            }

            await instance.ExecuteAsync(context, this);
            _executed.Add(key);
            ExecutedCount++;
            _trace.Add($"execute {registration.Name}");
        }
    }

    /// <summary>
    /// Returns the already-executed instance of <typeparamref name="TCapability"/>. Dependents call
    /// this from <see cref="ICapability{TContext}.ExecuteAsync"/> to read a dependency's result;
    /// topological ordering guarantees the dependency has already run in this session.
    /// </summary>
    public TCapability GetExecuted<TCapability>() where TCapability : ICapability<TContext>
    {
        var key = CapabilityKey.Of<TCapability>();
        if (_executed.Contains(key) && _instances.TryGetValue(key, out var instance))
            return (TCapability)instance;

        throw new InvalidOperationException(
            $"Capability '{typeof(TCapability).Name}' has not executed in this session. " +
            "Declare it in DependsOn so the plan orders it before this capability.");
    }

    /// <summary>Whether the given capability has already been created/executed in this session.</summary>
    public bool HasExecuted<TCapability>() where TCapability : ICapability<TContext>
        => _executed.Contains(CapabilityKey.Of<TCapability>());
}
