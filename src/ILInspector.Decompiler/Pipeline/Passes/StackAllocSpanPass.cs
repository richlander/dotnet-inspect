namespace ILInspector.Decompiler.Pipeline;

using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Raises the compiler's lowering of <c>Span&lt;T&gt; s = stackalloc T[n]</c> back
/// into a source-level <c>stackalloc T[n]</c>. The source form lowers to a
/// <c>localloc</c> of <c>n * sizeof(T)</c> bytes fed to the <c>Span&lt;T&gt;(void*,
/// int)</c> constructor, which left flat renders as
/// <c>new Span&lt;T&gt;(stackalloc byte[...], n)</c> — and that does not compile:
/// a <c>stackalloc</c> in argument position types as <c>Span&lt;byte&gt;</c>, not
/// <c>void*</c>, and the byte-count expression carries a <c>nuint</c> that will not
/// implicitly convert to <c>int</c>. Rewriting to <c>stackalloc T[n]</c> (target-typed
/// to the span) round-trips to the same lowering.
///
/// <para>The element type is the span's type argument; the element count is the
/// constructor's <c>length</c> argument (the byte size on the inner
/// <see cref="StackAllocate"/> is the redundant <c>n * sizeof(T)</c> and is dropped).
/// This raise is independent of the memory-safety rules — the lowered shape never
/// compiled — so the pass runs unconditionally. Whether the result needs an
/// <c>unsafe</c> context (only under <c>[SkipLocalsInit]</c>, per the stackalloc
/// rule) is decided later by the printer.</para>
///
/// <para>The pointer argument is usually the stackalloc directly, but
/// <see cref="StackAllocInitializerPass"/> (#2869) can leave the recovered
/// initializer sitting behind a compiler-owned stack slot indirection instead
/// — <c>slot = stackalloc T[n] {...}; ...; new Span&lt;T&gt;((void*)slot, n)</c> —
/// because that pass only replaces the slot's stored value, never the later
/// constructor call. This pass also resolves that indirection, but only when the
/// slot is exclusively owned by the one store/load pair reaching this
/// constructor (see <see cref="TryResolveOwnedSlotSource"/>), so the allocation
/// is never moved out from under some other reader or writer of the slot.</para>
/// </summary>
public sealed class StackAllocSpanPass : IIrPass
{
    public string Name => "stackalloc-span";

