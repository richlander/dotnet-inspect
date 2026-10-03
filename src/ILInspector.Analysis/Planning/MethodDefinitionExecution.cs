using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

namespace ILInspector.Analysis.Planning;

internal readonly record struct MethodDefinitionSharedExecution(
    ImmutableArray<MethodDefinitionExecution> Lanes,
    MethodDefinitionSourceCoverage PhysicalCoverage);

/// <summary>
/// The interim serial reference executor for method-definition units. It runs
/// a work description's passes over type definitions in metadata order and
/// each type's methods in order, stops a producer when its Exists terminal is
/// settled, contains a failing producer to itself and its dependents, and
/// records participation. Method Query Source delegates all-definition and
/// sparse producer work to it while source planning, binding, and receipts
/// remain owned above this executor.
/// </summary>
public sealed class MethodDefinitionExecution
{
    readonly WorkDescription _description;
    readonly ProducerState[] _states;
    readonly MethodDefinitionSourceBreadth _breadth;
    readonly MethodDefinitionSourceCoverageBuilder _sourceCoverage;
    MethodRowGate? _gate;
    LibraryMethodAnalysisRunner? _lookup;
    CriticalFailure? _critical;

    /// <summary>
    /// Units visited by the pass in progress, kept current as it runs, so an
    /// abort that unwinds the pass still records what was observed.
    /// </summary>
    internal int PassUnitsVisited;

    MethodDefinitionExecution(
        WorkDescription description,
        MethodDefinitionSourceBreadth breadth,
        bool tracksSourceCoverage)
    {
        _description = description;
        _breadth = breadth;
        _sourceCoverage =
            new MethodDefinitionSourceCoverageBuilder(tracksSourceCoverage);
        _states = new ProducerState[description.Producers.Length];
    }

    public WorkReceipt Receipt { get; private set; } = new(0, []);

    internal MethodDefinitionSourceCoverage SourceCoverage { get; private set; } =
        new(
            MethodDefinitionHandleCoverage.Empty,
            MethodDefinitionHandleCoverage.Empty,
            MethodDefinitionHandleCoverage.Empty);

    internal MethodDefinitionSourceCoverageBuilder SourceCoverageBuilder =>
        _sourceCoverage;

    internal MethodRowGate Gate =>
        _gate ?? throw new InvalidOperationException(
            "The Method-source gate has not been bound.");

    internal LibraryMethodAnalysisRunner? Lookup => _lookup;

    internal int CurrentOrdinal { get; private set; } = -1;

    /// <summary>
    /// Executes <paramref name="description"/> over the module in
    /// <paramref name="peReader"/>. Every planned producer must be a
    /// method-definition producer.
    /// </summary>
    public static MethodDefinitionExecution Execute(
        WorkDescription description,
        string sourceName,
        PEReader peReader) =>
        Execute(
            description,
            sourceName,
            peReader,
            MethodDefinitionSourceBreadth.AllDefinitions,
            tracksSourceCoverage: false);

    internal static MethodDefinitionExecution Execute(
        WorkDescription description,
        string sourceName,
        PEReader peReader,
        MethodDefinitionSourceBreadth breadth)
        => Execute(
            description,
            sourceName,
            peReader,
            breadth,
            tracksSourceCoverage: true);

    static MethodDefinitionExecution Execute(
        WorkDescription description,
        string sourceName,
        PEReader peReader,
        MethodDefinitionSourceBreadth breadth,
        bool tracksSourceCoverage)
    {
        ArgumentNullException.ThrowIfNull(description);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceName);
        ArgumentNullException.ThrowIfNull(peReader);
        ArgumentNullException.ThrowIfNull(breadth);

        MethodDefinitionExecution execution =
            CreateExecution(
                description,
                breadth,
                tracksSourceCoverage);
        if (!peReader.HasMetadata)
        {
            execution.CompleteWithoutUnits();
            return execution;
        }

        MetadataReader reader = peReader.GetMetadataReader();
        if (breadth.IsEmpty)
        {
            execution.CompleteWithoutUnits();
            return execution;
        }

