using ILInspector.Decompiler.Pipeline;
using ILInspector.Metadata;

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
    }

    static MethodRef CaptureMoveNextRequest(MetadataSource source)
    {
        IrFunction function = ImportClassicFixture(source, "AwaitVoid");
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
