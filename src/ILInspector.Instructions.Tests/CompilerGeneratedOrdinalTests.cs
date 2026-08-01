using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Text;

namespace ILInspector.Instructions.Tests;

/// <summary>
/// Controls for <see cref="IlBodyDiffNormalization.NormalizeCompilerGeneratedOrdinals"/>.
/// Most cases are expressed as a whole-image comparison through the public diff seam
/// (<c>IlAssemblyDiff.CompareMembers</c>), so the eligibility rules, the
/// <c>CompilerGeneratedAttribute</c> gate and the two-sided uniqueness requirement are
/// exercised together rather than asserted about a helper.
/// </summary>
/// <remarks>
/// Exactly five cases do not, because their claim is not about a comparison. Measured by
/// making <c>CompareMembers</c> throw and listing what still passed, rather than by
/// reading:
/// <list type="bullet">
/// <item><see cref="PlaceholderCannotBeSpelledByAMetadataName"/> and
/// <see cref="KeySeparatorCannotBeSpelledByAMetadataName"/> build one image and read the
/// names back out of it, because they claim a hostile name cannot exist at all.</item>
/// <item><see cref="DefaultStringDecoder_StillFolds"/> and
/// <see cref="NonDefaultStringDecoder_FoldsNothing"/> call
/// <see cref="CompilerGeneratedOrdinalCorrespondence.Build"/> directly, because the check
/// they cover is on <c>MetadataReader.UTF8Decoder</c> and the comparison helpers construct
/// their own readers.</item>
/// <item><see cref="AStringDecoderCanReturnANameContainingNul"/> calls the hostile decoder
/// itself; it pins that the hazard is real and asserts nothing about this product.</item>
/// </list>
/// </remarks>
public class CompilerGeneratedOrdinalTests
{
    const IlBodyDiffNormalization Ordinals =
        IlBodyDiffNormalization.NormalizeCompilerGeneratedOrdinals;

    [Fact]
    public void LocalFunctionOrdinal_FoldsWhenTheKeyIsUniqueOnBothSides()
    {
        Assert.False(Compare([Generated("<M>g__L|3_0")], [Generated("<M>g__L|7_0")]).IsExact);
        Assert.True(Compare([Generated("<M>g__L|3_0")], [Generated("<M>g__L|7_0")], Ordinals).IsExact);
    }

    [Fact]
    public void StateMachineOrdinal_FoldsWhenTheKeyIsUniqueOnBothSides()
    {
        // A state machine is a *type*. This fixture used to declare the name on a
        // method, which folded only because eligibility was blind to the entity a
        // name was read from, and so pinned a shape the compiler never emits in
        // place of the one the corpus actually exercises.
        Assert.False(CompareTypes(["<M>d__3"], ["<M>d__7"], IlBodyDiffNormalization.None).IsExact);
        Assert.True(CompareTypes(["<M>d__3"], ["<M>d__7"]).IsExact);
    }

    /// <summary>
    /// Each owned form belongs to exactly one entity kind: Roslyn emits
    /// <c>&lt;M&gt;d__N</c> as a state-machine type and <c>&lt;M&gt;g__L|N_K</c> as a
    /// local-function method. Carrying a form on the other kind is not a shape any
    /// compiler produces, so nothing relates the two sides' ordinals and folding them
    /// is a masked difference — the same rule the per-side rewrite applies to
    /// non-canonical ordinals, applied to the entity instead of the digits.
    /// </summary>
    /// <remarks>
    /// Both halves are separately load-bearing: eligibility is consulted on the type
    /// loop and the method loop independently, so a kind check on one does not imply
    /// one on the other.
    /// </remarks>
    [Fact]
    public void AnOwnedFormOnTheWrongEntityKind_DoesNotFold()
    {
        Assert.False(Compare([Generated("<M>d__3")], [Generated("<M>d__7")], Ordinals).IsExact,
            "A state-machine name on a method is not a Roslyn shape and must not fold.");

        Assert.False(CompareTypes(["<M>g__L|3_0"], ["<M>g__L|7_0"]).IsExact,
            "A local-function name on a type is not a Roslyn shape and must not fold.");
    }

    /// <summary>
    /// Roslyn formats these indices with an invariant <see cref="int"/> conversion, so a
    /// padded ordinal and one past <see cref="int.MaxValue"/> are forms it cannot emit.
    /// Folding them keys <c>&lt;M&gt;d__01</c> and <c>&lt;M&gt;d__1</c> alike, masking a
    /// difference between two names nothing relates.
    /// </summary>
    /// <remarks>
    /// The two rules are gated independently: a padded ordinal is rejected before the
    /// parse is reached, so a case that is only padded would leave the range rule
    /// untested. Both indices of the local-function form are covered because each is
    /// checked by its own call.
    /// </remarks>
    [Theory]
    [InlineData("<M>d__01", "<M>d__1")]
    [InlineData("<M>d__2147483648", "<M>d__2147483649")]
    public void NonCanonicalOrdinals_DoNotFold(string oldName, string newName)
    {
        Assert.False(CompareTypes([oldName], [newName]).IsExact);
    }

    [Theory]
    [InlineData("<M>g__L|01_0", "<M>g__L|1_0")]
    [InlineData("<M>g__L|2147483648_0", "<M>g__L|2147483649_0")]
    // The slot is held back rather than elided, so two names differing *in* the slot
    // never fold whatever the rule says, and a case shaped that way would pass
    // vacuously. These hold the non-canonical slot equal on both sides and differ in
    // the scope, so the fold is available and only the slot's own check withholds it.
    [InlineData("<M>g__L|3_01", "<M>g__L|7_01")]
    [InlineData("<M>g__L|3_2147483648", "<M>g__L|7_2147483648")]
    public void NonCanonicalLocalFunctionOrdinals_DoNotFold(string oldName, string newName)
    {
        Assert.False(Compare([Generated(oldName)], [Generated(newName)], Ordinals).IsExact);
    }

    /// <summary>
    /// Two local functions whose signatures differ only in <c>EXPLICITTHIS</c> must not
    /// compare equal once the correspondence folds their names.
    /// </summary>
    /// <remarks>
    /// The correspondence keys a method on its declaring type, its ordinal-free name, and
    /// its arity — deliberately not on its signature, because a signature blob encodes
    /// type references as metadata tokens that legitimately differ between the two
    /// assemblies being compared. Everything the key does not carry has to reach the
    /// rendered operand instead, and <c>EXPLICITTHIS</c> did not: both headers rendered
    /// as <c>instance</c>, so folding the names left nothing to tell the two apart.
    ///
    /// The assertion is deliberately not "does not fold". The names still fold, which is
    /// correct — these really are the same member modulo ordinal as far as the key can
    /// tell. What must survive is the <em>difference</em>, now spelled in the operand.
    /// </remarks>
    [Fact]
    public void MethodsDifferingOnlyInExplicitThis_DoNotFold()
    {
        // 0x20 is HASTHIS; 0x60 adds EXPLICITTHIS.
        Assert.False(Compare(
            [new Member("<M>g__L|3_0", CompilerGenerated: true, SignatureHeader: 0x20)],
            [new Member("<M>g__L|7_0", CompilerGenerated: true, SignatureHeader: 0x60)],
            Ordinals).IsExact);

        // The control: identical headers still fold, so the case above fails for the
        // signature bit rather than because this shape stopped folding altogether.
        Assert.True(Compare(
            [new Member("<M>g__L|3_0", CompilerGenerated: true, SignatureHeader: 0x20)],
            [new Member("<M>g__L|7_0", CompilerGenerated: true, SignatureHeader: 0x20)],
            Ordinals).IsExact);
    }

    /// <summary>
    /// A function-pointer type carries the same <c>this</c> attributes a method signature
    /// does, and two that differ in them are different types.
    /// </summary>
    /// <remarks>
    /// Found while fixing the <c>EXPLICITTHIS</c> hole above: the function-pointer
    /// renderer dropped <em>both</em> bits, so <c>method instance void *()</c> and
    /// <c>method void *()</c> rendered identically and any two operands differing only
    /// there compared equal. Unlike the method case this needs no name folding to bite —
    /// it is a plain rendering gap — but it is the same defect, that a signature bit the
    /// comparison depends on never reached the text being compared.
    ///
    /// The signatures below return a function pointer: <c>0x1B</c> is <c>FNPTR</c>,
    /// followed by the pointee's own header, parameter count, and return type.
    /// </remarks>
    [Theory]
    [InlineData((byte)0x20, (byte)0x00)]  // HASTHIS vs static
    [InlineData((byte)0x60, (byte)0x20)]  // HASTHIS|EXPLICITTHIS vs HASTHIS
    public void FunctionPointersDifferingOnlyInTheirThisAttributes_AreNotEqual(
        byte oldPointeeHeader,
        byte newPointeeHeader)
    {
        Assert.False(Compare(
            [new Member("M", CompilerGenerated: false,
                RawSignature: [0x00, 0x00, 0x1B, oldPointeeHeader, 0x00, 0x01])],
            [new Member("M", CompilerGenerated: false,
                RawSignature: [0x00, 0x00, 0x1B, newPointeeHeader, 0x00, 0x01])],
            Ordinals).IsExact);

        // Identical pointees still compare equal, so the cases above fail for the
        // attribute bits rather than because this shape never compares equal.
        Assert.True(Compare(
            [new Member("M", CompilerGenerated: false,
                RawSignature: [0x00, 0x00, 0x1B, oldPointeeHeader, 0x00, 0x01])],
            [new Member("M", CompilerGenerated: false,
                RawSignature: [0x00, 0x00, 0x1B, oldPointeeHeader, 0x00, 0x01])],
            Ordinals).IsExact);
    }

