using ILInspector.Decompiler.Pipeline;
using ILInspector.DecompilerHarness;
using DotnetInspector.Fixtures;

namespace ILInspector.Decompiler.Tests;

public sealed partial class ClassicInverseCoreTests
{
    [Theory]
    [InlineData("ByteArgument")]
    [InlineData("ByteMinimumArgument")]
    [InlineData("ByteMaximumArgument")]
    [InlineData("DecimalConstant")]
    [InlineData("CharacterArgument")]
    [InlineData("CharacterMaximumArgument")]
    [InlineData("EnumArgument")]
    [InlineData("LongEnumArgument")]
    [InlineData("ShortCircuitCall")]
    [InlineData("ShortCircuitOrCall")]
    [InlineData("NestedBooleanChoice")]
    [InlineData("NestedChoiceCalls")]
    [InlineData("ComposedSelectionTypes")]
    [InlineData("Concatenation")]
    [InlineData("IntReceiver")]
    [InlineData("LongReceiver")]
    [InlineData("BoolReceiver")]
    [InlineData("VariableAnd")]
    [InlineData("DateTimeReceiver")]
    [InlineData("DecimalReceiver")]
    [InlineData("NamedIntReceiver")]
    public void ClassicInversePreservesSinkAndCompositionExpressions(string method)
    {
        using RequestScope scope = OpenExpressionRequest(method);
        ClassicInverseDecision decision = ClassicInverseCore.Decide(scope.Request);
        Assert.True(decision is ClassicInverseDecision.Reconstruct, decision.ToString());
        BlockContainer body = ((ClassicInverseDecision.Reconstruct)decision).Plan.MaterializeBody();
        body.CheckInvariant();
        Assert.Single(body.Descendants.OfType<AwaitExpression>());
        DecompilerResult result = DecompileExpressionFixture(method);
        Assert.Equal(DecompilationFidelity.Full, result.Fidelity);
        Assert.True(result.RequiresAsyncBodyModifier);
        Assert.DoesNotContain(".Start<", result.Output, StringComparison.Ordinal);
    }

    [Theory]
    [Trait("Speed", "Slow")]
    [InlineData("ByteArgument")]
    [InlineData("ByteMinimumArgument")]
    [InlineData("ByteMaximumArgument")]
    [InlineData("DecimalConstant")]
    [InlineData("CharacterArgument")]
    [InlineData("CharacterMaximumArgument")]
    [InlineData("EnumArgument")]
    [InlineData("LongEnumArgument")]
    [InlineData("ShortCircuitCall")]
    [InlineData("ShortCircuitOrCall")]
    [InlineData("NestedBooleanChoice")]
    [InlineData("NestedChoiceCalls")]
    [InlineData("ComposedSelectionTypes")]
    [InlineData("Concatenation")]
    [InlineData("IntReceiver")]
    [InlineData("LongReceiver")]
    [InlineData("BoolReceiver")]
    [InlineData("VariableAnd")]
    [InlineData("DateTimeReceiver")]
    [InlineData("DecimalReceiver")]
    [InlineData("NamedIntReceiver")]
    public void ClassicInverseSinkAndCompositionOutputsCompileBack(string methodName)
    {
        var results = FidelityCheck.Evaluate(FixtureCatalog.DecompilerClassicAsync.AssemblyPath(),
            type => type == ExpressionFixtureType, method => method.Method == methodName);
        var result = Assert.Single(results);
        Assert.True(result.Status == FidelityCheck.CompileBackStatus.Exact,
            $"{result.Method}: {result.Status}: {result.Detail}\n"
                + (result.FidelityDiff is { Rows.IsDefault: false } diff
                    ? string.Join("\n", diff.Rows.Select(row => $"{row.Kind}: {row.Operation.Display}"))
                    : ""));
    }
}
