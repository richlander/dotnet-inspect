using System.Collections.Immutable;

namespace ILInspector.Decompiler.Pipeline;

/// <summary>How a residual stack-slot web became locals.</summary>
public enum ResidualSlotBindingKind
{
    /// <summary>One candidate type satisfied every store, load, and element target; the whole web is one local.</summary>
    Unified,
    /// <summary>No candidate qualified; each rendered node type is its own local (<c>S_n</c>, <c>S_n_1</c>, …).</summary>
    Split,
}

/// <summary>
/// Typed provenance for one local that <see cref="ResidualSlotBindingPass"/>
/// issued: which slot it came from, how it was bound, and the materialization
/// vetoes the web carried at the pass position. Not display text; never
/// identity.
/// </summary>
public readonly record struct ResidualSlotBinding(
    int Slot,
    ResidualSlotBindingKind Kind,
    SlotMaterializationVeto Vetoes);

/// <summary>
/// Binds every stack-slot web that <see cref="SlotMaterializationPass"/>
/// declined to decided, typed locals before presentation
/// (value-typed-emission.md, "Residual storage binding").
/// <para>
/// This is the printer's legacy residual policy, moved here unchanged and
/// frozen: a bounded compatibility derivation that unifies the web under the
/// first candidate type every store can be assigned to and every load read as,
/// or, failing that, splits the web by rendered node type. Do not improve it.
/// Admission growth belongs to <see cref="SlotMaterializationPass"/> and its
/// testimony owners; this pass only shrinks as that work retires its
/// population. A managed-reference web and an untyped piece are visible
/// failures, never a new rule.
/// </para>
/// <para>
/// It runs immediately after <see cref="CoercionInsertionPass"/> so it sees
/// the tree the printer saw: slot-consuming raises done, join facts bound,
/// leftover slot stores already wrapped at their testified type. Because
/// insertion exempts slot loads and a bound local can retype its ancestors or
/// complete a sibling's checked-operand or element-store target, the pass ends
/// by re-running the shared insertion decision to a fixpoint so the coercion
/// invariant's gates keep asserting zero violations.
/// </para>
/// </summary>
public sealed class ResidualSlotBindingPass : IIrPass
{
    public string Name => "residual-slot-binding";

    public void Run(IrFunction function, PassContext context)
    {
        // Coercion insertion ran after the final join binding and may have
        // wrapped join arms; the policy below and, when no slot remains, the
        // printer read the arm facts as the arms are now.
        PrimitiveJoinTargetBinding.RefreshArmFacts(function);
        var nodes = CoercionSinks.ScopeNodes(function.Body).ToList();
        if (!nodes.Any(static node => node is StoreStackSlot or LoadStackSlot))
            return;

        RejectManagedReferenceWebs(function, nodes);

        var vetoes = SlotMaterializationPass.Analyze(function)
            .Where(decision => ReferenceEquals(decision.Scope, function))
            .ToDictionary(static decision => decision.Slot, static decision => decision.Vetoes);

        var policy = new ResidualSlotPolicy(function);
        var plan = policy.Plan(nodes);

        var invariant = IrInvariants.Enabled
            ? ResidualSlotBindingInvariant.Capture(function, plan)
            : null;

        Bind(function, context, plan, vetoes);

        invariant?.Check();

        Discharge(function, context);

        // Binding retyped slot-load arms and the discharge may have wrapped
        // them again: the printer reads the arm facts as the arms end here.
        PrimitiveJoinTargetBinding.RefreshArmFacts(function);
    }

    static void RejectManagedReferenceWebs(IrFunction function, IReadOnlyList<IrNode> nodes)
    {
        foreach (var node in nodes)
        {
            int? slot = node switch
            {
                StoreStackSlot { Value.ResultType.Kind: TypeRefKind.ByRef } store => store.Slot,
                LoadStackSlot { Type.Kind: TypeRefKind.ByRef } load => load.Slot,
                _ => null,
            };
            if (slot is { } residual)
            {
                throw new InvalidOperationException(
                    $"{function.Name}: managed-reference stack slot {residual} reached residual storage binding after slot materialization.");
            }
        }
    }

