using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

namespace ILInspector.ILDiff.Tests;

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
    public void CompareMembers_DistinguishesRejectedArrayOperandShapes()
    {
        AssertArrayOperandDiff(
            ArrayParameterSignature(1, [6], []),
            ArrayParameterSignature(1, [7], []));
        AssertArrayOperandDiff(
            ArrayParameterSignature(1, [6], []),
            ArrayParameterSignature(2, [6], []));
        AssertArrayOperandDiff(
            ArrayParameterSignature(1, [0], [1]),
            ArrayParameterSignature(2, [0], [1]));
        AssertArrayOperandDiff(
            ArrayParameterSignature(33, [6], []),
            ArrayParameterSignature(33, [7], []));

        int[] oldSizes = new int[33];
        int[] newSizes = new int[33];
        newSizes[^1] = 1;
        int[] lowerBounds = new int[33];
        AssertArrayOperandDiff(
            ArrayParameterSignature(33, oldSizes, lowerBounds),
            ArrayParameterSignature(33, newSizes, lowerBounds));

        AssertNestedArrayOperandDiff(
            [ArrayTypeSignature(6)],
            [ArrayTypeSignature(7)]);
        AssertNestedArrayOperandDiff(
            [ModifiedTypeSpecSignature(2), ArrayTypeSignature(6)],
            [ModifiedTypeSpecSignature(2), ArrayTypeSignature(7)]);
        AssertNestedArrayOperandDiff(
            [RepresentableArrayTypeSignature(6)],
            [RepresentableArrayTypeSignature(7)],
            RejectedArrayAndModifiedTypeSpecParameterSignature());
    }

    [Fact]
    public void CompareStreams_DoesNotAlignMethodsWithDifferentRejectedArraySignatures()
    {
        using var oldStream = new MemoryStream(BuildCallImage(
            "Shapes",
            methodSignature: ArrayParameterSignature(33, [6], []),
            emitCall: false));
        using var newStream = new MemoryStream(BuildCallImage(
            "Shapes",
            methodSignature: ArrayParameterSignature(33, [7], []),
            emitCall: false));

        var result = IlAssemblyDiff.CompareStreams(
            oldStream,
            "old.dll",
            newStream,
            "new.dll").Diff;

        Assert.Equal(0, result.ComparedBodyCount);
        Assert.Equal(0, result.PairExactCount);
    }

    [Fact]
    public void CompareStreams_DoesNotAlignMethodsWithDifferentNestedRejectedArraySignatures()
    {
        using var oldStream = new MemoryStream(BuildCallImage(
            "Shapes",
            methodSignature: ModifiedTypeSpecParameterSignature(),
            typeSpecifications: [ArrayTypeSignature(6)],
            emitCall: false));
        using var newStream = new MemoryStream(BuildCallImage(
            "Shapes",
            methodSignature: ModifiedTypeSpecParameterSignature(),
            typeSpecifications: [ArrayTypeSignature(7)],
            emitCall: false));

        var result = IlAssemblyDiff.CompareStreams(
            oldStream,
            "old.dll",
            newStream,
            "new.dll").Diff;

        Assert.Equal(0, result.ComparedBodyCount);
        Assert.Equal(0, result.PairExactCount);
    }

    [Fact]
    public void RejectedArrayIdentityIsIndependentOfTypeSpecRowNumbers()
    {
        byte[] rejectedArray = ArrayTypeSignature(6);
        var oldImage = BuildCallImage(
            "Shapes",
            "Library",
            targetSignature: ModifiedTypeSpecParameterSignature(1),
            typeSpecifications: [rejectedArray]);
        var newImage = BuildCallImage(
            "Shapes",
            "Library",
            targetSignature: ModifiedTypeSpecParameterSignature(2),
            typeSpecifications: [new byte[] { 0x08 }, rejectedArray]);

        Assert.True(CompareImages(oldImage, newImage).IsExact);
    }

    [Fact]
    public void RejectedArrayIdentityPreservesResolvedTypeReferences()
    {
        byte[] signature = RejectedArrayAndTypeRefParameterSignature();
        var oldImage = BuildCallImage(
            "Shapes",
            "Library",
            targetSignature: signature,
            signatureTypeName: "Payload.One");
        var newImage = BuildCallImage(
            "Shapes",
            "Library",
            targetSignature: signature,
            signatureTypeName: "Payload.Two");

        Assert.Equal(IlBodyDiffOutcome.OperandDiff, CompareImages(oldImage, newImage).Outcome);
    }

    [Fact]
    public void CompareStreams_DoesNotAlignRejectedArraysWithDifferentResolvedTypeReferences()
    {
        byte[] signature = RejectedArrayAndTypeRefParameterSignature();
        using var oldStream = new MemoryStream(BuildCallImage(
            "Shapes",
            methodSignature: signature,
            signatureTypeName: "Payload.One",
            emitCall: false));
        using var newStream = new MemoryStream(BuildCallImage(
            "Shapes",
            methodSignature: signature,
            signatureTypeName: "Payload.Two",
            emitCall: false));

        var result = IlAssemblyDiff.CompareStreams(
            oldStream,
            "old.dll",
            newStream,
            "new.dll").Diff;

        Assert.Equal(0, result.ComparedBodyCount);
        Assert.Equal(0, result.PairExactCount);
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
    public void NormalizeCurrentAssemblyScope_DoesNotRewriteArrayBounds()
    {
        byte[] signature = AcceptedArrayParameterSignature(6);
        var oldImage = BuildCallImage("6", "Library", targetSignature: signature);
        var newImage = BuildCallImage("7", "Library", targetSignature: signature);

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

    [Fact]
    public void Compare_RejectsUndefinedOptions()
    {
        var body = Decode([0x2a]);

        Assert.Throws<ArgumentOutOfRangeException>(
            () => IlBodyDiff.Compare(body, body, (IlBodyDiffNormalization)(1 << 10)));
    }

    /// <summary>
    /// Bit 3 was the unsound per-side synthesized-member rewrite retired by #3645.
    /// Keep the hole in the flag space so a stale numeric caller fails visibly rather
    /// than silently selecting another normalization.
    /// </summary>
    [Fact]
    public void Compare_RejectsRetiredSynthesizedMemberOrdinalOption()
    {
        var body = Decode([0x2a]);

        Assert.Throws<ArgumentOutOfRangeException>(
            () => IlBodyDiff.Compare(body, body, (IlBodyDiffNormalization)(1 << 3)));
    }

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

    static void AssertArrayOperandDiff(byte[] oldSignature, byte[] newSignature)
    {
        var oldImage = BuildCallImage("Shapes", "Library", targetSignature: oldSignature);
        var newImage = BuildCallImage("Shapes", "Library", targetSignature: newSignature);

        var diff = CompareImages(oldImage, newImage);

        Assert.Equal(IlBodyDiffOutcome.OperandDiff, diff.Outcome);
        Assert.False(diff.IsExact);
    }

    static void AssertNestedArrayOperandDiff(
        byte[][] oldTypeSpecifications,
        byte[][] newTypeSpecifications,
        byte[]? signature = null)
    {
        signature ??= ModifiedTypeSpecParameterSignature();
        var oldImage = BuildCallImage(
            "Shapes",
            "Library",
            targetSignature: signature,
            typeSpecifications: oldTypeSpecifications);
        var newImage = BuildCallImage(
            "Shapes",
            "Library",
            targetSignature: signature,
            typeSpecifications: newTypeSpecifications);

        var diff = CompareImages(oldImage, newImage);

        Assert.Equal(IlBodyDiffOutcome.OperandDiff, diff.Outcome);
        Assert.False(diff.IsExact);
    }

    static byte[] BuildCallImage(
        string assemblyName,
        string? referenceAssemblyName = null,
        string? memberName = null,
        string? typeName = null,
        string? signatureTypeName = null,
        byte[]? targetSignature = null,
        byte[]? methodSignature = null,
        byte[][]? typeSpecifications = null,
        bool emitCall = true)
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
        if (typeSpecifications is not null)
        {
            foreach (byte[] signature in typeSpecifications)
                metadata.AddTypeSpecification(metadata.GetOrAddBlob(signature));
        }
        EntityHandle target;
        if (referenceAssemblyName is null)
        {
            target = MetadataTokens.MethodDefinitionHandle(1);
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
            if (signatureTypeName is not null)
            {
                metadata.AddTypeReference(
                    reference,
                    metadata.GetOrAddString("System"),
                    metadata.GetOrAddString(signatureTypeName));
            }
            var type = metadata.AddTypeReference(
                reference,
                selfReference ? default : metadata.GetOrAddString("System"),
                metadata.GetOrAddString(typeName ?? (selfReference ? "C" : "Probe")));
            target = metadata.AddMemberReference(
                type,
                metadata.GetOrAddString(memberName ?? (selfReference ? "Caller" : "Target")),
                metadata.GetOrAddBlob(targetSignature ?? [0x00, 0x00, 0x01]));
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
        if (emitCall)
        {
            if (targetSignature is not null || methodSignature is not null)
                encoder.OpCode(ILOpCode.Ldnull);
            encoder.Call(target);
        }
        encoder.OpCode(ILOpCode.Ret);
        var methodBodies = new BlobBuilder();
        int bodyOffset = new MethodBodyStreamEncoder(methodBodies).AddMethodBody(encoder);
        metadata.AddMethodDefinition(
            MethodAttributes.Public | MethodAttributes.Static,
            MethodImplAttributes.IL,
            metadata.GetOrAddString("Caller"),
            metadata.GetOrAddBlob(methodSignature ?? [0x00, 0x00, 0x01]),
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

    static byte[] ArrayParameterSignature(int rank, int[] sizes, int[] lowerBounds)
    {
        var signature = new BlobBuilder();
        signature.WriteByte(0x00); // default method signature
        signature.WriteCompressedInteger(1);
        signature.WriteByte(0x01); // void return type
        signature.WriteByte(0x14); // ARRAY
        signature.WriteByte(0x08); // int32 element type
        signature.WriteCompressedInteger(rank);
        signature.WriteCompressedInteger(sizes.Length);
        foreach (int size in sizes)
            signature.WriteCompressedInteger(size);
        signature.WriteCompressedInteger(lowerBounds.Length);
        foreach (int lowerBound in lowerBounds)
            signature.WriteCompressedSignedInteger(lowerBound);
        return signature.ToArray();
    }

    static byte[] ArrayTypeSignature(int size) =>
    [
        0x14, // ARRAY
        0x08, // int32 element type
        0x01, // rank 1
        0x01, // one size
        (byte)size,
        0x00, // no lower bounds
    ];

    static byte[] RepresentableArrayTypeSignature(int size) =>
    [
        0x14, // ARRAY
        0x08, // int32 element type
        0x01, // rank 1
        0x01, // one size
        (byte)size,
        0x01, // one lower bound
        0x00, // zero lower bound
    ];

    static byte[] ModifiedTypeSpecParameterSignature(int row = 1)
    {
        var signature = new BlobBuilder();
        signature.WriteByte(0x00); // default method signature
        signature.WriteCompressedInteger(1);
        signature.WriteByte(0x01); // void return type
        signature.WriteByte(0x1f); // required modifier
        signature.WriteCompressedInteger((row << 2) | 2);
        signature.WriteByte(0x08); // int32 parameter type
        return signature.ToArray();
    }

    static byte[] RejectedArrayAndModifiedTypeSpecParameterSignature() =>
    [
        0x00, // default method signature
        0x02, // two parameters
        0x01, // void return type
        0x14, // ARRAY
        0x08, // int32 element type
        0x01, // rank 1
        0x01, // one size
        0x06, // size 6
        0x00, // no lower bounds
        0x1f, // required modifier
        0x06, // TypeSpec row 1
        0x08, // int32 parameter type
    ];

    static byte[] ModifiedTypeSpecSignature(int row)
    {
        var signature = new BlobBuilder();
        signature.WriteByte(0x1f); // required modifier
        signature.WriteCompressedInteger((row << 2) | 2);
        signature.WriteByte(0x08); // int32
        return signature.ToArray();
    }

    static byte[] RejectedArrayAndTypeRefParameterSignature() =>
    [
        0x00, // default method signature
        0x02, // two parameters
        0x01, // void return type
        0x14, // ARRAY
        0x08, // int32 element type
        0x01, // rank 1
        0x01, // one size
        0x06, // size 6
        0x00, // no lower bounds
        0x12, // class
        0x05, // TypeRef row 1
    ];

    static byte[] AcceptedArrayParameterSignature(int size) =>
    [
        0x00, // default method signature
        0x01, // one parameter
        0x01, // void return type
        0x14, // ARRAY
        0x08, // int32 element type
        0x01, // rank 1
        0x01, // one size
        (byte)size,
        0x01, // one lower bound
        0x00, // zero lower bound
    ];

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
