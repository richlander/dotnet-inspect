using DotnetInspector.Commands;
using DotnetInspector.Options;
using DotnetInspector.Output;
using DotnetInspector.Services;
using Markout;

namespace DotnetInspector.Tests;

/// <summary>
/// Tests for FindCommand output formatting via OneLineWriter.
/// </summary>
public class FindCommandTests
{
    [Fact]
    public void OneLineWriter_MultiPattern_OutputsColumnarResults()
    {
        var results = new Dictionary<string, List<TypeSearchResult>>
        {
            ["Pattern1"] = [
                new TypeSearchResult { TypeName = "Zebra", Namespace = "Animals", Kind = "class", Assembly = "Zoo" },
                new TypeSearchResult { TypeName = "Alpha", Namespace = "Greek", Kind = "struct", Assembly = "Letters" }
            ],
            ["Pattern2"] = [
                new TypeSearchResult { TypeName = "Beta", Namespace = "Greek", Kind = "interface", Assembly = "Letters" }
            ]
        };

        var view = FindOutputFormatter.BuildMultiPatternView(results);
        var sw = new StringWriter();
        var writer = new OneLineWriter(sw, showHeader: false);
        new MarkoutContext().Serialize(view, writer);
        var lines = sw.ToString().TrimEnd().Split(Environment.NewLine);

        Assert.Equal(3, lines.Length);
        Assert.Contains("Zebra", lines[0]);
        Assert.Contains("Alpha", lines[1]);
        Assert.Contains("Beta", lines[2]);
    }

    [Fact]
    public void OneLineWriter_EmptyResults_NoOutput()
    {
        var results = new Dictionary<string, List<TypeSearchResult>>();

        var view = FindOutputFormatter.BuildMultiPatternView(results);
        var sw = new StringWriter();
        var writer = new OneLineWriter(sw, showHeader: false);
        new MarkoutContext().Serialize(view, writer);

        Assert.Equal("", sw.ToString().TrimEnd());
    }

    [Fact]
    public void BuildMultiPatternView_WithNotFoundPatterns_IncludesNotFoundSection()
    {
        var results = new Dictionary<string, List<TypeSearchResult>>
        {
            ["Found*"] = [
                new TypeSearchResult { TypeName = "FoundType", Namespace = "Ns", Kind = "class", Assembly = "Lib" }
            ]
        };
        var notFound = new List<string> { "Missing1", "Missing2" };

        var view = FindOutputFormatter.BuildMultiPatternView(results, null, notFound);

        Assert.NotNull(view.NotFoundPatterns);
        Assert.Equal(2, view.NotFoundPatterns.Count);
        Assert.Contains("Missing1", view.NotFoundPatterns);
        Assert.Contains("Missing2", view.NotFoundPatterns);
    }

    [Fact]
    public void BuildMultiPatternView_AllPatternsNotFound_OnlyNotFoundSection()
    {
        var results = new Dictionary<string, List<TypeSearchResult>>();
        var notFound = new List<string> { "Bad1", "Bad2", "Bad3" };

        var view = FindOutputFormatter.BuildMultiPatternView(results, null, notFound);

        Assert.Null(view.Results);
        Assert.Null(view.PartialMatches);
        Assert.NotNull(view.NotFoundPatterns);
        Assert.Equal(3, view.NotFoundPatterns.Count);
    }

    [Fact]
    public void OneLineWriter_WithHeader_IncludesColumnHeaders()
    {
        var results = new Dictionary<string, List<TypeSearchResult>>
        {
            ["Test*"] = [
                new TypeSearchResult { TypeName = "TestA", Namespace = "Ns", Kind = "class", Assembly = "Lib" }
            ]
        };

        var view = FindOutputFormatter.BuildMultiPatternView(results);
        var sw = new StringWriter();
        var writer = new OneLineWriter(sw, showHeader: true);
        new MarkoutContext().Serialize(view, writer);
        var output = sw.ToString();

        Assert.Contains("TYPE", output);
        Assert.Contains("TestA", output);
    }

