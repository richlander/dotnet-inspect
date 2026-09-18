using System.Reflection.PortableExecutable;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using DotnetInspector.Fixtures;
using ILInspector.Analysis;
using ILInspector.JsExportSurface.UnionFixtures;
using ILInspector.Metadata;

namespace ILInspector.JsExportSurface.Tests;

public sealed class JsonUnionWireTests
{
    static readonly Lazy<LibraryBodyIndex> Bodies = new(() =>
        LibraryBodyIndex.Open(
            FixtureCatalog.AssemblyPath(FixtureIds.JsExportUnions),
            LibraryBodyAnalysisFeatures.MethodEvidence
                | LibraryBodyAnalysisFeatures.JsonWireContractFlow));

    [Fact]
    public void Build_RetainsScalarCasesAndDefaultNullRatherThanValueWrapper()
    {
        var surface = Build(nameof(UnionExports.GetScalar));
        JsExportUnion union = Find(surface, nameof(ScalarUnion));

        Assert.Null(union.SerializationUnsupportedReason);
        Assert.True(union.IncludesNull);
        Assert.Equal(["int", "string"], union.CaseTypes.Select(type => type.ToDisplayString()));
        Assert.DoesNotContain(surface.Records, type => type.HasUnionAttribute == true);
        Assert.Equal(JsonWireDirection.Serialize, surface.WireDirections[union.Definition]);
        Assert.Equal(JsonTypeInfoKind.Union, UnionJsonContext.Default.ScalarUnion.Kind);
        Assert.Equal("42", UnionExports.GetScalar(0));
        Assert.Equal("\"hello\"", UnionExports.GetScalar(1));
        Assert.Equal("null", UnionExports.GetScalar(2));
    }

    [Fact]
    public void Build_FollowsDtoCasesWithTheirContextNamingAndDirection()
    {
        var surface = Build(nameof(UnionExports.GetDto));
        JsExportUnion union = Find(surface, nameof(DtoUnion));
        ApiType dto = Assert.Single(surface.Records, type => type.Name == nameof(PackageSummary));

        Assert.Equal(
            typeof(PackageSummary).FullName,
            union.CaseTypes[0].Resolution?.Type?.ToMetadataFullName());
        Assert.Equal(JsonWireNamingPolicy.CamelCase, dto.JsonPropertyNamingPolicy);
        Assert.Equal(JsonWireDirection.Serialize, surface.WireDirections[dto]);
        Assert.Equal("{\"id\":\"Example.Package\"}", UnionExports.GetDto());
        Assert.NotEmpty(Assert.Single(surface.Functions).ReturnWireContextScopeKeys);
    }

    [Fact]
    public void Build_PreservesGenericCasePositionsAndClosedRootArguments()
    {
        var surface = Build(nameof(UnionExports.GetInspectionResult));
        JsExportUnion union = Assert.Single(
            surface.Unions,
            item => item.Definition.Name.StartsWith(
                "InspectionResult",
                StringComparison.Ordinal));
        Assert.Equal(TypeRefKind.GenericParameter, union.CaseTypes[0].Kind);
        Assert.Equal(0, union.CaseTypes[0].GenericParameterIndex);
        ApiTypeShape root = Assert.IsType<ApiTypeShape>(
            Assert.Single(surface.Functions).ReturnWireTypeShape);
        Assert.Equal(ApiPrimitiveType.Int32, Assert.Single(root.TypeArguments).Primitive);
        Assert.Equal(
            typeof(int),
            UnionJsonContext.Default.InspectionResultInt32.UnionCases[0].CaseType);
        Assert.Equal("7", UnionExports.GetInspectionResult());
    }

    [Fact]
    public void Build_PreservesNullableValueCasesSeparatelyFromDefaultNull()
    {
        var surface = Build(nameof(UnionExports.GetNullable));
        JsExportUnion union = Find(surface, nameof(NullableUnion));
        Assert.Null(union.SerializationUnsupportedReason);
        Assert.Equal(TypeRefKind.GenericInstance, union.CaseTypes[0].Kind);
        Assert.Equal("Nullable`1", union.CaseTypes[0].ElementType?.Name);
        Assert.True(union.IncludesNull);
        Assert.Equal("42", UnionExports.GetNullable());
        Assert.Equal("null", JsonSerializer.Serialize(
            new NullableUnion((int?)null), UnionJsonContext.Default.NullableUnion));
    }

