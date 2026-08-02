using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

namespace ILInspector.Instructions.Tests;

public class IlBodyDiffNormalizationTests
{
    // Derived from the enum rather than restated, so a normalization added
    // without coverage here still flows into every AllNormalizations test.
    static readonly IlBodyDiffNormalization AllNormalizations =
        Enum.GetValues<IlBodyDiffNormalization>()
            .Aggregate(IlBodyDiffNormalization.None, (all, option) => all | option);

    /// <summary>
    /// Every declared option must be accepted by <see cref="IlBodyDiff.Compare"/>,
    /// which rejects any flag outside its internal <c>SupportedNormalizations</c>
    /// mask. This is the wiring gate: declaring an enum member without adding it
    /// to that mask makes every caller that requests it throw, and this fails
    /// rather than letting the gap surface at a call site.
    /// </summary>
    [Fact]
    public void EveryDeclaredNormalization_IsAcceptedByCompare()
    {
        var body = Decode([0x2a]); // ret

        foreach (var option in Enum.GetValues<IlBodyDiffNormalization>())
        {
            var result = Record.Exception(() => IlBodyDiff.Compare(body, body, option));
            Assert.True(result is null, $"{option} was rejected by Compare: {result?.Message}");
            }
    }

    [Fact]
    public void NormalizeVariableLayout_ToleratesLocalMacroAndSlotLayout()
    {
        var macro = Decode([0x06, 0x2a]); // ldloc.0; ret
        var explicitSlot = Decode([0x11, 0x07, 0x2a]); // ldloc.s 7; ret

        Assert.False(IlBodyDiff.Compare(macro, explicitSlot).IsExact);
        Assert.True(IlBodyDiff.Compare(
            macro,
            explicitSlot,
            IlBodyDiffNormalization.NormalizeVariableLayout).IsExact);
    }

    [Fact]
    public void NormalizeVariableLayout_ToleratesArgumentMacroAndSlotLayout()
    {
        var macro = Decode([0x02, 0x2a]); // ldarg.0; ret
        var explicitSlot = Decode([0x0e, 0x07, 0x2a]); // ldarg.s 7; ret

        Assert.False(IlBodyDiff.Compare(macro, explicitSlot).IsExact);
        Assert.True(IlBodyDiff.Compare(
            macro,
            explicitSlot,
            IlBodyDiffNormalization.NormalizeVariableLayout).IsExact);
    }

    [Fact]
    public void NormalizeVariableLayout_DoesNotFoldArgumentValueAndAddressLoads()
    {
        var valueLoad = Decode([0x02, 0x2a]); // ldarg.0; ret
        var addressLoad = Decode([0x0f, 0x00, 0x2a]); // ldarga.s 0; ret

        var diff = IlBodyDiff.Compare(
            valueLoad,
            addressLoad,
            IlBodyDiffNormalization.NormalizeVariableLayout);

        Assert.False(diff.IsExact);
        Assert.Contains(diff.Rows, row => row.Operation.OpcodeFamily == "ldarg");
        Assert.Contains(diff.Rows, row => row.Operation.OpcodeFamily == "ldarga");
    }

    [Fact]
    public void AllOptions_PreserveNumericOperandChanges()
    {
        var five = Decode([0x1b, 0x2a]); // ldc.i4.5; ret
        var seven = Decode([0x1d, 0x2a]); // ldc.i4.7; ret

        var diff = IlBodyDiff.Compare(five, seven, AllNormalizations);

        Assert.False(diff.IsExact);
        Assert.Equal(IlBodyDiffOutcome.OperandDiff, diff.Outcome);
        Assert.Contains(diff.Rows, row => row.Operation.Operand?.Value == "5");
        Assert.Contains(diff.Rows, row => row.Operation.Operand?.Value == "7");
    }

    [Fact]
    public void AllOptions_PreserveBranchTopologyChanges()
    {
        var firstTarget = Decode([0x2b, 0x03, 0x00, 0x2a, 0x00, 0x2a]);
        var secondTarget = Decode([0x2b, 0x01, 0x00, 0x2a, 0x00, 0x2a]);

        var diff = IlBodyDiff.Compare(firstTarget, secondTarget, AllNormalizations);

        Assert.False(diff.IsExact);
        Assert.Equal(IlBodyDiffOutcome.OperandDiff, diff.Outcome);
        Assert.Equal(2, diff.Rows.Length);
        Assert.All(diff.Rows, row => Assert.Equal("br", row.Operation.OpcodeFamily));
    }

    [Fact]
    public void NormalizePlatformAssemblyScope_ToleratesPlatformReferenceScopeChanges()
    {
        var defaultDiff = CompareCallImages("System.Runtime", "System.Private.CoreLib");
        var normalizedDiff = CompareCallImages(
            "System.Runtime",
            "System.Private.CoreLib",
            IlBodyDiffNormalization.NormalizePlatformAssemblyScope);

        Assert.False(defaultDiff.IsExact);
        Assert.True(normalizedDiff.IsExact);
    }

    [Fact]
    public void CompareStreams_AggregatesOperandDiffOutcome()
    {
        using var oldStream = new MemoryStream(BuildCallImage("Old", "Library.One"));
        using var newStream = new MemoryStream(BuildCallImage("New", "Library.Two"));

        var result = IlAssemblyDiff.CompareStreams(
            oldStream,
            "old.dll",
            newStream,
            "new.dll").Diff;

        Assert.Equal(1, result.ComparedBodyCount);
        Assert.Equal(0, result.PairExactCount);
        Assert.Equal(1, result.PairOperandDiffCount);
        Assert.Equal(0, result.PairOpcodeDiffCount);
        Assert.Equal(0, result.PairUnavailableCount);
        Assert.Equal(1, result.ChangedBodyCount);
        Assert.Equal(IlBodyDiffOutcome.OperandDiff, Assert.Single(result.Examples).Diff.Outcome);
    }

