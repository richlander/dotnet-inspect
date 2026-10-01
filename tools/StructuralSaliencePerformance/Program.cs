using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

using ILInspector.Metadata;
using ILInspector.Research;

if (args.Length < 2)
    return Usage();

var iterations = 15;
var assets = new List<(string Label, string Path)>();
for (var index = 0; index < args.Length; index++)
{
    if (args[index] == "--iterations"
        && index + 1 < args.Length
        && int.TryParse(args[++index], out int value)
        && value > 0)
    {
        iterations = value;
        continue;
    }
    if (index + 1 >= args.Length)
        return Usage();
    assets.Add((args[index], Path.GetFullPath(args[++index])));
}

Console.WriteLine(
    "asset\tasset_sha256\tmvid\tscenario\texact_namespace"
    + "\titerations\tmedian_elapsed_ms\tp95_elapsed_ms"
    + "\tmedian_cpu_ms\tp95_cpu_ms\tmedian_allocated_bytes"
    + "\tp95_allocated_bytes\tnamespaces\trows\tsea_level"
    + "\tmountain_peaks\tresult_identity_sha256");
foreach ((string label, string path) in assets)
{
    if (!File.Exists(path))
    {
        Console.Error.WriteLine($"Assembly not found: {path}");
        return 2;
    }
    Measure(label, path, iterations);
}
return 0;

static int Usage()
{
    Console.Error.WriteLine(
        "Usage: structural-salience-performance "
        + "[--iterations N] <label> <assembly> [<label> <assembly> ...]");
    return 2;
}

static void Measure(
    string label,
    string path,
    int iterations)
{
    CancellationToken cancellationToken = CancellationToken.None;
    LibraryStructuralNamespaceLeverageIndex initial =
        Index(path, cancellationToken);
    string topNamespace = initial.Rows[0].Namespace;
    Scenario[] scenarios =
    [
        new(
            "namespace-index",
            () => Index(path, cancellationToken)),
        new(
            "top-namespace-shard",
            () => Shard(path, topNamespace, cancellationToken)),
        new(
            "exhaustive-composition",
            () => Exhaustive(path, cancellationToken)),
    ];

    foreach (Scenario scenario in scenarios)
    {
        for (var warmup = 0; warmup < 2; warmup++)
            GC.KeepAlive(scenario.Execute());
    }

    var samples = scenarios.ToDictionary(
        static scenario => scenario.Name,
        static _ => new List<Sample>());
    var results = new Dictionary<string, object>();
    for (var iteration = 0; iteration < iterations; iteration++)
    {
        for (var scenarioIndex = 0;
            scenarioIndex < scenarios.Length;
            scenarioIndex++)
        {
            Scenario scenario =
                scenarios[(iteration + scenarioIndex) % scenarios.Length];
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            long allocatedBefore =
                GC.GetAllocatedBytesForCurrentThread();
            TimeSpan cpuBefore =
                Process.GetCurrentProcess().TotalProcessorTime;
            long started = Stopwatch.GetTimestamp();
            object result = scenario.Execute();
            double elapsed =
                Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            double cpu =
                (Process.GetCurrentProcess().TotalProcessorTime - cpuBefore)
                .TotalMilliseconds;
            long allocated =
                GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
            samples[scenario.Name].Add(
                new(elapsed, cpu, allocated));
            results[scenario.Name] = result;
        }
    }

    string assetSha256 =
        Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)))
            .ToLowerInvariant();
    foreach (Scenario scenario in scenarios)
    {
        List<Sample> scenarioSamples = samples[scenario.Name];
        object result = results[scenario.Name];
        (int namespaces, int rows, int sea, int peaks, string identity) =
            Result(result);
        Console.WriteLine(
            string.Join(
                '\t',
                label,
                assetSha256,
                initial.SignatureUse.Receipt.ModuleVersionId.ToString("D"),
                scenario.Name,
                scenario.Name == "top-namespace-shard"
                    ? topNamespace
                    : "",
                iterations.ToString(CultureInfo.InvariantCulture),
                MedianDouble(
                    scenarioSamples.Select(
                        static sample => sample.ElapsedMilliseconds))
                    .ToString("F4", CultureInfo.InvariantCulture),
                P95Double(
                    scenarioSamples.Select(
                        static sample => sample.ElapsedMilliseconds))
                    .ToString("F4", CultureInfo.InvariantCulture),
                MedianDouble(
                    scenarioSamples.Select(
                        static sample => sample.CpuMilliseconds))
                    .ToString("F4", CultureInfo.InvariantCulture),
                P95Double(
                    scenarioSamples.Select(
                        static sample => sample.CpuMilliseconds))
                    .ToString("F4", CultureInfo.InvariantCulture),
                MedianLong(
                    scenarioSamples.Select(
                        static sample => sample.AllocatedBytes))
                    .ToString(CultureInfo.InvariantCulture),
                P95Long(
                    scenarioSamples.Select(
                        static sample => sample.AllocatedBytes))
                    .ToString(CultureInfo.InvariantCulture),
                namespaces.ToString(CultureInfo.InvariantCulture),
                rows.ToString(CultureInfo.InvariantCulture),
                sea.ToString(CultureInfo.InvariantCulture),
                peaks.ToString(CultureInfo.InvariantCulture),
                identity));
    }
}