    static void Bind(
        IrFunction function,
        PassContext context,
        ResidualSlotPlan plan,
        IReadOnlyDictionary<int, SlotMaterializationVeto> vetoes)
    {
        var indices = new Dictionary<ResidualSlotPlan.PieceKey, int>();
        foreach (var piece in plan.Pieces)
        {
            if (piece.Type is null)
            {
                throw new InvalidOperationException(
                    $"{function.Name}: stack slot {piece.Key.Slot} has no decided type at residual storage binding ({piece.Name}).");
            }
            int index = function.AddSynthesizedLocal(piece.Type, piece.Name);
            indices[piece.Key] = index;
            // Byref-like value storage keeps its slot provenance exactly as
            // SlotMaterializationPass does: its legacy up-front declaration
            // order and scope remain part of the output contract.
            if (CSharpSpellability.CanSpellByRefLikeValueStorageType(piece.Type, function))
            {
                function.MarkMaterializedStackSlotLocal(
                    index,
                    piece.Key.Slot,
                    producerOnly: !plan.Loads.Any(load => load.Key == piece.Key));
            }
            function.RecordResidualSlotBinding(
                index,
                new ResidualSlotBinding(
                    piece.Key.Slot,
                    plan.Unified.ContainsKey(piece.Key.Slot)
                        ? ResidualSlotBindingKind.Unified
                        : ResidualSlotBindingKind.Split,
                    vetoes.GetValueOrDefault(piece.Key.Slot)));
        }

        // Replace loads before moving store values so nested slot loads have
        // already become locals, mirroring SlotMaterializationPass.
        foreach (var (load, key) in plan.Loads)
        {
            int index = indices[key];
            context.Stepper.StepOver($"bind residual slot {key.Slot} load as local {index}", load);
            load.ReplaceWith(new LoadLocal(index, function.Locals[index]));
        }
        foreach (var (store, key) in plan.Stores)
        {
            int index = indices[key];
            context.Stepper.StepOver($"bind residual slot {key.Slot} store as local {index}", store);
            var value = (IrExpression)store.DetachChildren()[0];
            store.ReplaceWith(new StoreLocal(index, function.Locals[index], value));
        }
    }

    /// <summary>
    /// Re-runs the shared insertion decision until a run adds no wrapper. Each
    /// run can only wrap a sink the previous run retyped, so the repetition is
    /// bounded by tree depth; exceeding it is a pass bug, not input data.
    /// </summary>
    static void Discharge(IrFunction function, PassContext context)
    {
        int bound = Depth(function) + 1;
        for (int run = 0; ; run++)
        {
            if (CoercionInsertionPass.Insert(function, context) == 0)
                return;
            if (run > bound)
            {
                throw new InvalidOperationException(
                    $"{function.Name}: residual storage binding's coercion discharge did not reach a fixpoint within tree depth {bound}.");
            }
        }
    }

    static int Depth(IrFunction function)
    {
        int deepest = 0;
        foreach (var node in function.Descendants)
        {
            int depth = 0;
            for (var parent = node.Parent; parent is not null; parent = parent.Parent)
                depth++;
            deepest = Math.Max(deepest, depth);
        }
        return deepest;
    }
}

/// <summary>The declared binding for one body scope: which slot nodes become which fresh locals.</summary>
sealed class ResidualSlotPlan
{
    public readonly record struct PieceKey(int Slot, string TypeKey);
    public readonly record struct Piece(PieceKey Key, TypeRef? Type, string Name);

    public List<Piece> Pieces { get; } = [];
    public List<(LoadStackSlot Load, PieceKey Key)> Loads { get; } = [];
    public List<(StoreStackSlot Store, PieceKey Key)> Stores { get; } = [];
    public Dictionary<int, TypeRef> Unified { get; } = [];
    public Dictionary<IrNode, PieceKey> KeyOf { get; } = new(ReferenceEqualityComparer.Instance);
}

