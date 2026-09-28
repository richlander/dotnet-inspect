using System.Collections.Immutable;
using System.Reflection.PortableExecutable;

using ILInspector.Analysis.Classification;
using ILInspector.Analysis.Planning;
using ILInspector.Metadata;
using Inspector.Findings;

namespace DotnetInspector.Queries;

/// <summary>One of the three method classification analyzers.</summary>
public enum MethodClassificationAnalyzer
{
    PInvoke,
    Async,
    PointerSignature,
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
public sealed record ClassificationQuestion(
    MethodClassificationAnalyzer Analyzer,
    ClassificationClosing Closing,
    ClassifiedRowOrder Order = ClassifiedRowOrder.Metadata);

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

    /// <summary>A containment budget was exhausted; every question in the request has this answer.</summary>
    public sealed record Aborted(CriticalFailure Critical) : ClassificationAnswer;
}

/// <summary>
/// The typed result of a classification request: one answer per question, in
/// question order, and, when the Finding was requested, the merged rows in
/// legacy order and the classified-method Finding inspection built from them.
/// <see cref="Finding"/> is null only when the Finding was not requested. When
/// an analyzer failed or the execution aborted, it is a failed inspection that
/// names the analyzer and the unit, and <see cref="MergedRows"/> is default.
/// </summary>
public sealed record MethodClassificationResult(
    ImmutableArray<(ClassificationQuestion Question, ClassificationAnswer Answer)> Answers,
    ImmutableArray<ClassifiedMethodRow> MergedRows,
    FindingInspection<ClassifiedMethodObservation>? Finding,
    CriticalFailure? Critical)
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
}

