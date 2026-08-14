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
/// The product-owned presentation of one <see cref="StyleOptionTier"/>: the
/// category level of the catalog, sitting one level above
/// <see cref="StyleOptionDescriptor"/>. A host grouping the catalog by tier reads
/// its label, blurb, and display position from here instead of restating a
/// taxonomy the product already owns, so a knob in a newly added tier surfaces
/// without a consumer edit.
///
/// <para>The <see cref="Id"/> is the stable grouping key; <see cref="Title"/> and
/// <see cref="Summary"/> are presentation and may be reworded. Every tier has
/// exactly one descriptor, which
/// <c>StyleOptionCatalogTests.Tiers_CoverEveryTierExactlyOnce</c> enforces by set
/// equality against <see cref="StyleOptionTier"/> — a new enum value with no
/// descriptor fails there rather than silently vanishing from a picker.</para>
/// </summary>
public sealed record StyleOptionTierDescriptor
{
    /// <summary>
    /// The tier this describes. The enum token is the stable, never-localized
    /// grouping key a host keys presentation and persisted selections off.
    /// </summary>
    public required StyleOptionTier Id { get; init; }

    /// <summary>Short human-facing label for a group heading in a picker.</summary>
    public required string Title { get; init; }

    /// <summary>
    /// One-sentence statement of the fidelity contract every knob in this tier
    /// honors, so a host can explain a group and not merely name it.
    ///
    /// <para>This is user-facing copy, not internal prose: a picker renders it
    /// verbatim as escaped plain text, so it must read as a complete sentence
    /// without code formatting, and must avoid vocabulary that only means
    /// something inside this repository (name what a choice costs the reader,
    /// rather than naming the gate that measures it).</para>
    /// </summary>
    public required string Summary { get; init; }

    /// <summary>
    /// Explicit display position, ascending, most conservative contract first.
    /// First-class rather than an <see cref="StyleOptionTier"/> ordinal so the
    /// enum's declaration order and the presentation order can move
    /// independently.
    /// </summary>
    public required int Order { get; init; }

    /// <summary>
    /// <see langword="true"/> when every knob in this tier is byte-divergent —
    /// the tier-level statement of the contract
    /// <see cref="StyleOptionDescriptor.ByteDivergent"/> carries per knob. A host
    /// can warn about a whole group without inspecting its members.
    /// <c>StyleOptionCatalogTests.ByteDivergence_IsATierProperty</c> enforces the
    /// agreement in both directions, so
    /// <see cref="StyleOptionTier.Lens"/> being the only byte-divergent tier is a
    /// gated claim rather than a comment.
    /// </summary>
    public required bool ByteDivergent { get; init; }
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

    /// <summary>
    /// Stable persisted id for this selectable value, or <see langword="null"/>
    /// for the axis default, which is not an opt-in choice. This id is explicit
    /// rather than derived from the current number of sibling values: adding a
    /// second non-default value to an existing two-state knob must not rename the
    /// choice users already stored.
    /// </summary>
    public string? ChoiceId { get; init; }

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
/// One product-owned opt-in choice projected from a
/// <see cref="StyleOptionDescriptor"/> and one of its non-default values. This is
/// the host-facing picker contract: stable persisted identity, complete display
/// text and fidelity metadata, the owning option/value identities, and
/// single-select conflict semantics all come from the product catalog rather
/// than being reconstructed by each consumer.
/// </summary>
public sealed record StyleOptionChoice
{
    /// <summary>Stable persisted choice id (never localized).</summary>
    public required string Id { get; init; }

    /// <summary>The owning <see cref="StyleOptionDescriptor.Id"/>.</summary>
    public required string OptionId { get; init; }

    /// <summary>The selected <see cref="StyleOptionValue.Token"/>.</summary>
    public required string ValueToken { get; init; }

    /// <summary>Complete human-facing label for a flat picker row.</summary>
    public required string Title { get; init; }

    /// <summary>One-sentence description inherited from the owning option.</summary>
    public required string Summary { get; init; }

    /// <summary>The owning option's fidelity/presentation tier.</summary>
    public required StyleOptionTier Tier { get; init; }

