using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using ILInspector.Metadata;

namespace ILInspector.Metadata.Tests;

public sealed class JsonStringEnumConverterAttributeTests
{
    const string ProbeAssemblyIdentity =
        "Probe, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null";

    [Fact]
    public void GenericConverterDoesNotAliasNestedAndNamespaceTypeNames()
    {
        using var peReader = new PEReader(
            ImmutableArray.Create(BuildImage()));
        MetadataReader reader = peReader.GetMetadataReader();
        TypeDefinition topLevelEnum = reader.GetTypeDefinition(
            MetadataTokens.TypeDefinitionHandle(4));
        MetadataTypeDefinitionName definitionName =
            Assert.IsType<MetadataTypeDefinitionNameResult.Valid>(
                MetadataTypeDefinitionName.Create(
                    "N.Outer",
                    ImmutableArray.Create("E")))
                .Name;

        bool supported =
            AttributeReader.HasJsonStringEnumConverterAttribute(
                reader,
                topLevelEnum.GetCustomAttributes(),
                definitionName,
                new ApiAssemblyIdentity(
                    "Probe",
                    new Version(1, 0, 0, 0),
                    culture: null,
                    publicKeyToken: null));

        Assert.False(supported);
    }

    static byte[] BuildImage()
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
        AssemblyReferenceHandle systemTextJson =
            AddAssemblyReference(
                metadata,
                "System.Text.Json",
                "cc7b13ffcd2ddd51");
        AssemblyReferenceHandle systemRuntime =
            AddAssemblyReference(
                metadata,
                "System.Runtime",
                "b03f5f7f11d50a3a");
        TypeReferenceHandle systemType = metadata.AddTypeReference(
            systemRuntime,
            metadata.GetOrAddString("System"),
            metadata.GetOrAddString("Type"));
        TypeReferenceHandle systemEnum = metadata.AddTypeReference(
            systemRuntime,
            metadata.GetOrAddString("System"),
            metadata.GetOrAddString("Enum"));
        TypeReferenceHandle attributeType = metadata.AddTypeReference(
            systemTextJson,
            metadata.GetOrAddString(
                "System.Text.Json.Serialization"),
            metadata.GetOrAddString("JsonConverterAttribute"));
        var constructorSignature = new BlobBuilder();
        new BlobEncoder(constructorSignature).MethodSignature(
            SignatureCallingConvention.Default,
            genericParameterCount: 0,
            isInstanceMethod: true).Parameters(
            1,
            returnType => returnType.Void(),
            parameters =>
                parameters.AddParameter().Type().Type(
                    systemType,
                    isValueType: false));
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
        TypeDefinitionHandle outer = metadata.AddTypeDefinition(
            TypeAttributes.Public,
            metadata.GetOrAddString("N"),
            metadata.GetOrAddString("Outer"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        TypeDefinitionHandle nestedEnum = metadata.AddTypeDefinition(
            TypeAttributes.NestedPublic | TypeAttributes.Sealed,
            default,
            metadata.GetOrAddString("E"),
            systemEnum,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        metadata.AddNestedType(nestedEnum, outer);
        TypeDefinitionHandle topLevelEnum = metadata.AddTypeDefinition(
            TypeAttributes.Public | TypeAttributes.Sealed,
            metadata.GetOrAddString("N.Outer"),
            metadata.GetOrAddString("E"),
            systemEnum,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));

        var value = new BlobBuilder();
        value.WriteUInt16(1);
        value.WriteSerializedString(
            "System.Text.Json.Serialization.JsonStringEnumConverter`1"
            + $"[[N.Outer+E, {ProbeAssemblyIdentity}]], "
            + "System.Text.Json, Version=10.0.0.0, Culture=neutral, "
            + "PublicKeyToken=cc7b13ffcd2ddd51");
        value.WriteUInt16(0);
        metadata.AddCustomAttribute(
            topLevelEnum,
            constructor,
            metadata.GetOrAddBlob(value));

        var pe = new ManagedPEBuilder(
            PEHeaderBuilder.CreateLibraryHeader(),
            new MetadataRootBuilder(metadata, suppressValidation: true),
            new BlobBuilder(),
            flags: CorFlags.ILOnly);
        var image = new BlobBuilder();
        pe.Serialize(image);
        return image.ToArray();
    }

    static AssemblyReferenceHandle AddAssemblyReference(
        MetadataBuilder metadata,
        string name,
        string publicKeyToken) =>
        metadata.AddAssemblyReference(
            metadata.GetOrAddString(name),
            new Version(10, 0, 0, 0),
            default,
            metadata.GetOrAddBlob(
                Convert.FromHexString(publicKeyToken)),
            default,
            default);
}
