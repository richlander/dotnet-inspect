using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using ILInspector.Decompiler;
using ILInspector.Decompiler.Pipeline;
using ILInspector.Metadata;

namespace DotnetInspector.Tests;

public sealed class BodyShapeSearchMetadataTests
{
    [Fact]
    public void Search_DefaultIncludesNestedExternalExplicitInterface()
    {
        WithImage(BuildNestedExternalInterfaceImage(), path =>
        {
            using var source = MetadataSource.Open(path);

            var result = BodyShapeSearch.Search(
                source,
                "LiteralExpression",
                cancellationToken: TestContext.Current.CancellationToken);

            var match = Assert.Single(result.Matches);
            Assert.Contains("explicit:Contracts.Outer.IProbe.Target~", match.Member);
            Assert.Equal("Sample.Impl", match.TypeName);
        });
    }

    [Fact]
    public void Search_CyclicExplicitInterfaceIsAnExplicitFailure()
    {
        WithImage(BuildCyclicInterfaceImage(), path =>
        {
            using var source = MetadataSource.Open(path);

            var result = BodyShapeSearch.Search(
                source,
                "LiteralExpression",
                cancellationToken: TestContext.Current.CancellationToken);

            Assert.Empty(result.Matches);
            Assert.Contains(result.Failures, failure =>
                failure.Subject.StartsWith("explicit-interface visibility at ", StringComparison.Ordinal)
                && failure.Reason.Contains("repeats handle", StringComparison.Ordinal));
        });
    }

    [Fact]
    public void Search_InvalidMethodImplDeclarationIsAnExplicitFailure()
    {
        WithImage(BuildInvalidMethodImplImage(), path =>
        {
            using var source = MetadataSource.Open(path);

            var result = BodyShapeSearch.Search(
                source,
                "LiteralExpression",
                cancellationToken: TestContext.Current.CancellationToken);

            Assert.Empty(result.Matches);
            Assert.Contains(result.Failures, failure =>
                failure.Subject.StartsWith("explicit-interface visibility at ", StringComparison.Ordinal)
                && failure.Reason.Contains("out of bounds", StringComparison.OrdinalIgnoreCase));
        });
    }

    [Fact]
    public void Search_OverBudgetTypeReferenceScopeIsAnExplicitFailure()
    {
        WithImage(BuildOverBudgetTypeReferenceImage(), path =>
        {
            using var source = MetadataSource.Open(path);

            var result = BodyShapeSearch.Search(
                source,
                "LiteralExpression",
                cancellationToken: TestContext.Current.CancellationToken);

            Assert.Empty(result.Matches);
            Assert.Contains(result.Failures, failure =>
                failure.Subject.StartsWith("explicit-interface visibility at ", StringComparison.Ordinal)
                && failure.Reason.Contains("exceeds 256 nodes", StringComparison.Ordinal));
        });
    }

    static byte[] BuildNestedExternalInterfaceImage()
    {
        var metadata = CreateMetadata();
        var contracts = metadata.AddAssemblyReference(
            metadata.GetOrAddString("Contracts"),
            new Version(1, 0, 0, 0),
            culture: default,
            publicKeyOrToken: default,
            flags: default,
            hashValue: default);
        var outer = metadata.AddTypeReference(
            contracts,
            metadata.GetOrAddString("Contracts"),
            metadata.GetOrAddString("Outer"));
        var probe = metadata.AddTypeReference(
            outer,
            default,
            metadata.GetOrAddString("IProbe"));
        var declaration = metadata.AddMemberReference(
            probe,
            metadata.GetOrAddString("Target"),
            InstanceInt32Signature(metadata));

        AddModuleType(metadata);
        var implementationType = metadata.AddTypeDefinition(
            TypeAttributes.Public,
            metadata.GetOrAddString("Sample"),
            metadata.GetOrAddString("Impl"),
            baseType: default,
            fieldList: MetadataTokens.FieldDefinitionHandle(1),
            methodList: MetadataTokens.MethodDefinitionHandle(1));
        var (methodBodies, bodyOffset) = Int32Body(1);
        var body = metadata.AddMethodDefinition(
            MethodAttributes.Private | MethodAttributes.Final
                | MethodAttributes.Virtual | MethodAttributes.HideBySig,
            MethodImplAttributes.IL,
            metadata.GetOrAddString("Contracts.Outer.IProbe.Target"),
            InstanceInt32Signature(metadata),
            bodyOffset,
            MetadataTokens.ParameterHandle(1));
        metadata.AddInterfaceImplementation(implementationType, probe);
        metadata.AddMethodImplementation(implementationType, body, declaration);
        return Serialize(metadata, methodBodies);
    }

    static byte[] BuildCyclicInterfaceImage()
    {
        var metadata = CreateMetadata();
        AddModuleType(metadata);
        var cyclicInterface = metadata.AddTypeDefinition(
            TypeAttributes.NestedPublic | TypeAttributes.Interface | TypeAttributes.Abstract,
            metadata.GetOrAddString("Contracts"),
            metadata.GetOrAddString("ICyclic"),
            baseType: default,
            fieldList: MetadataTokens.FieldDefinitionHandle(1),
            methodList: MetadataTokens.MethodDefinitionHandle(1));
        var implementationType = metadata.AddTypeDefinition(
            TypeAttributes.Public,
            metadata.GetOrAddString("Sample"),
            metadata.GetOrAddString("Impl"),
            baseType: default,
            fieldList: MetadataTokens.FieldDefinitionHandle(1),
            methodList: MetadataTokens.MethodDefinitionHandle(2));
        metadata.AddNestedType(cyclicInterface, cyclicInterface);

        var declaration = metadata.AddMethodDefinition(
            MethodAttributes.Public | MethodAttributes.Abstract
                | MethodAttributes.Virtual | MethodAttributes.HideBySig,
            MethodImplAttributes.IL,
            metadata.GetOrAddString("Target"),
            InstanceInt32Signature(metadata),
            bodyOffset: 0,
            MetadataTokens.ParameterHandle(1));
        var (methodBodies, bodyOffset) = Int32Body(1);
        var body = metadata.AddMethodDefinition(
            MethodAttributes.Private | MethodAttributes.Final
                | MethodAttributes.Virtual | MethodAttributes.HideBySig,
            MethodImplAttributes.IL,
            metadata.GetOrAddString("Contracts.ICyclic.Target"),
            InstanceInt32Signature(metadata),
            bodyOffset,
            MetadataTokens.ParameterHandle(1));
        metadata.AddMethodImplementation(implementationType, body, declaration);
        return Serialize(metadata, methodBodies);
    }

