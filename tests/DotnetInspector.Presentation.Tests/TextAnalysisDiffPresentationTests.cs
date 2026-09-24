using DotnetInspector.Presentation;
using Inspector.Findings;
using Inspector.Text;
using Markout;

namespace DotnetInspector.Presentation.Tests;

public sealed class TextAnalysisDiffPresentationTests
{
    static readonly FindingSubject Subject =
        new("presentation.source-pair", "Source pair");

    [Fact]
    public void CompletedComparisonProjectsWithoutChangingProducerRelations()
    {
        const string Before = "A\nB\nC\nmoved-one\nmoved-two\nD\nE";
        const string After = "moved-one\nmoved-two\nA\nB changed\nC\nD\nE";
        FindingComparison<string>.Complete comparison = Complete(
            TextFindings.Compare(
                Before,
                After,
                Subject,
                acceptanceThreshold: 0));

        AnalysisDiff<string> analysis =
            TextAnalysisDiffPresentation.CreateAnalysisDiff(
                comparison,
                Before,
                After);

        Assert.Equal(
            comparison.OldAtoms.Select(atom => atom.Payload),
            analysis.Before);
        Assert.Equal(
            comparison.NewAtoms.Select(atom => atom.Payload),
            analysis.After);
        Assert.Contains(
            analysis.Relations,
            relation => relation is AnalysisDiffRelation.Correspondence
            {
                Content: AnalysisDiffContentKind.Unchanged,
                Placement: AnalysisDiffPlacementKind.Moved,
            });
        Assert.Equal(
            comparison.Pairs.Length,
            analysis.Relations.Length);
    }

    [Fact]
    public void ProjectionRejectsTextThatDoesNotBelongToTheComparison()
    {
        FindingComparison<string>.Complete comparison = Complete(
            TextFindings.Compare("before", "after", Subject));

        ArgumentException error = Assert.Throws<ArgumentException>(() =>
            TextAnalysisDiffPresentation.CreateAnalysisDiff(
                comparison,
                "different",
                "after"));

        Assert.Contains("Before comparison atom", error.Message);
    }

    [Fact]
    public void ProjectionRequiresProducerAssertedAbsentFinalTerminators()
    {
        FindingComparison<string>.Complete comparison = Complete(
            TextFindings.Compare("before\n", "after", Subject));

        ArgumentException error = Assert.Throws<ArgumentException>(() =>
            TextAnalysisDiffPresentation.CreateAnalysisDiff(
                comparison,
                "before\n",
                "after"));

        Assert.Contains("final line terminator", error.Message);
    }

    [Fact]
    public void LabeledLoweringMarksWhitespaceOnlyChangesWithInnerMappings()
    {
        // Scrutor 4.2.2 -> 5.0.0 Decorate: a blank line that held spaces became empty.
        const string Before = "NotNull(decorator);\n        \nreturn services;";
        const string After = "NotNull(decorator);\n\nreturn services;";

        MappedTextDiff diff = Lower(Before, After);

        TextDiffChange change = Assert.Single(diff.Changes);
        Assert.Equal("whitespace-only: blank-line content", change.Label?.Text);
        Assert.Equal(TextDiffLabelEmphasis.Subdued, change.Label?.Emphasis);
        Assert.True(change.Label?.ShowWhitespace);
        TextDiffInnerMapping mapping = Assert.Single(change.InnerMappings);
        Assert.Equal(new TextDiffSpan(1, 0, 8), mapping.Before);
        Assert.Equal(new TextDiffSpan(1, 0, 0), mapping.After);
    }

    [Fact]
    public void LabeledLoweringLinksMoveEndsAndLeavesChangesUnlabeled()
    {
        const string Before = "A\nB\nx\ny\nz\nold";
        const string After = "x\ny\nz\nA\nB\nnew";

        MappedTextDiff diff = Lower(Before, After);

        TextDiffChange[] moved = [.. diff.Changes.Where(change => change.Label?.RelatedChange is not null)];
        Assert.Equal(2, moved.Length);
        Assert.Equal("moved (1) to +4", moved[0].Label!.Text);
        Assert.Equal("moved (1) from -1", moved[1].Label!.Text);
        Assert.Equal(Array.IndexOf([.. diff.Changes], moved[1]), moved[0].Label!.RelatedChange);
        Assert.Contains(diff.Changes, change => change.Label is null);
    }

    [Fact]
    public void LabeledLoweringSeparatesRealAndWhitespaceHunks()
    {
        // Newtonsoft.Json 13.0.3 JToken.Remove: a real edit next to a removed blank line.
        const string Before = "if (_parent == null)\n{\n    throw;\n}\n\n_parent.RemoveItem(this);";
        const string After = "if (_parent is null)\n{\n    throw;\n}\n_parent.RemoveItem(this);";

        MappedTextDiff diff = Lower(Before, After);
        var writer = MarkoutWriter.Create(new MarkdownFormatter(), new MarkoutWriterOptions { NewLine = "\n" });
        writer.WriteTextDiff(diff);
        string output = writer.ToString();

        Assert.Contains("@@ whitespace-only: line breaks\n", output);
        Assert.Equal(2, output.Split('\n').Count(line => line.StartsWith("@@", StringComparison.Ordinal)));
    }

    static MappedTextDiff Lower(string before, string after)
    {
        AnalysisDiff<string> analysis = TextFindings.CreateAnalysisDiff(before, after, Subject);
        TextDiffCharacterization characterization = TextDiffCharacterization.Create(analysis, before, after);
        return TextAnalysisDiffPresentation.CreateLabeledMappedTextDiff(
            analysis,
            characterization,
            "Before",
            TextDiffLineTerminator.Absent,
            "After",
            TextDiffLineTerminator.Absent);
    }

    static FindingComparison<string>.Complete Complete(
        FindingComparison<string> comparison)
        => comparison switch
        {
            FindingComparison<string>.Complete complete => complete,
            _ => throw new Xunit.Sdk.XunitException("Expected a completed comparison."),
        };
}
