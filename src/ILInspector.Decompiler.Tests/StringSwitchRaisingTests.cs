using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

public class StringSwitchRaisingTests
{
    static readonly TypeRef s_int = TypeRef.CoreLib("System", "Int32");
    static readonly TypeRef s_bool = TypeRef.CoreLib("System", "Boolean");
    static readonly TypeRef s_string = TypeRef.CoreLib("System", "String");
    static readonly TypeRef s_uint = TypeRef.CoreLib("System", "UInt32");

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
    public void BucketStringSwitch_RaisesDayNumber()
    {
        var function = Raised(nameof(CfgSampleClass.DayNumber));

        var node = Assert.Single(function.Descendants.OfType<Switch>());
        Assert.Single(node.Sections, s => s.IsDefault);
        var labels = node.Sections.Where(s => !s.IsDefault)
            .SelectMany(s => s.Labels).Select(l => l.Value).ToArray();
        Assert.Equal(["fri", "mon", "sat", "sun", "thu", "tue", "wed"], labels.Order().ToArray());
        Assert.DoesNotContain(function.Descendants.OfType<Call>(), call => call.Callee.Name == "ComputeStringHash");
    }

    [Fact]
    public void BucketStringSwitch_RendersSwitchOnString()
    {
        var output = CSharpPrinter.Print(Raised(nameof(CfgSampleClass.DayNumber))).Output!;

        Assert.Contains("switch (", output);
        Assert.Contains("case \"mon\":", output);
        Assert.Contains("case \"sun\":", output);
        Assert.Contains("default:", output);
        Assert.DoesNotContain("ComputeStringHash", output);
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

    [Fact]
    public void UserStringHashHelperLookalike_IsNotRaised()
    {
        var function = BuildUserStringHashSwitchLookalike();

        new SwitchRaisingPass().Run(function, PassContext.None);

        Assert.Empty(function.Descendants.OfType<Switch>());
        Assert.Single(function.Descendants.OfType<Call>(), call => call.Callee.Name == "ComputeStringHash");
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

    static IrFunction BuildUserStringHashSwitchLookalike()
    {
        var userPrivateImpl = TypeRef.Definition("UserAssembly", "", "<PrivateImplementationDetails>");
        var hash = new MethodRef(userPrivateImpl, "ComputeStringHash", s_uint, [s_string], HasThis: false)
        {
            DeclaringTypeCompilerGenerated = MetadataFactState.No,
        };
        var eq = new MethodRef(s_string, "op_Equality", s_bool, [s_string, s_string], HasThis: false);
        var body = new BlockContainer();

        var hashA = new Block(0);
        hashA.Add(new StoreLocal(0, s_uint, new Call(hash, isVirtual: false, [new LoadArgument(0, "s", s_string)])));
        hashA.Add(new ConditionalBranch(HashEqual(0, 1), targetOffset: 30));
        body.Add(hashA);

        var hashB = new Block(10);
        hashB.Add(new ConditionalBranch(HashEqual(0, 2), targetOffset: 50));
        body.Add(hashB);

        var hashDefault = new Block(20);
        hashDefault.Add(new Branch(70));
        body.Add(hashDefault);

        var eqA = new Block(30);
        eqA.Add(new ConditionalBranch(StringEq(eq, s_string, "a"), targetOffset: 80));
        body.Add(eqA);

        var eqAFail = new Block(40);
        eqAFail.Add(new Branch(70));
        body.Add(eqAFail);

        var eqB = new Block(50);
        eqB.Add(new ConditionalBranch(StringEq(eq, s_string, "b"), targetOffset: 90));
        body.Add(eqB);

        var eqBFail = new Block(60);
        eqBFail.Add(new Branch(70));
        body.Add(eqBFail);

        var defaultBlock = new Block(70);
        defaultBlock.Add(new Return(new Constant(0, s_int)));
        body.Add(defaultBlock);

        var caseA = new Block(80);
        caseA.Add(new Return(new Constant(1, s_int)));
        body.Add(caseA);

        var caseB = new Block(90);
        caseB.Add(new Return(new Constant(2, s_int)));
        body.Add(caseB);

        return new IrFunction(
            "M",
            TypeRef.Definition("Synthetic", "", "T"),
            new MethodSignature(s_int, [new Parameter("s", s_string)], HasThis: false, GenericParameterCount: 0),
            [s_uint],
            body);
    }

    static Comparison HashEqual(int local, int hash)
        => new(ComparisonKind.Equal, isUnsigned: false, new LoadLocal(local, s_uint), new Constant(hash, s_int));
}
