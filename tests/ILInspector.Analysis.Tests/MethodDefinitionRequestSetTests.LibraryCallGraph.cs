using System.Reflection.Metadata.Ecma335;

using ILInspector.Analysis.ClassicAsyncFixtures;
using ILInspector.Analysis.Planning;
using ILInspector.Metadata;

namespace ILInspector.Analysis.Tests;

public sealed partial class MethodDefinitionRequestSetTests
{
    [Fact]
    public void LibraryRootPathCallGraphProducer_MatchesFullCallEvidence()
    {
        string path = typeof(ExactMethodAnalysisFixture).Assembly.Location;
        LibraryCallGraphProducerResult expected =
            ExecuteLibraryCallGraph(
                path,
                LibraryCallGraphProducer.Instance,
                out _);
        LibraryRootPathCallGraphProducerResult actual =
            ExecuteLibraryCallGraph(
                path,
                LibraryCallGraphProducer
                    .LibraryRootPathCallGraphProducer.Instance,
                out MethodDefinitionSourceRequestResult sourceResult);

        Assert.Equal(expected.DeclaredMethods, actual.DeclaredMethods);
        Assert.Equal(expected.Methods, actual.Methods);
        Assert.Equal(
            expected.DirectCalls.Length,
            actual.DirectCalls.Length);
        for (int i = 0; i < expected.DirectCalls.Length; i++)
        {
            DirectCall expectedCall = expected.DirectCalls[i];
            DirectCall actualCall = actual.DirectCalls[i];
            Assert.Equal(expectedCall.Caller, actualCall.Caller);
            Assert.Equal(
                expectedCall.EvidenceMethod,
                actualCall.EvidenceMethod);
            Assert.Equal(expectedCall.Callee, actualCall.Callee);
            Assert.Equal(expectedCall.ILOffset, actualCall.ILOffset);
            Assert.Equal(
                expectedCall.OperandToken,
                actualCall.OperandToken);
            Assert.Equal(
                expectedCall.CalleeDefinitionToken,
                actualCall.CalleeDefinitionToken);
            Assert.Equal(expectedCall.Kind, actualCall.Kind);
            Assert.Equal(expectedCall.Opcode, actualCall.Opcode);
            Assert.Equal(
                expectedCall.ReturnAddress,
                actualCall.ReturnAddress);
        }
        Assert.Equal(expected.DeclaredSources, actual.DeclaredSources);
        Assert.Equal(
            expected.DeclaredSourceSuppressedMethodTokens,
            actual.DeclaredSourceSuppressedMethodTokens);
        Assert.Equal(expected.Diagnostics, actual.Diagnostics);
        MethodDefinitionSourceReceipt receipt =
            sourceResult.SourceReceipt;
        Assert.Equal(
            0,
            receipt.Coverage.Calls.LocalSignatureReads);
        Assert.Equal(
            0,
            receipt.Coverage.Calls.FieldOperandReads);
        Assert.False(receipt.Coverage.CallTargets.Constructed);
        Assert.False(receipt.Coverage.MethodAnalysis.Constructed);
    }

    [Fact]
    public void LibraryCallGraphProducer_HeadUsesInterpretedSource()
    {
        const int limit = 2;
        string path = typeof(ExactMethodAnalysisFixture).Assembly.Location;
        ProducerRequest head =
            ProducerRequest.Head(
                LibraryCallGraphProducer.Instance,
                limit);
        WorkDescription work =
            Assert.IsType<ProducerPlanResult.Accepted>(
                    ProducerPlanner.Plan([head]))
                .Description;
        MethodDefinitionSourceRequest<LibraryCallGraphProducerResult>
            request =
                MethodDefinitionSourceRequest<
                    LibraryCallGraphProducerResult>.Create(
                    QueryRequest(ProducerTerminal.Rows),
                    work,
                    LibraryCallGraphProducer.Instance,
                    MethodDefinitionSourceBreadth.AllDefinitions);
        MethodDefinitionSourceAssociation association =
            MethodDefinitionSourceAssociation.Create(request);

        MethodDefinitionSourceRequestSetExecution execution =
            ExecuteAt(AcceptedPlan([association]), path);
        ProducerResult<LibraryCallGraphProducerResult> producerResult =
            ResultOf<LibraryCallGraphProducerResult>(
                execution,
                association);
        Assert.True(
            producerResult.HasValue,
            producerResult.Failure?.Message
                ?? producerResult.Critical?.Message
                ?? producerResult.FailedPrerequisite
                ?? producerResult.Outcome.ToString());
        Assert.Equal(ProducerOutcome.Stopped, producerResult.Outcome);
        LibraryCallGraphProducerResult produced =
            Assert.IsType<LibraryCallGraphProducerResult>(
                producerResult.Value);
        MethodDefinitionSourceReceipt receipt =
            execution.ResultOf(association).SourceReceipt;

        Assert.Equal(
            [
                MetadataTokens.GetToken(
                    MetadataTokens.MethodDefinitionHandle(1)),
                MetadataTokens.GetToken(
                    MetadataTokens.MethodDefinitionHandle(2)),
            ],
            produced.DeclaredMethods.Select(
                static method => method.MetadataToken));
        Assert.Equal(
            MethodDefinitionSourceCompletion.Satisfied,
            receipt.Completion);
        Assert.Equal(limit, receipt.DefinitionsVisited);
        Assert.True(receipt.Coverage.MethodAnalysis.Constructed);
    }

