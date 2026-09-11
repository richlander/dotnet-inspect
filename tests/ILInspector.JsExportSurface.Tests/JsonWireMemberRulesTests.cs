using System.Reflection.PortableExecutable;
using ILInspector.JsExportSurface.Fixtures;
using ILInspector.Metadata;

namespace ILInspector.JsExportSurface.Tests;

public sealed class JsonWireMemberRulesTests
{
    static ApiMember Property(
        params IEnumerable<JsonWireIgnoreCondition?> conditions) =>
        new()
        {
            Name = "Value",
            Kind = "property",
            HasGetter = true,
            ReturnType = "int",
            IndexParameterCount = 0,
            JsonIgnoreConditions = [.. conditions],
        };

    /// <summary>
    /// The directional table: only <c>WhenWriting</c> and <c>WhenReading</c>
    /// split the two directions, and the value-dependent conditions stay
    /// conservatively absent from both.
    /// </summary>
    [Theory]
    [InlineData(null, true, true)]
    [InlineData(JsonWireIgnoreCondition.Never, true, true)]
    [InlineData(JsonWireIgnoreCondition.Always, false, false)]
    [InlineData(JsonWireIgnoreCondition.WhenWritingDefault, false, false)]
    [InlineData(JsonWireIgnoreCondition.WhenWritingNull, false, false)]
    [InlineData(JsonWireIgnoreCondition.WhenWriting, false, true)]
    [InlineData(JsonWireIgnoreCondition.WhenReading, true, false)]
    public void DirectionalIgnoreConditionsSelectDirections(
        JsonWireIgnoreCondition? condition,
        bool serialized,
        bool deserialized)
    {
        ApiMember member = condition is { } value
            ? Property(value)
            : Property();

        Assert.Equal(
            serialized,
            JsonWireMemberRules.IsSerialized(
                member,
                JsonWireDirection.Serialize));
        Assert.Equal(
            deserialized,
            JsonWireMemberRules.IsSerialized(
                member,
                JsonWireDirection.Deserialize));
        Assert.Equal(
            serialized || deserialized,
            JsonWireMemberRules.IsSerialized(member));
        Assert.Equal(
            serialized != deserialized,
            JsonWireMemberRules.IsDirectionSensitive(member));
    }

    [Fact]
    public void MalformedIgnoreRowIsExcludedFromEveryDirection()
    {
        ApiMember member = Property([null]);

        Assert.True(
            JsonWireMemberRules.HasUnsupportedJsonIgnoreMetadata(member));
        Assert.False(JsonWireMemberRules.IsSerialized(member));
        Assert.False(
            JsonWireMemberRules.IsSerialized(
                member,
                JsonWireDirection.Serialize));
        Assert.False(
            JsonWireMemberRules.IsSerialized(
                member,
                JsonWireDirection.Deserialize));
        Assert.False(JsonWireMemberRules.IsDirectionSensitive(member));
    }

    [Fact]
    public void DuplicateIgnoreRowsAreExcludedFromEveryDirection()
    {
        ApiMember member = Property(
            JsonWireIgnoreCondition.Never,
            JsonWireIgnoreCondition.WhenReading);

        Assert.True(
            JsonWireMemberRules.HasUnsupportedJsonIgnoreMetadata(member));
        Assert.False(JsonWireMemberRules.IsSerialized(member));
    }

    [Fact]
    public void MalformedIncludeRowIsExcludedFromEveryDirection()
    {
        ApiMember member = Property();
        member.HasMalformedJsonInclude = true;

        Assert.True(
            JsonWireMemberRules.HasUnsupportedJsonIncludeMetadata(member));
        Assert.False(JsonWireMemberRules.IsSerialized(member));
    }

    [Fact]
    public void StaticAndCompilerGeneratedMembersRemainExcluded()
    {
        ApiMember member = Property(JsonWireIgnoreCondition.WhenReading);
        member.IsStatic = true;

        Assert.False(
            JsonWireMemberRules.IsSerialized(
                member,
                JsonWireDirection.Serialize));
    }