/// <summary>
/// The printer's residual slot policy, ported verbatim from
/// <c>CSharpPrinter.CollectStackSlotNames</c> and
/// <c>TryChooseUnifiedStackSlotType</c>. Its inputs are closed: node result and
/// assignment types (including the Coerce wrappers insertion already placed),
/// StoreElement element types, conditional/coalesce/constant store shapes,
/// bound reference-arm and primitive-join facts, the body's return type for
/// the Boolean sink rule, and the function's type shapes and enum backing. It
/// reads no materialization testimony.
/// </summary>
sealed class ResidualSlotPolicy
{
    readonly IrFunction _function;
    readonly IReadOnlyDictionary<TypeRef, TypeShape> _shapes;

    public ResidualSlotPolicy(IrFunction function)
    {
        _function = function;
        _shapes = function.TypeShapes;
    }

    public ResidualSlotPlan Plan(IReadOnlyList<IrNode> nodes)
    {
        var storesBySlot = new Dictionary<int, List<IrExpression>>();
        var loadsBySlot = new Dictionary<int, List<LoadStackSlot>>();
        var extraLoadTargetsBySlot = new Dictionary<int, List<TypeRef>>();
        foreach (var node in nodes)
        {
            switch (node)
            {
                case StoreStackSlot store:
                    (storesBySlot.TryGetValue(store.Slot, out var stores) ? stores : storesBySlot[store.Slot] = []).Add(store.Value);
                    break;
                case LoadStackSlot load:
                    (loadsBySlot.TryGetValue(load.Slot, out var loads) ? loads : loadsBySlot[load.Slot] = []).Add(load);
                    break;
            }
        }

        foreach (var storeElement in nodes.OfType<StoreElement>())
        {
            if (storeElement is not { Value: LoadStackSlot load, ElementType: { } elementType })
                continue;
            if (!storesBySlot.TryGetValue(load.Slot, out var stores)
                || stores.Count == 0
                || !stores.All(store => store is Conditional conditional && conditional.CanRenderConditionalAt(elementType, _shapes)))
            {
                continue;
            }
            (extraLoadTargetsBySlot.TryGetValue(load.Slot, out var targets) ? targets : extraLoadTargetsBySlot[load.Slot] = []).Add(elementType);
        }

        var plan = new ResidualSlotPlan();
        foreach (int slot in storesBySlot.Keys.Concat(loadsBySlot.Keys).Distinct())
        {
            if (TryChooseUnifiedStackSlotType(
                storesBySlot.GetValueOrDefault(slot) ?? [],
                loadsBySlot.GetValueOrDefault(slot) ?? [],
                extraLoadTargetsBySlot.GetValueOrDefault(slot) ?? [],
                out var unifiedType))
            {
                plan.Unified[slot] = unifiedType;
            }
        }

        // The printer's split key: (slot, rendered type) by first occurrence in
        // tree order, loads and stores alike; the first distinct type is S_n and
        // each later one S_n_k.
        var ordinals = new Dictionary<int, int>();
        var seen = new HashSet<ResidualSlotPlan.PieceKey>();
        foreach (var node in nodes)
        {
            switch (node)
            {
                case LoadStackSlot load:
                {
                    var type = RenderType(plan, load.Slot, load.Type);
                    var key = Key(plan, seen, ordinals, load.Slot, type);
                    plan.Loads.Add((load, key));
                    plan.KeyOf[load] = key;
                    break;
                }
                case StoreStackSlot store:
                {
                    var type = RenderType(plan, store.Slot, store.Value.ResultType);
                    var key = Key(plan, seen, ordinals, store.Slot, type);
                    plan.Stores.Add((store, key));
                    plan.KeyOf[store] = key;
                    break;
                }
            }
        }
        return plan;
    }

