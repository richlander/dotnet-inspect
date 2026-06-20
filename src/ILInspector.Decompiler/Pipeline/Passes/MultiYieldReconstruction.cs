using System;
using System.Collections.Generic;
using System.Linq;

namespace ILInspector.Decompiler.Pipeline;

/// <summary>
/// Reconstructs <b>structured multi-yield</b> iterators — the lowered form of a
/// <c>MoveNext</c> whose dispatch is a jump table (<c>switch (this.&lt;&gt;1__state)</c>)
/// rather than the linear-constant <c>switch</c> chain or the irreducible single-loop
/// goto-dispatch. csc emits the jump-table form whenever an iterator has more than one
/// yield point — conditional yields (<c>if (flag) yield return …;</c>) and several
/// yields in one loop body (<c>while (…) { yield return i; yield return -i; }</c>) — and
/// the generic structurer raises everything but the leading dispatch into ordinary
/// <c>if</c>/<c>while</c> nodes with the per-yield <em>seams</em> (<c>&lt;&gt;2__current = X;
/// &lt;&gt;1__state = k; return true;</c>) left inline.
///
/// <para>The reconstruction is a local rewrite of that already-structured body: strip the
/// state dispatch and the scaffolding (state stores, the <c>return true</c>/<c>return
/// false</c> markers), turn each <c>&lt;&gt;2__current = X</c> store into <c>yield return
/// X;</c>, and remap the state machine's hoisted fields back to the kickoff's locals
/// (<c>&lt;i&gt;5__2</c>) and parameters (<c>n</c>, <c>flag</c>). The structurer's own
/// <c>if</c>/<c>while</c> output carries the control flow, so the yield semantics it
/// represents are exactly the source's.</para>
///
/// <para>Sound by construction: any residual unstructured goto (a <see cref="Branch"/> /
/// <see cref="ConditionalBranch"/> the structurer could not consume), an unexpected
/// statement shape, or a hoisted field that resolves to neither a loop local nor a
/// parameter fails the match and falls through to honest acknowledgment. The irreducible
/// single-yield shapes (foreach-delegation, nested loops) keep their goto dispatch and so
/// never reach this rewrite.</para>
/// </summary>
internal static class MultiYieldReconstruction
{
    public static bool TryReconstruct(IrFunction moveNext, IrFunction kickoff, NewObject handoff, out List<IrNode> statements)
    {
        statements = [];

        // Predict the kickoff local layout without mutating it: the MoveNext locals are
        // appended first (its temps survive the transplant), then one fresh local per
        // distinct hoisted loop field. AddLocal is only called once the whole rewrite
        // succeeds, so a failed match leaves the kickoff untouched.
        var localOffset = kickoff.Locals.Length;
        var hoistedBase = localOffset + moveNext.Locals.Length;
        var hoisted = new Dictionary<string, (int Index, TypeRef Type)>(StringComparer.Ordinal);
        foreach (var node in moveNext.Descendants)
        {
            var field = node switch
            {
                StoreField { Instance: LoadArgument { Index: 0 }, Field: var f } => f,
                LoadField { Instance: LoadArgument { Index: 0 }, Field: var f } => f,
                _ => null,
            };
            if (field is null || !IsHoistedLocal(field.Name) || hoisted.ContainsKey(field.Name))
                continue;
            hoisted[field.Name] = (hoistedBase + hoisted.Count, field.Type);
        }

        var rewriter = new Rewriter(kickoff, localOffset, hoisted);
        if (!rewriter.TryRewrite(moveNext.Body.Blocks.SelectMany(b => b.Children), out var rebuilt))
            return false;
        if (!rewriter.DispatchDropped || rewriter.YieldCount == 0)
            return false;

        // Commit the predicted locals (order must match the prediction above).
        for (var i = 0; i < moveNext.Locals.Length; i++)
            kickoff.AddLocal(moveNext.Locals[i], NameOf(moveNext, i));
        foreach (var (name, slot) in hoisted.OrderBy(e => e.Value.Index))
            kickoff.AddLocal(slot.Type, ExtractSourceName(name));

        // The transplanted nodes carry MoveNext IL offsets; clear them and anchor each
        // top-level statement to the kickoff's state-machine allocation so the mixed
        // C#/IL view never mis-projects a foreign offset and the allocation fact still
        // lands on the reconstructed body.
        foreach (var statement in rebuilt)
            Reanchor(statement, handoff.SourceOffset);

        statements = rebuilt;
        return true;
    }

    static string? NameOf(IrFunction function, int index)
        => index < function.LocalNames.Length ? function.LocalNames[index] : null;

    static void Reanchor(IrNode node, int offset)
    {
        foreach (var descendant in node.Descendants)
            descendant.SetSourceOffset(-1);
        node.SetSourceOffset(offset >= 0 ? offset : -1);
    }

    sealed class Rewriter(IrFunction kickoff, int localOffset, Dictionary<string, (int Index, TypeRef Type)> hoisted)
    {
        public bool DispatchDropped { get; private set; }
        public int YieldCount { get; private set; }

