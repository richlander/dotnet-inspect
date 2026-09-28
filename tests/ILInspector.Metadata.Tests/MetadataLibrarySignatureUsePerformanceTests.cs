using System.Diagnostics;
using System.Globalization;

namespace ILInspector.Metadata.Tests;

public sealed class MetadataLibrarySignatureUsePerformanceTests(
    ITestOutputHelper output)
{
    [Fact]
    [Trait("Speed", "Slow")]
    public void ReferenceAndProductRoutesRemainEquivalentWithMeasuredCost()
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
                    "runtime",
                    "System.Text.Json.dll")),
            Measure(
                "System.Private.CoreLib",
                Path.Combine(
                    artifacts,
                    "System.Private.CoreLib.dll")),
        ];

        string[] lines =
        [
            "asset\ttypes\tsites\toccurrences\tbefore_ms"
                + "\tbefore_bytes\tafter_ms\tafter_bytes"
                + "\ttime_ratio\tallocation_ratio"
                + "\tmetadata_rows\tdeclaration_candidates"
                + "\trelationship_edges\tsignature_bytes"
                + "\tstructured_nodes\tretained_text"
                + "\tinterface_rows",
            .. measurements.Select(Format),
        ];
        foreach (string line in lines)
            output.WriteLine(line);

        string? reportPath =
            Environment.GetEnvironmentVariable(
                "DOTNET_INSPECT_SIGNATURE_USE_REPORT");
        if (!string.IsNullOrWhiteSpace(reportPath))
            File.WriteAllLines(reportPath, lines);
    }

    private static Measurement Measure(
        string asset,
        string path)
    {
        CancellationToken cancellationToken =
            TestContext.Current.CancellationToken;
        using AssemblyInspectionSession session =
            AssemblyInspectionSession.Open(path);

        LibrarySignatureUseReferenceResult before =
            session.InspectImage(
                image =>
                    LibrarySignatureUseReferenceScanner.Scan(
                        image,
                        cancellationToken));
        MetadataLibrarySignatureUseResult after =
            Product(session, cancellationToken);
        Assert.Equal(before.TypeCount, after.Types.Length);
        Assert.Equal(before.SiteCount, after.Coverage.Considered);
        Assert.Equal(before.Occurrences, after.Occurrences);
        Assert.Equal(
            MetadataLibrarySignatureUseDisposition.Complete,
            after.Disposition);

        for (int index = 0; index < 2; index++)
        {
            _ = Reference(session, cancellationToken);
            _ = Product(session, cancellationToken);
        }

        const int repetitions = 9;
        var beforeTimes = new double[repetitions];
        var beforeAllocations = new long[repetitions];
        var afterTimes = new double[repetitions];
        var afterAllocations = new long[repetitions];
        for (int index = 0; index < repetitions; index++)
        {
            if ((index & 1) == 0)
            {
                MeasureReference(
                    session,
                    cancellationToken,
                    index,
                    beforeTimes,
                    beforeAllocations);
                MeasureProduct(
                    session,
                    cancellationToken,
                    index,
                    afterTimes,
                    afterAllocations);
            }
            else
            {
                MeasureProduct(
                    session,
                    cancellationToken,
                    index,
                    afterTimes,
                    afterAllocations);
                MeasureReference(
                    session,
                    cancellationToken,
                    index,
                    beforeTimes,
                    beforeAllocations);
            }
        }

        double beforeMilliseconds = Median(beforeTimes);
        long beforeBytes = Median(beforeAllocations);
        double afterMilliseconds = Median(afterTimes);
        long afterBytes = Median(afterAllocations);
        return new(
            asset,
            after.Types.Length,
            after.Coverage.Considered,
            after.Occurrences.Length,
            beforeMilliseconds,
            beforeBytes,
            afterMilliseconds,
            afterBytes,
            afterMilliseconds / beforeMilliseconds,
            (double)afterBytes / beforeBytes,
            after.Receipt.Counters);
    }

    private static void MeasureReference(
        AssemblyInspectionSession session,
        CancellationToken cancellationToken,
        int index,
        double[] times,
        long[] allocations)
    {
        long allocatedBefore =
            GC.GetAllocatedBytesForCurrentThread();
        long timestamp = Stopwatch.GetTimestamp();
        LibrarySignatureUseReferenceResult result =
            Reference(session, cancellationToken);
        times[index] = Stopwatch.GetElapsedTime(timestamp).TotalMilliseconds;
        allocations[index] =
            GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        GC.KeepAlive(result);
    }

    private static void MeasureProduct(
        AssemblyInspectionSession session,
        CancellationToken cancellationToken,
        int index,
        double[] times,
        long[] allocations)
    {
        long allocatedBefore =
            GC.GetAllocatedBytesForCurrentThread();
        long timestamp = Stopwatch.GetTimestamp();
        MetadataLibrarySignatureUseResult result =
            Product(session, cancellationToken);
        times[index] = Stopwatch.GetElapsedTime(timestamp).TotalMilliseconds;
        allocations[index] =
            GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        GC.KeepAlive(result);
    }

    private static LibrarySignatureUseReferenceResult Reference(
        AssemblyInspectionSession session,
        CancellationToken cancellationToken) =>
        session.InspectImage(
            image =>
                LibrarySignatureUseReferenceScanner.Scan(
                    image,
                    cancellationToken));

    private static MetadataLibrarySignatureUseResult Product(
        AssemblyInspectionSession session,
        CancellationToken cancellationToken)
    {
        MetadataLibrarySignatureUseOutcome outcome =
            session.LibrarySignatureUses(
                new(MetadataOperationPolicy.Unbounded),
                cancellationToken);
        return outcome
            is MetadataLibrarySignatureUseOutcome.Available available
                ? available.Result
                : throw new InvalidOperationException(
                    "The product route rejected a reference asset.");
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
            measurement.Sites.ToString(CultureInfo.InvariantCulture),
            measurement.Occurrences.ToString(
                CultureInfo.InvariantCulture),
            measurement.BeforeMilliseconds.ToString(
                "F4",
                CultureInfo.InvariantCulture),
            measurement.BeforeBytes.ToString(
                CultureInfo.InvariantCulture),
            measurement.AfterMilliseconds.ToString(
                "F4",
                CultureInfo.InvariantCulture),
            measurement.AfterBytes.ToString(
                CultureInfo.InvariantCulture),
            measurement.TimeRatio.ToString(
                "F4",
                CultureInfo.InvariantCulture),
            measurement.AllocationRatio.ToString(
                "F4",
                CultureInfo.InvariantCulture),
            measurement.Counters.MetadataRows.ToString(
                CultureInfo.InvariantCulture),
            measurement.Counters.DeclarationCandidates.ToString(
                CultureInfo.InvariantCulture),
            measurement.Counters.RelationshipEdges.ToString(
                CultureInfo.InvariantCulture),
            measurement.Counters.SignatureBytes.ToString(
                CultureInfo.InvariantCulture),
            measurement.Counters.StructuredNodes.ToString(
                CultureInfo.InvariantCulture),
            measurement.Counters.RetainedText.ToString(
                CultureInfo.InvariantCulture),
            measurement.Counters.InterfaceImplementationRows.ToString(
                CultureInfo.InvariantCulture));

    private sealed record Measurement(
        string Asset,
        int Types,
        int Sites,
        int Occurrences,
        double BeforeMilliseconds,
        long BeforeBytes,
        double AfterMilliseconds,
        long AfterBytes,
        double TimeRatio,
        double AllocationRatio,
        MetadataOperationCounters Counters);
}
