using System.Net;

using DotnetInspector.Fixtures;
using DotnetInspector.Packages;
using DotnetInspector.Services;
using ILInspector.Decompiler.Tests;
using ILInspector.Findings;
using ILInspector.Metadata;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Emit;

namespace ILInspector.DecompilerHarness;

[Trait("Area", "Fidelity")]
[Collection(FidelityGateCollection.Name)]
public sealed class AuthoredRebuildFidelityTests
{
    static readonly FindingSubject Subject = new("test", "test");

    [Fact]
    public void BuildContextAssessment_KeepsDeterminismSeparateFromRecordedContext()
    {
        string runtimePath = typeof(object).Assembly.Location;
        var assessment = AuthoredRebuildFidelity.AssessBuildContext(
            isDeterministic: false,
            CompleteOptions(
                new CompilationOptionInfo("optimization", "release"),
                new CompilationOptionInfo("unsafe", "true")),
            CompleteReferences(new CompilationReferenceInfo(
                Path.GetFileName(runtimePath),
                Aliases: "",
                CompilationReferenceImageKind.Assembly,
                EmbedInteropTypes: false,
                Timestamp: 0,
                ImageSize: 0,
                ModuleVersionId: Guid.Empty)),
            [MetadataReference.CreateFromFile(runtimePath)]);

        Assert.Equal(AuthoredBuildContextStatus.Recorded, assessment.Status);
        Assert.False(assessment.IsDeterministic);
    }

    [Fact]
    public void BuildContextAssessment_ReportsContextDriftIndependently()
    {
        var assessment = AuthoredRebuildFidelity.AssessBuildContext(
            isDeterministic: true,
            CompleteOptions(new CompilationOptionInfo("optimization", "debug")),
            CompleteReferences(new CompilationReferenceInfo(
                "Missing.Reference.dll",
                Aliases: "",
                CompilationReferenceImageKind.Assembly,
                EmbedInteropTypes: false,
                Timestamp: 0,
                ImageSize: 0,
                ModuleVersionId: Guid.Empty)),
            []);

        Assert.Equal(AuthoredBuildContextStatus.Drift, assessment.Status);
        Assert.True(assessment.IsDeterministic);
        Assert.Contains("optimization=debug", assessment.Detail, StringComparison.Ordinal);
        Assert.Contains("Missing.Reference.dll", assessment.Detail, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Speed", "Slow")]
    public void AuthoredBody_ReusesFinalRtsRequestAndProductIlDiff()
    {
        var decompiler = ReturnToSender.CompileBackFirstPropertyGetter(
            FixtureCatalog.DiffPair.OldAssemblyPath());
        var context = new AuthoredBuildContextAssessment(
            AuthoredBuildContextStatus.Incomplete,
            IsDeterministic: true,
            "test context");

        var result = AuthoredRebuildFidelity.CompileAuthoredBody(
            decompiler,
            decompiler.TargetBody,
            SourceChecksumVerification.Exact,
            context);

        Assert.True(
            result.Outcome is AuthoredRebuildOutcome.Exact or AuthoredRebuildOutcome.IlDifferent,
            result.Detail);
        Assert.NotNull(result.ImplementationDiff);
        Assert.Equal(SourceChecksumVerification.Exact, result.ChecksumVerification);
        Assert.Equal(decompiler, result.DecompilerLane);
    }

    [Fact]
    public void AuthoredBody_ReusesFrozenRtsCompilationClosure()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            $"authored-rts-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            string dependencyPath = CompileFixture(
                "namespace D; public sealed class Before { }",
                directory,
                "RtsAuthoredDependency");
            string assemblyPath = CompileFixture(
                "public sealed class Fixture { public D.Before Value => null; }",
                directory,
                "fixture",
                MetadataReference.CreateFromFile(dependencyPath));
            ReturnToSender.Result decompiler =
                ReturnToSender.CompileBackFirstPropertyGetter(
                    assemblyPath);

            CompileFixture(
                "namespace D; public sealed class After { }",
                directory,
                "RtsAuthoredDependency");
            var context = new AuthoredBuildContextAssessment(
                AuthoredBuildContextStatus.Incomplete,
                IsDeterministic: true,
                "test context");

            AuthoredRebuildFidelityResult result =
                AuthoredRebuildFidelity.CompileAuthoredBody(
                    decompiler,
                    decompiler.TargetBody,
                    SourceChecksumVerification.Exact,
                    context);

            Assert.True(
                result.Outcome is AuthoredRebuildOutcome.Exact
                    or AuthoredRebuildOutcome.IlDifferent,
                result.Detail);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Theory]
    [InlineData("public int Value { get { return 1; } }", "get_Value", "return 1;")]
    [InlineData("public int Value => 2;", "get_Value", "return 2;")]
    [InlineData("public int M() { return 3; }", "M", "return 3;")]
    [InlineData("int IFoo.Value { get { return 4; } }", "Sample.IFoo.get_Value", "return 4;")]
    [InlineData("int IFoo.M() { return 5; }", "Sample.IFoo.M", "return 5;")]
    [InlineData("public static bool operator ==(__AuthoredSourceHost left, __AuthoredSourceHost right) => true;", "op_Equality", "return true;")]
    [InlineData("public static implicit operator int(__AuthoredSourceHost value) => 6;", "op_Implicit", "return 6;")]
    [InlineData("public int this[int index] => index;", "get_Item", "return index;")]
    [InlineData("[System.Runtime.CompilerServices.IndexerName(\"Custom\")] public int this[int index] => index;", "get_Custom", "return index;")]
    public void AuthoredMemberSource_ExtractsRtsTargetBody(
        string memberSource,
        string methodName,
        string expected)
    {
        Assert.True(AuthoredRebuildFidelity.TryExtractTargetBody(
            memberSource,
            methodName,
            out string body));
        Assert.Contains(expected, body, StringComparison.Ordinal);
    }

