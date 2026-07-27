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

    enum OrderingShape
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
