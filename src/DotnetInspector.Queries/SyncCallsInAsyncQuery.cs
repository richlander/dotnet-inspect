using System.Collections.Immutable;

using ILInspector.Analysis;
using ILInspector.Analysis.Planning;
using ILInspector.Metadata;

namespace DotnetInspector.Queries;

/// <summary>What a Sync Calls in Async question asks for.</summary>
public enum SyncCallsInAsyncClosing
{
    /// <summary>The rows.</summary>
    Rows,

    /// <summary>The row count, without publishing rows.</summary>
    Count,

    /// <summary>Whether any row exists; execution stops at the first.</summary>
    Exists,
}

/// <summary>One closing's answer.</summary>
public abstract record SyncCallsInAsyncAnswer
{
    private SyncCallsInAsyncAnswer()
    {
    }

    public sealed record Rows(ImmutableArray<AsyncSiblingRow> Calls)
        : SyncCallsInAsyncAnswer;

    public sealed record Count(int Value) : SyncCallsInAsyncAnswer;

    public sealed record Exists(bool Value) : SyncCallsInAsyncAnswer;

    /// <summary>The producer failed recoverably before completing.</summary>
    public sealed record Failed(ProducerFailure Failure)
        : SyncCallsInAsyncAnswer;

    /// <summary>
    /// A containment budget was exhausted, or reference binding changed
    /// during the execution.
    /// </summary>
    public sealed record Aborted(CriticalFailure Critical)
        : SyncCallsInAsyncAnswer;
}

/// <summary>
/// The typed result of a Sync Calls in Async request: one answer and one work
/// receipt per requested closing. <see cref="Diagnostics"/> are the
/// per-body failures of the first successful closing that published any, so
/// an incomplete scan stays visible beside its answer.
/// </summary>
public sealed record SyncCallsInAsyncResult(
    ImmutableArray<(SyncCallsInAsyncClosing Closing, SyncCallsInAsyncAnswer Answer)> Answers,
    ImmutableArray<AnalysisDiagnostic> Diagnostics,
    ImmutableArray<(SyncCallsInAsyncClosing Closing, WorkReceipt Receipt)> Receipts)
{
    public SyncCallsInAsyncAnswer AnswerTo(SyncCallsInAsyncClosing closing)
    {
        foreach ((SyncCallsInAsyncClosing asked, SyncCallsInAsyncAnswer answer)
            in Answers)
        {
            if (asked == closing)
                return answer;
        }

        throw new ArgumentException(
            "The closing was not requested.",
            nameof(closing));
    }

    /// <summary>The first critical failure of any requested closing, if any.</summary>
    public CriticalFailure? Critical =>
        Receipts.Select(static r => r.Receipt.Critical)
            .FirstOrDefault(static c => c is not null);
}

/// <summary>The prepared, immutable request set for one retained session.</summary>
public sealed class PreparedSyncCallsInAsyncQuery
{
    internal PreparedSyncCallsInAsyncQuery(
        AssemblyInspectionSession session,
        AssemblyReferenceBindingAccess referenceBinding,
        ImmutableArray<SyncCallsInAsyncClosing> closings,
        ImmutableArray<Binding> bindings,
        AssemblyAnalysisRequestSetOperation? operation)
    {
        Session = session;
        ReferenceBinding = referenceBinding;
        Closings = closings;
        Bindings = bindings;
        Operation = operation;
    }

    public ImmutableArray<SyncCallsInAsyncClosing> Closings { get; }

    internal AssemblyInspectionSession Session { get; }

    internal AssemblyReferenceBindingAccess ReferenceBinding { get; }

    internal ImmutableArray<Binding> Bindings { get; }

    internal AssemblyAnalysisRequestSetOperation? Operation { get; }

    internal readonly record struct Binding(
        SyncCallsInAsyncClosing Closing,
        MethodDefinitionSourceAssociation Association,
        MethodDefinitionSourceRequest<AsyncSiblingProducerResult> Request);
}

/// <summary>
/// Host-neutral Sync Calls in Async query: each requested closing is an
/// independent QuerySpace request over one Method-source traversal, and the
/// hosts bind questions and map answers; they never sort, merge, or count rows.
/// The host supplies the reference binding because the callee's declaring type
/// usually lives in another assembly.
/// </summary>
/// <remarks>
/// Owned by <c>docs/design/analysis-ux-scopes.md</c> (Performance: Sync Calls
/// in Async).
/// </remarks>
public static class SyncCallsInAsyncQuery
{
    public static InspectionQuery<SyncCallsInAsyncResult> Definition { get; } =
        new("Sync calls in async", InspectionCost.NetworkFree);

    public static SyncCallsInAsyncResult Execute(
        AssemblyInspectionSession session,
        AssemblyReferenceBindingAccess referenceBinding,
        IReadOnlyList<SyncCallsInAsyncClosing> closings) =>
        Execute(Prepare(session, referenceBinding, closings));

    public static PreparedSyncCallsInAsyncQuery Prepare(
        AssemblyInspectionSession session,
        AssemblyReferenceBindingAccess referenceBinding,
        IReadOnlyList<SyncCallsInAsyncClosing> closings)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(referenceBinding);
        ArgumentNullException.ThrowIfNull(closings);
        ImmutableArray<SyncCallsInAsyncClosing> distinct =
            [.. closings.Distinct()];
        if (distinct.IsEmpty)
        {
            throw new ArgumentException(
                "At least one closing is required.",
                nameof(closings));
        }

        if (!session.HasMetadata
            || !session.InspectImage(MetadataFormatAdmission.AdmitImage))
        {
            return new(session, referenceBinding, distinct, [], null);
        }

