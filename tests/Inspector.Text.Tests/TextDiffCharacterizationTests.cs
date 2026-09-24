using System.Collections.Immutable;

using Inspector.Findings;

namespace Inspector.Text.Tests;

/// <summary>
/// Line-diff rows from the text whitespace characterization and text move characterization
/// designs' pathological demonstrations, driven through <see cref="TextFindings.CreateAnalysisDiff"/>
/// and checked by the independent <see cref="TextDiffCharacterizationValidator"/>.
/// </summary>
public class TextDiffCharacterizationTests
{
    static readonly FindingSubject Subject = new("document", "Document");

    // ---- Whitespace rows ----

    [Fact]
    public void BlankLineRemoved_IsItsOwnWhitespaceOnlyRegion()
    {
        // Newtonsoft.Json 13.0.3 JToken.Remove: PDB source has a blank line the decompiler omits.
        var result = Characterize(
            "    if (_parent == null)\n    {\n        throw;\n    }\n\n    _parent.RemoveItem(this);\n",
            "    if (_parent is null)\n    {\n        throw;\n    }\n    _parent.RemoveItem(this);\n");

        Assert.Equal(TextDocumentOutcome.Changed, result.Summary);
        TextRegion blank = Assert.Single(result.Regions, region => region.Outcome == TextRegionOutcome.WhitespaceOnly);
        TextChange change = Assert.Single(blank.Changes);
        Assert.Equal(TextWhitespaceEditKinds.LineBreaks, Assert.Single(change.Edits).Kinds);
    }

    [Fact]
    public void BlankLineSpaces_IsBlankLineContent()
    {
        // Scrutor 4.2.2 -> 5.0.0 Decorate: a blank line that held spaces became empty.
        var result = Characterize(
            "Preconditions.NotNull(decorator);\n    \nreturn services;\n",
            "Preconditions.NotNull(decorator);\n\nreturn services;\n");

        Assert.Equal(TextDocumentOutcome.WhitespaceOnly, result.Summary);
        TextChange change = Assert.Single(Assert.Single(result.Regions).Changes);
        Assert.Equal(TextWhitespaceEditKinds.BlankLineContent, Assert.Single(change.Edits).Kinds);
    }

    [Fact]
    public void MixedReflowAndEdit_SplitsIntoTwoChanges()
    {
        var result = Characterize("class Foo {\nint x;\n", "class Foo\n{\nint y;\n");

        TextRegion region = Assert.Single(result.Regions);
        Assert.Equal(TextRegionOutcome.Changed, region.Outcome);
        Assert.Collection(
            region.Changes,
            first =>
            {
                Assert.Equal(TextChangeOutcome.WhitespaceOnly, first.Outcome);
                Assert.Equal(new TextLineRange(0, 1), first.Before);
                Assert.Equal(new TextLineRange(0, 2), first.After);
                Assert.Equal(TextWhitespaceEditKinds.LineBreaks, Assert.Single(first.Edits).Kinds);
            },
            second =>
            {
                Assert.Equal(TextChangeOutcome.Changed, second.Outcome);
                Assert.Equal(new TextLineRange(1, 1), second.Before);
                Assert.Equal(new TextLineRange(2, 1), second.After);
            });
    }

    [Fact]
    public void BlankLineAfterRewrittenLine_IsItsOwnChange()
    {
        var result = Characterize("x = Foo(); // old\n\ny();\n", "x = Foo(); // new\ny();\n");

        TextRegion region = Assert.Single(result.Regions);
        Assert.Collection(
            region.Changes,
            first => Assert.Equal(TextChangeOutcome.Changed, first.Outcome),
            second =>
            {
                Assert.Equal(TextChangeOutcome.WhitespaceOnly, second.Outcome);
                Assert.Equal(TextWhitespaceEditKinds.LineBreaks, Assert.Single(second.Edits).Kinds);
            });
    }

    [Fact]
    public void LeadingZeroLineSide_CutsAtTheRegionStart()
    {
        var result = Characterize("p\nk", "p\n\nK");

        TextRegion region = Assert.Single(result.Regions);
        Assert.Collection(
            region.Changes,
            first =>
            {
                Assert.Equal(TextChangeOutcome.WhitespaceOnly, first.Outcome);
                Assert.Equal(0, first.Before.Count);
                Assert.Equal(TextWhitespaceEditKinds.LineBreaks, Assert.Single(first.Edits).Kinds);
            },
            second => Assert.Equal(TextChangeOutcome.Changed, second.Outcome));
    }

