#:project ../src/DotnetInspector.PackageQueries/DotnetInspector.PackageQueries.csproj
#:property EnablePreviewFeatures=true

using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using DotnetInspector.PackageQueries;

const string Usage =
    "Usage:\n"
    + "  dotnet run tools/PackageAssemblyQueryBenchmark.cs -c Release -- --self-test\n"
    + "  dotnet run tools/PackageAssemblyQueryBenchmark.cs -c Release -- "
    + "<revision> <cli-dll> <cold|warm> <trials> <output-json>";
BenchmarkJsonContext json = BenchmarkJsonContext.Default;

if (args is ["--emit-self-test"])
{
    Console.Write(
        """{"candidates":[{"package":"probe","version":"1.0.0","outcome":"no-match","asset":"lib/net8.0/Probe.dll","target_framework":"net8.0","detail":"complete"}],"matches":[]}""");
    await Task.Delay(30);
    return 0;
}

if (args is ["--self-test"])
{
    await SelfTestAsync(json);
    Console.WriteLine("Package assembly-query benchmark self-test passed.");
    return 0;
}

if (args.Length != 5
    || args[0].Length == 0
    || !File.Exists(args[1])
    || args[2] is not ("cold" or "warm")
    || !int.TryParse(args[3], out int trials)
    || trials is < 1 or > 20)
{
    Console.Error.WriteLine(Usage);
    return 1;
}

string revision = args[0];
string cliPath = Path.GetFullPath(args[1]);
string cacheState = args[2];
string outputPath = Path.GetFullPath(args[4]);
string manifestPath = Path.Combine(
    Directory.GetCurrentDirectory(),
    "tools",
    "PackageAssemblyQueryBenchmark.json");
BenchmarkManifest manifest =
    JsonSerializer.Deserialize<BenchmarkManifest>(
        await File.ReadAllTextAsync(manifestPath),
        json.BenchmarkManifest)
    ?? throw new InvalidOperationException("The benchmark manifest is empty.");
ValidateManifest(manifest);

string expectedJson = JsonSerializer.Serialize(
    manifest.Expected,
    json.SemanticProjection);
string expectedFingerprint = Fingerprint(expectedJson);
string scratchRoot = Directory.CreateTempSubdirectory(
    "inspect-package-query-benchmark-").FullName;
var samples = new List<BenchmarkSample>();
try
{
    string warmCache = Path.Combine(scratchRoot, "warm");
    if (cacheState == "warm")
    {
        _ = await RunCliAsync(
            cliPath,
            manifest,
            warmCache,
            trial: 0,
            expectedFingerprint,
            json);
    }

    for (int trial = 1; trial <= trials; trial++)
    {
        string cacheRoot = cacheState == "warm"
            ? warmCache
            : Path.Combine(scratchRoot, $"cold-{trial}");
        samples.Add(
            await RunCliAsync(
                cliPath,
                manifest,
                cacheRoot,
                trial,
                expectedFingerprint,
                json));
    }
}
finally
{
    Directory.Delete(scratchRoot, recursive: true);
}

var report = new BenchmarkReport(
    SchemaVersion: 1,
    TimestampUtc: DateTimeOffset.UtcNow,
    Revision: revision,
    ScenarioId: manifest.ScenarioId,
    CacheState: cacheState,
    Host: Environment.MachineName,
    OS: RuntimeInformation.OSDescription,
    Architecture: RuntimeInformation.OSArchitecture.ToString(),
    ProcessorCount: Environment.ProcessorCount,
    Framework: RuntimeInformation.FrameworkDescription,
    SemanticFingerprint: expectedFingerprint,
    Samples: samples,
    Summary: Summarize(samples));
Directory.CreateDirectory(
    Path.GetDirectoryName(outputPath)
        ?? throw new InvalidOperationException("The output path has no directory."));
await File.WriteAllTextAsync(
    outputPath,
    JsonSerializer.Serialize(report, json.BenchmarkReport) + Environment.NewLine);
Console.WriteLine(outputPath);
return 0;