        var associations =
            ImmutableArray.CreateBuilder<MethodDefinitionSourceAssociation>();
        var bindings =
            ImmutableArray.CreateBuilder<PreparedSyncCallsInAsyncQuery.Binding>();
        foreach (SyncCallsInAsyncClosing closing in distinct)
        {
            ProducerRequest producerRequest = new(
                AsyncSiblingProducer.Instance,
                TerminalFor(closing));
            WorkDescription work =
                ProducerPlanner.Plan([producerRequest])
                    is ProducerPlanResult.Accepted accepted
                        ? accepted.Description
                        : throw new ProducerContractException(
                            "The Sync Calls in Async request must plan.");
            MethodDefinitionSourceRequest<AsyncSiblingProducerResult> request =
                MethodDefinitionSourceRequest<AsyncSiblingProducerResult>.Create(
                    SyncCallsInAsyncQuerySpace.CreateRequest(closing),
                    work,
                    AsyncSiblingProducer.Instance);
            MethodDefinitionSourceAssociation association =
                MethodDefinitionSourceAssociation.Create(request);
            associations.Add(association);
            bindings.Add(new(closing, association, request));
        }

        MethodDefinitionSourceRequestSetPlan plan =
            MethodDefinitionSourceRequestSet.Plan(
                MethodDefinitionSourceResourceIdentity.Create(),
                associations)
            is MethodDefinitionSourceRequestSetPlanResult.Accepted planned
                ? planned.Plan
                : throw new ProducerContractException(
                    "The Sync Calls in Async QuerySpace request set must plan.");
        return new(
            session,
            referenceBinding,
            distinct,
            bindings.ToImmutable(),
            AssemblyAnalysisRequestSetOperation.Create(
                "SyncCallsInAsync",
                plan));
    }

    public static SyncCallsInAsyncResult Execute(
        PreparedSyncCallsInAsyncQuery prepared)
    {
        ArgumentNullException.ThrowIfNull(prepared);
        if (prepared.Operation is not { } operation)
            return Empty(prepared.Closings);

        AssemblyAnalysisRequestSetServiceResult serviceResult =
            prepared.Session.SnapshotOperation(
                operation,
                prepared.ReferenceBinding,
                access =>
                    AssemblyAnalysisService.Instance.Execute(
                        operation,
                        access));
        if (serviceResult
            is AssemblyAnalysisRequestSetServiceResult.Rejected rejected)
        {
            if (rejected.Kind
                == AssemblyAnalysisRejectionKind.ManagedMetadataUnavailable)
            {
                return Empty(prepared.Closings);
            }

            throw new ProducerContractException(
                "The Sync Calls in Async request-set operation was "
                + $"rejected: {rejected.Kind}.");
        }

        MethodDefinitionSourceRequestSetExecution completed =
            ((AssemblyAnalysisRequestSetServiceResult.Completed)serviceResult)
                .Execution;
        var answers = ImmutableArray.CreateBuilder<
            (SyncCallsInAsyncClosing, SyncCallsInAsyncAnswer)>();
        var receipts = ImmutableArray.CreateBuilder<
            (SyncCallsInAsyncClosing, WorkReceipt)>();
        ImmutableArray<AnalysisDiagnostic> diagnostics = [];
        foreach (PreparedSyncCallsInAsyncQuery.Binding binding
            in prepared.Bindings)
        {
            ProducerResult<AsyncSiblingProducerResult> result =
                completed.ResultOf(binding.Association, binding.Request);
            receipts.Add((
                binding.Closing,
                completed.ResultOf(binding.Association).WorkReceipt));
            if (result.HasValue
                && diagnostics.IsEmpty
                && !result.Value!.Diagnostics.IsEmpty)
            {
                diagnostics = result.Value.Diagnostics;
            }

            answers.Add((binding.Closing, Answer(result, binding.Closing)));
        }

        return new(
            answers.ToImmutable(),
            diagnostics,
            receipts.ToImmutable());
    }

    static SyncCallsInAsyncResult Empty(
        ImmutableArray<SyncCallsInAsyncClosing> closings) =>
        new(
            [.. closings.Select(static closing =>
                (closing,
                    closing switch
                    {
                        SyncCallsInAsyncClosing.Rows =>
                            (SyncCallsInAsyncAnswer)
                                new SyncCallsInAsyncAnswer.Rows([]),
                        SyncCallsInAsyncClosing.Count =>
                            new SyncCallsInAsyncAnswer.Count(0),
                        _ => new SyncCallsInAsyncAnswer.Exists(false),
                    }))],
            [],
            []);

    static ProducerTerminal TerminalFor(SyncCallsInAsyncClosing closing) =>
        closing switch
        {
            SyncCallsInAsyncClosing.Rows => ProducerTerminal.Rows,
            SyncCallsInAsyncClosing.Count => ProducerTerminal.Count,
            SyncCallsInAsyncClosing.Exists => ProducerTerminal.Exists,
            _ => throw new ArgumentOutOfRangeException(nameof(closing)),
        };

    static SyncCallsInAsyncAnswer Answer(
        ProducerResult<AsyncSiblingProducerResult> result,
        SyncCallsInAsyncClosing closing)
    {
        if (result.Outcome == ProducerOutcome.Aborted)
            return new SyncCallsInAsyncAnswer.Aborted(result.Critical!);
        if (!result.HasValue)
            return new SyncCallsInAsyncAnswer.Failed(result.Failure!);

        AsyncSiblingProducerResult value = result.Value!;
        return closing switch
        {
            SyncCallsInAsyncClosing.Rows =>
                new SyncCallsInAsyncAnswer.Rows(value.Rows),
            SyncCallsInAsyncClosing.Count =>
                new SyncCallsInAsyncAnswer.Count(value.Count),
            _ => new SyncCallsInAsyncAnswer.Exists(value.Exists),
        };
    }
}
