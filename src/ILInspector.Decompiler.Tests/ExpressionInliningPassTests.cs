using System.Collections.Immutable;
using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

public class ExpressionInliningPassTests
{
    static readonly TypeRef Holder = TypeRef.CoreLib("Synthetic", "Holder");
    static readonly TypeRef Void = TypeRef.CoreLib("System", "Void");
    static readonly TypeRef Int32 = TypeRef.CoreLib("System", "Int32");
    static readonly TypeRef ExceptionType = TypeRef.CoreLib("System", "Exception");
    static readonly TypeRef Object = TypeRef.CoreLib("System", "Object");
    static readonly TypeRef Action = TypeRef.CoreLib("System", "Action");

    static string PrintRaised(string methodName)
    {
        using var source = MetadataSource.Open(typeof(CfgSampleClass).Assembly.Location);
        var function = IrImporter.Import(source, typeof(CfgSampleClass).FullName!, methodName);
        Assert.NotNull(function);

        var result = CSharpPrinter.PrintRaised(function!, method => IrImporter.Import(source, method));
        Assert.True(result.Succeeded, string.Join("\n", result.Diagnostics.Select(d => d.Message)));
        Assert.NotNull(result.Output);
        return result.Output!.ReplaceLineEndings("\n").Trim();
    }

    // The collapsed cache leaves the chain spilled across reused stack slots
    // (`S_0 = xs; S_1 = x => ...; S_0 = Where(S_0, S_1); ...`). Live-range
    // inlining folds those temps into the call arguments, leaving one statement.
    [Fact]
    public void CachedDelegateArgument_InlinesToSingleCall()
    {
        string output = PrintRaised(nameof(CfgSampleClass.CachedDelegateArgument));

        Assert.Equal(1, output.Count(c => c == ';'));
        Assert.StartsWith("return ", output);
        Assert.Contains("Where", output);
        Assert.Contains("x => x > 0", output);
    }

    [Fact]
    public void CachedDelegateChain_InlinesToSingleNestedExpression()
    {
        string output = PrintRaised(nameof(CfgSampleClass.CachedDelegateChain));

        Assert.Equal(1, output.Count(c => c == ';'));
        Assert.StartsWith("return ", output);
        // The first call's result feeds the second as its receiver argument.
        int where = output.IndexOf("Where", StringComparison.Ordinal);
        int select = output.IndexOf("Select", StringComparison.Ordinal);
        Assert.True(where >= 0 && select >= 0 && select < where,
            $"expected Select(Where(...), ...) nesting, got: {output}");
        Assert.Contains("x => x > 0", output);
        Assert.Contains("x => x * 2", output);
    }

    [Fact]
    public void StoreBeforeTry_DoesNotInlineIntoCatchFilter()
    {
        var body = new BlockContainer();
        var block = new Block(0);
        var filter = new Comparison(
            ComparisonKind.Equal,
            isUnsigned: false,
            new LoadLocal(0, Int32),
            new Constant(0, Int32));
        block.Add(new StoreLocal(0, Int32, new LoadLocal(1, Int32)));
        var tryBody = new BlockContainer();
        tryBody.Add(new Block(1));
        var catchBody = new BlockContainer();
        catchBody.Add(new Block(2));
        block.Add(new TryCatch(
            tryBody,
            [new CatchClause(ExceptionType, catchBody, filter)]));
        body.Add(block);
        var function = new IrFunction(
            "M",
            Holder,
            new MethodSignature(Void, [], HasThis: false, GenericParameterCount: 0),
            [Int32, Int32],
            body);

        new ExpressionInliningPass().Run(function, PassContext.None);

        Assert.Contains(block.Children.OfType<StoreLocal>(), store => store.Index == 0);
        var clause = Assert.Single(function.Descendants.OfType<CatchClause>());
        Assert.NotNull(clause.Filter);
        Assert.Contains(clause.Filter.Descendants.OfType<LoadLocal>(), load => load.Index == 0);
        Assert.DoesNotContain(clause.Filter.Descendants.OfType<LoadLocal>(), load => load.Index == 1);
    }

