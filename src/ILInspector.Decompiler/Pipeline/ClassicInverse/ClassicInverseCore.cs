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

        foreach (IrNode _ in
            request.KickoffBody.Body.Descendants.Prepend(
                request.KickoffBody.Body))
        {
            if (!budget.Charge())
            {
                return ClassicInverseDecision.FailWith(
                    ClassicInverseFailureKind.BudgetExhausted,
                    "kickoff planning-view derivation exhausted the planning budget");
            }
        }
        foreach (IrNode _ in
            request.ExecutionBody.Body.Descendants.Prepend(
                request.ExecutionBody.Body))
        {
            if (!budget.Charge())
            {
                return ClassicInverseDecision.FailWith(
                    ClassicInverseFailureKind.BudgetExhausted,
                    "execution planning-view derivation exhausted the planning budget");
            }
        }

        ClassicInversePlanningView planning =
            ClassicInversePlanningView.Derive(request);
        ClassicInverseShellFacts shell =
            ClassicInverseShellFacts.Derive(planning.ExecutionBody);

        List<ClassicInverseCandidate> candidates =
            ClassicInverseRecipes.Match(planning, shell, budget);
        if (budget.Exhausted)
        {
            return ClassicInverseDecision.FailWith(
                ClassicInverseFailureKind.BudgetExhausted,
                "recipe matching exhausted the planning budget");
        }

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
