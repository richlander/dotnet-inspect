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

            if (TryNormalizeConstantStackAlloc(pointer, out var directSource))
            {
                source = directSource;
            }
            else if (TryResolveOwnedSlotSource(newObject, pointer, storesBySlot, loadsBySlot, out var slotSource, out ownedStore))
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
                        ? count is Constant { Value: int ctorCount } && ctorCount == sourceCount
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
        }
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
        if (!TryNormalizeConstantStackAlloc(store.Value, out var normalized) || store.Parent is not Block parentBlock)
            return false;

        var loadStatement = GetStatement(load);
        if (loadStatement == null || loadStatement.Parent != parentBlock || loadStatement.ChildIndex != store.ChildIndex + 1)
            return false; // Escaped, reordered, or not adjacent to the store: some other statement could sit between them and observe or alter state before the load.
        if (GetHeldExpression(loadStatement) != newObject)
            return false; // The constructor call must be the statement's entire expression, not merely reachable somewhere inside it -- otherwise some other subexpression of the same statement (an earlier call argument, a ternary/coalesce/switch-expression branch, a short-circuited && / || operand, ...) could be evaluated unconditionally-but-not-first, or only conditionally, relative to the moved allocation.

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
    /// count implies. Accepts either the canonical compiler shape --
    /// <paramref name="size"/> structurally is <c>count * sizeof(element)</c>
    /// (either operand order, seeing through unchecked <see cref="Convert"/>
    /// wrappers), which needs no numeric knowledge of the element's size and
    /// so works for any element type including generic/struct types -- or,
    /// when both sides are literal constants, requires a known fixed
    /// primitive byte size for <paramref name="element"/> and an exact
    /// arithmetic match.
    /// </summary>
    static bool IsProvenByteSize(IrExpression size, IrExpression count, TypeRef element)
    {
        var unwrappedSize = Unconvert(size);
        var unwrappedCount = Unconvert(count);

        if (unwrappedSize is Binary { Kind: BinaryKind.Multiply, IsChecked: false } multiply)
        {
            var left = Unconvert(multiply.Left);
            var right = Unconvert(multiply.Right);
            if (IsCountTimesElementSize(left, right, unwrappedCount, element)
                || IsCountTimesElementSize(right, left, unwrappedCount, element))
                return true;
        }

        return unwrappedSize is Constant { Value: int sizeValue }
            && unwrappedCount is Constant { Value: int countValue }
            && GetSizeOf(element) is { } elementSize
            && (long)sizeValue == (long)countValue * elementSize; // checked-width product: an int-width product can wrap around and falsely match a truncated size
    }

    static bool IsCountTimesElementSize(IrExpression countOperand, IrExpression sizeOfOperand, IrExpression count, TypeRef element)
        => sizeOfOperand is SizeOf sizeOf && sizeOf.Type.Equals(element) && StructurallyEqual(countOperand, count);

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
    /// node itself, and requires its byte size (<see cref="StackAllocate.Size"/>)
    /// or element count (<see cref="StackAllocArray.Count"/>) to be provably
    /// <see cref="IsSideEffectFree"/>. This pass always discards that size/count
    /// expression (the constructor's own <c>length</c> argument is used
    /// instead) — detaching and dropping an expression with an unproven side
    /// effect would silently erase it. A plain dynamic size (a local/argument
    /// read, or arithmetic over one, e.g. <c>stackalloc byte[n]</c>) is common
    /// and provably pure, so this does not require a literal constant.
    /// Returning the unwrapped node (rather than a possibly-<c>Convert</c>-
    /// wrapped <paramref name="pointer"/>) is required so later checks that
    /// pattern-match on <see cref="StackAllocArray"/> (element type,
    /// initializer count) see through the wrapper instead of silently skipping
    /// validation.
    /// </summary>
    static bool TryNormalizeConstantStackAlloc(IrExpression pointer, out IrExpression normalized)
    {
        var candidate = pointer;
        if (candidate is Convert { IsChecked: false, Operand: StackAllocate or StackAllocArray, Target: { } target } converted && IsPointerLikeTarget(target))
            candidate = converted.Operand;

        switch (candidate)
        {
            case StackAllocate { Size: { } size } when IsSideEffectFree(size):
            case StackAllocArray { Count: { } count } when IsSideEffectFree(count):
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
