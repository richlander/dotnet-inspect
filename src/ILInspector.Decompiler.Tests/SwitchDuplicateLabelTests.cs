using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

// Adversarial guards for the positive-polarity equality-chain switch raisers
// (issue #1430). C# forbids duplicate case labels (CS0152), so a chain with a
// repeated literal/constant is not a source switch; the raiser must decline
// rather than emit an uncompilable duplicate-label `switch`. csc can never emit
// such a chain (the source would not compile), so these are synthetic-IR
// near-misses pinning the guard, paired with a distinct-label positive canary.
public class SwitchDuplicateLabelTests
{
    static readonly TypeRef Int32 = TypeRef.CoreLib("System", "Int32");
    static readonly TypeRef String = TypeRef.CoreLib("System", "String");
    static readonly TypeRef Boolean = TypeRef.CoreLib("System", "Boolean");
    static readonly TypeRef Owner = TypeRef.CoreLib("Synthetic", "T");

    static IrFunction Run(TypeRef governingType, Func<IrExpression> value, int firstLabel, int secondLabel, bool asString)
    {
        IrExpression Test(int label) => asString
            ? new Call(
                new MethodRef(String, "op_Equality", Boolean, [String, String], HasThis: false),
                isVirtual: false,
                [value(), new Constant(label.ToString(), String)])
            : new Comparison(ComparisonKind.Equal, isUnsigned: false, value(), new Constant(label, Int32));

        var container = new BlockContainer();

        var b0 = new Block(0);
        b0.Add(new ConditionalBranch(Test(firstLabel), 100));

        var b1 = new Block(8);
        b1.Add(new ConditionalBranch(Test(secondLabel), 200));

        var b2 = new Block(16);
        b2.Add(new Branch(300));

        var case1 = new Block(100);
        case1.Add(new Return(new Constant(1, Int32)));

        var case2 = new Block(200);
        case2.Add(new Return(new Constant(2, Int32)));

        var def = new Block(300);
        def.Add(new Return(new Constant(0, Int32)));

        foreach (var block in (Block[])[b0, b1, b2, case1, case2, def])
            container.Add(block);

        var signature = new MethodSignature(Int32, [], HasThis: false, GenericParameterCount: 0);
        var function = new IrFunction("M", Owner, signature, [governingType], container);
        new SwitchRaisingPass().Run(function, PassContext.None);
        function.CheckInvariant();
        return function;
    }

    [Fact]
    public void SparseIntChain_DistinctLabels_RaisesToSwitch()
    {
        var function = Run(Int32, () => new LoadLocal(0, Int32), 5, 7, asString: false);

        var node = Assert.Single(function.Descendants.OfType<Switch>());
        var labels = node.Sections.Where(s => !s.IsDefault)
            .SelectMany(s => s.Labels).Select(l => l.Value).ToArray();
        Assert.Equal(new object[] { 5, 7 }, labels);
    }

    [Fact]
    public void SparseIntChain_DuplicateLabel_StaysFlat()
    {
        var function = Run(Int32, () => new LoadLocal(0, Int32), 5, 5, asString: false);

        Assert.Empty(function.Descendants.OfType<Switch>());
        // The flat equality chain is preserved (valid C#), not rewritten.
        Assert.Equal(2, function.Descendants.OfType<ConditionalBranch>().Count());
    }

    [Fact]
    public void StringEqualityChain_DistinctLiterals_RaisesToSwitch()
    {
        var function = Run(String, () => new LoadLocal(0, String), 5, 7, asString: true);

        var node = Assert.Single(function.Descendants.OfType<Switch>());
        var labels = node.Sections.Where(s => !s.IsDefault)
            .SelectMany(s => s.Labels).Select(l => l.Value).ToArray();
        Assert.Equal(new object[] { "5", "7" }, labels);
    }

    [Fact]
    public void StringEqualityChain_DuplicateLiteral_StaysFlat()
    {
        var function = Run(String, () => new LoadLocal(0, String), 5, 5, asString: true);

        Assert.Empty(function.Descendants.OfType<Switch>());
        Assert.Equal(2, function.Descendants.OfType<ConditionalBranch>().Count());
    }
}
