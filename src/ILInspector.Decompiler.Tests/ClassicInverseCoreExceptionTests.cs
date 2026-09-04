using System.Collections.Immutable;

using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

public sealed partial class ClassicInverseCoreTests
{
    [Fact]
    public void ClassicInverseCompletionCatchRejectsDetachedPlanningRepair()
    {
        using RequestScope baseline = OpenRequest("AwaitValue");
        Reconstruct(baseline.Request);

        ImmutableArray<HandlerRegion> originalRegions = [];
        int narrowedStart = -1;
        using RequestScope narrowed = OpenMutatedRequest(
            "AwaitValue",
            execution =>
            {
                originalRegions = execution.Regions;
                HandlerRegion completion = Assert.Single(
                    originalRegions,
                    region => region.Kind == HandlerKind.Catch);
                Call getAwaiter = Assert.Single(
                    execution.Body.Descendants.OfType<Call>(),
                    call => call.Callee.Name == "GetAwaiter");
                Call awaitCallback = Assert.Single(
                    execution.Body.Descendants.OfType<Call>(),
                    call => call.Callee.Name
                        == "AwaitUnsafeOnCompleted");
                IrExpression operand = Assert.Single(getAwaiter.Arguments);

                narrowedStart = execution.Body.Blocks
                    .Select(block => block.StartOffset)
                    .Where(offset =>
                        offset > getAwaiter.SourceOffset
                        && offset <= awaitCallback.SourceOffset
                        && offset < completion.HandlerOffset)
                    .Order()
                    .First();

                Assert.InRange(
                    operand.SourceOffset,
                    completion.TryOffset,
                    narrowedStart - 1);
                Assert.InRange(
                    getAwaiter.SourceOffset,
                    completion.TryOffset,
                    narrowedStart - 1);
                Assert.InRange(
                    awaitCallback.SourceOffset,
                    narrowedStart,
                    completion.HandlerOffset - 1);

                execution.Regions =
                [
                    .. originalRegions.Select(region =>
                        region.Kind == HandlerKind.Catch
                            ? region with
                            {
                                TryOffset = narrowedStart,
                                TryLength =
                                    region.HandlerOffset - narrowedStart,
                            }
                            : region),
                ];
            });

        ClassicInverseDecision decision = DecideWithRepairedExceptionRegions(
            narrowed,
            originalRegions,
            body =>
            {
                TryCatch completion = Assert.Single(
                    body.Body.Descendants.OfType<TryCatch>());
                Call getAwaiter = Assert.Single(
                    body.Body.Descendants.OfType<Call>(),
                    call => call.Callee.Name == "GetAwaiter");
                Assert.True(IsWithin(getAwaiter, completion.TryBody));
            });

        AssertExceptionContextDecline(decision);
        Assert.True(narrowedStart >= 0);
    }

