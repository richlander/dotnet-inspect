using System.Collections.Immutable;
using System.Reflection.Metadata;

using ILInspector.Decompiler.Pipeline;
using ILInspector.Metadata;
using ILInspector.MetadataPrimitives;

namespace ILInspector.Decompiler.Tests;

[Trait("Area", "Pass")]
public sealed partial class ClassicInverseCoreTests
{
    const string FixtureType =
        "ILInspector.Decompiler.Fixtures.ClassicAsync.AsyncFixtures";

    static readonly ImmutableArray<string> s_acceptedPopulation =
    [
        "AwaitConditional",
        "AwaitInLoop",
        "AwaitInTryFinally",
        "AwaitOrdinarySetMethod",
        "AwaitValue",
        "AwaitVoid",
        "DynamicArrayReferenceIdentity",
        "DynamicReferenceIdentity",
        "InterfaceReceiver",
        "ObjectArrayReferenceIdentity",
        "SequentialWithImplicitConversion",
        "SequentialWithRealizedInitializer",
        "SequentialWithRealizedWithExpression",
        "TwoSequentialAwaits",
        "TwoSequentialNamedAwaits",
    ];

    [Fact]
    public void ClassicInversePlanPartitionsPhysicalRegions()
    {
        using RequestScope scope = OpenRequest("TwoSequentialAwaits");
        ClassicInversePlan plan = Reconstruct(scope.Request);

        Assert.NotEmpty(plan.PhysicalPartition);
        Assert.All(
            plan.PhysicalPartition,
            region => Assert.Equal(
                ClassicInverseCoordinateSpace.Import,
                region.Space));
        Assert.Contains(
            plan.PhysicalPartition,
            region => region.Body == ClassicInverseBodyId.Kickoff);
        Assert.Contains(
            plan.PhysicalPartition,
            region => region.Body == ClassicInverseBodyId.Execution);
        Assert.Equal(
            plan.PhysicalPartition.Length,
            plan.PhysicalPartition
                .Select(region => (
                    region.Body,
                    Path: ClassicInverseSignature.Path(region.Path)))
                .Distinct()
                .Count());
        Assert.All(
            plan.PhysicalPartition.Where(
                region => region.Disposition
                    == ClassicInverseRegionDisposition.Semantic),
            region => Assert.NotEmpty(region.ImportOffsets));

        using RequestScope rejected = OpenRequest("SequentialWithFieldStore");
        var decline = Assert.IsType<ClassicInverseDecision.Decline>(
            ClassicInverseCore.Decide(rejected.Request));
        Assert.Equal(
            ClassicInverseDeclineReason.UnclassifiedPhysicalRegion,
            decline.Reason);
    }

    [Fact]
    public void ClassicInversePhysicalPartitionCoversRawRegionsConsumedByRaising()
    {
        using RequestScope scope =
            OpenRequest("SequentialWithRealizedInitializer");
        ClassicInversePlanningView planning =
            ClassicInversePlanningView.Derive(scope.Request);
        int rawNodeCount =
            scope.Request.ExecutionBody.Body.Descendants.Count() + 1;
        int planningNodeCount =
            planning.ExecutionBody.Body.Descendants.Count() + 1;

        Assert.DoesNotContain(
            scope.Request.ExecutionBody.Body.Descendants,
            node => node is ObjectInitializerExpression);
        Assert.Contains(
            planning.ExecutionBody.Body.Descendants,
            node => node is ObjectInitializerExpression);
        Assert.True(rawNodeCount > 0 && planningNodeCount > 0);

        ClassicInversePlan plan = Reconstruct(scope.Request);
        Assert.Equal(
            rawNodeCount,
            CountCoveredNodes(
                scope.Request.ExecutionBody.Body,
                plan.PhysicalPartition.Where(
                    region => region.Body
                        == ClassicInverseBodyId.Execution)));
        Assert.Equal(
            scope.Request.KickoffBody.Body.Descendants.Count() + 1,
            CountCoveredNodes(
                scope.Request.KickoffBody.Body,
                plan.PhysicalPartition.Where(
                    region => region.Body
                        == ClassicInverseBodyId.Kickoff)));
    }

    [Fact]
    public void ClassicInversePlanRealizesEverySemanticEffectExactlyOnce()
    {
        using RequestScope scope = OpenRequest("TwoSequentialAwaits");
        ClassicInversePlan plan = Reconstruct(scope.Request);

        Assert.NotEmpty(plan.SemanticRealizations);
        Assert.All(
            plan.SemanticRealizations,
            receipt =>
            {
                Assert.Equal(
                    ClassicInverseCoordinateSpace.Planning,
                    receipt.SourceSpace);
                Assert.Equal(
                    ClassicInverseCoordinateSpace.Output,
                    receipt.OutputSpace);
                Assert.NotEmpty(receipt.ImportOffsets);
                Assert.NotEmpty(receipt.ImportPaths);
            });
        Assert.Equal(
            plan.SemanticRealizations.Length,
            plan.SemanticRealizations
                .Select(receipt => ClassicInverseSignature.Path(
                    receipt.SourcePath))
                .Distinct(StringComparer.Ordinal)
                .Count());
        Assert.Equal(
            plan.SemanticRealizations.Length,
            plan.SemanticRealizations
                .Select(receipt => ClassicInverseSignature.Path(
                    receipt.OutputPath))
                .Distinct(StringComparer.Ordinal)
                .Count());
        Assert.All(
            plan.SemanticRealizations,
            receipt => Assert.Equal(
                receipt.SourceEffects,
                receipt.OutputEffects));

        using RequestScope rejected =
            OpenRequest("AwaitConditionalWithWrappedResult");
        var decline = Assert.IsType<ClassicInverseDecision.Decline>(
            ClassicInverseCore.Decide(rejected.Request));
        Assert.Equal(
            ClassicInverseDeclineReason.UnrealizedSemanticEffect,
            decline.Reason);

        using RequestScope invented = OpenRequest("TwoSequentialAwaits");
        (ClassicInversePlanningView planning,
            ClassicInverseCandidate candidate,
            ClassicInverseShellFacts shell) =
            Candidate(invented.Request);
        IrNode duplicatedEffect = candidate.Claims
            .Single(claim =>
                claim.Rule == ClassicInverseRealizationRule.Statement)
            .Output
            .Clone();
        candidate.Statements.Add(duplicatedEffect);
        var inventedDecline = Assert.IsType<ClassicInverseDecision.Decline>(
            ClassicInverseAccountant.Account(
                invented.Request,
                planning,
                candidate,
                shell,
                new ClassicInverseBudget()));
        Assert.Equal(
            ClassicInverseDeclineReason.InventedOutputEffect,
            inventedDecline.Reason);

        using RequestScope mistyped = OpenRequest("TwoSequentialAwaits");
        (ClassicInversePlanningView mistypedPlanning,
            ClassicInverseCandidate mistypedCandidate,
            ClassicInverseShellFacts mistypedShell) =
            Candidate(mistyped.Request);
        var outputStatement = Assert.IsType<ExpressionStatement>(
            mistypedCandidate.Claims.Single(claim =>
                claim.Rule == ClassicInverseRealizationRule.Statement).Output);
        Call outputCall = Assert.IsType<Call>(outputStatement.Expression);
        var replacement = new Call(
            outputCall.Callee with
            {
                ParameterTypes =
                [
                    TypeRef.CoreLib("System", "String"),
                ],
            },
            outputCall.IsVirtual,
            outputCall.Arguments.Select(
                argument => (IrExpression)argument.Clone()))
        {
            ConstrainedTo = outputCall.ConstrainedTo,
            ExtensionSyntaxConflict = outputCall.ExtensionSyntaxConflict,
        };
        outputStatement.SetChild(0, replacement);

        var mistypedDecline = Assert.IsType<ClassicInverseDecision.Decline>(
            ClassicInverseAccountant.Account(
                mistyped.Request,
                mistypedPlanning,
                mistypedCandidate,
                mistypedShell,
                new ClassicInverseBudget()));
        Assert.Equal(
            ClassicInverseDeclineReason.UnrealizedSemanticEffect,
            mistypedDecline.Reason);
    }

    [Fact]
    public void ClassicInverseRawAndPlanningValuesRetainIdentity()
    {
        using RequestScope scope = OpenRequest("TwoSequentialAwaits");
        ClassicInverseRequest request = CopyRequest(
            scope.Request,
            runPasses: (body, passes) =>
            {
                scope.Request.RunPasses!(body, passes);
                if (body.Name != "MoveNext")
                    return;

                Call[] binds =
                [
                    .. body.Body.Descendants
                        .OfType<Call>()
                        .Where(call =>
                            call.Callee.Name == "GetAwaiter"
                            && call.Arguments.Count == 1),
                ];
                Assert.Equal(2, binds.Length);
                IrNode first = binds[0].Children[0].Clone();
                IrNode second = binds[1].Children[0].Clone();
                binds[0].SetChild(0, second);
                binds[1].SetChild(0, first);
            });

        var decline = Assert.IsType<ClassicInverseDecision.Decline>(
            ClassicInverseCore.Decide(request));
        Assert.Equal(
            ClassicInverseDeclineReason.UnrealizedSemanticEffect,
            decline.Reason);
        Assert.Contains(
            "change semantic value identity at position",
            decline.Detail,
            StringComparison.Ordinal);

        using RequestScope dropped = OpenRequest(
            "SequentialWithRealizedInitializer");
        ClassicInverseRequest droppedRequest = CopyRequest(
            dropped.Request,
            runPasses: (body, passes) =>
            {
                dropped.Request.RunPasses!(body, passes);
                if (body.Name != "MoveNext")
                    return;

                Binary sum = Assert.Single(
                    body.Body.Descendants.OfType<Binary>(),
                    binary => binary.Kind == BinaryKind.Add);
                sum.ReplaceWith(sum.Left.Clone());
            });
        var droppedDecline = Assert.IsType<ClassicInverseDecision.Decline>(
            ClassicInverseCore.Decide(droppedRequest));
        Assert.Equal(
            ClassicInverseDeclineReason.UnrealizedSemanticEffect,
            droppedDecline.Reason);
        Assert.Contains(
            "different semantic value sequences",
            droppedDecline.Detail,
            StringComparison.Ordinal);

        ClassicInversePlan plan = Reconstruct(scope.Request);
        Assert.Contains(
            plan.PhysicalPartition,
            region => region.Disposition
                == ClassicInverseRegionDisposition.Semantic
                && region.Rule == "raw:user-value"
                && region.NodeForm.Contains(".a (Task<int>)"));
        Assert.Contains(
            plan.PhysicalPartition,
            region => region.Disposition
                == ClassicInverseRegionDisposition.Semantic
                && region.Rule == "raw:user-value"
                && region.NodeForm.Contains(".b (Task<int>)"));
        Assert.DoesNotContain(
            plan.PhysicalPartition,
            region => region.Rule == "raw:user-value"
                && region.NodeForm.Contains(
                    "TaskAwaiter",
                    StringComparison.Ordinal));
    }

    [Fact]
    public void ClassicInverseSemanticLedgerRejectsGloballyReorderedClaims()
    {
        using RequestScope scope = OpenRequest("TwoSequentialNamedAwaits");
        (ClassicInversePlanningView planning,
            ClassicInverseCandidate candidate,
            ClassicInverseShellFacts shell) = Candidate(scope.Request);
        Assert.True(candidate.Statements.Count >= 2);
        int first = 0;
        int second = 1;
        (candidate.Statements[first], candidate.Statements[second]) =
            (candidate.Statements[second], candidate.Statements[first]);

        var decline = Assert.IsType<ClassicInverseDecision.Decline>(
            ClassicInverseAccountant.Account(
                scope.Request,
                planning,
                candidate,
                shell,
                new ClassicInverseBudget()));
        Assert.Equal(
            ClassicInverseDeclineReason.UnrealizedSemanticEffect,
            decline.Reason);
    }

    [Fact]
    public void ClassicInverseSemanticLedgerIncludesInitializerMemberEffects()
    {
        using RequestScope initializer =
            OpenRequest("SequentialWithRealizedInitializer");
        using RequestScope with =
            OpenRequest("SequentialWithRealizedWithExpression");

        // The ledger names the consumed member by canonical typed identity, so
        // the expectation is the identity of the imported field itself rather
        // than any rendering of it.
        FieldRef boxValue = Assert.Single(
            initializer.Request.ExecutionBody.Body.Descendants
                .OfType<StoreField>()
                .Select(store => store.Field)
                .Distinct(),
            field => field.Name == "Value");
        Assert.Contains(
            Reconstruct(initializer.Request).SemanticRealizations
                .SelectMany(receipt => receipt.SourceEffects),
            effect => effect
                == $"store:{ClassicInverseTypedIdentity.Field(boxValue)}");
        Assert.Contains(
            Reconstruct(with.Request).SemanticRealizations
                .SelectMany(receipt => receipt.SourceEffects),
            effect => effect.Contains("Value", StringComparison.Ordinal));
    }

    [Fact]
    public void ClassicInversePlanRequiresCompleteStructuredAncestorPaths()
    {
        using RequestScope scope = OpenRequest("AwaitInTryFinally");
        ClassicInversePlan plan = Reconstruct(scope.Request);

        Assert.Equal(
            plan.SemanticRealizations.Length,
            plan.StructuredAncestorReceipts.Length);
        Assert.All(
            plan.StructuredAncestorReceipts,
            receipt =>
            {
                Assert.Equal(
                    ClassicInverseCoordinateSpace.Planning,
                    receipt.ConsumedSpace);
                Assert.NotEmpty(receipt.ImportOffsets);
                Assert.NotEmpty(receipt.ImportPaths);
                Assert.NotEmpty(receipt.Steps);
                Assert.Equal("recipe-root", receipt.Steps[^1].Rule);
                Assert.Equal(
                    ClassicInverseAncestorKind.Transparent,
                    receipt.Steps[^1].Kind);
            });
        Assert.Contains(
            plan.StructuredAncestorReceipts.SelectMany(
                receipt => receipt.Steps),
            step => step.Kind == ClassicInverseAncestorKind.Reproduced);
        Assert.Contains(
            plan.StructuredAncestorReceipts.SelectMany(
                receipt => receipt.Steps),
            step => step.Kind == ClassicInverseAncestorKind.Protocol);
        Assert.Contains(
            plan.StructuredAncestorReceipts.SelectMany(
                receipt => receipt.Steps),
            step => step.Rule == "try-body");
        Assert.Contains(
            plan.StructuredAncestorReceipts.SelectMany(
                receipt => receipt.Steps),
            step => step.Rule == "finally-body");

        using RequestScope rejected =
            OpenRequest("AwaitInLoopWithWrappedOperand");
        var decline = Assert.IsType<ClassicInverseDecision.Decline>(
            ClassicInverseCore.Decide(rejected.Request));
        Assert.Equal(
            ClassicInverseDeclineReason.UnmodeledStructuredAncestor,
            decline.Reason);
    }