    /// <summary>Whether selecting this choice may change emitted IL bytes.</summary>
    public required bool ByteDivergent { get; init; }

    /// <summary>Whether the declared runtime style oracle endorses this value.</summary>
    public required bool OracleEndorsed { get; init; }

    /// <summary>Whether the revealed runtime corpus endorses this value.</summary>
    public required bool CorpusEndorsed { get; init; }

    /// <summary>
    /// Product-owned single-select group, or <see langword="null"/> when the
    /// option currently has only one selectable value. Choices with the same
    /// non-null group conflict; the group is the owning option id.
    /// </summary>
    public string? ConflictGroup { get; init; }
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
/// product's NativeAOT constraint. Hosts consume the flattened
/// <see cref="StyleOptionCatalog.Choices"/> projection and resolve its stable ids
/// through <see cref="StyleOptionCatalog.ResolveChoices"/> rather than deriving
/// selectability, identity, or conflicts from <see cref="Values"/>.</para>
/// </summary>
public sealed record StyleOptionDescriptor
{
    /// <summary>Stable, kebab-case identifier for programmatic reference (never localized).</summary>
    public required string Id { get; init; }

    /// <summary>
    /// Optional config key whose value is one token from <see cref="Values"/>.
    /// This is the persistent spelling for a mutually-exclusive multi-value axis;
    /// boolean axes continue to put their key on the selected
    /// <see cref="StyleOptionValue"/>.
    /// </summary>
    public string? ValueConfigKey { get; init; }

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
    /// The token of the user-facing product-default value — the value in effect on
    /// <see cref="StyleOptionCatalog.DefaultOptions"/>. This may differ from the
    /// low-level <see cref="PrinterOptions.Default"/> used by fidelity and harness
    /// consumers. <see cref="GetValue"/> returns this when no value is selected.
    /// </summary>
    public required string DefaultValue { get; init; }

    /// <summary>
    /// The value domain this knob ranges over, in presentation order. The first
    /// entry is conventionally the off value for a boolean axis; the declared
    /// <see cref="DefaultValue"/> identifies the product default independently.
    /// For a multi-value axis the order is also the resolution
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
    /// <see langword="null"/> for an API-only or multi-value knob. Multi-value
    /// axes may use either per-value keys or <see cref="ValueConfigKey"/>.
    /// Convenience for the common boolean case
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
/// The catalog of every configurable <see cref="PrinterOptions"/> knob, exposed as the
/// shared source of truth for hosts (CLI config resolution, a Wasm UI, the "full
/// taste" aggregate) so option metadata lives in exactly one place and cannot
/// drift between the library and its consumers. Most knobs are two-state
/// (boolean) toggles; the guarded-boolean-return family is a single multi-value
/// axis whose value domain the descriptor carries directly.
/// </summary>
public static class StyleOptionCatalog
{
    private const string GuardedReturnId = "guarded-boolean-return-style";
    private const string VarStyleId = "var-spelling-style";
    private const string EnumLabelOrderId = "enum-case-label-order";

    // Value tokens for the guarded-boolean-return family axis.
    private const string GuardedReturnDefault = "flat";
    private const string GuardedReturnConditional = "conditional-expression";
    private const string GuardedReturnBranchless = "branchless";

    // Value tokens for the var-spelling family axis.
    private const string VarStyleExplicit = "explicit";
    private const string VarStyleBuiltInTypes = "var-for-built-in-types";
    private const string VarStyleWhenApparent = "var-when-type-apparent";
    private const string VarStyleElsewhere = "var-elsewhere";

    // Value tokens for shared-body enum case-label ordering.
    private const string EnumLabelAlphabetical = "alphabetical";
    private const string EnumLabelValue = "value";