    [Fact]
    public void StoreBeforeTry_FilterReadBlocksInliningIntoOtherUse()
    {
        var body = new BlockContainer();
        var block = new Block(0);
        var filter = new Comparison(
            ComparisonKind.Equal,
            isUnsigned: false,
            new LoadLocal(0, Int32),
            new Constant(0, Int32));
        block.Add(new StoreLocal(0, Int32, new LoadLocal(1, Int32)));
        block.Add(new ExpressionStatement(new LoadLocal(0, Int32)));
        var tryBody = new BlockContainer();
        tryBody.Add(new Block(1));
        var catchBody = new BlockContainer();
        catchBody.Add(new Block(2));
        block.Add(new TryCatch(
            tryBody,
            [new CatchClause(ExceptionType, catchBody, filter)]));
        body.Add(block);
        var function = new IrFunction(
            "M",
            Holder,
            new MethodSignature(Void, [], HasThis: false, GenericParameterCount: 0),
            [Int32, Int32],
            body);

        new ExpressionInliningPass().Run(function, PassContext.None);

        Assert.Contains(block.Children.OfType<StoreLocal>(), store => store.Index == 0);
        Assert.Contains(block.Children.OfType<ExpressionStatement>(), statement =>
            statement.Expression is LoadLocal { Index: 0 });
    }

    [Fact]
    public void DelegateCreationTargetFieldWrite_BlocksLiveRangeInlining()
    {
        var receiverField = new FieldRef(Holder, "Receiver", Object);
        var target = new MethodRef(Holder, "M", Void, [], HasThis: true);
        var use = new MethodRef(Holder, "Use", Void, [Action], HasThis: false);

        var body = new BlockContainer();
        var block = new Block(0);
        block.Add(new StoreStackSlot(0, new DelegateCreation(
            Action,
            target,
            isVirtual: false,
            new LoadField(receiverField, instance: null))));
        block.Add(new StoreField(receiverField, instance: null, new LoadArgument(0, "newReceiver", Object)));
        block.Add(new ExpressionStatement(new Call(use, isVirtual: false, [new LoadStackSlot(0, Action)])));
        body.Add(block);
        var function = new IrFunction(
            "M",
            Holder,
            new MethodSignature(Void, [new Parameter("newReceiver", Object)], HasThis: false, GenericParameterCount: 0),
            [],
            body);

        new ExpressionInliningPass().Run(function, PassContext.None);

        Assert.Contains(block.Children.OfType<StoreStackSlot>(), store => store.Slot == 0);
        Assert.Contains(function.Descendants.OfType<LoadStackSlot>(), load => load.Slot == 0);
        function.CheckInvariant();
    }

    [Fact]
    public void StaticFieldDelegateCreation_EffectBetweenStoreAndUse_BlocksLiveRangeInlining()
    {
        var receiverField = new FieldRef(Holder, "Receiver", Object);
        var target = new MethodRef(Holder, "M", Void, [], HasThis: true);
        var sideEffect = new MethodRef(Holder, "SideEffect", Void, [], HasThis: false);
        var use = new MethodRef(Holder, "Use", Void, [Action], HasThis: false);

        var body = new BlockContainer();
        var block = new Block(0);
        block.Add(new StoreStackSlot(0, new DelegateCreation(
            Action,
            target,
            isVirtual: false,
            new LoadField(receiverField, instance: null))));
        block.Add(new ExpressionStatement(new Call(sideEffect, isVirtual: false, [])));
        block.Add(new ExpressionStatement(new Call(use, isVirtual: false, [new LoadStackSlot(0, Action)])));
        body.Add(block);
        var function = new IrFunction(
            "M",
            Holder,
            new MethodSignature(Void, [], HasThis: false, GenericParameterCount: 0),
            [],
            body);

        new ExpressionInliningPass().Run(function, PassContext.None);

        Assert.Contains(block.Children.OfType<StoreStackSlot>(), store => store.Slot == 0);
        Assert.Contains(function.Descendants.OfType<LoadStackSlot>(), load => load.Slot == 0);
        function.CheckInvariant();
    }

