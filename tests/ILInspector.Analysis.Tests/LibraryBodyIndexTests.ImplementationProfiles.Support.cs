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

    static ImmutableArray<byte> EmitReturnOverloadAssembly()
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            0,
            metadata.GetOrAddString("ReturnOverloads.dll"),
            metadata.GetOrAddGuid(Guid.NewGuid()),
            default,
            default);
        metadata.AddAssembly(
            metadata.GetOrAddString("ReturnOverloads"),
            new Version(1, 0, 0, 0),
            default,
            default,
            default,
            default);
        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        TypeDefinitionHandle type = metadata.AddTypeDefinition(
            TypeAttributes.Public,
            metadata.GetOrAddString("Probe"),
            metadata.GetOrAddString("Sample"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));

        BlobHandle returnsInt = AddSignature(0x08);
        BlobHandle returnsString = AddSignature(0x0e);
        BlobHandle takesInt = AddSignature(0x01, 0x08);
        MemberReferenceHandle stringTarget =
            metadata.AddMemberReference(
                type,
                metadata.GetOrAddString("Route"),
                returnsString);

        var bodies = new BlobBuilder();
        var bodyEncoder = new MethodBodyStreamEncoder(bodies);
        int intBody = AddBody(0x16, 0x2a);
        int stringBody = AddBody(0x14, 0x2a);
        var callerIl = new BlobBuilder();
        callerIl.WriteByte(0x28);
        callerIl.WriteInt32(
            MetadataTokens.GetToken(stringTarget));
        callerIl.WriteByte(0x26);
        callerIl.WriteByte(0x2a);
        int callerBody = bodyEncoder.AddMethodBody(
            new InstructionEncoder(callerIl),
            maxStack: 1);

        AddMethod(returnsInt, intBody);
        AddMethod(returnsString, stringBody);
        AddMethod(takesInt, callerBody);

        var pe = new ManagedPEBuilder(
            PEHeaderBuilder.CreateLibraryHeader(),
            new MetadataRootBuilder(metadata),
            bodies,
            flags: CorFlags.ILOnly);
        var image = new BlobBuilder();
        pe.Serialize(image);
        return ImmutableArray.CreateRange(image.ToArray());

        BlobHandle AddSignature(
            byte returnType,
            params byte[] parameters)
        {
            var signature = new BlobBuilder();
            signature.WriteByte(0x00);
            signature.WriteCompressedInteger(parameters.Length);
            signature.WriteByte(returnType);
            foreach (byte parameter in parameters)
                signature.WriteByte(parameter);
            return metadata.GetOrAddBlob(signature);
        }

        int AddBody(params byte[] il)
        {
            var code = new BlobBuilder();
            code.WriteBytes(il);
            return bodyEncoder.AddMethodBody(
                new InstructionEncoder(code),
                maxStack: 1);
        }

        void AddMethod(
            BlobHandle signature,
            int bodyOffset)
        {
            metadata.AddMethodDefinition(
                MethodAttributes.Public
                    | MethodAttributes.Static,
                MethodImplAttributes.IL,
                metadata.GetOrAddString("Route"),
                signature,
                bodyOffset,
                MetadataTokens.ParameterHandle(1));
        }
    }

    static ImmutableArray<byte> EmitVarargOverloadAssembly(
        bool methodDefinitionParent = false)
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            0,
            metadata.GetOrAddString("VarargOverloads.dll"),
            metadata.GetOrAddGuid(Guid.NewGuid()),
            default,
            default);
        metadata.AddAssembly(
            metadata.GetOrAddString("VarargOverloads"),
            new Version(1, 0, 0, 0),
            default,
            default,
            default,
            default);
        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        TypeDefinitionHandle type = metadata.AddTypeDefinition(
            TypeAttributes.Public,
            metadata.GetOrAddString("Probe"),
            metadata.GetOrAddString("Sample"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));

        BlobHandle targetSignature =
            AddSignature(
                0x05,
                parameterCount: 1,
                0x01,
                0x08);
        BlobHandle callerSignature =
            AddSignature(
                0x00,
                parameterCount: 0,
                0x01);
        BlobHandle callSiteSignature =
            AddSignature(
                0x05,
                parameterCount: 2,
                0x01,
                0x08,
                0x41,
                0x08);
        BlobHandle secondCallSiteSignature =
            AddSignature(
                0x05,
                parameterCount: 2,
                0x01,
                0x08,
                0x41,
                0x0e);
        EntityHandle targetParent =
            methodDefinitionParent
                ? MetadataTokens.MethodDefinitionHandle(1)
                : type;
        MemberReferenceHandle targetReference =
            metadata.AddMemberReference(
                targetParent,
                metadata.GetOrAddString("Route"),
                callSiteSignature);
        MemberReferenceHandle secondTargetReference =
            metadata.AddMemberReference(
                targetParent,
                metadata.GetOrAddString("Route"),
                secondCallSiteSignature);

        var bodies = new BlobBuilder();
        var bodyEncoder = new MethodBodyStreamEncoder(bodies);
        int targetBody = AddBody(0x2a);
        var callerIl = new BlobBuilder();
        callerIl.WriteByte(0x16);
        callerIl.WriteByte(0x17);
        callerIl.WriteByte(0x28);
        callerIl.WriteInt32(
            MetadataTokens.GetToken(targetReference));
        if (methodDefinitionParent)
        {
            callerIl.WriteByte(0x16);
            callerIl.WriteByte(0x14);
            callerIl.WriteByte(0x28);
            callerIl.WriteInt32(
                MetadataTokens.GetToken(
                    secondTargetReference));
        }
        callerIl.WriteByte(0x2a);
        int callerBody = bodyEncoder.AddMethodBody(
            new InstructionEncoder(callerIl),
            maxStack: 2);

        AddMethod(targetSignature, targetBody);
        AddMethod(callerSignature, callerBody);

        var pe = new ManagedPEBuilder(
            PEHeaderBuilder.CreateLibraryHeader(),
            new MetadataRootBuilder(metadata),
            bodies,
            flags: CorFlags.ILOnly);
        var image = new BlobBuilder();
        pe.Serialize(image);
        return ImmutableArray.CreateRange(image.ToArray());

        BlobHandle AddSignature(
            byte header,
            int parameterCount,
            params byte[] signatureBytes)
        {
            var signature = new BlobBuilder();
            signature.WriteByte(header);
            signature.WriteCompressedInteger(parameterCount);
            signature.WriteBytes(signatureBytes);
            return metadata.GetOrAddBlob(signature);
        }

        int AddBody(params byte[] il)
        {
            var code = new BlobBuilder();
            code.WriteBytes(il);
            return bodyEncoder.AddMethodBody(
                new InstructionEncoder(code),
                maxStack: 1);
        }

        void AddMethod(
            BlobHandle signature,
            int bodyOffset)
        {
            metadata.AddMethodDefinition(
                MethodAttributes.Public
                    | MethodAttributes.Static,
                MethodImplAttributes.IL,
                metadata.GetOrAddString("Route"),
                signature,
                bodyOffset,
                MetadataTokens.ParameterHandle(1));
        }
    }

    static ImmutableArray<byte>
        EmitExternalScopeCollisionAssembly()
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            0,
            metadata.GetOrAddString("SelfCollision.dll"),
            metadata.GetOrAddGuid(Guid.NewGuid()),
            default,
            default);
        metadata.AddAssembly(
            metadata.GetOrAddString("SelfCollision"),
            new Version(1, 0, 0, 0),
            default,
            default,
            default,
            default);
        AssemblyReferenceHandle externalAssembly =
            metadata.AddAssemblyReference(
                metadata.GetOrAddString("SelfCollision"),
                new Version(2, 0, 0, 0),
                default,
                metadata.GetOrAddBlob(
                    new byte[]
                    {
                        0x01, 0x23, 0x45, 0x67,
                        0x89, 0xab, 0xcd, 0xef,
                    }),
                default,
                default);
        TypeReferenceHandle externalType =
            metadata.AddTypeReference(
                externalAssembly,
                metadata.GetOrAddString("Probe"),
                metadata.GetOrAddString("Sample"));

        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        metadata.AddTypeDefinition(
            TypeAttributes.Public,
            metadata.GetOrAddString("Probe"),
            metadata.GetOrAddString("Sample"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));

        BlobHandle noParameters = AddSignature(0x01);
        BlobHandle takesInt = AddSignature(0x01, 0x08);
        MemberReferenceHandle externalTarget =
            metadata.AddMemberReference(
                externalType,
                metadata.GetOrAddString("Route"),
                noParameters);

        var bodies = new BlobBuilder();
        var bodyEncoder = new MethodBodyStreamEncoder(bodies);
        int targetBody = AddBody(0x2a);
        var callerIl = new BlobBuilder();
        callerIl.WriteByte(0x28);
        callerIl.WriteInt32(
            MetadataTokens.GetToken(externalTarget));
        callerIl.WriteByte(0x2a);
        int callerBody = bodyEncoder.AddMethodBody(
            new InstructionEncoder(callerIl),
            maxStack: 1);

        AddMethod(noParameters, targetBody);
        AddMethod(takesInt, callerBody);

        var pe = new ManagedPEBuilder(
            PEHeaderBuilder.CreateLibraryHeader(),
            new MetadataRootBuilder(metadata),
            bodies,
            flags: CorFlags.ILOnly);
        var image = new BlobBuilder();
        pe.Serialize(image);
        return ImmutableArray.CreateRange(image.ToArray());

        BlobHandle AddSignature(
            byte returnType,
            params byte[] parameters)
        {
            var signature = new BlobBuilder();
            signature.WriteByte(0x00);
            signature.WriteCompressedInteger(
                parameters.Length);
            signature.WriteByte(returnType);
            foreach (byte parameter in parameters)
                signature.WriteByte(parameter);
            return metadata.GetOrAddBlob(signature);
        }

        int AddBody(params byte[] il)
        {
            var code = new BlobBuilder();
            code.WriteBytes(il);
            return bodyEncoder.AddMethodBody(
                new InstructionEncoder(code),
                maxStack: 1);
        }

        void AddMethod(
            BlobHandle signature,
            int bodyOffset)
        {
            metadata.AddMethodDefinition(
                MethodAttributes.Public
                    | MethodAttributes.Static,
                MethodImplAttributes.IL,
                metadata.GetOrAddString("Route"),
                signature,
                bodyOffset,
                MetadataTokens.ParameterHandle(1));
        }
    }

    static ImmutableArray<byte>
        EmitExternalSignatureTypeCollisionAssembly()
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            0,
            metadata.GetOrAddString("ParameterCollision.dll"),
            metadata.GetOrAddGuid(Guid.NewGuid()),
            default,
            default);
        metadata.AddAssembly(
            metadata.GetOrAddString("ParameterCollision"),
            new Version(1, 0, 0, 0),
            default,
            default,
            default,
            default);
        AssemblyReferenceHandle externalAssembly =
            metadata.AddAssemblyReference(
                metadata.GetOrAddString("ParameterCollision"),
                new Version(2, 0, 0, 0),
                default,
                default,
                default,
                default);
        TypeReferenceHandle externalArgument =
            metadata.AddTypeReference(
                externalAssembly,
                metadata.GetOrAddString("Probe"),
                metadata.GetOrAddString("Argument"));

        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        TypeDefinitionHandle localArgument =
            metadata.AddTypeDefinition(
                TypeAttributes.Public,
                metadata.GetOrAddString("Probe"),
                metadata.GetOrAddString("Argument"),
                default,
                MetadataTokens.FieldDefinitionHandle(1),
                MetadataTokens.MethodDefinitionHandle(1));
        TypeDefinitionHandle owner =
            metadata.AddTypeDefinition(
                TypeAttributes.Public,
                metadata.GetOrAddString("Probe"),
                metadata.GetOrAddString("Owner"),
                default,
                MetadataTokens.FieldDefinitionHandle(1),
                MetadataTokens.MethodDefinitionHandle(1));

        BlobHandle targetSignature =
            AddSignature(localArgument);
        BlobHandle callerSignature = AddIntSignature();
        MemberReferenceHandle externalTarget =
            metadata.AddMemberReference(
                owner,
                metadata.GetOrAddString("Route"),
                AddSignature(externalArgument));

        var bodies = new BlobBuilder();
        var bodyEncoder = new MethodBodyStreamEncoder(bodies);
        int targetBody = AddBody(0x2a);
        var callerIl = new BlobBuilder();
        callerIl.WriteByte(0x14);
        callerIl.WriteByte(0x28);
        callerIl.WriteInt32(
            MetadataTokens.GetToken(externalTarget));
        callerIl.WriteByte(0x2a);
        int callerBody = bodyEncoder.AddMethodBody(
            new InstructionEncoder(callerIl),
            maxStack: 1);

        AddMethod(targetSignature, targetBody);
        AddMethod(callerSignature, callerBody);

        var pe = new ManagedPEBuilder(
            PEHeaderBuilder.CreateLibraryHeader(),
            new MetadataRootBuilder(metadata),
            bodies,
            flags: CorFlags.ILOnly);
        var image = new BlobBuilder();
        pe.Serialize(image);
        return ImmutableArray.CreateRange(image.ToArray());

        BlobHandle AddSignature(EntityHandle argument)
        {
            var signature = new BlobBuilder();
            new BlobEncoder(signature)
                .MethodSignature(isInstanceMethod: false)
                .Parameters(
                    1,
                    returns => returns.Void(),
                    parameters => parameters
                        .AddParameter()
                        .Type()
                        .Type(
                            argument,
                            isValueType: false));
            return metadata.GetOrAddBlob(signature);
        }

        BlobHandle AddIntSignature()
        {
            var signature = new BlobBuilder();
            new BlobEncoder(signature)
                .MethodSignature(isInstanceMethod: false)
                .Parameters(
                    1,
                    returns => returns.Void(),
                    parameters => parameters
                        .AddParameter()
                        .Type()
                        .Int32());
            return metadata.GetOrAddBlob(signature);
        }

        int AddBody(params byte[] il)
        {
            var code = new BlobBuilder();
            code.WriteBytes(il);
            return bodyEncoder.AddMethodBody(
                new InstructionEncoder(code),
                maxStack: 1);
        }

        void AddMethod(
            BlobHandle signature,
            int bodyOffset)
        {
            metadata.AddMethodDefinition(
                MethodAttributes.Public
                    | MethodAttributes.Static,
                MethodImplAttributes.IL,
                metadata.GetOrAddString("Route"),
                signature,
                bodyOffset,
                MetadataTokens.ParameterHandle(1));
        }
    }

    static ImmutableArray<byte>
        EmitGuardRejectedLocalSignatureAssembly()
        => EmitLocalSignatureAssembly(
            "DeepLocal",
            localSignature =>
            {
                localSignature.WriteByte(0x07);
                localSignature.WriteByte(0x01);
                for (int i = 0;
                    i < SignatureBlobGuard.DefaultMaxDepth;
                    i++)
                {
                    localSignature.WriteByte(0x1d);
                }
                localSignature.WriteByte(0x08);
            });

    static ImmutableArray<byte>
        EmitMalformedLocalSignatureAssembly()
        => EmitLocalSignatureAssembly(
            "MalformedLocal",
            localSignature => localSignature.WriteByte(0x06));

    static ImmutableArray<byte> EmitLocalSignatureAssembly(
        string assemblyName,
        Action<BlobBuilder> writeLocalSignature)
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
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        metadata.AddTypeDefinition(
            TypeAttributes.Public,
            metadata.GetOrAddString("Probe"),
            metadata.GetOrAddString("Owner"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));

        var localSignature = new BlobBuilder();
        writeLocalSignature(localSignature);
        StandaloneSignatureHandle localSignatureHandle =
            metadata.AddStandaloneSignature(
                metadata.GetOrAddBlob(localSignature));

        var bodies = new BlobBuilder();
        var code = new BlobBuilder();
        code.WriteByte(0x2a);
        int bodyOffset =
            new MethodBodyStreamEncoder(bodies)
                .AddMethodBody(
                    new InstructionEncoder(code),
                    maxStack: 0,
                    localVariablesSignature:
                        localSignatureHandle,
                    attributes:
                        MethodBodyAttributes.InitLocals);

        var methodSignature = new BlobBuilder();
        new BlobEncoder(methodSignature)
            .MethodSignature(isInstanceMethod: false)
            .Parameters(
                0,
                returns => returns.Void(),
                _ => { });
        metadata.AddMethodDefinition(
            MethodAttributes.Public
                | MethodAttributes.Static,
            MethodImplAttributes.IL,
            metadata.GetOrAddString("M"),
            metadata.GetOrAddBlob(methodSignature),
            bodyOffset,
            MetadataTokens.ParameterHandle(1));

        var pe = new ManagedPEBuilder(
            PEHeaderBuilder.CreateLibraryHeader(),
            new MetadataRootBuilder(metadata),
            bodies,
            flags: CorFlags.ILOnly);
        var image = new BlobBuilder();
        pe.Serialize(image);
        return ImmutableArray.CreateRange(image.ToArray());
    }

}
