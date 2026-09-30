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
/// stops at the first unit that satisfies the predicate; a Rows row limit
/// stops at its Nth match; otherwise the result is the number of units that
/// satisfy it. A pass that visits only this producer runs as a closed-query
/// kernel specialized to the predicate and the terminal.
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

        if (state.RowLimit is int rowLimit)
        {
            return RunKernelCore(
                state,
                reader,
                peReader,
                lookup,
                gate,
                new HeadKernelStop(rowLimit),
                out unitsVisited);
        }

        return state.Terminal == ProducerTerminal.Exists
            ? RunKernelCore(
                state,
                reader,
                peReader,
                lookup,
                gate,
                default(ExistsKernelStop),
                out unitsVisited)
            : RunKernelCore(
                state,
                reader,
                peReader,
                lookup,
                gate,
                default(ExhaustiveKernelStop),
                out unitsVisited);
    }

    bool RunKernelCore<TStop>(
        MethodDefinitionExecution.ProducerState state,
        MetadataReader reader,
        PEReader peReader,
        LibraryMethodAnalysisRunner? lookup,
        MethodRowGate gate,
        TStop stop,
        out int unitsVisited)
        where TStop : struct, IKernelStop
    {
        unitsVisited = 0;
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

                    // Stop before advancing either enumerator once the
                    // requested closing is settled.
                    if (stop.ShouldStop(count))
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
/// <see cref="ProducerTerminal.Rows"/> closing, including Head(N). A struct,
/// so a kernel specialized to it calls it directly.
/// </summary>
public interface IMethodDefinitionProjection<TRow>
{
    TRow Project(scoped MethodDefinitionView view);
}

/// <summary>
/// A closed open query's result: how many units satisfy it, and, under the
/// Rows closing, their projected rows in unit order. A limited Rows closing
/// contains at most its requested Head(N). Count and Exists derived from these
/// rows are the selected-row count and presence.
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
/// unit that satisfies the predicate, Complete counts them, Rows projects each
/// match, and a limited Rows request stops at its Nth projected match. The
/// projection's fields, such as identity text, are declared only for the Rows
/// closing, so Count and Exists never read them. A pass that visits only this
/// producer runs as a closed-query kernel specialized to the predicate, the
/// projection, and the closing.
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
        if (SourceGate is not { } sourceGate)
            return RunKernelForStop<UngatedKernel>(
                state,
                reader,
                peReader,
                lookup,
                gate,
                null!,
                out unitsVisited);
        return RunGatedKernel(state, reader, peReader, lookup, gate, sourceGate, out unitsVisited);
    }

    /// <summary>
    /// The kernel for a gated producer. The base resolves the classifier's
    /// cache once and tests through it; a producer whose gate classification
    /// is a struct specializes the kernel to it instead.
    /// </summary>
    private protected virtual bool RunGatedKernel(
        MethodDefinitionExecution.ProducerState state,
        MetadataReader reader,
        PEReader peReader,
        LibraryMethodAnalysisRunner? lookup,
        MethodRowGate gate,
        SourceGateGuard sourceGate,
        out int unitsVisited) =>
        RunKernelForStop<CachedGateKernel>(
            state,
            reader,
            peReader,
            lookup,
            gate,
            sourceGate,
            out unitsVisited);

    private protected bool RunKernelForStop<TGate>(
        MethodDefinitionExecution.ProducerState state,
        MetadataReader reader,
        PEReader peReader,
        LibraryMethodAnalysisRunner? lookup,
        MethodRowGate gate,
        SourceGateGuard sourceGate,
        out int unitsVisited)
        where TGate : struct, IKernelGate
    {
        if (state.RowLimit is int rowLimit)
        {
            return RunKernelCore<TGate, HeadKernelStop>(
                state,
                reader,
                peReader,
                lookup,
                gate,
                sourceGate,
                new HeadKernelStop(rowLimit),
                out unitsVisited);
        }

        return state.Terminal == ProducerTerminal.Exists
            ? RunKernelCore<TGate, ExistsKernelStop>(
                state,
                reader,
                peReader,
                lookup,
                gate,
                sourceGate,
                default,
                out unitsVisited)
            : RunKernelCore<TGate, ExhaustiveKernelStop>(
                state,
                reader,
                peReader,
                lookup,
                gate,
                sourceGate,
                default,
                out unitsVisited);
    }

    /// <summary>
    /// The closed-query kernel: one loop specialized to the producer's gate,
    /// predicate, and projection. The gate strategy decides only how scope
    /// and class are tested; containment, early stop, and receipts are the
    /// same for every strategy.
    /// </summary>
    private protected bool RunKernelCore<TGate, TStop>(
        MethodDefinitionExecution.ProducerState state,
        MetadataReader reader,
        PEReader peReader,
        LibraryMethodAnalysisRunner? lookup,
        MethodRowGate gate,
        SourceGateGuard sourceGate,
        TStop stop,
        out int unitsVisited)
        where TGate : struct, IKernelGate
        where TStop : struct, IKernelStop
    {
        bool rows = state.Terminal == ProducerTerminal.Rows;
        bool typeScoped = HasTypeScope;
        TPredicate predicate = default;
        TProjection projection = default;
        TGate gateTests = default;
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
                    if (gateTests.Gated
                        && !gateTests.TypeInScope(state, gate, sourceGate, typeHandle, typeDefinition))
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
                    if (gateTests.Gated)
                    {
                        bool? accepted = gateTests.Accepts(ref unit, state, sourceGate);
                        if (accepted is null)
                            goto Done;
                        if (!accepted.Value)
                            continue;
                    }

                    attempted++;
                    try
                    {
                        var view = new MethodDefinitionView(ref unit, state);
                        if (!predicate.Test(view))
                        {
                            completed++;
                            continue;
                        }

                        accumulator.Count++;
                        if (rows)
                            (accumulator.Rows ??= []).Add(projection.Project(view));
                    }
                    catch (Exception ex)
                        when (LibraryMethodAnalysisRunner.IsRecoverableMethodFailure(ex))
                    {
                        state.UnitsFailed++;
                        QueryFail(state, MetadataTokens.GetToken(methodHandle), unit.Label, ex);
                        goto Done;
                    }

                    completed++;

                    // Stop before advancing either enumerator once the
                    // requested closing is settled.
                    if (stop.ShouldStop(accumulator.Count))
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

internal interface IKernelStop
{
    bool ShouldStop(int count);
}

internal readonly struct ExhaustiveKernelStop : IKernelStop
{
    public bool ShouldStop(int count) => false;
}

internal readonly struct ExistsKernelStop : IKernelStop
{
    public bool ShouldStop(int count) => true;
}

internal readonly struct HeadKernelStop(int limit) : IKernelStop
{
    public bool ShouldStop(int count) => count >= limit;
}

/// <summary>How a closed-query kernel tests its source gate.</summary>
internal interface IKernelGate
{
    bool Gated { get; }

    bool TypeInScope(
        MethodDefinitionExecution.ProducerState state,
        MethodRowGate gate,
        SourceGateGuard guard,
        TypeDefinitionHandle typeHandle,
        TypeDefinition typeDefinition);

    bool? Accepts(
        ref MethodDefinitionUnit unit,
        MethodDefinitionExecution.ProducerState state,
        SourceGateGuard guard);
}

/// <summary>A producer with no source gate.</summary>
internal readonly struct UngatedKernel : IKernelGate
{
    public bool Gated => false;

    public bool TypeInScope(
        MethodDefinitionExecution.ProducerState state,
        MethodRowGate gate,
        SourceGateGuard guard,
        TypeDefinitionHandle typeHandle,
        TypeDefinition typeDefinition) => true;

    public bool? Accepts(
        ref MethodDefinitionUnit unit,
        MethodDefinitionExecution.ProducerState state,
        SourceGateGuard guard) => true;
}

/// <summary>A gate whose classifier is tested through its resolved per-execution cache.</summary>
internal readonly struct CachedGateKernel : IKernelGate
{
    public bool Gated => true;

    public bool TypeInScope(
        MethodDefinitionExecution.ProducerState state,
        MethodRowGate gate,
        SourceGateGuard guard,
        TypeDefinitionHandle typeHandle,
        TypeDefinition typeDefinition) =>
        gate.TypeInScope(state.GateCache ??= gate.Resolve(guard.Classifier), typeHandle, typeDefinition);

    public bool? Accepts(
        ref MethodDefinitionUnit unit,
        MethodDefinitionExecution.ProducerState state,
        SourceGateGuard guard) =>
        MethodDefinitionExecution.GateAccepts(ref unit, state, guard);
}

/// <summary>
/// A gate whose classification is the struct <typeparamref name="TClassification"/>,
/// tested inline. A kernel pass has one producer, so nothing shares the
/// classifier's answers and no cache is kept; containment is the cached
/// gate's.
/// </summary>
internal readonly struct TypedGateKernel<TClassification> : IKernelGate
    where TClassification : struct, IMethodRowClassification
{
    public bool Gated => true;

    public bool TypeInScope(
        MethodDefinitionExecution.ProducerState state,
        MethodRowGate gate,
        SourceGateGuard guard,
        TypeDefinitionHandle typeHandle,
        TypeDefinition typeDefinition) =>
        default(TClassification).TypeInScope(new MethodRowTypeView(gate, typeDefinition, guard.Classifier));

    public bool? Accepts(
        ref MethodDefinitionUnit unit,
        MethodDefinitionExecution.ProducerState state,
        SourceGateGuard guard)
    {
        int unitClass;
        try
        {
            unitClass = default(TClassification).Classify(new MethodDefinitionView(ref unit, guard.Classifier));
        }
        catch (Exception ex)
            when (LibraryMethodAnalysisRunner.IsRecoverableMethodFailure(ex))
        {
            MethodDefinitionExecution.FailGate(ref unit, state, ex);
            return null;
        }

        return unitClass is >= 0 and < 64
            && ((guard.AcceptedClasses >> unitClass) & 1) != 0;
    }
}

/// <summary>
/// A closed query whose source gate classification is the struct
/// <typeparamref name="TGate"/>, so its kernel is one loop specialized to the
/// gate, predicate, and projection.
/// </summary>
public abstract class MethodDefinitionQueryProducer<TGate, TPredicate, TProjection, TRow>
    : MethodDefinitionQueryProducer<TPredicate, TProjection, TRow>
    where TGate : struct, IMethodRowClassification
    where TPredicate : struct, IMethodDefinitionPredicate
    where TProjection : struct, IMethodDefinitionProjection<TRow>
{
    private protected MethodDefinitionQueryProducer(
        string identity,
        int version,
        int tier,
        MethodDefinitionLayers layers,
        MethodDefinitionLayers rowLayers)
        : base(identity, version, tier, layers, rowLayers)
    {
    }

    /// <summary>The gate classifier; its classification must be <typeparamref name="TGate"/>.</summary>
    internal abstract MethodRowClassifier<TGate> GateClassifier { get; }

    private protected sealed override bool RunGatedKernel(
        MethodDefinitionExecution.ProducerState state,
        MetadataReader reader,
        PEReader peReader,
        LibraryMethodAnalysisRunner? lookup,
        MethodRowGate gate,
        SourceGateGuard sourceGate,
        out int unitsVisited) =>
        ReferenceEquals(sourceGate.Classifier, GateClassifier)
            ? RunKernelForStop<TypedGateKernel<TGate>>(
                state,
                reader,
                peReader,
                lookup,
                gate,
                sourceGate,
                out unitsVisited)
            : base.RunGatedKernel(state, reader, peReader, lookup, gate, sourceGate, out unitsVisited);
}
