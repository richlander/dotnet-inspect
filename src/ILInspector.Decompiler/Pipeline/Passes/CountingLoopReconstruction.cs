using System.Collections.Generic;
using System.Linq;

namespace ILInspector.Decompiler.Pipeline;

/// <summary>
/// Reconstructs the single-yield <b>counting loop</b> iterator — the lowered form
/// of <c>for (T i = init; i &lt; bound; i += step) yield return f(i);</c> (and the
/// equivalent <c>while</c>) — back to a structured loop with a real loop local.
/// Unlike the linear-constant case (a clean <c>switch</c> chain), a looping
/// iterator's <c>MoveNext</c> is an <em>irreducible</em> goto-dispatch CFG: the
/// resume edge for state 1 jumps into the middle of the loop, so the generic
/// structurer cannot form a natural loop. This recogniser matches that exact
/// dispatch shape and synthesises the loop directly, mapping the state machine's
/// hoisted loop field (<c>&lt;i&gt;5__2</c>) to a fresh local and its hoisted
/// parameter fields back to the kickoff's parameters.
///
/// <para>Deliberately narrow (phase 1): exactly one loop, one yield, two states
/// (entry + resume), and a body self-contained modulo the loop variable. Anything
/// else — nested loops, multiple yields, captured locals, calls in the bound or
/// yielded value — fails the match and falls through to honest acknowledgment.</para>
/// </summary>
internal static class CountingLoopReconstruction
{
    public static bool TryReconstruct(IrFunction moveNext, IrFunction kickoff, NewObject handoff, out List<IrNode> statements)
    {
        statements = [];
        var blocks = moveNext.Body.Blocks;
        if (blocks.Count != 8)
            return false;  // the single counting-loop lowering is exactly eight blocks

        var byOffset = new Dictionary<int, Block>();
        foreach (var block in blocks)
            byOffset[block.StartOffset] = block;

        // ---- dispatch prologue: state local, then `state==0 -> init`, `state==1 -> resume`, else return false ----
        if (blocks[0].Children is not [
                StoreLocal { Value: LoadField { Field.Name: "<>1__state", Instance: LoadArgument { Index: 0 } } } stateStore,
                ConditionalBranch entry])
            return false;
        var stateLocal = stateStore.Index;
        if (!IsStateZero(entry.Condition, stateLocal))
            return false;
        var initOff = entry.TargetOffset;

        if (blocks[1].Children is not [ConditionalBranch resumeDispatch]
            || !IsStateEqual(resumeDispatch.Condition, stateLocal, 1))
            return false;
        var resumeOff = resumeDispatch.TargetOffset;

        if (!IsReturnFalse(blocks[2]))
            return false;

        // ---- init block: `state = -1; <loopvar> = init; goto cond;` ----
        if (!byOffset.TryGetValue(initOff, out var initBlock)
            || initBlock.Children is not [
                StoreField { Field.Name: "<>1__state" },
                StoreField { Instance: LoadArgument { Index: 0 }, Field: var loopField } initStore,
                Branch initBranch]
            || !GeneratedCodeIdentity.IsHoistedLocalFieldName(loopField.Name))
            return false;
        var initExpr = initStore.Value;
        var condOff = initBranch.TargetOffset;

        // ---- cond block: `if (<loopvar> < bound) goto yield;` falling through to a terminal `return false` ----
        if (!byOffset.TryGetValue(condOff, out var condBlock)
            || condBlock.Children is not [ConditionalBranch { Condition: Comparison comparison } condBranch]
            || comparison.Left is not LoadField { Instance: LoadArgument { Index: 0 }, Field.Name: var condLeft }
            || condLeft != loopField.Name)
            return false;
        var yieldOff = condBranch.TargetOffset;
        var condIndex = blocks.ToList().IndexOf(condBlock);
        if (condIndex + 1 >= blocks.Count || !IsReturnFalse(blocks[condIndex + 1]))
            return false;
        var terminalBlock = blocks[condIndex + 1];

        // ---- yield block: `<>2__current = value; state = 1; return true;` ----
        if (!byOffset.TryGetValue(yieldOff, out var yieldBlock)
            || yieldBlock.Children is not [
                StoreField { Field.Name: "<>2__current" } currentStore,
                StoreField { Field.Name: "<>1__state" },
                Return { Value: Constant { Value: true } }])
            return false;
        var yieldExpr = currentStore.Value;

        // ---- resume block: `state = -1; [temp = <loopvar>;] <loopvar> = <loopvar> +/- step;` falling through to cond ----
        if (!byOffset.TryGetValue(resumeOff, out var resumeBlock))
            return false;
        var resumeChildren = resumeBlock.Children;
        if (resumeChildren.Count < 2
            || resumeChildren[0] is not StoreField { Field.Name: "<>1__state" }
            || resumeChildren[^1] is not StoreField { Instance: LoadArgument { Index: 0 }, Field.Name: var incName, Value: Binary increment }
            || incName != loopField.Name
            || increment.Right is not Constant stepConstant)
            return false;
        if (!IncrementReadsLoopField(moveNext, resumeChildren, increment.Left, loopField.Name))
            return false;
        var resumeIndex = blocks.ToList().IndexOf(resumeBlock);
        if (resumeIndex + 1 >= blocks.Count || blocks[resumeIndex + 1] != condBlock)
            return false;

        // Every block must have a recognised role; an unexplained block means a
        // richer shape than this slice handles.
        var matched = new HashSet<Block> { blocks[0], blocks[1], blocks[2], initBlock, condBlock, terminalBlock, yieldBlock, resumeBlock };
        if (matched.Count != blocks.Count)
            return false;

        // ---- synthesise: validate remap with the predicted local index, then commit ----
        var loopType = loopField.Type;
        var local = kickoff.Locals.Length;
        if (Remap(initExpr, loopField.Name, local, loopType, kickoff) is not { } init
            || Remap(comparison.Right, loopField.Name, local, loopType, kickoff) is not { } bound
            || Remap(yieldExpr, loopField.Name, local, loopType, kickoff) is not { } element)
            return false;

        kickoff.AddLocal(loopType, ExtractSourceName(loopField.Name));

        var body = new Block(0);
        body.Add(new YieldReturn(element));
        body.Add(new StoreLocal(local, loopType,
            new Binary(increment.Kind, increment.IsChecked, increment.IsUnsigned,
                new LoadLocal(local, loopType), new Constant(stepConstant.Value, stepConstant.Type))));

        var loop = new WhileLoop(new Comparison(comparison.Kind, comparison.IsUnsigned, new LoadLocal(local, loopType), bound), body);
        var declaration = new StoreLocal(local, loopType, init);

        // Anchor to the kickoff's state-machine allocation, not a MoveNext offset
        // (those live in a different method's offset space), so the allocation fact
        // classified at import still lands on the reconstructed body.
        if (handoff.SourceOffset >= 0)
        {
            declaration.SetSourceOffset(handoff.SourceOffset);
            loop.SetSourceOffset(handoff.SourceOffset);
        }

        statements.Add(declaration);
        statements.Add(loop);
        return true;
    }

