using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Text.Json;
using ILInspector.Analysis;
using ILInspector.JsExportSurface.Fixtures;
using ILInspector.Instructions;
using ILInspector.Metadata;

namespace ILInspector.JsExportSurface.Tests;

/// <summary>
/// Verifies <see cref="JsonWireContractResolver"/> and its wiring into
/// <see cref="JsExportSurfaceBuilder.Build"/> against
/// <see cref="ILInspector.JsExportSurface.Fixtures.FixtureExports"/>: proves each
/// <c>[JSExport]</c> export's actual DTO is resolved from its own body's
/// <c>JsonSerializer.Serialize</c> call site, not inferred from the assembly's whole registered
/// shape vocabulary.
/// </summary>
public sealed class JsonWireContractResolverTests
{
    private const string FixtureNamespace =
        "ILInspector.JsExportSurface.Fixtures.";
    private static readonly string s_runtimeAsyncFixturePath =
        Path.Combine(
            AppContext.BaseDirectory,
            "ILInspector.JsExportSurface.RuntimeAsyncFixtures.dll");

    [Fact]
    public void WireTypesEqual_DistinguishesCompleteAssemblyIdentity()
    {
        TypeRef first = ScopedType("0011223344556677");
        TypeRef equivalent = ScopedType("0011223344556677");
        TypeRef different = ScopedType("8899aabbccddeeff");

        Assert.True(
            JsonWireContractResolver.WireTypesEqual(
                first,
                equivalent));
        Assert.False(
            JsonWireContractResolver.WireTypesEqual(
                first,
                different));
    }

    [Fact]
    public void SerializerIdentityRequiresSignedSystemTextJsonAssembly()
    {
        TypeRef trusted = ExternalType(
            "System.Text.Json",
            "System.Text.Json",
            "JsonSerializer",
            "cc7b13ffcd2ddd51");
        TypeRef unsigned = ExternalType(
            "System.Text.Json",
            "System.Text.Json",
            "JsonSerializer",
            publicKeyToken: null);
        TypeRef lookalike = ExternalType(
            "Lookalikes",
            "System.Text.Json",
            "JsonSerializer",
            "cc7b13ffcd2ddd51");

        Assert.True(
            JsonWireContractResolver.IsTrustedJsonSerializerType(
                trusted));
        Assert.False(
            JsonWireContractResolver.IsTrustedJsonSerializerType(
                unsigned));
        Assert.False(
            JsonWireContractResolver.IsTrustedJsonSerializerType(
                lookalike));
    }

    [Fact]
    public void SourceGeneratedSerializerOverloadsRequireExactGenericShapes()
    {
        TypeRef dto = ExternalType(
            "Fixture",
            "Fixtures",
            "WidgetDto",
            "0011223344556677");
        TypeRef otherDto = ExternalType(
            "Fixture",
            "Fixtures",
            "OtherDto",
            "0011223344556677");
        MemberRef serialize = SourceGeneratedSerialize(dto);
        MemberRef deserialize = SourceGeneratedDeserialize(dto);

        Assert.True(JsonWireContractResolver.WireTypesEqual(
            dto,
            Assert.IsType<TypeRef>(
                JsonWireContractResolver.ResolveSerializeDto(
                    serialize))));
        Assert.True(JsonWireContractResolver.WireTypesEqual(
            dto,
            Assert.IsType<TypeRef>(
                JsonWireContractResolver.ResolveDeserializeDto(
                    deserialize))));

        MemberRef[] forgedSerializeShapes =
        [
            serialize with
            {
                ParameterTypes = [JsonTypeInfo(dto), dto],
            },
            serialize with
            {
                ParameterTypes = [dto],
            },
            serialize with
            {
                ParameterTypes = [otherDto, JsonTypeInfo(dto)],
            },
            serialize with
            {
                ParameterTypes = [dto, JsonTypeInfo(otherDto)],
            },
            serialize with
            {
                TypeArguments = [otherDto],
            },
            serialize with
            {
                ReturnType = dto,
            },
            serialize with
            {
                GenericArity = 0,
                TypeArguments = [],
                SignatureHeader = 0,
            },
            serialize with
            {
                OpenParameterTypes = [dto, JsonTypeInfo(dto)],
            },
            serialize with
            {
                HasThis = true,
                SignatureHeader = 0x30,
            },
            MemberRef.Unsupported("unresolved serializer signature"),
        ];
        Assert.All(
            forgedSerializeShapes,
            shape => Assert.Null(
                JsonWireContractResolver.ResolveSerializeDto(shape)));

        MemberRef[] forgedDeserializeShapes =
        [
            deserialize with
            {
                ParameterTypes =
                [
                    ExternalType(
                        "System.Text.Json",
                        "System",
                        "String",
                        "cc7b13ffcd2ddd51"),
                    JsonTypeInfo(dto),
                ],
            },
            deserialize with
            {
                ParameterTypes =
                [
                    NestedSystemStringFromCore(),
                    JsonTypeInfo(dto),
                ],
            },
            deserialize with
            {
                ParameterTypes = [JsonTypeInfo(dto), SystemString()],
            },
            deserialize with
            {
                ParameterTypes = [SystemString()],
            },
            deserialize with
            {
                ParameterTypes = [SystemString(), JsonTypeInfo(otherDto)],
            },
            deserialize with
            {
                ReturnType = otherDto,
            },
            deserialize with
            {
                GenericArity = 0,
                TypeArguments = [],
                SignatureHeader = 0,
            },
            deserialize with
            {
                OpenReturnType = dto,
            },
        ];
        Assert.All(
            forgedDeserializeShapes,
            shape => Assert.Null(
                JsonWireContractResolver.ResolveDeserializeDto(shape)));
    }

    static TypeRef ScopedType(string publicKeyToken)
        => ExternalType(
            "Shared",
            "Mine",
            "Result",
            publicKeyToken);

    static TypeRef ExternalType(
        string assemblyName,
        string @namespace,
        string nameValue,
        string? publicKeyToken)
    {
        var name = Assert.IsType<
            MetadataTypeDefinitionNameResult.Valid>(
            MetadataTypeDefinitionName.Create(
                @namespace,
                ImmutableArray.Create(nameValue)))
            .Name;
        var assembly = new AssemblyReferenceIdentity(
            assemblyName,
            new Version(1, 0, 0, 0),
            null,
            publicKeyToken);
        return TypeRef.Definition(
            assemblyName,
            @namespace,
            nameValue,
            new ResolvableTypeReference(
                new TypeReferenceOrigin.AssemblyReference(
                    assembly),
                name));
    }

