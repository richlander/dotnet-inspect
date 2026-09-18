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

    static LibraryBodyIndex OpenMemorySafetyContractImage(
        params int?[] moduleMarkers)
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            $"AnalysisMemorySafety-{Guid.NewGuid():N}.dll");
        try
        {
            File.WriteAllBytes(
                path,
                BuildMemorySafetyContractImage(moduleMarkers));
            return LibraryBodyIndex.Open(
                path,
                LibraryBodyAnalysisFeatures.MethodEvidence);
        }
        finally
        {
            File.Delete(path);
        }
    }

    static byte[] BuildMemorySafetyContractImage(
        IReadOnlyList<int?> moduleMarkers,
        bool includePointerSignature = true,
        MemorySafetyCallTarget callTarget =
            MemorySafetyCallTarget.PointerOnly,
        string aliasModuleName = "AnalysisMemorySafety.dll",
        bool includeLocalParameter = false,
        string? parameterModuleName = null,
        AssemblyReferenceIdentity? assemblyAlias = null,
        string aliasNamespace = "Samples",
        string aliasTypeName = "Target",
        string aliasMemberName = "AttributeOnly",
        string localTypeName = "Target")
    {
        var metadata = new MetadataBuilder();
        ModuleDefinitionHandle module = metadata.AddModule(
            0,
            metadata.GetOrAddString("AnalysisMemorySafety.dll"),
            metadata.GetOrAddGuid(Guid.NewGuid()),
            default,
            default);
        metadata.AddAssembly(
            metadata.GetOrAddString("AnalysisMemorySafety"),
            new Version(1, 0, 0, 0),
            default,
            default,
            default,
            default);

        BlobHandle rulesConstructorSignature =
            AddMemorySafetyMethodSignature(
                metadata,
                isInstance: true,
                parameterCount: 1,
                parameters =>
                    parameters.AddParameter().Type().Int32());
        BlobHandle markerConstructorSignature =
            AddMemorySafetyMethodSignature(
                metadata,
                isInstance: true,
                parameterCount: 0,
                _ => { });
        BlobHandle pointerMethodSignature =
            AddMemorySafetyMethodSignature(
                metadata,
                isInstance: false,
                parameterCount: includePointerSignature ? 1 : 0,
                parameters =>
                {
                    if (includePointerSignature)
                    {
                        parameters
                            .AddParameter()
                            .Type()
                            .Pointer()
                            .Int32();
                    }
                });
        BlobHandle emptyMethodSignature =
            AddMemorySafetyMethodSignature(
                metadata,
                isInstance: false,
                parameterCount: 0,
                _ => { });
        TypeReferenceHandle localType = metadata.AddTypeReference(
            module,
            metadata.GetOrAddString("Samples"),
            metadata.GetOrAddString(localTypeName));
        TypeReferenceHandle moduleAliasType = metadata.AddTypeReference(
            metadata.AddModuleReference(metadata.GetOrAddString(aliasModuleName)),
            metadata.GetOrAddString("Samples"),
            metadata.GetOrAddString("Target"));
        TypeReferenceHandle parameterAliasType = parameterModuleName is null
            ? moduleAliasType
            : metadata.AddTypeReference(
                metadata.AddModuleReference(
                    metadata.GetOrAddString(parameterModuleName)),
                metadata.GetOrAddString("Samples"),
                metadata.GetOrAddString("Target"));
        BlobHandle localSignature = includeLocalParameter
            ? AddMemorySafetyMethodSignature(
                metadata,
                isInstance: false,
                parameterCount: 1,
                parameters => parameters.AddParameter().Type()
                    .SZArray().Type(localType, isValueType: false))
            : emptyMethodSignature;
        BlobHandle aliasSignature = includeLocalParameter
            ? AddMemorySafetyMethodSignature(
                metadata,
                isInstance: false,
                parameterCount: 1,
                parameters => parameters.AddParameter().Type()
                    .SZArray().Type(parameterAliasType, isValueType: false))
            : emptyMethodSignature;
        BlobHandle moduleAliasCallerSignature =
            AddMemorySafetyMethodSignature(
                metadata,
                isInstance: false,
                parameterCount: 1,
                parameters =>
                    parameters.AddParameter()
                        .Type()
                        .Int32());
        var varArgTargetSignature = new BlobBuilder();
        new BlobEncoder(varArgTargetSignature)
            .MethodSignature(
                SignatureCallingConvention.VarArgs,
                genericParameterCount: 0,
                isInstanceMethod: false)
            .Parameters(
                parameterCount: 1,
                returnType => returnType.Void(),
                parameters =>
                    parameters.AddParameter()
                        .Type()
                        .Int32());
        BlobHandle varArgTargetSignatureHandle =
            metadata.GetOrAddBlob(
                varArgTargetSignature);
        var varArgCallSiteSignature =
            new BlobBuilder();
        new BlobEncoder(varArgCallSiteSignature)
            .MethodSignature(
                SignatureCallingConvention.VarArgs,
                genericParameterCount: 0,
                isInstanceMethod: false)
            .Parameters(
                parameterCount: 2,
                returnType => returnType.Void(),
                parameters =>
                {
                    parameters.AddParameter()
                        .Type()
                        .Int32();
                    parameters.StartVarArgs()
                        .AddParameter()
                        .Type()
                        .Int32();
                });
        BlobHandle varArgCallSiteSignatureHandle =
            metadata.GetOrAddBlob(
                varArgCallSiteSignature);

        var bodies = new BlobBuilder();
        var bodyEncoder = new MethodBodyStreamEncoder(bodies);
        var body = new BlobBuilder();
        var instructions = new InstructionEncoder(body);
        instructions.OpCode(ILOpCode.Ret);
        int bodyOffset =
            bodyEncoder.AddMethodBody(instructions, maxStack: 0);

        MethodDefinitionHandle rulesConstructor =
            metadata.AddMethodDefinition(
                MethodAttributes.Public
                    | MethodAttributes.SpecialName
                    | MethodAttributes.RTSpecialName,
                MethodImplAttributes.Runtime,
                metadata.GetOrAddString(".ctor"),
                rulesConstructorSignature,
                bodyOffset: -1,
                MetadataTokens.ParameterHandle(1));
        MethodDefinitionHandle requiresUnsafeConstructor =
            metadata.AddMethodDefinition(
                MethodAttributes.Public
                    | MethodAttributes.SpecialName
                    | MethodAttributes.RTSpecialName,
                MethodImplAttributes.Runtime,
                metadata.GetOrAddString(".ctor"),
                markerConstructorSignature,
                bodyOffset: -1,
                MetadataTokens.ParameterHandle(1));
        MethodDefinitionHandle pointerOnly =
            metadata.AddMethodDefinition(
            MethodAttributes.Public | MethodAttributes.Static,
            MethodImplAttributes.IL,
            metadata.GetOrAddString("PointerOnly"),
            pointerMethodSignature,
            bodyOffset,
            MetadataTokens.ParameterHandle(1));
        MethodDefinitionHandle attributeOnly =
            metadata.AddMethodDefinition(
                MethodAttributes.Public | MethodAttributes.Static,
                MethodImplAttributes.IL,
                metadata.GetOrAddString("AttributeOnly"),
                localSignature,
                bodyOffset,
                MetadataTokens.ParameterHandle(1));
        MethodDefinitionHandle moduleAliasTarget =
            metadata.AddMethodDefinition(
                MethodAttributes.Public
                    | MethodAttributes.Static,
                callTarget
                    == MemorySafetyCallTarget
                        .ModuleReferenceBodilessAttributeOnly
                    ? MethodImplAttributes.Runtime
                    : MethodImplAttributes.IL,
                metadata.GetOrAddString("ModuleAlias"),
                localSignature,
                callTarget
                    == MemorySafetyCallTarget
                        .ModuleReferenceBodilessAttributeOnly
                    ? -1
                    : bodyOffset,
                MetadataTokens.ParameterHandle(1));
        if (callTarget
            is MemorySafetyCallTarget
                .AmbiguousLocalTypeReferenceAttributeOnly
                or MemorySafetyCallTarget
                    .BodyBodilessAmbiguousLocalTypeReferenceAttributeOnly)
        {
            metadata.AddMethodDefinition(
                MethodAttributes.Public
                    | MethodAttributes.Static,
                callTarget
                    == MemorySafetyCallTarget
                        .BodyBodilessAmbiguousLocalTypeReferenceAttributeOnly
                    ? MethodImplAttributes.Runtime
                    : MethodImplAttributes.IL,
                metadata.GetOrAddString("AttributeOnly"),
                localSignature,
                callTarget
                    == MemorySafetyCallTarget
                        .BodyBodilessAmbiguousLocalTypeReferenceAttributeOnly
                    ? -1
                    : bodyOffset,
                MetadataTokens.ParameterHandle(1));
        }
        MethodDefinitionHandle varArgAttributeOnly =
            metadata.AddMethodDefinition(
                MethodAttributes.Public
                    | MethodAttributes.Static,
                MethodImplAttributes.IL,
                metadata.GetOrAddString(
                    "VarArgAttributeOnly"),
                varArgTargetSignatureHandle,
                bodyOffset,
                MetadataTokens.ParameterHandle(1));
        EntityHandle callerTarget = callTarget switch
        {
            MemorySafetyCallTarget.PointerOnly => pointerOnly,
            MemorySafetyCallTarget.AttributeOnly => attributeOnly,
            MemorySafetyCallTarget.LocalTypeReferenceAttributeOnly =>
                metadata.AddMemberReference(
                    metadata.AddTypeReference(
                        module,
                        metadata.GetOrAddString("Samples"),
                        metadata.GetOrAddString("Target")),
                    metadata.GetOrAddString("AttributeOnly"),
                    emptyMethodSignature),
            MemorySafetyCallTarget
                .AmbiguousLocalTypeReferenceAttributeOnly
                or MemorySafetyCallTarget
                    .BodyBodilessAmbiguousLocalTypeReferenceAttributeOnly =>
                metadata.AddMemberReference(
                    metadata.AddTypeReference(
                        module,
                        metadata.GetOrAddString("Samples"),
                        metadata.GetOrAddString(localTypeName)),
                    metadata.GetOrAddString("AttributeOnly"),
                    emptyMethodSignature),
            MemorySafetyCallTarget
                .LiteralPlusConstructedAttributeOnly =>
                    metadata.AddMemberReference(
                        metadata.AddTypeSpecification(
                            AddConstructedTypeSignature(
                                metadata,
                                localType)),
                        metadata.GetOrAddString("AttributeOnly"),
                        emptyMethodSignature),
            MemorySafetyCallTarget.ModuleReferenceAttributeOnly
                or MemorySafetyCallTarget
                    .ModuleReferenceBodilessAttributeOnly =>
                metadata.AddMemberReference(
                    moduleAliasType,
                    metadata.GetOrAddString("ModuleAlias"),
                    aliasSignature),
            MemorySafetyCallTarget.AssemblyReferenceAttributeOnly
                when assemblyAlias is { Version: { } aliasVersion } =>
                metadata.AddMemberReference(
                    metadata.AddTypeReference(
                        metadata.AddAssemblyReference(
                            metadata.GetOrAddString(assemblyAlias.Name),
                            aliasVersion,
                            metadata.GetOrAddString(assemblyAlias.Culture ?? ""),
                            assemblyAlias.PublicKeyToken is { } token
                                ? metadata.GetOrAddBlob(Convert.FromHexString(token))
                                : default,
                            default,
                            default),
                        metadata.GetOrAddString(aliasNamespace),
                        metadata.GetOrAddString(aliasTypeName)),
                    metadata.GetOrAddString(aliasMemberName),
                    emptyMethodSignature),
            MemorySafetyCallTarget
                .MethodDefinitionParentVarArgAttributeOnly =>
                    metadata.AddMemberReference(
                        varArgAttributeOnly,
                        metadata.GetOrAddString(
                            "VarArgAttributeOnly"),
                        varArgCallSiteSignatureHandle),
            MemorySafetyCallTarget.ExternalSameNameAttributeOnly =>
                metadata.AddMemberReference(
                    metadata.AddTypeReference(
                        metadata.AddAssemblyReference(
                            metadata.GetOrAddString(
                                "AnalysisMemorySafety"),
                            new Version(9, 0, 0, 0),
                            default,
                            metadata.GetOrAddBlob(
                                new byte[]
                                {
                                    0x01, 0x02, 0x03, 0x04,
                                    0x05, 0x06, 0x07, 0x08,
                                }),
                            default,
                            default),
                        metadata.GetOrAddString("Samples"),
                        metadata.GetOrAddString("Target")),
                    metadata.GetOrAddString("AttributeOnly"),
                    emptyMethodSignature),
            _ => throw new InvalidOperationException(
                $"Unsupported memory-safety call target: {callTarget}."),
        };
        var callerBody = new BlobBuilder();
        var callerInstructions =
            new InstructionEncoder(callerBody);
        if (callTarget == MemorySafetyCallTarget.PointerOnly
            && includePointerSignature)
        {
            callerInstructions.OpCode(ILOpCode.Ldarg_0);
        }
        else if (callTarget
            == MemorySafetyCallTarget
                .MethodDefinitionParentVarArgAttributeOnly)
        {
            callerInstructions.LoadConstantI4(1);
            callerInstructions.LoadConstantI4(2);
        }
        else if (includeLocalParameter)
        {
            callerInstructions.OpCode(ILOpCode.Ldnull);
        }
        callerInstructions.Call(callerTarget);
        callerInstructions.OpCode(ILOpCode.Ret);
        int callerBodyOffset =
            bodyEncoder.AddMethodBody(
                callerInstructions,
                maxStack: 1);
        metadata.AddMethodDefinition(
            MethodAttributes.Public | MethodAttributes.Static,
            MethodImplAttributes.IL,
            metadata.GetOrAddString(
                callTarget switch
                {
                    MemorySafetyCallTarget.PointerOnly =>
                        "CallsPointerOnly",
                    MemorySafetyCallTarget.AttributeOnly =>
                        "CallsAttributeOnly",
                    MemorySafetyCallTarget
                        .LocalTypeReferenceAttributeOnly =>
                            "CallsLocalAlias",
                    MemorySafetyCallTarget.ModuleReferenceAttributeOnly =>
                        "ModuleAlias",
                    MemorySafetyCallTarget
                        .ModuleReferenceBodilessAttributeOnly =>
                            "CallsBodilessModuleAlias",
                    MemorySafetyCallTarget
                        .AmbiguousLocalTypeReferenceAttributeOnly =>
                            "CallsAmbiguousAlias",
                    MemorySafetyCallTarget
                        .BodyBodilessAmbiguousLocalTypeReferenceAttributeOnly =>
                            "CallsAmbiguousBodyAlias",
                    MemorySafetyCallTarget
                        .LiteralPlusConstructedAttributeOnly =>
                            "CallsLiteralPlusConstructed",
                    MemorySafetyCallTarget.AssemblyReferenceAttributeOnly =>
                        "CallsAssemblyAlias",
                    MemorySafetyCallTarget
                        .ExternalSameNameAttributeOnly =>
                            "CallsExternalAlias",
                    MemorySafetyCallTarget
                        .MethodDefinitionParentVarArgAttributeOnly =>
                            "CallsVarArgAttributeOnly",
                    _ => throw new InvalidOperationException(),
                }),
            callTarget switch
            {
                MemorySafetyCallTarget.PointerOnly =>
                    pointerMethodSignature,
                MemorySafetyCallTarget.ModuleReferenceAttributeOnly =>
                    moduleAliasCallerSignature,
                _ => emptyMethodSignature,
            },
            callerBodyOffset,
            MetadataTokens.ParameterHandle(1));

        metadata.AddTypeDefinition(
            TypeAttributes.NotPublic,
            default,
            metadata.GetOrAddString("<Module>"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            rulesConstructor);
        metadata.AddTypeDefinition(
            TypeAttributes.NotPublic,
            metadata.GetOrAddString(
                "System.Runtime.CompilerServices"),
            metadata.GetOrAddString(
                "MemorySafetyRulesAttribute"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            rulesConstructor);
        metadata.AddTypeDefinition(
            TypeAttributes.NotPublic,
            metadata.GetOrAddString(
                "System.Diagnostics.CodeAnalysis"),
            metadata.GetOrAddString("RequiresUnsafeAttribute"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            requiresUnsafeConstructor);
        TypeDefinitionHandle targetType =
            metadata.AddTypeDefinition(
            TypeAttributes.Public,
            metadata.GetOrAddString("Samples"),
            metadata.GetOrAddString(localTypeName),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(3));
        if (callTarget
            == MemorySafetyCallTarget
                .LiteralPlusConstructedAttributeOnly)
        {
            metadata.AddGenericParameter(
                targetType,
                GenericParameterAttributes.None,
                metadata.GetOrAddString("T"),
                index: 0);
        }

        foreach (int? marker in moduleMarkers)
        {
            metadata.AddCustomAttribute(
                module,
                rulesConstructor,
                metadata.GetOrAddBlob(
                    marker is int version
                        ? MemorySafetyRulesBlob(version)
                        : [0x01, 0x00, 0x02]));
        }

        metadata.AddCustomAttribute(
            attributeOnly,
            requiresUnsafeConstructor,
            metadata.GetOrAddBlob(
                new byte[] { 0x01, 0x00, 0x00, 0x00 }));
        metadata.AddCustomAttribute(
            moduleAliasTarget,
            requiresUnsafeConstructor,
            metadata.GetOrAddBlob(
                new byte[] { 0x01, 0x00, 0x00, 0x00 }));
        metadata.AddCustomAttribute(
            varArgAttributeOnly,
            requiresUnsafeConstructor,
            metadata.GetOrAddBlob(
                new byte[] { 0x01, 0x00, 0x00, 0x00 }));

        var pe = new ManagedPEBuilder(
            PEHeaderBuilder.CreateLibraryHeader(),
            new MetadataRootBuilder(
                metadata,
                suppressValidation: true),
            bodies,
            flags: CorFlags.ILOnly);
        var image = new BlobBuilder();
        pe.Serialize(image);
        return image.ToArray();
    }

    static BlobHandle AddConstructedTypeSignature(
        MetadataBuilder metadata,
        EntityHandle genericType)
    {
        var signature = new BlobBuilder();
        new BlobEncoder(signature)
            .TypeSpecificationSignature()
            .GenericInstantiation(
                genericType,
                genericArgumentCount: 1,
                isValueType: false)
            .AddArgument()
            .Int32();
        return metadata.GetOrAddBlob(
            signature);
    }

    static BlobHandle AddMemorySafetyMethodSignature(
        MetadataBuilder metadata,
        bool isInstance,
        int parameterCount,
        Action<ParametersEncoder> addParameters)
    {
        var signature = new BlobBuilder();
        new BlobEncoder(signature)
            .MethodSignature(isInstanceMethod: isInstance)
            .Parameters(
                parameterCount,
                returnType => returnType.Void(),
                addParameters);
        return metadata.GetOrAddBlob(signature);
    }

    static byte[] MemorySafetyRulesBlob(int version)
    {
        var blob = new BlobBuilder();
        blob.WriteUInt16(1);
        blob.WriteInt32(version);
        blob.WriteUInt16(0);
        return blob.ToArray();
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