    public void Run(IrFunction function, PassContext context)
    {
        var storesBySlot = GenericDeclarationPatternProof.DescendantsOutsideNestedFunctions(function)
            .OfType<StoreStackSlot>()
            .GroupBy(s => s.Slot)
            .ToDictionary(g => g.Key, g => g.ToList());
        var loadsBySlot = GenericDeclarationPatternProof.DescendantsOutsideNestedFunctions(function)
            .OfType<LoadStackSlot>()
            .GroupBy(s => s.Slot)
            .ToDictionary(g => g.Key, g => g.ToList());

        foreach (var newObject in function.Descendants.OfType<NewObject>().ToList())
        {
            if (!MemberIdentity.IsStackAllocSpanConstructor(newObject, out var element))
                continue;

            // Span<T>(void* pointer, int length): the pointer is the stackalloc,
            // the length is the logical element count.
            if (newObject.Arguments is not [{ } pointer, var count])
                continue;

            // stackalloc T[n] evaluates n *before* performing the allocation,
            // but the constructor's own length argument here originally
            // evaluated *after* the pointer argument (left-to-right call
            // evaluation) -- i.e. after the localloc. Raising always inverts
            // that order, so an effectful count would silently reorder past
            // the allocation (and any StackOverflowException it might throw)
            // to run first. Retaining the exact same count expression is not
            // enough to preserve behavior; it must be provably pure.
            if (!IsSideEffectFree(count))
                continue;

            StoreStackSlot? ownedStore = null;
            IrExpression source;

            if (TryNormalizeStackAllocSource(pointer, count, element, out var directSource))
            {
                source = directSource;
            }
            else if (TryResolveOwnedSlotSource(newObject, pointer, count, element, storesBySlot, loadsBySlot, out var slotSource, out ownedStore))
            {
                source = slotSource;
            }
            else
            {
                continue;
            }

            // A source's own size/count must agree with this constructor's
            // own length argument and Span<T> type argument. Once the
            // pointer is resolved (whether directly or through a slot), the
            // source and the constructor's count/element-type are
            // independent expressions in the tree -- nothing else proves
            // they describe the same span, so a mismatch here would silently
            // reinterpret the allocation under the wrong element type,
            // change the observable Span.Length, or (for a raw
            // StackAllocate) reserve a different number of bytes than the
            // constructor's count implies. StackAllocArray.Count is already
            // an element count; StackAllocate.Size is a byte count and must
            // be proven equal to count * sizeof(element) instead.
            IEnumerable<IrExpression>? elements;
            if (source is StackAllocate stackAllocate)
            {
                if (!IsProvenByteSize(stackAllocate.Size, count, element))
                    continue;

                elements = null;
            }
            else if (source is StackAllocArray sourceArray)
            {
                if (!sourceArray.ElementType.Equals(element))
                    continue;

                if (sourceArray.HasInitializer)
                {
                    if (count is not Constant { Value: int expectedCount } || expectedCount != sourceArray.Elements.Length)
                        continue;

                    elements = GetInitializerElements(sourceArray);
                }
                else
                {
                    // Count is already an element count (unlike Size), so it
                    // is comparable to the constructor's length argument even
                    // when dynamic: a literal source count requires an equal
                    // literal ctor count, and a dynamic source count requires
                    // the ctor's count to be the structurally identical
                    // expression -- otherwise two independent, unrelated
                    // dynamic quantities (e.g. two different locals) could
                    // silently pass each other off as the same count.
                    var agrees = sourceArray.Count is Constant { Value: int sourceCount }
                        ? sourceCount >= 0 && count is Constant { Value: int ctorCount } && ctorCount == sourceCount
                        : StructurallyEqual(sourceArray.Count, count);
                    if (!agrees)
                        continue;

                    elements = null;
                }
            }
            else
            {
                elements = null;
            }

            count.Detach();
            var raised = new StackAllocArray(element, count, newObject.ResultType, elements);
            raised.InheritSourceOffset(newObject);
            context.Stepper.StepOver("raise Span-over-stackalloc to stackalloc T[n]", newObject);
            newObject.ReplaceWith(raised);
            ownedStore?.Detach();

            // The dynamic count reached the raise through a compiler spill local
            // loaded twice (the localloc byte size and this ctor length). Dropping
            // the byte-size load above just left that spill single-use, so recover
            // it now that the tree reflects the single remaining load.
            TryInlineCountSpill(function, raised, context);
        }
    }