    [Fact]
    public void CompareStreams_AppliesRequestedNormalization()
    {
        using var oldStream = new MemoryStream(BuildCallImage("Old", "System.Runtime"));
        using var newStream = new MemoryStream(BuildCallImage("New", "System.Private.CoreLib"));

        var result = IlAssemblyDiff.CompareStreams(
            oldStream,
            "old.dll",
            newStream,
            "new.dll",
            normalization: IlBodyDiffNormalization.NormalizePlatformAssemblyScope).Diff;

        Assert.Equal(1, result.PairExactCount);
        Assert.Equal(0, result.ChangedBodyCount);
    }

    [Fact]
    public void NormalizePlatformAssemblyScope_PreservesNonPlatformReferenceIdentity()
    {
        var diff = CompareCallImages(
            "Library.One",
            "Library.Two",
            IlBodyDiffNormalization.NormalizePlatformAssemblyScope);

        Assert.False(diff.IsExact);
    }

    [Fact]
    public void NormalizePlatformAssemblyScope_PreservesPlatformLikeStringLiterals()
    {
        var diff = CompareImages(
            BuildStringImage("Old", "[System.Runtime]"),
            BuildStringImage("New", "[System.Private.CoreLib]"),
            IlBodyDiffNormalization.NormalizePlatformAssemblyScope);

        Assert.False(diff.IsExact);
        Assert.Contains(diff.Rows, row => row.Operation.Operand?.Value.Contains("System.Runtime", StringComparison.Ordinal) == true);
        Assert.Contains(diff.Rows, row => row.Operation.Operand?.Value.Contains("System.Private.CoreLib", StringComparison.Ordinal) == true);
    }

    [Fact]
    public void NormalizeCurrentAssemblyScope_ToleratesCurrentAssemblyNameChanges()
    {
        var oldImage = BuildCallImage("System.Old");
        var newImage = BuildCallImage("System.New");

        Assert.False(CompareImages(oldImage, newImage).IsExact);
        Assert.False(CompareImages(
            oldImage,
            newImage,
            IlBodyDiffNormalization.NormalizePlatformAssemblyScope).IsExact);
        Assert.True(CompareImages(
            oldImage,
            newImage,
            IlBodyDiffNormalization.NormalizeCurrentAssemblyScope).IsExact);
    }

    [Fact]
    public void NormalizeCurrentAssemblyScope_ToleratesDirectAndAssemblyRefSelfReferences()
    {
        var directImage = BuildCallImage("System.Runtime");
        var assemblyRefImage = BuildCallImage("System.Runtime", "System.Runtime");

        Assert.False(CompareImages(directImage, assemblyRefImage).IsExact);
        Assert.False(CompareImages(
            directImage,
            assemblyRefImage,
            IlBodyDiffNormalization.NormalizePlatformAssemblyScope).IsExact);
        Assert.True(CompareImages(
            directImage,
            assemblyRefImage,
            IlBodyDiffNormalization.NormalizeCurrentAssemblyScope).IsExact);
        Assert.True(CompareImages(
            directImage,
            assemblyRefImage,
            AllNormalizations).IsExact);
    }

    /// <summary>
    /// The gate for #3503. ``StatementBodyLambdaInsideIf`` failed
    /// <c>FidelityGateTests.NoNewFidelityDiffsBeyondKnownDocket</c> with
    /// identical opcodes and only <c>&lt;&gt;9__103_0</c> vs
    /// <c>&lt;&gt;9__128_0</c> differing, because recompiling a reconstructed
    /// unit renumbers the containing method.
    /// </summary>
    [Theory]
    [InlineData("<Run>b__103_0", "<Run>b__128_0")]                    // lambda method
    [InlineData("<Run>g__Local|103_0", "<Run>g__Local|128_0")]        // local function
    [InlineData("<.ctor>b__103_0", "<.ctor>b__128_0")]                // lambda in a constructor
    [InlineData("<Run>g__A__B|103_0", "<Run>g__A__B|128_0")]          // local name containing `__`
    [InlineData("<<Run>b__103_0>b__104_1", "<<Run>b__128_0>b__129_1")] // lambda nested in a lambda
    public void NormalizeSynthesizedMemberOrdinals_ToleratesContainingMethodRenumbering(
        string oldName,
        string newName)
    {
        Assert.False(CompareMemberNames(oldName, newName).IsExact);
        Assert.True(CompareMemberNames(
            oldName,
            newName,
            IlBodyDiffNormalization.NormalizeSynthesizedMemberOrdinals).IsExact);
    }

    /// <summary>
    /// The composition rule between the two synthesized-ordinal options, which splits
    /// the name space rather than layering: when
    /// <see cref="IlBodyDiffNormalization.NormalizeCompilerGeneratedOrdinals"/> is also
    /// requested, the correspondence owns <c>d__</c> and <c>g__</c>, and the per-side
    /// rewrite must not fold a name it owns.
    ///
    /// It matters because the correspondence folds only where the ordinal-free key is
    /// one-to-one on both sides. Where it declines — an ambiguous key, or, as here, a
    /// <c>MemberReference</c> it never indexed — that refusal is a judgement that the
    /// two members are not known to correspond. Letting the per-side rewrite fold the
    /// same name anyway would overturn it on weaker evidence and mask a real difference,
    /// which is the defect #3645 records against the per-side option used alone.
    ///
    /// The first assertion is the composition; the second pins that this is a genuine
    /// composition rule and not a dead branch, by showing the same pair still folds when
    /// the correspondence is not requested. Without it, deleting the guard would leave
    /// only an assertion that something does not happen, which a broken build also
    /// satisfies.
    /// </summary>
    [Fact]
    public void CompilerGeneratedCorrespondence_KeepsTheSynthesizedRewriteOffTheNamesItOwns()
    {
        const string Old = "<Run>g__Local|103_0";
        const string New = "<Run>g__Local|128_0";

        Assert.False(CompareMemberNames(
            Old,
            New,
            IlBodyDiffNormalization.NormalizeSynthesizedMemberOrdinals
            | IlBodyDiffNormalization.NormalizeCompilerGeneratedOrdinals).IsExact);

        Assert.True(CompareMemberNames(
            Old,
            New,
            IlBodyDiffNormalization.NormalizeSynthesizedMemberOrdinals).IsExact);
    }

