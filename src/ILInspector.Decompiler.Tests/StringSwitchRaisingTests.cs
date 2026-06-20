using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

public class StringSwitchRaisingTests
{
    static readonly TypeRef s_int = TypeRef.CoreLib("System", "Int32");
    static readonly TypeRef s_bool = TypeRef.CoreLib("System", "Boolean");

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

    [Fact]
    public void UserStringEqualityLookalikeChain_IsNotRaised()
    {
        var function = BuildUserStringEqualityChain();

        new SwitchRaisingPass().Run(function, PassContext.None);

        Assert.Empty(function.Descendants.OfType<Switch>());
        Assert.Equal(2, function.Descendants.OfType<ConditionalBranch>().Count());
        function.CheckInvariant();
    }

    static IrFunction BuildUserStringEqualityChain()
    {
        var userString = TypeRef.Definition("UserAssembly", "System", "String");
        var eq = new MethodRef(userString, "op_Equality", s_bool, [userString, userString], HasThis: false);
        var body = new BlockContainer();

        var first = new Block(0);
        first.Add(new ConditionalBranch(StringEq(eq, userString, "a"), targetOffset: 30));
        body.Add(first);

        var second = new Block(10);
        second.Add(new ConditionalBranch(StringEq(eq, userString, "b"), targetOffset: 40));
        body.Add(second);

        var dispatchDefault = new Block(20);
        dispatchDefault.Add(new Branch(50));
        body.Add(dispatchDefault);

        var caseA = new Block(30);
        caseA.Add(new Return(new Constant(1, s_int)));
        body.Add(caseA);

        var caseB = new Block(40);
        caseB.Add(new Return(new Constant(2, s_int)));
        body.Add(caseB);

        var defaultBlock = new Block(50);
        defaultBlock.Add(new Return(new Constant(0, s_int)));
        body.Add(defaultBlock);

        return new IrFunction(
            "M",
            TypeRef.CoreLib("Synthetic", "T"),
            new MethodSignature(s_int, [new Parameter("s", userString)], HasThis: false, GenericParameterCount: 0),
            [],
            body);
    }

    static Call StringEq(MethodRef eq, TypeRef stringType, string value)
        => new(
            eq,
            isVirtual: false,
            [new LoadArgument(0, "s", stringType), new Constant(value, stringType)]);
}
