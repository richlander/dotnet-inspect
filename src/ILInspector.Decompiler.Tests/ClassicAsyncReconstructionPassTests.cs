using System.Reflection.Metadata.Ecma335;

using ILInspector.Decompiler.Pipeline;
using ILInspector.Metadata;
using ILInspector.MetadataPrimitives;

namespace ILInspector.Decompiler.Tests;

[Trait("Area", "Pass")]
public class ClassicAsyncReconstructionPassTests
{
    static readonly TypeRef Void = TypeRef.CoreLib("System", "Void");
    static readonly TypeRef Task = TypeRef.CoreLib(
        "System.Threading.Tasks",
        "Task");
    static readonly TypeRef StateMachine = TypeRef.Definition(
        "Synthetic",
        "Samples",
        "Outer+<Fake>d__0");
    static readonly TypeRef Builder = TypeRef.Definition(
        "Synthetic",
        "Samples",
        "BuilderLike");
    static readonly MetadataMethodAddress KickoffAddress = new(
        Guid.Parse("11111111-1111-1111-1111-111111111111"),
        MetadataTokens.MethodDefinitionHandle(1));
    static readonly MetadataMethodAddress ExecutionAddress = new(
        Guid.Parse("11111111-1111-1111-1111-111111111111"),
        MetadataTokens.MethodDefinitionHandle(2));

    [Fact]
    public void UnstampedSupportLookalike_IsNotEdited()
    {
        IrFunction function = BuildSupportLookalike();
        string before = IrPrinter.Dump(function);

        new ClassicAsyncReconstructionPass().Run(
            function,
            PassContext.None);

        Assert.Equal(before, IrPrinter.Dump(function));
    }

    [Fact]
    public void UnstampedKickoffLookalike_DoesNotReachImport()
    {
        IrFunction function = BuildKickoffLookalike();
        bool attempted = false;
        var context = PassContext.ForImport(_ =>
        {
            attempted = true;
            return null;
        });

        new ClassicAsyncReconstructionPass().Run(function, context);

        Assert.False(attempted);
    }

    [Fact]
    public void ResolvedClassicKickoff_ImportsOwnerIssuedMoveNext()
    {
        using var source = OpenClassicFixture();
        IrFunction function = ImportClassicFixture(source, "AwaitVoid");
        var context = PassContext.ForImport(
            method => IrImporter.Import(source, method));

        IrPasses.Run(function, IrPasses.Default, context);

        var evidence = Assert.IsType<ClassicAsyncRelationshipEvidence>(
            function.ClassicAsyncRelationship);
        var resolved = Assert.IsType<StateMachineRelationshipResult.Resolved>(
            evidence.Relationship);
        Assert.True(resolved.Relationship.TryGetMethod(
            StateMachineMethodRole.MoveNext,
            out var moveNext));
        var decision = Assert.IsType<ClassicAsyncDecision.Reconstruct>(
            PublishedDecision(evidence));
        Assert.Equal(moveNext, decision.Plan.Machine.Execution);
        Assert.Same(
            evidence.AcquisitionGuard,
            decision.Plan.Machine.AcquisitionGuard);
        Assert.Equal(1, PlanningSession(evidence).PreparationCount);
        Assert.True(function.RequiresAsyncBodyModifier);
    }

    [Fact]
    public void GenericStateMachine_UsesOwnerDefinitionIdentity()
    {
        using var source = OpenClassicFixture();
        IrFunction function = ImportClassicFixture(source, "AwaitGeneric");

        IrPasses.Run(
            function,
            IrPasses.Default,
            PassContext.ForImport(
                method => IrImporter.Import(source, method)));

        Assert.IsType<ClassicAsyncOutcome.Reconstructed>(
            function.ClassicAsyncOutcome);
        Assert.Equal(
            ClassicAsyncDeclarationDisposition.IncludeAsync,
            function.ClassicAsyncDeclarationDisposition);
    }

    [Fact]
    public void GenericContainingTypeAndMethodMapFieldTypeParameters()
    {
        using var source = OpenClassicFixture();
        IrFunction function = Assert.IsType<IrFunction>(
            IrImporter.Import(
                source,
                "ILInspector.Decompiler.Fixtures.ClassicAsync.GenericAsyncFixtures`1",
                "AwaitGeneric"));

        IrPasses.Run(
            function,
            IrPasses.Default,
            PassContext.ForImport(
                method => IrImporter.Import(source, method)));

        Assert.IsType<ClassicAsyncOutcome.Reconstructed>(
            function.ClassicAsyncOutcome);
        ClassicAsyncParameterBinding binding = Assert.Single(
            Assert.IsType<ClassicAsyncDecision.Reconstruct>(
                PublishedDecision(
                    Assert.IsType<ClassicAsyncRelationshipEvidence>(
                        function.ClassicAsyncRelationship)))
                .Plan.Machine.ParameterBindings.Items);
        Assert.Equal("value", binding.FieldName);
        Assert.Equal(
            TypeRefKind.MethodGenericParameter,
            binding.FieldType.TypeArguments[0].Kind);
        Assert.Equal(0, binding.FieldType.TypeArguments[0]
            .GenericParameterIndex);
    }

    [Fact]
    public void AsyncVoid_DeclinesAsUnsupportedBuilder()
    {
        using var source = OpenClassicFixture();
        IrFunction function = ImportClassicFixture(
            source,
            "AwaitAsyncVoid");

        IrPasses.Run(
            function,
            IrPasses.Default,
            PassContext.ForImport(
                method => IrImporter.Import(source, method)));

        var outcome = Assert.IsType<ClassicAsyncOutcome.Declined>(
            function.ClassicAsyncOutcome);
        Assert.Equal(
            ClassicAsyncDeclineReason.UnsupportedBuilder,
            outcome.Reason);
        Assert.Equal(
            ClassicAsyncKickoffDisposition.ReplacedNarrowHandoff,
            outcome.KickoffDisposition);
        Assert.Equal(
            ClassicAsyncDeclarationDisposition.OmitAsync,
            function.ClassicAsyncDeclarationDisposition);
    }

    [Fact]
    public void ResolvedClassicExecutionMethod_IsNotEdited()
    {
        using var source = OpenClassicFixture();
        IrFunction function = IrImporter.ImportAssembly(source)
            .Select(method => method.Function)
            .First(method => method.ClassicAsyncRelationship is
            {
                HostRole: ClassicAsyncHostRole.Execution,
                Relationship: StateMachineRelationshipResult.Resolved
                {
                    Relationship.Kind: StateMachineClaimKind.ClassicAsync,
                },
            });
        string before = IrPrinter.Dump(function);

        new ClassicAsyncReconstructionPass().Run(
            function,
            PassContext.ForImport(method => IrImporter.Import(source, method)));

        Assert.Equal(before, IrPrinter.Dump(function));
    }

    [Fact]
    public void UnsupportedResolvedClassic_PreservesKickoffAndNamesDecline()
    {
        using var source = OpenClassicFixture();
        IrFunction function = ImportClassicFixture(
            source,
            "AwaitVoidThenReturn");
        var context = PassContext.ForImport(
            method => IrImporter.Import(source, method));
        RunBeforeClassicAsync(function, context);
        IReadOnlyList<string> originalStatements = function.Body.Blocks[0]
            .Children
            .Select(SubtreeSignature)
            .ToList();

        new ClassicAsyncReconstructionPass().Run(function, context);

        var outcome = Assert.IsType<ClassicAsyncOutcome.Declined>(
            function.ClassicAsyncOutcome);
        Assert.Equal(
            ClassicAsyncDeclineReason.UnrecognizedAwaiterProtocol,
            outcome.Reason);
        Assert.Equal(
            ClassicAsyncKickoffDisposition.PreservedOriginal,
            outcome.KickoffDisposition);
        Assert.Equal(
            ClassicAsyncDeclarationDisposition.OmitAsync,
            function.ClassicAsyncDeclarationDisposition);
        Assert.Single(function.Body.Blocks);
        Assert.Equal(
            originalStatements,
            function.Body.Blocks[0]
                .Children
                .Skip(1)
                .Select(SubtreeSignature));
    }

