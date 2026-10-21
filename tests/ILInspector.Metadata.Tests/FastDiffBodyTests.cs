using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

using ILInspector.ILDiff;

namespace ILInspector.Metadata.Tests;

/// <summary>
/// The Body axis decides IL as canonical IL comparison does under the Body
/// policy: encodings canonical comparison treats as equal are
/// <c>Unchanged</c>, differences it shows are <c>Changed</c>, a dependency's
/// version is not an input, and what it cannot decode is
/// <c>Indeterminate</c>. Every verdict is checked against canonical comparison
/// over the same two bodies.
/// </summary>
public sealed class FastDiffBodyTests
{
    const string Namespace = "Cases";

    /// <summary>
    /// One Type per case; <c>equal</c> is canonical comparison's verdict and
    /// therefore the Body axis's.
    /// </summary>
    public static TheoryData<string, bool> EncodingCases => new()
    {
        { "BrShortVsLong", true },
        { "BrtrueShortVsLong", true },
        { "LeaveShortVsLong", true },
        { "LdcShortVsLong", true },
        { "LdcMacroVsShort", true },
        { "LdcMinusOneMacroVsLong", true },
        { "ShiftedBranchOffsets", true },
        { "ShiftedSwitchOffsets", true },
        { "LdargMacroVsShort", false },
        { "LdargShortVsLong", false },
        { "LdcValueChanged", false },
        { "LdcShortVsMacroOfAnotherValue", false },
        { "BranchRetargeted", false },
        { "SwitchArmCountChanged", false },
        { "CallTargetChanged", false },
        { "StringChanged", false },
        { "InstructionInserted", false },
        { "CalliSignatureTokenRenumbered", false },
    };

    [Theory]
    [MemberData(nameof(EncodingCases))]
    public void CanonicalEncodings_DecideAsCanonicalComparisonDoes(string name, bool equal)
    {
        (byte[] before, byte[] after) = EncodingImages.Value;
        FastDiffTypeState state = Single(CompareImages(before, after), $"{Namespace}.{name}");

        Assert.Equal(FastDiffState.Unchanged, state.Api);
        Assert.Equal(equal ? FastDiffState.Unchanged : FastDiffState.Changed, state.Body);
    }

    [Fact]
    public void CanonicalEncodings_AgreeWithCanonicalComparisonPerMethod()
    {
        (byte[] before, byte[] after) = EncodingImages.Value;
        AssertPerMethodAgreement(before, after, decided: true);
    }

    /// <summary>
    /// The dependency versions gate: a pair whose only difference is the
    /// version of a non-platform referenced assembly is <c>Unchanged</c> on
    /// both axes for every Type, and Member Body Diff's IL view under the Body
    /// policy shows no row for it either.
    /// </summary>
    [Fact]
    public void DependencyVersion_IsNotABodyChange()
    {
        byte[] before = DependencyImage(new Version(8, 0, 0, 0));
        byte[] after = DependencyImage(new Version(9, 0, 0, 0));

        FastDiffResult result = CompareImages(before, after);
        Assert.NotEmpty(result.Types);
        Assert.All(result.Types, type =>
            Assert.Equal((FastDiffState.Unchanged, FastDiffState.Unchanged), (type.Api, type.Body)));
        AssertPerMethodAgreement(before, after, decided: true);

        using var beforeReader = new PEReader(new MemoryStream(before, writable: false));
        using var afterReader = new PEReader(new MemoryStream(after, writable: false));
        IlMemberEndpointComparison view = CompareMemberEndpoints(beforeReader, afterReader, IlBodyDiffPolicy.Body);
        Assert.True(view.MemberDiff!.Diff.IsExact);
        Assert.Empty(view.MemberDiff.Diff.Rows);
        Assert.False(CompareMemberEndpoints(beforeReader, afterReader, IlBodyDiffNormalization.None).MemberDiff!.Diff.IsExact);
    }

    [Fact]
    public void BranchIntoAnInstruction_IsIndeterminate()
    {
        // br.s +2 targets the operand of ldc.i4, which starts no instruction.
        byte[] body = [0x2B, 0x02, 0x20, 0x00, 0x00, 0x00, 0x00, 0x26, 0x2A];
        AssertUndecidable(body, body);
    }

    [Fact]
    public void BranchIntoAPrefixedInstruction_IsIndeterminate()
    {
        // br.s +2 targets the ldind.i4 that volatile. prefixes.
        byte[] body = [0x2B, 0x02, 0xFE, 0x13, 0x4A, 0x26, 0x2A];
        AssertUndecidable(body, body);
    }

