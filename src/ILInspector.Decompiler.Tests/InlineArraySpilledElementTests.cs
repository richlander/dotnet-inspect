using System.Reflection;
using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

/// <summary>
/// Synthetic coverage for <see cref="InlineArrayCollectionPass"/>'s address-spilled
/// element-store recovery (issue #3129, S4). When a collection-expression element
/// value carries a branch, csc evaluates the element-ref address before the branch
/// and spills the <c>ref</c> — and often the branch value — to single-use
/// evaluation-stack slots, then stores through the spilled address. The pass
/// recovers that shape only when every spill slot is written once and read once; a
/// multi-use spill slot or a volatile store must leave the whole collection flat.
/// The compiled-fixture canary lives in <c>CfgSampleClass.InlineArrayObjectConditionalElementSpan</c>;
/// these tests pin the guard boundaries a compiled fixture cannot express.
/// </summary>
[Trait("Area", "Pass")]
public class InlineArraySpilledElementTests
{
    static readonly TypeRef Int32 = TypeRef.CoreLib("System", "Int32");
    static readonly TypeRef Object = TypeRef.CoreLib("System", "Object");
    static readonly TypeRef Bool = TypeRef.CoreLib("System", "Boolean");
    static readonly TypeRef Void = TypeRef.CoreLib("System", "Void");
    static readonly TypeRef ByRefObject = TypeRef.ByRef(Object);

    // A compiler-synthesized inline-array buffer (the older `<>y__InlineArrayN`
    // spelling) instantiated over object — the span source csc emits for a params
    // ReadOnlySpan<object> collection expression.
    static readonly TypeRef BufferDefinition =
        TypeRef.Definition(TypeRef.CoreLibrary, "", "<>y__InlineArray2`1", ValueTypeHint.ValueType, MetadataFactState.Yes);
    static readonly TypeRef Buffer = TypeRef.GenericInstance(BufferDefinition, [Object]);
    static readonly TypeRef SpanObject = TypeRef.GenericInstance(TypeRef.CoreLib("System", "ReadOnlySpan`1"), [Object]);

    enum SpillDefect
    {
        None,
        AddressSlotReadTwice,
        ValueSlotReadTwice,
        VolatileStore,
    }

    [Fact]
    public void SpilledAddressAndValue_RaisesToCollectionExpression()
    {
        var function = BuildSpilledCollection(SpillDefect.None);

        new InlineArrayCollectionPass().Run(function, PassContext.None);

        var collection = Assert.Single(function.Descendants.OfType<CollectionExpression>());
        // Elements are re-sequenced into slot (= source) order: the direct element 0
        // then the spilled element 1, recovered to its real branch value.
        Assert.Collection(
            collection.Children,
            first => Assert.IsType<Box>(first),
            second => Assert.IsType<Conditional>(second));
        Assert.Empty(function.Descendants.OfType<StoreStackSlot>());
        Assert.Empty(function.Descendants.OfType<LoadStackSlot>());
        Assert.DoesNotContain(
            function.Descendants.OfType<Call>(),
            c => c.Callee.Name.Contains("InlineArray", StringComparison.Ordinal));
        function.CheckInvariant();
    }

    [Fact]
    public void RaisedBuffer_SlotRetainedButEliminatedFromFidelity()
    {
        var function = BuildSpilledCollection(SpillDefect.None);

        // The unspellable `<>y__InlineArray2` buffer local (slot 0) caps fidelity
        // (DEC0009) while it is still a live rendered type.
        Assert.True(CSharpSpellability.HasUnrepresentableMetadataName(function));

        new InlineArrayCollectionPass().Run(function, PassContext.None);

        // The buffer slot is retained so the span local (slot 1) keeps its index,
        // but it is marked eliminated: every reference was consumed by the raise,
        // so it renders nowhere and its unspellable type no longer contributes a
        // fidelity cause. The fully raised body is Full, not Partial (#3221).
        Assert.Equal(2, function.Locals.Length);
        Assert.Contains(0, function.EliminatedLocalSlots);
        Assert.False(CSharpSpellability.HasUnrepresentableMetadataName(function));
        Assert.Equal(DecompilationFidelity.Full, function.Fidelity);
        function.CheckInvariant();
    }

    [Fact]
    public void MarkLocalEliminated_SkipsOnlyTheMarkedSlot()
    {
        // Two locals of the same unspellable synthesized buffer type. Eliminating
        // one slot must not stop the other, still-live slot from capping fidelity —
        // the fix skips exactly the marked slot, never blanket-ignoring unspellable
        // locals.
        var body = new BlockContainer();
        body.Add(new Block());
        var signature = new MethodSignature(Void, [], HasThis: false, GenericParameterCount: 0);
        var function = new IrFunction("M", TypeRef.Definition("Synthetic", "", "T"), signature, [Buffer, Buffer], body);

        Assert.True(CSharpSpellability.HasUnrepresentableMetadataName(function));

        function.MarkLocalEliminated(0);
        Assert.True(CSharpSpellability.HasUnrepresentableMetadataName(function));

        function.MarkLocalEliminated(1);
        Assert.False(CSharpSpellability.HasUnrepresentableMetadataName(function));
    }

    [Fact]
    public void ResetLocals_CarriesProvidedEliminatedSlots_ElseClears()
    {
        var body = new BlockContainer();
        body.Add(new Block());
        var signature = new MethodSignature(Void, [], HasThis: false, GenericParameterCount: 0);
        var function = new IrFunction("M", TypeRef.Definition("Synthetic", "", "T"), signature, [Buffer], body);
        function.MarkLocalEliminated(0);

        // A transplant that carries the reconstructed body's own eliminated slots
        // (e.g. an iterator MoveNext's dead inline-array buffer, whose slot indices
        // are the transplanted numbering) preserves them across the reset.
        function.ResetLocals([Object, Buffer], [null, null], new HashSet<int> { 1 });
        Assert.Equal(new[] { 1 }, function.EliminatedLocalSlots.Order());

        // A reset with no carried set drops the marking — the new numbering no
        // longer names the same locals.
        function.ResetLocals([Object], [null]);
        Assert.Empty(function.EliminatedLocalSlots);
    }

    [Fact]
    public void AddressSpillReadTwice_StaysFlat()
    {
        // The spilled element-ref address must be read exactly once — the address
        // computation has no observable effect only when the collection expression
        // is the sole consumer. A second read means the ref escapes; leave it flat.
        var function = BuildSpilledCollection(SpillDefect.AddressSlotReadTwice);

        new InlineArrayCollectionPass().Run(function, PassContext.None);

        Assert.Empty(function.Descendants.OfType<CollectionExpression>());
        Assert.Contains(function.Descendants.OfType<Call>(), c => c.Callee.Name == "InlineArrayAsReadOnlySpan");
        function.CheckInvariant();
    }

    [Fact]
    public void ValueSpillReadTwice_StaysFlat()
    {
        // A spilled element value read more than once cannot be lifted into the
        // element position without duplicating (and re-evaluating) it; leave flat.
        var function = BuildSpilledCollection(SpillDefect.ValueSlotReadTwice);

        new InlineArrayCollectionPass().Run(function, PassContext.None);

        Assert.Empty(function.Descendants.OfType<CollectionExpression>());
        Assert.Contains(function.Descendants.OfType<Call>(), c => c.Callee.Name == "InlineArrayAsReadOnlySpan");
        function.CheckInvariant();
    }

