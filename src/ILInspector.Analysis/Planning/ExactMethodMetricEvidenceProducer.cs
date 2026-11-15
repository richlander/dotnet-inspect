using System.Collections.Immutable;

using ILInspector.Instructions;

namespace ILInspector.Analysis.Planning;

/// <summary>Encoded managed-body size for one exact physical MethodDef.</summary>
public sealed record ExactMethodBodySize(
    int MethodToken,
    bool HasManagedBody,
    int? EncodedIlBytes,
    AnalysisDiagnostic? Diagnostic);

/// <summary>Body-size evidence retained from one exact Method source.</summary>
public sealed record ExactMethodBodySizeProducerResult(
    ImmutableArray<ExactMethodBodySize> Bodies);

/// <summary>
/// Reads encoded IL length without decoding instructions or acquiring any
/// semantic analysis support.
/// </summary>
public sealed class ExactMethodBodySizeProducer :
    MethodDefinitionProducer<
        ExactMethodBodySize,
        ImmutableArray<ExactMethodBodySize>.Builder,
        ExactMethodBodySizeProducerResult>
{
    ExactMethodBodySizeProducer()
        : base(
            "Exact Method Body Size",
            version: 1,
            tier: 0,
            MethodDefinitionLayers.Body)
    {
    }

    public static ExactMethodBodySizeProducer Instance { get; } = new();

    internal override ExactMethodBodySize Visit(
        scoped MethodDefinitionView view)
    {
        int token = view.Token;
        if (!view.HasManagedBody)
            return new(token, HasManagedBody: false, null, null);

        try
        {
            int encodedIlBytes = view.GetBody().GetILReader().Length;
            return new(
                token,
                HasManagedBody: true,
                encodedIlBytes,
                Diagnostic: null);
        }
        catch (Exception ex)
            when (ex
                    is not
                        MethodDefinitionTerminalWorkLimitExceededException
                && LibraryMethodAnalysisRunner
                    .IsRecoverableMethodFailure(ex))
        {
            return new(
                token,
                HasManagedBody: true,
                EncodedIlBytes: null,
                new(
                    token,
                    $"0x{token:x8}",
                    ex.Message));
        }
    }

    internal override ImmutableArray<ExactMethodBodySize>.Builder Seed() =>
        ImmutableArray.CreateBuilder<ExactMethodBodySize>();

    internal override ImmutableArray<ExactMethodBodySize>.Builder Accumulate(
        ImmutableArray<ExactMethodBodySize>.Builder accumulator,
        ExactMethodBodySize fact)
    {
        accumulator.Add(fact);
        return accumulator;
    }

    internal override ExactMethodBodySizeProducerResult Complete(
        ImmutableArray<ExactMethodBodySize>.Builder accumulator,
        MethodDefinitionCompletionView completion) =>
        new(accumulator.ToImmutable());
}

/// <summary>
/// One invocation from an exact physical MethodDef, retaining the typed target
/// and the peeled MethodDef token used when target classification is
/// unavailable.
/// </summary>
public sealed record ExactMethodSiblingCallSite(
    int ILOffset,
    int OperandToken,
    int DefinitionToken,
    CallKind Kind,
    MethodCallTarget Target);

/// <summary>Sibling-call evidence for one exact physical MethodDef.</summary>
public sealed record ExactMethodSiblingCallBody(
    int MethodToken,
    bool HasManagedBody,
    ImmutableArray<ExactMethodSiblingCallSite> Calls,
    ImmutableArray<AnalysisDiagnostic> Diagnostics);

/// <summary>Sibling-call evidence retained from one exact Method source.</summary>
public sealed record ExactMethodSiblingCallProducerResult(
    ImmutableArray<ExactMethodSiblingCallBody> Bodies);

/// <summary>
/// Decodes only invocation calls and their same-module targets. Relationship
/// scope and generated-body attribution remain with the population consumer.
/// </summary>
public sealed class ExactMethodSiblingCallProducer :
    MethodDefinitionProducer<
        ExactMethodSiblingCallBody,
        ImmutableArray<ExactMethodSiblingCallBody>.Builder,
        ExactMethodSiblingCallProducerResult>
{
    ExactMethodSiblingCallProducer()
        : base(
            "Exact Method Sibling Calls",
            version: 1,
            tier: 0,
            MethodDefinitionLayers.Calls)
    {
    }

    public static ExactMethodSiblingCallProducer Instance { get; } = new();

    internal override ImmutableArray<MethodBodyAnalyzerDeclaration>
        InstructionAnalyzers =>
        [
            MethodBodyAnalyzerDeclarations.ResolvedCalls,
        ];

    internal override bool SharesCalleeIdentities => true;

    internal override ExactMethodSiblingCallBody Visit(
        scoped MethodDefinitionView view)
    {
        int token = view.Token;
        if (!view.HasManagedBody)
        {
            return new(
                token,
                HasManagedBody: false,
                [],
                []);
        }

        var diagnostics =
            ImmutableArray.CreateBuilder<AnalysisDiagnostic>();
        ImmutableArray<ExactMethodSiblingCallSite> calls = [];
        try
        {
            MethodInstructions instructions =
                view.MaterializeInstructions(out _);
            MethodCallSites callSites =
                view.GetCallsRetainingPrefix(instructions);
            calls =
            [
                .. callSites.Sites
                    .Where(static site => IsInvocation(site.Kind))
                    .Select(static site =>
                        new ExactMethodSiblingCallSite(
                            site.ILOffset,
                            site.OperandToken,
                            site.DefinitionToken,
                            site.Kind,
                            site.Target)),
            ];
            if (callSites.Failure is { } failure)
            {
                diagnostics.Add(
                    new(
                        token,
                        $"0x{token:x8}",
                        "Call analysis stopped at "
                            + $"IL_{failure.ILOffset:x4}: "
                            + failure.Message));
            }
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
                    $"0x{token:x8}",
                    ex.Message));
        }

        return new(
            token,
            HasManagedBody: true,
            calls,
            diagnostics.ToImmutable());
    }

    internal override ImmutableArray<ExactMethodSiblingCallBody>.Builder
        Seed() =>
        ImmutableArray.CreateBuilder<ExactMethodSiblingCallBody>();

    internal override ImmutableArray<ExactMethodSiblingCallBody>.Builder
        Accumulate(
            ImmutableArray<ExactMethodSiblingCallBody>.Builder accumulator,
            ExactMethodSiblingCallBody fact)
    {
        accumulator.Add(fact);
        return accumulator;
    }

    internal override ExactMethodSiblingCallProducerResult Complete(
        ImmutableArray<ExactMethodSiblingCallBody>.Builder accumulator,
        MethodDefinitionCompletionView completion) =>
        new(accumulator.ToImmutable());

    static bool IsInvocation(CallKind kind) =>
        kind is CallKind.Call
            or CallKind.CallVirtual
            or CallKind.NewObject;
}