static LibraryStructuralNamespaceLeverageIndex Index(
    string path,
    CancellationToken cancellationToken) =>
    LibraryStructuralReport.CreateNamespaceLeverageIndex(
        SignaturePath(path, exactNamespace: null, cancellationToken));

static LibraryStructuralTypeLeverageShard Shard(
    string path,
    string exactNamespace,
    CancellationToken cancellationToken) =>
    LibraryStructuralReport.CreateTypeLeverageShard(
        SignaturePath(path, exactNamespace, cancellationToken));

static LibraryStructuralSalienceDocument Exhaustive(
    string path,
    CancellationToken cancellationToken)
{
    using AssemblyInspectionSession session =
        AssemblyInspectionSession.Open(path);
    LibraryStructuralNamespaceLeverageIndex index =
        LibraryStructuralReport.CreateNamespaceLeverageIndex(
            SignatureSession(
                session,
                exactNamespace: null,
                cancellationToken));
    LibraryStructuralTypeLeverageShard[] shards =
    [
        .. index.Rows.Select(row =>
            LibraryStructuralReport.CreateTypeLeverageShard(
                SignatureSession(
                    session,
                    row.Namespace,
                    cancellationToken))),
    ];
    return LibraryStructuralReport.CreateStructuralSalience(
        index,
        shards);
}

static MetadataLibrarySignatureUseResult SignaturePath(
    string path,
    string? exactNamespace,
    CancellationToken cancellationToken)
{
    using AssemblyInspectionSession session =
        AssemblyInspectionSession.Open(path);
    return SignatureSession(
        session,
        exactNamespace,
        cancellationToken);
}

static MetadataLibrarySignatureUseResult SignatureSession(
    AssemblyInspectionSession session,
    string? exactNamespace,
    CancellationToken cancellationToken)
{
    MetadataLibrarySignatureUseOutcome outcome =
        session.LibrarySignatureUses(
            exactNamespace is null
                ? new(MetadataOperationPolicy.Unbounded)
                : new(
                    MetadataOperationPolicy.Unbounded,
                    exactNamespace),
            cancellationToken);
    return outcome switch
    {
        MetadataLibrarySignatureUseOutcome.Available available =>
            available.Result,
        MetadataLibrarySignatureUseOutcome.Rejected rejected =>
            throw new InvalidOperationException(
                $"Signature use was rejected: "
                + $"{rejected.Kind}: {rejected.Detail}"),
        _ => throw new InvalidOperationException(
            "Unknown signature-use outcome."),
    };
}

