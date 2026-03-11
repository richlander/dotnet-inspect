using DotnetInspector.Commands;
using DotnetInspector.Models;
using DotnetInspector.Options;
using DotnetInspector.Output;
using DotnetInspector.Services;
using DotnetInspector.Views;
using Markout;

namespace DotnetInspector.Tests;

/// <summary>
/// Tests for FindCommand output formatting via OneLineFormatter.
/// </summary>
public class FindCommandTests
{
    [Fact]
    public void OneLineFormatter_MultiPattern_OutputsColumnarResults()
    {
        var results = new List<TypeFindResult>
        {
            new() { Pattern = "Pattern1", Match = MatchKind.Exact, Similarity = 1.0,
                     Type = "Zebra", Namespace = "Animals", Kind = "class", Library = "Zoo", Source = "runtime" },
            new() { Pattern = "Pattern1", Match = MatchKind.Exact, Similarity = 1.0,
                     Type = "Alpha", Namespace = "Greek", Kind = "struct", Library = "Letters", Source = "runtime" },
            new() { Pattern = "Pattern2", Match = MatchKind.Exact, Similarity = 1.0,
                     Type = "Beta", Namespace = "Greek", Kind = "interface", Library = "Letters", Source = "runtime" }
        };

        var view = FindOutputFormatter.BuildView(results);
        var sw = new StringWriter();
        new MarkoutContext().Serialize(view, sw, new OneLineFormatter(showHeader: false));
        var lines = sw.ToString().TrimEnd().Split(Environment.NewLine);

        Assert.Equal(3, lines.Length);
        Assert.Contains("Zebra", lines[0]);
        Assert.Contains("Alpha", lines[1]);
        Assert.Contains("Beta", lines[2]);
    }

    [Fact]
    public void OneLineFormatter_EmptyResults_NoOutput()
    {
        var view = FindOutputFormatter.BuildView([]);
        var sw = new StringWriter();
        new MarkoutContext().Serialize(view, sw, new OneLineFormatter(showHeader: false));

        // OneLineFormatter doesn't support paragraphs (no IBlockFormatter),
        // so the description is not rendered
        Assert.Equal("", sw.ToString().TrimEnd());
    }

    [Fact]
    public void BuildView_WithNotFoundPatterns_IncludesNotFoundRows()
    {
        var results = new List<TypeFindResult>
        {
            new() { Pattern = "Found*", Match = MatchKind.Glob, Similarity = 1.0,
                     Type = "FoundType", Namespace = "Ns", Kind = "class", Library = "Lib", Source = "runtime" },
            new() { Pattern = "Missing1", Match = MatchKind.NotFound },
            new() { Pattern = "Missing2", Match = MatchKind.NotFound }
        };

        var view = FindOutputFormatter.BuildView(results);

        Assert.NotNull(view.Results);
        Assert.Equal(3, view.Results.Count);
        Assert.Equal("notfound", view.Results[1].Match);
        Assert.Equal("notfound", view.Results[2].Match);
        Assert.Equal("-", view.Results[1].Type);
    }

    [Fact]
    public void BuildView_AllPatternsNotFound_NullsResultsWithDescription()
    {
        var results = new List<TypeFindResult>
        {
            new() { Pattern = "Bad1", Match = MatchKind.NotFound },
            new() { Pattern = "Bad2", Match = MatchKind.NotFound },
            new() { Pattern = "Bad3", Match = MatchKind.NotFound }
        };

        var view = FindOutputFormatter.BuildView(results);

        Assert.Null(view.Results);
        Assert.Equal(0, view.Matches);
        Assert.Equal("No types found matching the pattern.", view.Description);
    }