    [Fact]
    public void NonNarrowDecline_PreservesEveryOriginalStatement()
    {
        using var source = OpenClassicFixture();
        IrFunction function = ImportClassicFixture(
            source,
            "AwaitVoidThenReturn");
        var context = PassContext.ForImport(
            method => IrImporter.Import(source, method));
        RunBeforeClassicAsync(function, context);
        var unexplained = new ExpressionStatement(new Call(
            new MethodRef(
                TypeRef.Definition("Synthetic", "Samples", "Effects"),
                "Observe",
                Void,
                [],
                HasThis: false),
            isVirtual: false,
            []));
        function.Body.Blocks[0].Add(unexplained);
        IReadOnlyList<string> originalStatements = function.Body.Blocks[0]
            .Children
            .Select(SubtreeSignature)
            .ToList();

        ClassicAsyncReconstructionPass.ApplyDecision(
            function,
            context,
            new ClassicAsyncDecision.Decline(
                ClassicAsyncDeclineReason.UnrecognizedAwaiterProtocol,
                ClassicAsyncKickoffDisposition.PreservedOriginal));

        var outcome = Assert.IsType<ClassicAsyncOutcome.Declined>(
            function.ClassicAsyncOutcome);
        Assert.Equal(
            ClassicAsyncKickoffDisposition.PreservedOriginal,
            outcome.KickoffDisposition);
        Assert.Equal(
            originalStatements,
            function.Body.Blocks[0]
                .Children
                .Skip(1)
                .Select(SubtreeSignature));
    }

    [Fact]
    public void ExactMoveNextAddress_IsBoundToItsAcquisition()
    {
        using var source = OpenClassicFixture();
        MethodRef requested = CaptureMoveNextRequest(source);
        using var otherSource = OpenClassicFixture();

        Assert.Null(IrImporter.Import(otherSource, requested));
    }