        execution._gate = new MethodRowGate(
            peReader,
            reader,
            (FieldsRead(description) & MethodDefinitionLayers.IdentityText) != 0);

        bool lookupDeclared = false;
        foreach (ProducerState state in execution._states)
            lookupDeclared |= state.HasLookupLayer;
        using LibraryBodyAnalysisBuilder? builder = lookupDeclared
            ? new LibraryBodyAnalysisBuilder(sourceName, reader, peReader)
            : null;
        execution._lookup =
            builder is null ? null : new LibraryMethodAnalysisRunner(builder);
        MethodRowGate gate = execution.Gate;
        LibraryMethodAnalysisRunner? lookup = execution.Lookup;
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
                    || breadth.Kind
                        != MethodDefinitionSourceBreadthKind.AllDefinitions
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
            // A kernel aborts outside a unit visit. Interpreted visits contain
            // an abort at the request whose state raised it.
            execution.Abort(abort.Failure);
            execution.Receipt = execution.CreateReceipt(
                Math.Max(unitsVisited, execution.PassUnitsVisited),
                gate);
            execution.PublishSourceCoverage();
            return execution;
        }

        execution.PropagateFailures();
        execution.Receipt = execution.CreateReceipt(unitsVisited, gate);
        execution.PublishSourceCoverage();
        return execution;
    }

    static MethodDefinitionExecution CreateExecution(
        WorkDescription description,
        MethodDefinitionSourceBreadth breadth,
        bool tracksSourceCoverage)
    {
        var execution = new MethodDefinitionExecution(
            description,
            breadth,
            tracksSourceCoverage);
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
                description.RowLimitAtIndex(i),
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

        return execution;
    }

    internal static MethodDefinitionSharedExecution ExecuteShared(
        IReadOnlyList<WorkDescription> descriptions,
        string sourceName,
        PEReader peReader)
    {
        ArgumentNullException.ThrowIfNull(descriptions);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceName);
        ArgumentNullException.ThrowIfNull(peReader);
        if (descriptions.Count < 2)
        {
            throw new ArgumentException(
                "Shared Method-source execution requires at least two lanes.",
                nameof(descriptions));
        }

        var executions =
            ImmutableArray.CreateBuilder<MethodDefinitionExecution>(
                descriptions.Count);
        foreach (WorkDescription description in descriptions)
        {
            ArgumentNullException.ThrowIfNull(description);
            if (description.PassCount != 1)
            {
                throw new ProducerContractException(
                    "Shared Method-source execution currently accepts only "
                    + "single-pass producer descriptions.");
            }

            executions.Add(
                CreateExecution(
                    description,
                    MethodDefinitionSourceBreadth.AllDefinitions,
                    tracksSourceCoverage: true));
        }

        ImmutableArray<MethodDefinitionExecution> lanes =
            executions.MoveToImmutable();
        if (!peReader.HasMetadata)
        {
            foreach (MethodDefinitionExecution lane in lanes)
                lane.CompleteWithoutUnits();
            return new(lanes, EmptySourceCoverage());
        }

        MetadataReader reader = peReader.GetMetadataReader();
        var builders =
            new LibraryBodyAnalysisBuilder?[lanes.Length];
        try
        {
            var visiting = new ProducerState[lanes.Length][];
            for (int laneIndex = 0; laneIndex < lanes.Length; laneIndex++)
            {
                MethodDefinitionExecution lane = lanes[laneIndex];
                WorkDescription description = descriptions[laneIndex];
                lane._gate = new MethodRowGate(
                    peReader,
                    reader,
                    (FieldsRead(description)
                        & MethodDefinitionLayers.IdentityText) != 0);

                bool lookupDeclared = false;
                foreach (ProducerState state in lane._states)
                    lookupDeclared |= state.HasLookupLayer;
                if (lookupDeclared)
                {
                    builders[laneIndex] =
                        new LibraryBodyAnalysisBuilder(
                            sourceName,
                            reader,
                            peReader);
                    lane._lookup =
                        new LibraryMethodAnalysisRunner(
                            builders[laneIndex]!);
                }

                PlannedPass pass = description.Passes[0];
                visiting[laneIndex] =
                    new ProducerState[pass.Visits.Length];
                for (int i = 0; i < pass.Visits.Length; i++)
                {
                    ProducerState state = lane._states[pass.Visits[i]];
                    lane.FailIfPrerequisiteFailed(state);
                    visiting[laneIndex][i] = state;
                }
            }

            var physicalCoverage =
                new MethodDefinitionSourceCoverageBuilder(enabled: true);
            var unit = new MethodDefinitionUnit(
                reader,
                peReader,
                physicalCoverage);
            bool[] laneInScope = new bool[lanes.Length];

            foreach (TypeDefinitionHandle typeHandle
                in reader.TypeDefinitions)
            {
                TypeDefinition typeDefinition;
                try
                {
                    typeDefinition =
                        reader.GetTypeDefinition(typeHandle);
                }
                catch (Exception ex)
                    when (LibraryMethodAnalysisRunner
                        .IsRecoverableMethodFailure(ex))
                {
                    foreach (MethodDefinitionExecution lane in lanes)
                    {
                        lane.FailActiveSource(
                            MetadataTokens.GetToken(typeHandle),
                            "(type source)",
                            ex);
                    }
                    continue;
                }

                bool anyLaneInScope = false;
                for (int laneIndex = 0;
                    laneIndex < lanes.Length;
                    laneIndex++)
                {
                    MethodDefinitionExecution lane = lanes[laneIndex];
                    bool laneActive = lane.PrepareType(
                        reader,
                        lane.Gate,
                        visiting[laneIndex],
                        typeHandle,
                        typeDefinition,
                        out bool anyInScope);
                    laneInScope[laneIndex] =
                        laneActive && anyInScope;
                    anyLaneInScope |= laneInScope[laneIndex];
                }

                if (!anyLaneInScope)
                {
                    if (!AnyLaneActive(visiting))
                        break;
                    continue;
                }

                try
                {
                    foreach (MethodDefinitionHandle methodHandle
                        in typeDefinition.GetMethods())
                    {
                        unit.MoveTo(
                            typeHandle,
                            typeDefinition,
                            methodHandle);

                        for (int laneIndex = 0;
                            laneIndex < lanes.Length;
                            laneIndex++)
                        {
                            if (!laneInScope[laneIndex]
                                || !AnyActive(visiting[laneIndex]))
                            {
                                continue;
                            }

                            MethodDefinitionExecution lane = lanes[laneIndex];
                            lane.SelectRequest(
                                ref unit,
                                methodHandle);
                            lane.PassUnitsVisited =
                                lane.CurrentOrdinal + 1;
                            lane.VisitPreparedUnit(
                                visiting[laneIndex],
                                ref unit,
                                methodHandle);
                        }

                        if (!AnyLaneActiveInScope(
                                visiting,
                                laneInScope))
                            break;
                    }
                }
                catch (Exception ex)
                    when (LibraryMethodAnalysisRunner
                        .IsRecoverableMethodFailure(ex))
                {
                    for (int laneIndex = 0;
                        laneIndex < lanes.Length;
                        laneIndex++)
                    {
                        if (laneInScope[laneIndex])
                        {
                            lanes[laneIndex].FailActiveSource(
                                MetadataTokens.GetToken(typeHandle),
                                "(method source)",
                                ex);
                        }
                    }
                }

                if (!AnyLaneActive(visiting))
                    break;
            }

            foreach (MethodDefinitionExecution lane in lanes)
            {
                foreach (int completion
                    in lane._description.Passes[0].Completions)
                {
                    lane.CompleteProducer(
                        lane._states[completion]);
                }
                lane.PropagateFailures();
                lane.Receipt = lane.CreateReceipt(
                    lane.CurrentOrdinal + 1,
                    lane.Gate);
                lane.PublishSourceCoverage();
            }

            return new(lanes, physicalCoverage.Build());
        }
        finally
        {
            foreach (LibraryBodyAnalysisBuilder? builder in builders)
                builder?.Dispose();
        }
    }

    static MethodDefinitionSourceCoverage EmptySourceCoverage() =>
        new(
            MethodDefinitionHandleCoverage.Empty,
            MethodDefinitionHandleCoverage.Empty,
            MethodDefinitionHandleCoverage.Empty);

    static bool AnyLaneActive(ProducerState[][] lanes)
    {
        foreach (ProducerState[] lane in lanes)
        {
            if (AnyActive(lane))
                return true;
        }

        return false;
    }

    static bool AnyLaneActiveInScope(
        ProducerState[][] lanes,
        bool[] laneInScope)
    {
        for (int i = 0; i < lanes.Length; i++)
        {
            if (laneInScope[i] && AnyActive(lanes[i]))
                return true;
        }

        return false;
    }

    void FailActiveSource(
        int unit,
        string label,
        Exception error)
    {
        foreach (ProducerState state in _states)
        {
            if (!state.IsActive)
                continue;

            FailSource(state, unit, label, error);
        }
    }

    void FailActiveSourceForCurrentType(
        int unit,
        Exception error)
    {
        foreach (ProducerState state in _states)
        {
            if (!state.IsActive
                || !state.TypeInScopeNow)
            {
                continue;
            }

            FailSource(state, unit, "(method source)", error);
        }
    }

    internal static void FailSource(
        ProducerState state,
        int unit,
        string label,
        Exception error)
    {
        string message = ProducerFailure.Describe(error);
        state.Outcome = ProducerOutcome.Failed;
        state.Failure = new ProducerFailure(unit, label, message);
        state.SourceFailure =
            new MethodDefinitionSourceFailure(unit, label, message);
        state.IsActive = false;
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
            state.SourceFailure = null;
            state.FailedPrerequisite = null;
            state.ClearResult();
            state.IsActive = false;
        }
    }

    internal MethodDefinitionSourceFailure? SourceFailureOf(
        ProducerDeclaration producer)
    {
        ArgumentNullException.ThrowIfNull(producer);
        if (!_description.TryGetIndex(producer, out int index))
        {
            throw new ProducerContractException(
                $"Producer '{producer.Identity}' is not in this work "
                + "description.");
        }

        return _states[index].SourceFailure;
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
        MethodRowGate gate,
        ProducerState[] visiting)
    {
        var unit = new MethodDefinitionUnit(
            reader,
            peReader,
            _sourceCoverage);
        int visited = 0;
        switch (_breadth.Kind)
        {
            case MethodDefinitionSourceBreadthKind.AllDefinitions:
                foreach (TypeDefinitionHandle typeHandle
                    in reader.TypeDefinitions)
                {
                    TypeDefinition typeDefinition;
                    try
                    {
                        typeDefinition =
                            reader.GetTypeDefinition(typeHandle);
                    }
                    catch (Exception ex)
                        when (LibraryMethodAnalysisRunner
                            .IsRecoverableMethodFailure(ex))
                    {
                        FailActiveSource(
                            MetadataTokens.GetToken(typeHandle),
                            "(type source)",
                            ex);
                        continue;
                    }

                    try
                    {
                        if (!VisitType(
                                reader,
                                gate,
                                visiting,
                                ref unit,
                                typeHandle,
                                typeDefinition,
                                ref visited))
                        {
                            return visited;
                        }
                    }
                    catch (Exception ex)
                        when (LibraryMethodAnalysisRunner
                            .IsRecoverableMethodFailure(ex))
                    {
                        FailActiveSourceForCurrentType(
                            MetadataTokens.GetToken(typeHandle),
                            ex);
                    }
                }
                break;
            case MethodDefinitionSourceBreadthKind.ExactTypes:
                foreach (TypeDefinitionHandle typeHandle
                    in _breadth.Types)
                {
                    TypeDefinition typeDefinition;
                    try
                    {
                        typeDefinition =
                            reader.GetTypeDefinition(typeHandle);
                    }
                    catch (Exception ex)
                        when (LibraryMethodAnalysisRunner
                            .IsRecoverableMethodFailure(ex))
                    {
                        FailActiveSource(
                            MetadataTokens.GetToken(typeHandle),
                            "(type source)",
                            ex);
                        continue;
                    }

                    try
                    {
                        if (!VisitType(
                                reader,
                                gate,
                                visiting,
                                ref unit,
                                typeHandle,
                                typeDefinition,
                                ref visited))
                        {
                            return visited;
                        }
                    }
                    catch (Exception ex)
                        when (LibraryMethodAnalysisRunner
                            .IsRecoverableMethodFailure(ex))
                    {
                        FailActiveSourceForCurrentType(
                            MetadataTokens.GetToken(typeHandle),
                            ex);
                    }
                }
                break;
            case MethodDefinitionSourceBreadthKind.ExactMethods:
                VisitExactMethods(
                    reader,
                    gate,
                    visiting,
                    ref unit,
                    ref visited);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(_breadth));
        }

        return visited;
    }

    void VisitExactMethods(
        MetadataReader reader,
        MethodRowGate gate,
        ProducerState[] visiting,
        ref MethodDefinitionUnit unit,
        ref int visited)
    {
        TypeDefinitionHandle currentTypeHandle = default;
        TypeDefinition currentTypeDefinition = default;
        bool anyInScope = false;
        foreach (MethodDefinitionHandle methodHandle in _breadth.Methods)
        {
            if (!AnyActive(visiting))
                return;

            MethodDefinition methodDefinition;
            try
            {
                _sourceCoverage.RecordDefinitionExamined(methodHandle);
                methodDefinition =
                    reader.GetMethodDefinition(methodHandle);
            }
            catch (Exception ex)
                when (LibraryMethodAnalysisRunner
                    .IsRecoverableMethodFailure(ex))
            {
                FailActiveSource(
                    MetadataTokens.GetToken(methodHandle),
                    "(method source)",
                    ex);
                continue;
            }
            TypeDefinitionHandle typeHandle =
                methodDefinition.GetDeclaringType();
            if (typeHandle != currentTypeHandle)
            {
                currentTypeHandle = typeHandle;
                currentTypeDefinition =
                    reader.GetTypeDefinition(typeHandle);
                if (!PrepareType(
                        reader,
                        gate,
                        visiting,
                        typeHandle,
                        currentTypeDefinition,
                        out anyInScope))
                {
                    return;
                }
            }

            if (!anyInScope)
                continue;

            unit.MoveToPreviouslyRead(
                typeHandle,
                currentTypeDefinition,
                methodHandle,
                methodDefinition);
            SelectRequest(ref unit, methodHandle);
            visited++;
            PassUnitsVisited = visited;
            if (!VisitPreparedUnit(visiting, ref unit, methodHandle))
                return;
        }
    }

    bool VisitType(
        MetadataReader reader,
        MethodRowGate gate,
        ProducerState[] visiting,
        ref MethodDefinitionUnit unit,
        TypeDefinitionHandle typeHandle,
        TypeDefinition typeDefinition,
        ref int visited)
    {
        if (!PrepareType(
                reader,
                gate,
                visiting,
                typeHandle,
                typeDefinition,
                out bool anyInScope))
        {
            return false;
        }

        if (!anyInScope)
            return true;

        foreach (MethodDefinitionHandle methodHandle
            in typeDefinition.GetMethods())
        {
            unit.MoveTo(typeHandle, typeDefinition, methodHandle);
            SelectRequest(ref unit, methodHandle);
            visited++;
            PassUnitsVisited = visited;
            if (!VisitPreparedUnit(visiting, ref unit, methodHandle))
                return false;
        }

        return true;
    }

    void SelectRequest(
        ref MethodDefinitionUnit unit,
        MethodDefinitionHandle methodHandle)
    {
        _sourceCoverage.RecordDefinitionExamined(methodHandle);
        _sourceCoverage.RecordMethodSelected(methodHandle);
        CurrentOrdinal++;
        unit.SelectRequest(
            Gate,
            Lookup,
            _sourceCoverage,
            CurrentOrdinal);
    }

    bool PrepareType(
        MetadataReader reader,
        MethodRowGate gate,
        ProducerState[] visiting,
        TypeDefinitionHandle typeHandle,
        TypeDefinition typeDefinition,
        out bool anyInScope)
    {
        // Type scope: guards, then the producer's own predicate, in visit
        // order. Excluded types never cause their MethodDefs to be read.
        anyInScope = false;
        bool anyActiveProducer = false;
        foreach (ProducerState state in visiting)
        {
            if (state.HasDependencies)
                FailIfPrerequisiteFailed(state);
            if (!state.IsActive)
                continue;

            bool inScope = true;
            foreach (ProducerState guard in state.GuardStates)
                inScope &= guard.TypeInScopeNow;
            if (inScope && state.SourceGate is { } sourceGate)
            {
                inScope = GateTypeInScope(
                    state,
                    gate,
                    sourceGate,
                    typeHandle,
                    typeDefinition);
            }
            if (inScope)
            {
                inScope = TypeInScope(
                    state,
                    reader,
                    typeHandle,
                    typeDefinition);
            }

            state.TypeInScopeNow = inScope;
            anyInScope |= inScope && state.IsActive;
            anyActiveProducer |= state.IsActive;
        }

        return anyActiveProducer;
    }

    bool VisitPreparedUnit(
        ProducerState[] visiting,
        ref MethodDefinitionUnit unit,
        MethodDefinitionHandle methodHandle)
    {
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

            if (!state.TypeInScopeNow
                || (state.GuardStates.Length != 0
                    && !InScope(state, unitToken)))
            {
                anyActive = true;
                continue;
            }

            try
            {
                if (state.SourceGate is { } sourceGate)
                {
                    bool? accepted =
                        GateAccepts(ref unit, state, sourceGate);
                    if (accepted is null)
                        continue;
                    if (!accepted.Value)
                    {
                        anyActive = true;
                        continue;
                    }
                }

                VisitUnit(ref unit, state);
            }
            catch (ProducerAbortException abort)
            {
                state.Execution.Abort(abort.Failure);
                return false;
            }
            anyActive |= state.IsActive;
        }

        return anyActive;
    }

    static bool AnyActive(ProducerState[] visiting)
    {
        foreach (ProducerState state in visiting)
        {
            if (state.IsActive)
                return true;
        }

        return false;
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
        if (!settled)
            return;

        state.SettlingFacts++;
        if (state.Terminal == ProducerTerminal.Exists
            || state.RowLimit is int rowLimit
                && state.SettlingFacts >= rowLimit)
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
            or ProducerOutcome.PrerequisiteFailed
            or ProducerOutcome.Aborted)
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
                state.SourceFailure = target.SourceFailure;
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
                    or ProducerOutcome.PrerequisiteFailed
                    or ProducerOutcome.Aborted)
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
                        state.SourceFailure = target.SourceFailure;
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
        PublishSourceCoverage();
    }

    void PublishSourceCoverage() =>
        SourceCoverage = _sourceCoverage.Build();

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
        int? rowLimit,
        ImmutableArray<int> dependencies)
    {
        public MethodDefinitionExecution Execution => execution;

        public ProducerDeclaration Producer => producer;

        public ProducerTerminal Terminal => terminal;

        public int? RowLimit => rowLimit;

        public int SettlingFacts { get; set; }

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

        public MethodDefinitionSourceFailure? SourceFailure { get; set; }

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
        int? rowLimit,
        ImmutableArray<int> dependencies)
        : ProducerState(
            execution,
            producer,
            layers,
            terminal,
            rowLimit,
            dependencies)
    {
        public TResult? Result { get; private set; }

        protected void SetResult(TResult result) => Result = result;

        public override void ClearResult() => Result = default;
    }
}
