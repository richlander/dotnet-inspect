using System.Collections.Immutable;
using System.Diagnostics;
using System.Globalization;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

using DotnetInspector.PerformanceOracles;
using ILInspector.Analysis;
using ILInspector.Analysis.Planning;
using ILInspector.Metadata;
using NLinq;

namespace ILInspector.AnalysisHarness;

public static class BodyUseScorecardKernel
{
    public static IReadOnlyList<BodyUseScorecardColumn> Columns { get; } =
    [
        BodyUseScorecardColumn.Direct,
        BodyUseScorecardColumn.Linq,
        BodyUseScorecardColumn.NLinq,
        BodyUseScorecardColumn.Planner,
    ];

    public static IReadOnlyList<BodyUseScorecardClosing> Closings { get; } =
    [
        BodyUseScorecardClosing.Exists,
        BodyUseScorecardClosing.Count,
        BodyUseScorecardClosing.Rows,
    ];

    static long s_sink;

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
                        offset < Columns.Count;
                        offset++)
                    {
                        BodyUseScorecardColumn column =
                            Columns[(round + offset) % Columns.Count];
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

    public static string Name(BodyUseScorecardColumn column) =>
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
