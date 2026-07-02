namespace ILInspector.Decompiler.Pipeline;

/// <summary>
/// The one enumeration of typed sinks and their semantic target types
/// (docs/design/value-typed-emission.md, capability 1: "target type on every
/// sink"). CoercionInsertionPass wraps through it and CoercionInvariant asserts
/// through it, so the two cannot disagree about what a sink is. Growth of the
/// sink set happens here, in one reviewed place. TypedConstantsPass keeps its
/// own traversal deliberately: it also covers non-sink positions (bitwise and
/// comparison operands) where identity recovery, not sink typing, is the
/// concern.
/// </summary>
public static class CoercionSinks
{
    public readonly record struct TypedSink(IrExpression Value, TypeRef Target);

    public static IEnumerable<TypedSink> Enumerate(IrFunction function)
    {
        foreach (var node in function.Descendants)
        {
            switch (node)
            {
                case Return { Value: { } value } when function.Signature.ReturnType is { } returnType:
                    yield return new(value, returnType);
                    break;
                case StoreLocal { Value: { } value, Type: { } type }:
                    yield return new(value, type);
                    break;
                case StoreArgument { Value: { } value, Type: { } type }:
                    yield return new(value, type);
                    break;
                case StoreField { Value: { } value, Field.Type: { } type }:
                    yield return new(value, type);
                    break;
                case StoreProperty { Value: { } value } store
                    when store.Accessor.ParameterTypes is { IsDefault: false, Length: > 0 } setter:
                    yield return new(value, setter[^1]);
                    break;
                case NullCoalescingAssignment { Value: { } value, LocalType: { } type }:
                    yield return new(value, type);
                    break;
                case NullCoalescingFieldAssignment { Value: { } value, Field.Type: { } type }:
                    yield return new(value, type);
                    break;
                case NullCoalescingPropertyAssignment { Value: { } value, PropertyType: { } type }:
                    yield return new(value, type);
                    break;
                case Box { Operand: { } operand, Type: { } type }:
                    yield return new(operand, type);
                    break;
                // The stelem opcode carries a storage width; the array's element
                // type is the semantic target (the printer's
                // StoreElementTargetType split, and TypedConstantsPass's).
                case StoreElement { Value: { } value } store:
                    var element = store.Array.ResultType is { Kind: TypeRefKind.SzArray or TypeRefKind.Array, ElementType: { } semantic }
                        ? semantic
                        : store.ElementType;
                    if (element is { } elementType)
                        yield return new(value, elementType);
                    break;
                // StoreIndirect is deliberately absent: the printer's target
                // derivation (IndirectStoreType) carries special cases this
                // enumeration would drift from; TypedConstantsPass still retypes
                // its constants. Residual for the burn-down.
                case Call call when !call.Callee.ParameterTypes.IsDefault:
                    for (int i = 0, offset = call.Callee.HasThis ? 1 : 0;
                        i < call.Callee.ParameterTypes.Length && i + offset < call.Arguments.Count; i++)
                    {
                        yield return new(call.Arguments[i + offset], call.Callee.ParameterTypes[i]);
                    }
                    break;
                case NewObject ctor when !ctor.Constructor.ParameterTypes.IsDefault:
                    for (int i = 0; i < ctor.Constructor.ParameterTypes.Length && i < ctor.Arguments.Count; i++)
                        yield return new(ctor.Arguments[i], ctor.Constructor.ParameterTypes[i]);
                    break;
                // An enum-typed join types both arms. Enum merges only: bool
                // joins belong to BooleanFoldingPass's 0/1 reconciliation, and
                // int-merge arm rendering (bool arms as `(cond ? 1 : 0)`) is
                // still a printer rule — residual for the burn-down.
                case Conditional { MergedType: { } merged } conditional
                    when function.TypeShapes.GetValueOrDefault(merged) == TypeShape.Enum:
                    yield return new(conditional.WhenTrue, merged);
                    yield return new(conditional.WhenFalse, merged);
                    break;
            }
        }
    }
}

