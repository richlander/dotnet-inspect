using System.CommandLine;
using System.Collections.Concurrent;
using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Text;
using System.Text.Json;
using CSharpText;
using DotnetInspect.Cli.CommandLine;
using DotnetInspect.Cli.Commands;
using DotnetInspector.Fixtures;
using DotnetInspect.Cli.Options;
using DotnetInspect.Cli.Output;
using DotnetInspector.Packages;
using DotnetInspector.Sections;
using DotnetInspect.Cli.Views;
using ILInspector.Metadata;
using ILInspector.MetadataPrimitives;

namespace DotnetInspect.Cli.Tests;

/// <summary>
/// Focused gates for <c>match --similar</c>, the seeded structural-clone discovery surface
/// (issue #4740). These cover selection, scope, limits, and output completeness; the ranking
/// itself is owned and gated by Analysis and by the L1 retrieval query.
/// </summary>
[Collection("Console")]
public sealed partial class MatchDiscoveryTests
{
    static string TestAssembly => typeof(MatchDiscoveryTests).Assembly.Location;

    static string SampleSeed => $"{typeof(MatchDiscoverySample).FullName}.Seed";

    static MatchOptions Seeded(string seed) => new()
    {
        LeftSelector = seed,
        AssemblyPath = TestAssembly,
        IncludeAll = true,
        Similar = true,
    };

    static Task<(int ExitCode, string Output, string Error)> RunAsync(MatchOptions options)
        => ConsoleCapture.RunAsync(() => MatchCommand.ExecuteAsync(options));

    static JsonElement Parse(string output) => JsonDocument.Parse(output).RootElement;

    static RowSelectionIntent<string> Select(
        params RowSelectionIntentOperation<string>[] operations)
        => RowSelectionIntent<string>.Create(operations);

    static IEnumerable<(string Member, int Rank, int Score)> Candidates(JsonElement document)
        => document.GetProperty("candidates").EnumerateArray()
            .Select(candidate => (
                candidate.GetProperty("member").GetString()!,
                candidate.GetProperty("rank").GetInt32(),
                candidate.GetProperty("similarity").GetProperty("score").GetInt32()));

    [Fact]
    public async Task Similar_SameImage_RanksTheAuthoredExactPeerFirst()
    {
        MatchOptions options = Seeded(SampleSeed) with { JsonOutput = true };

        var (exitCode, output, error) = await RunAsync(options);

        Assert.Equal(0, exitCode);
        Assert.Empty(error);
        JsonElement document = Parse(output);
        Assert.Equal("Completed", document.GetProperty("disposition").GetString());

        var top = Candidates(document).First();
        Assert.Equal($"{typeof(MatchDiscoverySample).FullName}.ExactPeer", top.Member);
        Assert.Equal(1, top.Rank);
    }

    /// <summary>
    /// The seed's structural peer must outrank a body that is deliberately unlike it. Without this
    /// the surface could report an arbitrary order and still look successful.
    /// </summary>
    [Fact]
    public async Task Similar_RanksTheExactPeerAboveAHardNegative()
    {
        MatchOptions options = Seeded(SampleSeed) with { JsonOutput = true };

        var (exitCode, output, _) = await RunAsync(options);

        Assert.Equal(0, exitCode);
        var ranked = Candidates(Parse(output)).ToList();
        var peer = ranked.Single(
            candidate => candidate.Member == $"{typeof(MatchDiscoverySample).FullName}.ExactPeer");
        var negative = ranked.Single(
            candidate => candidate.Member == $"{typeof(MatchDiscoverySample).FullName}.HardNegative");

        Assert.True(
            peer.Score > negative.Score,
            $"Expected the exact peer to outscore the hard negative, got {peer.Score} vs {negative.Score}.");
        Assert.True(peer.Rank < negative.Rank);
    }

    /// <summary>Top selects candidate rows without shortening retrieval evidence.</summary>
    [Fact]
    public async Task Similar_TopSelectsCandidateRowsAcrossJsonAndMarkdown()
    {
        MatchOptions jsonOptions = Seeded(SampleSeed) with { JsonOutput = true };
        var (_, unbounded, _) = await RunAsync(jsonOptions);
        JsonElement complete = Parse(unbounded);
        int all = complete.GetProperty("candidates").GetArrayLength();
        string first = complete.GetProperty("candidates")[0]
            .GetProperty("member").GetString()!;
        Assert.True(all > 1, "The fixture must rank more than one candidate for this gate to bind.");

        RowSelectionIntent<string> top =
            Select(RowSelectionIntentOperation<string>.Top(1));
        var (_, bounded, _) = await RunAsync(
            jsonOptions with { RowSelection = top });
        JsonElement document = Parse(bounded);

        Assert.Equal(1, document.GetProperty("candidates").GetArrayLength());
        Assert.Equal(first, document.GetProperty("candidates")[0]
            .GetProperty("member").GetString());
        Assert.Equal(
            all,
            document.GetProperty("row_selection")
                .GetProperty("available_candidates").GetInt32());
        Assert.Equal(
            1,
            document.GetProperty("row_selection")
                .GetProperty("selected_candidates").GetInt32());
        Assert.Equal(
            all,
            document.GetProperty("receipt")
                .GetProperty("returned_candidates").GetInt32());

        var (_, markdown, _) = await RunAsync(
            Seeded(SampleSeed) with { RowSelection = top });
        Assert.Equal(1, CountRankedRows(markdown));
        Assert.Contains(first, markdown);
        Assert.Contains($"1 of {all} returned candidates selected", markdown);
    }

