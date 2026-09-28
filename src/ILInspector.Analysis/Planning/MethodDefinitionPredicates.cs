using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

namespace ILInspector.Analysis.Planning;

/// <summary>
/// An open query over method-definition units: a per-unit predicate with no
/// terminal. A struct, so a kernel specialized to it calls it directly.
/// </summary>
public interface IMethodDefinitionPredicate
{
    bool Test(scoped MethodDefinitionView view);
}

/// <summary>
/// A producer for an open query. The request's terminal closes it: Exists
/// stops at the first unit that satisfies the predicate; otherwise the result
/// is the number of units that do. A pass that visits only this producer runs
/// as a closed-query kernel specialized to the predicate and the terminal.
/// </summary>
public abstract class MethodDefinitionPredicateProducer<TPredicate>
    : MethodDefinitionProducer<bool, int, int>
    where TPredicate : struct, IMethodDefinitionPredicate
{
    private protected MethodDefinitionPredicateProducer(
        string identity,
        int version,
        int tier,
        MethodDefinitionLayers layers)
        : base(identity, version, tier, layers)
    {
    }

    /// <summary>Experiment toggle: whether a closed query over this predicate may run as a kernel.</summary>
    internal virtual bool AllowsKernel => true;

    internal sealed override bool Visit(scoped MethodDefinitionView view) =>
        default(TPredicate).Test(view);

    internal sealed override int Seed() => 0;

    internal sealed override int Accumulate(int accumulator, bool fact) =>
        fact ? accumulator + 1 : accumulator;

    internal sealed override int Complete(
        int accumulator,
        MethodDefinitionCompletionView completion) =>
        accumulator;

    internal sealed override bool Settles(bool fact) => fact;

    internal override bool RunKernel(
        MethodDefinitionExecution.ProducerState state,
        MetadataReader reader,
        PEReader peReader,
        LibraryMethodAnalysisRunner? lookup,
        MethodRowGate gate,
        out int unitsVisited)
    {
        unitsVisited = 0;
        if (!AllowsKernel)
            return false;

        bool exists = state.Terminal == ProducerTerminal.Exists;
        bool typeScoped = HasTypeScope;
        SourceGateGuard? sourceGate = SourceGate;
        TPredicate predicate = default;
        var unit = new MethodDefinitionUnit(reader, peReader, lookup, gate);
        int visited = 0;
        int attempted = 0;
        int completed = 0;
        int count = 0;

        // An abort unwinds this loop; the counts observed so far still reach
        // the receipt, but the accumulator is never published.
        try
        {
            foreach (TypeDefinitionHandle typeHandle in reader.TypeDefinitions)
            {
                TypeDefinition typeDefinition = reader.GetTypeDefinition(typeHandle);
                if (sourceGate is not null)
                {
                    bool gateInScope;
                    try
                    {
                        gateInScope = gate.TypeInScope(state.GateCache ??= gate.Resolve(sourceGate.Classifier), typeHandle, typeDefinition);
                    }
                    catch (Exception ex)
                        when (LibraryMethodAnalysisRunner.IsRecoverableMethodFailure(ex))
                    {
                        Fail(state, MetadataTokens.GetToken(typeHandle), "(type scope)", ex);
                        goto Done;
                    }

                    if (!gateInScope)
                        continue;
                }

                if (typeScoped)
                {
                    bool inScope;
                    try
                    {
                        inScope = TypeInScope(reader, typeDefinition);
                    }
                    catch (Exception ex)
                        when (LibraryMethodAnalysisRunner.IsRecoverableMethodFailure(ex))
                    {
                        Fail(state, MetadataTokens.GetToken(typeHandle), "(type scope)", ex);
                        goto Done;
                    }

                    if (!inScope)
                        continue;
                }

                foreach (MethodDefinitionHandle methodHandle in typeDefinition.GetMethods())
                {
                    unit.MoveTo(typeHandle, typeDefinition, methodHandle);
                    visited++;
                state.Execution.PassUnitsVisited = visited;
                    if (sourceGate is not null)
                    {
                        bool? accepted = MethodDefinitionExecution.GateAccepts(ref unit, state, sourceGate);
                        if (accepted is null)
                            goto Done;
                        if (!accepted.Value)
                            continue;
                    }

                    attempted++;
                    bool fact;
                    try
                    {
                        fact = predicate.Test(new MethodDefinitionView(ref unit, state));
                    }
                    catch (Exception ex)
                        when (LibraryMethodAnalysisRunner.IsRecoverableMethodFailure(ex))
                    {
                        state.UnitsFailed++;
                        Fail(state, MetadataTokens.GetToken(methodHandle), unit.Label, ex);
                        goto Done;
                    }

                    completed++;
                    if (!fact)
                        continue;
                    count++;

                    // Stop before advancing either enumerator, as the reference
                    // executor does, once the Exists terminal is settled.
                    if (exists)
                    {
                        state.Outcome = ProducerOutcome.Stopped;
                        state.IsActive = false;
                        goto Done;
                    }
                }
            }

        }
        catch (ProducerAbortException)
        {
            state.UnitsAttempted += attempted;
            state.UnitsCompleted += completed;
            throw;
        }

    Done:
        state.UnitsAttempted += attempted;
        state.UnitsCompleted += completed;
        ((State)state).SetAccumulator(count);
        unitsVisited = visited;
        return true;
    }

    static void Fail(
        MethodDefinitionExecution.ProducerState state,
        int token,
        string unit,
        Exception ex)
    {
        state.Outcome = ProducerOutcome.Failed;
        state.Failure = new ProducerFailure(token, unit, ProducerFailure.Describe(ex));
        state.IsActive = false;
    }
}

