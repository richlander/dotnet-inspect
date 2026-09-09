using ILInspector.Decompiler.Pipeline;
using ILInspector.DecompilerHarness;
using System.Globalization;

namespace ILInspector.Decompiler.Tests;

[Trait("Area", "Corpus")]
public class CorpusPipelineParityTests
{
    public static IEnumerable<object[]> CompletenessCases()
    {
        foreach (var profile in Enum.GetValues<CorpusProfile>())
        {
            yield return [(int)profile, typeof(HeterogeneousArmSample),
                nameof(HeterogeneousArmSample.GuardedArea), "PatternSwitchExpression"];
            yield return [(int)profile, typeof(HeterogeneousArmSample),
                nameof(HeterogeneousArmSample.Area), "PatternSwitchExpression"];
            yield return [(int)profile, typeof(CfgSampleClass),
                nameof(CfgSampleClass.DoubleViaLocalFunction), "LocalFunctionStatement"];
        }
    }

    [Theory]
    [MemberData(nameof(CompletenessCases))]
    public void CompletenessPipeline_MatchesProductCapabilities(
        int profileValue, Type fixtureType, string methodName, string expectedNode)
    {
        using var source = MetadataSource.Open(fixtureType.Assembly.Location);
        var function = IrImporter.Import(source, fixtureType.FullName!, methodName);
        var productFunction = IrImporter.Import(source, fixtureType.FullName!, methodName);
        Assert.NotNull(function);
        Assert.NotNull(productFunction);
        var profile = (CorpusProfile)profileValue;

        var changedPasses = CorpusSensor.RunCompletenessPasses(function, source, profile);
        var product = CSharpPrinter.PrintRaised(productFunction,
            importMethodBody: method => IrImporter.Import(source, method),
            typesProvablyDisjoint: source.AreProvablyDisjoint);

        function.CheckInvariant();
        Assert.Contains(expectedNode, IrPrinter.Dump(function));
        Assert.True(product.Succeeded);
        var actual = CSharpPrinter.Print(function);
        Assert.True(actual.Succeeded);
        Assert.Equal(product.Output, actual.Output);
        Assert.Equal(productFunction.Fidelity, function.Fidelity);
        if (profile == CorpusProfile.OptInNet11)
            Assert.NotEmpty(changedPasses);
        else
            Assert.Empty(changedPasses);
    }
}

public partial class AuthoredCorpusHarnessProcessTests
{
    [Theory]
    [InlineData(nameof(HeterogeneousArmSample.GuardedArea), 0)]
    [InlineData(nameof(HeterogeneousArmSample.GuardedArea), 1)]
    [InlineData(nameof(HeterogeneousArmSample.Area), 0)]
    [InlineData(nameof(HeterogeneousArmSample.Area), 1)]
    public void Harness_DumpSteps_ReplaysPatternSwitchBoundary(string methodName, int offset)
    {
        var fixtureType = typeof(HeterogeneousArmSample);
        string assemblyPath = fixtureType.Assembly.Location;
        using var metadata = CorpusMetadata.Create([assemblyPath]);
        using var source = MetadataSource.Open(assemblyPath, context: metadata);
        var completed = IrImporter.Import(source, fixtureType.FullName!, methodName);
        Assert.NotNull(completed);
        var steps = IrPasses.RunWithSteps(completed, int.MaxValue,
            method => IrImporter.Import(source, method), source.AreProvablyDisjoint);
        var rewrite = Assert.Single(steps.Steps,
            step => step.Description == "raise nested type-pattern dispatch to switch expression");
        int limit = rewrite.Index + offset;
        var replayed = IrImporter.Import(source, fixtureType.FullName!, methodName);
        Assert.NotNull(replayed);
        var replay = IrPasses.RunWithSteps(replayed, limit,
            method => IrImporter.Import(source, method), source.AreProvablyDisjoint);
        replayed.CheckInvariant();
        Assert.Equal(limit, replay.Count);
        Assert.Equal(offset == 1, replayed.Descendants.OfType<PatternSwitchExpression>().Any());

        var run = RunHarness(assemblyPath, "--dump", $"{fixtureType.FullName}::{methodName}",
            "--step-limit", limit.ToString(CultureInfo.InvariantCulture));

        Assert.Equal(0, run.ExitCode);
        Assert.Contains($"==== steps ({limit} recorded) ====", run.Output);
        Assert.Contains(
            $"==== IR (right before step {limit}) ====\n{IrPrinter.Dump(replayed).ReplaceLineEndings("\n")}",
            run.Output.ReplaceLineEndings("\n"));
    }

    [Theory]
    [InlineData(typeof(HeterogeneousArmSample), nameof(HeterogeneousArmSample.GuardedArea), "PatternSwitchExpression")]
    [InlineData(typeof(HeterogeneousArmSample), nameof(HeterogeneousArmSample.Area), "PatternSwitchExpression")]
    [InlineData(typeof(CfgSampleClass), nameof(CfgSampleClass.DoubleViaLocalFunction), "LocalFunctionStatement")]
    public void Harness_DumpSteps_MatchesMetadataBackedStages(
        Type fixtureType, string methodName, string expectedNode)
    {
        string assemblyPath = fixtureType.Assembly.Location;
        using var metadata = CorpusMetadata.Create([assemblyPath]);
        using var source = MetadataSource.Open(assemblyPath, context: metadata);
        var function = IrImporter.Import(source, fixtureType.FullName!, methodName);
        Assert.NotNull(function);
        var stages = IrPasses.RunWithStages(function,
            method => IrImporter.Import(source, method), source.AreProvablyDisjoint);
        function.CheckInvariant();
        Assert.Contains(expectedNode, stages[^1].Projection);

        var run = RunHarness(assemblyPath, "--dump", $"{fixtureType.FullName}::{methodName}", "--steps");

        Assert.Equal(0, run.ExitCode);
        Assert.Contains(
            $"==== IR (after all passes) ====\n{stages[^1].Projection.ReplaceLineEndings("\n")}",
            run.Output.ReplaceLineEndings("\n"));
    }

    [Theory]
    [InlineData(typeof(HeterogeneousArmSample), nameof(HeterogeneousArmSample.GuardedArea), "PatternSwitchExpression")]
    [InlineData(typeof(HeterogeneousArmSample), nameof(HeterogeneousArmSample.Area), "PatternSwitchExpression")]
    [InlineData(typeof(CfgSampleClass), nameof(CfgSampleClass.DoubleViaLocalFunction), "LocalFunctionStatement")]
    public void Harness_DumpDiff_MatchesMetadataBackedStages(
        Type fixtureType, string methodName, string expectedNode)
    {
        string assemblyPath = fixtureType.Assembly.Location;
        using var metadata = CorpusMetadata.Create([assemblyPath]);
        using var source = MetadataSource.Open(assemblyPath, context: metadata);
        var function = IrImporter.Import(source, fixtureType.FullName!, methodName);
        Assert.NotNull(function);
        var stages = IrPasses.RunWithStages(function,
            method => IrImporter.Import(source, method), source.AreProvablyDisjoint);
        Assert.Contains(expectedNode, stages[^1].Projection);

        var run = RunHarness(assemblyPath, "--dump", $"{fixtureType.FullName}::{methodName}", "--diff");

        Assert.Equal(0, run.ExitCode);
        Assert.Contains(StageDump.FormatDiff(stages).ReplaceLineEndings("\n"),
            run.Output.ReplaceLineEndings("\n"));
    }
}
