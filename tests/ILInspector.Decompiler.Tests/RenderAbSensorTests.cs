using ILInspector.Decompiler.Pipeline;
using ILInspector.DecompilerHarness;

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
                shellContext),
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
        }
        finally
        {
            File.Delete(path);
        }
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
