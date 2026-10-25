using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

using ILInspector.Instructions;
using Inspector.Findings;

namespace ILInspector.ILDiff.Tests;

/// <summary>
/// The clause stage over the cursor's instruction map applies the decoder's
/// clause-table rules: a table the decoder rejects ends the cursor failed at
/// the body's end with the decode's reason, and a table it admits yields
/// its clause atoms. Regions are encoded unchecked so the table can be
/// malformed in each way the boundary check must reject.
/// </summary>
public class IlCensusCursorClauseTableTests
{
    // nop; leave.s +2 (to 5); nop; endfinally; ret
    static readonly byte[] TryFinally = [0x00, 0xDE, 0x02, 0x00, 0xDC, 0x2A];

    // volatile. ldind.i4; pop; ret
    static readonly byte[] Prefixed = [0xFE, 0x13, 0x4A, 0x26, 0x2A];

    public static TheoryData<string, byte[], int, int, int, int> MalformedTables => new()
    {
        { "try start inside an operand", TryFinally, 2, 1, 3, 2 },
        { "try start on a prefixed instruction", Prefixed, 2, 1, 3, 2 },
        { "handler end past the body", TryFinally, 0, 3, 3, 9 },
    };

    [Theory]
    [MemberData(nameof(MalformedTables))]
    public void MalformedClauseTable_FailsAtTheEnd_WithTheDecodersReason(
        string name, byte[] il, int tryOffset, int tryLength, int handlerOffset, int handlerLength)
    {
        byte[] image = Image(il, tryOffset, tryLength, handlerOffset, handlerLength);
        using var pe = new PEReader(new MemoryStream(image, writable: false));
        MetadataReader reader = pe.GetMetadataReader();
        MethodDefinitionHandle method = MetadataTokens.MethodDefinitionHandle(1);
        int rva = reader.GetMethodDefinition(method).RelativeVirtualAddress;
        string? decoderReason = MethodInstructions.Decode(pe.GetMethodBody(rva)).Blocks.IncompleteReason;
        Assert.NotNull(decoderReason);

        IlCensusCursor cursor = IlCensusCursor.Create(IlCensusSide.Create(reader, IlBodyDiffPolicy.Body), pe, method);
        while (cursor.Advance())
        {
        }
        Assert.Equal(FindingCursorTerminalKind.Failed, cursor.Terminal!.Kind);
        Assert.Equal(il.Length, cursor.Terminal.Position);
        Assert.Equal(decoderReason, cursor.Terminal.Reason);
        Assert.True(name.Length > 0);
    }

    [Fact]
    public void AdmittedClauseTable_YieldsItsClauseAtom()
    {
        byte[] image = Image(TryFinally, 0, 3, 3, 2);
        using var pe = new PEReader(new MemoryStream(image, writable: false));
        MetadataReader reader = pe.GetMetadataReader();
        MethodDefinitionHandle method = MetadataTokens.MethodDefinitionHandle(1);
        int rva = reader.GetMethodDefinition(method).RelativeVirtualAddress;
        Assert.True(MethodInstructions.Decode(pe.GetMethodBody(rva)).IsComplete);

        IlCensusCursor cursor = IlCensusCursor.Create(IlCensusSide.Create(reader, IlBodyDiffPolicy.Body), pe, method);
        int atoms = 0;
        int clauses = 0;
        while (cursor.Advance())
        {
            atoms++;
            if (cursor.Current!.IsExceptionClause)
                clauses++;
        }
        Assert.Equal(FindingCursorTerminalKind.Complete, cursor.Terminal!.Kind);
        Assert.Equal(6, atoms);
        Assert.Equal(1, clauses);
    }

    /// <summary>One image with <c>C::M()</c> whose body is <paramref name="il"/> under one unchecked finally region.</summary>
    static byte[] Image(byte[] il, int tryOffset, int tryLength, int handlerOffset, int handlerLength)
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(0, metadata.GetOrAddString("Clauses.dll"), metadata.GetOrAddGuid(new Guid("6b6b6b6b-0000-4000-8000-000000000002")), default, default);
        metadata.AddAssembly(metadata.GetOrAddString("Clauses"), new Version(1, 0, 0, 0), default, default, 0, AssemblyHashAlgorithm.Sha1);
        AssemblyReferenceHandle runtime = metadata.AddAssemblyReference(
            metadata.GetOrAddString("System.Runtime"), new Version(10, 0, 0, 0), default, default, default, default);
        TypeReferenceHandle systemObject = metadata.AddTypeReference(runtime, metadata.GetOrAddString("System"), metadata.GetOrAddString("Object"));
        metadata.AddTypeDefinition(default, default, metadata.GetOrAddString("<Module>"), default, MetadataTokens.FieldDefinitionHandle(1), MetadataTokens.MethodDefinitionHandle(1));

        var voidSignature = new BlobBuilder();
        new BlobEncoder(voidSignature).MethodSignature().Parameters(0, returnType => returnType.Void(), _ => { });
        BlobHandle voidHandle = metadata.GetOrAddBlob(voidSignature);
        var bodies = new BlobBuilder();
        var stream = new MethodBodyStreamEncoder(bodies);

        metadata.AddTypeDefinition(
            TypeAttributes.Public | TypeAttributes.Class | TypeAttributes.Abstract | TypeAttributes.Sealed,
            default, metadata.GetOrAddString("C"), systemObject,
            MetadataTokens.FieldDefinitionHandle(1), MetadataTokens.MethodDefinitionHandle(1));
        MethodBodyStreamEncoder.MethodBody body = stream.AddMethodBody(il.Length, maxStack: 2, exceptionRegionCount: 1, hasSmallExceptionRegions: true);
        new BlobWriter(body.Instructions).WriteBytes(il);
        body.ExceptionRegions.AddUnchecked(ExceptionRegionKind.Finally, tryOffset, tryLength, handlerOffset, handlerLength);
        metadata.AddMethodDefinition(
            MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.HideBySig, MethodImplAttributes.IL,
            metadata.GetOrAddString("M"), voidHandle, body.Offset, default);

        var image = new BlobBuilder();
        new ManagedPEBuilder(
                new PEHeaderBuilder(imageCharacteristics: Characteristics.Dll | Characteristics.ExecutableImage),
                new MetadataRootBuilder(metadata),
                bodies)
            .Serialize(image);
        return image.ToArray();
    }
}
