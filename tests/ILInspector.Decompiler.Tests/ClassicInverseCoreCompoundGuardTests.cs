using ILInspector.Decompiler.Pipeline;
using ILInspector.DecompilerHarness;

namespace ILInspector.Decompiler.Tests;

public sealed partial class ClassicInverseCoreTests
{
    [Theory]
    [InlineData("ReferenceGuardedAwait")]
    [InlineData("CompoundReceiverCleanup")]
    public void ClassicInversePreservesCompoundReferenceGuard(string name)
    {
        using var source = OpenClassicFixture();
        using var scope = OpenRequest(source, name, false, fixtureType: ContinuationFixtureType);
        Assert.IsType<ClassicInverseDecision.Reconstruct>(ClassicInverseCore.Decide(scope.Request));
        var fresh = IrImporter.Import(source, ContinuationFixtureType, name)!;
        var printed = CSharpPrinter.PrintRaised(fresh, method => IrImporter.Import(source, method));
        Assert.True(printed.Succeeded, string.Join("; ", printed.Diagnostics));
        Assert.Equal(DecompilationFidelity.Full, printed.Fidelity);
        Assert.Contains("||", printed.Output);
        Assert.Contains("await", printed.Output);
        fresh.CheckInvariant();
    }

    [Theory]
    [InlineData("nested", false)]
    [InlineData("nested", true)]
    [InlineData("completion", false)]
    [InlineData("completion", true)]
    [InlineData("target", false)]
    [InlineData("target", true)]
    [InlineData("predicate", false)]
    [InlineData("predicate", true)]
    [InlineData("external-entry", false)]
    [InlineData("external-entry", true)]
    [InlineData("extra-effect", false)]
    [InlineData("extra-effect", true)]
    [InlineData("initialization-type", false)]
    [InlineData("initialization-type", true)]
    [InlineData("initialization-use", false)]
    [InlineData("initialization-use", true)]
    [InlineData("initialization-order", false)]
    [InlineData("initialization-order", true)]
    public void ClassicInverseCompoundGuardCannotBeHealed(string mutation, bool raw)
    {
        using var source = OpenClassicFixture();
        void Mutate(IrFunction body)
        {
            var branch = body.Descendants.OfType<ConditionalBranch>().Single(node =>
                node.Condition is LoadField field && field.Field.Name == "_second");
            var block = (Block)branch.Parent!;
            if (mutation == "target")
            {
                var replacement = new ConditionalBranch((IrExpression)branch.Condition.Clone(), block.StartOffset, branch.Origin);
                replacement.SetSourceOffset(branch.SourceOffset);
                branch.ReplaceWith(replacement);
            }
            if (mutation == "predicate") branch.Condition.ReplaceWith(new LogicalNot((IrExpression)branch.Condition.Clone()));
            if (mutation == "external-entry") ((BlockContainer)block.Parent!).Blocks[0].Add(new Branch(block.StartOffset));
            if (mutation == "extra-effect") block.Add(new ExpressionStatement((IrExpression)branch.Condition.Clone()));
            if (mutation == "nested")
            {
                block.DetachChildren();
                var nested = new Block();
                nested.Add(branch);
                block.Add(new IfStatement(new Constant(true, TypeRef.CoreLib("System", "Boolean")), nested, null));
            }
            if (mutation == "completion")
            {
                var container = (BlockContainer)block.Parent!;
                var earlyBlock = container.Blocks.Single(candidate => candidate.Children is [Leave]);
                var leave = Assert.IsType<Leave>(Assert.Single(earlyBlock.Children));
                var replacement = new Leave(branch.TargetOffset);
                replacement.SetSourceOffset(leave.SourceOffset);
                leave.ReplaceWith(replacement);
            }
            var init = body.Descendants.OfType<InitObject>().Single(node => node.Address is LoadLocalAddress);
            var initialized = (Block)init.Parent!;
            var local = (LoadLocalAddress)init.Address;
            if (mutation == "initialization-type")
            {
                var replacement = new InitObject(TypeRef.CoreLib("System", "Int32"), (IrExpression)local.Clone());
                replacement.SetSourceOffset(init.SourceOffset);
                init.ReplaceWith(replacement);
            }
            if (mutation == "initialization-use") initialized.Add(new ExpressionStatement(new LoadLocalAddress(local.Index, local.Type)));
            if (mutation == "initialization-order")
            {
                var nodes = initialized.Children.ToArray();
                initialized.DetachChildren();
                foreach (var node in nodes.Skip(1)) initialized.Add(node);
                initialized.Add(init);
            }
            body.CheckInvariant();
        }
        using var scope = OpenRequest(source, "CompoundReceiverCleanup", false,
            mutateExecution: raw ? Mutate : null, fixtureType: ContinuationFixtureType);
        var request = raw ? scope.Request : CopyRequest(scope.Request,
            runPasses: (body, passes) =>
            {
                scope.Request.RunPasses!(body, passes);
                if (body.Name == "MoveNext") Mutate(body);
            });
        Assert.IsType<ClassicInverseDecision.Decline>(ClassicInverseCore.Decide(request));
    }

    [Fact]
    public void ClassicInverseCompoundGuardPlanIsDetachedAndBudgeted()
    {
        using var source = OpenClassicFixture();
        using var scope = OpenRequest(source, "CompoundReceiverCleanup", false, fixtureType: ContinuationFixtureType);
        var budget = new ClassicInverseBudget();
        var plan = Assert.IsType<ClassicInverseDecision.Reconstruct>(ClassicInverseCore.Decide(scope.Request, budget)).Plan;
        var failed = Assert.IsType<ClassicInverseDecision.Failed>(ClassicInverseCore.Decide(scope.Request,
            new ClassicInverseBudget(budget.Consumed - 1)));
        Assert.Equal(ClassicInverseFailureKind.BudgetExhausted, failed.Failure.Kind);
        var first = plan.MaterializeBody();
        var init = Assert.Single(first.Descendants.OfType<InitObject>());
        init.Address.ReplaceWith(new LoadLocalAddress(99, init.Type));
        var logical = Assert.Single(first.Descendants.OfType<LogicalBinary>());
        logical.Right.ReplaceWith(new Constant(false, TypeRef.CoreLib("System", "Boolean")));
        scope.Request.ExecutionBody.Body.Blocks[0].DetachChildren();
        var fresh = plan.MaterializeBody();
        Assert.NotEqual(99, Assert.IsType<LoadLocalAddress>(Assert.Single(fresh.Descendants.OfType<InitObject>()).Address).Index);
        Assert.IsType<LoadField>(Assert.Single(fresh.Descendants.OfType<LogicalBinary>()).Right);
        fresh.CheckInvariant();
    }

    [Theory]
    [Trait("Speed", "Slow")]
    [Trait("Area", "Fidelity")]
    [InlineData("ReferenceGuardedAwait")]
    [InlineData("CompoundReceiverCleanup")]
    public void CompoundReferenceGuards_CompileBackExact(string name)
    {
        using var source = OpenClassicFixture();
        var result = Assert.Single(FidelityCheck.Evaluate(source.Path, false, FidelityCheck.ClusterMode.Off,
            type => type == ContinuationFixtureType, method => method.Method == name));
        Assert.True(result.Status == FidelityCheck.CompileBackStatus.Exact && result.UsedProductWholeMember,
            $"{result.Method}: {result.Status}: {result.Detail}");
    }
}
