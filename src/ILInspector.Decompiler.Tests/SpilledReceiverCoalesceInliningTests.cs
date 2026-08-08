using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

// A reference-type field initializer `field = arg ?? new()` spills the receiver
// `this` and the coalesce result into stack slots across the `??` branch
// (S_0 = this; S_1 = arg ?? new(); S_0.field = S_1). ExpressionInliningPass now
// collapses both temporaries — reference-type `this` is a plain, non-reassignable
// object reference, so the receiver spill folds via the live-range mode, which
// unblocks the value spill (a non-first-leaf, non-pure value deferred only past
// the now-pure receiver) via the preceding-evaluation-pure gate. A value-type
// receiver is a byref managed pointer an intervening call could mutate, so it
// stays spilled.
[Trait("Area", "Pass")]
public class SpilledReceiverCoalesceInliningTests
{
    static string PrintRaised(string typeName, string methodName)
    {
        using var source = MetadataSource.Open(typeof(SpilledCoalesceField).Assembly.Location);
        var function = IrImporter.Import(source, typeName, methodName);
        Assert.NotNull(function);
        var result = CSharpPrinter.PrintRaised(function!, method => IrImporter.Import(source, method));
        Assert.True(result.Succeeded, string.Join("\n", result.Diagnostics.Select(d => d.Message)));
        Assert.NotNull(result.Output);
        return result.Output!.ReplaceLineEndings("\n");
    }

    // Both defects together on a real compiled explicit constructor: the spilled
    // receiver (S_0) and coalesce (S_1) collapse into one clean field store.
    [Fact]
    public void ExplicitConstructor_SpilledReceiverAndCoalesce_CollapseToFieldInit()
    {
        string output = PrintRaised(typeof(SpilledCoalesceField).FullName!, ".ctor");

        Assert.Contains("options ?? new SpilledCoalesceOptions()", output);
        Assert.DoesNotContain("S_0", output);
        Assert.DoesNotContain("S_1", output);
        Assert.DoesNotContain("Unsupported", output);
    }

    // A primary constructor emits the implicit base object..ctor after the field
    // inits; with the temps spilled the prologue was not all instance-field
    // stores, so the base call leaked as an unsupported residual. Collapsing the
    // temps restores the clean prologue that elides the base call.
    [Fact]
    public void PrimaryConstructor_SpilledCoalesceField_ElidesBaseCallResidual()
    {
        string output = PrintRaised(typeof(SpilledCoalescePrimaryField).FullName!, ".ctor");

        Assert.Contains("_options = options ?? new SpilledCoalesceOptions();", output);
        Assert.DoesNotContain("Unsupported", output);
        Assert.DoesNotContain("S_0", output);
        Assert.DoesNotContain("S_1", output);
    }

    // Direct-IR A/B on the receiver-spill gate: a reference-type `this` folds
    // into the field store's receiver; a value-type `this` (byref, possibly
    // mutated by an intervening call) stays spilled.
    [Theory]
    [InlineData("System", "Object", true)]   // class receiver ⇒ fold
    [InlineData("System", "ValueType", false)] // struct receiver ⇒ keep spilled
    public void ReceiverSpill_FoldsOnlyForReferenceTypeThis(string baseNamespace, string baseName, bool folds)
    {
        var declaringType = TypeRef.Definition("Synthetic", "Samples", "SpillReceiver");
        var baseType = TypeRef.CoreLib(baseNamespace, baseName);
        var intType = TypeRef.CoreLib("System", "Int32");
        var voidType = TypeRef.CoreLib("System", "Void");
        var field = new FieldRef(declaringType, "_x", intType);
        var touch = new MethodRef(declaringType, "Touch", voidType, [], HasThis: false);

        // S_0 = this; Touch(); this._x = 0
        // The intervening void call is a non-removable statement, so the receiver
        // spill's single use is never adjacent to its store — only the live-range
        // path (governed by ReceiverThisIsPure) can fold it. A reference-type
        // `this` is an immutable object reference and folds; a value-type `this`
        // is a byref whose target the call could mutate, so it stays spilled.
        var container = new BlockContainer();
        var block = new Block(0);
        container.Add(block);
        block.Add(new StoreStackSlot(0, new LoadArgument(0, "this", declaringType)));
        block.Add(new ExpressionStatement(new Call(touch, isVirtual: false, [])));
        block.Add(new StoreField(field, new LoadStackSlot(0, declaringType), new Constant(0, intType)));
        block.Add(new Return(null));
        var signature = new MethodSignature(voidType, [], HasThis: true, GenericParameterCount: 0);
        var function = new IrFunction("SetX", declaringType, signature, [], container) { BaseType = baseType };

        new ExpressionInliningPass().Run(function, PassContext.None);

        var storeField = function.Descendants.OfType<StoreField>().Single();
        if (folds)
            Assert.IsType<LoadArgument>(storeField.Instance);
        else
            Assert.IsType<LoadStackSlot>(storeField.Instance);
    }

