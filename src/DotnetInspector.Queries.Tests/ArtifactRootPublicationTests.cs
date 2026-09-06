using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.CompilerServices;

using DotnetInspector.Packages;
using ILInspector.Metadata;
using NuGetFetch;

namespace DotnetInspector.Queries.Tests;

public sealed class ArtifactRootPublicationTests
{
    static readonly PackageAssemblyContextRealizationOptions Options = new()
    {
        MaxAggregateRetainedImageBytes = 16 * 1024 * 1024,
        MaxAssemblyEntryBytes = 4 * 1024 * 1024,
        MaxAssembliesPerRole = 8,
    };

    [Fact]
    public async Task PackageArtifactRootPreparation_DefaultOptionsAdmitTwoSmallPackages()
    {
        await using InspectionWorkspace workspace = InspectionWorkspace.CreateAsynchronous();
        var scope = new ScopeState(workspace.Identity);
        var authority = Authority(workspace);
        ArtifactRootResult<ArtifactRootPreparationReceipt> result =
            await workspace.PreparePackageArtifactRootsAsync(
                authority, [Binding("Default.First"), Binding("Default.Second")]);

        Assert.True(result is ArtifactRootResult<ArtifactRootPreparationReceipt>.Available,
            $"Expected the complete default batch to prepare; got {result}.");
        ArtifactRootPreparationReceipt receipt = Available(result);
        Assert.Equal(2, receipt.Entries.Length);
        ArtifactRootPublicationOutcome published = await workspace.PublishArtifactRootCompositionAsync(
            await Plan(workspace, scope, authority, [receipt]));
        Assert.Null(published.Failure);
        Assert.Equal(2, Assert.IsType<ArtifactRootPublishedComposition>(published.Published).Roots.Length);
    }

    [Fact]
    public async Task PackageArtifactRootPreparation_DefaultOptionsPrepareReplacementWhileOldRootRemainsCurrent()
    {
        await using InspectionWorkspace workspace = InspectionWorkspace.CreateAsynchronous();
        var scope = new ScopeState(workspace.Identity);
        var initialAuthority = Authority(workspace);
        ArtifactRootPreparationReceipt initialReceipt = Available(
            await workspace.PreparePackageArtifactRootsAsync(initialAuthority, [Binding("Default.Replace")]));
        ArtifactRootPublishedComposition initial = Assert.IsType<ArtifactRootPublishedComposition>(
            (await workspace.PublishArtifactRootCompositionAsync(
                await Plan(workspace, scope, initialAuthority, [initialReceipt]))).Published);
        ArtifactRootScopeProjection old = initial.Roots[0];
        using InspectionWorkspace.ArtifactRootQueryLease oldQuery = Available(
            await workspace.EnterArtifactRootQueryAsync(workspace.Identity, old.Correspondence, Ready(old)));
        var authority = Authority(workspace);

        ArtifactRootResult<ArtifactRootPreparationReceipt> result =
            await workspace.PreparePackageArtifactRootsAsync(authority, [Binding("Default.Replace")]);

        Assert.True(result is ArtifactRootResult<ArtifactRootPreparationReceipt>.Available,
            $"Expected the default replacement to prepare alongside the current Root; got {result}.");
        ArtifactRootPreparationReceipt receipt = Available(result);
        Assert.Same(initial.Composition, await Current(workspace));
        Assert.Same(initial.ScopeResult, scope.Current);
        Assert.Same(old, Available(await workspace.GetCurrentRootScopeProjectionAsync(workspace.Identity, old.Correspondence)));
        using (Stream image = oldQuery.Realization.SurfaceParticipants[0].Participant.Assembly.OpenRead())
            Assert.True(image.Length > 0);
        ArtifactRootPublishedComposition replacement = Assert.IsType<ArtifactRootPublishedComposition>(
            (await workspace.PublishArtifactRootCompositionAsync(
                await Plan(workspace, scope, authority, [receipt]))).Published);
        Assert.Equal(old.Correspondence, replacement.Roots[0].Correspondence);
        Assert.NotSame(Ready(old), Ready(replacement.Roots[0]));
    }

    [Fact]
    public async Task PackageArtifactRootPublication_ReplacesRetainsAndClearsCompleteComposition()
    {
        await using InspectionWorkspace workspace = InspectionWorkspace.CreateAsynchronous();
        var scope = new ScopeState(workspace.Identity);
        ArtifactRootPublishedComposition initial =
            await PublishPackages(workspace, scope, "First.Root", "Second.Root");
        ArtifactRootScopeProjection retained = initial.Roots[0];
        ArtifactRootScopeProjection removed = initial.Roots[1];
        ArtifactRootPreparationAuthority authority = Authority(workspace);
        ArtifactRootPreparationReceipt receipt =
            await Prepare(workspace, authority, Binding("Third.Root"));
        ArtifactRootPublicationOutcome replacement =
            await workspace.PublishArtifactRootCompositionAsync(new(
                authority, initial.Composition,
                [Retain(retained), Adopt(receipt)], [receipt],
                scope.Participant(authority)));
        Assert.Null(replacement.Failure);
        ArtifactRootPublishedComposition current = Assert.IsType<ArtifactRootPublishedComposition>(replacement.Published);
        Assert.Equal(2, current.Roots.Length);
        Assert.Same(Ready(retained), Ready(current.Roots[0]));
        Assert.Same(current.ScopeResult, scope.Current);
        Assert.Same(current.Composition, scope.Current!.Composition);
        Assert.Equal(current.Roots, scope.Current.Roots);
        Assert.Equal(ArtifactRootPreparationState.Published, receipt.State);
        Assert.Equal(ArtifactRootFailure.Absent, Rejected(await workspace.GetCurrentRootScopeProjectionAsync(
            workspace.Identity, removed.Correspondence)));
        Assert.Equal(ArtifactRootFailure.ArtifactGenerationMismatch, Rejected(
            await workspace.EnterArtifactRootQueryAsync(workspace.Identity, removed.Correspondence, Ready(removed))));
        var clearAuthority = Authority(workspace);
        ArtifactRootPublicationOutcome clear = await workspace.PublishArtifactRootCompositionAsync(new(
            clearAuthority, current.Composition, [], [], scope.Participant(clearAuthority)));
        Assert.NotNull(clear.Published);
        Assert.Empty(clear.Published.Roots);
        Assert.NotSame(current.Composition, clear.Published.Composition);
        Assert.Empty(scope.Current!.Roots);
    }

    [Fact]
    public async Task PackageArtifactRootPreparation_IsCompleteOrReleasesAll()
    {
        await using InspectionWorkspace workspace = InspectionWorkspace.CreateAsynchronous();
        PackageRootBinding good = Binding("Good.Root");
        PackageRootBinding bad = Binding("Bad.Root", malformed: true);
        ArtifactRootPreparationAuthority authority = Authority(workspace);
        Assert.Equal(ArtifactRootFailure.PreparationFailed, Rejected(
            await workspace.PreparePackageArtifactRootsAsync(authority, [good, bad], Options)));
        workspace.ConfigureArtifactRootAdmission(new()
        {
            MaxRoots = 1,
            MaxRetainedImageBytes = Options.MaxAggregateRetainedImageBytes,
        });
        ArtifactRootPreparationReceipt retry = await Prepare(workspace, authority, good);
        Assert.Equal(ArtifactRootReleaseOutcome.Released,
            await workspace.ReleaseArtifactRootPreparationAsync(retry));
        Assert.Empty((await workspace.CloseAsync()).ArtifactSessionCleanupFailures);
        Assert.Equal(ArtifactRootFailure.WorkspaceClosed, Rejected(
            await workspace.GetCurrentArtifactRootCompositionGenerationAsync(workspace.Identity)));
    }

