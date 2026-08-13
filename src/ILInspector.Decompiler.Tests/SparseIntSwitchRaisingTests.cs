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

    [Fact]
    [Trait("Area", "Pass")]
    public void OwnedCaseBranchEnteringSiblingRegion_DeclinesSwitchRaise()
    {
        var function = NestedOwnedTransferFunction(0x50);

        function.CheckInvariant();
        new SwitchRaisingPass().Run(function, PassContext.None);
        function.CheckInvariant();

        Assert.Empty(function.Descendants.OfType<Switch>());
        Assert.Equal(3, function.Descendants.OfType<ConditionalBranch>().Count());
    }

    [Fact]
    [Trait("Area", "Pass")]
    public void OwnedCaseBranchWithinSameRegion_RaisesSwitch()
    {
        var function = NestedOwnedTransferFunction(0x38);

        function.CheckInvariant();
        new SwitchRaisingPass().Run(function, PassContext.None);
        function.CheckInvariant();

        Assert.Single(function.Descendants.OfType<Switch>());
        Assert.Single(function.Descendants.OfType<ConditionalBranch>());
    }

    static IrFunction NestedOwnedTransferFunction(int nestedTarget)
    {
        var body = new BlockContainer();

        var firstTest = new Block(0x00);
        firstTest.Add(new ConditionalBranch(
            new Comparison(
                ComparisonKind.Equal,
                isUnsigned: false,
                new LoadArgument(0, "value", s_int),
                new Constant(1, s_int)),
            0x30));
        body.Add(firstTest);

        var secondTest = new Block(0x10);
        secondTest.Add(new ConditionalBranch(
            new Comparison(
                ComparisonKind.Equal,
                isUnsigned: false,
                new LoadArgument(0, "value", s_int),
                new Constant(2, s_int)),
            0x40));
        body.Add(secondTest);

        var defaultBody = new Block(0x20);
        defaultBody.Add(new Branch(0x70));
        body.Add(defaultBody);

        var enterTargetArm = new Block();
        enterTargetArm.Add(new Branch(nestedTarget));
        var firstCase = new Block(0x30);
        firstCase.Add(new IfStatement(
            new LoadArgument(1, "enterTarget", s_bool),
            enterTargetArm,
            elseArm: null));
        firstCase.Add(new Branch(0x38));
        body.Add(firstCase);

        var firstCaseExit = new Block(0x38);
        firstCaseExit.Add(new Branch(0x70));
        body.Add(firstCaseExit);

        var secondCase = new Block(0x40);
        secondCase.Add(new ConditionalBranch(
            new LoadArgument(2, "condition", s_bool),
            0x50));
        body.Add(secondCase);

        var falseBody = new Block(0x48);
        falseBody.Add(new StoreLocal(0, s_int, new Constant(1, s_int)));
        falseBody.Add(new Branch(0x60));
        body.Add(falseBody);

        var trueBody = new Block(0x50);
        trueBody.Add(new StoreLocal(0, s_int, new Constant(2, s_int)));
        body.Add(trueBody);

        var secondCaseExit = new Block(0x60);
        secondCaseExit.Add(new Branch(0x70));
        body.Add(secondCaseExit);

        var join = new Block(0x70);
        join.Add(new Return(null));
        body.Add(join);

        return new IrFunction(
            "M",
            TypeRef.Definition("Synthetic", "", "T"),
            new MethodSignature(
                s_void,
                [
                    new Parameter("value", s_int),
                    new Parameter("enterTarget", s_bool),
                    new Parameter("condition", s_bool),
                ],
                HasThis: false,
                GenericParameterCount: 0),
            [s_int],
            body);
    }
}
