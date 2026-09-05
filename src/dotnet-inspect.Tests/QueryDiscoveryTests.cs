using System.Text.Json;
using DotnetInspector.Options;
using DotnetInspector.Sections;

namespace DotnetInspector.Tests;

[Collection("Console")]
public class QueryDiscoveryTests
{
    private static Task<(int ExitCode, string Output, string Error)> Run(params string[] args)
        => ConsoleCapture.RunAsync(() =>
        {
            var root = CommandLineBuilder.CreateRootCommand();
            string[] processed = CommandLineBuilder.PreprocessArgs(args, root);
            return CommandLineBuilder.InvokeAsync(root.Parse(processed), processed);
        });

    [Theory]
    [InlineData("library")]
    [InlineData("type")]
    [InlineData("member")]
    public async Task BareQuery_ListsOnlyImplementedQuerySections(string command)
    {
        var result = await Run(command, "-Q", "--json");
        Assert.Equal(0, result.ExitCode);
        Assert.Empty(result.Error);
        using var json = JsonDocument.Parse(result.Output);
        JsonElement sections = json.RootElement.GetProperty("sections");
        Assert.Contains(sections.EnumerateArray(), section =>
            section.GetProperty("section").GetString() == "Body Shapes");
        Assert.DoesNotContain(sections.EnumerateArray(), section =>
            section.GetProperty("section").GetString() == "Top Leverage");
        foreach (JsonElement section in sections.EnumerateArray())
        {
            Assert.Equal("Query: " + section.GetProperty("section").GetString(),
                section.GetProperty("query_section").GetString());
            Assert.False(section.TryGetProperty("facets", out _));
            Assert.True(section.GetProperty("facet_count").GetInt32() > 0);
        }
    }

    [Theory]
    [InlineData("library", "Performance: Boxing")]
    [InlineData("type", "Performance Triage")]
    [InlineData("member", "Performance Triage")]
    public async Task NamedQuery_DescribesBindingsWithoutAcquiringTarget(string command, string section)
    {
        var result = await Run(command, "--package", "/missing/query-discovery.nupkg",
            "-Q", section, "--json");
        Assert.Equal(0, result.ExitCode);
        Assert.Empty(result.Error);
        using var json = JsonDocument.Parse(result.Output);
        JsonElement described = Assert.Single(json.RootElement.GetProperty("sections").EnumerateArray());
        JsonElement rootReach = Assert.Single(described.GetProperty("facets").EnumerateArray(),
            facet => facet.GetProperty("name").GetString() == "RootReach");
        Assert.Equal(["--where", "--order-by", "--top"],
            rootReach.GetProperty("operators").EnumerateArray().Select(value => value.GetString()));
        Assert.Equal(["=", "!=", ">=", "<="],
            rootReach.GetProperty("comparisons").EnumerateArray().Select(value => value.GetString()));
        Assert.Equal("integer", rootReach.GetProperty("value_kind").GetString());
    }

    [Theory]
    [InlineData("library", true)]
    [InlineData("type", false)]
    [InlineData("member", false)]
    public async Task BodyShapes_ExposesExactKindsAndOnlySupportedComposition(string command, bool composed)
    {
        var result = await Run(command, "-Q", "Body Shapes", "--json");
        Assert.Equal(0, result.ExitCode);
        using var json = JsonDocument.Parse(result.Output);
        JsonElement facets = json.RootElement.GetProperty("sections")[0].GetProperty("facets");
        JsonElement kind = facets[0];
        Assert.Equal("Kind", kind.GetProperty("name").GetString());
        Assert.Equal(["="], kind.GetProperty("comparisons").EnumerateArray().Select(value => value.GetString()));
        Assert.Equal(BodyKindQueryOptions.QueryFacet.Values,
            kind.GetProperty("values").EnumerateArray().Select(value => value.GetString()));
        Assert.Equal(composed, facets.GetArrayLength() > 1);
        Assert.All(facets.EnumerateArray(), facet =>
            Assert.Equal(["--where"],
                facet.GetProperty("operators").EnumerateArray().Select(value => value.GetString())));
    }

