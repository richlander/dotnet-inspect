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
    readonly ProducerState[] _states;

    MethodDefinitionExecution(WorkDescription description)
    {
        _description = description;
        _states = new ProducerState[description.Producers.Length];
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
        ImmutableArray<ProducerDeclaration> producers = description.Producers;
        for (int i = 0; i < producers.Length; i++)
        {
            if (producers[i] is not IMethodDefinitionProducer declaration)
            {
                throw new ProducerContractException(
                    $"Producer '{producers[i].Identity}' is not a "
                    + "method-definition producer.");
            }

            execution._states[i] = declaration.CreateState(
                execution,
                description.TerminalByIndex[i],
                description.DependencyIndices[i],
                description.FactRetention[i]);
        }

        // Each dependent reads its dependencies' states directly, aligned
        // with its declared dependencies, so a same-unit read needs no lookup.
        foreach (ProducerState state in execution._states)
        {
            var dependencies = new ProducerState[state.Dependencies.Length];
            for (int j = 0; j < dependencies.Length; j++)
                dependencies[j] = execution._states[state.Dependencies[j]];
            state.DependencyStates = dependencies;
            execution.BindScopeGuards(state);
        }

        if (!peReader.HasMetadata)
        {
            execution.CompleteWithoutUnits();
            return execution;
        }

        MetadataReader reader = peReader.GetMetadataReader();

        // The module lookup is execution-scoped and costly to build, so it
        // exists only when a planned producer declared it.
        bool lookupDeclared = false;
        foreach (ProducerState state in execution._states)
            lookupDeclared |= state.HasLookupLayer;
        using LibraryBodyAnalysisBuilder? builder = lookupDeclared
            ? new LibraryBodyAnalysisBuilder(sourceName, reader, peReader)
            : null;
        LibraryMethodAnalysisRunner? lookup =
            builder is null ? null : new LibraryMethodAnalysisRunner(builder);
        int unitsVisited = 0;

        foreach (PlannedPass pass in description.Passes)
        {
            ProducerState[] visiting = new ProducerState[pass.Visits.Length];
            bool anyActive = false;
            for (int i = 0; i < visiting.Length; i++)
            {
                ProducerState state = execution._states[pass.Visits[i]];
                execution.FailIfPrerequisiteFailed(state);
                anyActive |= state.IsActive;
                visiting[i] = state;
            }

            if (anyActive)
            {
                // A pass that closes one open query with nothing else to
                // coordinate runs as that query's kernel.
                int passUnits;
                if (visiting.Length != 1
                    || visiting[0].HasDependencies
                    || !visiting[0].TryRunKernel(
                        reader,
                        peReader,
                        lookup,
                        out passUnits))
                {
                    passUnits = execution.VisitUnits(
                        reader,
                        peReader,
                        lookup,
                        visiting);
                }

                unitsVisited = Math.Max(unitsVisited, passUnits);
            }

            foreach (int completion in pass.Completions)
                execution.CompleteProducer(execution._states[completion]);
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
        if (!_description.TryGetIndex(producer, out int index))
        {
            throw new ProducerContractException(
                $"Producer '{producer.Identity}' is not in this work "
                + "description.");
        }

        var state = (ProducerState<TResult>)_states[index];
        return state.Outcome switch
        {
            ProducerOutcome.Complete or ProducerOutcome.Stopped =>
                new(state.Outcome.Value, state.Result),
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

    /// <summary>
    /// Binds a producer's scope guards: same-pass visit dependencies on a
    /// producer that classifies units.
    /// </summary>
    void BindScopeGuards(ProducerState state)
    {
        ImmutableArray<ProducerDependency> declared = state.Producer.Dependencies;
        var accepted = new List<ulong>();
        var guardStates = new List<ProducerState>();
        for (int j = 0; j < declared.Length; j++)
        {
            ProducerDependency dependency = declared[j];
            if (!dependency.IsScopeGuard)
                continue;
            ProducerState guard = state.DependencyStates[j];
            if (dependency.Kind != ProducerDependencyKind.VisitNeedsVisit
                || !guard.ClassifiesUnits
                || _description.VisitPassOf(dependency.Producer)
                    != _description.VisitPassOf(state.Producer))
            {
                throw new ProducerContractException(
                    $"Producer '{state.Producer.Identity}' declares a scope guard on "
                    + $"'{dependency.Producer.Identity}', which must be a same-pass visit "
                    + "dependency on a producer that classifies units.");
            }

            accepted.Add(dependency.AcceptedUnitClasses);
            guardStates.Add(guard);
        }

        state.GuardClasses = [.. accepted];
        state.GuardStates = [.. guardStates];
    }

    /// <summary>
    /// The same-unit fact of a declared visit dependency, found through the
    /// dependent's own dependency states.
    /// </summary>
    internal static TFact FactFor<TFact, TAccumulator, TResult>(
        ProducerState reader,
        MethodDefinitionProducer<TFact, TAccumulator, TResult> producer,
        int unitToken)
    {
        ImmutableArray<ProducerDependency> declared = reader.Producer.Dependencies;
        for (int i = 0; i < declared.Length; i++)
        {
            if (!ReferenceEquals(declared[i].Producer, producer)
                || declared[i].Kind != ProducerDependencyKind.VisitNeedsVisit)
            {
                continue;
            }

            if (reader.DependencyStates[i] is MethodDefinitionProducer<TFact, TAccumulator, TResult>.State dependency
                && dependency.TryGetFact(unitToken, out TFact fact))
            {
                return fact;
            }

            throw new ProducerContractException(
                $"Producer '{producer.Identity}' has no fact for unit "
                + $"0x{unitToken:X8}.");
        }

        throw new ProducerContractException(
            $"Producer '{reader.Producer.Identity}' did not declare "
            + $"a dependency on '{producer.Identity}'.");
    }

    int VisitUnits(
        MetadataReader reader,
        PEReader peReader,
        LibraryMethodAnalysisRunner? lookup,
        ProducerState[] visiting)
    {
        var unit = new MethodDefinitionUnit(reader, peReader, lookup);
        int visited = 0;
        foreach (TypeDefinitionHandle typeHandle in reader.TypeDefinitions)
        {
            TypeDefinition typeDefinition =
                reader.GetTypeDefinition(typeHandle);

            // Type scope: a producer's own type predicate and its guards'
            // type scopes, in visit order so guards are decided first. When
            // no active producer has the type in scope, its methods are
            // skipped as a whole.
            bool anyInScope = false;
            foreach (ProducerState state in visiting)
            {
                if (!state.IsActive)
                    continue;
                bool inScope = TypeInScope(state, reader, typeHandle, typeDefinition);
                foreach (ProducerState guard in state.GuardStates)
                    inScope &= guard.TypeInScopeNow;
                state.TypeInScopeNow = inScope;
                anyInScope |= inScope && state.IsActive;
            }

            if (!anyInScope)
                continue;

            foreach (MethodDefinitionHandle methodHandle
                     in typeDefinition.GetMethods())
            {
                unit.MoveTo(typeHandle, typeDefinition, methodHandle);
                visited++;
                int unitToken = MetadataTokens.GetToken(methodHandle);
                bool anyActive = false;
                foreach (ProducerState state in visiting)
                {
                    // Only a producer with dependencies can gain a failed
                    // prerequisite between units; the plan says which do.
                    if (state.HasDependencies)
                        FailIfPrerequisiteFailed(state);
                    if (!state.IsActive)
                        continue;

                    // A unit outside the producer's type scope or a declared
                    // scope guard is not visited.
                    if (!state.TypeInScopeNow
                        || (state.GuardStates.Length != 0 && !InScope(state, unitToken)))
                    {
                        anyActive = true;
                        continue;
                    }

                    VisitUnit(ref unit, state);
                    anyActive |= state.IsActive;
                }

                // Stop before advancing either enumerator: the next read is
                // untrusted metadata that could throw and replace an answer
                // or diagnostic that is already settled. A visit changes only
                // its own producer's state, so the loop above sees every
                // producer's final activity for this unit.
                if (!anyActive)
                    return visited;
            }
        }

        return visited;
    }

    static bool TypeInScope(
        ProducerState state,
        MetadataReader reader,
        TypeDefinitionHandle typeHandle,
        TypeDefinition typeDefinition)
    {
        if (!state.HasTypeScope)
            return true;
        try
        {
            return state.TypeInScope(reader, typeDefinition);
        }
        catch (Exception ex)
            when (LibraryMethodAnalysisRunner.IsRecoverableMethodFailure(ex))
        {
            state.Outcome = ProducerOutcome.Failed;
            state.Failure = new ProducerFailure(
                MetadataTokens.GetToken(typeHandle),
                "(type scope)",
                $"{ex.GetType().Name}: {ex.Message}");
            state.IsActive = false;
            return false;
        }
    }

    static bool InScope(ProducerState state, int unitToken)
    {
        ProducerState[] guards = state.GuardStates;
        ulong[] accepted = state.GuardClasses;
        for (int i = 0; i < guards.Length; i++)
        {
            if (!guards[i].UnitClassIn(unitToken, accepted[i]))
                return false;
        }

        return true;
    }

    void VisitUnit(ref MethodDefinitionUnit unit, ProducerState state)
    {
        state.UnitsAttempted++;
        bool settled;
        try
        {
            settled = state.Visit(new MethodDefinitionView(ref unit, state));
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
        foreach (int dependency in state.Dependencies)
        {
            ProducerState target = _states[dependency];
            if (target.Outcome is ProducerOutcome.Failed
                or ProducerOutcome.PrerequisiteFailed)
            {
                state.Outcome = ProducerOutcome.PrerequisiteFailed;
                state.FailedPrerequisite = target.Producer.Identity;
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
            foreach (ProducerState state in _states)
            {
                if (state.Outcome is ProducerOutcome.Failed
                    or ProducerOutcome.PrerequisiteFailed)
                {
                    continue;
                }

                foreach (int dependency in state.Dependencies)
                {
                    ProducerState target = _states[dependency];
                    if (target.Outcome is ProducerOutcome.Failed
                        or ProducerOutcome.PrerequisiteFailed)
                    {
                        state.Outcome = ProducerOutcome.PrerequisiteFailed;
                        state.FailedPrerequisite = target.Producer.Identity;
                        state.ClearResult();
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
            state.Complete(new MethodDefinitionCompletionView(state));
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
        foreach (int completion in _description.CompletionIndices)
            CompleteProducer(_states[completion]);
        PropagateFailures();
        Receipt = CreateReceipt(0);
    }

    WorkReceipt CreateReceipt(int unitsVisited)
    {
        var producers = ImmutableArray.CreateBuilder<ProducerParticipation>(
            _states.Length);
        foreach (ProducerState state in _states)
        {
            var layers = ImmutableArray.CreateBuilder<ProducerLayerParticipation>(2);
            if (state.HasBodyLayer)
            {
                layers.Add(new ProducerLayerParticipation(
                    nameof(MethodDefinitionLayers.Body),
                    state.BodyAcquisitions));
            }

            if (state.HasLookupLayer)
            {
                layers.Add(new ProducerLayerParticipation(
                    nameof(MethodDefinitionLayers.ModuleLookup),
                    state.LookupUses));
            }

            producers.Add(new ProducerParticipation(
                state.Producer.Identity,
                state.Outcome
                ?? throw new InvalidOperationException(
                    $"Producer '{state.Producer.Identity}' has no outcome."),
                state.UnitsAttempted,
                state.UnitsCompleted,
                state.UnitsFailed,
                layers.ToImmutable()));
        }

        return new(unitsVisited, producers.MoveToImmutable());
    }

    /// <summary>
    /// A producer's per-execution bookkeeping. The executor holds every state
    /// in one array; the per-unit operations are virtual members that the
    /// typed state implements, so no unit crosses an interface dispatch.
    /// </summary>
    internal abstract class ProducerState(
        MethodDefinitionExecution execution,
        ProducerDeclaration producer,
        MethodDefinitionLayers layers,
        ProducerTerminal terminal,
        ImmutableArray<int> dependencies)
    {
        public MethodDefinitionExecution Execution => execution;

        public ProducerDeclaration Producer => producer;

        public ProducerTerminal Terminal => terminal;

        public abstract bool ClassifiesUnits { get; }

        public abstract bool HasTypeScope { get; }

        public abstract bool TypeInScope(MetadataReader reader, TypeDefinition type);

        /// <summary>Whether this producer classified <paramref name="unitToken"/> into one of <paramref name="acceptedClasses"/>.</summary>
        public abstract bool UnitClassIn(int unitToken, ulong acceptedClasses);

        /// <summary>Runs this producer's whole pass as a closed-query kernel, when it has one.</summary>
        public abstract bool TryRunKernel(
            MetadataReader reader,
            PEReader peReader,
            LibraryMethodAnalysisRunner? lookup,
            out int unitsVisited);

        public abstract bool Visit(scoped MethodDefinitionView view);

        public abstract void Complete(MethodDefinitionCompletionView completion);

        public abstract void ClearResult();

        /// <summary>The dependency targets, by index into the execution's states.</summary>
        public ImmutableArray<int> Dependencies => dependencies;

        public bool HasDependencies { get; } = !dependencies.IsEmpty;

        /// <summary>The dependencies' states, aligned with the declared dependencies.</summary>
        public ProducerState[] DependencyStates { get; set; } = [];

        /// <summary>The unit classes each scope guard in <see cref="GuardStates"/> accepts.</summary>
        public ulong[] GuardClasses { get; set; } = [];

        public ProducerState[] GuardStates { get; set; } = [];

        /// <summary>Whether the type being visited is in this producer's scope.</summary>
        public bool TypeInScopeNow = true;

        public bool HasBodyLayer { get; } =
            (layers & MethodDefinitionLayers.Body) != 0;

        public bool HasLookupLayer { get; } =
            (layers & MethodDefinitionLayers.ModuleLookup) != 0;

        public bool IsActive { get; set; } = true;

        public ProducerOutcome? Outcome { get; set; }

        public ProducerFailure? Failure { get; set; }

        public string? FailedPrerequisite { get; set; }

        public int UnitsAttempted { get; set; }

        public int UnitsCompleted { get; set; }

        public int UnitsFailed { get; set; }

        /// <summary>Units in which the body layer was acquired.</summary>
        public int BodyAcquisitions;

        /// <summary>Units in which the module lookup was used.</summary>
        public int LookupUses;

        public int LastBodyUnit;

        public int LastLookupUnit;

        /// <summary>Counts <paramref name="unitToken"/> once, however often a layer is read in it.</summary>
        public void CountUnit(ref int lastUnit, int unitToken, ref int count)
        {
            if (lastUnit == unitToken)
                return;
            lastUnit = unitToken;
            count++;
        }

        /// <summary>
        /// Requires a declared dependency on <paramref name="dependency"/> of
        /// kind <paramref name="kind"/> or, when given, <paramref name="alternative"/>.
        /// </summary>
        public void Require(
            ProducerDeclaration dependency,
            ProducerDependencyKind kind,
            ProducerDependencyKind? alternative = null)
        {
            ArgumentNullException.ThrowIfNull(dependency);
            foreach (ProducerDependency declared in producer.Dependencies)
            {
                if (ReferenceEquals(declared.Producer, dependency)
                    && (declared.Kind == kind || declared.Kind == alternative))
                {
                    return;
                }
            }

            throw new ProducerContractException(
                $"Producer '{producer.Identity}' did not declare "
                + $"a dependency on '{dependency.Identity}'.");
        }
    }

    /// <summary>A producer state that holds its typed result, so publishing it boxes nothing.</summary>
    internal abstract class ProducerState<TResult>(
        MethodDefinitionExecution execution,
        ProducerDeclaration producer,
        MethodDefinitionLayers layers,
        ProducerTerminal terminal,
        ImmutableArray<int> dependencies)
        : ProducerState(execution, producer, layers, terminal, dependencies)
    {
        public TResult? Result { get; private set; }

        protected void SetResult(TResult result) => Result = result;

        public override void ClearResult() => Result = default;
    }
}
