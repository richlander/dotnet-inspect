using System.Collections.Immutable;
using DotnetInspector.Fixtures;
using ILInspector.Decompiler.Pipeline;
using ILInspector.DecompilerHarness;

namespace ILInspector.Decompiler.Tests;

public sealed partial class ClassicInverseCoreTests
{
    const string GenericExpressionFixtureType =
        "ILInspector.Decompiler.Fixtures.ClassicAsync.GenericExpressionFixtures`1";

    [Theory]
    [InlineData("CharPropertyInitializer", "value")]
    [InlineData("CharPropertyInitializer", "sink")]
    [InlineData("CharFieldInitializer", "sink")]
    [InlineData("BoolPropertyInitializer", "value")]
    [InlineData("EnumPropertyInitializer", "value")]
    [InlineData("NestedCharInitializer", "member")]
    public void ClassicInverseRetainedInitializerSinksCannotBeHealed(string method, string mutation)
    {
        using RequestScope baseline = OpenExpressionRequest(method);
        Reconstruct(baseline.Request);
        using RequestScope changed = OpenRequest(OpenClassicFixture(), method, ownsSource: true,
            fixtureType: ExpressionFixtureType, mutateExecution: body =>
            {
                if (method == "CharFieldInitializer")
                {
                    StoreField store = Assert.Single(body.Body.Descendants.OfType<StoreField>(),
                        node => node.Field.Name == "CharacterField");
                    var replacement = new StoreField(store.Field with { Type = TypeRef.CoreLib("System", "UInt16") },
                        (IrExpression)store.Instance!.Clone(), (IrExpression)store.Value.Clone());
                    replacement.InheritSourceOffset(store);
                    store.ReplaceWith(replacement);
                    return;
                }
                Call setter = Assert.Single(body.Body.Descendants.OfType<Call>(),
                    call => call.Callee.Name.StartsWith("set_", StringComparison.Ordinal));
                if (mutation == "value")
                {
                    Constant value = Assert.IsType<Constant>(setter.Arguments[1]);
                    var replacement = new Constant((int)value.Value! + 2, value.Type);
                    replacement.InheritSourceOffset(value);
                    value.ReplaceWith(replacement);
                }
                else
                {
                    MethodRef member = mutation == "sink"
                        ? setter.Callee with { ParameterTypes = [TypeRef.CoreLib("System", "UInt16")] }
                        : setter.Callee with { Name = "set_Other" };
                    var replacement = new Call(member, setter.IsVirtual,
                        setter.Arguments.Select(argument => (IrExpression)argument.Clone()).ToArray());
                    replacement.InheritSourceOffset(setter);
                    setter.ReplaceWith(replacement);
                }
            });
        AssertPlanningCannotRepair(changed.Request, baseline.Request);
    }

    [Theory]
    [InlineData("GenericAwait")]
    [InlineData("GenericArrayAwait")]
    [InlineData("GenericConstruct")]
    [InlineData("GenericCall")]
    public void ClassicInverseGenericOutputKeepsDeclaredContext(string method)
    {
        using RequestScope scope = OpenExpressionRequest(method);
        ClassicInversePlan plan = Reconstruct(scope.Request);
        Assert.Equal(TypeRef.MethodGenericParameter(0), Assert.Single(plan.TypeArguments));
        BlockContainer body = plan.MaterializeBody();
        AwaitExpression awaited = Assert.Single(body.Descendants.OfType<AwaitExpression>());
        TypeRef declared = scope.Request.KickoffBody.Signature.Parameters[0].Type.TypeArguments[0];
        Assert.Equal(declared, awaited.ResultType);
        Assert.Equal(scope.Request.KickoffBody.Signature.Parameters[0].Type,
            Assert.IsType<LoadArgument>(awaited.Operand).Type);
        LoadField rawInput = Assert.Single(scope.Request.ExecutionBody.Body.Descendants.OfType<LoadField>(),
            load => load.Field.Name == "a");
        TypeRef rawValue = rawInput.Field.Type.TypeArguments[0];
        Assert.Equal(TypeRefKind.GenericParameter,
            rawValue.Kind == TypeRefKind.SzArray ? rawValue.ElementType!.Kind : rawValue.Kind);
        foreach (IrNode node in body.Descendants.Where(node => node is Call or NewObject))
        {
            string effect = Assert.IsType<string>(ClassicInverseNodeFacts.EffectSignature(
                node, TypeRef.CoreLib("System", "Object")));
            Assert.Contains(effect, plan.SemanticRealizations.SelectMany(receipt => receipt.OutputEffects));
        }
        if (method == "GenericCall")
        {
            Call call = Assert.Single(body.Descendants.OfType<Call>());
            Assert.Equal(TypeRef.MethodGenericParameter(0), Assert.Single(call.Callee.TypeArguments));
            Assert.Equal(TypeRef.MethodGenericParameter(0), Assert.Single(call.Callee.DefinitionParameterTypes));
        }
        body.CheckInvariant();
    }

