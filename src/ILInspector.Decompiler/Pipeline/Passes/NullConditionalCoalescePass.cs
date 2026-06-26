using System.Collections.Generic;
using System.Linq;

namespace ILInspector.Decompiler.Pipeline;

/// <summary>
/// Raises the generic null-conditional invocation <c>recv?.M() ?? fallback</c>
/// (and the bare <c>recv?.M()</c> when the fallback is literal null), before
/// structuring. When the receiver is of an unconstrained generic type, csc cannot
/// know at compile time whether it is a reference or value type, so it emits a
/// two-stage null test with a reload between, all targeting one shared
/// member-access arm — a diamond the <see cref="StructuringPass"/> cannot nest
/// (the <c>forward-branch-not-region-exit</c> stop, issue #911; the shape
/// <see cref="OrChainDiamondPass"/> deliberately left flat because the inner guard
/// carries a reload prologue). The lowering of <c>field?.GetHashCode() ?? 0</c>
/// over a generic field is:
/// <code>
///   A: Vk = default(Tk);  Sj = &amp;recv;   if ((object)Vk != null) goto TRUE
///   B: Vk = *Sj;          Sj = &amp;Vk;     if ((object)Vk != null) goto TRUE
///   FALSE: Sr = fallback;  goto MERGE
///   TRUE:  Sr = Sj->M()
///   MERGE: … uses Sr …
/// </code>
/// The first test boxes <c>default(Tk)</c> (non-null exactly when <c>Tk</c> is a
/// value type, so value types skip straight to the call); the second reloads the
/// real value and tests it (the reference-type path). Both paths call <c>M</c> on
/// the receiver, and the false arm supplies the <c>?? fallback</c>. This pass
/// collapses the four blocks into a single <c>Sr = recv?.M() ?? fallback</c> store
/// — a straight line the structuring pass then consumes — which re-lowers to the
/// same two-stage test (the round-trip invariant verified against
/// <c>ValueTuple&lt;…&gt;</c> / <c>HashCode.Combine</c>).
///
/// Conservative and sound: the two guards must share one true arm and one
/// generic-default temp, the receiver address must be side-effect free (so
/// evaluating it once in <c>recv?.M()</c> matches the lowering's single spill),
/// the temp and receiver slot must be dead outside the matched blocks, and nothing
/// outside may branch into the three blocks the fold drops.
/// </summary>
public sealed class NullConditionalCoalescePass : IIrPass
{
    public string Name => "null-conditional-coalesce";

    public void Run(IrFunction function, PassContext context)
    {
        while (FoldOne(function, context.Stepper))
        {
        }
    }

    sealed record Shape(
        int FirstGuard,
        int TrueArm,
        int MergeIndex,
        int ResultSlot,
        IrExpression Receiver,
        Call Call,
        IrExpression Fallback,
        int TempLocal,
        int ReceiverSlot);

    static bool FoldOne(IrFunction function, Stepper stepper)
    {
        var leaveTargets = function.Descendants.OfType<Leave>()
            .Select(leave => leave.TargetOffset)
            .ToHashSet();
        foreach (var container in function.Descendants.OfType<BlockContainer>().ToList())
        {
            var blocks = container.Blocks;
            var offsetToIndex = new Dictionary<int, int>();
            for (int i = 0; i < blocks.Count; i++)
                offsetToIndex[blocks[i].StartOffset] = i;

            for (int a = 0; a + 4 < blocks.Count + 1 && a + 3 < blocks.Count; a++)
            {
                if (Match(blocks, offsetToIndex, a) is { } shape
                    && DeadOutside(function, shape, blocks, a)
                    && NoExternalEntry(blocks, a, shape.TrueArm, offsetToIndex, leaveTargets))
                {
                    Fold(container, blocks, a, shape, stepper);
                    return true;
                }
            }
        }
        return false;
    }

