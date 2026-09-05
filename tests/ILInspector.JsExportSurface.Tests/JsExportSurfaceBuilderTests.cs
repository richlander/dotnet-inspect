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

public sealed class JsExportSurfaceBuilderTests
{
    static void FourArgumentCallback(
        Action<int, int, int, int> callback)
    {
    }

    private static ILInspector.JsExportSurface.JsExportSurface BuildFixtureSurface(bool includeAll = false)
    {
        using FileStream stream = File.OpenRead(typeof(FixtureExports).Assembly.Location);
        using var peReader = new PEReader(stream);
        ApiSurface apiSurface = ApiSurfaceExtractor.Extract(peReader, includeAll: includeAll);
        return JsExportSurfaceBuilder.Build(apiSurface);
    }

    private static ILInspector.JsExportSurface.JsExportSurface
        BuildFixtureSurfaceWithBodies()
    {
        string path = typeof(FixtureExports).Assembly.Location;
        using FileStream stream = File.OpenRead(path);
        using var peReader = new PEReader(stream);
        ApiSurface apiSurface = ApiSurfaceExtractor.Extract(
            peReader,
            includeAll: false);
        return JsExportSurfaceBuilder.Build(
            apiSurface,
            OpenWireContractBodyIndex(path));
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
        Assert.Equal(53, surface.Functions.Count);
        Assert.Contains("GetWidget", names);
        Assert.Contains("GetWidgetAsync", names);
        Assert.Contains("GetWidgetSerializedBeforeAwait", names);
        Assert.Contains(
            "GetWidgetConditionallySerializedBeforeAwait",
            names);
        Assert.Contains("GetStringArrayAsyncAfterAwait", names);
        Assert.Contains("GetWidgetOrRawAfterAwait", names);
        Assert.Contains(
            "GetWidgetFromIncompleteFlowAfterAwait",
            names);
        Assert.Contains("GetWidgetThroughLocalAsync", names);
        Assert.Contains("EchoBytes", names);
        Assert.Contains("ReportValue", names);
        Assert.Contains("ReportValueAgain", names);
        Assert.Contains("ReportNullableText", names);
        Assert.Contains("TransformValue", names);
        Assert.Contains("ObserveValues", names);
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
        Assert.Contains("SetDirectionalSharedInput", names);
        Assert.Contains("SetDirectionalAccessorInput", names);
        Assert.Contains("RoundTripDirectional", names);
        Assert.Contains("GetClosedGenericRoot", names);
        Assert.Contains("GetRegisteredInt", names);
        Assert.Contains("GetRegisteredIntArray", names);
        Assert.Contains("GetRegisteredByteArray", names);
        Assert.Contains("GetRegisteredDecimal", names);
        Assert.Contains("GetRegisteredDecimalArray", names);
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

    [Fact]
    public void Build_PublishesAuthenticatedSynchronousDelegateSignatures()
    {
        ILInspector.JsExportSurface.JsExportSurface surface =
            BuildFixtureSurfaceWithBodies();

        JsExportDelegateParameter action = Assert.Single(
            Assert.Single(
                surface.Functions,
                function => function.Name == "ReportValue")
            .DelegateParameters);
        Assert.Equal(0, action.ParameterIndex);
        Assert.Equal(JsExportDelegateKind.Action, action.Kind);
        Assert.Equal(
            "int",
            Assert.Single(action.ParameterTypes).ToDisplayString());
        Assert.Null(action.ReturnType);

        JsExportDelegateParameter func = Assert.Single(
            Assert.Single(
                surface.Functions,
                function => function.Name == "TransformValue")
            .DelegateParameters);
        Assert.Equal(JsExportDelegateKind.Func, func.Kind);
        Assert.Collection(
            func.ParameterTypes,
            type => Assert.Equal("int", type.ToDisplayString()),
            type => Assert.Equal("string", type.ToDisplayString()));
        Assert.Equal("bool", func.ReturnType?.ToDisplayString());
    }

    [Fact]
    public void TryGetDelegateShape_RejectsDecodedFourArgumentAction()
    {
        LibraryBodyIndex bodyIndex = LibraryBodyIndex.Open(
            typeof(JsExportSurfaceBuilderTests).Assembly.Location,
            LibraryBodyAnalysisFeatures.MethodEvidence);
        MethodIdentity method = Assert.Single(
            bodyIndex.DeclaredMethods,
            candidate => candidate.Name
                == nameof(FourArgumentCallback));
        TypeRef callbackType = Assert.Single(method.ParameterTypes);

        Assert.False(
            JsExportSurfaceBuilder.TryGetDelegateShape(
                callbackType,
                out _,
                out _,
                out _));
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
    public void Build_RejectsBodylessJsExportsWithoutRuntimeWrappers()
    {
        string path =
            typeof(BodylessInterfaceExportFixture).Assembly.Location;
        ApiSurface extracted = ExtractApiSurface(path);

        foreach (string typeName in new[]
        {
            nameof(BodylessInterfaceExportFixture),
            nameof(BodylessExternExportFixture),
        })
        {
            ApiType fixture = Assert.Single(
                extracted.Types,
                type => type.Name == typeName);
            ApiMember method = Assert.Single(
                fixture.Members,
                member => member.Name == "Compute");
            var isolated = new ApiSurface
            {
                AssemblyIdentity = extracted.AssemblyIdentity,
                Types = [fixture],
            };

            Assert.True(method.HasRuntimeJsExport);
            Assert.False(method.HasMethodBody);
            UnsupportedJsExportSurfaceException exception =
                Assert.Throws<UnsupportedJsExportSurfaceException>(
                    () => JsExportSurfaceBuilder.Build(isolated));
            Assert.Contains(
                "bodyless JS exports have no runtime wrapper",
                exception.Message,
                StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Build_RejectsJsExportWithoutGeneratedRuntimeWrapper()
    {
        string path =
            typeof(NonPartialExportFixture).Assembly.Location;
        ApiSurface extracted = ExtractApiSurface(path);
        ApiType fixture = Assert.Single(
            extracted.Types,
            type => type.Name == nameof(NonPartialExportFixture));
        ApiMember method = Assert.Single(
            fixture.Members,
            member => member.Name
                == nameof(NonPartialExportFixture.AddOne));
        var isolated = new ApiSurface
        {
            AssemblyIdentity = extracted.AssemblyIdentity,
            Types = [fixture],
        };

        Assert.True(method.HasRuntimeJsExport);
        Assert.True(method.HasMethodBody);
        Assert.False(
            method.HasRuntimeJsExportWrapperCandidate);
        Assert.DoesNotContain(
            fixture.Members,
            member => member.Name.StartsWith(
                "__Wrapper_AddOne_",
                StringComparison.Ordinal));
        UnsupportedJsExportSurfaceException exception =
            Assert.Throws<UnsupportedJsExportSurfaceException>(
                () => JsExportSurfaceBuilder.Build(isolated));
        Assert.Contains(
            "no compiler-generated runtime wrapper",
            exception.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Build_RejectsHandwrittenRuntimeWrapperCandidate()
    {
        string path =
            typeof(HandwrittenWrapperCandidateFixture)
                .Assembly.Location;
        ApiSurface extracted = ExtractApiSurface(path);
        ApiType fixture = Assert.Single(
            extracted.Types,
            type => type.Name
                == nameof(HandwrittenWrapperCandidateFixture));
        ApiMember method = Assert.Single(
            fixture.Members,
            member => member.Name
                == nameof(
                    HandwrittenWrapperCandidateFixture.AddOne));
        extracted.FilteredRuntimeJsExportFacts = [];
        extracted.Types = [fixture];

        Assert.False(
            method.HasRuntimeJsExportWrapperCandidate);
        method.HasRuntimeJsExportWrapperCandidate = true;
        UnsupportedJsExportSurfaceException exception =
            Assert.Throws<UnsupportedJsExportSurfaceException>(
                () => JsExportSurfaceBuilder.Build(
                    extracted,
                    OpenWireContractBodyIndex(path)));
        Assert.Contains(
            "no compiler-generated runtime wrapper",
            exception.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Build_DoesNotBorrowWrapperRegistrationFromAnotherType()
    {
        string path =
            typeof(TargetIdentitySpoofFixture).Assembly.Location;
        ApiSurface extracted = ExtractApiSurface(path);
        ApiType spoof = Assert.Single(
            extracted.Types,
            type => type.Name
                == nameof(TargetIdentitySpoofFixture));
        ApiMember export = Assert.Single(
            spoof.Members,
            member => member.Name
                == nameof(TargetIdentitySpoofFixture.ReadValue));
        LibraryBodyIndex bodyIndex =
            OpenWireContractBodyIndex(path);
        const string wrapperName =
            "__Wrapper_ReadValue_764966221";
        MethodIdentity wrapper = Assert.Single(
            bodyIndex.Methods,
            method => method.DeclaringType.Name
                    == nameof(TargetIdentitySpoofFixture)
                && method.Name == wrapperName);
        DirectCall wrapperCall = Assert.Single(
            bodyIndex.DirectCalls,
            call => call.EvidenceMethod.MetadataToken
                    == wrapper.MetadataToken
                && call.Callee.Name.StartsWith(
                    $"<{wrapperName}>g____Stub|",
                    StringComparison.Ordinal));
        Assert.Contains(
            bodyIndex.DirectCalls,
            call => call.EvidenceMethod.MetadataToken
                    == wrapperCall.CalleeDefinitionToken
                && call.CalleeDefinitionToken
                    == export.MetadataToken);
        Assert.Contains(
            bodyIndex.Methods,
            method => method.DeclaringType.Name
                    != nameof(TargetIdentitySpoofFixture)
                && method.Name == wrapperName);

        extracted.FilteredRuntimeJsExportFacts = [];
        extracted.Types = [spoof];

        Assert.False(
            export.HasRuntimeJsExportWrapperCandidate);
        UnsupportedJsExportSurfaceException exception =
            Assert.Throws<UnsupportedJsExportSurfaceException>(
                () => JsExportSurfaceBuilder.Build(
                    extracted,
                    bodyIndex));
        Assert.Contains(
            "no compiler-generated runtime wrapper",
            exception.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Build_RejectsRegistrationBodyCountMismatch()
    {
        string path =
            typeof(PopulateExports).Assembly.Location;
        ApiSurface extracted = ExtractApiSurface(path);
        ApiType exports = Assert.Single(
            extracted.Types,
            type => type.Name
                == nameof(PopulateExports));
        ApiMember export = Assert.Single(
            exports.Members,
            member => member.Name
                == nameof(PopulateExports.CountValues));
        LibraryBodyIndex bodyIndex =
            OpenWireContractBodyIndex(path);
        RuntimeJsExportWrapperCandidate generatedCandidate =
            Assert.Single(
                export.RuntimeJsExportWrapperCandidates!);

        export.RuntimeJsExportWrapperCandidates =
        [
            generatedCandidate with
            {
                RegistrationCount =
                    generatedCandidate.RegistrationCount + 1,
            },
        ];
        extracted.FilteredRuntimeJsExportFacts = [];
        extracted.Types = [exports];

        UnsupportedJsExportSurfaceException exception =
            Assert.Throws<UnsupportedJsExportSurfaceException>(
                () => JsExportSurfaceBuilder.Build(
                    extracted,
                    bodyIndex));
        Assert.Contains(
            "no compiler-generated runtime wrapper",
            exception.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Build_WithBodiesRejectsLegacyNullWrapperProvenance()
    {
        string path =
            typeof(PopulateExports).Assembly.Location;
        ApiSurface extracted = ExtractApiSurface(path);
        ApiType exports = Assert.Single(
            extracted.Types,
            type => type.Name
                == nameof(PopulateExports));
        ApiMember export = Assert.Single(
            exports.Members,
            member => member.Name
                == nameof(PopulateExports.CountValues));
        extracted.FilteredRuntimeJsExportFacts = [];
        extracted.Types = [exports];
        LibraryBodyIndex bodyIndex =
            OpenWireContractBodyIndex(path);

        Assert.Single(
            JsExportSurfaceBuilder.Build(
                extracted,
                bodyIndex).Functions);
        export.HasRuntimeJsExportWrapperCandidate = null;

        Assert.Single(
            JsExportSurfaceBuilder.Build(extracted).Functions);
        UnsupportedJsExportSurfaceException exception =
            Assert.Throws<UnsupportedJsExportSurfaceException>(
                () => JsExportSurfaceBuilder.Build(
                    extracted,
                    bodyIndex));
        Assert.Contains(
            "no compiler-generated runtime wrapper",
            exception.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Build_RejectsRuntimeWrapperFromDifferentModule()
    {
        string path =
            typeof(PopulateExports).Assembly.Location;
        ApiSurface extracted = ExtractApiSurface(path);
        ApiType exports = Assert.Single(
            extracted.Types,
            type => type.Name
                == nameof(PopulateExports));
        ApiMember export = Assert.Single(
            exports.Members,
            member => member.Name
                == nameof(PopulateExports.CountValues));
        extracted.FilteredRuntimeJsExportFacts = [];
        extracted.Types = [exports];
        LibraryBodyIndex bodyIndex =
            OpenWireContractBodyIndex(path);
        RuntimeJsExportWrapperCandidate candidate =
            Assert.Single(
                export.RuntimeJsExportWrapperCandidates!);

        Assert.Single(
            JsExportSurfaceBuilder.Build(
                extracted,
                bodyIndex).Functions);
        export.RuntimeJsExportWrapperCandidates =
        [
            candidate with
            {
                ModuleVersionId = Guid.NewGuid(),
            },
        ];

        UnsupportedJsExportSurfaceException exception =
            Assert.Throws<UnsupportedJsExportSurfaceException>(
                () => JsExportSurfaceBuilder.Build(
                    extracted,
                    bodyIndex));
        Assert.Contains(
            "no compiler-generated runtime wrapper",
            exception.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Build_RejectsRuntimeWrapperWithoutModuleIdentity()
    {
        string path =
            typeof(PopulateExports).Assembly.Location;
        ApiSurface extracted = ExtractApiSurface(path);
        ApiType exports = Assert.Single(
            extracted.Types,
            type => type.Name
                == nameof(PopulateExports));
        ApiMember export = Assert.Single(
            exports.Members,
            member => member.Name
                == nameof(PopulateExports.CountValues));
        extracted.FilteredRuntimeJsExportFacts = [];
        extracted.Types = [exports];
        LibraryBodyIndex bodyIndex =
            OpenWireContractBodyIndex(path);
        RuntimeJsExportWrapperCandidate candidate =
            Assert.Single(
                export.RuntimeJsExportWrapperCandidates!);
        ImmutableArray<DirectCall> emptyMvidCalls =
        [
            .. bodyIndex.DirectCalls.Select(call =>
                call with
                {
                    EvidenceMethod = call.EvidenceMethod with
                    {
                        ModuleVersionId = Guid.Empty,
                    },
                }),
        ];
        LibraryBodyIndex emptyMvidIndex =
            LibraryBodyIndex.FromEvidence(
                bodyIndex.Methods,
                [],
                diagnostics: bodyIndex.Diagnostics,
                directCalls: emptyMvidCalls,
                resultSinks: bodyIndex.ResultSinks);

        Assert.Single(
            JsExportSurfaceBuilder.Build(
                extracted,
                bodyIndex).Functions);
        export.RuntimeJsExportWrapperCandidates =
        [
            candidate with
            {
                ModuleVersionId = Guid.Empty,
            },
        ];

        UnsupportedJsExportSurfaceException exception =
            Assert.Throws<UnsupportedJsExportSurfaceException>(
                () => JsExportSurfaceBuilder.Build(
                    extracted,
                    emptyMvidIndex));
        Assert.Contains(
            "no compiler-generated runtime wrapper",
            exception.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Build_RejectsRuntimeWrapperWithNullModuleIdentity()
    {
        string path =
            typeof(PopulateExports).Assembly.Location;
        ApiSurface extracted = ExtractApiSurface(path);
        ApiType exports = Assert.Single(
            extracted.Types,
            type => type.Name
                == nameof(PopulateExports));
        ApiMember export = Assert.Single(
            exports.Members,
            member => member.Name
                == nameof(PopulateExports.CountValues));
        extracted.FilteredRuntimeJsExportFacts = [];
        extracted.Types = [exports];
        LibraryBodyIndex bodyIndex =
            OpenWireContractBodyIndex(path);
        RuntimeJsExportWrapperCandidate candidate =
            Assert.Single(
                export.RuntimeJsExportWrapperCandidates!);

        Assert.Single(
            JsExportSurfaceBuilder.Build(
                extracted,
                bodyIndex).Functions);
        export.RuntimeJsExportWrapperCandidates =
        [
            candidate with
            {
                ModuleVersionId = null,
            },
        ];

        UnsupportedJsExportSurfaceException exception =
            Assert.Throws<UnsupportedJsExportSurfaceException>(
                () => JsExportSurfaceBuilder.Build(
                    extracted,
                    bodyIndex));
        Assert.Contains(
            "no compiler-generated runtime wrapper",
            exception.Message,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("System.Runtime", true)]
    [InlineData(
        "System.Runtime.InteropServices.JavaScript",
        false)]
    public void Build_RejectsRuntimeWrapperWithUnauthenticatedMarshalerArgument(
        string marshalerAssembly,
        bool trustedFrameworkAssembly)
    {
        string path =
            typeof(PopulateExports).Assembly.Location;
        ApiSurface extracted = ExtractApiSurface(path);
        ApiType exports = Assert.Single(
            extracted.Types,
            type => type.Name
                == nameof(PopulateExports));
        ApiMember export = Assert.Single(
            exports.Members,
            member => member.Name
                == nameof(PopulateExports.CountValues));
        extracted.FilteredRuntimeJsExportFacts = [];
        extracted.Types = [exports];
        LibraryBodyIndex bodyIndex =
            OpenWireContractBodyIndex(path);
        RuntimeJsExportWrapperCandidate candidate =
            Assert.Single(
                export.RuntimeJsExportWrapperCandidates!);
        TypeRef wrongAssemblyArgument = TypeRef.Pointer(
            TypeRef.Definition(
                marshalerAssembly,
                "System.Runtime.InteropServices.JavaScript",
                "JSMarshalerArgument",
                trustedFrameworkAssembly));
        ImmutableArray<DirectCall> wrongAssemblyCalls =
        [
            .. bodyIndex.DirectCalls.Select(call =>
                call.EvidenceMethod.MetadataToken
                        == candidate.WrapperMethodToken
                    ? call with
                    {
                        EvidenceMethod = call.EvidenceMethod with
                        {
                            ParameterTypes = [wrongAssemblyArgument],
                        },
                    }
                    : call),
        ];
        LibraryBodyIndex wrongAssemblyIndex =
            LibraryBodyIndex.FromEvidence(
                bodyIndex.Methods,
                [],
                diagnostics: bodyIndex.Diagnostics,
                directCalls: wrongAssemblyCalls,
                resultSinks: bodyIndex.ResultSinks);

        Assert.Single(
            JsExportSurfaceBuilder.Build(
                extracted,
                bodyIndex).Functions);
        UnsupportedJsExportSurfaceException exception =
            Assert.Throws<UnsupportedJsExportSurfaceException>(
                () => JsExportSurfaceBuilder.Build(
                    extracted,
                    wrongAssemblyIndex));
        Assert.Contains(
            "no compiler-generated runtime wrapper",
            exception.Message,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void Build_RejectsRuntimeRegistrationWithUntrustedCoreAlias(
        int parameterIndex)
    {
        string path =
            typeof(PopulateExports).Assembly.Location;
        ApiSurface extracted = ExtractApiSurface(path);
        ApiType exports = Assert.Single(
            extracted.Types,
            type => type.Name == nameof(PopulateExports));
        extracted.FilteredRuntimeJsExportFacts = [];
        extracted.Types = [exports];
        LibraryBodyIndex bodyIndex =
            OpenWireContractBodyIndex(path);
        ImmutableArray<DirectCall> untrustedCalls =
        [
            .. bodyIndex.DirectCalls.Select(call =>
                call.Callee.Name == "BindManagedFunction"
                    ? call with
                    {
                        Callee = call.Callee with
                        {
                            ParameterTypes =
                                ReplaceRegistrationCoreParameter(
                                    call.Callee.ParameterTypes,
                                    parameterIndex),
                        },
                    }
                    : call),
        ];
        LibraryBodyIndex untrustedIndex =
            LibraryBodyIndex.FromEvidence(
                bodyIndex.Methods,
                [],
                diagnostics: bodyIndex.Diagnostics,
                directCalls: untrustedCalls,
                resultSinks: bodyIndex.ResultSinks);

        Assert.Single(
            JsExportSurfaceBuilder.Build(
                extracted,
                bodyIndex).Functions);
        UnsupportedJsExportSurfaceException exception =
            Assert.Throws<UnsupportedJsExportSurfaceException>(
                () => JsExportSurfaceBuilder.Build(
                    extracted,
                    untrustedIndex));
        Assert.Contains(
            "no compiler-generated runtime wrapper",
            exception.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Build_RejectsRuntimeWrapperWithUntrustedCoreVoid()
    {
        string path =
            typeof(PopulateExports).Assembly.Location;
        ApiSurface extracted = ExtractApiSurface(path);
        ApiType exports = Assert.Single(
            extracted.Types,
            type => type.Name == nameof(PopulateExports));
        ApiMember export = Assert.Single(
            exports.Members,
            member => member.Name == nameof(PopulateExports.CountValues));
        extracted.FilteredRuntimeJsExportFacts = [];
        extracted.Types = [exports];
        LibraryBodyIndex bodyIndex =
            OpenWireContractBodyIndex(path);
        RuntimeJsExportWrapperCandidate candidate =
            Assert.Single(
                export.RuntimeJsExportWrapperCandidates!);
        TypeRef untrustedVoid = TypeRef.Definition(
            "System.Runtime",
            "System",
            "Void",
            trustedFrameworkAssembly: false);
        ImmutableArray<DirectCall> untrustedCalls =
        [
            .. bodyIndex.DirectCalls.Select(call =>
                call.EvidenceMethod.MetadataToken
                        == candidate.WrapperMethodToken
                    ? call with
                    {
                        EvidenceMethod = call.EvidenceMethod with
                        {
                            ReturnType = untrustedVoid,
                        },
                    }
                    : call),
        ];
        LibraryBodyIndex untrustedIndex =
            LibraryBodyIndex.FromEvidence(
                bodyIndex.Methods,
                [],
                diagnostics: bodyIndex.Diagnostics,
                directCalls: untrustedCalls,
                resultSinks: bodyIndex.ResultSinks);

        Assert.Single(
            JsExportSurfaceBuilder.Build(
                extracted,
                bodyIndex).Functions);
        UnsupportedJsExportSurfaceException exception =
            Assert.Throws<UnsupportedJsExportSurfaceException>(
                () => JsExportSurfaceBuilder.Build(
                    extracted,
                    untrustedIndex));
        Assert.Contains(
            "no compiler-generated runtime wrapper",
            exception.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Build_RejectsSecondRuntimeBindingTargetWithDifferentHash()
    {
        string path =
            typeof(PopulateExports).Assembly.Location;
        ApiSurface extracted = ExtractApiSurface(path);
        ApiType exports = Assert.Single(
            extracted.Types,
            type => type.Name
                == nameof(PopulateExports));
        ApiMember export = Assert.Single(
            exports.Members,
            member => member.Name
                == nameof(PopulateExports.CountValues));
        extracted.FilteredRuntimeJsExportFacts = [];
        extracted.Types = [exports];
        LibraryBodyIndex bodyIndex =
            OpenWireContractBodyIndex(path);
        RuntimeJsExportWrapperCandidate candidate =
            Assert.Single(
                export.RuntimeJsExportWrapperCandidates!);
        Assert.True(candidate.RegistrationCount > 1);
        DirectCall targetCall = Assert.Single(
            bodyIndex.DirectCalls
                .Where(call =>
                    call.EvidenceMethod.MetadataToken
                        == candidate.RegistrationMethodToken
                    && call.FirstArgumentStringLiteral?.EndsWith(
                        $":{export.Name}",
                        StringComparison.Ordinal)
                        == true));
        string target = targetCall.FirstArgumentStringLiteral!;
        int targetHash = Assert.IsType<int>(
            targetCall.ResolvedArgumentValues[1]
                .Single!
                .Int32Value);
        DirectCall decoy = Assert.Single(
            bodyIndex.DirectCalls
                .Where(call =>
                    call.EvidenceMethod.MetadataToken
                        == candidate.RegistrationMethodToken
                    && call.Callee.Name == "BindManagedFunction"
                    && call.FirstArgumentStringLiteral is not null
                    && call.ResolvedArgumentValues[1].Single
                        is
                        {
                            Kind:
                                ResolvedValueSourceKind.Int32Literal,
                            Int32Value: { } hash,
                        }
                    && hash != targetHash)
                .Take(1));
        ImmutableArray<DirectCall> duplicatedCalls =
        [
            .. bodyIndex.DirectCalls.Select(call =>
                call.EvidenceMethod.MetadataToken
                        == decoy.EvidenceMethod.MetadataToken
                    && call.ILOffset == decoy.ILOffset
                    ? call with
                    {
                        FirstArgumentStringLiteral = target,
                    }
                    : call),
        ];
        LibraryBodyIndex reconstructedIndex =
            LibraryBodyIndex.FromEvidence(
                bodyIndex.Methods,
                [],
                diagnostics: bodyIndex.Diagnostics,
                directCalls: bodyIndex.DirectCalls,
                resultSinks: bodyIndex.ResultSinks);
        LibraryBodyIndex duplicatedIndex =
            LibraryBodyIndex.FromEvidence(
                bodyIndex.Methods,
                [],
                diagnostics: bodyIndex.Diagnostics,
                directCalls: duplicatedCalls,
                resultSinks: bodyIndex.ResultSinks);

        Assert.Single(
            JsExportSurfaceBuilder.Build(
                extracted,
                reconstructedIndex).Functions);
        UnsupportedJsExportSurfaceException exception =
            Assert.Throws<UnsupportedJsExportSurfaceException>(
                () => JsExportSurfaceBuilder.Build(
                    extracted,
                    duplicatedIndex));
        Assert.Contains(
            "no compiler-generated runtime wrapper",
            exception.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Build_DoesNotCreditPrefixSiblingWrapper()
    {
        string path =
            typeof(WrapperPrefixCollisionFixture)
                .Assembly.Location;
        ApiSurface extracted = ExtractApiSurface(path);
        ApiType fixture = Assert.Single(
            extracted.Types,
            type => type.Name
                == nameof(WrapperPrefixCollisionFixture));
        ApiMember foo = Assert.Single(
            fixture.Members,
            member => member.Name
                == nameof(WrapperPrefixCollisionFixture.Foo));
        ApiMember fooBar = Assert.Single(
            fixture.Members,
            member => member.Name == "Foo_Bar");
        extracted.FilteredRuntimeJsExportFacts = [];
        extracted.Types = [fixture];
        LibraryBodyIndex bodyIndex =
            OpenWireContractBodyIndex(path);

        Assert.False(
            foo.HasRuntimeJsExportWrapperCandidate);
        Assert.True(
            fooBar.HasRuntimeJsExportWrapperCandidate);
        UnsupportedJsExportSurfaceException exception =
            Assert.Throws<UnsupportedJsExportSurfaceException>(
                () => JsExportSurfaceBuilder.Build(
                    extracted,
                    bodyIndex));
        Assert.Contains(
            "no compiler-generated runtime wrapper",
            exception.Message,
            StringComparison.Ordinal);

        fixture.Members = [fooBar];
        ILInspector.JsExportSurface.JsExportSurface
            accepted = JsExportSurfaceBuilder.Build(
                extracted,
                bodyIndex);
        Assert.Equal("Foo_Bar", Assert.Single(
            accepted.Functions).Name);
    }

    [Fact]
    public void Build_ProjectsRuntimeQualifiedDeclaringTypePath()
    {
        string path =
            typeof(WrapperPrefixCollisionFixture)
                .Assembly.Location;
        ApiSurface extracted = ExtractApiSurface(path);
        ApiType fixture = Assert.Single(
            extracted.Types,
            type => type.Name
                == nameof(WrapperPrefixCollisionFixture));
        fixture.Members =
        [
            Assert.Single(
                fixture.Members,
                member => member.Name == "Foo_Bar"),
        ];
        extracted.FilteredRuntimeJsExportFacts = [];
        extracted.Types = [fixture];

        JsExportFunction function = Assert.Single(
            JsExportSurfaceBuilder.Build(
                extracted,
                OpenWireContractBodyIndex(path))
                .Functions);
        Assert.Equal(
            "ILInspector.JsExportSurface.PublishabilityFixtures"
                + ".WrapperPrefixCollisionFixture",
            function.DeclaringType);
    }

    [Fact]
    public void Build_ProjectsDistinctRuntimeDispatchKeysForCompiledOverloads()
    {
        string path = typeof(OverloadedExportFixture).Assembly.Location;
        ApiSurface extracted = ExtractApiSurface(path);
        ApiType fixture = Assert.Single(
            extracted.Types,
            type => type.Name == nameof(OverloadedExportFixture));
        fixture.Members =
        [
            .. fixture.Members.Where(
                member => member.Name
                    == nameof(OverloadedExportFixture.Identify)),
        ];
        extracted.FilteredRuntimeJsExportFacts = [];
        extracted.Types = [fixture];
        LibraryBodyIndex bodyIndex = OpenWireContractBodyIndex(path);

        JsExportFunction[] functions =
        [
            .. JsExportSurfaceBuilder.Build(
                extracted,
                bodyIndex)
                .Functions,
        ];

        Assert.Equal(2, functions.Length);
        Assert.Equal(
            2,
            functions
                .Select(function => function.RuntimeDispatchKey)
                .Distinct(StringComparer.Ordinal)
                .Count());

        DirectCall[] registrations =
        [
            .. bodyIndex.DirectCalls
                .Where(call =>
                    call.Callee.Name == "BindManagedFunction"
                    && call.FirstArgumentStringLiteral?.EndsWith(
                        ":Identify",
                        StringComparison.Ordinal) == true),
        ];
        string[] registrationKeys =
        [
            .. registrations
                .Select(call =>
                    "Identify."
                    + Assert.IsType<int>(
                        call.ResolvedArgumentValues[1]
                            .Single!
                            .Int32Value)
                        .ToString(CultureInfo.InvariantCulture))
                .Order(StringComparer.Ordinal),
        ];
        Assert.Equal(
            registrationKeys,
            functions
                .Select(function => function.RuntimeDispatchKey!)
                .Order(StringComparer.Ordinal));

        var runtimeExports =
            new Dictionary<string, MethodInfo>(
                StringComparer.Ordinal);
        foreach (RuntimeJsExportWrapperCandidate candidate
            in fixture.Members
                .SelectMany(member =>
                    member.RuntimeJsExportWrapperCandidates!)
                .DistinctBy(candidate =>
                    candidate.WrapperMethodToken))
        {
            MethodIdentity wrapper = Assert.Single(
                bodyIndex.Methods,
                method => method.MetadataToken
                    == candidate.WrapperMethodToken);
            Assert.True(
                RuntimeJsExportWrapperName.TryGetSignatureHash(
                    wrapper.Name,
                    nameof(OverloadedExportFixture.Identify),
                    out uint wrapperHash));
            DirectCall registration = Assert.Single(
                registrations,
                call => call.ResolvedArgumentValues[1].Single
                    is
                    {
                        Kind:
                            ResolvedValueSourceKind.Int32Literal,
                        Int32Value: { } hash,
                    }
                    && unchecked((uint)hash) == wrapperHash);
            DirectCall wrapperCall = Assert.Single(
                bodyIndex.DirectCalls,
                call => call.EvidenceMethod.MetadataToken
                        == wrapper.MetadataToken
                    && call.Callee.Name.StartsWith(
                        $"<{wrapper.Name}>g____Stub|",
                        StringComparison.Ordinal));
            DirectCall exportCall = Assert.Single(
                bodyIndex.DirectCalls,
                call => call.EvidenceMethod.MetadataToken
                        == wrapperCall.CalleeDefinitionToken
                    && call.Callee.DeclaringType.Name
                        == nameof(OverloadedExportFixture)
                    && call.Callee.Name
                        == nameof(OverloadedExportFixture.Identify));
            MethodInfo implementation = Assert.IsAssignableFrom<MethodInfo>(
                typeof(OverloadedExportFixture).Module.ResolveMethod(
                    exportCall.CalleeDefinitionToken));
            int signatureHash = Assert.IsType<int>(
                registration.ResolvedArgumentValues[1]
                    .Single!
                    .Int32Value);
            runtimeExports.Add(
                "Identify."
                    + signatureHash.ToString(
                        CultureInfo.InvariantCulture),
                implementation);
        }
        JsExportFunction intFunction = Assert.Single(
            functions,
            function => function.Parameters is
            [
                {
                    Type: "int",
                },
            ]);
        JsExportFunction stringFunction = Assert.Single(
            functions,
            function => function.Parameters is
            [
                {
                    Type: "string",
                },
            ]);
        Assert.Equal(
            "int:7",
            Assert.IsType<string>(
                runtimeExports[intFunction.RuntimeDispatchKey!]
                    .Invoke(null, [7])));
        Assert.Equal(
            "string:seven",
            Assert.IsType<string>(
                runtimeExports[stringFunction.RuntimeDispatchKey!]
                    .Invoke(null, ["seven"])));

        string json = JsonSerializer.Serialize(
            new ILInspector.JsExportSurface.JsExportSurface
            {
                AssemblyIdentity = extracted.AssemblyIdentity,
                Functions = functions,
            });
        Assert.All(
            functions,
            function => Assert.Contains(
                $"\"RuntimeDispatchKey\":\"{function.RuntimeDispatchKey}\"",
                json,
                StringComparison.Ordinal));
    }

    [Fact]
    public void Build_PreservesNegativeRuntimeDispatchKeyLiteral()
    {
        string path = typeof(OverloadedExportFixture).Assembly.Location;
        ApiSurface extracted = ExtractApiSurface(path);
        ApiType fixture = Assert.Single(
            extracted.Types,
            type => type.Name == nameof(OverloadedExportFixture));
        fixture.Members =
        [
            .. fixture.Members.Where(
                member => member.Name
                    == nameof(OverloadedExportFixture.Identify)),
        ];
        extracted.FilteredRuntimeJsExportFacts = [];
        extracted.Types = [fixture];
        LibraryBodyIndex bodyIndex = OpenWireContractBodyIndex(path);
        JsExportFunction intFunction = Assert.Single(
            JsExportSurfaceBuilder.Build(
                extracted,
                bodyIndex).Functions,
            function => function.Parameters is
            [
                {
                    Type: "int",
                },
            ]);
        int originalSignatureHash = int.Parse(
            intFunction.RuntimeDispatchKey!.AsSpan(
                "Identify.".Length),
            CultureInfo.InvariantCulture);
        ApiMember intExport = Assert.Single(
            fixture.Members,
            member => member.SignatureModel?.Parameters is
            [
                {
                    Type: "int",
                },
            ]);
        RuntimeJsExportWrapperCandidate candidate = Assert.Single(
            intExport.RuntimeJsExportWrapperCandidates!,
            candidate =>
            {
                MethodIdentity method = Assert.Single(
                    bodyIndex.Methods,
                    method => method.MetadataToken
                        == candidate.WrapperMethodToken);
                Assert.True(
                    RuntimeJsExportWrapperName.TryGetSignatureHash(
                        method.Name,
                        nameof(OverloadedExportFixture.Identify),
                        out uint wrapperHash));
                return wrapperHash
                    == unchecked((uint)originalSignatureHash);
            });
        MethodIdentity wrapper = Assert.Single(
            bodyIndex.Methods,
            method => method.MetadataToken
                == candidate.WrapperMethodToken);
        DirectCall wrapperCall = Assert.Single(
            bodyIndex.DirectCalls,
            call => call.EvidenceMethod.MetadataToken
                    == wrapper.MetadataToken
                && call.Callee.Name.StartsWith(
                    $"<{wrapper.Name}>g____Stub|",
                    StringComparison.Ordinal));
        MethodIdentity stub = Assert.Single(
            bodyIndex.Methods,
            method => method.MetadataToken
                == wrapperCall.CalleeDefinitionToken);
        DirectCall registration = Assert.Single(
            bodyIndex.DirectCalls,
            call => call.EvidenceMethod.MetadataToken
                    == candidate.RegistrationMethodToken
                && call.Callee.Name == "BindManagedFunction"
                && call.FirstArgumentStringLiteral?.EndsWith(
                    ":Identify",
                    StringComparison.Ordinal) == true
                && call.ResolvedArgumentValues[1].Single
                    is
                    {
                        Kind:
                            ResolvedValueSourceKind.Int32Literal,
                        Int32Value: { } signatureHash,
                    }
                && signatureHash == originalSignatureHash);

        const uint unsignedSignatureHash = uint.MaxValue;
        const int signedSignatureHash = -1;
        Assert.Equal(
            unsignedSignatureHash,
            unchecked((uint)signedSignatureHash));
        string wrapperName =
            $"__Wrapper_Identify_{unsignedSignatureHash}";
        MethodIdentity rewrittenWrapper = wrapper with
        {
            Name = wrapperName,
        };
        MethodIdentity rewrittenStub = stub with
        {
            Name = stub.Name.Replace(
                $"<{wrapper.Name}>",
                $"<{wrapperName}>",
                StringComparison.Ordinal),
        };
        ResolvedValueSource hashSource = Assert.IsType<
            ResolvedValueSource>(
                registration.ResolvedArgumentValues[1].Single);
        var rewrittenArguments = new ResolvedValueSets(
        [
            registration.ResolvedArgumentValues[0],
            new ResolvedValueSet(
                [
                    hashSource with
                    {
                        Int32Value = signedSignatureHash,
                    },
                ],
                isResolved: true),
            registration.ResolvedArgumentValues[2],
        ]);
        ImmutableArray<MethodIdentity> methods =
        [
            .. bodyIndex.Methods.Select(method =>
                method.MetadataToken == wrapper.MetadataToken
                    ? rewrittenWrapper
                    : method.MetadataToken == stub.MetadataToken
                        ? rewrittenStub
                        : method),
        ];
        ImmutableArray<DirectCall> calls =
        [
            .. bodyIndex.DirectCalls.Select(call =>
                call.EvidenceMethod.MetadataToken
                        == registration.EvidenceMethod.MetadataToken
                    && call.ILOffset == registration.ILOffset
                    ? call with
                    {
                        ResolvedArgumentValues =
                            rewrittenArguments,
                    }
                    : call.EvidenceMethod.MetadataToken
                            == wrapper.MetadataToken
                        ? call with
                        {
                            Caller = rewrittenWrapper,
                            EvidenceMethod = rewrittenWrapper,
                        }
                        : call.EvidenceMethod.MetadataToken
                                == stub.MetadataToken
                            ? call with
                            {
                                Caller = rewrittenStub,
                                EvidenceMethod = rewrittenStub,
                            }
                            : call),
        ];
        LibraryBodyIndex rewrittenIndex =
            LibraryBodyIndex.FromEvidence(
                methods,
                [],
                diagnostics: bodyIndex.Diagnostics,
                directCalls: calls,
                resultSinks: bodyIndex.ResultSinks);

        JsExportFunction rewrittenFunction = Assert.Single(
            JsExportSurfaceBuilder.Build(
                extracted,
                rewrittenIndex).Functions,
            function => function.Parameters is
            [
                {
                    Type: "int",
                },
            ]);
        Assert.Equal(
            $"Identify.{signedSignatureHash}",
            rewrittenFunction.RuntimeDispatchKey);
        string json = JsonSerializer.Serialize(
            new ILInspector.JsExportSurface.JsExportSurface
            {
                AssemblyIdentity = extracted.AssemblyIdentity,
                Functions = [rewrittenFunction],
            });
        Assert.Contains(
            $"\"RuntimeDispatchKey\":\"Identify.{signedSignatureHash}\"",
            json,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Build_DoesNotBorrowAnotherOverloadWrapperRegistration()
    {
        string path = typeof(OverloadedExportFixture).Assembly.Location;
        ApiSurface extracted = ExtractApiSurface(path);
        ApiType fixture = Assert.Single(
            extracted.Types,
            type => type.Name == nameof(OverloadedExportFixture));
        ApiMember[] overloads =
        [
            .. fixture.Members.Where(
                member => member.Name
                    == nameof(OverloadedExportFixture.Identify)),
        ];
        Assert.Equal(2, overloads.Length);
        extracted.FilteredRuntimeJsExportFacts = [];
        extracted.Types = [fixture];
        LibraryBodyIndex bodyIndex = OpenWireContractBodyIndex(path);

        JsExportFunction[] accepted =
        [
            .. JsExportSurfaceBuilder.Build(
                extracted,
                bodyIndex)
                .Functions,
        ];
        Assert.Equal(2, accepted.Length);
        JsExportFunction intFunction = Assert.Single(
            accepted,
            function => function.Parameters is
            [
                {
                    Type: "int",
                },
            ]);
        uint intHash = unchecked((uint)int.Parse(
            intFunction.RuntimeDispatchKey!.AsSpan(
                "Identify.".Length),
            CultureInfo.InvariantCulture));
        RuntimeJsExportWrapperCandidate wrongCandidate = Assert.Single(
            overloads[0].RuntimeJsExportWrapperCandidates!,
            candidate =>
            {
                MethodIdentity wrapper = Assert.Single(
                    bodyIndex.Methods,
                    method => method.MetadataToken
                        == candidate.WrapperMethodToken);
                Assert.True(
                    RuntimeJsExportWrapperName.TryGetSignatureHash(
                        wrapper.Name,
                        nameof(OverloadedExportFixture.Identify),
                        out uint candidateHash));
                return candidateHash != intHash;
            });
        ApiMember intExport = Assert.Single(
            overloads,
            member => member.SignatureModel?.Parameters is
            [
                {
                    Type: "int",
                },
            ]);
        intExport.RuntimeJsExportWrapperCandidates = [wrongCandidate];
        fixture.Members = [.. overloads];

        UnsupportedJsExportSurfaceException exception =
            Assert.Throws<UnsupportedJsExportSurfaceException>(
                () => JsExportSurfaceBuilder.Build(
                    extracted,
                    bodyIndex));
        Assert.Contains(
            "no compiler-generated runtime wrapper",
            exception.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Build_RejectsUnmatchedRuntimeBindingForOverloadGroup()
    {
        string path = typeof(OverloadedExportFixture).Assembly.Location;
        ApiSurface extracted = ExtractApiSurface(path);
        ApiType fixture = Assert.Single(
            extracted.Types,
            type => type.Name == nameof(OverloadedExportFixture));
        fixture.Members =
        [
            .. fixture.Members.Where(
                member => member.Name
                    == nameof(OverloadedExportFixture.Identify)),
        ];
        Assert.Equal(2, fixture.Members.Count);
        extracted.FilteredRuntimeJsExportFacts = [];
        extracted.Types = [fixture];
        LibraryBodyIndex bodyIndex = OpenWireContractBodyIndex(path);
        RuntimeJsExportWrapperCandidate[] candidates =
        [
            .. fixture.Members[0].RuntimeJsExportWrapperCandidates!,
        ];
        Assert.NotEmpty(candidates);
        int registrationToken = candidates[0].RegistrationMethodToken;
        Assert.All(
            candidates,
            candidate => Assert.Equal(
                registrationToken,
                candidate.RegistrationMethodToken));
        DirectCall[] targetCalls =
        [
            .. bodyIndex.DirectCalls.Where(call =>
                call.EvidenceMethod.MetadataToken == registrationToken
                && call.Callee.Name == "BindManagedFunction"
                && call.FirstArgumentStringLiteral?.EndsWith(
                    ":Identify",
                    StringComparison.Ordinal) == true),
        ];
        Assert.Equal(2, targetCalls.Length);
        string target = Assert.Single(
            targetCalls
                .Select(call => call.FirstArgumentStringLiteral!)
                .Distinct(StringComparer.Ordinal));
        HashSet<int> targetHashes =
        [
            .. targetCalls.Select(call =>
                Assert.IsType<int>(
                    call.ResolvedArgumentValues[1]
                        .Single!
                        .Int32Value)),
        ];
        DirectCall decoy = bodyIndex.DirectCalls.First(call =>
            call.EvidenceMethod.MetadataToken == registrationToken
            && call.Callee.Name == "BindManagedFunction"
            && call.FirstArgumentStringLiteral is not null
            && call.ResolvedArgumentValues[1].Single
                is
                {
                    Kind: ResolvedValueSourceKind.Int32Literal,
                    Int32Value: { } hash,
                }
            && !targetHashes.Contains(hash));
        ImmutableArray<DirectCall> calls =
        [
            .. bodyIndex.DirectCalls.Select(call =>
                call.EvidenceMethod.MetadataToken
                        == decoy.EvidenceMethod.MetadataToken
                    && call.ILOffset == decoy.ILOffset
                    ? call with
                    {
                        FirstArgumentStringLiteral = target,
                    }
                    : call),
        ];
        LibraryBodyIndex tamperedIndex =
            LibraryBodyIndex.FromEvidence(
                bodyIndex.Methods,
                [],
                diagnostics: bodyIndex.Diagnostics,
                directCalls: calls,
                resultSinks: bodyIndex.ResultSinks);

        UnsupportedJsExportSurfaceException exception =
            Assert.Throws<UnsupportedJsExportSurfaceException>(
                () => JsExportSurfaceBuilder.Build(
                    extracted,
                    tamperedIndex));
        Assert.Contains(
            "no compiler-generated runtime wrapper",
            exception.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Build_ProjectsNestedRuntimeDeclaringTypePath()
    {
        string path =
            typeof(NestedExportContainer.NestedExports)
                .Assembly.Location;
        ApiSurface extracted = ExtractApiSurface(path);
        ApiType fixture = Assert.Single(
            extracted.Types,
            type => type.Name
                == "NestedExportContainer.NestedExports");
        extracted.FilteredRuntimeJsExportFacts = [];
        extracted.Types = [fixture];

        JsExportFunction function = Assert.Single(
            JsExportSurfaceBuilder.Build(
                extracted,
                OpenWireContractBodyIndex(path))
                .Functions);
        Assert.Equal(
            "ILInspector.JsExportSurface.PublishabilityFixtures"
                + ".NestedExportContainer.NestedExports",
            function.DeclaringType);
    }

    [Fact]
    public void Extract_RetainsFilteredJsExportRowsFromCompilerGeneratedTypes()
    {
        string path = typeof(LambdaExportFixture).Assembly.Location;
        ApiSurface apiSurface = ExtractApiSurface(path);

        FilteredRuntimeJsExportFact fact = Assert.Single(
            apiSurface.FilteredRuntimeJsExportFacts);
        Assert.StartsWith(
            "<Create>b__",
            fact.MethodName,
            StringComparison.Ordinal);
        Assert.Equal(1, fact.AttributeCount);
        Assert.True(fact.HasValidRow);
        Assert.False(fact.HasMalformedRow);
        Assert.DoesNotContain(
            apiSurface.Types,
            type => type.Name.StartsWith("<", StringComparison.Ordinal));

        apiSurface.Types = [];
        UnsupportedJsExportSurfaceException exception =
            Assert.Throws<UnsupportedJsExportSurfaceException>(
                () => JsExportSurfaceBuilder.Build(apiSurface));
        Assert.Contains(
            "filtered MethodDefs",
            exception.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Build_RejectsReachedHandwrittenSerializerContextImplementation()
    {
        string path =
            typeof(HandwrittenContextExports).Assembly.Location;
        ApiSurface apiSurface = ExtractApiSurface(path);
        ApiType context = Assert.Single(
            apiSurface.Types,
            type => type.Name == nameof(HandwrittenJsonContext));
        ApiType generatedContext = Assert.Single(
            ExtractFixtureApiSurface().Types,
            type => type.Name == nameof(FixtureJsonContext));

        Assert.False(context.HasSystemTextJsonSourceGenerationMarker);
        Assert.True(
            generatedContext.HasSystemTextJsonSourceGenerationMarker);

        apiSurface.FilteredRuntimeJsExportFacts = [];
        apiSurface.Types =
        [
            Assert.Single(
                apiSurface.Types,
                type => type.Name == nameof(HandwrittenPayload)),
            context,
            Assert.Single(
                apiSurface.Types,
                type => type.Name == nameof(HandwrittenContextExports)),
        ];
        LibraryBodyIndex bodyIndex = OpenWireContractBodyIndex(path);
        context.HasSystemTextJsonSourceGenerationMarker = true;

        UnsupportedJsExportSurfaceException exception =
            Assert.Throws<UnsupportedJsExportSurfaceException>(
                () => JsExportSurfaceBuilder.Build(apiSurface, bodyIndex));
        Assert.Contains(
            "no authentic source-generated implementation",
            exception.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Build_RejectsGeneratedRootGetterWithoutTrustedBodyFlow()
    {
        (
            ApiSurface apiSurface,
            ApiType context,
            ApiMember rootProperty,
            LibraryBodyIndex bodyIndex) =
                ExtractSupportedScalarVectorSurface();
        TypeRef untrustedOptions = TypeRef.Definition(
            "System.Text.Json",
            "System.Text.Json.Serialization",
            "JsonSerializerContext",
            trustedFrameworkAssembly: false);
        ImmutableArray<DirectCall> untrustedCalls =
        [
            .. bodyIndex.DirectCalls.Select(call =>
                call.EvidenceMethod.MetadataToken
                        == rootProperty.GetterToken
                    && call.Callee.Name == "get_Options"
                    ? call with
                    {
                        Callee = call.Callee with
                        {
                            DeclaringType = untrustedOptions,
                        },
                    }
                    : call),
        ];
        LibraryBodyIndex untrustedIndex =
            LibraryBodyIndex.FromEvidence(
                bodyIndex.Methods,
                [],
                diagnostics: bodyIndex.Diagnostics,
                directCalls: untrustedCalls,
                resultSinks: bodyIndex.ResultSinks);

        Assert.Equal(
            "int[]",
            Assert.Single(
                JsExportSurfaceBuilder.Build(
                    apiSurface,
                    bodyIndex).Functions)
                .ReturnWireType);
        UnsupportedJsExportSurfaceException exception =
            Assert.Throws<UnsupportedJsExportSurfaceException>(
                () => JsExportSurfaceBuilder.Build(
                    apiSurface,
                    untrustedIndex));
        Assert.Contains(
            "no authentic source-generated implementation",
            exception.Message,
            StringComparison.Ordinal);
        Assert.NotNull(context.DefinitionName);
    }

    [Fact]
    public void Build_RejectsGeneratedContextWithoutTrustedDefaultInitialization()
    {
        (
            ApiSurface apiSurface,
            ApiType context,
            _,
            LibraryBodyIndex bodyIndex) =
                ExtractSupportedScalarVectorSurface();
        MethodIdentity staticConstructor = Assert.Single(
            bodyIndex.Methods,
            method => method.Name == ".cctor"
                && method.DeclaringType.Resolution?.Type
                    == context.DefinitionName);
        TypeRef untrustedOptions = TypeRef.Definition(
            "System.Text.Json",
            "System.Text.Json",
            "JsonSerializerOptions",
            trustedFrameworkAssembly: false);
        ImmutableArray<DirectCall> untrustedCalls =
        [
            .. bodyIndex.DirectCalls.Select(call =>
                call.EvidenceMethod.MetadataToken
                        == staticConstructor.MetadataToken
                    && call.Kind == CallKind.NewObject
                    && call.Callee.DeclaringType.Name
                        == "JsonSerializerOptions"
                    && call.Callee.ParameterTypes.Length == 1
                    && call.Callee.ParameterTypes[0].Name
                        == "JsonSerializerOptions"
                    ? call with
                    {
                        Callee = call.Callee with
                        {
                            DeclaringType = untrustedOptions,
                        },
                    }
                    : call),
        ];
        LibraryBodyIndex untrustedIndex =
            LibraryBodyIndex.FromEvidence(
                bodyIndex.Methods,
                [],
                diagnostics: bodyIndex.Diagnostics,
                directCalls: untrustedCalls,
                resultSinks: bodyIndex.ResultSinks);

        Assert.Equal(
            "int[]",
            Assert.Single(
                JsExportSurfaceBuilder.Build(
                    apiSurface,
                    bodyIndex).Functions)
                .ReturnWireType);
        UnsupportedJsExportSurfaceException exception =
            Assert.Throws<UnsupportedJsExportSurfaceException>(
                () => JsExportSurfaceBuilder.Build(
                    apiSurface,
                    untrustedIndex));
        Assert.Contains(
            "no authentic source-generated implementation",
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
        string[] publishabilityMethodNames = ReadMethodNames(
            typeof(BodylessInterfaceExportFixture).Assembly.Location);

        Assert.Contains(
            ordinaryMethodNames,
            name => name.StartsWith(
                "__Wrapper_SerializeWriteAsStringInt_",
                StringComparison.Ordinal));
        ApiSurface ordinarySurface = ExtractApiSurface(
            typeof(ScalarContextOptionsFixtureExports)
                .Assembly.Location);
        Assert.True(
            Assert.Single(
                Assert.Single(
                    ordinarySurface.Types,
                    type => type.Name
                        == nameof(
                            ScalarContextOptionsFixtureExports))
                    .Members,
                member => member.Name
                    == nameof(
                        ScalarContextOptionsFixtureExports
                            .SerializeWriteAsStringInt))
                .HasRuntimeJsExportWrapperCandidate);
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
        Assert.Contains(
            publishabilityMethodNames,
            name => name.StartsWith(
                "__Wrapper_GetPayload_",
                StringComparison.Ordinal));
        Assert.DoesNotContain(
            publishabilityMethodNames,
            name => name.StartsWith(
                "__Wrapper_Compute_",
                StringComparison.Ordinal));
        Assert.DoesNotContain(
            publishabilityMethodNames,
            name => name.Contains(
                    "Create",
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
        string path =
            typeof(PopulateExports).Assembly.Location;
        ApiSurface apiSurface = ExtractApiSurface(path);
        ApiType exports = Assert.Single(
            apiSurface.Types,
            type => type.Name
                == nameof(PopulateExports));
        ApiMember export = Assert.Single(
            exports.Members,
            member => member.Name
                == nameof(PopulateExports.CountValues));
        int exportToken = Assert.IsType<int>(
            export.MetadataToken);
        const int diagnosticToken = 0x0600FFFF;
        apiSurface.FilteredRuntimeJsExportFacts = [];
        apiSurface.Types = [exports];
        LibraryBodyIndex authenticIndex =
            OpenWireContractBodyIndex(path);
        var diagnostic = new AnalysisDiagnostic(
            diagnosticToken,
            "Exports.Failed",
            "BadImageFormatException: invalid body",
            SourceMethodToken:
                sourceAttributed ? exportToken : null);
        LibraryBodyIndex bodyIndex = LibraryBodyIndex.FromEvidence(
            authenticIndex.Methods,
            [],
            diagnostics: [diagnostic],
            directCalls: authenticIndex.DirectCalls,
            resultSinks: authenticIndex.ResultSinks);

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

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void Build_RejectsDiagnosedRuntimeWrapperChain(
        bool diagnoseStub,
        bool sourceAttributed)
    {
        string path =
            typeof(WrapperPrefixCollisionFixture)
                .Assembly.Location;
        ApiSurface apiSurface = ExtractApiSurface(path);
        ApiType fixture = Assert.Single(
            apiSurface.Types,
            type => type.Name
                == nameof(WrapperPrefixCollisionFixture));
        ApiMember export = Assert.Single(
            fixture.Members,
            member => member.Name == "Foo_Bar");
        fixture.Members = [export];
        apiSurface.FilteredRuntimeJsExportFacts = [];
        apiSurface.Types = [fixture];
        LibraryBodyIndex bodyIndex =
            OpenWireContractBodyIndex(path);
        MethodIdentity wrapper = Assert.Single(
            bodyIndex.Methods,
            method => method.DeclaringType.Name
                    == nameof(WrapperPrefixCollisionFixture)
                && RuntimeJsExportWrapperName.IsCandidateFor(
                    method.Name,
                    export.Name));
        DirectCall wrapperCall = Assert.Single(
            bodyIndex.DirectCalls,
            call => call.EvidenceMethod.MetadataToken
                    == wrapper.MetadataToken
                && call.Callee.Name.StartsWith(
                    $"<{wrapper.Name}>g____Stub|",
                    StringComparison.Ordinal));
        int diagnosedToken = diagnoseStub
            ? wrapperCall.CalleeDefinitionToken
            : wrapper.MetadataToken;
        var diagnostic = new AnalysisDiagnostic(
            sourceAttributed ? 0x0600FFFF : diagnosedToken,
            "generated wrapper chain",
            "BadImageFormatException: invalid body",
            SourceMethodToken:
                sourceAttributed ? diagnosedToken : null);
        LibraryBodyIndex diagnosedIndex =
            LibraryBodyIndex.FromEvidence(
                bodyIndex.Methods,
                [],
                diagnostics: [diagnostic],
                directCalls: bodyIndex.DirectCalls,
                resultSinks: bodyIndex.ResultSinks);

        Assert.Single(
            JsExportSurfaceBuilder.Build(
                apiSurface,
                bodyIndex)
                .Functions);
        UnsupportedJsExportSurfaceException exception =
            Assert.Throws<UnsupportedJsExportSurfaceException>(
                () => JsExportSurfaceBuilder.Build(
                    apiSurface,
                    diagnosedIndex));
        Assert.Contains(
            "no compiler-generated runtime wrapper",
            exception.Message,
            StringComparison.Ordinal);
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
        Assert.Equal(22, surface.Records.Count);
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
    public void Build_RejectsContextRelativeJsonIncludeValueTypeAccessibility()
    {
        using FileStream stream = File.OpenRead(
            typeof(NestedContextJsonIncludeHiddenTypeFixture)
                .Assembly.Location);
        using var peReader = new PEReader(stream);
        ApiSurface apiSurface = ApiSurfaceExtractor.Extract(
            peReader,
            includeAll: true);

        UnsupportedJsExportSurfaceException ex =
            Assert.Throws<UnsupportedJsExportSurfaceException>(
                () => JsExportSurfaceBuilder.Build(apiSurface));

        Assert.Contains(
            "[JsonInclude] members whose same-assembly value types depend on nested JsonSerializerContext accessibility are unsupported",
            ex.Message,
            StringComparison.Ordinal);
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

    static void SelectOnlyRuntimeJsExport(
        ApiSurface surface,
        ApiMember selected)
    {
        foreach (ApiType type in surface.Types)
        {
            type.Members =
            [
                .. type.Members.Where(member =>
                    member.HasRuntimeJsExport != true
                    || ReferenceEquals(member, selected)),
            ];
        }
    }

    static int ReplaceAscii(
        byte[] image,
        string oldValue,
        string newValue)
    {
        byte[] oldBytes = Encoding.ASCII.GetBytes(oldValue);
        byte[] newBytes = Encoding.ASCII.GetBytes(newValue);
        Assert.Equal(oldBytes.Length, newBytes.Length);

        int replacements = 0;
        for (int i = 0;
            i <= image.Length - oldBytes.Length;
            i++)
        {
            if (!image
                .AsSpan(i, oldBytes.Length)
                .SequenceEqual(oldBytes))
            {
                continue;
            }

            newBytes.CopyTo(image, i);
            replacements++;
        }

        return replacements;
    }

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

    /// <summary>
    /// TypeScript emission describes every <c>long</c> as <c>number</c>, which
    /// is the wrong type for a JavaScript <c>BigInt</c>. Until descriptor-aware
    /// TypeScript types exist, an authentic
    /// <c>[JSMarshalAs&lt;JSType.BigInt&gt;] long</c> export is rejected
    /// visibly rather than published under a type that misdescribes it.
    /// </summary>
    [Fact]
    public void Build_RejectsBigIntMarshaledLongExport()
    {
        UnsupportedJsExportSurfaceException exception =
            Assert.Throws<UnsupportedJsExportSurfaceException>(
                () => BuildMarshaledLongSurface(
                    nameof(BigIntMarshalFixture),
                    nameof(BigIntMarshalFixture.EchoBigInt)));

        Assert.Contains(
            "recognized but not supported",
            exception.Message,
            StringComparison.Ordinal);
        Assert.Contains(
            "get_BigInt64",
            exception.Message,
            StringComparison.Ordinal);
        Assert.Contains(
            "[JSMarshalAs<JSType.Number>]",
            exception.Message,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// The close negative for the rejection above: the Int52 descriptor is what
    /// <c>number</c> does describe, so the same <c>long</c> keeps publishing.
    /// </summary>
    [Fact]
    public void Build_PublishesNumberMarshaledLongExport()
    {
        JsExportFunction function = Assert.Single(
            BuildMarshaledLongSurface(
                nameof(Int52MarshalFixture),
                nameof(Int52MarshalFixture.EchoInt52)).Functions);

        Assert.Equal(
            nameof(Int52MarshalFixture.EchoInt52),
            function.Name);
    }

    static JsExportSurface BuildMarshaledLongSurface(
        string typeName,
        string exportName)
    {
        string path =
            typeof(BigIntMarshalFixture).Assembly.Location;
        ApiSurface extracted = ExtractApiSurface(path);
        ApiType fixture = Assert.Single(
            extracted.Types,
            type => type.Name == typeName);
        Assert.Contains(
            fixture.Members,
            member => member.Name == exportName);
        extracted.FilteredRuntimeJsExportFacts = [];
        extracted.Types = [fixture];
        return JsExportSurfaceBuilder.Build(
            extracted,
            OpenWireContractBodyIndex(path));
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

    static JsExportSurface BuildWith(
        string path,
        LibraryBodyIndex bodyIndex,
        FieldStoreFact extraStore)
        => JsExportSurfaceBuilder.Build(
            ExtractApiSurface(path),
            LibraryBodyIndex.FromEvidence(
                bodyIndex.Methods,
                [],
                diagnostics: bodyIndex.Diagnostics,
                directCalls: bodyIndex.DirectCalls,
                resultSinks: bodyIndex.ResultSinks,
                fieldStores: [.. bodyIndex.FieldStores, extraStore],
                fieldLoads: bodyIndex.FieldLoads,
                returnFlows: bodyIndex.ReturnFlows));

    static ApiSurface ExtractApiSurface(string path)
    {
        using FileStream stream = File.OpenRead(path);
        using var peReader = new PEReader(stream);
        return ApiSurfaceExtractor.Extract(peReader, includeAll: true);
    }

    static (
        ApiSurface Surface,
        ApiType Context,
        ApiMember RootProperty,
        LibraryBodyIndex BodyIndex)
        ExtractSupportedScalarVectorSurface()
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
        ApiMember rootProperty = Assert.Single(
            context.Members,
            member => member.Name == "Int32Array");
        return (
            apiSurface,
            context,
            rootProperty,
            OpenWireContractBodyIndex(path));
    }

    static ImmutableArray<TypeRef>
        ReplaceRegistrationCoreParameter(
            ImmutableArray<TypeRef> parameters,
            int parameterIndex)
    {
        TypeRef[] replacement = [.. parameters];
        replacement[parameterIndex] = parameterIndex switch
        {
            0 => TypeRef.Definition(
                "System.Runtime",
                "System",
                "String",
                trustedFrameworkAssembly: false),
            1 => TypeRef.Definition(
                "System.Runtime",
                "System",
                "Int32",
                trustedFrameworkAssembly: false),
            2 => TypeRef.GenericInstance(
                TypeRef.Definition(
                    "System.Runtime",
                    "System",
                    "ReadOnlySpan`1",
                    trustedFrameworkAssembly: false),
                parameters[2].TypeArguments),
            _ => throw new ArgumentOutOfRangeException(
                nameof(parameterIndex)),
        };
        return [.. replacement];
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
        nameof(DirectionalSharedInputDto),
        JsonWireDirection.Deserialize)]
    [InlineData(
        nameof(DirectionalAccessorInputDto),
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
    public void Build_RecordsInactiveDiscoveredTypeAsNone()
    {
        string path = typeof(FixtureExports).Assembly.Location;
        var bodyIndex = LibraryBodyIndex.Open(
            path,
            LibraryBodyAnalysisFeatures.MethodEvidence
                | LibraryBodyAnalysisFeatures.JsonWireContractFlow);
        using FileStream stream = File.OpenRead(path);
        using var peReader = new PEReader(stream);
        ILInspector.JsExportSurface.JsExportSurface surface =
            JsExportSurfaceBuilder.Build(
            ApiSurfaceExtractor.Extract(
                peReader,
                includeAll: false),
            bodyIndex);

        ApiType inactive = Assert.Single(
            surface.Records,
            type => type.Name
                == nameof(DirectionalInactiveInputDto));
        Assert.Equal(
            JsonWireDirection.None,
            surface.WireDirections[inactive]);
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
