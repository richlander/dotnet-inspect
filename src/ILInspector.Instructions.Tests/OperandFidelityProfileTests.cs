using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

namespace ILInspector.Instructions.Tests;

public class OperandFidelityProfileTests
{
    [Fact]
    public void OperandFidelityV1_ToleratesLocalMacroAndSlotLayout()
    {
        var macro = Decode([0x06, 0x2a]); // ldloc.0; ret
        var explicitSlot = Decode([0x11, 0x07, 0x2a]); // ldloc.s 7; ret

        Assert.False(IlBodyDiff.Compare(macro, explicitSlot).IsExact);
        Assert.True(IlBodyDiff.Compare(macro, explicitSlot, IlBodyDiffProfile.OperandFidelityV1).IsExact);
    }

    [Fact]
    public void OperandFidelityV1_ToleratesArgumentMacroAndSlotLayout()
    {
        var macro = Decode([0x02, 0x2a]); // ldarg.0; ret
        var explicitSlot = Decode([0x0e, 0x07, 0x2a]); // ldarg.s 7; ret

        Assert.False(IlBodyDiff.Compare(macro, explicitSlot).IsExact);
        Assert.True(IlBodyDiff.Compare(macro, explicitSlot, IlBodyDiffProfile.OperandFidelityV1).IsExact);
    }

    [Fact]
    public void OperandFidelityV1_DoesNotFoldArgumentValueAndAddressLoads()
    {
        var valueLoad = Decode([0x02, 0x2a]); // ldarg.0; ret
        var addressLoad = Decode([0x0f, 0x00, 0x2a]); // ldarga.s 0; ret

        var diff = IlBodyDiff.Compare(valueLoad, addressLoad, IlBodyDiffProfile.OperandFidelityV1);

        Assert.False(diff.IsExact);
        Assert.Contains(diff.Rows, row => row.Operation.OpcodeFamily == "ldarg");
        Assert.Contains(diff.Rows, row => row.Operation.OpcodeFamily == "ldarga");
    }

    [Fact]
    public void OperandFidelityV1_ReportsNumericOperandChange()
    {
        var five = Decode([0x1b, 0x2a]); // ldc.i4.5; ret
        var seven = Decode([0x1d, 0x2a]); // ldc.i4.7; ret

        var diff = IlBodyDiff.Compare(five, seven, IlBodyDiffProfile.OperandFidelityV1);

        Assert.False(diff.IsExact);
        Assert.Contains(diff.Rows, row => row.Operation.Operand?.Value == "5");
        Assert.Contains(diff.Rows, row => row.Operation.Operand?.Value == "7");
    }

    [Fact]
    public void OperandFidelityV1_ReportsBranchTopologyChange()
    {
        var firstTarget = Decode([0x2b, 0x03, 0x00, 0x2a, 0x00, 0x2a]);
        var secondTarget = Decode([0x2b, 0x01, 0x00, 0x2a, 0x00, 0x2a]);

        var diff = IlBodyDiff.Compare(firstTarget, secondTarget, IlBodyDiffProfile.OperandFidelityV1);

        Assert.False(diff.IsExact);
        Assert.Equal(2, diff.Rows.Length);
        Assert.All(diff.Rows, row => Assert.Equal("br", row.Operation.OpcodeFamily));
    }

    [Fact]
    public void OperandFidelityV1_ToleratesPlatformReferenceScopeChanges()
    {
        var diff = CompareCallImages("System.Runtime", "System.Private.CoreLib");

        Assert.False(diff.Default.IsExact);
        Assert.True(diff.OperandFidelity.IsExact);
    }

    [Fact]
    public void OperandFidelityV1_PreservesNonPlatformReferenceIdentity()
    {
        var diff = CompareCallImages("Library.One", "Library.Two");

        Assert.False(diff.OperandFidelity.IsExact);
    }

    [Fact]
    public void OperandFidelityV1_PreservesPlatformLikeStringLiterals()
    {
        var diff = CompareImages(
            BuildStringImage("Old", "[System.Runtime]"),
            BuildStringImage("New", "[System.Private.CoreLib]"));

        Assert.False(diff.OperandFidelity.IsExact);
        Assert.Contains(diff.OperandFidelity.Rows, row => row.Operation.Operand?.Value.Contains("System.Runtime", StringComparison.Ordinal) == true);
        Assert.Contains(diff.OperandFidelity.Rows, row => row.Operation.Operand?.Value.Contains("System.Private.CoreLib", StringComparison.Ordinal) == true);
    }

    static MethodInstructions Decode(byte[] il)
        => MethodInstructions.Decode(il, il.Length, exceptionRegions: []);

    static (IlBodyDiffResult Default, IlBodyDiffResult OperandFidelity) CompareCallImages(
        string oldReference,
        string newReference)
        => CompareImages(
            BuildCallImage("Old", oldReference),
            BuildCallImage("New", newReference));

    static (IlBodyDiffResult Default, IlBodyDiffResult OperandFidelity) CompareImages(
        byte[] oldImage,
        byte[] newImage)
    {
        using var oldPe = new PEReader(new MemoryStream(oldImage));
        using var newPe = new PEReader(new MemoryStream(newImage));
        var oldReader = oldPe.GetMetadataReader();
        var newReader = newPe.GetMetadataReader();
        var oldMethod = MetadataTokens.MethodDefinitionHandle(1);
        var newMethod = MetadataTokens.MethodDefinitionHandle(1);
        return (
            IlAssemblyDiff.CompareMembers(
                oldPe, oldReader, oldMethod, newPe, newReader, newMethod).Diff,
            IlAssemblyDiff.CompareMembers(
                oldPe,
                oldReader,
                oldMethod,
                newPe,
                newReader,
                newMethod,
                profile: IlBodyDiffProfile.OperandFidelityV1).Diff);
    }

    static byte[] BuildCallImage(string assemblyName, string referenceAssemblyName)
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
        var target = metadata.AddMemberReference(
            type,
            metadata.GetOrAddString("Target"),
            metadata.GetOrAddBlob(new byte[] { 0x00, 0x00, 0x01 }));

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
