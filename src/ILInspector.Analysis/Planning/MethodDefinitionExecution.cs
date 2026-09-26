using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

namespace ILInspector.Analysis.Planning;

/// <summary>
/// The interim serial reference executor for method-definition units. It runs
/// a work description's passes over type definitions in metadata order and
/// each type's methods in order, stops a producer when its Exists terminal is
/// settled, contains a failing producer to itself and its dependents, and
/// records participation. It stands in for level 2 until #8577.
/// </summary>
public sealed class MethodDefinitionExecution
{
    readonly WorkDescription _description;
    readonly Dictionary<ProducerDeclaration, ProducerState> _states =
        new(ReferenceEqualityComparer.Instance);

    MethodDefinitionExecution(WorkDescription description)
    {
        _description = description;
    }

    public WorkReceipt Receipt { get; private set; } = new(0, []);

    /// <summary>
    /// Executes <paramref name="description"/> over the module in
    /// <paramref name="peReader"/>. Every planned producer must be a
    /// method-definition producer.
    /// </summary>
    public static MethodDefinitionExecution Execute(
        WorkDescription description,
        string sourceName,
        PEReader peReader)
    {
        ArgumentNullException.ThrowIfNull(description);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceName);
        ArgumentNullException.ThrowIfNull(peReader);

        var execution = new MethodDefinitionExecution(description);
        foreach (ProducerDeclaration producer in description.Producers)
        {
            if (producer is not IMethodDefinitionProducer declaration)
            {
                throw new ProducerContractException(
                    $"Producer '{producer.Identity}' is not a "
                    + "method-definition producer.");
            }

            execution._states[producer] = new ProducerState(
                execution,
                declaration,
                declaration.CreateRun(),
                description.TerminalOf(producer));
        }

        if (!peReader.HasMetadata)
        {
            execution.CompleteWithoutUnits();
            return execution;
        }

        MetadataReader reader = peReader.GetMetadataReader();
        using var builder = new LibraryBodyAnalysisBuilder(
            sourceName,
            reader,
            peReader);
        var lookup = new LibraryMethodAnalysisRunner(builder);
        var unit = new MethodDefinitionUnit(reader, peReader, lookup);
        int unitsVisited = 0;

        for (int pass = 1; pass <= description.PassCount; pass++)
        {
            List<ProducerState> visiting =
            [
                .. description.Producers
                    .Where(producer =>
                        description.VisitPassOf(producer) == pass)
                    .Select(producer => execution._states[producer]),
            ];
            foreach (ProducerState state in visiting)
                execution.FailIfPrerequisiteFailed(state);

            if (visiting.Any(static state => state.IsActive))
            {
                int passUnits = execution.VisitUnits(
                    reader,
                    unit,
                    visiting);
                unitsVisited = Math.Max(unitsVisited, passUnits);
            }

            foreach (ProducerDeclaration producer in description.CompletionOrder
                         .Where(producer =>
                             description.CompletionPassOf(producer) == pass))
            {
                execution.CompleteProducer(execution._states[producer]);
            }
        }