    [Theory]
    [InlineData("GenericContextAwait")]
    [InlineData("GenericContextTuple")]
    public void ClassicInverseGenericContainerBindsDistinctParameterKinds(string method)
    {
        using RequestScope scope = OpenRequest(OpenClassicFixture(), method, ownsSource: true,
            fixtureType: GenericExpressionFixtureType);
        ClassicInverseDecision decision = ClassicInverseCore.Decide(scope.Request);
        Assert.True(decision is ClassicInverseDecision.Reconstruct, decision.ToString());
        ClassicInversePlan plan = ((ClassicInverseDecision.Reconstruct)decision).Plan;
        Assert.Equal(new[] { TypeRef.GenericParameter(0), TypeRef.MethodGenericParameter(0) }, plan.TypeArguments);
        BlockContainer body = plan.MaterializeBody();
        Assert.Equal(TypeRef.MethodGenericParameter(0),
            Assert.Single(body.Descendants.OfType<AwaitExpression>()).ResultType);
        if (method == "GenericContextTuple")
        {
            TupleExpression tuple = Assert.Single(body.Descendants.OfType<TupleExpression>());
            Assert.Equal(TypeRef.GenericParameter(0), Assert.IsType<LoadArgument>(tuple.Elements[1]).Type);
            Assert.Equal(plan.TypeArguments.Reverse(), tuple.TupleType.TypeArguments);
        }
        body.CheckInvariant();
    }

    [Theory]
    [Trait("Speed", "Slow")]
    [InlineData("GenericContextAwait")]
    [InlineData("GenericContextTuple")]
    public void ClassicInverseGenericContainerOutputsCompileBack(string method)
    {
        var result = Assert.Single(FidelityCheck.Evaluate(FixtureCatalog.DecompilerClassicAsync.AssemblyPath(),
            type => type == GenericExpressionFixtureType, candidate => candidate.Method == method));
        Assert.True(result.Status == FidelityCheck.CompileBackStatus.Exact,
            $"{result.Method}: {result.Status}: {result.Detail}");
    }

    [Theory]
    [InlineData("arity")]
    [InlineData("kind")]
    [InlineData("index")]
    public void ClassicInverseGenericBindingRejectsIncompleteContext(string mutation)
    {
        using RequestScope scope = OpenExpressionRequest("GenericAwait");
        Reconstruct(scope.Request);
        IrFunction kickoff = scope.Request.KickoffBody;
        TypeRef machine = kickoff.Locals[scope.Request.StateMachineLocal];
        TypeRef parameter = mutation == "kind" ? TypeRef.GenericParameter(0)
            : TypeRef.MethodGenericParameter(mutation == "index" ? 1 : 0);
        TypeRef changed = TypeRef.GenericInstance(machine.ElementType!,
            mutation == "arity" ? [parameter, TypeRef.MethodGenericParameter(1)] : [parameter]);
        kickoff.ResetLocals(kickoff.Locals.SetItem(scope.Request.StateMachineLocal, changed),
            kickoff.LocalNames, synthesizedNames: kickoff.SynthesizedLocalNames);
        AssertInvalidCorrelation(scope.Request);
    }

