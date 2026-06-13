using System.Diagnostics;
using ILInspector.Metadata;

namespace DotnetInspector.Commands;

/// <summary>
/// Hidden command for profiling the hot path of assembly metadata reading.
/// Runs the core logic in a tight loop so dotnet-trace can collect meaningful samples.
/// </summary>
public static class PerfTestCommand
{
    public const string Name = "perf-test";

    public static int Execute(string assemblyPath, int iterations, bool typesOnly)
    {
        if (!File.Exists(assemblyPath))
        {
            Console.Error.WriteLine($"File not found: {assemblyPath}");
            return 1;
        }

        // Warmup
        var api = AssemblyReader.ExtractApiSurface(assemblyPath, includeAll: false, typesOnly: typesOnly);
        if (api == null)
        {
            Console.Error.WriteLine($"Could not read metadata from: {assemblyPath}");
            return 1;
        }

        Console.Error.WriteLine($"Assembly: {Path.GetFileName(assemblyPath)}");
        Console.Error.WriteLine($"Types: {api.Types.Count}");
        Console.Error.WriteLine($"Iterations: {iterations}");
        Console.Error.WriteLine($"Mode: {(typesOnly ? "types-only" : "full")}");
        Console.Error.WriteLine();

        // Timed loop — this is what dotnet-trace will sample
        var sw = Stopwatch.StartNew();
        int typeCount = 0;

        for (int i = 0; i < iterations; i++)
        {
            var surface = AssemblyReader.ExtractApiSurface(assemblyPath, includeAll: false, typesOnly: typesOnly);
            typeCount += surface!.Types.Count;
        }

        sw.Stop();

        double totalMs = sw.Elapsed.TotalMilliseconds;
        double perIterMs = totalMs / iterations;

        Console.Error.WriteLine($"Total: {totalMs:F1}ms");
        Console.Error.WriteLine($"Per iteration: {perIterMs:F3}ms");
        Console.Error.WriteLine($"Types processed: {typeCount}");

        return 0;
    }
}
