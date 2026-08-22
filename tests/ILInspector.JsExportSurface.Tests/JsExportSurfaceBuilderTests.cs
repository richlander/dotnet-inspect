using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Text.Json;
using ILInspector.Analysis;
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

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Build_InvalidExportUsesContainedFailure(bool isUnsafe)
    {
        const string hostileTypeName = "Bad\u001b[31mType";
        const string hostileMemberName = "Bad\u202eMember";
        var apiSurface = new ApiSurface
        {
            Types =
            [
                new ApiType
                {
                    Name = hostileTypeName,
                    MetadataToken = 0x02000002,
                    Members =
                    [
                        new ApiMember
                        {
                            Name = hostileMemberName,
                            Kind = "method",
                            MetadataToken = 0x06000001,
                            IsStatic = true,
                            IsUnsafe = isUnsafe,
                            SignatureModel = isUnsafe
                                ? new ApiSignature()
                                : null,
                            HasRuntimeJsExport = true,
                        },
                    ],
                },
            ],
        };

        UnsupportedJsExportSurfaceException exception =
            Assert.Throws<UnsupportedJsExportSurfaceException>(
                () => JsExportSurfaceBuilder.Build(apiSurface));

        Assert.Contains("member 0x06000001", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(hostileTypeName, exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(hostileMemberName, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_RejectsDegradedJsExportSignature()
    {
        var apiSurface = new ApiSurface
        {
            Types =
            [
                new ApiType
                {
                    Name = "Exports",
                    Members =
                    [
                        new ApiMember
                        {
                            Name = "Broken",
                            Kind = "method",
                            IsStatic = true,
                            SignatureDecodeStatus =
                                SignatureDecodeStatus.Degraded,
                            SignatureModel = new ApiSignature
                            {
                                ReturnType = "object",
                            },
                            HasRuntimeJsExport = true,
                        },
                    ],
                },
            ],
        };

        Assert.Throws<UnsupportedJsExportSurfaceException>(
            () => JsExportSurfaceBuilder.Build(apiSurface));
    }

    [Fact]
    public void Build_IgnoresLookalikeJsExportAttribute()
    {
        var apiSurface = new ApiSurface
        {
            Types =
            [
                new ApiType
                {
                    Name = "Exports",
                    Members =
                    [
                        new ApiMember
                        {
                            Name = "NotAnExport",
                            Kind = "method",
                            IsStatic = true,
                            Attributes = ["Other.JSExport"],
                        },
                    ],
                },
            ],
        };

        ILInspector.JsExportSurface.JsExportSurface surface =
            JsExportSurfaceBuilder.Build(apiSurface);

        Assert.Empty(surface.Functions);
    }

    [Fact]
    public void Extract_DoesNotTrustSameNameJsExportFromAnotherAssembly()
    {
        using var stream = new MemoryStream(
            BuildFakeJsExportImage(),
            writable: false);
        using var peReader = new PEReader(stream);
        ApiSurface apiSurface = ApiSurfaceExtractor.Extract(
            peReader,
            includeAll: true);
        ApiType fixture = Assert.Single(
            apiSurface.Types,
            type => type.Name == "FakeJsExportFixture");
        ApiMember method = Assert.Single(
            fixture.Members,
            member => member.Name == "NotAnExport");

        Assert.Contains(
            "System.Runtime.InteropServices.JavaScript.JSExport",
            method.Attributes);
        Assert.False(method.HasRuntimeJsExport);
        Assert.DoesNotContain(
            JsExportSurfaceBuilder.Build(apiSurface).Functions,
            function => function.Name == "NotAnExport");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Build_RejectsOnlyExportScopedBodyDiagnostics(
        bool sourceAttributed)
    {
        const int exportToken = 0x06000001;
        const int diagnosticToken = 0x06000002;
        ApiSurface apiSurface = ExportSurface(exportToken);
        var diagnostic = new AnalysisDiagnostic(
            diagnosticToken,
            "Exports.Failed",
            "BadImageFormatException: invalid body",
            SourceMethodToken:
                sourceAttributed ? exportToken : null);
        LibraryBodyIndex bodyIndex = LibraryBodyIndex.FromEvidence(
            [],
            [],
            diagnostics: [diagnostic]);

        if (sourceAttributed)
        {
            Assert.Throws<UnsupportedJsExportSurfaceException>(
                () => JsExportSurfaceBuilder.Build(
                    apiSurface,
                    bodyIndex));
        }
        else
        {
            Assert.Single(
                JsExportSurfaceBuilder.Build(apiSurface, bodyIndex)
                    .Functions);
        }
    }

    [Fact]
    public void Build_RejectsDegradedSerializerContextProperty()
    {
        ApiType context = CreateSerializerContext(
            "Context",
            "Root",
            JsonWireNamingPolicy.None);
        context.Members[0].SignatureDecodeStatus =
            SignatureDecodeStatus.Degraded;
        var apiSurface = new ApiSurface
        {
            Types = [context, new ApiType { Name = "Root" }],
        };

        Assert.Throws<UnsupportedJsExportSurfaceException>(
            () => JsExportSurfaceBuilder.Build(apiSurface));
    }

    [Fact]
    public void Build_RejectsDegradedSerializedMember()
    {
        ApiType context = CreateSerializerContext(
            "Context",
            "Root",
            JsonWireNamingPolicy.None);
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
                    SignatureDecodeStatus =
                        SignatureDecodeStatus.Degraded,
                    SignatureModel = new ApiSignature
                    {
                        ReturnType = "object",
                    },
                },
            ],
        };
        var apiSurface = new ApiSurface
        {
            Types = [context, root],
        };

        Assert.Throws<UnsupportedJsExportSurfaceException>(
            () => JsExportSurfaceBuilder.Build(apiSurface));
    }

    [Fact]
    public void Build_IgnoresDegradedExcludedMember()
    {
        ApiType context = CreateSerializerContext(
            "Context",
            "Root",
            JsonWireNamingPolicy.None);
        var root = new ApiType
        {
            Name = "Root",
            Members =
            [
                new ApiMember
                {
                    Name = "Ignored",
                    Kind = "property",
                    HasGetter = true,
                    HasJsonIgnore = true,
                    SignatureDecodeStatus =
                        SignatureDecodeStatus.Degraded,
                    SignatureModel = new ApiSignature
                    {
                        ReturnType = "object",
                    },
                },
            ],
        };
        var apiSurface = new ApiSurface
        {
            Types = [context, root],
        };

        ILInspector.JsExportSurface.JsExportSurface surface =
            JsExportSurfaceBuilder.Build(apiSurface);

        Assert.Single(surface.Records);
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

    static ApiSurface ExportSurface(int token) =>
        new()
        {
            Types =
            [
                new ApiType
                {
                    Name = "Exports",
                    Members =
                    [
                        new ApiMember
                        {
                            Name = "Run",
                            Kind = "method",
                            MetadataToken = token,
                            IsStatic = true,
                            SignatureModel = new ApiSignature
                            {
                                ReturnType = "void",
                            },
                            HasRuntimeJsExport = true,
                        },
                    ],
                },
            ],
        };

    static byte[] BuildFakeJsExportImage()
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            0,
            metadata.GetOrAddString("Fake.dll"),
            metadata.GetOrAddGuid(Guid.NewGuid()),
            default,
            default);
        metadata.AddAssembly(
            metadata.GetOrAddString("Fake"),
            new Version(1, 0, 0, 0),
            default,
            default,
            default,
            default);
        AssemblyReferenceHandle fakeAssembly =
            metadata.AddAssemblyReference(
                metadata.GetOrAddString(
                    "System.Runtime.InteropServices.JavaScript"),
                new Version(1, 0, 0, 0),
                default,
                default,
                default,
                default);
        TypeReferenceHandle fakeAttribute =
            metadata.AddTypeReference(
                fakeAssembly,
                metadata.GetOrAddString(
                    "System.Runtime.InteropServices.JavaScript"),
                metadata.GetOrAddString("JSExportAttribute"));
        var attributeConstructorSignature = new BlobBuilder();
        new BlobEncoder(attributeConstructorSignature).MethodSignature(
            SignatureCallingConvention.Default,
            genericParameterCount: 0,
            isInstanceMethod: true).Parameters(
            0,
            returnType => returnType.Void(),
            _ => { });
        MemberReferenceHandle attributeConstructor =
            metadata.AddMemberReference(
                fakeAttribute,
                metadata.GetOrAddString(".ctor"),
                metadata.GetOrAddBlob(
                    attributeConstructorSignature));

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
            metadata.GetOrAddString(
                "ILInspector.JsExportSurface.Tests"),
            metadata.GetOrAddString("FakeJsExportFixture"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        var methodSignature = new BlobBuilder();
        new BlobEncoder(methodSignature).MethodSignature(
            SignatureCallingConvention.Default,
            genericParameterCount: 0,
            isInstanceMethod: false).Parameters(
            0,
            returnType => returnType.Void(),
            _ => { });
        MethodDefinitionHandle method = metadata.AddMethodDefinition(
            MethodAttributes.Public
                | MethodAttributes.Static,
            MethodImplAttributes.Runtime,
            metadata.GetOrAddString("NotAnExport"),
            metadata.GetOrAddBlob(methodSignature),
            bodyOffset: 0,
            parameterList: MetadataTokens.ParameterHandle(1));
        var attributeValue = new BlobBuilder();
        attributeValue.WriteUInt16(1);
        attributeValue.WriteUInt16(0);
        metadata.AddCustomAttribute(
            method,
            attributeConstructor,
            metadata.GetOrAddBlob(attributeValue));

        var pe = new ManagedPEBuilder(
            PEHeaderBuilder.CreateLibraryHeader(),
            new MetadataRootBuilder(metadata, suppressValidation: true),
            new BlobBuilder(),
            flags: CorFlags.ILOnly);
        var image = new BlobBuilder();
        pe.Serialize(image);
        return image.ToArray();
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
            ReturnTypeReferences =
            [
                new("External", "Mine.Result"),
            ],
        };
        var apiSurface = new ApiSurface
        {
            AssemblyName = "Local",
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