    [Fact]
    public void ClassicInverseFinallyRejectsDetachedPlanningRepair()
    {
        using RequestScope baseline = OpenRequest("AwaitInTryFinally");
        Reconstruct(baseline.Request);

        ImmutableArray<HandlerRegion> originalRegions = [];
        int narrowedStart = -1;
        using RequestScope narrowed = OpenMutatedRequest(
            "AwaitInTryFinally",
            execution =>
            {
                originalRegions = execution.Regions;
                HandlerRegion finallyRegion = Assert.Single(
                    originalRegions,
                    region => region.Kind == HandlerKind.Finally);
                Call getAwaiter = Assert.Single(
                    execution.Body.Descendants.OfType<Call>(),
                    call => call.Callee.Name == "GetAwaiter");
                Call awaitCallback = Assert.Single(
                    execution.Body.Descendants.OfType<Call>(),
                    call => call.Callee.Name
                        == "AwaitUnsafeOnCompleted");
                Call getResult = Assert.Single(
                    execution.Body.Descendants.OfType<Call>(),
                    call => call.Callee.Name == "GetResult");
                StoreLocal resultStore = Assert.Single(
                    execution.Body.Descendants.OfType<StoreLocal>(),
                    store => store.Descendants
                        .Prepend(store)
                        .Contains(getResult));
                IrExpression operand = Assert.Single(getAwaiter.Arguments);

                narrowedStart = execution.Body.Blocks
                    .Select(block => block.StartOffset)
                    .Where(offset =>
                        offset > awaitCallback.SourceOffset
                        && offset <= getResult.SourceOffset
                        && offset <= resultStore.SourceOffset
                        && offset < finallyRegion.HandlerOffset)
                    .Order()
                    .First();

                Assert.InRange(
                    operand.SourceOffset,
                    finallyRegion.TryOffset,
                    narrowedStart - 1);
                Assert.InRange(
                    getAwaiter.SourceOffset,
                    finallyRegion.TryOffset,
                    narrowedStart - 1);
                Assert.InRange(
                    awaitCallback.SourceOffset,
                    finallyRegion.TryOffset,
                    narrowedStart - 1);
                Assert.InRange(
                    getResult.SourceOffset,
                    narrowedStart,
                    finallyRegion.HandlerOffset - 1);
                Assert.InRange(
                    resultStore.SourceOffset,
                    narrowedStart,
                    finallyRegion.HandlerOffset - 1);

                execution.Regions =
                [
                    .. originalRegions.Select(region =>
                        region.Kind == HandlerKind.Finally
                            ? region with
                            {
                                TryOffset = narrowedStart,
                                TryLength =
                                    region.HandlerOffset - narrowedStart,
                            }
                            : region),
                ];
            });

        ClassicInverseDecision decision = DecideWithRepairedExceptionRegions(
            narrowed,
            originalRegions,
            body =>
            {
                TryFinally tryFinally = Assert.Single(
                    body.Body.Descendants.OfType<TryFinally>());
                Call getAwaiter = Assert.Single(
                    body.Body.Descendants.OfType<Call>(),
                    call => call.Callee.Name == "GetAwaiter");
                Assert.True(IsWithin(getAwaiter, tryFinally.TryBody));
            });

        AssertExceptionContextDecline(decision);
        Assert.True(narrowedStart >= 0);
    }

    static ClassicInverseDecision DecideWithRepairedExceptionRegions(
        RequestScope narrowed,
        ImmutableArray<HandlerRegion> originalRegions,
        Action<IrFunction> assertPlanning)
    {
        Assert.NotEqual(
            originalRegions,
            narrowed.Request.ExecutionBody.Regions);
        bool repairedPlanningClone = false;
        Action<IrFunction, ImmutableArray<IIrPass>> runner =
            Assert.IsType<Action<IrFunction, ImmutableArray<IIrPass>>>(
                narrowed.Request.RunPasses);
        ClassicInverseRequest repaired = CopyRequest(
            narrowed.Request,
            runPasses: (body, passes) =>
            {
                if (body.Name == "MoveNext")
                {
                    Assert.NotSame(
                        narrowed.Request.ExecutionBody,
                        body);
                    body.Regions = originalRegions;
                    repairedPlanningClone = true;
                }
                runner(body, passes);
                if (body.Name == "MoveNext")
                    assertPlanning(body);
            });

        ClassicInversePlanningView planning =
            ClassicInversePlanningView.Derive(repaired);
        var budget = new ClassicInverseBudget();
        ClassicInverseShellFacts shell =
            ClassicInverseShellFacts.Derive(
                planning.ExecutionBody,
                repaired.ExecutionBody,
                budget);
        Assert.False(budget.Exhausted);
        Assert.Contains(
            "structured exception context",
            Assert.IsType<string>(shell.Protocol.Failure),
            StringComparison.Ordinal);

        ClassicInverseDecision decision =
            ClassicInverseCore.Decide(repaired);
        Assert.True(repairedPlanningClone);
        Assert.NotEqual(
            originalRegions,
            narrowed.Request.ExecutionBody.Regions);
        return decision;
    }

    static void AssertExceptionContextDecline(
        ClassicInverseDecision decision)
    {
        var decline = Assert.IsType<ClassicInverseDecision.Decline>(
            decision);
        Assert.Equal(
            ClassicInverseDeclineReason.UnclassifiedPhysicalRegion,
            decline.Reason);
        Assert.Contains(
            "structured exception context",
            decline.Detail,
            StringComparison.Ordinal);
    }

    static bool IsWithin(IrNode node, IrNode ancestor)
    {
        for (IrNode? current = node; current is not null; current = current.Parent)
        {
            if (ReferenceEquals(current, ancestor))
                return true;
        }
        return false;
    }
}
