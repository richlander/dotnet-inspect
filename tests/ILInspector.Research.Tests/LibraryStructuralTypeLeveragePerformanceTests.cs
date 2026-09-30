using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

using ILInspector.Analysis;
using ILInspector.Metadata;

namespace ILInspector.Research.Tests;

public sealed class LibraryStructuralTypeLeveragePerformanceTests(
    ITestOutputHelper output)
{
    [Fact]
    [Trait("Speed", "Slow")]
    public void NamespaceIndexShardAndExhaustiveCompositionHaveMeasuredCost()
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
            "asset\ttypes\tsignature_occurrences\tnamespaces"
                + "\ttop_namespaces\tconnected_type_rows"
                + "\tsea_level_designations\tmountain_peak_designations"
                + "\tchecksum\tindex_ms\tindex_bytes"
                + "\ttop_shard_ms\ttop_shard_bytes"
                + "\texhaustive_ms\texhaustive_bytes",
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
        LibraryStructuralReportDocument parent = Available(
            LibraryStructuralReport.Execute(analysis));
        LibraryStructuralSalienceDocument salience =
            Exhaustive(path, cancellationToken);
        LibraryStructuralReportDocument child = Available(
            LibraryStructuralReport.Execute(
                analysis,
                salience));
        AssertUnchanged(parent, child);

        string topNamespace =
            salience.NamespaceIndex.Rows[0].Namespace;
        LibraryStructuralTypeLeverageShard standalone =
            Shard(path, topNamespace, cancellationToken);
        LibraryStructuralTypeLeverageShard composed =
            Assert.Single(
                salience.TypeLeverageShards,
                shard => StringComparer.Ordinal.Equals(
                    shard.Namespace,
                    topNamespace));
        Assert.Equivalent(composed, standalone, strict: true);

        for (var index = 0; index < 2; index++)
        {
            _ = Index(path, cancellationToken);
            _ = Shard(path, topNamespace, cancellationToken);
            _ = Exhaustive(path, cancellationToken);
        }

        const int repetitions = 7;
        (double IndexMilliseconds, long IndexBytes) indexCost =
            MeasureRepeated(
                repetitions,
                () => Index(path, cancellationToken));
        (double ShardMilliseconds, long ShardBytes) shardCost =
            MeasureRepeated(
                repetitions,
                () => Shard(
                    path,
                    topNamespace,
                    cancellationToken));
        (double ExhaustiveMilliseconds, long ExhaustiveBytes)
            exhaustiveCost =
                MeasureRepeated(
                    repetitions,
                    () => Exhaustive(path, cancellationToken));

        return new(
            asset,
            salience.NamespaceIndex.Rows.Sum(
                static row => row.TypeCount),
            salience.NamespaceIndex.SignatureUse.OccurrenceCount,
            salience.NamespaceIndex.Rows.Length,
            salience.NamespaceIndex.Rows.Count(
                static row => row.TopLeverage),
            salience.TypeLeverageShards.Sum(
                static shard => shard.Rows.Length),
            salience.TypeLeverageShards.Sum(
                static shard =>
                    shard.Rows.Count(static row => row.SeaLevel)),
            salience.TypeLeverageShards.Sum(
                static shard =>
                    shard.Rows.Count(
                        static row => row.MountainPeak)),
            Checksum(salience),
            indexCost.IndexMilliseconds,
            indexCost.IndexBytes,
            shardCost.ShardMilliseconds,
            shardCost.ShardBytes,
            exhaustiveCost.ExhaustiveMilliseconds,
            exhaustiveCost.ExhaustiveBytes);
    }

    private static LibraryStructuralNamespaceLeverageIndex Index(
        string path,
        CancellationToken cancellationToken) =>
        LibraryStructuralReport.CreateNamespaceLeverageIndex(
            Signature(
                path,
                exactNamespace: null,
                cancellationToken));

    private static LibraryStructuralTypeLeverageShard Shard(
        string path,
        string exactNamespace,
        CancellationToken cancellationToken) =>
        LibraryStructuralReport.CreateTypeLeverageShard(
            Signature(
                path,
                exactNamespace,
                cancellationToken));

    private static LibraryStructuralSalienceDocument Exhaustive(
        string path,
        CancellationToken cancellationToken)
    {
        using AssemblyInspectionSession session =
            AssemblyInspectionSession.Open(path);
        MetadataLibrarySignatureUseResult whole =
            Signature(
                session,
                exactNamespace: null,
                cancellationToken);
        LibraryStructuralNamespaceLeverageIndex index =
            LibraryStructuralReport.CreateNamespaceLeverageIndex(whole);
        LibraryStructuralTypeLeverageShard[] shards =
        [
            .. index.Rows.Select(row =>
                LibraryStructuralReport.CreateTypeLeverageShard(
                    Signature(
                        session,
                        row.Namespace,
                        cancellationToken))),
        ];
        return LibraryStructuralReport.CreateStructuralSalience(
            index,
            shards);
    }

    private static MetadataLibrarySignatureUseResult Signature(
        string path,
        string? exactNamespace,
        CancellationToken cancellationToken)
    {
        using AssemblyInspectionSession session =
            AssemblyInspectionSession.Open(path);
        return Signature(
            session,
            exactNamespace,
            cancellationToken);
    }

    private static MetadataLibrarySignatureUseResult Signature(
        AssemblyInspectionSession session,
        string? exactNamespace,
        CancellationToken cancellationToken)
    {
        MetadataLibrarySignatureUseRequest request =
            exactNamespace is null
                ? new(MetadataOperationPolicy.Unbounded)
                : new(
                    MetadataOperationPolicy.Unbounded,
                    exactNamespace);
        MetadataLibrarySignatureUseOutcome outcome =
            session.LibrarySignatureUses(
                request,
                cancellationToken);
        return Assert.IsType<
            MetadataLibrarySignatureUseOutcome.Available>(outcome).Result;
    }

    private static (double Milliseconds, long Bytes) MeasureRepeated<T>(
        int repetitions,
        Func<T> operation)
    {
        var times = new double[repetitions];
        var allocations = new long[repetitions];
        for (var index = 0; index < repetitions; index++)
        {
            long allocatedBefore =
                GC.GetAllocatedBytesForCurrentThread();
            long timestamp = Stopwatch.GetTimestamp();
            T result = operation();
            times[index] =
                Stopwatch.GetElapsedTime(timestamp).TotalMilliseconds;
            allocations[index] =
                GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
            GC.KeepAlive(result);
        }
        return (Median(times), Median(allocations));
    }

    private static LibraryStructuralReportDocument Available(
        LibraryStructuralReportResult result) =>
        Assert.IsType<LibraryStructuralReportResult.Available>(result)
            .Document;

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
        LibraryStructuralSalienceDocument salience)
    {
        var value = new StringBuilder();
        foreach (LibraryStructuralNamespaceLeverageRow row
            in salience.NamespaceIndex.Rows)
        {
            value.Append(row.Namespace);
            value.Append(':');
            value.Append(row.ExternalIncomingSourceTypeCount);
            value.Append(':');
            value.Append(row.TopLeverage ? '1' : '0');
            value.Append(';');
        }
        foreach (LibraryStructuralTypeLeverageShard shard
            in salience.TypeLeverageShards)
        {
            value.Append('[');
            value.Append(shard.Namespace);
            value.Append(']');
            foreach (LibraryStructuralTypeLeverageRow row in shard.Rows)
            {
                value.Append(row.Type.Definition.Value);
                value.Append(':');
                value.Append(row.SignatureIncomingDegree);
                value.Append(':');
                value.Append(row.SignatureOutgoingDegree);
                value.Append(':');
                value.Append(row.SeaLevel ? '1' : '0');
                value.Append(':');
                value.Append(row.MountainPeak ? '1' : '0');
                value.Append(';');
            }
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
            measurement.Namespaces.ToString(CultureInfo.InvariantCulture),
            measurement.TopNamespaces.ToString(
                CultureInfo.InvariantCulture),
            measurement.ConnectedTypeRows.ToString(
                CultureInfo.InvariantCulture),
            measurement.SeaLevelDesignations.ToString(
                CultureInfo.InvariantCulture),
            measurement.MountainPeakDesignations.ToString(
                CultureInfo.InvariantCulture),
            measurement.Checksum,
            measurement.IndexMilliseconds.ToString(
                "F4",
                CultureInfo.InvariantCulture),
            measurement.IndexBytes.ToString(CultureInfo.InvariantCulture),
            measurement.TopShardMilliseconds.ToString(
                "F4",
                CultureInfo.InvariantCulture),
            measurement.TopShardBytes.ToString(
                CultureInfo.InvariantCulture),
            measurement.ExhaustiveMilliseconds.ToString(
                "F4",
                CultureInfo.InvariantCulture),
            measurement.ExhaustiveBytes.ToString(
                CultureInfo.InvariantCulture));

    private sealed record Measurement(
        string Asset,
        int Types,
        int SignatureOccurrences,
        int Namespaces,
        int TopNamespaces,
        int ConnectedTypeRows,
        int SeaLevelDesignations,
        int MountainPeakDesignations,
        string Checksum,
        double IndexMilliseconds,
        long IndexBytes,
        double TopShardMilliseconds,
        long TopShardBytes,
        double ExhaustiveMilliseconds,
        long ExhaustiveBytes);
}
