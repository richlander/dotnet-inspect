using System.Collections.Immutable;
using System.Reflection.PortableExecutable;

using ILInspector.Analysis.Classification;
using ILInspector.Analysis.Planning;
using ILInspector.Metadata;
using Inspector.Findings;

using Analyzer = ILInspector.Analysis.Planning.ProducerDeclaration<
    ILInspector.Analysis.Planning.ClosedQueryResult<ILInspector.Analysis.Classification.ClassifiedMethodRow>>;
using Result = ILInspector.Analysis.Planning.ProducerResult<
    ILInspector.Analysis.Planning.ClosedQueryResult<ILInspector.Analysis.Classification.ClassifiedMethodRow>>;

namespace DotnetInspector.Queries;

/// <summary>What a question asks about: one method classification analyzer.</summary>
public enum MethodClassificationAnalyzer
{
    PInvoke,

    /// <summary>
    /// Runtime async or compiler async, in one pass: the union of
    /// <see cref="RuntimeAsync"/> and <see cref="CompilerAsync"/>, which are
    /// disjoint.
    /// </summary>
    Async,

    PointerSignature,

    /// <summary>The runtime-async analyzer: the <c>MethodImplAttributes.Async</c> flag.</summary>
    RuntimeAsync,

    /// <summary>The compiler-async analyzer: a compiler async state-machine attribute, without the runtime flag.</summary>
    CompilerAsync,
}

/// <summary>What a consumer asks of one analyzer.</summary>
public enum ClassificationClosing
{
    /// <summary>The classified rows, projected with identity text.</summary>
    Rows,

    /// <summary>How many rows there are; reads no identity text.</summary>
    Count,

    /// <summary>Whether any row exists; reads no identity text and stops at the first.</summary>
    Exists,

    /// <summary>
    /// The first N rows in metadata order; projects matching rows and stops
    /// when N have been produced or the source is exhausted.
    /// </summary>
    Head,
}

/// <summary>
/// The order of an analyzer's rows: exactly the orders today's outputs use.
/// </summary>
public enum ClassifiedRowOrder
{
    /// <summary>Metadata order: the order the analyzer visits rows.</summary>
    Metadata,

    /// <summary>
    /// The model order (JSON and <c>LibraryInspection</c>): declaring type, then
    /// method name (default comparer); async rows first by kind (ordinal).
    /// </summary>
    Model,

    /// <summary>
    /// The Markdown display order: the model order, stably re-sorted by declaring
    /// type, method name, then module name (P/Invoke) and signature
    /// (<see cref="StringComparer.OrdinalIgnoreCase"/>). Pointer rows have none.
    /// </summary>
    Display,
}

/// <summary>One consumer's question of one analyzer.</summary>
public sealed record ClassificationQuestion
{
    public ClassificationQuestion(
        MethodClassificationAnalyzer analyzer,
        ClassificationClosing closing,
        ClassifiedRowOrder order = ClassifiedRowOrder.Metadata,
        int? headCount = null)
    {
        Execution = new ClassificationExecution(closing, headCount);
        if (closing == ClassificationClosing.Head
            && order != ClassifiedRowOrder.Metadata)
        {
            throw new ArgumentException(
                "Head supports metadata order only.",
                nameof(order));
        }

        Analyzer = analyzer;
        Closing = closing;
        Order = order;
        HeadCount = headCount;
    }

    public MethodClassificationAnalyzer Analyzer { get; }

    public ClassificationClosing Closing { get; }

    public ClassifiedRowOrder Order { get; }

    public int? HeadCount { get; }

    public static ClassificationQuestion Head(
        MethodClassificationAnalyzer analyzer,
        int count) =>
        new(analyzer, ClassificationClosing.Head, headCount: count);

    internal ClassificationExecution Execution { get; }
}