    [Fact]
    public void AuthoredMemberSource_DoesNotUseDifferentPropertyBody()
    {
        Assert.False(AuthoredRebuildFidelity.TryExtractTargetBody(
            "public int Value { get { return 1; } }",
            "get_Other",
            out _));
    }

    [Fact]
    public void AuthoredMemberSource_FindsTargetAfterNeighboringProperty()
    {
        Assert.True(AuthoredRebuildFidelity.TryExtractTargetBody(
            "public int Other { get { return 1; } } public int Value { get { return 2; } }",
            "get_Value",
            out string body));
        Assert.Contains("return 2;", body, StringComparison.Ordinal);
    }

    [Fact]
    public void AuthoredMemberSource_DistinguishesExplicitInterfaceMethod()
    {
        Assert.True(AuthoredRebuildFidelity.TryExtractTargetBody(
            "public int M() { return 1; } int IFoo.M() { return 2; }",
            "Sample.IFoo.M",
            out string body));
        Assert.Contains("return 2;", body, StringComparison.Ordinal);
    }

    [Fact]
    public void AuthoredMemberSource_PrefersQualifiedExplicitInterfaceMethod()
    {
        Assert.True(AuthoredRebuildFidelity.TryExtractTargetBody(
            "int I1.M() { return 1; } int N.I1.M() { return 2; }",
            "N.I1.M",
            out string body));
        Assert.Contains("return 2;", body, StringComparison.Ordinal);
    }

    [Fact]
    public void AuthoredMemberSource_MatchesGenericExplicitInterfaceProperty()
    {
        Assert.True(AuthoredRebuildFidelity.TryExtractTargetBody(
            "int ICustom<int>.Value { get { return 3; } }",
            "ICustom<System.Int32>.get_Value",
            out string body));
        Assert.Contains("return 3;", body, StringComparison.Ordinal);
    }

    [Fact]
    public void AuthoredMemberSource_DistinguishesConstructedGenericInterfaces()
    {
        Assert.True(AuthoredRebuildFidelity.TryExtractTargetBody(
            "int ICustom<int>.Value { get { return 1; } } "
                + "int ICustom<string>.Value { get { return 2; } }",
            "ICustom<System.String>.get_Value",
            out string body));
        Assert.Contains("return 2;", body, StringComparison.Ordinal);
    }

