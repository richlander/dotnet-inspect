using ILInspector.Decompiler.Pipeline;
using ILInspector.DecompilerHarness;
using ILInspector.Research;

namespace ILInspector.Decompiler.Tests;

[Collection(ConsoleMutatorCollection.Name)]
public class RenderAbSensorTests
{
    static readonly object ConsoleGate = new();

    [Fact]
    public void RenderAbUsesCrossMethodImportForGenericTypeLocalFunctions()
    {
        var type = typeof(GenericTypeLocalFunctionSamples<>);
        using var source = MetadataSource.Open(type.Assembly.Location);
        var function = IrImporter.Import(
            source, type.FullName!, nameof(GenericTypeLocalFunctionSamples<int>.NoTypeParameter));
        Assert.NotNull(function);

        string? output = RenderAbSensor.Render(source, function!);

        Assert.Contains("return Own(value);", output);
        Assert.Contains("static int Own(int input) => input + 1;", output);
        Assert.DoesNotContain("_g__Own_", output);
    }

    [Theory]
    [InlineData(typeof(HeterogeneousArmSample), nameof(HeterogeneousArmSample.GuardedArea))]
    [InlineData(typeof(HeterogeneousArmSample), nameof(HeterogeneousArmSample.Area))]
    [InlineData(typeof(PatternSwitchSample), nameof(PatternSwitchSample.Classify))]
    public void RenderAbMatchesMetadataBackedProductProjection(Type type, string methodName)
    {
        using var source = MetadataSource.Open(type.Assembly.Location);
        var expectedFunction = IrImporter.Import(source, type.FullName!, methodName);
        var actualFunction = IrImporter.Import(source, type.FullName!, methodName);
        Assert.NotNull(expectedFunction);
        Assert.NotNull(actualFunction);

        string? expected = CSharpPrinter.PrintRaised(
            expectedFunction!,
            method => IrImporter.Import(source, method),
            typesProvablyDisjoint: source.AreProvablyDisjoint).Output;
        string? actual = RenderAbSensor.Render(source, actualFunction!);

        Assert.Contains("switch", expected);
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void RenderAbSemanticLane_CatchesParseValidCompileInvalidRegression()
    {
        const string key = "fixture.dll!T::M()";
        var function = SyntheticFunction();
        var shellContext = ValidityCheck.MethodShellContext.Create(
            function,
            requiresUnsafeContext: false);
        var baseline = new Dictionary<string, RenderAbSensor.BaselineMethod>(StringComparer.Ordinal)
        {
            [key] = new("return 1;", shellContext),
        };
        var current = new Dictionary<string, RenderAbSensor.RenderedMethod>(StringComparer.Ordinal)
        {
            [key] = new(
                "T",
                "M",
                "()",
                typeof(RenderAbSensorTests).Assembly.Location,
                "fixture.dll",
                "return 1++;",
                shellContext,
                new RenderAbSensor.SemanticContext(
                    "T",
                    "M",
                    function,
                    new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal),
                    ProductParameterList: null)),
        };

        string output = CaptureConsole(() => RenderAbSensor.Compare(baseline, current, maxExamples: 5), expectedExitCode: 2);

        Assert.Contains("Changed: 1", output);
        Assert.Contains("Semantic: valid->valid: 0, invalid->valid: 0, valid->invalid: 1, invalid->invalid: 0", output);
        Assert.Contains("==== Semantic Regressions (valid->invalid) ====", output);
        Assert.Contains("return 1++;", output);
    }