    // A non-pure spilled value whose single use sits in a conditionally-evaluated
    // position (here a ternary true-arm) must NOT inline: the spilled store always
    // ran, so folding the call into the arm would change it from unconditional to
    // conditional execution. The preceding-evaluation-pure gate alone does not
    // catch this (the condition is pure); LoadIsUnconditionallyEvaluated does
    // (#3500 adversarial review, GPT).
    [Fact]
    public void NonPureSpill_InConditionalArm_IsNotInlined()
    {
        var holder = TypeRef.Definition("Synthetic", "Samples", "Guarded");
        var intType = TypeRef.CoreLib("System", "Int32");
        var boolType = TypeRef.CoreLib("System", "Boolean");
        var sideEffect = new MethodRef(holder, "SideEffect", intType, [], HasThis: false);

        // S_0 = SideEffect(); return condition ? S_0 : 0;
        var container = new BlockContainer();
        var block = new Block(0);
        container.Add(block);
        block.Add(new StoreStackSlot(0, new Call(sideEffect, isVirtual: false, [])));
        block.Add(new Return(new Conditional(
            new LoadArgument(0, "condition", boolType),
            new LoadStackSlot(0, intType),
            new Constant(0, intType))));
        var signature = new MethodSignature(intType,
            [new Parameter("condition", boolType)], HasThis: false, GenericParameterCount: 0);
        var function = new IrFunction("Guard", holder, signature, [], container);

        new ExpressionInliningPass().Run(function, PassContext.None);

        var survivingStore = Assert.Single(function.Descendants.OfType<StoreStackSlot>());
        Assert.IsType<Call>(survivingStore.Value);
        var conditional = function.Descendants.OfType<Conditional>().Single();
        Assert.IsType<LoadStackSlot>(conditional.WhenTrue);
    }

    // A `??=` exposes its guarded fallback as Children[0], so IsFirstEvaluatedLeaf
    // misreports the fallback as first-evaluated and the firstLeaf path would move
    // a non-pure spill into the conditionally-evaluated right side. The unconditional
    // guard now runs for every non-pure inline (not just the precedingPure path) and
    // ChildIsUnconditional classifies the `??=` fallback as conditional, so the store
    // survives (#3500 adversarial review, GPT).
    [Fact]
    public void NonPureSpill_InNullCoalescingAssignmentFallback_IsNotInlined()
    {
        var holder = TypeRef.Definition("Synthetic", "Samples", "Guarded");
        var intType = TypeRef.CoreLib("System", "Int32");
        var sideEffect = new MethodRef(holder, "SideEffect", intType, [], HasThis: false);

        // S_0 = SideEffect(); V_0 ??= S_0;
        var container = new BlockContainer();
        var block = new Block(0);
        container.Add(block);
        block.Add(new StoreStackSlot(0, new Call(sideEffect, isVirtual: false, [])));
        block.Add(new NullCoalescingAssignment(0, intType, new LoadStackSlot(0, intType)));
        var signature = new MethodSignature(intType, [], HasThis: false, GenericParameterCount: 0);
        var function = new IrFunction("Guard", holder, signature, [], container);

        new ExpressionInliningPass().Run(function, PassContext.None);

        var survivingStore = Assert.Single(function.Descendants.OfType<StoreStackSlot>());
        Assert.IsType<Call>(survivingStore.Value);
        var nca = function.Descendants.OfType<NullCoalescingAssignment>().Single();
        Assert.IsType<LoadStackSlot>(nca.Value);
    }