    /// <summary>
    /// Folds the compiler's element-count spill left standing by the raise back
    /// into the <c>stackalloc T[n]</c> count. A dynamic <c>stackalloc T[n]</c>
    /// lowers the count into a local read twice — once in the <c>localloc</c>
    /// byte size, once as the <c>Span&lt;T&gt;</c> constructor length — so the
    /// early full inliner sees a multi-use local and the late inliner is
    /// stack-slot-only; after this pass discards the byte-size read the surviving
    /// single-use spill would otherwise print as <c>int V = n; ... stackalloc
    /// T[V]</c> instead of <c>stackalloc T[n]</c>.
    ///
    /// <para>Recovers only under the same ownership and evaluation-order proof the
    /// general inliner requires, so the folded expression evaluates at exactly the
    /// point — and under exactly the exception ordering — the spill did: the local
    /// is referenced nowhere but one block-level store and this one count load
    /// (no extra use/store, address-of, or designation binding), the store's
    /// declared type matches the stored value (no dropped widening/narrowing
    /// witness), the store is the count statement's immediate predecessor in the
    /// same block (no statement between them to observe or reorder past the moved
    /// allocation), and the count is that statement's first-evaluated leaf
    /// (evaluation order preserved verbatim — nothing runs before it, so moving the
    /// stored value into the count position crosses no effect). A pure stored value
    /// alone is deliberately not accepted for a non-first-leaf count: an earlier
    /// leaf can mutate a local the value reads, which would fold in a stale count.
    /// The count load is, by construction,
    /// the just-raised <see cref="StackAllocArray"/>'s own operand — never an
    /// increment lvalue — so this cannot mint an invalid <c>1++</c> the way an
    /// unrestricted late inline of a user local could.</para>
    /// </summary>
    static void TryInlineCountSpill(IrFunction function, StackAllocArray raised, PassContext context)
    {
        if (raised.Count is not LoadLocal load)
            return;

        // The reference scan counts by local index within the single method
        // body's index space; a nested (already-raised) function body has its
        // own numbering, so never fold a count sitting inside one.
        if (ReferenceOwnership.IsInsideNestedFunctionBody(load))
            return;

        int index = load.Index;
        StoreLocal? store = null;
        bool sawLoad = false;
        foreach (var node in GenericDeclarationPatternProof.DescendantsOutsideNestedFunctions(function))
        {
            if (!ReferenceOwnership.ReferencesOrBindsLocal(node, index))
                continue;
            if (ReferenceEquals(node, load))
                sawLoad = true;
            else if (node is StoreLocal { Parent: Block } blockStore && store is null)
                store = blockStore;
            else
                return; // a second use/store, an address-of, or a designation binding: not the sole owner
        }
        if (!sawLoad || store is null)
            return;

        // The spill declaration `T V = value` carries value's type; a mismatch
        // means the store performs a conversion the bare count position would drop.
        if (!store.Type.Equals(store.Value.ResultType))
            return;

        // Immediately-preceding, same-block store: no statement sits between the
        // spill and the count to be observed or reordered across the allocation.
        var loadStatement = GetStatement(load);
        if (loadStatement == null || loadStatement.Parent != store.Parent || loadStatement.ChildIndex != store.ChildIndex + 1)
            return;

        // The count must still be the statement's first-evaluated leaf, so nothing
        // runs before it and moving the stored value into the count position crosses
        // no effect. A pure stored value is NOT sufficient on its own: an earlier
        // leaf (e.g. `V0++` in `Consume(V0++, stackalloc byte[V1])` with `V1 = V0`)
        // can mutate a local the value reads, so folding it later would read a stale
        // count. Requiring the first-leaf position rules that reordering out entirely.
        if (!IsFirstEvaluatedLeaf(load, loadStatement))
            return;

        var value = (IrExpression)store.DetachChildren()[0];
        context.Stepper.StepOver("inline dynamic stackalloc count spill", load);
        store.Detach();
        load.ReplaceWith(value);
    }

    /// <summary>True when the node is the first thing the statement evaluates: the spine of first children.</summary>
    static bool IsFirstEvaluatedLeaf(IrNode node, IrNode statement)
    {
        var current = statement;
        while (current.Children.Count > 0)
        {
            current = current.Children[0];
            if (ReferenceEquals(current, node))
                return true;
        }
        return false;
    }

