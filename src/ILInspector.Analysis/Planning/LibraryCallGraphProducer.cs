using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

using ILInspector.Instructions;
using ILInspector.Metadata;

namespace ILInspector.Analysis.Planning;

public sealed record LibraryCallGraphProducerResult(
    ImmutableArray<MethodIdentity> DeclaredMethods,
    ImmutableArray<MethodIdentity> Methods,
    ImmutableArray<DirectCall> DirectCalls,
    ImmutableArray<UnsafeEvidence> UnsafeEvidence,
    IReadOnlyDictionary<int, BodySignals> BodySignals,
    IReadOnlyDictionary<(string Namespace, string Name), bool>
        InAssemblyTypeIsException,
    IReadOnlySet<int> NonHeapNewObjOperandTokens,
    IReadOnlyDictionary<int, MethodIdentity> DeclaredSources,
    IReadOnlySet<int> DeclaredSourceSuppressedMethodTokens,
    ImmutableArray<AnalysisDiagnostic> Diagnostics);

public sealed record LibraryRootPathCallGraphProducerResult(
    ImmutableArray<MethodIdentity> DeclaredMethods,
    ImmutableArray<MethodIdentity> Methods,
    ImmutableArray<DirectCall> DirectCalls,
    IReadOnlyDictionary<int, MethodIdentity> DeclaredSources,
    IReadOnlySet<int> DeclaredSourceSuppressedMethodTokens,
    ImmutableArray<AnalysisDiagnostic> Diagnostics);

public readonly record struct LibraryCallGraphBodyEvidence(
    MethodIdentity? Method,
    bool HasManagedBody,
    ImmutableArray<DirectCall> DirectCalls,
    ImmutableArray<UnsafeEvidence> UnsafeEvidence,
    BodySignals? BodySignals,
    IReadOnlyDictionary<(string Namespace, string Name), bool>?
        InAssemblyTypeIsException,
    IReadOnlySet<int>? NonHeapNewObjOperandTokens,
    MethodIdentity? DeclaredSource,
    bool DeclaredSourceSuppressesOpportunities,
    ImmutableArray<AnalysisDiagnostic> Diagnostics);

