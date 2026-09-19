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
}