    [Fact]
    public void StaticFieldDelegateCreation_PriorEffectInUseStatement_BlocksLiveRangeInlining()
    {
        var receiverField = new FieldRef(Holder, "Receiver", Object);
        var target = new MethodRef(Holder, "M", Void, [], HasThis: true);
        var sideEffect = new MethodRef(Holder, "SideEffect", Int32, [], HasThis: false);
        var use = new MethodRef(Holder, "Use", Void, [Int32, Action], HasThis: false);

        var body = new BlockContainer();
        var block = new Block(0);
        block.Add(new StoreStackSlot(0, new DelegateCreation(
            Action,
            target,
            isVirtual: false,
            new LoadField(receiverField, instance: null))));
        block.Add(new ExpressionStatement(new Call(
            use,
            isVirtual: false,
            [new Call(sideEffect, isVirtual: false, []), new LoadStackSlot(0, Action)])));
        body.Add(block);
        var function = new IrFunction(
            "M",
            Holder,
            new MethodSignature(Void, [], HasThis: false, GenericParameterCount: 0),
            [],
            body);

        new ExpressionInliningPass().Run(function, PassContext.None);

        Assert.Contains(block.Children.OfType<StoreStackSlot>(), store => store.Slot == 0);
        Assert.Contains(function.Descendants.OfType<LoadStackSlot>(), load => load.Slot == 0);
        function.CheckInvariant();
    }

    [Fact]
    public void StaticFieldDelegateCreation_AdjacentUse_StillInlines()
    {
        var receiverField = new FieldRef(Holder, "Receiver", Object);
        var target = new MethodRef(Holder, "M", Void, [], HasThis: true);
        var use = new MethodRef(Holder, "Use", Void, [Action], HasThis: false);

        var body = new BlockContainer();
        var block = new Block(0);
        block.Add(new StoreStackSlot(0, new DelegateCreation(
            Action,
            target,
            isVirtual: false,
            new LoadField(receiverField, instance: null))));
        block.Add(new ExpressionStatement(new Call(use, isVirtual: false, [new LoadStackSlot(0, Action)])));
        body.Add(block);
        var function = new IrFunction(
            "M",
            Holder,
            new MethodSignature(Void, [], HasThis: false, GenericParameterCount: 0),
            [],
            body);

        new ExpressionInliningPass().Run(function, PassContext.None);

        Assert.Empty(function.Descendants.OfType<StoreStackSlot>());
        Assert.Empty(function.Descendants.OfType<LoadStackSlot>());
        Assert.Single(function.Descendants.OfType<DelegateCreation>());
        function.CheckInvariant();
    }

    // ---------------------------------------------------------------------
    // Pass-audit packet (#1288): named proof modes for ExpressionInliningPass.
    //
    // Each mode below pairs a positive (the pass must inline) with a near-miss
    // negative (the pass must decline) so the proof contract for each hazard is
    // visible as an executable fixture, not only as a pass-local helper. Modes:
    //   - evaluation-order : impure value may only move when it still evaluates
    //                        first; a pure value may move anywhere.
    //   - lock-object      : a static-field receiver copied for `lock` stays
    //                        copied (inlining can fail to bind in a shell).
    //   - typed-local      : a local whose declared type differs from its value
    //                        carries a cast/type witness and must stay.
    //   - address-escape   : an addressed local is never single-use-inlined.
    //   - live-range       : a movable read inlines into its one reached load
    //                        only when nothing in between writes what it reads
    //                        and the definition is dead after that single use.
    //   - fallthrough EH   : a store ending its block inlines into the next
    //                        block only across a pure fallthrough edge.
    // Real-importer positives for the headline live-range collapse already exist
    // (CachedDelegate* via CfgSampleClass); these synthetic fixtures pin the
    // per-hazard boundaries the importer corpus does not isolate.
    // ---------------------------------------------------------------------