    [Fact]
    public void ClassicInverseGenericFieldContextCannotBeHealed()
    {
        using RequestScope baseline = OpenExpressionRequest("GenericAwait");
        Reconstruct(baseline.Request);
        using RequestScope changed = OpenRequest(OpenClassicFixture(), "GenericAwait", ownsSource: true,
            fixtureType: ExpressionFixtureType, mutateExecution: body =>
            {
                LoadField load = Assert.Single(body.Body.Descendants.OfType<LoadField>(),
                    node => node.Field.Name == "a");
                TypeRef wrong = load.Field.Type.Instantiate([TypeRef.MethodGenericParameter(0)], []);
                var replacement = new LoadField(load.Field with { Type = wrong }, (IrExpression)load.Instance!.Clone());
                replacement.InheritSourceOffset(load);
                load.ReplaceWith(replacement);
            });
        AssertPlanningCannotRepair(changed.Request, baseline.Request);
    }

    [Fact]
    public void ClassicInverseGenericBindingRequiresItsKickoffInstantiation()
    {
        using RequestScope scope = OpenRequest(OpenClassicFixture(), "GenericContextAwait", ownsSource: true,
            fixtureType: GenericExpressionFixtureType);
        Reconstruct(scope.Request);
        IrFunction kickoff = scope.Request.KickoffBody;
        TypeRef machine = kickoff.Locals[scope.Request.StateMachineLocal];
        TypeRef changed = TypeRef.GenericInstance(machine.ElementType!, [.. machine.TypeArguments.Reverse()]);
        kickoff.ResetLocals(kickoff.Locals.SetItem(scope.Request.StateMachineLocal, changed),
            kickoff.LocalNames, synthesizedNames: kickoff.SynthesizedLocalNames);
        Assert.IsType<ClassicInverseDecision.Decline>(ClassicInverseCore.Decide(scope.Request));
    }

    [Fact]
    public void ClassicInverseGenericBindingKeepsExactMachineModule()
    {
        using RequestScope scope = OpenExpressionRequest("GenericAwait");
        Reconstruct(scope.Request);
        StoreField builder = Assert.Single(scope.Request.KickoffBody.Body.Descendants.OfType<StoreField>(),
            node => node.Field.Name == "<>t__builder");
        TypeRef original = builder.Field.DeclaringType;
        TypeRef definition = original.ElementType!;
        TypeRef foreign = TypeRef.DefinitionWithResolution(definition.Assembly, definition.Namespace,
            definition.Name, definition.ValueTypeHint, definition.InlineArray, definition.EnclosingType,
            definition.DefinitionName ?? throw new InvalidOperationException(), definition.ResolutionAssembly,
            definition.IntroducedTypeParameterCounts, definition.DefinitionHandle, Guid.NewGuid());
        TypeRef changed = TypeRef.GenericInstance(foreign, original.TypeArguments);
        Assert.Equal(original, changed);
        var replacement = new StoreField(builder.Field with { DeclaringType = changed },
            (IrExpression)builder.Instance!.Clone(), (IrExpression)builder.Value.Clone());
        replacement.InheritSourceOffset(builder);
        builder.ReplaceWith(replacement);
        Assert.IsType<ClassicInverseDecision.Decline>(ClassicInverseCore.Decide(scope.Request));
    }

    [Fact]
    public void ClassicInverseGenericParameterKindsParticipateInPlanEquality()
    {
        TypeRef type = TypeRef.GenericParameter(0, "T");
        TypeRef method = TypeRef.MethodGenericParameter(0, "T");
        Assert.NotEqual(new ClassicInverseLoadArgumentNode(0, "a", type, false, MetadataFactState.Unknown).Signature,
            new ClassicInverseLoadArgumentNode(0, "a", method, false, MetadataFactState.Unknown).Signature);
        using RequestScope scope = OpenExpressionRequest("GenericAwait");
        ClassicInversePlan plan = Reconstruct(scope.Request);
        var changed = new ClassicInversePlan(plan.Recipe, plan.Body, plan.Locals, plan.LocalNames,
            plan.SynthesizedLocalNames, plan.TypeFacts, plan.SourceOffset, plan.PhysicalPartition,
            plan.SemanticRealizations, plan.StructuredAncestorReceipts, [type]);
        Assert.NotEqual(plan, changed);
        using RequestScope container = OpenRequest(OpenClassicFixture(), "GenericContextAwait", ownsSource: true,
            fixtureType: GenericExpressionFixtureType);
        ClassicInversePlan contextual = Reconstruct(container.Request);
        var swapped = new ClassicInversePlan(contextual.Recipe, contextual.Body, contextual.Locals,
            contextual.LocalNames, contextual.SynthesizedLocalNames, contextual.TypeFacts, contextual.SourceOffset,
            contextual.PhysicalPartition, contextual.SemanticRealizations, contextual.StructuredAncestorReceipts,
            [.. contextual.TypeArguments.Reverse()]);
        Assert.NotEqual(contextual, swapped);
    }

