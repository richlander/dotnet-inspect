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
    ImmutableArray<AnalysisDiagnostic> Diagnostics);

public sealed record ExactMethodAnalysisProducerResult(
    ImmutableArray<ExactMethodAnalysisBody> Bodies);

/// <summary>
/// Produces the exact-member facts whose semantic inputs are owned by Method
/// Query Source. Broader body-analysis products remain on their legacy owner.
/// </summary>
public sealed class ExactMethodAnalysisProducer :
    MethodDefinitionProducer<
        ExactMethodAnalysisBody,
        ImmutableArray<ExactMethodAnalysisBody>.Builder,
        ExactMethodAnalysisProducerResult>
{
    public static readonly ExactMethodAnalysisProducer Instance = new();

    ExactMethodAnalysisProducer()
        : base(
            "Exact Method Analysis",
            version: 1,
            tier: 0,
            MethodDefinitionLayers.SemanticIdentity
                | MethodDefinitionLayers.MemorySafetyContracts
                | MethodDefinitionLayers.Calls)
    {
    }

    internal override ImmutableArray<MethodBodyAnalyzerDeclaration>
        InstructionAnalyzers =>
        [
            MethodBodyAnalyzerDeclarations.ResolvedCalls,
        ];

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

        if (!view.HasManagedBody)
        {
            return Empty(
                token,
                method,
                hasManagedBody: false,
                diagnostics);
        }

        try
        {
            MethodBodyBlock body = view.GetBody();
            MethodInstructions instructions =
                view.MaterializeInstructions(out MethodBodyData bodyData);
            MethodLocalSignature localSignature = view.GetLocalSignature();
            MethodBodyAnalysisContext context =
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
            MethodCallSites callSites;
            try
            {
                callSites = view.GetCalls(instructions);
            }
            catch (Exception ex)
                when (LibraryMethodAnalysisRunner
                    .IsRecoverableMethodFailure(ex))
            {
                CollectOperationSafety(
                    method,
                    instructions,
                    includeIndirectOperations,
                    safety);
                MethodCallRecoveryResolver recovery =
                    view.GetCallRecoveryResolver();
                unsafeOccurrences =
                    MethodSafetyAnalysis.CollectOccurrences(
                        context,
                        recovery.CalliReturnDetail,
                        recovery.ResolveMember);
                throw;
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
                diagnostics.ToImmutable());
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
                diagnostics.ToImmutable());
        }
    }

    static void CollectOperationSafety(
        MethodIdentity method,
        MethodInstructions instructions,
        bool includeIndirectOperations,
        ImmutableArray<UnsafeEvidence>.Builder safety)
    {
        foreach (DecodedInstruction instruction in instructions.Instructions)
        {
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

    static MethodIdentity ToMethodIdentity(
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

    static CallerUnsafeMode ToCallerUnsafeMode(
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

    static string MethodLabel(MethodIdentity method) =>
        $"{method.DeclaringType}.{method.Name}";

    sealed class OwnerIssuedCallResolver(MethodCallSites callSites)
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
                : MemberRef.Unsupported(
                    $"Call operand 0x{token:x8} was not resolved.");

        public MemberRef ResolveIndirectCall(int signatureToken) =>
            ResolveMember(signatureToken);

        public int DefinitionToken(int operandToken) =>
            _byToken.TryGetValue(operandToken, out MethodCallSite? site)
                && site.Target
                    is MethodCallTarget.CurrentModule currentModule
                ? MetadataTokens.GetToken(currentModule.Method)
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