        public bool TryRewrite(IEnumerable<IrNode> source, out List<IrNode> result)
        {
            result = [];
            foreach (var statement in source)
            {
                switch (statement)
                {
                    case SwitchBranch { Value: LoadField { Instance: LoadArgument { Index: 0 }, Field.Name: "<>1__state" } }:
                        DispatchDropped = true;
                        continue;  // the state dispatch — drop it
                    case SwitchBranch or Branch or ConditionalBranch:
                        return false;  // a real jump table or residual goto — not cleanly structured
                    case Return { Value: Constant { Value: bool } }:
                        continue;  // the `return true`/`return false` state markers
                    case Return:
                        return false;
                    case StoreField { Instance: LoadArgument { Index: 0 }, Field.Name: "<>1__state" }:
                        continue;  // a state advance — drop it
                    case StoreField { Instance: LoadArgument { Index: 0 }, Field.Name: "<>2__current" } yieldStore:
                        if (TryMap(yieldStore.Value) is not { } yielded)
                            return false;
                        result.Add(new YieldReturn(yielded));
                        YieldCount++;
                        break;
                    case StoreField { Instance: LoadArgument { Index: 0 }, Field: var field } hoistedStore
                        when hoisted.TryGetValue(field.Name, out var slot):
                        if (TryMap(hoistedStore.Value) is not { } stored)
                            return false;
                        result.Add(new StoreLocal(slot.Index, field.Type, stored));
                        break;
                    case StoreField:
                        return false;  // an unmodelled hoisted-state store
                    case StoreLocal store:
                        if (TryMap(store.Value) is not { } value)
                            return false;
                        result.Add(new StoreLocal(store.Index + localOffset, store.Type, value));
                        break;
                    case IfStatement conditional:
                        if (TryMap(conditional.Condition) is not { } condition
                            || !TryRewriteBlock(conditional.Then, out var then))
                            return false;
                        Block? elseArm = null;
                        if (conditional.Else is { } existing && !TryRewriteBlock(existing, out elseArm))
                            return false;
                        result.Add(new IfStatement(condition, then, elseArm));
                        break;
                    case WhileLoop loop:
                        if (TryMap(loop.Condition) is not { } guard
                            || !TryRewriteBlock(loop.Body, out var body))
                            return false;
                        result.Add(new WhileLoop(guard, body));
                        break;
                    case Block nested:
                        if (!TryRewriteBlock(nested, out var rewrittenBlock))
                            return false;
                        result.Add(rewrittenBlock);
                        break;
                    default:
                        return false;  // an unmodelled statement — bail rather than guess
                }
            }

            return true;
        }

        bool TryRewriteBlock(Block source, out Block result)
        {
            result = null!;
            if (!TryRewrite(source.Children, out var statements))
                return false;
            var block = new Block(0);
            foreach (var statement in statements)
                block.Add(statement);
            result = block;
            return true;
        }

        // Rebuilds a value expression in the kickoff's scope: a clone of the subtree with
        // every hoisted-field/local leaf swapped for the matching local or argument. Any
        // `this`-field that resolves to neither, or a leaked argument/spill slot, fails
        // the whole match (the self-containment guard).
        IrExpression? TryMap(IrExpression expression)
        {
            var clone = (IrExpression)expression.Clone();
            var ok = true;
            var swaps = new List<(IrNode Old, IrNode New)>();
            Visit(clone);
            if (!ok)
                return null;

            IrExpression root = clone;
            foreach (var (old, replacement) in swaps)
            {
                if (ReferenceEquals(old, clone))
                    root = (IrExpression)replacement;
                else
                    old.ReplaceWith(replacement);
            }
            return root;

            void Visit(IrNode node)
            {
                if (!ok)
                    return;
                switch (node)
                {
                    case LoadField { Instance: LoadArgument { Index: 0 }, Field: var field }:
                        if (hoisted.TryGetValue(field.Name, out var slot))
                            swaps.Add((node, new LoadLocal(slot.Index, field.Type)));
                        else if (TryGetParameter(kickoff, field.Name, out var index, out var parameter))
                            swaps.Add((node, new LoadArgument(index, parameter.Name, parameter.Type)));
                        else
                            ok = false;
                        return;  // never descend into a swapped field's `this` receiver
                    case LoadLocal local:
                        swaps.Add((node, new LoadLocal(local.Index + localOffset, local.Type)));
                        return;
                    case LoadArgument or LoadStackSlot:
                        ok = false;  // a leaked receiver/spill — outside this slice
                        return;
                    default:
                        foreach (var child in node.Children)
                            Visit(child);
                        return;
                }
            }
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

    static bool IsHoistedLocal(string fieldName)
        => fieldName.StartsWith("<", StringComparison.Ordinal)
            && !fieldName.StartsWith("<>", StringComparison.Ordinal)
            && fieldName.Contains(">5__", StringComparison.Ordinal);

    static string ExtractSourceName(string fieldName)
    {
        var close = fieldName.IndexOf('>');
        return close > 1 ? fieldName[1..close] : "i";
    }
}
