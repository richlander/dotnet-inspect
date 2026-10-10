using System.Runtime.CompilerServices;
using DotnetInspector.Packages;
using DotnetInspector.Queries;
using DotnetInspector.SourceSelection;

namespace DotnetInspector.Queries.Tests;

public class EcosystemFindSearchSessionTests
{
    private static EcosystemFindLayer Layer(
        string id, string? core, params string[] prefixes) =>
        new(new WorkspaceEcosystemRegistrationDeclaration(
            WorkspaceEcosystemRegistrationId.Create($"ecosystem.{id}"),
            [],
            core is null ? [] : [new PackageCoordinate(core)],
            prefixes.Select(prefix =>
                (WorkspaceEcosystemPopulationDeclaration)new
                    WorkspaceEcosystemPopulationDeclaration.PackagePrefix(
                        new PackagePrefixDeclaration(prefix)))),
            Platform: false);

    private static EcosystemFindSearchRequest Request(
        EcosystemFindLayer[] layers, int? maximumRows = null,
        int maximumCandidates = 500) =>
        new(TypeFindQuestion.Create(["Thing"], FindVisibility.Public),
            layers,
            layers.Select(layer => layer.Registration.Id),
            maximumCandidates, maximumRows);

    private static async IAsyncEnumerable<EcosystemFindPrefixPage> Pages(
        EcosystemFindPrefixPage page,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        yield return page;
        await Task.CompletedTask;
    }

    private static async IAsyncEnumerable<EcosystemFindPrefixPage> Pages(
        EcosystemFindPrefixPage first,
        EcosystemFindPrefixPage second,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        yield return first;
        cancellationToken.ThrowIfCancellationRequested();
        yield return second;
        await Task.CompletedTask;
    }

