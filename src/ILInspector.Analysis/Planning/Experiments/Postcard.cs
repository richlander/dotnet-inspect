using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

using ILInspector.Metadata;

namespace ILInspector.Analysis.Planning.Experiments;

// Experiment only: the "postcard" comparison. One open query (the dense
// population: public, non-accessor methods on types whose name does not
// start with '<', projected to a text row) closed six ways, measured Before
// (build every row, then LINQ), NLinq (in the probe), and After (the planner).

/// <summary>The dense open query's predicate, with its type filter as a type scope.</summary>
public struct DenseMethodPredicate : IMethodDefinitionPredicate
{
    public bool Test(scoped MethodDefinitionView view) =>
        ClassifiedScope.IsScopedMethod(view.Reader, view.MethodDefinition);
}

/// <summary>Exists and Count: the open query closed through the closed-query kernel, as the stack ships it.</summary>
public sealed class DensePredicateProducer : MethodDefinitionPredicateProducer<DenseMethodPredicate>
{
    DensePredicateProducer()
        : base("Experiment.Postcard.DensePredicate", 1, 0, MethodDefinitionLayers.Declaration)
    {
    }

    public static DensePredicateProducer Instance { get; } = new();

    internal override bool HasTypeScope => true;

    internal override bool TypeInScope(MetadataReader reader, TypeDefinition type) =>
        ClassifiedScope.IsScopedType(reader, type);
}

/// <summary>
/// Rows: a producer written against the fold contract as the stack ships it
/// (#8734), run by the interpreted reference executor. Its fact is the unit's
/// row when selected.
/// </summary>
public sealed class DenseRowsFoldProducer
    : MethodDefinitionProducer<ProjectedRow<MethodTextRow>, ImmutableArray<MethodTextRow>.Builder, ImmutableArray<MethodTextRow>>
{
    DenseRowsFoldProducer()
        : base("Experiment.Postcard.DenseRows", 1, 0, MethodDefinitionLayers.Declaration)
    {
    }

    public static DenseRowsFoldProducer Instance { get; } = new();

    internal override bool HasTypeScope => true;

    internal override bool TypeInScope(MetadataReader reader, TypeDefinition type) =>
        ClassifiedScope.IsScopedType(reader, type);

    internal override ProjectedRow<MethodTextRow> Visit(scoped MethodDefinitionView view) =>
        ClassifiedScope.IsScopedMethod(view.Reader, view.MethodDefinition)
            ? new(true, MethodText.Row(view.Reader, view.TypeDefinition, view.MethodDefinition))
            : default;

    internal override ImmutableArray<MethodTextRow>.Builder Seed() => ImmutableArray.CreateBuilder<MethodTextRow>();

    internal override ImmutableArray<MethodTextRow>.Builder Accumulate(
        ImmutableArray<MethodTextRow>.Builder accumulator,
        ProjectedRow<MethodTextRow> fact)
    {
        if (fact.Selected)
            accumulator.Add(fact.Row);
        return accumulator;
    }

    internal override ImmutableArray<MethodTextRow> Complete(
        ImmutableArray<MethodTextRow>.Builder accumulator,
        MethodDefinitionCompletionView completion) =>
        accumulator.DrainToImmutable();
}

/// <summary>Rows over the async population, as a fold-contract producer run by the reference executor.</summary>
public sealed class AsyncRowsFoldProducer
    : MethodDefinitionProducer<ProjectedRow<MethodTextRow>, ImmutableArray<MethodTextRow>.Builder, ImmutableArray<MethodTextRow>>
{
    AsyncRowsFoldProducer()
        : base("Experiment.Postcard.AsyncRows", 1, 0, MethodDefinitionLayers.Declaration)
    {
    }

    public static AsyncRowsFoldProducer Instance { get; } = new();

    internal override bool HasTypeScope => true;

    internal override bool TypeInScope(MetadataReader reader, TypeDefinition type) =>
        AsyncMethodScope.IsCountedType(reader, type);

    internal override ProjectedRow<MethodTextRow> Visit(scoped MethodDefinitionView view) =>
        AsyncMethodScope.IsCountedMethod(view.Reader, view.MethodDefinition)
            ? new(true, MethodText.Row(view.Reader, view.TypeDefinition, view.MethodDefinition))
            : default;

    internal override ImmutableArray<MethodTextRow>.Builder Seed() => ImmutableArray.CreateBuilder<MethodTextRow>();

    internal override ImmutableArray<MethodTextRow>.Builder Accumulate(
        ImmutableArray<MethodTextRow>.Builder accumulator,
        ProjectedRow<MethodTextRow> fact)
    {
        if (fact.Selected)
            accumulator.Add(fact.Row);
        return accumulator;
    }

    internal override ImmutableArray<MethodTextRow> Complete(
        ImmutableArray<MethodTextRow>.Builder accumulator,
        MethodDefinitionCompletionView completion) =>
        accumulator.DrainToImmutable();
}

