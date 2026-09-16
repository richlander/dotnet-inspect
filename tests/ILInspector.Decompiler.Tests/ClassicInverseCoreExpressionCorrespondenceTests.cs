using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

public sealed partial class ClassicInverseCoreTests
{
    [Theory]
    [InlineData("TypeEquality")]
    [InlineData("CoalesceTypeOf")]
    public void ClassicInverseRejectsChangedTypeOfTarget(string method)
    {
        using RequestScope scope = OpenExpressionRequest(method);
        Reconstruct(scope.Request);
        var (planning, candidate, shell) = Candidate(scope.Request);
        TypeOf typeOf = Assert.Single(candidate.Statements.SelectMany(node => node.Descendants).OfType<TypeOf>());
        typeOf.ReplaceWith(new TypeOf(TypeRef.CoreLib("System", "Object")));
        Assert.IsType<ClassicInverseDecision.Decline>(ClassicInverseAccountant.Account(
            scope.Request, planning, candidate, shell, new ClassicInverseBudget()));
    }

    [Theory]
    [InlineData("target")]
    [InlineData("assembly")]
    [InlineData("token-kind")]
    [InlineData("member")]
    public void ClassicInverseTypeOfCannotBeHealedByPlanning(string mutation)
    {
        using RequestScope baseline = OpenExpressionRequest("CoalesceTypeOf");
        Reconstruct(baseline.Request);
        using RequestScope changed = OpenRequest(OpenClassicFixture(), "CoalesceTypeOf", ownsSource: true,
            mutateExecution: body =>
            {
                Call call = Assert.Single(body.Body.Descendants.OfType<Call>(),
                    call => call.Callee.Name == "GetTypeFromHandle");
                LoadToken token = Assert.IsType<LoadToken>(Assert.Single(call.Arguments));
                if (mutation == "member")
                {
                    var replacement = new Call(call.Callee with
                    {
                        DeclaringType = TypeRef.Definition("OtherAssembly", "System", "Type"),
                    }, false, [(IrExpression)token.Clone()]);
                    replacement.InheritSourceOffset(call);
                    call.ReplaceWith(replacement);
                    return;
                }
                var changedToken = new LoadToken(
                    mutation == "token-kind" ? RuntimeTokenKind.Method : RuntimeTokenKind.Type,
                    mutation == "target" ? TypeRef.CoreLib("System", "Int32")
                        : mutation == "assembly" ? TypeRef.Definition("OtherAssembly", "System", "String") : null,
                    token.Display);
                changedToken.InheritSourceOffset(token);
                token.ReplaceWith(changedToken);
            }, fixtureType: ExpressionFixtureType);
        AssertPlanningCannotRepair(changed.Request, baseline.Request);
    }

    [Theory]
    [InlineData("TypeEquality")]
    [InlineData("CoalesceTypeOf")]
    public void ClassicInverseTypeOfOriginsAreRequiredAndAccounted(string method)
    {
        using RequestScope scope = OpenExpressionRequest(method);
        Call call = Assert.Single(scope.Request.ExecutionBody.Body.Descendants.OfType<Call>(),
            call => call.Callee.Name == "GetTypeFromHandle");
        LoadToken token = Assert.IsType<LoadToken>(Assert.Single(call.Arguments));
        ClassicInversePlan plan = Reconstruct(scope.Request);
        Assert.Contains(plan.SemanticRealizations, receipt =>
            receipt.ImportOffsets.Contains(call.SourceOffset) && receipt.ImportOffsets.Contains(token.SourceOffset));
        bool changed = false;
        ClassicInverseRequest missing = CopyRequest(scope.Request, runPasses: (body, passes) =>
        {
            scope.Request.RunPasses!(body, passes);
            if (body.Name == "MoveNext")
            {
                Assert.Single(body.Body.Descendants.OfType<TypeOf>()).SetSourceOffset(-1);
                changed = true;
            }
        });
        Assert.IsType<ClassicInverseDecision.Decline>(ClassicInverseCore.Decide(missing));
        Assert.True(changed);
    }