    /// <summary>
    /// The same composition rule one level down, which is where checking only the
    /// outermost name leaked. The rewrite folds an outer ordinal and then recurses on
    /// the enclosing name under the same grammar, so a `b__` name enclosing a `g__`
    /// local function had its `g__` ordinal folded even while the correspondence was
    /// declining to fold it — a masked difference reached through nesting.
    ///
    /// Found by adversarial review (round 12). The reviewer demonstrated it against the
    /// name rewriter in isolation; this gate pins it end-to-end through
    /// <see cref="IlBodyDiff.Compare"/>, because a name folding does not by itself prove
    /// a false <c>Exact</c> — the rest of the operand may still separate the two.
    ///
    /// Roslyn does not emit this nesting: measured, a lambda inside a local function is
    /// named after the outermost method (<c>&lt;Run&gt;b__0_1</c>), not after the local
    /// function. It is reachable from untrusted metadata, which
    /// docs/design/untrusted-data-threat-model.md puts in scope, and the third case pins
    /// that the fix did not simply switch the rewrite off for everything nested — the
    /// lambda-in-lambda shape Roslyn *does* emit must still fold.
    /// </summary>
    [Fact]
    public void CompilerGeneratedCorrespondence_KeepsTheRewriteOffAnOwnedNameNestedInsideAnother()
    {
        const IlBodyDiffNormalization Both =
            IlBodyDiffNormalization.NormalizeSynthesizedMemberOrdinals
            | IlBodyDiffNormalization.NormalizeCompilerGeneratedOrdinals;

        Assert.False(CompareMemberNames(
            "<<Run>g__Inner|0_1>b__2_0",
            "<<Run>g__Inner|1_1>b__2_0",
            Both).IsExact);

        // Without the correspondence the rewrite still owns the name, so this is a
        // composition rule rather than a name the diff simply stopped relating.
        Assert.True(CompareMemberNames(
            "<<Run>g__Inner|0_1>b__2_0",
            "<<Run>g__Inner|1_1>b__2_0",
            IlBodyDiffNormalization.NormalizeSynthesizedMemberOrdinals).IsExact);

        // The other nesting order, where the owned `g__` form is outermost and its
        // containing name is the generated one. This reaches the guard through a
        // different failure: the correspondence parsed the name at the first `>` and
        // disowned a form it owns, so the guard was never consulted about it.
        Assert.False(CompareMemberNames(
            "<<Run>b__0_0>g__Local|0_0",
            "<<Run>b__1_0>g__Local|1_0",
            Both).IsExact);

        Assert.True(CompareMemberNames(
            "<<Run>b__0_0>g__Local|0_0",
            "<<Run>b__1_0>g__Local|1_0",
            IlBodyDiffNormalization.NormalizeSynthesizedMemberOrdinals).IsExact);

        // A shape Roslyn does emit, carrying no name the correspondence owns, keeps
        // folding under both options.
        Assert.True(CompareMemberNames(
            "<<Run>b__103_0>b__104_1",
            "<<Run>b__128_0>b__129_1",
            Both).IsExact);
    }

    /// <summary>
    /// The cache-field half of #3503. <c>&lt;&gt;9__N_M</c> is a field, so it
    /// reaches the formatter through the field paths rather than the call
    /// paths and needs its own gate — the ``StatementBodyLambdaInsideIf`` row
    /// diffed on exactly this name.
    /// </summary>
    [Fact]
    public void NormalizeSynthesizedMemberOrdinals_ToleratesRenumberingOfALambdaCacheField()
    {
        Assert.False(CompareFieldNames("<>9__103_0", "<>9__128_0").IsExact);
        Assert.True(CompareFieldNames(
            "<>9__103_0",
            "<>9__128_0",
            IlBodyDiffNormalization.NormalizeSynthesizedMemberOrdinals).IsExact);
    }

    /// <summary>
    /// Roslyn emits the cache form only as a field and the lambda and
    /// local-function forms only as methods. A name that carries one form on
    /// the other metadata table did not come from C#, so relating its ordinals
    /// would equate two members that nothing else relates.
    /// </summary>
    [Theory]
    [InlineData("<>9__103_0", "<>9__128_0")]                    // cache form on a method
    public void NormalizeSynthesizedMemberOrdinals_RejectsAFieldFormOnAMethod(
        string oldName,
        string newName)
    {
        Assert.False(CompareMemberNames(
            oldName,
            newName,
            IlBodyDiffNormalization.NormalizeSynthesizedMemberOrdinals).IsExact);
    }

    /// <inheritdoc cref="NormalizeSynthesizedMemberOrdinals_RejectsAFieldFormOnAMethod"/>
    [Theory]
    [InlineData("<Run>b__103_0", "<Run>b__128_0")]              // lambda form on a field
    [InlineData("<Run>g__Local|103_0", "<Run>g__Local|128_0")]  // local-function form on a field
    public void NormalizeSynthesizedMemberOrdinals_RejectsAMethodFormOnAField(
        string oldName,
        string newName)
    {
        Assert.False(CompareFieldNames(
            oldName,
            newName,
            IlBodyDiffNormalization.NormalizeSynthesizedMemberOrdinals).IsExact);
    }

