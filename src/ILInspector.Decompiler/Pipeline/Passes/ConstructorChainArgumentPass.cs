namespace ILInspector.Decompiler.Pipeline;

/// <summary>
/// Collapses a constructor-chain call's spilled argument temporaries into the
/// call so it lands as the body's first statement, where the printer lifts it
/// to a <c>: base(args)</c> / <c>: this(args)</c> signature initializer.
///
/// The compiler spills any base/this argument that carries control flow — the
/// ubiquitous <c>base(message ?? SR.Default)</c> exception shape — into a
/// temporary the general inliner then declines to fold: the chain receiver is
/// evaluated first, and <see cref="TypeRef"/> cannot yet prove it is a class
/// rather than a mutable byref struct, so it reads as an impure leaf the stored
/// value may not be reordered past. But the receiver of a constructor's own
/// base/this call is the object under construction — an immutable reference with
/// no observable evaluation effect — so moving an argument's computation past it
/// never reorders anything. This pass makes that one safe move
/// <see cref="ConstructorChainPass"/> set up: it inlines each single-use
/// argument temp stored in the run of statements immediately preceding the
/// chain call. Left in place the call prints as an invalid <c>base(temp);</c>
/// body statement (CS0175) that drops its argument on recompile.
/// </summary>
public sealed class ConstructorChainArgumentPass : IIrPass
{
    public string Name => "constructor-chain-argument";

    public void Run(IrFunction function, PassContext context)
    {
        if (!function.Signature.HasThis)
            return;

        // ConstructorChainPass has already canonicalized the receiver to `this`.
        if (FindChainCall(function) is not { } call
            || call.Parent is not ExpressionStatement statement
            || statement.Parent is not Block block)
        {
            return;
        }

        var usage = CountPlaces(function);

        // Inline the contiguous run of single-use argument spills immediately
        // preceding the call. Each inline detaches its store, so the call's
        // predecessor shifts down and the next iteration re-checks it.
        while (statement.ChildIndex > 0
            && TryInlineSpill(block, block.Children[statement.ChildIndex - 1], call, usage, context.Stepper))
        {
        }
    }

    static Call? FindChainCall(IrFunction function)
    {
        foreach (var node in function.Descendants)
        {
            if (node is Call { Callee: { Name: ".ctor", HasThis: true } } call
                && call.Arguments is [LoadArgument { Index: 0 }, ..])
            {
                return call;
            }
        }
        return null;
    }

    /// <summary>
    /// Inlines <paramref name="previous"/> into <paramref name="call"/> when it
    /// is the single store of a temporary loaded exactly once, inside the call,
    /// with its address never taken — and only when the move preserves effect
    /// order. Returns false (and changes nothing) for anything else.
    /// </summary>
    static bool TryInlineSpill(Block block, IrNode previous, Call call, Dictionary<(bool IsSlot, int Index), Place> usage, Stepper stepper)
    {
        (bool IsSlot, int Index)? key = previous switch
        {
            StoreLocal store => (false, store.Index),
            StoreStackSlot store => (true, store.Slot),
            _ => null,
        };
        if (key is not { } place
            || !usage.TryGetValue(place, out var record)
            || record.AddressTaken
            || record.Stores != 1
            || record.Loads.Count != 1)
        {
            return false;
        }

        var load = record.Loads[0];
        if (!ReferenceOwnership.IsInside(load, call))
            return false;

        var value = (IrExpression)previous.Children[0];
        if (HasObservableEffect(value) && !PreservesEffectOrder(block, previous, call, load, usage))
            return false;

        value = (IrExpression)previous.DetachChildren()[0];
        previous.Detach();
        stepper.StepOver("inline spilled base/this constructor argument", call);
        load.ReplaceWith(value);
        return true;
    }

