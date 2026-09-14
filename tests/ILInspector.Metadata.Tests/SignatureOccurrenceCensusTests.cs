using System.Numerics;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ILInspector.Metadata.Tests;

public sealed class SignatureOccurrenceCensusTests
{
    const string CorpusVariable = "DOTNET_INSPECT_SIGNATURE_CORPUS";
    const string BaselineVariable = "DOTNET_INSPECT_SIGNATURE_BASELINE";
    static readonly SignatureOccurrenceMetric[] Quantities = Enum.GetValues<SignatureOccurrenceMetric>();

    [Fact]
    [Trait("Speed", "Slow")]
    public async Task PinnedCorpus_DecodesEverySignatureWithinProductionBudgets()
    {
        string? configured = Environment.GetEnvironmentVariable(CorpusVariable);
        string? baselinePath = Environment.GetEnvironmentVariable(BaselineVariable);
        Assert.True(string.IsNullOrWhiteSpace(baselinePath) || !string.IsNullOrWhiteSpace(configured),
            $"{BaselineVariable} requires {CorpusVariable}.");
        Assert.SkipWhen(string.IsNullOrWhiteSpace(configured), $"Set {CorpusVariable} to the prepared corpus directory.");
        string root = Path.GetFullPath(configured!);
        string manifestPath = Path.Combine(root, "manifest.json");
        Assert.True(File.Exists(manifestPath), $"Configured corpus manifest is missing: {manifestPath}");
        using JsonDocument manifest = JsonDocument.Parse(await File.ReadAllTextAsync(
            manifestPath, TestContext.Current.CancellationToken));
        Assert.Equal(1, manifest.RootElement.GetProperty("schemaVersion").GetInt32());
        var inputs = manifest.RootElement.GetProperty("assemblies").EnumerateArray().ToArray();
        Assert.NotEmpty(inputs);
        foreach (var tier in manifest.RootElement.GetProperty("tiers").EnumerateArray())
        {
            var rows = inputs.Where(row => row.GetProperty("tier").GetString()
                == tier.GetProperty("tier").GetString()).ToArray();
            Assert.Equal(tier.GetProperty("assemblies").GetInt32(), rows.Length);
            string digest = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(
                string.Concat(rows.Select(row => row.GetProperty("sha256").GetString()!)
                    .Order(StringComparer.Ordinal).Select(hash => hash + "\n")))));
            Assert.Equal(tier.GetProperty("orderedSha256").GetString(), digest);
        }
        Assert.Equal(inputs.Length, manifest.RootElement.GetProperty("tiers").EnumerateArray()
            .Sum(tier => tier.GetProperty("assemblies").GetInt32()));

        using JsonDocument? baseline = string.IsNullOrWhiteSpace(baselinePath) ? null
            : JsonDocument.Parse(await File.ReadAllTextAsync(baselinePath, TestContext.Current.CancellationToken));
        string? baselineHash = null;
        if (baseline is not null)
        {
            SignatureOccurrenceCensusContract.RequireComparableBaseline(baseline.RootElement, manifest.RootElement);
            baselineHash = await HashFile(baselinePath!);
        }

        var totals = new Dictionary<string, Totals>(StringComparer.Ordinal);
        string reportPath = Path.Combine(root, "signature-decode-census.json");
        await using (var stream = File.Create(reportPath))
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject();
            writer.WriteNumber("schemaVersion", SignatureOccurrenceCensusContract.SchemaVersion);
            writer.WriteNumber("contractVersion", SignatureOccurrenceCensusContract.CurrentVersion);
            writer.WriteString("manifestSha256", await HashFile(manifestPath));
            writer.WriteStartObject("inputFingerprints");
            foreach (var tier in manifest.RootElement.GetProperty("tiers").EnumerateArray())
                writer.WriteString(tier.GetProperty("tier").GetString()!, tier.GetProperty("orderedSha256").GetString());
            writer.WriteEndObject();
            if (baselineHash is not null)
                writer.WriteString("baselineReportSha256", baselineHash);
            writer.WriteString("metadataAssemblySha256", await HashFile(typeof(SignatureOccurrenceDecoder).Assembly.Location));
            writer.WriteString("primitivesAssemblySha256", await HashFile(typeof(SignatureBlobGuard).Assembly.Location));
            writer.WriteBoolean("productionCeilingsEnforced", true);
            writer.WriteString("measurementPopulation",
                "All decode attempts. Usage excludes refused charges; quantities include the first refused attempt. "
                + "Array quantities come from the guard, not provider callbacks.");
            writer.WriteStartArray("assemblies");
            foreach (JsonElement input in inputs)
            {
                TestContext.Current.CancellationToken.ThrowIfCancellationRequested();
                string tier = input.GetProperty("tier").GetString()!;
                string identity = input.GetProperty("identity").GetString()!;
                string path = Path.Combine(root, input.GetProperty("path").GetString()!);
                Assert.Equal(input.GetProperty("sha256").GetString(), await HashFile(path));
                if (!totals.TryGetValue(tier, out Totals? total))
                    totals.Add(tier, total = new());
                total.Assemblies++;
                using var file = File.OpenRead(path);
                using var image = new PEReader(file);
                Assert.IsType<MetadataImageFormatResult.SupportedEcma335>(
                    MetadataImageFormatClassifier.Classify(image));
                MetadataReader reader = image.GetMetadataReader(MetadataReaderOptions.None);
                writer.WriteStartObject();
                writer.WriteString("tier", tier);
                writer.WriteString("identity", identity);
                writer.WriteString("sha256", input.GetProperty("sha256").GetString());
                writer.WriteNumber("methods", reader.MethodDefinitions.Count);
                writer.WriteNumber("fields", reader.FieldDefinitions.Count);
                writer.WriteNumber("properties", reader.PropertyDefinitions.Count);
                long rejectedBefore = total.Rejected;
                writer.WriteStartArray("refusals");
                foreach (var member in reader.MethodDefinitions)
                    Decode(member);
                foreach (var member in reader.FieldDefinitions)
                    Decode(member);
                foreach (var member in reader.PropertyDefinitions)
                    Decode(member);
                writer.WriteEndArray();
                writer.WriteNumber("rejected", total.Rejected - rejectedBefore);
                writer.WriteEndObject();
                writer.Flush();

                void Decode(EntityHandle member)
                {
                    var metrics = new SignatureOccurrenceMetrics();
                    SignatureOccurrenceDecodeResult result = SignatureOccurrenceDecoder.Decode(image, member, metrics);
                    string example = $"{identity}#0x{MetadataTokens.GetToken(member):X8}";
                    total.Observe(metrics, example);
                    if (result is SignatureOccurrenceDecodeResult.Decoded)
                    {
                        total.Decoded++;
                        return;
                    }
                    var rejection = Assert.IsType<SignatureOccurrenceDecodeResult.Rejected>(result);
                    total.Rejected++;
                    writer.WriteStartObject();
                    writer.WriteString("token", $"0x{MetadataTokens.GetToken(member):X8}");
                    writer.WriteString("kind", member.Kind.ToString());
                    writer.WriteString("reason", rejection.Reason.ToString());
                    writer.WriteNumber("nodes", metrics.Nodes);
                    writer.WriteNumber("copies", metrics.Copies);
                    writer.WriteNumber("work", metrics.Work);
                    writer.WriteStartObject("quantities");
                    foreach (var quantity in Quantities)
                    {
                        SignatureOccurrenceMeasurement measurement = metrics[quantity];
                        writer.WriteStartObject(quantity.ToString());
                        writer.WriteNumber("count", measurement.Count);
                        writer.WriteNumber("total", measurement.Total);
                        writer.WriteNumber("largestCharge", measurement.LargestCharge);
                        writer.WriteEndObject();
                    }
                    writer.WriteEndObject();
                    writer.WriteEndObject();
                }
            }
            writer.WriteEndArray();
            writer.WriteStartObject("tiers");
            foreach (var (tier, total) in totals.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {
                writer.WriteStartObject(tier);
                JsonElement? previous = baseline?.RootElement.GetProperty("tiers").GetProperty(tier);
                if (previous is { } recorded)
                    Assert.Equal(recorded.GetProperty("decoded").GetInt64(), total.Decoded + total.Rejected);
                total.Write(writer, previous);
                writer.WriteEndObject();
            }
            writer.WriteEndObject();
            writer.WriteBoolean("complete", true);
            writer.WriteEndObject();
        }
        long rejected = totals.Values.Sum(total => total.Rejected);
        long decoded = totals.Values.Sum(total => total.Decoded);
        TestContext.Current.TestOutputHelper?.WriteLine(
            $"Contract v{SignatureOccurrenceCensusContract.CurrentVersion}: "
            + $"{inputs.Length} assemblies; {decoded} decoded; {rejected} rejected. Report: {reportPath}");
        Assert.True(rejected == 0, $"{rejected} signatures were rejected. Every refusal is retained in {reportPath}.");
    }

    [Fact]
    public void CensusStatistics_KeepRefusedAttemptsSeparateFromUsage()
    {
        var metrics = new SignatureOccurrenceMetrics();
        var budget = new SignatureOccurrenceWorkBudget(SignatureOccurrenceLimits.Default with { Work = 2 }, metrics);
        budget.Work(SignatureOccurrenceMetric.ModuleReferenceNameBytes, 2);
        Assert.Throws<SignatureOccurrenceRejectedException>(() =>
            budget.Work(SignatureOccurrenceMetric.ModuleReferenceNameBytes, 3));
        var total = new Totals();
        total.Observe(metrics, "fixture#0x04000001");
        Assert.Equal(2, total.Work.Maximum);
        var quantity = total.Quantities[(int)SignatureOccurrenceMetric.ModuleReferenceNameBytes];
        Assert.Equal(2, quantity.Charges);
        Assert.Equal(5, quantity.Total);
        Assert.Equal(3, quantity.LargestCharge);
        Assert.Equal(5, quantity.PerDecode.Maximum);
    }

    [Fact]
    public void CensusHistograms_ReportBucketUpperBounds()
    {
        var distribution = new Distribution();
        foreach (int value in new[] { 0, 1, 2, 3, 4 })
            distribution.Observe(value, "fixture");
        Assert.Equal(4, distribution.Maximum);
        Assert.Equal(3, distribution.PercentileUpperBound(0.5m));
        Assert.Equal(7, distribution.PercentileUpperBound(0.9999m));
    }

    static async Task<string> HashFile(string path)
    {
        await using var stream = File.OpenRead(path);
        return Convert.ToHexStringLower(await SHA256.HashDataAsync(stream, TestContext.Current.CancellationToken));
    }

    sealed class Totals
    {
        internal int Assemblies;
        internal long Decoded;
        internal long Rejected;
        internal readonly Distribution Nodes = new();
        internal readonly Distribution Copies = new();
        internal readonly Distribution Work = new();
        internal readonly QuantityTotals[] Quantities =
            SignatureOccurrenceCensusTests.Quantities.Select(_ => new QuantityTotals()).ToArray();

        internal void Observe(SignatureOccurrenceMetrics metrics, string example)
        {
            Nodes.Observe(metrics.Nodes, example);
            Copies.Observe(metrics.Copies, example);
            Work.Observe(metrics.Work, example);
            foreach (var quantity in SignatureOccurrenceCensusTests.Quantities)
                Quantities[(int)quantity].Observe(metrics[quantity], example);
        }

        internal void Write(Utf8JsonWriter writer, JsonElement? baseline)
        {
            writer.WriteNumber("assemblies", Assemblies);
            writer.WriteNumber("decoded", Decoded);
            writer.WriteNumber("rejected", Rejected);
            writer.WriteStartObject("budgets");
            JsonElement? budgets = baseline?.GetProperty("budgets");
            Nodes.Write(writer, "nodes", SignatureOccurrenceLimits.Default.Nodes,
                budgets?.GetProperty("nodes").GetProperty("maximum").GetInt64());
            Copies.Write(writer, "copies", SignatureOccurrenceLimits.Default.Copies,
                budgets?.GetProperty("copies").GetProperty("maximum").GetInt64());
            Work.Write(writer, "work", SignatureOccurrenceLimits.Default.Work,
                budgets?.GetProperty("work").GetProperty("maximum").GetInt64());
            writer.WriteEndObject();
            writer.WriteStartObject("quantities");
            foreach (var quantity in SignatureOccurrenceCensusTests.Quantities)
            {
                var measured = Quantities[(int)quantity];
                writer.WriteStartObject(quantity.ToString());
                writer.WriteNumber("charges", measured.Charges);
                writer.WriteNumber("total", measured.Total);
                writer.WriteNumber("largestCharge", measured.LargestCharge);
                measured.PerDecode.Write(writer, "perDecode");
                writer.WriteEndObject();
            }
            writer.WriteEndObject();
        }
    }

    sealed class QuantityTotals
    {
        internal long Charges;
        internal long Total;
        internal int LargestCharge;
        internal readonly Distribution PerDecode = new();

        internal void Observe(SignatureOccurrenceMeasurement measurement, string example)
        {
            Charges += measurement.Count;
            Total += measurement.Total;
            LargestCharge = Math.Max(LargestCharge, measurement.LargestCharge);
            PerDecode.Observe(measurement.Total, example);
        }
    }

    sealed class Distribution
    {
        readonly long[] _buckets = new long[64];
        long _count;
        internal long Maximum;
        string? _example;

        internal void Observe(long value, string example)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            _count++;
            _buckets[value == 0 ? 0 : BitOperations.Log2((ulong)value) + 1]++;
            if (value > Maximum)
            {
                Maximum = value;
                _example = example;
            }
        }

        internal long PercentileUpperBound(decimal percentile)
        {
            long target = (long)decimal.Ceiling(_count * percentile);
            long seen = 0;
            for (int i = 0; i < _buckets.Length; i++)
            {
                seen += _buckets[i];
                if (seen >= target)
                    return (long)((1UL << i) - 1);
            }
            throw new InvalidOperationException("Histogram population is inconsistent.");
        }

        internal void Write(
            Utf8JsonWriter writer, string name, int? ceiling = null, long? baselineMaximum = null)
        {
            writer.WriteStartObject(name);
            if (ceiling is { } limit)
                writer.WriteNumber("ceiling", limit);
            writer.WriteNumber("maximum", Maximum);
            if (baselineMaximum is { } previous)
            {
                writer.WriteNumber("baselineMaximum", previous);
                writer.WriteNumber("maximumDelta", Maximum - previous);
            }
            writer.WriteString("maximumExample", _example);
            writer.WriteNumber("p50BucketUpperBound", PercentileUpperBound(0.5m));
            writer.WriteNumber("p9999BucketUpperBound", PercentileUpperBound(0.9999m));
            writer.WriteStartArray("histogram");
            foreach (long bucket in _buckets)
                writer.WriteNumberValue(bucket);
            writer.WriteEndArray();
            writer.WriteEndObject();
        }
    }
}
