using System.Text.Json;
using DotnetInspect.Cli.Commands;
using DotnetInspect.Cli.Inspectors;
using DotnetInspect.Cli.Models;
using DotnetInspect.Cli.Options;
using DotnetInspect.Cli.Output;
using DotnetInspector.Cache;
using QuerySpace.Rows;

namespace DotnetInspect.Cli.Tests;

/// <summary>
/// Find's tier ladder (find-search-service.md#classification) over the real
/// System.Text.Json assembly and .NET Platform, whose names motivated it.
/// </summary>
[Collection("Console")]
public class FindMatchTierTests
{
    private static readonly string SystemTextJson =
        typeof(JsonSerializer).Assembly.Location;

    public FindMatchTierTests()
        => PersistentCache.Initialize("dotnet-inspect-test");

    [Theory]
    [InlineData("JsonSer")]
    [InlineData("JsonSer,NoSuchTypeName")]
    public async Task FindTypesAsync_PrefixRanksShortestCompletionFirst(
        string patternList)
    {
        List<TypeFindResult> rows = await FindAsync(patternList);

        List<TypeFindResult> prefix =
            [.. rows.Where(static row => row.Pattern == "JsonSer")];
        Assert.NotEmpty(prefix);
        Assert.All(
            prefix,
            static row => Assert.Equal(TypeFindMatchKind.Prefix, row.Match));
        Assert.Equal("System.Text.Json.JsonSerializer", prefix[0].FullName);
        Assert.Equal(
            prefix.Count,
            prefix.Select(static row => row.FullName).Distinct().Count());
    }

    [Fact]
    public async Task
        FindTypesAsync_LimitOrdersOnlyThePopulationFoundBeforeTheLimit()
    {
        using var httpClient = new HttpClient();
        const string pattern = "FindFoundPopulationOrder";
        FindSearchResult<TypeFindResult> search =
            await TypeSearchService.FindTypesAsync(
                new FindOptions
                {
                    Pattern = pattern,
                    Assemblies =
                    [
                        typeof(
                            FindFoundPopulationOrderMuchLonger)
                            .Assembly.Location,
                    ],
                    Limit = 1,
                },
                [pattern],
                new VerboseLogger(enabled: false),
                httpClient,
                TestContext.Current.CancellationToken);

        TypeFindResult row = Assert.Single(search.Rows);
        Assert.Equal(
            typeof(FindFoundPopulationOrderMuchLonger).FullName,
            row.FullName);
        Assert.Equal(TypeFindMatchKind.Prefix, row.Match);
    }

    [Fact]
    public async Task FindTypesAsync_DottedPrefixKeepsEffectiveWildcard()
    {
        List<TypeFindResult> rows =
            await FindAsync("System.Text.Json.JsonSer");

        Assert.NotEmpty(rows);
        Assert.All(rows, static row =>
        {
            Assert.Equal("System.Text.Json.JsonSer*", row.Pattern);
            Assert.Equal(TypeFindMatchKind.Prefix, row.Match);
        });
        Assert.Equal("System.Text.Json.JsonSerializer", rows[0].FullName);
    }

    [Fact]
    public async Task FindTypesAsync_SubstringFollowsEmptyPrefix()
    {
        List<TypeFindResult> rows = await FindAsync("Serializer");

        Assert.NotEmpty(rows);
        Assert.All(
            rows,
            static row => Assert.Equal(TypeFindMatchKind.Substring, row.Match));
        Assert.Equal("System.Text.Json.JsonSerializer", rows[0].FullName);
    }

    [Fact]
    public async Task FindTypesAsync_DirectTierStillSettlesBeforePrefix()
    {
        List<TypeFindResult> rows = await FindAsync("JsonSerializer");

        TypeFindResult row = Assert.Single(rows);
        Assert.Equal(TypeFindMatchKind.Direct, row.Match);
    }

    [Fact]
    public async Task FindTypesAsync_PartialSuggestionsOrderBySimilarity()
    {
        List<TypeFindResult> rows = await FindAsync("JsonSerialiser");

        Assert.NotEmpty(rows);
        Assert.All(
            rows,
            static row => Assert.Equal(TypeFindMatchKind.Partial, row.Match));
        Assert.Equal("System.Text.Json.JsonSerializer", rows[0].FullName);
        Assert.Equal(
            rows.Select(static row => row.Similarity)
                .OrderByDescending(static similarity => similarity),
            rows.Select(static row => row.Similarity));
    }

    [Fact]
    public async Task Find_MemberNameOutranksWeakTypeEvidence()
    {
        var options = new FindOptions
        {
            Pattern = "AppendFormat",
            PlatformFrameworks = ["runtime"],
        };

        var (exit, output, _) = await ConsoleCapture.RunAsync(
            () => FindCommand.ExecuteAsync(options));

        Assert.Equal(0, exit);
        int members = output.IndexOf("## Members", StringComparison.Ordinal);
        Assert.True(members >= 0, output);
        Assert.Contains("System.Text.StringBuilder", output);
        Assert.DoesNotContain("DateFormat", output);
    }