    static TypeRef SystemString()
        => ExternalType(
            "System.Private.CoreLib",
            "System",
            "String",
            "7cec85d7bea7798e");

    static TypeRef NestedSystemStringFromCore()
    {
        var name = Assert.IsType<
            MetadataTypeDefinitionNameResult.Valid>(
            MetadataTypeDefinitionName.Create(
                "",
                ImmutableArray.Create("System", "String")))
            .Name;
        var assembly = new AssemblyReferenceIdentity(
            "System.Private.CoreLib",
            new Version(1, 0, 0, 0),
            null,
            "7cec85d7bea7798e");
        return TypeRef.Definition(
            "System.Private.CoreLib",
            "System",
            "String",
            new ResolvableTypeReference(
                new TypeReferenceOrigin.AssemblyReference(
                    assembly),
                name));
    }

    static TypeRef JsonSerializer()
        => ExternalType(
            "System.Text.Json",
            "System.Text.Json",
            "JsonSerializer",
            "cc7b13ffcd2ddd51");

    static TypeRef JsonTypeInfo(TypeRef dto)
        => TypeRef.GenericInstance(
            ExternalType(
                "System.Text.Json",
                "System.Text.Json.Serialization.Metadata",
                "JsonTypeInfo`1",
                "cc7b13ffcd2ddd51"),
            [dto]);

    static TypeRef TaskOfString(
        string assemblyName = "System.Runtime")
        => TypeRef.GenericInstance(
            ExternalType(
                assemblyName,
                "System.Threading.Tasks",
                "Task`1",
                "b03f5f7f11d50a3a"),
            [SystemString()]);

    static MemberRef SourceGeneratedSerialize(TypeRef dto)
    {
        TypeRef typeParameter = TypeRef.MethodGenericParameter(0, "TValue");
        return new(
            JsonSerializer(),
            "Serialize",
            [dto, JsonTypeInfo(dto)],
            SystemString(),
            MemberKind.Method)
        {
                TypeArguments = [dto],
                GenericArity = 1,
                SignatureHeader = 0x10,
                OpenParameterTypes =
                [
                    typeParameter,
                    JsonTypeInfo(typeParameter),
                ],
                OpenReturnType = SystemString(),
        };
    }

    static MemberRef SourceGeneratedDeserialize(TypeRef dto)
    {
        TypeRef typeParameter = TypeRef.MethodGenericParameter(0, "TValue");
        return new(
            JsonSerializer(),
            "Deserialize",
            [SystemString(), JsonTypeInfo(dto)],
            dto,
            MemberKind.Method)
        {
                TypeArguments = [dto],
                GenericArity = 1,
                SignatureHeader = 0x10,
                OpenParameterTypes =
                [
                    SystemString(),
                    JsonTypeInfo(typeParameter),
                ],
                OpenReturnType = typeParameter,
        };
    }

    private static ILInspector.JsExportSurface.JsExportSurface BuildFixtureSurfaceWithWireContracts()
        => BuildFixtureSurfaceWithWireContracts(
            typeof(FixtureExports).Assembly.Location)
            .Surface;

    private static (
        ILInspector.JsExportSurface.JsExportSurface Surface,
        LibraryBodyIndex BodyIndex)
        BuildFixtureSurfaceWithWireContracts(string path)
    {
        using FileStream stream = File.OpenRead(path);
        using var peReader = new PEReader(stream);
        ApiSurface apiSurface = ApiSurfaceExtractor.Extract(peReader, includeAll: false);
        var bodyIndex = LibraryBodyIndex.Open(
            path,
            LibraryBodyAnalysisFeatures.MethodEvidence
                | LibraryBodyAnalysisFeatures.JsonWireContractFlow);
        return (
            JsExportSurfaceBuilder.Build(apiSurface, bodyIndex),
            bodyIndex);
    }

    [Fact]
    public void Build_ResolvesReturnWireTypeForSyncExport()
    {
        ILInspector.JsExportSurface.JsExportSurface surface = BuildFixtureSurfaceWithWireContracts();

        JsExportFunction getWidget = Assert.Single(
            surface.Functions,
            f => f.Name == "GetWidget");
        Assert.Equal(
            FixtureNamespace + "WidgetDto",
            getWidget.ReturnWireType);
        Assert.Equal(
            [
                new ApiTypeReferenceIdentity(
                    surface.AssemblyIdentity!,
                    FixtureNamespace + "WidgetDto",
                    Assert.Single(
                        surface.Records,
                        type => type.Name == "WidgetDto")
                        .DefinitionName),
            ],
            getWidget.ReturnWireTypeReferences);
        Assert.Empty(getWidget.ParameterWireTypes);
    }

    [Fact]
    public void Build_ResolvesReturnWireTypeForAsyncExport()
    {
        // GetWidgetAsync's JsonSerializer.Serialize call is physically emitted in the compiler
        // generated state machine's MoveNext body, not GetWidgetAsync's own body. This only
        // resolves correctly because DirectCall.Caller is already attributed to the declared
        // async method (see PR #4461 / issue #4459) rather than to MoveNext.
        ILInspector.JsExportSurface.JsExportSurface surface = BuildFixtureSurfaceWithWireContracts();

        JsExportFunction getWidgetAsync = Assert.Single(
            surface.Functions,
            f => f.Name == "GetWidgetAsync");
        Assert.Equal(
            FixtureNamespace + "WidgetDto",
            getWidgetAsync.ReturnWireType);
        Assert.Empty(getWidgetAsync.ParameterWireTypes);
    }

