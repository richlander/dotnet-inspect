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
/// One selectable value on a <see cref="StyleOptionDescriptor"/>'s axis: its
/// stable token, optional human label, oracle endorsement, and the
/// <c>.dotnet-inspectconfig</c> key (if any) that selects it. A boolean knob has
/// two values (<c>false</c>/<c>true</c>); a multi-value knob such as the
/// guarded-boolean-return family has three or more. The accessors are explicit,
/// NativeAOT-safe delegates (never reflection) so any host can read and set the
/// value on a <see cref="PrinterOptions"/> without knowing the concrete backing
/// property or properties.
/// </summary>
public sealed record StyleOptionValue
{
    /// <summary>
    /// Stable token identifying this value on the descriptor's axis
    /// (kebab-case; <c>false</c>/<c>true</c> for a plain two-state toggle). Never
    /// localized.
    /// </summary>
    public required string Token { get; init; }

    /// <summary>Short human-facing label for a picker, or null to fall back to <see cref="Token"/>.</summary>
    public string? Title { get; init; }

    /// <summary>
    /// <see langword="true"/> when the runtime <c>.editorconfig</c>/IDE oracle
    /// endorses this value (so it is the value a "full taste" aggregate selects on
    /// this axis). At most one value per descriptor is oracle-endorsed. This is the
    /// <b>declared</b> oracle facet; <see cref="CorpusEndorsed"/> is the orthogonal
    /// <b>revealed</b> facet.
    /// </summary>
    public bool OracleEndorsed { get; init; }

    /// <summary>
    /// <see langword="true"/> when the runtime's own <b>source corpus</b> reveals a
    /// dominant practice endorsing this value — the <b>revealed</b> oracle facet.
    /// Independent of <see cref="OracleEndorsed"/> (the declared facet): a value may
    /// be endorsed by neither facet (an idiosyncratic preference such as the
    /// branchless "bool hack", or wrapping the expression-body arrow, which the
    /// corpus does not do), by the declared facet, by the revealed facet, or by
    /// both. Each <see langword="true"/> is a deliberate, documented judgment,
    /// never a silently-inferred or measured-heat claim (see
    /// <c>docs/decompiler-taste.md</c>, the two oracle facets).
    /// </summary>
    public bool CorpusEndorsed { get; init; }

    /// <summary>
    /// The <c>.dotnet-inspectconfig</c> key whose <c>= true</c> selects this value
    /// (and whose <c>= false</c> deselects it), or <see langword="null"/> when the
    /// value is not directly config-selectable — the default/off value of a knob,
    /// or an API-only knob with no config vocabulary. Oracle-endorsed keys use the
    /// editorconfig <c>dotnet_style_*</c> vocabulary; tool-owned values (e.g. the
    /// branchless "bool hack") use a <c>dotnet_inspect_style_*</c> key.
    /// </summary>
    public string? ConfigKey { get; init; }

    /// <summary>Reads whether this value is currently selected on a <see cref="PrinterOptions"/>.</summary>
    public required Func<PrinterOptions, bool> IsSelected { get; init; }

    /// <summary>
    /// Returns a copy of <paramref name="options"/> with this value's presence set
    /// to the given flag. This is the per-key config setter: <c>key = true</c>
    /// calls it with <see langword="true"/>, <c>key = false</c> with
    /// <see langword="false"/>. It sets only this value's own backing state and
    /// does not clear sibling values — the printer resolves any overlap
    /// deterministically, exactly as it did when each value was an independent
    /// boolean knob. Callers wanting single-select (clear siblings) semantics use
    /// <see cref="StyleOptionDescriptor.WithValue"/>.
    /// </summary>
    public required Func<PrinterOptions, bool, PrinterOptions> SetSelected { get; init; }
}

