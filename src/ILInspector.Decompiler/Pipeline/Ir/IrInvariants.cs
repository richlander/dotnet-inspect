namespace ILInspector.Decompiler.Pipeline;

/// <summary>
/// Runtime configuration for the IR invariant check
/// (<see cref="IrNode.CheckInvariant"/>). On by default so every host that runs
/// the pipeline validates it after every pass; the shipped tool is the one host
/// that opts out (<see cref="DisableForShippedTool"/>), so it pays nothing on
/// the decompile hot path. The check is leveled: <see cref="Enabled"/> turns on
/// suite-safe structural invariants, and <see cref="CheckSemantics"/>
/// additionally turns on semantic invariants that require fully-formed importer
/// output.
/// <para>
/// This replaces the check's former <c>[Conditional("DEBUG")]</c> gate, which
/// stripped every call site in the Release configuration the test suite actually
/// runs — so the check that reads as if it asserts IR integrity asserted it zero
/// times (#3241). A runtime flag runs in any build, including the optimized
/// product assembly the corpus sweeps consume, with no change to product
/// codegen. It is the same separation the .NET runtime uses for its
/// <c>Checked</c> configuration: optimized codegen, assertions live.
/// </para>
/// <para>
/// The default is <em>on</em> rather than opt-in (#3267). Opt-in moved the
/// #3241 failure mode instead of removing it: a new host — another harness, a
/// sweep tool, a benchmark — would exercise the pipeline broadly while
/// validating nothing, and look healthy doing it. With the default inverted,
/// declining validation has exactly one form — <see cref="Enabled"/>'s setter
/// is private, so the compiler rejects any other spelling — and that one form
/// is pinned to a single call site by
/// <c>IrInvariantsHostContractTests</c>.
/// </para>
/// </summary>
public static class IrInvariants
{
    static readonly string? EnvValue =
        Environment.GetEnvironmentVariable("DOTNET_INSPECT_IR_INVARIANTS");

    /// <summary>
    /// The operator's explicit request from <c>DOTNET_INSPECT_IR_INVARIANTS</c>:
    /// <see langword="true"/> for <c>1</c>/<c>true</c>/<c>full</c>,
    /// <see langword="false"/> for <c>0</c>/<c>false</c>/<c>off</c>, and
    /// <see langword="null"/> when unset or unrecognized. An explicit request
    /// outranks the host opt-out, so the shipped tool can be run with the check
    /// armed for debugging without a rebuild.
    /// </summary>
    static readonly bool? EnvironmentRequest = ParseRequest(EnvValue);

    /// <summary>
    /// When true, the pipeline runner and importer validate the IR after every
    /// pass. On unless <c>DOTNET_INSPECT_IR_INVARIANTS</c> asks for off or a
    /// host calls <see cref="DisableForShippedTool"/>. Explicit
    /// <see cref="IrNode.CheckInvariant()"/> calls (e.g. in tests) run
    /// regardless of this flag.
    /// <para>
    /// The setter is private on purpose: turning validation off is a decision
    /// with exactly one sanctioned form, so the compiler — not a convention or a
    /// source census — is what stops a host from writing
    /// <c>Enabled = false</c> under a <c>using static</c> or a namespace alias.
    /// It also removes the temptation to flip the flag inside a test, which
    /// would race the parallel collections xUnit runs it under.
    /// </para>
    /// </summary>
    public static bool Enabled { get; private set; } = ResolveEnabled(EnvironmentRequest, hostOptedOut: false);

    /// <summary>
    /// When true, the per-pass hooks additionally validate <em>semantic</em>
    /// invariants that only hold for a fully-formed function tree — currently
    /// local-slot range (see <see cref="IrNode.CheckInvariant(bool)"/>). Off by
    /// default even in validating hosts, because hand-built test fixtures
    /// legitimately omit the local table these checks validate against; enabling
    /// it suite-wide false-positives 5 minimal-fixture tests (measured with
    /// <c>DOTNET_INSPECT_IR_INVARIANTS=full --gate fast</c> — re-measure rather
    /// than trust this figure; it read <c>~120</c> until #3303 corrected it, and
    /// #3302 tracks populating those fixtures' locals so the level can be armed
    /// by default). Requires <see cref="Enabled"/> to take effect at the
    /// per-pass hooks.
    /// <para>
    /// <c>DOTNET_INSPECT_IR_INVARIANTS=full</c> is the one spelling that raises
    /// this level, resolved once at startup: the property has no setter at all,
    /// so the level can be neither raised nor lowered in-process, and no test
    /// can race the collections xUnit runs in parallel by toggling it. A host
    /// that wants semantic coverage over real importer output threads the level
    /// per call instead — <c>CheckInvariant(includeSemantics: true)</c>, as the
    /// corpus gates do (<c>CorpusSweepGateTests</c>) — which keeps the coverage
    /// hermetic. Over real output these are true invariants (verified
    /// zero-violation over CoreLib's 41,952 methods); the environment spelling
    /// additionally sweeps them after every pass, which also catches transient
    /// mid-pass shapes the final-tree gates cannot see.
    /// </para>
    /// </summary>
    public static bool CheckSemantics { get; } = RequestsSemantics(EnvValue);

    /// <summary>
    /// The one sanctioned opt-out: the shipped CLI's decompile hot path, where
    /// the check is pure overhead for a user who is not developing the pipeline.
    /// Any other host — harness, sweep, benchmark, test — is a validating host
    /// and must leave the check armed. Honors an explicit
    /// <c>DOTNET_INSPECT_IR_INVARIANTS</c> request, so an operator can arm the
    /// shipped tool for debugging.
    /// </summary>
    public static void DisableForShippedTool() =>
        Enabled = ResolveEnabled(EnvironmentRequest, hostOptedOut: true);

    /// <summary>
    /// The default rule, factored out so the environment/host precedence is
    /// testable without process isolation: an explicit environment request wins;
    /// otherwise validation is on unless the host opted out.
    /// </summary>
    internal static bool ResolveEnabled(bool? environmentRequest, bool hostOptedOut) =>
        environmentRequest ?? !hostOptedOut;

    /// <summary>
    /// Maps a <c>DOTNET_INSPECT_IR_INVARIANTS</c> value to an explicit on/off
    /// request, or <see langword="null"/> when it expresses none. Trimmed and
    /// case-insensitive: with the default inverted, silently ignoring
    /// <c>False</c> or <c>Off</c> would leave the check armed against an
    /// operator who explicitly asked for it off.
    /// </summary>
    internal static bool? ParseRequest(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        string request = value.Trim();
        if (Is(request, "1", "true", "full", "on", "yes"))
            return true;
        if (Is(request, "0", "false", "off", "no"))
            return false;

        return null;

        static bool Is(string request, params string[] candidates) =>
            candidates.Any(candidate => string.Equals(request, candidate, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Whether the environment asked for the semantic level (<c>full</c>).</summary>
    static bool RequestsSemantics(string? value) =>
        string.Equals(value?.Trim(), "full", StringComparison.OrdinalIgnoreCase);
}
