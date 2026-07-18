using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using ILInspector.Findings;

namespace ILInspector.Metadata.Tests;

public class ApiSurfaceRelationshipFailureTests
{
    [Fact]
    public void ExtractApiSurface_CyclicTypePreservesValidSiblingAndFailure()
    {
        using var stream = new MemoryStream(BuildImage(
            cyclicTypeName: "Rejected",
            validTypeNames: ["Sibling"]));

        var surface = AssemblyReader.ExtractApiSurface(
            stream,
            includeAll: true,
            typesOnly: true);

        Assert.NotNull(surface);
        var sibling = Assert.Single(surface.Types);
        Assert.Equal("Sibling", sibling.Name);
        Assert.Equal(1, surface.PublicTypeCount);
        var failure = Assert.Single(surface.InspectionFailures);
        Assert.Equal("type identity", failure.Operation);
        Assert.Equal(0x02000002, failure.SubjectToken);
        Assert.Equal(MetadataTypeNameFailureMechanism.Relationship, failure.Mechanism);
        Assert.Equal("Cycle", failure.Kind);
    }

    [Fact]
    public void ApiDiff_IncompleteNewIdentityDoesNotClaimOldTypeWasRemoved()
    {
        using var oldStream = new MemoryStream(BuildImage(
            cyclicTypeName: null,
            validTypeNames: ["Maybe"]));
        using var newStream = new MemoryStream(BuildImage(
            cyclicTypeName: "Maybe",
            validTypeNames: ["Sibling"]));

        var oldSurface = AssemblyReader.ExtractApiSurface(
            oldStream,
            includeAll: true,
            typesOnly: true);
        var newSurface = AssemblyReader.ExtractApiSurface(
            newStream,
            includeAll: true,
            typesOnly: true);

        Assert.NotNull(oldSurface);
        Assert.NotNull(newSurface);
        var diff = ApiDiffAnalyzer.Compare(oldSurface, newSurface);

        Assert.DoesNotContain(
            diff.TypeDiffs,
            type => type.TypeFullName == "Maybe" && type.IsRemoved);
        Assert.Contains(
            diff.TypeDiffs,
            type => type.TypeFullName == "Sibling" && type.IsAdded);
        var failure = Assert.Single(diff.InspectionFailures);
        Assert.Equal("new", failure.Side);
        Assert.Equal("Cycle", failure.Kind);
        Assert.False(diff.IsEmpty);

        var findings = MetadataFindings.CompareApi(
            oldSurface,
            newSurface,
            new FindingSubject("api", "API surface"));
        Assert.IsType<
            FindingComparison<ApiTypeHandle>.Failed>(findings.Types.Value);
        Assert.False(findings.IsExact);
    }

    [Fact]
    public void EnumDefaultLookup_DoesNotAttributeUnrelatedFailureToValidType()
    {
        using var stream = new MemoryStream(BuildEnumDefaultImage());

        var surface = AssemblyReader.ExtractApiSurface(
            stream,
            includeAll: true);

        Assert.NotNull(surface);
        var consumer = Assert.Single(
            surface.Types,
            type => type.Name == "Consumer");
        var method = Assert.Single(
            consumer.Members,
            member => member.Name == "M");
        Assert.Contains(
            "color = GoodEnum.Red",
            method.Signature,
            StringComparison.Ordinal);
        Assert.Contains(
            surface.InspectionFailures,
            failure => failure.SubjectToken == 0x01000001
                && failure.Kind == "Cycle");
        Assert.DoesNotContain(
            surface.InspectionFailures,
            failure => failure.SubjectToken == 0x02000002);
    }

