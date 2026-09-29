using System.Collections.Immutable;
using System.Diagnostics;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

using ILInspector.Analysis;

#if IMPLEMENTATION_METRIC_EVIDENCE_V1
using ImplementationMetricSelection =
    ILInspector.Analysis.ImplementationMetricEvidenceKind;
#else
using ImplementationMetricSelection =
    ILInspector.Analysis.ImplementationMetricKind;
#endif

namespace ILInspector.AnalysisHarness;

public sealed record ImplementationMetricPerformanceReport(
    string AssemblyPath,
    string Sha256,
    Guid ModuleVersionId,
    int MethodCount,
    int Iterations,
    IReadOnlyList<ImplementationMetricScenarioReport> Scenarios);

public sealed record ImplementationMetricScenarioReport(
    string Name,
    double MedianElapsedMilliseconds,
    double P95ElapsedMilliseconds,
    double MedianCpuMilliseconds,
    double P95CpuMilliseconds,
    long MedianAllocatedBytes,
    long P95AllocatedBytes,
    int BodyCount,
    int RelationshipCount,
    int AttributionProbeBodies,
    long AttributionProbeIlBytes,
    int MetricBodies,
    long MetricIlBytes,
    IReadOnlyList<string> ParticipatingStages,
    IReadOnlyList<double> ElapsedMilliseconds,
    IReadOnlyList<double> CpuMilliseconds,
    IReadOnlyList<long> AllocatedBytes);

public static class ImplementationMetricPerformance
{
    const int WarmupIterations = 2;

    public static int Run(
        string assemblyPath,
        int iterations,
        bool json)
    {
        if (!File.Exists(assemblyPath))
        {
            Console.Error.WriteLine(
                $"Assembly not found: {assemblyPath}");
            return 2;
        }
        if (iterations < 1)
        {
            Console.Error.WriteLine(
                "--iterations requires a positive integer.");
            return 2;
        }

        byte[] bytes = File.ReadAllBytes(assemblyPath);
        ImmutableArray<byte> image = ImmutableArray.Create(bytes);
        (Guid mvid, ImmutableHashSet<int> scope) =
            ResolveAppendFormatScope(image);
        ImplementationMetricWorkLimits limits = new(
            maximumPhysicalBodies: 64,
            maximumEncodedIlBytes: 2 * 1024 * 1024,
            maximumAttributionProbeBodies: 100_000,
            maximumAttributionProbeIlBytes: 64L * 1024 * 1024);
        Scenario[] scenarios =
        [
            new(
                "body-size",
                ImplementationMetricSelection.BodySize),
            new(
                "body-size-plus-relationships",
                ImplementationMetricSelection.BodySize
                    | ImplementationMetricSelection
                        .SiblingOverloadRelationships),
            new(
                "complete-profile-v1",
                ImplementationMetricAnalysisRequest
                    .CompleteProfileV1),
        ];

        foreach (Scenario scenario in scenarios)
        {
            for (var warmup = 0;
                warmup < WarmupIterations;
                warmup++)
            {
                _ = Execute(
                    image,
                    scope,
                    limits,
                    scenario.Metrics);
            }
        }

        var samples = scenarios.ToDictionary(
            static scenario => scenario.Name,
            static _ => new List<Sample>());
        var lastExecutions =
            new Dictionary<string, LibraryBodyAnalysisExecution>();
        for (var iteration = 0;
            iteration < iterations;
            iteration++)
        {
            for (var scenarioIndex = 0;
                scenarioIndex < scenarios.Length;
                scenarioIndex++)
            {
                Scenario scenario =
                    scenarios[
                        (iteration + scenarioIndex)
                        % scenarios.Length];
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
                long allocatedBefore =
                    GC.GetAllocatedBytesForCurrentThread();
                TimeSpan cpuBefore =
                    Process.GetCurrentProcess()
                        .TotalProcessorTime;
                long started = Stopwatch.GetTimestamp();
                LibraryBodyAnalysisExecution execution =
                    Execute(
                        image,
                        scope,
                        limits,
                        scenario.Metrics);
                TimeSpan elapsed =
                    Stopwatch.GetElapsedTime(started);
                TimeSpan cpu =
                    Process.GetCurrentProcess()
                        .TotalProcessorTime
                    - cpuBefore;
                long allocated =
                    GC.GetAllocatedBytesForCurrentThread()
                    - allocatedBefore;
                samples[scenario.Name].Add(
                    new(
                        elapsed.TotalMilliseconds,
                        cpu.TotalMilliseconds,
                        allocated));
                lastExecutions[scenario.Name] = execution;
            }
        }

        ImplementationMetricScenarioReport[] reports =
        [
            .. scenarios.Select(scenario =>
                CreateReport(
                    scenario,
                    samples[scenario.Name],
                    lastExecutions[scenario.Name])),
        ];
        var report = new ImplementationMetricPerformanceReport(
            Path.GetFullPath(assemblyPath),
            Convert.ToHexString(SHA256.HashData(bytes)),
            mvid,
            scope.Count,
            iterations,
            reports);

        if (json)
        {
            Console.WriteLine(
                JsonSerializer.Serialize(
                    report,
                    ImplementationMetricPerformanceJsonContext
                        .Default
                        .ImplementationMetricPerformanceReport));
        }
        else
        {
            Print(report);
        }
        return 0;
    }