    static IrFunction StraightLine(ImmutableArray<TypeRef> locals, params IrNode[] statements)
    {
        var body = new BlockContainer();
        var block = new Block(0);
        foreach (var statement in statements)
            block.Add(statement);
        body.Add(block);
        return new IrFunction(
            "M",
            Holder,
            new MethodSignature(Void, [], HasThis: false, GenericParameterCount: 0),
            locals,
            body);
    }

    static bool HasStoreLocal(IrFunction function, int index)
        => function.Descendants.OfType<StoreLocal>().Any(store => store.Index == index);

    static bool HasStoreStackSlot(IrFunction function, int slot)
        => function.Descendants.OfType<StoreStackSlot>().Any(store => store.Slot == slot);

    // evaluation-order: a pure stored value (a constant) may move past whatever
    // evaluates before its load, because reordering it is invisible.
    [Fact]
    public void EvaluationOrder_PureValueNotFirstLeaf_StillInlines()
    {
        var other = new MethodRef(Holder, "Other", Int32, [], HasThis: false);
        var use = new MethodRef(Holder, "Use", Void, [Int32, Int32], HasThis: false);
        var function = StraightLine(
            [Int32],
            new StoreLocal(0, Int32, new Constant(5, Int32)),
            new ExpressionStatement(new Call(use, isVirtual: false,
                [new Call(other, isVirtual: false, []), new LoadLocal(0, Int32)])));

        new ExpressionInliningPass().Run(function, PassContext.None);

        Assert.False(HasStoreLocal(function, 0));
        Assert.Empty(function.Descendants.OfType<LoadLocal>());
        function.CheckInvariant();
    }

    // evaluation-order near miss: an impure value whose load is not evaluated
    // first must stay, or inlining would move the call past `Other()`.
    [Fact]
    public void EvaluationOrder_ImpureValueNotFirstLeaf_StaysToPreserveOrder()
    {
        var produce = new MethodRef(Holder, "Produce", Int32, [], HasThis: false);
        var other = new MethodRef(Holder, "Other", Int32, [], HasThis: false);
        var use = new MethodRef(Holder, "Use", Void, [Int32, Int32], HasThis: false);
        var function = StraightLine(
            [Int32],
            new StoreLocal(0, Int32, new Call(produce, isVirtual: false, [])),
            new ExpressionStatement(new Call(use, isVirtual: false,
                [new Call(other, isVirtual: false, []), new LoadLocal(0, Int32)])));

        new ExpressionInliningPass().Run(function, PassContext.None);

        Assert.True(HasStoreLocal(function, 0));
        Assert.Contains(function.Descendants.OfType<LoadLocal>(), load => load.Index == 0);
    }

    // evaluation-order: the same impure value inlines when its load is the
    // first-evaluated leaf, so the stored value still evaluates first.
    [Fact]
    public void EvaluationOrder_ImpureValueFirstLeaf_Inlines()
    {
        var produce = new MethodRef(Holder, "Produce", Int32, [], HasThis: false);
        var other = new MethodRef(Holder, "Other", Int32, [], HasThis: false);
        var use = new MethodRef(Holder, "Use", Void, [Int32, Int32], HasThis: false);
        var function = StraightLine(
            [Int32],
            new StoreLocal(0, Int32, new Call(produce, isVirtual: false, [])),
            new ExpressionStatement(new Call(use, isVirtual: false,
                [new LoadLocal(0, Int32), new Call(other, isVirtual: false, [])])));

        new ExpressionInliningPass().Run(function, PassContext.None);

        Assert.False(HasStoreLocal(function, 0));
        function.CheckInvariant();
    }