    /// <summary>
    /// Resolves a Span constructor's pointer argument through a compiler-owned
    /// stack slot indirection. Only raises when the slot is exclusively owned by
    /// this one store/load pair — no other store or load of the slot anywhere in
    /// the function — and the load's statement is the store's immediate
    /// successor in the same block, so detaching the store cannot move or
    /// discard the allocation from under some other reader or writer, and no
    /// other statement can sit between them to observe or reorder past the
    /// allocation.
    /// </summary>
    static bool TryResolveOwnedSlotSource(
        NewObject newObject,
        IrExpression pointer,
        IrExpression count,
        TypeRef element,
        Dictionary<int, List<StoreStackSlot>> storesBySlot,
        Dictionary<int, List<LoadStackSlot>> loadsBySlot,
        out IrExpression source,
        out StoreStackSlot? ownedStore)
    {
        source = null!;
        ownedStore = null;

        LoadStackSlot? load = pointer as LoadStackSlot;
        if (load == null && pointer is Convert { IsChecked: false, Operand: LoadStackSlot loadOperand, Target: { } target } && IsPointerLikeTarget(target))
            load = loadOperand;
        if (load == null)
            return false;

        if (!storesBySlot.TryGetValue(load.Slot, out var stores) || stores.Count != 1)
            return false;
        if (!loadsBySlot.TryGetValue(load.Slot, out var loads) || loads.Count != 1 || loads[0] != load)
            return false;

        var store = stores[0];
        if (!TryNormalizeStackAllocSource(store.Value, count, element, out var normalized) || store.Parent is not Block parentBlock)
            return false;

        var loadStatement = GetStatement(load);
        if (loadStatement == null || loadStatement.Parent != parentBlock || loadStatement.ChildIndex != store.ChildIndex + 1)
            return false; // Escaped, reordered, or not adjacent to the store: some other statement could sit between them and observe or alter state before the load.
        var held = GetHeldExpression(loadStatement);
        if (held == null || !ReachesAsOnlyPrecedingEffect(held, newObject))
            return false; // The constructor call must be the statement's held expression itself, or an argument of a Call it sits in with every earlier-evaluated operand (receiver, earlier arguments) provably pure -- otherwise some other operand of the same statement (a ternary/coalesce/switch-expression branch, a short-circuited && / || operand, an earlier *effectful* call argument, ...) could be evaluated unconditionally-but-not-first, or only conditionally, relative to the moved allocation.

        source = normalized;
        ownedStore = store;
        return true;
    }

    /// <summary>
    /// The single expression a linear (single-evaluation) statement holds when
    /// the value being stored/returned/thrown is the *only* operand the
    /// statement evaluates, or null otherwise (a loop/switch/if header, any
    /// other control-construct GetStatement can return, or a statement shape
    /// -- an instance field/indirect/element store -- that evaluates another
    /// operand, such as the receiver/address/array-and-index, before its
    /// value). Requiring the constructor call be exactly this expression --
    /// not merely nested somewhere inside it -- proves nothing else in the
    /// statement evaluates before, instead of, or conditionally relative to
    /// the moved allocation.
    /// </summary>
    static IrExpression? GetHeldExpression(IrNode statement) => statement switch
    {
        ExpressionStatement s => s.Expression,
        Return s => s.Value,
        Throw s => s.Value,
        StoreLocal s => s.Value,
        StoreStackSlot s => s.Value,
        StoreField { HasInstance: false } s => s.Value, // a static field store has no receiver evaluated before Value
        StoreArgument s => s.Value,
        _ => null, // StoreIndirect (Address before Value), StoreElement (Array/Index before Value), and an instance StoreField (Instance before Value) each evaluate another operand first
    };

    /// <summary>
    /// Whether <paramref name="newObject"/> is <paramref name="expression"/>
    /// itself, or sits as one of a <see cref="Call"/>'s or <see
    /// cref="NewObject"/>'s arguments (which, per <see cref="Call.Arguments"/>
    /// / <see cref="NewObject.Arguments"/>, includes the receiver first for
    /// an instance call) with every argument evaluated *before* it in that
    /// call's or constructor's left-to-right evaluation order proven
    /// <see cref="IsSideEffectFree"/> -- recursively, so a nested call's or
    /// constructor's own preceding arguments are proven too. This is the
    /// common, safe shape for the constructor call being passed directly as
    /// a call argument (e.g. <c>Consume(new Span&lt;int&gt;(ptr, n))</c>) or
    /// as another constructor's argument (e.g. <c>new Consumer(new
    /// Span&lt;int&gt;(ptr, n))</c>) -- moving the allocation to sit in the
    /// constructor's own position changes nothing observable, since nothing
    /// effectful was evaluated ahead of it either before or after the raise.
    /// Any other shape (a ternary/coalesce/switch-expression branch, a
    /// short-circuited operand, an argument evaluated strictly after an
    /// unproven earlier one) is rejected: the raise could reorder an
    /// effectful operand across the moved allocation, or move the allocation
    /// into a conditionally-evaluated branch.
    /// </summary>
    static bool ReachesAsOnlyPrecedingEffect(IrExpression expression, NewObject newObject)
    {
        if (ReferenceEquals(expression, newObject))
            return true;

        var arguments = expression switch
        {
            Call call => call.Arguments,
            NewObject outerNewObject => outerNewObject.Arguments,
            _ => null,
        };
        if (arguments is not null)
        {
            foreach (var argument in arguments)
            {
                if (ReachesAsOnlyPrecedingEffect(argument, newObject))
                    return true;
                if (!IsSideEffectFree(argument))
                    return false; // An earlier operand with an unproven effect: reordering it past the moved allocation is not safe.
            }
        }

        return false;
    }


