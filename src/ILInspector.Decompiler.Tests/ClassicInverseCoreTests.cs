using System.Collections.Immutable;
using System.Reflection.Metadata;

using ILInspector.Decompiler.Pipeline;
using ILInspector.Metadata;
using ILInspector.MetadataPrimitives;

namespace ILInspector.Decompiler.Tests;

[Trait("Area", "Pass")]
public sealed class ClassicInverseCoreTests
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

        Assert.Contains(
            Reconstruct(initializer.Request).SemanticRealizations
                .SelectMany(receipt => receipt.SourceEffects),
            effect => effect == "store:Box.Value");
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
        ClassicInversePlanningView planning =
            ClassicInversePlanningView.Derive(request);
        ClassicInverseShellFacts shell =
            ClassicInverseShellFacts.Derive(planning.ExecutionBody);
        ClassicInverseCandidate candidate = Assert.Single(
            ClassicInverseRecipes.Match(
                planning,
                shell,
                new ClassicInverseBudget()));
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

    static RequestScope OpenRequest(
        MetadataSource source,
        string methodName)
        => OpenRequest(source, methodName, ownsSource: false);

    static RequestScope OpenRequest(
        MetadataSource source,
        string methodName,
        bool ownsSource)
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