    [Theory]
    [InlineData("NullableIntReceiver", "slot")]
    [InlineData("NullableGuidReceiver", "type")]
    [InlineData("NullableBoolReceiver", "extra-use")]
    [InlineData("AwaitTuple", "extra-use")]
    [InlineData("AwaitNestedTuple", "type")]
    [InlineData("AwaitTupleEffect", "value-order")]
    public void ClassicInverseAwaitedValueTransferCannotBeHealed(string method, string mutation)
    {
        using RequestScope baseline = OpenExpressionRequest(method);
        Reconstruct(baseline.Request);
        using RequestScope changed = OpenRequest(OpenClassicFixture(), method, ownsSource: true,
            fixtureType: ExpressionFixtureType, mutateExecution: body =>
            {
                Call result = Assert.Single(body.Body.Descendants.OfType<Call>(),
                    call => call.Callee.Name == "GetResult");
                StoreLocal store = Assert.IsType<StoreLocal>(result.Parent);
                IrNode use = Assert.Single(body.Body.Descendants,
                    node => node is LoadLocal read && read.Index == store.Index
                        || node is LoadLocalAddress address && address.Index == store.Index);
                if (mutation == "extra-use")
                {
                    Block block = Assert.IsType<Block>(store.Parent);
                    IrNode[] statements = [.. block.DetachChildren()];
                    block.Add(statements[0]);
                    block.Add(new StoreStackSlot(999, (IrExpression)use.Clone()));
                    foreach (IrNode statement in statements.Skip(1))
                        block.Add(statement);
                }
                else if (mutation == "value-order")
                {
                    NewObject tuple = Assert.IsType<NewObject>(use.Parent);
                    IrNode[] arguments = [.. tuple.DetachChildren()];
                    var replacement = new NewObject(tuple.Constructor,
                        arguments.Reverse().Cast<IrExpression>().ToArray());
                    replacement.InheritSourceOffset(tuple);
                    tuple.ReplaceWith(replacement);
                }
                else
                {
                    int index = mutation == "slot" ? store.Index + 1 : store.Index;
                    TypeRef type = mutation == "type" ? TypeRef.CoreLib("System", "Int64") : store.Type;
                    IrExpression replacement = use is LoadLocalAddress
                        ? new LoadLocalAddress(index, type) : new LoadLocal(index, type);
                    replacement.InheritSourceOffset(use);
                    use.ReplaceWith(replacement);
                }
            });
        AssertPlanningCannotRepair(changed.Request, baseline.Request);
    }

    [Theory]
    [InlineData("type")]
    [InlineData("address")]
    [InlineData("extra-read")]
    [InlineData("extra-write")]
    [InlineData("escape")]
    public void ClassicInverseDefaultInitializationCannotBeHealed(string mutation)
    {
        using RequestScope baseline = OpenExpressionRequest("DefaultGuidInitializer");
        Reconstruct(baseline.Request);
        using RequestScope changed = OpenRequest(OpenClassicFixture(), "DefaultGuidInitializer", ownsSource: true,
            fixtureType: ExpressionFixtureType, mutateExecution: body =>
            {
                InitObject init = Assert.Single(body.Body.Descendants.OfType<InitObject>(),
                    node => node.Address is LoadLocalAddress);
                LoadLocalAddress address = Assert.IsType<LoadLocalAddress>(init.Address);
                if (mutation is "type" or "address")
                {
                    var changedAddress = new LoadLocalAddress(
                        mutation == "address" ? address.Index + 1 : address.Index, address.Type);
                    changedAddress.InheritSourceOffset(address);
                    var replacement = new InitObject(
                        mutation == "type" ? TypeRef.CoreLib("System", "DateTime") : init.Type,
                        changedAddress);
                    replacement.InheritSourceOffset(init);
                    init.ReplaceWith(replacement);
                    return;
                }
                LoadLocal read = Assert.Single(body.Body.Descendants.OfType<LoadLocal>(),
                    node => node.Index == address.Index);
                Block block = Assert.IsType<Block>(init.Parent);
                IrNode[] statements = [.. block.DetachChildren()];
                foreach (IrNode statement in statements)
                {
                    block.Add(statement);
                    if (!ReferenceEquals(statement, init))
                        continue;
                    block.Add(mutation == "extra-write" ? init.Clone()
                        : new StoreStackSlot(999,
                            (IrExpression)(mutation == "escape" ? (IrNode)address : read).Clone()));
                }
            });
        AssertPlanningCannotRepair(changed.Request, baseline.Request);
    }

