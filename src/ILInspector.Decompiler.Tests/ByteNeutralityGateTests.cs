using System.Collections.Generic;
using System.Linq;
using System.Reflection.PortableExecutable;

using ILInspector.Decompiler;
using ILInspector.Decompiler.Pipeline;
using ILInspector.DecompilerHarness;
using ILInspector.Instructions;
using ILInspector.Metadata;

namespace ILInspector.Decompiler.Tests;

/// <summary>
/// The byte-neutrality gate (#3247). The <see cref="StyleOptionCatalog"/> classifies
/// every opt-in knob as <see cref="StyleOptionDescriptor.ByteDivergent"/> true/false,
/// and that bit selects the fidelity contract the knob's output is held to. The
/// byte-neutral (<c>ByteDivergent = false</c>) knobs claim IL-identity — the emitted
/// form recompiles to the same IL as the shipped default — but nothing exercised that
/// claim: <see cref="StyleOptionCatalogTests"/> only checks the classification agrees
/// with itself, and the quality harness never set an option.
///
/// <para>
/// This gate closes that hole and is driven off the classification, not a hand list,
/// so a new byte-neutral knob is covered automatically:
/// </para>
/// <list type="bullet">
/// <item><description>
/// The <see cref="StyleOptionTier.Spelling"/> byte-neutral knobs change emitted C#
/// <em>tokens</em> (a <c>this.</c> qualifier, a <c>var</c> inference) the compiler must
/// bind to the same IL. That is a semantic claim — the <c>var x = new()</c> → CS8754
/// near-miss lived here — so each emitting value is compiled back: rendering a firing
/// specimen with the value on and off must both recompile to the original IL. A new
/// byte-neutral value with no specimen fails
/// <see cref="EveryByteNeutralValue_HasASpecimen"/>.
/// </description></item>
/// <item><description>
/// The <see cref="StyleOptionTier.Formatting"/> (whitespace-only) byte-neutral knobs
/// change layout the assembly never stored. That is still checkable without a compiler:
/// rendering a firing specimen with the knob on and off must differ (the knob fired) yet
/// collapse to the same text once insignificant whitespace is removed — a fast lane the
/// reviewer valued. A knob mis-classified as Formatting that actually moved a token would
/// fail the whitespace-equality assertion. See <see cref="FormattingValue_On_ChangesOnlyWhitespace"/>.
/// </description></item>
/// <item><description>
/// The one <see cref="StyleOptionTier.Synthesis"/> byte-neutral knob (readable local
/// names) renames a local, and a local's name lives in the PDB, never the method body,
/// so it is byte-neutral by construction. It also cannot fire through the member-body
/// entrypoint when a source name is present — the test assembly's embedded PDB supplies
/// one — so the gate pins its inert render (on == off) on a source-named local. Firing
/// the synthesis (proving IL-identity by compile-back, since names are absent from the
/// IL) needs a name-less compiler temporary this corpus cannot author deterministically;
/// that is tracked as follow-up.
/// </description></item>
/// </list>
///
/// <para>
/// The complement — the <see cref="StyleOptionTier.Lens"/> (<c>ByteDivergent = true</c>)
/// knobs — is deliberately excluded from this byte gate: a lens is behavior-preserving
/// but <em>not</em> opcode-faithful, so it earns a behavioral (100%) gate instead of a
/// byte gate. Those live in <see cref="PreferConditionalReturnLensTests"/> and
/// <see cref="PreferBranchlessBooleanLensTests"/>, which pin executed runtime
/// equivalence over every input. <see cref="ByteDivergentKnobs_AreExcludedAndBehaviorGated"/>
/// records that contract.
/// </para>
/// </summary>
[Trait("Area", "Fidelity")]
public sealed class ByteNeutralityGateTests
{
    static string AssemblyPath => typeof(ByteNeutralityGateTests).Assembly.Location;

