using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

if (args.Length != 4 || !int.TryParse(args[3], out int samples) || samples < 5)
{
    Console.Error.WriteLine("Usage: measure-type-lookup <base-apphost> <head-apphost> <scenario-json> <samples>");
    return 2;
}

string[] binaries = [Path.GetFullPath(args[0]), Path.GetFullPath(args[1])];
using JsonDocument scenarioDocument = JsonDocument.Parse(File.ReadAllText(args[2]));
Scenario[] scenarios =
[
    .. scenarioDocument.RootElement.EnumerateArray().Select(item => new Scenario(
        item.GetProperty("name").GetString()!,
        [.. item.GetProperty("arguments").EnumerateArray().Select(argument => argument.GetString()!)])),
];
foreach (string binary in binaries)
{
    Measurement flavor = await Run(binary, ["--flavor"]);
    if (flavor.ExitCode != 0 || !flavor.Output.StartsWith("NativeAOT;", StringComparison.Ordinal))
        throw new InvalidOperationException($"Expected a NativeAOT production apphost: {binary}");
    Measurement version = await Run(binary, ["--version"]);
    Console.WriteLine($"identity\t{binary}\t{version.Output.Trim()}\t{Hash(File.ReadAllBytes(binary))}");
}

Console.WriteLine("scenario\tbase_median_ms\tbase_p95_ms\thead_median_ms\thead_p95_ms\texit\toutput_sha256\toutput_lines\tmatched_type\tmembers");
foreach (Scenario scenario in scenarios)
{
    Measurement baseline = await Run(binaries[0], scenario.Arguments);
    for (int warmup = 0; warmup < 2; warmup++)
    {
        Verify(baseline, await Run(binaries[0], scenario.Arguments));
        Verify(baseline, await Run(binaries[1], scenario.Arguments));
    }

    List<double>[] timings = [[], []];
    for (int sample = 0; sample < samples; sample++)
    {
        for (int leg = 0; leg < 2; leg++)
        {
            int index = (sample + leg) % 2;
            Measurement observed = await Run(binaries[index], scenario.Arguments);
            Verify(baseline, observed);
            timings[index].Add(observed.Milliseconds);
        }
    }
    foreach (List<double> timing in timings)
        timing.Sort();

    string? matchedType = null;
    int? members = null;
    if (baseline.Output.StartsWith('{'))
    {
        using JsonDocument document = JsonDocument.Parse(baseline.Output);
        JsonElement content = document.RootElement;
        if (content.TryGetProperty("content", out JsonElement nested))
            content = nested;
        if (content.TryGetProperty("matchedType", out JsonElement name))
            matchedType = name.GetString();
        if (content.TryGetProperty("type", out JsonElement type)
            && type.ValueKind == JsonValueKind.Object)
            members = type.GetProperty("members").GetArrayLength();
    }
    Console.WriteLine(string.Join('\t', scenario.Name,
        Percentile(timings[0], 0.50), Percentile(timings[0], 0.95),
        Percentile(timings[1], 0.50), Percentile(timings[1], 0.95),
        baseline.ExitCode, Hash(Encoding.UTF8.GetBytes(baseline.Output)),
        baseline.Output.Count(character => character == '\n'), matchedType, members));
}
return 0;

static async Task<Measurement> Run(string binary, string[] arguments)
{
    var start = new ProcessStartInfo(binary)
    {
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
    };
    foreach (string argument in arguments)
        start.ArgumentList.Add(argument);
    var watch = Stopwatch.StartNew();
    using Process process = Process.Start(start)!;
    Task<string> output = process.StandardOutput.ReadToEndAsync();
    Task<string> error = process.StandardError.ReadToEndAsync();
    await process.WaitForExitAsync();
    await Task.WhenAll(output, error);
    watch.Stop();
    return new(watch.Elapsed.TotalMilliseconds, process.ExitCode, output.Result, error.Result);
}

static void Verify(Measurement expected, Measurement actual)
{
    if (expected.ExitCode != actual.ExitCode || expected.Output != actual.Output || expected.Error != actual.Error)
        throw new InvalidOperationException("Base/head output, diagnostic, or exit-code disagreement.");
}

static string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

static string Percentile(List<double> sorted, double fraction) =>
    sorted[(int)Math.Ceiling(sorted.Count * fraction) - 1].ToString("F3", CultureInfo.InvariantCulture);

sealed record Scenario(string Name, string[] Arguments);
sealed record Measurement(double Milliseconds, int ExitCode, string Output, string Error);
