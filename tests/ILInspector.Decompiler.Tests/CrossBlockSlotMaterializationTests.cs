using ILInspector.DecompilerHarness;
using ILInspector.Decompiler.Pipeline;
using Microsoft.CodeAnalysis.CSharp;
using static ILInspector.Decompiler.Tests.ReferenceSlotMaterializationTestHelpers;

namespace ILInspector.Decompiler.Tests;

[Trait("Area", "Pass")]
public class CrossBlockSlotMaterializationTests
{
    static readonly TypeRef StringType = TypeRef.CoreLib("System", "String");
    static readonly TypeRef Void = TypeRef.CoreLib("System", "Void");

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EveryCrossBlockObserverMustAgree(bool untyped)
    {
        var first = new Block(0);
        first.Add(new StoreStackSlot(0, new Constant("first", StringType)));
        first.Add(new ExpressionStatement(new LoadStackSlot(0,
            untyped ? null : TypeRef.CoreLib("System", "Object"))));
        first.Add(new Branch(4));
        var second = new Block(4);
        second.Add(new StoreStackSlot(0, new Constant("second", StringType)));
        second.Add(new Return(new LoadStackSlot(0, StringType)));
        var body = new BlockContainer();
        body.Add(first);
        body.Add(second);
        var function = new IrFunction("M", Owner,
            new MethodSignature(StringType, [], HasThis: false, GenericParameterCount: 0), [], body);

        Assert.Equal(untyped
            ? SlotMaterializationVeto.UnderivableTypeTestimony
            : SlotMaterializationVeto.ConflictingTypeTestimony,
            Assert.Single(SlotMaterializationPass.Analyze(function)).Vetoes);
        AssertRetained(function);
    }