    [Fact]
    public async Task Find_MemberTierIsVisibleWhenJsonOmitsIt()
    {
        var options = new FindOptions
        {
            Pattern = "AppendFormat",
            PlatformFrameworks = ["runtime"],
            JsonOutput = true,
        };

        var (exit, output, error) = await ConsoleCapture.RunAsync(
            () => FindCommand.ExecuteAsync(options));

        Assert.Equal(0, exit);
        Assert.Equal("[]", output.Trim());
        Assert.Contains("find .AppendFormat", error);
    }

    [Fact]
    public async Task Find_CountRejectsMemberTierAnswer()
    {
        var options = new FindOptions
        {
            Pattern = "AppendFormat",
            PlatformFrameworks = ["runtime"],
            Count = true,
        };

        var (exit, _, error) = await ConsoleCapture.RunAsync(
            () => FindCommand.ExecuteAsync(options));

        Assert.Equal(1, exit);
        Assert.Contains("member matches", error);
    }

    [Fact]
    public async Task Find_PrefixTypeAnswerDoesNotSearchMembers()
    {
        var options = new FindOptions
        {
            Pattern = "StringBuild",
            PlatformFrameworks = ["runtime"],
        };

        var (exit, output, _) = await ConsoleCapture.RunAsync(
            () => FindCommand.ExecuteAsync(options));

        Assert.Equal(0, exit);
        Assert.DoesNotContain("## Members", output);
        Assert.Contains("| StringBuilder |", output);
    }

    [Fact]
    public async Task Find_RowSelectionHeadSelectsMembersBeforeTypes()
    {
        var options = new FindOptions
        {
            Pattern = "Parse",
            PlatformFrameworks = ["runtime"],
            RowSelection = RowSelectionIntent<string>.Create(
                [RowSelectionIntentOperation<string>.Head(2)]),
        };

        var (exit, output, _) = await ConsoleCapture.RunAsync(
            () => FindCommand.ExecuteAsync(options));

        Assert.Equal(0, exit);
        Assert.Equal(2, MemberRows(output).Length);
        Assert.DoesNotContain("## Results", output);
    }

    [Fact]
    public async Task Find_RowSelectionWindowCrossesFromMembersIntoTypes()
    {
        var unselected = new FindOptions
        {
            Pattern = "Parse",
            PlatformFrameworks = ["runtime"],
        };
        var (_, all, _) = await ConsoleCapture.RunAsync(
            () => FindCommand.ExecuteAsync(unselected));
        int memberCount = MemberRows(all).Length;
        Assert.True(memberCount > 0, all);
        Assert.Contains("## Results", all);

        var windowed = unselected with
        {
            RowSelection = RowSelectionIntent<string>.Create(
                [
                    RowSelectionIntentOperation<string>.Window(
                        memberCount,
                        memberCount + 1),
                ]),
        };
        var (exit, output, _) = await ConsoleCapture.RunAsync(
            () => FindCommand.ExecuteAsync(windowed));

        Assert.Equal(0, exit);
        Assert.Single(MemberRows(output));
        int results = output.IndexOf("## Results", StringComparison.Ordinal);
        Assert.True(results >= 0, output);
        Assert.Single(
            output[results..].Split('\n'),
            static line => line.StartsWith("| ", StringComparison.Ordinal)
                && !line.StartsWith("| Type", StringComparison.Ordinal)
                && !line.StartsWith("| -", StringComparison.Ordinal));
    }

    private static string[] MemberRows(string output) =>
    [
        .. output.Split('\n').Where(static line =>
            line.StartsWith("| Parse |", StringComparison.Ordinal)),
    ];

    [Fact]
    [Trait("Speed", "Slow")]
    public async Task FindTypesAsync_LocatorPathUsesSameTiers()
    {
        using var httpClient = new HttpClient();
        var options = new FindOptions
        {
            Pattern = "JsonSer",
            Packages = ["System.Text.Json@10.0.0"],
            Tfm = "net10.0",
        };

        FindSearchResult<TypeFindResult> result =
            await TypeSearchService.FindTypesAsync(
                options,
                [options.Pattern],
                new VerboseLogger(enabled: false),
                httpClient,
                TestContext.Current.CancellationToken);

        Assert.False(result.HasFailures);
        Assert.NotEmpty(result.LocatorSections);
        Assert.All(
            result.Rows,
            static row => Assert.Equal(TypeFindMatchKind.Prefix, row.Match));
        Assert.Equal(
            "System.Text.Json.JsonSerializer",
            result.Rows[0].FullName);
    }

    private static async Task<List<TypeFindResult>> FindAsync(
        string patternList)
    {
        using var httpClient = new HttpClient();
        string[] patterns = patternList.Split(',');
        FindSearchResult<TypeFindResult> search =
            await TypeSearchService.FindTypesAsync(
                new FindOptions
                {
                    Pattern = patternList,
                    Assemblies = [SystemTextJson],
                },
                patterns,
                new VerboseLogger(enabled: false),
                httpClient,
                TestContext.Current.CancellationToken);
        Assert.False(search.HasFailures);
        return search.Rows;
    }
}

public sealed class FindFoundPopulationOrderMuchLonger
{
}

public sealed class FindFoundPopulationOrderZ
{
}
