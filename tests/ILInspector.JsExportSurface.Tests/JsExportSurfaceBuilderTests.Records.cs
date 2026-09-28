using System.Collections.Immutable;
using System.Globalization;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Text;
using System.Text.Json;
using ILInspector.Analysis;
using ILInspector.JsExportSurface.Fixtures;
using ILInspector.JsExportSurface.NamingFixtures;
using ILInspector.JsExportSurface.OperatorFixtures;
using ILInspector.JsExportSurface.PublishabilityFixtures;
using ILInspector.JsExportSurface.ScalarFixtures;
using ILInspector.Metadata;

namespace ILInspector.JsExportSurface.Tests;

public sealed partial class JsExportSurfaceBuilderTests
{
    [Fact]
    public void Build_RoutesEnumRootsToEnumsNotRecords()
    {
        ILInspector.JsExportSurface.JsExportSurface surface = BuildFixtureSurface();

        Assert.Contains(surface.Enums, e => e.Name == "WidgetStatus");
        Assert.DoesNotContain(surface.Records, r => r.Name == "WidgetStatus");
    }

    [Fact]
    public void Build_CapturesFlagsAndJsonStringEnumConverterFactsOnEnum()
    {
        ILInspector.JsExportSurface.JsExportSurface surface = BuildFixtureSurface();

        ApiType permission = Assert.Single(surface.Enums, e => e.Name == "WidgetPermission");
        Assert.True(permission.IsFlagsEnum);
        Assert.True(permission.HasJsonStringEnumConverter);
        Assert.Equal(1, permission.FlagsAttributeCount);
        Assert.False(permission.HasMalformedFlagsAttribute);
    }

    [Fact]
    public void Build_CapturesAbsenceOfJsonStringEnumConverterOnEnum()
    {
        ILInspector.JsExportSurface.JsExportSurface surface = BuildFixtureSurface();

        ApiType priority = Assert.Single(surface.Enums, e => e.Name == "WidgetPriority");
        Assert.False(priority.IsFlagsEnum);
        Assert.False(priority.HasJsonStringEnumConverter);
    }

    [Fact]
    public void Build_DiscoversRecordOnlyInNonFirstGenericRootArgumentAndPropagatesPolicy()
    {
        var apiSurface = new ApiSurface
        {
            Types =
            [
                new ApiType
                {
                    Name = "SurfaceJsonContext",
                    BaseType = "System.Text.Json.Serialization.JsonSerializerContext",
                    JsonPropertyNamingPolicy = JsonWireNamingPolicy.CamelCase,
                    Members =
                    [
                        new ApiMember
                        {
                            Name = "NestedDtosByKey",
                            Kind = "property",
                            ReturnType =
                                "System.Text.Json.Serialization.Metadata.JsonTypeInfo<"
                                + "System.Collections.Generic.Dictionary<string, NestedDto>>",
                        },
                    ],
                },
                new ApiType { Name = "NestedDto" },
            ],
        };

        ILInspector.JsExportSurface.JsExportSurface surface = JsExportSurfaceBuilder.Build(apiSurface);

        ApiType nested = Assert.Single(surface.Records);
        Assert.Equal("NestedDto", nested.Name);
        Assert.Equal(JsonWireNamingPolicy.CamelCase, nested.JsonPropertyNamingPolicy);
    }

    [Fact]
    public void Build_DoesNotDiscoverTheJsonSerializerContextTypeItselfAsARecord()
    {
        ILInspector.JsExportSurface.JsExportSurface surface = BuildFixtureSurface();

        Assert.DoesNotContain(surface.Records, r => r.Name == "FixtureJsonContext");
        Assert.DoesNotContain(surface.Records, r => r.Name == "InternalContextFixtureJsonContext");
        Assert.DoesNotContain(surface.Records, r => r.Name == "InternalContextCamelFixtureJsonContext");
        Assert.DoesNotContain(surface.Records, r => r.Name == "NeedsUnmappedTypeFixtureJsonContext");
    }

