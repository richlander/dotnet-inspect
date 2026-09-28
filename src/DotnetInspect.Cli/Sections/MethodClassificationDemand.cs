using System.Collections.Immutable;
using DotnetInspector.Queries;

namespace DotnetInspect.Cli.Sections;

/// <summary>
/// The CLI's typed result of the method classification request: the
/// host-neutral answers, or the failure to acquire the image they read.
/// </summary>
public abstract record MethodClassificationBindingResult
{
    private MethodClassificationBindingResult()
    {
    }

    public sealed record Available(MethodClassificationResult Result)
        : MethodClassificationBindingResult;

    public sealed record Failed(Exception Error) : MethodClassificationBindingResult;
}

/// <summary>
/// What each library consumer asks of the method classification analyzers,
/// as owned by <c>docs/design/method-classification-analyzers.md#queries-and-demand</c>.
/// Each consumer is a query definition that a section or the command demands,
/// and each asks only for what it shows: summaries ask for counts, and the
/// row sections ask for rows, or a count under <c>--count</c>. The requested
/// consumers' questions run as one <see cref="MethodClassificationQuery"/>
/// request, which runs each closing as its own execution.
/// </summary>
public static class MethodClassificationDemand
{
    /// <summary>Library Info shows the async method count.</summary>
    public static InspectionQuery<MethodClassificationBindingResult> LibraryInfo { get; } =
        new("Method classification (Library Info counts)", InspectionCost.NetworkFree);

    /// <summary>Signals show the public pointer-signature and P/Invoke counts.</summary>
    public static InspectionQuery<MethodClassificationBindingResult> Signals { get; } =
        new("Method classification (Signals counts)", InspectionCost.NetworkFree);

    /// <summary>The Async Methods section: its rows, or its count under <c>--count</c>.</summary>
    public static InspectionQuery<MethodClassificationBindingResult> AsyncMethods { get; } =
        new("Method classification (Async Methods)", InspectionCost.NetworkFree);

    /// <summary>The P/Invoke Methods section: its rows, or its count under <c>--count</c>.</summary>
    public static InspectionQuery<MethodClassificationBindingResult> PInvokeMethods { get; } =
        new("Method classification (P/Invoke Methods)", InspectionCost.NetworkFree);

    /// <summary>
    /// The <c>--json</c> model dump: every analyzer's count. Lists appear only
    /// when a row section asks for them.
    /// </summary>
    public static InspectionQuery<MethodClassificationBindingResult> ModelCounts { get; } =
        new("Method classification (model counts)", InspectionCost.NetworkFree);

    public static ImmutableArray<InspectionQuery<MethodClassificationBindingResult>> All { get; } =
        [LibraryInfo, Signals, AsyncMethods, PInvokeMethods, ModelCounts];

    /// <summary>
    /// Every consumer's questions, for a caller that fills the whole model:
    /// every count and the row sections' rows.
    /// </summary>
    public static IReadOnlyList<ClassificationQuestion> AllQuestions { get; } =
        QuestionsFor(All, countOnly: false);

    /// <summary>
    /// The distinct questions the <paramref name="demands"/> ask, in a stable
    /// order. <paramref name="countOnly"/> is <c>--count</c>: a row section
    /// then asks for its count instead of its rows.
    /// </summary>
    public static IReadOnlyList<ClassificationQuestion> QuestionsFor(
        IEnumerable<InspectionQueryDefinition> demands,
        bool countOnly)
    {
        var questions = new List<ClassificationQuestion>();
        foreach (InspectionQuery<MethodClassificationBindingResult> demand in All)
        {
            if (!demands.Contains(demand))
                continue;

            foreach (ClassificationQuestion question in QuestionsOf(demand, countOnly))
            {
                if (!questions.Contains(question))
                    questions.Add(question);
            }
        }

        return questions;
    }

    static IEnumerable<ClassificationQuestion> QuestionsOf(
        InspectionQueryDefinition demand,
        bool countOnly)
    {
        if (demand == LibraryInfo)
            return [Count(MethodClassificationAnalyzer.Async)];
        if (demand == Signals)
            return [Count(MethodClassificationAnalyzer.PointerSignature), Count(MethodClassificationAnalyzer.PInvoke)];
        if (demand == AsyncMethods)
            return countOnly ? [Count(MethodClassificationAnalyzer.Async)] : Rows(MethodClassificationAnalyzer.Async);
        if (demand == PInvokeMethods)
            return countOnly ? [Count(MethodClassificationAnalyzer.PInvoke)] : Rows(MethodClassificationAnalyzer.PInvoke);
        if (demand == ModelCounts)
        {
            return
            [
                Count(MethodClassificationAnalyzer.PInvoke),
                Count(MethodClassificationAnalyzer.Async),
                Count(MethodClassificationAnalyzer.PointerSignature),
            ];
        }

        throw new ArgumentOutOfRangeException(nameof(demand));
    }

    static ClassificationQuestion Count(MethodClassificationAnalyzer analyzer) =>
        new(analyzer, ClassificationClosing.Count);

    // The model list (JSON) and the Markdown view each read the rows in their
    // own order; both orders come from the one Rows execution.
    static ClassificationQuestion[] Rows(MethodClassificationAnalyzer analyzer) =>
    [
        new(analyzer, ClassificationClosing.Rows, ClassifiedRowOrder.Model),
        new(analyzer, ClassificationClosing.Rows, ClassifiedRowOrder.Display),
    ];
}
