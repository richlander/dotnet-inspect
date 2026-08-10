using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

[Trait("Area", "Pass")]
public class SwitchRaisingTerminalContinuationTests
{
    [Fact]
    public void CompilerProducedBreakToTerminalReturn_PreservesReturnAsJoin()
    {
        var function = Import(nameof(CfgSampleClass.TerminalSwitchBreakToReturn));
        Assert.Single(function.Descendants.OfType<SwitchBranch>());
        Assert.Same(
            function.Body.Blocks[function.Body.Blocks.Count - 1],
            Assert.Single(function.Body.Blocks, block => block.Children is [Return { Value: null }]));

        new SwitchRaisingPass().Run(function, PassContext.None);
        function.CheckInvariant();

        var node = Assert.Single(function.Descendants.OfType<Switch>());
        Assert.Empty(node.Descendants.OfType<Return>());
        Assert.NotEmpty(node.Descendants.OfType<Break>());
        Assert.Single(function.Descendants.OfType<Return>());

        string output = CSharpPrinter.Print(function).Output!.ReplaceLineEndings("\n");
        Assert.Contains("case CfgTerminalSwitchKind.Value25:", output);
        Assert.Contains("case CfgTerminalSwitchKind.Value40:", output);
        Assert.Contains("        break;", output);
        Assert.DoesNotContain("        return;", output);
    }

    [Fact]
    public void CompilerProducedReturnInsideCase_RemainsTerminatingSection()
    {
        var function = Import(nameof(CfgSampleClass.TerminalSwitchReturnInCase));
        Assert.Single(function.Descendants.OfType<SwitchBranch>());
        Assert.NotSame(
            function.Body.Blocks[function.Body.Blocks.Count - 1],
            Assert.Single(function.Body.Blocks, block => block.Children is [Return { Value: null }]));

        new SwitchRaisingPass().Run(function, PassContext.None);
        function.CheckInvariant();

        var node = Assert.Single(function.Descendants.OfType<Switch>());
        Assert.Single(node.Descendants.OfType<Return>());
        Assert.Empty(node.Descendants.OfType<Break>());

        string output = CSharpPrinter.Print(function).Output!.ReplaceLineEndings("\n");
        Assert.Contains("case CfgTerminalSwitchKind.Value25:", output);
        Assert.Contains("case CfgTerminalSwitchKind.Value40:", output);
        Assert.Contains("        return;", output);
    }

    static IrFunction Import(string methodName)
    {
        using var source = MetadataSource.Open(typeof(CfgSampleClass).Assembly.Location);
        var function = IrImporter.Import(source, typeof(CfgSampleClass).FullName!, methodName);
        Assert.NotNull(function);
        return function;
    }
}
