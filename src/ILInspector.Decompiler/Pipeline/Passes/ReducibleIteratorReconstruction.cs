using System;
using System.Collections.Generic;
using System.Linq;

namespace ILInspector.Decompiler.Pipeline;

/// <summary>
/// Reconstructs an <b>irreducible</b> iterator — one whose <c>MoveNext</c> the generic
/// structurer cannot raise because the state dispatch jumps back into the middle of a
/// loop body (the resume edge for state <c>k</c> lands after the <c>k</c>th yield). This
/// is the principled <em>transform-then-restructure</em> path: rather than pattern-match
/// one lowered shape, it strips the state-machine scaffolding from the <em>raw</em>
/// <c>MoveNext</c> to make the control-flow graph reducible, then re-runs the full pass
/// pipeline so the ordinary structurer forms the loops.
///
/// <para>The transform, on the flat imported blocks:</para>
/// <list type="number">
/// <item>drop the leading state dispatch (the blocks that test
/// <c>this.&lt;&gt;1__state</c>) and the trailing "no state matched" default — the
/// remaining graph roots at the state-0 init, and each resume edge vanishes because the
/// yield it served now falls through to its own continuation;</item>
/// <item>turn each <c>&lt;&gt;2__current = X; &lt;&gt;1__state = k; return true;</c> seam
/// into <c>yield return X;</c> (the <c>return true</c> dropped, so control falls through to
/// the resume landing that already follows it);</item>
/// <item>drop every <c>&lt;&gt;1__state = …</c> store and the bool <c>return</c> markers;</item>
/// <item>remap the hoisted loop fields (<c>&lt;i&gt;5__2</c>) to fresh locals and the
/// hoisted parameter fields back to the kickoff's parameters.</item>
/// </list>
///
/// <para>The stripped body is then a normal reducible method (yields are opaque statement
/// nodes the other passes pass through), so the structurer raises the loops uniformly —
/// including nested loops, which no single-shape matcher handles. Sound by validation: if
/// the restructured body still carries raw control flow (a goto the structurer could not
/// consume), an unresolved state-machine field, or no yield, the match is rejected and the
/// iterator falls through to honest acknowledgment. Iterators with exception regions
/// (<c>foreach</c>-delegation's <c>try/finally</c> Dispose) are out of this slice and
/// decline immediately.</para>
/// </summary>
internal static class ReducibleIteratorReconstruction
{
    public static bool TryReconstruct(IrFunction work, IrFunction kickoff, NewObject handoff, PassContext context, out BlockContainer body)
    {
        body = null!;

        // Exception regions (the foreach-delegation try/finally Dispose) are a richer
        // shape than this slice models; decline before touching the body.
        if (!work.Regions.IsEmpty)
            return false;

        var blocks = work.Body.Blocks;
        if (blocks.Count == 0)
            return false;

        // The dispatch reads the state into a local: `Vs = this.<>1__state;`.
        var stateStore = blocks[0].Children.OfType<StoreLocal>()
            .FirstOrDefault(s => s.Value is LoadField { Instance: LoadArgument { Index: 0 }, Field.Name: "<>1__state" });
        if (stateStore is null)
            return false;
        var stateLocal = stateStore.Index;

        // The dispatch is the maximal leading run of blocks that test Vs, plus the
        // "no state matched" default block that follows the last test.
        var dispatchEnd = 0;
        while (dispatchEnd < blocks.Count && TestsState(blocks[dispatchEnd], stateLocal))
            dispatchEnd++;
        if (dispatchEnd == 0)
            return false;
        if (dispatchEnd < blocks.Count
            && blocks[dispatchEnd].Children is [Return { Value: Constant }] or [Branch])
            dispatchEnd++;
        if (dispatchEnd >= blocks.Count)
            return false;

        // Materialize the hoisted loop fields as fresh locals on the work function
        // before the rewrite references them.
        var hoisted = new Dictionary<string, (int Index, TypeRef Type)>(StringComparer.Ordinal);
        foreach (var node in work.Descendants)
        {
            var field = node switch
            {
                StoreField { Instance: LoadArgument { Index: 0 }, Field: var f } => f,
                LoadField { Instance: LoadArgument { Index: 0 }, Field: var f } => f,
                _ => null,
            };
            if (field is null || !IsHoistedLocal(field.Name) || hoisted.ContainsKey(field.Name))
                continue;
            hoisted[field.Name] = (work.AddLocal(field.Type, ExtractSourceName(field.Name)), field.Type);
        }

        // Rebuild the surviving blocks with the scaffolding stripped, the yields sewn,
        // and the state-machine fields remapped.
        var container = new BlockContainer();
        for (var i = dispatchEnd; i < blocks.Count; i++)
        {
            var rebuilt = new Block(blocks[i].StartOffset);
            foreach (var statement in blocks[i].Children.ToList())
            {
                statement.Detach();
                switch (statement)
                {
                    case StoreField { Instance: LoadArgument { Index: 0 }, Field.Name: "<>1__state" }:
                        continue;  // a state advance / resume marker
                    case StoreField { Instance: LoadArgument { Index: 0 }, Field.Name: "<>2__current" } yieldStore:
                        if (!TryRemap(yieldStore.Value, kickoff, hoisted, out var yielded))
                            return false;
                        rebuilt.Add(new YieldReturn(yielded));
                        continue;
                    case StoreField { Instance: LoadArgument { Index: 0 }, Field: var hoistedField } hoistedStore
                        when hoisted.TryGetValue(hoistedField.Name, out var slot):
                        if (!TryRemap(hoistedStore.Value, kickoff, hoisted, out var stored))
                            return false;
                        rebuilt.Add(new StoreLocal(slot.Index, hoistedField.Type, stored));
                        continue;
                    case Return { Value: Constant }:
                        continue;  // the bool `return true`/`return false` markers
                    default:
                        if (!TryRemapInPlace(statement, kickoff, hoisted))
                            return false;
                        rebuilt.Add(statement);
                        continue;
                }
            }
            container.Add(rebuilt);
        }
        work.Body.ReplaceWith(container);

        // Re-run the pipeline: the body is reducible now, so the ordinary structurer
        // forms the loops and the yields ride along as opaque statements.
        IrPasses.Run(work, IrPasses.Default, context);

        // Validate: a clean reconstruction is fully structured (no residual goto), keeps
        // at least one yield, and leaves no unspeakable state-machine field behind.
        if (work.Body.Descendants.Any(IsUnstructured))
            return false;
        if (!work.Descendants.OfType<YieldReturn>().Any())
            return false;
        if (work.Descendants.Any(IsStateMachineField))
            return false;

        // The transplanted nodes carry the MoveNext's IL offsets, foreign to the kickoff;
        // clear them and anchor each top-level statement to the state-machine allocation so
        // the mixed C#/IL view never mis-projects and the allocation fact still lands.
        body = (BlockContainer)work.Body;
        foreach (var block in body.Blocks)
            foreach (var statement in block.Children)
                Reanchor(statement, handoff.SourceOffset);

        return true;
    }

