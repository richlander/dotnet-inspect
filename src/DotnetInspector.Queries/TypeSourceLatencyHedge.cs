namespace DotnetInspector.Queries;

/// <summary>
/// Finite preference windows for ordinary type source acquisition.
/// </summary>
public sealed class TypeSourceLatencyHedge
{
    public TypeSourceLatencyHedge(
        TimeSpan portablePdbPreferenceWindow,
        TimeSpan authoredSourcePreferenceWindow,
        TimeProvider? timeProvider = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(
            portablePdbPreferenceWindow,
            TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(
            authoredSourcePreferenceWindow,
            TimeSpan.Zero);

        PortablePdbPreferenceWindow =
            portablePdbPreferenceWindow;
        AuthoredSourcePreferenceWindow =
            authoredSourcePreferenceWindow;
        TimeProvider = timeProvider ?? TimeProvider.System;
    }

    public TimeSpan PortablePdbPreferenceWindow { get; }
    public TimeSpan AuthoredSourcePreferenceWindow { get; }
    public TimeProvider TimeProvider { get; }
}
