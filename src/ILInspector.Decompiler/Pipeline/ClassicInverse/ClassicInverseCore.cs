using System.Collections.Immutable;

namespace ILInspector.Decompiler.Pipeline;

/// <summary>
/// The classic async inverse core.
/// <para>
/// Given one authenticated request and unmodified import snapshots bound to its
/// exact guarded identities, the core returns a healthy
/// <see cref="ClassicInverseDecision.Decline"/>, a visible
/// <see cref="ClassicInverseDecision.Failed"/>, or one immutable detached
/// <see cref="ClassicInverseDecision.Reconstruct"/>. Planning is deterministic
/// and side-effect free over the request; nothing it publishes points back into
/// the request's mutable trees.
/// </para>
/// <para>Owning design: <c>docs/design/classic-async-reconstruction.md</c>.</para>
/// </summary>
internal static class ClassicInverseCore
{
    /// <summary>
    /// Maximum imported-tree depth admitted before recursive cloning and
    /// prerequisite passes. The gate is deliberately below the native-stack
    /// danger zone and is checked iteratively.
    /// </summary>
    internal const int MaxPlanningDepth = 256;

    internal static ClassicInverseDecision Decide(ClassicInverseRequest request)
        => Decide(request, new ClassicInverseBudget());

    internal static ClassicInverseDecision Decide(
        ClassicInverseRequest request,
        ClassicInverseBudget budget)
    {
        if (request.CorrelationFailure() is { } correlation)
        {
            return ClassicInverseDecision.FailWith(
                ClassicInverseFailureKind.InvalidCorrelation,
                correlation);
        }
        if (request.HasBodyReplacingBodies)
        {
            return ClassicInverseDecision.DeclineWith(
                ClassicInverseDeclineReason.NoRecipeMatched,
                "authenticated bodies contain only reference-assembly replacement IL");
        }

        if (AdmitPlanningBody(
                request.KickoffBody.Body,
                "kickoff",
                budget) is { } kickoffFailure)
        {
            return kickoffFailure;
        }
        if (AdmitPlanningBody(
                request.ExecutionBody.Body,
                "execution",
                budget) is { } executionFailure)
        {
            return executionFailure;
        }

        ClassicInversePlanningView planning =
            ClassicInversePlanningView.Derive(request, budget);
        if (planning.TypeBinding.Failure is { } bindingFailure)
            return ClassicInverseDecision.FailWith(ClassicInverseFailureKind.InvalidCorrelation, bindingFailure);
        ClassicInverseShellFacts shell =
            ClassicInverseShellFacts.Derive(
                planning.ExecutionBody,
                request.ExecutionBody,
                budget);
        if (budget.Exhausted)
        {
            return ClassicInverseDecision.FailWith(
                ClassicInverseFailureKind.BudgetExhausted,
                "lowering-protocol proof exhausted the planning budget");
        }

        List<ClassicInverseCandidate> candidates =
            ClassicInverseRecipes.Match(request, planning, shell, budget, out string? unsafeAwait);
        if (budget.Exhausted)
        {
            return ClassicInverseDecision.FailWith(
                ClassicInverseFailureKind.BudgetExhausted,
                "recipe matching exhausted the planning budget");
        }
        if (unsafeAwait is not null)
            return ClassicInverseDecision.DeclineWith(ClassicInverseDeclineReason.UnsafeAwaitContext, unsafeAwait);

        if (candidates.Count == 0)
        {
            return ClassicInverseDecision.DeclineWith(
                ClassicInverseDeclineReason.NoRecipeMatched,
                "no closed recipe recognized the request's lowering shell");
        }

        if (candidates.Count > 1)
        {
            // Registration order must never decide an outcome. The accepted
            // recipes are mutually exclusive by construction, so a multiple
            // match means an unproven overlap, not a preference.
            return ClassicInverseDecision.DeclineWith(
                ClassicInverseDeclineReason.AmbiguousRecipeMatch,
                "more than one recipe claimed the request: "
                    + string.Join(
                        ", ",
                        candidates.Select(static c => c.Recipe).Order(
                            StringComparer.Ordinal)));
        }

        return ClassicInverseAccountant.Account(
            request,
            planning,
            candidates[0],
            shell,
            budget);
    }

    static ClassicInverseDecision? AdmitPlanningBody(
        IrNode root,
        string body,
        ClassicInverseBudget budget)
    {
        var pending = new Stack<(IrNode Node, int Depth)>();
        pending.Push((root, 0));
        while (pending.TryPop(out (IrNode Node, int Depth) item))
        {
            if (!budget.Charge())
            {
                return ClassicInverseDecision.FailWith(
                    ClassicInverseFailureKind.BudgetExhausted,
                    $"{body} planning-view derivation exhausted the planning budget");
            }
            if (item.Depth > MaxPlanningDepth)
            {
                return ClassicInverseDecision.FailWith(
                    ClassicInverseFailureKind.BudgetExhausted,
                    $"{body} planning-view depth exceeds {MaxPlanningDepth}");
            }

            for (int i = item.Node.Children.Count - 1; i >= 0; i--)
                pending.Push((item.Node.Children[i], item.Depth + 1));
        }
        return null;
    }

    /// <summary>
    /// Forms a core request from a kickoff body, its state-machine local, and
    /// the exact execution body. The unmodified execution import snapshot
    /// supplies the offsets that bind every receipt back to the artifact.
    /// </summary>
    internal static ClassicInverseRequest Request(
        IrFunction kickoff,
        int stateMachineLocal,
        int kickoffSourceOffset,
        IrFunction execution,
        ImmutableHashSet<int> executionImportOffsets,
        ClassicAsyncRequestSeed? seed,
        Action<IrFunction, ImmutableArray<IIrPass>>? runPasses = null)
        => new(
            seed?.DeclaredMethod,
            seed?.ExecutionMethod,
            seed?.Relationship,
            seed?.AcquisitionGuard,
            kickoff,
            execution,
            stateMachineLocal,
            kickoffSourceOffset,
            executionImportOffsets,
            runPasses);
}