    static LibraryBodyAnalysisExecution Execute(
        ImmutableArray<byte> image,
        ImmutableHashSet<int> scope,
        ImplementationMetricWorkLimits limits,
        ImplementationMetricSelection metrics) =>
        LibraryBodyAnalysisService.ExecuteImage(
            "System.Private.CoreLib.dll",
            image,
            LibraryBodyAnalysisRequest
                .CreateImplementationMetrics(
                    metrics,
                    limits,
                    scope));

    static ImplementationMetricScenarioReport CreateReport(
        Scenario scenario,
        List<Sample> samples,
        LibraryBodyAnalysisExecution execution)
    {
        ImplementationMetricParticipationReceipt participation =
            execution.ImplementationMetrics.Participation
            ?? throw new InvalidOperationException(
                "Implementation metric participation was not published.");
        ImplementationMetricWorkBudgetSnapshot work =
            participation.Work
            ?? throw new InvalidOperationException(
                "Finite implementation metric work was not published.");
        int relationshipCount =
            scenario.Metrics.HasFlag(
                ImplementationMetricSelection
                    .SiblingOverloadRelationships)
                ? MethodImplementationProfileAnalysis
                    .CollectOverloadRelationships(
                        execution.CallGraph.DeclaredMethods,
                        execution.CallGraph.DirectCalls,
                        execution.CallGraph.DeclaredMethodMap)
                    .Length
                : 0;

        double[] elapsed =
        [
            .. samples
                .Select(static sample =>
                    sample.ElapsedMilliseconds)
                .Order(),
        ];
        long[] allocated =
        [
            .. samples
                .Select(static sample =>
                    sample.AllocatedBytes)
                .Order(),
        ];
        double[] cpu =
        [
            .. samples
                .Select(static sample =>
                    sample.CpuMilliseconds)
                .Order(),
        ];
        return new(
            scenario.Name,
            Median(elapsed),
            Percentile95(elapsed),
            Median(cpu),
            Percentile95(cpu),
            Median(allocated),
            Percentile95(allocated),
            execution.ImplementationMetrics.Bodies.Length,
            relationshipCount,
            work.AttributionProbeBodies,
            work.AttributionProbeIlBytes,
            work.MetricBodies,
            work.MetricIlBytes,
            [
                .. participation.ActualStages
                    .Select(static stage =>
                        stage.Stage.ToString()),
            ],
            elapsed,
            cpu,
            allocated);
    }

