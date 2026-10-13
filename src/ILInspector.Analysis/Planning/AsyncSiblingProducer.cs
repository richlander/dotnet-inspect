using System.Collections.Immutable;

using ILInspector.Metadata;

namespace ILInspector.Analysis.Planning;

/// <summary>How an async sibling relates to the synchronous call it replaces.</summary>
public enum AsyncSiblingPairKind
{
    /// <summary>A <c>Foo</c> call with a <c>FooAsync</c> sibling.</summary>
    Operation,

    /// <summary>A <c>Dispose</c> call with a <c>DisposeAsync</c> sibling.</summary>
    Dispose,
}

/// <summary>
/// One synchronous call in an async method whose declaring type offers a
/// callable async sibling.
/// </summary>
/// <param name="Caller">The async method as the source declares it.</param>
/// <param name="Callee">The synchronous method called.</param>
/// <param name="Alternative">The async sibling that could be awaited instead.</param>
/// <param name="PairKind">How the sibling relates to the callee.</param>
/// <param name="CallOffset">The IL offset of the call in the evidence body.</param>
/// <param name="InLoop">Whether the call site is inside a loop region.</param>
/// <param name="EvidenceMethodToken">The physical MethodDef whose body holds the call.</param>
public sealed record AsyncSiblingRow(
    MethodIdentity Caller,
    MemberRef Callee,
    MemberRef Alternative,
    AsyncSiblingPairKind PairKind,
    int CallOffset,
    bool InLoop,
    int EvidenceMethodToken);

/// <summary>
/// Detached rows and per-body diagnostics from one async-sibling execution.
/// </summary>
/// <param name="Rows">The rows; empty unless the terminal is Rows.</param>
/// <param name="Diagnostics">Per-body diagnostics.</param>
/// <param name="Count">
/// The rows found. Under the Exists terminal the execution stops at the first
/// body with a row, so the count is then at least one rather than complete.
/// </param>
public sealed record AsyncSiblingProducerResult(
    ImmutableArray<AsyncSiblingRow> Rows,
    ImmutableArray<AnalysisDiagnostic> Diagnostics,
    int Count)
{
    /// <summary>Whether any row was found.</summary>
    public bool Exists => Count > 0;
}

/// <summary>
/// Finds synchronous calls in async methods that have a callable async
/// sibling. It declares reference binding because the callee's declaring type
/// and its candidates usually live in another assembly.
/// </summary>
public sealed class AsyncSiblingProducer
    : MethodDefinitionProducer<
        AsyncSiblingProducer.BodyFact,
        AsyncSiblingProducer.Accumulator,
        AsyncSiblingProducerResult>
{
    AsyncSiblingProducer()
        : base(
            "AsyncSiblingOpportunities",
            version: 1,
            tier: 0,
            MethodDefinitionLayers.Body
                | MethodDefinitionLayers.ModuleLookup
                | MethodDefinitionLayers.ReferenceBinding)
    {
    }

    public static AsyncSiblingProducer Instance { get; } = new();

    internal override BodyFact Visit(scoped MethodDefinitionView view)
    {
        if (!view.HasManagedBody)
            return new([], [], 0);

        try
        {
            AsyncSiblingBodyResult result =
                view.Lookup.AnalyzeAsyncSiblings(
                    view.TypeHandle,
                    view.TypeDefinition,
                    view.MethodHandle,
                    view.MethodDefinition,
                    view.GetBody(),
                    view.Terminal == ProducerTerminal.Rows,
                    CancellationToken.None);
            return new(result.Rows, result.Diagnostics, result.Count);
        }
        catch (AssemblyBindingPolicyChangedException exception)
        {
            // A policy change fails the execution rather than one body.
            throw new ProducerAbortException(
                new(
                    "ReferenceBinding",
                    "PolicyVersion",
                    view.Token,
                    $"0x{view.Token:X8}",
                    exception.Message));
        }
        catch (Exception exception)
            when (exception
                    is not
                        MethodDefinitionTerminalWorkLimitExceededException
                && LibraryMethodAnalysisRunner
                    .IsRecoverableMethodFailure(exception))
        {
            return new(
                [],
                [
                    new(
                        view.Token,
                        $"0x{view.Token:X8}",
                        ProducerFailure.Describe(exception)),
                ],
                0);
        }
    }

    internal override Accumulator Seed() => new();

    internal override Accumulator Accumulate(
        Accumulator accumulator,
        BodyFact fact)
    {
        accumulator.Rows.AddRange(fact.Rows);
        accumulator.Diagnostics.AddRange(fact.Diagnostics);
        accumulator.Count += fact.Count;
        return accumulator;
    }

    internal override AsyncSiblingProducerResult Complete(
        Accumulator accumulator,
        MethodDefinitionCompletionView completion) =>
        new(
            accumulator.Rows.ToImmutable(),
            accumulator.Diagnostics.ToImmutable(),
            accumulator.Count);

    internal override bool Settles(BodyFact fact) => fact.Count > 0;

    public sealed record BodyFact(
        ImmutableArray<AsyncSiblingRow> Rows,
        ImmutableArray<AnalysisDiagnostic> Diagnostics,
        int Count);

    public sealed class Accumulator
    {
        internal int Count { get; set; }

        internal ImmutableArray<AsyncSiblingRow>.Builder Rows { get; } =
            ImmutableArray.CreateBuilder<AsyncSiblingRow>();

        internal ImmutableArray<AnalysisDiagnostic>.Builder Diagnostics { get; } =
            ImmutableArray.CreateBuilder<AnalysisDiagnostic>();
    }
}