    [Fact]
    public void
        LibraryCallGraphProducer_FiniteTerminalLimitUsesInterpretedSource()
    {
        string path = typeof(ExactMethodAnalysisFixture).Assembly.Location;
        var limits = new MethodDefinitionTerminalWorkLimits(
            maximumBodies: 1,
            maximumEncodedIlBytes: long.MaxValue);
        MethodDefinitionSourceAssociation association =
            Association(
                LibraryCallGraphProducer.Instance,
                ProducerTerminal.Rows,
                MethodDefinitionSourceBreadth.AllDefinitions,
                limits);

        MethodDefinitionSourceRequestSetExecution execution =
            ExecuteAt(AcceptedPlan([association]), path);

        Assert.Equal(
            ProducerOutcome.Failed,
            ResultOf<LibraryCallGraphProducerResult>(
                execution,
                association).Outcome);
        MethodDefinitionSourceReceipt receipt =
            execution.ResultOf(association).SourceReceipt;
        Assert.Equal(
            MethodDefinitionSourceCompletion.SourceIncomplete,
            receipt.Completion);
        Assert.Contains(
            "terminal physical-body limit",
            Assert.IsType<MethodDefinitionSourceFailure>(
                    receipt.SourceFailure)
                .Message,
            StringComparison.Ordinal);
        Assert.Equal(
            1,
            receipt.Coverage.TerminalWork.BodiesAdmitted);
        Assert.Equal(
            MethodDefinitionTerminalWorkLimitKind.Bodies,
            receipt.Coverage.TerminalWork.ReachedLimit);
        Assert.True(receipt.Coverage.MethodAnalysis.Constructed);
    }

    [Fact]
    public void LibraryCallGraphProducer_ExactMethodUsesInterpretedSource()
    {
        string path = typeof(ExactMethodAnalysisFixture).Assembly.Location;
        int token = typeof(ExactMethodAnalysisFixture)
            .GetMethod(nameof(ExactMethodAnalysisFixture.Target))!
            .MetadataToken;
        MethodDefinitionSourceAssociation association =
            Association(
                LibraryCallGraphProducer.Instance,
                ProducerTerminal.Rows,
                MethodDefinitionSourceBreadth.ExactMethods(
                    MetadataTokens.MethodDefinitionHandle(
                        token & 0x00FF_FFFF)));

        MethodDefinitionSourceRequestSetExecution execution =
            ExecuteAt(AcceptedPlan([association]), path);
        LibraryCallGraphProducerResult produced =
            Assert.IsType<LibraryCallGraphProducerResult>(
                ResultOf<LibraryCallGraphProducerResult>(
                    execution,
                    association).Value);
        MethodDefinitionSourceReceipt receipt =
            execution.ResultOf(association).SourceReceipt;

        Assert.Equal(
            token,
            Assert.Single(produced.DeclaredMethods).MetadataToken);
        Assert.Equal(1, receipt.DefinitionsVisited);
        Assert.True(receipt.Coverage.MethodAnalysis.Constructed);
    }