    [Fact]
    public void AuthoredMemberSource_AllowsUnresolvedNamespaceAlias()
    {
        Assert.True(AuthoredRebuildFidelity.TryExtractTargetBody(
            "int Alias::I1.Value { get { return 4; } }",
            "N.I1.get_Value",
            out string body));
        Assert.Contains("return 4;", body, StringComparison.Ordinal);
    }

    [Fact]
    public void AuthoredMemberSource_ExtractsIndexerBody()
    {
        Assert.True(AuthoredRebuildFidelity.TryExtractTargetBody(
            "public int this[int index] { get { return index; } }",
            "get_Item",
            out string body));
        Assert.Contains("return index;", body, StringComparison.Ordinal);
    }

    [Fact]
    public void AuthoredMemberSource_ExpressionBodiedIndexerIsNotBodyless()
    {
        const string source = "public int this[int index] => index;";

        Assert.True(AuthoredRebuildFidelity.TryExtractTargetBodies(
            source,
            "get_Item",
            expectedParameterCount: 1,
            out string body,
            out string? printerBody));
        Assert.Equal("return index;", body);
        Assert.Null(printerBody);
        Assert.False(AuthoredRebuildFidelity.IsBodylessTarget(
            source,
            "get_Item",
            expectedParameterCount: 1));
    }

    [Theory]
    [InlineData("void operator +=(int value) { }", "op_AdditionAssignment")]
    [InlineData("void operator -=(int value) { }", "op_SubtractionAssignment")]
    [InlineData("void operator *=(int value) { }", "op_MultiplyAssignment")]
    [InlineData("void operator /=(int value) { }", "op_DivisionAssignment")]
    [InlineData("void operator %=(int value) { }", "op_ModulusAssignment")]
    [InlineData("void operator &=(int value) { }", "op_BitwiseAndAssignment")]
    [InlineData("void operator |=(int value) { }", "op_BitwiseOrAssignment")]
    [InlineData("void operator ^=(int value) { }", "op_ExclusiveOrAssignment")]
    [InlineData("void operator <<=(int value) { }", "op_LeftShiftAssignment")]
    [InlineData("void operator >>=(int value) { }", "op_RightShiftAssignment")]
    [InlineData("void operator >>>=(int value) { }", "op_UnsignedRightShiftAssignment")]
    [InlineData("void operator ++() { }", "op_IncrementAssignment")]
    [InlineData("void operator --() { }", "op_DecrementAssignment")]
    [InlineData("void operator checked +=(int value) { }", "op_CheckedAdditionAssignment")]
    [InlineData("void operator checked ++() { }", "op_CheckedIncrementAssignment")]
    public void AuthoredMemberSource_MapsAssignmentOperatorMetadataNames(
        string source,
        string expected)
    {
        CancellationToken cancellationToken =
            TestContext.Current.CancellationToken;
        var root = CSharpSyntaxTree.ParseText(
                $"class C {{ {source} }}",
                new CSharpParseOptions(LanguageVersion.Preview),
                cancellationToken: cancellationToken)
            .GetCompilationUnitRoot(cancellationToken);
        var declaration =
            Assert.Single(root.DescendantNodes().OfType<OperatorDeclarationSyntax>());

        Assert.Equal(
            expected,
            CSharpSourceIdentityContext.OperatorMetadataName(declaration));
    }

    [Fact]
    public void AuthoredMemberSource_RejectsEqualRankGenericFallbacks()
    {
        Assert.False(AuthoredRebuildFidelity.TryExtractTargetBody(
            "int ICustom<(int, string)>.Value { get { return 1; } } "
                + "int ICustom<(int, int)>.Value { get { return 2; } }",
            "ICustom<System.ValueTuple<System.Byte,System.Byte>>.get_Value",
            out _));
    }

    [Fact]
    public void AuthoredMemberSource_RejectsMultipleConstructorsWithoutArity()
    {
        Assert.False(AuthoredRebuildFidelity.TryExtractTargetBody(
            "public Sample() { } public Sample(int value) { }",
            ".ctor",
            out _));
    }

    [Fact]
    public void AuthoredMemberSource_UsesConstructorArity()
    {
        Assert.True(AuthoredRebuildFidelity.TryExtractTargetBody(
            "public Sample() { Value = 1; } "
                + "public Sample(int value) { Value = value; }",
            ".ctor",
            expectedParameterCount: 1,
            out string body));
        Assert.Contains("Value = value;", body, StringComparison.Ordinal);
    }

