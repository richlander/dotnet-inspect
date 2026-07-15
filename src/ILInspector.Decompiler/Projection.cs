namespace ILInspector.Decompiler;

/// <summary>
/// The output currency every producer renders: an offset-anchorable view of one
/// entity (or, for Research, one correlation), plus the diagnostics gathered
/// producing it. The faithful machines — the signature decoder, the IL
/// disassembler, and the C# type-shell formatter — produce a
/// <see cref="Projection"/> directly, because a faithful re-representation earns
/// no fidelity ladder. The lossy decompiler produces a
/// <see cref="DecompilerResult"/>, which IS-A <see cref="Projection"/> that also
/// carries a <see cref="DecompilationFidelity"/> ladder and raise cargo.
/// </summary>
public record Projection(string? Output, IReadOnlyList<DecompilerDiagnostic> Diagnostics)
{
    /// <summary>True when the producer rendered output rather than failing.</summary>
    public bool Succeeded => Output is not null;
}