    [Fact]
    public void Build_WidgetDtoSerializesFourPropertiesAndExcludesIndexer()
    {
        ILInspector.JsExportSurface.JsExportSurface surface = BuildFixtureSurface();

        ApiType widgetDto = surface.Records.Single(r => r.Name == "WidgetDto");
        var propertyNames = widgetDto.Members
            .Where(m => m.Kind == "property"
                && JsonWireMemberRules.IsSerialized(m))
            .Select(m => m.Name)
            .ToHashSet(StringComparer.Ordinal);

        Assert.Equal(4, propertyNames.Count);
        Assert.Contains("Name", propertyNames);
        Assert.Contains("Count", propertyNames);
        Assert.Contains("Tags", propertyNames);
        Assert.Contains("Owner", propertyNames);
        ApiMember indexer = Assert.Single(
            widgetDto.Members,
            member => member.Kind == "property"
                && member.IndexParameterCount == 1);
        Assert.False(JsonWireMemberRules.IsSerialized(indexer));
    }

    [Fact]
    public void Build_DoesNotThrow_WhenTwoDistinctTypesShareASimpleName()
    {
        var apiSurface = new ApiSurface
        {
            Types =
            [
                new ApiType
                {
                    Name = "SurfaceJsonContext",
                    BaseType = "System.Text.Json.Serialization.JsonSerializerContext",
                    Members =
                    [
                        new ApiMember
                        {
                            Name = "Widget",
                            Kind = "property",
                            ReturnType = "System.Text.Json.Serialization.Metadata.JsonTypeInfo<Widget>",
                        },
                    ],
                },
                new ApiType
                {
                    Name = "Widget",
                    Members =
                    [
                        new ApiMember { Name = "Value", Kind = "property", ReturnType = "Result" },
                    ],
                },
                new ApiType { Namespace = "A", Name = "Result" },
                new ApiType { Namespace = "B", Name = "Result" },
            ],
        };

        ILInspector.JsExportSurface.JsExportSurface surface = JsExportSurfaceBuilder.Build(apiSurface);
        Assert.Contains(surface.Records, r => r.Name == "Widget");
        Assert.DoesNotContain(surface.Records, r => r.Name == "Result");
    }

    [Fact]
    public void Build_DoesNotBindQualifiedContainerToUnrelatedLocalSimpleName()
    {
        var apiSurface = new ApiSurface
        {
            Types =
            [
                new ApiType
                {
                    Name = "SurfaceJsonContext",
                    BaseType =
                        "System.Text.Json.Serialization.JsonSerializerContext",
                    Members =
                    [
                        new ApiMember
                        {
                            Name = "Dtos",
                            Kind = "property",
                            ReturnType =
                                "System.Text.Json.Serialization.Metadata."
                                + "JsonTypeInfo<System.Collections.Generic."
                                + "Dictionary<string, Mine.ActualDto>>",
                        },
                    ],
                },
                new ApiType { Namespace = "Mine", Name = "Dictionary" },
                new ApiType { Namespace = "Mine", Name = "ActualDto" },
            ],
        };

        ILInspector.JsExportSurface.JsExportSurface surface =
            JsExportSurfaceBuilder.Build(apiSurface);

        ApiType record = Assert.Single(surface.Records);
        Assert.Equal("ActualDto", record.Name);
    }

    [Fact]
    public void Build_IncludeAllKeepsJsonIncludeNonPublicPropertyOnRecord()
    {
        ILInspector.JsExportSurface.JsExportSurface surface = BuildFixtureSurface(includeAll: true);

        ApiType audit = Assert.Single(surface.Records, r => r.Name == "WidgetAudit");
        Assert.True(Assert.Single(audit.Members, m => m.Name == "LastEditedBy").HasJsonInclude);
    }

    [Fact]
    public void Extract_CapturesJsonIncludeOnFields()
    {
        using FileStream stream = File.OpenRead(
            typeof(ControlFieldPropertyNameFixture).Assembly.Location);
        using var peReader = new PEReader(stream);
        ApiSurface apiSurface = ApiSurfaceExtractor.Extract(
            peReader,
            includeAll: true);

        ApiType record = Assert.Single(
            apiSurface.Types,
            type => type.Name == nameof(ControlFieldPropertyNameFixture));
        ApiMember field = Assert.Single(
            record.Members,
            member => member.Name == "Value");
        Assert.Equal("field\nbreak\r\t\u0001", field.JsonPropertyName);
        Assert.True(field.HasJsonInclude);
    }

