using DotnetInspector.Commands;
using DotnetInspector.Options;
using DotnetInspector.PackageQueries;
using DotnetInspector.Packages;
using DotnetInspector.Queries;
using DotnetInspector.Sections;
using DotnetInspector.Views;
using DotnetInspector.Fixtures;
using NuGetFetch;

namespace DotnetInspector.Tests;

/// <summary>
/// Covers how the CLI projects the shared assembly Package Query's own
/// outcomes, using real producer evidence from a locally built fixture package
/// rather than a network source or a fabricated outcome.
/// </summary>
[Collection("Console")]
public sealed class PackageAssemblyQueryOutputTests
{
    const string PackageId = "Literal.Query.Fixture";
    const string Version = "1.0.0";
    const string Framework = "net11.0";
    const string AssetPath = $"lib/{Framework}/ILInspector.Analysis.Fixtures.dll";
    const string RepeatedMarker = "shared-literal-use-marker";

    [Fact]
    public async Task Matches_LeadWithOccurrenceEvidenceAndAnOwnerIssuedRootToken()
    {
        (PackageAssemblyQueryView view, PackageAssemblyEvaluationOutcome outcome) =
            await RunAsync(RepeatedMarker);

        var matched = Assert.IsType<PackageAssemblyEvaluationOutcome.Matched>(outcome);
        Assert.NotNull(view.Matches);
        Assert.Equal(matched.Evidence.Occurrences.Length, view.Matches.Count);
        Assert.All(
            view.Matches,
            row =>
            {
                Assert.Equal(PackageId.ToLowerInvariant(), row.Package);
                Assert.Equal(Version, row.Version);
                Assert.Equal("ILInspector.Analysis.Fixtures.dll", row.Assembly);
                Assert.StartsWith("0x", row.Method, StringComparison.Ordinal);
                Assert.StartsWith("IL_", row.Offset, StringComparison.Ordinal);
                Assert.Contains(RepeatedMarker, row.Literal, StringComparison.Ordinal);
            });
        Assert.Equal(
            [.. matched.Evidence.Occurrences.Select(
                occurrence => $"0x{occurrence.Address.MethodDefinitionToken:X8}")],
            view.Matches.Select(row => row.Method));

        PackageAssemblyCandidateRow candidate = Assert.Single(view.Candidates!);
        Assert.Equal("matched", candidate.Outcome);
        Assert.Equal(AssetPath, candidate.Asset);
        Assert.Equal(Framework, candidate.TargetFramework);
        Assert.Contains("sibling assemblies not evaluated", candidate.Detail, StringComparison.Ordinal);

        // The rendered token is the owner's own value, not a rebuilt one.
        Assert.True(
            PackageRootReacquisitionRequest.TryDecode(candidate.Root, out var decoded));
        Assert.Equal(outcome.Subject.RootRequest, decoded);
        Assert.Equal(1, view.MatchedCandidateCount);
        Assert.Equal(0, view.FailureCount);
    }

    [Fact]
    public async Task SemanticMiss_StaysDistinctFromAMatchAndKeepsItsRootToken()
    {
        (PackageAssemblyQueryView view, PackageAssemblyEvaluationOutcome outcome) =
            await RunAsync("no-fixture-declares-this-literal");

        Assert.IsType<PackageAssemblyEvaluationOutcome.NoMatch>(outcome);
        Assert.Null(view.Matches);
        PackageAssemblyCandidateRow candidate = Assert.Single(view.Candidates!);
        Assert.Equal("no-match", candidate.Outcome);
        Assert.Equal(AssetPath, candidate.Asset);
        Assert.Contains(
            "no matching decoded ldstr use",
            candidate.Detail,
            StringComparison.Ordinal);
        Assert.True(
            PackageRootReacquisitionRequest.TryDecode(candidate.Root, out var decoded));
        Assert.Equal(outcome.Subject.RootRequest, decoded);
        Assert.Equal(0, view.FailureCount);
        Assert.Equal(1, view.SemanticMissCount);
    }

