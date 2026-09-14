using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

public sealed partial class ClassicInverseCoreTests
{
    [Theory]
    [InlineData("ReferenceCast")]
    [InlineData("ValueCast")]
    [InlineData("AsReference")]
    [InlineData("TypeTest")]
    [InlineData("NegatedTypeTest")]
    [InlineData("NewIntArray")]
    [InlineData("NewReferenceArray")]
    [InlineData("ArrayElementAfterAwait")]
    public void ClassicInversePreservesTypedExpressionForms(string method)
    {
        using RequestScope scope = OpenExpressionRequest(method);
        ClassicInversePlan plan = Reconstruct(scope.Request);
        BlockContainer body = plan.MaterializeBody();
        body.CheckInvariant();
        Assert.Single(body.Descendants.OfType<AwaitExpression>());
        IrExpression value = Assert.IsAssignableFrom<IrExpression>(
            Assert.Single(body.Descendants.OfType<Return>()).Value);
        TypeRef integer = TypeRef.CoreLib("System", "Int32");
        TypeRef text = TypeRef.CoreLib("System", "String");
        switch (method)
        {
            case "ReferenceCast":
                Assert.Equal(text, Assert.IsType<CastClass>(value).Type);
                break;
            case "ValueCast":
                Assert.Equal(integer, Assert.IsType<UnboxAny>(value).Type);
                break;
            case "AsReference":
                Assert.Equal(text, Assert.IsType<IsInstance>(value).Type);
                break;
            case "TypeTest":
            case "NegatedTypeTest":
                Assert.Equal(text, Assert.Single(body.Descendants.OfType<IsInstance>()).Type);
                Assert.Equal(method == "NegatedTypeTest", value is LogicalNot);
                break;
            case "NewIntArray":
                Assert.Equal(integer, Assert.IsType<NewArray>(value).ElementType);
                Assert.IsType<AwaitExpression>(((NewArray)value).Length);
                break;
            case "NewReferenceArray":
                Assert.Equal(text, Assert.IsType<NewArray>(value).ElementType);
                Binary length = Assert.IsType<Binary>(((NewArray)value).Length);
                Assert.Equal(BinaryKind.Add, length.Kind);
                Assert.IsType<AwaitExpression>(length.Left);
                Assert.Equal(1, Assert.IsType<Constant>(length.Right).Value);
                break;
            case "ArrayElementAfterAwait":
                Assert.Equal(integer, Assert.IsType<LoadElement>(value).ElementType);
                Assert.Equal("index", Assert.IsType<LoadArgument>(((LoadElement)value).Index).Name);
                break;
        }
        Assert.All(plan.SemanticRealizations,
            receipt => Assert.Equal(receipt.SourceEffects, receipt.OutputEffects));
        DecompilerResult result = DecompileExpressionFixture(method);
        Assert.Equal(DecompilationFidelity.Full, result.Fidelity);
    }