/// <summary>A closing's answer, compared across the columns.</summary>
public readonly record struct PostcardAnswer(bool? Exists, int? Count, IReadOnlyList<MethodTextRow>? Rows, bool Failed);

public static class Postcard
{
    public const int N = 6;
    public const int WindowFirst = 100;
    public const int WindowLast = 110;

    static readonly WorkDescription s_exists = Plan(new ProducerRequest(DensePredicateProducer.Instance, ProducerTerminal.Exists));
    static readonly WorkDescription s_count = Plan(new ProducerRequest(DensePredicateProducer.Instance, ProducerTerminal.All));
    static readonly WorkDescription s_rows = Plan(new ProducerRequest(DenseRowsFoldProducer.Instance));

    static WorkDescription Plan(ProducerRequest request) =>
        ProducerPlanner.Plan([request]) is ProducerPlanResult.Accepted accepted
            ? accepted.Description
            : throw new InvalidOperationException("The experiment request must plan.");

    // After -------------------------------------------------------------

    public static PostcardAnswer After(string closing, string path, PEReader peReader)
    {
        switch (closing)
        {
            case "exists":
                return new(PredicateValue(s_exists, path, peReader) > 0, null, null, false);
            case "count":
                return new(null, PredicateValue(s_count, path, peReader), null, false);
            case "rows":
            {
                ProducerResult<ImmutableArray<MethodTextRow>> result = MethodDefinitionExecution
                    .Execute(s_rows, path, peReader)
                    .ResultOf(DenseRowsFoldProducer.Instance);
                return result.HasValue
                    ? new(null, null, result.Value, false)
                    : throw new InvalidDataException(result.Failure?.Message);
            }
            case "head":
                return FromWindow(RowPhaseKernel.Run<PublicMethodPredicate, MethodTextProjection, MethodTextRow>(
                    peReader, RowWindowRequest.Head(N)));
            case "window":
                return FromWindow(RowPhaseKernel.Run<PublicMethodPredicate, MethodTextProjection, MethodTextRow>(
                    peReader, RowWindowRequest.Window(WindowFirst, WindowLast)));
            case "tail":
                return new(null, null, TailKernel<PublicMethodPredicate, MethodTextProjection, MethodTextRow>(peReader, N), false);
            default:
                throw new ArgumentException(closing);
        }
    }

    static int PredicateValue(WorkDescription description, string path, PEReader peReader)
    {
        ProducerResult<int> result = MethodDefinitionExecution
            .Execute(description, path, peReader)
            .ResultOf(DensePredicateProducer.Instance);
        return result.HasValue ? result.Value : throw new InvalidDataException(result.Failure?.Message);
    }

    /// <summary>
    /// A strict window reached position B only when it returned every row;
    /// otherwise it is the row selection owner's failure, never a shorter
    /// success.
    /// </summary>
    static PostcardAnswer FromWindow(RowWindowResult<MethodTextRow> result) =>
        result.WindowStartMissing
            ? new(null, null, null, true)
            : new(null, null, result.Rows, false);