    [Theory]
    [InlineData(
        "AwaitConditionalWithWrappedResult",
        "UnrealizedSemanticEffect")]
    [InlineData(
        "AwaitInLoopWithWrappedOperand",
        "UnmodeledStructuredAncestor")]
    [InlineData(
        "AwaitInTryFinallyWithGuardedCall",
        "UnmodeledStructuredAncestor")]
    public void
        ClassicInverseSideEffectsInExpressionsDeclineWithoutRealization(
            string methodName,
            string expectedReason)
    {
        using RequestScope scope = OpenRequest(methodName);

        var decline = Assert.IsType<ClassicInverseDecision.Decline>(
            ClassicInverseCore.Decide(scope.Request));
        Assert.Equal(expectedReason, decline.Reason.ToString());
    }

    [Theory]
    [InlineData("SequentialWithFieldStore")]
    [InlineData("LoopWithAccumulatorWrite")]
    public void ClassicInverseNestedStoresDoNotEscapeTheirControlContext(
        string methodName)
    {
        using RequestScope scope = OpenRequest(methodName);

        var decline = Assert.IsType<ClassicInverseDecision.Decline>(
            ClassicInverseCore.Decide(scope.Request));
        Assert.Equal(
            ClassicInverseDeclineReason.UnclassifiedPhysicalRegion,
            decline.Reason);
    }

    [Fact]
    public void ClassicInverseControlRegionsRejectEscapedRealizations()
    {
        using RequestScope scope = OpenRequest("TwoSequentialAwaits");
        (ClassicInversePlanningView planning,
            ClassicInverseCandidate candidate,
            ClassicInverseShellFacts shell) =
            Candidate(scope.Request);
        ClassicInverseClaim awaitClaim = candidate.Claims.First(
            claim => claim.Rule == ClassicInverseRealizationRule.AwaitResult);
        candidate.DeclareControlRegion(
            "synthetic-escaped-control",
            [awaitClaim.Source],
            candidate.Statements[^1]);

        var decline = Assert.IsType<ClassicInverseDecision.Decline>(
            ClassicInverseAccountant.Account(
                scope.Request,
                planning,
                candidate,
                shell,
                new ClassicInverseBudget()));
        Assert.Equal(
            ClassicInverseDeclineReason.EscapedControlContext,
            decline.Reason);
    }

    [Fact]
    public void ClassicInverseStructuredViewsRetainImportCorrespondence()
    {
        using RequestScope scope = OpenRequest("TwoSequentialAwaits");
        ClassicInversePlan plan = Reconstruct(scope.Request);

        Assert.All(
            plan.PhysicalPartition.Where(region =>
                region.Body == ClassicInverseBodyId.Execution
                && region.Disposition
                    == ClassicInverseRegionDisposition.Semantic),
            region =>
            {
                Assert.NotEmpty(region.ImportOffsets);
                Assert.All(
                    region.ImportOffsets,
                    offset => Assert.Contains(
                        offset,
                        scope.Request.ExecutionImportOffsets));
            });

        ClassicInverseRequest missingCorrespondence = CopyRequest(
            scope.Request,
            executionImportOffsets: ImmutableHashSet<int>.Empty);
        var decline = Assert.IsType<ClassicInverseDecision.Decline>(
            ClassicInverseCore.Decide(missingCorrespondence));
        Assert.Equal(
            ClassicInverseDeclineReason.MissingImportCorrespondence,
            decline.Reason);
    }

    [Fact]
    public void ClassicInverseDecisionIsDetachedAndDeterministic()
    {
        using RequestScope first = OpenRequest("TwoSequentialAwaits");
        using RequestScope second = OpenRequest("TwoSequentialAwaits");
        TypeRef stringType = TypeRef.CoreLib("System", "String");
        TypeRef objectType = TypeRef.CoreLib("System", "Object");
        var callerFacts = new Dictionary<TypeRef, TypeShape>
        {
            [stringType] = TypeShape.Reference,
            [objectType] = TypeShape.Reference,
        };
        var equivalentFacts = new Dictionary<TypeRef, TypeShape>
        {
            [objectType] = TypeShape.Reference,
            [stringType] = TypeShape.Reference,
        };
        first.Request.ExecutionBody.TypeShapes = callerFacts;
        second.Request.ExecutionBody.TypeShapes = equivalentFacts;

        ClassicInversePlan firstPlan = Reconstruct(first.Request);
        ClassicInversePlan secondPlan = Reconstruct(second.Request);
        Assert.Equal(firstPlan, secondPlan);

        using RequestScope different = OpenRequest("TwoSequentialAwaits");
        different.Request.ExecutionBody.TypeShapes =
            new Dictionary<TypeRef, TypeShape>
            {
                [stringType] = TypeShape.ValueType,
                [objectType] = TypeShape.Reference,
            };
        Assert.NotEqual(firstPlan, Reconstruct(different.Request));

        int capturedFactCount = firstPlan.TypeFacts.TypeShapes.Count;
        callerFacts.Clear();
        first.Request.ExecutionBody.Body.DetachChildren();

        Assert.Equal(capturedFactCount, firstPlan.TypeFacts.TypeShapes.Count);
        BlockContainer firstBody = firstPlan.MaterializeBody();
        BlockContainer secondBody = firstPlan.MaterializeBody();
        Assert.NotSame(firstBody, secondBody);
        Assert.NotSame(firstBody.Blocks[0], secondBody.Blocks[0]);
        Assert.Equal(
            firstBody.Blocks[0].Children.Select(node => node.Describe()),
            secondBody.Blocks[0].Children.Select(node => node.Describe()));

        using RequestScope initializer =
            OpenRequest("SequentialWithRealizedWithExpression");
        BlockContainer initializerBody =
            Reconstruct(initializer.Request).MaterializeBody();
        MethodRef[] consumedMethods =
        [
            .. initializerBody.Descendants
                .OfType<WithExpression>()
                .SelectMany(node => node.ConsumedMethods)
                .OfType<MethodRef>(),
        ];
        Assert.NotEmpty(consumedMethods);
        Assert.All(
            consumedMethods,
            method => Assert.Null(method.ExactDefinitionAcquisitionGuard));
    }

    [Fact]
    public void ClassicInversePlanningUsesTheProvidedPassContext()
    {
        using MetadataSource source = OpenClassicFixture();
        using RequestScope scope =
            OpenRequest(source, "TwoSequentialAwaits");
        int runs = 0;

        var decision = ClassicInverseCore.Decide(
            CopyRequest(
                scope.Request,
                runPasses: (body, passes) =>
                {
                    runs++;
                    IrPasses.Run(
                        body,
                        passes,
                        PassContext.ForImport(
                            method => IrImporter.Import(source, method)));
                }));

        Assert.IsType<ClassicInverseDecision.Reconstruct>(decision);
        Assert.Equal(2, runs);
    }

    [Fact]
    public void ClassicInverseRawKickoffBindsExactParameterTransfers()
    {
        using RequestScope scope = OpenRequest("TwoSequentialAwaits");
        Assert.IsType<ClassicInverseDecision.Reconstruct>(
            ClassicInverseCore.Decide(scope.Request));

        var rawKickoff = (IrFunction)scope.Request.KickoffBody.Clone();
        SwapKickoffParameterTransfers(rawKickoff);

        bool repairedPlanningKickoff = false;
        Action<IrFunction, ImmutableArray<IIrPass>> originalRunner =
            Assert.IsType<Action<IrFunction, ImmutableArray<IIrPass>>>(
                scope.Request.RunPasses);
        ClassicInverseRequest request = ClassicInverseCore.Request(
            rawKickoff,
            scope.Request.StateMachineLocal,
            scope.Request.KickoffSourceOffset,
            scope.Request.ExecutionBody,
            scope.Request.ExecutionImportOffsets,
            SeedOf(scope.Request),
            (body, passes) =>
            {
                if (body.Name == scope.Request.KickoffBody.Name)
                {
                    SwapKickoffParameterTransfers(body);
                    repairedPlanningKickoff = true;
                }
                originalRunner(body, passes);
            });

        var decline = Assert.IsType<ClassicInverseDecision.Decline>(
            ClassicInverseCore.Decide(request));
        Assert.Equal(
            ClassicInverseDeclineReason.UnclassifiedPhysicalRegion,
            decline.Reason);
        Assert.True(repairedPlanningKickoff);
    }

    [Fact]
    public void ClassicInversePlanningFailuresRemainFailures()
    {
        using RequestScope scope = OpenRequest("TwoSequentialAwaits");
        ClassicInverseRequest invalid = CopyRequest(
            scope.Request,
            stateMachineLocal: -1);

        var invalidFailure = Assert.IsType<ClassicInverseDecision.Failed>(
            ClassicInverseCore.Decide(invalid));
        Assert.Equal(
            ClassicInverseFailureKind.InvalidCorrelation,
            invalidFailure.Failure.Kind);

        var budgetFailure = Assert.IsType<ClassicInverseDecision.Failed>(
            ClassicInverseCore.Decide(
                scope.Request,
                new ClassicInverseBudget(1)));
        Assert.Equal(
            ClassicInverseFailureKind.BudgetExhausted,
            budgetFailure.Failure.Kind);
    }

    [Fact]
    public void ClassicInversePlanningDepthExhaustionRemainsVisible()
    {
        using RequestScope scope = OpenMutatedRequest(
            "AwaitValue",
            execution =>
            {
                LoadField original = Assert.Single(
                    execution.Body.Descendants.OfType<LoadField>(),
                    load => load.Field.Name == "b");
                IrExpression nested = (IrExpression)original.Clone();
                TypeRef intType = TypeRef.CoreLib("System", "Int32");
                for (int depth = 0; depth < 12_000; depth++)
                {
                    var zero = new Pipeline.Constant(0, intType);
                    zero.SetSourceOffset(100_000 + (depth * 2));
                    var add = new Binary(
                        BinaryKind.Add,
                        isChecked: false,
                        isUnsigned: false,
                        nested,
                        zero);
                    add.SetSourceOffset(100_001 + (depth * 2));
                    nested = add;
                }
                original.ReplaceWith(nested);
            });

        var failure = Assert.IsType<ClassicInverseDecision.Failed>(
            ClassicInverseCore.Decide(scope.Request));
        Assert.Equal(
            ClassicInverseFailureKind.BudgetExhausted,
            failure.Failure.Kind);
        Assert.Contains(
            "planning-view depth",
            failure.Failure.Detail,
            StringComparison.Ordinal);
    }

    [Fact]
    public void ClassicInverseCompletionCallbacksAreProvenExactlyOnce()
    {
        using RequestScope scope = OpenRequest("TwoSequentialAwaits");
        ClassicInversePlan plan = Reconstruct(scope.Request);

        Assert.Single(
            plan.PhysicalPartition,
            region => region.Rule == "raw:builder-SetResult");
        Assert.Single(
            plan.PhysicalPartition,
            region => region.Rule == "raw:builder-SetException");
        Assert.All(
            plan.PhysicalPartition.Where(region => region.Rule.StartsWith(
                "raw:builder-",
                StringComparison.Ordinal)),
            region =>
            {
                Assert.Equal(
                    ClassicInverseRegionDisposition.Protocol,
                    region.Disposition);
                Assert.True(region.OwnsSubtree);
            });

        using RequestScope duplicated = OpenMutatedRequest(
            "TwoSequentialAwaits",
            DuplicateCompletionCallback);
        var decline = Assert.IsType<ClassicInverseDecision.Decline>(
            ClassicInverseCore.Decide(duplicated.Request));
        Assert.Equal(
            ClassicInverseDeclineReason.UnclassifiedPhysicalRegion,
            decline.Reason);
        Assert.Contains(
            "exactly one builder SetResult callback",
            decline.Detail,
            StringComparison.Ordinal);
    }

    [Fact]
    public void ClassicInverseDeclinesWhenSuccessfulPathBypassesSetResult()
    {
        int changedOffset = -1;
        int decoyBlockOffset = -1;
        int originalTarget = -1;
        int returnOffset = -1;
        using RequestScope bypassed = OpenMutatedRequest(
            "TwoSequentialAwaits",
            execution =>
            {
                Call setResult = Assert.Single(
                    execution.Body.Descendants.OfType<Call>(),
                    call => call.Callee.Name == "SetResult");
                Block setResultBlock = Assert.IsType<Block>(
                    setResult.Parent?.Parent);
                Leave success = Assert.Single(
                    execution.Body.Descendants.OfType<Leave>(),
                    leave => leave.TargetOffset == setResultBlock.StartOffset);
                Block returnBlock = Assert.Single(
                    execution.Body.Descendants.OfType<Block>(),
                    block => block.Children is [Return { Value: null }]);

                changedOffset = success.SourceOffset;
                originalTarget = success.TargetOffset;
                returnOffset = returnBlock.StartOffset;
                Assert.NotEqual(setResultBlock.StartOffset, returnOffset);
                var replacement = new Leave(returnOffset);
                replacement.SetSourceOffset(changedOffset);
                success.ReplaceWith(replacement);

                BlockContainer container =
                    Assert.IsType<BlockContainer>(setResultBlock.Parent);
                decoyBlockOffset = execution.Body.Descendants
                    .Max(node => node.SourceOffset) + 1;
                var decoy = new Block(decoyBlockOffset);
                var decoyLeave = new Leave(originalTarget);
                decoyLeave.SetSourceOffset(decoyBlockOffset);
                decoy.Add(decoyLeave);
                container.Add(decoy);
            });

        Leave changed = Assert.Single(
            bypassed.Request.ExecutionBody.Body.Descendants.OfType<Leave>(),
            leave => leave.SourceOffset == changedOffset);
        Assert.Equal(returnOffset, changed.TargetOffset);

        Assert.IsType<ClassicInverseDecision.Decline>(
            ClassicInverseCore.Decide(bypassed.Request));

        Action<IrFunction, ImmutableArray<IIrPass>> originalRunner =
            Assert.IsType<Action<IrFunction, ImmutableArray<IIrPass>>>(
                bypassed.Request.RunPasses);
        bool repairedPlanningClone = false;
        bool removedDecoy = false;
        ClassicInverseRequest healedPlanning = CopyRequest(
            bypassed.Request,
            runPasses: (body, passes) =>
            {
                Leave? changedLeave = body.Body.Descendants.OfType<Leave>()
                    .SingleOrDefault(leave =>
                        leave.SourceOffset == changedOffset
                        && leave.TargetOffset == returnOffset);
                if (changedLeave is not null)
                {
                    repairedPlanningClone = true;
                    var replacement = new Leave(originalTarget);
                    replacement.SetSourceOffset(changedOffset);
                    changedLeave.ReplaceWith(replacement);
                }
                Block? decoy = body.Body.Descendants.OfType<Block>()
                    .SingleOrDefault(block =>
                        block.StartOffset == decoyBlockOffset);
                if (decoy is not null)
                {
                    removedDecoy = true;
                    decoy.Detach();
                }
                originalRunner(body, passes);
            });
        Assert.IsType<ClassicInverseDecision.Decline>(
            ClassicInverseCore.Decide(healedPlanning));
        Assert.True(repairedPlanningClone);
        Assert.True(removedDecoy);
    }