    [Theory]
    [InlineData("DefaultGuidInitializer")]
    [InlineData("AwaitNestedTuple")]
    public void ClassicInverseRetainedValuesRequireTheirOrigins(string method)
    {
        using RequestScope scope = OpenExpressionRequest(method);
        Reconstruct(scope.Request);
        ClassicInverseRequest request = CopyRequest(scope.Request, runPasses: (body, passes) =>
        {
            scope.Request.RunPasses!(body, passes);
            if (body.Name == "MoveNext")
                body.Body.Descendants.First(node => node is DefaultValue or TupleExpression).SetSourceOffset(-1);
        });
        Assert.IsType<ClassicInverseDecision.Decline>(ClassicInverseCore.Decide(request));
    }

    [Theory]
    [InlineData("DefaultGuidInitializer")]
    [InlineData("AwaitTuple")]
    public void ClassicInverseRetainedOutputKeepsExactValueType(string method)
    {
        using RequestScope scope = OpenExpressionRequest(method);
        Reconstruct(scope.Request);
        var (planning, candidate, shell) = Candidate(scope.Request);
        IrNode value = candidate.Statements.SelectMany(node => node.Descendants)
            .Single(node => node is DefaultValue or TupleExpression);
        if (value is DefaultValue)
            value.ReplaceWith(new DefaultValue(TypeRef.CoreLib("System", "DateTime")));
        else
        {
            var tuple = (TupleExpression)value;
            var type = TypeRef.GenericInstance(tuple.TupleType.ElementType!,
                [TypeRef.CoreLib("System", "Int64"), TypeRef.CoreLib("System", "Int32")]);
            IrExpression[] elements = [.. tuple.DetachChildren().Cast<IrExpression>()];
            tuple.ReplaceWith(new TupleExpression(type, elements));
        }
        Assert.IsType<ClassicInverseDecision.Decline>(ClassicInverseAccountant.Account(
            scope.Request, planning, candidate, shell, new ClassicInverseBudget()));
    }

    [Fact]
    public void ClassicInverseDefaultInitializationCarriesSemanticOrigins()
    {
        using RequestScope scope = OpenExpressionRequest("DefaultGuidInitializer");
        ClassicInversePlan plan = Reconstruct(scope.Request);
        InitObject init = Assert.Single(scope.Request.ExecutionBody.Body.Descendants.OfType<InitObject>(),
            node => node.Address is LoadLocalAddress);
        LoadLocalAddress address = Assert.IsType<LoadLocalAddress>(init.Address);
        LoadLocal read = Assert.Single(scope.Request.ExecutionBody.Body.Descendants.OfType<LoadLocal>(),
            node => node.Index == address.Index);
        var realization = Assert.Single(plan.SemanticRealizations,
            receipt => receipt.SourceEffects.Any(effect => effect.StartsWith("default:", StringComparison.Ordinal)));
        Assert.Equal(realization.SourceEffects, realization.OutputEffects);
        Assert.Contains(init.SourceOffset, realization.ImportOffsets);
        Assert.Contains(address.SourceOffset, realization.ImportOffsets);
        Assert.Contains(read.SourceOffset, realization.ImportOffsets);
        Assert.Contains(plan.PhysicalPartition, region => region.NodeForm == init.Describe()
            && region.Disposition == ClassicInverseRegionDisposition.Semantic);
        Assert.Equal(scope.Request.ExecutionBody.Body.Descendants.Count() + 1,
            CountCoveredNodes(scope.Request.ExecutionBody.Body,
                plan.PhysicalPartition.Where(region => region.Body == ClassicInverseBodyId.Execution)));
    }