    [Fact]
    public void LibraryCallGraphProducer_MatchesLegacyMethodEvidence()
    {
        string path = typeof(ExactMethodAnalysisFixture).Assembly.Location;
        MethodDefinitionSourceAssociation association =
            Association(
                LibraryCallGraphProducer.Instance,
                ProducerTerminal.Rows,
                MethodDefinitionSourceBreadth
                    .AllDefinitions);
        MethodDefinitionSourceRequestSetExecution sourceExecution =
            ExecuteAt(AcceptedPlan([association]), path);
        ProducerResult<LibraryCallGraphProducerResult> producerResult =
            ResultOf<LibraryCallGraphProducerResult>(
                sourceExecution,
                association);
        Assert.True(
            producerResult.HasValue,
            producerResult.Failure?.Message
                ?? producerResult.Critical?.Message
                ?? $"{producerResult.Outcome}: "
                    + producerResult.FailedPrerequisite);
        LibraryCallGraphProducerResult produced =
            Assert.IsType<LibraryCallGraphProducerResult>(
                producerResult.Value);
        MethodDefinitionSourceRequestResult sourceResult =
            sourceExecution.ResultOf(association);
        LibraryCallGraphAnalysisResult actual =
            LibraryCallGraphAnalysisResult.FromProducer(
                path,
                produced,
                sourceResult.SourceReceipt,
                sourceResult.WorkReceipt);

        LibraryBodyAnalysisExecution legacy =
            LibraryBodyAnalysisService.ExecutePath(
                path,
                LibraryBodyAnalysisRequest.Create(
                    LibraryBodyAnalysisFeatures.MethodEvidence));
        LibraryCallGraphAnalysisResult expected = legacy.CallGraph;
        try
        {
            Assert.Equal(
                expected.ModuleIdentity,
                actual.ModuleIdentity);
            Assert.Equal(
                expected.DeclaredMethods,
                actual.DeclaredMethods);
            Assert.Equal(expected.Methods, actual.Methods);
            Assert.Equal(expected.DirectCalls, actual.DirectCalls);
            Assert.Equal(
                legacy.Leverage.Top(int.MaxValue),
                actual.TopLeverage(int.MaxValue));
            Assert.Equal(
                legacy.Safety.Evidence,
                produced.UnsafeEvidence);
            Assert.Equal(
                expected.Diagnostics,
                actual.Diagnostics);
            Assert.True(
                expected.MethodSignals.Keys.ToHashSet().SetEquals(
                    actual.MethodSignals.Keys),
                $"Expected-only: {DescribeSignals(expected, actual)}"
                + $"{Environment.NewLine}Actual-only: "
                + DescribeSignals(actual, expected));
            foreach (int token in expected.MethodSignals.Keys)
            {
                MethodSignals expectedSignals =
                    expected.MethodSignals[token];
                MethodSignals actualSignals =
                    actual.MethodSignals[token];
                Assert.Equal(
                    expectedSignals with
                    {
                        EvidenceOffsets = [],
                        ExceptionTypeNames = [],
                    },
                    actualSignals with
                    {
                        EvidenceOffsets = [],
                        ExceptionTypeNames = [],
                    });
                Assert.Equal(
                    expectedSignals.Evidence,
                    actualSignals.Evidence);
                Assert.Equal(
                    expectedSignals.ExceptionTypes,
                    actualSignals.ExceptionTypes);
            }
            foreach (MethodIdentity method in actual.Methods)
            {
                Assert.Equal(
                    expected.ResolveDeclaredMethod(method),
                    actual.ResolveDeclaredMethod(method));
                Assert.True(
                    expected.DeclaredSourceSuppressesOpportunities(
                        method.MetadataToken)
                        == actual.DeclaredSourceSuppressesOpportunities(
                            method.MetadataToken),
                    method.ToString());
            }

            MethodIdentity localFaultFactory =
                Assert.Single(
                    actual.Methods,
                    static method =>
                        method.DeclaringType.Name
                            == nameof(CallGraphExceptionFixture)
                        && method.Name
                            == nameof(
                                CallGraphExceptionFixture
                                    .ConstructUnsuffixedLocalException));
            Assert.DoesNotContain(
                nameof(CallGraphLocalFault),
                actual.MethodSignals[
                        localFaultFactory.MetadataToken]
                    .ExceptionTypes);
        }
        finally
        {
            expected.ReleaseCaches();
            actual.ReleaseCaches();
        }

        MethodDefinitionSourceReceipt receipt =
            Assert.IsType<MethodDefinitionSourceReceipt>(
                actual.MethodSourceReceipt);
        Assert.Same(sourceResult.SourceReceipt, receipt);
        Assert.Same(sourceResult.WorkReceipt, actual.ProducerReceipt);
        Assert.Equal(
            MethodDefinitionSourceBreadthKind.AllDefinitions,
            receipt.Breadth.Kind);
        Assert.Equal(
            MethodDefinitionSourceCompletion.Exhausted,
            receipt.Completion);
        Assert.Equal(
            receipt.DefinitionsVisited,
            actual.DeclaredMethods.Length);
        Assert.Equal(
            receipt.BodiesAcquired,
            actual.Methods.Length);
        Assert.True(
            receipt.DefinitionsVisited
                >= LibraryMethodAnalysisRunner.ParallelMethodThreshold);
        Assert.False(
            receipt.Coverage.MethodAnalysis.Constructed);
        ProducerLayerParticipation moduleLookup =
            Assert.Single(
                sourceResult.WorkReceipt
                    .For(LibraryCallGraphProducer.Instance)
                    .Layers,
                layer => layer.Layer
                    == nameof(
                        MethodDefinitionLayers.ModuleLookup));
        Assert.Equal(
            receipt.DefinitionsVisited,
            moduleLookup.Acquired);
        ProducerLayerParticipation bodies =
            Assert.Single(
                sourceResult.WorkReceipt
                    .For(LibraryCallGraphProducer.Instance)
                    .Layers,
                layer => layer.Layer
                    == nameof(
                        MethodDefinitionLayers.Body));
        Assert.Equal(
            receipt.BodiesAcquired,
            bodies.Acquired);
        Assert.True(actual.HasFullMethodEvidenceScope);

        static string DescribeSignals(
            LibraryCallGraphAnalysisResult left,
            LibraryCallGraphAnalysisResult right) =>
            string.Join(
                ", ",
                left.MethodSignals.Keys
                    .Except(right.MethodSignals.Keys)
                    .Select(token =>
                        left.DeclaredMethods.First(
                            method =>
                                method.MetadataToken == token)));
    }

