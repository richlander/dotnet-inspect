using System.Collections.Immutable;
using System.Reflection.Metadata;

using ILInspector.Decompiler.Pipeline;
using ILInspector.Metadata;
using ILInspector.MetadataPrimitives;
using InstructionExceptionClause =
    ILInspector.Instructions.InstructionExceptionClause;
using InstructionExceptionFlowFacts =
    ILInspector.Instructions.InstructionExceptionFlowFacts;
using InstructionExceptionRegion =
    ILInspector.Instructions.InstructionExceptionRegion;
using InstructionExceptionRegionId =
    ILInspector.Instructions.InstructionExceptionRegionId;
using MethodInstructions =
    ILInspector.Instructions.MethodInstructions;

namespace ILInspector.Decompiler.Tests;

public sealed partial class ClassicInverseCoreTests
{
    [Fact]
    public void
        ClassicInverseProductRequestJoinsRelationshipMoveNextToExceptionFacts()
    {
        using RequestScope scope = OpenRequest("TwoSequentialAwaits");
        StateMachineRelationship relationship =
            Assert.IsType<StateMachineRelationship>(scope.Request.Relationship);
        Assert.True(
            relationship.TryGetMethod(
                StateMachineMethodRole.MoveNext,
                out MetadataMethodAddress moveNext));

        InstructionExceptionFlowFacts facts =
            AvailableFacts(scope.Request.ExecutionBody);

        Assert.Equal(moveNext, facts.Body.Method);
        Reconstruct(scope.Request);
    }

    [Fact]
    public void
        ClassicInverseProductRequestRejectsForeignExceptionFlowMethod()
    {
        using RequestScope scope = OpenRequest("TwoSequentialAwaits");
        using RequestScope foreign = OpenRequest("AwaitValue");
        IrFunction execution = (IrFunction)scope.Request.ExecutionBody.Clone();
        execution.ExceptionInstructions =
            foreign.Request.ExecutionBody.ExceptionInstructions;
        execution.ExceptionClauseImports =
            foreign.Request.ExecutionBody.ExceptionClauseImports;
        Assert.NotEqual(
            AvailableFacts(scope.Request.ExecutionBody).Body.Method,
            AvailableFacts(execution).Body.Method);

        ClassicInverseRequest mismatched = CopyRequest(
            scope.Request,
            executionBody: execution);
        var failed = Assert.IsType<ClassicInverseDecision.Failed>(
            ClassicInverseCore.Decide(mismatched));

        Assert.Equal(
            ClassicInverseFailureKind.InvalidCorrelation,
            failed.Failure.Kind);
        Assert.Contains(
            "exception-flow body is not the relationship's MoveNext MethodDef",
            failed.Failure.Detail,
            StringComparison.Ordinal);
    }

    [Fact]
    public void
        ClassicInverseProductRequestWithUserFinallyStillReconstructs()
    {
        using RequestScope scope = OpenRequest("AwaitInTryFinally");

        Reconstruct(scope.Request);
    }

    [Fact]
    public void
        ClassicInverseCompletionCatchUsesSharedFactsInsteadOfCompatibilityRanges()
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

        ClassicInverseDecision decision = DecideWithRestoredPlanningRegions(
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

        Assert.IsType<ClassicInverseDecision.Reconstruct>(decision);
        Assert.True(narrowedStart >= 0);
    }

    [Fact]
    public void
        ClassicInverseFinallyUsesSharedFactsInsteadOfCompatibilityRanges()
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

        ClassicInverseDecision decision = DecideWithRestoredPlanningRegions(
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

        Assert.IsType<ClassicInverseDecision.Reconstruct>(decision);
        Assert.True(narrowedStart >= 0);
    }

    [Fact]
    public void ClassicInverseMissingSharedExceptionFlowDeclinesVisibly()
    {
        using RequestScope scope = OpenRequest("TwoSequentialAwaits");
        scope.Request.ExecutionBody.ExceptionInstructions = null;

        var decline = Assert.IsType<ClassicInverseDecision.Decline>(
            ClassicInverseCore.Decide(scope.Request));

        Assert.Equal(
            ClassicInverseDeclineReason.UnclassifiedPhysicalRegion,
            decline.Reason);
        Assert.Contains(
            "no correlated Instructions exception-flow evidence",
            decline.Detail,
            StringComparison.Ordinal);
    }

    [Fact]
    public void ClassicInverseUnavailableSharedExceptionFlowDeclinesVisibly()
    {
        using RequestScope scope = OpenRequest("TwoSequentialAwaits");
        MethodInstructions available =
            Assert.IsType<MethodInstructions>(
                scope.Request.ExecutionBody.ExceptionInstructions);
        scope.Request.ExecutionBody.ExceptionInstructions =
            new MethodInstructions(
                available.Instructions,
                available.Blocks);

        var decline = Assert.IsType<ClassicInverseDecision.Decline>(
            ClassicInverseCore.Decide(scope.Request));

        Assert.Equal(
            ClassicInverseDeclineReason.UnclassifiedPhysicalRegion,
            decline.Reason);
        Assert.Contains(
            "Instructions exception-flow evidence is unavailable",
            decline.Detail,
            StringComparison.Ordinal);
    }

