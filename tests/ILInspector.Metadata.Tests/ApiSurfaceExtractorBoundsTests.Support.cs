using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

using Inspector.Findings;
using ILInspector.Metadata;

namespace ILInspector.Metadata.Tests;

public sealed partial class ApiSurfaceExtractorBoundsTests
{
    static ApiSurface Unbounded()
    {
        using var stream = File.OpenRead(SelfPath);
        using var peReader = new PEReader(stream);
        return ApiSurfaceExtractor.Extract(peReader, ApiSurfaceExtractionScope.Public);
    }

    static ApiSurface Extracted(ApiSurfaceExtractionBounds bounds)
        => Assert.IsType<ApiSurfaceExtractionResult.Extracted>(Extract(bounds)).Surface;

    static ApiSurfaceExtractionResult Extract(
        ApiSurfaceExtractionBounds bounds,
        bool typesOnly = false)
    {
        using var stream = File.OpenRead(SelfPath);
        using var peReader = new PEReader(stream);
        return ApiSurfaceExtractor.ExtractBounded(
            peReader,
            ApiSurfaceExtractionScope.Public,
            bounds,
            typesOnly);
    }

    static ApiSurfaceExtractionResult Extract(
        byte[] image,
        ApiSurfaceExtractionBounds bounds)
    {
        using var stream = new MemoryStream(image, writable: false);
        using var peReader = new PEReader(stream);
        return ApiSurfaceExtractor.ExtractBounded(
            peReader,
            ApiSurfaceExtractionScope.Public,
            bounds);
    }

    static void AssertRejectedSignatureDoesNotAmplify(byte[] image)
    {
        using var stream = new MemoryStream(image, writable: false);
        using var peReader = new PEReader(stream);
        long before = GC.GetAllocatedBytesForCurrentThread();

        ApiSurfaceExtractionResult result = ApiSurfaceExtractor.ExtractBounded(
            peReader,
            ApiSurfaceExtractionScope.Public,
            new ApiSurfaceExtractionBounds(
                maxTypes: 100_000,
                maxMembers: 1_000_000,
                maxInspectionFailures: 1_024,
                maxTypeForwarders: 100_000,
                maxMetadataRows: 250_000,
                maxRetainedTextCharacters: 8_000_000));

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        var extracted = Assert.IsType<ApiSurfaceExtractionResult.Extracted>(result);
        Assert.True(
            allocated < 64L * 1024 * 1024,
            $"bounded extraction allocated {allocated:N0} bytes");
        ApiMember field = Assert.Single(
            extracted.Surface.Types.SelectMany(type => type.Members),
            member => member.Name == "Value");
        Assert.Equal(SignatureDecodeStatus.Degraded, field.SignatureDecodeStatus);
        Assert.True(
            (field.ReturnType?.Length ?? 0) < 64,
            $"rejected field still retained {field.ReturnType?.Length:N0} characters");
    }

    static void AssertTextAmplificationIsBounded(byte[] image)
    {
        using var stream = new MemoryStream(image, writable: false);
        using var peReader = new PEReader(stream);
        long before = GC.GetAllocatedBytesForCurrentThread();

        ApiSurfaceExtractionResult result = ApiSurfaceExtractor.ExtractBounded(
            peReader,
            ApiSurfaceExtractionScope.Public,
            new ApiSurfaceExtractionBounds(
                maxTypes: 100_000,
                maxMembers: 1_000_000,
                maxInspectionFailures: 1_024,
                maxTypeForwarders: 100_000,
                maxMetadataRows: 250_000,
                maxRetainedTextCharacters: 8_000_000));

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        var exceeded = Assert.IsType<ApiSurfaceExtractionResult.Exceeded>(result);
        Assert.Equal(
            ApiSurfaceExtractionBound.RetainedTextCharacters,
            exceeded.Bound);
        Assert.True(
            allocated < 64L * 1024 * 1024,
            $"bounded extraction allocated {allocated:N0} bytes");
    }

    static void AssertRefusedAttributeDoesNotAmplify(byte[] image)
    {
        using var stream = new MemoryStream(image, writable: false);
        using var peReader = new PEReader(stream);
        long before = GC.GetAllocatedBytesForCurrentThread();

        ApiSurfaceExtractionResult result = ApiSurfaceExtractor.ExtractBounded(
            peReader,
            ApiSurfaceExtractionScope.Public,
            new ApiSurfaceExtractionBounds(
                maxTypes: 100_000,
                maxMembers: 1_000_000,
                maxInspectionFailures: 1_024,
                maxTypeForwarders: 100_000,
                maxMetadataRows: 250_000,
                maxRetainedTextCharacters: 8_000_000));

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        var extracted = Assert.IsType<ApiSurfaceExtractionResult.Extracted>(result);
        Assert.NotEmpty(extracted.Surface.Types);
        Assert.True(
            allocated < 64L * 1024 * 1024,
            $"bounded extraction allocated {allocated:N0} bytes");
    }