    [Fact]
    public void ClassicInverseDeclinesConditionalWithMovedJoin()
    {
        int joinOffset = -1;
        int awaitedBlockOffset = -1;
        int zeroBlockOffset = -1;
        using RequestScope movedJoin = OpenMutatedRequest(
            "AwaitConditional",
            execution =>
            {
                Call getResult = Assert.Single(
                    execution.Body.Descendants.OfType<Call>(),
                    call => call.Callee.Name == "GetResult");
                StoreLocal awaitStore = Assert.Single(
                    execution.Body.Descendants.OfType<StoreLocal>(),
                    store => store.Value.Descendants.Prepend(store.Value)
                        .Contains(getResult));
                StoreLocal zeroStore = Assert.Single(
                    execution.Body.Descendants.OfType<StoreLocal>(),
                    store => store.Index == awaitStore.Index
                        && store.Value is Pipeline.Constant { Value: 0 });
                Block awaitedContinuation =
                    Assert.IsType<Block>(awaitStore.Parent);
                Block zeroBlock = Assert.IsType<Block>(zeroStore.Parent);
                StoreLocal finalStore = Assert.Single(
                    execution.Body.Descendants.OfType<StoreLocal>(),
                    store => store.Value is LoadLocal load
                        && load.Index == awaitStore.Index
                        && store.Index != awaitStore.Index);
                Block finalBlock = Assert.IsType<Block>(finalStore.Parent);
                Branch join = Assert.Single(
                    awaitedContinuation.Children.OfType<Branch>(),
                    branch => branch.TargetOffset == finalBlock.StartOffset);
                BlockContainer container =
                    Assert.IsType<BlockContainer>(awaitedContinuation.Parent);
                IReadOnlyList<Block> blocks = container.Blocks;
                int awaitedIndex = blocks.ToList().IndexOf(
                    awaitedContinuation);
                Assert.Same(zeroBlock, blocks[awaitedIndex + 1]);

                joinOffset = join.SourceOffset;
                awaitedBlockOffset = awaitedContinuation.StartOffset;
                zeroBlockOffset = zeroBlock.StartOffset;
                join.Detach();
                zeroBlock.Add(join);
            });

        Branch moved = Assert.Single(
            movedJoin.Request.ExecutionBody.Body.Descendants.OfType<Branch>(),
            branch => branch.SourceOffset == joinOffset);
        Assert.Equal(
            zeroBlockOffset,
            Assert.IsType<Block>(moved.Parent).StartOffset);

        ClassicInverseDecision decision =
            ClassicInverseCore.Decide(movedJoin.Request);
        if (decision is ClassicInverseDecision.Reconstruct reconstructed)
        {
            Conditional conditional = Assert.Single(
                reconstructed.Plan.MaterializeBody()
                    .Descendants.OfType<Conditional>());
            Assert.IsType<AwaitExpression>(conditional.WhenTrue);
            Assert.Equal(
                0,
                Assert.IsType<Pipeline.Constant>(
                    conditional.WhenFalse).Value);
        }
        Assert.IsType<ClassicInverseDecision.Decline>(decision);

        Action<IrFunction, ImmutableArray<IIrPass>> originalRunner =
            Assert.IsType<Action<IrFunction, ImmutableArray<IIrPass>>>(
                movedJoin.Request.RunPasses);
        bool repairedPlanningClone = false;
        ClassicInverseRequest healedPlanning = CopyRequest(
            movedJoin.Request,
            runPasses: (body, passes) =>
            {
                Branch? join = body.Body.Descendants.OfType<Branch>()
                    .SingleOrDefault(branch =>
                        branch.SourceOffset == joinOffset
                        && branch.Parent is Block parent
                        && parent.StartOffset == zeroBlockOffset);
                if (join is not null)
                {
                    Block awaited = Assert.Single(
                        body.Body.Descendants.OfType<Block>(),
                        block => block.StartOffset == awaitedBlockOffset);
                    join.Detach();
                    awaited.Add(join);
                    repairedPlanningClone = true;
                }
                originalRunner(body, passes);
            });
        Assert.IsType<ClassicInverseDecision.Decline>(
            ClassicInverseCore.Decide(healedPlanning));
        Assert.True(repairedPlanningClone);
    }

    [Fact]
    public void ClassicInverseDeclinesLoopWithPostLoopCollectionHoist()
    {
        int hoistOffset = -1;
        int entryBlockOffset = -1;
        int entryPosition = -1;
        int exitBlockOffset = -1;
        using RequestScope movedHoist = OpenMutatedRequest(
            "AwaitInLoop",
            execution =>
            {
                StoreField hoist = Assert.Single(
                    execution.Body.Descendants.OfType<StoreField>(),
                    store => store.Field.Name == "<>7__wrap1"
                        && store.Value is LoadField);
                Call setResult = Assert.Single(
                    execution.Body.Descendants.OfType<Call>(),
                    call => call.Callee.Name == "SetResult");
                var finalResult =
                    Assert.IsType<LoadLocal>(setResult.Arguments[1]);
                StoreLocal finalStore = Assert.Single(
                    execution.Body.Descendants.OfType<StoreLocal>(),
                    store => store.Index == finalResult.Index
                        && store.Value is LoadLocal);
                Block exitBlock = Assert.IsType<Block>(finalStore.Parent);

                Block entryBlock = Assert.IsType<Block>(hoist.Parent);
                hoistOffset = hoist.SourceOffset;
                entryBlockOffset = entryBlock.StartOffset;
                entryPosition = hoist.ChildIndex;
                exitBlockOffset = exitBlock.StartOffset;
                hoist.Detach();
                IReadOnlyList<IrNode> exitStatements =
                    exitBlock.DetachChildren();
                foreach (IrNode statement in exitStatements)
                {
                    if (ReferenceEquals(statement, finalStore))
                        exitBlock.Add(hoist);
                    exitBlock.Add(statement);
                }
            });

        StoreField moved = Assert.Single(
            movedHoist.Request.ExecutionBody.Body.Descendants.OfType<StoreField>(),
            store => store.SourceOffset == hoistOffset);
        Assert.Equal(
            exitBlockOffset,
            Assert.IsType<Block>(moved.Parent).StartOffset);

        ClassicInverseDecision decision =
            ClassicInverseCore.Decide(movedHoist.Request);
        if (decision is ClassicInverseDecision.Reconstruct reconstructed)
        {
            ForeachStatement loop = Assert.Single(
                reconstructed.Plan.MaterializeBody()
                    .Descendants.OfType<ForeachStatement>());
            var collection = Assert.IsType<LoadArgument>(loop.Collection);
            Assert.Equal(0, collection.Index);
        }
        Assert.IsType<ClassicInverseDecision.Decline>(decision);

        Action<IrFunction, ImmutableArray<IIrPass>> originalRunner =
            Assert.IsType<Action<IrFunction, ImmutableArray<IIrPass>>>(
                movedHoist.Request.RunPasses);
        bool repairedPlanningClone = false;
        ClassicInverseRequest healedPlanning = CopyRequest(
            movedHoist.Request,
            runPasses: (body, passes) =>
            {
                StoreField? hoist = body.Body.Descendants
                    .OfType<StoreField>()
                    .SingleOrDefault(store =>
                        store.SourceOffset == hoistOffset
                        && store.Parent is Block parent
                        && parent.StartOffset == exitBlockOffset);
                if (hoist is not null)
                {
                    Block entry = Assert.Single(
                        body.Body.Descendants.OfType<Block>(),
                        block => block.StartOffset == entryBlockOffset);
                    hoist.Detach();
                    IReadOnlyList<IrNode> statements = entry.DetachChildren();
                    for (int i = 0; i <= statements.Count; i++)
                    {
                        if (i == entryPosition)
                            entry.Add(hoist);
                        if (i < statements.Count)
                            entry.Add(statements[i]);
                    }
                    repairedPlanningClone = true;
                }
                originalRunner(body, passes);
            });
        Assert.IsType<ClassicInverseDecision.Decline>(
            ClassicInverseCore.Decide(healedPlanning));
        Assert.True(repairedPlanningClone);
    }

    [Fact]
    public void ClassicInverseCompletionCatchBindsItsExactHandler()
    {
        using RequestScope narrowed = OpenMutatedRequest(
            "TwoSequentialAwaits",
            static execution => execution.Regions =
            [
                .. execution.Regions.Select(static region =>
                    region.Kind == HandlerKind.Catch
                        ? region with
                        {
                            CatchType = TypeRef.CoreLib(
                                "System",
                                "ArgumentException"),
                        }
                        : region),
            ]);
        var typeDecline = Assert.IsType<ClassicInverseDecision.Decline>(
            ClassicInverseCore.Decide(narrowed.Request));
        Assert.Equal(
            ClassicInverseDeclineReason.UnclassifiedPhysicalRegion,
            typeDecline.Reason);
        Assert.Contains(
            "does not catch core-library System.Exception",
            typeDecline.Detail,
            StringComparison.Ordinal);

        using RequestScope rebound = OpenMutatedRequest(
            "TwoSequentialAwaits",
            RebindCompletionException);
        var bindingDecline = Assert.IsType<ClassicInverseDecision.Decline>(
            ClassicInverseCore.Decide(rebound.Request));
        Assert.Equal(
            ClassicInverseDeclineReason.UnclassifiedPhysicalRegion,
            bindingDecline.Reason);
        Assert.Contains(
            "completion catch variable is not the local SetException reads",
            bindingDecline.Detail,
            StringComparison.Ordinal);
    }

    [Fact]
    public void ClassicInverseRawFinallyBindsItsExactRegion()
    {
        using RequestScope scope = OpenRequest("AwaitInTryFinally");
        Assert.IsType<ClassicInverseDecision.Reconstruct>(
            ClassicInverseCore.Decide(scope.Request));

        using RequestScope narrowed = OpenMutatedRequest(
            "AwaitInTryFinally",
            execution => NarrowRawHandlerTryOffset(
                execution,
                HandlerKind.Finally));

        Assert.IsType<ClassicInverseDecision.Decline>(
            ClassicInverseCore.Decide(narrowed.Request));
    }

    [Fact]
    public void ClassicInverseCompletionCatchBindsItsExactProtectedExtent()
    {
        using RequestScope scope = OpenRequest("TwoSequentialAwaits");
        Assert.IsType<ClassicInverseDecision.Reconstruct>(
            ClassicInverseCore.Decide(scope.Request));

        using RequestScope narrowed = OpenMutatedRequest(
            "TwoSequentialAwaits",
            execution => NarrowRawHandlerTryOffset(
                execution,
                HandlerKind.Catch));

        Assert.IsType<ClassicInverseDecision.Decline>(
            ClassicInverseCore.Decide(narrowed.Request));
    }

    [Fact]
    public void ClassicInverseResumeStatesAreProvenAgainstTheirDispatch()
    {
        foreach (string methodName in new[]
        {
            "AwaitConditional",
            "AwaitInLoop",
            "AwaitInTryFinally",
            "TwoSequentialAwaits",
        })
        {
            using RequestScope accepted = OpenRequest(methodName);
            ClassicInversePlan plan = Reconstruct(accepted.Request);

            Assert.All(
                plan.PhysicalPartition.Where(region => region.NodeForm.Contains(
                    "<>1__state",
                    StringComparison.Ordinal)),
                region =>
                {
                    Assert.Equal(
                        ClassicInverseRegionDisposition.Protocol,
                        region.Disposition);
                    Assert.Equal("raw:state-field-store", region.Rule);
                });
            Assert.Contains(
                plan.PhysicalPartition,
                region => region.Rule == "raw:state-local-store");
            Assert.Contains(
                plan.PhysicalPartition,
                region => region.Rule == "raw:state-dispatch");
            Assert.Contains(
                plan.PhysicalPartition,
                region => region.Rule == "raw:state-spill");
            Assert.DoesNotContain(
                plan.PhysicalPartition,
                region => region.Rule == "raw:pure-structure"
                    && region.NodeForm.Contains(
                        "<>1__state",
                        StringComparison.Ordinal));
        }

        using RequestScope altered = OpenMutatedRequest(
            "TwoSequentialAwaits",
            RetargetFirstSuspensionState);
        var decline = Assert.IsType<ClassicInverseDecision.Decline>(
            ClassicInverseCore.Decide(altered.Request));
        Assert.Equal(
            ClassicInverseDeclineReason.UnclassifiedPhysicalRegion,
            decline.Reason);
        Assert.Contains(
            "state 42 is stored at a suspension but 0 dispatch tests resume it",
            decline.Detail,
            StringComparison.Ordinal);
    }

