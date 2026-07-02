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

    /// <summary>
    /// One shared decision for pass and checker: an enumerated sink requires a
    /// <see cref="Coerce"/> when its target is in the invariant's domain, the
    /// value is not provably at the target, and the value's type is not owned
    /// by a later decider — a <see cref="LoadStackSlot"/>'s C# type belongs to
    /// the printer's slot unifier until instance 2 materializes typed locals,
    /// and wrapping it breaks the unifier's structural pattern matches
    /// (review finding: phantom split locals).
    /// </summary>
    public static bool RequiresCoercion(TypedSink sink, IReadOnlyDictionary<TypeRef, TypeShape> shapes)
        // Merge nodes (conditional/coalesce/switch expressions) are excluded
        // like slot loads: CoerceText owns their target-aware rendering via its
        // own branches (TryConditionalTextForTarget and siblings), and wrapping
        // them re-routes statement-position spellings into the inline forms
        // (a multi-line switch expression collapsing to one line).
        => sink.Value is not (Coerce or LoadStackSlot or Conditional or Coalesce or SwitchExpression or UnionSwitchExpression)
            && CoercionDomain.InDomain(sink.Target, shapes)
            && !CoercionDomain.IsAtTarget(sink.Value, sink.Target);

    /// <summary>
    /// The one semantic target for an array-element store, shared by the
    /// printer's cast decision and this sink model: the stelem opcode carries a
    /// storage width (`stelem.i8` says Int64, not the long-backed enum), so the
    /// array's element type wins exactly when it is an enum-like definition —
    /// a named type with no primitive stack family that the shape map does not
    /// class as a reference or non-enum struct. TypedConstantsPass's ungated
    /// preference is a different question (identity recovery for bool/char,
    /// where the storage width is never the semantic type).
    /// </summary>
    public static TypeRef? StoreElementTarget(StoreElement store, IReadOnlyDictionary<TypeRef, TypeShape> shapes)
        => store.Array.ResultType is { Kind: TypeRefKind.SzArray or TypeRefKind.Array, ElementType: { } element }
            && element is { Kind: TypeRefKind.Definition }
            && TypeFamilies.Of(element) is null
            && shapes.GetValueOrDefault(element) is not (TypeShape.Reference or TypeShape.ValueType)
            ? element
            : store.ElementType;

    public static IEnumerable<TypedSink> Enumerate(IrFunction function)
        => Enumerate(function.Body, function.Signature.ReturnType, function);

    /// <summary>
    /// Walks one body scope. Nested bodies carry their own return types: a
    /// <see cref="LocalFunctionStatement"/> declares its, a <see cref="Lambda"/>
    /// does not expose one (the delegate's Invoke signature is not resolved
    /// here), so lambda returns are skipped rather than mis-attributed to the
    /// outer signature (review finding: a bool predicate return coerced to the
    /// outer method's int).
    /// </summary>
    static IEnumerable<TypedSink> Enumerate(IrNode scope, TypeRef? returnType, IrFunction function)
    {
        foreach (var child in scope.Children)
        {
            switch (child)
            {
                case Lambda lambda:
                    foreach (var nested in Enumerate(lambda, returnType: null, function))
                        yield return nested;
                    continue;
                case LocalFunctionStatement local:
                    foreach (var nested in Enumerate(local, local.ReturnType, function))
                        yield return nested;
                    continue;
            }
            switch (child)
            {
                case Return { Value: { } value } when returnType is { } scopeReturn:
                    yield return new(value, scopeReturn);
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
                // Box is deliberately absent: the unbox-over-box spelling
                // (`(T)(object)x`) renders the operand through ConvertText, not
                // CoerceText, and a bare constant under `(object)` boxes the
                // literal's own type regardless of the box token — the coercion
                // needs the explicit type spelling there. Residual for the
                // burn-down; the plain-Box printer branch still coerces via its
                // own CoerceText call.
                case StoreElement store when store.Value is { } value
                    && StoreElementTarget(store, function.TypeShapes) is { } elementType:
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
                // Switch case labels are deliberately not enumerated:
                // SwitchSection holds them outside the child tree (ReplaceWith
                // cannot reach them) and the printer spells labels through
                // EnumConstantText.
            }
            foreach (var nested in Enumerate(child, returnType, function))
                yield return nested;
        }
    }
}

/// <summary>
/// The invariant's declared domain and its one type-identity exemption
/// predicate. "Provably already at the target type" is decided here, never per
/// sink — otherwise the scattered-partial-judgment problem reappears one level
/// up as per-sink identity checks with blind spots.
/// </summary>
public static class CoercionDomain
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
        // Char and the native ints ride IsNumericPrimitive; only bool needs the
        // explicit disjunct (it is deliberately not a numeric primitive).
        => TypeFamilies.IsNumericPrimitive(target)
            || TypeFamilies.IsBoolean(target)
            || shapes.GetValueOrDefault(target) == TypeShape.Enum;

    public static bool IsAtTarget(IrExpression value, TypeRef target)
        => value.ResultType is { } resultType && resultType.Equals(target);
}

/// <summary>
/// Wraps every typed-sink value that requires coercion in a
/// <see cref="Coerce"/> node (value-typed-emission.md, slice 3). Runs last: the
/// tree the printer receives is the decided tree. Output-neutral for sinks that
/// render through CoerceText — the node renders through the same function with
/// the same target; the render-text corpus A/B is the empirical gate on that
/// claim.
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
            .Where(s => CoercionSinks.RequiresCoercion(s, shapes))
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
        foreach (var sink in CoercionSinks.Enumerate(function))
        {
            if (!CoercionSinks.RequiresCoercion(sink, shapes))
                continue;
            (violations ??= []).Add(
                $"{function.Name}: {sink.Value.Describe()} occupies a {sink.Target.ToDisplayString()} sink without a Coerce");
        }
        return violations ?? [];
    }
}
