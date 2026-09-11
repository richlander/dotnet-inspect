using System.Collections.Immutable;
using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

[Trait("Area", "Pass")]
public class SlotMaterializationPassTests
{
    static readonly TypeRef Int32 = TypeRef.CoreLib("System", "Int32");
    static readonly TypeRef Int64 = TypeRef.CoreLib("System", "Int64");
    static readonly TypeRef Boolean = TypeRef.CoreLib("System", "Boolean");
    static readonly TypeRef Char = TypeRef.CoreLib("System", "Char");
    static readonly TypeRef Void = TypeRef.CoreLib("System", "Void");
    static readonly TypeRef StringType = TypeRef.CoreLib("System", "String");
    static readonly TypeRef Action = TypeRef.CoreLib("System", "Action");
    static readonly TypeRef Owner = TypeRef.Definition("Synthetic", "Samples", "Owner");

    static IrFunction Function(ImmutableArray<TypeRef> locals, BlockContainer body)
        => new("M", Owner, new MethodSignature(TypeRef.CoreLib("System", "Void"), [
            new Parameter("x", Int32),
        ], HasThis: false, GenericParameterCount: 0), locals, body);

    // Adversarial review (5b-2, CRITICAL): a materialized store whose value
    // nests a load of a slot processed later. Cloning that store before the
    // nested load was replaced injected a fresh LoadStackSlot into the live
    // tree while the pass replaced the original inside the discarded subtree,
    // leaving the live one orphaned forever. The two-phase rewrite (all loads,
    // then all stores) retires every slot node.
    [Fact]
    public void MaterializesCoupledSlotsWithoutOrphaningNestedLoads()
    {
        var body = new BlockContainer();
        var block = new Block(0);
        // Slot 0's first load appears before slot 256's, so slot 0 is
        // processed first — the ordering that triggered the orphan.
        block.Add(new StoreStackSlot(0, new Constant(1, Int32)));
        block.Add(new StoreLocal(0, Int32, new LoadStackSlot(0, Int32)));
        block.Add(new StoreStackSlot(256, new LoadArgument(0, "x", Int32)));
        block.Add(new StoreStackSlot(0, new Binary(BinaryKind.Add, isChecked: false, isUnsigned: false,
            new LoadStackSlot(256, Int32), new Constant(1, Int32))));
        block.Add(new StoreLocal(1, Int32, new LoadStackSlot(0, Int32)));
        body.Add(block);
        var function = Function([Int32, Int32], body);

        new SlotMaterializationPass().Run(function, PassContext.None);

        Assert.Empty(function.Descendants.OfType<LoadStackSlot>());
        Assert.Empty(function.Descendants.OfType<StoreStackSlot>());
        Assert.Equal(4, function.Locals.Length);
    }

    [Fact]
    public void MaterializesCompleteDirectCopyComponent()
    {
        var body = new BlockContainer();
        var block = new Block(0);
        block.Add(new StoreStackSlot(256, new LoadArgument(0, "x", Int32)));
        block.Add(new StoreStackSlot(1, new LoadStackSlot(256, Int32)));
        block.Add(new StoreStackSlot(0, new LoadStackSlot(1, Int32)));
        block.Add(new StoreLocal(0, Int32, new LoadStackSlot(0, Int32)));
        body.Add(block);
        var function = Function([Int32], body);

        var decisions = SlotMaterializationPass.Analyze(function);
        Assert.All(decisions, static decision => Assert.True(decision.WillMaterialize));

        new SlotMaterializationPass().Run(function, PassContext.None);

        var output = CSharpPrinter.Print(function).Output;
        Assert.Empty(function.Descendants.OfType<LoadStackSlot>());
        Assert.Empty(function.Descendants.OfType<StoreStackSlot>());
        Assert.Equal(4, function.Locals.Length);
        Assert.Contains("int S_256 = x;", output);
        Assert.Contains("int S_1 = S_256;", output);
        Assert.Contains("int S_0 = S_1;", output);
        function.CheckInvariant();
    }

