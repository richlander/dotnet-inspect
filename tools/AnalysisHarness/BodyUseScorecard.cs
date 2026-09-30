using System.Collections.Immutable;
using System.Diagnostics;
using System.Globalization;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text;

using DotnetInspector.PerformanceOracles;
using ILInspector.Analysis;
using ILInspector.Analysis.Planning;
using ILInspector.Metadata;
using NLinq;

namespace ILInspector.AnalysisHarness;

public enum BodyUseScorecardColumn
{
    Direct,
    Linq,
    NLinq,
    Planner,
}

public sealed record BodyUseScorecardAsset(
    string Name,
    string SourceName,
    ImmutableArray<byte> Image);

public sealed record BodyUseScorecardAnswer(
    AnalysisLibraryBodyUseDisposition Disposition,
    ImmutableArray<AnalysisLibraryBodyUseType> Types,
    ImmutableArray<AnalysisLibraryBodyUseOccurrence> Occurrences,
    ImmutableArray<AnalysisLibraryBodyUsePhysicalEvidence> PhysicalEvidence,
    AnalysisLibraryBodyUseCoverage Coverage,
    ImmutableArray<AnalysisLibraryBodyUseDiagnostic> Diagnostics);

public sealed record BodyUseScorecardExecution(
    BodyUseScorecardColumn Column,
    BodyUseScorecardAnswer? Answer,
    string? Rejection)
{
    public bool Available => Answer is not null;
}

public sealed record BodyUseScorecardMismatch(
    string Asset,
    BodyUseScorecardColumn Column,
    string Answer,
    string OracleAnswer);

public sealed record BodyUseScorecardAnswerHash(
    string Asset,
    string Hash);

public sealed record BodyUseScorecardCheck(
    int Compared,
    IReadOnlyList<BodyUseScorecardMismatch> Mismatches,
    IReadOnlyList<BodyUseScorecardAnswerHash> AnswerHashes)
{
    public bool Agrees => Mismatches.Count == 0;
}

public sealed record BodyUseScorecardCell(
    int AssetIndex,
    string Asset,
    BodyUseScorecardColumn Column,
    IReadOnlyList<double> RoundMediansMicroseconds,
    IReadOnlyList<long> RoundMediansAllocatedBytes)
{
    public double MedianMicroseconds =>
        Median(RoundMediansMicroseconds);

    public long MedianAllocatedBytes =>
        Median(RoundMediansAllocatedBytes);

    static double Median(IReadOnlyList<double> values)
    {
        double[] sorted = [.. values];
        Array.Sort(sorted);
        int middle = sorted.Length / 2;
        return sorted.Length % 2 == 1
            ? sorted[middle]
            : (sorted[middle - 1] + sorted[middle]) / 2;
    }

    static long Median(IReadOnlyList<long> values)
    {
        long[] sorted = [.. values];
        Array.Sort(sorted);
        int middle = sorted.Length / 2;
        return sorted.Length % 2 == 1
            ? sorted[middle]
            : (sorted[middle - 1] + sorted[middle]) / 2;
    }
}

public static class BodyUseScorecard
{
    static readonly BodyUseScorecardColumn[] Columns =
    [
        BodyUseScorecardColumn.Direct,
        BodyUseScorecardColumn.Linq,
        BodyUseScorecardColumn.NLinq,
        BodyUseScorecardColumn.Planner,
    ];

    static long s_sink;

    public static IReadOnlyList<BodyUseScorecardAsset> LoadAssets(
        IReadOnlyList<string> paths)
    {
        IReadOnlyList<string> names =
            ScorecardAssetNames.FromPaths(paths);
        var assets = new List<BodyUseScorecardAsset>(paths.Count);
        for (int index = 0; index < paths.Count; index++)
        {
            string path = Path.GetFullPath(paths[index]);
            assets.Add(
                new(
                    names[index],
                    path,
                    ImmutableArray.Create(File.ReadAllBytes(path))));
        }
        return assets;
    }

