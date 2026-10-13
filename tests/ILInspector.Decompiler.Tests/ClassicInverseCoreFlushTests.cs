using ILInspector.Decompiler.Pipeline;
using ILInspector.DecompilerHarness;

namespace ILInspector.Decompiler.Tests;

public sealed partial class ClassicInverseCoreTests
{
    const string FlushFixtureType = "ILInspector.Decompiler.Fixtures.ClassicAsync.FlushFixtures";

    [Theory]
    [InlineData("FlushConditional")]
    [InlineData("FlushConditionalValueTask")]
    public void ClassicInversePreservesConditionalVoidPair(string name)
    {
        using var source = OpenClassicFixture();
        using var scope = OpenRequest(source, name, false, fixtureType: FlushFixtureType);
        Assert.IsType<ClassicInverseDecision.Reconstruct>(ClassicInverseCore.Decide(scope.Request));
        var fresh = IrImporter.Import(source, FlushFixtureType, name)!;
        var printed = CSharpPrinter.PrintRaised(fresh, method => IrImporter.Import(source, method));
        Assert.True(printed.Succeeded, string.Join("; ", printed.Diagnostics));
        Assert.Equal(DecompilationFidelity.Full, printed.Fidelity);
        Assert.Contains("else", printed.Output);
        Assert.Contains("_memory = default", printed.Output);
        Assert.Equal(2, fresh.Descendants.OfType<AwaitExpression>().Count());
        fresh.CheckInvariant();
    }

    [Theory]
    [InlineData("target", false)]
    [InlineData("target", true)]
    [InlineData("predicate", false)]
    [InlineData("predicate", true)]
    [InlineData("external-entry", false)]
    [InlineData("external-entry", true)]
    [InlineData("prefix-type", false)]
    [InlineData("prefix-type", true)]
    [InlineData("setter-value", false)]
    [InlineData("setter-value", true)]
    [InlineData("effect-order", false)]
    [InlineData("effect-order", true)]
    [InlineData("optional-target", false)]
    [InlineData("optional-target", true)]
    [InlineData("alternative-target", false)]
    [InlineData("alternative-target", true)]
    [InlineData("setter-origin", false)]
    [InlineData("setter-origin", true)]
    [InlineData("receiver-escape", true)]
    [InlineData("completion", false)]
    [InlineData("completion", true)]
    public void ClassicInverseConditionalVoidPairCannotBeHealed(string mutation, bool raw)
    {
        using var source = OpenClassicFixture();
        void Mutate(IrFunction body)
        {
            var branch = body.Descendants.OfType<ConditionalBranch>().Single(node =>
                node.Condition.Descendants.OfType<LoadField>().Any(field => field.Field.Name == "_stream"));
            var block = (Block)branch.Parent!;
            if (mutation is "optional-target" or "alternative-target")
            {
                var candidates = body.Descendants.OfType<ConditionalBranch>().Where(node =>
                    node.Condition.Descendants.Any(child => child is LoadProperty { PropertyName: "Pending" }
                        or Call { Callee.Name: "get_Pending" })).ToArray();
                var selected = mutation == "optional-target" ? candidates[0] : candidates[1];
                var changed = new ConditionalBranch((IrExpression)selected.Condition.Clone(), block.StartOffset, selected.Origin);
                changed.SetSourceOffset(selected.SourceOffset);
                selected.ReplaceWith(changed);
            }
            if (mutation == "setter-origin")
            {
                IrNode setter = raw
                    ? body.Descendants.OfType<Call>().First(call => call.Callee.Name == "set_Pending")
                    : body.Descendants.OfType<StoreProperty>().First(store => store.PropertyName == "Pending");
                setter.SetSourceOffset(-1);
            }
            if (mutation == "receiver-escape")
            {
                var configure = body.Descendants.OfType<Call>().First(call =>
                    call.Callee.Name == "ConfigureAwait" && call.Arguments[0] is LoadLocalAddress);
                var address = (LoadLocalAddress)configure.Arguments[0];
                var statement = configure;
                var configuredStore = (StoreLocal)statement.Parent!;
                ((Block)configuredStore.Parent!).Add(new ExpressionStatement((IrExpression)address.Clone()));
            }
            if (mutation == "target")
            {
                var changed = new ConditionalBranch((IrExpression)branch.Condition.Clone(), block.StartOffset, branch.Origin);
                changed.SetSourceOffset(branch.SourceOffset);
                branch.ReplaceWith(changed);
            }
            if (mutation == "predicate") branch.Condition.ReplaceWith(new Constant(false, TypeRef.CoreLib("System", "Boolean")));
            if (mutation == "external-entry") ((BlockContainer)block.Parent!).Blocks[0].Add(new Branch(block.StartOffset));
            if (mutation == "prefix-type")
            {
                var init = block.Children.OfType<InitObject>().Single();
                var changed = new InitObject(TypeRef.CoreLib("System", "Int32"), (IrExpression)init.Address.Clone());
                changed.SetSourceOffset(init.SourceOffset);
                init.ReplaceWith(changed);
            }
            if (mutation == "setter-value")
            {
                IrNode value = raw
                    ? body.Descendants.OfType<Call>().First(call => call.Callee.Name == "set_Pending").Arguments.Last()
                    : body.Descendants.OfType<StoreProperty>().First(store => store.PropertyName == "Pending").Value;
                var changed = new Constant(1, TypeRef.CoreLib("System", "Int32"));
                changed.SetSourceOffset(value.SourceOffset);
                value.ReplaceWith(changed);
            }
            if (mutation == "effect-order")
            {
                var result = body.Descendants.OfType<ExpressionStatement>().First(node =>
                    node.Expression is Call { Callee.Name: "GetResult" });
                var continuation = (Block)result.Parent!;
                var nodes = continuation.Children.ToArray();
                continuation.DetachChildren();
                continuation.Add(nodes[1]);
                continuation.Add(nodes[0]);
                foreach (var node in nodes.Skip(2)) continuation.Add(node);
            }
            if (mutation == "completion")
            {
                if (raw)
                {
                    var completionBlock = body.Descendants.OfType<Block>().First(candidate => candidate.Children is [Leave]);
                    var leave = (Leave)completionBlock.Children[0];
                    var changed = new Leave(block.StartOffset);
                    changed.SetSourceOffset(leave.SourceOffset);
                    leave.ReplaceWith(changed);
                }
                else
                {
                    body.Descendants.OfType<StoreField>().Last(store =>
                        store.Field.Name == "<>1__state" && store.Value is Constant { Value: -2 }).SetSourceOffset(-1);
                }
            }
            body.CheckInvariant();
        }
        using var scope = OpenRequest(source, "FlushConditional", false,
            mutateExecution: raw ? Mutate : null, fixtureType: FlushFixtureType);
        var request = CopyRequest(scope.Request, runPasses: (body, passes) =>
        {
            scope.Request.RunPasses!(body, passes);
            if (body.Name != "MoveNext") return;
            if (!raw) Mutate(body);
            else if (mutation == "setter-value")
            {
                // Simulate a planning regression that heals the changed raw
                // assigned value back to the original fixture's zero.
                var value = body.Descendants.OfType<StoreProperty>().First(store => store.PropertyName == "Pending").Value;
                var original = new Constant(0, TypeRef.CoreLib("System", "Int32"));
                original.SetSourceOffset(value.SourceOffset);
                value.ReplaceWith(original);
            }
        });
        Assert.IsType<ClassicInverseDecision.Decline>(ClassicInverseCore.Decide(request));
    }