    [Fact]
    public void ClassicInverseDefaultFieldIsNotAPrivateLocalTransfer()
    {
        using RequestScope scope = OpenExpressionRequest("DefaultGuidFieldInitializer");
        Assert.Contains(scope.Request.ExecutionBody.Body.Descendants,
            node => node is InitObject { Address: LoadFieldAddress { Field.Name: "IdField" } });
        ClassicInversePlanningView planning = ClassicInversePlanningView.Derive(scope.Request);
        Assert.DoesNotContain(planning.ExecutionBody.Body.Descendants, node => node is DefaultValue);
        Assert.IsType<ClassicInverseDecision.Decline>(ClassicInverseCore.Decide(scope.Request));
        Assert.False(DecompileExpressionFixture("DefaultGuidFieldInitializer").RequiresAsyncBodyModifier);
    }

    [Theory]
    [InlineData("target")]
    [InlineData("predicate")]
    [InlineData("extra-effect")]
    [InlineData("join")]
    public void ClassicInverseComposedPredicateRawControlCannotBeHealed(string mutation)
    {
        using RequestScope baseline = OpenExpressionRequest("ShortCircuitBoth");
        Reconstruct(baseline.Request);
        using RequestScope changed = OpenRequest(OpenClassicFixture(), "ShortCircuitBoth", ownsSource: true,
            fixtureType: ExpressionFixtureType, mutateExecution: body =>
            {
                Call predicate = Assert.Single(body.Body.Descendants.OfType<Call>(),
                    node => node.Callee.Name == "Predicate");
                ConditionalBranch test = Assert.IsType<ConditionalBranch>(predicate.Parent);
                Block block = Assert.IsType<Block>(test.Parent);
                if (mutation == "target")
                {
                    BlockContainer container = Assert.IsType<BlockContainer>(block.Parent);
                    var replacement = new ConditionalBranch((IrExpression)predicate.Clone(),
                        container.Blocks[block.ChildIndex + 1].StartOffset);
                    replacement.InheritSourceOffset(test);
                    test.ReplaceWith(replacement);
                }
                else if (mutation == "predicate")
                {
                    var replacement = new Call(predicate.Callee with { Name = "OtherPredicate" }, false, []);
                    replacement.InheritSourceOffset(predicate);
                    predicate.ReplaceWith(replacement);
                }
                else if (mutation == "join")
                {
                    LoadStackSlot read = Assert.Single(body.Body.Descendants.OfType<LoadStackSlot>(),
                        node => node.Parent is StoreLocal { Type.Name: "Boolean" });
                    var replacement = new LoadStackSlot(read.Slot + 1, read.Type);
                    replacement.InheritSourceOffset(read);
                    read.ReplaceWith(replacement);
                }
                else
                {
                    test.Detach();
                    block.Add(new ExpressionStatement((IrExpression)predicate.Clone()));
                    block.Add(test);
                }
            });
        AssertPlanningCannotRepair(changed.Request, baseline.Request);
    }

    [Theory]
    [InlineData("operator")]
    [InlineData("arm")]
    [InlineData("origin")]
    public void ClassicInverseComposedPredicatePlanningKeepsExactControl(string mutation)
    {
        using RequestScope scope = OpenExpressionRequest("ShortCircuitBoth");
        Reconstruct(scope.Request);
        ClassicInverseRequest request = CopyRequest(scope.Request, runPasses: (body, passes) =>
        {
            scope.Request.RunPasses!(body, passes);
            if (body.Name != "MoveNext")
                return;
            Conditional conditional = Assert.Single(body.Body.Descendants.OfType<Conditional>());
            if (mutation == "origin")
                conditional.SetSourceOffset(-1);
            else if (mutation == "arm")
            {
                var replacement = new Constant(false, TypeRef.CoreLib("System", "Boolean"));
                replacement.InheritSourceOffset(conditional.WhenTrue);
                conditional.WhenTrue.ReplaceWith(replacement);
            }
            else
            {
                LogicalBinary logical = Assert.Single(conditional.Descendants.OfType<LogicalBinary>());
                var replacement = new LogicalBinary(LogicalKind.And,
                    (IrExpression)logical.Left.Clone(), (IrExpression)logical.Right.Clone());
                logical.ReplaceWith(replacement);
            }
        });
        Assert.IsType<ClassicInverseDecision.Decline>(ClassicInverseCore.Decide(request));
    }