    /// <summary>
    /// Deep structural equality over the same expression shapes
    /// <see cref="IsSideEffectFree"/> permits -- used to prove a dynamic
    /// (non-constant) <see cref="StackAllocArray.Count"/> and the
    /// constructor's own length argument describe the same quantity, since
    /// neither side being a literal rules out comparing by value.
    /// </summary>
    static bool StructurallyEqual(IrExpression a, IrExpression b) => (a, b) switch
    {
        (Constant x, Constant y) => Equals(x.Value, y.Value),
        (LoadLocal x, LoadLocal y) => x.Index == y.Index,
        (LoadArgument x, LoadArgument y) => x.Index == y.Index,
        (LoadStackSlot x, LoadStackSlot y) => x.Slot == y.Slot,
        (LoadLocalAddress x, LoadLocalAddress y) => x.Index == y.Index,
        (LoadArgumentAddress x, LoadArgumentAddress y) => x.Index == y.Index,
        (SizeOf x, SizeOf y) => x.Type.Equals(y.Type),
        (Unary x, Unary y) => x.Kind == y.Kind && StructurallyEqual(x.Operand, y.Operand),
        (Binary x, Binary y) => x.Kind == y.Kind && x.IsChecked == y.IsChecked && x.IsUnsigned == y.IsUnsigned
            && StructurallyEqual(x.Left, y.Left) && StructurallyEqual(x.Right, y.Right),
        (Convert x, Convert y) => x.Target.Equals(y.Target) && x.IsChecked == y.IsChecked && x.IsUnsigned == y.IsUnsigned
            && StructurallyEqual(x.Operand, y.Operand),
        _ => false,
    };

    /// <summary>
    /// Proves a raw <see cref="StackAllocate"/>'s byte size describes the
    /// same allocation as the constructor's <paramref name="count"/> element
    /// argument under <paramref name="element"/>'s size, so raising doesn't
    /// silently reserve a different number of bytes than the constructor's
    /// count implies.
    ///
    /// <para>Accepts two shapes. The real compiler shape for a dynamic
    /// <c>stackalloc T[n]</c> (confirmed against this repo's own compiled
    /// fixtures, not just a synthetic one) is a <b>checked, unsigned</b>
    /// multiply -- <c>Binary { Multiply, IsChecked: true, IsUnsigned: true }</c>
    /// -- of the count converted <c>int</c>-to-<c>nuint</c> (an explicit,
    /// not implicit, C# conversion, so peeled here rather than via the
    /// general-purpose <see cref="Unconvert"/>) and the element's byte size,
    /// which for primitive element types the compiler folds to a literal
    /// <see cref="Constant"/> rather than emitting a symbolic
    /// <see cref="SizeOf"/> node (struct/generic element types still emit
    /// <see cref="SizeOf"/>). Either operand order is accepted. For one-byte
    /// primitive elements, the compiler omits the multiply entirely and uses
    /// the element count directly as the byte count; that shape requires the
    /// two expressions to be structurally identical. Separately, when both
    /// the size and count are literal constants, a known fixed primitive byte
    /// size for <paramref name="element"/> and an exact arithmetic match is
    /// required.</para>
    ///
    /// <para>A dynamic count reaching this pass through a <see cref="StackAllocArray"/>
    /// source (the initializer/slot path) is unaffected by any of this: that
    /// count is already an element count, not a byte size, so no arithmetic
    /// proof is needed there.</para>
    /// </summary>
    static bool IsProvenByteSize(IrExpression size, IrExpression count, TypeRef element)
    {
        var unwrappedSize = Unconvert(size);
        var unwrappedCount = Unconvert(count);

        if (unwrappedCount is Constant { Value: int constantCount } && constantCount < 0)
            return false;

        if (GetSizeOf(element) == 1 && IsSameInt32Expression(unwrappedSize, unwrappedCount))
            return true;

        if (unwrappedSize is Binary { Kind: BinaryKind.Multiply, IsChecked: true, IsUnsigned: true } multiply
            && (IsCheckedByteSizeProduct(multiply.Left, multiply.Right, unwrappedCount, element)
                || IsCheckedByteSizeProduct(multiply.Right, multiply.Left, unwrappedCount, element)))
            return true;

        return unwrappedSize is Constant { Value: int sizeValue } && sizeValue >= 0
            && unwrappedCount is Constant { Value: int countValue } && countValue >= 0
            && GetSizeOf(element) is { } elementSize
            && (long)sizeValue == (long)countValue * elementSize; // checked-width product: an int-width product can wrap around and falsely match a truncated size
    }