    /// <summary>
    /// True when inlining <paramref name="previous"/>'s effectful value into the
    /// call cannot reorder effects. <paramref name="previous"/> is the store
    /// immediately before the call, so originally its effect runs after every
    /// other spill stored ahead of it and before every argument the call
    /// evaluates inline. Two hazards break that order once the value lands at its
    /// argument slot <c>p</c>:
    /// <list type="bullet">
    /// <item>an earlier spill in the contiguous run whose load sits to the right
    /// of <c>p</c> (it was stored first but would now evaluate after this value);</item>
    /// <item>an inline (non-spill) argument with its own effect to the left of
    /// <c>p</c> (it was evaluated after every store but would now run first).</item>
    /// </list>
    /// Declining leaves the chain call un-lifted rather than emit reordered C#.
    /// </summary>
    static bool PreservesEffectOrder(Block block, IrNode previous, Call call, IrNode load, Dictionary<(bool IsSlot, int Index), Place> usage)
    {
        var arguments = call.Arguments;
        int p = ArgumentIndexOf(arguments, load);
        if (p < 0)
            return false;

        // Hazard 1: an earlier spill in the contiguous run loaded right of p.
        for (int i = previous.ChildIndex - 1; i >= 0; i--)
        {
            var earlier = block.Children[i];
            if (SpillLoadInside(earlier, call, usage) is not { } earlierLoad)
                break;  // run ends; stores above the gap are not inlined here
            if (ArgumentIndexOf(arguments, earlierLoad) > p)
                return false;
        }

        // Hazard 2: an inline argument with an observable effect left of p.
        for (int i = 0; i < p; i++)
        {
            if (HasObservableEffect(arguments[i]))
                return false;
        }
        return true;
    }

    /// <summary>The single load of <paramref name="node"/> when it is a single-use
    /// spill store whose load sits inside <paramref name="call"/>; otherwise null.</summary>
    static IrNode? SpillLoadInside(IrNode node, Call call, Dictionary<(bool IsSlot, int Index), Place> usage)
    {
        (bool IsSlot, int Index)? key = node switch
        {
            StoreLocal store => (false, store.Index),
            StoreStackSlot store => (true, store.Slot),
            _ => null,
        };
        if (key is not { } place
            || !usage.TryGetValue(place, out var record)
            || record.AddressTaken
            || record.Stores != 1
            || record.Loads.Count != 1)
        {
            return null;
        }
        var load = record.Loads[0];
        return ReferenceOwnership.IsInside(load, call) ? load : null;
    }

    static int ArgumentIndexOf(IReadOnlyList<IrExpression> arguments, IrNode load)
    {
        for (int i = 0; i < arguments.Count; i++)
        {
            if (ReferenceOwnership.IsInside(load, arguments[i]))
                return i;
        }
        return -1;
    }

    static bool HasObservableEffect(IrNode node)
        => node.Descendants.Prepend(node).Any(static n => n is
            Call or CallIndirect or NewObject or DelegateCreation or StoreField or StoreProperty or StoreElement or Throw);

    static Dictionary<(bool IsSlot, int Index), Place> CountPlaces(IrFunction function)
    {
        var places = new Dictionary<(bool IsSlot, int Index), Place>();

        Place Entry(bool isSlot, int index)
        {
            if (!places.TryGetValue((isSlot, index), out var entry))
                places[(isSlot, index)] = entry = new Place();
            return entry;
        }

        foreach (var node in function.Descendants)
        {
            switch (node)
            {
                case LoadLocal load: Entry(false, load.Index).Loads.Add(load); break;
                case StoreLocal store: Entry(false, store.Index).Stores++; break;
                case LoadLocalAddress address: Entry(false, address.Index).AddressTaken = true; break;
                case LoadStackSlot load: Entry(true, load.Slot).Loads.Add(load); break;
                case StoreStackSlot store: Entry(true, store.Slot).Stores++; break;
            }
        }
        return places;
    }
    sealed class Place
    {
        public List<IrNode> Loads { get; } = [];
        public int Stores { get; set; }
        public bool AddressTaken { get; set; }
    }
}