    [Fact]
    public async Task PackageArtifactRootPreparation_PreservesRootOnlyAndExplicitEmptyInputs()
    {
        await using InspectionWorkspace workspace = InspectionWorkspace.CreateAsynchronous();
        var scope = new ScopeState(workspace.Identity);
        ArtifactRootPreparationAuthority authority = Authority(workspace);
        PackageRootBinding rootOnly = Binding("Root.Only", entry: "README.txt");
        PackageRootBinding empty = Binding("Empty.Group", entry: "ref/net11.0/_._");
        ArtifactRootPreparationReceipt receipt = await Prepare(workspace, authority, rootOnly, empty);
        Assert.Equal(2, receipt.Entries.Length);
        ArtifactRootPublicationOutcome published = await workspace.PublishArtifactRootCompositionAsync(
            await Plan(workspace, scope, authority, [receipt]));
        Assert.NotNull(published.Published);
        Assert.Equal(2, published.Published.Roots.Length);
        foreach (ArtifactRootScopeProjection projection in published.Published.Roots)
        {
            using InspectionWorkspace.ArtifactRootQueryLease access = Available(
                await workspace.EnterArtifactRootQueryAsync(workspace.Identity, projection.Correspondence, Ready(projection)));
            Assert.False(access.Realization.HasAssemblyContexts);
        }
    }

    [Fact]
    public async Task PackageArtifactRootPreparation_RejectsUnmatchedSelectionRatherThanDroppingInput()
    {
        await using InspectionWorkspace workspace = InspectionWorkspace.CreateAsynchronous();
        var authority = Authority(workspace);
        PackageRootBinding unmatched = Binding("No.Target", entry: "lib/net99.0/No.Target.dll");
        Assert.Equal(ArtifactRootFailure.PreparationFailed, Rejected(
            await workspace.PreparePackageArtifactRootsAsync(authority, [Binding("Good.Target"), unmatched], Options)));
    }

    [Fact]
    public async Task PackageArtifactRootPublication_ValidatesCompleteDesiredSetBeforeConsumption()
    {
        await using InspectionWorkspace workspace = InspectionWorkspace.CreateAsynchronous();
        var scope = new ScopeState(workspace.Identity);
        var authority = Authority(workspace);
        ArtifactRootPreparationReceipt receipt = await Prepare(
            workspace, authority, Binding("One.Root"), Binding("Two.Root"));
        ArtifactRootPublicationPlan valid = await Plan(workspace, scope, authority, [receipt]);
        ArtifactRootPublicationPlan[] malformed =
        [
            valid with { DesiredRoots = [Adopt(receipt)] },
            valid with { Preparations = [receipt, receipt] },
            valid with { Preparations = [] },
            valid with { DesiredRoots = [Adopt(receipt), Adopt(receipt)] },
            valid with { Authority = Authority(workspace) },
            valid with { DesiredRoots = [new ArtifactRootPublicationEntry.Adopt(receipt.Preparation, new())] },
        ];
        foreach (ArtifactRootPublicationPlan plan in malformed)
        {
            Assert.Equal(ArtifactRootFailure.Malformed,
                (await workspace.PublishArtifactRootCompositionAsync(plan)).Failure);
            Assert.Equal(ArtifactRootPreparationState.Prepared, receipt.State);
            Assert.Equal(0, scope.PrepareCalls);
        }
        Assert.NotNull((await workspace.PublishArtifactRootCompositionAsync(valid)).Published);
        Assert.Equal(1, scope.PrepareCalls);
    }

    [Fact]
    public async Task PackageArtifactRootPreparation_BindsExactWorkspaceCandidateAndDeadline()
    {
        await using InspectionWorkspace workspace = InspectionWorkspace.CreateAsynchronous();
        await using InspectionWorkspace foreign = InspectionWorkspace.CreateAsynchronous();
        var authority = Authority(workspace);
        var scope = new ScopeState(workspace.Identity);
        ArtifactRootPreparationReceipt receipt = await Prepare(workspace, authority, Binding("Exact.Root"));
        ArtifactRootPublicationPlan plan = await Plan(workspace, scope, authority, [receipt]);
        Assert.Equal(ArtifactRootFailure.ForeignWorkspace,
            (await foreign.PublishArtifactRootCompositionAsync(plan)).Failure);
        Assert.Equal(ArtifactRootReleaseOutcome.ForeignWorkspace,
            await foreign.ReleaseArtifactRootPreparationAsync(receipt));
        Assert.Equal(ArtifactRootPreparationState.Prepared, receipt.State);
        Assert.Equal(ArtifactRootFailure.Malformed,
            (await workspace.PublishArtifactRootCompositionAsync(plan with
            {
                Authority = new(workspace.Identity, authority.CandidateSet,
                    authority.Deadline.AddSeconds(1), authority.Cancellation),
            })).Failure);
        Assert.Equal(ArtifactRootPreparationState.Prepared, receipt.State);
        Assert.NotNull((await workspace.PublishArtifactRootCompositionAsync(plan)).Published);
    }

    [Fact]
    public async Task PackageArtifactRootPublication_IndependentBatchesHaveDistinctCandidatesAndPublishTogether()
    {
        await using InspectionWorkspace workspace = InspectionWorkspace.CreateAsynchronous();
        var authority = Authority(workspace);
        var scope = new ScopeState(workspace.Identity);
        ArtifactRootPreparationReceipt first = await Prepare(workspace, authority, Binding("Batch.One"));
        ArtifactRootPreparationReceipt second = await Prepare(workspace, authority, Binding("Batch.Two"));
        Assert.NotSame(first.CandidateSet, second.CandidateSet);
        Assert.NotSame(first.Preparation, second.Preparation);
        ArtifactRootPublicationOutcome result = await workspace.PublishArtifactRootCompositionAsync(
            await Plan(workspace, scope, authority, [first, second]));
        Assert.NotNull(result.Published);
        Assert.Equal(2, result.Published.Roots.Length);
        Assert.Equal(ArtifactRootPreparationState.Published, first.State);
        Assert.Equal(ArtifactRootPreparationState.Published, second.State);
    }

    [Fact]
    public async Task PackageArtifactRootPublication_DuplicateCorrespondencePreservesBothReceipts()
    {
        await using InspectionWorkspace workspace = InspectionWorkspace.CreateAsynchronous();
        var authority = Authority(workspace);
        var scope = new ScopeState(workspace.Identity);
        ArtifactRootPreparationReceipt first = await Prepare(workspace, authority, Binding("Duplicate.Root"));
        ArtifactRootPreparationReceipt second = await Prepare(workspace, authority, Binding("Duplicate.Root"));
        Assert.Equal(ArtifactRootFailure.Malformed, (await workspace.PublishArtifactRootCompositionAsync(
            await Plan(workspace, scope, authority, [first, second]))).Failure);
        Assert.Equal(ArtifactRootPreparationState.Prepared, first.State);
        Assert.Equal(ArtifactRootPreparationState.Prepared, second.State);
        Assert.Equal(0, scope.PrepareCalls);
    }

