using System.Collections.Generic;
using System.Linq;
using System.Reflection.PortableExecutable;

using ILInspector.Decompiler;
using ILInspector.Decompiler.Pipeline;
using ILInspector.DecompilerHarness;
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
/// near-miss lived here — so each is compiled back: rendering a firing specimen with
/// the knob on and off must produce the same recompiled IL. A new Spelling byte-neutral
/// knob with no firing specimen fails <see cref="EverySpellingByteNeutralKnob_HasAFiringSpecimen"/>.
/// </description></item>
/// <item><description>
/// The <see cref="StyleOptionTier.Formatting"/> (whitespace-only) and
/// <see cref="StyleOptionTier.Synthesis"/> (local-name-only) byte-neutral knobs change
/// neither the emitted tokens nor the metadata the IL is decoded from — layout is not
/// in the assembly at all, and a local's name lives in the PDB, never the method body.
/// Their byte-neutrality is structural, recorded and pinned by tier rather than
/// re-proven per knob.
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
    /// One firing specimen per Spelling-tier byte-neutral knob: the catalog value token
    /// to turn on, the declaring type and method whose decompiled body that token
    /// rewrites, and the harness signature that identifies the method for compile-back.
    /// </summary>
    sealed record FiringSpecimen(
        string KnobId,
        string ValueToken,
        System.Type DeclaringType,
        string Method,
        string Signature);

    static readonly IReadOnlyList<FiringSpecimen> Specimens =
    [
        new("qualify-field-access", "true",
            typeof(ThisQualificationSpecimen), nameof(ThisQualificationSpecimen.ReadField),
            "() -> corelib:System.Int32"),
        new("qualify-property-access", "true",
            typeof(ThisQualificationSpecimen), nameof(ThisQualificationSpecimen.ReadProperty),
            "() -> corelib:System.Int32"),
        new("qualify-method-access", "true",
            typeof(ThisQualificationSpecimen), nameof(ThisQualificationSpecimen.CallMethod),
            "() -> corelib:System.Int32"),
        new("qualify-event-access", "true",
            typeof(ThisQualificationSpecimen), nameof(ThisQualificationSpecimen.Subscribe),
            "(corelib:System.EventHandler) -> corelib:System.Void"),
        new("var-spelling-style", "var-when-type-apparent",
            typeof(VarWhenApparentSpecimen), nameof(VarWhenApparentSpecimen.ObjectCreation),
            "() -> corelib:System.Int32"),
    ];

    static IReadOnlyList<StyleOptionDescriptor> ByteNeutralKnobs =>
        StyleOptionCatalog.Options.Where(o => !o.ByteDivergent).ToArray();

    static StyleOptionDescriptor Knob(string id) =>
        StyleOptionCatalog.Options.Single(o => o.Id == id);

    // The knob's non-default state, built through the catalog descriptor (never a raw
    // property set) so the gate exercises the same value-domain plumbing a host uses.
    static PrinterOptions On(FiringSpecimen specimen) =>
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

    static FidelityCheck.CompileBackTarget Target(FiringSpecimen specimen) =>
        new(AssemblyPath, specimen.DeclaringType.FullName!, specimen.Method, Overload: 0, Signature: specimen.Signature);

    static string Key(FiringSpecimen specimen) => $"{specimen.DeclaringType.FullName}::{specimen.Method}";

    // One compile-back pass over a set of specimens under a single options set. The
    // (large) test assembly is decompiled up to the target types, so batching every
    // target into one pass keeps the gate to a few passes instead of one per knob.
    static IReadOnlyDictionary<string, FidelityCheck.CompileBackResult> CompileBackAll(
        IReadOnlyList<FiringSpecimen> specimens, PrinterOptions? options)
        => FidelityCheck.EvaluateTargets(
                [AssemblyPath], [.. specimens.Select(Target)], lowered: false, options)
            .ToDictionary(r => $"{r.Type}::{r.Method}", r => r, StringComparer.Ordinal);

    [Fact]
    public void EverySpellingByteNeutralKnob_HasAFiringSpecimen()
    {
        // Drift guard: the compile-back gate must cover every Spelling-tier byte-neutral
        // knob. A new one added without a firing specimen fails here, forcing the gate —
        // driven off the classification — to stay exhaustive over the tier whose
        // byte-neutrality is a semantic (token-binding) claim rather than structural.
        var required = ByteNeutralKnobs
            .Where(o => o.Tier == StyleOptionTier.Spelling)
            .Select(o => o.Id)
            .ToHashSet(StringComparer.Ordinal);
        var covered = Specimens.Select(s => s.KnobId).ToHashSet(StringComparer.Ordinal);
        Assert.Equal(required, covered);
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
    public void SpellingKnob_On_RewritesTheSpecimenTokens()
    {
        // Non-vacuity, fast (no compile-back): each knob, on alone, actually rewrites
        // its firing specimen's tokens. This is what makes the slow IL-identity proof
        // below a real check rather than a comparison of two identical renders.
        foreach (var specimen in Specimens)
        {
            var offText = Render(specimen.DeclaringType, specimen.Method, options: null);
            var onText = Render(specimen.DeclaringType, specimen.Method, On(specimen));
            Assert.NotEqual(offText, onText);
        }
    }

    [Fact]
    [Trait("Speed", "Slow")]
    public void SpellingKnob_On_RecompilesToIdenticalIl()
    {
        // The claim under test: a Spelling knob's rewritten output recompiles to the
        // same IL as the shipped default. Compare the recompiled opcode stream
        // (opcode-level identity) AND the compile-back contract-V1 verdict
        // (operand/branch-target identity) of the knob-on and knob-off renders.
        //
        // Batched to three passes over the large test assembly: one knob-off baseline
        // for all specimens, then one knob-on pass per declaring type. Turning a type's
        // knobs on together also exercises the same-line interaction the catalog flags
        // (qualification x var), while each method's site is rewritten by exactly one
        // knob, so the per-method comparison still isolates that knob's neutrality.
        var off = CompileBackAll(Specimens, options: null);

        foreach (var group in Specimens.GroupBy(s => s.DeclaringType))
        {
            var groupSpecimens = group.ToArray();
            var onOptions = groupSpecimens.Aggregate(
                PrinterOptions.Default, (o, s) => Knob(s.KnobId).WithValue(o, s.ValueToken));
            var on = CompileBackAll(groupSpecimens, onOptions);

            foreach (var specimen in groupSpecimens)
            {
                var offResult = off[Key(specimen)];
                var onResult = on[Key(specimen)];

                Assert.False(IsUncheckable(offResult.Status),
                    $"{specimen.KnobId}: knob-off render did not compile back ({offResult.Status}: {offResult.Detail}).");
                Assert.False(IsUncheckable(onResult.Status),
                    $"{specimen.KnobId}: knob-on render did not compile back ({onResult.Status}: {onResult.Detail}).");
                Assert.Equal(offResult.RecompiledOpcodes, onResult.RecompiledOpcodes);
                Assert.Equal(offResult.Status, onResult.Status);
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
