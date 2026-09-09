namespace ILInspector.Decompiler.Tests.Gating;

/// <summary>
/// A named, discoverable shortcut for a set of xUnit v3 console-runner
/// arguments (typically <c>-trait</c>/<c>-trait-</c> filters). Presets let
/// callers bound which test gates run without memorizing the trait taxonomy.
/// </summary>
/// <remarks>
/// This type is deliberately free of any project-specific knowledge so the
/// gate machinery can be shared across test executables. The concrete preset
/// table (the mapping from a name to trait arguments) is supplied by each
/// consuming project.
/// </remarks>
/// <param name="Name">The preset name selected via <c>--gate &lt;name&gt;</c>.</param>
/// <param name="Summary">One-line description shown by <c>--gate list</c>.</param>
/// <param name="Args">
/// The xUnit console-runner arguments this preset expands to. They are
/// prepended ahead of any caller-supplied arguments. An empty list means the
/// preset adds no filter (i.e. runs everything).
/// </param>
public sealed record GatePreset(string Name, string Summary, IReadOnlyList<string> Args)
{
    public GatePreset(string name, string summary, params string[] args)
        : this(name, summary, (IReadOnlyList<string>)args)
    {
    }
}
