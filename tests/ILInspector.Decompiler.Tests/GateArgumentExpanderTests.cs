using ILInspector.Decompiler.Tests.Gating;

namespace ILInspector.Decompiler.Tests;

public class GateArgumentExpanderTests
{
    private static readonly IReadOnlyList<GatePreset> Presets =
    [
        new("all", "everything"),
        new("fast", "skip slow", "-trait-", "Speed=Slow"),
        new("no-corpus", "skip corpus", "-trait-", "Area=Corpus"),
    ];

    [Fact]
    public void NoGateFlag_PassesArgumentsThroughUnchanged()
    {
        string[] args = ["-class", "Foo", "-parallel", "none"];

        GateExpansion result = GateArgumentExpander.Expand(args, Presets);

        Assert.Equal(GateOutcome.Run, result.Outcome);
        Assert.Equal(args, result.Args);
    }

    [Fact]
    public void KnownPreset_PrependsTraitArgsAndDropsTheGatePair()
    {
        string[] args = ["--gate", "fast", "-class", "Foo"];

        GateExpansion result = GateArgumentExpander.Expand(args, Presets);

        Assert.Equal(GateOutcome.Run, result.Outcome);
        Assert.Equal(["-trait-", "Speed=Slow", "-class", "Foo"], result.Args);
    }

    [Fact]
    public void EmptyPreset_ProducesNoFilterButRemovesTheGatePair()
    {
        string[] args = ["--gate", "all", "-class", "Foo"];

        GateExpansion result = GateArgumentExpander.Expand(args, Presets);

        Assert.Equal(GateOutcome.Run, result.Outcome);
        Assert.Equal(["-class", "Foo"], result.Args);
    }

    [Fact]
    public void PresetNameMatchIsCaseInsensitive()
    {
        string[] args = ["--gate", "No-Corpus"];

        GateExpansion result = GateArgumentExpander.Expand(args, Presets);

        Assert.Equal(GateOutcome.Run, result.Outcome);
        Assert.Equal(["-trait-", "Area=Corpus"], result.Args);
    }

    [Theory]
    [InlineData("list")]
    [InlineData("help")]
    [InlineData("?")]
    public void HelpTokens_RequestThePresetTable(string token)
    {
        string[] args = ["--gate", token];

        GateExpansion result = GateArgumentExpander.Expand(args, Presets);

        Assert.Equal(GateOutcome.Help, result.Outcome);
        Assert.NotNull(result.Message);
        Assert.Contains("no-corpus", result.Message);
    }

    [Fact]
    public void GateFlagWithNoValue_RequestsThePresetTable()
    {
        string[] args = ["-class", "Foo", "--gate"];

        GateExpansion result = GateArgumentExpander.Expand(args, Presets);

        Assert.Equal(GateOutcome.Help, result.Outcome);
    }

    [Fact]
    public void UnknownPreset_IsAnError()
    {
        string[] args = ["--gate", "bogus"];

        GateExpansion result = GateArgumentExpander.Expand(args, Presets);

        Assert.Equal(GateOutcome.Error, result.Outcome);
        Assert.Contains("bogus", result.Message);
    }

    [Fact]
    public void RepeatedGateFlag_IsAnError()
    {
        string[] args = ["--gate", "fast", "--gate", "no-corpus"];

        GateExpansion result = GateArgumentExpander.Expand(args, Presets);

        Assert.Equal(GateOutcome.Error, result.Outcome);
    }

    [Fact]
    public void RenderTable_IncludesEveryPresetAndItsFilter()
    {
        string table = GateArgumentExpander.RenderTable(Presets);

        Assert.Contains("all", table);
        Assert.Contains("(no filter)", table);
        Assert.Contains("-trait- Speed=Slow", table);
        Assert.Contains("-trait- Area=Corpus", table);
    }
}