    [Fact]
    public void OneLineWriter_NoHeader_OmitsColumnHeaders()
    {
        var results = new Dictionary<string, List<TypeSearchResult>>
        {
            ["Test*"] = [
                new TypeSearchResult { TypeName = "TestA", Namespace = "Ns", Kind = "class", Assembly = "Lib" }
            ]
        };

        var view = FindOutputFormatter.BuildMultiPatternView(results);
        var sw = new StringWriter();
        var writer = new OneLineWriter(sw, showHeader: false);
        new MarkoutContext().Serialize(view, writer);
        var output = sw.ToString();

        Assert.DoesNotContain("TYPE", output);
        Assert.Contains("TestA", output);
    }
}

/// <summary>
/// Integration tests for FindCommand across platform frameworks.
/// Tests FQN/UQN matching, framework coverage, and type resolution.
/// </summary>
[Collection("Console")]
public class FindCommandIntegrationTests
{
    // ── Framework coverage tests ─────────────────────────────────────

    [Fact]
    public async Task Find_RuntimeFramework_FindsJsonSerializer()
    {
        var options = new FindOptions
        {
            Pattern = "JsonSerializer",
            PlatformFrameworks = ["runtime"]
        };

        var (exit, output, _) = await ConsoleCapture.RunAsync(
            () => FindCommand.ExecuteAsync(options));

        Assert.Equal(0, exit);
        Assert.Contains("JsonSerializer", output);
        Assert.Contains("System.Text.Json", output);
    }

    [Fact]
    public async Task Find_AspNetCoreFramework_FindsTypedResults()
    {
        // Skip if aspnetcore is not installed
        var (refPath, _, _) = PlatformResolver.ResolveFramework("aspnetcore");
        if (refPath == null)
            return;

        var options = new FindOptions
        {
            Pattern = "TypedResults",
            PlatformFrameworks = ["aspnetcore"]
        };

        var (exit, output, _) = await ConsoleCapture.RunAsync(
            () => FindCommand.ExecuteAsync(options));

        Assert.Equal(0, exit);
        Assert.Contains("TypedResults", output);
    }

    [Fact]
    public async Task Find_AspNetCoreFramework_FindsServerSentEventsResult()
    {
        // Skip if aspnetcore is not installed
        var (refPath, _, _) = PlatformResolver.ResolveFramework("aspnetcore");
        if (refPath == null)
            return;

        var options = new FindOptions
        {
            Pattern = "ServerSentEventsResult*",
            PlatformFrameworks = ["aspnetcore"]
        };

        var (exit, output, _) = await ConsoleCapture.RunAsync(
            () => FindCommand.ExecuteAsync(options));

        Assert.Equal(0, exit);
        // ServerSentEventsResult<T> is the generic version
        Assert.Contains("ServerSentEventsResult", output);
    }

    [Fact]
    public async Task Find_AspNetCoreFramework_FindsSseItem()
    {
        // Skip if aspnetcore is not installed
        var (refPath, _, _) = PlatformResolver.ResolveFramework("aspnetcore");
        if (refPath == null)
            return;

        var options = new FindOptions
        {
            Pattern = "SseItem*",
            PlatformFrameworks = ["aspnetcore"]
        };

        var (exit, output, _) = await ConsoleCapture.RunAsync(
            () => FindCommand.ExecuteAsync(options));

        Assert.Equal(0, exit);
        // SseItem<T> is the generic version
        Assert.Contains("SseItem", output);
    }

    [Fact]
    public async Task Find_NetstandardFramework_FindsIEnumerable()
    {
        // Skip if netstandard is not installed
        var (refPath, _, _) = PlatformResolver.ResolveFramework("netstandard");
        if (refPath == null)
            return;

        var options = new FindOptions
        {
            Pattern = "IEnumerable",
            PlatformFrameworks = ["netstandard"]
        };

        var (exit, output, _) = await ConsoleCapture.RunAsync(
            () => FindCommand.ExecuteAsync(options));

        Assert.Equal(0, exit);
        Assert.Contains("IEnumerable", output);
    }

    [Fact]
    public async Task Find_AllFrameworks_SearchesAllThree()
    {
        var options = new FindOptions
        {
            Pattern = "Stream",
            PlatformFrameworks = ["runtime", "aspnetcore", "netstandard"]
        };

        var (exit, output, _) = await ConsoleCapture.RunAsync(
            () => FindCommand.ExecuteAsync(options));

        Assert.Equal(0, exit);
        Assert.Contains("Stream", output);
        // Should find Stream in at least runtime
        Assert.Contains("runtime", output);
    }