    static byte[] BuildRepeatedLongMethodNameImage(
        int methodCount,
        int nameLength,
        string prefix = "")
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            0,
            metadata.GetOrAddString("Amplification.dll"),
            metadata.GetOrAddGuid(Guid.NewGuid()),
            default,
            default);
        metadata.AddAssembly(
            metadata.GetOrAddString("Amplification"),
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
            TypeAttributes.Public | TypeAttributes.Abstract,
            metadata.GetOrAddString("Samples"),
            metadata.GetOrAddString("Amplifier"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        var signature = new BlobBuilder();
        new BlobEncoder(signature).MethodSignature().Parameters(
            0,
            returnType => returnType.Void(),
            _ => { });
        BlobHandle signatureHandle = metadata.GetOrAddBlob(signature);
        StringHandle name =
            metadata.GetOrAddString(
                prefix + new string('M', nameLength - prefix.Length));
        for (int index = 0; index < methodCount; index++)
        {
            metadata.AddMethodDefinition(
                MethodAttributes.Public
                    | MethodAttributes.Static
                    | MethodAttributes.Abstract,
                MethodImplAttributes.IL,
                name,
                signatureHandle,
                bodyOffset: -1,
                MetadataTokens.ParameterHandle(1));
        }

        var pe = new ManagedPEBuilder(
            PEHeaderBuilder.CreateLibraryHeader(),
            new MetadataRootBuilder(metadata, suppressValidation: true),
            new BlobBuilder(),
            flags: CorFlags.ILOnly);
        var image = new BlobBuilder();
        pe.Serialize(image);
        return image.ToArray();
    }

    static byte[] BuildRepeatedLongFieldNameImage(
        int fieldCount,
        int nameLength)
    {
        var metadata = Metadata("FieldAmplification");
        var fieldSignature = new BlobBuilder();
        new BlobEncoder(fieldSignature).FieldSignature().Int32();
        BlobHandle signatureHandle = metadata.GetOrAddBlob(fieldSignature);
        StringHandle name = metadata.GetOrAddString(
            "<" + new string('F', nameLength - 1));
        for (int index = 0; index < fieldCount; index++)
        {
            metadata.AddFieldDefinition(
                FieldAttributes.Public,
                name,
                signatureHandle);
        }
        AddModuleAndPublicType(metadata, "Amplifier");
        return Serialize(metadata);
    }

    static byte[] BuildRepeatedLongGenericConstraintNameImage(
        int parameterCount,
        int nameLength)
    {
        MetadataBuilder metadata = Metadata(
            $"RepeatedLongConstraint{Guid.NewGuid():N}");
        TypeDefinitionHandle type = AddModuleAndPublicType(
            metadata,
            $"ConstraintCarrier`{parameterCount}");
        TypeReferenceHandle constraint = metadata.AddTypeReference(
            resolutionScope: default,
            @namespace: metadata.GetOrAddString("N"),
            name: metadata.GetOrAddString(new string('C', nameLength)));

        for (int i = 0; i < parameterCount; i++)
        {
            GenericParameterHandle parameter =
                metadata.AddGenericParameter(
                    type,
                    GenericParameterAttributes.None,
                    metadata.GetOrAddString($"T{i}"),
                    index: i);
            metadata.AddGenericParameterConstraint(
                parameter,
                constraint);
        }

        return Serialize(metadata);
    }

    static byte[] BuildSameModuleTypeReferenceDefinitionNameWorkImage(
        int definitionCount,
        int nameLength)
    {
        var metadata = new MetadataBuilder();
        ModuleDefinitionHandle module = metadata.AddModule(
            0,
            metadata.GetOrAddString("LocalReferenceWork.dll"),
            metadata.GetOrAddGuid(Guid.NewGuid()),
            default,
            default);
        metadata.AddAssembly(
            metadata.GetOrAddString("LocalReferenceWork"),
            new Version(1, 0, 0, 0),
            default,
            default,
            default,
            default);
        metadata.AddTypeReference(
            module,
            metadata.GetOrAddString("N"),
            metadata.GetOrAddString("Referenced"));
        MethodDefinitionHandle getter = metadata.AddMethodDefinition(
            MethodAttributes.Public
            | MethodAttributes.Abstract
            | MethodAttributes.Virtual
            | MethodAttributes.NewSlot
            | MethodAttributes.HideBySig
            | MethodAttributes.SpecialName,
            MethodImplAttributes.IL,
            metadata.GetOrAddString("get_Value"),
            metadata.GetOrAddBlob((byte[])[0x20, 0x00, 0x12, 0x05]),
            bodyOffset: -1,
            MetadataTokens.ParameterHandle(1));
        metadata.AddTypeDefinition(
            TypeAttributes.NotPublic,
            default,
            metadata.GetOrAddString("<Module>"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            getter);
        TypeDefinitionHandle target = metadata.AddTypeDefinition(
            TypeAttributes.Public | TypeAttributes.Abstract,
            metadata.GetOrAddString("N"),
            metadata.GetOrAddString("Target"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            getter);
        TypeDefinitionHandle referenced = metadata.AddTypeDefinition(
            TypeAttributes.NotPublic,
            metadata.GetOrAddString("N"),
            metadata.GetOrAddString("Referenced"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(2));
        metadata.AddGenericParameter(
            referenced,
            GenericParameterAttributes.None,
            metadata.GetOrAddString("T"),
            index: 0);
        StringHandle longName =
            metadata.GetOrAddString(new string('X', nameLength));
        for (int i = 0; i < definitionCount; i++)
        {
            metadata.AddTypeDefinition(
                TypeAttributes.NotPublic,
                metadata.GetOrAddString("Fillers"),
                longName,
                default,
                MetadataTokens.FieldDefinitionHandle(1),
                MetadataTokens.MethodDefinitionHandle(2));
        }
        PropertyDefinitionHandle property = metadata.AddProperty(
            PropertyAttributes.None,
            metadata.GetOrAddString("Value"),
            metadata.GetOrAddBlob((byte[])[0x28, 0x00, 0x12, 0x05]));
        metadata.AddPropertyMap(target, property);
        metadata.AddMethodSemantics(
            property,
            MethodSemanticsAttributes.Getter,
            getter);
        return Serialize(metadata);
    }

    static byte[] BuildRepeatedLongVisibilityAttributeTypeNameImage(
        int methodCount,
        int nameLength)
    {
        var metadata = Metadata("AttributeNameAmplification");
        AssemblyReferenceHandle runtime = metadata.AddAssemblyReference(
            metadata.GetOrAddString("System.Runtime"),
            new Version(11, 0, 0, 0),
            default,
            default,
            default,
            default);
        TypeReferenceHandle longAttributeType = metadata.AddTypeReference(
            runtime,
            metadata.GetOrAddString("Samples"),
            metadata.GetOrAddString(
                new string('A', nameLength - "Attribute".Length)
                    + "Attribute"));
        TypeReferenceHandle editorBrowsableType = metadata.AddTypeReference(
            runtime,
            metadata.GetOrAddString("System.ComponentModel"),
            metadata.GetOrAddString("EditorBrowsableAttribute"));
        var emptyConstructorSignature = new BlobBuilder();
        new BlobEncoder(emptyConstructorSignature).MethodSignature(
            SignatureCallingConvention.Default,
            genericParameterCount: 0,
            isInstanceMethod: true).Parameters(
                0,
                returnType => returnType.Void(),
                _ => { });
        MemberReferenceHandle longConstructor = metadata.AddMemberReference(
            longAttributeType,
            metadata.GetOrAddString(".ctor"),
            metadata.GetOrAddBlob(emptyConstructorSignature));
        var editorBrowsableConstructorSignature = new BlobBuilder();
        new BlobEncoder(editorBrowsableConstructorSignature).MethodSignature(
            SignatureCallingConvention.Default,
            genericParameterCount: 0,
            isInstanceMethod: true).Parameters(
                1,
                returnType => returnType.Void(),
                parameters => parameters.AddParameter().Type().Int32());
        MemberReferenceHandle editorBrowsableConstructor =
            metadata.AddMemberReference(
                editorBrowsableType,
                metadata.GetOrAddString(".ctor"),
                metadata.GetOrAddBlob(editorBrowsableConstructorSignature));
        AddModuleAndPublicType(metadata, "Amplifier");
        var methodSignature = new BlobBuilder();
        new BlobEncoder(methodSignature).MethodSignature().Parameters(
            0,
            returnType => returnType.Void(),
            _ => { });
        BlobHandle methodSignatureHandle = metadata.GetOrAddBlob(methodSignature);
        var emptyValue = new BlobBuilder();
        emptyValue.WriteUInt16(1);
        emptyValue.WriteUInt16(0);
        BlobHandle emptyValueHandle = metadata.GetOrAddBlob(emptyValue);
        var hiddenValue = new BlobBuilder();
        hiddenValue.WriteUInt16(1);
        hiddenValue.WriteInt32(1);
        hiddenValue.WriteUInt16(0);
        BlobHandle hiddenValueHandle = metadata.GetOrAddBlob(hiddenValue);
        for (int index = 0; index < methodCount; index++)
        {
            MethodDefinitionHandle method = metadata.AddMethodDefinition(
                MethodAttributes.Public
                    | MethodAttributes.Static
                    | MethodAttributes.Abstract,
                MethodImplAttributes.IL,
                metadata.GetOrAddString($"Method{index}"),
                methodSignatureHandle,
                bodyOffset: -1,
                MetadataTokens.ParameterHandle(1));
            metadata.AddCustomAttribute(
                method,
                longConstructor,
                emptyValueHandle);
            metadata.AddCustomAttribute(
                method,
                editorBrowsableConstructor,
                hiddenValueHandle);
        }
        return Serialize(metadata);
    }

    static byte[] BuildWideSignatureImage(int parameterCount, int nameLength)
    {
        var metadata = Metadata("Wide");
        AssemblyReferenceHandle assembly = metadata.AddAssemblyReference(
            metadata.GetOrAddString("Other"),
            new Version(1, 0, 0, 0),
            default,
            default,
            default,
            default);
        TypeReferenceHandle parameterType = metadata.AddTypeReference(
            assembly,
            metadata.GetOrAddString("Contracts"),
            metadata.GetOrAddString(new string('P', nameLength)));
        AddModuleAndPublicType(metadata, "Wide");
        var signature = new BlobBuilder();
        new BlobEncoder(signature).MethodSignature().Parameters(
            parameterCount,
            returnType => returnType.Void(),
            parameters =>
            {
                for (int index = 0; index < parameterCount; index++)
                    parameters.AddParameter().Type().Type(parameterType, isValueType: false);
            });
        metadata.AddMethodDefinition(
            MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.Abstract,
            MethodImplAttributes.IL,
            metadata.GetOrAddString("Wide"),
            metadata.GetOrAddBlob(signature),
            bodyOffset: -1,
            MetadataTokens.ParameterHandle(1));
        return Serialize(metadata);
    }

    static byte[] BuildLargeAccessorReturnAttributeImage(
        AccessorOwner owner,
        int valueLength)
    {
        var metadata = Metadata($"{owner}ReturnAttribute");
        AssemblyReferenceHandle assembly = metadata.AddAssemblyReference(
            metadata.GetOrAddString("Other"),
            new Version(1, 0, 0, 0),
            default,
            default,
            default,
            default);
        TypeReferenceHandle attributeType = metadata.AddTypeReference(
            assembly,
            metadata.GetOrAddString("System"),
            metadata.GetOrAddString("SampleAttribute"));
        var constructorSignature = new BlobBuilder();
        new BlobEncoder(constructorSignature).MethodSignature(
            SignatureCallingConvention.Default,
            genericParameterCount: 0,
            isInstanceMethod: true).Parameters(
                1,
                returnType => returnType.Void(),
                parameters => parameters.AddParameter().Type().String());
        MemberReferenceHandle constructor = metadata.AddMemberReference(
            attributeType,
            metadata.GetOrAddString(".ctor"),
            metadata.GetOrAddBlob(constructorSignature));
        TypeDefinitionHandle type = AddModuleAndPublicType(
            metadata,
            $"{owner}ReturnAttribute");
        ParameterHandle returnParameter = metadata.AddParameter(
            ParameterAttributes.None,
            default,
            sequenceNumber: 0);
        var accessorSignature = new BlobBuilder();
        new BlobEncoder(accessorSignature).MethodSignature(
            SignatureCallingConvention.Default,
            genericParameterCount: 0,
            isInstanceMethod: true).Parameters(
                0,
                returnType =>
                {
                    if (owner == AccessorOwner.Property)
                        returnType.Type().Int32();
                    else
                        returnType.Void();
                },
                _ => { });
        MethodDefinitionHandle accessor = metadata.AddMethodDefinition(
            MethodAttributes.Public
                | MethodAttributes.Abstract
                | MethodAttributes.Virtual,
            MethodImplAttributes.IL,
            metadata.GetOrAddString(
                owner == AccessorOwner.Property ? "get_Value" : "add_Changed"),
            metadata.GetOrAddBlob(accessorSignature),
            bodyOffset: -1,
            returnParameter);
        if (owner == AccessorOwner.Property)
        {
            var propertySignature = new BlobBuilder();
            new BlobEncoder(propertySignature).PropertySignature(
                isInstanceProperty: true).Parameters(
                    0,
                    returnType => returnType.Type().Int32(),
                    _ => { });
            PropertyDefinitionHandle property = metadata.AddProperty(
                PropertyAttributes.None,
                metadata.GetOrAddString("Value"),
                metadata.GetOrAddBlob(propertySignature));
            metadata.AddPropertyMap(type, property);
            metadata.AddMethodSemantics(
                property,
                MethodSemanticsAttributes.Getter,
                accessor);
        }
        else
        {
            TypeReferenceHandle eventType = metadata.AddTypeReference(
                assembly,
                metadata.GetOrAddString("System"),
                metadata.GetOrAddString("EventHandler"));
            EventDefinitionHandle @event = metadata.AddEvent(
                EventAttributes.None,
                metadata.GetOrAddString("Changed"),
                eventType);
            metadata.AddEventMap(type, @event);
            metadata.AddMethodSemantics(
                @event,
                MethodSemanticsAttributes.Adder,
                accessor);
        }
        var value = new BlobBuilder(valueLength + 16);
        value.WriteUInt16(1);
        value.WriteCompressedInteger(valueLength);
        for (int index = 0; index < valueLength; index++)
            value.WriteByte((byte)'"');
        value.WriteUInt16(0);
        metadata.AddCustomAttribute(
            returnParameter,
            constructor,
            metadata.GetOrAddBlob(value));
        return Serialize(metadata);
    }

    static byte[] BuildInterfaceFloodImage(
        int interfaceCount,
        int nameLength,
        int typeCount = 1)
    {
        var metadata = Metadata("Interfaces");
        AssemblyReferenceHandle assembly = metadata.AddAssemblyReference(
            metadata.GetOrAddString("Other"),
            new Version(1, 0, 0, 0),
            default,
            default,
            default,
            default);
        TypeReferenceHandle interfaceType = metadata.AddTypeReference(
            assembly,
            metadata.GetOrAddString("Contracts"),
            metadata.GetOrAddString(new string('I', nameLength)));
        TypeDefinitionHandle type = AddModuleAndPublicType(
            metadata,
            "Interfaces",
            TypeAttributes.Public | TypeAttributes.Interface | TypeAttributes.Abstract);
        AddImplementations(type);
        for (int typeIndex = 1; typeIndex < typeCount; typeIndex++)
        {
            type = metadata.AddTypeDefinition(
                TypeAttributes.Public
                    | TypeAttributes.Interface
                    | TypeAttributes.Abstract,
                metadata.GetOrAddString("Samples"),
                metadata.GetOrAddString($"Interfaces{typeIndex}"),
                default,
                MetadataTokens.FieldDefinitionHandle(1),
                MetadataTokens.MethodDefinitionHandle(1));
            AddImplementations(type);
        }
        return Serialize(metadata);

        void AddImplementations(TypeDefinitionHandle owner)
        {
            for (int index = 0; index < interfaceCount; index++)
                metadata.AddInterfaceImplementation(owner, interfaceType);
        }
    }

    static byte[] BuildWideTypeSpecImage(
        WideTypeSpecUse use,
        int argumentCount,
        int nameLength)
    {
        var metadata = Metadata($"Wide{use}");
        AssemblyReferenceHandle assembly = metadata.AddAssemblyReference(
            metadata.GetOrAddString("Other"),
            new Version(1, 0, 0, 0),
            default,
            default,
            default,
            default);
        TypeReferenceHandle genericType = metadata.AddTypeReference(
            assembly,
            metadata.GetOrAddString("Contracts"),
            metadata.GetOrAddString($"Generic`{argumentCount}"));
        TypeReferenceHandle argumentType = metadata.AddTypeReference(
            assembly,
            metadata.GetOrAddString("Contracts"),
            metadata.GetOrAddString(new string('A', nameLength)));
        var typeSpecSignature = new BlobBuilder();
        WriteWideGenericType(
            typeSpecSignature,
            genericType,
            argumentType,
            argumentCount);
        TypeSpecificationHandle typeSpec =
            metadata.AddTypeSpecification(metadata.GetOrAddBlob(typeSpecSignature));

        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        TypeDefinitionHandle type = metadata.AddTypeDefinition(
            TypeAttributes.Public | TypeAttributes.Abstract,
            metadata.GetOrAddString("Samples"),
            metadata.GetOrAddString("Wide"),
            use == WideTypeSpecUse.BaseType ? typeSpec : default(EntityHandle),
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));

        switch (use)
        {
            case WideTypeSpecUse.Field:
                var fieldSignature = new BlobBuilder();
                fieldSignature.WriteByte(0x06);
                WriteWideGenericType(
                    fieldSignature,
                    genericType,
                    argumentType,
                    argumentCount);
                metadata.AddFieldDefinition(
                    FieldAttributes.Public | FieldAttributes.Static,
                    metadata.GetOrAddString("Value"),
                    metadata.GetOrAddBlob(fieldSignature));
                break;
            case WideTypeSpecUse.Interface:
                metadata.AddInterfaceImplementation(type, typeSpec);
                break;
            case WideTypeSpecUse.Event:
                var accessorSignature = new BlobBuilder();
                new BlobEncoder(accessorSignature).MethodSignature().Parameters(
                    0,
                    returnType => returnType.Void(),
                    _ => { });
                MethodDefinitionHandle accessor = metadata.AddMethodDefinition(
                    MethodAttributes.Public
                        | MethodAttributes.Abstract
                        | MethodAttributes.Virtual,
                    MethodImplAttributes.IL,
                    metadata.GetOrAddString("add_Changed"),
                    metadata.GetOrAddBlob(accessorSignature),
                    bodyOffset: -1,
                    MetadataTokens.ParameterHandle(1));
                EventDefinitionHandle @event = metadata.AddEvent(
                    EventAttributes.None,
                    metadata.GetOrAddString("Changed"),
                    typeSpec);
                metadata.AddEventMap(type, @event);
                metadata.AddMethodSemantics(
                    @event,
                    MethodSemanticsAttributes.Adder,
                    accessor);
                break;
            case WideTypeSpecUse.GenericConstraint:
                GenericParameterHandle parameter = metadata.AddGenericParameter(
                    type,
                    GenericParameterAttributes.None,
                    metadata.GetOrAddString("T"),
                    index: 0);
                metadata.AddGenericParameterConstraint(parameter, typeSpec);
                break;
        }

        return Serialize(metadata);
    }

    static void WriteWideGenericType(
        BlobBuilder signature,
        TypeReferenceHandle genericType,
        TypeReferenceHandle argumentType,
        int argumentCount)
    {
        signature.WriteByte(0x15);
        signature.WriteByte(0x12);
        signature.WriteCompressedInteger(
            MetadataTokens.GetRowNumber(genericType) << 2 | 1);
        signature.WriteCompressedInteger(argumentCount);
        int argumentCode = MetadataTokens.GetRowNumber(argumentType) << 2 | 1;
        for (int index = 0; index < argumentCount; index++)
        {
            signature.WriteByte(0x12);
            signature.WriteCompressedInteger(argumentCode);
        }
    }

    static byte[] BuildLargeAttributeImage(int valueLength)
    {
        var metadata = Metadata("LargeAttribute");
        AssemblyReferenceHandle assembly = metadata.AddAssemblyReference(
            metadata.GetOrAddString("Other"),
            new Version(1, 0, 0, 0),
            default,
            default,
            default,
            default);
        TypeReferenceHandle attributeType = metadata.AddTypeReference(
            assembly,
            metadata.GetOrAddString("System"),
            metadata.GetOrAddString("SampleAttribute"));
        var constructorSignature = new BlobBuilder();
        new BlobEncoder(constructorSignature).MethodSignature(
            SignatureCallingConvention.Default,
            genericParameterCount: 0,
            isInstanceMethod: true).Parameters(
                1,
                returnType => returnType.Void(),
                parameters => parameters.AddParameter().Type().String());
        MemberReferenceHandle constructor = metadata.AddMemberReference(
            attributeType,
            metadata.GetOrAddString(".ctor"),
            metadata.GetOrAddBlob(constructorSignature));
        TypeDefinitionHandle type = AddModuleAndPublicType(metadata, "Attributed");
        var value = new BlobBuilder(valueLength + 16);
        value.WriteUInt16(1);
        value.WriteCompressedInteger(valueLength);
        for (int index = 0; index < valueLength; index++)
            value.WriteByte((byte)'"');
        value.WriteUInt16(0);
        metadata.AddCustomAttribute(
            type,
            constructor,
            metadata.GetOrAddBlob(value));
        return Serialize(metadata);
    }

    static byte[] BuildNestedTypeSpecFieldImage(int depth, int nameLength)
    {
        var metadata = Metadata("Nested");
        AssemblyReferenceHandle assembly = metadata.AddAssemblyReference(
            metadata.GetOrAddString("Other"),
            new Version(1, 0, 0, 0),
            default,
            default,
            default,
            default);
        TypeReferenceHandle head = metadata.AddTypeReference(
            assembly,
            metadata.GetOrAddString("Contracts"),
            metadata.GetOrAddString("Node`1"));
        TypeReferenceHandle argument = metadata.AddTypeReference(
            assembly,
            metadata.GetOrAddString("Contracts"),
            metadata.GetOrAddString(new string('A', nameLength)));
        AddModuleAndPublicType(metadata, "Nested");
        var fieldSignature = new BlobBuilder();
        fieldSignature.WriteByte(0x06);
        for (int index = 0; index < depth; index++)
            fieldSignature.WriteByte(0x15);
        fieldSignature.WriteByte(0x12);
        WriteTypeDefOrRef(fieldSignature, head);
        for (int index = 0; index < depth; index++)
        {
            fieldSignature.WriteCompressedInteger(1);
            fieldSignature.WriteByte(0x12);
            WriteTypeDefOrRef(fieldSignature, argument);
        }
        metadata.AddFieldDefinition(
            FieldAttributes.Public,
            metadata.GetOrAddString("Value"),
            metadata.GetOrAddBlob(fieldSignature));
        return Serialize(metadata);
    }

    static byte[] BuildArgumentNestedTypeSpecFieldImage(int depth, int nameLength)
    {
        var metadata = Metadata("ArgumentNested");
        AssemblyReferenceHandle assembly = metadata.AddAssemblyReference(
            metadata.GetOrAddString("Other"),
            new Version(1, 0, 0, 0),
            default,
            default,
            default,
            default);
        TypeReferenceHandle head = metadata.AddTypeReference(
            assembly,
            metadata.GetOrAddString("Contracts"),
            metadata.GetOrAddString($"{new string('H', nameLength)}`1"));
        AddModuleAndPublicType(metadata, "ArgumentNested");
        var fieldSignature = new BlobBuilder();
        fieldSignature.WriteByte(0x06);
        for (int index = 0; index < depth; index++)
        {
            fieldSignature.WriteByte(0x15);
            fieldSignature.WriteByte(0x12);
            WriteTypeDefOrRef(fieldSignature, head);
            fieldSignature.WriteCompressedInteger(1);
        }
        fieldSignature.WriteByte(0x08);
        metadata.AddFieldDefinition(
            FieldAttributes.Public,
            metadata.GetOrAddString("Value"),
            metadata.GetOrAddBlob(fieldSignature));
        return Serialize(metadata);
    }

    static byte[] BuildNestedArrayFieldImage(int depth, int rank)
    {
        var metadata = Metadata("NestedArray");
        AddModuleAndPublicType(metadata, "NestedArray");
        var fieldSignature = new BlobBuilder();
        fieldSignature.WriteByte(0x06);
        for (int index = 0; index < depth; index++)
            fieldSignature.WriteByte(0x14);
        fieldSignature.WriteByte(0x08);
        for (int index = 0; index < depth; index++)
        {
            fieldSignature.WriteCompressedInteger(rank);
            fieldSignature.WriteCompressedInteger(0);
            fieldSignature.WriteCompressedInteger(0);
        }
        metadata.AddFieldDefinition(
            FieldAttributes.Public,
            metadata.GetOrAddString("Value"),
            metadata.GetOrAddBlob(fieldSignature));
        return Serialize(metadata);
    }

    static byte[] BuildNestedTypeNameChainImage(int depth, int nameLength)
    {
        var metadata = Metadata("NestedNames");
        StringHandle sharedName =
            metadata.GetOrAddString(new string('N', nameLength));
        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        var handles = new TypeDefinitionHandle[depth];
        for (int index = 0; index < depth; index++)
        {
            handles[index] = metadata.AddTypeDefinition(
                index == 0 ? TypeAttributes.Public : TypeAttributes.NestedPublic,
                default,
                sharedName,
                default,
                MetadataTokens.FieldDefinitionHandle(1),
                MetadataTokens.MethodDefinitionHandle(1));
        }
        for (int index = 1; index < handles.Length; index++)
            metadata.AddNestedType(handles[index], handles[index - 1]);
        return Serialize(metadata);
    }

    static byte[] BuildRepeatedNestedGenericTypesImage(
        int typeCount,
        int depth,
        int nameLength,
        bool poison)
    {
        var metadata = Metadata("RepeatedNested");
        AssemblyReferenceHandle assembly = metadata.AddAssemblyReference(
            metadata.GetOrAddString("Other"),
            new Version(1, 0, 0, 0),
            default,
            default,
            default,
            default);
        TypeReferenceHandle head = metadata.AddTypeReference(
            assembly,
            metadata.GetOrAddString("Contracts"),
            metadata.GetOrAddString($"{new string('G', nameLength)}`1"));
        var fieldSignature = new BlobBuilder();
        fieldSignature.WriteByte(0x06);
        for (int index = 0; index < depth; index++)
        {
            fieldSignature.WriteByte(0x15);
            fieldSignature.WriteByte(0x12);
            WriteTypeDefOrRef(fieldSignature, head);
            fieldSignature.WriteCompressedInteger(1);
        }
        fieldSignature.WriteByte(0x0e);
        BlobHandle fieldSignatureHandle =
            metadata.GetOrAddBlob(fieldSignature);
        var poisonSignature = new BlobBuilder();
        poisonSignature.WriteByte(0x06);
        BlobHandle poisonSignatureHandle =
            metadata.GetOrAddBlob(poisonSignature);
        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        int fieldRow = 1;
        for (int index = 0; index < typeCount; index++)
        {
            metadata.AddTypeDefinition(
                TypeAttributes.Public,
                metadata.GetOrAddString("Samples"),
                metadata.GetOrAddString($"T{index}"),
                default,
                MetadataTokens.FieldDefinitionHandle(fieldRow),
                MetadataTokens.MethodDefinitionHandle(1));
            metadata.AddFieldDefinition(
                FieldAttributes.Public,
                metadata.GetOrAddString("Value"),
                fieldSignatureHandle);
            fieldRow++;
            if (poison)
            {
                metadata.AddFieldDefinition(
                    FieldAttributes.Public,
                    metadata.GetOrAddString("Poison"),
                    poisonSignatureHandle);
                fieldRow++;
            }
        }
        return Serialize(metadata);
    }

    static byte[] BuildLargeTransformArrayImage(
        TransformArrayKind kind,
        int elementCount)
    {
        var metadata = Metadata($"Large{kind}");
        AssemblyReferenceHandle runtime = metadata.AddAssemblyReference(
            metadata.GetOrAddString("System.Runtime"),
            new Version(11, 0, 0, 0),
            default,
            default,
            default,
            default);
        string attributeName = kind switch
        {
            TransformArrayKind.TupleElementNames => "TupleElementNamesAttribute",
            TransformArrayKind.Nullable => "NullableAttribute",
            TransformArrayKind.Dynamic => "DynamicAttribute",
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
        TypeReferenceHandle attributeType = metadata.AddTypeReference(
            runtime,
            metadata.GetOrAddString("System.Runtime.CompilerServices"),
            metadata.GetOrAddString(attributeName));
        var constructorSignature = new BlobBuilder();
        constructorSignature.WriteByte(0x20);
        constructorSignature.WriteCompressedInteger(1);
        constructorSignature.WriteByte(0x01);
        constructorSignature.WriteByte(0x1d);
        constructorSignature.WriteByte((byte)(kind switch
        {
            TransformArrayKind.TupleElementNames => 0x0e,
            TransformArrayKind.Nullable => 0x05,
            TransformArrayKind.Dynamic => 0x02,
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        }));
        MemberReferenceHandle constructor = metadata.AddMemberReference(
            attributeType,
            metadata.GetOrAddString(".ctor"),
            metadata.GetOrAddBlob(constructorSignature));
        AddModuleAndPublicType(metadata, "Transformed");
        var fieldSignature = new BlobBuilder();
        fieldSignature.WriteByte(0x06);
        fieldSignature.WriteByte(0x1c);
        FieldDefinitionHandle field = metadata.AddFieldDefinition(
            FieldAttributes.Public | FieldAttributes.Static,
            metadata.GetOrAddString("Value"),
            metadata.GetOrAddBlob(fieldSignature));
        var value = new BlobBuilder(elementCount + 8);
        value.WriteUInt16(1);
        value.WriteInt32(elementCount);
        byte element = kind == TransformArrayKind.TupleElementNames
            ? (byte)0xff
            : (byte)0;
        for (int index = 0; index < elementCount; index++)
            value.WriteByte(element);
        value.WriteUInt16(0);
        metadata.AddCustomAttribute(
            field,
            constructor,
            metadata.GetOrAddBlob(value));
        return Serialize(metadata);
    }

    static byte[] BuildRepeatedMethodGenericContextImage(
        int genericParameterCount,
        int nameLength,
        int methodCount)
    {
        var metadata = Metadata("GenericContext");
        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        TypeDefinitionHandle type = metadata.AddTypeDefinition(
            TypeAttributes.Public | TypeAttributes.Abstract,
            metadata.GetOrAddString("Samples"),
            metadata.GetOrAddString($"Host`{genericParameterCount}"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        StringHandle genericName =
            metadata.GetOrAddString(new string('G', nameLength));
        for (int index = 0; index < genericParameterCount; index++)
        {
            metadata.AddGenericParameter(
                type,
                GenericParameterAttributes.None,
                genericName,
                index);
        }
        var signature = new BlobBuilder();
        new BlobEncoder(signature).MethodSignature().Parameters(
            0,
            returnType => returnType.Void(),
            _ => { });
        BlobHandle signatureHandle = metadata.GetOrAddBlob(signature);
        for (int index = 0; index < methodCount; index++)
        {
            metadata.AddMethodDefinition(
                MethodAttributes.Public
                    | MethodAttributes.Static
                    | MethodAttributes.Abstract,
                MethodImplAttributes.IL,
                metadata.GetOrAddString($"M{index}"),
                signatureHandle,
                bodyOffset: -1,
                MetadataTokens.ParameterHandle(1));
        }
        return Serialize(metadata);
    }

    static byte[] BuildHugeArrayRankFieldImage(int rank)
    {
        var metadata = Metadata("HugeArray");
        AddModuleAndPublicType(metadata, "HugeArray");
        var signature = new BlobBuilder();
        signature.WriteByte(0x06);
        signature.WriteByte(0x14);
        signature.WriteByte(0x08);
        signature.WriteCompressedInteger(rank);
        signature.WriteCompressedInteger(0);
        signature.WriteCompressedInteger(0);
        metadata.AddFieldDefinition(
            FieldAttributes.Public,
            metadata.GetOrAddString("Value"),
            metadata.GetOrAddBlob(signature));
        return Serialize(metadata);
    }

    static byte[] BuildRepeatedHiddenAttributeImage(
        int typeCount,
        int blobLength)
    {
        var metadata = Metadata("Hidden");
        AssemblyReferenceHandle assembly = metadata.AddAssemblyReference(
            metadata.GetOrAddString("Other"),
            new Version(1, 0, 0, 0),
            default,
            default,
            default,
            default);
        TypeReferenceHandle attributeType = metadata.AddTypeReference(
            assembly,
            metadata.GetOrAddString("System.ComponentModel"),
            metadata.GetOrAddString("EditorBrowsableAttribute"));
        var constructorSignature = new BlobBuilder();
        new BlobEncoder(constructorSignature).MethodSignature(
            SignatureCallingConvention.Default,
            genericParameterCount: 0,
            isInstanceMethod: true).Parameters(
                1,
                returnType => returnType.Void(),
                parameters => parameters.AddParameter().Type().Int32());
        MemberReferenceHandle constructor = metadata.AddMemberReference(
            attributeType,
            metadata.GetOrAddString(".ctor"),
            metadata.GetOrAddBlob(constructorSignature));
        var value = new BlobBuilder(blobLength);
        value.WriteUInt16(1);
        value.WriteInt32(1);
        for (int index = 6; index < blobLength; index++)
            value.WriteByte(0);
        BlobHandle valueHandle = metadata.GetOrAddBlob(value);

        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        var types = new List<TypeDefinitionHandle>(typeCount);
        for (int index = 0; index < typeCount; index++)
        {
            types.Add(metadata.AddTypeDefinition(
                TypeAttributes.Public | TypeAttributes.Abstract,
                metadata.GetOrAddString("Samples"),
                metadata.GetOrAddString($"T{index}"),
                default,
                MetadataTokens.FieldDefinitionHandle(1),
                MetadataTokens.MethodDefinitionHandle(1)));
        }
        foreach (TypeDefinitionHandle type in types)
            metadata.AddCustomAttribute(type, constructor, valueHandle);
        return Serialize(metadata);
    }

    static byte[] BuildHiddenAutoPropertyImage(
        int argumentCount,
        int nameLength)
    {
        var metadata = Metadata("HiddenAuto");
        AssemblyReferenceHandle runtime = metadata.AddAssemblyReference(
            metadata.GetOrAddString("System.Runtime"),
            new Version(11, 0, 0, 0),
            default,
            default,
            default,
            default);
        TypeReferenceHandle compilerGenerated = metadata.AddTypeReference(
            runtime,
            metadata.GetOrAddString("System.Runtime.CompilerServices"),
            metadata.GetOrAddString("CompilerGeneratedAttribute"));
        var constructorSignature = new BlobBuilder();
        new BlobEncoder(constructorSignature).MethodSignature(
            SignatureCallingConvention.Default,
            genericParameterCount: 0,
            isInstanceMethod: true).Parameters(
                0,
                returnType => returnType.Void(),
                _ => { });
        MemberReferenceHandle constructor = metadata.AddMemberReference(
            compilerGenerated,
            metadata.GetOrAddString(".ctor"),
            metadata.GetOrAddBlob(constructorSignature));
        AssemblyReferenceHandle other = metadata.AddAssemblyReference(
            metadata.GetOrAddString("Other"),
            new Version(1, 0, 0, 0),
            default,
            default,
            default,
            default);
        TypeReferenceHandle genericType = metadata.AddTypeReference(
            other,
            metadata.GetOrAddString("Contracts"),
            metadata.GetOrAddString($"Generic`{argumentCount}"));
        TypeReferenceHandle argumentType = metadata.AddTypeReference(
            other,
            metadata.GetOrAddString("Contracts"),
            metadata.GetOrAddString(new string('A', nameLength)));
        TypeDefinitionHandle type = AddModuleAndPublicType(metadata, "Host");
        var getterSignature = new BlobBuilder();
        new BlobEncoder(getterSignature).MethodSignature(
            SignatureCallingConvention.Default,
            genericParameterCount: 0,
            isInstanceMethod: true).Parameters(
                0,
                returnType => returnType.Void(),
                _ => { });
        MethodDefinitionHandle getter = metadata.AddMethodDefinition(
            MethodAttributes.Private
                | MethodAttributes.Abstract
                | MethodAttributes.Virtual,
            MethodImplAttributes.IL,
            metadata.GetOrAddString("get_Hidden"),
            metadata.GetOrAddBlob(getterSignature),
            bodyOffset: -1,
            MetadataTokens.ParameterHandle(1));
        var attributeValue = new BlobBuilder();
        attributeValue.WriteUInt16(1);
        attributeValue.WriteUInt16(0);
        metadata.AddCustomAttribute(
            getter,
            constructor,
            metadata.GetOrAddBlob(attributeValue));
        var propertySignature = new BlobBuilder();
        propertySignature.WriteByte(0x28);
        propertySignature.WriteCompressedInteger(0);
        WriteWideGenericType(
            propertySignature,
            genericType,
            argumentType,
            argumentCount);
        PropertyDefinitionHandle property = metadata.AddProperty(
            PropertyAttributes.None,
            metadata.GetOrAddString("Hidden"),
            metadata.GetOrAddBlob(propertySignature));
        metadata.AddPropertyMap(type, property);
        metadata.AddMethodSemantics(
            property,
            MethodSemanticsAttributes.Getter,
            getter);
        return Serialize(metadata);
    }

    static byte[] BuildHugeParameterDefaultImage(int characterCount)
    {
        var metadata = Metadata("DefaultBomb");
        AddModuleAndPublicType(metadata, "Host");
        ParameterHandle parameter = metadata.AddParameter(
            ParameterAttributes.Optional | ParameterAttributes.HasDefault,
            metadata.GetOrAddString("value"),
            sequenceNumber: 1);
        metadata.AddConstant(parameter, new string('"', characterCount));
        var signature = new BlobBuilder();
        new BlobEncoder(signature).MethodSignature().Parameters(
            1,
            returnType => returnType.Void(),
            parameters => parameters.AddParameter().Type().String());
        metadata.AddMethodDefinition(
            MethodAttributes.Public
                | MethodAttributes.Static
                | MethodAttributes.Abstract,
            MethodImplAttributes.IL,
            metadata.GetOrAddString("M"),
            metadata.GetOrAddBlob(signature),
            bodyOffset: -1,
            parameter);
        return Serialize(metadata);
    }

    static byte[] BuildNestedEnumDefaultImage(
        int depth,
        int nameLength,
        byte enumElementType = 0x08,
        bool hideStorageSlot = false)
    {
        var metadata = Metadata("EnumDefaultBomb");
        AssemblyReferenceHandle runtime = metadata.AddAssemblyReference(
            metadata.GetOrAddString("System.Runtime"),
            new Version(11, 0, 0, 0),
            default,
            default,
            default,
            default);
        TypeReferenceHandle enumBase = metadata.AddTypeReference(
            runtime,
            metadata.GetOrAddString("System"),
            metadata.GetOrAddString("Enum"));
        MemberReferenceHandle editorBrowsableConstructor = default;
        if (hideStorageSlot)
        {
            TypeReferenceHandle editorBrowsableType = metadata.AddTypeReference(
                runtime,
                metadata.GetOrAddString("System.ComponentModel"),
                metadata.GetOrAddString("EditorBrowsableAttribute"));
            var constructorSignature = new BlobBuilder();
            new BlobEncoder(constructorSignature).MethodSignature(
                SignatureCallingConvention.Default,
                genericParameterCount: 0,
                isInstanceMethod: true).Parameters(
                    1,
                    returnType => returnType.Void(),
                    parameters => parameters.AddParameter().Type().Int32());
            editorBrowsableConstructor = metadata.AddMemberReference(
                editorBrowsableType,
                metadata.GetOrAddString(".ctor"),
                metadata.GetOrAddBlob(constructorSignature));
        }
        TypeDefinitionHandle host = AddModuleAndPublicType(metadata, "Host");
        TypeDefinitionHandle target =
            MetadataTokens.TypeDefinitionHandle(depth + 3);
        ParameterHandle parameter = metadata.AddParameter(
            ParameterAttributes.Optional | ParameterAttributes.HasDefault,
            metadata.GetOrAddString("value"),
            sequenceNumber: 1);
        metadata.AddConstant(parameter, 1);
        var methodSignature = new BlobBuilder();
        methodSignature.WriteByte(0x00);
        methodSignature.WriteCompressedInteger(1);
        methodSignature.WriteByte(0x01);
        methodSignature.WriteByte(0x11);
        WriteTypeDefOrRef(methodSignature, target);
        metadata.AddMethodDefinition(
            MethodAttributes.Public
                | MethodAttributes.Static
                | MethodAttributes.Abstract,
            MethodImplAttributes.IL,
            metadata.GetOrAddString("M"),
            metadata.GetOrAddBlob(methodSignature),
            bodyOffset: -1,
            parameter);

        TypeDefinitionHandle parent = host;
        for (int index = 0; index < depth; index++)
        {
            TypeDefinitionHandle nested = metadata.AddTypeDefinition(
                TypeAttributes.NestedPrivate
                    | TypeAttributes.Sealed,
                default,
                metadata.GetOrAddString(new string('E', nameLength)),
                enumBase,
                MetadataTokens.FieldDefinitionHandle(1),
                MetadataTokens.MethodDefinitionHandle(2));
            metadata.AddNestedType(nested, parent);
            parent = nested;
        }

        TypeDefinitionHandle actualTarget = metadata.AddTypeDefinition(
            TypeAttributes.Public | TypeAttributes.Sealed,
            metadata.GetOrAddString("Samples"),
            metadata.GetOrAddString("TargetEnum"),
            enumBase,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(2));
        Assert.Equal(target, actualTarget);
        var enumFieldSignature = new BlobBuilder();
        enumFieldSignature.WriteByte(0x06);
        enumFieldSignature.WriteByte(enumElementType);
        FieldDefinitionHandle storage = metadata.AddFieldDefinition(
            FieldAttributes.Public
                | FieldAttributes.SpecialName
                | FieldAttributes.RTSpecialName,
            metadata.GetOrAddString("value__"),
            metadata.GetOrAddBlob(enumFieldSignature));
        BlobHandle editorBrowsableValue = default;
        if (hideStorageSlot)
        {
            var attributeValue = new BlobBuilder();
            attributeValue.WriteUInt16(1);
            attributeValue.WriteInt32(1);
            attributeValue.WriteUInt16(0);
            editorBrowsableValue = metadata.GetOrAddBlob(attributeValue);
            metadata.AddCustomAttribute(
                storage,
                editorBrowsableConstructor,
                editorBrowsableValue);
        }
        FieldDefinitionHandle literal = metadata.AddFieldDefinition(
            FieldAttributes.Public
                | FieldAttributes.Static
                | FieldAttributes.Literal,
            metadata.GetOrAddString("One"),
            metadata.GetOrAddBlob(enumFieldSignature));
        if (enumElementType == 0x05)
            metadata.AddConstant(literal, (byte)1);
        else
            metadata.AddConstant(literal, 1);
        if (hideStorageSlot)
        {
            FieldDefinitionHandle hidden = metadata.AddFieldDefinition(
                FieldAttributes.Public
                    | FieldAttributes.Static
                    | FieldAttributes.Literal,
                metadata.GetOrAddString("Hidden"),
                metadata.GetOrAddBlob(enumFieldSignature));
            metadata.AddConstant(hidden, (byte)2);
            metadata.AddCustomAttribute(
                hidden,
                editorBrowsableConstructor,
                editorBrowsableValue);
        }
        return Serialize(metadata);
    }

    static byte[] BuildEnumDefaultDecoyImage(
        int decoyTypeCount,
        int defaultMethodCount,
        int baseNameLength)
    {
        var metadata = Metadata("EnumDefaultDecoyBomb");
        AssemblyReferenceHandle contracts = metadata.AddAssemblyReference(
            metadata.GetOrAddString("Contracts"),
            new Version(1, 0, 0, 0),
            default,
            default,
            default,
            default);
        TypeReferenceHandle longBase = metadata.AddTypeReference(
            contracts,
            metadata.GetOrAddString("Contracts"),
            metadata.GetOrAddString(new string('B', baseNameLength)));
        AddModuleAndPublicType(metadata, "Host");
        for (int index = 0; index < decoyTypeCount; index++)
        {
            metadata.AddTypeDefinition(
                TypeAttributes.NotPublic,
                metadata.GetOrAddString("Decoys"),
                metadata.GetOrAddString($"D{index}"),
                longBase,
                MetadataTokens.FieldDefinitionHandle(1),
                MetadataTokens.MethodDefinitionHandle(defaultMethodCount + 1));
        }

        var signature = new BlobBuilder();
        new BlobEncoder(signature).MethodSignature().Parameters(
            1,
            returnType => returnType.Void(),
            parameters => parameters.AddParameter().Type().Int32());
        BlobHandle signatureHandle = metadata.GetOrAddBlob(signature);
        for (int index = 0; index < defaultMethodCount; index++)
        {
            ParameterHandle parameter = metadata.AddParameter(
                ParameterAttributes.Optional | ParameterAttributes.HasDefault,
                metadata.GetOrAddString("value"),
                sequenceNumber: 1);
            metadata.AddConstant(parameter, 1);
            metadata.AddMethodDefinition(
                MethodAttributes.Public
                    | MethodAttributes.Static
                    | MethodAttributes.Abstract,
                MethodImplAttributes.IL,
                metadata.GetOrAddString($"M{index}"),
                signatureHandle,
                bodyOffset: -1,
                parameter);
        }
        return Serialize(metadata);
    }

    static byte[] BuildEnumDefaultTypeSpecDecoyImage(
        int decoyTypeCount,
        int defaultMethodCount,
        int rank)
    {
        var metadata = Metadata("EnumDefaultTypeSpecBomb");
        var typeSpecSignature = new BlobBuilder();
        typeSpecSignature.WriteByte(0x14);
        typeSpecSignature.WriteByte(0x08);
        typeSpecSignature.WriteCompressedInteger(rank);
        typeSpecSignature.WriteCompressedInteger(0);
        typeSpecSignature.WriteCompressedInteger(0);
        TypeSpecificationHandle decoyBase =
            metadata.AddTypeSpecification(metadata.GetOrAddBlob(typeSpecSignature));
        AddModuleAndPublicType(metadata, "Host");
        for (int index = 0; index < decoyTypeCount; index++)
        {
            metadata.AddTypeDefinition(
                TypeAttributes.NotPublic,
                metadata.GetOrAddString("Decoys"),
                metadata.GetOrAddString($"D{index}"),
                decoyBase,
                MetadataTokens.FieldDefinitionHandle(1),
                MetadataTokens.MethodDefinitionHandle(defaultMethodCount + 1));
        }

        var signature = new BlobBuilder();
        new BlobEncoder(signature).MethodSignature().Parameters(
            1,
            returnType => returnType.Void(),
            parameters => parameters.AddParameter().Type().Int32());
        BlobHandle signatureHandle = metadata.GetOrAddBlob(signature);
        for (int index = 0; index < defaultMethodCount; index++)
        {
            ParameterHandle parameter = metadata.AddParameter(
                ParameterAttributes.Optional | ParameterAttributes.HasDefault,
                metadata.GetOrAddString("value"),
                sequenceNumber: 1);
            metadata.AddConstant(parameter, 1);
            metadata.AddMethodDefinition(
                MethodAttributes.Public
                    | MethodAttributes.Static
                    | MethodAttributes.Abstract,
                MethodImplAttributes.IL,
                metadata.GetOrAddString($"M{index}"),
                signatureHandle,
                bodyOffset: -1,
                parameter);
        }
        return Serialize(metadata);
    }

    static byte[] BuildDeepBoxedAttributeImage(int depth)
    {
        var metadata = Metadata("BoxedNest");
        AssemblyReferenceHandle assembly = metadata.AddAssemblyReference(
            metadata.GetOrAddString("Other"),
            new Version(1, 0, 0, 0),
            default,
            default,
            default,
            default);
        TypeReferenceHandle attributeType = metadata.AddTypeReference(
            assembly,
            metadata.GetOrAddString("System"),
            metadata.GetOrAddString("SampleAttribute"));
        var constructorSignature = new BlobBuilder();
        new BlobEncoder(constructorSignature).MethodSignature(
            SignatureCallingConvention.Default,
            genericParameterCount: 0,
            isInstanceMethod: true).Parameters(
                1,
                returnType => returnType.Void(),
                parameters => parameters.AddParameter().Type().Object());
        MemberReferenceHandle constructor = metadata.AddMemberReference(
            attributeType,
            metadata.GetOrAddString(".ctor"),
            metadata.GetOrAddBlob(constructorSignature));
        var value = new BlobBuilder();
        value.WriteUInt16(1);
        for (int index = 0; index < depth; index++)
            value.WriteByte(0x51);
        value.WriteByte(0x08);
        value.WriteInt32(1);
        value.WriteUInt16(0);
        TypeDefinitionHandle type = AddModuleAndPublicType(metadata, "Host");
        metadata.AddCustomAttribute(type, constructor, metadata.GetOrAddBlob(value));
        return Serialize(metadata);
    }

    static byte[] BuildNamedArgumentArrayCountImage(int elementCount)
    {
        var metadata = Metadata("NamedArrayCount");
        AssemblyReferenceHandle assembly = metadata.AddAssemblyReference(
            metadata.GetOrAddString("Other"),
            new Version(1, 0, 0, 0),
            default,
            default,
            default,
            default);
        TypeReferenceHandle attributeType = metadata.AddTypeReference(
            assembly,
            metadata.GetOrAddString("System"),
            metadata.GetOrAddString("SampleAttribute"));
        var constructorSignature = new BlobBuilder();
        new BlobEncoder(constructorSignature).MethodSignature(
            SignatureCallingConvention.Default,
            genericParameterCount: 0,
            isInstanceMethod: true).Parameters(
                0,
                returnType => returnType.Void(),
                _ => { });
        MemberReferenceHandle constructor = metadata.AddMemberReference(
            attributeType,
            metadata.GetOrAddString(".ctor"),
            metadata.GetOrAddBlob(constructorSignature));
        var value = new BlobBuilder();
        value.WriteUInt16(1);
        value.WriteUInt16(1);
        value.WriteByte(0x53);
        value.WriteByte(0x1d);
        value.WriteByte(0x08);
        value.WriteSerializedString("V");
        value.WriteInt32(elementCount);
        TypeDefinitionHandle type = AddModuleAndPublicType(metadata, "Host");
        metadata.AddCustomAttribute(type, constructor, metadata.GetOrAddBlob(value));
        return Serialize(metadata);
    }

    static byte[] BuildNamedNestedArrayAttributeImage(int depth)
    {
        var metadata = Metadata("NamedNest");
        AssemblyReferenceHandle assembly = metadata.AddAssemblyReference(
            metadata.GetOrAddString("Other"),
            new Version(1, 0, 0, 0),
            default,
            default,
            default,
            default);
        TypeReferenceHandle attributeType = metadata.AddTypeReference(
            assembly,
            metadata.GetOrAddString("System"),
            metadata.GetOrAddString("SampleAttribute"));
        var constructorSignature = new BlobBuilder();
        new BlobEncoder(constructorSignature).MethodSignature(
            SignatureCallingConvention.Default,
            genericParameterCount: 0,
            isInstanceMethod: true).Parameters(
                0,
                returnType => returnType.Void(),
                _ => { });
        MemberReferenceHandle constructor = metadata.AddMemberReference(
            attributeType,
            metadata.GetOrAddString(".ctor"),
            metadata.GetOrAddBlob(constructorSignature));
        var value = new BlobBuilder();
        value.WriteUInt16(1);
        value.WriteUInt16(1);
        value.WriteByte(0x53);
        value.WriteByte(0x1d);
        value.WriteByte(0x51);
        value.WriteSerializedString("V");
        value.WriteInt32(1);
        for (int index = 0; index < depth; index++)
        {
            value.WriteByte(0x1d);
            value.WriteByte(0x51);
            value.WriteInt32(1);
        }

        value.WriteByte(0x08);
        value.WriteInt32(7);
        TypeDefinitionHandle type = AddModuleAndPublicType(metadata, "Host");
        metadata.AddCustomAttribute(type, constructor, metadata.GetOrAddBlob(value));
        return Serialize(metadata);
    }

    static byte[] BuildTypeRefEnumDesyncImage(int elementCount)
    {
        var metadata = Metadata("EnumDesync");
        AssemblyReferenceHandle other = metadata.AddAssemblyReference(
            metadata.GetOrAddString("Other"),
            new Version(1, 0, 0, 0),
            default,
            default,
            default,
            default);
        TypeReferenceHandle enumRef = metadata.AddTypeReference(
            other,
            metadata.GetOrAddString("Samples"),
            metadata.GetOrAddString("E"));
        TypeReferenceHandle systemEnum = metadata.AddTypeReference(
            other,
            metadata.GetOrAddString("System"),
            metadata.GetOrAddString("Enum"));
        TypeReferenceHandle attributeType = metadata.AddTypeReference(
            other,
            metadata.GetOrAddString("System"),
            metadata.GetOrAddString("SampleAttribute"));
        var constructorSignature = new BlobBuilder();
        new BlobEncoder(constructorSignature).MethodSignature(
            SignatureCallingConvention.Default,
            genericParameterCount: 0,
            isInstanceMethod: true).Parameters(
                2,
                returnType => returnType.Void(),
                parameters =>
                {
                    parameters.AddParameter().Type().Type(enumRef, isValueType: true);
                    parameters.AddParameter().Type().SZArray().Int32();
                });
        MemberReferenceHandle constructor = metadata.AddMemberReference(
            attributeType,
            metadata.GetOrAddString(".ctor"),
            metadata.GetOrAddBlob(constructorSignature));
        var fieldSignature = new BlobBuilder();
        new BlobEncoder(fieldSignature).FieldSignature().Int64();
        metadata.AddFieldDefinition(
            FieldAttributes.Public | FieldAttributes.SpecialName | FieldAttributes.RTSpecialName,
            metadata.GetOrAddString("value__"),
            metadata.GetOrAddBlob(fieldSignature));
        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        metadata.AddTypeDefinition(
            TypeAttributes.Public | TypeAttributes.Sealed,
            metadata.GetOrAddString("Samples"),
            metadata.GetOrAddString("E"),
            systemEnum,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        TypeDefinitionHandle attributed = metadata.AddTypeDefinition(
            TypeAttributes.Public | TypeAttributes.Abstract,
            metadata.GetOrAddString("Samples"),
            metadata.GetOrAddString("Host"),
            default,
            MetadataTokens.FieldDefinitionHandle(2),
            MetadataTokens.MethodDefinitionHandle(1));
        var value = new BlobBuilder();
        value.WriteUInt16(1);
        value.WriteInt64(0);
        value.WriteInt32(elementCount);
        value.WriteUInt16(0);
        metadata.AddCustomAttribute(
            attributed,
            constructor,
            metadata.GetOrAddBlob(value));
        return Serialize(metadata);
    }

    static byte[] BuildEnumCmodDesyncImage(int modifierCount, int elementCount)
    {
        var metadata = Metadata("EnumCmod");
        AssemblyReferenceHandle other = metadata.AddAssemblyReference(
            metadata.GetOrAddString("Other"),
            new Version(1, 0, 0, 0),
            default,
            default,
            default,
            default);
        TypeReferenceHandle systemEnum = metadata.AddTypeReference(
            other,
            metadata.GetOrAddString("System"),
            metadata.GetOrAddString("Enum"));
        TypeReferenceHandle attributeType = metadata.AddTypeReference(
            other,
            metadata.GetOrAddString("System"),
            metadata.GetOrAddString("SampleAttribute"));
        TypeDefinitionHandle enumDef = MetadataTokens.TypeDefinitionHandle(2);
        var constructorSignature = new BlobBuilder();
        new BlobEncoder(constructorSignature).MethodSignature(
            SignatureCallingConvention.Default,
            genericParameterCount: 0,
            isInstanceMethod: true).Parameters(
                2,
                returnType => returnType.Void(),
                parameters =>
                {
                    parameters.AddParameter().Type().Type(enumDef, isValueType: true);
                    parameters.AddParameter().Type().SZArray().Int32();
                });
        MemberReferenceHandle constructor = metadata.AddMemberReference(
            attributeType,
            metadata.GetOrAddString(".ctor"),
            metadata.GetOrAddBlob(constructorSignature));
        var fieldSignature = new BlobBuilder();
        fieldSignature.WriteByte(0x06);
        int coded = (MetadataTokens.GetRowNumber(systemEnum) << 2) | 0x01;
        for (int index = 0; index < modifierCount; index++)
        {
            fieldSignature.WriteByte(0x20);
            fieldSignature.WriteCompressedInteger(coded);
        }

        fieldSignature.WriteByte(0x0a);
        metadata.AddFieldDefinition(
            FieldAttributes.Public | FieldAttributes.SpecialName | FieldAttributes.RTSpecialName,
            metadata.GetOrAddString("value__"),
            metadata.GetOrAddBlob(fieldSignature));
        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        metadata.AddTypeDefinition(
            TypeAttributes.Public | TypeAttributes.Sealed,
            metadata.GetOrAddString("Samples"),
            metadata.GetOrAddString("E"),
            systemEnum,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        TypeDefinitionHandle attributed = metadata.AddTypeDefinition(
            TypeAttributes.Public | TypeAttributes.Abstract,
            metadata.GetOrAddString("Samples"),
            metadata.GetOrAddString("Host"),
            default,
            MetadataTokens.FieldDefinitionHandle(2),
            MetadataTokens.MethodDefinitionHandle(1));
        var value = new BlobBuilder();
        value.WriteUInt16(1);
        value.WriteInt32(0);
        value.WriteInt32(elementCount);
        value.WriteUInt16(0);
        metadata.AddCustomAttribute(
            attributed,
            constructor,
            metadata.GetOrAddBlob(value));
        return Serialize(metadata);
    }

    static byte[] BuildAssemblyQualifiedNamedEnumImage(int elementCount)
    {
        var metadata = Metadata("EnumSuffix");
        AssemblyReferenceHandle other = metadata.AddAssemblyReference(
            metadata.GetOrAddString("Other"),
            new Version(1, 0, 0, 0),
            default,
            default,
            default,
            default);
        TypeReferenceHandle systemEnum = metadata.AddTypeReference(
            other,
            metadata.GetOrAddString("System"),
            metadata.GetOrAddString("Enum"));
        TypeReferenceHandle attributeType = metadata.AddTypeReference(
            other,
            metadata.GetOrAddString("System"),
            metadata.GetOrAddString("SampleAttribute"));
        var constructorSignature = new BlobBuilder();
        new BlobEncoder(constructorSignature).MethodSignature(
            SignatureCallingConvention.Default,
            genericParameterCount: 0,
            isInstanceMethod: true).Parameters(
                0,
                returnType => returnType.Void(),
                _ => { });
        MemberReferenceHandle constructor = metadata.AddMemberReference(
            attributeType,
            metadata.GetOrAddString(".ctor"),
            metadata.GetOrAddBlob(constructorSignature));
        var fieldSignature = new BlobBuilder();
        new BlobEncoder(fieldSignature).FieldSignature().Int64();
        metadata.AddFieldDefinition(
            FieldAttributes.Public | FieldAttributes.SpecialName | FieldAttributes.RTSpecialName,
            metadata.GetOrAddString("value__"),
            metadata.GetOrAddBlob(fieldSignature));
        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        metadata.AddTypeDefinition(
            TypeAttributes.Public | TypeAttributes.Sealed,
            metadata.GetOrAddString("Samples"),
            metadata.GetOrAddString("E"),
            systemEnum,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        TypeDefinitionHandle attributed = metadata.AddTypeDefinition(
            TypeAttributes.Public | TypeAttributes.Abstract,
            metadata.GetOrAddString("Samples"),
            metadata.GetOrAddString("Host"),
            default,
            MetadataTokens.FieldDefinitionHandle(2),
            MetadataTokens.MethodDefinitionHandle(1));
        var value = new BlobBuilder();
        value.WriteUInt16(1);
        value.WriteUInt16(2);
        value.WriteByte(0x53);
        value.WriteByte(0x55);
        value.WriteSerializedString("Samples.E, Other");
        value.WriteSerializedString("F");
        value.WriteInt64(0);
        value.WriteByte(0x53);
        value.WriteByte(0x1d);
        value.WriteByte(0x08);
        value.WriteSerializedString("V");
        value.WriteInt32(elementCount);
        metadata.AddCustomAttribute(
            attributed,
            constructor,
            metadata.GetOrAddBlob(value));
        return Serialize(metadata);
    }

    static byte[] BuildGenericEarlierThenArrayImage(
        bool pointerToFnPtr,
        int elementCount)
    {
        var metadata = Metadata("FnPtrDesync");
        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        TypeDefinitionHandle attributeType = metadata.AddTypeDefinition(
            TypeAttributes.Public | TypeAttributes.Sealed,
            metadata.GetOrAddString("Samples"),
            metadata.GetOrAddString("MyAttr`2"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        var typeSpecSignature = new BlobBuilder();
        typeSpecSignature.WriteByte(0x15);
        typeSpecSignature.WriteByte(0x12);
        WriteTypeDefOrRef(typeSpecSignature, attributeType);
        typeSpecSignature.WriteCompressedInteger(2);
        if (pointerToFnPtr)
            typeSpecSignature.WriteByte(0x0f);
        typeSpecSignature.WriteByte(0x1b);
        typeSpecSignature.WriteByte(0x00);
        typeSpecSignature.WriteCompressedInteger(0);
        typeSpecSignature.WriteByte(0x01);
        typeSpecSignature.WriteByte(0x1d);
        typeSpecSignature.WriteByte(0x08);
        TypeSpecificationHandle typeSpec = metadata.AddTypeSpecification(
            metadata.GetOrAddBlob(typeSpecSignature));
        var constructorSignature = new BlobBuilder();
        constructorSignature.WriteByte(0x20);
        constructorSignature.WriteCompressedInteger(1);
        constructorSignature.WriteByte(0x01);
        constructorSignature.WriteByte(0x13);
        constructorSignature.WriteCompressedInteger(1);
        MemberReferenceHandle constructor = metadata.AddMemberReference(
            typeSpec,
            metadata.GetOrAddString(".ctor"),
            metadata.GetOrAddBlob(constructorSignature));
        TypeDefinitionHandle attributed = metadata.AddTypeDefinition(
            TypeAttributes.Public | TypeAttributes.Abstract,
            metadata.GetOrAddString("Samples"),
            metadata.GetOrAddString("Host"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        var value = new BlobBuilder();
        value.WriteUInt16(1);
        value.WriteInt32(elementCount);
        value.WriteUInt16(0);
        metadata.AddCustomAttribute(
            attributed,
            constructor,
            metadata.GetOrAddBlob(value));
        return Serialize(metadata);
    }

    static byte[] BuildClassTypeDefRow4DesyncImage(int elementCount)
    {
        var metadata = Metadata("ClassDesync");
        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        TypeDefinitionHandle attributeType = metadata.AddTypeDefinition(
            TypeAttributes.Public | TypeAttributes.Sealed,
            metadata.GetOrAddString("Samples"),
            metadata.GetOrAddString("MyAttr`2"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        metadata.AddTypeDefinition(
            TypeAttributes.Public | TypeAttributes.Abstract,
            metadata.GetOrAddString("Samples"),
            metadata.GetOrAddString("Pad"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        TypeDefinitionHandle dummy = metadata.AddTypeDefinition(
            TypeAttributes.Public | TypeAttributes.Abstract,
            metadata.GetOrAddString("Samples"),
            metadata.GetOrAddString("Dummy"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        var typeSpecSignature = new BlobBuilder();
        typeSpecSignature.WriteByte(0x15);
        typeSpecSignature.WriteByte(0x12);
        WriteTypeDefOrRef(typeSpecSignature, attributeType);
        typeSpecSignature.WriteCompressedInteger(3);
        typeSpecSignature.WriteByte(0x12);
        WriteTypeDefOrRef(typeSpecSignature, dummy);
        typeSpecSignature.WriteByte(0x08);
        typeSpecSignature.WriteByte(0x1d);
        typeSpecSignature.WriteByte(0x08);
        TypeSpecificationHandle typeSpec = metadata.AddTypeSpecification(
            metadata.GetOrAddBlob(typeSpecSignature));
        var constructorSignature = new BlobBuilder();
        constructorSignature.WriteByte(0x20);
        constructorSignature.WriteCompressedInteger(1);
        constructorSignature.WriteByte(0x01);
        constructorSignature.WriteByte(0x13);
        constructorSignature.WriteCompressedInteger(1);
        MemberReferenceHandle constructor = metadata.AddMemberReference(
            typeSpec,
            metadata.GetOrAddString(".ctor"),
            metadata.GetOrAddBlob(constructorSignature));
        TypeDefinitionHandle attributed = metadata.AddTypeDefinition(
            TypeAttributes.Public | TypeAttributes.Abstract,
            metadata.GetOrAddString("Samples"),
            metadata.GetOrAddString("Host"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        var value = new BlobBuilder();
        value.WriteUInt16(1);
        value.WriteInt32(elementCount);
        value.WriteUInt16(0);
        metadata.AddCustomAttribute(
            attributed,
            constructor,
            metadata.GetOrAddBlob(value));
        return Serialize(metadata);
    }

    static byte[] BuildValueTypeTypeRefRow4DesyncImage(int elementCount)
    {
        var metadata = Metadata("VtDesync");
        AssemblyReferenceHandle other = metadata.AddAssemblyReference(
            metadata.GetOrAddString("Other"),
            new Version(1, 0, 0, 0),
            default,
            default,
            default,
            default);
        metadata.AddTypeReference(
            other,
            metadata.GetOrAddString("A"),
            metadata.GetOrAddString("T1"));
        metadata.AddTypeReference(
            other,
            metadata.GetOrAddString("A"),
            metadata.GetOrAddString("T2"));
        metadata.AddTypeReference(
            other,
            metadata.GetOrAddString("A"),
            metadata.GetOrAddString("T3"));
        TypeReferenceHandle typeRef4 = metadata.AddTypeReference(
            other,
            metadata.GetOrAddString("A"),
            metadata.GetOrAddString("T4"));
        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        TypeDefinitionHandle attributeType = metadata.AddTypeDefinition(
            TypeAttributes.Public | TypeAttributes.Sealed,
            metadata.GetOrAddString("Samples"),
            metadata.GetOrAddString("MyAttr`2"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        var typeSpecSignature = new BlobBuilder();
        typeSpecSignature.WriteByte(0x15);
        typeSpecSignature.WriteByte(0x12);
        WriteTypeDefOrRef(typeSpecSignature, attributeType);
        typeSpecSignature.WriteCompressedInteger(3);
        typeSpecSignature.WriteByte(0x11);
        WriteTypeDefOrRef(typeSpecSignature, typeRef4);
        typeSpecSignature.WriteByte(0x08);
        typeSpecSignature.WriteByte(0x1d);
        typeSpecSignature.WriteByte(0x08);
        TypeSpecificationHandle typeSpec = metadata.AddTypeSpecification(
            metadata.GetOrAddBlob(typeSpecSignature));
        var constructorSignature = new BlobBuilder();
        constructorSignature.WriteByte(0x20);
        constructorSignature.WriteCompressedInteger(1);
        constructorSignature.WriteByte(0x01);
        constructorSignature.WriteByte(0x13);
        constructorSignature.WriteCompressedInteger(1);
        MemberReferenceHandle constructor = metadata.AddMemberReference(
            typeSpec,
            metadata.GetOrAddString(".ctor"),
            metadata.GetOrAddBlob(constructorSignature));
        TypeDefinitionHandle attributed = metadata.AddTypeDefinition(
            TypeAttributes.Public | TypeAttributes.Abstract,
            metadata.GetOrAddString("Samples"),
            metadata.GetOrAddString("Host"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        var value = new BlobBuilder();
        value.WriteUInt16(1);
        value.WriteInt32(elementCount);
        value.WriteUInt16(0);
        metadata.AddCustomAttribute(
            attributed,
            constructor,
            metadata.GetOrAddBlob(value));
        return Serialize(metadata);
    }

    static byte[] BuildSelfReferentialGenericVarImage()
    {
        var metadata = Metadata("VarSo");
        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        TypeDefinitionHandle attributeType = metadata.AddTypeDefinition(
            TypeAttributes.Public | TypeAttributes.Sealed,
            metadata.GetOrAddString("Samples"),
            metadata.GetOrAddString("MyAttr`1"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        var typeSpecSignature = new BlobBuilder();
        typeSpecSignature.WriteByte(0x15);
        typeSpecSignature.WriteByte(0x12);
        WriteTypeDefOrRef(typeSpecSignature, attributeType);
        typeSpecSignature.WriteCompressedInteger(1);
        typeSpecSignature.WriteByte(0x13);
        typeSpecSignature.WriteCompressedInteger(0);
        TypeSpecificationHandle typeSpec = metadata.AddTypeSpecification(
            metadata.GetOrAddBlob(typeSpecSignature));
        var constructorSignature = new BlobBuilder();
        constructorSignature.WriteByte(0x20);
        constructorSignature.WriteCompressedInteger(1);
        constructorSignature.WriteByte(0x01);
        constructorSignature.WriteByte(0x13);
        constructorSignature.WriteCompressedInteger(0);
        MemberReferenceHandle constructor = metadata.AddMemberReference(
            typeSpec,
            metadata.GetOrAddString(".ctor"),
            metadata.GetOrAddBlob(constructorSignature));
        TypeDefinitionHandle attributed = metadata.AddTypeDefinition(
            TypeAttributes.Public | TypeAttributes.Abstract,
            metadata.GetOrAddString("Samples"),
            metadata.GetOrAddString("Host"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        var value = new BlobBuilder();
        value.WriteUInt16(1);
        value.WriteInt32(0);
        value.WriteUInt16(0);
        metadata.AddCustomAttribute(
            attributed,
            constructor,
            metadata.GetOrAddBlob(value));
        return Serialize(metadata);
    }

    static byte[] BuildClassSystemStringImage(int elementCount)
    {
        var metadata = Metadata("ClassString");
        AssemblyReferenceHandle other = metadata.AddAssemblyReference(
            metadata.GetOrAddString("Other"),
            new Version(1, 0, 0, 0),
            default,
            default,
            default,
            default);
        TypeReferenceHandle systemString = metadata.AddTypeReference(
            other,
            metadata.GetOrAddString("System"),
            metadata.GetOrAddString("String"));
        TypeReferenceHandle attributeType = metadata.AddTypeReference(
            other,
            metadata.GetOrAddString("System"),
            metadata.GetOrAddString("SampleAttribute"));
        var constructorSignature = new BlobBuilder();
        new BlobEncoder(constructorSignature).MethodSignature(
            SignatureCallingConvention.Default,
            genericParameterCount: 0,
            isInstanceMethod: true).Parameters(
                2,
                returnType => returnType.Void(),
                parameters =>
                {
                    parameters.AddParameter().Type().Type(systemString, isValueType: false);
                    parameters.AddParameter().Type().SZArray().Int32();
                });
        MemberReferenceHandle constructor = metadata.AddMemberReference(
            attributeType,
            metadata.GetOrAddString(".ctor"),
            metadata.GetOrAddBlob(constructorSignature));
        var value = new BlobBuilder();
        value.WriteUInt16(1);
        value.WriteInt32(0);
        value.WriteInt32(elementCount);
        value.WriteUInt16(0);
        TypeDefinitionHandle type = AddModuleAndPublicType(metadata, "Host");
        metadata.AddCustomAttribute(type, constructor, metadata.GetOrAddBlob(value));
        return Serialize(metadata);
    }

    static byte[] BuildDottedSystemTypeImage(int elementCount)
    {
        var metadata = Metadata("DottedType");
        AssemblyReferenceHandle other = metadata.AddAssemblyReference(
            metadata.GetOrAddString("Other"),
            new Version(1, 0, 0, 0),
            default,
            default,
            default,
            default);
        TypeReferenceHandle systemType = metadata.AddTypeReference(
            other,
            default,
            metadata.GetOrAddString("System.Type"));
        TypeReferenceHandle attributeType = metadata.AddTypeReference(
            other,
            metadata.GetOrAddString("System"),
            metadata.GetOrAddString("SampleAttribute"));
        var constructorSignature = new BlobBuilder();
        new BlobEncoder(constructorSignature).MethodSignature(
            SignatureCallingConvention.Default,
            genericParameterCount: 0,
            isInstanceMethod: true).Parameters(
                2,
                returnType => returnType.Void(),
                parameters =>
                {
                    parameters.AddParameter().Type().Type(systemType, isValueType: false);
                    parameters.AddParameter().Type().SZArray().Int32();
                });
        MemberReferenceHandle constructor = metadata.AddMemberReference(
            attributeType,
            metadata.GetOrAddString(".ctor"),
            metadata.GetOrAddBlob(constructorSignature));
        var value = new BlobBuilder();
        value.WriteUInt16(1);
        value.WriteSerializedString(string.Empty);
        value.WriteInt32(elementCount);
        value.WriteUInt16(0);
        TypeDefinitionHandle type = AddModuleAndPublicType(metadata, "Host");
        metadata.AddCustomAttribute(type, constructor, metadata.GetOrAddBlob(value));
        return Serialize(metadata);
    }

    static byte[] BuildStringTypedEnumImage(int elementCount)
    {
        var metadata = Metadata("StringEnum");
        AssemblyReferenceHandle other = metadata.AddAssemblyReference(
            metadata.GetOrAddString("Other"),
            new Version(1, 0, 0, 0),
            default,
            default,
            default,
            default);
        TypeReferenceHandle systemEnum = metadata.AddTypeReference(
            other,
            metadata.GetOrAddString("System"),
            metadata.GetOrAddString("Enum"));
        TypeReferenceHandle attributeType = metadata.AddTypeReference(
            other,
            metadata.GetOrAddString("System"),
            metadata.GetOrAddString("SampleAttribute"));
        TypeDefinitionHandle enumDef = MetadataTokens.TypeDefinitionHandle(2);
        var constructorSignature = new BlobBuilder();
        new BlobEncoder(constructorSignature).MethodSignature(
            SignatureCallingConvention.Default,
            genericParameterCount: 0,
            isInstanceMethod: true).Parameters(
                2,
                returnType => returnType.Void(),
                parameters =>
                {
                    parameters.AddParameter().Type().Type(enumDef, isValueType: true);
                    parameters.AddParameter().Type().SZArray().Int32();
                });
        MemberReferenceHandle constructor = metadata.AddMemberReference(
            attributeType,
            metadata.GetOrAddString(".ctor"),
            metadata.GetOrAddBlob(constructorSignature));
        var fieldSignature = new BlobBuilder();
        new BlobEncoder(fieldSignature).FieldSignature().String();
        metadata.AddFieldDefinition(
            FieldAttributes.Public | FieldAttributes.SpecialName | FieldAttributes.RTSpecialName,
            metadata.GetOrAddString("value__"),
            metadata.GetOrAddBlob(fieldSignature));
        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        metadata.AddTypeDefinition(
            TypeAttributes.Public | TypeAttributes.Sealed,
            metadata.GetOrAddString("Samples"),
            metadata.GetOrAddString("E"),
            systemEnum,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        TypeDefinitionHandle host = metadata.AddTypeDefinition(
            TypeAttributes.Public | TypeAttributes.Abstract,
            metadata.GetOrAddString("Samples"),
            metadata.GetOrAddString("Host"),
            default,
            MetadataTokens.FieldDefinitionHandle(2),
            MetadataTokens.MethodDefinitionHandle(1));
        var value = new BlobBuilder();
        value.WriteUInt16(1);
        value.WriteInt32(0);
        value.WriteInt32(elementCount);
        value.WriteUInt16(0);
        metadata.AddCustomAttribute(host, constructor, metadata.GetOrAddBlob(value));
        return Serialize(metadata);
    }

    static byte[] BuildBoxedEnumArrayEmptyNameImage(int elementCount)
    {
        var metadata = Metadata("BoxedEnumAmp");
        AssemblyReferenceHandle other = metadata.AddAssemblyReference(
            metadata.GetOrAddString("Other"),
            new Version(1, 0, 0, 0),
            default,
            default,
            default,
            default);
        TypeReferenceHandle attributeType = metadata.AddTypeReference(
            other,
            metadata.GetOrAddString("System"),
            metadata.GetOrAddString("SampleAttribute"));
        var constructorSignature = new BlobBuilder();
        new BlobEncoder(constructorSignature).MethodSignature(
            SignatureCallingConvention.Default,
            genericParameterCount: 0,
            isInstanceMethod: true).Parameters(
                1,
                returnType => returnType.Void(),
                parameters => parameters.AddParameter().Type().Object());
        MemberReferenceHandle constructor = metadata.AddMemberReference(
            attributeType,
            metadata.GetOrAddString(".ctor"),
            metadata.GetOrAddBlob(constructorSignature));
        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        TypeDefinitionHandle host = metadata.AddTypeDefinition(
            TypeAttributes.Public | TypeAttributes.Abstract,
            metadata.GetOrAddString("Samples"),
            metadata.GetOrAddString("Host"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        var value = new BlobBuilder();
        value.WriteUInt16(1);
        value.WriteByte(0x1d);
        value.WriteByte(0x55);
        value.WriteByte(0x00);
        value.WriteInt32(elementCount);
        metadata.AddCustomAttribute(host, constructor, metadata.GetOrAddBlob(value));
        return Serialize(metadata);
    }

    static byte[] BuildCustomAttributeArrayCountImage(
        int attributeCount,
        int elementCount)
    {
        var metadata = Metadata("ArrayCount");
        AssemblyReferenceHandle assembly = metadata.AddAssemblyReference(
            metadata.GetOrAddString("Other"),
            new Version(1, 0, 0, 0),
            default,
            default,
            default,
            default);
        TypeReferenceHandle attributeType = metadata.AddTypeReference(
            assembly,
            metadata.GetOrAddString("System"),
            metadata.GetOrAddString("SampleAttribute"));
        var constructorSignature = new BlobBuilder();
        new BlobEncoder(constructorSignature).MethodSignature(
            SignatureCallingConvention.Default,
            genericParameterCount: 0,
            isInstanceMethod: true).Parameters(
                1,
                returnType => returnType.Void(),
                parameters => parameters.AddParameter().Type().SZArray().Int32());
        MemberReferenceHandle constructor = metadata.AddMemberReference(
            attributeType,
            metadata.GetOrAddString(".ctor"),
            metadata.GetOrAddBlob(constructorSignature));
        var value = new BlobBuilder();
        value.WriteUInt16(1);
        value.WriteInt32(elementCount);
        BlobHandle valueHandle = metadata.GetOrAddBlob(value);
        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        for (int index = 0; index < attributeCount; index++)
        {
            TypeDefinitionHandle type = metadata.AddTypeDefinition(
                TypeAttributes.Public | TypeAttributes.Abstract,
                metadata.GetOrAddString("Samples"),
                metadata.GetOrAddString($"Attributed{index}"),
                default,
                MetadataTokens.FieldDefinitionHandle(1),
                MetadataTokens.MethodDefinitionHandle(1));
            metadata.AddCustomAttribute(type, constructor, valueHandle);
        }

        return Serialize(metadata);
    }

    static byte[] BuildSharedCustomAttributeArrayImage(
        int attributeCount,
        int elementCount)
    {
        var metadata = Metadata("SharedAttributeBlob");
        AssemblyReferenceHandle assembly = metadata.AddAssemblyReference(
            metadata.GetOrAddString("Other"),
            new Version(1, 0, 0, 0),
            default,
            default,
            default,
            default);
        TypeReferenceHandle attributeType = metadata.AddTypeReference(
            assembly,
            metadata.GetOrAddString("System"),
            metadata.GetOrAddString("SampleAttribute"));
        var constructorSignature = new BlobBuilder();
        new BlobEncoder(constructorSignature).MethodSignature(
            SignatureCallingConvention.Default,
            genericParameterCount: 0,
            isInstanceMethod: true).Parameters(
                1,
                returnType => returnType.Void(),
                parameters => parameters.AddParameter().Type().SZArray().Int32());
        MemberReferenceHandle constructor = metadata.AddMemberReference(
            attributeType,
            metadata.GetOrAddString(".ctor"),
            metadata.GetOrAddBlob(constructorSignature));
        var value = new BlobBuilder();
        value.WriteUInt16(1);
        value.WriteInt32(elementCount);
        for (int index = 0; index < elementCount; index++)
            value.WriteInt32(index);
        value.WriteUInt16(0);
        BlobHandle valueHandle = metadata.GetOrAddBlob(value);

        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        for (int index = 0; index < attributeCount; index++)
        {
            TypeDefinitionHandle type = metadata.AddTypeDefinition(
                TypeAttributes.Public | TypeAttributes.Abstract,
                metadata.GetOrAddString("Samples"),
                metadata.GetOrAddString($"Attributed{index}"),
                default,
                MetadataTokens.FieldDefinitionHandle(1),
                MetadataTokens.MethodDefinitionHandle(1));
            metadata.AddCustomAttribute(type, constructor, valueHandle);
        }

        return Serialize(metadata);
    }

    static byte[] BuildSharedBudgetLifecycleImage(bool warmIndex)
    {
        const int DefinitionCount = 700;
        const int NameLength = 2_048;
        MetadataBuilder metadata = Metadata("SharedBudgetLifecycle");
        AssemblyReferenceHandle external = metadata.AddAssemblyReference(
            metadata.GetOrAddString("External.Enums"),
            new Version(1, 0, 0, 0),
            default,
            default,
            default,
            default);
        TypeReferenceHandle attributeType = metadata.AddTypeReference(
            external,
            metadata.GetOrAddString("System"),
            metadata.GetOrAddString("SampleAttribute"));
        StringHandle longName = metadata.GetOrAddString(
            new string('E', NameLength));
        StringHandle longNamespace = metadata.GetOrAddString(
            new string('N', NameLength));
        TypeReferenceHandle enumReference = metadata.AddTypeReference(
            external,
            metadata.GetOrAddString("Match"),
            longName);

        var warmSignature = new BlobBuilder();
        new BlobEncoder(warmSignature).MethodSignature(
            SignatureCallingConvention.Default,
            genericParameterCount: 0,
            isInstanceMethod: true).Parameters(
                1,
                returnType => returnType.Void(),
                parameters => parameters.AddParameter().Type().Object());
        MemberReferenceHandle warmConstructor = metadata.AddMemberReference(
            attributeType,
            metadata.GetOrAddString(".ctor"),
            metadata.GetOrAddBlob(warmSignature));

        var targetSignature = new BlobBuilder();
        new BlobEncoder(targetSignature).MethodSignature(
            SignatureCallingConvention.Default,
            genericParameterCount: 0,
            isInstanceMethod: true).Parameters(
                2,
                returnType => returnType.Void(),
                parameters =>
                {
                    parameters.AddParameter().Type().Object();
                    parameters.AddParameter().Type().Type(
                        enumReference,
                        isValueType: true);
                });
        MemberReferenceHandle targetConstructor =
            metadata.AddMemberReference(
                attributeType,
                metadata.GetOrAddString(".ctor"),
                metadata.GetOrAddBlob(targetSignature));

        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        for (int index = 0; index < DefinitionCount; index++)
        {
            metadata.AddTypeDefinition(
                TypeAttributes.NotPublic,
                longNamespace,
                longName,
                default,
                MetadataTokens.FieldDefinitionHandle(1),
                MetadataTokens.MethodDefinitionHandle(1));
        }

        TypeDefinitionHandle warm = metadata.AddTypeDefinition(
            TypeAttributes.Public | TypeAttributes.Abstract,
            metadata.GetOrAddString("Samples"),
            metadata.GetOrAddString("Warm"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        TypeDefinitionHandle target1 = metadata.AddTypeDefinition(
            TypeAttributes.Public | TypeAttributes.Abstract,
            metadata.GetOrAddString("Samples"),
            metadata.GetOrAddString("Target1"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        TypeDefinitionHandle target2 = metadata.AddTypeDefinition(
            TypeAttributes.Public | TypeAttributes.Abstract,
            metadata.GetOrAddString("Samples"),
            metadata.GetOrAddString("Target2"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));

        var warmValue = new BlobBuilder();
        warmValue.WriteUInt16(1);
        warmValue.WriteByte(0x55);
        warmValue.WriteSerializedString("Missing.Enum");
        warmValue.WriteInt32(0);
        warmValue.WriteUInt16(0);
        if (warmIndex)
        {
            metadata.AddCustomAttribute(
                warm,
                warmConstructor,
                metadata.GetOrAddBlob(warmValue));
        }

        var targetValue = new BlobBuilder();
        targetValue.WriteUInt16(1);
        targetValue.WriteByte(0x55);
        targetValue.WriteSerializedString("Missing.Enum");
        targetValue.WriteInt32(0);
        targetValue.WriteInt32(0);
        targetValue.WriteUInt16(0);
        BlobHandle sharedTargetValue = metadata.GetOrAddBlob(targetValue);
        metadata.AddCustomAttribute(
            target1,
            targetConstructor,
            sharedTargetValue);
        metadata.AddCustomAttribute(
            target2,
            targetConstructor,
            sharedTargetValue);
        return Serialize(metadata);
    }

    static byte[] BuildCustomAttributeNamedArgumentCountImage(
        int attributeCount,
        int namedArgumentCount)
    {
        var metadata = Metadata("NamedArgCount");
        AssemblyReferenceHandle assembly = metadata.AddAssemblyReference(
            metadata.GetOrAddString("Other"),
            new Version(1, 0, 0, 0),
            default,
            default,
            default,
            default);
        TypeReferenceHandle attributeType = metadata.AddTypeReference(
            assembly,
            metadata.GetOrAddString("System"),
            metadata.GetOrAddString("SampleAttribute"));
        var constructorSignature = new BlobBuilder();
        new BlobEncoder(constructorSignature).MethodSignature(
            SignatureCallingConvention.Default,
            genericParameterCount: 0,
            isInstanceMethod: true).Parameters(
                0,
                returnType => returnType.Void(),
                _ => { });
        MemberReferenceHandle constructor = metadata.AddMemberReference(
            attributeType,
            metadata.GetOrAddString(".ctor"),
            metadata.GetOrAddBlob(constructorSignature));
        var value = new BlobBuilder();
        value.WriteUInt16(1);
        value.WriteUInt16((ushort)namedArgumentCount);
        BlobHandle valueHandle = metadata.GetOrAddBlob(value);
        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        for (int index = 0; index < attributeCount; index++)
        {
            TypeDefinitionHandle type = metadata.AddTypeDefinition(
                TypeAttributes.Public | TypeAttributes.Abstract,
                metadata.GetOrAddString("Samples"),
                metadata.GetOrAddString($"Attributed{index}"),
                default,
                MetadataTokens.FieldDefinitionHandle(1),
                MetadataTokens.MethodDefinitionHandle(1));
            metadata.AddCustomAttribute(type, constructor, valueHandle);
        }

        return Serialize(metadata);
    }

    static byte[] BuildRefPropertyDuplicateSeq0TypeSpecImage(
        int returnParameterCount,
        int rank)
    {
        var metadata = Metadata("RefPropertySeq0TypeSpec");
        var typeSpecSignature = new BlobBuilder();
        typeSpecSignature.WriteByte(0x14);
        typeSpecSignature.WriteByte(0x08);
        typeSpecSignature.WriteCompressedInteger(rank);
        typeSpecSignature.WriteCompressedInteger(0);
        typeSpecSignature.WriteCompressedInteger(0);
        TypeSpecificationHandle typeSpec =
            metadata.AddTypeSpecification(metadata.GetOrAddBlob(typeSpecSignature));
        var constructorSignature = new BlobBuilder();
        new BlobEncoder(constructorSignature).MethodSignature(
            SignatureCallingConvention.Default,
            genericParameterCount: 0,
            isInstanceMethod: true).Parameters(
                0,
                returnType => returnType.Void(),
                _ => { });
        MemberReferenceHandle constructor = metadata.AddMemberReference(
            typeSpec,
            metadata.GetOrAddString(".ctor"),
            metadata.GetOrAddBlob(constructorSignature));
        BlobHandle attributeValue =
            metadata.GetOrAddBlob(new byte[] { 0x01, 0x00, 0x00, 0x00 });
        TypeDefinitionHandle type = AddModuleAndPublicType(metadata, "Host");
        ParameterHandle first = default;
        for (int i = 0; i < returnParameterCount; i++)
        {
            ParameterHandle parameter = metadata.AddParameter(
                ParameterAttributes.None,
                default,
                sequenceNumber: 0);
            if (i == 0)
                first = parameter;
            metadata.AddCustomAttribute(parameter, constructor, attributeValue);
        }

        var accessorSignature = new BlobBuilder();
        new BlobEncoder(accessorSignature).MethodSignature(
            SignatureCallingConvention.Default,
            genericParameterCount: 0,
            isInstanceMethod: true).Parameters(
                0,
                returnType => returnType.Type(isByRef: true).Int32(),
                _ => { });
        MethodDefinitionHandle accessor = metadata.AddMethodDefinition(
            MethodAttributes.Public
                | MethodAttributes.Abstract
                | MethodAttributes.Virtual,
            MethodImplAttributes.IL,
            metadata.GetOrAddString("get_Value"),
            metadata.GetOrAddBlob(accessorSignature),
            bodyOffset: -1,
            first);
        var propertySignature = new BlobBuilder();
        new BlobEncoder(propertySignature).PropertySignature(
            isInstanceProperty: true).Parameters(
                0,
                returnType => returnType.Type(isByRef: true).Int32(),
                _ => { });
        PropertyDefinitionHandle property = metadata.AddProperty(
            PropertyAttributes.None,
            metadata.GetOrAddString("Value"),
            metadata.GetOrAddBlob(propertySignature));
        metadata.AddPropertyMap(type, property);
        metadata.AddMethodSemantics(
            property,
            MethodSemanticsAttributes.Getter,
            accessor);
        return Serialize(metadata);
    }

    static byte[] BuildAccessorTypeSpecArrayAttributeImage(int rank)
    {
        var metadata = Metadata("AccessorTypeSpec");
        var typeSpecSignature = new BlobBuilder();
        typeSpecSignature.WriteByte(0x14);
        typeSpecSignature.WriteByte(0x08);
        typeSpecSignature.WriteCompressedInteger(rank);
        typeSpecSignature.WriteCompressedInteger(0);
        typeSpecSignature.WriteCompressedInteger(0);
        TypeSpecificationHandle typeSpec =
            metadata.AddTypeSpecification(metadata.GetOrAddBlob(typeSpecSignature));
        var constructorSignature = new BlobBuilder();
        new BlobEncoder(constructorSignature).MethodSignature(
            SignatureCallingConvention.Default,
            genericParameterCount: 0,
            isInstanceMethod: true).Parameters(
                1,
                returnType => returnType.Void(),
                parameters => parameters.AddParameter().Type().String());
        MemberReferenceHandle constructor = metadata.AddMemberReference(
            typeSpec,
            metadata.GetOrAddString(".ctor"),
            metadata.GetOrAddBlob(constructorSignature));
        TypeDefinitionHandle type = AddModuleAndPublicType(metadata, "Host");
        ParameterHandle returnParameter = metadata.AddParameter(
            ParameterAttributes.None,
            default,
            sequenceNumber: 0);
        var accessorSignature = new BlobBuilder();
        new BlobEncoder(accessorSignature).MethodSignature(
            SignatureCallingConvention.Default,
            genericParameterCount: 0,
            isInstanceMethod: true).Parameters(
                0,
                returnType => returnType.Type().Int32(),
                _ => { });
        MethodDefinitionHandle accessor = metadata.AddMethodDefinition(
            MethodAttributes.Public
                | MethodAttributes.Abstract
                | MethodAttributes.Virtual,
            MethodImplAttributes.IL,
            metadata.GetOrAddString("get_Value"),
            metadata.GetOrAddBlob(accessorSignature),
            bodyOffset: -1,
            returnParameter);
        var propertySignature = new BlobBuilder();
        new BlobEncoder(propertySignature).PropertySignature(
            isInstanceProperty: true).Parameters(
                0,
                returnType => returnType.Type().Int32(),
                _ => { });
        PropertyDefinitionHandle property = metadata.AddProperty(
            PropertyAttributes.None,
            metadata.GetOrAddString("Value"),
            metadata.GetOrAddBlob(propertySignature));
        metadata.AddPropertyMap(type, property);
        metadata.AddMethodSemantics(
            property,
            MethodSemanticsAttributes.Getter,
            accessor);
        metadata.AddCustomAttribute(
            accessor,
            constructor,
            metadata.GetOrAddBlob(new byte[] { 0x01, 0x00 }));
        return Serialize(metadata);
    }

    static byte[] BuildTypeSpecArrayAttributeImage(int rank, int typeCount)
    {
        var metadata = Metadata("AttributeTypeSpecBomb");
        var typeSpecSignature = new BlobBuilder();
        typeSpecSignature.WriteByte(0x14);
        typeSpecSignature.WriteByte(0x08);
        typeSpecSignature.WriteCompressedInteger(rank);
        typeSpecSignature.WriteCompressedInteger(0);
        typeSpecSignature.WriteCompressedInteger(0);
        TypeSpecificationHandle typeSpec =
            metadata.AddTypeSpecification(metadata.GetOrAddBlob(typeSpecSignature));
        var constructorSignature = new BlobBuilder();
        new BlobEncoder(constructorSignature).MethodSignature(
            SignatureCallingConvention.Default,
            genericParameterCount: 0,
            isInstanceMethod: true).Parameters(
                1,
                returnType => returnType.Void(),
                parameters => parameters.AddParameter().Type().String());
        MemberReferenceHandle constructor = metadata.AddMemberReference(
            typeSpec,
            metadata.GetOrAddString(".ctor"),
            metadata.GetOrAddBlob(constructorSignature));
        BlobHandle truncatedValue =
            metadata.GetOrAddBlob(new byte[] { 0x01, 0x00 });
        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        for (int index = 0; index < typeCount; index++)
        {
            TypeDefinitionHandle type = metadata.AddTypeDefinition(
                TypeAttributes.Public | TypeAttributes.Abstract,
                metadata.GetOrAddString("Samples"),
                metadata.GetOrAddString($"Host{index}"),
                default,
                MetadataTokens.FieldDefinitionHandle(1),
                MetadataTokens.MethodDefinitionHandle(1));
            metadata.AddCustomAttribute(type, constructor, truncatedValue);
        }
        return Serialize(metadata);
    }

    static byte[] BuildLocalExtensionFloodImage(
        int methodCount,
        int assemblyPublicKeyLength = 0)
    {
        var metadata = Metadata(
            "ExtensionFlood",
            assemblyPublicKeyLength == 0
                ? null
                : new byte[assemblyPublicKeyLength]);
        AssemblyReferenceHandle coreLibrary = metadata.AddAssemblyReference(
            metadata.GetOrAddString("System.Private.CoreLib"),
            new Version(11, 0, 0, 0),
            default,
            default,
            default,
            default);
        TypeReferenceHandle extensionAttribute = metadata.AddTypeReference(
            coreLibrary,
            metadata.GetOrAddString("System.Runtime.CompilerServices"),
            metadata.GetOrAddString("ExtensionAttribute"));
        var constructorSignature = new BlobBuilder();
        new BlobEncoder(constructorSignature).MethodSignature(
            SignatureCallingConvention.Default,
            genericParameterCount: 0,
            isInstanceMethod: true).Parameters(
                0,
                returnType => returnType.Void(),
                _ => { });
        MemberReferenceHandle constructor = metadata.AddMemberReference(
            extensionAttribute,
            metadata.GetOrAddString(".ctor"),
            metadata.GetOrAddBlob(constructorSignature));
        BlobHandle attributeValue =
            metadata.GetOrAddBlob(new byte[] { 0x01, 0x00, 0x00, 0x00 });
        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        TypeDefinitionHandle target = metadata.AddTypeDefinition(
            TypeAttributes.Public,
            metadata.GetOrAddString("Samples"),
            metadata.GetOrAddString("Target"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        TypeDefinitionHandle extensions = metadata.AddTypeDefinition(
            TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed,
            metadata.GetOrAddString("Samples"),
            metadata.GetOrAddString("Extensions"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        metadata.AddCustomAttribute(extensions, constructor, attributeValue);
        for (int index = 0; index < methodCount; index++)
        {
            TypeReferenceHandle discriminator = metadata.AddTypeReference(
                coreLibrary,
                metadata.GetOrAddString("Samples"),
                metadata.GetOrAddString($"T{index}"));
            var signature = new BlobBuilder();
            new BlobEncoder(signature).MethodSignature().Parameters(
                2,
                returnType => returnType.Void(),
                parameters =>
                {
                    parameters.AddParameter().Type().Type(
                        target,
                        isValueType: false);
                    parameters.AddParameter().Type().Type(
                        discriminator,
                        isValueType: false);
                });
            MethodDefinitionHandle method = metadata.AddMethodDefinition(
                MethodAttributes.Public
                    | MethodAttributes.Static
                    | MethodAttributes.Abstract,
                MethodImplAttributes.IL,
                metadata.GetOrAddString("M"),
                metadata.GetOrAddBlob(signature),
                bodyOffset: -1,
                MetadataTokens.ParameterHandle(1));
            metadata.AddCustomAttribute(method, constructor, attributeValue);
        }
        return Serialize(metadata);
    }

    static byte[] BuildRepeatedFinalizerImage(
        int typeCount,
        int publicKeyLength)
    {
        var metadata = Metadata("FinalizerTokenBomb");
        AssemblyReferenceHandle coreLibrary = metadata.AddAssemblyReference(
            metadata.GetOrAddString("System.Private.CoreLib"),
            new Version(11, 0, 0, 0),
            default,
            metadata.GetOrAddBlob(new byte[publicKeyLength]),
            AssemblyFlags.PublicKey,
            default);
        TypeReferenceHandle objectType = metadata.AddTypeReference(
            coreLibrary,
            metadata.GetOrAddString("System"),
            metadata.GetOrAddString("Object"));
        for (int index = 0; index < typeCount; index++)
        {
            metadata.AddTypeDefinition(
                TypeAttributes.Public,
                metadata.GetOrAddString("Samples"),
                metadata.GetOrAddString($"Finalizable{index}"),
                objectType,
                MetadataTokens.FieldDefinitionHandle(1),
                MetadataTokens.MethodDefinitionHandle(index + 1));
        }

        var signature = new BlobBuilder();
        new BlobEncoder(signature).MethodSignature(
            SignatureCallingConvention.Default,
            genericParameterCount: 0,
            isInstanceMethod: true).Parameters(
                0,
                returnType => returnType.Void(),
                _ => { });
        BlobHandle signatureHandle = metadata.GetOrAddBlob(signature);
        for (int index = 0; index < typeCount; index++)
        {
            metadata.AddMethodDefinition(
                MethodAttributes.Public
                    | MethodAttributes.Virtual
                    | MethodAttributes.HideBySig,
                MethodImplAttributes.IL,
                metadata.GetOrAddString("Finalize"),
                signatureHandle,
                bodyOffset: -1,
                MetadataTokens.ParameterHandle(1));
        }
        return Serialize(metadata);
    }

    static byte[] BuildForwarderImage(string typeName, string assemblyName)
    {
        var metadata = Metadata("ForwarderBomb");
        AssemblyReferenceHandle target = metadata.AddAssemblyReference(
            metadata.GetOrAddString(assemblyName),
            new Version(1, 0, 0, 0),
            default,
            default,
            default,
            default);
        metadata.AddExportedType(
            TypeAttributes.Public | (TypeAttributes)0x00200000,
            metadata.GetOrAddString("Samples"),
            metadata.GetOrAddString(typeName),
            target,
            typeDefinitionId: 0);
        return Serialize(metadata);
    }

    static byte[] BuildWideGenericAttributeImage(
        int argumentCount,
        int nameLength)
    {
        var metadata = Metadata("GenericAttributeBomb");
        AssemblyReferenceHandle contracts = metadata.AddAssemblyReference(
            metadata.GetOrAddString("Contracts"),
            new Version(1, 0, 0, 0),
            default,
            default,
            default,
            default);
        TypeReferenceHandle attributeType = metadata.AddTypeReference(
            contracts,
            metadata.GetOrAddString("Samples"),
            metadata.GetOrAddString($"GenericAttribute`{argumentCount}"));
        TypeReferenceHandle argumentType = metadata.AddTypeReference(
            contracts,
            metadata.GetOrAddString("Samples"),
            metadata.GetOrAddString(new string('A', nameLength)));
        var typeSpecSignature = new BlobBuilder();
        WriteWideGenericType(
            typeSpecSignature,
            attributeType,
            argumentType,
            argumentCount);
        TypeSpecificationHandle typeSpec =
            metadata.AddTypeSpecification(metadata.GetOrAddBlob(typeSpecSignature));
        var constructorSignature = new BlobBuilder();
        new BlobEncoder(constructorSignature).MethodSignature(
            SignatureCallingConvention.Default,
            genericParameterCount: 0,
            isInstanceMethod: true).Parameters(
                0,
                returnType => returnType.Void(),
                _ => { });
        MemberReferenceHandle constructor = metadata.AddMemberReference(
            typeSpec,
            metadata.GetOrAddString(".ctor"),
            metadata.GetOrAddBlob(constructorSignature));
        TypeDefinitionHandle host = AddModuleAndPublicType(metadata, "Host");
        var value = new BlobBuilder();
        value.WriteUInt16(1);
        value.WriteUInt16(0);
        metadata.AddCustomAttribute(
            host,
            constructor,
            metadata.GetOrAddBlob(value));
        return Serialize(metadata);
    }

    static byte[] BuildRepeatedEnumAttributeLookupImage(
        int typeCount,
        int namedArgumentCount,
        int attributeCount,
        bool poisonTypeDefinitionIndex = false)
    {
        var metadata = Metadata("EnumAttributeLookupBomb");
        AssemblyReferenceHandle contracts = metadata.AddAssemblyReference(
            metadata.GetOrAddString("Contracts"),
            new Version(1, 0, 0, 0),
            default,
            default,
            default,
            default);
        TypeReferenceHandle attributeType = metadata.AddTypeReference(
            contracts,
            metadata.GetOrAddString("Samples"),
            metadata.GetOrAddString("SampleAttribute"));
        var constructorSignature = new BlobBuilder();
        new BlobEncoder(constructorSignature).MethodSignature(
            SignatureCallingConvention.Default,
            genericParameterCount: 0,
            isInstanceMethod: true).Parameters(
                0,
                returnType => returnType.Void(),
                _ => { });
        MemberReferenceHandle constructor = metadata.AddMemberReference(
            attributeType,
            metadata.GetOrAddString(".ctor"),
            metadata.GetOrAddBlob(constructorSignature));

        AddModuleAndPublicType(metadata, "Host");
        if (poisonTypeDefinitionIndex)
        {
            TypeDefinitionHandle poison = metadata.AddTypeDefinition(
                TypeAttributes.NestedAssembly,
                default,
                metadata.GetOrAddString("Poison"),
                default,
                MetadataTokens.FieldDefinitionHandle(1),
                MetadataTokens.MethodDefinitionHandle(1));
            metadata.AddNestedType(poison, poison);
        }
        for (int i = 0; i < typeCount; i++)
        {
            metadata.AddTypeDefinition(
                TypeAttributes.Public | TypeAttributes.Abstract,
                metadata.GetOrAddString("Samples.Decoys.Namespace"),
                metadata.GetOrAddString($"Decoy{i}"),
                default,
                MetadataTokens.FieldDefinitionHandle(1),
                MetadataTokens.MethodDefinitionHandle(1));
        }

        TypeDefinitionHandle attributed = metadata.AddTypeDefinition(
            TypeAttributes.Public | TypeAttributes.Abstract,
            metadata.GetOrAddString("Samples"),
            metadata.GetOrAddString("Attributed"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        var value = new BlobBuilder();
        value.WriteUInt16(1);
        value.WriteUInt16((ushort)namedArgumentCount);
        for (int i = 0; i < namedArgumentCount; i++)
        {
            value.WriteByte(0x54);
            value.WriteByte(0x55);
            value.WriteSerializedString("NoSuchEnumType");
            value.WriteSerializedString("P");
            value.WriteInt32(1);
        }
        BlobHandle valueHandle = metadata.GetOrAddBlob(value);
        for (int i = 0; i < attributeCount; i++)
        {
            metadata.AddCustomAttribute(
                attributed,
                constructor,
                valueHandle);
        }
        return Serialize(metadata);
    }

    static byte[] BuildRepeatedParameterEnumAttributeLookupImage(
        int typeCount,
        int methodCount,
        string attributeNamespace,
        string attributeName)
    {
        var metadata = Metadata("ParamEnumAttributeLookupBomb");
        AssemblyReferenceHandle contracts = metadata.AddAssemblyReference(
            metadata.GetOrAddString("Contracts"),
            new Version(1, 0, 0, 0),
            default,
            default,
            default,
            default);
        TypeReferenceHandle attributeType = metadata.AddTypeReference(
            contracts,
            metadata.GetOrAddString(attributeNamespace),
            metadata.GetOrAddString(attributeName));
        var constructorSignature = new BlobBuilder();
        new BlobEncoder(constructorSignature).MethodSignature(
            SignatureCallingConvention.Default,
            genericParameterCount: 0,
            isInstanceMethod: true).Parameters(
                0,
                returnType => returnType.Void(),
                _ => { });
        MemberReferenceHandle constructor = metadata.AddMemberReference(
            attributeType,
            metadata.GetOrAddString(".ctor"),
            metadata.GetOrAddBlob(constructorSignature));

        AddModuleAndPublicType(metadata, "Host");
        for (int i = 0; i < typeCount; i++)
        {
            metadata.AddTypeDefinition(
                TypeAttributes.Public | TypeAttributes.Abstract,
                metadata.GetOrAddString("Samples.Decoys.Namespace"),
                metadata.GetOrAddString($"Decoy{i}"),
                default,
                MetadataTokens.FieldDefinitionHandle(1),
                MetadataTokens.MethodDefinitionHandle(methodCount + 1));
        }

        var methodSignature = new BlobBuilder();
        new BlobEncoder(methodSignature).MethodSignature(
            SignatureCallingConvention.Default,
            genericParameterCount: 0,
            isInstanceMethod: false).Parameters(
                1,
                returnType => returnType.Void(),
                parameters => parameters.AddParameter().Type().Int32());
        BlobHandle methodSignatureHandle = metadata.GetOrAddBlob(methodSignature);
        var value = new BlobBuilder();
        value.WriteUInt16(1);
        value.WriteUInt16(1);
        value.WriteByte(0x54);
        value.WriteByte(0x55);
        value.WriteSerializedString("NoSuchEnumType");
        value.WriteSerializedString("P");
        value.WriteInt32(1);
        BlobHandle valueHandle = metadata.GetOrAddBlob(value);
        for (int i = 0; i < methodCount; i++)
        {
            ParameterHandle parameter = metadata.AddParameter(
                ParameterAttributes.None,
                metadata.GetOrAddString("arg"),
                sequenceNumber: 1);
            metadata.AddCustomAttribute(parameter, constructor, valueHandle);
            metadata.AddMethodDefinition(
                MethodAttributes.Public
                    | MethodAttributes.Static
                    | MethodAttributes.HideBySig,
                MethodImplAttributes.IL,
                metadata.GetOrAddString($"M{i}"),
                methodSignatureHandle,
                bodyOffset: -1,
                parameter);
        }

        return Serialize(metadata);
    }

    static byte[] BuildNestedTypeReferenceFieldImage(int depth, int nameLength)
    {
        var metadata = Metadata("NestedTypeReferenceBomb");
        AssemblyReferenceHandle target = metadata.AddAssemblyReference(
            metadata.GetOrAddString("Contracts"),
            new Version(1, 0, 0, 0),
            default,
            default,
            default,
            default);
        EntityHandle scope = target;
        for (int index = 0; index < depth; index++)
        {
            scope = metadata.AddTypeReference(
                scope,
                index == 0
                    ? metadata.GetOrAddString("Contracts")
                    : default,
                metadata.GetOrAddString(new string('N', nameLength)));
        }

        AddModuleAndPublicType(metadata, "Host");
        var signature = new BlobBuilder();
        signature.WriteByte(0x06);
        signature.WriteByte(0x12);
        WriteTypeDefOrRef(signature, scope);
        metadata.AddFieldDefinition(
            FieldAttributes.Public | FieldAttributes.Static,
            metadata.GetOrAddString("Value"),
            metadata.GetOrAddBlob(signature));
        return Serialize(metadata);
    }

    static byte[] BuildLargeObsoleteAttributeImage(int messageLength)
    {
        var metadata = Metadata("ObsoleteBomb");
        AssemblyReferenceHandle runtime = metadata.AddAssemblyReference(
            metadata.GetOrAddString("System.Runtime"),
            new Version(11, 0, 0, 0),
            default,
            default,
            default,
            default);
        TypeReferenceHandle obsoleteType = metadata.AddTypeReference(
            runtime,
            metadata.GetOrAddString("System"),
            metadata.GetOrAddString("ObsoleteAttribute"));
        var constructorSignature = new BlobBuilder();
        new BlobEncoder(constructorSignature).MethodSignature(
            SignatureCallingConvention.Default,
            genericParameterCount: 0,
            isInstanceMethod: true).Parameters(
                1,
                returnType => returnType.Void(),
                parameters => parameters.AddParameter().Type().String());
        MemberReferenceHandle constructor = metadata.AddMemberReference(
            obsoleteType,
            metadata.GetOrAddString(".ctor"),
            metadata.GetOrAddBlob(constructorSignature));
        TypeDefinitionHandle type = AddModuleAndPublicType(
            metadata,
            "ObsoleteBomb");
        var value = new BlobBuilder(messageLength + 8);
        value.WriteUInt16(1);
        value.WriteCompressedInteger(messageLength);
        for (int index = 0; index < messageLength; index++)
            value.WriteByte((byte)'X');
        value.WriteUInt16(0);
        metadata.AddCustomAttribute(
            type,
            constructor,
            metadata.GetOrAddBlob(value));
        return Serialize(metadata);
    }

    public sealed class GenericApiSurfaceAttribute<T> : Attribute
    {
    }

    [GenericApiSurfaceAttribute<string>]
    public sealed class GenericAttributeBoundedFixture
    {
    }

    public sealed class LegalNamedAttribute : Attribute
    {
        public LegalNamedAttribute(string value)
        {
        }

        public int Count { get; set; }
    }

    [LegalNamed("ok", Count = 3)]
    public sealed class LegalNamedAttributeFixture
    {
    }

    public sealed class NestedEnumHost
    {
        public enum Wide : long
        {
            Value = 0x112233445566778
        }
    }

    public sealed class LegalNestedEnumAttribute : Attribute
    {
        public NestedEnumHost.Wide Choice { get; set; }
    }

    [LegalNestedEnum(Choice = NestedEnumHost.Wide.Value)]
    public sealed class LegalNestedEnumFixture
    {
    }

    public sealed class LegalGenericCtorAttribute<T> : Attribute
    {
        public LegalGenericCtorAttribute(T value)
        {
        }
    }

    [LegalGenericCtor<int>(5)]
    public sealed class LegalGenericCtorFixture
    {
    }

    static void AssertCompilerAttributeParity(
        string fixtureTypeName,
        string attributeMarker,
        string valueMarker)
    {
        using var unboundedStream = File.OpenRead(SelfPath);
        using var unboundedReader = new PEReader(unboundedStream);
        ApiSurface unbounded = ApiSurfaceExtractor.Extract(
            unboundedReader,
            ApiSurfaceExtractionScope.Public);
        ApiType unboundedType = Assert.Single(
            unbounded.Types,
            type => type.FullName.EndsWith(fixtureTypeName, StringComparison.Ordinal));
        using var boundedStream = File.OpenRead(SelfPath);
        using var boundedReader = new PEReader(boundedStream);
        var bounded = Assert.IsType<ApiSurfaceExtractionResult.Extracted>(
            ApiSurfaceExtractor.ExtractBounded(
                boundedReader,
                ApiSurfaceExtractionScope.Public,
                new ApiSurfaceExtractionBounds(
                    int.MaxValue,
                    int.MaxValue,
                    int.MaxValue,
                    int.MaxValue,
                    int.MaxValue,
                    int.MaxValue)));
        ApiType boundedType = Assert.Single(
            bounded.Surface.Types,
            type => type.FullName.EndsWith(fixtureTypeName, StringComparison.Ordinal));
        Assert.Equal(unboundedType.Attributes, boundedType.Attributes);
        Assert.Contains(
            unboundedType.Attributes,
            attribute => attribute.Contains(attributeMarker, StringComparison.Ordinal)
                && attribute.Contains(valueMarker, StringComparison.Ordinal));
    }

    static void WriteTypeDefOrRef(BlobBuilder signature, EntityHandle handle)
    {
        int tag = handle.Kind switch
        {
            HandleKind.TypeDefinition => 0,
            HandleKind.TypeReference => 1,
            HandleKind.TypeSpecification => 2,
            _ => throw new ArgumentOutOfRangeException(nameof(handle)),
        };
        signature.WriteCompressedInteger(
            MetadataTokens.GetRowNumber(handle) << 2 | tag);
    }

    public enum WideTypeSpecUse
    {
        Field,
        BaseType,
        Event,
        Interface,
        GenericConstraint,
    }

    enum AccessorOwner
    {
        Property,
        Event,
    }

    public enum TransformArrayKind
    {
        TupleElementNames,
        Nullable,
        Dynamic,
    }

    static MetadataBuilder Metadata(
        string assemblyName,
        byte[]? publicKey = null)
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
            publicKey is null
                ? default
                : metadata.GetOrAddBlob(publicKey),
            publicKey is null
                ? default
                : AssemblyFlags.PublicKey,
            default);
        return metadata;
    }

    static byte[] BuildInvalidModuleMvidImage()
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            generation: 0,
            metadata.GetOrAddString("InvalidMvid.dll"),
            MetadataTokens.GuidHandle(100),
            encId: default,
            encBaseId: default);
        metadata.AddAssembly(
            metadata.GetOrAddString("InvalidMvid"),
            new Version(1, 0, 0, 0),
            culture: default,
            publicKey: default,
            flags: default,
            hashAlgorithm: default);
        AddModuleAndPublicType(metadata, "Subject");
        return Serialize(metadata);
    }

    static TypeDefinitionHandle AddModuleAndPublicType(
        MetadataBuilder metadata,
        string name,
        TypeAttributes attributes = TypeAttributes.Public | TypeAttributes.Abstract)
    {
        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        return metadata.AddTypeDefinition(
            attributes,
            metadata.GetOrAddString("Samples"),
            metadata.GetOrAddString(name),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
    }

    static byte[] Serialize(MetadataBuilder metadata)
    {
        var pe = new ManagedPEBuilder(
            PEHeaderBuilder.CreateLibraryHeader(),
            new MetadataRootBuilder(metadata, suppressValidation: true),
            new BlobBuilder(),
            flags: CorFlags.ILOnly);
        var image = new BlobBuilder();
        pe.Serialize(image);
        return image.ToArray();
    }
}
