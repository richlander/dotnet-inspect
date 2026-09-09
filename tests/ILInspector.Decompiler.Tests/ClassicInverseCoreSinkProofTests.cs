using ILInspector.Decompiler.Pipeline;
using Convert = ILInspector.Decompiler.Pipeline.Convert;

namespace ILInspector.Decompiler.Tests;

public sealed partial class ClassicInverseCoreTests
{
    [Theory]
    [InlineData("ByteArgument", "range")]
    [InlineData("CharacterArgument", "value")]
    [InlineData("CharacterArgument", "type")]
    [InlineData("EnumArgument", "value")]
    [InlineData("ShortCircuitCall", "value")]
    [InlineData("LongEnumArgument", "checked")]
    [InlineData("LongEnumArgument", "unsigned")]
    public void ClassicInverseSinkLiteralCannotBeHealedByPlanning(string method, string mutation)
    {
        using RequestScope baseline = OpenExpressionRequest(method);
        Reconstruct(baseline.Request);
        using RequestScope changed = OpenRequest(OpenClassicFixture(), method, ownsSource: true,
            mutateExecution: body =>
            {
                if (method == "LongEnumArgument")
                {
                    Convert conversion = Assert.Single(body.Body.Descendants.OfType<Convert>());
                    var replacement = new Convert(conversion.Target, mutation == "checked",
                        mutation == "unsigned", (IrExpression)conversion.Operand.Clone());
                    replacement.InheritSourceOffset(conversion);
                    conversion.ReplaceWith(replacement);
                    return;
                }
                Constant constant;
                if (method == "ShortCircuitCall")
                {
                    constant = Assert.Single(body.Body.Descendants.OfType<Constant>(),
                        value => value.Parent is StoreStackSlot && value.Value is int number
                            && number == 0 && value.SourceOffset > 90);
                }
                else
                {
                    Call call = Assert.Single(body.Body.Descendants.OfType<Call>(),
                        call => call.Callee.Name.StartsWith("With", StringComparison.Ordinal));
                    constant = Assert.IsType<Constant>(call.Arguments[1]);
                }
                var replacementLiteral = mutation == "type"
                    ? new Constant((long)(int)constant.Value!, TypeRef.CoreLib("System", "Int64"))
                    : new Constant(mutation == "range" ? 300 : (int)constant.Value! + 2, constant.Type);
                replacementLiteral.InheritSourceOffset(constant);
                constant.ReplaceWith(replacementLiteral);
            }, fixtureType: ExpressionFixtureType);
        AssertPlanningCannotRepair(changed.Request, baseline.Request);
    }

    [Fact]
    public void ClassicInverseCoerceRetainsExactTarget()
    {
        using RequestScope scope = OpenExpressionRequest("ByteArgument");
        Reconstruct(scope.Request);
        var (planning, candidate, shell) = Candidate(scope.Request);
        Coerce coerce = Assert.Single(candidate.Statements.SelectMany(node => node.Descendants).OfType<Coerce>());
        IrExpression operand = (IrExpression)Assert.Single(coerce.DetachChildren());
        coerce.ReplaceWith(new Coerce(TypeRef.CoreLib("System", "SByte"), operand));
        Assert.IsType<ClassicInverseDecision.Decline>(ClassicInverseAccountant.Account(
            scope.Request, planning, candidate, shell, new ClassicInverseBudget()));
    }

    [Fact]
    public void ClassicInverseCoerceRequiresAnActualSink()
    {
        using RequestScope scope = OpenExpressionRequest("IntegerAdd");
        Reconstruct(scope.Request);
        bool changed = false;
        ClassicInverseRequest request = CopyRequest(scope.Request, runPasses: (body, passes) =>
        {
            scope.Request.RunPasses!(body, passes);
            if (body.Name != "MoveNext")
                return;
            Binary arithmetic = Assert.Single(body.Body.Descendants.OfType<Binary>());
            var coerce = new Coerce(TypeRef.CoreLib("System", "Byte"), (IrExpression)arithmetic.Left.Clone());
            coerce.InheritSourceOffset(arithmetic.Left);
            arithmetic.SetChild(0, coerce);
            changed = true;
        });
        Assert.IsType<ClassicInverseDecision.Decline>(ClassicInverseCore.Decide(request));
        Assert.True(changed);
    }

