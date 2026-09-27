using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

namespace ILInspector.Analysis.Planning;

/// <summary>
/// An open query over method-definition units that selects units and projects
/// each selected one to a row. A struct, so a kernel specialized to it calls
/// it directly.
/// </summary>
public interface IMethodDefinitionProjection<TRow>
{
    bool TryProject(scoped MethodDefinitionView view, out TRow row);
}

/// <summary>A unit's fact under a projection: whether it was selected, and its row.</summary>
public readonly record struct ProjectedRow<TRow>(bool Selected, TRow Row);

/// <summary>
/// A producer for a projecting open query. The Rows terminal (All) publishes
/// the selected units' rows in unit order; Exists stops once its threshold of
/// units is selected. A pass that visits only this producer runs as a
/// closed-query kernel specialized to the projection and the terminal.
/// </summary>
public abstract class MethodDefinitionRowsProducer<TProjection, TRow>
    : MethodDefinitionProducer<ProjectedRow<TRow>, ImmutableArray<TRow>.Builder, ImmutableArray<TRow>>
    where TProjection : struct, IMethodDefinitionProjection<TRow>
{
    private protected MethodDefinitionRowsProducer(
        string identity,
        int version,
        int tier,
        MethodDefinitionLayers layers)
        : base(identity, version, tier, layers)
    {
    }

    /// <summary>Experiment toggle: whether a closed query over this projection may run as a kernel.</summary>
    internal virtual bool AllowsKernel => true;

    internal sealed override ProjectedRow<TRow> Visit(scoped MethodDefinitionView view) =>
        default(TProjection).TryProject(view, out TRow row)
            ? new(true, row)
            : default;

    internal sealed override ImmutableArray<TRow>.Builder Seed() =>
        ImmutableArray.CreateBuilder<TRow>();

    internal sealed override ImmutableArray<TRow>.Builder Accumulate(
        ImmutableArray<TRow>.Builder accumulator,
        ProjectedRow<TRow> fact)
    {
        if (fact.Selected)
            accumulator.Add(fact.Row);
        return accumulator;
    }

    internal sealed override ImmutableArray<TRow> Complete(
        ImmutableArray<TRow>.Builder accumulator,
        MethodDefinitionCompletionView completion) =>
        accumulator.DrainToImmutable();

    internal sealed override bool Settles(ProjectedRow<TRow> fact) => fact.Selected;

    internal override bool RunKernel(
        MethodDefinitionExecution.ProducerState state,
        MetadataReader reader,
        PEReader peReader,
        LibraryMethodAnalysisRunner? lookup,
        out int unitsVisited)
    {
        unitsVisited = 0;
        if (!AllowsKernel)
            return false;

        bool exists = state.Terminal == ProducerTerminal.Exists;
        bool typeScoped = HasTypeScope;
        TProjection projection = default;
        ImmutableArray<TRow>.Builder rows = ImmutableArray.CreateBuilder<TRow>();
        var unit = new MethodDefinitionUnit(reader, peReader, lookup);
        int visited = 0;
        int attempted = 0;
        int completed = 0;

        foreach (TypeDefinitionHandle typeHandle in reader.TypeDefinitions)
        {
            TypeDefinition typeDefinition = reader.GetTypeDefinition(typeHandle);
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
                attempted++;
                bool selected;
                TRow row;
                try
                {
                    selected = projection.TryProject(new MethodDefinitionView(ref unit, state), out row);
                }
                catch (Exception ex)
                    when (LibraryMethodAnalysisRunner.IsRecoverableMethodFailure(ex))
                {
                    state.UnitsFailed++;
                    Fail(state, MetadataTokens.GetToken(methodHandle), unit.Label, ex);
                    goto Done;
                }

                completed++;
                if (!selected)
                    continue;
                rows.Add(row);

                // Stop before advancing either enumerator once the Exists
                // threshold is settled, as the reference executor does.
                if (exists && rows.Count >= state.Threshold)
                {
                    state.Outcome = ProducerOutcome.Stopped;
                    state.IsActive = false;
                    goto Done;
                }
            }
        }

    Done:
        if (exists)
            state.SettledUnits = rows.Count;
        state.UnitsAttempted += attempted;
        state.UnitsCompleted += completed;
        ((State)state).SetAccumulator(rows);
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
        state.Failure = new ProducerFailure(token, unit, $"{ex.GetType().Name}: {ex.Message}");
        state.IsActive = false;
    }
}