    [Fact]
    public void VolatileSpilledStore_StaysFlat()
    {
        // A volatile store through the spilled address is an observable memory
        // operation the collection expression would silently drop; leave flat.
        var function = BuildSpilledCollection(SpillDefect.VolatileStore);

        new InlineArrayCollectionPass().Run(function, PassContext.None);

        Assert.Empty(function.Descendants.OfType<CollectionExpression>());
        Assert.Contains(function.Descendants.OfType<Call>(), c => c.Callee.Name == "InlineArrayAsReadOnlySpan");
        function.CheckInvariant();
    }

    [Fact]
    public void InterleavedStatementBetweenStores_StaysFlat()
    {
        // An unrelated side-effecting statement sits between the two element
        // stores. The raise lifts the elements to the AsSpan site, which would move
        // their side effects past that statement and silently re-sequence the
        // program; the shape must stay flat.
        var function = BuildOrderingCollection(OrderingShape.InterleavedStatement);

        new InlineArrayCollectionPass().Run(function, PassContext.None);

        Assert.Empty(function.Descendants.OfType<CollectionExpression>());
        Assert.Contains(function.Descendants.OfType<Call>(), c => c.Callee.Name == "InlineArrayAsReadOnlySpan");
        function.CheckInvariant();
    }

    [Fact]
    public void DescendingSlotOrderStores_StaysFlat()
    {
        // The element stores are emitted in descending slot order. The raise
        // re-sequences them into ascending (source) order, inverting the evaluation
        // order of the two element values; the shape must stay flat.
        var function = BuildOrderingCollection(OrderingShape.DescendingSlotOrder);

        new InlineArrayCollectionPass().Run(function, PassContext.None);

        Assert.Empty(function.Descendants.OfType<CollectionExpression>());
        Assert.Contains(function.Descendants.OfType<Call>(), c => c.Callee.Name == "InlineArrayAsReadOnlySpan");
        function.CheckInvariant();
    }

    [Fact]
    public void InlineEffectBeforeSpanInConsumer_StaysFlat()
    {
        // The consumer evaluates a side-effecting call inline before the span
        // (Consume(PrefixEffect(), span)). Block order is canonical, but lifting the
        // elements to the span's position would move them past PrefixEffect(),
        // inverting their order; the shape must stay flat.
        var function = BuildOrderingCollection(OrderingShape.InlineEffectBeforeSpan);

        new InlineArrayCollectionPass().Run(function, PassContext.None);

        Assert.Empty(function.Descendants.OfType<CollectionExpression>());
        Assert.Contains(function.Descendants.OfType<Call>(), c => c.Callee.Name == "InlineArrayAsReadOnlySpan");
        function.CheckInvariant();
    }

    [Fact]
    public void SpilledPrefixBeforeSpanInConsumer_RaisesToCollectionExpression()
    {
        // The canonical csc shape: a prefix effect evaluated before the collection is
        // spilled to a stack slot ahead of the fill, so the consumer loads that slot
        // (Consume(spilledPrefix, span)) and only a side-effect-free load precedes the
        // span. The within-consumer guard must not over-tighten this — it still raises.
        var function = BuildOrderingCollection(OrderingShape.SpilledPrefixBeforeSpan);

        new InlineArrayCollectionPass().Run(function, PassContext.None);

        var collection = Assert.Single(function.Descendants.OfType<CollectionExpression>());
        Assert.Equal(2, collection.Children.Count);
        Assert.DoesNotContain(
            function.Descendants.OfType<Call>(),
            c => c.Callee.Name.Contains("InlineArray", StringComparison.Ordinal));
        function.CheckInvariant();
    }

    [Fact]
    public void ConditionalSpanArmInConsumer_StaysFlat()
    {
        // The span sits in a ternary arm (Consume(cond ? span : other)), so it is only
        // conditionally evaluated. The element stores are unconditional; lifting them
        // into that arm would suppress their side effects whenever the false arm is
        // taken. Block order is canonical, so only the conditional guard rejects it —
        // the shape must stay flat.
        var function = BuildOrderingCollection(OrderingShape.ConditionalSpanArm);

        new InlineArrayCollectionPass().Run(function, PassContext.None);

        Assert.Empty(function.Descendants.OfType<CollectionExpression>());
        Assert.Contains(function.Descendants.OfType<Call>(), c => c.Callee.Name == "InlineArrayAsReadOnlySpan");
        function.CheckInvariant();
    }

    [Fact]
    public void NullCoalescingAssignmentValueSpan_StaysFlat()
    {
        // The span is the right operand of `local ??= span`, evaluated only when the
        // target is null. `NullCoalescingAssignment` is one of the conditional IR
        // nodes an enumerated deny list missed; the sound-by-default allow list leaves
        // it flat because the node is not a recognized unconditional container.
        var function = BuildOrderingCollection(OrderingShape.NullCoalescingAssignmentValue);

        new InlineArrayCollectionPass().Run(function, PassContext.None);

        Assert.Empty(function.Descendants.OfType<CollectionExpression>());
        Assert.Contains(function.Descendants.OfType<Call>(), c => c.Callee.Name == "InlineArrayAsReadOnlySpan");
        function.CheckInvariant();
    }

    [Fact]
    public void UnionSwitchArmValueSpan_StaysFlat()
    {
        // The span is a union-switch-expression arm value, evaluated only when its arm
        // matches. `UnionSwitchExpressionArm` does not derive from `SwitchExpressionArm`,
        // so a deny list keyed on the base arm type missed it; the allow list rejects any
        // parent it does not explicitly recognize as unconditional, so it stays flat.
        var function = BuildOrderingCollection(OrderingShape.UnionSwitchArmValue);

        new InlineArrayCollectionPass().Run(function, PassContext.None);

        Assert.Empty(function.Descendants.OfType<CollectionExpression>());
        Assert.Contains(function.Descendants.OfType<Call>(), c => c.Callee.Name == "InlineArrayAsReadOnlySpan");
        function.CheckInvariant();
    }

    [Fact]
    public void WhileConditionSpan_StaysFlat()
    {
        // The span is a while-loop condition: evaluated unconditionally but once per
        // iteration. Lifting the element stores into it would call each element
        // function once per loop test instead of once. The allow list rejects the
        // WhileLoop condition edge (not a once-through container), so it stays flat —
        // covering the exactly-once axis, not just definite evaluation.
        var function = BuildOrderingCollection(OrderingShape.WhileConditionSpan);

        new InlineArrayCollectionPass().Run(function, PassContext.None);

        Assert.Empty(function.Descendants.OfType<CollectionExpression>());
        Assert.Contains(function.Descendants.OfType<Call>(), c => c.Callee.Name == "InlineArrayAsReadOnlySpan");
        function.CheckInvariant();
    }

    [Fact]
    public void BinaryOperandSpanConsumer_RaisesToCollectionExpression()
    {
        // The span-consuming call is the left operand of `IntFromSpan(span) + 1` — a
        // non-short-circuiting Binary that evaluates it exactly once, unconditionally.
        // The allow list recognizes Binary, so the fidelity a Call-only allow list would
        // lose is recovered: the collection still raises.
        var function = BuildOrderingCollection(OrderingShape.BinaryOperandSpan);

        new InlineArrayCollectionPass().Run(function, PassContext.None);

        var collection = Assert.Single(function.Descendants.OfType<CollectionExpression>());
        Assert.Equal(2, collection.Children.Count);
        Assert.DoesNotContain(
            function.Descendants.OfType<Call>(),
            c => c.Callee.Name.Contains("InlineArray", StringComparison.Ordinal));
        function.CheckInvariant();
    }