/// <summary>
/// The single, library-owned source of truth describing one opt-in
/// <see cref="PrinterOptions"/> knob: its identity, human-facing text, tier and
/// contract, and the value domain it ranges over (its <see cref="Values"/>). A
/// boolean knob is the two-value case (<c>false</c>/<c>true</c>); a multi-value
/// knob — the guarded-boolean-return family — carries three tokens on one axis.
///
/// <para>All accessors are explicit delegates (never reflection) so the catalog is
/// enumerable from any host — including a Wasm build — without breaking the
/// product's NativeAOT constraint. A UI can list every option, present each
/// axis's <see cref="Values"/> as a mutually-exclusive choice, filter the
/// oracle-endorsed value of each axis for the "full taste" aggregate, and read or
/// set the axis through <see cref="GetValue"/>/<see cref="WithValue"/> without
/// knowing the concrete backing property.</para>
/// </summary>
public sealed record StyleOptionDescriptor
{
    /// <summary>Stable, kebab-case identifier for programmatic reference (never localized).</summary>
    public required string Id { get; init; }

    /// <summary>Short human-facing label for a picker (e.g. a checkbox or dropdown caption).</summary>
    public required string Title { get; init; }

    /// <summary>One-sentence description of what the knob controls.</summary>
    public required string Summary { get; init; }

    /// <summary>The family and fidelity contract this knob belongs to.</summary>
    public required StyleOptionTier Tier { get; init; }

    /// <summary>
    /// <see langword="true"/> when the knob's non-default output recompiles to
    /// different bytes than the shipped default (behavior-faithful but not
    /// opcode-faithful). Only the <see cref="StyleOptionTier.Lens"/> knobs are
    /// byte-divergent; selecting any non-default value of one promotes the whole
    /// render out of the compile-back fidelity gates.
    /// </summary>
    public required bool ByteDivergent { get; init; }

    /// <summary>
    /// The token of the shipped-default value — the value in effect on
    /// <see cref="PrinterOptions.Default"/>. <see cref="GetValue"/> returns this
    /// when no non-default value is selected.
    /// </summary>
    public required string DefaultValue { get; init; }

    /// <summary>
    /// The value domain this knob ranges over, in presentation order. The first
    /// entry is the default/off value; later entries are the selectable
    /// alternatives. For a multi-value axis the order is also the resolution
    /// precedence <see cref="GetValue"/> reports when more than one value is set at
    /// once (the earlier, oracle-endorsed value wins — matching the printer).
    /// </summary>
    public required IReadOnlyList<StyleOptionValue> Values { get; init; }

    /// <summary>
    /// The oracle-endorsed value on this axis (the one a "full taste" aggregate
    /// selects), or <see langword="null"/> when the oracle takes no position on
    /// this knob. At most one value is endorsed.
    /// </summary>
    public StyleOptionValue? EndorsedValue => Values.SingleOrDefault(v => v.OracleEndorsed);

    /// <summary>
    /// <see langword="true"/> when some value on this axis is oracle-endorsed, so
    /// the knob participates in the "full taste" aggregate.
    /// </summary>
    public bool OracleEndorsed => EndorsedValue is not null;

    /// <summary>
    /// The corpus-endorsed (revealed) value on this axis, or <see langword="null"/>
    /// when the runtime source corpus reveals no dominant practice for this knob.
    /// At most one value is corpus-endorsed.
    /// </summary>
    public StyleOptionValue? CorpusEndorsedValue => Values.SingleOrDefault(v => v.CorpusEndorsed);

    /// <summary>
    /// <see langword="true"/> when some value on this axis is corpus-endorsed (the
    /// revealed facet). Orthogonal to <see cref="OracleEndorsed"/>.
    /// </summary>
    public bool CorpusEndorsed => CorpusEndorsedValue is not null;

    /// <summary>
    /// The config key that turns a two-state (boolean) knob on, or
    /// <see langword="null"/> for an API-only or multi-value knob (whose per-value
    /// keys live on <see cref="Values"/>). Convenience for the common boolean case
    /// so a host recording a two-state taste choice can name the key without
    /// walking <see cref="Values"/>.
    /// </summary>
    public string? ConfigKey =>
        Values.Count == 2 ? Values[1].ConfigKey : null;

    /// <summary>
    /// The token of the value currently selected on <paramref name="options"/> —
    /// the first value in <see cref="Values"/> order whose
    /// <see cref="StyleOptionValue.IsSelected"/> holds, or <see cref="DefaultValue"/>
    /// when none does. The <see cref="Values"/> order makes this deterministic when
    /// more than one value is set at once (the earlier value wins).
    /// </summary>
    public string GetValue(PrinterOptions options)
    {
        foreach (var value in Values)
            if (value.IsSelected(options))
                return value.Token;

        return DefaultValue;
    }