    [Fact]
    public void BodyEndingWithAPrefix_IsIndeterminate()
    {
        byte[] body = [0x00, 0xFE, 0x13]; // nop; volatile.
        AssertUndecidable(body, body);
    }

    [Fact]
    public void UndefinedOpcodeOnOneSide_IsIndeterminate()
    {
        // The undefined opcode is the first difference, so no difference is
        // decided before it.
        AssertUndecidable([0x00, 0x2A], [0x24, 0x2A]);
    }

    [Fact]
    public void RetargetedBranchBeforeAnUndefinedOpcode_IsIndeterminate()
    {
        // The branches target different instructions, which is decided only
        // once the instruction map is complete; the undefined opcode comes first.
        AssertUndecidable(
            [0x2B, 0x00, 0x00, 0x00, 0x24, 0x2A],
            [0x2B, 0x01, 0x00, 0x00, 0x24, 0x2A]);
    }

    [Fact]
    public void BranchToTheEndOfTheBody_ComparesByOffset()
    {
        // Both branches target the end of the body, which starts no
        // instruction, so they compare as equal raw offsets.
        byte[] body = [0x2B, 0x02, 0x00, 0x2A]; // br.s +2 (to 4, the end); nop; ret
        FastDiffResult result = CompareRaw(body, body);
        Assert.Equal(FastDiffState.Unchanged, Single(result, "N.C").Body);
        AssertPerMethodAgreement(RawImage(body), RawImage(body), decided: true);
    }

    static void AssertUndecidable(byte[] before, byte[] after)
    {
        FastDiffResult result = CompareRaw(before, after);
        FastDiffTypeState state = Single(result, "N.C");
        Assert.Equal(FastDiffState.Unchanged, state.Api);
        Assert.Equal(FastDiffState.Indeterminate, state.Body);
        AssertPerMethodAgreement(RawImage(before), RawImage(after), decided: false);
    }

    /// <summary>
    /// Every method pair's verdict equals canonical comparison's, and the pair
    /// is decided or undecidable as the test expects.
    /// </summary>
    static void AssertPerMethodAgreement(byte[] before, byte[] after, bool decided)
    {
        using var beforeReader = new PEReader(new MemoryStream(before, writable: false));
        using var afterReader = new PEReader(new MemoryStream(after, writable: false));
        List<FastDiff.BodyVerdict> verdicts = FastDiff.BodyVerdicts(beforeReader, afterReader);
        Assert.NotEmpty(verdicts);
        Assert.All(verdicts, verdict =>
        {
            FastDiffState canonical = FastDiffTests.CanonicalBodyVerdict(beforeReader, afterReader, verdict);
            Assert.True(
                canonical == verdict.Body,
                $"{verdict.Type} {verdict.Method}: canonical {canonical}, Fast Diff {verdict.Body}.");
            if (decided)
                Assert.NotEqual(FastDiffState.Indeterminate, verdict.Body);
        });
        if (!decided)
            Assert.Contains(verdicts, verdict => verdict.Body == FastDiffState.Indeterminate);
    }

    static IlMemberEndpointComparison CompareMemberEndpoints(
        PEReader before,
        PEReader after,
        IlBodyDiffNormalization normalization)
    {
        var subject = new IlMemberDiffSubject("N.C::M", "M");
        return IlAssemblyDiff.CompareMemberEndpoints(
            new IlMemberDiffEndpoint.Present(subject, before, before.GetMetadataReader(), FirstMethod(before)),
            new IlMemberDiffEndpoint.Present(subject, after, after.GetMetadataReader(), FirstMethod(after)),
            normalization);
    }

    static MethodDefinitionHandle FirstMethod(PEReader pe)
        => pe.GetMetadataReader().MethodDefinitions.First();

    static FastDiffTypeState Single(FastDiffResult result, string fullName)
        => Assert.Single(result.Types, type => type.FullName == fullName);

    static FastDiffResult CompareRaw(byte[] before, byte[] after)
        => CompareImages(RawImage(before), RawImage(after));

    /// <summary>Whole and stepped comparisons agree; the whole result is returned.</summary>
    static FastDiffResult CompareImages(byte[] beforeImage, byte[] afterImage)
    {
        using var beforeReader = new PEReader(new MemoryStream(beforeImage, writable: false));
        using var afterReader = new PEReader(new MemoryStream(afterImage, writable: false));
        FastDiffResult whole = FastDiff.Compare(beforeReader, afterReader);

        var stepped = new FastDiffComparison();
        bool complete = false;
        while (!complete)
        {
            using var stepBefore = new PEReader(new MemoryStream(beforeImage, writable: false));
            using var stepAfter = new PEReader(new MemoryStream(afterImage, writable: false));
            complete = stepped.Step(stepBefore, stepAfter, TimeSpan.Zero);
        }
        Assert.Equal(whole.Types, stepped.Result!.Types);
        return whole;
    }

