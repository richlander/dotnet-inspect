using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace ILInspector.Decompiler.Pipeline;

/// <summary>
/// Reconstructs a <b>foreach-delegation</b> iterator — the most common iterator shape,
/// <c>foreach (var x in source) { … yield … }</c>. Its <c>MoveNext</c> is doubly beyond
/// the generic structurer: the state dispatch is irreducible (the resume edge jumps into
/// the loop), <em>and</em> the enumerator's disposal lowers to the iterator-specific split
/// idiom — a <c>fault</c> region whose handler calls <c>Dispose()</c>, plus a
/// <c>&lt;&gt;m__Finally1()</c> call on the normal-completion path — which is neither a
/// reducible graph nor the <c>try/finally</c> the <see cref="UsingStatementPass"/> expects.
///
/// <para>This is the <em>transform-then-restructure</em> path (the sibling of
/// <see cref="ReducibleIteratorReconstruction"/>) extended for the enumerator loop. On the
/// flat raw blocks it:</para>
/// <list type="number">
/// <item>drops the leading state dispatch and the "no state matched" default exit;</item>
/// <item>materializes the hoisted enumerator field (<c>&lt;&gt;7__wrap1</c>) as a fresh
/// hidden local, so <c>e = source.GetEnumerator()</c> / <c>e.MoveNext()</c> / <c>e.Current</c>
/// read an ordinary local;</item>
/// <item>turns each <c>&lt;&gt;2__current = X; … return true;</c> seam into
/// <c>yield return X;</c>, and drops the <c>&lt;&gt;1__state = …</c> stores, the bool
/// return-value local stores, the <c>leave</c>/<c>return</c> markers, and the terminal
/// <c>return</c>;</item>
/// <item>drops the disposal scaffolding — the <c>&lt;&gt;m__Finally1()</c> call, the
/// <c>&lt;&gt;7__wrap1 = null</c> reset, and the entire fault-handler block — and clears the
/// region, because <c>foreach</c> re-implies the disposal.</item>
/// </list>
///
/// <para>The stripped body is now a reducible enumerator loop, so the structurer forms
/// <c>e = source.GetEnumerator(); while (e.MoveNext()) { … }</c>; a final raise folds that
/// hidden-enumerator loop into a <see cref="ForeachStatement"/>. Sound by validation: if the
/// restructured body still carries raw control flow, an unresolved state-machine field, no
/// yield, or no recovered <c>foreach</c>, the match is rejected and the iterator falls
/// through to honest acknowledgment.</para>
/// </summary>
internal static class ForeachIteratorReconstruction
{
    public static bool TryReconstruct(IrFunction work, IrFunction kickoff, NewObject handoff, PassContext context, out BlockContainer body)
    {
        body = null!;

        var blocks = work.Body.Blocks;
        if (blocks.Count == 0)
            return false;

        // The dispatch reads the state into a local: `Vs = this.<>1__state;`.
        var stateStore = blocks[0].Children.OfType<StoreLocal>()
            .FirstOrDefault(s => s.Value is LoadField { Instance: LoadArgument { Index: 0 }, Field.Name: "<>1__state" });
        if (stateStore is null)
            return false;
        var stateLocal = stateStore.Index;

        // The bool return-value local, returned by the terminal `return V;`. The EH
        // lowering routes every exit through it (a `leave` cannot carry a value).
        var returnLocal = -1;
        foreach (var block in blocks)
            if (block.Children.Count > 0 && block.Children[^1] is Return { Value: LoadLocal terminal })
                returnLocal = terminal.Index;
        if (returnLocal < 0)
            return false;

        // The hoisted enumerator field, assigned from a GetEnumerator() call. Its presence
        // (with a region) is the foreach-delegation signature; without it this is some other
        // shape and we decline so the dedicated matchers or acknowledgment own it.
        FieldRef? enumeratorField = null;
        foreach (var node in work.Descendants)
            if (node is StoreField { Instance: LoadArgument { Index: 0 }, Field: var f, Value: Call { Callee.Name: "GetEnumerator" } })
                enumeratorField = f;
        if (enumeratorField is null)
            return false;

        // field name -> (local index, type): the enumerator, plus any hoisted loop fields.
        var locals = new Dictionary<string, (int Index, TypeRef Type)>(StringComparer.Ordinal)
        {
            [enumeratorField.Name] = (work.AddLocal(enumeratorField.Type, null), enumeratorField.Type),
        };
        foreach (var node in work.Descendants)
        {
            var field = node switch
            {
                StoreField { Instance: LoadArgument { Index: 0 }, Field: var f } => f,
                LoadField { Instance: LoadArgument { Index: 0 }, Field: var f } => f,
                _ => null,
            };
            if (field is null || !GeneratedCodeIdentity.IsHoistedLocalFieldName(field.Name) || locals.ContainsKey(field.Name))
                continue;
            locals[field.Name] = (work.AddLocal(field.Type, ExtractSourceName(field.Name)), field.Type);
        }

        // The dispatch is the maximal leading run of state-testing blocks, plus the
        // "no state matched" default — `return false;` flows through the return local as
        // `V = false; leave terminal;` here (a `leave` cannot carry the constant).
        var dispatchEnd = 0;
        while (dispatchEnd < blocks.Count && TestsState(blocks[dispatchEnd], stateLocal))
            dispatchEnd++;
        if (dispatchEnd == 0)
            return false;
        if (dispatchEnd < blocks.Count && IsDefaultExit(blocks[dispatchEnd], returnLocal))
            dispatchEnd++;
        if (dispatchEnd >= blocks.Count)
            return false;

        // Rebuild the surviving blocks: strip the scaffolding, sew the yields, drop the
        // disposal idiom, and remap the state-machine fields to the fresh locals/arguments.
        var container = new BlockContainer();
        for (var i = dispatchEnd; i < blocks.Count; i++)
        {
            // The fault handler (`Dispose(); endfinally`) is disposal scaffolding — drop it.
            if (blocks[i].Children.Count > 0 && blocks[i].Children[^1] is EndFinally)
                continue;

            var rebuilt = new Block(blocks[i].StartOffset);
            foreach (var statement in blocks[i].Children.ToList())
            {
                statement.Detach();
                switch (statement)
                {
                    case StoreField { Instance: LoadArgument { Index: 0 }, Field.Name: "<>1__state" }:
                        continue;  // a state advance / resume marker
                    case StoreField { Instance: LoadArgument { Index: 0 }, Field.Name: "<>2__current" } yieldStore:
                        if (!TryRemap(yieldStore.Value, kickoff, locals, out var yielded))
                            return false;
                        rebuilt.Add(new YieldReturn(yielded));
                        continue;
                    case StoreField { Instance: LoadArgument { Index: 0 }, Field: var slotField } slotStore
                        when locals.TryGetValue(slotField.Name, out var slot):
                        // `<>7__wrap1 = null` is the disposal reset — drop it.
                        if (slotStore.Value is Constant { Value: null })
                            continue;
                        if (!TryRemap(slotStore.Value, kickoff, locals, out var stored))
                            return false;
                        rebuilt.Add(new StoreLocal(slot.Index, slotField.Type, stored));
                        continue;
                    case ExpressionStatement { Expression: Call { Callee.Name: var callee } }
                        when callee.StartsWith("<>m__Finally", StringComparison.Ordinal):
                        continue;  // the dispose-on-normal-completion call
                    case StoreLocal returnStore when returnStore.Index == returnLocal && returnStore.Value is Constant:
                        continue;  // the bool return-value marker
                    case Leave:
                    case Return:
                        continue;  // the leave-to-terminal and terminal `return V` markers
                    default:
                        if (!TryRemapInPlace(statement, kickoff, locals))
                            return false;
                        rebuilt.Add(statement);
                        continue;
                }
            }
            container.Add(rebuilt);
        }

        work.Regions = ImmutableArray<HandlerRegion>.Empty;
        work.Body.ReplaceWith(container);

        // Re-run the pipeline: the enumerator loop is reducible now, so the structurer
        // forms the `while (e.MoveNext())` loop with the yields riding along.
        IrPasses.Run(work, IrPasses.Default, context);

        // Fold the hidden-enumerator loop into a foreach.
        if (!RaiseForeach(work, context))
            return false;

        // Validate: fully structured, keeps a yield, recovers the foreach, and leaves no
        // unspeakable state-machine field behind.
        if (work.Body.Descendants.Any(IsUnstructured))
            return false;
        if (!work.Descendants.OfType<YieldReturn>().Any())
            return false;
        if (!work.Descendants.OfType<ForeachStatement>().Any())
            return false;
        if (work.Descendants.Any(IsStateMachineField))
            return false;

        body = (BlockContainer)work.Body;
        foreach (var block in body.Blocks)
            foreach (var statement in block.Children)
                Reanchor(statement, handoff.SourceOffset);

        return true;
    }

