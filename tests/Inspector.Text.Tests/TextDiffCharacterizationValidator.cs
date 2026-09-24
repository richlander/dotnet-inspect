using System.Text;

using Inspector.Findings;

namespace Inspector.Text.Tests;

/// <summary>
/// An independent validator for <see cref="TextDiffCharacterization"/>. It recomputes every
/// checkable fact from the texts and the diff without using the characterizer's internals, so a
/// characterizer defect can't hide itself. This is the soundness gate named by the text
/// whitespace and move characterization designs.
/// </summary>
static class TextDiffCharacterizationValidator
{
    public static void Validate(
        TextDiffCharacterization characterization,
        AnalysisDiff<string> diff,
        string beforeText,
        string afterText)
    {
        var before = Lines.Parse(beforeText);
        var after = Lines.Parse(afterText);

        var anchors = diff.Relations
            .OfType<AnalysisDiffRelation.Correspondence>()
            .Where(relation => relation is
            {
                Content: AnalysisDiffContentKind.Unchanged,
                Placement: AnalysisDiffPlacementKind.Stable,
                BeforeCoordinates.Length: 1,
                AfterCoordinates.Length: 1,
            })
            .Select(relation => (Before: relation.BeforeCoordinates[0], After: relation.AfterCoordinates[0]))
            .OrderBy(anchor => anchor.Before)
            .ToList();
        foreach (var anchor in anchors)
            Require(before.Content(anchor.Before) == after.Content(anchor.After), "anchor content differs");

        // Regions are the maximal non-anchor runs between anchors.
        var expectedRegions = new List<(int B0, int B1, int A0, int A1, int BS, int BE, int AS, int AE, bool EndsText)>();
        for (int index = 0; index <= anchors.Count; index++)
        {
            int b0 = index == 0 ? 0 : anchors[index - 1].Before + 1;
            int a0 = index == 0 ? 0 : anchors[index - 1].After + 1;
            int b1 = index == anchors.Count ? before.Count : anchors[index].Before;
            int a1 = index == anchors.Count ? after.Count : anchors[index].After;
            if (b0 == b1 && a0 == a1)
                continue;
            expectedRegions.Add((
                b0, b1, a0, a1,
                index == 0 ? 0 : before.ContentEnd(anchors[index - 1].Before),
                index == anchors.Count ? beforeText.Length : before.Start(anchors[index].Before),
                index == 0 ? 0 : after.ContentEnd(anchors[index - 1].After),
                index == anchors.Count ? afterText.Length : after.Start(anchors[index].After),
                index == anchors.Count));
        }

        Require(characterization.Regions.Length == expectedRegions.Count, "region count differs");

        var moveEndsBefore = characterization.Moves.ToDictionary(move => move.Id, move => move.Before);
        var moveEndsAfter = characterization.Moves.ToDictionary(move => move.Id, move => move.After);
        var seenMoveIds = new List<int>();
        var moveChangeCounts = new Dictionary<int, int>();

        for (int r = 0; r < expectedRegions.Count; r++)
        {
            var expected = expectedRegions[r];
            TextRegion region = characterization.Regions[r];
            Require(region.Before == new TextLineRange(expected.B0, expected.B1 - expected.B0), "region Before range");
            Require(region.After == new TextLineRange(expected.A0, expected.A1 - expected.A0), "region After range");

            bool holdsMoveEnd = region.Changes.Any(change => change.Outcome == TextChangeOutcome.Moved);
            string beforeRegion = beforeText[expected.BS..expected.BE];
            string afterRegion = afterText[expected.AS..expected.AE];
            if (!holdsMoveEnd)
                Require(beforeRegion != afterRegion, "a region without a move end has identical texts");

            TextRegionOutcome regionOutcome = !holdsMoveEnd && Strip(beforeRegion) == Strip(afterRegion)
                ? TextRegionOutcome.WhitespaceOnly
                : TextRegionOutcome.Changed;
            Require(region.Outcome == regionOutcome, "region outcome");
            if (regionOutcome == TextRegionOutcome.WhitespaceOnly)
                Require(region.Changes.Length == 1, "a whitespace-only region is exactly one change");
            else
                Require(region.Changes.Any(change => change.Outcome != TextChangeOutcome.WhitespaceOnly), "a changed region holds a changed or moved change");

            // The partition is ordered, non-overlapping, and complete on both sides.
            int bCursor = expected.B0;
            int aCursor = expected.A0;
            foreach (TextChange change in region.Changes)
            {
                Require(change.Before.Start == bCursor && change.After.Start == aCursor, "changes are contiguous and ordered");
                Require(change.Before.Count + change.After.Count > 0, "a change holds at least one line");
                bCursor = change.Before.End;
                aCursor = change.After.End;
            }

            Require(bCursor == expected.B1 && aCursor == expected.A1, "changes cover the region");

            // Change texts are consecutive cuts of the region texts.
            int count = region.Changes.Length;
            var bStarts = new int[count + 1];
            var aStarts = new int[count + 1];
            bStarts[count] = expected.BE;
            aStarts[count] = expected.AE;
            for (int c = count - 1; c >= 0; c--)
            {
                TextChange change = region.Changes[c];
                bStarts[c] = change.Before.Count > 0 ? before.Start(change.Before.Start) : bStarts[c + 1];
                aStarts[c] = change.After.Count > 0 ? after.Start(change.After.Start) : aStarts[c + 1];
            }

            bStarts[0] = expected.BS;
            aStarts[0] = expected.AS;

            for (int c = 0; c < count; c++)
            {
                TextChange change = region.Changes[c];
                if (change.Outcome == TextChangeOutcome.Moved)
                {
                    int id = change.MoveId ?? throw new InvalidOperationException("moved change without id");
                    if (!seenMoveIds.Contains(id))
                        seenMoveIds.Add(id);
                    moveChangeCounts[id] = moveChangeCounts.GetValueOrDefault(id) + 1;
                    bool isRemoval = change.After.Count == 0 && change.Before == moveEndsBefore[id];
                    bool isAddition = change.Before.Count == 0 && change.After == moveEndsAfter[id];
                    Require(isRemoval || isAddition, "a moved change is exactly one move end");
                    continue;
                }

                Require(change.MoveId is null, "only moved changes carry ids");
                string beforeCut = beforeText[bStarts[c]..bStarts[c + 1]];
                string afterCut = afterText[aStarts[c]..aStarts[c + 1]];
                Require(beforeCut != afterCut, "a change never has identical texts");
                TextChangeOutcome outcome = Strip(beforeCut) == Strip(afterCut)
                    ? TextChangeOutcome.WhitespaceOnly
                    : TextChangeOutcome.Changed;
                Require(change.Outcome == outcome, "change outcome");
                if (outcome == TextChangeOutcome.WhitespaceOnly)
                    RequireEditsReplay(change.Edits, beforeText, bStarts[c], bStarts[c + 1], afterText, aStarts[c], aStarts[c + 1], before, after);
                else
                    Require(change.Edits.IsEmpty, "a changed change issues no edits");

                if (c > 0 && region.Changes[c - 1].Outcome != TextChangeOutcome.Moved)
                    Require(region.Changes[c - 1].Outcome != change.Outcome, "adjacent non-moved changes differ in outcome");
            }
        }

        // Every move id is on exactly two changes, and ids follow first appearance.
        Require(seenMoveIds.SequenceEqual(Enumerable.Range(1, seenMoveIds.Count)), "move ids follow first appearance");
        Require(seenMoveIds.Count == characterization.Moves.Length, "every move appears");
        foreach (TextMove move in characterization.Moves)
        {
            Require(moveChangeCounts.GetValueOrDefault(move.Id) == 2, "a move id is on exactly two changes");
            string beforeEnd = string.Join('\n', Enumerable.Range(move.Before.Start, move.Before.Count).Select(before.Content));
            string afterEnd = string.Join('\n', Enumerable.Range(move.After.Start, move.After.Count).Select(after.Content));
            TextMoveContent content = beforeEnd == afterEnd
                ? TextMoveContent.Unchanged
                : Strip(beforeEnd) == Strip(afterEnd) ? TextMoveContent.WhitespaceOnly : TextMoveContent.Changed;
            Require(move.Content == content, "move content");
            Require(Strip(beforeEnd).Length > 0, "a move holds content");
        }

        TextDocumentOutcome summary = beforeText == afterText
            ? TextDocumentOutcome.NoDifference
            : characterization.Regions.Any(region => region.Outcome == TextRegionOutcome.Changed)
                ? TextDocumentOutcome.Changed
                : TextDocumentOutcome.WhitespaceOnly;
        Require(characterization.Summary == summary, "document summary");
        if (summary == TextDocumentOutcome.WhitespaceOnly)
            Require(Strip(beforeText) == Strip(afterText), "a whitespace-only summary implies whitespace-only texts");
    }

