using System.Collections.Immutable;

using DotnetInspector.Presentation;
using DotnetInspector.Views;
using ILInspector.Findings;
using ILInspector.Text;
using Markout;

namespace DotnetInspector.Output;

internal static class SourceTextDiffRenderer
{
    static readonly FindingSubject Subject = new("source.text", "Source text");

    public static string CreateUnifiedDiff(
        string? before,
        string? after,
        string beforeLabel,
        string afterLabel,
        bool detailed = false)
        => CreateOutput(before, after, beforeLabel, afterLabel, detailed).Content;

    public static SourceDiffOutput CreateOutput(
        string? before,
        string? after,
        string beforeLabel,
        string afterLabel,
        bool detailed = false)
    {
        ArgumentNullException.ThrowIfNull(beforeLabel);
        ArgumentNullException.ThrowIfNull(afterLabel);

        if (before is null)
        {
            return new SourceDiffOutput(
                $"{beforeLabel} unavailable; source diff requires both {beforeLabel} and {afterLabel}.");
        }
        if (after is null)
        {
            return new SourceDiffOutput(
                $"{afterLabel} unavailable; source diff requires both {beforeLabel} and {afterLabel}.");
        }

        AnalysisDiff<string> analysis =
            TextFindings.CreateAnalysisDiff(before, after, Subject);
        SourceTextDiffStatistics statistics = SourceTextDiffStatistics.Create(analysis);
        if (!statistics.HasDifferences)
            return new SourceDiffOutput($"{beforeLabel} and {afterLabel} are identical.", analysis);

        MappedTextDiff mapped = TextAnalysisDiffPresentation.CreateMappedTextDiff(
            analysis,
            beforeLabel,
            FinalLineTerminator(before),
            afterLabel,
            FinalLineTerminator(after));
        return detailed
            ? SourceDiffOutput.CreateDetailed(analysis, mapped)
            : SourceDiffOutput.CreateSummary(
                analysis,
                mapped,
                CreateSummaryFields(statistics, beforeLabel, afterLabel));
    }

    static ImmutableArray<MarkoutField> CreateSummaryFields(
        SourceTextDiffStatistics statistics,
        string beforeLabel,
        string afterLabel)
        =>
        [
            new MarkoutField("Added lines", statistics.Added.ToString()),
            new MarkoutField("Removed lines", statistics.Removed.ToString()),
            new MarkoutField(
                "Changed lines",
                $"{statistics.ChangedBefore} {beforeLabel} -> "
                + $"{statistics.ChangedAfter} {afterLabel}"),
            new MarkoutField(
                "Moved lines",
                $"{statistics.MovedBefore} {beforeLabel} -> "
                + $"{statistics.MovedAfter} {afterLabel}"),
        ];

    static TextDiffLineTerminator FinalLineTerminator(string text)
        => text.Length == 0
            ? TextDiffLineTerminator.Unknown
            : text[^1] is '\r' or '\n'
                ? TextDiffLineTerminator.Present
                : TextDiffLineTerminator.Absent;

    readonly record struct SourceTextDiffStatistics(
        int Added,
        int Removed,
        int ChangedBefore,
        int ChangedAfter,
        int MovedBefore,
        int MovedAfter)
    {
        public bool HasDifferences =>
            Added > 0
            || Removed > 0
            || ChangedBefore > 0
            || ChangedAfter > 0
            || MovedBefore > 0
            || MovedAfter > 0;

        public static SourceTextDiffStatistics Create(AnalysisDiff<string> analysis)
        {
            int added = 0;
            int removed = 0;
            int changedBefore = 0;
            int changedAfter = 0;
            int movedBefore = 0;
            int movedAfter = 0;

            foreach (AnalysisDiffRelation relation in analysis.Relations)
            {
                switch (relation)
                {
                    case AnalysisDiffRelation.Addition addition:
                        added += addition.AfterCoordinates.Length;
                        break;

                    case AnalysisDiffRelation.Removal removal:
                        removed += removal.BeforeCoordinates.Length;
                        break;

                    case AnalysisDiffRelation.Correspondence correspondence:
                        if (correspondence.Content == AnalysisDiffContentKind.Changed)
                        {
                            changedBefore += correspondence.BeforeCoordinates.Length;
                            changedAfter += correspondence.AfterCoordinates.Length;
                        }
                        if (correspondence.Placement == AnalysisDiffPlacementKind.Moved)
                        {
                            movedBefore += correspondence.BeforeCoordinates.Length;
                            movedAfter += correspondence.AfterCoordinates.Length;
                        }
                        break;

                    default:
                        throw new InvalidOperationException(
                            "Source-text analysis contains an unknown relation kind.");
                }
            }

            return new SourceTextDiffStatistics(
                added,
                removed,
                changedBefore,
                changedAfter,
                movedBefore,
                movedAfter);
        }
    }
}
