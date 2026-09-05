using ILInspector.Decompiler.Pipeline;
using ILInspector.DecompilerHarness;
using DotnetInspector.Fixtures;

namespace ILInspector.Decompiler.Tests;

public sealed partial class ClassicInverseCoreTests
{
    [Theory]
    [InlineData("CharPropertyInitializer")]
    [InlineData("CharFieldInitializer")]
    [InlineData("BoolPropertyInitializer")]
    [InlineData("EnumPropertyInitializer")]
    [InlineData("NestedCharInitializer")]
    [InlineData("DefaultGuidInitializer")]
    [InlineData("DefaultDateTimeInitializer")]
    [InlineData("GenericAwait")]
    [InlineData("GenericArrayAwait")]
    [InlineData("GenericConstruct")]
    [InlineData("NullableDefaultReceiver")]
    [InlineData("NullableIntReceiver")]
    [InlineData("NullableGuidReceiver")]
    [InlineData("NullableBoolReceiver")]
    [InlineData("NamedNullableReceiver")]
    [InlineData("AwaitTuple")]
    [InlineData("AwaitNestedTuple")]
    [InlineData("AwaitTupleEffect")]
    [InlineData("MixedAndOr")]
    [InlineData("ShortCircuitBoth")]
    [InlineData("GenericCall")]
    [InlineData("AwaitLongTuple")]
    [InlineData("AndThenOr")]
    [InlineData("AwaitTypedTuple")]
    public void ClassicInversePreservesRetainedExpressionConsumers(string method)
    {
        using RequestScope scope = OpenExpressionRequest(method);
        ClassicInverseDecision decision = ClassicInverseCore.Decide(scope.Request);
        Assert.True(decision is ClassicInverseDecision.Reconstruct, decision.ToString());
        BlockContainer body = ((ClassicInverseDecision.Reconstruct)decision).Plan.MaterializeBody();
        body.CheckInvariant();
        Assert.Single(body.Descendants.OfType<AwaitExpression>());
        DecompilerResult result = DecompileExpressionFixture(method);
        Assert.True(result.RequiresAsyncBodyModifier);
        Assert.DoesNotContain(".Start<", result.Output, StringComparison.Ordinal);
        Assert.Equal(method == "NestedCharInitializer" ? DecompilationFidelity.Partial
            : DecompilationFidelity.Full, result.Fidelity);
    }

    [Theory]
    [Trait("Speed", "Slow")]
    [InlineData("CharPropertyInitializer")]
    [InlineData("CharFieldInitializer")]
    [InlineData("BoolPropertyInitializer")]
    [InlineData("EnumPropertyInitializer")]
    [InlineData("NestedCharInitializer")]
    [InlineData("CharCollectionInitializer")]
    [InlineData("DefaultGuidInitializer")]
    [InlineData("DefaultDateTimeInitializer")]
    [InlineData("GenericAwait")]
    [InlineData("GenericArrayAwait")]
    [InlineData("GenericConstruct")]
    [InlineData("NullableDefaultReceiver")]
    [InlineData("NullableIntReceiver")]
    [InlineData("NullableGuidReceiver")]
    [InlineData("NullableBoolReceiver")]
    [InlineData("NamedNullableReceiver")]
    [InlineData("AwaitTuple")]
    [InlineData("AwaitNestedTuple")]
    [InlineData("AwaitTupleEffect")]
    [InlineData("MixedAndOr")]
    [InlineData("ShortCircuitBoth")]
    [InlineData("GenericCall")]
    [InlineData("DefaultGuidCollectionInitializer")]
    [InlineData("MultiArgumentCollectionInitializer")]
    [InlineData("AwaitLongTuple")]
    [InlineData("AndThenOr")]
    [InlineData("AwaitTypedTuple")]
    public void ClassicInverseRetainedExpressionOutputsCompileBack(string methodName)
    {
        var result = Assert.Single(FidelityCheck.Evaluate(FixtureCatalog.DecompilerClassicAsync.AssemblyPath(),
            type => type == ExpressionFixtureType, method => method.Method == methodName));
        Assert.True(result.Status == FidelityCheck.CompileBackStatus.Exact,
            $"{result.Method}: {result.Status}: {result.Detail}\n"
                + (result.FidelityDiff is { Rows.IsDefault: false } diff
                    ? string.Join("\n", diff.Rows.Select(row => $"{row.Kind}: {row.Operation.Display}")) : ""));
    }
}
