using DotnetInspector.Packages;

namespace DotnetInspector.Queries.Tests;

public sealed partial class PackageDependencyTraversalQueryTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task Traversal_ChildManifestOverlapsSiblingResolution(
        int manifestBudget)
    {
        await using var fixture = new RegistryFixture();
        fixture.Registry.Add("first", "1.0.0", "");
        fixture.Registry.Add("second", "1.0.0", "");
        var first = new AcquisitionGate();
        var second = new AcquisitionGate();
        var manifest = new AcquisitionGate();
        fixture.Client.BeforeManifest = id =>
            id == "first" ? manifest.WaitAsync() : Task.CompletedTask;
        var resolver = new GatedSourceCandidateResolver(
            fixture.CandidateResolver,
            new Dictionary<string, AcquisitionGate>
            {
                ["first"] = first,
                ["second"] = second,
            });
        PackageDependencyTraversalRootOccurrence root = Root(
            "root", "1.0.0",
            Dependency("first", "[1.0.0]") + Dependency("second", "[1.0.0]"),
            PackageDependencyTraversalExpansionAuthority.RecursiveSources);

        Task<PackageDependencyTraversalOutcome> traversal = ExecuteAsync(
            [root], resolver, fixture.ManifestAcquirer,
            maxManifestProjections: manifestBudget,
            sourceLease: fixture.Lease, maximumConcurrentSteps: 2);
        try
        {
            Assert.True(first.Entered.Task.IsCompletedSuccessfully);
            Assert.True(second.Entered.Task.IsCompletedSuccessfully);
            first.Finish.SetResult();
            await manifest.Entered.Task.WaitAsync(
                TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            Assert.False(second.Finish.Task.IsCompleted);
            Assert.False(traversal.IsCompleted);
            manifest.Finish.SetResult();
            second.Finish.SetResult();

            PackageDependencyTraversalOutcome outcome = await traversal;
            Assert.Equal(
                ["root", "first", "second"],
                outcome.Nodes.Select(node => node.Coordinate.PackageId));
            Assert.Equal(
                ["first", "second"],
                outcome.Edges.Select(edge => edge.Declaration.CanonicalPackageId));
            Assert.Equal(1, fixture.Client.ManifestRequests.Count(id => id == "first"));
            Assert.Equal(
                manifestBudget - 1,
                fixture.Client.ManifestRequests.Count(id => id == "second"));
            Assert.Equal(2 - manifestBudget, outcome.Failures.Length);
        }
        finally
        {
            first.Finish.TrySetResult();
            second.Finish.TrySetResult();
            manifest.Finish.TrySetResult();
            await traversal;
        }
    }

    [Fact]
    public async Task Traversal_ReadyFrontierResolvesBeforeOtherManifestsSettle()
    {
        await using var fixture = new RegistryFixture();
        fixture.Registry.Add("first", "1.0.0", Dependency("leaf", "[1.0.0]"));
        fixture.Registry.Add("second", "1.0.0", "");
        fixture.Registry.Add("leaf", "1.0.0", "");
        var first = new AcquisitionGate();
        var second = new AcquisitionGate();
        var leaf = new AcquisitionGate();
        fixture.Client.BeforeManifest = id => id switch
        {
            "first" => first.WaitAsync(),
            "second" => second.WaitAsync(),
            _ => Task.CompletedTask,
        };
        var resolver = new GatedSourceCandidateResolver(
            fixture.CandidateResolver,
            new Dictionary<string, AcquisitionGate> { ["leaf"] = leaf });
        PackageDependencyTraversalRootOccurrence root = Root(
            "root", "1.0.0",
            Dependency("first", "[1.0.0]") + Dependency("second", "[1.0.0]"),
            PackageDependencyTraversalExpansionAuthority.RecursiveSources);

        Task<PackageDependencyTraversalOutcome> traversal = ExecuteAsync(
            [root], resolver, fixture.ManifestAcquirer,
            maxDepth: 2, sourceLease: fixture.Lease, maximumConcurrentSteps: 2);
        try
        {
            Assert.True(first.Entered.Task.IsCompletedSuccessfully);
            Assert.True(second.Entered.Task.IsCompletedSuccessfully);
            first.Finish.SetResult();
            await leaf.Entered.Task.WaitAsync(
                TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            Assert.False(second.Finish.Task.IsCompleted);
            leaf.Finish.SetResult();
            second.Finish.SetResult();

            PackageDependencyTraversalOutcome outcome = await traversal;
            Assert.Equal(
                ["root", "first", "second", "leaf"],
                outcome.Nodes.Select(node => node.Coordinate.PackageId));
            Assert.DoesNotContain("leaf", fixture.Client.ManifestRequests);
            Assert.Empty(outcome.Failures);
        }
        finally
        {
            first.Finish.TrySetResult();
            second.Finish.TrySetResult();
            leaf.Finish.TrySetResult();
            await traversal;
        }
    }

    [Fact]
    public async Task Traversal_ConcurrentCandidatesRefillBeforeTheFirstSettles()
    {
        string[] ids = ["first", "second", "third", "unadmitted"];
        var issuer = new PackageAcquisitionCandidateIssuer();
        PackageSourceAuthorization authorization = Authorize("concurrent-candidates");
        var candidates = ids.ToDictionary(
            id => id, id => Pinned(issuer, authorization, id, "1.0.0"));
        var gates = ids.ToDictionary(id => id, _ => new AcquisitionGate());
        var resolver = new GatedCandidateResolver(candidates, gates);
        PackageDependencyTraversalRootOccurrence root = Root(
            "root", "1.0.0",
            string.Concat(ids.Select(id => Dependency(id, "[1.0.0]"))),
            PackageDependencyTraversalExpansionAuthority.RecursiveSources);

        Task<PackageDependencyTraversalOutcome> traversal = ExecuteAsync(
            [root], resolver, new StubManifestAcquirer(),
            maxDepth: 1, maxDeclarationResolutions: 3,
            maximumConcurrentSteps: 2);
        try
        {
            Assert.True(gates["first"].Entered.Task.IsCompletedSuccessfully);
            Assert.True(gates["second"].Entered.Task.IsCompletedSuccessfully);
            Assert.False(gates["third"].Entered.Task.IsCompleted);
            gates["second"].Finish.SetResult();
            await gates["third"].Entered.Task.WaitAsync(
                TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            Assert.False(gates["first"].Finish.Task.IsCompleted);
            Assert.False(gates["unadmitted"].Entered.Task.IsCompleted);
            gates["third"].Finish.SetResult();
            gates["first"].Finish.SetResult();

            PackageDependencyTraversalOutcome outcome = await traversal;
            Assert.Equal(
                ids,
                outcome.Edges.Select(edge => edge.Declaration.CanonicalPackageId));
            Assert.Equal(
                ["root", "first", "second", "third"],
                outcome.Nodes.Select(node => node.Coordinate.PackageId));
            Assert.Equal(
                "unadmitted",
                Assert.Single(outcome.WorkBudgetDeclarations).CanonicalPackageId);
        }
        finally
        {
            foreach (AcquisitionGate gate in gates.Values)
                gate.Finish.TrySetResult();
            await traversal;
        }
    }

    [Fact]
    public async Task Traversal_ConcurrentManifestsRefillWithinTheProjectionBudget()
    {
        await using var fixture = new RegistryFixture();
        string[] ids = ["first", "second", "third", "unadmitted"];
        var gates = ids.ToDictionary(id => id, _ => new AcquisitionGate());
        foreach (string id in ids)
            fixture.Registry.Add(id, "1.0.0", "");
        fixture.Client.BeforeManifest = id => gates[id].WaitAsync();
        PackageDependencyTraversalRootOccurrence root = Root(
            "root", "1.0.0",
            string.Concat(ids.Select(id => Dependency(id, "[1.0.0]"))),
            PackageDependencyTraversalExpansionAuthority.RecursiveSources);

        Task<PackageDependencyTraversalOutcome> traversal = ExecuteAsync(
            [root], fixture.CandidateResolver, fixture.ManifestAcquirer,
            maxManifestProjections: 3, sourceLease: fixture.Lease,
            maximumConcurrentSteps: 2);
        try
        {
            Assert.True(gates["first"].Entered.Task.IsCompletedSuccessfully);
            Assert.True(gates["second"].Entered.Task.IsCompletedSuccessfully);
            Assert.False(gates["third"].Entered.Task.IsCompleted);
            gates["second"].Finish.SetResult();
            await gates["third"].Entered.Task.WaitAsync(
                TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            Assert.False(gates["first"].Finish.Task.IsCompleted);
            Assert.False(gates["unadmitted"].Entered.Task.IsCompleted);
            gates["third"].Finish.SetResult();
            gates["first"].Finish.SetResult();

            PackageDependencyTraversalOutcome outcome = await traversal;
            Assert.Equal(
                ids,
                outcome.Edges.Select(edge => edge.Declaration.CanonicalPackageId));
            var failure = Assert.Single(outcome.Failures);
            Assert.Equal("unadmitted", outcome.Nodes[failure.NodeIndex].Coordinate.PackageId);
            Assert.IsType<PackageDependencyTraversalManifestFailureDetail
                .ManifestProjectionBudgetExhausted>(failure.Detail);
            Assert.Equal(
                PackageDependencyTraversalRootCompletion.Partial,
                Assert.Single(outcome.Roots).Completion);
        }
        finally
        {
            foreach (AcquisitionGate gate in gates.Values)
                gate.Finish.TrySetResult();
            await traversal;
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task Traversal_ConcurrentTerminalPathsDrainSourceWork(
        bool cancel,
        bool crossStage)
    {
        await using var fixture = new RegistryFixture();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken);
        fixture.Registry.Add("first", "1.0.0", "");
        fixture.Registry.Add("second", "1.0.0", "");
        var first = new AcquisitionGate();
        var second = new AcquisitionGate();
        var failure = new InvalidOperationException("The first source operation failed.");
        fixture.Client.BeforeManifest = id =>
            crossStage && id == "second"
                ? Task.CompletedTask
                : (id == "first" ? first : second).WaitAsync();
        IPackageDependencyTraversalCandidateResolver resolver = crossStage
            ? new GatedSourceCandidateResolver(
                fixture.CandidateResolver,
                new Dictionary<string, AcquisitionGate> { ["second"] = second })
            : fixture.CandidateResolver;
        PackageDependencyTraversalRootOccurrence root = Root(
            "root", "1.0.0",
            Dependency("first", "[1.0.0]") + Dependency("second", "[1.0.0]"),
            PackageDependencyTraversalExpansionAuthority.RecursiveSources);
        using PackageSourceOperationLease operation = fixture.Lease.IssueOperationLease(
            cancellation.Token, null, null, maximumConcurrentSteps: 2);
        Task<PackageDependencyTraversalOutcome> traversal =
            PackageDependencyTraversalQuery.ExecuteAsync(
                BuildRequest([root], resolver, fixture.ManifestAcquirer),
                operation);
        try
        {
            Assert.True(first.Entered.Task.IsCompletedSuccessfully);
            Assert.True(second.Entered.Task.IsCompletedSuccessfully);
            if (cancel)
            {
                cancellation.Cancel();
                first.Finish.SetResult();
            }
            else
            {
                first.Finish.SetException(failure);
            }

            if (!crossStage)
                Assert.Throws<InvalidOperationException>(operation.Dispose);
            Assert.False(traversal.IsCompleted);
            second.Finish.SetResult();
            if (cancel)
            {
                OperationCanceledException error =
                    await Assert.ThrowsAnyAsync<OperationCanceledException>(
                        () => traversal);
                Assert.Equal(cancellation.Token, error.CancellationToken);
            }
            else
            {
                Assert.Same(
                    failure,
                    await Assert.ThrowsAsync<InvalidOperationException>(
                        () => traversal));
            }
            operation.Dispose();
        }
        finally
        {
            first.Finish.TrySetResult();
            second.Finish.TrySetResult();
            Task settlement = traversal;
            await settlement.ConfigureAwait(
                ConfigureAwaitOptions.SuppressThrowing
                | ConfigureAwaitOptions.ContinueOnCapturedContext);
        }
    }

    private sealed class AcquisitionGate
    {
        public TaskCompletionSource Entered { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Finish { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task WaitAsync()
        {
            Entered.TrySetResult();
            await Finish.Task.ConfigureAwait(false);
        }
    }

    private sealed class GatedCandidateResolver(
        IReadOnlyDictionary<string, PackageAcquisitionCandidate> candidates,
        IReadOnlyDictionary<string, AcquisitionGate> gates) :
        IPackageDependencyTraversalCandidateResolver
    {
        public async ValueTask<PackageDependencyTraversalCandidateResult> ResolveAsync(
            PackageDependencyEvidenceDeclaration declaration,
            PackageSourceOperationLease operation)
        {
            string id = declaration.CanonicalPackageId;
            await gates[id].WaitAsync().ConfigureAwait(false);
            return Resolved(candidates[id]);
        }
    }

    private sealed class GatedSourceCandidateResolver(
        IPackageDependencyTraversalCandidateResolver inner,
        IReadOnlyDictionary<string, AcquisitionGate> gates) :
        IPackageDependencyTraversalCandidateResolver
    {
        public async ValueTask<PackageDependencyTraversalCandidateResult> ResolveAsync(
            PackageDependencyEvidenceDeclaration declaration,
            PackageSourceOperationLease operation)
        {
            if (gates.TryGetValue(declaration.CanonicalPackageId, out AcquisitionGate? gate))
                await gate.WaitAsync().ConfigureAwait(false);
            return await inner.ResolveAsync(declaration, operation).ConfigureAwait(false);
        }
    }
}