    // ── FQN vs UQN tests ─────────────────────────────────────────────

    [Fact]
    public async Task Find_UQN_MatchesWithoutNamespace()
    {
        var options = new FindOptions
        {
            Pattern = "JsonSerializer",
            PlatformFrameworks = ["runtime"]
        };

        var (exit, output, _) = await ConsoleCapture.RunAsync(
            () => FindCommand.ExecuteAsync(options));

        Assert.Equal(0, exit);
        Assert.Contains("JsonSerializer", output);
    }

    [Fact]
    public async Task Find_FQN_MatchesWithNamespace()
    {
        var options = new FindOptions
        {
            Pattern = "System.Text.Json.JsonSerializer",
            PlatformFrameworks = ["runtime"]
        };

        var (exit, output, _) = await ConsoleCapture.RunAsync(
            () => FindCommand.ExecuteAsync(options));

        Assert.Equal(0, exit);
        Assert.Contains("JsonSerializer", output);
    }

    [Fact]
    public async Task Find_PartialNamespace_MatchesWithWildcard()
    {
        var options = new FindOptions
        {
            Pattern = "System.Text.*Serializer",
            PlatformFrameworks = ["runtime"]
        };

        var (exit, output, _) = await ConsoleCapture.RunAsync(
            () => FindCommand.ExecuteAsync(options));

        Assert.Equal(0, exit);
        Assert.Contains("JsonSerializer", output);
    }

    // ── Guessable vs non-guessable library tests ─────────────────────

    [Fact]
    public async Task Find_TypeInGuessableLibrary_FindsType()
    {
        // JsonSerializer is in System.Text.Json - the library name matches the namespace prefix
        var options = new FindOptions
        {
            Pattern = "JsonSerializer",
            PlatformAssemblies = ["System.Text.Json"]
        };

        var (exit, output, _) = await ConsoleCapture.RunAsync(
            () => FindCommand.ExecuteAsync(options));

        Assert.Equal(0, exit);
        Assert.Contains("JsonSerializer", output);
    }

    [Fact]
    public async Task Find_TypeInNonGuessableLibrary_RequiresFrameworkSearch()
    {
        // SortedSet`1 is a generic type - need wildcard to match
        // Finding it requires framework search to discover the library
        var options = new FindOptions
        {
            Pattern = "SortedSet*",  // SortedSet`1 is defined in System.Collections
            PlatformFrameworks = ["runtime"]
        };

        var (exit, output, _) = await ConsoleCapture.RunAsync(
            () => FindCommand.ExecuteAsync(options));

        Assert.Equal(0, exit);
        Assert.Contains("SortedSet", output);
        Assert.Contains("System.Collections", output);
    }

    [Fact]
    public async Task Find_TypeDefinedNotForwarded_FoundInExpectedLibrary()
    {
        // SortedDictionary is defined in System.Collections (not forwarded)
        var options = new FindOptions
        {
            Pattern = "SortedDictionary",
            PlatformAssemblies = ["System.Collections"]
        };

        var (exit, output, _) = await ConsoleCapture.RunAsync(
            () => FindCommand.ExecuteAsync(options));

        Assert.Equal(0, exit);
        Assert.Contains("SortedDictionary", output);
    }

    // ── Wildcard pattern tests ───────────────────────────────────────

    [Fact]
    public async Task Find_WildcardSuffix_MatchesMultipleTypes()
    {
        var options = new FindOptions
        {
            Pattern = "Json*",
            PlatformAssemblies = ["System.Text.Json"]
        };

        var (exit, output, _) = await ConsoleCapture.RunAsync(
            () => FindCommand.ExecuteAsync(options));

        Assert.Equal(0, exit);
        Assert.Contains("JsonSerializer", output);
        Assert.Contains("JsonDocument", output);
    }

    [Fact]
    public async Task Find_WildcardPrefix_MatchesMultipleTypes()
    {
        var options = new FindOptions
        {
            Pattern = "*Exception",
            PlatformAssemblies = ["System.Runtime"],
            Limit = 10
        };

        var (exit, output, _) = await ConsoleCapture.RunAsync(
            () => FindCommand.ExecuteAsync(options));

        Assert.Equal(0, exit);
        Assert.Contains("Exception", output);
    }