    [Fact]
    public void LibraryCallGraphProducer_UsesSourceOwnedStateMachineKind()
    {
        string path =
            typeof(ClassicAsyncSiblingFixture).Assembly.Location;
        MethodDefinitionSourceAssociation association =
            Association(
                LibraryCallGraphProducer.Instance,
                ProducerTerminal.Rows,
                MethodDefinitionSourceBreadth
                    .AllDefinitions);
        MethodDefinitionSourceRequestSetExecution execution =
            ExecuteAt(AcceptedPlan([association]), path);
        LibraryCallGraphProducerResult produced =
            Assert.IsType<LibraryCallGraphProducerResult>(
                ResultOf<LibraryCallGraphProducerResult>(
                    execution,
                    association).Value);
        MethodDefinitionSourceRequestResult sourceResult =
            execution.ResultOf(association);
        LibraryCallGraphAnalysisResult graph =
            LibraryCallGraphAnalysisResult.FromProducer(
                path,
                produced,
                sourceResult.SourceReceipt,
                sourceResult.WorkReceipt);
        try
        {
            MethodIdentity customTaskOwner =
                Assert.Single(
                    graph.DeclaredMethods,
                    static method =>
                        method.DeclaringType.Name
                            == nameof(ClassicAsyncSiblingFixture)
                        && method.Name
                            == nameof(
                                ClassicAsyncSiblingFixture
                                    .UsesCustomAsyncBuilder));
            DirectCall customTaskCompletion =
                Assert.Single(
                    graph.DirectCalls,
                    call =>
                        call.Caller == customTaskOwner
                        && call.Callee.Name == "SetResult");
            Assert.NotEqual(
                customTaskCompletion.Caller,
                customTaskCompletion.EvidenceMethod);

            DirectCall generatedCodeCall =
                Assert.Single(
                    graph.DirectCalls,
                    call =>
                        call.Caller.Name
                            == nameof(
                                ClassicAsyncSiblingFixture
                                    .GeneratedCodeAsyncOwner)
                        && call.Callee.Name
                            == nameof(
                                ClassicAsyncSiblingFixture
                                    .GeneratedRead));
            Assert.NotEqual(
                generatedCodeCall.Caller,
                generatedCodeCall.EvidenceMethod);
            Assert.True(
                graph.DeclaredSourceSuppressesOpportunities(
                    generatedCodeCall.EvidenceMethod.MetadataToken));

            MethodIdentity iteratorOwner =
                Assert.Single(
                    graph.DeclaredMethods,
                    static method =>
                        method.DeclaringType.Name
                            == nameof(ClassicAsyncSiblingFixture)
                        && method.Name
                            == "ScopedIteratorAsyncLocalAllocationOwner");
            DirectCall iteratorCall =
                Assert.Single(
                    graph.DirectCalls,
                    call =>
                        call.Callee.Name.Contains(
                            "BuildAsync",
                            StringComparison.Ordinal)
                        && call.EvidenceMethod.Name == "MoveNext"
                        && call.EvidenceMethod.DeclaringType.Name
                            .Contains(
                                iteratorOwner.Name,
                                StringComparison.Ordinal));
            Assert.Equal(
                iteratorCall.EvidenceMethod,
                iteratorCall.Caller);
            Assert.NotEqual(iteratorOwner, iteratorCall.Caller);
        }
        finally
        {
            graph.ReleaseCaches();
        }
    }