    /// <summary>
    /// The cache form carries no containing-method name — Roslyn spells it
    /// <c>&lt;&gt;9__N_M</c>, with nothing between the angle brackets. A field
    /// whose name puts a containing method in front of the <c>9</c> is not a
    /// form any compiler emits, so it must keep comparing literally.
    /// <para>
    /// This is the field half of the containing-name correspondence. The
    /// method half — a <c>b</c> or <c>g</c> form with an empty containing
    /// name, such as <c>&lt;&gt;b__103_0</c> — is gated by
    /// <c>NormalizeSynthesizedMemberOrdinals_PreservesEveryOtherNameComponent</c>.
    /// The two halves need separate gates because the single predicate that
    /// enforces both can be broken one direction at a time: removing only the
    /// field half leaves every other test green.
    /// </para>
    /// </summary>
    [Fact]
    public void NormalizeSynthesizedMemberOrdinals_RejectsACacheFormWithAContainingName()
    {
        Assert.False(CompareFieldNames(
            "<Run>9__103_0",
            "<Run>9__128_0",
            IlBodyDiffNormalization.NormalizeSynthesizedMemberOrdinals).IsExact);

        // The one-character containing name is the boundary of `close > 1`.
        // Widening that to `close > 2` reads `<A>9__103_0` as having no
        // containing name at all, and the multi-character case above stays
        // green, so only this case pins it.
        Assert.False(CompareFieldNames(
            "<A>9__103_0",
            "<A>9__128_0",
            IlBodyDiffNormalization.NormalizeSynthesizedMemberOrdinals).IsExact);
    }

    /// <summary>
    /// Normalization introduces <c>#</c> as the ordinal placeholder, and
    /// member names come from untrusted metadata that may already contain
    /// one. Without escaping, <c>&lt;Run&gt;b__103_0</c> normalizes to
    /// <c>&lt;Run&gt;b__#_0</c> — a legal metadata name that matches no
    /// recognized form and so passes through unchanged — and the two compare
    /// equal despite naming different members. That is a masked difference,
    /// the one outcome this option must never produce.
    /// </summary>
    [Fact]
    public void NormalizeSynthesizedMemberOrdinals_DoesNotCollideWithALiteralPlaceholder()
    {
        Assert.False(CompareMemberNames(
            "<Run>b__103_0",
            "<Run>b__#_0",
            IlBodyDiffNormalization.NormalizeSynthesizedMemberOrdinals).IsExact,
            "A real closure name must not collapse onto a literal name spelled with the placeholder.");

        Assert.False(CompareFieldNames(
            "<>9__103_0",
            "<>9__#_0",
            IlBodyDiffNormalization.NormalizeSynthesizedMemberOrdinals).IsExact,
            "The cache field form must not collapse onto a literal placeholder name either.");

        // Escaping must stay injective: two literal names that differ only in
        // how many placeholders they carry must keep differing.
        Assert.False(CompareMemberNames(
            "<Run>b__#_0",
            "<Run>b__##_0",
            IlBodyDiffNormalization.NormalizeSynthesizedMemberOrdinals).IsExact,
            "Escaping must not relate two literal names that differ only in placeholder count.");

        // And it must not disturb the collapse the option exists to make.
        Assert.True(CompareMemberNames(
            "<Run>b__103_0",
            "<Run>b__128_0",
            IlBodyDiffNormalization.NormalizeSynthesizedMemberOrdinals).IsExact,
            "Escaping must leave the intended renumbering collapse intact.");
    }

    /// <summary>
    /// The escape runs once on the whole name, not once per level: the
    /// recursion calls the private overload deliberately. That is what makes
    /// the separation total rather than merely top-level — a literal
    /// <c>#</c> anywhere, including inside a containing name several levels
    /// down, is doubled before any level is parsed, while every placeholder a
    /// level introduces is a lone <c>#</c> bounded by <c>_</c>. Gated apart
    /// from
    /// <see cref="NormalizeSynthesizedMemberOrdinals_DoesNotCollideWithALiteralPlaceholder"/>
    /// so that neither can stand in for the other: that test's first assertion
    /// fails on a missing escape at depth 0 and would otherwise hide whether
    /// the nested case is checked at all.
    /// </summary>
    [Fact]
    public void NormalizeSynthesizedMemberOrdinals_DoesNotCollideWithALiteralPlaceholderWhileNested()
    {
        Assert.False(CompareMemberNames(
            "<<Inner>b__5_0>b__103_0",
            "<<Inner>b__#_0>b__103_0",
            IlBodyDiffNormalization.NormalizeSynthesizedMemberOrdinals).IsExact,
            "A placeholder the recursion introduces must not collapse onto a literal one at the same position.");
    }

    /// <summary>
    /// The cache form's ordinals must be canonical too. Checked separately
    /// from the method forms because <c>&lt;&gt;9__N_M</c> only ever reaches
    /// the field paths.
    /// </summary>
    [Fact]
    public void NormalizeSynthesizedMemberOrdinals_RejectsANonCanonicalCacheFieldOrdinal()
    {
        Assert.False(CompareFieldNames(
            "<>9__0103_0",
            "<>9__0128_0",
            IlBodyDiffNormalization.NormalizeSynthesizedMemberOrdinals).IsExact);

        Assert.False(CompareFieldNames(
            "<>9__2147483648_0",
            "<>9__2147483649_0",
            IlBodyDiffNormalization.NormalizeSynthesizedMemberOrdinals).IsExact);
    }