    [Fact]
    public void AuthoredMemberSource_UsesMethodArity()
    {
        Assert.True(AuthoredRebuildFidelity.TryExtractTargetBody(
            "public int M() { return 1; } "
                + "public int M(int value) { return value; }",
            "M",
            expectedParameterCount: 0,
            out string body));
        Assert.Contains("return 1;", body, StringComparison.Ordinal);
    }

    [Fact]
    public void AuthoredMemberSource_PreservesCanonicalBlockTextForPrinterComparison()
    {
        const string source = """
            public int M()
            {
                if (Value)
                {
                    return 1;
                }

                return 2;
            }
            """;

        Assert.True(AuthoredRebuildFidelity.TryExtractTargetBodies(
            source,
            "M",
            expectedParameterCount: 0,
            out _,
            out string? printerBody));
        Assert.Equal(
            """
            if (Value)
            {
                return 1;
            }

            return 2;
            """,
            printerBody);
    }

    [Fact]
    public void AuthoredMemberSource_DoesNotProjectExpressionBodyAsPrinterExact()
    {
        Assert.True(AuthoredRebuildFidelity.TryExtractTargetBodies(
            "public int M() => 1;",
            "M",
            expectedParameterCount: 0,
            out string body,
            out string? printerBody));

        Assert.Equal("return 1;", body);
        Assert.Null(printerBody);
    }

    [Fact]
    public void AuthoredMemberSource_BodylessAccessorIsNotExtractable()
    {
        Assert.False(AuthoredRebuildFidelity.TryExtractTargetBodies(
            "public int Value { get; }",
            "get_Value",
            expectedParameterCount: 0,
            out _,
            out _));
        Assert.True(AuthoredRebuildFidelity.IsBodylessTarget(
            "public int Value { get; }",
            "get_Value",
            expectedParameterCount: 0));
    }

    [Fact]
    public void AuthoredMemberSource_EmptyBlockIsARealBody()
    {
        Assert.True(AuthoredRebuildFidelity.TryExtractTargetBodies(
            "public void M() { }",
            "M",
            expectedParameterCount: 0,
            out string body,
            out _));
        Assert.Equal("", body);
        Assert.False(AuthoredRebuildFidelity.IsBodylessTarget(
            "public void M() { }",
            "M",
            expectedParameterCount: 0));
    }

    [Fact]
    public void AuthoredMemberSource_RemovesSingleLineBlockEnvelope()
    {
        Assert.True(AuthoredRebuildFidelity.TryExtractTargetBodies(
            "public int M() { return 1; }",
            "M",
            expectedParameterCount: 0,
            out _,
            out string? printerBody));

        Assert.Equal("return 1;", printerBody);
    }

    [Theory]
    [InlineData("""
        public string M()
        {
            return @"first
        second";
        }
        """)]
    [InlineData("""
        public int M()
        {
        #if FEATURE
            return 1;
        #else
            return 0;
        #endif
        }
        """)]
    public void AuthoredMemberSource_DeclinesNonMechanicalPrinterProjection(
        string source)
    {
        Assert.True(AuthoredRebuildFidelity.TryExtractTargetBodies(
            source,
            "M",
            expectedParameterCount: 0,
            out string body,
            out string? printerBody));

        Assert.NotEmpty(body);
        Assert.Null(printerBody);
    }

    [Theory]
    [InlineData(0x0085)]
    [InlineData(0x2028)]
    [InlineData(0x2029)]
    public void AuthoredMemberSource_DeclinesUnicodeMultilineToken(
        int separator)
    {
        string source =
            $"public string M() {{ return @\"first{(char)separator}second\"; }}";

        Assert.True(AuthoredRebuildFidelity.TryExtractTargetBodies(
            source,
            "M",
            expectedParameterCount: 0,
            out string body,
            out string? printerBody));

        Assert.NotEmpty(body);
        Assert.Null(printerBody);
    }