    /// <summary>
    /// Tail(N) as the phase design lowers it: Buffer(N) until never, keeping
    /// only the last N selected units' handles, then projecting those N.
    /// </summary>
    internal static ImmutableArray<TRow> TailKernel<TPredicate, TProjection, TRow>(PEReader peReader, int n)
        where TPredicate : struct, IRowPredicate
        where TProjection : struct, IRowProjection<TRow>
    {
        MetadataReader reader = peReader.GetMetadataReader();
        TPredicate predicate = default;
        TProjection projection = default;
        var unit = new MethodDefinitionUnit(reader, peReader, lookup: null);
        Span<(TypeDefinitionHandle Type, MethodDefinitionHandle Method)> ring =
            new (TypeDefinitionHandle, MethodDefinitionHandle)[n];
        int selected = 0;
        foreach (TypeDefinitionHandle typeHandle in reader.TypeDefinitions)
        {
            TypeDefinition type = reader.GetTypeDefinition(typeHandle);
            if (!predicate.TypeInScope(reader, type))
                continue;
            foreach (MethodDefinitionHandle methodHandle in type.GetMethods())
            {
                unit.MoveTo(typeHandle, type, methodHandle);
                if (predicate.Test(ref unit))
                    ring[selected++ % n] = (typeHandle, methodHandle);
            }
        }

        int count = Math.Min(selected, n);
        ImmutableArray<TRow>.Builder rows = ImmutableArray.CreateBuilder<TRow>(count);
        for (int i = selected - count; i < selected; i++)
        {
            (TypeDefinitionHandle typeHandle, MethodDefinitionHandle methodHandle) = ring[i % n];
            unit.MoveTo(typeHandle, reader.GetTypeDefinition(typeHandle), methodHandle);
            rows.Add(projection.Project(ref unit));
        }

        return rows.MoveToImmutable();
    }

    // Naive Planner -----------------------------------------------------

    /// <summary>The planner asked for Rows, then the closing applied with LINQ over the returned rows.</summary>
    public static PostcardAnswer NaivePlanner(string closing, string path, PEReader peReader) =>
        FromList(closing, After("rows", path, peReader).Rows!);

    // Async population --------------------------------------------------

    static readonly WorkDescription s_asyncRows = Plan(new ProducerRequest(AsyncRowsFoldProducer.Instance));

    /// <summary>The planner over the async population, each closing pushed into the plan.</summary>
    public static PostcardAnswer AsyncPlanner(string closing, string path, PEReader peReader)
    {
        switch (closing)
        {
            case "exists":
                return new(AsyncClosedQueries.Exists(kernel: true, path, peReader), null, null, false);
            case "count":
                return new(null, AsyncClosedQueries.Count(kernel: true, path, peReader), null, false);
            case "rows":
            {
                ProducerResult<ImmutableArray<MethodTextRow>> result = MethodDefinitionExecution
                    .Execute(s_asyncRows, path, peReader)
                    .ResultOf(AsyncRowsFoldProducer.Instance);
                return result.HasValue
                    ? new(null, null, result.Value, false)
                    : throw new InvalidDataException(result.Failure?.Message);
            }
            case "head":
                return FromWindow(RowPhaseKernel.Run<AsyncMethodRowPredicate, MethodTextProjection, MethodTextRow>(
                    peReader, RowWindowRequest.Head(N)));
            case "window":
            {
                RowWindowResult<MethodTextRow> result = RowPhaseKernel.Run<AsyncMethodRowPredicate, MethodTextProjection, MethodTextRow>(
                    peReader, RowWindowRequest.Window(WindowFirst, WindowLast));
                // Strict: the window succeeds only with every row from A to B.
                return result.Rows.Length == WindowLast - WindowFirst + 1
                    ? new(null, null, result.Rows, false)
                    : new(null, null, null, true);
            }
            case "tail":
                return new(null, null, TailKernel<AsyncMethodRowPredicate, MethodTextProjection, MethodTextRow>(peReader, N), false);
            default:
                throw new ArgumentException(closing);
        }
    }

    /// <summary>
    /// The legacy product code on a question it answers: MethodClassificationScanner
    /// rows filtered to async, then LINQ. A method that is both async and has a
    /// pointer signature emits an async row and an Unsafe row; filtering to async
    /// keeps one row per method, so no deduplication is needed.
    /// </summary>
    public static PostcardAnswer AsyncOld(string closing, PEReader peReader)
    {
        List<MethodTextRow> rows = MethodClassificationScanner.Scan(peReader)
            .Where(static m => m.Classification is MethodClassification.RuntimeAsync or MethodClassification.StateMachineAsync)
            .Select(static m => new MethodTextRow(m.MethodName, m.DeclaringType, m.Signature))
            .ToList();
        return FromList(closing, rows);
    }