    /// <summary>
    /// Replays the edits over the Before cut: substituting each edit's After content for its
    /// Before range must reproduce the After cut, and edits touch only whitespace.
    /// </summary>
    static void RequireEditsReplay(
        IReadOnlyList<TextWhitespaceEdit> edits,
        string beforeText,
        int beforeStart,
        int beforeEnd,
        string afterText,
        int afterStart,
        int afterEnd,
        Lines before,
        Lines after)
    {
        Require(edits.Count > 0, "a whitespace-only change issues edits");
        var rebuilt = new StringBuilder();
        int cursor = beforeStart;
        foreach (TextWhitespaceEdit edit in edits)
        {
            int b0 = before.Offset(edit.BeforeStart);
            int b1 = before.Offset(edit.BeforeEnd);
            int a0 = after.Offset(edit.AfterStart);
            int a1 = after.Offset(edit.AfterEnd);
            Require(b0 >= cursor && b1 <= beforeEnd && a0 >= afterStart && a1 <= afterEnd, "edits lie inside the change in order");
            Require(beforeText[b0..b1].All(TextWhitespace.IsWhitespace), "edit Before range is whitespace");
            Require(afterText[a0..a1].All(TextWhitespace.IsWhitespace), "edit After range is whitespace");
            Require(beforeText[b0..b1] != afterText[a0..a1], "an edit changes something");
            Require(edit.Kinds != TextWhitespaceEditKinds.None, "an edit has a kind");
            rebuilt.Append(beforeText, cursor, b0 - cursor);
            rebuilt.Append(afterText, a0, a1 - a0);
            cursor = b1;
        }

        rebuilt.Append(beforeText, cursor, beforeEnd - cursor);
        Require(rebuilt.ToString() == afterText[afterStart..afterEnd], "edits replay to the After text");
    }