    [Fact]
    public void IfConditionSpanConsumer_RaisesToCollectionExpression()
    {
        // The span sits in a forward `if (Predicate(span))` condition — evaluated
        // exactly once when the statement is reached (this pass runs after loop
        // structuring, so an if-statement is never a back edge). The per-edge allow list
        // accepts the condition edge, so the collection raises.
        var function = BuildOrderingCollection(OrderingShape.IfConditionSpan);

        new InlineArrayCollectionPass().Run(function, PassContext.None);

        var collection = Assert.Single(function.Descendants.OfType<CollectionExpression>());
        Assert.Equal(2, collection.Children.Count);
        Assert.DoesNotContain(
            function.Descendants.OfType<Call>(),
            c => c.Callee.Name.Contains("InlineArray", StringComparison.Ordinal));
        function.CheckInvariant();
    }

    [Fact]
    public void SwitchValueSpanConsumer_RaisesToCollectionExpression()
    {
        // The span sits in a forward `switch (IntFromSpan(span))` scrutinee —
        // evaluated exactly once. The per-edge allow list accepts the Switch value edge
        // (the sections are the conditional parts), so the collection raises.
        var function = BuildOrderingCollection(OrderingShape.SwitchValueSpan);

        new InlineArrayCollectionPass().Run(function, PassContext.None);

        var collection = Assert.Single(function.Descendants.OfType<CollectionExpression>());
        Assert.Equal(2, collection.Children.Count);
        Assert.DoesNotContain(
            function.Descendants.OfType<Call>(),
            c => c.Callee.Name.Contains("InlineArray", StringComparison.Ordinal));
        function.CheckInvariant();
    }

    [Fact]
    public void ThrowValueSpanConsumer_RaisesToCollectionExpression()
    {
        // The span-consuming call is the `throw MakeException(span)` value — evaluated
        // exactly once, unconditionally. The allow list recognizes Throw, so the
        // collection raises (matching the vetted peer StackAllocSpanPass consumer set).
        var function = BuildOrderingCollection(OrderingShape.ThrowValueSpan);

        new InlineArrayCollectionPass().Run(function, PassContext.None);

        var collection = Assert.Single(function.Descendants.OfType<CollectionExpression>());
        Assert.Equal(2, collection.Children.Count);
        Assert.DoesNotContain(
            function.Descendants.OfType<Call>(),
            c => c.Callee.Name.Contains("InlineArray", StringComparison.Ordinal));
        function.CheckInvariant();
    }

    [Theory]
    [InlineData(OrderingShape.ConditionalConditionSpan)]
    [InlineData(OrderingShape.SwitchExpressionValueSpan)]
    [InlineData(OrderingShape.UnionSwitchValueSpan)]
    [InlineData(OrderingShape.PatternSwitchValueSpan)]
    [InlineData(OrderingShape.TupleSwitchComponentSpan)]
    [InlineData(OrderingShape.ForeachCollectionSpan)]
    [InlineData(OrderingShape.ForInitializerSpan)]
    [InlineData(OrderingShape.UsingResourceSpan)]
    [InlineData(OrderingShape.LockObjectSpan)]
    public void RunOnceGoverningEdgeSpanConsumer_RaisesToCollectionExpression(OrderingShape shape)
    {
        // Each shape places the span on a run-once, unconditional governing edge — a
        // selection scrutinee/condition (ternary condition, switch-expression
        // value/component) or a statement's single on-entry sub-expression (foreach
        // source, for initializer, using resource, lock object). Each is evaluated
        // exactly once before any conditional arm/body runs, so lifting the
        // unconditional element stores to the span position preserves their order and
        // occurrence count: the collection raises (#3129 S4 follow-up #3281).
        var function = BuildOrderingCollection(shape);

        new InlineArrayCollectionPass().Run(function, PassContext.None);

        var collection = Assert.Single(function.Descendants.OfType<CollectionExpression>());
        Assert.Equal(2, collection.Children.Count);
        Assert.DoesNotContain(
            function.Descendants.OfType<Call>(),
            c => c.Callee.Name.Contains("InlineArray", StringComparison.Ordinal));
        function.CheckInvariant();
    }

    [Fact]
    public void ForConditionSpan_StaysFlat()
    {
        // The close negative of ForInitializerSpan: the span sits in the for-loop
        // condition, re-evaluated every iteration rather than once on entry. The
        // per-edge allow list accepts only ForLoop.Initializer, so the condition edge
        // is rejected and the collection stays flat — proving the ForLoop case is
        // edge-precise, not a blanket "ForLoop is unconditional".
        var function = BuildOrderingCollection(OrderingShape.ForConditionSpan);

        new InlineArrayCollectionPass().Run(function, PassContext.None);

        Assert.Empty(function.Descendants.OfType<CollectionExpression>());
        Assert.Contains(function.Descendants.OfType<Call>(), c => c.Callee.Name == "InlineArrayAsReadOnlySpan");
        function.CheckInvariant();
    }

    [Fact]
    public void ByValueBufferStoreAfterSpan_LeavesSlotCountedAtPartial()
    {
        // A by-value store reuses the dead buffer slot (0) after the span consumer.
        // The pass's reference tally (LoadLocal bail + LoadLocalAddress count) never
        // inspects a by-value StoreLocal, so the raise fires — soundly, since the
        // store re-sequences no element effect.
        var function = BuildOrderingCollection(OrderingShape.ByValueBufferStoreAfterSpan);

        new InlineArrayCollectionPass().Run(function, PassContext.None);

        // Raise stays sound: the collection expression is still recovered.
        Assert.Single(function.Descendants.OfType<CollectionExpression>());

        // But MarkLocalEliminated verifies deadness before marking: a StoreLocal still
        // names slot 0, so the slot is NOT eliminated. Its unspellable buffer type
        // keeps capping fidelity at Partial — an honest number rather than a false
        // Full over output that still writes the slot (#3295). Without the liveness
        // check the slot would be marked and the method would wrongly report Full.
        Assert.DoesNotContain(0, function.EliminatedLocalSlots);
        Assert.True(CSharpSpellability.HasUnrepresentableMetadataName(function));
        Assert.Equal(DecompilationFidelity.Partial, function.Fidelity);
        function.CheckInvariant();
    }

    [Fact]
    public void MarkLocalEliminated_RefusesWhenSlotBoundByNonLoadStoreNode()
    {
        // The buffer slot (0) is bound by a node that is neither a load, a store, nor
        // an address — here a `??=` whose target is slot 0. A reference tally keyed on
        // the load/store/address trio misses it, but the printer still declares the
        // slot (CSharpPrinter.CollectDeclarations enumerates NullCoalescingAssignment),
        // so eliminating it would drop the unspellable buffer type from fidelity and
        // report a false Full. Verification must cover every local-binding node kind
        // the printer declares, not just the trio (#3295).
        var block = new Block();
        block.Add(new NullCoalescingAssignment(0, Object, BoxInt(7)));
        var body = new BlockContainer();
        body.Add(block);
        var signature = new MethodSignature(Void, [], HasThis: false, GenericParameterCount: 0);
        var function = new IrFunction("M", TypeRef.Definition("Synthetic", "", "T"), signature, [Buffer], body);

        function.MarkLocalEliminated(0);

        // Slot 0 is still live, so it is not eliminated and its unspellable buffer type
        // keeps capping fidelity. The old trio-only check marked it, a false Full.
        Assert.DoesNotContain(0, function.EliminatedLocalSlots);
        Assert.True(CSharpSpellability.HasUnrepresentableMetadataName(function));
    }

