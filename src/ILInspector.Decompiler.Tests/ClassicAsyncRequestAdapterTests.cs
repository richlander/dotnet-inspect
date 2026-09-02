using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Runtime.CompilerServices;

using ILInspector.Decompiler.Pipeline;
using ILInspector.Metadata;
using ILInspector.MetadataPrimitives;

namespace ILInspector.Decompiler.Tests;

[Trait("Area", "Pass")]
[Trait("Speed", "Fast")]
public sealed class ClassicAsyncRequestAdapterTests
{
    [Fact]
    public void RuntimeAsyncBudgetFailureSurvivesProductionImport()
    {
        byte[] image = BuildRuntimeAsyncImage(
            MetadataSafetyPolicy.MaxRelationshipNodes + 1);
        using var source = MetadataSource.OpenFromPrefetchedImage(
            "RuntimeAsyncBudget.dll",
            ImmutableArray.CreateRange(image));

        IrFunction imported = Assert.IsType<IrFunction>(
            IrImporter.Import(
                source,
                "Fixtures.Owner",
                "Kickoff"));
        var unavailable = Assert.IsType<
            ClassicAsyncRequestAdapterResult.OwnerUnavailable>(
                imported.ClassicAsyncRequest);
        var rejected = Assert.IsType<
            StateMachineRelationshipResult.Rejected>(
                unavailable.Evidence.Relationship);

        Assert.Equal(
            MethodClassification.RuntimeAsync,
            unavailable.Evidence.Classification);
        Assert.Equal(
            StateMachineRelationshipFailureKind.BudgetExceeded,
            rejected.Failure.Kind);
        Assert.Contains(
            unavailable.Evidence.RequestedMethod,
            rejected.Failure.KickoffCandidates);
    }

    [Fact]
    public void RuntimeAsyncNonBudgetRejectionRemainsFiltered()
    {
        byte[] image = BuildRuntimeAsyncImage(attributeCount: 1);
        using var source = MetadataSource.OpenFromPrefetchedImage(
            "RuntimeAsyncNonBudget.dll",
            ImmutableArray.CreateRange(image));

        IrFunction imported = Assert.IsType<IrFunction>(
            IrImporter.Import(
                source,
                "Fixtures.Owner",
                "Kickoff"));
        var filtered = Assert.IsType<
            ClassicAsyncRequestAdapterResult.Filtered>(
                imported.ClassicAsyncRequest);
        var rejected = Assert.IsType<
            StateMachineRelationshipResult.Rejected>(
                filtered.Evidence.Relationship);

        Assert.Equal(
            MethodClassification.RuntimeAsync,
            filtered.Evidence.Classification);
        Assert.NotEqual(
            StateMachineRelationshipFailureKind.BudgetExceeded,
            rejected.Failure.Kind);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(ushort.MaxValue, false)]
    [InlineData(0x10000001, true)]
    public void InvalidModuleIdentityRemainsVisibleAcquisitionFailure(
        int mvidIndex,
        bool largeGuidHeap)
    {
        byte[] image = BuildRuntimeAsyncImage(
            attributeCount: 1,
            largeGuidHeap);
        SetModuleMvid(image, mvidIndex);
        using var source = MetadataSource.OpenFromPrefetchedImage(
            "RuntimeAsyncInvalidMvid.dll",
            ImmutableArray.CreateRange(image));

        IrFunction imported = Assert.IsType<IrFunction>(
            IrImporter.Import(
                source,
                "Fixtures.Owner",
                "Kickoff"));
        var failed = Assert.IsType<
            ClassicAsyncRequestAdapterResult.AcquisitionFailed>(
                imported.ClassicAsyncRequest);
        var rejected = Assert.IsType<
            StateMachineRelationshipResult.Rejected>(
                failed.Relationship);

        Assert.Equal(
            MethodClassification.RuntimeAsync,
            failed.Classification);
        Assert.Equal(
            StateMachineRelationshipFailureKind.Malformed,
            rejected.Failure.Kind);
        Assert.DoesNotContain(
            imported.Diagnostics,
            diagnostic => diagnostic.Id == DiagnosticIds.InternalError);

        var exactReference = new MethodRef(
            TypeRef.Definition(
                "RuntimeAsyncInvalidMvid",
                "Fixtures",
                "Owner"),
            "Kickoff",
            TypeRef.CoreLib("System", "Void"),
            [],
            HasThis: false)
        {
            ExactDefinitionAddress = new MetadataMethodAddress(
                Guid.NewGuid(),
                MetadataTokens.MethodDefinitionHandle(1)),
            ExactDefinitionAcquisitionGuard = source.AcquisitionGuard,
        };
        IrFunction exactImport = Assert.IsType<IrFunction>(
            IrImporter.Import(source, exactReference));
        Assert.Contains(
            exactImport.Diagnostics,
            diagnostic => diagnostic.Id == DiagnosticIds.InternalError);
    }

