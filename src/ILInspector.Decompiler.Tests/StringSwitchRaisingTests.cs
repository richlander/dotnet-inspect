using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

public class StringSwitchRaisingTests
{
    static IrFunction Raised(string methodName)
    {
        using var source = MetadataSource.Open(typeof(CfgSampleClass).Assembly.Location);
        var function = IrImporter.Import(source, typeof(CfgSampleClass).FullName!, methodName);
        Assert.NotNull(function);
        IrPasses.Run(function!);
        function.CheckInvariant();
        return function!;
    }

    [Fact]
    public void SmallStringSwitch_RaisesToSwitchStatement()
    {
        var function = Raised(nameof(CfgSampleClass.SmallStringSwitch));

        var node = Assert.Single(function.Descendants.OfType<Switch>());
        // Two case sections plus the default.
        Assert.Equal(3, node.Sections.Count);
        Assert.Single(node.Sections, s => s.IsDefault);

        var labels = node.Sections.Where(s => !s.IsDefault)
            .SelectMany(s => s.Labels).Select(l => l.Value).ToArray();
        Assert.Equal(["a", "b"], labels);

        // No flat gotos survive the raise.
        Assert.Empty(function.Descendants.OfType<ConditionalBranch>());
    }

    [Fact]
    public void SmallStringSwitch_RendersSwitchOnString()
    {
        var output = CSharpPrinter.Print(Raised(nameof(CfgSampleClass.SmallStringSwitch)))
            .Output!.ReplaceLineEndings("\n").Trim();

        Assert.Equal(
            """
            switch (s)
            {
                case "a":
                    return 1;
                case "b":
                    return 2;
                default:
                    return 0;
            }
            """,
            output);
    }

    [Fact]
    public void StringSwitchWithJoin_RaisesCasesThatBreakToAJoin()
    {
        var function = Raised(nameof(CfgSampleClass.StringSwitchWithJoin));

        var node = Assert.Single(function.Descendants.OfType<Switch>());
        Assert.Equal(4, node.Sections.Count);
        // Every non-default section breaks to the shared continuation.
        Assert.Equal(3, node.Sections.Count(s => !s.IsDefault));
        Assert.True(node.Descendants.OfType<Break>().Count() >= 4);
    }

    [Fact]
    public void StringSwitchNoDefault_RaisesWithLeadingSetupAndNoDefault()
    {
        var function = Raised(nameof(CfgSampleClass.StringSwitchNoDefault));

        var node = Assert.Single(function.Descendants.OfType<Switch>());
        // Two case sections and no default (the default fell through to the join).
        Assert.Equal(2, node.Sections.Count);
        Assert.DoesNotContain(node.Sections, s => s.IsDefault);

        var labels = node.Sections.SelectMany(s => s.Labels).Select(l => l.Value).ToArray();
        Assert.Equal(["a", "b"], labels);

        // No flat gotos survive the raise.
        Assert.Empty(function.Descendants.OfType<ConditionalBranch>());
    }

    [Fact]
    public void SingleStringEqualityTest_IsNotRaised()
    {
        // One `s == "x"` test is an `if`, not a switch — raising it would be a
        // pointless reshaping, so the pass requires at least two cases.
        var function = Raised(nameof(CfgSampleClass.SingleStringEquality));

        Assert.Empty(function.Descendants.OfType<Switch>());
    }
}