    [Fact]
    public void
        ClassicInverseStructuredContextRejectsSameRangeForeignBodyIdentity()
    {
        using RequestScope scope = OpenRequest("TwoSequentialAwaits");
        using RequestScope foreign = OpenRequest("TwoSequentialAwaits");
        InstructionExceptionFlowFacts localFacts =
            AvailableFacts(scope.Request.ExecutionBody);
        InstructionExceptionFlowFacts foreignFacts =
            AvailableFacts(foreign.Request.ExecutionBody);
        InstructionExceptionClause localClause = Assert.Single(
            localFacts.Clauses,
            static clause => clause.Kind == ExceptionRegionKind.Catch);
        InstructionExceptionClause foreignClause = Assert.Single(
            foreignFacts.Clauses,
            static clause => clause.Kind == ExceptionRegionKind.Catch);

        Assert.Equal(
            Region(localFacts, localClause.ProtectedRegion).Extent,
            Region(foreignFacts, foreignClause.ProtectedRegion).Extent);
        Assert.NotEqual(
            localClause.ProtectedRegion,
            foreignClause.ProtectedRegion);

        Action<IrFunction, ImmutableArray<IIrPass>> runner =
            Assert.IsType<Action<IrFunction, ImmutableArray<IIrPass>>>(
                scope.Request.RunPasses);
        ClassicInverseRequest stale = CopyRequest(
            scope.Request,
            runPasses: (body, passes) =>
            {
                runner(body, passes);
                if (body.Name != "MoveNext")
                    return;

                TryCatch original = Assert.Single(
                    body.Body.Descendants.OfType<TryCatch>());
                IReadOnlyList<IrNode> children =
                    original.DetachChildren();
                var replacement = new TryCatch(
                    (BlockContainer)children[0],
                    children.Skip(1).Cast<CatchClause>())
                {
                    ExceptionProtectedRegion =
                        foreignClause.ProtectedRegion,
                };
                original.ReplaceWith(replacement);
            });

        var decline = Assert.IsType<ClassicInverseDecision.Decline>(
            ClassicInverseCore.Decide(stale));
        Assert.Equal(
            ClassicInverseDeclineReason.UnclassifiedPhysicalRegion,
            decline.Reason);
        Assert.Contains(
            "no exact Instructions protected-region association",
            decline.Detail,
            StringComparison.Ordinal);
    }

    [Fact]
    public void
        ClassicInverseCatchRejectsSameRangeForeignClauseIdentity()
    {
        using RequestScope scope = OpenRequest("TwoSequentialAwaits");
        using RequestScope foreign = OpenRequest("TwoSequentialAwaits");
        InstructionExceptionClause foreignClause = Assert.Single(
            AvailableFacts(foreign.Request.ExecutionBody).Clauses,
            static clause => clause.Kind == ExceptionRegionKind.Catch);
        Action<IrFunction, ImmutableArray<IIrPass>> runner =
            Assert.IsType<Action<IrFunction, ImmutableArray<IIrPass>>>(
                scope.Request.RunPasses);
        ClassicInverseRequest stale = CopyRequest(
            scope.Request,
            runPasses: (body, passes) =>
            {
                runner(body, passes);
                if (body.Name != "MoveNext")
                    return;

                TryCatch original = Assert.Single(
                    body.Body.Descendants.OfType<TryCatch>());
                CatchClause originalClause =
                    Assert.Single(original.Clauses);
                Assert.Null(originalClause.Filter);
                var replacementClause = new CatchClause(
                    originalClause.ExceptionType,
                    (BlockContainer)originalClause.Body.Clone())
                {
                    VariableIndex = originalClause.VariableIndex,
                    ExceptionClause = foreignClause,
                };
                var replacement = new TryCatch(
                    (BlockContainer)original.TryBody.Clone(),
                    [replacementClause])
                {
                    ExceptionProtectedRegion =
                        original.ExceptionProtectedRegion,
                };
                original.ReplaceWith(replacement);
            });

        var decline = Assert.IsType<ClassicInverseDecision.Decline>(
            ClassicInverseCore.Decide(stale));
        Assert.Equal(
            ClassicInverseDeclineReason.UnclassifiedPhysicalRegion,
            decline.Reason);
        Assert.Contains(
            "structured catch clause has no exact Instructions clause association",
            decline.Detail,
            StringComparison.Ordinal);
    }

