using ILInspector.Analysis.ClassicAsyncFixtures;
using ILInspector.Analysis.Planning;

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
    public void LibraryCallGraphProducer_MatchesLegacyMethodEvidence()
    {
        string path = typeof(ExactMethodAnalysisFixture).Assembly.Location;
        MethodDefinitionSourceAssociation association =
            Association(
                LibraryCallGraphProducer.Instance,
                ProducerTerminal.Rows,
                MethodDefinitionSourceBreadth
                    .AllDefinitions
                    .IncludeGeneratedExecutionBodies());
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
        Assert.Equal(
            0,
            receipt.Coverage.MethodAnalysis.DeclaredSource.Queries);
        ProducerLayerParticipation methodAnalysis =
            Assert.Single(
                sourceResult.WorkReceipt
                    .For(LibraryCallGraphProducer.Instance)
                    .Layers,
                layer => layer.Layer
                    == nameof(
                        MethodDefinitionLayers.MethodAnalysis));
        Assert.Equal(
            receipt.BodiesAcquired,
            methodAnalysis.Acquired);
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
                    .AllDefinitions
                    .IncludeGeneratedExecutionBodies());
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
        Assert.True(receipt.Coverage.CallTargets.Constructed);
        Assert.True(actual.HasFullMethodEvidenceScope);
    }

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