/// <summary>
/// The one type-identity predicate gating the coercion invariant's exemption:
/// "provably already at the target type" is decided here, never per sink —
/// otherwise the scattered-partial-judgment problem reappears one level up as
/// per-sink identity checks with blind spots.
/// </summary>
public static class CoercionTyping
{
    /// <summary>
    /// The value-flow class the invariant owns: integer-family primitives,
    /// bool/char, and resolved enums. Reference and struct conversions stay
    /// outside until an inverse conversion-classifier exists; a cross-assembly
    /// <see cref="TypeShape.Unknown"/> definition cannot be proven an enum, so
    /// it is out of the checkable domain (the printer's structural constant
    /// rule still covers it at render time).
    /// </summary>
    public static bool InDomain(TypeRef target, IReadOnlyDictionary<TypeRef, TypeShape> shapes)
        => TypeFamilies.IsNumericPrimitive(target)
            || TypeFamilies.IsBoolean(target)
            || target is { Kind: TypeRefKind.Definition, Assembly: TypeRef.CoreLibrary, Namespace: "System", Name: "Char" }
            || shapes.GetValueOrDefault(target) == TypeShape.Enum;

    public static bool IsAtTarget(IrExpression value, TypeRef target)
        => value.ResultType is { } resultType && resultType.Equals(target);
}

/// <summary>
/// Wraps every in-domain typed-sink value that is not provably at its target in
/// a <see cref="Coerce"/> node (value-typed-emission.md, slice 3). Runs last:
/// the tree the printer receives is the decided tree. Output-neutral by
/// construction — sinks already render through CoerceText(value, target), and
/// the node renders through the same function with the same target.
/// </summary>
public sealed class CoercionInsertionPass : IIrPass
{
    public string Name => "coercion-insertion";

    public void Run(IrFunction function, PassContext context)
    {
        var shapes = function.TypeShapes;
        // Deepest-first: wrapping an outer sink clones its subtree, so a nested
        // sink recorded in the snapshot would otherwise edit the discarded
        // original (the same stale-match hazard BitwiseBoolOperandPass reverses
        // for).
        var sinks = CoercionSinks.Enumerate(function)
            .Where(s => s.Value is not Coerce
                && CoercionTyping.InDomain(s.Target, shapes)
                && !CoercionTyping.IsAtTarget(s.Value, s.Target))
            .OrderByDescending(s => Depth(s.Value))
            .ToList();
        foreach (var (value, target) in sinks)
        {
            context.Stepper.StepOver($"coerce sink value to {target.Name}", value);
            // Clone so the wrapper owns a detached copy before the in-place
            // replace swaps the original out of its slot.
            value.ReplaceWith(new Coerce(target, (IrExpression)value.Clone()));
        }
    }

    static int Depth(IrNode node)
    {
        int depth = 0;
        for (var parent = node.Parent; parent is not null; parent = parent.Parent)
            depth++;
        return depth;
    }
}

/// <summary>
/// The thin-writer well-formedness assertion: no value occupies an in-domain
/// typed sink except through a <see cref="Coerce"/> (or provably already at the
/// target type). Violations are returned, not thrown, so tests and gates fail
/// with the full list.
/// </summary>
public static class CoercionInvariant
{
    public static IReadOnlyList<string> Check(IrFunction function)
    {
        var shapes = function.TypeShapes;
        List<string>? violations = null;
        foreach (var (value, target) in CoercionSinks.Enumerate(function))
        {
            if (value is Coerce || !CoercionTyping.InDomain(target, shapes) || CoercionTyping.IsAtTarget(value, target))
                continue;
            (violations ??= []).Add(
                $"{function.Name}: {value.Describe()} occupies a {target.ToDisplayString()} sink without a Coerce");
        }
        return violations ?? [];
    }
}