    [Fact]
    public async Task PackageArtifactRootPreparation_ReleaseIsIdempotentAndTerminal()
    {
        await using InspectionWorkspace workspace = InspectionWorkspace.CreateAsynchronous();
        var authority = Authority(workspace);
        ArtifactRootPreparationReceipt receipt = await Prepare(workspace, authority, Binding("Release.Root"));
        ArtifactRootPublicationPlan plan = await Plan(workspace, new(workspace.Identity), authority, [receipt]);
        Assert.Equal(ArtifactRootReleaseOutcome.Released, await workspace.ReleaseArtifactRootPreparationAsync(receipt));
        Assert.Equal(ArtifactRootReleaseOutcome.NoEffect, await workspace.ReleaseArtifactRootPreparationAsync(receipt));
        Assert.Equal(ArtifactRootFailure.PreparationReleased,
            (await workspace.PublishArtifactRootCompositionAsync(plan)).Failure);
        Assert.Empty(await receipt.Settlement.Task);
    }

    [Fact]
    public async Task PackageArtifactRootPublication_StalePhysicalCandidateReleasesCompleteBatch()
    {
        await using InspectionWorkspace workspace = InspectionWorkspace.CreateAsynchronous();
        var scope = new ScopeState(workspace.Identity);
        var authority = Authority(workspace);
        ArtifactRootPreparationReceipt receipt = await Prepare(workspace, authority, Binding("Stale.Root"));
        ArtifactRootPublicationPlan stale = await Plan(workspace, scope, authority, [receipt]);
        ArtifactRootPublishedComposition winner = await PublishPackages(workspace, scope, "Winner.Root");
        Assert.Equal(ArtifactRootFailure.CompositionMismatch,
            (await workspace.PublishArtifactRootCompositionAsync(stale)).Failure);
        Assert.Equal(ArtifactRootPreparationState.Released, receipt.State);
        Assert.Same(winner.Composition, await Current(workspace));
        Assert.Same(winner.ScopeResult, scope.Current);
    }

    [Fact]
    public async Task PackageArtifactRootPublication_ParticipantRefusalReleasesStaging()
    {
        await using InspectionWorkspace workspace = InspectionWorkspace.CreateAsynchronous();
        var scope = new ScopeState(workspace.Identity);
        ArtifactRootPublishedComposition old = await PublishPackages(workspace, scope, "Old.Root");
        var authority = Authority(workspace);
        ArtifactRootPreparationReceipt receipt = await Prepare(workspace, authority, Binding("Refused.Root"));
        var candidate = new Candidate(scope, authority) { Refusal = ArtifactRootFailure.Superseded };
        var participant = new ArtifactRootScopePublicationParticipant(candidate);
        var plan = new ArtifactRootPublicationPlan(authority, old.Composition, [Adopt(receipt)], [receipt], participant);
        Assert.Equal(ArtifactRootFailure.Superseded, (await workspace.PublishArtifactRootCompositionAsync(plan)).Failure);
        Assert.Equal(ArtifactRootPreparationState.Released, receipt.State);
        Assert.Same(old.Composition, await Current(workspace));
        Assert.Same(old.ScopeResult, scope.Current);
        Assert.NotNull(candidate.CandidateComposition);
        Assert.NotSame(old.Composition, candidate.CandidateComposition);
    }

    [Fact]
    public async Task PackageArtifactRootPublication_ReceiptFreePlanCommitsOrRefusesOnce()
    {
        await using InspectionWorkspace workspace = InspectionWorkspace.CreateAsynchronous();
        var scope = new ScopeState(workspace.Identity);
        var authority = Authority(workspace);
        var candidate = new Candidate(scope, authority);
        var equivalent = new Candidate(scope, authority);
        ArtifactRootPublicationPlan plan = new(authority, await Current(workspace), [], [],
            new(candidate));
        ArtifactRootPublicationOutcome result = await workspace.PublishArtifactRootCompositionAsync(plan);
        Assert.NotNull(result.Published);
        Assert.Same(candidate.CandidateComposition, result.Published.Composition);
        Assert.Equal(ArtifactRootFailure.ParticipantAlreadyConsumed,
            (await workspace.PublishArtifactRootCompositionAsync(plan with
            {
                ExpectedComposition = result.Published.Composition,
            })).Failure);
        Assert.Equal(ArtifactRootFailure.ScopeBaseMismatch,
            (await workspace.PublishArtifactRootCompositionAsync(plan with
            {
                ExpectedComposition = result.Published.Composition,
                Participant = new(equivalent),
            })).Failure);
        Assert.Equal(1, scope.Commits);
    }

    [Fact]
    public async Task PackageArtifactRootPublication_RetainOnlyAdvancesCompositionButNotRootGeneration()
    {
        await using InspectionWorkspace workspace = InspectionWorkspace.CreateAsynchronous();
        var scope = new ScopeState(workspace.Identity);
        ArtifactRootPublishedComposition first = await PublishPackages(workspace, scope, "Retained.Root");
        var authority = Authority(workspace);
        ArtifactRootPublicationOutcome retained = await workspace.PublishArtifactRootCompositionAsync(new(
            authority, first.Composition, [Retain(first.Roots[0])], [], scope.Participant(authority)));
        Assert.NotNull(retained.Published);
        Assert.NotSame(first.Composition, retained.Published.Composition);
        Assert.Same(Ready(first.Roots[0]), Ready(retained.Published.Roots[0]));
    }

    [Fact]
    public async Task PackageArtifactRootPublication_ReceiptStatePrecedenceReleasesOtherPreparedReceipts()
    {
        await using InspectionWorkspace workspace = InspectionWorkspace.CreateAsynchronous();
        var scope = new ScopeState(workspace.Identity);
        var authority = Authority(workspace);
        ArtifactRootPreparationReceipt published = await Prepare(workspace, authority, Binding("Published.Root"));
        Assert.NotNull((await workspace.PublishArtifactRootCompositionAsync(
            await Plan(workspace, scope, authority, [published]))).Published);
        ArtifactRootPreparationReceipt other = await Prepare(workspace, authority, Binding("Other.Root"));
        ArtifactRootPublicationPlan replay = await Plan(workspace, scope, authority, [published, other]);
        Assert.Equal(ArtifactRootFailure.PreparationAlreadyPublished,
            (await workspace.PublishArtifactRootCompositionAsync(replay)).Failure);
        Assert.Equal(ArtifactRootPreparationState.Released, other.State);
        Assert.Equal(ArtifactRootPreparationState.Published, published.State);
        Assert.Equal(ArtifactRootReleaseOutcome.PreparationAlreadyPublished,
            await workspace.ReleaseArtifactRootPreparationAsync(published));
    }

    [Fact]
    public async Task PackageArtifactRootPublication_CancellationAtFinalRecheckPreservesOldStates()
    {
        await using InspectionWorkspace workspace = InspectionWorkspace.CreateAsynchronous();
        using var cancellation = new CancellationTokenSource();
        var scope = new ScopeState(workspace.Identity);
        var authority = CancellableAuthority(workspace, cancellation.Token);
        ArtifactRootPreparationReceipt receipt = await Prepare(workspace, authority, Binding("Cancelled.Root"));
        ArtifactRootCompositionGenerationIdentity old = await Current(workspace);
        var candidate = new Candidate(scope, authority) { OnPrepare = cancellation.Cancel };
        ArtifactRootPublicationOutcome result = await workspace.PublishArtifactRootCompositionAsync(new(
            authority, old, [Adopt(receipt)], [receipt], new(candidate)));
        Assert.Equal(ArtifactRootFailure.Cancelled, result.Failure);
        Assert.Null(scope.Current);
        Assert.Same(old, await Current(workspace));
        Assert.Equal(ArtifactRootPreparationState.Released, receipt.State);
        Assert.Equal(0, scope.Commits);
    }