    [Theory]
    [InlineData("target")]
    [InlineData("condition")]
    [InlineData("arms")]
    [InlineData("extra-entry")]
    public void ClassicInverseAwaitedPredicateRawControlCannotBeHealed(string mutation)
    {
        using RequestScope baseline = OpenExpressionRequest("BooleanChoiceCalls");
        Reconstruct(baseline.Request);
        using RequestScope changed = OpenRequest(OpenClassicFixture(), "BooleanChoiceCalls", ownsSource: true,
            mutateExecution: body =>
            {
                Call getResult = Assert.Single(body.Body.Descendants.OfType<Call>(),
                    call => call.Callee.Name == "GetResult");
                ConditionalBranch branch = Assert.IsType<ConditionalBranch>(getResult.Parent);
                Block head = Assert.IsType<Block>(branch.Parent);
                BlockContainer container = Assert.IsType<BlockContainer>(head.Parent);
                Block whenTrue = Assert.Single(container.Blocks, block => block.StartOffset == branch.TargetOffset);
                Block whenFalse = container.Blocks[head.ChildIndex + 1];
                switch (mutation)
                {
                    case "target":
                    {
                        var replacement = new ConditionalBranch((IrExpression)branch.Condition.Clone(),
                            whenFalse.StartOffset);
                        replacement.InheritSourceOffset(branch);
                        branch.ReplaceWith(replacement);
                        break;
                    }
                    case "condition":
                    {
                        var replacement = new LogicalNot((IrExpression)branch.Condition.Clone());
                        replacement.InheritSourceOffset(branch);
                        branch.SetChild(0, replacement);
                        break;
                    }
                    case "arms":
                    {
                        StoreStackSlot left = Assert.IsType<StoreStackSlot>(whenTrue.Children[0]);
                        StoreStackSlot right = Assert.IsType<StoreStackSlot>(whenFalse.Children[0]);
                        IrExpression value = (IrExpression)left.Value.Clone();
                        left.SetChild(0, right.Value.Clone());
                        right.SetChild(0, value);
                        break;
                    }
                    case "extra-entry":
                    {
                        Branch exit = Assert.IsType<Branch>(whenFalse.Children[^1]);
                        var extra = new Block(whenTrue.StartOffset - 1);
                        extra.Add(new Branch(exit.TargetOffset));
                        container.Add(extra);
                        break;
                    }
                }
            }, fixtureType: ExpressionFixtureType);
        AssertPlanningCannotRepair(changed.Request, baseline.Request);
    }

    [Theory]
    [InlineData("condition")]
    [InlineData("type")]
    [InlineData("effect")]
    public void ClassicInverseAwaitedPredicatePlanningCannotLoseControl(string mutation)
    {
        using RequestScope scope = OpenExpressionRequest("BooleanChoiceCalls");
        Reconstruct(scope.Request);
        bool changed = false;
        ClassicInverseRequest request = CopyRequest(scope.Request, runPasses: (body, passes) =>
        {
            scope.Request.RunPasses!(body, passes);
            if (body.Name != "MoveNext")
                return;
            Conditional conditional = Assert.Single(body.Body.Descendants.OfType<Conditional>());
            if (mutation == "condition")
            {
                var not = new LogicalNot((IrExpression)conditional.Condition.Clone());
                not.InheritSourceOffset(conditional.Condition);
                conditional.SetChild(0, not);
            }
            else if (mutation == "type")
            {
                conditional.MergedType = TypeRef.CoreLib("System", "Double");
            }
            else
            {
                StoreLocal store = Assert.IsType<StoreLocal>(conditional.Parent);
                Block merge = Assert.IsType<Block>(store.Parent);
                var effect = new ExpressionStatement((IrExpression)conditional.WhenFalse.Clone());
                store.Detach();
                merge.Add(effect);
                merge.Add(store);
            }
            changed = true;
        });
        Assert.IsType<ClassicInverseDecision.Decline>(ClassicInverseCore.Decide(request));
        Assert.True(changed);
    }

