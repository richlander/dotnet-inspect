namespace ILInspector.Decompiler.Pipeline;

/// <summary>
/// The per-run context threaded through every pass — ILSpy's
/// <c>ILTransformContext</c> in this codebase's vocabulary. A pass receives one
/// and records its rewrites through <see cref="Stepper"/>. The type is the seam
/// for the dataflow facts and diagnostics sink the IR design calls for
/// (docs/decompiler-ir.md); today it carries the stepper and the cross-method
/// import seam.
/// </summary>
public sealed class PassContext
{
    public PassContext(
        Stepper stepper,
        StructuringDiagnostics? structuringDiagnostics = null,
        Func<MethodRef, IrFunction?>? importMethodBody = null)
    {
        Stepper = stepper;
        StructuringDiagnostics = structuringDiagnostics;
        ImportMethodBody = importMethodBody;
    }

    public Stepper Stepper { get; }

    /// <summary>
    /// Optional sink for <see cref="StructuringPass"/> stop reasons. Null on every
    /// normal run (the pass records nothing); set only by the <c>--structuring-stops</c>
    /// diagnostic to make the common-exit docket reproducible.
    /// </summary>
    public StructuringDiagnostics? StructuringDiagnostics { get; }

    /// <summary>
    /// The cross-method import seam: imports a sibling method's freshly-imported
    /// (un-raised) body by reference, or null when the method is absent. The
    /// pipeline is otherwise per-method — a pass sees only its one function — so
    /// this is the single sanctioned way for a pass to reach another body.
    /// <see cref="LambdaRaisingPass"/> uses it to pull in a lambda's synthesized
    /// method. Null on runs that wired no source (stage dumps, direct pass tests,
    /// the lowered view); a pass that needs it must no-op when it is null.
    /// </summary>
    public Func<MethodRef, IrFunction?>? ImportMethodBody { get; }

    /// <summary>A context with stepping disabled — the default for normal runs and for tests that drive a pass directly.</summary>
    public static PassContext None { get; } = new(new Stepper(enabled: false));
}
