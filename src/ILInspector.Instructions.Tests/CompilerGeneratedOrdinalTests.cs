using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Text;

namespace ILInspector.Instructions.Tests;

/// <summary>
/// Controls for <see cref="IlBodyDiffNormalization.NormalizeCompilerGeneratedOrdinals"/>.
/// Every case is expressed as a whole-image comparison through the public diff seam, so
/// the eligibility rules, the <c>CompilerGeneratedAttribute</c> gate and the two-sided
/// uniqueness requirement are exercised together rather than asserted about a helper.
/// </summary>
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
        Assert.False(Compare([Generated("<M>d__3")], [Generated("<M>d__7")]).IsExact);
        Assert.True(Compare([Generated("<M>d__3")], [Generated("<M>d__7")], Ordinals).IsExact);
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
    /// reaches the parser and is rejected by a different guard; without that guard the
    /// parser indexes outside the name and the whole diff fails with an exception, which
    /// is a comparison the caller would otherwise have completed.
    /// </summary>
    /// <remarks>
    /// The two sides are identical, so the assertion is that a body compares equal to
    /// itself. That is the weakest claim that still fails on a throw, and it cannot pass
    /// for the wrong reason the way an inequality assertion could.
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
    /// nothing else in the suite reaches that branch, so the loss would be silent.
    /// </remarks>
    [Fact]
    public void LocallyDefinedCompilerGeneratedAttribute_IsRecognized()
    {
        Assert.True(Compare(
            [Generated("<M>g__L|3_0")],
            [Generated("<M>g__L|7_0")],
            Ordinals,
            localAttributeDefinition: true).IsExact);
    }


    /// not a number elides to the same form as a real generated member, so two genuinely
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
    /// This control and its two siblings also gate the ordinal-comparison dependency the
    /// placeholder rests on. NUL is collation-ignorable under ICU, so a culture-sensitive
    /// comparison anywhere on the diff path reports the colliding raw name and the folded
    /// form as equal and restores the collision. Making <c>IlBodyDiff.CanonicalEquals</c>
    /// culture-sensitive fails all three.
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
    /// such a name cannot exist, which is the whole basis for <c>OrdinalPlaceholder</c> and
    /// <c>KeySeparator</c> being safe to embed in compared text.
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
    /// </remarks>
    [Fact]
    public void NonDefaultStringDecoder_FoldsNothing()
    {
        using var pe = new PEReader(new MemoryStream(BuildImage("Probe", [Generated("<M>g__L|3_0")])));
        var reader = pe.GetMetadataReader(
            MetadataReaderOptions.Default,
            new MetadataStringDecoder(Encoding.UTF8));

        var (oldSide, newSide) = CompilerGeneratedOrdinalCorrespondence.Build(reader, reader);

        Assert.False(oldSide.TryGetMethodName(MetadataTokens.MethodDefinitionHandle(2), out _));
        Assert.False(newSide.TryGetMethodName(MetadataTokens.MethodDefinitionHandle(2), out _));
    }

    /// <summary>
    /// The complement, so <see cref="NonDefaultStringDecoder_FoldsNothing"/> cannot pass
    /// because the image happens to fold nothing anyway: the same image under the default
    /// decoder does fold, and folds to the elided form.
    /// </summary>
    [Fact]
    public void DefaultStringDecoder_StillFolds()
    {
        using var pe = new PEReader(new MemoryStream(BuildImage("Probe", [Generated("<M>g__L|3_0")])));
        var reader = pe.GetMetadataReader();

        var (oldSide, _) = CompilerGeneratedOrdinalCorrespondence.Build(reader, reader);

        Assert.True(oldSide.TryGetMethodName(MetadataTokens.MethodDefinitionHandle(2), out string? folded));
        Assert.Equal($"<M>g__L|{CompilerGeneratedOrdinalCorrespondence.OrdinalPlaceholder}_0", folded);
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
    /// the same property <c>PlaceholderCannotBeSpelledByAMetadataName</c> asserts.
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
        string? AttributeName = null);

    /// <summary>
    /// Compares two assemblies whose caller invokes a method on the first
    /// <c>CompilerGeneratedAttribute</c>-bearing <em>type</em>, so the rendered operand
    /// carries the declaring type's name and the type index decides the outcome.
    /// </summary>
    static IlBodyDiffResult CompareTypes(
        string[] oldTypes,
        string[] newTypes,
        IlBodyDiffNormalization normalization,
        bool typesAttributed = true)
    {
        using var oldPe = new PEReader(new MemoryStream(
            BuildImage("Probe", [], generatedTypes: oldTypes, typesAttributed: typesAttributed)));
        using var newPe = new PEReader(new MemoryStream(
            BuildImage("Probe", [], generatedTypes: newTypes, typesAttributed: typesAttributed)));
        return Compare(oldPe, newPe, normalization);
    }

    static IlBodyDiffResult Compare(
        Member[] oldMembers,
        Member[] newMembers,
        IlBodyDiffNormalization normalization = IlBodyDiffNormalization.None,
        string? newCallsReferenceNamed = null,
        bool localAttributeDefinition = false)
    {
        using var oldPe = new PEReader(new MemoryStream(
            BuildImage("Probe", oldMembers, localAttributeDefinition: localAttributeDefinition)));
        using var newPe = new PEReader(new MemoryStream(
            BuildImage("Probe", newMembers, newCallsReferenceNamed, localAttributeDefinition: localAttributeDefinition)));
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
    /// Emits an assembly whose first method calls the first of the supplied members. The
    /// remaining members exist only to populate the type, which is what makes a key
    /// ambiguous.
    /// </summary>
    static byte[] BuildImage(
        string assemblyName,
        Member[] members,
        string? callReferenceNamed = null,
        string typeName = "C",
        string[]? generatedTypes = null,
        bool typesAttributed = true,
        bool localAttributeDefinition = false)
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

        EntityHandle compilerGeneratedCtor = localAttributeDefinition
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
        }

        // An assembly may define CompilerGeneratedAttribute itself — System.Private.CoreLib
        // does — in which case its own generated members reference the constructor as a
        // MethodDefinition rather than through a MemberReference to another assembly.
        TypeDefinitionHandle localAttributeType = default;
        if (localAttributeDefinition)
        {
            localAttributeType = metadata.AddTypeDefinition(
                TypeAttributes.Public,
                metadata.GetOrAddString("System.Runtime.CompilerServices"),
                metadata.GetOrAddString("CompilerGeneratedAttribute"),
                default,
                MetadataTokens.FieldDefinitionHandle(1),
                MetadataTokens.MethodDefinitionHandle(members.Length + 2 + extraTypes.Length));
        }

        var signature = metadata.GetOrAddBlob(new byte[] { 0x00, 0x00, 0x01 });

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
        if (localAttributeDefinition)
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
                signature,
                memberOffsets[i],
                MetadataTokens.ParameterHandle(1));
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

        if (localAttributeDefinition)
        {
            compilerGeneratedCtor = metadata.AddMethodDefinition(
                MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.RTSpecialName,
                MethodImplAttributes.IL,
                metadata.GetOrAddString(".ctor"),
                metadata.GetOrAddBlob(new byte[] { 0x20, 0x00, 0x01 }),
                localCtorOffset,
                MetadataTokens.ParameterHandle(1));
            _ = localAttributeType;
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