    // Old ---------------------------------------------------------------

    /// <summary>
    /// The legacy product code as the CLI runs it today: MethodClassificationScanner
    /// rows, then LINQ. The scanner applies the dense scope itself (public,
    /// non-accessor, type name not starting with '&lt;') but emits a row only for
    /// a P/Invoke, async, or pointer-signature method, and a method that is both
    /// async and has a pointer signature emits two rows. Its rows carry no
    /// visibility or raw type-name field, so no further filter is possible or
    /// needed; the answers are those of the classified subset.
    /// </summary>
    public static PostcardAnswer Old(string closing, PEReader peReader)
    {
        List<MethodTextRow> all = MethodClassificationScanner.Scan(peReader)
            .Select(static m => new MethodTextRow(m.MethodName, m.DeclaringType, m.Signature))
            .ToList();
        return FromList(closing, all);
    }

    // LINQ --------------------------------------------------------------

    /// <summary>An idiomatic streaming System.Linq pipeline, written as an ordinary C# author would.</summary>
    public static PostcardAnswer Linq(string closing, PEReader peReader)
    {
        MetadataReader reader = peReader.GetMetadataReader();
        IEnumerable<MethodTextRow> rows = reader.TypeDefinitions
            .Select(reader.GetTypeDefinition)
            .SelectMany(type => type.GetMethods().Select(handle => (Type: type, Method: reader.GetMethodDefinition(handle))))
            .Where(m => ClassifiedScope.IsScopedType(reader, m.Type) && ClassifiedScope.IsScopedMethod(reader, m.Method))
            .Select(m => MethodText.Row(reader, m.Type, m.Method));
        switch (closing)
        {
            case "exists":
                return new(rows.Any(), null, null, false);
            case "count":
                return new(null, rows.Count(), null, false);
            case "head":
                return new(null, null, rows.Take(N).ToList(), false);
            case "tail":
                return new(null, null, rows.TakeLast(N).ToList(), false);
            case "rows":
                return new(null, null, rows.ToList(), false);
            case "window":
            {
                int take = WindowLast - WindowFirst + 1;
                List<MethodTextRow> window = rows.Skip(WindowFirst - 1).Take(take).ToList();
                return window.Count == take ? new(null, null, window, false) : new(null, null, null, true);
            }
            default:
                throw new ArgumentException(closing);
        }
    }

    static PostcardAnswer FromList(string closing, IReadOnlyList<MethodTextRow> all) => closing switch
    {
        "exists" => new(all.Any(), null, null, false),
        "count" => new(null, all.Count(), null, false),
        "head" => new(null, null, all.Take(N).ToList(), false),
        "tail" => new(null, null, all.TakeLast(N).ToList(), false),
        "rows" => new(null, null, all, false),
        "window" => all.Count >= WindowLast
            ? new(null, null, all.Skip(WindowFirst - 1).Take(WindowLast - WindowFirst + 1).ToList(), false)
            : new(null, null, null, true),
        _ => throw new ArgumentException(closing),
    };

    // Materialize -------------------------------------------------------

    /// <summary>Today's shape: build every row, then answer with ordinary LINQ over them.</summary>
    public static PostcardAnswer Before(string closing, PEReader peReader)
    {
        ImmutableArray<MethodTextRow> all = RowWindowExperiment.Hand(dense: true, peReader, RowWindowRequest.All).Rows;
        return closing switch
        {
            "exists" => new(all.Any(), null, null, false),
            "count" => new(null, all.Count(), null, false),
            "head" => new(null, null, [.. all.Take(N)], false),
            "tail" => new(null, null, [.. all.TakeLast(N)], false),
            "rows" => new(null, null, all, false),
            "window" => all.Length >= WindowLast
                ? new(null, null, [.. all.Skip(WindowFirst - 1).Take(WindowLast - WindowFirst + 1)], false)
                : new(null, null, null, true),
            _ => throw new ArgumentException(closing),
        };
    }
}
