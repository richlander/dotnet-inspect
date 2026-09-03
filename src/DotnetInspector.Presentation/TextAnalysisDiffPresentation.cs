using ILInspector.Findings;
using Markout;

namespace DotnetInspector.Presentation;

/// <summary>Host-neutral presentation lowering for analytical text diffs.</summary>
public static class TextAnalysisDiffPresentation
{
    /// <summary>
    /// Lowers producer-issued analytical line relations into a conventional mapped text diff.
    /// Stable unchanged one-to-one correspondences become anchors; every other relation is
    /// intentionally presented as removal and addition text.
    /// </summary>
    public static MappedTextDiff CreateMappedTextDiff(
        AnalysisDiff<string> analysis,
        string beforeLabel,
        TextDiffLineTerminator beforeTerminator,
        string afterLabel,
        TextDiffLineTerminator afterTerminator)
    {
        ArgumentNullException.ThrowIfNull(analysis);
        ArgumentNullException.ThrowIfNull(beforeLabel);
        ArgumentNullException.ThrowIfNull(afterLabel);

        var anchors = analysis.Relations
            .OfType<AnalysisDiffRelation.Correspondence>()
            .Where(relation =>
                relation.Content == AnalysisDiffContentKind.Unchanged
                && relation.Placement == AnalysisDiffPlacementKind.Stable
                && relation.BeforeCoordinates.Length == 1
                && relation.AfterCoordinates.Length == 1)
            .Select(relation => (
                Before: relation.BeforeCoordinates[0],
                After: relation.AfterCoordinates[0]))
            .OrderBy(anchor => anchor.Before)
            .ToArray();

        var changes = new List<TextDiffChange>();
        int beforePosition = 0;
        int afterPosition = 0;
        foreach ((int beforeAnchor, int afterAnchor) in anchors)
        {
            if (beforeAnchor < beforePosition || afterAnchor < afterPosition)
            {
                throw new InvalidOperationException(
                    "Stable text-diff anchors must preserve endpoint order.");
            }

            AddChange(
                changes,
                beforePosition,
                beforeAnchor,
                afterPosition,
                afterAnchor);
            beforePosition = beforeAnchor + 1;
            afterPosition = afterAnchor + 1;
        }
        AddChange(
            changes,
            beforePosition,
            analysis.Before.Length,
            afterPosition,
            analysis.After.Length);

        return new MappedTextDiff(
            new TextDiffSequence(analysis.Before, beforeLabel, beforeTerminator),
            new TextDiffSequence(analysis.After, afterLabel, afterTerminator),
            changes);
    }

    static void AddChange(
        List<TextDiffChange> changes,
        int beforeStart,
        int beforeEnd,
        int afterStart,
        int afterEnd)
    {
        int beforeCount = beforeEnd - beforeStart;
        int afterCount = afterEnd - afterStart;
        if (beforeCount == 0 && afterCount == 0)
            return;

        changes.Add(new TextDiffChange(
            new TextDiffRange(beforeStart, beforeCount),
            new TextDiffRange(afterStart, afterCount)));
    }
}