    [Fact]
    public void Build_ResolvesRegisteredStringArrayAfterAwait()
    {
        var bodyIndex = LibraryBodyIndex.Open(
            typeof(FixtureExports).Assembly.Location,
            LibraryBodyAnalysisFeatures.MethodEvidence
                | LibraryBodyAnalysisFeatures.JsonWireContractFlow);
        MethodIdentity export = Assert.Single(
            bodyIndex.DeclaredMethods,
            method => method.Name == "GetStringArrayAsyncAfterAwait");
        DirectCall serializer = Assert.Single(
            bodyIndex.DirectCalls,
            call => call.Caller == export
                && call.Callee.Name == "Serialize");
        Assert.Equal(
            "string[]",
            JsonWireContractResolver.ResolveSerializeDto(
                serializer.Callee)?.ToQualifiedDisplayString());
        CallArgumentSource typeInfoArgument = Assert.Single(
            serializer.ArgumentSources,
            source => source.ArgumentIndex == 1);
        Assert.True(typeInfoArgument.IsComplete);
        int typeInfoGetterOffset = Assert.Single(
            typeInfoArgument.SourceCallOffsets);
        DirectCall typeInfoGetter = Assert.Single(
            bodyIndex.DirectCalls,
            call => call.EvidenceMethod == serializer.EvidenceMethod
                && call.ILOffset == typeInfoGetterOffset);
        Assert.Equal("get_StringArray", typeInfoGetter.Callee.Name);

        MethodResultSink resultSink = Assert.Single(
            bodyIndex.ResultSinks,
            sink => sink.Caller == export
                && sink.Kind == MethodResultSinkKind.SingleArgumentCall
                && sink.SourceCallOffsets.Contains(serializer.ILOffset));
        Assert.True(resultSink.IsComplete);
        Assert.Equal("MoveNext", resultSink.EvidenceMethod.Name);
        Assert.NotEqual(resultSink.Caller, resultSink.EvidenceMethod);
        AsyncBodyAttribution asyncBody =
            Assert.IsType<AsyncBodyAttribution>(
                resultSink.AsyncBody);
        Assert.Equal(
            AsyncLoweringKind.StateMachine,
            asyncBody.Lowering);
        Assert.Equal(export, asyncBody.SourceMethod);
        Assert.Equal(
            export,
            bodyIndex.ResolveDeclaredMethod(resultSink.EvidenceMethod));

        ILInspector.JsExportSurface.JsExportSurface surface =
            BuildFixtureSurfaceWithWireContracts();

        JsExportFunction function = Assert.Single(
            surface.Functions,
            candidate =>
                candidate.Name == "GetStringArrayAsyncAfterAwait");

        Assert.Equal("string[]", function.ReturnWireType);
    }

    [Fact]
    public void Build_ProducesEqualWireFactsAcrossAsyncLoweringsForDirectSerializerResult()
    {
        var (compilerSurface, compilerBodyIndex) =
            BuildFixtureSurfaceWithWireContracts(
                typeof(FixtureExports).Assembly.Location);
        var (runtimeSurface, runtimeBodyIndex) =
            BuildFixtureSurfaceWithWireContracts(
                s_runtimeAsyncFixturePath);

        const string exportName =
            "GetStringArrayAsyncAfterAwait";
        MethodIdentity compilerExport = Assert.Single(
            compilerBodyIndex.DeclaredMethods,
            method => method.Name == exportName);
        MethodIdentity runtimeExport = Assert.Single(
            runtimeBodyIndex.DeclaredMethods,
            method => method.Name == exportName);
        DirectCall compilerSerializer = Assert.Single(
            compilerBodyIndex.DirectCalls,
            call => call.Caller == compilerExport
                && call.Callee.Name == "Serialize");
        DirectCall runtimeSerializer = Assert.Single(
            runtimeBodyIndex.DirectCalls,
            call => call.Caller == runtimeExport
                && call.Callee.Name == "Serialize");
        MethodResultSink compilerResult = Assert.Single(
            compilerBodyIndex.ResultSinks,
            sink => sink.Caller == compilerExport
                && sink.SourceCallOffsets.Contains(
                    compilerSerializer.ILOffset));
        MethodResultSink runtimeResult = Assert.Single(
            runtimeBodyIndex.ResultSinks,
            sink => sink.Caller == runtimeExport
                && sink.SourceCallOffsets.Contains(
                    runtimeSerializer.ILOffset));

        Assert.Equal(
            MethodResultSinkKind.SingleArgumentCall,
            compilerResult.Kind);
        Assert.Equal("MoveNext", compilerResult.EvidenceMethod.Name);
        Assert.Equal(
            AsyncLoweringKind.StateMachine,
            Assert.IsType<AsyncBodyAttribution>(
                compilerResult.AsyncBody)
                .Lowering);
        Assert.Equal(
            MethodResultSinkKind.MethodReturn,
            runtimeResult.Kind);
        Assert.Equal(runtimeExport, runtimeResult.EvidenceMethod);
        AsyncBodyAttribution runtimeAttribution =
            Assert.IsType<AsyncBodyAttribution>(
                runtimeResult.AsyncBody);
        Assert.Equal(
            AsyncLoweringKind.Runtime,
            runtimeAttribution.Lowering);
        Assert.Equal(runtimeExport, runtimeAttribution.SourceMethod);
        Assert.DoesNotContain(
            runtimeBodyIndex.Methods,
            method => method.Name == "MoveNext"
                && method.DeclaringType
                    .ToQualifiedDisplayString()
                    .Contains(
                        $"<{exportName}>",
                        StringComparison.Ordinal));

        JsExportFunction compilerFunction = Assert.Single(
            compilerSurface.Functions,
            function => function.Name == exportName);
        JsExportFunction runtimeFunction = Assert.Single(
            runtimeSurface.Functions,
            function => function.Name == exportName);
        Assert.Equal(
            System.Text.Json.JsonSerializer.Serialize(
                compilerFunction),
            System.Text.Json.JsonSerializer.Serialize(
                runtimeFunction));
        Assert.Equal(
            compilerFunction.ReturnTypeReferences,
            runtimeFunction.ReturnTypeReferences);
        Assert.Equal(
            compilerFunction.ReturnWireTypeReferences,
            runtimeFunction.ReturnWireTypeReferences);
        Assert.Equal(
            compilerFunction.ParameterWireTypeReferences,
            runtimeFunction.ParameterWireTypeReferences);
        Assert.Equal("string[]", runtimeFunction.ReturnWireType);

        string[] compilerFacts =
        [
            .. compilerSurface.Functions
                .Select(function =>
                    System.Text.Json.JsonSerializer.Serialize(
                        function))
                .Order(StringComparer.Ordinal),
        ];
        string[] runtimeFacts =
        [
            .. runtimeSurface.Functions
                .Select(function =>
                    System.Text.Json.JsonSerializer.Serialize(
                        function))
                .Order(StringComparer.Ordinal),
        ];
        Assert.Equal(compilerFacts, runtimeFacts);
    }

