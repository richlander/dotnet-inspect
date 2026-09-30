using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

using ILInspector.Analysis;
using ILInspector.Analysis.Planning;

const int DefaultRounds = 15;
const int WarmupRounds = 2;

int rounds = DefaultRounds;
var suppliedAssets = new List<string>();
for (int i = 0; i < args.Length; i++)
{
    if (args[i] == "--rounds")
    {
        if (++i >= args.Length
            || !int.TryParse(
                args[i],
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out rounds)
            || rounds <= 0)
        {
            Console.Error.WriteLine("--rounds requires a positive integer.");
            return 2;
        }
    }
    else
    {
        suppliedAssets.Add(args[i]);
    }
}

BenchmarkAsset[] assets = suppliedAssets.Count == 0
    ?
    [
        DefaultAsset(
            "System.Text.Json",
            "System.Text.Json.10.0.0.dll"),
        DefaultAsset(
            "MessagePack",
            "MessagePack.2.5.192.dll"),
        DefaultAsset(
            "Jurassic",
            "Jurassic.3.2.9.dll"),
    ]
    : [.. suppliedAssets.ConvertAll(
        static path => new BenchmarkAsset(
            Path.GetFileNameWithoutExtension(path),
            Path.GetFullPath(path)))];

Console.WriteLine(
    "asset\tdisposition\tfingerprint\ttypes\tbodies\texamined"
    + "\tphysical_only\tunavailable\tlimited\toperands\toccurrences"
    + "\tdiagnostics\tmedian_ms\tp95_ms\tmedian_bytes");

foreach (BenchmarkAsset asset in assets)
{
    if (!File.Exists(asset.Path))
    {
        Console.Error.WriteLine($"Asset does not exist: {asset.Path}");
        return 2;
    }

    AnalysisLibraryBodyUseResult expected = Execute(asset.Path);
    string fingerprint = Fingerprint(expected);
    for (int i = 0; i < WarmupRounds; i++)
    {
        AnalysisLibraryBodyUseResult warmup = Execute(asset.Path);
        RequireFingerprint(asset, fingerprint, warmup);
    }

    var elapsed = new double[rounds];
    var allocated = new long[rounds];
    for (int i = 0; i < rounds; i++)
    {
        GC.Collect(
            GC.MaxGeneration,
            GCCollectionMode.Forced,
            blocking: true,
            compacting: false);

        long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        long started = Stopwatch.GetTimestamp();
        AnalysisLibraryBodyUseResult result = Execute(asset.Path);
        elapsed[i] = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        allocated[i] =
            GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        RequireFingerprint(asset, fingerprint, result);
        GC.KeepAlive(result);
    }

    AnalysisLibraryBodyUseCoverage coverage = expected.Coverage;
    Console.WriteLine(
        string.Join(
            '\t',
            asset.Name,
            expected.Disposition,
            fingerprint,
            expected.Types.Length.ToString(CultureInfo.InvariantCulture),
            coverage.BodiesConsidered.ToString(CultureInfo.InvariantCulture),
            coverage.BodiesExamined.ToString(CultureInfo.InvariantCulture),
            coverage.BodiesPhysicalOnly.ToString(
                CultureInfo.InvariantCulture),
            coverage.BodiesUnavailable.ToString(
                CultureInfo.InvariantCulture),
            coverage.BodiesLimited.ToString(CultureInfo.InvariantCulture),
            coverage.OperandsConsidered.ToString(
                CultureInfo.InvariantCulture),
            expected.Occurrences.Length.ToString(
                CultureInfo.InvariantCulture),
            expected.Diagnostics.Length.ToString(
                CultureInfo.InvariantCulture),
            Median(elapsed).ToString("F4", CultureInfo.InvariantCulture),
            Percentile95(elapsed).ToString(
                "F4",
                CultureInfo.InvariantCulture),
            MedianLong(allocated).ToString(CultureInfo.InvariantCulture)));
}

return 0;

static BenchmarkAsset DefaultAsset(string name, string fileName) =>
    new(
        name,
        Path.Combine(AppContext.BaseDirectory, "assets", fileName));

static AnalysisLibraryBodyUseResult Execute(string path) =>
    AnalysisLibraryBodyUseService.ExecutePath(path, new()) switch
    {
        AnalysisLibraryBodyUseOutcome.Available available =>
            available.Result,
        AnalysisLibraryBodyUseOutcome.Rejected rejected =>
            throw new InvalidOperationException(
                $"{rejected.Kind}: {rejected.Detail}"),
        _ => throw new InvalidOperationException(
            "Unknown Library body-use outcome."),
    };

