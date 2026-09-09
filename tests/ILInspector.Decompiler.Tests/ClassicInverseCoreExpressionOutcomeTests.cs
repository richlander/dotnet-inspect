using ILInspector.Decompiler.Pipeline;
using ILInspector.DecompilerHarness;
using DotnetInspector.Fixtures;

namespace ILInspector.Decompiler.Tests;

public sealed partial class ClassicInverseCoreTests
{
    [Theory]
    [InlineData("TypeEquality", "typeof(string)")]
    [InlineData("CoalesceTypeOf", "?? typeof(string)")]
    [InlineData("TypeArrayEquality", "typeof(string[])")]
    [InlineData("TypeGenericEquality", "typeof(Dictionary<string, int>)")]
    [InlineData("TypeArguments", "TypeChoice(await value, typeof(string), typeof(int))")]
    public void ClassicInversePreservesTypeOfExpressions(string method, string expected)
    {
        using RequestScope scope = OpenExpressionRequest(method);
        ClassicInverseDecision decision = ClassicInverseCore.Decide(scope.Request);
        Assert.True(decision is ClassicInverseDecision.Reconstruct, decision.ToString());
        BlockContainer body = ((ClassicInverseDecision.Reconstruct)decision).Plan.MaterializeBody();
        body.CheckInvariant();
        Assert.NotEmpty(body.Descendants.OfType<TypeOf>());
        DecompilerResult result = DecompileExpressionFixture(method);
        Assert.Equal(DecompilationFidelity.Full, result.Fidelity);
        Assert.Contains(expected, result.Output, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("BooleanChoice")]
    [InlineData("BooleanChoiceCalls")]
    [InlineData("NegatedBooleanChoice")]
    [InlineData("BooleanChoiceObjects")]
    [InlineData("BooleanChoiceTypeOf")]
    [InlineData("ComparisonChoice")]
    [InlineData("BooleanChoiceThenCall")]
    public void ClassicInversePreservesAwaitedPredicates(string method)
    {
        using RequestScope scope = OpenExpressionRequest(method);
        ClassicInverseDecision decision = ClassicInverseCore.Decide(scope.Request);
        Assert.True(decision is ClassicInverseDecision.Reconstruct, decision.ToString());
        BlockContainer body = ((ClassicInverseDecision.Reconstruct)decision).Plan.MaterializeBody();
        body.CheckInvariant();
        Conditional conditional = Assert.Single(body.Descendants.OfType<Conditional>());
        Assert.Single(conditional.Condition.Descendants.Prepend(conditional.Condition).OfType<AwaitExpression>());
        DecompilerResult result = DecompileExpressionFixture(method);
        Assert.Equal(DecompilationFidelity.Full, result.Fidelity);
        Assert.Contains("?", result.Output, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("NestedInitializer", "Child = { Value = 7 }")]
    [InlineData("NestedInitializerEntries", "Other = PositiveChoice()")]
    [InlineData("NestedInitializerTypeOf", "Kind = typeof(string)")]
    public void ClassicInversePreservesUsefulPartialInitializerSource(string method, string expected)
    {
        using RequestScope scope = OpenExpressionRequest(method);
        ClassicInverseDecision decision = ClassicInverseCore.Decide(scope.Request);
        Assert.True(decision is ClassicInverseDecision.Reconstruct, decision.ToString());
        BlockContainer body = ((ClassicInverseDecision.Reconstruct)decision).Plan.MaterializeBody();
        body.CheckInvariant();
        Assert.Null(Assert.Single(body.Descendants.OfType<InitializerBlock>()).ResultType);
        DecompilerResult result = DecompileExpressionFixture(method);
        Assert.Equal(DecompilationFidelity.Partial, result.Fidelity);
        Assert.True(result.RequiresAsyncBodyModifier);
        Assert.Contains("CombineInitialized(await value,", result.Output, StringComparison.Ordinal);
        Assert.Contains(expected, result.Output, StringComparison.Ordinal);
        Assert.DoesNotContain(".Start<", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void ClassicInverseKeepsUnresolvedCollectionInitializerVisible()
    {
        using RequestScope scope = OpenExpressionRequest("NestedCollectionInitializer");
        ClassicInversePlanningView planning = ClassicInversePlanningView.Derive(scope.Request);
        Assert.DoesNotContain(planning.ExecutionBody.Body.Descendants, node => node is InitializerBlock);
        Assert.IsType<ClassicInverseDecision.Decline>(ClassicInverseCore.Decide(scope.Request));
        DecompilerResult result = DecompileExpressionFixture("NestedCollectionInitializer");
        Assert.Equal(DecompilationFidelity.Partial, result.Fidelity);
        Assert.False(result.RequiresAsyncBodyModifier);
        Assert.Contains(".Start<", result.Output, StringComparison.Ordinal);
    }

    [Theory]
    [Trait("Speed", "Slow")]
    [InlineData("TypeEquality")]
    [InlineData("CoalesceTypeOf")]
    [InlineData("TypeArrayEquality")]
    [InlineData("TypeGenericEquality")]
    [InlineData("TypeArguments")]
    [InlineData("BooleanChoice")]
    [InlineData("BooleanChoiceCalls")]
    [InlineData("NegatedBooleanChoice")]
    [InlineData("BooleanChoiceObjects")]
    [InlineData("BooleanChoiceTypeOf")]
    [InlineData("ComparisonChoice")]
    [InlineData("BooleanChoiceThenCall")]
    [InlineData("NestedInitializer")]
    [InlineData("NestedInitializerEntries")]
    [InlineData("NestedCollectionInitializer")]
    [InlineData("NestedInitializerTypeOf")]
    public void ClassicInverseExtendedExpressionOutputsCompileBack(string methodName)
    {
        var results = FidelityCheck.Evaluate(FixtureCatalog.DecompilerClassicAsync.AssemblyPath(),
            type => type == ExpressionFixtureType, method => method.Method == methodName);
        var result = Assert.Single(results);
        Assert.True(result.Status == FidelityCheck.CompileBackStatus.Exact,
            $"{result.Method}: {result.Status}: {result.Detail}");
    }
}