    /// <summary>
    /// A zero ordinal is canonical and must keep folding: the padding rule rejects a
    /// leading zero only when it is padding, so rejecting a bare <c>0</c> would silently
    /// stop folding the first generated member of every containing method.
    /// </summary>
    [Fact]
    public void AZeroOrdinal_StillFolds()
    {
        Assert.True(CompareTypes(["<M>d__0"], ["<M>d__7"]).IsExact);
        Assert.True(Compare([Generated("<M>g__L|0_0")], [Generated("<M>g__L|7_0")], Ordinals).IsExact);
    }

    /// <summary>
    /// A generated name whose <em>containing</em> name is itself generated is still one
    /// this correspondence owns. Parsing at the first <c>&gt;</c> instead of the matching
    /// one splits <c>&lt;&lt;Run&gt;b__0_0&gt;g__Local|0_0</c> after <c>&lt;&lt;Run&gt;</c>,
    /// leaving <c>b__0_0&gt;g__Local|0_0</c>, which matches no owned form — so the name is
    /// disowned and the ordinal is never elided.
    ///
    /// Two things go wrong when that happens, and the second is the dangerous one.
    /// The correspondence stops folding a name it is responsible for, which costs a false
    /// positive. And IlBodyDiff asks this same predicate which names belong to the
    /// correspondence, so a disowned name is handed to the per-side rewrite, which folds
    /// it with no two-sided evidence at all — a masked difference. That end-to-end
    /// consequence is gated separately by
    /// IlBodyDiffNormalizationTests.CompilerGeneratedCorrespondence_KeepsTheRewriteOffAnOwnedNameNestedInsideAnother.
    ///
    /// The negative cases pin that depth matching did not widen ownership: the
    /// <c>&lt;&gt;</c>-prefixed anonymous shapes still report a closing angle of 1 and
    /// stay disowned, which is what keeps unrelated closures from merging.
    /// </summary>
    [Fact]
    public void GeneratedNameWhoseContainingNameIsItselfGenerated_IsStillOwned()
    {
        Assert.Equal(
            "<<Run>b__0_0>g__Local|#\0_0",
            CompilerGeneratedOrdinalCorrespondence.TryElideOrdinal("<<Run>b__0_0>g__Local|0_0", CompilerGeneratedOrdinalCorrespondence.GeneratedNameKind.Method));

        Assert.Equal(
            "<<Run>g__Outer|0_0>d__#\0",
            CompilerGeneratedOrdinalCorrespondence.TryElideOrdinal("<<Run>g__Outer|0_0>d__7", CompilerGeneratedOrdinalCorrespondence.GeneratedNameKind.Type));

        // Unchanged by depth matching: the anonymous shapes stay disowned. Asked under
        // `Any`, which is the weakest refusal available, so these also pin that the
        // entity-kind split did not widen ownership anywhere.
        Assert.Null(CompilerGeneratedOrdinalCorrespondence.TryElideOrdinal("<>9__1_0", CompilerGeneratedOrdinalCorrespondence.GeneratedNameKind.Any));
        Assert.Null(CompilerGeneratedOrdinalCorrespondence.TryElideOrdinal("<>c__DisplayClass1_0", CompilerGeneratedOrdinalCorrespondence.GeneratedNameKind.Any));
        Assert.Null(CompilerGeneratedOrdinalCorrespondence.TryElideOrdinal("<>c", CompilerGeneratedOrdinalCorrespondence.GeneratedNameKind.Any));

        // A containing name that never closes is not a form at all.
        Assert.Null(CompilerGeneratedOrdinalCorrespondence.TryElideOrdinal("<<Run>g__Local|0_0", CompilerGeneratedOrdinalCorrespondence.GeneratedNameKind.Any));
    }

    /// <summary>
    /// The decisive control for the two-sided rule. Both sides call an identically named
    /// local function, so the bodies are exact before normalization. Adding an unrelated
    /// same-key member to one side makes the key ambiguous there. A resolver-local
    /// eligibility test would fold the unique side only and report a difference that
    /// neither assembly contains; folding must stay symmetric, so this stays exact.
    /// </summary>
    [Fact]
    public void UniqueAgainstAmbiguous_DoesNotManufactureADifference()
    {
        var oldSide = new[] { Generated("<M>g__L|3_0") };
        var newSide = new[] { Generated("<M>g__L|3_0"), Generated("<M>g__L|9_0") };

        Assert.True(Compare(oldSide, newSide).IsExact);
        Assert.True(Compare(oldSide, newSide, Ordinals).IsExact);
    }

    /// <summary>The mirror of the case above, with the ambiguity on the old side.</summary>
    [Fact]
    public void AmbiguousAgainstUnique_DoesNotManufactureADifference()
    {
        var oldSide = new[] { Generated("<M>g__L|3_0"), Generated("<M>g__L|9_0") };
        var newSide = new[] { Generated("<M>g__L|3_0") };

        Assert.True(Compare(oldSide, newSide).IsExact);
        Assert.True(Compare(oldSide, newSide, Ordinals).IsExact);
    }

    /// <summary>
    /// The gate for the NEW-side half of the two-sided rule, which the two
    /// manufactured-difference controls below do not reach: they only exercise a member
    /// that folds identically whichever side is consulted, so they pass even when the
    /// new-side ambiguity test is deleted. Here the sides share no ordinal, so consulting
    /// only the old side would fold the unique old member onto an arbitrary first-seen
    /// ambiguous counterpart and report two unrelated methods as equal. Dropping
    /// <c>newIndex.AmbiguousMethods</c> from the eligibility test makes this exact.
    /// </summary>
    [Fact]
    public void UniqueAgainstAmbiguous_DoesNotFoldOntoAnArbitraryCounterpart()
    {
        var oldSide = new[] { Generated("<M>g__L|3_0") };
        var newSide = new[] { Generated("<M>g__L|7_0"), Generated("<M>g__L|9_0") };

        Assert.False(Compare(oldSide, newSide).IsExact);
        Assert.False(Compare(oldSide, newSide, Ordinals).IsExact);
    }

    /// <summary>The mirror, with the ambiguity on the old side.</summary>
    [Fact]
    public void AmbiguousAgainstUnique_DoesNotFoldOntoAnArbitraryCounterpart()
    {
        var oldSide = new[] { Generated("<M>g__L|7_0"), Generated("<M>g__L|9_0") };
        var newSide = new[] { Generated("<M>g__L|3_0") };

        Assert.False(Compare(oldSide, newSide).IsExact);
        Assert.False(Compare(oldSide, newSide, Ordinals).IsExact);
    }

    /// <summary>
    /// Ambiguous on both sides: two local functions that share a key must never be folded
    /// together, because the fold would equate calls to genuinely different methods.
    /// </summary>
    [Fact]
    public void AmbiguousOnBothSides_KeepsDistinctMembersDistinct()
    {
        var oldSide = new[] { Generated("<M>g__L|3_0"), Generated("<M>g__L|9_0") };
        var newSide = new[] { Generated("<M>g__L|4_0"), Generated("<M>g__L|8_0") };

        Assert.False(Compare(oldSide, newSide).IsExact);
        Assert.False(Compare(oldSide, newSide, Ordinals).IsExact);
    }

    /// <summary>
    /// The slot ordinal distinguishes local functions declared in one containing method,
    /// so it is compared, not elided. Only the member ordinal is unstable.
    /// </summary>
    [Fact]
    public void SlotOrdinal_IsPreserved()
    {
        Assert.False(Compare([Generated("<M>g__L|3_0")], [Generated("<M>g__L|7_1")], Ordinals).IsExact);
    }

    /// <summary>
    /// The mangled shapes are unspellable in C# but not in IL, so eligibility is gated on
    /// the attribute Roslyn actually emits rather than on the name alone.
    /// </summary>
    /// <remarks>
    /// This covers a member carrying <em>no</em> attribute, which never enters the
    /// attribute inspection at all. The identity of the attribute that is found is a
    /// separate property, pinned by <see cref="UnrelatedAttribute_DoesNotFold"/>.
    /// </remarks>
    [Fact]
    public void NameShapeAlone_DoesNotFold()
    {
        Assert.False(Compare([Plain("<M>g__L|3_0")], [Plain("<M>g__L|7_0")], Ordinals).IsExact);
    }

    /// <summary>
    /// Eligibility requires the attribute to be <c>CompilerGeneratedAttribute</c> in
    /// <c>System.Runtime.CompilerServices</c>, not merely some attribute. Each row drops
    /// exactly one half of that test, so the two rows fail independently: the first when
    /// the type name stops being compared, the second when the namespace does.
    /// </summary>
    [Theory]
    [InlineData("System.Runtime.CompilerServices", "IsReadOnlyAttribute")]
    [InlineData("Evil", "CompilerGeneratedAttribute")]
    public void UnrelatedAttribute_DoesNotFold(string attributeNamespace, string attributeName)
    {
        Assert.False(Compare(
            [Attributed("<M>g__L|3_0", attributeNamespace, attributeName)],
            [Attributed("<M>g__L|7_0", attributeNamespace, attributeName)],
            Ordinals).IsExact);
    }

    /// <summary>
    /// The opening bracket is a discriminator, not decoration. Without it a name whose
    /// first character is anything at all still yields a containing-name span, so two raw
    /// members that merely contain <c>&gt;</c> elide to one form and a changed call target
    /// reads as <c>Exact</c>.
    /// </summary>
    [Fact]
    public void NameNotOpeningWithABracket_DoesNotFold()
    {
        Assert.False(Compare(
            [Generated("XM>g__L|3_0")],
            [Generated("XM>g__L|7_0")],
            Ordinals).IsExact);
    }

