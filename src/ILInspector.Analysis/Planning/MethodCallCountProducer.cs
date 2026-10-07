using System.Collections.Immutable;

namespace ILInspector.Analysis.Planning;

/// <summary>The source-native scalar counted for one physical method body.</summary>
public enum MethodCallCountKind
{
    /// <summary><c>call</c>, <c>callvirt</c>, and <c>newobj</c> instructions.</summary>
    DirectInvocations,

    /// <summary>Every Calls-row opcode family, including function loads and <c>calli</c>.</summary>
    CallSites,
}

/// <summary>Count outcome for one physical MethodDef selected by the Method source.</summary>
public sealed record MethodCallCountBody(
    int MethodToken,
    bool HasManagedBody,
    int? Count,
    AnalysisDiagnostic? Diagnostic);

/// <summary>Detached physical-body outcomes from one call-count producer execution.</summary>
public sealed record MethodCallCountProducerResult(
    ImmutableArray<MethodCallCountBody> Bodies);

/// <summary>
/// Counts call opcodes without target resolution, signature enrichment, or
/// <see cref="DirectCall"/> row construction.
/// </summary>
public sealed class MethodCallCountProducer
    : MethodDefinitionProducer<
        MethodCallCountBody,
        ImmutableArray<MethodCallCountBody>.Builder,
        MethodCallCountProducerResult>
{
    readonly MethodCallCountKind _kind;

    MethodCallCountProducer(
        string identity,
        MethodCallCountKind kind)
        : base(
            identity,
            version: 1,
            tier: 0,
            MethodDefinitionLayers.Body,
            parameters: $"kind={kind}")
    {
        _kind = kind;
    }

    public static MethodCallCountProducer DirectInvocations { get; } =
        new(
            "MethodDirectCallCount",
            MethodCallCountKind.DirectInvocations);

    public static MethodCallCountProducer CallSites { get; } =
        new(
            "MethodCallSiteCount",
            MethodCallCountKind.CallSites);

    public MethodCallCountKind Kind => _kind;

    internal override MethodCallCountBody Visit(
        scoped MethodDefinitionView view)
    {
        if (!view.HasManagedBody)
        {
            return new(
                view.Token,
                HasManagedBody: false,
                Count: null,
                Diagnostic: null);
        }

        try
        {
            MethodCallAnalysis.DiscoveryCounts counts =
                MethodCallAnalysis.DiscoverCounts(view.GetBody());
            return new(
                view.Token,
                HasManagedBody: true,
                _kind == MethodCallCountKind.DirectInvocations
                    ? counts.InvocationCount
                    : counts.CallSiteCount,
                Diagnostic: null);
        }
        catch (Exception exception)
            when (exception
                    is not
                        MethodDefinitionTerminalWorkLimitExceededException
                && LibraryMethodAnalysisRunner
                    .IsRecoverableMethodFailure(exception))
        {
            return new(
                view.Token,
                HasManagedBody: true,
                Count: null,
                new(
                    view.Token,
                    $"0x{view.Token:X8}",
                    ProducerFailure.Describe(exception)));
        }
    }

    internal override ImmutableArray<MethodCallCountBody>.Builder Seed() =>
        ImmutableArray.CreateBuilder<MethodCallCountBody>();

    internal override ImmutableArray<MethodCallCountBody>.Builder Accumulate(
        ImmutableArray<MethodCallCountBody>.Builder accumulator,
        MethodCallCountBody fact)
    {
        accumulator.Add(fact);
        return accumulator;
    }

    internal override MethodCallCountProducerResult Complete(
        ImmutableArray<MethodCallCountBody>.Builder accumulator,
        MethodDefinitionCompletionView completion) =>
        new(accumulator.ToImmutable());
}
