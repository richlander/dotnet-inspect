using System.Collections.Immutable;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

using ILInspector.Analysis.Planning;
using ILInspector.Metadata;
using QuerySpace;
using QuerySpace.Composition;
using QuerySpace.Operations;
using QuerySpace.Rows;

namespace ILInspector.AnalysisHarness;

public sealed record MethodFusionPerformanceReport(
    string AssemblyPath,
    string Sha256,
    int Iterations,
    MethodFusionScenarioReport Shared,
    MethodFusionScenarioReport Independent,
    double ElapsedRatio,
    double AllocatedBytesRatio,
    double InstructionVisitRatio,
    double SourceOpeningRatio);

public sealed record MethodFusionScenarioReport(
    double MedianElapsedMilliseconds,
    double P95ElapsedMilliseconds,
    long MedianAllocatedBytes,
    long P95AllocatedBytes,
    int GroupCount,
    int NoRetentionSourcesOpened,
    int InstructionsVisited,
    string DirectInvocationResultSha256,
    string CallSiteResultSha256,
    IReadOnlyList<double> ElapsedMilliseconds,
    IReadOnlyList<long> AllocatedBytes);

public static class MethodFusionPerformance
{
    const int WarmupIterations = 2;
    const string MethodsRowSet = "methods";

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
        PreparedExecution prepared = Prepare();

        using AssemblyInspectionSession session =
            AssemblyInspectionSession.OpenPrefetched(
                new MemoryStream(bytes, writable: false));

        for (int i = 0; i < WarmupIterations; i++)
        {
            _ = ExecuteShared(session, prepared);
            _ = ExecuteIndependent(session, prepared);
        }

        var sharedSamples = new List<Sample>(iterations);
        var independentSamples = new List<Sample>(iterations);
        ScenarioResult? lastShared = null;
        ScenarioResult? lastIndependent = null;
        for (int i = 0; i < iterations; i++)
        {
            if ((i & 1) == 0)
            {
                lastShared = Measure(
                    session,
                    prepared,
                    shared: true,
                    sharedSamples);
                lastIndependent = Measure(
                    session,
                    prepared,
                    shared: false,
                    independentSamples);
            }
            else
            {
                lastIndependent = Measure(
                    session,
                    prepared,
                    shared: false,
                    independentSamples);
                lastShared = Measure(
                    session,
                    prepared,
                    shared: true,
                    sharedSamples);
            }
        }

        VerifyEquivalent(
            lastShared
                ?? throw new InvalidOperationException(
                    "Shared execution did not run."),
            lastIndependent
                ?? throw new InvalidOperationException(
                    "Independent execution did not run."));

        MethodFusionScenarioReport sharedReport =
            CreateReport(sharedSamples, lastShared);
        MethodFusionScenarioReport independentReport =
            CreateReport(independentSamples, lastIndependent);
        VerifyWorkReduction(sharedReport, independentReport);

        var report = new MethodFusionPerformanceReport(
            Path.GetFullPath(assemblyPath),
            Convert.ToHexString(SHA256.HashData(image.AsSpan())),
            iterations,
            sharedReport,
            independentReport,
            sharedReport.MedianElapsedMilliseconds
                / independentReport.MedianElapsedMilliseconds,
            (double)sharedReport.MedianAllocatedBytes
                / independentReport.MedianAllocatedBytes,
            (double)sharedReport.InstructionsVisited
                / independentReport.InstructionsVisited,
            (double)sharedReport.NoRetentionSourcesOpened
                / independentReport.NoRetentionSourcesOpened);

