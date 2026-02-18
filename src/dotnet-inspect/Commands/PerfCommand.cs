using System.Diagnostics;
using DotnetInspector.Core;
using DotnetInspector.Options;
using DotnetInspector.Packages;

namespace DotnetInspector.Commands;

/// <summary>
/// Hidden command for profiling the full package inspection path.
/// Runs bare-name resolution + inspection in a loop for dotnet-trace sampling.
/// </summary>
public static class PerfCommand
{
    public const string Name = "perf";

    public static async Task<int> ExecuteAsync(string packageName, int iterations)
    {
        // Warmup — ensure caches are populated
        var warmupOptions = new InspectionOptions
        {
            PackageArgs = [packageName],
            Verbosity = Verbosity.Quiet
        };
        await PackageCommand.ExecuteAsync(warmupOptions with { TipLevel = TipLevel.Quiet });

        Console.Error.WriteLine($"Package: {packageName}");
        Console.Error.WriteLine($"Iterations: {iterations}");
        Console.Error.WriteLine();

        // Timed loop — this is what dotnet-trace will sample
        var sw = Stopwatch.StartNew();
        var devNull = TextWriter.Null;

        for (int i = 0; i < iterations; i++)
        {
            var savedOut = Console.Out;
            Console.SetOut(devNull);
            try
            {
                var options = new InspectionOptions
                {
                    PackageArgs = [packageName],
                    Verbosity = Verbosity.Quiet,
                    TipLevel = TipLevel.Quiet
                };
                await PackageCommand.ExecuteAsync(options);
            }
            finally
            {
                Console.SetOut(savedOut);
            }
        }

        sw.Stop();

        double totalMs = sw.Elapsed.TotalMilliseconds;
        double perIterMs = totalMs / iterations;

        Console.Error.WriteLine();
        Console.Error.WriteLine($"Total: {totalMs:F1}ms");
        Console.Error.WriteLine($"Per iteration: {perIterMs:F3}ms");

        return 0;
    }
}