    /// <summary>One public Type <c>N.C</c> whose public static method <c>M</c> has the given IL.</summary>
    static byte[] RawImage(byte[] body)
    {
        var image = new ImageBuilder("Raw", "11111111-1111-1111-1111-111111111111");
        image.AddType("N", "C", ("M", image.VoidSignature, il => il.CodeBuilder.WriteBytes(body)));
        return image.Build();
    }

    static readonly Lazy<(byte[] Before, byte[] After)> EncodingImages =
        new(() => (EncodingImage(after: false), EncodingImage(after: true)));

    /// <summary>
    /// The canonical encodings pair: a Type per case, each holding one method
    /// <c>M</c>, beside the callees and the stand-alone signature they use.
    /// </summary>
    static byte[] EncodingImage(bool after)
    {
        var image = new ImageBuilder(
            "Encodings",
            after ? "22222222-2222-2222-2222-222222222222" : "11111111-1111-1111-1111-111111111111");
        AssemblyReferenceHandle library = image.Metadata.AddAssemblyReference(
            image.Metadata.GetOrAddString("Dep"), new Version(1, 0, 0, 0), default, default, default, default);
        if (after)
        {
            // An extra reference row renumbers every later TypeRef token.
            image.Metadata.AddTypeReference(library, image.Metadata.GetOrAddString("Dep"), image.Metadata.GetOrAddString("Padding"));
        }
        TypeReferenceHandle probe = image.Metadata.AddTypeReference(
            library, image.Metadata.GetOrAddString("Dep"), image.Metadata.GetOrAddString("Probe"));
        var callSite = new BlobBuilder();
        new BlobEncoder(callSite).MethodSignature().Parameters(
            1, returnType => returnType.Void(), parameters => parameters.AddParameter().Type().Type(probe, isValueType: false));
        StandaloneSignatureHandle signature = image.Metadata.AddStandaloneSignature(image.Metadata.GetOrAddBlob(callSite));
        UserStringHandle one = image.Metadata.GetOrAddUserString("one");
        UserStringHandle two = image.Metadata.GetOrAddUserString("two");

        (MethodDefinitionHandle callee1, MethodDefinitionHandle callee2) = image.AddCallees();
        ILOpCode br = after ? ILOpCode.Br : ILOpCode.Br_s;
        image.AddCase("BrShortVsLong", il => BranchToRet(il, br));
        image.AddCase("BrtrueShortVsLong", il => ConditionalBranch(il, after ? ILOpCode.Brtrue : ILOpCode.Brtrue_s));
        image.AddCase("LeaveShortVsLong", il => BranchToRet(il, after ? ILOpCode.Leave : ILOpCode.Leave_s));
        image.AddCase("LdcShortVsLong", il => LoadConstant(il, after ? ILOpCode.Ldc_i4 : ILOpCode.Ldc_i4_s, 5));
        image.AddCase("LdcMacroVsShort", il => LoadConstant(il, after ? ILOpCode.Ldc_i4_s : ILOpCode.Ldc_i4_5, 5));
        image.AddCase("LdcMinusOneMacroVsLong", il => LoadConstant(il, after ? ILOpCode.Ldc_i4 : ILOpCode.Ldc_i4_m1, -1));
        image.AddCase("ShiftedBranchOffsets", il => Shifted(il, br));
        image.AddCase("ShiftedSwitchOffsets", il => Switch(il, 2, after ? ILOpCode.Br : ILOpCode.Br_s));
        image.AddCase("LdargMacroVsShort", il => LoadArgument(il, after ? ILOpCode.Ldarg_s : ILOpCode.Ldarg_1));
        image.AddCase("LdargShortVsLong", il => LoadArgument(il, after ? ILOpCode.Ldarg : ILOpCode.Ldarg_s));
        image.AddCase("LdcValueChanged", il => LoadConstant(il, ILOpCode.Ldc_i4, after ? 6 : 5));
        image.AddCase("LdcShortVsMacroOfAnotherValue", il => LoadConstant(il, after ? ILOpCode.Ldc_i4_6 : ILOpCode.Ldc_i4_s, after ? 6 : 5));
        image.AddCase("BranchRetargeted", il => Retarget(il, first: !after));
        image.AddCase("SwitchArmCountChanged", il => Switch(il, after ? 3 : 2, ILOpCode.Br_s));
        image.AddCase("CallTargetChanged", il =>
        {
            il.Call(after ? callee2 : callee1);
            il.OpCode(ILOpCode.Ret);
        });
        image.AddCase("StringChanged", il =>
        {
            il.LoadString(after ? two : one);
            il.OpCode(ILOpCode.Pop);
            il.OpCode(ILOpCode.Ret);
        });
        image.AddCase("InstructionInserted", il =>
        {
            il.OpCode(ILOpCode.Nop);
            if (after)
                il.OpCode(ILOpCode.Nop);
            il.OpCode(ILOpCode.Ret);
        });
        image.AddCase("CalliSignatureTokenRenumbered", il =>
        {
            il.OpCode(ILOpCode.Ldnull);
            il.OpCode(ILOpCode.Ldnull);
            il.OpCode(ILOpCode.Calli);
            il.Token(signature);
            il.OpCode(ILOpCode.Ret);
        });
        return image.Build();
    }

