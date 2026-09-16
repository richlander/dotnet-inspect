using DotnetInspect.Cli.Commands;
using DotnetInspect.Cli.Options;
using DotnetInspector.PackageQueries;
using DotnetInspector.Packages;
using DotnetInspector.Queries;
using DotnetInspector.Sections;
using DotnetInspector.SourceSelection;
using DotnetInspect.Cli.Sections;
using DotnetInspect.Cli.Views;
using DotnetInspector.Fixtures;
using NuGetFetch;

namespace DotnetInspect.Cli.Tests;

/// <summary>
/// Covers how the CLI projects the shared assembly-semantic Find Document,
/// using real producer evidence from locally built fixture packages rather
/// than a network source or fabricated semantic outcomes.
/// </summary>
[Collection("Console")]
public sealed class PackageAssemblyQueryOutputTests
{
    const string PackageId = "Literal.Query.Fixture";
    const string Version = "1.0.0";
    const string Framework = "net11.0";
    const string AssetPath = $"lib/{Framework}/ILInspector.Analysis.Fixtures.dll";
    const string RepeatedMarker = "shared-literal-use-marker";

    static byte[] MatchImage =>
        File.ReadAllBytes(
            FixtureCatalog.AnalysisStringLiterals.AssemblyPath());

    static byte[] NoMatchImage =>
        File.ReadAllBytes(
            typeof(PackageAssemblySemanticFindDocument)
                .Assembly.Location);

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
    public async Task CompletedDocument_IsTheProjectionAndCountAuthority()
    {
        (PackageAssemblyQueryView view, _) =
            await RunAsync(RepeatedMarker);

        Assert.True(view.IsComplete);
        Assert.Equal(2, view.Matches!.Count);
        Assert.Equal(1, view.CandidateCount);
    }