    [Fact]
    public void
        Build_ProducesEqualWireFactsAcrossAsyncLoweringsForSerializerStoredAcrossSuspension()
    {
        var (compilerSurface, compilerBodyIndex) =
            BuildFixtureSurfaceWithWireContracts(
                typeof(FixtureExports).Assembly.Location);
        var (runtimeSurface, runtimeBodyIndex) =
            BuildFixtureSurfaceWithWireContracts(
                s_runtimeAsyncFixturePath);

        const string exportName =
            "GetWidgetSerializedBeforeAwait";
        MethodIdentity compilerExport = Assert.Single(
            compilerBodyIndex.DeclaredMethods,
            method => method.Name == exportName);
        MethodIdentity runtimeExport = Assert.Single(
            runtimeBodyIndex.DeclaredMethods,
            method => method.Name == exportName);
        DirectCall compilerSerializer = Assert.Single(
            compilerBodyIndex.DirectCalls,
            call => call.Caller == compilerExport
                && call.Callee.Name == "Serialize");
        DirectCall runtimeSerializer = Assert.Single(
            runtimeBodyIndex.DirectCalls,
            call => call.Caller == runtimeExport
                && call.Callee.Name == "Serialize");
        MethodResultSink compilerResult = Assert.Single(
            compilerBodyIndex.ResultSinks,
            sink => sink.Caller == compilerExport
                && sink.StateMachineFieldSource
                    ?.SourceCallOffsets.Contains(
                        compilerSerializer.ILOffset)
                    == true);
        MethodResultSink runtimeResult = Assert.Single(
            runtimeBodyIndex.ResultSinks,
            sink => sink.Caller == runtimeExport
                && sink.SourceCallOffsets.Contains(
                    runtimeSerializer.ILOffset));

        Assert.False(compilerResult.IsComplete);
        Assert.Empty(compilerResult.SourceCallOffsets);
        Assert.Equal("MoveNext", compilerResult.EvidenceMethod.Name);
        AsyncStateMachineFieldResultSource fieldSource =
            compilerResult.StateMachineFieldSource!;
        Assert.Equal(
            compilerResult.EvidenceMethod.DeclaringType,
            fieldSource.Field.DeclaringType);
        Assert.True(fieldSource.StoreOffset < fieldSource.LoadOffset);
        Assert.Equal(
            AsyncLoweringKind.StateMachine,
            compilerResult.AsyncBody?.Lowering);

        Assert.True(runtimeResult.IsComplete);
        Assert.Null(runtimeResult.StateMachineFieldSource);
        Assert.Equal(runtimeExport, runtimeResult.EvidenceMethod);
        Assert.Equal(
            AsyncLoweringKind.Runtime,
            runtimeResult.AsyncBody?.Lowering);

        JsExportFunction compilerFunction = Assert.Single(
            compilerSurface.Functions,
            function => function.Name == exportName);
        JsExportFunction runtimeFunction = Assert.Single(
            runtimeSurface.Functions,
            function => function.Name == exportName);
        Assert.Equal(
            System.Text.Json.JsonSerializer.Serialize(
                compilerFunction),
            System.Text.Json.JsonSerializer.Serialize(
                runtimeFunction));
        Assert.Equal(
            FixtureNamespace + "WidgetDto",
            compilerFunction.ReturnWireType);
        Assert.Equal(
            compilerFunction.ReturnWireType,
            runtimeFunction.ReturnWireType);
    }

    [Fact]
    public void
        Build_RejectsConditionalSerializerStoreAcrossAsyncLowerings()
    {
        var (compilerSurface, compilerBodyIndex) =
            BuildFixtureSurfaceWithWireContracts(
                typeof(FixtureExports).Assembly.Location);
        var (runtimeSurface, _) =
            BuildFixtureSurfaceWithWireContracts(
                s_runtimeAsyncFixturePath);

        const string exportName =
            "GetWidgetConditionallySerializedBeforeAwait";
        MethodIdentity compilerExport = Assert.Single(
            compilerBodyIndex.DeclaredMethods,
            method => method.Name == exportName);
        DirectCall compilerSerializer = Assert.Single(
            compilerBodyIndex.DirectCalls,
            call => call.Caller == compilerExport
                && call.Callee.Name == "Serialize");
        Assert.Contains(
            compilerBodyIndex.FieldStores,
            store => store.Caller == compilerExport
                && store.Value.Sources.Any(source =>
                    source.Kind
                        == ResolvedValueSourceKind.CallResult
                    && source.ILOffset
                        == compilerSerializer.ILOffset));
        Assert.DoesNotContain(
            compilerBodyIndex.ResultSinks,
            sink => sink.Caller == compilerExport
                && sink.StateMachineFieldSource is not null);

        JsExportFunction compilerFunction = Assert.Single(
            compilerSurface.Functions,
            function => function.Name == exportName);
        JsExportFunction runtimeFunction = Assert.Single(
            runtimeSurface.Functions,
            function => function.Name == exportName);
        Assert.Null(compilerFunction.ReturnWireType);
        Assert.Null(runtimeFunction.ReturnWireType);
    }