static async Task<BenchmarkSample> RunCliAsync(
    string cliPath,
    BenchmarkManifest manifest,
    string cacheRoot,
    int trial,
    string expectedFingerprint,
    BenchmarkJsonContext json)
{
    Directory.CreateDirectory(cacheRoot);
    var arguments = new List<string>
    {
        cliPath,
        "find",
        "--literal",
        manifest.Literal,
    };
    foreach (string package in manifest.Packages)
    {
        arguments.Add("--package");
        arguments.Add(package);
    }
    arguments.Add("--tfm");
    arguments.Add(manifest.TargetFramework);
    arguments.Add("-v:n");
    arguments.Add("--json");
    arguments.Add("--info");
    arguments.Add("--no-nuget-cache");

    ChildMeasurement measured = await RunChildAsync(
        DotnetHostPath(),
        arguments,
        cacheRoot);
    if (measured.ExitCode != 0)
    {
        throw new InvalidOperationException(
            $"Package Query trial {trial} failed with exit code {measured.ExitCode}: "
            + measured.StandardError.Trim());
    }

    SemanticProjection projection =
        JsonSerializer.Deserialize(
            measured.StandardOutput,
            json.SemanticProjection)
        ?? throw new InvalidOperationException(
            $"Package Query trial {trial} returned an empty document.");
    ProjectionSummary validated = ValidateProjection(
        projection,
        expectedFingerprint,
        manifest.Packages.Count,
        trial,
        json);
    int requests = ParseRequestCount(measured.StandardError);

    return new(
        trial,
        measured.ElapsedMilliseconds,
        measured.CpuMilliseconds,
        measured.PeakWorkingSetBytes,
        validated.Candidates,
        validated.Matches,
        validated.SemanticMisses,
        validated.NotApplicable,
        validated.Failures,
        requests,
        validated.Candidates / (measured.ElapsedMilliseconds / 1000d),
        validated.Fingerprint);
}

static ProjectionSummary ValidateProjection(
    SemanticProjection projection,
    string expectedFingerprint,
    int expectedCandidates,
    int trial,
    BenchmarkJsonContext json)
{
    string fingerprint = Fingerprint(
        JsonSerializer.Serialize(projection, json.SemanticProjection));
    if (fingerprint != expectedFingerprint)
    {
        throw new InvalidOperationException(
            $"Package Query trial {trial} returned semantic fingerprint {fingerprint}; "
            + $"expected {expectedFingerprint}.");
    }

    int candidates = projection.Candidates.Count;
    int matched = projection.Candidates.Count(
        static candidate => candidate.Outcome == "matched");
    int misses = projection.Candidates.Count(
        static candidate => candidate.Outcome == "no-match");
    int notApplicable = projection.Candidates.Count(
        static candidate => candidate.Outcome == "not-applicable");
    int failures = candidates - matched - misses - notApplicable;
    if (candidates != expectedCandidates || failures != 0)
    {
        throw new InvalidOperationException(
            $"Package Query trial {trial} completed {candidates} of "
            + $"{expectedCandidates} candidates with {failures} failures.");
    }
    return new(
        candidates,
        matched,
        misses,
        notApplicable,
        failures,
        fingerprint);
}

static int ParseRequestCount(string standardError)
{
    foreach (string line in standardError.Split('\n'))
    {
        if (!line.StartsWith("| HTTP |", StringComparison.Ordinal))
            continue;
        string[] cells = line.Split('|', StringSplitOptions.TrimEntries);
        string[] value = cells[2].Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (value.Length == 2
            && value[1] == "requests"
            && int.TryParse(value[0], out int requests))
        {
            return requests;
        }
    }
    throw new InvalidOperationException(
        "The CLI did not report its --info HTTP request count.");
}

static async Task<ChildMeasurement> RunChildAsync(
    string executable,
    IReadOnlyList<string> arguments,
    string cacheRoot)
{
    var startInfo = new ProcessStartInfo(executable)
    {
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
    };
    foreach (string argument in arguments)
        startInfo.ArgumentList.Add(argument);
    startInfo.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
    startInfo.Environment["DOTNET_NOLOGO"] = "1";
    startInfo.Environment["DOTNET_INSPECT_CACHE_DIR"] =
        Path.Combine(cacheRoot, "product");
    startInfo.Environment["NUGET_PACKAGES"] = Path.Combine(cacheRoot, "nuget");

    using var process = new Process { StartInfo = startInfo };
    long started = Stopwatch.GetTimestamp();
    if (!process.Start())
        throw new InvalidOperationException($"Could not start {executable}.");
    Task<string> stdout = process.StandardOutput.ReadToEndAsync();
    Task<string> stderr = process.StandardError.ReadToEndAsync();
    long peakWorkingSet = 0;
    double cpuMilliseconds = 0;
    while (!process.HasExited)
    {
        ObserveProcess(process, ref peakWorkingSet, ref cpuMilliseconds);
        await Task.Delay(5);
    }
    await process.WaitForExitAsync();
    ObserveProcess(process, ref peakWorkingSet, ref cpuMilliseconds);
    return new(
        process.ExitCode,
        await stdout,
        await stderr,
        Stopwatch.GetElapsedTime(started).TotalMilliseconds,
        cpuMilliseconds,
        peakWorkingSet);
}

