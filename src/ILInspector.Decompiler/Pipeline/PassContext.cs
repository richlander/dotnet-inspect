namespace ILInspector.Decompiler.Pipeline;

/// <summary>
/// The per-run context threaded through every pass — ILSpy's
/// <c>ILTransformContext</c> in this codebase's vocabulary. A pass receives one
/// and records its rewrites through <see cref="Stepper"/>. The type is the seam
/// for the dataflow facts and diagnostics sink the IR design calls for
/// (docs/decompiler-ir.md); today it carries the stepper.
/// </summary>
public sealed class PassContext
{
    public PassContext(Stepper stepper) => Stepper = stepper;

    public Stepper Stepper { get; }

    /// <summary>A context with stepping disabled — the default for normal runs and for tests that drive a pass directly.</summary>
    public static PassContext None { get; } = new(new Stepper(enabled: false));
}
