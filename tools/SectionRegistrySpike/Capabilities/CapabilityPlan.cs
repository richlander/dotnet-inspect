namespace SectionRegistrySpike.Capabilities;

/// <summary>
/// Immutable, dependency-ordered capability plan. Authorization is preflighted before any executor
/// runs, so a rejected dependent cannot leave earlier prerequisites partially applied.
/// </summary>
public sealed class CapabilityPlan<TContext>
{
    private readonly CapabilityRegistry<TContext>.Registration[] _entries;
    private readonly string[] _names;

    internal CapabilityPlan(CapabilityRegistry<TContext>.Registration[] entries)
    {
        _entries = entries;
        _names = entries.Select(entry => entry.Name).ToArray();
    }

    public int Count => _entries.Length;

    public IReadOnlyList<string> Names => _names;

    public bool CanExecute(CapabilityExecutionModes mode)
    {
        if (!IsSingleMode(mode))
            return false;

        foreach (var entry in _entries)
        {
            if ((entry.AllowedModes & mode) == 0)
                return false;
        }

        return true;
    }

    public async ValueTask ExecuteAsync(
        TContext context,
        CapabilityExecutionModes mode,
        Action<string>? trace = null)
    {
        if (!IsSingleMode(mode))
            throw new ArgumentOutOfRangeException(nameof(mode), mode, "Select one execution strategy.");

        foreach (var entry in _entries)
        {
            if ((entry.AllowedModes & mode) == 0)
            {
                throw new CapabilityNotAuthorizedException(
                    $"Capability '{entry.Name}' is not authorized for the '{mode}' strategy.");
            }
        }

        foreach (var entry in _entries)
        {
            await entry.Execute(context).ConfigureAwait(false);
            trace?.Invoke($"execute {entry.Name}");
        }
    }

    private static bool IsSingleMode(CapabilityExecutionModes mode)
        => mode is CapabilityExecutionModes.Probe
            or CapabilityExecutionModes.Detailed
            or CapabilityExecutionModes.Explicit;
}