    /// <summary>
    /// Returns a copy of <paramref name="options"/> with this axis set to
    /// <paramref name="token"/> and every other value on the axis cleared
    /// (single-select). Selecting <see cref="DefaultValue"/> clears the whole axis.
    /// Reflection-free: it only folds the values' explicit
    /// <see cref="StyleOptionValue.SetSelected"/> delegates.
    /// </summary>
    public PrinterOptions WithValue(PrinterOptions options, string token)
    {
        foreach (var value in Values)
            options = value.SetSelected(options, string.Equals(value.Token, token, StringComparison.Ordinal));

        return options;
    }

    /// <summary>
    /// Boolean convenience: <see langword="true"/> when any non-default value is
    /// selected. Meaningful for the two-state knobs (and reports "some non-default
    /// value is on" for a multi-value axis).
    /// </summary>
    public bool Get(PrinterOptions options) =>
        !string.Equals(GetValue(options), DefaultValue, StringComparison.Ordinal);
}

/// <summary>
/// The catalog of every opt-in <see cref="PrinterOptions"/> knob, exposed as the
/// shared source of truth for hosts (CLI config resolution, a Wasm UI, the "full
/// taste" aggregate) so option metadata lives in exactly one place and cannot
/// drift between the library and its consumers. Most knobs are two-state
/// (boolean) toggles; the guarded-boolean-return family is a single multi-value
/// axis whose value domain the descriptor carries directly.
/// </summary>
public static class StyleOptionCatalog
{
    // Value tokens for the guarded-boolean-return family axis.
    private const string GuardedReturnDefault = "flat";
    private const string GuardedReturnConditional = "conditional-expression";
    private const string GuardedReturnBranchless = "branchless";

    // Value tokens for the var-spelling family axis.
    private const string VarStyleExplicit = "explicit";
    private const string VarStyleBuiltInTypes = "var-for-built-in-types";
    private const string VarStyleWhenApparent = "var-when-type-apparent";
    private const string VarStyleElsewhere = "var-elsewhere";