    // Raises `e = collection.GetEnumerator(); while (e.MoveNext()) { [item = e.Current;] BODY }`
    // — e a compiler-hidden enumerator local — into `foreach (item in collection) BODY`. The
    // copy-propagated single-use form (where `item = e.Current` folded into the body) is
    // recovered by introducing a fresh loop variable for the surviving `e.Current` reads.
    static bool RaiseForeach(IrFunction work, PassContext context)
    {
        foreach (var block in work.Body.Descendants.OfType<Block>().ToList())
        {
            var children = block.Children;
            for (var i = 0; i + 1 < children.Count; i++)
            {
                if (children[i] is not StoreLocal enumeratorStore
                    || enumeratorStore.Value is not Call { Callee.Name: "GetEnumerator" } getEnumerator
                    || getEnumerator.Arguments.Count != 1
                    || children[i + 1] is not WhileLoop loop
                    || loop.Condition is not Call { Callee.Name: "MoveNext" } moveNext
                    || moveNext.Arguments is not [LoadLocal conditionReceiver]
                    || conditionReceiver.Index != enumeratorStore.Index)
                {
                    continue;
                }

                var enumeratorIndex = enumeratorStore.Index;
                var loopBody = loop.Body;

                int loopVariable;
                TypeRef elementType;
                if (loopBody.Children.Count > 0
                    && loopBody.Children[0] is StoreLocal currentStore
                    && IsCurrentOf(currentStore.Value, enumeratorIndex))
                {
                    // `item = e.Current` survived (multi-use) — adopt it as the loop variable.
                    loopVariable = currentStore.Index;
                    elementType = currentStore.Type;
                    currentStore.Detach();
                }
                else
                {
                    // Single-use: `e.Current` folded into the body — reintroduce a loop variable.
                    var read = loopBody.Descendants.OfType<LoadProperty>()
                        .FirstOrDefault(p => IsCurrentOf(p, enumeratorIndex));
                    if (read is null)
                        return false;
                    elementType = read.ResultType!;
                    loopVariable = work.AddLocal(elementType, "item");
                    foreach (var current in loopBody.Descendants.OfType<LoadProperty>().ToList())
                        if (IsCurrentOf(current, enumeratorIndex))
                            current.ReplaceWith(new LoadLocal(loopVariable, elementType));
                }

                // The enumerator local must not leak past its Current/MoveNext uses.
                if (loopBody.Descendants.Any(n => n is LoadLocal load && load.Index == enumeratorIndex))
                    return false;

                var collection = getEnumerator.Arguments[0];
                collection.Detach();
                loopBody.Detach();
                var foreachStatement = new ForeachStatement(loopVariable, elementType, collection, loopBody);
                context.Stepper.StepOver("raise hidden-enumerator loop to foreach", loop);
                loop.ReplaceWith(foreachStatement);
                enumeratorStore.Detach();
                return true;
            }
        }
        return false;
    }

