using ILInspector.Analysis.Planning;

namespace ILInspector.Analysis.Tests;

public sealed partial class MethodDefinitionRequestSetTests
{
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
                Assert.Equal(
                    expected.DeclaredSourceSuppressesOpportunities(
                        method.MetadataToken),
                    actual.DeclaredSourceSuppressesOpportunities(
                        method.MetadataToken));
            }
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
}
