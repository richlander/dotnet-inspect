using System.Collections.Immutable;
using System.Diagnostics;
using System.Globalization;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
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

public enum BodyUseScorecardClosing
{
    Exists,
    Count,
    Rows,
}

public enum BodyUseScorecardDisposition
{
    Settled,
    Complete,
    Qualified,
    Partial,
}

public sealed record BodyUseScorecardAsset(
    string Name,
    string SourceName,
    ImmutableArray<byte> Image);

public sealed record BodyUseScorecardTerminalEvidence(
    BodyUseScorecardDisposition Disposition,
    AnalysisLibraryBodyUseCoverage Coverage,
    ImmutableArray<AnalysisLibraryBodyUseDiagnostic> Diagnostics);

public abstract record BodyUseScorecardAnswer
{
    private protected BodyUseScorecardAnswer()
    {
    }

    public sealed record Exists(
        bool Value,
        BodyUseScorecardTerminalEvidence Evidence)
        : BodyUseScorecardAnswer;

    public sealed record Count(
        int Value,
        BodyUseScorecardTerminalEvidence Evidence)
        : BodyUseScorecardAnswer;

    public sealed record Rows(
        AnalysisLibraryBodyUseDisposition Disposition,
        ImmutableArray<AnalysisLibraryBodyUseType> Types,
        ImmutableArray<AnalysisLibraryBodyUseOccurrence> Occurrences,
        ImmutableArray<AnalysisLibraryBodyUsePhysicalEvidence> PhysicalEvidence,
        AnalysisLibraryBodyUseCoverage Coverage,
        ImmutableArray<AnalysisLibraryBodyUseDiagnostic> Diagnostics)
        : BodyUseScorecardAnswer;
}

public sealed record BodyUseScorecardRejection(
    AnalysisLibraryBodyUseRejectionKind Kind,
    string Detail)
{
    public override string ToString() => $"{Kind}: {Detail}";
}

public sealed record BodyUseScorecardExecution(
    BodyUseScorecardColumn Column,
    BodyUseScorecardClosing Closing,
    BodyUseScorecardAnswer? Answer,
    BodyUseScorecardRejection? Rejection)
{
    public bool Available => Answer is not null;
}

public sealed record BodyUseScorecardMismatch(
    string Asset,
    BodyUseScorecardClosing Closing,
    BodyUseScorecardColumn Column,
    string Answer,
    string OracleAnswer);