    /// <summary>
    /// One specimen per non-default value of a byte-neutral knob: the catalog value
    /// token to turn on, the declaring type and method whose decompiled body that token
    /// governs, and how that value's byte-neutrality is proven. A knob's value domain —
    /// not the knob — is the coverage unit, so a multi-value axis (e.g. the three
    /// <c>var</c> categories) needs one specimen per category, not one per axis.
    ///
    /// <para>
    /// <see cref="Proof"/> selects the check the value's neutrality is held to and must
    /// agree with the knob's tier (asserted by <see cref="EveryByteNeutralValue_HasASpecimen"/>):
    /// a Spelling or Synthesis value rewrites tokens or metadata the compiler binds, so
    /// it is <see cref="NeutralityProof.CompileBack"/>; a Formatting value only moves
    /// whitespace, so it is <see cref="NeutralityProof.Whitespace"/>.
    /// </para>
    ///
    /// <para>
    /// <see cref="Emits"/> distinguishes a value the printer actually consumes today (the
    /// render changes, so the value is exercised) from one that renders identically to
    /// the default — either a catalog value not yet wired into emission (the deferred var
    /// buckets) or a knob that is inert on this corpus (readable-local-names, whose
    /// synthesis a present PDB source name suppresses). The inert values are pinned as
    /// no-ops on an input they <em>would</em> govern once active, so the day emission
    /// lands (or a name-less local appears) the pin flips and forces an emitting specimen.
    /// </para>
    /// </summary>
    sealed record ValueSpecimen(
        string KnobId,
        string ValueToken,
        System.Type DeclaringType,
        string Method,
        NeutralityProof Proof,
        string Signature = "",
        bool Emits = true,
        FidelityCheck.CompileBackStatus ExpectedBaseline = FidelityCheck.CompileBackStatus.Exact);

    enum NeutralityProof
    {
        // Compile the value's on/off render back and prove IL-identity (Spelling, Synthesis).
        CompileBack,
        // Prove the value's on/off render differs only in insignificant whitespace (Formatting).
        Whitespace,
    }

    static readonly IReadOnlyList<ValueSpecimen> Specimens =
    [
        new("qualify-field-access", "true",
            typeof(ThisQualificationSpecimen), nameof(ThisQualificationSpecimen.ReadField),
            NeutralityProof.CompileBack, "() -> corelib:System.Int32"),
        new("qualify-property-access", "true",
            typeof(ThisQualificationSpecimen), nameof(ThisQualificationSpecimen.ReadProperty),
            NeutralityProof.CompileBack, "() -> corelib:System.Int32"),
        new("qualify-method-access", "true",
            typeof(ThisQualificationSpecimen), nameof(ThisQualificationSpecimen.CallMethod),
            NeutralityProof.CompileBack, "() -> corelib:System.Int32"),
        // The event-subscription decompilation over-renders into a benign OpcodeDiff
        // baseline (independent of the this. qualifier), so its knob-off compile-back is
        // anchored at OpcodeDiff, not Exact; the gate proves the knob-on render deviates
        // from the original identically to knob-off, not that either reaches Exact.
        new("qualify-event-access", "true",
            typeof(ThisQualificationSpecimen), nameof(ThisQualificationSpecimen.Subscribe),
            NeutralityProof.CompileBack, "(corelib:System.EventHandler) -> corelib:System.Void",
            ExpectedBaseline: FidelityCheck.CompileBackStatus.OpcodeDiff),
        new("var-spelling-style", "var-when-type-apparent",
            typeof(VarWhenApparentSpecimen), nameof(VarWhenApparentSpecimen.ObjectCreation),
            NeutralityProof.CompileBack, "() -> corelib:System.Int32"),
        // Declared-but-unwired var categories (deferred #3169). Pinned as no-ops on the
        // input each would govern once emission lands: a built-in-type object creation
        // and a not-apparent local. When the bucket is wired these renders diverge and
        // ValueTokenState_MatchesEmits fails, forcing an emitting specimen.
        new("var-spelling-style", "var-for-built-in-types",
            typeof(VarWhenApparentSpecimen), nameof(VarWhenApparentSpecimen.BuiltInObjectCreation),
            NeutralityProof.CompileBack, "() -> corelib:System.Int32", Emits: false),
        new("var-spelling-style", "var-elsewhere",
            typeof(VarWhenApparentSpecimen), nameof(VarWhenApparentSpecimen.NotApparent),
            NeutralityProof.CompileBack, "() -> corelib:System.Int32", Emits: false),
        // Synthesis: readable-local-names renames a local, and a name lives in the PDB,
        // not the IL. It is inert here (the embedded PDB names this local), so it is
        // pinned as a no-op; when a name-less local makes it fire, the pin flips.
        new("readable-local-names", "true",
            typeof(FormattingSynthesisSpecimen), nameof(FormattingSynthesisSpecimen.ReadableLocal),
            NeutralityProof.CompileBack, "() -> corelib:System.Int32", Emits: false),
        // Formatting: whitespace-only knobs, each on a specimen it wraps or flattens.
        new("wrap-expression-body-arrow", "true",
            typeof(FormattingSynthesisSpecimen), nameof(FormattingSynthesisSpecimen.ArrowBody),
            NeutralityProof.Whitespace),
        new("wrap-splittable-expressions", "true",
            typeof(FormattingSynthesisSpecimen), nameof(FormattingSynthesisSpecimen.LongLogicalChain),
            NeutralityProof.Whitespace),
        new("disable-one-liner-wrapping", "true",
            typeof(FormattingSynthesisSpecimen), nameof(FormattingSynthesisSpecimen.LongFluentChain),
            NeutralityProof.Whitespace),
    ];


