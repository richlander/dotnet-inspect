namespace ILInspector.Decompiler.Pipeline;

/// <summary>
/// Opt-in render knobs for <see cref="CSharpPrinter"/>. Every field defaults to
/// the shipped behavior, so <see cref="Default"/> reproduces today's output
/// byte-for-byte — the fidelity gate, <c>--skip-pdb</c> deterministic reading,
/// and annotated-IL alignment all depend on that. This is the single home for
/// render-quality options (readable names today; declaration placement and other
/// knobs as the #998 row grows), keeping them off the printer's positional
/// signature.
/// </summary>
public sealed record PrinterOptions
{
    /// <summary>
    /// When set, a local with no usable PDB source name renders a synthesized
    /// readable name (see <see cref="LocalNameSynthesizer"/>) instead of
    /// <c>V_index</c>. Off by default; the synthesizer falls back to
    /// <c>V_index</c> for any local it cannot name from IR evidence.
    /// </summary>
    public bool ReadableLocalNames { get; init; }

    /// <summary>
    /// When set, an expression-bodied member or accessor wraps the <c>=&gt;</c>
    /// arrow onto the next line (indented one level deeper than the declaration
    /// head) instead of keeping <c>head =&gt; expr;</c> on one line. Off by
    /// default (same line is the shipped default); a whitespace-only formatting
    /// choice that leaves the tokens and IL unchanged.
    /// </summary>
    public bool WrapExpressionBodyArrow { get; init; }

    /// <summary>
    /// When set, a long, splittable expression — today a short-circuit
    /// <c>&amp;&amp;</c>/<c>||</c> boolean chain — whose single-line form would
    /// exceed the fluent-chain wrap width breaks each operand onto its own
    /// continuation line (operator trailing each broken line) instead of one very
    /// wide line. Off by default; this is a whitespace-only formatting
    /// tiebreaker, so the broken form is token-identical to the inline form and
    /// the IL is unchanged (the boolean analog of the always-on fluent-chain
    /// wrapper).
    /// </summary>
    public bool WrapSplittableExpressions { get; init; }

    /// <summary>
    /// When set, an instance field accessed through <c>this</c> renders the explicit
    /// <c>this.</c> qualifier even where the bare name is unambiguous (the shipped
    /// default qualifies only to escape a local/parameter shadow or a member/type
    /// name collision). IL-identical — <c>this.field</c> and <c>field</c> both emit
    /// <c>ldarg.0; ldfld</c>, so the qualified form is a spelling choice with no
    /// anchor. Off by default. Mirrors <c>dotnet_style_qualification_for_field</c>.
    /// </summary>
    public bool QualifyFieldAccess { get; init; }

    /// <summary>
    /// When set, an instance property accessed through <c>this</c> renders the
    /// explicit <c>this.</c> qualifier even where the bare name is unambiguous.
    /// IL-identical — <c>this.Prop</c> and <c>Prop</c> both emit
    /// <c>ldarg.0; call get_Prop</c>. Off by default. Mirrors
    /// <c>dotnet_style_qualification_for_property</c>.
    /// </summary>
    public bool QualifyPropertyAccess { get; init; }

    /// <summary>
    /// <summary>
    /// When set, an instance method invoked through <c>this</c> renders the explicit
    /// <c>this.</c> qualifier even where the bare name is unambiguous. IL-identical —
    /// <c>this.M()</c> and <c>M()</c> both emit <c>ldarg.0; call/callvirt</c>. A
    /// genuine non-virtual base call still renders <c>base.M()</c> (qualifying it
    /// with <c>this.</c> would re-enable virtual dispatch). Off by default. Mirrors
    /// <c>dotnet_style_qualification_for_method</c>.
    /// </summary>
    public bool QualifyMethodAccess { get; init; }

    /// <summary>
    /// When set, an instance event subscribed through <c>this</c> renders the
    /// explicit <c>this.</c> qualifier even where the bare name is unambiguous.
    /// IL-identical — <c>this.E += h</c> and <c>E += h</c> both emit
    /// <c>ldarg.0; call add_E</c>. Off by default. Mirrors
    /// <c>dotnet_style_qualification_for_event</c>.
    /// </summary>
    public bool QualifyEventAccess { get; init; }

    /// <summary>
    /// When set, a guarded boolean return the default view must render as a flat
    /// <c>if (c) return A; return B;</c> — because no short-circuit fold is
    /// opcode-faithful for that shape (see <c>ShortCircuitFidelity</c> / #3114) —
    /// is instead rendered as the conditional expression <c>return c ? A : B;</c>.
    /// This is the runtime <c>.editorconfig</c> IDE0046-preferred spelling
    /// (<c>dotnet_style_prefer_conditional_expression_over_return</c>).
    ///
    /// Unlike every other knob on this record, this one is <b>byte-divergent</b>:
    /// the ternary recompiles to a different branch stream than the original (a
    /// polarity flip and block reorder), so it is <b>not</b> opcode-faithful. It
    /// is the first opt-in <b>style lens</b> (#3138): the rewrite is the canonical
    /// desugaring of the guarded return, so it is unconditionally
    /// <b>behavior-preserving</b>, but the output must not be fed the compile-back
    /// fidelity gates. Off by default; the default view stays byte-faithful.
    /// </summary>
    public bool PreferConditionalExpressionReturn { get; init; }

    /// <summary>
    /// When set, a guarded boolean return with a constant arm the default view
    /// must render as a flat <c>if (c) return A; return B;</c> — because the
    /// short-circuit fold is not opcode-faithful for that shape (see
    /// <c>ShortCircuitFidelity</c> / #3114) — is instead rendered as the compact
    /// short-circuit "bool hack" (<c>return c &amp;&amp; A;</c>,
    /// <c>return c || X;</c>, <c>return !c &amp;&amp; X;</c>,
    /// <c>return !c || A;</c>).
    ///
    /// Like <see cref="PreferConditionalExpressionReturn"/> this is a
    /// <b>byte-divergent</b> opt-in <b>style lens</b> (#3138): the short-circuit
    /// spelling keeps the same condition, surviving operand, and short-circuit
    /// order, so it is unconditionally <b>behavior-preserving</b>, but it is not
    /// opcode-faithful (a bare operand recompiles branchless, a negation flips
    /// polarity), so its output must not feed the compile-back fidelity gates.
    ///
    /// Unlike the ternary lens this form is <b>not</b> oracle-endorsed
    /// (dotnet/runtime's <c>.editorconfig</c> would never recommend it), so it is a
    /// user compactness preference, opt-in only, and never part of a "full taste"
    /// aggregate. When both this and
    /// <see cref="PreferConditionalExpressionReturn"/> are set the oracle-endorsed
    /// ternary wins (it consumes the shape first). Off by default; the default view
    /// stays byte-faithful.
    /// </summary>
    public bool PreferBranchlessBoolean { get; init; }

    /// <summary>The shipped defaults — every knob off.</summary>
    public static PrinterOptions Default { get; } = new();
}