    static byte[] BuildRuntimeAsyncImage(
        int attributeCount,
        bool largeGuidHeap = false)
    {
        var metadata = new MetadataBuilder();
        GuidHandle moduleVersionId =
            metadata.GetOrAddGuid(Guid.NewGuid());
        metadata.AddModule(
            generation: 0,
            moduleName:
                metadata.GetOrAddString("RuntimeAsyncBudget.dll"),
            mvid: moduleVersionId,
            encId: default,
            encBaseId: default);
        if (largeGuidHeap)
        {
            for (int i = 0; i < 4096; i++)
                metadata.GetOrAddGuid(Guid.NewGuid());
        }

        metadata.AddAssembly(
            metadata.GetOrAddString("RuntimeAsyncBudget"),
            new Version(1, 0, 0, 0),
            culture: default,
            publicKey: default,
            flags: default,
            hashAlgorithm: default);

        AssemblyName coreLibrary =
            typeof(AsyncStateMachineAttribute).Assembly.GetName();
        AssemblyReferenceHandle coreReference =
            metadata.AddAssemblyReference(
                metadata.GetOrAddString(coreLibrary.Name!),
                coreLibrary.Version!,
                culture: default,
                publicKeyOrToken:
                    metadata.GetOrAddBlob(
                        coreLibrary.GetPublicKeyToken()!),
                flags: default,
                hashValue: default);
        TypeReferenceHandle systemType =
            metadata.AddTypeReference(
                coreReference,
                metadata.GetOrAddString("System"),
                metadata.GetOrAddString("Type"));

        var methodSignature = new BlobBuilder();
        new BlobEncoder(methodSignature)
            .MethodSignature(isInstanceMethod: false)
            .Parameters(
                parameterCount: 0,
                returnType => returnType.Void(),
                parameters => { });
        BlobHandle methodSignatureHandle =
            metadata.GetOrAddBlob(methodSignature);

        metadata.AddTypeDefinition(
            attributes: default,
            @namespace: default,
            name: metadata.GetOrAddString("<Module>"),
            baseType: default,
            fieldList:
                MetadataTokens.FieldDefinitionHandle(1),
            methodList:
                MetadataTokens.MethodDefinitionHandle(1));
        TypeDefinitionHandle owner =
            metadata.AddTypeDefinition(
                TypeAttributes.Public
                    | TypeAttributes.Abstract
                    | TypeAttributes.Sealed,
                metadata.GetOrAddString("Fixtures"),
                metadata.GetOrAddString("Owner"),
                baseType: default,
                fieldList:
                    MetadataTokens.FieldDefinitionHandle(1),
                methodList:
                    MetadataTokens.MethodDefinitionHandle(1));
        TypeDefinitionHandle machine =
            metadata.AddTypeDefinition(
                TypeAttributes.NestedPrivate
                    | TypeAttributes.Sealed,
                @namespace: default,
                name: metadata.GetOrAddString("Machine"),
                baseType: default,
                fieldList:
                    MetadataTokens.FieldDefinitionHandle(1),
                methodList:
                    MetadataTokens.MethodDefinitionHandle(2));
        metadata.AddNestedType(machine, owner);

        var methodBodies = new BlobBuilder();
        var instructions = new BlobBuilder();
        var instructionEncoder = new InstructionEncoder(
            instructions,
            new ControlFlowBuilder());
        instructionEncoder.OpCode(ILOpCode.Ret);
        int bodyOffset =
            new MethodBodyStreamEncoder(methodBodies)
                .AddMethodBody(
                    instructionEncoder,
                    maxStack: 0);

        MethodDefinitionHandle kickoff =
            metadata.AddMethodDefinition(
                MethodAttributes.Public
                    | MethodAttributes.Static,
                MethodImplAttributes.IL,
                metadata.GetOrAddString("Kickoff"),
                methodSignatureHandle,
                bodyOffset,
                MetadataTokens.ParameterHandle(1));

        for (int i = 0; i < attributeCount; i++)
        {
            TypeReferenceHandle attributeType =
                metadata.AddTypeReference(
                    coreReference,
                    metadata.GetOrAddString(
                        "System.Runtime.CompilerServices"),
                    metadata.GetOrAddString(
                        nameof(AsyncStateMachineAttribute)));
            MemberReferenceHandle constructor =
                metadata.AddMemberReference(
                    attributeType,
                    metadata.GetOrAddString(".ctor"),
                    metadata.GetOrAddBlob(
                        BuildSystemTypeConstructorSignature(
                            systemType)));

            var value = new BlobBuilder();
            value.WriteUInt16(1);
            value.WriteSerializedString(
                "Fixtures.Owner+Machine");
            value.WriteUInt16(0);
            metadata.AddCustomAttribute(
                kickoff,
                constructor,
                metadata.GetOrAddBlob(value));
        }

        var pe = new ManagedPEBuilder(
            PEHeaderBuilder.CreateLibraryHeader(),
            new MetadataRootBuilder(
                metadata,
                suppressValidation: true),
            methodBodies,
            flags: CorFlags.ILOnly);
        var imageBuilder = new BlobBuilder();
        pe.Serialize(imageBuilder);
        byte[] image = imageBuilder.ToArray();

        MarkMethodRuntimeAsync(image, kickoff);
        return image;
    }

