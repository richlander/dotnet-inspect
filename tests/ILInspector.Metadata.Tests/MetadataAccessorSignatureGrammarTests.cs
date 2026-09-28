using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

namespace ILInspector.Metadata.Tests;

public class MetadataAccessorSignatureGrammarTests
{
    [Theory]
    [MemberData(nameof(LegalPropertySignatures))]
    public void PropertySigAcceptsRuntimeAugmentedRetTypeAndParam(
        byte[] signature) =>
        Assert.True(IsValidProperty(signature));

    [Theory]
    [MemberData(nameof(MalformedPropertySignatures))]
    public void PropertySigRejectsIllegalHeadersAndRecursivePositions(
        byte[] signature) =>
        Assert.False(IsValidProperty(signature));

    [Theory]
    [MemberData(nameof(LegalMethodDefinitionSignatures))]
    public void MethodDefSigAcceptsCompleteCliForms(
        byte[] signature,
        int typeParameterCount,
        int methodParameterCount) =>
        Assert.True(
            IsValidMethod(
                signature,
                typeParameterCount,
                methodParameterCount));

    [Theory]
    [MemberData(nameof(MalformedMethodDefinitionSignatures))]
    public void MethodDefSigRejectsIllegalHeadersContextsAndPositions(
        byte[] signature,
        int typeParameterCount,
        int methodParameterCount) =>
        Assert.False(
            IsValidMethod(
                signature,
                typeParameterCount,
                methodParameterCount));

    [Theory]
    [MemberData(nameof(LegalEventTypeSignatures))]
    public void EventTypeSpecAcceptsTypeGrammar(byte[] signature) =>
        Assert.True(IsValidEventType(signature));

    [Theory]
    [MemberData(nameof(MalformedEventTypeSignatures))]
    public void EventTypeSpecRejectsNonTypePositions(byte[] signature) =>
        Assert.False(IsValidEventType(signature));

    public static IEnumerable<object[]> LegalPropertySignatures()
    {
        yield return Case([0x08, 0x00, 0x01]);
        yield return Case([0x28, 0x00, 0x10, 0x08]);
        yield return Case([0x28, 0x00, 0x16]);
        yield return Case([0x28, 0x00, 0x0F, 0x01]);
        yield return Case(
            [0x28, 0x00, 0x10, 0x1F, ModifierToken, 0x08]);
        yield return Case(
            [0x28, 0x01, 0x08, 0x1F, ModifierToken, 0x10, 0x08]);
        yield return Case(
            [0x28, 0x00, 0x15, 0x12, GenericTypeToken, 0x01, 0x08]);
        yield return Case(
            [0x28, 0x00, 0x1B, 0x00, 0x01, 0x01, 0x16]);
        yield return Case(
            [0x28, 0x00, 0x1B, 0x05, 0x01, 0x01, 0x08]);
        yield return Case(
            [0x28, 0x00, 0x1B, 0x10, 0x01, 0x00, 0x1E, 0x00]);
    }

    public static IEnumerable<object[]> MalformedPropertySignatures()
    {
        yield return Case([0x00, 0x00, 0x08]);
        yield return Case([0x28, 0x00, 0x1D, 0x01]);
        yield return Case([0x28, 0x00, 0x1D, 0x10, 0x08]);
        yield return Case([0x28, 0x00, 0x1D, 0x16]);
        yield return Case([0x28, 0x00, 0x45, 0x08]);
        yield return Case(
            [0x28, 0x00, 0x1F, TypeSpecificationToken, 0x08]);
        yield return Case(
            [0x28, 0x00, 0x15, 0x12, GenericTypeToken, 0x00]);
        yield return Case(
            [
                0x28,
                0x00,
                0x15,
                0x12,
                GenericTypeToken,
                0x02,
                0x08,
                0x08,
            ]);
        yield return Case(
            [0x28, 0x00, 0x1B, 0x05, 0x02, 0x01, 0x08, 0x41, 0x08]);
    }