    /// <summary>
    /// The negative half of #3503: only the containing-method ordinal is
    /// non-evidence. Every other component of a closure name still identifies
    /// which lambda a body binds to, so a real mis-binding must keep diffing.
    /// Two forms are documented limitations rather than desired outcomes:
    /// state-machine names (<c>&lt;Name&gt;d__N</c>), whose ordinal is their
    /// only distinguishing component, and display classes
    /// (<c>&lt;&gt;c__DisplayClassN_M</c>), which do carry the same
    /// compilation-unit ordinal and so can still produce a false positive for
    /// a capturing lambda. Widening to display classes is deliberately out of
    /// scope here; the corpus has no remaining row that needs it.
    /// </summary>
    [Theory]
    [InlineData("<Run>b__103_0", "<Run>b__103_1")]              // different lambda in the same method
    [InlineData("<Run>b__103_0", "<Walk>b__103_0")]             // different containing method
    [InlineData("<Run>g__Local|103_0", "<Run>g__Other|103_0")]  // different local function
    [InlineData("<Run>d__103", "<Run>d__128")]                  // state machine: not normalized
    [InlineData("Grab__103_0", "Grab__128_0")]                  // authored name, not synthesized
    [InlineData("<b__1_0>b__103_0", "<b__2_0>b__128_0")]        // authored enclosing name that looks synthesized
    [InlineData("<>c__DisplayClass103_0", "<>c__DisplayClass128_0")] // display class: known limitation, see remarks
    [InlineData("<Run>b__103_0_extra", "<Run>b__128_0_extra")]  // trailing text: not a closure name
    [InlineData("<Run>g__Local|103_0x", "<Run>g__Local|128_0x")] // trailing text after a local function
    [InlineData("<Run>b__103_0$x", "<Run>b__128_0$x")]          // trailing `$`, which some producers emit
    [InlineData("<Run>b__103_0\u00e9", "<Run>b__128_0\u00e9")]  // trailing letter (Lu/Ll)
    [InlineData("<Run>b__103_0\u16ee", "<Run>b__128_0\u16ee")]  // trailing letter number (Nl)
    [InlineData("<Run>b__103_0\u0301", "<Run>b__128_0\u0301")]  // trailing combining mark (Mn)
    [InlineData("<Run>b__103_0\u0903", "<Run>b__128_0\u0903")]  // trailing combining mark (Mc)
    [InlineData("<Run>b__103_0\u203f", "<Run>b__128_0\u203f")]  // trailing connector punctuation (Pc)
    [InlineData("<Run>b__103_0\u200c", "<Run>b__128_0\u200c")]  // trailing format character (Cf)
    [InlineData("<Run>b__103_0\U00010400", "<Run>b__128_0\U00010400")] // trailing supplementary-plane letter
    [InlineData("<Run>b__103_0!suffix", "<Run>b__128_0!suffix")]  // trailing text after a non-identifier char
    [InlineData("x!<Run>b__103_0", "x!<Run>b__128_0")]            // synthesized form buried after leading text
    [InlineData("<<Run>b__103_0", "<<Run>b__128_0")]              // buried behind an unbalanced `<`
    [InlineData("<>b__103_0", "<>b__128_0")]                      // empty containing method with a lambda marker
    [InlineData("<>g__Local|103_0", "<>g__Local|128_0")]          // empty containing method with a local-function marker
    [InlineData("<>h__103_0", "<>h__128_0")]                      // marker adjacent to the accepted set
    [InlineData("<A__B>b-_103_0", "<A__B>b-_128_0")]              // first marker separator is not '_'
    [InlineData("<A__B>b_-103_0", "<A__B>b_-128_0")]              // second marker separator is not '_'
    [InlineData("<Run]b__103_0", "<Run]b__128_0")]                // containing name closed by ']' rather than '>'
    [InlineData("<Run>g__Local!103_0", "<Run>g__Local!128_0")]    // local-function ordinal separator is not '|'
    [InlineData("<Run>g__|103_0", "<Run>g__|128_0")]              // empty local function name
    [InlineData("<<Run>b__103_0>d__1", "<<Run>b__128_0>d__1")]    // state machine of an async lambda: a type name
    [InlineData("<Run>b__0103_0", "<Run>b__0128_0")]              // leading zero: not how Roslyn spells an ordinal
    [InlineData("<Run>b__103_00", "<Run>b__128_00")]              // leading zero in the per-method index
    [InlineData(
        "<Run>b__000000000000000000000000000103_0",
        "<Run>b__000000000000000000000000000128_0")]              // padded ordinal
    [InlineData("<Run>b__2147483648_0", "<Run>b__2147483649_0")]  // ordinal past int.MaxValue, no leading zero
    [InlineData("<Run>b__103_2147483648", "<Run>b__128_2147483648")] // per-method index past int.MaxValue
    [InlineData("<>x__103_0", "<>x__128_0")]                      // marker outside the `9`/`b`/`g` set
    [InlineData("<A__B>bX_103_0", "<A__B>bX_128_0")]              // marker not followed by `__`
    [InlineData("<A__B>b_X103_0", "<A__B>b_X128_0")]              // marker followed by only one `_`
    [InlineData("<Run>b___0", "<Run>b__128_0")]                   // no ordinal digits at all
    [InlineData("<Run>b__103_", "<Run>b__128_")]                  // no per-method index digits
    [InlineData("<Run>b__103X0", "<Run>b__128X0")]                // ordinals not separated by `_`
    public void NormalizeSynthesizedMemberOrdinals_PreservesEveryOtherNameComponent(
        string oldName,
        string newName)
    {
        Assert.False(CompareMemberNames(
            oldName,
            newName,
            IlBodyDiffNormalization.NormalizeSynthesizedMemberOrdinals).IsExact);
    }

