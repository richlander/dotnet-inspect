namespace DotnetInspector.Queries;

/// <summary>
/// Finite Portable PDB preference for conservative type source acquisition.
/// </summary>
public sealed class TypeSourcePdbLatencyHedge
{
    public TypeSourcePdbLatencyHedge(
        TimeSpan portablePdbPreferenceWindow,
        TimeProvider? timeProvider = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(
            portablePdbPreferenceWindow,
            TimeSpan.Zero);

        PortablePdbPreferenceWindow =
            portablePdbPreferenceWindow;
        TimeProvider = timeProvider ?? TimeProvider.System;
    }

    public TimeSpan PortablePdbPreferenceWindow { get; }
    public TimeProvider TimeProvider { get; }
}