    /// <summary>
    /// One operand order of the checked-multiply byte-size proof: <paramref
    /// name="countOperand"/> must be the compiler's exact unchecked, signed
    /// <c>int</c>-to-<c>nuint</c> conversion over <paramref name="count"/>,
    /// and
    /// <paramref name="elementSizeOperand"/> must be a known byte size for
    /// <paramref name="element"/> -- either a symbolic <see cref="SizeOf"/>
    /// node or, for primitives, a folded literal <see cref="Constant"/>.
    /// </summary>
    static bool IsCheckedByteSizeProduct(IrExpression countOperand, IrExpression elementSizeOperand, IrExpression count, TypeRef element)
    {
        bool isElementSize = (elementSizeOperand is SizeOf sizeOf && sizeOf.Type.Equals(element))
            || (elementSizeOperand is Constant { Value: int sizeValue } && GetSizeOf(element) == sizeValue);
        return isElementSize && IsCompilerNuintCount(countOperand, count);
    }

    /// <summary>
    /// Matches the exact conversion the compiler emits to widen a stackalloc
    /// element count before the checked byte-size multiply. Requiring corelib
    /// <c>System.UIntPtr</c>, an <c>int</c> operand, and the observed unchecked
    /// signed-conversion flags prevents lookalike or width-changing converts
    /// from being treated as the compiler's native-width count.
    /// </summary>
    static bool IsCompilerNuintCount(IrExpression expression, IrExpression count)
        => expression is Convert
        {
            Target: { } target,
            IsChecked: false,
            IsUnsigned: false,
            Operand: { } operand,
        }
        && MemberIdentity.IsCoreLibraryType(target, "System", "UIntPtr")
        && IsSameInt32Expression(operand, count);

    static bool IsSameInt32Expression(IrExpression left, IrExpression right)
        => MemberIdentity.IsCoreLibraryType(left.ResultType, "System", "Int32")
            && MemberIdentity.IsCoreLibraryType(right.ResultType, "System", "Int32")
            && StructurallyEqual(left, right);

    /// <summary>
    /// Strips only <see cref="Convert"/> wrappers proven value-preserving --
    /// C# implicit integer widening from the operand's own result type to the
    /// conversion's target -- so a narrowing or sign-changing conversion (e.g.
    /// <c>(byte)someLargerValue</c>) is never silently discarded: that would
    /// let two expressions that produce different values at runtime compare
    /// as structurally equal.
    /// </summary>
    static IrExpression Unconvert(IrExpression expression)
    {
        while (expression is Convert { IsChecked: false } convert
            && convert.Operand.ResultType is { } operandType
            && CSharpConversionRules.IsImplicitIntegerWidening(operandType, convert.Target))
        {
            expression = convert.Operand;
        }
        return expression;
    }