    static bool IncrementReadsLoopField(IrFunction moveNext, IReadOnlyList<IrNode> resumeChildren, IrExpression left, string loopFieldName)
    {
        if (IsLoopFieldLoad(left, loopFieldName))
            return resumeChildren.Count == 2;

        if (left is not LoadLocal temp
            || resumeChildren.Count != 3
            || resumeChildren[1] is not StoreLocal { Index: var storedTemp, Value: var value }
            || storedTemp != temp.Index
            || !IsLoopFieldLoad(value, loopFieldName))
        {
            return false;
        }

        return moveNext.Descendants.OfType<StoreLocal>().Count(store => store.Index == temp.Index) == 1
            && moveNext.Descendants.OfType<LoadLocal>().Count(load => load.Index == temp.Index) == 1;
    }

    static bool IsLoopFieldLoad(IrExpression expression, string loopFieldName)
        => expression is LoadField { Instance: LoadArgument { Index: 0 }, Field.Name: var name }
            && name == loopFieldName;

    // Rebuilds an expression in the kickoff's scope: the hoisted loop field becomes
    // the loop local, hoisted parameter fields become the kickoff's parameters, and
    // anything else (another field, a leaked `this`, a MoveNext temp, an unsupported
    // node) returns null — the self-containment guard for this slice.
    static IrExpression? Remap(IrExpression expr, string loopFieldName, int local, TypeRef loopType, IrFunction kickoff)
    {
        switch (expr)
        {
            case LoadField { Instance: LoadArgument { Index: 0 }, Field: var field }:
                if (field.Name == loopFieldName)
                    return new LoadLocal(local, loopType);
                if (TryGetParameter(kickoff, field.Name, out var index, out var parameter))
                    return new LoadArgument(index, parameter.Name, parameter.Type);
                return null;
            case Constant constant:
                return new Constant(constant.Value, constant.Type);
            case Comparison comparison:
                if (Remap(comparison.Left, loopFieldName, local, loopType, kickoff) is not { } left
                    || Remap(comparison.Right, loopFieldName, local, loopType, kickoff) is not { } right)
                    return null;
                return new Comparison(comparison.Kind, comparison.IsUnsigned, left, right);
            case Binary binary:
                if (Remap(binary.Left, loopFieldName, local, loopType, kickoff) is not { } bl
                    || Remap(binary.Right, loopFieldName, local, loopType, kickoff) is not { } br)
                    return null;
                return new Binary(binary.Kind, binary.IsChecked, binary.IsUnsigned, bl, br);
            default:
                return null;
        }
    }

    static bool TryGetParameter(IrFunction kickoff, string name, out int index, out Parameter parameter)
    {
        var parameters = kickoff.Signature.Parameters;
        var argumentBase = kickoff.Signature.HasThis ? 1 : 0;
        for (var i = 0; i < parameters.Length; i++)
            if (parameters[i].Name == name)
            {
                index = argumentBase + i;
                parameter = parameters[i];
                return true;
            }

        index = -1;
        parameter = null!;
        return false;
    }

    // `state == 0` — csc emits `brfalse` on the state local, which raises to a
    // logical-not; accept an explicit `== 0` comparison too.
    static bool IsStateZero(IrExpression condition, int stateLocal) => condition switch
    {
        LogicalNot { Operand: LoadLocal load } => load.Index == stateLocal,
        Comparison { Kind: ComparisonKind.Equal, Left: LoadLocal load, Right: Constant { Value: 0 } } => load.Index == stateLocal,
        _ => false,
    };

    static bool IsStateEqual(IrExpression condition, int stateLocal, int value)
        => condition is Comparison { Kind: ComparisonKind.Equal, Left: LoadLocal load, Right: Constant { Value: int v } }
            && load.Index == stateLocal && v == value;

    static bool IsReturnFalse(Block block)
        => block.Children is [Return { Value: Constant { Value: false } }];

    static string ExtractSourceName(string fieldName)
    {
        var close = fieldName.IndexOf('>');
        return close > 1 ? fieldName[1..close] : "i";
    }
}