    [Fact]
    public async Task Find_WildcardMiddle_MatchesTypes()
    {
        var options = new FindOptions
        {
            Pattern = "Json*Options",
            PlatformAssemblies = ["System.Text.Json"]
        };

        var (exit, output, _) = await ConsoleCapture.RunAsync(
            () => FindCommand.ExecuteAsync(options));

        Assert.Equal(0, exit);
        Assert.Contains("JsonSerializerOptions", output);
    }

    [Fact]
    public async Task Find_QuestionMarkWildcard_MatchesSingleCharacter()
    {
        var options = new FindOptions
        {
            Pattern = "Int??",
            PlatformFrameworks = ["runtime"],
            Limit = 20
        };

        var (exit, output, _) = await ConsoleCapture.RunAsync(
            () => FindCommand.ExecuteAsync(options));

        Assert.Equal(0, exit);
        Assert.Contains("Int32", output);
        Assert.Contains("Int64", output);
    }

    // ── Multi-pattern tests ──────────────────────────────────────────

    [Fact]
    public async Task Find_MultiplePatterns_ReturnsResultsForEach()
    {
        var options = new FindOptions
        {
            Pattern = "JsonSerializer,JsonDocument",
            PlatformAssemblies = ["System.Text.Json"]
        };

        var (exit, output, _) = await ConsoleCapture.RunAsync(
            () => FindCommand.ExecuteAsync(options));

        Assert.Equal(0, exit);
        Assert.Contains("JsonSerializer", output);
        Assert.Contains("JsonDocument", output);
    }

    [Fact]
    public async Task Find_MultipleWildcardPatterns_ExpandsAll()
    {
        var options = new FindOptions
        {
            Pattern = "Json*,Utf8*",
            PlatformAssemblies = ["System.Text.Json"],
            Limit = 20
        };

        var (exit, output, _) = await ConsoleCapture.RunAsync(
            () => FindCommand.ExecuteAsync(options));

        Assert.Equal(0, exit);
        Assert.Contains("JsonSerializer", output);
        // Utf8JsonWriter is the class (Utf8JsonReader is a ref struct not in public API)
        Assert.Contains("Utf8JsonWriter", output);
    }

    // ── Generic type tests ───────────────────────────────────────────

    [Fact]
    public async Task Find_GenericType_WithArityNotation()
    {
        var options = new FindOptions
        {
            Pattern = "Dictionary`2",
            PlatformFrameworks = ["runtime"],
            Limit = 5
        };

        var (exit, output, _) = await ConsoleCapture.RunAsync(
            () => FindCommand.ExecuteAsync(options));

        Assert.Equal(0, exit);
        Assert.Contains("Dictionary", output);
    }

    [Fact]
    public async Task Find_GenericType_WithAngleBracketNotation()
    {
        var options = new FindOptions
        {
            Pattern = "List<T>",
            PlatformFrameworks = ["runtime"],
            Limit = 5
        };

        var (exit, output, _) = await ConsoleCapture.RunAsync(
            () => FindCommand.ExecuteAsync(options));

        Assert.Equal(0, exit);
        Assert.Contains("List", output);
    }

    // ── Limit tests ──────────────────────────────────────────────────

    [Fact]
    public async Task Find_WithLimit_RespectsLimit()
    {
        var options = new FindOptions
        {
            Pattern = "*",
            PlatformAssemblies = ["System.Runtime"],
            Limit = 5
        };

        var (exit, output, _) = await ConsoleCapture.RunAsync(
            () => FindCommand.ExecuteAsync(options));

        Assert.Equal(0, exit);
        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        // Output includes headers and formatting, but results should be limited
        Assert.True(lines.Length <= 20, "Output should be limited");
    }

    // ── JSON output tests ────────────────────────────────────────────

    [Fact]
    public async Task Find_JsonOutput_ProducesValidJson()
    {
        var options = new FindOptions
        {
            Pattern = "JsonSerializer",
            PlatformAssemblies = ["System.Text.Json"],
            JsonOutput = true
        };

        var (exit, output, _) = await ConsoleCapture.RunAsync(
            () => FindCommand.ExecuteAsync(options));

        Assert.Equal(0, exit);
        var doc = System.Text.Json.JsonDocument.Parse(output);
        Assert.NotNull(doc);
        Assert.True(doc.RootElement.GetArrayLength() > 0);
    }