    [Fact]
    public async Task Similar_SemanticTailSelectsTheSameCandidateAcrossFormats()
    {
        MatchOptions completeOptions = Seeded(SampleSeed) with { JsonOutput = true };
        var (_, completeOutput, _) = await RunAsync(completeOptions);
        string expected = Parse(completeOutput)
            .GetProperty("candidates")
            .EnumerateArray()
            .Last()
            .GetProperty("member")
            .GetString()!;
        RowSelectionIntent<string> tail =
            Select(RowSelectionIntentOperation<string>.Tail(1));

        foreach (MatchOptions options in new[]
        {
            Seeded(SampleSeed) with { RowSelection = tail },
            Seeded(SampleSeed) with { RowSelection = tail, JsonOutput = true },
            Seeded(SampleSeed) with { RowSelection = tail, Tabular = true },
            Seeded(SampleSeed) with { RowSelection = tail, Tabular = true, Tsv = true },
            Seeded(SampleSeed) with { RowSelection = tail, Tabular = true, Jsonl = true },
        })
        {
            var (exitCode, output, _) = await RunAsync(options);

            Assert.Equal(0, exitCode);
            Assert.Contains(expected, output);
        }
    }

    [Fact]
    public async Task Similar_CountObservesTheSelectedCandidateSequence()
    {
        MatchOptions options = Seeded(SampleSeed) with
        {
            Count = true,
            JsonOutput = true,
            RowSelection =
                Select(RowSelectionIntentOperation<string>.Top(2)),
        };

        var (exitCode, output, error) = await RunAsync(options);

        Assert.Equal(0, exitCode);
        Assert.Equal("2", output.Trim());
        Assert.Empty(error);
    }

    [Fact]
    public async Task Similar_CliCountObservesSemanticTail()
    {
        var (exitCode, output, error) = await RunCliAsync(
            "match",
            SampleSeed,
            "--similar",
            "--library",
            TestAssembly,
            "--all",
            "-n",
            "1",
            "--tail",
            "--count",
            "--json");

        Assert.Equal(0, exitCode);
        Assert.Equal("1", output.Trim());
        Assert.Empty(error);
    }

    [Fact]
    public async Task Similar_UnavailableSemanticWindowWithholdsOutput()
    {
        MatchOptions options = Seeded(SampleSeed) with
        {
            JsonOutput = true,
            RowSelection =
                Select(RowSelectionIntentOperation<string>.Window(999, 999)),
        };

        var (exitCode, output, error) = await RunAsync(options);

        Assert.Equal(1, exitCode);
        Assert.Empty(output);
        Assert.Contains("requires row 999", error);
    }

    [Fact]
    public async Task Similar_CliUnavailableSemanticWindowWithholdsOutput()
    {
        var (exitCode, output, error) = await RunCliAsync(
            "match",
            SampleSeed,
            "--similar",
            "--library",
            TestAssembly,
            "--all",
            "--rows",
            "999..999",
            "--json");

        Assert.Equal(1, exitCode);
        Assert.Empty(output);
        Assert.Contains("requires row 999", error);
    }

    [Fact]
    public async Task Similar_SemanticSelectionDoesNotHideRetrievalFailure()
    {
        MatchOptions options = Seeded(SampleSeed) with
        {
            MaximumMethods = 1,
            JsonOutput = true,
            RowSelection =
                Select(RowSelectionIntentOperation<string>.Window(999, 999)),
        };

        var (exitCode, output, error) = await RunAsync(options);

        Assert.Equal(1, exitCode);
        Assert.Empty(error);
        JsonElement document = Parse(output);
        Assert.Equal(
            "LimitReached",
            document.GetProperty("disposition").GetString());
        Assert.NotEmpty(document.GetProperty("blockers").EnumerateArray());
    }

    [Fact]
    public async Task Similar_CliTopUsesSharedSemanticSelection()
    {
        var (exitCode, output, error) = await RunCliAsync(
            "match",
            SampleSeed,
            "--similar",
            "--library",
            TestAssembly,
            "--all",
            "--top",
            "1",
            "--json");

        Assert.Equal(0, exitCode);
        Assert.Empty(error);
        JsonElement document = Parse(output);
        Assert.Equal(1, document.GetProperty("candidates").GetArrayLength());
        Assert.Equal(
            1,
            document.GetProperty("row_selection")
                .GetProperty("selected_candidates").GetInt32());
    }