    // NullCoalescingFieldAssignmentExpression (`obj.F ??= fallback`) is an IrExpression
    // — the generic non-expression guard does not catch it — whose Value is
    // conditionally evaluated. ChildIsUnconditional must classify its fallback as
    // conditional so a non-pure spill stays out (#3500 adversarial review, Gemini).
    [Fact]
    public void NonPureSpill_InNullCoalescingFieldAssignmentExpressionFallback_IsNotInlined()
    {
        var holder = TypeRef.Definition("Synthetic", "Samples", "Guarded");
        var intType = TypeRef.CoreLib("System", "Int32");
        var objType = TypeRef.CoreLib("System", "Object");
        var sideEffect = new MethodRef(holder, "SideEffect", intType, [], HasThis: false);
        var field = new FieldRef(holder, "F", intType);

        // S_0 = SideEffect(); return obj.F ??= S_0;
        var container = new BlockContainer();
        var block = new Block(0);
        container.Add(block);
        block.Add(new StoreStackSlot(0, new Call(sideEffect, isVirtual: false, [])));
        block.Add(new Return(new NullCoalescingFieldAssignmentExpression(
            field, new LoadArgument(0, "obj", objType), new LoadStackSlot(0, intType))));
        var signature = new MethodSignature(intType,
            [new Parameter("obj", objType)], HasThis: false, GenericParameterCount: 0);
        var function = new IrFunction("Guard", holder, signature, [], container);

        new ExpressionInliningPass().Run(function, PassContext.None);

        var survivingStore = Assert.Single(function.Descendants.OfType<StoreStackSlot>());
        Assert.IsType<Call>(survivingStore.Value);
        var ncfae = function.Descendants.OfType<NullCoalescingFieldAssignmentExpression>().Single();
        Assert.IsType<LoadStackSlot>(ncfae.Value);
    }

    // Guard-rail against over-restriction: C# tuple `==`/`!=` evaluates every element
    // of both operand tuples before any element comparison (verified by compiled
    // canary), so its components are UNCONDITIONAL. The guard must NOT reject folding
    // a spilled value into a tuple-equality component (#3500).
    [Fact]
    public void NonPureSpill_InTupleEqualityComponent_StillInlines()
    {
        var holder = TypeRef.Definition("Synthetic", "Samples", "Guarded");
        var intType = TypeRef.CoreLib("System", "Int32");
        var boolType = TypeRef.CoreLib("System", "Boolean");
        var tupleType = TypeRef.CoreLib("System", "ValueTuple");
        var sideEffect = new MethodRef(holder, "SideEffect", intType, [], HasThis: false);

        // S_0 = SideEffect(); return (0, S_0) == (1, 1);
        var container = new BlockContainer();
        var block = new Block(0);
        container.Add(block);
        block.Add(new StoreStackSlot(0, new Call(sideEffect, isVirtual: false, [])));
        block.Add(new Return(new TupleBinaryExpression(true, tupleType,
            new TupleExpression(tupleType, [new Constant(0, intType), new LoadStackSlot(0, intType)]),
            new TupleExpression(tupleType, [new Constant(1, intType), new Constant(1, intType)]))));
        var signature = new MethodSignature(boolType, [], HasThis: false, GenericParameterCount: 0);
        var function = new IrFunction("Guard", holder, signature, [], container);

        new ExpressionInliningPass().Run(function, PassContext.None);

        Assert.Empty(function.Descendants.OfType<StoreStackSlot>());
        var tupleBinary = function.Descendants.OfType<TupleBinaryExpression>().Single();
        var leftTuple = Assert.IsType<TupleExpression>(tupleBinary.Left);
        Assert.IsType<Call>(leftTuple.Elements[1]);
    }

