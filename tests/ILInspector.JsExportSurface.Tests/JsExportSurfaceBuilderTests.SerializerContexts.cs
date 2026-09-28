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
        Assert.Equal(23, surface.Records.Count);
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
        Assert.Contains("DirectionalSharedInputDto", recordNames);
        Assert.Contains("DirectionalInactiveInputDto", recordNames);
        Assert.Contains("DirectionalAccessorInputDto", recordNames);
        Assert.Contains("DirectionalServerNoteDto", recordNames);
        Assert.Contains("DirectionalNote", recordNames);
        Assert.Contains("DirectionalConditionalNote", recordNames);
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
        Assert.True(serializer.HasRuntimeJsExport);
        SelectOnlyRuntimeJsExport(apiSurface, serializer);
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
                ApiTypeShapeKind.Named,
                ApiTypeShapeKind.SzArray,
            ],
            roots.Select(root => root.Kind));
        Assert.Equal(ApiPrimitiveType.Int32, roots[0].Primitive);
        Assert.Equal(ApiPrimitiveType.Int32, roots[1].ElementType?.Primitive);
        Assert.Equal(ApiPrimitiveType.Byte, roots[2].ElementType?.Primitive);
        Assert.Equal(
            "System.Decimal",
            roots[3].Definition?.FullName);
        Assert.Equal(
            "System.Decimal",
            roots[4].ElementType?.Definition?.FullName);

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
    public void Build_RejectsReachedPopulateObjectCreationHandling()
    {
        string path = typeof(PopulateExports).Assembly.Location;

#pragma warning disable CA1416 // The browser-marked fixture executes serializer-only code in this test.
        PopulateInput runtime = JsonSerializer.Deserialize(
            """{"Values":[2]}""",
            PopulateJsonContext.Default.PopulateInput)!;
        Assert.Equal([1, 2], runtime.Values);
        Assert.Equal(
            2,
            PopulateExports.CountValues("""{"Values":[2]}"""));
#pragma warning restore CA1416

        ApiSurface apiSurface = ExtractApiSurface(path);
        ApiType context = Assert.Single(
            apiSurface.Types,
            type => type.Name == nameof(PopulateJsonContext));
        Assert.Equal(
            JsonWireNamingPolicy.Unsupported,
            context.JsonPropertyNamingPolicy);
        apiSurface.FilteredRuntimeJsExportFacts = [];
        apiSurface.Types =
        [
            .. apiSurface.Types.Where(type =>
                type.Name is nameof(PopulateInput)
                    or nameof(PopulateJsonContext)
                    or nameof(PopulateExports)),
        ];

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

    [Theory]
    [InlineData(
        nameof(AttributePopulateExports.CountTypeValues),
        nameof(TypeAttributePopulateContract))]
    [InlineData(
        nameof(AttributePopulateExports.CountPropertyValues),
        nameof(PropertyAttributePopulateContract))]
    public void Emit_BlocksReachedPopulateObjectCreationHandlingAttribute(
        string exportName,
        string contractName)
    {
        string path =
            typeof(AttributePopulateExports).Assembly.Location;

#pragma warning disable CA1416
        Assert.Equal(
            2,
            exportName
                == nameof(
                    AttributePopulateExports.CountTypeValues)
                ? AttributePopulateExports.CountTypeValues(
                    """{"Values":[2]}""")
                : AttributePopulateExports.CountPropertyValues(
                    """{"Values":[2]}"""));
#pragma warning restore CA1416

        ApiSurface apiSurface = ExtractApiSurface(path);
        ApiType contract = Assert.Single(
            apiSurface.Types,
            type => type.Name == contractName);
        Assert.True(
            contract.HasUnsupportedJsonWireAttributes
            || Assert.Single(
                    contract.Members,
                    member => member.Name == "Values")
                .HasUnsupportedJsonWireAttributes);
        ApiType exports = Assert.Single(
            apiSurface.Types,
            type => type.Name
                == nameof(AttributePopulateExports));
        ApiType context = Assert.Single(
            apiSurface.Types,
            type => type.Name
                == nameof(AttributePopulateJsonContext));
        foreach (ApiMember export in exports.Members.Where(
            member => member.HasRuntimeJsExport
                && member.Name != exportName))
        {
            export.HasRuntimeJsExport = false;
            export.RuntimeJsExportAttributeCount = 0;
            export.HasMalformedRuntimeJsExportAttribute = false;
        }
        apiSurface.FilteredRuntimeJsExportFacts = [];
        foreach (ApiType type in new[]
        {
            contract,
            context,
            exports,
        })
        {
            type.FilteredRuntimeJsExportFacts = [];
        }
        apiSurface.Types =
        [
            contract,
            context,
            exports,
        ];

        ILInspector.JsExportSurface.JsExportSurface surface =
            JsExportSurfaceBuilder.Build(
                apiSurface,
                OpenWireContractBodyIndex(path));
        var diagnostics = new TypeScriptGenerationDiagnostics();
        string dts = DtsEmitter.Emit(
            surface,
            diagnostics);
        Assert.Contains(
            $"export type {contractName} = unknown;",
            dts,
            StringComparison.Ordinal);
        TypeScriptGenerationDiagnostic diagnostic =
            Assert.Single(diagnostics.UnmappedTypes);
        Assert.Equal(
            $"{contractName} JSON wire shape",
            diagnostic.Location);
    }

    [Fact]
    public void Extract_AcceptsExplicitReplaceObjectCreationHandlingAttribute()
    {
        ApiSurface apiSurface = ExtractApiSurface(
            typeof(TypeAttributeReplaceContract)
                .Assembly.Location);
        ApiType contract = Assert.Single(
            apiSurface.Types,
            type => type.Name
                == nameof(TypeAttributeReplaceContract));

        Assert.False(
            contract.HasUnsupportedJsonWireAttributes);
        Assert.False(
            Assert.Single(
                    contract.Members,
                    member => member.Name == "Values")
                .HasUnsupportedJsonWireAttributes);
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
        ApiType scalarExports = Assert.Single(
            apiSurface.Types,
            type => type.Name
                == nameof(ScalarContextOptionsFixtureExports));
        ApiMember vectorSerializer = Assert.Single(
            scalarExports.Members,
            member =>
                member.Name
                == nameof(
                    ScalarContextOptionsFixtureExports.SerializeVector));
        Assert.True(vectorSerializer.HasRuntimeJsExport);
        foreach (ApiMember export in scalarExports.Members.Where(
            member => member.HasRuntimeJsExport
                && member != vectorSerializer))
        {
            export.HasRuntimeJsExport = false;
            export.RuntimeJsExportAttributeCount = 0;
            export.HasMalformedRuntimeJsExportAttribute = false;
        }

        LibraryBodyIndex bodyIndex =
            OpenWireContractBodyIndex(path);
        DirectCall typeInfoGetter = Assert.Single(
            bodyIndex.DirectCalls,
            call => call.Caller.Name
                    == nameof(
                        ScalarContextOptionsFixtureExports
                            .SerializeVector)
                && call.Callee.Name == "get_Int32Array");
        Assert.NotNull(typeInfoGetter.ReceiverSource);
        Assert.True(typeInfoGetter.ReceiverSource.IsComplete);
        int receiverOffset = Assert.Single(
            typeInfoGetter.ReceiverSource.SourceCallOffsets);
        DirectCall defaultGetter = Assert.Single(
            bodyIndex.DirectCalls,
            call => call.EvidenceMethod
                    == typeInfoGetter.EvidenceMethod
                && call.ILOffset == receiverOffset);
        Assert.Equal("get_Default", defaultGetter.Callee.Name);
        ApiType supportedContext = Assert.Single(
            apiSurface.Types,
            type => type.Name
                == nameof(SupportedScalarContextOptions));
        ApiMember defaultProperty = Assert.Single(
            supportedContext.Members,
            member => member.Name == "Default");
        Assert.Equal(
            defaultProperty.GetterToken,
            defaultGetter.CalleeDefinitionToken);

        ILInspector.JsExportSurface.JsExportSurface surface =
            JsExportSurfaceBuilder.Build(
                apiSurface,
                bodyIndex);

        Assert.Equal(
            "int[]",
            Assert.Single(surface.Functions).ReturnWireType);
    }

    [Fact]
    public void Build_RejectsDefaultContextReturnWithCollidingStructuredIdentity()
    {
        string path =
            typeof(ScalarContextOptionsFixtureExports).Assembly.Location;
        ApiSurface apiSurface = ExtractApiSurface(path);
        ApiType exports = Assert.Single(
            apiSurface.Types,
            type => type.Name
                == nameof(ScalarContextOptionsFixtureExports));
        ApiMember vectorSerializer = Assert.Single(
            exports.Members,
            member => member.Name
                == nameof(
                    ScalarContextOptionsFixtureExports.SerializeVector));
        foreach (ApiMember export in exports.Members.Where(
            member => member.HasRuntimeJsExport
                && member != vectorSerializer))
        {
            export.HasRuntimeJsExport = false;
            export.RuntimeJsExportAttributeCount = 0;
            export.HasMalformedRuntimeJsExportAttribute = false;
        }

        ApiType context = Assert.Single(
            apiSurface.Types,
            type => type.Name
                == nameof(SupportedScalarContextOptions));
        ApiMember defaultProperty = Assert.Single(
            context.Members,
            member => member.Name == "Default");
        ApiSignature defaultSignature =
            Assert.IsType<ApiSignature>(
                defaultProperty.SignatureModel);
        ApiTypeReferenceIdentity authenticReturn =
            Assert.Single(defaultSignature.ReturnTypeReferences);
        MetadataTypeDefinitionName authenticName =
            Assert.IsType<MetadataTypeDefinitionName>(
                authenticReturn.DefinitionName);
        int namespaceSeparator =
            authenticName.Namespace.LastIndexOf('.');
        Assert.True(namespaceSeparator > 0);
        MetadataTypeDefinitionName collision =
            Assert.IsType<MetadataTypeDefinitionNameResult.Valid>(
                MetadataTypeDefinitionName.Create(
                    authenticName.Namespace[..namespaceSeparator],
                    [
                        authenticName.Namespace[
                            (namespaceSeparator + 1)..],
                        .. authenticName.Segments,
                    ]))
                .Name;
        Assert.NotEqual(authenticName, collision);
        LibraryBodyIndex bodyIndex =
            OpenWireContractBodyIndex(path);

        Assert.Equal(
            "int[]",
            Assert.Single(
                JsExportSurfaceBuilder.Build(
                    apiSurface,
                    bodyIndex).Functions)
                .ReturnWireType);
        defaultSignature.ReturnTypeReferences =
        [
            authenticReturn with
            {
                DefinitionName = collision,
            },
        ];

        Assert.Single(
            JsExportSurfaceBuilder.Build(
                apiSurface).Functions);
        UnsupportedJsExportSurfaceException exception =
            Assert.Throws<UnsupportedJsExportSurfaceException>(
                () => JsExportSurfaceBuilder.Build(
                    apiSurface,
                    bodyIndex));
        Assert.Contains(
            "no authentic default-instance getter",
            exception.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Build_RejectsDefaultContextReturnWithoutStructuredIdentity()
    {
        string path =
            typeof(ScalarContextOptionsFixtureExports).Assembly.Location;
        ApiSurface apiSurface = ExtractApiSurface(path);
        ApiType exports = Assert.Single(
            apiSurface.Types,
            type => type.Name
                == nameof(ScalarContextOptionsFixtureExports));
        ApiMember vectorSerializer = Assert.Single(
            exports.Members,
            member => member.Name
                == nameof(
                    ScalarContextOptionsFixtureExports.SerializeVector));
        foreach (ApiMember export in exports.Members.Where(
            member => member.HasRuntimeJsExport
                && member != vectorSerializer))
        {
            export.HasRuntimeJsExport = false;
            export.RuntimeJsExportAttributeCount = 0;
            export.HasMalformedRuntimeJsExportAttribute = false;
        }

        ApiType context = Assert.Single(
            apiSurface.Types,
            type => type.Name
                == nameof(SupportedScalarContextOptions));
        ApiMember defaultProperty = Assert.Single(
            context.Members,
            member => member.Name == "Default");
        ApiSignature defaultSignature =
            Assert.IsType<ApiSignature>(
                defaultProperty.SignatureModel);
        ApiTypeReferenceIdentity authenticReturn =
            Assert.Single(defaultSignature.ReturnTypeReferences);
        LibraryBodyIndex bodyIndex =
            OpenWireContractBodyIndex(path);

        Assert.Equal(
            "int[]",
            Assert.Single(
                JsExportSurfaceBuilder.Build(
                    apiSurface,
                    bodyIndex).Functions)
                .ReturnWireType);
        context.DefinitionName = null;
        defaultSignature.ReturnTypeReferences =
        [
            authenticReturn with
            {
                DefinitionName = null,
            },
        ];

        UnsupportedJsExportSurfaceException exception =
            Assert.Throws<UnsupportedJsExportSurfaceException>(
                () => JsExportSurfaceBuilder.Build(
                    apiSurface,
                    bodyIndex));
        Assert.Contains(
            "no authentic default-instance getter",
            exception.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Build_RejectsCustomSerializerContextInstanceReceiver()
    {
        string path =
            typeof(ScalarContextOptionsFixtureExports).Assembly.Location;
#pragma warning disable CA1416
        Assert.Equal(
            "\"42\"",
            ScalarContextOptionsFixtureExports
                .SerializeCustomInstanceInt());
#pragma warning restore CA1416
        ApiSurface apiSurface = ExtractApiSurface(path);
        ApiType exports = Assert.Single(
            apiSurface.Types,
            type => type.Name
                == nameof(ScalarContextOptionsFixtureExports));
        foreach (ApiMember export in exports.Members.Where(
            member => member.HasRuntimeJsExport
                && member.Name
                    != nameof(
                        ScalarContextOptionsFixtureExports
                            .SerializeCustomInstanceInt)))
        {
            export.HasRuntimeJsExport = false;
            export.RuntimeJsExportAttributeCount = 0;
            export.HasMalformedRuntimeJsExportAttribute = false;
        }
        apiSurface.Types =
        [
            exports,
            Assert.Single(
                apiSurface.Types,
                type => type.Name
                    == nameof(SupportedScalarContextOptions)),
        ];

        UnsupportedJsExportSurfaceException exception =
            Assert.Throws<UnsupportedJsExportSurfaceException>(
                () => JsExportSurfaceBuilder.Build(
                    apiSurface,
                    OpenWireContractBodyIndex(path)));

        Assert.Contains(
            "receiver is not the authenticated default context",
            exception.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Build_RejectsIndexedGetterWithGeneratedRootName()
    {
        string sourcePath =
            typeof(IndexedRootExports).Assembly.Location;
        byte[] image = File.ReadAllBytes(sourcePath);
        Assert.Equal(
            2,
            ReplaceAscii(
                image,
                "Fake",
                "Root"));

        Assembly patchedAssembly = Assembly.Load(image);
        Type? patchedExports = patchedAssembly.GetType(
            typeof(IndexedRootExports).FullName!);
        Assert.NotNull(patchedExports);
        MethodInfo? serialize = patchedExports.GetMethod(
            nameof(IndexedRootExports.Serialize));
        Assert.NotNull(serialize);
        Assert.Equal(
            """{"Value":"42"}""",
            serialize.Invoke(null, null));

        string patchedPath = Path.Combine(
            Path.GetTempPath(),
            $"indexed-root-{Guid.NewGuid():N}.dll");
        try
        {
            File.WriteAllBytes(patchedPath, image);
            ApiSurface apiSurface =
                ExtractApiSurface(patchedPath);
            apiSurface.FilteredRuntimeJsExportFacts = [];
            apiSurface.Types =
            [
                .. apiSurface.Types.Where(type =>
                    type.Name is nameof(IndexedRootDto)
                        or nameof(IndexedRootJsonContext)
                        or nameof(IndexedRootExports)),
            ];
            ApiType context = Assert.Single(
                apiSurface.Types,
                type => type.Name
                    == nameof(IndexedRootJsonContext));
            Assert.Equal(
                [0, 1],
                context.Members
                    .Where(member => member.Name == "Root")
                    .Select(member =>
                        Assert.IsType<int>(
                            member.IndexParameterCount))
                    .Order()
                    .ToArray());

            UnsupportedJsExportSurfaceException exception =
                Assert.Throws<UnsupportedJsExportSurfaceException>(
                    () => JsExportSurfaceBuilder.Build(
                        apiSurface,
                        OpenWireContractBodyIndex(
                            patchedPath)));
            Assert.Contains(
                "not the parameterless generated getter",
                exception.Message,
                StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(patchedPath);
        }
    }

    [Fact]
    public void Build_RejectsDuplicateGeneratedRootPropertyIdentity()
    {
        string sourcePath =
            typeof(DuplicateRootExports).Assembly.Location;
#pragma warning disable CA1416
        Assert.Equal(
            """{"display_name":"probe"}""",
            DuplicateRootExports.Serialize());
#pragma warning restore CA1416
        byte[] image = File.ReadAllBytes(sourcePath);
        Assert.Equal(
            1,
            ReplaceAscii(
                image,
                "EvilRoot",
                "RealRoot"));

        string patchedPath = Path.Combine(
            Path.GetTempPath(),
            $"duplicate-root-{Guid.NewGuid():N}.dll");
        try
        {
            File.WriteAllBytes(patchedPath, image);
            ApiSurface apiSurface =
                ExtractApiSurface(patchedPath);
            apiSurface.FilteredRuntimeJsExportFacts = [];
            apiSurface.Types =
            [
                .. apiSurface.Types.Where(type =>
                    type.Name is nameof(DuplicateRootDto)
                        or nameof(DuplicateRootJsonContext)
                        or nameof(DuplicateRootExports)),
            ];
            ApiType context = Assert.Single(
                apiSurface.Types,
                type => type.Name
                    == nameof(DuplicateRootJsonContext));
            Assert.Equal(
                2,
                context.Members.Count(member =>
                    member.Kind == "property"
                    && member.Name == "RealRoot"));

            UnsupportedJsExportSurfaceException exception =
                Assert.Throws<UnsupportedJsExportSurfaceException>(
                    () => JsExportSurfaceBuilder.Build(
                        apiSurface,
                        OpenWireContractBodyIndex(
                            patchedPath)));
            Assert.Contains(
                "property identity is duplicated",
                exception.Message,
                StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(patchedPath);
        }
    }

    [Fact]
    public void Extract_AcceptsGeneralAndRejectsWebSerializerDefaults()
    {
        ApiSurface fixtureSurface = ExtractFixtureApiSurface();
        ApiType general = Assert.Single(
            fixtureSurface.Types,
            type => type.Name
                == nameof(PrimitiveRootFixtureJsonContext));
        ApiSurface scalarSurface = ExtractApiSurface(
            typeof(UnsupportedWebDefaultsContext).Assembly.Location);
        ApiType web = Assert.Single(
            scalarSurface.Types,
            type => type.Name
                == nameof(UnsupportedWebDefaultsContext));

        Assert.Equal(
            JsonWireNamingPolicy.None,
            general.JsonPropertyNamingPolicy);
        Assert.Equal(
            JsonWireNamingPolicy.Unsupported,
            web.JsonPropertyNamingPolicy);
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
        Assert.True(serializer.HasRuntimeJsExport);
        SelectOnlyRuntimeJsExport(apiSurface, serializer);

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
        Assert.True(serializer.HasRuntimeJsExport);
        SelectOnlyRuntimeJsExport(apiSurface, serializer);

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

    /// <summary>
    /// Gates <see cref="FieldIdentity"/> linking for the generated default
    /// instance: a second static write through a <c>MemberRef</c> alias names
    /// the same runtime field under a different metadata token, so token
    /// equality would count one write where there are two.
    /// </summary>
    [Fact]
    public void Build_RejectsAliasedSecondWriteToGeneratedDefaultInstanceField()
    {
        string path = typeof(FixtureExports).Assembly.Location;
        LibraryBodyIndex bodyIndex = OpenWireContractBodyIndex(path);
        FieldStoreFact instanceStore = Assert.Single(
            bodyIndex.FieldStores,
            store => store.IsStatic
                && store.FieldName == "<Default>k__BackingField"
                && store.DeclaringType?.Name == "FixtureJsonContext");
        Assert.NotNull(instanceStore.Identity);

        // The control: an unrelated static field under a fresh token is not
        // this field, so the surface still publishes.
        Assert.Contains(
            "Ping",
            BuildWith(
                path,
                bodyIndex,
                instanceStore with
                {
                    FieldToken = instanceStore.FieldToken + 0x100,
                    FieldName = "s_unrelatedInstance",
                    Identity = FieldIdentity.TryCreate(
                        instanceStore.DeclaringType,
                        "s_unrelatedInstance"),
                }).Functions.Select(function => function.Name));

        UnsupportedJsExportSurfaceException exception =
            Assert.Throws<UnsupportedJsExportSurfaceException>(
                () => BuildWith(
                    path,
                    bodyIndex,
                    instanceStore with
                    {
                        ILOffset = instanceStore.ILOffset + 0x1000,
                        FieldToken = instanceStore.FieldToken + 0x100,
                    }));
        Assert.Contains(
            "no authentic source-generated implementation",
            exception.Message,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// Gates the "might be this field" half of candidate selection: a static
    /// write carrying an identity that names this exact field but never
    /// canonicalized to its local definition is neither null nor equal, so
    /// selecting candidates by equality alone would silently drop it — and drop
    /// precisely the write with the least provenance behind it.
    /// </summary>
    [Fact]
    public void Build_RejectsUnprovenSecondStaticWriteNamingTheSameField()
    {
        string path = typeof(FixtureExports).Assembly.Location;
        LibraryBodyIndex bodyIndex = OpenWireContractBodyIndex(path);
        FieldStoreFact instanceStore = Assert.Single(
            bodyIndex.FieldStores,
            store => store.IsStatic
                && store.FieldName == "<Default>k__BackingField"
                && store.DeclaringType?.Name == "FixtureJsonContext");
        FieldIdentity? unproven = FieldIdentity.TryCreate(
            instanceStore.DeclaringType,
            instanceStore.FieldName);
        Assert.NotNull(unproven);
        Assert.Equal(0, unproven.LocalDefinitionToken);

        // The state that made this reachable: not equal to the authenticated
        // identity, and not null either.
        Assert.NotEqual(instanceStore.Identity, unproven);

        UnsupportedJsExportSurfaceException exception =
            Assert.Throws<UnsupportedJsExportSurfaceException>(
                () => BuildWith(
                    path,
                    bodyIndex,
                    instanceStore with
                    {
                        ILOffset = instanceStore.ILOffset + 0x1000,
                        FieldToken = instanceStore.FieldToken + 0x100,
                        Identity = unproven,
                    }));
        Assert.Contains(
            "no authentic source-generated implementation",
            exception.Message,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// Gates the fail-closed half of <see cref="FieldIdentity"/>: a static
    /// write whose own field could not be resolved might be a write to the
    /// authenticated field, and "might be" has to reject.
    /// </summary>
    [Fact]
    public void Build_RejectsUnidentifiedSecondStaticWrite()
    {
        string path = typeof(FixtureExports).Assembly.Location;
        LibraryBodyIndex bodyIndex = OpenWireContractBodyIndex(path);
        FieldStoreFact instanceStore = Assert.Single(
            bodyIndex.FieldStores,
            store => store.IsStatic
                && store.FieldName == "<Default>k__BackingField"
                && store.DeclaringType?.Name == "FixtureJsonContext");

        UnsupportedJsExportSurfaceException exception =
            Assert.Throws<UnsupportedJsExportSurfaceException>(
                () => BuildWith(
                    path,
                    bodyIndex,
                    instanceStore with
                    {
                        ILOffset = instanceStore.ILOffset + 0x1000,
                        FieldToken = instanceStore.FieldToken + 0x100,
                        DeclaringType = null,
                        FieldName = null,
                        Identity = null,
                    }));
        Assert.Contains(
            "no authentic source-generated implementation",
            exception.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Build_RejectsContextBaseConstructorCallThatCanBeSkipped()
    {
        string path = typeof(FixtureExports).Assembly.Location;
        LibraryBodyIndex bodyIndex = OpenWireContractBodyIndex(path);
        DirectCall baseCall = Assert.Single(
            bodyIndex.DirectCalls,
            call => call.EvidenceMethod.DeclaringType.Name
                    == "FixtureJsonContext"
                && call.EvidenceMethod.Name == ".ctor"
                && call.EvidenceMethod.ParameterTypes.Length == 1
                && call.Callee.DeclaringType.Name
                    == "JsonSerializerContext"
                && call.Callee.Name == ".ctor");
        Assert.True(baseCall.DominatesEveryNormalReturn);
        ImmutableArray<DirectCall> calls =
        [
            .. bodyIndex.DirectCalls.Select(call =>
                call == baseCall
                    ? call with
                    {
                        DominatesEveryNormalReturn = false,
                    }
                    : call),
        ];

        UnsupportedJsExportSurfaceException exception =
            Assert.Throws<UnsupportedJsExportSurfaceException>(
                () => JsExportSurfaceBuilder.Build(
                    ExtractApiSurface(path),
                    LibraryBodyIndex.FromEvidence(
                        bodyIndex.Methods,
                        [],
                        diagnostics: bodyIndex.Diagnostics,
                        directCalls: calls,
                        resultSinks: bodyIndex.ResultSinks,
                        fieldStores: bodyIndex.FieldStores,
                        fieldLoads: bodyIndex.FieldLoads,
                        returnFlows: bodyIndex.ReturnFlows)));

        Assert.Contains(
            "no authentic source-generated implementation",
            exception.Message,
            StringComparison.Ordinal);
    }
}