    // The conditional producer and boolean sink can occupy different members
    // of one direct-copy component. The sink-end veto must keep the whole
    // component on the printer's boolean identity recovery.
    [Fact]
    public void DefersBooleanSinkIdentityAcrossDirectCopyComponent()
    {
        var body = new BlockContainer();
        var first = new Block(0);
        first.Add(new StoreStackSlot(0, new Conditional(
            new Comparison(
                ComparisonKind.NotEqual,
                isUnsigned: false,
                new LoadArgument(0, "x", Int32),
                new Constant(0, Int32)),
            new Constant(true, Boolean),
            new Constant(false, Boolean))));
        first.Add(new Branch(4));
        var second = new Block(4);
        second.Add(new StoreStackSlot(1, new LoadStackSlot(0, Boolean)));
        second.Add(new Branch(8));
        var third = new Block(8);
        third.Add(new StoreLocal(0, Boolean, new LoadStackSlot(1, Int32)));
        body.Add(first);
        body.Add(second);
        body.Add(third);
        var function = Function([Boolean], body);

        var decisions = SlotMaterializationPass.Analyze(function);
        Assert.Contains(decisions, decision => decision.Slot == 0
            && decision.Vetoes == SlotMaterializationVeto.IncompleteCopyComponent);
        Assert.Contains(decisions, decision => decision.Slot == 1
            && decision.Vetoes.HasFlag(SlotMaterializationVeto.BooleanSinkIdentityRecovery)
            && decision.Vetoes.HasFlag(SlotMaterializationVeto.IncompleteCopyComponent));

        new SlotMaterializationPass().Run(function, PassContext.None);
        new CoercionInsertionPass().Run(function, PassContext.None);

        var output = CSharpPrinter.Print(function).Output;
        Assert.Equal(2, function.Descendants.OfType<LoadStackSlot>().Count());
        Assert.Equal(2, function.Descendants.OfType<StoreStackSlot>().Count());
        Assert.Contains("bool S_0", output);
        Assert.Contains("bool S_1", output);
        Assert.DoesNotContain("int S_1", output);
        Assert.Empty(CoercionInvariant.Check(function));
        function.CheckInvariant();
    }

    [Fact]
    public void DefersWholeDirectCopyComponentWhenOneSlotIsUndecided()
    {
        var body = new BlockContainer();
        var block = new Block(0);
        block.Add(new StoreStackSlot(256, new LoadArgument(0, "x", Int32)));
        block.Add(new StoreStackSlot(1, new LoadStackSlot(256, Int32)));
        block.Add(new StoreStackSlot(0, new LoadStackSlot(1, Int32)));
        block.Add(new StoreLocal(0, Int32, new LoadStackSlot(0, Int32)));
        block.Add(new StoreLocal(1, Char, new LoadStackSlot(0, Char)));
        block.Add(new StoreStackSlot(5, new Constant(1, Int32)));
        block.Add(new StoreLocal(2, Int32, new LoadStackSlot(5, Int32)));
        body.Add(block);
        var function = Function([Int32, Char, Int32], body);

        var decisions = SlotMaterializationPass.Analyze(function);
        Assert.Contains(decisions, decision => decision.Slot == 0
            && decision.Vetoes.HasFlag(SlotMaterializationVeto.ConflictingTypeTestimony)
            && decision.Vetoes.HasFlag(SlotMaterializationVeto.IncompleteCopyComponent));
        Assert.Contains(decisions, decision => decision.Slot == 1
            && decision.Vetoes == SlotMaterializationVeto.IncompleteCopyComponent);
        Assert.Contains(decisions, decision => decision.Slot == 256
            && decision.Vetoes == SlotMaterializationVeto.IncompleteCopyComponent);
        Assert.Contains(decisions, decision => decision.Slot == 5 && decision.WillMaterialize);

        new SlotMaterializationPass().Run(function, PassContext.None);

        Assert.Equal(3, function.Descendants.OfType<StoreStackSlot>().Count());
        Assert.Equal(4, function.Descendants.OfType<LoadStackSlot>().Count());
        Assert.Equal(4, function.Locals.Length);
        Assert.Contains(function.Descendants.OfType<StoreLocal>(), store => store.Index == 3);
        function.CheckInvariant();
    }