    [Fact]
    public async Task Similar_JsonLineSelectionRejectsBeforeSourceResolution()
    {
        var (exitCode, output, error) = await RunCliAsync(
            "match",
            "Example.Seed",
            "--similar",
            "--library",
            Path.Combine(Path.GetTempPath(), "missing-match-source.dll"),
            "-n",
            "1",
            "--lines",
            "--json");

        Assert.Equal(1, exitCode);
        Assert.Empty(output);
        Assert.Contains(
            "Rendered-line selection cannot be combined with JSON output.",
            error);
        Assert.DoesNotContain("not found", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Pairwise_InferredLimitRetainsRenderedLineFallback()
    {
        var (exitCode, output, error) = await RunCliAsync(
            "match",
            "Example.Left",
            "Example.Right",
            "--library",
            Path.Combine(Path.GetTempPath(), "missing-pairwise-source.dll"),
            "-n",
            "1",
            "--json");

        Assert.Equal(1, exitCode);
        Assert.Empty(output);
        Assert.Contains(
            "Rendered-line selection cannot be combined with JSON output.",
            error);
        Assert.DoesNotContain("not found", error, StringComparison.OrdinalIgnoreCase);
    }

    static int CountRankedRows(string markdown)
    {
        int index = markdown.IndexOf("## Ranked Candidates", StringComparison.Ordinal);
        Assert.True(index >= 0, "The rendered view must contain a Ranked Candidates section.");
        return markdown[index..]
            .Split('\n')
            .Count(line => line.StartsWith("| ", StringComparison.Ordinal)
                && int.TryParse(line.AsSpan(2, line.IndexOf(" |", StringComparison.Ordinal) - 2), out _));
    }

    /// <summary>
    /// <c>--max-results</c> moves the product retrieval limit and stays visible, so a bounded run
    /// is never mistaken for an exhaustive one.
    /// </summary>
    [Fact]
    public async Task Similar_MaximumResultsBoundsTheProductRetrievalAndIsReported()
    {
        MatchOptions options = Seeded(SampleSeed) with
        {
            MaximumResults = 2,
            JsonOutput = true,
        };

        var (exitCode, output, _) = await RunAsync(options);

        Assert.Equal(0, exitCode);
        JsonElement document = Parse(output);
        Assert.Equal(2, document.GetProperty("candidates").GetArrayLength());
        Assert.Equal(2, document.GetProperty("limits").GetProperty("maximum_results").GetInt32());

        JsonElement receipt = document.GetProperty("receipt");
        Assert.Equal(2, receipt.GetProperty("returned_candidates").GetInt32());
        Assert.True(
            receipt.GetProperty("ranked_candidates").GetInt32() > 2,
            "The receipt must still disclose how many candidates ranked before the limit applied.");
    }

    [Fact]
    public async Task Similar_JsonRetainsSeedOutcomeReceiptAndScoreComponents()
    {
        MatchOptions options = Seeded(SampleSeed) with { JsonOutput = true };

        var (exitCode, output, _) = await RunAsync(options);

        Assert.Equal(0, exitCode);
        JsonElement document = Parse(output);

        JsonElement seed = document.GetProperty("seed_outcome");
        Assert.Equal("Completed", seed.GetProperty("disposition").GetString());
        Assert.StartsWith("0x06", seed.GetProperty("token").GetString());

        foreach (string field in new[]
        {
            "input_methods", "processed_methods", "eligible_methods", "unsupported_methods",
            "limit_reached_methods", "failed_methods", "ranked_candidates", "returned_candidates",
            "body_productions",
        })
        {
            Assert.True(
                document.GetProperty("receipt").TryGetProperty(field, out _),
                $"The receipt must retain '{field}'.");
        }

        JsonElement similarity = document.GetProperty("candidates")[0].GetProperty("similarity");
        foreach (string component in new[]
        {
            "score", "operation_score", "position_score", "block_score", "edge_score",
            "local_score", "seed_instructions", "candidate_instructions", "seed_blocks",
            "candidate_blocks", "seed_edges", "candidate_edges", "seed_locals", "candidate_locals",
        })
        {
            Assert.True(
                similarity.TryGetProperty(component, out _),
                $"Similarity evidence must retain '{component}'.");
        }
    }

}

/// <summary>
/// A purpose-built candidate population for discovery gates: one seed, one structurally exact
/// peer, one near peer, and one deliberately unlike body. Every member is <c>int</c>-to-<c>int</c>
/// so signature suppression cannot silently remove a row a gate depends on.
/// </summary>
public static class MatchDiscoverySample
{
    public static int Seed(int value) => (value * 2) + 7;

    public static int ExactPeer(int input) => (input * 2) + 7;

    public static int NearPeer(int value) => (value * 2) + 9;

    public static int HardNegative(int value)
    {
        int total = 0;
        for (int i = 0; i < value; i++)
            total += i % 3;
        return total;
    }
}