static string DotnetHostPath()
{
    string? host = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH");
    if (!string.IsNullOrWhiteSpace(host) && File.Exists(host))
        return host;

    string? root = Environment.GetEnvironmentVariable("DOTNET_ROOT");
    string executable = OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet";
    string rooted = Path.Combine(root ?? "", executable);
    if (!string.IsNullOrWhiteSpace(root) && File.Exists(rooted))
        return rooted;

    throw new InvalidOperationException(
        "DOTNET_HOST_PATH or DOTNET_ROOT must identify the dotnet host.");
}

static void ObserveProcess(
    Process process,
    ref long peakWorkingSet,
    ref double cpuMilliseconds)
{
    try
    {
        process.Refresh();
        peakWorkingSet = Math.Max(
            peakWorkingSet,
            Math.Max(process.WorkingSet64, process.PeakWorkingSet64));
        cpuMilliseconds = Math.Max(
            cpuMilliseconds,
            process.TotalProcessorTime.TotalMilliseconds);
    }
    catch (InvalidOperationException) when (process.HasExited)
    {
        // The process may exit between HasExited and reading live counters.
    }
}

static void ValidateManifest(BenchmarkManifest manifest)
{
    if (manifest.SchemaVersion != 1
        || manifest.Packages.Count != PackageAssemblyQuery.MaximumPackages
        || manifest.Expected.Candidates.Count != manifest.Packages.Count)
    {
        throw new InvalidOperationException(
            "The benchmark manifest must use schema 1 and the complete five-package product limit.");
    }

    PackageAssemblyEvaluationBudget budget = PackageAssemblyEvaluationBudget.Default;
    var actual = new BenchmarkLimits(
        PackageAssemblyQuery.MaximumPackages,
        budget.MaximumEntryBytes,
        budget.MaximumRetainedImageBytes,
        budget.SemanticBudget.MaximumMethods,
        budget.SemanticBudget.MaximumMethodBodyBytes,
        budget.SemanticBudget.MaximumMethodBodyBytesVisited,
        budget.SemanticBudget.MaximumInstructions,
        budget.SemanticBudget.MaximumDecodedUserStringCharacters,
        budget.SemanticBudget.MaximumOccurrences,
        (int)budget.MaximumDuration.TotalSeconds);
    if (actual != manifest.Limits)
    {
        throw new InvalidOperationException(
            "The pinned benchmark limits no longer match the production defaults.");
    }
}

static BenchmarkSummary Summarize(IReadOnlyList<BenchmarkSample> samples) =>
    new(
        Statistics(samples.Select(static sample => sample.ElapsedMilliseconds)),
        Statistics(samples.Select(static sample => sample.CpuMilliseconds)),
        Statistics(samples.Select(static sample => (double)sample.PeakWorkingSetBytes)),
        Statistics(samples.Select(static sample => (double)sample.HttpRequests)),
        Statistics(samples.Select(static sample => sample.CandidatesPerSecond)));

static MetricSummary Statistics(IEnumerable<double> values)
{
    double[] ordered = [.. values.Order()];
    int middle = ordered.Length / 2;
    double median = ordered.Length % 2 == 0
        ? (ordered[middle - 1] + ordered[middle]) / 2d
        : ordered[middle];
    int p95 = Math.Max(0, (int)Math.Ceiling(ordered.Length * 0.95) - 1);
    return new(
        median,
        ordered.Average(),
        ordered[0],
        ordered[^1],
        ordered[p95]);
}

static string Fingerprint(string value) =>
    Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

static async Task SelfTestAsync(BenchmarkJsonContext json)
{
    var expected = new SemanticProjection(
        [
            new(
                "probe",
                "1.0.0",
                "no-match",
                "lib/net8.0/Probe.dll",
                "net8.0",
                "complete"),
        ],
        []);
    string source = Path.Combine(
        Directory.GetCurrentDirectory(),
        "tools",
        "PackageAssemblyQueryBenchmark.cs");
    string scratchRoot = Directory.CreateTempSubdirectory(
        "inspect-package-query-benchmark-self-test-").FullName;
    try
    {
        ChildMeasurement measured = await RunChildAsync(
            DotnetHostPath(),
            ["run", source, "-c", "Release", "--", "--emit-self-test"],
            scratchRoot);
        if (measured.ExitCode != 0)
        {
            throw new InvalidOperationException(
                "The fake child process failed: "
                + measured.StandardError.Trim());
        }
        SemanticProjection actual =
            JsonSerializer.Deserialize(
                measured.StandardOutput,
                json.SemanticProjection)
            ?? throw new InvalidOperationException("The fake child returned no projection.");
        string expectedFingerprint = Fingerprint(
            JsonSerializer.Serialize(expected, json.SemanticProjection));
        ProjectionSummary projection = ValidateProjection(
            actual,
            expectedFingerprint,
            expectedCandidates: 1,
            trial: 1,
            json);
        if (measured.ElapsedMilliseconds <= 0
            || measured.CpuMilliseconds < 0
            || measured.PeakWorkingSetBytes <= 0
            || projection != new ProjectionSummary(
                1,
                0,
                1,
                0,
                0,
                expectedFingerprint))
        {
            throw new InvalidOperationException(
                "The fake child process did not produce one complete measured projection.");
        }

        bool mismatchRejected = false;
        try
        {
            ValidateProjection(
                actual with
                {
                    Candidates =
                    [
                        actual.Candidates[0] with { Detail = "different" },
                    ],
                },
                expectedFingerprint,
                expectedCandidates: 1,
                trial: 2,
                json);
        }
        catch (InvalidOperationException)
        {
            mismatchRejected = true;
        }
        if (!mismatchRejected)
            throw new InvalidOperationException("A semantic mismatch was accepted.");

        MetricSummary summary = Statistics([3, 1, 2, 5, 4]);
        if (summary != new MetricSummary(3, 3, 1, 5, 5))
            throw new InvalidOperationException("Benchmark summary statistics are incorrect.");
        if (ParseRequestCount("# Info\n| HTTP | 5 requests |\n") != 5)
            throw new InvalidOperationException("CLI HTTP metrics were not parsed.");
    }
    finally
    {
        Directory.Delete(scratchRoot, recursive: true);
    }
}