/// <summary>
/// A projection of one unit that satisfies an open query, read only under the
/// <see cref="ProducerTerminal.Rows"/> closing. A struct, so a kernel
/// specialized to it calls it directly.
/// </summary>
public interface IMethodDefinitionProjection<TRow>
{
    TRow Project(scoped MethodDefinitionView view);
}

/// <summary>
/// A closed open query's result: how many units satisfy it, and, under the
/// Rows closing, their projected rows in unit order. Count and Exists derived
/// from Rows are the same count.
/// </summary>
public sealed record ClosedQueryResult<TRow>(int Count, ImmutableArray<TRow> Rows)
{
    public bool Exists => Count > 0;

    public bool HasRows => !Rows.IsDefault;
}

/// <summary>One unit's fact for an open query with a projection.</summary>
public readonly record struct QueryFact<TRow>(bool Matched, TRow? Row);

/// <summary>The folded count and, under the Rows closing, the rows.</summary>
public sealed class QueryAccumulator<TRow>
{
    internal int Count;
    internal List<TRow>? Rows;
}

/// <summary>
/// An open query whose request chooses its closing: Exists stops at the first
/// unit that satisfies the predicate, Complete counts them, and Rows also projects
/// each one. The projection's fields, such as identity text, are declared
/// only for the Rows closing, so Count and Exists never read them. A pass that
/// visits only this producer runs as a closed-query kernel specialized to the
/// predicate, the projection, and the closing.
/// </summary>
public abstract class MethodDefinitionQueryProducer<TPredicate, TProjection, TRow>
    : MethodDefinitionProducer<QueryFact<TRow>, QueryAccumulator<TRow>, ClosedQueryResult<TRow>>
    where TPredicate : struct, IMethodDefinitionPredicate
    where TProjection : struct, IMethodDefinitionProjection<TRow>
{
    readonly MethodDefinitionLayers _rowLayers;

    private protected MethodDefinitionQueryProducer(
        string identity,
        int version,
        int tier,
        MethodDefinitionLayers layers,
        MethodDefinitionLayers rowLayers)
        : base(identity, version, tier, layers)
    {
        _rowLayers = rowLayers;
    }

    internal sealed override MethodDefinitionLayers RowLayers => _rowLayers;

    /// <summary>Whether a closed query over this producer may run as a kernel.</summary>
    internal virtual bool AllowsKernel => true;

    internal sealed override QueryFact<TRow> Visit(scoped MethodDefinitionView view)
    {
        if (!default(TPredicate).Test(view))
            return default;
        return view.Terminal == ProducerTerminal.Rows
            ? new QueryFact<TRow>(true, default(TProjection).Project(view))
            : new QueryFact<TRow>(true, default);
    }

    internal sealed override QueryAccumulator<TRow> Seed() => new();

    internal sealed override QueryAccumulator<TRow> Accumulate(
        QueryAccumulator<TRow> accumulator,
        QueryFact<TRow> fact)
    {
        if (fact.Matched)
        {
            accumulator.Count++;
            if (fact.Row is { } row)
                (accumulator.Rows ??= []).Add(row);
        }

        return accumulator;
    }

    internal sealed override ClosedQueryResult<TRow> Complete(
        QueryAccumulator<TRow> accumulator,
        MethodDefinitionCompletionView completion) =>
        new(
            accumulator.Count,
            completion.Terminal == ProducerTerminal.Rows
                ? accumulator.Rows is { } rows ? [.. rows] : []
                : default);

    internal sealed override bool Settles(QueryFact<TRow> fact) => fact.Matched;

    internal override bool RunKernel(
        MethodDefinitionExecution.ProducerState state,
        MetadataReader reader,
        PEReader peReader,
        LibraryMethodAnalysisRunner? lookup,
        MethodRowGate gate,
        out int unitsVisited)
    {
        unitsVisited = 0;
        if (!AllowsKernel)
            return false;

        bool exists = state.Terminal == ProducerTerminal.Exists;
        bool rows = state.Terminal == ProducerTerminal.Rows;
        bool typeScoped = HasTypeScope;
        SourceGateGuard? sourceGate = SourceGate;
        TPredicate predicate = default;
        TProjection projection = default;
        var unit = new MethodDefinitionUnit(reader, peReader, lookup, gate);
        var accumulator = new QueryAccumulator<TRow>();
        int visited = 0;
        int attempted = 0;
        int completed = 0;

        // An abort unwinds this loop; the counts observed so far still reach
        // the receipt, but the accumulator is never published.
        try
        {
            foreach (TypeDefinitionHandle typeHandle in reader.TypeDefinitions)
            {
                TypeDefinition typeDefinition = reader.GetTypeDefinition(typeHandle);
                try
                {
                    if (sourceGate is not null
                        && !gate.TypeInScope(state.GateCache ??= gate.Resolve(sourceGate.Classifier), typeHandle, typeDefinition))
                    {
                        continue;
                    }

                    if (typeScoped && !TypeInScope(reader, typeDefinition))
                        continue;
                }
                catch (Exception ex)
                    when (LibraryMethodAnalysisRunner.IsRecoverableMethodFailure(ex))
                {
                    QueryFail(state, MetadataTokens.GetToken(typeHandle), "(type scope)", ex);
                    goto Done;
                }

                foreach (MethodDefinitionHandle methodHandle in typeDefinition.GetMethods())
                {
                    unit.MoveTo(typeHandle, typeDefinition, methodHandle);
                    visited++;
                state.Execution.PassUnitsVisited = visited;
                    if (sourceGate is not null)
                    {
                        bool? accepted = MethodDefinitionExecution.GateAccepts(ref unit, state, sourceGate);
                        if (accepted is null)
                            goto Done;
                        if (!accepted.Value)
                            continue;
                    }

                    attempted++;
                    try
                    {
                        var view = new MethodDefinitionView(ref unit, state);
                        if (predicate.Test(view))
                        {
                            accumulator.Count++;
                            if (rows)
                                (accumulator.Rows ??= []).Add(projection.Project(view));
                        }
                    }
                    catch (Exception ex)
                        when (LibraryMethodAnalysisRunner.IsRecoverableMethodFailure(ex))
                    {
                        state.UnitsFailed++;
                        QueryFail(state, MetadataTokens.GetToken(methodHandle), unit.Label, ex);
                        goto Done;
                    }

                    completed++;

                    // Stop before advancing either enumerator, as the reference
                    // executor does, once the Exists terminal is settled.
                    if (exists && accumulator.Count > 0)
                    {
                        state.Outcome = ProducerOutcome.Stopped;
                        state.IsActive = false;
                        goto Done;
                    }
                }
            }

        }
        catch (ProducerAbortException)
        {
            state.UnitsAttempted += attempted;
            state.UnitsCompleted += completed;
            throw;
        }

    Done:
        state.UnitsAttempted += attempted;
        state.UnitsCompleted += completed;
        ((State)state).SetAccumulator(accumulator);
        unitsVisited = visited;
        return true;
    }

    static void QueryFail(
        MethodDefinitionExecution.ProducerState state,
        int token,
        string unit,
        Exception ex)
    {
        state.Outcome = ProducerOutcome.Failed;
        state.Failure = new ProducerFailure(token, unit, $"{ex.GetType().Name}: {ex.Message}");
        state.IsActive = false;
    }
}
