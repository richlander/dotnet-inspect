using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Text.Json;
using DotnetInspector.Views;
using ILInspector.Metadata;
using ILInspector.MetadataPrimitives;
using DotnetInspector.Commands;
using DotnetInspector.Options;
using System.CommandLine;
using DotnetInspector.CommandLine;
using DotnetInspector.Fixtures;

namespace DotnetInspector.Tests;

/// <summary>
/// Focused gates for <c>match --similar</c>, the seeded structural-clone discovery surface
/// (issue #4740). These cover selection, scope, limits, and output completeness; the ranking
/// itself is owned and gated by Analysis and by the L1 retrieval query.
/// </summary>
[Collection("Console")]
public sealed class MatchDiscoveryTests
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

    [Fact]
    public async Task Similar_CrossImage_RanksTheSurvivingMemberFirst()
    {
        MatchOptions options = new()
        {
            LeftSelector = "DiffSample.Stable",
            AssemblyPath =
                $"{FixtureCatalog.DiffV1.AssemblyPath()}..{FixtureCatalog.DiffV2.AssemblyPath()}",
            IncludeAll = true,
            Similar = true,
            JsonOutput = true,
        };

        var (exitCode, output, error) = await RunAsync(options);

        Assert.Equal(0, exitCode);
        Assert.Empty(error);
        JsonElement document = Parse(output);
        Assert.Equal("Completed", document.GetProperty("disposition").GetString());
        Assert.Equal(
            FixtureCatalog.DiffV2.AssemblyPath(),
            document.GetProperty("candidate_assembly").GetString());

        var top = Candidates(document).First();
        Assert.Equal("DiffFixtureSample.DiffSample.Stable", top.Member);
        Assert.Equal(10_000, top.Score);
    }

    /// <summary>
    /// <c>--top</c> is a presentation control. Structured output must keep every candidate the
    /// query returned, so evidence is never silently discarded by a text-shaping flag.
    /// </summary>
    [Fact]
    public async Task Similar_TopBoundsTextRowsWithoutTruncatingJson()
    {
        MatchOptions jsonOptions = Seeded(SampleSeed) with { JsonOutput = true };
        var (_, unbounded, _) = await RunAsync(jsonOptions);
        int all = Parse(unbounded).GetProperty("candidates").GetArrayLength();
        Assert.True(all > 1, "The fixture must rank more than one candidate for this gate to bind.");

        var (_, bounded, _) = await RunAsync(jsonOptions with { Top = 1 });
        JsonElement document = Parse(bounded);

        Assert.Equal(all, document.GetProperty("candidates").GetArrayLength());
        Assert.Equal(1, document.GetProperty("limits").GetProperty("text_rows").GetInt32());

        var (_, markdown, _) = await RunAsync(Seeded(SampleSeed) with { Top = 1 });
        Assert.Equal(1, CountRankedRows(markdown));
        Assert.Contains($"1 of {all} ranked candidates", markdown);
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

    /// <summary>
    /// Retrieval selects candidates; it does not decide a relation. The rendered output has to say
    /// so, because a ranked table otherwise reads as a verdict.
    /// </summary>
    [Fact]
    public async Task Similar_DisclosesThatRankingIsNotAVerdict()
    {
        var (exitCode, output, _) = await RunAsync(Seeded(SampleSeed));

        Assert.Equal(0, exitCode);
        Assert.Contains("Ranks structural candidates only", output);
        Assert.Contains("does not establish Exact, Near, or Different", output);
        Assert.Contains("authorship, copying intent, or vulnerability", output);
    }

    [Fact]
    public async Task Similar_AmbiguousSeed_RequiresANarrowerSelector()
    {
        var (exitCode, output, error) = await RunAsync(
            Seeded($"{typeof(MatchSampleA).FullName}.Overloaded"));

        Assert.Equal(1, exitCode);
        Assert.Empty(output);
        Assert.Contains("matches 2 overloads", error);
        Assert.Contains("narrow the pattern", error);
    }

    /// <summary>
    /// A MethodDef token is the unambiguous escape hatch the overload error points at.
    /// </summary>
    [Fact]
    public async Task Similar_MethodDefTokenSeed_Resolves()
    {
        int token = typeof(MatchDiscoverySample)
            .GetMethod(nameof(MatchDiscoverySample.Seed))!.MetadataToken;

        MatchOptions options = Seeded($"0x{token:X8}") with { JsonOutput = true };

        var (exitCode, output, error) = await RunAsync(options);

        Assert.Equal(0, exitCode);
        Assert.Empty(error);
        JsonElement document = Parse(output);
        Assert.Equal($"0x{token:X8}", document.GetProperty("seed_outcome").GetProperty("token").GetString());
    }

    [Fact]
    public async Task Similar_UnknownSeed_IsAVisibleFailure()
    {
        var (exitCode, output, error) = await RunAsync(
            Seeded($"{typeof(MatchSampleA).FullName}.NoSuchMember"));

        Assert.Equal(1, exitCode);
        Assert.Empty(output);
        Assert.Contains("NoSuchMember", error);
    }

    [Fact]
    public async Task Similar_UnknownCandidateType_IsAVisibleFailure()
    {
        MatchOptions options = Seeded($"{typeof(MatchSampleA).FullName}.AddOne") with
        {
            RightSelector = "No.Such.Type",
        };

        var (exitCode, output, error) = await RunAsync(options);

        Assert.Equal(1, exitCode);
        Assert.Empty(output);
        Assert.Contains("Candidate type 'No.Such.Type' not found", error);
    }

    [Fact]
    public async Task Similar_MalformedLibraryRange_IsAVisibleFailure()
    {
        MatchOptions options = Seeded($"{typeof(MatchSampleA).FullName}.AddOne") with
        {
            AssemblyPath = "..only-a-right-side.dll",
        };

        var (exitCode, output, error) = await RunAsync(options);

        Assert.Equal(1, exitCode);
        Assert.Empty(output);
        Assert.Contains("Invalid library range", error);
    }

    [Fact]
    public async Task Similar_MissingSeed_FailsWithoutRunning()
    {
        var (exitCode, output, error) = await RunAsync(Seeded(""));

        Assert.Equal(1, exitCode);
        Assert.Empty(output);
        Assert.Contains("match --similar requires a seed method selector", error);
    }

    [Fact]
    public async Task Similar_WithImplementation_RejectsCombination()
    {
        MatchOptions options = Seeded($"{typeof(MatchSampleA).FullName}.AddOne") with
        {
            IncludeImplementation = true,
        };

        var (exitCode, output, error) = await RunAsync(options);

        Assert.Equal(1, exitCode);
        Assert.Empty(output);
        Assert.Contains("--implementation cannot be combined with --similar", error);
    }

    [Fact]
    public async Task Similar_AssemblyWideWithExplicitCandidateType_RejectsCombination()
    {
        MatchOptions options = Seeded($"{typeof(MatchSampleA).FullName}.AddOne") with
        {
            RightSelector = typeof(MatchSampleA).FullName,
            AssemblyWide = true,
        };

        var (exitCode, output, error) = await RunAsync(options);

        Assert.Equal(1, exitCode);
        Assert.Empty(output);
        Assert.Contains("--assembly-wide searches every method", error);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task Similar_NonPositiveBounds_AreRejected(int value)
    {
        MatchOptions seed = Seeded($"{typeof(MatchSampleA).FullName}.AddOne");

        foreach (MatchOptions options in new[]
        {
            seed with { Top = value },
            seed with { MaximumResults = value },
            seed with { MaximumMethods = value },
        })
        {
            var (exitCode, output, error) = await RunAsync(options);
            Assert.Equal(1, exitCode);
            Assert.Empty(output);
            Assert.Contains("must be greater than zero", error);
        }
    }

    /// <summary>
    /// Pairwise <c>match</c> must be unaffected by the discovery surface sharing its options.
    /// </summary>
    [Fact]
    public async Task PairwiseMatch_IsUnchangedWhenSimilarIsNotRequested()
    {
        var options = new MatchOptions
        {
            LeftSelector = $"{typeof(MatchSampleA).FullName}.AddOne",
            RightSelector = $"{typeof(MatchSampleB).FullName}.AddOneToo",
            AssemblyPath = TestAssembly,
            IncludeAll = true,
            JsonOutput = true,
        };

        var (exitCode, output, error) = await RunAsync(options);

        Assert.Equal(0, exitCode);
        Assert.Empty(error);
        Assert.Contains("\"relation\": \"Exact\"", output);
        Assert.DoesNotContain("\"disclosure\":", output);
    }

    // ---- Round 1 review findings ----

    /// <summary>
    /// ".." means two things in one argument: a parent directory and a range separator. Splitting
    /// on the first occurrence rejects "--library ../a.dll", which pairwise match accepts.
    /// </summary>
    [Theory]
    [InlineData("a.dll", -1)]
    [InlineData("../a.dll", -1)]
    [InlineData("..\\a.dll", -1)]
    [InlineData("a/../b.dll", -1)]
    [InlineData("a/..", -1)]
    [InlineData("../../a.dll", -1)]
    [InlineData("old.dll..new.dll", 7)]
    [InlineData("a/../v1/F.dll..b/v2/F.dll", 13)]
    [InlineData("a.dll..b.dll..c.dll", -2)]
    public void RangeSeparator_TreatsParentSegmentsAsPathsAndNotRanges(string value, int expected)
        => Assert.Equal(expected, MatchDiscovery.FindRangeSeparator(value));

    /// <summary>
    /// The end-to-end consequence of the rule above: a parent-relative library path is one library,
    /// not a malformed range.
    /// </summary>
    [Fact]
    public async Task Similar_ParentRelativeLibraryPath_IsASingleLibrary()
    {
        string directory = Path.GetDirectoryName(TestAssembly)!;
        string relative = Path.Combine(
            directory, "..", Path.GetFileName(directory), Path.GetFileName(TestAssembly));

        MatchOptions options = Seeded(SampleSeed) with
        {
            AssemblyPath = relative,
            JsonOutput = true,
        };

        var (exitCode, output, error) = await RunAsync(options);

        Assert.Equal(0, exitCode);
        Assert.Empty(error);
        Assert.Equal("Completed", Parse(output).GetProperty("disposition").GetString());
    }

    /// <summary>
    /// Table, TSV, and JSONL carry no prose, so the Markout description is dropped. The disclosure
    /// is not optional, so it must still reach the reader -- on stderr, which keeps the parsed
    /// stream on stdout intact.
    /// </summary>
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task Similar_TabularRenderings_StillCarryTheDisclosure(bool tsv, bool jsonl)
    {
        MatchOptions options = Seeded(SampleSeed) with
        {
            Tabular = true,
            Tsv = tsv,
            Jsonl = jsonl,
        };

        var (exitCode, output, error) = await RunAsync(options);

        Assert.Equal(0, exitCode);
        Assert.Contains("does not establish", error);
        Assert.DoesNotContain("does not establish", output);
    }

    /// <summary>
    /// The receipt counts unsupported, limit-reached, and failed methods. Without the per-method
    /// outcomes those counts name no method, so the structured output cannot say which method was
    /// skipped or why.
    /// </summary>
    [Fact]
    public async Task Similar_Json_IdentifiesEveryMethodOutcomeBehindTheReceiptCounts()
    {
        MatchOptions options = Seeded(SampleSeed) with
        {
            AssemblyWide = true,
            JsonOutput = true,
        };

        var (exitCode, output, _) = await RunAsync(options);

        Assert.Equal(0, exitCode);
        JsonElement document = Parse(output);
        JsonElement receipt = document.GetProperty("receipt");
        JsonElement outcomes = document.GetProperty("method_outcomes");

        int unsupported = outcomes.EnumerateArray()
            .Count(outcome => outcome.GetProperty("disposition").GetString() == "Unsupported");
        Assert.Equal(receipt.GetProperty("unsupported_methods").GetInt32(), unsupported);

        // Non-vacuity: a run with no skipped method would prove nothing about attribution.
        Assert.True(unsupported > 0, "Expected the whole-assembly population to skip some method.");
        Assert.All(
            outcomes.EnumerateArray().Where(
                outcome => outcome.GetProperty("disposition").GetString() != "Completed"),
            outcome => Assert.NotEmpty(outcome.GetProperty("blockers").EnumerateArray()));
    }

    /// <summary>--top shapes text; it must not shorten the per-method evidence.</summary>
    [Fact]
    public async Task Similar_MethodOutcomes_AreNotBoundedByTop()
    {
        MatchOptions bounded = Seeded(SampleSeed) with { JsonOutput = true, Top = 1 };
        MatchOptions unbounded = Seeded(SampleSeed) with { JsonOutput = true };

        var (_, boundedOutput, _) = await RunAsync(bounded);
        var (_, unboundedOutput, _) = await RunAsync(unbounded);

        int expected = Parse(unboundedOutput).GetProperty("method_outcomes").GetArrayLength();
        Assert.True(expected > 1, "Expected more than one outcome for this to prove anything.");
        Assert.Equal(expected, Parse(boundedOutput).GetProperty("method_outcomes").GetArrayLength());
    }

    // ---- Round 2 review findings ----

    /// <summary>
    /// Runs through the real root command so the option parser is part of the gate. Every earlier
    /// gate calls <c>MatchCommand.ExecuteAsync</c> directly, which cannot see an option the
    /// command definition never registers or never reads.
    /// </summary>
    static Task<(int ExitCode, string Output, string Error)> RunCliAsync(params string[] args)
        => ConsoleCapture.RunAsync(async () =>
        {
            RootCommand root = CommandLineBuilder.CreateRootCommand();
            string[] processed = CommandLineBuilder.PreprocessArgs(args, root);
            return await CommandLineBuilder.InvokeAsync(root.Parse(processed), processed);
        });

    /// <summary>
    /// A MethodDef token is a table row, so the seed's token names a different member in the
    /// candidate image. The seed row must carry the seed's own resolved name, never a lookup in
    /// the candidate name map, or cross-image JSON reports the wrong member as the seed.
    /// </summary>
    [Fact]
    public async Task Similar_CrossImage_NamesTheSeedFromItsOwnImage()
    {
        string coreLibrary = typeof(string).Assembly.Location;
        string facade = Path.Combine(Path.GetDirectoryName(coreLibrary)!, "System.Runtime.dll");

        MatchOptions options = Seeded(SampleSeed) with
        {
            AssemblyPath = $"{TestAssembly}..{facade}",
            RightSelector = "System.String",
            JsonOutput = true,
        };

        var (exitCode, output, error) = await RunAsync(options);

        Assert.Equal(0, exitCode);
        Assert.Empty(error);
        JsonElement document = Parse(output);
        Assert.Equal(SampleSeed, document.GetProperty("seed").GetString());
        Assert.Equal(
            SampleSeed,
            document.GetProperty("seed_outcome").GetProperty("member").GetString());
    }

    /// <summary>
    /// Only <c>Completed</c> ranked the population. <c>Unsupported</c> and <c>LimitReached</c> are
    /// terminal non-completions carrying blockers, so reporting success would turn an analysis
    /// failure into success-shaped empty output.
    /// </summary>
    [Fact]
    public async Task Similar_LimitReached_IsAVisibleFailure()
    {
        MatchOptions options = Seeded(SampleSeed) with
        {
            MaximumMethods = 1,
            JsonOutput = true,
        };

        var (exitCode, output, _) = await RunAsync(options);

        Assert.Equal(1, exitCode);
        JsonElement document = Parse(output);
        Assert.Equal("LimitReached", document.GetProperty("disposition").GetString());
        Assert.NotEmpty(document.GetProperty("blockers").EnumerateArray());
    }

    /// <summary>
    /// The receipt is the retrieval's own evidence, so every field the query issued must survive
    /// the projection. Dropping <c>BodyBytes</c> or <c>Locals</c> while claiming complete
    /// evidence is a silent loss.
    /// </summary>
    [Fact]
    public async Task Similar_MethodOutcomes_ProjectEveryReceiptField()
    {
        MatchOptions options = Seeded(SampleSeed) with { JsonOutput = true };

        var (exitCode, output, _) = await RunAsync(options);

        Assert.Equal(0, exitCode);
        foreach (JsonElement outcome in
            Parse(output).GetProperty("method_outcomes").EnumerateArray())
        {
            foreach (string field in
                (string[])["body_bytes", "instructions", "blocks", "edges", "locals"])
            {
                Assert.True(
                    outcome.TryGetProperty(field, out JsonElement value),
                    $"{field} is missing from a method outcome.");
                Assert.Equal(JsonValueKind.Number, value.ValueKind);
            }
        }
    }

    /// <summary>
    /// A range separator abuts the right operand's own parent segment, so the two spellings run
    /// together into one run of dots. Scanning for the leftmost split that leaves two well-formed
    /// paths accepts that, where skipping bounded occurrences rejected it as ambiguous.
    /// </summary>
    [Theory]
    [InlineData("old/F.dll..../../new/F.dll", 9)]
    [InlineData("../a/F.dll..../b/F.dll", 10)]
    [InlineData("/x/F.dll../../y/F.dll", 8)]
    [InlineData("old/F.dll..new/F.dll", 9)]
    [InlineData("old\\F.dll..new\\F.dll", 9)]
    [InlineData("a.dll..b.dll..c.dll", -2)]
    [InlineData("..a.dll", -2)]
    [InlineData("a.dll..", -2)]
    public void RangeSeparator_SplitsWhereBothSidesRemainWellFormedPaths(
        string value,
        int expected)
        => Assert.Equal(expected, MatchDiscovery.FindRangeSeparator(value));

    /// <summary>
    /// The end-to-end consequence: a range whose right operand is parent-relative is a range, not
    /// an ambiguity.
    /// </summary>
    [Fact]
    public async Task Similar_RangeWithParentRelativeRightPath_IsARange()
    {
        string directory = Path.GetDirectoryName(TestAssembly)!;
        string relative = Path.Combine(
            directory, "..", Path.GetFileName(directory), Path.GetFileName(TestAssembly));

        MatchOptions options = Seeded(SampleSeed) with
        {
            AssemblyPath = $"{TestAssembly}..{relative}",
            JsonOutput = true,
        };

        var (exitCode, output, error) = await RunAsync(options);

        Assert.Equal(0, exitCode);
        Assert.Empty(error);
        Assert.Equal("Completed", Parse(output).GetProperty("disposition").GetString());
    }

    /// <summary>
    /// <c>--table</c>, <c>--tsv</c>, and <c>--jsonl</c> require exactly one table shape
    /// (<c>docs/design/output-shapes.md</c>), and <c>match</c> carries no section-selection
    /// options. Emitting a field/value table followed by a candidate table gives a scripted
    /// consumer two incompatible row schemas on one stream.
    /// </summary>
    [Fact]
    public async Task Similar_Jsonl_EmitsExactlyOneRowSchema()
    {
        MatchOptions options = Seeded(SampleSeed) with { Tabular = true, Jsonl = true };

        var (exitCode, output, error) = await RunAsync(options);

        Assert.Equal(0, exitCode);
        string[] lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.NotEmpty(lines);

        string[] first = Keys(lines[0]);
        Assert.Contains("rank", first);
        foreach (string line in lines)
            Assert.Equal(first, Keys(line));

        // The context the single table cannot carry still reaches the reader.
        Assert.Contains("Disposition: Completed", error);
        Assert.Contains("Seed:", error);

        static string[] Keys(string line) =>
            [.. JsonDocument.Parse(line).RootElement.EnumerateObject().Select(p => p.Name)];
    }

    /// <summary>
    /// An overloaded member has no unambiguous <c>Type.Member</c> spelling, so the promise that
    /// every ranked row is addressable by pairwise <c>match</c> holds only if the printed token is
    /// itself a selector.
    /// </summary>
    [Fact]
    public async Task RankedRowToken_IsAcceptedByPairwiseMatch()
    {
        MatchOptions discovery = Seeded($"{typeof(MatchSampleA).FullName}.AddOne") with
        {
            JsonOutput = true,
        };

        var (discoveryExit, discoveryOutput, _) = await RunAsync(discovery);
        Assert.Equal(0, discoveryExit);

        JsonElement top = Parse(discoveryOutput).GetProperty("candidates").EnumerateArray().First();
        string token = top.GetProperty("token").GetString()!;
        Assert.Equal(
            $"{typeof(MatchSampleA).FullName}.Overloaded",
            top.GetProperty("member").GetString());

        var pairwise = new MatchOptions
        {
            LeftSelector = $"{typeof(MatchSampleA).FullName}.AddOne",
            RightSelector = token,
            AssemblyPath = TestAssembly,
            IncludeAll = true,
            JsonOutput = true,
        };

        var (exitCode, output, error) = await RunAsync(pairwise);

        Assert.Equal(0, exitCode);
        Assert.Empty(error);
        Assert.Contains("\"relation\":", output);
    }

    /// <summary>
    /// The discovery options share the pairwise options object. Accepting one without
    /// <c>--similar</c> silently ignores a scope or limit the caller asked for.
    /// </summary>
    [Theory]
    [InlineData("--assembly-wide")]
    [InlineData("--top", "1")]
    [InlineData("--max-results", "1")]
    [InlineData("--max-methods", "1")]
    public async Task Pairwise_RejectsDiscoveryOnlyOptions(params string[] option)
    {
        string[] args =
        [
            "match",
            $"{typeof(MatchSampleA).FullName}.AddOne",
            $"{typeof(MatchSampleB).FullName}.AddOneToo",
            "--library", TestAssembly,
            "--all",
            .. option,
        ];

        var (exitCode, output, error) = await RunCliAsync(args);

        Assert.Equal(1, exitCode);
        Assert.Empty(output);
        Assert.Contains("applies to discovery; add --similar", error);
    }

    /// <summary>
    /// Runs the whole surface through the real parser, so an option the command definition fails
    /// to register or read is a failure here rather than an untested gap.
    /// </summary>
    [Fact]
    public async Task Similar_RunsThroughTheRealCommandLine()
    {
        var (exitCode, output, error) = await RunCliAsync(
            "match", SampleSeed, "--similar", "--library", TestAssembly, "--all", "--top", "1",
            "--json");

        Assert.Equal(0, exitCode);
        Assert.Empty(error);
        JsonElement document = Parse(output);
        Assert.Equal("Completed", document.GetProperty("disposition").GetString());
        Assert.Equal(SampleSeed, document.GetProperty("seed").GetString());
        Assert.Equal(1, document.GetProperty("limits").GetProperty("text_rows").GetInt32());
    }

    /// <summary>
    /// <c>System.Runtime</c> is a pure facade: it forwards <c>System.String</c> to
    /// <c>System.Private.CoreLib</c> and defines no bodies at all. Scoping discovery to a
    /// forwarded type must read the image that defines it, not the facade that only points at it.
    /// Because a MethodDef token is a table row that means nothing across images, opening the
    /// wrong side does not merely fail to find candidates -- it can name the wrong members -- so
    /// this gate pins the reported names, not just a non-empty result.
    /// </summary>
    [Fact]
    public async Task Similar_TypeScopeFollowsAForwarderToTheDefiningImage()
    {
        string coreLibrary = typeof(string).Assembly.Location;
        string facade = Path.Combine(
            Path.GetDirectoryName(coreLibrary)!,
            "System.Runtime.dll");
        Assert.True(File.Exists(facade), facade);

        MatchOptions options = Seeded(SampleSeed) with
        {
            AssemblyPath = $"{TestAssembly}..{facade}",
            JsonOutput = true,
            RightSelector = "System.String",
        };

        var (exitCode, output, error) = await RunAsync(options);

        Assert.Equal(0, exitCode);
        Assert.Empty(error);
        JsonElement document = Parse(output);
        Assert.Equal("Completed", document.GetProperty("disposition").GetString());

        // The facade defines no bodies, so a facade-scoped run cannot rank anything.
        string[] members = Candidates(document).Select(candidate => candidate.Member).ToArray();
        Assert.NotEmpty(members);
        Assert.All(members, member => Assert.StartsWith("System.String.", member));
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
