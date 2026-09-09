using ILInspector.Decompiler.Pipeline;
using Convert = ILInspector.Decompiler.Pipeline.Convert;

namespace ILInspector.Decompiler.Tests;

public sealed partial class ClassicInverseCoreTests
{
    [Theory]
    [InlineData("NegateAwaitedValue", "-(await a)")]
    [InlineData("ComplementAwaitedValue", "~(await a)")]
    [InlineData("AwaitedArrayLength", "(await a).Length")]
    [InlineData("FieldAfterAwait", "(await a).FieldValue")]
    [InlineData("StaticFieldAfterAwait", "await a + Reading.StaticFieldValue")]
    [InlineData("VolatileFieldAfterAwait", "(await a).VolatileValue")]
    public void ClassicInversePreservesOrdinaryExpressionForms(
        string method, string expected)
    {
        using RequestScope scope = OpenExpressionRequest(method);
        BlockContainer body = Reconstruct(scope.Request).MaterializeBody();
        if (method == "VolatileFieldAfterAwait")
            Assert.True(Assert.Single(body.Descendants.OfType<LoadField>()).IsVolatile);
        DecompilerResult result = DecompileExpressionFixture(method);
        Assert.Equal(DecompilationFidelity.Full, result.Fidelity);
        Assert.Contains(expected, result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void ClassicInverseRejectsChangedUnaryKind()
    {
        using RequestScope scope = OpenExpressionRequest("NegateAwaitedValue");
        Reconstruct(scope.Request);
        var (planning, candidate, shell) = Candidate(scope.Request);
        Unary unary = Assert.Single(candidate.Statements
            .SelectMany(statement => statement.Descendants).OfType<Unary>());
        IrNode operand = Assert.Single(unary.DetachChildren());
        unary.ReplaceWith(new Unary(UnaryKind.BitwiseNot, (IrExpression)operand));

        var decline = Assert.IsType<ClassicInverseDecision.Decline>(
            ClassicInverseAccountant.Account(scope.Request, planning, candidate,
                shell, new ClassicInverseBudget()));
        Assert.Equal(ClassicInverseDeclineReason.UnrealizedSemanticEffect, decline.Reason);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ClassicInverseRejectsChangedFieldIdentityOrVolatility(bool changeMember)
    {
        using RequestScope scope = OpenExpressionRequest("FieldAfterAwait");
        Reconstruct(scope.Request);
        var (planning, candidate, shell) = Candidate(scope.Request);
        LoadField field = Assert.Single(candidate.Statements
            .SelectMany(statement => statement.Descendants).OfType<LoadField>());
        IrNode instance = Assert.Single(field.DetachChildren());
        field.ReplaceWith(new LoadField(
            changeMember ? field.Field with { Name = "OtherField" } : field.Field,
            (IrExpression)instance)
        {
            IsVolatile = !changeMember,
        });

        var decline = Assert.IsType<ClassicInverseDecision.Decline>(
            ClassicInverseAccountant.Account(scope.Request, planning, candidate,
                shell, new ClassicInverseBudget()));
        Assert.Equal(ClassicInverseDeclineReason.UnrealizedSemanticEffect, decline.Reason);
    }

    [Theory]
    [InlineData("NegateAwaitedValue")]
    [InlineData("AwaitedArrayLength")]
    [InlineData("FieldAfterAwait")]
    public void ClassicInverseOrdinaryExpressionPlansAreDetached(string method)
    {
        using RequestScope scope = OpenExpressionRequest(method);
        ClassicInversePlan plan = Reconstruct(scope.Request);
        AwaitExpression first = Assert.Single(
            plan.MaterializeBody().Descendants.OfType<AwaitExpression>());
        first.SetChild(0, new Constant(null,
            Assert.IsType<LoadArgument>(first.Operand).Type));
        scope.Request.KickoffBody.Body.DetachChildren();
        scope.Request.ExecutionBody.Body.DetachChildren();

        AwaitExpression second = Assert.Single(
            plan.MaterializeBody().Descendants.OfType<AwaitExpression>());
        Assert.Equal("a", Assert.IsType<LoadArgument>(second.Operand).Name);
    }

    [Theory]
    [InlineData("ConfiguredTask", "await a.ConfigureAwait(false)")]
    [InlineData("ConfiguredTaskCapturesContext", "await a.ConfigureAwait(true)")]
    [InlineData("ConfiguredVoidTask", "await a.ConfigureAwait(false)")]
    [InlineData("ConfiguredValueTask", "await a.ConfigureAwait(false)")]
    [InlineData("ConfiguredVoidValueTask", "await a.ConfigureAwait(false)")]
    [InlineData("ConfiguredTaskWithOption", "await a.ConfigureAwait(continueOnCapturedContext)")]
    public void ClassicInversePreservesConfiguredAwaitables(
        string method, string expected)
    {
        using RequestScope scope = OpenExpressionRequest(method);
        BlockContainer body = Reconstruct(scope.Request).MaterializeBody();
        AwaitExpression await = Assert.Single(body.Descendants.OfType<AwaitExpression>());
        Assert.Equal("ConfigureAwait", Assert.IsType<Call>(await.Operand).Callee.Name);
        DecompilerResult result = DecompileExpressionFixture(method);
        Assert.Equal(DecompilationFidelity.Full, result.Fidelity);
        Assert.Contains(expected, result.Output, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("ConfiguredTask")]
    [InlineData("ConfiguredVoidTask")]
    [InlineData("ConfiguredValueTask")]
    [InlineData("ConfiguredVoidValueTask")]
    public void ClassicInverseConfiguredDispatchCannotBeHealedByPlanning(string method)
    {
        using RequestScope baseline = OpenExpressionRequest(method);
        Reconstruct(baseline.Request);
        using RequestScope changed = OpenRequest(
            OpenClassicFixture(), method, ownsSource: true,
            mutateExecution: body =>
            {
                Call original = Assert.Single(body.Body.Descendants.OfType<Call>(),
                    call => call.Callee.Name == "GetAwaiter");
                Assert.False(original.IsVirtual);
                var replacement = new Call(original.Callee, isVirtual: true,
                    [.. original.Arguments.Select(argument =>
                        (IrExpression)argument.Clone())]);
                replacement.SetSourceOffset(original.SourceOffset);
                original.ReplaceWith(replacement);
            },
            fixtureType: ExpressionFixtureType);
        bool repaired = false;
        ClassicInverseRequest request = CopyRequest(changed.Request,
            runPasses: (body, passes) =>
            {
                if (body.Name == "MoveNext")
                {
                    body.SetChild(0, baseline.Request.ExecutionBody.Body.Clone());
                    repaired = true;
                }
                baseline.Request.RunPasses!(body, passes);
            });
        var decline = Assert.IsType<ClassicInverseDecision.Decline>(
            ClassicInverseCore.Decide(request));
        Assert.Equal(ClassicInverseDeclineReason.UnclassifiedPhysicalRegion, decline.Reason);
        Assert.True(repaired);
    }

    [Theory]
    [InlineData("checked")]
    [InlineData("unsigned")]
    [InlineData("widening")]
    public void ClassicInverseArrayLengthConversionCannotBeHealedByPlanning(string mutation)
    {
        using RequestScope baseline = OpenExpressionRequest("AwaitedArrayLength");
        Reconstruct(baseline.Request);
        using RequestScope changed = OpenRequest(
            OpenClassicFixture(), "AwaitedArrayLength", ownsSource: true,
            mutateExecution: body =>
            {
                Convert original = Assert.Single(body.Body.Descendants.OfType<Convert>(),
                    convert => convert.Operand is ArrayLength);
                var replacement = new Convert(
                    mutation == "widening" ? TypeRef.CoreLib("System", "Int64") : original.Target,
                    isChecked: mutation == "checked",
                    isUnsigned: mutation == "unsigned",
                    (IrExpression)original.Operand.Clone());
                replacement.InheritSourceOffset(original);
                original.ReplaceWith(replacement);
            },
            fixtureType: ExpressionFixtureType);
        AssertPlanningCannotRepair(changed.Request, baseline.Request);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ClassicInverseConfiguredOperandCannotBeHealedByPlanning(bool moveStore)
    {
        using RequestScope baseline = OpenExpressionRequest("ConfiguredTask");
        Reconstruct(baseline.Request);
        using RequestScope changed = OpenRequest(
            OpenClassicFixture(), "ConfiguredTask", ownsSource: true,
            mutateExecution: body =>
            {
                Call configure = Assert.Single(body.Body.Descendants.OfType<Call>(),
                    call => call.Callee.Name == "ConfigureAwait");
                if (!moveStore)
                {
                    Constant original = Assert.IsType<Constant>(configure.Arguments[1]);
                    var replacement = new Constant(1, original.Type);
                    replacement.InheritSourceOffset(original);
                    original.ReplaceWith(replacement);
                    return;
                }

                StoreLocal store = Assert.IsType<StoreLocal>(configure.Parent);
                Block block = Assert.IsType<Block>(store.Parent);
                IrNode bind = block.Children[store.ChildIndex + 1];
                IReadOnlyList<IrNode> children = block.DetachChildren();
                foreach (IrNode child in children)
                {
                    if (ReferenceEquals(child, store))
                        continue;
                    block.Add(child);
                    if (ReferenceEquals(child, bind))
                        block.Add(store);
                }
            },
            fixtureType: ExpressionFixtureType);
        AssertPlanningCannotRepair(changed.Request, baseline.Request);
    }

    static void AssertPlanningCannotRepair(
        ClassicInverseRequest changed, ClassicInverseRequest baseline)
    {
        bool repaired = false;
        ClassicInverseRequest request = CopyRequest(changed,
            runPasses: (body, passes) =>
            {
                if (body.Name == "MoveNext")
                {
                    body.SetChild(0, baseline.ExecutionBody.Body.Clone());
                    repaired = true;
                }
                baseline.RunPasses!(body, passes);
            });
        Assert.IsType<ClassicInverseDecision.Decline>(ClassicInverseCore.Decide(request));
        Assert.True(repaired);
    }

    [Fact]
    public void ClassicInverseFieldVolatilityCannotBeHealedByPlanning()
    {
        using RequestScope baseline = OpenExpressionRequest("FieldAfterAwait");
        Reconstruct(baseline.Request);
        using RequestScope changed = OpenRequest(
            OpenClassicFixture(), "FieldAfterAwait", ownsSource: true,
            mutateExecution: body =>
            {
                LoadField original = Assert.Single(body.Body.Descendants.OfType<LoadField>(),
                    field => field.Field.Name == "FieldValue");
                var replacement = new LoadField(original.Field,
                    (IrExpression)Assert.IsAssignableFrom<IrExpression>(
                        original.Instance).Clone())
                {
                    IsVolatile = true,
                };
                replacement.InheritSourceOffset(original);
                original.ReplaceWith(replacement);
            },
            fixtureType: ExpressionFixtureType);
        AssertPlanningCannotRepair(changed.Request, baseline.Request);
    }
}
