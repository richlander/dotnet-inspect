using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

public sealed partial class ClassicInverseCoreTests
{
    const string NamedResultFixtureType =
        "ILInspector.Decompiler.Fixtures.ClassicAsync.NamedAwaitResultSamples";

    [Theory]
    [InlineData("completion-target")]
    [InlineData("user-condition")]
    [InlineData("catch-filter")]
    public void SingleAwaitNamedResultRejectsUnownedControl(string mutation)
    {
        using RequestScope scope = OpenNamedResultRequest("NamedReceiver");
        Reconstruct(scope.Request);
        bool changed = false;
        ClassicInverseRequest request = CopyRequest(scope.Request, runPasses: (execution, passes) =>
        {
            scope.Request.RunPasses!(execution, passes);
            if (execution.Name != "MoveNext")
                return;
            Call getResult = Assert.Single(execution.Body.Descendants.OfType<Call>(),
                call => call.Callee.Name == "GetResult");
            Block continuation = Assert.IsType<Block>(getResult.Parent!.Parent);
            BlockContainer body = Assert.IsType<BlockContainer>(continuation.Parent);
            TryCatch handler = Assert.IsType<TryCatch>(body.Parent);
            switch (mutation)
            {
                case "completion-target":
                {
                    ConditionalBranch branch = Assert.Single(body.Descendants.OfType<ConditionalBranch>(),
                        branch => branch.Condition is LoadProperty { PropertyName: "IsCompleted" });
                    var replacement = new ConditionalBranch(
                        (IrExpression)branch.Condition.Clone(), body.Blocks[2].StartOffset);
                    replacement.InheritSourceOffset(branch);
                    branch.ReplaceWith(replacement);
                    break;
                }
                case "user-condition":
                {
                    var guarded = new Block(continuation.StartOffset);
                    foreach (IrNode statement in continuation.DetachChildren())
                        guarded.Add(statement);
                    continuation.Add(new IfStatement(
                        new Constant(true, TypeRef.CoreLib("System", "Boolean")), guarded, null));
                    break;
                }
                case "catch-filter":
                {
                    CatchClause clause = Assert.Single(handler.Clauses);
                    clause.ReplaceWith(new CatchClause(clause.ExceptionType,
                        (BlockContainer)clause.Body.Clone(),
                        new Constant(true, TypeRef.CoreLib("System", "Boolean")))
                    {
                        VariableIndex = clause.VariableIndex,
                    });
                    break;
                }
            }
            execution.CheckInvariant();
            changed = true;
        });
        Assert.IsType<ClassicInverseDecision.Decline>(ClassicInverseCore.Decide(request));
        Assert.True(changed);
    }

    [Theory]
    [InlineData("NamedReceiver")]
    [InlineData("NamedReceiverTransform")]
    public void ClassicInverseNamedResultPlanPreservesDetachedBinding(string method)
    {
        using RequestScope scope = OpenNamedResultRequest(method);
        ClassicInversePlan plan = Reconstruct(scope.Request);
        Assert.Equal("result", Assert.Single(plan.LocalNames));
        Assert.Null(Assert.Single(plan.SynthesizedLocalNames));
        BlockContainer first = plan.MaterializeBody();
        Assert.IsType<StoreLocal>(first.Blocks[0].Children[0]);
        LoadLocalAddress address = Assert.Single(first.Descendants.OfType<LoadLocalAddress>());
        Assert.Equal(0, address.Index);
        Assert.Equal(Assert.Single(plan.Locals), address.Type);
        address.ReplaceWith(new LoadLocalAddress(99, address.Type));
        scope.Request.KickoffBody.Body.DetachChildren();
        scope.Request.ExecutionBody.Body.DetachChildren();

        BlockContainer second = plan.MaterializeBody();
        second.CheckInvariant();
        Assert.Equal(0, Assert.Single(second.Descendants.OfType<LoadLocalAddress>()).Index);
        Assert.Equal("result", Assert.Single(plan.LocalNames));
    }

    [Fact]
    public void ClassicInversePlanKeepsSynthesizedNamesSeparate()
    {
        using RequestScope scope = OpenRequest("AwaitInLoop");
        ClassicInversePlan plan = Reconstruct(scope.Request);
        Assert.Equal([null, null], plan.LocalNames);
        Assert.Equal(["sum", "task"], plan.SynthesizedLocalNames);
        var renamed = new ClassicInversePlan(plan.Recipe, plan.Body, plan.Locals,
            plan.LocalNames, ["total", "task"], plan.TypeFacts, plan.SourceOffset,
            plan.PhysicalPartition, plan.SemanticRealizations, plan.StructuredAncestorReceipts);
        Assert.NotEqual(plan, renamed);
        Assert.Equal(plan, Reconstruct(scope.Request));
    }

