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

    static byte[] BuildDuplicateLocalTypeAssembly(
        bool useMemberReference)
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            0,
            metadata.GetOrAddString(
                "DuplicateLocalTypes.dll"),
            metadata.GetOrAddGuid(Guid.NewGuid()),
            default,
            default);
        metadata.AddAssembly(
            metadata.GetOrAddString(
                "DuplicateLocalTypes"),
            new Version(1, 0, 0, 0),
            default,
            default,
            default,
            default);
        AssemblyReferenceHandle systemRuntime =
            metadata.AddAssemblyReference(
                metadata.GetOrAddString(
                    "System.Runtime"),
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
        TypeReferenceHandle task =
            metadata.AddTypeReference(
                systemRuntime,
                metadata.GetOrAddString(
                    "System.Threading.Tasks"),
                metadata.GetOrAddString("Task`1"));
        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        TypeAttributes staticType =
            TypeAttributes.Public
            | TypeAttributes.Abstract
            | TypeAttributes.Sealed;
        metadata.AddTypeDefinition(
            staticType,
            metadata.GetOrAddString("Sample"),
            metadata.GetOrAddString("Service"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        TypeDefinitionHandle secondService =
            metadata.AddTypeDefinition(
                staticType,
                metadata.GetOrAddString("Sample"),
                metadata.GetOrAddString("Service"),
                default,
                MetadataTokens.FieldDefinitionHandle(1),
                MetadataTokens.MethodDefinitionHandle(2));
        metadata.AddTypeDefinition(
            staticType,
            metadata.GetOrAddString("Sample"),
            metadata.GetOrAddString("Consumer"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(3));

        BlobHandle intSignature =
            metadata.GetOrAddBlob(
                new byte[] { 0x00, 0x00, 0x08 });
        BlobHandle taskSignature =
            metadata.GetOrAddBlob(
            new byte[]
            {
                        0x00, 0x00, 0x15, 0x12,
                        (byte)CodedIndex
                            .TypeDefOrRefOrSpec(task),
                        0x01, 0x08,
            });
        var bodies = new BlobBuilder();
        var encoder =
            new MethodBodyStreamEncoder(bodies);
        var asyncIl = new BlobBuilder();
        asyncIl.WriteByte(
            (byte)ILOpCode.Ldnull);
        asyncIl.WriteByte((byte)ILOpCode.Ret);
        int asyncBody = encoder.AddMethodBody(
            new InstructionEncoder(asyncIl),
            maxStack: 1);
        var readIl = new BlobBuilder();
        readIl.WriteByte(
            (byte)ILOpCode.Ldc_i4_1);
        readIl.WriteByte((byte)ILOpCode.Ret);
        int readBody = encoder.AddMethodBody(
            new InstructionEncoder(readIl),
            maxStack: 1);
        MethodAttributes publicStatic =
            MethodAttributes.Public
            | MethodAttributes.Static;
        metadata.AddMethodDefinition(
            publicStatic,
            MethodImplAttributes.IL,
            metadata.GetOrAddString("ReadAsync"),
            taskSignature,
            asyncBody,
            MetadataTokens.ParameterHandle(1));
        MethodDefinitionHandle read =
            metadata.AddMethodDefinition(
                publicStatic,
                MethodImplAttributes.IL,
                metadata.GetOrAddString("Read"),
                intSignature,
                readBody,
                MetadataTokens.ParameterHandle(1));
        EntityHandle readOperand = read;
        if (useMemberReference)
        {
            readOperand = metadata.AddMemberReference(
                secondService,
                metadata.GetOrAddString("Read"),
                intSignature);
        }
        var callerIl = new BlobBuilder();
        callerIl.WriteByte((byte)ILOpCode.Call);
        callerIl.WriteInt32(
            MetadataTokens.GetToken(readOperand));
        callerIl.WriteByte((byte)ILOpCode.Pop);
        callerIl.WriteByte(
            (byte)ILOpCode.Ldnull);
        callerIl.WriteByte((byte)ILOpCode.Ret);
        int callerBody = encoder.AddMethodBody(
            new InstructionEncoder(callerIl),
            maxStack: 1);
        metadata.AddMethodDefinition(
            publicStatic,
            MethodImplAttributes.IL
                | (MethodImplAttributes)0x2000,
            metadata.GetOrAddString("AnalyzeAsync"),
            taskSignature,
            callerBody,
            MetadataTokens.ParameterHandle(1));

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

    static byte[] BuildDirectionProbeDependency(
        bool byRef,
        ParameterAttributes synchronousDirection,
        ParameterAttributes asynchronousDirection,
        bool duplicateSynchronous = false,
        bool asynchronousMethodIsStatic = true,
        bool genericSignature = false,
        bool addAsynchronousGenericParameter = false,
        string synchronousName = "Read")
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            0,
            metadata.GetOrAddString(
                "DirectionProbeDependency.dll"),
            metadata.GetOrAddGuid(Guid.NewGuid()),
            default,
            default);
        metadata.AddAssembly(
            metadata.GetOrAddString(
                "DirectionProbeDependency"),
            new Version(1, 0, 0, 0),
            default,
            default,
            default,
            default);

        AssemblyReferenceHandle systemRuntime =
            AddDirectionProbeSystemRuntime(metadata);
        TypeReferenceHandle task =
            metadata.AddTypeReference(
                systemRuntime,
                metadata.GetOrAddString(
                    "System.Threading.Tasks"),
                metadata.GetOrAddString("Task"));
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
            metadata.GetOrAddString("Api"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));

        ParameterHandle readParameter =
            metadata.AddParameter(
                synchronousDirection,
                metadata.GetOrAddString("value"),
                sequenceNumber: 1);
        ParameterHandle asyncParameter =
            metadata.AddParameter(
                asynchronousDirection,
                metadata.GetOrAddString("value"),
                sequenceNumber: 1);
        ParameterHandle duplicateParameter =
            duplicateSynchronous
                ? metadata.AddParameter(
                    synchronousDirection,
                    metadata.GetOrAddString("value"),
                    sequenceNumber: 1)
                : default;
        var bodies = new BlobBuilder();
        var bodyEncoder =
            new MethodBodyStreamEncoder(bodies);
        var readIl = new BlobBuilder();
        readIl.WriteByte((byte)ILOpCode.Ret);
        int readBody = bodyEncoder.AddMethodBody(
            new InstructionEncoder(readIl),
            maxStack: 0);
        var asyncIl = new BlobBuilder();
        asyncIl.WriteByte((byte)ILOpCode.Ldnull);
        asyncIl.WriteByte((byte)ILOpCode.Ret);
        int asyncBody = bodyEncoder.AddMethodBody(
            new InstructionEncoder(asyncIl),
            maxStack: 1);
        const MethodAttributes Attributes =
            MethodAttributes.Public
            | MethodAttributes.Static;

        MethodDefinitionHandle read =
            metadata.AddMethodDefinition(
            Attributes,
            MethodImplAttributes.IL,
            metadata.GetOrAddString(synchronousName),
            AddDirectionProbeSignature(
                metadata,
                asynchronous: false,
                byRef,
                task,
                genericSignature),
            readBody,
            readParameter);
        MethodDefinitionHandle readAsync =
            metadata.AddMethodDefinition(
            asynchronousMethodIsStatic
                ? Attributes
                : MethodAttributes.Public,
            MethodImplAttributes.IL,
            metadata.GetOrAddString("ReadAsync"),
            AddDirectionProbeSignature(
                metadata,
                asynchronous: true,
                byRef,
                task,
                genericSignature),
            asyncBody,
            asyncParameter);
        if (genericSignature)
        {
            metadata.AddGenericParameter(
                read,
                GenericParameterAttributes.None,
                metadata.GetOrAddString("T"),
                index: 0);
            if (addAsynchronousGenericParameter)
            {
                metadata.AddGenericParameter(
                    readAsync,
                    GenericParameterAttributes.None,
                    metadata.GetOrAddString("T"),
                    index: 0);
            }
        }
        if (duplicateSynchronous)
        {
            metadata.AddMethodDefinition(
                Attributes,
                MethodImplAttributes.IL,
                metadata.GetOrAddString(synchronousName),
                AddDirectionProbeSignature(
                    metadata,
                    asynchronous: false,
                    byRef,
                    task,
                    genericSignature),
                readBody,
                duplicateParameter);
        }
        return SerializeDirectionProbe(
            metadata,
            bodies);
    }

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

    static byte[] BuildMvidCollisionDependency(
        Guid mvid)
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            0,
            metadata.GetOrAddString(
                "CollisionDependency.dll"),
            metadata.GetOrAddGuid(mvid),
            default,
            default);
        metadata.AddAssembly(
            metadata.GetOrAddString(
                "CollisionDependency"),
            new Version(1, 0, 0, 0),
            default,
            default,
            default,
            default);
        AssemblyReferenceHandle systemRuntime =
            AddCollisionSystemRuntime(metadata);
        TypeReferenceHandle systemObject =
            metadata.AddTypeReference(
                systemRuntime,
                metadata.GetOrAddString("System"),
                metadata.GetOrAddString("Object"));
        TypeReferenceHandle taskOfT =
            metadata.AddTypeReference(
                systemRuntime,
                metadata.GetOrAddString(
                    "System.Threading.Tasks"),
                metadata.GetOrAddString("Task`1"));
        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        TypeDefinitionHandle reader =
            metadata.AddTypeDefinition(
                TypeAttributes.Public
                    | TypeAttributes.Interface
                    | TypeAttributes.Abstract,
                metadata.GetOrAddString("Sample"),
                metadata.GetOrAddString("IReader"),
                default,
                MetadataTokens.FieldDefinitionHandle(1),
                MetadataTokens.MethodDefinitionHandle(1));
        TypeDefinitionHandle implementation =
            metadata.AddTypeDefinition(
                TypeAttributes.Public,
                metadata.GetOrAddString("Sample"),
                metadata.GetOrAddString("B"),
                systemObject,
                MetadataTokens.FieldDefinitionHandle(1),
                MetadataTokens.MethodDefinitionHandle(3));
        metadata.AddInterfaceImplementation(
            implementation,
            reader);
        BlobHandle intSignature =
            metadata.GetOrAddBlob(
                new byte[]
                {
                    0x20, 0x00, 0x08,
                });
        BlobHandle taskSignature =
            AddCollisionTaskSignature(
                metadata,
                taskOfT);
        const MethodAttributes InterfaceMethod =
            MethodAttributes.Public
            | MethodAttributes.Virtual
            | MethodAttributes.Abstract
            | MethodAttributes.NewSlot;
        metadata.AddMethodDefinition(
            InterfaceMethod,
            MethodImplAttributes.IL,
            metadata.GetOrAddString("Read"),
            intSignature,
            -1,
            MetadataTokens.ParameterHandle(1));
        metadata.AddMethodDefinition(
            InterfaceMethod,
            MethodImplAttributes.IL,
            metadata.GetOrAddString("ReadAsync"),
            taskSignature,
            -1,
            MetadataTokens.ParameterHandle(1));

        var bodies = new BlobBuilder();
        var encoder =
            new MethodBodyStreamEncoder(bodies);
        var readIl = new BlobBuilder();
        readIl.WriteByte((byte)ILOpCode.Ldc_i4_1);
        readIl.WriteByte((byte)ILOpCode.Ret);
        int readBody = encoder.AddMethodBody(
            new InstructionEncoder(readIl),
            maxStack: 1);
        var asyncIl = new BlobBuilder();
        asyncIl.WriteByte((byte)ILOpCode.Ldnull);
        asyncIl.WriteByte((byte)ILOpCode.Ret);
        int asyncBody = encoder.AddMethodBody(
            new InstructionEncoder(asyncIl),
            maxStack: 1);
        const MethodAttributes ImplementationMethod =
            MethodAttributes.Public
            | MethodAttributes.Virtual
            | MethodAttributes.NewSlot;
        metadata.AddMethodDefinition(
            ImplementationMethod,
            MethodImplAttributes.IL,
            metadata.GetOrAddString("Read"),
            intSignature,
            readBody,
            MetadataTokens.ParameterHandle(1));
        metadata.AddMethodDefinition(
            ImplementationMethod,
            MethodImplAttributes.IL,
            metadata.GetOrAddString("ReadAsync"),
            taskSignature,
            asyncBody,
            MetadataTokens.ParameterHandle(1));
        return SerializeCollisionProbe(
            metadata,
            bodies);
    }

    static byte[] BuildMvidCollisionRoot(Guid mvid)
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            0,
            metadata.GetOrAddString(
                "CollisionRoot.dll"),
            metadata.GetOrAddGuid(mvid),
            default,
            default);
        metadata.AddAssembly(
            metadata.GetOrAddString("CollisionRoot"),
            new Version(1, 0, 0, 0),
            default,
            default,
            default,
            default);
        AssemblyReferenceHandle systemRuntime =
            AddCollisionSystemRuntime(metadata);
        AssemblyReferenceHandle dependency =
            metadata.AddAssemblyReference(
                metadata.GetOrAddString(
                    "CollisionDependency"),
                new Version(1, 0, 0, 0),
                default,
                default,
                default,
                default);
        TypeReferenceHandle systemObject =
            metadata.AddTypeReference(
                systemRuntime,
                metadata.GetOrAddString("System"),
                metadata.GetOrAddString("Object"));
        TypeReferenceHandle taskOfT =
            metadata.AddTypeReference(
                systemRuntime,
                metadata.GetOrAddString(
                    "System.Threading.Tasks"),
                metadata.GetOrAddString("Task`1"));
        TypeReferenceHandle baseType =
            metadata.AddTypeReference(
                dependency,
                metadata.GetOrAddString("Sample"),
                metadata.GetOrAddString("B"));
        TypeReferenceHandle reader =
            metadata.AddTypeReference(
                dependency,
                metadata.GetOrAddString("Sample"),
                metadata.GetOrAddString("IReader"));
        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        metadata.AddTypeDefinition(
            TypeAttributes.Public,
            metadata.GetOrAddString("Sample"),
            metadata.GetOrAddString("Padding"),
            systemObject,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        metadata.AddTypeDefinition(
            TypeAttributes.Public,
            metadata.GetOrAddString("Sample"),
            metadata.GetOrAddString("C"),
            baseType,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        MemberReferenceHandle read =
            metadata.AddMemberReference(
                reader,
                metadata.GetOrAddString("Read"),
                metadata.GetOrAddBlob(
                    new byte[]
                    {
                        0x20, 0x00, 0x08,
                    }));
        var bodies = new BlobBuilder();
        var encoder =
            new MethodBodyStreamEncoder(bodies);
        var il = new BlobBuilder();
        il.WriteByte(0x02);
        il.WriteByte((byte)ILOpCode.Callvirt);
        il.WriteInt32(MetadataTokens.GetToken(read));
        il.WriteByte((byte)ILOpCode.Pop);
        il.WriteByte((byte)ILOpCode.Ldnull);
        il.WriteByte((byte)ILOpCode.Ret);
        int body = encoder.AddMethodBody(
            new InstructionEncoder(il),
            maxStack: 1);
        metadata.AddMethodDefinition(
            MethodAttributes.Public
                | MethodAttributes.Virtual,
            MethodImplAttributes.IL
                | (MethodImplAttributes)0x2000,
            metadata.GetOrAddString("ReadAsync"),
            AddCollisionTaskSignature(
                metadata,
                taskOfT),
            body,
            MetadataTokens.ParameterHandle(1));
        return SerializeCollisionProbe(
            metadata,
            bodies);
    }

    static AssemblyReferenceHandle
        AddCollisionSystemRuntime(
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

    static BlobHandle AddCollisionTaskSignature(
        MetadataBuilder metadata,
        TypeReferenceHandle taskOfT)
        => metadata.GetOrAddBlob(
            new byte[]
            {
                0x20,
                0x00,
                0x15,
                0x12,
                (byte)CodedIndex
                    .TypeDefOrRefOrSpec(taskOfT),
                0x01,
                0x08,
            });

    static byte[] SerializeCollisionProbe(
        MetadataBuilder metadata,
        BlobBuilder bodies)
    {
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

    sealed class DirectionProbeResolver
        : IAssemblyReferenceResolver
    {
        readonly ResolvedAssemblyReference _dependency;

        public DirectionProbeResolver(byte[] image)
        {
            _dependency =
                ResolvedAssemblyReference.Create(
                    new AssemblyReferenceIdentity(
                        "DirectionProbeDependency",
                        new Version(1, 0, 0, 0),
                        null,
                        null),
                    path: null,
                    () => new MemoryStream(
                        image,
                        writable: false),
                    AssemblyResolutionProvenance.Local(
                        "async sibling direction probe"));
        }

        public ResolvedAssemblyReference? Resolve(
            AssemblyReferenceIdentity identity,
            AssemblyResolutionScope scope)
            => identity.Name.Equals(
                    "DirectionProbeDependency",
                    StringComparison.OrdinalIgnoreCase)
                ? _dependency
                : null;
    }

    sealed class MvidCollisionResolver
        : IAssemblyReferenceResolver
    {
        readonly ResolvedAssemblyReference _dependency;

        public MvidCollisionResolver(byte[] image)
        {
            _dependency =
                ResolvedAssemblyReference.Create(
                    new AssemblyReferenceIdentity(
                        "CollisionDependency",
                        new Version(1, 0, 0, 0),
                        null,
                        null),
                    path: null,
                    () => new MemoryStream(
                        image,
                        writable: false),
                    AssemblyResolutionProvenance.Local(
                        "async sibling MVID collision probe"));
        }

        public ResolvedAssemblyReference? Resolve(
            AssemblyReferenceIdentity identity,
            AssemblyResolutionScope scope)
            => identity.Name.Equals(
                    "CollisionDependency",
                    StringComparison.OrdinalIgnoreCase)
                ? _dependency
                : null;
    }

}