        execution.PropagateFailures();
        execution.Receipt = execution.CreateReceipt(unitsVisited);
        return execution;
    }

    /// <summary>
    /// The typed result of a planned producer. Reading a producer that is not
    /// in the work description is a contract violation.
    /// </summary>
    public ProducerResult<TResult> ResultOf<TResult>(
        ProducerDeclaration<TResult> producer)
    {
        ArgumentNullException.ThrowIfNull(producer);
        if (!_states.TryGetValue(producer, out ProducerState? state))
        {
            throw new ProducerContractException(
                $"Producer '{producer.Identity}' is not in this work "
                + "description.");
        }

        return state.Outcome switch
        {
            ProducerOutcome.Complete or ProducerOutcome.Stopped =>
                new(state.Outcome.Value, (TResult)state.Result!),
            ProducerOutcome.Failed =>
                new(state.Outcome.Value, default, state.Failure),
            ProducerOutcome.PrerequisiteFailed =>
                new(
                    state.Outcome.Value,
                    default,
                    FailedPrerequisite: state.FailedPrerequisite),
            _ => throw new ProducerContractException(
                $"Producer '{producer.Identity}' has not completed."),
        };
    }

    internal object? FactFor(ProducerDeclaration producer, int unitToken) =>
        _states[producer].Run.TryGetFact(unitToken, out object? fact)
            ? fact
            : throw new ProducerContractException(
                $"Producer '{producer.Identity}' has no fact for unit "
                + $"0x{unitToken:X8}.");

    int VisitUnits(
        MetadataReader reader,
        MethodDefinitionUnit unit,
        List<ProducerState> visiting)
    {
        int visited = 0;
        foreach (TypeDefinitionHandle typeHandle in reader.TypeDefinitions)
        {
            TypeDefinition typeDefinition =
                reader.GetTypeDefinition(typeHandle);
            foreach (MethodDefinitionHandle methodHandle
                     in typeDefinition.GetMethods())
            {
                if (!visiting.Any(static state => state.IsActive))
                    return visited;

                unit.MoveTo(typeHandle, typeDefinition, methodHandle);
                visited++;
                foreach (ProducerState state in visiting)
                {
                    FailIfPrerequisiteFailed(state);
                    if (!state.IsActive)
                        continue;
                    VisitUnit(unit, state);
                }
            }
        }

        return visited;
    }

    void VisitUnit(MethodDefinitionUnit unit, ProducerState state)
    {
        state.UnitsAttempted++;
        bool settled;
        try
        {
            settled = state.Run.Visit(new MethodDefinitionView(unit, state));
        }
        catch (Exception ex)
            when (LibraryMethodAnalysisRunner.IsRecoverableMethodFailure(ex))
        {
            state.UnitsFailed++;
            state.Outcome = ProducerOutcome.Failed;
            state.Failure = new ProducerFailure(
                MetadataTokens.GetToken(unit.MethodHandle),
                unit.Label,
                $"{ex.GetType().Name}: {ex.Message}");
            state.IsActive = false;
            return;
        }

        state.UnitsCompleted++;
        if (settled && state.Terminal == ProducerTerminal.Exists)
        {
            state.Outcome = ProducerOutcome.Stopped;
            state.IsActive = false;
        }
    }

    void FailIfPrerequisiteFailed(ProducerState state)
    {
        // A producer whose visits stopped at a settled terminal still needs
        // its completion prerequisites, so only a final outcome is skipped.
        if (state.Outcome is ProducerOutcome.Complete
            or ProducerOutcome.Failed
            or ProducerOutcome.PrerequisiteFailed)
        {
            return;
        }
        foreach (ProducerDependency dependency in state.Run.Producer.Dependencies)
        {
            ProducerState target = _states[dependency.Producer];
            if (target.Outcome is ProducerOutcome.Failed
                or ProducerOutcome.PrerequisiteFailed)
            {
                state.Outcome = ProducerOutcome.PrerequisiteFailed;
                state.FailedPrerequisite = target.Run.Producer.Identity;
                state.IsActive = false;
                return;
            }
        }
    }

    /// <summary>
    /// A prerequisite can fail after a dependent has already completed, when
    /// the stage order lets the dependent's completion run first. Before any
    /// result or receipt is published, every producer with a failed
    /// prerequisite of any dependency kind becomes prerequisite-failed,
    /// transitively, so an outcome never depends on which other producers were
    /// requested.
    /// </summary>
    void PropagateFailures()
    {
        bool changed;
        do
        {
            changed = false;
            foreach (ProducerDeclaration producer in _description.Producers)
            {
                ProducerState state = _states[producer];
                if (state.Outcome is ProducerOutcome.Failed
                    or ProducerOutcome.PrerequisiteFailed)
                {
                    continue;
                }

                foreach (ProducerDependency dependency in producer.Dependencies)
                {
                    ProducerState target = _states[dependency.Producer];
                    if (target.Outcome is ProducerOutcome.Failed
                        or ProducerOutcome.PrerequisiteFailed)
                    {
                        state.Outcome = ProducerOutcome.PrerequisiteFailed;
                        state.FailedPrerequisite = target.Run.Producer.Identity;
                        state.Result = null;
                        state.IsActive = false;
                        changed = true;
                        break;
                    }
                }
            }
        }
        while (changed);
    }

    void CompleteProducer(ProducerState state)
    {
        FailIfPrerequisiteFailed(state);
        if (state.Outcome is ProducerOutcome.Failed
            or ProducerOutcome.PrerequisiteFailed)
        {
            return;
        }

        try
        {
            state.Result = state.Run.Complete(
                new MethodDefinitionCompletionView(state));
        }
        catch (Exception ex)
            when (LibraryMethodAnalysisRunner.IsRecoverableMethodFailure(ex))
        {
            state.Outcome = ProducerOutcome.Failed;
            state.Failure = new ProducerFailure(
                0,
                "(completion)",
                $"{ex.GetType().Name}: {ex.Message}");
            state.IsActive = false;
            return;
        }

        state.Outcome ??= ProducerOutcome.Complete;
        state.IsActive = false;
    }

    void CompleteWithoutUnits()
    {
        foreach (ProducerDeclaration producer in _description.CompletionOrder)
            CompleteProducer(_states[producer]);
        PropagateFailures();
        Receipt = CreateReceipt(0);
    }

    WorkReceipt CreateReceipt(int unitsVisited) =>
        new(
            unitsVisited,
            [
                .. _description.Producers.Select(producer =>
                {
                    ProducerState state = _states[producer];
                    return new ProducerParticipation(
                        producer.Identity,
                        state.Outcome
                        ?? throw new InvalidOperationException(
                            $"Producer '{producer.Identity}' has no outcome."),
                        state.UnitsAttempted,
                        state.UnitsCompleted,
                        state.UnitsFailed,
                        state.Declaration.Layers.HasFlag(
                            MethodDefinitionLayers.Body)
                            ? [new ProducerLayerParticipation(
                                nameof(MethodDefinitionLayers.Body),
                                state.BodyAcquisitions)]
                            : []);
                }),
            ]);

    internal sealed class ProducerState(
        MethodDefinitionExecution execution,
        IMethodDefinitionProducer declaration,
        IMethodDefinitionProducerRun run,
        ProducerTerminal terminal)
    {
        public MethodDefinitionExecution Execution => execution;

        public IMethodDefinitionProducer Declaration => declaration;

        public IMethodDefinitionProducerRun Run => run;

        public ProducerTerminal Terminal => terminal;

        public bool IsActive { get; set; } = true;

        public ProducerOutcome? Outcome { get; set; }

        public ProducerFailure? Failure { get; set; }

        public string? FailedPrerequisite { get; set; }

        public object? Result { get; set; }

        public int UnitsAttempted { get; set; }

        public int UnitsCompleted { get; set; }

        public int UnitsFailed { get; set; }

        public int BodyAcquisitions { get; set; }

        public void Require(
            ProducerDeclaration dependency,
            params ProducerDependencyKind[] kinds)
        {
            ArgumentNullException.ThrowIfNull(dependency);
            if (!run.Producer.Dependencies.Any(declared =>
                    ReferenceEquals(declared.Producer, dependency)
                    && kinds.Contains(declared.Kind)))
            {
                throw new ProducerContractException(
                    $"Producer '{run.Producer.Identity}' did not declare "
                    + $"a dependency on '{dependency.Identity}'.");
            }
        }
    }
}
