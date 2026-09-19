using DotnetInspector.Platforms;

namespace DotnetInspector.PlatformHouse.Tests;

public sealed class PlatformTargetSelectorTests
{
    [Theory]
    [InlineData("10.0.1", "net10.0")]
    [InlineData("11.0.0-rc.1", "net11.0")]
    public async Task EligiblePreferredTargetSuppressesFallback(
        string version,
        string framework)
    {
        PlatformSourceCapabilityIdentity installed =
            PlatformSourceCapabilityIdentity.Create("installed");
        PlatformSourceCapabilityIdentity package =
            PlatformSourceCapabilityIdentity.Create("package");
        PlatformHouseRequest request = Request(
            [installed],
            [package]);
        int fallbackInvocations = 0;
        PlatformFamilyTarget target = Target(framework, version);

        PlatformHouseOutcome<TestValue> outcome =
            await PlatformHouseTargetSelector.ExecuteAsync<TestValue>(
                request,
                [
                    Success(installed, target),
                    Success(
                        package,
                        () => fallbackInvocations++,
                        Target("net10.0", "10.0.12")),
                ],
                Continue(request));

        Assert.Equal(target, outcome.Receipt.TargetSettlement.SettledTarget);
        Assert.Equal(0, fallbackInvocations);
        Assert.Single(outcome.Receipt.SourceSettlements);
        Assert.Equal(
            PlatformSourceSettlementDisposition.Selected,
            outcome.Receipt.SourceSettlements[0].Disposition);
    }

    [Fact]
    public async Task BelowFloorPreferredFallsBackToGreatestStableTarget()
    {
        PlatformSourceCapabilityIdentity installed =
            PlatformSourceCapabilityIdentity.Create("installed");
        PlatformSourceCapabilityIdentity package =
            PlatformSourceCapabilityIdentity.Create("package");
        PlatformHouseRequest request = Request(
            [installed],
            [package]);
        PlatformFamilyTarget expected = Target("net10.0", "10.0.12");

        PlatformHouseOutcome<TestValue> outcome =
            await PlatformHouseTargetSelector.ExecuteAsync<TestValue>(
                request,
                [
                    Success(
                        installed,
                        Target("net9.0", "9.0.11"),
                        Target("net10.0", "10.0.0")),
                    Success(
                        package,
                        Target("net10.0", "10.0.13-rc.1"),
                        Target("net10.0", "10.0.11"),
                        expected),
                ],
                Continue(request));

        Assert.Equal(expected, outcome.Receipt.TargetSettlement.SettledTarget);
        Assert.Collection(
            outcome.Receipt.SourceSettlements,
            preferred => Assert.Equal(
                PlatformSourceSettlementDisposition.OutcomeRelevant,
                preferred.Disposition),
            fallback => Assert.Equal(
                PlatformSourceSettlementDisposition.Selected,
                fallback.Disposition));
    }

    [Fact]
    public async Task PreferredAbsencePermitsFallback()
    {
        PlatformSourceCapabilityIdentity installed =
            PlatformSourceCapabilityIdentity.Create("installed");
        PlatformSourceCapabilityIdentity package =
            PlatformSourceCapabilityIdentity.Create("package");
        PlatformHouseRequest request = Request(
            [installed],
            [package]);

        PlatformHouseOutcome<TestValue> outcome =
            await PlatformHouseTargetSelector.ExecuteAsync<TestValue>(
                request,
                [
                    Terminal(
                        installed,
                        PlatformSourceContributionKind.Unavailable,
                        PlatformSourceUnavailabilityKind.Absent),
                    Success(package, Target("net10.0", "10.0.12")),
                ],
                Continue(request));

        Assert.Equal(
            "10.0.12",
            outcome.Receipt.TargetSettlement.SettledTarget!.Version.Value);
    }

    [Fact]
    public async Task FallbackOnlyPolicySelectsStableTarget()
    {
        PlatformSourceCapabilityIdentity package =
            PlatformSourceCapabilityIdentity.Create("package");
        PlatformHouseRequest request = Request(
            [],
            [package]);

        PlatformHouseOutcome<TestValue> outcome =
            await PlatformHouseTargetSelector.ExecuteAsync<TestValue>(
                request,
                [
                    Success(
                        package,
                        Target("net10.0", "10.0.13-preview.1"),
                        Target("net10.0", "10.0.12")),
                ],
                Continue(request));

        Assert.Equal(
            "10.0.12",
            outcome.Receipt.TargetSettlement.SettledTarget!.Version.Value);
    }