    // Historical 5b-2 boundary: #2356 made inner naming collision-free, but
    // removing this conservative gate is a separate behavior slice from
    // exposing its measured population.
    [Fact]
    public void DefersSlotWhoseNumberAppearsInNestedLambdaScope()
    {
        var lambdaBody = new BlockContainer();
        var lambdaBlock = new Block(0);
        lambdaBlock.Add(new StoreStackSlot(0, new Constant(2, Int32)));
        lambdaBlock.Add(new Return(new LoadStackSlot(0, Int32)));
        lambdaBody.Add(lambdaBlock);
        var lambda = new Lambda(Action, [], [], [],
            usesUpdatedMemorySafetyRules: false, skipLocalsInit: false, lambdaBody);

        var body = new BlockContainer();
        var block = new Block(0);
        block.Add(new StoreStackSlot(0, new Constant(1, Int32)));
        block.Add(new StoreLocal(0, Int32, new LoadStackSlot(0, Int32)));
        block.Add(new StoreLocal(1, Action, lambda));
        body.Add(block);
        var function = Function([Int32, Action], body);

        var decisions = SlotMaterializationPass.Analyze(function);
        Assert.Contains(decisions, decision => decision.Slot == 0
            && ReferenceEquals(decision.Scope, function)
            && decision.Vetoes.HasFlag(SlotMaterializationVeto.NestedSlotNumberCollision));
        Assert.Contains(decisions, decision => decision.Slot == 0
            && ReferenceEquals(decision.Scope, lambda)
            && decision.Vetoes == SlotMaterializationVeto.NestedScope);

        new SlotMaterializationPass().Run(function, PassContext.None);

        // Outer slot 0 deferred; the lambda's slot 0 untouched.
        Assert.Equal(2, function.Descendants.OfType<StoreStackSlot>().Count());
        Assert.Equal(2, function.Locals.Length);
    }

    // 5b-2 Opus review (de-inlining): a multi-store slot with a single read is
    // the printer's inline consumer fold (`Use(c ? a : b)`); materializing it
    // renders a branchy statement-level assignment instead. It stays deferred.
    [Fact]
    public void DefersMultiStoreSlotWithSingleRead()
    {
        var body = new BlockContainer();
        var thenBlock = new Block(0);
        thenBlock.Add(new StoreStackSlot(0, new Constant(1, Int32)));
        thenBlock.Add(new Branch(8));
        var elseBlock = new Block(4);
        elseBlock.Add(new StoreStackSlot(0, new Constant(2, Int32)));
        var join = new Block(8);
        join.Add(new StoreLocal(0, Int32, new LoadStackSlot(0, Int32)));
        foreach (var block in (Block[])[thenBlock, elseBlock, join])
            body.Add(block);
        var function = Function([Int32], body);

        var decision = Assert.Single(SlotMaterializationPass.Analyze(function));
        Assert.True(decision.Vetoes.HasFlag(SlotMaterializationVeto.MultiStoreSingleLoadFold));
        Assert.True(decision.Vetoes.HasFlag(SlotMaterializationVeto.CrossBlockStoreFold));

        new SlotMaterializationPass().Run(function, PassContext.None);

        Assert.Equal(2, function.Descendants.OfType<StoreStackSlot>().Count());
        Assert.Single(function.Locals);
    }

    [Fact]
    public void MaterializesSingleStoreConditionalWithSingleRead()
    {
        var body = new BlockContainer();
        var storeBlock = new Block(0);
        storeBlock.Add(new StoreStackSlot(0, new Conditional(
            new Comparison(
                ComparisonKind.NotEqual,
                isUnsigned: false,
                new LoadArgument(0, "x", Int32),
                new Constant(0, Int32)),
            new Constant(1, Int32),
            new Constant(2, Int32))));
        storeBlock.Add(new Branch(4));
        var loadBlock = new Block(4);
        loadBlock.Add(new StoreLocal(0, Int32, new LoadStackSlot(0, Int32)));
        body.Add(storeBlock);
        body.Add(loadBlock);
        var function = Function([Int32], body);

        var decision = Assert.Single(SlotMaterializationPass.Analyze(function));
        Assert.True(decision.WillMaterialize);

        new SlotMaterializationPass().Run(function, PassContext.None);

        Assert.Empty(function.Descendants.OfType<LoadStackSlot>());
        Assert.Empty(function.Descendants.OfType<StoreStackSlot>());
        Assert.Equal(2, function.Locals.Length);
        Assert.Contains(function.Descendants.OfType<StoreLocal>(), store =>
            store.Index == 1 && store.Value is Conditional);
        function.CheckInvariant();
    }

