using DotnetInspector.Queries;
using Inspector.Findings;
using Markout;

namespace DotnetInspector.Presentation;

/// <summary>
/// The host-neutral analytical and mapped presentation of one authored Source version pair.
/// </summary>
public sealed record MemberSourcePairDiffPresentation(
    AssemblyMemberSourcePairResult Pair,
    AssemblyMemberSourcePairEndpoint.Resolved Before,
    AssemblyMemberSourcePairEndpoint.Resolved After,
    string BeforeText,
    string AfterText,
    AnalysisDiff<string> Analysis,
    MemberSourceDiffStatistics Statistics,
    MappedTextDiff Diff);

/// <summary>The closed outcome of presenting one authored Source version pair.</summary>
public abstract record MemberSourcePairDiffPresentationResult(
    AssemblyMemberSourcePairResult Pair)
{
    public sealed record Available(MemberSourcePairDiffPresentation Presentation)
        : MemberSourcePairDiffPresentationResult(Presentation.Pair);

    public sealed record Unavailable(AssemblyMemberSourcePairResult Pair)
        : MemberSourcePairDiffPresentationResult(Pair);

    public sealed record Failed(
        AssemblyMemberSourcePairResult Pair,
        string Detail)
        : MemberSourcePairDiffPresentationResult(Pair);
}

/// <summary>Creates the shared authored Source version-pair presentation.</summary>
public static class MemberSourcePairDiffPresentationAdapter
{
    public const string BeforeLabel = "Before authored Source";
    public const string AfterLabel = "After authored Source";

    public static MemberSourcePairDiffPresentationResult Create(
        AssemblyMemberSourcePairResult pair)
    {
        ArgumentNullException.ThrowIfNull(pair);

        if (pair.Status == AssemblyMemberSourcePairStatus.Failed)
        {
            return new MemberSourcePairDiffPresentationResult.Failed(
                pair,
                pair.Failure?.Detail
                    ?? pair.Comparison?.Failure
                    ?? "Authored Source comparison failed.");
        }
        if (pair.Status != AssemblyMemberSourcePairStatus.Compared
            || pair.Comparison is not FindingComparison<string>.Complete comparison
            || pair.Before is not AssemblyMemberSourcePairEndpoint.Resolved before
            || pair.After is not AssemblyMemberSourcePairEndpoint.Resolved after
            || before.Source is not AssemblyMemberPdbSourceAttempt.Available beforeSource
            || after.Source is not AssemblyMemberPdbSourceAttempt.Available afterSource)
        {
            return new MemberSourcePairDiffPresentationResult.Unavailable(pair);
        }

        string beforeText = beforeSource.Inspection.Text
            ?? throw new InvalidOperationException(
                "Available Before authored Source has no text.");
        string afterText = afterSource.Inspection.Text
            ?? throw new InvalidOperationException(
                "Available After authored Source has no text.");

        try
        {
            AnalysisDiff<string> analysis =
                TextAnalysisDiffPresentation.CreateAnalysisDiff(
                    comparison,
                    beforeText,
                    afterText);
            MemberSourceDiffStatistics statistics =
                MemberSourceDiffStatistics.Create(analysis);
            MappedTextDiff diff =
                TextAnalysisDiffPresentation.CreateMappedTextDiff(
                    analysis,
                    BeforeLabel,
                    TextDiffLineTerminator.Absent,
                    AfterLabel,
                    TextDiffLineTerminator.Absent);
            return new MemberSourcePairDiffPresentationResult.Available(
                new(
                    pair,
                    before,
                    after,
                    beforeText,
                    afterText,
                    analysis,
                    statistics,
                    diff));
        }
        catch (ArgumentException error)
        {
            return new MemberSourcePairDiffPresentationResult.Failed(
                pair,
                error.Message);
        }
    }
}