    [Fact]
    public void ClassicInverseConditionalVoidPairPlanIsDetachedAndBudgeted()
    {
        using var source = OpenClassicFixture();
        using var scope = OpenRequest(source, "FlushConditional", false, fixtureType: FlushFixtureType);
        var budget = new ClassicInverseBudget();
        var plan = Assert.IsType<ClassicInverseDecision.Reconstruct>(ClassicInverseCore.Decide(scope.Request, budget)).Plan;
        Assert.Equal(ClassicInverseFailureKind.BudgetExhausted,
            Assert.IsType<ClassicInverseDecision.Failed>(ClassicInverseCore.Decide(scope.Request,
                new ClassicInverseBudget(budget.Consumed - 1))).Failure.Kind);
        var first = plan.MaterializeBody();
        var branch = first.Descendants.OfType<IfStatement>().Single(node => node.HasElse);
        branch.Else!.DetachChildren();
        var store = first.Descendants.OfType<StoreProperty>().First();
        store.Value.ReplaceWith(new Constant(17, TypeRef.CoreLib("System", "Int32")));
        scope.Request.ExecutionBody.Body.Blocks[0].DetachChildren();
        var fresh = plan.MaterializeBody();
        Assert.NotEmpty(fresh.Descendants.OfType<IfStatement>().Single(node => node.HasElse).Else!.Children);
        Assert.Equal(0, Assert.IsType<Constant>(fresh.Descendants.OfType<StoreProperty>().First().Value).Value);
        fresh.CheckInvariant();
    }

    [Theory]
    [Trait("Speed", "Slow")]
    [Trait("Area", "Fidelity")]
    [InlineData("FlushConditional")]
    [InlineData("FlushConditionalValueTask")]
    public void ConditionalVoidPair_CompileBackExact(string name)
    {
        using var source = OpenClassicFixture();
        var result = Assert.Single(FidelityCheck.Evaluate(source.Path, false, FidelityCheck.ClusterMode.Off,
            type => type == FlushFixtureType, method => method.Method == name));
        Assert.True(result.Status == FidelityCheck.CompileBackStatus.Exact && result.UsedProductWholeMember,
            $"{result.Method}: {result.Status}: {result.Detail}");
    }
}