    [Theory]
    [InlineData("@Performance")]
    [InlineData("Performance Triage")]
    [InlineData("Performance:*")]
    public async Task QuerySelectors_ReuseCategoriesAliasesAndGlobs(string selector)
    {
        var result = await Run("library", "-Q", selector, "--json");
        Assert.Equal(0, result.ExitCode);
        using var json = JsonDocument.Parse(result.Output);
        Assert.Contains(json.RootElement.GetProperty("sections").EnumerateArray(),
            section => section.GetProperty("section").GetString() == SectionNames.PerformanceBoxing);
    }

    [Theory]
    [InlineData("-S")]
    [InlineData("-D")]
    [InlineData("--effective")]
    [InlineData("--schema")]
    [InlineData("--tree")]
    [InlineData("--print")]
    [InlineData("--bare")]
    public async Task QueryRejectsConflictingModes(string flag)
    {
        var result = await Run("library", "/missing/target.dll", "-Q", "Body Shapes", flag);
        Assert.NotEqual(0, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains(flag, result.Error);
        Assert.DoesNotContain("Unhandled", result.Error);
    }

    [Theory]
    [InlineData("--where", "Kind=ObjectCreationExpression")]
    [InlineData("--order-by", "RootReach desc")]
    [InlineData("--top", "10")]
    public async Task QueryDoesNotSilentlyApplyOrDiscardExecutionOperators(string option, string value)
    {
        var result = await Run("library", "-Q", "Body Shapes", option, value);
        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains("does not execute", result.Error);
    }

    [Theory]
    [InlineData("package", "Package Info")]
    [InlineData("find", "Packages")]
    [InlineData("library", "Top Leverage")]
    public async Task NonQueryableSection_IsExplicitNotUnknownOrCoreOnlyAdvertisement(string command, string section)
    {
        var result = await Run(command, "-Q", section, "--json");
        Assert.Equal(0, result.ExitCode);
        using var json = JsonDocument.Parse(result.Output);
        JsonElement described = json.RootElement.GetProperty("sections")[0];
        Assert.Equal(0, described.GetProperty("facet_count").GetInt32());
        Assert.Contains("no CLI query", described.GetProperty("summary").GetString());
        Assert.Empty(described.GetProperty("facets").EnumerateArray());
    }

    [Fact]
    public async Task PackageProfileQueryDiscovery_IsInertAndDoesNotAdvertiseUnwiredFacets()
    {
        var result = await Run("find", "--package-prefix", "Microsoft.", "-Q", "Packages", "--json");
        Assert.Equal(0, result.ExitCode);
        Assert.Contains("no CLI query", result.Output);
        Assert.DoesNotContain("package.query.", result.Output);
    }

    [Fact]
    public async Task UnknownSection_FailsWithSuggestions()
    {
        var result = await Run("library", "-Q", "Body Shape");
        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains("Body Shapes", result.Error);
    }

    [Fact]
    public async Task CompanionSelection_IsEquivalentAndSupportsSchemaDiscovery()
    {
        var query = await Run("type", "-Q", "Body Shapes", "--json");
        var selected = await Run("type", "-S", "Query: Body Shapes", "--json");
        Assert.Equal(0, selected.ExitCode);
        Assert.Equal(query.Output, selected.Output);
        var schema = await Run("type", "-D", "Query: Body Shapes", "--schema", "--json");
        Assert.Equal(0, schema.ExitCode);
        Assert.Contains("Operators", schema.Output);
        Assert.Contains("Comparisons", schema.Output);
    }

    [Fact]
    public async Task CompanionSelection_CannotRunAlongsideData()
    {
        var result = await Run("library", "-S", "Query: Body Shapes,Signals");
        Assert.Equal(1, result.ExitCode);
        Assert.Contains("cannot be mixed", result.Error);
        Assert.Empty(result.Output);
    }

    [Fact]
    public async Task OrdinarySchemaDiscovery_DoesNotAcquireQueryCompanions()
    {
        var result = await Run("library", "-D", "--schema", "--json");
        Assert.Equal(0, result.ExitCode);
        Assert.DoesNotContain("Query:", result.Output);
    }

    [Theory]
    [InlineData("--tsv")]
    [InlineData("--jsonl")]
    [InlineData("--json")]
    [InlineData("--plaintext")]
    public async Task QueryRows_UseSharedProjectionAndWindow(string format)
    {
        var result = await Run("type", "-Q", "Performance Triage",
            "--columns", "Facet", "--rows", "2", format);
        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Member", result.Output);
        Assert.Contains("Candidate", result.Output);
        Assert.DoesNotContain("RootReach", result.Output);
        Assert.DoesNotContain("--where \"Member", result.Output);
    }

    [Fact]
    public async Task QueryCounts_DescribeMetadataRows()
    {
        var result = await Run("type", "-Q", "Performance Triage", "--rows", "2", "--count");
        Assert.Equal(0, result.ExitCode);
        Assert.Equal("2", result.Output.Trim());
        var bare = await Run("type", "-Q", "--count");
        Assert.Equal(0, bare.ExitCode);
        Assert.Equal("2", bare.Output.Trim());
    }

    [Fact]
    public async Task MultiSectionStreams_AreRejectedRatherThanFlattened()
    {
        var result = await Run("type", "-Q", "Performance Triage,Body Shapes", "--jsonl");
        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains("one section", result.Error);
    }

    [Fact]
    public async Task LongAliasAndAttachedSelector_AreEquivalent()
    {
        var shortForm = await Run("type", "-Q", "Body Shapes", "--json");
        var longForm = await Run("type", "--query-help=Body Shapes", "--json");
        Assert.Equal(0, longForm.ExitCode);
        Assert.Equal(shortForm.Output, longForm.Output);
    }

    [Fact]
    public async Task QueryBeforeSubcommand_DoesNotFallThroughToItsExecution()
    {
        var result = await Run("package", "-Q=Packages", "search");
        Assert.NotEqual(0, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains("subcommand", result.Error);
    }

    [Fact]
    public async Task CommandlessQuery_RequiresAnAcquisitionFreeRoute()
    {
        var result = await Run("not-a-real-query-target", "-Q", "Body Shapes");
        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains("explicit command", result.Error);
    }

    [Theory]
    [InlineData("-Q", "Body Shapes", "Performance Triage")]
    [InlineData("-D", "Query: Body Shapes", "Query: Performance Triage")]
    [InlineData("-D", "Signature", "IL")]
    public async Task CommandlessRepeatedSelectors_UseParserDiagnostic(
        string option, string first, string second)
    {
        var result = await Run("Missing.Helpers", option, first, option, second);
        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains("expects a single argument", result.Error);
        Assert.DoesNotContain("InvalidOperationException", result.Error);
    }

    [Fact]
    public async Task CommandlessRepeatedCompanionSelection_RetainsListMerging()
    {
        var result = await Run("/missing/query-discovery.dll",
            "-S", "Query: Body Shapes", "-S", "Query: Performance: Arrays", "--json");
        var query = await Run("library", "-Q", "Body Shapes,Performance: Arrays", "--json");
        Assert.Equal(0, result.ExitCode);
        Assert.Empty(result.Error);
        Assert.Equal(query.Output, result.Output);
    }

    [Fact]
    public async Task ExplicitCompanionWildcard_DoesNotChangeOrdinaryDataWildcards()
    {
        var result = await Run("type", "-S", "Query: Body*", "--json");
        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Query: Body Shapes", result.Output);
        var sections = LibrarySections.CreateCatalog().Sections.SelectableSectionNames;
        Assert.DoesNotContain(sections, name => name.StartsWith("Query:"));
    }

    [Fact]
    public void DiscoveredPerformanceBindings_AreAcceptedByTheirOwner()
    {
        foreach (SectionQueryFacet facet in PerformanceTriageOptions.QueryFacets)
        {
            Assert.Equal(PerformanceTriageOptions.FilterableFields.Contains(facet.Name),
                facet.Operators.Contains("--where"));
            Assert.Equal(PerformanceTriageOptions.SortableFields.Contains(facet.Name),
                facet.Operators.Contains("--order-by"));
            Assert.Equal(facet.Operators.Contains("--order-by"), facet.Operators.Contains("--top"));
            foreach (string comparison in facet.Comparisons)
            {
                string value = facet.ValueKind switch { "integer" => "10", "rank" => "high", _ => "*" };
                var options = new PerformanceTriageOptions { Where = [$"{facet.Name}{comparison}{value}"] };
                Assert.True(options.TryGetPredicates(out _, out var error), error.ToString());
            }
            if (facet.Operators.Contains("--order-by"))
            {
                var options = new PerformanceTriageOptions { OrderBy = $"{facet.Name} desc", Top = 10 };
                Assert.True(options.TryGetOrderTerms(out _, out var error), error.ToString());
            }
        }
    }
}