    [Fact]
    public async Task NotApplicableCandidate_KeepsItsOwnOutcomeRatherThanReadingAsAMiss()
    {
        (PackageAssemblyQueryView view, PackageAssemblyEvaluationOutcome outcome) =
            await RunAsync(
                RepeatedMarker,
                entries: [($"{PackageId}.nuspec", "<package />"u8.ToArray())]);

        var notApplicable =
            Assert.IsType<PackageAssemblyEvaluationOutcome.NotApplicable>(outcome);
        Assert.Equal(
            PackageAssemblyNotApplicableReason.NoCompileAssets,
            notApplicable.Reason);
        Assert.Null(view.Matches);
        PackageAssemblyCandidateRow candidate = Assert.Single(view.Candidates!);
        Assert.Equal("not-applicable", candidate.Outcome);
        Assert.Equal("No compile assembly is available.", candidate.Detail);
        Assert.Equal(1, view.NotApplicableCount);
        Assert.Equal(0, view.SemanticMissCount);
        Assert.StartsWith(
            "No matching decoded string literal uses were reported.",
            view.Description,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task EvaluationFailure_DoesNotClaimSemanticAbsence()
    {
        (PackageAssemblyQueryView view, PackageAssemblyEvaluationOutcome outcome) =
            await RunAsync(
                RepeatedMarker,
                entries: [(AssetPath, "not an assembly"u8.ToArray())]);

        Assert.IsType<PackageAssemblyEvaluationOutcome.Failure>(outcome);
        Assert.Equal("failed", Assert.Single(view.Candidates!).Outcome);
        Assert.Equal(1, view.FailureCount);
        Assert.Equal(0, view.SemanticMissCount);
        Assert.StartsWith(
            "No matching decoded string literal uses were reported.",
            view.Description,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Operand_IsARawOrdinalSubstringRatherThanAPattern()
    {
        (PackageAssemblyQueryView exact, _) = await RunAsync("Ordinal-Case-Marker");
        Assert.NotNull(exact.Matches);

        (PackageAssemblyQueryView folded, _) = await RunAsync("ordinal-case-marker");
        Assert.Null(folded.Matches);

        (PackageAssemblyQueryView glob, _) = await RunAsync("Ordinal-*-Marker");
        Assert.Null(glob.Matches);

        // Precomposed and decomposed spellings are different character
        // sequences, so an ordinal substring separates them.
        (PackageAssemblyQueryView precomposed, _) =
            await RunAsync("caf\u00E9-literal-marker");
        PackageAssemblyLiteralUseRow onlyPrecomposed =
            Assert.Single(precomposed.Matches!);
        Assert.DoesNotContain(
            "cafe\u0301",
            onlyPrecomposed.Literal,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task NonGraphicLiteralContent_IsRenderedInertlyAndLosslessly()
    {
        (PackageAssemblyQueryView view, _) = await RunAsync("nul-literal-marker");

        PackageAssemblyLiteralUseRow row = Assert.Single(view.Matches!);
        Assert.DoesNotContain('\0', row.Literal);
        Assert.Contains("embedded", row.Literal, StringComparison.Ordinal);
        Assert.Contains("nul-literal-marker", row.Literal, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExpandedOutput_LeadsWithMatchesAndKeepsTheRootTokenUsable()
    {
        (PackageAssemblyQueryView view, PackageAssemblyEvaluationOutcome outcome) =
            await RunAsync(RepeatedMarker);
        string token = outcome.Subject.RootRequest.Encode();

        var markdown = await ConsoleCapture.RunAsync(() =>
        {
            FindCommand.WriteAssemblyQueryOutput(
                view, new FindOptions { Verbosity = Verbosity.Normal });
            return Task.FromResult(0);
        });
        int matchesHeading = markdown.Output.IndexOf(
            "## Matches",
            StringComparison.Ordinal);
        int candidatesHeading = markdown.Output.IndexOf(
            "## Candidates",
            StringComparison.Ordinal);
        Assert.True(matchesHeading >= 0);
        Assert.True(candidatesHeading > matchesHeading);
        Assert.Contains(
            PackageAssemblyQuerySections.Scope,
            markdown.Output,
            StringComparison.Ordinal);
        Assert.Contains(token, markdown.Output, StringComparison.Ordinal);

        var json = await ConsoleCapture.RunAsync(() =>
        {
            FindCommand.WriteAssemblyQueryOutput(
                view,
                new FindOptions
                {
                    Verbosity = Verbosity.Normal,
                    JsonOutput = true,
                    CompactJson = true,
                });
            return Task.FromResult(0);
        });
        Assert.Contains(token, json.Output, StringComparison.Ordinal);
        Assert.Contains("\"matches\"", json.Output, StringComparison.Ordinal);
        Assert.Contains("\"candidates\"", json.Output, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(Verbosity.Quiet, false, false)]
    [InlineData(Verbosity.Minimal, false, true)]
    [InlineData(Verbosity.Normal, true, true)]
    [InlineData(Verbosity.Detailed, true, true)]
    public async Task RenderedOutput_AppliesDisclosureAcrossFormats(
        Verbosity verbosity,
        bool showMatches,
        bool showCandidates)
    {
        (PackageAssemblyQueryView view, PackageAssemblyEvaluationOutcome outcome) =
            await RunAsync(RepeatedMarker);
        Assert.Equal(Verbosity.Minimal, new FindOptions().Verbosity);

        (bool Json, bool Tsv, bool Jsonl, string MatchMarker, string CandidateMarker)[] formats =
        [
            (false, false, false, "## Matches", "## Candidates"),
            (true, false, false, "\"matches\"", "\"candidates\""),
            (false, true, false, "\tliteral", "\toutcome"),
            (false, false, true, "\"literal\"", "\"outcome\""),
        ];
        foreach (var format in formats)
        {
            var captured = await ConsoleCapture.RunAsync(() =>
            {
                FindCommand.WriteAssemblyQueryOutput(
                    view,
                    new FindOptions
                    {
                        Verbosity = verbosity,
                        JsonOutput = format.Json,
                        Tsv = format.Tsv,
                        Jsonl = format.Jsonl,
                        Tabular = format.Tsv || format.Jsonl,
                    });
                return Task.FromResult(0);
            });

            Assert.True(
                showMatches == captured.Output.Contains(format.MatchMarker, StringComparison.Ordinal),
                $"Unexpected match rows for {format}: {captured.Output}");
            Assert.True(
                showCandidates == captured.Output.Contains(format.CandidateMarker, StringComparison.Ordinal),
                $"Unexpected candidate rows for {format}: {captured.Output}");
            if (showCandidates)
                Assert.Contains(outcome.Subject.RootRequest.Encode(), captured.Output, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData(RepeatedMarker, 2)]
    [InlineData("no-fixture-declares-this-literal", 0)]
    public async Task Count_ReportsMatchingUsesForACompleteQuery(string operand, int expected)
    {
        (PackageAssemblyQueryView view, _) = await RunAsync(operand);
        var captured = await ConsoleCapture.RunAsync(() =>
        {
            FindCommand.WriteAssemblyQueryOutput(view, new FindOptions { Count = true });
            return Task.FromResult(0);
        });

        Assert.Equal($"{expected}", captured.Output.Trim());
    }

    [Fact]
    public void StreamWithoutACompletionSummary_IsRefusedRatherThanRenderedEmpty()
    {
        PackageAssemblyQueryPlan plan = Plan(RepeatedMarker);

        var failure = Assert.Throws<InvalidOperationException>(
            () => PackageAssemblyQuerySections.CreateDocument(plan, []));

        Assert.Contains("completion summary", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AcquisitionFailure_IsVisibleAndCarriesNoRootToken()
    {
        PackageAssemblyQueryPlan plan = Plan(RepeatedMarker);
        var failure = new PackageAssemblyQueryAcquisitionFailure(
            PackageSourceCoordinate.Create(PackageId, Version),
            new InertText.InertString(InertText.TextPolicy.Field, "fixture-producer"),
            new InertText.InertString(InertText.TextPolicy.Prose, "the fixture source refused"),
            PackageSourceFailureKind.NotFound);

        PackageAssemblyQueryView view = PackageAssemblyQuerySections.CreateDocument(
            plan,
            [
                new PackageAssemblyQueryEvent.AcquisitionFailed(failure),
                new PackageAssemblyQueryEvent.Completed(new(1, 0, 0, 0, 1)),
            ]);

        PackageAssemblyCandidateRow row = Assert.Single(view.Candidates!);
        Assert.Equal("failed", row.Outcome);
        Assert.Equal("", row.Root);
        Assert.Contains("fixture-producer", row.Detail, StringComparison.Ordinal);
        Assert.Contains(
            PackageSourceFailureKind.NotFound.ToString(),
            row.Detail,
            StringComparison.Ordinal);
        Assert.Contains("the fixture source refused", row.Detail, StringComparison.Ordinal);
        Assert.Equal(1, view.FailureCount);
        Assert.StartsWith(
            "No matching decoded string literal uses were reported.",
            view.Description,
            StringComparison.Ordinal);
        Assert.Throws<InvalidOperationException>(() =>
            FindCommand.WriteAssemblyQueryOutput(view, new FindOptions { Count = true }));
    }

    [Fact]
    public async Task Literal_RejectsSourceOverridesRatherThanIgnoringThem()
    {
        var captured = await ConsoleCapture.RunAsync(
            () => FindCommand.ExecuteAsync(
                new FindOptions
                {
                    Literal = RepeatedMarker,
                    Packages = [$"{PackageId}@{Version}"],
                    Tfm = Framework,
                    SourceOptions = new NuGetSourceOptions
                    {
                        Sources = ["https://private.invalid/v3/index.json"],
                    },
                },
                TestContext.Current.CancellationToken));

        Assert.Equal(1, captured.ExitCode);
        Assert.Empty(captured.Output);
        Assert.Contains(
            "cannot be combined with source overrides",
            captured.Error,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Literal_RequiresAnExplicitTargetFrameworkBeforeAcquiring()
    {
        var captured = await ConsoleCapture.RunAsync(
            () => FindCommand.ExecuteAsync(
                new FindOptions
                {
                    Literal = RepeatedMarker,
                    Packages = [$"{PackageId}@{Version}"],
                },
                TestContext.Current.CancellationToken));

        Assert.Equal(1, captured.ExitCode);
        Assert.Empty(captured.Output);
        Assert.Contains("--tfm", captured.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Literal_RejectsApiSearchScopesBeforeAcquiring()
    {
        var captured = await ConsoleCapture.RunAsync(
            () => FindCommand.ExecuteAsync(
                new FindOptions
                {
                    Literal = RepeatedMarker,
                    Packages = [$"{PackageId}@{Version}"],
                    Tfm = Framework,
                    PlatformFrameworks = ["runtime"],
                },
                TestContext.Current.CancellationToken));

        Assert.Equal(1, captured.ExitCode);
        Assert.Empty(captured.Output);
        Assert.Contains(
            "searches only explicit ID@VERSION packages",
            captured.Error,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Literal_RejectsAnInexactPackageCoordinateBeforeAcquiring()
    {
        var captured = await ConsoleCapture.RunAsync(
            () => FindCommand.ExecuteAsync(
                new FindOptions
                {
                    Literal = RepeatedMarker,
                    Packages = [PackageId],
                    Tfm = Framework,
                },
                TestContext.Current.CancellationToken));

        Assert.Equal(1, captured.ExitCode);
        Assert.Empty(captured.Output);
        Assert.Contains(
            "exact ID@VERSION coordinate",
            captured.Error,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Literal_DiscoveryDescribesTheLiteralSectionsRatherThanTypeSearchFields()
    {
        var captured = await ConsoleCapture.RunAsync(
            () => FindCommand.ExecuteAsync(
                new FindOptions
                {
                    Literal = RepeatedMarker,
                    Packages = [$"{PackageId}@{Version}"],
                    Tfm = Framework,
                    Discover = [],
                },
                TestContext.Current.CancellationToken));

        Assert.Equal(0, captured.ExitCode);
        Assert.Contains(
            PackageAssemblyQuerySections.Matches,
            captured.Output,
            StringComparison.Ordinal);
        Assert.Contains(
            PackageAssemblyQuerySections.Candidates,
            captured.Output,
            StringComparison.Ordinal);
        Assert.DoesNotContain("Namespace", captured.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("Sim", captured.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExecutionPlanViolation_ReportsTheAuthoredSentenceOnly()
    {
        // Planning rejects the selection before any acquisition, so this exercises the execution
        // fallback the parse validator normally shadows.
        var options = new FindOptions
        {
            Literal = "literal",
            Packages = [$"{PackageId}@{Version}", $"{PackageId}@{Version}"],
            Tfm = Framework,
        };

        var (exit, _, error) = await ConsoleCapture.RunAsync(
            () => FindCommand.ExecuteAsync(options));

        Assert.Equal(1, exit);
        Assert.Contains(
            "An assembly query cannot contain duplicate package coordinates.",
            error,
            StringComparison.Ordinal);
        Assert.DoesNotContain("packageCoordinates", error, StringComparison.Ordinal);
        Assert.DoesNotContain("Arg_ParamName_Name", error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExecutionWithoutATargetFramework_ReportsTheCliRequirement()
    {
        var options = new FindOptions
        {
            Literal = "literal",
            Packages = [$"{PackageId}@{Version}"],
        };

        var (exit, _, error) = await ConsoleCapture.RunAsync(
            () => FindCommand.ExecuteAsync(options));

        Assert.Equal(1, exit);
        Assert.Contains(
            "--literal requires an explicit --tfm (for example --tfm net10.0).",
            error,
            StringComparison.Ordinal);
        Assert.DoesNotContain("targetFramework", error, StringComparison.Ordinal);
    }

    static PackageAssemblyQueryPlan Plan(string operand) =>
        PackageAssemblyQuery.Plan(
            PackageAssemblyPatterns.StringLiteralContains,
            operand,
            [$"{PackageId}@{Version}"],
            Framework);

    static async Task<(PackageAssemblyQueryView View, PackageAssemblyEvaluationOutcome Outcome)>
        RunAsync(
            string operand,
            (string Path, byte[] Bytes)[]? entries = null)
    {
        PackageAssemblyQueryPlan plan = Plan(operand);
        byte[] image = await File.ReadAllBytesAsync(
            FixtureCatalog.AnalysisStringLiterals.AssemblyPath(),
            TestContext.Current.CancellationToken);
        byte[] archive = SnupkgPdbReaderTests.MakeSnupkg(
            entries
            ??
            [
                ($"{PackageId}.nuspec", "<package />"u8.ToArray()),
                (AssetPath, image),
            ]);
        var content = new InMemoryPackageContent(archive, false, "fixture-producer");
        PackageRootBinding binding = PackageRootBinding.CreateFromSource(
            new AcquiredPackageSourcePayload(
                PackageSourceCoordinate.Create(PackageId, Version),
                content,
                content.ProducerKey,
                PackagePayloadOrigin.Download),
            Framework);
        PackageAssemblyEvaluationOutcome outcome =
            await PackageAssemblyEvaluator.EvaluateAsync(
                binding,
                plan.Pattern,
                plan.Budget,
                TestContext.Current.CancellationToken);
        PackageAssemblyQueryView view = PackageAssemblyQuerySections.CreateDocument(
            plan,
            [
                new PackageAssemblyQueryEvent.Evaluated(outcome),
                new PackageAssemblyQueryEvent.Completed(
                    new(
                        1,
                        outcome is PackageAssemblyEvaluationOutcome.Matched ? 1 : 0,
                        outcome is PackageAssemblyEvaluationOutcome.NoMatch ? 1 : 0,
                        outcome is PackageAssemblyEvaluationOutcome.NotApplicable ? 1 : 0,
                        outcome is PackageAssemblyEvaluationOutcome.Failure ? 1 : 0)),
            ]);
        return (view, outcome);
    }
}
