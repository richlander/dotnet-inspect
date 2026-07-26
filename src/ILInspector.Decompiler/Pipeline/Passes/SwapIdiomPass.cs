using System.Collections.Immutable;

namespace ILInspector.Decompiler.Pipeline;

/// <summary>
/// Raises the compiler's value-swap lowering — a single-def/single-use temp
/// carrying one place across the two cross-assignments that exchange two
/// simple lvalues:
/// <code>
/// S = p;      // save p
/// p = q;      // p := q
/// q = S;      // q := old p
/// </code>
/// into the recognized swap form <c>(q, p) = (p, q);</c>. Roslyn lowers the
/// tuple swap <c>(q, p) = (p, q)</c> to exactly this one-temp sequence (the
/// dup-slot save, then the two stores), so the raise is opcode-exact, not a
/// byte-divergent rewrite: it recompiles to the same IL the flat form came
/// from. Left flat, the surviving carrier renders with a synthetic
/// <c>S_{slot}</c> / hidden-local name (issue #3166).
///
/// Scoped to the safe, alias-free case: the two exchanged places are distinct
/// by-value parameters or locals of the same type (side-effect-free,
/// non-aliasing lvalues) that are legal ValueTuple elements, and the carrier is
/// a stack slot or an unnamed (compiler) local referenced only by the save and
/// the final restore. Field, element, indexer, pointer, function-pointer,
/// byref, generic-parameter, and ref-struct / stack-only places — which can
/// alias, carry side effects, reseat rather than assign, or are illegal as
/// tuple elements — keep their explicit three-statement spelling.
/// </summary>
public sealed class SwapIdiomPass : IIrPass
{
    public string Name => "swap-idiom";

    public void Run(IrFunction function, PassContext context)
    {
        foreach (var block in function.Descendants.OfType<Block>().ToList())
        {
            // Re-scan from the top after each raise: the three matched
            // statements collapse to one, so indices shift. Swaps are rare;
            // a restart keeps the index bookkeeping trivially correct.
            bool raisedAny;
            do
            {
                raisedAny = false;
                var children = block.Children;
                for (int i = 0; i + 2 < children.Count; i++)
                {
                    if (TryRaiseSwap(function, block, i, context))
                    {
                        raisedAny = true;
                        break;
                    }
                }
            }
            while (raisedAny);
        }
    }

    static bool TryRaiseSwap(IrFunction function, Block block, int i, PassContext context)
    {
        var children = block.Children;
        var save = children[i];
        var cross = children[i + 1];
        var restore = children[i + 2];

        // 1. save: `carrier = load(p)` — a stack slot or unnamed-local temp.
        if (MatchCarrier(function, save) is not { } carrier
            || MatchPlace(carrier.SavedValue) is not { } p)
        {
            return false;
        }

        // 2. cross: `p = load(q)` — assigns into the just-saved place p.
        if (MatchStore(cross) is not { } crossStore
            || !crossStore.Place.Equals(p)
            || MatchPlace(crossStore.Value) is not { } q)
        {
            return false;
        }

        // 3. restore: `q = carrier` — writes the saved old-p value into q.
        if (MatchStore(restore) is not { } restoreStore
            || !restoreStore.Place.Equals(q)
            || !carrier.Matches(restoreStore.Value))
        {
            return false;
        }

        // A swap exchanges two *distinct* places of the same type. Equal
        // places, or a type mismatch, are not the swap idiom.
        if (p.Equals(q) || !p.Type.Equals(q.Type))
            return false;

        // The exchanged places must be spellable, non-aliasing, by-value
        // lvalues that are legal ValueTuple elements. Byref (`ref`
        // parameters/locals — a byref reseat, not a value swap), pointers,
        // function pointers, and ref-struct / stack-only value types either
        // change meaning under tuple deconstruction (a `ref` reseat becomes a
        // value write to the referent) or cannot appear as a tuple element at
        // all (CS9244 / CS0306). Leave those in the flat three-statement form.
        if (!IsSwappablePlaceType(function, p.Type))
            return false;

        // The carrier must be a genuine single-def/single-use temp: referenced
        // only by this save and this restore. Any other read or write means it
        // is not a throwaway swap slot and the sequence is not a swap.
        if (!carrier.ReferencedOnlyWithin(function, save, restore))
            return false;

        // Emit `(q, p) = (p, q);`. The deconstruction evaluates the tuple
        // (old p, old q) then assigns left-to-right: q := old p, p := old q —
        // the swap. Targets list q first so the source lists p first, matching
        // the natural declaration-order reading of the original code.
        var tupleType = TypeRef.GenericInstance(
            TypeRef.CoreLib("System", "ValueTuple"), [p.Type, q.Type]);
        var source = new TupleExpression(tupleType, [p.Load(), q.Load()]);
        var deconstruction = new DeconstructionAssignment(
            [q.Target(), p.Target()], source);

        context.Stepper.StepOver("raise value-swap temp to tuple deconstruction", save);
        save.ReplaceWith(deconstruction);
        cross.Detach();
        restore.Detach();
        return true;
    }

    /// <summary>A distinct by-value parameter or local lvalue in a swap.</summary>
    abstract record Place(TypeRef Type)
    {
        public abstract IrExpression Load();
        public abstract DeconstructionTarget Target();
    }

    sealed record ArgumentPlace(int Index, string PlaceName, TypeRef Type) : Place(Type)
    {
        public override IrExpression Load() => new LoadArgument(Index, PlaceName, Type);
        public override DeconstructionTarget Target()
            => DeconstructionTarget.Argument(Index, PlaceName, Type);
    }

