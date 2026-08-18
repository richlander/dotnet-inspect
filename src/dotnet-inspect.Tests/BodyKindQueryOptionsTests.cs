using DotnetInspector.Options;

namespace DotnetInspector.Tests;

public sealed class BodyKindQueryOptionsTests
{
    [Fact]
    public void TryExtract_ClaimsKindAndLeavesOtherSectionPredicates()
    {
        bool success = BodyKindQueryOptions.TryExtract(
            ["Kind=ObjectCreationExpression", "Member=Example"],
            out var options,
            out var remaining,
            out var error);

        Assert.True(success, error.ToString());
        Assert.Equal("ObjectCreationExpression", options.Kind);
        Assert.Equal(["Member=Example"], remaining);
    }

    [Fact]
    public void TryExtract_RejectsNonEqualityOperator()
    {
        bool success = BodyKindQueryOptions.TryExtract(
            ["Kind!=ObjectCreationExpression"],
            out _,
            out _,
            out var error);

        Assert.False(success);
        Assert.Contains("supports only =", error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void TryExtract_RejectsWronglyCasedStableIdWithExactSuggestion()
    {
        bool success = BodyKindQueryOptions.TryExtract(
            ["Kind=objectcreationexpression"],
            out _,
            out _,
            out var error);

        Assert.False(success);
        Assert.Contains(
            error.Details,
            detail => detail.Contains("case-sensitive", StringComparison.Ordinal));
        Assert.Contains(
            error.Details,
            detail => detail.Contains("ObjectCreationExpression", StringComparison.Ordinal));
    }

    [Fact]
    public void TryExtract_RejectsMoreThanOneKindPredicate()
    {
        bool success = BodyKindQueryOptions.TryExtract(
            ["Kind=ObjectCreationExpression", "Kind=LiteralExpression"],
            out _,
            out _,
            out var error);

        Assert.False(success);
        Assert.Contains("exactly one", error.ToString(), StringComparison.Ordinal);
    }
}
