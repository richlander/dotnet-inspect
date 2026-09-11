using System.Collections.Immutable;
using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

public sealed partial class ClassicInverseCoreTests
{
    [Theory]
    [InlineData("ConstantArrayOperand", "data")]
    [InlineData("ConstantByteArrayOperand", "data")]
    [InlineData("ConstantArrayOperand", "count")]
    [InlineData("ConstantArrayOperand", "escape")]
    [InlineData("FromEndResult", "checked")]
    [InlineData("SliceResult", "polarity")]
    [InlineData("ReadOnlySpanLiteral", "index")]
    [InlineData("ReadOnlySpanLiteral", "member")]
    [InlineData("RangeResult", "range-read")]
    [InlineData("RangeResult", "range-conversion")]
    public void ClassicInverseInventoryRawEvidenceCannotBeHealed(string method, string mutation)
    {
        using RequestScope baseline = OpenInventoryRequest(method);
        Reconstruct(baseline.Request);
        using RequestScope changed = OpenRequest(OpenClassicFixture(), method, ownsSource: true,
            fixtureType: InventoryFixtureType, mutateExecution: body =>
            {
                if (mutation == "data")
                {
                    LoadToken token = Assert.Single(body.Body.Descendants.OfType<LoadToken>(),
                        token => token.FieldRvaData is not null);
                    token.FieldRvaData![0] ^= 1;
                }
                else if (mutation == "count")
                {
                    NewArray array = Assert.Single(body.Body.Descendants.OfType<NewArray>());
                    Constant count = Assert.IsType<Constant>(array.Length);
                    var replacement = new Constant((int)count.Value! + 1, count.Type);
                    replacement.InheritSourceOffset(count);
                    count.ReplaceWith(replacement);
                }
                else if (mutation == "escape")
                {
                    StoreStackSlot store = Assert.Single(body.Body.Descendants.OfType<StoreStackSlot>(),
                        store => store.Value is NewArray);
                    var block = Assert.IsType<Block>(store.Parent);
                    block.Add(new StoreStackSlot(999, new LoadStackSlot(store.Slot, store.Value.ResultType!)));
                }
                else if (mutation == "checked")
                {
                    Binary subtract = Assert.Single(body.Body.Descendants.OfType<Binary>());
                    var replacement = new Binary(subtract.Kind, true, subtract.IsUnsigned,
                        (IrExpression)subtract.Left.Clone(), (IrExpression)subtract.Right.Clone());
                    replacement.InheritSourceOffset(subtract);
                    subtract.ReplaceWith(replacement);
                }
                else if (mutation == "polarity")
                {
                    NewObject index = Assert.Single(body.Body.Descendants.OfType<NewObject>(),
                        creation => MemberIdentity.IsIndexFromEndConstructor(creation.Constructor));
                    Constant polarity = Assert.IsType<Constant>(index.Arguments[1]);
                    var replacement = new Constant(0, polarity.Type);
                    replacement.InheritSourceOffset(polarity);
                    polarity.ReplaceWith(replacement);
                }
                else if (mutation is "range-read" or "range-conversion")
                {
                    Call result = Assert.Single(body.Body.Descendants.OfType<Call>(), call => call.Callee.Name == "GetResult");
                    if (mutation == "range-read")
                    {
                        StoreLocal store = Assert.IsType<StoreLocal>(result.Parent);
                        Assert.IsType<Block>(store.Parent).Add(new StoreStackSlot(999, new LoadLocal(store.Index, store.Type)));
                    }
                    else
                    {
                        Call conversion = Assert.Single(body.Body.Descendants.OfType<Call>(),
                            call => MemberIdentity.IsIndexFromStartConversion(call.Callee));
                        var replacement = new Call(conversion.Callee with { Name = "op_Explicit" }, false,
                            conversion.Arguments.Select(argument => (IrExpression)argument.Clone()).ToArray());
                        replacement.InheritSourceOffset(conversion);
                        conversion.ReplaceWith(replacement);
                    }
                }
                else
                {
                    Call address = body.Body.Descendants.OfType<Call>().First(call => call.Callee.Name == "InlineArrayElementRef");
                    if (mutation == "index")
                    {
                        Constant index = Assert.IsType<Constant>(address.Arguments[1]);
                        var replacement = new Constant(1, index.Type);
                        replacement.InheritSourceOffset(index);
                        index.ReplaceWith(replacement);
                    }
                    else
                    {
                        var replacement = new Call(address.Callee with { Name = "OtherElementRef" }, false,
                            address.Arguments.Select(argument => (IrExpression)argument.Clone()).ToArray());
                        replacement.InheritSourceOffset(address);
                        address.ReplaceWith(replacement);
                    }
                }
            });
        AssertPlanningCannotRepair(changed.Request, baseline.Request);
    }