    [Fact]
    public async Task FailedFallbackCannotSelectAResult()
    {
        PlatformSourceCapabilityIdentity installed =
            PlatformSourceCapabilityIdentity.Create("installed");
        PlatformSourceCapabilityIdentity package =
            PlatformSourceCapabilityIdentity.Create("package");
        PlatformHouseRequest request = Request(
            [installed],
            [package]);

        PlatformHouseOutcome<TestValue> outcome =
            await PlatformHouseTargetSelector.ExecuteAsync<TestValue>(
                request,
                [
                    Success(installed, Target("net10.0", "10.0.0")),
                    Terminal(
                        package,
                        PlatformSourceContributionKind.Failed),
                ],
                Continue(request));

        Assert.IsType<PlatformHouseOutcome<TestValue>.Failed>(outcome);
        Assert.Null(outcome.Receipt.TargetSettlement.SettledTarget);
        Assert.Equal(2, outcome.Receipt.SourceSettlements.Count);
    }

    [Theory]
    [InlineData(PlatformSourceContributionKind.Unavailable)]
    [InlineData(PlatformSourceContributionKind.Rejected)]
    [InlineData(PlatformSourceContributionKind.Incomplete)]
    [InlineData(PlatformSourceContributionKind.Failed)]
    public async Task PreferredTerminalDoesNotInvokeFallback(
        PlatformSourceContributionKind terminalKind)
    {
        PlatformSourceCapabilityIdentity installed =
            PlatformSourceCapabilityIdentity.Create("installed");
        PlatformSourceCapabilityIdentity package =
            PlatformSourceCapabilityIdentity.Create("package");
        PlatformHouseRequest request = Request(
            [installed],
            [package]);
        int fallbackInvocations = 0;

        PlatformHouseOutcome<TestValue> outcome =
            await PlatformHouseTargetSelector.ExecuteAsync<TestValue>(
                request,
                [
                    Terminal(
                        installed,
                        terminalKind,
                        PlatformSourceUnavailabilityKind.Unavailable),
                    Success(
                        package,
                        () => fallbackInvocations++,
                        Target("net10.0", "10.0.12")),
                ],
                Continue(request));

        Assert.Equal(0, fallbackInvocations);
        Assert.Null(outcome.Receipt.TargetSettlement.SettledTarget);
        Assert.Equal(
            terminalKind switch
            {
                PlatformSourceContributionKind.Unavailable =>
                    PlatformHouseSettlementKind.Unavailable,
                PlatformSourceContributionKind.Rejected =>
                    PlatformHouseSettlementKind.Rejected,
                PlatformSourceContributionKind.Incomplete =>
                    PlatformHouseSettlementKind.Incomplete,
                PlatformSourceContributionKind.Failed =>
                    PlatformHouseSettlementKind.Failed,
                _ => throw new InvalidOperationException(),
            },
            outcome.Receipt.SettlementKind);
    }

    [Fact]
    public async Task EveryPreferredCapabilitySettlesBeforeSelection()
    {
        PlatformSourceCapabilityIdentity first =
            PlatformSourceCapabilityIdentity.Create("first-installed");
        PlatformSourceCapabilityIdentity second =
            PlatformSourceCapabilityIdentity.Create("second-installed");
        PlatformSourceCapabilityIdentity package =
            PlatformSourceCapabilityIdentity.Create("package");
        PlatformHouseRequest request = Request(
            [first, second],
            [package]);
        int invoked = 0;

        PlatformHouseOutcome<TestValue> outcome =
            await PlatformHouseTargetSelector.ExecuteAsync<TestValue>(
                request,
                [
                    Success(
                        second,
                        () => invoked++,
                        Target("net11.0", "11.0.0-rc.1")),
                    Success(
                        package,
                        Target("net10.0", "10.0.12")),
                    Success(
                        first,
                        () => invoked++,
                        Target("net10.0", "10.0.1")),
                ],
                Continue(request));

        Assert.Equal(2, invoked);
        Assert.Equal(
            "11.0.0-rc.1",
            outcome.Receipt.TargetSettlement.SettledTarget!.Version.Value);
        Assert.Collection(
            outcome.Receipt.SourceSettlements,
            firstSettlement => Assert.Equal(
                PlatformSourceSettlementDisposition.Shadowed,
                firstSettlement.Disposition),
            secondSettlement => Assert.Equal(
                PlatformSourceSettlementDisposition.Selected,
                secondSettlement.Disposition));
    }