    [Fact]
    public void LineReCut_IsOneWhitespaceOnlyChange()
    {
        var result = Characterize("ab\nb\nc", "a\n b\nbc");

        TextRegion region = Assert.Single(result.Regions);
        Assert.Equal(TextRegionOutcome.WhitespaceOnly, region.Outcome);
        TextChange change = Assert.Single(region.Changes);
        Assert.Equal(2, change.Edits.Count(edit => edit.Kinds.HasFlag(TextWhitespaceEditKinds.LineBreaks)));
    }

    [Theory]
    [InlineData("return (_annotations as T);", "return (T)(_annotations as T);")]
    [InlineData("foo(a, b)", "foo(a)")]
    [InlineData("x = 1;", "x = 1; z")]
    [InlineData("ab", "a\nXYZ\nb")]
    public void ContentEdits_AreOneChangedChange(string before, string after)
    {
        var result = Characterize(before, after);

        TextChange change = Assert.Single(Assert.Single(result.Regions).Changes);
        Assert.Equal(TextChangeOutcome.Changed, change.Outcome);
        Assert.Equal(TextDocumentOutcome.Changed, result.Summary);
    }

    [Fact]
    public void FinalNewline_IsLocatedInItsRegion()
    {
        var result = Characterize("x", "x\n");

        TextChange change = Assert.Single(Assert.Single(result.Regions).Changes);
        Assert.Equal(TextChangeOutcome.WhitespaceOnly, change.Outcome);
        Assert.Equal(
            TextWhitespaceEditKinds.LineBreaks | TextWhitespaceEditKinds.FinalLineTerminator,
            Assert.Single(change.Edits).Kinds);
    }

    [Theory]
    [InlineData("a\r\nb", "a\nb")]
    [InlineData("a\r\n", "a\n")]
    public void TerminatorSpellingOutsideRegions_IsSummaryOnly(string before, string after)
    {
        var result = Characterize(before, after);

        Assert.Equal(TextDocumentOutcome.WhitespaceOnly, result.Summary);
        Assert.Empty(result.Regions);
    }

    [Fact]
    public void InsertionAfterRespelledBoundary_LocatesTheBoundary()
    {
        var result = Characterize("a\r\nb", "a\n\nb");

        TextChange change = Assert.Single(Assert.Single(result.Regions).Changes);
        TextWhitespaceEdit edit = Assert.Single(change.Edits);
        Assert.Equal(TextWhitespaceEditKinds.LineBreaks, edit.Kinds);
        Assert.Equal(new TextPosition(0, 1), edit.BeforeStart);
        Assert.Equal(new TextPosition(1, 0), edit.BeforeEnd);
    }

    [Fact]
    public void Identical_IsNoDifference()
    {
        var result = Characterize("a\nb\n", "a\nb\n");

        Assert.Equal(TextDocumentOutcome.NoDifference, result.Summary);
        Assert.Empty(result.Regions);
    }

    [Fact]
    public void SplitBudget_KeepsOneChangedChange()
    {
        string before = string.Join('\n', Enumerable.Range(0, 30).Select(i => $"before {i}")) + "\n\n";
        string after = string.Join('\n', Enumerable.Range(0, 30).Select(i => $"after {i}")) + "\n";

        var result = Characterize(before, after);

        TextChange change = Assert.Single(Assert.Single(result.Regions).Changes);
        Assert.Equal(TextChangeOutcome.Changed, change.Outcome);
    }

    [Fact]
    public void AnchorWithDifferentContent_IsAnArgumentFailure()
    {
        var diff = new AnalysisDiff<string>(
            ["Foo"],
            ["foo"],
            [new AnalysisDiffRelation.Correspondence([0], [0], AnalysisDiffContentKind.Unchanged, AnalysisDiffPlacementKind.Stable)]);

        Assert.Throws<ArgumentException>(() => TextDiffCharacterization.Create(diff, "Foo", "foo"));
    }

    [Fact]
    public void ChangedCorrespondenceOverEqualText_IsAnArgumentFailure()
    {
        var diff = new AnalysisDiff<string>(
            ["a"],
            ["a"],
            [new AnalysisDiffRelation.Correspondence([0], [0], AnalysisDiffContentKind.Changed, AnalysisDiffPlacementKind.Stable)]);

        Assert.Throws<ArgumentException>(() => TextDiffCharacterization.Create(diff, "a", "a"));
    }

    [Fact]
    public void MismatchedEndpoint_IsAnArgumentFailure()
    {
        AnalysisDiff<string> diff = TextFindings.CreateAnalysisDiff("a\n", "b\n", Subject);

        Assert.Throws<ArgumentException>(() => TextDiffCharacterization.Create(diff, "a\n", "c\n"));
    }