sealed record BenchmarkManifest(
    int SchemaVersion,
    string ScenarioId,
    string Literal,
    string TargetFramework,
    List<string> Packages,
    BenchmarkLimits Limits,
    SemanticProjection Expected);

sealed record BenchmarkLimits(
    int MaximumPackages,
    long MaximumEntryBytes,
    long MaximumRetainedImageBytes,
    int MaximumMethods,
    int MaximumMethodBodyBytes,
    long MaximumMethodBodyBytesVisited,
    long MaximumInstructions,
    long MaximumDecodedUserStringCharacters,
    int MaximumOccurrences,
    int MaximumDurationSeconds);

sealed record SemanticProjection(
    [property: JsonPropertyName("candidates")]
    List<CandidateProjection> Candidates,
    [property: JsonPropertyName("matches")]
    List<MatchProjection> Matches);

sealed record CandidateProjection(
    [property: JsonPropertyName("package")] string Package,
    [property: JsonPropertyName("version")] string Version,
    [property: JsonPropertyName("outcome")] string Outcome,
    [property: JsonPropertyName("asset")] string Asset,
    [property: JsonPropertyName("target_framework")] string TargetFramework,
    [property: JsonPropertyName("detail")] string Detail);

sealed record MatchProjection(
    [property: JsonPropertyName("package")] string Package,
    [property: JsonPropertyName("version")] string Version,
    [property: JsonPropertyName("assembly")] string Assembly,
    [property: JsonPropertyName("method")] string Method,
    [property: JsonPropertyName("offset")] string Offset,
    [property: JsonPropertyName("literal")] string Literal);

readonly record struct ChildMeasurement(
    int ExitCode,
    string StandardOutput,
    string StandardError,
    double ElapsedMilliseconds,
    double CpuMilliseconds,
    long PeakWorkingSetBytes);

readonly record struct ProjectionSummary(
    int Candidates,
    int Matches,
    int SemanticMisses,
    int NotApplicable,
    int Failures,
    string Fingerprint);

sealed record BenchmarkSample(
    int Trial,
    double ElapsedMilliseconds,
    double CpuMilliseconds,
    long PeakWorkingSetBytes,
    int Candidates,
    int Matches,
    int SemanticMisses,
    int NotApplicable,
    int Failures,
    int HttpRequests,
    double CandidatesPerSecond,
    string SemanticFingerprint);

sealed record BenchmarkReport(
    int SchemaVersion,
    DateTimeOffset TimestampUtc,
    string Revision,
    string ScenarioId,
    string CacheState,
    string Host,
    string OS,
    string Architecture,
    int ProcessorCount,
    string Framework,
    string SemanticFingerprint,
    List<BenchmarkSample> Samples,
    BenchmarkSummary Summary);

sealed record BenchmarkSummary(
    MetricSummary ElapsedMilliseconds,
    MetricSummary CpuMilliseconds,
    MetricSummary PeakWorkingSetBytes,
    MetricSummary HttpRequests,
    MetricSummary CandidatesPerSecond);

readonly record struct MetricSummary(
    double Median,
    double Mean,
    double Minimum,
    double Maximum,
    double P95);

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = true)]
[JsonSerializable(typeof(BenchmarkManifest))]
[JsonSerializable(typeof(SemanticProjection))]
[JsonSerializable(typeof(BenchmarkReport))]
partial class BenchmarkJsonContext : JsonSerializerContext;