static void RequireFingerprint(
    BenchmarkAsset asset,
    string expected,
    AnalysisLibraryBodyUseResult result)
{
    string actual = Fingerprint(result);
    if (!string.Equals(expected, actual, StringComparison.Ordinal))
    {
        throw new InvalidOperationException(
            $"{asset.Name} result changed between benchmark rounds: "
            + $"{expected} != {actual}.");
    }
}

static string Fingerprint(AnalysisLibraryBodyUseResult result)
{
    using IncrementalHash hash =
        IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

    Add(result.Disposition);
    Add(result.Receipt.ModuleVersionId);
    Add(result.Receipt.Assembly.Name);
    Add(result.Receipt.Assembly.Version);
    Add(result.Receipt.Assembly.Culture);
    Add(result.Receipt.Assembly.PublicKeyToken);
    WorkReceipt work = result.Receipt.Work;
    Add(work.UnitsVisited);
    Add(work.IdentityBudgetArmed);
    Add(work.IdentityWorkCharged);
    Add(work.SignatureShapeNodesWalked);
    foreach (ProducerParticipation producer in work.Producers)
    {
        Add(producer.Producer);
        Add(producer.Outcome);
        Add(producer.UnitsAttempted);
        Add(producer.UnitsCompleted);
        Add(producer.UnitsFailed);
        foreach (ProducerLayerParticipation layer in producer.Layers)
        {
            Add(layer.Layer);
            Add(layer.Acquired);
        }
    }

    foreach (AnalysisLibraryBodyUseType type in result.Types)
    {
        Add(type.Type.ModuleVersionId);
        Add(type.Type.Definition.Value);
        Add(type.Name.ToMetadataFullName());
        Add(type.DefinitionKind);
    }

    foreach (AnalysisLibraryBodyUseOccurrence occurrence
        in result.Occurrences)
    {
        Add(occurrence.Source.ModuleVersionId);
        Add(occurrence.Source.Definition.Value);
        Add(occurrence.SourceType.ToMetadataFullName());
        Add(occurrence.Target.ModuleVersionId);
        Add(occurrence.Target.Definition.Value);
        Add(occurrence.TargetType.ToMetadataFullName());
        Add(occurrence.PhysicalMethodToken);
        Add(occurrence.OperandKind);
        Add(occurrence.OperandToken);
        Add(occurrence.IlOffset);
        Add(occurrence.OccurrenceOrdinal);
    }

    foreach (AnalysisLibraryBodyUsePhysicalEvidence physical
        in result.PhysicalEvidence)
    {
        Add(physical.PhysicalSource.ModuleVersionId);
        Add(physical.PhysicalSource.Definition.Value);
        Add(physical.PhysicalSourceType.ToMetadataFullName());
        Add(physical.PhysicalMethodToken);
        Add(physical.Fidelity);
    }

    AnalysisLibraryBodyUseCoverage coverage = result.Coverage;
    Add(coverage.BodiesConsidered);
    Add(coverage.BodiesExamined);
    Add(coverage.BodiesPhysicalOnly);
    Add(coverage.BodiesUnavailable);
    Add(coverage.BodiesLimited);
    Add(coverage.OperandsConsidered);
    Add(coverage.OperandsExamined);
    Add(coverage.OperandsUnavailable);
    Add(coverage.OperandsLimited);

    foreach (AnalysisLibraryBodyUseDiagnostic diagnostic
        in result.Diagnostics)
    {
        Add(diagnostic.Kind);
        Add(diagnostic.MethodToken);
        Add(diagnostic.IlOffset);
        Add(diagnostic.Detail);
        Add(diagnostic.Limit);
        Add(diagnostic.AttemptedCharge);
    }

    return Convert.ToHexString(hash.GetHashAndReset());

    void Add<T>(T value)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(
            Convert.ToString(value, CultureInfo.InvariantCulture)
                ?? "<null>");
        hash.AppendData(bytes);
        hash.AppendData("\n"u8);
    }
}

static double Median(double[] values)
{
    double[] sorted = [.. values];
    Array.Sort(sorted);
    return sorted[sorted.Length / 2];
}

static long MedianLong(long[] values)
{
    long[] sorted = [.. values];
    Array.Sort(sorted);
    return sorted[sorted.Length / 2];
}

static double Percentile95(double[] values)
{
    double[] sorted = [.. values];
    Array.Sort(sorted);
    int index = (int)Math.Ceiling(sorted.Length * 0.95) - 1;
    return sorted[index];
}

sealed record BenchmarkAsset(string Name, string Path);
