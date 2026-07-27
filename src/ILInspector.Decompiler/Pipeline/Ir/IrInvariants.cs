namespace ILInspector.Decompiler.Pipeline;

/// <summary>
/// Runtime opt-in for the IR structural invariant check
/// (<see cref="IrNode.CheckInvariant"/>). Off by default so the shipped tool
/// pays nothing on the decompile hot path; the test host and the harness corpus
/// sweep enable it to validate the pipeline after every pass in Release.
/// <para>
/// This replaces the check's former <c>[Conditional("DEBUG")]</c> gate, which
/// stripped every call site in the Release configuration the test suite actually
/// runs — so the check that reads as if it asserts IR integrity asserted it zero
/// times (#3241). A runtime opt-in runs in any build, including the optimized
/// product assembly the corpus sweeps consume, with no change to product
/// codegen. It is the same separation the .NET runtime uses for its
/// <c>Checked</c> configuration: optimized codegen, assertions live.
/// </para>
/// </summary>
public static class IrInvariants
{
    /// <summary>
    /// When true, the pipeline runner and importer validate the IR after every
    /// pass. Initialized from the <c>DOTNET_INSPECT_IR_INVARIANTS</c> environment
    /// variable (<c>1</c> or <c>true</c>), and settable by the test host and the
    /// harness so a corpus sweep can turn the check on without an environment
    /// variable. Explicit <see cref="IrNode.CheckInvariant"/> calls (e.g. in
    /// tests) run regardless of this flag.
    /// </summary>
    public static bool Enabled { get; set; } =
        Environment.GetEnvironmentVariable("DOTNET_INSPECT_IR_INVARIANTS") is "1" or "true";
}