    [Theory]
    [InlineData("AnonymousOperand")]
    [InlineData("DelegateOperand")]
    [InlineData("ByRefArgument")]
    [InlineData("ReadOnlySpanLiteral")]
    public void ClassicInverseInventoryOutputRetainsTypedPayload(string method)
    {
        using RequestScope scope = OpenInventoryRequest(method);
        Reconstruct(scope.Request);
        var (planning, candidate, shell) = Candidate(scope.Request);
        IrNode[] nodes = [.. candidate.Statements.SelectMany(statement => statement.Descendants)];
        if (method == "AnonymousOperand")
        {
            AnonymousObject value = Assert.Single(nodes.OfType<AnonymousObject>());
            var replacement = new AnonymousObject(value.Type, ["Other"],
                value.DetachChildren().Cast<IrExpression>(), value.Constructor);
            replacement.InheritSourceOffset(value);
            value.ReplaceWith(replacement);
        }
        else if (method == "DelegateOperand")
        {
            DelegateCreation value = Assert.Single(nodes.OfType<DelegateCreation>());
            var target = (IrExpression)Assert.Single(value.DetachChildren());
            var replacement = new DelegateCreation(value.DelegateType, value.Method with { Name = "Other" },
                value.IsVirtual, target, value.Constructor);
            replacement.InheritSourceOffset(value);
            value.ReplaceWith(replacement);
        }
        else if (method == "ByRefArgument")
        {
            LoadFieldAddress value = Assert.Single(nodes.OfType<LoadFieldAddress>());
            var instance = (IrExpression)Assert.Single(value.DetachChildren());
            var replacement = new LoadFieldAddress(value.Field with { Name = "Other" }, instance);
            replacement.InheritSourceOffset(value);
            value.ReplaceWith(replacement);
        }
        else
        {
            CollectionExpression value = Assert.Single(nodes.OfType<CollectionExpression>());
            IrExpression[] elements = [.. value.DetachChildren().Cast<IrExpression>()];
            var replacement = new CollectionExpression(TypeRef.CoreLib("System", "Byte"), value.TargetType,
                elements, value.ConsumedMemberRefs);
            replacement.InheritSourceOffset(value);
            value.ReplaceWith(replacement);
        }
        Assert.IsType<ClassicInverseDecision.Decline>(ClassicInverseAccountant.Account(
            scope.Request, planning, candidate, shell, new ClassicInverseBudget()));
    }

