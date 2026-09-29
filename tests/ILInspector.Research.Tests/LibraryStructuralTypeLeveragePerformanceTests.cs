using System.Collections.Immutable;
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
            "asset\ttypes\tsignature_occurrences"
                + "\trows\tsea_level\tmountain_peak"
                + "\tsea_category\tmountain_category\tchecksum"
                + "\tparent_ms\tparent_bytes\tchild_ms\tchild_bytes"
                + "\ttime_ratio\tallocation_ratio"
                + "\tgraph_composition_ms\tgraph_composition_bytes"
                + "\tprojection_ms"
                + "\tprojection_bytes\tsurface_query_ms"
                + "\tsurface_query_bytes\tcanonical_nodes"
                + "\tcanonical_edges\tincoming_edges\toutgoing_edges"
                + "\tadjacency_entries",
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
        LibraryStructuralReportDocument parent = Available(
            LibraryStructuralReport.Execute(analysis));
        LibraryStructuralReportDocument child = Available(
            LibraryStructuralReport.Execute(
                analysis,
                signature));
        AssertUnchanged(parent, child);
        LibraryStructuralTypeLeverageDocument leverage =
            Assert.IsType<LibraryStructuralTypeLeverageDocument>(
                child.TypeLeverage);

        for (var index = 0; index < 2; index++)
        {
            _ = Parent(analysis);
            _ = Child(analysis, signature);
            _ = Surface(path, cancellationToken);
        }

        const int repetitions = 7;
        ((double ParentMilliseconds, long ParentBytes) parentCost,
            (double ChildMilliseconds, long ChildBytes) childCost) =
                MeasurePair(
                    repetitions,
                    () => Parent(analysis),
                    () => Child(analysis, signature));
        (double GraphMilliseconds, long GraphBytes) graphCost =
            MeasureRepeated(
                repetitions,
                () => LibraryStructuralReport.ExecuteTypeLeverageGraph(
                    signature));
        LibraryStructuralReport.TypeLeverageGraphExecution graph =
            LibraryStructuralReport.ExecuteTypeLeverageGraph(
                signature);
        (double ProjectionMilliseconds, long ProjectionBytes) projectionCost =
            MeasureRepeated(
                repetitions,
                () => LibraryStructuralReport.ProjectTypeLeverage(
                    signature,
                    graph));
        (double SurfaceMilliseconds, long SurfaceBytes) surfaceCost =
            MeasureRepeated(
                repetitions,
                () => Surface(path, cancellationToken));

        GraphExecutionWorkReceipt signatureWork =
            leverage.GraphWork.SignatureIncomingDegree;
        GraphExecutionWorkReceipt outgoingWork =
            leverage.GraphWork.SignatureOutgoingDegree;
        return new(
            asset,
            signature.Types.Length,
            signature.Occurrences.Length,
            leverage.Rows.Length,
            leverage.SeaLevel.Types.Length,
            leverage.MountainPeak.Types.Length,
            CategoryCount(
                leverage.Rows,
                static row => row.SignatureIncomingDegree),
            CategoryCount(
                leverage.Rows,
                static row => row.SignatureOutgoingDegree),
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
            surfaceCost.SurfaceMilliseconds,
            surfaceCost.SurfaceBytes,
            signatureWork.CanonicalNodesExamined,
            signatureWork.CanonicalEdgesExamined,
            signatureWork.SelectedEdgesIndexed,
            outgoingWork.SelectedEdgesIndexed,
            outgoingWork.AdjacencyEntriesExamined);
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
        MetadataLibrarySignatureUseResult signature) =>
        Available(
            LibraryStructuralReport.Execute(analysis, signature));

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

    private static LibraryStructuralTypeLeverageDocument Surface(
        string path,
        CancellationToken cancellationToken) =>
        LibraryStructuralReport.CreateTypeLeverage(
            Signature(path, cancellationToken));

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
            value.Append(row.SignatureOutgoingDegree);
            value.Append(':');
            value.Append((int)row.Role);
            value.Append(';');
        }
        byte[] hash = SHA256.HashData(
            Encoding.UTF8.GetBytes(value.ToString()));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static int CategoryCount(
        ImmutableArray<LibraryStructuralTypeLeverageRow> rows,
        Func<LibraryStructuralTypeLeverageRow, int> degree)
    {
        int maximum = rows
            .Where(static row => row.RankingEligible)
            .Select(degree)
            .DefaultIfEmpty()
            .Max();
        if (maximum == 0)
            return 0;
        return rows.Count(row =>
            row.RankingEligible
            && degree(row) >= maximum * 0.5);
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
            measurement.Rows.ToString(CultureInfo.InvariantCulture),
            measurement.SeaLevel.ToString(CultureInfo.InvariantCulture),
            measurement.MountainPeak.ToString(
                CultureInfo.InvariantCulture),
            measurement.SeaCategory.ToString(
                CultureInfo.InvariantCulture),
            measurement.MountainCategory.ToString(
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
            measurement.SurfaceMilliseconds.ToString(
                "F4",
                CultureInfo.InvariantCulture),
            measurement.SurfaceBytes.ToString(
                CultureInfo.InvariantCulture),
            measurement.CanonicalNodes.ToString(
                CultureInfo.InvariantCulture),
            measurement.CanonicalEdges.ToString(
                CultureInfo.InvariantCulture),
            measurement.IncomingEdges.ToString(
                CultureInfo.InvariantCulture),
            measurement.OutgoingEdges.ToString(
                CultureInfo.InvariantCulture),
            measurement.AdjacencyEntries.ToString(
                CultureInfo.InvariantCulture));

    private sealed record Measurement(
        string Asset,
        int Types,
        int SignatureOccurrences,
        int Rows,
        int SeaLevel,
        int MountainPeak,
        int SeaCategory,
        int MountainCategory,
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
        double SurfaceMilliseconds,
        long SurfaceBytes,
        int CanonicalNodes,
        int CanonicalEdges,
        int IncomingEdges,
        int OutgoingEdges,
        int AdjacencyEntries);
}
