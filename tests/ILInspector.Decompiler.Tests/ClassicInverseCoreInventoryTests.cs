using DotnetInspector.Fixtures;
using ILInspector.Decompiler.Pipeline;
using ILInspector.DecompilerHarness;

namespace ILInspector.Decompiler.Tests;

public sealed partial class ClassicInverseCoreTests
{
    const string InventoryFixtureType = "ILInspector.Decompiler.Fixtures.ClassicAsync.AsyncInventoryFixtures";

    public static IEnumerable<object[]> InventoryCases()
    {
        foreach (string method in new[]
        {
            "Plain", "ArrayNeighbor", "InterpolationNeighbor", "StructNeighbor", "InterfaceNeighbor",
            "ConstantArrayOperand", "ConstantByteArrayOperand", "RangeResult", "SliceResult", "SliceOperand",
            "FromEndResult", "FromEndOperand", "AnonymousResult", "AnonymousOperand", "DelegateOperand",
            "InterpolationAnonymous", "InterpolationNullable", "NestedInterpolation", "PriorInterpolationArgument",
            "FollowingInterpolationArgument", "ReadOnlySpanLiteral", "ByRefArgument", "OutArgument",
            "RefReceiverResult", "ConditionalAwaitable", "CoalesceAwaitable", "SwitchResult",
            "GenericAny", "GenericStruct", "GenericClass",
            "SliceStart", "SliceEnd", "SliceAll", "VirtualDelegateOperand", "SwitchEffects", "SwitchLabels",
            "ConditionalOperandEffects", "CoalesceOperandEffects", "CollectionEffects",
        })
            yield return [method];
    }

    [Theory]
    [MemberData(nameof(InventoryCases))]
    public void ClassicInversePreservesExpressionInventory(string method)
    {
        using RequestScope scope = OpenInventoryRequest(method);
        ClassicInverseDecision decision = ClassicInverseCore.Decide(scope.Request);
        Assert.True(decision is ClassicInverseDecision.Reconstruct, decision.ToString());
        BlockContainer body = ((ClassicInverseDecision.Reconstruct)decision).Plan.MaterializeBody();
        body.CheckInvariant();
        Assert.Single(body.Descendants.OfType<AwaitExpression>());
        using var source = OpenClassicFixture();
        IrFunction function = IrImporter.Import(source, InventoryFixtureType, method)!;
        IrPasses.Run(function, IrPasses.Default, PassContext.ForImport(target => IrImporter.Import(source, target)));
        function.CheckInvariant();
        DecompilerResult result = CSharpPrinter.Print(function);
        Assert.True(result.RequiresAsyncBodyModifier);
        Assert.Equal(method.Contains("Anonymous", StringComparison.Ordinal)
            ? DecompilationFidelity.Partial : DecompilationFidelity.Full, result.Fidelity);
    }

    [Theory]
    [Trait("Speed", "Slow")]
    [MemberData(nameof(InventoryCases))]
    public void ClassicInverseExpressionInventoryCompilesBack(string method)
    {
        var result = Assert.Single(FidelityCheck.Evaluate(FixtureCatalog.DecompilerClassicAsync.AssemblyPath(),
            type => type == InventoryFixtureType, candidate => candidate.Method == method));
        Assert.True(result.Status == FidelityCheck.CompileBackStatus.Exact,
            $"{result.Method}: {result.Status}: {result.Detail}");
    }

    static RequestScope OpenInventoryRequest(string method)
        => OpenRequest(OpenClassicFixture(), method, ownsSource: true, fixtureType: InventoryFixtureType);

    [Fact]
    public void ClassicInverseDoesNotAuthorizeUnmodeledJumpTable()
    {
        using RequestScope scope = OpenInventoryRequest("SwitchTable");
        Assert.Contains(scope.Request.ExecutionBody.Body.Descendants, node => node is SwitchBranch);
        ClassicInversePlanningView planning = ClassicInversePlanningView.Derive(scope.Request);
        Assert.DoesNotContain(planning.ExecutionBody.Body.Descendants, node => node is SwitchExpression);
        Assert.IsType<ClassicInverseDecision.Decline>(ClassicInverseCore.Decide(scope.Request));
    }
}