    /// <summary>
    /// A Type whose public method takes, calls into, and names a Type of a
    /// non-platform referenced assembly at the given version.
    /// </summary>
    static byte[] DependencyImage(Version referenceVersion)
    {
        var image = new ImageBuilder("Consumer", "33333333-3333-3333-3333-333333333333");
        AssemblyReferenceHandle library = image.Metadata.AddAssemblyReference(
            image.Metadata.GetOrAddString("Library.One"), referenceVersion, default, default, default, default);
        TypeReferenceHandle probe = image.Metadata.AddTypeReference(
            library, image.Metadata.GetOrAddString("Dep"), image.Metadata.GetOrAddString("Probe"));
        MemberReferenceHandle target = image.Metadata.AddMemberReference(
            probe, image.Metadata.GetOrAddString("Target"), image.VoidSignature);
        var parameterSignature = new BlobBuilder();
        new BlobEncoder(parameterSignature).MethodSignature().Parameters(
            1, returnType => returnType.Void(), parameters => parameters.AddParameter().Type().Type(probe, isValueType: false));
        image.AddType("N", "C", ("M", image.Metadata.GetOrAddBlob(parameterSignature), il =>
        {
            il.Call(target);
            il.OpCode(ILOpCode.Ldtoken);
            il.Token(probe);
            il.OpCode(ILOpCode.Pop);
            il.OpCode(ILOpCode.Ret);
        }));
        return image.Build();
    }

    static void BranchToRet(InstructionEncoder il, ILOpCode branch)
    {
        LabelHandle end = il.DefineLabel();
        il.Branch(branch, end);
        il.OpCode(ILOpCode.Nop);
        il.MarkLabel(end);
        il.OpCode(ILOpCode.Ret);
    }

    static void ConditionalBranch(InstructionEncoder il, ILOpCode branch)
    {
        LabelHandle end = il.DefineLabel();
        il.LoadArgument(0);
        il.Branch(branch, end);
        il.OpCode(ILOpCode.Nop);
        il.MarkLabel(end);
        il.OpCode(ILOpCode.Ret);
    }

    /// <summary>A short or long first branch shifts the offset of every later target.</summary>
    static void Shifted(InstructionEncoder il, ILOpCode firstBranch)
    {
        LabelHandle middle = il.DefineLabel();
        LabelHandle end = il.DefineLabel();
        il.Branch(firstBranch, middle);
        il.OpCode(ILOpCode.Nop);
        il.MarkLabel(middle);
        il.LoadArgument(0);
        il.Branch(ILOpCode.Brfalse_s, end);
        il.OpCode(ILOpCode.Nop);
        il.MarkLabel(end);
        il.OpCode(ILOpCode.Ret);
    }

    static void Retarget(InstructionEncoder il, bool first)
    {
        LabelHandle a = il.DefineLabel();
        LabelHandle b = il.DefineLabel();
        il.Branch(ILOpCode.Br_s, first ? a : b);
        il.MarkLabel(a);
        il.OpCode(ILOpCode.Nop);
        il.MarkLabel(b);
        il.OpCode(ILOpCode.Ret);
    }

    /// <summary>A switch whose arms follow a branch whose encoding shifts their offsets.</summary>
    static void Switch(InstructionEncoder il, int arms, ILOpCode skipBranch)
    {
        il.LoadArgument(0);
        LabelHandle[] labels = [.. Enumerable.Range(0, arms).Select(_ => il.DefineLabel())];
        SwitchInstructionEncoder table = il.Switch(arms);
        foreach (LabelHandle label in labels)
            table.Branch(label);
        LabelHandle skip = il.DefineLabel();
        il.Branch(skipBranch, skip);
        il.MarkLabel(skip);
        foreach (LabelHandle label in labels)
        {
            il.MarkLabel(label);
            il.OpCode(ILOpCode.Nop);
        }
        il.OpCode(ILOpCode.Ret);
    }