    [Fact]
    public void RuntimeAsyncAuthenticationRejectsForgedAttributionAndMetadata()
    {
        MethodIdentity export = RuntimeAsyncMethod(
            0x06000001,
            TaskOfString());
        MethodIdentity other = RuntimeAsyncMethod(
            0x06000002,
            TaskOfString(),
            "Other");
        LibraryBodyIndex bodyIndex =
            LibraryBodyIndex.FromEvidence(
                [export, other],
                []);
        MethodResultSink authentic = RuntimeAsyncSink(
            export,
            export,
            export,
            AsyncLoweringKind.Runtime);

        Assert.True(
            JsonWireContractResolver
                .IsAuthenticRuntimeAsyncResultSink(
                    bodyIndex,
                    authentic,
                    export.MetadataToken));
        Assert.False(
            JsonWireContractResolver
                .IsAuthenticRuntimeAsyncResultSink(
                    bodyIndex,
                    RuntimeAsyncSink(
                        export,
                        export,
                        export,
                        AsyncLoweringKind.StateMachine),
                    export.MetadataToken));
        Assert.False(
            JsonWireContractResolver
                .IsAuthenticRuntimeAsyncResultSink(
                    bodyIndex,
                    RuntimeAsyncSink(
                        export,
                        export,
                        other,
                        AsyncLoweringKind.Runtime),
                    export.MetadataToken));
        Assert.False(
            JsonWireContractResolver
                .IsAuthenticRuntimeAsyncResultSink(
                    bodyIndex,
                    RuntimeAsyncSink(
                        export,
                        other,
                        export,
                        AsyncLoweringKind.Runtime),
                    export.MetadataToken));

        MethodIdentity synchronous = export with
        {
            ReturnType = SystemString(),
        };
        MethodIdentity spoofedTask = export with
        {
            ReturnType = TaskOfString("Lookalikes"),
        };
        Assert.False(
            JsonWireContractResolver
                .IsAuthenticRuntimeAsyncResultSink(
                    LibraryBodyIndex.FromEvidence(
                        [synchronous],
                        []),
                    RuntimeAsyncSink(
                        synchronous,
                        synchronous,
                        synchronous,
                        AsyncLoweringKind.Runtime),
                    synchronous.MetadataToken));
        Assert.False(
            JsonWireContractResolver
                .IsAuthenticRuntimeAsyncResultSink(
                    LibraryBodyIndex.FromEvidence(
                        [spoofedTask],
                        []),
                    RuntimeAsyncSink(
                        spoofedTask,
                        spoofedTask,
                        spoofedTask,
                        AsyncLoweringKind.Runtime),
                    spoofedTask.MetadataToken));
    }

    [Fact]
    public void Build_RuntimeAsyncRejectsMixedSerializerAndRawReturns()
    {
        var (surface, bodyIndex) =
            BuildFixtureSurfaceWithWireContracts(
                s_runtimeAsyncFixturePath);
        MethodIdentity export = Assert.Single(
            bodyIndex.DeclaredMethods,
            method =>
                method.Name == "GetWidgetOrRawAfterAwait");
        MethodResultSink[] returns =
        [
            .. bodyIndex.ResultSinks.Where(sink =>
                sink.Caller == export
                && sink.Kind
                    == MethodResultSinkKind.MethodReturn),
        ];

        Assert.Contains(
            returns,
            sink => sink.IsComplete
                && !sink.SourceCallOffsets.IsDefaultOrEmpty);
        Assert.Contains(
            returns,
            sink => !sink.IsComplete);
        Assert.Null(
            Assert.Single(
                surface.Functions,
                function =>
                    function.Name
                        == "GetWidgetOrRawAfterAwait")
                .ReturnWireType);
    }

    [Fact]
    public void Build_RuntimeAsyncRejectsIncompleteReturnCoverage()
    {
        var (surface, bodyIndex) =
            BuildFixtureSurfaceWithWireContracts(
                s_runtimeAsyncFixturePath);
        MethodIdentity export = Assert.Single(
            bodyIndex.DeclaredMethods,
            method =>
                method.Name
                    == "GetWidgetFromIncompleteFlowAfterAwait");
        MethodResultSink result = Assert.Single(
            bodyIndex.ResultSinks,
            sink => sink.Caller == export
                && sink.Kind
                    == MethodResultSinkKind.MethodReturn);

        Assert.Equal(
            AsyncLoweringKind.Runtime,
            Assert.IsType<AsyncBodyAttribution>(
                result.AsyncBody)
                .Lowering);
        Assert.False(result.IsComplete);
        Assert.Null(
            Assert.Single(
                surface.Functions,
                function =>
                    function.Name
                        == "GetWidgetFromIncompleteFlowAfterAwait")
                .ReturnWireType);
    }

    [Fact]
    public void Build_RuntimeAsyncRejectsAnotherMethodsSerializerEvidence()
    {
        var (surface, bodyIndex) =
            BuildFixtureSurfaceWithWireContracts(
                s_runtimeAsyncFixturePath);
        MethodIdentity export = Assert.Single(
            bodyIndex.DeclaredMethods,
            method =>
                method.Name == "GetWidgetThroughLocalAsync");
        MethodResultSink foreignSink = Assert.Single(
            bodyIndex.ResultSinks,
            sink => sink.Caller == export
                && sink.EvidenceMethod != export
                && sink.SourceCallOffsets.Any(
                    offset => bodyIndex.DirectCalls.Any(call =>
                        call.EvidenceMethod
                            == sink.EvidenceMethod
                        && call.ILOffset == offset
                        && call.Callee.Name
                            == "Serialize")));
        Assert.False(
            JsonWireContractResolver
                .IsAuthenticRuntimeAsyncResultSink(
                    bodyIndex,
                    foreignSink,
                    export.MetadataToken));
        Assert.Null(
            Assert.Single(
                surface.Functions,
                function =>
                    function.Name
                        == "GetWidgetThroughLocalAsync")
                .ReturnWireType);
    }

    static MethodIdentity RuntimeAsyncMethod(
        int metadataToken,
        TypeRef returnType,
        string name = "Export")
        => new(
            "Fixture",
            new Guid("00112233-4455-6677-8899-aabbccddeeff"),
            ExternalType(
                "Fixture",
                "Fixtures",
                "Exports",
                publicKeyToken: null),
            name,
            [],
            returnType,
            metadataToken,
            IsStatic: true);

    static MethodResultSink RuntimeAsyncSink(
        MethodIdentity caller,
        MethodIdentity evidence,
        MethodIdentity source,
        AsyncLoweringKind lowering)
        => new(
            caller,
            evidence,
            ILOffset: 0,
            MethodResultSinkKind.MethodReturn,
            SourceCallOffsets: [1],
            IsComplete: true)
        {
            AsyncBody = new(source, lowering),
        };

    [Fact]
    public void Build_ResolvesRegisteredString()
    {
        ILInspector.JsExportSurface.JsExportSurface surface =
            BuildFixtureSurfaceWithWireContracts();

        JsExportFunction function = Assert.Single(
            surface.Functions,
            candidate => candidate.Name == "GetRegisteredString");

        Assert.Equal("string", function.ReturnWireType);
    }

