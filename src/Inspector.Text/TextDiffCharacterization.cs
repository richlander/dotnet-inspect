using System.Collections.Immutable;

using Inspector.Findings;

namespace Inspector.Text;

/// <summary>A zero-based, half-open run of logical lines on one side of a diff.</summary>
public readonly record struct TextLineRange(int Start, int Count)
{
    public int End => Start + Count;
}

/// <summary>The outcome of one region between anchors, computed from the region's own texts.</summary>
public enum TextRegionOutcome
{
    /// <summary>The region texts differ whitespace-only and the region holds no move end.</summary>
    WhitespaceOnly,

    /// <summary>The region texts differ beyond whitespace, or the region holds a move end.</summary>
    Changed,
}

/// <summary>The outcome of one homogeneous change.</summary>
public enum TextChangeOutcome
{
    /// <summary>The change texts differ whitespace-only; its edits are issued.</summary>
    WhitespaceOnly,

    /// <summary>The whitespace-stripped change texts differ.</summary>
    Changed,

    /// <summary>The change is exactly one end of a move.</summary>
    Moved,
}

/// <summary>One homogeneous change: contiguous Before and After line runs and one outcome.</summary>
public sealed record TextChange(
    TextLineRange Before,
    TextLineRange After,
    TextChangeOutcome Outcome,
    ImmutableArray<TextWhitespaceEdit> Edits,
    int? MoveId);

/// <summary>One region: a maximal run of non-anchor lines between anchors, and its changes.</summary>
public sealed record TextRegion(
    TextLineRange Before,
    TextLineRange After,
    TextRegionOutcome Outcome,
    ImmutableArray<TextChange> Changes);

/// <summary>How a moved block's content compares between its two ends.</summary>
public enum TextMoveContent
{
    /// <summary>The ends' lines are ordinal-equal.</summary>
    Unchanged,

    /// <summary>The ends differ whitespace-only; the move's edits are issued.</summary>
    WhitespaceOnly,

    /// <summary>The ends differ beyond whitespace.</summary>
    Changed,
}

/// <summary>
/// One moved block: its document-local id, its two ends, and its content fact. Edit coordinates
/// refer to the ends' lines.
/// </summary>
public sealed record TextMove(
    int Id,
    TextLineRange Before,
    TextLineRange After,
    TextMoveContent Content,
    ImmutableArray<TextWhitespaceEdit> Edits);

/// <summary>The document-level summary of a line diff.</summary>
public enum TextDocumentOutcome
{
    /// <summary>The two texts are ordinal-equal.</summary>
    NoDifference,

    /// <summary>The texts differ and every region is whitespace-only.</summary>
    WhitespaceOnly,

    /// <summary>At least one region is changed.</summary>
    Changed,
}