    /// <summary>
    /// Member names come from untrusted metadata, and the threat model
    /// requires recursion over hostile input to be bounded
    /// (docs/design/untrusted-data-threat-model.md), so the enclosing-name
    /// recursion stops at <c>MaxNestingDepth</c> (16). This pins the
    /// boundary's observable behavior exactly: the last level within the cap
    /// still normalizes, and the first level past it stays literal.
    /// Degrading to literal can only cost a false positive, never a masked
    /// difference.
    /// <para>
    /// Asserting only a far-outside level (19) would not pin the boundary —
    /// widening the cap by one still leaves such a test green. The level-16
    /// and level-17 assertions are what make an off-by-one in the comparison
    /// observable.
    /// </para>
    /// </summary>
    [Fact]
    public void NormalizeSynthesizedMemberOrdinals_StopsNormalizingPastTheNestingCap()
    {
        Assert.True(CompareMemberNames(
            Nest(depth: 20, differingLevel: 0),
            Nest(depth: 20, differingLevel: 0, shift: 500),
            IlBodyDiffNormalization.NormalizeSynthesizedMemberOrdinals).IsExact,
            "The outermost ordinal is within the cap and must still normalize.");

        Assert.True(CompareMemberNames(
            Nest(depth: 20, differingLevel: 16),
            Nest(depth: 20, differingLevel: 16, shift: 500),
            IlBodyDiffNormalization.NormalizeSynthesizedMemberOrdinals).IsExact,
            "Level 16 is the last level within the cap and must still normalize.");

        Assert.False(CompareMemberNames(
            Nest(depth: 20, differingLevel: 17),
            Nest(depth: 20, differingLevel: 17, shift: 500),
            IlBodyDiffNormalization.NormalizeSynthesizedMemberOrdinals).IsExact,
            "Level 17 is the first level past the cap and must stay literal.");

        Assert.False(CompareMemberNames(
            Nest(depth: 20, differingLevel: 19),
            Nest(depth: 20, differingLevel: 19, shift: 500),
            IlBodyDiffNormalization.NormalizeSynthesizedMemberOrdinals).IsExact,
            "An ordinal nested past the cap must stay literal rather than silently comparing equal.");
    }

    /// <summary>
    /// Builds a name nested <paramref name="depth"/> levels deep where only
    /// the ordinal at <paramref name="differingLevel"/> (counted from the
    /// outermost) is moved by <paramref name="shift"/>.
    /// </summary>
    static string Nest(int depth, int differingLevel, int shift = 0)
    {
        string name = "Run";
        for (int level = depth - 1; level >= 0; level--)
        {
            int ordinal = 100 + (level == differingLevel ? shift : 0);
            name = $"<{name}>b__{ordinal}_0";
        }

        return name;
    }

    [Fact]
    public void NormalizeSynthesizedMemberOrdinals_PreservesSynthesizedLikeStringLiterals()
    {
        var diff = CompareImages(
            BuildStringImage("Old", "<>9__103_0"),
            BuildStringImage("New", "<>9__128_0"),
            IlBodyDiffNormalization.NormalizeSynthesizedMemberOrdinals);

        Assert.False(diff.IsExact);
    }

    /// <summary>
    /// The option is scoped to a member's simple name, so a type operand keeps
    /// its ordinal even when the type name is spelled like a closure. Applying
    /// the rewrite to the formatted operand string instead would let it reach
    /// declaring types, parameter types, and generic arguments, collapsing
    /// references to genuinely distinct types.
    /// </summary>
    [Theory]
    [InlineData("<Run>b__103_0", "<Run>b__128_0")]
    [InlineData("<>9__103_0", "<>9__128_0")]
    [InlineData("<>c__DisplayClass103_0", "<>c__DisplayClass128_0")]
    public void NormalizeSynthesizedMemberOrdinals_LeavesTypeOperandsAlone(
        string oldTypeName,
        string newTypeName)
    {
        Assert.False(CompareDeclaringTypeNames(
            oldTypeName,
            newTypeName,
            IlBodyDiffNormalization.NormalizeSynthesizedMemberOrdinals).IsExact);
    }

    /// <summary>
    /// Member names come from untrusted metadata and the threat model requires
    /// CPU amplification to be bounded
    /// (docs/design/untrusted-data-threat-model.md). Anchoring the grammar to
    /// the whole name is what supplies that bound: there is exactly one
    /// candidate start, so each nesting level performs one angle scan plus at
    /// most one separator scan over a disjoint part of the same string, and
    /// <c>MaxNestingDepth</c> bounds the levels. Work is therefore linear in
    /// the name's length.
    /// </summary>
    /// <remarks>
    /// The property is only observable as timing, because an anchored scan
    /// declines hostile input at every size rather than changing behavior at a
    /// threshold. The bound is deliberately loose: the shapes below are
    /// microseconds when the scan is anchored, and were measured at 5.7 s
    /// (unbalanced angles) and 1.4 s (local-function separators) at a quarter
    /// of this size back when each candidate rescanned. Anything that
    /// reintroduces a per-candidate rescan misses this bound by orders of
    /// magnitude.
    /// </remarks>
    [Fact]
    public void NormalizeSynthesizedMemberOrdinals_BoundsScanWorkOnHostileNames()
    {
        string unbalanced = new string('<', 400_000) + "<Run>b__103_0";
        string separators = "<Run>b__103_0" + string.Concat(Enumerable.Repeat("<>g__.", 64_000));

        var elapsed = System.Diagnostics.Stopwatch.StartNew();

        Assert.False(CompareMemberNames(
            unbalanced,
            unbalanced.Replace("103", "128", StringComparison.Ordinal),
            IlBodyDiffNormalization.NormalizeSynthesizedMemberOrdinals).IsExact,
            "A name that is not entirely one of these forms must compare literally.");

        Assert.False(CompareMemberNames(
            separators,
            separators.Replace("103", "128", StringComparison.Ordinal),
            IlBodyDiffNormalization.NormalizeSynthesizedMemberOrdinals).IsExact,
            "A name that is not entirely one of these forms must compare literally.");

        elapsed.Stop();

        Assert.True(elapsed.Elapsed < TimeSpan.FromSeconds(5),
            $"Scanning hostile names must stay linear; took {elapsed.Elapsed.TotalSeconds:F1}s.");
    }

