using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using ILInspector.Instructions;
using ILInspector.Metadata;
using ILInspector.MetadataPrimitives;

namespace ILInspector.Analysis.Planning;

public sealed record ExactMethodAnalysisBody(
    int MethodToken,
    MethodIdentity? Method,
    bool HasManagedBody,
    ImmutableArray<UnsafeEvidence> SafetyEvidence,
    ImmutableArray<UnsafetyOccurrence> UnsafeOccurrences,
    ImmutableArray<MethodExceptionRegionInfo> ExceptionRegions,
    string? ExceptionRegionError,
    ImmutableArray<AnalysisDiagnostic> Diagnostics)
{
    public MethodIdentity? DeclaredOwner { get; init; }

    public bool DeclaredOwnerSuppressesOpportunities { get; init; }

    internal MethodBodyAnalysisContext? Context { get; init; }

    internal MethodCallSites? CallSites { get; init; }

    internal IReadOnlyDictionary<int, CallerUnsafeMode> TargetModes
        { get; init; } =
            new Dictionary<int, CallerUnsafeMode>();
}

public sealed record ExactMethodAnalysisProducerResult(
    ImmutableArray<ExactMethodAnalysisBody> Bodies);

/// <summary>
/// Adds exact-target declared-source attribution to reusable physical Method
/// evidence.
/// </summary>
public sealed class ExactMethodAnalysisProducer :
    MethodDefinitionProducer<
        ExactMethodAnalysisBody,
        ImmutableArray<ExactMethodAnalysisBody>.Builder,
        ExactMethodAnalysisProducerResult>
{
    ExactMethodAnalysisProducer()
        : base(
            "Exact Method Analysis",
            version: 1,
            tier: 0,
            MethodDefinitionLayers.MethodAnalysis,
            dependencies: static () =>
            [
                new(
                    PhysicalMethodAnalysisProducer.Instance,
                    ProducerDependencyKind.VisitNeedsVisit),
            ])
    {
    }

    public static ExactMethodAnalysisProducer Instance { get; } = new();

    internal override ExactMethodAnalysisBody Visit(
        scoped MethodDefinitionView view)
    {
        ExactMethodAnalysisBody analysis =
            view.FactOf(PhysicalMethodAnalysisProducer.Instance);
        if (analysis.Method is not { } method)
            return analysis;

        ExactMethodDeclaredSource declaredSource =
            view.ResolveDeclaredSource(method);
        ImmutableArray<AnalysisDiagnostic> diagnostics =
            declaredSource.IsAuthenticated
                ? analysis.Diagnostics
                :
                [
                    new(
                        analysis.MethodToken,
                        MethodLabel(method),
                        declaredSource.Failure
                            ?? "Declared owner resolution failed."),
                    .. analysis.Diagnostics,
                ];
        return analysis with
        {
            Diagnostics = diagnostics,
            DeclaredOwner = declaredSource.Owner,
            DeclaredOwnerSuppressesOpportunities =
                declaredSource.SuppressesOpportunities,
        };
    }

    internal override ImmutableArray<ExactMethodAnalysisBody>.Builder
        Seed() =>
        ImmutableArray.CreateBuilder<ExactMethodAnalysisBody>();

    internal override ImmutableArray<ExactMethodAnalysisBody>.Builder
        Accumulate(
            ImmutableArray<ExactMethodAnalysisBody>.Builder accumulator,
            ExactMethodAnalysisBody fact)
    {
        accumulator.Add(fact);
        return accumulator;
    }

    internal override ExactMethodAnalysisProducerResult Complete(
        ImmutableArray<ExactMethodAnalysisBody>.Builder accumulator,
        MethodDefinitionCompletionView completion) =>
        new(accumulator.ToImmutable());

    internal static MethodIdentity ToMethodIdentity(
        MethodSemanticIdentityResult semanticIdentity,
        CallerUnsafeMode callerUnsafeMode) =>
        PhysicalMethodAnalysisProducer.ToMethodIdentity(
            semanticIdentity,
            callerUnsafeMode);

    internal static CallerUnsafeMode ToCallerUnsafeMode(
        MemorySafetyMethodContract contract) =>
        PhysicalMethodAnalysisProducer.ToCallerUnsafeMode(contract);

    internal static string MethodLabel(MethodIdentity method) =>
        PhysicalMethodAnalysisProducer.MethodLabel(method);

    internal static ImmutableArray<DirectCall> CollectDirectCalls(
        ExactMethodAnalysisBody method,
        MethodAllocationFacts? facts,
        out IReadOnlyDictionary<int, CallReceiverSource> receiverSources) =>
        PhysicalMethodAnalysisProducer.CollectDirectCalls(
            method,
            facts,
            out receiverSources);

    internal static ImmutableArray<DirectCall> CollectDirectCalls(
        MethodBodyAnalysisContext context,
        MethodCallSites callSites) =>
        PhysicalMethodAnalysisProducer.CollectDirectCalls(
            context,
            callSites);
}

