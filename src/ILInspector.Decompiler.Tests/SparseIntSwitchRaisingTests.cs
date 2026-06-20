using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

public class SparseIntSwitchRaisingTests
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
    public void ClassifyMode_RaisesBinarySearchDispatchToSwitch()
    {
        var function = Raised(nameof(CfgSampleClass.ClassifyMode));

        var node = Assert.Single(function.Descendants.OfType<Switch>());
        // Six int cases plus the default.
        Assert.Equal(7, node.Sections.Count);
        Assert.Single(node.Sections, s => s.IsDefault);

        var labels = node.Sections.Where(s => !s.IsDefault)
            .SelectMany(s => s.Labels).Select(l => l.Value).ToArray();
        Assert.Equal(new object[] { 0x1000, 0x2000, 0x4000, 0x8000, 0xA000, 0xC000 }, labels);

        // The relational pivots and equality tests are all consumed by the raise.
        Assert.Empty(function.Descendants.OfType<ConditionalBranch>());
        Assert.Empty(function.Descendants.OfType<Comparison>());
    }

    [Fact]
    public void ClassifyMode_RendersSwitchOnTheGoverningExpression()
    {
        var output = CSharpPrinter.Print(Raised(nameof(CfgSampleClass.ClassifyMode)))
            .Output!.ReplaceLineEndings("\n");

        Assert.Contains("switch (mode & 61440)", output);
        Assert.Contains("case 4096:", output);
        Assert.Contains("default:", output);
        Assert.DoesNotContain("if (", output);
    }

    [Fact]
    public void ClassifyWide_RaisesMultiLevelBinarySearchTree()
    {
        // Enough scattered cases that csc builds a multi-level pivot tree; every
        // equality leaf is still collected into one flat switch.
        var function = Raised(nameof(CfgSampleClass.ClassifyWide));

        var node = Assert.Single(function.Descendants.OfType<Switch>());
        Assert.Equal(9, node.Sections.Count);
        Assert.Single(node.Sections, s => s.IsDefault);

        var labels = node.Sections.Where(s => !s.IsDefault)
            .SelectMany(s => s.Labels).Select(l => l.Value).ToArray();
        Assert.Equal(new object[] { 3, 17, 42, 99, 128, 256, 500, 1000 }, labels);
        Assert.Empty(function.Descendants.OfType<ConditionalBranch>());
    }
}
