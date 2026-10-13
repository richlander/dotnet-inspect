using ILInspector.Decompiler.Pipeline;
using ILInspector.DecompilerHarness;

namespace ILInspector.Decompiler.Tests;

public sealed partial class ClassicInverseCoreTests
{
    [Theory]
    [InlineData("AwaitFieldReset")]
    [InlineData("GuardedAwaitFieldReset")]
    public void ClassicInversePreservesAwaitFieldReset(string name)
    {
        using var source = OpenClassicFixture();
        using var scope = OpenRequest(source, name, false, fixtureType: ContinuationFixtureType);
        Assert.IsType<ClassicInverseDecision.Reconstruct>(ClassicInverseCore.Decide(scope.Request));
        var fresh = IrImporter.Import(source, ContinuationFixtureType, name)!;
        var printed = CSharpPrinter.PrintRaised(fresh, method => IrImporter.Import(source, method));
        Assert.True(printed.Succeeded, string.Join("; ", printed.Diagnostics));
        Assert.Equal(DecompilationFidelity.Full, printed.Fidelity);
        Assert.Contains("_memory = default", printed.Output);
        Assert.Contains("Cleanup();", printed.Output);
        fresh.CheckInvariant();
    }

    [Theory]
    [InlineData("type", false)]
    [InlineData("type", true)]
    [InlineData("address", false)]
    [InlineData("address", true)]
    [InlineData("order", false)]
    [InlineData("order", true)]
    [InlineData("origin", false)]
    [InlineData("origin", true)]
    public void ClassicInverseAwaitFieldResetCannotBeHealed(string mutation, bool raw)
    {
        using var source = OpenClassicFixture();
        void Mutate(IrFunction body)
        {
            var init = body.Descendants.OfType<InitObject>().Single(node =>
                node.Address is LoadFieldAddress address && address.Field.Name == "_memory");
            if (mutation == "type")
            {
                var changed = new InitObject(TypeRef.CoreLib("System", "Int32"), (IrExpression)init.Address.Clone());
                changed.SetSourceOffset(init.SourceOffset);
                init.ReplaceWith(changed);
            }
            if (mutation == "address")
            {
                var field = body.Descendants.OfType<LoadFieldAddress>().First(address => address.Field.Name == "<>t__builder");
                init.Address.ReplaceWith(field.Clone());
            }
            if (mutation == "origin") init.SetSourceOffset(-1);
            if (mutation == "order")
            {
                var block = (Block)init.Parent!;
                var nodes = block.Children.ToArray();
                block.DetachChildren();
                block.Add(init);
                foreach (var node in nodes.Where(node => !ReferenceEquals(node, init))) block.Add(node);
            }
            body.CheckInvariant();
        }
        using var scope = OpenRequest(source, "AwaitFieldReset", false,
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
    public void ClassicInverseAwaitFieldResetPlanIsDetachedAndBudgeted()
    {
        using var source = OpenClassicFixture();
        using var scope = OpenRequest(source, "AwaitFieldReset", false, fixtureType: ContinuationFixtureType);
        var budget = new ClassicInverseBudget();
        var plan = Assert.IsType<ClassicInverseDecision.Reconstruct>(ClassicInverseCore.Decide(scope.Request, budget)).Plan;
        Assert.Equal(ClassicInverseFailureKind.BudgetExhausted,
            Assert.IsType<ClassicInverseDecision.Failed>(ClassicInverseCore.Decide(scope.Request,
                new ClassicInverseBudget(budget.Consumed - 1))).Failure.Kind);
        var first = plan.MaterializeBody();
        Assert.Single(first.Descendants.OfType<InitObject>()).Address.ReplaceWith(new Constant(0, TypeRef.CoreLib("System", "Int32")));
        scope.Request.ExecutionBody.Body.Blocks[0].DetachChildren();
        var fresh = plan.MaterializeBody();
        Assert.IsType<LoadFieldAddress>(Assert.Single(fresh.Descendants.OfType<InitObject>()).Address);
        fresh.CheckInvariant();
    }

    [Theory]
    [Trait("Speed", "Slow")]
    [Trait("Area", "Fidelity")]
    [InlineData("AwaitFieldReset")]
    [InlineData("GuardedAwaitFieldReset")]
    public void AwaitFieldReset_CompileBackExact(string name)
    {
        using var source = OpenClassicFixture();
        var result = Assert.Single(FidelityCheck.Evaluate(source.Path, false, FidelityCheck.ClusterMode.Off,
            type => type == ContinuationFixtureType, method => method.Method == name));
        Assert.True(result.Status == FidelityCheck.CompileBackStatus.Exact && result.UsedProductWholeMember,
            $"{result.Method}: {result.Status}: {result.Detail}");
    }
}