    [Fact]
    public void ClassicInverseAppliedArgumentsKeepKickoffBinders()
    {
        using MetadataSource source = OpenClassicFixture();
        IrFunction function = Assert.IsType<IrFunction>(
            IrImporter.Import(source, NamedResultFixtureType, "NamedReceiver"));
        Parameter parameter = Assert.Single(function.Signature.Parameters);
        IrPasses.Run(function, IrPasses.Default,
            PassContext.ForImport(method => IrImporter.Import(source, method)));
        function.CheckInvariant();
        Assert.Equal(DecompilationFidelity.Full, CSharpPrinter.Print(function).Fidelity);
        LoadArgument argument = Assert.Single(function.Body.Descendants.OfType<LoadArgument>());
        Assert.Same(parameter, argument.Parameter);
    }

    [Fact]
    public void ClassicInverseNamedResultRawOverwriteCannotBeHealedByPlanning()
    {
        using RequestScope baseline = OpenNamedResultRequest("NamedReceiver");
        Reconstruct(baseline.Request);
        using RequestScope changed = OpenRequest(OpenClassicFixture(), "NamedReceiver",
            ownsSource: true, mutateExecution: body =>
            {
                Call getResult = Assert.Single(body.Body.Descendants.OfType<Call>(),
                    call => call.Callee.Name == "GetResult");
                StoreLocal store = Assert.IsType<StoreLocal>(getResult.Parent);
                Block continuation = Assert.IsType<Block>(store.Parent);
                IReadOnlyList<IrNode> statements = continuation.DetachChildren();
                foreach (IrNode statement in statements)
                {
                    continuation.Add(statement);
                    if (ReferenceEquals(statement, store))
                    {
                        var overwrite = new StoreLocal(store.Index, store.Type, new DefaultValue(store.Type));
                        overwrite.InheritSourceOffset(store);
                        continuation.Add(overwrite);
                    }
                }
            }, fixtureType: NamedResultFixtureType);
        AssertPlanningCannotRepair(changed.Request, baseline.Request);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ClassicInverseUnnamedReceiverAddressCannotBeHealedByPlanning(bool changeType)
    {
        using RequestScope baseline = OpenNamedResultRequest("UnnamedReceiver");
        Reconstruct(baseline.Request);
        using RequestScope changed = OpenRequest(OpenClassicFixture(), "UnnamedReceiver",
            ownsSource: true, mutateExecution: body =>
            {
                Call getter = Assert.Single(body.Body.Descendants.OfType<Call>(),
                    call => call.Callee.Name == "get_Response");
                LoadLocalAddress address = Assert.IsType<LoadLocalAddress>(Assert.Single(getter.Arguments));
                Call getResult = Assert.Single(body.Body.Descendants.OfType<Call>(),
                    call => call.Callee.Name == "GetResult");
                LoadLocalAddress awaiter = Assert.IsType<LoadLocalAddress>(Assert.Single(getResult.Arguments));
                Assert.NotEqual(address.Index, awaiter.Index);
                var replacement = new LoadLocalAddress(changeType ? address.Index : awaiter.Index,
                    changeType ? TypeRef.CoreLib("System", "Int32") : address.Type);
                replacement.InheritSourceOffset(address);
                address.ReplaceWith(replacement);
            }, fixtureType: NamedResultFixtureType);
        AssertPlanningCannotRepair(changed.Request, baseline.Request);
    }

    [Theory]
    [InlineData("NamedReceiver")]
    [InlineData("UnnamedReceiver")]
    public void ClassicInverseNamedResultBudgetRemainsLoadBearing(string method)
    {
        using RequestScope scope = OpenNamedResultRequest(method);
        var budget = new ClassicInverseBudget();
        Assert.IsType<ClassicInverseDecision.Reconstruct>(
            ClassicInverseCore.Decide(scope.Request, budget));
        var failed = Assert.IsType<ClassicInverseDecision.Failed>(
            ClassicInverseCore.Decide(scope.Request, new ClassicInverseBudget(budget.Consumed - 1)));
        Assert.Equal(ClassicInverseFailureKind.BudgetExhausted, failed.Failure.Kind);
    }

    static RequestScope OpenNamedResultRequest(string method)
        => OpenRequest(OpenClassicFixture(), method, ownsSource: true,
            fixtureType: NamedResultFixtureType);
}
