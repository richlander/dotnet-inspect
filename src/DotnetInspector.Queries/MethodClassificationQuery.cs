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

    /// <summary>
    /// Public static extension methods on static extension types, neither
    /// hidden: the method half of the Library Info Extension Methods row.
    /// </summary>
    Extension,
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
/// One analyzer association's independently settled work receipt.
/// </summary>
public readonly record struct ClassificationAssociationReceipt(
    ClassificationExecution Execution,
    MethodClassificationAnalyzer Analyzer,
    WorkReceipt Receipt);

internal readonly record struct ClassificationRequestBinding(
    ClassificationExecution Execution,
    MethodClassificationAnalyzer Analyzer,
    MethodDefinitionSourceAssociation Association,
    MethodDefinitionSourceRequest<
        ClosedQueryResult<ClassifiedMethodRow>> Request);

/// <summary>
/// The typed result of a classification request: one answer per question, in
/// question order, and, when the Finding was requested, the merged rows in
/// legacy order and the classified-method Finding inspection built from them.
/// <see cref="Finding"/> is null only when the Finding was not requested. When
/// an analyzer failed or the execution aborted, it is a failed inspection that
/// names the analyzer and the unit, and <see cref="MergedRows"/> is default.
/// <see cref="Receipts"/> holds one work receipt per closing and operand that
/// ran, and <see cref="Critical"/> the first requested execution's first
/// analyzer-association critical failure, if any.
/// </summary>
public sealed record MethodClassificationResult(
    ImmutableArray<(ClassificationQuestion Question, ClassificationAnswer Answer)> Answers,
    ImmutableArray<ClassifiedMethodRow> MergedRows,
    FindingInspection<ClassifiedMethodObservation>? Finding,
    CriticalFailure? Critical,
    ImmutableArray<ClassificationReceipt> Receipts)
{
    /// <summary>
    /// Physical Method-source work for the session-backed QuerySpace
    /// execution; default for the direct PEReader reference path.
    /// </summary>
    public ImmutableArray<MethodDefinitionSourceGroupReceipt> SourceGroups
    {
        get;
        init;
    }

    /// <summary>
    /// Exact per-association receipts for the session-backed QuerySpace path;
    /// default for the direct PEReader reference path.
    /// </summary>
    public ImmutableArray<ClassificationAssociationReceipt>
        AssociationReceipts
    {
        get;
        init;
    }

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
/// Immutable Method Classification planning bound to one retained assembly
/// session, preserving the exact question set and caller associations it
/// prepared.
/// </summary>
public sealed class PreparedMethodClassificationQuery
{
    internal PreparedMethodClassificationQuery(
        AssemblyInspectionSession session,
        ImmutableArray<ClassificationQuestion> questions,
        FindingSubject? findingSubject,
        ImmutableArray<ClassificationExecution> requestedExecutions,
        ImmutableArray<ClassificationRequestBinding> bindings,
        AssemblyAnalysisRequestSetOperation? operation)
    {
        Session = session;
        Questions = questions;
        FindingSubject = findingSubject;
        RequestedExecutions = requestedExecutions;
        Bindings = bindings;
        Operation = operation;
    }

    public ImmutableArray<ClassificationQuestion> Questions { get; }

    internal AssemblyInspectionSession Session { get; }

    internal FindingSubject? FindingSubject { get; }

    internal ImmutableArray<ClassificationExecution>
        RequestedExecutions
    {
        get;
    }

    internal ImmutableArray<ClassificationRequestBinding> Bindings { get; }

    internal AssemblyAnalysisRequestSetOperation? Operation { get; }
}

/// <summary>
/// Host-neutral method classification: per-analyzer questions and one
/// combined request. A session-backed request composes independent closings
/// through QuerySpace over one Method-source traversal; the PEReader overload
/// remains the direct reference path. Count and Exists never read identity
/// text, and no closing derives from another.
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
        return Execute(
            Prepare(
                session,
                questions,
                findingSubject));
    }

    /// <summary>
    /// Prepares immutable request associations and source planning against one
    /// retained session. The caller owns that session and keeps it alive while
    /// reusing the prepared query.
    /// </summary>
    public static PreparedMethodClassificationQuery Prepare(
        AssemblyInspectionSession session,
        IReadOnlyList<ClassificationQuestion> questions,
        FindingSubject? findingSubject = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        ValidateQuestions(questions);
        ImmutableArray<ClassificationQuestion> retainedQuestions =
            [.. questions];
        if (!session.HasMetadata
            || !session.InspectImage(
                MetadataFormatAdmission.AdmitImage))
        {
            return new(
                session,
                retainedQuestions,
                findingSubject,
                [],
                [],
                null);
        }

        bool finding = findingSubject is not null;
        ImmutableArray<ClassificationExecution> requestedExecutions =
            [.. RequestedExecutions(retainedQuestions, finding)];
        var associations =
            ImmutableArray.CreateBuilder<
                MethodDefinitionSourceAssociation>();
        var bindings =
            ImmutableArray.CreateBuilder<
                ClassificationRequestBinding>();

        foreach (ClassificationExecution execution
            in requestedExecutions)
        {
            foreach (MethodClassificationAnalyzer analyzer
                in RequestedAnalyzers(
                    execution,
                    retainedQuestions,
                    finding))
            {
                Analyzer producer = ProducerFor(analyzer);
                ProducerRequest producerRequest =
                    execution.HeadCount is int count
                        ? ProducerRequest.Head(producer, count)
                        : new(
                            producer,
                            TerminalFor(execution.Closing));
                WorkDescription work =
                    ProducerPlanner.Plan([producerRequest])
                        is ProducerPlanResult.Accepted producerPlan
                            ? producerPlan.Description
                            : throw new ProducerContractException(
                                "The Method Classification request must plan.");
                MethodDefinitionSourceRequest<
                    ClosedQueryResult<ClassifiedMethodRow>> request =
                        MethodDefinitionSourceRequest<
                            ClosedQueryResult<ClassifiedMethodRow>>.Create(
                                MethodClassificationQuerySpace.CreateRequest(
                                    analyzer,
                                    execution),
                                work,
                                producer);
                MethodDefinitionSourceAssociation association =
                    MethodDefinitionSourceAssociation.Create(request);
                associations.Add(association);
                bindings.Add(
                    new(
                        execution,
                        analyzer,
                        association,
                        request));
            }
        }

        AssemblyAnalysisRequestSetOperation? operation = null;
        if (associations.Count > 0)
        {
            MethodDefinitionSourceRequestSetPlan plan =
                MethodDefinitionSourceRequestSet.Plan(
                    MethodDefinitionSourceResourceIdentity.Create(),
                    associations)
                is MethodDefinitionSourceRequestSetPlanResult.Accepted accepted
                    ? accepted.Plan
                    : throw new ProducerContractException(
                        "The Method Classification QuerySpace request set "
                        + "must plan.");
            operation = AssemblyAnalysisRequestSetOperation.Create(
                "MethodClassification",
                plan);
        }

        return new(
            session,
            retainedQuestions,
            findingSubject,
            requestedExecutions,
            bindings.ToImmutable(),
            operation);
    }

    /// <summary>
    /// Executes one prepared Method Classification request against its
    /// retained session.
    /// </summary>
    public static MethodClassificationResult Execute(
        PreparedMethodClassificationQuery prepared)
    {
        ArgumentNullException.ThrowIfNull(prepared);
        return ExecuteRequestSet(
            prepared.Session,
            prepared);
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
        ValidateQuestions(questions);

        bool finding = findingSubject is not null;

        // The legacy scan published nothing for an image it did not admit.
        if (!MetadataFormatAdmission.AdmitImage(peReader))
            return Empty(questions, findingSubject);

        // Each closing and operand is its own request and execution: planning
        // never derives Count or Exists from Rows. The Finding asks for Rows
        // of every analyzer, so it shares the Rows execution.
        List<ClassificationExecution> requestedExecutions =
            RequestedExecutions(questions, finding);

        var executions =
            new MethodDefinitionExecution[requestedExecutions.Count];
        for (int executionIndex = 0;
            executionIndex < requestedExecutions.Count;
            executionIndex++)
        {
            ClassificationExecution execution =
                requestedExecutions[executionIndex];
            var requests = new List<ProducerRequest>();
            foreach (MethodClassificationAnalyzer analyzer
                in RequestedAnalyzers(
                    execution,
                    questions,
                    finding))
            {
                Analyzer producer = ProducerFor(analyzer);
                requests.Add(execution.HeadCount is int count
                    ? ProducerRequest.Head(producer, count)
                    : new ProducerRequest(
                        producer,
                        TerminalFor(execution.Closing)));
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

        return BuildResult(
            questions,
            findingSubject,
            requestedExecutions,
            (execution, analyzer) =>
                ExecutionOf(
                    requestedExecutions,
                    executions,
                    execution)
                    .ResultOf(ProducerFor(analyzer)),
            execution =>
                ExecutionOf(
                    requestedExecutions,
                    executions,
                    execution)
                    .Receipt);
    }

    static MethodClassificationResult ExecuteRequestSet(
        AssemblyInspectionSession session,
        PreparedMethodClassificationQuery prepared)
    {
        if (prepared.Operation is not { } operation)
        {
            return Empty(
                prepared.Questions,
                prepared.FindingSubject);
        }

        AssemblyAnalysisRequestSetServiceResult serviceResult =
            session.SnapshotOperation(
                operation,
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
                return Empty(
                    prepared.Questions,
                    prepared.FindingSubject);
            }

            throw new ProducerContractException(
                $"The Method Classification request-set operation was "
                + $"rejected: {rejected.Kind}.");
        }

        MethodDefinitionSourceRequestSetExecution completed =
            ((AssemblyAnalysisRequestSetServiceResult.Completed)serviceResult)
                .Execution;
        ImmutableArray<ClassificationAssociationReceipt>
            associationReceipts =
                AssociationReceipts(prepared, completed);
        return BuildResult(
            prepared.Questions,
            prepared.FindingSubject,
            prepared.RequestedExecutions,
            (execution, analyzer) =>
            {
                ClassificationRequestBinding binding =
                    BindingOf(
                        prepared,
                        execution,
                        analyzer);
                return completed.ResultOf(
                    binding.Association,
                    binding.Request);
            },
            execution => AggregateReceipt(
                associationReceipts,
                execution),
            completed.GroupReceipts,
            associationReceipts);
    }

    static MethodClassificationResult BuildResult(
        IReadOnlyList<ClassificationQuestion> questions,
        FindingSubject? findingSubject,
        IReadOnlyList<ClassificationExecution> requestedExecutions,
        Func<
            ClassificationExecution,
            MethodClassificationAnalyzer,
            Result> resultOf,
        Func<ClassificationExecution, WorkReceipt> receiptOf,
        ImmutableArray<MethodDefinitionSourceGroupReceipt> sourceGroups =
            default,
        ImmutableArray<ClassificationAssociationReceipt>
            associationReceipts = default)
    {
        var answers =
            ImmutableArray.CreateBuilder<
                (ClassificationQuestion, ClassificationAnswer)>(
                    questions.Count);
        foreach (ClassificationQuestion question in questions)
        {
            answers.Add((
                question,
                Answer(
                    resultOf(
                        question.Execution,
                        question.Analyzer),
                    question)));
        }

        ImmutableArray<ClassifiedMethodRow> merged = default;
        FindingInspection<ClassifiedMethodObservation>? inspection = null;
        if (findingSubject is not null)
        {
            ClassificationExecution rows =
                new(ClassificationClosing.Rows);
            merged = Merge(
                analyzer => resultOf(rows, analyzer),
                out string? failure);
            inspection = merged.IsDefault
                ? new FindingInspection<ClassifiedMethodObservation>(
                    new FindingInspection<
                        ClassifiedMethodObservation>.Failed(
                            new InspectionError(
                                findingSubject,
                                MetadataFindings
                                    .ClassifiedMethodDescriptor,
                                failure!)))
                : MetadataFindings.InspectClassifiedMethods(
                    merged.Select(ToClassifiedMethodInfo),
                    findingSubject);
        }

        var receipts =
            ImmutableArray.CreateBuilder<ClassificationReceipt>(
                requestedExecutions.Count);
        CriticalFailure? critical = null;
        foreach (ClassificationExecution execution
            in requestedExecutions)
        {
            WorkReceipt receipt = receiptOf(execution);
            receipts.Add(new(
                execution,
                receipt));
            critical ??= receipt.Critical;
        }

        return new(
            answers.MoveToImmutable(),
            merged,
            inspection,
            critical,
            receipts.MoveToImmutable())
        {
            SourceGroups = sourceGroups,
            AssociationReceipts = associationReceipts,
        };
    }

    static void ValidateQuestions(
        IReadOnlyList<ClassificationQuestion> questions)
    {
        ArgumentNullException.ThrowIfNull(questions);
        foreach (ClassificationQuestion question in questions)
        {
            ArgumentNullException.ThrowIfNull(question);
            if (question.Analyzer
                    == MethodClassificationAnalyzer.PointerSignature
                && question.Order == ClassifiedRowOrder.Display)
            {
                throw new ArgumentException(
                    "Pointer-signature rows have no display order.",
                    nameof(questions));
            }
        }
    }

    static List<ClassificationExecution> RequestedExecutions(
        IReadOnlyList<ClassificationQuestion> questions,
        bool finding)
    {
        var executions = new List<ClassificationExecution>();
        if (finding)
        {
            executions.Add(
                new(ClassificationClosing.Rows));
        }

        foreach (ClassificationClosing closing
            in Enum.GetValues<ClassificationClosing>())
        {
            foreach (ClassificationQuestion question in questions)
            {
                if (question.Closing == closing
                    && !executions.Contains(question.Execution))
                {
                    executions.Add(question.Execution);
                }
            }
        }

        return executions;
    }

    static List<MethodClassificationAnalyzer> RequestedAnalyzers(
        ClassificationExecution execution,
        IReadOnlyList<ClassificationQuestion> questions,
        bool finding)
    {
        var analyzers = new List<MethodClassificationAnalyzer>();
        foreach (MethodClassificationAnalyzer analyzer
            in Enum.GetValues<MethodClassificationAnalyzer>())
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
                analyzers.Add(analyzer);
        }

        return analyzers;
    }

    static ClassificationRequestBinding BindingOf(
        PreparedMethodClassificationQuery prepared,
        ClassificationExecution execution,
        MethodClassificationAnalyzer analyzer)
    {
        foreach (ClassificationRequestBinding binding
            in prepared.Bindings)
        {
            if (binding.Execution == execution
                && binding.Analyzer == analyzer)
            {
                return binding;
            }
        }

        throw new InvalidOperationException(
            $"The Method Classification association "
            + $"'{analyzer}/{execution}' was not prepared.");
    }

    static ImmutableArray<ClassificationAssociationReceipt>
        AssociationReceipts(
            PreparedMethodClassificationQuery prepared,
            MethodDefinitionSourceRequestSetExecution completed)
    {
        var receipts =
            ImmutableArray.CreateBuilder<
                ClassificationAssociationReceipt>(
                    prepared.Bindings.Length);
        foreach (ClassificationRequestBinding binding
            in prepared.Bindings)
        {
            receipts.Add(
                new(
                    binding.Execution,
                    binding.Analyzer,
                    completed.ResultOf(
                        binding.Association)
                        .WorkReceipt));
        }

        return receipts.MoveToImmutable();
    }

    static WorkReceipt AggregateReceipt(
        ImmutableArray<ClassificationAssociationReceipt> receipts,
        ClassificationExecution execution)
    {
        var producers =
            ImmutableArray.CreateBuilder<ProducerParticipation>();
        int unitsVisited = 0;
        CriticalFailure? critical = null;
        bool identityBudgetArmed = false;
        long identityWorkCharged = 0;
        long signatureShapeNodesWalked = 0;
        foreach (ClassificationAssociationReceipt association
            in receipts)
        {
            if (association.Execution != execution)
                continue;

            WorkReceipt receipt = association.Receipt;
            unitsVisited = Math.Max(
                unitsVisited,
                receipt.UnitsVisited);
            producers.AddRange(receipt.Producers);
            critical ??= receipt.Critical;
            identityBudgetArmed |= receipt.IdentityBudgetArmed;
            identityWorkCharged += receipt.IdentityWorkCharged;
            signatureShapeNodesWalked +=
                receipt.SignatureShapeNodesWalked;
        }

        if (producers.Count == 0)
        {
            throw new InvalidOperationException(
                $"The Method Classification execution "
                + $"'{execution}' has no association receipts.");
        }

        return new(
            unitsVisited,
            producers.ToImmutable())
        {
            Critical = critical,
            IdentityBudgetArmed = identityBudgetArmed,
            IdentityWorkCharged = identityWorkCharged,
            SignatureShapeNodesWalked =
                signatureShapeNodesWalked,
        };
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
            MethodClassificationAnalyzer.Extension => ExtensionMethodAnalyzer.Instance,
            _ => throw new ArgumentOutOfRangeException(nameof(analyzer)),
        };

    static ProducerTerminal TerminalFor(ClassificationClosing closing) =>
        closing switch
        {
            ClassificationClosing.Rows or ClassificationClosing.Head =>
                ProducerTerminal.Rows,
            ClassificationClosing.Count => ProducerTerminal.Count,
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
        Func<MethodClassificationAnalyzer, Result> resultOf,
        out string? failure)
    {
        failure = null;
        var rows = new List<ClassifiedMethodRow>();
        foreach (MethodClassificationAnalyzer analyzer in MergedAnalyzers)
        {
            Analyzer producer = ProducerFor(analyzer);
            Result result = resultOf(analyzer);
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
