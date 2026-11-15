using System.Collections.Generic;
using System.Linq;

namespace ILInspector.Decompiler.Pipeline;

/// <summary>
/// Raises a compiler-generated iterator kickoff back to its <c>yield</c> body —
/// the inverse of the C# compiler's iterator lowering. The kickoff (a method
/// returning <c>IEnumerable</c>/<c>IEnumerator</c> that hands off to a
/// <c>new &lt;Method&gt;d__N(...)</c> state machine) carries no yield logic of its
/// own; the body lives in the state machine's <c>MoveNext</c>, imported through
/// the pass context's cross-method seam.
///
/// <para>The general inverse
/// (<see cref="StateMachineInverse.IteratorGeneralInverse"/>,
/// docs/design/iterator-reconstruction.md) runs first and owns every machine
/// it does not decline. It matches no shape of user code: it removes the
/// protocol roles from the raw <c>MoveNext</c> and lets the ordinary pipeline
/// structure what is left.</para>
///
/// <para>A shape matcher sees only a machine the inverse declined, and each one
/// that remains keeps a population the inverse does not yet own:
/// <see cref="ForeachIteratorReconstruction"/> (a yield inside an enumerator
/// loop or a <c>using</c>, whose disposal lowers to finally frames),
/// <see cref="SwitchIteratorReconstruction"/> (a <c>switch</c> with a yield in
/// each section), <see cref="YieldBreakLoopIteratorReconstruction"/> (a
/// <c>while (true)</c> loop left by a conditional <c>yield break</c>), and
/// <see cref="TryReconstructStateless"/> (a single-state machine that yields
/// nothing and has no state dispatch for the inverse to fold). A machine no
/// one reconstructs keeps its kickoff and is acknowledged with the inverse's
/// decline reason. A no-op when the seam is absent (stage dumps, the
/// lowered/annotated views).</para>
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
        if (!IteratorShapes.IsNarrowKickoffHandoff(function, handoff))
            return;

        var moveNextMethod = handoff.Constructor with { Name = "MoveNext" };
        // The general inverse runs first; a shape matcher is consulted only for
        // a machine the inverse declines (finally frames, today).
        if (TryGeneralInverse(function, context, handoff, moveNextMethod, out string? declineReason))
            return;
        RunShapeMatchers(function, context, handoff, moveNextMethod);
        if (declineReason is not null
            && IteratorShapes.TryGetKickoff(function, out var remaining) && ReferenceEquals(remaining, handoff))
        {
            string machine = IteratorShapes.MetadataName(handoff.Constructor.DeclaringType);
            context.Stepper.StepOver($"general iterator inverse declined '{machine}': {declineReason}");
            IteratorAcknowledgmentPass.Acknowledge(function, machine, handoff.SourceOffset, declineReason);
        }
    }

    static bool TryGeneralInverse(
        IrFunction function, PassContext context, NewObject handoff, MethodRef moveNextMethod, out string? declineReason)
    {
        declineReason = null;
        if (!context.TryEnterCrossMethodPipeline(moveNextMethod, out var scope))
            return false;

        IteratorGeneralInverse.Outcome outcome;
        using (scope)
        {
            IrFunction? raw = scope.Import();
            if (raw is null)
                return false;
            outcome = IteratorGeneralInverse.TryReconstruct(raw, function, handoff.Constructor.DeclaringType, scope.Run);
        }

        string machine = IteratorShapes.MetadataName(handoff.Constructor.DeclaringType);
        switch (outcome)
        {
            case IteratorGeneralInverse.Outcome.Reconstructed reconstructed:
                {
                    IrFunction body = reconstructed.Body;
                    // Slots the inverse emptied (the state local, a receiver
                    // copy) keep their lowered types but have no use; declare
                    // only the slots the body still binds or reads.
                    var eliminated = new HashSet<int>(body.EliminatedLocalSlots);
                    eliminated.UnionWith(
                        Enumerable.Range(0, body.Locals.Length)
                            .Where(index => !IrFunction.LocalSlotReferencesInScope(body, index).Any()));
                    context.Stepper.StepOver(
                        $"reconstruct iterator '{machine}' via the general inverse ({reconstructed.YieldCount} yields)", handoff);
                    function.MergeTypeFactsFrom(body);
                    function.ClearImportedExceptionFacts();
                    function.ResetLocals(
                        body.Locals,
                        body.LocalNames,
                        eliminatedSlots: eliminated,
                        synthesizedNames: body.SynthesizedLocalNames,
                        declaredInNestedScope: body.LocalDeclaredInNestedScope,
                        declarationBindings: body.LocalDeclarationBindings,
                        pdbLocalNameCandidates: body.PdbLocalNameCandidates);
                    function.LocalNameImportCauses = function.LocalNameImportCauses.AddRange(
                        body.LocalNameImportCauses.Select(static cause =>
                            cause.WithLocation(DecompilerFidelityLocation.Unknown)));
                    function.Body.DetachChildren();
                    foreach (Block moved in body.Body.Blocks.ToList())
                    {
                        moved.Detach();
                        function.Body.Add(moved);
                    }
                    // Every node came from MoveNext, whose offsets are another
                    // method's coordinates, so none survives. Each top-level
                    // statement is anchored at the handoff instead, which is
                    // what keeps the kickoff's IL and its state-machine
                    // allocation fact in the mixed C#/IL view.
                    int anchor = handoff.SourceOffset >= 0 ? handoff.SourceOffset : -1;
                    foreach (var node in function.Body.Descendants)
                        node.SetSourceOffset(-1);
                    foreach (var block in function.Body.Blocks)
                        foreach (var statement in block.Children)
                            statement.SetSourceOffset(anchor);
                    return true;
                }
            case IteratorGeneralInverse.Outcome.Declined declined:
                declineReason = declined.Reason;
                return false;
            default:
                return false;
        }
    }

    static void RunShapeMatchers(IrFunction function, PassContext context, NewObject handoff, MethodRef moveNextMethod)
    {
        if (!context.TryImportAndRunMethodBody(moveNextMethod, IrPasses.ForReconstruction<IteratorReconstructionPass>(), out var moveNext)
            || moveNext is null)
            return;

        var statements = new List<IrNode>();
        var rawSwitchCandidate = context.ImportMethodBody!(moveNextMethod);
        if (rawSwitchCandidate is not null
            && ForeachIteratorReconstruction.TryReconstructNestedDelegation(
                rawSwitchCandidate, function, handoff, context, out var nestedForeachBody))
        {
            Transplant(function, rawSwitchCandidate, nestedForeachBody, handoff, context,
                $"reconstruct nested foreach-delegation iterator '{IteratorShapes.MetadataName(handoff.Constructor.DeclaringType)}'");
            return;
        }

        if (rawSwitchCandidate is not null
            && ForeachIteratorReconstruction.TryReconstructUsingResources(
                rawSwitchCandidate, function, handoff, context, out var usingBody))
        {
            Transplant(function, rawSwitchCandidate, usingBody, handoff, context,
                $"reconstruct disposal-bearing iterator '{IteratorShapes.MetadataName(handoff.Constructor.DeclaringType)}'");
            return;
        }

        if (rawSwitchCandidate is not null && SwitchIteratorReconstruction.IsCandidate(rawSwitchCandidate))
        {
            if (SwitchIteratorReconstruction.TryReconstruct(rawSwitchCandidate, function, handoff, out var switchCandidateBody))
            {
                statements.AddRange(switchCandidateBody);
                var switchStateMachine = IteratorShapes.MetadataName(handoff.Constructor.DeclaringType);
                context.Stepper.StepOver($"reconstruct switch iterator '{switchStateMachine}' as {statements.Count} statement(s)", handoff);

                ReplaceBody(function, rawSwitchCandidate, statements);
                return;
            }
            return;  // exact switch shape with unowned code: leave for honest acknowledgment
        }

        if (TryReconstructStateless(moveNext, handoff, statements)
            && !IteratorReconstructionValidation.HasUnsupportedStructuredShape(statements))
        {
            var stateMachine = IteratorShapes.MetadataName(handoff.Constructor.DeclaringType);
            context.Stepper.StepOver($"reconstruct iterator '{stateMachine}' as {statements.Count} statement(s)", handoff);

            ReplaceBody(function, moveNext, statements);
            return;
        }

        var raw = context.ImportMethodBody(moveNextMethod);
        if (raw is null)
            return;  // leave for IteratorAcknowledgmentPass

        statements.Clear();
        if (SwitchIteratorReconstruction.TryReconstruct(raw, function, handoff, out var switchBody))
        {
            statements.AddRange(switchBody);
            var switchStateMachine = IteratorShapes.MetadataName(handoff.Constructor.DeclaringType);
            context.Stepper.StepOver($"reconstruct switch iterator '{switchStateMachine}' as {statements.Count} statement(s)", handoff);

            ReplaceBody(function, raw, statements);
            return;
        }
        if (SwitchIteratorReconstruction.IsCandidate(raw))
            return;  // exact switch shape with unowned code: leave for honest acknowledgment

        var rawYieldBreakLoop = context.ImportMethodBody(moveNextMethod);
        if (rawYieldBreakLoop is not null
            && YieldBreakLoopIteratorReconstruction.TryReconstruct(rawYieldBreakLoop, function, handoff, out var yieldBreakLoopBody))
        {
            statements.AddRange(yieldBreakLoopBody);
            var loopStateMachine = IteratorShapes.MetadataName(handoff.Constructor.DeclaringType);
            context.Stepper.StepOver($"reconstruct yield-break loop iterator '{loopStateMachine}' as {statements.Count} statement(s)", handoff);

            ReplaceBody(function, rawYieldBreakLoop, statements);
            return;
        }
        if (rawYieldBreakLoop is not null && YieldBreakLoopIteratorReconstruction.IsCandidate(rawYieldBreakLoop))
            return;  // exact yield-break-loop shape with unowned code: leave for honest acknowledgment

        // A foreach-delegation iterator carries the enumerator's disposal as a
        // fault region; strip that idiom and recover the foreach.
        var rawForeach = context.ImportMethodBody(moveNextMethod);
        if (rawForeach is not null
            && ForeachIteratorReconstruction.TryReconstruct(rawForeach, function, handoff, context, out var foreachBody))
        {
            Transplant(function, rawForeach, foreachBody, handoff, context,
                $"reconstruct foreach-delegation iterator '{IteratorShapes.MetadataName(handoff.Constructor.DeclaringType)}'");
        }
    }

    static void ReplaceBody(IrFunction function, IrFunction source, IEnumerable<IrNode> statements)
    {
        function.MergeTypeFactsFrom(source);
        function.LocalNameImportCauses = function.LocalNameImportCauses.AddRange(
            source.LocalNameImportCauses.Select(static cause =>
                cause.WithLocation(DecompilerFidelityLocation.Unknown)));
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

        function.MergeTypeFactsFrom(work);
        function.ClearImportedExceptionFacts();
        function.ResetLocals(
            work.Locals,
            work.LocalNames,
            work.EliminatedLocalSlots,
            work.SynthesizedLocalNames,
            work.LocalDeclaredInNestedScope,
            work.LocalDeclarationBindings,
            work.PdbLocalNameCandidates);
        function.LocalNameImportCauses = function.LocalNameImportCauses.AddRange(
            work.LocalNameImportCauses.Select(static cause =>
                cause.WithLocation(DecompilerFidelityLocation.Unknown)));
        function.Body.DetachChildren();
        foreach (var block in reconstructed.Blocks.ToList())
        {
            block.Detach();
            function.Body.Add(block);
        }
    }

    // A yield-nothing iterator: MoveNext stores no `<>2__current`, so for a single
    // state csc emits one block of `if (<>1__state ...) return false;` (the resume
    // guard), the `<>1__state = -1` advance, the method's own statements, and a
    // terminal `return false`. Strip the scaffolding, keep the observable
    // statements, and close with an explicit `yield break;` so the body still reads
    // — and recompiles — as an iterator. Declines (to honest acknowledgment) when a
    // kept statement reaches into the state machine, since hoisted fields, locals,
    // and spills have no spelling in the kickoff's scope.
    static bool TryReconstructStateless(IrFunction moveNext, NewObject handoff, List<IrNode> statements)
    {
        // An iterator yields exactly where MoveNext stores `<>2__current`.
        if (moveNext.Descendants.OfType<StoreField>().Any(s => s.Field.Name == "<>2__current"))
            return false;
        if (moveNext.Body.Blocks is not [var block])
            return false;

        var kept = new List<IrNode>();
        foreach (var statement in block.Children)
        {
            switch (statement)
            {
                case IfStatement guard when IsResumeGuard(guard):
                    continue;  // the `if (<>1__state ...) return false;` resume guard
                case StoreField { Instance: LoadArgument { Index: 0 }, Field.Name: "<>1__state" }:
                    continue;  // the state advance / running guard store
                case Return { Value: Constant { Value: bool } }:
                    continue;  // the terminal `return false` marker
                default:
                    if (!IsSelfContained(statement))
                        return false;
                    kept.Add(statement);
                    continue;
            }
        }

        foreach (var statement in kept)
        {
            var clone = statement.Clone();
            // The kept nodes carry the MoveNext's IL offsets, foreign to the kickoff;
            // anchor each to the state-machine allocation so the mixed C#/IL view never
            // mis-projects and the allocation fact still lands.
            foreach (var descendant in clone.Descendants)
                descendant.SetSourceOffset(-1);
            clone.SetSourceOffset(handoff.SourceOffset >= 0 ? handoff.SourceOffset : -1);
            statements.Add(clone);
        }

        var brk = new YieldBreak();
        // Carry the handoff's provenance so the state-machine allocation fact
        // (classified at import) still anchors onto the reconstructed body.
        if (handoff.SourceOffset >= 0)
            brk.SetSourceOffset(handoff.SourceOffset);
        statements.Add(brk);
        return true;
    }

    static bool IsResumeGuard(IfStatement guard)
        => guard.Else is null
            && guard.Then.Children is [Return { Value: Constant { Value: bool } }]
            && guard.Condition.Descendants.Prepend(guard.Condition).Any(
                node => node is LoadField { Instance: LoadArgument { Index: 0 }, Field.Name: "<>1__state" });

    static bool IsSelfContained(IrNode node)
        => !node.Descendants.Prepend(node)
            .Any(child => child is LoadField or LoadArgument or LoadLocal or LoadStackSlot);
}