    [Theory]
    [InlineData("PriorInterpolationArgument", "slot")]
    [InlineData("PriorInterpolationArgument", "order")]
    [InlineData("NestedInterpolation", "origin")]
    public void ClassicInversePriorOperandRequiresItsExactOrder(string method, string mutation)
    {
        using RequestScope baseline = OpenInventoryRequest(method);
        Reconstruct(baseline.Request);
        using RequestScope changed = OpenRequest(OpenClassicFixture(), method, ownsSource: true,
            fixtureType: InventoryFixtureType, mutateExecution: body =>
            {
                if (mutation == "origin")
                {
                    Constant literal = Assert.Single(body.Body.Descendants.OfType<Constant>(),
                        value => Equals(value.Value, "["));
                    literal.SetSourceOffset(literal.SourceOffset + 1000);
                    return;
                }
                StoreStackSlot store = Assert.Single(body.Body.Descendants.OfType<StoreStackSlot>(),
                    node => node.Value is Call { Callee.Name: "Tick" });
                if (mutation == "slot")
                {
                    LoadStackSlot use = Assert.Single(body.Body.Descendants.OfType<LoadStackSlot>(),
                        read => read.Slot == store.Slot);
                    var replacement = new LoadStackSlot(use.Slot + 1000, use.Type);
                    replacement.InheritSourceOffset(use);
                    use.ReplaceWith(replacement);
                    return;
                }
                Block block = Assert.IsType<Block>(store.Parent);
                IrNode[] statements = [.. block.DetachChildren()];
                int index = Array.IndexOf(statements, store);
                (statements[index], statements[index + 1]) = (statements[index + 1], statements[index]);
                foreach (IrNode statement in statements)
                    block.Add(statement);
            });
        AssertPlanningCannotRepair(changed.Request, baseline.Request);
    }

    [Theory]
    [InlineData("GenericAny", "constraint")]
    [InlineData("GenericStruct", "receiver")]
    [InlineData("GenericClass", "class")]
    public void ClassicInverseInterfaceAwaitKeepsConstraintAndDispatch(string method, string mutation)
    {
        using RequestScope scope = OpenInventoryRequest(method);
        Reconstruct(scope.Request);
        if (mutation == "receiver")
        {
            Call call = Assert.Single(scope.Request.ExecutionBody.Body.Descendants.OfType<Call>(),
                call => call.Callee.Name == "GetAwaiter");
            var replacement = new Call(call.Callee, call.IsVirtual,
                call.Arguments.Select(argument => (IrExpression)argument.Clone()).ToArray())
            {
                ConstrainedTo = TypeRef.GenericParameter(17),
            };
            replacement.InheritSourceOffset(call);
            call.ReplaceWith(replacement);
            Assert.IsType<ClassicInverseDecision.Decline>(ClassicInverseCore.Decide(scope.Request));
            return;
        }
        var parameters = scope.Request.ExecutionBody.DeclaringTypeParameters;
        var first = parameters[0];
        scope.Request.ExecutionBody.DeclaringTypeParameters = parameters.SetItem(0, mutation == "constraint"
            ? first with { Types = [] }
            : first with { Attributes = first.Attributes & ~System.Reflection.GenericParameterAttributes.ReferenceTypeConstraint });
        AssertInvalidCorrelation(scope.Request);
    }

