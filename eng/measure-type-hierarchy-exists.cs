#:project ../src/DotnetInspect.Cli/DotnetInspect.Cli.csproj
#:property IsPublishable=true
#:property PublishAot=true
#:property OptimizationPreference=Speed
#:property InvariantGlobalization=true
#:property DotnetInspectRuntimeFlavor=true

// Measures the type command's hierarchy terminals in process, as the
// NativeAOT production host runs them (#10616): effective discovery of
// Implementers and Derived Types, which the candidate decides through the
// hierarchy operation's Exists closing and the base lists unprobed, beside
// the Count and Rows terminals of the same sections. Every scenario runs the
// complete command, including planning, Workspace admission, execution, and
// output, with stdout discarded.
//
//   measure-type-hierarchy-exists --identity
//   measure-type-hierarchy-exists [--samples N] [--warmup N] [--only name,...]
//
// Platform scenarios resolve the installed shared framework through
// DOTNET_ROOT; set it to a root whose shared/Microsoft.NETCore.App holds the
// pinned runtime before running.

using System.CommandLine;
using System.Diagnostics;
using System.Globalization;
using System.Text;

using DotnetInspect.Cli;
using DotnetInspector.Packages;

if (args is ["--identity"])
{
    Console.WriteLine($"{VersionInfo.Version};{VersionInfo.Flavor}");
    return 0;
}

int samples = 30;
int warmup = 3;
HashSet<string>? only = null;
for (int index = 0; index < args.Length; index++)
{
    switch (args[index])
    {
        case "--samples" when index + 1 < args.Length
            && int.TryParse(args[++index], NumberStyles.None, CultureInfo.InvariantCulture, out int parsedSamples)
            && parsedSamples > 0:
            samples = parsedSamples;
            break;
        case "--warmup" when index + 1 < args.Length
            && int.TryParse(args[++index], NumberStyles.None, CultureInfo.InvariantCulture, out int parsedWarmup):
            warmup = parsedWarmup;
            break;
        case "--only" when index + 1 < args.Length:
            only = new HashSet<string>(args[++index].Split(','), StringComparer.Ordinal);
            break;
        default:
            Console.Error.WriteLine(
                "Usage: measure-type-hierarchy-exists [--samples N] [--warmup N] [--only name,...]");
            return 2;
    }
}

if (VersionInfo.Flavor != "NativeAOT")
{
    Console.Error.WriteLine($"Expected NativeAOT; got {VersionInfo.Flavor}.");
    return 2;
}

Console.OutputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
NuGetCache.Initialize("dotnet-inspect");

// Scenario names: <type>.<terminal>. "exists" is effective discovery of the
// one hierarchy section, which the candidate answers through the Exists
// closing; "count" and "rows1" are the same section's Count and one-row Rows
// terminals; "catalog" is bare effective discovery of the whole Type.
(string Name, string[] Arguments)[] scenarios =
[
    ("IDisposable.implementers.exists",
        ["type", "System.IDisposable", "--platform", "-D", "Implementers", "--effective"]),
    ("IDisposable.implementers.count",
        ["type", "System.IDisposable", "--platform", "-S", "Implementers", "--count"]),
    ("IDisposable.implementers.rows1",
        ["type", "System.IDisposable", "--platform", "-S", "Implementers", "--rows", "1"]),
    ("Stream.derived.exists",
        ["type", "System.IO.Stream", "--platform", "-D", "Derived Types", "--effective"]),
    ("Stream.derived.count",
        ["type", "System.IO.Stream", "--platform", "-S", "Derived Types", "--count"]),
    ("Stream.derived.rows1",
        ["type", "System.IO.Stream", "--platform", "-S", "Derived Types", "--rows", "1"]),
    ("String.derived.exists",
        ["type", "System.String", "--platform", "-D", "Derived Types", "--effective"]),
    ("String.derived.count",
        ["type", "System.String", "--platform", "-S", "Derived Types", "--count"]),
    ("IDisposable.catalog",
        ["type", "System.IDisposable", "--platform", "-D", "--effective"]),
    ("String.catalog",
        ["type", "System.String", "--platform", "-D", "--effective"]),
];
if (only is not null)
    scenarios = [.. scenarios.Where(scenario => only.Contains(scenario.Name))];

