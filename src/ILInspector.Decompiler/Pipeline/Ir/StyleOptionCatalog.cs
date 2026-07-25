namespace ILInspector.Decompiler.Pipeline;

/// <summary>
/// The family a <see cref="StyleOptionDescriptor"/> belongs to, ordered from the
/// most conservative contract to the least. The tier fixes the fidelity contract
/// the knob's output honors; <see cref="StyleOptionDescriptor.ByteDivergent"/> is
/// the machine-checkable consequence.
/// </summary>
public enum StyleOptionTier
{
    /// <summary>
    /// Layout only (whitespace/line breaks). Token- and byte-identical to the
    /// shipped default; no IL consequence. Below the class-3 spelling line.
    /// </summary>
    Formatting,

    /// <summary>
    /// A class-3 no-anchor token choice (e.g. <c>this.</c> qualification). Equally
    /// faithful to the default — the round-trip fixed point holds for either
    /// spelling — and byte-preserving (IL-identical).
    /// </summary>
    Spelling,

    /// <summary>
    /// A byte-divergent style lens (#3138): behavior-faithful but not
    /// opcode-faithful, so its output must never feed the compile-back fidelity
    /// gates. The only tier whose <see cref="StyleOptionDescriptor.ByteDivergent"/>
    /// is <see langword="true"/>.
    /// </summary>
    Lens,

    /// <summary>
    /// Name synthesis (readable local names). A separate axis from the
    /// fidelity-neutral knobs because it trades a fidelity property
    /// (Annotated-IL name alignment), even though the emitted IL is unchanged.
    /// </summary>
    Synthesis,
}

/// <summary>
/// The single, library-owned source of truth describing one opt-in
/// <see cref="PrinterOptions"/> knob: its identity, human-facing text, tier and
/// contract, oracle endorsement, config key, mutual-exclusivity group, and
/// NativeAOT-safe accessors to read and set it on a <see cref="PrinterOptions"/>.
///
/// <para>The accessors are explicit delegates (never reflection) so the catalog is
/// enumerable from any host — including a Wasm build — without breaking the
/// product's NativeAOT constraint. A UI can list every option, group the
/// mutually-exclusive ones by <see cref="ConflictGroup"/>, filter the
/// oracle-endorsed set for a future "full taste" aggregate, and toggle each knob
/// through <see cref="Get"/>/<see cref="With"/> without knowing the concrete
/// property.</para>
/// </summary>
public sealed record StyleOptionDescriptor
{
    /// <summary>Stable, kebab-case identifier for programmatic reference (never localized).</summary>
    public required string Id { get; init; }

    /// <summary>Short human-facing label for a picker (e.g. a checkbox caption).</summary>
    public required string Title { get; init; }

    /// <summary>One-sentence description of what enabling the knob does.</summary>
    public required string Summary { get; init; }

    /// <summary>The family and fidelity contract this knob belongs to.</summary>
    public required StyleOptionTier Tier { get; init; }

    /// <summary>
    /// <see langword="true"/> when the knob's output recompiles to different bytes
    /// than the shipped default (behavior-faithful but not opcode-faithful). Only
    /// the <see cref="StyleOptionTier.Lens"/> knobs are byte-divergent; enabling
    /// any of them promotes the whole render out of the compile-back fidelity gates.
    /// </summary>
    public required bool ByteDivergent { get; init; }

    /// <summary>
    /// <see langword="true"/> when the runtime <c>.editorconfig</c>/IDE oracle
    /// endorses this spelling (so it is eligible for a future "full taste"
    /// aggregate). <see langword="false"/> for idiosyncratic user preferences such
    /// as the branchless "bool hack".
    /// </summary>
    public required bool OracleEndorsed { get; init; }

    /// <summary>
    /// The <c>.dotnet-inspectconfig</c> key that selects this knob, or
    /// <see langword="null"/> when the knob has no config key (it is settable only
    /// through the API, e.g. a formatting knob). Oracle-endorsed keys use the
    /// editorconfig <c>dotnet_style_*</c> vocabulary; the non-endorsed branchless
    /// lens uses a tool-owned <c>dotnet_inspect_style_*</c> key.
    /// </summary>
    public string? ConfigKey { get; init; }