    // `receiver with { P = fallback }` clones the receiver (a possibly effectful /
    // throwing copy constructor) BEFORE evaluating any initializer value. The
    // receiver is pure, so PrecedingEvaluationIsPure alone would accept folding a
    // non-pure spill into the initializer — moving it past the clone.
    // ChildFollowsHiddenOperation rejects any non-receiver child of a
    // WithExpression, so the store survives (#3500 adversarial review, GPT).
    [Fact]
    public void NonPureSpill_InWithExpressionInitializer_IsNotInlined()
    {
        var holder = TypeRef.Definition("Synthetic", "Samples", "Rec");
        var intType = TypeRef.CoreLib("System", "Int32");
        var sideEffect = new MethodRef(holder, "SideEffect", intType, [], HasThis: false);

        // S_0 = SideEffect(); return receiver with { P = S_0 };
        var container = new BlockContainer();
        var block = new Block(0);
        container.Add(block);
        block.Add(new StoreStackSlot(0, new Call(sideEffect, isVirtual: false, [])));
        block.Add(new Return(new WithExpression(
            new LoadArgument(0, "receiver", holder),
            [new InitializerEntry("P", [new LoadStackSlot(0, intType)])])));
        var signature = new MethodSignature(holder,
            [new Parameter("receiver", holder)], HasThis: false, GenericParameterCount: 0);
        var function = new IrFunction("M", holder, signature, [], container);

        new ExpressionInliningPass().Run(function, PassContext.None);

        var survivingStore = Assert.Single(function.Descendants.OfType<StoreStackSlot>());
        Assert.IsType<Call>(survivingStore.Value);
        var with = function.Descendants.OfType<WithExpression>().Single();
        Assert.IsType<LoadStackSlot>(with.Entries[0].Arguments[0]);
    }

    // `$"{formatter}{S_0}"` constructs a handler and appends each part in turn, so
    // the second hole is preceded by AppendFormatted(formatter) — which for an
    // IFormattable/custom-handler value runs arbitrary user code. The formatter
    // load is pure, so PrecedingEvaluationIsPure alone would accept folding the
    // non-pure spill into the second hole, moving SideEffect() past that append.
    // ChildFollowsHiddenOperation rejects every formatted-value child of an
    // InterpolatedStringExpression, so the store survives (#3500 adversarial
    // review, GPT).
    [Fact]
    public void NonPureSpill_InInterpolatedStringHole_IsNotInlined()
    {
        var holder = TypeRef.Definition("Synthetic", "Samples", "Holder");
        var strType = TypeRef.CoreLib("System", "String");
        var objType = TypeRef.CoreLib("System", "Object");
        var sideEffect = new MethodRef(holder, "SideEffect", objType, [], HasThis: false);

        // S_0 = SideEffect(); return $"{formatter}{S_0}";
        var container = new BlockContainer();
        var block = new Block(0);
        container.Add(block);
        block.Add(new StoreStackSlot(0, new Call(sideEffect, isVirtual: false, [])));
        InterpolatedStringPart[] parts =
        [
            InterpolatedStringPart.FormattedValue(0),
            InterpolatedStringPart.FormattedValue(1),
        ];
        block.Add(new Return(new InterpolatedStringExpression(parts,
            [new LoadArgument(0, "formatter", objType), new LoadStackSlot(0, objType)])));
        var signature = new MethodSignature(strType,
            [new Parameter("formatter", objType)], HasThis: false, GenericParameterCount: 0);
        var function = new IrFunction("M", holder, signature, [], container);

        new ExpressionInliningPass().Run(function, PassContext.None);

        var survivingStore = Assert.Single(function.Descendants.OfType<StoreStackSlot>());
        Assert.IsType<Call>(survivingStore.Value);
        var interp = function.Descendants.OfType<InterpolatedStringExpression>().Single();
        Assert.IsType<LoadStackSlot>(interp.FormattedValues[1]);
    }

