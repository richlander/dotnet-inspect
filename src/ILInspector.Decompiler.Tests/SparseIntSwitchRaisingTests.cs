using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

public class SparseIntSwitchRaisingTests
{
    static readonly TypeRef s_int = TypeRef.CoreLib("System", "Int32");
    static readonly TypeRef s_bool = TypeRef.CoreLib("System", "Boolean");
    static readonly TypeRef s_void = TypeRef.CoreLib("System", "Void");

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

    [Fact]
    [Trait("Area", "Pass")]
    public void NestedBranchEnteringSecondComparison_DeclinesSwitchRaise()
    {
        var body = new BlockContainer();

        var enteringArm = new Block();
        enteringArm.Add(new Branch(0x20));
        var preceding = new Block(0);
        preceding.Add(new IfStatement(
            new LoadArgument(1, "enterDispatch", s_bool),
            enteringArm,
            elseArm: null));
        body.Add(preceding);

        var firstTest = new Block(0x10);
        firstTest.Add(new ConditionalBranch(
            new Comparison(
                ComparisonKind.Equal,
                isUnsigned: false,
                new LoadArgument(0, "value", s_int),
                new Constant(1, s_int)),
            0x40));
        body.Add(firstTest);

        var secondTest = new Block(0x20);
        secondTest.Add(new ConditionalBranch(
            new Comparison(
                ComparisonKind.Equal,
                isUnsigned: false,
                new LoadArgument(0, "value", s_int),
                new Constant(2, s_int)),
            0x50));
        body.Add(secondTest);

        foreach (int offset in new[] { 0x30, 0x40, 0x50 })
        {
            var section = new Block(offset);
            section.Add(new Return(null));
            body.Add(section);
        }

        var function = new IrFunction(
            "M",
            TypeRef.Definition("Synthetic", "", "T"),
            new MethodSignature(
                s_void,
                [
                    new Parameter("value", s_int),
                    new Parameter("enterDispatch", s_bool),
                ],
                HasThis: false,
                GenericParameterCount: 0),
            [],
            body);

        function.CheckInvariant();
        new SwitchRaisingPass().Run(function, PassContext.None);
        function.CheckInvariant();

        Assert.Empty(function.Descendants.OfType<Switch>());
        Assert.Equal(2, function.Descendants.OfType<ConditionalBranch>().Count());
    }
}
