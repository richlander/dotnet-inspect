using System.Collections.Immutable;
using System.Text.Json;

using ILInspector.Metadata;

namespace DotnetInspector.Queries.Tests;

public sealed partial class NavigationSessionTests
{
    [Fact]
    public async Task Initialization_PreservesOrderAndDoesNotChooseAnOccurrence()
    {
        await using Fixture fixture = await Fixture.CreateAsync(selectPackage: false);
        NavigationConsumerSnapshot snapshot = fixture.Session.Snapshot;
        Assert.Equal(StructuralSubjectKind.Workspace, snapshot.ActiveSubject.Kind);
        Assert.Null(snapshot.ActivePackage);
        Assert.Equal([1, 2], snapshot.Packages.Select(row => row.Order));
        Assert.All(snapshot.Packages, row => Assert.NotNull(row.Action));
        Assert.Empty(snapshot.Types);
        Assert.Null(snapshot.Hierarchy[0].Action);
        Assert.Equal(NavigationSynchronizationDisposition.SynchronizationRequired,
            fixture.Session.Initialization.Synchronization);
    }

    [Fact]
    public async Task RepeatedEqualCoordinates_ActivateExactOwnerIssuedOccurrence()
    {
        await using Fixture fixture = await Fixture.CreateAsync(selectPackage: false, repeatedCoordinates: true);
        NavigationConsumerPackageDescriptor[] rows = [.. fixture.Session.Snapshot.Packages];
        Assert.Equal(rows[0].PackageId, rows[1].PackageId);
        Assert.Equal(rows[0].Version, rows[1].Version);
        Assert.NotEqual(rows[0].Subject.Id, rows[1].Subject.Id);
        Assert.NotEqual(rows[0].Action!.Id, rows[1].Action!.Id);
        NavigationConsumerResult result = await fixture.Session.ExecuteAsync(rows[1].Action!, TestContext.Current.CancellationToken);
        Assert.Equal(NavigationOutcomeKind.Applied, result.Outcome.Kind);
        Assert.Same(fixture.Scope.Packages[1].Occurrence, fixture.LastRequest!.Occurrence);
        Assert.Same(fixture.Scope.Packages[1].Occurrence, fixture.Session.InstalledSnapshot.ActiveOccurrence);
        Assert.Equal(StructuralSubjectKind.Library, result.Snapshot.ActiveSubject.Kind);
        Assert.True(result.Snapshot.Packages[1].IsCurrent);
        Assert.Null(result.Snapshot.Packages[1].Action);
        Assert.NotNull(result.Snapshot.Packages[0].Action);
    }

    [Fact]
    public async Task ExactSubjects_PreserveContextAndRecommendOnlyForChangedSubject()
    {
        await using Fixture fixture = await Fixture.CreateAsync();
        NavigationTestHost session = fixture.Session;
        NavigationConsumerResult type = await session.ExecuteAsync(session.Snapshot.Types[1].Navigation.Action!, TestContext.Current.CancellationToken);
        Assert.Equal(StructuralSubjectKind.Type, type.Snapshot.ActiveSubject.Kind);
        Assert.Equal(type.Snapshot.Types[1].Library, type.Snapshot.TypeInventoryLibraryContext!.Id);
        Assert.Equal("type.api", type.Snapshot.LensOutcome.EffectiveLens!.Facet);
        NavigationConsumerResult member = await session.ExecuteAsync(session.Snapshot.Members[1].Navigation.Action!, TestContext.Current.CancellationToken);
        Assert.Equal("member.overview", member.Snapshot.LensOutcome.EffectiveLens!.Facet);
        string retainedMember = member.Snapshot.ActiveSubject.Id;
        NavigationConsumerResult package = await session.ExecuteAsync(session.Snapshot.Hierarchy[1].Action!, TestContext.Current.CancellationToken);
        Assert.Equal(StructuralSubjectKind.Package, package.Snapshot.ActiveSubject.Kind);
        Assert.Equal(retainedMember, package.Snapshot.Hierarchy[4].Subject!.Id);
        NavigationConsumerResult workspace = await session.ExecuteAsync(session.Snapshot.Hierarchy[0].Action!, TestContext.Current.CancellationToken);
        Assert.Equal(StructuralSubjectKind.Workspace, workspace.Snapshot.ActiveSubject.Kind);
        Assert.Equal(package.Snapshot.ActivePackage, workspace.Snapshot.ActivePackage);
        Assert.Equal(retainedMember, workspace.Snapshot.Hierarchy[4].Subject!.Id);
        NavigationConsumerResult library = await session.ExecuteAsync(session.Snapshot.Libraries[1].Navigation.Action!, TestContext.Current.CancellationToken);
        Assert.Equal(StructuralSubjectKind.Library, library.Snapshot.ActiveSubject.Kind);
        Assert.Equal("library.references", library.Snapshot.LensOutcome.EffectiveLens!.Facet);
        Assert.Null(library.Snapshot.Libraries[1].Navigation.Action);
    }

