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
/// Find's discovery-order classification over the real System.Text.Json
/// assembly and .NET Platform, whose names motivated it.
/// </summary>
[Collection("Console")]
public class FindMatchTierTests
{
    private static readonly string SystemTextJson =
        typeof(JsonSerializer).Assembly.Location;

    public FindMatchTierTests()
        => PersistentCache.Initialize("dotnet-inspect-test");

    [Fact]
    public async Task
        FindTypesAsync_PreservesDiscoveryOrderAcrossMatchClasses()
    {
        List<TypeFindResult> rows =
            await FindAsync("JsonSerializer");

        Assert.Equal(
            [
                "System.Text.Json.JsonSerializerDefaults",
                "System.Text.Json.JsonSerializer",
                "System.Text.Json.JsonSerializerOptions",
            ],
            rows.Take(3).Select(static row => row.FullName));
        Assert.Equal(
            [
                TypeFindMatchKind.Prefix,
                TypeFindMatchKind.Exact,
                TypeFindMatchKind.Prefix,
            ],
            rows.Take(3).Select(static row => row.Match));
    }

    [Fact]
    public async Task
        FindTypesAsync_LimitKeepsFirstDiscoveredCandidate()
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
        Assert.Equal(
            FindSearchCompletion.ResultLimitReached,
            search.Completion);
    }

    [Fact]
    public async Task
        FindTypesAsync_ExactOnlyRejectsEarlierBroaderCandidates()
    {
        using var httpClient = new HttpClient();
        const string pattern = "JsonSerializer";
        FindSearchResult<TypeFindResult> search =
            await TypeSearchService.FindTypesAsync(
                new FindOptions
                {
                    Pattern = pattern,
                    Assemblies =
                    [
                        typeof(JsonSerializer).Assembly.Location,
                    ],
                    Limit = 1,
                    TypeMatchIntent = FindTypeMatchIntent.ExactOnly,
                },
                [pattern],
                new VerboseLogger(enabled: false),
                httpClient,
                TestContext.Current.CancellationToken);

        TypeFindResult row = Assert.Single(search.Rows);
        Assert.Equal(
            "System.Text.Json.JsonSerializer",
            row.FullName);
        Assert.Equal(TypeFindMatchKind.Exact, row.Match);
        Assert.Equal(
            FindSearchCompletion.ResultLimitReached,
            search.Completion);
    }

    [Fact]
    public async Task
        FindTypesAsync_ExactOnlyRecordsSourceExhaustionBelowHead()
    {
        using var httpClient = new HttpClient();
        const string pattern = "JsonSerialiser";
        FindSearchResult<TypeFindResult> search =
            await TypeSearchService.FindTypesAsync(
                new FindOptions
                {
                    Pattern = pattern,
                    Assemblies =
                    [
                        typeof(JsonSerializer).Assembly.Location,
                    ],
                    Limit = 1,
                    TypeMatchIntent = FindTypeMatchIntent.ExactOnly,
                },
                [pattern],
                new VerboseLogger(enabled: false),
                httpClient,
                TestContext.Current.CancellationToken);

        Assert.Empty(search.Rows);
        Assert.Equal(
            FindSearchCompletion.Exhausted,
            search.Completion);
    }

    [Fact]
    public async Task FindTypesAsync_DottedPrefixKeepsEffectiveWildcard()
    {
        List<TypeFindResult> rows =
            await FindAsync("System.Text.Json.JsonSer");

        Assert.NotEmpty(rows);
        Assert.All(
            rows.Where(
                static row =>
                    row.Match == TypeFindMatchKind.Prefix),
            static row =>
                Assert.Equal(
                    "System.Text.Json.JsonSer*",
                    row.Pattern));
        Assert.All(
            rows.Where(
                static row =>
                    row.Match == TypeFindMatchKind.Partial),
            static row =>
                Assert.Equal(
                    "System.Text.Json.JsonSer",
                    row.Pattern));
    }

    [Fact]
    public async Task FindTypesAsync_SubstringDoesNotExcludeSimilarRows()
    {
        List<TypeFindResult> rows = await FindAsync("Serializer");

        Assert.NotEmpty(rows);
        Assert.Contains(
            rows,
            static row => row.Match == TypeFindMatchKind.Substring);
        Assert.Contains(
            rows,
            static row => row.Match == TypeFindMatchKind.Partial);
    }

    [Fact]
    public async Task FindTypesAsync_ExactDoesNotSettleBeforePrefix()
    {
        List<TypeFindResult> rows = await FindAsync("JsonSerializer");

        Assert.Contains(
            rows,
            static row => row.Match == TypeFindMatchKind.Exact);
        Assert.Contains(
            rows,
            static row => row.Match == TypeFindMatchKind.Prefix);
    }

    [Fact]
    public async Task FindTypesAsync_SimilarRowsPreserveDiscoveryOrder()
    {
        List<TypeFindResult> rows = await FindAsync("JsonSerialiser");

        Assert.True(rows.Count >= 3);
        Assert.All(
            rows,
            static row => Assert.Equal(TypeFindMatchKind.Partial, row.Match));
        Assert.Equal(
            [
                "System.Text.Json.JsonSerializerDefaults",
                "System.Text.Json.JsonSerializer",
                "System.Text.Json.JsonSerializerOptions",
            ],
            rows.Take(3).Select(static row => row.FullName));
        Assert.True(rows[0].Similarity < rows[1].Similarity);
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
    public async Task Find_HeadStillLetsMemberNameOutrankWeakTypeEvidence()
    {
        var options = new FindOptions
        {
            Pattern = "AppendFormat",
            PlatformFrameworks = ["runtime"],
            Limit = 1,
        };

        var (exit, output, _) = await ConsoleCapture.RunAsync(
            () => FindCommand.ExecuteAsync(options));

        Assert.Equal(0, exit);
        Assert.Contains("## Members", output);
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
    public async Task FindTypesAsync_LocatorPathUsesSameClassification()
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
        Assert.Equal(
            "System.Text.Json.JsonSerializerDefaults",
            result.Rows[0].FullName);
        Assert.Contains(
            result.Rows,
            static row => row.Match == TypeFindMatchKind.Prefix);
        Assert.Contains(
            result.Rows,
            static row => row.Match == TypeFindMatchKind.Partial);
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
