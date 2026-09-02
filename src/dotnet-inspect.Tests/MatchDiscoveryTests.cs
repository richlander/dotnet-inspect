using System.CommandLine;
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
using DotnetInspector.CommandLine;
using DotnetInspector.Commands;
using DotnetInspector.Fixtures;
using DotnetInspector.Options;
using DotnetInspector.Packages;
using DotnetInspector.Views;
using ILInspector.Metadata;
using ILInspector.MetadataPrimitives;

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
        Assert.Contains($"1 of {all} returned candidates", markdown);
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
    /// Table, TSV, and JSONL persist rows without prose, so retrieval-budget and truncation
    /// provenance must travel with them on stderr. A persisted table that omits the limits and
    /// the truncation note reads as the complete ranking under default budgets rather than the
    /// first --top rows of a bounded search.
    /// </summary>
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task Similar_TabularRenderings_CarryLimitAndTruncationProvenance(bool tsv, bool jsonl)
    {
        MatchOptions options = Seeded(SampleSeed) with
        {
            AssemblyWide = true,
            Tabular = true,
            Tsv = tsv,
            Jsonl = jsonl,
            Top = 1,
        };

        var (exitCode, _, error) = await RunAsync(options);

        Assert.Equal(0, exitCode);
        Assert.Contains("Limits:", error);
        Assert.Contains("max-methods", error);
        Assert.Contains("Showing:", error);
    }

    /// <summary>
    /// --max-results bounds the returned candidate array and --top bounds the rendered rows, so
    /// the "showing" note counts returned candidates. Calling that denominator "ranked" restates
    /// the receipt's own ranked count as a smaller number, which is the one reading the receipt
    /// exists to prevent.
    /// </summary>
    [Fact]
    public async Task Similar_ShowingNote_CountsReturnedCandidatesNotRankedOnes()
    {
        MatchOptions options = Seeded(SampleSeed) with
        {
            AssemblyWide = true,
            MaximumResults = 3,
            Top = 1,
        };

        var (exitCode, output, _) = await RunAsync(options);

        Assert.Equal(0, exitCode);

        // The receipt ranks more than it returns, so "ranked" and "returned" are different
        // numbers here and naming the wrong one is observable.
        Assert.Contains("3 returned", output);
        Assert.Contains("1 of 3 returned candidates", output);
        Assert.DoesNotContain("ranked candidates", output);
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

        MatchOptions options = Seeded("System.String.IsNullOrEmpty") with
        {
            AssemblyPath = facade,
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

    /// <summary>
    /// A run whose ranked rows come from a forwarded-to image must name that image. The printed
    /// token indexes a MethodDef row that exists only there, so a disclosure that names nothing --
    /// or names the facade the caller typed -- hands back an address the caller cannot resolve.
    /// This is the surviving cross-image shape now that a library argument names one image.
    /// </summary>
    [Fact]
    public async Task Similar_ForwardedPopulation_NamesTheImageThatDefinesTheRankedTokens()
    {
        string coreLibrary = typeof(string).Assembly.Location;
        string facade = Path.Combine(Path.GetDirectoryName(coreLibrary)!, "System.Runtime.dll");
        Assert.True(File.Exists(facade), facade);

        MatchOptions options = Seeded("System.String.IsNullOrEmpty") with
        {
            AssemblyPath = facade,
            RightSelector = "System.String",
            JsonOutput = true,
        };

        var (exitCode, output, error) = await RunAsync(options);

        Assert.Equal(0, exitCode);
        Assert.Empty(error);
        JsonElement document = Parse(output);

        string candidateAssembly = document.GetProperty("candidate_assembly").GetString()!;
        Assert.EndsWith("System.Private.CoreLib.dll", candidateAssembly);
        Assert.NotEqual(facade, candidateAssembly);

        // The disclosure has to hand back the exact library that resolves the printed tokens.
        string disclosure = document.GetProperty("disclosure").GetString()!;
        Assert.Contains(candidateAssembly, disclosure);
    }

    // ---- Round 3 review findings ----

    /// <summary>
    /// Spells the library the way the README does — relative to the working directory — and
    /// requires it to name the same image as the seed's absolute origin. A raw path comparison
    /// reported one file as two images, which stopped retrieval from suppressing the seed and
    /// ranked the seed as its own best candidate.
    /// </summary>
    [Fact]
    public async Task Similar_RelativeLibraryPath_StillSuppressesTheSeed()
    {
        string relative = Path.GetRelativePath(Directory.GetCurrentDirectory(), TestAssembly);
        Assert.False(Path.IsPathRooted(relative));

        MatchOptions options = Seeded(SampleSeed) with
        {
            AssemblyPath = relative,
            AssemblyWide = true,
            JsonOutput = true,
        };

        var (exitCode, output, error) = await RunAsync(options);

        Assert.Equal(0, exitCode);
        Assert.Empty(error);
        JsonElement document = Parse(output);
        Assert.False(document.TryGetProperty("candidate_assembly", out _));

        int seedToken = document.GetProperty("seed_outcome").GetProperty("token").GetString()
            is string token
            ? Convert.ToInt32(token, 16)
            : throw new InvalidOperationException("seed token missing");

        Assert.DoesNotContain(
            document.GetProperty("candidates").EnumerateArray(),
            candidate => Convert.ToInt32(candidate.GetProperty("token").GetString()!, 16) == seedToken);
    }

    /// <summary>
    /// Every ranked row prints a token so the row is addressable by pairwise <c>match</c>. A token
    /// that the projection cannot name falls back to the caller's own library spelling, so a
    /// relative spelling made the seed and the candidate look like different assemblies and the
    /// documented transition failed for exactly the rows that need it.
    /// </summary>
    [Fact]
    public async Task Pairwise_RelativeLibraryPath_AcceptsARankedToken()
    {
        string relative = Path.GetRelativePath(Directory.GetCurrentDirectory(), TestAssembly);

        MatchOptions discovery = Seeded(SampleSeed) with
        {
            AssemblyPath = relative,
            AssemblyWide = true,
            MaximumResults = 500,
            JsonOutput = true,
        };

        var (discoveryExit, discoveryOutput, _) = await RunAsync(discovery);
        Assert.Equal(0, discoveryExit);

        // Deliberately a row the API projection cannot name. A named row carries the surface's own
        // absolute path on both sides and would pass even with the origin left uncanonicalized;
        // only a token absent from the projection falls back to the caller's relative spelling,
        // and those are exactly the rows whose sole address is the printed token.
        string rankedToken = Parse(discoveryOutput)
            .GetProperty("candidates").EnumerateArray()
            .First(candidate => candidate.GetProperty("member").GetString()!.StartsWith(
                "MethodDef ", StringComparison.Ordinal))
            .GetProperty("token").GetString()!;

        var (exitCode, output, error) = await RunAsync(new MatchOptions
        {
            LeftSelector = SampleSeed,
            RightSelector = rankedToken,
            AssemblyPath = relative,
            IncludeAll = true,
        });

        Assert.Equal(0, exitCode);
        Assert.Empty(error);
        Assert.Contains("Relation", output);
    }

    /// <summary>
    /// JSON escaping is not containment: a parser restores the original control character, so a
    /// bidi override in inspected metadata would reach a JSON consumer intact. The document records
    /// contain their own metadata-derived strings, because <c>MarkoutRowContainmentTests</c> covers
    /// Markout views and a JSON document is not one.
    /// </summary>
    [Fact]
    public async Task Similar_Json_ContainsEveryMetadataDerivedString()
    {
        MatchOptions options = Seeded(SampleSeed) with { JsonOutput = true };

        var (exitCode, output, _) = await RunAsync(options);
        Assert.Equal(0, exitCode);

        JsonElement document = Parse(output);
        string[] contained =
        [
            document.GetProperty("seed").GetString()!,
            document.GetProperty("scope").GetString()!,
            document.GetProperty("seed_outcome").GetProperty("member").GetString()!,
            .. document.GetProperty("candidates").EnumerateArray()
                .Select(candidate => candidate.GetProperty("member").GetString()!),
            .. document.GetProperty("method_outcomes").EnumerateArray()
                .Select(outcome => outcome.GetProperty("member").GetString()!),
        ];

        Assert.NotEmpty(contained);
        foreach (string value in contained)
            Assert.Equal(CSharpIdentifier.ContainRenderedText(value), value);
    }

    /// <summary>
    /// The containment above has to survive a hostile name rather than only a well-behaved one, so
    /// this drives the same records directly with a rendering hazard the fixtures cannot carry.
    /// </summary>
    [Fact]
    public void MatchDiscoveryDocuments_ContainRenderingHazards()
    {
        const string Hostile = "Evil\u202EName";

        var document = new MatchDiscoveryDocument
        {
            Seed = Hostile,
            Scope = Hostile,
            CandidateAssembly = Hostile,
            Disposition = "Completed",
            Disclosure = Hostile,
            Limits = new MatchDiscoveryLimitsDocument(1, 1, null),
        };

        Assert.DoesNotContain('\u202E', document.Seed);
        Assert.DoesNotContain('\u202E', document.Scope);
        Assert.DoesNotContain('\u202E', document.CandidateAssembly!);

        // The disclosure embeds the candidate assembly path, so leaving it raw would reinstate
        // through the prose exactly what containing the field above removes.
        Assert.DoesNotContain('\u202E', document.Disclosure);

        var seed = new MatchDiscoverySeedDocument
        {
            Member = Hostile,
            Token = Hostile,
            Disposition = "Completed",
        };

        Assert.DoesNotContain('\u202E', seed.Member);
        Assert.DoesNotContain('\u202E', seed.Token);

        var blocker = new MatchDiscoveryBlockerDocument { Kind = Hostile, Detail = Hostile };

        Assert.DoesNotContain('\u202E', blocker.Kind);
        Assert.DoesNotContain('\u202E', blocker.Detail);
    }

    // ---- Round 4 review findings ----

    /// <summary>
    /// The same-image disclosure still names the transition, because within one image the printed
    /// token really is addressable. A cross-image wording that leaked here would retract a promise
    /// the command does keep.
    /// </summary>
    [Fact]
    public async Task Similar_SameImage_StillNamesThePairwiseTransition()
    {
        MatchOptions options = Seeded(SampleSeed) with { JsonOutput = true };

        var (exitCode, output, _) = await RunAsync(options);

        Assert.Equal(0, exitCode);
        string disclosure = Parse(output).GetProperty("disclosure").GetString()!;

        Assert.Contains("Run pairwise `match` on a candidate", disclosure);
        Assert.DoesNotContain("across images", disclosure);
    }

    /// <summary>
    /// Image identity separates two files that share a name and differ only by directory, and
    /// unifies the spellings of one file that canonicalization reconciles — a relative path and
    /// its absolute form. A case-only variant is two images by the rule above.
    /// </summary>
    [Fact]
    public void SameImage_DistinguishesDifferentFilesAndUnifiesSpellingsOfOne()
    {
        string v1 = FixtureCatalog.DiffV1.AssemblyPath();
        string v2 = FixtureCatalog.DiffV2.AssemblyPath();

        Assert.False(MatchCommand.SameImage(v1, v2));
        Assert.True(MatchCommand.SameImage(v1, v1));
        Assert.True(MatchCommand.SameImage(
            v1,
            Path.GetRelativePath(Directory.GetCurrentDirectory(), v1)));

        string lowered = LoweredPath(v1);
        if (!string.Equals(lowered, v1, StringComparison.Ordinal))
            Assert.False(MatchCommand.SameImage(v1, lowered));
    }

    static string LoweredPath(string path)
        => Path.Combine(
            Path.GetDirectoryName(path)!,
            Path.GetFileName(path).ToLowerInvariant());

    /// <summary>
    /// The failure document's detail is the query layer's own spelling of a missing or ambiguous
    /// target and can carry a metadata exception's message, so it is metadata-derived exactly like
    /// every other document string here. It was the one record left uncontained.
    /// </summary>
    [Fact]
    public void MatchDiscoveryFailureDocument_ContainsRenderingHazards()
    {
        const string Hostile = "Evil\u202EName";

        var failure = new MatchDiscoveryFailureDocument
        {
            Kind = Hostile,
            Role = Hostile,
            Detail = Hostile,
        };

        Assert.DoesNotContain('\u202E', failure.Kind);
        Assert.DoesNotContain('\u202E', failure.Role);
        Assert.DoesNotContain('\u202E', failure.Detail);
    }

    /// <summary>
    /// Containment is a property of every document record, not of the three a gate happened to
    /// name. Driving all six with the same hazard is what keeps a later record from being added
    /// without it.
    /// </summary>
    [Fact]
    public void MatchDiscoveryCandidateAndOutcomeDocuments_ContainRenderingHazards()
    {
        const string Hostile = "Evil\u202EName";

        var candidate = new MatchDiscoveryCandidateDocument
        {
            Rank = 1,
            Member = Hostile,
            Token = Hostile,
            Similarity = new MatchDiscoverySimilarityDocument(
                0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0),
        };

        Assert.DoesNotContain('\u202E', candidate.Member);
        Assert.DoesNotContain('\u202E', candidate.Token);

        var outcome = new MatchDiscoveryMethodOutcomeDocument
        {
            Member = Hostile,
            Token = Hostile,
            Disposition = "Unsupported",
        };

        Assert.DoesNotContain('\u202E', outcome.Member);
        Assert.DoesNotContain('\u202E', outcome.Token);
    }

    // ---- Round 5 review findings ----

    /// <summary>
    /// A limit rejects the candidate population atomically, so nothing is processed even though
    /// the input was large. The receipt line must not report the input count as work performed.
    /// </summary>
    [Fact]
    public async Task Similar_LimitReachedReceipt_DoesNotClaimUnprocessedMethodsWereProcessed()
    {
        MatchOptions options = Seeded(SampleSeed) with
        {
            AssemblyWide = true,
            MaximumMethods = 1,
        };

        var (exitCode, markdown, _) = await RunAsync(options);

        Assert.Equal(1, exitCode);
        Assert.Contains("LimitReached", markdown);
        Assert.Contains("0 processed", markdown);
        Assert.DoesNotContain("scanned", markdown);
    }

    /// <summary>
    /// On the ordinary path the seed itself is suppressed, so processed is smaller than input.
    /// The receipt keeps both numbers rather than presenting one of them as the other.
    /// </summary>
    [Fact]
    public async Task Similar_Receipt_ReportsProcessedAndInputSeparately()
    {
        MatchOptions options = Seeded(SampleSeed) with { AssemblyWide = true };

        var (exitCode, markdown, _) = await RunAsync(options);

        Assert.Equal(0, exitCode);
        Assert.Matches(@"\d+ eligible of \d+ processed \(\d+ input\)", markdown);
    }

    // ---- Round 8 review findings: image-local names and single-image discovery ----

    /// <summary>
    /// An <see cref="ApiSurface"/> describes the types an image forwards as well as the types it
    /// defines, and a forwarded type's members carry tokens from the image that defines them. Those
    /// tokens collide with the caller image's own dense row indices, so admitting forwarded types
    /// into the projection let one shadow a local row and label it with a name from another
    /// assembly -- discovery printed a name that pairwise <c>match</c> contradicted for the very
    /// token discovery had just printed. Only rows the image owns may name anything.
    /// </summary>
    [Fact]
    public void Names_DoNotLabelALocalRowWithAForwardedTypesName()
    {
        const string image = "/images/Local.dll";

        var local = new ApiType
        {
            Namespace = "Z",
            Name = "LocalType",
            Members = { new ApiMember { Name = "LocalMethod", MetadataToken = 0x06000002 } },
        };

        // Ordered first so it wins TryAdd if the projection fails to exclude it.
        var forwarded = new ApiType
        {
            Namespace = "A",
            Name = "ForwardedType",
            SourceAssemblyPath = "/images/Other.dll",
            Members = { new ApiMember { Name = "Foreign", MetadataToken = 0x06000002 } },
        };

        var surface = new ApiSurface();
        surface.Types.Add(forwarded);
        surface.Types.Add(local);

        MatchDiscoveryNames names = MatchDiscoveryNames.Build(surface, image);

        var address = new MetadataMethodAddress(
            Guid.Empty,
            MetadataTokens.MethodDefinitionHandle(2));

        Assert.Equal("Z.LocalType.LocalMethod", names.Display(address));
    }

    /// <summary>
    /// Discovery ranks rows of one image. When forwarding resolves the seed and the candidate type
    /// to different assemblies the run is unrepairable downstream: the ranked tokens address the
    /// candidate image, the seed is absent from it, and the pairwise confirmation the disclosure
    /// points at cannot be run. It must be refused at the gate, naming both images.
    /// </summary>
    [Fact]
    public async Task Similar_RefusesACandidateTypeDefinedInAnotherImage()
    {
        string coreLibrary = typeof(string).Assembly.Location;
        string facade = Path.Combine(Path.GetDirectoryName(coreLibrary)!, "System.dll");
        Assert.True(File.Exists(facade), facade);

        MatchOptions options = Seeded("System.Net.Sockets.NetworkStream.Flush") with
        {
            AssemblyPath = facade,
            RightSelector = "System.Collections.Generic.SortedDictionary`2",
        };

        var (exitCode, _, error) = await RunAsync(options);

        Assert.Equal(1, exitCode);
        Assert.Contains("System.Net.Sockets.dll", error);
        Assert.Contains("System.Collections.dll", error);
        Assert.Contains("single image", error);
    }

    /// <summary>
    /// A candidate is a type scope. Accepting a Type.Member selector and silently widening to the
    /// declaring type turned a typo into a completed run over a scope the caller never named.
    /// </summary>
    [Fact]
    public async Task Similar_RefusesAMemberShapedCandidateInsteadOfWideningToItsType()
    {
        MatchOptions options = Seeded($"{typeof(MatchSampleA).FullName}.AddOne") with
        {
            RightSelector = $"{typeof(MatchSampleA).FullName}.NoSuchMember",
        };

        var (exitCode, _, error) = await RunAsync(options);

        Assert.Equal(1, exitCode);
        Assert.Contains("NoSuchMember", error);
        Assert.Contains("type scope", error);
    }

    // ---- Round 9 review findings: the disclosed address must outlive the command ----

    /// <summary>
    /// A package is extracted to a temporary directory that <c>match</c> deletes as it exits, so
    /// disclosing that extraction path handed the caller an address that no longer existed by the
    /// time they could type it: discovery completed at exit 0 and the command it printed failed
    /// with "File not found". A package-sourced run must disclose the package and the library
    /// inside it, which is what actually replays.
    /// </summary>
    [Fact]
    public void Disclosure_ForAPackageSourcedRun_NamesThePackageRatherThanTheExtractionPath()
    {
        var request = new MatchDiscoveryRequest(
            "A.Type.Member",
            "A.Type",
            "lib/net10.0/Target.dll",
            new ILInspector.Analysis.StructuralCloneRetrievalLimits(1, 1),
            null,
            CandidatePackage: "Fixture@1.0.0",
            CandidateTfm: "net10.0");

        string disclosure = MatchDiscoveryFormatter.DisclosureFor(request);

        Assert.Contains(
            "`--package 'Fixture@1.0.0' --library 'lib/net10.0/Target.dll' --tfm 'net10.0'`",
            disclosure);
    }

    [Fact]
    public async Task Similar_PackageForwardedPopulation_DisclosesTheExactReplayAddress()
    {
        string fixtureDirectory = Path.Combine(
            Path.GetTempPath(),
            $"match-replay-{Guid.NewGuid():N}");
        string package = Path.Combine(fixtureDirectory, "Forwarding.Fixture.1.0.0.nupkg");
        Directory.CreateDirectory(fixtureDirectory);

        try
        {
            using (ZipArchive archive = ZipFile.Open(package, ZipArchiveMode.Create))
            {
                ZipArchiveEntry facade = archive.CreateEntry("lib/net10.0/Facade.dll");
                await using (Stream stream = facade.Open())
                {
                    await stream.WriteAsync(BuildForwarderFacade(
                        "Facade",
                        TestAssembly,
                        typeof(MatchDiscoverySample)),
                        TestContext.Current.CancellationToken);
                }
                archive.CreateEntryFromFile(
                    TestAssembly,
                    $"lib/net10.0/{Path.GetFileName(TestAssembly)}");
            }

            var options = new MatchOptions
            {
                LeftSelector = SampleSeed,
                RightSelector = typeof(MatchDiscoverySample).FullName,
                PackagePath = package,
                AssemblyPath = "lib/net10.0/Facade.dll",
                IncludeAll = true,
                Similar = true,
                JsonOutput = true,
            };

            var (exitCode, output, error) = await RunAsync(options);

            Assert.Equal(0, exitCode);
            Assert.Empty(error);
            JsonElement document = Parse(output);
            Assert.Equal(
                $"lib/net10.0/{Path.GetFileName(TestAssembly)}",
                document.GetProperty("candidate_assembly").GetString());
            Assert.Contains(
                $"--package '{package}' "
                    + $"--library 'lib/net10.0/{Path.GetFileName(TestAssembly)}' "
                    + "--tfm 'net10.0'",
                document.GetProperty("disclosure").GetString());
        }
        finally
        {
            Directory.Delete(fixtureDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task Similar_PackageForwarderUsesOnlyAnAuthorizedDependencyPayload()
    {
        string fixtureDirectory = Path.Combine(
            Path.GetTempPath(),
            $"match-forwarded-dependency-{Guid.NewGuid():N}");
        string appCache = Path.Combine(fixtureDirectory, "app-cache");
        string globalRoot = Path.Combine(fixtureDirectory, "global");
        string rootPackage = Path.Combine(
            fixtureDirectory,
            "Forwarding.Root.1.0.0.nupkg");
        string dependencyId = $"Forwarding.Target.{Guid.NewGuid():N}";
        const string dependencyVersion = "1.0.0";
        const string authorizedSource =
            "https://authorized.invalid/v3/index.json";
        const string unauthorizedSource =
            "https://unauthorized.invalid/v3/index.json";
        const string targetNamespace = "Forwarding.Target";
        const string targetTypeName = "Sample";
        string targetSelector = $"{targetNamespace}.{targetTypeName}.Seed";
        string targetType = $"{targetNamespace}.{targetTypeName}";
        string dependencyAsset =
            $"lib/net10.0/{dependencyId}.dll";
        byte[] targetImage = BuildMatchTargetAssembly(
            dependencyId,
            targetNamespace,
            targetTypeName);
        string? previousNuGetPackages =
            Environment.GetEnvironmentVariable("NUGET_PACKAGES");
        Directory.CreateDirectory(fixtureDirectory);

        try
        {
            using (ZipArchive archive = ZipFile.Open(
                       rootPackage,
                       ZipArchiveMode.Create))
            {
                ZipArchiveEntry facade =
                    archive.CreateEntry("lib/net10.0/Facade.dll");
                await using (Stream stream = facade.Open())
                {
                    await stream.WriteAsync(
                        BuildForwarderFacade(
                            "Facade",
                            new AssemblyReferenceIdentity(
                                dependencyId,
                                new Version(1, 0, 0, 0),
                                Culture: null,
                                PublicKeyToken: null),
                            targetNamespace,
                            targetTypeName),
                        TestContext.Current.CancellationToken);
                }

                ZipArchiveEntry nuspec =
                    archive.CreateEntry("Forwarding.Root.nuspec");
                await using Stream nuspecStream = nuspec.Open();
                await using var writer = new StreamWriter(nuspecStream);
                await writer.WriteAsync(
                    $"""
                    <?xml version="1.0"?>
                    <package>
                      <metadata>
                        <id>Forwarding.Root</id>
                        <version>1.0.0</version>
                        <authors>dotnet-inspect tests</authors>
                        <description>forwarded dependency fixture</description>
                        <dependencies>
                          <group targetFramework="net10.0">
                            <dependency id="{dependencyId}" version="{dependencyVersion}" />
                          </group>
                        </dependencies>
                      </metadata>
                    </package>
                    """);
            }

            string dependencyDirectory = Path.Combine(
                globalRoot,
                dependencyId.ToLowerInvariant(),
                dependencyVersion);
            string dependencyAssetPath = Path.Combine(
                dependencyDirectory,
                dependencyAsset.Replace(
                    '/',
                    Path.DirectorySeparatorChar));
            Directory.CreateDirectory(
                Path.GetDirectoryName(dependencyAssetPath)!);
            File.WriteAllBytes(dependencyAssetPath, targetImage);
            File.WriteAllText(
                Path.Combine(
                    dependencyDirectory,
                    $"{dependencyId}.nuspec"),
                $"""
                <?xml version="1.0"?>
                <package>
                  <metadata>
                    <id>{dependencyId}</id>
                    <version>{dependencyVersion}</version>
                    <authors>dotnet-inspect tests</authors>
                    <description>forwarded target fixture</description>
                  </metadata>
                </package>
                """);
            string metadataPath = Path.Combine(
                dependencyDirectory,
                ".nupkg.metadata");
            File.WriteAllText(
                metadataPath,
                $$"""{"source":"{{unauthorizedSource}}"}""");

            Environment.SetEnvironmentVariable(
                "NUGET_PACKAGES",
                globalRoot);
            NuGetCache.Initialize(
                "dotnet-inspect-match-forwarded-dependency",
                appCache,
                skipNuGetCache: false);
            var options = new MatchOptions
            {
                LeftSelector = targetSelector,
                RightSelector = targetType,
                PackagePath = rootPackage,
                AssemblyPath = "lib/net10.0/Facade.dll",
                IncludeAll = true,
                Similar = true,
                JsonOutput = true,
                SourceOptions = new NuGetSourceOptions
                {
                    Sources = [authorizedSource],
                },
            };

            var (unauthorizedExit, unauthorizedOutput, unauthorizedError) =
                await RunAsync(options);

            Assert.Null(
                PackageExtractor.TryGetAdmittedCachedPackagePath(
                    dependencyId,
                    dependencyVersion,
                    options.SourceOptions,
                    [globalRoot]));
            Assert.True(
                unauthorizedExit == 1,
                $"Expected unavailable forwarding.\nOutput:\n{unauthorizedOutput}\nError:\n{unauthorizedError}");
            Assert.Empty(unauthorizedOutput);
            Assert.Contains(
                $"Forwarded type '{targetType}' "
                    + "could not be resolved: UnboundBinding.",
                unauthorizedError);

            File.WriteAllText(
                metadataPath,
                $$"""{"source":"{{authorizedSource}}"}""");
            var (authorizedExit, authorizedOutput, authorizedError) =
                await RunAsync(options);

            Assert.Equal(0, authorizedExit);
            Assert.Empty(authorizedError);
            JsonElement document = Parse(authorizedOutput);
            Assert.Equal(
                dependencyAsset,
                document.GetProperty("candidate_assembly").GetString());
            string disclosure =
                document.GetProperty("disclosure").GetString()!;
            string expectedDisclosure =
                $"--package '{dependencyId.ToLowerInvariant()}@{dependencyVersion}' "
                    + $"--library '{dependencyAsset}' --tfm 'net10.0' "
                    + $"--source '{authorizedSource}'";
            Assert.True(
                disclosure.Contains(
                    expectedDisclosure,
                    StringComparison.Ordinal),
                $"Expected disclosure fragment:\n{expectedDisclosure}\nActual:\n{disclosure}");
        }
        finally
        {
            Environment.SetEnvironmentVariable(
                "NUGET_PACKAGES",
                previousNuGetPackages);
            NuGetCache.Initialize("dotnet-inspect");
            Directory.Delete(fixtureDirectory, recursive: true);
        }
    }

    [Fact]
    public void ReplayAddress_AppCachePathRequiresTypedPackageProvenance()
    {
        string cacheRoot = Path.Combine(
            Path.GetTempPath(),
            $"match-app-cache-address-{Guid.NewGuid():N}");
        string packageId = $"app.cache.dependency.{Guid.NewGuid():N}";
        NuGetCache.Initialize(
            "dotnet-inspect-match-app-cache-address",
            cacheRoot,
            skipNuGetCache: true);
        try
        {
            string candidate = Path.Combine(
                NuGetCache.GetPackageContentCachePath(),
                packageId,
                "1.0.0",
                "producer",
                "lib",
                "net10.0",
                "Target.dll");

            var directAddress =
                MatchDiscovery.GetReplayableCandidateAddress(
                    packagePath: null,
                    packageExtractPath: null,
                    candidate);
            var packageAddress =
                MatchDiscovery.GetReplayableCandidateAddress(
                    packagePath: null,
                    packageExtractPath: null,
                    candidate,
                    AssemblyResolutionProvenance.Package(
                        packageId,
                        "1.0.0",
                        tfm: null,
                        rid: null));

            Assert.Null(directAddress.Package);
            Assert.Equal(candidate, directAddress.Library);
            Assert.Equal($"{packageId}@1.0.0", packageAddress.Package);
            Assert.Equal("lib/net10.0/Target.dll", packageAddress.Library);
            Assert.Equal("net10.0", packageAddress.Tfm);
        }
        finally
        {
            NuGetCache.Initialize("dotnet-inspect");
            if (Directory.Exists(cacheRoot))
                Directory.Delete(cacheRoot, recursive: true);
        }
    }

    [Fact]
    public async Task Similar_PackageSameImage_DisclosesTheExactReplayAddress()
    {
        string fixtureDirectory = Path.Combine(
            Path.GetTempPath(),
            $"match-same-image-replay-{Guid.NewGuid():N}");
        string package = Path.Combine(fixtureDirectory, "Same.Image.Fixture.1.0.0.nupkg");
        string asset = $"lib/net10.0/{Path.GetFileName(TestAssembly)}";
        Directory.CreateDirectory(fixtureDirectory);

        try
        {
            using (ZipArchive archive = ZipFile.Open(package, ZipArchiveMode.Create))
                archive.CreateEntryFromFile(TestAssembly, asset);

            var options = new MatchOptions
            {
                LeftSelector = SampleSeed,
                PackagePath = package,
                AssemblyPath = Path.GetFileName(TestAssembly),
                IncludeAll = true,
                Similar = true,
                JsonOutput = true,
            };

            var (exitCode, output, error) = await RunAsync(options);

            Assert.Equal(0, exitCode);
            Assert.Empty(error);
            JsonElement document = Parse(output);
            Assert.False(document.TryGetProperty("candidate_assembly", out _));
            Assert.NotEmpty(document.GetProperty("candidates").EnumerateArray());
            Assert.Contains(
                $"--package '{package}' --library '{asset}' --tfm 'net10.0'",
                document.GetProperty("disclosure").GetString());
            Assert.Contains(
                "against that same image",
                document.GetProperty("disclosure").GetString());
        }
        finally
        {
            Directory.Delete(fixtureDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task Similar_ExactPackageReplayRetainsExplicitSourceAuthorityOffline()
    {
        string cacheDirectory = Path.Combine(
            Path.GetTempPath(),
            $"match-source-replay-{Guid.NewGuid():N}");
        string packageName = $"Match.Source.Replay.{Guid.NewGuid():N}";
        const string version = "1.0.0";
        const string source = "https://private.invalid/v3/index.json";
        string asset = $"lib/net10.0/{Path.GetFileName(TestAssembly)}";
        string nupkg = Path.Combine(cacheDirectory, $"{packageName}.{version}.nupkg");
        string staged = Path.Combine(cacheDirectory, "staged");
        bool wasOffline = Core.HttpClientFactory.IsOffline;

        Directory.CreateDirectory(cacheDirectory);
        try
        {
            using (ZipArchive archive = ZipFile.Open(nupkg, ZipArchiveMode.Create))
            {
                archive.CreateEntryFromFile(TestAssembly, asset);
                ZipArchiveEntry nuspec = archive.CreateEntry($"{packageName}.nuspec");
                await using Stream stream = nuspec.Open();
                await using var writer = new StreamWriter(stream);
                await writer.WriteAsync(
                    $"""
                    <?xml version="1.0"?>
                    <package>
                      <metadata>
                        <id>{packageName}</id>
                        <version>{version}</version>
                        <authors>dotnet-inspect tests</authors>
                        <description>match replay fixture</description>
                      </metadata>
                    </package>
                    """);
            }

            ZipFile.ExtractToDirectory(nupkg, staged);
            Core.HttpClientFactory.Initialize(
                new Core.HttpClientFactoryOptions { Offline = true });
            Core.HttpClientFactory.ResetSharedForTesting();
            NuGetCache.Initialize(
                "dotnet-inspect-match-source-replay",
                cacheDirectory,
                skipNuGetCache: true);
            NuGetCache.CommitPackage(
                staged,
                nupkg,
                packageName,
                version,
                NuGetCache.GetSourceKey(source));

            var sourceOptions = new NuGetSourceOptions { Sources = [source] };
            var discovery = new MatchOptions
            {
                LeftSelector = SampleSeed,
                PackagePath = $"{packageName}@{version}",
                AssemblyPath = asset,
                IncludeAll = true,
                Similar = true,
                JsonOutput = true,
                SourceOptions = sourceOptions,
            };

            var (discoveryExit, output, discoveryError) = await RunAsync(discovery);

            Assert.Equal(0, discoveryExit);
            Assert.Empty(discoveryError);
            Assert.Contains(
                $"--package '{packageName}@{version}' --library '{asset}' --tfm 'net10.0' "
                    + $"--source '{source}'",
                Parse(output).GetProperty("disclosure").GetString());

            MatchOptions replay = discovery with
            {
                LeftSelector = SampleSeed,
                RightSelector = $"{typeof(MatchDiscoverySample).FullName}.ExactPeer",
                Similar = false,
                JsonOutput = false,
            };
            string[] replayArguments =
            [
                "match",
                replay.LeftSelector!,
                replay.RightSelector!,
                "--package",
                replay.PackagePath!,
                "--library",
                replay.AssemblyPath!,
                "--tfm",
                "net10.0",
                "--all",
            ];
            var (withoutSourceExit, _, withoutSourceError) =
                await RunCliAsync(replayArguments);
            var (withSourceExit, _, withSourceError) =
                await RunCliAsync([.. replayArguments, "--source", source]);

            Assert.Equal(1, withoutSourceExit);
            Assert.Contains("not available offline", withoutSourceError);
            Assert.Equal(0, withSourceExit);
            Assert.Empty(withSourceError);
        }
        finally
        {
            Core.HttpClientFactory.Initialize(
                new Core.HttpClientFactoryOptions { Offline = wasOffline });
            Core.HttpClientFactory.ResetSharedForTesting();
            NuGetCache.Initialize("dotnet-inspect");
            if (Directory.Exists(cacheDirectory))
                Directory.Delete(cacheDirectory, recursive: true);
        }
    }

    [Theory]
    [InlineData("", null, false, false)]
    [InlineData("@1.*", null, false, false)]
    [InlineData("@1.0.0..1.0.0", "last", false, false)]
    [InlineData("@1.0.0..1.0.0", "last", true, false)]
    [InlineData("@1.0.0..1.0.0", "last", true, true)]
    public async Task Similar_SelectedVersionProducer_ReplayReopensTheSamePayload(
        string packageVersionSelector,
        string? rangeAddress,
        bool useConfig,
        bool useMapping)
    {
        string root = Path.Combine(
            Path.GetTempPath(),
            $"match-range-source-replay-{Guid.NewGuid():N}");
        string cacheRoot = Path.Combine(root, "cache");
        string packageName = $"Match.Range.Replay.{Guid.NewGuid():N}";
        const string version = "1.0.0";
        string asset = $"lib/net10.0/{Path.GetFileName(TestAssembly)}";
        Directory.CreateDirectory(root);
        using var feed = new RangeReplayFeed(packageName, version);
        string sourceA = feed.SourceA;
        string sourceB = feed.SourceB;
        string configPath = Path.Combine(root, "nuget.config");
        if (useConfig)
        {
            File.WriteAllText(
                configPath,
                $"""
                <configuration>
                  <packageSources>
                    <clear />
                    <add key="feed-a" value="{sourceA}" />
                    <add key="feed-b" value="{sourceB}" />
                  </packageSources>
                  {(useMapping
                    ? $"""
                      <packageSourceMapping>
                        <packageSource key="feed-b">
                          <package pattern="{packageName}" />
                        </packageSource>
                      </packageSourceMapping>
                      """
                    : "")}
                </configuration>
                """);
        }
        bool wasOffline = Core.HttpClientFactory.IsOffline;

        try
        {
            Core.HttpClientFactory.Initialize(
                new Core.HttpClientFactoryOptions { Offline = false });
            Core.HttpClientFactory.ResetSharedForTesting();
            NuGetCache.Initialize(
                "dotnet-inspect-match-range-source-replay",
                cacheRoot,
                skipNuGetCache: true);
            string packageA = CreatePackageArchive(
                root,
                "package-a",
                packageName,
                version,
                asset,
                [1, 2, 3, 4]);
            string packageB = CreatePackageArchive(
                root,
                "package-b",
                packageName,
                version,
                asset,
                File.ReadAllBytes(TestAssembly));
            CommitCachedPackage(
                root,
                "staged-a",
                packageA,
                packageName,
                version,
                sourceA);
            CommitCachedPackage(
                root,
                "staged-b",
                packageB,
                packageName,
                version,
                sourceB);

            var sourceOptions = new NuGetSourceOptions
            {
                Sources = useConfig ? [] : [sourceA, sourceB],
                ConfigFile = useConfig ? configPath : null,
            };
            var discovery = new MatchOptions
            {
                LeftSelector = SampleSeed,
                PackagePath = $"{packageName}{packageVersionSelector}",
                PackageRangeAddress = rangeAddress,
                AssemblyPath = asset,
                IncludeAll = true,
                Similar = true,
                JsonOutput = true,
                SourceOptions = sourceOptions,
            };

            var (discoveryExit, output, discoveryError) =
                await RunAsync(discovery);

            Assert.True(
                discoveryExit == 0,
                $"Expected discovery success, got {discoveryExit}: {discoveryError}");
            Assert.Empty(discoveryError);
            string disclosure =
                Parse(output).GetProperty("disclosure").GetString()!;
            if (useMapping)
            {
                Assert.DoesNotContain(
                    $"--source '{sourceB}'",
                    disclosure);
            }
            else
            {
                Assert.Contains(
                    $"--source '{sourceB}'",
                    disclosure);
            }
            Assert.DoesNotContain(
                $"--source '{sourceA}'",
                disclosure);
            if (useConfig)
            {
                Assert.Contains(
                    $"--nugetconfig '{Path.GetFullPath(configPath)}'",
                    disclosure);
            }

            string[] exactArguments =
            [
                "match",
                SampleSeed,
                $"{typeof(MatchDiscoverySample).FullName}.ExactPeer",
                "--package",
                $"{packageName}@{version}",
                "--library",
                asset,
                "--tfm",
                "net10.0",
                "--all",
            ];
            var (widenedExit, _, widenedError) =
                await RunCliAsync(
                    [
                        .. exactArguments,
                        "--source",
                        sourceA,
                        "--source",
                        sourceB,
                    ]);
            string[] replaySourceArguments = useMapping
                ? ["--nugetconfig", configPath]
                : ["--source", sourceB, .. ConfigArguments()];
            var (replayExit, _, replayError) =
                await RunCliAsync(
                    [.. exactArguments, .. replaySourceArguments]);

            Assert.Equal(1, widenedExit);
            Assert.Contains(
                "Could not extract API",
                widenedError);
            Assert.Equal(0, replayExit);
            Assert.Empty(replayError);

            string[] ConfigArguments() =>
                useConfig ? ["--nugetconfig", configPath] : [];
        }
        finally
        {
            Core.HttpClientFactory.Initialize(
                new Core.HttpClientFactoryOptions { Offline = wasOffline });
            Core.HttpClientFactory.ResetSharedForTesting();
            NuGetCache.Initialize("dotnet-inspect");
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Similar_PackageAssetThatCannotBeDisclosedLosslessly_IsRefused()
    {
        string root = Path.Combine(
            Path.GetTempPath(),
            $"match-replay-containment-{Guid.NewGuid():N}");
        const string packageName = "Match.Replay.Containment";
        const string version = "1.0.0";
        const string asset = "lib/net10.0/Target\u202E.dll";
        Directory.CreateDirectory(root);

        try
        {
            string package = CreatePackageArchive(
                root,
                "package",
                packageName,
                version,
                asset,
                File.ReadAllBytes(TestAssembly));
            var options = new MatchOptions
            {
                LeftSelector = SampleSeed,
                PackagePath = package,
                AssemblyPath = asset,
                IncludeAll = true,
                Similar = true,
                JsonOutput = true,
            };

            var (exit, output, error) = await RunAsync(options);

            Assert.Equal(1, exit);
            Assert.Empty(output);
            Assert.Contains("library selector", error);
            Assert.Contains("cannot be emitted losslessly", error);
            Assert.Equal(
                -1,
                error.IndexOf("\u202E", StringComparison.Ordinal));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Similar_UnavailableForwardedSeed_ReportsTheTypedFailureAndTarget()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            $"match-missing-forwarder-{Guid.NewGuid():N}");
        string facade = Path.Combine(directory, "Facade.dll");
        Directory.CreateDirectory(directory);

        try
        {
            File.WriteAllBytes(
                facade,
                BuildForwarderFacade(
                    "Facade",
                    new AssemblyReferenceIdentity(
                        "Missing.Target",
                        new Version(1, 0, 0, 0),
                        "fr-FR",
                        "0011223344556677"),
                    "Missing",
                    "Forwarded"));
            var options = new MatchOptions
            {
                LeftSelector = "Missing.Forwarded.Member",
                AssemblyPath = facade,
                IncludeAll = true,
                Similar = true,
            };

            var (exitCode, output, error) = await RunAsync(options);

            Assert.Equal(1, exitCode);
            Assert.Empty(output);
            Assert.Contains(
                "Forwarded type 'Missing.Forwarded' could not be resolved: UnboundBinding.",
                error);
            Assert.Contains(
                "Target: Missing.Target, Version=1.0.0.0, Culture=fr-FR, "
                    + "PublicKeyToken=0011223344556677.",
                error);
            Assert.DoesNotContain("must name a Type.Member selector", error);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void ResolveSelector_AmbiguousUnavailableForwardedTypeDoesNotInventATarget()
    {
        var api = new ApiSurface();
        api.InspectionFailures.Add(ForwardingFailure(
            DefinitionName("A", "Widget"),
            "A.Target"));
        api.InspectionFailures.Add(ForwardingFailure(
            DefinitionName("B", "Widget"),
            "B.Target"));

        MatchCommand.ResolvedSelector resolved =
            MatchCommand.ResolveSelector(api, "/images/Facade.dll", "Widget.Member");

        Assert.Contains("must name a Type.Member selector", resolved.Error);
        Assert.DoesNotContain("A.Target", resolved.Error);
        Assert.DoesNotContain("B.Target", resolved.Error);
    }

    [Fact]
    public void ResolveSelector_ExactHealthyTypeDoesNotReportAFailedForwarderPrefix()
    {
        var api = new ApiSurface();
        api.Types.Add(new ApiType
        {
            Namespace = "A.Widget",
            Name = "Member",
        });
        api.InspectionFailures.Add(ForwardingFailure(
            DefinitionName("A", "Widget"),
            "Failed.Target"));

        MatchCommand.ResolvedSelector resolved =
            MatchCommand.ResolveSelector(api, "/images/Facade.dll", "A.Widget.Member");

        Assert.Contains("must name a Type.Member selector", resolved.Error);
        Assert.DoesNotContain("Forwarded type", resolved.Error);
        Assert.DoesNotContain("Failed.Target", resolved.Error);
    }

    [Fact]
    public void ResolveSelector_MalformedDoubleDotDoesNotReportAForwarderFailure()
    {
        var api = new ApiSurface();
        api.InspectionFailures.Add(ForwardingFailure(
            DefinitionName("A", "Widget"),
            "Failed.Target"));

        MatchCommand.ResolvedSelector resolved =
            MatchCommand.ResolveSelector(api, "/images/Facade.dll", "A.Widget..Bogus");

        Assert.Contains("must name a Type.Member selector", resolved.Error);
        Assert.DoesNotContain("Forwarded type", resolved.Error);
        Assert.DoesNotContain("Failed.Target", resolved.Error);
    }

    [Fact]
    public void ResolveSelector_TrailingDotDoesNotReportAForwarderFailure()
    {
        var api = new ApiSurface();
        api.InspectionFailures.Add(ForwardingFailure(
            DefinitionName("A", "Widget"),
            "Failed.Target"));

        MatchCommand.ResolvedSelector resolved =
            MatchCommand.ResolveSelector(api, "/images/Facade.dll", "A.Widget.");

        Assert.Contains("must name a Type.Member selector", resolved.Error);
        Assert.DoesNotContain("Forwarded type", resolved.Error);
        Assert.DoesNotContain("Failed.Target", resolved.Error);
    }

    [Theory]
    [InlineData("A.Widget...ctor")]
    [InlineData("A.Widget...cctor")]
    public void ResolveSelector_RepeatedDotConstructorDoesNotReportAForwarderFailure(
        string selector)
    {
        var api = new ApiSurface();
        api.InspectionFailures.Add(ForwardingFailure(
            DefinitionName("A", "Widget"),
            "Failed.Target"));

        MatchCommand.ResolvedSelector resolved =
            MatchCommand.ResolveSelector(api, "/images/Facade.dll", selector);

        Assert.Contains("must name a Type.Member selector", resolved.Error);
        Assert.DoesNotContain("Forwarded type", resolved.Error);
        Assert.DoesNotContain("Failed.Target", resolved.Error);
    }

    [Fact]
    public void ResolveSelector_ExplicitGenericArityDoesNotReportAnotherForwardedType()
    {
        var api = new ApiSurface();
        api.InspectionFailures.Add(ForwardingFailure(
            DefinitionName("A", "Widget`2"),
            "Failed.Target"));

        MatchCommand.ResolvedSelector resolved =
            MatchCommand.ResolveSelector(api, "/images/Facade.dll", "Widget<T>.Member");

        Assert.Contains("must name a Type.Member selector", resolved.Error);
        Assert.DoesNotContain("Forwarded type", resolved.Error);
        Assert.DoesNotContain("Failed.Target", resolved.Error);
    }

    /// <summary>
    /// The directly named library keeps its full path, which is already replayable. Only the
    /// package case may substitute a package-relative spelling.
    /// </summary>
    [Fact]
    public void Disclosure_ForADirectlyNamedLibrary_KeepsThePathAndNamesNoPackage()
    {
        var request = new MatchDiscoveryRequest(
            "A.Type.Member",
            "A.Type",
            "/images/Target.dll",
            new ILInspector.Analysis.StructuralCloneRetrievalLimits(1, 1),
            null);

        string disclosure = MatchDiscoveryFormatter.DisclosureFor(request);

        Assert.Contains("`--library '/images/Target.dll'`", disclosure);
        Assert.DoesNotContain("--package", disclosure);
    }

    /// <summary>
    /// The candidate image that came out of a package extraction is addressed by its exact
    /// package-relative asset and TFM, so another same-named assembly cannot win during replay.
    /// </summary>
    [Fact]
    public void ReplayableCandidateAddress_ForAnImageInsideTheExtraction_RetainsTheExactAsset()
    {
        string extraction = Path.Combine(Path.GetTempPath(), "inspect-api-xyz", "extracted");

        MatchDiscovery.ReplayableCandidateAddress address =
            MatchDiscovery.GetReplayableCandidateAddress(
                "Fixture@1.0.0",
                extraction,
                Path.Combine(extraction, "lib", "net10.0", "Target.dll"));

        Assert.Equal("Fixture@1.0.0", address.Package);
        Assert.Equal("lib/net10.0/Target.dll", address.Library);
        Assert.Equal("net10.0", address.Tfm);
    }

    /// <summary>
    /// A directly named library outlives the command, so its path is disclosed unchanged. Nothing
    /// here may shorten an address the caller can still use.
    /// </summary>
    [Fact]
    public void ReplayableCandidateAddress_ForADirectlyNamedLibrary_KeepsThePathIntact()
    {
        MatchDiscovery.ReplayableCandidateAddress address =
            MatchDiscovery.GetReplayableCandidateAddress(null, null, "/images/Target.dll");

        Assert.Null(address.Package);
        Assert.Equal("/images/Target.dll", address.Library);
        Assert.Null(address.Tfm);
    }

    /// <summary>
    /// A package run whose candidate image lives outside the extraction is a path the caller
    /// supplied, so it survives the command and must not be rewritten to a bare file name that
    /// the package does not contain.
    /// </summary>
    [Fact]
    public void ReplayableCandidateAddress_ForAnImageOutsideTheExtraction_KeepsThePathIntact()
    {
        MatchDiscovery.ReplayableCandidateAddress address =
            MatchDiscovery.GetReplayableCandidateAddress(
                "Fixture.1.0.0.nupkg",
                Path.Combine(Path.GetTempPath(), "inspect-api-xyz"),
                "/images/Target.dll");

        Assert.Null(address.Package);
        Assert.Equal("/images/Target.dll", address.Library);
        Assert.Null(address.Tfm);
    }

    [Fact]
    public void ReplayableCandidateAddress_RejectsAnExtractionSiblingWithTheSamePrefix()
    {
        string extraction = Path.Combine(Path.GetTempPath(), "inspect-api-xyz");
        string candidate = Path.Combine(
            Path.GetTempPath(),
            "inspect-api-xyz-other",
            "lib",
            "net10.0",
            "Target.dll");

        MatchDiscovery.ReplayableCandidateAddress address =
            MatchDiscovery.GetReplayableCandidateAddress(
                "Fixture@1.0.0",
                extraction,
                candidate);

        Assert.Null(address.Package);
        Assert.Equal(candidate, address.Library);
        Assert.Null(address.Tfm);
    }

    [Fact]
    public void ReplayableCandidateAddress_ForAGlobalPackageDependency_UsesItsOwnCoordinate()
    {
        string candidate = Path.Combine(
            DotnetInspector.Packages.NuGetCache.GetNuGetCachePath(),
            "dependency.fixture",
            "2.3.4",
            "lib",
            "net10.0",
            "Dependency.dll");

        MatchDiscovery.ReplayableCandidateAddress address =
            MatchDiscovery.GetReplayableCandidateAddress(
                "facade.fixture@1.0.0",
                Path.Combine(Path.GetTempPath(), "facade-fixture"),
                candidate,
                AssemblyResolutionProvenance.Package(
                    "dependency.fixture",
                    "2.3.4",
                    tfm: null,
                    rid: null));

        Assert.Equal("dependency.fixture@2.3.4", address.Package);
        Assert.Equal("lib/net10.0/Dependency.dll", address.Library);
        Assert.Equal("net10.0", address.Tfm);
    }

    [Fact]
    public void ReplayableCandidateAddress_ChecksTheDefaultRootAfterAnOverride()
    {
        string overrideRoot = Path.Combine(Path.GetTempPath(), "override-packages");
        string defaultRoot = Path.Combine(Path.GetTempPath(), "default-packages");
        string candidate = Path.Combine(
            defaultRoot,
            "dependency.fixture",
            "2.3.4",
            "lib",
            "net10.0",
            "Dependency.dll");

        MatchDiscovery.ReplayableCandidateAddress address =
            MatchDiscovery.GetReplayableCandidateAddress(
                "facade.fixture@1.0.0",
                Path.Combine(Path.GetTempPath(), "facade-fixture"),
                candidate,
                AssemblyResolutionProvenance.Package(
                    "dependency.fixture",
                    "2.3.4",
                    tfm: null,
                    rid: null),
                packageRoots: [overrideRoot, defaultRoot]);

        Assert.Equal("dependency.fixture@2.3.4", address.Package);
        Assert.Equal("lib/net10.0/Dependency.dll", address.Library);
        Assert.Equal("net10.0", address.Tfm);
    }

    [Fact]
    public void ReplayablePackage_ReplacesARangeWithTheResolvedExactVersion()
    {
        string? package = MatchDiscovery.GetReplayablePackage(
            "Fixture@1.0.0..2.0.0",
            "Fixture",
            "1.2.3");

        Assert.Equal("Fixture@1.2.3", package);
    }

    [Fact]
    public void ReplayablePackage_ReplacesAnUnversionedCoordinateWithTheResolvedExactVersion()
    {
        string? package = MatchDiscovery.GetReplayablePackage(
            "Fixture",
            "Fixture",
            "1.2.3");

        Assert.Equal("Fixture@1.2.3", package);
    }

    [Fact]
    public void Disclosure_ShellQuotesPackageAssetAndTfm()
    {
        var request = new MatchDiscoveryRequest(
            "A.Type.Member",
            "A.Type",
            "lib/net10.0/Target's build.dll",
            new ILInspector.Analysis.StructuralCloneRetrievalLimits(1, 1),
            null,
            CandidatePackage: "/packages/Fixture's build.nupkg",
            CandidateTfm: "net10.0");

        string disclosure = MatchDiscoveryFormatter.DisclosureFor(request);

        Assert.Contains(
            "--package '/packages/Fixture'\"'\"'s build.nupkg' "
                + "--library 'lib/net10.0/Target'\"'\"'s build.dll' --tfm 'net10.0'",
            disclosure);
    }

    [Fact]
    public void Disclosure_PackageReplayRetainsSourceSelection()
    {
        var request = new MatchDiscoveryRequest(
            "A.Type.Member",
            "A.Type",
            "lib/net10.0/Target.dll",
            new ILInspector.Analysis.StructuralCloneRetrievalLimits(1, 1),
            null,
            CandidatePackage: "Fixture@1.0.0",
            CandidateTfm: "net10.0",
            ReplaySources: new MatchDiscoveryReplaySources(
                ["https://feed-a.invalid/v3/index.json"],
                ["https://feed-b.invalid/v3/index.json"],
                "/configs/NuGet Config"));

        string disclosure = MatchDiscoveryFormatter.DisclosureFor(request);

        Assert.Contains(
            "--source 'https://feed-a.invalid/v3/index.json' "
                + "--add-source 'https://feed-b.invalid/v3/index.json' "
                + "--nugetconfig '/configs/NuGet Config'",
            disclosure);
    }

    [Fact]
    public void ReplaySources_RejectAValueThatDiagnosticsWouldRedact()
    {
        const string secret = "do-not-print";

        bool accepted = MatchDiscovery.TryGetReplaySources(
            new NuGetSourceOptions
            {
                Sources = [$"https://feed.invalid/v3/index.json?token={secret}"],
            },
            out MatchDiscoveryReplaySources? replaySources,
            out string? error);

        Assert.False(accepted);
        Assert.Null(replaySources);
        Assert.Contains("--source", error);
        Assert.Contains("--nugetconfig", error);
        Assert.DoesNotContain(secret, error);
    }

    [Fact]
    public void ReplaySources_SelectedProducerRedactionDoesNotRecommendTheConfigAlreadyInUse()
    {
        const string secret = "do-not-print";

        bool accepted = MatchDiscovery.TryGetReplaySources(
            new NuGetSourceOptions
            {
                Sources = [$"https://feed.invalid/v3/index.json?token={secret}"],
                ConfigFile = "nuget.config",
            },
            out MatchDiscoveryReplaySources? replaySources,
            out string? error,
            selectedVersionSourceRestriction: true);

        Assert.False(accepted);
        Assert.Null(replaySources);
        Assert.Contains("package source mapping", error);
        Assert.DoesNotContain("Configure that source", error);
        Assert.DoesNotContain(secret, error);
    }

    [Theory]
    [InlineData("http://localhost:5000")]
    [InlineData("https://feed.test:443/v3/index.json")]
    [InlineData("https://MyFeed.Test/v3/index.json")]
    [InlineData("HTTPS://feed.test/v3/index.json")]
    [InlineData("https://feed.test/my%20feed/index.json")]
    public void ReplaySources_AcceptHarmlessUrlNormalization(string source)
    {
        bool accepted = MatchDiscovery.TryGetReplaySources(
            new NuGetSourceOptions { Sources = [source] },
            out MatchDiscoveryReplaySources? replaySources,
            out string? error);

        Assert.True(accepted, error);
        Assert.Equal(source, Assert.Single(replaySources!.Sources));
    }

    [Fact]
    public void ReplaySources_MakesTheConfigPathIndependentOfTheNextWorkingDirectory()
    {
        string relative = Path.Combine("config", "NuGet.Config");

        bool accepted = MatchDiscovery.TryGetReplaySources(
            new NuGetSourceOptions { ConfigFile = relative },
            out MatchDiscoveryReplaySources? replaySources,
            out string? error);

        Assert.True(accepted, error);
        Assert.Equal(Path.GetFullPath(relative), replaySources!.ConfigFile);
    }

    [Fact]
    public void PhysicalImageLoad_ClearsPackageRangeCoordinates()
    {
        var options = new MatchOptions
        {
            PackagePath = "Fixture@1.0.0..2.0.0",
            PackageRangeAddress = "#3",
        };

        MatchOptions physical = MatchDiscovery.ForPhysicalImageLoad(options);

        Assert.Null(physical.PackagePath);
        Assert.Null(physical.PackageRangeAddress);
    }

    static byte[] BuildForwarderFacade(
        string assemblyName,
        string targetAssemblyPath,
        Type forwardedType)
    {
        using var targetPe = new PEReader(File.OpenRead(targetAssemblyPath));
        MetadataReader targetReader = targetPe.GetMetadataReader();
        AssemblyDefinition target = targetReader.GetAssemblyDefinition();
        return BuildForwarderFacade(
            assemblyName,
            new AssemblyReferenceIdentity(
                targetReader.GetString(target.Name),
                target.Version,
                null,
                null),
            forwardedType.Namespace!,
            forwardedType.Name);
    }

    static byte[] BuildForwarderFacade(
        string assemblyName,
        AssemblyReferenceIdentity target,
        string typeNamespace,
        string typeName)
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            generation: 0,
            moduleName: metadata.GetOrAddString($"{assemblyName}.dll"),
            mvid: metadata.GetOrAddGuid(Guid.NewGuid()),
            encId: default,
            encBaseId: default);
        metadata.AddAssembly(
            metadata.GetOrAddString(assemblyName),
            new Version(1, 0, 0, 0),
            culture: default,
            publicKey: default,
            flags: default,
            hashAlgorithm: default);
        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            baseType: default,
            fieldList: MetadataTokens.FieldDefinitionHandle(1),
            methodList: MetadataTokens.MethodDefinitionHandle(1));

        AssemblyReferenceHandle targetReference = metadata.AddAssemblyReference(
            metadata.GetOrAddString(target.Name),
            target.Version
                ?? throw new InvalidOperationException(
                    "The forwarder fixture requires a target assembly version."),
            culture: target.Culture is null
                ? default
                : metadata.GetOrAddString(target.Culture),
            publicKeyOrToken: target.PublicKeyToken is null
                ? default
                : metadata.GetOrAddBlob(
                    Convert.FromHexString(target.PublicKeyToken)),
            flags: default,
            hashValue: default);
        metadata.AddExportedType(
            TypeAttributes.Public | (TypeAttributes)0x00200000,
            metadata.GetOrAddString(typeNamespace),
            metadata.GetOrAddString(typeName),
            targetReference,
            typeDefinitionId: 0);

        var builder = new ManagedPEBuilder(
            PEHeaderBuilder.CreateLibraryHeader(),
            new MetadataRootBuilder(metadata),
            new BlobBuilder(),
            flags: CorFlags.ILOnly);
        var image = new BlobBuilder();
        builder.Serialize(image);
        return image.ToArray();
    }

    static string CreatePackageArchive(
        string root,
        string fileName,
        string packageName,
        string version,
        string asset,
        byte[] assetBytes)
    {
        string path = Path.Combine(root, $"{fileName}.nupkg");
        using ZipArchive archive = ZipFile.Open(
            path,
            ZipArchiveMode.Create);
        ZipArchiveEntry library = archive.CreateEntry(asset);
        using (Stream stream = library.Open())
            stream.Write(assetBytes);
        ZipArchiveEntry nuspec =
            archive.CreateEntry($"{packageName}.nuspec");
        using (Stream stream = nuspec.Open())
        using (var writer = new StreamWriter(stream))
        {
            writer.Write(
                $"""
                <?xml version="1.0"?>
                <package>
                  <metadata>
                    <id>{packageName}</id>
                    <version>{version}</version>
                    <authors>dotnet-inspect tests</authors>
                    <description>range replay fixture</description>
                  </metadata>
                </package>
                """);
        }

        return path;
    }

    static void CommitCachedPackage(
        string root,
        string directoryName,
        string nupkg,
        string packageName,
        string version,
        string source)
    {
        string extracted = Path.Combine(root, directoryName);
        ZipFile.ExtractToDirectory(nupkg, extracted);
        NuGetCache.CommitPackage(
            extracted,
            nupkg,
            packageName,
            version,
            NuGetCache.GetSourceKey(source));
    }

    private sealed class RangeReplayFeed : IDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource _shutdown = new();
        private readonly string _packageId;
        private readonly string _version;

        public RangeReplayFeed(string packageId, string version)
        {
            _packageId = packageId.ToLowerInvariant();
            _version = version;
            _listener.Start();
            int port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            SourceA = $"http://127.0.0.1:{port}/a/index.json";
            SourceB = $"http://127.0.0.1:{port}/b/index.json";
            _ = Task.Run(() => ServeAsync(_shutdown.Token));
        }

        public string SourceA { get; }

        public string SourceB { get; }

        public void Dispose()
        {
            _shutdown.Cancel();
            _listener.Stop();
            _shutdown.Dispose();
        }

        private async Task ServeAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                TcpClient client;
                try
                {
                    client = await _listener.AcceptTcpClientAsync(cancellationToken);
                }
                catch (Exception) when (cancellationToken.IsCancellationRequested)
                {
                    return;
                }
                catch (SocketException)
                {
                    return;
                }

                _ = Task.Run(
                    () => RespondAsync(client, cancellationToken),
                    CancellationToken.None);
            }
        }

        private async Task RespondAsync(
            TcpClient client,
            CancellationToken cancellationToken)
        {
            using (client)
            {
                NetworkStream stream = client.GetStream();
                var buffer = new byte[4096];
                int read = await stream.ReadAsync(buffer, cancellationToken);
                string request = Encoding.ASCII.GetString(buffer, 0, read);
                string path =
                    request.Split(' ').Skip(1).FirstOrDefault() ?? string.Empty;
                int port = ((IPEndPoint)_listener.LocalEndpoint).Port;
                string? body = path switch
                {
                    "/a/index.json" =>
                        $$"""{"resources":[{"@type":"PackageBaseAddress/3.0.0","@id":"http://127.0.0.1:{{port}}/a/flat/"}]}""",
                    "/b/index.json" =>
                        $$"""{"resources":[{"@type":"PackageBaseAddress/3.0.0","@id":"http://127.0.0.1:{{port}}/b/flat/"}]}""",
                    var value when value.Equals(
                        $"/a/flat/{_packageId}/index.json",
                        StringComparison.OrdinalIgnoreCase) =>
                        """{"versions":[]}""",
                    var value when value.Equals(
                        $"/b/flat/{_packageId}/index.json",
                        StringComparison.OrdinalIgnoreCase) =>
                        $$"""{"versions":["{{_version}}"]}""",
                    _ => null,
                };
                HttpStatusCode status =
                    body is null ? HttpStatusCode.NotFound : HttpStatusCode.OK;
                byte[] bytes = Encoding.UTF8.GetBytes(body ?? "");
                byte[] head = Encoding.ASCII.GetBytes(
                    $"HTTP/1.1 {(int)status} {status}\r\n"
                        + "Content-Type: application/json\r\n"
                        + $"Content-Length: {bytes.Length}\r\n"
                        + "Connection: close\r\n\r\n");
                await stream.WriteAsync(head, cancellationToken);
                await stream.WriteAsync(bytes, cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }
        }
    }

    static byte[] BuildMatchTargetAssembly(
        string assemblyName,
        string typeNamespace,
        string typeName)
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            generation: 0,
            moduleName: metadata.GetOrAddString($"{assemblyName}.dll"),
            mvid: metadata.GetOrAddGuid(Guid.NewGuid()),
            encId: default,
            encBaseId: default);
        metadata.AddAssembly(
            metadata.GetOrAddString(assemblyName),
            new Version(1, 0, 0, 0),
            culture: default,
            publicKey: default,
            flags: default,
            hashAlgorithm: default);
        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            baseType: default,
            fieldList: MetadataTokens.FieldDefinitionHandle(1),
            methodList: MetadataTokens.MethodDefinitionHandle(1));
        metadata.AddTypeDefinition(
            TypeAttributes.Public
                | TypeAttributes.Abstract
                | TypeAttributes.Sealed,
            metadata.GetOrAddString(typeNamespace),
            metadata.GetOrAddString(typeName),
            baseType: default,
            fieldList: MetadataTokens.FieldDefinitionHandle(1),
            methodList: MetadataTokens.MethodDefinitionHandle(1));

        var bodies = new BlobBuilder();
        var bodyEncoder = new MethodBodyStreamEncoder(bodies);
        AddMatchTargetMethod(metadata, bodyEncoder, "Seed");
        AddMatchTargetMethod(metadata, bodyEncoder, "ExactPeer");

        var builder = new ManagedPEBuilder(
            PEHeaderBuilder.CreateLibraryHeader(),
            new MetadataRootBuilder(metadata),
            bodies,
            flags: CorFlags.ILOnly);
        var image = new BlobBuilder();
        builder.Serialize(image);
        return image.ToArray();
    }

    static void AddMatchTargetMethod(
        MetadataBuilder metadata,
        MethodBodyStreamEncoder bodies,
        string name)
    {
        var code = new BlobBuilder();
        code.WriteBytes(new byte[] { 0x17, 0x18, 0x58, 0x26, 0x2A });
        int body = bodies.AddMethodBody(
            new InstructionEncoder(code),
            maxStack: 2);
        var signature = new BlobBuilder();
        new BlobEncoder(signature)
            .MethodSignature(isInstanceMethod: false)
            .Parameters(
                parameterCount: 0,
                returnType => returnType.Void(),
                parameters => { });
        metadata.AddMethodDefinition(
            MethodAttributes.Public | MethodAttributes.Static,
            MethodImplAttributes.IL,
            metadata.GetOrAddString(name),
            metadata.GetOrAddBlob(signature),
            body,
            MetadataTokens.ParameterHandle(1));
    }

    static ApiSurfaceInspectionFailure ForwardingFailure(
        MetadataTypeDefinitionName type,
        string targetAssembly)
        => new(
            "resolve forwarded type",
            0,
            MetadataTypeNameFailureMechanism.Metadata,
            "UnboundBinding",
            $"Forwarded type '{type.ToMetadataFullName()}' could not be resolved: UnboundBinding.",
            new AssemblyReferenceIdentity("Facade", new Version(1, 0, 0, 0), null, null),
            new AssemblyReferenceIdentity(targetAssembly, new Version(1, 0, 0, 0), null, null))
        {
            AffectedTypeDefinitions = [type],
        };

    static MetadataTypeDefinitionName DefinitionName(
        string @namespace,
        string name)
        => Assert.IsType<MetadataTypeDefinitionNameResult.Valid>(
            MetadataTypeDefinitionName.Create(@namespace, [name])).Name;

    /// <summary>
    /// The README tells same-image callers to confirm a candidate with the pairwise form. That
    /// instruction must stay scoped, because there is no cross-image confirmation to run.
    /// </summary>
    [Fact]
    public void Readme_DoesNotPromiseCrossImagePairwiseConfirmation()
    {
        string readme = Path.Combine(CommandErrorOwnershipTests.RepositoryRoot(), "README.md");
        string text = File.ReadAllText(readme);

        int section = text.IndexOf("### Structural matching", StringComparison.Ordinal);
        Assert.True(section >= 0, "README no longer has a Structural matching section.");
        int end = text.IndexOf("\n### ", section + 1, StringComparison.Ordinal);
        string structuralMatching = end < 0 ? text[section..] : text[section..end];

        Assert.DoesNotContain(
            "Confirm a candidate by re-running the pairwise form on the selected pair.",
            structuralMatching);
        Assert.Contains("Within one image, confirm a candidate", structuralMatching);
    }

    /// <summary>
    /// The range grammar was removed from the product, but the surviving promise of it sat in the
    /// command table, outside the Structural matching section the gate above reads. Scoping a
    /// documentation gate to one section is why that line shipped stale for a full round; this one
    /// reads the whole file.
    /// </summary>
    [Fact]
    public void Readme_DoesNotPromiseTheRemovedLibraryRangeGrammar()
    {
        string readme = Path.Combine(CommandErrorOwnershipTests.RepositoryRoot(), "README.md");
        string text = File.ReadAllText(readme);

        Assert.DoesNotContain("old.dll..new.dll", text);
        Assert.DoesNotContain("--library old", text);
    }

    // ---- Round 7 review findings: raw MethodDef token selectors ----

    /// <summary>
    /// A MethodDef token is a dense table row index, not an identity. Resolving one against a
    /// merged surface -- which includes type-forwarded types whose rows live in other images --
    /// binds it to whichever type collides first. Feeding the tool its own printed token back
    /// returned a confidently wrong member at exit 0, in both the pairwise and the seeded
    /// direction, for four consecutive rounds. A token now resolves only against the one image
    /// named by <c>--library</c>, and never silently against a forwarded one.
    /// </summary>
    [Fact]
    public async Task Pairwise_TokenOutsideTheNamedImage_FailsRatherThanNamingAnotherMember()
    {
        string coreLibrary = typeof(string).Assembly.Location;
        string facade = Path.Combine(Path.GetDirectoryName(coreLibrary)!, "System.Runtime.dll");
        Assert.True(File.Exists(facade), facade);

        // System.Runtime is a pure facade: it defines no method bodies, so no MethodDef row it
        // could be asked about is its own. Any answer other than a failure is a wrong answer.
        MatchOptions options = new()
        {
            LeftSelector = "0x06000001",
            RightSelector = "0x06000001",
            AssemblyPath = facade,
        };

        var (exitCode, output, error) = await RunAsync(options);

        Assert.Equal(1, exitCode);
        Assert.Empty(output);
        Assert.DoesNotContain("Relation", error);
        Assert.Contains("is not a MethodDef row in", error);
        Assert.Contains("System.Runtime.dll", error);
        Assert.DoesNotContain("method handle is outside", error);
    }

    /// <summary>
    /// The seeded direction of the same defect: a token seed silently scoped discovery to whatever
    /// forwarded type its row collided with, then ranked candidates inside that unrelated type and
    /// reported success. Failing is the only honest outcome when the named image does not define
    /// the row.
    /// </summary>
    [Fact]
    public async Task Similar_TokenSeedOutsideTheNamedImage_DoesNotScopeToAForeignType()
    {
        string coreLibrary = typeof(string).Assembly.Location;
        string facade = Path.Combine(Path.GetDirectoryName(coreLibrary)!, "System.Runtime.dll");
        Assert.True(File.Exists(facade), facade);

        MatchOptions options = Seeded("0x06000001") with { AssemblyPath = facade };

        var (exitCode, output, error) = await RunAsync(options);

        // Discovery fails on its own terms here -- it cannot determine a declaring type for a row
        // the named image does not define. The defect was that it *could*: the row collided with a
        // forwarded type and silently scoped retrieval to it.
        Assert.Equal(1, exitCode);
        Assert.Empty(output);
        Assert.DoesNotContain("SortedDictionary", error);
        Assert.DoesNotContain("method handle is outside", error);
    }

    /// <summary>
    /// An out-of-range row must produce the command's own typed selector error. Reaching metadata
    /// with an unchecked row surfaced a framework resource name (<c>Arg_ParamName_Name</c>) to the
    /// user instead.
    /// </summary>
    [Fact]
    public async Task Pairwise_TokenBeyondTheMethodDefTable_ReportsATypedSelectorError()
    {
        MatchOptions options = new()
        {
            LeftSelector = "0x06FFFFFF",
            RightSelector = "0x06FFFFFF",
            AssemblyPath = TestAssembly,
        };

        var (exitCode, output, error) = await RunAsync(options);

        Assert.Equal(1, exitCode);
        Assert.Empty(output);

        // The command's own selector error, naming the token and the image -- not the Analysis
        // layer's ArgumentOutOfRangeException, whose message is a framework resource. On a
        // resource-stripped AOT host that message renders as the raw key "Arg_ParamName_Name".
        Assert.Contains("0x06FFFFFF", error);
        Assert.Contains("is not a MethodDef row in", error);
        Assert.DoesNotContain("method handle is outside", error);
        Assert.DoesNotContain("Arg_ParamName_Name", error);
    }

    /// <summary>
    /// The contract the token selector exists to serve: a token this run printed must address the
    /// same member when handed straight back. This is the round-trip that was silently broken.
    /// </summary>
    [Fact]
    public async Task Similar_PrintedToken_AddressesTheSameMemberWhenFedBack()
    {
        MatchOptions options = Seeded(SampleSeed) with { JsonOutput = true };

        var (exitCode, output, error) = await RunAsync(options);
        Assert.Equal(0, exitCode);
        Assert.Empty(error);

        JsonElement document = Parse(output);
        JsonElement first = document.GetProperty("candidates").EnumerateArray().First();
        string token = first.GetProperty("token").GetString()!;
        string member = first.GetProperty("member").GetString()!;

        var (pairExit, pairOutput, pairError) = await RunAsync(new MatchOptions
        {
            LeftSelector = token,
            RightSelector = token,
            AssemblyPath = TestAssembly,
        });

        Assert.Equal(0, pairExit);
        Assert.Empty(pairError);
        Assert.Contains(member[..member.LastIndexOf('.')], pairOutput);
    }

    /// <summary>
    /// <c>--assembly-wide</c> with a forwarded seed searched the facade, which defines no bodies,
    /// and reported an empty ranking at exit 0 -- while the narrower default scope ranked real
    /// candidates. A widened scope must never return strictly less than the narrower one.
    /// </summary>
    [Fact]
    public async Task Similar_AssemblyWideWithForwardedSeed_SearchesTheDefiningImage()
    {
        string coreLibrary = typeof(string).Assembly.Location;
        string facade = Path.Combine(Path.GetDirectoryName(coreLibrary)!, "System.Runtime.dll");
        Assert.True(File.Exists(facade), facade);

        MatchOptions options = Seeded("System.String.IsNullOrEmpty") with
        {
            AssemblyPath = facade,
            AssemblyWide = true,
            JsonOutput = true,
        };

        var (exitCode, output, error) = await RunAsync(options);

        Assert.Equal(0, exitCode);
        Assert.Empty(error);
        Assert.NotEmpty(Candidates(Parse(output)));
    }

    /// <summary>
    /// The printed token is a promise scoped to the image the disclosure names. Feeding a token
    /// that discovery printed for a forwarded population back against the facade must fail: the
    /// facade does not define that row. Resolving the token through the merged surface instead --
    /// which carries forwarded types whose rows live elsewhere -- silently re-attributed it to the
    /// defining image and compared a member the caller never named, at exit 0.
    /// </summary>
    [Fact]
    public async Task Pairwise_TokenPrintedForAForwardedPopulation_IsNotHonoredAgainstTheFacade()
    {
        string coreLibrary = typeof(string).Assembly.Location;
        string facade = Path.Combine(Path.GetDirectoryName(coreLibrary)!, "System.Runtime.dll");
        Assert.True(File.Exists(facade), facade);

        MatchOptions seedOptions = Seeded("System.String.IsNullOrEmpty") with
        {
            AssemblyPath = facade,
            RightSelector = "System.String",
            JsonOutput = true,
        };

        var (seedExit, seedOutput, _) = await RunAsync(seedOptions);
        Assert.Equal(0, seedExit);

        JsonElement document = Parse(seedOutput);
        string definingImage = document.GetProperty("candidate_assembly").GetString()!;
        string token = document.GetProperty("candidates").EnumerateArray()
            .First().GetProperty("token").GetString()!;

        // Against the image the disclosure named, the promise holds.
        var (honored, _, honoredError) = await RunCliAsync(
            "match", token, token, "--library", definingImage);
        Assert.Equal(0, honored);
        Assert.Empty(honoredError);

        // Against the facade the caller typed, it must fail rather than name another member.
        var (rejected, rejectedOutput, rejectedError) = await RunCliAsync(
            "match", token, token, "--library", facade);
        Assert.Equal(1, rejected);
        Assert.Empty(rejectedOutput);
        Assert.Contains("is not a MethodDef row in", rejectedError);
    }

    /// <summary>
    /// <c>System.dll</c> is a multi-target facade that defines no method bodies of its own, so no
    /// MethodDef token can name one of its members. Resolving a token through the merged surface
    /// bound it to whichever forwarded type held a colliding row -- returning
    /// <c>Relation: Exact</c> at exit 0 for a member the caller never named. Every probed row must
    /// be rejected; a facade has no row to compare.
    /// </summary>
    [Fact]
    public async Task Pairwise_TokenAgainstAPureFacade_NeverBindsToAForwardedType()
    {
        string coreLibrary = typeof(string).Assembly.Location;
        string facade = Path.Combine(Path.GetDirectoryName(coreLibrary)!, "System.dll");
        if (!File.Exists(facade))
            return;

        foreach (string token in new[] { "0x06000001", "0x06000169", "0x06000FFF" })
        {
            var (exitCode, output, error) = await RunCliAsync(
                "match", token, token, "--library", facade);

            Assert.Equal(1, exitCode);
            Assert.Empty(output);
            Assert.Contains("is not a MethodDef row in", error);
        }
    }

    /// <summary>
    /// The parse layer rejects a missing second selector before the pairwise body runs, so the
    /// guidance for "you supplied a discovery flag; add <c>--similar</c>" was reachable only when
    /// the caller had already supplied two selectors. The caller who wrote one selector and a
    /// discovery flag -- the one who most clearly meant discovery -- was told to add a selector.
    /// </summary>
    [Theory]
    [InlineData("--assembly-wide")]
    [InlineData("--top")]
    [InlineData("--max-results")]
    [InlineData("--max-methods")]
    public async Task Pairwise_OneSelectorWithADiscoveryFlag_PointsAtSimilar(string flag)
    {
        // Must run through the real parser: the check this pins lives in the command definition,
        // which rejects a missing second selector before MatchCommand.ExecuteAsync is ever called.
        string[] args = flag == "--assembly-wide"
            ? ["match", SampleSeed, "--library", TestAssembly, flag]
            : ["match", SampleSeed, "--library", TestAssembly, flag, "5"];

        var (exitCode, output, error) = await RunCliAsync(args);

        Assert.Equal(1, exitCode);
        Assert.Empty(output);
        Assert.Contains($"{flag} applies to discovery; add --similar.", error);
        Assert.DoesNotContain("requires two method selectors", error);
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