    static string Strip(string text)
        => new(text.Where(character => !TextWhitespace.IsWhitespace(character)).ToArray());

    static void Require(bool condition, string property)
    {
        if (!condition)
            throw new Xunit.Sdk.XunitException($"Characterization violates: {property}.");
    }

    /// <summary>An independent logical-line parser: CRLF, CR, and LF; no final empty line.</summary>
    sealed class Lines
    {
        readonly string _text;
        readonly List<(int Start, int ContentEnd)> _lines = [];

        Lines(string text)
        {
            _text = text;
            int start = 0;
            int index = 0;
            while (index < text.Length)
            {
                if (text[index] == '\r' || text[index] == '\n')
                {
                    _lines.Add((start, index));
                    index += text[index] == '\r' && index + 1 < text.Length && text[index + 1] == '\n' ? 2 : 1;
                    start = index;
                }
                else
                {
                    index++;
                }
            }

            if (start < text.Length)
                _lines.Add((start, text.Length));
        }

        public static Lines Parse(string text) => new(text);

        public int Count => _lines.Count;

        public int Start(int line) => _lines[line].Start;

        public int ContentEnd(int line) => _lines[line].ContentEnd;

        public string Content(int line) => _text[_lines[line].Start.._lines[line].ContentEnd];

        public int Offset(TextPosition position)
            => position.Line == _lines.Count ? _text.Length : _lines[position.Line].Start + position.Column;
    }
}
