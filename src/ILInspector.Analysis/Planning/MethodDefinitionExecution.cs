using ILInspector.Metadata;
using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

using ILInspector.Instructions;

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
    readonly MethodBodyAnalyzerPlan? _instructionPlan;
    readonly ProducerState[] _states;
    MethodDefinitionSourceBreadth _breadth;
    readonly MethodDefinitionSourceCoverageBuilder _sourceCoverage;
    MethodRowGate? _gate;
    LibraryMethodAnalysisRunner? _lookup;
    MemorySafetyContractSupport? _memorySafety;
    SameModuleCallTargetSupport? _callTargets;
    MethodCallMetadataSupport? _methodCalls;
    MethodSemanticIdentitySupport? _semanticIdentity;
    MethodAnalysisSupport? _methodAnalysis;
    PublicMethodRootSupport? _publicRoots;
    ReferenceAssemblyStatusSupport? _referenceStatus;
    MethodDefinitionDeclaredSources? _declaredSources;

    /// <summary>
    /// The attribution that records traversed bodies' operands while an
    /// all-definition relation is pending; null once it settles or when the
    /// breadth declares none.
    /// </summary>
    DeclaredSourceAttribution? _pendingRelation;
    MethodDefinitionGeneratedExpansionWork? _pendingRelationWork;

    /// <summary>
    /// The physical group's receipt when this execution is one lane of a
    /// shared traversal; otherwise this execution's own receipt.
    /// </summary>
    MethodDefinitionSourceCoverageBuilder _physicalCoverage;
    CriticalFailure? _critical;

    /// <summary>
    /// Units visited by the pass in progress, kept current as it runs, so an
    /// abort that unwinds the pass still records what was observed.
    /// </summary>
    internal int PassUnitsVisited;

    MethodDefinitionExecution(
        WorkDescription description,
        MethodDefinitionSourceBreadth breadth,
        MethodDefinitionTerminalWorkLimits terminalWorkLimits,
        bool tracksSourceCoverage,
        MethodBodyAnalyzerPlan? instructionPlan)
    {
        _description = description;
        _instructionPlan = instructionPlan;
        _breadth = breadth;
        _sourceCoverage =
            new MethodDefinitionSourceCoverageBuilder(
                tracksSourceCoverage,
                tracksSourceCoverage
                    ? terminalWorkLimits
                    : null);
        _physicalCoverage = _sourceCoverage;
        _states = new ProducerState[description.Producers.Length];
    }

    public WorkReceipt Receipt { get; private set; } = new(0, []);

    internal MethodDefinitionSourceCoverage SourceCoverage { get; private set; } =
        new(
            MethodDefinitionHandleCoverage.Empty,
            MethodDefinitionHandleCoverage.Empty,
            MethodDefinitionHandleCoverage.Empty,
            MethodDefinitionHandleCoverage.Empty,
            MethodDefinitionHandleCoverage.Empty);

    internal MethodDefinitionSourceCoverageBuilder SourceCoverageBuilder =>
        _sourceCoverage;

    internal MethodRowGate Gate =>
        _gate ?? throw new InvalidOperationException(
            "The Method-source gate has not been bound.");

    internal LibraryMethodAnalysisRunner? Lookup => _lookup;

    internal MethodBodyAnalyzerPlan? InstructionPlan => _instructionPlan;

    /// <summary>
    /// The declared-source outcome of each unit, present when the breadth
    /// declared generated-body expansion and the relation has settled: before
    /// traversal over exact breadth, and at source completion over
    /// all-definition breadth.
    /// </summary>
    internal MethodDefinitionDeclaredSources? DeclaredSources =>
        _declaredSources;

    /// <summary>
    /// Whether the breadth declared a relation that settles at source
    /// completion and has not settled yet.
    /// </summary>
    internal bool DeclaredSourcesPending => _pendingRelation is not null;

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
            MethodDefinitionTerminalWorkLimits.Unbounded,
            tracksSourceCoverage: false,
            MethodBodyAnalyzerPlanner.Plan(description));

    internal static MethodDefinitionExecution Execute(
        WorkDescription description,
        string sourceName,
        PEReader peReader,
        MethodDefinitionSourceBreadth breadth,
        MethodDefinitionTerminalWorkLimits terminalWorkLimits,
        AssemblyReferenceBindingAccess? referenceBinding = null)
        => Execute(
            description,
            sourceName,
            peReader,
            breadth,
            terminalWorkLimits,
            tracksSourceCoverage: true,
            MethodBodyAnalyzerPlanner.Plan(description),
            referenceBinding);

    static MethodDefinitionExecution Execute(
        WorkDescription description,
        string sourceName,
        PEReader peReader,
        MethodDefinitionSourceBreadth breadth,
        MethodDefinitionTerminalWorkLimits terminalWorkLimits,
        bool tracksSourceCoverage,
        MethodBodyAnalyzerPlan? instructionPlan,
        AssemblyReferenceBindingAccess? referenceBinding = null)
    {
        ArgumentNullException.ThrowIfNull(description);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceName);
        ArgumentNullException.ThrowIfNull(peReader);
        ArgumentNullException.ThrowIfNull(breadth);
        ArgumentNullException.ThrowIfNull(terminalWorkLimits);

        MethodDefinitionExecution execution =
            CreateExecution(
                description,
                breadth,
                terminalWorkLimits,
                tracksSourceCoverage,
                instructionPlan);
        MetadataReader? exposureReader =
            peReader.HasMetadata ? peReader.GetMetadataReader() : null;
        if (execution._states.Any(
                static state => state.HasPublicMethodRootsLayer))
        {
            execution._publicRoots = new(exposureReader);
        }
        if (execution._states.Any(
                static state => state.HasReferenceAssemblyStatusLayer))
        {
            execution._referenceStatus = new(exposureReader);
        }
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
        bool referenceBindingDeclared = false;
        bool memorySafetyDeclared = false;
        bool callTargetsDeclared = false;
        bool methodCallsDeclared = false;
        bool calleeIdentitiesShared = false;
        bool semanticIdentityDeclared = false;
        bool methodAnalysisDeclared = false;
        foreach (ProducerState state in execution._states)
        {
            lookupDeclared |= state.HasLookupLayer;
            referenceBindingDeclared |= state.HasReferenceBindingLayer;
            memorySafetyDeclared |= state.HasMemorySafetyLayer;
            callTargetsDeclared |= state.HasCallTargetLayer;
            methodCallsDeclared |=
                state.HasCallsLayer
                || state.HasCallOperandsLayer
                || state.HasLocalSignatureLayer;
            calleeIdentitiesShared |= state.SharesCalleeIdentities;
            semanticIdentityDeclared |= state.HasSemanticIdentityLayer;
            methodAnalysisDeclared |= state.HasMethodAnalysisLayer;
        }
        execution._memorySafety =
            memorySafetyDeclared
                ? new MemorySafetyContractSupport(reader)
                : null;
        execution._callTargets =
            callTargetsDeclared
                ? new SameModuleCallTargetSupport(reader)
                : null;
        execution._methodCalls =
            methodCallsDeclared
                ? new MethodCallMetadataSupport(reader, calleeIdentitiesShared)
                : null;
        execution._semanticIdentity =
            semanticIdentityDeclared
                ? new MethodSemanticIdentitySupport(reader, sourceName)
                : null;
        execution._methodAnalysis =
            methodAnalysisDeclared
                ? new MethodAnalysisSupport(
                    sourceName,
                    reader,
                    peReader,
                    execution._memorySafety)
                : null;
        if (referenceBindingDeclared && referenceBinding is null)
        {
            throw new ProducerContractException(
                "A producer declared reference binding but the operation "
                + "access carries none.");
        }
        bool generatedExpansion =
            breadth.Expansion.HasFlag(
                MethodDefinitionSourceExpansion.GeneratedExecutionBodies);
        MethodDefinitionGeneratedExpansionWork? expansionWork =
            generatedExpansion
                ? new(
                    breadth.GeneratedExpansionLimits
                    ?? throw new ProducerContractException(
                        "Generated Method-source expansion requires finite "
                        + "work limits."))
                : null;
        // Expansion needs only declared-source attribution. The module lookup
        // still runs on the legacy builder, which then shares the same
        // attribution and its caches over exact breadth, where the relation
        // settles first. A pending all-definition relation keeps its work
        // apart from the lookup's, so no unit visit charges it.
        bool relationPending =
            generatedExpansion
            && breadth.Kind == MethodDefinitionSourceBreadthKind.AllDefinitions;
        DeclaredSourceAttribution? attribution =
            generatedExpansion
                ? new DeclaredSourceAttribution(
                    sourceName,
                    reader,
                    peReader,
                    generatedExpansionWork: expansionWork)
                : null;
        using LibraryBodyAnalysisBuilder? builder =
            lookupDeclared
            ? new LibraryBodyAnalysisBuilder(
                sourceName,
                reader,
                peReader,
                bindingPolicy: referenceBinding?.Policy,
                rootAssembly: referenceBinding?.Subject,
                attribution: relationPending ? null : attribution)
            : null;
        execution._lookup =
            lookupDeclared
                ? new LibraryMethodAnalysisRunner(builder!)
                : null;
        MethodRowGate gate = execution.Gate;
        LibraryMethodAnalysisRunner? lookup = execution.Lookup;
        int unitsVisited = 0;

        if (relationPending)
        {
            // Every body is already in breadth, so the relation adds none. It
            // reads the operands traversal records and settles at source
            // completion.
            execution._pendingRelation = attribution;
            execution._pendingRelationWork = expansionWork;
        }
        else if (generatedExpansion)
        {
            try
            {
                ImmutableArray<MethodDefinitionHandle> directMethods =
                    ResolveDirectMethods(reader, breadth);
                MethodDefinitionGeneratedExpansionResult expansion =
                    attribution!.ExpandGeneratedExecutionBodies(
                        directMethods,
                        breadth.GeneratedRoles,
                        breadth.Kind
                            == MethodDefinitionSourceBreadthKind.ExactTypes
                                ? breadth.Types
                                : [],
                        breadth.IncludesUnauthenticatedCandidates);
                execution._sourceCoverage.RecordGeneratedExpansion(
                    expansion.Coverage);
                execution._declaredSources =
                    new MethodDefinitionDeclaredSources(expansion.Coverage);
                execution._breadth =
                    MethodDefinitionSourceBreadth.ExactMethods(
                        expansion.Methods);
                if (expansion.Bound is { } bound)
                    execution.BoundRelationReaders(bound);
            }
            catch (Exception ex)
                when (LibraryMethodAnalysisRunner
                    .IsRecoverableMethodFailure(ex))
            {
                expansionWork!.Settle();
                execution._sourceCoverage.RecordGeneratedExpansion(
                    expansionWork.Build());
                execution._declaredSources =
                    new MethodDefinitionDeclaredSources(
                        MethodDefinitionGeneratedExpansionCoverage.Empty);
                execution.FailActiveSource(
                    0,
                    GeneratedExpansionLabel,
                    ex);
                execution._breadth =
                    MethodDefinitionSourceBreadth.ExactMethods();
            }
        }

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
                    || execution._pendingRelation is not null
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

            if (execution._pendingRelation is not null)
                execution.SettleAllDefinitionRelation(reader);
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

        static ImmutableArray<MethodDefinitionHandle> ResolveDirectMethods(
            MetadataReader reader,
            MethodDefinitionSourceBreadth breadth)
        {
            if (breadth.Kind == MethodDefinitionSourceBreadthKind.ExactMethods)
                return breadth.Methods;
            if (breadth.Kind != MethodDefinitionSourceBreadthKind.ExactTypes)
            {
                throw new ProducerContractException(
                    "Generated Method-source expansion requires exact MethodDef "
                    + "or TypeDef breadth.");
            }

            var methods =
                ImmutableArray.CreateBuilder<MethodDefinitionHandle>();
            foreach (TypeDefinitionHandle typeHandle in breadth.Types)
            {
                TypeDefinition type = reader.GetTypeDefinition(typeHandle);
                methods.AddRange(type.GetMethods());
            }
            return methods.ToImmutable();
        }

        execution.PropagateFailures();
        execution.Receipt = execution.CreateReceipt(unitsVisited, gate);
        execution.PublishSourceCoverage();
        return execution;
    }

    static MethodDefinitionExecution CreateExecution(
        WorkDescription description,
        MethodDefinitionSourceBreadth breadth,
        MethodDefinitionTerminalWorkLimits terminalWorkLimits,
        bool tracksSourceCoverage,
        MethodBodyAnalyzerPlan? instructionPlan)
    {
        var execution = new MethodDefinitionExecution(
            description,
            breadth,
            terminalWorkLimits,
            tracksSourceCoverage,
            instructionPlan);
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
                description.FactRetention[i],
                description.ResultRetention[i]);
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
        IReadOnlyList<MethodDefinitionTerminalWorkLimits>
            terminalWorkLimits,
        MethodDefinitionSourceBreadth breadth,
        MethodBodyAnalyzerPlan? instructionPlan,
        string sourceName,
        PEReader peReader,
        AssemblyReferenceBindingAccess? referenceBinding = null)
    {
        ArgumentNullException.ThrowIfNull(descriptions);
        ArgumentNullException.ThrowIfNull(terminalWorkLimits);
        ArgumentNullException.ThrowIfNull(breadth);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceName);
        ArgumentNullException.ThrowIfNull(peReader);
        if (descriptions.Count < 2)
        {
            throw new ArgumentException(
                "Shared Method-source execution requires at least two lanes.",
                nameof(descriptions));
        }
        if (terminalWorkLimits.Count != descriptions.Count)
        {
            throw new ArgumentException(
                "Shared Method-source execution requires one terminal-work "
                + "limit set per lane.",
                nameof(terminalWorkLimits));
        }
        if (breadth.Kind
            is not (
                MethodDefinitionSourceBreadthKind.AllDefinitions
                or MethodDefinitionSourceBreadthKind.ExactMethods)
            || breadth.Expansion != MethodDefinitionSourceExpansion.None)
        {
            throw new ProducerContractException(
                "Shared Method-source execution accepts all-definition or "
                    + "exact-MethodDef breadth without expansion.");
        }

        var executions =
            ImmutableArray.CreateBuilder<MethodDefinitionExecution>(
                descriptions.Count);
        for (int i = 0; i < descriptions.Count; i++)
        {
            WorkDescription description = descriptions[i];
            ArgumentNullException.ThrowIfNull(description);
            ArgumentNullException.ThrowIfNull(terminalWorkLimits[i]);
            if (description.PassCount != 1)
            {
                throw new ProducerContractException(
                    "Shared Method-source execution currently accepts only "
                    + "single-pass producer descriptions.");
            }

            executions.Add(
                CreateExecution(
                    description,
                    breadth,
                    terminalWorkLimits[i],
                    tracksSourceCoverage: true,
                    instructionPlan));
        }

        ImmutableArray<MethodDefinitionExecution> lanes =
            executions.MoveToImmutable();
        var physicalCoverage =
            new MethodDefinitionSourceCoverageBuilder(enabled: true);
        MetadataReader? exposureReader =
            peReader.HasMetadata ? peReader.GetMetadataReader() : null;
        PublicMethodRootSupport? publicRoots =
            lanes.Any(static lane => lane._states.Any(
                static state => state.HasPublicMethodRootsLayer))
                ? new(exposureReader)
                : null;
        ReferenceAssemblyStatusSupport? referenceStatus =
            lanes.Any(static lane => lane._states.Any(
                static state => state.HasReferenceAssemblyStatusLayer))
                ? new(exposureReader)
                : null;
        foreach (MethodDefinitionExecution lane in lanes)
        {
            lane._physicalCoverage = physicalCoverage;
            if (lane._states.Any(
                    static state => state.HasPublicMethodRootsLayer))
            {
                lane._publicRoots = publicRoots;
            }
            if (lane._states.Any(
                    static state => state.HasReferenceAssemblyStatusLayer))
            {
                lane._referenceStatus = referenceStatus;
            }
        }

        if (!peReader.HasMetadata)
        {
            foreach (MethodDefinitionExecution lane in lanes)
                lane.CompleteWithoutUnits();
            return new(lanes, physicalCoverage.Build());
        }

        MetadataReader reader = peReader.GetMetadataReader();
        var builders =
            new LibraryBodyAnalysisBuilder?[lanes.Length];
        MemorySafetyContractSupport? memorySafety =
            lanes.Any(static lane => lane._states.Any(
                static state => state.HasMemorySafetyLayer))
                ? new MemorySafetyContractSupport(reader)
                : null;
        SameModuleCallTargetSupport? callTargets =
            lanes.Any(static lane => lane._states.Any(
                static state => state.HasCallTargetLayer))
                ? new SameModuleCallTargetSupport(reader)
                : null;
        MethodCallMetadataSupport? methodCalls =
            lanes.Any(static lane => lane._states.Any(
                static state =>
                    state.HasCallsLayer
                    || state.HasCallOperandsLayer
                    || state.HasLocalSignatureLayer))
                ? new MethodCallMetadataSupport(
                    reader,
                    lanes.Any(static lane => lane._states.Any(
                        static state => state.SharesCalleeIdentities)))
                : null;
        MethodSemanticIdentitySupport? semanticIdentity =
            lanes.Any(static lane => lane._states.Any(
                static state => state.HasSemanticIdentityLayer))
                ? new MethodSemanticIdentitySupport(reader, sourceName)
                : null;
        MethodAnalysisSupport? methodAnalysis =
            lanes.Any(static lane => lane._states.Any(
                static state => state.HasMethodAnalysisLayer))
                ? new MethodAnalysisSupport(
                    sourceName,
                    reader,
                    peReader,
                    memorySafety)
                : null;
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
                if (lane._states.Any(
                        static state => state.HasMemorySafetyLayer))
                {
                    lane._memorySafety = memorySafety;
                }
                if (lane._states.Any(
                        static state => state.HasCallTargetLayer))
                {
                    lane._callTargets = callTargets;
                }
                if (lane._states.Any(
                        static state =>
                            state.HasCallsLayer
                            || state.HasCallOperandsLayer
                            || state.HasLocalSignatureLayer))
                {
                    lane._methodCalls = methodCalls;
                }
                if (lane._states.Any(
                        static state => state.HasSemanticIdentityLayer))
                {
                    lane._semanticIdentity = semanticIdentity;
                }
                if (lane._states.Any(
                        static state => state.HasMethodAnalysisLayer))
                {
                    lane._methodAnalysis = methodAnalysis;
                }
                if (referenceBinding is null
                    && lane._states.Any(
                        static state => state.HasReferenceBindingLayer))
                {
                    throw new ProducerContractException(
                        "A request-set lane declared reference binding but "
                        + "the operation access carries none.");
                }
                if (lookupDeclared)
                {
                    builders[laneIndex] =
                        new LibraryBodyAnalysisBuilder(
                            sourceName,
                            reader,
                            peReader,
                            bindingPolicy: referenceBinding?.Policy,
                            rootAssembly: referenceBinding?.Subject);
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

            var unit = new MethodDefinitionUnit(
                reader,
                peReader,
                physicalCoverage,
                retainResolvedInstructionDetail:
                    instructionPlan?.Demand.Detail
                        == MethodBodyInstructionDetail.SelectiveOperands);
            bool[] laneInScope = new bool[lanes.Length];
            bool fuseInstructionShapes =
                instructionPlan?.Source
                    == MethodBodyInstructionSourceKind.NoRetentionStream;
            var fusedInstructionVisits =
                fuseInstructionShapes
                    ? new FusedInstructionShapeVisit[
                        visiting.Sum(static states => states.Length)]
                    : null;
            int[] laneInstructionVisits =
                fuseInstructionShapes
                    ? new int[lanes.Length]
                    : [];
            bool[] laneVisitedInstructions =
                fuseInstructionShapes
                    ? new bool[lanes.Length]
                    : [];

            if (breadth.Kind
                == MethodDefinitionSourceBreadthKind.ExactMethods)
            {
                VisitSharedExactMethods(
                    breadth.Methods,
                    lanes,
                    visiting,
                    reader,
                    ref unit,
                    physicalCoverage,
                    laneInScope,
                    fusedInstructionVisits,
                    laneInstructionVisits,
                    laneVisitedInstructions);
            }
            else
            {
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

                    bool anyLaneInScope = PrepareSharedType(
                        lanes,
                        visiting,
                        reader,
                        typeHandle,
                        typeDefinition,
                        laneInScope);
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
                            VisitSharedPreparedMethod(
                                lanes,
                                visiting,
                                ref unit,
                                methodHandle,
                                laneInScope,
                                fusedInstructionVisits,
                                laneInstructionVisits,
                                laneVisitedInstructions);

                            if (!AnyLaneActiveInScope(
                                    visiting,
                                    laneInScope))
                            {
                                break;
                            }
                        }
                    }
                    catch (Exception ex)
                        when (LibraryMethodAnalysisRunner
                            .IsRecoverableMethodFailure(ex))
                    {
                        FailSharedSource(
                            lanes,
                            laneInScope,
                            MetadataTokens.GetToken(typeHandle),
                            "(method source)",
                            ex);
                    }

                    if (!AnyLaneActive(visiting))
                        break;
                }
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

    static void VisitSharedExactMethods(
        ImmutableArray<MethodDefinitionHandle> methods,
        ImmutableArray<MethodDefinitionExecution> lanes,
        ProducerState[][] visiting,
        MetadataReader reader,
        ref MethodDefinitionUnit unit,
        MethodDefinitionSourceCoverageBuilder physicalCoverage,
        bool[] laneInScope,
        FusedInstructionShapeVisit[]? fusedInstructionVisits,
        int[] laneInstructionVisits,
        bool[] laneVisitedInstructions)
    {
        TypeDefinitionHandle currentTypeHandle = default;
        TypeDefinition currentTypeDefinition = default;
        foreach (MethodDefinitionHandle methodHandle in methods)
        {
            if (!AnyLaneActive(visiting))
                return;

            physicalCoverage.RecordDefinitionExamined(methodHandle);
            MethodDefinition methodDefinition;
            try
            {
                methodDefinition =
                    reader.GetMethodDefinition(methodHandle);
            }
            catch (Exception ex)
                when (LibraryMethodAnalysisRunner
                    .IsRecoverableMethodFailure(ex))
            {
                FailSharedSource(
                    lanes,
                    laneInScope: null,
                    MetadataTokens.GetToken(methodHandle),
                    "(method source)",
                    ex);
                continue;
            }

            TypeDefinitionHandle typeHandle =
                methodDefinition.GetDeclaringType();
            if (typeHandle != currentTypeHandle)
            {
                try
                {
                    currentTypeDefinition =
                        reader.GetTypeDefinition(typeHandle);
                }
                catch (Exception ex)
                    when (LibraryMethodAnalysisRunner
                        .IsRecoverableMethodFailure(ex))
                {
                    FailSharedSource(
                        lanes,
                        laneInScope: null,
                        MetadataTokens.GetToken(typeHandle),
                        "(type source)",
                        ex);
                    continue;
                }
                currentTypeHandle = typeHandle;

                if (!PrepareSharedType(
                        lanes,
                        visiting,
                        reader,
                        typeHandle,
                        currentTypeDefinition,
                        laneInScope))
                {
                    continue;
                }
            }

            if (!AnyLaneActiveInScope(
                    visiting,
                    laneInScope))
            {
                continue;
            }

            unit.MoveToPreviouslyRead(
                typeHandle,
                currentTypeDefinition,
                methodHandle,
                methodDefinition);
            VisitSharedPreparedMethod(
                lanes,
                visiting,
                ref unit,
                methodHandle,
                laneInScope,
                fusedInstructionVisits,
                laneInstructionVisits,
                laneVisitedInstructions);
        }
    }

    static bool PrepareSharedType(
        ImmutableArray<MethodDefinitionExecution> lanes,
        ProducerState[][] visiting,
        MetadataReader reader,
        TypeDefinitionHandle typeHandle,
        TypeDefinition typeDefinition,
        bool[] laneInScope)
    {
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

        return anyLaneInScope;
    }

    static void VisitSharedPreparedMethod(
        ImmutableArray<MethodDefinitionExecution> lanes,
        ProducerState[][] visiting,
        ref MethodDefinitionUnit unit,
        MethodDefinitionHandle methodHandle,
        bool[] laneInScope,
        FusedInstructionShapeVisit[]? fusedInstructionVisits,
        int[] laneInstructionVisits,
        bool[] laneVisitedInstructions)
    {
        int fusedInstructionVisitCount = 0;
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
            if (fusedInstructionVisits is null)
            {
                lane.VisitPreparedUnit(
                    visiting[laneIndex],
                    ref unit,
                    methodHandle);
            }
            else
            {
                lane.VisitPreparedUnit(
                    visiting[laneIndex],
                    ref unit,
                    methodHandle,
                    laneIndex,
                    fusedInstructionVisits,
                    ref fusedInstructionVisitCount);
            }
        }

        if (fusedInstructionVisitCount != 0)
        {
            VisitFusedInstructionShapes(
                lanes,
                ref unit,
                fusedInstructionVisits!,
                fusedInstructionVisitCount,
                laneInstructionVisits,
                laneVisitedInstructions);
        }
    }

    static void FailSharedSource(
        ImmutableArray<MethodDefinitionExecution> lanes,
        bool[]? laneInScope,
        int token,
        string unit,
        Exception exception)
    {
        for (int laneIndex = 0;
            laneIndex < lanes.Length;
            laneIndex++)
        {
            if (laneInScope is null
                || laneInScope[laneIndex])
            {
                lanes[laneIndex].FailActiveSource(
                    token,
                    unit,
                    exception);
            }
        }
    }

    static void VisitFusedInstructionShapes(
        ImmutableArray<MethodDefinitionExecution> lanes,
        ref MethodDefinitionUnit unit,
        FusedInstructionShapeVisit[] visits,
        int visitCount,
        int[] laneInstructionVisits,
        bool[] laneVisitedInstructions)
    {
        Array.Clear(laneInstructionVisits);
        Array.Clear(laneVisitedInstructions);
        var dispatch = new FusedInstructionShapeDispatch(
            visits,
            visitCount);
        int physicalInstructionsVisited = 0;
        Exception? failure = null;
        try
        {
            InstructionDecoder.VisitWithProgress(
                visits[0].Body,
                ref dispatch,
                static (
                    ref FusedInstructionShapeDispatch current,
                    ILOpCode opcode,
                    int _,
                    int encodedLength) =>
                        current.Visit(opcode, encodedLength),
                ref physicalInstructionsVisited);
        }
        catch (Exception ex)
            when (ex
                    is not
                        MethodDefinitionTerminalWorkLimitExceededException
                && LibraryMethodAnalysisRunner
                    .IsRecoverableMethodFailure(ex))
        {
            failure = ex;
        }
        finally
        {
            unit.RecordPhysicalNoRetentionInstructionWork(
                physicalInstructionsVisited);
            for (int i = 0; i < visitCount; i++)
            {
                ref FusedInstructionShapeVisit visit =
                    ref visits[i];
                int laneIndex = visit.LaneIndex;
                laneVisitedInstructions[laneIndex] = true;
                laneInstructionVisits[laneIndex] =
                    Math.Max(
                        laneInstructionVisits[laneIndex],
                        visit.InstructionsVisited);
            }
            for (int laneIndex = 0;
                laneIndex < lanes.Length;
                laneIndex++)
            {
                if (laneVisitedInstructions[laneIndex])
                {
                    lanes[laneIndex]
                        .RecordNoRetentionInstructionWork(
                            laneInstructionVisits[laneIndex]);
                }
            }
        }

        for (int i = 0; i < visitCount; i++)
        {
            ref FusedInstructionShapeVisit visit =
                ref visits[i];
            bool settled =
                visit.State.CompleteInstructionShapes(failure);
            visit.State.Execution.CompleteVisitedUnit(
                visit.State,
                settled);
            visit = default;
        }
    }

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

    /// <summary>
    /// Whether the producer will still complete and so reads the relation:
    /// it is active, or its visits stopped at a settled terminal.
    /// </summary>
    static bool ReadsRelation(ProducerState state) =>
        state.IsActive || state.Outcome is ProducerOutcome.Stopped;

    /// <summary>
    /// Marks every producer that reads the relation as running over the
    /// retained prefix of a bounded expansion. The producers still visit that
    /// prefix and complete; their outcome becomes
    /// <see cref="ProducerOutcome.SourceBounded"/>, including a producer that
    /// stopped early, because it answered only over the prefix.
    /// </summary>
    void BoundRelationReaders(
        MethodDefinitionGeneratedExpansionBoundException bound)
    {
        // The bound is source work, not a unit; its subject token stays in
        // the expansion coverage and the message.
        const int unit = 0;
        string message = ProducerFailure.Describe(bound);
        foreach (ProducerState state in _states)
        {
            if (!ReadsRelation(state))
                continue;

            state.SourceBounded = true;
            state.Failure =
                new ProducerFailure(unit, GeneratedExpansionLabel, message);
            state.SourceFailure = new MethodDefinitionSourceFailure(
                unit,
                GeneratedExpansionLabel,
                message);
        }
    }

    const string GeneratedExpansionLabel = "(generated body expansion)";

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
            state.SourceBounded = false;
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
        if (!state.RetainsResult
            && state.Outcome is
                ProducerOutcome.Complete
                or ProducerOutcome.Stopped
                or ProducerOutcome.SourceBounded)
        {
            throw new ProducerContractException(
                $"Producer '{producer.Identity}' was planned only for "
                + "same-unit facts; no result was retained.");
        }
        return state.Outcome switch
        {
            ProducerOutcome.Complete or ProducerOutcome.Stopped =>
                new(state.Outcome.Value, state.Result),
            ProducerOutcome.SourceBounded =>
                new(state.Outcome.Value, state.Result, state.Failure),
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
            _sourceCoverage,
            retainResolvedInstructionDetail:
                _instructionPlan?.Demand.Detail
                    == MethodBodyInstructionDetail.SelectiveOperands);
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
            bool anyActive =
                VisitPreparedUnit(visiting, ref unit, methodHandle);
            if (_pendingRelation is not null)
                RecordRelationReferences(ref unit);
            if (!anyActive)
                return false;
        }

        return true;
    }

    /// <summary>
    /// Records the visited unit's call and function-pointer operands for the
    /// pending relation, from the body a producer already acquired or, when
    /// none did, from the relation's own read of it.
    /// </summary>
    void RecordRelationReferences(ref MethodDefinitionUnit unit)
    {
        try
        {
            MethodDefinition method = unit.MethodDefinition;
            MethodBodyBlock? body = null;
            bool acquiredByRelation = false;
            if (method.RelativeVirtualAddress != 0)
            {
                body = unit.AcquiredBody;
                if (body is null)
                {
                    body = unit.AcquireRelationBody();
                    acquiredByRelation = true;
                }
            }
            _pendingRelation!.RecordTraversedBodyReferences(
                unit.MethodHandle,
                method,
                body,
                acquiredByRelation);
        }
        catch (Exception ex)
            when (LibraryMethodAnalysisRunner
                .IsRecoverableMethodFailure(ex))
        {
            // An unreadable body stays unrecorded; settling probes it, and
            // that probe observes the same failure.
        }
    }

    /// <summary>
    /// Settles the all-definition relation at source completion: the sparse
    /// expansion from every user type's methods, reading the operands
    /// traversal recorded. A reached bound keeps the retained prefix.
    /// </summary>
    void SettleAllDefinitionRelation(MetadataReader reader)
    {
        DeclaredSourceAttribution attribution = _pendingRelation!;
        MethodDefinitionGeneratedExpansionWork work = _pendingRelationWork!;
        _pendingRelation = null;
        _pendingRelationWork = null;
        try
        {
            var userTypes =
                ImmutableArray.CreateBuilder<TypeDefinitionHandle>();
            var userMethods =
                ImmutableArray.CreateBuilder<MethodDefinitionHandle>();
            foreach (TypeDefinitionHandle typeHandle in reader.TypeDefinitions)
            {
                TypeDefinition type = reader.GetTypeDefinition(typeHandle);
                if (attribution.PrimaryMetadataResolver
                    .IsCompilerGeneratedTypeOrEnclosing(typeHandle))
                {
                    if (!type.GetDeclaringType().IsNil)
                        continue;

                    ImmutableArray<MethodDefinitionHandle> methods =
                        [.. type.GetMethods()];
                    HashSet<string> liftedOwnerNames =
                    [
                        .. methods
                            .Select(method =>
                                reader.GetString(
                                    reader.GetMethodDefinition(
                                        method).Name))
                            .Select(name =>
                                CompilerGeneratedNames
                                    .TryGetLiftedOwnerName(
                                        name,
                                        out string ownerName)
                                    ? ownerName
                                    : null)
                            .OfType<string>(),
                    ];
                    userMethods.AddRange(
                        methods.Where(method =>
                            liftedOwnerNames.Contains(
                                reader.GetString(
                                    reader.GetMethodDefinition(
                                        method).Name))));
                    continue;
                }
                userTypes.Add(typeHandle);
                userMethods.AddRange(type.GetMethods());
            }

            MethodDefinitionGeneratedExpansionResult relation =
                attribution.ExpandGeneratedExecutionBodies(
                    userMethods.ToImmutable(),
                    _breadth.GeneratedRoles,
                    userTypes.ToImmutable());
            _sourceCoverage.RecordGeneratedExpansion(relation.Coverage);
            _declaredSources =
                new MethodDefinitionDeclaredSources(relation.Coverage);
            if (relation.Bound is { } bound)
                BoundRelationReaders(bound);
        }
        catch (Exception ex)
            when (LibraryMethodAnalysisRunner
                .IsRecoverableMethodFailure(ex))
        {
            work.Settle();
            _sourceCoverage.RecordGeneratedExpansion(work.Build());
            _declaredSources =
                new MethodDefinitionDeclaredSources(
                    MethodDefinitionGeneratedExpansionCoverage.Empty);
            // A producer that stopped early still reads the relation at
            // completion, so the failure is its source failure too.
            foreach (ProducerState state in _states)
            {
                if (ReadsRelation(state))
                    FailSource(state, 0, GeneratedExpansionLabel, ex);
            }
        }
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
            _memorySafety,
            _callTargets,
            _methodCalls,
            _semanticIdentity,
            _methodAnalysis,
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
        int fusedInstructionVisitCount = 0;
        return VisitPreparedUnit(
            visiting,
            ref unit,
            methodHandle,
            laneIndex: -1,
            fusedInstructionVisits: null,
            ref fusedInstructionVisitCount);
    }

    bool VisitPreparedUnit(
        ProducerState[] visiting,
        ref MethodDefinitionUnit unit,
        MethodDefinitionHandle methodHandle,
        int laneIndex,
        FusedInstructionShapeVisit[]? fusedInstructionVisits,
        ref int fusedInstructionVisitCount)
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

            if (!state.TypeInScopeNow)
            {
                anyActive = true;
                continue;
            }

            if (fusedInstructionVisits is not null
                && DependsOnPendingInstructionVisit(
                    state,
                    fusedInstructionVisits,
                    fusedInstructionVisitCount,
                    laneIndex))
            {
                throw new ProducerContractException(
                    $"Producer '{state.Producer.Identity}' reads a "
                    + "same-unit fact from a fused Method-body analyzer "
                    + "before the shared instruction stream completes.");
            }

            if (state.GuardStates.Length != 0
                && !InScope(state, unitToken))
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

                if (fusedInstructionVisits is not null
                    && !state.InstructionAnalyzers.IsEmpty)
                {
                    if (!state.SupportsFusedInstructionShapes)
                    {
                        throw new ProducerContractException(
                            $"Producer '{state.Producer.Identity}' declares "
                            + "a forward-only Method-body analyzer but does "
                            + "not support fused instruction visitation.");
                    }

                    state.UnitsAttempted++;
                    var view = new MethodDefinitionView(
                        ref unit,
                        state);
                    if (state.TryBeginInstructionShapes(
                            view,
                            out MethodBodyBlock? body,
                            out bool settled))
                    {
                        fusedInstructionVisits[
                            fusedInstructionVisitCount++] =
                                new(
                                    state,
                                    body
                                    ?? throw new InvalidOperationException(
                                        "An instruction consumer did not "
                                        + "supply its acquired body."),
                                    laneIndex);
                    }
                    else
                    {
                        CompleteVisitedUnit(state, settled);
                    }
                }
                else
                {
                    VisitUnit(ref unit, state);
                }
            }
            catch (ProducerAbortException abort)
            {
                state.Execution.Abort(abort.Failure);
                return false;
            }
            catch (MethodDefinitionTerminalWorkLimitExceededException ex)
            {
                state.UnitsFailed++;
                FailActiveSource(
                    MetadataTokens.GetToken(unit.MethodHandle),
                    unit.Label,
                    ex);
            }
            anyActive |= state.IsActive;
        }

        return anyActive;
    }

    static bool DependsOnPendingInstructionVisit(
        ProducerState state,
        FusedInstructionShapeVisit[] visits,
        int visitCount,
        int laneIndex)
    {
        foreach (ProducerDependency dependency
            in state.Producer.Dependencies)
        {
            if (dependency.Kind
                != ProducerDependencyKind.VisitNeedsVisit)
            {
                continue;
            }

            for (int i = 0; i < visitCount; i++)
            {
                FusedInstructionShapeVisit visit = visits[i];
                if (visit.LaneIndex == laneIndex
                    && ReferenceEquals(
                        visit.State.Producer,
                        dependency.Producer))
                {
                    return true;
                }
            }
        }

        return false;
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
            when (ex
                    is not
                        MethodDefinitionTerminalWorkLimitExceededException
                && LibraryMethodAnalysisRunner
                    .IsRecoverableMethodFailure(ex))
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

        CompleteVisitedUnit(state, settled);
    }

    void CompleteVisitedUnit(
        ProducerState state,
        bool settled)
    {
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

    void RecordNoRetentionInstructionWork(
        int instructionsVisited) =>
        _sourceCoverage.RecordInstructionWork(
            MethodBodyInstructionSourceKind.NoRetentionStream,
            instructionsVisited);

    void FailIfPrerequisiteFailed(ProducerState state)
    {
        // A producer whose visits stopped at a settled terminal still needs
        // its completion prerequisites, so only a final outcome is skipped.
        if (state.Outcome is ProducerOutcome.Complete
            or ProducerOutcome.SourceBounded
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

        // A bounded source settles no terminal: even a producer that stopped
        // early answered only over the retained prefix.
        state.Outcome =
            state.SourceBounded
                && state.Outcome is null or ProducerOutcome.Stopped
                ? ProducerOutcome.SourceBounded
                : state.Outcome ?? ProducerOutcome.Complete;
        state.IsActive = false;
    }

    /// <summary>
    /// The subject's public MethodDef roots, recording the computation for the
    /// physical source once and for the lane whose read created it.
    /// </summary>
    internal PublicMethodRootsResult ReadPublicMethodRoots()
    {
        PublicMethodRootSupport support =
            _publicRoots ?? throw new InvalidOperationException(
                "Public MethodDef root support was not declared by any "
                + "planned producer.");
        PublicMethodRootsResult result =
            support.Acquire(out bool constructed);
        RecordExposure(coverage =>
        {
            if (constructed)
                coverage.RecordPublicMethodRootsConstruction(result);
            coverage.RecordPublicMethodRootsRead();
        });
        return result;
    }

    /// <summary>
    /// The subject's reference-assembly status, recording the classification
    /// for the physical source once and for the lane whose read created it.
    /// </summary>
    internal ReferenceAssemblyState ReadReferenceAssemblyStatus()
    {
        ReferenceAssemblyStatusSupport support =
            _referenceStatus ?? throw new InvalidOperationException(
                "Reference-assembly status support was not declared by any "
                + "planned producer.");
        ReferenceAssemblyState state =
            support.Acquire(out bool constructed);
        RecordExposure(coverage =>
        {
            if (constructed)
            {
                coverage.RecordReferenceAssemblyStatusConstruction(
                    support.AttributeRowsRead);
            }
            coverage.RecordReferenceAssemblyStatusRead();
        });
        return state;
    }

    void RecordExposure(Action<MethodDefinitionSourceCoverageBuilder> record)
    {
        record(_sourceCoverage);
        if (!ReferenceEquals(_physicalCoverage, _sourceCoverage))
            record(_physicalCoverage);
    }

    void CompleteWithoutUnits()
    {
        foreach (int completion in _description.CompletionIndices)
            CompleteProducer(_states[completion]);
        PropagateFailures();
        Receipt = CreateReceipt(0, gate: null);
        PublishSourceCoverage();
    }

    void PublishSourceCoverage()
    {
        SourceCoverage = _sourceCoverage.Build();
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
                    state.HasReferenceBindingLayer
                        ? nameof(MethodDefinitionLayers.ReferenceBinding)
                        : nameof(MethodDefinitionLayers.ModuleLookup),
                    state.LookupUses));
            }

            if (state.HasMemorySafetyLayer)
            {
                layers.Add(new ProducerLayerParticipation(
                    nameof(MethodDefinitionLayers.MemorySafetyContracts),
                    state.MemorySafetyUses));
            }

            if (state.HasCallTargetLayer)
            {
                layers.Add(new ProducerLayerParticipation(
                    nameof(MethodDefinitionLayers.CallTargets),
                    state.CallTargetUses));
            }

            if (state.HasSemanticIdentityLayer)
            {
                layers.Add(new ProducerLayerParticipation(
                    nameof(MethodDefinitionLayers.SemanticIdentity),
                    state.SemanticIdentityUses));
            }

            if (state.HasPublicMethodRootsLayer)
            {
                layers.Add(new ProducerLayerParticipation(
                    nameof(MethodDefinitionLayers.PublicMethodRoots),
                    state.PublicMethodRootReads));
            }

            if (state.HasReferenceAssemblyStatusLayer)
            {
                layers.Add(new ProducerLayerParticipation(
                    nameof(MethodDefinitionLayers.ReferenceAssemblyStatus),
                    state.ReferenceAssemblyStatusReads));
            }

            if (state.HasCallsLayer)
            {
                layers.Add(new ProducerLayerParticipation(
                    nameof(MethodDefinitionLayers.Calls),
                    state.CallsUses));
            }

            if (state.HasCallOperandsLayer)
            {
                layers.Add(new ProducerLayerParticipation(
                    nameof(MethodDefinitionLayers.CallOperands),
                    state.CallOperandUses));
            }

            if (state.HasLocalSignatureLayer)
            {
                layers.Add(new ProducerLayerParticipation(
                    nameof(MethodDefinitionLayers.LocalSignature),
                    state.LocalSignatureUses));
            }

            if (state.HasMethodAnalysisLayer)
            {
                layers.Add(new ProducerLayerParticipation(
                    nameof(MethodDefinitionLayers.MethodAnalysis),
                    state.MethodAnalysisUses));
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

    struct FusedInstructionShapeVisit(
        ProducerState state,
        MethodBodyBlock body,
        int laneIndex)
    {
        public ProducerState State = state;

        public MethodBodyBlock Body = body;

        public int LaneIndex = laneIndex;

        public int InstructionsVisited;

        public bool IsActive = true;
    }

    readonly struct FusedInstructionShapeDispatch(
        FusedInstructionShapeVisit[] visits,
        int count)
    {
        readonly FusedInstructionShapeVisit[] _visits = visits;
        readonly int _count = count;

        public bool Visit(
            ILOpCode opcode,
            int encodedLength)
        {
            bool anyActive = false;
            for (int i = 0; i < _count; i++)
            {
                ref FusedInstructionShapeVisit visit =
                    ref _visits[i];
                if (!visit.IsActive)
                    continue;

                visit.InstructionsVisited++;
                visit.IsActive =
                    visit.State.VisitInstructionShape(
                        opcode,
                        encodedLength);
                anyActive |= visit.IsActive;
            }
            return anyActive;
        }
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

        public ImmutableArray<MethodBodyAnalyzerDeclaration>
            InstructionAnalyzers =>
                ((IMethodDefinitionProducer)producer)
                    .InstructionAnalyzers;

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

        public virtual bool SupportsFusedInstructionShapes => false;

        public virtual bool TryBeginInstructionShapes(
            scoped MethodDefinitionView view,
            out MethodBodyBlock? body,
            out bool settled) =>
            throw new ProducerContractException(
                $"Producer '{Producer.Identity}' does not support fused "
                + "instruction visitation.");

        public virtual bool VisitInstructionShape(
            ILOpCode opcode,
            int encodedLength) =>
            throw new ProducerContractException(
                $"Producer '{Producer.Identity}' does not support fused "
                + "instruction visitation.");

        public virtual bool CompleteInstructionShapes(
            Exception? failure) =>
            throw new ProducerContractException(
                $"Producer '{Producer.Identity}' does not support fused "
                + "instruction visitation.");

        public abstract void Complete(MethodDefinitionCompletionView completion);

        public abstract void ClearResult();

        /// <summary>The dependency targets, by index into the execution's states.</summary>
        public ImmutableArray<int> Dependencies => dependencies;

        public bool HasDependencies { get; } = !dependencies.IsEmpty;

        /// <summary>Whether the plan keeps this producer's per-unit facts for a reader.</summary>
        public virtual bool RetainsFacts => false;

        /// <summary>
        /// Whether the plan keeps this producer's accumulator and completed
        /// result for a request or result-reading dependency.
        /// </summary>
        public abstract bool RetainsResult { get; }

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
            (layers
                & (MethodDefinitionLayers.ModuleLookup
                    | MethodDefinitionLayers.ReferenceBinding)) != 0;

        public bool HasReferenceBindingLayer { get; } =
            (layers & MethodDefinitionLayers.ReferenceBinding) != 0;

        public bool HasMemorySafetyLayer { get; } =
            (layers & MethodDefinitionLayers.MemorySafetyContracts) != 0;

        public bool HasCallTargetLayer { get; } =
            (layers & MethodDefinitionLayers.CallTargets) != 0;

        public bool HasSemanticIdentityLayer { get; } =
            (layers & MethodDefinitionLayers.SemanticIdentity) != 0;

        public bool HasCallsLayer { get; } =
            (layers & MethodDefinitionLayers.Calls) != 0;

        public bool HasCallOperandsLayer { get; } =
            (layers & MethodDefinitionLayers.CallOperands) != 0;

        public bool HasLocalSignatureLayer { get; } =
            (layers & MethodDefinitionLayers.LocalSignature) != 0;

        public bool SharesCalleeIdentities =>
            ((IMethodDefinitionProducer)producer).SharesCalleeIdentities;

        public bool HasMethodAnalysisLayer { get; } =
            (layers & MethodDefinitionLayers.MethodAnalysis) != 0;

        public bool HasPublicMethodRootsLayer { get; } =
            (layers & MethodDefinitionLayers.PublicMethodRoots) != 0;

        public bool HasReferenceAssemblyStatusLayer { get; } =
            (layers & MethodDefinitionLayers.ReferenceAssemblyStatus) != 0;

        /// <summary>Completion reads of public MethodDef root support.</summary>
        public int PublicMethodRootReads;

        /// <summary>Completion reads of reference-assembly status support.</summary>
        public int ReferenceAssemblyStatusReads;

        public bool IsActive { get; set; } = true;

        public ProducerOutcome? Outcome { get; set; }

        public ProducerFailure? Failure { get; set; }

        public MethodDefinitionSourceFailure? SourceFailure { get; set; }

        /// <summary>Whether the source covers only a bounded expansion's retained prefix.</summary>
        public bool SourceBounded { get; set; }

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

        /// <summary>Units in which memory-safety contract support was queried.</summary>
        public int MemorySafetyUses;

        public int LastMemorySafetyUnit;

        /// <summary>Units in which call-target support was queried.</summary>
        public int CallTargetUses;

        public int LastCallTargetUnit;

        /// <summary>Units whose semantic identity was read.</summary>
        public int SemanticIdentityUses;

        public int LastSemanticIdentityUnit;

        /// <summary>Units whose Method call support was read.</summary>
        public int CallsUses;

        public int LastCallsUnit;

        /// <summary>Units whose resolved call operands were read.</summary>
        public int CallOperandUses;

        public int LastCallOperandsUnit;

        /// <summary>Units whose local-signature support was read.</summary>
        public int LocalSignatureUses;

        public int LastLocalSignatureUnit;

        /// <summary>Units in which method-analysis support was queried.</summary>
        public int MethodAnalysisUses;

        public int LastMethodAnalysisUnit;

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