    [Fact]
    public void SourceCorrespondenceAcquisition_PreservesAbsentAndFailedOutcomes()
    {
        var target = new ReturnToSenderSourceProbe.ProbeTarget(
            new ReturnToSender.RequestedTarget("Sample.Widget", "M", 0),
            ExpectedFragments: [],
            MetadataToken: 0x06000001,
            ParameterCount: 0);
        var absent = new PdbMemberSourceInspection(
            new FindingInspection<string>(
                new FindingInspection<string>.Absent(
                    FindingInspectionAbsenceKind.NoApplicableInput,
                    "no source mapping")),
            Text: null,
            Mapping: null,
            Document: null,
            ChecksumVerification: null);
        var failed = PdbSourceAcquisition.MemberPdbAcquisitionFailed(
            Subject,
            new IOException("source fetch failed"));

        ReturnToSenderSourceProbe.SourceAcquisitionAttempt absentAttempt =
            ReturnToSenderSourceProbe.CreateSourceAcquisition(target, absent);
        ReturnToSenderSourceProbe.SourceAcquisitionAttempt failedAttempt =
            ReturnToSenderSourceProbe.CreateSourceAcquisition(target, failed);

        Assert.Equal(SourceAcquisitionOutcome.Absent, absentAttempt.Outcome);
        Assert.Equal("no source mapping", absentAttempt.Detail);
        Assert.Equal(SourceAcquisitionOutcome.Failed, failedAttempt.Outcome);
        Assert.Contains("source fetch failed", failedAttempt.Detail, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound, false, "No matching portable PDB")]
    [InlineData(HttpStatusCode.InternalServerError, true, "sources did not answer")]
    [InlineData(HttpStatusCode.OK, true, "invalid or mismatched")]
    public async Task SourceCorrespondencePdbAcquisition_DistinguishesAbsenceFromFailure(
        HttpStatusCode statusCode,
        bool expectedFailure,
        string expectedDetail)
    {
        NuGetCache.Initialize("dotnet-inspect");
        var fixture = CompilePortablePdbFixture();
        try
        {
            using var httpClient =
                new HttpClient(new StaticStatusHandler(statusCode));
            var results =
                await ReturnToSenderSourceProbe.EvaluateSourceCorrespondenceAsync(
                    [fixture.AssemblyPath],
                    cap: 1,
                    httpClient,
                    new SourceFetcher(httpClient));

            ReturnToSenderSourceProbeResult result = Assert.Single(results);
            SourceAcquisitionOutcome expected = expectedFailure
                ? SourceAcquisitionOutcome.Failed
                : SourceAcquisitionOutcome.Absent;
            Assert.True(
                result.SourceAcquisition == expected,
                $"Expected {expected}, got {result.SourceAcquisition}: "
                    + result.SourceAcquisitionDetail);
            Assert.Contains(
                expectedDetail,
                result.SourceAcquisitionDetail,
                StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(fixture.Directory, recursive: true);
        }
    }

    [Fact]
    public async Task SourceCorrespondencePdbAcquisition_AdaptsCustomExpressionBodiedIndexer()
    {
        var fixture = CompilePortablePdbFixture(
            """
            public sealed class Fixture
            {
                [System.Runtime.CompilerServices.IndexerName("Custom")]
                public int this[int index] => index;
            }
            """,
            deletePdb: false);
        try
        {
            var handler = new StaticStatusHandler(HttpStatusCode.NotFound);
            using var httpClient = new HttpClient(handler);
            var results =
                await ReturnToSenderSourceProbe.EvaluateSourceCorrespondenceAsync(
                    [fixture.AssemblyPath],
                    cap: 1,
                    httpClient,
                    new SourceFetcher(httpClient));

            ReturnToSenderSourceProbeResult result = Assert.Single(results);
            Assert.True(
                result.SourceAcquisition == SourceAcquisitionOutcome.Complete,
                $"{result.SourceAcquisition}: {result.SourceAcquisitionDetail}; "
                    + $"source={result.SourcePath}");
            Assert.Equal("get_Custom", result.Target.Method);
            Assert.Contains(
                "return index;",
                result.ExpectedBody,
                StringComparison.Ordinal);
            Assert.NotEqual("valid_match.source_bodyless", result.Reason);
            Assert.Equal(0, handler.RequestCount);
        }
        finally
        {
            Directory.Delete(fixture.Directory, recursive: true);
        }
    }

    [Fact]
    public void SourceCorrespondenceAcquisition_InfersGlobalPackageCoordinate()
    {
        string packageRoot = NuGetCache.GetNuGetCachePath();
        string assemblyPath = Path.Combine(
            packageRoot,
            "example.package",
            "1.2.3",
            "lib",
            "net10.0",
            "Example.Package.dll");

        ReturnToSenderSourceProbe.NuGetPackageCoordinate? package =
            ReturnToSenderSourceProbe.TryGetNuGetPackageCoordinate(assemblyPath);

        Assert.NotNull(package);
        Assert.Equal("example.package", package.Id);
        Assert.Equal("1.2.3", package.Version);
        Assert.Null(ReturnToSenderSourceProbe.TryGetNuGetPackageCoordinate(
            Path.Combine(Path.GetTempPath(), "Example.Package.dll")));
    }

    [Theory]
    [InlineData(".ctor", "Value = 1;")]
    [InlineData(".cctor", "Value = 2;")]
    public void AuthoredMemberSource_DistinguishesConstructorKind(
        string methodName,
        string expected)
    {
        Assert.True(AuthoredRebuildFidelity.TryExtractTargetBody(
            "public Sample() { Value = 1; } "
                + "static Sample() { Value = 2; }",
            methodName,
            expectedParameterCount: 0,
            out string body));
        Assert.Contains(expected, body, StringComparison.Ordinal);
    }

    static FindingInspection<CompilationOptionInfo> CompleteOptions(
        params CompilationOptionInfo[] options)
        => MetadataFindings.InspectCompilationOptions(options, Subject);

    static FindingInspection<CompilationReferenceInfo> CompleteReferences(
        params CompilationReferenceInfo[] references)
        => MetadataFindings.InspectCompilationReferences(references, Subject);

    static string CompileFixture(
        string source,
        string directory,
        string assemblyName,
        params MetadataReference[] additionalReferences)
    {
        string path = Path.Combine(directory, assemblyName + ".dll");
        var compilation = CSharpCompilation.Create(
            assemblyName,
            [CSharpSyntaxTree.ParseText(source)],
            RoslynTestReferences.TrustedPlatform
                .Concat(additionalReferences),
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                optimizationLevel: OptimizationLevel.Release));
        using var stream = File.Create(path);
        var emit = compilation.Emit(stream);
        Assert.True(
            emit.Success,
            string.Join(
                Environment.NewLine,
                emit.Diagnostics));
        return path;
    }