    [Fact]
    public void MaterializedConditionalUsesLoadTestimonyForCoercion()
    {
        var body = new BlockContainer();
        var block = new Block(0);
        block.Add(new StoreStackSlot(0, new Conditional(
            new Comparison(
                ComparisonKind.NotEqual,
                isUnsigned: false,
                new LoadArgument(0, "x", Int32),
                new Constant(0, Int32)),
            new Constant(true, Boolean),
            new Constant(false, Boolean))));
        block.Add(new StoreLocal(0, Int32, new LoadStackSlot(0, Int32)));
        body.Add(block);
        var function = Function([Int32], body);

        var decision = Assert.Single(SlotMaterializationPass.Analyze(function));
        Assert.Equal(Int32, decision.Type);
        Assert.True(decision.WillMaterialize);

        new SlotMaterializationPass().Run(function, PassContext.None);
        new CoercionInsertionPass().Run(function, PassContext.None);

        var materialized = Assert.Single(
            function.Descendants.OfType<StoreLocal>(),
            store => store.Index == 1);
        var conditional = Assert.IsType<Conditional>(materialized.Value);
        Assert.Equal(Boolean, conditional.ResultType);
        Assert.Equal(Int32, materialized.Type);
        Assert.Contains("? 1 : 0", CSharpPrinter.Print(function).Output);
        Assert.Empty(CoercionInvariant.Check(function));
        function.CheckInvariant();
    }

    [Fact]
    public void DefersIntegerTestimonyWhenConditionalFeedsBooleanProperty()
    {
        var setter = new MethodRef(
            Owner,
            "set_Flag",
            Void,
            [Boolean],
            HasThis: false)
        {
            IsSpecialName = true,
        };
        var body = new BlockContainer();
        var block = new Block(0);
        block.Add(new StoreStackSlot(0, new Conditional(
            new Comparison(
                ComparisonKind.NotEqual,
                isUnsigned: false,
                new LoadArgument(0, "x", Int32),
                new Constant(0, Int32)),
            new Constant(true, Boolean),
            new Constant(false, Boolean))));
        block.Add(new StoreProperty(
            setter,
            instance: null,
            [],
            new LoadStackSlot(0, Int32)));
        body.Add(block);
        var function = Function([], body);

        var decision = Assert.Single(SlotMaterializationPass.Analyze(function));
        Assert.Equal(Int32, decision.Type);
        Assert.Equal(SlotMaterializationVeto.BooleanSinkIdentityRecovery, decision.Vetoes);

        new SlotMaterializationPass().Run(function, PassContext.None);

        Assert.Single(function.Descendants.OfType<LoadStackSlot>());
        Assert.Single(function.Descendants.OfType<StoreStackSlot>());
        Assert.Empty(function.Locals);
        Assert.Contains("bool S_0", CSharpPrinter.Print(function).Output);
        function.CheckInvariant();
    }

    [Fact]
    public void DefersIntegerTestimonyWhenConditionalFeedsBooleanLocal()
    {
        var body = new BlockContainer();
        var block = new Block(0);
        block.Add(new StoreStackSlot(0, new Conditional(
            new Comparison(
                ComparisonKind.NotEqual,
                isUnsigned: false,
                new LoadArgument(0, "x", Int32),
                new Constant(0, Int32)),
            new Constant(true, Boolean),
            new Constant(false, Boolean))));
        block.Add(new StoreLocal(0, Boolean, new LoadStackSlot(0, Int32)));
        body.Add(block);
        var function = Function([Boolean], body);

        var decision = Assert.Single(SlotMaterializationPass.Analyze(function));
        Assert.Equal(Int32, decision.Type);
        Assert.Equal(SlotMaterializationVeto.BooleanSinkIdentityRecovery, decision.Vetoes);

        new SlotMaterializationPass().Run(function, PassContext.None);

        Assert.Single(function.Descendants.OfType<LoadStackSlot>());
        Assert.Single(function.Descendants.OfType<StoreStackSlot>());
        Assert.Single(function.Locals);
        Assert.Contains("bool S_0", CSharpPrinter.Print(function).Output);
        function.CheckInvariant();
    }

    // Adversarial review (5b-2 round 2, blocking): a typed-int slot feeding a
    // char[] element store is the printer's #1751 identity recovery — the
    // slot RE-TYPES to char ('+' / '-'), which a materialized int local
    // forecloses. Slots whose element-store target disagrees with the
    // testified type stay on the unifier.
    [Fact]
    public void DefersSlotWhoseElementStoreTargetDisagreesWithTestimony()
    {
        var body = new BlockContainer();
        var block = new Block(0);
        block.Add(new StoreStackSlot(0, new Constant(43, Int32)));
        block.Add(new StoreElement(Char,
            new LoadArgument(0, "chars", TypeRef.SzArray(Char)),
            new Constant(0, Int32),
            new LoadStackSlot(0, Int32)));
        body.Add(block);
        var function = Function([], body);

        var decision = Assert.Single(SlotMaterializationPass.Analyze(function));
        Assert.Equal(SlotMaterializationVeto.ElementStoreIdentityRecovery, decision.Vetoes);

        new SlotMaterializationPass().Run(function, PassContext.None);

        Assert.Single(function.Descendants.OfType<StoreStackSlot>());
        Assert.Empty(function.Locals);
    }