/// <summary>
/// Produces reusable physical Method facts whose semantic inputs are owned by
/// Method Query Source. Consumers add their breadth-specific attribution.
/// </summary>
internal sealed class PhysicalMethodAnalysisProducer :
    MethodDefinitionProducer<
        ExactMethodAnalysisBody,
        ImmutableArray<ExactMethodAnalysisBody>.Builder,
        ExactMethodAnalysisProducerResult>
{
    PhysicalMethodAnalysisProducer()
        : base(
            "Physical Method Analysis",
            version: 1,
            tier: 0,
            MethodDefinitionLayers.SemanticIdentity
                | MethodDefinitionLayers.MemorySafetyContracts
                | MethodDefinitionLayers.Calls
                | MethodDefinitionLayers.LocalSignature)
    {
    }

    internal static PhysicalMethodAnalysisProducer Instance { get; } = new();

    internal override ImmutableArray<MethodBodyAnalyzerDeclaration>
        InstructionAnalyzers =>
        [
            MethodBodyAnalyzerDeclarations.ResolvedCalls,
        ];

    // Every body's direct calls stay in the result, so one identity per
    // callee replaces a MemberRef graph per call site.
    internal override bool SharesCalleeIdentities => true;

    internal override ExactMethodAnalysisBody Visit(
        scoped MethodDefinitionView view)
    {
        int token = view.Token;
        ImmutableArray<AnalysisDiagnostic>.Builder diagnostics =
            ImmutableArray.CreateBuilder<AnalysisDiagnostic>();

        MethodSemanticIdentityResult semanticIdentity =
            view.SemanticIdentity;
        if (semanticIdentity is MethodSemanticIdentityResult.Malformed malformed)
        {
            diagnostics.Add(
                new(
                    token,
                    $"0x{token:x8}",
                    malformed.Reason));
            return Empty(
                token,
                method: null,
                hasManagedBody: view.HasManagedBody,
                diagnostics);
        }

        MethodIdentity method =
            ToMethodIdentity(
                semanticIdentity,
                ToCallerUnsafeMode(
                    view.GetMemorySafetyMethodContract(
                        MetadataTokens.MethodDefinitionHandle(token))));
        ImmutableArray<UnsafeEvidence>.Builder safety =
            ImmutableArray.CreateBuilder<UnsafeEvidence>();
        ImmutableArray<UnsafetyOccurrence> unsafeOccurrences = [];
        ImmutableArray<MethodExceptionRegionInfo> exceptionRegions = [];
        string? exceptionRegionError = null;
        MethodDeclarationSafety declarationSafety =
            MethodSafetyAnalysis.InspectDeclaration(
                method,
                safety);
        MethodBodyAnalysisContext? context = null;

        if (!view.HasManagedBody)
        {
            return new(
                token,
                method,
                HasManagedBody: false,
                safety.ToImmutable(),
                UnsafeOccurrences: [],
                ExceptionRegions: [],
                ExceptionRegionError: null,
                diagnostics.ToImmutable());
        }

        try
        {
            MethodBodyBlock body = view.GetBody();
            MethodInstructions instructions =
                view.MaterializeInstructions(out MethodBodyData bodyData);
            MethodLocalSignature localSignature = view.GetLocalSignature();
            context =
                MethodBodyAnalysisContext.Create(
                    method,
                    bodyData,
                    instructions,
                    localSignature.Types,
                    localSignature.DeclaredCount,
                    localSignature.IncompleteReason,
                    body.LocalVariablesInitialized);

            MethodLocalSafety localSafety =
                MethodSafetyAnalysis.InspectLocals(context, safety);
            bool includeIndirectOperations =
                declarationSafety.HasUnsafeApiMember
                || declarationSafety.HasUnsafeSignature
                || localSafety.HasUnsafeLocals;
            (exceptionRegions, exceptionRegionError) =
                ProjectExceptionRegions(
                    bodyData.ExceptionRegionCatalog);
            MethodCallSites callSites =
                view.GetCallsRetainingPrefix(instructions);
            if (callSites.Failure is { } callFailure)
            {
                diagnostics.Add(
                    new(
                        token,
                        MethodLabel(method),
                        "Call analysis stopped at "
                            + $"IL_{callFailure.ILOffset:x4}: "
                            + callFailure.Message));
            }
            var resolver = new OwnerIssuedCallResolver(callSites);
            Dictionary<int, CallerUnsafeMode> targetModes =
                ReadTargetContracts(
                    view,
                    callSites);
            foreach (DecodedInstruction instruction in instructions.Instructions)
            {
                if (resolver.TryGetSite(
                        instruction.Offset,
                        out MethodCallSite? site))
                {
                    MethodCallSite callSite = site!;
                    if (callSite.Kind is CallKind.CallIndirect)
                    {
                        safety.Add(
                            MethodSafetyAnalysis.CallIndirect(
                                method,
                                callSite.ILOffset,
                                callSite.OperandToken));
                    }
                    else if (MethodSafetyAnalysis.InspectCall(
                            method,
                            callSite.Callee,
                            callSite.Kind,
                            callSite.ILOffset,
                            callSite.OperandToken,
                            targetModes.TryGetValue(
                                callSite.ILOffset,
                                out CallerUnsafeMode targetMode)
                                    ? targetMode
                                    : null)
                        is { } callEvidence)
                    {
                        safety.Add(callEvidence);
                    }
                    continue;
                }

                if (MethodSafetyAnalysis.InspectOperation(
                        method,
                        instruction.OpCode,
                        instruction.Offset,
                        includeIndirectOperations)
                    is { } operationEvidence)
                {
                    safety.Add(operationEvidence);
                }
            }

            ImmutableArray<UnsafeEvidence> safetyEvidence =
                safety.ToImmutable();
            unsafeOccurrences =
                MethodSafetyAnalysis.CollectOccurrences(
                    context,
                    resolver.CalliReturnDetail,
                    resolver.ResolveMember);
            return new(
                token,
                method,
                HasManagedBody: true,
                safetyEvidence,
                unsafeOccurrences,
                exceptionRegions,
                exceptionRegionError,
                diagnostics.ToImmutable())
            {
                Context = context,
                CallSites = callSites,
                TargetModes = targetModes,
            };
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
                    MethodLabel(method),
                    ex.Message));
            return new(
                token,
                method,
                HasManagedBody: true,
                safety.ToImmutable(),
                unsafeOccurrences,
                exceptionRegions,
                exceptionRegionError,
                diagnostics.ToImmutable())
            {
                Context = context,
            };
        }
    }

    internal override ImmutableArray<ExactMethodAnalysisBody>.Builder Seed() =>
        ImmutableArray.CreateBuilder<ExactMethodAnalysisBody>();

    internal override ImmutableArray<ExactMethodAnalysisBody>.Builder
        Accumulate(
            ImmutableArray<ExactMethodAnalysisBody>.Builder accumulator,
            ExactMethodAnalysisBody fact)
    {
        accumulator.Add(fact);
        return accumulator;
    }

    internal override ExactMethodAnalysisProducerResult Complete(
        ImmutableArray<ExactMethodAnalysisBody>.Builder accumulator,
        MethodDefinitionCompletionView completion) =>
        new(accumulator.ToImmutable());

    static Dictionary<int, CallerUnsafeMode> ReadTargetContracts(
        MethodDefinitionView view,
        MethodCallSites callSites)
    {
        Dictionary<int, CallerUnsafeMode> targetModes = [];
        Dictionary<MethodDefinitionHandle, CallerUnsafeMode> cachedModes = [];
        foreach (MethodCallSite callSite in callSites.Sites)
        {
            if (callSite.Kind is CallKind.CallIndirect)
                continue;

            CallerUnsafeMode? targetCallerUnsafeMode =
                callSite.Kind is
                    CallKind.Call
                    or CallKind.CallVirtual
                    or CallKind.NewObject
                && callSite.Target
                    is MethodCallTarget.CurrentModule currentModule
                    ? cachedModes.TryGetValue(
                        currentModule.Method,
                        out CallerUnsafeMode cachedMode)
                            ? cachedMode
                            : CacheMode(
                                currentModule.Method,
                                ToCallerUnsafeMode(
                                    view.GetMemorySafetyMethodContract(
                                        currentModule.Method)))
                    : null;
            if (targetCallerUnsafeMode is { } mode)
                targetModes.Add(callSite.ILOffset, mode);
        }

        return targetModes;

        CallerUnsafeMode CacheMode(
            MethodDefinitionHandle method,
            CallerUnsafeMode mode)
        {
            cachedModes.Add(method, mode);
            return mode;
        }
    }

    static (
        ImmutableArray<MethodExceptionRegionInfo> Regions,
        string? Error) ProjectExceptionRegions(
            MethodExceptionRegionCatalog regions)
    {
        ImmutableArray<MethodExceptionRegionInfo>.Builder projected =
            ImmutableArray.CreateBuilder<MethodExceptionRegionInfo>(
                regions.Clauses.Length);

        for (int index = 0; index < regions.Clauses.Length; index++)
        {
            MethodExceptionClause region = regions.Clauses[index];
            string? caughtType = null;
            if (region.CatchType?.Name
                is MetadataTypeNameResult.Resolved resolved)
            {
                caughtType = resolved.Value;
            }
            else if (region.CatchType?.Name
                is MetadataTypeNameResult.Rejected)
            {
                return (
                    [],
                    "Could not resolve exception regions for token "
                    + $"0x{regions.Body.Method.Token:X}.");
            }

            projected.Add(
                new(
                    index + 1,
                    ClauseName(region.Kind),
                    region.ProtectedExtent.Start,
                    region.ProtectedExtent.End,
                    region.HandlerExtent.Start,
                    region.HandlerExtent.End,
                    region.FilterExtent?.Start,
                    region.FilterExtent?.End,
                    caughtType));
        }

        return (projected.ToImmutable(), Error: null);
    }

    static string ClauseName(ExceptionRegionKind kind) =>
        kind switch
        {
            ExceptionRegionKind.Catch => "catch",
            ExceptionRegionKind.Filter => "filter",
            ExceptionRegionKind.Finally => "finally",
            ExceptionRegionKind.Fault => "fault",
            _ => throw new ArgumentOutOfRangeException(
                nameof(kind),
                kind,
                "Unsupported exception region kind."),
        };

    internal static MethodIdentity ToMethodIdentity(
        MethodSemanticIdentityResult semanticIdentity,
        CallerUnsafeMode callerUnsafeMode) =>
        semanticIdentity switch
        {
            MethodSemanticIdentityResult.Decoded decoded =>
                decoded.Identity.ToMethodIdentity(callerUnsafeMode),
            MethodSemanticIdentityResult.SignatureBoundExceeded exceeded =>
                exceeded.Identity.ToMethodIdentity(callerUnsafeMode),
            _ => throw new InvalidOperationException(
                "Malformed semantic identity has no MethodIdentity."),
        };

    internal static CallerUnsafeMode ToCallerUnsafeMode(
        MemorySafetyMethodContract contract) =>
        CallerUnsafeModeFacts.FromContract(contract.Contract);

    static ExactMethodAnalysisBody Empty(
        int token,
        MethodIdentity? method,
        bool hasManagedBody,
        ImmutableArray<AnalysisDiagnostic>.Builder diagnostics) =>
        new(
            token,
            method,
            hasManagedBody,
            [],
            [],
            [],
            ExceptionRegionError: null,
            diagnostics.ToImmutable());

    internal static string MethodLabel(MethodIdentity method) =>
        $"{method.DeclaringType}.{method.Name}";

    internal static ImmutableArray<DirectCall>
        CollectDirectCalls(
            MethodBodyAnalysisContext context,
            MethodCallSites callSites)
    {
        var calls = ImmutableArray.CreateBuilder<DirectCall>();
        try
        {
            MethodCallAnalysis.CollectCompleteDirectCalls(
                context,
                new OwnerIssuedCallResolver(
                    callSites,
                    failOnUnknown: true),
                calls);
        }
        catch (Exception ex)
            when (LibraryMethodAnalysisRunner
                .IsRecoverableMethodFailure(ex))
        {
        }
        return calls.ToImmutable();
    }

    internal static ImmutableArray<DirectCall>
        CollectDirectCalls(
            ExactMethodAnalysisBody method,
            MethodAllocationFacts? facts,
            out IReadOnlyDictionary<
                int,
                CallReceiverSource> receiverSources)
    {
        receiverSources =
            new Dictionary<int, CallReceiverSource>();
        if (method.Context is not { } context
            || method.CallSites is not { } callSites)
        {
            return [];
        }

        var calls =
            ImmutableArray.CreateBuilder<DirectCall>();
        var ignoredSafety =
            ImmutableArray.CreateBuilder<UnsafeEvidence>();
        var privateReceiverSources =
            new Dictionary<int, CallReceiverSource>();
        try
        {
            MethodCallAnalysis.Collect(
                context,
                new OwnerIssuedCallResolver(
                    callSites,
                    failOnUnknown: true),
                offset =>
                    facts?.MultiplicityAt(offset)
                    ?? AllocationMultiplicity.Unknown,
                calls,
                ignoredSafety,
                includeIndirectOpcodes: true,
                includeCallValueFlow: false,
                privateReceiverSources:
                    privateReceiverSources);
        }
        catch (Exception ex)
            when (LibraryMethodAnalysisRunner
                .IsRecoverableMethodFailure(ex))
        {
        }

        Dictionary<int, MethodCallSite> sites =
            callSites.Sites.ToDictionary(
                static site => site.ILOffset);
        MethodIdentity caller =
            method.DeclaredOwner
            ?? method.Method
            ?? context.Method;
        receiverSources = privateReceiverSources;
        return
        [
            .. calls.Select(call =>
            {
                if (!sites.TryGetValue(
                        call.ILOffset,
                        out MethodCallSite? site))
                {
                    return call with
                    {
                        Caller = caller,
                        EvidenceMethod = context.Method,
                    };
                }

                return call with
                {
                    Caller = caller,
                    EvidenceMethod = context.Method,
                    TargetCallerUnsafeMode =
                        method.TargetModes.TryGetValue(
                            call.ILOffset,
                            out CallerUnsafeMode targetMode)
                            ? targetMode
                            : null,
                };
            }),
        ];
    }

    sealed class OwnerIssuedCallResolver(
        MethodCallSites callSites,
        bool failOnUnknown = false)
        : IMethodCallResolver
    {
        readonly Dictionary<int, MethodCallSite> _byOffset =
            callSites.Sites.ToDictionary(
                static site => site.ILOffset);
        readonly Dictionary<int, MethodCallSite> _byToken =
            callSites.Sites
                .GroupBy(static site => site.OperandToken)
                .ToDictionary(
                    static group => group.Key,
                    static group => group.First());

        public MemberRef ResolveMember(int token) =>
            _byToken.TryGetValue(token, out MethodCallSite? site)
                ? site.Callee
                : failOnUnknown
                    ? throw new BadImageFormatException(
                        $"Call operand 0x{token:x8} was not resolved.")
                    : MemberRef.Unsupported(
                        $"Call operand 0x{token:x8} was not resolved.");

        public MemberRef ResolveIndirectCall(int signatureToken) =>
            ResolveMember(signatureToken);

        public int DefinitionToken(int operandToken) =>
            _byToken.TryGetValue(operandToken, out MethodCallSite? site)
                ? site.DefinitionToken
                : operandToken;

        public string? ResolveUserString(int token) => null;

        public string? CalliReturnDetail(int token) =>
            _byToken.TryGetValue(token, out MethodCallSite? site)
                && site.Kind == CallKind.CallIndirect
                    ? site.Callee.ReturnType.ToDisplayString()
                    : null;

        public bool TryGetSite(
            int offset,
            out MethodCallSite? site) =>
            _byOffset.TryGetValue(offset, out site);
    }
}