    [Theory]
    [InlineData("Not", "return !(await a);")]
    [InlineData("EqualFalse", "return !(await a);")]
    [InlineData("EqualTrue", "return await a;")]
    [InlineData("NotEqualFalse", "return await a;")]
    [InlineData("NotEqualTrue", "return !(await a);")]
    [InlineData("CompareBooleans", "return (await a) == b;")]
    [InlineData("NotComparison", "return (await a) <= b;")]
    [InlineData("NotUnsignedComparison", "return (await a) <= b;")]
    [InlineData("NotFloatComparison", "return !((await a) > b);")]
    [InlineData("DoubleNot", "return await a;")]
    public void ClassicInversePreservesBooleanExpressions(string method, string expected)
    {
        using RequestScope scope = OpenExpressionRequest(method);
        BlockContainer body = Reconstruct(scope.Request).MaterializeBody();
        body.CheckInvariant();
        Assert.Single(body.Descendants.OfType<AwaitExpression>());
        DecompilerResult result = DecompileExpressionFixture(method);
        Assert.Equal(DecompilationFidelity.Full, result.Fidelity);
        Assert.Contains(expected, result.Output, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("ReferenceCast")]
    [InlineData("ValueCast")]
    [InlineData("AsReference")]
    [InlineData("NewIntArray")]
    public void ClassicInverseRejectsChangedExpressionType(string method)
    {
        using RequestScope scope = OpenExpressionRequest(method);
        Reconstruct(scope.Request);
        var (planning, candidate, shell) = Candidate(scope.Request);
        IrNode expression = Assert.Single(candidate.Statements.SelectMany(node => node.Descendants),
            node => node is CastClass or UnboxAny or IsInstance or NewArray);
        ChangeExpressionType(expression);

        var decline = Assert.IsType<ClassicInverseDecision.Decline>(
            ClassicInverseAccountant.Account(scope.Request, planning, candidate,
                shell, new ClassicInverseBudget()));
        Assert.Equal(ClassicInverseDeclineReason.UnrealizedSemanticEffect, decline.Reason);
    }

    [Theory]
    [InlineData("ReferenceCast")]
    [InlineData("ValueCast")]
    [InlineData("AsReference")]
    [InlineData("NewIntArray")]
    public void ClassicInverseExpressionTypeCannotBeHealedByPlanning(string method)
    {
        using RequestScope baseline = OpenExpressionRequest(method);
        Reconstruct(baseline.Request);
        using RequestScope changed = OpenRequest(OpenClassicFixture(), method, ownsSource: true,
            mutateExecution: body =>
                ChangeExpressionType(Assert.Single(body.Body.Descendants,
                    node => node is CastClass or UnboxAny or IsInstance or NewArray)),
            fixtureType: ExpressionFixtureType);
        AssertPlanningCannotRepair(changed.Request, baseline.Request);
    }

    [Theory]
    [InlineData("ReferenceCast")]
    [InlineData("ValueCast")]
    [InlineData("AsReference")]
    [InlineData("NewIntArray")]
    [InlineData("Not")]
    public void ClassicInverseTypedAndBooleanPlansAreDetached(string method)
    {
        using RequestScope scope = OpenExpressionRequest(method);
        ClassicInversePlan plan = Reconstruct(scope.Request);
        BlockContainer first = plan.MaterializeBody();
        IrExpression original = Assert.IsAssignableFrom<IrExpression>(
            Assert.Single(first.Descendants.OfType<Return>()).Value);
        Assert.Single(original.Children);
        original.SetChild(0, new Constant(null, original.ResultType!));
        scope.Request.KickoffBody.Body.DetachChildren();
        scope.Request.ExecutionBody.Body.DetachChildren();

        BlockContainer second = plan.MaterializeBody();
        second.CheckInvariant();
        IrExpression restored = Assert.IsAssignableFrom<IrExpression>(
            Assert.Single(second.Descendants.OfType<Return>()).Value);
        Assert.NotSame(original, restored);
        Assert.Equal(original.GetType(), restored.GetType());
        Assert.Equal(original.ResultType, restored.ResultType);
        AwaitExpression await = Assert.Single(second.Descendants.OfType<AwaitExpression>());
        Assert.Equal("a", Assert.IsType<LoadArgument>(await.Operand).Name);
    }

    [Theory]
    [InlineData("literal")]
    [InlineData("non-boolean-literal")]
    [InlineData("literal-type")]
    [InlineData("operator")]
    [InlineData("unsigned")]
    public void ClassicInverseBooleanNegationCannotBeHealedByPlanning(string mutation)
    {
        using RequestScope baseline = OpenExpressionRequest("Not");
        Reconstruct(baseline.Request);
        using RequestScope changed = OpenRequest(OpenClassicFixture(), "Not", ownsSource: true,
            mutateExecution: body =>
            {
                Comparison comparison = Assert.Single(body.Body.Descendants.OfType<Comparison>());
                Constant literal = Assert.IsType<Constant>(comparison.Right);
                Assert.Equal(0, literal.Value);
                if (mutation is "operator" or "unsigned")
                {
                    var replacement = new Comparison(
                        mutation == "operator" ? ComparisonKind.NotEqual : comparison.Kind,
                        isUnsigned: mutation == "unsigned",
                        (IrExpression)comparison.Left.Clone(), (IrExpression)literal.Clone());
                    replacement.InheritSourceOffset(comparison);
                    comparison.ReplaceWith(replacement);
                }
                else
                {
                    var replacement = mutation == "literal-type"
                        ? new Constant(0L, TypeRef.CoreLib("System", "Int64"))
                        : new Constant(mutation == "literal" ? 1 : 2, literal.Type);
                    replacement.InheritSourceOffset(literal);
                    literal.ReplaceWith(replacement);
                }
            }, fixtureType: ExpressionFixtureType);
        AssertPlanningCannotRepair(changed.Request, baseline.Request);
    }

    [Theory]
    [InlineData("Not", "lost-negation")]
    [InlineData("Not", "missing-offset")]
    [InlineData("NotComparison", "operator")]
    [InlineData("NotFloatComparison", "unsigned")]
    [InlineData("NotUnsignedComparison", "unsigned")]
    public void ClassicInverseRejectsChangedBooleanFold(string method, string mutation)
    {
        using RequestScope scope = OpenExpressionRequest(method);
        Reconstruct(scope.Request);
        bool changed = false;
        ClassicInverseRequest request = CopyRequest(scope.Request, runPasses: (body, passes) =>
        {
            scope.Request.RunPasses!(body, passes);
            if (body.Name != "MoveNext")
                return;
            StoreLocal result = Assert.Single(body.Body.Descendants.OfType<StoreLocal>(),
                store => store.Value.Descendants.OfType<Call>()
                    .Any(call => call.Callee.Name == "GetResult"));
            if (mutation == "lost-negation")
            {
                LogicalNot not = Assert.IsType<LogicalNot>(result.Value);
                IrNode operand = Assert.Single(not.DetachChildren());
                not.ReplaceWith(operand);
            }
            else if (mutation == "missing-offset")
            {
                Assert.IsType<LogicalNot>(result.Value).SetSourceOffset(-1);
            }
            else
            {
                Comparison comparison = Assert.IsType<Comparison>(result.Value);
                var replacement = new Comparison(
                    mutation == "operator" ? ComparisonKind.GreaterThan : comparison.Kind,
                    mutation == "unsigned" ? !comparison.IsUnsigned : comparison.IsUnsigned,
                    (IrExpression)comparison.Left.Clone(), (IrExpression)comparison.Right.Clone());
                replacement.InheritSourceOffset(comparison);
                comparison.ReplaceWith(replacement);
            }
            changed = true;
        });
        Assert.IsType<ClassicInverseDecision.Decline>(ClassicInverseCore.Decide(request));
        Assert.True(changed);
    }

    [Theory]
    [InlineData("Not")]
    [InlineData("NotComparison")]
    [InlineData("NegatedTypeTest")]
    public void ClassicInverseBooleanFoldAccountsForConsumedValues(string method)
    {
        using RequestScope scope = OpenExpressionRequest(method);
        Comparison comparison = Assert.Single(scope.Request.ExecutionBody.Body.Descendants
            .OfType<Comparison>(), node => node.Parent is StoreLocal);
        ClassicInversePlan plan = Reconstruct(scope.Request);
        ClassicInverseSemanticRealization result = Assert.Single(plan.SemanticRealizations,
            receipt => receipt.Rule == ClassicInverseRealizationRule.ResultStore);
        Assert.Contains(comparison.SourceOffset, result.ImportOffsets);
        Assert.Contains(comparison.Right.SourceOffset, result.ImportOffsets);
        if (comparison.Left is Comparison inner)
            Assert.Contains(inner.SourceOffset, result.ImportOffsets);
        foreach (int offset in new[] { comparison.SourceOffset, comparison.Right.SourceOffset })
        {
            Assert.Contains(plan.PhysicalPartition, region =>
                region.Body == ClassicInverseBodyId.Execution
                && region.Disposition == ClassicInverseRegionDisposition.Semantic
                && region.ImportOffsets.Contains(offset));
        }
    }

    static void ChangeExpressionType(IrNode expression)
    {
        IrExpression operand = Assert.IsAssignableFrom<IrExpression>(
            Assert.Single(expression.DetachChildren()));
        IrNode replacement = expression switch
        {
            CastClass => new CastClass(TypeRef.CoreLib("System", "Object"), operand),
            UnboxAny => new UnboxAny(TypeRef.CoreLib("System", "Int64"), operand),
            IsInstance => new IsInstance(TypeRef.CoreLib("System", "Object"), operand),
            NewArray => new NewArray(TypeRef.CoreLib("System", "String"), operand),
            _ => throw new InvalidOperationException("Expected a typed expression."),
        };
        replacement.InheritSourceOffset(expression);
        expression.ReplaceWith(replacement);
    }
}