    static void LoadConstant(InstructionEncoder il, ILOpCode opCode, int value)
    {
        il.OpCode(opCode);
        if (opCode == ILOpCode.Ldc_i4_s)
            il.CodeBuilder.WriteSByte((sbyte)value);
        else if (opCode == ILOpCode.Ldc_i4)
            il.CodeBuilder.WriteInt32(value);
        il.OpCode(ILOpCode.Pop);
        il.OpCode(ILOpCode.Ret);
    }

    static void LoadArgument(InstructionEncoder il, ILOpCode opCode)
    {
        il.OpCode(opCode);
        if (opCode == ILOpCode.Ldarg_s)
            il.CodeBuilder.WriteByte(1);
        else if (opCode == ILOpCode.Ldarg)
            il.CodeBuilder.WriteUInt16(1);
        il.OpCode(ILOpCode.Pop);
        il.OpCode(ILOpCode.Ret);
    }

    /// <summary>A minimal image: one module and assembly, public Types with public static methods.</summary>
    sealed class ImageBuilder
    {
        readonly BlobBuilder _il = new();
        readonly MethodBodyStreamEncoder _bodies;
        int _nextMethod = 1;

        public ImageBuilder(string assemblyName, string mvid)
        {
            _bodies = new MethodBodyStreamEncoder(_il);
            Metadata.AddModule(
                0,
                Metadata.GetOrAddString($"{assemblyName}.dll"),
                Metadata.GetOrAddGuid(new Guid(mvid)),
                default,
                default);
            Metadata.AddAssembly(
                Metadata.GetOrAddString(assemblyName),
                new Version(1, 0, 0, 0),
                default,
                default,
                0,
                AssemblyHashAlgorithm.Sha1);
            Metadata.AddTypeDefinition(
                default,
                default,
                Metadata.GetOrAddString("<Module>"),
                default,
                MetadataTokens.FieldDefinitionHandle(1),
                MetadataTokens.MethodDefinitionHandle(1));
            var signature = new BlobBuilder();
            new BlobEncoder(signature).MethodSignature().Parameters(0, returnType => returnType.Void(), _ => { });
            VoidSignature = Metadata.GetOrAddBlob(signature);
        }

        public MetadataBuilder Metadata { get; } = new();

        public BlobHandle VoidSignature { get; }

        public (MethodDefinitionHandle, MethodDefinitionHandle) AddCallees()
        {
            MethodDefinitionHandle first = MetadataTokens.MethodDefinitionHandle(_nextMethod);
            AddType(
                Namespace,
                "Callees",
                ("Callee1", VoidSignature, il => il.OpCode(ILOpCode.Ret)),
                ("Callee2", VoidSignature, il => il.OpCode(ILOpCode.Ret)));
            return (first, MetadataTokens.MethodDefinitionHandle(MetadataTokens.GetRowNumber(first) + 1));
        }

        public void AddCase(string name, Action<InstructionEncoder> emit)
            => AddType(Namespace, name, ("M", VoidSignature, emit));

        public void AddType(
            string ns,
            string name,
            params (string Name, BlobHandle Signature, Action<InstructionEncoder> Emit)[] methods)
        {
            Metadata.AddTypeDefinition(
                TypeAttributes.Public | TypeAttributes.Class | TypeAttributes.Abstract | TypeAttributes.Sealed,
                Metadata.GetOrAddString(ns),
                Metadata.GetOrAddString(name),
                default,
                MetadataTokens.FieldDefinitionHandle(1),
                MetadataTokens.MethodDefinitionHandle(_nextMethod));
            foreach ((string methodName, BlobHandle signature, Action<InstructionEncoder> emit) in methods)
            {
                var code = new InstructionEncoder(new BlobBuilder(), new ControlFlowBuilder());
                emit(code);
                Metadata.AddMethodDefinition(
                    MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.HideBySig,
                    MethodImplAttributes.IL,
                    Metadata.GetOrAddString(methodName),
                    signature,
                    _bodies.AddMethodBody(code, maxStack: 8),
                    default);
                _nextMethod++;
            }
        }

        public byte[] Build()
        {
            var image = new BlobBuilder();
            new ManagedPEBuilder(
                    new PEHeaderBuilder(imageCharacteristics: Characteristics.Dll | Characteristics.ExecutableImage),
                    new MetadataRootBuilder(Metadata),
                    _il)
                .Serialize(image);
            return image.ToArray();
        }
    }
}