    public static IEnumerable<object[]>
        LegalMethodDefinitionSignatures()
    {
        yield return Case([0x00, 0x00, 0x01], 0, 0);
        yield return Case([0x05, 0x01, 0x01, 0x08], 0, 0);
        yield return Case([0x20, 0x00, 0x01], 0, 0);
        yield return Case([0x60, 0x00, 0x01], 0, 0);
        yield return Case(
            [0x10, 0x01, 0x00, 0x1E, 0x00],
            0,
            1);
        yield return Case([0x00, 0x00, 0x13, 0x00], 1, 0);
        yield return Case(
            [0x00, 0x01, 0x1F, ModifierToken, 0x01, 0x16],
            0,
            0);
        yield return Case(
            [0x00, 0x01, 0x10, 0x08, 0x10, 0x08],
            0,
            0);
    }

    public static IEnumerable<object[]>
        MalformedMethodDefinitionSignatures()
    {
        yield return Case([0x80, 0x00, 0x01], 0, 0);
        yield return Case([0x01, 0x00, 0x01], 0, 0);
        yield return Case([0x40, 0x00, 0x01], 0, 0);
        yield return Case(
            [0x05, 0x02, 0x01, 0x08, 0x41, 0x08],
            0,
            0);
        yield return Case([0x10, 0x00, 0x00, 0x01], 0, 0);
        yield return Case(
            [0x10, 0x01, 0x00, 0x1E, 0x00],
            0,
            0);
        yield return Case([0x00, 0x00, 0x01], 0, 1);
        yield return Case(
            [0x10, 0x01, 0x00, 0x1E, 0x01],
            0,
            1);
        yield return Case(
            [0x00, 0x01, 0x01, 0x1D, 0x01],
            0,
            0);
        yield return Case(
            [0x00, 0x00, 0x1B, 0x08, 0x00, 0x01],
            0,
            0);
        yield return Case(
            [0x00, 0x00, 0x1F, TypeSpecificationToken, 0x08],
            0,
            0);
    }

    public static IEnumerable<object[]> LegalEventTypeSignatures()
    {
        yield return Case([0x08]);
        yield return Case([0x0F, 0x01]);
        yield return Case([0x1F, ModifierToken, 0x1D, 0x08]);
        yield return Case(
            [0x15, 0x12, GenericTypeToken, 0x01, 0x08]);
        yield return Case([0x1B, 0x00, 0x00, 0x01]);
    }

    public static IEnumerable<object[]> MalformedEventTypeSignatures()
    {
        yield return Case([0x01]);
        yield return Case([0x10, 0x08]);
        yield return Case([0x16]);
        yield return Case([0x45, 0x08]);
        yield return Case([0x1D, 0x01]);
        yield return Case([0x1F, TypeSpecificationToken, 0x08]);
        yield return Case([0x1B, 0x08, 0x00, 0x01]);
        yield return Case([0x14, 0x08, 0x00, 0x00, 0x00]);
    }

    static object[] Case(byte[] signature) => [signature];

    static object[] Case(
        byte[] signature,
        int typeParameterCount,
        int methodParameterCount) =>
        [signature, typeParameterCount, methodParameterCount];

    static bool IsValidProperty(byte[] signature)
    {
        try
        {
            using var fixture = new Fixture(
                BuildPropertyImage(signature));
            MetadataReader reader = fixture.Reader;
            PropertyDefinitionHandle propertyHandle =
                Assert.Single(reader.PropertyDefinitions);
            PropertyDefinition property =
                reader.GetPropertyDefinition(propertyHandle);
            TypeDefinition owner = TargetType(reader);
            GenericContext generic =
                GenericContext.ForType(reader, owner);
            if (SignatureBlobGuard.ValidateComplete(
                    reader,
                    property.Signature,
                    SignatureBlobGuard.Kind.Property)
                != SignatureBlobGuard.CompleteValidationKind.Valid)
            {
                return false;
            }

            var decoded = GuardedProviderDecode.PropertyResult(
                reader,
                property,
                TypeNodeProvider.Instance,
                generic,
                (TypeNode)new DegradedTypeNode());
            return !decoded.IsDegraded
                && !decoded.Value.ReturnType.IsDegraded
                && decoded.Value.ParameterTypes.All(
                    static parameter => !parameter.IsDegraded)
                && MetadataStructuralTypeValidator
                    .ValidatePropertySignature(
                        decoded.Value,
                        generic.TypeParameters.Count,
                        "PropertySig") is null;
        }
        catch (BadImageFormatException)
        {
            return false;
        }
    }

