namespace ILInspector.Decompiler.Pipeline;

/// <summary>
/// Opt-in render knobs for <see cref="CSharpPrinter"/>. Every field defaults to
/// the shipped behavior, so <see cref="Default"/> reproduces today's output
/// byte-for-byte — the fidelity gate, <c>--skip-pdb</c> deterministic reading,
/// and annotated-IL alignment all depend on that. This is the single home for
/// render-quality options (readable names today; declaration placement and other
/// knobs as the #998 row grows), keeping them off the printer's positional
/// signature.
/// </summary>
public sealed record PrinterOptions
{
    /// <summary>
    /// When set, a local with no usable PDB source name renders a synthesized
    /// readable name (see <see cref="LocalNameSynthesizer"/>) instead of
    /// <c>V_index</c>. Off by default; the synthesizer falls back to
    /// <c>V_index</c> for any local it cannot name from IR evidence.
    /// </summary>
    public bool ReadableLocalNames { get; init; }

    /// <summary>The shipped defaults — every knob off.</summary>
    public static PrinterOptions Default { get; } = new();
}