/// <summary>
/// One independently executed closing, including the operand that gives
/// Head(N) its identity.
/// </summary>
public readonly record struct ClassificationExecution
{
    public ClassificationExecution(
        ClassificationClosing closing,
        int? headCount = null)
    {
        if (closing == ClassificationClosing.Head)
        {
            if (headCount is not > 0)
                throw new ArgumentOutOfRangeException(nameof(headCount));
        }
        else if (headCount is not null)
        {
            throw new ArgumentException(
                "A Head count is valid only for the Head closing.",
                nameof(headCount));
        }

        Closing = closing;
        HeadCount = headCount;
    }

    public ClassificationClosing Closing { get; }

    public int? HeadCount { get; }

    public override string ToString() =>
        HeadCount is int count ? $"Head({count})" : Closing.ToString();
}

/// <summary>An analyzer's answer to one question.</summary>
public abstract record ClassificationAnswer
{
    private ClassificationAnswer()
    {
    }

    public sealed record Rows(ImmutableArray<ClassifiedMethodRow> Methods) : ClassificationAnswer;

    public sealed record Count(int Value) : ClassificationAnswer;

    public sealed record Exists(bool Value) : ClassificationAnswer;

    /// <summary>The analyzer failed recoverably at one unit, such as a malformed pointer signature.</summary>
    public sealed record Failed(ProducerFailure Failure) : ClassificationAnswer;

    /// <summary>A containment budget was exhausted; every question in that closing's execution has this answer.</summary>
    public sealed record Aborted(CriticalFailure Critical) : ClassificationAnswer;
}

/// <summary>One completed closing and its source-work receipt.</summary>
public readonly record struct ClassificationReceipt(
    ClassificationExecution Execution,
    WorkReceipt Receipt);

/// <summary>
/// The typed result of a classification request: one answer per question, in
/// question order, and, when the Finding was requested, the merged rows in
/// legacy order and the classified-method Finding inspection built from them.
/// <see cref="Finding"/> is null only when the Finding was not requested. When
/// an analyzer failed or the execution aborted, it is a failed inspection that
/// names the analyzer and the unit, and <see cref="MergedRows"/> is default.
/// <see cref="Receipts"/> holds one work receipt per closing and operand that
/// ran, and
/// <see cref="Critical"/> the first execution's critical failure, if any.
/// </summary>
public sealed record MethodClassificationResult(
    ImmutableArray<(ClassificationQuestion Question, ClassificationAnswer Answer)> Answers,
    ImmutableArray<ClassifiedMethodRow> MergedRows,
    FindingInspection<ClassifiedMethodObservation>? Finding,
    CriticalFailure? Critical,
    ImmutableArray<ClassificationReceipt> Receipts)
{
    public ClassificationAnswer AnswerTo(ClassificationQuestion question)
    {
        foreach ((ClassificationQuestion asked, ClassificationAnswer answer) in Answers)
        {
            if (asked == question)
                return answer;
        }

        throw new ArgumentException("The question was not asked.", nameof(question));
    }

    public WorkReceipt ReceiptOf(ClassificationExecution execution)
    {
        foreach (ClassificationReceipt receipt in Receipts)
        {
            if (receipt.Execution == execution)
                return receipt.Receipt;
        }

        throw new ArgumentException(
            "The execution was not requested.",
            nameof(execution));
    }
}

/// <summary>
/// Host-neutral method classification: per-analyzer questions and one
/// combined request. Each closing runs as its own request in its own Producer
/// Planning execution, so no closing derives from another. Count and Exists
/// never read identity text; composing requests belongs to QuerySpace (#8574).
/// Hosts bind questions and map answers; they never sort, merge, or count rows.
/// </summary>
/// <remarks>
/// Owned by <c>docs/design/method-classification-analyzers.md#queries-and-demand</c>.
/// </remarks>
public static class MethodClassificationQuery
{
    public static InspectionQuery<MethodClassificationResult> Definition { get; } =
        new("Method classification", InspectionCost.NetworkFree);

    /// <summary>Answers <paramref name="questions"/> over an open session.</summary>
    public static MethodClassificationResult Execute(
        AssemblyInspectionSession session,
        IReadOnlyList<ClassificationQuestion> questions,
        FindingSubject? findingSubject = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        return session.InspectImage(peReader => Execute(peReader, questions, findingSubject));
    }