    [Fact]
    public async Task PackageArtifactRootPublication_CommittedCancellationCannotRewriteSuccess()
    {
        await using InspectionWorkspace workspace = InspectionWorkspace.CreateAsynchronous();
        using var cancellation = new CancellationTokenSource();
        var scope = new ScopeState(workspace.Identity);
        var authority = CancellableAuthority(workspace, cancellation.Token);
        ArtifactRootPreparationReceipt receipt = await Prepare(workspace, authority, Binding("Committed.Root"));
        ArtifactRootPublicationOutcome result = await workspace.PublishArtifactRootCompositionAsync(
            await Plan(workspace, scope, authority, [receipt]));
        cancellation.Cancel();
        Assert.NotNull(result.Published);
        Assert.Same(result.Published.Composition, await Current(workspace));
        Assert.Equal(ArtifactRootPreparationState.Published, receipt.State);
    }

    [Fact]
    public async Task PackageArtifactRootPublication_DeadlineWhileWaitingReleasesBeforeGateEntry()
    {
        await using InspectionWorkspace workspace = InspectionWorkspace.CreateAsynchronous();
        var time = new AdvancingTimeProvider();
        workspace.ConfigureArtifactRootAdmission(new(), time);
        var authority = new ArtifactRootPreparationAuthority(
            workspace.Identity, new(), time.GetUtcNow().AddMinutes(1), TestContext.Current.CancellationToken);
        var scope = new ScopeState(workspace.Identity);
        ArtifactRootPreparationReceipt receipt = await Prepare(workspace, authority, Binding("Waiting.Root"));
        ArtifactRootPublicationPlan plan = await Plan(workspace, scope, authority, [receipt]);
        using InspectionWorkspace.ArtifactRootCompositionReadLease read =
            Available(await workspace.ReadArtifactRootCompositionAsync(workspace.Identity));
        Task<ArtifactRootPublicationOutcome> waiting =
            workspace.PublishArtifactRootCompositionAsync(plan).AsTask();
        Assert.False(waiting.IsCompleted);
        time.Advance(TimeSpan.FromMinutes(2));
        await receipt.Settlement.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        Assert.Equal(ArtifactRootPreparationState.Released, receipt.State);
        read.Dispose();
        Assert.Equal(ArtifactRootFailure.PreparationReleased, (await waiting).Failure);
        Assert.Equal(0, scope.PrepareCalls);
        Assert.Same(plan.ExpectedComposition, await Current(workspace));
    }

    [Fact]
    public async Task PackageArtifactRootPublication_DeadlineAfterStagingDiscardsCandidateIdentity()
    {
        await using InspectionWorkspace workspace = InspectionWorkspace.CreateAsynchronous();
        var time = new AdvancingTimeProvider();
        workspace.ConfigureArtifactRootAdmission(new(), time);
        var authority = new ArtifactRootPreparationAuthority(
            workspace.Identity, new(), time.GetUtcNow().AddMinutes(1), TestContext.Current.CancellationToken);
        var scope = new ScopeState(workspace.Identity);
        ArtifactRootPreparationReceipt receipt = await Prepare(workspace, authority, Binding("Expired.Root"));
        var candidate = new Candidate(scope, authority)
        {
            OnPrepare = () => time.Advance(TimeSpan.FromMinutes(2)),
        };
        ArtifactRootCompositionGenerationIdentity original = await Current(workspace);
        Assert.Equal(ArtifactRootFailure.DeadlineExpired, (await workspace.PublishArtifactRootCompositionAsync(
            new(authority, original, [Adopt(receipt)], [receipt], new(candidate)))).Failure);
        Assert.NotSame(original, candidate.CandidateComposition);
        Assert.Same(original, await Current(workspace));
        Assert.Equal(ArtifactRootPreparationState.Released, receipt.State);
    }

    [Fact]
    public async Task PackageArtifactRootPublication_OldOrNewCompositionIsObserved()
    {
        await using InspectionWorkspace workspace = InspectionWorkspace.CreateAsynchronous();
        var scope = new ScopeState(workspace.Identity);
        var authority = Authority(workspace);
        ArtifactRootPreparationReceipt receipt = await Prepare(workspace, authority, Binding("Atomic.Root"));
        Task<ArtifactRootResult<ArtifactRootCompositionGenerationIdentity>>? read = null;
        Task<ArtifactRootReleaseOutcome>? release = null;
        var candidate = new Candidate(scope, authority)
        {
            OnPrepare = () =>
            {
                read = workspace.GetCurrentArtifactRootCompositionGenerationAsync(workspace.Identity).AsTask();
                Assert.False(read.IsCompleted);
                release = workspace.ReleaseArtifactRootPreparationAsync(receipt).AsTask();
            },
        };
        ArtifactRootPublicationOutcome result = await workspace.PublishArtifactRootCompositionAsync(new(
            authority, await Current(workspace), [Adopt(receipt)], [receipt], new(candidate)));
        Assert.NotNull(result.Published);
        Assert.Same(result.Published.Composition, Available(await read!));
        Assert.Same(result.Published.ScopeResult, scope.Current);
        Assert.Equal(ArtifactRootReleaseOutcome.PreparationPublishing, await release!);
    }

    [Fact]
    public async Task PackageArtifactRootPublication_RetirementStopsNewEntryAndDrainsLeases()
    {
        await using InspectionWorkspace workspace = InspectionWorkspace.CreateAsynchronous();
        var scope = new ScopeState(workspace.Identity);
        ArtifactRootPublishedComposition first = await PublishPackages(workspace, scope, "Drain.Root");
        ArtifactRootScopeProjection old = first.Roots[0];
        using InspectionWorkspace.ArtifactRootQueryLease query = Available(
            await workspace.EnterArtifactRootQueryAsync(workspace.Identity, old.Correspondence, Ready(old)));
        ResolvedAssemblyReference assembly = Assert.Single(query.Realization.SurfaceParticipants).Participant.Assembly;
        ArtifactRootPublishedComposition replacement = await PublishPackages(workspace, scope, "Drain.Root");
        Assert.Equal(old.Correspondence, replacement.Roots[0].Correspondence);
        Assert.NotSame(Ready(old), Ready(replacement.Roots[0]));
        Assert.Equal(ArtifactRootFailure.ArtifactGenerationMismatch, Rejected(
            await workspace.EnterArtifactRootQueryAsync(workspace.Identity, old.Correspondence, Ready(old),
                query.Realization.SurfaceGroup.BindingPolicyVersion)));
        using (Stream stream = assembly.OpenRead())
            Assert.True(stream.Length > 0);
        Task<InspectionWorkspaceCloseReport> close = workspace.CloseAsync();
        Assert.False(close.IsCompleted);
        query.Dispose();
        Assert.Empty((await close).ArtifactSessionCleanupFailures);
        Assert.Throws<ObjectDisposedException>(() => assembly.OpenRead());
    }

