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

    static Member Generated(string name) => new(name, CompilerGenerated: true);

    static Member Plain(string name) => new(name, CompilerGenerated: false);

    readonly record struct Member(string Name, bool CompilerGenerated);

    static IlBodyDiffResult Compare(
        Member[] oldMembers,
        Member[] newMembers,
        IlBodyDiffNormalization normalization = IlBodyDiffNormalization.None)
    {
        using var oldPe = new PEReader(new MemoryStream(BuildImage("Probe", oldMembers)));
        using var newPe = new PEReader(new MemoryStream(BuildImage("Probe", newMembers)));
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
    static byte[] BuildImage(string assemblyName, Member[] members)
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
            metadata.GetOrAddString("C"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));

        var bodies = new MethodBodyStreamEncoder(new BlobBuilder());
        var callerIl = new BlobBuilder();
        var caller = new InstructionEncoder(callerIl, new ControlFlowBuilder());
        // The first member is always method 2: method 1 is the caller emitted below.
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

        var signature = metadata.GetOrAddBlob(new byte[] { 0x00, 0x00, 0x01 });
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