    [Fact]
    public void Build_ResolvesRegisteredPrimitiveAndArrayRoots()
    {
        ILInspector.JsExportSurface.JsExportSurface surface =
            BuildFixtureSurfaceWithWireContracts();

        Assert.Equal(
            "int",
            Assert.Single(
                surface.Functions,
                function => function.Name == "GetRegisteredInt")
                .ReturnWireType);
        Assert.Equal(
            "int[]",
            Assert.Single(
                surface.Functions,
                function => function.Name == "GetRegisteredIntArray")
                .ReturnWireType);
        Assert.Equal(
            "byte[]",
            Assert.Single(
                surface.Functions,
                function => function.Name == "GetRegisteredByteArray")
                .ReturnWireType);
        Assert.Equal(
            "decimal",
            Assert.Single(
                surface.Functions,
                function => function.Name == "GetRegisteredDecimal")
                .ReturnWireType);
        Assert.Equal(
            "decimal[]",
            Assert.Single(
                surface.Functions,
                function => function.Name == "GetRegisteredDecimalArray")
                .ReturnWireType);
        Assert.Equal(
            ["int"],
            Assert.Single(
                surface.Functions,
                function => function.Name == "ReadRegisteredInt")
                .ParameterWireTypes);
    }

    [Fact]
    public void Build_ResolvesClosedGenericRootAndItsLocalArgument()
    {
        ILInspector.JsExportSurface.JsExportSurface surface =
            BuildFixtureSurfaceWithWireContracts();

        JsExportFunction function = Assert.Single(
            surface.Functions,
            candidate => candidate.Name == "GetClosedGenericRoot");

        Assert.NotNull(function.ReturnWireType);
        Assert.Contains(
            "Dictionary",
            function.ReturnWireType,
            StringComparison.Ordinal);
        Assert.Contains(
            function.ReturnWireTypeReferences,
            reference => reference.DefinitionName?.Segments
                is [nameof(ClosedGenericRootDto)]);
    }

    [Fact]
    public void Build_UsesEffectiveSourceGenerationModesForWireDirections()
    {
        ILInspector.JsExportSurface.JsExportSurface surface =
            BuildFixtureSurfaceWithWireContracts();

        JsExportFunction serializeOnly = Assert.Single(
            surface.Functions,
            function => function.Name == "GetContextSerializationOnly");
        Assert.Equal(
            FixtureNamespace + nameof(ContextSerializationOnlyDto),
            serializeOnly.ReturnWireType);
        Assert.Empty(
            Assert.Single(
                surface.Functions,
                function => function.Name == "SetContextSerializationOnly")
                .ParameterWireTypes);
        Assert.Equal(
            [FixtureNamespace + nameof(MetadataOverrideDto)],
            Assert.Single(
                surface.Functions,
                function => function.Name == "SetMetadataOverride")
                .ParameterWireTypes);
    }

    [Fact]
    public void SourceGeneratedJson_SerializationOnlyRootRejectsDeserializeAndPreservesSerializeShape()
    {
        string json = System.Text.Json.JsonSerializer.Serialize(
            new ContextSerializationOnlyDto("mode")
            {
                ServerNote = "server",
                ClientSecret = "client",
            },
            SourceGenerationModeFixtureJsonContext.Default
                .ContextSerializationOnlyDto);

        Assert.Equal(
            """{"Name":"mode","ServerNote":"server"}""",
            json);
        Assert.Throws<InvalidOperationException>(
            () => System.Text.Json.JsonSerializer.Deserialize(
                """{"Name":"mode"}""",
                SourceGenerationModeFixtureJsonContext.Default
                    .ContextSerializationOnlyDto));
        Assert.Equal(
            "metadata",
            System.Text.Json.JsonSerializer.Deserialize(
                """{"Name":"metadata"}""",
                SourceGenerationModeFixtureJsonContext.Default
                    .MetadataOverrideDto)!
                .Name);
    }

    [Fact]
    public void Build_AuthenticatesOnlyGeneratedCustomNamedContextProperty()
    {
        ILInspector.JsExportSurface.JsExportSurface surface =
            BuildFixtureSurfaceWithWireContracts();

        JsExportFunction generated = Assert.Single(
            surface.Functions,
            candidate => candidate.Name == "GetCustomNamedGenerated");
        JsExportFunction handwritten = Assert.Single(
            surface.Functions,
            candidate => candidate.Name == "GetCustomNamedHandwritten");

        Assert.Equal(
            FixtureNamespace + "CustomNamedDto",
            generated.ReturnWireType);
        Assert.Null(handwritten.ReturnWireType);
    }

    [Fact]
    public void Build_ResolvesParameterWireTypeForDeserializeCall()
    {
        ILInspector.JsExportSurface.JsExportSurface surface = BuildFixtureSurfaceWithWireContracts();

        JsExportFunction renameWidget = Assert.Single(
            surface.Functions,
            f => f.Name == "RenameWidget");
        Assert.Equal(
            FixtureNamespace + "WidgetDto",
            renameWidget.ReturnWireType);
        Assert.Equal(
            [FixtureNamespace + "WidgetDto"],
            renameWidget.ParameterWireTypes);
    }

    [Fact]
    public void Build_LeavesReturnWireTypeUnsetWhenBodySerializesMoreThanOneDistinctDto()
    {
        // GetWidgetOrOwner Serialize<T>'s WidgetOwner on one branch and WidgetDto on the other.
        // DirectCall carries no branch/reachability evidence to decide which one actually reaches
        // the caller, so the ambiguity must be left unresolved rather than guessed.
        ILInspector.JsExportSurface.JsExportSurface surface = BuildFixtureSurfaceWithWireContracts();

        JsExportFunction fn = Assert.Single(
            surface.Functions,
            f => f.Name == "GetWidgetOrOwner");
        Assert.Null(fn.ReturnWireType);
    }

    [Fact]
    public void Build_LeavesReturnWireTypeUnsetWhenAnyPhysicalReturnIsRaw()
    {
        ILInspector.JsExportSurface.JsExportSurface surface =
            BuildFixtureSurfaceWithWireContracts();

        JsExportFunction fn = Assert.Single(
            surface.Functions,
            function => function.Name == "GetWidgetOrRawOk");

        Assert.Null(fn.ReturnWireType);
    }