    [Fact]
    public async Task PackageArtifactRootProjection_RefreshReturnsCurrentPointInTimeStatus()
    {
        await using InspectionWorkspace workspace = InspectionWorkspace.CreateAsynchronous();
        var scope = new ScopeState(workspace.Identity);
        ArtifactRootPublishedComposition first = await PublishPackages(workspace, scope, "Status.Root");
        ArtifactRootScopeProjection ready = first.Roots[0];
        ArtifactRootCompositionGenerationIdentity pending = Available(
            await workspace.RetireArtifactRootAsync(ready.Correspondence, Ready(ready)));
        Assert.NotSame(first.Composition, pending);
        Assert.IsType<ArtifactRootRealizationStatus.Pending>(Available(
            await workspace.GetCurrentRootScopeProjectionAsync(workspace.Identity, ready.Correspondence)).Status);
        Assert.IsType<ArtifactRootRealizationStatus.Ready>(ready.Status);
        ArtifactRootCompositionGenerationIdentity failed = Available(
            await workspace.FailArtifactRootReplacementAsync(
                ready.Correspondence, pending, ArtifactRootFailure.PreparationFailed));
        Assert.NotSame(pending, failed);
        Assert.IsType<ArtifactRootRealizationStatus.Failed>(Available(
            await workspace.GetCurrentRootScopeProjectionAsync(workspace.Identity, ready.Correspondence)).Status);
        Assert.Equal(ArtifactRootFailure.ArtifactGenerationMismatch, Rejected(
            await workspace.EnterArtifactRootQueryAsync(workspace.Identity, ready.Correspondence, Ready(ready))));
        var authority = Authority(workspace);
        ArtifactRootPreparationReceipt receipt = await Prepare(workspace, authority, Binding("Status.Root"));
        ArtifactRootReplacementSettlement replacement = Available(
            await workspace.SettleArtifactRootReplacementAsync(authority, receipt, failed));
        Assert.Equal(ready.Correspondence, replacement.Root.Correspondence);
        Assert.NotSame(Ready(ready), Ready(replacement.Root));
        Assert.NotSame(failed, replacement.Composition);
        Assert.Same(replacement.Composition, await Current(workspace));
        Assert.Same(first.ScopeResult, scope.Current);
        Assert.Equal(ArtifactRootPreparationState.Published, receipt.State);
    }

    [Fact]
    public async Task PackageArtifactRootGenerationReference_StaleForeignAndUnknownPrecedePolicyChecks()
    {
        await using InspectionWorkspace workspace = InspectionWorkspace.CreateAsynchronous();
        await using InspectionWorkspace foreign = InspectionWorkspace.CreateAsynchronous();
        var scope = new ScopeState(workspace.Identity);
        ArtifactRootPublishedComposition first = await PublishPackages(workspace, scope, "Foreign.Root");
        ArtifactRootPublishedComposition other = await PublishPackages(foreign, new(foreign.Identity), "Foreign.Root");
        using InspectionWorkspace.ArtifactRootQueryLease otherQuery = Available(
            await foreign.EnterArtifactRootQueryAsync(foreign.Identity,
                other.Roots[0].Correspondence, Ready(other.Roots[0])));
        ArtifactRootPublishedComposition current = await PublishPackages(workspace, scope, "Foreign.Root");
        foreach (ArtifactRootGenerationReference generation in new[]
        {
            Ready(first.Roots[0]), Ready(other.Roots[0]), new(),
        })
        {
            Assert.Equal(ArtifactRootFailure.ArtifactGenerationMismatch, Rejected(
                await workspace.EnterArtifactRootQueryAsync(workspace.Identity,
                    current.Roots[0].Correspondence, generation,
                    otherQuery.Realization.SurfaceGroup.BindingPolicyVersion)));
        }
        Assert.Equal(ArtifactRootFailure.BindingPolicyMismatch, Rejected(
            await workspace.EnterArtifactRootQueryAsync(workspace.Identity,
                current.Roots[0].Correspondence, Ready(current.Roots[0]),
                otherQuery.Realization.SurfaceGroup.BindingPolicyVersion)));
    }

    [Fact]
    public async Task PackageArtifactRootPublication_RuntimeCloseRefusesWaitingPublicationAndDrainsPreparation()
    {
        await using InspectionWorkspace workspace = InspectionWorkspace.CreateAsynchronous();
        var scope = new ScopeState(workspace.Identity);
        var authority = Authority(workspace);
        ArtifactRootPreparationReceipt receipt = await Prepare(workspace, authority, Binding("Closing.Root"));
        ArtifactRootPublicationPlan plan = await Plan(workspace, scope, authority, [receipt]);
        using InspectionWorkspace.ArtifactRootCompositionReadLease read =
            Available(await workspace.ReadArtifactRootCompositionAsync(workspace.Identity));
        Task<ArtifactRootPublicationOutcome> waiting =
            workspace.PublishArtifactRootCompositionAsync(plan).AsTask();
        Task<InspectionWorkspaceCloseReport> close = workspace.CloseAsync();
        Assert.False(close.IsCompleted);
        read.Dispose();
        Assert.Contains((await waiting).Failure, new ArtifactRootFailure?[]
        {
            ArtifactRootFailure.PreparationReleased, ArtifactRootFailure.WorkspaceClosing,
        });
        Assert.Empty((await close).ArtifactSessionCleanupFailures);
        Assert.Equal(ArtifactRootPreparationState.Released, receipt.State);
        Assert.Equal(0, scope.Commits);
    }

    [Fact]
    public async Task PackageArtifactRootPublication_ActiveGroupWorkDrainsAfterItsEntryLeaseEnds()
    {
        await using InspectionWorkspace workspace = InspectionWorkspace.CreateAsynchronous();
        var scope = new ScopeState(workspace.Identity);
        ArtifactRootPublishedComposition first = await PublishPackages(workspace, scope, "Active.Root");
        using InspectionWorkspace.ArtifactRootQueryLease query = Available(
            await workspace.EnterArtifactRootQueryAsync(workspace.Identity,
                first.Roots[0].Correspondence, Ready(first.Roots[0])));
        var resume = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        ResolvedAssemblyReference assembly = query.Realization.SurfaceParticipants[0].Participant.Assembly;
        Task<AssemblyImageAccessResult<int>> operation =
            query.Realization.SurfaceGroup.UseAndReleaseAssemblySessionAsync(assembly, async (_, _) =>
            {
                entered.SetResult();
                await resume.Task.WaitAsync(TestContext.Current.CancellationToken);
                return 1;
            });
        await entered.Task.WaitAsync(TestContext.Current.CancellationToken);
        query.Dispose();
        await workspace.RetireArtifactRootAsync(first.Roots[0].Correspondence, Ready(first.Roots[0]));
        Task<InspectionWorkspaceCloseReport> close = workspace.CloseAsync();
        Assert.False(close.IsCompleted);
        resume.SetResult();
        Assert.IsType<AssemblyImageAccessResult<int>.Available>(await operation);
        Assert.Empty((await close).ArtifactSessionCleanupFailures);
        Assert.Throws<ObjectDisposedException>(() => assembly.OpenRead());
    }