    static bool TestsState(Block block, int stateLocal)
        => block.Children.OfType<ConditionalBranch>().Any(branch =>
            branch.Condition.Descendants.Prepend(branch.Condition)
                .Any(node => node is LoadLocal load && load.Index == stateLocal));

    static bool IsUnstructured(IrNode node)
        => node is Branch or ConditionalBranch or SwitchBranch or Leave or EndFinally or EndFilter or UnsupportedNode;

    static bool IsStateMachineField(IrNode node)
        => (node is LoadField { Field.Name: var loaded } && loaded.StartsWith("<", StringComparison.Ordinal))
            || (node is StoreField { Field.Name: var stored } && stored.StartsWith("<", StringComparison.Ordinal));

    static void Reanchor(IrNode node, int offset)
    {
        foreach (var descendant in node.Descendants)
            descendant.SetSourceOffset(-1);
        node.SetSourceOffset(offset >= 0 ? offset : -1);
    }

    // Rebuilds a value expression in the kickoff's scope: a clone with each hoisted-field
    // leaf swapped for the matching local or argument. Fails on any `this`-field that
    // resolves to neither, or a leaked argument/spill slot (the self-containment guard).
    static bool TryRemap(IrExpression expression, IrFunction kickoff,
        IReadOnlyDictionary<string, (int Index, TypeRef Type)> hoisted, out IrExpression result)
    {
        var clone = (IrExpression)expression.Clone();
        if (!TryRemapInPlace(clone, kickoff, hoisted, out var replacedRoot))
        {
            result = null!;
            return false;
        }
        result = (IrExpression)(replacedRoot ?? clone);
        return true;
    }

    static bool TryRemapInPlace(IrNode node, IrFunction kickoff,
        IReadOnlyDictionary<string, (int Index, TypeRef Type)> hoisted)
        => TryRemapInPlace(node, kickoff, hoisted, out _);

    static bool TryRemapInPlace(IrNode node, IrFunction kickoff,
        IReadOnlyDictionary<string, (int Index, TypeRef Type)> hoisted, out IrNode? replacedRoot)
    {
        replacedRoot = null;
        var ok = true;
        var swaps = new List<(IrNode Old, IrNode New)>();
        Visit(node);
        if (!ok)
            return false;

        foreach (var (old, replacement) in swaps)
        {
            if (ReferenceEquals(old, node))
                replacedRoot = replacement;
            else
                old.ReplaceWith(replacement);
        }
        return true;

        void Visit(IrNode current)
        {
            if (!ok)
                return;
            switch (current)
            {
                case LoadField { Instance: LoadArgument { Index: 0 }, Field: var field }:
                    if (hoisted.TryGetValue(field.Name, out var slot))
                        swaps.Add((current, new LoadLocal(slot.Index, field.Type)));
                    else if (TryGetParameter(kickoff, field.Name, out var index, out var parameter))
                        swaps.Add((current, new LoadArgument(index, parameter.Name, parameter.Type)));
                    else
                        ok = false;
                    return;  // never descend into a swapped field's `this` receiver
                case LoadField { Field.Name: var name } when name.StartsWith("<", StringComparison.Ordinal):
                    ok = false;  // a state-machine field reached through some other path
                    return;
                default:
                    foreach (var child in current.Children)
                        Visit(child);
                    return;
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
