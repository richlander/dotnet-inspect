using ILInspector.Decompiler.Pipeline;
using ILInspector.DecompilerHarness;
using DotnetInspector.Fixtures;

namespace ILInspector.Decompiler.Tests;

public sealed partial class ClassicInverseCoreTests
{
    [Theory]
    [InlineData("InterpolatedOperand")]
    [InlineData("InterpolatedFormats")]
    [InlineData("InterpolatedZeroAlignment")]
    [InlineData("InterpolatedNegativeAlignment")]
    [InlineData("InterpolatedEffects")]
    [InlineData("InterpolatedGeneric")]
    [InlineData("InterpolatedEscapedFormat")]
    [InlineData("YieldOnce")]
    [InlineData("StructOperand")]
    [InlineData("MutableStructOperand")]
    [InlineData("StructFactoryOperand")]
    [InlineData("ClassAwaitableOperand")]
    public void ClassicInversePreservesInterpolationAndValueBinds(string method)
    {
        using RequestScope scope = OpenExpressionRequest(method);
        ClassicInverseDecision decision = ClassicInverseCore.Decide(scope.Request);
        Assert.True(decision is ClassicInverseDecision.Reconstruct, decision.ToString());
        BlockContainer body = ((ClassicInverseDecision.Reconstruct)decision).Plan.MaterializeBody();
        Assert.NotEmpty(body.Descendants.OfType<AwaitExpression>());
        body.CheckInvariant();
        DecompilerResult result = DecompileExpressionFixture(method);
        Assert.Equal(DecompilationFidelity.Full, result.Fidelity);
        Assert.True(result.RequiresAsyncBodyModifier);
    }

    [Theory]
    [Trait("Speed", "Slow")]
    [InlineData("InterpolatedOperand")]
    [InlineData("InterpolatedFormats")]
    [InlineData("InterpolatedZeroAlignment")]
    [InlineData("InterpolatedNegativeAlignment")]
    [InlineData("InterpolatedEffects")]
    [InlineData("InterpolatedGeneric")]
    [InlineData("InterpolatedEscapedFormat")]
    [InlineData("YieldOnce")]
    [InlineData("StructOperand")]
    [InlineData("MutableStructOperand")]
    [InlineData("StructFactoryOperand")]
    [InlineData("ClassAwaitableOperand")]
    public void ClassicInverseInterpolationAndValueBindsCompileBack(string method)
    {
        var result = Assert.Single(FidelityCheck.Evaluate(FixtureCatalog.DecompilerClassicAsync.AssemblyPath(),
            type => type == ExpressionFixtureType, candidate => candidate.Method == method));
        Assert.True(result.Status == FidelityCheck.CompileBackStatus.Exact,
            $"{result.Method}: {result.Status}: {result.Detail}");
    }
}