    [Fact]
    public void Extract_CapturesPropertyGetterAccessibility()
    {
        using FileStream stream = File.OpenRead(
            typeof(GetterAccessibilityFixture).Assembly.Location);
        using var peReader = new PEReader(stream);
        ApiSurface apiSurface = ApiSurfaceExtractor.Extract(
            peReader,
            includeAll: true);

        ApiType record = Assert.Single(
            apiSurface.Types,
            type => type.Name == nameof(GetterAccessibilityFixture));
        ApiMember setterOnly = Assert.Single(
            record.Members,
            member => member.Name == "SetterOnlyAtWire");
        ApiMember included = Assert.Single(
            record.Members,
            member => member.Name == "IncludedPrivateGetter");
        ApiMember publicGetter = Assert.Single(
            record.Members,
            member => member.Name == "PublicGetter");
        ApiMember noGetter = Assert.Single(
            record.Members,
            member => member.Name == "NoGetter");

        Assert.True(setterOnly.HasGetter);
        Assert.Equal("private", setterOnly.GetterAccessibility);
        Assert.False(setterOnly.HasJsonInclude);
        Assert.True(included.HasGetter);
        Assert.Equal("private", included.GetterAccessibility);
        Assert.True(included.HasJsonInclude);
        Assert.True(publicGetter.HasGetter);
        Assert.Null(publicGetter.GetterAccessibility);
        Assert.False(noGetter.HasGetter);
        Assert.True(noGetter.HasJsonInclude);
    }

    [Fact]
    public void Extract_DistinguishesJsonIgnoreNever()
    {
        using FileStream stream = File.OpenRead(
            typeof(JsonIgnoreNeverFixture).Assembly.Location);
        using var peReader = new PEReader(stream);
        ApiSurface apiSurface = ApiSurfaceExtractor.Extract(
            peReader,
            includeAll: true);

        ApiType record = Assert.Single(
            apiSurface.Types,
            type => type.Name == nameof(JsonIgnoreNeverFixture));
        ApiMember included = Assert.Single(
            record.Members,
            member => member.Name == "Included");
        ApiMember excluded = Assert.Single(
            record.Members,
            member => member.Name == "Excluded");

        Assert.True(included.HasJsonIgnore);
        Assert.True(included.HasJsonIgnoreNever);
        Assert.True(excluded.HasJsonIgnore);
        Assert.False(excluded.HasJsonIgnoreNever);
    }

    [Fact]
    public void Extract_DecodesByteBackedReadCommentHandlingOption()
    {
        using FileStream stream = File.OpenRead(
            typeof(AdditionalOptionsJsonContext).Assembly.Location);
        using var peReader = new PEReader(stream);
        ApiSurface apiSurface = ApiSurfaceExtractor.Extract(
            peReader,
            includeAll: true);

        ApiType context = Assert.Single(
            apiSurface.Types,
            type => type.Name == nameof(AdditionalOptionsJsonContext));

        Assert.Equal(
            JsonWireNamingPolicy.CamelCase,
            context.JsonPropertyNamingPolicy);
    }

    [Fact]
    public void Extract_CapturesJsonConverterAndEnumWireNameFacts()
    {
        using FileStream stream = File.OpenRead(
            typeof(MemberJsonConverterFixture).Assembly.Location);
        using var peReader = new PEReader(stream);
        ApiSurface apiSurface = ApiSurfaceExtractor.Extract(
            peReader,
            includeAll: true);

        ApiType record = Assert.Single(
            apiSurface.Types,
            type => type.Name == nameof(MemberJsonConverterFixture));
        Assert.Equal(
            1,
            Assert.Single(
                record.Members,
                member => member.Name == "Value")
                .JsonConverterAttributeCount);

        ApiType enumType = Assert.Single(
            apiSurface.Types,
            type => type.Name == nameof(NamedEnumFixture));
        Assert.Equal(1, enumType.JsonConverterAttributeCount);
        Assert.True(enumType.HasJsonStringEnumConverter);
        ApiMember value = Assert.Single(
            enumType.Members,
            member => member.Name == "Value");
        Assert.Equal(
            "wire \"value\"\n\u2028",
            value.JsonStringEnumMemberName);
        Assert.Equal(
            ["wire \"value\"\n\u2028"],
            value.JsonStringEnumMemberNameAttributeValues);
    }