    // lock-object near miss: a static-field receiver copied into a local that is
    // used as a `lock` object must stay copied.
    [Fact]
    public void LockObject_StaticFieldReceiver_StaysCopied()
    {
        var gate = new FieldRef(Holder, "Gate", Object);
        var lockBody = new BlockContainer();
        lockBody.Add(new Block(1));
        var lockNode = new ILInspector.Decompiler.Pipeline.Lock(new LoadLocal(0, Object), lockBody);
        var function = StraightLine(
            [Object],
            new StoreLocal(0, Object, new LoadField(gate, instance: null)),
            lockNode);

        new ExpressionInliningPass().Run(function, PassContext.None);

        Assert.True(HasStoreLocal(function, 0));
        Assert.IsType<LoadLocal>(lockNode.LockObject);
    }

    // lock-object: the same static-field copy inlines when it is not a lock
    // object — the guard is specific to `lock` receivers.
    [Fact]
    public void LockObject_StaticFieldCopyNotLocked_Inlines()
    {
        var gate = new FieldRef(Holder, "Gate", Object);
        var use = new MethodRef(Holder, "Use", Void, [Object], HasThis: false);
        var function = StraightLine(
            [Object],
            new StoreLocal(0, Object, new LoadField(gate, instance: null)),
            new ExpressionStatement(new Call(use, isVirtual: false, [new LoadLocal(0, Object)])));

        new ExpressionInliningPass().Run(function, PassContext.None);

        Assert.False(HasStoreLocal(function, 0));
        Assert.Single(function.Descendants.OfType<LoadField>());
        function.CheckInvariant();
    }

    // typed-local near miss: a local declared with a wider type than its value
    // carries a required cast/type witness and must stay.
    [Fact]
    public void TypedLocalWitness_CastCarryingLocal_Stays()
    {
        var use = new MethodRef(Holder, "Use", Void, [Object], HasThis: false);
        var function = StraightLine(
            [Object, Int32],
            new StoreLocal(0, Object, new LoadLocal(1, Int32)),
            new ExpressionStatement(new Call(use, isVirtual: false, [new LoadLocal(0, Object)])));

        new ExpressionInliningPass().Run(function, PassContext.None);

        Assert.True(HasStoreLocal(function, 0));
        Assert.Contains(function.Descendants.OfType<LoadLocal>(), load => load.Index == 0);
    }

    // typed-local: a local whose declared type matches its value carries no
    // witness, so the single use inlines.
    [Fact]
    public void TypedLocalWitness_MatchingType_Inlines()
    {
        var use = new MethodRef(Holder, "Use", Void, [Int32], HasThis: false);
        var function = StraightLine(
            [Int32, Int32],
            new StoreLocal(0, Int32, new LoadLocal(1, Int32)),
            new ExpressionStatement(new Call(use, isVirtual: false, [new LoadLocal(0, Int32)])));

        new ExpressionInliningPass().Run(function, PassContext.None);

        Assert.False(HasStoreLocal(function, 0));
        function.CheckInvariant();
    }

    // address-escape near miss: a local whose address is taken anywhere is never
    // single-use-inlined — an escaped pointer can observe or mutate it.
    [Fact]
    public void AddressEscape_AddressedLocal_StaysInline()
    {
        var use = new MethodRef(Holder, "Use", Void, [Int32], HasThis: false);
        var consume = new MethodRef(Holder, "Consume", Void, [TypeRef.Pointer(Int32)], HasThis: false);
        var function = StraightLine(
            [Int32],
            new StoreLocal(0, Int32, new Constant(5, Int32)),
            new ExpressionStatement(new Call(use, isVirtual: false, [new LoadLocal(0, Int32)])),
            new ExpressionStatement(new Call(consume, isVirtual: false, [new LoadLocalAddress(0, Int32)])));

        new ExpressionInliningPass().Run(function, PassContext.None);

        Assert.True(HasStoreLocal(function, 0));
    }