    [Fact]
    public void IndexersAndUnprovenPropertySignaturesRemainExcluded()
    {
        ApiMember indexer = Property();
        indexer.IndexParameterCount = 1;
        ApiMember unproven = Property();
        unproven.IndexParameterCount = null;

        Assert.False(JsonWireMemberRules.IsSerialized(indexer));
        Assert.False(JsonWireMemberRules.IsSerialized(unproven));
        Assert.True(JsonWireMemberRules.IsSerialized(Property()));
    }

    [Fact]
    public void PropertyAccessorsSelectTheirOwnWireDirections()
    {
        ApiMember publicSetter = Property();
        publicSetter.HasGetter = true;
        publicSetter.GetterAccessibility = "private";
        publicSetter.HasSetter = true;
        publicSetter.SetterAccessibility = null;

        Assert.False(
            JsonWireMemberRules.IsSerialized(
                publicSetter,
                JsonWireDirection.Serialize));
        Assert.True(
            JsonWireMemberRules.IsSerialized(
                publicSetter,
                JsonWireDirection.Deserialize));

        ApiMember privateSetter = Property();
        privateSetter.HasSetter = true;
        privateSetter.SetterAccessibility = "private";

        Assert.True(
            JsonWireMemberRules.IsSerialized(
                privateSetter,
                JsonWireDirection.Serialize));
        Assert.False(
            JsonWireMemberRules.IsSerialized(
                privateSetter,
                JsonWireDirection.Deserialize));

        ApiMember includedPrivateGetter = Property();
        includedPrivateGetter.HasGetter = true;
        includedPrivateGetter.GetterAccessibility = "private";
        includedPrivateGetter.HasSetter = true;
        includedPrivateGetter.HasJsonInclude = true;

        Assert.True(
            JsonWireMemberRules.IsSerialized(
                includedPrivateGetter,
                JsonWireDirection.Serialize));
        Assert.True(
            JsonWireMemberRules.IsSerialized(
                includedPrivateGetter,
                JsonWireDirection.Deserialize));

        ApiMember includedPrivateSetter = Property();
        includedPrivateSetter.HasSetter = true;
        includedPrivateSetter.SetterAccessibility = "private";
        includedPrivateSetter.HasJsonInclude = true;

        Assert.True(
            JsonWireMemberRules.IsSerialized(
                includedPrivateSetter,
                JsonWireDirection.Serialize));
        Assert.True(
            JsonWireMemberRules.IsSerialized(
                includedPrivateSetter,
                JsonWireDirection.Deserialize));
    }

    [Fact]
    public void JsonIncludedFieldsParticipateRegardlessOfAccessibility()
    {
        var privateField = new ApiMember
        {
            Name = "Value",
            Kind = "field",
            Accessibility = "private",
            HasJsonInclude = true,
        };

        Assert.True(JsonWireMemberRules.IsSerialized(privateField));
        Assert.True(
            JsonWireMemberRules.IsSerialized(
                privateField,
                JsonWireDirection.Serialize));
        Assert.True(
            JsonWireMemberRules.IsSerialized(
                privateField,
                JsonWireDirection.Deserialize));
    }

    [Fact]
    public void JsonIncludedMembersRequireAccessibleSameAssemblyValueTypes()
    {
        ApiAssemblyIdentity assembly = new(
            "Fixture",
            new Version(1, 0, 0, 0),
            culture: null,
            publicKeyToken: null);
        MetadataTypeDefinitionName hiddenDefinition =
            Assert.IsType<MetadataTypeDefinitionNameResult.Valid>(
                MetadataTypeDefinitionName.Create(
                    "Fixture",
                    ["Dto", "HiddenValue"]))
                .Name;
        ApiTypeReferenceIdentity hiddenReference = new(
            assembly,
            "Fixture.Dto.HiddenValue",
            hiddenDefinition);
        var hiddenType = new ApiType
        {
            Namespace = "Fixture",
            Name = "Dto.HiddenValue",
            DefinitionName = hiddenDefinition,
            Accessibility = "private",
            Kind = "enum",
        };
        ApiMember member = Property();
        member.HasJsonInclude = true;
        member.SignatureModel = new ApiSignature
        {
            ReturnType = "Fixture.Dto.HiddenValue",
            ReturnTypeReferences = [hiddenReference],
        };

        Assert.False(
            JsonWireMemberRules.IsSerialized(
                member,
                assembly,
                new Dictionary<ApiTypeReferenceIdentity, ApiType>
                {
                    [hiddenReference] = hiddenType,
                }));
    }