    /// <summary>
    /// Matches the four-block generic null-conditional diamond rooted at block
    /// <paramref name="a"/> (default-box guard, reload guard, false arm, call arm),
    /// or null. The true arm is the block right after the false arm and the merge
    /// is the block right after the true arm.
    /// </summary>
    static Shape? Match(IReadOnlyList<Block> blocks, Dictionary<int, int> offsetToIndex, int a)
    {
        var blockA = blocks[a];
        var blockB = blocks[a + 1];
        var falseArm = blocks[a + 2];
        // A: initobj Tk(&Vk); Sj = &recv; if ((object)Vk != null) goto TRUE
        if (blockA.Children is not
            [
                InitObject { Type: var tkInit, Address: LoadLocalAddress { Index: var vkInit } },
                StoreStackSlot { Slot: var sjA, Value: var receiver },
                ConditionalBranch { TargetOffset: var trueOffA } condA,
            ]
            || !IsDefaultBoxTest(condA, vkInit, tkInit)
            || !IsPureAddress(receiver)
            || !CanBeGenericNullConditionalReceiver(tkInit))
        {
            return null;
        }
        // B: Vk = *Sj; Sj = &Vk; if ((object)Vk != null) goto TRUE
        if (blockB.Children is not
            [
                StoreLocal { Index: var vkB, Value: LoadIndirect { IsVolatile: false, Address: LoadStackSlot { Slot: var sjReadB } } },
                StoreStackSlot { Slot: var sjB, Value: LoadLocalAddress { Index: var vkAddrB } },
                ConditionalBranch { TargetOffset: var trueOffB } condB,
            ]
            || vkB != vkInit || vkAddrB != vkInit || sjReadB != sjA || sjB != sjA
            || trueOffB != trueOffA
            || !IsDefaultBoxTest(condB, vkInit, tkInit))
        {
            return null;
        }
        // FALSE: Sr = fallback; goto MERGE
        if (falseArm.Children is not
            [
                StoreStackSlot { Slot: var srFalse, Value: var fallback },
                Branch { TargetOffset: var mergeOff },
            ])
        {
            return null;
        }
        // TRUE sits right after the false arm and is the shared guard target.
        int trueArm = a + 3;
        if (blocks[trueArm].StartOffset != trueOffA)
            return null;
        // MERGE sits right after the true arm (the true arm falls through).
        if (!offsetToIndex.TryGetValue(mergeOff, out int mergeIndex) || mergeIndex != trueArm + 1)
            return null;
        // TRUE: Sr = Sj->M()  (one virtual call whose only argument is the slot).
        if (blocks[trueArm].Children is not
            [
                StoreStackSlot { Slot: var srTrue, Value: Call { IsVirtual: true, Arguments: [LoadStackSlot { Slot: var sjCall }] } call },
            ]
            || srTrue != srFalse || sjCall != sjA)
        {
            return null;
        }

        return new Shape(a, trueArm, mergeIndex, srTrue, receiver, call, fallback, vkInit, sjA);
    }

    /// <summary>A <c>brtrue</c> on <c>(object)Vk</c> — the boxed null test the lowering emits for both guards.</summary>
    static bool IsDefaultBoxTest(ConditionalBranch branch, int tempLocal, TypeRef tempType)
        => branch.Condition is Box { Type: var boxType, Operand: LoadLocal { Index: var index } }
            && index == tempLocal
            && boxType.Equals(tempType);

    static bool CanBeGenericNullConditionalReceiver(TypeRef type)
        => type.Kind is TypeRefKind.GenericParameter or TypeRefKind.MethodGenericParameter;

    /// <summary>
    /// The receiver address is evaluated once in <c>recv?.M()</c>, so it must
    /// re-evaluate without side effects: a chain of field/local/argument loads and
    /// address-of, never a call or other observable expression.
    /// </summary>
    static bool IsPureAddress(IrExpression expression) => expression switch
    {
        LoadArgument or LoadLocal or LoadArgumentAddress or LoadLocalAddress => true,
        LoadField field => field.Instance is null || IsPureAddress(field.Instance),
        LoadFieldAddress field => field.Instance is null || IsPureAddress(field.Instance),
        _ => false,
    };

