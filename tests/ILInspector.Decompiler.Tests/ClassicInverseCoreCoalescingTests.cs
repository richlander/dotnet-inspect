using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

public sealed partial class ClassicInverseCoreTests
{
    [Theory]
    [InlineData("Coalesce", "return await a ?? \"fallback\";")]
    [InlineData("CoalesceCall", "return await a ?? Fallback();")]
    [InlineData("CoalesceParameter", "return await a ?? fallback;")]
    [InlineData("CoalesceThenCall", "(await a ?? Fallback()).Trim()")]
    [InlineData("CoalesceOperandCall", "(await a).Trim() ?? Fallback()")]
    [InlineData("CoalesceBooleanArgument", "await a ?? ChooseFallback(!useFirst)")]
    public void ClassicInversePreservesCoalescingExpressions(string method, string expected)
    {
        using RequestScope scope = OpenExpressionRequest(method);
        ClassicInversePlan plan = Reconstruct(scope.Request);
        BlockContainer body = plan.MaterializeBody();
        body.CheckInvariant();
        Coalesce expression = Assert.Single(body.Descendants.OfType<Coalesce>());
        Assert.Single(expression.Left.Descendants.Prepend(expression.Left).OfType<AwaitExpression>());
        Assert.DoesNotContain(expression.Right.Descendants, node => node is AwaitExpression);
        Assert.All(plan.SemanticRealizations,
            receipt => Assert.Equal(receipt.SourceEffects, receipt.OutputEffects));
        Assert.Contains(plan.StructuredAncestorReceipts, receipt => receipt.Steps.Any(step =>
            step.NodeForm == "Coalesce" && step.Kind == ClassicInverseAncestorKind.Reproduced));
        Assert.Equal(scope.Request.ExecutionBody.Body.Descendants.Count() + 1,
            CountCoveredNodes(scope.Request.ExecutionBody.Body,
                plan.PhysicalPartition.Where(region => region.Body == ClassicInverseBodyId.Execution)));

        ConditionalBranch guard = Assert.Single(scope.Request.ExecutionBody.Body.Descendants
            .OfType<ConditionalBranch>(), branch => branch.Condition is LoadStackSlot);
        Assert.Contains(plan.PhysicalPartition, region =>
            region.Disposition == ClassicInverseRegionDisposition.Semantic
            && region.ImportOffsets.Contains(guard.SourceOffset));
        Assert.Contains(plan.SemanticRealizations, receipt =>
            receipt.Rule == ClassicInverseRealizationRule.ResultStore
            && receipt.ImportOffsets.Contains(guard.SourceOffset));

        DecompilerResult result = DecompileExpressionFixture(method);
        Assert.Equal(DecompilationFidelity.Full, result.Fidelity);
        Assert.Contains(expected, result.Output, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("target")]
    [InlineData("condition")]
    [InlineData("lost-branch")]
    [InlineData("extra-fallback-effect")]
    [InlineData("carried-slot")]
    [InlineData("joined-slot")]
    public void ClassicInverseCoalescingControlCannotBeHealedByPlanning(string mutation)
    {
        using RequestScope baseline = OpenExpressionRequest("CoalesceCall");
        Reconstruct(baseline.Request);
        using RequestScope changed = OpenRequest(OpenClassicFixture(), "CoalesceCall",
            ownsSource: true, mutateExecution: body =>
            {
                ConditionalBranch guard = Assert.Single(body.Body.Descendants.OfType<ConditionalBranch>(),
                    branch => branch.Condition is LoadStackSlot);
                Call call = Assert.Single(body.Body.Descendants.OfType<Call>(),
                    call => call.Callee.Name == "Fallback");
                StoreStackSlot fallback = Assert.IsType<StoreStackSlot>(call.Parent);
                Block fallbackBlock = Assert.IsType<Block>(fallback.Parent);
                switch (mutation)
                {
                    case "target":
                    {
                        var replacement = new ConditionalBranch(
                            (IrExpression)guard.Condition.Clone(), fallbackBlock.StartOffset);
                        replacement.InheritSourceOffset(guard);
                        guard.ReplaceWith(replacement);
                        break;
                    }
                    case "condition":
                    {
                        var replacement = new Constant(true, TypeRef.CoreLib("System", "Boolean"));
                        replacement.InheritSourceOffset(guard.Condition);
                        guard.SetChild(0, replacement);
                        break;
                    }
                    case "lost-branch":
                        guard.Detach();
                        break;
                    case "extra-fallback-effect":
                        fallback.Detach();
                        fallbackBlock.Add(new ExpressionStatement((IrExpression)call.Clone()));
                        fallbackBlock.Add(fallback);
                        break;
                    case "carried-slot":
                    {
                        Block head = Assert.IsType<Block>(guard.Parent);
                        StoreStackSlot carry = Assert.IsType<StoreStackSlot>(head.Children[1]);
                        var replacement = new LoadStackSlot(carry.Slot, carry.Value.ResultType);
                        replacement.InheritSourceOffset(carry.Value);
                        carry.SetChild(0, replacement);
                        break;
                    }
                    case "joined-slot":
                    {
                        Block head = Assert.IsType<Block>(guard.Parent);
                        StoreStackSlot first = Assert.IsType<StoreStackSlot>(head.Children[0]);
                        Block merge = Assert.Single(body.Body.Blocks,
                            block => block.StartOffset == guard.TargetOffset);
                        LoadStackSlot joined = Assert.Single(merge.Descendants.OfType<LoadStackSlot>());
                        var replacement = new LoadStackSlot(first.Slot, joined.Type);
                        replacement.InheritSourceOffset(joined);
                        joined.ReplaceWith(replacement);
                        break;
                    }
                }
            }, fixtureType: ExpressionFixtureType);
        AssertPlanningCannotRepair(changed.Request, baseline.Request);
    }

    [Theory]
    [InlineData("swap")]
    [InlineData("lost-condition")]
    [InlineData("wrong-member")]
    [InlineData("unconditional-fallback")]
    [InlineData("missing-offset")]
    public void ClassicInverseCoalescingRequiresExactPlanningJoin(string mutation)
    {
        using RequestScope scope = OpenExpressionRequest("CoalesceCall");
        Reconstruct(scope.Request);
        bool changed = false;
        ClassicInverseRequest request = CopyRequest(scope.Request, runPasses: (body, passes) =>
        {
            scope.Request.RunPasses!(body, passes);
            if (body.Name != "MoveNext")
                return;
            Coalesce expression = Assert.Single(body.Body.Descendants.OfType<Coalesce>());
            switch (mutation)
            {
                case "swap":
                case "lost-condition":
                {
                    var children = expression.DetachChildren();
                    IrNode replacement = mutation == "swap"
                        ? new Coalesce((IrExpression)children[1], (IrExpression)children[0])
                        : children[0];
                    replacement.InheritSourceOffset(expression);
                    expression.ReplaceWith(replacement);
                    break;
                }
                case "wrong-member":
                {
                    Call call = Assert.IsType<Call>(expression.Right);
                    var replacement = new Call(call.Callee with { Name = "OtherFallback" }, call.IsVirtual, []);
                    replacement.InheritSourceOffset(call);
                    call.ReplaceWith(replacement);
                    break;
                }
                case "unconditional-fallback":
                {
                    var fallback = (IrExpression)expression.Right.Clone();
                    var replacement = new Constant("fallback", expression.Right.ResultType!);
                    replacement.InheritSourceOffset(expression.Right);
                    expression.SetChild(1, replacement);
                    StoreLocal store = Assert.IsType<StoreLocal>(expression.Parent);
                    Block merge = Assert.IsType<Block>(store.Parent);
                    store.Detach();
                    merge.Add(new ExpressionStatement(fallback));
                    merge.Add(store);
                    break;
                }
                case "missing-offset":
                    expression.SetSourceOffset(-1);
                    break;
            }
            changed = true;
        });
        Assert.IsType<ClassicInverseDecision.Decline>(ClassicInverseCore.Decide(request));
        Assert.True(changed);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ClassicInverseRejectsLostCoalescingConditionOrEffect(bool loseCondition)
    {
        using RequestScope scope = OpenExpressionRequest("CoalesceCall");
        Reconstruct(scope.Request);
        var (planning, candidate, shell) = Candidate(scope.Request);
        Coalesce expression = Assert.Single(candidate.Statements
            .SelectMany(statement => statement.Descendants).OfType<Coalesce>());
        if (loseCondition)
        {
            var children = expression.DetachChildren();
            expression.ReplaceWith(children[0]);
        }
        else
        {
            expression.SetChild(1, new Constant("fallback", expression.Right.ResultType!));
        }
        Assert.IsType<ClassicInverseDecision.Decline>(
            ClassicInverseAccountant.Account(scope.Request, planning, candidate,
                shell, new ClassicInverseBudget()));
    }

    [Fact]
    public void ClassicInverseCoalescingPlanIsDetached()
    {
        using RequestScope scope = OpenExpressionRequest("CoalesceCall");
        ClassicInversePlan plan = Reconstruct(scope.Request);
        Coalesce first = Assert.Single(plan.MaterializeBody().Descendants.OfType<Coalesce>());
        MethodRef fallback = Assert.IsType<Call>(first.Right).Callee;
        first.SetChild(0, new Constant(null, first.Left.ResultType!));
        first.SetChild(1, new Constant("changed", first.Right.ResultType!));
        scope.Request.KickoffBody.Body.DetachChildren();
        scope.Request.ExecutionBody.Body.DetachChildren();

        BlockContainer body = plan.MaterializeBody();
        body.CheckInvariant();
        Coalesce second = Assert.Single(body.Descendants.OfType<Coalesce>());
        Assert.NotSame(first, second);
        Assert.Equal("a", Assert.IsType<LoadArgument>(
            Assert.IsType<AwaitExpression>(second.Left).Operand).Name);
        Assert.Equal(fallback, Assert.IsType<Call>(second.Right).Callee);
        Assert.Null(((Call)second.Right).Callee.ExactDefinitionAcquisitionGuard);
    }

    [Theory]
    [InlineData("ReferenceCast")]
    [InlineData("Not")]
    [InlineData("NotFloatComparison")]
    [InlineData("CoalesceCall")]
    [InlineData("CoalesceBooleanArgument")]
    public void ClassicInverseExpressionBridgeBudgetRemainsLoadBearing(string method)
    {
        using RequestScope scope = OpenExpressionRequest(method);
        var budget = new ClassicInverseBudget();
        Assert.IsType<ClassicInverseDecision.Reconstruct>(
            ClassicInverseCore.Decide(scope.Request, budget));
        var failed = Assert.IsType<ClassicInverseDecision.Failed>(
            ClassicInverseCore.Decide(scope.Request, new ClassicInverseBudget(budget.Consumed - 1)));
        Assert.Equal(ClassicInverseFailureKind.BudgetExhausted, failed.Failure.Kind);
        Assert.IsType<ClassicInverseDecision.Reconstruct>(
            ClassicInverseCore.Decide(scope.Request, new ClassicInverseBudget(budget.Consumed)));
    }
}