    /// <summary>
    /// Answers <paramref name="questions"/> over <paramref name="peReader"/>.
    /// With <paramref name="findingSubject"/>, every analyzer runs Rows and the
    /// result carries the merged rows and the classified-method Finding.
    /// </summary>
    public static MethodClassificationResult Execute(
        PEReader peReader,
        IReadOnlyList<ClassificationQuestion> questions,
        FindingSubject? findingSubject = null)
    {
        ArgumentNullException.ThrowIfNull(peReader);
        ArgumentNullException.ThrowIfNull(questions);
        foreach (ClassificationQuestion question in questions)
        {
            ArgumentNullException.ThrowIfNull(question);
            if (question.Analyzer == MethodClassificationAnalyzer.PointerSignature
                && question.Order == ClassifiedRowOrder.Display)
            {
                throw new ArgumentException(
                    "Pointer-signature rows have no display order.",
                    nameof(questions));
            }
        }

        bool finding = findingSubject is not null;

        // The legacy scan published nothing for an image it did not admit.
        if (!MetadataFormatAdmission.AdmitImage(peReader))
            return Empty(questions, findingSubject);

        // Each closing and operand is its own request and execution: planning
        // never derives Count or Exists from Rows. The Finding asks for Rows
        // of every analyzer, so it shares the Rows execution.
        var requestedExecutions = new List<ClassificationExecution>();
        if (finding)
        {
            requestedExecutions.Add(
                new ClassificationExecution(ClassificationClosing.Rows));
        }

        foreach (ClassificationClosing closing in Enum.GetValues<ClassificationClosing>())
        {
            foreach (ClassificationQuestion question in questions)
            {
                if (question.Closing == closing
                    && !requestedExecutions.Contains(question.Execution))
                {
                    requestedExecutions.Add(question.Execution);
                }
            }
        }

        var executions =
            new MethodDefinitionExecution[requestedExecutions.Count];
        for (int executionIndex = 0;
            executionIndex < requestedExecutions.Count;
            executionIndex++)
        {
            ClassificationExecution execution =
                requestedExecutions[executionIndex];
            var requests = new List<ProducerRequest>();
            foreach (MethodClassificationAnalyzer analyzer in Enum.GetValues<MethodClassificationAnalyzer>())
            {
                bool requested = finding
                    && execution.Closing == ClassificationClosing.Rows
                    && MergedAnalyzers.Contains(analyzer);
                foreach (ClassificationQuestion question in questions)
                {
                    requested |= question.Analyzer == analyzer
                        && question.Execution == execution;
                }
                if (requested)
                {
                    Analyzer producer = ProducerFor(analyzer);
                    requests.Add(execution.HeadCount is int count
                        ? ProducerRequest.Head(producer, count)
                        : new ProducerRequest(
                            producer,
                            TerminalFor(execution.Closing)));
                }
            }

            if (requests.Count == 0)
                continue;

            WorkDescription description =
                ProducerPlanner.Plan(requests) is ProducerPlanResult.Accepted accepted
                    ? accepted.Description
                    : throw new InvalidOperationException(
                        "The method classification request must plan.");
            executions[executionIndex] = MethodDefinitionExecution.Execute(
                description,
                "MethodClassification",
                peReader);
        }

        var answers = ImmutableArray.CreateBuilder<(ClassificationQuestion, ClassificationAnswer)>(questions.Count);
        foreach (ClassificationQuestion question in questions)
        {
            answers.Add((question, Answer(
                ExecutionOf(
                    requestedExecutions,
                    executions,
                    question.Execution)
                    .ResultOf(ProducerFor(question.Analyzer)),
                question)));
        }

        ImmutableArray<ClassifiedMethodRow> merged = default;
        FindingInspection<ClassifiedMethodObservation>? inspection = null;
        if (finding)
        {
            merged = Merge(
                ExecutionOf(
                    requestedExecutions,
                    executions,
                    new ClassificationExecution(
                        ClassificationClosing.Rows)),
                out string? failure);
            inspection = merged.IsDefault
                ? new FindingInspection<ClassifiedMethodObservation>(
                    new FindingInspection<ClassifiedMethodObservation>.Failed(
                        new InspectionError(
                            findingSubject!,
                            MetadataFindings.ClassifiedMethodDescriptor,
                            failure!)))
                : MetadataFindings.InspectClassifiedMethods(
                    merged.Select(ToClassifiedMethodInfo),
                    findingSubject!);
        }

        var receipts =
            ImmutableArray.CreateBuilder<ClassificationReceipt>(
                requestedExecutions.Count);
        CriticalFailure? critical = null;
        for (int i = 0; i < requestedExecutions.Count; i++)
        {
            ClassificationExecution execution = requestedExecutions[i];
            MethodDefinitionExecution completed = executions[i];
            receipts.Add(new ClassificationReceipt(
                execution,
                completed.Receipt));
            critical ??= completed.Receipt.Critical;
        }

        return new MethodClassificationResult(
            answers.MoveToImmutable(),
            merged,
            inspection,
            critical,
            receipts.MoveToImmutable());
    }