    [Fact]
    public void
        ClassicInversePlanningAndRawRequireOneExceptionFlowObservation()
    {
        using RequestScope scope = OpenRequest("TwoSequentialAwaits");
        using RequestScope foreign = OpenRequest("TwoSequentialAwaits");
        InstructionExceptionClause foreignClause = Assert.Single(
            AvailableFacts(foreign.Request.ExecutionBody).Clauses,
            static clause => clause.Kind == ExceptionRegionKind.Catch);
        Action<IrFunction, ImmutableArray<IIrPass>> runner =
            Assert.IsType<Action<IrFunction, ImmutableArray<IIrPass>>>(
                scope.Request.RunPasses);
        ClassicInverseRequest mismatched = CopyRequest(
            scope.Request,
            runPasses: (body, passes) =>
            {
                runner(body, passes);
                if (body.Name != "MoveNext")
                    return;

                TryCatch original = Assert.Single(
                    body.Body.Descendants.OfType<TryCatch>());
                CatchClause originalClause =
                    Assert.Single(original.Clauses);
                Assert.Null(originalClause.Filter);
                var replacementClause = new CatchClause(
                    originalClause.ExceptionType,
                    (BlockContainer)originalClause.Body.Clone())
                {
                    VariableIndex = originalClause.VariableIndex,
                    ExceptionClause = foreignClause,
                };
                var replacement = new TryCatch(
                    (BlockContainer)original.TryBody.Clone(),
                    [replacementClause])
                {
                    ExceptionProtectedRegion =
                        foreignClause.ProtectedRegion,
                };
                original.ReplaceWith(replacement);
                body.ExceptionInstructions =
                    foreign.Request.ExecutionBody.ExceptionInstructions;
                body.ExceptionClauseImports =
                    foreign.Request.ExecutionBody.ExceptionClauseImports;
            });

        var decline = Assert.IsType<ClassicInverseDecision.Decline>(
            ClassicInverseCore.Decide(mismatched));
        Assert.Equal(
            ClassicInverseDeclineReason.UnclassifiedPhysicalRegion,
            decline.Reason);
        Assert.Contains(
            "do not share one Instructions exception-flow observation",
            decline.Detail,
            StringComparison.Ordinal);
    }

    [Fact]
    public void
        ClassicInverseFinallyRejectsSameRangeForeignClauseIdentity()
    {
        using RequestScope scope = OpenRequest("AwaitInTryFinally");
        using RequestScope foreign = OpenRequest("AwaitInTryFinally");
        InstructionExceptionFlowFacts localFacts =
            AvailableFacts(scope.Request.ExecutionBody);
        InstructionExceptionFlowFacts foreignFacts =
            AvailableFacts(foreign.Request.ExecutionBody);
        InstructionExceptionClause localClause = Assert.Single(
            localFacts.Clauses,
            static clause => clause.Kind == ExceptionRegionKind.Finally);
        InstructionExceptionClause foreignClause = Assert.Single(
            foreignFacts.Clauses,
            static clause => clause.Kind == ExceptionRegionKind.Finally);
        Assert.Equal(
            Region(localFacts, localClause.ProtectedRegion).Extent,
            Region(foreignFacts, foreignClause.ProtectedRegion).Extent);
        Assert.NotEqual(localClause.Id, foreignClause.Id);

        Action<IrFunction, ImmutableArray<IIrPass>> runner =
            Assert.IsType<Action<IrFunction, ImmutableArray<IIrPass>>>(
                scope.Request.RunPasses);
        ClassicInverseRequest stale = CopyRequest(
            scope.Request,
            runPasses: (body, passes) =>
            {
                runner(body, passes);
                if (body.Name != "MoveNext")
                    return;

                TryFinally original = Assert.Single(
                    body.Body.Descendants.OfType<TryFinally>());
                var replacement = new TryFinally(
                    (BlockContainer)original.TryBody.Clone(),
                    (BlockContainer)original.FinallyBody.Clone())
                {
                    ExceptionClause = foreignClause,
                };
                original.ReplaceWith(replacement);
            });

        var decline = Assert.IsType<ClassicInverseDecision.Decline>(
            ClassicInverseCore.Decide(stale));
        Assert.Equal(
            ClassicInverseDeclineReason.UnclassifiedPhysicalRegion,
            decline.Reason);
        Assert.Contains(
            "structured finally has no exact Instructions clause association",
            decline.Detail,
            StringComparison.Ordinal);
    }

    static ClassicInverseDecision DecideWithRestoredPlanningRegions(
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
        Assert.Null(shell.Protocol.Failure);

        ClassicInverseDecision decision =
            ClassicInverseCore.Decide(repaired);
        Assert.True(repairedPlanningClone);
        Assert.NotEqual(
            originalRegions,
            narrowed.Request.ExecutionBody.Regions);
        return decision;
    }

    static InstructionExceptionFlowFacts AvailableFacts(
        IrFunction function)
        => Assert.IsType<ILInspector.Instructions.InstructionExceptionFlowResult<
            InstructionExceptionFlowFacts>.Available>(
                function.ExceptionFlow).Value;

    static InstructionExceptionRegion Region(
        InstructionExceptionFlowFacts facts,
        InstructionExceptionRegionId id)
        => Assert.Single(facts.Regions, region => region.Id == id);

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