    [Fact]
    public void ContextRelativeAccessibilityIgnoresMembersOutsideTheWireContract()
    {
        ApiAssemblyIdentity assembly = new(
            "Fixture",
            new Version(1, 0, 0, 0),
            culture: null,
            publicKeyToken: null);
        MetadataTypeDefinitionName dtoDefinition =
            Assert.IsType<MetadataTypeDefinitionNameResult.Valid>(
                MetadataTypeDefinitionName.Create(
                    "Fixture",
                    ["Dto"]))
                .Name;
        MetadataTypeDefinitionName hiddenDefinition =
            Assert.IsType<MetadataTypeDefinitionNameResult.Valid>(
                MetadataTypeDefinitionName.Create(
                    "Fixture",
                    ["Dto", "HiddenValue"]))
                .Name;
        MetadataTypeDefinitionName contextDefinition =
            Assert.IsType<MetadataTypeDefinitionNameResult.Valid>(
                MetadataTypeDefinitionName.Create(
                    "Fixture",
                    ["Dto", "NestedContextJsonContext"]))
                .Name;
        ApiTypeReferenceIdentity dtoReference = new(
            assembly,
            "Fixture.Dto",
            dtoDefinition);
        ApiTypeReferenceIdentity hiddenReference = new(
            assembly,
            "Fixture.Dto.HiddenValue",
            hiddenDefinition);
        var typesByScopedIdentity =
            new Dictionary<ApiTypeReferenceIdentity, ApiType>
            {
                [dtoReference] = new ApiType
                {
                    Namespace = "Fixture",
                    Name = "Dto",
                    DefinitionName = dtoDefinition,
                    Kind = "class",
                },
                [hiddenReference] = new ApiType
                {
                    Namespace = "Fixture",
                    Name = "Dto.HiddenValue",
                    DefinitionName = hiddenDefinition,
                    Accessibility = "private",
                    Kind = "enum",
                },
            };

        ApiMember ignored = new()
        {
            Name = "Ignored",
            Kind = "field",
            HasJsonInclude = true,
            JsonIgnoreConditions = [JsonWireIgnoreCondition.Always],
            SignatureModel = new ApiSignature
            {
                ReturnTypeReferences = [hiddenReference],
            },
        };
        ApiMember @static = new()
        {
            Name = "Static",
            Kind = "field",
            HasJsonInclude = true,
            IsStatic = true,
            SignatureModel = new ApiSignature
            {
                ReturnTypeReferences = [hiddenReference],
            },
        };
        ApiMember indexer = new()
        {
            Name = "Item",
            Kind = "property",
            HasGetter = true,
            HasSetter = true,
            HasJsonInclude = true,
            IndexParameterCount = 1,
            SignatureModel = new ApiSignature
            {
                Parameters =
                [
                    new ApiParameter
                    {
                        Name = "index",
                        Type = "int",
                    },
                ],
                ReturnTypeReferences = [hiddenReference],
            },
        };

        Assert.False(
            JsonWireMemberRules
                .RequiresContextRelativeValueTypeAccessibilityEvidence(
                    ignored,
                    assembly,
                    typesByScopedIdentity,
                    contextDefinition));
        Assert.False(
            JsonWireMemberRules
                .RequiresContextRelativeValueTypeAccessibilityEvidence(
                    @static,
                    assembly,
                    typesByScopedIdentity,
                    contextDefinition));
        Assert.False(
            JsonWireMemberRules
                .RequiresContextRelativeValueTypeAccessibilityEvidence(
                    indexer,
                    assembly,
                    typesByScopedIdentity,
                    contextDefinition));
    }