    [Fact]
    public void LibraryMethodLeverageProducer_MatchesLegacyRanking()
    {
        string path = typeof(ExactMethodAnalysisFixture).Assembly.Location;
        LibraryMethodLeverageProducerResult produced =
            ExecuteLibraryCallGraph(
                path,
                LibraryMethodLeverageProducer.Instance,
                out MethodDefinitionSourceRequestResult sourceResult);
        LibraryCallGraphAnalysisResult actual =
            LibraryCallGraphAnalysisResult.FromLeverageProducer(
                path,
                produced,
                sourceResult.SourceReceipt,
                sourceResult.WorkReceipt);
        LibraryBodyAnalysisExecution legacy =
            LibraryBodyAnalysisService.ExecutePath(
                path,
                LibraryBodyAnalysisRequest.Create(
                    LibraryBodyAnalysisFeatures.MethodEvidence));
        try
        {
            Assert.Equal(
                legacy.Leverage.Top(int.MaxValue),
                actual.TopLeverage(int.MaxValue));
            Assert.Equal(
                legacy.CallGraph.Diagnostics,
                actual.Diagnostics);
        }
        finally
        {
            legacy.CallGraph.ReleaseCaches();
            actual.ReleaseCaches();
        }

        MethodDefinitionSourceReceipt receipt =
            Assert.IsType<MethodDefinitionSourceReceipt>(
                actual.MethodSourceReceipt);
        Assert.Same(sourceResult.SourceReceipt, receipt);
        Assert.Same(sourceResult.WorkReceipt, actual.ProducerReceipt);
        Assert.Equal(
            MethodDefinitionSourceBreadthKind.AllDefinitions,
            receipt.Breadth.Kind);
        Assert.Equal(
            MethodDefinitionSourceCompletion.Exhausted,
            receipt.Completion);
        Assert.Equal(0, receipt.Coverage.Calls.LocalSignatureReads);
        Assert.False(receipt.Coverage.MethodAnalysis.Constructed);
        Assert.False(receipt.Coverage.CallTargets.Constructed);
        Assert.True(actual.HasFullMethodEvidenceScope);
    }