    static (string Directory, string AssemblyPath) CompilePortablePdbFixture(
        string? source = null,
        bool deletePdb = true)
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            $"authored-pdb-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        string sourcePath = Path.Combine(directory, "Fixture.cs");
        string assemblyPath = Path.Combine(directory, "Fixture.dll");
        string pdbPath = Path.Combine(directory, "Fixture.pdb");
        source ??=
            "public sealed class Fixture { public int Value => 1; }";
        File.WriteAllText(
            sourcePath,
            source);
        var compilation = CSharpCompilation.Create(
            "Fixture",
            [
                CSharpSyntaxTree.ParseText(
                    Microsoft.CodeAnalysis.Text.SourceText.From(
                        File.ReadAllText(sourcePath),
                        new System.Text.UTF8Encoding(
                            encoderShouldEmitUTF8Identifier: false)),
                    path: sourcePath),
            ],
            RoslynTestReferences.TrustedPlatform,
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                optimizationLevel: OptimizationLevel.Release));
        using (var assembly = File.Create(assemblyPath))
        using (var pdb = File.Create(pdbPath))
        {
            var emit = compilation.Emit(
                assembly,
                pdb,
                options: new EmitOptions(
                    debugInformationFormat: DebugInformationFormat.PortablePdb,
                    pdbFilePath: pdbPath));
            Assert.True(
                emit.Success,
                string.Join(
                    Environment.NewLine,
                    emit.Diagnostics));
        }

        if (deletePdb)
            File.Delete(pdbPath);
        return (directory, assemblyPath);
    }

    sealed class StaticStatusHandler(HttpStatusCode statusCode)
        : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestCount++;
            return Task.FromResult(new HttpResponseMessage(statusCode)
            {
                RequestMessage = request,
            });
        }
    }
}
