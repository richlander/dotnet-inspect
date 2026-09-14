using ILInspector.Decompiler.Pipeline;
using ILInspector.Metadata;
using DotnetInspector.Fixtures;
using ILInspector.DecompilerHarness;

namespace ILInspector.Decompiler.Tests;

public sealed partial class ClassicInverseCoreTests
{
    [Theory]
    [InlineData("AwaitValue", false)]
    [InlineData("TwoSequentialAwaits", false)]
    [InlineData("AwaitInLoop", false)]
    [InlineData("AwaitInTryFinally", false)]
    [InlineData("GenericAwait", true)]
    [InlineData("ConfiguredTask", true)]
    public void ClassicInverseAwaitRetainsDetachedPatternMembers(string method, bool expression)
    {
        using RequestScope scope = expression ? OpenExpressionRequest(method) : OpenRequest(method);
        ClassicInversePlan plan = Reconstruct(scope.Request);
        BlockContainer first = plan.MaterializeBody();
        var awaits = first.Descendants.OfType<AwaitExpression>().ToArray();
        Assert.NotEmpty(awaits);
        foreach (AwaitExpression awaited in awaits)
        {
            Assert.Equal(new[] { "GetAwaiter", "get_IsCompleted", "GetResult" },
                awaited.ConsumedMemberRefs.Select(member => member.Name));
            Assert.Equal(awaited.ResultType, awaited.ConsumedMemberRefs[2].ReturnType);
            Assert.All(awaited.ConsumedMemberRefs, member => Assert.Null(member.ExactDefinitionAcquisitionGuard));
        }
        string signature = plan.Signature;
        first.DetachChildren();
        scope.Request.ExecutionBody.Body.DetachChildren();
        BlockContainer second = plan.MaterializeBody();
        second.CheckInvariant();
        Assert.Equal(awaits.Length, second.Descendants.OfType<AwaitExpression>().Count());
        Assert.Equal(signature, plan.Signature);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ClassicInverseAwaitRejectsChangedConsumedMembers(bool changeIdentity)
    {
        using RequestScope scope = OpenRequest("AwaitValue");
        var (_, candidate, shell) = Candidate(scope.Request);
        ClassicInverseClaim claim = Assert.Single(candidate.Claims,
            claim => claim.Rule == ClassicInverseRealizationRule.AwaitResult);
        var original = Assert.IsType<AwaitExpression>(claim.Output);
        IrExpression operand = (IrExpression)Assert.Single(original.DetachChildren());
        var members = changeIdentity
            ? original.ConsumedMemberRefs.SetItem(1, original.ConsumedMemberRefs[1] with { Name = "get_Other" })
            : original.ConsumedMemberRefs.RemoveAt(1);
        var replacement = new AwaitExpression(operand, original.ResultType, original.ResultIsDynamic, members);
        Assert.False(ClassicInverseRealizationRules.Verify(
            claim with { Output = replacement }, candidate, shell,
            candidate.Claims.ToDictionary(item => item.Source),
            candidate.Claims.ToDictionary(item => item.Output),
            new ClassicInverseBudget(), out string failure));
        Assert.Contains("exact proven pattern members", failure, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("AwaitUnsafeProperty")]
    [InlineData("AwaitPointerReceiver")]
    public void ClassicInverseUnsafeAwaitDeclinesThroughCore(string method)
    {
        using RequestScope scope = OpenRequest(method);
        var budget = new ClassicInverseBudget();
        var decline = Assert.IsType<ClassicInverseDecision.Decline>(
            ClassicInverseCore.Decide(scope.Request, budget));
        Assert.Equal(ClassicInverseDeclineReason.UnsafeAwaitContext, decline.Reason);
        var failed = Assert.IsType<ClassicInverseDecision.Failed>(
            ClassicInverseCore.Decide(scope.Request, new ClassicInverseBudget(budget.Consumed - 1)));
        Assert.Equal(ClassicInverseFailureKind.BudgetExhausted, failed.Failure.Kind);
        scope.Request.ExecutionBody.CheckInvariant();
    }

    [Fact]
    public void ClassicInverseAwaitRetainsInvalidMemberFacts()
    {
        using RequestScope scope = OpenMutatedRequest("AwaitValue", body =>
        {
            Call result = Assert.Single(body.Body.Descendants.OfType<Call>(),
                call => call.Callee.Name == "GetResult");
            var replacement = new Call(result.Callee with
            {
                MemorySafetyRulesState = MemorySafetyRulesState.Unsupported,
            }, result.IsVirtual, result.Arguments.Select(argument => (IrExpression)argument.Clone()).ToArray());
            replacement.InheritSourceOffset(result);
            result.ReplaceWith(replacement);
        });
        ClassicInversePlan plan = Reconstruct(scope.Request);
        AwaitExpression awaited = Assert.Single(plan.MaterializeBody().Descendants.OfType<AwaitExpression>());
        Assert.Equal(MemorySafetyRulesState.Unsupported, awaited.ConsumedMemberRefs[2].MemorySafetyRulesState);
        using RequestScope ordinary = OpenRequest("AwaitValue");
        Assert.NotEqual(Reconstruct(ordinary.Request), plan);
    }

    [Theory]
    [InlineData("AwaitInitializedStackalloc")]
    [InlineData("AwaitInitializedByteStackalloc")]
    public void ClassicInverseInitializedStackallocRemainsSupported(string method)
    {
        using RequestScope scope = OpenExpressionRequest(method);
        ClassicInverseDecision decision = ClassicInverseCore.Decide(scope.Request);
        Assert.True(decision is ClassicInverseDecision.Reconstruct, decision.ToString());
        BlockContainer body = ((ClassicInverseDecision.Reconstruct)decision).Plan.MaterializeBody();
        Assert.Single(body.Descendants.OfType<StackAllocArray>());
        Assert.Single(body.Descendants.OfType<AwaitExpression>());
        body.CheckInvariant();
        Assert.Equal(DecompilationFidelity.Full, DecompileExpressionFixture(method).Fidelity);
    }

    [Theory]
    [InlineData("byte-size")]
    [InlineData("count-slot")]
    [InlineData("extra-read")]
    [InlineData("extra-writer")]
    [InlineData("checked")]
    [InlineData("origin")]
    public void ClassicInverseStackallocCannotBeHealedByPlanning(string mutation)
    {
        using RequestScope baseline = OpenExpressionRequest("AwaitInitializedStackalloc");
        Reconstruct(baseline.Request);
        using RequestScope changed = OpenRequest(OpenClassicFixture(), "AwaitInitializedStackalloc",
            ownsSource: true, fixtureType: ExpressionFixtureType, mutateExecution: body =>
            {
                NewObject span = Assert.Single(body.Body.Descendants.OfType<NewObject>(),
                    creation => MemberIdentity.IsStackAllocSpanConstructor(creation, out _));
                StackAllocate allocation = Assert.IsType<StackAllocate>(span.Arguments[0]);
                LoadLocal count = Assert.IsType<LoadLocal>(span.Arguments[1]);
                Binary bytes = Assert.IsType<Binary>(allocation.Size);
                if (mutation == "byte-size")
                {
                    Constant size = Assert.IsType<Constant>(bytes.Right);
                    var replacement = new Constant(8, size.Type);
                    replacement.InheritSourceOffset(size);
                    size.ReplaceWith(replacement);
                }
                else if (mutation == "checked")
                {
                    var replacement = new Binary(bytes.Kind, false, bytes.IsUnsigned,
                        (IrExpression)bytes.Left.Clone(), (IrExpression)bytes.Right.Clone());
                    replacement.InheritSourceOffset(bytes);
                    bytes.ReplaceWith(replacement);
                }
                else if (mutation == "origin")
                    allocation.SetSourceOffset(-1);
                else if (mutation == "count-slot")
                {
                    var replacement = new LoadLocal(count.Index + 10, count.Type);
                    replacement.InheritSourceOffset(count);
                    count.ReplaceWith(replacement);
                }
                else
                {
                    StoreLocal store = Assert.Single(body.Body.Descendants.OfType<StoreLocal>(),
                        node => node.Index == count.Index);
                    Block block = Assert.IsType<Block>(store.Parent);
                    IrNode[] statements = [.. block.DetachChildren()];
                    foreach (IrNode statement in statements)
                    {
                        block.Add(statement);
                        if (ReferenceEquals(statement, store))
                            block.Add(mutation == "extra-writer" ? store.Clone()
                                : new StoreStackSlot(999, (IrExpression)count.Clone()));
                    }
                }
            });
        AssertPlanningCannotRepair(changed.Request, baseline.Request);
    }

    [Theory]
    [InlineData("AwaitInitializedStackalloc", true)]
    [InlineData("AwaitInitializedByteStackalloc", false)]
    public void ClassicInverseStackallocKeepsPrimitiveEffects(string method, bool checkedSize)
    {
        using RequestScope scope = OpenExpressionRequest(method);
        ClassicInversePlan plan = Reconstruct(scope.Request);
        ClassicInverseSemanticRealization receipt = Assert.Single(plan.SemanticRealizations,
            item => item.SourceEffects.Contains("alloc:stack"));
        Assert.Equal(receipt.SourceEffects, receipt.OutputEffects);
        Assert.Equal(checkedSize, receipt.SourceEffects.Contains("throw:checked-Multiply"));
        Assert.Single(receipt.SourceEffects, effect => effect.StartsWith("newobj:", StringComparison.Ordinal));
        StackAllocate allocation = Assert.Single(scope.Request.ExecutionBody.Body.Descendants.OfType<StackAllocate>());
        Assert.Contains(allocation.SourceOffset, receipt.ImportOffsets);
        Assert.Contains(allocation.Size.SourceOffset, receipt.ImportOffsets);
        Assert.Equal(scope.Request.ExecutionBody.Body.Descendants.Count() + 1,
            CountCoveredNodes(scope.Request.ExecutionBody.Body,
                plan.PhysicalPartition.Where(region => region.Body == ClassicInverseBodyId.Execution)));
        plan.MaterializeBody().DetachChildren();
        scope.Request.ExecutionBody.Body.DetachChildren();
        BlockContainer materialized = plan.MaterializeBody();
        materialized.CheckInvariant();
        Assert.Single(materialized.Descendants.OfType<StackAllocArray>());
    }

    [Fact]
    public void ClassicInverseStackallocKeepsUnsafeDecline()
    {
        using RequestScope scope = OpenExpressionRequest("AwaitUninitializedStackalloc");
        var result = Assert.IsType<ClassicInverseDecision.Decline>(ClassicInverseCore.Decide(scope.Request));
        Assert.Equal(ClassicInverseDeclineReason.UnsafeAwaitContext, result.Reason);
    }

    [Theory]
    [Trait("Speed", "Slow")]
    [InlineData("AwaitInitializedStackalloc")]
    [InlineData("AwaitInitializedByteStackalloc")]
    public void ClassicInverseStackallocOutputsCompileBack(string method)
    {
        var result = Assert.Single(FidelityCheck.Evaluate(FixtureCatalog.DecompilerClassicAsync.AssemblyPath(),
            type => type == ExpressionFixtureType, candidate => candidate.Method == method));
        Assert.True(result.Status == FidelityCheck.CompileBackStatus.Exact,
            $"{result.Method}: {result.Status}: {result.Detail}");
    }

    [Theory]
    [Trait("Speed", "Slow")]
    [InlineData("AwaitInitializedStackalloc")]
    [InlineData("AwaitInitializedByteStackalloc")]
    public void ClassicInverseStackallocBudgetCutsNeverDecline(string method)
    {
        using RequestScope scope = OpenExpressionRequest(method);
        var complete = new ClassicInverseBudget();
        Assert.IsType<ClassicInverseDecision.Reconstruct>(ClassicInverseCore.Decide(scope.Request, complete));
        for (int limit = 1; limit <= complete.Consumed; limit++)
        {
            var budget = new ClassicInverseBudget(limit);
            ClassicInverseDecision result = ClassicInverseCore.Decide(scope.Request, budget);
            Assert.True(budget.Exhausted
                ? result is ClassicInverseDecision.Failed { Failure.Kind: ClassicInverseFailureKind.BudgetExhausted }
                : result is ClassicInverseDecision.Reconstruct,
                $"{method}: {limit}/{complete.Consumed}: {result}");
        }
    }
}