    [Fact]
    public void Build_LeavesReturnWireTypeUnsetForRawEvaluationStackMerges()
    {
        ILInspector.JsExportSurface.JsExportSurface surface =
            BuildFixtureSurfaceWithWireContracts();

        JsExportFunction[] functions = [.. surface.Functions.Where(function =>
            function.Name is "GetWidgetOrCached"
                or "GetWidgetOrCachedViaLocal")];
        Assert.Equal(2, functions.Length);
        Assert.All(
            functions,
            function => Assert.Null(function.ReturnWireType));
    }

    [Fact]
    public void Build_ResolvesReturnWireTypeForSameDtoSerializerBranches()
    {
        ILInspector.JsExportSurface.JsExportSurface surface =
            BuildFixtureSurfaceWithWireContracts();

        JsExportFunction fn = Assert.Single(
            surface.Functions,
            function => function.Name == "GetWidgetFromEitherJsonBranch");

        Assert.Equal(FixtureNamespace + "WidgetDto", fn.ReturnWireType);
    }

    [Fact]
    public void Build_ResolvesContainerShapedReturnWireType()
    {
        // The Serialize<T> type argument is WidgetDto[], not WidgetDto. TypeRef.Name is empty for
        // non-Definition kinds, so this only resolves correctly via
        // ToQualifiedDisplayString().
        ILInspector.JsExportSurface.JsExportSurface surface = BuildFixtureSurfaceWithWireContracts();

        JsExportFunction fn = Assert.Single(
            surface.Functions,
            f => f.Name == "GetWidgetArray");
        Assert.Equal(
            FixtureNamespace + "WidgetDto[]",
            fn.ReturnWireType);
    }

    [Fact]
    public void Build_DoesNotTreatStreamSerializationAsReturnEnvelope()
    {
        ILInspector.JsExportSurface.JsExportSurface surface =
            BuildFixtureSurfaceWithWireContracts();

        JsExportFunction fn = Assert.Single(
            surface.Functions,
            function =>
                function.Name == "SerializeWidgetSideEffect");

        Assert.Null(fn.ReturnWireType);
    }

    [Fact]
    public void Build_DoesNotTreatDiscardedSerializationAsReturnEnvelope()
    {
        ILInspector.JsExportSurface.JsExportSurface surface =
            BuildFixtureSurfaceWithWireContracts();

        JsExportFunction fn = Assert.Single(
            surface.Functions,
            function =>
                function.Name == "IgnoreSerializedWidget");

        Assert.Null(fn.ReturnWireType);
    }

    [Fact]
    public void Build_RejectsUnrelatedAsyncBuilderResultSink()
    {
        ILInspector.JsExportSurface.JsExportSurface surface =
            BuildFixtureSurfaceWithWireContracts();

        JsExportFunction fn = Assert.Single(
            surface.Functions,
            function => function.Name == "SetUnrelatedAsyncBuilder");

        Assert.Null(fn.ReturnWireType);
    }

    [Fact]
    public void Build_RequiresRegisteredContextPropertyArgumentProvenance()
    {
        ILInspector.JsExportSurface.JsExportSurface surface =
            BuildFixtureSurfaceWithWireContracts();

        JsExportFunction[] functions = [.. surface.Functions.Where(function =>
            function.Name is "RoundTripWidgetWithRuntimeTypeInfo"
                or "RoundTripWidgetWithUnrelatedTypeInfo")];
        Assert.Equal(2, functions.Length);
        Assert.All(
            functions,
            function =>
            {
                Assert.Null(function.ReturnWireType);
                Assert.Empty(function.ParameterWireTypes);
            });
    }

    [Fact]
    public void Build_LeavesWireContractUnsetForNonEnvelopeExport()
    {
        // Ping has no JSON envelope at all (returns a non-generic Task), so no
        // JsonSerializer.Serialize/Deserialize call site exists in its body.
        ILInspector.JsExportSurface.JsExportSurface surface = BuildFixtureSurfaceWithWireContracts();

        JsExportFunction ping = Assert.Single(surface.Functions, f => f.Name == "Ping");
        Assert.Null(ping.ReturnWireType);
        Assert.Empty(ping.ParameterWireTypes);
    }

    [Fact]
    public void Build_WithoutBodyIndex_LeavesWireContractFieldsUnset()
    {
        // The overload without a LibraryBodyIndex must not attempt call-site resolution.
        string path = typeof(FixtureExports).Assembly.Location;
        using FileStream stream = File.OpenRead(path);
        using var peReader = new PEReader(stream);
        ApiSurface apiSurface = ApiSurfaceExtractor.Extract(peReader, includeAll: false);

        ILInspector.JsExportSurface.JsExportSurface surface = JsExportSurfaceBuilder.Build(apiSurface);

        Assert.All(surface.Functions, f => Assert.Null(f.ReturnWireType));
        Assert.All(surface.Functions, f => Assert.Empty(f.ParameterWireTypes));
    }

