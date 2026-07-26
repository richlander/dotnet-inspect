namespace ILInspector.Decompiler.Pipeline;

/// <summary>
/// Runtime opt-in for the IR invariant check
/// (<see cref="IrNode.CheckInvariant"/>). Off by default so the shipped tool
/// pays nothing on the decompile hot path; the test host and the harness corpus
/// sweep enable it to validate the pipeline after every pass in Release. The
/// check is leveled: <see cref="Enabled"/> turns on suite-safe structural
/// invariants, and <see cref="CheckSemantics"/> additionally turns on semantic
/// invariants that require fully-formed importer output.
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
    static readonly string? EnvValue =
        Environment.GetEnvironmentVariable("DOTNET_INSPECT_IR_INVARIANTS");

    /// <summary>
    /// When true, the pipeline runner and importer validate the IR after every
    /// pass. Initialized from the <c>DOTNET_INSPECT_IR_INVARIANTS</c> environment
    /// variable (<c>1</c>, <c>true</c>, or <c>full</c>), and settable by the test
    /// host and the harness so a corpus sweep can turn the check on without an
    /// environment variable. Explicit <see cref="IrNode.CheckInvariant()"/> calls
    /// (e.g. in tests) run regardless of this flag.
    /// </summary>
    public static bool Enabled { get; set; } =
        EnvValue is "1" or "true" or "full";

    /// <summary>
    /// When true, the per-pass hooks additionally validate <em>semantic</em>
    /// invariants that only hold for a fully-formed function tree — currently
    /// local-slot range (see <see cref="IrNode.CheckInvariant(bool)"/>). Off by
    /// default and left off by the unit-test host, because hand-built test
    /// fixtures legitimately omit the local table these checks validate against;
    /// enabling it suite-wide would false-positive on ~120 minimal-fixture tests.
    /// The harness corpus sweep sets it (directly or via
    /// <c>DOTNET_INSPECT_IR_INVARIANTS=full</c>) so semantic checks run over real
    /// importer output, where they are true invariants (verified zero-violation
    /// over CoreLib's 41,952 methods). Requires <see cref="Enabled"/> to take
    /// effect at the per-pass hooks.
    /// </summary>
    public static bool CheckSemantics { get; set; } =
        EnvValue is "full";
}