    [Fact]
    public void Extract_ChargesSerializedConverterTypeNameBeforeDecode()
    {
        using FileStream stream = File.OpenRead(
            typeof(NamedEnumFixture).Assembly.Location);
        using var peReader = new PEReader(stream);
        ApiSurface apiSurface = ApiSurfaceExtractor.Extract(
            peReader,
            includeAll: true);
        ApiAssemblyIdentity assemblyIdentity =
            apiSurface.AssemblyIdentity!;
        MetadataTypeDefinitionName enumDefinitionName =
            Assert.Single(
                apiSurface.Types,
                type => type.Name == nameof(NamedEnumFixture))
                .DefinitionName!;
        MetadataReader reader = peReader.GetMetadataReader();
        TypeDefinition enumType = reader.GetTypeDefinition(
            Assert.Single(
                reader.TypeDefinitions,
                handle => reader.StringComparer.Equals(
                    reader.GetTypeDefinition(handle).Name,
                    nameof(NamedEnumFixture))));
        CustomAttribute converter = Assert.Single(
            enumType.GetCustomAttributes()
                .Select(reader.GetCustomAttribute),
            attribute =>
                AttributeDecoder.GetAttributeTypeName(
                    reader,
                    attribute.Constructor)
                == "System.Text.Json.Serialization.JsonConverterAttribute");
        string serializedName = Assert.IsType<string>(
            Assert.Single(
                AttributeDecoder
                    .TryDecodePreservingSerializedTypeNames(
                        reader,
                        converter)!
                    .Value
                    .FixedArguments)
                .Value);
        int charged = 0;

        bool supported =
            AttributeReader.HasJsonStringEnumConverterAttribute(
                reader,
                enumType.GetCustomAttributes(),
                enumDefinitionName,
                assemblyIdentity,
                amount => charged = checked(charged + amount));

        Assert.True(supported);
        Assert.True(
            charged >= serializedName.Length,
            $"Expected at least {serializedName.Length} charged characters, "
                + $"but observed {charged}.");
    }

    [Fact]
    public void Extract_RejectsStringEnumConverterForAnotherEnum()
    {
        using FileStream stream = File.OpenRead(
            typeof(MismatchedStringEnumConverterFixture).Assembly.Location);
        using var peReader = new PEReader(stream);
        ApiSurface apiSurface = ApiSurfaceExtractor.Extract(
            peReader,
            includeAll: true);
        ApiType enumType = Assert.Single(
            apiSurface.Types,
            type => type.Name
                == nameof(MismatchedStringEnumConverterFixture));

        Assert.Equal(1, enumType.JsonConverterAttributeCount);
        Assert.False(enumType.HasJsonStringEnumConverter);
        Assert.Throws<InvalidOperationException>(
            () => JsonSerializer.Serialize(
                MismatchedStringEnumConverterFixture.Value));
    }

    [Fact]
    public void Build_CapturesJsonPropertyNameAndJsonIgnoreFacts()
    {
        ILInspector.JsExportSurface.JsExportSurface surface = BuildFixtureSurface(includeAll: true);

        ApiType audit = Assert.Single(surface.Records, r => r.Name == "WidgetAudit");
        Assert.Equal("wire_name", Assert.Single(audit.Members, m => m.Name == "DisplayName").JsonPropertyName);
        Assert.Equal(string.Empty, Assert.Single(audit.Members, m => m.Name == "EmptyWireName").JsonPropertyName);
        Assert.True(Assert.Single(audit.Members, m => m.Name == "IgnoredAtWire").HasJsonIgnore);
    }