    /// <summary>
    /// Every <see cref="StyleOptionTier"/> as a presentation descriptor, in
    /// display order (most conservative contract first). This is the category
    /// level of the catalog: a host renders a grouped picker by walking these and
    /// filtering <see cref="Options"/> on <see cref="StyleOptionDescriptor.Tier"/>,
    /// with no locally-held label, blurb, or ordering. The list is exhaustive and
    /// duplicate-free by gate (<c>StyleOptionCatalogTests</c>), so a tier added to
    /// the enum cannot silently drop out of a consumer's layout.
    /// </summary>
    public static IReadOnlyList<StyleOptionTierDescriptor> Tiers { get; } =
    [
        new StyleOptionTierDescriptor
        {
            Id = StyleOptionTier.Formatting,
            Title = "Formatting",
            Summary = "Layout only — whitespace and line breaks. The code itself is unchanged, and compiles to identical IL.",
            Order = 1,
            ByteDivergent = false,
        },
        new StyleOptionTierDescriptor
        {
            Id = StyleOptionTier.Spelling,
            Title = "Spelling",
            Summary = "An equally faithful way to spell the same code, such as qualifying a member with this or writing var. Compiles to identical IL.",
            Order = 2,
            ByteDivergent = false,
        },
        new StyleOptionTierDescriptor
        {
            Id = StyleOptionTier.Synthesis,
            Title = "Name synthesis",
            Summary = "Readable invented names for locals that have none of their own. Compiles to identical IL, but these names no longer match the ones the IL uses.",
            Order = 3,
            ByteDivergent = false,
        },
        new StyleOptionTierDescriptor
        {
            Id = StyleOptionTier.Lens,
            Title = "Style lenses",
            Summary = "Keeps what the code does, but not the exact bytes: this rendering recompiles to different IL than the shipped output, so it is excluded from byte-level fidelity checking.",
            Order = 4,
            ByteDivergent = true,
        },
    ];

    /// <summary>
    /// The presentation descriptor for <paramref name="tier"/>. Throws rather than
    /// returning null for an unregistered tier: the registry is exhaustive by
    /// gate, so a miss is a catalog defect and stays visible instead of degrading
    /// into an unlabeled group.
    /// </summary>
    public static StyleOptionTierDescriptor GetTier(StyleOptionTier tier)
    {
        foreach (var descriptor in Tiers)
            if (descriptor.Id == tier)
                return descriptor;

        throw new ArgumentOutOfRangeException(nameof(tier), tier, "No style-option tier descriptor is registered for this tier.");
    }

    /// <summary>
    /// Every configurable knob, in a stable presentation order (formatting and
    /// spelling first, then the byte-divergent lens). Two-state knobs carry a
    /// <c>false</c>/<c>true</c> value domain; the guarded-boolean-return knob is a
    /// single multi-value axis (<c>flat</c> / <c>conditional-expression</c> /
    /// <c>branchless</c>). The catalog is exhaustive: the drift-guard test asserts
    /// every backing <see cref="PrinterOptions"/> property is reachable through
    /// some descriptor value.
    /// </summary>
    public static IReadOnlyList<StyleOptionDescriptor> Options { get; } =
    [
        Boolean(
            id: "slot-local-names",
            title: "Use IL slot local names",
            summary: "Keep V_index for a local that has no PDB source name instead of synthesizing a readable name.",
            tier: StyleOptionTier.Synthesis,
            byteDivergent: false,
            oracleEndorsed: false,
            configKey: "dotnet_inspect_style_slot_local_names",
            get: static o => !o.ReadableLocalNames,
            with: static (o, v) => o with { ReadableLocalNames = !v }),
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
        EnumCaseLabelOrderStyle(),
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
    /// User-facing product defaults derived from the catalog. Hosts that present
    /// normal source output or initialize a style picker use this value; fidelity,
    /// corpus, and harness paths use <see cref="PrinterOptions.Default"/> directly
    /// to retain stable slot names and other low-level defaults.
    /// </summary>
    public static PrinterOptions DefaultOptions { get; } = ApplyDefaults();

    /// <summary>
    /// Every selectable non-default style value as a product-owned picker row, in
    /// stable option/value presentation order. Choice ids preserve the browser's
    /// existing persisted vocabulary: a lone non-default value keeps the option
    /// id, while existing multi-value choices use
    /// <c>option-id:value-token</c>. The ids are stored explicitly on values so
    /// future catalog growth cannot rename an existing choice.
    /// </summary>
    public static IReadOnlyList<StyleOptionChoice> Choices { get; } = CreateChoices();

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

    /// <summary>
    /// Resolves product-owned picker <see cref="Choices"/> ids into user-facing
    /// <see cref="DefaultOptions"/> plus the selected values. This is deliberately
    /// separate from the composable <see cref="StyleOptionValue.ConfigKey"/>
    /// vocabulary consumed by configuration files. Unknown ids and two distinct
    /// choices from one non-null
    /// <see cref="StyleOptionChoice.ConflictGroup"/> are rejected rather than
    /// silently producing default or order-dependent output. Duplicate copies of
    /// the same id are harmless, matching set semantics.
    /// </summary>
    public static PrinterOptions ResolveChoices(IEnumerable<string> choiceIds)
    {
        ArgumentNullException.ThrowIfNull(choiceIds);

        var options = DefaultOptions;
        var applied = new HashSet<string>(StringComparer.Ordinal);
        var selectedByGroup = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var id in choiceIds)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                throw new ArgumentException(
                    "A style choice id cannot be null or whitespace.",
                    nameof(choiceIds));
            }