    [Fact]
    public void MarkLocalEliminated_IgnoresReferencesInNestedFunctionScopes()
    {
        // A nested local function carries an independent local pool, so its slot 0 is
        // an unrelated variable. The outer buffer slot 0 is genuinely dead (nothing in
        // the outer scope reads it), so eliminating it is correct. Verification must
        // not let the nested-scope store block the elimination, or every clean outer
        // raise that happens to share a slot index with a nested lambda / local
        // function would regress to Partial (#3295).
        var nestedBlock = new Block();
        nestedBlock.Add(new StoreLocal(0, Object, BoxInt(7)));
        nestedBlock.Add(new Return(null));
        var nestedBody = new BlockContainer();
        nestedBody.Add(nestedBlock);
        var nested = new LocalFunctionStatement(
            "Local", Void, [], isStatic: true, [Object], [null],
            usesUpdatedMemorySafetyRules: false, skipLocalsInit: false, nestedBody);

        var block = new Block();
        block.Add(nested);
        var body = new BlockContainer();
        body.Add(block);
        var signature = new MethodSignature(Void, [], HasThis: false, GenericParameterCount: 0);
        var function = new IrFunction("M", TypeRef.Definition("Synthetic", "", "T"), signature, [Buffer], body);

        function.MarkLocalEliminated(0);

        // The nested slot 0 lives in a separate pool, so it does not keep the outer
        // slot 0 alive. The old whole-tree walk saw it and refused to mark, a false
        // Partial on ordinary code.
        Assert.Contains(0, function.EliminatedLocalSlots);
    }

    [Fact]
    public void MarkLocalEliminated_RefusesWhenSlotBoundBySwitchExpressionArmPattern()
    {
        // A switch-expression arm binds the buffer slot (0) as its pattern variable.
        // The printer declares such pattern locals (CSharpPrinter collects
        // UnionSwitchExpressionArm / PatternSwitchExpressionArm into _isPatternLocals and
        // renders them inline), so eliminating slot 0 would drop the unspellable buffer
        // type from fidelity and report a false Full. The load/store/`??=`/foreach set is
        // not enough — switch-arm pattern bindings must count too (#3295).
        var block = new Block();
        block.Add(new UnionSwitchExpressionArm(Object, localIndex: 0, BoxInt(1)));
        var body = new BlockContainer();
        body.Add(block);
        var signature = new MethodSignature(Void, [], HasThis: false, GenericParameterCount: 0);
        var function = new IrFunction("M", TypeRef.Definition("Synthetic", "", "T"), signature, [Buffer], body);

        function.MarkLocalEliminated(0);

        Assert.DoesNotContain(0, function.EliminatedLocalSlots);
        Assert.True(CSharpSpellability.HasUnrepresentableMetadataName(function));
    }

    [Fact]
    public void MarkLocalEliminated_RefusesWhenSlotReferencedInSharedNestedFunctionScope()
    {
        // A nested local function that owns no locals and touches no stack slot shares
        // the enclosing local pool (the printer renders its body inline through the outer
        // scope), so its `V_0` IS the outer buffer slot. Unconditionally skipping every
        // nested function would miss this genuine reference and eliminate a still-rendered
        // slot, a false Full. Only nested functions with their OWN pool may be skipped —
        // contrast MarkLocalEliminated_IgnoresReferencesInNestedFunctionScopes (#3295).
        var nestedBlock = new Block();
        nestedBlock.Add(new StoreLocal(0, Object, BoxInt(7)));
        nestedBlock.Add(new Return(null));
        var nestedBody = new BlockContainer();
        nestedBody.Add(nestedBlock);
        var nested = new LocalFunctionStatement(
            "Local", Void, [], isStatic: true, [], [],
            usesUpdatedMemorySafetyRules: false, skipLocalsInit: false, nestedBody);

        var block = new Block();
        block.Add(nested);
        var body = new BlockContainer();
        body.Add(block);
        var signature = new MethodSignature(Void, [], HasThis: false, GenericParameterCount: 0);
        var function = new IrFunction("M", TypeRef.Definition("Synthetic", "", "T"), signature, [Buffer], body);

        function.MarkLocalEliminated(0);

        Assert.DoesNotContain(0, function.EliminatedLocalSlots);
        Assert.True(CSharpSpellability.HasUnrepresentableMetadataName(function));
    }

    [Fact]
    public void MarkLocalEliminated_RefusesWhenSlotReferencedInSharedNestedLambda()
    {
        // A locals-free lambda shares the enclosing local pool, so its `V_0` is the outer
        // buffer slot: `return () => { V_0 = box(7); };`. Skipping every lambda would mark
        // the slot eliminated and report Full while the lambda body still names it. The
        // walk must descend into shared-scope lambdas, only skipping ones with their own
        // pool (#3295).
        var lambdaBlock = new Block();
        lambdaBlock.Add(new StoreLocal(0, Object, BoxInt(7)));
        var lambdaBody = new BlockContainer();
        lambdaBody.Add(lambdaBlock);
        var lambda = new Lambda(
            Object, [], [], [],
            usesUpdatedMemorySafetyRules: false, skipLocalsInit: false, lambdaBody);

        var block = new Block();
        block.Add(new Return(lambda));
        var body = new BlockContainer();
        body.Add(block);
        var signature = new MethodSignature(Void, [], HasThis: false, GenericParameterCount: 0);
        var function = new IrFunction("M", TypeRef.Definition("Synthetic", "", "T"), signature, [Buffer], body);

        function.MarkLocalEliminated(0);

        Assert.DoesNotContain(0, function.EliminatedLocalSlots);
        Assert.True(CSharpSpellability.HasUnrepresentableMetadataName(function));
    }

    // Property getter used by the pattern carriers (RecursivePropertyDeclarationPattern
    // and PropertySubpattern) — its name must start with `get_` because both derive
    // PropertyName by trimming that prefix.
    static readonly MethodRef PatternAccessor = new(Object, "get_Value", Object, [], HasThis: true);

    // One IrFunction factory per local-slot-binding carrier kind, parameterized by the
    // slot the carrier binds. Each builds a method whose slot 0 is the unspellable
    // inline-array buffer; when the carrier binds slot 0, MarkLocalEliminated must refuse
    // to drop it, because dropping it would hide the buffer's unspellable type and report
    // a false Full (#3295). The slot parameter is what lets the Theory prove the carrier
    // is the *sole* slot-0 binder rather than assert it by construction (#3329): the same
    // factory pointed at slot 1 must leave slot 0 eliminable. Aggregation-only carriers
    // are bound through the parent that owns their index, matching how NodeBindsLocalSlot
    // reaches them: DeconstructionTarget through DeconstructionAssignment,
    // PropertySubpattern through PatternSwitchExpressionArm.Subpattern.
    static readonly IReadOnlyDictionary<Type, Func<int, IrFunction>> LocalSlotCarrierFactories =
        new Dictionary<Type, Func<int, IrFunction>>
        {
            [typeof(NullCoalescingAssignment)] = slot => BufferBoundBy(new NullCoalescingAssignment(slot, Object, BoxInt(7))),
            [typeof(ForeachStatement)] = slot => BufferBoundBy(new ForeachStatement(slot, Object, BoxInt(0), new Block())),
            [typeof(UsingStatement)] = slot => BufferBoundBy(new UsingStatement(slot, Object, BoxInt(0), new BlockContainer())),
            [typeof(Fixed)] = slot => BufferBoundBy(new Fixed(Object, slot, BoxInt(0), new BlockContainer())),
            [typeof(IsPattern)] = slot => BufferBoundBy(new IsPattern(BoxInt(0), Object, slot)),
            [typeof(RecursivePropertyDeclarationPattern)] = slot => BufferBoundBy(new RecursivePropertyDeclarationPattern(BoxInt(0), PatternAccessor, Object, slot)),
            [typeof(UnionSwitchExpressionArm)] = slot => BufferBoundBy(new UnionSwitchExpressionArm(Object, localIndex: slot, BoxInt(1))),
            [typeof(PatternSwitchExpressionArm)] = slot => BufferBoundBy(new PatternSwitchExpressionArm(Object, localIndex: slot, subpattern: null, BoxInt(1))),
            [typeof(PropertySubpattern)] = slot => BufferBoundBy(new PatternSwitchExpressionArm(Object, localIndex: null, new PropertySubpattern(PatternAccessor, Object, slot), BoxInt(1))),
            [typeof(CatchClause)] = slot => BufferBoundBy(new CatchClause(Object, new BlockContainer()) { VariableIndex = slot }),
            [typeof(DeconstructionAssignment)] = slot => BufferBoundBy(new DeconstructionAssignment([slot], [Object], BoxInt(0), [true])),
            [typeof(DeconstructionTarget)] = slot => BufferBoundBy(new DeconstructionAssignment([slot], [Object], BoxInt(0), [true])),
        };

