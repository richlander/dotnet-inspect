using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

public sealed partial class ClassicInverseCoreTests
{
    [Theory]
    [InlineData("literal")]
    [InlineData("alignment")]
    [InlineData("format")]
    [InlineData("member")]
    [InlineData("dispatch")]
    [InlineData("order")]
    [InlineData("extra-use")]
    public void ClassicInverseInterpolationCannotBeHealedByPlanning(string mutation)
    {
        using RequestScope baseline = OpenExpressionRequest("InterpolatedFormats");
        Reconstruct(baseline.Request);
        using RequestScope changed = OpenRequest(OpenClassicFixture(), "InterpolatedFormats", ownsSource: true,
            fixtureType: ExpressionFixtureType, mutateExecution: body =>
            {
                Call formatted = Assert.Single(body.Body.Descendants.OfType<Call>(),
                    call => call.Callee.Name == "AppendFormatted");
                if (mutation is "alignment" or "format" or "literal")
                {
                    Constant value = mutation == "literal"
                        ? Assert.IsType<Constant>(body.Body.Descendants.OfType<Call>()
                            .First(call => call.Callee.Name == "AppendLiteral").Arguments[1])
                        : Assert.IsType<Constant>(formatted.Arguments[mutation == "alignment" ? 2 : 3]);
                    var replacement = new Constant(mutation == "alignment" ? -8
                        : mutation == "format" ? "X4" : "changed", value.Type);
                    replacement.InheritSourceOffset(value);
                    value.ReplaceWith(replacement);
                }
                else if (mutation is "member" or "dispatch")
                {
                    var replacement = new Call(
                        mutation == "member" ? formatted.Callee with { TypeArguments = [TypeRef.CoreLib("System", "Int64")] }
                            : formatted.Callee,
                        mutation == "dispatch",
                        formatted.Arguments.Select(argument => (IrExpression)argument.Clone()).ToArray());
                    replacement.InheritSourceOffset(formatted);
                    formatted.ReplaceWith(replacement);
                }
                else
                {
                    StoreLocal store = Assert.Single(body.Body.Descendants.OfType<StoreLocal>(),
                        node => node.Value is NewObject creation
                            && MemberIdentity.IsDefaultInterpolatedStringHandlerConstructor(creation));
                    Block block = Assert.IsType<Block>(store.Parent);
                    IrNode[] statements = [.. block.DetachChildren()];
                    int position = Array.IndexOf(statements, store);
                    if (mutation == "order")
                        (statements[position + 1], statements[position + 2]) =
                            (statements[position + 2], statements[position + 1]);
                    foreach (IrNode statement in statements)
                    {
                        block.Add(statement);
                        if (mutation == "extra-use" && ReferenceEquals(statement, store))
                            block.Add(new StoreStackSlot(999, new LoadLocal(store.Index, store.Type)));
                    }
                }
            });
        AssertPlanningCannotRepair(changed.Request, baseline.Request);
    }

    [Theory]
    [InlineData("InterpolatedFormats", "parts")]
    [InlineData("InterpolatedFormats", "format")]
    [InlineData("InterpolatedFormats", "member")]
    [InlineData("InterpolatedZeroAlignment", "alignment")]
    public void ClassicInverseInterpolationOutputKeepsItsParts(string method, string mutation)
    {
        using RequestScope scope = OpenExpressionRequest(method);
        Reconstruct(scope.Request);
        var (planning, candidate, shell) = Candidate(scope.Request);
        InterpolatedStringExpression interpolation = Assert.Single(candidate.Statements
            .SelectMany(statement => statement.Descendants).OfType<InterpolatedStringExpression>());
        InterpolatedStringPart[] parts = [.. interpolation.Parts];
        var members = interpolation.ConsumedMemberRefs;
        int hole = Array.FindIndex(parts, part => !part.IsLiteral);
        if (mutation == "parts")
            Array.Reverse(parts);
        else if (mutation == "member")
            members = members.SetItem(hole + 1, members[hole + 1] with { Name = "AppendOther" });
        else
            parts[hole] = parts[hole] with
            {
                Format = mutation == "alignment"
                    ? parts[hole].Format! with { HasAlignment = false }
                    : parts[hole].Format! with { FormatString = "X4" },
            };
        var replacement = new InterpolatedStringExpression(parts,
            interpolation.DetachChildren().Cast<IrExpression>(), members);
        replacement.InheritSourceOffset(interpolation);
        interpolation.ReplaceWith(replacement);
        Assert.IsType<ClassicInverseDecision.Decline>(ClassicInverseAccountant.Account(
            scope.Request, planning, candidate, shell, new ClassicInverseBudget()));
    }

    [Fact]
    public void ClassicInverseInterpolationRequiresAndRetainsOrigins()
    {
        using RequestScope scope = OpenExpressionRequest("InterpolatedFormats");
        ClassicInversePlan plan = Reconstruct(scope.Request);
        var receipt = Assert.Single(plan.SemanticRealizations, item =>
            item.SourceEffects.Any(effect => effect.Contains("AppendFormatted", StringComparison.Ordinal)));
        Assert.Equal(receipt.SourceEffects, receipt.OutputEffects);
        Assert.Single(receipt.SourceEffects, effect => effect.StartsWith("newobj:", StringComparison.Ordinal));
        Assert.Single(receipt.SourceEffects, effect => effect.Contains("ToStringAndClear", StringComparison.Ordinal));
        Call append = Assert.Single(scope.Request.ExecutionBody.Body.Descendants.OfType<Call>(),
            call => call.Callee.Name == "AppendFormatted");
        Assert.Contains(append.SourceOffset, receipt.ImportOffsets);
        Assert.Contains(append.Arguments[2].SourceOffset, receipt.ImportOffsets);
        Assert.Contains(append.Arguments[3].SourceOffset, receipt.ImportOffsets);
        Assert.Equal(scope.Request.ExecutionBody.Body.Descendants.Count() + 1,
            CountCoveredNodes(scope.Request.ExecutionBody.Body,
                plan.PhysicalPartition.Where(region => region.Body == ClassicInverseBodyId.Execution)));
        ClassicInverseRequest missing = CopyRequest(scope.Request, runPasses: (body, passes) =>
        {
            scope.Request.RunPasses!(body, passes);
            if (body.Name == "MoveNext")
                Assert.Single(body.Body.Descendants.OfType<InterpolatedStringExpression>()).SetSourceOffset(-1);
        });
        Assert.IsType<ClassicInverseDecision.Decline>(ClassicInverseCore.Decide(missing));
    }