    static byte[] BuildImage(
        string? cyclicTypeName,
        IReadOnlyList<string> validTypeNames)
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            generation: 0,
            moduleName: metadata.GetOrAddString("Synthetic.dll"),
            mvid: metadata.GetOrAddGuid(Guid.NewGuid()),
            encId: default,
            encBaseId: default);
        metadata.AddAssembly(
            metadata.GetOrAddString("Synthetic"),
            new Version(1, 0, 0, 0),
            culture: default,
            publicKey: default,
            flags: default,
            hashAlgorithm: default);
        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            baseType: default,
            fieldList: MetadataTokens.FieldDefinitionHandle(1),
            methodList: MetadataTokens.MethodDefinitionHandle(1));

        if (cyclicTypeName is not null)
        {
            var cyclic = metadata.AddTypeDefinition(
                TypeAttributes.NestedPublic,
                default,
                metadata.GetOrAddString(cyclicTypeName),
                baseType: default,
                fieldList: MetadataTokens.FieldDefinitionHandle(1),
                methodList: MetadataTokens.MethodDefinitionHandle(1));
            metadata.AddNestedType(cyclic, cyclic);
        }

        foreach (string name in validTypeNames)
        {
            metadata.AddTypeDefinition(
                TypeAttributes.Public,
                default,
                metadata.GetOrAddString(name),
                baseType: default,
                fieldList: MetadataTokens.FieldDefinitionHandle(1),
                methodList: MetadataTokens.MethodDefinitionHandle(1));
        }

        var pe = new ManagedPEBuilder(
            PEHeaderBuilder.CreateLibraryHeader(),
            new MetadataRootBuilder(metadata, suppressValidation: true),
            new BlobBuilder(),
            flags: CorFlags.ILOnly);
        var image = new BlobBuilder();
        pe.Serialize(image);
        return image.ToArray();
    }

    static byte[] BuildEnumDefaultImage()
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            generation: 0,
            moduleName: metadata.GetOrAddString("Synthetic.dll"),
            mvid: metadata.GetOrAddGuid(Guid.NewGuid()),
            encId: default,
            encBaseId: default);
        metadata.AddAssembly(
            metadata.GetOrAddString("Synthetic"),
            new Version(1, 0, 0, 0),
            culture: default,
            publicKey: default,
            flags: default,
            hashAlgorithm: default);
        var coreLib = metadata.AddAssemblyReference(
            metadata.GetOrAddString("System.Private.CoreLib"),
            new Version(11, 0, 0, 0),
            culture: default,
            publicKeyOrToken: default,
            flags: default,
            hashValue: default);
        var cyclicBase = metadata.AddTypeReference(
            MetadataTokens.TypeReferenceHandle(1),
            metadata.GetOrAddString("N"),
            metadata.GetOrAddString("Loop"));
        var enumBase = metadata.AddTypeReference(
            coreLib,
            metadata.GetOrAddString("System"),
            metadata.GetOrAddString("Enum"));

        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            baseType: default,
            fieldList: MetadataTokens.FieldDefinitionHandle(1),
            methodList: MetadataTokens.MethodDefinitionHandle(1));
        metadata.AddTypeDefinition(
            TypeAttributes.Public,
            default,
            metadata.GetOrAddString("Consumer"),
            baseType: default,
            fieldList: MetadataTokens.FieldDefinitionHandle(1),
            methodList: MetadataTokens.MethodDefinitionHandle(1));
        metadata.AddTypeDefinition(
            TypeAttributes.Public,
            default,
            metadata.GetOrAddString("Rejected"),
            cyclicBase,
            fieldList: MetadataTokens.FieldDefinitionHandle(1),
            methodList: MetadataTokens.MethodDefinitionHandle(2));
        metadata.AddTypeDefinition(
            TypeAttributes.Public | TypeAttributes.Sealed,
            default,
            metadata.GetOrAddString("GoodEnum"),
            enumBase,
            fieldList: MetadataTokens.FieldDefinitionHandle(1),
            methodList: MetadataTokens.MethodDefinitionHandle(2));

        var valueField = metadata.AddFieldDefinition(
            FieldAttributes.Public | FieldAttributes.SpecialName | FieldAttributes.RTSpecialName,
            metadata.GetOrAddString("value__"),
            FieldSignature(metadata));
        var redField = metadata.AddFieldDefinition(
            FieldAttributes.Public | FieldAttributes.Static | FieldAttributes.Literal,
            metadata.GetOrAddString("Red"),
            FieldSignature(metadata));
        metadata.AddConstant(redField, 0);

        var parameter = metadata.AddParameter(
            ParameterAttributes.Optional | ParameterAttributes.HasDefault,
            metadata.GetOrAddString("color"),
            sequenceNumber: 1);
        metadata.AddConstant(parameter, 0);

        var instructions = new BlobBuilder();
        var encoder = new InstructionEncoder(
            instructions,
            new ControlFlowBuilder());
        encoder.OpCode(ILOpCode.Ret);
        var methodBodies = new BlobBuilder();
        int bodyOffset = new MethodBodyStreamEncoder(methodBodies)
            .AddMethodBody(encoder, maxStack: 0);
        metadata.AddMethodDefinition(
            MethodAttributes.Public | MethodAttributes.Static,
            MethodImplAttributes.IL,
            metadata.GetOrAddString("M"),
            EnumDefaultMethodSignature(metadata),
            bodyOffset,
            parameter);

        var pe = new ManagedPEBuilder(
            PEHeaderBuilder.CreateLibraryHeader(),
            new MetadataRootBuilder(metadata, suppressValidation: true),
            methodBodies,
            flags: CorFlags.ILOnly);
        var image = new BlobBuilder();
        pe.Serialize(image);
        return image.ToArray();
    }

    static BlobHandle FieldSignature(MetadataBuilder metadata)
    {
        var signature = new BlobBuilder();
        signature.WriteByte(0x06);
        signature.WriteByte(0x08);
        return metadata.GetOrAddBlob(signature);
    }

    static BlobHandle EnumDefaultMethodSignature(MetadataBuilder metadata)
    {
        var signature = new BlobBuilder();
        signature.WriteByte(0x00);
        signature.WriteCompressedInteger(1);
        signature.WriteByte(0x01);
        signature.WriteByte(0x11);
        signature.WriteCompressedInteger(4 << 2);
        return metadata.GetOrAddBlob(signature);
    }
}
