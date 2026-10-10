using System.Collections.Immutable;
using ILInspector.Decompiler.Pipeline;
using ILInspector.DecompilerHarness;

namespace ILInspector.Decompiler.Tests;

public sealed partial class ClassicInverseCoreTests
{
    [Theory]
    [InlineData("GuardedReceiverCleanup")]
    [InlineData("GuardedAwait")]
    [InlineData("InvertedGuardedAwait")]
    public void ClassicInversePreservesSingleAwaitGuard(string method)
    {
        using var source = OpenClassicFixture();
        using var scope = OpenRequest(source, method, false, fixtureType: ContinuationFixtureType);
        var decision = ClassicInverseCore.Decide(scope.Request);
        Assert.True(decision is ClassicInverseDecision.Reconstruct, decision.ToString());
        var plan = ((ClassicInverseDecision.Reconstruct)decision).Plan;
        Assert.Single(plan.MaterializeBody().Descendants.OfType<IfStatement>());
        var function = IrImporter.Import(source, ContinuationFixtureType, method)!;
        var printed = CSharpPrinter.PrintRaised(function, reference => IrImporter.Import(source, reference));
        Assert.True(printed.Succeeded, string.Join("; ", printed.Diagnostics));
        Assert.Equal(DecompilationFidelity.Full, printed.Fidelity);
        Assert.Contains(method == "InvertedGuardedAwait" ? "if (!disabled)" : "if (enabled)", printed.Output);
        Assert.Contains("await Work()", printed.Output);
        if (method == "GuardedReceiverCleanup") Assert.Contains("Cleanup();", printed.Output);
        function.CheckInvariant();
    }

    [Fact]
    public void ClassicInverseDeclinesCompoundAwaitGuard()
    {
        using var source = OpenClassicFixture();
        using var scope = OpenRequest(source, "CompoundGuardedAwait", false, fixtureType: ContinuationFixtureType);
        Assert.IsType<ClassicInverseDecision.Decline>(ClassicInverseCore.Decide(scope.Request));
        scope.Request.ExecutionBody.CheckInvariant();
    }

    [Theory]
    [InlineData("target")]
    [InlineData("condition")]
    [InlineData("completion")]
    [InlineData("extra-effect")]
    [InlineData("external-entry")]
    [InlineData("nested")]
    [InlineData("completion-origin")]
    public void ClassicInverseAwaitGuardCannotBeHealedByPlanning(string mutation)
    {
        using var source = OpenClassicFixture();
        using var scope = OpenRequest(source, "GuardedReceiverCleanup", false, fixtureType: ContinuationFixtureType);
        var run = Assert.IsType<Action<IrFunction, ImmutableArray<IIrPass>>>(scope.Request.RunPasses);
        var changed = CopyRequest(scope.Request, runPasses: (body, passes) =>
        {
            run(body, passes);
            var branch = body.Descendants.OfType<ConditionalBranch>().SingleOrDefault(node =>
                node.Condition is LoadField field && field.Field.Name == "enabled");
            if (branch is null) return;
            var block = (Block)branch.Parent!;
            var container = (BlockContainer)block.Parent!;
            int index = container.Blocks.ToList().IndexOf(block);
            if (mutation == "target")
            {
                var replacement = new ConditionalBranch((IrExpression)branch.Condition.Clone(),
                    container.Blocks[index + 1].StartOffset, branch.Origin);
                replacement.SetSourceOffset(branch.SourceOffset);
                branch.ReplaceWith(replacement);
            }
            if (mutation == "condition") branch.Condition.ReplaceWith(new LogicalNot((IrExpression)branch.Condition.Clone()));
            if (mutation == "completion")
            {
                var leave = Assert.IsType<Leave>(Assert.Single(container.Blocks[index + 1].Children));
                var replacement = new Leave(branch.TargetOffset);
                replacement.SetSourceOffset(leave.SourceOffset);
                leave.ReplaceWith(replacement);
            }
            if (mutation == "extra-effect")
            {
                var cleanup = body.Descendants.OfType<ExpressionStatement>().Single(node =>
                    node.Expression is Call call && call.Callee.Name == "Cleanup");
                block.Add(cleanup.Clone());
            }
            if (mutation == "external-entry") container.Blocks[0].Add(new Branch(branch.TargetOffset));
            if (mutation == "nested")
            {
                var nested = new Block();
                block.DetachChildren();
                nested.Add(branch);
                block.Add(new IfStatement(new Constant(true, branch.Condition.ResultType!), nested, null));
            }
            if (mutation == "completion-origin")
            {
                var completion = body.Descendants.OfType<StoreField>().Last(store =>
                    store.Field.Name == "<>1__state" && store.Value is Constant { Value: -2 });
                var replacement = new StoreField(completion.Field, (IrExpression)completion.Instance!.Clone(),
                    (IrExpression)completion.Value.Clone());
                replacement.SetSourceOffset(completion.SourceOffset);
                completion.ReplaceWith(replacement);
            }
            body.CheckInvariant();
        });
        Assert.IsType<ClassicInverseDecision.Decline>(ClassicInverseCore.Decide(changed));
    }