    static IReadOnlyList<StyleOptionDescriptor> ByteNeutralKnobs =>
        StyleOptionCatalog.Options.Where(o => !o.ByteDivergent).ToArray();

    static StyleOptionDescriptor Knob(string id) =>
        StyleOptionCatalog.Options.Single(o => o.Id == id);

    // The knob's non-default state, built through the catalog descriptor (never a raw
    // property set) so the gate exercises the same value-domain plumbing a host uses.
    static PrinterOptions On(ValueSpecimen specimen) =>
        Knob(specimen.KnobId).WithValue(PrinterOptions.Default, specimen.ValueToken);

    static string Render(System.Type declaringType, string member, PrinterOptions? options)
    {
        using var pe = new PEReader(File.OpenRead(AssemblyPath));
        var api = ApiSurfaceExtractor.Extract(pe);
        var type = Assert.Single(api.Types, t => t.FullName == declaringType.FullName);
        var m = Assert.Single(type.Members, x => x.Name == member);
        var rendered = MemberBodyProducer.ProduceMember(type, m, AssemblyPath, pdbPath: null, printerOptions: options);
        Assert.Equal(MemberBodyProductionStatus.Complete, rendered.Status);
        Assert.NotNull(rendered.Text);
        return rendered.Text!;
    }

    static FidelityCheck.CompileBackTarget Target(ValueSpecimen specimen) =>
        new(AssemblyPath, specimen.DeclaringType.FullName!, specimen.Method, Overload: 0, Signature: specimen.Signature);

    static string Key(ValueSpecimen specimen) => $"{specimen.DeclaringType.FullName}::{specimen.Method}";

    // One compile-back pass over a set of specimens under a single options set. The
    // (large) test assembly is decompiled up to the target types, so batching every
    // target into one pass keeps the gate to a few passes instead of one per knob.
    static IReadOnlyDictionary<string, FidelityCheck.CompileBackResult> CompileBackAll(
        IReadOnlyList<ValueSpecimen> specimens, PrinterOptions? options)
        => FidelityCheck.EvaluateTargets(
                [AssemblyPath], [.. specimens.Select(Target)], lowered: false, options)
            .ToDictionary(r => $"{r.Type}::{r.Method}", r => r, StringComparer.Ordinal);

    // Insignificant-whitespace normalization: keep a whitespace run only where it
    // separates two word characters (so `int alpha` never collapses to `intalpha`, which
    // would mask a token change), and drop it everywhere else. Two renders that differ
    // only in layout normalize equal; a render that moved a real token does not.
    static string NormalizeWhitespace(string text)
    {
        var significant = System.Text.RegularExpressions.Regex.Replace(text, @"(?<=\w)\s+(?=\w)", " ");
        return System.Text.RegularExpressions.Regex.Replace(significant, @"\s+", "").Trim();
    }

    // Every non-default value token of a byte-neutral knob, across all tiers — the
    // coverage unit the drift guard and value-state tests are keyed on.
    static IReadOnlyList<(string KnobId, string ValueToken)> ByteNeutralNonDefaultValues =>
        ByteNeutralKnobs
            .SelectMany(o => o.Values
                .Where(v => v.Token != o.DefaultValue)
                .Select(v => (o.Id, v.Token)))
            .ToArray();

    // The proof a knob's tier is entitled to: token/metadata-changing tiers are compiled
    // back; whitespace-only Formatting is checked by normalized-text equality.
    static NeutralityProof ProofForTier(StyleOptionTier tier) => tier switch
    {
        StyleOptionTier.Formatting => NeutralityProof.Whitespace,
        _ => NeutralityProof.CompileBack,
    };

