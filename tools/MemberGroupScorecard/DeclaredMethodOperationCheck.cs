using System.Diagnostics;

using DotnetInspector.Queries;
using DotnetInspector.Sections;
using ILInspector.Metadata;
using QuerySpace.Composition;

namespace MemberGroupScorecard;

internal sealed record DeclaredMethodOperationScorecardCell(
    string Terminal,
    double Microseconds,
    long AllocatedBytes);

internal sealed record DeclaredMethodOperationScorecardResult(
    int Count,
    IReadOnlyList<DeclaredMethodOperationScorecardCell> Cells);

internal static class DeclaredMethodOperationCheck
{
    private const int OperationsPerSample = 64;

    internal static async Task<int> CheckAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        cancellationToken.ThrowIfCancellationRequested();

        MetadataTypeDefinitionName type =
            TypeName();
        MetadataTypeDefinitionBinding binding =
            Binding(path, type);
        ResolvedAssemblyReference assembly =
            ResolvedAssemblyReference.CreateFromPath(
                path,
                AssemblyResolutionProvenance.Local(
                    "declared-method scorecard"));
        var participant = new AssemblyContextParticipant(
            assembly,
            NoResolverAssemblyBindingPolicy.Instance);
        await using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group =
            workspace.CreateAssemblyContextGroup([participant]);

        var count = AssertCounted(
            Execute(
                group,
                participant,
                type,
                binding,
                QuerySpaceTerminalRequirement.Count,
                cancellationToken));
        var rows = AssertRead(
            Execute(
                group,
                participant,
                type,
                binding,
                QuerySpaceTerminalRequirement.Rows,
                cancellationToken));
        if (count.Count != rows.Count
            || count.Count != rows.Rows.Length)
        {
            throw new InvalidOperationException(
                "QuerySpace declared MethodDef Count and Rows disagree.");
        }
        if (count.Receipt.MethodDefinitionHandlesVisited != 0
            || count.Receipt.MethodDefinitionRowsRead != 0
            || count.Receipt.MethodNamesDecoded != 0
            || count.Receipt.MethodSignaturesDecoded != 0
            || count.Receipt.MethodAttributesDecoded != 0
            || count.Receipt.ProjectedRows != 0)
        {
            throw new InvalidOperationException(
                "QuerySpace declared MethodDef Count performed per-method work.");
        }