    [Fact]
    public async Task ExactIdentityBreaksEqualSemVerPrecedence()
    {
        PlatformSourceCapabilityIdentity installed =
            PlatformSourceCapabilityIdentity.Create("installed");
        PlatformSourceCapabilityIdentity package =
            PlatformSourceCapabilityIdentity.Create("package");
        PlatformHouseRequest request = Request(
            [installed],
            [package]);

        PlatformHouseOutcome<TestValue> outcome =
            await PlatformHouseTargetSelector.ExecuteAsync<TestValue>(
                request,
                [
                    Success(
                        installed,
                        Target("net10.0", "10.0.1+aaa"),
                        Target("net10.0", "10.0.1+zzz")),
                    Success(package, Target("net10.0", "10.0.12")),
                ],
                Continue(request));

        Assert.Equal(
            "10.0.1+zzz",
            outcome.Receipt.TargetSettlement.SettledTarget!.Version.Value);
    }

    [Fact]
    public async Task TypedStagesIgnoreSourcePlanModeAndOrder()
    {
        PlatformSourceCapabilityIdentity installed =
            PlatformSourceCapabilityIdentity.Create("installed");
        PlatformSourceCapabilityIdentity package =
            PlatformSourceCapabilityIdentity.Create("package");
        PlatformHouseRequest request = Request(
            [installed],
            [package],
            planCapabilities: [package, installed],
            planMode: PlatformSourceSelectionMode.Aggregation);
        int fallbackInvocations = 0;

        PlatformHouseOutcome<TestValue> outcome =
            await PlatformHouseTargetSelector.ExecuteAsync<TestValue>(
                request,
                [
                    Success(
                        package,
                        () => fallbackInvocations++,
                        Target("net10.0", "10.0.12")),
                    Success(
                        installed,
                        Target("net11.0", "11.0.0-rc.1")),
                ],
                Continue(request));

        Assert.Equal(0, fallbackInvocations);
        Assert.Equal(
            "11.0.0-rc.1",
            outcome.Receipt.TargetSettlement.SettledTarget!.Version.Value);
    }

    [Fact]
    public async Task InvalidSourceSetRejectsBeforeSourceWork()
    {
        PlatformSourceCapabilityIdentity installed =
            PlatformSourceCapabilityIdentity.Create("installed");
        PlatformSourceCapabilityIdentity package =
            PlatformSourceCapabilityIdentity.Create("package");
        PlatformHouseRequest request = Request(
            [installed],
            [package]);
        int invocations = 0;

        PlatformHouseOutcome<TestValue> outcome =
            await PlatformHouseTargetSelector.ExecuteAsync<TestValue>(
                request,
                [
                    Success(
                        installed,
                        () => invocations++,
                        Target("net10.0", "10.0.1")),
                ],
                Continue(request));

        Assert.IsType<PlatformHouseOutcome<TestValue>.Rejected>(outcome);
        Assert.Equal(0, invocations);
        Assert.Equal(0, outcome.Receipt.ConsumedWork.SourceOperations);
    }

    [Fact]
    public async Task DuplicateSourceRejectsBeforeSourceWork()
    {
        PlatformSourceCapabilityIdentity installed =
            PlatformSourceCapabilityIdentity.Create("installed");
        PlatformSourceCapabilityIdentity package =
            PlatformSourceCapabilityIdentity.Create("package");
        PlatformHouseRequest request = Request(
            [installed],
            [package]);
        int invocations = 0;
        PlatformTargetDiscoverySource duplicate = Success(
            installed,
            () => invocations++,
            Target("net10.0", "10.0.1"));

        PlatformHouseOutcome<TestValue> outcome =
            await PlatformHouseTargetSelector.ExecuteAsync<TestValue>(
                request,
                [
                    duplicate,
                    duplicate,
                    Success(package, Target("net10.0", "10.0.12")),
                ],
                Continue(request));

        Assert.IsType<PlatformHouseOutcome<TestValue>.Rejected>(outcome);
        Assert.Equal(0, invocations);
    }