    // Adversarial review (5b-2, latent): LoadSinkTargetType omitted
    // StoreElement even though the sink model handles it — an untyped slot
    // load consumed by an array-element store contributed no testimony,
    // vetoing (or mis-typing) the slot. It now testifies the element type.
    [Fact]
    public void StoreElementSinkTestifiesElementType()
    {
        var body = new BlockContainer();
        var block = new Block(0);
        block.Add(new StoreStackSlot(1, new Constant('a', Char)));
        block.Add(new StoreElement(Char,
            new LoadArgument(0, "chars", TypeRef.SzArray(Char)),
            new Constant(0, Int32),
            new LoadStackSlot(1, type: null)));
        body.Add(block);

        var testified = CoercionSinks.TestifiedSlotTypes(
            body, returnType: null, ImmutableDictionary<TypeRef, TypeShape>.Empty);

        Assert.Equal(Char, testified[1]);
    }

    [Fact]
    public void AnalysisAttributesEveryFunctionScopeSlotAndOverlappingVeto()
    {
        var body = new BlockContainer();
        var block = new Block(0);
        block.Add(new StoreStackSlot(0, new Constant(1, Int32)));
        block.Add(new ExpressionStatement(new LoadStackSlot(0, type: null)));

        block.Add(new StoreStackSlot(1, new Constant(1, Int32)));
        block.Add(new StoreLocal(0, Int32, new LoadStackSlot(1, Int32)));
        block.Add(new StoreLocal(1, Char, new LoadStackSlot(1, Char)));

        block.Add(new StoreStackSlot(2, new Constant(2, Int32)));
        block.Add(new StoreLocal(2, Int32, new LoadStackSlot(3, Int32)));

        block.Add(new StoreStackSlot(4, new Constant("x", StringType)));
        block.Add(new StoreLocal(3, StringType, new LoadStackSlot(4, StringType)));

        block.Add(new StoreStackSlot(5, new Constant(5, Int32)));
        block.Add(new StoreStackSlot(5, new Constant(5L, Int64)));
        block.Add(new StoreLocal(4, Int32, new LoadStackSlot(5, Int32)));
        body.Add(block);
        var function = Function([Int32, Char, Int32, StringType, Int32], body);

        var decisions = SlotMaterializationPass.Analyze(function);

        Assert.Equal(6, decisions.Count);
        Assert.Contains(decisions, decision => decision.Slot == 0
            && decision.Vetoes.HasFlag(SlotMaterializationVeto.UnderivableTypeTestimony));
        Assert.Contains(decisions, decision => decision.Slot == 1
            && decision.Vetoes.HasFlag(SlotMaterializationVeto.ConflictingTypeTestimony));
        Assert.Contains(decisions, decision => decision.Slot == 2
            && decision.Vetoes.HasFlag(SlotMaterializationVeto.MissingLoad));
        Assert.Contains(decisions, decision => decision.Slot == 3
            && decision.Vetoes.HasFlag(SlotMaterializationVeto.MissingStore));
        Assert.Contains(decisions, decision => decision.Slot == 4
            && decision.Vetoes.HasFlag(SlotMaterializationVeto.OutsideCoercionDomain));
        Assert.Contains(decisions, decision => decision.Slot == 5
            && decision.Vetoes.HasFlag(SlotMaterializationVeto.UnrenderableStoreType)
            && decision.Vetoes.HasFlag(SlotMaterializationVeto.MultiStoreSingleLoadFold));
    }