    [Fact]
    public async Task BoundedBarrierIssuesExactContinuationWithoutEnumeratingPrefixes()
    {
        EcosystemFindLayer[] layers =
        [
            Layer("first", "First.Core", "Contoso."),
            Layer("second", "Second.Core", "Contoso.Special.", "Other."),
        ];
        List<string> observations = [];
        var request = Request(layers, 5);
        using var session = new EcosystemFindSearchSession<string>(
            request,
            (layer, _) =>
            {
                observations.Add(layer.Registration.Id.Value);
                return Task.FromResult(new EcosystemFindBlock<string>(
                    layer.Registration.Id.Value, 1));
            },
            (prefix, remaining, token) =>
            {
                observations.Add($"{prefix.Prefix}:{remaining}");
                return Pages(new(
                [
                    new("Contoso.Special.Widget", "1.0.0"),
                    new("Contoso.Special.Widget", "1.0.0"),
                    new("Contoso.Another", "1.0.0"),
                    new("First.Core", "1.0.0"),
                ]), token);
            },
            (candidate, membership, _) =>
            {
                observations.Add($"{candidate.PackageId}:"
                    + string.Join(",", membership.Select(id => id.Value)));
                return Task.FromResult(new EcosystemFindBlock<string>(
                    candidate.PackageId, 1));
            });
        var bounded = await session.RunBoundedAsync(TestContext.Current.CancellationToken);
        Assert.Null(bounded.Completed);
        Assert.NotNull(bounded.Continuation);
        Assert.Equal(request.Identity, bounded.Continuation.RequestIdentity);
        Assert.Equal(3, bounded.Continuation.MaximumRemainingRows);
        Assert.Equal(["ecosystem.first", "ecosystem.second"], observations);
        var completed = await bounded.Continuation.ResumeAsync(
            2, TestContext.Current.CancellationToken);
        Assert.Equal(EcosystemFindCompletion.RowLimitReached,
            completed.Completion);
        Assert.Equal(2, completed.BoundedBlocks.Count);
        Assert.Equal(2, completed.PrefixBlocks.Count);
        Assert.Contains(observations, value =>
            value.Contains("ecosystem.first,ecosystem.second",
                StringComparison.Ordinal));
        Assert.Equal(["Contoso.Special.", "Other."], completed.Unsearched);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            bounded.Continuation.ResumeAsync(
                1, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ZeroCapacityAndBoundedWindowDoNotStartPrefixWork()
    {
        var layer = Layer("first", "First.Core", "Contoso.");
        int enumerations = 0;
        EcosystemFindSearchSession<int> Create(int boundedRows) =>
            new(Request([layer], maximumRows: 1),
                (_, _) => Task.FromResult(new EcosystemFindBlock<int>(
                    boundedRows, boundedRows)),
                (_, _, token) =>
                {
                    enumerations++;
                    return Pages(new([]), token);
                },
                (_, _, _) => Task.FromResult(new EcosystemFindBlock<int>(0, 0)));
        using var full = Create(1);
        var stopped = await full.RunBoundedAsync(TestContext.Current.CancellationToken);
        Assert.Null(stopped.Continuation);
        Assert.Equal(EcosystemFindCompletion.RowLimitReached,
            stopped.Completed!.Completion);
        using var empty = Create(0);
        var bounded = await empty.RunBoundedAsync(TestContext.Current.CancellationToken);
        var finalized = await bounded.Continuation!.ResumeAsync(
            0, TestContext.Current.CancellationToken);
        Assert.Equal(EcosystemFindCompletion.RowLimitReached,
            finalized.Completion);
        Assert.Equal(["Contoso."], finalized.Unsearched);
        Assert.Equal(0, enumerations);
    }

    [Fact]
    public async Task ContinuationRejectsLargerWindowAndCancellation()
    {
        var layer = Layer("first", "First.Core", "Contoso.");
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken);
        using var session = new EcosystemFindSearchSession<int>(
            Request([layer], 3),
            (_, _) => Task.FromResult(new EcosystemFindBlock<int>(1, 1)),
            (_, _, token) => Pages(new([]), token),
            (_, _, _) => Task.FromResult(new EcosystemFindBlock<int>(0, 0)));
        var bounded = await session.RunBoundedAsync(cancellation.Token);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            bounded.Continuation!.ResumeAsync(3, cancellation.Token));
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            bounded.Continuation!.ResumeAsync(1, cancellation.Token));
    }

    [Fact]
    public async Task CandidateBoundIsSharedAcrossUnequalPrefixes()
    {
        var layer = Layer("first", "First.Core", "Contoso.", "Other.");
        List<string> searched = [];
        using var session = new EcosystemFindSearchSession<string>(
            Request([layer], maximumCandidates: 1),
            (_, _) => Task.FromResult(new EcosystemFindBlock<string>("core", 0)),
            (prefix, _, token) =>
            {
                searched.Add(prefix.Prefix);
                return Pages(new([new("Contoso.Widget", "1.0.0")]), token);
            },
            (candidate, _, _) =>
                Task.FromResult(new EcosystemFindBlock<string>(
                    candidate.PackageId, 0)));
        var result = await session.ExecuteAsync(TestContext.Current.CancellationToken);
        Assert.Equal(["Contoso."], searched);
        Assert.Equal(["Other."], result.Unsearched);
        Assert.Equal(EcosystemFindCompletion.CandidateLimitReached,
            result.Completion);
    }

    [Fact]
    public async Task RequestedPageLimitWithDuplicatesAndCoresIsNotCandidateExhaustion()
    {
        var layer = Layer("first", "Contoso.Core", "Contoso.", "Other.");
        List<string> searched = [];
        List<string> evaluated = [];
        using var session = new EcosystemFindSearchSession<string>(
            Request([layer], maximumCandidates: 3),
            (_, _) => Task.FromResult(new EcosystemFindBlock<string>("core", 0)),
            (prefix, remaining, token) =>
            {
                searched.Add($"{prefix.Prefix}:{remaining}");
                return prefix.Prefix == "Contoso."
                    ? Pages(
                        new([new("Contoso.Widget", "1")]),
                        new(
                        [
                            new("Contoso.Core", "1"),
                            new("Contoso.Widget", "1"),
                            new("Contoso.Widget", "1"),
                        ], EcosystemFindPrefixPageCompletion.RequestedLimit),
                        token)
                    : Pages(new([new("Other.Widget", "1")]), token);
            },
            (item, _, _) =>
            {
                evaluated.Add(item.PackageId);
                return Task.FromResult(new EcosystemFindBlock<string>(
                    item.PackageId, 0));
            });
        var result = await session.ExecuteAsync(TestContext.Current.CancellationToken);
        Assert.Equal(["Contoso.:3", "Other.:2"], searched);
        Assert.Equal(["Contoso.Widget", "Other.Widget"], evaluated);
        Assert.Equal(EcosystemFindCompletion.Partial,
            result.Completion);
        Assert.True(result.Incomplete);
        Assert.Empty(result.Unsearched);
        Assert.Equal(
            [EcosystemFindPrefixPageCompletion.RequestedLimit,
             EcosystemFindPrefixPageCompletion.Exhausted],
            result.PrefixCoverage.Select(coverage => coverage.Completion));
    }

    [Fact]
    public async Task CoreOfFirstEcosystemRetainsDemandedPrefixMembershipOfSecond()
    {
        EcosystemFindLayer[] layers =
        [
            Layer("first", "Contoso.Core"),
            Layer("second", "Other.Core", "Contoso."),
        ];
        List<string> evaluatedCores = [];
        List<string> evaluatedPrefixes = [];
        using var session = new EcosystemFindSearchSession<string>(
            Request(layers),
            (layer, _) =>
            {
                evaluatedCores.Add(layer.CorePackageId!);
                return Task.FromResult(new EcosystemFindBlock<string>(
                    layer.CorePackageId!, 1));
            },
            (_, _, token) =>
                Pages(new([new("Contoso.Core", "1")]), token),
            (item, _, _) =>
            {
                evaluatedPrefixes.Add(item.PackageId);
                return Task.FromResult(new EcosystemFindBlock<string>(
                    item.PackageId, 1));
            },
            reuseCore: (layer, source, _) => source,
            combineCore: sources => string.Join(",",
                sources.Select(source => source.Content)));
        var result = await session.ExecuteAsync(TestContext.Current.CancellationToken);
        Assert.Equal(["Contoso.Core", "Other.Core"], evaluatedCores);
        Assert.Empty(evaluatedPrefixes);
        Assert.Empty(result.PrefixBlocks);
        Assert.Equal(
            ["ecosystem.first", "ecosystem.second"],
            result.BoundedBlocks[0].Memberships.Select(id => id.Value));
        Assert.Equal(EcosystemFindCompletion.Exhausted, result.Completion);
    }

    [Fact]
    public async Task CancellationAfterEvaluationBeforeAcceptanceDoesNotPublish()
    {
        var layer = Layer("first", "First.Core", "Contoso.");
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken);
        List<string> published = [];
        using var session = new EcosystemFindSearchSession<string>(
            Request([layer]),
            (_, _) => Task.FromResult(new EcosystemFindBlock<string>("core", 1)),
            (_, _, token) => Pages(new(
                [new("Contoso.Widget", "1")]), token),
            (_, _, _) =>
            {
                cancellation.Cancel();
                return Task.FromResult(new EcosystemFindBlock<string>(
                    "unaccepted", 1));
            },
            publish: block => published.Add(block.Content));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            session.ExecuteAsync(cancellation.Token));
        Assert.Equal(["core"], published);
    }

    [Fact]
    public async Task EqualPrefixesShareEnumerationAndCorePackagesAreNotRepeated()
    {
        EcosystemFindLayer[] layers =
        [
            Layer("first", "Contoso.Core", "Contoso."),
            Layer("second", "Other.Core", "contoso.", "Contoso.Special."),
        ];
        List<string> enumerated = [];
        List<string> evaluated = [];
        using var session = new EcosystemFindSearchSession<string>(
            Request(layers),
            (layer, _) => Task.FromResult(new EcosystemFindBlock<string>(
                layer.Registration.Id.Value, 0)),
            (prefix, _, token) =>
            {
                enumerated.Add(prefix.Prefix);
                return Pages(new(
                [
                    new("Contoso.Core", "1.0.0"),
                    new("Contoso.Special.Widget", "1.0.0"),
                ]), token);
            },
            (candidate, memberships, _) =>
            {
                evaluated.Add(candidate.PackageId);
                Assert.Equal(
                    ["ecosystem.first", "ecosystem.second"],
                    memberships.Select(id => id.Value));
                return Task.FromResult(new EcosystemFindBlock<string>(
                    candidate.PackageId, 1));
            });
        var result = await session.ExecuteAsync(TestContext.Current.CancellationToken);
        Assert.Equal(["Contoso.", "Contoso.Special."], enumerated);
        Assert.Equal(["Contoso.Special.Widget"], evaluated);
        Assert.Equal(EcosystemFindCompletion.Exhausted, result.Completion);
    }

    [Fact]
    public async Task ForeignContinuationCannotBeResumedByAnotherSession()
    {
        var layer = Layer("first", "First.Core", "Contoso.");
        EcosystemFindSearchSession<int> Create() =>
            new(Request([layer]),
                (_, _) => Task.FromResult(new EcosystemFindBlock<int>(0, 0)),
                (_, _, token) => Pages(new([]), token),
                (_, _, _) => Task.FromResult(new EcosystemFindBlock<int>(0, 0)));
        using var original = Create();
        using var replacement = Create();
        var bounded = await original.RunBoundedAsync(
            TestContext.Current.CancellationToken);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            replacement.ResumeAsync(
                bounded.Continuation!, null, TestContext.Current.CancellationToken));
        Assert.Equal(EcosystemFindCompletion.Exhausted,
            (await bounded.Continuation!.ResumeAsync(
                null, TestContext.Current.CancellationToken)).Completion);
    }

    [Fact]
    public void InvalidSelectionAndDemandFailBeforeSourceWork()
    {
        var layer = Layer("first", "First.Core", "Contoso.");
        Assert.Throws<ArgumentException>(() => Request([layer, layer]));
        Assert.Throws<ArgumentException>(() => new EcosystemFindSearchRequest(
            TypeFindQuestion.Create(["Thing"], FindVisibility.Public),
            [layer],
            [WorkspaceEcosystemRegistrationId.Create("ecosystem.other")],
            500));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            Request([layer], maximumCandidates: 501));
    }

    [Fact]
    public async Task StartedBlocksSettleWholeBeforeWindowStopsNextWork()
    {
        var layers = new[]
        {
            Layer("first", "First.Core", "Contoso."),
            Layer("second", "Second.Core", "Other."),
        };
        var searched = new List<string>();
        using var boundedSession = new EcosystemFindSearchSession<string>(
            Request(layers, maximumRows: 2),
            (layer, _) =>
            {
                searched.Add(layer.Registration.Id.Value);
                return Task.FromResult(new EcosystemFindBlock<string>(
                    layer.Registration.Id.Value, 3));
            },
            (_, _, token) => Pages(new([]), token),
            (_, _, _) => Task.FromResult(new EcosystemFindBlock<string>("", 0)));
        var stopped = await boundedSession.ExecuteAsync(
            TestContext.Current.CancellationToken);
        Assert.Equal(["ecosystem.first"], searched);
        Assert.Equal(["ecosystem.second", "Contoso.", "Other."],
            stopped.Unsearched);

        using var prefixSession = new EcosystemFindSearchSession<string>(
            Request([layers[0]], maximumRows: 2),
            (_, _) => Task.FromResult(new EcosystemFindBlock<string>("", 0)),
            (_, _, token) => Pages(new(
                [new("Contoso.One", "1"), new("Contoso.Two", "1")]), token),
            (item, _, _) =>
            {
                searched.Add(item.PackageId);
                return Task.FromResult(new EcosystemFindBlock<string>(
                    item.PackageId, 3));
            });
        var prefix = await prefixSession.ExecuteAsync(
            TestContext.Current.CancellationToken);
        Assert.Contains("Contoso.One", searched);
        Assert.DoesNotContain("Contoso.Two", searched);
        Assert.Equal(3, Assert.Single(prefix.PrefixBlocks).RowCount);
        Assert.Equal(EcosystemFindCompletion.RowLimitReached,
            prefix.Completion);
        Assert.Equal(["Contoso."], prefix.Unsearched);
    }

    [Fact]
    public async Task ScopedFailuresAndSourceTruncationStayInCompletion()
    {
        var layer = Layer("first", "First.Core", "Contoso.", "Other.");
        using var session = new EcosystemFindSearchSession<string>(
            Request([layer]),
            (_, _) => Task.FromResult(new EcosystemFindBlock<string>(
                "partial", 1, HasFailures: true, Incomplete: true)
                { Failure = "Could not settle core." }),
            (prefix, _, token) => Pages(
                prefix.Prefix == "Contoso."
                    ? new([], EcosystemFindPrefixPageCompletion.SourcePageLimit)
                    : new([], EcosystemFindPrefixPageCompletion.Exhausted),
                token),
            (_, _, _) => Task.FromResult(new EcosystemFindBlock<string>("", 0)));
        var result = await session.ExecuteAsync(TestContext.Current.CancellationToken);
        Assert.Equal(EcosystemFindCompletion.SourcePageLimitReached,
            result.Completion);
        Assert.Empty(result.Unsearched);
        Assert.Equal(
            [EcosystemFindPrefixPageCompletion.SourcePageLimit,
             EcosystemFindPrefixPageCompletion.Exhausted],
            result.PrefixCoverage.Select(source => source.Completion));
        Assert.Equal("ecosystem.first", Assert.Single(result.Failures).Source);
        Assert.True(result.HasFailures);
    }

    [Fact]
    public async Task SharedBoundedCoreIsEvaluatedOnceAndReducedIntoBothLayers()
    {
        EcosystemFindLayer[] layers =
        [
            Layer("first", "Shared.Core"),
            Layer("second", "Shared.Core"),
        ];
        int evaluations = 0;
        using var session = new EcosystemFindSearchSession<string>(
            new(TypeFindQuestion.Create(["Thing"], FindVisibility.Public),
                layers, [], 500),
            (layer, _) =>
            {
                evaluations++;
                Assert.Equal("Shared.Core", layer.CorePackageId);
                return Task.FromResult(new EcosystemFindBlock<string>(
                    layer.CorePackageId!, 1));
            },
            (_, _, token) => Pages(new([]), token),
            (_, _, _) => Task.FromResult(new EcosystemFindBlock<string>("", 0)),
            reuseCore: (layer, source, _) =>
                source with
                {
                    Content = layer.Registration.Id.Value
                        + ":" + source.Content,
                },
            combineCore: sources => string.Join(",",
                sources.Select(source => source.Content)));
        var result = await session.ExecuteAsync(TestContext.Current.CancellationToken);
        Assert.Equal(1, evaluations);
        Assert.Equal(
            ["Shared.Core", "ecosystem.second:Shared.Core"],
            result.BoundedBlocks.Select(block => block.Content));
        Assert.Equal(
            ["ecosystem.first", "ecosystem.second"],
            result.BoundedBlocks.Select(block => block.Ecosystem!.Value));
    }

    [Fact]
    public async Task EntirelyFailedSelectionDoesNotClaimExhaustion()
    {
        var layer = Layer("first", "First.Core");
        using var session = new EcosystemFindSearchSession<int>(
            new(TypeFindQuestion.Create(["Thing"], FindVisibility.Public),
                [layer], [], 500),
            (_, _) => Task.FromResult(new EcosystemFindBlock<int>(
                0, 0, HasFailures: true, Incomplete: true)
                { Failure = "Source unavailable." }),
            (_, _, token) => Pages(new([]), token),
            (_, _, _) => Task.FromResult(new EcosystemFindBlock<int>(0, 0)));
        var result = await session.ExecuteAsync(TestContext.Current.CancellationToken);
        Assert.Equal(EcosystemFindCompletion.Failed, result.Completion);
        Assert.Single(result.Failures);
    }
}