    [Fact]
    public void LibraryMethodLeverageProducer_ParallelMatchesSerial()
    {
        string path = typeof(ExactMethodAnalysisFixture).Assembly.Location;
        LeverageExecution serial =
            ExecuteLibraryMethodLeverage(
                path,
                parallelExecution: null);
        var parallelExecution = new TestParallelExecution(4);
        LeverageExecution parallel =
            ExecuteLibraryMethodLeverage(
                path,
                parallelExecution);

        Assert.Equal(
            serial.Result.DeclaredMethods,
            parallel.Result.DeclaredMethods);
        Assert.Equal(serial.Result.Methods, parallel.Result.Methods);
        Assert.Equal(
            serial.Result.DirectCalls,
            parallel.Result.DirectCalls);
        Assert.Equal(
            serial.Result.DeclaredSources.OrderBy(
                static pair => pair.Key),
            parallel.Result.DeclaredSources.OrderBy(
                static pair => pair.Key));
        Assert.Equal(
            serial.Result.DeclaredSourceSuppressedMethodTokens
                .Order(),
            parallel.Result.DeclaredSourceSuppressedMethodTokens
                .Order());
        Assert.Equal(
            serial.Result.Diagnostics,
            parallel.Result.Diagnostics);
        Assert.True(parallelExecution.PartitionCount >= 2);

        Assert.Equal(
            serial.SourceReceipt.Completion,
            parallel.SourceReceipt.Completion);
        Assert.Equal(
            serial.SourceReceipt.Breadth.Kind,
            parallel.SourceReceipt.Breadth.Kind);
        Assert.Equal(
            serial.SourceReceipt.Coverage.DefinitionsExamined.Ranges,
            parallel.SourceReceipt.Coverage.DefinitionsExamined.Ranges);
        Assert.Equal(
            serial.SourceReceipt.Coverage.MethodsSelected.Ranges,
            parallel.SourceReceipt.Coverage.MethodsSelected.Ranges);
        Assert.Equal(
            serial.SourceReceipt.Coverage.BodiesAttempted.Ranges,
            parallel.SourceReceipt.Coverage.BodiesAttempted.Ranges);
        Assert.Equal(
            serial.SourceReceipt.Coverage.BodiesAcquired.Ranges,
            parallel.SourceReceipt.Coverage.BodiesAcquired.Ranges);
        AssertGeneratedExpansionEqual(
            serial.SourceReceipt.Coverage.GeneratedExpansion,
            parallel.SourceReceipt.Coverage.GeneratedExpansion);

        ProducerParticipation serialWork =
            serial.WorkReceipt.For(
                LibraryMethodLeverageProducer.Instance);
        ProducerParticipation parallelWork =
            parallel.WorkReceipt.For(
                LibraryMethodLeverageProducer.Instance);
        Assert.Equal(serialWork.Outcome, parallelWork.Outcome);
        Assert.Equal(
            serialWork.UnitsAttempted,
            parallelWork.UnitsAttempted);
        Assert.Equal(
            serialWork.UnitsCompleted,
            parallelWork.UnitsCompleted);
        Assert.Equal(serialWork.UnitsFailed, parallelWork.UnitsFailed);
        Assert.Equal(serialWork.Layers, parallelWork.Layers);
    }

    [Fact]
    public void LibraryMethodLeverageProducer_LimitedAndExactStaySerial()
    {
        string path = typeof(ExactMethodAnalysisFixture).Assembly.Location;
        var headExecution = new TestParallelExecution(4);
        ExecuteLibraryMethodLeverage(
            path,
            headExecution,
            ProducerRequest.Head(
                LibraryMethodLeverageProducer.Instance,
                2));
        Assert.Equal(0, headExecution.PartitionCount);

        var exactExecution = new TestParallelExecution(4);
        ExecuteLibraryMethodLeverage(
            path,
            exactExecution,
            producerRequest: null,
            breadth: MethodDefinitionSourceBreadth.ExactMethods(
                MetadataTokens.MethodDefinitionHandle(1)));
        Assert.Equal(0, exactExecution.PartitionCount);
    }

    static void AssertGeneratedExpansionEqual(
        MethodDefinitionGeneratedExpansionCoverage expected,
        MethodDefinitionGeneratedExpansionCoverage actual)
    {
        Assert.Equal(
            expected.CandidateDefinitionsExamined.Ranges,
            actual.CandidateDefinitionsExamined.Ranges);
        Assert.Equal(
            expected.ProbeBodiesAttempted.Ranges,
            actual.ProbeBodiesAttempted.Ranges);
        Assert.Equal(
            expected.ProbeEncodedIlBytes,
            actual.ProbeEncodedIlBytes);
        Assert.Equal(expected.RelationshipNodes, actual.RelationshipNodes);
        Assert.Equal(expected.Origins, actual.Origins);
        Assert.Equal(
            expected.AttributedOutsideRoles,
            actual.AttributedOutsideRoles);
        Assert.Equal(
            expected.UnauthenticatedCandidates,
            actual.UnauthenticatedCandidates);
        Assert.Equal(expected.ReachedLimit, actual.ReachedLimit);
        Assert.Equal(expected.ReachedAtToken, actual.ReachedAtToken);
        Assert.Equal(
            expected.TraversalReferenceBodies,
            actual.TraversalReferenceBodies);
        Assert.Equal(
            expected.TraversalReferenceBodiesAcquired,
            actual.TraversalReferenceBodiesAcquired);
        Assert.Equal(
            expected.TraversalReferenceEncodedIlBytes,
            actual.TraversalReferenceEncodedIlBytes);
    }