    [Fact]
    public void TrailingLineAfterAnAnchor_LocatesTheAnchorBoundary()
    {
        // As TextAnalysisDiffPresentation.CreateAnalysisDiff issues it for unterminated texts.
        var diff = new AnalysisDiff<string>(
            ["a", " "],
            ["a"],
            [
                new AnalysisDiffRelation.Correspondence([0], [0], AnalysisDiffContentKind.Unchanged, AnalysisDiffPlacementKind.Stable),
                new AnalysisDiffRelation.Removal([1]),
            ]);

        TextDiffCharacterization result = TextDiffCharacterization.Create(diff, "a\n ", "a");
        TextDiffCharacterizationValidator.Validate(result, diff, "a\n ", "a");

        TextChange change = Assert.Single(Assert.Single(result.Regions).Changes);
        Assert.Equal(TextChangeOutcome.WhitespaceOnly, change.Outcome);
        Assert.Equal(TextWhitespaceEditKinds.LineBreaks, Assert.Single(change.Edits).Kinds);
    }

    // ---- Move rows ----

    [Fact]
    public void BlockMovedDown_IsOneMoveWithRemovalFirst()
    {
        var result = Characterize("A\nB\nx\ny\nz\n", "x\ny\nz\nA\nB\n");

        TextMove move = Assert.Single(result.Moves);
        Assert.Equal(1, move.Id);
        Assert.Equal(new TextLineRange(0, 2), move.Before);
        Assert.Equal(new TextLineRange(3, 2), move.After);
        Assert.Equal(TextMoveContent.Unchanged, move.Content);
        TextChange first = result.Regions.SelectMany(region => region.Changes).First();
        Assert.Equal(TextChangeOutcome.Moved, first.Outcome);
        Assert.Equal(0, first.After.Count);
    }

    [Fact]
    public void BlockMovedUp_AssignsTheIdAtTheAddition()
    {
        var result = Characterize("x\ny\nz\nA\nB\n", "A\nB\nx\ny\nz\n");

        Assert.Single(result.Moves);
        TextChange first = result.Regions.SelectMany(region => region.Changes).First();
        Assert.Equal(TextChangeOutcome.Moved, first.Outcome);
        Assert.Equal(0, first.Before.Count);
        Assert.Equal(1, first.MoveId);
    }

    [Fact]
    public void RelocatedAndReIndented_IsAWhitespaceOnlyMove()
    {
        var result = Characterize("A\nB\nx\ny\nz\n", "x\ny\nz\nif (c) {\n  A\n  B\n}\n");

        TextMove move = Assert.Single(result.Moves);
        Assert.Equal(TextMoveContent.WhitespaceOnly, move.Content);
        Assert.All(move.Edits, edit => Assert.Equal(TextWhitespaceEditKinds.Indentation, edit.Kinds));
    }

    [Fact]
    public void ReIndentedInPlace_IsNotAMove()
    {
        var result = Characterize(
            "if (!e) {\nreturn;\n}\nA\nB\n",
            "if (e) {\n  A\n  B\n}\n");

        Assert.Empty(result.Moves);
        Assert.Equal(TextDocumentOutcome.Changed, result.Summary);
    }

    [Fact]
    public void MovedAndEdited_IsNotAMove()
    {
        var result = Characterize("A\nB\nx\ny\nz\n", "x\ny\nz\nA\nB2\n");

        Assert.Empty(result.Moves);
    }

    [Fact]
    public void SingleLineMove_IsNotAMove()
        => Assert.Empty(Characterize("A\nx\ny\n", "x\ny\nA\n").Moves);

    [Fact]
    public void BlankLinesMoved_IsNotAMove()
    {
        var result = Characterize("\n\nx\ny\nz\n", "x\ny\nz\n\n\n");

        Assert.Empty(result.Moves);
        Assert.Equal(TextDocumentOutcome.WhitespaceOnly, result.Summary);
    }

    [Fact]
    public void TwoMoves_NumberInChangeSequenceOrder()
    {
        var result = Characterize(
            "A\nB\nx1\nx2\nx3\nC\nD\ny1\ny2\ny3\n",
            "x1\nx2\nx3\nA\nB\ny1\ny2\ny3\nC\nD\n");

        Assert.Equal([1, 2], result.Moves.Select(move => move.Id));
    }

    [Fact]
    public void SwappedBlocks_IsOneMove()
        => Assert.Single(Characterize("A\nB\nC\nD\n", "C\nD\nA\nB\n").Moves);

