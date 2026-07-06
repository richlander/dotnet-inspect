using ILInspector.Decompiler.Pipeline;
using ILInspector.DecompilerHarness;

namespace ILInspector.Decompiler.Tests;

[Collection(ConsoleMutatorCollection.Name)]
public class RenderAbSensorTests
{
    static readonly object ConsoleGate = new();

    [Fact]
    public void RenderAbSemanticLane_CatchesParseValidCompileInvalidRegression()
    {
        const string key = "fixture.dll!T::M()";
        var function = SyntheticFunction();
        var baseline = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [key] = "return 1;",
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
                Assert.Equal(expectedExitCode, exitCode);
                return writer.ToString();
            }
            finally
            {
                Console.SetOut(originalOut);
            }
        }
    }
}
