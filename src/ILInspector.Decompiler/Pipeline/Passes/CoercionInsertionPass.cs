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
    /// <summary>
    /// Who decides the sink's coercion. <see cref="Wrappable"/> sinks are the
    /// insertion pass's to wrap; <see cref="PrinterOwned"/> sinks are rendered
    /// by CoerceText's own targeted branches (merge-node arms at non-enum
    /// joins) — the checker counts their mismatches as residuals so the
    /// invariant's scope limit is visible, never vacuous (#2145).
    /// </summary>
    public enum SinkScope { Wrappable, PrinterOwned }

    public readonly record struct TypedSink(IrExpression Value, TypeRef Target, SinkScope Scope = SinkScope.Wrappable);

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
        => sink.Scope == SinkScope.Wrappable
            && sink.Value is not (Coerce or LoadStackSlot or Conditional or Coalesce or SwitchExpression or UnionSwitchExpression)
            && CoercionDomain.InDomain(sink.Target, shapes)
            && !CoercionDomain.IsAtTarget(sink.Value, sink.Target);

    /// <summary>
    /// A sink the invariant cannot assert but must not hide: in-domain,
    /// mismatched, and owned by a later or targeted decider. Counted by
    /// CoercionInvariant so "0 violations" is never read as covering them.
    /// </summary>
    public static bool IsResidual(TypedSink sink, IReadOnlyDictionary<TypeRef, TypeShape> shapes)
        => !RequiresCoercion(sink, shapes)
            && sink.Value is not Coerce
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
        => Enumerate(function.Body, function.Signature.ReturnType, function,
            TestifiedSlotTypes(function.Body, function.Signature.ReturnType));

    /// <summary>Scope-bounded descendants: every node in this body, not crossing into nested lambda / local-function bodies (their scopes walk separately).</summary>
    public static IEnumerable<IrNode> ScopeNodes(IrNode scope)
    {
        foreach (var child in scope.Children)
        {
            yield return child;
            if (child is not (Lambda or LocalFunctionStatement))
            {
                foreach (var nested in ScopeNodes(child))
                    yield return nested;
            }
        }
    }

    /// <summary>
    /// Per slot (per body scope), the single type every load testifies to —
    /// the importer's recovered join type. A typed load contributes its type,
    /// an untyped load contributes its consuming sink's target type, and an
    /// untyped load with no derivable sink vetoes the slot; disagreement
    /// vetoes. The one slot-evidence rule (slice 5a review), shared by
    /// constant reconciliation (TypedConstantsPass) and slot-store sink
    /// enumeration (slice 5b).
    /// </summary>
    public static Dictionary<int, TypeRef> TestifiedSlotTypes(IrNode scope, TypeRef? returnType)
    {
        var types = new Dictionary<int, TypeRef>();
        var ambiguous = new HashSet<int>();
        foreach (var load in ScopeNodes(scope).OfType<LoadStackSlot>())
        {
            if (ambiguous.Contains(load.Slot))
                continue;
            var evidence = load.Type ?? LoadSinkTargetType(load, returnType);
            if (evidence is null)
            {
                types.Remove(load.Slot);
                ambiguous.Add(load.Slot);
                continue;
            }
            if (types.TryGetValue(load.Slot, out var existing))
            {
                if (!existing.Equals(evidence))
                {
                    types.Remove(load.Slot);
                    ambiguous.Add(load.Slot);
                }
            }
            else
            {
                types[load.Slot] = evidence;
            }
        }
        return types;
    }

    /// <summary>
    /// Both types resolve to the same known stack family — the precondition
    /// for a renderable, value-preserving coercion (CoerceText's same-family
    /// contract; slice 4's rule). An enum target contributes its underlying's
    /// family. Either family unknown → false: unknowable is not wrappable.
    /// </summary>
    static bool SameSemanticFamily(TypeRef? valueType, TypeRef target, IrFunction function)
    {
        // Symmetric enum unwrapping (round-2 review: the target-only version
        // rejected an enum store at an int-testified slot — a coercion
        // CoerceText renders fine as `(int)e` — and the denial split the slot
        // into a severed range with an unassigned read).
        var valueFamily = SemanticFamily(valueType, function);
        var targetFamily = SemanticFamily(target, function);
        if (valueFamily is null || targetFamily is null || valueFamily != targetFamily)
            return false;
        // I4 buckets bool with the integers, but the renderer is directional:
        // bool→integer-like has the `? 1 : 0` composition; integer→bool has no
        // spelling at all (NeedsNumericCast excludes boolean targets), and
        // bool→enum has none either. Booleanness must match, except the one
        // spellable direction (round-2 review, both reviewers independently).
        bool valueBool = TypeFamilies.IsBoolean(valueType);
        bool targetBool = TypeFamilies.IsBoolean(target);
        return valueBool == targetBool
            || (valueBool && TypeFamilies.IsIntegerLike(target));
    }

    /// <summary>A type's stack family, unwrapping resolved enums to their underlying's family (missing-`value__` assumes int, the MayOverflowEnumBackingType precedent).</summary>
    static StackFamily? SemanticFamily(TypeRef? type, IrFunction function)
        => TypeFamilies.Of(type)
            ?? (type is not null && function.TypeShapes.GetValueOrDefault(type) == TypeShape.Enum
                ? TypeFamilies.Of(function.EnumUnderlyingTypes.GetValueOrDefault(type)) ?? StackFamily.I4
                : null);

    /// <summary>The target type of the sink directly consuming an untyped slot load, where one is derivable — the printer's StackSlotLoadTargetType vocabulary.</summary>
    static TypeRef? LoadSinkTargetType(LoadStackSlot load, TypeRef? returnType)
        => load.Parent switch
        {
            StoreLocal store when ReferenceEquals(store.Value, load) => store.Type,
            StoreArgument store when ReferenceEquals(store.Value, load) => store.Type,
            StoreField store when ReferenceEquals(store.Value, load) => store.Field.Type,
            StoreIndirect store when ReferenceEquals(store.Value, load) => store.Type,
            Return ret when ReferenceEquals(ret.Value, load) => returnType,
            _ => null,
        };

    /// <summary>
    /// Walks one body scope. Nested bodies carry their own return types: a
    /// <see cref="LocalFunctionStatement"/> declares its, a <see cref="Lambda"/>
    /// does not expose one (the delegate's Invoke signature is not resolved
    /// here), so lambda returns are skipped rather than mis-attributed to the
    /// outer signature (review finding: a bool predicate return coerced to the
    /// outer method's int).
    /// </summary>
    static IEnumerable<TypedSink> Enumerate(IrNode scope, TypeRef? returnType, IrFunction function, Dictionary<int, TypeRef> slotTypes)
    {
        foreach (var child in scope.Children)
        {
            switch (child)
            {
                case Lambda lambda:
                    foreach (var nested in Enumerate(lambda, returnType: null, function, TestifiedSlotTypes(lambda, returnType: null)))
                        yield return nested;
                    continue;
                case LocalFunctionStatement local:
                    foreach (var nested in Enumerate(local, local.ReturnType, function, TestifiedSlotTypes(local, local.ReturnType)))
                        yield return nested;
                    continue;
            }
            switch (child)
            {
                case Return { Value: { } value } when returnType is { } scopeReturn:
                    yield return new(value, scopeReturn);
                    break;
                // A slot store is a typed sink once the slot's loads testify to
                // one type (slice 5b): the census's dominant shape is the
                // diamond whose arms store different types into a slot the join
                // reads at one type — 5a reconciled the constant arms; the
                // Coerce wrap covers the rest, and the printer's type-keyed
                // naming collapses to one declaration by construction.
                // Wrappable only within the testified type's stack family: a
                // cross-family store (a dead `long` store on an int-testified
                // slot) is not a coercion candidate but PROOF of a disjoint
                // live range — the verifier never merges across families, so
                // the testimony belongs to a different range. Those yield as
                // PrinterOwned: counted residuals (the #2209 burn-down), left
                // to the unifier's split naming until materialization
                // (slice-5b adversarial review, Gemini blocking finding).
                case StoreStackSlot { Value: { } slotValue } slotStore
                    when slotTypes.GetValueOrDefault(slotStore.Slot) is { } slotType:
                    yield return new(slotValue, slotType,
                        SameSemanticFamily(slotValue.ResultType, slotType, function)
                            ? SinkScope.Wrappable
                            : SinkScope.PrinterOwned);
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
                // A typed join types both arms. Enum merges are wrappable; other
                // in-domain merges (int joins with bool or enum arms) are
                // printer-owned — ConditionalArm renders them, and the checker
                // counts their mismatches as residuals rather than hiding them
                // behind the exclusion (#2145).
                case Conditional { MergedType: { } merged } conditional:
                    var armScope = function.TypeShapes.GetValueOrDefault(merged) == TypeShape.Enum
                        ? SinkScope.Wrappable
                        : SinkScope.PrinterOwned;
                    yield return new(conditional.WhenTrue, merged, armScope);
                    yield return new(conditional.WhenFalse, merged, armScope);
                    break;
                // Switch case labels are deliberately not enumerated:
                // SwitchSection holds them outside the child tree (ReplaceWith
                // cannot reach them) and the printer spells labels through
                // EnumConstantText.
            }
            foreach (var nested in Enumerate(child, returnType, function, slotTypes))
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
        foreach (var (value, target, _) in sinks)
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
    /// <summary>
    /// <see cref="Violations"/> break the invariant (gates assert zero);
    /// <see cref="Residuals"/> are in-domain mismatches the invariant's scope
    /// deliberately leaves to later or targeted deciders, counted by value
    /// kind so the scope limit is measurable burn-down, never silent (#2145).
    /// </summary>
    public readonly record struct CoercionViolation(IrNode Node, string Message);

    public readonly record struct CoercionAudit(
        IReadOnlyList<CoercionViolation> Violations,
        IReadOnlyDictionary<string, int> Residuals);

    public static IReadOnlyList<CoercionViolation> Check(IrFunction function) => Audit(function).Violations;

    public static CoercionAudit Audit(IrFunction function)
    {
        var shapes = function.TypeShapes;
        List<CoercionViolation>? violations = null;
        Dictionary<string, int>? residuals = null;
        foreach (var sink in CoercionSinks.Enumerate(function))
        {
            if (CoercionSinks.RequiresCoercion(sink, shapes))
            {
                (violations ??= []).Add(new(sink.Value,
                    $"{function.Name}: {sink.Value.Describe()} occupies a {sink.Target.ToDisplayString()} sink without a Coerce"));
            }
            else if (CoercionSinks.IsResidual(sink, shapes))
            {
                var kind = sink.Scope == CoercionSinks.SinkScope.PrinterOwned
                    ? $"printer-owned arm ({sink.Value.GetType().Name})"
                    : sink.Value.GetType().Name;
                residuals ??= [];
                residuals[kind] = residuals.GetValueOrDefault(kind) + 1;
            }
        }
        return new(violations ?? [], residuals ?? []);
    }
}