    [Theory]
    [InlineData("target")]
    [InlineData("slot")]
    [InlineData("extra-use")]
    public void ClassicInverseComposedSelectionCannotBeHealedByPlanning(string mutation)
    {
        using RequestScope baseline = OpenExpressionRequest("NestedBooleanChoice");
        Reconstruct(baseline.Request);
        using RequestScope changed = OpenRequest(OpenClassicFixture(), "NestedBooleanChoice", ownsSource: true,
            mutateExecution: body =>
            {
                ConditionalBranch next = Assert.Single(body.Body.Descendants.OfType<ConditionalBranch>(),
                    branch => branch.Condition is LoadStackSlot);
                LoadStackSlot use = Assert.IsType<LoadStackSlot>(next.Condition);
                if (mutation == "slot")
                {
                    var replacement = new LoadStackSlot(use.Slot + 1, use.Type);
                    replacement.InheritSourceOffset(use);
                    use.ReplaceWith(replacement);
                }
                else if (mutation == "target")
                {
                    Block block = Assert.IsType<Block>(next.Parent);
                    BlockContainer container = Assert.IsType<BlockContainer>(block.Parent);
                    var replacement = new ConditionalBranch((IrExpression)next.Condition.Clone(),
                        container.Blocks[block.ChildIndex + 1].StartOffset);
                    replacement.InheritSourceOffset(next);
                    next.ReplaceWith(replacement);
                }
                else
                {
                    Block block = Assert.IsType<Block>(next.Parent);
                    next.Detach();
                    block.Add(new StoreStackSlot(999, (IrExpression)use.Clone()));
                    block.Add(next);
                }
            }, fixtureType: ExpressionFixtureType);
        AssertPlanningCannotRepair(changed.Request, baseline.Request);
    }

    [Fact]
    public void ClassicInversePrimitiveReceiverCannotBorrowAnAwaiterSlot()
    {
        using RequestScope baseline = OpenExpressionRequest("IntReceiver");
        Reconstruct(baseline.Request);
        using RequestScope changed = OpenRequest(OpenClassicFixture(), "IntReceiver", ownsSource: true,
            mutateExecution: body =>
            {
                Call result = Assert.Single(body.Body.Descendants.OfType<Call>(),
                    call => call.Callee.Name == "GetResult");
                LoadLocalAddress awaiter = Assert.IsType<LoadLocalAddress>(Assert.Single(result.Arguments));
                Call receiverCall = Assert.Single(body.Body.Descendants.OfType<Call>(),
                    call => call.Callee.Name == "ToString");
                LoadLocalAddress address = Assert.IsType<LoadLocalAddress>(Assert.Single(receiverCall.Arguments));
                var replacement = new LoadLocalAddress(awaiter.Index, address.Type);
                replacement.InheritSourceOffset(address);
                address.ReplaceWith(replacement);
            }, fixtureType: ExpressionFixtureType);
        AssertPlanningCannotRepair(changed.Request, baseline.Request);
    }

    [Theory]
    [InlineData("ByteArgument")]
    [InlineData("CharacterArgument")]
    [InlineData("LongEnumArgument")]
    [InlineData("NestedBooleanChoice")]
    public void ClassicInverseSinkAndCompositionPlansAreDetached(string method)
    {
        using RequestScope scope = OpenExpressionRequest(method);
        ClassicInversePlan plan = Reconstruct(scope.Request);
        string signature = plan.Signature;
        BlockContainer first = plan.MaterializeBody();
        first.DetachChildren();
        scope.Request.KickoffBody.Body.DetachChildren();
        scope.Request.ExecutionBody.Body.DetachChildren();
        BlockContainer second = plan.MaterializeBody();
        second.CheckInvariant();
        Assert.Single(second.Descendants.OfType<AwaitExpression>());
        Assert.Equal(signature, plan.Signature);
        if (method == "ByteArgument")
            Assert.Equal(TypeRef.CoreLib("System", "Byte"),
                Assert.Single(second.Descendants.OfType<Coerce>()).Target);
    }

    [Theory]
    [InlineData("ByteArgument")]
    [InlineData("LongEnumArgument")]
    [InlineData("IntReceiver")]
    [InlineData("NestedBooleanChoice")]
    public void ClassicInverseSinkAndCompositionBudgetsRemainLoadBearing(string method)
    {
        using RequestScope scope = OpenExpressionRequest(method);
        var budget = new ClassicInverseBudget();
        Assert.IsType<ClassicInverseDecision.Reconstruct>(ClassicInverseCore.Decide(scope.Request, budget));
        var failed = Assert.IsType<ClassicInverseDecision.Failed>(
            ClassicInverseCore.Decide(scope.Request, new ClassicInverseBudget(budget.Consumed - 1)));
        Assert.Equal(ClassicInverseFailureKind.BudgetExhausted, failed.Failure.Kind);
    }

    [Fact]
    [Trait("Speed", "Slow")]
    public void ClassicInverseSinkMatchingBudgetExhaustionCannotDecline()
    {
        using RequestScope scope = OpenExpressionRequest("CharacterArgument");
        var complete = new ClassicInverseBudget();
        Assert.IsType<ClassicInverseDecision.Reconstruct>(ClassicInverseCore.Decide(scope.Request, complete));
        for (int limit = 1; limit <= complete.Consumed; limit++)
        {
            var budget = new ClassicInverseBudget(limit);
            ClassicInverseDecision decision = ClassicInverseCore.Decide(scope.Request, budget);
            Assert.True(budget.Exhausted
                ? decision is ClassicInverseDecision.Failed { Failure.Kind: ClassicInverseFailureKind.BudgetExhausted }
                : decision is ClassicInverseDecision.Reconstruct,
                $"Budget {limit}/{complete.Consumed}: {decision}");
        }
    }
}
