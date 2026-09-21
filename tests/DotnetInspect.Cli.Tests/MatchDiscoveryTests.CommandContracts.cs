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

public partial class MatchDiscoveryTests
{
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
    public async Task Similar_WithBody_RejectsCombination()
    {
        MatchOptions options = Seeded($"{typeof(MatchSampleA).FullName}.AddOne") with
        {
            IncludeBody = true,
        };

        var (exitCode, output, error) = await RunAsync(options);

        Assert.Equal(1, exitCode);
        Assert.Empty(output);
        Assert.Contains("--body cannot be combined with --similar", error);
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
            seed with { MaximumResults = value },
            seed with { MaximumMethods = value },
        })
        {
            var (exitCode, output, error) = await RunAsync(options);
            Assert.Equal(1, exitCode);
            Assert.Empty(output);
            Assert.Contains("must be greater than zero", error);
        }

        var (topExit, topOutput, topError) = await RunCliAsync(
            "match",
            seed.LeftSelector!,
            "--similar",
            "--library",
            TestAssembly,
            "--all",
            "--top",
            value.ToString());
        Assert.Equal(1, topExit);
        Assert.Empty(topOutput);
        Assert.Contains("requires a positive whole number", topError);
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
            RowSelection =
                Select(RowSelectionIntentOperation<string>.Top(1)),
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
            RowSelection =
                Select(RowSelectionIntentOperation<string>.Top(1)),
        };

        var (exitCode, output, _) = await RunAsync(options);

        Assert.Equal(0, exitCode);

        // The receipt ranks more than it returns, so "ranked" and "returned" are different
        // numbers here and naming the wrong one is observable.
        Assert.Contains("3 returned", output);
        Assert.Contains("1 of 3 returned candidates selected", output);
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

    /// <summary>Candidate selection must not shorten the per-method evidence.</summary>
    [Fact]
    public async Task Similar_MethodOutcomes_AreNotBoundedByTop()
    {
        MatchOptions bounded = Seeded(SampleSeed) with
        {
            JsonOutput = true,
            RowSelection =
                Select(RowSelectionIntentOperation<string>.Top(1)),
        };
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
    [InlineData("--count")]
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
        Assert.Single(document.GetProperty("candidates").EnumerateArray());
        Assert.Equal(
            1,
            document.GetProperty("row_selection")
                .GetProperty("selected_candidates").GetInt32());
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
    [Trait("Speed", "Slow")]
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
            Limits = new MatchDiscoveryLimitsDocument(1, 1),
            RowSelection = new MatchDiscoveryRowSelectionDocument(0, 0),
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

        Assert.Contains("run pairwise `match`", disclosure);
        Assert.Contains("on a candidate with", disclosure);
        Assert.Contains(
            $"--library {ShellCommandText.Quote(TestAssembly)}",
            disclosure);
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

}
