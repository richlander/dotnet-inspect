using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using ILInspector.Decompiler;
using ILInspector.Metadata;

namespace ILInspector.Decompiler.Tests;

public class CSharpBodyDiffRelationshipFailureTests
{
    [Fact]
    public void CompareAssemblies_CyclicTypePreservesValidSiblingDiff()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            $"dotnet-inspect-csharp-relationship-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        string oldPath = Path.Combine(directory, "old.dll");
        string newPath = Path.Combine(directory, "new.dll");

        try
        {
            File.WriteAllBytes(oldPath, BuildImage(1));
            File.WriteAllBytes(newPath, BuildImage(2));

            var diff = CSharpBodyDiff.CompareAssemblies(
                oldPath,
                newPath,
                includeNonPublic: true);

            Assert.Equal(2, diff.IdentityFailures.Length);
            Assert.All(
                diff.IdentityFailures,
                failure =>
                {
                    Assert.Equal(
                        MetadataTypeNameFailureMechanism.Relationship,
                        failure.Mechanism);
                    Assert.Equal("Cycle", failure.Kind);
                    Assert.Equal(0x02000002, failure.SubjectToken);
                });
            Assert.Contains(
                diff.Rows,
                row => row.Member.Contains("Valid.Value", StringComparison.Ordinal)
                    && row.OldValue == "1"
                    && row.NewValue == "2");
            Assert.False(diff.IsExact);

            var findings = CSharpFindings.CompareAssembliesWithFailures(
                [oldPath],
                [newPath],
                includeNonPublic: true);
            Assert.Equal(2, findings.IdentityFailures.Length);
            Assert.Contains(
                findings.Comparisons,
                comparison => comparison.Member.Contains(
                    "Valid.Value",
                    StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void CompareAssemblies_RejectedBodyIdentityDoesNotBecomeMethodAddOrRemove()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            $"dotnet-inspect-csharp-body-relationship-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        string oldPath = Path.Combine(directory, "old.dll");
        string newPath = Path.Combine(directory, "new.dll");

        try
        {
            File.WriteAllBytes(oldPath, BuildBodyReferenceImage());
            File.WriteAllBytes(newPath, BuildBodyReferenceImage());

            var diff = CSharpBodyDiff.CompareAssemblies(
                oldPath,
                newPath,
                includeNonPublic: true);

            Assert.Equal(2, diff.IdentityFailures.Length);
            Assert.Empty(diff.Rows);
            Assert.All(
                diff.IdentityFailures,
                failure =>
                {
                    Assert.Equal(
                        MetadataTypeNameFailureMechanism.Relationship,
                        failure.Mechanism);
                    Assert.Equal("Cycle", failure.Kind);
                    Assert.Equal(0x01000001, failure.SubjectToken);
                });
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    static byte[] BuildImage(int value)
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
        var rejected = metadata.AddTypeDefinition(
            TypeAttributes.NestedPublic,
            default,
            metadata.GetOrAddString("Rejected"),
            baseType: default,
            fieldList: MetadataTokens.FieldDefinitionHandle(1),
            methodList: MetadataTokens.MethodDefinitionHandle(1));
        metadata.AddNestedType(rejected, rejected);
        metadata.AddTypeDefinition(
            TypeAttributes.Public,
            default,
            metadata.GetOrAddString("Valid"),
            baseType: default,
            fieldList: MetadataTokens.FieldDefinitionHandle(1),
            methodList: MetadataTokens.MethodDefinitionHandle(2));

        var methodBodies = new BlobBuilder();
        var bodyEncoder = new MethodBodyStreamEncoder(methodBodies);

        var rejectedBody = new BlobBuilder();
        var rejectedInstructions = new InstructionEncoder(
            rejectedBody,
            new ControlFlowBuilder());
        rejectedInstructions.OpCode(ILOpCode.Ret);
        int rejectedBodyOffset = bodyEncoder.AddMethodBody(
            rejectedInstructions,
            maxStack: 0);
        metadata.AddMethodDefinition(
            MethodAttributes.Public | MethodAttributes.Static,
            MethodImplAttributes.IL,
            metadata.GetOrAddString("RejectedMethod"),
            VoidMethodSignature(metadata),
            rejectedBodyOffset,
            MetadataTokens.ParameterHandle(1));

        var validBody = new BlobBuilder();
        var validInstructions = new InstructionEncoder(
            validBody,
            new ControlFlowBuilder());
        validInstructions.LoadConstantI4(value);
        validInstructions.OpCode(ILOpCode.Ret);
        int validBodyOffset = bodyEncoder.AddMethodBody(
            validInstructions,
            maxStack: 1);
        metadata.AddMethodDefinition(
            MethodAttributes.Public | MethodAttributes.Static,
            MethodImplAttributes.IL,
            metadata.GetOrAddString("Value"),
            Int32MethodSignature(metadata),
            validBodyOffset,
            MetadataTokens.ParameterHandle(1));

        var pe = new ManagedPEBuilder(
            PEHeaderBuilder.CreateLibraryHeader(),
            new MetadataRootBuilder(metadata, suppressValidation: true),
            methodBodies,
            flags: CorFlags.ILOnly);
        var image = new BlobBuilder();
        pe.Serialize(image);
        return image.ToArray();
    }

    static byte[] BuildBodyReferenceImage()
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
        var cyclicType = metadata.AddTypeReference(
            MetadataTokens.TypeReferenceHandle(1),
            metadata.GetOrAddString("N"),
            metadata.GetOrAddString("Loop"));
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
            metadata.GetOrAddString("Valid"),
            baseType: default,
            fieldList: MetadataTokens.FieldDefinitionHandle(1),
            methodList: MetadataTokens.MethodDefinitionHandle(1));

        var methodBodies = new BlobBuilder();
        var instructions = new BlobBuilder();
        var encoder = new InstructionEncoder(
            instructions,
            new ControlFlowBuilder());
        encoder.OpCode(ILOpCode.Ldtoken);
        encoder.Token(cyclicType);
        encoder.OpCode(ILOpCode.Pop);
        encoder.OpCode(ILOpCode.Ret);
        int bodyOffset = new MethodBodyStreamEncoder(methodBodies)
            .AddMethodBody(encoder, maxStack: 1);
        metadata.AddMethodDefinition(
            MethodAttributes.Public | MethodAttributes.Static,
            MethodImplAttributes.IL,
            metadata.GetOrAddString("M"),
            VoidMethodSignature(metadata),
            bodyOffset,
            MetadataTokens.ParameterHandle(1));

        var pe = new ManagedPEBuilder(
            PEHeaderBuilder.CreateLibraryHeader(),
            new MetadataRootBuilder(metadata, suppressValidation: true),
            methodBodies,
            flags: CorFlags.ILOnly);
        var image = new BlobBuilder();
        pe.Serialize(image);
        return image.ToArray();
    }

    static BlobHandle VoidMethodSignature(MetadataBuilder metadata)
    {
        var signature = new BlobBuilder();
        signature.WriteByte(0x00);
        signature.WriteCompressedInteger(0);
        signature.WriteByte(0x01);
        return metadata.GetOrAddBlob(signature);
    }

    static BlobHandle Int32MethodSignature(MetadataBuilder metadata)
    {
        var signature = new BlobBuilder();
        signature.WriteByte(0x00);
        signature.WriteCompressedInteger(0);
        signature.WriteByte(0x08);
        return metadata.GetOrAddBlob(signature);
    }
}