    [Fact]
    public void ReIndentedSwap_IsOneWhitespaceOnlyMove()
    {
        var result = Characterize("x\nA\nB\nC\nD\ny\n", "x\n  C\n  D\n  A\n  B\ny\n");

        TextMove move = Assert.Single(result.Moves);
        Assert.Equal(TextMoveContent.WhitespaceOnly, move.Content);
    }

    [Fact]
    public void InteriorBlankLine_BelongsToTheMove()
    {
        var result = Characterize("A\n\nB\nx\ny\nz\n", "x\ny\nz\nA\n\nB\n");

        TextMove move = Assert.Single(result.Moves);
        Assert.Equal(3, move.Before.Count);
    }

    [Fact]
    public void IdTieAtOnePosition_PutsTheRemovalFirst()
    {
        var result = Characterize("A\nB\nx1\nx2\nx3\nC\nD\n", "C\nD\nx1\nx2\nx3\nA\nB\n");

        Assert.Equal(2, result.Moves.Length);
        TextMove first = result.Moves.Single(move => move.Id == 1);
        Assert.Equal(new TextLineRange(0, 2), first.Before);
    }

    [Fact]
    public void BracesOnly_IsAMove()
        => Assert.Single(Characterize("}\n}\nx\ny\nz\n", "x\ny\nz\n}\n}\n").Moves);

    // ---- Real assets ----

    [Fact]
    public void Polly_MeterEvent_ReIndentsInPlaceWithoutMoves()
    {
        string before = Asset("polly-meterevent-8.5.2.txt");
        string after = Asset("polly-meterevent-8.6.0.txt");

        var result = Characterize(before, after);

        Assert.Empty(result.Moves);
        TextChange[] whitespace = result.Regions
            .SelectMany(region => region.Changes)
            .Where(change => change.Outcome == TextChangeOutcome.WhitespaceOnly)
            .ToArray();
        // Each re-indented block (5 and 7 lines) sits in a whitespace-only change whose edits
        // include the four-space indentation; the removed blank line adds a line-break edit.
        Assert.Equal(2, whitespace.Length);
        Assert.All(whitespace, change =>
        {
            Assert.True(change.Before.Count >= 5);
            Assert.Contains(change.Edits, edit => edit.Kinds == TextWhitespaceEditKinds.Indentation);
        });
    }

    [Fact]
    public void Newtonsoft_JsonConvertToString_RelocatedCasesAreMoves()
    {
        string before = Asset("newtonsoft-jsonconvert-tostring-pdb.txt");
        string after = Asset("newtonsoft-jsonconvert-tostring-decompiled.txt");

        var result = Characterize(before, after);

        Assert.NotEmpty(result.Moves);
        Assert.Contains(
            result.Moves,
            move => move.Content == TextMoveContent.Unchanged
                && Lines(before, move.Before).Any(line => line.Contains("PrimitiveTypeCode.String", StringComparison.Ordinal)));
    }

    // ---- Exhaustive small-text sweep ----

    [Fact]
    public void SmallTextSweep_EveryCharacterizationValidates()
    {
        string[] alphabet = ["", "A", " A", "B", " "];
        string[] terminators = ["", "\n"];
        var texts = new List<string>();
        foreach (string[] lines in Sequences(alphabet, maxLength: 3))
        {
            foreach (string terminator in terminators)
            {
                if (lines.Length == 0 && terminator.Length > 0)
                    continue;
                texts.Add(string.Join('\n', lines) + terminator);
            }
        }

        foreach (string before in texts)
        {
            foreach (string after in texts)
            {
                AnalysisDiff<string> diff = TextFindings.CreateAnalysisDiff(before, after, Subject);
                TextDiffCharacterization result = TextDiffCharacterization.Create(diff, before, after);
                TextDiffCharacterizationValidator.Validate(result, diff, before, after);
            }
        }
    }

    static IEnumerable<string[]> Sequences(string[] alphabet, int maxLength)
    {
        var layer = new List<string[]> { Array.Empty<string>() };
        for (int length = 0; length <= maxLength; length++)
        {
            foreach (string[] sequence in layer)
                yield return sequence;
            layer = [.. layer.SelectMany(sequence => alphabet.Select(item => (string[])[.. sequence, item]))];
        }
    }

    static TextDiffCharacterization Characterize(string before, string after)
    {
        AnalysisDiff<string> diff = TextFindings.CreateAnalysisDiff(before, after, Subject);
        TextDiffCharacterization result = TextDiffCharacterization.Create(diff, before, after);
        TextDiffCharacterizationValidator.Validate(result, diff, before, after);
        return result;
    }

    static string Asset(string name)
        => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Assets", name));

    static IEnumerable<string> Lines(string text, TextLineRange range)
        => text.Split('\n').Skip(range.Start).Take(range.Count);
}
