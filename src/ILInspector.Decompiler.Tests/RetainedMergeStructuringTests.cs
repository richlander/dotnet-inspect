using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

public class RetainedMergeStructuringTests
{
    static readonly TypeRef I32 = TypeRef.CoreLib("System", "Int32");
    static readonly TypeRef Owner = TypeRef.CoreLib("Synthetic", "RetainedMerge");

    [Fact]
    public void SequentialRetainedMergesStructureFurthestRangeAndTail()
    {
        var (function, diagnostics) = Structure(SequentialRetainedMerges());

        Assert.Equal(2, diagnostics.RetainedRegions);
        Assert.Equal(3, function.Body.Blocks.Count);
        Assert.Equal(4, function.Descendants.OfType<StoreLocal>().Count());
        Assert.Equal(2, function.Descendants.OfType<Branch>().Count());
        Assert.Contains(function.Descendants.OfType<Branch>(), branch => branch.TargetOffset == 40);
        Assert.Contains(function.Descendants.OfType<Branch>(), branch => branch.TargetOffset == 80);

        var firstRetained = Assert.Single(
            function.Descendants.OfType<IfStatement>(),
            statement => statement.Then.Children.OfType<Branch>().Any(branch => branch.TargetOffset == 40));
        Assert.IsType<Comparison>(firstRetained.Condition);

        Assert.Equal(40, function.Body.Blocks[1].Children[0].SourceOffset);
        string output = CSharpPrinter.Print(function).Output!.ReplaceLineEndings("\n");
        Assert.Contains("if (", output);
        Assert.Contains("goto IL_0028;", output);
        Assert.Contains("IL_0028:", output);
        Assert.Contains("goto IL_0050;", output);
        Assert.Contains("IL_0050:", output);
    }

    [Fact]
    public void CrossingForwardRegionsStayFlat()
    {
        var blocks = new[]
        {
            Term(0, Cond(5)),
            Term(1, new Branch(3)),
            Term(2, Cond(6)),
            Term(3, Cond(4)),
            Term(4, new Branch(5)),
            Term(5, Cond(6)),
            Term(6, new Return(null)),
        };

        var (function, diagnostics) = Structure(blocks);

        Assert.Equal(0, diagnostics.RetainedRegions);
        Assert.Contains("retained-crossing", diagnostics.RetainedDeclines);
        Assert.Equal(blocks.Length, function.Body.Blocks.Count);
        Assert.Empty(function.Descendants.OfType<IfStatement>());
    }

    [Fact]
    public void ForwardRegionEntangledWithBackEdgeIsRepresentedButNotStructured()
    {
        var blocks = new[]
        {
            Term(0, new StoreLocal(0, I32, new Constant(0, I32))),
            Term(1, Cond(3)),
            Term(2, new Branch(3)),
            Term(3, new StoreLocal(0, I32, new Constant(1, I32))),
            Term(4, Cond(1)),
            Term(5, new Return(new LoadLocal(0, I32))),
        };

        var plan = StructuringJoinAnalysis.Analyze(blocks);
        Assert.Contains(plan.ForwardRegions, region =>
            region.Start == 1 && region.Merge == 3 && region.IsBackEdgeEntangled);
        Assert.NotEmpty(plan.BackEdgeRegions);

        var (function, diagnostics) = Structure(blocks);

        Assert.Equal(0, diagnostics.RetainedRegions);
        Assert.Contains("retained-back-edge-entangled", diagnostics.RetainedDeclines);
        Assert.Contains("retained-back-edge-region", diagnostics.RetainedDeclines);
        Assert.Equal(blocks.Length, function.Body.Blocks.Count);
    }

    [Fact]
    public void UnrelatedTailBackEdgeDoesNotRejectForwardRetainedRegion()
    {
        var blocks = SequentialRetainedMerges().Take(6).ToList();
        blocks[5] = Term(40, new StoreLocal(0, I32, new Constant(5, I32)));
        blocks.Add(Term(48, Cond(64)));
        blocks.Add(Term(56, new Branch(48)));
        blocks.Add(Term(64, new Return(new LoadLocal(0, I32))));

        var (function, diagnostics) = Structure([.. blocks]);

        Assert.Equal(1, diagnostics.RetainedRegions);
        Assert.Contains("retained-back-edge-region", diagnostics.RetainedDeclines);
        Assert.Contains(function.Descendants.OfType<Branch>(), branch => branch.TargetOffset == 48);
        Assert.Contains(function.Descendants.OfType<Branch>(), branch => branch.TargetOffset == 40);
    }

    [Fact]
    public void TailEntryIntoRetainedInteriorDeclines()
    {
        var blocks = SequentialRetainedMerges().Take(6).ToList();
        blocks[5] = Term(40, Cond(16));
        blocks.Add(Term(48, new Return(new LoadLocal(0, I32))));

        var (function, diagnostics) = Structure([.. blocks]);

        Assert.Equal(0, diagnostics.RetainedRegions);
        Assert.Contains("retained-external-entry", diagnostics.RetainedDeclines);
        Assert.Equal(blocks.Count, function.Body.Blocks.Count);
        Assert.Empty(function.Descendants.OfType<IfStatement>());
    }