    sealed record LocalPlace(int Index, TypeRef Type) : Place(Type)
    {
        public override IrExpression Load() => new LoadLocal(Index, Type);
        public override DeconstructionTarget Target()
            => DeconstructionTarget.Local(Index, Type, isDeclared: false);
    }

    static Place? MatchPlace(IrExpression expression) => expression switch
    {
        LoadArgument argument => new ArgumentPlace(argument.Index, argument.Name, argument.Type),
        LoadLocal local => new LocalPlace(local.Index, local.Type),
        _ => null,
    };

    readonly record struct StoreMatch(Place Place, IrExpression Value);

    static StoreMatch? MatchStore(IrNode node) => node switch
    {
        StoreArgument argument => new StoreMatch(
            new ArgumentPlace(argument.Index, argument.Name, argument.Type), argument.Value),
        StoreLocal local => new StoreMatch(
            new LocalPlace(local.Index, local.Type), local.Value),
        _ => null,
    };

    /// <summary>The throwaway temp carrying the saved value across the swap.</summary>
    abstract record Carrier
    {
        public required IrExpression SavedValue { get; init; }

        /// <summary>True when <paramref name="value"/> reads this carrier.</summary>
        public abstract bool Matches(IrExpression value);

        /// <summary>True when the carrier is referenced only by <paramref name="save"/> and <paramref name="restore"/>.</summary>
        public bool ReferencedOnlyWithin(IrFunction function, IrNode save, IrNode restore)
        {
            foreach (var node in function.Descendants)
                if (IsReference(node)
                    && !ReferenceOwnership.IsInside(node, save)
                    && !ReferenceOwnership.IsInside(node, restore))
                {
                    return false;
                }
            return true;
        }

        protected abstract bool IsReference(IrNode node);
    }

    sealed record StackSlotCarrier(int Slot) : Carrier
    {
        public override bool Matches(IrExpression value)
            => value is LoadStackSlot load && load.Slot == Slot;

        protected override bool IsReference(IrNode node) => node switch
        {
            LoadStackSlot load => load.Slot == Slot,
            StoreStackSlot store => store.Slot == Slot,
            _ => false,
        };
    }

    sealed record HiddenLocalCarrier(int Index) : Carrier
    {
        public override bool Matches(IrExpression value)
            => value is LoadLocal load && load.Index == Index;

        protected override bool IsReference(IrNode node) => node switch
        {
            LoadLocal load => load.Index == Index,
            StoreLocal store => store.Index == Index,
            LoadLocalAddress address => address.Index == Index,
            _ => false,
        };
    }

    static Carrier? MatchCarrier(IrFunction function, IrNode node) => node switch
    {
        StoreStackSlot slot => new StackSlotCarrier(slot.Slot) { SavedValue = slot.Value },
        StoreLocal local when IsHiddenLocal(function, local.Index)
            => new HiddenLocalCarrier(local.Index) { SavedValue = local.Value },
        _ => null,
    };

    static bool IsHiddenLocal(IrFunction function, int index)
        => index >= 0
            && index < function.LocalNames.Length
            && function.LocalNames[index] is null;

    // A place type is swappable only when it is a spellable, boxable-or-plain
    // by-value type that is legal as a ValueTuple element. Mirrors the
    // ref-struct / stack-only reasoning in PatternSwitchExpressionPass
    // (IsStackOnlyValueType / IsByRefLike): those helpers live there privately,
    // so the swap pass keeps its own trimmed copies rather than raising invalid
    // or meaning-changing `Full` C#. Generic parameters are declined
    // conservatively: a `where T : allows ref struct` parameter can be a ref
    // struct at some instantiation, which is illegal as a tuple element
    // (CS9244), and that anti-constraint is not tracked in the IR.
    static bool IsSwappablePlaceType(IrFunction function, TypeRef type)
    {
        if (type.Kind is TypeRefKind.ByRef or TypeRefKind.Pointer
            or TypeRefKind.FunctionPointer or TypeRefKind.Pinned or TypeRefKind.Unsupported
            or TypeRefKind.GenericParameter or TypeRefKind.MethodGenericParameter)
        {
            return false;
        }
        return !IsStackOnlyValueType(type) && !IsByRefLike(function, type);
    }

    // The corelib stack-only value types (ref structs and byref-like intrinsics)
    // recognised by name, since their ref-struct nature lives on the
    // cross-assembly corelib definition rather than this TypeRef. Matching
    // `System` avoids user types of the same simple name; user-defined ref
    // structs in the inspected assembly are caught by IsByRefLike instead.
    static bool IsStackOnlyValueType(TypeRef type)
    {
        var (name, ns) = type.Kind is TypeRefKind.GenericInstance
            ? (type.ElementType?.Name, type.ElementType?.Namespace)
            : (type.Name, type.Namespace);
        if (ns != "System" || name is null)
            return false;
        int tick = name.IndexOf('`');
        string simple = tick < 0 ? name : name[..tick];
        return simple is "Span" or "ReadOnlySpan" or "TypedReference"
            or "ArgIterator" or "RuntimeArgumentHandle";
    }

    // Whether the type (or a generic instance's definition) carries the
    // compiler's [IsByRefLike] fact recovered at import — a user-defined
    // `ref struct` in the inspected assembly.
    static bool IsByRefLike(IrFunction function, TypeRef type)
    {
        var definition = type.Kind == TypeRefKind.GenericInstance ? type.ElementType : type;
        return definition is not null && function.ByRefLikeTypes.Contains(definition);
    }
}