    [Fact]
    public async Task ForeignAttemptRejectsWithoutRetainingForeignEvidence()
    {
        PlatformSourceCapabilityIdentity installed =
            PlatformSourceCapabilityIdentity.Create("installed");
        PlatformSourceCapabilityIdentity package =
            PlatformSourceCapabilityIdentity.Create("package");
        PlatformHouseRequest request = Request(
            [installed],
            [package]);
        PlatformHouseRequest foreign = Request(
            [installed],
            [package]);
        PlatformFamilyTarget target = Target("net10.0", "10.0.1");
        var source = new PlatformTargetDiscoverySource(
            installed,
            _ =>
            {
                var contribution =
                    new PlatformSourceContribution.TargetDiscovery(
                        installed,
                        foreign.Snapshot,
                        PlatformSourceGeneration.Create(
                            "foreign-generation"),
                        [target]);
                PlatformTargetDiscoveryAttempt attempt =
                    new PlatformTargetDiscoveryAttempt.Succeeded(
                        contribution,
                        [
                            new PlatformTargetDiscoveryCandidate<
                                TestAssociation>(
                                    target,
                                    new("foreign")),
                        ]);
                return ValueTask.FromResult(attempt);
            });

        PlatformHouseOutcome<TestValue> outcome =
            await PlatformHouseTargetSelector.ExecuteAsync<TestValue>(
                request,
                [
                    source,
                    Success(package, Target("net10.0", "10.0.12")),
                ],
                Continue(request));

        Assert.IsType<PlatformHouseOutcome<TestValue>.Rejected>(outcome);
        Assert.Empty(outcome.Receipt.SourceSettlements);
        Assert.Equal(1, outcome.Receipt.ConsumedWork.SourceOperations);
    }

    [Theory]
    [InlineData(0, 8)]
    [InlineData(8, 0)]
    public async Task FiniteTargetWorkCannotSelectObservedPrefix(
        int maxCandidates,
        int maxComparisons)
    {
        PlatformSourceCapabilityIdentity installed =
            PlatformSourceCapabilityIdentity.Create("installed");
        PlatformSourceCapabilityIdentity package =
            PlatformSourceCapabilityIdentity.Create("package");
        PlatformHouseRequest request = Request(
            [installed],
            [package],
            discoveryWork: new(
                maxCandidates,
                maxComparisons));

        PlatformHouseOutcome<TestValue> outcome =
            await PlatformHouseTargetSelector.ExecuteAsync<TestValue>(
                request,
                [
                    Success(installed, Target("net10.0", "10.0.1")),
                    Success(package, Target("net10.0", "10.0.12")),
                ],
                Continue(request));

        Assert.IsType<PlatformHouseOutcome<TestValue>.Incomplete>(outcome);
        Assert.Null(outcome.Receipt.TargetSettlement.SettledTarget);
    }

    [Fact]
    public async Task SourceOperationLimitStopsBeforeFallbackInvocation()
    {
        PlatformSourceCapabilityIdentity installed =
            PlatformSourceCapabilityIdentity.Create("installed");
        PlatformSourceCapabilityIdentity package =
            PlatformSourceCapabilityIdentity.Create("package");
        PlatformHouseRequest request = Request(
            [installed],
            [package],
            maxSourceOperations: 1);
        int fallbackInvocations = 0;

        PlatformHouseOutcome<TestValue> outcome =
            await PlatformHouseTargetSelector.ExecuteAsync<TestValue>(
                request,
                [
                    Success(installed, Target("net10.0", "10.0.0")),
                    Success(
                        package,
                        () => fallbackInvocations++,
                        Target("net10.0", "10.0.12")),
                ],
                Continue(request));

        Assert.IsType<PlatformHouseOutcome<TestValue>.Incomplete>(outcome);
        Assert.Equal(0, fallbackInvocations);
    }