    [Fact]
    public void RetainedGotoParticipatesInDefiniteAssignment()
    {
        var blocks = new[]
        {
            Term(0, Cond(24)),
            Block(
                8,
                new StoreLocal(0, I32, new Constant(1, I32)),
                Cond(40)),
            Block(16, new StoreLocal(0, I32, new Constant(2, I32)), new Branch(32)),
            Term(24, new StoreLocal(0, I32, new Constant(3, I32))),
            Term(32, new Branch(40)),
            Term(40, new Return(new LoadLocal(0, I32))),
        };

        var (function, diagnostics) = Structure(blocks);
        var facts = CSharpPrinter.CollectDataflowFacts(function);

        Assert.Equal(1, diagnostics.RetainedRegions);
        Assert.False(facts.Bailed);
        Assert.Empty(facts.ReadBeforeAssign);
        string output = CSharpPrinter.Print(function).Output!;
        Assert.Contains("int V_0;", output);
        Assert.DoesNotContain("int V_0 = default;", output);
    }

    [Fact]
    public void RetainedGotoWithFlatTailBailsDefiniteAssignmentConservatively()
    {
        var blocks = new[]
        {
            Term(0, Cond(24)),
            Term(8, Cond(40)),
            Block(16, new StoreLocal(0, I32, new Constant(2, I32)), new Branch(32)),
            Term(24, new StoreLocal(0, I32, new Constant(3, I32))),
            Term(32, new Branch(40)),
            Term(40, Cond(64)),
            Term(48, new Branch(40)),
            Term(64, new Return(new LoadLocal(0, I32))),
        };

        var (function, diagnostics) = Structure(blocks);
        var facts = CSharpPrinter.CollectDataflowFacts(function);

        Assert.Equal(1, diagnostics.RetainedRegions);
        Assert.True(facts.Bailed);
        Assert.Contains("int V_0 = default;", CSharpPrinter.Print(function).Output!);
    }

    [Fact]
    public void RetainedRegionStartKeepsOutsideTargetLabel()
    {
        var blocks = new[]
        {
            Term(0, new Branch(8)),
            Term(8, Cond(32)),
            Term(16, Cond(48)),
            Block(24, new StoreLocal(0, I32, new Constant(2, I32)), new Branch(40)),
            Term(32, new StoreLocal(0, I32, new Constant(3, I32))),
            Term(40, new Branch(48)),
            Term(48, new Return(new LoadLocal(0, I32))),
        };

        var (function, diagnostics) = Structure(blocks);

        Assert.Equal(1, diagnostics.RetainedRegions);
        Assert.Equal(8, function.Body.Blocks[1].Children[0].SourceOffset);
        string output = CSharpPrinter.Print(function).Output!;
        Assert.Contains("goto IL_0008;", output);
        Assert.Contains("IL_0008:", output);
    }

    [Fact]
    public void OneJumpPlusFallthroughIsNotAProvenSharedMerge()
    {
        var blocks = new[]
        {
            Term(0, Cond(24)),
            Term(8, Cond(40)),
            Term(16, new Branch(32)),
            Term(24, new StoreLocal(0, I32, new Constant(3, I32))),
            Term(32, new StoreLocal(0, I32, new Constant(4, I32))),
            Term(40, new Return(new LoadLocal(0, I32))),
        };

        var (function, diagnostics) = Structure(blocks);

        Assert.Equal(0, diagnostics.RetainedRegions);
        Assert.Contains("retained-merge-not-shared", diagnostics.RetainedDeclines);
        Assert.Equal(blocks.Length, function.Body.Blocks.Count);
    }

    [Fact]
    public void DuplicateOffsetsDeclineWithoutCrashingDefiniteAssignment()
    {
        var blocks = SequentialRetainedMerges();
        blocks[1] = Block(
            0,
            new StoreLocal(0, I32, new Constant(1, I32)),
            new Branch(40));

        var (function, diagnostics) = Structure(blocks);
        var facts = CSharpPrinter.CollectDataflowFacts(function);

        Assert.Equal(0, diagnostics.RetainedRegions);
        Assert.Contains("retained-duplicate-offset", diagnostics.RetainedDeclines);
        Assert.NotNull(facts);
    }

    static Block[] SequentialRetainedMerges() =>
    [
        Term(0, Cond(24)),
        Term(8, Cond(40)),
        Block(16, new StoreLocal(0, I32, new Constant(2, I32)), new Branch(32)),
        Term(24, new StoreLocal(0, I32, new Constant(3, I32))),
        Term(32, new Branch(40)),
        Term(40, Cond(64)),
        Term(48, Cond(80)),
        Block(56, new StoreLocal(0, I32, new Constant(7, I32)), new Branch(72)),
        Term(64, new StoreLocal(0, I32, new Constant(8, I32))),
        Term(72, new Branch(80)),
        Term(80, new Return(new LoadLocal(0, I32))),
    ];

    static (IrFunction Function, StructuringDiagnostics Diagnostics) Structure(Block[] blocks)
    {
        var container = new BlockContainer();
        foreach (var block in blocks)
            container.Add(block);
        var function = new IrFunction(
            "M",
            Owner,
            new MethodSignature(I32, [], HasThis: false, GenericParameterCount: 0),
            [I32],
            container);
        var diagnostics = new StructuringDiagnostics();

        new StructuringPass().Run(
            function,
            new PassContext(new Stepper(enabled: false), diagnostics));
        function.CheckInvariant();
        return (function, diagnostics);
    }

    static Block Term(int offset, IrNode terminator) => Block(offset, terminator);

    static Block Block(int offset, params IrNode[] statements)
    {
        var block = new Block(offset);
        foreach (var statement in statements)
            block.Add(statement);
        return block;
    }

    static ConditionalBranch Cond(int targetOffset)
        => new(
            new Comparison(
                ComparisonKind.Equal,
                isUnsigned: false,
                new Constant(1, I32),
                new Constant(0, I32)),
            targetOffset);
}
