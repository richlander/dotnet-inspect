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

        foreach (TypeDefinitionHandle typeHandle in reader.TypeDefinitions)
        {
            TypeDefinition typeDefinition = reader.GetTypeDefinition(typeHandle);
            if (sourceGate is not null)
            {
                bool gateInScope;
                try
                {
                    gateInScope = gate.TypeInScope(sourceGate.Classifier, typeHandle, typeDefinition);
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
        state.Failure = new ProducerFailure(token, unit, $"{ex.GetType().Name}: {ex.Message}");
        state.IsActive = false;
    }
}
