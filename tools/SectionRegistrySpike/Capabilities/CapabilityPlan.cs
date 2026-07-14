namespace SectionRegistrySpike.Capabilities;

public readonly record struct CapabilityPlanEntry<TContext>(
    // Generated IDs are unique topological ordinals used to merge arbitrary selections.
    int Id,
    string Name,
    CapabilityExecutionModes AllowedModes,
    Func<TContext, ValueTask> Execute);

/// <summary>
/// Immutable, dependency-ordered static lambda plan. Authorization is preflighted before any
/// executor runs, so a rejected entry cannot leave earlier work partially applied.
/// </summary>
public readonly struct CapabilityPlan<TContext>
{
    private readonly CapabilityPlanEntry<TContext>[] _entries;
    private readonly CapabilityExecutionModes _allowedModes;

    public CapabilityPlan(params CapabilityPlanEntry<TContext>[] entries)
    {
        for (int current = 0; current < entries.Length; current++)
        {
            for (int prior = 0; prior < current; prior++)
            {
                if (entries[prior].Id == entries[current].Id)
                {
                    throw new InvalidOperationException(
                        $"Capability '{entries[current].Name}' appears more than once in a plan.");
                }
            }
        }

        _entries = entries;
        var allowedModes = CapabilityExecutionModes.All;
        for (int index = 0; index < entries.Length; index++)
            allowedModes &= entries[index].AllowedModes;
        _allowedModes = allowedModes;
    }

    private CapabilityPlanEntry<TContext>[] EntryArray =>
        _entries ?? Array.Empty<CapabilityPlanEntry<TContext>>();

    internal ReadOnlySpan<CapabilityPlanEntry<TContext>> Entries => EntryArray;

    public int Count => EntryArray.Length;

    public bool CanExecute(CapabilityExecutionModes mode)
        => IsSingleMode(mode)
            && (_entries is null || (_allowedModes & mode) != 0);

    public bool HasSameEntries(CapabilityPlan<TContext> other)
    {
        var entries = EntryArray;
        var otherEntries = other.EntryArray;
        if (entries.Length != otherEntries.Length)
            return false;
        for (int index = 0; index < entries.Length; index++)
        {
            if (entries[index].Id != otherEntries[index].Id)
                return false;
        }
        return true;
    }

    public ValueTask ExecuteAsync(
        TContext context,
        CapabilityExecutionModes mode,
        Action<string>? trace = null)
    {
        if (!IsSingleMode(mode))
            throw new ArgumentOutOfRangeException(nameof(mode), mode, "Select one execution strategy.");

        var entries = EntryArray;
        if (entries.Length > 0 && (_allowedModes & mode) == 0)
        {
            foreach (var entry in entries)
            {
                if ((entry.AllowedModes & mode) == 0)
                {
                    throw new CapabilityNotAuthorizedException(
                        $"Capability '{entry.Name}' is not authorized for the '{mode}' strategy.");
                }
            }
        }

        for (int index = 0; index < entries.Length; index++)
        {
            var entry = entries[index];
            var execution = entry.Execute(context);
            if (!execution.IsCompletedSuccessfully)
                return AwaitRemainingAsync(execution, index, context, trace);
            execution.GetAwaiter().GetResult();
            trace?.Invoke($"execute {entry.Name}");
        }

        return ValueTask.CompletedTask;
    }

    private async ValueTask AwaitRemainingAsync(
        ValueTask execution,
        int currentIndex,
        TContext context,
        Action<string>? trace)
    {
        await execution.ConfigureAwait(false);
        var entries = EntryArray;
        trace?.Invoke($"execute {entries[currentIndex].Name}");

        for (int index = currentIndex + 1; index < entries.Length; index++)
        {
            var entry = entries[index];
            await entry.Execute(context).ConfigureAwait(false);
            trace?.Invoke($"execute {entry.Name}");
        }
    }

    private static bool IsSingleMode(CapabilityExecutionModes mode)
        => mode is CapabilityExecutionModes.Probe
            or CapabilityExecutionModes.Detailed
            or CapabilityExecutionModes.Explicit;
}
