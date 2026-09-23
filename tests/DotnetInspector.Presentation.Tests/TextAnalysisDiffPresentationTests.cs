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

    static FindingComparison<string>.Complete Complete(
        FindingComparison<string> comparison)
        => comparison switch
        {
            FindingComparison<string>.Complete complete => complete,
            _ => throw new Xunit.Sdk.XunitException("Expected a completed comparison."),
        };
}
