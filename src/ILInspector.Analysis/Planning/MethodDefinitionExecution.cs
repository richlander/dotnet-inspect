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
    CriticalFailure? _critical;

    /// <summary>
    /// Units visited by the pass in progress, kept current as it runs, so an
    /// abort that unwinds the pass still records what was observed.
    /// </summary>
    internal int PassUnitsVisited;

    /// <summary>
    /// Last source token reached by the pass in progress, so a source read
    /// failure remains attributable after its enumerator unwinds.
    /// </summary>
    internal int PassSourceToken;

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

        // The gate is part of the source. Its identity budget is armed only
        // when the plan declares identity text, so a plan of counts and
        // existence checks spends none.
        var gate = new MethodRowGate(
            peReader,
            reader,
            (FieldsRead(description) & MethodDefinitionLayers.IdentityText) != 0);

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

        try
        {
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
                execution.PassUnitsVisited = 0;

                // A pass that closes one open query with nothing else to
                // coordinate runs as that query's kernel. A kernel folds
                // without keeping facts, so a producer whose facts a reader
                // needs stays interpreted.
                int passUnits;
                if (visiting.Length != 1
                    || visiting[0].HasDependencies
                    || visiting[0].RetainsFacts
                    || !visiting[0].TryRunKernel(
                        reader,
                        peReader,
                        lookup,
                        gate,
                        out passUnits))
                {
                    passUnits = execution.VisitUnits(
                        reader,
                        peReader,
                        lookup,
                        gate,
                        visiting);
                }

                unitsVisited = Math.Max(unitsVisited, passUnits);
            }

            foreach (int completion in pass.Completions)
                execution.CompleteProducer(execution._states[completion]);
        }
        }
        catch (ProducerAbortException abort)
        {
            // A critical failure is never contained: the execution stops
            // where it was raised, and no producer's result is published.
            // Participation observed before the abort is kept.
            execution.Abort(abort.Failure);
            execution.Receipt = execution.CreateReceipt(
                Math.Max(unitsVisited, execution.PassUnitsVisited),
                gate);
            return execution;
        }

        execution.PropagateFailures();
        execution.Receipt = execution.CreateReceipt(unitsVisited, gate);
        return execution;
    }

    /// <summary>
    /// The fields and layers a work description reads: every planned
    /// producer's declaration and every gate classifier its guards name. It
    /// explains a plan without executing it.
    /// </summary>
    public static MethodDefinitionLayers FieldsRead(WorkDescription description)
    {
        ArgumentNullException.ThrowIfNull(description);
        MethodDefinitionLayers fields = 0;
        foreach (ProducerDeclaration producer in description.Producers)
        {
            if (producer is not IMethodDefinitionProducer declaration)
                continue;
            fields |= declaration.LayersFor(description.TerminalOf(producer));
            if (declaration.SourceGate is { } guard)
                fields |= guard.Classifier.Fields;
        }

        return fields;
    }

    void Abort(CriticalFailure failure)
    {
        _critical = failure;
        foreach (ProducerState state in _states)
        {
            state.Outcome = ProducerOutcome.Aborted;
            state.Failure = null;
            state.FailedPrerequisite = null;
            state.ClearResult();
            state.IsActive = false;
        }
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
            ProducerOutcome.Aborted =>
                new(state.Outcome.Value, default, Critical: _critical),
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

            accepted.Add(dependency.AcceptedUnitClasses!.Value);
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
        MethodRowGate gate,
        ProducerState[] visiting)
    {
        PassSourceToken = 0;
        try
        {
            return VisitUnitsCore(
                reader,
                peReader,
                lookup,
                gate,
                visiting);
        }
        catch (Exception ex)
            when (LibraryMethodAnalysisRunner.IsRecoverableMethodFailure(ex))
        {
            FailActiveSourceReaders(visiting, PassSourceToken, ex);
            return PassUnitsVisited;
        }
    }

    int VisitUnitsCore(
        MetadataReader reader,
        PEReader peReader,
        LibraryMethodAnalysisRunner? lookup,
        MethodRowGate gate,
        ProducerState[] visiting)
    {
        var unit = new MethodDefinitionUnit(reader, peReader, lookup, gate);
        int visited = 0;
        foreach (TypeDefinitionHandle typeHandle in reader.TypeDefinitions)
        {
            PassSourceToken = MetadataTokens.GetToken(typeHandle);
            TypeDefinition typeDefinition =
                reader.GetTypeDefinition(typeHandle);

            // Type scope: a producer's guards' type scopes, then its own
            // type predicate, in visit order so guards are decided first. A
            // type a guard excludes is not tested, so it cannot fail the
            // producer. When no active producer has the type in scope, its
            // methods are skipped as a whole; when no producer remains
            // active, the pass stops before the next untrusted read.
            bool anyInScope = false;
            bool anyActiveProducer = false;
            foreach (ProducerState state in visiting)
            {
                // A prerequisite that failed on an earlier producer's type
                // predicate fails this producer before its own is asked.
                if (state.HasDependencies)
                    FailIfPrerequisiteFailed(state);
                if (!state.IsActive)
                    continue;
                bool inScope = true;
                foreach (ProducerState guard in state.GuardStates)
                    inScope &= guard.TypeInScopeNow;
                if (inScope && state.SourceGate is { } sourceGate)
                    inScope = GateTypeInScope(state, gate, sourceGate, typeHandle, typeDefinition);
                if (inScope)
                    inScope = TypeInScope(state, reader, typeHandle, typeDefinition);
                state.TypeInScopeNow = inScope;
                anyInScope |= inScope && state.IsActive;
                anyActiveProducer |= state.IsActive;
            }

            if (!anyActiveProducer)
                return visited;
            if (!anyInScope)
                continue;

            foreach (MethodDefinitionHandle methodHandle
                     in typeDefinition.GetMethods())
            {
                PassSourceToken = MetadataTokens.GetToken(methodHandle);
                unit.MoveTo(typeHandle, typeDefinition, methodHandle);
                visited++;
                PassUnitsVisited = visited;
                int unitToken = MetadataTokens.GetToken(methodHandle);
                bool anyActive = false;
                bool anyActiveInType = false;
                foreach (ProducerState state in visiting)
                {
                    // Only a producer with dependencies can gain a failed
                    // prerequisite between units; the plan says which do.
                    if (state.HasDependencies)
                        FailIfPrerequisiteFailed(state);
                    if (!state.IsActive)
                        continue;

                    if (!state.TypeInScopeNow)
                    {
                        anyActive = true;
                        continue;
                    }

                    state.SourceOrdinal++;
                    if (state.GuardStates.Length != 0
                        && !InScope(state, unitToken))
                    {
                        anyActive = true;
                        anyActiveInType = true;
                        continue;
                    }

                    if (state.SourceGate is { } sourceGate)
                    {
                        bool? accepted = GateAccepts(ref unit, state, sourceGate);
                        if (accepted is null)
                            continue;
                        if (!accepted.Value)
                        {
                            anyActive = true;
                            anyActiveInType = true;
                            continue;
                        }
                    }

                    VisitUnit(ref unit, state);
                    anyActive |= state.IsActive;
                    anyActiveInType |= state.IsActive;
                }

                // Stop before advancing either enumerator: the next read is
                // untrusted metadata that could throw and replace an answer
                // or diagnostic that is already settled. A visit changes only
                // its own producer's state, so the loop above sees every
                // producer's final activity for this unit.
                if (!anyActive)
                    return visited;

                // Producers excluded from this whole type can still be active
                // for later types. Stop this type before advancing its method
                // enumerator once no in-scope producer remains.
                if (!anyActiveInType)
                    break;
            }
        }

        return visited;
    }

    static void FailActiveSourceReaders(
        ProducerState[] visiting,
        int token,
        Exception ex)
    {
        foreach (ProducerState state in visiting)
        {
            if (!state.IsActive)
                continue;

            state.Outcome = ProducerOutcome.Failed;
            state.Failure = new ProducerFailure(
                token,
                "(source enumeration)",
                ProducerFailure.Describe(ex));
            state.IsActive = false;
        }
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
                ProducerFailure.Describe(ex));
            state.IsActive = false;
            return false;
        }
    }

    /// <summary>
    /// Whether the gate classifier admits the type for a gate-guarded
    /// producer. A classifier that cannot read the type fails the producer
    /// there, as its own type predicate would.
    /// </summary>
    static bool GateTypeInScope(
        ProducerState state,
        MethodRowGate gate,
        SourceGateGuard guard,
        TypeDefinitionHandle typeHandle,
        TypeDefinition typeDefinition)
    {
        try
        {
            return gate.TypeInScope(state.GateCache ??= gate.Resolve(guard.Classifier), typeHandle, typeDefinition);
        }
        catch (Exception ex)
            when (LibraryMethodAnalysisRunner.IsRecoverableMethodFailure(ex))
        {
            state.Outcome = ProducerOutcome.Failed;
            state.Failure = new ProducerFailure(
                MetadataTokens.GetToken(typeHandle),
                "(type scope)",
                ProducerFailure.Describe(ex));
            state.IsActive = false;
            return false;
        }
    }

    /// <summary>
    /// Whether the gate classifies the unit into an accepted class. Null when
    /// the classifier could not read the unit, which fails the producer there.
    /// </summary>
    internal static bool? GateAccepts(
        ref MethodDefinitionUnit unit,
        ProducerState state,
        SourceGateGuard guard)
    {
        int unitClass;
        try
        {
            unitClass = unit.Gate.ClassOf(state.GateCache ??= unit.Gate.Resolve(guard.Classifier), ref unit);
        }
        catch (Exception ex)
            when (LibraryMethodAnalysisRunner.IsRecoverableMethodFailure(ex))
        {
            FailGate(ref unit, state, ex);
            return null;
        }

        return unitClass is >= 0 and < 64
            && ((guard.AcceptedClasses >> unitClass) & 1) != 0;
    }

    /// <summary>The gate could not read the unit: the producer fails there.</summary>
    internal static void FailGate(ref MethodDefinitionUnit unit, ProducerState state, Exception ex)
    {
        state.UnitsAttempted++;
        state.UnitsFailed++;
        state.Outcome = ProducerOutcome.Failed;
        state.Failure = new ProducerFailure(
            MetadataTokens.GetToken(unit.MethodHandle),
            unit.Label,
            ProducerFailure.Describe(ex));
        state.IsActive = false;
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
                ProducerFailure.Describe(ex));
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
                ProducerFailure.Describe(ex));
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
        Receipt = CreateReceipt(0, gate: null);
    }

    /// <summary>How many times the execution's gate looked up a classifier's cache.</summary>
    internal int GateCacheLookups { get; private set; }

    WorkReceipt CreateReceipt(int unitsVisited, MethodRowGate? gate)
    {
        GateCacheLookups = gate?.CacheLookups ?? 0;
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

        return new(unitsVisited, producers.MoveToImmutable())
        {
            Critical = _critical,
            IdentityBudgetArmed = gate?.IdentityBudgetArmed ?? false,
            IdentityWorkCharged = gate?.IdentityWorkCharged ?? 0,
            SignatureShapeNodesWalked = gate?.SignatureShapeNodesWalked ?? 0,
        };
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

        /// <summary>The producer's scope guard on the source gate, if any.</summary>
        public abstract SourceGateGuard? SourceGate { get; }

        /// <summary>The fields and layers the producer declared.</summary>
        public MethodDefinitionLayers Layers => layers;

        public abstract bool TypeInScope(MetadataReader reader, TypeDefinition type);

        /// <summary>Whether this producer classified <paramref name="unitToken"/> into one of <paramref name="acceptedClasses"/>.</summary>
        public abstract bool UnitClassIn(int unitToken, ulong acceptedClasses);

        /// <summary>Runs this producer's whole pass as a closed-query kernel, when it has one.</summary>
        public abstract bool TryRunKernel(
            MetadataReader reader,
            PEReader peReader,
            LibraryMethodAnalysisRunner? lookup,
            MethodRowGate gate,
            out int unitsVisited);

        public abstract bool Visit(scoped MethodDefinitionView view);

        public abstract void Complete(MethodDefinitionCompletionView completion);

        public abstract void ClearResult();

        /// <summary>The dependency targets, by index into the execution's states.</summary>
        public ImmutableArray<int> Dependencies => dependencies;

        public bool HasDependencies { get; } = !dependencies.IsEmpty;

        /// <summary>Whether the plan keeps this producer's per-unit facts for a reader.</summary>
        public virtual bool RetainsFacts => false;

        /// <summary>The dependencies' states, aligned with the declared dependencies.</summary>
        public ProducerState[] DependencyStates { get; set; } = [];

        /// <summary>The unit classes each scope guard in <see cref="GuardStates"/> accepts.</summary>
        public ulong[] GuardClasses { get; set; } = [];

        public ProducerState[] GuardStates { get; set; } = [];

        /// <summary>
        /// The source gate classifier's cache, resolved on this producer's
        /// first gate test and reused for every later unit of the execution.
        /// </summary>
        internal MethodRowGate.ClassifierCache? GateCache;

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

        /// <summary>
        /// The unit's position in this producer's independently scoped source
        /// traversal.
        /// </summary>
        public int SourceOrdinal { get; set; } = -1;

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