    static bool IsValidMethod(
        byte[] signature,
        int typeParameterCount,
        int methodParameterCount)
    {
        try
        {
            using var fixture = new Fixture(
                BuildMethodImage(
                    signature,
                    typeParameterCount,
                    methodParameterCount));
            MetadataReader reader = fixture.Reader;
            MethodDefinition method =
                reader.GetMethodDefinition(
                    Assert.Single(reader.MethodDefinitions));
            TypeDefinition owner = TargetType(reader);
            GenericContext generic =
                GenericContext.ForMethod(reader, owner, method);
            if (SignatureBlobGuard.ValidateComplete(
                    reader,
                    method.Signature,
                    SignatureBlobGuard.Kind.MethodDefinition)
                != SignatureBlobGuard.CompleteValidationKind.Valid)
            {
                return false;
            }

            var decoded = GuardedProviderDecode.MethodResult(
                reader,
                method,
                TypeNodeProvider.Instance,
                generic,
                (TypeNode)new DegradedTypeNode());
            return !decoded.IsDegraded
                && !decoded.Value.ReturnType.IsDegraded
                && decoded.Value.ParameterTypes.All(
                    static parameter => !parameter.IsDegraded)
                && MetadataStructuralTypeValidator
                    .ValidateMethodDefinitionSignature(
                        decoded.Value,
                        generic.TypeParameters.Count,
                        generic.MethodParameters.Count,
                        "MethodDefSig") is null;
        }
        catch (BadImageFormatException)
        {
            return false;
        }
    }

    static bool IsValidEventType(byte[] signature)
    {
        try
        {
            using var fixture = new Fixture(
                BuildEventTypeImage(signature));
            MetadataReader reader = fixture.Reader;
            EventDefinition @event =
                reader.GetEventDefinition(
                    Assert.Single(reader.EventDefinitions));
            TypeDefinition owner = TargetType(reader);
            GenericContext generic =
                GenericContext.ForType(reader, owner);
            var handle = (TypeSpecificationHandle)@event.Type;
            BlobHandle blob =
                reader.GetTypeSpecification(handle).Signature;
            if (SignatureBlobGuard.ValidateComplete(
                    reader,
                    blob,
                    SignatureBlobGuard.Kind.Type)
                != SignatureBlobGuard.CompleteValidationKind.Valid)
            {
                return false;
            }

            TypeNode decoded = GuardedProviderDecode.TypeSpec(
                reader,
                handle,
                TypeNodeProvider.Instance,
                generic,
                (TypeNode)new DegradedTypeNode());
            return !decoded.IsDegraded
                && MetadataStructuralTypeValidator
                    .ValidateTypeSignature(
                        decoded,
                        generic.TypeParameters.Count,
                        generic.MethodParameters.Count,
                        "EventType") is null;
        }
        catch (BadImageFormatException)
        {
            return false;
        }
    }

    static byte[] BuildPropertyImage(byte[] signature)
    {
        MetadataBuilder metadata = CreateMetadata();
        AddModuleType(metadata);
        TypeDefinitionHandle owner = AddTargetType(metadata);
        PropertyDefinitionHandle property =
            metadata.AddProperty(
                PropertyAttributes.None,
                metadata.GetOrAddString("Value"),
                AddBlob(metadata, signature));
        metadata.AddPropertyMap(owner, property);
        return Serialize(metadata);
    }

    static byte[] BuildMethodImage(
        byte[] signature,
        int typeParameterCount,
        int methodParameterCount)
    {
        MetadataBuilder metadata = CreateMetadata();
        MethodDefinitionHandle method =
            metadata.AddMethodDefinition(
                MethodAttributes.Public,
                MethodImplAttributes.IL,
                metadata.GetOrAddString("Accessor"),
                AddBlob(metadata, signature),
                bodyOffset: -1,
                MetadataTokens.ParameterHandle(1));
        AddModuleType(metadata);
        TypeDefinitionHandle owner = AddTargetType(metadata);
        AddGenericParameters(
            metadata,
            owner,
            typeParameterCount,
            "T");
        AddGenericParameters(
            metadata,
            method,
            methodParameterCount,
            "M");
        return Serialize(metadata);
    }