    [Fact]
    public void ContextRelativeAccessibilityIncludesConstructorBoundDeserializeMembers()
    {
        ApiAssemblyIdentity assembly = new(
            "Fixture",
            new Version(1, 0, 0, 0),
            culture: null,
            publicKeyToken: null);
        MetadataTypeDefinitionName dtoDefinition =
            Assert.IsType<MetadataTypeDefinitionNameResult.Valid>(
                MetadataTypeDefinitionName.Create(
                    "Fixture",
                    ["Dto"]))
                .Name;
        MetadataTypeDefinitionName hiddenDefinition =
            Assert.IsType<MetadataTypeDefinitionNameResult.Valid>(
                MetadataTypeDefinitionName.Create(
                    "Fixture",
                    ["Dto", "HiddenValue"]))
                .Name;
        MetadataTypeDefinitionName contextDefinition =
            Assert.IsType<MetadataTypeDefinitionNameResult.Valid>(
                MetadataTypeDefinitionName.Create(
                    "Fixture",
                    ["Dto", "NestedContextJsonContext"]))
                .Name;
        ApiTypeReferenceIdentity dtoReference = new(
            assembly,
            "Fixture.Dto",
            dtoDefinition);
        ApiTypeReferenceIdentity hiddenReference = new(
            assembly,
            "Fixture.Dto.HiddenValue",
            hiddenDefinition);
        var declaringType = new ApiType
        {
            Namespace = "Fixture",
            Name = "Dto",
            DefinitionName = dtoDefinition,
            Kind = "class",
        };
        var member = new ApiMember
        {
            Name = "Hidden",
            Kind = "property",
            HasGetter = true,
            HasSetter = false,
            HasJsonInclude = true,
            SignatureModel = new ApiSignature
            {
                ReturnTypeReferences = [hiddenReference],
            },
        };
        declaringType.Members =
        [
            member,
            new ApiMember
            {
                Name = ".ctor",
                Kind = "constructor",
                SignatureModel = new ApiSignature
                {
                    Parameters =
                    [
                        new ApiParameter
                        {
                            Name = "hidden",
                            Type = "Fixture.Dto.HiddenValue",
                        },
                    ],
                },
            },
        ];
        var typesByScopedIdentity =
            new Dictionary<ApiTypeReferenceIdentity, ApiType>
            {
                [dtoReference] = declaringType,
                [hiddenReference] = new ApiType
                {
                    Namespace = "Fixture",
                    Name = "Dto.HiddenValue",
                    DefinitionName = hiddenDefinition,
                    Accessibility = "private",
                    Kind = "enum",
                },
            };

        Assert.True(
            JsonWireMemberRules
                .RequiresContextRelativeValueTypeAccessibilityEvidence(
                    declaringType,
                    member,
                    JsonWireDirection.Deserialize,
                    assembly,
                    typesByScopedIdentity,
                    contextDefinition));
    }

