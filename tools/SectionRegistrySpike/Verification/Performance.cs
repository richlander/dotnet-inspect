using System.Diagnostics;
using DotnetInspector.Options;
using SectionRegistrySpike.Capabilities;
using SectionRegistrySpike.CurrentBaseline;
using SectionRegistrySpike.Sections;

namespace SectionRegistrySpike.Verification;

public static class Performance
{
    private const int SampleCount = 9;
    private static int s_sink;

    public static string Run()
    {
        var currentPipeline = CurrentBaselinePipelines.CreatePipeline();
        var currentScanners = CurrentBaselinePipelines.CreateScannerRegistry();
        var typed = SpikeSections.Registry;

        string[] metadataSelection = ["Metadata"];
        string[] sharedSelection = ["Calls", "Facts"];
        string[] arbitrarySelection = ["Metadata", "Facts"];
        var metadataInclude = new HashSet<string>(metadataSelection, StringComparer.Ordinal);
        var sharedInclude = new HashSet<string>(sharedSelection, StringComparer.Ordinal);
        var arbitraryInclude = new HashSet<string>(arbitrarySelection, StringComparer.Ordinal);
        var currentMetadataPlan = currentPipeline.GetRequiredScanners(Verbosity.Quiet, metadataInclude);
        var currentSharedPlan = currentPipeline.GetRequiredScanners(Verbosity.Quiet, sharedInclude);
        var typedMetadataPlan = typed.PlanFor(metadataSelection);
        var typedSharedPlan = typed.PlanFor(sharedSelection);
        var currentMetadataContext = new CurrentScannerContext { Model = new SpikeModel() };
        var currentSharedContext = new CurrentScannerContext { Model = new SpikeModel() };
        var typedMetadataContext = new SpikeContext { Model = new SpikeModel() };
        var typedSharedContext = new SpikeContext { Model = new SpikeModel() };

        var rows = new[]
        {
            Compare(
                "Registry acquisition",
                200_000,
                () =>
                {
                    var pipeline = CurrentBaselinePipelines.CreatePipeline();
                    var scanners = CurrentBaselinePipelines.CreateScannerRegistry();
                    s_sink = pipeline.GetHashCode() + scanners.GetHashCode();
                },
                () =>
                {
                    var registry = SpikeSections.Registry;
                    s_sink = registry.Pipeline.GetHashCode() + registry.GetHashCode();
                }),
            Compare(
                "Plan one section",
                200_000,
                () => s_sink = currentPipeline.GetRequiredScanners(Verbosity.Quiet, metadataInclude).Count,
                () => s_sink = typed.PlanFor(metadataSelection).Count),
            Compare(
                "Plan shared sections",
                100_000,
                () => s_sink = currentPipeline.GetRequiredScanners(Verbosity.Quiet, sharedInclude).Count,
                () => s_sink = typed.PlanFor(sharedSelection).Count),
            Compare(
                "Plan arbitrary sections (cold)",
                50_000,
                () => s_sink = currentPipeline.GetRequiredScanners(Verbosity.Quiet, arbitraryInclude).Count,
                () => s_sink = typed.PlanFor(arbitrarySelection).Count),
            Compare(
                "Execute one section",
                200_000,
                () =>
                {
                    currentMetadataContext.Reset();
                    currentScanners.RunScanners(currentMetadataPlan, currentMetadataContext);
                    s_sink = currentMetadataContext.WorkCount;
                },
                () =>
                {
                    typedMetadataContext.Reset();
                    typedMetadataPlan.ExecuteAsync(typedMetadataContext, CapabilityExecutionModes.Explicit)
                        .GetAwaiter().GetResult();
                    s_sink = typedMetadataContext.WorkCount;
                }),
            Compare(
                "Execute shared sections",
                200_000,
                () =>
                {
                    currentSharedContext.Reset();
                    currentScanners.RunScanners(currentSharedPlan, currentSharedContext);
                    s_sink = currentSharedContext.WorkCount;
                },
                () =>
                {
                    typedSharedContext.Reset();
                    typedSharedPlan.ExecuteAsync(typedSharedContext, CapabilityExecutionModes.Explicit)
                        .GetAwaiter().GetResult();
                    s_sink = typedSharedContext.WorkCount;
                }),
        };

        return Render(rows);
    }

