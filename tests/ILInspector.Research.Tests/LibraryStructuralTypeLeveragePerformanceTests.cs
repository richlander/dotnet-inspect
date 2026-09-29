using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

using ILInspector.Analysis;
using ILInspector.Metadata;
using Inspector.Graph;

namespace ILInspector.Research.Tests;

public sealed class LibraryStructuralTypeLeveragePerformanceTests(
    ITestOutputHelper output)
{
    [Fact]
    [Trait("Speed", "Slow")]
    public void ParentAndLeverageRoutesRetainEquivalentReportFactsWithMeasuredCost()
    {
        string artifacts = Path.Combine(
            AppContext.BaseDirectory,
            "PinnedArtifacts");
        Measurement[] measurements =
        [
            Measure(
                "System.Text.Json",
                Path.Combine(
                    artifacts,
                    "packages",
                    "System.Text.Json.10.0.0.dll")),
            Measure(
                "System.Private.CoreLib",
                Path.Combine(
                    artifacts,
                    "System.Private.CoreLib.dll")),
        ];

        string[] lines =
        [
            "asset\ttypes\tsignature_occurrences\tbody_occurrences"
                + "\trows\tsea_level\tmountain_peak\tchecksum"
                + "\tparent_ms\tparent_bytes\tchild_ms\tchild_bytes"
                + "\ttime_ratio\tallocation_ratio"
                + "\tgraph_composition_ms\tgraph_composition_bytes"
                + "\tprojection_ms"
                + "\tprojection_bytes\tcanonical_nodes"
                + "\tcanonical_edges\tsignature_edges\tbody_edges"
                + "\tcombined_edges\tadjacency_entries",
            .. measurements.Select(Format),
        ];
        foreach (string line in lines)
            output.WriteLine(line);

        string? reportPath = Environment.GetEnvironmentVariable(
            "DOTNET_INSPECT_TYPE_LEVERAGE_REPORT");
        if (!string.IsNullOrWhiteSpace(reportPath))
            File.WriteAllLines(reportPath, lines);
    }

    private static Measurement Measure(string asset, string path)
    {
        CancellationToken cancellationToken =
            TestContext.Current.CancellationToken;
        LibraryBodyAnalysisExecution analysis =
            LibraryBodyAnalysisService.ExecutePath(
                path,
                LibraryBodyAnalysisRequest
                    .CreateCompleteImplementationProfile());
        MetadataLibrarySignatureUseResult signature =
            Signature(path, cancellationToken);
        AnalysisLibraryBodyUseResult body =
            Body(path, cancellationToken);

        LibraryStructuralReportDocument parent = Available(
            LibraryStructuralReport.Execute(analysis));
        LibraryStructuralReportDocument child = Available(
            LibraryStructuralReport.Execute(
                analysis,
                signature,
                body));
        AssertUnchanged(parent, child);
        LibraryStructuralTypeLeverageDocument leverage =
            Assert.IsType<LibraryStructuralTypeLeverageDocument>(
                child.TypeLeverage);

        for (var index = 0; index < 2; index++)
        {
            _ = Parent(analysis);
            _ = Child(analysis, signature, body);
        }

        const int repetitions = 7;
        ((double ParentMilliseconds, long ParentBytes) parentCost,
            (double ChildMilliseconds, long ChildBytes) childCost) =
                MeasurePair(
                    repetitions,
                    () => Parent(analysis),
                    () => Child(analysis, signature, body));
        (double GraphMilliseconds, long GraphBytes) graphCost =
            MeasureRepeated(
                repetitions,
                () => LibraryStructuralReport.ExecuteTypeLeverageGraph(
                    analysis.Receipt,
                    signature,
                    body));
        LibraryStructuralReport.TypeLeverageGraphExecution graph =
            LibraryStructuralReport.ExecuteTypeLeverageGraph(
                analysis.Receipt,
                signature,
                body);
        (double ProjectionMilliseconds, long ProjectionBytes) projectionCost =
            MeasureRepeated(
                repetitions,
                () => LibraryStructuralReport.ProjectTypeLeverage(
                    signature,
                    body,
                    graph));

        GraphExecutionWorkReceipt signatureWork =
            leverage.GraphWork.SignatureIncomingDegree;
        GraphExecutionWorkReceipt bodyWork =
            leverage.GraphWork.BodyOutgoingDegree;
        GraphExecutionWorkReceipt combinedWork =
            leverage.GraphWork.CombinedIncomingDegree;
        return new(
            asset,
            signature.Types.Length,
            signature.Occurrences.Length,
            body.Occurrences.Length,
            leverage.Rows.Length,
            leverage.SeaLevel.Types.Length,
            leverage.MountainPeak.Types.Length,
            Checksum(leverage),
            parentCost.ParentMilliseconds,
            parentCost.ParentBytes,
            childCost.ChildMilliseconds,
            childCost.ChildBytes,
            childCost.ChildMilliseconds
                / parentCost.ParentMilliseconds,
            (double)childCost.ChildBytes / parentCost.ParentBytes,
            graphCost.GraphMilliseconds,
            graphCost.GraphBytes,
            projectionCost.ProjectionMilliseconds,
            projectionCost.ProjectionBytes,
            signatureWork.CanonicalNodesExamined,
            signatureWork.CanonicalEdgesExamined,
            signatureWork.SelectedEdgesIndexed,
            bodyWork.SelectedEdgesIndexed,
            combinedWork.SelectedEdgesIndexed,
            combinedWork.AdjacencyEntriesExamined);
    }

    private static T Measure<T>(
        Func<T> operation,
        out double milliseconds,
        out long allocated)
    {
        long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        long timestamp = Stopwatch.GetTimestamp();
        T result = operation();
        milliseconds = Stopwatch.GetElapsedTime(
            timestamp).TotalMilliseconds;
        allocated =
            GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        return result;
    }

    private static (double Milliseconds, long Bytes) MeasureRepeated<T>(
        int repetitions,
        Func<T> operation)
    {
        var times = new double[repetitions];
        var allocations = new long[repetitions];
        for (var index = 0; index < repetitions; index++)
        {
            T result = Measure(
                operation,
                out times[index],
                out allocations[index]);
            GC.KeepAlive(result);
        }
        return (Median(times), Median(allocations));
    }

    private static (
        (double Milliseconds, long Bytes) Before,
        (double Milliseconds, long Bytes) After) MeasurePair<
            TBefore,
            TAfter>(
        int repetitions,
        Func<TBefore> before,
        Func<TAfter> after)
    {
        var beforeTimes = new double[repetitions];
        var beforeAllocations = new long[repetitions];
        var afterTimes = new double[repetitions];
        var afterAllocations = new long[repetitions];
        for (var index = 0; index < repetitions; index++)
        {
            if ((index & 1) == 0)
            {
                TBefore beforeResult = Measure(
                    before,
                    out beforeTimes[index],
                    out beforeAllocations[index]);
                TAfter afterResult = Measure(
                    after,
                    out afterTimes[index],
                    out afterAllocations[index]);
                GC.KeepAlive(beforeResult);
                GC.KeepAlive(afterResult);
            }
            else
            {
                TAfter afterResult = Measure(
                    after,
                    out afterTimes[index],
                    out afterAllocations[index]);
                TBefore beforeResult = Measure(
                    before,
                    out beforeTimes[index],
                    out beforeAllocations[index]);
                GC.KeepAlive(afterResult);
                GC.KeepAlive(beforeResult);
            }
        }
        return (
            (Median(beforeTimes), Median(beforeAllocations)),
            (Median(afterTimes), Median(afterAllocations)));
    }

    private static LibraryStructuralReportDocument Parent(
        LibraryBodyAnalysisExecution analysis) =>
        Available(LibraryStructuralReport.Execute(analysis));

    private static LibraryStructuralReportDocument Child(
        LibraryBodyAnalysisExecution analysis,
        MetadataLibrarySignatureUseResult signature,
        AnalysisLibraryBodyUseResult body) =>
        Available(
            LibraryStructuralReport.Execute(analysis, signature, body));

    private static LibraryStructuralReportDocument Available(
        LibraryStructuralReportResult result) =>
        Assert.IsType<LibraryStructuralReportResult.Available>(result)
            .Document;

    private static MetadataLibrarySignatureUseResult Signature(
        string path,
        CancellationToken cancellationToken)
    {
        using AssemblyInspectionSession session =
            AssemblyInspectionSession.Open(path);
        MetadataLibrarySignatureUseOutcome outcome =
            session.LibrarySignatureUses(
                new(MetadataOperationPolicy.Unbounded),
                cancellationToken);
        return Assert.IsType<
            MetadataLibrarySignatureUseOutcome.Available>(outcome).Result;
    }

    private static AnalysisLibraryBodyUseResult Body(
        string path,
        CancellationToken cancellationToken)
    {
        AnalysisLibraryBodyUseOutcome outcome =
            AnalysisLibraryBodyUseService.ExecutePath(
                path,
                new(),
                cancellationToken);
        return Assert.IsType<
            AnalysisLibraryBodyUseOutcome.Available>(outcome).Result;
    }

    private static void AssertUnchanged(
        LibraryStructuralReportDocument parent,
        LibraryStructuralReportDocument child)
    {
        Assert.Same(parent.AnalysisReceipt, child.AnalysisReceipt);
        Assert.Equivalent(parent.Population, child.Population, strict: true);
        Assert.Equivalent(
            parent.Distributions,
            child.Distributions,
            strict: true);
        Assert.Equivalent(
            parent.AsyncStateMachinePresence,
            child.AsyncStateMachinePresence,
            strict: true);
        Assert.Equivalent(
            parent.TypeSummaries,
            child.TypeSummaries,
            strict: true);
        Assert.Equivalent(
            parent.EntangledRelationships,
            child.EntangledRelationships,
            strict: true);
        Assert.Equivalent(
            parent.Diagnostics,
            child.Diagnostics,
            strict: true);
    }

    private static string Checksum(
        LibraryStructuralTypeLeverageDocument leverage)
    {
        var value = new StringBuilder();
        foreach (LibraryStructuralTypeLeverageRow row in leverage.Rows)
        {
            value.Append(row.Type.Definition.Value);
            value.Append(':');
            value.Append(row.SignatureIncomingDegree);
            value.Append(':');
            value.Append(row.BodyOutgoingDegree);
            value.Append(':');
            value.Append(row.CombinedIncomingDegree);
            value.Append(':');
            value.Append(row.CombinedOutgoingDegree);
            value.Append(':');
            value.Append((int)row.Role);
            value.Append(';');
        }
        byte[] hash = SHA256.HashData(
            Encoding.UTF8.GetBytes(value.ToString()));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static double Median(double[] values)
    {
        double[] sorted = [.. values.Order()];
        return sorted[sorted.Length / 2];
    }

    private static long Median(long[] values)
    {
        long[] sorted = [.. values.Order()];
        return sorted[sorted.Length / 2];
    }

    private static string Format(Measurement measurement) =>
        string.Join(
            '\t',
            measurement.Asset,
            measurement.Types.ToString(CultureInfo.InvariantCulture),
            measurement.SignatureOccurrences.ToString(
                CultureInfo.InvariantCulture),
            measurement.BodyOccurrences.ToString(
                CultureInfo.InvariantCulture),
            measurement.Rows.ToString(CultureInfo.InvariantCulture),
            measurement.SeaLevel.ToString(CultureInfo.InvariantCulture),
            measurement.MountainPeak.ToString(
                CultureInfo.InvariantCulture),
            measurement.Checksum,
            measurement.ParentMilliseconds.ToString(
                "F4",
                CultureInfo.InvariantCulture),
            measurement.ParentBytes.ToString(
                CultureInfo.InvariantCulture),
            measurement.ChildMilliseconds.ToString(
                "F4",
                CultureInfo.InvariantCulture),
            measurement.ChildBytes.ToString(
                CultureInfo.InvariantCulture),
            measurement.TimeRatio.ToString(
                "F4",
                CultureInfo.InvariantCulture),
            measurement.AllocationRatio.ToString(
                "F4",
                CultureInfo.InvariantCulture),
            measurement.GraphMilliseconds.ToString(
                "F4",
                CultureInfo.InvariantCulture),
            measurement.GraphBytes.ToString(CultureInfo.InvariantCulture),
            measurement.ProjectionMilliseconds.ToString(
                "F4",
                CultureInfo.InvariantCulture),
            measurement.ProjectionBytes.ToString(
                CultureInfo.InvariantCulture),
            measurement.CanonicalNodes.ToString(
                CultureInfo.InvariantCulture),
            measurement.CanonicalEdges.ToString(
                CultureInfo.InvariantCulture),
            measurement.SignatureEdges.ToString(
                CultureInfo.InvariantCulture),
            measurement.BodyEdges.ToString(
                CultureInfo.InvariantCulture),
            measurement.CombinedEdges.ToString(
                CultureInfo.InvariantCulture),
            measurement.AdjacencyEntries.ToString(
                CultureInfo.InvariantCulture));

    private sealed record Measurement(
        string Asset,
        int Types,
        int SignatureOccurrences,
        int BodyOccurrences,
        int Rows,
        int SeaLevel,
        int MountainPeak,
        string Checksum,
        double ParentMilliseconds,
        long ParentBytes,
        double ChildMilliseconds,
        long ChildBytes,
        double TimeRatio,
        double AllocationRatio,
        double GraphMilliseconds,
        long GraphBytes,
        double ProjectionMilliseconds,
        long ProjectionBytes,
        int CanonicalNodes,
        int CanonicalEdges,
        int SignatureEdges,
        int BodyEdges,
        int CombinedEdges,
        int AdjacencyEntries);
}