    [Fact]
    public async Task PackageArtifactRootPublication_OpenArtifactStreamDrainsAfterRetirement()
    {
        await using InspectionWorkspace workspace = InspectionWorkspace.CreateAsynchronous();
        var scope = new ScopeState(workspace.Identity);
        ArtifactRootPublishedComposition first = await PublishPackages(workspace, scope, "Stream.Root");
        using InspectionWorkspace.ArtifactRootQueryLease query = Available(
            await workspace.EnterArtifactRootQueryAsync(workspace.Identity,
                first.Roots[0].Correspondence, Ready(first.Roots[0])));
        using Stream stream = query.Realization.SurfaceParticipants[0].Participant.Assembly.OpenRead();
        query.Dispose();
        await workspace.RetireArtifactRootAsync(first.Roots[0].Correspondence, Ready(first.Roots[0]));
        Task<InspectionWorkspaceCloseReport> close = workspace.CloseAsync();
        Assert.False(close.IsCompleted);
        Assert.NotEqual(-1, stream.ReadByte());
        stream.Dispose();
        Assert.Empty((await close).ArtifactSessionCleanupFailures);
    }

    [Fact]
    public async Task PackageArtifactRootPreparation_DeadlineReleasesAbandonedReceipt()
    {
        await using InspectionWorkspace workspace = InspectionWorkspace.CreateAsynchronous();
        var time = new AdvancingTimeProvider();
        workspace.ConfigureArtifactRootAdmission(new(), time);
        var authority = new ArtifactRootPreparationAuthority(
            workspace.Identity, new(), time.GetUtcNow().AddMinutes(1), default);
        ArtifactRootPreparationReceipt receipt = await Prepare(workspace, authority, Binding("Abandoned.Root"));
        time.Advance(TimeSpan.FromMinutes(2));
        await receipt.Settlement.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        Assert.Equal(ArtifactRootPreparationState.Released, receipt.State);
        Assert.Equal(ArtifactRootReleaseOutcome.NoEffect, await workspace.ReleaseArtifactRootPreparationAsync(receipt));
    }

    [Fact]
    public async Task PackageArtifactRootPreparation_AggregateReservationIncludesPreparedAndDrainingRoots()
    {
        await using InspectionWorkspace workspace = InspectionWorkspace.CreateAsynchronous();
        workspace.ConfigureArtifactRootAdmission(new()
        {
            MaxRoots = 1,
            MaxRetainedImageBytes = Options.MaxAggregateRetainedImageBytes,
        });
        var scope = new ScopeState(workspace.Identity);
        ArtifactRootPublishedComposition first = await PublishPackages(workspace, scope, "Budget.Root");
        using InspectionWorkspace.ArtifactRootQueryLease query = Available(
            await workspace.EnterArtifactRootQueryAsync(workspace.Identity, first.Roots[0].Correspondence, Ready(first.Roots[0])));
        await workspace.RetireArtifactRootAsync(first.Roots[0].Correspondence, Ready(first.Roots[0]));
        Assert.Equal(ArtifactRootFailure.BudgetExceeded, Rejected(
            await workspace.PreparePackageArtifactRootsAsync(Authority(workspace), [Binding("Over.Budget")], Options)));
        query.Dispose();
    }

    [Fact]
    public async Task PackageArtifactRootPreparation_ChargesActualBytesAcrossPreparedCurrentAndDrainingRoots()
    {
        await using InspectionWorkspace workspace = InspectionWorkspace.CreateAsynchronous();
        long imageBytes = new FileInfo(typeof(AssemblyReferenceIdentity).Assembly.Location).Length;
        workspace.ConfigureArtifactRootAdmission(new() { MaxRetainedImageBytes = 4 * imageBytes });
        var scope = new ScopeState(workspace.Identity);
        var authority = Authority(workspace);
        ArtifactRootPreparationReceipt first = Available(await workspace.PreparePackageArtifactRootsAsync(
            authority, [Binding("Charge.First")]));
        Task firstRetirement = PreparedResourceEvidence(workspace, first).Retirement;
        ArtifactRootPreparationReceipt second = Available(await workspace.PreparePackageArtifactRootsAsync(
            authority, [Binding("Charge.Second")]));
        Assert.Equal(ArtifactRootFailure.BudgetExceeded, Rejected(
            await workspace.PreparePackageArtifactRootsAsync(authority, [Binding("Charge.Third")])));

        ArtifactRootPublishedComposition current = Assert.IsType<ArtifactRootPublishedComposition>(
            (await workspace.PublishArtifactRootCompositionAsync(
                await Plan(workspace, scope, authority, [first]))).Published);
        await workspace.ReleaseArtifactRootPreparationAsync(second);
        ArtifactRootPreparationReceipt third = Available(await workspace.PreparePackageArtifactRootsAsync(
            authority, [Binding("Charge.Third")]));
        using InspectionWorkspace.ArtifactRootQueryLease query = Available(
            await workspace.EnterArtifactRootQueryAsync(workspace.Identity,
                current.Roots[0].Correspondence, Ready(current.Roots[0])));
        var clearAuthority = Authority(workspace);
        Assert.NotNull((await workspace.PublishArtifactRootCompositionAsync(new(
            clearAuthority, current.Composition, [], [], scope.Participant(clearAuthority)))).Published);
        Assert.False(firstRetirement.IsCompleted);
        Assert.Equal(ArtifactRootFailure.BudgetExceeded, Rejected(
            await workspace.PreparePackageArtifactRootsAsync(authority, [Binding("Charge.Fourth")])));

        query.Dispose();
        await firstRetirement.WaitAsync(TestContext.Current.CancellationToken);
        ArtifactRootPreparationReceipt fourth = Available(await workspace.PreparePackageArtifactRootsAsync(
            authority, [Binding("Charge.Fourth")]));
        Assert.Equal(ArtifactRootPreparationState.Prepared, third.State);
        await workspace.ReleaseArtifactRootPreparationAsync(third);
        await workspace.ReleaseArtifactRootPreparationAsync(fourth);
    }

    [Fact]
    public async Task PackageArtifactRootPreparation_InsufficientBatchEnvelopeReleasesEveryRootAndReservation()
    {
        await using InspectionWorkspace workspace = InspectionWorkspace.CreateAsynchronous();
        long imageBytes = new FileInfo(typeof(AssemblyReferenceIdentity).Assembly.Location).Length;
        workspace.ConfigureArtifactRootAdmission(new() { MaxRetainedImageBytes = 3 * imageBytes });
        var authority = Authority(workspace);
        ArtifactRootCompositionGenerationIdentity original = await Current(workspace);

        Assert.IsType<ArtifactRootResult<ArtifactRootPreparationReceipt>.Rejected>(
            await workspace.PreparePackageArtifactRootsAsync(
                authority, [Binding("Envelope.First"), Binding("Envelope.Second")]));
        Assert.Same(original, await Current(workspace));
        workspace.ConfigureArtifactRootAdmission(new() { MaxRetainedImageBytes = 2 * imageBytes });
        ArtifactRootPreparationReceipt retry = Available(await workspace.PreparePackageArtifactRootsAsync(
            authority, [Binding("Envelope.Retry")]));
        await workspace.ReleaseArtifactRootPreparationAsync(retry);
    }