    [Fact]
    public void AnalysisIdentifiesRepeatedSlotNumbersByOwningScope()
    {
        Lambda NestedLambda()
        {
            var nestedBlock = new Block();
            nestedBlock.Add(new StoreStackSlot(0, new Constant(1, Int32)));
            nestedBlock.Add(new Return(new LoadStackSlot(0, Int32)));
            var nestedBody = new BlockContainer();
            nestedBody.Add(nestedBlock);
            return new Lambda(Action, [], [], [],
                usesUpdatedMemorySafetyRules: false, skipLocalsInit: false, nestedBody);
        }

        var first = NestedLambda();
        var second = NestedLambda();
        var body = new BlockContainer();
        var block = new Block();
        block.Add(new StoreLocal(0, Action, first));
        block.Add(new StoreLocal(1, Action, second));
        body.Add(block);
        var function = Function([Action, Action], body);

        var decisions = SlotMaterializationPass.Analyze(function);

        Assert.Equal(2, decisions.Count);
        Assert.Contains(decisions, decision => ReferenceEquals(decision.Scope, first)
            && decision.Slot == 0
            && decision.Vetoes == SlotMaterializationVeto.NestedScope);
        Assert.Contains(decisions, decision => ReferenceEquals(decision.Scope, second)
            && decision.Slot == 0
            && decision.Vetoes == SlotMaterializationVeto.NestedScope);
        Assert.NotEqual(decisions[0], decisions[1]);
    }

    [Fact]
    public void PreservesFirstTestimonyOrderWhenAppendingMaterializedLocals()
    {
        var body = new BlockContainer();
        var block = new Block();
        block.Add(new StoreStackSlot(0, new Constant(0, Int32)));
        block.Add(new StoreStackSlot(1, new Constant(1, Int32)));
        block.Add(new StoreLocal(0, Int32, new LoadStackSlot(1, Int32)));
        block.Add(new StoreLocal(1, Int32, new LoadStackSlot(0, Int32)));
        body.Add(block);
        var function = Function([Int32, Int32], body);

        new SlotMaterializationPass().Run(function, PassContext.None);

        Assert.Equal([null, null], function.LocalNames[^2..]);
        Assert.Equal(["S_1", "S_0"], function.SynthesizedLocalNames[^2..]);
    }

    [Fact]
    public void MaterializationPreservesNestedSlotWebScopeIdentity()
    {
        var nestedBlock = new Block();
        nestedBlock.Add(new StoreStackSlot(1, new Constant(1, Int32)));
        nestedBlock.Add(new Return(new LoadStackSlot(1, Int32)));
        var nestedBody = new BlockContainer();
        nestedBody.Add(nestedBlock);
        var lambda = new Lambda(Action, [], [], [],
            usesUpdatedMemorySafetyRules: false, skipLocalsInit: false, nestedBody);

        var body = new BlockContainer();
        var block = new Block();
        block.Add(new StoreStackSlot(0, new Call(
            new MethodRef(Owner, "Use", Int32, [Action], HasThis: false),
            isVirtual: false,
            [lambda])));
        block.Add(new StoreLocal(0, Int32, new LoadStackSlot(0, Int32)));
        block.Add(new StoreLocal(1, Int32, new LoadStackSlot(0, Int32)));
        body.Add(block);
        var function = Function([Int32, Int32], body);
        var nestedDecision = Assert.Single(
            SlotMaterializationPass.Analyze(function),
            decision => decision.Vetoes == SlotMaterializationVeto.NestedScope);

        new SlotMaterializationPass().Run(function, PassContext.None);

        Assert.Same(nestedDecision.Scope, Assert.Single(function.Descendants.OfType<Lambda>()));
        Assert.Single(function.Descendants.OfType<StoreStackSlot>());
        Assert.Single(function.Descendants.OfType<LoadStackSlot>());
        function.CheckInvariant();
    }

    [Fact]
    public void AnalysisAttributesStructuralFoldsWithoutTypeTestimony()
    {
        var body = new BlockContainer();
        var firstStore = new Block(0);
        firstStore.Add(new StoreStackSlot(0, new Constant(1, Int32)));
        var secondStore = new Block(4);
        secondStore.Add(new StoreStackSlot(0, new Constant(2, Int32)));
        var loadBlock = new Block(8);
        loadBlock.Add(new ExpressionStatement(new LoadStackSlot(0, type: null)));
        body.Add(firstStore);
        body.Add(secondStore);
        body.Add(loadBlock);
        var function = Function([], body);

        var decision = Assert.Single(SlotMaterializationPass.Analyze(function));

        Assert.Equal(
            SlotMaterializationVeto.UnderivableTypeTestimony
                | SlotMaterializationVeto.MultiStoreSingleLoadFold
                | SlotMaterializationVeto.CrossBlockStoreFold,
            decision.Vetoes);
    }
}