            var choice = Choices.FirstOrDefault(
                candidate => string.Equals(candidate.Id, id, StringComparison.Ordinal));
            if (choice is null)
            {
                throw new ArgumentException(
                    $"'{id}' is not a style choice in the product catalog.",
                    nameof(choiceIds));
            }

            if (!applied.Add(id))
                continue;

            if (choice.ConflictGroup is { } group)
            {
                if (selectedByGroup.TryGetValue(group, out var existing))
                {
                    throw new ArgumentException(
                        $"Style choices '{existing}' and '{id}' conflict in group '{group}'.",
                        nameof(choiceIds));
                }

                selectedByGroup.Add(group, id);
            }

            var option = Options.Single(candidate =>
                string.Equals(candidate.Id, choice.OptionId, StringComparison.Ordinal));
            options = option.WithValue(options, choice.ValueToken);
        }

        return options;
    }

    private static IReadOnlyList<StyleOptionChoice> CreateChoices()
    {
        var choices = new List<StyleOptionChoice>();
        var ids = new HashSet<string>(StringComparer.Ordinal);

        foreach (var option in Options)
        {
            var selectable = option.Values
                .Where(value => !string.Equals(
                    value.Token,
                    option.DefaultValue,
                    StringComparison.Ordinal))
                .ToArray();
            string? conflictGroup = selectable.Length > 1 ? option.Id : null;

            foreach (var value in option.Values)
            {
                bool isDefault = string.Equals(
                    value.Token,
                    option.DefaultValue,
                    StringComparison.Ordinal);
                if (isDefault)
                {
                    if (value.ChoiceId is not null)
                    {
                        throw new InvalidOperationException(
                            $"Default value '{option.Id}:{value.Token}' cannot be a selectable style choice.");
                    }

                    continue;
                }

                if (string.IsNullOrWhiteSpace(value.ChoiceId))
                {
                    throw new InvalidOperationException(
                        $"Non-default value '{option.Id}:{value.Token}' has no stable style choice id.");
                }

                if (!ids.Add(value.ChoiceId))
                {
                    throw new InvalidOperationException(
                        $"Style choice id '{value.ChoiceId}' is registered more than once.");
                }

                choices.Add(new StyleOptionChoice
                {
                    Id = value.ChoiceId,
                    OptionId = option.Id,
                    ValueToken = value.Token,
                    Title = selectable.Length > 1
                        ? $"{option.Title} · {value.Title ?? value.Token}"
                        : option.Title,
                    Summary = option.Summary,
                    Tier = option.Tier,
                    ByteDivergent = option.ByteDivergent,
                    OracleEndorsed = value.OracleEndorsed,
                    CorpusEndorsed = value.CorpusEndorsed,
                    ConflictGroup = conflictGroup,
                });
            }
        }

        return choices;
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
                    ChoiceId = id,
                    OracleEndorsed = oracleEndorsed,
                    CorpusEndorsed = corpusEndorsed,
                    ConfigKey = configKey,
                    IsSelected = get,
                    SetSelected = with,
                },
            ],
        };

    private static PrinterOptions ApplyDefaults()
    {
        var options = PrinterOptions.Default;
        foreach (var knob in Options)
            options = knob.WithValue(options, knob.DefaultValue);
        return options;
    }

    // The guarded-boolean-return family as one multi-value axis. Its two non-default
    // values map onto the two independent byte-divergent lens properties, so config
    // resolution and printer behavior are unchanged: each value's SetSelected sets
    // only its own backing bool (last-write-wins per key), and GetValue reports the
    // oracle-endorsed conditional-expression first when both happen to be set — the
    // same "ternary wins" order the printer already applies.
    private static StyleOptionDescriptor GuardedBooleanReturnStyle()
        => new()
        {
            Id = GuardedReturnId,
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
                    ChoiceId = $"{GuardedReturnId}:{GuardedReturnConditional}",
                    Title = "Conditional expression (ternary)",
                    OracleEndorsed = true,
                    ConfigKey = "dotnet_style_prefer_conditional_expression_over_return",
                    IsSelected = static o => o.PreferConditionalExpressionReturn,
                    SetSelected = static (o, on) => o with { PreferConditionalExpressionReturn = on },
                },
                new StyleOptionValue
                {
                    Token = GuardedReturnBranchless,
                    ChoiceId = $"{GuardedReturnId}:{GuardedReturnBranchless}",
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
            Id = VarStyleId,
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
                    ChoiceId = $"{VarStyleId}:{VarStyleBuiltInTypes}",
                    Title = "var for built-in types",
                    OracleEndorsed = false,
                    ConfigKey = "csharp_style_var_for_built_in_types",
                    IsSelected = static o => o.PreferVarForBuiltInTypes,
                    SetSelected = static (o, on) => o with { PreferVarForBuiltInTypes = on },
                },
                new StyleOptionValue
                {
                    Token = VarStyleWhenApparent,
                    ChoiceId = $"{VarStyleId}:{VarStyleWhenApparent}",
                    Title = "var when the type is apparent",
                    OracleEndorsed = false,
                    ConfigKey = "csharp_style_var_when_type_is_apparent",
                    IsSelected = static o => o.PreferVarWhenTypeApparent,
                    SetSelected = static (o, on) => o with { PreferVarWhenTypeApparent = on },
                },
                new StyleOptionValue
                {
                    Token = VarStyleElsewhere,
                    ChoiceId = $"{VarStyleId}:{VarStyleElsewhere}",
                    Title = "var elsewhere",
                    OracleEndorsed = false,
                    ConfigKey = "csharp_style_var_elsewhere",
                    IsSelected = static o => o.PreferVarElsewhere,
                    SetSelected = static (o, on) => o with { PreferVarElsewhere = on },
                },
            ],
        };

    private static StyleOptionDescriptor EnumCaseLabelOrderStyle()
        => new()
        {
            Id = EnumLabelOrderId,
            ValueConfigKey = "dotnet_inspect_style_enum_case_label_order",
            Title = "Shared enum case-label order",
            Summary = "Order named enum labels that share one switch body alphabetically (default) or by recovered numeric value. Byte-neutral; mixed or unnamed labels keep value order.",
            Tier = StyleOptionTier.Spelling,
            ByteDivergent = false,
            DefaultValue = EnumLabelAlphabetical,
            Values =
            [
                new StyleOptionValue
                {
                    Token = EnumLabelAlphabetical,
                    Title = "Alphabetical by member name",
                    IsSelected = static o => o.EnumCaseLabelOrder == EnumCaseLabelOrder.Alphabetical,
                    SetSelected = static (o, on) => on
                        ? o with { EnumCaseLabelOrder = EnumCaseLabelOrder.Alphabetical }
                        : o,
                },
                new StyleOptionValue
                {
                    Token = EnumLabelValue,
                    ChoiceId = EnumLabelOrderId,
                    Title = "Recovered numeric value order",
                    IsSelected = static o => o.EnumCaseLabelOrder == EnumCaseLabelOrder.Value,
                    SetSelected = static (o, on) => on
                        ? o with { EnumCaseLabelOrder = EnumCaseLabelOrder.Value }
                        : o,
                },
            ],
        };
}
