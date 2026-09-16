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
    static readonly TypeRef Boolean = TypeRef.CoreLib("System", "Boolean");
    static readonly TypeRef Int32 = TypeRef.CoreLib("System", "Int32");
    static readonly TypeRef Object = TypeRef.CoreLib("System", "Object");

    [Theory]
    [InlineData("boolean")]
    [InlineData("numeric-observer")]
    [InlineData("mixed-producers")]
    [InlineData("integer")]
    public void BoxObserversParticipateInCrossBlockIdentity(string variant)
    {
        var entry = new Block(0);
        entry.Add(new ConditionalBranch(new LoadArgument(0, "flag", Boolean), 4));
        entry.Add(new Branch(8));
        var first = new Block(4);
        first.Add(new StoreStackSlot(0, variant == "integer"
            ? new Constant(7, Int32) : new Constant(true, Boolean)));
        var firstBox = new Box(variant == "integer" ? Int32 : Boolean, new LoadStackSlot(0, Int32));
        first.Add(new Return(firstBox));
        var second = new Block(8);
        second.Add(new StoreStackSlot(0, variant is "integer" or "mixed-producers"
            ? new Constant(2, Int32) : new Constant(false, Boolean)));
        var secondBox = new Box(variant is "integer" or "numeric-observer" ? Int32 : Boolean,
            new LoadStackSlot(0, Int32));
        second.Add(new Return(secondBox));
        var body = new BlockContainer();
        body.Add(entry);
        body.Add(first);
        body.Add(second);
        var function = new IrFunction("M", Owner,
            new MethodSignature(Object, [new Parameter("flag", Boolean)],
                HasThis: false, GenericParameterCount: 0), [], body);

        var decision = Assert.Single(SlotMaterializationPass.Analyze(function));
        if (variant is "numeric-observer" or "mixed-producers")
        {
            Assert.Equal(variant == "numeric-observer"
                ? SlotMaterializationVeto.ConflictingTypeTestimony
                : SlotMaterializationVeto.BooleanSinkIdentityRecovery, decision.Vetoes);
            AssertRetained(function);
            return;
        }

        var expectedType = variant == "integer" ? Int32 : Boolean;
        Assert.True(decision.WillMaterialize);
        Assert.Equal(expectedType, decision.Type);
        Assert.Contains($"{(variant == "integer" ? "int" : "bool")} S_0", CSharpPrinter.Print(function).Output);
        var invariant = SlotMaterializationInvariant.Capture(function);
        new SlotMaterializationPass().Run(function, PassContext.None);
        invariant.Check();
        Assert.Equal(expectedType, Assert.Single(function.Locals));
        Assert.Equal(firstBox.Type, Assert.IsType<LoadLocal>(firstBox.Operand).Type);
        Assert.Equal(secondBox.Type, Assert.IsType<LoadLocal>(secondBox.Operand).Type);
        function.CheckInvariant(includeSemantics: true);
    }

    [Fact]
    public void RealRoslynBooleanBoxesMaterializeBooleanStorage()
    {
        using var source = MetadataSource.Open(typeof(CSharpCompilation).Assembly.Location);
        AssertBooleanBoxesMaterialize(source,
            "Microsoft.CodeAnalysis.CSharp.Binder", "FoldNeverOverflowBinaryOperators");
    }

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

    static void AssertBooleanBoxesMaterialize(MetadataSource source, string type, string method)
    {
        var function = RaiseToMaterialization(source, type, method);
        var nodes = CoercionSinks.ScopeNodes(function.Body).ToArray();
        var boxes = nodes.OfType<Box>().Where(box => Boolean.Equals(box.Type)
            && box.Operand is LoadStackSlot { Type: { } loadType } && Int32.Equals(loadType)).ToArray();
        Assert.Equal(2, boxes.Length);
        var slot = Assert.Single(boxes.Select(box => Assert.IsType<LoadStackSlot>(box.Operand).Slot).Distinct());
        var stores = nodes.OfType<StoreStackSlot>().Where(store => store.Slot == slot).ToArray();
        Assert.Equal(2, stores.Length);
        Assert.Equal(2, stores.Select(store => store.Parent).Distinct().Count());
        Assert.All(stores, store => Assert.Equal(Boolean, store.Value.ResultType));
        var decision = Assert.Single(SlotMaterializationPass.Analyze(function),
            decision => ReferenceEquals(decision.Scope, function) && decision.Slot == slot);
        Assert.True(decision.WillMaterialize, decision.Vetoes.ToString());
        Assert.Equal(Boolean, decision.Type);

        var invariant = SlotMaterializationInvariant.Capture(function);
        new SlotMaterializationPass().Run(function, PassContext.None);
        invariant.Check();
        var context = PassContext.ForImport(reference => IrImporter.Import(source, reference),
            source.AreProvablyDisjoint);
        foreach (var pass in IrPasses.Default.SkipWhile(pass => pass is not SlotMaterializationPass).Skip(1))
            pass.Run(function, context);

        Assert.All(boxes, box =>
        {
            Assert.Contains(box, function.Descendants);
            Assert.Equal(Boolean, Assert.IsType<LoadLocal>(box.Operand).Type);
        });
        Assert.Contains($"bool S_{slot}", CSharpPrinter.Print(function).Output);
        Assert.Empty(CoercionInvariant.Check(function));
        function.CheckInvariant(includeSemantics: true);
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
