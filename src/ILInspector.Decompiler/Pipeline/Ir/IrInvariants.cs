namespace ILInspector.Decompiler.Pipeline;

/// <summary>
/// Runtime configuration for the IR invariant check
/// (<see cref="IrNode.CheckInvariant"/>). On by default so every host that runs
/// the pipeline validates it after every pass; the shipped tool is the one host
/// that opts out (<see cref="DisableForShippedTool"/>), so it pays nothing on
/// the decompile hot path. The check is leveled — <see cref="Enabled"/> for
/// structural invariants and <see cref="CheckSemantics"/> for semantic ones —
/// but both levels are now armed together, so the leveling describes what is
/// checked rather than offering a way to check less (#3302).
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
    /// armed for debugging without a rebuild. <c>full</c> is retained as a
    /// synonym for on: it selected both levels when the semantic level was
    /// opt-in, and both levels are now the default, so it keeps working and
    /// keeps meaning the same thing.
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
/// local-slot range (see <see cref="IrNode.CheckInvariant(bool)"/>). Armed
    /// with <see cref="Enabled"/> since #3302, and not merely alongside it: this
    /// is a computed projection of that flag, so the two levels cannot drift
    /// apart in-process by construction rather than by discipline. There is no
    /// backing field to move, so #3303's guarantee — the level can be neither
    /// raised nor lowered in-process, and no test can race the collections xUnit
    /// runs in parallel by toggling it — is preserved, and the shipped tool's
    /// opt-out lowers both levels with one assignment.
    /// <para>
    /// This level was opt-in on the grounds that arming it suite-wide would
    /// false-positive on ~120 minimal-fixture tests. Measured, the real number
    /// was five, all of them hand-built fixtures that referenced a local slot
    /// without declaring it. At ~120 the opt-in is obviously right; at five it
    /// is a bounded cleanup, not a standing reason to ship less validation, so
    /// the fixtures now declare their locals and the level is on. Semantic
    /// invariants are the ones that catch a pass leaving a slot reference that
    /// no longer resolves — exactly the defect class the local-elimination
    /// bookkeeping can get wrong — and per-pass coverage is what makes a
    /// transient corruption visible even when a later pass repairs it. Over real
    /// output these are true invariants (verified zero-violation over CoreLib's
    /// 41,952 methods). Cost, measured as a paired A/B of the <c>Area=Corpus</c>
    /// gate against this change's parent: ~18.0s to ~18.6s. Treat that as
    /// "below the noise floor" rather than as a figure to quote — run-to-run
    /// spread on the same build (17.8s-22.8s) is wider than the difference.
    /// </para>
    /// <para>
    /// Read that five precisely, because the obvious misreading is the one this
    /// type keeps getting punished for: five is the number of fixtures that
    /// <em>reach the per-pass hook</em> and fail, not the number of fixtures
    /// that would satisfy the level. Per-pass validation fires inside
    /// <c>IrPasses.Run</c>/<c>PipelineRunner</c>, so a test that calls
    /// <c>pass.Run(...)</c> directly never reaches it. Roughly a dozen test
    /// files still build an <c>IrFunction</c> with an empty local table and
    /// reference slots in it; they are unaffected today and were equally
    /// unchecked before this change, but converting one to <c>IrPasses.Run</c>
    /// will now fail it. That is the intended signal rather than a regression —
    /// the fixture is genuinely malformed — and the fix is to declare the
    /// locals. Tracked as follow-up, not silently absorbed.
    /// </para>
    /// <para>
    /// A fixture that cannot satisfy this level should declare the locals it
    /// uses, not lower the level. Deriving the local table from the body would
    /// make every fixture pass by construction and would retire the invariant
    /// while appearing to keep it.
    /// </para>
    /// <para>
    /// Consequence: there is no longer an <em>environment</em> spelling for
    /// structural-only. <c>full</c> keeps working and keeps meaning both levels;
    /// <c>1</c>/<c>true</c> now arm both. A spelling that quietly bought
    /// <em>less</em> validation than the default would be the same
    /// silent-downgrade trap #3289 removed for the off case. The per-call
    /// <c>CheckInvariant(includeSemantics:)</c> parameter is deliberately
    /// <em>not</em> such a spelling: it is a visible argument at a call site
    /// chosen by the test that owns it, which is what keeps that coverage
    /// hermetic under xUnit's parallel collections, rather than a process-wide
    /// knob that silently lowers what some other host asked for.
    /// </para>
    /// </summary>
    public static bool CheckSemantics => Enabled;

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
}
