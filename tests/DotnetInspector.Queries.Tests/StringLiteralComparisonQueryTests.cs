using DotnetInspector.Fixtures;
using ILInspector.Analysis;
using ILInspector.Research;
using Inspector.Findings;
using QuerySpace;

namespace DotnetInspector.Queries.Tests;

public sealed class StringLiteralComparisonQueryTests
{
    [Theory]
    [InlineData(PortableQueryOperator.Contains)]
    [InlineData(PortableQueryOperator.StartsWith)]
    public void Vocabulary_resolves_one_exact_literal_predicate(
        PortableQueryOperator @operator)
    {
        PortableQueryIntent intent =
            StringLiteralComparisonQuery.CreateIntent(
                @operator,
                "https://");

        var accepted =
            Assert.IsType<
                StringLiteralComparisonQueryPlanResult.Accepted>(
                StringLiteralComparisonQuery.ResolveIntent(
                    intent,
                    TestContext.Current.CancellationToken));

        Assert.Equal(
            StringLiteralComparisonQuery.LiteralKey,
            accepted.Plan.Term.Key);
        Assert.Equal(@operator, accepted.Plan.Term.Operator);
        Assert.Equal("https://", accepted.Plan.Term.Value);
        Assert.Equal(
            @operator == PortableQueryOperator.Contains
                ? StringLiteralUsePredicateKind.Contains
                : StringLiteralUsePredicateKind.StartsWith,
            accepted.Plan.Predicate.Kind);
    }

    [Fact]
    public void Vocabulary_rejects_missing_unknown_and_conflicting_terms()
    {
        Assert.Equal(
            PortableQueryFailureReason.RequiredTermFamilyMissing,
            Rejection(PortableQueryIntent.Empty).Reason);
        Assert.Equal(
            PortableQueryFailureReason.UnknownKey,
            Rejection(PortableQueryIntent.Create(
                [
                    new PortableQueryTerm(
                        "literal",
                        PortableQueryOperator.Contains,
                        "https://"),
                ],
                [],
                [],
                [])).Reason);
        Assert.Equal(
            PortableQueryFailureReason.TermsIncompatible,
            Rejection(PortableQueryIntent.Create(
                [
                    new PortableQueryTerm(
                        StringLiteralComparisonQuery.LiteralKey,
                        PortableQueryOperator.Contains,
                        "https://"),
                    new PortableQueryTerm(
                        StringLiteralComparisonQuery.LiteralKey,
                        PortableQueryOperator.StartsWith,
                        "https://"),
                ],
                [],
                [],
                [])).Reason);
    }

    [Fact]
    public void Path_comparison_retains_one_complete_literal_per_physical_use()
    {
        StringLiteralComparisonQueryPlan plan =
            Assert.IsType<
                StringLiteralComparisonQueryPlanResult.Accepted>(
                StringLiteralComparisonQuery.ResolveIntent(
                    StringLiteralComparisonQuery.CreateIntent(
                        PortableQueryOperator.StartsWith,
                        "https://"),
                    TestContext.Current.CancellationToken)).Plan;
        string path =
            FixtureCatalog.AnalysisStringLiterals.AssemblyPath();

        RetainedFindingComparisonSet result =
            StringLiteralComparisonQuery.ExecutePaths(
                path,
                path,
                "library:fixture",
                "String literal fixture",
                plan,
                cancellationToken:
                    TestContext.Current.CancellationToken);

        RetainedFindingComparison<StringLiteralUseOccurrence> retained =
            Assert.Single(
                result.Get<StringLiteralUseOccurrence>(
                    StringLiteralUseFindings.Descriptor));
        FindingComparison<StringLiteralUseOccurrence>.Complete comparison =
            Assert.IsType<
                FindingComparison<StringLiteralUseOccurrence>.Complete>(
                retained.Comparison.Value);
        PairFinding<StringLiteralUseOccurrence>.Present pair =
            Assert.IsType<
                PairFinding<StringLiteralUseOccurrence>.Present>(
                Assert.Single(comparison.Pairs).Value);
        const string literal =
            "https://first.example and https://second.example";
        Assert.Equal(literal, pair.Old.Payload.LiteralText.ToString());
        Assert.Equal(literal, pair.New.Payload.LiteralText.ToString());
    }

    private static PortableQueryFailure Rejection(
        PortableQueryIntent intent)
        => Assert.IsType<
                StringLiteralComparisonQueryPlanResult.Rejected>(
                StringLiteralComparisonQuery.ResolveIntent(intent))
            .Failure;
}
