using System.Collections.Immutable;
using ILInspector.Decompiler.Pipeline;
using ILInspector.DecompilerHarness;

namespace ILInspector.Decompiler.Tests;

public sealed partial class ClassicInverseCoreTests
{
    const string ContinuationFixtureType = "ILInspector.Decompiler.Fixtures.ClassicAsync.ContinuationFixtures";

    [Fact]
    public void ClassicInversePreservesAwaitContinuationCleanup()
    {
        using var source = OpenClassicFixture();
        using var scope = OpenRequest(source, "AwaitReceiverCleanup", false, fixtureType: ContinuationFixtureType);
        Reconstruct(scope.Request);
        var function = IrImporter.Import(source, ContinuationFixtureType, "AwaitReceiverCleanup")!;
        var printed = CSharpPrinter.PrintRaised(function, reference => IrImporter.Import(source, reference));
        Assert.True(printed.Succeeded, string.Join("; ", printed.Diagnostics));
        Assert.Equal(DecompilationFidelity.Full, printed.Fidelity);
        var text = Assert.IsType<string>(printed.Output);
        Assert.Contains("await Work()", text);
        Assert.Contains("Cleanup();", printed.Output);
        Assert.Contains("_first = null", printed.Output);
        Assert.Contains("_second = null", printed.Output);
        Assert.True(text.IndexOf("Cleanup();", StringComparison.Ordinal)
            < text.IndexOf("_first = null", StringComparison.Ordinal));
        function.CheckInvariant();
    }

    [Theory]
    [InlineData("order")]
    [InlineData("omit")]
    [InlineData("value")]
    [InlineData("completion")]
    [InlineData("field")]
    [InlineData("volatile")]
    public void ClassicInverseContinuationCannotBeHealedByPlanning(string mutation)
    {
        using var source = OpenClassicFixture();
        using var scope = OpenRequest(source, "AwaitReceiverCleanup", false, fixtureType: ContinuationFixtureType);
        var run = Assert.IsType<Action<IrFunction, ImmutableArray<IIrPass>>>(scope.Request.RunPasses);
        var changed = CopyRequest(scope.Request, runPasses: (body, passes) =>
        {
            run(body, passes);
            var first = body.Descendants.OfType<StoreField>().SingleOrDefault(store => store.Field.Name == "_first");
            if (first is null) return;
            var block = (Block)first.Parent!;
            var children = block.DetachChildren().ToList();
            int index = children.IndexOf(first);
            if (mutation == "order") (children[index], children[index + 1]) = (children[index + 1], children[index]);
            if (mutation == "omit") children.Remove(first);
            if (mutation == "value")
            {
                var replacement = new StoreField(first.Field, (IrExpression?)first.Instance?.Clone(),
                    new Constant("changed", first.Field.Type!));
                replacement.SetSourceOffset(first.SourceOffset);
                children[index] = replacement;
            }
            if (mutation is "field" or "volatile")
            {
                var field = mutation == "field" ? ((StoreField)children[index + 1]).Field : first.Field;
                var replacement = new StoreField(field, (IrExpression?)first.Instance?.Clone(),
                    (IrExpression)first.Value.Clone()) { IsVolatile = mutation == "volatile" };
                replacement.SetSourceOffset(first.SourceOffset);
                children[index] = replacement;
            }
            if (mutation == "completion") children.Add(new Leave(0));
            foreach (var child in children) block.Add(child);
        });
        Assert.IsType<ClassicInverseDecision.Decline>(ClassicInverseCore.Decide(changed));
    }

    [Fact]
    public void ClassicInverseContinuationPlanIsDetachedAndBudgeted()
    {
        using var source = OpenClassicFixture();
        using var scope = OpenRequest(source, "AwaitReceiverCleanup", false, fixtureType: ContinuationFixtureType);
        var budget = new ClassicInverseBudget();
        var plan = Assert.IsType<ClassicInverseDecision.Reconstruct>(ClassicInverseCore.Decide(scope.Request, budget)).Plan;
        var failure = Assert.IsType<ClassicInverseDecision.Failed>(
            ClassicInverseCore.Decide(scope.Request, new ClassicInverseBudget(budget.Consumed - 1)));
        Assert.Equal(ClassicInverseFailureKind.BudgetExhausted, failure.Failure.Kind);
        var materialized = plan.MaterializeBody();
        var expectedFields = materialized.Descendants.OfType<StoreField>().Select(store => store.Field).ToArray();
        scope.Request.ExecutionBody.Body.Blocks[0].DetachChildren();
        var store = materialized.Descendants.OfType<StoreField>().First();
        store.Value.ReplaceWith(new Constant("changed", store.Field.Type!));
        var fresh = plan.MaterializeBody();
        Assert.NotSame(materialized, fresh);
        var freshStores = fresh.Descendants.OfType<StoreField>().ToArray();
        Assert.Equal(expectedFields, freshStores.Select(store => store.Field));
        Assert.All(freshStores, freshStore => Assert.Null(Assert.IsType<Constant>(freshStore.Value).Value));
    }

    [Fact]
    [Trait("Speed", "Slow")]
    [Trait("Area", "Fidelity")]
    public void AwaitContinuationCleanup_CompileBackExact()
    {
        using var source = OpenClassicFixture();
        var results = FidelityCheck.Evaluate(source.Path, false, FidelityCheck.ClusterMode.Off,
            type => type == ContinuationFixtureType, method => method.Method == "AwaitReceiverCleanup").ToArray();
        var result = Assert.Single(results);
        Assert.True(result.Status == FidelityCheck.CompileBackStatus.Exact && result.UsedProductWholeMember,
            $"{result.Method}: {result.Status}: {result.Detail}");
    }
}