    [Fact]
    public void ContextRelativeAccessibilityTreatsDerivedContextAsProtectedAccess()
    {
        ApiAssemblyIdentity assembly = new(
            "Fixture",
            new Version(1, 0, 0, 0),
            culture: null,
            publicKeyToken: null);
        MetadataTypeDefinitionName baseDefinition =
            Assert.IsType<MetadataTypeDefinitionNameResult.Valid>(
                MetadataTypeDefinitionName.Create(
                    "Fixture",
                    ["BaseOwner"]))
                .Name;
        MetadataTypeDefinitionName derivedDefinition =
            Assert.IsType<MetadataTypeDefinitionNameResult.Valid>(
                MetadataTypeDefinitionName.Create(
                    "Fixture",
                    ["DerivedOwner"]))
                .Name;
        MetadataTypeDefinitionName hiddenDefinition =
            Assert.IsType<MetadataTypeDefinitionNameResult.Valid>(
                MetadataTypeDefinitionName.Create(
                    "Fixture",
                    ["BaseOwner", "HiddenValue"]))
                .Name;
        MetadataTypeDefinitionName contextDefinition =
            Assert.IsType<MetadataTypeDefinitionNameResult.Valid>(
                MetadataTypeDefinitionName.Create(
                    "Fixture",
                    ["DerivedOwner", "NestedContextJsonContext"]))
                .Name;
        ApiTypeReferenceIdentity baseReference = new(
            assembly,
            "Fixture.BaseOwner",
            baseDefinition);
        ApiTypeReferenceIdentity derivedReference = new(
            assembly,
            "Fixture.DerivedOwner",
            derivedDefinition);
        ApiTypeReferenceIdentity hiddenReference = new(
            assembly,
            "Fixture.BaseOwner.HiddenValue",
            hiddenDefinition);
        var declaringType = new ApiType
        {
            Namespace = "Fixture",
            Name = "BaseOwner",
            DefinitionName = baseDefinition,
            Kind = "class",
        };
        ApiMember member = new()
        {
            Name = "Hidden",
            Kind = "field",
            HasJsonInclude = true,
            SignatureModel = new ApiSignature
            {
                ReturnTypeReferences = [hiddenReference],
            },
        };
        var typesByScopedIdentity =
            new Dictionary<ApiTypeReferenceIdentity, ApiType>
            {
                [baseReference] = declaringType,
                [derivedReference] = new ApiType
                {
                    Namespace = "Fixture",
                    Name = "DerivedOwner",
                    DefinitionName = derivedDefinition,
                    Kind = "class",
                    BaseTypeReference = baseReference,
                },
                [hiddenReference] = new ApiType
                {
                    Namespace = "Fixture",
                    Name = "BaseOwner.HiddenValue",
                    DefinitionName = hiddenDefinition,
                    Accessibility = "protected",
                    Kind = "enum",
                },
            };

        Assert.True(
            JsonWireMemberRules
                .RequiresContextRelativeValueTypeAccessibilityEvidence(
                    declaringType,
                    member,
                    JsonWireDirection.Serialize,
                    assembly,
                    typesByScopedIdentity,
                    contextDefinition));
    }