    static MethodDefinitionExecution ExecutionOf(
        IReadOnlyList<ClassificationExecution> identities,
        IReadOnlyList<MethodDefinitionExecution> executions,
        ClassificationExecution identity)
    {
        for (int i = 0; i < identities.Count; i++)
        {
            if (identities[i] == identity)
                return executions[i];
        }

        throw new InvalidOperationException(
            $"The method classification execution '{identity}' did not run.");
    }

    static MethodClassificationResult Empty(
        IReadOnlyList<ClassificationQuestion> questions,
        FindingSubject? findingSubject)
    {
        var answers = ImmutableArray.CreateBuilder<(ClassificationQuestion, ClassificationAnswer)>(questions.Count);
        foreach (ClassificationQuestion question in questions)
        {
            answers.Add((question, question.Closing switch
            {
                ClassificationClosing.Rows or ClassificationClosing.Head =>
                    new ClassificationAnswer.Rows([]),
                ClassificationClosing.Count => new ClassificationAnswer.Count(0),
                _ => new ClassificationAnswer.Exists(false),
            }));
        }

        return new MethodClassificationResult(
            answers.MoveToImmutable(),
            findingSubject is null ? default : [],
            findingSubject is null
                ? null
                : MetadataFindings.InspectClassifiedMethods([], findingSubject),
            null,
            []);
    }

    /// <summary>
    /// The analyzers the Finding merges, in the legacy within-method order:
    /// P/Invoke, async, pointer signature. Async covers runtime and compiler async.
    /// </summary>
    static readonly MethodClassificationAnalyzer[] MergedAnalyzers =
    [
        MethodClassificationAnalyzer.PInvoke,
        MethodClassificationAnalyzer.Async,
        MethodClassificationAnalyzer.PointerSignature,
    ];

    static Analyzer ProducerFor(MethodClassificationAnalyzer analyzer) =>
        analyzer switch
        {
            MethodClassificationAnalyzer.PInvoke => PInvokeAnalyzer.Instance,
            MethodClassificationAnalyzer.Async => AsyncAnalyzer.Instance,
            MethodClassificationAnalyzer.PointerSignature => PointerSignatureAnalyzer.Instance,
            MethodClassificationAnalyzer.RuntimeAsync => RuntimeAsyncAnalyzer.Instance,
            MethodClassificationAnalyzer.CompilerAsync => CompilerAsyncAnalyzer.Instance,
            _ => throw new ArgumentOutOfRangeException(nameof(analyzer)),
        };

    static ProducerTerminal TerminalFor(ClassificationClosing closing) =>
        closing switch
        {
            ClassificationClosing.Rows => ProducerTerminal.Rows,
            ClassificationClosing.Head => ProducerTerminal.Rows,
            ClassificationClosing.Count => ProducerTerminal.Complete,
            _ => ProducerTerminal.Exists,
        };

