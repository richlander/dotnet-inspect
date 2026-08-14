using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

namespace ILInspector.Metadata.Tests;

public sealed class MethodClassificationScannerSafetyTests
{
    [Fact]
    public void Scan_MultiMethodHostileIdentitiesFailClosedBeforeLargeAllocation()
    {
        // Many public P/Invoke methods each carry a wide discarded-modopt TypeSpec.
        // Per-anchor budgets bound one CreateMethodAnchorInfo call, but catch-and-
        // continue would multiply that cost by method count. The scan must fail
        // closed after MaxClassificationIdentityDecodeFailures.
        const int methodCount = 64;
        const int parameterCount = 2_000;
        const int genericArity = 2_030;
        byte[] image = BuildHostilePInvokeIdentityImage(
            methodCount,
            parameterCount,
            genericArity);
        using var pe = new PEReader(new MemoryStream(image));

        long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        BadImageFormatException ex = Assert.Throws<BadImageFormatException>(
            () => MethodClassificationScanner.Scan(pe));
        long allocated =
            GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;

        Assert.True(
            ex.Message.Contains("method-identity decode failure budget", StringComparison.Ordinal)
                || ex.Message.Contains("cumulative work budget", StringComparison.Ordinal)
                || ex.Message.Contains("classification scan work budget", StringComparison.Ordinal),
            ex.Message);
        Assert.True(
            allocated < 24 * 1024 * 1024,
            $"Multi-method hostile identity scan allocated {allocated:N0} bytes.");
    }

    [Fact]
    public void Scan_NearLimitMultiMethodIdentitiesFailClosedBeforeLargeAllocation()
    {
        // Each method stays under the per-anchor work budget, so the failure
        // counter never trips — but 64 near-limit successes still multiplied to
        // ~800 MiB. The scan-level work budget must reject earlier.
        const int methodCount = 64;
        const int parameterCount = 30;
        const int genericArity = 2_030;
        byte[] image = BuildHostilePInvokeIdentityImage(
            methodCount,
            parameterCount,
            genericArity);
        using var pe = new PEReader(new MemoryStream(image));

        long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        BadImageFormatException ex = Assert.Throws<BadImageFormatException>(
            () => MethodClassificationScanner.Scan(pe));
        long allocated =
            GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;

        Assert.True(
            ex.Message.Contains("cumulative work budget", StringComparison.Ordinal)
                || ex.Message.Contains("classification scan work budget", StringComparison.Ordinal),
            ex.Message);
        Assert.True(
            allocated < 24 * 1024 * 1024,
            $"Near-limit multi-method identity scan allocated {allocated:N0} bytes.");
    }

    [Fact]
    public void Scan_RealTestAssemblyStillClassifies()
    {
        using var stream = File.OpenRead(
            typeof(MethodClassificationScannerSafetyTests).Assembly.Location);
        using var pe = new PEReader(stream);

        List<ClassifiedMethodInfo> results = MethodClassificationScanner.Scan(pe);

        Assert.Contains(
            results,
            static m => m.MethodName
                == nameof(MetadataMethodFindingsTests.ClassifiedPointerMethod));
    }

    static byte[] BuildHostilePInvokeIdentityImage(
        int methodCount,
        int parameterCount,
        int genericArity)
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            0,
            metadata.GetOrAddString("HostilePInvoke.dll"),
            metadata.GetOrAddGuid(Guid.NewGuid()),
            default,
            default);
        metadata.AddAssembly(
            metadata.GetOrAddString("HostilePInvoke"),
            new Version(1, 0, 0, 0),
            default,
            default,
            default,
            default);
        ModuleReferenceHandle moduleRef = metadata.AddModuleReference(
            metadata.GetOrAddString("hostile.dll"));
        AssemblyReferenceHandle assembly =
            metadata.AddAssemblyReference(
                metadata.GetOrAddString("Dependency"),
                new Version(1, 0, 0, 0),
                default,
                default,
                default,
                default);
        metadata.AddTypeReference(
            assembly,
            metadata.GetOrAddString("N"),
            metadata.GetOrAddString("T"));
        metadata.AddTypeReference(
            assembly,
            metadata.GetOrAddString("N"),
            metadata.GetOrAddString("G"));

        var typeSpecSignature = new BlobBuilder();
        typeSpecSignature.WriteByte(0x15); // GENERICINST
        typeSpecSignature.WriteByte(0x12); // CLASS
        typeSpecSignature.WriteCompressedInteger((2 << 2) | 1); // TypeRef G
        typeSpecSignature.WriteCompressedInteger(genericArity);
        for (int i = 0; i < genericArity; i++)
        {
            typeSpecSignature.WriteByte(0x12);
            typeSpecSignature.WriteCompressedInteger((1 << 2) | 1); // TypeRef T
        }
        TypeSpecificationHandle typeSpec =
            metadata.AddTypeSpecification(
                metadata.GetOrAddBlob(typeSpecSignature));
        int typeSpecCodedIndex =
            (MetadataTokens.GetRowNumber(typeSpec) << 2) | 2;

        var signature = new BlobBuilder();
        signature.WriteByte(0x00);
        signature.WriteCompressedInteger(parameterCount);
        signature.WriteByte(0x01);
        for (int i = 0; i < parameterCount; i++)
        {
            signature.WriteByte(0x20); // CMOD_OPT
            signature.WriteCompressedInteger(typeSpecCodedIndex);
            signature.WriteByte(0x08);
        }
        BlobHandle signatureBlob = metadata.GetOrAddBlob(signature);

        metadata.AddTypeDefinition(
            TypeAttributes.NotPublic,
            default,
            metadata.GetOrAddString("<Module>"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        metadata.AddTypeDefinition(
            TypeAttributes.Public,
            default,
            metadata.GetOrAddString("C"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));

        for (int i = 0; i < methodCount; i++)
        {
            MethodDefinitionHandle method = metadata.AddMethodDefinition(
                MethodAttributes.Public
                    | MethodAttributes.Static
                    | MethodAttributes.PinvokeImpl,
                MethodImplAttributes.PreserveSig,
                metadata.GetOrAddString($"M{i}"),
                signatureBlob,
                bodyOffset: -1,
                MetadataTokens.ParameterHandle(1));
            metadata.AddMethodImport(
                method,
                MethodImportAttributes.CallingConventionWinApi,
                metadata.GetOrAddString($"M{i}"),
                moduleRef);
        }

        var pe = new ManagedPEBuilder(
            new PEHeaderBuilder(
                imageCharacteristics:
                    Characteristics.Dll | Characteristics.ExecutableImage),
            new MetadataRootBuilder(metadata),
            ilStream: new BlobBuilder());
        var image = new BlobBuilder();
        pe.Serialize(image);
        return image.ToArray();
    }
}