    [Fact]
    public void EveryByteNeutralValue_HasASpecimen()
    {
        // Drift guard: the gate must cover every non-default value of every byte-neutral
        // knob — the value, not the knob, is the byte-neutrality unit, so a multi-value
        // axis (the three var categories) needs one specimen per category. A new value
        // added without a specimen fails here, keeping the gate — driven off the
        // classification — exhaustive over the whole byte-neutral set.
        var required = ByteNeutralNonDefaultValues.ToHashSet();
        var covered = Specimens.Select(s => (s.KnobId, s.ValueToken)).ToHashSet();
        Assert.Equal(required, covered);

        // Tier/proof agreement: a specimen must be checked by the proof its knob's tier
        // entitles it to. This is what catches a mis-tiered knob — tagging a
        // token-changing knob Formatting would demand a whitespace specimen it cannot
        // satisfy (its render moves a token, so NormalizeWhitespace stays unequal), and
        // tagging a whitespace knob Spelling would demand a compile-back it does not need.
        foreach (var specimen in Specimens)
        {
            var tier = Knob(specimen.KnobId).Tier;
            Assert.Equal(ProofForTier(tier), specimen.Proof);
        }
    }

    [Fact]
    public void ByteNeutralKnobs_AreOnlyFormattingSpellingOrSynthesis()
    {
        // The gate's account of the byte-neutral set: Spelling knobs are compiled back
        // (below); Formatting and Synthesis knobs are byte-neutral by construction
        // (layout is absent from the assembly; a local name lives in the PDB, not the
        // method body). No other tier may be byte-neutral, so the two accounts together
        // cover the whole classification.
        Assert.All(ByteNeutralKnobs, o => Assert.True(
            o.Tier is StyleOptionTier.Formatting or StyleOptionTier.Spelling or StyleOptionTier.Synthesis,
            $"Byte-neutral knob '{o.Id}' has unexpected tier {o.Tier}; it needs a compile-back gate or a structural-neutrality rationale."));
    }

    [Fact]
    public void ValueTokenState_MatchesEmits()
    {
        // Non-vacuity + wiring pin, fast (no compile-back). An emitting value must
        // actually change its specimen's render (off != on) — this is what makes the
        // neutrality proofs below real checks rather than comparisons of two identical
        // renders. An inert value (a declared-but-unwired var bucket, or readable local
        // names when a source name is present) must render identically to the default
        // (off == on) on the input it would govern; when the value becomes active that
        // equality breaks and this test forces it into the emitting set.
        foreach (var specimen in Specimens)
        {
            var offText = Render(specimen.DeclaringType, specimen.Method, options: null);
            var onText = Render(specimen.DeclaringType, specimen.Method, On(specimen));
            if (specimen.Emits)
                Assert.NotEqual(offText, onText);
            else
                Assert.True(offText == onText,
                    $"{specimen.KnobId}={specimen.ValueToken} is marked inert (Emits:false) but its render changed; " +
                    $"the value is now active — add a firing specimen and set Emits:true so it is proven.");
        }
    }

    [Fact]
    public void FormattingValue_On_ChangesOnlyWhitespace()
    {
        // The Formatting-tier claim: a whitespace-only knob moves layout the assembly
        // never stored, so it cannot change the IL. Checked fast, without a compiler:
        // the on/off renders must differ (the knob fired — ValueTokenState_MatchesEmits
        // pins that too, re-asserted here for locality) yet be equal once insignificant
        // whitespace is normalized away. A knob mis-classified as Formatting that moved a
        // real token would leave the normalized texts unequal and fail here.
        foreach (var specimen in Specimens.Where(s => s.Proof == NeutralityProof.Whitespace))
        {
            var offText = Render(specimen.DeclaringType, specimen.Method, options: null);
            var onText = Render(specimen.DeclaringType, specimen.Method, On(specimen));
            var label = $"{specimen.KnobId}={specimen.ValueToken}";

            Assert.NotEqual(offText, onText);
            Assert.True(NormalizeWhitespace(offText) == NormalizeWhitespace(onText),
                $"{label}: knob-on render differs from knob-off in more than whitespace — the knob " +
                $"is tagged Formatting but moved a token, so it is not byte-neutral by layout alone.");
        }
    }

