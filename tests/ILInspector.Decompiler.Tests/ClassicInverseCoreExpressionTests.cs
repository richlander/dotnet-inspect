using ILInspector.Decompiler.Pipeline;
using Convert = ILInspector.Decompiler.Pipeline.Convert;

namespace ILInspector.Decompiler.Tests;

public sealed partial class ClassicInverseCoreTests
{
    const string ExpressionFixtureType =
        "ILInspector.Decompiler.Fixtures.ClassicAsync.AsyncExpressionFixtures";

    [Theory]
    [InlineData("WidenBeforeAdd", "(long)(await a) + (long)b", false)]
    [InlineData("WidenBeforeDivide", "(double)(await a) / (double)b", false)]
    [InlineData("CheckedWidenBeforeAdd", "(long)(await a) + (long)b", true)]
    public void ClassicInversePreservesArithmeticConversions(
        string method, string expected, bool isChecked)
    {
        using RequestScope scope = OpenExpressionRequest(method);
        BlockContainer body = Reconstruct(scope.Request).MaterializeBody();
        Binary binary = Assert.Single(body.Descendants.OfType<Binary>());
        Assert.IsType<Convert>(binary.Left);
        Assert.IsType<Convert>(binary.Right);
        Assert.Equal(isChecked, binary.IsChecked);

        DecompilerResult result = DecompileExpressionFixture(method);
        Assert.Equal(DecompilationFidelity.Full, result.Fidelity);
        string output = Assert.IsType<string>(result.Output);
        Assert.Contains(expected, output, StringComparison.Ordinal);
        Assert.Equal(isChecked, output.Contains("checked(", StringComparison.Ordinal));
    }

    [Fact]
    public void ClassicInverseDoesNotWidenIntegerArithmetic()
    {
        using RequestScope scope = OpenExpressionRequest("IntegerAdd");
        BlockContainer body = Reconstruct(scope.Request).MaterializeBody();
        Assert.Empty(body.Descendants.OfType<Convert>());
        DecompilerResult result = DecompileExpressionFixture("IntegerAdd");
        Assert.Equal(DecompilationFidelity.Full, result.Fidelity);
        Assert.Contains("return await a + b;", result.Output, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("WidenBeforeAdd")]
    [InlineData("WidenBeforeDivide")]
    public void ClassicInverseRejectsErasedArithmeticConversion(string method)
    {
        using RequestScope scope = OpenExpressionRequest(method);
        Reconstruct(scope.Request);
        var (planning, candidate, shell) = Candidate(scope.Request);
        Binary binary = Assert.Single(candidate.Statements
            .SelectMany(statement => statement.Descendants).OfType<Binary>());
        var conversion = Assert.IsType<Convert>(binary.Left);
        IrNode operand = Assert.Single(conversion.DetachChildren());
        conversion.ReplaceWith(operand);

        var decline = Assert.IsType<ClassicInverseDecision.Decline>(
            ClassicInverseAccountant.Account(scope.Request, planning, candidate,
                shell, new ClassicInverseBudget()));
        Assert.Equal(ClassicInverseDeclineReason.UnrealizedSemanticEffect, decline.Reason);
    }

    [Theory]
    [InlineData("AwaitedLength", "(await a).Length")]
    [InlineData("AwaitedIndexer", "(await a)[index]")]
    [InlineData("StaticPropertyAfterAwait", "await a + Environment.TickCount")]
    [InlineData("VirtualPropertyAfterAwait", "(await a).Value")]
    public void ClassicInversePreservesPropertyAccess(string method, string expected)
    {
        using RequestScope scope = OpenExpressionRequest(method);
        ClassicInversePlan plan = Reconstruct(scope.Request);
        LoadProperty property = Assert.Single(
            plan.MaterializeBody().Descendants.OfType<LoadProperty>());
        Assert.Equal(method != "StaticPropertyAfterAwait", property.HasInstance);
        Assert.Equal(method != "StaticPropertyAfterAwait", property.IsVirtual);
        Assert.Equal(method == "AwaitedIndexer" ? 1 : 0, property.IndexArguments.Count);

        DecompilerResult result = DecompileExpressionFixture(method);
        Assert.Equal(DecompilationFidelity.Full, result.Fidelity);
        Assert.Contains(expected, result.Output, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ClassicInverseRejectsChangedPropertyIdentityOrDispatch(bool changeMember)
    {
        using RequestScope scope = OpenExpressionRequest("VirtualPropertyAfterAwait");
        Reconstruct(scope.Request);
        var (planning, candidate, shell) = Candidate(scope.Request);
        LoadProperty property = Assert.Single(candidate.Statements
            .SelectMany(statement => statement.Descendants).OfType<LoadProperty>());
        IrNode instance = Assert.Single(property.DetachChildren());
        property.ReplaceWith(new LoadProperty(
            changeMember ? property.Accessor with { Name = "get_OtherValue" } : property.Accessor,
            (IrExpression)instance, [])
        {
            IsVirtual = changeMember,
        });

        var decline = Assert.IsType<ClassicInverseDecision.Decline>(
            ClassicInverseAccountant.Account(scope.Request, planning, candidate,
                shell, new ClassicInverseBudget()));
        Assert.Equal(ClassicInverseDeclineReason.UnrealizedSemanticEffect, decline.Reason);
        Assert.Contains("differs from", decline.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void ClassicInversePropertyPlanIsDetached()
    {
        using RequestScope scope = OpenExpressionRequest("AwaitedIndexer");
        ClassicInversePlan plan = Reconstruct(scope.Request);
        LoadProperty first = Assert.Single(
            plan.MaterializeBody().Descendants.OfType<LoadProperty>());
        first.SetChild(1, new Constant(999,
            Assert.IsType<LoadArgument>(Assert.Single(first.IndexArguments)).Type));
        scope.Request.KickoffBody.Body.DetachChildren();
        scope.Request.ExecutionBody.Body.DetachChildren();

        LoadProperty second = Assert.Single(
            plan.MaterializeBody().Descendants.OfType<LoadProperty>());
        Assert.Equal(first.Accessor, second.Accessor);
        Assert.True(second.IsVirtual);
        Assert.IsType<AwaitExpression>(second.Instance);
        Assert.Equal("index", Assert.IsType<LoadArgument>(
            Assert.Single(second.IndexArguments)).Name);
        Assert.Null(second.Accessor.ExactDefinitionAcquisitionGuard);
    }

    static RequestScope OpenExpressionRequest(string method)
        => OpenRequest(OpenClassicFixture(), method, ownsSource: true,
            fixtureType: ExpressionFixtureType);

    static DecompilerResult DecompileExpressionFixture(string method)
    {
        using MetadataSource source = OpenClassicFixture();
        IrFunction function = Assert.IsType<IrFunction>(
            IrImporter.Import(source, ExpressionFixtureType, method));
        IrPasses.Run(function, IrPasses.Default, PassContext.ForImport(
            target => IrImporter.Import(source, target)));
        function.CheckInvariant();
        return CSharpPrinter.Print(function);
    }
}