    [Theory]
    [InlineData("target")]
    [InlineData("completion")]
    [InlineData("external-entry")]
    public void ClassicInverseRawAwaitGuardCannotBeHealed(string mutation)
    {
        using var source = OpenClassicFixture();
        using var scope = OpenRequest(source, "GuardedReceiverCleanup", false, mutateExecution: body =>
        {
            var guard = body.Descendants.OfType<ConditionalBranch>().Single(node =>
                node.Condition is LoadField field && field.Field.Name == "enabled");
            var block = (Block)guard.Parent!;
            var container = (BlockContainer)block.Parent!;
            int index = container.Blocks.ToList().IndexOf(block);
            if (mutation == "target")
            {
                var replacement = new ConditionalBranch((IrExpression)guard.Condition.Clone(),
                    container.Blocks[index + 1].StartOffset, guard.Origin);
                replacement.SetSourceOffset(guard.SourceOffset);
                guard.ReplaceWith(replacement);
            }
            if (mutation == "completion")
            {
                var leave = Assert.IsType<Leave>(Assert.Single(container.Blocks[index + 1].Children));
                var replacement = new Leave(guard.TargetOffset);
                replacement.SetSourceOffset(leave.SourceOffset);
                leave.ReplaceWith(replacement);
            }
            if (mutation == "external-entry") container.Blocks[0].Add(new Branch(guard.TargetOffset));
            body.CheckInvariant();
        }, fixtureType: ContinuationFixtureType);
        Assert.IsType<ClassicInverseDecision.Decline>(ClassicInverseCore.Decide(scope.Request));
    }

    [Fact]
    public void ClassicInverseGuardCompletionRetainsImportedStoreOrigin()
    {
        using var source = OpenClassicFixture();
        using var scope = OpenRequest(source, "GuardedReceiverCleanup", false, fixtureType: ContinuationFixtureType);
        var planning = ClassicInversePlanningView.Derive(scope.Request);
        var original = scope.Request.ExecutionBody.Descendants.OfType<StoreField>().Last(store =>
            store.Field.Name == "<>1__state" && store.Value is Constant { Value: -2 });
        var retained = planning.ExecutionBody.Descendants.OfType<StoreField>().Last(store =>
            store.Field.Name == "<>1__state" && store.Value is Constant { Value: -2 });
        Assert.NotEqual(original.SourceOffset, retained.SourceOffset);
        Assert.Contains(original.SourceOffset, retained.ProvenanceOffsets);
        Assert.Contains(original.SourceOffset, retained.Clone().ProvenanceOffsets);
        Reconstruct(scope.Request);
        planning.ExecutionBody.CheckInvariant();
    }

    [Fact]
    public void ClassicInverseAwaitGuardPlanIsDetachedAndBudgeted()
    {
        using var source = OpenClassicFixture();
        using var scope = OpenRequest(source, "GuardedReceiverCleanup", false, fixtureType: ContinuationFixtureType);
        var budget = new ClassicInverseBudget();
        var plan = Assert.IsType<ClassicInverseDecision.Reconstruct>(ClassicInverseCore.Decide(scope.Request, budget)).Plan;
        var failure = Assert.IsType<ClassicInverseDecision.Failed>(
            ClassicInverseCore.Decide(scope.Request, new ClassicInverseBudget(budget.Consumed - 1)));
        Assert.Equal(ClassicInverseFailureKind.BudgetExhausted, failure.Failure.Kind);
        var first = plan.MaterializeBody();
        var guard = Assert.Single(first.Descendants.OfType<IfStatement>());
        guard.Condition.ReplaceWith(new LogicalNot((IrExpression)guard.Condition.Clone()));
        scope.Request.ExecutionBody.Body.Blocks[0].DetachChildren();
        var fresh = plan.MaterializeBody();
        Assert.IsType<LoadArgument>(Assert.Single(fresh.Descendants.OfType<IfStatement>()).Condition);
        Assert.NotSame(first, fresh);
    }

    [Theory]
    [InlineData("GuardedReceiverCleanup")]
    [InlineData("GuardedAwait")]
    [InlineData("InvertedGuardedAwait")]
    [Trait("Speed", "Slow")]
    [Trait("Area", "Fidelity")]
    public void SingleAwaitGuards_CompileBackExact(string methodName)
    {
        using var source = OpenClassicFixture();
        var result = Assert.Single(FidelityCheck.Evaluate(source.Path, false, FidelityCheck.ClusterMode.Off,
            type => type == ContinuationFixtureType, method => method.Method == methodName));
        Assert.True(result.Status == FidelityCheck.CompileBackStatus.Exact && result.UsedProductWholeMember,
            $"{result.Method}: {result.Status}: {result.Detail}");
    }
}