    public static BodyUseScorecardCheck Check(
        IReadOnlyList<BodyUseScorecardAsset> assets,
        AnalysisLibraryBodyUseLimits? limits = null,
        CancellationToken cancellationToken = default)
    {
        var mismatches = new List<BodyUseScorecardMismatch>();
        var hashes = new List<BodyUseScorecardAnswerHash>();
        int compared = 0;
        foreach (BodyUseScorecardAsset asset in assets)
        {
            BodyUseScorecardExecution oracle = Execute(
                BodyUseScorecardColumn.NLinq,
                asset,
                limits,
                cancellationToken);
            hashes.Add(new(asset.Name, AnswerHash(oracle)));
            foreach (BodyUseScorecardColumn column in Columns)
            {
                if (column == BodyUseScorecardColumn.NLinq)
                    continue;
                BodyUseScorecardExecution actual = Execute(
                    column,
                    asset,
                    limits,
                    cancellationToken);
                compared++;
                if (!Same(actual, oracle))
                {
                    mismatches.Add(
                        new(
                            asset.Name,
                            column,
                            Describe(actual),
                            Describe(oracle)));
                }
            }
        }
        return new(compared, mismatches, hashes);
    }

    public static BodyUseScorecardExecution Execute(
        BodyUseScorecardColumn column,
        BodyUseScorecardAsset asset,
        AnalysisLibraryBodyUseLimits? limits = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(asset);
        AnalysisLibraryBodyUseLimits validated =
            new AnalysisLibraryBodyUseRequest(limits).Limits;
        return column switch
        {
            BodyUseScorecardColumn.Direct =>
                ExecuteOracle(
                    column,
                    asset,
                    validated,
                    cancellationToken,
                    static context => context.Direct()),
            BodyUseScorecardColumn.Linq =>
                ExecuteOracle(
                    column,
                    asset,
                    validated,
                    cancellationToken,
                    static context => context.Linq()),
            BodyUseScorecardColumn.NLinq =>
                ExecuteOracle(
                    column,
                    asset,
                    validated,
                    cancellationToken,
                    static context => context.NLinq()),
            BodyUseScorecardColumn.Planner =>
                ExecutePlanner(
                    asset,
                    validated,
                    cancellationToken),
            _ => throw new ArgumentOutOfRangeException(nameof(column)),
        };
    }

    public static IReadOnlyList<BodyUseScorecardCell> Measure(
        IReadOnlyList<BodyUseScorecardAsset> assets,
        ScorecardTiming timing,
        Action<string>? progress = null,
        AnalysisLibraryBodyUseLimits? limits = null,
        CancellationToken cancellationToken = default)
    {
        var times =
            new Dictionary<(int Asset, BodyUseScorecardColumn Column),
                List<double>>();
        var allocations =
            new Dictionary<(int Asset, BodyUseScorecardColumn Column),
                List<long>>();
        for (int round = 0; round < timing.Rounds; round++)
        {
            for (int assetIndex = 0;
                assetIndex < assets.Count;
                assetIndex++)
            {
                BodyUseScorecardAsset asset = assets[assetIndex];
                for (int offset = 0; offset < Columns.Length; offset++)
                {
                    BodyUseScorecardColumn column =
                        Columns[(round + offset) % Columns.Length];
                    Measurement measurement = MeasureCell(
                        column,
                        asset,
                        timing,
                        limits,
                        cancellationToken);
                    var key = (assetIndex, column);
                    if (!times.TryGetValue(
                            key,
                            out List<double>? roundTimes))
                    {
                        times.Add(key, roundTimes = []);
                        allocations.Add(key, []);
                    }
                    roundTimes.Add(measurement.Microseconds);
                    allocations[key].Add(measurement.AllocatedBytes);
                }
                progress?.Invoke(
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"round {round + 1}/{timing.Rounds}: {asset.Name}"));
            }
        }