    [Theory]
    [InlineData("CharPropertyInitializer")]
    [InlineData("GenericCall")]
    [InlineData("NamedNullableReceiver")]
    [InlineData("AwaitNestedTuple")]
    [InlineData("DefaultGuidInitializer")]
    [InlineData("ShortCircuitBoth")]
    public void ClassicInverseRetainedExpressionPlansAreDetached(string method)
    {
        using RequestScope scope = OpenExpressionRequest(method);
        ClassicInversePlan plan = Reconstruct(scope.Request);
        string signature = plan.Signature;
        plan.MaterializeBody().DetachChildren();
        scope.Request.KickoffBody.Body.DetachChildren();
        scope.Request.ExecutionBody.Body.DetachChildren();
        BlockContainer body = plan.MaterializeBody();
        body.CheckInvariant();
        Assert.Single(body.Descendants.OfType<AwaitExpression>());
        Assert.Equal(signature, plan.Signature);
        Assert.All(body.Descendants.OfType<Call>(),
            call => Assert.Null(call.Callee.ExactDefinitionAcquisitionGuard));
    }

    [Theory]
    [InlineData("CharPropertyInitializer")]
    [InlineData("GenericCall")]
    [InlineData("NullableIntReceiver")]
    [InlineData("AwaitNestedTuple")]
    [InlineData("DefaultGuidInitializer")]
    [InlineData("ShortCircuitBoth")]
    public void ClassicInverseRetainedExpressionBudgetsRemainLoadBearing(string method)
    {
        using RequestScope scope = OpenExpressionRequest(method);
        var complete = new ClassicInverseBudget();
        Assert.IsType<ClassicInverseDecision.Reconstruct>(ClassicInverseCore.Decide(scope.Request, complete));
        foreach (int limit in new[] { 1, complete.Consumed / 2, complete.Consumed - 1 })
        {
            var failed = Assert.IsType<ClassicInverseDecision.Failed>(
                ClassicInverseCore.Decide(scope.Request, new ClassicInverseBudget(limit)));
            Assert.Equal(ClassicInverseFailureKind.BudgetExhausted, failed.Failure.Kind);
        }
        Assert.IsType<ClassicInverseDecision.Reconstruct>(
            ClassicInverseCore.Decide(scope.Request, new ClassicInverseBudget(complete.Consumed)));
    }

    [Theory]
    [Trait("Speed", "Slow")]
    [InlineData("CharPropertyInitializer")]
    [InlineData("GenericCall")]
    [InlineData("NullableIntReceiver")]
    [InlineData("AwaitNestedTuple")]
    [InlineData("DefaultGuidInitializer")]
    [InlineData("ShortCircuitBoth")]
    public void ClassicInverseRetainedExpressionBudgetCutsNeverDecline(string method)
    {
        using RequestScope scope = OpenExpressionRequest(method);
        var complete = new ClassicInverseBudget();
        Assert.IsType<ClassicInverseDecision.Reconstruct>(ClassicInverseCore.Decide(scope.Request, complete));
        for (int limit = 1; limit <= complete.Consumed; limit++)
        {
            var budget = new ClassicInverseBudget(limit);
            ClassicInverseDecision decision = ClassicInverseCore.Decide(scope.Request, budget);
            Assert.True(budget.Exhausted
                ? decision is ClassicInverseDecision.Failed { Failure.Kind: ClassicInverseFailureKind.BudgetExhausted }
                : decision is ClassicInverseDecision.Reconstruct,
                $"{method} budget {limit}/{complete.Consumed}: {decision}");
        }
    }
}