    /// <summary>
    /// Malformed generated names must be declined, not crash the comparison. Each of these
    /// reaches the parser and is refused before it indexes outside the name; without those
    /// guards the whole diff fails with an exception, which is a comparison the caller
    /// would otherwise have completed.
    /// </summary>
    /// <remarks>
    /// The two sides are identical, so the assertion is that a body compares equal to
    /// itself. That is the weakest claim that still fails on a throw, and it cannot pass
    /// for the wrong reason the way an inequality assertion could.
    /// <para>
    /// The rows cover two guards, not three: <c>""</c> and <c>&lt;M&gt;</c> are both short
    /// enough to be refused on length, and <c>&lt;M&gt;g__</c> reaches the separator guard.
    /// The <c>g__</c> prefix test itself is <em>not</em> pinned here — deleting it leaves
    /// all three rows green — and is gated by
    /// <see cref="NonLocalFunctionShape_IsNotRewrittenIntoOne"/> instead.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData("<M>g__")]
    [InlineData("<M>")]
    [InlineData("")]
    public void MalformedGeneratedName_DoesNotCrashTheComparison(string name)
    {
        Assert.True(Compare([Generated(name)], [Generated(name)], Ordinals).IsExact);
    }

    /// <summary>
    /// An assembly may define <c>CompilerGeneratedAttribute</c> itself — the platform's own
    /// core library does — and then its generated members name the constructor as a
    /// <c>MethodDefinition</c> rather than through a <c>MemberReference</c> to another
    /// assembly. Both spellings must be recognized, or folding silently stops working for
    /// exactly the assembly the corpus cares most about.
    /// </summary>
    /// <remarks>
    /// This is a completeness control, not a soundness one: losing the definition path
    /// costs retirements rather than producing a false <c>Exact</c>. It is here because
    /// nothing else in the suite reaches that branch, so the loss would be silent. The
    /// matching soundness claim — that the branch still checks <em>which</em> attribute it
    /// found — is <see cref="UnrelatedLocallyDefinedAttribute_DoesNotFold"/>.
    /// </remarks>
    [Fact]
    public void LocallyDefinedCompilerGeneratedAttribute_IsRecognized()
    {
        Assert.True(Compare(
            [Generated("<M>g__L|3_0")],
            [Generated("<M>g__L|7_0")],
            Ordinals,
            ctorSpelling: AttributeCtorSpelling.MethodDefinition).IsExact);
    }

    /// <summary>
    /// Identity decides eligibility on the definition path as well as the reference path.
    /// Without that comparison an assembly that defines any attribute at all folds every
    /// member carrying one, which is the same false-<c>Exact</c> class as
    /// <see cref="UnrelatedAttribute_DoesNotFold"/> reached through the other spelling.
    /// </summary>
    /// <remarks>
    /// Deleting the whole branch is <em>not</em> a faithful tamper for this control: the
    /// walk to the declaring type is itself observed by
    /// <c>IlAssemblyDiffMetadataGraphSafetyTests.MetadataGraphEdgeCensus_HasNoLocalIdentityRelationshipWalk</c>,
    /// so removing the walk fails that census test for a reason unrelated to identity. A
    /// tamper that keeps the walk and drops only the comparison passed the whole suite
    /// before this control existed.
    /// </remarks>
    [Theory]
    [InlineData("System.Runtime.CompilerServices", "IsReadOnlyAttribute")]
    [InlineData("Evil", "CompilerGeneratedAttribute")]
    public void UnrelatedLocallyDefinedAttribute_DoesNotFold(
        string attributeNamespace,
        string attributeName)
    {
        Assert.False(Compare(
            [Generated("<M>g__L|3_0")],
            [Generated("<M>g__L|7_0")],
            Ordinals,
            ctorSpelling: AttributeCtorSpelling.MethodDefinition,
            localAttributeNamespace: attributeNamespace,
            localAttributeName: attributeName).IsExact);
    }

    /// <summary>
    /// A constructor named through a <c>MemberReference</c> whose parent is a
    /// <c>TypeDefinition</c> is legal metadata that no C# compiler emits. The reference
    /// path accepts only a <c>TypeReference</c> parent and so declines it, which is the
    /// fail-closed choice: it costs a retirement on a shape the corpus never contains.
    /// </summary>
    /// <remarks>
    /// The decline is load-bearing in two directions, and this control holds both. Dropping
    /// the restriction while keeping the cast that depends on it turns the decline into an
    /// <c>InvalidCastException</c> that fails the entire comparison; dropping the branch
    /// altogether turns it into a fold, and the attribute's own name is never consulted.
    /// </remarks>
    [Fact]
    public void AttributeConstructorReferencedOnALocalTypeDefinition_DoesNotFold()
    {
        Assert.False(Compare(
            [Generated("<M>g__L|3_0")],
            [Generated("<M>g__L|7_0")],
            Ordinals,
            ctorSpelling: AttributeCtorSpelling.MemberReferenceOnTypeDefinition).IsExact);
    }

    /// <summary>
    /// A scope ordinal that is not a number elides to the same form as a real generated
    /// member, so two genuinely
    /// different call targets compare equal — the same false-<c>Exact</c> class as a forged
    /// name, reached through a malformed one.
    /// </summary>
    /// <remarks>
    /// The two sides must elide to the <em>same</em> form for this to see the check. A
    /// control that merely asserts a malformed name is left alone would pass either way,
    /// because the raw names already differ.
    /// </remarks>
    [Fact]
    public void NonNumericScopeOrdinal_DoesNotFoldOntoANumericOne()
    {
        Assert.False(Compare(
            [Generated("<M>g__L|3_0")],
            [Generated("<M>g__L|x_0")],
            Ordinals).IsExact);
    }

    /// <summary>
    /// A member whose name is not a <c>g__</c> shape must not be elided as if it were. The
    /// rewrite assumes the three characters after the containing-method brackets are
    /// <c>g__</c> and re-emits them literally, so without the prefix test a raw name shaped
    /// <c>&lt;M&gt;abcd|1_2</c> is <em>mutated</em> into <c>&lt;M&gt;g__d|N_2</c> and folds
    /// onto the unrelated generated member <c>&lt;M&gt;g__d|5_2</c>.
    /// </summary>
    [Fact]
    public void NonLocalFunctionShape_IsNotRewrittenIntoOne()
    {
        Assert.False(Compare(
            [Generated("<M>g__d|5_2")],
            [Generated("<M>abcd|1_2")],
            Ordinals).IsExact);
    }

    /// <summary>
    /// Display classes and cached-delegate fields carry no containing-method name, so the
    /// ordinal is their only discriminator and folding it would merge unrelated closures.
    /// The <c>&lt;&gt;d__N</c> row is the one that pins the empty-brackets rule itself: the
    /// other two are additionally excluded by not being <c>g__</c> or <c>d__</c> shapes,
    /// so only this row fails if that rule is removed.
    /// </summary>
    [Theory]
    [InlineData("<>c__DisplayClass3_0", "<>c__DisplayClass7_0")]
    [InlineData("<>9__3_0", "<>9__7_0")]
    [InlineData("<>d__3", "<>d__7")]
    public void AnonymousShapes_NeverFold(string oldName, string newName)
    {
        Assert.False(Compare([Generated(oldName)], [Generated(newName)], Ordinals).IsExact);
    }

    /// <summary>
    /// Lambda bodies embed the same unstable ordinal, but no measured fidelity diff is
    /// attributable to them, so they are deliberately out of scope. This pins the
    /// exclusion so widening it is a decision rather than an accident.
    /// </summary>
    [Fact]
    public void LambdaShape_IsOutOfScope()
    {
        Assert.False(Compare([Generated("<M>b__3_0")], [Generated("<M>b__7_0")], Ordinals).IsExact);
    }

    /// <summary>A malformed ordinal is not an ordinal; the name is compared verbatim.</summary>
    [Theory]
    [InlineData("<M>d__3x", "<M>d__7x")]
    [InlineData("<M>d__", "<M>d__7")]
    [InlineData("<M>g__L|3_", "<M>g__L|7_")]
    [InlineData("<M>g__|3_0", "<M>g__|7_0")]
    public void MalformedOrdinals_NeverFold(string oldName, string newName)
    {
        Assert.False(Compare([Generated(oldName)], [Generated(newName)], Ordinals).IsExact);
    }

    /// <summary>
    /// Folding is name-directed, so two local functions of the same containing method must
    /// not be equated just because both sides renumber. The caller targets the first
    /// member on each side; swapping which local function is called is a real difference.
    /// </summary>
    [Fact]
    public void DistinctLocalFunctionNames_StayDistinct()
    {
        Assert.False(Compare([Generated("<M>g__A|3_0")], [Generated("<M>g__B|3_0")], Ordinals).IsExact);
    }

    /// <summary>
    /// The elided form is substituted into the compared text, so it shares a namespace
    /// with every raw name in either assembly. A member literally named with the
    /// placeholder must not become indistinguishable from a folded one, or a real change
    /// of call target reads as identical. The names here are unspellable in C# but legal
    /// in metadata, and this tool reads untrusted assemblies.
    /// <para>
    /// This control does <em>not</em> gate the ordinal-comparison dependency, though its
    /// shape suggests it might. NUL is collation-ignorable, so a culture-sensitive
    /// comparison on the diff path would equate a colliding raw name with the folded form
    /// — but the forgery here carries the placeholder's NUL, and the <c>#Strings</c> heap
    /// truncates it there, so what reaches the comparison is <c>&lt;M&gt;g__L|#</c>, which
    /// has lost its <c>_0</c> and equals the folded form under no comparison at all. The
    /// controls that do gate it are
    /// <see cref="CollationCollidingName_DoesNotHideARealTargetChange"/>, whose forgery
    /// omits the NUL and so survives the heap, and
    /// <see cref="PlaceholderCollidingTypeName_DoesNotHideARealTargetChange"/>, whose
    /// placeholder is last in the name so truncation leaves exactly the collating prefix.
    /// </para>
    /// <para>
    /// The colliding name is derived from <see cref="CompilerGeneratedOrdinalCorrespondence.OrdinalPlaceholder"/>
    /// rather than written out, so the control follows the constant. A placeholder changed
    /// to any spellable text — not just the historical <c>#</c> — makes this name reproduce
    /// the folded form exactly and fails here.
    /// </para>
    /// </summary>
    [Fact]
    public void PlaceholderCollidingName_DoesNotHideARealTargetChange()
    {
        var result = Compare(
            [Generated("<M>g__L|3_0")],
            [Plain($"<M>g__L|{CompilerGeneratedOrdinalCorrespondence.OrdinalPlaceholder}_0"), Generated("<M>g__L|7_0")],
            Ordinals);

        Assert.False(result.IsExact);
    }