public sealed class LibraryCallGraphProducer :
    MethodDefinitionProducer<
        LibraryCallGraphBodyEvidence,
        ImmutableArray<LibraryCallGraphBodyEvidence>.Builder,
        LibraryCallGraphProducerResult>
{
    LibraryCallGraphProducer()
        : base(
            "Library Call Graph",
            version: 1,
            tier: 0,
            MethodDefinitionLayers.ModuleLookup
                | MethodDefinitionLayers.SemanticIdentity
                | MethodDefinitionLayers.MemorySafetyContracts
                | MethodDefinitionLayers.Calls
                | MethodDefinitionLayers.LocalSignature
                | MethodDefinitionLayers.MethodAnalysis)
    {
    }

    static LibraryBodyAnalysisPlan MethodEvidencePlan { get; } =
        LibraryBodyAnalysisPlan.Create(
            LibraryBodyAnalysisFeatures.MethodEvidence,
            methodScope: null,
            typeScope: null);

    /// <summary>
    /// Produces the complete local-call evidence required by bounded root-path
    /// search without acquiring unrelated call-graph analysis families.
    /// </summary>
    public sealed class LibraryRootPathCallGraphProducer :
        MethodDefinitionProducer<
            LibraryCallGraphBodyEvidence,
            ImmutableArray<LibraryCallGraphBodyEvidence>.Builder,
            LibraryRootPathCallGraphProducerResult>
    {
        LibraryRootPathCallGraphProducer()
            : base(
                "Library Root Path Call Graph",
                version: 1,
                tier: 0,
                MethodDefinitionLayers.SemanticIdentity
                    | MethodDefinitionLayers.MemorySafetyContracts
                    | MethodDefinitionLayers.CallOperands)
        {
        }

        public static LibraryRootPathCallGraphProducer Instance { get; } =
            new();

        internal override ImmutableArray<MethodBodyAnalyzerDeclaration>
            InstructionAnalyzers =>
            [
                MethodBodyAnalyzerDeclarations.ResolvedCalls,
            ];

        internal override LibraryCallGraphBodyEvidence Visit(
            scoped MethodDefinitionView view)
        {
            int token = view.Token;
            var diagnostics =
                ImmutableArray.CreateBuilder<AnalysisDiagnostic>();
            MethodSemanticIdentityResult semanticIdentity =
                view.SemanticIdentity;
            if (semanticIdentity
                is MethodSemanticIdentityResult.Malformed malformed)
            {
                diagnostics.Add(
                    new(
                        token,
                        $"0x{token:x8}",
                        malformed.Reason));
                return new(
                    null,
                    view.HasManagedBody,
                    [],
                    [],
                    null,
                    null,
                    null,
                    null,
                    false,
                    diagnostics.ToImmutable());
            }

            MethodIdentity method =
                ExactMethodAnalysisProducer.ToMethodIdentity(
                    semanticIdentity,
                    ExactMethodAnalysisProducer.ToCallerUnsafeMode(
                        view.GetMemorySafetyMethodContract(
                            MetadataTokens.MethodDefinitionHandle(token))));
            if (!view.HasManagedBody)
            {
                return new(
                    method,
                    false,
                    [],
                    [],
                    null,
                    null,
                    null,
                    null,
                    false,
                    diagnostics.ToImmutable());
            }

            ImmutableArray<DirectCall> directCalls = [];
            try
            {
                MethodInstructions instructions =
                    view.MaterializeInstructions(
                        out _);
                MethodCallOperandSites operands =
                    view.GetCallOperands(instructions);
                if (operands.Failure is { } operandFailure)
                {
                    diagnostics.Add(
                        new(
                            token,
                            ExactMethodAnalysisProducer.MethodLabel(method),
                            "Call analysis stopped at "
                                + $"IL_{operandFailure.ILOffset:x4}: "
                                + operandFailure.Message));
                }

                var calls =
                    ImmutableArray.CreateBuilder<DirectCall>(
                        operands.Sites.Length);
                foreach (MethodCallOperandSite operand in operands.Sites)
                {
                    calls.Add(
                        new(
                            method,
                            operand.Callee,
                            operand.ILOffset,
                            operand.OperandToken,
                            operand.DefinitionToken,
                            operand.Kind,
                            InLoop: false)
                        {
                            EvidenceMethod = method,
                            Opcode = operand.Kind switch
                            {
                                CallKind.CallVirtual => "callvirt",
                                CallKind.NewObject => "newobj",
                                CallKind.LoadFunction => "ldftn",
                                CallKind.LoadVirtualFunction =>
                                    "ldvirtftn",
                                CallKind.CallIndirect => "calli",
                                _ => "call",
                            },
                            ReturnAddress = operand.ReturnAddress,
                        });
                }
                directCalls = calls.MoveToImmutable();
            }
            catch (Exception ex)
                when (ex
                        is not
                            MethodDefinitionTerminalWorkLimitExceededException
                    && LibraryMethodAnalysisRunner
                        .IsRecoverableMethodFailure(ex))
            {
                diagnostics.Add(
                    new(
                        token,
                        ExactMethodAnalysisProducer.MethodLabel(method),
                        ex.Message));
            }

            return new(
                method,
                true,
                directCalls,
                [],
                null,
                null,
                null,
                null,
                false,
                diagnostics.ToImmutable());
        }

        internal override ImmutableArray<LibraryCallGraphBodyEvidence>.Builder
            Seed() =>
            ImmutableArray.CreateBuilder<LibraryCallGraphBodyEvidence>();

        internal override ImmutableArray<LibraryCallGraphBodyEvidence>.Builder
            Accumulate(
                ImmutableArray<LibraryCallGraphBodyEvidence>.Builder accumulator,
                LibraryCallGraphBodyEvidence fact)
        {
            accumulator.Add(fact);
            return accumulator;
        }

        internal override LibraryRootPathCallGraphProducerResult Complete(
            ImmutableArray<LibraryCallGraphBodyEvidence>.Builder accumulator,
            MethodDefinitionCompletionView completion)
        {
            LibraryCallGraphProducerResult result =
                LibraryCallGraphProducer.CompleteEvidence(
                accumulator,
                completion,
                includeSupplementalEvidence: false);
            return new(
                result.DeclaredMethods,
                result.Methods,
                result.DirectCalls,
                result.DeclaredSources,
                result.DeclaredSourceSuppressedMethodTokens,
                result.Diagnostics);
        }
    }

    public static LibraryCallGraphProducer Instance { get; } = new();

    internal override ImmutableArray<MethodBodyAnalyzerDeclaration>
        InstructionAnalyzers =>
        [
            MethodBodyAnalyzerDeclarations.ResolvedCalls,
        ];

    internal override LibraryCallGraphBodyEvidence Visit(
        scoped MethodDefinitionView view)
    {
        ExactMethodAnalysisBody method =
            PhysicalMethodAnalysisProducer.Instance.Visit(view);
        ImmutableArray<DirectCall> directCalls = [];
        BodySignals? bodySignals = null;
        Dictionary<(string Namespace, string Name), bool>?
            exceptionTypes = null;
        HashSet<int>? nonHeapNewObjectTokens = null;
        MethodIdentity? declaredSource = null;
        bool declaredSourceSuppressesOpportunities = false;

        if (!view.HasDeclaredSourceRelation
            && method.Method is { } identity)
        {
            ExactMethodDeclaredSource source =
                view.ResolveDeclaredSource(identity);
            if (source.IsAuthenticated)
            {
                declaredSource = source.Owner;
                declaredSourceSuppressesOpportunities =
                    source.SuppressesOpportunities;
            }
        }

        if (method.Method is { } analyzedMethod
            && method.Context is { } context)
        {
            MethodAllocationFacts facts =
                MethodAllocationFacts.Create(context);
            directCalls =
                ExactMethodAnalysisProducer.CollectDirectCalls(
                    method,
                    facts,
                    out _);
            using MethodAnalysisQuery query =
                view.BeginMethodAnalysis(
                    analyzedMethod,
                    context.Instructions);
            IMethodAllocationResolver resolver = query.Resolver;
            bodySignals = BodySignalAnalysis.Collect(
                context,
                operandToken =>
                    resolver.IsAllocatingValueTypeBox(
                        operandToken,
                        resolver.ResolveType(operandToken)));
            foreach (DirectCall call in directCalls)
            {
                if (call.Kind != CallKind.NewObject)
                    continue;
                if (resolver.ClassifyConstruction(
                        call.OperandToken,
                        call.Callee.DeclaringType)
                    != NewObjectConstructionKind.Heap)
                {
                    (nonHeapNewObjectTokens ??= []).Add(
                        call.OperandToken);
                }
                if (resolver.IsInAssemblyExceptionType(
                        call.OperandToken,
                        call.Callee.DeclaringType)
                    is bool isException)
                {
                    TypeRef definition =
                        call.Callee.DeclaringType.Kind
                            == TypeRefKind.GenericInstance
                        ? call.Callee.DeclaringType.ElementType
                            ?? call.Callee.DeclaringType
                        : call.Callee.DeclaringType;
                    (exceptionTypes ??= []).TryAdd(
                        (definition.Namespace, definition.Name),
                        isException);
                }
            }
        }

        return new(
            method.Method,
            method.HasManagedBody,
            directCalls,
            method.SafetyEvidence,
            HasSignals(bodySignals)
                ? bodySignals
                : null,
            exceptionTypes,
            nonHeapNewObjectTokens,
            declaredSource,
            declaredSourceSuppressesOpportunities,
            method.Diagnostics);
    }

    internal override bool RunKernel(
        MethodDefinitionExecution.ProducerState state,
        MetadataReader reader,
        PEReader peReader,
        LibraryMethodAnalysisRunner? lookup,
        MethodRowGate gate,
        out int unitsVisited)
    {
        unitsVisited = 0;
        if (state.Terminal != ProducerTerminal.Rows
            || !state.Execution.HasUnboundedTerminalWork
            || reader.MethodDefinitions.Count
                < LibraryMethodAnalysisRunner.ParallelMethodThreshold
            || lookup is null)
        {
            return false;
        }

        var workItems = new List<MethodEvidenceWorkItem>(
            reader.MethodDefinitions.Count);
        try
        {
            foreach (TypeDefinitionHandle typeHandle
                in reader.TypeDefinitions)
            {
                TypeDefinition typeDefinition =
                    reader.GetTypeDefinition(typeHandle);
                bool typeSourceGenerated =
                    lookup.IsSourceGeneratedTypeOrEnclosing(
                        typeHandle);
                foreach (MethodDefinitionHandle methodHandle
                    in typeDefinition.GetMethods())
                {
                    workItems.Add(
                        new(
                            typeHandle,
                            typeDefinition,
                            typeSourceGenerated,
                            methodHandle));
                }
            }
        }
        catch (Exception ex)
            when (LibraryMethodAnalysisRunner
                .IsRecoverableMethodFailure(ex))
        {
            MethodDefinitionExecution.FailSource(
                state,
                0,
                "(method source)",
                ex);
            return true;
        }

        lookup.PrepareParallelMethodEvidence();
        var results =
            new LibraryMethodAnalysisResult[workItems.Count];
        Parallel.For(
            0,
            workItems.Count,
            index =>
            {
                MethodEvidenceWorkItem item = workItems[index];
                results[index] =
                    lookup.Analyze(
                        item.TypeHandle,
                        item.TypeDefinition,
                        item.TypeSourceGenerated,
                        item.MethodHandle,
                        MethodEvidencePlan);
            });

        var accumulator =
            ImmutableArray.CreateBuilder<LibraryCallGraphBodyEvidence>(
                results.Length);
        MethodDefinitionSourceCoverageBuilder coverage =
            state.Execution.SourceCoverageBuilder;
        int bodyAcquisitions = 0;
        for (int index = 0; index < results.Length; index++)
        {
            MethodEvidenceWorkItem item = workItems[index];
            LibraryMethodAnalysisResult result = results[index];
            coverage.RecordDefinitionExamined(item.MethodHandle);
            coverage.RecordMethodSelected(item.MethodHandle);
            coverage.RecordModuleLookupUsed(item.MethodHandle);
            if (result.BodyAcquired)
            {
                bodyAcquisitions++;
                coverage.RecordTerminalBodyAttempted(
                    item.MethodHandle);
                coverage.RecordBodyAttempted(item.MethodHandle);
                coverage.RecordBodyAcquired(item.MethodHandle);
                coverage.RecordTerminalBodyAcquired(
                    item.MethodHandle,
                    result.EncodedIlBytes);
            }
            accumulator.Add(Project(result, lookup));
        }

        state.UnitsAttempted += results.Length;
        state.UnitsCompleted += results.Length;
        state.BodyAcquisitions += bodyAcquisitions;
        state.LookupUses += results.Length;
        ((MethodDefinitionProducer<
            LibraryCallGraphBodyEvidence,
            ImmutableArray<LibraryCallGraphBodyEvidence>.Builder,
            LibraryCallGraphProducerResult>.State)state)
            .SetAccumulator(accumulator);
        unitsVisited = results.Length;
        return true;
    }

    static LibraryCallGraphBodyEvidence Project(
        LibraryMethodAnalysisResult result,
        LibraryMethodAnalysisRunner lookup)
    {
        ImmutableArray<DirectCall> directCalls =
            result.Calls.IsDefault ? [] : result.Calls;
        BodySignals? bodySignals =
            result.HasSignals ? result.Signals : null;
        Dictionary<(string Namespace, string Name), bool>?
            exceptionTypes = null;
        HashSet<int>? nonHeapNewObjectTokens = null;

        foreach (DirectCall call in directCalls)
        {
            if (call.Kind != CallKind.NewObject)
                continue;
            if (lookup.IsNonHeapNewObj(
                    call.OperandToken,
                    call.Callee.DeclaringType))
            {
                (nonHeapNewObjectTokens ??= []).Add(
                    call.OperandToken);
            }
            if (lookup.IsInAssemblyExceptionType(
                    call.OperandToken,
                    call.Callee.DeclaringType)
                is bool isException)
            {
                TypeRef definition =
                    call.Callee.DeclaringType.Kind
                        == TypeRefKind.GenericInstance
                    ? call.Callee.DeclaringType.ElementType
                        ?? call.Callee.DeclaringType
                    : call.Callee.DeclaringType;
                (exceptionTypes ??= []).TryAdd(
                    (definition.Namespace, definition.Name),
                    isException);
            }
        }

        return new(
            result.Caller,
            result.HasBody,
            directCalls,
            result.UnsafeEvidence.IsDefault
                ? []
                : result.UnsafeEvidence,
            bodySignals,
            exceptionTypes,
            nonHeapNewObjectTokens,
            result.DeclaredSource,
            result.DeclaredSourceSuppressesOpportunities,
            result.Diagnostic is null
                ? []
                : [result.Diagnostic]);
    }

    internal override ImmutableArray<LibraryCallGraphBodyEvidence>.Builder
        Seed() =>
        ImmutableArray.CreateBuilder<LibraryCallGraphBodyEvidence>();

    internal override ImmutableArray<LibraryCallGraphBodyEvidence>.Builder
        Accumulate(
            ImmutableArray<LibraryCallGraphBodyEvidence>.Builder accumulator,
            LibraryCallGraphBodyEvidence fact)
    {
        accumulator.Add(fact);
        return accumulator;
    }

    internal override LibraryCallGraphProducerResult Complete(
        ImmutableArray<LibraryCallGraphBodyEvidence>.Builder accumulator,
        MethodDefinitionCompletionView completion) =>
        CompleteEvidence(accumulator, completion);

    internal static LibraryCallGraphProducerResult CompleteEvidence(
        ImmutableArray<LibraryCallGraphBodyEvidence>.Builder accumulator,
        MethodDefinitionCompletionView completion,
        bool includeSupplementalEvidence = true)
    {
        var bodySignals = new Dictionary<int, BodySignals>();
        var exceptionTypes =
            new Dictionary<(string Namespace, string Name), bool>();
        var nonHeapNewObjectTokens = new HashSet<int>();
        var declaredSources = new Dictionary<int, MethodIdentity>();
        var declaredSourceSuppressedMethodTokens = new HashSet<int>();
        var methodsByToken = new Dictionary<int, MethodIdentity>();
        var declaredMethods =
            ImmutableArray.CreateBuilder<MethodIdentity>();
        var methods = ImmutableArray.CreateBuilder<MethodIdentity>();
        var calls = ImmutableArray.CreateBuilder<DirectCall>();
        var unsafeEvidence =
            ImmutableArray.CreateBuilder<UnsafeEvidence>();
        var diagnostics =
            ImmutableArray.CreateBuilder<AnalysisDiagnostic>();

        for (int index = 0; index < accumulator.Count; index++)
        {
            if (accumulator[index].Method is { } method)
                methodsByToken.Add(method.MetadataToken, method);
        }
        MethodDefinitionMap methodMap =
            MethodDefinitionMap.Create(
                [.. methodsByToken.Values]);

        for (int index = 0; index < accumulator.Count; index++)
        {
            LibraryCallGraphBodyEvidence body = accumulator[index];
            accumulator[index] = default;
            diagnostics.AddRange(body.Diagnostics);
            if (body.Method is not { } method)
                continue;
            declaredMethods.Add(method);
            if (body.HasManagedBody)
                methods.Add(method);
            if (includeSupplementalEvidence
                && body.BodySignals is { } signals)
                bodySignals.Add(method.MetadataToken, signals);
            if (includeSupplementalEvidence
                && body.InAssemblyTypeIsException is { } bodyExceptionTypes)
            {
                foreach (KeyValuePair<
                    (string Namespace, string Name),
                    bool> classification in bodyExceptionTypes)
                {
                    exceptionTypes.TryAdd(
                        classification.Key,
                        classification.Value);
                }
            }

            if (includeSupplementalEvidence
                && body.NonHeapNewObjOperandTokens is { } bodyNonHeapTokens)
                nonHeapNewObjectTokens.UnionWith(bodyNonHeapTokens);
            MethodIdentity? owner = null;
            bool ownerSuppressesOpportunities = false;
            if (completion.HasDeclaredSources
                && completion.DeclaredSourceOf(
                         MetadataTokens.MethodDefinitionHandle(
                             method.MetadataToken))
                     is MethodDefinitionDeclaredSource.Generated generated
                && methodsByToken.TryGetValue(
                    MetadataTokens.GetToken(
                        generated.Origin.DeclaredOwner),
                    out MethodIdentity? declaredOwner)
                && (generated.Origin.Kind
                        == MethodDefinitionGeneratedExpansionOriginKind
                            .LiftedExecutionBody
                    || generated.Origin.IsAsyncStateMachine))
            {
                owner = declaredOwner;
                ownerSuppressesOpportunities =
                    generated.Origin
                        .DeclaredOwnerSuppressesOpportunities;
            }
            else if (body.DeclaredSource is { } recordedOwner)
            {
                owner = recordedOwner;
                ownerSuppressesOpportunities =
                    body.DeclaredSourceSuppressesOpportunities;
            }
            ImmutableArray<DirectCall> normalizedCalls =
            [
                .. body.DirectCalls.Select(call =>
                    NormalizeSameImageCallContract(
                        call,
                        methodMap,
                        methodsByToken)),
            ];
            foreach (DirectCall call in normalizedCalls)
            {
                calls.Add(
                    owner is null
                        ? call
                        : call with
                        {
                            Caller = owner,
                        });
            }
            if (includeSupplementalEvidence)
            {
                unsafeEvidence.AddRange(
                    ReconcileCallSafetyEvidence(
                        body.UnsafeEvidence,
                        normalizedCalls));
            }
            if (owner is not null)
            {
                declaredSources.Add(
                    method.MetadataToken,
                    owner);
                if (ownerSuppressesOpportunities)
                {
                    declaredSourceSuppressedMethodTokens.Add(
                        method.MetadataToken);
                }
            }
        }

        return new(
            declaredMethods.ToImmutable(),
            methods.ToImmutable(),
            calls.ToImmutable(),
            unsafeEvidence.ToImmutable(),
            bodySignals,
            exceptionTypes,
            nonHeapNewObjectTokens,
            declaredSources,
            declaredSourceSuppressedMethodTokens,
            diagnostics.ToImmutable());
    }

    static bool HasSignals(BodySignals? signals) =>
        signals is
        {
            Newarr: > 0,
        }
        or
        {
            Throws: > 0,
        }
        or
        {
            Catches: > 0,
        }
        or
        {
            Finallys: > 0,
        }
        or
        {
            Boxes: > 0,
        };

    static ImmutableArray<UnsafeEvidence> ReconcileCallSafetyEvidence(
        ImmutableArray<UnsafeEvidence> evidence,
        ImmutableArray<DirectCall> calls)
    {
        if (calls.IsDefaultOrEmpty)
            return evidence;

        IEnumerable<UnsafeEvidence> nonCallEvidence =
            evidence.IsDefaultOrEmpty
                ? []
                : evidence.Where(
                    static item => item.Reason != "Unsafe call");
        IEnumerable<UnsafeEvidence> callEvidence =
            calls.Where(
                    static call =>
                        call.Kind != CallKind.CallIndirect)
                .Select(call =>
                    MethodSafetyAnalysis.InspectCall(
                        call.Caller,
                        call.Callee,
                        call.Kind,
                        call.ILOffset,
                        call.OperandToken,
                        call.TargetCallerUnsafeMode))
                .OfType<UnsafeEvidence>();

        return
        [
            .. nonCallEvidence
                .Concat(callEvidence)
                .OrderBy(static item =>
                    item.ILOffset ?? int.MinValue),
        ];
    }

    static DirectCall NormalizeSameImageCallContract(
        DirectCall call,
        MethodDefinitionMap methodMap,
        IReadOnlyDictionary<int, MethodIdentity> methodsByToken)
    {
        if (call.Kind is not (
                CallKind.Call
                or CallKind.CallVirtual
                or CallKind.NewObject))
        {
            return call;
        }

        int targetToken = methodMap.Resolve(call);
        if (targetToken == 0
            || !methodsByToken.TryGetValue(
                targetToken,
                out MethodIdentity? target))
        {
            return call;
        }

        return call with
        {
            TargetCallerUnsafeMode =
                target.CallerUnsafeMode,
        };
    }

    readonly record struct MethodEvidenceWorkItem(
        TypeDefinitionHandle TypeHandle,
        TypeDefinition TypeDefinition,
        bool TypeSourceGenerated,
        MethodDefinitionHandle MethodHandle);

}