    [Fact]
    public void ClassicInverseRawLocalValuesKeepPlanningCorrespondence()
    {
        using RequestScope scope = OpenRequest("TwoSequentialAwaits");
        ClassicInversePlan plan = Reconstruct(scope.Request);

        Assert.Single(
            plan.PhysicalPartition,
            region => region.Rule == "raw:user-value"
                && region.NodeForm.Contains("<x>5__2", StringComparison.Ordinal));
        Assert.Single(
            plan.PhysicalPartition,
            region => region.Rule == "raw:user-value"
                && region.NodeForm.StartsWith(
                    "LoadLocal 1 ",
                    StringComparison.Ordinal));

        ClassicInverseRequest dropped = CopyRequest(
            scope.Request,
            runPasses: (body, passes) =>
            {
                scope.Request.RunPasses!(body, passes);
                if (body.Name != "MoveNext")
                    return;

                TupleExpression tuple = Assert.Single(
                    body.Body.Descendants.OfType<TupleExpression>());
                var replacement = new TupleExpression(
                    tuple.TupleType,
                    [(IrExpression)tuple.Children[0].Clone()]);
                replacement.SetSourceOffset(tuple.SourceOffset);
                tuple.ReplaceWith(replacement);
            });

        var decline = Assert.IsType<ClassicInverseDecision.Decline>(
            ClassicInverseCore.Decide(dropped));
        Assert.Equal(
            ClassicInverseDeclineReason.UnrealizedSemanticEffect,
            decline.Reason);
        Assert.Contains(
            "different semantic value sequences",
            decline.Detail,
            StringComparison.Ordinal);
    }

    [Fact]
    public void ClassicInverseCallIdentityComparesTypedInstantiation()
    {
        using RequestScope scope = OpenRequest("TwoSequentialAwaits");
        ClassicInversePlan plan = Reconstruct(scope.Request);

        string keepAlive = Assert.Single(
            plan.SemanticRealizations.SelectMany(
                receipt => receipt.SourceEffects),
            effect => effect.Contains("KeepAlive", StringComparison.Ordinal));
        MethodRef keepAliveCallee = Assert.Single(
            scope.Request.ExecutionBody.Body.Descendants.OfType<Call>()
                .Select(call => call.Callee),
            callee => callee.Name == "KeepAlive");
        Assert.Equal(
            $"call:{ClassicInverseTypedIdentity.Method(keepAliveCallee)}:direct",
            keepAlive);

        // Only the generic instantiation changes; display text does not.
        AssertRebindingCalleeDeclines(
            scope.Request,
            static callee => callee with
            {
                TypeArguments = [TypeRef.CoreLib("System", "String")],
            });
        // Only the declaring assembly changes; display text does not.
        AssertRebindingCalleeDeclines(
            scope.Request,
            static callee => callee with
            {
                DeclaringType = TypeRef.Definition("Planted", "System", "GC"),
            });
        // Only the by-ref call-site facts change; display text does not.
        AssertRebindingCalleeDeclines(
            scope.Request,
            static callee => callee with
            {
                ParameterRefKinds = [ArgumentRefKind.Ref],
                ParameterRefKindsFacts = ParameterRefKindFacts.Known,
            });
        // Only the exact definition provenance changes.
        AssertRebindingCalleeDeclines(
            scope.Request,
            static callee => callee with
            {
                ExactDefinitionAddress = new MetadataMethodAddress(
                    Guid.Empty,
                    System.Reflection.Metadata.Ecma335.MetadataTokens
                        .MethodDefinitionHandle(1)),
            });
    }