    [Fact]
    public void RenderAbSemanticLane_ReportsUnavailableWithoutCompilation()
    {
        const string key = "unsupported.dll!T::M()";
        var function = SyntheticFunction();
        var shellContext = ValidityCheck.MethodShellContext.Create(
            function,
            requiresUnsafeContext: false);
        var baseline = new Dictionary<string, RenderAbSensor.BaselineMethod>(
            StringComparer.Ordinal)
        {
            [key] = new("return 1;", shellContext),
        };
        var current = new Dictionary<string, RenderAbSensor.RenderedMethod>(
            StringComparer.Ordinal)
        {
            [key] = new(
                "T",
                "M",
                "()",
                "/path/that/must/not/be-opened.dll",
                "unsupported.dll",
                "",
                shellContext,
                CompileBackUnavailableReason:
                    "module memory-safety rules are Unsupported"),
        };

        string output = CaptureConsole(
            () => RenderAbSensor.Compare(
                baseline,
                current,
                maxExamples: 5),
            expectedExitCode: 1);

        Assert.Contains("Compile-back unavailable: 1", output);
        Assert.Contains("unavailable: 1", output);
        Assert.Contains("semantic unavailable", output);
    }

    [Fact]
    public void RenderAbSemanticLane_BindsCrossMethodRaisedBodies()
    {
        var type = typeof(UnraisedLocalFunctionSamples);
        string assemblyPath = type.Assembly.Location;
        string methodName = nameof(UnraisedLocalFunctionSamples.CallsRaisedIf);
        using var source = MetadataSource.Open(assemblyPath);
        var function = IrImporter.Import(source, type.FullName!, methodName);
        Assert.NotNull(function);

        string before = RenderAbSensor.Render(source, function!)!.Trim();
        Assert.Equal(DecompilationFidelity.Full, function!.Fidelity);
        var shellContext = ValidityCheck.MethodShellContext.Create(
            function,
            requiresUnsafeContext: false);
        var sample = new RenderAbSensor.RenderedMethod(
            type.FullName!,
            methodName,
            CorpusMethodIdentity.SignatureText(function.Signature),
            assemblyPath,
            CorpusSensor.PortablePath(assemblyPath),
            "return \"bad\";",
            shellContext);
        var baseline = new Dictionary<string, RenderAbSensor.BaselineMethod>(StringComparer.Ordinal)
        {
            [sample.Key] = new(before, shellContext),
        };
        var current = new Dictionary<string, RenderAbSensor.RenderedMethod>(StringComparer.Ordinal)
        {
            [sample.Key] = sample,
        };

        string output = CaptureConsole(
            () => RenderAbSensor.Compare(baseline, current, maxExamples: 5),
            expectedExitCode: 2);

        Assert.Contains(
            "Semantic: valid->valid: 0, invalid->valid: 0, valid->invalid: 1, invalid->invalid: 0",
            output);
        Assert.Contains("CS0029", output);
    }

    [Fact]
    public void RenderAbSemanticLane_UsesEachSidesDeclarationContext()
    {
        const string key = "fixture.dll!T::M()";
        var function = SyntheticAsyncFunction();
        var semanticContext = new RenderAbSensor.SemanticContext(
            "T",
            "M",
            function,
            new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal),
            ProductParameterList: null);
        var baseline = new Dictionary<string, RenderAbSensor.BaselineMethod>(StringComparer.Ordinal)
        {
            [key] = new(
                """
                int value = 0;
                int* pointer = &value;
                _ = *pointer;
                """,
                new ValidityCheck.MethodShellContext(
                    RequiresAsyncContext: true,
                    RequiresUnsafeContext: true,
                    HasAwaitSyntax: false)),
        };
        var current = new Dictionary<string, RenderAbSensor.RenderedMethod>(StringComparer.Ordinal)
        {
            [key] = new(
                "T",
                "M",
                "()",
                typeof(RenderAbSensorTests).Assembly.Location,
                "fixture.dll",
                """
                int value = 0;
                int* pointer = &value;
                _ = *pointer;
                await Task.Yield();
                """,
                new ValidityCheck.MethodShellContext(
                    RequiresAsyncContext: true,
                    RequiresUnsafeContext: false,
                    HasAwaitSyntax: true),
                semanticContext),
        };

        string output = CaptureConsole(
            () => RenderAbSensor.Compare(baseline, current, maxExamples: 5),
            expectedExitCode: 2);