    [Fact]
    [Trait("Speed", "Slow")]
    public void CompileBackValue_On_RecompilesToTheSameIlAsOff()
    {
        // The claim under test: an emitting compile-back value's rewritten output
        // recompiles to the same IL as the shipped default. Proving that soundly means
        // comparing the two recompiled bodies to EACH OTHER, not each to the original: a
        // coarser off-vs-on comparison of status + opcode names would accept two renders
        // that both diverge from the original (and each other) in the same category —
        // e.g. two OperandDiff renders with different operands.
        //
        // The harness compiles each render back and diffs it against the shared original
        // under compile-back contract V1 (a complete LCS operand/branch-target diff). If
        // the knob-off and knob-on renders produce the SAME contract-V1 diff against that
        // one original, they deviate from it identically and are therefore IL-identical to
        // each other — even when the shared baseline is itself a benign over-render (the
        // event-subscription decompilation is an OpcodeDiff, not Exact, independent of the
        // `this.` qualifier). Comparing under the same contract the product's own fidelity
        // claims use keeps the gate consistent with those claims.
        //
        // Batched over the large test assembly: one knob-off baseline for all emitting
        // specimens, then one knob-on pass per declaring type. Turning a type's values on
        // together also exercises the same-line interaction the catalog flags
        // (qualification x var), while each method's site is governed by exactly one
        // value, so the per-method verdict still isolates that value's neutrality.
        var emitting = Specimens.Where(s => s.Proof == NeutralityProof.CompileBack && s.Emits).ToArray();
        var off = CompileBackAll(emitting, options: null);

        foreach (var group in emitting.GroupBy(s => s.DeclaringType))
        {
            var groupSpecimens = group.ToArray();
            var onOptions = groupSpecimens.Aggregate(
                PrinterOptions.Default, (o, s) => Knob(s.KnobId).WithValue(o, s.ValueToken));
            var on = CompileBackAll(groupSpecimens, onOptions);

            foreach (var specimen in groupSpecimens)
            {
                var offResult = off[Key(specimen)];
                var onResult = on[Key(specimen)];
                var label = $"{specimen.KnobId}={specimen.ValueToken}";

                Assert.False(IsUncheckable(offResult.Status),
                    $"{label}: knob-off render did not compile back ({offResult.Status}: {offResult.Detail}).");
                Assert.False(IsUncheckable(onResult.Status),
                    $"{label}: knob-on render did not compile back ({onResult.Status}: {onResult.Detail}).");

                // Baseline anchor: the knob-off render must reach the specific compile-back
                // status this specimen is expected to (Exact for most; the event specimen's
                // benign OpcodeDiff). Without this the off-vs-on equality below would still
                // pass if the baseline silently degraded (e.g. an unrelated regression made
                // both renders OperandDiff), so pin the strongest status the baseline earns.
                Assert.True(offResult.Status == specimen.ExpectedBaseline,
                    $"{label}: knob-off compile-back baseline is {offResult.Status}, expected " +
                    $"{specimen.ExpectedBaseline}. A changed baseline can hide an off-vs-on match that " +
                    $"is only equal because both renders regressed; re-establish or update the anchor.");

                var offDiff = offResult.FidelityDiff;
                var onDiff = onResult.FidelityDiff;
                Assert.True(offDiff is { IsAvailable: true },
                    $"{label}: knob-off compile-back fidelity is unavailable, so IL identity cannot be proven.");
                Assert.True(onDiff is { IsAvailable: true },
                    $"{label}: knob-on compile-back fidelity is unavailable, so IL identity cannot be proven.");

                // Opcode-level identity of the two recompiled bodies (fast belt).
                Assert.Equal(offResult.RecompiledOpcodes, onResult.RecompiledOpcodes);

                // Operand/branch-target identity: both recompiled bodies deviate from the
                // shared original identically under contract V1, hence equal each other.
                Assert.Equal(offDiff!.Outcome, onDiff!.Outcome);
                Assert.True(offDiff.Rows.SequenceEqual(onDiff.Rows),
                    $"{label}: knob-on recompiled body diverges from knob-off at the operand or " +
                    $"branch-target level (contract-V1 diff-vs-original differs between the two renders).");
            }
        }

        static bool IsUncheckable(FidelityCheck.CompileBackStatus status) =>
            status is FidelityCheck.CompileBackStatus.RecompileFail
                or FidelityCheck.CompileBackStatus.ContextFail
                or FidelityCheck.CompileBackStatus.FidelityUnavailable;
    }

    [Fact]
    public void ByteDivergentKnobs_AreExcludedAndBehaviorGated()
    {
        // The complement of the byte gate. A byte-divergent knob is behavior-preserving
        // but not opcode-faithful, so it is excluded from this byte gate by design and
        // instead held to a behavioral (100%) contract — pinned by executed runtime
        // equivalence in PreferConditionalReturnLensTests / PreferBranchlessBooleanLensTests.
        var byteDivergent = StyleOptionCatalog.Options.Where(o => o.ByteDivergent).ToArray();
        Assert.NotEmpty(byteDivergent);
        Assert.All(byteDivergent, o => Assert.Equal(StyleOptionTier.Lens, o.Tier));

        // None of them is in the byte gate's covered (Spelling) set.
        var covered = Specimens.Select(s => s.KnobId).ToHashSet(StringComparer.Ordinal);
        Assert.All(byteDivergent, o => Assert.DoesNotContain(o.Id, covered));
    }
}