    /// <summary>The same collision on the type side, likewise derived from the constant.</summary>
    [Fact]
    public void PlaceholderCollidingTypeName_DoesNotHideARealTargetChange()
    {
        var result = Compare(
            [Generated("<M>d__3")],
            [Plain($"<M>d__{CompilerGeneratedOrdinalCorrespondence.OrdinalPlaceholder}"), Generated("<M>d__7")],
            Ordinals);

        Assert.False(result.IsExact);
    }

    /// <summary>
    /// Building the correspondence enumerates every type and method in both assemblies —
    /// exposure the comparison itself does not have. Malformed metadata in a type the
    /// comparison never touches must therefore not turn a comparison that succeeds without
    /// this normalization into a thrown exception.
    /// </summary>
    /// <remarks>
    /// The corruption points the unrelated <c>&lt;Module&gt;</c> type's name at a string
    /// heap offset past the end of the heap. The first assertion is load-bearing: it fails
    /// if the byte patch damaged anything the comparison actually reads, so this test can
    /// only pass while the corruption really is confined to metadata the un-normalized
    /// comparison ignores.
    /// </remarks>
    [Fact]
    public void MalformedUnrelatedMetadata_FailsClosedRatherThanThrowing()
    {
        byte[] image = CorruptUnrelatedTypeName(BuildImage("Probe", [Generated("<M>g__L|3_0")]));

        using var pe = new PEReader(new MemoryStream(image));
        using var other = new PEReader(new MemoryStream(image));

        Assert.True(Compare(pe, other, IlBodyDiffNormalization.None).IsExact);
        Assert.True(Compare(pe, other, Ordinals).IsExact);
    }

    /// <summary>
    /// Repoints every reference to the <c>&lt;Module&gt;</c> type's name at an offset past
    /// the end of the string heap.
    /// </summary>
    static byte[] CorruptUnrelatedTypeName(byte[] image)
    {
        int offset;
        using (var pe = new PEReader(new MemoryStream(image)))
        {
            var reader = pe.GetMetadataReader();
            var module = reader.GetTypeDefinition(MetadataTokens.TypeDefinitionHandle(1));
            Assert.Equal("<Module>", reader.GetString(module.Name));
            offset = MetadataTokens.GetHeapOffset(module.Name);
        }

        Assert.InRange(offset, 1, ushort.MaxValue);
        byte lo = (byte)offset;
        byte hi = (byte)(offset >> 8);

        var patched = (byte[])image.Clone();
        for (int i = 0; i < patched.Length - 1; i++)
        {
            if (patched[i] == lo && patched[i + 1] == hi)
            {
                patched[i] = 0xF0;
                patched[i + 1] = 0x7F;
            }
        }

        return patched;
    }

    static IlBodyDiffResult Compare(PEReader oldPe, PEReader newPe, IlBodyDiffNormalization normalization)
        => IlAssemblyDiff.CompareMembers(
            oldPe,
            oldPe.GetMetadataReader(),
            MetadataTokens.MethodDefinitionHandle(1),
            newPe,
            newPe.GetMetadataReader(),
            MetadataTokens.MethodDefinitionHandle(1),
            normalization: normalization).Diff;

    /// <summary>
    /// A decoder that really does return a name containing NUL. Under the default decoder
    /// such a name cannot exist, which is what makes <c>OrdinalPlaceholder</c> safe to
    /// embed in compared text and <c>KeySeparator</c> safe to flatten keys with. The two
    /// consequences differ: the placeholder does reach the rendered operand, while keys
    /// are never rendered, so the separator's safety is about injectivity rather than
    /// about output.
    /// </summary>
    sealed class NulReturningDecoder() : MetadataStringDecoder(Encoding.UTF8)
    {
        public override unsafe string GetString(byte* bytes, int byteCount)
            => $"<M>g__L|{CompilerGeneratedOrdinalCorrespondence.OrdinalPlaceholder}_0";
    }

    /// <summary>
    /// The hazard the decoder check exists for is real, not hypothetical: a decoder can
    /// hand back a name containing NUL, and therefore a name that spells the elided form
    /// exactly. Nothing about the <c>#Strings</c> heap prevents this, because the decoder
    /// runs after the heap is read.
    /// </summary>
    [Fact]
    public void AStringDecoderCanReturnANameContainingNul()
    {
        using var pe = new PEReader(new MemoryStream(BuildImage("Probe", [Generated("<M>g__L|3_0")])));
        var hostile = pe.GetMetadataReader(MetadataReaderOptions.Default, new NulReturningDecoder());

        string name = hostile.GetString(
            hostile.GetMethodDefinition(MetadataTokens.MethodDefinitionHandle(2)).Name);

        Assert.Contains('\0', name);
        Assert.Equal($"<M>g__L|{CompilerGeneratedOrdinalCorrespondence.OrdinalPlaceholder}_0", name);
    }

    /// <summary>
    /// The NUL unspellability argument holds for the default string decoder only, so a
    /// reader carrying any other decoder folds nothing. The check is on decoder identity
    /// rather than on whether a decoder looks dangerous: the decoder used here is an
    /// ordinary UTF-8 one that behaves exactly like the default and is still refused,
    /// because the correspondence declines to reason about decoders it did not establish
    /// the argument for.
    /// </summary>
    /// <remarks>
    /// This replaces an earlier source-text scan that looked for the decoder type being
    /// named in this repository. That gate was bypassable — a line opening with <c>*/</c>
    /// escaped its comment filter, and <c>MetadataStringDecod\u0065r</c> escaped it
    /// entirely — and it could say nothing at all about callers outside this repository.
    /// Checking the reader is neither lexical nor repository-scoped.
    /// <para>
    /// A hostile decoder is deliberately not used here. One that returns the elided form
    /// for every name is refused by the parser anyway, so the test would pass with the
    /// check deleted; a decoder that folds under the default rules is what makes this
    /// non-vacuous.
    /// </para>
    /// <para>
    /// The rows are asymmetric on purpose. Passing one reader as both sides — which an
    /// earlier version of this test did — leaves each half of the check redundant with the
    /// other, so either could be deleted with the suite green while the surviving half hid
    /// it. Only a row whose <em>other</em> side is default can observe one half alone.
    /// </para>
    /// <para>
    /// The image carries a generated <em>type</em> as well as a generated method, and both
    /// sides of the correspondence are asserted. Checking only the method side would leave
    /// the claim narrower than its prose: the type index is built by the same call, and a
    /// change that kept indexing types under a custom decoder while clearing only the
    /// method maps would satisfy a method-only assertion.
    /// <see cref="DefaultStringDecoder_StillFolds"/> carries the matching type assertion,
    /// so neither half is vacuous.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void NonDefaultStringDecoder_FoldsNothing(bool oldIsCustom, bool newIsCustom)
    {
        using var pe = new PEReader(new MemoryStream(
            BuildImage("Probe", [Generated("<M>g__L|3_0")], generatedTypes: ["<M>d__3"])));
        // An ordinary UTF-8 decoder: it decodes exactly as the default one does, so the
        // image still folds under the default rules and only decoder identity differs.
        var custom = pe.GetMetadataReader(
            MetadataReaderOptions.Default,
            new MetadataStringDecoder(Encoding.UTF8));
        var standard = pe.GetMetadataReader();

        var (oldSide, newSide) = CompilerGeneratedOrdinalCorrespondence.Build(
            oldIsCustom ? custom : standard,
            newIsCustom ? custom : standard);

        Assert.False(oldSide.TryGetMethodName(MetadataTokens.MethodDefinitionHandle(2), out _));
        Assert.False(newSide.TryGetMethodName(MetadataTokens.MethodDefinitionHandle(2), out _));

        var generatedType = GeneratedTypeHandle(standard, "<M>d__3");
        Assert.False(oldSide.TryGetTypeName(generatedType, out _));
        Assert.False(newSide.TryGetTypeName(generatedType, out _));
    }

    /// <summary>
    /// The complement, so <see cref="NonDefaultStringDecoder_FoldsNothing"/> cannot pass
    /// because the image happens to fold nothing anyway: the same image under the default
    /// decoder does fold, and folds to the elided form. Both the method and the type are
    /// asserted, because that test asserts both.
    /// </summary>
    [Fact]
    public void DefaultStringDecoder_StillFolds()
    {
        using var pe = new PEReader(new MemoryStream(
            BuildImage("Probe", [Generated("<M>g__L|3_0")], generatedTypes: ["<M>d__3"])));
        var reader = pe.GetMetadataReader();

        var (oldSide, _) = CompilerGeneratedOrdinalCorrespondence.Build(reader, reader);

        Assert.True(oldSide.TryGetMethodName(MetadataTokens.MethodDefinitionHandle(2), out string? folded));
        Assert.Equal($"<M>g__L|{CompilerGeneratedOrdinalCorrespondence.OrdinalPlaceholder}_0", folded);

        Assert.True(oldSide.TryGetTypeName(GeneratedTypeHandle(reader, "<M>d__3"), out string? foldedType));
        Assert.Equal($"<M>d__{CompilerGeneratedOrdinalCorrespondence.OrdinalPlaceholder}", foldedType);
    }