    // The FIRST interpolation hole takes the firstLeaf path, so the guard must be
    // universal. `$"{S_0}"` still constructs the handler before the hole is
    // evaluated; the handler reserves an object identity and rents a pooled buffer,
    // which a non-pure value that reads allocation order can observe (GPT's
    // ArrayPool identity repro). The universal LoadCrossesHiddenOperation guard
    // keeps the store even though the load is the first evaluated leaf (#3500,
    // GPT). Verified to cost nothing on the corpus.
    [Fact]
    public void NonPureSpill_InFirstInterpolatedStringHole_IsNotInlined()
    {
        var holder = TypeRef.Definition("Synthetic", "Samples", "Holder");
        var strType = TypeRef.CoreLib("System", "String");
        var objType = TypeRef.CoreLib("System", "Object");
        var sideEffect = new MethodRef(holder, "SideEffect", objType, [], HasThis: false);

        // S_0 = SideEffect(); return $"{S_0}";  (single, first hole -> firstLeaf)
        var container = new BlockContainer();
        var block = new Block(0);
        container.Add(block);
        block.Add(new StoreStackSlot(0, new Call(sideEffect, isVirtual: false, [])));
        InterpolatedStringPart[] parts = [InterpolatedStringPart.FormattedValue(0)];
        block.Add(new Return(new InterpolatedStringExpression(parts,
            [new LoadStackSlot(0, objType)])));
        var signature = new MethodSignature(strType, [], HasThis: false, GenericParameterCount: 0);
        var function = new IrFunction("M", holder, signature, [], container);

        new ExpressionInliningPass().Run(function, PassContext.None);

        var survivingStore = Assert.Single(function.Descendants.OfType<StoreStackSlot>());
        Assert.IsType<Call>(survivingStore.Value);
        var interp = function.Descendants.OfType<InterpolatedStringExpression>().Single();
        Assert.IsType<LoadStackSlot>(interp.FormattedValues[0]);
    }

    // `new T[] { S_0 }` (ArrayLiteral, e.g. from ArrayLiteralFromStoresPass)
    // allocates the array (newarr) BEFORE evaluating any element. Folding the
    // non-pure spill into the element reorders it past the allocation, changing
    // allocation/exception ordering. ChildFollowsHiddenOperation rejects every
    // ArrayLiteral element, so the store survives (#3500 adversarial review,
    // Gemini).
    [Fact]
    public void NonPureSpill_InArrayLiteralElement_IsNotInlined()
    {
        var holder = TypeRef.Definition("Synthetic", "Samples", "Holder");
        var intType = TypeRef.CoreLib("System", "Int32");
        var arrayType = TypeRef.SzArray(intType);
        var sideEffect = new MethodRef(holder, "SideEffect", intType, [], HasThis: false);

        // S_0 = SideEffect(); return new int[] { S_0 };
        var container = new BlockContainer();
        var block = new Block(0);
        container.Add(block);
        block.Add(new StoreStackSlot(0, new Call(sideEffect, isVirtual: false, [])));
        block.Add(new Return(new ArrayLiteral(intType, arrayType,
            [new LoadStackSlot(0, intType)])));
        var signature = new MethodSignature(arrayType, [], HasThis: false, GenericParameterCount: 0);
        var function = new IrFunction("M", holder, signature, [], container);

        new ExpressionInliningPass().Run(function, PassContext.None);

        var survivingStore = Assert.Single(function.Descendants.OfType<StoreStackSlot>());
        Assert.IsType<Call>(survivingStore.Value);
        var literal = function.Descendants.OfType<ArrayLiteral>().Single();
        Assert.IsType<LoadStackSlot>(literal.Elements[0]);
    }