    /// <summary>
    /// The generic-default temp and the receiver slot are folded away, so they
    /// must not be read or written anywhere outside the four matched blocks
    /// [a, a+3]. The result slot survives (it carries the value into the merge).
    /// </summary>
    static bool DeadOutside(IrFunction function, Shape shape, IReadOnlyList<Block> blocks, int a)
    {
        var matched = new HashSet<Block>(blocks.Skip(a).Take(4));
        foreach (var node in function.Descendants)
        {
            bool touchesTemp = node switch
            {
                LoadLocal load => load.Index == shape.TempLocal,
                StoreLocal store => store.Index == shape.TempLocal,
                LoadLocalAddress address => address.Index == shape.TempLocal,
                _ => false,
            };
            bool touchesSlot = node switch
            {
                LoadStackSlot load => load.Slot == shape.ReceiverSlot,
                StoreStackSlot store => store.Slot == shape.ReceiverSlot,
                _ => false,
            };
            if ((touchesTemp || touchesSlot) && !InMatchedBlock(node, matched))
                return false;
        }
        return true;
    }

    static bool InMatchedBlock(IrNode node, HashSet<Block> matched)
    {
        for (var current = node; current is not null; current = current.Parent)
            if (current is Block block && matched.Contains(block))
                return true;
        return false;
    }

    /// <summary>
    /// No block outside the four matched ones may branch into the reload guard,
    /// false arm, or true arm [a+1, a+3] — those vanish into the folded store. The
    /// entry (a) and the merge stay in place, so edges to them are fine; a leave
    /// into the span aborts the fold too.
    /// </summary>
    static bool NoExternalEntry(
        IReadOnlyList<Block> blocks, int a, int trueArm, Dictionary<int, int> offsetToIndex, HashSet<int> leaveTargets)
    {
        var forbidden = new HashSet<int>();
        for (int idx = a + 1; idx <= trueArm; idx++)
            forbidden.Add(blocks[idx].StartOffset);

        if (forbidden.Overlaps(leaveTargets))
            return false;

        for (int idx = 0; idx < blocks.Count; idx++)
        {
            if (idx >= a && idx <= trueArm)
                continue;   // inside the matched span — internal edges are fine
            foreach (var node in blocks[idx].Children)
                foreach (int target in Targets(node))
                    if (forbidden.Contains(target))
                        return false;
        }
        return true;
    }

    static IEnumerable<int> Targets(IrNode node) => node switch
    {
        Branch branch => [branch.TargetOffset],
        ConditionalBranch conditional => [conditional.TargetOffset],
        SwitchBranch sw => sw.TargetOffsets,
        _ => [],
    };

    static void Fold(BlockContainer container, IReadOnlyList<Block> blocks, int a, Shape shape, Stepper stepper)
    {
        var ordered = container.Blocks.ToList();

        // Rebuild the call with the receiver as its own receiver: recv?.M(). The
        // constrained-virtual prefix the generic receiver carries must survive, or
        // the call re-lowers to a different opcode. The receiver and fallback are
        // detached from their original blocks before those blocks drop.
        var receiver = shape.Receiver;
        receiver.Detach();
        var call = new Call(shape.Call.Callee, shape.Call.IsVirtual, [receiver])
        {
            ConstrainedTo = shape.Call.ConstrainedTo,
        };
        var fallback = shape.Fallback;
        fallback.Detach();

        // A literal-null fallback is just recv?.M() (the member result is a
        // reference type); any other fallback is the `?? fallback` coalesce.
        IrExpression value = fallback is Constant { Value: null }
            ? new NullConditional(call)
            : new Coalesce(new NullConditional(call), fallback);

        var folded = new Block(ordered[a].StartOffset);
        folded.Add(new StoreStackSlot(shape.ResultSlot, value));

        foreach (var block in ordered)
            block.Detach();

        var rebuilt = new BlockContainer();
        for (int idx = 0; idx < a; idx++)
            rebuilt.Add(ordered[idx]);
        rebuilt.Add(folded);
        for (int idx = shape.TrueArm + 1; idx < ordered.Count; idx++)
            rebuilt.Add(ordered[idx]);
        stepper.StepOver("fold generic null-conditional invocation", container);
        container.ReplaceWith(rebuilt);
    }
}