static (int Namespaces, int Rows, int Sea, int Peaks, string Identity)
    Result(object result) =>
    result switch
    {
        LibraryStructuralNamespaceLeverageIndex index => (
            index.Rows.Length,
            index.Rows.Length,
            0,
            0,
            IdentityIndex(index)),
        LibraryStructuralTypeLeverageShard shard => (
            1,
            shard.Rows.Length,
            shard.Rows.Count(
                static row =>
                    row.Pole
                        == LibraryStructuralTypePole.SeaLevel),
            shard.Rows.Count(
                static row =>
                    row.Pole
                        == LibraryStructuralTypePole.MountainPeak),
            IdentityShard(shard)),
        LibraryStructuralSalienceDocument document => (
            document.NamespaceIndex.Rows.Length,
            document.TypeLeverageShards.Sum(
                static shard => shard.Rows.Length),
            document.TypeLeverageShards.Sum(
                static shard =>
                    shard.Rows.Count(
                        static row =>
                            row.Pole
                                == LibraryStructuralTypePole.SeaLevel)),
            document.TypeLeverageShards.Sum(
                static shard =>
                    shard.Rows.Count(
                        static row =>
                            row.Pole
                                == LibraryStructuralTypePole.MountainPeak)),
            IdentityDocument(document)),
        _ => throw new InvalidOperationException(
            "Unknown structural-salience result."),
    };

static string IdentityIndex(
    LibraryStructuralNamespaceLeverageIndex index)
{
    var value = new StringBuilder();
    AppendIndex(value, index);
    return Hash(value);
}

static string IdentityShard(
    LibraryStructuralTypeLeverageShard shard)
{
    var value = new StringBuilder();
    AppendShard(value, shard);
    return Hash(value);
}

static string IdentityDocument(
    LibraryStructuralSalienceDocument document)
{
    var value = new StringBuilder();
    AppendIndex(value, document.NamespaceIndex);
    foreach (LibraryStructuralTypeLeverageShard shard
        in document.TypeLeverageShards)
    {
        AppendShard(value, shard);
    }
    return Hash(value);
}

static void AppendIndex(
    StringBuilder value,
    LibraryStructuralNamespaceLeverageIndex index)
{
    foreach (LibraryStructuralNamespaceLeverageRow row in index.Rows)
    {
        value.Append(row.Namespace);
        value.Append(':');
        value.Append(row.ExternalIncomingSourceTypeCount);
        value.Append(':');
        value.Append(row.TopLeverage ? '1' : '0');
        value.Append(';');
    }
}

static void AppendShard(
    StringBuilder value,
    LibraryStructuralTypeLeverageShard shard)
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
        value.Append(
            row.Pole == LibraryStructuralTypePole.SeaLevel
                ? '1'
                : '0');
        value.Append(':');
        value.Append(
            row.Pole == LibraryStructuralTypePole.MountainPeak
                ? '1'
                : '0');
        value.Append(';');
    }
}

static string Hash(StringBuilder value) =>
    Convert.ToHexString(
        SHA256.HashData(
            Encoding.UTF8.GetBytes(value.ToString())))
        .ToLowerInvariant();

static double MedianDouble(IEnumerable<double> values)
{
    double[] ordered = [.. values.Order()];
    return ordered[ordered.Length / 2];
}

static long MedianLong(IEnumerable<long> values)
{
    long[] ordered = [.. values.Order()];
    return ordered[ordered.Length / 2];
}

static double P95Double(IEnumerable<double> values)
{
    double[] ordered = [.. values.Order()];
    return ordered[(int)Math.Ceiling(ordered.Length * 0.95) - 1];
}

static long P95Long(IEnumerable<long> values)
{
    long[] ordered = [.. values.Order()];
    return ordered[(int)Math.Ceiling(ordered.Length * 0.95) - 1];
}

internal sealed record Scenario(
    string Name,
    Func<object> Execute);

internal sealed record Sample(
    double ElapsedMilliseconds,
    double CpuMilliseconds,
    long AllocatedBytes);