    [Theory]
    [InlineData("protected")]
    [InlineData("private protected")]
    public void ContextRelativeAccessibilityTreatsCrossNamespaceDerivedContextAsProtectedAccess(
        string accessibility)
    {
        ApiAssemblyIdentity assembly = new(
            "Fixture",
            new Version(1, 0, 0, 0),
            culture: null,
            publicKeyToken: null);
        MetadataTypeDefinitionName baseDefinition =
            Assert.IsType<MetadataTypeDefinitionNameResult.Valid>(
                MetadataTypeDefinitionName.Create(
                    "Owners",
                    ["BaseOwner"]))
                .Name;
        MetadataTypeDefinitionName intermediateDefinition =
            Assert.IsType<MetadataTypeDefinitionNameResult.Valid>(
                MetadataTypeDefinitionName.Create(
                    "Contexts",
                    ["IntermediateOwner"]))
                .Name;
        MetadataTypeDefinitionName derivedDefinition =
            Assert.IsType<MetadataTypeDefinitionNameResult.Valid>(
                MetadataTypeDefinitionName.Create(
                    "Contexts",
                    ["DerivedOwner"]))
                .Name;
        MetadataTypeDefinitionName hiddenDefinition =
            Assert.IsType<MetadataTypeDefinitionNameResult.Valid>(
                MetadataTypeDefinitionName.Create(
                    "Owners",
                    ["BaseOwner", "HiddenValue"]))
                .Name;
        MetadataTypeDefinitionName contextDefinition =
            Assert.IsType<MetadataTypeDefinitionNameResult.Valid>(
                MetadataTypeDefinitionName.Create(
                    "Contexts",
                    ["DerivedOwner", "NestedContextJsonContext"]))
                .Name;
        ApiTypeReferenceIdentity baseReference = new(
            assembly,
            "Owners.BaseOwner",
            baseDefinition);
        ApiTypeReferenceIdentity intermediateReference = new(
            assembly,
            "Contexts.IntermediateOwner",
            intermediateDefinition);
        ApiTypeReferenceIdentity derivedReference = new(
            assembly,
            "Contexts.DerivedOwner",
            derivedDefinition);
        ApiTypeReferenceIdentity hiddenReference = new(
            assembly,
            "Owners.BaseOwner.HiddenValue",
            hiddenDefinition);
        var declaringType = new ApiType
        {
            Namespace = "Owners",
            Name = "BaseOwner",
            DefinitionName = baseDefinition,
            Kind = "class",
        };
        ApiMember member = new()
        {
            Name = "Hidden",
            Kind = "field",
            HasJsonInclude = true,
            SignatureModel = new ApiSignature
            {
                ReturnTypeReferences = [hiddenReference],
            },
        };
        var typesByScopedIdentity =
            new Dictionary<ApiTypeReferenceIdentity, ApiType>
            {
                [baseReference] = declaringType,
                [intermediateReference] = new ApiType
                {
                    Namespace = "Contexts",
                    Name = "IntermediateOwner",
                    DefinitionName = intermediateDefinition,
                    Kind = "class",
                    BaseTypeReference = baseReference,
                },
                [derivedReference] = new ApiType
                {
                    Namespace = "Contexts",
                    Name = "DerivedOwner",
                    DefinitionName = derivedDefinition,
                    Kind = "class",
                    BaseTypeReference = intermediateReference,
                },
                [hiddenReference] = new ApiType
                {
                    Namespace = "Owners",
                    Name = "BaseOwner.HiddenValue",
                    DefinitionName = hiddenDefinition,
                    Accessibility = accessibility,
                    Kind = "enum",
                },
            };

        Assert.True(
            JsonWireMemberRules
                .RequiresContextRelativeValueTypeAccessibilityEvidence(
                    declaringType,
                    member,
                    JsonWireDirection.Serialize,
                    assembly,
                    typesByScopedIdentity,
                    contextDefinition));
    }