    // ── Error handling tests ─────────────────────────────────────────

    [Fact]
    public async Task Find_NoScope_AppliesCuratedDefault()
    {
        var options = new FindOptions
        {
            Pattern = "Stream"
            // No scope specified - should apply curated defaults
        };

        var (exit, output, _) = await ConsoleCapture.RunAsync(
            () => FindCommand.ExecuteAsync(options));

        // Should succeed with curated scope applied
        Assert.Equal(0, exit);
        Assert.Contains("Stream", output);
    }

    [Fact]
    public async Task Find_NonExistentFramework_ShowsWarning()
    {
        var options = new FindOptions
        {
            Pattern = "Test",
            PlatformFrameworks = ["nonexistent"]
        };

        var (exit, _, error) = await ConsoleCapture.RunAsync(
            () => FindCommand.ExecuteAsync(options));

        Assert.Contains("Warning", error);
        Assert.Contains("Unknown framework", error);
    }

    // ── Multi-pattern with partial matches tests ─────────────────────

    [Fact]
    public async Task Find_MultiPattern_AllPatternsHaveExactOrPartialMatches()
    {
        // Test scenario:
        // - Good FQN type (exact match)
        // - Good UQN type (exact match)
        // - Misspelled FQN (partial match)
        // - Misspelled UQN (partial match)
        // - Glob pattern (multiple exact matches)
        var options = new FindOptions
        {
            Pattern = "System.Text.Json.JsonSerializer,JsonDocument,System.Text.Json.JsonSeriali,TypedResul,Sorted*",
            PlatformFrameworks = ["runtime", "aspnetcore", "netstandard"]
        };

        var (exit, output, _) = await ConsoleCapture.RunAsync(
            () => FindCommand.ExecuteAsync(options));

        Assert.Equal(0, exit);

        // Exact matches section
        Assert.Contains("## Results", output);

        // Good FQN - exact match
        Assert.Contains("System.Text.Json.JsonSerializer", output);
        Assert.Contains("JsonSerializer", output);

        // Good UQN - exact match
        Assert.Contains("JsonDocument", output);

        // Glob - multiple exact matches
        Assert.Contains("SortedDictionary", output);
        Assert.Contains("SortedList", output);
        Assert.Contains("SortedSet", output);

        // Partial matches section
        Assert.Contains("## Partial Matches", output);

        // Misspelled FQN - should have partial match to JsonSerializer
        Assert.Contains("System.Text.Json.JsonSeriali", output);

        // Misspelled UQN - should have partial match to TypedResults
        Assert.Contains("TypedResul", output);
        Assert.Contains("TypedResults", output);
    }

    [Fact]
    public async Task Find_MultiPattern_SomePatternsHaveNoMatches()
    {
        // Test scenario: same as above plus patterns with no matches at all
        // - Good FQN type (exact match)
        // - Good UQN type (exact match)
        // - Misspelled FQN (partial match)
        // - Misspelled UQN (partial match)
        // - Glob pattern (multiple exact matches)
        // - Bad FQN (no match at all)
        // - Bad UQN (no match at all)
        var options = new FindOptions
        {
            Pattern = "System.Text.Json.JsonSerializer,JsonDocument,System.Text.Json.JsonSeriali,TypedResul,Sorted*,System.Nonexistent.FooBarXyz,XyzNonexistent123",
            PlatformFrameworks = ["runtime", "aspnetcore", "netstandard"]
        };

        var (exit, output, _) = await ConsoleCapture.RunAsync(
            () => FindCommand.ExecuteAsync(options));

        Assert.Equal(0, exit);

        // Exact matches section should still have results
        Assert.Contains("## Results", output);

        // Good patterns should still match
        Assert.Contains("JsonSerializer", output);
        Assert.Contains("JsonDocument", output);
        Assert.Contains("SortedDictionary", output);

        // Partial matches section should have suggestions for misspellings
        Assert.Contains("## Partial Matches", output);
        Assert.Contains("TypedResults", output);

        // Not Found section should list patterns with no matches
        Assert.Contains("## Not Found", output);
        Assert.Contains("System.Nonexistent.FooBarXyz", output);
        Assert.Contains("XyzNonexistent123", output);
    }
}
