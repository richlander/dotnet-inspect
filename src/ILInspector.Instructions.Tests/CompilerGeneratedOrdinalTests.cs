using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

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
    [Fact]
    public void NameShapeAlone_DoesNotFold()
    {
        Assert.False(Compare([Plain("<M>g__L|3_0")], [Plain("<M>g__L|7_0")], Ordinals).IsExact);
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
    /// </summary>
    [Fact]
    public void PlaceholderCollidingName_DoesNotHideARealTargetChange()
    {
        var result = Compare(
            [Generated("<M>g__L|3_0")],
            [Plain("<M>g__L|#_0"), Generated("<M>g__L|7_0")],
            Ordinals);

        Assert.False(result.IsExact);
    }

    /// <summary>The same collision on the type side.</summary>
    [Fact]
    public void PlaceholderCollidingTypeName_DoesNotHideARealTargetChange()
    {
        var result = Compare(
            [Generated("<M>d__3")],
            [Plain("<M>d__#"), Generated("<M>d__7")],
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
            newCallsReferenceNamed: "<M>g__L|#_0");

        Assert.False(result.IsExact);
    }

    /// <summary>
    /// The property the placeholder's safety rests on: the <c>#Strings</c> heap is
    /// NUL-terminated, so a name read back through <see cref="MetadataReader"/> can never
    /// contain NUL however the assembly was written. That is what makes the elided form
    /// unequal to every name in the compared text without enumerating any of them.
    /// </summary>
    /// <remarks>
    /// This asserts the metadata property, not the constant — <c>OrdinalPlaceholder</c> is
    /// internal and this assembly has no <c>InternalsVisibleTo</c>. The constant is held to
    /// its visible half by the three <c>PlaceholderColliding*</c> controls, which fail if
    /// the placeholder becomes spellable as <c>#</c>. A placeholder changed to some other
    /// spellable text would defeat both, which is why the argument for NUL is recorded on
    /// the constant itself rather than left implicit.
    /// </remarks>
    [Fact]
    public void PlaceholderCannotBeSpelledByAMetadataName()
    {
        using var pe = new PEReader(new MemoryStream(
            BuildImage("Probe", [Generated("<M>g__L|#\0_0")])));

        var reader = pe.GetMetadataReader();
        foreach (var handle in reader.TypeDefinitions)
        {
            var type = reader.GetTypeDefinition(handle);
            Assert.DoesNotContain('\0', reader.GetString(type.Name));
            foreach (var methodHandle in type.GetMethods())
                Assert.DoesNotContain('\0', reader.GetString(reader.GetMethodDefinition(methodHandle).Name));
        }
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

    static Member Generated(string name) => new(name, CompilerGenerated: true);

    static Member Plain(string name) => new(name, CompilerGenerated: false);

    readonly record struct Member(string Name, bool CompilerGenerated);

    static IlBodyDiffResult Compare(
        Member[] oldMembers,
        Member[] newMembers,
        IlBodyDiffNormalization normalization = IlBodyDiffNormalization.None,
        string? newCallsReferenceNamed = null)
    {
        using var oldPe = new PEReader(new MemoryStream(BuildImage("Probe", oldMembers)));
        using var newPe = new PEReader(new MemoryStream(BuildImage("Probe", newMembers, newCallsReferenceNamed)));
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
        string typeName = "C")
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
        var attributeType = metadata.AddTypeReference(
            corlib,
            metadata.GetOrAddString("System.Runtime.CompilerServices"),
            metadata.GetOrAddString("CompilerGeneratedAttribute"));
        // instance void .ctor(): HASTHIS, zero parameters, void return.
        var attributeCtor = metadata.AddMemberReference(
            attributeType,
            metadata.GetOrAddString(".ctor"),
            metadata.GetOrAddBlob(new byte[] { 0x20, 0x00, 0x01 }));

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
        if (callReferenceNamed is not null)
            caller.Call(reference);
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

        metadata.AddMethodDefinition(
            MethodAttributes.Public | MethodAttributes.Static,
            MethodImplAttributes.IL,
            metadata.GetOrAddString("Caller"),
            signature,
            callerOffset,
            MetadataTokens.ParameterHandle(1));

        var generated = new List<MethodDefinitionHandle>();
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
                generated.Add(handle);
        }

        // The CustomAttribute table must be sorted by its coded parent index, and the
        // members are already emitted in ascending row order, so appending in order is
        // sufficient here.
        foreach (var handle in generated)
        {
            metadata.AddCustomAttribute(
                handle,
                attributeCtor,
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