    static byte[] BuildInvalidMethodImplImage()
    {
        var metadata = CreateMetadata();
        AddModuleType(metadata);
        var implementationType = metadata.AddTypeDefinition(
            TypeAttributes.Public,
            metadata.GetOrAddString("Sample"),
            metadata.GetOrAddString("Impl"),
            baseType: default,
            fieldList: MetadataTokens.FieldDefinitionHandle(1),
            methodList: MetadataTokens.MethodDefinitionHandle(1));
        var (methodBodies, bodyOffset) = Int32Body(1);
        var body = metadata.AddMethodDefinition(
            MethodAttributes.Private | MethodAttributes.Final
                | MethodAttributes.Virtual | MethodAttributes.HideBySig,
            MethodImplAttributes.IL,
            metadata.GetOrAddString("Invalid.Target"),
            InstanceInt32Signature(metadata),
            bodyOffset,
            MetadataTokens.ParameterHandle(1));
        metadata.AddMethodImplementation(
            implementationType,
            body,
            MetadataTokens.MemberReferenceHandle(99));
        return Serialize(metadata, methodBodies);
    }

    static byte[] BuildOverBudgetTypeReferenceImage()
    {
        var metadata = CreateMetadata();
        EntityHandle scope = metadata.AddAssemblyReference(
            metadata.GetOrAddString("Contracts"),
            new Version(1, 0, 0, 0),
            culture: default,
            publicKeyOrToken: default,
            flags: default,
            hashValue: default);
        for (int i = 0; i < MetadataSafetyPolicy.MaxRelationshipNodes + 1; i++)
        {
            scope = metadata.AddTypeReference(
                scope,
                i == 0 ? metadata.GetOrAddString("Contracts") : default,
                metadata.GetOrAddString($"Nested{i}"));
        }
        var declaration = metadata.AddMemberReference(
            scope,
            metadata.GetOrAddString("Target"),
            InstanceInt32Signature(metadata));

        AddModuleType(metadata);
        var implementationType = metadata.AddTypeDefinition(
            TypeAttributes.Public,
            metadata.GetOrAddString("Sample"),
            metadata.GetOrAddString("Impl"),
            baseType: default,
            fieldList: MetadataTokens.FieldDefinitionHandle(1),
            methodList: MetadataTokens.MethodDefinitionHandle(1));
        var (methodBodies, bodyOffset) = Int32Body(1);
        var body = metadata.AddMethodDefinition(
            MethodAttributes.Private | MethodAttributes.Final
                | MethodAttributes.Virtual | MethodAttributes.HideBySig,
            MethodImplAttributes.IL,
            metadata.GetOrAddString("Contracts.Nested.Target"),
            InstanceInt32Signature(metadata),
            bodyOffset,
            MetadataTokens.ParameterHandle(1));
        metadata.AddMethodImplementation(implementationType, body, declaration);
        return Serialize(metadata, methodBodies);
    }

    static MetadataBuilder CreateMetadata()
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
        return metadata;
    }

    static void AddModuleType(MetadataBuilder metadata)
        => metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            baseType: default,
            fieldList: MetadataTokens.FieldDefinitionHandle(1),
            methodList: MetadataTokens.MethodDefinitionHandle(1));

    static BlobHandle InstanceInt32Signature(MetadataBuilder metadata)
    {
        var signature = new BlobBuilder();
        signature.WriteByte(0x20);
        signature.WriteCompressedInteger(0);
        signature.WriteByte(0x08);
        return metadata.GetOrAddBlob(signature);
    }

    static (BlobBuilder MethodBodies, int BodyOffset) Int32Body(int value)
    {
        var methodBodies = new BlobBuilder();
        var instructions = new BlobBuilder();
        var encoder = new InstructionEncoder(instructions, new ControlFlowBuilder());
        encoder.LoadConstantI4(value);
        encoder.OpCode(ILOpCode.Ret);
        int bodyOffset = new MethodBodyStreamEncoder(methodBodies)
            .AddMethodBody(encoder, maxStack: 1);
        return (methodBodies, bodyOffset);
    }

    static byte[] Serialize(MetadataBuilder metadata, BlobBuilder methodBodies)
    {
        var pe = new ManagedPEBuilder(
            PEHeaderBuilder.CreateLibraryHeader(),
            new MetadataRootBuilder(metadata, suppressValidation: true),
            methodBodies,
            flags: CorFlags.ILOnly);
        var image = new BlobBuilder();
        pe.Serialize(image);
        return image.ToArray();
    }

    static void WithImage(byte[] image, Action<string> assertion)
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            $"body-shape-metadata-{Guid.NewGuid():N}.dll");
        File.WriteAllBytes(path, image);
        try
        {
            assertion(path);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