    static BlobBuilder BuildSystemTypeConstructorSignature(
        TypeReferenceHandle systemType)
    {
        var signature = new BlobBuilder();
        new BlobEncoder(signature)
            .MethodSignature(isInstanceMethod: true)
            .Parameters(
                parameterCount: 1,
                returnType => returnType.Void(),
                parameters => parameters
                    .AddParameter()
                    .Type()
                    .Type(
                        systemType,
                        isValueType: false));
        return signature;
    }

    static void MarkMethodRuntimeAsync(
        byte[] image,
        MethodDefinitionHandle method)
    {
        int implFlagsOffset;
        using (var pe = new PEReader(
            new MemoryStream(
                image,
                writable: false)))
        {
            MetadataReader reader = pe.GetMetadataReader();
            implFlagsOffset =
                pe.PEHeaders.MetadataStartOffset
                + reader.GetTableMetadataOffset(
                    TableIndex.MethodDef)
                + (MetadataTokens.GetRowNumber(method) - 1)
                    * reader.GetTableRowSize(
                        TableIndex.MethodDef)
                + sizeof(int);
        }

        ushort implFlags =
            BinaryPrimitives.ReadUInt16LittleEndian(
                image.AsSpan(
                    implFlagsOffset,
                    sizeof(ushort)));
        BinaryPrimitives.WriteUInt16LittleEndian(
            image.AsSpan(
                implFlagsOffset,
                sizeof(ushort)),
            (ushort)(implFlags | 0x2000));
    }

    static void SetModuleMvid(byte[] image, int mvidIndex)
    {
        int mvidOffset;
        int guidIndexSize;
        using (var pe = new PEReader(
            new MemoryStream(
                image,
                writable: false)))
        {
            MetadataReader reader = pe.GetMetadataReader();
            int stringIndexSize =
                reader.GetHeapSize(HeapIndex.String) > ushort.MaxValue
                    ? sizeof(int)
                    : sizeof(ushort);
            guidIndexSize =
                reader.GetHeapSize(HeapIndex.Guid) > ushort.MaxValue
                    ? sizeof(int)
                    : sizeof(ushort);
            mvidOffset =
                pe.PEHeaders.MetadataStartOffset
                + reader.GetTableMetadataOffset(TableIndex.Module)
                + sizeof(ushort)
                + stringIndexSize;
        }

        if (guidIndexSize == sizeof(ushort))
        {
            BinaryPrimitives.WriteUInt16LittleEndian(
                image.AsSpan(mvidOffset, guidIndexSize),
                checked((ushort)mvidIndex));
        }
        else
        {
            BinaryPrimitives.WriteInt32LittleEndian(
                image.AsSpan(mvidOffset, guidIndexSize),
                mvidIndex);
        }
    }
}
