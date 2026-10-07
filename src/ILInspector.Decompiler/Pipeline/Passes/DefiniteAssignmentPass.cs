using System.Collections.Immutable;

namespace ILInspector.Decompiler.Pipeline;

/// <summary>
/// Decides, before printing, which up-front locals keep their <c>= default</c>
/// initializer (value-typed-emission.md, Instance 3). Definite assignment
/// (<see cref="DefiniteAssignment"/>) yields the locals that may be read before
/// they are definitely assigned; residual-bound locals
/// (<see cref="IrFunction.ResidualSlotBindings"/>) are removed from that set,
/// because a residual piece read with no reaching store is a binding gap that
/// <c>= default</c> would silently paper over. The result is issued on the
/// function and on every raised lambda and local function body, which own
/// their own local tables and print through their own scope. Runs last in the
/// emission tail — after residual binding and every pass that rewrites
/// statements — and again after the opt-in style lenses, so the set describes
/// the tree the printer reads.
/// </summary>
public sealed class DefiniteAssignmentPass : IIrPass
{
    public string Name => "definite-assignment";

    public PassAnalysisKind RequiredAnalyses
        => PassAnalysisKind.BranchTargets;

    public PassAnalysisKind PreservedAnalyses
        => PassAnalysisKind.BranchTargets;

    public void Run(IrFunction function, PassContext context)
        => Issue(function, context.BranchTargets(function));

    /// <summary>Issues the decided zero-initialized locals on the function and every nested body.</summary>
    public static void Issue(IrFunction function)
        => Issue(
            function,
            ReferenceOwnership.CollectBranchTargets(function));

    static void Issue(
        IrFunction function,
        IReadOnlySet<int> branchTargets)
    {
        function.ZeroInitializedLocals = Decide(
            function.Body,
            function.Locals.Length,
            branchTargets,
            function.ResidualSlotBindings);
        foreach (var node in function.Descendants)
        {
            switch (node)
            {
                case Lambda lambda:
                    lambda.ZeroInitializedLocals = Decide(
                        lambda.Body,
                        lambda.Locals.Length,
                        ReferenceOwnership.CollectBranchTargets(lambda.Body),
                        lambda.ResidualSlotBindings);
                    break;
                case LocalFunctionStatement localFunction:
                    localFunction.ZeroInitializedLocals = Decide(
                        localFunction.Body,
                        localFunction.Locals.Length,
                        ReferenceOwnership.CollectBranchTargets(localFunction.Body),
                        localFunction.ResidualSlotBindings);
                    break;
            }
        }
    }

    /// <summary>
    /// The runtime freshness check (<see cref="IrInvariants"/>): the issued set
    /// of a body about to be printed equals a fresh decision over that body.
    /// A pass that rewrites statements after this one without re-issuing it
    /// fails here instead of printing a stale initializer.
    /// </summary>
    internal static void CheckFresh(IrFunction function)
    {
        if (function.ZeroInitializedLocals is not { } issued)
            return;
        var fresh = Decide(
            function.Body,
            function.Locals.Length,
            ReferenceOwnership.CollectBranchTargets(function),
            function.ResidualSlotBindings);
        if (!issued.SetEquals(fresh))
        {
            throw new InvalidOperationException(
                $"{function.Name}: issued zero-initialized locals [{string.Join(", ", issued.Order())}] differ from a fresh decision [{string.Join(", ", fresh.Order())}]; a pass rewrote the body after DefiniteAssignmentPass.");
        }
    }

    static ImmutableHashSet<int> Decide(
        BlockContainer body,
        int localCount,
        IReadOnlySet<int> labelTargets,
        IReadOnlyDictionary<int, ResidualSlotBinding> residual)
        => [.. DefiniteAssignment.Compute(body, localCount, labelTargets, facts: null)
            .Where(index => !residual.ContainsKey(index))];
}