/// <summary>
/// Host-neutral method classification: per-analyzer questions and one
/// combined request, run in one Producer Planning execution. Requests for the
/// same analyzer compose: if any asks for Rows, Rows runs once and every Count
/// and Exists derives from it, taking its outcome; otherwise Count and Exists
/// run alone and read no identity text. Hosts bind questions and map answers;
/// they never sort, merge, or count rows.
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

        var requests = new List<ProducerRequest>();
        foreach (MethodClassificationAnalyzer analyzer in Enum.GetValues<MethodClassificationAnalyzer>())
        {
            ProducerTerminal? terminal = finding ? ProducerTerminal.Rows : null;
            foreach (ClassificationQuestion question in questions)
            {
                if (question.Analyzer == analyzer)
                    terminal = MostExpansive(terminal, TerminalFor(question.Closing));
            }

            if (terminal is { } requested)
                requests.Add(new ProducerRequest(ProducerFor(analyzer), requested));
        }

        if (requests.Count == 0)
        {
            return new MethodClassificationResult([], default, null, null);
        }

        WorkDescription description =
            ProducerPlanner.Plan(requests) is ProducerPlanResult.Accepted accepted
                ? accepted.Description
                : throw new InvalidOperationException(
                    "The method classification request must plan.");
        MethodDefinitionExecution execution = MethodDefinitionExecution.Execute(
            description,
            "MethodClassification",
            peReader);

        var answers = ImmutableArray.CreateBuilder<(ClassificationQuestion, ClassificationAnswer)>(questions.Count);
        foreach (ClassificationQuestion question in questions)
            answers.Add((question, Answer(execution.ResultOf(ProducerFor(question.Analyzer)), question)));

        ImmutableArray<ClassifiedMethodRow> merged = default;
        FindingInspection<ClassifiedMethodObservation>? inspection = null;
        if (finding)
        {
            merged = Merge(execution, out string? failure);
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

        return new MethodClassificationResult(
            answers.MoveToImmutable(),
            merged,
            inspection,
            execution.Receipt.Critical);
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
                ClassificationClosing.Rows => new ClassificationAnswer.Rows([]),
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
            null);
    }

    static MethodDefinitionQueryProducer<TTest, TProjection, ClassifiedMethodRow> Typed<TTest, TProjection>(
        MethodDefinitionQueryProducer<TTest, TProjection, ClassifiedMethodRow> producer)
        where TTest : struct, IMethodDefinitionPredicate
        where TProjection : struct, IMethodDefinitionProjection<ClassifiedMethodRow> =>
        producer;

    static ProducerDeclaration<ClosedQueryResult<ClassifiedMethodRow>> ProducerFor(
        MethodClassificationAnalyzer analyzer) =>
        analyzer switch
        {
            MethodClassificationAnalyzer.PInvoke => PInvokeAnalyzer.Instance,
            MethodClassificationAnalyzer.Async => AsyncAnalyzer.Instance,
            MethodClassificationAnalyzer.PointerSignature => PointerSignatureAnalyzer.Instance,
            _ => throw new ArgumentOutOfRangeException(nameof(analyzer)),
        };

    static ProducerTerminal TerminalFor(ClassificationClosing closing) =>
        closing switch
        {
            ClassificationClosing.Rows => ProducerTerminal.Rows,
            ClassificationClosing.Count => ProducerTerminal.Complete,
            _ => ProducerTerminal.Exists,
        };

    static ProducerTerminal MostExpansive(ProducerTerminal? current, ProducerTerminal next) =>
        current is not { } existing ? next
        : Rank(existing) >= Rank(next) ? existing
        : next;

    static int Rank(ProducerTerminal terminal) => terminal switch
    {
        ProducerTerminal.Exists => 0,
        ProducerTerminal.Complete => 1,
        _ => 2,
    };

    static ClassificationAnswer Answer(
        ProducerResult<ClosedQueryResult<ClassifiedMethodRow>> result,
        ClassificationQuestion question)
    {
        if (result.Outcome == ProducerOutcome.Aborted)
            return new ClassificationAnswer.Aborted(result.Critical!);
        if (!result.HasValue)
            return new ClassificationAnswer.Failed(result.Failure!);

        ClosedQueryResult<ClassifiedMethodRow> value = result.Value!;
        return question.Closing switch
        {
            ClassificationClosing.Rows => new ClassificationAnswer.Rows(
                Order(value.Rows, question.Analyzer, question.Order)),
            ClassificationClosing.Count => new ClassificationAnswer.Count(value.Count),
            _ => new ClassificationAnswer.Exists(value.Exists),
        };
    }

    /// <summary>
    /// The analyzers' rows merged in legacy order: traversal order, then
    /// P/Invoke, async, pointer signature within one method. Default when an
    /// analyzer did not publish rows, with <paramref name="failure"/> naming
    /// the analyzer and the unit.
    /// </summary>
    static ImmutableArray<ClassifiedMethodRow> Merge(
        MethodDefinitionExecution execution,
        out string? failure)
    {
        failure = null;
        var rows = new List<(ClassifiedMethodRow Row, int Rank)>();
        int rank = 0;
        foreach (MethodClassificationAnalyzer analyzer in Enum.GetValues<MethodClassificationAnalyzer>())
        {
            ProducerDeclaration<ClosedQueryResult<ClassifiedMethodRow>> producer = ProducerFor(analyzer);
            ProducerResult<ClosedQueryResult<ClassifiedMethodRow>> result = execution.ResultOf(producer);
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

            foreach (ClassifiedMethodRow row in result.Value.Rows)
                rows.Add((row, rank));
            rank++;
        }

        return [.. rows
            .OrderBy(static entry => entry.Row.Ordinal)
            .ThenBy(static entry => entry.Rank)
            .Select(static entry => entry.Row)];
    }

    static ImmutableArray<ClassifiedMethodRow> Order(
        ImmutableArray<ClassifiedMethodRow> rows,
        MethodClassificationAnalyzer analyzer,
        ClassifiedRowOrder order)
    {
        string key = (order, analyzer) switch
        {
            (ClassifiedRowOrder.Metadata, _) => ClassifiedMethodRowOrders.Metadata,
            (ClassifiedRowOrder.Model, MethodClassificationAnalyzer.Async) => ClassifiedMethodRowOrders.AsyncModel,
            (ClassifiedRowOrder.Model, _) => ClassifiedMethodRowOrders.Model,
            (ClassifiedRowOrder.Display, MethodClassificationAnalyzer.Async) => ClassifiedMethodRowOrders.AsyncDisplay,
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
