using DotnetInspector.RoundTripCompilation;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace ILInspector.Decompiler.Tests;

public sealed class RoundTripCompilationEngineTests
{
    static readonly MetadataReference[] References =
    [
        MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
    ];

    static readonly CSharpParseOptions ParseOptions = new(LanguageVersion.Preview);

    static readonly CSharpCompilationOptions CompilationOptions = new(
        OutputKind.DynamicallyLinkedLibrary,
        optimizationLevel: OptimizationLevel.Release);

    [Fact]
    public void Compile_GrowsArtifactUntilRoslynEmitSucceeds()
    {
        bool includeDependency = false;
        int growthCalls = 0;

        var result = RoundTripCompilationEngine.Compile(
            compose: () => includeDependency
                ? "public class Helper { } public class Target { public Helper M() => new(); }"
                : "public class Target { public Helper M() => new(); }",
            source: artifact => artifact,
            References,
            ParseOptions,
            CompilationOptions,
            grow: (_, errors, _) =>
            {
                growthCalls++;
                Assert.Contains(errors, diagnostic => diagnostic.Id == "CS0246");
                includeDependency = true;
                return RoundTripGrowthResult.Continue;
            });

        Assert.Equal(RoundTripCompilationStatus.Succeeded, result.Status);
        Assert.True(result.Succeeded);
        Assert.Equal(2, result.Attempts);
        Assert.Equal(1, growthCalls);
        Assert.NotNull(result.PeImage);
        Assert.NotNull(result.FirstError);
        Assert.Contains("class Helper", result.Artifact);
    }

    [Fact]
    public void Compile_PreservesTypedStopReasonWhenGrowthStalls()
    {
        var result = RoundTripCompilationEngine.Compile(
            compose: () => "public class Target { public Missing M() => null; }",
            source: artifact => artifact,
            References,
            ParseOptions,
            CompilationOptions,
            grow: (_, _, _) => RoundTripGrowthResult.Stop("missing-root-unresolved"));

        Assert.Equal(RoundTripCompilationStatus.Stopped, result.Status);
        Assert.False(result.Succeeded);
        Assert.Equal(1, result.Attempts);
        Assert.Equal("missing-root-unresolved", result.StopReason);
        Assert.Null(result.PeImage);
        Assert.NotNull(result.FirstError);
    }

    [Fact]
    public void Compile_ReportsIterationBudgetWithoutDiscardingFinalArtifact()
    {
        int compositions = 0;
        var result = RoundTripCompilationEngine.Compile(
            compose: () => $"public class Target {{ public Missing M{++compositions}() => null; }}",
            source: artifact => artifact,
            References,
            ParseOptions,
            CompilationOptions,
            grow: (_, _, _) => RoundTripGrowthResult.Continue,
            new RoundTripCompilationOptions { MaxIterations = 2 });

        Assert.Equal(RoundTripCompilationStatus.IterationBudget, result.Status);
        Assert.Equal(2, result.Attempts);
        Assert.Equal(3, compositions);
        Assert.Contains("M3", result.Artifact);
        Assert.Equal("closure-iteration-budget", result.StopReason);
        Assert.Null(result.PeImage);
    }
}