    // live-range: an effect-free read inlines into its one reached load even
    // across an independent interleaved statement (which defeats the simple
    // adjacency mode), because the read reorders freely.
    [Fact]
    public void LiveRange_EffectFreeReadAcrossIndependentStatement_Inlines()
    {
        var other = new MethodRef(Holder, "Other", Void, [], HasThis: false);
        var use = new MethodRef(Holder, "Use", Void, [Int32], HasThis: false);
        var function = StraightLine(
            [Int32, Int32],
            new StoreStackSlot(0, new LoadLocal(1, Int32)),
            new ExpressionStatement(new Call(other, isVirtual: false, [])),
            new ExpressionStatement(new Call(use, isVirtual: false, [new LoadStackSlot(0, Int32)])));

        new ExpressionInliningPass().Run(function, PassContext.None);

        Assert.False(HasStoreStackSlot(function, 0));
        Assert.Empty(function.Descendants.OfType<LoadStackSlot>());
        Assert.Contains(function.Descendants.OfType<LoadLocal>(), load => load.Index == 1);
        function.CheckInvariant();
    }

    // live-range near miss: a read crossing a write to the place it reads must
    // stay — moving it past the write would read the wrong value.
    [Fact]
    public void LiveRange_MovableReadCrossingWriteToReadPlace_Stays()
    {
        var use = new MethodRef(Holder, "Use", Void, [Int32], HasThis: false);
        var function = StraightLine(
            [Int32, Int32],
            new StoreStackSlot(0, new LoadLocal(1, Int32)),
            new StoreLocal(1, Int32, new Constant(0, Int32)),
            new ExpressionStatement(new Call(use, isVirtual: false, [new LoadStackSlot(0, Int32)])));

        new ExpressionInliningPass().Run(function, PassContext.None);

        Assert.True(HasStoreStackSlot(function, 0));
        Assert.Contains(function.Descendants.OfType<LoadStackSlot>(), load => load.Slot == 0);
    }

    // live-range near miss: a definition that is read twice before being
    // rewritten is not dead after a single use, so it must stay.
    [Fact]
    public void LiveRange_SecondUseOfDefinition_Stays()
    {
        var use = new MethodRef(Holder, "Use", Void, [Int32], HasThis: false);
        var function = StraightLine(
            [Int32, Int32],
            new StoreStackSlot(0, new LoadLocal(1, Int32)),
            new ExpressionStatement(new Call(use, isVirtual: false, [new LoadStackSlot(0, Int32)])),
            new ExpressionStatement(new Call(use, isVirtual: false, [new LoadStackSlot(0, Int32)])));

        new ExpressionInliningPass().Run(function, PassContext.None);

        Assert.True(HasStoreStackSlot(function, 0));
        Assert.Equal(2, function.Descendants.OfType<LoadStackSlot>().Count(load => load.Slot == 0));
    }

    // fallthrough EH: a store that ends its block inlines into the first
    // statement of the next block when fallthrough is the only incoming edge.
    [Fact]
    public void Fallthrough_PureFallthroughEdge_InlinesIntoNextBlock()
    {
        var use = new MethodRef(Holder, "Use", Void, [Int32], HasThis: false);
        var body = new BlockContainer();
        var first = new Block(0);
        first.Add(new StoreLocal(0, Int32, new Constant(7, Int32)));
        var second = new Block(1);
        second.Add(new ExpressionStatement(new Call(use, isVirtual: false, [new LoadLocal(0, Int32)])));
        body.Add(first);
        body.Add(second);
        var function = new IrFunction(
            "M",
            Holder,
            new MethodSignature(Void, [], HasThis: false, GenericParameterCount: 0),
            [Int32],
            body);

        new ExpressionInliningPass().Run(function, PassContext.None);

        Assert.False(HasStoreLocal(function, 0));
        Assert.Empty(function.Descendants.OfType<LoadLocal>());
        Assert.Single(function.Descendants.OfType<Constant>());
        function.CheckInvariant();
    }
}