TextWriter console = Console.Out;
TextWriter errors = Console.Error;

// One untimed run per scenario records the answer the timed runs repeat.
Console.Error.WriteLine(
    string.Join('\t', "identity", VersionInfo.Version, VersionInfo.Flavor));
foreach ((string name, string[] arguments) in scenarios)
{
    var output = new StringWriter();
    var error = new StringWriter();
    int exitCode = await InvokeAsync(arguments, output, error);
    string firstLines = string.Join(
        " | ",
        output.ToString()
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Take(6));
    errors.WriteLine(
        string.Join('\t', "answer", name, exitCode, firstLines, error.ToString().Trim().Replace('\n', ' ')));
}

for (int round = 0; round < warmup; round++)
{
    foreach ((_, string[] arguments) in scenarios)
        await InvokeAsync(arguments, TextWriter.Null, TextWriter.Null);
}

var timings = new Dictionary<string, List<double>>(StringComparer.Ordinal);
var allocations = new Dictionary<string, List<long>>(StringComparer.Ordinal);
foreach ((string name, _) in scenarios)
{
    timings[name] = new List<double>(samples);
    allocations[name] = new List<long>(samples);
}

// Rotated rounds: each round starts the scenario list at a different offset
// so no scenario always follows the same predecessor.
for (int sample = 0; sample < samples; sample++)
{
    for (int offset = 0; offset < scenarios.Length; offset++)
    {
        (string name, string[] arguments) = scenarios[(sample + offset) % scenarios.Length];
        long before = GC.GetTotalAllocatedBytes(precise: true);
        long start = Stopwatch.GetTimestamp();
        int exitCode = await InvokeAsync(arguments, TextWriter.Null, TextWriter.Null);
        double elapsed = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        long allocated = GC.GetTotalAllocatedBytes(precise: true) - before;
        if (exitCode != 0)
        {
            errors.WriteLine($"exit\t{name}\t{exitCode}");
            return 1;
        }
        timings[name].Add(elapsed);
        allocations[name].Add(allocated);
    }
}

errors.WriteLine("scenario\tsamples\tmedian_ms\tp95_ms\tmin_ms\tmedian_mb");
foreach ((string name, _) in scenarios)
{
    List<double> times = timings[name];
    List<long> bytes = allocations[name];
    times.Sort();
    bytes.Sort();
    errors.WriteLine(
        string.Join(
            '\t',
            name,
            times.Count,
            Percentile(times, 0.50).ToString("F1", CultureInfo.InvariantCulture),
            Percentile(times, 0.95).ToString("F1", CultureInfo.InvariantCulture),
            times[0].ToString("F1", CultureInfo.InvariantCulture),
            (Percentile(bytes, 0.50) / 1_048_576.0).ToString("F1", CultureInfo.InvariantCulture)));
}

return 0;

static async Task<int> InvokeAsync(string[] commandArguments, TextWriter output, TextWriter error)
{
    TextWriter previousOut = Console.Out;
    TextWriter previousError = Console.Error;
    Console.SetOut(output);
    Console.SetError(error);
    try
    {
        RootCommand root = CommandLineBuilder.CreateRootCommand();
        ParseResult result = root.Parse(commandArguments);
        return await CommandLineBuilder.InvokeWithLineWindowAsync(result, commandArguments);
    }
    finally
    {
        Console.SetOut(previousOut);
        Console.SetError(previousError);
    }
}

static T Percentile<T>(IReadOnlyList<T> sorted, double percentile)
{
    int index = Math.Min(sorted.Count - 1, (int)Math.Ceiling(sorted.Count * percentile) - 1);
    return sorted[Math.Max(0, index)];
}
