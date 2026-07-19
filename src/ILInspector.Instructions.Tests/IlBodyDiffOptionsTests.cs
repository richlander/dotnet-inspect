using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

namespace ILInspector.Instructions.Tests;

public class IlBodyDiffOptionsTests
{
    const IlBodyDiffOptions AllNormalizationOptions =
        IlBodyDiffOptions.NormalizeVariableLayout
        | IlBodyDiffOptions.NormalizeCurrentAssemblyScope
        | IlBodyDiffOptions.NormalizePlatformAssemblyScopes;

    [Fact]
    public void NormalizeVariableLayout_ToleratesLocalMacroAndSlotLayout()
    {
        var macro = Decode([0x06, 0x2a]); // ldloc.0; ret
        var explicitSlot = Decode([0x11, 0x07, 0x2a]); // ldloc.s 7; ret

        Assert.False(IlBodyDiff.Compare(macro, explicitSlot).IsExact);
        Assert.True(IlBodyDiff.Compare(macro, explicitSlot, IlBodyDiffOptions.NormalizeVariableLayout).IsExact);
    }

    [Fact]
    public void NormalizeVariableLayout_ToleratesArgumentMacroAndSlotLayout()
    {
        var macro = Decode([0x02, 0x2a]); // ldarg.0; ret
        var explicitSlot = Decode([0x0e, 0x07, 0x2a]); // ldarg.s 7; ret

        Assert.False(IlBodyDiff.Compare(macro, explicitSlot).IsExact);
        Assert.True(IlBodyDiff.Compare(macro, explicitSlot, IlBodyDiffOptions.NormalizeVariableLayout).IsExact);
    }

    [Fact]
    public void NormalizeVariableLayout_DoesNotFoldArgumentValueAndAddressLoads()
    {
        var valueLoad = Decode([0x02, 0x2a]); // ldarg.0; ret
        var addressLoad = Decode([0x0f, 0x00, 0x2a]); // ldarga.s 0; ret

        var diff = IlBodyDiff.Compare(valueLoad, addressLoad, IlBodyDiffOptions.NormalizeVariableLayout);

        Assert.False(diff.IsExact);
        Assert.Contains(diff.Rows, row => row.Operation.OpcodeFamily == "ldarg");
        Assert.Contains(diff.Rows, row => row.Operation.OpcodeFamily == "ldarga");
    }

    [Fact]
    public void AllOptions_PreserveNumericOperandChanges()
    {
        var five = Decode([0x1b, 0x2a]); // ldc.i4.5; ret
        var seven = Decode([0x1d, 0x2a]); // ldc.i4.7; ret

        var diff = IlBodyDiff.Compare(five, seven, AllNormalizationOptions);

        Assert.False(diff.IsExact);
        Assert.Contains(diff.Rows, row => row.Operation.Operand?.Value == "5");
        Assert.Contains(diff.Rows, row => row.Operation.Operand?.Value == "7");
    }

    [Fact]
    public void AllOptions_PreserveBranchTopologyChanges()
    {
        var firstTarget = Decode([0x2b, 0x03, 0x00, 0x2a, 0x00, 0x2a]);
        var secondTarget = Decode([0x2b, 0x01, 0x00, 0x2a, 0x00, 0x2a]);

        var diff = IlBodyDiff.Compare(firstTarget, secondTarget, AllNormalizationOptions);

        Assert.False(diff.IsExact);
        Assert.Equal(2, diff.Rows.Length);
        Assert.All(diff.Rows, row => Assert.Equal("br", row.Operation.OpcodeFamily));
    }

    [Fact]
    public void NormalizePlatformAssemblyScopes_ToleratesPlatformReferenceScopeChanges()
    {
        var defaultDiff = CompareCallImages("System.Runtime", "System.Private.CoreLib");
        var normalizedDiff = CompareCallImages(
            "System.Runtime",
            "System.Private.CoreLib",
            IlBodyDiffOptions.NormalizePlatformAssemblyScopes);

        Assert.False(defaultDiff.IsExact);
        Assert.True(normalizedDiff.IsExact);
    }

    [Fact]
    public void NormalizePlatformAssemblyScopes_PreservesNonPlatformReferenceIdentity()
    {
        var diff = CompareCallImages(
            "Library.One",
            "Library.Two",
            IlBodyDiffOptions.NormalizePlatformAssemblyScopes);

        Assert.False(diff.IsExact);
    }

    [Fact]
    public void NormalizePlatformAssemblyScopes_PreservesPlatformLikeStringLiterals()
    {
        var diff = CompareImages(
            BuildStringImage("Old", "[System.Runtime]"),
            BuildStringImage("New", "[System.Private.CoreLib]"),
            IlBodyDiffOptions.NormalizePlatformAssemblyScopes);

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
            IlBodyDiffOptions.NormalizePlatformAssemblyScopes).IsExact);
        Assert.True(CompareImages(
            oldImage,
            newImage,
            IlBodyDiffOptions.NormalizeCurrentAssemblyScope).IsExact);
    }

    [Fact]
    public void AllOptions_PreservePlatformNormalizationForSelfReferences()
    {
        var diff = CompareImages(
            BuildCallImage("System.Runtime", "System.Runtime"),
            BuildCallImage("System.Private.CoreLib", "System.Private.CoreLib"),
            AllNormalizationOptions);

        Assert.True(diff.IsExact);
    }

    [Fact]
    public void Compare_RejectsUndefinedOptions()
    {
        var body = Decode([0x2a]);

        Assert.Throws<ArgumentOutOfRangeException>(
            () => IlBodyDiff.Compare(body, body, (IlBodyDiffOptions)(1 << 10)));
    }

    static MethodInstructions Decode(byte[] il)
        => MethodInstructions.Decode(il, il.Length, exceptionRegions: []);

    static IlBodyDiffResult CompareCallImages(
        string oldReference,
        string newReference,
        IlBodyDiffOptions options = IlBodyDiffOptions.None)
        => CompareImages(
            BuildCallImage("Old", oldReference),
            BuildCallImage("New", newReference),
            options);

    static IlBodyDiffResult CompareImages(
        byte[] oldImage,
        byte[] newImage,
        IlBodyDiffOptions options = IlBodyDiffOptions.None)
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
            options: options).Diff;
    }

    static byte[] BuildCallImage(string assemblyName, string? referenceAssemblyName = null)
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
            target = MetadataTokens.MethodDefinitionHandle(1);
        }
        else
        {
            var reference = metadata.AddAssemblyReference(
                metadata.GetOrAddString(referenceAssemblyName),
                new Version(1, 0, 0, 0),
                default,
                default,
                default,
                default);
            var type = metadata.AddTypeReference(
                reference,
                metadata.GetOrAddString("System"),
                metadata.GetOrAddString("Probe"));
            target = metadata.AddMemberReference(
                type,
                metadata.GetOrAddString("Target"),
                metadata.GetOrAddBlob(new byte[] { 0x00, 0x00, 0x01 }));
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
        encoder.Call(target);
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