    /// <summary>
    /// Identifier of a mutual-exclusivity group, or <see langword="null"/> when the
    /// knob conflicts with nothing. Knobs that share a group rewrite the same shape,
    /// so a host should let at most one be enabled at a time (the printer still
    /// resolves any overlap deterministically, but a picker should not offer both).
    /// </summary>
    public string? ConflictGroup { get; init; }

    /// <summary>Reads the knob's current value from a <see cref="PrinterOptions"/>.</summary>
    public required Func<PrinterOptions, bool> Get { get; init; }

    /// <summary>Returns a copy of <paramref name="options"/> with this knob set to the given value.</summary>
    public required Func<PrinterOptions, bool, PrinterOptions> With { get; init; }
}

/// <summary>
/// The catalog of every opt-in boolean <see cref="PrinterOptions"/> knob, exposed
/// as the shared source of truth for hosts (CLI config resolution, a Wasm UI, the
/// future "full taste" aggregate) so option metadata lives in exactly one place
/// and cannot drift between the library and its consumers.
/// </summary>
public static class StyleOptionCatalog
{
    /// <summary>Mutual-exclusivity group for the guarded-boolean-return style lenses (ternary vs. branchless).</summary>
    public const string GuardedBooleanReturnGroup = "guarded-boolean-return";

    /// <summary>
    /// Every opt-in boolean knob, in a stable presentation order (formatting and
    /// spelling first, then the byte-divergent lenses). Every opt-in
    /// <see cref="PrinterOptions"/> knob is a boolean toggle, so this catalog is
    /// exhaustive; a future non-boolean knob would need a descriptor shape that
    /// carries its value domain.
    /// </summary>
    public static IReadOnlyList<StyleOptionDescriptor> Options { get; } =
    [
        new StyleOptionDescriptor
        {
            Id = "readable-local-names",
            Title = "Readable local names",
            Summary = "Synthesize a readable name for a local that has no PDB source name instead of V_index.",
            Tier = StyleOptionTier.Synthesis,
            ByteDivergent = false,
            OracleEndorsed = false,
            ConfigKey = null,
            Get = static o => o.ReadableLocalNames,
            With = static (o, v) => o with { ReadableLocalNames = v },
        },
        new StyleOptionDescriptor
        {
            Id = "wrap-splittable-expressions",
            Title = "Wrap long boolean chains",
            Summary = "Break a long short-circuit &&/|| chain across lines instead of one very wide line (whitespace only).",
            Tier = StyleOptionTier.Formatting,
            ByteDivergent = false,
            OracleEndorsed = false,
            ConfigKey = null,
            Get = static o => o.WrapSplittableExpressions,
            With = static (o, v) => o with { WrapSplittableExpressions = v },
        },
        new StyleOptionDescriptor
        {
            Id = "wrap-expression-body-arrow",
            Title = "Wrap expression-body arrow",
            Summary = "Wrap the => of an expression-bodied member or accessor onto the next line instead of keeping it on the declaration line (whitespace only).",
            Tier = StyleOptionTier.Formatting,
            ByteDivergent = false,
            OracleEndorsed = false,
            ConfigKey = null,
            Get = static o => o.WrapExpressionBodyArrow,
            With = static (o, v) => o with { WrapExpressionBodyArrow = v },
        },
        new StyleOptionDescriptor
        {
            Id = "qualify-field-access",
            Title = "Qualify field access with this.",
            Summary = "Render this. on an instance field even where the bare name is unambiguous (IL-identical).",
            Tier = StyleOptionTier.Spelling,
            ByteDivergent = false,
            OracleEndorsed = true,
            ConfigKey = "dotnet_style_qualification_for_field",
            Get = static o => o.QualifyFieldAccess,
            With = static (o, v) => o with { QualifyFieldAccess = v },
        },
        new StyleOptionDescriptor
        {
            Id = "qualify-property-access",
            Title = "Qualify property access with this.",
            Summary = "Render this. on an instance property even where the bare name is unambiguous (IL-identical).",
            Tier = StyleOptionTier.Spelling,
            ByteDivergent = false,
            OracleEndorsed = true,
            ConfigKey = "dotnet_style_qualification_for_property",
            Get = static o => o.QualifyPropertyAccess,
            With = static (o, v) => o with { QualifyPropertyAccess = v },
        },
        new StyleOptionDescriptor
        {
            Id = "qualify-method-access",
            Title = "Qualify method access with this.",
            Summary = "Render this. on an instance method call even where the bare name is unambiguous (IL-identical).",
            Tier = StyleOptionTier.Spelling,
            ByteDivergent = false,
            OracleEndorsed = true,
            ConfigKey = "dotnet_style_qualification_for_method",
            Get = static o => o.QualifyMethodAccess,
            With = static (o, v) => o with { QualifyMethodAccess = v },
        },
        new StyleOptionDescriptor
        {
            Id = "qualify-event-access",
            Title = "Qualify event access with this.",
            Summary = "Render this. on an instance event even where the bare name is unambiguous (IL-identical).",
            Tier = StyleOptionTier.Spelling,
            ByteDivergent = false,
            OracleEndorsed = true,
            ConfigKey = "dotnet_style_qualification_for_event",
            Get = static o => o.QualifyEventAccess,
            With = static (o, v) => o with { QualifyEventAccess = v },
        },
        new StyleOptionDescriptor
        {
            Id = "prefer-conditional-expression-return",
            Title = "Prefer conditional expression return",
            Summary = "Render a declined guarded boolean return as the ternary return c ? A : B; (oracle-preferred, byte-divergent).",
            Tier = StyleOptionTier.Lens,
            ByteDivergent = true,
            OracleEndorsed = true,
            ConfigKey = "dotnet_style_prefer_conditional_expression_over_return",
            ConflictGroup = GuardedBooleanReturnGroup,
            Get = static o => o.PreferConditionalExpressionReturn,
            With = static (o, v) => o with { PreferConditionalExpressionReturn = v },
        },
        new StyleOptionDescriptor
        {
            Id = "prefer-branchless-boolean",
            Title = "Prefer branchless boolean",
            Summary = "Render a declined guarded boolean return as the compact short-circuit \"bool hack\" (not oracle-endorsed, byte-divergent).",
            Tier = StyleOptionTier.Lens,
            ByteDivergent = true,
            OracleEndorsed = false,
            ConfigKey = "dotnet_inspect_style_prefer_branchless_boolean",
            ConflictGroup = GuardedBooleanReturnGroup,
            Get = static o => o.PreferBranchlessBoolean,
            With = static (o, v) => o with { PreferBranchlessBoolean = v },
        },
    ];