        var cells = new List<BodyUseScorecardCell>();
        for (int assetIndex = 0;
            assetIndex < assets.Count;
            assetIndex++)
        {
            foreach (BodyUseScorecardColumn column in Columns)
            {
                var key = (assetIndex, column);
                cells.Add(
                    new(
                        assetIndex,
                        assets[assetIndex].Name,
                        column,
                        times[key],
                        allocations[key]));
            }
        }
        return cells;
    }

    public static string Report(
        IReadOnlyList<BodyUseScorecardCell> cells)
    {
        var text = new StringBuilder();
        text.AppendLine(
            "Rows-terminal ratios to NLinq: geometric mean across assets "
                + "(min-max); lower is faster.");
        text.AppendLine();
        text.AppendLine(
            "| Metric | Assets | Direct | LINQ | NLinq | Planner |");
        text.AppendLine("| --- | ---: | ---: | ---: | ---: | ---: |");
        AppendRatioRow(
            text,
            cells,
            "Time",
            static cell => cell.MedianMicroseconds);
        AppendRatioRow(
            text,
            cells,
            "Allocation",
            static cell => cell.MedianAllocatedBytes);
        text.AppendLine();
        text.AppendLine("Absolute medians.");
        text.AppendLine();
        text.AppendLine(
            "| Asset | Column | Time (us) | Allocated bytes |");
        text.AppendLine("| --- | --- | ---: | ---: |");
        foreach (BodyUseScorecardCell cell in cells)
        {
            text.Append("| ")
                .Append(cell.Asset)
                .Append(" | ")
                .Append(Name(cell.Column))
                .Append(" | ")
                .Append(
                    cell.MedianMicroseconds.ToString(
                        "0.0",
                        CultureInfo.InvariantCulture))
                .Append(" | ")
                .Append(
                    cell.MedianAllocatedBytes.ToString(
                        "N0",
                        CultureInfo.InvariantCulture))
                .AppendLine(" |");
        }
        return text.ToString();
    }

    public static void WriteTsv(
        IReadOnlyList<BodyUseScorecardCell> cells,
        TextWriter writer)
    {
        writer.WriteLine(
            "asset\tcolumn\tmedian_us\tallocated_bytes"
                + "\tround_medians_us\tround_allocated_bytes");
        foreach (BodyUseScorecardCell cell in cells)
        {
            writer.Write(cell.Asset);
            writer.Write('\t');
            writer.Write(Name(cell.Column));
            writer.Write('\t');
            writer.Write(
                cell.MedianMicroseconds.ToString(
                    "0.###",
                    CultureInfo.InvariantCulture));
            writer.Write('\t');
            writer.Write(
                cell.MedianAllocatedBytes.ToString(
                    CultureInfo.InvariantCulture));
            writer.Write('\t');
            writer.Write(
                string.Join(
                    ",",
                    cell.RoundMediansMicroseconds.Select(
                        static value => value.ToString(
                            "0.###",
                            CultureInfo.InvariantCulture))));
            writer.Write('\t');
            writer.WriteLine(
                string.Join(",", cell.RoundMediansAllocatedBytes));
        }
    }

    public static string Describe(
        BodyUseScorecardExecution execution)
    {
        if (execution.Answer is not { } answer)
            return "rejected:" + execution.Rejection;
        return string.Create(
            CultureInfo.InvariantCulture,
            $"types={answer.Types.Length};"
                + $"bodies={answer.Coverage.BodiesConsidered};"
                + $"operands={answer.Coverage.OperandsConsidered};"
                + $"occurrences={answer.Occurrences.Length};"
                + $"disposition={answer.Disposition};"
                + $"hash={AnswerHash(execution)}");
    }

    static BodyUseScorecardExecution ExecuteOracle(
        BodyUseScorecardColumn column,
        BodyUseScorecardAsset asset,
        AnalysisLibraryBodyUseLimits limits,
        CancellationToken cancellationToken,
        Func<
            OracleContext,
            AnalysisLibraryBodyUseProducer.Result> execute)
    {
        try
        {
            using var image = new PEReader(asset.Image);
            AssemblyTypeDeclarationInventoryOutcome inventoryOutcome =
                AssemblyTypeDeclarationInventoryReader.Read(
                    image,
                    limits.MaximumTypeDefinitions,
                    limits.MaximumRetainedTextCharacters);
            if (inventoryOutcome
                is AssemblyTypeDeclarationInventoryOutcome.Incomplete
                    incomplete)
            {
                return new(
                    column,
                    null,
                    $"Type inventory exceeded {incomplete.Bound}.");
            }
            if (inventoryOutcome
                is AssemblyTypeDeclarationInventoryOutcome.Rejected rejected)
            {
                return new(
                    column,
                    null,
                    rejected.Failure.Detail);
            }

            MetadataReader reader = image.GetMetadataReader();
            if (!reader.IsAssembly)
            {
                return new(
                    column,
                    null,
                    "The image has no assembly manifest.");
            }
            if (reader.GetGuid(reader.GetModuleDefinition().Mvid)
                == Guid.Empty)
            {
                return new(
                    column,
                    null,
                    "The image has an empty module version identifier.");
            }

            var inventory =
                ((AssemblyTypeDeclarationInventoryOutcome.Read)
                    inventoryOutcome).Inventory;
            using var builder = new LibraryBodyAnalysisBuilder(
                asset.SourceName,
                reader,
                image);
            var context = new OracleContext(
                image,
                reader,
                new LibraryMethodAnalysisRunner(builder),
                limits,
                cancellationToken);
            AnalysisLibraryBodyUseProjection projection =
                AnalysisLibraryBodyUseService.Project(
                    reader,
                    inventory,
                    execute(context));
            return new(column, Normalize(projection), null);
        }
        catch (Exception exception)
            when (LibraryMethodAnalysisRunner
                .IsRecoverableMethodFailure(exception))
        {
            return new(
                column,
                null,
                ProducerFailure.Describe(exception));
        }
    }

    static BodyUseScorecardExecution ExecutePlanner(
        BodyUseScorecardAsset asset,
        AnalysisLibraryBodyUseLimits limits,
        CancellationToken cancellationToken)
    {
        AnalysisLibraryBodyUseOutcome outcome =
            AnalysisLibraryBodyUseService.ExecuteImage(
                asset.SourceName,
                asset.Image,
                new(limits),
                cancellationToken);
        return outcome switch
        {
            AnalysisLibraryBodyUseOutcome.Available available =>
                new(
                    BodyUseScorecardColumn.Planner,
                    Normalize(available.Result),
                    null),
            AnalysisLibraryBodyUseOutcome.Rejected rejected =>
                new(
                    BodyUseScorecardColumn.Planner,
                    null,
                    $"{rejected.Kind}: {rejected.Detail}"),
            _ => throw new InvalidOperationException(
                "The body-use operation returned an unknown outcome."),
        };
    }

    static BodyUseScorecardAnswer Normalize(
        AnalysisLibraryBodyUseResult result) =>
        new(
            result.Disposition,
            result.Types,
            result.Occurrences,
            result.PhysicalEvidence,
            result.Coverage,
            result.Diagnostics);

    static BodyUseScorecardAnswer Normalize(
        AnalysisLibraryBodyUseProjection projection) =>
        new(
            projection.Disposition,
            projection.Types,
            projection.Occurrences,
            projection.PhysicalEvidence,
            projection.Coverage,
            projection.Diagnostics);

    static bool Same(
        BodyUseScorecardExecution left,
        BodyUseScorecardExecution right)
    {
        if (left.Answer is null || right.Answer is null)
        {
            return left.Answer is null
                && right.Answer is null;
        }
        BodyUseScorecardAnswer first = left.Answer;
        BodyUseScorecardAnswer second = right.Answer;
        return first.Disposition == second.Disposition
            && first.Types.AsSpan().SequenceEqual(
                second.Types.AsSpan())
            && first.Occurrences.AsSpan().SequenceEqual(
                second.Occurrences.AsSpan())
            && first.PhysicalEvidence.AsSpan().SequenceEqual(
                second.PhysicalEvidence.AsSpan())
            && first.Coverage == second.Coverage
            && first.Diagnostics.AsSpan().SequenceEqual(
                second.Diagnostics.AsSpan());
    }

    static string AnswerHash(BodyUseScorecardExecution execution)
    {
        if (execution.Answer is not { } answer)
            return "rejected:" + execution.Rejection;

        ulong hash = 14695981039346656037;
        Add((int)answer.Disposition);
        Add(answer.Coverage.BodiesConsidered);
        Add(answer.Coverage.BodiesExamined);
        Add(answer.Coverage.BodiesPhysicalOnly);
        Add(answer.Coverage.BodiesUnavailable);
        Add(answer.Coverage.BodiesLimited);
        Add(answer.Coverage.OperandsConsidered);
        Add(answer.Coverage.OperandsExamined);
        Add(answer.Coverage.OperandsUnavailable);
        Add(answer.Coverage.OperandsLimited);
        foreach (AnalysisLibraryBodyUseType type in answer.Types)
        {
            AddAddress(type.Type);
            AddName(type.Name);
            Add((int)type.DefinitionKind);
        }
        foreach (AnalysisLibraryBodyUseOccurrence occurrence
            in answer.Occurrences)
        {
            AddAddress(occurrence.Source);
            AddName(occurrence.SourceType);
            AddAddress(occurrence.Target);
            AddName(occurrence.TargetType);
            Add(occurrence.PhysicalMethodToken);
            Add((int)occurrence.OperandKind);
            Add(occurrence.OperandToken);
            Add(occurrence.IlOffset);
            Add(occurrence.OccurrenceOrdinal);
        }
        foreach (AnalysisLibraryBodyUsePhysicalEvidence physical
            in answer.PhysicalEvidence)
        {
            AddAddress(physical.PhysicalSource);
            AddName(physical.PhysicalSourceType);
            Add(physical.PhysicalMethodToken);
            Add((int)physical.Fidelity);
        }
        foreach (AnalysisLibraryBodyUseDiagnostic diagnostic
            in answer.Diagnostics)
        {
            Add((int)diagnostic.Kind);
            Add(diagnostic.MethodToken ?? -1);
            Add(diagnostic.IlOffset ?? -1);
            AddText(diagnostic.Detail);
            Add(diagnostic.Limit ?? -1);
            Add(diagnostic.AttemptedCharge ?? -1);
        }
        return hash.ToString("x16", CultureInfo.InvariantCulture);

        void AddAddress(MetadataTypeDefinitionAddress address)
        {
            AddText(address.ModuleVersionId.ToString("N"));
            Add(address.Definition.Value);
        }

        void AddName(MetadataTypeDefinitionName name)
        {
            AddText(name.Namespace);
            foreach (string segment in name.Segments)
                AddText(segment);
        }

        void AddText(string text)
        {
            foreach (char character in text)
                Add(character);
            Add(-2);
        }

        void Add(long value)
        {
            unchecked
            {
                for (int shift = 0; shift < 64; shift += 8)
                {
                    hash ^= (byte)(value >> shift);
                    hash *= 1099511628211;
                }
            }
        }
    }

    static Measurement MeasureCell(
        BodyUseScorecardColumn column,
        BodyUseScorecardAsset asset,
        ScorecardTiming timing,
        AnalysisLibraryBodyUseLimits? limits,
        CancellationToken cancellationToken)
    {
        for (int index = 0; index < timing.Warmup; index++)
        {
            Keep(
                RequireAvailable(
                    Execute(
                        column,
                        asset,
                        limits,
                        cancellationToken)));
        }

        var times = new List<double>();
        var allocations = new List<long>();
        long budget =
            Stopwatch.Frequency * timing.BudgetMilliseconds / 1000;
        long started = Stopwatch.GetTimestamp();
        while (times.Count < timing.MaxSamples
            && (times.Count < timing.MinSamples
                || Stopwatch.GetTimestamp() - started < budget))
        {
            cancellationToken.ThrowIfCancellationRequested();
            long allocatedBefore =
                GC.GetAllocatedBytesForCurrentThread();
            long timestamp = Stopwatch.GetTimestamp();
            BodyUseScorecardAnswer answer = RequireAvailable(
                Execute(
                    column,
                    asset,
                    limits,
                    cancellationToken));
            times.Add(
                Stopwatch.GetElapsedTime(timestamp).TotalMicroseconds);
            allocations.Add(
                GC.GetAllocatedBytesForCurrentThread()
                    - allocatedBefore);
            Keep(answer);
        }
        return new(Median(times), Median(allocations));
    }

    static BodyUseScorecardAnswer RequireAvailable(
        BodyUseScorecardExecution execution) =>
        execution.Answer
        ?? throw new InvalidOperationException(
            $"{Name(execution.Column)} rejected the scorecard asset: "
                + execution.Rejection);

    static void Keep(BodyUseScorecardAnswer answer) =>
        Volatile.Write(
            ref s_sink,
            s_sink
                + answer.Types.Length
                + answer.Coverage.BodiesConsidered
                + answer.Coverage.OperandsConsidered
                + answer.Occurrences.Length);

    static void AppendRatioRow(
        StringBuilder text,
        IReadOnlyList<BodyUseScorecardCell> cells,
        string metric,
        Func<BodyUseScorecardCell, double> value)
    {
        int assets = cells.Select(static cell => cell.AssetIndex)
            .Distinct()
            .Count();
        text.Append("| ")
            .Append(metric)
            .Append(" | ")
            .Append(assets.ToString(CultureInfo.InvariantCulture))
            .Append(" |");
        foreach (BodyUseScorecardColumn column in Columns)
        {
            if (column == BodyUseScorecardColumn.NLinq)
            {
                text.Append(" 1.00x |");
                continue;
            }
            double[] ratios =
            [
                .. cells
                    .Where(cell => cell.Column == column)
                    .Select(cell =>
                    {
                        BodyUseScorecardCell oracle =
                            cells.Single(candidate =>
                                candidate.AssetIndex
                                    == cell.AssetIndex
                                && candidate.Column
                                    == BodyUseScorecardColumn.NLinq);
                        return value(cell) / value(oracle);
                    }),
            ];
            text.Append(' ')
                .Append(
                    Math.Exp(ratios.Average(Math.Log)).ToString(
                        "0.00",
                        CultureInfo.InvariantCulture))
                .Append("x (")
                .Append(
                    ratios.Min().ToString(
                        "0.00",
                        CultureInfo.InvariantCulture))
                .Append('-')
                .Append(
                    ratios.Max().ToString(
                        "0.00",
                        CultureInfo.InvariantCulture))
                .Append(") |");
        }
        text.AppendLine();
    }

    static double Median(List<double> values)
    {
        values.Sort();
        int middle = values.Count / 2;
        return values.Count % 2 == 1
            ? values[middle]
            : (values[middle - 1] + values[middle]) / 2;
    }

    static long Median(List<long> values)
    {
        values.Sort();
        int middle = values.Count / 2;
        return values.Count % 2 == 1
            ? values[middle]
            : (values[middle - 1] + values[middle]) / 2;
    }

    static string Name(BodyUseScorecardColumn column) =>
        column == BodyUseScorecardColumn.Linq
            ? "LINQ"
            : column.ToString();

    readonly record struct Measurement(
        double Microseconds,
        long AllocatedBytes);

    sealed class OracleContext(
        PEReader image,
        MetadataReader reader,
        LibraryMethodAnalysisRunner runner,
        AnalysisLibraryBodyUseLimits limits,
        CancellationToken cancellationToken)
    {
        readonly PEReader _image = image;
        readonly MetadataReader _reader = reader;
        readonly LibraryMethodAnalysisRunner _runner = runner;
        readonly AnalysisLibraryBodyUseLimits _limits = limits;
        readonly CancellationToken _cancellationToken =
            cancellationToken;

        internal AnalysisLibraryBodyUseProducer.Result Direct()
        {
            var answer =
                new AnalysisLibraryBodyUseProducer.Accumulator(
                _limits.MaximumOccurrences);
            foreach (TypeDefinitionHandle typeHandle
                in _reader.TypeDefinitions)
            {
                _cancellationToken.ThrowIfCancellationRequested();
                TypeDefinition type =
                    _reader.GetTypeDefinition(typeHandle);
                if (!AnalysisLibraryBodyUseProducer
                        .IsTypeInPopulation(_reader, type))
                {
                    continue;
                }
                foreach (MethodDefinitionHandle methodHandle
                    in type.GetMethods())
                {
                    MethodDefinition method =
                        _reader.GetMethodDefinition(methodHandle);
                    if (!IsManaged(method))
                        continue;
                    answer.Add(new(
                        Analyze(
                            new(
                                _reader,
                                typeHandle,
                                type,
                                methodHandle,
                                method))));
                }
            }
            return answer.Complete();
        }

        internal AnalysisLibraryBodyUseProducer.Result Linq()
        {
            AnalysisLibraryBodyUseProducer.Accumulator answer =
                _reader.TypeDefinitions
                .Select(handle =>
                    (Handle: handle,
                    Type: _reader.GetTypeDefinition(handle)))
                .Where(item =>
                    AnalysisLibraryBodyUseProducer.IsTypeInPopulation(
                        _reader,
                        item.Type))
                .SelectMany(item =>
                    item.Type.GetMethods().Select(methodHandle =>
                        new MethodDefinitionRow(
                            _reader,
                            item.Handle,
                            item.Type,
                            methodHandle,
                            _reader.GetMethodDefinition(methodHandle))))
                .Where(static row => IsManaged(row.Method))
                .Select(Analyze)
                .Aggregate(
                    new AnalysisLibraryBodyUseProducer.Accumulator(
                        _limits.MaximumOccurrences),
                    static (current, fact) =>
                    {
                        current.Add(new(fact));
                        return current;
                    });
            return answer.Complete();
        }

        internal AnalysisLibraryBodyUseProducer.Result NLinq()
        {
            var selected =
                new MethodDefinitionRows(_reader)
                    .Where<
                        MethodDefinitionRows,
                        MethodDefinitionRow,
                        SelectManaged>(default);
            var projected =
                selected.Select<
                    Filter<
                        MethodDefinitionRow,
                        MethodDefinitionRows,
                        SelectManaged>,
                    MethodDefinitionRow,
                    BodyTypeUseMethodFact,
                    AnalyzeMethod>(new(this));
            AnalysisLibraryBodyUseProducer.Accumulator answer =
                projected.Fold<
                    Map<
                        MethodDefinitionRow,
                        BodyTypeUseMethodFact,
                        Filter<
                            MethodDefinitionRow,
                            MethodDefinitionRows,
                            SelectManaged>,
                        AnalyzeMethod>,
                    BodyTypeUseMethodFact,
                    AnalysisLibraryBodyUseProducer.Accumulator,
                    AddFact>(
                        new AnalysisLibraryBodyUseProducer.Accumulator(
                            _limits.MaximumOccurrences),
                        default);
            return answer.Complete();
        }

        BodyTypeUseMethodFact Analyze(MethodDefinitionRow row) =>
            _runner.AnalyzeBodyTypeUses(
                row.TypeHandle,
                row.Type,
                row.MethodHandle,
                row.Method,
                _image.GetMethodBody(
                    row.Method.RelativeVirtualAddress),
                _limits.MaximumInstructionsPerBody,
                _limits.MaximumOccurrences,
                _limits.MaximumMethodSignatureBytes,
                _cancellationToken);

        static bool IsManaged(MethodDefinition method) =>
            method.RelativeVirtualAddress != 0
            && LibraryMethodAnalysisRunner.HasManagedIlBody(
                method.ImplAttributes);

        readonly struct SelectManaged
            : IFunc<MethodDefinitionRow, bool>
        {
            public bool Invoke(MethodDefinitionRow row) =>
                AnalysisLibraryBodyUseProducer.IsTypeInPopulation(
                    row.Reader,
                    row.Type)
                && IsManaged(row.Method);
        }

        readonly struct AnalyzeMethod(OracleContext context)
            : IFunc<MethodDefinitionRow, BodyTypeUseMethodFact>
        {
            public BodyTypeUseMethodFact Invoke(
                MethodDefinitionRow row) =>
                context.Analyze(row);
        }

        readonly struct AddFact
            : IFunc<
                AnalysisLibraryBodyUseProducer.Accumulator,
                BodyTypeUseMethodFact,
                AnalysisLibraryBodyUseProducer.Accumulator>
        {
            public AnalysisLibraryBodyUseProducer.Accumulator Invoke(
                AnalysisLibraryBodyUseProducer.Accumulator answer,
                BodyTypeUseMethodFact fact)
            {
                answer.Add(new(fact));
                return answer;
            }
        }
    }
}