    static ClassificationAnswer Answer(Result result, ClassificationQuestion question)
    {
        if (result.Outcome == ProducerOutcome.Aborted)
            return new ClassificationAnswer.Aborted(result.Critical!);
        if (!result.HasValue)
            return new ClassificationAnswer.Failed(result.Failure!);

        ClosedQueryResult<ClassifiedMethodRow> value = result.Value!;
        return question.Closing switch
        {
            ClassificationClosing.Rows or ClassificationClosing.Head =>
                new ClassificationAnswer.Rows(
                    Order(
                    value.HasRows
                        ? value.Rows
                        : throw new InvalidOperationException(
                            "A row closing must publish rows."),
                    question.Analyzer,
                    question.Order)),
            ClassificationClosing.Count => new ClassificationAnswer.Count(value.Count),
            _ => new ClassificationAnswer.Exists(value.Exists),
        };
    }

    /// <summary>
    /// The analyzers' rows in the legacy order the vocabulary declares:
    /// traversal order, then P/Invoke, async, pointer signature within one
    /// method. Default when an
    /// analyzer did not publish rows, with <paramref name="failure"/> naming
    /// the analyzer and the unit.
    /// </summary>
    static ImmutableArray<ClassifiedMethodRow> Merge(
        MethodDefinitionExecution execution,
        out string? failure)
    {
        failure = null;
        var rows = new List<ClassifiedMethodRow>();
        foreach (MethodClassificationAnalyzer analyzer in MergedAnalyzers)
        {
            Analyzer producer = ProducerFor(analyzer);
            Result result = execution.ResultOf(producer);
            if (result.Outcome == ProducerOutcome.Aborted)
            {
                CriticalFailure critical = result.Critical!;
                failure = $"{producer.Identity} aborted at {critical.Unit}: {critical.Budget}.";
                return default;
            }

            if (!result.HasValue || !result.Value!.HasRows)
            {
                failure = result.Failure is { } failed
                    ? $"{producer.Identity} failed at {failed.Unit}: {failed.Message}"
                    : $"{producer.Identity} did not complete ({result.Outcome}).";
                return default;
            }

            rows.AddRange(result.Value.Rows);
        }

        return [.. ClassifiedMethodRowOrders.Apply(rows, ClassifiedMethodRowOrders.Legacy)];
    }

    static ImmutableArray<ClassifiedMethodRow> Order(
        ImmutableArray<ClassifiedMethodRow> rows,
        MethodClassificationAnalyzer analyzer,
        ClassifiedRowOrder order)
    {
        string key = (order, analyzer) switch
        {
            (ClassifiedRowOrder.Metadata, _) => ClassifiedMethodRowOrders.Metadata,
            (ClassifiedRowOrder.Model, MethodClassificationAnalyzer.Async
                or MethodClassificationAnalyzer.RuntimeAsync
                or MethodClassificationAnalyzer.CompilerAsync) => ClassifiedMethodRowOrders.AsyncModel,
            (ClassifiedRowOrder.Model, _) => ClassifiedMethodRowOrders.Model,
            (ClassifiedRowOrder.Display, MethodClassificationAnalyzer.Async
                or MethodClassificationAnalyzer.RuntimeAsync
                or MethodClassificationAnalyzer.CompilerAsync) => ClassifiedMethodRowOrders.AsyncDisplay,
            (ClassifiedRowOrder.Display, MethodClassificationAnalyzer.PInvoke) => ClassifiedMethodRowOrders.PInvokeDisplay,
            _ => ClassifiedMethodRowOrders.Display,
        };
        return [.. ClassifiedMethodRowOrders.Apply(rows, key)];
    }

    /// <summary>The async kind text the model sorts by.</summary>
    public static string AsyncKind(MethodClassification classification) =>
        ClassifiedMethodRowOrders.AsyncKind(classification);

    static ClassifiedMethodInfo ToClassifiedMethodInfo(ClassifiedMethodRow row) =>
        new(
            row.MethodName.ToString(),
            row.DeclaringType.ToString(),
            row.Namespace.ToString(),
            row.Signature.ToString(),
            row.Classification,
            row.ModuleName?.ToString())
        {
            Anchor = row.Anchor?.Key,
            ReturnType = row.ReturnType?.ToString(),
        };
}