    [Fact]
    public async Task ZeroDurationStopsBeforeSourceInvocation()
    {
        PlatformSourceCapabilityIdentity installed =
            PlatformSourceCapabilityIdentity.Create("installed");
        PlatformSourceCapabilityIdentity package =
            PlatformSourceCapabilityIdentity.Create("package");
        PlatformHouseRequest request = Request(
            [installed],
            [package],
            maxDuration: TimeSpan.Zero);
        int invocations = 0;

        PlatformHouseOutcome<TestValue> outcome =
            await PlatformHouseTargetSelector.ExecuteAsync<TestValue>(
                request,
                [
                    Success(
                        installed,
                        () => invocations++,
                        Target("net10.0", "10.0.1")),
                    Success(package, Target("net10.0", "10.0.12")),
                ],
                Continue(request));

        Assert.IsType<PlatformHouseOutcome<TestValue>.Incomplete>(outcome);
        Assert.Equal(0, invocations);
    }

    [Fact]
    public async Task CancellationDuringInvocationRemainsCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        PlatformSourceCapabilityIdentity installed =
            PlatformSourceCapabilityIdentity.Create("installed");
        PlatformSourceCapabilityIdentity package =
            PlatformSourceCapabilityIdentity.Create("package");
        PlatformHouseRequest request = Request(
            [installed],
            [package],
            cancellation: cancellation);
        var source = new PlatformTargetDiscoverySource(
            installed,
            async current =>
            {
                cancellation.Cancel();
                await Task.Yield();
                current.CancellationToken.ThrowIfCancellationRequested();
                throw new InvalidOperationException();
            });

