using DotnetInspector.Packages;

namespace DotnetInspector.Queries.Tests;

public sealed partial class PackageDependencyTraversalQueryTests
{
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
    [InlineData(false)]
    [InlineData(true)]
    public async Task Traversal_ConcurrentTerminalPathsDrainSourceWork(bool cancel)
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
            (id == "first" ? first : second).WaitAsync();
        PackageDependencyTraversalRootOccurrence root = Root(
            "root", "1.0.0",
            Dependency("first", "[1.0.0]") + Dependency("second", "[1.0.0]"),
            PackageDependencyTraversalExpansionAuthority.RecursiveSources);
        using PackageSourceOperationLease operation = fixture.Lease.IssueOperationLease(
            cancellation.Token, null, null, maximumConcurrentSteps: 2);
        Task<PackageDependencyTraversalOutcome> traversal =
            PackageDependencyTraversalQuery.ExecuteAsync(
                BuildRequest([root], fixture.CandidateResolver, fixture.ManifestAcquirer),
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
}
