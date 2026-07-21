namespace ILInspector.Decompiler.Pipeline;

/// <summary>
/// Re-composes a spilled fluent call chain. The compiler breaks a chain such as
/// <c>Root.A(x).B(y).C(z)</c> into a run of single-use scratch temps whenever a
/// link carries an effect the general inliner will not move:
///
/// <code>
/// AssertionChain S_256 = CurrentAssertionChain;
/// AssertionChain S_258 = S_256.ForCondition(cond).BecauseOf(because, args);
/// object[] S_257 = new object[] { subject };
/// S_258.FailWith("...", S_257);
/// </code>
///
/// <see cref="ExpressionInliningPass"/> only moves values with no observable
/// effect, so a chain whose links are property getters or ordinary calls stays
/// spilled — each temp read as an "impure leaf" the stored value may not cross.
/// But when the temp is the <em>receiver</em> of the next chained call, the
/// receiver is that call's first-evaluated operand: folding the spill back into
/// the receiver position reorders nothing, exactly the safe move
/// <see cref="ConstructorChainArgumentPass"/> makes for a constructor's own
/// base/this chain call. This pass makes the same move for a fluent sink — an
/// instance <see cref="Call"/> whose receiver spine bottoms out in a single-use
/// spilled temp whose value is itself a chain link — folding the maximal run of
/// single-use spills immediately preceding it (the receiver temp and any trailing
/// argument temps) back into the call, all-or-nothing, only when
/// <see cref="SpilledReceiverFold.RunPreservesEffectOrder"/> proves the fold
/// reorders no effect. It runs to a fixpoint so a chain broken across several
/// temps collapses link by link.
///
/// The sink is deliberately typed to an instance call on a spilled chain receiver
/// — not an arbitrary statement — so the fold never reaches a lock receiver copy,
/// a char/element store's slot, a <c>calli</c> pointer, or a fixed-buffer /
/// pinning ladder temp, whose distinct named locals other passes depend on.
/// </summary>
public sealed class FluentChainRecompositionPass : IIrPass
{
    public string Name => "fluent-chain-recomposition";

    public void Run(IrFunction function, PassContext context)
    {
        // Fold to a fixpoint: each successful fold removes at least one spill
        // store, so the loop strictly shrinks the function and terminates. Usage
        // is recomputed each round because a fold moves loads into the sink.
        while (FoldOne(function, context))
        {
        }
    }

    static bool FoldOne(IrFunction function, PassContext context)
    {
        var usage = SpilledReceiverFold.CountPlaces(function);
        foreach (var node in function.Descendants)
        {
            if (SinkStatement(node) is not { } statement
                || RootCall(node) is not { } sink
                || !ReceiverSpineBottomsAtSpilledChainLink(sink, usage))
            {
                continue;
            }
            if (SpilledReceiverFold.TryFold(statement, sink, usage, context, "re-compose spilled fluent chain link"))
                return true;
        }
        return false;
    }

    /// <summary>
    /// The statement whose root value is <paramref name="node"/> when it is a
    /// block-level fluent-call statement — an expression statement, or a single
    /// store of a call value — else null. These are the sinks a spilled chain
    /// lands in; a <c>lock</c>, element store, or pin is never one.
    /// </summary>
    static IrNode? SinkStatement(IrNode node) => node switch
    {
        ExpressionStatement { Expression: Call, Parent: Block } statement => statement,
        StoreStackSlot { Value: Call, Parent: Block } store => store,
        StoreLocal { Value: Call, Parent: Block } store => store,
        _ => null,
    };

    /// <summary>The root call value of a sink statement identified by <see cref="SinkStatement"/>.</summary>
    static Call? RootCall(IrNode node) => node switch
    {
        ExpressionStatement { Expression: Call call } => call,
        StoreStackSlot { Value: Call call } => call,
        StoreLocal { Value: Call call } => call,
        _ => null,
    };

    /// <summary>
    /// True when following <paramref name="sink"/>'s receiver spine — the
    /// <c>arguments[0]</c> of each instance call and the instance of each property
    /// load — bottoms out in the single load of a single-use spill whose stored
    /// value is itself a chain link (a call or property load). That spilled,
    /// effect-carrying head is what marks the statement as a re-composable fluent
    /// chain rather than an ordinary call on a live local or argument.
    /// </summary>
    static bool ReceiverSpineBottomsAtSpilledChainLink(
        Call sink,
        IReadOnlyDictionary<(bool IsSlot, int Index), SpilledReceiverFold.Place> usage)
    {
        IrExpression current = sink;
        while (true)
        {
            switch (current)
            {
                case Call { Callee.HasThis: true } call when call.Arguments.Count >= 1:
                    current = call.Arguments[0];
                    continue;
                case LoadProperty { HasInstance: true } property:
                    current = property.Instance!;
                    continue;
                case LoadStackSlot slot:
                    return IsSpilledChainLink((true, slot.Slot), usage);
                case LoadLocal local:
                    return IsSpilledChainLink((false, local.Index), usage);
                default:
                    return false;
            }
        }
    }

    static bool IsSpilledChainLink(
        (bool IsSlot, int Index) place,
        IReadOnlyDictionary<(bool IsSlot, int Index), SpilledReceiverFold.Place> usage)
    {
        if (!usage.TryGetValue(place, out var record)
            || record.AddressTaken
            || record.Stores != 1
            || record.Loads.Count != 1)
        {
            return false;
        }
        // The single load's defining store is the statement whose value we would
        // fold; require that value to be a chain link so the discriminator matches
        // the issue's "a call/property load on a chained receiver".
        var load = record.Loads[0];
        return DefiningStoreValue(load) is Call or LoadProperty;
    }

    /// <summary>The value expression of the store that defines the slot/local
    /// <paramref name="load"/> reads, found by walking its block; null when the
    /// definition is not a single preceding store in the same block.</summary>
    static IrExpression? DefiningStoreValue(IrNode load)
    {
        int slot = load switch { LoadStackSlot s => s.Slot, _ => -1 };
        int local = load switch { LoadLocal l => l.Index, _ => -1 };
        for (var current = load.Parent; current is not null; current = current.Parent)
        {
            if (current.Parent is not Block block)
                continue;
            for (int i = current.ChildIndex - 1; i >= 0; i--)
            {
                switch (block.Children[i])
                {
                    case StoreStackSlot store when store.Slot == slot:
                        return store.Value;
                    case StoreLocal store when store.Index == local:
                        return store.Value;
                }
            }
        }
        return null;
    }
}