        await Assert.ThrowsAsync<OperationCanceledException>(
            async () => await PlatformHouseTargetSelector.ExecuteAsync<TestValue>(
                request,
                [
                    source,
                    Success(package, Target("net10.0", "10.0.12")),
                ],
                Continue(request)));
    }

    static PlatformTargetSelectionContinuation<TestValue> Continue(
        PlatformHouseRequest request) =>
        selection =>
        {
            var termination = new PlatformHouseTermination.Unavailable(
                PlatformHouseTerminalEvidenceIdentity.Create(
                    "downstream-not-yet-realized"));
            var receipt = new PlatformHouseReceipt(
                request.Snapshot,
                selection.TargetSettlement,
                selection.SourceSettlements,
                selection.ConsumedWork,
                termination: termination);
            PlatformHouseOutcome<TestValue> outcome =
                new PlatformHouseOutcome<TestValue>.Unavailable(
                    termination,
                    receipt);
            return ValueTask.FromResult(outcome);
        };

    static PlatformTargetDiscoverySource Success(
        PlatformSourceCapabilityIdentity capability,
        params PlatformFamilyTarget[] targets) =>
        Success(capability, observed: null, targets);

    static PlatformTargetDiscoverySource Success(
        PlatformSourceCapabilityIdentity capability,
        Action? observed,
        params PlatformFamilyTarget[] targets) =>
        new(
            capability,
            request =>
            {
                observed?.Invoke();
                var contribution =
                    new PlatformSourceContribution.TargetDiscovery(
                        capability,
                        request.Snapshot,
                        PlatformSourceGeneration.Create(
                            capability.Name + "-generation"),
                        targets);
                PlatformTargetDiscoveryAttempt attempt =
                    new PlatformTargetDiscoveryAttempt.Succeeded(
                        contribution,
                        targets.Select(
                            target =>
                                new PlatformTargetDiscoveryCandidate<
                                    TestAssociation>(
                                        target,
                                        new(target.Version.Value))));
                return ValueTask.FromResult(attempt);
            });

    static PlatformTargetDiscoverySource Terminal(
        PlatformSourceCapabilityIdentity capability,
        PlatformSourceContributionKind kind,
        PlatformSourceUnavailabilityKind unavailableReason =
            PlatformSourceUnavailabilityKind.Unavailable) =>
        new(
            capability,
            request =>
            {
                PlatformSourceGeneration generation =
                    PlatformSourceGeneration.Create(
                        capability.Name + "-generation");
                PlatformSourceContribution contribution = kind switch
                {
                    PlatformSourceContributionKind.Unavailable =>
                        new PlatformSourceContribution.Unavailable(
                            PlatformSourceFacet.TargetDiscovery,
                            capability,
                            request.Snapshot,
                            generation,
                            exactTarget: null,
                            unavailableReason),
                    PlatformSourceContributionKind.Rejected =>
                        new PlatformSourceContribution.Rejected(
                            PlatformSourceFacet.TargetDiscovery,
                            capability,
                            request.Snapshot,
                            generation,
                            exactTarget: null),
                    PlatformSourceContributionKind.Incomplete =>
                        new PlatformSourceContribution.Incomplete(
                            PlatformSourceFacet.TargetDiscovery,
                            capability,
                            request.Snapshot,
                            generation,
                            exactTarget: null),
                    PlatformSourceContributionKind.Failed =>
                        new PlatformSourceContribution.Failed(
                            PlatformSourceFacet.TargetDiscovery,
                            capability,
                            request.Snapshot,
                            generation,
                            exactTarget: null),
                    _ => throw new ArgumentOutOfRangeException(nameof(kind)),
                };
                PlatformTargetDiscoveryAttempt attempt =
                    new PlatformTargetDiscoveryAttempt.NotSucceeded(
                        contribution,
                        kind == PlatformSourceContributionKind.Rejected
                            ? PlatformHouseRejectionKind.InvalidOwnerResult
                            : null);
                return ValueTask.FromResult(attempt);
            });

    static PlatformHouseRequest Request(
        IReadOnlyList<PlatformSourceCapabilityIdentity> preferredCapabilities,
        IReadOnlyList<PlatformSourceCapabilityIdentity> fallbackCapabilities,
        IReadOnlyList<PlatformSourceCapabilityIdentity>? planCapabilities =
            null,
        PlatformSourceSelectionMode planMode =
            PlatformSourceSelectionMode.Precedence,
        PlatformTargetDiscoveryBudget? discoveryWork = null,
        int maxSourceOperations = 8,
        TimeSpan? maxDuration = null,
        CancellationTokenSource? cancellation = null)
    {
        PlatformTargetDiscoveryStage? preferred =
            preferredCapabilities.Count == 0
                ? null
                : new(
                    new PlatformTargetDiscoveryScope.AllFrameworks(),
                    preferredCapabilities);
        var fallback = new PlatformTargetDiscoveryStage(
            new PlatformTargetDiscoveryScope.ExactFramework(
                PlatformTargetFramework.Parse("net10.0")),
            fallbackCapabilities);
        var demand = new PlatformTargetDemand.FamilyDefault(
            PlatformFamily.DotNetRuntime,
            new PlatformVersionlessRuntimeTargetPolicy(
                PlatformTargetSelectionPolicyIdentity.Create(
                    "versionless-runtime-default"),
                PlatformTargetSelectionPolicyGeneration.Create(
                    "generation-1"),
                PlatformVersion.Parse("10.0.1"),
                preferred,
                fallback),
            discoveryWork ?? new PlatformTargetDiscoveryBudget(32, 128));
        var sources = new PlatformSourcePlan(
            PlatformSourcePlanIdentity.Create("sources"),
            PlatformSourcePolicyGeneration.Create("generation"),
            [
                new PlatformSourceSelection(
                    PlatformSourceFacet.TargetDiscovery,
                    planMode,
                    planCapabilities
                        ?? [.. preferredCapabilities, .. fallbackCapabilities]),
            ]);
        return new(
            PlatformHouseRequestIdentity.Create("request"),
            demand,
            new PlatformHouseRequestOrigin.Standalone(
                PlatformStandaloneOperationIdentity.Create("standalone")),
            new PlatformHouseOperation.Realize(
                new PlatformPopulationDemand.CompletePopulation(),
                PlatformViewDemand.Reference),
            sources,
            new PlatformHouseWorkBudget(
                maxSourceOperations,
                maxTargetCandidates: 32,
                maxAssemblies: 64,
                maxXmlDocuments: 8,
                maxPortablePdbs: 8,
                maxSourceDocuments: 16,
                maxBytes: 1024 * 1024,
                maxForwardingHops: 16,
                maxDuration ?? TimeSpan.FromSeconds(30)),
            cancellation?.Token
                ?? TestContext.Current.CancellationToken);
    }

    static PlatformFamilyTarget Target(
        string framework,
        string version) =>
        new(
            PlatformFamily.DotNetRuntime,
            PlatformTargetFramework.Parse(framework),
            PlatformVersion.Parse(version));

    sealed record TestAssociation(string Name);
    sealed class TestValue;
}