    static bool IsCurrentOf(IrExpression expression, int enumeratorIndex)
        => expression is LoadProperty { PropertyName: "Current", Instance: LoadLocal receiver }
            && receiver.Index == enumeratorIndex;

    static bool IsDefaultExit(Block block, int returnLocal)
        => block.Children is [Return { Value: Constant }]
            or [Branch]
            || (block.Children is [StoreLocal { Value: Constant } store, Leave] && store.Index == returnLocal);

    static bool TestsState(Block block, int stateLocal)
        => block.Children.OfType<ConditionalBranch>().Any(branch =>
            branch.Condition.Descendants.Prepend(branch.Condition)
                .Any(node => node is LoadLocal load && load.Index == stateLocal));

    static bool IsUnstructured(IrNode node)
        => node is Branch or ConditionalBranch or SwitchBranch or Leave or EndFinally or EndFilter or UnsupportedNode;

    static bool IsStateMachineField(IrNode node)
        => (node is LoadField { Field.Name: var loaded } && GeneratedCodeIdentity.IsGeneratedFieldName(loaded))
            || (node is StoreField { Field.Name: var stored } && GeneratedCodeIdentity.IsGeneratedFieldName(stored));

    static void Reanchor(IrNode node, int offset)
    {
        foreach (var descendant in node.Descendants)
            descendant.SetSourceOffset(-1);
        node.SetSourceOffset(offset >= 0 ? offset : -1);
    }

    static bool TryRemap(IrExpression expression, IrFunction kickoff,
        IReadOnlyDictionary<string, (int Index, TypeRef Type)> locals, out IrExpression result)
    {
        var clone = (IrExpression)expression.Clone();
        if (!TryRemapInPlace(clone, kickoff, locals, out var replacedRoot))
        {
            result = null!;
            return false;
        }
        result = (IrExpression)(replacedRoot ?? clone);
        return true;
    }

    static bool TryRemapInPlace(IrNode node, IrFunction kickoff,
        IReadOnlyDictionary<string, (int Index, TypeRef Type)> locals)
        => TryRemapInPlace(node, kickoff, locals, out _);

    static bool TryRemapInPlace(IrNode node, IrFunction kickoff,
        IReadOnlyDictionary<string, (int Index, TypeRef Type)> locals, out IrNode? replacedRoot)
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
                    if (locals.TryGetValue(field.Name, out var slot))
                        swaps.Add((current, new LoadLocal(slot.Index, field.Type)));
                    else if (TryGetParameter(kickoff, field.Name, out var index, out var parameter))
                        swaps.Add((current, new LoadArgument(index, parameter.Name, parameter.Type)));
                    else
                        ok = false;
                    return;  // never descend into a swapped field's `this` receiver
                case LoadField { Field.Name: var name } when GeneratedCodeIdentity.IsGeneratedFieldName(name):
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

    static string ExtractSourceName(string fieldName)
    {
        var close = fieldName.IndexOf('>');
        return close > 1 ? fieldName[1..close] : "i";
    }
}
