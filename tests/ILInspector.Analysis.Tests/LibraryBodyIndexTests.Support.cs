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

    static void AssertCompilerPositiveSuppressedByCensus(
        LibraryBodyIndex index)
    {
        MethodIdentity compilerPositive = Assert.Single(
            index.DeclaredMethods,
            method => method.DeclaringType.Name
                    == nameof(ClassicAsyncSiblingFixture)
                && method.Name == nameof(
                    ClassicAsyncSiblingFixture
                        .ReturnsCallStoredBeforeAwait));
        Assert.DoesNotContain(
            index.ResultSinks,
            sink => sink.Caller == compilerPositive
                && sink.StateMachineFieldSource is not null);
    }

    static int MethodHeaderSize(
        byte[] image,
        int bodyOffset)
    {
        byte first = image[bodyOffset];
        if ((first & 0x3) == 0x2)
            return 1;
        ushort flagsAndSize =
            BinaryPrimitives.ReadUInt16LittleEndian(
                image.AsSpan(
                    bodyOffset,
                    sizeof(ushort)));
        return (flagsAndSize >> 12) * 4;
    }

    static int RvaToFileOffset(
        PEHeaders headers,
        int rva)
    {
        foreach (SectionHeader section
            in headers.SectionHeaders)
        {
            int size = Math.Max(
                section.VirtualSize,
                section.SizeOfRawData);
            if (rva >= section.VirtualAddress
                && rva < section.VirtualAddress + size)
            {
                return section.PointerToRawData
                    + rva
                    - section.VirtualAddress;
            }
        }

        throw new InvalidOperationException(
            $"RVA 0x{rva:X8} was not found.");
    }

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

    static byte[] BuildMethodImplAsyncSourceAssembly(
        bool includeMethodImpl,
        byte siblingSignatureHeader = 0x20,
        bool methodImplBodyAsMemberReference = false,
        bool finalInterfaceSibling = false,
        string sourceMethodName = "AnalyzeAsync",
        byte attributeConstructorHeader = 0x20,
        bool inheritedMethodImpl = false,
        bool validStateMachine = true,
        bool sourceStartsNewSlot = false,
        string? inheritedMethodImplBodyName = null,
        bool sourceHasBody = true,
        bool unrelatedSourceMethodImpl = false,
        bool runtimeAsyncSource = false,
        bool malformedSourceMethodImpl = false,
        bool sourceImplementsOtherInterface = true,
        bool incompatibleSourceMethodImpl = false,
        bool moveNextHasBody = true,
        MethodImplAttributes moveNextImplementation =
            MethodImplAttributes.IL,
        MethodImplAttributes sourceImplementation =
            MethodImplAttributes.IL,
        bool stateMachineUsesTasksContract = false,
        bool duplicateSourceGeneratedSource = false)
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            0,
            metadata.GetOrAddString("MethodImplAsyncSource.dll"),
            metadata.GetOrAddGuid(Guid.NewGuid()),
            default,
            default);
        metadata.AddAssembly(
            metadata.GetOrAddString("MethodImplAsyncSource"),
            new Version(1, 0, 0, 0),
            default,
            default,
            default,
            default);

        AssemblyReferenceHandle systemRuntime =
            metadata.AddAssemblyReference(
                metadata.GetOrAddString("System.Runtime"),
                new Version(11, 0, 0, 0),
                default,
                metadata.GetOrAddBlob(
                    new byte[]
                    {
                        0xB0,
                        0x3F,
                        0x5F,
                        0x7F,
                        0x11,
                        0xD5,
                        0x0A,
                        0x3A,
                    }),
                default,
                default);
        AssemblyReferenceHandle systemThreadingTasks =
            metadata.AddAssemblyReference(
                metadata.GetOrAddString(
                    "System.Threading.Tasks"),
                new Version(4, 0, 0, 0),
                default,
                metadata.GetOrAddBlob(
                    new byte[]
                    {
                        0xB0,
                        0x3F,
                        0x5F,
                        0x7F,
                        0x11,
                        0xD5,
                        0x0A,
                        0x3A,
                    }),
                default,
                default);
        TypeReferenceHandle asyncStateMachineAttribute =
            metadata.AddTypeReference(
                systemRuntime,
                metadata.GetOrAddString(
                    "System.Runtime.CompilerServices"),
                metadata.GetOrAddString(
                    "AsyncStateMachineAttribute"));
        TypeReferenceHandle compilerGeneratedAttribute =
            duplicateSourceGeneratedSource
                ? metadata.AddTypeReference(
                    systemRuntime,
                    metadata.GetOrAddString(
                        "System.Runtime.CompilerServices"),
                    metadata.GetOrAddString(
                        "CompilerGeneratedAttribute"))
                : default;
        TypeReferenceHandle generatedCodeAttribute =
            duplicateSourceGeneratedSource
                ? metadata.AddTypeReference(
                    systemRuntime,
                    metadata.GetOrAddString(
                        "System.CodeDom.Compiler"),
                    metadata.GetOrAddString(
                        "GeneratedCodeAttribute"))
                : default;
        TypeReferenceHandle asyncStateMachine =
            metadata.AddTypeReference(
                stateMachineUsesTasksContract
                    ? systemThreadingTasks
                    : systemRuntime,
                metadata.GetOrAddString(
                    "System.Runtime.CompilerServices"),
                metadata.GetOrAddString("IAsyncStateMachine"));
        TypeReferenceHandle systemType =
            metadata.AddTypeReference(
                systemRuntime,
                metadata.GetOrAddString("System"),
                metadata.GetOrAddString("Type"));
        TypeReferenceHandle task =
            metadata.AddTypeReference(
                stateMachineUsesTasksContract
                    ? systemThreadingTasks
                    : systemRuntime,
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
        TypeDefinitionHandle interfaceType =
            metadata.AddTypeDefinition(
                TypeAttributes.Public
                    | TypeAttributes.Interface
                    | TypeAttributes.Abstract,
                metadata.GetOrAddString("Sample"),
                metadata.GetOrAddString("IReader"),
                default,
                MetadataTokens.FieldDefinitionHandle(1),
                MetadataTokens.MethodDefinitionHandle(1));
        TypeDefinitionHandle baseType =
            inheritedMethodImpl
                ? metadata.AddTypeDefinition(
                    TypeAttributes.Public,
                    metadata.GetOrAddString("Sample"),
                    metadata.GetOrAddString("ReaderBase"),
                    default,
                    MetadataTokens.FieldDefinitionHandle(1),
                    MetadataTokens.MethodDefinitionHandle(3))
                : default;
        TypeDefinitionHandle sourceType =
            metadata.AddTypeDefinition(
                TypeAttributes.Public
                    | (!sourceHasBody
                        ? TypeAttributes.Abstract
                        : 0),
                metadata.GetOrAddString("Sample"),
                metadata.GetOrAddString("Reader"),
                baseType,
                MetadataTokens.FieldDefinitionHandle(1),
                MetadataTokens.MethodDefinitionHandle(
                    inheritedMethodImpl ? 4 : 3));
        int extraSourceMethods =
            duplicateSourceGeneratedSource ? 1 : 0;
        TypeDefinitionHandle generatedSourceType =
            duplicateSourceGeneratedSource
                ? metadata.AddTypeDefinition(
                    TypeAttributes.NestedPrivate
                        | TypeAttributes.Sealed,
                    default,
                    metadata.GetOrAddString("<>c"),
                    default,
                    MetadataTokens.FieldDefinitionHandle(1),
                    MetadataTokens.MethodDefinitionHandle(
                        inheritedMethodImpl ? 5 : 4))
                : default;
        TypeDefinitionHandle stateMachineType =
            metadata.AddTypeDefinition(
                TypeAttributes.NestedPrivate
                    | TypeAttributes.Sealed,
                default,
                metadata.GetOrAddString(
                    "<AnalyzeAsync>d__0"),
                default,
                MetadataTokens.FieldDefinitionHandle(1),
                MetadataTokens.MethodDefinitionHandle(
                    (inheritedMethodImpl ? 5 : 4)
                        + extraSourceMethods));
        TypeDefinitionHandle otherInterfaceType =
            metadata.AddTypeDefinition(
                TypeAttributes.Public
                    | TypeAttributes.Interface
                    | TypeAttributes.Abstract,
                metadata.GetOrAddString("Sample"),
                metadata.GetOrAddString("IOther"),
                default,
                MetadataTokens.FieldDefinitionHandle(1),
                MetadataTokens.MethodDefinitionHandle(
                    (inheritedMethodImpl ? 6 : 5)
                        + extraSourceMethods));
        metadata.AddNestedType(stateMachineType, sourceType);
        if (!generatedSourceType.IsNil)
        {
            metadata.AddNestedType(
                generatedSourceType,
                sourceType);
        }
        metadata.AddInterfaceImplementation(
            inheritedMethodImpl
                ? baseType
                : sourceType,
            interfaceType);
        if (sourceImplementsOtherInterface)
        {
            metadata.AddInterfaceImplementation(
                sourceType,
                otherInterfaceType);
        }
        if (validStateMachine)
        {
            metadata.AddInterfaceImplementation(
                stateMachineType,
                asyncStateMachine);
        }

        BlobHandle voidSignature = metadata.GetOrAddBlob(
            new byte[]
            {
                siblingSignatureHeader,
                0x00,
                0x01,
            });
        BlobHandle taskSignature = metadata.GetOrAddBlob(
            new byte[]
            {
                siblingSignatureHeader,
                0x00,
                0x12,
                (byte)CodedIndex.TypeDefOrRefOrSpec(task),
            });
        BlobHandle taskIntSignature =
            metadata.GetOrAddBlob(
                new byte[]
                {
                    siblingSignatureHeader,
                    0x01,
                    0x12,
                    (byte)CodedIndex.TypeDefOrRefOrSpec(
                        task),
                    0x08,
                });
        MethodAttributes interfaceMethod =
            MethodAttributes.Public
            | MethodAttributes.Virtual
            | MethodAttributes.Abstract
            | MethodAttributes.NewSlot;
        MethodDefinitionHandle read =
            metadata.AddMethodDefinition(
                interfaceMethod,
                MethodImplAttributes.IL,
                metadata.GetOrAddString("Read"),
                voidSignature,
                -1,
                MetadataTokens.ParameterHandle(1));
        MethodDefinitionHandle readAsync =
            metadata.AddMethodDefinition(
                interfaceMethod
                    | (finalInterfaceSibling
                        ? MethodAttributes.Final
                        : 0),
                MethodImplAttributes.IL,
                metadata.GetOrAddString("ReadAsync"),
                taskSignature,
                -1,
                MetadataTokens.ParameterHandle(1));

        var bodies = new BlobBuilder();
        var bodyEncoder = new MethodBodyStreamEncoder(bodies);
        var sourceIl = new BlobBuilder();
        sourceIl.WriteByte((byte)ILOpCode.Ldnull);
        sourceIl.WriteByte((byte)ILOpCode.Ret);
        int sourceBody = bodyEncoder.AddMethodBody(
            new InstructionEncoder(sourceIl),
            maxStack: 1);
        MethodDefinitionHandle methodImplBody = default;
        if (inheritedMethodImpl)
        {
            methodImplBody =
                metadata.AddMethodDefinition(
                    MethodAttributes.Public
                        | MethodAttributes.Virtual
                        | MethodAttributes.NewSlot,
                    MethodImplAttributes.IL,
                    metadata.GetOrAddString(
                        inheritedMethodImplBodyName
                            ?? sourceMethodName),
                    taskSignature,
                    sourceBody,
                    MetadataTokens.ParameterHandle(1));
        }
        MethodDefinitionHandle source =
            metadata.AddMethodDefinition(
                MethodAttributes.Public
                    | MethodAttributes.Virtual
                    | (!sourceHasBody
                        ? MethodAttributes.Abstract
                        : 0)
                    | (inheritedMethodImpl
                        ? sourceStartsNewSlot
                            ? MethodAttributes.NewSlot
                            : 0
                        : MethodAttributes.Final
                            | MethodAttributes.NewSlot),
                sourceImplementation
                    | (runtimeAsyncSource
                        ? (MethodImplAttributes)0x2000
                        : 0),
                metadata.GetOrAddString(sourceMethodName),
                taskSignature,
                sourceHasBody ? sourceBody : -1,
                MetadataTokens.ParameterHandle(1));
        MethodDefinitionHandle generatedSource = default;
        if (duplicateSourceGeneratedSource)
        {
            generatedSource = metadata.AddMethodDefinition(
                MethodAttributes.Private
                    | MethodAttributes.HideBySig,
                sourceImplementation,
                metadata.GetOrAddString(
                    $"<{sourceMethodName}>g__Generated|0_0"),
                taskSignature,
                sourceBody,
                MetadataTokens.ParameterHandle(1));
        }

        var moveNextIl = new BlobBuilder();
        moveNextIl.WriteByte((byte)ILOpCode.Ldnull);
        moveNextIl.WriteByte((byte)ILOpCode.Callvirt);
        moveNextIl.WriteInt32(MetadataTokens.GetToken(read));
        moveNextIl.WriteByte((byte)ILOpCode.Ret);
        int moveNextBody = bodyEncoder.AddMethodBody(
            new InstructionEncoder(moveNextIl),
            maxStack: 1);
        metadata.AddMethodDefinition(
            MethodAttributes.Public
                | MethodAttributes.Virtual
                | (!moveNextHasBody
                    ? MethodAttributes.Abstract
                    : 0),
            moveNextImplementation,
            metadata.GetOrAddString("MoveNext"),
            metadata.GetOrAddBlob(
                new byte[] { 0x20, 0x00, 0x01 }),
            moveNextHasBody ? moveNextBody : -1,
            MetadataTokens.ParameterHandle(1));
        MethodDefinitionHandle otherAsync =
            metadata.AddMethodDefinition(
                interfaceMethod,
                MethodImplAttributes.IL,
                metadata.GetOrAddString("OtherAsync"),
                incompatibleSourceMethodImpl
                    ? taskIntSignature
                    : taskSignature,
                -1,
                MetadataTokens.ParameterHandle(1));
        if (includeMethodImpl)
        {
            EntityHandle methodBody =
                inheritedMethodImpl
                    ? methodImplBody
                    : source;
            if (methodImplBodyAsMemberReference)
            {
                methodBody =
                    metadata.AddMemberReference(
                        inheritedMethodImpl
                            ? baseType
                            : sourceType,
                        metadata.GetOrAddString(
                            sourceMethodName),
                        taskSignature);
            }
            metadata.AddMethodImplementation(
                inheritedMethodImpl
                    ? baseType
                    : sourceType,
                methodBody,
                readAsync);
        }
        if (unrelatedSourceMethodImpl)
        {
            metadata.AddMethodImplementation(
                sourceType,
                source,
                otherAsync);
        }
        if (malformedSourceMethodImpl)
        {
            MemberReferenceHandle missingDeclaration =
                metadata.AddMemberReference(
                    otherInterfaceType,
                    metadata.GetOrAddString(
                        "MissingAsync"),
                    taskSignature);
            metadata.AddMethodImplementation(
                sourceType,
                source,
                missingDeclaration);
        }

        MemberReferenceHandle attributeConstructor =
            metadata.AddMemberReference(
                asyncStateMachineAttribute,
                metadata.GetOrAddString(".ctor"),
                metadata.GetOrAddBlob(
                    new byte[]
                    {
                        attributeConstructorHeader,
                        0x01,
                        0x01,
                        0x12,
                        (byte)CodedIndex.TypeDefOrRefOrSpec(
                            systemType),
                    }));
        AddAsyncStateMachineAttribute(
            metadata,
            source,
            attributeConstructor,
            "Sample.Reader+<AnalyzeAsync>d__0, MethodImplAsyncSource");
        if (!generatedSource.IsNil)
        {
            MemberReferenceHandle
                compilerGeneratedConstructor =
                    metadata.AddMemberReference(
                        compilerGeneratedAttribute,
                        metadata.GetOrAddString(".ctor"),
                        metadata.GetOrAddBlob(
                            new byte[]
                            {
                                0x20, 0x00, 0x01,
                            }));
            MemberReferenceHandle generatedCodeConstructor =
                metadata.AddMemberReference(
                    generatedCodeAttribute,
                    metadata.GetOrAddString(".ctor"),
                    metadata.GetOrAddBlob(
                        new byte[]
                        {
                            0x20, 0x02, 0x01, 0x0E,
                            0x0E,
                        }));
            AddAsyncStateMachineAttribute(
                metadata,
                generatedSource,
                attributeConstructor,
                "Sample.Reader+<AnalyzeAsync>d__0, MethodImplAsyncSource");
            metadata.AddCustomAttribute(
                generatedSource,
                compilerGeneratedConstructor,
                metadata.GetOrAddBlob(
                    new byte[] { 0x01, 0x00, 0x00, 0x00 }));
            metadata.AddCustomAttribute(
                generatedSourceType,
                compilerGeneratedConstructor,
                metadata.GetOrAddBlob(
                    new byte[] { 0x01, 0x00, 0x00, 0x00 }));
            var generatedCodeValue = new BlobBuilder();
            generatedCodeValue.WriteUInt16(0x0001);
            generatedCodeValue.WriteSerializedString("test");
            generatedCodeValue.WriteSerializedString("1.0");
            generatedCodeValue.WriteUInt16(0);
            metadata.AddCustomAttribute(
                generatedSourceType,
                generatedCodeConstructor,
                metadata.GetOrAddBlob(generatedCodeValue));
        }

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

    public enum IteratorOwnershipProbe
    {
        ExplicitMoveNextDecoy,
        DuplicateOwners,
        AsyncIteratorWrongKind,
    }

    static byte[] BuildIteratorOwnershipAssembly(
        IteratorOwnershipProbe probe)
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            0,
            metadata.GetOrAddString(
                "IteratorOwnership.dll"),
            metadata.GetOrAddGuid(Guid.NewGuid()),
            default,
            default);
        metadata.AddAssembly(
            metadata.GetOrAddString("IteratorOwnership"),
            new Version(1, 0, 0, 0),
            default,
            default,
            default,
            default);
        AssemblyReferenceHandle systemRuntime =
            metadata.AddAssemblyReference(
                metadata.GetOrAddString("System.Runtime"),
                new Version(11, 0, 0, 0),
                default,
                metadata.GetOrAddBlob(
                    new byte[]
                    {
                        0xB0,
                        0x3F,
                        0x5F,
                        0x7F,
                        0x11,
                        0xD5,
                        0x0A,
                        0x3A,
                    }),
                default,
                default);
        bool asyncIterator =
            probe
                == IteratorOwnershipProbe
                    .AsyncIteratorWrongKind;
        TypeReferenceHandle stateMachineAttribute =
            metadata.AddTypeReference(
                systemRuntime,
                metadata.GetOrAddString(
                    "System.Runtime.CompilerServices"),
                metadata.GetOrAddString(
                    asyncIterator
                        ? "AsyncIteratorStateMachineAttribute"
                        : "IteratorStateMachineAttribute"));
        TypeReferenceHandle stateMachineInterface =
            metadata.AddTypeReference(
                systemRuntime,
                metadata.GetOrAddString(
                    asyncIterator
                        ? "System.Runtime.CompilerServices"
                        : "System.Collections"),
                metadata.GetOrAddString(
                    asyncIterator
                        ? "IAsyncStateMachine"
                        : "IEnumerator"));
        TypeReferenceHandle systemType =
            metadata.AddTypeReference(
                systemRuntime,
                metadata.GetOrAddString("System"),
                metadata.GetOrAddString("Type"));

        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        TypeDefinitionHandle sourceType =
            metadata.AddTypeDefinition(
                TypeAttributes.Public
                    | TypeAttributes.Abstract
                    | TypeAttributes.Sealed,
                metadata.GetOrAddString("Sample"),
                metadata.GetOrAddString("Source"),
                default,
                MetadataTokens.FieldDefinitionHandle(1),
                MetadataTokens.MethodDefinitionHandle(1));
        TypeDefinitionHandle stateMachineType =
            metadata.AddTypeDefinition(
                TypeAttributes.NestedPrivate
                    | TypeAttributes.Sealed,
                default,
                metadata.GetOrAddString(
                    "<OwnerA>d__0"),
                default,
                MetadataTokens.FieldDefinitionHandle(1),
                MetadataTokens.MethodDefinitionHandle(4));
        metadata.AddNestedType(
            stateMachineType,
            sourceType);
        metadata.AddInterfaceImplementation(
            stateMachineType,
            stateMachineInterface);

        var bodies = new BlobBuilder();
        var bodyEncoder =
            new MethodBodyStreamEncoder(bodies);
        static int AddRetBody(
            MethodBodyStreamEncoder encoder)
        {
            var il = new BlobBuilder();
            var instructions =
                new InstructionEncoder(il);
            instructions.OpCode(ILOpCode.Ret);
            return encoder.AddMethodBody(
                instructions,
                maxStack: 0);
        }
        BlobHandle staticVoidSignature =
            metadata.GetOrAddBlob(
                new byte[] { 0x00, 0x00, 0x01 });
        BlobHandle moveNextSignature =
            metadata.GetOrAddBlob(
                asyncIterator
                    ? new byte[] { 0x20, 0x00, 0x01 }
                    : new byte[] { 0x20, 0x00, 0x02 });
        MethodDefinitionHandle ownerA =
            metadata.AddMethodDefinition(
                MethodAttributes.Public
                    | MethodAttributes.Static,
                MethodImplAttributes.IL,
                metadata.GetOrAddString("OwnerA"),
                staticVoidSignature,
                AddRetBody(bodyEncoder),
                MetadataTokens.ParameterHandle(1));
        MethodDefinitionHandle ownerB =
            metadata.AddMethodDefinition(
                MethodAttributes.Public
                    | MethodAttributes.Static,
                MethodImplAttributes.IL,
                metadata.GetOrAddString("OwnerB"),
                staticVoidSignature,
                AddRetBody(bodyEncoder),
                MetadataTokens.ParameterHandle(1));
        MethodDefinitionHandle local =
            metadata.AddMethodDefinition(
                MethodAttributes.Private
                    | MethodAttributes.Static,
                MethodImplAttributes.IL,
                metadata.GetOrAddString(
                    "<OwnerA>g__Local|0_0"),
                staticVoidSignature,
                AddRetBody(bodyEncoder),
                MetadataTokens.ParameterHandle(1));

        var actualIl = new BlobBuilder();
        if (probe
            != IteratorOwnershipProbe
                .ExplicitMoveNextDecoy)
        {
            actualIl.WriteByte((byte)ILOpCode.Call);
            actualIl.WriteInt32(
                MetadataTokens.GetToken(local));
        }
        if (!asyncIterator)
            actualIl.WriteByte((byte)ILOpCode.Ldc_i4_0);
        actualIl.WriteByte((byte)ILOpCode.Ret);
        MethodDefinitionHandle actualMoveNext =
            metadata.AddMethodDefinition(
                MethodAttributes.Private
                    | MethodAttributes.Virtual,
                MethodImplAttributes.IL,
                metadata.GetOrAddString(
                    asyncIterator
                        ? "MoveNext"
                        : "ActualMoveNext"),
                moveNextSignature,
                bodyEncoder.AddMethodBody(
                    new InstructionEncoder(actualIl),
                    maxStack: 1),
                MetadataTokens.ParameterHandle(1));

        if (probe
            == IteratorOwnershipProbe
                .ExplicitMoveNextDecoy)
        {
            var decoyIl = new BlobBuilder();
            decoyIl.WriteByte((byte)ILOpCode.Call);
            decoyIl.WriteInt32(
                MetadataTokens.GetToken(local));
            decoyIl.WriteByte(
                (byte)ILOpCode.Ldc_i4_0);
            decoyIl.WriteByte((byte)ILOpCode.Ret);
            metadata.AddMethodDefinition(
                MethodAttributes.Public
                    | MethodAttributes.Virtual,
                MethodImplAttributes.IL,
                metadata.GetOrAddString("MoveNext"),
                moveNextSignature,
                bodyEncoder.AddMethodBody(
                    new InstructionEncoder(decoyIl),
                    maxStack: 1),
                MetadataTokens.ParameterHandle(1));
        }

        if (!asyncIterator)
        {
            MemberReferenceHandle declaration =
                metadata.AddMemberReference(
                    stateMachineInterface,
                    metadata.GetOrAddString("MoveNext"),
                    moveNextSignature);
            metadata.AddMethodImplementation(
                stateMachineType,
                actualMoveNext,
                declaration);
        }

        MemberReferenceHandle constructor =
            metadata.AddMemberReference(
                stateMachineAttribute,
                metadata.GetOrAddString(".ctor"),
                metadata.GetOrAddBlob(
                    new byte[]
                    {
                        0x20,
                        0x01,
                        0x01,
                        0x12,
                        (byte)CodedIndex
                            .TypeDefOrRefOrSpec(
                                systemType),
                    }));
        const string SerializedStateMachine =
            "Sample.Source+<OwnerA>d__0, IteratorOwnership";
        AddAsyncStateMachineAttribute(
            metadata,
            ownerA,
            constructor,
            SerializedStateMachine);
        if (probe
            == IteratorOwnershipProbe.DuplicateOwners)
        {
            AddAsyncStateMachineAttribute(
                metadata,
                ownerB,
                constructor,
                SerializedStateMachine);
        }

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

    static byte[] BuildMalformedAsyncSourceAssembly(
        bool ambiguousSource = false,
        bool malformedMoveNextMethodImpl = false,
        int extraMethodCount = 0,
        bool moveNextSmallArray = false,
        bool unresolvedLiftedSource = false,
        bool malformedAnalyzeSignature = false,
        bool duplicateAnalyzeAttribute = false,
        bool malformedAnalyzeConstructor = false,
        bool forgedStateMachineOwnerEvidence = false,
        bool forgedTopLevelOwnerEvidence = false,
        bool malformedTopLevelEntryPoint = false,
        bool nestedUnresolvedLiftedSource = false,
        bool malformedGeneratedLiftedSource = false,
        bool typeGeneratedMalformedLiftedSource = false,
        bool malformedNestedLiftedIntermediate = false,
        bool deepNestedLiftedSource = false,
        bool crossKindDuplicate = false,
        bool crossKindIteratorFirst = false,
        bool crossKindSynchronousIterator = false,
        bool runtimeAsyncCompetingSource = false,
        bool orphanStateMachine = false,
        bool authoredCallerIntoMoveNext = false,
        bool explicitMoveNextMethodImpl = false,
        string moveNextMethodName = "MoveNext")
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            0,
            metadata.GetOrAddString("MalformedAsyncSource.dll"),
            metadata.GetOrAddGuid(Guid.NewGuid()),
            default,
            default);
        metadata.AddAssembly(
            metadata.GetOrAddString("MalformedAsyncSource"),
            new Version(1, 0, 0, 0),
            default,
            default,
            default,
            default);

        AssemblyReferenceHandle systemRuntime =
            metadata.AddAssemblyReference(
                metadata.GetOrAddString("System.Runtime"),
                new Version(11, 0, 0, 0),
                default,
                metadata.GetOrAddBlob(
                    new byte[]
                    {
                        0xB0,
                        0x3F,
                        0x5F,
                        0x7F,
                        0x11,
                        0xD5,
                        0x0A,
                        0x3A,
                    }),
                default,
                default);
        TypeReferenceHandle asyncStateMachineAttribute =
            metadata.AddTypeReference(
                systemRuntime,
                metadata.GetOrAddString(
                    "System.Runtime.CompilerServices"),
                metadata.GetOrAddString(
                    "AsyncStateMachineAttribute"));
        TypeReferenceHandle asyncIteratorStateMachineAttribute =
            crossKindDuplicate
                ? metadata.AddTypeReference(
                    systemRuntime,
                    metadata.GetOrAddString(
                        "System.Runtime.CompilerServices"),
                    metadata.GetOrAddString(
                        crossKindSynchronousIterator
                            ? "IteratorStateMachineAttribute"
                            : "AsyncIteratorStateMachineAttribute"))
                : default;
        TypeReferenceHandle asyncStateMachine =
            metadata.AddTypeReference(
                systemRuntime,
                metadata.GetOrAddString(
                    "System.Runtime.CompilerServices"),
                metadata.GetOrAddString("IAsyncStateMachine"));
        TypeReferenceHandle compilerGeneratedAttribute =
            metadata.AddTypeReference(
                systemRuntime,
                metadata.GetOrAddString(
                    "System.Runtime.CompilerServices"),
                metadata.GetOrAddString(
                    "CompilerGeneratedAttribute"));
        TypeReferenceHandle systemType =
            metadata.AddTypeReference(
                systemRuntime,
                metadata.GetOrAddString("System"),
                metadata.GetOrAddString("Type"));
        TypeReferenceHandle systemString =
            metadata.AddTypeReference(
                systemRuntime,
                metadata.GetOrAddString("System"),
                metadata.GetOrAddString("String"));
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
        TypeDefinitionHandle sourceType =
            metadata.AddTypeDefinition(
                TypeAttributes.Public | TypeAttributes.Abstract
                    | TypeAttributes.Sealed,
                metadata.GetOrAddString("Sample"),
                metadata.GetOrAddString("Source"),
                default,
                MetadataTokens.FieldDefinitionHandle(1),
                MetadataTokens.MethodDefinitionHandle(1));
        TypeDefinitionHandle stateMachineType =
            metadata.AddTypeDefinition(
                TypeAttributes.NestedPrivate
                    | TypeAttributes.Sealed,
                default,
                metadata.GetOrAddString(
                    "<AnalyzeAsync>d__1"),
                default,
                MetadataTokens.FieldDefinitionHandle(1),
                MetadataTokens.MethodDefinitionHandle(9));
        metadata.AddNestedType(stateMachineType, sourceType);
        metadata.AddInterfaceImplementation(
            stateMachineType,
            asyncStateMachine);

        var bodies = new BlobBuilder();
        var bodyEncoder = new MethodBodyStreamEncoder(bodies);
        static int AddRetBody(
            MethodBodyStreamEncoder encoder)
        {
            var il = new BlobBuilder();
            var instructions = new InstructionEncoder(il);
            instructions.OpCode(ILOpCode.Ret);
            return encoder.AddMethodBody(
                instructions,
                maxStack: 0);
        }

        var entryPointIl = new BlobBuilder();
        entryPointIl.WriteByte((byte)ILOpCode.Call);
        entryPointIl.WriteInt32(
            MetadataTokens.GetToken(
                MetadataTokens.MethodDefinitionHandle(6)));
        entryPointIl.WriteByte((byte)ILOpCode.Pop);
        entryPointIl.WriteByte((byte)ILOpCode.Ret);
        int entryPointBody = bodyEncoder.AddMethodBody(
            new InstructionEncoder(entryPointIl),
            maxStack: 1);
        var outerLiftedIl = new BlobBuilder();
        outerLiftedIl.WriteByte((byte)ILOpCode.Call);
        outerLiftedIl.WriteInt32(
            MetadataTokens.GetToken(
                MetadataTokens.MethodDefinitionHandle(6)));
        outerLiftedIl.WriteByte((byte)ILOpCode.Pop);
        outerLiftedIl.WriteByte((byte)ILOpCode.Ret);
        int outerLiftedBody = bodyEncoder.AddMethodBody(
            new InstructionEncoder(outerLiftedIl),
            maxStack: 1);
        var deepOuterLiftedIl = new BlobBuilder();
        deepOuterLiftedIl.WriteByte((byte)ILOpCode.Call);
        deepOuterLiftedIl.WriteInt32(
            MetadataTokens.GetToken(
                MetadataTokens.MethodDefinitionHandle(5)));
        deepOuterLiftedIl.WriteByte((byte)ILOpCode.Pop);
        deepOuterLiftedIl.WriteByte((byte)ILOpCode.Ret);
        int deepOuterLiftedBody = bodyEncoder.AddMethodBody(
            new InstructionEncoder(deepOuterLiftedIl),
            maxStack: 1);
        var liftedReadIl = new BlobBuilder();
        liftedReadIl.WriteByte((byte)ILOpCode.Call);
        liftedReadIl.WriteInt32(
            MetadataTokens.GetToken(
                MetadataTokens.MethodDefinitionHandle(7)));
        liftedReadIl.WriteByte((byte)ILOpCode.Ret);
        int liftedReadBody = bodyEncoder.AddMethodBody(
            new InstructionEncoder(liftedReadIl),
            maxStack: 0);
        var competingCallerIl = new BlobBuilder();
        competingCallerIl.WriteByte(
            (byte)ILOpCode.Ldc_i4_0);
        competingCallerIl.WriteByte(
            (byte)ILOpCode.Newarr);
        competingCallerIl.WriteInt32(
            MetadataTokens.GetToken(sourceType));
        competingCallerIl.WriteByte(
            (byte)ILOpCode.Pop);
        competingCallerIl.WriteByte(
            (byte)ILOpCode.Call);
        competingCallerIl.WriteInt32(
            MetadataTokens.GetToken(
                MetadataTokens.MethodDefinitionHandle(9)));
        competingCallerIl.WriteByte(
            (byte)ILOpCode.Ret);
        int competingCallerBody =
            bodyEncoder.AddMethodBody(
                new InstructionEncoder(competingCallerIl),
                maxStack: 1);

        BlobHandle taskSignature = metadata.GetOrAddBlob(
            new byte[]
            {
                0x00,
                0x00,
                0x12,
                (byte)CodedIndex.TypeDefOrRefOrSpec(task),
            });
        BlobHandle voidSignature = metadata.GetOrAddBlob(
            new byte[] { 0x00, 0x00, 0x01 });
        var malformedValueIl = new BlobBuilder();
        malformedValueIl.WriteByte((byte)ILOpCode.Call);
        malformedValueIl.WriteInt32(
            MetadataTokens.GetToken(
            MetadataTokens.MethodDefinitionHandle(7)));
        malformedValueIl.WriteByte((byte)ILOpCode.Ldnull);
        malformedValueIl.WriteByte((byte)ILOpCode.Ret);
        int malformedValueBody =
            bodyEncoder.AddMethodBody(
                new InstructionEncoder(malformedValueIl),
                maxStack: 1);
        MethodDefinitionHandle broken =
            metadata.AddMethodDefinition(
                MethodAttributes.Public
                    | MethodAttributes.Static,
                MethodImplAttributes.IL,
                metadata.GetOrAddString("BrokenAsync"),
                metadata.GetOrAddBlob(new byte[] { 0x00 }),
                forgedTopLevelOwnerEvidence
                    || malformedTopLevelEntryPoint
                    ? entryPointBody
                    : AddRetBody(bodyEncoder),
                MetadataTokens.ParameterHandle(1));
        MethodDefinitionHandle malformedValue =
            metadata.AddMethodDefinition(
                MethodAttributes.Public
                    | MethodAttributes.Static,
                MethodImplAttributes.IL,
                metadata.GetOrAddString(
                    "MalformedValueAsync"),
                taskSignature,
                malformedValueBody,
                MetadataTokens.ParameterHandle(1));
        MethodDefinitionHandle duplicate =
            metadata.AddMethodDefinition(
                MethodAttributes.Public
                    | MethodAttributes.Static,
                MethodImplAttributes.IL,
                metadata.GetOrAddString("DuplicateAsync"),
                taskSignature,
                AddRetBody(bodyEncoder),
                MetadataTokens.ParameterHandle(1));
        MethodDefinitionHandle foreignAssembly =
            metadata.AddMethodDefinition(
                MethodAttributes.Public
                    | MethodAttributes.Static,
                MethodImplAttributes.IL,
                metadata.GetOrAddString(
                    "ForeignAssemblyAsync"),
                taskSignature,
                AddRetBody(bodyEncoder),
                MetadataTokens.ParameterHandle(1));
        MethodDefinitionHandle competing =
            metadata.AddMethodDefinition(
                MethodAttributes.Public
                    | MethodAttributes.Static,
                MethodImplAttributes.IL
                    | (runtimeAsyncCompetingSource
                        ? MethodImplAttributes.Async
                        : 0),
                metadata.GetOrAddString(
                    malformedNestedLiftedIntermediate
                        ? "Noise>b__0_0"
                    : deepNestedLiftedSource
                        ? "<<Outer>b__0_0>b__0_1"
                    : nestedUnresolvedLiftedSource
                        ? "<Outer>b__0_0"
                        : forgedStateMachineOwnerEvidence
                        ? "<AnalyzeAsync>g__Local|0_0"
                        : forgedTopLevelOwnerEvidence
                            || malformedTopLevelEntryPoint
                            ? "<<Main>$>g__Local|0_0"
                        : "CompetingAsync"),
                taskSignature,
                authoredCallerIntoMoveNext
                    ? competingCallerBody
                    : nestedUnresolvedLiftedSource
                    || malformedNestedLiftedIntermediate
                    || deepNestedLiftedSource
                    ? outerLiftedBody
                    : AddRetBody(bodyEncoder),
                MetadataTokens.ParameterHandle(1));
        MethodDefinitionHandle analyze =
            metadata.AddMethodDefinition(
                MethodAttributes.Public
                    | MethodAttributes.Static,
                MethodImplAttributes.IL,
                metadata.GetOrAddString(
                    malformedNestedLiftedIntermediate
                        ? "<Noise>b__0_0>b__0_1"
                    : deepNestedLiftedSource
                        ? "<<<Outer>b__0_0>b__0_1>b__0_2"
                    : nestedUnresolvedLiftedSource
                        ? "<<Outer>b__0_0>b__0_1"
                        : malformedGeneratedLiftedSource
                            || typeGeneratedMalformedLiftedSource
                            ? "Noise>b__0_0"
                        : unresolvedLiftedSource
                        ? "<Outer>b__0_0"
                        : forgedTopLevelOwnerEvidence
                            || malformedTopLevelEntryPoint
                            ? "<Main>$"
                        : "AnalyzeAsync"),
                malformedAnalyzeSignature
                    ? metadata.GetOrAddBlob(
                        new byte[] { 0x00 })
                    : taskSignature,
                malformedNestedLiftedIntermediate
                    ? liftedReadBody
                    : AddRetBody(bodyEncoder),
                MetadataTokens.ParameterHandle(1));
        MethodDefinitionHandle read =
            metadata.AddMethodDefinition(
                MethodAttributes.Public
                    | MethodAttributes.Static,
                MethodImplAttributes.IL,
                metadata.GetOrAddString("Read"),
                voidSignature,
                AddRetBody(bodyEncoder),
                MetadataTokens.ParameterHandle(1));
        metadata.AddMethodDefinition(
            MethodAttributes.Public
                | MethodAttributes.Static,
            MethodImplAttributes.IL,
            metadata.GetOrAddString(
                deepNestedLiftedSource
                    ? "<Outer>b__0_0"
                    : "ReadAsync"),
            taskSignature,
            deepNestedLiftedSource
                ? deepOuterLiftedBody
                : AddRetBody(bodyEncoder),
            MetadataTokens.ParameterHandle(1));

        var moveNextIl = new BlobBuilder();
        if (moveNextSmallArray)
        {
            moveNextIl.WriteByte(
                (byte)ILOpCode.Ldc_i4_0);
            moveNextIl.WriteByte(
                (byte)ILOpCode.Newarr);
            moveNextIl.WriteInt32(
                MetadataTokens.GetToken(sourceType));
            moveNextIl.WriteByte(
                (byte)ILOpCode.Pop);
        }
        moveNextIl.WriteByte((byte)ILOpCode.Call);
        moveNextIl.WriteInt32(
            MetadataTokens.GetToken(
                forgedStateMachineOwnerEvidence
                    || forgedTopLevelOwnerEvidence
                    || malformedTopLevelEntryPoint
                    ? competing
                    : read));
        moveNextIl.WriteByte((byte)ILOpCode.Ret);
        int moveNextBody = bodyEncoder.AddMethodBody(
            new InstructionEncoder(moveNextIl),
            maxStack: moveNextSmallArray ? 1 : 0);
        MethodDefinitionHandle moveNext =
            metadata.AddMethodDefinition(
            MethodAttributes.Public
                | (forgedStateMachineOwnerEvidence
                    || forgedTopLevelOwnerEvidence
                    ? MethodAttributes.Static
                    : MethodAttributes.Virtual),
            MethodImplAttributes.IL,
            metadata.GetOrAddString(moveNextMethodName),
            metadata.GetOrAddBlob(
                forgedStateMachineOwnerEvidence
                    || forgedTopLevelOwnerEvidence
                    ? new byte[]
                    {
                        0x00, 0x01, 0x01, 0x08,
                    }
                    : new byte[]
                    {
                        0x20, 0x00, 0x01,
                    }),
            moveNextBody,
            MetadataTokens.ParameterHandle(1));
        metadata.AddMethodDefinition(
            MethodAttributes.Public,
            MethodImplAttributes.IL,
            metadata.GetOrAddString("MoveNext"),
            metadata.GetOrAddBlob(
                new byte[]
                {
                    0x20, 0x01, 0x01, 0x08,
                }),
            moveNextBody,
            MetadataTokens.ParameterHandle(1));

        if (malformedMoveNextMethodImpl
            || explicitMoveNextMethodImpl)
        {
            MemberReferenceHandle moveNextDeclaration =
                metadata.AddMemberReference(
                    asyncStateMachine,
                    metadata.GetOrAddString("MoveNext"),
                    metadata.GetOrAddBlob(
                        new byte[]
                        {
                            0x20, 0x00, 0x01,
                        }));
            metadata.AddMethodImplementation(
                stateMachineType,
                moveNext,
                moveNextDeclaration);
        }
        for (int i = 0; i < extraMethodCount; i++)
        {
            metadata.AddMethodDefinition(
                MethodAttributes.Private
                    | MethodAttributes.Static,
                MethodImplAttributes.IL,
                metadata.GetOrAddString(
                    $"Dummy{i}"),
                voidSignature,
                AddRetBody(bodyEncoder),
                MetadataTokens.ParameterHandle(1));
        }

        MemberReferenceHandle attributeConstructor =
            metadata.AddMemberReference(
                asyncStateMachineAttribute,
                metadata.GetOrAddString(".ctor"),
                metadata.GetOrAddBlob(
                    new byte[]
                    {
                        0x20,
                        0x01,
                        0x01,
                        0x12,
                        (byte)CodedIndex.TypeDefOrRefOrSpec(
                            systemType),
                    }));
        MemberReferenceHandle malformedAttributeConstructor =
            metadata.AddMemberReference(
                asyncStateMachineAttribute,
                metadata.GetOrAddString(".ctor"),
                metadata.GetOrAddBlob(
                    new byte[]
                    {
                        0x20,
                        0x01,
                        0x01,
                        0x12,
                        (byte)CodedIndex.TypeDefOrRefOrSpec(
                            systemString),
                    }));
        MemberReferenceHandle generatedAttributeConstructor =
            metadata.AddMemberReference(
                compilerGeneratedAttribute,
                metadata.GetOrAddString(".ctor"),
                metadata.GetOrAddBlob(
                    new byte[] { 0x20, 0x00, 0x01 }));
        MemberReferenceHandle iteratorAttributeConstructor =
            crossKindDuplicate
                ? metadata.AddMemberReference(
                    asyncIteratorStateMachineAttribute,
                    metadata.GetOrAddString(".ctor"),
                    metadata.GetOrAddBlob(
                        new byte[]
                        {
                            0x20,
                            0x01,
                            0x01,
                            0x12,
                            (byte)CodedIndex
                                .TypeDefOrRefOrSpec(
                                    systemType),
                        }))
                : default;
        AddAsyncStateMachineAttribute(
            metadata,
            broken,
            attributeConstructor,
            "Sample.Source+<BrokenAsync>d__0, MalformedAsyncSource");
        metadata.AddCustomAttribute(
            malformedValue,
            attributeConstructor,
            metadata.GetOrAddBlob(
                new byte[] { 0x01 }));
        AddAsyncStateMachineAttribute(
            metadata,
            duplicate,
            attributeConstructor,
            "Sample.Source+<DuplicateAsync>d__0, MalformedAsyncSource");
        AddAsyncStateMachineAttribute(
            metadata,
            duplicate,
            attributeConstructor,
            "Sample.Source+<DuplicateAsync>d__9, MalformedAsyncSource");
        AddAsyncStateMachineAttribute(
            metadata,
            foreignAssembly,
            attributeConstructor,
            "Sample.Source+<ForeignAssemblyAsync>d__0, OtherAssembly");
        if (ambiguousSource)
        {
            AddAsyncStateMachineAttribute(
                metadata,
                competing,
                attributeConstructor,
                "Sample.Source+<AnalyzeAsync>d__1, MalformedAsyncSource");
        }
        const string AnalyzeStateMachine =
            "Sample.Source+<AnalyzeAsync>d__1, MalformedAsyncSource";
        if (!iteratorAttributeConstructor.IsNil
            && crossKindIteratorFirst)
        {
            AddAsyncStateMachineAttribute(
                metadata,
                analyze,
                iteratorAttributeConstructor,
                AnalyzeStateMachine);
        }
        if (!orphanStateMachine)
        {
            AddAsyncStateMachineAttribute(
                metadata,
                analyze,
                malformedAnalyzeConstructor
                    ? malformedAttributeConstructor
                    : attributeConstructor,
                AnalyzeStateMachine);
        }
        if (malformedGeneratedLiftedSource)
        {
            metadata.AddCustomAttribute(
                analyze,
                generatedAttributeConstructor,
                metadata.GetOrAddBlob(
                    new byte[] { 0x01, 0x00, 0x00, 0x00 }));
        }
        if (typeGeneratedMalformedLiftedSource)
        {
            metadata.AddCustomAttribute(
                sourceType,
                generatedAttributeConstructor,
                metadata.GetOrAddBlob(
                    new byte[] { 0x01, 0x00, 0x00, 0x00 }));
        }
        if (malformedNestedLiftedIntermediate)
        {
            metadata.AddCustomAttribute(
                competing,
                generatedAttributeConstructor,
                metadata.GetOrAddBlob(
                    new byte[] { 0x01, 0x00, 0x00, 0x00 }));
        }
        if (duplicateAnalyzeAttribute)
        {
            AddAsyncStateMachineAttribute(
                metadata,
                analyze,
                attributeConstructor,
                AnalyzeStateMachine);
        }
        if (!iteratorAttributeConstructor.IsNil
            && !crossKindIteratorFirst)
        {
            AddAsyncStateMachineAttribute(
                metadata,
                analyze,
                iteratorAttributeConstructor,
                AnalyzeStateMachine);
        }

        var pe = new ManagedPEBuilder(
            forgedTopLevelOwnerEvidence
                || malformedTopLevelEntryPoint
                ? PEHeaderBuilder.CreateExecutableHeader()
                : PEHeaderBuilder.CreateLibraryHeader(),
            new MetadataRootBuilder(
                metadata,
                suppressValidation: true),
            bodies,
            entryPoint: forgedTopLevelOwnerEvidence
                    || malformedTopLevelEntryPoint
                ? broken
                : default,
            flags: CorFlags.ILOnly);
        var image = new BlobBuilder();
        pe.Serialize(image);
        byte[] bytes = image.ToArray();

        using var peReader = new PEReader(
            new MemoryStream(bytes, writable: false));
        MetadataReader reader = peReader.GetMetadataReader();
        if (malformedMoveNextMethodImpl)
        {
            int methodImplBodyOffset =
                peReader.PEHeaders.MetadataStartOffset
                + reader.GetTableMetadataOffset(
                    TableIndex.MethodImpl)
                + sizeof(ushort);
            BinaryPrimitives.WriteUInt16LittleEndian(
                bytes.AsSpan(
                    methodImplBodyOffset,
                    sizeof(ushort)),
                0x7FFE);
        }
        int constructorOffset =
            peReader.PEHeaders.MetadataStartOffset
            + reader.GetTableMetadataOffset(
                TableIndex.CustomAttribute)
            + sizeof(ushort);
        BinaryPrimitives.WriteUInt16LittleEndian(
            bytes.AsSpan(
                constructorOffset,
                sizeof(ushort)),
            0xFFFB);
        return bytes;
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

    static TypeReferenceHandle FirstExternalTypeReference(MetadataReader reader)
    {
        foreach (var handle in reader.TypeReferences)
        {
            if (reader.GetTypeReference(handle).ResolutionScope.Kind == HandleKind.AssemblyReference)
                return handle;
        }

        throw new InvalidOperationException("Expected at least one external TypeRef.");
    }

    static bool AssemblyForwardsType(
        string path,
        string @namespace,
        string name)
    {
        MetadataTypeDefinitionName structuredName =
            Assert.IsType<MetadataTypeDefinitionNameResult.Valid>(
                MetadataTypeDefinitionName.Create(
                    @namespace,
                    [name]))
            .Name;
        using var stream = File.OpenRead(path);
        using var peReader = new PEReader(stream);
        return peReader.HasMetadata
            && MetadataTypeDeclarationProbe.Probe(
                peReader.GetMetadataReader(),
                structuredName)
            is TypeDeclarationResult.Forwarded;
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

    static byte[] EmitStandaloneModule(
        string name,
        Guid moduleVersionId)
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            generation: 0,
            metadata.GetOrAddString(name),
            metadata.GetOrAddGuid(moduleVersionId),
            default,
            default);
        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            baseType: default,
            fieldList: MetadataTokens.FieldDefinitionHandle(1),
            methodList: MetadataTokens.MethodDefinitionHandle(1));
        var pe = new ManagedPEBuilder(
            PEHeaderBuilder.CreateLibraryHeader(),
            new MetadataRootBuilder(metadata),
            new BlobBuilder(),
            flags: CorFlags.ILOnly);
        var image = new BlobBuilder();
        pe.Serialize(image);
        return image.ToArray();
    }

    static MethodIdentity SyntheticMethod(
        string assemblyName,
        Guid moduleVersionId) =>
        new(
            assemblyName,
            moduleVersionId,
            TypeRef.Definition(
                assemblyName,
                "Fixtures",
                "SyntheticType"),
            "SyntheticMethod",
            [],
            TypeRef.CoreLib("System", "Void"),
            0x06000001,
            IsStatic: true);

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

    static TypeReferenceHandle FindExternalTypeReference(MetadataReader reader, string ns, string name)
    {
        foreach (var handle in reader.TypeReferences)
        {
            var typeReference = reader.GetTypeReference(handle);
            if (typeReference.ResolutionScope.Kind != HandleKind.AssemblyReference)
                continue;
            if (reader.StringComparer.Equals(typeReference.Namespace, ns)
                && reader.StringComparer.Equals(typeReference.Name, name))
                return handle;
        }

        return default;
    }

    /// <summary>Resolves an identity to the same-named assembly in a directory, with no redirect.</summary>
    sealed class FrameworkDirectoryResolver(string directory) : IAssemblyReferenceResolver
    {
        public ResolvedAssemblyReference? Resolve(AssemblyReferenceIdentity identity, AssemblyResolutionScope scope)
        {
            string candidate = Path.Combine(directory, identity.Name + ".dll");
            return File.Exists(candidate)
                ? ResolvedAssemblyReference.CreateFromPath(
                    candidate,
                    AssemblyResolutionProvenance.Local("test"))
                : null;
        }
    }

    /// <summary>Answers every identity with one assembly, so any forwarder chain cycles.</summary>
    sealed class ConstantResolver(string path) : IAssemblyReferenceResolver
    {
        public ResolvedAssemblyReference? Resolve(AssemblyReferenceIdentity identity, AssemblyResolutionScope scope)
            => ResolvedAssemblyReference.CreateFromPath(
                path,
                AssemblyResolutionProvenance.Local("test"));
    }

    /// <summary>Records every identity and scope the builder asks for.</summary>
    sealed class RecordingResolver(IAssemblyReferenceResolver inner) : IAssemblyReferenceResolver
    {
        public List<(string Name, AssemblyResolutionScope Scope)> Requests { get; } = [];

        public ResolvedAssemblyReference? Resolve(AssemblyReferenceIdentity identity, AssemblyResolutionScope scope)
        {
            Requests.Add((identity.Name, scope));
            return inner.Resolve(identity, scope);
        }
    }

    sealed class CountingResolver : IAssemblyReferenceResolver
    {
        public int ResolveCalls { get; private set; }

        public ResolvedAssemblyReference? Resolve(AssemblyReferenceIdentity identity, AssemblyResolutionScope scope)
        {
            ResolveCalls++;
            return null;
        }
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

    sealed class ThirdOpenFailsResolver(
        IAssemblyReferenceResolver inner)
        : IAssemblyReferenceResolver
    {
        readonly Dictionary<
            (AssemblyReferenceIdentity Identity,
                AssemblyResolutionScope Scope),
            ResolvedAssemblyReference?> _cache = [];
        readonly List<Func<int>> _openCounts = [];

        public IEnumerable<int> OpenCounts =>
            _openCounts.Select(read => read());

        public ResolvedAssemblyReference? Resolve(
            AssemblyReferenceIdentity identity,
            AssemblyResolutionScope scope)
        {
            var key = (identity, scope);
            if (_cache.TryGetValue(
                    key,
                    out ResolvedAssemblyReference? cached))
            {
                return cached;
            }

            ResolvedAssemblyReference? selected =
                inner.Resolve(identity, scope);
            if (selected is null)
            {
                _cache.Add(key, null);
                return null;
            }

            int opens = 0;
            ResolvedAssemblyReference retained =
                ResolvedAssemblyReference.Create(
                    selected.Identity,
                    selected.Path,
                    () =>
                    {
                        if (Interlocked.Increment(ref opens) > 2)
                        {
                            throw new IOException(
                                "The mutable source was reopened.");
                        }
                        return selected.OpenRead();
                    },
                    selected.Provenance,
                    selected.LastWriteTimeUtc);
            _openCounts.Add(() => opens);
            _cache.Add(key, retained);
            return retained;
        }
    }

    static List<string> FlattenCallTree(
        CallTreeNode root,
        bool includePerf = false)
    {
        var lines = new List<string>();
        void Walk(CallTreeNode node, int depth)
        {
            lines.Add(includePerf
                ? $"{depth}|{MemberIdentity(node.Member)}"
                    + $"|kind={node.Kind}"
                    + $"|status={node.Status}"
                    + $"|fanin={node.Perf?.Fanin}"
                    + $"|max-depth={node.Perf?.MaxDepth}"
                    + $"|in-loop={node.Perf?.InLoop}"
                    + $"|root-kind={node.Perf?.RootKind}"
                    + $"|source={node.Perf?.Source}"
                : $"{depth}|{node.Member.Name}|{node.Status}|"
                    + $"{node.Perf?.Source ?? ""}");
            foreach (var child in node.Children)
                Walk(child, depth + 1);
        }
        Walk(root, 0);
        return lines;

        static string MemberIdentity(MemberRef member) =>
            $"{GenericMemberIdentity.KeyFragment(member.DeclaringType)}"
            + $"::{member.Name}"
            + $"|arity={member.GenericArity}"
            + $"|parameters={TypeKeys(member.ParameterTypes)}"
            + $"|return={GenericMemberIdentity.KeyFragment(member.ReturnType)}"
            + $"|type-arguments={TypeKeys(member.TypeArguments)}"
            + $"|has-this={member.HasThis}"
            + $"|signature-header={member.SignatureHeader}"
            + $"|required-parameters={member.RequiredParameterCount}"
            + $"|open-parameters={TypeKeys(member.OpenParameterTypes)}"
            + $"|open-return={GenericMemberIdentity.KeyFragment(member.OpenSignatureReturn)}";

        static string TypeKeys(IEnumerable<TypeRef> types) =>
            string.Join(",", types.Select(GenericMemberIdentity.KeyFragment));
    }

    static bool InLeverageFixtures(MethodIdentity method)
        => method.DeclaringType.Name == nameof(LeverageFixtures);

    static MethodIdentity LeverageMethod(string name, int token)
        => new("Asm", Guid.Empty, TypeRef.Definition("Asm", "Ns", "Type"), name, [], TypeRef.CoreLib("System", "Void"), token, IsStatic: true);

    static DirectCall LeverageCall(MethodIdentity caller, MethodIdentity callee)
    {
        var calleeRef = new MemberRef(callee.DeclaringType, callee.Name, callee.ParameterTypes, callee.ReturnType, MemberKind.Method);
        return new DirectCall(caller, calleeRef, ILOffset: 0, OperandToken: callee.MetadataToken, CalleeDefinitionToken: callee.MetadataToken, CallKind.Call);
    }

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
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            0,
            metadata.GetOrAddString("DeepLocal.dll"),
            metadata.GetOrAddGuid(Guid.NewGuid()),
            default,
            default);
        metadata.AddAssembly(
            metadata.GetOrAddString("DeepLocal"),
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
        localSignature.WriteByte(0x07);
        localSignature.WriteByte(0x01);
        for (int i = 0;
            i < SignatureBlobGuard.DefaultMaxDepth;
            i++)
        {
            localSignature.WriteByte(0x1d);
        }
        localSignature.WriteByte(0x08);
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

    static IEnumerable<string> ArrayShapes(LibraryBodyIndex index, string methodName)
        => index.OptimizationOpportunities
            .Where(o => o.Method.Name == methodName && o.Shape is "small-array" or "stackalloc-candidate")
            .Select(o => o.Shape);

    static AllocationOccurrence SingleAllocationOccurrence(LibraryBodyIndex index, string methodName, AllocationKind kind)
        => SingleAllocationOccurrence(index, nameof(OptimizationOpportunityFixtures), methodName, kind);

    static AllocationOccurrence SingleAllocationOccurrence(LibraryBodyIndex index, string typeName, string methodName, AllocationKind kind)
    {
        var method = Assert.Single(index.Methods.Where(m =>
            m.DeclaringType.Name == typeName
            && m.Name == methodName));
        Assert.True(index.GetAllocationOccurrences().TryGetValue(method.MetadataToken, out var occurrences));
        return Assert.Single(occurrences.Where(occurrence =>
            occurrence.Kind == kind
            && occurrence.CountsAsHeapAllocation));
    }

    static (string Path, string Directory) BuildSlotReuseArrayFixture()
    {
        var directory = Path.Combine(Path.GetTempPath(), "dotnet-inspect-slot-reuse-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "SlotReuseArrayFixture.dll");

        var assemblyName = new AssemblyName("SlotReuseArrayFixture");
        var assembly = new PersistedAssemblyBuilder(assemblyName, typeof(object).Assembly);
        var module = assembly.DefineDynamicModule(assemblyName.Name!);
        var type = module.DefineType("SlotReuseArrayFixture", TypeAttributes.Public | TypeAttributes.Class | TypeAttributes.Abstract | TypeAttributes.Sealed);
        var method = type.DefineMethod(
            "LocalThenEscapingSlotReuse",
            MethodAttributes.Public | MethodAttributes.Static,
            typeof(int[]),
            Type.EmptyTypes);
        var il = method.GetILGenerator();
        il.DeclareLocal(typeof(int[]));

        il.Emit(OpCodes.Ldc_I4_4);
        il.Emit(OpCodes.Newarr, typeof(int));
        il.Emit(OpCodes.Stloc_0);
        il.Emit(OpCodes.Ldloc_0);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stelem_I4);
        il.Emit(OpCodes.Ldloc_0);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldelem_I4);
        il.Emit(OpCodes.Pop);

        il.Emit(OpCodes.Ldc_I4_4);
        il.Emit(OpCodes.Newarr, typeof(int));
        il.Emit(OpCodes.Stloc_0);
        il.Emit(OpCodes.Ldloc_0);
        il.Emit(OpCodes.Ret);

        type.CreateType();
        assembly.Save(path);
        return (path, directory);
    }

    static (string Path, string Directory) BuildLongAddressLoadArrayFixture()
    {
        var directory = Path.Combine(Path.GetTempPath(), "dotnet-inspect-long-address-load-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "LongAddressLoadArrayFixture.dll");

        var assemblyName = new AssemblyName("LongAddressLoadArrayFixture");
        var assembly = new PersistedAssemblyBuilder(assemblyName, typeof(object).Assembly);
        var module = assembly.DefineDynamicModule(assemblyName.Name!);
        var type = module.DefineType("LongAddressLoadArrayFixture", TypeAttributes.Public | TypeAttributes.Class | TypeAttributes.Abstract | TypeAttributes.Sealed);

        DefineLdlocaMethod(type);
        DefineLdargaMethod(type);

        type.CreateType();
        assembly.Save(path);
        return (path, directory);

        static void DefineLdlocaMethod(TypeBuilder type)
        {
            var method = type.DefineMethod(
                "ArrayReturnedAfterLongLdloca",
                MethodAttributes.Public | MethodAttributes.Static,
                typeof(int[]),
                Type.EmptyTypes);
            var il = method.GetILGenerator();
            var array = il.DeclareLocal(typeof(int[]));
            var marker = il.DeclareLocal(typeof(int));

            il.Emit(OpCodes.Ldc_I4_4);
            il.Emit(OpCodes.Newarr, typeof(int));
            il.Emit(OpCodes.Stloc, array);
            il.Emit(OpCodes.Ldloc, array);
            il.Emit(OpCodes.Ldloca, marker);
            il.Emit(OpCodes.Pop);
            il.Emit(OpCodes.Ret);
        }

        static void DefineLdargaMethod(TypeBuilder type)
        {
            var method = type.DefineMethod(
                "ArrayReturnedAfterLongLdarga",
                MethodAttributes.Public | MethodAttributes.Static,
                typeof(int[]),
                [typeof(int)]);
            var il = method.GetILGenerator();
            var array = il.DeclareLocal(typeof(int[]));

            il.Emit(OpCodes.Ldc_I4_4);
            il.Emit(OpCodes.Newarr, typeof(int));
            il.Emit(OpCodes.Stloc, array);
            il.Emit(OpCodes.Ldloc, array);
            il.Emit(OpCodes.Ldarga, (short)0);
            il.Emit(OpCodes.Pop);
            il.Emit(OpCodes.Ret);
        }
    }

    static (string Path, string Directory) BuildPathContextFixture()
    {
        var directory = Path.Combine(Path.GetTempPath(), "dotnet-inspect-path-context-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "PathContextFixture.dll");

        var assemblyName = new AssemblyName("PathContextFixture");
        var assembly = new PersistedAssemblyBuilder(assemblyName, typeof(object).Assembly);
        var module = assembly.DefineDynamicModule(assemblyName.Name!);
        var type = module.DefineType("PathContextFixture", TypeAttributes.Public | TypeAttributes.Class | TypeAttributes.Abstract | TypeAttributes.Sealed);
        var ctor = typeof(PlainObject).GetConstructor([typeof(int)])!;

        DefineBranchAllocation(type, ctor);
        DefineSwitchAllocation(type, ctor);
        DefineAfterIfJoinAllocation(type, ctor);
        DefineReturnOrInfiniteLoopAllocation(type, ctor);
        DefineInternalReturnLoopAllocation(type, ctor);

        type.CreateType();
        assembly.Save(path);
        return (path, directory);

        static void EmitNewPlainObject(ILGenerator il, ConstructorInfo ctor, int value)
        {
            il.Emit(OpCodes.Ldc_I4, value);
            il.Emit(OpCodes.Newobj, ctor);
            il.Emit(OpCodes.Ret);
        }

        static void DefineBranchAllocation(TypeBuilder type, ConstructorInfo ctor)
        {
            var method = type.DefineMethod(
                "BranchAllocation",
                MethodAttributes.Public | MethodAttributes.Static,
                typeof(object),
                [typeof(bool)]);
            var il = method.GetILGenerator();
            var elseLabel = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Brfalse_S, elseLabel);
            EmitNewPlainObject(il, ctor, 1);
            il.MarkLabel(elseLabel);
            EmitNewPlainObject(il, ctor, 2);
        }

        static void DefineSwitchAllocation(TypeBuilder type, ConstructorInfo ctor)
        {
            var method = type.DefineMethod(
                "SwitchAllocation",
                MethodAttributes.Public | MethodAttributes.Static,
                typeof(object),
                [typeof(int)]);
            var il = method.GetILGenerator();
            var case0 = il.DefineLabel();
            var case1 = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Switch, [case0, case1]);
            EmitNewPlainObject(il, ctor, 3);
            il.MarkLabel(case0);
            EmitNewPlainObject(il, ctor, 1);
            il.MarkLabel(case1);
            EmitNewPlainObject(il, ctor, 2);
        }

        static void DefineAfterIfJoinAllocation(TypeBuilder type, ConstructorInfo ctor)
        {
            var method = type.DefineMethod(
                "AfterIfJoinAllocation",
                MethodAttributes.Public | MethodAttributes.Static,
                typeof(object),
                [typeof(bool)]);
            var il = method.GetILGenerator();
            var join = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Brfalse_S, join);
            il.Emit(OpCodes.Nop);
            il.Emit(OpCodes.Br_S, join);
            il.MarkLabel(join);
            EmitNewPlainObject(il, ctor, 1);
        }

        static void DefineReturnOrInfiniteLoopAllocation(TypeBuilder type, ConstructorInfo ctor)
        {
            var method = type.DefineMethod(
                "ReturnOrInfiniteLoopAllocation",
                MethodAttributes.Public | MethodAttributes.Static,
                typeof(object),
                [typeof(bool)]);
            var il = method.GetILGenerator();
            var loop = il.DefineLabel();
            var value = il.DeclareLocal(typeof(object));
            il.Emit(OpCodes.Ldc_I4, 1);
            il.Emit(OpCodes.Newobj, ctor);
            il.Emit(OpCodes.Stloc, value);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Brtrue_S, loop);
            il.Emit(OpCodes.Ldloc, value);
            il.Emit(OpCodes.Ret);
            il.MarkLabel(loop);
            il.Emit(OpCodes.Br_S, loop);
        }

        static void DefineInternalReturnLoopAllocation(TypeBuilder type, ConstructorInfo ctor)
        {
            var method = type.DefineMethod(
                "InternalReturnLoopAllocation",
                MethodAttributes.Public | MethodAttributes.Static,
                typeof(object),
                [typeof(bool)]);
            var il = method.GetILGenerator();
            var header = il.DefineLabel();
            var ret = il.DefineLabel();
            var value = il.DeclareLocal(typeof(object));
            il.Emit(OpCodes.Ldc_I4, 1);
            il.Emit(OpCodes.Newobj, ctor);
            il.Emit(OpCodes.Stloc, value);
            il.MarkLabel(header);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Brtrue_S, ret);
            il.Emit(OpCodes.Br_S, header);
            il.MarkLabel(ret);
            il.Emit(OpCodes.Ldloc, value);
            il.Emit(OpCodes.Ret);
        }
    }

    static (string Path, string Directory) BuildSpanToArrayLocalFixture()
    {
        var directory = Path.Combine(Path.GetTempPath(), "dotnet-inspect-span-local-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "SpanToArrayLocalFixture.dll");

        var assemblyName = new AssemblyName("SpanToArrayLocalFixture");
        var assembly = new PersistedAssemblyBuilder(assemblyName, typeof(object).Assembly);
        var module = assembly.DefineDynamicModule(assemblyName.Name!);
        var type = module.DefineType("SpanToArrayLocalFixture", TypeAttributes.Public | TypeAttributes.Class | TypeAttributes.Abstract | TypeAttributes.Sealed);
        var spanType = typeof(ReadOnlySpan<int>);
        var toArray = spanType.GetMethod(nameof(ReadOnlySpan<int>.ToArray), Type.EmptyTypes)!;

        DefineLocalRead(type, spanType, toArray);
        DefineLocalReturn(type, spanType, toArray);

        type.CreateType();
        assembly.Save(path);
        return (path, directory);

        static void DefineLocalRead(TypeBuilder type, Type spanType, MethodInfo toArray)
        {
            var method = type.DefineMethod(
                "LocalRead",
                MethodAttributes.Public | MethodAttributes.Static,
                typeof(int),
                [spanType]);
            var il = method.GetILGenerator();
            il.DeclareLocal(typeof(int[]));
            il.Emit(OpCodes.Ldarga_S, (byte)0);
            il.Emit(OpCodes.Call, toArray);
            il.Emit(OpCodes.Stloc_0);
            il.Emit(OpCodes.Ldloc_0);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Ldelem_I4);
            il.Emit(OpCodes.Ldloc_0);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Ldelem_I4);
            il.Emit(OpCodes.Add);
            il.Emit(OpCodes.Ret);
        }

        static void DefineLocalReturn(TypeBuilder type, Type spanType, MethodInfo toArray)
        {
            var method = type.DefineMethod(
                "LocalReturn",
                MethodAttributes.Public | MethodAttributes.Static,
                typeof(int[]),
                [spanType]);
            var il = method.GetILGenerator();
            il.DeclareLocal(typeof(int[]));
            il.Emit(OpCodes.Ldarga_S, (byte)0);
            il.Emit(OpCodes.Call, toArray);
            il.Emit(OpCodes.Stloc_0);
            il.Emit(OpCodes.Ldloc_0);
            il.Emit(OpCodes.Ret);
        }
    }

    static IEnumerable<string> DelegateShapes(LibraryBodyIndex index, string methodName)
        => index.OptimizationOpportunities
            .Where(o => o.Method.Name == methodName && o.Shape is "delegate-allocation" or "capturing-delegate" or "instance-method-group-delegate" or "cache-lookup-factory-delegate")
            .Select(o => o.Shape);

    static System.Collections.Generic.List<OptimizationOpportunity> BoxRows(LibraryBodyIndex index, string methodName)
        => index.OptimizationOpportunities
            .Where(o => o.Method.Name == methodName && o.Shape == "box-value-type")
            .ToList();

    static System.Collections.Generic.List<OptimizationOpportunity> GenericObjectBoxRows(
        LibraryBodyIndex index,
        string methodName)
        => index.OptimizationOpportunities
            .Where(o => o.Method.Name == methodName
                && o.Shape == "generic-parameter-object-box")
            .ToList();

    static System.Collections.Generic.List<OptimizationOpportunity> HotspotRows(LibraryBodyIndex index, string methodName)
        => index.OptimizationOpportunities
            .Where(o => o.Method.Name == methodName && o.Shape == "allocation-hotspot")
            .ToList();

    static System.Collections.Generic.List<OptimizationOpportunity> StringBuildRows(LibraryBodyIndex index, string methodName)
        => index.OptimizationOpportunities
            .Where(o => o.Method.Name == methodName && o.Shape == "string-build-in-loop")
            .ToList();

    static System.Collections.Generic.List<OptimizationOpportunity> EnumeratorRows(LibraryBodyIndex index, string methodName)
        => index.OptimizationOpportunities
            .Where(o => o.Method.Name == methodName && o.Shape == "enumerator-allocation")
            .ToList();

    static int AllocationsOf(LibraryBodyIndex index, string methodName)
    {
        int token = index.Methods.First(method => method.Name == methodName).MetadataToken;
        return index.GetMethodSignals().GetValueOrDefault(token, MethodSignals.None).Allocations;
    }

    static void CorruptStateMachineClaim(
        byte[] image,
        string ownerName)
    {
        string? claimedType = null;
        using (var peReader = new PEReader(
            new MemoryStream(image, writable: false)))
        {
            MetadataReader reader =
                peReader.GetMetadataReader();
            foreach (MethodDefinitionHandle methodHandle
                in reader.MethodDefinitions)
            {
                MethodDefinition method =
                    reader.GetMethodDefinition(methodHandle);
                if (!reader.StringComparer.Equals(
                        method.Name,
                        ownerName))
                {
                    continue;
                }
                foreach (CustomAttributeHandle attributeHandle
                    in method.GetCustomAttributes())
                {
                    CustomAttribute attribute =
                        reader.GetCustomAttribute(
                            attributeHandle);
                    BlobReader value =
                        reader.GetBlobReader(
                            attribute.Value);
                    if (value.Length < 3
                        || value.ReadUInt16() != 0x0001)
                    {
                        continue;
                    }
                    string? candidate =
                        value.ReadSerializedString();
                    if (candidate?.Contains(
                            ownerName,
                            StringComparison.Ordinal)
                        == true)
                    {
                        claimedType = candidate;
                        break;
                    }
                }
                if (claimedType is not null)
                    break;
            }
        }

        Assert.NotNull(claimedType);
        byte[] claim = Encoding.UTF8.GetBytes(
            claimedType);
        int offset = image.AsSpan().IndexOf(claim);
        Assert.True(offset >= 0);
        image[offset] = (byte)'Z';
    }

    static void SetRuntimeAsyncFlag(
        byte[] image,
        Func<MetadataReader, MethodDefinitionHandle>
            selectMethod)
    {
        using var peReader = new PEReader(
            new MemoryStream(image, writable: false));
        MetadataReader reader =
            peReader.GetMetadataReader();
        MethodDefinitionHandle methodHandle =
            selectMethod(reader);
        int implFlagsOffset =
            peReader.PEHeaders.MetadataStartOffset
            + reader.GetTableMetadataOffset(
                TableIndex.MethodDef)
            + (MetadataTokens.GetRowNumber(methodHandle) - 1)
                * reader.GetTableRowSize(
                    TableIndex.MethodDef)
            + sizeof(int);
        ushort implFlags =
            BinaryPrimitives.ReadUInt16LittleEndian(
                image.AsSpan(
                    implFlagsOffset,
                    sizeof(ushort)));
        BinaryPrimitives.WriteUInt16LittleEndian(
            image.AsSpan(
                implFlagsOffset,
                sizeof(ushort)),
            (ushort)(implFlags
                | (ushort)MethodImplAttributes.Async));
    }

    static byte[] EmitLiftedMemberReferenceReplayAssembly(
        int referenceCount,
        int ownerCount,
        int parameterCount,
        bool distinctSignatureBlobs = false)
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            0,
            metadata.GetOrAddString("DuplicateMemberRefs.dll"),
            metadata.GetOrAddGuid(Guid.NewGuid()),
            default,
            default);
        metadata.AddAssembly(
            metadata.GetOrAddString("DuplicateMemberRefs"),
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
            metadata.GetOrAddString("N"),
            metadata.GetOrAddString("Sample"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));

        var liftedSignatureHandles =
            new BlobHandle[referenceCount];
        int liftedSignatureLength = 0;
        var referenceTokens = new int[referenceCount];
        for (int i = 0; i < referenceTokens.Length; i++)
        {
            var liftedSignature = new BlobBuilder();
            liftedSignature.WriteByte(0);
            liftedSignature.WriteCompressedInteger(parameterCount);
            liftedSignature.WriteByte(1);
            for (int parameter = 0;
                parameter < parameterCount;
                parameter++)
            {
                liftedSignature.WriteByte(
                    distinctSignatureBlobs
                        && parameter == parameterCount - 1
                            ? checked((byte)(i + 2))
                            : (byte)8);
            }
            liftedSignatureLength = liftedSignature.Count;
            liftedSignatureHandles[i] =
                metadata.GetOrAddBlob(liftedSignature);
            referenceTokens[i] = MetadataTokens.GetToken(
                metadata.AddMemberReference(
                    type,
                    metadata.GetOrAddString(
                        "<Owner>g__Core|0_0"),
                    liftedSignatureHandles[i]));
        }
        if (distinctSignatureBlobs)
        {
            Assert.Equal(
                referenceCount,
                liftedSignatureHandles.Distinct().Count());
        }

        var bodies = new BlobBuilder();
        var bodyEncoder = new MethodBodyStreamEncoder(bodies);
        var ownerIl = new BlobBuilder();
        foreach (int token in referenceTokens)
        {
            ownerIl.WriteByte((byte)ILOpCode.Call);
            ownerIl.WriteInt32(token);
        }
        ownerIl.WriteByte((byte)ILOpCode.Ret);
        int ownerBody = bodyEncoder.AddMethodBody(
            new InstructionEncoder(ownerIl),
            maxStack: 0);
        var liftedIl = new BlobBuilder();
        liftedIl.WriteByte((byte)ILOpCode.Ret);
        int liftedBody = bodyEncoder.AddMethodBody(
            new InstructionEncoder(liftedIl),
            maxStack: 0);
        var ownerSignature = new BlobBuilder();
        ownerSignature.WriteByte(0);
        ownerSignature.WriteByte(0);
        ownerSignature.WriteByte(1);
        BlobHandle ownerSignatureHandle =
            metadata.GetOrAddBlob(ownerSignature);
        for (int i = 0; i < ownerCount; i++)
        {
            metadata.AddMethodDefinition(
                MethodAttributes.Public | MethodAttributes.Static,
                MethodImplAttributes.IL,
                metadata.GetOrAddString("Owner"),
                ownerSignatureHandle,
                ownerBody,
                MetadataTokens.ParameterHandle(1));
        }
        metadata.AddMethodDefinition(
            MethodAttributes.Private | MethodAttributes.Static,
            MethodImplAttributes.IL,
            metadata.GetOrAddString("<Owner>g__Core|0_0"),
            liftedSignatureHandles[0],
            liftedBody,
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
        byte[] bytes = image.ToArray();
        if (distinctSignatureBlobs)
        {
            using var stream = new MemoryStream(bytes, writable: false);
            using var reader = new PEReader(stream);
            int blobStream = MetadataStreamOffset(
                bytes,
                reader.PEHeaders.MetadataStartOffset,
                "#Blob");
            int prefixLength = liftedSignatureLength switch
            {
                <= 0x7F => 1,
                <= 0x3FFF => 2,
                _ => 4,
            };
            foreach (BlobHandle handle in liftedSignatureHandles)
            {
                int lastByte = blobStream
                    + MetadataTokens.GetHeapOffset(handle)
                    + prefixLength
                    + liftedSignatureLength
                    - 1;
                bytes[lastByte] = 8;
            }
        }
        return bytes;
    }

    static int CountMethodReferenceResolutions(byte[] image)
    {
        using var stream = new MemoryStream(image);
        using var peReader = new PEReader(stream);
        MetadataReader reader = peReader.GetMetadataReader();
        int resolved = 0;
        using var builder = new LibraryBodyAnalysisBuilder(
            "DuplicateMemberRefs.dll",
            reader,
            peReader,
            resolver: null,
            methodReferenceResolved: (_, _) =>
                Interlocked.Increment(ref resolved));

        _ = builder.Build(LibraryBodyAnalysisPlan.Create(
            LibraryBodyAnalysisFeatures.OptimizationOpportunities,
            methodScope: null,
            typeScope: null));
        return resolved;
    }

    static TypeRef ExactDefinition(
        string flattenedName,
        params string[] segments)
    {
        var result =
            Assert.IsType<
                MetadataTypeDefinitionNameResult.Valid>(
                MetadataTypeDefinitionName.Create(
                    "Sample",
                    [.. segments]));
        return TypeRef.Definition(
            "Fixture",
            "Sample",
            flattenedName,
            new ResolvableTypeReference(
                new TypeReferenceOrigin.CurrentAssembly(),
                result.Name));
    }

    static byte[]
        BuildNestedLiftedInvalidAsyncSourceAssembly(
            out int sourceToken,
            out int liftedToken)
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            0,
            metadata.GetOrAddString(
                "NestedLiftedInvalidAsyncSource.dll"),
            metadata.GetOrAddGuid(Guid.NewGuid()),
            default,
            default);
        metadata.AddAssembly(
            metadata.GetOrAddString(
                "NestedLiftedInvalidAsyncSource"),
            new Version(1, 0, 0, 0),
            default,
            default,
            default,
            default);
        AssemblyReferenceHandle systemRuntime =
            metadata.AddAssemblyReference(
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
        TypeReferenceHandle objectType =
            metadata.AddTypeReference(
                systemRuntime,
                metadata.GetOrAddString("System"),
                metadata.GetOrAddString("Object"));
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
        TypeReferenceHandle compilerGeneratedAttribute =
            metadata.AddTypeReference(
                systemRuntime,
                metadata.GetOrAddString(
                    "System.Runtime.CompilerServices"),
                metadata.GetOrAddString(
                    "CompilerGeneratedAttribute"));
        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        TypeDefinitionHandle sourceType =
            metadata.AddTypeDefinition(
                TypeAttributes.Public
                    | TypeAttributes.Class,
                metadata.GetOrAddString("Sample"),
                metadata.GetOrAddString("Source"),
                objectType,
                MetadataTokens.FieldDefinitionHandle(1),
                MetadataTokens.MethodDefinitionHandle(1));
        TypeDefinitionHandle displayClass =
            metadata.AddTypeDefinition(
                TypeAttributes.NestedPrivate
                    | TypeAttributes.Class
                    | TypeAttributes.Sealed,
                default,
                metadata.GetOrAddString("<>c"),
                objectType,
                MetadataTokens.FieldDefinitionHandle(1),
                MetadataTokens.MethodDefinitionHandle(2));
        TypeDefinitionHandle invalidStateMachine =
            metadata.AddTypeDefinition(
                TypeAttributes.NestedPrivate
                    | TypeAttributes.Class
                    | TypeAttributes.Sealed,
                default,
                metadata.GetOrAddString(
                    "NotAStateMachine"),
                objectType,
                MetadataTokens.FieldDefinitionHandle(1),
                MetadataTokens.MethodDefinitionHandle(3));
        metadata.AddNestedType(
            displayClass,
            sourceType);
        metadata.AddNestedType(
            invalidStateMachine,
            sourceType);

        BlobHandle signature =
            metadata.GetOrAddBlob(
                new byte[] { 0x00, 0x00, 0x01 });
        var bodies = new BlobBuilder();
        var bodyEncoder =
            new MethodBodyStreamEncoder(bodies);
        MethodDefinitionHandle lifted =
            MetadataTokens.MethodDefinitionHandle(2);
        var sourceIl = new BlobBuilder();
        sourceIl.WriteByte((byte)ILOpCode.Call);
        sourceIl.WriteInt32(
            MetadataTokens.GetToken(lifted));
        sourceIl.WriteByte((byte)ILOpCode.Ret);
        int sourceBody = bodyEncoder.AddMethodBody(
            new InstructionEncoder(sourceIl));
        var liftedIl = new BlobBuilder();
        liftedIl.WriteByte((byte)ILOpCode.Ret);
        int liftedBody = bodyEncoder.AddMethodBody(
            new InstructionEncoder(liftedIl));
        MethodDefinitionHandle source =
            metadata.AddMethodDefinition(
                MethodAttributes.Public
                    | MethodAttributes.Static,
                MethodImplAttributes.IL,
                metadata.GetOrAddString(
                    "BadSourceLambda"),
                signature,
                sourceBody,
                MetadataTokens.ParameterHandle(1));
        sourceToken =
            MetadataTokens.GetToken(source);
        MethodDefinitionHandle addedLifted =
            metadata.AddMethodDefinition(
                MethodAttributes.Private
                    | MethodAttributes.Static,
                MethodImplAttributes.IL,
                metadata.GetOrAddString(
                    "<BadSourceLambda>b__0_0"),
                signature,
                liftedBody,
                MetadataTokens.ParameterHandle(1));
        Assert.Equal(lifted, addedLifted);
        liftedToken =
            MetadataTokens.GetToken(addedLifted);

        MemberReferenceHandle asyncConstructor =
            metadata.AddMemberReference(
                asyncStateMachineAttribute,
                metadata.GetOrAddString(".ctor"),
                metadata.GetOrAddBlob(
                    new byte[]
                    {
                        0x20, 0x01, 0x01, 0x12,
                        (byte)CodedIndex
                            .TypeDefOrRefOrSpec(
                                systemType),
                    }));
        AddAsyncStateMachineAttribute(
            metadata,
            source,
            asyncConstructor,
            "Sample.Source+NotAStateMachine, "
                + "NestedLiftedInvalidAsyncSource");
        MemberReferenceHandle generatedConstructor =
            metadata.AddMemberReference(
                compilerGeneratedAttribute,
                metadata.GetOrAddString(".ctor"),
                metadata.GetOrAddBlob(
                    new byte[] { 0x20, 0x00, 0x01 }));
        metadata.AddCustomAttribute(
            displayClass,
            generatedConstructor,
            metadata.GetOrAddBlob(
                new byte[] { 0x01, 0x00, 0x00, 0x00 }));

        return SerializeDirectionProbe(
            metadata,
            bodies);
    }

    static void ReplaceUniqueAscii(
        byte[] image,
        string oldValue,
        string newValue)
        => ReplaceAscii(
            image,
            oldValue,
            newValue,
            expectedReplacements: 1);

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

    static int MetadataStreamOffset(
        byte[] image,
        int metadataRoot,
        string streamName)
    {
        int versionLength = BinaryPrimitives.ReadInt32LittleEndian(
            image.AsSpan(metadataRoot + 12, 4));
        int position = metadataRoot + 16
            + ((versionLength + 3) & ~3);
        int streamCount = BinaryPrimitives.ReadUInt16LittleEndian(
            image.AsSpan(position + 2, 2));
        position += 4;
        for (int i = 0; i < streamCount; i++)
        {
            int offset = BinaryPrimitives.ReadInt32LittleEndian(
                image.AsSpan(position, 4));
            position += 8;
            int nameStart = position;
            while (image[position] != 0)
                position++;
            string name = System.Text.Encoding.ASCII.GetString(
                image,
                nameStart,
                position - nameStart);
            position = (position + 4) & ~3;
            if (name == streamName)
                return metadataRoot + offset;
        }

        throw new BadImageFormatException(
            $"Metadata stream {streamName} was not found.");
    }

    static DirectCall EnumerableToArrayCall(string calleeAssembly, int callerToken)
    {
        var enumerable = TypeRef.Definition(calleeAssembly, "System.Linq", "Enumerable");
        var callee = new MemberRef(enumerable, "ToArray", [], TypeRef.Unsupported("ret"), MemberKind.Method);
        return FrameworkCall(callee, callerToken);
    }

    static DirectCall ExpressionConstantCall(string calleeAssembly, int callerToken)
    {
        var expression = TypeRef.Definition(calleeAssembly, "System.Linq.Expressions", "Expression");
        var callee = new MemberRef(expression, "Constant", [], TypeRef.Unsupported("ret"), MemberKind.Method);
        return FrameworkCall(callee, callerToken);
    }

    static DirectCall FrameworkCall(MemberRef callee, int callerToken)
    {
        var callerType = TypeRef.Definition("Caller.Assembly", "Caller.Ns", "CallerType");
        var caller = new MethodIdentity(
            "Caller.Assembly",
            Guid.Empty,
            callerType,
            "Caller",
            [],
            TypeRef.CoreLib("System", "Void"),
            callerToken,
            IsStatic: true);
        return new DirectCall(caller, callee, ILOffset: 0, OperandToken: 0, CalleeDefinitionToken: 0, CallKind.Call);
    }

    static (string Path, string Directory) BuildRepeatedCallSiteFixture()
    {
        var directory = Path.Combine(Path.GetTempPath(), "dotnet-inspect-repeated-call-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "RepeatedCallSiteFixture.dll");

        var assemblyName = new AssemblyName("RepeatedCallSiteFixture");
        var assembly = new PersistedAssemblyBuilder(assemblyName, typeof(object).Assembly);
        var module = assembly.DefineDynamicModule(assemblyName.Name!);
        var type = module.DefineType("RepeatedCallSiteFixture", TypeAttributes.Public | TypeAttributes.Class | TypeAttributes.Abstract | TypeAttributes.Sealed);

        var target = type.DefineMethod("Target", MethodAttributes.Public | MethodAttributes.Static, typeof(void), Type.EmptyTypes);
        target.GetILGenerator().Emit(OpCodes.Ret);

        // Two call sites from one caller.
        var twice = type.DefineMethod("CallsTargetTwice", MethodAttributes.Public | MethodAttributes.Static, typeof(void), Type.EmptyTypes);
        var twiceIl = twice.GetILGenerator();
        twiceIl.Emit(OpCodes.Call, target);
        twiceIl.Emit(OpCodes.Call, target);
        twiceIl.Emit(OpCodes.Ret);

        // One call site from a second caller.
        var once = type.DefineMethod("CallsTargetOnce", MethodAttributes.Public | MethodAttributes.Static, typeof(void), Type.EmptyTypes);
        var onceIl = once.GetILGenerator();
        onceIl.Emit(OpCodes.Call, target);
        onceIl.Emit(OpCodes.Ret);

        type.CreateType();
        assembly.Save(path);
        return (path, directory);
    }
}
