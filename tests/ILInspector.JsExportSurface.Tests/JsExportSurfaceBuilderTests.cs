using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Text.Json;
using ILInspector.Analysis;
using ILInspector.JsExportSurface.Fixtures;
using ILInspector.JsExportSurface.NamingFixtures;
using ILInspector.JsExportSurface.OperatorFixtures;
using ILInspector.JsExportSurface.ScalarFixtures;
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
    public void Extract_CapturesStructuredSerializerContextBaseIdentity()
    {
        using FileStream stream = File.OpenRead(
            typeof(FixtureExports).Assembly.Location);
        using var peReader = new PEReader(stream);
        ApiSurface apiSurface = ApiSurfaceExtractor.Extract(
            peReader,
            includeAll: true);
        ApiType context = Assert.Single(
            apiSurface.Types,
            type => type.Name == "FixtureJsonContext");

        Assert.Equal(
            TopLevelDefinitionName(
                "System.Text.Json.Serialization",
                "JsonSerializerContext"),
            context.BaseTypeReference?.DefinitionName);
    }

    [Fact]
    public void Build_DiscoversAllJsExportFunctions()
    {
        ILInspector.JsExportSurface.JsExportSurface surface = BuildFixtureSurface();

        var names = surface.Functions.Select(f => f.Name).ToHashSet(StringComparer.Ordinal);
        Assert.Equal(39, surface.Functions.Count);
        Assert.Contains("GetWidget", names);
        Assert.Contains("GetWidgetAsync", names);
        Assert.Contains("GetStringArrayAsyncAfterAwait", names);
        Assert.Contains("EchoBytes", names);
        Assert.Contains("GetRegisteredString", names);
        Assert.Contains("Ping", names);
        Assert.Contains("RenameWidget", names);
        Assert.Contains("GetWidgetOrOwner", names);
        Assert.Contains("GetWidgetOrRawOk", names);
        Assert.Contains("GetWidgetOrCached", names);
        Assert.Contains("GetWidgetOrCachedViaLocal", names);
        Assert.Contains("GetWidgetFromEitherJsonBranch", names);
        Assert.Contains("GetWidgetArray", names);
        Assert.Contains("GetWidgetSummary", names);
        Assert.Contains("GetWidgetPermissionSummary", names);
        Assert.Contains("GetWidgetPrioritySummary", names);
        Assert.Contains("GetWidgetAudit", names);
        Assert.Contains("GetCustomNamedGenerated", names);
        Assert.Contains("GetCustomNamedHandwritten", names);
        Assert.Contains("SetUnrelatedAsyncBuilder", names);
        Assert.Contains("RoundTripWidgetWithRuntimeTypeInfo", names);
        Assert.Contains("RoundTripWidgetWithUnrelatedTypeInfo", names);
        Assert.Contains("QueryPackage", names);
        Assert.Contains("GetInternalContextWidget", names);
        Assert.Contains("GetInternalContextCamelWidget", names);
        Assert.Contains("GetNeedsUnmappedType", names);
        Assert.Contains("GetDirectionalOutput", names);
        Assert.Contains("SetDirectionalInput", names);
        Assert.Contains("RoundTripDirectional", names);
        Assert.Contains("GetClosedGenericRoot", names);
        Assert.Contains("GetRegisteredInt", names);
        Assert.Contains("GetRegisteredIntArray", names);
        Assert.Contains("GetRegisteredByteArray", names);
        Assert.Contains("ReadRegisteredInt", names);
        Assert.Contains("GetContextSerializationOnly", names);
        Assert.Contains("SetContextSerializationOnly", names);
        Assert.Contains("SetMetadataOverride", names);
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
        Assert.Equal(0, method.RuntimeJsExportAttributeCount);
        Assert.False(method.HasMalformedRuntimeJsExportAttribute);
        Assert.DoesNotContain(
            JsExportSurfaceBuilder.Build(apiSurface).Functions,
            function => function.Name == "NotAnExport");
    }

    [Theory]
    [InlineData(".notctor", false)]
    [InlineData(".ctor", true)]
    public void Extract_RetainsMalformedAuthenticJsExportRowsAsFailureEvidence(
        string constructorName,
        bool addNamedArgument)
    {
        using var stream = new MemoryStream(
            BuildFakeJsExportImage(
                trustedAssembly: true,
                constructorName,
                addNamedArgument),
            writable: false);
        using var peReader = new PEReader(stream);
        ApiSurface apiSurface = ApiSurfaceExtractor.Extract(
            peReader,
            includeAll: true);
        ApiMember method = Assert.Single(
            Assert.Single(
                apiSurface.Types,
                type => type.Name == "FakeJsExportFixture")
                .Members,
            member => member.Name == "NotAnExport");

        Assert.False(method.HasRuntimeJsExport);
        Assert.Equal(1, method.RuntimeJsExportAttributeCount);
        Assert.True(method.HasMalformedRuntimeJsExportAttribute);
        Assert.Throws<UnsupportedJsExportSurfaceException>(
            () => JsExportSurfaceBuilder.Build(apiSurface));
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void Extract_RejectsDuplicateOrMixedAuthenticJsExportRows(
        bool duplicateValid,
        bool addMalformedSibling)
    {
        using var stream = new MemoryStream(
            BuildFakeJsExportImage(
                trustedAssembly: true,
                addDuplicateValid: duplicateValid,
                addMalformedSibling: addMalformedSibling),
            writable: false);
        using var peReader = new PEReader(stream);
        ApiSurface apiSurface = ApiSurfaceExtractor.Extract(
            peReader,
            includeAll: true);
        ApiMember method = Assert.Single(
            Assert.Single(
                apiSurface.Types,
                type => type.Name == "FakeJsExportFixture")
                .Members,
            member => member.Name == "NotAnExport");

        Assert.True(method.HasRuntimeJsExport);
        Assert.Equal(2, method.RuntimeJsExportAttributeCount);
        Assert.Equal(
            addMalformedSibling,
            method.HasMalformedRuntimeJsExportAttribute);
        Assert.Throws<UnsupportedJsExportSurfaceException>(
            () => JsExportSurfaceBuilder.Build(apiSurface));
    }

    [Fact]
    public void Build_RejectsAuthenticJsExportOperatorBeforePublication()
    {
        string path = typeof(JsExportOperatorFixture).Assembly.Location;
        ApiSurface apiSurface = ExtractApiSurface(path);
        ApiMember @operator = Assert.Single(
            Assert.Single(
                apiSurface.Types,
                type => type.Name == nameof(JsExportOperatorFixture))
                .Members,
            member => member.Kind == "operator");

        Assert.True(@operator.HasRuntimeJsExport);
        UnsupportedJsExportSurfaceException exception =
            Assert.Throws<UnsupportedJsExportSurfaceException>(
                () => JsExportSurfaceBuilder.Build(apiSurface));

        Assert.Contains(
            "JS exports must be ordinary methods",
            exception.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Build_RejectsGenericJsExportWithoutRuntimeWrapper()
    {
        string path = typeof(GenericJsExportFixture).Assembly.Location;
        ApiSurface apiSurface = ExtractApiSurface(path);
        ApiType fixture = Assert.Single(
            apiSurface.Types,
            type => type.Name == nameof(GenericJsExportFixture));
        ApiMember method = Assert.Single(
            fixture.Members,
            member => member.Name == nameof(GenericJsExportFixture.Echo));
        apiSurface.Types = [fixture];

        Assert.True(method.HasRuntimeJsExport);
        Assert.Equal(1, method.GenericArity);
        UnsupportedJsExportSurfaceException exception =
            Assert.Throws<UnsupportedJsExportSurfaceException>(
                () => JsExportSurfaceBuilder.Build(apiSurface));

        Assert.Contains(
            "generic JS exports have no runtime wrapper",
            exception.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Extract_RetainsFilteredJsExportMethodDefsAsFailureEvidence()
    {
        string path = typeof(FilteredJsExportFixture).Assembly.Location;
        ApiSurface apiSurface = ExtractApiSurface(path);
        ApiType fixture = Assert.Single(
            apiSurface.Types,
            type => type.Name == nameof(FilteredJsExportFixture));
        apiSurface.Types = [fixture];

        Assert.Equal(2, fixture.FilteredRuntimeJsExportFacts.Count);
        Assert.Contains(
            fixture.FilteredRuntimeJsExportFacts,
            fact => fact.MethodName == "get_Value"
                && fact.AttributeCount == 1
                && fact.HasValidRow
                && !fact.HasMalformedRow);
        Assert.Contains(
            fixture.FilteredRuntimeJsExportFacts,
            fact => fact.MethodName.StartsWith(
                    "<InvokeLocal>g__Local",
                    StringComparison.Ordinal)
                && fact.AttributeCount == 1
                && fact.HasValidRow
                && !fact.HasMalformedRow);

        UnsupportedJsExportSurfaceException exception =
            Assert.Throws<UnsupportedJsExportSurfaceException>(
                () => JsExportSurfaceBuilder.Build(apiSurface));
        Assert.Contains(
            "filtered MethodDefs",
            exception.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void SourceGeneratedJsExport_EmitsOnlyOrdinaryMethodWrappers()
    {
        string[] ordinaryMethodNames = ReadMethodNames(
            typeof(ScalarContextOptionsFixtureExports).Assembly.Location);
        string[] operatorMethodNames = ReadMethodNames(
            typeof(JsExportOperatorFixture).Assembly.Location);

        Assert.Contains(
            ordinaryMethodNames,
            name => name.StartsWith(
                "__Wrapper_SerializeWriteAsStringInt_",
                StringComparison.Ordinal));
        Assert.DoesNotContain(
            operatorMethodNames,
            name => name.StartsWith(
                "__Wrapper_op_Addition_",
                StringComparison.Ordinal));
        Assert.DoesNotContain(
            operatorMethodNames,
            name => name.StartsWith(
                "__Wrapper_Echo_",
                StringComparison.Ordinal));
        Assert.DoesNotContain(
            operatorMethodNames,
            name => name.StartsWith(
                "__Wrapper_get_Value_",
                StringComparison.Ordinal));
        Assert.DoesNotContain(
            operatorMethodNames,
            name => name.Contains(
                "InvokeLocal",
                StringComparison.Ordinal)
                && name.StartsWith(
                    "__Wrapper_",
                    StringComparison.Ordinal));
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
                    JsonIgnoreConditions =
                        [JsonWireIgnoreCondition.Always],
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
        Assert.Equal(19, surface.Records.Count);
        Assert.Contains(nameof(ByteEnvelopeDto), recordNames);
        Assert.Contains(nameof(BytePayloadDto), recordNames);
        Assert.Contains(nameof(CustomNamedDto), recordNames);
        Assert.Contains("WidgetDto", recordNames);
        Assert.Contains("WidgetOwner", recordNames);
        Assert.Contains("WidgetCatalog", recordNames);
        Assert.Contains("WidgetSummary", recordNames);
        Assert.Contains("WidgetPermissionSummary", recordNames);
        Assert.Contains("WidgetPrioritySummary", recordNames);
        Assert.Contains("WidgetAudit", recordNames);
        Assert.Contains("ConflictingPolicyWidget", recordNames);
        Assert.Contains("NeedsUnmappedTypeFixture", recordNames);
        Assert.Contains("DirectionalOutputDto", recordNames);
        Assert.Contains("DirectionalInputDto", recordNames);
        Assert.Contains("DirectionalRoundTripDto", recordNames);
        Assert.Contains("DirectionalNote", recordNames);
        Assert.Contains(nameof(ClosedGenericRootDto), recordNames);
        Assert.Contains(nameof(ContextSerializationOnlyDto), recordNames);
        Assert.Contains(nameof(MetadataOverrideDto), recordNames);
    }

    [Fact]
    public void Build_DoesNotDiscoverHandwrittenContextProperties()
    {
        ILInspector.JsExportSurface.JsExportSurface surface =
            BuildFixtureSurface(includeAll: true);

        Assert.DoesNotContain(
            surface.Records,
            record =>
                record.Name
                == nameof(HandwrittenContextPropertyFixture));
    }

    [Fact]
    public void Extract_CapturesCustomJsonSerializableTypeInfoPropertyName()
    {
        using FileStream stream = File.OpenRead(
            typeof(CustomNamedJsonContext).Assembly.Location);
        using var peReader = new PEReader(stream);
        ApiSurface apiSurface = ApiSurfaceExtractor.Extract(
            peReader,
            includeAll: true);

        ApiType context = Assert.Single(
            apiSurface.Types,
            type => type.Name == nameof(CustomNamedJsonContext));
        ApiJsonSerializableRoot root =
            Assert.Single(context.JsonSerializableRoots);

        Assert.Equal(
            "RegisteredCustomNamed",
            root.TypeInfoPropertyName);
    }

    [Fact]
    public void Build_AuthenticatesNestedSerializerRootUsingLeafPropertyName()
    {
        string path =
            typeof(NestedJsonSerializableRootFixtureContext)
                .Assembly.Location;
        using FileStream stream = File.OpenRead(path);
        using var peReader = new PEReader(stream);
        ApiSurface extracted = ApiSurfaceExtractor.Extract(
            peReader,
            includeAll: true);
        ApiType context = Assert.Single(
            extracted.Types,
            type => type.Name
                == nameof(NestedJsonSerializableRootFixtureContext));
        ApiJsonSerializableRoot root =
            Assert.Single(context.JsonSerializableRoots);
        Assert.Null(root.TypeInfoPropertyName);
        Assert.Equal(
            ApiTypeShapeKind.Named,
            Assert.IsType<ApiTypeShape>(root.Type).Kind);
        Assert.Contains(
            context.Members,
            member => member.Kind == "property" && member.Name == "Leaf");

        ApiType leaf = Assert.Single(
            extracted.Types,
            type => type.DefinitionName?.Segments
                is [nameof(NestedJsonSerializableRootFixture), "Leaf"]);
        var selected = new ApiSurface
        {
            AssemblyIdentity = extracted.AssemblyIdentity,
            Types = [context, leaf],
        };

        ILInspector.JsExportSurface.JsExportSurface surface =
            JsExportSurfaceBuilder.Build(selected);

        Assert.Contains(surface.Records, record => record == leaf);
    }

    [Fact]
    public void Build_RejectsNestedAndTopLevelSerializerRootCollisionWhenReached()
    {
        string path =
            typeof(NestedJsonSerializableRootCollisionContext)
                .Assembly.Location;
        using FileStream stream = File.OpenRead(path);
        using var peReader = new PEReader(stream);
        ApiSurface apiSurface = ApiSurfaceExtractor.Extract(
            peReader,
            includeAll: true);
        ApiMember serializer = Assert.Single(
            Assert.Single(
                apiSurface.Types,
                type => type.Name
                    == nameof(NestedJsonSerializableRootCollisionSerializer))
                .Members,
            member => member.Name
                == nameof(NestedJsonSerializableRootCollisionSerializer.Serialize));
        serializer.HasRuntimeJsExport = true;
        LibraryBodyIndex bodyIndex = LibraryBodyIndex.Open(
            path,
            LibraryBodyAnalysisFeatures.MethodEvidence
                | LibraryBodyAnalysisFeatures.JsonWireContractFlow);

        UnsupportedJsExportSurfaceException exception =
            Assert.Throws<UnsupportedJsExportSurfaceException>(
                () => JsExportSurfaceBuilder.Build(apiSurface, bodyIndex));

        Assert.Contains(
            "serializer root property identity is ambiguous",
            exception.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Extract_PreservesClosedGenericAndPrimitiveSerializerRootShapes()
    {
        ApiSurface apiSurface = ExtractFixtureApiSurface();
        ApiType genericContext = Assert.Single(
            apiSurface.Types,
            type => type.Name
                == nameof(ClosedGenericRootFixtureJsonContext));
        ApiTypeShape generic = Assert.IsType<ApiTypeShape>(
            Assert.Single(genericContext.JsonSerializableRoots).Type);
        Assert.Equal(ApiTypeShapeKind.GenericInstance, generic.Kind);
        Assert.Equal(
            "System.Collections.Generic.Dictionary`2",
            generic.Definition?.FullName);
        Assert.Equal(
            [ApiPrimitiveType.String, null],
            generic.TypeArguments.Select(argument => argument.Primitive));
        Assert.Equal(
            nameof(ClosedGenericRootDto),
            generic.TypeArguments[1].Definition?.DefinitionName?.Segments
                .Last());

        ApiType primitiveContext = Assert.Single(
            apiSurface.Types,
            type => type.Name
                == nameof(PrimitiveRootFixtureJsonContext));
        ApiTypeShape[] roots =
            [.. primitiveContext.JsonSerializableRoots.Select(
                root => Assert.IsType<ApiTypeShape>(root.Type))];
        Assert.Equal(
            [
                ApiTypeShapeKind.Primitive,
                ApiTypeShapeKind.SzArray,
                ApiTypeShapeKind.SzArray,
            ],
            roots.Select(root => root.Kind));
        Assert.Equal(ApiPrimitiveType.Int32, roots[0].Primitive);
        Assert.Equal(ApiPrimitiveType.Int32, roots[1].ElementType?.Primitive);
        Assert.Equal(ApiPrimitiveType.Byte, roots[2].ElementType?.Primitive);

        ApiType modeContext = Assert.Single(
            apiSurface.Types,
            type => type.Name
                == nameof(SourceGenerationModeFixtureJsonContext));
        Assert.Equal(
            JsonSourceGenerationMode.Serialization,
            modeContext.JsonSourceGenerationMode);
        Assert.Equal(
            JsonSourceGenerationMode.Default,
            Assert.Single(
                modeContext.JsonSerializableRoots,
                root => root.Type?.Definition?.DefinitionName?.Segments
                    is [nameof(ContextSerializationOnlyDto)])
                .GenerationMode);
        Assert.Equal(
            JsonSourceGenerationMode.Metadata,
            Assert.Single(
                modeContext.JsonSerializableRoots,
                root => root.Type?.Definition?.DefinitionName?.Segments
                    is [nameof(MetadataOverrideDto)])
                .GenerationMode);
    }

    [Fact]
    public void Build_DiscoversClosedGenericRootWithoutFailingAnUnreachedUnsupportedContext()
    {
        ApiSurface apiSurface = ExtractFixtureApiSurface();
        ApiType trustedContext = Assert.Single(
            apiSurface.Types,
            type => type.Name == nameof(FixtureJsonContext));
        apiSurface.Types.Add(new ApiType
        {
            Name = "UnreachedUnsupportedContext",
            BaseType =
                "System.Text.Json.Serialization.JsonSerializerContext",
            BaseTypeReference = trustedContext.BaseTypeReference,
            JsonSerializableAttributeCount = 1,
            JsonSerializableRoots =
            [
                new(
                    ElementType: null,
                    IsArray: false)
                {
                    UnsupportedReason =
                        "serializer root type shape is unsupported",
                },
            ],
        });

        ILInspector.JsExportSurface.JsExportSurface surface =
            JsExportSurfaceBuilder.Build(apiSurface);

        Assert.Contains(
            surface.Records,
            record => record.Name == nameof(ClosedGenericRootDto));
    }

    [Fact]
    public void Build_ReportsUnsupportedSerializerRootOnlyWhenAnExportReachesItsProperty()
    {
        ApiSurface apiSurface = ExtractFixtureApiSurface();
        ApiType context = Assert.Single(
            apiSurface.Types,
            type => type.Name == nameof(CustomNamedJsonContext));
        context.JsonSerializableRoots[0] =
            context.JsonSerializableRoots[0] with
            {
                Type = null,
                UnsupportedReason =
                    "serializer root type shape is unsupported",
            };

        Assert.NotNull(JsExportSurfaceBuilder.Build(apiSurface));

        string path = typeof(FixtureExports).Assembly.Location;
        LibraryBodyIndex bodyIndex = LibraryBodyIndex.Open(
            path,
            LibraryBodyAnalysisFeatures.MethodEvidence
                | LibraryBodyAnalysisFeatures.JsonWireContractFlow);
        UnsupportedJsExportSurfaceException exception =
            Assert.Throws<UnsupportedJsExportSurfaceException>(
                () => JsExportSurfaceBuilder.Build(apiSurface, bodyIndex));

        Assert.Contains(
            "serializer root type shape is unsupported",
            exception.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Build_BindsUnnamedMalformedRootToReachedTrustedGetter()
    {
        ApiSurface apiSurface = ExtractFixtureApiSurface();
        ApiType context = Assert.Single(
            apiSurface.Types,
            type => type.Name == nameof(FixtureJsonContext));
        int rootIndex = context.JsonSerializableRoots.FindIndex(
            root => root.Type?.Definition?.DefinitionName?.Segments
                is [nameof(WidgetDto)]);
        Assert.True(rootIndex >= 0);
        context.JsonSerializableRoots[rootIndex] =
            context.JsonSerializableRoots[rootIndex] with
            {
                Type = null,
                UnsupportedReason =
                    "serializer root type shape is unsupported",
            };

        Assert.NotNull(JsExportSurfaceBuilder.Build(apiSurface));

        string path = typeof(FixtureExports).Assembly.Location;
        LibraryBodyIndex bodyIndex = LibraryBodyIndex.Open(
            path,
            LibraryBodyAnalysisFeatures.MethodEvidence
                | LibraryBodyAnalysisFeatures.JsonWireContractFlow);
        UnsupportedJsExportSurfaceException exception =
            Assert.Throws<UnsupportedJsExportSurfaceException>(
                () => JsExportSurfaceBuilder.Build(apiSurface, bodyIndex));

        Assert.Contains(
            "serializer root type shape is unsupported",
            exception.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Build_RejectsReachedUnsupportedScalarContextOptions()
    {
        string path =
            typeof(ScalarContextOptionsFixtureExports).Assembly.Location;

#pragma warning disable CA1416 // The browser-marked fixture executes serializer-only code in this test.
        Assert.Equal(
            "\"42\"",
            ScalarContextOptionsFixtureExports.SerializeWriteAsStringInt());
#pragma warning restore CA1416

        ApiSurface apiSurface = ExtractApiSurface(path);
        ApiType context = Assert.Single(
            apiSurface.Types,
            type => type.Name == nameof(UnsupportedScalarContextOptions));
        ApiJsonSerializableRoot root =
            Assert.Single(context.JsonSerializableRoots);
        Assert.Null(root.TypeInfoPropertyName);
        Assert.Equal(ApiPrimitiveType.Int32, root.Type?.Primitive);
        Assert.Contains(
            context.Members,
            member => member.Kind == "property" && member.Name == "Int32");

        UnsupportedJsExportSurfaceException exception =
            Assert.Throws<UnsupportedJsExportSurfaceException>(
                () => JsExportSurfaceBuilder.Build(
                    apiSurface,
                    OpenWireContractBodyIndex(path)));

        Assert.Contains(
            "serializer context options are unsupported",
            exception.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Build_IgnoresUnusedUnsupportedScalarContextAndResolvesVectorSibling()
    {
        string path =
            typeof(ScalarContextOptionsFixtureExports).Assembly.Location;
        ApiSurface apiSurface = ExtractApiSurface(path);
        ApiType unusedContext = Assert.Single(
            apiSurface.Types,
            type =>
                type.Name
                == nameof(UnusedUnsupportedScalarContextOptions));
        Assert.Contains(
            unusedContext.Members,
            member => member.Kind == "property" && member.Name == "Int32");
        ApiMember scalarExport = Assert.Single(
            Assert.Single(
                apiSurface.Types,
                type =>
                    type.Name
                    == nameof(ScalarContextOptionsFixtureExports))
                .Members,
            member =>
                member.Name
                == nameof(
                    ScalarContextOptionsFixtureExports
                        .SerializeWriteAsStringInt));
        scalarExport.HasRuntimeJsExport = false;
        scalarExport.RuntimeJsExportAttributeCount = 0;
        scalarExport.HasMalformedRuntimeJsExportAttribute = false;

        ApiMember vectorSerializer = Assert.Single(
            Assert.Single(
                apiSurface.Types,
                type =>
                    type.Name
                    == nameof(ScalarContextOptionsFixtureExports))
                .Members,
            member =>
                member.Name
                == nameof(
                    ScalarContextOptionsFixtureExports.SerializeVector));
        Assert.True(vectorSerializer.HasRuntimeJsExport);

        ILInspector.JsExportSurface.JsExportSurface surface =
            JsExportSurfaceBuilder.Build(
                apiSurface,
                OpenWireContractBodyIndex(path));

        Assert.Equal(
            "int[]",
            Assert.Single(surface.Functions).ReturnWireType);
    }

    [Fact]
    public void Extract_RecordsMultidimensionalRootEvidenceAndSourceGeneratorNames()
    {
        ApiSurface apiSurface = ExtractApiSurface(
            typeof(ArrayRootNamingFixtureExports).Assembly.Location);
        ApiType context = Assert.Single(
            apiSurface.Types,
            type => type.Name == nameof(ArrayRootNamingFixtureContext));

        Assert.Contains(
            context.JsonSerializableRoots,
            root => root.Type is
            {
                Kind: ApiTypeShapeKind.Array,
                ArrayRank: 2,
            }
                && root.UnsupportedReason
                    == "multidimensional serializer roots are not supported");
        Assert.Contains(
            context.JsonSerializableRoots,
            root => root.Type is
            {
                Kind: ApiTypeShapeKind.Array,
                ArrayRank: 3,
            }
                && root.UnsupportedReason
                    == "multidimensional serializer roots are not supported");
        Assert.Contains(
            context.JsonSerializableRoots,
            root => root.Type is
            {
                Kind: ApiTypeShapeKind.SzArray,
            }
                && root.UnsupportedReason is null);

        string[] generatedNames =
        [
            "Int32Array",
            "Int32Array2D",
            "Int32Array3D",
            "Int32ArrayArray",
            "Int32Array2DArray",
            "Int32ArrayArray2D",
        ];
        foreach (string generatedName in generatedNames)
        {
            Assert.Contains(
                context.Members,
                member =>
                    member.Kind == "property"
                    && member.Name == generatedName);
        }
    }

    [Theory]
    [InlineData(nameof(ArrayRootNamingFixtureExports.SerializeIntMatrix))]
    [InlineData(nameof(ArrayRootNamingFixtureExports.SerializeIntCube))]
    [InlineData(nameof(ArrayRootNamingFixtureExports.SerializeIntArrayMatrix))]
    [InlineData(nameof(ArrayRootNamingFixtureExports.SerializeIntMatrixArray))]
    public void Build_RejectsReachedMultidimensionalSerializerRoot(
        string serializerName)
    {
        string path =
            typeof(ArrayRootNamingFixtureExports).Assembly.Location;
        ApiSurface apiSurface = ExtractApiSurface(path);
        ApiMember serializer = Assert.Single(
            Assert.Single(
                apiSurface.Types,
                type => type.Name == nameof(ArrayRootNamingFixtureExports))
                .Members,
            member => member.Name == serializerName);
        serializer.HasRuntimeJsExport = true;

        UnsupportedJsExportSurfaceException exception =
            Assert.Throws<UnsupportedJsExportSurfaceException>(
                () => JsExportSurfaceBuilder.Build(
                    apiSurface,
                    OpenWireContractBodyIndex(path)));

        Assert.Contains(
            "multidimensional serializer roots are not supported",
            exception.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Build_DoesNotNormalizeNonDefaultMultidimensionalArrayBounds()
    {
        string path =
            typeof(ArrayRootNamingFixtureExports).Assembly.Location;
        ApiSurface apiSurface = ExtractApiSurface(path);
        ApiType context = Assert.Single(
            apiSurface.Types,
            type => type.Name == nameof(ArrayRootNamingFixtureContext));
        ApiMember matrixProperty = Assert.Single(
            context.Members,
            member => member.Name == "Int32Array2D");
        ApiTypeReferenceIdentity jsonTypeInfo = Assert.IsType<ApiTypeShape>(
            matrixProperty.SignatureModel?.ReturnTypeShape).Definition!;
        matrixProperty.SignatureModel!.ReturnTypeShape =
            ApiTypeShape.GenericInstance(
                jsonTypeInfo,
                [
                    ApiTypeShape.Array(
                        ApiTypeShape.PrimitiveType(ApiPrimitiveType.Int32),
                        rank: 2,
                        arraySizes: ImmutableArray.Create(1, 1),
                        arrayLowerBounds: ImmutableArray.Create(0, 0)),
                ]);

        ApiMember serializer = Assert.Single(
            Assert.Single(
                apiSurface.Types,
                type => type.Name == nameof(ArrayRootNamingFixtureExports))
                .Members,
            member =>
                member.Name
                == nameof(ArrayRootNamingFixtureExports.SerializeIntMatrix));
        serializer.HasRuntimeJsExport = true;

        ILInspector.JsExportSurface.JsExportSurface surface =
            JsExportSurfaceBuilder.Build(
                apiSurface,
                OpenWireContractBodyIndex(path));

        Assert.Null(Assert.Single(surface.Functions).ReturnWireType);
    }

    [Fact]
    public void SourceGeneratedJson_MultidimensionalRootRemainsUnsupportedAtRuntime()
    {
        Assert.Throws<NotSupportedException>(
            ArrayRootNamingFixtureExports.SerializeIntMatrix);
    }

    [Fact]
    public void Build_DefersUnreachedAmbiguousAndRejectsMalformedGeneratedPropertyIdentities()
    {
        ApiSurface duplicate = ExtractFixtureApiSurface();
        ApiType duplicateContext = Assert.Single(
            duplicate.Types,
            type => type.Name == nameof(CustomNamedJsonContext));
        ApiJsonSerializableRoot root =
            Assert.Single(duplicateContext.JsonSerializableRoots);
        duplicateContext.JsonSerializableRoots.Add(root);
        duplicateContext.JsonSerializableAttributeCount++;

        Assert.NotNull(JsExportSurfaceBuilder.Build(duplicate));

        ApiSurface malformed = ExtractFixtureApiSurface();
        ApiType malformedContext = Assert.Single(
            malformed.Types,
            type => type.Name == nameof(CustomNamedJsonContext));
        malformedContext.JsonSerializableRoots[0] =
            root with { TypeInfoPropertyName = "" };

        Assert.Throws<UnsupportedJsExportSurfaceException>(
            () => JsExportSurfaceBuilder.Build(malformed));
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
        ApiAssemblyIdentity assemblyIdentity =
            ApiSurfaceExtractor.Extract(
                peReader,
                includeAll: true)
                .AssemblyIdentity!;
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
                typeof(NamedEnumFixture).FullName!,
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

    static byte[] BuildFakeJsExportImage(
        bool trustedAssembly = false,
        string constructorName = ".ctor",
        bool addNamedArgument = false,
        bool addDuplicateValid = false,
        bool addMalformedSibling = false)
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
                new Version(11, 0, 0, 0),
                default,
                trustedAssembly
                    ? metadata.GetOrAddBlob(
                        new byte[]
                        {
                            0xcc, 0x7b, 0x13, 0xff,
                            0xcd, 0x2d, 0xdd, 0x51,
                        })
                    : default,
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
                metadata.GetOrAddString(constructorName),
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
        if (addNamedArgument)
        {
            attributeValue.WriteUInt16(1);
            attributeValue.WriteByte(0x54);
            attributeValue.WriteByte(0x0e);
            attributeValue.WriteSerializedString("Bogus");
            attributeValue.WriteSerializedString("value");
        }
        else
        {
            attributeValue.WriteUInt16(0);
        }
        metadata.AddCustomAttribute(
            method,
            attributeConstructor,
            metadata.GetOrAddBlob(attributeValue));
        if (addDuplicateValid)
        {
            metadata.AddCustomAttribute(
                method,
                attributeConstructor,
                metadata.GetOrAddBlob(attributeValue));
        }
        if (addMalformedSibling)
        {
            var malformedValue = new BlobBuilder();
            malformedValue.WriteUInt16(1);
            malformedValue.WriteUInt16(1);
            malformedValue.WriteByte(0x54);
            malformedValue.WriteByte(0x0e);
            malformedValue.WriteSerializedString("Bogus");
            malformedValue.WriteSerializedString("value");
            metadata.AddCustomAttribute(
                method,
                attributeConstructor,
                metadata.GetOrAddBlob(malformedValue));
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

    static ApiSurface ExtractFixtureApiSurface()
    {
        using FileStream stream = File.OpenRead(
            typeof(FixtureExports).Assembly.Location);
        using var peReader = new PEReader(stream);
        return ApiSurfaceExtractor.Extract(peReader, includeAll: true);
    }

    static ApiSurface ExtractApiSurface(string path)
    {
        using FileStream stream = File.OpenRead(path);
        using var peReader = new PEReader(stream);
        return ApiSurfaceExtractor.Extract(peReader, includeAll: true);
    }

    static LibraryBodyIndex OpenWireContractBodyIndex(string path) =>
        LibraryBodyIndex.Open(
            path,
            LibraryBodyAnalysisFeatures.MethodEvidence
                | LibraryBodyAnalysisFeatures.JsonWireContractFlow);

    static string[] ReadMethodNames(string path)
    {
        using FileStream stream = File.OpenRead(path);
        using var peReader = new PEReader(stream);
        MetadataReader reader = peReader.GetMetadataReader();
        return
        [
            .. reader.MethodDefinitions.Select(handle =>
                reader.GetString(reader.GetMethodDefinition(handle).Name)),
        ];
    }

    static MetadataTypeDefinitionName TopLevelDefinitionName(
        string @namespace,
        string name) =>
        Assert.IsType<MetadataTypeDefinitionNameResult.Valid>(
            MetadataTypeDefinitionName.Create(
                @namespace,
                [name])).Name;

    /// <summary>
    /// Directions come from how exports use a type, so a DTO reached only
    /// through a resolved return wire type is serialize-only, one reached only
    /// through a resolved parameter wire type is deserialize-only, and one
    /// reached both ways is bidirectional.
    /// </summary>
    [Theory]
    [InlineData(
        nameof(DirectionalOutputDto),
        JsonWireDirection.Serialize)]
    [InlineData(
        nameof(DirectionalInputDto),
        JsonWireDirection.Deserialize)]
    [InlineData(
        nameof(DirectionalRoundTripDto),
        JsonWireDirection.Both)]
    [InlineData(
        nameof(DirectionalNote),
        JsonWireDirection.Serialize)]
    public void Build_RecordsSerializeOnlyDirectionForReturnOnlyDto(
        string typeName,
        JsonWireDirection expected)
    {
        string path = typeof(FixtureExports).Assembly.Location;
        using FileStream stream = File.OpenRead(path);
        using var peReader = new PEReader(stream);
        ApiSurface apiSurface = ApiSurfaceExtractor.Extract(
            peReader,
            includeAll: false);
        var bodyIndex = LibraryBodyIndex.Open(
            path,
            LibraryBodyAnalysisFeatures.MethodEvidence
                | LibraryBodyAnalysisFeatures.JsonWireContractFlow);

        ILInspector.JsExportSurface.JsExportSurface surface =
            JsExportSurfaceBuilder.Build(apiSurface, bodyIndex);

        ApiType record = Assert.Single(
            surface.Records,
            candidate => candidate.Name == typeName);
        Assert.Equal(expected, surface.WireDirections[record]);
    }

    /// <summary>
    /// Without body evidence there are no resolved wire types, so no direction
    /// is recorded and consumers fall back to the conservative bidirectional
    /// reading.
    /// </summary>
    [Fact]
    public void Build_RecordsNoDirectionsWithoutBodyEvidence()
    {
        ILInspector.JsExportSurface.JsExportSurface surface =
            BuildFixtureSurface();

        Assert.Empty(surface.WireDirections);
    }

    [Fact]
    public void Build_ResolvesParameterWireTypeReferences()
    {
        string path = typeof(FixtureExports).Assembly.Location;
        using FileStream stream = File.OpenRead(path);
        using var peReader = new PEReader(stream);
        ApiSurface apiSurface = ApiSurfaceExtractor.Extract(
            peReader,
            includeAll: false);
        var bodyIndex = LibraryBodyIndex.Open(
            path,
            LibraryBodyAnalysisFeatures.MethodEvidence
                | LibraryBodyAnalysisFeatures.JsonWireContractFlow);

        JsExportFunction function = Assert.Single(
            JsExportSurfaceBuilder.Build(apiSurface, bodyIndex).Functions,
            candidate => candidate.Name == "SetDirectionalInput");

        Assert.Contains(
            function.ParameterWireTypeReferences,
            reference => reference.DefinitionName?.Segments
                is [nameof(DirectionalInputDto)]);
    }
}