    [Fact]
    public async Task ExactLens_SameEffectiveLensInstallsExactBasisOnce()
    {
        await using Fixture fixture = await Fixture.CreateAsync();
        NavigationTestHost session = fixture.Session;
        NavigationLensIdentity lens = session.InstalledSnapshot.LensOutcome.EffectiveLens!;
        NavigationConsumerResult first = await session.ActivateLensAsync(lens, TestContext.Current.CancellationToken);
        Assert.Equal(NavigationOutcomeKind.Applied, first.Outcome.Kind);
        Assert.Equal(NavigationLensBasisKind.ExactRequest, first.Snapshot.LensOutcome.Basis);
        Assert.NotEqual(session.Initialization.Authority!.Revision, first.Authority!.Revision);
        fixture.Acknowledge(first);
        NavigationConsumerResult second = await session.ActivateLensAsync(lens, TestContext.Current.CancellationToken);
        Assert.Equal(first.Authority.Revision, second.Authority!.Revision);
        Assert.Same(first.Snapshot, second.Snapshot);
        Assert.NotEqual(first.Authority.Epoch, second.Authority.Epoch);
        Assert.Equal(NavigationSynchronizationDisposition.Current, second.Synchronization);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExactNonSuccess_InstallsEvidenceAndRefreshPreservesExactRequest(bool failed)
    {
        await using Fixture fixture = await Fixture.CreateAsync();
        NavigationTestHost session = fixture.Session;
        NavigationAction action = session.Snapshot.Lenses.First(row => row.Facet.Id == "library.metadata").Action!;
        ViewFacetAvailability availability = failed
            ? new ViewFacetAvailability.Failed("metadata failed", new Diagnostic())
            : new ViewFacetAvailability.Unavailable(ViewFacetUnavailableReason.CapabilityAbsent("metadata absent"));
        fixture.Override = id => id.Value == "library.metadata" ? availability : null;
        NavigationConsumerResult result = await session.ExecuteAsync(action, TestContext.Current.CancellationToken);
        Assert.Equal(failed ? NavigationOutcomeKind.Failed : NavigationOutcomeKind.Unavailable, result.Outcome.Kind);
        Assert.Equal(NavigationLensBasisKind.ExactRequest, result.Snapshot.LensOutcome.Basis);
        Assert.Null(result.Snapshot.LensOutcome.EffectiveLens);
        Assert.Equal("library.metadata", result.Snapshot.LensOutcome.Request!.Facet);
        Assert.NotEqual(session.Initialization.Authority!.Revision, result.Authority!.Revision);
        Assert.Null(result.Snapshot.Lenses.First(row => row.Facet.Id == "library.metadata").Action);
        NavigationLensIdentity exact = ((NavigationLensEvaluationBasis.ExactRequest)
            session.InstalledSnapshot.LensOutcome.Basis).Request;
        NavigationConsumerResult unchanged = await session.ActivateLensAsync(exact, TestContext.Current.CancellationToken);
        Assert.Equal(result.Authority.Revision, unchanged.Authority!.Revision);
        Assert.Same(result.Snapshot, unchanged.Snapshot);
        fixture.Acknowledge(unchanged);
        fixture.Override = null;
        NavigationConsumerResult refreshed = await session.RefreshAsync(TestContext.Current.CancellationToken);
        Assert.Equal("library.metadata", refreshed.Snapshot.LensOutcome.EffectiveLens!.Facet);
        Assert.Equal(NavigationLensBasisKind.ExactRequest, refreshed.Snapshot.LensOutcome.Basis);
        Assert.NotEqual(unchanged.Authority.Revision, refreshed.Authority!.Revision);
    }

    [Fact]
    public async Task StaleForeignUnknownDuplicateAndSourceMismatch_RejectBeforePreparationOrRegistry()
    {
        await using Fixture fixture = await Fixture.CreateAsync();
        await using Fixture foreign = await Fixture.CreateAsync();
        NavigationTestHost session = fixture.Session;
        NavigationAction action = session.Snapshot.Types[0].Navigation.Action!;
        fixture.Prepare = _ => ValueTask.FromResult<NavigationPreparation>(
            new NavigationPreparation.Unavailable("not ready"));
        NavigationConsumerResult unavailable = await session.ExecuteAsync(action, TestContext.Current.CancellationToken);
        NavigationWorkspaceSnapshot installed = session.InstalledSnapshot;
        fixture.Prepare = _ => throw new InvalidOperationException("No provider or Registry work is allowed.");
        foreach ((NavigationAction invalid, NavigationRejectionKind expected) in new[]
        {
            (action, NavigationRejectionKind.DuplicateAction),
            (foreign.Session.Snapshot.Types[0].Navigation.Action!, NavigationRejectionKind.ForeignSession),
            (action with { Generation = "old" }, NavigationRejectionKind.StaleGeneration),
            (session.Snapshot.Types[0].Navigation.Action! with { Id = "unknown" }, NavigationRejectionKind.UnknownAction),
            (session.Snapshot.Types[1].Navigation.Action! with { Source = "other" }, NavigationRejectionKind.SourceMismatch),
            (session.Snapshot.Types[1].Navigation.Action! with { Kind = NavigationOperationKind.Package }, NavigationRejectionKind.InvalidAction),
        })
        {
            NavigationConsumerResult rejected = await session.ExecuteAsync(invalid, TestContext.Current.CancellationToken);
            Assert.Equal(NavigationOutcomeKind.Rejected, rejected.Outcome.Kind);
            Assert.Equal(expected, rejected.Outcome.Rejection);
            Assert.Same(installed, session.InstalledSnapshot);
            Assert.Same(unavailable.Snapshot, rejected.Snapshot);
            Assert.Equal(unavailable.Authority!.Revision, rejected.Authority!.Revision);
            Assert.True(session.ValidateAuthority(rejected.Authority));
        }
    }

    [Fact]
    public async Task EarlierGenerationAction_RemainsStaleAfterReturningToSameSubject()
    {
        await using Fixture fixture = await Fixture.CreateAsync();
        NavigationAction old = fixture.Session.Snapshot.Types[0].Navigation.Action!;
        string initialSubject = fixture.Session.Snapshot.ActiveSubject.Id;
        await fixture.Session.ExecuteAsync(fixture.Session.Snapshot.Types[1].Navigation.Action!, TestContext.Current.CancellationToken);
        await fixture.Session.ExecuteAsync(fixture.Session.Snapshot.Libraries[1].Navigation.Action!, TestContext.Current.CancellationToken);
        Assert.Equal(initialSubject, fixture.Session.Snapshot.ActiveSubject.Id);
        fixture.Prepare = _ => throw new InvalidOperationException("Stale action must not gather.");
        NavigationConsumerResult rejected = await fixture.Session.ExecuteAsync(old, TestContext.Current.CancellationToken);
        Assert.Equal(NavigationRejectionKind.StaleGeneration, rejected.Outcome.Rejection);
    }

    [Fact]
    public async Task StructuredLens_ForeignOrDifferentSubjectRejectsBeforeRegistry()
    {
        await using Fixture fixture = await Fixture.CreateAsync();
        await using Fixture foreign = await Fixture.CreateAsync();
        fixture.Prepare = _ => throw new InvalidOperationException("Invalid subject must not gather.");
        NavigationWorkspaceSnapshot installed = fixture.Session.InstalledSnapshot;
        foreach (StructuralSubjectIdentity subject in new[]
        {
            fixture.Session.InstalledSnapshot.Types[0].Row.Subject,
            foreign.Session.InstalledSnapshot.Types[0].Row.Subject,
        })
        {
            NavigationConsumerResult result = await fixture.Session.ActivateLensAsync(
                new(subject, new ViewFacetId("type.api")), TestContext.Current.CancellationToken);
            Assert.Equal(NavigationOutcomeKind.Rejected, result.Outcome.Kind);
            Assert.Equal("type.api", result.Outcome.Request!.Lens!.Facet);
            Assert.Equal(StructuralSubjectKind.Type, result.Outcome.Request.Destination.Kind);
            Assert.Equal(fixture.Session.Snapshot.ActiveSubject.Id, result.Outcome.Request.Source.Id);
            Assert.Same(installed, fixture.Session.InstalledSnapshot);
        }
    }

    [Fact]
    public async Task DescendantPair_UsesExactDefiningLibraryThenExactDeclaringType()
    {
        await using Fixture fixture = await Fixture.CreateAsync();
        NavigationTestHost session = fixture.Session;
        await session.ExecuteAsync(session.Snapshot.Libraries[0].Navigation.Action!, TestContext.Current.CancellationToken);
        Assert.True(session.Snapshot.Libraries[0].IsAggregate);
        Assert.Equal(session.Snapshot.Types[0].Navigation.Subject!.Label,
            session.Snapshot.Types[1].Navigation.Subject!.Label);
        NavigationConsumerTypeDescriptor row = session.Snapshot.Types[1];
        NavigationConsumerLensDescriptor lens = row.DescendantLenses.First(item => item.Facet.Id == "type.compare");
        NavigationConsumerResult type = await session.ExecuteAsync(lens.Action!, TestContext.Current.CancellationToken);
        Assert.Equal(NavigationOutcomeKind.Applied, type.Outcome.Kind);
        Assert.Equal(row.Navigation.Subject!.Id, type.Snapshot.ActiveSubject.Id);
        Assert.Equal(row.Library, type.Snapshot.TypeInventoryLibraryContext!.Id);
        Assert.Equal(lens.Target, type.Snapshot.LensOutcome.EffectiveLens);
        Assert.Equal(type.Outcome.Request!.Lens, type.Snapshot.LensOutcome.EffectiveLens);
        NavigationConsumerMemberDescriptor member = type.Snapshot.Members[1];
        NavigationConsumerResult result = await session.ExecuteAsync(
            member.DescendantLenses.First(item => item.Facet.Id == "member.compare").Action!, TestContext.Current.CancellationToken);
        Assert.Equal(member.Navigation.Subject!.Id, result.Snapshot.ActiveSubject.Id);
        Assert.Equal("member.compare", result.Snapshot.LensOutcome.EffectiveLens!.Facet);
        Assert.Equal(member.DeclaringType, result.Snapshot.Hierarchy[3].Subject!.Id);
        Assert.Empty(result.Snapshot.Types.SelectMany(item => item.DescendantLenses));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DescendantNonSuccess_InstallsNeitherHalf(bool failed)
    {
        await using Fixture fixture = await Fixture.CreateAsync();
        NavigationTestHost session = fixture.Session;
        NavigationAction action = session.Snapshot.Types[0].DescendantLenses
            .First(item => item.Facet.Id == "type.compare").Action!;
        NavigationWorkspaceSnapshot installed = session.InstalledSnapshot;
        ViewFacetAvailability unavailable = failed
            ? new ViewFacetAvailability.Failed("compare failed", new Diagnostic())
            : new ViewFacetAvailability.Unavailable(ViewFacetUnavailableReason.CapabilityAbsent("compare absent"));
        fixture.Override = id => id.Value == "type.compare" ? unavailable : null;
        NavigationConsumerResult result = await session.ExecuteAsync(action, TestContext.Current.CancellationToken);
        Assert.Equal(failed ? NavigationOutcomeKind.Failed : NavigationOutcomeKind.Unavailable, result.Outcome.Kind);
        Assert.Same(installed, session.InstalledSnapshot);
        Assert.NotEqual(session.Initialization.Snapshot.Generation, result.Snapshot.Generation);
        Assert.Equal(session.Initialization.Snapshot.ActiveSubject, result.Snapshot.ActiveSubject);
        Assert.Equal(session.Initialization.Snapshot.LensOutcome, result.Snapshot.LensOutcome);
        Assert.Equal(session.Initialization.Authority!.Revision, result.Authority!.Revision);
        Assert.Equal("type.compare", result.Outcome.Request!.Lens!.Facet);
        Assert.Equal(failed ? NavigationResolutionKind.Failed : NavigationResolutionKind.Unavailable,
            result.Outcome.Resolution!.Kind);
    }

    [Fact]
    public async Task NonDescendantRows_HaveNoPairActions()
    {
        await using Fixture fixture = await Fixture.CreateAsync();
        Assert.NotEmpty(fixture.Session.Snapshot.Types[0].DescendantLenses);
        Assert.Empty(fixture.Session.Snapshot.Types[1].DescendantLenses);
        Assert.All(fixture.Session.Snapshot.Members, row => Assert.Empty(row.DescendantLenses));
        await fixture.Session.ExecuteAsync(fixture.Session.Snapshot.Types[1].Navigation.Action!, TestContext.Current.CancellationToken);
        Assert.Empty(fixture.Session.Snapshot.Members[0].DescendantLenses);
        Assert.NotEmpty(fixture.Session.Snapshot.Members[1].DescendantLenses);
    }

    [Fact]
    public async Task LatestAdmittedExplicitIntent_SupersedesOlderCompletion()
    {
        await using Fixture fixture = await Fixture.CreateAsync();
        NavigationTestHost session = fixture.Session;
        var pending = new TaskCompletionSource<NavigationPreparation>(TaskCreationOptions.RunContinuationsAsynchronously);
        NavigationAction old = session.Snapshot.Types[0].Navigation.Action!;
        NavigationAction latest = session.Snapshot.Types[1].Navigation.Action!;
        fixture.Prepare = request => request.Request == old.Id
            ? new(pending.Task) : ValueTask.FromResult(fixture.Ready(request));
        Task<NavigationConsumerResult> older = session.ExecuteAsync(old, TestContext.Current.CancellationToken).AsTask();
        Assert.False(session.ValidateAuthority(session.Initialization.Authority));
        NavigationConsumerResult newer = await session.ExecuteAsync(latest, TestContext.Current.CancellationToken);
        NavigationWorkspaceSnapshot installed = session.InstalledSnapshot;
        pending.SetResult(fixture.Ready(fixture.LastRequest!));
        NavigationConsumerResult superseded = await older.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        Assert.Equal(NavigationOutcomeKind.Superseded, superseded.Outcome.Kind);
        Assert.Null(superseded.Authority);
        Assert.Same(installed, session.InstalledSnapshot);
        Assert.Same(newer.Snapshot, superseded.Snapshot);
        Assert.True(session.ValidateAuthority(newer.Authority));
    }

    [Fact]
    public async Task Maintenance_RequestOrderAndUnconsumedAuthorityBlockInstallation()
    {
        await using Fixture fixture = await Fixture.CreateAsync();
        NavigationTestHost session = fixture.Session;
        int calls = 0;
        var firstFacts = new TaskCompletionSource<NavigationPreparation>(TaskCreationOptions.RunContinuationsAsynchronously);
        NavigationEvaluationRequest? firstRequest = null;
        fixture.Prepare = request =>
        {
            calls++;
            if (calls == 1)
            {
                firstRequest = request;
                return new(firstFacts.Task);
            }
            return ValueTask.FromResult(fixture.Ready(request));
        };
        Task<NavigationConsumerResult> first = session.RefreshAsync(TestContext.Current.CancellationToken).AsTask();
        Task<NavigationConsumerResult> second = session.RefreshAsync(TestContext.Current.CancellationToken).AsTask();
        Assert.Equal(0, calls);
        Assert.False(first.IsCompleted);
        fixture.Acknowledge(session.Initialization);
        await fixture.WaitForRequestAsync();
        Assert.Equal(1, calls);
        Assert.NotNull(firstRequest);
        firstFacts.SetResult(fixture.Ready(firstRequest!));
        NavigationConsumerResult one = await first.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        Assert.Equal(firstRequest!.Request, one.Request);
        Assert.Equal(1, calls);
        Assert.False(second.IsCompleted);
        fixture.Acknowledge(one);
        NavigationConsumerResult two = await second.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        Assert.NotEqual(one.Request, two.Request);
        Assert.Equal(2, calls);
        Assert.Equal(one.Authority!.Revision, two.Authority!.Revision);
    }

    [Fact]
    public async Task Maintenance_InvalidatedByExplicitIntentRegathersSameRequestFromInstalledState()
    {
        await using Fixture fixture = await Fixture.CreateAsync();
        NavigationTestHost session = fixture.Session;
        fixture.Acknowledge(session.Initialization);
        var gathered = new TaskCompletionSource<NavigationPreparation>(TaskCreationOptions.RunContinuationsAsynchronously);
        var requests = new List<NavigationEvaluationRequest>();
        fixture.Prepare = request =>
        {
            if (request.Operation == NavigationOperationKind.Maintenance)
            {
                requests.Add(request);
                if (requests.Count == 1)
                    return new(gathered.Task);
            }
            return ValueTask.FromResult(fixture.Ready(request));
        };
        Task<NavigationConsumerResult> maintenance = session.RefreshAsync(TestContext.Current.CancellationToken).AsTask();
        NavigationConsumerResult selected = await session.ExecuteAsync(session.Snapshot.Types[1].Navigation.Action!, TestContext.Current.CancellationToken);
        gathered.SetResult(fixture.Ready(requests[0]));
        Assert.False(maintenance.IsCompleted);
        Assert.Equal(NavigationAuthorityResult.Accepted, session.Abandon(selected.Authority!));
        NavigationConsumerResult refreshed = await maintenance.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        Assert.Equal(2, requests.Count);
        Assert.Same(requests[0].Identity, requests[1].Identity);
        Assert.Equal(requests[0].Request, requests[1].Request);
        Assert.NotSame(requests[0], requests[1]);
        Assert.NotEqual(requests[0].Attempt, requests[1].Attempt);
        Assert.NotEqual(requests[0].Intent, requests[1].Intent);
        Assert.Equal(selected.Snapshot.ActiveSubject.Id, refreshed.Snapshot.ActiveSubject.Id);
        Assert.Equal(selected.Authority!.Revision, refreshed.Authority!.Revision);
    }

    [Fact]
    public async Task Authority_RequiresExactSessionRevisionIntentAndEpoch()
    {
        await using Fixture fixture = await Fixture.CreateAsync();
        NavigationTestHost session = fixture.Session;
        NavigationEffectAuthority authority = session.Initialization.Authority!;
        foreach (NavigationEffectAuthority wrong in new[]
        {
            authority with { Session = "foreign" },
            authority with { Revision = "other" },
            authority with { Intent = "other" },
            authority with { Epoch = "other" },
        })
        {
            Assert.False(session.ValidateAuthority(wrong));
            Assert.Equal(NavigationAuthorityResult.InvalidAuthority, session.RecordConsumerInstallation(wrong));
            Assert.Equal(NavigationAuthorityResult.InvalidAuthority, session.Acknowledge(wrong));
            Assert.Equal(NavigationAuthorityResult.InvalidAuthority, session.Abandon(wrong));
        }
        Assert.True(session.ValidateAuthority(authority));
        Assert.Equal(NavigationAuthorityResult.InstallationRequired, session.Acknowledge(authority));
        Assert.Equal(NavigationAuthorityResult.Accepted, session.RecordConsumerInstallation(authority));
        Assert.True(session.ValidateAuthority(authority));
        Assert.Equal(NavigationAuthorityResult.Accepted, session.Acknowledge(authority));
        Assert.False(session.ValidateAuthority(authority));
        Assert.Equal(NavigationAuthorityResult.InvalidAuthority, session.Acknowledge(authority));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InstallationAndAbandonment_DoNotAdvanceReceipt(bool install)
    {
        await using Fixture fixture = await Fixture.CreateAsync();
        NavigationTestHost session = fixture.Session;
        fixture.Acknowledge(session.Initialization);
        NavigationConsumerResult applied = await session.ExecuteAsync(session.Snapshot.Types[0].Navigation.Action!, TestContext.Current.CancellationToken);
        if (install)
            Assert.Equal(NavigationAuthorityResult.Accepted, session.RecordConsumerInstallation(applied.Authority!));
        Assert.Equal(NavigationAuthorityResult.Accepted, session.Abandon(applied.Authority!));
        NavigationConsumerResult rejected = await session.ExecuteAsync(
            applied.Snapshot.Hierarchy[0].Action! with { Id = "unknown" }, TestContext.Current.CancellationToken);
        Assert.Equal(NavigationOutcomeKind.Rejected, rejected.Outcome.Kind);
        Assert.Equal(NavigationSynchronizationDisposition.SynchronizationRequired, rejected.Synchronization);
        Assert.Same(applied.Snapshot, rejected.Snapshot);
        Assert.Equal(NavigationAuthorityResult.InstallationRequired, session.Acknowledge(rejected.Authority!));
        fixture.Acknowledge(rejected);
        NavigationConsumerResult again = await session.ExecuteAsync(
            rejected.Snapshot.Hierarchy[0].Action! with { Id = "unknown" }, TestContext.Current.CancellationToken);
        Assert.Equal(NavigationOutcomeKind.Rejected, again.Outcome.Kind);
        Assert.Equal(NavigationSynchronizationDisposition.Current, again.Synchronization);
        Assert.Equal(NavigationAuthorityResult.InstallationRequired, session.Acknowledge(again.Authority!));
    }

    [Theory]
    [InlineData(NavigationOutcomeKind.Unavailable)]
    [InlineData(NavigationOutcomeKind.Failed)]
    [InlineData(NavigationOutcomeKind.Aborted)]
    public async Task SynchronizationDisposition_IsIndependentOfNonInstallingSemanticOutcome(NavigationOutcomeKind kind)
    {
        await using Fixture fixture = await Fixture.CreateAsync();
        NavigationTestHost session = fixture.Session;
        fixture.Prepare = _ => ValueTask.FromResult<NavigationPreparation>(kind switch
        {
            NavigationOutcomeKind.Unavailable => new NavigationPreparation.Unavailable("absent"),
            NavigationOutcomeKind.Failed => new NavigationPreparation.Failed("preparation failed"),
            _ => new NavigationPreparation.Aborted("prerequisite failed"),
        });
        NavigationConsumerResult result = await session.ExecuteAsync(session.Snapshot.Types[0].Navigation.Action!, TestContext.Current.CancellationToken);
        Assert.Equal(kind, result.Outcome.Kind);
        Assert.Equal(NavigationSynchronizationDisposition.SynchronizationRequired, result.Synchronization);
        Assert.Same(session.Initialization.Snapshot.ActiveSubject, result.Snapshot.ActiveSubject);
        Assert.NotEqual(session.Initialization.Snapshot.Generation, result.Snapshot.Generation);
        fixture.Acknowledge(result);
        NavigationConsumerResult current = await session.ExecuteAsync(session.Snapshot.Types[1].Navigation.Action!, TestContext.Current.CancellationToken);
        Assert.Equal(kind, current.Outcome.Kind);
        Assert.Equal(NavigationSynchronizationDisposition.SynchronizationRequired, current.Synchronization);
        Assert.Equal(result.Authority!.Revision, current.Authority!.Revision);
        fixture.Acknowledge(current);
        NavigationConsumerResult noProjectionChange = await session.ActivateLensAsync(
            session.InstalledSnapshot.LensOutcome.EffectiveLens!, TestContext.Current.CancellationToken);
        Assert.Equal(kind, noProjectionChange.Outcome.Kind);
        Assert.Equal(NavigationSynchronizationDisposition.Current, noProjectionChange.Synchronization);
    }

    [Fact]
    public async Task DedicatedSynchronization_RemountGetsLatestSnapshotAndFreshAuthorityRepeatedly()
    {
        await using Fixture fixture = await Fixture.CreateAsync();
        NavigationTestHost session = fixture.Session;
        NavigationConsumerResult applied = await session.ExecuteAsync(session.Snapshot.Types[1].Navigation.Action!, TestContext.Current.CancellationToken);
        session.Abandon(applied.Authority!);
        for (int remount = 0; remount < 3; remount++)
        {
            NavigationConsumerResult sync = await session.SynchronizeAsync(TestContext.Current.CancellationToken);
            Assert.Equal(NavigationOutcomeKind.Synchronized, sync.Outcome.Kind);
            Assert.Equal(NavigationSynchronizationDisposition.SynchronizationRequired, sync.Synchronization);
            Assert.Same(applied.Snapshot, sync.Snapshot);
            Assert.Equal(applied.Authority!.Revision, sync.Authority!.Revision);
            Assert.NotEqual(applied.Authority.Epoch, sync.Authority.Epoch);
            session.RecordConsumerInstallation(sync.Authority);
            if (remount == 2)
                Assert.Equal(NavigationAuthorityResult.Accepted, session.Acknowledge(sync.Authority));
            else
                Assert.Equal(NavigationAuthorityResult.Accepted, session.Abandon(sync.Authority));
        }
        NavigationConsumerResult current = await session.SynchronizeAsync(TestContext.Current.CancellationToken);
        Assert.Equal(NavigationSynchronizationDisposition.Current, current.Synchronization);
    }

    [Fact]
    public async Task Synchronization_WaitsForExplicitWorkAndQueuedMaintenance()
    {
        await using Fixture fixture = await Fixture.CreateAsync();
        NavigationTestHost session = fixture.Session;
        var explicitFacts = new TaskCompletionSource<NavigationPreparation>(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Prepare = request => request.Operation == NavigationOperationKind.Subject
            ? new(explicitFacts.Task) : ValueTask.FromResult(fixture.Ready(request));
        Task<NavigationConsumerResult> explicitTask = session.ExecuteAsync(session.Snapshot.Types[0].Navigation.Action!, TestContext.Current.CancellationToken).AsTask();
        NavigationEvaluationRequest explicitRequest = fixture.LastRequest!;
        Task<NavigationConsumerResult> maintenance = session.RefreshAsync(TestContext.Current.CancellationToken).AsTask();
        Task<NavigationConsumerResult> synchronization = session.SynchronizeAsync(TestContext.Current.CancellationToken).AsTask();
        Assert.False(maintenance.IsCompleted);
        Assert.False(synchronization.IsCompleted);
        explicitFacts.SetResult(fixture.Ready(explicitRequest));
        NavigationConsumerResult selected = await explicitTask;
        session.Abandon(selected.Authority!);
        NavigationConsumerResult refreshed = await maintenance.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        Assert.False(synchronization.IsCompleted);
        fixture.Acknowledge(refreshed);
        NavigationConsumerResult synced = await synchronization.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        Assert.Equal(refreshed.Snapshot.ActiveSubject.Id, synced.Snapshot.ActiveSubject.Id);
        Assert.Equal(NavigationSynchronizationDisposition.Current, synced.Synchronization);
    }

    [Fact]
    public async Task Reconciliation_UsesInstalledContextAndDoesNotPromoteAnAncestor()
    {
        await using Fixture fixture = await Fixture.CreateAsync();
        NavigationTestHost session = fixture.Session;
        await session.ExecuteAsync(session.Snapshot.Members[1].Navigation.Action!, TestContext.Current.CancellationToken);
        NavigationConsumerResult workspace = await session.ExecuteAsync(session.Snapshot.Hierarchy[0].Action!, TestContext.Current.CancellationToken);
        string retainedType = workspace.Snapshot.Hierarchy[3].Subject!.Id;
        fixture.Acknowledge(workspace);
        NavigationConsumerResult refresh = await session.RefreshAsync(TestContext.Current.CancellationToken);
        Assert.Equal(StructuralSubjectKind.Workspace, refresh.Snapshot.ActiveSubject.Kind);
        Assert.Equal(retainedType, refresh.Snapshot.Hierarchy[3].Subject!.Id);
        Assert.Equal(workspace.Authority!.Revision, refresh.Authority!.Revision);
        Assert.Same(workspace.Snapshot, refresh.Snapshot);
        Assert.All(typeof(NavigationTransitions).GetMethods().SelectMany(method => method.GetParameters()),
            parameter => Assert.NotEqual(typeof(NavigationWorkspaceSnapshot), parameter.ParameterType));
    }

    [Fact]
    public async Task JsonContract_RoundTripsExactResultUsingSourceGeneratedMetadata()
    {
        await using Fixture fixture = await Fixture.CreateAsync();
        NavigationConsumerResult result = fixture.Session.Initialization;
        string json = JsonSerializer.Serialize(result, NavigationConsumerJsonContext.Default.NavigationConsumerResult);
        NavigationConsumerResult copy = JsonSerializer.Deserialize(
            json, NavigationConsumerJsonContext.Default.NavigationConsumerResult)!;
        Assert.Equal(json, JsonSerializer.Serialize(copy, NavigationConsumerJsonContext.Default.NavigationConsumerResult));
        Assert.Equal(result.Authority, copy.Authority);
        Assert.Equal(result.Snapshot.Types[0].Navigation.Action, copy.Snapshot.Types[0].Navigation.Action);
        using JsonDocument document = JsonDocument.Parse(json);
        Assert.Equal(JsonValueKind.String, document.RootElement.GetProperty("snapshot").GetProperty("generation").ValueKind);
        foreach (JsonProperty part in document.RootElement.GetProperty("authority").EnumerateObject())
            Assert.Equal(JsonValueKind.String, part.Value.ValueKind);
        Assert.DoesNotContain("registration", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("producerRow", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("workspaceIdentity", json, StringComparison.OrdinalIgnoreCase);
        NavigationConsumerResult selected = await fixture.Session.ExecuteAsync(copy.Snapshot.Types[0].Navigation.Action!, TestContext.Current.CancellationToken);
        Assert.Equal(NavigationOutcomeKind.Applied, selected.Outcome.Kind);
    }

    [Fact]
    public async Task ExactUnknownOrInapplicableLens_RetainsSnapshotAndExactResolution()
    {
        await using Fixture fixture = await Fixture.CreateAsync();
        NavigationTestHost session = fixture.Session;
        NavigationWorkspaceSnapshot installed = session.InstalledSnapshot;
        foreach ((string id, NavigationResolutionKind kind) in new[]
        {
            ("library.unknown", NavigationResolutionKind.Unknown),
            ("type.api", NavigationResolutionKind.Inapplicable),
        })
        {
            NavigationConsumerResult result = await session.ActivateLensAsync(
                new(installed.ActiveSubject, new ViewFacetId(id)), TestContext.Current.CancellationToken);
            Assert.Equal(NavigationOutcomeKind.Rejected, result.Outcome.Kind);
            Assert.Equal(kind, result.Outcome.Resolution!.Kind);
            Assert.Equal(id, result.Outcome.Request!.Lens!.Facet);
            Assert.Same(installed, session.InstalledSnapshot);
            Assert.Equal(session.Initialization.Authority!.Revision, result.Authority!.Revision);
        }
    }

    [Fact]
    public async Task GenericReplacementHelpers_RejectForeignSubjectAndMismatchedLensBeforeRegistry()
    {
        await using Fixture fixture = await Fixture.CreateAsync();
        await using Fixture foreign = await Fixture.CreateAsync();
        NavigationWorkspaceSnapshot installed = fixture.Session.InstalledSnapshot;
        Assert.Throws<ArgumentException>(() => NavigationWorkspaceSnapshotEvaluation.WithSubject(
            installed, foreign.Session.InstalledSnapshot.ActiveSubject, InspectionViewFacetCatalog.Registry, ThrowingAvailability));
        NavigationLensOutcome foreignLens = foreign.Session.InstalledSnapshot.LensOutcome;
        Assert.Throws<ArgumentException>(() => NavigationWorkspaceSnapshotEvaluation.WithLensOutcome(
            installed, foreignLens, InspectionViewFacetCatalog.Registry, ThrowingAvailability));

        static IViewFacetAvailabilityFacts ThrowingAvailability(
            StructuralSubjectIdentity subject, NavigationSubjectInventory? inventory) =>
            throw new InvalidOperationException("Invalid exact identities must not query the Registry.");
    }

    [Fact]
    public async Task InventoryRefresh_RemovesMemberWithoutPromotingWorkspaceOrChoosingAnotherMember()
    {
        await using Fixture fixture = await Fixture.CreateAsync();
        NavigationTestHost session = fixture.Session;
        await session.ExecuteAsync(session.Snapshot.Members[1].Navigation.Action!, TestContext.Current.CancellationToken);
        NavigationConsumerResult workspace = await session.ExecuteAsync(
            session.Snapshot.Hierarchy[0].Action!, TestContext.Current.CancellationToken);
        string retainedType = workspace.Snapshot.Hierarchy[3].Subject!.Id;
        fixture.Acknowledge(workspace);
        fixture.SetTypes(0,
            [NavigationSnapshotTestData.Type("Widget", NavigationSnapshotTestData.Member("Run"))],
            [NavigationSnapshotTestData.Type("Widget", NavigationSnapshotTestData.Member("Different"))]);
        NavigationConsumerResult refresh = await session.RefreshAsync(TestContext.Current.CancellationToken);
        Assert.Equal(StructuralSubjectKind.Workspace, refresh.Snapshot.ActiveSubject.Kind);
        Assert.Equal(retainedType, refresh.Snapshot.Hierarchy[3].Subject!.Id);
        Assert.Null(refresh.Snapshot.Hierarchy[4].Subject);
        Assert.Equal(NavigationDescriptorState.SelectionRequired, refresh.Snapshot.Hierarchy[4].State);
        Assert.NotEqual(workspace.Authority!.Revision, refresh.Authority!.Revision);
        Assert.Equal(NavigationSynchronizationDisposition.SynchronizationRequired, refresh.Synchronization);
    }

    [Fact]
    public async Task InventoryRefresh_MissingTypeFallsBackOnlyWithinItsExactLibrary()
    {
        await using Fixture fixture = await Fixture.CreateAsync();
        NavigationTestHost session = fixture.Session;
        NavigationConsumerResult selected = await session.ExecuteAsync(
            session.Snapshot.Types[1].Navigation.Action!, TestContext.Current.CancellationToken);
        string library = selected.Snapshot.TypeInventoryLibraryContext!.Id;
        fixture.Acknowledge(selected);
        fixture.SetTypes(0,
            [NavigationSnapshotTestData.Type("Widget")],
            [NavigationSnapshotTestData.Type("Replacement")]);
        NavigationConsumerResult refresh = await session.RefreshAsync(TestContext.Current.CancellationToken);
        Assert.Equal(StructuralSubjectKind.Type, refresh.Snapshot.ActiveSubject.Kind);
        Assert.Equal("Sample.Replacement", refresh.Snapshot.ActiveSubject.Label);
        Assert.Equal(library, refresh.Snapshot.TypeInventoryLibraryContext!.Id);
        Assert.Equal(NavigationLensBasisKind.Recommendation, refresh.Snapshot.LensOutcome.Basis);
        Assert.Equal("type.api", refresh.Snapshot.LensOutcome.EffectiveLens!.Facet);
    }

    [Fact]
    public async Task InventoryRefresh_EquivalentFreshRowsDoNotAdvanceRevision()
    {
        await using Fixture fixture = await Fixture.CreateAsync();
        fixture.Acknowledge(fixture.Session.Initialization);
        fixture.SetTypes(0,
            [NavigationSnapshotTestData.Type("Widget", NavigationSnapshotTestData.Member("Run"))],
            [NavigationSnapshotTestData.Type("Widget", NavigationSnapshotTestData.Member("Run"))]);
        NavigationConsumerResult refreshed = await fixture.Session.RefreshAsync(TestContext.Current.CancellationToken);
        Assert.Equal(fixture.Session.Initialization.Authority!.Revision, refreshed.Authority!.Revision);
        Assert.Same(fixture.Session.Initialization.Snapshot, refreshed.Snapshot);
        Assert.Equal(NavigationSynchronizationDisposition.Current, refreshed.Synchronization);
    }

    [Fact]
    public async Task IndeterminateInventory_RetainsSnapshotAndReturnsPreparationFailureEvidence()
    {
        await using Fixture fixture = await Fixture.CreateAsync();
        NavigationTestHost session = fixture.Session;
        NavigationConsumerResult selected = await session.ExecuteAsync(
            session.Snapshot.Types[1].Navigation.Action!, TestContext.Current.CancellationToken);
        fixture.Acknowledge(selected);
        fixture.SetTypes(0, [], []);
        NavigationPackageEvaluation package = fixture.Packages[0];
        var error = new InvalidOperationException("surface failed");
        fixture.Packages[0] = new NavigationPackageEvaluation(
            package.Occurrence, fixture.Bindings[0], package.Libraries,
            new AssemblyContextApiSurfaceResult(
                new AssemblyContextResult<AssemblyApiSurface>(
                [
                    .. package.Libraries.Select(library =>
                        (AssemblyContextEntry<AssemblyApiSurface>)new AssemblyContextEntry<AssemblyApiSurface>.Failed(
                            new AssemblyContextSubject(library.Library.Participant.Assembly), error)),
                ]), [], Truncation: null));
        NavigationConsumerResult failed = await session.RefreshAsync(TestContext.Current.CancellationToken);
        Assert.Equal(NavigationOutcomeKind.Failed, failed.Outcome.Kind);
        Assert.Equal(NavigationFailureSource.Preparation, failed.Outcome.FailureSource);
        Assert.Same(selected.Snapshot, failed.Snapshot);
        Assert.Equal(selected.Authority!.Revision, failed.Authority!.Revision);
        Assert.Equal("surface failed", Assert.Single(failed.Outcome.Diagnostics).Message);
        Assert.Equal(NavigationSynchronizationDisposition.Current, failed.Synchronization);
    }

    [Fact]
    public async Task OmittedOccurrenceRefresh_SelectsWorkspaceRatherThanSibling()
    {
        await using Fixture fixture = await Fixture.CreateAsync();
        fixture.Acknowledge(fixture.Session.Initialization);
        WorkspacePackageOccurrenceDescriptor remaining = fixture.Scope.Packages[1];
        var revision = new WorkspaceScopeRevision(fixture.Workspace.Identity, [remaining.Occurrence]);
        var scope = new WorkspaceScopeSnapshot(
            revision, fixture.Scope.PhysicalComposition, [remaining],
            new WorkspaceClosureObservation(revision.Identity), preparing: null);
        fixture.Prepare = _ => ValueTask.FromResult<NavigationPreparation>(
            new NavigationPreparation.Ready(new(scope, null, fixture.Availability)));
        NavigationConsumerResult refreshed = await fixture.Session.RefreshAsync(TestContext.Current.CancellationToken);
        Assert.Equal(StructuralSubjectKind.Workspace, refreshed.Snapshot.ActiveSubject.Kind);
        Assert.Null(refreshed.Snapshot.ActivePackage);
        Assert.False(Assert.Single(refreshed.Snapshot.Packages).IsCurrent);
        Assert.Empty(refreshed.Snapshot.Types);
    }

    [Fact]
    public async Task WorkspaceProjection_PendingAndFailedPackagesHaveNoActionsOrProcessLocalRealization()
    {
        await using Fixture fixture = await Fixture.CreateAsync(selectPackage: false);
        var scope = new WorkspaceScopeSnapshot(
            fixture.Scope.Revision, fixture.Scope.PhysicalComposition,
            [
                new(fixture.Scope.Packages[0].Occurrence,
                    fixture.Scope.Packages[0].Realization with { Status = new ArtifactRootRealizationStatus.Pending() }),
                new(fixture.Scope.Packages[1].Occurrence,
                    fixture.Scope.Packages[1].Realization with
                    {
                        Status = new ArtifactRootRealizationStatus.Failed(ArtifactRootFailure.PreparationFailed),
                    }),
            ],
            fixture.Scope.Closure, preparing: null);
        fixture.Prepare = _ => ValueTask.FromResult<NavigationPreparation>(
            new NavigationPreparation.Ready(new(scope, null, fixture.Availability)));
        fixture.Acknowledge(fixture.Session.Initialization);
        NavigationConsumerResult refreshed = await fixture.Session.RefreshAsync(TestContext.Current.CancellationToken);
        Assert.All(refreshed.Snapshot.Packages, row => Assert.Null(row.Action));
        Assert.Equal(NavigationRealizationKind.Pending, refreshed.Snapshot.Packages[0].Realization);
        Assert.Equal(NavigationDescriptorState.Pending, refreshed.Snapshot.Packages[0].State);
        Assert.Equal(NavigationRealizationKind.Failed, refreshed.Snapshot.Packages[1].Realization);
        Assert.Equal(ArtifactRootFailure.PreparationFailed, refreshed.Snapshot.Packages[1].RealizationFailure);
        string json = JsonSerializer.Serialize(refreshed, NavigationConsumerJsonContext.Default.NavigationConsumerResult);
        Assert.Contains("\"realization\":\"Pending\"", json);
        Assert.DoesNotContain("artifact", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ConsumerResultGraph_ContainsOnlyTransportValues()
    {
        var visited = new HashSet<Type>();
        Inspect(typeof(NavigationConsumerResult));

        void Inspect(Type type)
        {
            if (!visited.Add(type) || type.IsEnum
                || type == typeof(string) || type == typeof(int) || type == typeof(bool))
            {
                return;
            }
            if (type.IsGenericType
                && (type.GetGenericTypeDefinition() == typeof(ImmutableArray<>)
                    || type.GetGenericTypeDefinition() == typeof(Nullable<>)))
            {
                Inspect(type.GetGenericArguments()[0]);
                return;
            }
            Assert.True(type.Name.StartsWith("NavigationConsumer", StringComparison.Ordinal)
                || type == typeof(NavigationAction) || type == typeof(NavigationEffectAuthority),
                $"Unexpected process-local or non-transport property type: {type}");
            foreach (var property in type.GetProperties())
                Inspect(property.PropertyType);
        }
    }

    [Fact]
    public async Task CancelledExplicitIntent_ReturnsAbortAuthorityAndReleasesMaintenanceAfterConsumption()
    {
        await using Fixture fixture = await Fixture.CreateAsync();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        await cancellation.CancelAsync();
        NavigationConsumerResult aborted = await fixture.Session.ExecuteAsync(
            fixture.Session.Snapshot.Types[0].Navigation.Action!, cancellation.Token);
        Assert.Equal(NavigationOutcomeKind.Aborted, aborted.Outcome.Kind);
        Assert.Null(fixture.LastRequest);
        Task<NavigationConsumerResult> maintenance = fixture.Session.RefreshAsync(TestContext.Current.CancellationToken).AsTask();
        Assert.False(maintenance.IsCompleted);
        fixture.Acknowledge(aborted);
        NavigationConsumerResult refreshed = await maintenance.WaitAsync(
            TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        Assert.Equal(NavigationOperationKind.Maintenance, refreshed.Operation);
    }

    [Fact]
    public async Task CancelledQueuedMaintenance_SettlesOnlyItsOwnRequest()
    {
        await using Fixture fixture = await Fixture.CreateAsync();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        Task<NavigationConsumerResult> cancelled = fixture.Session.RefreshAsync(cancellation.Token).AsTask();
        Task<NavigationConsumerResult> next = fixture.Session.RefreshAsync(TestContext.Current.CancellationToken).AsTask();
        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelled);
        Assert.False(next.IsCompleted);
        fixture.Acknowledge(fixture.Session.Initialization);
        NavigationConsumerResult result = await next.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        Assert.Equal(NavigationOperationKind.Maintenance, result.Operation);
    }

    sealed record Diagnostic : IViewFacetDiagnosticEvidence;

    internal sealed class Fixture : IAsyncDisposable
    {
        readonly TaskCompletionSource _requested = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public required InspectionWorkspace Workspace { get; init; }
        public required WorkspaceScopeSnapshot Scope { get; init; }
        public required NavigationPackageEvaluation[] Packages { get; init; }
        public required PackageRootBinding[] Bindings { get; init; }
        public NavigationTestHost Session { get; private set; } = null!;
        public NavigationEvaluationRequest? LastRequest { get; private set; }
        public int RequestCount { get; private set; }
        public Func<NavigationEvaluationRequest, ValueTask<NavigationPreparation>>? Prepare { get; set; }
        public Func<ViewFacetId, ViewFacetAvailability?>? Override { get; set; }
        public ViewFacetRegistry Registry { get; init; } = InspectionViewFacetCatalog.Registry;

        public static async Task<Fixture> CreateAsync(
            bool selectPackage = true, bool repeatedCoordinates = false, ViewFacetRegistry? registry = null)
        {
            var workspace = new InspectionWorkspace();
            byte[] firstImage = await File.ReadAllBytesAsync(
                typeof(NavigationSessionTests).Assembly.Location, TestContext.Current.CancellationToken);
            byte[] secondImage = await File.ReadAllBytesAsync(
                typeof(ApiType).Assembly.Location, TestContext.Current.CancellationToken);
            PackageRootBinding[] bindings =
            [
                NavigationSnapshotTestData.BindingWithAssemblyImages("Navigation.First", "net11.0",
                    ("Primary", firstImage), ("Secondary", secondImage)),
                NavigationSnapshotTestData.BindingWithAssemblyImages("Navigation.Second", "net11.0",
                    ("Primary", firstImage), ("Secondary", secondImage)),
            ];
            WorkspaceScopeSnapshot scope = await NavigationSnapshotTestData.ReplaceAsync(workspace, bindings);
            if (repeatedCoordinates)
            {
                // Scope's current admission policy coalesces coordinates. Exercise
                // Navigation's occurrence contract with two exact owner-type instances.
                WorkspacePackageOccurrence first = scope.Packages[0].Occurrence;
                var second = new WorkspacePackageOccurrence(
                    workspace.Identity, first.Package, first.Correspondence);
                var revision = new WorkspaceScopeRevision(workspace.Identity, [first, second]);
                scope = new WorkspaceScopeSnapshot(
                    revision, scope.PhysicalComposition,
                    [scope.Packages[0], new(second, scope.Packages[0].Realization)],
                    new WorkspaceClosureObservation(revision.Identity), preparing: null);
                bindings[1] = bindings[0];
            }
            var fixture = new Fixture
            {
                Workspace = workspace,
                Scope = scope,
                Bindings = bindings,
                Registry = registry ?? InspectionViewFacetCatalog.Registry,
                Packages =
                [
                    .. bindings.Select((binding, index) => NavigationSnapshotTestData.PackageEvaluation(
                        scope.Packages[index], binding,
                        NavigationSnapshotTestData.Surface("Primary",
                            NavigationSnapshotTestData.Type("Widget", NavigationSnapshotTestData.Member("Run"))),
                        NavigationSnapshotTestData.Surface("Secondary",
                            NavigationSnapshotTestData.Type("Widget", NavigationSnapshotTestData.Member("Run"))))),
                ],
            };
            fixture.Session = new(
                workspace.Identity,
                new(scope, selectPackage ? fixture.Packages[0] : null, fixture.Availability),
                fixture.Registry, fixture.ProvideAsync);
            return fixture;
        }

        public IViewFacetAvailabilityFacts Availability(StructuralSubjectIdentity subject, NavigationSubjectInventory? inventory) =>
            new ViewFacetAvailabilitySnapshot(Registry.Descriptors.Select(descriptor =>
                new ViewFacetAvailabilityFact(descriptor.Id,
                    Override?.Invoke(descriptor.Id) ?? ViewFacetAvailability.Available.Instance)));

        public void SetTypes(int packageIndex, params ApiType[][] types)
        {
            NavigationPackageEvaluation package = Packages[packageIndex];
            Packages[packageIndex] = new NavigationPackageEvaluation(
                package.Occurrence, Bindings[packageIndex], package.Libraries,
                new AssemblyContextApiSurfaceResult(
                    new AssemblyContextResult<AssemblyApiSurface>(
                    [
                        .. package.Libraries.Select((library, index) =>
                            (AssemblyContextEntry<AssemblyApiSurface>)new AssemblyContextEntry<AssemblyApiSurface>.Available(
                                new AssemblyContextSubject(library.Library.Participant.Assembly),
                                new AssemblyApiSurface(new ApiSurface { Types = [.. types[index]] }, []))),
                    ]), [], Truncation: null));
        }

        ValueTask<NavigationPreparation> ProvideAsync(
            NavigationEvaluationRequest request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            RequestCount++;
            ValueTask<NavigationPreparation> result =
                Prepare?.Invoke(request) ?? ValueTask.FromResult(Ready(request));
            _requested.TrySetResult();
            return result;
        }

        public NavigationPreparation Ready(NavigationEvaluationRequest request) =>
            new NavigationPreparation.Ready(new(
                Scope,
                request.Occurrence is null ? null : Packages.Single(package => package.Occurrence.Occurrence == request.Occurrence),
                Availability));

        public Task WaitForRequestAsync() => _requested.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        public void Acknowledge(NavigationConsumerResult result)
        {
            Assert.Equal(NavigationAuthorityResult.Accepted, Session.RecordConsumerInstallation(result.Authority!));
            Assert.Equal(NavigationAuthorityResult.Accepted, Session.Acknowledge(result.Authority!));
        }

        public ValueTask DisposeAsync() => Workspace.DisposeAsync();
    }
}