    [Fact]
    public void Build_AssignsNamingPolicyPerContextWithoutBleed()
    {
        ILInspector.JsExportSurface.JsExportSurface surface = BuildFixtureSurface(includeAll: true);

        ApiType pascalRecord = Assert.Single(surface.Records, r => r.Name == "InternalContextPascalWidget");
        Assert.Equal(JsonWireNamingPolicy.None, pascalRecord.JsonPropertyNamingPolicy);

        ApiType camelRecord = Assert.Single(surface.Records, r => r.Name == "WidgetDto");
        Assert.Equal(JsonWireNamingPolicy.CamelCase, camelRecord.JsonPropertyNamingPolicy);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Build_MarksConflictingContextPoliciesUnsupportedRegardlessOfMetadataOrder(
        bool reverseContexts)
    {
        ApiType camelContext = CreateSerializerContext(
            "CamelContext",
            "SharedDto",
            JsonWireNamingPolicy.CamelCase);
        ApiType snakeContext = CreateSerializerContext(
            "SnakeContext",
            "SharedDto",
            JsonWireNamingPolicy.SnakeCaseLower);
        var sharedDto = new ApiType { Name = "SharedDto" };
        var apiSurface = new ApiSurface
        {
            Types = reverseContexts
                ? [snakeContext, sharedDto, camelContext]
                : [camelContext, sharedDto, snakeContext],
        };

        ILInspector.JsExportSurface.JsExportSurface surface = JsExportSurfaceBuilder.Build(apiSurface);

        ApiType record = Assert.Single(surface.Records);
        Assert.Equal(JsonWireNamingPolicy.Unsupported, record.JsonPropertyNamingPolicy);
    }

    [Fact]
    public void Build_KeepsPolicyWhenMultipleContextsAgree()
    {
        var apiSurface = new ApiSurface
        {
            Types =
            [
                CreateSerializerContext("FirstContext", "SharedDto", JsonWireNamingPolicy.CamelCase),
                new ApiType { Name = "SharedDto" },
                CreateSerializerContext("SecondContext", "SharedDto", JsonWireNamingPolicy.CamelCase),
            ],
        };

        ILInspector.JsExportSurface.JsExportSurface surface = JsExportSurfaceBuilder.Build(apiSurface);

        ApiType record = Assert.Single(surface.Records);
        Assert.Equal(JsonWireNamingPolicy.CamelCase, record.JsonPropertyNamingPolicy);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Build_TreatsAbsentAndExplicitNeverContextDefaultsAsEquivalent(
        bool reverseContexts)
    {
        ApiType absentContext = CreateSerializerContext(
            "AbsentContext",
            "SharedDto",
            JsonWireNamingPolicy.None);
        ApiType explicitNeverContext = CreateSerializerContext(
            "ExplicitNeverContext",
            "SharedDto",
            JsonWireNamingPolicy.None);
        explicitNeverContext.JsonDefaultIgnoreConditionEvidence =
            new JsonSourceGenerationDefaultIgnoreConditionEvidence(
                1,
                JsonWireIgnoreCondition.Never,
                HasUnsupportedRow: false);
        var sharedDto = new ApiType { Name = "SharedDto" };
        var apiSurface = new ApiSurface
        {
            Types = reverseContexts
                ? [explicitNeverContext, sharedDto, absentContext]
                : [absentContext, sharedDto, explicitNeverContext],
        };

        ILInspector.JsExportSurface.JsExportSurface surface =
            JsExportSurfaceBuilder.Build(apiSurface);

        ApiType record = Assert.Single(surface.Records);
        Assert.Equal(
            JsonWireContextDefaultIgnoreCondition.Never,
            surface.ContextDefaultIgnoreConditions[record]);
        Assert.Equal(
            JsonWireNamingPolicy.None,
            record.JsonPropertyNamingPolicy);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Build_MarksConflictingContextDefaultsUnsupportedRegardlessOfOrder(
        bool reverseContexts)
    {
        ApiType neverContext = CreateSerializerContext(
            "NeverContext",
            "SharedDto",
            JsonWireNamingPolicy.None);
        ApiType whenWritingNullContext = CreateSerializerContext(
            "WhenWritingNullContext",
            "SharedDto",
            JsonWireNamingPolicy.None);
        whenWritingNullContext.JsonDefaultIgnoreConditionEvidence =
            new JsonSourceGenerationDefaultIgnoreConditionEvidence(
                1,
                JsonWireIgnoreCondition.WhenWritingNull,
                HasUnsupportedRow: false);
        var sharedDto = new ApiType { Name = "SharedDto" };
        var apiSurface = new ApiSurface
        {
            Types = reverseContexts
                ? [whenWritingNullContext, sharedDto, neverContext]
                : [neverContext, sharedDto, whenWritingNullContext],
        };

        ILInspector.JsExportSurface.JsExportSurface surface =
            JsExportSurfaceBuilder.Build(apiSurface);

        ApiType record = Assert.Single(surface.Records);
        Assert.Equal(
            JsonWireContextDefaultIgnoreCondition.Unsupported,
            surface.ContextDefaultIgnoreConditions[record]);
        Assert.Equal(
            JsonWireNamingPolicy.Unsupported,
            record.JsonPropertyNamingPolicy);
    }

    [Fact]
    public void Build_RetainsUnsupportedContextDefaultDespiteMemberOverride()
    {
        ApiType context = CreateSerializerContext(
            "Context",
            "Root",
            JsonWireNamingPolicy.None);
        context.JsonDefaultIgnoreConditionEvidence =
            new JsonSourceGenerationDefaultIgnoreConditionEvidence(
                2,
                Value: null,
                HasUnsupportedRow: false);
        var root = new ApiType
        {
            Name = "Root",
            Members =
            [
                new ApiMember
                {
                    Name = "Value",
                    Kind = "property",
                    HasGetter = true,
                    ReturnType = "string",
                    IndexParameterCount = 0,
                    JsonIgnoreConditions =
                        [JsonWireIgnoreCondition.Never],
                },
            ],
        };

        ILInspector.JsExportSurface.JsExportSurface surface =
            JsExportSurfaceBuilder.Build(
                new ApiSurface { Types = [context, root] });

        ApiType record = Assert.Single(surface.Records);
        Assert.Equal(
            JsonWireContextDefaultIgnoreCondition.Unsupported,
            surface.ContextDefaultIgnoreConditions[record]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Build_DoesNotTraverseConverterControlledShapes(
        bool converterIsOnType)
    {
        ApiType root = new()
        {
            Name = "Root",
            JsonConverterAttributeCount = converterIsOnType ? 1 : 0,
            Members =
            [
                new ApiMember
                {
                    Name = "Child",
                    Kind = "property",
                    HasGetter = true,
                    ReturnType = "Child",
                    JsonConverterAttributeCount =
                        converterIsOnType ? 0 : 1,
                },
            ],
        };
        var apiSurface = new ApiSurface
        {
            Types =
            [
                CreateSerializerContext(
                    "Context",
                    "Root",
                    JsonWireNamingPolicy.None),
                root,
                new ApiType { Name = "Child" },
            ],
        };

        ILInspector.JsExportSurface.JsExportSurface surface =
            JsExportSurfaceBuilder.Build(apiSurface);

        Assert.Equal(["Root"], surface.Records.Select(type => type.Name));
    }

    [Fact]
    public void Build_DoesNotAliasExternalContextRootToLocalType()
    {
        ApiType context = CreateSerializerContext(
            "Context",
            "Mine.Result",
            JsonWireNamingPolicy.None);
        context.Members[0].SignatureModel = new ApiSignature
        {
            ReturnType = context.Members[0].ReturnType,
            ReturnTypeDefinitionReference = new(
                new ApiAssemblyIdentity(
                    "System.Text.Json",
                    new Version(11, 0, 0, 0),
                    culture: null,
                    publicKeyToken:
                        "cc7b13ffcd2ddd51"),
                "System.Text.Json.Serialization.Metadata.JsonTypeInfo`1",
                TopLevelDefinitionName(
                    "System.Text.Json.Serialization.Metadata",
                    "JsonTypeInfo`1")),
            ReturnTypeReferences =
            [
                new(
                    new ApiAssemblyIdentity(
                        "System.Text.Json",
                        new Version(11, 0, 0, 0),
                        culture: null,
                        publicKeyToken:
                            "cc7b13ffcd2ddd51"),
                    "System.Text.Json.Serialization.Metadata.JsonTypeInfo`1",
                    TopLevelDefinitionName(
                        "System.Text.Json.Serialization.Metadata",
                        "JsonTypeInfo`1")),
                new(
                    new ApiAssemblyIdentity(
                        "Local",
                        new Version(1, 0, 0, 0),
                        culture: null,
                        publicKeyToken:
                            "8899aabbccddeeff"),
                    "Mine.Result"),
            ],
        };
        context.BaseTypeReference = new(
            new ApiAssemblyIdentity(
                "System.Text.Json",
                new Version(11, 0, 0, 0),
                culture: null,
                publicKeyToken: "cc7b13ffcd2ddd51"),
            "System.Text.Json.Serialization.JsonSerializerContext",
            TopLevelDefinitionName(
                "System.Text.Json.Serialization",
                "JsonSerializerContext"));
        var apiSurface = new ApiSurface
        {
            AssemblyIdentity = new ApiAssemblyIdentity(
                "Local",
                new Version(1, 0, 0, 0),
                culture: null,
                publicKeyToken:
                    "0011223344556677"),
            Types =
            [
                context,
                new ApiType
                {
                    Namespace = "Mine",
                    Name = "Result",
                },
            ],
        };

        ILInspector.JsExportSurface.JsExportSurface surface =
            JsExportSurfaceBuilder.Build(apiSurface);

        Assert.Empty(surface.Records);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Build_DoesNotTrustLookalikeSerializerContextTypes(
        bool spoofBaseType)
    {
        var localAssembly = new ApiAssemblyIdentity(
            "Local",
            new Version(1, 0, 0, 0),
            culture: null,
            publicKeyToken: "0011223344556677");
        var authenticSystemTextJson = new ApiAssemblyIdentity(
            "System.Text.Json",
            new Version(11, 0, 0, 0),
            culture: null,
            publicKeyToken: "cc7b13ffcd2ddd51");
        var lookalikeSystemTextJson = new ApiAssemblyIdentity(
            "System.Text.Json",
            new Version(11, 0, 0, 0),
            culture: null,
            publicKeyToken: "8899aabbccddeeff");
        ApiType context = CreateSerializerContext(
            "Context",
            "Mine.Result",
            JsonWireNamingPolicy.None);
        context.BaseTypeReference = new(
            spoofBaseType
                ? lookalikeSystemTextJson
                : authenticSystemTextJson,
            "System.Text.Json.Serialization.JsonSerializerContext",
            TopLevelDefinitionName(
                "System.Text.Json.Serialization",
                "JsonSerializerContext"));
        context.Members[0].SignatureModel = new ApiSignature
        {
            ReturnType = context.Members[0].ReturnType,
            ReturnTypeDefinitionReference = new(
                spoofBaseType
                    ? authenticSystemTextJson
                    : lookalikeSystemTextJson,
                "System.Text.Json.Serialization.Metadata.JsonTypeInfo`1",
                TopLevelDefinitionName(
                    "System.Text.Json.Serialization.Metadata",
                    "JsonTypeInfo`1")),
            ReturnTypeReferences =
            [
                new(
                    spoofBaseType
                        ? authenticSystemTextJson
                        : lookalikeSystemTextJson,
                    "System.Text.Json.Serialization.Metadata.JsonTypeInfo`1",
                    TopLevelDefinitionName(
                        "System.Text.Json.Serialization.Metadata",
                        "JsonTypeInfo`1")),
                new(
                    authenticSystemTextJson,
                    "System.Text.Json.Serialization.Metadata.JsonTypeInfo`1",
                    TopLevelDefinitionName(
                        "System.Text.Json.Serialization.Metadata",
                        "JsonTypeInfo`1")),
                new(localAssembly, "Mine.Result"),
            ],
        };
        var apiSurface = new ApiSurface
        {
            AssemblyIdentity = localAssembly,
            Types =
            [
                context,
                new ApiType
                {
                    Namespace = "Mine",
                    Name = "Result",
                },
            ],
        };

        ILInspector.JsExportSurface.JsExportSurface surface =
            JsExportSurfaceBuilder.Build(apiSurface);

        Assert.Empty(surface.Records);
    }

    [Fact]
    public void Build_DoesNotTrustNestedSerializerContextIdentity()
    {
        var localAssembly = new ApiAssemblyIdentity(
            "Local",
            new Version(1, 0, 0, 0),
            culture: null,
            publicKeyToken: "0011223344556677");
        var systemTextJson = new ApiAssemblyIdentity(
            "System.Text.Json",
            new Version(11, 0, 0, 0),
            culture: null,
            publicKeyToken: "cc7b13ffcd2ddd51");
        MetadataTypeDefinitionName resultName =
            TopLevelDefinitionName("Mine", "Result");
        var resultIdentity = new ApiTypeReferenceIdentity(
            localAssembly,
            "Mine.Result",
            resultName);
        ApiType context = CreateSerializerContext(
            "Context",
            "Mine.Result",
            JsonWireNamingPolicy.None);
        context.BaseTypeReference = new(
            systemTextJson,
            "System.Text.Json.Serialization.JsonSerializerContext",
            Assert.IsType<MetadataTypeDefinitionNameResult.Valid>(
                MetadataTypeDefinitionName.Create(
                    "System.Text.Json",
                    ["Serialization", "JsonSerializerContext"]))
                .Name);
        context.JsonSerializableAttributeCount = 1;
        context.JsonSerializableRoots = [new(resultIdentity, IsArray: false)];
        context.Members[0].SignatureModel = new ApiSignature
        {
            ReturnType = context.Members[0].ReturnType,
            ReturnTypeDefinitionReference = new(
                systemTextJson,
                "System.Text.Json.Serialization.Metadata.JsonTypeInfo`1",
                TopLevelDefinitionName(
                    "System.Text.Json.Serialization.Metadata",
                    "JsonTypeInfo`1")),
            ReturnTypeReferences =
            [
                new(
                    systemTextJson,
                    "System.Text.Json.Serialization.Metadata.JsonTypeInfo`1",
                    TopLevelDefinitionName(
                        "System.Text.Json.Serialization.Metadata",
                        "JsonTypeInfo`1")),
                resultIdentity,
            ],
        };
        var apiSurface = new ApiSurface
        {
            AssemblyIdentity = localAssembly,
            Types =
            [
                context,
                new ApiType
                {
                    Namespace = "Mine",
                    Name = "Result",
                    DefinitionName = resultName,
                },
            ],
        };

        ILInspector.JsExportSurface.JsExportSurface surface =
            JsExportSurfaceBuilder.Build(apiSurface);

        Assert.Empty(surface.Records);
    }

    [Fact]
    public void Build_MalformedContextUsesContainedTokenLocation()
    {
        var systemTextJson = new ApiAssemblyIdentity(
            "System.Text.Json",
            new Version(11, 0, 0, 0),
            culture: null,
            publicKeyToken: "cc7b13ffcd2ddd51");
        ApiType context = CreateSerializerContext(
            "Context\u000BInjected",
            "Mine.Result",
            JsonWireNamingPolicy.None);
        context.MetadataToken = 0x02000002;
        context.BaseTypeReference = new(
            systemTextJson,
            "System.Text.Json.Serialization.JsonSerializerContext",
            TopLevelDefinitionName(
                "System.Text.Json.Serialization",
                "JsonSerializerContext"));
        context.JsonSerializableAttributeCount = 1;
        var apiSurface = new ApiSurface
        {
            AssemblyIdentity = new(
                "Local",
                new Version(1, 0, 0, 0),
                culture: null,
                publicKeyToken: "0011223344556677"),
            Types = [context],
        };

        UnsupportedJsExportSurfaceException exception =
            Assert.Throws<UnsupportedJsExportSurfaceException>(
                () => JsExportSurfaceBuilder.Build(apiSurface));

        Assert.StartsWith(
            "type 0x02000002:",
            exception.Message,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "\u000B",
            exception.Message,
            StringComparison.Ordinal);
    }
}