    [Fact]
    public void Compare_RejectsUndefinedOptions()
    {
        var body = Decode([0x2a]);

        Assert.Throws<ArgumentOutOfRangeException>(
            () => IlBodyDiff.Compare(body, body, (IlBodyDiffNormalization)(1 << 10)));
    }

    static IlBodyDiffResult CompareMemberNames(
        string oldMemberName,
        string newMemberName,
        IlBodyDiffNormalization normalization = IlBodyDiffNormalization.None)
        => CompareImages(
            BuildCallImage("Same", "Library.Probe", oldMemberName),
            BuildCallImage("Same", "Library.Probe", newMemberName),
            normalization);

    /// <summary>
    /// The kind check must hold on <em>every</em> member-reference path, not
    /// only the direct ones. A <c>MethodSpecification</c> can name a
    /// <c>MemberReference</c> that is actually a field — malformed, but a
    /// shape untrusted metadata can carry — and the generic-instantiation
    /// formatter reaches the name through its own code path. Without a kind
    /// check there, a method-form name on a field normalizes and two
    /// unrelated members collapse.
    /// </summary>
    [Fact]
    public void NormalizeSynthesizedMemberOrdinals_RejectsAMethodFormBehindAMethodSpecificationOnAField()
    {
        Assert.False(CompareImages(
            BuildMethodSpecificationOverFieldReferenceImage("Same", "<Run>b__103_0"),
            BuildMethodSpecificationOverFieldReferenceImage("Same", "<Run>b__128_0"),
            IlBodyDiffNormalization.NormalizeSynthesizedMemberOrdinals).IsExact);
    }

