using System.Collections.Immutable;

using DotnetInspector.Presentation;
using DotnetInspector.Views;
using Markout;

namespace DotnetInspector.Output;

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
                    .. CreateSummaryFields(statistics),
                ]);
        }

        return detailed
            ? SourceDiffOutput.CreateDetailed(
                presentation.Analysis,
                presentation.Diff)
            : SourceDiffOutput.CreateSummary(
                presentation.Analysis,
                presentation.Diff,
                CreateSummaryFields(statistics));
    }

    static ImmutableArray<MarkoutField> CreateSummaryFields(
        MemberSourceDiffStatistics statistics)
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
        ];
}