    public static string RunInitialization(bool staticTable)
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        long started = Stopwatch.GetTimestamp();
        if (staticTable)
        {
            var typed = SpikeSections.Registry;
            s_sink = typed.Pipeline.GetHashCode() + typed.GetHashCode();
        }
        else
        {
            var currentPipeline = CurrentBaselinePipelines.CreatePipeline();
            var currentScanners = CurrentBaselinePipelines.CreateScannerRegistry();
            s_sink = currentPipeline.GetHashCode() + currentScanners.GetHashCode();
        }
        long elapsed = Stopwatch.GetTimestamp() - started;
        long allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        string design = staticTable
            ? "Static lambda table initialization"
            : "Current first construction";

        return $"""
            # Section Registry Spike - One-Time Initialization

            Runtime: {System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription}; {System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture}; {System.Runtime.InteropServices.RuntimeInformation.OSDescription}

            | Design | Nanoseconds | Allocated bytes |
            | --- | ---: | ---: |
            | {design} | {ToNanoseconds(elapsed):F1} | {allocated} |

            One fresh-process observation; use allocated bytes as the stable quantity. Managed time includes first-use runtime effects.
            """;
    }

    private static Comparison Compare(string name, int iterations, Action current, Action candidate)
    {
        WarmUp(current);
        WarmUp(candidate);
        return new Comparison(name, Measure(current, iterations), Measure(candidate, iterations));
    }

    private static void WarmUp(Action action)
    {
        for (int i = 0; i < 1_000_000; i++)
            action();
    }

    private static Measurement Measure(Action action, int iterations)
    {
        double[] nanoseconds = new double[SampleCount];
        double[] bytes = new double[SampleCount];

        for (int sample = 0; sample < SampleCount; sample++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
            long started = Stopwatch.GetTimestamp();
            for (int i = 0; i < iterations; i++)
                action();
            long elapsed = Stopwatch.GetTimestamp() - started;
            long allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;

            nanoseconds[sample] = elapsed * 1_000_000_000d / Stopwatch.Frequency / iterations;
            bytes[sample] = (double)allocated / iterations;
        }

        Array.Sort(nanoseconds);
        Array.Sort(bytes);
        return new Measurement(nanoseconds[SampleCount / 2], bytes[SampleCount / 2]);
    }

    private static double ToNanoseconds(long elapsed)
        => elapsed * 1_000_000_000d / Stopwatch.Frequency;

    private static string Render(IEnumerable<Comparison> rows)
    {
        var lines = new List<string>
        {
            "# Section Registry Spike - Performance",
            "",
            $"Runtime: {System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription}; " +
            $"{System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture}; " +
            $"{System.Runtime.InteropServices.RuntimeInformation.OSDescription}",
            "",
            "| Scenario | Current ns/op | Static ns/op | Time delta | Current B/op | Static B/op | Allocation delta |",
            "| --- | ---: | ---: | ---: | ---: | ---: | ---: |",
        };

        foreach (var row in rows)
        {
            lines.Add(
                $"| {row.Name} | {row.Current.Nanoseconds:F1} | {row.Registry.Nanoseconds:F1} | " +
                $"{Percent(row.Registry.Nanoseconds, row.Current.Nanoseconds)} | " +
                $"{row.Current.Bytes:F1} | {row.Registry.Bytes:F1} | " +
                $"{Percent(row.Registry.Bytes, row.Current.Bytes)} |");
        }

        lines.Add("");
        lines.Add("Median of 9 samples after warmup; execution rows reuse and reset model/context objects to isolate dispatch.");
        lines.Add("Registry acquisition compares current per-use construction with access to the initialized static table.");
        lines.Add("One-time static table initialization is excluded and reported separately by fresh-process measurement and allocation-fanout.");
        lines.Add("Single-section and shared planning rows use precompiled plans; the arbitrary row measures explicit cold compilation.");
        return string.Join('\n', lines) + "\n";
    }

    private static string Percent(double value, double baseline)
    {
        if (baseline == 0)
            return value == 0 ? "0.0%" : "n/a";
        return $"{(value / baseline - 1) * 100:+0.0;-0.0;0.0}%";
    }

    private sealed record Measurement(double Nanoseconds, double Bytes);
    private sealed record Comparison(string Name, Measurement Current, Measurement Registry);
}
