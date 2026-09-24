using Inspector.Text;
using System.Collections.Immutable;

using DotnetInspector.Presentation;
using DotnetInspect.Cli.Views;
using Markout;

namespace DotnetInspect.Cli.Output;

internal static class SourceTextDiffRenderer
{
    public static SourceDiffOutput CreateOutput(
        MemberSourceDiffPresentation presentation,
        bool detailed = false)
    {
        ArgumentNullException.ThrowIfNull(presentation);
        MemberSourceDiffStatistics statistics = presentation.Statistics;
        if (!statistics.HasDifferences)
        {
            return SourceDiffOutput.CreateSummary(
                presentation.Analysis,
                presentation.Diff,
                [
                    new MarkoutField(
                        "Status",
                        $"{MemberSourceDiffPresentationAdapter.BeforeLabel} and "
                        + $"{MemberSourceDiffPresentationAdapter.AfterLabel} are identical."),
                    .. CreateSummaryFields(statistics, presentation.Characterization),
                ]);
        }

        return detailed
            ? SourceDiffOutput.CreateDetailed(
                presentation.Analysis,
                presentation.Diff)
            : SourceDiffOutput.CreateSummary(
                presentation.Analysis,
                presentation.Diff,
                CreateSummaryFields(statistics, presentation.Characterization));
    }

    static ImmutableArray<MarkoutField> CreateSummaryFields(
        MemberSourceDiffStatistics statistics,
        TextDiffCharacterization? characterization)
        =>
        [
            new MarkoutField("Added lines", statistics.Added.ToString()),
            new MarkoutField("Removed lines", statistics.Removed.ToString()),
            new MarkoutField(
                "Changed lines",
                $"{statistics.ChangedBefore} "
                + $"{MemberSourceDiffPresentationAdapter.BeforeLabel} -> "
                + $"{statistics.ChangedAfter} "
                + MemberSourceDiffPresentationAdapter.AfterLabel),
            new MarkoutField(
                "Moved lines",
                $"{statistics.MovedBefore} "
                + $"{MemberSourceDiffPresentationAdapter.BeforeLabel} -> "
                + $"{statistics.MovedAfter} "
                + MemberSourceDiffPresentationAdapter.AfterLabel),
        .. CharacterizationFields(characterization),
        ];

    static IEnumerable<MarkoutField> CharacterizationFields(TextDiffCharacterization? characterization)
    {
        if (characterization is null)
            yield break;

        TextChange[] whitespace = [.. characterization.Regions
            .SelectMany(region => region.Changes)
            .Where(change => change.Outcome == TextChangeOutcome.WhitespaceOnly)];
        yield return new MarkoutField(
            "Whitespace-only lines",
            $"{whitespace.Sum(change => change.Before.Count)} "
            + $"{MemberSourceDiffPresentationAdapter.BeforeLabel} -> "
            + $"{whitespace.Sum(change => change.After.Count)} "
            + MemberSourceDiffPresentationAdapter.AfterLabel);
        yield return new MarkoutField("Moved blocks", characterization.Moves.Length.ToString());
    }
}
