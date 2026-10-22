using System.Collections.Immutable;
using System.Reflection.Metadata.Ecma335;

namespace ILInspector.Analysis.Planning;

/// <summary>
/// Metadata-only identity outcome for one MethodDef selected by Method Query
/// Source.
/// </summary>
public sealed record MethodIdentityBody(
    int MethodToken,
    MethodIdentity? Method,
    string? Limitation);

public sealed record MethodIdentityProducerResult(
    ImmutableArray<MethodIdentityBody> Methods);

/// <summary>
/// Produces exact MethodDef identities without acquiring method bodies.
/// </summary>
public sealed class MethodIdentityProducer :
    MethodDefinitionProducer<
        MethodIdentityBody,
        ImmutableArray<MethodIdentityBody>.Builder,
        MethodIdentityProducerResult>
{
    MethodIdentityProducer()
        : base(
            "Method Identity",
            version: 1,
            tier: 0,
            MethodDefinitionLayers.SemanticIdentity
                | MethodDefinitionLayers.MemorySafetyContracts)
    {
    }

    public static MethodIdentityProducer Instance { get; } = new();

    internal override MethodIdentityBody Visit(
        scoped MethodDefinitionView view)
    {
        int token = view.Token;
        MethodSemanticIdentityResult semanticIdentity =
            view.SemanticIdentity;
        if (semanticIdentity is MethodSemanticIdentityResult.Malformed malformed)
            return new(token, null, malformed.Reason);

        MethodIdentity method =
            ExactMethodAnalysisProducer.ToMethodIdentity(
                semanticIdentity,
                ExactMethodAnalysisProducer.ToCallerUnsafeMode(
                    view.GetMemorySafetyMethodContract(
                        MetadataTokens.MethodDefinitionHandle(token))));
        return new(token, method, null);
    }

    internal override ImmutableArray<MethodIdentityBody>.Builder Seed() =>
        ImmutableArray.CreateBuilder<MethodIdentityBody>();

    internal override ImmutableArray<MethodIdentityBody>.Builder Accumulate(
        ImmutableArray<MethodIdentityBody>.Builder accumulator,
        MethodIdentityBody fact)
    {
        accumulator.Add(fact);
        return accumulator;
    }

    internal override MethodIdentityProducerResult Complete(
        ImmutableArray<MethodIdentityBody>.Builder accumulator,
        MethodDefinitionCompletionView completion) =>
        new(accumulator.ToImmutable());
}

/// <summary>
/// Physical identity of one direct-call instruction visited through exact
/// Method-source breadth.
/// </summary>
public sealed record FocusedMethodCallSiteIdentity
{
    internal FocusedMethodCallSiteIdentity(
        Guid moduleVersionId,
        int methodToken,
        int ilOffset,
        int operandToken,
        CallKind callKind)
    {
        ModuleVersionId = moduleVersionId;
        MethodToken = methodToken;
        ILOffset = ilOffset;
        OperandToken = operandToken;
        CallKind = callKind;
    }

    public Guid ModuleVersionId { get; }
    public int MethodToken { get; }
    public int ILOffset { get; }
    public int OperandToken { get; }
    public CallKind CallKind { get; }
}

/// <summary>
/// One focused physical call and its Analysis-owned target classification.
/// </summary>
public sealed record FocusedMethodCallSite(
    FocusedMethodCallSiteIdentity Identity,
    DirectCall Call,
    MethodCallTarget Target);

public sealed record FocusedMethodCallBody(
    int MethodToken,
    MethodIdentity? Method,
    bool HasManagedBody,
    ImmutableArray<FocusedMethodCallSite> Calls,
    ImmutableArray<AnalysisDiagnostic> Diagnostics);

public sealed record FocusedMethodCallProducerResult(
    ImmutableArray<FocusedMethodCallBody> Bodies);

/// <summary>
/// Retains only direct-call evidence from focused exact-method analysis.
/// </summary>
public sealed class FocusedMethodCallProducer :
    MethodDefinitionProducer<
        FocusedMethodCallBody,
        ImmutableArray<FocusedMethodCallBody>.Builder,
        FocusedMethodCallProducerResult>
{
    FocusedMethodCallProducer()
        : base(
            "Focused Method Calls",
            version: 1,
            tier: 0,
            layers: 0,
            dependencies: static () =>
            [
                new(
                    ExactMethodAnalysisProducer.Instance,
                    ProducerDependencyKind.VisitNeedsVisit),
            ])
    {
    }

    public static FocusedMethodCallProducer Instance { get; } = new();

    internal override FocusedMethodCallBody Visit(
        scoped MethodDefinitionView view)
    {
        ExactMethodAnalysisBody method =
            view.FactOf(ExactMethodAnalysisProducer.Instance);
        ImmutableArray<DirectCall> calls =
            ExactMethodAnalysisProducer.CollectDirectCalls(
                method,
                facts: null,
                out _);
        if (method.CallSites is not { } callSites)
        {
            return new(
                method.MethodToken,
                method.Method,
                method.HasManagedBody,
                [],
                method.Diagnostics);
        }

        Dictionary<int, MethodCallSite> sites =
            callSites.Sites.ToDictionary(static site => site.ILOffset);
        ImmutableArray<FocusedMethodCallSite> focused =
        [
            .. calls.Select(call =>
            {
                MethodCallSite site = sites[call.ILOffset];
                return new FocusedMethodCallSite(
                    new(
                        call.EvidenceMethod.ModuleVersionId,
                        call.EvidenceMethod.MetadataToken,
                        call.ILOffset,
                        call.OperandToken,
                        call.Kind),
                    call,
                    site.Target);
            }),
        ];
        return new(
            method.MethodToken,
            method.Method,
            method.HasManagedBody,
            focused,
            method.Diagnostics);
    }

    internal override ImmutableArray<FocusedMethodCallBody>.Builder Seed() =>
        ImmutableArray.CreateBuilder<FocusedMethodCallBody>();

    internal override ImmutableArray<FocusedMethodCallBody>.Builder Accumulate(
        ImmutableArray<FocusedMethodCallBody>.Builder accumulator,
        FocusedMethodCallBody fact)
    {
        accumulator.Add(fact);
        return accumulator;
    }

    internal override FocusedMethodCallProducerResult Complete(
        ImmutableArray<FocusedMethodCallBody>.Builder accumulator,
        MethodDefinitionCompletionView completion) =>
        new(accumulator.ToImmutable());
}