    static ResidualSlotPlan.PieceKey Key(
        ResidualSlotPlan plan,
        HashSet<ResidualSlotPlan.PieceKey> seen,
        Dictionary<int, int> ordinals,
        int slot,
        TypeRef? type)
    {
        var key = new ResidualSlotPlan.PieceKey(slot, TypeKey(type));
        if (seen.Add(key))
        {
            int ordinal = ordinals.GetValueOrDefault(slot);
            ordinals[slot] = ordinal + 1;
            string name = ordinal == 0 ? $"S_{slot}" : $"S_{slot}_{ordinal}";
            plan.Pieces.Add(new(key, type, name));
        }
        return key;
    }

    static TypeRef? RenderType(ResidualSlotPlan plan, int slot, TypeRef? type)
        => plan.Unified.TryGetValue(slot, out var unified) ? unified : type;

    static string TypeKey(TypeRef? type) => type?.ToDisplayString() ?? "<unknown>";

    bool TryChooseUnifiedStackSlotType(
        IReadOnlyList<IrExpression> stores,
        IReadOnlyList<LoadStackSlot> loads,
        IReadOnlyList<TypeRef> extraLoadTargets,
        out TypeRef unifiedType)
    {
        var candidates = loads.Select(load => load.Type)
            .Concat(extraLoadTargets)
            .Concat(stores.Select(store => store.ResultType))
            .Where(type => type is not null)
            .Cast<TypeRef>()
            .Distinct()
            .ToList();
        foreach (var candidate in candidates)
        {
            if (stores.All(store => CanAssignTo(store, candidate))
                && loads.All(load => CanLoadAsType(candidate, load))
                && extraLoadTargets.All(target => CanAssignType(candidate, target) && !StrictlyNarrowsReference(candidate, target)))
            {
                unifiedType = candidate;
                return true;
            }
        }

        // The printer computed a System.Object here and discarded it; there is
        // no reference fallback, and this pass must not introduce one.
        unifiedType = null!;
        return false;
    }

    bool StrictlyNarrowsReference(TypeRef candidate, TypeRef load)
        => IsReferenceLike(candidate)
            && !candidate.Equals(load)
            && CanAssignForNarrowing(candidate, load)
            && !CanAssignForNarrowing(load, candidate);

    bool CanAssignForNarrowing(TypeRef source, TypeRef target)
        => CanAssignType(source, target)
            || IsCoreObject(target) && IsReferenceLike(source);

    bool CanLoadAsType(TypeRef source, LoadStackSlot load)
    {
        if (load.Type is null)
            return true;
        if (CanAssignType(source, load.Type))
            return !StrictlyNarrowsReference(source, load.Type);
        return TypeFamilies.IsBoolean(source)
            && CoercionSinks.BooleanSlotLoadType(load, _function.Signature.ReturnType, _shapes) is not null;
    }

    bool CanAssignTo(IrExpression value, TypeRef target)
    {
        if (value is Conditional conditional)
            return conditional.CanRenderValueJoinAt(target, _shapes)
                || (conditional.ResultType is { } condType && CanAssignType(condType, target));
        if (value is Coalesce)
            return false;
        if (value is Constant { Value: int or long } constant
            && target.DeclaredValueTypeHint == ValueTypeHint.ValueType
            && CoercionRendering.CanSpellUnknownEnumConstant(constant.ResultType, target, _shapes))
            return true;
        return value.AssignmentType is { } source && CanAssignType(source, target);
    }

    static bool CanAssignType(TypeRef source, TypeRef target)
    {
        if (source.Equals(target))
            return true;
        if (CSharpConversionRules.IsImplicitNumericAssignment(source, target))
            return true;
        return false;
    }

    bool IsReferenceLike(TypeRef type)
        => CoercionRendering.IsReferenceLike(type, _shapes);

    static bool IsCoreObject(TypeRef type)
        => type is { Kind: TypeRefKind.Definition, Assembly: TypeRef.CoreLibrary, Namespace: "System", Name: "Object" };
}