    /// <summary>
    /// Every opt-in knob, in a stable presentation order (formatting and spelling
    /// first, then the byte-divergent lens). Two-state knobs carry a
    /// <c>false</c>/<c>true</c> value domain; the guarded-boolean-return knob is a
    /// single multi-value axis (<c>flat</c> / <c>conditional-expression</c> /
    /// <c>branchless</c>). The catalog is exhaustive: the drift-guard test asserts
    /// every backing <see cref="PrinterOptions"/> property is reachable through
    /// some descriptor value.
    /// </summary>
    public static IReadOnlyList<StyleOptionDescriptor> Options { get; } =
    [
        Boolean(
            id: "readable-local-names",
            title: "Readable local names",
            summary: "Synthesize a readable name for a local that has no PDB source name instead of V_index.",
            tier: StyleOptionTier.Synthesis,
            byteDivergent: false,
            oracleEndorsed: false,
            configKey: "dotnet_inspect_style_readable_local_names",
            get: static o => o.ReadableLocalNames,
            with: static (o, v) => o with { ReadableLocalNames = v }),
        Boolean(
            id: "wrap-splittable-expressions",
            title: "Wrap long boolean chains",
            summary: "Break a long short-circuit &&/|| chain across lines instead of one very wide line (whitespace only).",
            tier: StyleOptionTier.Formatting,
            byteDivergent: false,
            oracleEndorsed: false,
            configKey: null,
            get: static o => o.WrapSplittableExpressions,
            with: static (o, v) => o with { WrapSplittableExpressions = v },
            corpusEndorsed: true),
        Boolean(
            id: "disable-one-liner-wrapping",
            title: "Keep one-liners on one line",
            summary: "Suppress the always-on width wrappers (long fluent chains and long member signatures) so a wide construct stays on a single physical line instead of wrapping (whitespace only).",
            tier: StyleOptionTier.Formatting,
            byteDivergent: false,
            oracleEndorsed: false,
            configKey: null,
            get: static o => o.DisableOneLinerWrapping,
            with: static (o, v) => o with { DisableOneLinerWrapping = v }),
        Boolean(
            id: "wrap-expression-body-arrow",
            title: "Wrap expression-body arrow",
            summary: "Wrap the => of an expression-bodied member or accessor onto the next line instead of keeping it on the declaration line (whitespace only).",
            tier: StyleOptionTier.Formatting,
            byteDivergent: false,
            oracleEndorsed: false,
            configKey: null,
            get: static o => o.WrapExpressionBodyArrow,
            with: static (o, v) => o with { WrapExpressionBodyArrow = v }),
        Boolean(
            id: "qualify-field-access",
            title: "Qualify field access with this.",
            summary: "Render this. on an instance field even where the bare name is unambiguous (IL-identical).",
            tier: StyleOptionTier.Spelling,
            byteDivergent: false,
            oracleEndorsed: true,
            configKey: "dotnet_style_qualification_for_field",
            get: static o => o.QualifyFieldAccess,
            with: static (o, v) => o with { QualifyFieldAccess = v }),
        Boolean(
            id: "qualify-property-access",
            title: "Qualify property access with this.",
            summary: "Render this. on an instance property even where the bare name is unambiguous (IL-identical).",
            tier: StyleOptionTier.Spelling,
            byteDivergent: false,
            oracleEndorsed: true,
            configKey: "dotnet_style_qualification_for_property",
            get: static o => o.QualifyPropertyAccess,
            with: static (o, v) => o with { QualifyPropertyAccess = v }),
        Boolean(
            id: "qualify-method-access",
            title: "Qualify method access with this.",
            summary: "Render this. on an instance method call even where the bare name is unambiguous (IL-identical).",
            tier: StyleOptionTier.Spelling,
            byteDivergent: false,
            oracleEndorsed: true,
            configKey: "dotnet_style_qualification_for_method",
            get: static o => o.QualifyMethodAccess,
            with: static (o, v) => o with { QualifyMethodAccess = v }),
        Boolean(
            id: "qualify-event-access",
            title: "Qualify event access with this.",
            summary: "Render this. on an instance event even where the bare name is unambiguous (IL-identical).",
            tier: StyleOptionTier.Spelling,
            byteDivergent: false,
            oracleEndorsed: true,
            configKey: "dotnet_style_qualification_for_event",
            get: static o => o.QualifyEventAccess,
            with: static (o, v) => o with { QualifyEventAccess = v }),
        GuardedBooleanReturnStyle(),
        VarSpellingStyle(),
        Boolean(
            id: "prefer-long-literal-suffix",
            title: "Long literal suffix (10L)",
            summary: "Render a long constant the IL spells `ldc.i4(.s) N; conv.i8` as the idiomatic NL literal instead of the (long)N cast; a genuine ldc.i8 source keeps its current spelling.",
            tier: StyleOptionTier.Lens,
            byteDivergent: true,
            oracleEndorsed: false,
            configKey: "dotnet_inspect_style_prefer_long_literal_suffix",
            get: static o => o.PreferLongLiteralSuffix,
            with: static (o, v) => o with { PreferLongLiteralSuffix = v }),
    ];

    /// <summary>
    /// The oracle-endorsed subset of <see cref="Options"/> — the knobs whose axis
    /// carries a value the runtime <c>.editorconfig</c>/IDE oracle prefers — that
    /// the "full taste" aggregate selects the endorsed value of. A host lists these
    /// as the members a single "full taste" toggle turns on. Excludes knobs the
    /// oracle takes no position on (the formatting/synthesis knobs and the
    /// idiosyncratic branchless "bool hack" value).
    ///
    /// <para>Declared after <see cref="Options"/> so its initializer reads the
    /// fully-built list. Because at most one value per axis is endorsed, the
    /// aggregate selects at most one value per knob and so carries no internal
    /// conflict.</para>
    /// </summary>
    public static IReadOnlyList<StyleOptionDescriptor> OracleEndorsedOptions { get; } =
        [.. Options.Where(o => o.OracleEndorsed)];

    /// <summary>
    /// The corpus-endorsed (revealed-preference) subset of <see cref="Options"/> —
    /// the knobs whose axis carries a value the runtime's own <b>source corpus</b>
    /// reveals a dominant practice for. Orthogonal to
    /// <see cref="OracleEndorsedOptions"/> (the declared facet): today's subsets
    /// happen to be disjoint, but that is not a contract. Reserved for a future
    /// "house style" aggregate (declared ∪ revealed), tracked in #3179; the "full
    /// taste" aggregate stays declared-only.
    ///
    /// <para>Declared after <see cref="Options"/> so its initializer reads the
    /// fully-built list.</para>
    /// </summary>
    public static IReadOnlyList<StyleOptionDescriptor> CorpusEndorsedOptions { get; } =
        [.. Options.Where(o => o.CorpusEndorsed)];

