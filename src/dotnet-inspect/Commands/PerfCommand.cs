using System.Diagnostics;
using DotnetInspector.Core;
using DotnetInspector.Options;
using DotnetInspector.Packages;
using DotnetInspector.Services;

namespace DotnetInspector.Commands;

/// <summary>
/// Hidden command for profiling various code paths.
/// Runs operations in a loop for dotnet-trace sampling.
/// </summary>
public static class PerfCommand
{
    public const string Name = "perf";

    public enum Mode
    {
        Package,    // Full package inspection (default)
        Version,    // Version lookups (--versions path)
        Library,    // Platform library access
        Type        // Type listing
    }

    public static async Task<int> ExecuteAsync(string target, int iterations, Mode mode, bool skipWarmup)
    {
        Console.Error.WriteLine($"Mode: {mode}");
        Console.Error.WriteLine($"Target: {target}");
        Console.Error.WriteLine($"Iterations: {iterations}");
        Console.Error.WriteLine($"Skip warmup: {skipWarmup}");
        Console.Error.WriteLine();

        // Warmup — ensure caches are populated (unless testing cold start)
        if (!skipWarmup)
        {
            Console.Error.WriteLine("Warming up...");
            await RunIterationAsync(target, mode);
            Console.Error.WriteLine();
        }

        // Timed loop — this is what dotnet-trace will sample
        var sw = Stopwatch.StartNew();
        var devNull = TextWriter.Null;

        for (int i = 0; i < iterations; i++)
        {
            var savedOut = Console.Out;
            Console.SetOut(devNull);
            try
            {
                await RunIterationAsync(target, mode);
            }
            finally
            {
                Console.SetOut(savedOut);
            }
        }

        sw.Stop();

        double totalMs = sw.Elapsed.TotalMilliseconds;
        double perIterMs = totalMs / iterations;

        Console.Error.WriteLine($"Total: {totalMs:F1}ms");
        Console.Error.WriteLine($"Per iteration: {perIterMs:F3}ms");

        return 0;
    }

    private static async Task RunIterationAsync(string target, Mode mode)
    {
        switch (mode)
        {
            case Mode.Package:
                await RunPackageAsync(target);
                break;
            case Mode.Version:
                await RunVersionAsync(target);
                break;
            case Mode.Library:
                await RunLibraryAsync(target);
                break;
            case Mode.Type:
                await RunTypeAsync(target);
                break;
        }
    }

    private static async Task RunPackageAsync(string packageName)
    {
        var options = new InspectionOptions
        {
            PackageArgs = [packageName],
            Verbosity = Verbosity.Quiet,
            TipLevel = TipLevel.Quiet
        };
        await PackageCommand.ExecuteAsync(options);
    }

    private static async Task RunVersionAsync(string packageName)
    {
        var versions = await PackageExtractor.GetVersionsAsync(
            HttpClientFactory.Shared,
            packageName,
            includePrerelease: false,
            limit: null,
            log: null);
        // Consume to ensure work is done
        _ = versions?.Count ?? 0;
    }

    private static async Task RunLibraryAsync(string platformLibrary)
    {
        var options = new AssemblyOptions
        {
            PlatformAssembly = platformLibrary,
            IncludeMetadata = true,
            Verbosity = Verbosity.Quiet
        };
        await AssemblyCommand.ExecuteAsync(options);
    }

    private static async Task RunTypeAsync(string target)
    {
        // Parse target as either platform library or package
        bool isPlatform = PlatformResolver.IsPlatformCandidate(target);

        var options = new TypeOptions
        {
            PlatformAssembly = isPlatform ? target : null,
            PackagePath = isPlatform ? null : target,
            TypeName = null, // List all types
            Verbosity = Verbosity.Quiet,
            TipLevel = TipLevel.Quiet
        };
        await TypeCommand.ExecuteAsync(options);
    }
}
