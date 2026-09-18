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

}