    [Fact]
    public void ExactMoveNextAddress_RejectsSymbolicSignatureMismatch()
    {
        using var source = OpenClassicFixture();
        MethodRef requested = CaptureMoveNextRequest(source);

        Assert.Null(IrImporter.Import(
            source,
            requested with { Name = "SetStateMachine" }));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void KickoffLocalWithoutExactDefinitionIdentityDeclines(
        bool differentModule)
    {
        using var source = OpenClassicFixture();
        IrFunction function = ImportClassicFixture(source, "AwaitVoid");
        var context = PassContext.ForImport(
            method => IrImporter.Import(source, method));
        RunBeforeClassicAsync(function, context);
        StoreField builderStore = Assert.Single(
            function.Descendants.OfType<StoreField>(),
            static store => store.Field.Name == "<>t__builder");
        var machineAddress = Assert.IsType<LoadLocalAddress>(
            builderStore.Instance);
        TypeRef machine = function.Locals[machineAddress.Index];
        Assert.NotNull(machine.DefinitionName);
        Assert.NotNull(machine.DefinitionModuleVersionId);
        TypeRef foreign = TypeRef.DefinitionWithResolution(
            machine.Assembly,
            machine.Namespace,
            machine.Name,
            machine.ValueTypeHint,
            machine.InlineArray,
            machine.EnclosingType,
            machine.DefinitionName,
            machine.ResolutionAssembly,
            definitionHandle: differentModule
                ? machine.DefinitionHandle
                : MetadataTokens.TypeDefinitionHandle(
                    MetadataTokens.GetRowNumber(
                        machine.DefinitionHandle)
                    + 1),
            definitionModuleVersionId: differentModule
                ? Guid.NewGuid()
                : machine.DefinitionModuleVersionId);
        function.ResetLocals(
            function.Locals.SetItem(
                machineAddress.Index,
                foreign),
            function.LocalNames);
        var evidence = Assert.IsType<
            ClassicAsyncRelationshipEvidence>(
                function.ClassicAsyncRelationship);
        var resolved = Assert.IsType<
            StateMachineRelationshipResult.Resolved>(
                evidence.Relationship);

        Assert.False(
            ClassicAsyncReconstructionPass.TryGetKickoff(
                function,
                resolved.Relationship.StateMachineType,
                resolved.Relationship.StateMachineName,
                out _,
                out ClassicAsyncDeclineReason reason,
                out bool narrow));
        Assert.Equal(
            ClassicAsyncDeclineReason.KickoffMachineMismatch,
            reason);
        Assert.False(narrow);
    }

    [Fact]
    public void SwappedKickoffParameterCopiesDecline()
    {
        using var source = OpenClassicFixture();
        IrFunction function = ImportClassicFixture(
            source,
            "TwoSequentialAwaits");
        var context = PassContext.ForImport(
            method => IrImporter.Import(source, method));
        RunBeforeClassicAsync(function, context);
        StoreField first = Assert.Single(
            function.Descendants.OfType<StoreField>(),
            static store => store.Field.Name == "a");
        StoreField second = Assert.Single(
            function.Descendants.OfType<StoreField>(),
            static store => store.Field.Name == "b");
        var firstSource = Assert.IsType<LoadArgument>(first.Value);
        var secondSource = Assert.IsType<LoadArgument>(second.Value);
        firstSource.ReplaceWith(new LoadArgument(
            secondSource.Index,
            secondSource.Name,
            secondSource.Type)
        {
            IsDynamic = secondSource.IsDynamic,
            ArrayElementIsDynamic =
                secondSource.ArrayElementIsDynamic,
        });
        secondSource.ReplaceWith(new LoadArgument(
            firstSource.Index,
            firstSource.Name,
            firstSource.Type)
        {
            IsDynamic = firstSource.IsDynamic,
            ArrayElementIsDynamic =
                firstSource.ArrayElementIsDynamic,
        });
        var evidence = Assert.IsType<
            ClassicAsyncRelationshipEvidence>(
                function.ClassicAsyncRelationship);
        var resolved = Assert.IsType<
            StateMachineRelationshipResult.Resolved>(
                evidence.Relationship);

        Assert.True(
            ClassicAsyncReconstructionPass.TryGetKickoff(
                function,
                resolved.Relationship.StateMachineType,
                resolved.Relationship.StateMachineName,
                out _,
                out _,
                out bool narrow));
        Assert.False(narrow);
    }

    [Fact]
    public void CompetingAwaiterDefinitionsDecline()
    {
        using var source = OpenClassicFixture();
        MethodRef request = CaptureMoveNextRequest(
            source,
            "TwoSequentialAwaits");
        IrFunction moveNext = Assert.IsType<IrFunction>(
            IrImporter.Import(source, request));
        var context = PassContext.ForImport(
            method => IrImporter.Import(source, method));
        IrPasses.Run(
            moveNext,
            IrPasses.ForReconstruction<
                ClassicAsyncReconstructionPass>(),
            context);
        List<StoreLocal> awaiterStores =
        [
            .. moveNext.Descendants
                .OfType<StoreLocal>()
                .Where(static store => store.Value is Call
                {
                    Callee.Name: "GetAwaiter",
                }),
        ];
        Assert.True(awaiterStores.Count >= 2);
        StoreLocal first = awaiterStores[0];
        StoreLocal second = awaiterStores[1];
        var thenArm = new Block(0);
        thenArm.Add((StoreLocal)first.Clone());
        var elseArm = new Block(0);
        elseArm.Add(new StoreLocal(
            first.Index,
            first.Type,
            (IrExpression)second.Value.Clone()));
        first.ReplaceWith(new IfStatement(
            new Constant(
                true,
                TypeRef.CoreLib("System", "Boolean")),
            thenArm,
            elseArm));
        Call getResult = moveNext.Descendants
            .OfType<Call>()
            .First(static call =>
                call.Callee.Name == "GetResult");

        Assert.False(
            ClassicAsyncReconstructionPass.TryGetAwaitSource(
                moveNext,
                getResult,
                out _,
                out _));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RaisedAndLoweredShareOneDecisionWithoutAliasing(
        bool loweredFirst)
    {
        using var source = OpenClassicFixture();
        IrFunction raised = ImportClassicFixture(
            source,
            "AwaitValue");
        IrFunction lowered = ImportClassicFixture(
            source,
            "AwaitValue");
        var raisedEvidence =
            Assert.IsType<ClassicAsyncRelationshipEvidence>(
                raised.ClassicAsyncRelationship);
        var loweredEvidence =
            Assert.IsType<ClassicAsyncRelationshipEvidence>(
                lowered.ClassicAsyncRelationship);
        ClassicAsyncPlanningSession planningSession =
            PlanningSession(raisedEvidence);
        Assert.Same(
            planningSession,
            PlanningSession(loweredEvidence));

        Func<MethodRef, IrFunction?> import =
            method => IrImporter.Import(source, method);
        DecompilerResult raisedResult;
        DecompilerResult loweredResult;
        if (loweredFirst)
        {
            loweredResult = CSharpPrinter.PrintLowered(
                lowered,
                import);
            raisedResult = CSharpPrinter.PrintRaised(
                raised,
                import,
                typesProvablyDisjoint: source.AreProvablyDisjoint);
        }
        else
        {
            raisedResult = CSharpPrinter.PrintRaised(
                raised,
                import,
                typesProvablyDisjoint: source.AreProvablyDisjoint);
            loweredResult = CSharpPrinter.PrintLowered(
                lowered,
                import);
        }

        Assert.True(raisedResult.Succeeded);
        Assert.True(loweredResult.Succeeded);
        Assert.IsType<ClassicAsyncDecision.Reconstruct>(
            PublishedDecision(raisedEvidence));
        var raisedStage = Assert.IsType<
            ClassicAsyncStageResult.Applied>(
                raised.ClassicAsyncStageResult);
        var loweredStage = Assert.IsType<
            ClassicAsyncStageResult.Applied>(
                lowered.ClassicAsyncStageResult);
        Assert.Equal(ClassicAsyncStage.Raised, raisedStage.Stage);
        Assert.Equal(ClassicAsyncStage.Lowered, loweredStage.Stage);
        Assert.Equal(
            1,
            planningSession.PreparationCount);
        Assert.Equal(
            1,
            planningSession.PublishedPreparationCount);
        Assert.NotSame(raised.Body, lowered.Body);
        Assert.NotSame(
            raised.Body.Blocks[0].Children[0],
            lowered.Body.Blocks[0].Children[0]);
        string loweredBefore = IrPrinter.Dump(lowered);

        raised.Body.DetachChildren();

        Assert.Equal(loweredBefore, IrPrinter.Dump(lowered));
        IrFunction later = ImportClassicFixture(
            source,
            "AwaitValue");
        DecompilerResult laterResult = CSharpPrinter.PrintRaised(
            later,
            import,
            typesProvablyDisjoint: source.AreProvablyDisjoint);
        Assert.Equal(raisedResult.Output, laterResult.Output);
        Assert.NotSame(later.Body, lowered.Body);
    }

    [Fact]
    public async Task ConcurrentRequestsPublishOneDecisionWithoutDeadlock()
    {
        using var source = OpenClassicFixture();
        IrFunction[] functions = Enumerable.Range(0, 8)
            .Select(_ => ImportClassicFixture(
                source,
                "AwaitValue"))
            .ToArray();
        var evidence = Assert.IsType<ClassicAsyncRelationshipEvidence>(
            functions[0].ClassicAsyncRelationship);
        Func<MethodRef, IrFunction?> import =
            method => IrImporter.Import(source, method);

        System.Threading.Tasks.Task<DecompilerResult>[] requests = functions
            .Select((function, index) => System.Threading.Tasks.Task.Run(() =>
                index % 2 == 0
                    ? CSharpPrinter.PrintRaised(
                        function,
                        import,
                        typesProvablyDisjoint:
                            source.AreProvablyDisjoint)
                    : CSharpPrinter.PrintLowered(
                        function,
                        import)))
            .ToArray();
        DecompilerResult[] results = await System.Threading.Tasks.Task.WhenAll(requests)
            .WaitAsync(
                TimeSpan.FromSeconds(30),
                TestContext.Current.CancellationToken);

        Assert.All(results, result => Assert.True(result.Succeeded));
        Assert.IsType<ClassicAsyncDecision.Reconstruct>(
            PublishedDecision(evidence));
        Assert.Equal(
            1,
            PlanningSession(evidence).PublishedPreparationCount);
    }

    [Fact]
    public void NestedLocalPreparationDoesNotPoisonTopLevelRequest()
    {
        using var source = OpenClassicFixture();
        IrFunction parent = ImportClassicFixture(
            source,
            "CallsClassicLocal");
        Func<MethodRef, IrFunction?> import =
            method => IrImporter.Import(source, method);

        CSharpPrinter.PrintRaised(
            parent,
            import,
            typesProvablyDisjoint: source.AreProvablyDisjoint);

        IrFunction local = IrImporter.ImportAssembly(source)
            .Select(method => method.Function)
            .First(function => function.Name.Contains(
                "g__ClassicLocal",
                StringComparison.Ordinal));
        var evidence = Assert.IsType<ClassicAsyncRelationshipEvidence>(
            local.ClassicAsyncRelationship);
        ClassicAsyncPlanningSession planningSession =
            PlanningSession(evidence);
        int preparationsAfterNestedRequest =
            planningSession.PreparationCount;

        CSharpPrinter.PrintRaised(
            local,
            import,
            typesProvablyDisjoint: source.AreProvablyDisjoint);

        Assert.IsType<ClassicAsyncDecision.Reconstruct>(
            PublishedDecision(evidence));
        Assert.Equal(
            preparationsAfterNestedRequest,
            planningSession.PreparationCount);
        Assert.Equal(
            1,
            planningSession.PublishedPreparationCount);
    }

    [Fact]
    public void RejectedKickoffEvidenceIsNotOverwrittenByImplementationLookup()
    {
        using var source = OpenClassicFixture();
        IrFunction function = ImportClassicFixture(
            source,
            "RejectedClassicClaim");
        var evidence = Assert.IsType<ClassicAsyncRelationshipEvidence>(
            function.ClassicAsyncRelationship);

        Assert.IsType<StateMachineRelationshipResult.Rejected>(
            evidence.Relationship);

        IrPasses.Run(
            function,
            IrPasses.Default,
            PassContext.ForImport(
                method => IrImporter.Import(source, method)));

        Assert.IsType<ClassicAsyncStageResult.Failed>(
            function.ClassicAsyncStageResult);
        Assert.Null(function.ClassicAsyncOutcome);
        Assert.Equal(
            ClassicAsyncDeclarationDisposition.NoOpinion,
            function.ClassicAsyncDeclarationDisposition);
    }

    [Fact]
    public void KickoffPlanningPrefixIsDerivedFromRegisteredPipeline()
    {
        string[] expected = IrPasses.Default
            .TakeWhile(pass =>
                pass is not ClassicAsyncReconstructionPass)
            .Select(pass => pass.Name)
            .ToArray();

        Assert.Equal(
            expected,
            IrPasses.Before<ClassicAsyncReconstructionPass>()
                .Select(pass => pass.Name));
    }

    [Fact]
    public void IndependentlyPreparedPlansHaveValueSemantics()
    {
        using var source = OpenClassicFixture();
        IrFunction function = ImportClassicFixture(
            source,
            "AwaitValue");
        var evidence = Assert.IsType<ClassicAsyncRelationshipEvidence>(
            function.ClassicAsyncRelationship);

        ClassicAsyncPreparationResult first =
            ClassicAsyncReconstructionPass.Prepare(
                source,
                evidence);
        ClassicAsyncPreparationResult second =
            ClassicAsyncReconstructionPass.Prepare(
                source,
                evidence);

        Assert.NotSame(first, second);
        Assert.Equal(first, second);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
        var reconstruct =
            Assert.IsType<ClassicAsyncDecision.Reconstruct>(
                Assert.IsType<
                    ClassicAsyncPreparationResult.Decided>(
                        first).Decision);
        Assert.Equal(
            "<>1__state",
            reconstruct.Plan.Machine.StateStorage.Name);
        Assert.Equal(
            "<>t__builder",
            reconstruct.Plan.Machine.BuilderStorage.Name);
        Assert.Contains(
            reconstruct.Plan.Machine.AwaiterStorages.Items,
            storage => storage.Name.StartsWith(
                "<>u__",
                StringComparison.Ordinal));
        Assert.Collection(
            reconstruct.Plan.Machine.ParameterBindings.Items,
            first =>
            {
                Assert.Equal("a", first.FieldName);
                Assert.Equal("a", first.ArgumentName);
                Assert.Equal(0, first.ArgumentIndex);
            },
            second =>
            {
                Assert.Equal("b", second.FieldName);
                Assert.Equal("b", second.ArgumentName);
                Assert.Equal(1, second.ArgumentIndex);
            });
    }

    [Fact]
    public void CheckedRegionHasOnePrimaryRealization()
    {
        using var source = OpenClassicFixture();
        IrFunction function = ImportClassicFixture(
            source,
            "AwaitInLoopChecked");
        var evidence = Assert.IsType<ClassicAsyncRelationshipEvidence>(
            function.ClassicAsyncRelationship);

        var reconstruct =
            Assert.IsType<ClassicAsyncDecision.Reconstruct>(
                PublishedDecision(evidence));
        ClassicAsyncRegionLedger ledger =
            reconstruct.Plan.RegionLedger;
        ClassicAsyncUserRegion region = Assert.Single(
            ledger.UserRegions,
            static region =>
                region.Semantics.Kind
                    == ClassicAsyncUserRegionKind.CheckedArithmetic);
        ClassicAsyncUserRegionRealization realization = Assert.Single(
            ledger.Realizations,
            realization => realization.UserRegion == region.Id);

        Assert.Equal(
            ClassicAsyncUserRegionKind.CheckedArithmetic,
            region.Semantics.Kind);
        Assert.Equal(region.Id, realization.UserRegion);
        Assert.Equal(
            region.Semantics,
            realization.PrimaryOutputNode.Semantics);
        Assert.Contains(
            region.PhysicalRegion,
            ledger.ConsumedRegions);
        Assert.DoesNotContain(
            region.PhysicalRegion,
            ledger.PreservedRegions);
    }

    [Theory]
    [InlineData("AwaitValue", 1)]
    [InlineData("AwaitVoid", 1)]
    [InlineData("AwaitDelayConstant", 1)]
    [InlineData("TwoSequentialAwaits", 2)]
    [InlineData("AwaitOrdinarySetMethod", 1)]
    [InlineData("AwaitConditional", 1)]
    [InlineData("AwaitInLoop", 1)]
    [InlineData("AwaitInTryFinally", 1)]
    public void AwaitedOperandsHaveOnePrimaryRealization(
        string methodName,
        int expectedCount)
    {
        using var source = OpenClassicFixture();
        IrFunction function = ImportClassicFixture(source, methodName);
        var evidence = Assert.IsType<ClassicAsyncRelationshipEvidence>(
            function.ClassicAsyncRelationship);
        var reconstruct =
            Assert.IsType<ClassicAsyncDecision.Reconstruct>(
                PublishedDecision(evidence));
        ClassicAsyncRegionLedger ledger =
            reconstruct.Plan.RegionLedger;
        ClassicAsyncUserRegion[] operands =
        [
            .. ledger.UserRegions.Where(static region =>
                region.Semantics.Kind
                    == ClassicAsyncUserRegionKind.AwaitedOperand),
        ];

        Assert.Equal(expectedCount, operands.Length);
        Assert.All(
            operands,
            operand => Assert.Single(
                ledger.Realizations,
                realization =>
                    realization.UserRegion == operand.Id
                    && realization.PrimaryOutputNode.Semantics
                        == operand.Semantics));
    }

    [Fact]
    public void PredicateRegionHasOnePrimaryRealization()
    {
        using var source = OpenClassicFixture();
        IrFunction function = ImportClassicFixture(
            source,
            "AwaitConditional");
        var evidence = Assert.IsType<ClassicAsyncRelationshipEvidence>(
            function.ClassicAsyncRelationship);
        var reconstruct =
            Assert.IsType<ClassicAsyncDecision.Reconstruct>(
                PublishedDecision(evidence));
        ClassicAsyncRegionLedger ledger =
            reconstruct.Plan.RegionLedger;
        ClassicAsyncUserRegion predicate = Assert.Single(
            ledger.UserRegions,
            static region =>
                region.Semantics.Kind
                    == ClassicAsyncUserRegionKind.Predicate);
        ClassicAsyncUserRegionRealization realization = Assert.Single(
            ledger.Realizations,
            realization => realization.UserRegion == predicate.Id);

        Assert.Equal(
            predicate.Semantics,
            realization.PrimaryOutputNode.Semantics);
        Assert.Contains(
            "4:flag",
            predicate.Semantics.Discriminator,
            StringComparison.Ordinal);
        Assert.Contains(
            predicate.PhysicalRegion,
            ledger.ConsumedRegions);
        Assert.DoesNotContain(
            predicate.PhysicalRegion,
            ledger.PreservedRegions);
    }

    [Fact]
    public void RegionLedgerRejectsChangedPredicate()
    {
        var id = new ClassicAsyncRegionId(
            ClassicAsyncRegionHost.Execution,
            "0.0");
        var predicate = new ClassicAsyncUserRegion(
            id,
            PhysicalId(
                ClassicAsyncRegionHost.Execution,
                "0.0"),
            new(
                ClassicAsyncUserRegionKind.Predicate,
                "parameter|flag|System.Boolean",
                Occurrence: 0));
        var changed = new ClassicAsyncOutputNode(
            predicate.Semantics with
            {
                Discriminator = "parameter|other|System.Boolean",
            });

        Assert.False(TryCreateRegionLedger(
            [predicate],
            [new(id, changed)],
            out _));
    }

    [Fact]
    public void GuardedEffectRegionHasOnePrimaryRealization()
    {
        using var source = OpenClassicFixture();
        IrFunction function = ImportClassicFixture(
            source,
            "AwaitInTryFinally");
        var evidence = Assert.IsType<ClassicAsyncRelationshipEvidence>(
            function.ClassicAsyncRelationship);
        var reconstruct =
            Assert.IsType<ClassicAsyncDecision.Reconstruct>(
                PublishedDecision(evidence));
        ClassicAsyncRegionLedger ledger =
            reconstruct.Plan.RegionLedger;
        ClassicAsyncUserRegion effect = Assert.Single(
            ledger.UserRegions,
            static region =>
                region.Semantics.Kind
                    == ClassicAsyncUserRegionKind.GuardedEffect);
        ClassicAsyncUserRegionRealization realization = Assert.Single(
            ledger.Realizations,
            realization => realization.UserRegion == effect.Id);

        Assert.Equal(
            effect.Semantics,
            realization.PrimaryOutputNode.Semantics);
        Assert.Contains(
            "KeepAlive",
            effect.Semantics.Discriminator,
            StringComparison.Ordinal);
        Assert.Contains(effect.PhysicalRegion, ledger.ConsumedRegions);
        Assert.DoesNotContain(
            effect.PhysicalRegion,
            ledger.PreservedRegions);
    }

    [Fact]
    public void RegionLedgerRejectsChangedGuardedEffect()
    {
        using var source = OpenClassicFixture();
        IrFunction function = ImportClassicFixture(
            source,
            "AwaitInTryFinally");
        var evidence = Assert.IsType<ClassicAsyncRelationshipEvidence>(
            function.ClassicAsyncRelationship);
        var reconstruct =
            Assert.IsType<ClassicAsyncDecision.Reconstruct>(
                PublishedDecision(evidence));
        ClassicAsyncUserRegion effect = Assert.Single(
            reconstruct.Plan.RegionLedger.UserRegions,
            static region =>
                region.Semantics.Kind
                    == ClassicAsyncUserRegionKind.GuardedEffect);
        BlockContainer output = reconstruct.Plan.Body.Materialize();
        Call keepAlive = Assert.Single(
            output.Descendants.OfType<Call>(),
            static call => call.Callee.Name == "KeepAlive");
        LoadArgument argument = Assert.IsType<LoadArgument>(
            Assert.Single(keepAlive.Arguments));
        argument.ReplaceWith(new LoadArgument(
            argument.Index + 1,
            "other",
            argument.Type));

        Assert.True(
            ClassicAsyncReconstructionPass.TryCaptureOutputNodes(
                output,
                out List<ClassicAsyncOutputNode> outputNodes));
        ClassicAsyncOutputNode changed = Assert.Single(
            outputNodes,
            static node =>
                node.Semantics.Kind
                    == ClassicAsyncUserRegionKind.GuardedEffect);
        Assert.NotEqual(effect.Semantics, changed.Semantics);

        Assert.False(TryCreateRegionLedger(
            [effect],
            [new(effect.Id, changed)],
            out _));
    }

    [Fact]
    public void OutputGuardedEffectInventoryRejectsNonCall()
    {
        TypeRef int32 = TypeRef.CoreLib("System", "Int32");
        var tryBlock = new Block(0);
        tryBlock.Add(new Return(new Constant(0, int32)));
        var tryBody = new BlockContainer();
        tryBody.Add(tryBlock);
        var finallyBlock = new Block(1);
        finallyBlock.Add(
            new ExpressionStatement(new Constant(1, int32)));
        var finallyBody = new BlockContainer();
        finallyBody.Add(finallyBlock);
        var outputBlock = new Block(2);
        outputBlock.Add(new TryFinally(tryBody, finallyBody));
        var output = new BlockContainer();
        output.Add(outputBlock);

        Assert.False(
            ClassicAsyncReconstructionPass.TryCaptureOutputNodes(
                output,
                out _));
    }

    [Theory]
    [InlineData("AwaitValue", "9:parameter1:01:a", "9:parameter1:01:a")]
    [InlineData(
        "AwaitOrdinarySetMethod",
        "11:set_GetTask",
        "9:parameter1:04:task")]
    [InlineData(
        "AwaitInLoop",
        "15:foreach-element5:tasks",
        "15:foreach-element5:tasks")]
    public void AwaitedOperandIdentityRetainsItsAuthoredSource(
        string methodName,
        string firstFragment,
        string secondFragment)
    {
        using var source = OpenClassicFixture();
        IrFunction function = ImportClassicFixture(source, methodName);
        var evidence = Assert.IsType<ClassicAsyncRelationshipEvidence>(
            function.ClassicAsyncRelationship);
        var reconstruct =
            Assert.IsType<ClassicAsyncDecision.Reconstruct>(
                PublishedDecision(evidence));
        ClassicAsyncUserRegion operand = Assert.Single(
            reconstruct.Plan.RegionLedger.UserRegions,
            static region =>
                region.Semantics.Kind
                    == ClassicAsyncUserRegionKind.AwaitedOperand);

        Assert.Contains(
            firstFragment,
            operand.Semantics.Discriminator,
            StringComparison.Ordinal);
        Assert.Contains(
            secondFragment,
            operand.Semantics.Discriminator,
            StringComparison.Ordinal);
    }

    [Fact]
    public void RegionLedgerRejectsChangedAwaitedOperand()
    {
        var id = new ClassicAsyncRegionId(
            ClassicAsyncRegionHost.Execution,
            "0.0");
        var region = new ClassicAsyncUserRegion(
            id,
            PhysicalId(
                ClassicAsyncRegionHost.Execution,
                "0.0"),
            new(
                ClassicAsyncUserRegionKind.AwaitedOperand,
                "parameter|a|System.Threading.Tasks.Task<int>",
                Occurrence: 0));
        var changed = new ClassicAsyncOutputNode(
            region.Semantics with
            {
                Discriminator =
                    "parameter|b|System.Threading.Tasks.Task<int>",
            });

        Assert.False(TryCreateRegionLedger(
            [region],
            [new(id, changed)],
            out _));

        var second = new ClassicAsyncUserRegion(
            new(
                ClassicAsyncRegionHost.Execution,
                "0.1"),
            PhysicalId(
                ClassicAsyncRegionHost.Execution,
                "0.1"),
            new(
                ClassicAsyncUserRegionKind.AwaitedOperand,
                "parameter|b|System.Threading.Tasks.Task<int>",
                Occurrence: 1));
        Assert.False(TryCreateRegionLedger(
            [region, second],
            [
                new(
                    region.Id,
                    new(second.Semantics with { Occurrence = 0 })),
                new(
                    second.Id,
                    new(region.Semantics with { Occurrence = 1 })),
            ],
            out _));
    }

    [Fact]
    public void OutputAwaitInventoryRejectsUnrecognizedOperand()
    {
        TypeRef task = TypeRef.Definition(
            "Synthetic",
            "System.Threading.Tasks",
            "Task");
        TypeRef int32 = TypeRef.CoreLib("System", "Int32");
        var block = new Block(0);
        block.Add(new ExpressionStatement(new AwaitExpression(
            new LoadArgument(0, "task", task),
            resultType: Void)));
        block.Add(new ExpressionStatement(new AwaitExpression(
            new UnsupportedNode(
                0,
                "synthetic",
                "unrecognized awaited operand"),
            resultType: int32)));
        var body = new BlockContainer();
        body.Add(block);

        Assert.False(
            ClassicAsyncReconstructionPass.TryCaptureOutputNodes(
                body,
                out _));
    }

    [Fact]
    public void AwaitedIdentityIncludesTypedCallAndArgumentFacts()
    {
        TypeRef task = TypeRef.Definition(
            "Synthetic",
            "System.Threading.Tasks",
            "Task");
        TypeRef int32 = TypeRef.CoreLib("System", "Int32");
        TypeRef firstOwner =
            TypeRef.Definition("First.Assembly", "Samples", "Factory");
        TypeRef secondOwner =
            TypeRef.Definition("Second.Assembly", "Samples", "Factory");
        var firstMethod = new MethodRef(
            firstOwner,
            "Create",
            task,
            [task],
            HasThis: false)
        {
            TypeArguments = [int32],
        };
        var stringInstantiation = firstMethod with
        {
            TypeArguments =
            [
                TypeRef.CoreLib("System", "String"),
            ],
        };
        var otherAssembly = firstMethod with
        {
            DeclaringType = secondOwner,
        };
        TypeRef requiredModifier = TypeRef.CoreLib(
            "System.Runtime.CompilerServices",
            "IsReadOnlyAttribute");
        var modifiedSignature = firstMethod with
        {
            ParameterTypes =
            [
                task.WithCustomModifier(
                    requiredModifier,
                    isRequired: true),
            ],
        };
        var argument = new LoadArgument(0, "task", task);
        var first = new Call(firstMethod, isVirtual: false, [argument])
        {
            ConstrainedTo = firstOwner,
        };
        var otherConstraint =
            new Call(
                firstMethod,
                isVirtual: false,
                [(IrExpression)argument.Clone()])
            {
                ConstrainedTo = secondOwner,
            };

        Assert.True(
            ClassicAsyncReconstructionPass
                .TryGetSemanticExpressionKey(
                    first,
                    out string firstKey));
        Assert.True(
            ClassicAsyncReconstructionPass
                .TryGetSemanticExpressionKey(
                    new Call(
                        stringInstantiation,
                        isVirtual: false,
                        [(IrExpression)argument.Clone()]),
                    out string stringKey));
        Assert.True(
            ClassicAsyncReconstructionPass
                .TryGetSemanticExpressionKey(
                    new Call(
                        otherAssembly,
                        isVirtual: false,
                        [(IrExpression)argument.Clone()]),
                    out string assemblyKey));
        Assert.True(
            ClassicAsyncReconstructionPass
                .TryGetSemanticExpressionKey(
                    new Call(
                        modifiedSignature,
                        isVirtual: false,
                        [(IrExpression)argument.Clone()]),
                    out string modifiedKey));
        Assert.True(
            ClassicAsyncReconstructionPass
                .TryGetSemanticExpressionKey(
                    otherConstraint,
                    out string constraintKey));

        Assert.NotEqual(firstKey, stringKey);
        Assert.NotEqual(firstKey, assemblyKey);
        Assert.NotEqual(firstKey, modifiedKey);
        Assert.NotEqual(firstKey, constraintKey);

        Assert.True(
            ClassicAsyncReconstructionPass
                .TryGetSemanticExpressionKey(
                    new LoadArgument(0, "value", task),
                    out string firstArgumentKey));
        Assert.True(
            ClassicAsyncReconstructionPass
                .TryGetSemanticExpressionKey(
                    new LoadArgument(1, "value", task),
                    out string secondArgumentKey));
        Assert.True(
            ClassicAsyncReconstructionPass
                .TryGetSemanticExpressionKey(
                    new LoadArgument(0, "value", task)
                    {
                        IsDynamic = true,
                    },
                    out string dynamicArgumentKey));
        Assert.NotEqual(firstArgumentKey, secondArgumentKey);
        Assert.NotEqual(firstArgumentKey, dynamicArgumentKey);
    }

    [Theory]
    [InlineData("AwaitValue")]
    [InlineData("AwaitVoid")]
    [InlineData("TwoSequentialAwaits")]
    [InlineData("AwaitConditional")]
    [InlineData("AwaitInLoop")]
    [InlineData("AwaitInTryFinally")]
    public void AcceptedPlanPartitionsEveryPhysicalStatementSlot(
        string methodName)
    {
        using var source = OpenClassicFixture();
        IrFunction function = ImportClassicFixture(source, methodName);
        var evidence = Assert.IsType<ClassicAsyncRelationshipEvidence>(
            function.ClassicAsyncRelationship);
        var reconstruct =
            Assert.IsType<ClassicAsyncDecision.Reconstruct>(
                PublishedDecision(evidence));
        ClassicAsyncPlan plan = reconstruct.Plan;
        ClassicAsyncRegionLedger ledger = plan.RegionLedger;
        ClassicAsyncPhysicalRegionId[] physical =
        [
            .. ledger.PhysicalRegions.Select(static region => region.Id),
        ];

        Assert.NotEmpty(physical);
        Assert.Equal(
            physical.Length,
            ledger.ConsumedRegions.Count
                + ledger.PreservedRegions.Count);
        Assert.Empty(
            ledger.ConsumedRegions.Intersect(
                ledger.PreservedRegions));
        Assert.All(
            ledger.PhysicalRegions.Where(static region =>
                region.Id.Host == ClassicAsyncRegionHost.Kickoff),
            region => Assert.Contains(
                region.Id,
                ledger.ConsumedRegions));
        Assert.Contains(
            ledger.ConsumedRegions,
            region =>
                region.Host == ClassicAsyncRegionHost.Execution);
        Assert.Contains(
            ledger.PreservedRegions,
            region =>
                region.Host == ClassicAsyncRegionHost.Execution);
        Assert.All(
            ledger.UserRegions,
            region => Assert.Contains(
                region.PhysicalRegion,
                ledger.ConsumedRegions));
        Assert.All(
            ledger.PhysicalRegions,
            region => Assert.Equal(
                region.Id.Host == ClassicAsyncRegionHost.Kickoff
                    ? plan.Machine.Kickoff
                    : plan.Machine.Execution,
                region.Id.Method));
    }

    [Fact]
    public void RegionLedgerRejectsMissingDuplicateAndMismatchedRealizations()
    {
        var firstId = new ClassicAsyncRegionId(
            ClassicAsyncRegionHost.Execution,
            "0.0");
        var secondId = new ClassicAsyncRegionId(
            ClassicAsyncRegionHost.Execution,
            "0.1");
        var first = new ClassicAsyncUserRegion(
            firstId,
            PhysicalId(
                ClassicAsyncRegionHost.Execution,
                "0.0"),
            new(
                ClassicAsyncUserRegionKind.Throw,
                "throw",
                Occurrence: 0));
        var second = new ClassicAsyncUserRegion(
            secondId,
            PhysicalId(
                ClassicAsyncRegionHost.Execution,
                "0.1"),
            new(
                ClassicAsyncUserRegionKind.Break,
                "break",
                Occurrence: 0));
        var throwOutput = new ClassicAsyncOutputNode(
            first.Semantics);

        Assert.False(TryCreateRegionLedger(
            [first, second],
            [new(firstId, throwOutput)],
            out _));
        Assert.False(TryCreateRegionLedger(
            [first],
            [
                new(firstId, throwOutput),
                new(firstId, throwOutput with
                {
                    Semantics = throwOutput.Semantics with
                    {
                        Occurrence = 1,
                    },
                }),
            ],
            out _));
        Assert.False(TryCreateRegionLedger(
            [first],
            [new(firstId, throwOutput with
            {
                Semantics = second.Semantics,
            })],
            out _));
        Assert.False(TryCreateRegionLedger(
            [first, second],
            [
                new(firstId, throwOutput),
                new(secondId, throwOutput),
            ],
            out _));
    }

    [Fact]
    public void RegionLedgerUsesOccurrenceToPairRepeatedSemantics()
    {
        var first = new ClassicAsyncUserRegion(
            new(
                ClassicAsyncRegionHost.Execution,
                "0.0"),
            PhysicalId(
                ClassicAsyncRegionHost.Execution,
                "0.0"),
            new(
                ClassicAsyncUserRegionKind.CheckedArithmetic,
                "Add|True|False",
                Occurrence: 0));
        var second = new ClassicAsyncUserRegion(
            new(
                ClassicAsyncRegionHost.Execution,
                "0.1"),
            PhysicalId(
                ClassicAsyncRegionHost.Execution,
                "0.1"),
            first.Semantics with { Occurrence = 1 });
        var firstOutput = new ClassicAsyncOutputNode(first.Semantics);
        var secondOutput = new ClassicAsyncOutputNode(second.Semantics);

        Assert.True(TryCreateRegionLedger(
            [first, second],
            [
                new(first.Id, firstOutput),
                new(second.Id, secondOutput),
            ],
            out _));
        Assert.False(TryCreateRegionLedger(
            [first, second],
            [
                new(first.Id, secondOutput),
                new(second.Id, firstOutput),
            ],
            out _));
    }

    [Fact]
    public void RegionLedgerRequiresCompleteDisjointPhysicalPartition()
    {
        ClassicAsyncPhysicalRegion kickoff =
            Physical(ClassicAsyncRegionHost.Kickoff, "0.0");
        ClassicAsyncPhysicalRegion execution =
            Physical(ClassicAsyncRegionHost.Execution, "0.0");

        Assert.True(TryCreatePhysicalLedger(
            [kickoff, execution],
            [kickoff.Id],
            [execution.Id]));
        Assert.False(TryCreatePhysicalLedger(
            [kickoff, execution],
            [kickoff.Id],
            []));
        Assert.False(TryCreatePhysicalLedger(
            [kickoff, execution],
            [kickoff.Id, execution.Id],
            [execution.Id]));
        Assert.False(TryCreatePhysicalLedger(
            [kickoff, execution],
            [kickoff.Id, kickoff.Id],
            [execution.Id]));
        Assert.False(TryCreatePhysicalLedger(
            [kickoff, execution],
            [execution.Id],
            [kickoff.Id]));
    }

    [Fact]
    public void RegionLedgerRejectsRealizationFromPreservedMaterial()
    {
        ClassicAsyncPhysicalRegion kickoff =
            Physical(ClassicAsyncRegionHost.Kickoff, "0.0");
        ClassicAsyncPhysicalRegion execution =
            Physical(ClassicAsyncRegionHost.Execution, "0.0");
        var semantics = new ClassicAsyncRegionSemantics(
            ClassicAsyncUserRegionKind.Throw,
            "throw",
            Occurrence: 0);
        var userRegion = new ClassicAsyncUserRegion(
            new(ClassicAsyncRegionHost.Execution, "0.0.0"),
            execution.Id,
            semantics);

        Assert.False(ClassicAsyncRegionLedger.TryCreate(
            KickoffAddress,
            ExecutionAddress,
            [kickoff, execution],
            [kickoff.Id],
            [execution.Id],
            [userRegion],
            [
                new(
                    userRegion.Id,
                    new ClassicAsyncOutputNode(semantics)),
            ],
            out _));
    }

    [Fact]
    public void RegionLedgerRejectsUnsupportedConsumedControlFlow()
    {
        ClassicAsyncPhysicalRegion kickoff =
            Physical(ClassicAsyncRegionHost.Kickoff, "0.0");
        ClassicAsyncPhysicalRegion external = Physical(
            ClassicAsyncRegionHost.Execution,
            "0.0",
            hasExternalTarget: true);
        ClassicAsyncPhysicalRegion externalEntry = Physical(
            ClassicAsyncRegionHost.Execution,
            "0.1",
            hasExternalEntry: true);
        ClassicAsyncPhysicalRegion multiSuccessor = Physical(
            ClassicAsyncRegionHost.Execution,
            "0.2",
            successorMultiplicity: 3);

        Assert.False(TryCreatePhysicalLedger(
            [kickoff, external],
            [kickoff.Id, external.Id],
            []));
        Assert.False(TryCreatePhysicalLedger(
            [kickoff, externalEntry],
            [kickoff.Id, externalEntry.Id],
            []));
        Assert.False(TryCreatePhysicalLedger(
            [kickoff, multiSuccessor],
            [kickoff.Id, multiSuccessor.Id],
            []));
    }

    [Fact]
    public void PhysicalCensusRecordsExternalEntryAndSuccessorMultiplicity()
    {
        TypeRef boolean = TypeRef.CoreLib("System", "Boolean");
        var entered = new Block(0x20);
        entered.Add(new Return(null));
        var outer = new Block(0);
        outer.Add(new IfStatement(
            new Constant(true, boolean),
            entered,
            elseArm: null));
        outer.Add(new Branch(0x20));
        var externalBody = new BlockContainer();
        externalBody.Add(outer);
        var externalFunction = new IrFunction(
            "ExternalEntry",
            StateMachine,
            new MethodSignature(Void, [], true, 0),
            [],
            externalBody);

        Assert.True(
            ClassicAsyncReconstructionPass.TryCapturePhysicalRegions(
                externalFunction,
                ClassicAsyncRegionHost.Execution,
                ExecutionAddress,
                out var externalRegions));
        Assert.Contains(
            externalRegions,
            static region => region.HasExternalTarget);
        Assert.Contains(
            externalRegions,
            static region => region.HasExternalEntry);

        var dispatch = new Block(0);
        dispatch.Add(new SwitchBranch(
            new Constant(0, TypeRef.CoreLib("System", "Int32")),
            [4, 8]));
        var fallthrough = new Block(4);
        fallthrough.Add(new Return(null));
        var alternate = new Block(8);
        alternate.Add(new Return(null));
        var switchBody = new BlockContainer();
        switchBody.Add(dispatch);
        switchBody.Add(fallthrough);
        switchBody.Add(alternate);
        var switchFunction = new IrFunction(
            "MultiSuccessor",
            StateMachine,
            new MethodSignature(Void, [], true, 0),
            [],
            switchBody);

        Assert.True(
            ClassicAsyncReconstructionPass.TryCapturePhysicalRegions(
                switchFunction,
                ClassicAsyncRegionHost.Execution,
                ExecutionAddress,
                out var switchRegions));
        Assert.Contains(
            switchRegions,
            static region => region.SuccessorMultiplicity == 3);
    }

    [Fact]
    public void RegionLedgerRejectsForeignAndNonCanonicalPhysicalPaths()
    {
        ClassicAsyncPhysicalRegion kickoff =
            Physical(ClassicAsyncRegionHost.Kickoff, "0.0");
        var foreignId = new ClassicAsyncPhysicalRegionId(
            ClassicAsyncRegionHost.Execution,
            KickoffAddress,
            "0.0");
        var foreign = new ClassicAsyncPhysicalRegion(
            foreignId,
            EntryMultiplicity: 1,
            SuccessorMultiplicity: 1,
            HasExternalEntry: false,
            HasExternalTarget: false,
            LeavesRegion: false);
        ClassicAsyncPhysicalRegion unstable =
            Physical(ClassicAsyncRegionHost.Execution, "0.01");

        Assert.False(TryCreatePhysicalLedger(
            [kickoff, foreign],
            [kickoff.Id, foreign.Id],
            []));
        Assert.False(TryCreatePhysicalLedger(
            [kickoff, unstable],
            [kickoff.Id, unstable.Id],
            []));
    }

    static bool TryCreateRegionLedger(
        IReadOnlyList<ClassicAsyncUserRegion> userRegions,
        IReadOnlyList<ClassicAsyncUserRegionRealization> realizations,
        out ClassicAsyncRegionLedger ledger)
    {
        List<ClassicAsyncPhysicalRegion> physical =
        [
            Physical(ClassicAsyncRegionHost.Kickoff, "0.0"),
            .. userRegions
                .Select(static region => region.PhysicalRegion)
                .Distinct()
                .Select(static id => new ClassicAsyncPhysicalRegion(
                    id,
                    EntryMultiplicity: 1,
                    SuccessorMultiplicity: 1,
                    HasExternalEntry: false,
                    HasExternalTarget: false,
                    LeavesRegion: false)),
        ];
        ClassicAsyncPhysicalRegionId[] consumed =
        [
            .. physical.Select(static region => region.Id),
        ];
        return ClassicAsyncRegionLedger.TryCreate(
            KickoffAddress,
            ExecutionAddress,
            physical,
            consumed,
            [],
            userRegions,
            realizations,
            out ledger);
    }

    static bool TryCreatePhysicalLedger(
        IReadOnlyList<ClassicAsyncPhysicalRegion> physical,
        IReadOnlyList<ClassicAsyncPhysicalRegionId> consumed,
        IReadOnlyList<ClassicAsyncPhysicalRegionId> preserved)
        => ClassicAsyncRegionLedger.TryCreate(
            KickoffAddress,
            ExecutionAddress,
            physical,
            consumed,
            preserved,
            [],
            [],
            out _);

    static ClassicAsyncPhysicalRegion Physical(
        ClassicAsyncRegionHost host,
        string path,
        int entryMultiplicity = 1,
        int successorMultiplicity = 1,
        bool hasExternalEntry = false,
        bool hasExternalTarget = false,
        bool leavesRegion = false)
        => new(
            PhysicalId(host, path),
            entryMultiplicity,
            successorMultiplicity,
            hasExternalEntry,
            hasExternalTarget,
            leavesRegion);

    static ClassicAsyncPhysicalRegionId PhysicalId(
        ClassicAsyncRegionHost host,
        string path)
        => new(
            host,
            host == ClassicAsyncRegionHost.Kickoff
                ? KickoffAddress
                : ExecutionAddress,
            path);

    static MethodRef CaptureMoveNextRequest(
        MetadataSource source,
        string methodName = "AwaitVoid")
    {
        IrFunction function = ImportClassicFixture(
            source,
            methodName);
        var context = PassContext.ForImport(
            method => IrImporter.Import(source, method));

        RunUntilClassicAsync(function, context);

        var evidence = Assert.IsType<ClassicAsyncRelationshipEvidence>(
            function.ClassicAsyncRelationship);
        var decision = Assert.IsType<ClassicAsyncDecision.Reconstruct>(
            PublishedDecision(evidence));
        ClassicAsyncMachine machine = decision.Plan.Machine;
        return new MethodRef(
            machine.StateMachineType,
            "MoveNext",
            Void,
            [],
            HasThis: true)
        {
            ExactDefinitionAddress = machine.Execution,
            ExactDefinitionAcquisitionGuard =
                machine.AcquisitionGuard,
        };
    }

    static ClassicAsyncPlanningSession PlanningSession(
        ClassicAsyncRelationshipEvidence evidence)
        => Assert.IsType<ClassicAsyncPlanningSession>(
            evidence.PlanningSession);

    static ClassicAsyncDecision PublishedDecision(
        ClassicAsyncRelationshipEvidence evidence)
    {
        var prepared =
            Assert.IsType<ClassicAsyncPreparationResult.Decided>(
                PlanningSession(evidence).Prepare(evidence));
        return prepared.Decision;
    }

    static IrFunction BuildSupportLookalike()
    {
        var block = new Block(0);
        block.Add(new ExpressionStatement(new LoadField(
            new FieldRef(StateMachine, "<>t__builder", Builder),
            new LoadArgument(0, "this", StateMachine))));
        block.Add(new ExpressionStatement(new Call(
            new MethodRef(
                StateMachine,
                "SideEffect",
                Void,
                [],
                HasThis: false),
            isVirtual: false,
            [])));
        block.Add(new Return(null));

        var body = new BlockContainer();
        body.Add(block);
        return new IrFunction(
            "MoveNext",
            StateMachine,
            new MethodSignature(
                Void,
                [],
                HasThis: true,
                GenericParameterCount: 0),
            [],
            body)
        {
            DeclaringTypeCompilerGenerated = MetadataFactState.Yes,
        };
    }

    static IrFunction BuildKickoffLookalike()
    {
        var owner = TypeRef.Definition("Synthetic", "Samples", "Outer");
        var block = new Block(0);
        block.Add(new StoreField(
            new FieldRef(StateMachine, "<>t__builder", Builder),
            new LoadLocalAddress(0, StateMachine),
            new Call(
                new MethodRef(
                    Builder,
                    "Create",
                    Builder,
                    [],
                    HasThis: false),
                isVirtual: false,
                [])));
        block.Add(new ExpressionStatement(new Call(
            new MethodRef(
                Builder,
                "Start",
                Void,
                [],
                HasThis: true),
            isVirtual: false,
            [])));
        block.Add(new Return(new LoadProperty(
            new MethodRef(
                Builder,
                "get_Task",
                Task,
                [],
                HasThis: true),
            new LoadLocalAddress(0, StateMachine),
            [])));

        var body = new BlockContainer();
        body.Add(block);
        return new IrFunction(
            "KickoffMethod",
            owner,
            new MethodSignature(
                Task,
                [],
                HasThis: false,
                GenericParameterCount: 0),
            [StateMachine],
            body);
    }

    static MetadataSource OpenClassicFixture()
    {
        string configuration = new DirectoryInfo(
            AppContext.BaseDirectory).Name;
        string path = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "ILInspector.Decompiler.Fixtures.ClassicAsync",
            configuration,
            "ILInspector.Decompiler.Fixtures.ClassicAsync.dll"));
        return MetadataSource.Open(path);
    }

    static IrFunction ImportClassicFixture(
        MetadataSource source,
        string methodName)
        => Assert.IsType<IrFunction>(IrImporter.Import(
            source,
            "ILInspector.Decompiler.Fixtures.ClassicAsync.AsyncFixtures",
            methodName));

    static void RunUntilClassicAsync(
        IrFunction function,
        PassContext context)
    {
        foreach (IIrPass pass in IrPasses.Default)
        {
            pass.Run(function, context);
            if (pass is ClassicAsyncReconstructionPass)
                return;
        }

        Assert.Fail("ClassicAsyncReconstructionPass is not registered.");
    }

    static void RunBeforeClassicAsync(
        IrFunction function,
        PassContext context)
    {
        foreach (IIrPass pass in IrPasses.Default)
        {
            if (pass is ClassicAsyncReconstructionPass)
                return;
            pass.Run(function, context);
        }

        Assert.Fail("ClassicAsyncReconstructionPass is not registered.");
    }

    static string SubtreeSignature(IrNode node)
        => string.Join(
            "\n",
            node.Descendants.Prepend(node).Select(current =>
                $"{current.GetType().Name}:{current.Describe()}"));
}
