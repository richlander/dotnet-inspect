namespace ILInspector.Decompiler;

/// <summary>
/// Where a method's source symbols (local names) came from for a render, or
/// <see cref="None"/> when none were consulted — no PDB was found, symbols were
/// disabled, or the body had no locals to name. A host surfaces this to explain
/// local-name quality and to tie a decompile to any preceding symbol-server fetch.
/// </summary>
public enum DecompilerSymbolSource
{
    /// <summary>No PDB was consulted (none found, disabled, or no locals to name).</summary>
    None,

    /// <summary>A portable PDB embedded in the PE.</summary>
    Embedded,

    /// <summary>A sidecar <c>.pdb</c> next to the assembly.</summary>
    Sidecar,

    /// <summary>An external PDB supplied at open (e.g. a symbol-server download).</summary>
    External,
}

/// <summary>
/// A telemetry-free, structured record of what a decompilation observed — its
/// fidelity outcome, the symbol source it used, and any diagnostics. The
/// decompiler depends only on <c>ILInspector.Metadata</c> and never emits
/// telemetry itself; a host converts this shape into its own diagnostics (e.g.
/// the CLI's request-trace breadcrumbs).
/// </summary>
public sealed record DecompilerTrace(
    DecompilationFidelity Fidelity,
    DecompilerSymbolSource Symbols,
    IReadOnlyList<DecompilerDiagnostic> Diagnostics)
{
    /// <summary>True when the decompiler produced usable output.</summary>
    public bool Succeeded => Fidelity != DecompilationFidelity.Failed;
}
