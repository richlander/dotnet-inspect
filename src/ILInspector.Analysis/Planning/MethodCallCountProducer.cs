using System.Collections.Immutable;
using System.Reflection.Metadata;

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

    internal override MethodDefinitionExecution.ProducerState CreateState(
        MethodDefinitionExecution execution,
        ProducerTerminal terminal,
        int? rowLimit,
        ImmutableArray<int> dependencies,
        UnitFactRetention retention) =>
        new FusedState(
            this,
            execution,
            terminal,
            rowLimit,
            dependencies,
            retention);

    internal override ImmutableArray<MethodBodyAnalyzerDeclaration>
        InstructionAnalyzers =>
        [
            _kind == MethodCallCountKind.DirectInvocations
                ? MethodBodyAnalyzerDeclarations.DirectCalls
                : MethodBodyAnalyzerDeclarations.CallSites,
        ];

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
            var counts = new CallCounts();
            view.VisitInstructionShapes(
                ref counts,
                CountInstruction);
            return CompletedBody(view.Token, _kind, counts);
        }
        catch (Exception exception)
            when (exception
                    is not
                        MethodDefinitionTerminalWorkLimitExceededException
                && LibraryMethodAnalysisRunner
                .IsRecoverableMethodFailure(exception))
        {
            return FailedBody(view.Token, exception);
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

    struct CallCounts
    {
        public int InvocationCount;

        public int CallSiteCount;
    }

    static bool CountInstruction(
        ref CallCounts counts,
        ILOpCode opcode,
        int encodedLength)
    {
        _ = encodedLength;
        if (opcode is ILOpCode.Call
            or ILOpCode.Callvirt
            or ILOpCode.Newobj)
        {
            counts.InvocationCount++;
            counts.CallSiteCount++;
        }
        else if (opcode is ILOpCode.Ldftn
            or ILOpCode.Ldvirtftn
            or ILOpCode.Calli)
        {
            counts.CallSiteCount++;
        }
        return true;
    }

    static MethodCallCountBody CompletedBody(
        int methodToken,
        MethodCallCountKind kind,
        CallCounts counts) =>
        new(
            methodToken,
            HasManagedBody: true,
            kind == MethodCallCountKind.DirectInvocations
                ? counts.InvocationCount
                : counts.CallSiteCount,
            Diagnostic: null);

    static MethodCallCountBody FailedBody(
        int methodToken,
        Exception exception) =>
        new(
            methodToken,
            HasManagedBody: true,
            Count: null,
            new(
                methodToken,
                $"0x{methodToken:X8}",
                ProducerFailure.Describe(exception)));

    sealed class FusedState : State
    {
        readonly MethodCallCountProducer _producer;
        CallCounts _counts;
        int _methodToken;

        public FusedState(
            MethodCallCountProducer producer,
            MethodDefinitionExecution execution,
            ProducerTerminal terminal,
            int? rowLimit,
            ImmutableArray<int> dependencies,
            UnitFactRetention retention)
            : base(
                producer,
                execution,
                terminal,
                rowLimit,
                dependencies,
                retention)
        {
            _producer = producer;
        }

        public override bool SupportsFusedInstructionShapes => true;

        public override bool TryBeginInstructionShapes(
            scoped MethodDefinitionView view,
            out MethodBodyBlock? body,
            out bool settled)
        {
            _methodToken = view.Token;
            _counts = default;
            if (!view.HasManagedBody)
            {
                body = null;
                settled = AcceptFact(
                    view.Token,
                    new(
                        view.Token,
                        HasManagedBody: false,
                        Count: null,
                        Diagnostic: null));
                return false;
            }

            try
            {
                body = view.GetBody();
                settled = false;
                return true;
            }
            catch (Exception exception)
                when (exception
                        is not
                            MethodDefinitionTerminalWorkLimitExceededException
                    && LibraryMethodAnalysisRunner
                        .IsRecoverableMethodFailure(exception))
            {
                body = null;
                settled = AcceptFact(
                    view.Token,
                    FailedBody(view.Token, exception));
                return false;
            }
        }

        public override bool VisitInstructionShape(
            ILOpCode opcode,
            int encodedLength) =>
            CountInstruction(
                ref _counts,
                opcode,
                encodedLength);

        public override bool CompleteInstructionShapes(Exception? failure) =>
            AcceptFact(
                _methodToken,
                failure is null
                    ? CompletedBody(
                        _methodToken,
                        _producer._kind,
                        _counts)
                    : FailedBody(_methodToken, failure));

    }
}
