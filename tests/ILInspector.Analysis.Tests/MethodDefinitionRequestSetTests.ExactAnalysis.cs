using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

using ILInspector.Analysis.Planning;
using ILInspector.Metadata;

namespace ILInspector.Analysis.Tests;

public sealed partial class MethodDefinitionRequestSetTests
{
    [Fact]
    public void ExactMethodAnalysis_UsesOnePhysicalBodyAndMatchesLegacyFacts()
    {
        string path = typeof(ExactMethodAnalysisFixture).Assembly.Location;
        int token = typeof(ExactMethodAnalysisFixture)
            .GetMethod(nameof(ExactMethodAnalysisFixture.Target))!
            .MetadataToken;
        MethodDefinitionHandle method =
            MetadataTokens.MethodDefinitionHandle(token & 0x00FF_FFFF);
        MethodDefinitionSourceAssociation association =
            Association(
                ExactMethodAnalysisProducer.Instance,
                ProducerTerminal.Rows,
                MethodDefinitionSourceBreadth.ExactMethods(method),
                new(
                    maximumBodies: 1,
                    maximumEncodedIlBytes: int.MaxValue));

        MethodDefinitionSourceRequestSetExecution sourceExecution =
            ExecuteAt(AcceptedPlan([association]), path);
        ProducerResult<ExactMethodAnalysisProducerResult> sourceResult =
            ResultOf<ExactMethodAnalysisProducerResult>(
                sourceExecution,
                association);
        ExactMethodAnalysisBody actual =
            Assert.Single(sourceResult.Value!.Bodies);

        LibraryBodyAnalysisExecution legacyExecution =
            LibraryBodyAnalysisService.ExecutePath(
                path,
                LibraryBodyAnalysisRequest.Create(
                    LibraryBodyAnalysisFeatures
                        .OptimizationOpportunities,
                    new HashSet<int> { token }));
        LibraryCallGraphAnalysisResult graph =
            legacyExecution.CallGraph;
        try
        {
            MethodIdentity expectedMethod = Assert.Single(
                graph.Methods,
                candidate => candidate.MetadataToken == token);
            legacyExecution.Safety.Occurrences.TryGetValue(
                token,
                out ImmutableArray<UnsafetyOccurrence> expectedOccurrences);
            legacyExecution.Safety.GetEvidenceByMember().TryGetValue(
                token,
                out ImmutableArray<UnsafeEvidence> expectedEvidence);
            using PdbContext metadata =
                PdbContext.OpenMetadataOnly(path);
            IReadOnlyList<MethodExceptionRegionInfo> expectedRegions =
                metadata.ResolveExceptionRegions(
                    token,
                    out string? exceptionRegionError);

            Assert.Equal(expectedMethod, actual.Method);
            Assert.Contains(
                actual.SafetyEvidence,
                evidence =>
                    evidence.Reason == "Unsafe call"
                    && evidence.Detail.Contains(
                        "get_Pointer",
                        StringComparison.Ordinal));
            Assert.Contains(
                actual.SafetyEvidence,
                evidence =>
                    evidence.Kind == "calli");
            Assert.Equal(
                EmptyIfDefault(expectedOccurrences),
                actual.UnsafeOccurrences);
            Assert.Equal(
                EmptyIfDefault(expectedEvidence),
                actual.SafetyEvidence);
            Assert.Null(exceptionRegionError);
            Assert.Null(actual.ExceptionRegionError);
            Assert.Equal(expectedRegions, actual.ExceptionRegions);
            Assert.Equal(
                legacyExecution.Receipt.Diagnostics.Where(
                    diagnostic => diagnostic.MethodToken == token),
                actual.Diagnostics);
        }
        finally
        {
            graph.ReleaseCaches();
        }

        MethodDefinitionSourceReceipt receipt =
            sourceExecution.ResultOf(association).SourceReceipt;
        Assert.Equal(1, receipt.DefinitionsVisited);
        Assert.Equal(1, receipt.BodiesAcquired);
        Assert.Equal(0, receipt.ModuleLookups);
        Assert.Equal(1, receipt.Coverage.MethodsSelected.Count);
        Assert.True(receipt.Coverage.MethodsSelected.Contains(method));
        Assert.Equal(1, receipt.Coverage.BodiesAcquired.Count);
        Assert.True(receipt.Coverage.BodiesAcquired.Contains(method));
        Assert.True(
            receipt.DeclaredLayers.HasFlag(
                MethodDefinitionLayers.SemanticIdentity));
        Assert.True(
            receipt.DeclaredLayers.HasFlag(
                MethodDefinitionLayers.MemorySafetyContracts));
        Assert.True(
            receipt.DeclaredLayers.HasFlag(
                MethodDefinitionLayers.Calls));
        Assert.Equal(1, receipt.Coverage.Calls.CallReads);
        Assert.Equal(1, receipt.Coverage.Calls.LocalSignatureReads);
        Assert.True(receipt.Coverage.CallTargets.Queries > 0);
    }

    static ImmutableArray<T> EmptyIfDefault<T>(
        ImmutableArray<T> values) =>
        values.IsDefault ? [] : values;
}

public static unsafe class ExactMethodAnalysisFixture
{
    public static int Target(int value)
    {
        try
        {
            int* buffer = stackalloc int[1];
            buffer[0] = value;
            int* pointer = Pointer;
            if (pointer != null)
                buffer[0] += *pointer;
            delegate*<int, int> transform = &Callee;
            return transform(buffer[0]);
        }
        catch (InvalidOperationException)
        {
            return -1;
        }
    }

    public static int* Pointer => null;

    public static int Callee(int value) => value + 1;
}