    static int? GetSizeOf(TypeRef type)
    {
        if (type.Kind == TypeRefKind.Definition && type.Assembly == TypeRef.CoreLibrary && type.Namespace == "System")
        {
            return type.Name switch
            {
                "Byte" or "SByte" or "Boolean" => 1,
                "Int16" or "UInt16" or "Char" => 2,
                "Int32" or "UInt32" or "Single" => 4,
                "Int64" or "UInt64" or "Double" => 8,
                _ => null,
            };
        }
        return null;
    }

    static IrNode? GetStatement(IrNode node)
    {
        while (node.Parent != null && node.Parent is not Block)
            node = node.Parent;
        return node.Parent is Block ? node : null;
    }

    static IEnumerable<IrExpression> GetInitializerElements(StackAllocArray source)
    {
        var elements = source.Elements.ToArray().Cast<IrExpression>().ToList();
        foreach (var e in elements) e.Detach();
        return elements;
    }

    /// <summary>
    /// Resolves a Span constructor's pointer argument to a stackalloc, whether
    /// direct or <c>Convert</c>-wrapped, unwrapping the wrapper to return the
    /// underlying <see cref="StackAllocate"/> or <see cref="StackAllocArray"/>
    /// node itself. Requires the discarded byte size (<see cref="StackAllocate.Size"/>)
    /// or element count (<see cref="StackAllocArray.Count"/>) to be provably
    /// safe to drop. A <see cref="StackAllocate"/> requires the compiler-shape
    /// equivalence proof in <see cref="IsProvenByteSize"/>; generic purity is
    /// insufficient because a pure but unrelated byte count would still
    /// describe a different allocation. A <see cref="StackAllocArray"/> count
    /// must be <see cref="IsSideEffectFree"/> and is checked separately for
    /// count agreement below.
    /// For <see cref="StackAllocArray"/> that agreement is instead checked
    /// separately below (by count or by <see cref="StructurallyEqual"/>).
    /// Returning the unwrapped node (rather than a possibly-<c>Convert</c>-
    /// wrapped <paramref name="pointer"/>) is required so later checks that
    /// pattern-match on <see cref="StackAllocArray"/> (element type,
    /// initializer count) see through the wrapper instead of silently skipping
    /// validation.
    /// </summary>
    static bool TryNormalizeStackAllocSource(IrExpression pointer, IrExpression count, TypeRef element, out IrExpression normalized)
    {
        var candidate = pointer;
        if (candidate is Convert { IsChecked: false, Operand: StackAllocate or StackAllocArray, Target: { } target } converted && IsPointerLikeTarget(target))
            candidate = converted.Operand;

        switch (candidate)
        {
            case StackAllocate { Size: { } size } when IsProvenByteSize(size, count, element):
            case StackAllocArray { Count: { } arrayCount } when IsSideEffectFree(arrayCount):
                normalized = candidate;
                return true;
            default:
                normalized = null!;
                return false;
        }
    }

    /// <summary>
    /// Whether evaluating the expression is non-observable, so discarding it
    /// (rather than reusing the constructor's own length argument) is sound.
    /// This is an allow-list, mirroring the same-named helpers in
    /// <see cref="RedundantBranchEliminationPass"/> and
    /// <see cref="IsPatternPass"/>: anything outside the proven-safe set is
    /// treated as possibly effectful.
    /// </summary>
    static bool IsSideEffectFree(IrExpression expression) => expression switch
    {
        Constant or LoadLocal or LoadArgument or LoadStackSlot
            or LoadLocalAddress or LoadArgumentAddress or SizeOf => true,
        Unary unary => IsSideEffectFree(unary.Operand),
        Binary { Kind: BinaryKind.Divide or BinaryKind.Remainder } => false,
        Binary { IsChecked: true } => false,
        Binary binary => IsSideEffectFree(binary.Left) && IsSideEffectFree(binary.Right),
        Convert { IsChecked: true } => false,
        Convert convert => IsSideEffectFree(convert.Operand),
        _ => false,
    };

    static bool IsPointerLikeTarget(TypeRef target)
        => target.Kind == TypeRefKind.Pointer
            || target is { Kind: TypeRefKind.Definition, Assembly: TypeRef.CoreLibrary, Namespace: "System", Name: "IntPtr" or "UIntPtr" };
}