    static LeverageExecution ExecuteLibraryMethodLeverage(
        string path,
        MethodDefinitionParallelExecution? parallelExecution,
        ProducerRequest? producerRequest = null,
        MethodDefinitionSourceBreadth? breadth = null)
    {
        WorkDescription work =
            Assert.IsType<ProducerPlanResult.Accepted>(
                    ProducerPlanner.Plan(
                        [
                            producerRequest
                                ?? new ProducerRequest(
                                    LibraryMethodLeverageProducer.Instance,
                                    ProducerTerminal.Rows),
                        ]))
                .Description;
        MethodDefinitionSourceRequest<
            LibraryMethodLeverageProducerResult> request =
                MethodDefinitionSourceRequest<
                    LibraryMethodLeverageProducerResult>.Create(
                        work,
                        LibraryMethodLeverageProducer.Instance,
                        breadth
                            ?? MethodDefinitionSourceBreadth
                                .AllDefinitions
                                .IncludeGeneratedExecutionBodies());
        AssemblyAnalysisOperation<
            LibraryMethodLeverageProducerResult> operation =
                AssemblyAnalysisOperation<
                    LibraryMethodLeverageProducerResult>.Create(
                        path,
                        request.AsAssemblyAnalysisSource());
        using AssemblyInspectionSession session =
            AssemblyInspectionSession.Open(path);
        AssemblyAnalysisExecution<
            LibraryMethodLeverageProducerResult> execution =
                Assert.IsType<
                    AssemblyAnalysisServiceResult<
                        LibraryMethodLeverageProducerResult>.Completed>(
                            session.SnapshotOperation(
                                operation,
                                access =>
                                    AssemblyAnalysisService.Instance.Execute(
                                        operation,
                                        access,
                                        parallelExecution)))
                    .Execution;
        ProducerResult<
            LibraryMethodLeverageProducerResult> result =
                execution.ResultOf(
                    LibraryMethodLeverageProducer.Instance);
        Assert.True(
            result.HasValue,
            result.Failure?.Message
                ?? result.Critical?.Message
                ?? result.Outcome.ToString());
        return new(
            result.Value!,
            execution.MethodDefinitionReceipt(),
            execution.WorkReceipt);
    }

    sealed class TestParallelExecution(int maximumConcurrency)
        : MethodDefinitionParallelExecution(maximumConcurrency)
    {
        public int PartitionCount { get; private set; }

        public override void ExecutePartitions(
            int partitionCount,
            Action<int> executePartition)
        {
            PartitionCount = partitionCount;
            Parallel.For(
                0,
                partitionCount,
                new ParallelOptions
                {
                    MaxDegreeOfParallelism = MaximumConcurrency,
                },
                executePartition);
        }
    }

    sealed record LeverageExecution(
        LibraryMethodLeverageProducerResult Result,
        MethodDefinitionSourceReceipt SourceReceipt,
        WorkReceipt WorkReceipt);

    static TResult ExecuteLibraryCallGraph<TResult>(
        string path,
        ProducerDeclaration<TResult> producer,
        out MethodDefinitionSourceRequestResult sourceResult)
    {
        MethodDefinitionSourceAssociation association =
            Association(
                producer,
                ProducerTerminal.Rows,
                MethodDefinitionSourceBreadth
                    .AllDefinitions
                    .IncludeGeneratedExecutionBodies());
        MethodDefinitionSourceRequestSetExecution execution =
            ExecuteAt(AcceptedPlan([association]), path);
        sourceResult = execution.ResultOf(association);
        ProducerResult<TResult> producerResult =
            ResultOf<TResult>(
                execution,
                association);
        Assert.True(
            producerResult.HasValue,
            producerResult.Failure?.Message
                ?? producerResult.Critical?.Message
                ?? $"{producerResult.Outcome}: "
                    + producerResult.FailedPrerequisite);
        return Assert.IsType<TResult>(producerResult.Value);
    }
}

public sealed class CallGraphLocalFault
    : InvalidOperationException;

public static class CallGraphExceptionFixture
{
    public static object ConstructUnsuffixedLocalException() =>
        new CallGraphLocalFault();
}