    // `[first, S_0]` (CollectionExpression) allocates the collection (newobj list /
    // newarr) before evaluating elements and may interleave Add / spread MoveNext
    // (user code) between them. Folding a non-pure spill into a later element
    // reorders it past those operations. ChildFollowsHiddenOperation rejects every
    // CollectionExpression element, so the store survives (#3500 adversarial
    // review, Gemini).
    [Fact]
    public void NonPureSpill_InCollectionExpressionElement_IsNotInlined()
    {
        var holder = TypeRef.Definition("Synthetic", "Samples", "Holder");
        var intType = TypeRef.CoreLib("System", "Int32");
        var listType = TypeRef.CoreLib("System.Collections.Generic", "List");
        var sideEffect = new MethodRef(holder, "SideEffect", intType, [], HasThis: false);

        // S_0 = SideEffect(); return [first, S_0];
        var container = new BlockContainer();
        var block = new Block(0);
        container.Add(block);
        block.Add(new StoreStackSlot(0, new Call(sideEffect, isVirtual: false, [])));
        block.Add(new Return(new CollectionExpression(intType, listType,
            [new LoadArgument(0, "first", intType), new LoadStackSlot(0, intType)])));
        var signature = new MethodSignature(listType,
            [new Parameter("first", intType)], HasThis: false, GenericParameterCount: 0);
        var function = new IrFunction("M", holder, signature, [], container);

        new ExpressionInliningPass().Run(function, PassContext.None);

        var survivingStore = Assert.Single(function.Descendants.OfType<StoreStackSlot>());
        Assert.IsType<Call>(survivingStore.Value);
        var collection = function.Descendants.OfType<CollectionExpression>().Single();
        Assert.IsType<LoadStackSlot>(collection.Elements[1]);
    }

    // `stackalloc T[] { S_0 }` (StackAllocArray with an initializer) evaluates the
    // count, performs `localloc`, THEN evaluates its elements. The stack address the
    // allocation reserves is program-observable, so folding a non-pure spill into an
    // initializer element reorders it past the `localloc`. ChildFollowsHiddenOperation
    // rejects every non-Count child of a StackAllocArray, so the store survives; the
    // Count child (evaluated before the allocation) stays foldable (#3500 adversarial
    // review, GPT).
    [Fact]
    public void NonPureSpill_InStackAllocArrayElement_IsNotInlined()
    {
        var holder = TypeRef.Definition("Synthetic", "Samples", "Holder");
        var longType = TypeRef.CoreLib("System", "Int64");
        var spanType = TypeRef.CoreLib("System", "Span");
        var sideEffect = new MethodRef(holder, "SideEffect", longType, [], HasThis: false);

        // S_0 = SideEffect(); return stackalloc long[1] { S_0 };
        var container = new BlockContainer();
        var block = new Block(0);
        container.Add(block);
        block.Add(new StoreStackSlot(0, new Call(sideEffect, isVirtual: false, [])));
        block.Add(new Return(new StackAllocArray(longType, new Constant(1, longType),
            spanType, [new LoadStackSlot(0, longType)])));
        var signature = new MethodSignature(spanType, [], HasThis: false, GenericParameterCount: 0);
        var function = new IrFunction("M", holder, signature, [], container);

        new ExpressionInliningPass().Run(function, PassContext.None);

        var survivingStore = Assert.Single(function.Descendants.OfType<StoreStackSlot>());
        Assert.IsType<Call>(survivingStore.Value);
        var stackAlloc = function.Descendants.OfType<StackAllocArray>().Single();
        Assert.IsType<LoadStackSlot>(stackAlloc.Elements.Span[0]);
    }
}