    [Fact]
    public void ContextRelativeAccessibilityRequiresCompleteCompoundValueTypeAccess()
    {
        ApiAssemblyIdentity assembly = new(
            "Fixture",
            new Version(1, 0, 0, 0),
            culture: null,
            publicKeyToken: null);
        MetadataTypeDefinitionName baseDefinition =
            Assert.IsType<MetadataTypeDefinitionNameResult.Valid>(
                MetadataTypeDefinitionName.Create(
                    "Owners",
                    ["BaseOwner"]))
                .Name;
        MetadataTypeDefinitionName derivedDefinition =
            Assert.IsType<MetadataTypeDefinitionNameResult.Valid>(
                MetadataTypeDefinitionName.Create(
                    "Contexts",
                    ["DerivedOwner"]))
                .Name;
        MetadataTypeDefinitionName reachableDefinition =
            Assert.IsType<MetadataTypeDefinitionNameResult.Valid>(
                MetadataTypeDefinitionName.Create(
                    "Owners",
                    ["BaseOwner", "ReachableValue"]))
                .Name;
        MetadataTypeDefinitionName hiddenDefinition =
            Assert.IsType<MetadataTypeDefinitionNameResult.Valid>(
                MetadataTypeDefinitionName.Create(
                    "Owners",
                    ["BaseOwner", "HiddenValue"]))
                .Name;
        MetadataTypeDefinitionName contextDefinition =
            Assert.IsType<MetadataTypeDefinitionNameResult.Valid>(
                MetadataTypeDefinitionName.Create(
                    "Contexts",
                    ["DerivedOwner", "NestedContextJsonContext"]))
                .Name;
        ApiTypeReferenceIdentity baseReference = new(
            assembly,
            "Owners.BaseOwner",
            baseDefinition);
        ApiTypeReferenceIdentity derivedReference = new(
            assembly,
            "Contexts.DerivedOwner",
            derivedDefinition);
        ApiTypeReferenceIdentity reachableReference = new(
            assembly,
            "Owners.BaseOwner.ReachableValue",
            reachableDefinition);
        ApiTypeReferenceIdentity hiddenReference = new(
            assembly,
            "Owners.BaseOwner.HiddenValue",
            hiddenDefinition);
        var declaringType = new ApiType
        {
            Namespace = "Owners",
            Name = "BaseOwner",
            DefinitionName = baseDefinition,
            Kind = "class",
        };
        ApiMember member = new()
        {
            Name = "Mixed",
            Kind = "field",
            HasJsonInclude = true,
            SignatureModel = new ApiSignature
            {
                ReturnTypeReferences =
                [
                    reachableReference,
                    hiddenReference,
                ],
            },
        };
        var typesByScopedIdentity =
            new Dictionary<ApiTypeReferenceIdentity, ApiType>
            {
                [baseReference] = declaringType,
                [derivedReference] = new ApiType
                {
                    Namespace = "Contexts",
                    Name = "DerivedOwner",
                    DefinitionName = derivedDefinition,
                    Kind = "class",
                    BaseTypeReference = baseReference,
                },
                [reachableReference] = new ApiType
                {
                    Namespace = "Owners",
                    Name = "BaseOwner.ReachableValue",
                    DefinitionName = reachableDefinition,
                    Accessibility = "protected",
                    Kind = "enum",
                },
                [hiddenReference] = new ApiType
                {
                    Namespace = "Owners",
                    Name = "BaseOwner.HiddenValue",
                    DefinitionName = hiddenDefinition,
                    Accessibility = "private",
                    Kind = "enum",
                },
            };

        Assert.False(
            JsonWireMemberRules
                .RequiresContextRelativeValueTypeAccessibilityEvidence(
                    declaringType,
                    member,
                    JsonWireDirection.Serialize,
                    assembly,
                    typesByScopedIdentity,
                    contextDefinition));
    }

    [Fact]
    public void GetterOnlyDeserializePropertyRequiresConstructorEvidence()
    {
        ApiMember getterOnly = Property();
        getterOnly.HasGetter = true;
        getterOnly.HasSetter = false;
        var declaringType = new ApiType
        {
            Name = "Input",
            Members = [getterOnly],
        };

        Assert.True(
            JsonWireMemberRules
                .RequiresConstructorBindingEvidence(
                    declaringType,
                    getterOnly));

        ApiMember privateSetter = Property();
        privateSetter.HasGetter = true;
        privateSetter.HasSetter = true;
        privateSetter.SetterAccessibility = "private";
        declaringType.Members =
        [
            privateSetter,
            new ApiMember
            {
                Name = ".ctor",
                Kind = "constructor",
                SignatureModel = new ApiSignature
                {
                    Parameters =
                    [
                        new ApiParameter
                        {
                            Name = "value",
                            Type = "int",
                        },
                    ],
                },
            },
        ];
        Assert.True(
            JsonWireMemberRules
                .RequiresConstructorBindingEvidence(
                    declaringType,
                    privateSetter));

        getterOnly.JsonIgnoreConditions =
        [
            JsonWireIgnoreCondition.WhenReading,
        ];
        Assert.False(
            JsonWireMemberRules
                .RequiresConstructorBindingEvidence(
                    declaringType,
                    getterOnly));
    }

    [Fact]
    public void ExtractedCompilerIndexerIsExcludedFromJsonContract()
    {
        using FileStream stream = File.OpenRead(
            typeof(WidgetDto).Assembly.Location);
        using var peReader = new PEReader(stream);
        ApiSurface surface = ApiSurfaceExtractor.Extract(
            peReader,
            includeAll: true);
        ApiMember indexer = Assert.Single(
            Assert.Single(
                surface.Types,
                type => type.Name == nameof(WidgetDto))
                .Members,
            member => member.Kind == "property"
                && member.IndexParameterCount == 1);

        Assert.False(JsonWireMemberRules.IsSerialized(indexer));
    }
}