    [Fact]
    public void CrossBlockMaterializationPreservesLoopBackedge()
    {
        var first = new Block(0);
        first.Add(new StoreStackSlot(0, new Constant("first", StringType)));
        var entry = new Branch(4);
        first.Add(entry);
        var loop = new Block(4);
        loop.Add(Observe());
        loop.Add(new StoreStackSlot(0, new Constant("next", StringType)));
        loop.Add(Observe());
        var backedge = new Branch(4);
        loop.Add(backedge);
        var body = new BlockContainer();
        body.Add(first);
        body.Add(loop);
        var function = new IrFunction("M", Owner,
            new MethodSignature(Void, [], HasThis: false, GenericParameterCount: 0), [], body);

        Assert.True(Assert.Single(SlotMaterializationPass.Analyze(function)).WillMaterialize);
        var invariant = SlotMaterializationInvariant.Capture(function);
        new SlotMaterializationPass().Run(function, PassContext.None);
        invariant.Check();
        Assert.Same(entry, first.Children[1]);
        Assert.Same(backedge, loop.Children[3]);
        Assert.Equal(StringType, Assert.Single(function.Locals));
        function.CheckInvariant(includeSemantics: true);

        static ExpressionStatement Observe() => new(new Call(
            new MethodRef(Owner, "Observe", Void, [StringType], HasThis: false),
            isVirtual: false, [new LoadStackSlot(0, StringType)]));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CrossBlockCopyComponentsRemainAtomic(bool incomplete)
    {
        var firstValue = new Constant("first", StringType);
        var secondValue = new Constant("second", StringType);
        var first = new Block(0);
        first.Add(new StoreStackSlot(0, firstValue));
        var transfer = new Branch(4);
        first.Add(transfer);
        var second = new Block(4);
        second.Add(new StoreStackSlot(1, new LoadStackSlot(0, StringType)));
        second.Add(new ExpressionStatement(new Call(
            new MethodRef(Owner, "Observe", Void, [StringType], HasThis: false),
            isVirtual: false, [new LoadStackSlot(0, StringType)])));
        second.Add(new StoreStackSlot(0, secondValue));
        if (incomplete)
            second.Add(new ExpressionStatement(new LoadStackSlot(1, type: null)));
        second.Add(new Return(new LoadStackSlot(1, StringType)));
        var body = new BlockContainer();
        body.Add(first);
        body.Add(second);
        var function = new IrFunction("M", Owner,
            new MethodSignature(StringType, [], HasThis: false, GenericParameterCount: 0), [], body);
        var decisions = SlotMaterializationPass.Analyze(function);
        Assert.Equal(2, decisions.Count);
        if (incomplete)
        {
            Assert.All(decisions, decision =>
                Assert.True(decision.Vetoes.HasFlag(SlotMaterializationVeto.IncompleteCopyComponent)));
            AssertRetained(function);
            return;
        }

        Assert.All(decisions, decision => Assert.True(decision.WillMaterialize));
        var invariant = SlotMaterializationInvariant.Capture(function);
        new SlotMaterializationPass().Run(function, PassContext.None);
        invariant.Check();
        Assert.Equal([StringType, StringType], function.Locals);
        Assert.Same(transfer, first.Children[1]);
        Assert.Contains(function.Descendants.OfType<StoreLocal>(), store => ReferenceEquals(store.Value, firstValue));
        Assert.Contains(function.Descendants.OfType<StoreLocal>(), store => ReferenceEquals(store.Value, secondValue));
        function.CheckInvariant(includeSemantics: true);
    }

    [Theory]
    [InlineData(nameof(CrossBlockSlotMaterializationSamples.AccessorName))]
    [InlineData(nameof(CrossBlockSlotMaterializationSamples.SelectedNumber))]
    [InlineData(nameof(CrossBlockSlotMaterializationSamples.ValidateName))]
    [InlineData(nameof(CrossBlockSlotMaterializationSamples.RepeatedConditional))]
    public void CompilerProducedCrossBlockMultiUseMaterializes(string method)
    {
        using var source = MetadataSource.Open(typeof(CrossBlockSlotMaterializationSamples).Assembly.Location);
        AssertCrossBlockMaterializes(source, typeof(CrossBlockSlotMaterializationSamples).FullName!, method);
    }

    [Fact]
    public void RealRoslynAccessorNameMaterializes()
    {
        using var source = MetadataSource.Open(typeof(CSharpCompilation).Assembly.Location);
        AssertCrossBlockMaterializes(source,
            "Microsoft.CodeAnalysis.CSharp.Symbols.SourcePropertyAccessorSymbol", "GetAccessorName");
    }

    [Fact]
    [Trait("Speed", "Slow")]
    [Trait("Area", "Fidelity")]
    public void CompilerProducedCrossBlockFixturesPreserveFidelity()
    {
        var results = FidelityCheck.Evaluate(
            typeof(CrossBlockSlotMaterializationSamples).Assembly.Location,
            type => type == typeof(CrossBlockSlotMaterializationSamples).FullName).ToArray();
        Assert.Equal(4, results.Length);
        foreach (var result in results)
        {
            var existingDifference = result.Method switch
            {
                nameof(CrossBlockSlotMaterializationSamples.AccessorName) => (
                    Original: "ldarg brtrue ldarg brtrue ldstr br ldstr br ldstr ldarg call ret",
                    Recompiled: "ldarg brtrue ldarg brtrue ldstr ldarg call ret ldstr ldarg call ret ldstr ldarg call ret"),
                nameof(CrossBlockSlotMaterializationSamples.SelectedNumber) => (
                    Original: "ldarg brtrue ldarg brtrue ldc.i4 br ldc.i4 br ldc.i4 ldarg add ret",
                    Recompiled: "ldarg brtrue ldarg brtrue ldc.i4 ldarg add ret ldc.i4 ldarg add ret ldc.i4 ldarg add ret"),
                _ => ((string Original, string Recompiled)?)null,
            };
            if (existingDifference is { } expected)
            {
                // Earlier structuring already duplicates the return across arms.
                Assert.Equal(FidelityCheck.CompileBackStatus.OpcodeDiff, result.Status);
                Assert.Equal(expected.Original, result.OriginalOpcodes);
                Assert.Equal(expected.Recompiled, result.RecompiledOpcodes);
            }
            else
            {
                Assert.True(result.Status == FidelityCheck.CompileBackStatus.Exact,
                    $"{result.Method}: {result.Status}: {result.Detail}");
            }
        }
    }

    static void AssertCrossBlockMaterializes(MetadataSource source, string type, string method)
    {
        var function = RaiseToMaterialization(source, type, method);
        var nodes = CoercionSinks.ScopeNodes(function.Body).ToArray();
        var crossBlock = SlotMaterializationPass.Analyze(function).Where(decision =>
            ReferenceEquals(decision.Scope, function)
            && nodes.OfType<StoreStackSlot>().Where(store => store.Slot == decision.Slot)
                .Select(store => store.Parent).Distinct().Count() > 1
            && nodes.OfType<LoadStackSlot>().Count(load => load.Slot == decision.Slot) > 1).ToArray();
        Assert.NotEmpty(crossBlock);
        Assert.All(crossBlock, decision => Assert.True(decision.WillMaterialize, decision.Vetoes.ToString()));
        var invariant = SlotMaterializationInvariant.Capture(function);
        new SlotMaterializationPass().Run(function, PassContext.None);
        invariant.Check();
        new CoercionInsertionPass().Run(function, PassContext.None);
        Assert.Empty(CoercionInvariant.Check(function));
        function.CheckInvariant(includeSemantics: true);
    }
}