    [Theory]
    [InlineData("InterpolatedFormats")]
    [InlineData("InterpolatedGeneric")]
    [InlineData("YieldOnce")]
    [InlineData("MutableStructOperand")]
    [InlineData("StructFactoryOperand")]
    public void ClassicInverseInterpolationAndBindsAreDetached(string method)
    {
        using RequestScope scope = OpenExpressionRequest(method);
        ClassicInversePlan plan = Reconstruct(scope.Request);
        string signature = plan.Signature;
        BlockContainer first = plan.MaterializeBody();
        first.DetachChildren();
        scope.Request.ExecutionBody.Body.DetachChildren();
        scope.Request.KickoffBody.Body.DetachChildren();
        BlockContainer second = plan.MaterializeBody();
        second.CheckInvariant();
        Assert.Single(second.Descendants.OfType<AwaitExpression>());
        Assert.Equal(signature, plan.Signature);
        foreach (InterpolatedStringExpression interpolation in second.Descendants.OfType<InterpolatedStringExpression>())
        {
            Assert.Equal(interpolation.Parts.Length + 2, interpolation.ConsumedMemberRefs.Length);
            Assert.All(interpolation.ConsumedMemberRefs, member => Assert.Null(member.ExactDefinitionAcquisitionGuard));
            if (method == "InterpolatedGeneric")
            {
                var formatted = Assert.Single(interpolation.ConsumedMemberRefs, member => member.Name == "AppendFormatted");
                Assert.Equal(TypeRef.MethodGenericParameter(0), Assert.Single(formatted.TypeArguments));
            }
        }
    }

    [Theory]
    [InlineData("ClassAwaitableOperand", "direct")]
    [InlineData("StructOperand", "value-copy")]
    [InlineData("StructOperand", "field")]
    [InlineData("StructOperand", "type")]
    [InlineData("YieldOnce", "order")]
    [InlineData("YieldOnce", "extra-read")]
    [InlineData("StructFactoryOperand", "extra-read")]
    public void ClassicInverseValueBindCannotBeHealedByPlanning(string method, string mutation)
    {
        using RequestScope baseline = OpenExpressionRequest(method);
        Reconstruct(baseline.Request);
        using RequestScope changed = OpenRequest(OpenClassicFixture(), method, ownsSource: true,
            fixtureType: ExpressionFixtureType, mutateExecution: body =>
            {
                Call getAwaiter = Assert.Single(body.Body.Descendants.OfType<Call>(),
                    call => call.Callee.Name == "GetAwaiter");
                if (mutation == "direct")
                {
                    var replacement = new Call(getAwaiter.Callee, false,
                        getAwaiter.Arguments.Select(argument => (IrExpression)argument.Clone()).ToArray());
                    replacement.InheritSourceOffset(getAwaiter);
                    getAwaiter.ReplaceWith(replacement);
                }
                else if (mutation is "value-copy" or "field" or "type")
                {
                    LoadFieldAddress address = Assert.IsType<LoadFieldAddress>(getAwaiter.Arguments[0]);
                    IrExpression replacement = mutation == "value-copy"
                        ? new LoadField(address.Field, (IrExpression)address.Instance!.Clone())
                        : new LoadFieldAddress(address.Field with
                        {
                            Name = mutation == "field" ? "other" : address.Field.Name,
                            Type = mutation == "type" ? TypeRef.CoreLib("System", "Int32") : address.Field.Type,
                        }, (IrExpression)address.Instance!.Clone());
                    replacement.InheritSourceOffset(address);
                    address.ReplaceWith(replacement);
                }
                else
                {
                    LoadLocalAddress address = Assert.IsType<LoadLocalAddress>(getAwaiter.Arguments[0]);
                    StoreLocal bind = Assert.IsType<StoreLocal>(getAwaiter.Parent);
                    Block block = Assert.IsType<Block>(bind.Parent);
                    IrNode[] statements = [.. block.DetachChildren()];
                    int position = Array.IndexOf(statements, bind);
                    if (mutation == "order")
                        (statements[position - 1], statements[position]) = (statements[position], statements[position - 1]);
                    foreach (IrNode statement in statements)
                    {
                        block.Add(statement);
                        if (mutation == "extra-read" && ReferenceEquals(statement, bind))
                            block.Add(new StoreStackSlot(999, new LoadLocal(address.Index, address.Type)));
                    }
                }
            });
        AssertPlanningCannotRepair(changed.Request, baseline.Request);
    }

    [Theory]
    [Trait("Speed", "Slow")]
    [InlineData("InterpolatedFormats")]
    [InlineData("InterpolatedGeneric")]
    [InlineData("YieldOnce")]
    [InlineData("StructOperand")]
    [InlineData("StructFactoryOperand")]
    public void ClassicInverseInterpolationAndBindBudgetCutsNeverDecline(string method)
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
