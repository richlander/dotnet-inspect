namespace DotnetInspector.RowSelection.Tests;

public sealed class RowQueryTextTests
{
    [Theory]
    [InlineData("System.Text.Json", "system.text.json", true)]
    [InlineData("System.Text.Json", "System.Text", false)]
    [InlineData("System.Text.Json", "System.*", true)]
    [InlineData("System.Text.Json", "*.Json", true)]
    [InlineData("System.Text.Json", "*Text*", true)]
    [InlineData("System.Text.Json", "System.????.Json", true)]
    [InlineData("System.Text.Json", "System.???.Json", false)]
    [InlineData("", "*", true)]
    [InlineData("", "?", false)]
    [InlineData("", "", true)]
    [InlineData("aXbYbZc", "a*b*c", true)]
    [InlineData("aXbYbZ", "a*b*c", false)]
    [InlineData("abc", "a**c", true)]
    public void MatchesUsesCaseInsensitiveWildcards(
        string value,
        string pattern,
        bool expected) =>
        Assert.Equal(expected, RowQueryText.Matches(value, pattern));

    [Fact]
    public void BindMapsEqualsAndNotEqualsAndRejectsOtherOperators()
    {
        var token = new RowQueryValueToken("System.*");

        Predicate<string> equals =
            Assert.IsType<Predicate<string>>(
                RowQueryText.Bind(RowQueryOperator.Equals, token));
        Predicate<string> notEquals =
            Assert.IsType<Predicate<string>>(
                RowQueryText.Bind(RowQueryOperator.NotEquals, token));
        Assert.True(equals("System.Memory"));
        Assert.False(equals("Microsoft.Extensions"));
        Assert.False(notEquals("System.Memory"));
        Assert.True(notEquals("Microsoft.Extensions"));
        Assert.Null(
            RowQueryText.Bind(RowQueryOperator.GreaterOrEqual, token));
    }

    [Fact]
    public void KeyFiltersMissingAsEmptyAndOrdersCaseInsensitively()
    {
        RowQueryKey<TextRow> name =
            RowQueryText.Key<TextRow>("Name", row => row.Name);
        RowQueryKey<TextRow> note =
            RowQueryText.Key<TextRow>(
                "Note",
                row => row.Note,
                ordered: false);
        Assert.True(name.SupportsOrdering);
        Assert.False(note.SupportsOrdering);
        RowQueryVocabulary<TextRow> vocabulary =
            RowQueryVocabulary<TextRow>.Create(
                RowQueryVocabularyIdentity.Create(),
                [name, note],
                []);
        TextRow[] rows =
        [
            new("beta", null),
            new("Alpha", "x"),
            new("gamma", "y"),
        ];

        Assert.Equal(
            ["beta"],
            Apply(
                vocabulary,
                rows,
                [
                    new RowQueryPredicateIntent(
                        "Note",
                        RowQueryOperator.Equals,
                        new RowQueryValueToken("")),
                ],
                baseline: null));
        Assert.Equal(
            ["Alpha", "beta", "gamma"],
            Apply(
                vocabulary,
                rows,
                [],
                RowQueryOrderIntent.Keys(
                [
                    new RowQueryOrderTermIntent(
                        "Name",
                        RowQueryOrderDirection.Ascending),
                ])));
    }

    private static IReadOnlyList<string> Apply(
        RowQueryVocabulary<TextRow> vocabulary,
        IReadOnlyList<TextRow> rows,
        IReadOnlyList<RowQueryPredicateIntent> predicates,
        RowQueryOrderIntent? baseline)
    {
        RowQueryResolutionResult<TextRow> resolution =
            RowQueryResolver.Resolve(
                vocabulary,
                RowQueryIntent.Create(
                    predicates,
                    baseline,
                    RowSelectionIntent<RowQueryOrderIntent>.Create([])));
        ResolvedRowQueryPlan<TextRow> plan =
            Assert.IsType<ResolvedRowQueryPlan<TextRow>>(resolution.Plan);
        RowSelectionResult<TextRow> result =
            RowQueryExecutor.Apply(rows, plan);
        Assert.True(result.IsSuccess);
        return [.. result.Values.Select(row => row.Name)];
    }

    private sealed record TextRow(string Name, string? Note);
}