    /// <summary>
    /// Builds a body whose call target is a <c>MethodSpecification</c> naming
    /// a <c>MemberReference</c> that carries a <em>field</em> signature.
    /// </summary>
    static byte[] BuildMethodSpecificationOverFieldReferenceImage(string assemblyName, string memberName)
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            0,
            metadata.GetOrAddString($"{assemblyName}.dll"),
            metadata.GetOrAddGuid(Guid.NewGuid()),
            default,
            default);
        metadata.AddAssembly(
            metadata.GetOrAddString(assemblyName),
            new Version(1, 0, 0, 0),
            default,
            default,
            default,
            default);
        var reference = metadata.AddAssemblyReference(
            metadata.GetOrAddString("Library.Probe"),
            new Version(1, 0, 0, 0),
            default,
            default,
            default,
            default);
        var type = metadata.AddTypeReference(
            reference,
            metadata.GetOrAddString("System"),
            metadata.GetOrAddString("Probe"));
        var fieldReference = metadata.AddMemberReference(
            type,
            metadata.GetOrAddString(memberName),
            metadata.GetOrAddBlob(new byte[] { 0x06, 0x08 }));
        var spec = metadata.AddMethodSpecification(
            fieldReference,
            metadata.GetOrAddBlob(new byte[] { 0x0a, 0x01, 0x08 }));

        metadata.AddTypeDefinition(
            TypeAttributes.NotPublic,
            default,
            metadata.GetOrAddString("<Module>"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        metadata.AddTypeDefinition(
            TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed,
            default,
            metadata.GetOrAddString("C"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));

        var il = new BlobBuilder();
        var encoder = new InstructionEncoder(il, new ControlFlowBuilder());
        encoder.Call(spec);
        encoder.OpCode(ILOpCode.Ret);
        var methodBodies = new BlobBuilder();
        int bodyOffset = new MethodBodyStreamEncoder(methodBodies).AddMethodBody(encoder);
        metadata.AddMethodDefinition(
            MethodAttributes.Public | MethodAttributes.Static,
            MethodImplAttributes.IL,
            metadata.GetOrAddString("Caller"),
            metadata.GetOrAddBlob(new byte[] { 0x00, 0x00, 0x01 }),
            bodyOffset,
            MetadataTokens.ParameterHandle(1));

        var pe = new ManagedPEBuilder(
            PEHeaderBuilder.CreateLibraryHeader(),
            new MetadataRootBuilder(metadata),
            methodBodies,
            flags: CorFlags.ILOnly);
        var image = new BlobBuilder();
        pe.Serialize(image);
        return image.ToArray();
    }

    static IlBodyDiffResult CompareFieldNames(
        string oldMemberName,
        string newMemberName,
        IlBodyDiffNormalization normalization = IlBodyDiffNormalization.None)
        => CompareImages(
            BuildFieldImage("Same", "Library.Probe", oldMemberName),
            BuildFieldImage("Same", "Library.Probe", newMemberName),
            normalization);

    static IlBodyDiffResult CompareDeclaringTypeNames(
        string oldTypeName,
        string newTypeName,
        IlBodyDiffNormalization normalization = IlBodyDiffNormalization.None)
        => CompareImages(
            BuildCallImage("Same", "Library.Probe", "Target", oldTypeName),
            BuildCallImage("Same", "Library.Probe", "Target", newTypeName),
            normalization);

    static MethodInstructions Decode(byte[] il)
        => MethodInstructions.Decode(il, il.Length, exceptionRegions: []);

    static IlBodyDiffResult CompareCallImages(
        string oldReference,
        string newReference,
        IlBodyDiffNormalization normalization = IlBodyDiffNormalization.None)
        => CompareImages(
            BuildCallImage("Old", oldReference),
            BuildCallImage("New", newReference),
            normalization);

    static IlBodyDiffResult CompareImages(
        byte[] oldImage,
        byte[] newImage,
        IlBodyDiffNormalization normalization = IlBodyDiffNormalization.None)
    {
        using var oldPe = new PEReader(new MemoryStream(oldImage));
        using var newPe = new PEReader(new MemoryStream(newImage));
        var oldReader = oldPe.GetMetadataReader();
        var newReader = newPe.GetMetadataReader();
        var oldMethod = MetadataTokens.MethodDefinitionHandle(1);
        var newMethod = MetadataTokens.MethodDefinitionHandle(1);
        return IlAssemblyDiff.CompareMembers(
            oldPe,
            oldReader,
            oldMethod,
            newPe,
            newReader,
            newMethod,
            normalization: normalization).Diff;
    }

    static byte[] BuildCallImage(
        string assemblyName,
        string? referenceAssemblyName = null,
        string? memberName = null,
        string? typeName = null)
        => BuildMemberImage(assemblyName, referenceAssemblyName, memberName, typeName, asField: false);

    /// <summary>
    /// The field counterpart of <see cref="BuildCallImage"/>: the body loads a
    /// static field through a <c>MemberReference</c> rather than calling a
    /// method, so a name reaches the formatter as a <em>field</em> name.
    /// </summary>
    static byte[] BuildFieldImage(
        string assemblyName,
        string? referenceAssemblyName = null,
        string? memberName = null,
        string? typeName = null)
        => BuildMemberImage(assemblyName, referenceAssemblyName, memberName, typeName, asField: true);

    static byte[] BuildMemberImage(
        string assemblyName,
        string? referenceAssemblyName,
        string? memberName,
        string? typeName,
        bool asField)
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            0,
            metadata.GetOrAddString($"{assemblyName}.dll"),
            metadata.GetOrAddGuid(Guid.NewGuid()),
            default,
            default);
        metadata.AddAssembly(
            metadata.GetOrAddString(assemblyName),
            new Version(1, 0, 0, 0),
            default,
            default,
            default,
            default);
        EntityHandle target;
        if (referenceAssemblyName is null)
        {
            target = asField
                ? MetadataTokens.FieldDefinitionHandle(1)
                : MetadataTokens.MethodDefinitionHandle(1);
        }
        else
        {
            bool selfReference = referenceAssemblyName == assemblyName;
            var reference = metadata.AddAssemblyReference(
                metadata.GetOrAddString(referenceAssemblyName),
                new Version(1, 0, 0, 0),
                default,
                default,
                default,
                default);
            var type = metadata.AddTypeReference(
                reference,
                selfReference ? default : metadata.GetOrAddString("System"),
                metadata.GetOrAddString(typeName ?? (selfReference ? "C" : "Probe")));
            target = metadata.AddMemberReference(
                type,
                metadata.GetOrAddString(memberName ?? (selfReference ? "Caller" : "Target")),
                metadata.GetOrAddBlob(asField
                    ? new byte[] { 0x06, 0x08 }
                    : new byte[] { 0x00, 0x00, 0x01 }));
        }

        metadata.AddTypeDefinition(
            TypeAttributes.NotPublic,
            default,
            metadata.GetOrAddString("<Module>"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        metadata.AddTypeDefinition(
            TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed,
            default,
            metadata.GetOrAddString("C"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));

        var il = new BlobBuilder();
        var encoder = new InstructionEncoder(il, new ControlFlowBuilder());
        if (asField)
        {
            encoder.OpCode(ILOpCode.Ldsfld);
            encoder.Token(target);
            encoder.OpCode(ILOpCode.Pop);
        }
        else
        {
            encoder.Call(target);
        }

        encoder.OpCode(ILOpCode.Ret);
        var methodBodies = new BlobBuilder();
        int bodyOffset = new MethodBodyStreamEncoder(methodBodies).AddMethodBody(encoder);
        metadata.AddMethodDefinition(
            MethodAttributes.Public | MethodAttributes.Static,
            MethodImplAttributes.IL,
            metadata.GetOrAddString("Caller"),
            metadata.GetOrAddBlob(new byte[] { 0x00, 0x00, 0x01 }),
            bodyOffset,
            MetadataTokens.ParameterHandle(1));

        var pe = new ManagedPEBuilder(
            PEHeaderBuilder.CreateLibraryHeader(),
            new MetadataRootBuilder(metadata),
            methodBodies,
            flags: CorFlags.ILOnly);
        var image = new BlobBuilder();
        pe.Serialize(image);
        return image.ToArray();
    }

    static byte[] BuildStringImage(string assemblyName, string value)
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            0,
            metadata.GetOrAddString($"{assemblyName}.dll"),
            metadata.GetOrAddGuid(Guid.NewGuid()),
            default,
            default);
        metadata.AddAssembly(
            metadata.GetOrAddString(assemblyName),
            new Version(1, 0, 0, 0),
            default,
            default,
            default,
            default);
        metadata.AddTypeDefinition(
            TypeAttributes.NotPublic,
            default,
            metadata.GetOrAddString("<Module>"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        metadata.AddTypeDefinition(
            TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed,
            default,
            metadata.GetOrAddString("C"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));

        var il = new BlobBuilder();
        var encoder = new InstructionEncoder(il, new ControlFlowBuilder());
        encoder.LoadString(metadata.GetOrAddUserString(value));
        encoder.OpCode(ILOpCode.Pop);
        encoder.OpCode(ILOpCode.Ret);
        var methodBodies = new BlobBuilder();
        int bodyOffset = new MethodBodyStreamEncoder(methodBodies).AddMethodBody(encoder);
        metadata.AddMethodDefinition(
            MethodAttributes.Public | MethodAttributes.Static,
            MethodImplAttributes.IL,
            metadata.GetOrAddString("Caller"),
            metadata.GetOrAddBlob(new byte[] { 0x00, 0x00, 0x01 }),
            bodyOffset,
            MetadataTokens.ParameterHandle(1));

        var pe = new ManagedPEBuilder(
            PEHeaderBuilder.CreateLibraryHeader(),
            new MetadataRootBuilder(metadata),
            methodBodies,
            flags: CorFlags.ILOnly);
        var image = new BlobBuilder();
        pe.Serialize(image);
        return image.ToArray();
    }
}