    /// <summary>
    /// The oracle-endorsed subset of <see cref="Options"/> — the knobs whose
    /// spelling the runtime <c>.editorconfig</c>/IDE oracle prefers — that the
    /// "full taste" aggregate enables together. A host lists these as the members
    /// a single "full taste" toggle turns on. Excludes idiosyncratic user
    /// preferences (e.g. the branchless "bool hack" lens) and the fidelity-neutral
    /// formatting/synthesis knobs the oracle takes no position on.
    ///
    /// <para>Declared after <see cref="Options"/> so its initializer reads the
    /// fully-built list. Because only oracle-endorsed knobs are included, at most
    /// one member of any <see cref="StyleOptionDescriptor.ConflictGroup"/> is
    /// present (the ternary lens, never the branchless one), so the subset carries
    /// no internal conflict.</para>
    /// </summary>
    public static IReadOnlyList<StyleOptionDescriptor> OracleEndorsedOptions { get; } =
        [.. Options.Where(o => o.OracleEndorsed)];

    /// <summary>
    /// Returns a copy of <paramref name="options"/> with every
    /// <see cref="OracleEndorsedOptions"/> knob set to <paramref name="enabled"/> —
    /// the "full taste" aggregate. When <paramref name="enabled"/> is
    /// <see langword="true"/> (the default) it turns the oracle-endorsed subset on;
    /// <see langword="false"/> turns exactly that subset off. Non-endorsed knobs are
    /// left untouched, and because the enabled subset shares no conflict group the
    /// result is deterministic. Reflection-free and NativeAOT-safe: it only folds
    /// the descriptors' explicit <see cref="StyleOptionDescriptor.With"/> delegates.
    /// </summary>
    public static PrinterOptions ApplyFullTaste(PrinterOptions options, bool enabled = true)
    {
        foreach (var knob in OracleEndorsedOptions)
            options = knob.With(options, enabled);

        return options;
    }
}