    // Two locals so a carrier can be pointed at slot 1 without leaving the local table.
    // Slot 0 is the unspellable buffer and slot 1 is spellable, so eliminating slot 0
    // is observable through HasUnrepresentableMetadataName: if the refusal leg ever
    // wrongly dropped slot 0, only the spellable Object would remain and the
    // spellability assertion would fail rather than stay incidentally true.
    static IrFunction BufferBoundBy(IrNode carrier)
    {
        var block = new Block();
        block.Add(carrier);
        var body = new BlockContainer();
        body.Add(block);
        var signature = new MethodSignature(Void, [], HasThis: false, GenericParameterCount: 0);
        return new IrFunction("M", TypeRef.Definition("Synthetic", "", "T"), signature, [Buffer, Object], body);
    }

    public static IEnumerable<object[]> LocalSlotCarrierKinds =>
        LocalSlotCarrierFactories.Keys.Select(type => new object[] { type.Name });

    [Theory]
    [MemberData(nameof(LocalSlotCarrierKinds))]
    public void MarkLocalEliminated_RefusesEveryLocalSlotBindingNodeKind(string carrierKind)
    {
        // Behavioral guard for IrFunction.NodeBindsLocalSlot (#3295). For every IR node
        // kind that can bind a local slot, the factory above builds a method whose slot 0
        // is the unspellable buffer, bound by that kind. MarkLocalEliminated must leave the
        // slot in place so its type keeps capping fidelity. Deleting a NodeBindsLocalSlot
        // case makes its kind's row fail here — unlike an inventory check, this actually
        // runs the switch.
        var factory = LocalSlotCarrierFactories.Single(entry => entry.Key.Name == carrierKind).Value;

        // Guard against a vacuous factory that satisfies the completeness tripwire by
        // reusing another kind's factory (binding slot 0 through some OTHER carrier): the
        // built function must actually contain a node of the keyed kind.
        var bound = factory(0);
        Assert.True(FunctionContainsCarrier(bound, carrierKind),
            $"The factory for {carrierKind} built no node of that kind, so its row proves nothing.");

        bound.MarkLocalEliminated(0);

        Assert.DoesNotContain(0, bound.EliminatedLocalSlots);
        Assert.True(CSharpSpellability.HasUnrepresentableMetadataName(bound));

        // What makes the refusal above attributable to THIS carrier (#3329). Containing a
        // node of the kind does not establish that the kind is what held slot 0: a factory
        // that built its carrier alongside any second slot-0 binder — a bare StoreLocal(0)
        // is enough — would satisfy both assertions above while NodeBindsLocalSlot never
        // handled the kind at all, and a function whose only slot-0 binder was that kind
        // would then be marked eliminated, which is the false Full this exists to prevent.
        // Pointing the same factory at slot 1 leaves slot 0 bound by nothing, so it must be
        // eliminable. Any stray slot-0 binder makes this leg refuse and fails the row,
        // which turns "sole slot-0 binder" from a property of how the factories happen to
        // be written into one the Theory asserts.
        var unbound = factory(1);
        Assert.True(FunctionContainsCarrier(unbound, carrierKind),
            $"The slot-1 factory for {carrierKind} built no node of that kind, so its row proves nothing.");

        unbound.MarkLocalEliminated(0);

        Assert.Contains(0, unbound.EliminatedLocalSlots);
    }

