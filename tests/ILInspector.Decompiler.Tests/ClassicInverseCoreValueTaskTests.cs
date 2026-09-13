using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

public sealed partial class ClassicInverseCoreTests
{
    [Fact]
    public void ClassicInversePreservesExistingValueTaskReconstruction()
    {
        using RequestScope scope = OpenRequest("AwaitValueTask");
        Reconstruct(scope.Request);
        using MetadataSource source = OpenClassicFixture();
        IrFunction function = Assert.IsType<IrFunction>(
            IrImporter.Import(source, FixtureType, "AwaitValueTask"));
        IrPasses.Run(function, IrPasses.Default, PassContext.ForImport(
            method => IrImporter.Import(source, method)));
        function.CheckInvariant();
        DecompilerResult result = CSharpPrinter.Print(function);
        Assert.Equal(DecompilationFidelity.Full, result.Fidelity);
        Assert.Contains("return await a;", result.Output, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ClassicInverseValueTaskDispatchCannotBeHealedByPlanning(
        bool changeMember)
    {
        using RequestScope baseline = OpenRequest("AwaitValueTask");
        Reconstruct(baseline.Request);
        using RequestScope changed = OpenMutatedRequest("AwaitValueTask", body =>
        {
            Call original = Assert.Single(body.Body.Descendants.OfType<Call>(),
                call => call.Callee.Name == "GetAwaiter");
            Assert.False(original.IsVirtual);
            var replacement = new Call(
                changeMember
                    ? original.Callee with { Name = "DifferentGetAwaiter" }
                    : original.Callee,
                isVirtual: !changeMember,
                [.. original.Arguments.Select(argument =>
                    (IrExpression)argument.Clone())]);
            replacement.SetSourceOffset(original.SourceOffset);
            original.ReplaceWith(replacement);
        });
        bool repaired = false;
        ClassicInverseRequest request = CopyRequest(
            changed.Request,
            runPasses: (body, passes) =>
            {
                if (body.Name == "MoveNext")
                {
                    body.SetChild(0, baseline.Request.ExecutionBody.Body.Clone());
                    repaired = true;
                }
                baseline.Request.RunPasses!(body, passes);
            });
        var decision = Assert.IsType<ClassicInverseDecision.Decline>(
            ClassicInverseCore.Decide(request));
        Assert.Equal(ClassicInverseDeclineReason.UnclassifiedPhysicalRegion,
            decision.Reason);
        Assert.True(repaired);
    }
}