    [Theory]
    [InlineData("member")]
    [InlineData("dispatch")]
    [InlineData("order")]
    public void ClassicInverseNestedInitializerRequiresExactEntries(string mutation)
    {
        using RequestScope scope = OpenExpressionRequest("NestedInitializerEntries");
        Reconstruct(scope.Request);
        var (planning, candidate, shell) = Candidate(scope.Request);
        InitializerBlock block = Assert.Single(candidate.Statements.SelectMany(node => node.Descendants)
            .OfType<InitializerBlock>());
        InitializerEntry[] entries = [.. block.Entries];
        if (mutation == "order")
            Array.Reverse(entries);
        else
            entries[0] = entries[0] with
            {
                ConsumedMethod = mutation == "member"
                    ? entries[0].ConsumedMethod! with { Name = "set_Other" } : entries[0].ConsumedMethod,
                ConsumedMethodIsVirtual = mutation != "dispatch" && entries[0].ConsumedMethodIsVirtual,
            };
        block.DetachChildren();
        block.ReplaceWith(new InitializerBlock(block.IsCollection, entries));
        Assert.IsType<ClassicInverseDecision.Decline>(ClassicInverseAccountant.Account(
            scope.Request, planning, candidate, shell, new ClassicInverseBudget()));
    }

    [Fact]
    public void ClassicInverseDeferredAwaitSpillRetainsItsOrderingWitness()
    {
        using RequestScope baseline = OpenExpressionRequest("NestedInitializer");
        Reconstruct(baseline.Request);
        using RequestScope changed = OpenRequest(OpenClassicFixture(), "NestedInitializer", ownsSource: true,
            mutateExecution: body =>
            {
                Call result = Assert.Single(body.Body.Descendants.OfType<Call>(),
                    call => call.Callee.Name == "GetResult");
                NewObject creation = Assert.Single(body.Body.Descendants.OfType<NewObject>());
                Assert.True(result.SourceOffset < creation.SourceOffset);
                creation.SetSourceOffset(result.SourceOffset - 1);
            }, fixtureType: ExpressionFixtureType);
        AssertPlanningCannotRepair(changed.Request, baseline.Request);
    }

    [Theory]
    [InlineData("TypeArguments")]
    [InlineData("BooleanChoiceTypeOf")]
    [InlineData("NestedInitializerEntries")]
    public void ClassicInverseExtendedExpressionPlansAreDetached(string method)
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
        Assert.Equal(signature, plan.Signature);
        Assert.Single(second.Descendants.OfType<AwaitExpression>());
        if (method == "NestedInitializerEntries")
        {
            InitializerBlock nested = Assert.Single(second.Descendants.OfType<InitializerBlock>());
            Assert.Equal(["Value", "Other"], nested.Members);
            Assert.All(nested.ConsumedMethods.Where(method => method is not null),
                method => Assert.Null(method!.ExactDefinitionAcquisitionGuard));
        }
        else
        {
            Assert.Equal(2, second.Descendants.OfType<TypeOf>().Count());
        }
    }

    [Theory]
    [InlineData("CoalesceTypeOf")]
    [InlineData("BooleanChoiceCalls")]
    [InlineData("NestedInitializerEntries")]
    public void ClassicInverseExtendedExpressionBudgetsRemainLoadBearing(string method)
    {
        using RequestScope scope = OpenExpressionRequest(method);
        var budget = new ClassicInverseBudget();
        Assert.IsType<ClassicInverseDecision.Reconstruct>(ClassicInverseCore.Decide(scope.Request, budget));
        var failure = Assert.IsType<ClassicInverseDecision.Failed>(
            ClassicInverseCore.Decide(scope.Request, new ClassicInverseBudget(budget.Consumed - 1)));
        Assert.Equal(ClassicInverseFailureKind.BudgetExhausted, failure.Failure.Kind);
        Assert.IsType<ClassicInverseDecision.Reconstruct>(
            ClassicInverseCore.Decide(scope.Request, new ClassicInverseBudget(budget.Consumed)));
    }
}