    [Fact]
    public void Build_FollowsNestedAndCollectionUnionEdges()
    {
        var nested = Build(nameof(UnionExports.GetNested));
        Assert.Equal(JsonWireDirection.Serialize,
            nested.WireDirections[Find(nested, nameof(ScalarUnion)).Definition]);
        Assert.Equal("42", UnionExports.GetNested());
        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize("42", UnionJsonContext.Default.NestedUnion));

        var envelope = Build(nameof(UnionExports.GetEnvelope));
        Assert.Equal(JsonWireDirection.Serialize,
            envelope.WireDirections[Find(envelope, nameof(ScalarUnion)).Definition]);
        Assert.Equal(JsonWireDirection.Serialize,
            envelope.WireDirections[Find(envelope, nameof(DtoUnion)).Definition]);
        Assert.Equal(
            "{\"result\":\"missing\",\"items\":[7,\"ok\",null]}",
            UnionExports.GetEnvelope());
    }

    [Fact]
    public void Build_DoesNotPromoteWritingEvidenceToReadClassification()
    {
        var surface = Build(nameof(UnionExports.ReadPackageResolution));
        JsExportUnion union = Find(surface, nameof(PackageResolutionResult));
        Assert.Null(union.SerializationUnsupportedReason);
        Assert.NotEmpty(union.DeserializationUnsupportedReason);
        Assert.Equal(JsonWireDirection.Deserialize, surface.WireDirections[union.Definition]);
        Assert.Equal("{\"code\":404}", UnionExports.GetPackageResolution());
        Assert.Throws<JsonException>(() =>
            UnionExports.ReadPackageResolution("{\"code\":404}"));
        Assert.Equal("1.5", UnionExports.GetMetricValue());
        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize(
                "1.5",
                UnionJsonContext.Default.MetricValue));
    }

    [Fact]
    public void Build_TracksBothDirectionsWithoutClaimingReaderSupport()
    {
        var surface = Build(nameof(UnionExports.GetScalar), nameof(UnionExports.ReadScalar));
        JsExportUnion union = Find(surface, nameof(ScalarUnion));
        Assert.Equal(JsonWireDirection.Both, surface.WireDirections[union.Definition]);
        Assert.NotEmpty(union.DeserializationUnsupportedReason);
        UnionExports.ReadScalar("42");
    }

    [Fact]
    public void Build_RetainsUnsupportedCustomUnionConverter()
    {
        var surface = Build(nameof(UnionExports.GetCustom));
        JsExportUnion union = Find(surface, nameof(CustomUnion));
        Assert.NotNull(union.SerializationUnsupportedReason);
        Assert.Null(union.IncludesNull);
        Assert.Empty(union.CaseTypes);
        Assert.Equal("\"custom:42\"", UnionExports.GetCustom());
    }

    [Fact]
    public void Build_DeclarationOnlyDoesNotInventSignatureEvidence()
    {
        var surface = JsExportSurfaceBuilder.Build(Extract(nameof(UnionExports.GetScalar)));
        JsExportUnion union = Find(surface, nameof(ScalarUnion));
        Assert.NotNull(union.SerializationUnsupportedReason);
        Assert.Null(union.IncludesNull);
    }

    [Fact]
    public void Build_DoesNotDropAnUnreadableConstructorFromTheCaseSet()
    {
        ApiSurface api = Extract(nameof(UnionExports.GetScalar));
        ApiType type = Assert.Single(api.Types, type => type.Name == nameof(ScalarUnion));
        ApiMember constructor = type.Members.First(member => member.Kind == "constructor");
        constructor.SignatureDecodeStatus = SignatureDecodeStatus.Degraded;
        constructor.SignatureModel = null;

        var surface = JsExportSurfaceBuilder.Build(api, Bodies.Value);
        JsExportUnion union = Find(surface, nameof(ScalarUnion));
        Assert.NotNull(union.SerializationUnsupportedReason);
        Assert.Empty(union.CaseTypes);
        Assert.Null(union.IncludesNull);
    }

    [Theory]
    [InlineData(nameof(UnionExports.GetOrdinary))]
    [InlineData(nameof(UnionExports.GetPlain))]
    public void Emit_UnreachedUnionsRemainInert(string method)
    {
        var surface = Build(method);
        Assert.All(surface.Unions, union =>
            Assert.Equal(JsonWireDirection.None, surface.WireDirections[union.Definition]));
        var diagnostics = new TypeScriptGenerationDiagnostics();
        string output = DtsEmitter.Emit(surface, diagnostics);
        Assert.False(diagnostics.HasUnmappedTypes);
        Assert.DoesNotContain("ScalarUnion", output, StringComparison.Ordinal);
        if (method == nameof(UnionExports.GetOrdinary))
        {
            Assert.Contains("interface OrdinaryValue", output, StringComparison.Ordinal);
            Assert.Equal("{\"value\":42}", UnionExports.GetOrdinary());
        }
    }

    [Fact]
    public void Emit_RetainsScalarUnionAliasAndRawFacadeReturn()
    {
        var surface = Build(nameof(UnionExports.GetScalar));
        string declaration = DtsEmitter.Emit(surface);
        Assert.Contains("export type ScalarUnion = number | string | null;", declaration, StringComparison.Ordinal);
        Assert.Contains("getScalar(choice: number): ScalarUnion", declaration, StringComparison.Ordinal);
        Assert.DoesNotContain("interface ScalarUnion", declaration, StringComparison.Ordinal);
        string facade = TypeScriptFacadeEmitter.Emit(surface, "./dotnet.js");
        Assert.Contains("export type ScalarUnion = number | string | null;", facade, StringComparison.Ordinal);
        Assert.Contains("getScalar(choice: number): ScalarUnion", facade, StringComparison.Ordinal);
        Assert.Contains("=> string;", facade, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(nameof(UnionExports.GetDto), "export type DtoUnion = PackageSummary | string | null;")]
    [InlineData(nameof(UnionExports.GetNullable), "export type NullableUnion = number | string | null;")]
    [InlineData(
        nameof(UnionExports.GetInspectionResult),
        "export type InspectionResult<T0> = T0 | string | null;")]
    [InlineData(nameof(UnionExports.GetNested), "export type NestedUnion = ScalarUnion | boolean | null;")]
    [InlineData(
        nameof(UnionExports.GetPackageResolution),
        "export type PackageResolutionResult = PackageSummary | PackageProblem | null;")]
    [InlineData(
        nameof(UnionExports.GetMetricValue),
        "export type MetricValue = number | null;")]
    [InlineData(nameof(UnionExports.GetEnvelope), "readonly items: ReadonlyArray<ScalarUnion>;")]
    public void Emit_PreservesRepresentedCaseKinds(string method, string expected)
    {
        var surface = Build(method);
        var diagnostics = new TypeScriptGenerationDiagnostics();
        Assert.Contains(expected, DtsEmitter.Emit(surface, diagnostics), StringComparison.Ordinal);
        Assert.False(diagnostics.HasUnmappedTypes);
        Assert.Contains(expected, TypeScriptFacadeEmitter.Emit(surface, "./dotnet.js"), StringComparison.Ordinal);
        if (method == nameof(UnionExports.GetInspectionResult))
        {
            Assert.Contains(
                "getInspectionResult(): InspectionResult<number>",
                DtsEmitter.Emit(surface),
                StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData(nameof(UnionExports.ReadScalar), "deserialization")]
    [InlineData(nameof(UnionExports.ReadPackageResolution), "deserialization")]
    [InlineData(nameof(UnionExports.GetCustom), "unsupported wire-shaping attributes")]
    public void Emit_UnsupportedUnionsStillFailBeforePublication(string method, string reason)
    {
        var surface = Build(method);
        var exception = Assert.Throws<UnsupportedWireContractException>(
            () => DtsEmitter.Emit(surface));
        Assert.Contains(reason, exception.Message, StringComparison.Ordinal);
        Assert.Throws<UnsupportedWireContractException>(
            () => TypeScriptFacadeEmitter.Emit(surface, "./dotnet.js"));
    }

    [Fact]
    public void Emit_MapsPackagePayloadAndInspectionArgumentsToTheirWireTypes()
    {
        string bytes = DtsEmitter.Emit(
            Build(nameof(UnionExports.GetPackageReadBytes)));
        Assert.Contains(
            "getPackageReadBytes(): PackageReadResult<string>",
            bytes,
            StringComparison.Ordinal);
        Assert.Equal("\"AQID\"", UnionExports.GetPackageReadBytes());

        string dictionary = DtsEmitter.Emit(
            Build(nameof(UnionExports.GetInspectionDictionary)));
        Assert.Contains(
            "InspectionResult<Readonly<Record<string, number | null>>>",
            dictionary,
            StringComparison.Ordinal);
        Assert.Equal(
            "{\"value\":42,\"empty\":null}",
            UnionExports.GetInspectionDictionary());
    }

    [Fact]
    public void Emit_RejectsItemsOrCountWhenArrayWireShapeDependsOnT()
    {
        Assert.Equal("\"AQID\"", UnionExports.GetItemsOrCountBytes());
        Assert.Equal("[1,2,3]", UnionExports.GetItemsOrCountNumbers());
        foreach (string method in new[]
        {
            nameof(UnionExports.GetItemsOrCountBytes),
            nameof(UnionExports.GetItemsOrCountNumbers),
        })
        {
            var surface = Build(method);
            var exception = Assert.Throws<UnsupportedWireContractException>(() => DtsEmitter.Emit(surface));
            Assert.Contains("generic parameters embedded", exception.Message, StringComparison.Ordinal);
            Assert.Throws<UnsupportedWireContractException>(() => TypeScriptFacadeEmitter.Emit(surface, "./dotnet.js"));
        }
    }

    [Fact]
    public void Emit_DoesNotPublishGenericRecordParameterArrays()
    {
        Assert.Equal(
            "{\"items\":\"AQID\"}",
            UnionExports.GetNonParametricArrayRecord());
        var surface = Build(
            nameof(UnionExports.GetNonParametricArrayRecord));
        var exception = Assert.Throws<UnsupportedWireContractException>(
            () => DtsEmitter.Emit(surface));
        Assert.Contains(
            "array whose JSON mapping is not parametric",
            exception.Message,
            StringComparison.Ordinal);
        Assert.Throws<UnsupportedWireContractException>(
            () => TypeScriptFacadeEmitter.Emit(surface, "./dotnet.js"));
    }

    [Fact]
    public void Emit_DoesNotPublishNestedGenericRecordParameterArrays()
    {
        Assert.Equal(
            "{\"items\":{\"value\":\"AQID\"}}",
            UnionExports.GetNonParametricNestedArrayRecord());
        var surface = Build(
            nameof(UnionExports.GetNonParametricNestedArrayRecord));
        var exception = Assert.Throws<UnsupportedWireContractException>(
            () => DtsEmitter.Emit(surface));
        Assert.Contains(
            "array whose JSON mapping is not parametric",
            exception.Message,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(
        nameof(UnionExports.GetNonParametricAnnotatedArrayRecord),
        "{\"items\":\"AQID\"}")]
    [InlineData(
        nameof(UnionExports.GetNonParametricNestedAnnotatedArrayRecord),
        "{\"items\":{\"value\":\"AQID\"}}")]
    public void Emit_DoesNotPublishAnnotatedGenericRecordParameterArrays(
        string method,
        string expectedPayload)
    {
        string payload = method switch
        {
            nameof(UnionExports.GetNonParametricAnnotatedArrayRecord) =>
                UnionExports.GetNonParametricAnnotatedArrayRecord(),
            _ => UnionExports.GetNonParametricNestedAnnotatedArrayRecord(),
        };
        Assert.Equal(expectedPayload, payload);
        var exception = Assert.Throws<UnsupportedWireContractException>(
            () => DtsEmitter.Emit(Build(method)));
        Assert.Contains(
            "array whose JSON mapping is not parametric",
            exception.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Emit_PreservesParametricNullableValueArrays()
    {
        Assert.Equal(
            "{\"items\":[1,null]}",
            UnionExports.GetParametricNullableValueArrayRecord());
        string declaration = DtsEmitter.Emit(
            Build(nameof(
                UnionExports.GetParametricNullableValueArrayRecord)));
        Assert.Contains(
            "readonly items: ReadonlyArray<T0 | null>;",
            declaration,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Emit_RejectsConditionalGenericRecordParameter()
    {
        Assert.Equal(
            """{"payload":{"value":1}}""",
            UnionExports.GetConditionalGenericRecord());

        var surface = Build(
            nameof(UnionExports.GetConditionalGenericRecord));
        UnsupportedWireContractException exception =
            Assert.Throws<UnsupportedWireContractException>(
                () => DtsEmitter.Emit(surface));

        Assert.Contains(
            "conditional generic record parameter",
            exception.Message,
            StringComparison.Ordinal);
        Assert.Throws<UnsupportedWireContractException>(
            () => TypeScriptFacadeEmitter.Emit(
                surface,
                "./dotnet.js"));
    }

    [Theory]
    [InlineData(
        nameof(UnionExports.GetConditionalInspectionValue),
        "{\"payload\":{\"value\":1}}")]
    [InlineData(
        nameof(UnionExports.GetConditionalRawInspectionResult),
        "{\"payload\":{\"value\":1}}")]
    [InlineData(
        nameof(UnionExports.GetConditionalNestedRawInspectionResult),
        "{\"payload\":{\"value\":1}}")]
    [InlineData(
        nameof(UnionExports.GetConditionalOpenRawInspectionResult),
        "{\"payload\":{\"value\":1}}")]
    public void Emit_RejectsConditionalUnionThatCanCollapseToUnknown(
        string method,
        string expectedPayload)
    {
        string payload = method switch
        {
            nameof(UnionExports.GetConditionalInspectionValue) =>
                UnionExports.GetConditionalInspectionValue(),
            nameof(UnionExports.GetConditionalRawInspectionResult) =>
                UnionExports.GetConditionalRawInspectionResult(),
            nameof(UnionExports.GetConditionalNestedRawInspectionResult) =>
                UnionExports.GetConditionalNestedRawInspectionResult(),
            _ => UnionExports.GetConditionalOpenRawInspectionResult(),
        };
        Assert.Equal(expectedPayload, payload);

        var surface = Build(method);
        UnsupportedWireContractException exception =
            Assert.Throws<UnsupportedWireContractException>(
                () => DtsEmitter.Emit(surface));

        Assert.Contains(
            "conditional union present-value type can collapse to unknown",
            exception.Message,
            StringComparison.Ordinal);
        Assert.Throws<UnsupportedWireContractException>(
            () => TypeScriptFacadeEmitter.Emit(
                surface,
                "./dotnet.js"));
    }

    [Fact]
    public void Emit_AllowsConditionalInspectionRowsBecauseUnknownStaysNested()
    {
        Assert.Equal(
            """{"payload":[{"value":1}]}""",
            UnionExports.GetConditionalInspectionRows());

        var surface = Build(
            nameof(UnionExports.GetConditionalInspectionRows));
        string declaration = DtsEmitter.Emit(surface);

        Assert.Contains(
            "export type InspectionRowsOrCount = "
                + "ReadonlyArray<unknown> | number | null;",
            declaration,
            StringComparison.Ordinal);
        Assert.Contains(
            "readonly payload?: InspectionRowsOrCount;",
            declaration,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Emit_DoesNotConfuseConcreteArrayTypesWithGenericParameters()
    {
        Assert.Equal(
            "{\"value\":7,\"items\":[{\"value\":8}]}",
            UnionExports.GetConcreteArrayRecord());
        string declaration = DtsEmitter.Emit(
            Build(nameof(UnionExports.GetConcreteArrayRecord)));
        Assert.Contains(
            "readonly items: ReadonlyArray<T0>;",
            declaration,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Emit_DoesNotConfuseGlobalConcreteArraysWithGenericParameters()
    {
        Assert.Equal(
            "{\"value\":7,\"items\":[{\"value\":8}]}",
            UnionExports.GetGlobalConcreteArrayRecord());
        string declaration = DtsEmitter.Emit(
            Build(nameof(UnionExports.GetGlobalConcreteArrayRecord)));
        Assert.Contains(
            "export interface GlobalConcreteArrayRecord<",
            declaration,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "readonly items: unknown;",
            declaration,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Emit_GenericParametersDoNotShadowCaseDeclarations()
    {
        string output = DtsEmitter.Emit(Build(nameof(UnionExports.GetParameterNameUnion)));
        Assert.Contains("export type ParameterNameUnion<T0_> = T0_ | T0 | null;", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Emit_UnionNamesParticipateInFacadeAllocation()
    {
        string output = TypeScriptFacadeEmitter.Emit(Build(nameof(UnionExports.GetReservedUnionName)), "./dotnet.js");
        string alias = output.Split('\n').Single(line => line.StartsWith("export type type_", StringComparison.Ordinal))
            .Split(' ')[2];
        Assert.Contains($"getReservedUnionName(): {alias}", output, StringComparison.Ordinal);
        Assert.Contains("export function initializeRuntime(", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Emit_UnmappedCaseDoesNotBecomeAnUnknownAlternative()
    {
        var surface = Build(nameof(UnionExports.GetUnsupportedCase));
        Assert.Throws<UnsupportedWireContractException>(() => DtsEmitter.Emit(surface));
        Assert.Throws<UnsupportedWireContractException>(() => TypeScriptFacadeEmitter.Emit(surface, "./dotnet.js"));
    }

    [Fact]
    public void Emit_RecursiveUnionAliasesFailBeforeTypeScriptPublication()
    {
        Assert.Equal("null", UnionExports.GetRecursiveUnion());
        var surface = Build(nameof(UnionExports.GetRecursiveUnion));
        var exception = Assert.Throws<UnsupportedWireContractException>(() => DtsEmitter.Emit(surface));
        Assert.Contains("recursive union case aliases", exception.Message, StringComparison.Ordinal);
        Assert.Throws<UnsupportedWireContractException>(() => TypeScriptFacadeEmitter.Emit(surface, "./dotnet.js"));
    }

    [Fact]
    public void Emit_ReferenceCollectionEntriesRetainPossibleNulls()
    {
        Assert.Equal("[\"value\",null]", UnionExports.GetReferenceArrayUnion());
        Assert.Equal(
            "[\"value\",null]",
            UnionExports.GetInspectionReferenceArray());
        string direct = DtsEmitter.Emit(Build(nameof(UnionExports.GetReferenceArrayUnion)));
        Assert.Contains("ReadonlyArray<string | null> | number | null", direct, StringComparison.Ordinal);
        string generic = DtsEmitter.Emit(
            Build(nameof(UnionExports.GetInspectionReferenceArray)));
        Assert.Contains(
            "InspectionResult<ReadonlyArray<string | null>>",
            generic,
            StringComparison.Ordinal);
    }

    static JsExportUnion Find(JsExportSurface surface, string name) =>
        Assert.Single(surface.Unions, union => union.Definition.Name == name);

    static JsExportSurface Build(params string[] methods) =>
        JsExportSurfaceBuilder.Build(Extract(methods), Bodies.Value);

    static ApiSurface Extract(params string[] methods)
    {
        using var stream = File.OpenRead(FixtureCatalog.AssemblyPath(FixtureIds.JsExportUnions));
        using var pe = new PEReader(stream);
        ApiSurface surface = ApiSurfaceExtractor.Extract(pe, includeAll: true);
        foreach (ApiMember member in surface.Types.SelectMany(type => type.Members))
        {
            if (member.HasRuntimeJsExport && !methods.Contains(member.Name, StringComparer.Ordinal))
            {
                member.HasRuntimeJsExport = false;
                member.RuntimeJsExportAttributeCount = 0;
                member.HasMalformedRuntimeJsExportAttribute = false;
            }
        }
        return surface;
    }
}