    static byte[] BuildEventTypeImage(byte[] signature)
    {
        MetadataBuilder metadata = CreateMetadata();
        AddModuleType(metadata);
        TypeDefinitionHandle owner = AddTargetType(metadata);
        TypeSpecificationHandle type =
            metadata.AddTypeSpecification(
                AddBlob(metadata, signature));
        EventDefinitionHandle @event =
            metadata.AddEvent(
                EventAttributes.None,
                metadata.GetOrAddString("Value"),
                type);
        metadata.AddEventMap(owner, @event);
        return Serialize(metadata);
    }

    static MetadataBuilder CreateMetadata()
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            0,
            metadata.GetOrAddString("Probe.dll"),
            metadata.GetOrAddGuid(Guid.NewGuid()),
            default,
            default);
        metadata.AddAssembly(
            metadata.GetOrAddString("Probe"),
            new Version(1, 0, 0, 0),
            default,
            default,
            default,
            default);
        AssemblyReferenceHandle coreLibrary =
            metadata.AddAssemblyReference(
                metadata.GetOrAddString("System.Runtime"),
                new Version(11, 0, 0, 0),
                default,
                default,
                default,
                default);
        metadata.AddTypeReference(
            coreLibrary,
            metadata.GetOrAddString("System.Runtime.CompilerServices"),
            metadata.GetOrAddString("IsExternalInit"));
        metadata.AddTypeReference(
            coreLibrary,
            metadata.GetOrAddString("Samples"),
            metadata.GetOrAddString("Generic`1"));
        metadata.AddTypeSpecification(
            AddBlob(metadata, [0x08]));
        return metadata;
    }

    static void AddModuleType(MetadataBuilder metadata) =>
        metadata.AddTypeDefinition(
            TypeAttributes.NotPublic,
            default,
            metadata.GetOrAddString("<Module>"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));

    static TypeDefinitionHandle AddTargetType(
        MetadataBuilder metadata) =>
        metadata.AddTypeDefinition(
            TypeAttributes.Public,
            metadata.GetOrAddString("Samples"),
            metadata.GetOrAddString("Target"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));

    static void AddGenericParameters(
        MetadataBuilder metadata,
        EntityHandle owner,
        int count,
        string prefix)
    {
        for (int index = 0; index < count; index++)
        {
            metadata.AddGenericParameter(
                owner,
                GenericParameterAttributes.None,
                metadata.GetOrAddString($"{prefix}{index}"),
                index);
        }
    }

    static BlobHandle AddBlob(
        MetadataBuilder metadata,
        byte[] bytes)
    {
        var blob = new BlobBuilder();
        blob.WriteBytes(bytes);
        return metadata.GetOrAddBlob(blob);
    }

    static byte[] Serialize(MetadataBuilder metadata)
    {
        var image = new BlobBuilder();
        new ManagedPEBuilder(
            PEHeaderBuilder.CreateLibraryHeader(),
            new MetadataRootBuilder(
                metadata,
                suppressValidation: true),
            new BlobBuilder(),
            flags: CorFlags.ILOnly)
            .Serialize(image);
        return image.ToArray();
    }

    static TypeDefinition TargetType(MetadataReader reader) =>
        reader.GetTypeDefinition(reader.TypeDefinitions.Skip(1).Single());

    const byte ModifierToken = 0x05;
    const byte GenericTypeToken = 0x09;
    const byte TypeSpecificationToken = 0x06;

    sealed class Fixture : IDisposable
    {
        readonly MemoryStream _stream;
        readonly PEReader _pe;

        internal Fixture(byte[] image)
        {
            _stream = new MemoryStream(image, writable: false);
            _pe = new PEReader(_stream);
            Reader = _pe.GetMetadataReader();
        }

        internal MetadataReader Reader { get; }

        public void Dispose()
        {
            _pe.Dispose();
            _stream.Dispose();
        }
    }
}
