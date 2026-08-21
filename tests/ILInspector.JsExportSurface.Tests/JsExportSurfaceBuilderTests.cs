using System.Reflection.PortableExecutable;
using ILInspector.JsExportSurface.Fixtures;
using ILInspector.Metadata;

namespace ILInspector.JsExportSurface.Tests;

public sealed class JsExportSurfaceBuilderTests
{
    private static ILInspector.JsExportSurface.JsExportSurface BuildFixtureSurface(bool includeAll = false)
    {
        using FileStream stream = File.OpenRead(typeof(FixtureExports).Assembly.Location);
        using var peReader = new PEReader(stream);
        ApiSurface apiSurface = ApiSurfaceExtractor.Extract(peReader, includeAll: includeAll);
        return JsExportSurfaceBuilder.Build(apiSurface);
    }

    [Fact]
    public void Build_DiscoversAllJsExportFunctions()
    {
        ILInspector.JsExportSurface.JsExportSurface surface = BuildFixtureSurface();

        var names = surface.Functions.Select(f => f.Name).ToHashSet(StringComparer.Ordinal);
        Assert.Equal(14, surface.Functions.Count);
        Assert.Contains("GetWidget", names);
        Assert.Contains("GetWidgetAsync", names);
        Assert.Contains("Ping", names);
        Assert.Contains("RenameWidget", names);
        Assert.Contains("GetWidgetOrOwner", names);
        Assert.Contains("GetWidgetArray", names);
        Assert.Contains("GetWidgetSummary", names);
        Assert.Contains("GetWidgetPermissionSummary", names);
        Assert.Contains("GetWidgetPrioritySummary", names);
        Assert.Contains("GetWidgetAudit", names);
        Assert.Contains("QueryPackage", names);
        Assert.Contains("GetInternalContextWidget", names);
        Assert.Contains("GetInternalContextCamelWidget", names);
        Assert.Contains("GetNeedsUnmappedType", names);
    }

    [Fact]
    public void Build_ReportsFunctionSignaturesUnmodified()
    {
        ILInspector.JsExportSurface.JsExportSurface surface = BuildFixtureSurface();

        JsExportFunction getWidget = surface.Functions.Single(f => f.Name == "GetWidget");
        Assert.Equal("string", getWidget.ReturnType);
        Assert.Equal(2, getWidget.Parameters.Count);

        JsExportFunction getWidgetAsync = surface.Functions.Single(f => f.Name == "GetWidgetAsync");
        Assert.Contains("Task", getWidgetAsync.ReturnType, StringComparison.Ordinal);
        Assert.Contains("string", getWidgetAsync.ReturnType, StringComparison.Ordinal);

        JsExportFunction ping = surface.Functions.Single(f => f.Name == "Ping");
        Assert.Contains("Task", ping.ReturnType, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_DiscoversJsonSerializerContextRootsAndNestedRecords()
    {
        ILInspector.JsExportSurface.JsExportSurface surface = BuildFixtureSurface();

        var recordNames = surface.Records.Select(r => r.Name).ToHashSet(StringComparer.Ordinal);
        Assert.Equal(9, surface.Records.Count);
        Assert.Contains("WidgetDto", recordNames);
        Assert.Contains("WidgetOwner", recordNames);
        Assert.Contains("WidgetCatalog", recordNames);
        Assert.Contains("WidgetSummary", recordNames);
        Assert.Contains("WidgetPermissionSummary", recordNames);
        Assert.Contains("WidgetPrioritySummary", recordNames);
        Assert.Contains("WidgetAudit", recordNames);
        Assert.Contains("ConflictingPolicyWidget", recordNames);
        Assert.Contains("NeedsUnmappedTypeFixture", recordNames);
    }

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
    public void Build_WidgetDtoExposesAllFourDeclaredProperties()
    {
        ILInspector.JsExportSurface.JsExportSurface surface = BuildFixtureSurface();

        ApiType widgetDto = surface.Records.Single(r => r.Name == "WidgetDto");
        var propertyNames = widgetDto.Members
            .Where(m => m.Kind == "property")
            .Select(m => m.Name)
            .ToHashSet(StringComparer.Ordinal);

        Assert.Equal(4, propertyNames.Count);
        Assert.Contains("Name", propertyNames);
        Assert.Contains("Count", propertyNames);
        Assert.Contains("Tags", propertyNames);
        Assert.Contains("Owner", propertyNames);
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

    private static ApiType CreateSerializerContext(
        string name,
        string recordName,
        JsonWireNamingPolicy namingPolicy) =>
        new()
        {
            Name = name,
            BaseType = "System.Text.Json.Serialization.JsonSerializerContext",
            JsonPropertyNamingPolicy = namingPolicy,
            Members =
            [
                new ApiMember
                {
                    Name = recordName,
                    Kind = "property",
                    ReturnType =
                        $"System.Text.Json.Serialization.Metadata.JsonTypeInfo<{recordName}>",
                },
            ],
        };
}