        return count.Count;
    }

    internal static async Task<DeclaredMethodOperationScorecardResult>
        MeasureAsync(
            string path,
            CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        cancellationToken.ThrowIfCancellationRequested();

        MetadataTypeDefinitionName type = TypeName();
        MetadataTypeDefinitionBinding binding =
            Binding(path, type);
        ResolvedAssemblyReference assembly =
            ResolvedAssemblyReference.CreateFromPath(
                path,
                AssemblyResolutionProvenance.Local(
                    "declared-method scorecard"));
        var participant = new AssemblyContextParticipant(
            assembly,
            NoResolverAssemblyBindingPolicy.Instance);
        await using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group =
            workspace.CreateAssemblyContextGroup([participant]);

        TypeDeclaredMethodPopulationOutcome.Counted count =
            AssertCounted(
                Execute(
                    group,
                    participant,
                    type,
                    binding,
                    QuerySpaceTerminalRequirement.Count,
                    cancellationToken));
        TypeDeclaredMethodPopulationOutcome.Read rows =
            AssertRead(
                Execute(
                    group,
                    participant,
                    type,
                    binding,
                    QuerySpaceTerminalRequirement.Rows,
                    cancellationToken));
        Validate(count, rows);

        QuerySpaceTerminalRequirement[] terminals =
        [
            QuerySpaceTerminalRequirement.Count,
            QuerySpaceTerminalRequirement.Rows,
        ];
        foreach (QuerySpaceTerminalRequirement terminal in terminals)
        {
            for (int warmup = 0; warmup < 10; warmup++)
            {
                GC.KeepAlive(
                    Execute(
                        group,
                        participant,
                        type,
                        binding,
                        terminal,
                        cancellationToken));
            }
        }

        var times = terminals.ToDictionary(
            static terminal => terminal,
            static _ => new List<double>());
        var allocations = terminals.ToDictionary(
            static terminal => terminal,
            static _ => new List<long>());
        for (int round = 0; round < 8; round++)
        {
            for (int offset = 0; offset < terminals.Length; offset++)
            {
                QuerySpaceTerminalRequirement terminal =
                    terminals[(round + offset) % terminals.Length];
                Measurement measurement =
                    Measure(
                        () => Execute(
                            group,
                            participant,
                            type,
                            binding,
                            terminal,
                            cancellationToken));
                times[terminal].Add(measurement.Microseconds);
                allocations[terminal].Add(
                    measurement.AllocatedBytes);
            }
        }

        return new(
            count.Count,
            [
                Cell(
                    QuerySpaceTerminalRequirement.Count,
                    times,
                    allocations),
                Cell(
                    QuerySpaceTerminalRequirement.Rows,
                    times,
                    allocations),
            ]);
    }

    internal static string Report(
        DeclaredMethodOperationScorecardResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        using var writer = new StringWriter(
            System.Globalization.CultureInfo.InvariantCulture);
        DeclaredMethodOperationScorecardCell count =
            result.Cells.Single(cell => cell.Terminal == "Count");
        DeclaredMethodOperationScorecardCell rows =
            result.Cells.Single(cell => cell.Terminal == "Rows");
        writer.WriteLine("# QuerySpace operation");
        writer.WriteLine(
            "| Terminal | Median | Allocated | Time vs Count | "
                + "Allocation vs Count |");
        writer.WriteLine("| --- | ---: | ---: | ---: | ---: |");
        Write("Count", count, count);
        Write("Rows", rows, count);
        writer.WriteLine();
        return writer.ToString();

        void Write(
            string terminal,
            DeclaredMethodOperationScorecardCell cell,
            DeclaredMethodOperationScorecardCell baseline)
        {
            writer.WriteLine(
                $"| {terminal} | {cell.Microseconds:F3} us | "
                    + $"{cell.AllocatedBytes:N0} B | "
                    + $"{cell.Microseconds / baseline.Microseconds:F2}x | "
                    + $"{Ratio(cell.AllocatedBytes, baseline.AllocatedBytes):F2}x |");
        }
    }

    private static TypeDeclaredMethodPopulationOutcome Execute(
        AssemblyContextGroup group,
        AssemblyContextParticipant participant,
        MetadataTypeDefinitionName type,
        MetadataTypeDefinitionBinding binding,
        QuerySpaceTerminalRequirement terminal,
        CancellationToken cancellationToken) =>
        TypeDeclaredMethodPopulationInspectionOperation.Execute(
            new(
                group,
                participant,
                type,
                binding,
                TypeDeclaredMethodPopulationQuery.CreateRequest(terminal)),
            cancellationToken).Content;

    private static TypeDeclaredMethodPopulationOutcome.Counted
        AssertCounted(TypeDeclaredMethodPopulationOutcome outcome) =>
            outcome
                as TypeDeclaredMethodPopulationOutcome.Counted
            ?? throw new InvalidOperationException(
                $"Expected QuerySpace Counted, got {outcome}.");

    private static TypeDeclaredMethodPopulationOutcome.Read
        AssertRead(TypeDeclaredMethodPopulationOutcome outcome) =>
            outcome
                as TypeDeclaredMethodPopulationOutcome.Read
            ?? throw new InvalidOperationException(
                $"Expected QuerySpace Read, got {outcome}.");

    private static MetadataTypeDefinitionName TypeName() =>
        MetadataTypeDefinitionName.Create(
            "System.Text.Json",
            ["JsonSerializer"])
        is MetadataTypeDefinitionNameResult.Valid valid
            ? valid.Name
            : throw new InvalidOperationException(
                "The scorecard Type name was invalid.");

    private static MetadataTypeDefinitionBinding Binding(
        string path,
        MetadataTypeDefinitionName type)
    {
        using AssemblyInspectionSession session =
            AssemblyInspectionSession.Open(path);
        TypeDefinitionToken definition =
            session.ProbeDeclaration(type) switch
            {
                TypeDeclarationResult.Defined defined =>
                    defined.Definition,
                TypeDeclarationResult.DefinitionKindUnavailable
                    unavailable =>
                    unavailable.Definition,
                _ => throw new InvalidOperationException(
                    "System.Text.Json.JsonSerializer was not defined."),
            };
        return new(session.ModuleVersionId(), definition);
    }

    private static void Validate(
        TypeDeclaredMethodPopulationOutcome.Counted count,
        TypeDeclaredMethodPopulationOutcome.Read rows)
    {
        if (count.Count != rows.Count
            || count.Count != rows.Rows.Length)
        {
            throw new InvalidOperationException(
                "QuerySpace declared MethodDef Count and Rows disagree.");
        }
        if (count.Receipt.MethodDefinitionHandlesVisited != 0
            || count.Receipt.MethodDefinitionRowsRead != 0
            || count.Receipt.MethodNamesDecoded != 0
            || count.Receipt.MethodSignaturesDecoded != 0
            || count.Receipt.MethodAttributesDecoded != 0
            || count.Receipt.ProjectedRows != 0)
        {
            throw new InvalidOperationException(
                "QuerySpace declared MethodDef Count performed per-method work.");
        }
    }

    private static DeclaredMethodOperationScorecardCell Cell(
        QuerySpaceTerminalRequirement terminal,
        IReadOnlyDictionary<
            QuerySpaceTerminalRequirement,
            List<double>> times,
        IReadOnlyDictionary<
            QuerySpaceTerminalRequirement,
            List<long>> allocations) =>
        new(
            terminal.ToString(),
            Median(times[terminal]),
            Median(allocations[terminal]));

    private static Measurement Measure(Func<object> execute)
    {
        var times = new double[31];
        var allocations = new long[31];
        for (int sample = 0; sample < times.Length; sample++)
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            long started = Stopwatch.GetTimestamp();
            object? result = null;
            for (int operation = 0;
                operation < OperationsPerSample;
                operation++)
            {
                result = execute();
            }
            long elapsed = Stopwatch.GetTimestamp() - started;
            allocations[sample] =
                (GC.GetAllocatedBytesForCurrentThread() - before)
                    / OperationsPerSample;
            times[sample] =
                elapsed * 1_000_000.0
                    / Stopwatch.Frequency
                    / OperationsPerSample;
            GC.KeepAlive(result);
        }
        Array.Sort(times);
        Array.Sort(allocations);
        return new(
            times[times.Length / 2],
            allocations[allocations.Length / 2]);
    }

    private static double Median(List<double> values)
    {
        values.Sort();
        return values[values.Count / 2];
    }

    private static long Median(List<long> values)
    {
        values.Sort();
        return values[values.Count / 2];
    }

    private static double Ratio(long value, long baseline) =>
        baseline == 0
            ? value == 0 ? 1 : double.PositiveInfinity
            : (double)value / baseline;

    private readonly record struct Measurement(
        double Microseconds,
        long AllocatedBytes);
}