    /// <summary>
    /// Resolves a type definition by its metadata name. Looked up rather than assumed from
    /// a row number, so that changing what <see cref="BuildImage"/> emits cannot silently
    /// point an assertion at a different type.
    /// </summary>
    static TypeDefinitionHandle GeneratedTypeHandle(MetadataReader reader, string name)
    {
        foreach (var handle in reader.TypeDefinitions)
        {
            if (reader.GetString(reader.GetTypeDefinition(handle).Name) == name)
                return handle;
        }

        throw new InvalidOperationException($"No type definition named '{name}' in the probe image.");
    }

    /// <summary>
    /// The same collision reached through a <c>MemberReference</c> rather than a
    /// definition. This is the case an enumeration of indexed definition names misses: the
    /// reference's parent is a type reference scoped to this module, so the rendered
    /// operand agrees in opcode, scope, type, and signature, and only the member name
    /// distinguishes a genuinely different call target from a folded one.
    /// </summary>
    [Fact]
    public void PlaceholderCollidingReference_DoesNotHideARealTargetChange()
    {
        var result = Compare(
            [Generated("<M>g__L|3_0")],
            [Generated("<M>g__L|7_0")],
            Ordinals,
            newCallsReferenceNamed: $"<M>g__L|{CompilerGeneratedOrdinalCorrespondence.OrdinalPlaceholder}_0");

        Assert.False(result.IsExact);
    }

    /// <summary>
    /// The property the placeholder's safety rests on, asserted against the constant the
    /// product actually uses: an assembly that tries to spell the elided form cannot carry
    /// it, because the <c>#Strings</c> heap is NUL-terminated and truncates at the first
    /// NUL. That is what makes the elided form unequal to every name in the compared text
    /// without enumerating any of them.
    /// </summary>
    /// <remarks>
    /// This drives the assertion from <see cref="CompilerGeneratedOrdinalCorrespondence.OrdinalPlaceholder"/>
    /// rather than restating it, so a placeholder changed to any spellable text fails here
    /// — including one the <c>PlaceholderColliding*</c> controls would not notice on their
    /// own. The two halves are complementary: this one pins the constant's unspellability,
    /// and those three pin that unspellability is what prevents a hidden target change.
    /// </remarks>
    [Fact]
    public void PlaceholderCannotBeSpelledByAMetadataName()
    {
        string elided = $"<M>g__L|{CompilerGeneratedOrdinalCorrespondence.OrdinalPlaceholder}_0";
        using var pe = new PEReader(new MemoryStream(
            BuildImage("Probe", [Generated(elided)])));

        var reader = pe.GetMetadataReader();
        foreach (var handle in reader.TypeDefinitions)
        {
            var type = reader.GetTypeDefinition(handle);
            Assert.NotEqual(elided, reader.GetString(type.Name));
            foreach (var methodHandle in type.GetMethods())
                Assert.NotEqual(elided, reader.GetString(reader.GetMethodDefinition(methodHandle).Name));
        }
    }

    /// <summary>
    /// The property the key separator's injectivity rests on, asserted against the constant
    /// the product actually uses: a metadata name cannot contain the separator, so no
    /// forged name can reproduce a different segmentation of the same flattened key.
    /// </summary>
    /// <remarks>
    /// Driven from <see cref="CompilerGeneratedOrdinalCorrespondence.KeySeparator"/>, so a separator
    /// changed to any spellable character fails here. That is the half
    /// <c>ForgedKeySegmentation_DoesNotFoldAcrossDeclaringTypes</c> cannot see: its attack
    /// is written against the historical <c>.</c>/<c>+</c>/<c>::</c> joining, so it does
    /// not fire for an arbitrary spellable single-character separator.
    /// </remarks>
    [Fact]
    public void KeySeparatorCannotBeSpelledByAMetadataName()
    {
        string forged = $"A{CompilerGeneratedOrdinalCorrespondence.KeySeparator}B";
        using var pe = new PEReader(new MemoryStream(
            BuildImage("Probe", [Generated("<M>g__L|3_0")], typeName: forged)));

        var reader = pe.GetMetadataReader();
        foreach (var handle in reader.TypeDefinitions)
            Assert.NotEqual(forged, reader.GetString(reader.GetTypeDefinition(handle).Name));
    }

    /// <summary>
    /// A key is a flattened sequence of segments, so the flattening has to be injective.
    /// A method named <c>&lt;M&gt;g__L::&lt;N&gt;g__X|3_0</c> on type <c>C</c> and a method
    /// named <c>&lt;N&gt;g__X|7_0</c> on a type named <c>C::&lt;M&gt;g__L</c> are different
    /// members of different types, but a spellable separator flattens both to the same key.
    /// Each is unique on its own side, so both pass the two-sided ambiguity check, and the
    /// rendered operand concatenates the same way — so folding them equates two genuinely
    /// different call targets.
    /// </summary>
    /// <remarks>
    /// This pins the concrete historical shape — a <c>.</c>/<c>+</c> path joined to the
    /// member name by <c>::</c> — and fails when that scheme is restored. It does not by
    /// itself prove every spellable separator is unsafe; a single-character separator
    /// defeats this particular name while remaining forgeable by a name containing that
    /// character. The general property rests on the separator being unspellable, which is
    /// the property <see cref="KeySeparatorCannotBeSpelledByAMetadataName"/> asserts.
    /// </remarks>
    [Fact]
    public void ForgedKeySegmentation_DoesNotFoldAcrossDeclaringTypes()
    {
        using var oldPe = new PEReader(new MemoryStream(
            BuildImage("Probe", [Generated("<M>g__L::<N>g__X|3_0")])));
        using var newPe = new PEReader(new MemoryStream(
            BuildImage("Probe", [Generated("<N>g__X|7_0")], typeName: "C::<M>g__L")));

        Assert.False(Compare(oldPe, newPe, IlBodyDiffNormalization.None).IsExact);
        Assert.False(Compare(oldPe, newPe, Ordinals).IsExact);
    }

    /// <summary>
    /// Type-side counterpart of <see cref="NameShapeAlone_DoesNotFold"/>. Eligibility is a
    /// property of the attribute, not of the name, on both sides of the correspondence.
    /// Two types whose names take a generated shape but carry no attribute name different
    /// state machines, so folding them equates two genuinely different call targets.
    /// </summary>
    /// <remarks>
    /// The method-side rule was gated from the start and the type-side rule was not, which
    /// is the same asymmetry the ambiguity checks had: a rule mirrored in the product but
    /// not in its controls.
    /// <para>
    /// This pins the type-side <em>outcome</em>, not the specific call site that produces
    /// it. Eligibility is computed twice — once when indexing the type and again when
    /// building an enclosing key prefix — and the second keeps an unattributed name raw on
    /// its own, so deleting the first leaves this control green. Distinguishing them needs
    /// a nested-type fixture; that branch is tracked as unverified in the class remarks.
    /// </para>
    /// </remarks>
    [Fact]
    public void TypeNameShapeAlone_DoesNotFold()
    {
        Assert.False(CompareTypes(["<M>d__3"], ["<M>d__7"], Ordinals, typesAttributed: false).IsExact);
    }

    /// <summary>
    /// Type-side counterpart of the method ambiguity controls. The new side declares two
    /// generated types that elide to one key, so that key identifies no single counterpart
    /// and nothing may fold — the old side's type must still be compared under its real
    /// name. Deleting the <c>newIndex.AmbiguousTypes</c> check makes the index keep its
    /// first-seen type, fold the two onto each other, and hide a changed target.
    /// </summary>
    /// <remarks>
    /// The two sides deliberately share no ordinal. <c>SideIndex.Add</c> keeps the
    /// first-seen handle, so a control whose sides agree on their first ordinal folds to
    /// the same text either way and cannot see the check at all — the mistake round one
    /// found on the method side.
    /// </remarks>
    [Fact]
    public void AmbiguousGeneratedTypeOnNewSide_DoesNotFold()
    {
        var result = CompareTypes(["<M>d__3"], ["<M>d__5", "<M>d__9"], Ordinals);

        Assert.False(result.IsExact);
    }

    /// <summary>
    /// The same on the old side, so that deleting either type ambiguity check alone is
    /// caught by one of the pair rather than only their simultaneous deletion.
    /// </summary>
    [Fact]
    public void AmbiguousGeneratedTypeOnOldSide_DoesNotFold()
    {
        var result = CompareTypes(["<M>d__3", "<M>d__7"], ["<M>d__5"], Ordinals);

        Assert.False(result.IsExact);
    }

    /// <summary>
    /// The positive case the two controls above are measured against: one generated type
    /// per side, differing only in ordinal, folds and compares equal. Without this, the
    /// pair could pass by nothing ever folding.
    /// </summary>
    [Fact]
    public void UnambiguousGeneratedType_FoldsAcrossOrdinals()
    {
        Assert.False(CompareTypes(["<M>d__3"], ["<M>d__5"], IlBodyDiffNormalization.None).IsExact);
        Assert.True(CompareTypes(["<M>d__3"], ["<M>d__5"], Ordinals).IsExact);
    }