        Assert.Contains(
            "Semantic: valid->valid: 0, invalid->valid: 0, valid->invalid: 1, invalid->invalid: 0",
            output);
        Assert.Contains("CS0214", output);
    }

    [Fact]
    public void RenderAbBaseline_RoundTripsDeclarationContext()
    {
        const string key = "fixture.dll!T::M()";
        var shellContext = new ValidityCheck.MethodShellContext(
            RequiresAsyncContext: true,
            RequiresUnsafeContext: true,
            HasAwaitSyntax: false);
        var renders = new Dictionary<string, RenderAbSensor.RenderedMethod>(StringComparer.Ordinal)
        {
            [key] = new(
                "T",
                "M",
                "()",
                typeof(RenderAbSensorTests).Assembly.Location,
                "fixture.dll",
                "return;",
                shellContext,
                SourceDocument: StructuralDocument("return;")),
        };
        string path = Path.GetTempFileName();

        try
        {
            var artifact = RenderAbSensor.CreateBaseline(renders);
            File.WriteAllText(path, System.Text.Json.JsonSerializer.Serialize(artifact));

            var loaded = RenderAbSensor.LoadBaseline(path);

            Assert.NotNull(loaded);
            Assert.Equal(shellContext, loaded.Methods[key].ShellContext);
            Assert.Equal("return;", loaded.Methods[key].Body);
            Assert.Equal(
                renders[key].SourceDocument,
                loaded.Methods[key].SourceDocument);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void RenderAbBaseline_CarriesProductStructuralProjections()
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            $"render-ab-baseline-{Guid.NewGuid():N}.json");

        try
        {
            int exitCode = RenderAbSensor.Run(
                [typeof(RenderAbSensorTests).Assembly.Location],
                diffPath: null,
                emitPath: path,
                maxExamples: 5,
                methodCap: 10,
                workers: null,
                sequential: true);

            Assert.Equal(0, exitCode);
            var baseline = RenderAbSensor.LoadBaseline(path);
            Assert.NotNull(baseline);
            Assert.NotEmpty(baseline.Methods);
            Assert.All(
                baseline.Methods.Values,
                static method =>
                {
                    var document = Assert.IsType<AnnotatedSourceDocument>(
                        method.SourceDocument);
                    Assert.NotNull(document.Source);
                    Assert.All(
                        document.Nodes,
                        static node => Assert.Equal(
                            SourceLineKind.CSharp,
                            node.Medium));
                    Assert.Empty(document.Facts);
                    Assert.Empty(document.Targets);
                    Assert.Equal(
                        method.Body,
                        CSharpStructuralDiffDocument
                            .Create(document, document)
                            .Before
                            .Text
                            .Trim());
                });
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void RenderAbBaseline_AcceptsTriviaOnlyProjectionDifference()
    {
        const string key = "fixture.dll!T::M()";
        var renders = new Dictionary<string, RenderAbSensor.RenderedMethod>(
            StringComparer.Ordinal)
        {
            [key] = new(
                "T",
                "M",
                "()",
                typeof(RenderAbSensorTests).Assembly.Location,
                "fixture.dll",
                "int value = 1;\n    \nreturn value;",
                ValidityCheck.MethodShellContext.Create(
                    SyntheticFunction(),
                    requiresUnsafeContext: false),
                SourceDocument: StructuralDocument(
                    "int value = 1;\n\nreturn value;")),
        };

        var baseline = RenderAbSensor.CreateBaseline(renders);

        Assert.Equal(
            "int value = 1;\n    \nreturn value;",
            baseline.Methods[key].Body);
    }

    [Fact]
    public void RenderAbChangedMethod_EmitsReplayableProductStructuralDiff()
    {
        const string key = "fixture.dll!T::M()";
        var function = SyntheticFunction();
        var shellContext = ValidityCheck.MethodShellContext.Create(
            function,
            requiresUnsafeContext: false);
        var baseline = new Dictionary<string, RenderAbSensor.BaselineMethod>(
            StringComparer.Ordinal)
        {
            [key] = new(
                "return 1;",
                shellContext,
                StructuralDocument("return 1;")),
        };
        var current = new Dictionary<string, RenderAbSensor.RenderedMethod>(
            StringComparer.Ordinal)
        {
            [key] = new(
                "T",
                "M",
                "()",
                typeof(RenderAbSensorTests).Assembly.Location,
                "fixture.dll",
                "return 2;",
                shellContext,
                new RenderAbSensor.SemanticContext(
                    "T",
                    "M",
                    function,
                    new Dictionary<string, Dictionary<string, string>>(
                        StringComparer.Ordinal),
                    ProductParameterList: null),
                StructuralDocument("return 2;")),
        };
        string directory = Path.Combine(
            Path.GetTempPath(),
            $"render-ab-structural-{Guid.NewGuid():N}");

        try
        {
            string output = CaptureConsole(
                () => RenderAbSensor.Compare(
                    baseline,
                    current,
                    maxExamples: 5,
                    structuralDiffDirectory: directory),
                expectedExitCode: 1);

            Assert.Contains(
                "A: stored baseline; B: current product render.",
                output);
            Assert.Contains(
                "Structural review: complete: 1, partial: 0, unavailable: 0",
                output);
            Assert.Contains("# Structural review", output);
            string artifactPath = Assert.Single(
                Directory.GetFiles(
                    directory,
                    "*.structural-diff.json"));
            var artifact = AnnotatedSourceJson.DeserializeStructuralDiff(
                File.ReadAllText(artifactPath));
            Assert.Contains(
                artifact.Rows,
                static row => row.Change == CSharpStructuralChangeKind.Changed);
            Assert.True(artifact.ToComparison().IsCorrespondenceComplete);
            Assert.True(File.Exists(Path.Combine(directory, "manifest.json")));
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void RenderAbChangedCompilerMethod_UsesExactProductDocuments()
    {
        var type = typeof(AuthoredCorpusRatchetTests);
        string methodName =
            nameof(AuthoredCorpusRatchetTests
                .DeepInspect_RunsAuthoredCorpusDailyAndKeepsPackageDiscoveryWeekly);
        int methodToken = type.GetMethod(methodName)!.MetadataToken;
        using var source = MetadataSource.Open(type.Assembly.Location);
        var function = IrImporter.Import(source, methodToken);
        Assert.NotNull(function);
        var baselineDocument = ProductStructuralDocument(
            source,
            type,
            methodName,
            methodToken,
            PrinterOptions.Default);
        var currentDocument = ProductStructuralDocument(
            source,
            type,
            methodName,
            methodToken,
            PrinterOptions.Default with { ReadableLocalNames = true });
        Assert.NotEqual(baselineDocument.Text, currentDocument.Text);

        string signature = CorpusMethodIdentity.SignatureText(
            function!.Signature);
        string portablePath = CorpusSensor.PortablePath(type.Assembly.Location);
        string key =
            $"{portablePath}!{type.FullName}::{methodName}{signature}";
        var shellContext = ValidityCheck.MethodShellContext.Create(
            function,
            requiresUnsafeContext: false);
        var baseline = new Dictionary<string, RenderAbSensor.BaselineMethod>(
            StringComparer.Ordinal)
        {
            [key] = new(
                baselineDocument.Text.Trim(),
                shellContext,
                baselineDocument),
        };
        var current = new Dictionary<string, RenderAbSensor.RenderedMethod>(
            StringComparer.Ordinal)
        {
            [key] = new(
                type.FullName!,
                methodName,
                signature,
                type.Assembly.Location,
                portablePath,
                currentDocument.Text.Trim(),
                shellContext,
                new RenderAbSensor.SemanticContext(
                    type.FullName!,
                    methodName,
                    function,
                    new Dictionary<string, Dictionary<string, string>>(
                        StringComparer.Ordinal),
                    ProductParameterList: null),
                currentDocument),
        };
        string directory = Path.Combine(
            Path.GetTempPath(),
            $"render-ab-structural-{Guid.NewGuid():N}");

        try
        {
            string output = CaptureConsole(
                () => RenderAbSensor.Compare(
                    baseline,
                    current,
                    maxExamples: 5,
                    structuralDiffDirectory: directory),
                expectedExitCode: 1);

            Assert.Contains(
                "Structural review: ",
                output);
            Assert.Contains(
                "unavailable: 0",
                output);
            string artifactPath = Assert.Single(
                Directory.GetFiles(
                    directory,
                    "*.structural-diff.json"));
            var artifact = AnnotatedSourceJson.DeserializeStructuralDiff(
                File.ReadAllText(artifactPath));
            Assert.Equal(baselineDocument, artifact.Correspondence.Before);
            Assert.Equal(currentDocument, artifact.Correspondence.After);
            Assert.Contains(
                artifact.Rows,
                static row => row.Change == CSharpStructuralChangeKind.Changed);
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void RenderAbUnchangedMethod_EmitsNoStructuralDiff()
    {
        const string key = "fixture.dll!T::M()";
        var shellContext = ValidityCheck.MethodShellContext.Create(
            SyntheticFunction(),
            requiresUnsafeContext: false);
        var document = StructuralDocument("return 1;");
        var baseline = new Dictionary<string, RenderAbSensor.BaselineMethod>(
            StringComparer.Ordinal)
        {
            [key] = new("return 1;", shellContext, document),
        };
        var current = new Dictionary<string, RenderAbSensor.RenderedMethod>(
            StringComparer.Ordinal)
        {
            [key] = new(
                "T",
                "M",
                "()",
                typeof(RenderAbSensorTests).Assembly.Location,
                "fixture.dll",
                "return 1;",
                shellContext,
                SourceDocument: document),
        };
        string directory = Path.Combine(
            Path.GetTempPath(),
            $"render-ab-structural-{Guid.NewGuid():N}");

        try
        {
            string output = CaptureConsole(
                () => RenderAbSensor.Compare(
                    baseline,
                    current,
                    maxExamples: 5,
                    structuralDiffDirectory: directory),
                expectedExitCode: 0);

            Assert.Contains("Changed: 0", output);
            Assert.Empty(Directory.GetFiles(
                directory,
                "*.structural-diff.json"));
            Assert.True(File.Exists(Path.Combine(directory, "manifest.json")));
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void RenderAbChangedMethod_RejectsMismatchedPhysicalProvenance()
    {
        const string key = "fixture.dll!T::M()";
        var function = SyntheticFunction();
        var shellContext = ValidityCheck.MethodShellContext.Create(
            function,
            requiresUnsafeContext: false);
        var baseline = new Dictionary<string, RenderAbSensor.BaselineMethod>(
            StringComparer.Ordinal)
        {
            [key] = new(
                "return 1;",
                shellContext,
                StructuralDocument("return 1;")),
        };
        var current = new Dictionary<string, RenderAbSensor.RenderedMethod>(
            StringComparer.Ordinal)
        {
            [key] = new(
                "T",
                "M",
                "()",
                typeof(RenderAbSensorTests).Assembly.Location,
                "fixture.dll",
                "return 2;",
                shellContext,
                new RenderAbSensor.SemanticContext(
                    "T",
                    "M",
                    function,
                    new Dictionary<string, Dictionary<string, string>>(
                        StringComparer.Ordinal),
                    ProductParameterList: null),
                StructuralDocument(
                    "return 2;",
                    moduleVersionId: Guid.Parse(
                        "22222222-2222-2222-2222-222222222222"))),
        };

        string output = CaptureConsole(
            () => RenderAbSensor.Compare(
                baseline,
                current,
                maxExamples: 5),
            expectedExitCode: 2);

        Assert.Contains(
            "Structural review: complete: 0, partial: 0, unavailable: 1",
            output);
        Assert.Contains("Structural review unavailable:", output);
    }

    [Fact]
    public void RenderAbBaseline_RejectsBodyOnlyArtifact()
    {
        string path = Path.GetTempFileName();

        try
        {
            File.WriteAllText(path, """{"fixture.dll!T::M()":"return;"}""");

            lock (ConsoleGate)
            {
                var originalError = Console.Error;
                using var writer = new StringWriter();
                try
                {
                    Console.SetError(writer);
                    Assert.Null(RenderAbSensor.LoadBaseline(path));
                    Assert.Contains("Regenerate it with --emit-render-ab", writer.ToString());
                }
                finally
                {
                    Console.SetError(originalError);
                }
            }
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void RenderAbBaseline_RejectsVersionTwoMethodWithoutProductDocument()
    {
        const string key = "fixture.dll!T::M()";
        string path = Path.GetTempFileName();
        var artifact = new RenderAbSensor.BaselineArtifact(
            2,
            new Dictionary<string, RenderAbSensor.BaselineMethod>(
                StringComparer.Ordinal)
            {
                [key] = new(
                    "return;",
                    new ValidityCheck.MethodShellContext(
                        RequiresAsyncContext: false,
                        RequiresUnsafeContext: false,
                        HasAwaitSyntax: false)),
            });

        try
        {
            File.WriteAllText(
                path,
                System.Text.Json.JsonSerializer.Serialize(artifact));

            lock (ConsoleGate)
            {
                var originalError = Console.Error;
                using var writer = new StringWriter();
                try
                {
                    Console.SetError(writer);
                    Assert.Null(RenderAbSensor.LoadBaseline(path));
                    Assert.Contains(
                        "must carry matching product structural documents",
                        writer.ToString());
                }
                finally
                {
                    Console.SetError(originalError);
                }
            }
        }
        finally
        {
            File.Delete(path);
        }
    }

    static IrFunction SyntheticFunction()
        => new(
            "M",
            TypeRef.CoreLib("Synthetic", "T"),
            new MethodSignature(
                TypeRef.CoreLib("System", "Int32"),
                [],
                HasThis: false,
                GenericParameterCount: 0),
            [],
            new BlockContainer());

    static IrFunction SyntheticAsyncFunction()
        => new(
            "M",
            TypeRef.CoreLib("Synthetic", "T"),
            new MethodSignature(
                TypeRef.CoreLib("System.Threading.Tasks", "Task"),
                [],
                HasThis: false,
                GenericParameterCount: 0),
            [],
            new BlockContainer())
        {
            RequiresAsyncBodyModifier = true,
        };

    static AnnotatedSourceDocument StructuralDocument(
        string text,
        Guid? moduleVersionId = null)
        => new(
            text,
            [
                new AnnotatedSourceNode(
                    0,
                    "ReturnStatement",
                    SourceLineKind.CSharp,
                    [new AnnotatedSourceSpan(0, text.Length)],
                    Provenance: new AnnotatedSourceNodeProvenance([0]))
            ],
            [],
            [],
            [],
            new AnnotatedSourceDocumentSource(
                "Fixture",
                moduleVersionId
                    ?? Guid.Parse("11111111-1111-1111-1111-111111111111"),
                0x06000001,
                new string('A', 64),
                "T.M (0x06000001)"));

    static AnnotatedSourceDocument ProductStructuralDocument(
        MetadataSource source,
        Type type,
        string methodName,
        int methodToken,
        PrinterOptions options)
    {
        var projection = ResearchViews.ProjectMember(
            new ResearchViews.MemberProjectionRequest(
                source,
                type.FullName!,
                methodName,
                Registry: new ResearchFactRegistry(),
                MethodToken: methodToken,
                PrinterOptions: options,
                SourceDocument: true));
        var document = Assert.IsType<AnnotatedSourceDocument>(
            projection.SourceDocument);
        return CSharpStructuralDiffDocument
            .Create(document, document)
            .Before;
    }

    static string CaptureConsole(Func<int> action, int expectedExitCode)
    {
        lock (ConsoleGate)
        {
            var originalOut = Console.Out;
            using var writer = new StringWriter();
            try
            {
                Console.SetOut(writer);
                int exitCode = action();
                Assert.True(
                    exitCode == expectedExitCode,
                    $"Expected exit code {expectedExitCode}, got {exitCode}.{Environment.NewLine}{writer}");
                return writer.ToString();
            }
            finally
            {
                Console.SetOut(originalOut);
            }
        }
    }
}
