using System.Collections.Generic;
using System.Linq;

namespace ILInspector.Decompiler.Pipeline;

/// <summary>
/// Raises a compiler-generated iterator kickoff back to its <c>yield return</c>
/// sequence — the inverse of the C# compiler's iterator lowering. The kickoff
/// (a method returning <c>IEnumerable</c>/<c>IEnumerator</c> that hands off to a
/// <c>new &lt;Method&gt;d__N(...)</c> state machine) carries no yield logic of its
/// own; the <c>yield</c> body lives in the state machine's <c>MoveNext</c>. This
/// pass imports that <c>MoveNext</c> through the pass context's cross-method seam,
/// recognizes the lowered state dispatch, and reconstructs the original
/// <c>yield return</c> statements.
///
/// <para>Current slice — <b>linear, self-contained</b> iterators: the dispatch
/// must be a single <c>switch (this.&lt;&gt;1__state)</c> whose case sections form
/// a straight chain (state 0 yields and advances to 1, state 1 to 2, …, ending in
/// a terminal <c>return false</c> section) with no back-edges (no loops), and each
/// yielded expression must be self-contained — referencing no hoisted field,
/// argument, or local of the state machine (constants and constant expressions).
/// An <b>empty</b> iterator — one whose <c>MoveNext</c> never stores
/// <c>&lt;&gt;2__current</c> (so it yields nothing, e.g. a bare <c>yield break;</c>) —
/// is reconstructed as <c>yield break;</c> regardless of dispatch shape (csc emits
/// an <c>if</c> rather than a <c>switch</c> for a single state). A <b>counting
/// loop</b> — a single <c>for</c>/<c>while</c> with one yield whose body is
/// self-contained modulo the loop variable (e.g. <c>for (int i = 0; i &lt; n; i++)
/// yield return i;</c>) — is reconstructed by
/// <see cref="CountingLoopReconstruction"/> from the lowered goto-dispatch
/// MoveNext. A <b>structured multi-yield</b> iterator — more than one yield point,
/// whether conditional (<c>if (flag) yield return …;</c>) or several per loop
/// iteration (<c>while (…) { yield return i; yield return -i; }</c>) — lowers to a
/// jump-table dispatch the structurer raises into ordinary <c>if</c>/<c>while</c>
/// nodes, and is reconstructed by <see cref="MultiYieldReconstruction"/> via a local
/// seam rewrite. An <b>irreducible</b> iterator whose dispatch jumps back into a loop
/// body (nested loops) is reconstructed by
/// <see cref="ReducibleIteratorReconstruction"/>, which strips the state scaffolding
/// from the raw MoveNext to make the graph reducible and re-runs the structurer. A
/// <b>foreach-delegation</b> iterator (<c>foreach (var x in source) yield …;</c>),
/// which adds the enumerator's split-disposal idiom on top of the irreducible
/// dispatch, is reconstructed by <see cref="ForeachIteratorReconstruction"/>, which
/// strips the disposal too and recovers the <c>foreach</c>. Other captured shapes do
/// not match and fall through to <see cref="IteratorAcknowledgmentPass"/>, which keeps the
/// gap honest. A no-op when the seam is absent (stage dumps, the lowered/annotated
/// views).</para>
/// </summary>
public sealed class IteratorReconstructionPass : IIrPass
{
    public string Name => "iterator-reconstruction";

    public void Run(IrFunction function, PassContext context)
    {
        if (context.ImportMethodBody is null)
            return;
        if (!IteratorShapes.TryGetKickoff(function, out var handoff))
            return;

        var moveNext = context.ImportMethodBody(handoff.Constructor with { Name = "MoveNext" });
        if (moveNext is null)
            return;

        // Raise the MoveNext body with the same pipeline so its state dispatch
        // lands at the altitude this pass recognizes.
        IrPasses.Run(moveNext, IrPasses.Default, context);

        if (!TryReconstruct(moveNext, function, handoff, out var statements))
        {
            // The structured matchers above all declined. Fall back to the general
            // transform-then-restructure path: strip the state scaffolding from a fresh
            // raw import to make the CFG reducible, then let the structurer raise it.
            var raw = context.ImportMethodBody(handoff.Constructor with { Name = "MoveNext" });
            if (raw is null)
                return;  // leave for IteratorAcknowledgmentPass

            if (ReducibleIteratorReconstruction.TryReconstruct(raw, function, handoff, context, out var reducibleBody))
            {
                Transplant(function, raw, reducibleBody, handoff, context,
                    $"reconstruct irreducible iterator '{IteratorShapes.MetadataName(handoff.Constructor.DeclaringType)}'");
                return;
            }

            // A foreach-delegation iterator carries an exception region (the enumerator's
            // disposal), so the reducible path declines; strip the disposal idiom too and
            // recover the foreach.
            var rawForeach = context.ImportMethodBody(handoff.Constructor with { Name = "MoveNext" });
            if (rawForeach is not null
                && ForeachIteratorReconstruction.TryReconstruct(rawForeach, function, handoff, context, out var foreachBody))
            {
                Transplant(function, rawForeach, foreachBody, handoff, context,
                    $"reconstruct foreach-delegation iterator '{IteratorShapes.MetadataName(handoff.Constructor.DeclaringType)}'");
                return;
            }

            return;  // leave for IteratorAcknowledgmentPass
        }

        var stateMachine = IteratorShapes.MetadataName(handoff.Constructor.DeclaringType);
        context.Stepper.StepOver($"reconstruct iterator '{stateMachine}' as {statements.Count} statement(s)", handoff);

        function.Body.DetachChildren();
        var block = new Block(0);
        foreach (var statement in statements)
            block.Add(statement);
        function.Body.Add(block);
    }