public sealed record BodyUseScorecardAnswerHash(
    string Asset,
    BodyUseScorecardClosing Closing,
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
    BodyUseScorecardClosing Closing,
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

    static readonly BodyUseScorecardClosing[] Closings =
    [
        BodyUseScorecardClosing.Exists,
        BodyUseScorecardClosing.Count,
        BodyUseScorecardClosing.Rows,
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
            foreach (BodyUseScorecardClosing closing in Closings)
            {
                BodyUseScorecardExecution oracle = Execute(
                    BodyUseScorecardColumn.NLinq,
                    closing,
                    asset,
                    limits,
                    cancellationToken);
                hashes.Add(
                    new(
                        asset.Name,
                        closing,
                        AnswerHash(oracle)));
                foreach (BodyUseScorecardColumn column in Columns)
                {
                    if (column == BodyUseScorecardColumn.NLinq)
                        continue;
                    BodyUseScorecardExecution actual = Execute(
                        column,
                        closing,
                        asset,
                        limits,
                        cancellationToken);
                    compared++;
                    if (!Same(actual, oracle))
                    {
                        mismatches.Add(
                            new(
                                asset.Name,
                                closing,
                                column,
                                Describe(actual),
                                Describe(oracle)));
                    }
                }
            }
        }
        return new(compared, mismatches, hashes);
    }

    public static BodyUseScorecardExecution Execute(
        BodyUseScorecardColumn column,
        BodyUseScorecardAsset asset,
        AnalysisLibraryBodyUseLimits? limits = null,
        CancellationToken cancellationToken = default) =>
        Execute(
            column,
            BodyUseScorecardClosing.Rows,
            asset,
            limits,
            cancellationToken);

    public static BodyUseScorecardExecution Execute(
        BodyUseScorecardColumn column,
        BodyUseScorecardClosing closing,
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
                    closing,
                    asset,
                    validated,
                    cancellationToken,
                    static (context, terminal) =>
                        context.Direct(terminal)),
            BodyUseScorecardColumn.Linq =>
                ExecuteOracle(
                    column,
                    closing,
                    asset,
                    validated,
                    cancellationToken,
                    static (context, terminal) =>
                        context.Linq(terminal)),
            BodyUseScorecardColumn.NLinq =>
                ExecuteOracle(
                    column,
                    closing,
                    asset,
                    validated,
                    cancellationToken,
                    static (context, terminal) =>
                        context.NLinq(terminal)),
            BodyUseScorecardColumn.Planner =>
                ExecutePlanner(
                    closing,
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
            new Dictionary<
                (
                    int Asset,
                    BodyUseScorecardClosing Closing,
                    BodyUseScorecardColumn Column),
                List<double>>();
        var allocations =
            new Dictionary<
                (
                    int Asset,
                    BodyUseScorecardClosing Closing,
                    BodyUseScorecardColumn Column),
                List<long>>();
        for (int round = 0; round < timing.Rounds; round++)
        {
            for (int assetIndex = 0;
                assetIndex < assets.Count;
                assetIndex++)
            {
                BodyUseScorecardAsset asset = assets[assetIndex];
                foreach (BodyUseScorecardClosing closing in Closings)
                {
                    for (int offset = 0;
                        offset < Columns.Length;
                        offset++)
                    {
                        BodyUseScorecardColumn column =
                            Columns[(round + offset) % Columns.Length];
                        Measurement measurement = MeasureCell(
                            column,
                            closing,
                            asset,
                            timing,
                            limits,
                            cancellationToken);
                        var key = (assetIndex, closing, column);
                        if (!times.TryGetValue(
                                key,
                                out List<double>? roundTimes))
                        {
                            times.Add(key, roundTimes = []);
                            allocations.Add(key, []);
                        }
                        roundTimes.Add(measurement.Microseconds);
                        allocations[key].Add(
                            measurement.AllocatedBytes);
                    }
                    progress?.Invoke(
                        string.Create(
                            CultureInfo.InvariantCulture,
                            $"round {round + 1}/{timing.Rounds}: "
                                + $"{asset.Name} / {closing}"));
                }
            }
        }

        var cells = new List<BodyUseScorecardCell>();
        for (int assetIndex = 0;
            assetIndex < assets.Count;
            assetIndex++)
        {
            foreach (BodyUseScorecardClosing closing in Closings)
            {
                foreach (BodyUseScorecardColumn column in Columns)
                {
                    var key = (assetIndex, closing, column);
                    cells.Add(
                        new(
                            assetIndex,
                            assets[assetIndex].Name,
                            closing,
                            column,
                            times[key],
                            allocations[key]));
                }
            }
        }
        return cells;
    }

    public static string Report(
        IReadOnlyList<BodyUseScorecardCell> cells)
    {
        var text = new StringBuilder();
        text.AppendLine(
            "Terminal ratios to NLinq: geometric mean across assets "
                + "(min-max); lower is faster.");
        text.AppendLine();
        text.AppendLine(
            "| Terminal | Metric | Assets | Direct | LINQ | NLinq | Planner |");
        text.AppendLine(
            "| --- | --- | ---: | ---: | ---: | ---: | ---: |");
        foreach (BodyUseScorecardClosing closing in Closings)
        {
            AppendRatioRow(
                text,
                cells,
                closing,
                "Time",
                static cell => cell.MedianMicroseconds);
            AppendRatioRow(
                text,
                cells,
                closing,
                "Allocation",
                static cell => cell.MedianAllocatedBytes);
        }
        text.AppendLine();
        text.AppendLine("Absolute medians.");
        text.AppendLine();
        text.AppendLine(
            "| Asset | Terminal | Column | Time (us) | Allocated bytes |");
        text.AppendLine("| --- | --- | --- | ---: | ---: |");
        foreach (BodyUseScorecardCell cell in cells)
        {
            text.Append("| ")
                .Append(cell.Asset)
                .Append(" | ")
                .Append(cell.Closing)
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
            "asset\tterminal\tcolumn\tmedian_us\tallocated_bytes"
                + "\tround_medians_us\tround_allocated_bytes");
        foreach (BodyUseScorecardCell cell in cells)
        {
            writer.Write(cell.Asset);
            writer.Write('\t');
            writer.Write(cell.Closing);
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
        return answer switch
        {
            BodyUseScorecardAnswer.Exists exists =>
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"exists={exists.Value};"
                        + $"disposition="
                        + $"{exists.Evidence.Disposition};"
                        + $"hash={AnswerHash(execution)}"),
            BodyUseScorecardAnswer.Count count =>
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"count={count.Value};"
                        + $"disposition="
                        + $"{count.Evidence.Disposition};"
                        + $"hash={AnswerHash(execution)}"),
            BodyUseScorecardAnswer.Rows rows =>
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"types={rows.Types.Length};"
                        + $"bodies={rows.Coverage.BodiesConsidered};"
                        + $"operands={rows.Coverage.OperandsConsidered};"
                        + $"occurrences={rows.Occurrences.Length};"
                        + $"disposition={rows.Disposition};"
                        + $"hash={AnswerHash(execution)}"),
            _ => throw new InvalidOperationException(
                "The scorecard returned an unknown answer."),
        };
    }

    static BodyUseScorecardExecution ExecuteOracle(
        BodyUseScorecardColumn column,
        BodyUseScorecardClosing closing,
        BodyUseScorecardAsset asset,
        AnalysisLibraryBodyUseLimits limits,
        CancellationToken cancellationToken,
        Func<
            OracleContext,
            ProducerTerminal,
            AnalysisLibraryBodyUseProducer.Result> execute)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
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
                    closing,
                    null,
                    new(
                        AnalysisLibraryBodyUseRejectionKind.Limit,
                        $"The Type inventory exceeded "
                            + $"{incomplete.Bound}."));
            }
            if (inventoryOutcome
                is AssemblyTypeDeclarationInventoryOutcome.Rejected rejected)
            {
                return new(
                    column,
                    closing,
                    null,
                    new(
                        AnalysisLibraryBodyUseRejectionKind.UnsupportedImage,
                        rejected.Failure.Detail));
            }

            MetadataReader reader = image.GetMetadataReader();
            if (!reader.IsAssembly)
            {
                return new(
                    column,
                    closing,
                    null,
                    new(
                        AnalysisLibraryBodyUseRejectionKind
                            .MissingAssemblyIdentity,
                        "A Library body-use population requires "
                            + "an assembly manifest."));
            }
            if (reader.GetGuid(reader.GetModuleDefinition().Mvid)
                == Guid.Empty)
            {
                return new(
                    column,
                    closing,
                    null,
                    new(
                        AnalysisLibraryBodyUseRejectionKind
                            .MissingAssemblyIdentity,
                        "The metadata image has an empty module "
                            + "version identifier."));
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
            AnalysisLibraryBodyUseProducer.Result result;
            try
            {
                result = execute(
                    context,
                    Terminal(closing));
            }
            catch (ProducerAbortException abort)
            {
                return new(
                    column,
                    closing,
                    null,
                    new(
                        AnalysisLibraryBodyUseRejectionKind.Limit,
                        abort.Failure.Message));
            }
            catch (Exception exception)
                when (LibraryMethodAnalysisRunner
                    .IsRecoverableMethodFailure(exception))
            {
                return new(
                    column,
                    closing,
                    null,
                    new(
                        AnalysisLibraryBodyUseRejectionKind.Execution,
                        ProducerFailure.Describe(exception)));
            }
            BodyUseScorecardAnswer answer =
                Normalize(
                    closing,
                    reader,
                    inventory,
                    result);
            return new(column, closing, answer, null);
        }
        catch (Exception exception)
            when (LibraryMethodAnalysisRunner
                .IsRecoverableMethodFailure(exception))
        {
            return new(
                column,
                closing,
                null,
                new(
                    AnalysisLibraryBodyUseRejectionKind.MalformedImage,
                    ProducerFailure.Describe(exception)));
        }
    }

    static BodyUseScorecardExecution ExecutePlanner(
        BodyUseScorecardClosing closing,
        BodyUseScorecardAsset asset,
        AnalysisLibraryBodyUseLimits limits,
        CancellationToken cancellationToken)
    {
        AnalysisLibraryBodyUseQueryOutcome outcome =
            AnalysisLibraryBodyUseService.ExecuteTerminalImage(
                asset.SourceName,
                asset.Image,
                Terminal(closing),
                limits,
                cancellationToken);
        return outcome switch
        {
            AnalysisLibraryBodyUseQueryOutcome.Available available =>
                new(
                    BodyUseScorecardColumn.Planner,
                    closing,
                    Normalize(available.Answer),
                    null),
            AnalysisLibraryBodyUseQueryOutcome.Rejected rejected =>
                new(
                    BodyUseScorecardColumn.Planner,
                    closing,
                    null,
                    new(rejected.Kind, rejected.Detail)),
            _ => throw new InvalidOperationException(
                "The body-use operation returned an unknown outcome."),
        };
    }

    static BodyUseScorecardAnswer Normalize(
        AnalysisLibraryBodyUseAnswer answer) =>
        answer switch
        {
            AnalysisLibraryBodyUseAnswer.Exists exists =>
                new BodyUseScorecardAnswer.Exists(
                    exists.Value,
                    Normalize(exists.Evidence)),
            AnalysisLibraryBodyUseAnswer.Count count =>
                new BodyUseScorecardAnswer.Count(
                    count.Value,
                    Normalize(count.Evidence)),
            AnalysisLibraryBodyUseAnswer.Rows rows =>
                Normalize(rows.Result),
            _ => throw new InvalidOperationException(
                "The body-use query returned an unknown answer."),
        };

    static BodyUseScorecardAnswer Normalize(
        BodyUseScorecardClosing closing,
        MetadataReader reader,
        AssemblyTypeDeclarationInventory inventory,
        AnalysisLibraryBodyUseProducer.Result result)
    {
        if (closing == BodyUseScorecardClosing.Rows)
        {
            AnalysisLibraryBodyUseProjection projection =
                AnalysisLibraryBodyUseService.Project(
                    reader,
                    inventory,
                    result);
            return new BodyUseScorecardAnswer.Rows(
                projection.Disposition,
                projection.Types,
                projection.Occurrences,
                projection.PhysicalEvidence,
                projection.Coverage,
                projection.Diagnostics);
        }

        var evidence = new BodyUseScorecardTerminalEvidence(
            Normalize(
                AnalysisLibraryBodyUseService.TerminalDisposition(
                    result,
                    closing == BodyUseScorecardClosing.Exists
                        && result.OccurrenceCount != 0)),
            result.Coverage,
            result.Diagnostics);
        return closing == BodyUseScorecardClosing.Count
            ? new BodyUseScorecardAnswer.Count(
                result.OccurrenceCount,
                evidence)
            : new BodyUseScorecardAnswer.Exists(
                result.OccurrenceCount != 0,
                evidence);
    }

    static BodyUseScorecardAnswer Normalize(
        AnalysisLibraryBodyUseResult result) =>
        new BodyUseScorecardAnswer.Rows(
            result.Disposition,
            result.Types,
            result.Occurrences,
            result.PhysicalEvidence,
            result.Coverage,
            result.Diagnostics);

    static BodyUseScorecardTerminalEvidence Normalize(
        AnalysisLibraryBodyUseTerminalEvidence evidence) =>
        new(
            Normalize(evidence.Disposition),
            evidence.Coverage,
            evidence.Diagnostics);

    static BodyUseScorecardDisposition Normalize(
        AnalysisLibraryBodyUseTerminalDisposition disposition) =>
        disposition switch
        {
            AnalysisLibraryBodyUseTerminalDisposition.Settled =>
                BodyUseScorecardDisposition.Settled,
            AnalysisLibraryBodyUseTerminalDisposition.Complete =>
                BodyUseScorecardDisposition.Complete,
            AnalysisLibraryBodyUseTerminalDisposition.Qualified =>
                BodyUseScorecardDisposition.Qualified,
            AnalysisLibraryBodyUseTerminalDisposition.Partial =>
                BodyUseScorecardDisposition.Partial,
            _ => throw new ArgumentOutOfRangeException(
                nameof(disposition)),
        };

    static bool Same(
        BodyUseScorecardExecution left,
        BodyUseScorecardExecution right)
    {
        if (left.Answer is null || right.Answer is null)
        {
            return left.Answer is null
                && right.Answer is null
                && Equals(
                    left.Rejection,
                    right.Rejection);
        }
        return (left.Answer, right.Answer) switch
        {
            (
                BodyUseScorecardAnswer.Exists first,
                BodyUseScorecardAnswer.Exists second) =>
                first.Value == second.Value
                && Same(first.Evidence, second.Evidence),
            (
                BodyUseScorecardAnswer.Count first,
                BodyUseScorecardAnswer.Count second) =>
                first.Value == second.Value
                && Same(first.Evidence, second.Evidence),
            (
                BodyUseScorecardAnswer.Rows first,
                BodyUseScorecardAnswer.Rows second) =>
                first.Disposition == second.Disposition
                && first.Types.AsSpan().SequenceEqual(
                    second.Types.AsSpan())
                && first.Occurrences.AsSpan().SequenceEqual(
                    second.Occurrences.AsSpan())
                && first.PhysicalEvidence.AsSpan().SequenceEqual(
                    second.PhysicalEvidence.AsSpan())
                && first.Coverage == second.Coverage
                && first.Diagnostics.AsSpan().SequenceEqual(
                    second.Diagnostics.AsSpan()),
            _ => false,
        };
    }

    static bool Same(
        BodyUseScorecardTerminalEvidence first,
        BodyUseScorecardTerminalEvidence second) =>
        first.Disposition == second.Disposition
        && first.Coverage == second.Coverage
        && first.Diagnostics.AsSpan().SequenceEqual(
            second.Diagnostics.AsSpan());

    static string AnswerHash(BodyUseScorecardExecution execution)
    {
        if (execution.Answer is not { } answer)
            return "rejected:" + execution.Rejection;

        ulong hash = 14695981039346656037;
        Add((int)execution.Closing);
        switch (answer)
        {
            case BodyUseScorecardAnswer.Exists exists:
                Add(exists.Value ? 1 : 0);
                AddEvidence(exists.Evidence);
                break;
            case BodyUseScorecardAnswer.Count count:
                Add(count.Value);
                AddEvidence(count.Evidence);
                break;
            case BodyUseScorecardAnswer.Rows rows:
                Add((int)rows.Disposition);
                AddCoverage(rows.Coverage);
                foreach (AnalysisLibraryBodyUseType type in rows.Types)
                {
                    AddAddress(type.Type);
                    AddName(type.Name);
                    Add((int)type.DefinitionKind);
                }
                foreach (AnalysisLibraryBodyUseOccurrence occurrence
                    in rows.Occurrences)
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
                    in rows.PhysicalEvidence)
                {
                    AddAddress(physical.PhysicalSource);
                    AddName(physical.PhysicalSourceType);
                    Add(physical.PhysicalMethodToken);
                    Add((int)physical.Fidelity);
                }
                AddDiagnostics(rows.Diagnostics);
                break;
            default:
                throw new InvalidOperationException(
                    "The scorecard returned an unknown answer.");
        }
        return hash.ToString("x16", CultureInfo.InvariantCulture);

        void AddEvidence(BodyUseScorecardTerminalEvidence evidence)
        {
            Add((int)evidence.Disposition);
            AddCoverage(evidence.Coverage);
            AddDiagnostics(evidence.Diagnostics);
        }

        void AddCoverage(AnalysisLibraryBodyUseCoverage coverage)
        {
            Add(coverage.BodiesConsidered);
            Add(coverage.BodiesExamined);
            Add(coverage.BodiesPhysicalOnly);
            Add(coverage.BodiesUnavailable);
            Add(coverage.BodiesLimited);
            Add(coverage.OperandsConsidered);
            Add(coverage.OperandsExamined);
            Add(coverage.OperandsUnavailable);
            Add(coverage.OperandsLimited);
        }

        void AddDiagnostics(
            ImmutableArray<AnalysisLibraryBodyUseDiagnostic> diagnostics)
        {
            foreach (AnalysisLibraryBodyUseDiagnostic diagnostic
                in diagnostics)
            {
                Add((int)diagnostic.Kind);
                Add(diagnostic.MethodToken ?? -1);
                Add(diagnostic.IlOffset ?? -1);
                AddText(diagnostic.Detail);
                Add(diagnostic.Limit ?? -1);
                Add(diagnostic.AttemptedCharge ?? -1);
            }
        }

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
        BodyUseScorecardClosing closing,
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
                        closing,
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
                    closing,
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

    static void Keep(BodyUseScorecardAnswer answer)
    {
        long value = answer switch
        {
            BodyUseScorecardAnswer.Exists exists =>
                (exists.Value ? 1 : 0)
                + exists.Evidence.Coverage.BodiesConsidered
                + exists.Evidence.Coverage.OperandsConsidered,
            BodyUseScorecardAnswer.Count count =>
                count.Value
                + count.Evidence.Coverage.BodiesConsidered
                + count.Evidence.Coverage.OperandsConsidered,
            BodyUseScorecardAnswer.Rows rows =>
                rows.Types.Length
                + rows.Coverage.BodiesConsidered
                + rows.Coverage.OperandsConsidered
                + rows.Occurrences.Length,
            _ => throw new InvalidOperationException(
                "The scorecard returned an unknown answer."),
        };
        Volatile.Write(ref s_sink, s_sink + value);
    }

    static void AppendRatioRow(
        StringBuilder text,
        IReadOnlyList<BodyUseScorecardCell> cells,
        BodyUseScorecardClosing closing,
        string metric,
        Func<BodyUseScorecardCell, double> value)
    {
        BodyUseScorecardCell[] terminalCells =
        [
            .. cells.Where(cell => cell.Closing == closing),
        ];
        int assets = terminalCells
            .Select(static cell => cell.AssetIndex)
            .Distinct()
            .Count();
        text.Append("| ")
            .Append(closing)
            .Append(" | ")
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
                .. terminalCells
                    .Where(cell => cell.Column == column)
                    .Select(cell =>
                    {
                        BodyUseScorecardCell oracle =
                            terminalCells.Single(candidate =>
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

    static ProducerTerminal Terminal(
        BodyUseScorecardClosing closing) =>
        closing switch
        {
            BodyUseScorecardClosing.Exists =>
                ProducerTerminal.Exists,
            BodyUseScorecardClosing.Count =>
                ProducerTerminal.Count,
            BodyUseScorecardClosing.Rows =>
                ProducerTerminal.Rows,
            _ => throw new ArgumentOutOfRangeException(nameof(closing)),
        };

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

        internal AnalysisLibraryBodyUseProducer.Result Direct(
            ProducerTerminal terminal)
        {
            var answer =
                new AnalysisLibraryBodyUseProducer.Accumulator(
                _limits.MaximumOccurrences);
            var rows = terminal == ProducerTerminal.Rows
                ? ImmutableArray.CreateBuilder<BodyTypeUseOccurrence>()
                : null;
            int count = 0;
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
                    _cancellationToken.ThrowIfCancellationRequested();
                    MethodDefinition method =
                        _reader.GetMethodDefinition(methodHandle);
                    if (!IsManaged(method))
                        continue;
                    var visit =
                        new AnalysisLibraryBodyUseProducer.VisitFact(
                            Analyze(
                                new(
                                    _reader,
                                    typeHandle,
                                    type,
                                    methodHandle,
                                    method)),
                            terminal);
                    ImmutableArray<BodyTypeUseOccurrence> occurrences =
                        answer.Add(
                            visit,
                            retainOccurrences: false,
                            retainBodies:
                                terminal == ProducerTerminal.Rows);
                    if (terminal == ProducerTerminal.Exists
                        && AnalysisLibraryBodyUseProducer.IsSettling(
                            visit,
                            _limits.MaximumOccurrences))
                    {
                        return answer.Complete(retainRows: false);
                    }
                    if (terminal == ProducerTerminal.Count)
                    {
                        foreach (BodyTypeUseOccurrence _ in occurrences)
                            count++;
                    }
                    else
                    {
                        rows?.AddRange(occurrences);
                    }
                }
            }
            return terminal == ProducerTerminal.Count
                ? answer.CompleteCount(count)
                : terminal == ProducerTerminal.Rows
                    ? answer.CompleteRows(rows!.DrainToImmutable())
                    : answer.Complete(retainRows: false);
        }

        internal AnalysisLibraryBodyUseProducer.Result Linq(
            ProducerTerminal terminal)
        {
            IEnumerable<MethodDefinitionRow> rows =
                _reader.TypeDefinitions
                    .Select(ReadType)
                    .Where(item =>
                        AnalysisLibraryBodyUseProducer
                            .IsTypeInPopulation(
                                _reader,
                                item.Type))
                    .SelectMany(item =>
                        item.Type.GetMethods().Select(methodHandle =>
                            ReadMethod(
                                item.Handle,
                                item.Type,
                                methodHandle)))
                    .Where(IsManagedRow);
            var answer =
                new AnalysisLibraryBodyUseProducer.Accumulator(
                    _limits.MaximumOccurrences);
            if (terminal == ProducerTerminal.Exists)
            {
                _ = rows.Any(row =>
                {
                    var visit =
                        new AnalysisLibraryBodyUseProducer.VisitFact(
                            Analyze(row),
                            terminal);
                    answer.Add(
                        visit,
                        retainOccurrences: false,
                        retainBodies: false);
                    return AnalysisLibraryBodyUseProducer
                        .IsSettling(
                            visit,
                            _limits.MaximumOccurrences);
                });
                return answer.Complete(retainRows: false);
            }

            IEnumerable<BodyTypeUseOccurrence> occurrences = rows
                .Select(row =>
                    new AnalysisLibraryBodyUseProducer.VisitFact(
                        Analyze(row),
                        terminal))
                .SelectMany(visit =>
                    answer.Add(
                        visit,
                        retainOccurrences: false,
                        retainBodies:
                            terminal == ProducerTerminal.Rows));
            return terminal == ProducerTerminal.Count
                ? answer.CompleteCount(occurrences.Count())
                : answer.CompleteRows([.. occurrences]);
        }

        internal AnalysisLibraryBodyUseProducer.Result NLinq(
            ProducerTerminal terminal)
        {
            if (terminal == ProducerTerminal.Exists)
            {
                var existsAnswer =
                    new AnalysisLibraryBodyUseProducer.Accumulator(
                        _limits.MaximumOccurrences);
                _ = new MethodDefinitionRows(_reader)
                    .Any<
                        MethodDefinitionRows,
                        MethodDefinitionRow,
                        HasOccurrence>(new(this, existsAnswer));
                return existsAnswer.Complete(retainRows: false);
            }

            var selected =
                new MethodDefinitionRows(_reader)
                    .Where<
                        MethodDefinitionRows,
                        MethodDefinitionRow,
                        SelectManaged>(new(_cancellationToken));
            var answer =
                new AnalysisLibraryBodyUseProducer.Accumulator(
                    _limits.MaximumOccurrences);
            var occurrences = new OccurrenceRows(
                this,
                selected,
                answer,
                terminal,
                retainBodies:
                    terminal == ProducerTerminal.Rows);
            return terminal == ProducerTerminal.Count
                ? answer.CompleteCount(
                    occurrences.CountFold<
                        OccurrenceRows,
                        BodyTypeUseOccurrence>())
                : answer.CompleteRows(
                    [.. occurrences.ToList<
                        OccurrenceRows,
                        BodyTypeUseOccurrence>()]);
        }

        BodyTypeUseMethodFact Analyze(MethodDefinitionRow row)
        {
            try
            {
                return _runner.AnalyzeBodyTypeUses(
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
            }
            catch (Exception exception)
                when (LibraryMethodAnalysisRunner
                    .IsRecoverableMethodFailure(exception))
            {
                return BodyTypeUseMethodFact.Unavailable(
                    row.TypeHandle,
                    MetadataTokens.GetToken(row.MethodHandle),
                    ProducerFailure.Describe(exception));
            }
        }

        (TypeDefinitionHandle Handle, TypeDefinition Type) ReadType(
            TypeDefinitionHandle handle)
        {
            _cancellationToken.ThrowIfCancellationRequested();
            return (handle, _reader.GetTypeDefinition(handle));
        }

        MethodDefinitionRow ReadMethod(
            TypeDefinitionHandle typeHandle,
            TypeDefinition type,
            MethodDefinitionHandle methodHandle)
        {
            _cancellationToken.ThrowIfCancellationRequested();
            return new(
                _reader,
                typeHandle,
                type,
                methodHandle,
                _reader.GetMethodDefinition(methodHandle));
        }

        bool IsManagedRow(MethodDefinitionRow row)
        {
            _cancellationToken.ThrowIfCancellationRequested();
            return IsManaged(row.Method);
        }

        static bool IsManaged(MethodDefinition method) =>
            method.RelativeVirtualAddress != 0
            && LibraryMethodAnalysisRunner.HasManagedIlBody(
                method.ImplAttributes);

        readonly struct SelectManaged(CancellationToken cancellationToken)
            : IFunc<MethodDefinitionRow, bool>
        {
            readonly CancellationToken _cancellationToken =
                cancellationToken;

            public bool Invoke(MethodDefinitionRow row)
            {
                _cancellationToken.ThrowIfCancellationRequested();
                return AnalysisLibraryBodyUseProducer.IsTypeInPopulation(
                        row.Reader,
                        row.Type)
                    && IsManaged(row.Method);
            }
        }

        ref struct OccurrenceRows(
            OracleContext context,
            Filter<
                MethodDefinitionRow,
                MethodDefinitionRows,
                SelectManaged> methods,
            AnalysisLibraryBodyUseProducer.Accumulator answer,
            ProducerTerminal terminal,
            bool retainBodies)
            : NLinq.IEnumerator<
                OccurrenceRows,
                BodyTypeUseOccurrence>
        {
            readonly OracleContext _context = context;
            Filter<
                MethodDefinitionRow,
                MethodDefinitionRows,
                SelectManaged> _methods = methods;
            readonly AnalysisLibraryBodyUseProducer.Accumulator _answer =
                answer;
            readonly ProducerTerminal _terminal = terminal;
            readonly bool _retainBodies = retainBodies;
            ImmutableArray<BodyTypeUseOccurrence> _occurrences = [];
            int _index;

            public BodyTypeUseOccurrence TryGetNext(
                out bool hasMore)
            {
                while (true)
                {
                    if (_index < _occurrences.Length)
                    {
                        hasMore = true;
                        return _occurrences[_index++];
                    }

                    MethodDefinitionRow row =
                        _methods.TryGetNext(out hasMore);
                    if (!hasMore)
                        return default;

                    _occurrences = _answer.Add(
                        new(_context.Analyze(row), _terminal),
                        retainOccurrences: false,
                        retainBodies: _retainBodies);
                    _index = 0;
                }
            }
        }

        readonly struct HasOccurrence(
            OracleContext context,
            AnalysisLibraryBodyUseProducer.Accumulator answer)
            : IFunc<MethodDefinitionRow, bool>
        {
            public bool Invoke(MethodDefinitionRow row)
            {
                var selected = new SelectManaged(
                    context._cancellationToken);
                if (!selected.Invoke(row))
                    return false;
                var visit =
                    new AnalysisLibraryBodyUseProducer.VisitFact(
                        context.Analyze(row),
                        ProducerTerminal.Exists);
                answer.Add(visit);
                return AnalysisLibraryBodyUseProducer
                    .IsSettling(
                        visit,
                        context._limits.MaximumOccurrences);
            }
        }
    }
}
