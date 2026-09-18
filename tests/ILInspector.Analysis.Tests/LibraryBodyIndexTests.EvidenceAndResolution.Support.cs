using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

using DotnetInspector.Fixtures;
using DotnetInspector.Services;
using ILInspector.Analysis;
using ILInspector.Analysis.ClassicAsyncFixtures;
using ILInspector.Analysis.MalformedOwnershipFixtures;
using ILInspector.Analysis.UnoptimizedAsyncFixtures;
using ILInspector.CallGraph;
using ILInspector.Instructions;
using ILInspector.Metadata;

namespace ILInspector.Analysis.Tests;

public partial class LibraryBodyIndexTests
{

    static byte[] EmitFieldAliasAssembly()
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            0,
            metadata.GetOrAddString("FieldAlias.dll"),
            metadata.GetOrAddGuid(Guid.NewGuid()),
            default,
            default);
        metadata.AddAssembly(
            metadata.GetOrAddString("FieldAlias"),
            new Version(1, 0, 0, 0),
            default,
            default,
            default,
            AssemblyHashAlgorithm.Sha1);

        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        TypeDefinitionHandle owner = metadata.AddTypeDefinition(
            TypeAttributes.Public,
            metadata.GetOrAddString("Fixtures"),
            metadata.GetOrAddString("Context"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));

        var int32FieldSignature = new BlobBuilder();
        new BlobEncoder(int32FieldSignature)
            .FieldSignature()
            .Int32();
        BlobHandle int32Field =
            metadata.GetOrAddBlob(int32FieldSignature);
        FieldDefinitionHandle definition =
            metadata.AddFieldDefinition(
                FieldAttributes.Public | FieldAttributes.Static,
                metadata.GetOrAddString("Value"),
                int32Field);
        MemberReferenceHandle alias =
            metadata.AddMemberReference(
                owner,
                metadata.GetOrAddString("Value"),
                int32Field);

        AssemblyReferenceHandle selfAssembly =
            metadata.AddAssemblyReference(
                metadata.GetOrAddString("FieldAlias"),
                new Version(1, 0, 0, 0),
                default,
                default,
                default,
                default);
        TypeReferenceHandle selfType =
            metadata.AddTypeReference(
                selfAssembly,
                metadata.GetOrAddString("Fixtures"),
                metadata.GetOrAddString("Context"));
        MemberReferenceHandle selfAlias =
            metadata.AddMemberReference(
                selfType,
                metadata.GetOrAddString("Value"),
                int32Field);

        ModuleReferenceHandle selfModule =
            metadata.AddModuleReference(
                metadata.GetOrAddString("FieldAlias.dll"));
        TypeReferenceHandle moduleType =
            metadata.AddTypeReference(
                selfModule,
                metadata.GetOrAddString("Fixtures"),
                metadata.GetOrAddString("Context"));
        MemberReferenceHandle moduleAlias =
            metadata.AddMemberReference(
                moduleType,
                metadata.GetOrAddString("Value"),
                int32Field);

        AssemblyReferenceHandle externalAssembly =
            metadata.AddAssemblyReference(
                metadata.GetOrAddString("FieldAlias.External"),
                new Version(1, 0, 0, 0),
                default,
                default,
                default,
                default);
        TypeReferenceHandle externalType =
            metadata.AddTypeReference(
                externalAssembly,
                metadata.GetOrAddString("Fixtures"),
                metadata.GetOrAddString("Context"));
        MemberReferenceHandle externalAlias =
            metadata.AddMemberReference(
                externalType,
                metadata.GetOrAddString("Value"),
                int32Field);
        var localTypeSpecSignature = new BlobBuilder();
        localTypeSpecSignature.WriteByte(0x12);
        localTypeSpecSignature.WriteCompressedInteger(
            MetadataTokens.GetRowNumber(owner) << 2);
        TypeSpecificationHandle localTypeSpec =
            metadata.AddTypeSpecification(
                metadata.GetOrAddBlob(localTypeSpecSignature));
        MemberReferenceHandle localTypeSpecAlias =
            metadata.AddMemberReference(
                localTypeSpec,
                metadata.GetOrAddString("Value"),
                int32Field);
        AssemblyReferenceHandle sameNameExternalAssembly =
            metadata.AddAssemblyReference(
                metadata.GetOrAddString("FieldAlias"),
                new Version(2, 0, 0, 0),
                default,
                default,
                default,
                default);
        TypeReferenceHandle sameNameExternalType =
            metadata.AddTypeReference(
                sameNameExternalAssembly,
                metadata.GetOrAddString("Fixtures"),
                metadata.GetOrAddString("Context"));
        MemberReferenceHandle sameNameExternalAlias =
            metadata.AddMemberReference(
                sameNameExternalType,
                metadata.GetOrAddString("Value"),
                int32Field);

        var int64FieldSignature = new BlobBuilder();
        new BlobEncoder(int64FieldSignature)
            .FieldSignature()
            .Int64();
        MemberReferenceHandle wrongSignature =
            metadata.AddMemberReference(
                owner,
                metadata.GetOrAddString("Value"),
                metadata.GetOrAddBlob(int64FieldSignature));
        MemberReferenceHandle wrongSelfSignature =
            metadata.AddMemberReference(
                selfType,
                metadata.GetOrAddString("Value"),
                metadata.GetOrAddBlob(int64FieldSignature));

        var methodSignature = new BlobBuilder();
        new BlobEncoder(methodSignature)
            .MethodSignature(isInstanceMethod: false)
            .Parameters(
                0,
                returnType => returnType.Void(),
                _ => { });
        var il = new BlobBuilder();
        foreach (EntityHandle field in new EntityHandle[]
            {
                definition,
                alias,
                selfAlias,
                moduleAlias,
                externalAlias,
                localTypeSpecAlias,
                sameNameExternalAlias,
                wrongSignature,
                wrongSelfSignature,
            })
        {
            il.WriteByte((byte)ILOpCode.Ldc_i4_0);
            il.WriteByte((byte)ILOpCode.Stsfld);
            il.WriteInt32(MetadataTokens.GetToken(field));
        }
        il.WriteByte((byte)ILOpCode.Ret);
        var bodies = new BlobBuilder();
        int body = new MethodBodyStreamEncoder(bodies)
            .AddMethodBody(
                new InstructionEncoder(il),
                maxStack: 1);
        metadata.AddMethodDefinition(
            MethodAttributes.Public | MethodAttributes.Static,
            MethodImplAttributes.IL,
            metadata.GetOrAddString("M"),
            metadata.GetOrAddBlob(methodSignature),
            body,
            default);

        var pe = new ManagedPEBuilder(
            PEHeaderBuilder.CreateLibraryHeader(),
            new MetadataRootBuilder(metadata),
            bodies,
            flags: CorFlags.ILOnly);
        var image = new BlobBuilder();
        pe.Serialize(image);
        return image.ToArray();
    }

}