/// <summary>
/// Whitespace and move facts for a completed line <see cref="AnalysisDiff{T}"/>, as defined by the
/// text whitespace characterization and text move characterization designs. The facts sit beside
/// the diff; its relations are consumed unchanged.
/// </summary>
public sealed record TextDiffCharacterization(
    TextDocumentOutcome Summary,
    ImmutableArray<TextRegion> Regions,
    ImmutableArray<TextMove> Moves)
{
    /// <summary>
    /// The largest Before-by-After line product a changed region may have for the owner to split
    /// it into homogeneous changes. A larger region is one changed change, which is sound.
    /// </summary>
    public const int SplitBudget = 400;

    /// <summary>
    /// Characterizes <paramref name="diff"/>, which must have been computed from
    /// <paramref name="beforeText"/> and <paramref name="afterText"/> under the
    /// <see cref="TextFindings"/> analysis-line model.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// The texts don't match the diff endpoints, or the diff violates an input precondition: an
    /// anchor joins lines whose content differs, stable anchors are out of order, a moved
    /// population isn't contiguous, or a region without a move end has identical texts.
    /// </exception>
    public static TextDiffCharacterization Create(
        AnalysisDiff<string> diff,
        string beforeText,
        string afterText)
    {
        ArgumentNullException.ThrowIfNull(diff);
        ArgumentNullException.ThrowIfNull(beforeText);
        ArgumentNullException.ThrowIfNull(afterText);

        var before = new TextLines(beforeText);
        var after = new TextLines(afterText);
        RequireEndpoint(before, diff.Before, nameof(beforeText));
        RequireEndpoint(after, diff.After, nameof(afterText));

        List<(int Before, int After)> anchors = CollectAnchors(diff, before, after);
        List<MoveRun> moves = CollectMoves(diff, before, after);
        var beforeMove = new int[before.Count];
        var afterMove = new int[after.Count];
        Array.Fill(beforeMove, -1);
        Array.Fill(afterMove, -1);
        for (int index = 0; index < moves.Count; index++)
        {
            for (int line = moves[index].Before.Start; line < moves[index].Before.End; line++)
                beforeMove[line] = index;
            for (int line = moves[index].After.Start; line < moves[index].After.End; line++)
                afterMove[line] = index;
        }

        var regions = ImmutableArray.CreateBuilder<TextRegion>();
        for (int anchor = 0; anchor <= anchors.Count; anchor++)
        {
            int beforeFirst = anchor == 0 ? 0 : anchors[anchor - 1].Before + 1;
            int afterFirst = anchor == 0 ? 0 : anchors[anchor - 1].After + 1;
            int beforeLast = anchor == anchors.Count ? before.Count : anchors[anchor].Before;
            int afterLast = anchor == anchors.Count ? after.Count : anchors[anchor].After;
            if (beforeFirst == beforeLast && afterFirst == afterLast)
                continue;

            var context = new RegionContext(
                before,
                after,
                new TextLineRange(beforeFirst, beforeLast - beforeFirst),
                new TextLineRange(afterFirst, afterLast - afterFirst),
                anchor == 0 ? 0 : before.ContentEndOf(anchors[anchor - 1].Before),
                anchor == anchors.Count ? beforeText.Length : before.StartOf(anchors[anchor].Before),
                anchor == 0 ? 0 : after.ContentEndOf(anchors[anchor - 1].After),
                anchor == anchors.Count ? afterText.Length : after.StartOf(anchors[anchor].After),
                EndsText: anchor == anchors.Count,
                beforeMove,
                afterMove);
            regions.Add(CharacterizeRegion(context, moves));
        }

        ImmutableArray<TextRegion> issued = AssignMoveIds(regions.ToImmutable(), moves, out var moveIds);
        var issuedMoves = ImmutableArray.CreateBuilder<TextMove>(moves.Count);
        foreach (var (index, id) in moveIds.OrderBy(pair => pair.Value).Select(pair => (pair.Key, pair.Value)))
            issuedMoves.Add(CharacterizeMove(id, moves[index], before, after));

        TextDocumentOutcome summary = beforeText == afterText
            ? TextDocumentOutcome.NoDifference
            : issued.Any(region => region.Outcome == TextRegionOutcome.Changed)
                ? TextDocumentOutcome.Changed
                : TextDocumentOutcome.WhitespaceOnly;

        return new TextDiffCharacterization(summary, issued, issuedMoves.MoveToImmutable());
    }

    static void RequireEndpoint(TextLines lines, ImmutableArray<string> sequence, string parameterName)
    {
        if (lines.Count != sequence.Length)
        {
            throw new ArgumentException(
                "The text's logical lines don't match the diff's endpoint sequence.",
                parameterName);
        }

        for (int line = 0; line < lines.Count; line++)
        {
            if (!string.Equals(lines.ContentOf(line), sequence[line], StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    "The text's logical lines don't match the diff's endpoint sequence.",
                    parameterName);
            }
        }
    }

    static List<(int Before, int After)> CollectAnchors(
        AnalysisDiff<string> diff,
        TextLines before,
        TextLines after)
    {
        var anchors = new List<(int Before, int After)>();
        foreach (AnalysisDiffRelation relation in diff.Relations)
        {
            if (relation is not AnalysisDiffRelation.Correspondence
                {
                    Content: AnalysisDiffContentKind.Unchanged,
                    Placement: AnalysisDiffPlacementKind.Stable,
                    BeforeCoordinates.Length: 1,
                    AfterCoordinates.Length: 1,
                } anchor)
            {
                continue;
            }

            int beforeLine = anchor.BeforeCoordinates[0];
            int afterLine = anchor.AfterCoordinates[0];
            if (!string.Equals(before.ContentOf(beforeLine), after.ContentOf(afterLine), StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    "An anchor joins lines whose content differs; the producer's comparison contract can't be characterized.",
                    nameof(diff));
            }

            anchors.Add((beforeLine, afterLine));
        }

        anchors.Sort(static (left, right) => left.Before.CompareTo(right.Before));
        for (int index = 1; index < anchors.Count; index++)
        {
            if (anchors[index].After <= anchors[index - 1].After)
                throw new ArgumentException("Stable anchors must preserve order on both sides.", nameof(diff));
        }

        return anchors;
    }

    static List<MoveRun> CollectMoves(AnalysisDiff<string> diff, TextLines before, TextLines after)
    {
        var moved = new List<MoveRun>();
        foreach (AnalysisDiffRelation relation in diff.Relations)
        {
            if (relation is not AnalysisDiffRelation.Correspondence
                {
                    Placement: AnalysisDiffPlacementKind.Moved,
                } correspondence)
            {
                continue;
            }

            moved.Add(new MoveRun(
                ContiguousRange(correspondence.BeforeCoordinates, nameof(diff)),
                ContiguousRange(correspondence.AfterCoordinates, nameof(diff))));
        }

        moved.Sort(static (left, right) => left.Before.Start.CompareTo(right.Before.Start));
        var runs = new List<MoveRun>();
        foreach (MoveRun item in moved)
        {
            if (runs.Count > 0
                && runs[^1].Before.End == item.Before.Start
                && runs[^1].After.End == item.After.Start)
            {
                MoveRun last = runs[^1];
                runs[^1] = new MoveRun(
                    new TextLineRange(last.Before.Start, last.Before.Count + item.Before.Count),
                    new TextLineRange(last.After.Start, last.After.Count + item.After.Count));
                continue;
            }

            runs.Add(item);
        }

        // A run made entirely of whitespace-only lines relocates blank space, which the whitespace
        // characterization reports; only runs with content are moves.
        return runs.Where(run => HasContent(before, run.Before) || HasContent(after, run.After)).ToList();
    }

    static TextLineRange ContiguousRange(ImmutableArray<int> coordinates, string parameterName)
    {
        int start = coordinates.Min();
        int end = coordinates.Max() + 1;
        if (end - start != coordinates.Length)
            throw new ArgumentException("A moved population isn't contiguous.", parameterName);
        return new TextLineRange(start, end - start);
    }

    static bool HasContent(TextLines lines, TextLineRange range)
    {
        for (int line = range.Start; line < range.End; line++)
        {
            foreach (char character in lines.ContentOf(line))
            {
                if (!TextWhitespace.IsWhitespace(character))
                    return true;
            }
        }

        return false;
    }

    static TextRegion CharacterizeRegion(RegionContext context, List<MoveRun> moves)
    {
        bool holdsMoveEnd =
            AnyMove(context.BeforeMove, context.Before) || AnyMove(context.AfterMove, context.After);

        if (!holdsMoveEnd)
        {
            TextPairCharacterization whole = TextWhitespace.Characterize(
                context.BeforeLines,
                context.BeforeStart,
                context.BeforeEnd,
                context.AfterLines,
                context.AfterStart,
                context.AfterEnd,
                context.EndsText);
            if (whole.Outcome == TextPairOutcome.Identical)
            {
                throw new ArgumentException(
                    "A region without a move end has identical texts; the producer's comparison contract can't be characterized.",
                    "diff");
            }

            if (whole.Outcome == TextPairOutcome.WhitespaceOnly)
            {
                return new TextRegion(
                    context.Before,
                    context.After,
                    TextRegionOutcome.WhitespaceOnly,
                    [new TextChange(context.Before, context.After, TextChangeOutcome.WhitespaceOnly, whole.Edits, null)]);
            }
        }

        List<Piece> pieces = PartitionRegion(context);
        ImmutableArray<TextChange> changes = IssueChanges(context, pieces);
        return new TextRegion(context.Before, context.After, TextRegionOutcome.Changed, changes);
    }

    static bool AnyMove(int[] moveOfLine, TextLineRange range)
    {
        for (int line = range.Start; line < range.End; line++)
        {
            if (moveOfLine[line] >= 0)
                return true;
        }

        return false;
    }

    /// <summary>
    /// Orders move ends and the free lines between them into pieces. At each step, a move end at
    /// the Before cursor is taken first, then a move end at the After cursor, then the free runs
    /// up to the next move end on each side; free runs are then split for whitespace-only parts.
    /// </summary>
    static List<Piece> PartitionRegion(RegionContext context)
    {
        var pieces = new List<Piece>();
        int b = context.Before.Start;
        int a = context.After.Start;
        while (b < context.Before.End || a < context.After.End)
        {
            if (b < context.Before.End && context.BeforeMove[b] >= 0)
            {
                int move = context.BeforeMove[b];
                int end = b;
                while (end < context.Before.End && context.BeforeMove[end] == move)
                    end++;
                pieces.Add(new Piece(new TextLineRange(b, end - b), new TextLineRange(a, 0), move));
                b = end;
                continue;
            }

            if (a < context.After.End && context.AfterMove[a] >= 0)
            {
                int move = context.AfterMove[a];
                int end = a;
                while (end < context.After.End && context.AfterMove[end] == move)
                    end++;
                pieces.Add(new Piece(new TextLineRange(b, 0), new TextLineRange(a, end - a), move));
                a = end;
                continue;
            }

            int beforeFree = b;
            while (beforeFree < context.Before.End && context.BeforeMove[beforeFree] < 0)
                beforeFree++;
            int afterFree = a;
            while (afterFree < context.After.End && context.AfterMove[afterFree] < 0)
                afterFree++;

            pieces.AddRange(SplitFree(
                context,
                new TextLineRange(b, beforeFree - b),
                new TextLineRange(a, afterFree - a)));
            b = beforeFree;
            a = afterFree;
        }

        return pieces;
    }

    /// <summary>
    /// Splits one free run into candidate whitespace-only and changed pieces, maximizing the lines
    /// in whitespace-only pieces and then minimizing the number of pieces. Candidates are only
    /// proposals; outcomes are recomputed from the final cut texts.
    /// </summary>
    static IEnumerable<Piece> SplitFree(RegionContext context, TextLineRange beforeRange, TextLineRange afterRange)
    {
        int m = beforeRange.Count;
        int n = afterRange.Count;
        if (m == 0 || n == 0 || (long)m * n > SplitBudget)
        {
            yield return new Piece(beforeRange, afterRange, -1);
            yield break;
        }

        string[] strippedBefore = new string[m];
        string[] strippedAfter = new string[n];
        for (int i = 0; i < m; i++)
            strippedBefore[i] = TextWhitespace.Strip(context.BeforeLines.ContentOf(beforeRange.Start + i));
        for (int j = 0; j < n; j++)
            strippedAfter[j] = TextWhitespace.Strip(context.AfterLines.ContentOf(afterRange.Start + j));
        string joinedBefore = string.Concat(strippedBefore);
        string joinedAfter = string.Concat(strippedAfter);
        int[] beforeOffsets = PrefixOffsets(strippedBefore);
        int[] afterOffsets = PrefixOffsets(strippedAfter);

        var best = new int[m + 1, n + 1];
        var from = new (int I, int J, bool Whitespace)[m + 1, n + 1];
        var prefixBest = new int[m + 1, n + 1];
        var prefixFrom = new (int I, int J)[m + 1, n + 1];
        for (int i = 0; i <= m; i++)
        {
            for (int j = 0; j <= n; j++)
            {
                if (i == 0 && j == 0)
                {
                    best[0, 0] = 0;
                }
                else
                {
                    // A changed piece may start at any earlier cut point.
                    int value = int.MinValue;
                    (int I, int J) origin = (0, 0);
                    if (i > 0 && prefixBest[i - 1, j] > value)
                    {
                        value = prefixBest[i - 1, j];
                        origin = prefixFrom[i - 1, j];
                    }

                    if (j > 0 && prefixBest[i, j - 1] > value)
                    {
                        value = prefixBest[i, j - 1];
                        origin = prefixFrom[i, j - 1];
                    }

                    best[i, j] = value - 1;
                    from[i, j] = (origin.I, origin.J, false);

                    // A whitespace-only piece needs equal stripped text on both sides.
                    for (int i0 = 0; i0 <= i; i0++)
                    {
                        for (int j0 = 0; j0 <= j; j0++)
                        {
                            if (i0 == i && j0 == j)
                                continue;

                            int beforeLength = beforeOffsets[i] - beforeOffsets[i0];
                            int afterLength = afterOffsets[j] - afterOffsets[j0];
                            if (beforeLength != afterLength)
                                continue;

                            int candidate = best[i0, j0] + LinesWeight * ((i - i0) + (j - j0)) - 1;
                            if (candidate <= best[i, j])
                                continue;

                            if (!joinedBefore.AsSpan(beforeOffsets[i0], beforeLength)
                                    .SequenceEqual(joinedAfter.AsSpan(afterOffsets[j0], afterLength)))
                            {
                                continue;
                            }

                            best[i, j] = candidate;
                            from[i, j] = (i0, j0, true);
                        }
                    }
                }

                prefixBest[i, j] = best[i, j];
                prefixFrom[i, j] = (i, j);
                if (i > 0 && prefixBest[i - 1, j] > prefixBest[i, j])
                {
                    prefixBest[i, j] = prefixBest[i - 1, j];
                    prefixFrom[i, j] = prefixFrom[i - 1, j];
                }

                if (j > 0 && prefixBest[i, j - 1] > prefixBest[i, j])
                {
                    prefixBest[i, j] = prefixBest[i, j - 1];
                    prefixFrom[i, j] = prefixFrom[i, j - 1];
                }
            }
        }

        var reversed = new List<Piece>();
        int ci = m;
        int cj = n;
        while (ci > 0 || cj > 0)
        {
            (int pi, int pj, _) = from[ci, cj];
            reversed.Add(new Piece(
                new TextLineRange(beforeRange.Start + pi, ci - pi),
                new TextLineRange(afterRange.Start + pj, cj - pj),
                -1));
            ci = pi;
            cj = pj;
        }

        reversed.Reverse();
        foreach (Piece piece in reversed)
            yield return piece;
    }

    /// <summary>Weights whitespace-only lines above the one-point cost of each piece.</summary>
    const int LinesWeight = 1000;

    static int[] PrefixOffsets(string[] parts)
    {
        var offsets = new int[parts.Length + 1];
        for (int index = 0; index < parts.Length; index++)
            offsets[index + 1] = offsets[index] + parts[index].Length;
        return offsets;
    }

    /// <summary>
    /// Cuts the region texts at the pieces, recomputes each free piece's outcome from its cut
    /// texts, joins identical pieces to a free neighbor, and merges adjacent free pieces with the
    /// same outcome so every non-moved change is maximal.
    /// </summary>
    static ImmutableArray<TextChange> IssueChanges(RegionContext context, List<Piece> pieces)
    {
        var free = new List<Piece>(pieces);
        while (true)
        {
            List<(Piece Piece, TextPairCharacterization Characterization)> evaluated = Evaluate(context, free);
            int merge = FindMerge(evaluated);
            if (merge < 0)
                return [.. evaluated.Select(item => ToChange(item.Piece, item.Characterization))];

            Piece left = free[merge];
            Piece right = free[merge + 1];
            free[merge] = new Piece(
                new TextLineRange(left.Before.Start, left.Before.Count + right.Before.Count),
                new TextLineRange(left.After.Start, left.After.Count + right.After.Count),
                -1);
            free.RemoveAt(merge + 1);
        }
    }

    static int FindMerge(List<(Piece Piece, TextPairCharacterization Characterization)> evaluated)
    {
        for (int index = 0; index < evaluated.Count; index++)
        {
            if (evaluated[index].Piece.Move >= 0
                || evaluated[index].Characterization.Outcome != TextPairOutcome.Identical)
            {
                continue;
            }

            if (index + 1 < evaluated.Count && evaluated[index + 1].Piece.Move < 0)
                return index;
            if (index > 0 && evaluated[index - 1].Piece.Move < 0)
                return index - 1;

            throw new ArgumentException(
                "A region holds identical text between move ends that the producer left unmatched.",
                "diff");
        }

        for (int index = 0; index + 1 < evaluated.Count; index++)
        {
            if (evaluated[index].Piece.Move < 0
                && evaluated[index + 1].Piece.Move < 0
                && evaluated[index].Characterization.Outcome == evaluated[index + 1].Characterization.Outcome)
            {
                return index;
            }
        }

        return -1;
    }

    static List<(Piece Piece, TextPairCharacterization Characterization)> Evaluate(
        RegionContext context,
        List<Piece> pieces)
    {
        int count = pieces.Count;
        var beforeStarts = new int[count];
        var afterStarts = new int[count];
        for (int index = count - 1; index >= 0; index--)
        {
            beforeStarts[index] = pieces[index].Before.Count > 0
                ? context.BeforeLines.StartOf(pieces[index].Before.Start)
                : index + 1 < count ? beforeStarts[index + 1] : context.BeforeEnd;
            afterStarts[index] = pieces[index].After.Count > 0
                ? context.AfterLines.StartOf(pieces[index].After.Start)
                : index + 1 < count ? afterStarts[index + 1] : context.AfterEnd;
        }

        beforeStarts[0] = context.BeforeStart;
        afterStarts[0] = context.AfterStart;

        var evaluated = new List<(Piece, TextPairCharacterization)>(count);
        for (int index = 0; index < count; index++)
        {
            Piece piece = pieces[index];
            if (piece.Move >= 0)
            {
                evaluated.Add((piece, new TextPairCharacterization(TextPairOutcome.Changed, [])));
                continue;
            }

            bool last = index + 1 == count;
            TextPairCharacterization characterization = TextWhitespace.Characterize(
                context.BeforeLines,
                beforeStarts[index],
                last ? context.BeforeEnd : beforeStarts[index + 1],
                context.AfterLines,
                afterStarts[index],
                last ? context.AfterEnd : afterStarts[index + 1],
                context.EndsText && last);
            evaluated.Add((piece, characterization));
        }

        return evaluated;
    }

    static TextChange ToChange(Piece piece, TextPairCharacterization characterization)
    {
        if (piece.Move >= 0)
            return new TextChange(piece.Before, piece.After, TextChangeOutcome.Moved, [], piece.Move);

        return characterization.Outcome == TextPairOutcome.WhitespaceOnly
            ? new TextChange(piece.Before, piece.After, TextChangeOutcome.WhitespaceOnly, characterization.Edits, null)
            : new TextChange(piece.Before, piece.After, TextChangeOutcome.Changed, [], null);
    }

    /// <summary>
    /// Numbers moves from 1 in the order their first end appears in the change sequence, and
    /// replaces each moved change's internal move index with its id.
    /// </summary>
    static ImmutableArray<TextRegion> AssignMoveIds(
        ImmutableArray<TextRegion> regions,
        List<MoveRun> moves,
        out Dictionary<int, int> moveIds)
    {
        moveIds = [];
        var result = ImmutableArray.CreateBuilder<TextRegion>(regions.Length);
        foreach (TextRegion region in regions)
        {
            var changes = ImmutableArray.CreateBuilder<TextChange>(region.Changes.Length);
            foreach (TextChange change in region.Changes)
            {
                if (change.MoveId is not { } index)
                {
                    changes.Add(change);
                    continue;
                }

                if (!moveIds.TryGetValue(index, out int id))
                {
                    id = moveIds.Count + 1;
                    moveIds[index] = id;
                }

                changes.Add(change with { MoveId = id });
            }

            result.Add(region with { Changes = changes.MoveToImmutable() });
        }

        return result.MoveToImmutable();
    }

    static TextMove CharacterizeMove(int id, MoveRun move, TextLines before, TextLines after)
    {
        var beforeEnd = new TextLines(JoinLines(before, move.Before));
        var afterEnd = new TextLines(JoinLines(after, move.After));
        TextPairCharacterization characterization = TextWhitespace.Characterize(
            beforeEnd,
            0,
            beforeEnd.Text.Length,
            afterEnd,
            0,
            afterEnd.Text.Length,
            endsText: false);

        TextMoveContent content = characterization.Outcome switch
        {
            TextPairOutcome.Identical => TextMoveContent.Unchanged,
            TextPairOutcome.WhitespaceOnly => TextMoveContent.WhitespaceOnly,
            _ => TextMoveContent.Changed,
        };

        ImmutableArray<TextWhitespaceEdit> edits = [.. characterization.Edits.Select(edit => edit with
        {
            BeforeStart = Shift(edit.BeforeStart, move.Before.Start),
            BeforeEnd = Shift(edit.BeforeEnd, move.Before.Start),
            AfterStart = Shift(edit.AfterStart, move.After.Start),
            AfterEnd = Shift(edit.AfterEnd, move.After.Start),
        })];

        return new TextMove(id, move.Before, move.After, content, edits);
    }

    static string JoinLines(TextLines lines, TextLineRange range)
        => string.Join('\n', Enumerable.Range(range.Start, range.Count).Select(lines.ContentOf));

    static TextPosition Shift(TextPosition position, int lines)
        => position with { Line = position.Line + lines };

    readonly record struct MoveRun(TextLineRange Before, TextLineRange After);

    /// <summary>One proposed change: a move end (<see cref="Move"/> is its index) or a free piece.</summary>
    readonly record struct Piece(TextLineRange Before, TextLineRange After, int Move);

    sealed record RegionContext(
        TextLines BeforeLines,
        TextLines AfterLines,
        TextLineRange Before,
        TextLineRange After,
        int BeforeStart,
        int BeforeEnd,
        int AfterStart,
        int AfterEnd,
        bool EndsText,
        int[] BeforeMove,
        int[] AfterMove);
}