    /// <summary>
    /// An eligible type on the old side whose key has no counterpart on the new side must
    /// be skipped, not resolved against a default handle. Both sides fold here — so the
    /// early empty-index return does not hide the lookup — but the keys disagree, which is
    /// the only arrangement that reaches the miss.
    /// </summary>
    /// <remarks>
    /// The failure this prevents is a <c>KeyNotFoundException</c> raised while naming the
    /// counterpart, so the assertion is that the comparison completes at all. Like
    /// <see cref="MalformedGeneratedName_DoesNotCrashTheComparison"/> this pins that a
    /// declined fold stays a declined fold rather than becoming a failed comparison.
    /// </remarks>
    [Fact]
    public void TypeWithoutACounterpart_DoesNotCrashTheComparison()
    {
        Assert.False(CompareTypes(["<A>d__3"], ["<B>d__3"], Ordinals).IsExact);
    }

    /// <summary>
    /// The local-function separator is found from the right. No Roslyn-emitted name needs
    /// this — a local function nested inside another is named after the outermost method,
    /// so it carries exactly one separator — but IL may spell a name with several, and
    /// which one is chosen decides whether such a name folds at all.
    /// </summary>
    /// <remarks>
    /// This exists because the choice was previously invisible: scanning from the left
    /// passed the entire suite. A reviewer then explained the code's use of
    /// <c>LastIndexOf</c> by supposing Roslyn emits <c>&lt;M&gt;g__Outer|Inner|N_K</c> for
    /// nested local functions. It does not; compiling one emits
    /// <c>&lt;M&gt;g__Inner|0_1</c>. The rationale was wrong and nothing contradicted it,
    /// which is the failure this control closes — the behavior is now pinned by a test
    /// rather than by a plausible story.
    /// <para>
    /// A later reviewer read this test the other way, as a soundness hole: two attributed
    /// methods <c>&lt;M&gt;g__a|b|1_2</c> and <c>&lt;M&gt;g__a|b|3_2</c> are distinct
    /// members and this asserts they compare equal. They do — and so do
    /// <c>&lt;M&gt;g__a|1_2</c> against <c>&lt;M&gt;g__a|3_2</c>, and <c>&lt;M&gt;d__1</c>
    /// against <c>&lt;M&gt;d__3</c>, which is the whole feature: a pair differing in
    /// nothing but the member ordinal is exactly what folds, per
    /// <see cref="LocalFunctionOrdinal_FoldsWhenTheKeyIsUniqueOnBothSides"/>. Declining
    /// multiple separators would therefore buy no safety, because the same forged pair
    /// spelled with one separator still folds. What bounds the residue is the two-sided
    /// uniqueness requirement — two <em>genuinely</em> different local functions sharing a
    /// containing-method name and slot (overloaded <c>M</c>, each with a local <c>a</c>)
    /// differ only in the scope ordinal too, and are refused because the elided key is
    /// then ambiguous on both sides.
    /// </para>
    /// </remarks>
    [Fact]
    public void MultiplePipesInAGeneratedName_SplitAtTheLast()
    {
        Assert.True(Compare(
            [Generated("<M>g__a|b|1_2")],
            [Generated("<M>g__a|b|3_2")],
            Ordinals).IsExact);
    }

    /// <summary>
    /// The pair the previous test's remarks appeal to, taken from the compiler rather than
    /// invented: two overloads of <c>M</c> each declaring a local function <c>a</c> emit
    /// <c>&lt;M&gt;g__a|0_0</c> and <c>&lt;M&gt;g__a|1_0</c> — genuinely different members
    /// differing in nothing but the scope ordinal. Both elide to the same key, so each
    /// side is ambiguous and neither folds.
    /// </summary>
    /// <remarks>
    /// <see cref="Compare"/> makes the caller target the <em>first</em> member, so the two
    /// sides here call different local functions. Were the ambiguous key folded, both
    /// operands would render as the elided form and this would compare exact, hiding a
    /// real difference in the call target.
    /// <para>
    /// This is an artifact canary, not a new gate. The method-side ambiguity checks are
    /// already gated by
    /// <see cref="UniqueAgainstAmbiguous_DoesNotFoldOntoAnArbitraryCounterpart"/> and
    /// <see cref="AmbiguousAgainstUnique_DoesNotFoldOntoAnArbitraryCounterpart"/>, which
    /// discriminate the two sides: measured, deleting the new-side check alone fails the
    /// first and nothing else, and deleting the old-side check alone fails the second and
    /// nothing else. Deleting both fails those two plus
    /// <see cref="AmbiguousOnBothSides_KeepsDistinctMembersDistinct"/> and this test. So
    /// this adds no discrimination the class already lacks.
    /// </para>
    /// <para>
    /// What it does add is that the shape those controls model is one Roslyn actually
    /// emits. Measured by compiling the overload pair and reading the names back out of
    /// the image, because the claim that two different local functions can differ only in
    /// the scope ordinal is a claim about the compiler, not about this assembly.
    /// </para>
    /// </remarks>
    [Fact]
    public void RealOverloadShape_IsRefusedAsAmbiguous()
    {
        Assert.False(Compare(
            [Generated("<M>g__a|0_0"), Generated("<M>g__a|1_0")],
            [Generated("<M>g__a|1_0"), Generated("<M>g__a|0_0")],
            Ordinals).IsExact);
    }

    /// <summary>
    /// The collision that survives the <c>#Strings</c> heap. The three
    /// <c>PlaceholderColliding*</c> controls forge names containing the placeholder's NUL,
    /// which the heap truncates — so the forged method name arrives as
    /// <c>&lt;M&gt;g__L|#</c>, having lost its <c>_0</c>, and differs from the elided form
    /// under any comparison. A hostile assembly would instead spell the collision
    /// <em>without</em> the NUL: <c>&lt;M&gt;g__L|#_0</c> is a legal metadata name, reaches
    /// the comparison intact, and is equal to the elided <c>&lt;M&gt;g__L|#\0_0</c> under
    /// every culture-sensitive comparison, because NUL is collation-ignorable.
    /// </summary>
    /// <remarks>
    /// This is the method-side gate for the ordinal-comparison dependency documented on
    /// <c>OrdinalPlaceholder</c>. Making <c>IlBodyDiff.CanonicalEquals</c> culture-sensitive
    /// fails this test and
    /// <see cref="PlaceholderCollidingTypeName_DoesNotHideARealTargetChange"/>, and nothing
    /// else; the two NUL-bearing method controls cannot observe it, for the truncation
    /// reason above. The type control observes it only incidentally — its placeholder is
    /// last in the name, so truncation leaves exactly the collating prefix.
    /// <para>
    /// Ordinally the two names differ, so the changed call target stays visible, which is
    /// what this asserts.
    /// </para>
    /// </remarks>
    [Fact]
    public void CollationCollidingName_DoesNotHideARealTargetChange()
    {
        var result = Compare(
            [Generated("<M>g__L|3_0")],
            [Plain("<M>g__L|#_0"), Generated("<M>g__L|7_0")],
            Ordinals);

        Assert.False(result.IsExact);
    }

    /// <summary>
    /// Two generic methods differing only in arity stay distinct. The barrier here is the
    /// operand renderer, not this correspondence: a well-formed generic method declares its
    /// count in its signature, and <c>IlBodyDiff.FormatCall</c> spells that as an arity
    /// tick, so <c>&lt;M&gt;g__L|#_0`1</c> and <c>&lt;M&gt;g__L|#_0`2</c> differ even after
    /// both names fold to one.
    /// </summary>
    /// <remarks>
    /// This test therefore does <em>not</em> gate the arity term in the method key —
    /// deleting that term leaves it green. It is stated here because the distinction is
    /// easy to get backwards: this is the Roslyn-shaped case, and Roslyn's shape is safe
    /// for a reason that belongs to the renderer.
    /// <see cref="MethodsWhoseSignatureHidesTheirArity_DoNotFold"/> is the case the key
    /// term actually carries.
    /// </remarks>
    [Fact]
    public void MethodsDifferingOnlyInGenericArity_DoNotFold()
    {
        var result = Compare(
            [GenericGenerated("<M>g__L|3_0", 1)],
            [GenericGenerated("<M>g__L|7_0", 2)],
            Ordinals);

        Assert.False(result.IsExact);
    }

    /// <summary>
    /// The case the method key's arity term carries. A method's arity is recorded twice —
    /// in the <c>GenericParam</c> table and in the signature — and nothing in the format
    /// ties the two together. When the signature says non-generic, the renderer spells no
    /// arity tick, so two methods differing only in their <c>GenericParam</c> rows produce
    /// operands that are identical once their names fold.
    /// </summary>
    /// <remarks>
    /// Roslyn never emits this, but this tool reads untrusted assemblies, and folding here
    /// would report a changed call target as unchanged. Deleting the arity term from the
    /// method key fails this test and nothing else.
    /// <para>
    /// Discovered by review: the original version of this control used a fixture that
    /// emitted <c>GenericParam</c> rows without declaring them in the signature, so it was
    /// testing this case while claiming to test the Roslyn-shaped one above.
    /// </para>
    /// </remarks>
    [Fact]
    public void MethodsWhoseSignatureHidesTheirArity_DoNotFold()
    {
        var result = Compare(
            [GenericGeneratedWithNonGenericSignature("<M>g__L|3_0", 1)],
            [GenericGeneratedWithNonGenericSignature("<M>g__L|7_0", 2)],
            Ordinals);

        Assert.False(result.IsExact);
    }

    [Fact]
    public void TypesDifferingOnlyInGenericArity_DoNotFold()
    {
        var result = CompareTypes(
            ["<M>d__3"],
            ["<M>d__7"],
            oldTypeArities: [1],
            newTypeArities: [2]);

        Assert.False(result.IsExact);
    }