    [Fact]
    public void MarkLocalEliminated_HasABehavioralCaseForEveryLocalSlotCarrierKind()
    {
        // Drift tripwire: every carrier that can bind a local slot in the IR tree must
        // have a behavioral factory above so the refusal Theory exercises NodeBindsLocalSlot
        // for it. A new carrier grows the discovered set and fails here until a factory is
        // added; because the Theory asserts the factory actually builds a node of that kind
        // and — by running the same factory against slot 1 — that the kind is the sole
        // slot-0 binder, adding a factory then forces the carrier to be handled in
        // NodeBindsLocalSlot (neither a name in a list, a reused factory, nor a factory
        // that smuggles in a second slot-0 binder can silence both tests).
        // Load/Store/LoadLocalAddress use `Index` (shared with arguments) and are handled
        // separately.
        //
        // Discovery is two-pronged so a future non-IrNode embedded carrier record — as
        // PropertySubpattern already is (reached via PatternSwitchExpressionArm.Subpattern) —
        // is fail-safe rather than silently ignored: (1) IrNode subclasses that carry an
        // index member, and (2) non-IrNode types that carry an index member and are embedded
        // as a property of some IrNode. A non-IrNode record that no IrNode references cannot
        // bind a slot in the tree, so it is correctly out of scope. It stays name-based, so a
        // carrier that spells its index outside the LocalIndex / LocalIndices / VariableIndex
        // convention would slip through — keep to it.
        var carrierNames = new[] { "LocalIndex", "LocalIndices", "VariableIndex" };
        var irNodeTypes = typeof(IrFunction).Assembly.GetTypes()
            .Where(type => typeof(IrNode).IsAssignableFrom(type))
            .ToArray();
        var directCarriers = irNodeTypes
            .Where(type => HasCarrierMember(type, carrierNames));
        var embeddedCarriers = irNodeTypes
            .SelectMany(EmbeddedMemberTypes)
            .SelectMany(EmbeddedCandidateTypes)
            .Where(type => !typeof(IrNode).IsAssignableFrom(type))
            .Where(type => HasCarrierMember(type, carrierNames));
        var discovered = directCarriers.Concat(embeddedCarriers)
            .Select(type => type.Name)
            .Distinct()
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        var covered = LocalSlotCarrierFactories.Keys
            .Select(type => type.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(discovered, covered);
    }

    static bool HasCarrierMember(Type type, string[] carrierNames)
    {
        const BindingFlags flags = BindingFlags.Public | BindingFlags.Instance;
        return type.GetProperties(flags).Any(member => carrierNames.Contains(member.Name))
            || type.GetFields(flags).Any(member => carrierNames.Contains(member.Name));
    }

    // The declared member types of an IrNode (public property and field types), so an
    // embedded carrier is discovered whether it is exposed as a property (the common IR
    // record shape) or a field.
    static IEnumerable<Type> EmbeddedMemberTypes(Type type)
    {
        const BindingFlags flags = BindingFlags.Public | BindingFlags.Instance;
        foreach (var property in type.GetProperties(flags))
        {
            if (property.GetIndexParameters().Length == 0)
                yield return property.PropertyType;
        }
        foreach (var field in type.GetFields(flags))
            yield return field.FieldType;
    }

    // A member's own type plus, recursively, the types it structurally wraps — generic
    // arguments (ImmutableArray<Subpattern>, List<List<Subpattern>>) and element types
    // (Subpattern[]) — so an embedded carrier is considered no matter how deeply a
    // collection/array shape nests it, not just a bare or single-level-generic member type.
    static IEnumerable<Type> EmbeddedCandidateTypes(Type type)
    {
        yield return type;
        if (type.IsGenericType)
            foreach (var argument in type.GetGenericArguments())
                foreach (var inner in EmbeddedCandidateTypes(argument))
                    yield return inner;
        if (type.HasElementType && type.GetElementType() is { } elementType)
            foreach (var inner in EmbeddedCandidateTypes(elementType))
                yield return inner;
    }

    // Whether the function's IR tree contains a carrier of the named kind. IrNode carriers
    // (including aggregation children such as DeconstructionTarget) appear as nodes;
    // non-IrNode carrier records (PropertySubpattern, reached via PatternSwitchExpressionArm.
    // Subpattern) are embedded as properties rather than Children, so property values are
    // matched by runtime type name too.
    //
    // The catch is narrowed to what reflection wraps a property body's own exception in
    // (#3329): several IR properties cast or index Children and would throw on a synthetic
    // node, and skipping those is intended. A blanket catch also swallowed bugs in this
    // helper itself — an NRE or bad cast in the loop would read as "carrier absent" — and
    // measured across every row today, no property read throws at all, so nothing depends
    // on the broader form. Either way this helper is fail-closed: its only callers assert
    // the result is true, so a carrier missed behind a throwing property fails its row
    // loudly rather than letting it pass.
    static bool FunctionContainsCarrier(IrNode node, string carrierKind)
    {
        if (node.GetType().Name == carrierKind)
            return true;
        foreach (var property in node.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (property.GetIndexParameters().Length != 0)
                continue;
            object? value;
            try { value = property.GetValue(node); }
            catch (TargetInvocationException) { continue; }
            if (value is not null && value is not IrNode && value.GetType().Name == carrierKind)
                return true;
        }
        return node.Children.Any(child => FunctionContainsCarrier(child, carrierKind));
    }

    public enum OrderingShape
    {
        InterleavedStatement,
        DescendingSlotOrder,
        InlineEffectBeforeSpan,
        SpilledPrefixBeforeSpan,
        ConditionalSpanArm,
        NullCoalescingAssignmentValue,
        UnionSwitchArmValue,
        WhileConditionSpan,
        BinaryOperandSpan,
        IfConditionSpan,
        SwitchValueSpan,
        ThrowValueSpan,
        ConditionalConditionSpan,
        SwitchExpressionValueSpan,
        UnionSwitchValueSpan,
        PatternSwitchValueSpan,
        TupleSwitchComponentSpan,
        ForeachCollectionSpan,
        ForInitializerSpan,
        UsingResourceSpan,
        LockObjectSpan,
        ForConditionSpan,
        ByValueBufferStoreAfterSpan,
    }

    /// <summary>
    /// Builds a two-element params ReadOnlySpan&lt;object&gt; collection with direct
    /// element stores, perturbed to prove the statement-ordering guard.
    /// <see cref="OrderingShape.InterleavedStatement"/> drops an unrelated
    /// side-effecting call between the two stores;
    /// <see cref="OrderingShape.DescendingSlotOrder"/> emits the stores in
    /// descending slot order;
    /// <see cref="OrderingShape.InlineEffectBeforeSpan"/> evaluates an effectful
    /// call inline before the span inside the consumer;
    /// <see cref="OrderingShape.SpilledPrefixBeforeSpan"/> spills that prefix effect
    /// to a stack slot ahead of the fill (exactly what csc emits) so only a
    /// side-effect-free load precedes the span. The first three are shapes csc never
    /// emits but arbitrary IL can, and lifting any would re-sequence element side
    /// effects — so all must leave the collection flat; the last is the canonical
    /// csc shape and must still raise.
    /// </summary>
    static IrFunction BuildOrderingCollection(OrderingShape shape)
    {
        var block = new Block();

        // csc spills a prefix effect evaluated before the collection to a stack slot
        // ahead of the fill; the consumer then loads that slot (side-effect free).
        if (shape == OrderingShape.SpilledPrefixBeforeSpan)
            block.Add(new StoreStackSlot(0, PrefixEffect()));

        // <>y__InlineArray2<object> buffer = default; (local 0)
        block.Add(new InitObject(Buffer, new LoadLocalAddress(0, Buffer)));

        if (shape == OrderingShape.DescendingSlotOrder)
        {
            // Store slot 1 before slot 0.
            block.Add(new StoreIndirect(Object, ElementRef(1), BoxInt(2)));
            block.Add(new StoreIndirect(Object, ElementRef(0), BoxInt(1)));
        }
        else if (shape == OrderingShape.InterleavedStatement)
        {
            // Store slot 0, an unrelated side-effecting statement, then slot 1.
            block.Add(new StoreIndirect(Object, ElementRef(0), BoxInt(1)));
            block.Add(new ExpressionStatement(Separator()));
            block.Add(new StoreIndirect(Object, ElementRef(1), BoxInt(2)));
        }
        else
        {
            block.Add(new StoreIndirect(Object, ElementRef(0), BoxInt(1)));
            block.Add(new StoreIndirect(Object, ElementRef(1), BoxInt(2)));
        }

        var span = new Call(
            AsReadOnlySpan(),
            isVirtual: false,
            [new LoadLocalAddress(0, Buffer), new Constant(2, Int32)]);

        switch (shape)
        {
            case OrderingShape.InlineEffectBeforeSpan:
                // Consume(PrefixEffect(), span): an effect evaluated inline before
                // the span in the same statement — lifting the elements to the span
                // would move them past that effect.
                block.Add(new ExpressionStatement(
                    new Call(ConsumeMethod(), isVirtual: false, [PrefixEffect(), span])));
                break;
            case OrderingShape.SpilledPrefixBeforeSpan:
                // Consume(spilledPrefix, span): only a side-effect-free load precedes
                // the span, so the raise is order preserving.
                block.Add(new ExpressionStatement(
                    new Call(ConsumeMethod(), isVirtual: false, [new LoadStackSlot(0, Object), span])));
                break;
            case OrderingShape.ConditionalSpanArm:
                // Consume(cond ? span : other): the span is in a ternary arm, so it
                // is only conditionally evaluated. Lifting the unconditional element
                // stores into that arm would drop their effects on the false path.
                block.Add(new ExpressionStatement(
                    new Call(ConsumeSpanMethod(), isVirtual: false, [
                        new Conditional(
                            new Constant(true, Bool),
                            span,
                            new LoadLocal(1, SpanObject))])));
                break;
            case OrderingShape.NullCoalescingAssignmentValue:
                // local ??= span: the span is the ??= right operand, evaluated only
                // when the target is null. NothingEffectfulBefore passes (the span is
                // the only child), so only the definite-evaluation allow list rejects
                // this — the missed conditional node from adversarial review.
                block.Add(new NullCoalescingAssignment(1, SpanObject, span));
                break;
            case OrderingShape.UnionSwitchArmValue:
                // local = scrutinee switch { Case => span, ... }: the span is a switch
                // arm value, evaluated only when its arm matches. A union-switch arm is
                // a distinct IR node from SwitchExpressionArm, so the allow list (not an
                // enumerated deny list) must still reject it.
                block.Add(new StoreLocal(1, SpanObject,
                    new UnionSwitchExpression(
                        new LoadArgument(0, "arg", Object),
                        [new UnionSwitchExpressionArm(Object, null, span)])));
                break;
            case OrderingShape.WhileConditionSpan:
                // while (Predicate(span)) { }: the span is unconditionally evaluated
                // but re-evaluated on every loop iteration. Definite evaluation alone
                // is not enough — the span must also be evaluated exactly once, so the
                // allow list rejects a WhileLoop condition edge (it is not a recognized
                // once-through container), leaving the collection flat.
                block.Add(new WhileLoop(
                    new Call(PredicateSpanMethod(), isVirtual: false, [span]),
                    new Block()));
                break;
            case OrderingShape.BinaryOperandSpan:
                // local = IntFromSpan(span) + 1: the span-consuming call is the left
                // operand of a non-short-circuiting Binary, evaluated exactly once and
                // unconditionally. The allow list recognizes Binary, so the collection
                // still raises (recovering fidelity a Call-only allow list would lose).
                block.Add(new StoreLocal(2, Int32,
                    new Binary(
                        BinaryKind.Add,
                        isChecked: false,
                        isUnsigned: false,
                        new Call(IntFromSpanMethod(), isVirtual: false, [span]),
                        new Constant(1, Int32))));
                break;
            case OrderingShape.IfConditionSpan:
                // if (Predicate(span)) { }: the span sits in a forward if-statement
                // condition, evaluated exactly once when the statement is reached. The
                // per-edge allow list accepts the condition edge, so the collection raises.
                block.Add(new IfStatement(
                    new Call(PredicateSpanMethod(), isVirtual: false, [span]),
                    new Block(),
                    null));
                break;
            case OrderingShape.SwitchValueSpan:
                // switch (IntFromSpan(span)) { }: the span sits in a forward switch
                // scrutinee, evaluated exactly once. The per-edge allow list accepts the
                // Switch value edge, so the collection raises.
                block.Add(new Switch(
                    new Call(IntFromSpanMethod(), isVirtual: false, [span]),
                    []));
                break;
            case OrderingShape.ThrowValueSpan:
                // throw MakeException(span): the span-consuming call is the throw value,
                // evaluated exactly once and unconditionally. The allow list recognizes
                // Throw, so the collection raises.
                block.Add(new Throw(
                    new Call(ThrowableFromSpanMethod(), isVirtual: false, [span])));
                break;
            case OrderingShape.ConditionalConditionSpan:
                // local = Predicate(span) ? a : b: the span sits in the ternary
                // *condition*, evaluated exactly once before either arm is chosen. The
                // per-edge allow list accepts the Conditional.Condition edge (the arms
                // stay rejected — see ConditionalSpanArmInConsumer_StaysFlat), so the
                // collection raises (#3129 S4 follow-up #3281).
                block.Add(new StoreLocal(2, Object,
                    new Conditional(
                        new Call(PredicateSpanMethod(), isVirtual: false, [span]),
                        new LoadArgument(0, "a", Object),
                        new LoadArgument(1, "b", Object))));
                break;
            case OrderingShape.SwitchExpressionValueSpan:
                // local = IntFromSpan(span) switch { _ => 0 }: the span sits in the
                // switch-expression scrutinee, evaluated exactly once. The allow list
                // accepts the SwitchExpression.Value edge; the arms stay rejected.
                block.Add(new StoreLocal(2, Int32,
                    new SwitchExpression(
                        new Call(IntFromSpanMethod(), isVirtual: false, [span]),
                        [new SwitchExpressionArm([], isDefault: true, new Constant(0, Int32))])));
                break;
            case OrderingShape.UnionSwitchValueSpan:
                // local = ObjectFromSpan(span) switch { T => v }: the span sits in the
                // union-switch *scrutinee* (contrast UnionSwitchArmValue, where it sits
                // in an arm value and stays flat). The scrutinee runs once, so the
                // Value edge is accepted.
                block.Add(new StoreLocal(2, Object,
                    new UnionSwitchExpression(
                        new Call(ObjectFromSpanMethod(), isVirtual: false, [span]),
                        [new UnionSwitchExpressionArm(Object, null, new LoadArgument(0, "a", Object))])));
                break;
            case OrderingShape.PatternSwitchValueSpan:
                // local = ObjectFromSpan(span) switch { T v => v, _ => d }: the span
                // sits in the pattern-switch scrutinee, evaluated exactly once. The
                // allow list accepts the PatternSwitchExpression.Value edge.
                block.Add(new StoreLocal(2, Object,
                    new PatternSwitchExpression(
                        new Call(ObjectFromSpanMethod(), isVirtual: false, [span]),
                        [new PatternSwitchExpressionArm(Object, null, null, new LoadArgument(1, "v", Object))],
                        new LoadArgument(0, "d", Object))));
                break;
            case OrderingShape.TupleSwitchComponentSpan:
                // local = (IntFromSpan(span), y) switch { _ => 0 }: the span sits in a
                // tuple-switch governing *component*, each read once left to right
                // before matching. The allow list accepts any component edge.
                block.Add(new StoreLocal(2, Int32,
                    new TupleSwitchExpression(
                        [new Call(IntFromSpanMethod(), isVirtual: false, [span]), new LoadArgument(0, "y", Int32)],
                        [new TupleSwitchExpressionArm([], [], new Constant(0, Int32))])));
                break;
            case OrderingShape.ForeachCollectionSpan:
                // foreach (object o in EnumerableFromSpan(span)) { }: the source
                // enumerable is obtained exactly once on entry (the body then repeats).
                // The allow list accepts the ForeachStatement.Collection edge.
                block.Add(new ForeachStatement(
                    2,
                    Object,
                    new Call(EnumerableFromSpanMethod(), isVirtual: false, [span]),
                    new Block()));
                break;
            case OrderingShape.ForInitializerSpan:
                // for (int i = IntFromSpan(span); true; ) { }: the initializer runs
                // exactly once on entry (condition/increment/body repeat). The allow
                // list accepts only the ForLoop.Initializer edge.
                block.Add(new ForLoop(
                    new StoreLocal(2, Int32, new Call(IntFromSpanMethod(), isVirtual: false, [span])),
                    new Constant(true, Bool),
                    new Block(),
                    new Block()));
                break;
            case OrderingShape.UsingResourceSpan:
                // using (ObjectFromSpan(span)) { }: the resource is acquired exactly
                // once on entry. The allow list accepts the UsingStatement.Resource edge.
                block.Add(new UsingStatement(
                    2,
                    Object,
                    new Call(ObjectFromSpanMethod(), isVirtual: false, [span]),
                    new BlockContainer()));
                break;
            case OrderingShape.LockObjectSpan:
                // lock (ObjectFromSpan(span)) { }: the lock object is evaluated exactly
                // once on entry. The allow list accepts the Lock.LockObject edge.
                block.Add(new ILInspector.Decompiler.Pipeline.Lock(
                    new Call(ObjectFromSpanMethod(), isVirtual: false, [span]),
                    new BlockContainer()));
                break;
            case OrderingShape.ForConditionSpan:
                // for (int i = 0; Predicate(span); ) { }: the span sits in the for-loop
                // *condition*, re-evaluated every iteration — the close negative of
                // ForInitializerSpan. Only the Initializer edge is allow-listed, so the
                // condition edge is rejected and the collection stays flat.
                block.Add(new ForLoop(
                    new StoreLocal(2, Int32, new Constant(0, Int32)),
                    new Call(PredicateSpanMethod(), isVirtual: false, [span]),
                    new Block(),
                    new Block()));
                break;
            case OrderingShape.ByValueBufferStoreAfterSpan:
                // The canonical raising shape, then a by-value store that reuses the
                // now-dead buffer slot (0) for a spellable value after the span is
                // consumed. csc never emits this; obfuscated IL can. The store names
                // slot 0 without being a load or an address, so the pass's reference
                // tally misses it entirely and the raise still fires soundly (it
                // re-sequences no element effect). But the slot is not actually dead:
                // MarkLocalEliminated must verify liveness and refuse to mark it, or
                // the buffer's unspellable type would be dropped from fidelity over
                // output that still writes the slot — a false Full (#3295).
                block.Add(new StoreLocal(1, SpanObject, span));
                block.Add(new StoreLocal(0, Object, BoxInt(7)));
                break;
            default:
                // ReadOnlySpan<object> span = InlineArrayAsReadOnlySpan(ref buffer, 2); (local 1)
                block.Add(new StoreLocal(1, SpanObject, span));
                break;
        }

        block.Add(new Return(null));

        var body = new BlockContainer();
        body.Add(block);
        var signature = new MethodSignature(
            Void,
            [],
            HasThis: false,
            GenericParameterCount: 0);
        var declaringType = TypeRef.Definition("Synthetic", "", "T");
        return new IrFunction("M", declaringType, signature, [Buffer, SpanObject], body);
    }

    static Box BoxInt(int value) => new(Int32, new Constant(value, Int32));

    // A void call with no buffer reference — a standalone side-effecting statement.
    static Call Separator()
        => new(
            new MethodRef(
                TypeRef.Definition("Synthetic", "", "Fx"),
                "Separator",
                Void,
                [],
                HasThis: false),
            isVirtual: false,
            []);

    // An object-returning side-effecting call, used as a consumer prefix argument.
    static Call PrefixEffect()
        => new(
            new MethodRef(
                TypeRef.Definition("Synthetic", "", "Fx"),
                "PrefixEffect",
                Object,
                [],
                HasThis: false),
            isVirtual: false,
            []);

    // void Consume(object prefix, ReadOnlySpan<object> span)
    static MethodRef ConsumeMethod()
        => new(
            TypeRef.Definition("Synthetic", "", "Fx"),
            "Consume",
            Void,
            [Object, SpanObject],
            HasThis: false);

    // void Consume(ReadOnlySpan<object> span)
    static MethodRef ConsumeSpanMethod()
        => new(
            TypeRef.Definition("Synthetic", "", "Fx"),
            "Consume",
            Void,
            [SpanObject],
            HasThis: false);

    // bool Predicate(ReadOnlySpan<object> span)
    static MethodRef PredicateSpanMethod()
        => new(
            TypeRef.Definition("Synthetic", "", "Fx"),
            "Predicate",
            Bool,
            [SpanObject],
            HasThis: false);

    // int IntFromSpan(ReadOnlySpan<object> span)
    static MethodRef IntFromSpanMethod()
        => new(
            TypeRef.Definition("Synthetic", "", "Fx"),
            "IntFromSpan",
            Int32,
            [SpanObject],
            HasThis: false);

    // object MakeException(ReadOnlySpan<object> span) — a reference-returning call
    // used as a throw value; the pass never type-checks throwability.
    static MethodRef ThrowableFromSpanMethod()
        => new(
            TypeRef.Definition("Synthetic", "", "Fx"),
            "MakeException",
            Object,
            [SpanObject],
            HasThis: false);

    // object ObjectFromSpan(ReadOnlySpan<object> span) — a reference-returning call
    // used as a selection scrutinee / using resource / lock object. The pass checks
    // only the structural run-once edge, never the value's runtime type.
    static MethodRef ObjectFromSpanMethod()
        => new(
            TypeRef.Definition("Synthetic", "", "Fx"),
            "ObjectFromSpan",
            Object,
            [SpanObject],
            HasThis: false);

    // object EnumerableFromSpan(ReadOnlySpan<object> span) — a reference-returning
    // call used as a foreach source; the pass never type-checks enumerability.
    static MethodRef EnumerableFromSpanMethod()
        => new(
            TypeRef.Definition("Synthetic", "", "Fx"),
            "EnumerableFromSpan",
            Object,
            [SpanObject],
            HasThis: false);

    /// <summary>
    /// Builds the two-element params ReadOnlySpan&lt;object&gt; collection shape:
    /// element 0 is a direct indirect store of <c>box a</c>; element 1's value
    /// carries a branch, so its element-ref address is spilled to slot 0 and its
    /// conditional value to slot 1 before the store through the spilled address.
    /// <paramref name="defect"/> perturbs exactly one guard input to prove the
    /// negative leaves the collection flat.
    /// </summary>
    static IrFunction BuildSpilledCollection(SpillDefect defect)
    {
        const int AddressSlot = 0;
        const int ValueSlot = 1;

        var block = new Block();

        // <>y__InlineArray2<object> buffer = default; (local 0)
        block.Add(new InitObject(Buffer, new LoadLocalAddress(0, Buffer)));

        // Element 0: *(InlineArrayElementRef(ref buffer, 0)) = box a;
        block.Add(new StoreIndirect(
            Object,
            ElementRef(0),
            new Box(Int32, new LoadArgument(0, "a", Int32))));

        // Element 1: ref object address = InlineArrayElementRef(ref buffer, 1);
        block.Add(new StoreStackSlot(AddressSlot, ElementRef(1)));
        // object value = flag ? box 1 : box 2;
        block.Add(new StoreStackSlot(
            ValueSlot,
            new Conditional(
                new LoadArgument(1, "flag", Bool),
                new Box(Int32, new Constant(1, Int32)),
                new Box(Int32, new Constant(2, Int32)))));
        // *address = value;
        block.Add(new StoreIndirect(Object, new LoadStackSlot(AddressSlot, ByRefObject), new LoadStackSlot(ValueSlot, Object))
        {
            IsVolatile = defect == SpillDefect.VolatileStore,
        });

        // A second read of a spill slot perturbs the single-use guard.
        if (defect == SpillDefect.AddressSlotReadTwice)
            block.Add(new StoreLocal(2, ByRefObject, new LoadStackSlot(AddressSlot, ByRefObject)));
        else if (defect == SpillDefect.ValueSlotReadTwice)
            block.Add(new StoreLocal(2, Object, new LoadStackSlot(ValueSlot, Object)));

        // ReadOnlySpan<object> span = InlineArrayAsReadOnlySpan<...>(ref buffer, 2); (local 1)
        block.Add(new StoreLocal(1, SpanObject, new Call(
            AsReadOnlySpan(),
            isVirtual: false,
            [new LoadLocalAddress(0, Buffer), new Constant(2, Int32)])));
        block.Add(new Return(null));

        var body = new BlockContainer();
        body.Add(block);
        var signature = new MethodSignature(
            Void,
            [new Parameter("a", Int32), new Parameter("flag", Bool)],
            HasThis: false,
            GenericParameterCount: 0);
        var declaringType = TypeRef.Definition("Synthetic", "", "T");
        return defect switch
        {
            SpillDefect.AddressSlotReadTwice =>
                new IrFunction("M", declaringType, signature, [Buffer, SpanObject, ByRefObject], body),
            SpillDefect.ValueSlotReadTwice =>
                new IrFunction("M", declaringType, signature, [Buffer, SpanObject, Object], body),
            _ => new IrFunction("M", declaringType, signature, [Buffer, SpanObject], body),
        };
    }

    static Call ElementRef(int slot)
        => new(
            new MethodRef(
                TypeRef.Definition(TypeRef.CoreLibrary, "", "<PrivateImplementationDetails>"),
                "InlineArrayElementRef",
                ByRefObject,
                [TypeRef.ByRef(Buffer), Int32],
                HasThis: false)
            {
                TypeArguments = [Buffer, Object],
                DeclaringTypeCompilerGenerated = MetadataFactState.Yes,
            },
            isVirtual: false,
            [new LoadLocalAddress(0, Buffer), new Constant(slot, Int32)]);

    static MethodRef AsReadOnlySpan()
        => new(
            TypeRef.Definition(TypeRef.CoreLibrary, "", "<PrivateImplementationDetails>"),
            "InlineArrayAsReadOnlySpan",
            SpanObject,
            [TypeRef.ByRef(Buffer), Int32],
            HasThis: false)
        {
            TypeArguments = [Buffer, Object],
            DeclaringTypeCompilerGenerated = MetadataFactState.Yes,
        };
}