    [Fact]
    public async Task BoundedPrefixProjection_KeepsAllCandidatesAfterOccurrenceHead()
    {
        string[] packageIds =
        [
            "Contoso.Miss",
            "Contoso.Match.First",
            "Contoso.NotApplicable",
            "Contoso.Failed",
            "Contoso.Match.Later",
        ];
        await using var fixture = new SemanticFindSourceFixture();
        fixture.ConfigurePrefix(packageIds);
        await fixture.CachePackageAsync(
            packageIds[0],
            ($"lib/{Framework}/{packageIds[0]}.dll", NoMatchImage));
        await fixture.CachePackageAsync(
            packageIds[1],
            ($"lib/{Framework}/{packageIds[1]}.dll", MatchImage));
        await fixture.CachePackageAsync(packageIds[2]);
        await fixture.CachePackageAsync(
            packageIds[4],
            ($"lib/{Framework}/{packageIds[4]}.dll", MatchImage));

        PackageSourceOperationLease operation =
            fixture.Root.IssueOperationLease(
                TestContext.Current.CancellationToken,
                operationTimeout:
                    PackageAssemblySemanticFindBudget.Default.MaximumDuration);
        PackageAcquisitionPopulation population =
            await PackageAcquisitionPopulationResolver
                .ResolveGalleryPrefixAsync(
                    operation,
                    new PackagePrefixDeclaration("Contoso."),
                    maximumCandidates: 5,
                    fixture.Authorization);
        var request = new PackageAssemblySemanticFindRequest(
            population,
            PackageHouseTargetContext.Exact(Framework),
            PackageAssemblyPatterns.CreateRequest(
                PackageAssemblyPatterns.StringLiteralContains,
                RepeatedMarker));
        InspectionEnvelope<PackageAssemblySemanticFindDocument> envelope =
            await PackageAssemblySemanticFindInspection.ExecuteAsync(
                request,
                operation,
                fixture.PayloadAcquisition,
                TestContext.Current.CancellationToken);
        PackageAssemblySemanticFindDocument document =
            envelope.Content;

        PackageAssemblyQueryView view =
            PackageAssemblyQuerySections.CreateDocument(
                request,
                document,
                [document.Results[0]]);

        Assert.Equal(
            PackageAcquisitionPopulationCompletionKind.CandidateLimitReached,
            document.Population.Completion);
        Assert.Equal(5, document.CandidateCount);
        Assert.Equal(2, document.MatchedCandidateCount);
        Assert.Equal(4, document.OccurrenceCount);
        Assert.Single(view.Matches!);
        Assert.Equal(
            [
                "no-match",
                "matched",
                "not-applicable",
                "failed",
                "matched",
            ],
            view.Candidates!.Select(row => row.Outcome));
        Assert.Equal("", view.Candidates![3].Root);
        Assert.NotEqual("", view.Candidates[4].Root);
        Assert.Equal(1, view.FailureCount);
        Assert.False(view.IsComplete);
        Assert.NotNull(view.Description);
        Assert.StartsWith(
            "Showing 1 of 4 decoded string literal uses; "
            + "2 of 5 package candidates matched.",
            view.Description,
            StringComparison.Ordinal);
        Assert.Contains(
            "wider prefix was not exhausted",
            view.Description,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task PopulationFailure_IsSeparateAndMakesCountUnavailable()
    {
        string[] packageIds =
        [
            "Contoso.Available",
            "Contoso.Version.Failed",
        ];
        await using var fixture = new SemanticFindSourceFixture();
        fixture.ConfigurePrefix(packageIds);
        fixture.SourceClient.VersionFailures[packageIds[1]] =
            PackageSourceFailureKind.Transport;
        await fixture.CachePackageAsync(
            packageIds[0],
            ($"lib/{Framework}/{packageIds[0]}.dll", NoMatchImage));

        PackageSourceOperationLease operation =
            fixture.Root.IssueOperationLease(
                TestContext.Current.CancellationToken,
                operationTimeout:
                    PackageAssemblySemanticFindBudget.Default.MaximumDuration);
        PackageAcquisitionPopulation population =
            await PackageAcquisitionPopulationResolver
                .ResolveGalleryPrefixAsync(
                    operation,
                    new PackagePrefixDeclaration("Contoso."),
                    maximumCandidates: 2,
                    fixture.Authorization);
        var request = new PackageAssemblySemanticFindRequest(
            population,
            PackageHouseTargetContext.Exact(Framework),
            PackageAssemblyPatterns.CreateRequest(
                PackageAssemblyPatterns.StringLiteralContains,
                RepeatedMarker));
        InspectionEnvelope<PackageAssemblySemanticFindDocument> envelope =
            await PackageAssemblySemanticFindInspection.ExecuteAsync(
                request,
                operation,
                fixture.PayloadAcquisition,
                TestContext.Current.CancellationToken);
        PackageAssemblyQueryView view =
            PackageAssemblyQuerySections.CreateDocument(
                request,
                envelope.Content);

        PackageAssemblyPopulationFailureRow failure =
            Assert.Single(view.PopulationFailures!);
        Assert.Equal("2", failure.Candidate);
        Assert.Equal(
            "contoso.version.failed",
            failure.Package);
        Assert.Equal(
            PackageAuthorityFailureKind.Transport.ToString(),
            failure.Kind);
        Assert.False(view.IsComplete);
        Assert.Throws<InvalidOperationException>(() =>
            FindCommand.WriteAssemblyQueryOutput(
                view,
                new FindOptions { Count = true }));
    }

    [Fact]
    public async Task CompleteBoundedPrefix_AllowsSelectedOccurrenceCount()
    {
        string[] packageIds =
        [
            "Contoso.First",
            "Contoso.Beyond.Bound",
        ];
        await using var fixture = new SemanticFindSourceFixture();
        fixture.ConfigurePrefix(packageIds);
        await fixture.CachePackageAsync(
            packageIds[0],
            ($"lib/{Framework}/{packageIds[0]}.dll", MatchImage));

        PackageSourceOperationLease operation =
            fixture.Root.IssueOperationLease(
                TestContext.Current.CancellationToken,
                operationTimeout:
                    PackageAssemblySemanticFindBudget.Default.MaximumDuration);
        PackageAcquisitionPopulation population =
            await PackageAcquisitionPopulationResolver
                .ResolveGalleryPrefixAsync(
                    operation,
                    new PackagePrefixDeclaration("Contoso."),
                    maximumCandidates: 1,
                    fixture.Authorization);
        var request = new PackageAssemblySemanticFindRequest(
            population,
            PackageHouseTargetContext.Exact(Framework),
            PackageAssemblyPatterns.CreateRequest(
                PackageAssemblyPatterns.StringLiteralContains,
                RepeatedMarker));
        InspectionEnvelope<PackageAssemblySemanticFindDocument> envelope =
            await PackageAssemblySemanticFindInspection.ExecuteAsync(
                request,
                operation,
                fixture.PayloadAcquisition,
                TestContext.Current.CancellationToken);
        PackageAssemblyQueryView view =
            PackageAssemblyQuerySections.CreateDocument(
                request,
                envelope.Content,
                [envelope.Content.Results[0]]);

        Assert.True(view.IsComplete);
        Assert.Equal(
            PackageAcquisitionPopulationCompletionKind.CandidateLimitReached,
            envelope.Content.Population.Completion);
        var captured = await ConsoleCapture.RunAsync(() =>
        {
            FindCommand.WriteAssemblyQueryOutput(
                view,
                new FindOptions { Count = true });
            return Task.FromResult(0);
        });
        Assert.Equal("1", captured.Output.Trim());
    }

    [Fact]
    public async Task AcquisitionFailure_IsVisibleAndCarriesNoRootToken()
    {
        (PackageAssemblySemanticFindRequest request,
            PackageAssemblySemanticFindDocument document) =
            await RunDocumentAsync(
                RepeatedMarker,
                entries: null,
                cachePayload: false);
        PackageAssemblyQueryView view =
            PackageAssemblyQuerySections.CreateDocument(
                request,
                document);

        PackageAssemblyCandidateRow row = Assert.Single(view.Candidates!);
        Assert.Equal("failed", row.Outcome);
        Assert.Equal("", row.Root);
        Assert.Contains(
            "package payload was not found",
            row.Detail,
            StringComparison.Ordinal);
        Assert.Equal(1, view.FailureCount);
        Assert.False(view.IsComplete);
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
            "searches only explicit ID@VERSION packages or one bounded package prefix",
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
        Assert.Contains(
            PackageAssemblyQuerySections.PopulationFailures,
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

    static async Task<(PackageAssemblyQueryView View, PackageAssemblyEvaluationOutcome Outcome)>
        RunAsync(
            string operand,
            (string Path, byte[] Bytes)[]? entries = null)
    {
        (PackageAssemblySemanticFindRequest request,
            PackageAssemblySemanticFindDocument document) =
            await RunDocumentAsync(
                operand,
                entries
                ??
                [
                    ($"{PackageId}.nuspec", "<package />"u8.ToArray()),
                    (AssetPath, MatchImage),
                ],
                cachePayload: true);
        PackageAssemblyEvaluationOutcome outcome =
            Assert.Single(document.CandidateOutcomes) switch
            {
                PackageAssemblySemanticFindCandidateOutcome.Matched matched =>
                    matched.Evaluation,
                PackageAssemblySemanticFindCandidateOutcome.NoMatch noMatch =>
                    noMatch.Evaluation,
                PackageAssemblySemanticFindCandidateOutcome.NotApplicable
                    notApplicable =>
                    notApplicable.Evaluation,
                PackageAssemblySemanticFindCandidateOutcome.Failure
                    {
                        Reason:
                            PackageAssemblySemanticFindFailureReason.Evaluation
                            failure,
                    } =>
                    failure.Evidence,
                _ => throw new InvalidOperationException(
                    "The fixture expected an evaluated candidate."),
            };
        PackageAssemblyQueryView view =
            PackageAssemblyQuerySections.CreateDocument(
                request,
                document);
        return (view, outcome);
    }

    static async Task<(
        PackageAssemblySemanticFindRequest Request,
        PackageAssemblySemanticFindDocument Document)> RunDocumentAsync(
        string operand,
        (string Path, byte[] Bytes)[]? entries,
        bool cachePayload)
    {
        await using var fixture = new SemanticFindSourceFixture();
        if (cachePayload)
            await fixture.CacheAsync(entries!);

        PackageSourceOperationLease operation =
            fixture.Root.IssueOperationLease(
                TestContext.Current.CancellationToken,
                operationTimeout:
                    PackageAssemblySemanticFindBudget.Default.MaximumDuration);
        PackageAcquisitionPopulation population =
            await operation.ResolvePinnedPopulationAsync(
                new FixedAuthorization(fixture.Authorization),
                [
                    PackageSourceCoordinate.Create(
                        PackageId,
                        Version),
                ]);
        var request = new PackageAssemblySemanticFindRequest(
            population,
            PackageHouseTargetContext.Exact(Framework),
            PackageAssemblyPatterns.CreateRequest(
                PackageAssemblyPatterns.StringLiteralContains,
                operand));
        InspectionEnvelope<PackageAssemblySemanticFindDocument> envelope =
            await PackageAssemblySemanticFindInspection.ExecuteAsync(
                request,
                operation,
                fixture.PayloadAcquisition,
                TestContext.Current.CancellationToken);
        return (request, envelope.Content);
    }

    private sealed class FixedAuthorization(
        PackageSourceAuthorization authorization)
        : IPackageSourceAuthorization
    {
        public PackageSourceAuthorization AuthorizeSourcesFor(
            string packageId) =>
            authorization;
    }

    private sealed class SemanticFindSourceFixture : IAsyncDisposable
    {
        internal PackageSourceAuthorization Authorization { get; } =
            PackageSourceAuthorization.Authorize(
                [PackageSource.NuGetOrg]);

        internal FixtureSourceClient SourceClient { get; }

        internal IPackageSourceClient Client { get; }

        internal PackageSourceSettlementLease Root { get; }

        internal InMemoryPackageStore Store { get; } = new();

        internal PackagePayloadAcquisitionPlan PayloadAcquisition
            { get; }

        internal SemanticFindSourceFixture()
        {
            FixtureSourceClient? client = null;
            Client = PackageSourceClientFactory.CreateCustom(
                PackageSourceDescriptor.NuGetGallery,
                Authorization.Authorities[0].Association,
                factory => client = new FixtureSourceClient(factory));
            SourceClient = client!;
            Root = PackageSourceSettlementService.IssueLease(
                authority =>
                {
                    Assert.Same(
                        Authorization.Authorities[0],
                        authority);
                    return Client;
                });
            PayloadAcquisition =
                new PackagePayloadAcquisitionPlan(
                    (_, _) => Store);
        }

        internal async Task CacheAsync(
            (string Path, byte[] Bytes)[] entries)
        {
            await CachePackageAsync(PackageId, entries);
        }

        internal void ConfigurePrefix(
            IReadOnlyList<string> packageIds)
        {
            SourceClient.SearchResults =
            [
                .. packageIds.Select(
                    packageId =>
                        new SearchResult(packageId, Version)),
            ];
            SourceClient.SearchTruncation =
                PackageSearchTruncationReason.RequestedLimit;
            foreach (string packageId in packageIds)
                SourceClient.Versions[packageId] = [Version];
        }

        internal async Task CachePackageAsync(
            string packageId,
            params (string Path, byte[] Bytes)[] entries)
        {
            (string Path, byte[] Bytes)[] archiveEntries =
                entries.Any(entry =>
                    entry.Path.EndsWith(
                        ".nuspec",
                        StringComparison.OrdinalIgnoreCase))
                    ? entries
                    :
                    [
                        ($"{packageId}.nuspec", "<package />"u8.ToArray()),
                        .. entries,
                    ];
            byte[] archive =
                SnupkgPdbReaderTests.MakeSnupkg(archiveEntries);
            await Store.CommitAsync(
                packageId,
                Version,
                Client.Source.Producer.Key,
                new MemoryStream(
                    archive,
                    writable: false),
                TestContext.Current.CancellationToken);
        }

        public async ValueTask DisposeAsync()
        {
            await Root.DisposeAsync();
            Client.Dispose();
        }
    }

    private sealed class FixtureSourceClient(
        PackageSourceResultFactory results)
        : IPackageSourceClient
    {
        internal IReadOnlyList<SearchResult> SearchResults { get; set; } =
            [];

        internal PackageSearchTruncationReason SearchTruncation
            { get; set; }

        internal Dictionary<string, IReadOnlyList<string>> Versions
            { get; } =
            new(StringComparer.OrdinalIgnoreCase);

        internal Dictionary<string, PackageSourceFailureKind>
            VersionFailures { get; } =
            new(StringComparer.OrdinalIgnoreCase);

        public PackageSourceResultIdentity Source => results.Source;

        public PackageSourceCapabilities Capabilities =>
            PackageSourceCapabilities.Search
            | PackageSourceCapabilities.VersionEnumeration
            | PackageSourceCapabilities.PackagePayload;

        public Task<PackageSourceOperationResult<PackageSourceManifest>>
            GetManifestAsync(
                string packageId,
                string version,
                CancellationToken cancellationToken = default,
                NuGetOperationContext? operationContext = null) =>
            throw new NotSupportedException();

        public Task<PackageSourceOperationResult<PackageVersionResult>>
            GetVersionsAsync(
                string packageId,
                CancellationToken cancellationToken = default,
                NuGetOperationContext? operationContext = null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            operationContext?.ThrowIfExpired();
            if (VersionFailures.TryGetValue(
                    packageId,
                    out PackageSourceFailureKind failure))
            {
                return Task.FromResult(
                    results.FailedVersions(failure));
            }
            IReadOnlyList<string> versions =
                Versions.TryGetValue(
                    packageId,
                    out IReadOnlyList<string>? configured)
                    ? configured
                    : [];
            return Task.FromResult(
                results.SucceededVersions(
                    results.Versions(
                        [
                            .. versions.Select(version =>
                                results.Candidate(
                                    PackageSourceCoordinate.Create(
                                        packageId,
                                        version),
                                    PackageDiscoveryContract
                                        .CompleteVersionEnumeration,
                                    PackageListingState.Listed)),
                        ],
                        hasAuthoritativeListingState: true)));
        }

        public Task<PackageSourceOperationResult<PackageSearchResult>>
            SearchAsync(
                string query,
                int take = 20,
                bool prerelease = false,
                CancellationToken cancellationToken = default,
                NuGetOperationContext? operationContext = null) =>
            throw new NotSupportedException();

        public Task<PackageSourceOperationResult<PackageSearchResult>>
            SearchByPrefixAsync(
                string prefix,
                int take = 100,
                bool prerelease = false,
                CancellationToken cancellationToken = default,
                NuGetOperationContext? operationContext = null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            operationContext?.ThrowIfExpired();
            return Task.FromResult(
                results.SucceededSearch(
                    results.Search(
                        [.. SearchResults.Take(take)],
                        SearchTruncation)));
        }

        public async IAsyncEnumerable<
            PackageSourceOperationResult<PackageSearchResult>>
            SearchByPrefixPagesAsync(
                string prefix,
                int take = 100,
                bool prerelease = false,
                [System.Runtime.CompilerServices.EnumeratorCancellation]
                    CancellationToken cancellationToken = default,
                NuGetOperationContext? operationContext = null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            operationContext?.ThrowIfExpired();
            yield return results.SucceededSearch(
                results.Search(
                    [.. SearchResults.Take(take)],
                    SearchTruncation));
        }

        public Task<PackageSourceOperationResult<PackageSourcePayload>>
            GetPackageAsync(
                string packageId,
                string version,
                CancellationToken cancellationToken = default,
                NuGetOperationContext? operationContext = null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            operationContext?.ThrowIfExpired();
            return Task.FromResult(
                results.FailedPackage(
                    PackageSourceCoordinate.Create(
                        packageId,
                        version),
                    PackageSourceFailureKind.NotFound));
        }

        public Task<PackageSourceOperationResult<PackageSourcePayload>>
            TryGetSymbolsAsync(
                string packageId,
                string version,
                CancellationToken cancellationToken = default,
                NuGetOperationContext? operationContext = null) =>
            throw new NotSupportedException();

        public void Dispose()
        {
        }
    }
}