    /// <summary>
    /// The fixture builder really attaches the requested generic parameters to the
    /// requested owners. Without this, a builder that silently emitted arity 0 everywhere
    /// would make both arity controls vacuous while still passing them — and passing their
    /// tampers too, because a key term computed from zero is as constant as no term at all.
    /// </summary>
    /// <remarks>
    /// This reads the arity back out of the produced image rather than trusting the builder,
    /// and asserts the owners are distinguished: the method arity must not land on the type
    /// or vice versa. <c>GenericParam</c> is sorted by coded owner under
    /// <c>TypeOrMethodDef</c>, where TypeDef precedes MethodDef, so a builder that emitted
    /// the rows in declaration order would produce an image whose table is out of order;
    /// <see cref="MetadataReader"/> would still read it, which is why this asserts the
    /// association rather than merely that the rows exist.
    /// </remarks>
    [Fact]
    public void BuildImage_AttachesGenericParametersToTheRequestedOwners()
    {
        byte[] image = BuildImage(
            "Probe",
            [GenericGenerated("<M>g__L|3_0", 2), Generated("<M>g__P|4_0")],
            generatedTypes: ["<M>d__3", "<M>d__9"],
            generatedTypeArities: [1, 0]);

        using var pe = new PEReader(new MemoryStream(image));
        var reader = pe.GetMetadataReader();

        Assert.Equal(2, ArityOfMethod(reader, "<M>g__L|3_0"));
        Assert.Equal(0, ArityOfMethod(reader, "<M>g__P|4_0"));
        Assert.Equal(1, ArityOfType(reader, "<M>d__3"));
        Assert.Equal(0, ArityOfType(reader, "<M>d__9"));

        static int ArityOfMethod(MetadataReader reader, string name)
        {
            foreach (var typeHandle in reader.TypeDefinitions)
            {
                foreach (var handle in reader.GetTypeDefinition(typeHandle).GetMethods())
                {
                    var method = reader.GetMethodDefinition(handle);
                    if (reader.GetString(method.Name) == name)
                        return method.GetGenericParameters().Count;
                }
            }

            throw new InvalidOperationException($"no method named {name}");
        }

        static int ArityOfType(MetadataReader reader, string name)
            => reader.GetTypeDefinition(GeneratedTypeHandle(reader, name)).GetGenericParameters().Count;
    }

    static Member Generated(string name) => new(name, CompilerGenerated: true);

    /// <summary>
    /// A member carrying a real custom attribute that is not
    /// <c>CompilerGeneratedAttribute</c>, so the attribute inspection runs and has to
    /// reject it on identity rather than on absence.
    /// </summary>
    static Member Attributed(string name, string attributeNamespace, string attributeName)
        => new(name, CompilerGenerated: true, attributeNamespace, attributeName);

    static Member Plain(string name) => new(name, CompilerGenerated: false);

    /// <param name="CompilerGenerated">
    /// Whether the member carries a custom attribute at all. When it does,
    /// <paramref name="AttributeNamespace"/> and <paramref name="AttributeName"/> choose
    /// which one; both null means <c>CompilerGeneratedAttribute</c>.
    /// </param>
    readonly record struct Member(
        string Name,
        bool CompilerGenerated,
        string? AttributeNamespace = null,
        string? AttributeName = null,
        int GenericArity = 0,
        bool SignatureDeclaresArity = true,
        byte SignatureHeader = 0x00,
        byte[]? RawSignature = null);

    /// <summary>
    /// A compiler-generated member declaring <paramref name="arity"/> generic parameters.
    /// A method's arity lives in its signature and never in its name, so two of these are
    /// indistinguishable by name alone.
    /// </summary>
    static Member GenericGenerated(string name, int arity)
        => new(name, CompilerGenerated: true, GenericArity: arity);

    /// <summary>
    /// A compiler-generated member whose <c>GenericParam</c> rows say it is generic while
    /// its signature says it is not. Roslyn never emits this, but the two counts live in
    /// different tables and nothing in the format ties them together, so an untrusted
    /// assembly can disagree with itself. It matters here because the correspondence keys
    /// on the <c>GenericParam</c> count while the operand renderer reads the signature.
    /// </summary>
    static Member GenericGeneratedWithNonGenericSignature(string name, int arity)
        => new(name, CompilerGenerated: true, GenericArity: arity, SignatureDeclaresArity: false);

    /// <summary>
    /// How a fixture spells the constructor of the attribute it applies to its generated
    /// members. Roslyn emits the first spelling and a corelib build emits the second; the
    /// third is legal metadata that no C# compiler emits, and exists so the restriction to
    /// a <c>TypeReference</c> parent is a decision the suite holds rather than an
    /// assumption nothing tests.
    /// </summary>
    enum AttributeCtorSpelling
    {
        /// <summary>A <c>MemberReference</c> whose parent is a <c>TypeReference</c> into another assembly.</summary>
        TypeReference,

        /// <summary>A <c>MethodDefinition</c> on a type this assembly defines.</summary>
        MethodDefinition,

        /// <summary>A <c>MemberReference</c> whose parent is a <c>TypeDefinition</c> in this assembly.</summary>
        MemberReferenceOnTypeDefinition,
    }

    /// <summary>
    /// Compares two assemblies whose caller invokes a method on the first generated
    /// <em>type</em>, so the rendered operand carries the declaring type's name and the
    /// type index decides the outcome. <paramref name="typesAttributed"/> chooses whether
    /// those types actually carry <c>CompilerGeneratedAttribute</c>; passing
    /// <see langword="false"/> is how the type-side eligibility controls present a
    /// generated <em>name</em> with no attribute behind it.
    /// </summary>
    static IlBodyDiffResult CompareTypes(
        string[] oldTypes,
        string[] newTypes,
        IlBodyDiffNormalization normalization = Ordinals,
        bool typesAttributed = true,
        int[]? oldTypeArities = null,
        int[]? newTypeArities = null)
    {
        using var oldPe = new PEReader(new MemoryStream(BuildImage(
            "Probe", [], generatedTypes: oldTypes, typesAttributed: typesAttributed,
            generatedTypeArities: oldTypeArities)));
        using var newPe = new PEReader(new MemoryStream(BuildImage(
            "Probe", [], generatedTypes: newTypes, typesAttributed: typesAttributed,
            generatedTypeArities: newTypeArities)));
        return Compare(oldPe, newPe, normalization);
    }

    static IlBodyDiffResult Compare(
        Member[] oldMembers,
        Member[] newMembers,
        IlBodyDiffNormalization normalization = IlBodyDiffNormalization.None,
        string? newCallsReferenceNamed = null,
        AttributeCtorSpelling ctorSpelling = AttributeCtorSpelling.TypeReference,
        string localAttributeNamespace = "System.Runtime.CompilerServices",
        string localAttributeName = "CompilerGeneratedAttribute")
    {
        using var oldPe = new PEReader(new MemoryStream(
            BuildImage(
                "Probe",
                oldMembers,
                ctorSpelling: ctorSpelling,
                localAttributeNamespace: localAttributeNamespace,
                localAttributeName: localAttributeName)));
        using var newPe = new PEReader(new MemoryStream(
            BuildImage(
                "Probe",
                newMembers,
                newCallsReferenceNamed,
                ctorSpelling: ctorSpelling,
                localAttributeNamespace: localAttributeNamespace,
                localAttributeName: localAttributeName)));
        return IlAssemblyDiff.CompareMembers(
            oldPe,
            oldPe.GetMetadataReader(),
            MetadataTokens.MethodDefinitionHandle(1),
            newPe,
            newPe.GetMetadataReader(),
            MetadataTokens.MethodDefinitionHandle(1),
            normalization: normalization).Diff;
    }

    /// <summary>
    /// Emits an assembly whose first method calls one other method, chosen in this order:
    /// the member reference named by <paramref name="callReferenceNamed"/>; otherwise the
    /// first of <paramref name="generatedTypes"/>, so the rendered operand carries that
    /// type's name; otherwise the first of <paramref name="members"/>. The members and
    /// types that are not called exist only to populate the image, which is what makes a
    /// key ambiguous.
    /// </summary>
    static byte[] BuildImage(
        string assemblyName,
        Member[] members,
        string? callReferenceNamed = null,
        string typeName = "C",
        string[]? generatedTypes = null,
        bool typesAttributed = true,
        AttributeCtorSpelling ctorSpelling = AttributeCtorSpelling.TypeReference,
        string localAttributeNamespace = "System.Runtime.CompilerServices",
        string localAttributeName = "CompilerGeneratedAttribute",
        int[]? generatedTypeArities = null)
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

        var corlib = metadata.AddAssemblyReference(
            metadata.GetOrAddString("System.Runtime"),
            new Version(1, 0, 0, 0),
            default,
            default,
            default,
            default);
        // Attribute constructors are created on demand so a member can carry an attribute
        // that is deliberately not CompilerGeneratedAttribute.
        var attributeCtors = new Dictionary<(string Namespace, string Name), EntityHandle>();
        EntityHandle AttributeCtor(string attributeNamespace, string attributeName)
        {
            if (attributeCtors.TryGetValue((attributeNamespace, attributeName), out var existing))
                return existing;
            var attributeType = metadata.AddTypeReference(
                corlib,
                metadata.GetOrAddString(attributeNamespace),
                metadata.GetOrAddString(attributeName));
            // instance void .ctor(): HASTHIS, zero parameters, void return.
            var ctor = metadata.AddMemberReference(
                attributeType,
                metadata.GetOrAddString(".ctor"),
                metadata.GetOrAddBlob(new byte[] { 0x20, 0x00, 0x01 }));
            attributeCtors[(attributeNamespace, attributeName)] = ctor;
            return ctor;
        }