    // Adopts a transform-then-restructure reconstruction: the kickoff's body is replaced
    // wholesale by the restructured MoveNext body, so the kickoff's local table is reset to
    // the work function's (the transplanted blocks use its local indices).
    static void Transplant(IrFunction function, IrFunction work, BlockContainer reconstructed,
        NewObject handoff, PassContext context, string description)
    {
        context.Stepper.StepOver(description, handoff);

        function.ResetLocals(work.Locals, work.LocalNames);
        function.Body.DetachChildren();
        foreach (var block in reconstructed.Blocks.ToList())
        {
            block.Detach();
            function.Body.Add(block);
        }
    }

    static bool TryReconstruct(IrFunction moveNext, IrFunction kickoff, NewObject handoff, out List<IrNode> statements)
    {
        statements = [];

        // An iterator yields exactly where MoveNext stores `<>2__current`; with no
        // such store the sequence is empty regardless of the dispatch shape (csc
        // emits an `if` rather than a `switch` for a single state), so the whole
        // body reduces to `yield break;`.
        if (!moveNext.Descendants.OfType<StoreField>().Any(s => s.Field.Name == "<>2__current"))
        {
            var brk = new YieldBreak();
            // Carry the handoff's provenance so the state-machine allocation fact
            // (classified at import) still anchors onto the reconstructed body.
            if (handoff.SourceOffset >= 0)
                brk.SetSourceOffset(handoff.SourceOffset);
            statements.Add(brk);
            return true;
        }

        if (TryReconstructLinear(moveNext, out var yields))
        {
            statements.AddRange(yields);
            return true;
        }

        if (CountingLoopReconstruction.TryReconstruct(moveNext, kickoff, handoff, out var loop))
        {
            statements.AddRange(loop);
            return true;
        }

        if (MultiYieldReconstruction.TryReconstruct(moveNext, kickoff, handoff, out var multi))
        {
            statements.AddRange(multi);
            return true;
        }

        return false;
    }

    static bool TryReconstructLinear(IrFunction moveNext, out List<YieldReturn> yields)
    {
        yields = [];

        if (moveNext.Body.Blocks is not [{ Children: [Switch dispatch] }])
            return false;
        if (dispatch.Value is not LoadField { Instance: LoadArgument { Index: 0 }, Field.Name: "<>1__state" })
            return false;

        var sections = new Dictionary<int, List<IrNode>>();
        foreach (var section in dispatch.Sections)
        {
            if (section.IsDefault)
                continue;
            if (section.Labels is not [Constant { Value: int label }])
                return false;
            sections[label] = Flatten(section);
        }

        var state = 0;
        var visited = new HashSet<int>();
        while (true)
        {
            if (!visited.Add(state))
                return false;  // a back-edge: not a linear chain
            if (!sections.TryGetValue(state, out var statements))
                return false;

            switch (Classify(statements, out var value, out var nextState, out var offset))
            {
                case StateKind.Yield:
                    if (!IsSelfContained(value))
                        return false;
                    var yield = new YieldReturn((IrExpression)value.Clone());
                    if (offset >= 0)
                        yield.SetSourceOffset(offset);
                    yields.Add(yield);
                    state = nextState;
                    continue;
                case StateKind.Terminal:
                    return yields.Count > 0;
                default:
                    return false;
            }
        }
    }

    static List<IrNode> Flatten(SwitchSection section)
    {
        var statements = new List<IrNode>();
        foreach (var block in section.Body.Blocks)
            foreach (var statement in block.Children)
                if (statement is not Branch)
                    statements.Add(statement);
        return statements;
    }

    enum StateKind { Unrecognized, Yield, Terminal }

    static StateKind Classify(List<IrNode> statements, out IrExpression value, out int nextState, out int offset)
    {
        value = null!;
        nextState = -1;
        offset = -1;

        // The lowered state body ends in `return true` (a yield point) or
        // `return false` (the iterator terminates).
        if (statements.Count == 0 || statements[^1] is not Return { Value: Constant { Value: bool returns } })
            return StateKind.Unrecognized;

        StoreField? current = null;
        var advance = int.MinValue;
        for (var i = 0; i < statements.Count - 1; i++)
        {
            // A yield/terminal state only stores the `<>2__current` value and the
            // next `<>1__state`; anything else (locals, loop counters, captured
            // fields) is outside this slice.
            if (statements[i] is not StoreField { Instance: LoadArgument { Index: 0 } } store)
                return StateKind.Unrecognized;

            switch (store.Field.Name)
            {
                case "<>2__current":
                    if (current is not null)
                        return StateKind.Unrecognized;
                    current = store;
                    break;
                case "<>1__state":
                    if (store.Value is not Constant { Value: int stateValue })
                        return StateKind.Unrecognized;
                    if (stateValue != -1)
                        advance = stateValue;  // the running guard store is -1; the advance is the next state
                    break;
                default:
                    return StateKind.Unrecognized;
            }
        }

        if (returns)
        {
            if (current is null || advance == int.MinValue)
                return StateKind.Unrecognized;
            value = current.Value;
            nextState = advance;
            offset = current.SourceOffset;
            return StateKind.Yield;
        }

        return current is null ? StateKind.Terminal : StateKind.Unrecognized;
    }

    static bool IsSelfContained(IrExpression expression)
        => !expression.Descendants.Prepend(expression)
            .Any(node => node is LoadField or LoadArgument or LoadLocal or LoadStackSlot);
}