    /// <summary>
    /// Returns a copy of <paramref name="options"/> with every
    /// <see cref="OracleEndorsedOptions"/> knob's endorsed value selected (or, when
    /// <paramref name="enabled"/> is <see langword="false"/>, deselected) — the
    /// "full taste" aggregate. When <paramref name="enabled"/> is
    /// <see langword="true"/> (the default) it turns the oracle-endorsed value of
    /// each participating axis on; <see langword="false"/> turns exactly those
    /// values off. Knobs the oracle takes no position on are left untouched, and
    /// because at most one value per axis is endorsed the result is deterministic.
    /// Reflection-free and NativeAOT-safe: it only folds the endorsed values'
    /// explicit <see cref="StyleOptionValue.SetSelected"/> delegates.
    /// </summary>
    public static PrinterOptions ApplyFullTaste(PrinterOptions options, bool enabled = true)
    {
        foreach (var knob in OracleEndorsedOptions)
            if (knob.EndorsedValue is { } endorsed)
                options = endorsed.SetSelected(options, enabled);

        return options;
    }

    // Builds a two-state (boolean) knob: a false/true value domain where the
    // config key (when present) lives on the "true" value and its SetSelected both
    // sets and clears the backing property, exactly matching how a plain boolean
    // knob toggled before the value domain was generalized.
    private static StyleOptionDescriptor Boolean(
        string id,
        string title,
        string summary,
        StyleOptionTier tier,
        bool byteDivergent,
        bool oracleEndorsed,
        string? configKey,
        Func<PrinterOptions, bool> get,
        Func<PrinterOptions, bool, PrinterOptions> with,
        bool corpusEndorsed = false)
        => new()
        {
            Id = id,
            Title = title,
            Summary = summary,
            Tier = tier,
            ByteDivergent = byteDivergent,
            DefaultValue = "false",
            Values =
            [
                new StyleOptionValue
                {
                    Token = "false",
                    IsSelected = o => !get(o),
                    SetSelected = (o, on) => on ? with(o, false) : o,
                },
                new StyleOptionValue
                {
                    Token = "true",
                    OracleEndorsed = oracleEndorsed,
                    CorpusEndorsed = corpusEndorsed,
                    ConfigKey = configKey,
                    IsSelected = get,
                    SetSelected = with,
                },
            ],
        };

    // The guarded-boolean-return family as one multi-value axis. Its two non-default
    // values map onto the two independent byte-divergent lens properties, so config
    // resolution and printer behavior are unchanged: each value's SetSelected sets
    // only its own backing bool (last-write-wins per key), and GetValue reports the
    // oracle-endorsed conditional-expression first when both happen to be set — the
    // same "ternary wins" order the printer already applies.
    private static StyleOptionDescriptor GuardedBooleanReturnStyle()
        => new()
        {
            Id = "guarded-boolean-return-style",
            Title = "Guarded boolean return style",
            Summary = "How to render a declined guarded boolean return: the byte-faithful flat if/return (default), the oracle-preferred ternary return c ? A : B; (byte-divergent), or the compact short-circuit \"bool hack\" (byte-divergent, not oracle-endorsed).",
            Tier = StyleOptionTier.Lens,
            ByteDivergent = true,
            DefaultValue = GuardedReturnDefault,
            Values =
            [
                new StyleOptionValue
                {
                    Token = GuardedReturnDefault,
                    Title = "Flat if/return (byte-faithful)",
                    IsSelected = static o => !o.PreferConditionalExpressionReturn && !o.PreferBranchlessBoolean,
                    // Selecting the default value clears the whole axis; deselecting
                    // it is a no-op (a sibling selection clears it instead).
                    SetSelected = static (o, on) =>
                        on ? o with { PreferConditionalExpressionReturn = false, PreferBranchlessBoolean = false } : o,
                },
                new StyleOptionValue
                {
                    Token = GuardedReturnConditional,
                    Title = "Conditional expression (ternary)",
                    OracleEndorsed = true,
                    ConfigKey = "dotnet_style_prefer_conditional_expression_over_return",
                    IsSelected = static o => o.PreferConditionalExpressionReturn,
                    SetSelected = static (o, on) => o with { PreferConditionalExpressionReturn = on },
                },
                new StyleOptionValue
                {
                    Token = GuardedReturnBranchless,
                    Title = "Branchless \"bool hack\"",
                    OracleEndorsed = false,
                    ConfigKey = "dotnet_inspect_style_prefer_branchless_boolean",
                    IsSelected = static o => o.PreferBranchlessBoolean,
                    SetSelected = static (o, on) => o with { PreferBranchlessBoolean = on },
                },
            ],
        };