    static void AssertRebindingCalleeDeclines(
        ClassicInverseRequest request,
        Func<MethodRef, MethodRef> rebind)
    {
        ClassicInverseRequest rebound = CopyRequest(
            request,
            runPasses: (body, passes) =>
            {
                request.RunPasses!(body, passes);
                if (body.Name != "MoveNext")
                    return;

                Call call = Assert.Single(
                    body.Body.Descendants.OfType<Call>(),
                    candidate => candidate.Callee.Name == "KeepAlive");
                var statement = Assert.IsType<ExpressionStatement>(call.Parent);
                var replacement = new Call(
                    rebind(call.Callee),
                    call.IsVirtual,
                    call.Arguments.Select(
                        argument => (IrExpression)argument.Clone()))
                {
                    ConstrainedTo = call.ConstrainedTo,
                    ExtensionSyntaxConflict = call.ExtensionSyntaxConflict,
                };
                replacement.SetSourceOffset(call.SourceOffset);
                statement.SetChild(0, replacement);
            });

        var decline = Assert.IsType<ClassicInverseDecision.Decline>(
            ClassicInverseCore.Decide(rebound));
        Assert.Equal(
            ClassicInverseDeclineReason.UnrealizedSemanticEffect,
            decline.Reason);
        Assert.Contains(
            "different semantic effect sequences",
            decline.Detail,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// Adds a second, structurally identical completion callback whose nodes
    /// carry fresh import offsets, so nothing but callback cardinality and
    /// identity can reject it.
    /// </summary>
    static void DuplicateCompletionCallback(IrFunction execution)
    {
        ExpressionStatement completion = execution.Body.Descendants
            .OfType<ExpressionStatement>()
            .Last(statement =>
                statement.Expression is Call { Callee.Name: "SetResult" });
        var block = (Block)completion.Parent!;
        IrNode duplicate = completion.Clone();
        int offset = execution.Body.Descendants
            .Select(node => node.SourceOffset)
            .DefaultIfEmpty(0)
            .Max() + 1;
        foreach (IrNode node in duplicate.Descendants.Prepend(duplicate))
            node.SetSourceOffset(offset++);

        IReadOnlyList<IrNode> statements = block.DetachChildren();
        foreach (IrNode statement in statements)
        {
            if (ReferenceEquals(statement, completion))
                block.Add(duplicate);
            block.Add(statement);
        }
    }

    /// <summary>
    /// Passes a different local to <c>SetException</c> than the handler bound.
    /// </summary>
    static void RebindCompletionException(IrFunction execution)
    {
        Call setException = Assert.Single(
            execution.Body.Descendants.OfType<Call>(),
            call => call.Callee.Name == "SetException");
        var caught = Assert.IsType<LoadLocal>(setException.Arguments[1]);
        var replacement = new LoadLocal(2, caught.Type);
        replacement.SetSourceOffset(caught.SourceOffset);
        caught.ReplaceWith(replacement);
    }

    /// <summary>
    /// Replaces the first suspension's state constant with one no dispatcher
    /// tests, leaving every other shape intact.
    /// </summary>
    static void RetargetFirstSuspensionState(IrFunction execution)
    {
        StoreStackSlot spill = execution.Body.Descendants
            .OfType<StoreStackSlot>()
            .First(store =>
                store.Value is Pipeline.Constant { Value: 0 });
        var zero = (Pipeline.Constant)spill.Value;
        var replacement = new Pipeline.Constant(42, zero.Type);
        replacement.SetSourceOffset(zero.SourceOffset);
        zero.ReplaceWith(replacement);
    }

    [Fact]
    public void ClassicInverseSuspensionsBindTheirExactAwaiterTransfer()
    {
        using RequestScope scope = OpenRequest("TwoSequentialAwaits");
        ClassicInversePlan plan = Reconstruct(scope.Request);

        foreach (string rule in new[]
        {
            "raw:awaiter-cache-store",
            "raw:awaiter-restore",
            "raw:awaiter-clear",
        })
        {
            Assert.Equal(
                2,
                plan.PhysicalPartition.Count(region => region.Rule == rule));
        }
        Assert.All(
            plan.PhysicalPartition.Where(region => region.Rule.StartsWith(
                "raw:awaiter-",
                StringComparison.Ordinal)),
            region => Assert.Equal(
                ClassicInverseRegionDisposition.Protocol,
                region.Disposition));

        // Same shapes, same awaiter local, same machine — only the cache field
        // a suspension writes no longer matches the one its resume restores.
        using RequestScope mismatched = OpenMutatedRequest(
            "TwoSequentialAwaits",
            RenameFirstAwaiterCacheField);
        var decline = Assert.IsType<ClassicInverseDecision.Decline>(
            ClassicInverseCore.Decide(mismatched.Request));
        Assert.Equal(
            ClassicInverseDeclineReason.UnclassifiedPhysicalRegion,
            decline.Reason);
        Assert.Contains(
            "does not restore the exact awaiter its suspension cached",
            decline.Detail,
            StringComparison.Ordinal);
    }

    [Fact]
    public void ClassicInverseBuilderCallbacksAreProvenByExactTypedSignature()
    {
        // A same-named builder outside the core library is a lookalike; its
        // callbacks are not this machine's completion protocol.
        using RequestScope lookalike = OpenMutatedRequest(
            "TwoSequentialAwaits",
            execution => RebindCompletionBuilder(
                execution,
                TypeRef.Definition(
                    "Planted",
                    "System.Runtime.CompilerServices",
                    "AsyncTaskMethodBuilder"),
                rebindBuilderField: true));
        var lookalikeDecline = Assert.IsType<ClassicInverseDecision.Decline>(
            ClassicInverseCore.Decide(lookalike.Request));
        Assert.Equal(
            ClassicInverseDeclineReason.UnclassifiedPhysicalRegion,
            lookalikeDecline.Reason);
        Assert.Contains(
            "exactly one builder SetException callback; the body has 0",
            lookalikeDecline.Detail,
            StringComparison.Ordinal);

        // A core-library builder callback that is not declared on the type the
        // machine's own '<>t__builder' field carries.
        using RequestScope unbound = OpenMutatedRequest(
            "TwoSequentialAwaits",
            execution => RebindCompletionBuilder(
                execution,
                TypeRef.CoreLib(
                    "System.Runtime.CompilerServices",
                    "AsyncValueTaskMethodBuilder"),
                rebindBuilderField: false));
        var unboundDecline = Assert.IsType<ClassicInverseDecision.Decline>(
            ClassicInverseCore.Decide(unbound.Request));
        Assert.Equal(
            ClassicInverseDeclineReason.UnclassifiedPhysicalRegion,
            unboundDecline.Reason);
        Assert.Contains(
            "not on the machine's own '<>t__builder' type",
            unboundDecline.Detail,
            StringComparison.Ordinal);

        // Same callee name, same argument shape, different declared signature.
        using RequestScope mistyped = OpenMutatedRequest(
            "AwaitValue",
            MistypeSetResultSignature);
        var mistypedDecline = Assert.IsType<ClassicInverseDecision.Decline>(
            ClassicInverseCore.Decide(mistyped.Request));
        Assert.Equal(
            ClassicInverseDeclineReason.UnclassifiedPhysicalRegion,
            mistypedDecline.Reason);
        Assert.Contains(
            "the SetResult callback is not 'void SetResult(T)'",
            mistypedDecline.Detail,
            StringComparison.Ordinal);

        // Both spaces can independently prove a callback shape, but they must
        // still agree on the exact callback and builder-field identities.
        using RequestScope mismatched = OpenRequest("TwoSequentialAwaits");
        Action<IrFunction, ImmutableArray<IIrPass>> runPasses =
            Assert.IsType<Action<IrFunction, ImmutableArray<IIrPass>>>(
                mismatched.Request.RunPasses);
        var mismatchedDecision = ClassicInverseCore.Decide(
            CopyRequest(
                mismatched.Request,
                runPasses: (body, passes) =>
                {
                    runPasses(body, passes);
                    if (body.Body.Descendants.OfType<Call>().Any(
                        call => call.Callee.Name == "SetException"))
                    {
                        RebindCompletionBuilder(
                            body,
                            TypeRef.CoreLib(
                                "System.Runtime.CompilerServices",
                                "AsyncValueTaskMethodBuilder"),
                            rebindBuilderField: true);
                    }
                }));
        var mismatchedDecline = Assert.IsType<ClassicInverseDecision.Decline>(
            mismatchedDecision);
        Assert.Contains(
            "raw import and planning view complete through different SetException callbacks",
            mismatchedDecline.Detail,
            StringComparison.Ordinal);
    }

    [Fact]
    public void ClassicInverseStorageBindsExactTypedFieldIdentity()
    {
        using RequestScope scope = OpenRequest("AwaitValue");
        Assert.IsType<ClassicInverseDecision.Reconstruct>(
            ClassicInverseCore.Decide(scope.Request));

        using RequestScope mistyped = OpenMutatedRequest(
            "AwaitValue",
            RetypeExecutionFieldAAsProbeAwaitable);

        Assert.IsType<ClassicInverseDecision.Decline>(
            ClassicInverseCore.Decide(mistyped.Request));
    }

    [Fact]
    public void ClassicInverseProofWorkStaysProportionalToItsChargedBudget()
    {
        // Every proof phase charges once per node it touches, so a rescan of
        // the whole body per state would appear here as consumption growing
        // with states x nodes rather than with nodes.
        foreach (string methodName in new[]
        {
            "AwaitValue",
            "AwaitInLoop",
            "TwoSequentialAwaits",
        })
        {
            using RequestScope scope = OpenRequest(methodName);
            var budget = new ClassicInverseBudget();
            ClassicInversePlanningView planning =
                ClassicInversePlanningView.Derive(scope.Request);
            ClassicInverseShellFacts shell = ClassicInverseShellFacts.Derive(
                planning.ExecutionBody,
                scope.Request.ExecutionBody,
                budget);
            Assert.Null(shell.Protocol.Failure);

            int nodes = planning.ExecutionBody.Body.Descendants.Count() + 1
                + scope.Request.ExecutionBody.Body.Descendants.Count() + 1;
            Assert.InRange(budget.Consumed, nodes, 4 * nodes);

            // The charges are load-bearing, not decoration: one unit short of
            // what the proof consumed, it exhausts instead of proving.
            var starved = new ClassicInverseBudget(budget.Consumed - 1);
            ClassicInverseShellFacts starvedShell =
                ClassicInverseShellFacts.Derive(
                    ClassicInversePlanningView.Derive(scope.Request)
                        .ExecutionBody,
                    scope.Request.ExecutionBody,
                    starved);
            Assert.True(starved.Exhausted);
            Assert.Contains(
                "exhausted the planning budget",
                Assert.IsType<string>(starvedShell.Protocol.Failure),
                StringComparison.Ordinal);

            // Exhaustion stays a visible failure, never a decline or a
            // partial proof.
            var failure = Assert.IsType<ClassicInverseDecision.Failed>(
                ClassicInverseCore.Decide(
                    scope.Request,
                    new ClassicInverseBudget(budget.Consumed)));
            Assert.Equal(
                ClassicInverseFailureKind.BudgetExhausted,
                failure.Failure.Kind);
        }
    }

    [Fact]
    public void ClassicInverseTypedIdentityIsCompleteAndPrefixFree()
    {
        // The encoder claims to mirror TypeRef equality exactly. Product
        // construction cannot naturally form these close pairs, so the
        // invariant is asserted directly against the compared facts.
        MetadataTypeDefinitionName nested = Assert.IsType<
            MetadataTypeDefinitionNameResult.Valid>(
                MetadataTypeDefinitionName.Create("N", ["Outer", "Inner"]))
            .Name;

        TypeRef[] samples =
        [
            TypeRef.Definition("A", "N", "X"),
            TypeRef.Definition("A", "N", "Y"),
            TypeRef.Definition("B", "N", "X"),
            TypeRef.Definition("A", "M", "X"),
            // Separator-shifted pairs whose joined renderings coincide.
            TypeRef.Definition("A", "N.X", "Y"),
            TypeRef.Definition("A", "N", "X.Y"),
            TypeRef.Definition("A!N", "", "X"),
            TypeRef.Definition("A", "", "N!X"),
            // Same metadata name segments, different Name.
            TypeRef.Definition("A", "N", "Outer+Inner"),
            TypeRef.DefinitionWithResolution(
                "A",
                "N",
                "Renamed",
                ValueTypeHint.Unknown,
                MetadataFactState.Unknown,
                enclosingType: null,
                definitionName: nested,
                resolutionAssembly: null),
            TypeRef.CoreLib("System", "Int32"),
            TypeRef.CoreLib("System", "String"),
            TypeRef.SzArray(TypeRef.CoreLib("System", "Int32")),
            TypeRef.SzArray(TypeRef.CoreLib("System", "String")),
            TypeRef.MdArray(TypeRef.CoreLib("System", "Int32"), 2),
            TypeRef.MdArray(TypeRef.CoreLib("System", "Int32"), 3),
            TypeRef.ByRef(TypeRef.CoreLib("System", "Int32")),
            TypeRef.Pointer(TypeRef.CoreLib("System", "Int32")),
            TypeRef.GenericParameter(0, "T"),
            TypeRef.GenericParameter(1, "T"),
            TypeRef.MethodGenericParameter(0, "T"),
            TypeRef.GenericInstance(
                TypeRef.Definition("A", "N", "G`1"),
                [TypeRef.CoreLib("System", "Int32")]),
            TypeRef.GenericInstance(
                TypeRef.Definition("A", "N", "G`1"),
                [TypeRef.CoreLib("System", "String")]),
            // Unsupported reason and calling convention are both variable text.
            TypeRef.Unsupported("a$b"),
            TypeRef.Unsupported("a"),
            TypeRef.FunctionPointer(
                TypeRef.CoreLib("System", "Void"),
                [TypeRef.CoreLib("System", "Int32")],
                "unmanaged"),
            TypeRef.FunctionPointer(
                TypeRef.CoreLib("System", "Void"),
                [TypeRef.CoreLib("System", "Int32")],
                "unmanaged[Cdecl]"),
            TypeRef.FunctionPointer(
                TypeRef.CoreLib("System", "Void"),
                [TypeRef.ByRef(TypeRef.CoreLib("System", "Int32"))],
                ""),
            TypeRef.FunctionPointer(
                TypeRef.CoreLib("System", "Void"),
                [TypeRef.ByRef(TypeRef.CoreLib("System", "Int32"))
                    .WithCustomModifier(
                        TypeRef.CoreLib(
                            "System.Runtime.InteropServices",
                            "OutAttribute"),
                        isRequired: true)],
                ""),
        ];

        MetadataTypeDefinitionName nestedX = Assert.IsType<
            MetadataTypeDefinitionNameResult.Valid>(
                MetadataTypeDefinitionName.Create("N", ["X", "Inner"]))
            .Name;
        samples =
        [
            .. samples,
            TypeRef.DefinitionWithResolution(
                "A",
                "N",
                "X",
                ValueTypeHint.Unknown,
                MetadataFactState.Unknown,
                enclosingType: null,
                definitionName: nestedX,
                resolutionAssembly: null),
        ];

        foreach (TypeRef left in samples)
        {
            foreach (TypeRef right in samples)
            {
                Assert.Equal(
                    left.Equals(right),
                    ClassicInverseTypedIdentity.Type(left)
                        == ClassicInverseTypedIdentity.Type(right));
            }
        }

        // The member-level encodings length-prefix their own text too, so a
        // member name cannot absorb its declaring type's or its own separator.
        var box = TypeRef.Definition("A", "N", "Box");
        Assert.NotEqual(
            ClassicInverseTypedIdentity.Field(
                new FieldRef(box, "Value", TypeRef.CoreLib("System", "Int32"))),
            ClassicInverseTypedIdentity.Field(
                new FieldRef(
                    TypeRef.Definition("A", "N", "Box::Value"),
                    "",
                    TypeRef.CoreLib("System", "Int32"))));
        var target = new MethodRef(
            box,
            "M",
            TypeRef.CoreLib("System", "Void"),
            [],
            HasThis: true);
        Assert.NotEqual(
            ClassicInverseTypedIdentity.Method(target),
            ClassicInverseTypedIdentity.Method(target with { Name = "M/instance" }));
        Assert.NotEqual(
            ClassicInverseTypedIdentity.Method(target),
            ClassicInverseTypedIdentity.Method(target with { HasThis = false }));
        Assert.NotEqual(
            ClassicInverseTypedIdentity.Method(target),
            ClassicInverseTypedIdentity.Method(
                target with
                {
                    TypeArguments = [TypeRef.CoreLib("System", "Int32")],
                }));
        Assert.NotEqual(
            ClassicInverseTypedIdentity.Method(target),
            ClassicInverseTypedIdentity.Method(
                target with { HasRefReadOnlyParameters = true }));
    }

    /// <summary>
    /// Renames the field one suspension caches its awaiter into, leaving the
    /// awaiter local, the resume restore, and the resume clear untouched.
    /// </summary>
    static void RenameFirstAwaiterCacheField(IrFunction execution)
    {
        StoreField cache = execution.Body.Descendants
            .OfType<StoreField>()
            .First(store =>
                store.Field.Name.StartsWith("<>u__", StringComparison.Ordinal)
                && store.Value is LoadLocal);
        var replacement = new StoreField(
            cache.Field with { Name = "<>u__9" },
            (IrExpression?)cache.Instance?.Clone(),
            (IrExpression)cache.Value.Clone());
        replacement.SetSourceOffset(cache.SourceOffset);
        cache.ReplaceWith(replacement);
    }

    /// <summary>
    /// Re-declares the <c>SetException</c> callback on another builder type,
    /// optionally moving the machine's own <c>&lt;&gt;t__builder</c> field type
    /// with it so only the builder's assembly identity differs.
    /// </summary>
    static void RebindCompletionBuilder(
        IrFunction execution,
        TypeRef builder,
        bool rebindBuilderField)
    {
        Call setException = Assert.Single(
            execution.Body.Descendants.OfType<Call>(),
            call => call.Callee.Name == "SetException");
        var receiver = Assert.IsType<LoadFieldAddress>(setException.Arguments[0]);
        var rebound = new LoadFieldAddress(
            rebindBuilderField
                ? receiver.Field with { Type = builder }
                : receiver.Field,
            (IrExpression?)receiver.Instance?.Clone());
        rebound.SetSourceOffset(receiver.SourceOffset);

        var replacement = new Call(
            setException.Callee with { DeclaringType = builder },
            setException.IsVirtual,
            [
                rebound,
                .. setException.Arguments.Skip(1).Select(
                    argument => (IrExpression)argument.Clone()),
            ]);
        replacement.SetSourceOffset(setException.SourceOffset);
        setException.ReplaceWith(replacement);
    }

    /// <summary>
    /// Declares <c>SetResult</c> over a parameter type the builder's own result
    /// type is not, leaving the callee name, receiver, and argument shape — the
    /// facts a shape-only rule reads — unchanged.
    /// </summary>
    static void MistypeSetResultSignature(IrFunction execution)
    {
        Call setResult = Assert.Single(
            execution.Body.Descendants.OfType<Call>(),
            call => call.Callee.Name == "SetResult");
        var replacement = new Call(
            setResult.Callee with
            {
                ParameterTypes = [TypeRef.CoreLib("System", "String")],
            },
            setResult.IsVirtual,
            setResult.Arguments.Select(
                argument => (IrExpression)argument.Clone()));
        replacement.SetSourceOffset(setResult.SourceOffset);
        setResult.ReplaceWith(replacement);
    }

    static ClassicAsyncRequestSeed SeedOf(ClassicInverseRequest request)
        => new(
            request.DeclaredMethod!.Value,
            request.ExecutionMethod!.Value,
            request.Relationship!,
            request.AcquisitionGuard!);

    static void SwapKickoffParameterTransfers(IrFunction kickoff)
    {
        StoreField a = Assert.Single(
            kickoff.Body.Descendants.OfType<StoreField>(),
            store => store.Field.Name == "a"
                && store.Value is LoadArgument);
        StoreField b = Assert.Single(
            kickoff.Body.Descendants.OfType<StoreField>(),
            store => store.Field.Name == "b"
                && store.Value is LoadArgument);
        IrNode aArgument = a.Value.Clone();
        IrNode bArgument = b.Value.Clone();
        a.SetChild(a.HasInstance ? 1 : 0, bArgument);
        b.SetChild(b.HasInstance ? 1 : 0, aArgument);
    }

    static void NarrowRawHandlerTryOffset(
        IrFunction execution,
        HandlerKind kind)
    {
        HandlerRegion[] regions = [.. execution.Regions];
        int regionIndex = Array.FindIndex(
            regions,
            region => region.Kind == kind);
        Assert.InRange(regionIndex, 0, regions.Length - 1);
        Assert.Equal(
            regionIndex,
            Array.FindLastIndex(
                regions,
                region => region.Kind == kind));

        HandlerRegion region = regions[regionIndex];
        int nextBlockOffset = execution.Body.Blocks
            .Select(block => block.StartOffset)
            .Where(offset =>
                offset > region.TryOffset
                && offset < region.HandlerOffset)
            .Order()
            .First();
        int delta = nextBlockOffset - region.TryOffset;
        regions[regionIndex] = region with
        {
            TryOffset = nextBlockOffset,
            TryLength = region.TryLength - delta,
        };
        execution.Regions = [.. regions];
    }

    static void RetypeExecutionFieldAAsProbeAwaitable(IrFunction execution)
    {
        LoadField reference = Assert.Single(
            execution.Body.Descendants.OfType<LoadField>(),
            load => load.Field.Name == "a");
        TypeRef originalType = reference.Field.Type;
        TypeRef probeType = TypeRef.Definition(
            "ProbeAssembly",
            "ProbeNamespace",
            "ProbeAwaitable");

        foreach (IrNode node in execution.Body.Descendants.ToList())
        {
            switch (node)
            {
                case LoadField load
                    when load.Field.Name == "a"
                        && load.Field.Type.Equals(originalType):
                {
                    var replacement = new LoadField(
                        load.Field with { Type = probeType },
                        (IrExpression?)load.Instance?.Clone())
                    {
                        IsVolatile = load.IsVolatile,
                    };
                    replacement.SetSourceOffset(load.SourceOffset);
                    load.ReplaceWith(replacement);
                    break;
                }

                case StoreField store
                    when store.Field.Name == "a"
                        && store.Field.Type.Equals(originalType):
                {
                    var replacement = new StoreField(
                        store.Field with { Type = probeType },
                        (IrExpression?)store.Instance?.Clone(),
                        (IrExpression)store.Value.Clone())
                    {
                        IsVolatile = store.IsVolatile,
                    };
                    replacement.SetSourceOffset(store.SourceOffset);
                    store.ReplaceWith(replacement);
                    break;
                }

                case LoadFieldAddress address
                    when address.Field.Name == "a"
                        && address.Field.Type.Equals(originalType):
                {
                    var replacement = new LoadFieldAddress(
                        address.Field with { Type = probeType },
                        (IrExpression?)address.Instance?.Clone())
                    {
                        FieldRvaData = address.FieldRvaData,
                    };
                    replacement.SetSourceOffset(address.SourceOffset);
                    address.ReplaceWith(replacement);
                    break;
                }
            }
        }
    }

    [Fact]
    public void ClassicInverseCorrelationBindsOwnerIssuedRolesExactly()
    {
        using RequestScope scope = OpenRequest("TwoSequentialAwaits");
        ClassicInverseRequest request = scope.Request;
        MetadataMethodAddress declared = request.DeclaredMethod!.Value;
        MetadataMethodAddress execution = request.ExecutionMethod!.Value;

        AssertInvalidCorrelation(
            CopyRequest(request, declaredMethod: execution));
        AssertInvalidCorrelation(
            CopyRequest(request, executionMethod: declared));

        using MetadataSource source = OpenClassicStateMachinesFixture();
        var relationships = Assert.IsType<
            StateMachineRelationshipsResult.Available>(
                StateMachineRelationshipIndex.Create(source.Reader)
                    .Relationships);
        StateMachineRelationship wrongKind = relationships.Relationships.First(
            candidate => candidate.Kind == StateMachineClaimKind.Iterator);
        AssertInvalidCorrelation(
            CopyRequest(request, relationship: wrongKind));
    }

    [Fact]
    public void ClassicInverseLoopElementBindsItsExactStorage()
    {
        using RequestScope accepted = OpenRequest("AwaitInLoop");
        ClassicInversePlan plan = Reconstruct(accepted.Request);
        Assert.Contains(
            plan.SemanticRealizations,
            receipt => receipt.Rule
                == ClassicInverseRealizationRule.LoopElement);

        // The compiler's hoisted collection, loop index, and accumulator are
        // three distinct machine fields that share one generated name family.
        // Reading the array at the accumulator instead of the loop index is a
        // valid, well-typed body that iterates something else entirely.
        using RequestScope retargetedIndex = OpenMutatedRequest(
            "AwaitInLoop",
            execution => RetargetLoopElement(
                execution,
                retargetIndex: true));
        var indexDecline = Assert.IsType<ClassicInverseDecision.Decline>(
            ClassicInverseCore.Decide(retargetedIndex.Request));
        Assert.Equal(
            ClassicInverseDeclineReason.NoRecipeMatched,
            indexDecline.Reason);

        // Reading the un-hoisted source field instead of the proven hoist is
        // likewise a different storage identity, not a spelling difference.
        using RequestScope retargetedArray = OpenMutatedRequest(
            "AwaitInLoop",
            execution => RetargetLoopElement(
                execution,
                retargetIndex: false));
        var arrayDecline = Assert.IsType<ClassicInverseDecision.Decline>(
            ClassicInverseCore.Decide(retargetedArray.Request));
        Assert.Equal(
            ClassicInverseDeclineReason.NoRecipeMatched,
            arrayDecline.Reason);

        // The array-access effect is suppressed only for the element read the
        // recipe bound, so a retargeted read is not silently protocol either.
        (ClassicInversePlanningView planning,
            ClassicInverseCandidate candidate,
            ClassicInverseShellFacts shell) = Candidate(accepted.Request);
        LoadElement element = Assert.Single(
            planning.ExecutionBody.Body.Descendants.OfType<LoadElement>());
        var boundIndex = Assert.IsType<LoadField>(element.Index);
        FieldRef accumulator = AccumulatorField(planning.ExecutionBody);
        Assert.NotEqual(boundIndex.Field, accumulator);
        var replacement = new LoadField(
            accumulator,
            (IrExpression?)boundIndex.Instance?.Clone());
        replacement.SetSourceOffset(boundIndex.SourceOffset);
        boundIndex.ReplaceWith(replacement);

        var suppressionDecline = Assert.IsType<ClassicInverseDecision.Decline>(
            ClassicInverseAccountant.Account(
                accepted.Request,
                planning,
                candidate,
                shell,
                new ClassicInverseBudget()));
        Assert.Equal(
            ClassicInverseDeclineReason.UnrealizedSemanticEffect,
            suppressionDecline.Reason);
    }

    [Fact]
    public void ClassicInverseLoopBindsItsExactControlFlow()
    {
        using RequestScope accepted = OpenRequest("AwaitInLoop");
        Assert.IsType<ClassicInverseDecision.Reconstruct>(
            ClassicInverseCore.Decide(accepted.Request));

        int originalTarget = -1;
        using RequestScope retargeted = OpenMutatedRequest(
            "AwaitInLoop",
            execution =>
            {
                ConditionalBranch bound = Assert.Single(
                    execution.Body.Descendants.OfType<ConditionalBranch>(),
                    branch => branch.Condition is Comparison
                    {
                        Kind: ComparisonKind.LessThan,
                    }
                        && branch.Condition.Descendants.Any(
                            node => node is ArrayLength));
                StoreField advance = Assert.Single(
                    execution.Body.Descendants.OfType<StoreField>(),
                    store => store.Value is Binary
                    {
                        Kind: BinaryKind.Add,
                        Right: Pipeline.Constant { Value: 1 },
                    });
                Block advanceBlock = Assert.IsType<Block>(advance.Parent);
                originalTarget = bound.TargetOffset;
                var replacement = new ConditionalBranch(
                    (IrExpression)bound.Condition.Clone(),
                    advanceBlock.StartOffset,
                    bound.Origin);
                replacement.SetSourceOffset(bound.SourceOffset);
                bound.ReplaceWith(replacement);
            });

        var decline = Assert.IsType<ClassicInverseDecision.Decline>(
            ClassicInverseCore.Decide(retargeted.Request));
        Assert.Equal(
            ClassicInverseDeclineReason.NoRecipeMatched,
            decline.Reason);

        Action<IrFunction, ImmutableArray<IIrPass>> originalRunner =
            Assert.IsType<Action<IrFunction, ImmutableArray<IIrPass>>>(
                retargeted.Request.RunPasses);
        ClassicInverseRequest healedPlanning = CopyRequest(
            retargeted.Request,
            runPasses: (body, passes) =>
            {
                ConditionalBranch? bound = body.Body.Descendants
                    .OfType<ConditionalBranch>()
                    .SingleOrDefault(branch => branch.Condition is Comparison
                    {
                        Kind: ComparisonKind.LessThan,
                    }
                        && branch.Condition.Descendants.Any(
                            node => node is ArrayLength));
                if (bound is not null)
                {
                    var replacement = new ConditionalBranch(
                        (IrExpression)bound.Condition.Clone(),
                        originalTarget,
                        bound.Origin);
                    replacement.SetSourceOffset(bound.SourceOffset);
                    bound.ReplaceWith(replacement);
                }
                originalRunner(body, passes);
            });
        Assert.IsType<ClassicInverseDecision.Decline>(
            ClassicInverseCore.Decide(healedPlanning));
    }

    [Fact]
    public void ClassicInverseAwaitCompletionBindsItsExactControlFlow()
    {
        int originalTarget = -1;
        using RequestScope retargetedCompletion = OpenMutatedRequest(
            "AwaitInLoop",
            execution =>
            {
                ConditionalBranch completed = Assert.Single(
                    execution.Body.Descendants.OfType<ConditionalBranch>(),
                    branch => branch.Condition is Call
                        {
                            Callee.Name: "get_IsCompleted",
                        }
                        || branch.Condition.Descendants.OfType<Call>()
                            .Any(call =>
                                call.Callee.Name == "get_IsCompleted"));
                StoreField advance = Assert.Single(
                    execution.Body.Descendants.OfType<StoreField>(),
                    store => store.Value is Binary
                    {
                        Kind: BinaryKind.Add,
                        Right: Pipeline.Constant { Value: 1 },
                    });
                Block continuation = Assert.IsType<Block>(advance.Parent);
                var advanceBlock = new Block(advance.SourceOffset);
                IReadOnlyList<IrNode> statements =
                    continuation.DetachChildren();
                bool reachedAdvance = false;
                foreach (IrNode statement in statements)
                {
                    reachedAdvance |= ReferenceEquals(statement, advance);
                    (reachedAdvance ? advanceBlock : continuation).Add(
                        statement);
                }
                BlockContainer container =
                    Assert.IsType<BlockContainer>(continuation.Parent);
                IReadOnlyList<IrNode> blocks = container.DetachChildren();
                foreach (IrNode block in blocks)
                {
                    container.Add(Assert.IsType<Block>(block));
                    if (ReferenceEquals(block, continuation))
                        container.Add(advanceBlock);
                }
                originalTarget = completed.TargetOffset;
                Assert.NotEqual(
                    originalTarget,
                    advanceBlock.StartOffset);
                var replacement = new ConditionalBranch(
                    (IrExpression)completed.Condition.Clone(),
                    advanceBlock.StartOffset,
                    completed.Origin);
                replacement.SetSourceOffset(completed.SourceOffset);
                completed.ReplaceWith(replacement);
            });
        var decline = Assert.IsType<ClassicInverseDecision.Decline>(
            ClassicInverseCore.Decide(retargetedCompletion.Request));
        Assert.Equal(
            ClassicInverseDeclineReason.NoRecipeMatched,
            decline.Reason);

        Action<IrFunction, ImmutableArray<IIrPass>> originalRunner =
            Assert.IsType<Action<IrFunction, ImmutableArray<IIrPass>>>(
                retargetedCompletion.Request.RunPasses);
        bool repairedPlanningClone = false;
        ClassicInverseRequest healedPlanning = CopyRequest(
            retargetedCompletion.Request,
            runPasses: (body, passes) =>
            {
                ConditionalBranch? completed = body.Body.Descendants
                    .OfType<ConditionalBranch>().SingleOrDefault(
                    branch => branch.Condition is Call
                        {
                            Callee.Name: "get_IsCompleted",
                        });
                if (completed is not null)
                {
                    Assert.False(repairedPlanningClone);
                    repairedPlanningClone = true;
                    var replacement = new ConditionalBranch(
                        (IrExpression)completed.Condition.Clone(),
                        originalTarget,
                        completed.Origin);
                    replacement.SetSourceOffset(completed.SourceOffset);
                    completed.ReplaceWith(replacement);
                }
                originalRunner(body, passes);
            });
        var rawDecline = Assert.IsType<ClassicInverseDecision.Decline>(
            ClassicInverseCore.Decide(healedPlanning));
        Assert.True(repairedPlanningClone);
        Assert.Equal(
            ClassicInverseDeclineReason.NoRecipeMatched,
            rawDecline.Reason);
    }

    [Fact]
    public void ClassicInverseAwaitSuspensionBindsItsExactExit()
    {
        int originalTarget = -1;
        int leaveOffset = -1;
        using RequestScope retargetedExit = OpenMutatedRequest(
            "AwaitInLoop",
            execution =>
            {
                Call callback = Assert.Single(
                    execution.Body.Descendants.OfType<Call>(),
                    call => call.Callee.Name == "AwaitUnsafeOnCompleted");
                Block suspension = Assert.IsType<Block>(
                    callback.Parent?.Parent);
                Leave leave = Assert.IsType<Leave>(suspension.Children[^1]);
                Call getResult = Assert.Single(
                    execution.Body.Descendants.OfType<Call>(),
                    call => call.Callee.Name == "GetResult");
                Block continuation = Assert.IsType<Block>(
                    getResult.Parent?.Parent);
                originalTarget = leave.TargetOffset;
                leaveOffset = leave.SourceOffset;
                Assert.NotEqual(originalTarget, continuation.StartOffset);
                var replacement = new Leave(continuation.StartOffset);
                replacement.SetSourceOffset(leaveOffset);
                leave.ReplaceWith(replacement);
            });
        Assert.IsType<ClassicInverseDecision.Decline>(
            ClassicInverseCore.Decide(retargetedExit.Request));

        Action<IrFunction, ImmutableArray<IIrPass>> originalRunner =
            Assert.IsType<Action<IrFunction, ImmutableArray<IIrPass>>>(
                retargetedExit.Request.RunPasses);
        bool repairedPlanningClone = false;
        ClassicInverseRequest healedPlanning = CopyRequest(
            retargetedExit.Request,
            runPasses: (body, passes) =>
            {
                Leave? leave = body.Body.Descendants.OfType<Leave>()
                    .SingleOrDefault(candidate =>
                        candidate.SourceOffset == leaveOffset
                        && candidate.TargetOffset != originalTarget);
                if (leave is not null)
                {
                    repairedPlanningClone = true;
                    var replacement = new Leave(originalTarget);
                    replacement.SetSourceOffset(leaveOffset);
                    leave.ReplaceWith(replacement);
                }
                originalRunner(body, passes);
            });
        Assert.IsType<ClassicInverseDecision.Decline>(
            ClassicInverseCore.Decide(healedPlanning));
        Assert.True(repairedPlanningClone);
    }

    [Fact]
    public void ClassicInverseLoopIndexWritesBindExactRoles()
    {
        using RequestScope accepted = OpenRequest("AwaitInLoop");
        Assert.IsType<ClassicInverseDecision.Reconstruct>(
            ClassicInverseCore.Decide(accepted.Request));

        int duplicateOffset = -1;
        using RequestScope duplicateReset = OpenMutatedRequest(
            "AwaitInLoop",
            execution =>
            {
                StoreField advance = Assert.Single(
                    execution.Body.Descendants.OfType<StoreField>(),
                    store => store.Value is Binary
                    {
                        Kind: BinaryKind.Add,
                        Right: Pipeline.Constant { Value: 1 },
                    });
                StoreField reset = Assert.Single(
                    execution.Body.Descendants.OfType<StoreField>(),
                    store => store.Field == advance.Field
                        && store.Value is Pipeline.Constant { Value: 0 });
                Block advanceBlock = Assert.IsType<Block>(advance.Parent);
                IReadOnlyList<IrNode> statements =
                    advanceBlock.DetachChildren();
                foreach (IrNode statement in statements)
                {
                    advanceBlock.Add(statement);
                    if (!ReferenceEquals(statement, advance))
                        continue;
                    var duplicate = (StoreField)reset.Clone();
                    duplicateOffset = execution.Body.Descendants
                        .Max(node => node.SourceOffset) + 1;
                    duplicate.SetSourceOffset(duplicateOffset);
                    advanceBlock.Add(duplicate);
                }
            });
        var decline = Assert.IsType<ClassicInverseDecision.Decline>(
            ClassicInverseCore.Decide(duplicateReset.Request));
        Assert.Equal(
            ClassicInverseDeclineReason.NoRecipeMatched,
            decline.Reason);

        Action<IrFunction, ImmutableArray<IIrPass>> originalRunner =
            Assert.IsType<Action<IrFunction, ImmutableArray<IIrPass>>>(
                duplicateReset.Request.RunPasses);
        bool repairedPlanningClone = false;
        ClassicInverseRequest healedPlanning = CopyRequest(
            duplicateReset.Request,
            runPasses: (body, passes) =>
            {
                IrNode? duplicate = body.Body.Descendants.SingleOrDefault(
                    node => node.SourceOffset == duplicateOffset);
                if (duplicate is not null)
                {
                    Assert.False(repairedPlanningClone);
                    repairedPlanningClone = true;
                    duplicate.Detach();
                }
                originalRunner(body, passes);
            });
        var rawDecline = Assert.IsType<ClassicInverseDecision.Decline>(
            ClassicInverseCore.Decide(healedPlanning));
        Assert.True(repairedPlanningClone);
        Assert.Equal(
            ClassicInverseDeclineReason.NoRecipeMatched,
            rawDecline.Reason);

        int indirectResetOffset = -1;
        using RequestScope indirectReset = OpenMutatedRequest(
            "AwaitInLoop",
            execution =>
            {
                StoreField advance = Assert.Single(
                    execution.Body.Descendants.OfType<StoreField>(),
                    store => store.Value is Binary
                    {
                        Kind: BinaryKind.Add,
                        Right: Pipeline.Constant { Value: 1 },
                    });
                Block advanceBlock = Assert.IsType<Block>(advance.Parent);
                var reset = new InitObject(
                    advance.Field.Type,
                    new LoadFieldAddress(
                        advance.Field,
                        (IrExpression?)advance.Instance?.Clone()));
                indirectResetOffset = execution.Body.Descendants
                    .Max(node => node.SourceOffset) + 1;
                reset.SetSourceOffset(indirectResetOffset);
                advanceBlock.Add(reset);
            });
        Assert.IsType<ClassicInverseDecision.Decline>(
            ClassicInverseCore.Decide(indirectReset.Request));

        originalRunner =
            Assert.IsType<Action<IrFunction, ImmutableArray<IIrPass>>>(
                indirectReset.Request.RunPasses);
        repairedPlanningClone = false;
        healedPlanning = CopyRequest(
            indirectReset.Request,
            runPasses: (body, passes) =>
            {
                IrNode? reset = body.Body.Descendants.SingleOrDefault(
                    node => node.SourceOffset == indirectResetOffset);
                if (reset is not null)
                {
                    Assert.False(repairedPlanningClone);
                    repairedPlanningClone = true;
                    reset.Detach();
                }
                originalRunner(body, passes);
            });
        Assert.IsType<ClassicInverseDecision.Decline>(
            ClassicInverseCore.Decide(healedPlanning));
        Assert.True(repairedPlanningClone);
    }

    [Fact]
    public void ClassicInverseLoopRawRolesCannotBeHealedByPlanning()
    {
        foreach (string role in new[] { "bound", "initializer", "advance" })
        {
            int changedOffset = -1;
            using RequestScope changed = OpenMutatedRequest(
                "AwaitInLoop",
                execution =>
                {
                    StoreField advance = Assert.Single(
                        execution.Body.Descendants.OfType<StoreField>(),
                        store => store.Value is Binary
                        {
                            Kind: BinaryKind.Add,
                            Right: Pipeline.Constant { Value: 1 },
                        });
                    switch (role)
                    {
                        case "bound":
                            var comparison = Assert.Single(
                                execution.Body.Descendants.OfType<Comparison>(),
                                candidate => candidate.Kind
                                    == ComparisonKind.LessThan
                                    && candidate.Right.Descendants
                                        .Prepend(candidate.Right)
                                        .OfType<ArrayLength>()
                                        .Any());
                            changedOffset = comparison.SourceOffset;
                            var changedBound = new Comparison(
                                ComparisonKind.LessThanOrEqual,
                                comparison.IsUnsigned,
                                (IrExpression)comparison.Left.Clone(),
                                (IrExpression)comparison.Right.Clone());
                            changedBound.SetSourceOffset(changedOffset);
                            comparison.ReplaceWith(changedBound);
                            break;

                        case "initializer":
                            StoreField initializer = Assert.Single(
                                execution.Body.Descendants.OfType<StoreField>(),
                                store => store.Field == advance.Field
                                    && store.Value is Pipeline.Constant
                                        { Value: 0 });
                            changedOffset = initializer.SourceOffset;
                            var one = new Pipeline.Constant(
                                1,
                                initializer.Field.Type);
                            one.SetSourceOffset(initializer.Value.SourceOffset);
                            initializer.Value.ReplaceWith(one);
                            break;

                        case "advance":
                            changedOffset = advance.SourceOffset;
                            var addition = Assert.IsType<Binary>(advance.Value);
                            var two = new Pipeline.Constant(
                                2,
                                advance.Field.Type);
                            two.SetSourceOffset(addition.Right.SourceOffset);
                            addition.Right.ReplaceWith(two);
                            break;
                    }
                });
            Assert.IsType<ClassicInverseDecision.Decline>(
                ClassicInverseCore.Decide(changed.Request));

            Action<IrFunction, ImmutableArray<IIrPass>> originalRunner =
                Assert.IsType<Action<IrFunction, ImmutableArray<IIrPass>>>(
                    changed.Request.RunPasses);
            bool repairedPlanningClone = false;
            ClassicInverseRequest healedPlanning = CopyRequest(
                changed.Request,
                runPasses: (body, passes) =>
                {
                    switch (role)
                    {
                        case "bound":
                            Comparison? comparison = body.Body.Descendants
                                .OfType<Comparison>()
                                .SingleOrDefault(candidate =>
                                    candidate.SourceOffset == changedOffset
                                    && candidate.Kind
                                        == ComparisonKind.LessThanOrEqual);
                            if (comparison is not null)
                            {
                                repairedPlanningClone = true;
                                var restored = new Comparison(
                                    ComparisonKind.LessThan,
                                    comparison.IsUnsigned,
                                    (IrExpression)comparison.Left.Clone(),
                                    (IrExpression)comparison.Right.Clone());
                                restored.SetSourceOffset(changedOffset);
                                comparison.ReplaceWith(restored);
                            }
                            break;

                        case "initializer":
                            StoreField? initializer = body.Body.Descendants
                                .OfType<StoreField>()
                                .SingleOrDefault(store =>
                                    store.SourceOffset == changedOffset
                                    && store.Value is Pipeline.Constant
                                        { Value: 1 });
                            if (initializer is not null)
                            {
                                repairedPlanningClone = true;
                                var zero = new Pipeline.Constant(
                                    0,
                                    initializer.Field.Type);
                                zero.SetSourceOffset(
                                    initializer.Value.SourceOffset);
                                initializer.Value.ReplaceWith(zero);
                            }
                            break;

                        case "advance":
                            StoreField? advance = body.Body.Descendants
                                .OfType<StoreField>()
                                .SingleOrDefault(store =>
                                    store.SourceOffset == changedOffset
                                    && store.Value is Binary
                                    {
                                        Kind: BinaryKind.Add,
                                        Right: Pipeline.Constant { Value: 2 },
                                    });
                            if (advance is not null)
                            {
                                repairedPlanningClone = true;
                                var addition =
                                    Assert.IsType<Binary>(advance.Value);
                                var one = new Pipeline.Constant(
                                    1,
                                    advance.Field.Type);
                                one.SetSourceOffset(
                                    addition.Right.SourceOffset);
                                addition.Right.ReplaceWith(one);
                            }
                            break;
                    }
                    originalRunner(body, passes);
                });
            Assert.IsType<ClassicInverseDecision.Decline>(
                ClassicInverseCore.Decide(healedPlanning));
            Assert.True(repairedPlanningClone, role);
        }
    }

    /// <summary>
    /// Points the compiler's loop element read at another valid state-machine
    /// field of the right type: the accumulator instead of the loop index, or
    /// the un-hoisted source array instead of the proven hoist. Every other
    /// shape — the hoist, the bound test, the index advance, the await, and the
    /// accumulate — is left exactly as the compiler emitted it.
    /// </summary>
    static void RetargetLoopElement(IrFunction execution, bool retargetIndex)
    {
        LoadElement element = Assert.Single(
            execution.Body.Descendants.OfType<LoadElement>());
        if (retargetIndex)
        {
            var index = Assert.IsType<LoadField>(element.Index);
            var replacement = new LoadField(
                AccumulatorField(execution),
                (IrExpression?)index.Instance?.Clone());
            replacement.SetSourceOffset(index.SourceOffset);
            index.ReplaceWith(replacement);
            return;
        }

        var array = Assert.IsType<LoadField>(element.Array);
        StoreField hoist = Assert.Single(
            execution.Body.Descendants.OfType<StoreField>(),
            store => store.Field == array.Field
                && store.Value is LoadField);
        var source = Assert.IsType<LoadField>(hoist.Value);
        var replacementArray = new LoadField(
            source.Field,
            (IrExpression?)array.Instance?.Clone());
        replacementArray.SetSourceOffset(array.SourceOffset);
        array.ReplaceWith(replacementArray);
    }

    /// <summary>
    /// The machine field the loop folds into — located by the compiler's own
    /// accumulate shape, which the product may not use as an authorization.
    /// </summary>
    static FieldRef AccumulatorField(IrFunction execution)
    {
        Binary accumulate = Assert.Single(
            execution.Body.Descendants.OfType<Binary>(),
            binary => binary.Kind == BinaryKind.Add
                && binary.Left is LoadField
                && binary.Right is LoadLocal);
        return Assert.IsType<LoadField>(accumulate.Left).Field;
    }

    [Fact]
    public void ClassicInverseAwaitResultBindsItsExactAwaiterMember()
    {
        using RequestScope accepted = OpenRequest("AwaitValue");
        ClassicInversePlan plan = Reconstruct(accepted.Request);
        Assert.Contains(
            plan.SemanticRealizations.SelectMany(
                receipt => receipt.SourceEffects),
            effect => effect == "await");

        Call getResult = Assert.Single(
            accepted.Request.ExecutionBody.Body.Descendants.OfType<Call>(),
            call => call.Callee.Name == "GetResult");
        TypeRef awaiter = getResult.Callee.DeclaringType;

        // A valid static helper taking the very same awaiter by reference: the
        // callee name, the argument count, and the awaiter slot are unchanged,
        // so only exact member identity separates it from the compiler's own
        // instance TaskAwaiter<int>.GetResult().
        using RequestScope helper = OpenMutatedRequest(
            "AwaitValue",
            execution => RebindAwaiterGetResult(
                execution,
                callee => callee with
                {
                    DeclaringType = TypeRef.Definition(
                        "Planted",
                        "ILInspector.Probes",
                        "Probe"),
                    ParameterTypes = [TypeRef.ByRef(awaiter)],
                    HasThis = false,
                }));
        var helperDecline = Assert.IsType<ClassicInverseDecision.Decline>(
            ClassicInverseCore.Decide(helper.Request));
        Assert.Equal(
            ClassicInverseDeclineReason.NoRecipeMatched,
            helperDecline.Reason);

        // Still instance and still parameterless, but declared on a lookalike
        // awaiter the suspension never bound.
        using RequestScope lookalike = OpenMutatedRequest(
            "AwaitValue",
            execution => RebindAwaiterGetResult(
                execution,
                callee => callee with
                {
                    DeclaringType = TypeRef.Definition(
                        "Planted",
                        "System.Runtime.CompilerServices",
                        "TaskAwaiter`1"),
                }));
        var lookalikeDecline = Assert.IsType<ClassicInverseDecision.Decline>(
            ClassicInverseCore.Decide(lookalike.Request));
        Assert.Equal(
            ClassicInverseDeclineReason.NoRecipeMatched,
            lookalikeDecline.Reason);
    }

    [Fact]
    public void ClassicInverseAwaitBindsItsExactGetAwaiterMember()
    {
        using RequestScope accepted = OpenRequest("AwaitValue");
        Assert.IsType<ClassicInverseDecision.Reconstruct>(
            ClassicInverseCore.Decide(accepted.Request));

        using RequestScope helper = OpenMutatedRequest(
            "AwaitValue",
            execution =>
            {
                Call getAwaiter = Assert.Single(
                    execution.Body.Descendants.OfType<Call>(),
                    call => call.Callee.Name == "GetAwaiter");
                IrExpression receiver = getAwaiter.Arguments.Single();
                var replacement = new Call(
                    new MethodRef(
                        TypeRef.Definition(
                            "Planted",
                            "ILInspector.Probes",
                            "AwaitProbe"),
                        "GetAwaiter",
                        getAwaiter.Callee.ReturnType,
                        [Assert.IsType<TypeRef>(receiver.ResultType)],
                        HasThis: false),
                    isVirtual: false,
                    [(IrExpression)receiver.Clone()]);
                replacement.SetSourceOffset(getAwaiter.SourceOffset);
                getAwaiter.ReplaceWith(replacement);
            });

        Assert.IsType<ClassicInverseDecision.Decline>(
            ClassicInverseCore.Decide(helper.Request));

        using RequestScope direct = OpenMutatedRequest(
            "AwaitValue",
            execution =>
            {
                Call getAwaiter = Assert.Single(
                    execution.Body.Descendants.OfType<Call>(),
                    call => call.Callee.Name == "GetAwaiter");
                Assert.True(getAwaiter.IsVirtual);
                var replacement = new Call(
                    getAwaiter.Callee,
                    isVirtual: false,
                    getAwaiter.Arguments.Select(
                        argument => (IrExpression)argument.Clone()));
                replacement.SetSourceOffset(getAwaiter.SourceOffset);
                getAwaiter.ReplaceWith(replacement);
            });
        Assert.IsType<ClassicInverseDecision.Decline>(
            ClassicInverseCore.Decide(direct.Request));
    }

    static void RebindAwaiterGetResult(
        IrFunction execution,
        Func<MethodRef, MethodRef> rebind)
    {
        Call getResult = Assert.Single(
            execution.Body.Descendants.OfType<Call>(),
            call => call.Callee.Name == "GetResult");
        var replacement = new Call(
            rebind(getResult.Callee),
            getResult.IsVirtual,
            getResult.Arguments.Select(
                argument => (IrExpression)argument.Clone()));
        replacement.SetSourceOffset(getResult.SourceOffset);
        getResult.ReplaceWith(replacement);
    }

    [Fact]
    public void ClassicInverseWithSetterBindsItsExactDispatch()
    {
        using RequestScope accepted =
            OpenRequest("SequentialWithRealizedWithExpression");
        MethodRef setter = Assert.Single(
            accepted.Request.ExecutionBody.Body.Descendants.OfType<Call>()
                .Where(call => call.Callee.Name.StartsWith(
                    "set_",
                    StringComparison.Ordinal))
                .Select(call => call.Callee));
        Assert.Contains(
            Reconstruct(accepted.Request).SemanticRealizations.SelectMany(
                receipt => receipt.SourceEffects),
            effect => effect
                == $"call:{ClassicInverseTypedIdentity.Method(setter)}:virt");

        // 'receiver with { P = v }' re-emits a virtual setter call, so a direct
        // setter store has no with-expression spelling: raising it would restore
        // dispatch the input did not have.
        using RequestScope direct = OpenMutatedRequest(
            "SequentialWithRealizedWithExpression",
            MakeWithSetterDirect);
        Assert.IsType<ClassicInverseDecision.Decline>(
            ClassicInverseCore.Decide(direct.Request));

        ClassicInversePlanningView planning =
            ClassicInversePlanningView.Derive(direct.Request);
        Assert.DoesNotContain(
            planning.ExecutionBody.Body.Descendants,
            node => node is WithExpression);
        Assert.Contains(
            planning.ExecutionBody.Body.Descendants,
            node => node is StoreProperty { IsVirtual: false });
    }

    [Fact]
    public void ClassicInverseWithCloneBindsItsExactDispatch()
    {
        using RequestScope accepted =
            OpenRequest("SequentialWithRealizedWithExpression");
        Call clone = Assert.Single(
            accepted.Request.ExecutionBody.Body.Descendants.OfType<Call>(),
            call => call.Callee.Name == "<Clone>$");
        ClassicInversePlan plan = Reconstruct(accepted.Request);
        Assert.Contains(
            plan.SemanticRealizations.SelectMany(
                receipt => receipt.SourceEffects),
            effect => effect
                == ClassicInverseConsumedMembers.Effect(
                    clone.Callee,
                    clone.IsVirtual));
    }

    /// <summary>
    /// Turns the compiler's <c>callvirt</c> record-setter store into a valid
    /// direct call, leaving the callee, the receiver, the clone, and the value
    /// exactly as they were.
    /// </summary>
    static void MakeWithSetterDirect(IrFunction execution)
    {
        Call setter = Assert.Single(
            execution.Body.Descendants.OfType<Call>(),
            call => call.Callee.Name.StartsWith("set_", StringComparison.Ordinal));
        Assert.True(setter.IsVirtual);
        var replacement = new Call(
            setter.Callee,
            isVirtual: false,
            setter.Arguments.Select(
                argument => (IrExpression)argument.Clone()))
        {
            ConstrainedTo = setter.ConstrainedTo,
            ExtensionSyntaxConflict = setter.ExtensionSyntaxConflict,
        };
        replacement.SetSourceOffset(setter.SourceOffset);
        setter.ReplaceWith(replacement);
    }

    [Fact]
    public void ClassicInverseConsumedMemberAccountingChargesEveryLookup()
    {
        // Raw-effect accounting must decide, per call in the unmodified import,
        // whether the planning view still carries it as a consumed initializer
        // member. Answering by rescanning the planning tree per call buys
        // quadratic work at a linear charge, so the answer comes from one index
        // that charges for every element it touches.
        foreach (string methodName in new[]
        {
            "SequentialWithRealizedInitializer",
            "SequentialWithRealizedWithExpression",
        })
        {
            using RequestScope scope = OpenRequest(methodName);
            ClassicInversePlanningView planning =
                ClassicInversePlanningView.Derive(scope.Request);
            IrNode planningRoot = planning.ExecutionBody.Body;
            int planningNodes = planningRoot.Descendants.Count() + 1;
            int entries = planningRoot.Descendants.Prepend(planningRoot)
                .Sum(node => node switch
                {
                    ObjectInitializerExpression initializer =>
                        initializer.Entries.Count,
                    WithExpression with => with.Entries.Count,
                    InitializerBlock block => block.Entries.Count,
                    _ => 0,
                });
            int clones = planningRoot.Descendants.Prepend(planningRoot)
                .Count(node => node is WithExpression
                {
                    ConsumedCloneMethod: not null,
                });
            Assert.True(entries > 0);

            // Construction charges exactly once per node, entry, and consumed
            // clone.
            var indexBudget = new ClassicInverseBudget();
            ClassicInverseConsumedMembers index = Assert.IsType<
                ClassicInverseConsumedMembers>(
                    ClassicInverseConsumedMembers.Build(
                        planningRoot,
                        indexBudget));
            Assert.Equal(
                planningNodes + entries + clones,
                indexBudget.Consumed);

            // Every question charges one unit, whether or not it is a hit.
            var lookupBudget = new ClassicInverseBudget();
            string absent = ClassicInverseConsumedMembers.Effect(
                new MethodRef(
                    TypeRef.Definition("Planted", "N", "Absent"),
                    "set_Missing",
                    TypeRef.CoreLib("System", "Void"),
                    [TypeRef.CoreLib("System", "Int32")],
                    HasThis: true),
                isVirtual: true);
            Assert.False(index.Contains(absent, lookupBudget));
            Assert.Equal(1, lookupBudget.Consumed);

            // Those charges are load-bearing: one unit short, the index refuses
            // to answer at all rather than answering from a partial scan.
            Assert.Null(ClassicInverseConsumedMembers.Build(
                planningRoot,
                new ClassicInverseBudget(
                    planningNodes + entries + clones - 1)));

            // Through the product path the same shortfall stays a visible
            // failure, never a decline or a partial proof, and total planning
            // work stays proportional to the two bodies.
            var budget = new ClassicInverseBudget();
            Assert.IsType<ClassicInverseDecision.Reconstruct>(
                ClassicInverseCore.Decide(scope.Request, budget));
            int rawNodes =
                scope.Request.ExecutionBody.Body.Descendants.Count() + 1
                + scope.Request.KickoffBody.Body.Descendants.Count() + 1;
            Assert.InRange(
                budget.Consumed,
                rawNodes + planningNodes + entries,
                10 * (rawNodes + planningNodes));

            var failure = Assert.IsType<ClassicInverseDecision.Failed>(
                ClassicInverseCore.Decide(
                    scope.Request,
                    new ClassicInverseBudget(budget.Consumed - 1)));
            Assert.Equal(
                ClassicInverseFailureKind.BudgetExhausted,
                failure.Failure.Kind);

            Assert.IsType<ClassicInverseDecision.Reconstruct>(
                ClassicInverseCore.Decide(
                    scope.Request,
                    new ClassicInverseBudget(budget.Consumed)));
        }
    }

    [Fact]
    public void ClassicInverseRecipeScanChargesEveryNodeVisit()
    {
        using RequestScope scope = OpenRequest("TwoSequentialAwaits");
        ClassicInversePlanningView planning =
            ClassicInversePlanningView.Derive(scope.Request);
        ClassicInverseShellFacts shell = ClassicInverseShellFacts.Derive(
            planning.ExecutionBody,
            scope.Request.ExecutionBody,
            new ClassicInverseBudget());
        Assert.Null(shell.Protocol.Failure);

        int planningNodes = planning.ExecutionBody.Body.Descendants.Count() + 1;
        var budget = new ClassicInverseBudget();
        ClassicInverseCandidate candidate = Assert.Single(
            ClassicInverseRecipes.Match(
                scope.Request,
                planning,
                shell,
                budget));

        Assert.Equal("classic-sequential-await-void", candidate.Recipe);
        Assert.InRange(budget.Consumed, planningNodes, 8 * planningNodes);
    }

    [Fact]
    public void ClassicInverseAcceptedPopulationIsMeasured()
    {
        using MetadataSource source = OpenClassicFixture();
        var accepted = ImmutableArray.CreateBuilder<string>();
        var rejectedExpected = ImmutableArray.CreateBuilder<string>();

        foreach (string methodName in ClassicKickoffMethods(source))
        {
            IrFunction function = Assert.IsType<IrFunction>(
                IrImporter.Import(source, FixtureType, methodName));
            IrPasses.Run(
                function,
                IrPasses.Default,
                PassContext.ForImport(
                    method => IrImporter.Import(source, method)));
            DecompilerResult result = CSharpPrinter.Print(function);
            if (result.Fidelity == DecompilationFidelity.Full)
            {
                accepted.Add(methodName);
            }
            else if (s_acceptedPopulation.Contains(methodName))
            {
                rejectedExpected.Add(
                    $"{methodName}: "
                        + string.Join(
                            " | ",
                            function.Diagnostics.Select(
                                diagnostic => diagnostic.Message)));
            }
        }

        ImmutableArray<string> actual =
            accepted.ToImmutable().Sort(StringComparer.Ordinal);
        Assert.True(
            s_acceptedPopulation.SequenceEqual(actual),
            $"Expected: {string.Join(", ", s_acceptedPopulation)}"
                + $"{Environment.NewLine}Actual: {string.Join(", ", actual)}"
                + $"{Environment.NewLine}Unexpected declines: "
                + string.Join(
                    Environment.NewLine,
                    rejectedExpected));
    }

    static ClassicInversePlan Reconstruct(ClassicInverseRequest request)
        => Assert.IsType<ClassicInverseDecision.Reconstruct>(
            ClassicInverseCore.Decide(request)).Plan;

    static (
        ClassicInversePlanningView Planning,
        ClassicInverseCandidate Candidate,
        ClassicInverseShellFacts Shell)
        Candidate(ClassicInverseRequest request)
    {
        var budget = new ClassicInverseBudget();
        ClassicInversePlanningView planning =
            ClassicInversePlanningView.Derive(request);
        ClassicInverseShellFacts shell =
            ClassicInverseShellFacts.Derive(
                planning.ExecutionBody,
                request.ExecutionBody,
                budget);
        ClassicInverseCandidate candidate = Assert.Single(
            ClassicInverseRecipes.Match(request, planning, shell, budget));
        return (planning, candidate, shell);
    }

    static ClassicInverseRequest CopyRequest(
        ClassicInverseRequest request,
        int? stateMachineLocal = null,
        ImmutableHashSet<int>? executionImportOffsets = null,
        MetadataMethodAddress? declaredMethod = null,
        MetadataMethodAddress? executionMethod = null,
        StateMachineRelationship? relationship = null,
        Action<IrFunction, ImmutableArray<IIrPass>>? runPasses = null)
        => new(
            declaredMethod ?? request.DeclaredMethod,
            executionMethod ?? request.ExecutionMethod,
            relationship ?? request.Relationship,
            request.AcquisitionGuard,
            request.KickoffBody,
            request.ExecutionBody,
            stateMachineLocal ?? request.StateMachineLocal,
            request.KickoffSourceOffset,
            executionImportOffsets ?? request.ExecutionImportOffsets,
            runPasses ?? request.RunPasses);

    static void AssertInvalidCorrelation(ClassicInverseRequest request)
    {
        var failed = Assert.IsType<ClassicInverseDecision.Failed>(
            ClassicInverseCore.Decide(request));
        Assert.Equal(
            ClassicInverseFailureKind.InvalidCorrelation,
            failed.Failure.Kind);
    }

    static int CountCoveredNodes(
        IrNode root,
        IEnumerable<ClassicInversePhysicalRegion> regions)
    {
        var nodes = new HashSet<IrNode>(ReferenceEqualityComparer.Instance);
        foreach (ClassicInversePhysicalRegion region in regions)
        {
            IrNode node = region.Path.Aggregate(
                root,
                static (current, slot) => current.Children[slot]);
            Assert.True(nodes.Add(node));
            if (!region.OwnsSubtree)
                continue;
            foreach (IrNode descendant in node.Descendants)
                Assert.True(nodes.Add(descendant));
        }
        return nodes.Count;
    }

    static RequestScope OpenRequest(string methodName)
        => OpenRequest(OpenClassicFixture(), methodName, ownsSource: true);

    /// <summary>
    /// Opens a request whose unmodified execution snapshot is mutated first, so
    /// the mutation reaches both the import snapshot and the planning view the
    /// core derives from it — exactly what a differently lowered compiler body
    /// would present at the boundary.
    /// </summary>
    static RequestScope OpenMutatedRequest(
        string methodName,
        Action<IrFunction> mutateExecution)
        => OpenRequest(
            OpenClassicFixture(),
            methodName,
            ownsSource: true,
            mutateExecution);

    static RequestScope OpenRequest(
        MetadataSource source,
        string methodName)
        => OpenRequest(source, methodName, ownsSource: false);

    static RequestScope OpenRequest(
        MetadataSource source,
        string methodName,
        bool ownsSource,
        Action<IrFunction>? mutateExecution = null)
    {
        IrFunction kickoff = Assert.IsType<IrFunction>(
            IrImporter.Import(source, FixtureType, methodName));
        var seed = Assert.IsType<
            ClassicAsyncRequestAdapterResult.RequestAvailable>(
                kickoff.ClassicAsyncRequest).Request;

        IrFunction planningKickoff = (IrFunction)kickoff.Clone();
        IrPasses.Run(
            planningKickoff,
            [.. IrPasses.Default.TakeWhile(
                pass => pass is not ClassicAsyncReconstructionPass)],
            PassContext.ForImport(
                method => IrImporter.Import(source, method)));

        IrFunction execution = Assert.IsType<IrFunction>(
            IrImporter.Import(source, seed.ExecutionMethod.Handle));
        mutateExecution?.Invoke(execution);
        ImmutableHashSet<int> importOffsets =
            ClassicInverseRequest.OffsetsOf(execution);

        StoreField builder = Assert.Single(
            planningKickoff.Body.Descendants.OfType<StoreField>(),
            store => store.Field.Name == "<>t__builder"
                && store.Instance is LoadLocalAddress);
        int stateMachineLocal =
            Assert.IsType<LoadLocalAddress>(builder.Instance).Index;
        var request = ClassicInverseCore.Request(
            kickoff,
            stateMachineLocal,
            builder.SourceOffset,
            execution,
            importOffsets,
            seed,
            (body, passes) => IrPasses.Run(
                body,
                passes,
                PassContext.ForImport(
                    method => IrImporter.Import(source, method))));
        return new RequestScope(
            ownsSource ? source : null,
            request);
    }

    static IEnumerable<string> ClassicKickoffMethods(
        MetadataSource source)
    {
        MetadataReader reader = source.Reader;
        TypeDefinition type = reader.TypeDefinitions
            .Select(reader.GetTypeDefinition)
            .Single(candidate =>
                reader.StringComparer.Equals(
                    candidate.Namespace,
                    "ILInspector.Decompiler.Fixtures.ClassicAsync")
                && reader.StringComparer.Equals(
                    candidate.Name,
                    "AsyncFixtures"));

        foreach (MethodDefinitionHandle handle in type.GetMethods())
        {
            IrFunction? function = IrImporter.Import(source, handle);
            if (function?.ClassicAsyncRequest
                is ClassicAsyncRequestAdapterResult.RequestAvailable)
            {
                yield return reader.GetString(
                    reader.GetMethodDefinition(handle).Name);
            }
        }
    }

    static MetadataSource OpenClassicFixture()
    {
        string configuration =
            new DirectoryInfo(AppContext.BaseDirectory).Name;
        string path = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "ILInspector.Decompiler.Fixtures.ClassicAsync",
            configuration,
            "ILInspector.Decompiler.Fixtures.ClassicAsync.dll"));
        return MetadataSource.Open(path);
    }

    static MetadataSource OpenClassicStateMachinesFixture()
    {
        string configuration =
            new DirectoryInfo(AppContext.BaseDirectory).Name;
        string path = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "ILInspector.Decompiler.Fixtures.ClassicStateMachines",
            configuration,
            "ILInspector.Decompiler.Fixtures.ClassicStateMachines.dll"));
        return MetadataSource.Open(path);
    }

    sealed class RequestScope(
        MetadataSource? source,
        ClassicInverseRequest request) : IDisposable
    {
        internal ClassicInverseRequest Request { get; } = request;

        public void Dispose() => source?.Dispose();
    }
}