    [Fact]
    public void Build_RejectsRealAsyncStateMachineAnalysisFailure()
    {
        string sourcePath = typeof(FixtureExports).Assembly.Location;
        byte[] image = File.ReadAllBytes(sourcePath);
        int exportToken;
        int moveNextToken;
        int moveNextRva;
        using (var stream = new MemoryStream(image, writable: false))
        using (var peReader = new PEReader(stream))
        {
            MetadataReader reader = peReader.GetMetadataReader();
            TypeDefinition fixtureType = reader.TypeDefinitions
                .Select(reader.GetTypeDefinition)
                .Single(type => reader.GetString(type.Name)
                    == nameof(FixtureExports));
            MethodDefinitionHandle exportHandle =
                fixtureType.GetMethods().Single(handle =>
                    reader.GetString(
                        reader.GetMethodDefinition(handle).Name)
                        == "GetWidgetAsync");
            exportToken = MetadataTokens.GetToken(exportHandle);

            TypeDefinition stateMachine = reader.TypeDefinitions
                .Select(reader.GetTypeDefinition)
                .Single(type => reader.GetString(type.Name)
                    .StartsWith(
                        "<GetWidgetAsync>d__",
                        StringComparison.Ordinal));
            MethodDefinitionHandle moveNextHandle =
                stateMachine.GetMethods().Single(handle =>
                    reader.GetString(
                        reader.GetMethodDefinition(handle).Name)
                        == "MoveNext");
            MethodDefinition moveNext =
                reader.GetMethodDefinition(moveNextHandle);
            moveNextToken = MetadataTokens.GetToken(moveNextHandle);
            moveNextRva = moveNext.RelativeVirtualAddress;

            int bodyOffset = RvaToFileOffset(
                peReader.PEHeaders,
                moveNextRva);
            image[bodyOffset] = 0x01;
        }

        string scratchDirectory = Path.Combine(
            "artifacts",
            $"jsexport-async-{Guid.NewGuid():N}");
        Directory.CreateDirectory(scratchDirectory);
        string corruptedPath = Path.Combine(
            scratchDirectory,
            "fixture.dll");
        try
        {
            File.WriteAllBytes(corruptedPath, image);
            LibraryBodyIndex bodyIndex =
                LibraryBodyIndex.Open(
                    corruptedPath,
                    LibraryBodyAnalysisFeatures.MethodEvidence
                        | LibraryBodyAnalysisFeatures.JsonWireContractFlow);
            AnalysisDiagnostic diagnostic = Assert.Single(
                bodyIndex.Diagnostics,
                candidate => candidate.MethodToken == moveNextToken);
            Assert.Equal(exportToken, diagnostic.SourceMethodToken);

            using FileStream source = File.OpenRead(sourcePath);
            using var sourceReader = new PEReader(source);
            ApiSurface apiSurface = ApiSurfaceExtractor.Extract(
                sourceReader,
                includeAll: false);

            Assert.Throws<UnsupportedJsExportSurfaceException>(
                () => JsExportSurfaceBuilder.Build(
                    apiSurface,
                    bodyIndex));
        }
        finally
        {
            File.Delete(corruptedPath);
            Directory.Delete(scratchDirectory);
        }
    }

    [Fact]
    public void Build_RejectsRealAsyncStateMachineCallAnalysisFailure()
    {
        string sourcePath = typeof(FixtureExports).Assembly.Location;
        byte[] image = File.ReadAllBytes(sourcePath);
        int exportToken;
        int moveNextToken;
        using (var stream = new MemoryStream(image, writable: false))
        using (var peReader = new PEReader(stream))
        {
            MetadataReader reader = peReader.GetMetadataReader();
            TypeDefinition fixtureType = reader.TypeDefinitions
                .Select(reader.GetTypeDefinition)
                .Single(type => reader.GetString(type.Name)
                    == nameof(FixtureExports));
            MethodDefinitionHandle exportHandle =
                fixtureType.GetMethods().Single(handle =>
                    reader.GetString(
                        reader.GetMethodDefinition(handle).Name)
                        == "GetWidgetAsync");
            exportToken = MetadataTokens.GetToken(exportHandle);

            TypeDefinition stateMachine = reader.TypeDefinitions
                .Select(reader.GetTypeDefinition)
                .Single(type => reader.GetString(type.Name)
                    .StartsWith(
                        "<GetWidgetAsync>d__",
                        StringComparison.Ordinal));
            MethodDefinitionHandle moveNextHandle =
                stateMachine.GetMethods().Single(handle =>
                    reader.GetString(
                        reader.GetMethodDefinition(handle).Name)
                        == "MoveNext");
            MethodDefinition moveNext =
                reader.GetMethodDefinition(moveNextHandle);
            moveNextToken = MetadataTokens.GetToken(moveNextHandle);
            MethodBodyBlock body = peReader.GetMethodBody(
                moveNext.RelativeVirtualAddress);
            DecodedInstruction call = MethodInstructions
                .Decode(body)
                .Instructions
                .First(instruction =>
                    instruction.OpCode == ILOpCode.Call);
            int bodyOffset = RvaToFileOffset(
                peReader.PEHeaders,
                moveNext.RelativeVirtualAddress);
            int headerSize = MethodHeaderSize(
                image,
                bodyOffset);
            BinaryPrimitives.WriteInt32LittleEndian(
                image.AsSpan(
                    bodyOffset
                        + headerSize
                        + call.OperandOffset,
                    sizeof(int)),
                0x06FFFFFF);
        }

        string scratchDirectory = Path.Combine(
            "artifacts",
            $"jsexport-async-call-{Guid.NewGuid():N}");
        Directory.CreateDirectory(scratchDirectory);
        string corruptedPath = Path.Combine(
            scratchDirectory,
            "fixture.dll");
        try
        {
            File.WriteAllBytes(corruptedPath, image);
            LibraryBodyIndex bodyIndex =
                LibraryBodyIndex.Open(
                    corruptedPath,
                    LibraryBodyAnalysisFeatures.MethodEvidence
                        | LibraryBodyAnalysisFeatures.JsonWireContractFlow);
            AnalysisDiagnostic diagnostic = Assert.Single(
                bodyIndex.Diagnostics,
                candidate => candidate.MethodToken == moveNextToken);
            Assert.Equal(exportToken, diagnostic.SourceMethodToken);

            using FileStream source = File.OpenRead(sourcePath);
            using var sourceReader = new PEReader(source);
            ApiSurface apiSurface = ApiSurfaceExtractor.Extract(
                sourceReader,
                includeAll: false);

            Assert.Throws<UnsupportedJsExportSurfaceException>(
                () => JsExportSurfaceBuilder.Build(
                    apiSurface,
                    bodyIndex));
        }
        finally
        {
            File.Delete(corruptedPath);
            Directory.Delete(scratchDirectory);
        }
    }

    static int MethodHeaderSize(
        byte[] image,
        int bodyOffset)
    {
        byte first = image[bodyOffset];
        if ((first & 0x3) == 0x2)
            return 1;
        ushort flagsAndSize =
            BinaryPrimitives.ReadUInt16LittleEndian(
                image.AsSpan(
                    bodyOffset,
                    sizeof(ushort)));
        return (flagsAndSize >> 12) * 4;
    }

    static int RvaToFileOffset(PEHeaders headers, int rva)
    {
        foreach (SectionHeader section in headers.SectionHeaders)
        {
            int size = Math.Max(section.VirtualSize, section.SizeOfRawData);
            if (rva >= section.VirtualAddress
                && rva < section.VirtualAddress + size)
            {
                return section.PointerToRawData
                    + rva
                    - section.VirtualAddress;
            }
        }

        throw new InvalidOperationException(
            $"RVA 0x{rva:X8} is not in a PE section.");
    }
}