    [Theory]
    [InlineData("ConditionalAwaitable", "target")]
    [InlineData("CoalesceAwaitable", "slot")]
    [InlineData("SwitchResult", "label")]
    [InlineData("SwitchResult", "use")]
    [InlineData("SwitchEffects", "effect")]
    public void ClassicInverseSelectedValuesCannotBeHealed(string method, string mutation)
    {
        using RequestScope baseline = OpenInventoryRequest(method);
        Reconstruct(baseline.Request);
        using RequestScope changed = OpenRequest(OpenClassicFixture(), method, ownsSource: true,
            fixtureType: InventoryFixtureType, mutateExecution: body =>
            {
                if (mutation == "target")
                {
                    ConditionalBranch test = Assert.Single(body.Body.Descendants.OfType<ConditionalBranch>(),
                        test => test.Condition is LoadField { Field.Name: "value" });
                    Block block = Assert.IsType<Block>(test.Parent);
                    BlockContainer container = Assert.IsType<BlockContainer>(block.Parent);
                    var replacement = new ConditionalBranch((IrExpression)test.Condition.Clone(),
                        container.Blocks[block.ChildIndex + 1].StartOffset);
                    replacement.InheritSourceOffset(test);
                    test.ReplaceWith(replacement);
                }
                else if (mutation == "slot")
                {
                    Call bind = Assert.Single(body.Body.Descendants.OfType<Call>(), call => call.Callee.Name == "GetAwaiter");
                    LoadStackSlot read = Assert.IsType<LoadStackSlot>(bind.Arguments[0]);
                    var replacement = new LoadStackSlot(read.Slot + 100, read.Type);
                    replacement.InheritSourceOffset(read);
                    read.ReplaceWith(replacement);
                }
                else if (mutation == "label")
                {
                    Comparison label = Assert.Single(body.Body.Descendants.OfType<Comparison>());
                    Constant value = Assert.IsType<Constant>(label.Right);
                    var replacement = new Constant((int)value.Value! + 1, value.Type);
                    replacement.InheritSourceOffset(value);
                    value.ReplaceWith(replacement);
                }
                else if (mutation == "use")
                {
                    Call result = Assert.Single(body.Body.Descendants.OfType<Call>(), call => call.Callee.Name == "GetResult");
                    StoreLocal store = Assert.IsType<StoreLocal>(result.Parent);
                    Block block = Assert.IsType<Block>(store.Parent);
                    block.Add(new StoreStackSlot(999, new LoadLocal(store.Index, store.Type)));
                }
                else
                {
                    Call effect = body.Body.Descendants.OfType<Call>().First(call => call.Callee.Name == "Tick");
                    var replacement = new Call(effect.Callee with { Name = "OtherTick" }, false, []);
                    replacement.InheritSourceOffset(effect);
                    effect.ReplaceWith(replacement);
                }
            });
        AssertPlanningCannotRepair(changed.Request, baseline.Request);
    }

    [Theory]
    [InlineData("RangeResult")]
    [InlineData("VirtualDelegateOperand")]
    [InlineData("AnonymousOperand")]
    [InlineData("ConstantArrayOperand")]
    [InlineData("ReadOnlySpanLiteral")]
    [InlineData("NestedInterpolation")]
    [InlineData("GenericAny")]
    [InlineData("SwitchResult")]
    public void ClassicInverseInventoryPlansAreDetached(string method)
    {
        using RequestScope scope = OpenInventoryRequest(method);
        ClassicInversePlan plan = Reconstruct(scope.Request);
        string signature = plan.Signature;
        plan.MaterializeBody().DetachChildren();
        scope.Request.ExecutionBody.Body.DetachChildren();
        scope.Request.KickoffBody.Body.DetachChildren();
        BlockContainer body = plan.MaterializeBody();
        body.CheckInvariant();
        Assert.Single(body.Descendants.OfType<AwaitExpression>());
        Assert.Equal(signature, plan.Signature);
        var evidence = new List<ConsumedMemberEvidence>();
        foreach (IrNode node in body.Descendants)
            ConsumedMemberEvidence.AddFrom(node, evidence);
        Assert.All(evidence.Where(item => item.Method is not null),
            item => Assert.Null(item.Method!.ExactDefinitionAcquisitionGuard));
    }

    [Theory]
    [Trait("Speed", "Slow")]
    [InlineData("ConstantArrayOperand")]
    [InlineData("ReadOnlySpanLiteral")]
    [InlineData("VirtualDelegateOperand")]
    [InlineData("SliceResult")]
    [InlineData("PriorInterpolationArgument")]
    [InlineData("NestedInterpolation")]
    [InlineData("GenericAny")]
    [InlineData("GenericClass")]
    [InlineData("ConditionalAwaitable")]
    [InlineData("CoalesceAwaitable")]
    [InlineData("RangeResult")]
    [InlineData("SwitchResult")]
    public void ClassicInverseInventoryBudgetCutsNeverDecline(string method)
    {
        using RequestScope scope = OpenInventoryRequest(method);
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
