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

    static byte[] BuildDirectionProbeCaller(
        bool byRef,
        bool genericSignature = false,
        bool addStateMachineAttribute = false)
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            0,
            metadata.GetOrAddString(
                "DirectionProbeCaller.dll"),
            metadata.GetOrAddGuid(Guid.NewGuid()),
            default,
            default);
        metadata.AddAssembly(
            metadata.GetOrAddString(
                "DirectionProbeCaller"),
            new Version(1, 0, 0, 0),
            default,
            default,
            default,
            default);
        AssemblyReferenceHandle systemRuntime =
            AddDirectionProbeSystemRuntime(metadata);
        AssemblyReferenceHandle dependency =
            metadata.AddAssemblyReference(
                metadata.GetOrAddString(
                    "DirectionProbeDependency"),
                new Version(1, 0, 0, 0),
                default,
                default,
                default,
                default);
        TypeReferenceHandle task =
            metadata.AddTypeReference(
                systemRuntime,
                metadata.GetOrAddString(
                    "System.Threading.Tasks"),
                metadata.GetOrAddString("Task"));
        TypeReferenceHandle api =
            metadata.AddTypeReference(
                dependency,
                metadata.GetOrAddString("Probe"),
                metadata.GetOrAddString("Api"));
        TypeReferenceHandle systemType =
            metadata.AddTypeReference(
                systemRuntime,
                metadata.GetOrAddString("System"),
                metadata.GetOrAddString("Type"));
        TypeReferenceHandle asyncStateMachineAttribute =
            metadata.AddTypeReference(
                systemRuntime,
                metadata.GetOrAddString(
                    "System.Runtime.CompilerServices"),
                metadata.GetOrAddString(
                    "AsyncStateMachineAttribute"));
        TypeReferenceHandle asyncStateMachine =
            metadata.AddTypeReference(
                systemRuntime,
                metadata.GetOrAddString(
                    "System.Runtime.CompilerServices"),
                metadata.GetOrAddString(
                    "IAsyncStateMachine"));
        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        metadata.AddTypeDefinition(
            TypeAttributes.Public
                | TypeAttributes.Abstract
                | TypeAttributes.Sealed,
            metadata.GetOrAddString("Probe"),
            metadata.GetOrAddString("Consumer"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        TypeDefinitionHandle stateMachineType =
            metadata.AddTypeDefinition(
                TypeAttributes.NotPublic
                    | TypeAttributes.Sealed,
                metadata.GetOrAddString("Probe"),
                metadata.GetOrAddString("StateMachine"),
                default,
                MetadataTokens.FieldDefinitionHandle(1),
                MetadataTokens.MethodDefinitionHandle(2));
        metadata.AddInterfaceImplementation(
            stateMachineType,
            asyncStateMachine);
        MemberReferenceHandle readReference =
            metadata.AddMemberReference(
                api,
                metadata.GetOrAddString("Read"),
                AddDirectionProbeSignature(
                    metadata,
                    asynchronous: false,
                    byRef,
                    task,
                    genericSignature));
        EntityHandle read = readReference;
        if (genericSignature)
        {
            read = metadata.AddMethodSpecification(
                readReference,
                metadata.GetOrAddBlob(
                    new byte[]
                    {
                        0x0A, 0x01, 0x08,
                    }));
        }

        var bodies = new BlobBuilder();
        var bodyEncoder =
            new MethodBodyStreamEncoder(bodies);
        var il = new BlobBuilder();
        StandaloneSignatureHandle localSignature =
            default;
        if (byRef)
        {
            localSignature =
                metadata.AddStandaloneSignature(
                    metadata.GetOrAddBlob(
                        new byte[]
                        {
                            0x07, 0x01, 0x08,
                        }));
            il.WriteByte((byte)ILOpCode.Ldloca_s);
            il.WriteByte(0);
        }
        else
        {
            il.WriteByte((byte)ILOpCode.Ldc_i4_0);
        }
        il.WriteByte((byte)ILOpCode.Call);
        il.WriteInt32(MetadataTokens.GetToken(read));
        il.WriteByte((byte)ILOpCode.Ldnull);
        il.WriteByte((byte)ILOpCode.Ret);
        int body = byRef
            ? bodyEncoder.AddMethodBody(
                new InstructionEncoder(il),
                maxStack: 1,
                localSignature,
                MethodBodyAttributes.InitLocals)
            : bodyEncoder.AddMethodBody(
                new InstructionEncoder(il),
                maxStack: 1);
        MethodDefinitionHandle analyze =
            metadata.AddMethodDefinition(
            MethodAttributes.Public
                | MethodAttributes.Static,
            MethodImplAttributes.IL
                | (MethodImplAttributes)0x2000,
            metadata.GetOrAddString("AnalyzeAsync"),
            metadata.GetOrAddBlob(
                new byte[]
                {
                    0x00,
                    0x00,
                    0x12,
                    (byte)CodedIndex
                        .TypeDefOrRefOrSpec(task),
                }),
            body,
            MetadataTokens.ParameterHandle(1));
        if (addStateMachineAttribute)
        {
            var moveNextIl = new BlobBuilder();
            moveNextIl.WriteByte((byte)ILOpCode.Ldc_i4_0);
            moveNextIl.WriteByte((byte)ILOpCode.Call);
            moveNextIl.WriteInt32(
                MetadataTokens.GetToken(read));
            moveNextIl.WriteByte((byte)ILOpCode.Ret);
            metadata.AddMethodDefinition(
                MethodAttributes.Public
                    | MethodAttributes.Virtual,
                MethodImplAttributes.IL,
                metadata.GetOrAddString("MoveNext"),
                metadata.GetOrAddBlob(
                    new byte[] { 0x20, 0x00, 0x01 }),
                bodyEncoder.AddMethodBody(
                    new InstructionEncoder(moveNextIl),
                    maxStack: 1),
                MetadataTokens.ParameterHandle(1));
            MemberReferenceHandle constructor =
                metadata.AddMemberReference(
                    asyncStateMachineAttribute,
                    metadata.GetOrAddString(".ctor"),
                    metadata.GetOrAddBlob(
                        new byte[]
                        {
                            0x20, 0x01, 0x01, 0x12,
                            (byte)CodedIndex
                                .TypeDefOrRefOrSpec(systemType),
                        }));
            metadata.AddCustomAttribute(
                analyze,
                constructor,
                AddSerializedTypeAttributeValue(
                    metadata,
                    "Probe.StateMachine, DirectionProbeCaller"));
        }
        return SerializeDirectionProbe(
            metadata,
            bodies);
    }

    static BlobHandle AddSerializedTypeAttributeValue(
        MetadataBuilder metadata,
        string serializedType)
    {
        var value = new BlobBuilder();
        value.WriteUInt16(0x0001);
        value.WriteSerializedString(serializedType);
        value.WriteUInt16(0);
        return metadata.GetOrAddBlob(value);
    }

    static AssemblyReferenceHandle
        AddDirectionProbeSystemRuntime(
            MetadataBuilder metadata)
        => metadata.AddAssemblyReference(
            metadata.GetOrAddString("System.Runtime"),
            new Version(11, 0, 0, 0),
            default,
            metadata.GetOrAddBlob(
                new byte[]
                {
                    0xB0, 0x3F, 0x5F, 0x7F,
                    0x11, 0xD5, 0x0A, 0x3A,
                }),
            default,
            default);

    static BlobHandle AddDirectionProbeSignature(
        MetadataBuilder metadata,
        bool asynchronous,
        bool byRef,
        TypeReferenceHandle task,
        bool generic = false)
    {
        var signature = new BlobBuilder();
        signature.WriteByte(
            generic ? (byte)0x10 : (byte)0x00);
        if (generic)
            signature.WriteByte(0x01);
        signature.WriteByte(0x01);
        if (asynchronous)
        {
            signature.WriteByte(0x12);
            signature.WriteByte(
                (byte)CodedIndex
                    .TypeDefOrRefOrSpec(task));
        }
        else
        {
            signature.WriteByte(0x01);
        }
        if (byRef)
            signature.WriteByte(0x10);
        signature.WriteByte(0x08);
        return metadata.GetOrAddBlob(signature);
    }

    static byte[] SerializeDirectionProbe(
        MetadataBuilder metadata,
        BlobBuilder bodies)
    {
        var pe = new ManagedPEBuilder(
            PEHeaderBuilder.CreateLibraryHeader(),
            new MetadataRootBuilder(metadata),
            bodies,
            flags: CorFlags.ILOnly);
        var image = new BlobBuilder();
        pe.Serialize(image);
        return image.ToArray();
    }

    static void AddAsyncStateMachineAttribute(
        MetadataBuilder metadata,
        MethodDefinitionHandle method,
        MemberReferenceHandle constructor,
        string stateMachineType)
    {
        var value = new BlobBuilder();
        value.WriteUInt16(0x0001);
        value.WriteSerializedString(stateMachineType);
        value.WriteUInt16(0);
        metadata.AddCustomAttribute(
            method,
            constructor,
            metadata.GetOrAddBlob(value));
    }

    /// <summary>Emits a minimal unsigned assembly whose only content is what <paramref name="addContent"/> adds.</summary>
    static byte[] EmitAssembly(
        string name,
        Action<MetadataBuilder> addContent,
        Guid? moduleVersionId = null)
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            generation: 0,
            metadata.GetOrAddString(name + ".dll"),
            metadata.GetOrAddGuid(moduleVersionId ?? Guid.NewGuid()),
            default,
            default);
        metadata.AddAssembly(
            metadata.GetOrAddString(name),
            new Version(1, 0, 0, 0),
            culture: default,
            publicKey: default,
            flags: default,
            hashAlgorithm: System.Reflection.AssemblyHashAlgorithm.Sha1);

        // Row 1 is always <Module>, exactly as a compiler emits.
        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            baseType: default,
            fieldList: MetadataTokens.FieldDefinitionHandle(1),
            methodList: MetadataTokens.MethodDefinitionHandle(1));

        addContent(metadata);

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

    static void ReplaceAscii(
        byte[] image,
        string oldValue,
        string newValue,
        int expectedReplacements)
    {
        Assert.Equal(oldValue.Length, newValue.Length);
        byte[] oldBytes = System.Text.Encoding.ASCII.GetBytes(oldValue);
        byte[] newBytes = System.Text.Encoding.ASCII.GetBytes(newValue);
        int replacements = 0;
        for (int i = 0; i <= image.Length - oldBytes.Length; i++)
        {
            if (!image.AsSpan(i, oldBytes.Length).SequenceEqual(oldBytes))
                continue;

            newBytes.CopyTo(image.AsSpan(i, newBytes.Length));
            replacements++;
        }

        Assert.Equal(expectedReplacements, replacements);
    }

}