    // The var-spelling family as one value-domain axis. Its value domain is the
    // C# `var` decision as dotnet/runtime's editorconfig models it: an `explicit`
    // default (every csharp_style_var_* key false — the shipped, byte-stable
    // spelling the runtime prefers) plus three independent site-category values
    // mapping 1:1 onto the three csharp_style_var_* keys and their backing bools.
    //
    // The three categories partition declaration sites (built-in type, else
    // type-apparent, else elsewhere), so at most one governs any given site — the
    // printer classifies each site into one bucket and reads that bucket's bool.
    // Because they are independent keys (a user may enable any subset), each value's
    // SetSelected sets only its own bool and the `explicit` default's SetSelected
    // clears all three; GetValue reports the first enabled category in Values order
    // as a coarse summary (WithValue single-select is a picker affordance — the
    // authoritative state is the three independent bools set via config). None is
    // oracle-endorsed: dotnet/runtime's .editorconfig sets every csharp_style_var_*
    // key false (prefer explicit), so `var` never joins the "full taste" aggregate.
    // Byte-neutral (Spelling tier): `var` is compile-time inference with no IL
    // consequence, so the axis is IL-identical to the explicit spelling.
    private static StyleOptionDescriptor VarSpellingStyle()
        => new()
        {
            Id = "var-spelling-style",
            Title = "var vs. explicit type",
            Summary = "When to spell a local declaration with var instead of its explicit type: never (explicit, the default), for built-in types, when the type is apparent from the initializer, and/or elsewhere. Byte-neutral; the three var categories are independent, matching the csharp_style_var_* editorconfig keys.",
            Tier = StyleOptionTier.Spelling,
            ByteDivergent = false,
            DefaultValue = VarStyleExplicit,
            Values =
            [
                new StyleOptionValue
                {
                    Token = VarStyleExplicit,
                    Title = "Explicit type (byte-stable default)",
                    IsSelected = static o => !o.PreferVarForBuiltInTypes && !o.PreferVarWhenTypeApparent && !o.PreferVarElsewhere,
                    // Selecting the explicit default clears the whole axis; deselecting
                    // it is a no-op (a sibling selection clears it instead).
                    SetSelected = static (o, on) =>
                        on ? o with { PreferVarForBuiltInTypes = false, PreferVarWhenTypeApparent = false, PreferVarElsewhere = false } : o,
                },
                new StyleOptionValue
                {
                    Token = VarStyleBuiltInTypes,
                    Title = "var for built-in types",
                    OracleEndorsed = false,
                    ConfigKey = "csharp_style_var_for_built_in_types",
                    IsSelected = static o => o.PreferVarForBuiltInTypes,
                    SetSelected = static (o, on) => o with { PreferVarForBuiltInTypes = on },
                },
                new StyleOptionValue
                {
                    Token = VarStyleWhenApparent,
                    Title = "var when the type is apparent",
                    OracleEndorsed = false,
                    ConfigKey = "csharp_style_var_when_type_is_apparent",
                    IsSelected = static o => o.PreferVarWhenTypeApparent,
                    SetSelected = static (o, on) => o with { PreferVarWhenTypeApparent = on },
                },
                new StyleOptionValue
                {
                    Token = VarStyleElsewhere,
                    Title = "var elsewhere",
                    OracleEndorsed = false,
                    ConfigKey = "csharp_style_var_elsewhere",
                    IsSelected = static o => o.PreferVarElsewhere,
                    SetSelected = static (o, on) => o with { PreferVarElsewhere = on },
                },
            ],
        };
}