        bool definesAttributeLocally = ctorSpelling != AttributeCtorSpelling.TypeReference;
        EntityHandle compilerGeneratedCtor = definesAttributeLocally
            ? default
            : AttributeCtor("System.Runtime.CompilerServices", "CompilerGeneratedAttribute");

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
            metadata.GetOrAddString(typeName),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));

        // Each generated type owns exactly one method, laid out after the caller and the
        // members so the MethodList ranges stay contiguous and ascending.
        // GenericParam rows are emitted in one sorted pass below rather than as their
        // owners are declared: the table is sorted by coded owner, and TypeDef and
        // MethodDef interleave under TypeOrMethodDef exactly as they do for
        // CustomAttribute, so declaration order is not sorted order.
        var genericParameters = new List<(EntityHandle Owner, int Index)>();
        string[] extraTypes = generatedTypes ?? [];
        var generatedTypeHandles = new List<TypeDefinitionHandle>();
        for (int i = 0; i < extraTypes.Length; i++)
        {
            generatedTypeHandles.Add(metadata.AddTypeDefinition(
                TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed,
                default,
                metadata.GetOrAddString(extraTypes[i]),
                default,
                MetadataTokens.FieldDefinitionHandle(1),
                MetadataTokens.MethodDefinitionHandle(members.Length + 2 + i)));

            int arity = generatedTypeArities is { } arities && i < arities.Length ? arities[i] : 0;
            for (int g = 0; g < arity; g++)
                genericParameters.Add((generatedTypeHandles[i], g));
        }

        // An assembly may define CompilerGeneratedAttribute itself — System.Private.CoreLib
        // does — in which case its own generated members reference the constructor as a
        // MethodDefinition rather than through a MemberReference to another assembly.
        TypeDefinitionHandle localAttributeType = default;
        if (definesAttributeLocally)
        {
            localAttributeType = metadata.AddTypeDefinition(
                TypeAttributes.Public,
                metadata.GetOrAddString(localAttributeNamespace),
                metadata.GetOrAddString(localAttributeName),
                default,
                MetadataTokens.FieldDefinitionHandle(1),
                MetadataTokens.MethodDefinitionHandle(members.Length + 2 + extraTypes.Length));
        }

        var signature = metadata.GetOrAddBlob(new byte[] { 0x00, 0x00, 0x01 });

        // A generic method's signature carries GENERIC (0x10) and its own parameter count,
        // which is what the operand renderer reads. A member may deliberately omit it to
        // model an assembly whose two arity records disagree.
        BlobHandle SignatureFor(Member member) => member switch
        {
            { RawSignature: { } raw } => metadata.GetOrAddBlob(raw),
            { GenericArity: > 0, SignatureDeclaresArity: true }
                => metadata.GetOrAddBlob(
                    new byte[] { (byte)(0x10 | member.SignatureHeader), (byte)member.GenericArity, 0x00, 0x01 }),
            { SignatureHeader: not 0x00 }
                => metadata.GetOrAddBlob(new byte[] { member.SignatureHeader, 0x00, 0x01 }),
            _ => signature,
        };

        // A reference to a type named `C` scoped to this module renders under the same
        // scope and type as the definition above, so only the member name distinguishes
        // the two operands.
        MemberReferenceHandle reference = default;
        if (callReferenceNamed is not null)
        {
            reference = metadata.AddMemberReference(
                metadata.AddTypeReference(EntityHandle.ModuleDefinition, default, metadata.GetOrAddString("C")),
                metadata.GetOrAddString(callReferenceNamed),
                signature);
        }

        var bodies = new MethodBodyStreamEncoder(new BlobBuilder());
        var callerIl = new BlobBuilder();
        var caller = new InstructionEncoder(callerIl, new ControlFlowBuilder());
        // The first member is always method 2: method 1 is the caller emitted below.
        // With generated types present the caller instead targets the first such type's
        // method, so the rendered operand carries that type's name.
        if (callReferenceNamed is not null)
            caller.Call(reference);
        else if (extraTypes.Length > 0)
            caller.Call(MetadataTokens.MethodDefinitionHandle(members.Length + 2));
        else
            caller.Call(MetadataTokens.MethodDefinitionHandle(2));
        caller.OpCode(ILOpCode.Ret);
        int callerOffset = bodies.AddMethodBody(caller);

        var memberOffsets = new int[members.Length];
        for (int i = 0; i < members.Length; i++)
        {
            var il = new BlobBuilder();
            var encoder = new InstructionEncoder(il, new ControlFlowBuilder());
            encoder.OpCode(ILOpCode.Ret);
            memberOffsets[i] = bodies.AddMethodBody(encoder);
        }

        var extraOffsets = new int[extraTypes.Length];
        for (int i = 0; i < extraTypes.Length; i++)
        {
            var il = new BlobBuilder();
            var encoder = new InstructionEncoder(il, new ControlFlowBuilder());
            encoder.OpCode(ILOpCode.Ret);
            extraOffsets[i] = bodies.AddMethodBody(encoder);
        }

        int localCtorOffset = -1;
        if (definesAttributeLocally)
        {
            var il = new BlobBuilder();
            var encoder = new InstructionEncoder(il, new ControlFlowBuilder());
            encoder.OpCode(ILOpCode.Ret);
            localCtorOffset = bodies.AddMethodBody(encoder);
        }

        metadata.AddMethodDefinition(
            MethodAttributes.Public | MethodAttributes.Static,
            MethodImplAttributes.IL,
            metadata.GetOrAddString("Caller"),
            signature,
            callerOffset,
            MetadataTokens.ParameterHandle(1));

        var generated = new List<(MethodDefinitionHandle Handle, EntityHandle Ctor)>();
        for (int i = 0; i < members.Length; i++)
        {
            var handle = metadata.AddMethodDefinition(
                MethodAttributes.Public | MethodAttributes.Static,
                MethodImplAttributes.IL,
                metadata.GetOrAddString(members[i].Name),
                SignatureFor(members[i]),
                memberOffsets[i],
                MetadataTokens.ParameterHandle(1));
            for (int g = 0; g < members[i].GenericArity; g++)
                genericParameters.Add((handle, g));

            if (members[i].CompilerGenerated)
            {
                // A nil constructor means "the CompilerGeneratedAttribute one", resolved
                // below: with a local definition that row does not exist yet.
                EntityHandle ctor = members[i].AttributeNamespace is { } ns && members[i].AttributeName is { } an
                    ? AttributeCtor(ns, an)
                    : default;
                generated.Add((handle, ctor));
            }
        }

        // The CustomAttribute table must be sorted by its coded parent index. Methods and
        // types interleave under that encoding — HasCustomAttribute puts MethodDef at tag 0
        // and TypeDef at tag 3 — so the rows are sorted explicitly rather than appended in
        // declaration order.
        foreach (var handle in generatedTypeHandles)
        {
            metadata.AddMethodDefinition(
                MethodAttributes.Public | MethodAttributes.Static,
                MethodImplAttributes.IL,
                metadata.GetOrAddString("M"),
                signature,
                extraOffsets[generatedTypeHandles.IndexOf(handle)],
                MetadataTokens.ParameterHandle(1));
        }

        if (definesAttributeLocally)
        {
            var localCtor = metadata.AddMethodDefinition(
                MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.RTSpecialName,
                MethodImplAttributes.IL,
                metadata.GetOrAddString(".ctor"),
                metadata.GetOrAddBlob(new byte[] { 0x20, 0x00, 0x01 }),
                localCtorOffset,
                MetadataTokens.ParameterHandle(1));
            // Naming that same constructor through a MemberReference on its own
            // TypeDefinition is legal metadata; only the first spelling is what a C#
            // compiler emits.
            compilerGeneratedCtor = ctorSpelling == AttributeCtorSpelling.MemberReferenceOnTypeDefinition
                ? metadata.AddMemberReference(
                    localAttributeType,
                    metadata.GetOrAddString(".ctor"),
                    metadata.GetOrAddBlob(new byte[] { 0x20, 0x00, 0x01 }))
                : localCtor;
        }

        var attributeTargets = new List<(int Coded, EntityHandle Parent, EntityHandle Ctor)>();
        foreach (var (handle, ctor) in generated)
        {
            attributeTargets.Add((
                MetadataTokens.GetRowNumber(handle) << 5,
                handle,
                ctor.IsNil ? compilerGeneratedCtor : ctor));
        }
        if (typesAttributed)
        {
            foreach (var handle in generatedTypeHandles)
                attributeTargets.Add(((MetadataTokens.GetRowNumber(handle) << 5) | 3, handle, compilerGeneratedCtor));
        }
        attributeTargets.Sort((left, right) => left.Coded.CompareTo(right.Coded));

        foreach (var (_, parent, ctor) in attributeTargets)
        {
            metadata.AddCustomAttribute(
                parent,
                ctor,
                metadata.GetOrAddBlob(new byte[] { 0x01, 0x00, 0x00, 0x00 }));
        }

        // TypeOrMethodDef puts TypeDef at tag 0 and MethodDef at tag 1, so the coded owner
        // is (row << 1) | tag and the two kinds interleave: MethodDef row 2 codes to 5 and
        // sorts ahead of TypeDef row 5, which codes to 10. Sorting on the code rather than
        // emitting per owner is what keeps the table valid for an image that carries both.
        foreach (var (owner, index) in genericParameters.OrderBy(CodedOwner).ThenBy(e => e.Index))
        {
            metadata.AddGenericParameter(
                owner,
                GenericParameterAttributes.None,
                metadata.GetOrAddString($"T{index}"),
                index);
        }

        static int CodedOwner((EntityHandle Owner, int Index) entry)
            => (MetadataTokens.GetRowNumber(entry.Owner) << 1)
                | (entry.Owner.Kind == HandleKind.MethodDefinition ? 1 : 0);

        var pe = new ManagedPEBuilder(
            PEHeaderBuilder.CreateLibraryHeader(),
            new MetadataRootBuilder(metadata),
            bodies.Builder,
            flags: CorFlags.ILOnly);
        var image = new BlobBuilder();
        pe.Serialize(image);
        return image.ToArray();
    }
}