        if (json)
        {
            Console.WriteLine(
                JsonSerializer.Serialize(
                    report,
                    MethodFusionPerformanceJsonContext
                        .Default
                        .MethodFusionPerformanceReport));
        }
        else
        {
            Print(report);
        }
        return 0;
    }

    static PreparedExecution Prepare()
    {
        PreparedRequest direct = PrepareRequest(
            MethodCallCountProducer.DirectInvocations);
        PreparedRequest callSites = PrepareRequest(
            MethodCallCountProducer.CallSites);
        MethodDefinitionSourceResourceIdentity sharedResource =
            MethodDefinitionSourceResourceIdentity.Create();
        MethodDefinitionSourceRequestSetPlan shared = Plan(
            sharedResource,
            [direct.Association, callSites.Association]);
        MethodDefinitionSourceRequestSetPlan directOnly = Plan(
            MethodDefinitionSourceResourceIdentity.Create(),
            [direct.Association]);
        MethodDefinitionSourceRequestSetPlan callSitesOnly = Plan(
            MethodDefinitionSourceResourceIdentity.Create(),
            [callSites.Association]);
        return new(
            direct,
            callSites,
            AssemblyAnalysisRequestSetOperation.Create(
                "MethodFusion.Shared",
                shared),
            AssemblyAnalysisRequestSetOperation.Create(
                "MethodFusion.DirectInvocations",
                directOnly),
            AssemblyAnalysisRequestSetOperation.Create(
                "MethodFusion.CallSites",
                callSitesOnly));
    }

    static PreparedRequest PrepareRequest(
        MethodCallCountProducer producer)
    {
        WorkDescription work =
            ProducerPlanner.Plan(
                [new ProducerRequest(producer, ProducerTerminal.Rows)])
            is ProducerPlanResult.Accepted accepted
                ? accepted.Description
                : throw new ProducerContractException(
                    $"Producer '{producer.Identity}' did not plan.");
        MethodDefinitionSourceRequest<MethodCallCountProducerResult> request =
            MethodDefinitionSourceRequest<
                MethodCallCountProducerResult>.Create(
                    QueryRequest(),
                    work,
                    producer);
        return new(
            request,
            MethodDefinitionSourceAssociation.Create(request));
    }

    static MethodDefinitionSourceRequestSetPlan Plan(
        MethodDefinitionSourceResourceIdentity resource,
        IReadOnlyList<MethodDefinitionSourceAssociation> associations) =>
        MethodDefinitionSourceRequestSet.Plan(resource, associations)
            is MethodDefinitionSourceRequestSetPlanResult.Accepted accepted
                ? accepted.Plan
                : throw new ProducerContractException(
                    "The Method fusion request set did not plan.");

    static ScenarioResult Measure(
        AssemblyInspectionSession session,
        PreparedExecution prepared,
        bool shared,
        List<Sample> samples)
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        long allocatedBefore =
            GC.GetAllocatedBytesForCurrentThread();
        long started = Stopwatch.GetTimestamp();
        ScenarioResult result =
            shared
                ? ExecuteShared(session, prepared)
                : ExecuteIndependent(session, prepared);
        TimeSpan elapsed = Stopwatch.GetElapsedTime(started);
        long allocated =
            GC.GetAllocatedBytesForCurrentThread()
            - allocatedBefore;
        samples.Add(new(elapsed.TotalMilliseconds, allocated));
        return result;
    }

    static ScenarioResult ExecuteShared(
        AssemblyInspectionSession session,
        PreparedExecution prepared)
    {
        MethodDefinitionSourceRequestSetExecution execution =
            Execute(session, prepared.Shared);
        return new(
            RequireValue(
                execution.ResultOf(
                    prepared.Direct.Association,
                    prepared.Direct.Request)),
            RequireValue(
                execution.ResultOf(
                    prepared.CallSites.Association,
                    prepared.CallSites.Request)),
            execution.GroupReceipts);
    }

    static ScenarioResult ExecuteIndependent(
        AssemblyInspectionSession session,
        PreparedExecution prepared)
    {
        MethodDefinitionSourceRequestSetExecution direct =
            Execute(session, prepared.DirectOnly);
        MethodDefinitionSourceRequestSetExecution callSites =
            Execute(session, prepared.CallSitesOnly);
        return new(
            RequireValue(
                direct.ResultOf(
                    prepared.Direct.Association,
                    prepared.Direct.Request)),
            RequireValue(
                callSites.ResultOf(
                    prepared.CallSites.Association,
                    prepared.CallSites.Request)),
            [.. direct.GroupReceipts, .. callSites.GroupReceipts]);
    }

    static MethodDefinitionSourceRequestSetExecution Execute(
        AssemblyInspectionSession session,
        AssemblyAnalysisRequestSetOperation operation) =>
        session.SnapshotOperation(
            operation,
            access =>
                AssemblyAnalysisService.Instance.Execute(
                    operation,
                    access))
        is AssemblyAnalysisRequestSetServiceResult.Completed completed
            ? completed.Execution
            : throw new InvalidOperationException(
                "The Method fusion operation was rejected.");

    static MethodCallCountProducerResult RequireValue(
        ProducerResult<MethodCallCountProducerResult> result)
    {
        if (result.Outcome != ProducerOutcome.Complete
            || result.Value is null)
        {
            throw new InvalidOperationException(
                $"Producer execution ended as '{result.Outcome}'.");
        }
        return result.Value;
    }

    static void VerifyEquivalent(
        ScenarioResult shared,
        ScenarioResult independent)
    {
        if (!shared.DirectInvocations.Bodies.SequenceEqual(
                independent.DirectInvocations.Bodies)
            || !shared.CallSites.Bodies.SequenceEqual(
                independent.CallSites.Bodies))
        {
            throw new InvalidOperationException(
                "Shared and independent producer results differ.");
        }
    }

    static void VerifyWorkReduction(
        MethodFusionScenarioReport shared,
        MethodFusionScenarioReport independent)
    {
        if (shared.GroupCount != 1
            || independent.GroupCount != 2
            || checked(shared.NoRetentionSourcesOpened * 2)
                != independent.NoRetentionSourcesOpened
            || checked(shared.InstructionsVisited * 2)
                != independent.InstructionsVisited)
        {
            throw new InvalidOperationException(
                "Shared execution did not halve physical instruction-source "
                + "openings and visits.");
        }
    }

    static MethodFusionScenarioReport CreateReport(
        List<Sample> samples,
        ScenarioResult result)
    {
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
        MethodDefinitionInstructionWorkCoverage work =
            result.GroupReceipts
                .Select(static receipt =>
                    receipt.PhysicalCoverage.InstructionWork)
                .Aggregate(
                    MethodDefinitionInstructionWorkCoverage.Empty,
                    static (sum, current) =>
                        new(
                            checked(
                                sum.NoRetentionSourcesOpened
                                + current.NoRetentionSourcesOpened),
                            checked(
                                sum.LazyRetainedSourcesOpened
                                + current.LazyRetainedSourcesOpened),
                            checked(
                                sum.InstructionsVisited
                                + current.InstructionsVisited)));
        return new(
            Median(elapsed),
            Percentile95(elapsed),
            Median(allocated),
            Percentile95(allocated),
            result.GroupReceipts.Length,
            work.NoRetentionSourcesOpened,
            work.InstructionsVisited,
            ResultIdentity(result.DirectInvocations),
            ResultIdentity(result.CallSites),
            elapsed,
            allocated);
    }

    static string ResultIdentity(
        MethodCallCountProducerResult result)
    {
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(
            stream,
            System.Text.Encoding.UTF8,
            leaveOpen: true))
        {
            writer.Write(result.Bodies.Length);
            foreach (MethodCallCountBody body in result.Bodies)
            {
                writer.Write(body.MethodToken);
                writer.Write(body.HasManagedBody);
                writer.Write(body.Count.HasValue);
                if (body.Count.HasValue)
                    writer.Write(body.Count.GetValueOrDefault());
                writer.Write(body.Diagnostic?.Message ?? "");
            }
        }
        return Convert.ToHexString(
            SHA256.HashData(stream.GetBuffer().AsSpan(
                0,
                checked((int)stream.Length))));
    }

    static QuerySpaceRequest QueryRequest() =>
        QuerySpaceRequest.Create(
            QueryDescriptor,
            PortableQueryIntent.Empty,
            [MethodsRowSet],
            [],
            QuerySpaceTerminalRequirement.Rows);

    static double Median(double[] values) =>
        values.Length % 2 == 0
            ? (values[values.Length / 2 - 1]
                + values[values.Length / 2]) / 2
            : values[values.Length / 2];

    static long Median(long[] values) =>
        values.Length % 2 == 0
            ? checked(
                values[values.Length / 2 - 1]
                + values[values.Length / 2]) / 2
            : values[values.Length / 2];

    static double Percentile95(double[] values) =>
        values[(int)Math.Ceiling(values.Length * 0.95) - 1];

    static long Percentile95(long[] values) =>
        values[(int)Math.Ceiling(values.Length * 0.95) - 1];

    static void Print(MethodFusionPerformanceReport report)
    {
        Console.WriteLine($"assembly: {report.AssemblyPath}");
        Console.WriteLine($"sha256: {report.Sha256}");
        Console.WriteLine($"iterations: {report.Iterations}");
        Print("shared", report.Shared);
        Print("independent", report.Independent);
        Console.WriteLine(
            $"ratios: elapsed={report.ElapsedRatio:F3}x; "
            + $"allocated={report.AllocatedBytesRatio:F3}x; "
            + $"instruction-visits={report.InstructionVisitRatio:F3}x; "
            + $"source-openings={report.SourceOpeningRatio:F3}x");
    }

    static void Print(
        string name,
        MethodFusionScenarioReport report)
    {
        Console.WriteLine(
            $"{name}: median={report.MedianElapsedMilliseconds:F3} ms; "
            + $"p95={report.P95ElapsedMilliseconds:F3} ms; "
            + $"allocated={report.MedianAllocatedBytes:N0} bytes; "
            + $"groups={report.GroupCount}; "
            + $"opens={report.NoRetentionSourcesOpened:N0}; "
            + $"visits={report.InstructionsVisited:N0}");
    }

    static readonly QueryOperationDefinition<
        EmptyPredicate,
        EmptyPlan> Operation =
            QueryOperationDefinition<
                EmptyPredicate,
                EmptyPlan>.Create(
                    "analysis.method-fusion-performance",
                    new EmptyVocabulary(),
                    ["managed-assembly"],
                    ["method"],
                    [MethodsRowSet],
                    [],
                    [],
                    [new("default", [], [])]);

    static readonly QueryOperationRoute<
        EmptyPredicate,
        EmptyPlan> Route =
            QueryOperationRoute<
                EmptyPredicate,
                EmptyPlan>.Create(
                    "analysis.method-fusion-performance/default",
                    Operation,
                    "managed-assembly",
                    "method",
                    [MethodsRowSet],
                    "default",
                    [],
                    []);

    static readonly QuerySpaceDescriptor QueryDescriptor =
        QuerySpaceDescriptor.Create(
            "analysis.method-fusion-performance/query-space/v1",
            Route,
            [
                new QuerySpaceRowScopeDescriptor(
                    "analysis.method-fusion-performance/methods/v1",
                    "analysis.method-fusion-performance/method-rows/v1",
                    [MethodsRowSet],
                    [],
                    [],
                    []),
            ],
            [QuerySpaceTerminalRequirement.Rows],
            acceptsContinuation: false,
            [
                new(
                    QuerySpaceTerminalRequirement.Rows,
                    "analysis.method-fusion-performance/rows/v1"),
            ]);

    readonly record struct Sample(
        double ElapsedMilliseconds,
        long AllocatedBytes);

    sealed record ScenarioResult(
        MethodCallCountProducerResult DirectInvocations,
        MethodCallCountProducerResult CallSites,
        ImmutableArray<MethodDefinitionSourceGroupReceipt> GroupReceipts);

    sealed record PreparedRequest(
        MethodDefinitionSourceRequest<MethodCallCountProducerResult> Request,
        MethodDefinitionSourceAssociation Association);

    sealed record PreparedExecution(
        PreparedRequest Direct,
        PreparedRequest CallSites,
        AssemblyAnalysisRequestSetOperation Shared,
        AssemblyAnalysisRequestSetOperation DirectOnly,
        AssemblyAnalysisRequestSetOperation CallSitesOnly);

    readonly record struct EmptyPredicate;

    sealed record EmptyPlan;

    sealed class EmptyVocabulary
        : PortableQueryVocabulary<EmptyPredicate, EmptyPlan>
    {
        public override string Identity =>
            "analysis.method-fusion-performance/operation/v1";

        public override IReadOnlyList<string> RequiredDimensions => [];

        public override bool TryGetKey(
            string key,
            [NotNullWhen(true)]
            out PortableQueryKeyDeclaration<EmptyPredicate>? declaration)
        {
            declaration = null;
            return false;
        }

        public override bool TryGetDimension(
            string dimension,
            [NotNullWhen(true)]
            out PortableQueryDimensionDeclaration<EmptyPredicate>?
                declaration)
        {
            declaration = null;
            return false;
        }

        public override bool AdmitsStageKind(
            RowSelectionStageKind kind) =>
            false;

        public override bool TryGetNamedOrder(
            string reference,
            out PortableQueryOrderPurpose purpose)
        {
            purpose = default;
            return false;
        }

        public override bool IsOrderable(string key) => false;

        public override bool CollapsesDuplicateBindings => true;

        public override bool AreTermsCompatible(
            PortableQueryResolvedTerm<EmptyPredicate> first,
            PortableQueryResolvedTerm<EmptyPredicate> second) =>
            true;

        public override EmptyPlan CreatePlan(
            PortableQueryResolvedIntent<EmptyPredicate> resolved) =>
            new();
    }
}

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(MethodFusionPerformanceReport))]
internal partial class MethodFusionPerformanceJsonContext
    : JsonSerializerContext;