    [Fact]
    public void OneLineFormatter_WithHeader_IncludesColumnHeaders()
    {
        var results = new List<TypeFindResult>
        {
            new() { Pattern = "Test*", Match = MatchKind.Glob, Similarity = 1.0,
                     Type = "TestA", Namespace = "Ns", Kind = "class", Library = "Lib", Source = "runtime" }
        };

        var view = FindOutputFormatter.BuildView(results);
        var sw = new StringWriter();
        new MarkoutContext().Serialize(view, sw, new OneLineFormatter(showHeader: true));
        var output = sw.ToString();

        Assert.Contains("TYPE", output);
        Assert.Contains("TestA", output);
    }

    [Fact]
    public void OneLineFormatter_NoHeader_OmitsColumnHeaders()
    {
        var results = new List<TypeFindResult>
        {
            new() { Pattern = "Test*", Match = MatchKind.Glob, Similarity = 1.0,
                     Type = "TestA", Namespace = "Ns", Kind = "class", Library = "Lib", Source = "runtime" }
        };

        var view = FindOutputFormatter.BuildView(results);
        var sw = new StringWriter();
        new MarkoutContext().Serialize(view, sw, new OneLineFormatter(showHeader: false));
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

        var (exit, output, error) = await ConsoleCapture.RunAsync(
            () => FindCommand.ExecuteAsync(options));

        Assert.Equal(0, exit);
        // SseItem<T> is the generic version; if not found, verify friendly message
        Assert.True(
            output.Contains("SseItem") || error.Contains("No types found"),
            "Expected either SseItem in output or 'No types found' message");
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

        var (exit, output, error) = await ConsoleCapture.RunAsync(
            () => FindCommand.ExecuteAsync(options));

        Assert.Equal(0, exit);
        Assert.True(
            output.Contains("Exception") || error.Contains("No types found"),
            "Expected either Exception types in output or 'No types found' message");
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
    public async Task Find_JsonOutput_ProducesValidJsonl()
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
        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.True(lines.Length > 0);
        foreach (var line in lines)
        {
            var doc = System.Text.Json.JsonDocument.Parse(line);
            Assert.Equal(System.Text.Json.JsonValueKind.Object, doc.RootElement.ValueKind);
        }
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

        // Results section contains all match types
        Assert.Contains("## Results", output);

        // Good FQN - exact match
        Assert.Contains("JsonSerializer", output);

        // Good UQN - exact match
        Assert.Contains("JsonDocument", output);

        // Glob - multiple exact matches
        Assert.Contains("SortedDictionary", output);
        Assert.Contains("SortedList", output);
        Assert.Contains("SortedSet", output);

        // Misspelled patterns appear in Match column as "partial"
        Assert.Contains("partial", output);

        // Misspelled UQN - should have partial match to TypedResults
        Assert.Contains("TypedResul", output);
        Assert.Contains("TypedResults", output);
    }

    [Fact]
    public async Task Find_MultiPattern_SomePatternsHaveNoMatches()
    {
        // Test scenario: same as above plus patterns with no matches at all
        var options = new FindOptions
        {
            Pattern = "System.Text.Json.JsonSerializer,JsonDocument,System.Text.Json.JsonSeriali,TypedResul,Sorted*,System.Nonexistent.FooBarXyz,XyzNonexistent123",
            PlatformFrameworks = ["runtime", "aspnetcore", "netstandard"]
        };

        var (exit, output, _) = await ConsoleCapture.RunAsync(
            () => FindCommand.ExecuteAsync(options));

        Assert.Equal(0, exit);

        // Results section contains all match types in one table
        Assert.Contains("## Results", output);

        // Good patterns should still match
        Assert.Contains("JsonSerializer", output);
        Assert.Contains("JsonDocument", output);
        Assert.Contains("SortedDictionary", output);

        // Partial matches appear as rows with "partial" match kind
        Assert.Contains("partial", output);
        Assert.Contains("TypedResults", output);

        // Not found patterns appear as rows with "notfound" match kind
        Assert.Contains("notfound", output);
        Assert.Contains("System.Nonexistent.FooBarXyz", output);
        Assert.Contains("XyzNonexistent123", output);
    }
}
