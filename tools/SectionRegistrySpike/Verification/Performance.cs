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
        var capabilities = SpikeSections.CreateCapabilityRegistry();
        var typed = SpikeSections.CreateCapabilityRegistrySections(capabilities);

        string[] metadataSelection = ["Metadata"];
        string[] sharedSelection = ["Calls", "Facts"];
        var metadataInclude = new HashSet<string>(metadataSelection, StringComparer.Ordinal);
        var sharedInclude = new HashSet<string>(sharedSelection, StringComparer.Ordinal);
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
                "Registry construction",
                10_000,
                () =>
                {
                    var pipeline = CurrentBaselinePipelines.CreatePipeline();
                    var scanners = CurrentBaselinePipelines.CreateScannerRegistry();
                    s_sink = pipeline.AllSectionNames.Length + scanners.GetHashCode();
                },
                () =>
                {
                    var registry = SpikeSections.CreateCapabilityRegistry();
                    var sections = SpikeSections.CreateCapabilityRegistrySections(registry);
                    s_sink = sections.Pipeline.AllSectionNames.Length + registry.GetHashCode();
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

    private static Comparison Compare(string name, int iterations, Action current, Action typed)
    {
        WarmUp(current);
        WarmUp(typed);
        return new Comparison(name, Measure(current, iterations), Measure(typed, iterations));
    }

    private static void WarmUp(Action action)
    {
        for (int i = 0; i < 10_000; i++)
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
            "| Scenario | Current ns/op | Typed ns/op | Time delta | Current B/op | Typed B/op | Allocation delta |",
            "| --- | ---: | ---: | ---: | ---: | ---: | ---: |",
        };

        foreach (var row in rows)
        {
            lines.Add(
                $"| {row.Name} | {row.Current.Nanoseconds:F1} | {row.Typed.Nanoseconds:F1} | " +
                $"{Percent(row.Typed.Nanoseconds, row.Current.Nanoseconds)} | " +
                $"{row.Current.Bytes:F1} | {row.Typed.Bytes:F1} | " +
                $"{Percent(row.Typed.Bytes, row.Current.Bytes)} |");
        }

        lines.Add("");
        lines.Add("Median of 9 samples after warmup; execution rows reuse and reset model/context objects to isolate dispatch.");
        lines.Add("Planning rows measure selection-to-work-plan overhead only; registry construction is one-time setup.");
        return string.Join('\n', lines) + "\n";
    }

    private static string Percent(double value, double baseline)
    {
        if (baseline == 0)
            return value == 0 ? "0.0%" : "n/a";
        return $"{(value / baseline - 1) * 100:+0.0;-0.0;0.0}%";
    }

    private sealed record Measurement(double Nanoseconds, double Bytes);
    private sealed record Comparison(string Name, Measurement Current, Measurement Typed);
}
