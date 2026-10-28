using System.Collections.Immutable;
using System.Reflection.Metadata.Ecma335;

using ILInspector.Instructions;
using ILInspector.Metadata;

namespace ILInspector.Analysis.Planning;

public sealed record LibraryMethodLeverageProducerResult(
    ImmutableArray<MethodIdentity> DeclaredMethods,
    ImmutableArray<MethodIdentity> Methods,
    ImmutableArray<DirectCall> DirectCalls,
    IReadOnlyDictionary<int, MethodIdentity> DeclaredSources,
    IReadOnlySet<int> DeclaredSourceSuppressedMethodTokens,
    ImmutableArray<AnalysisDiagnostic> Diagnostics);

/// <summary>
/// Produces complete whole-library call evidence for method-leverage ranking
/// without acquiring unrelated call-graph analysis families.
/// </summary>
public sealed class LibraryMethodLeverageProducer :
    MethodDefinitionProducer<
        LibraryCallGraphBodyEvidence,
        ImmutableArray<LibraryCallGraphBodyEvidence>.Builder,
        LibraryMethodLeverageProducerResult>
{
    LibraryMethodLeverageProducer()
        : base(
            "Library Method Leverage",
            version: 1,
            tier: 0,
            MethodDefinitionLayers.SemanticIdentity
                | MethodDefinitionLayers.MemorySafetyContracts
                | MethodDefinitionLayers.CallOperands)
    {
    }

    public static LibraryMethodLeverageProducer Instance { get; } = new();

    internal override ImmutableArray<MethodBodyAnalyzerDeclaration>
        InstructionAnalyzers =>
        [
            MethodBodyAnalyzerDeclarations.ResolvedCalls,
        ];

    internal override bool SharesCalleeIdentities => true;

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
            return Empty(
                method: null,
                view.HasManagedBody,
                diagnostics);
        }

        MethodIdentity method =
            ExactMethodAnalysisProducer.ToMethodIdentity(
                semanticIdentity,
                ExactMethodAnalysisProducer.ToCallerUnsafeMode(
                    view.GetMemorySafetyMethodContract(
                        MetadataTokens.MethodDefinitionHandle(token))));
        if (!view.HasManagedBody)
        {
            return Empty(
                method,
                hasManagedBody: false,
                diagnostics);
        }

        ImmutableArray<DirectCall> calls = [];
        try
        {
            MethodInstructions instructions =
                view.MaterializeInstructions(
                    out MethodBodyData body);
            MethodCallOperandSites callSites =
                view.GetCallOperands(instructions);
            if (callSites.Failure is { } callFailure)
            {
                diagnostics.Add(
                    new(
                        token,
                        ExactMethodAnalysisProducer.MethodLabel(method),
                        "Call analysis stopped at "
                            + $"IL_{callFailure.ILOffset:x4}: "
                            + callFailure.Message));
            }
            MethodBodyAnalysisContext context =
                MethodBodyAnalysisContext.Create(
                    method,
                    body,
                    instructions,
                    localTypes: []);
            calls =
                ExactMethodAnalysisProducer.CollectDirectCalls(
                    context,
                    callSites);
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
            HasManagedBody: true,
            calls,
            UnsafeEvidence: [],
            BodySignals: null,
            InAssemblyTypeIsException: null,
            NonHeapNewObjOperandTokens: null,
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

    internal override LibraryMethodLeverageProducerResult Complete(
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

    static LibraryCallGraphBodyEvidence Empty(
        MethodIdentity? method,
        bool hasManagedBody,
        ImmutableArray<AnalysisDiagnostic>.Builder diagnostics) =>
        new(
            method,
            hasManagedBody,
            DirectCalls: [],
            UnsafeEvidence: [],
            BodySignals: null,
            InAssemblyTypeIsException: null,
            NonHeapNewObjOperandTokens: null,
            diagnostics.ToImmutable());
}