    [Theory]
    [InlineData("root")]
    [InlineData("entry")]
    [InlineData("participants")]
    public async Task PackageArtifactRootPreparation_BatchEnvelopePreservesExplicitCallerLimits(string limit)
    {
        await using InspectionWorkspace workspace = InspectionWorkspace.CreateAsynchronous();
        long imageBytes = new FileInfo(typeof(AssemblyReferenceIdentity).Assembly.Location).Length;
        PackageAssemblyContextRealizationOptions options = limit switch
        {
            "root" => Options with { MaxAggregateRetainedImageBytes = 2 * imageBytes - 1 },
            "entry" => Options with { MaxAssemblyEntryBytes = imageBytes - 1 },
            _ => Options with { MaxAssembliesPerRole = 0 },
        };
        Assert.IsType<ArtifactRootResult<ArtifactRootPreparationReceipt>.Rejected>(
            await workspace.PreparePackageArtifactRootsAsync(
                Authority(workspace), [Binding("Explicit.Limit")], options));
        workspace.ConfigureArtifactRootAdmission(new());
    }

    [Fact]
    public async Task PackageArtifactRootPreparation_ReentrantAdmissionCannotSpendReservedEnvelope()
    {
        await using InspectionWorkspace workspace = InspectionWorkspace.CreateAsynchronous();
        var authority = Authority(workspace);
        Task<ArtifactRootResult<ArtifactRootPreparationReceipt>>? overlapping = null;
        PackageRootBinding other = Binding("Concurrent.Other");
        PackageRootBinding first = Binding("Concurrent.First", onOpen: () =>
        {
            overlapping = workspace.PreparePackageArtifactRootsAsync(authority, [other]).AsTask();
        });

        ArtifactRootPreparationReceipt receipt = Available(await workspace.PreparePackageArtifactRootsAsync(
            authority, [first, Binding("Concurrent.Second")]));
        Assert.NotNull(overlapping);
        Assert.Equal(ArtifactRootFailure.BudgetExceeded, Rejected(await overlapping));
        Assert.Equal(2, receipt.Entries.Length);
        ArtifactRootPreparationReceipt later = Available(await workspace.PreparePackageArtifactRootsAsync(
            authority, [other]));
        await workspace.ReleaseArtifactRootPreparationAsync(receipt);
        await workspace.ReleaseArtifactRootPreparationAsync(later);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PackageArtifactRootPreparation_TerminalReceiptAndProjectionRetainNoPackageResources(bool publish)
    {
        await using InspectionWorkspace workspace = InspectionWorkspace.CreateAsynchronous();
        (ArtifactRootPreparationReceipt receipt, ArtifactRootScopeProjection? projection,
            ImmutableArray<WeakReference> resources) =
            await PrepareWeak(workspace, publish);
        for (int attempt = 0; attempt < 10 && resources.Any(resource => resource.IsAlive); attempt++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            await Task.Yield();
        }
        Assert.All(resources, resource => Assert.False(resource.IsAlive));
        Assert.NotEmpty(receipt.Entries);
        GC.KeepAlive(receipt);
        GC.KeepAlive(projection);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    static async Task<(ArtifactRootPreparationReceipt, ArtifactRootScopeProjection?, ImmutableArray<WeakReference>)> PrepareWeak(
        InspectionWorkspace workspace, bool publish)
    {
        PackageRootBinding binding = Binding("Weak.Root");
        var weakContent = new WeakReference(binding.Root.Content);
        var weakBinding = new WeakReference(binding);
        var authority = Authority(workspace);
        ArtifactRootPreparationReceipt receipt = await Prepare(workspace, authority, binding);
        var resources = ImmutableArray.CreateBuilder<WeakReference>();
        resources.Add(weakContent);
        resources.Add(weakBinding);
        (ImmutableArray<WeakReference> references, Task retirement) =
            PreparedResourceEvidence(workspace, receipt);
        resources.AddRange(references);
        ArtifactRootScopeProjection? projection = null;
        if (publish)
        {
            var scope = new ScopeState(workspace.Identity);
            ArtifactRootPublishedComposition published = Assert.IsType<ArtifactRootPublishedComposition>(
                (await workspace.PublishArtifactRootCompositionAsync(
                    await Plan(workspace, scope, authority, [receipt]))).Published);
            projection = published.Roots[0];
            using InspectionWorkspace.ArtifactRootQueryLease query = Available(
                await workspace.EnterArtifactRootQueryAsync(workspace.Identity, projection.Correspondence, Ready(projection)));
            resources.Add(new(query.Realization));
            resources.Add(new(query.Realization.SurfaceGroup));
            var clear = Authority(workspace);
            Assert.NotNull((await workspace.PublishArtifactRootCompositionAsync(
                new(clear, await Current(workspace), [], [], scope.Participant(clear)))).Published);
            Assert.False(retirement.IsCompleted);
            query.Dispose();
        }
        else
            await workspace.ReleaseArtifactRootPreparationAsync(receipt);
        await retirement.WaitAsync(TestContext.Current.CancellationToken);
        return (receipt, projection, resources.ToImmutable());
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    static (ImmutableArray<WeakReference> References, Task Retirement) PreparedResourceEvidence(
        InspectionWorkspace workspace, ArtifactRootPreparationReceipt receipt)
    {
        var preparations = Assert.IsAssignableFrom<System.Collections.IDictionary>(
            typeof(InspectionWorkspace).GetField("_rootPreparations",
                BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(workspace));
        object batch = preparations[receipt]!;
        var roots = Assert.IsType<ImmutableArray<InspectionWorkspace.RootLifetime>>(
            batch.GetType().GetProperty("Roots")!.GetValue(batch));
        var references = ImmutableArray.CreateBuilder<WeakReference>();
        foreach (InspectionWorkspace.RootLifetime root in roots)
        {
            references.Add(new(root));
            references.Add(new(root.Resources));
            references.Add(new(root.Resources.Realization));
            if (root.Resources.Session is { } session)
                references.Add(new(session));
        }
        return (references.ToImmutable(), Task.WhenAll(roots.Select(root => root.Released.Task)));
    }

    static ArtifactRootPreparationAuthority Authority(InspectionWorkspace workspace) =>
        CancellableAuthority(workspace, TestContext.Current.CancellationToken);

    static ArtifactRootPreparationAuthority CancellableAuthority(
        InspectionWorkspace workspace, CancellationToken cancellation) =>
        new(workspace.Identity, new(), DateTimeOffset.UtcNow.AddMinutes(5), cancellation);

    static async Task<ArtifactRootPreparationReceipt> Prepare(
        InspectionWorkspace workspace, ArtifactRootPreparationAuthority authority,
        params PackageRootBinding[] packages) =>
        Available(await workspace.PreparePackageArtifactRootsAsync(authority, [.. packages], Options));

    static async Task<ArtifactRootCompositionGenerationIdentity> Current(InspectionWorkspace workspace) =>
        Available(await workspace.GetCurrentArtifactRootCompositionGenerationAsync(workspace.Identity));

    static ArtifactRootGenerationReference Ready(ArtifactRootScopeProjection projection) =>
        Assert.IsType<ArtifactRootRealizationStatus.Ready>(projection.Status).Generation;

    static T Available<T>(ArtifactRootResult<T> result) =>
        Assert.IsType<ArtifactRootResult<T>.Available>(result).Value;

    static ArtifactRootFailure Rejected<T>(ArtifactRootResult<T> result) =>
        Assert.IsType<ArtifactRootResult<T>.Rejected>(result).Failure;

    static ArtifactRootPublicationEntry.Adopt Adopt(ArtifactRootPreparationReceipt receipt, int index = 0) =>
        new(receipt.Preparation, receipt.Entries[index].Entry);

    static ArtifactRootPublicationEntry.Retain Retain(ArtifactRootScopeProjection projection) =>
        new(projection.Correspondence, Ready(projection));

    static async Task<ArtifactRootPublicationPlan> Plan(
        InspectionWorkspace workspace, ScopeState scope,
        ArtifactRootPreparationAuthority authority,
        ImmutableArray<ArtifactRootPreparationReceipt> receipts) =>
        new(authority, await Current(workspace),
            [.. receipts.SelectMany(receipt => receipt.Entries.Select(
                entry => (ArtifactRootPublicationEntry)new ArtifactRootPublicationEntry.Adopt(receipt.Preparation, entry.Entry)))],
            receipts, scope.Participant(authority));

    static async Task<ArtifactRootPublishedComposition> PublishPackages(
        InspectionWorkspace workspace, ScopeState scope, params string[] packages)
    {
        var authority = Authority(workspace);
        ArtifactRootPreparationReceipt receipt = await Prepare(
            workspace, authority, packages.Select(name => Binding(name)).ToArray());
        ArtifactRootPublicationOutcome outcome = await workspace.PublishArtifactRootCompositionAsync(
            await Plan(workspace, scope, authority, [receipt]));
        Assert.Null(outcome.Failure);
        return Assert.IsType<ArtifactRootPublishedComposition>(outcome.Published);
    }

    static PackageRootBinding Binding(
        string packageId, bool malformed = false, string? entry = null, Action? onOpen = null)
    {
        using var bytes = new MemoryStream();
        using (var archive = new ZipArchive(bytes, ZipArchiveMode.Create, leaveOpen: true))
        {
            using Stream destination = archive.CreateEntry(entry ?? $"lib/net11.0/{packageId}.dll").Open();
            destination.Write(malformed ? [1, 2, 3] :
                File.ReadAllBytes(typeof(AssemblyReferenceIdentity).Assembly.Location));
        }
        IPackageContent content = new InMemoryPackageContent(bytes.ToArray(), fromCache: false, producerKey: "tests");
        if (onOpen is not null)
            content = new CallbackPackageContent(content, onOpen);
        return PackageRootBinding.CreateFromSource(new AcquiredPackageSourcePayload(
            PackageSourceCoordinate.Create(packageId, "1.0.0"), content, "tests", PackagePayloadOrigin.Download),
            "net11.0");
    }

    sealed class CallbackPackageContent(IPackageContent inner, Action onOpen) : IPackageContent
    {
        Action? _onOpen = onOpen;
        public string? RootPath => inner.RootPath;
        public string? NupkgPath => inner.NupkgPath;
        public bool FromCache => inner.FromCache;
        public string ProducerKey => inner.ProducerKey;
        public bool RequiresArchiveTreeMatch => inner.RequiresArchiveTreeMatch;
        public bool TryOpenArchive([NotNullWhen(true)] out Stream? stream) =>
            inner.TryOpenArchive(out stream);
        public bool TryOpenEntry(string relativePath, [NotNullWhen(true)] out Stream? stream)
        {
            Interlocked.Exchange(ref _onOpen, null)?.Invoke();
            return inner.TryOpenEntry(relativePath, out stream);
        }
        public bool TryOpenEntry(string relativePath, long maxExpandedBytes, [NotNullWhen(true)] out Stream? stream)
        {
            Interlocked.Exchange(ref _onOpen, null)?.Invoke();
            return inner.TryOpenEntry(relativePath, maxExpandedBytes, out stream);
        }
        public IEnumerable<string> EnumerateEntries() => inner.EnumerateEntries();
    }

    sealed class ScopeState(InspectionWorkspaceIdentity workspace)
    {
        internal InspectionWorkspaceIdentity Workspace { get; } = workspace;
        internal WorkspaceScopePublicationBaseIdentity Base { get; set; } = new();
        internal ScopeResult? Current { get; set; }
        internal int PrepareCalls { get; set; }
        internal int Commits { get; set; }
        internal ArtifactRootScopePublicationParticipant Participant(ArtifactRootPreparationAuthority authority) =>
            new(new Candidate(this, authority));
    }

    sealed class ScopeResult(
        ArtifactRootCompositionGenerationIdentity composition,
        ImmutableArray<ArtifactRootScopeProjection> roots) : WorkspaceScopePublicationResult
    {
        internal ArtifactRootCompositionGenerationIdentity Composition { get; } = composition;
        internal ImmutableArray<ArtifactRootScopeProjection> Roots { get; } = roots;
    }

    sealed class Candidate(ScopeState owner, ArtifactRootPreparationAuthority authority)
        : IWorkspaceScopePublicationCandidate
    {
        public InspectionWorkspaceIdentity Workspace => owner.Workspace;
        public WorkspaceScopePublicationBaseIdentity ExpectedBase { get; } = owner.Base;
        public WorkspaceScopePublicationOperationIdentity Operation { get; } = new();
        public WorkspaceScopePublicationCandidateIdentity CandidateSet => authority.CandidateSet;
        internal Action? OnPrepare { get; init; }
        internal ArtifactRootFailure? Refusal { get; init; }
        internal ArtifactRootCompositionGenerationIdentity? CandidateComposition { get; private set; }

        public ArtifactRootResult<WorkspaceScopePreparedCommit> PrepareCommit(
            ArtifactRootCompositionGenerationIdentity currentComposition,
            ArtifactRootCompositionGenerationIdentity candidateComposition,
            ImmutableArray<ArtifactRootScopeProjection> roots)
        {
            owner.PrepareCalls++;
            CandidateComposition = candidateComposition;
            OnPrepare?.Invoke();
            if (!ReferenceEquals(owner.Base, ExpectedBase))
                return new ArtifactRootResult<WorkspaceScopePreparedCommit>.Rejected(ArtifactRootFailure.ScopeBaseMismatch);
            if (Refusal is { } failure)
                return new ArtifactRootResult<WorkspaceScopePreparedCommit>.Rejected(failure);
            return new ArtifactRootResult<WorkspaceScopePreparedCommit>.Available(
                new ScopeCommitToken(owner, new ScopeResult(candidateComposition, roots)));
        }
    }

    sealed class ScopeCommitToken(ScopeState owner, ScopeResult result) : WorkspaceScopePreparedCommit(result)
    {
        readonly WorkspaceScopePublicationBaseIdentity _next = new();
        internal override void Commit()
        {
            owner.Base = _next;
            owner.Current = result;
            owner.Commits++;
        }
    }

    sealed class AdvancingTimeProvider : TimeProvider
    {
        DateTimeOffset _now = DateTimeOffset.UtcNow;
        readonly List<ManualTimer> _timers = [];
        public override DateTimeOffset GetUtcNow() => _now;
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = new ManualTimer(callback, state);
            _timers.Add(timer);
            return timer;
        }
        internal void Advance(TimeSpan delta)
        {
            _now += delta;
            foreach (ManualTimer timer in _timers.ToArray())
                timer.Fire();
        }
        sealed class ManualTimer(TimerCallback callback, object? state) : ITimer
        {
            bool _disposed;
            public bool Change(TimeSpan dueTime, TimeSpan period) => !_disposed;
            public void Dispose() => _disposed = true;
            public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
            internal void Fire() { if (!_disposed) callback(state); }
        }
    }
}
