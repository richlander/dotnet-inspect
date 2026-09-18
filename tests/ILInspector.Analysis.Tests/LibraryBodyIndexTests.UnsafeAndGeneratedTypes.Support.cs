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

    static byte[] EmitAssemblyIdentity(
        string name,
        byte[] publicKey)
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            0,
            metadata.GetOrAddString(name + ".dll"),
            metadata.GetOrAddGuid(Guid.NewGuid()),
            default,
            default);
        metadata.AddAssembly(
            metadata.GetOrAddString(name),
            new Version(1, 0, 0, 0),
            default,
            publicKey.Length == 0
                ? default
                : metadata.GetOrAddBlob(publicKey),
            publicKey.Length == 0
                ? default
                : AssemblyFlags.PublicKey,
            AssemblyHashAlgorithm.Sha1);
        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        var pe = new ManagedPEBuilder(
            PEHeaderBuilder.CreateLibraryHeader(),
            new MetadataRootBuilder(metadata),
            new BlobBuilder(),
            flags: CorFlags.ILOnly);
        var image = new BlobBuilder();
        pe.Serialize(image);
        return image.ToArray();
    }

    static string? FindRealGoogleProtobufAssembly()
    {
        string root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nuget", "packages", "google.protobuf");
        if (!Directory.Exists(root))
            return null;
        return Directory.EnumerateFiles(root, "Google.Protobuf.dll", SearchOption.AllDirectories)
            .OrderByDescending(p => p)
            .FirstOrDefault();
    }

    static bool SameClrType(TypeRef type, Type clr)
        => type.Namespace == (clr.Namespace ?? "")
            && type.Name == MetadataName(clr);

    static string MetadataName(Type type)
        => type.DeclaringType is null
            ? type.Name
            : MetadataName(type.DeclaringType) + "+" + type.Name;

    static byte[] EmitDisplayNameCollisionAssembly()
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            generation: 0,
            metadata.GetOrAddString("DisplayCollision.dll"),
            metadata.GetOrAddGuid(Guid.NewGuid()),
            default,
            default);
        metadata.AddAssembly(
            metadata.GetOrAddString("DisplayCollision"),
            new Version(1, 0, 0, 0),
            culture: default,
            publicKey: default,
            flags: default,
            hashAlgorithm: System.Reflection.AssemblyHashAlgorithm.Sha1);

        var voidSignature = new BlobBuilder();
        new BlobEncoder(voidSignature)
            .MethodSignature(isInstanceMethod: false)
            .Parameters(0, returnType => returnType.Void(), _ => { });
        BlobHandle voidSignatureHandle = metadata.GetOrAddBlob(voidSignature);

        var bodies = new BlobBuilder();
        var bodyEncoder = new MethodBodyStreamEncoder(bodies);
        var retIl = new BlobBuilder();
        retIl.WriteByte((byte)ILOpCode.Ret);
        int retBody = bodyEncoder.AddMethodBody(
            new InstructionEncoder(retIl),
            maxStack: 0);

        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            baseType: default,
            fieldList: MetadataTokens.FieldDefinitionHandle(1),
            methodList: MetadataTokens.MethodDefinitionHandle(1));

        metadata.AddTypeDefinition(
            TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed,
            metadata.GetOrAddString("Grpc.Core"),
            metadata.GetOrAddString("ServerServiceDefinition"),
            baseType: default,
            fieldList: MetadataTokens.FieldDefinitionHandle(1),
            methodList: MetadataTokens.MethodDefinitionHandle(1));
        MethodDefinitionHandle createBuilder = metadata.AddMethodDefinition(
            MethodAttributes.Public | MethodAttributes.Static,
            MethodImplAttributes.IL,
            metadata.GetOrAddString("CreateBuilder"),
            voidSignatureHandle,
            retBody,
            parameterList: default);

        var bindIl = new BlobBuilder();
        bindIl.WriteByte((byte)ILOpCode.Call);
        bindIl.WriteInt32(MetadataTokens.GetToken(createBuilder));
        bindIl.WriteByte((byte)ILOpCode.Ret);
        int bindBody = bodyEncoder.AddMethodBody(
            new InstructionEncoder(bindIl),
            maxStack: 0);

        metadata.AddTypeDefinition(
            TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed,
            metadata.GetOrAddString("CollisionNs.A"),
            metadata.GetOrAddString("GeneratedLeaf"),
            baseType: default,
            fieldList: MetadataTokens.FieldDefinitionHandle(1),
            methodList: MetadataTokens.MethodDefinitionHandle(2));
        metadata.AddMethodDefinition(
            MethodAttributes.Public | MethodAttributes.Static,
            MethodImplAttributes.IL,
            metadata.GetOrAddString("__Helper_SerializeMessage"),
            voidSignatureHandle,
            retBody,
            parameterList: default);
        metadata.AddMethodDefinition(
            MethodAttributes.Public | MethodAttributes.Static,
            MethodImplAttributes.IL,
            metadata.GetOrAddString("BindService"),
            voidSignatureHandle,
            bindBody,
            parameterList: default);

        TypeDefinitionHandle collisionOuter = metadata.AddTypeDefinition(
            TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed,
            metadata.GetOrAddString("CollisionNs"),
            metadata.GetOrAddString("A"),
            baseType: default,
            fieldList: MetadataTokens.FieldDefinitionHandle(1),
            methodList: MetadataTokens.MethodDefinitionHandle(4));
        TypeDefinitionHandle collisionNested = metadata.AddTypeDefinition(
            TypeAttributes.NestedPublic | TypeAttributes.Abstract | TypeAttributes.Sealed,
            default,
            metadata.GetOrAddString("GeneratedLeaf"),
            baseType: default,
            fieldList: MetadataTokens.FieldDefinitionHandle(1),
            methodList: MetadataTokens.MethodDefinitionHandle(4));
        metadata.AddNestedType(collisionNested, collisionOuter);
        metadata.AddMethodDefinition(
            MethodAttributes.Public | MethodAttributes.Static,
            MethodImplAttributes.IL,
            metadata.GetOrAddString("NotGenerated"),
            voidSignatureHandle,
            retBody,
            parameterList: default);

        metadata.AddTypeDefinition(
            TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed,
            metadata.GetOrAddString("ReverseNs.A"),
            metadata.GetOrAddString("NestedLeaf"),
            baseType: default,
            fieldList: MetadataTokens.FieldDefinitionHandle(1),
            methodList: MetadataTokens.MethodDefinitionHandle(5));
        metadata.AddMethodDefinition(
            MethodAttributes.Public | MethodAttributes.Static,
            MethodImplAttributes.IL,
            metadata.GetOrAddString("NotGenerated"),
            voidSignatureHandle,
            retBody,
            parameterList: default);

        TypeDefinitionHandle reverseOuter = metadata.AddTypeDefinition(
            TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed,
            metadata.GetOrAddString("ReverseNs"),
            metadata.GetOrAddString("A"),
            baseType: default,
            fieldList: MetadataTokens.FieldDefinitionHandle(1),
            methodList: MetadataTokens.MethodDefinitionHandle(6));
        TypeDefinitionHandle reverseNested = metadata.AddTypeDefinition(
            TypeAttributes.NestedPublic | TypeAttributes.Abstract | TypeAttributes.Sealed,
            default,
            metadata.GetOrAddString("NestedLeaf"),
            baseType: default,
            fieldList: MetadataTokens.FieldDefinitionHandle(1),
            methodList: MetadataTokens.MethodDefinitionHandle(6));
        metadata.AddNestedType(reverseNested, reverseOuter);
        metadata.AddMethodDefinition(
            MethodAttributes.Public | MethodAttributes.Static,
            MethodImplAttributes.IL,
            metadata.GetOrAddString("__Helper_SerializeMessage"),
            voidSignatureHandle,
            retBody,
            parameterList: default);
        metadata.AddMethodDefinition(
            MethodAttributes.Public | MethodAttributes.Static,
            MethodImplAttributes.IL,
            metadata.GetOrAddString("BindService"),
            voidSignatureHandle,
            bindBody,
            parameterList: default);

        var pe = new ManagedPEBuilder(
            PEHeaderBuilder.CreateLibraryHeader(),
            new MetadataRootBuilder(metadata),
            bodies,
            flags: CorFlags.ILOnly);
        var image = new BlobBuilder();
        pe.Serialize(image);
        return image.ToArray();
    }

    static byte[] EmitLiteralPlusVsNestedAssembly()
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            generation: 0,
            metadata.GetOrAddString("LiteralPlus.dll"),
            metadata.GetOrAddGuid(Guid.NewGuid()),
            default,
            default);
        metadata.AddAssembly(
            metadata.GetOrAddString("LiteralPlus"),
            new Version(1, 0, 0, 0),
            culture: default,
            publicKey: default,
            flags: default,
            hashAlgorithm: System.Reflection.AssemblyHashAlgorithm.Sha1);

        var voidSignature = new BlobBuilder();
        new BlobEncoder(voidSignature)
            .MethodSignature(isInstanceMethod: false)
            .Parameters(0, returnType => returnType.Void(), _ => { });
        BlobHandle voidSignatureHandle = metadata.GetOrAddBlob(voidSignature);

        var bodies = new BlobBuilder();
        var bodyEncoder = new MethodBodyStreamEncoder(bodies);
        var retIl = new BlobBuilder();
        retIl.WriteByte((byte)ILOpCode.Ret);
        int retBody = bodyEncoder.AddMethodBody(
            new InstructionEncoder(retIl),
            maxStack: 0);

        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            baseType: default,
            fieldList: MetadataTokens.FieldDefinitionHandle(1),
            methodList: MetadataTokens.MethodDefinitionHandle(1));

        metadata.AddTypeDefinition(
            TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed,
            metadata.GetOrAddString("Grpc.Core"),
            metadata.GetOrAddString("ServerServiceDefinition"),
            baseType: default,
            fieldList: MetadataTokens.FieldDefinitionHandle(1),
            methodList: MetadataTokens.MethodDefinitionHandle(1));
        MethodDefinitionHandle createBuilder = metadata.AddMethodDefinition(
            MethodAttributes.Public | MethodAttributes.Static,
            MethodImplAttributes.IL,
            metadata.GetOrAddString("CreateBuilder"),
            voidSignatureHandle,
            retBody,
            parameterList: default);

        var bindIl = new BlobBuilder();
        bindIl.WriteByte((byte)ILOpCode.Call);
        bindIl.WriteInt32(MetadataTokens.GetToken(createBuilder));
        bindIl.WriteByte((byte)ILOpCode.Ret);
        int bindBody = bodyEncoder.AddMethodBody(
            new InstructionEncoder(bindIl),
            maxStack: 0);

        TypeDefinitionHandle stub = metadata.AddTypeDefinition(
            TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed,
            metadata.GetOrAddString("Ns"),
            metadata.GetOrAddString("GenStub"),
            baseType: default,
            fieldList: MetadataTokens.FieldDefinitionHandle(1),
            methodList: MetadataTokens.MethodDefinitionHandle(2));
        metadata.AddMethodDefinition(
            MethodAttributes.Public | MethodAttributes.Static,
            MethodImplAttributes.IL,
            metadata.GetOrAddString("__Helper_SerializeMessage"),
            voidSignatureHandle,
            retBody,
            parameterList: default);
        metadata.AddMethodDefinition(
            MethodAttributes.Public | MethodAttributes.Static,
            MethodImplAttributes.IL,
            metadata.GetOrAddString("BindService"),
            voidSignatureHandle,
            bindBody,
            parameterList: default);

        TypeDefinitionHandle nested = metadata.AddTypeDefinition(
            TypeAttributes.NestedPublic | TypeAttributes.Abstract | TypeAttributes.Sealed,
            default,
            metadata.GetOrAddString("Inner"),
            baseType: default,
            fieldList: MetadataTokens.FieldDefinitionHandle(1),
            methodList: MetadataTokens.MethodDefinitionHandle(4));
        metadata.AddNestedType(nested, stub);
        metadata.AddMethodDefinition(
            MethodAttributes.Public | MethodAttributes.Static,
            MethodImplAttributes.IL,
            metadata.GetOrAddString("InnerMethod"),
            voidSignatureHandle,
            retBody,
            parameterList: default);

        metadata.AddTypeDefinition(
            TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed,
            metadata.GetOrAddString("Ns"),
            metadata.GetOrAddString("GenStub+LiteralPlus"),
            baseType: default,
            fieldList: MetadataTokens.FieldDefinitionHandle(1),
            methodList: MetadataTokens.MethodDefinitionHandle(5));
        metadata.AddMethodDefinition(
            MethodAttributes.Public | MethodAttributes.Static,
            MethodImplAttributes.IL,
            metadata.GetOrAddString("UnrelatedUserMethod"),
            voidSignatureHandle,
            retBody,
            parameterList: default);

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