    static (Guid Mvid, ImmutableHashSet<int> Scope)
        ResolveAppendFormatScope(
            ImmutableArray<byte> image)
    {
        using var stream = new MemoryStream(
            image.AsMemory().ToArray(),
            writable: false);
        using var peReader = new PEReader(stream);
        MetadataReader reader =
            peReader.GetMetadataReader();
        TypeDefinitionHandle typeHandle =
            reader.TypeDefinitions.Single(handle =>
            {
                TypeDefinition type =
                    reader.GetTypeDefinition(handle);
                return reader.StringComparer.Equals(
                        type.Namespace,
                        "System.Text")
                    && reader.StringComparer.Equals(
                        type.Name,
                        "StringBuilder");
            });
        TypeDefinition stringBuilder =
            reader.GetTypeDefinition(typeHandle);
        ImmutableHashSet<int> scope =
        [
            .. stringBuilder.GetMethods()
                .Where(handle =>
                {
                    MethodDefinition method =
                        reader.GetMethodDefinition(handle);
                    return (method.Attributes
                            & MethodAttributes.Public) != 0
                        && reader.StringComparer.Equals(
                            method.Name,
                            "AppendFormat");
                })
                .Select(static handle =>
                    MetadataTokens.GetToken(handle)),
        ];
        if (scope.IsEmpty)
        {
            throw new InvalidOperationException(
                "System.Text.StringBuilder.AppendFormat was not found.");
        }

        ModuleDefinition module =
            reader.GetModuleDefinition();
        return (
            reader.GetGuid(module.Mvid),
            scope);
    }

    static double Median(double[] values) =>
        values.Length % 2 == 0
            ? (values[(values.Length / 2) - 1]
                + values[values.Length / 2]) / 2
            : values[values.Length / 2];

    static long Median(long[] values) =>
        values.Length % 2 == 0
            ? (values[(values.Length / 2) - 1]
                + values[values.Length / 2]) / 2
            : values[values.Length / 2];

    static double Percentile95(double[] values) =>
        values[Percentile95Index(values.Length)];

    static long Percentile95(long[] values) =>
        values[Percentile95Index(values.Length)];

    static int Percentile95Index(int length) =>
        Math.Max(
            0,
            (int)Math.Ceiling(length * 0.95) - 1);

    static void Print(
        ImplementationMetricPerformanceReport report)
    {
        Console.WriteLine(
            $"assembly: {report.AssemblyPath}");
        Console.WriteLine($"sha256: {report.Sha256}");
        Console.WriteLine($"mvid: {report.ModuleVersionId}");
        Console.WriteLine(
            $"methods: {report.MethodCount}");
        Console.WriteLine(
            $"iterations: {report.Iterations}");
        Console.WriteLine();
        Console.WriteLine(
            $"{"scenario",-32} {"median ms",12} {"p95 ms",12} "
            + $"{"cpu ms",12} "
            + $"{"median alloc",14} {"p95 alloc",14} "
            + $"{"bodies",8} {"edges",8}");
        foreach (ImplementationMetricScenarioReport scenario
            in report.Scenarios)
        {
            Console.WriteLine(
                $"{scenario.Name,-32} "
                + $"{scenario.MedianElapsedMilliseconds,12:N3} "
                + $"{scenario.P95ElapsedMilliseconds,12:N3} "
                + $"{scenario.MedianCpuMilliseconds,12:N3} "
                + $"{scenario.MedianAllocatedBytes,14:N0} "
                + $"{scenario.P95AllocatedBytes,14:N0} "
                + $"{scenario.BodyCount,8:N0} "
                + $"{scenario.RelationshipCount,8:N0}");
            Console.WriteLine(
                $"  work: attribution "
                + $"{scenario.AttributionProbeBodies:N0} bodies / "
                + $"{scenario.AttributionProbeIlBytes:N0} bytes; "
                + $"metrics {scenario.MetricBodies:N0} bodies / "
                + $"{scenario.MetricIlBytes:N0} bytes");
            Console.WriteLine(
                $"  stages: {string.Join(", ", scenario.ParticipatingStages)}");
        }
    }

    sealed record Scenario(
        string Name,
        ImplementationMetricSelection Metrics);

    readonly record struct Sample(
        double ElapsedMilliseconds,
        double CpuMilliseconds,
        long AllocatedBytes);
}

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(ImplementationMetricPerformanceReport))]
internal partial class ImplementationMetricPerformanceJsonContext
    : JsonSerializerContext;
