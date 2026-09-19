using System.Runtime.CompilerServices;
using System.Text.Json;

using Fixture = DotnetInspector.Queries.Tests.NavigationSessionTests.Fixture;

namespace DotnetInspector.Queries.Tests;

public sealed class NavigationStateTests
{
    [Fact]
    public async Task Initialize_EqualFactsSeedIndependentSessionIdentities()
    {
        await using Fixture fixture = await Fixture.CreateAsync();
        NavigationState first = fixture.Session.State;
        NavigationOperationInitialization second = NavigationTransitions.Initialize(
            fixture.Workspace.Identity, new(fixture.Scope, fixture.Packages[0], fixture.Availability), fixture.Registry);

        Assert.NotEqual(first.Id, second.State.Id);
        Assert.Same(first.Workspace, second.State.Workspace);
        Assert.True(NavigationWorkspaceSnapshotEquality.Equals(first.CurrentSnapshot, second.State.CurrentSnapshot));
        Assert.NotEqual(first.Publication, second.State.Publication);
        Assert.Same(second.State.Snapshot, second.Result.Consumer.Snapshot);
        Assert.Null(second.Result.LensResolution);
        Assert.True(NavigationTransitions.ValidateAuthority(first, fixture.Session.Initialization.Authority));
        Assert.False(NavigationTransitions.ValidateAuthority(second.State, fixture.Session.Initialization.Authority));
        Assert.True(NavigationTransitions.ValidateAuthority(second.State, second.Result.Consumer.Authority));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SameStateAndInput_ProduceEquivalentResultsWithoutChangingInput(bool lens)
    {
        await using Fixture fixture = await Fixture.CreateAsync();
        NavigationState original = fixture.Session.State;
        NavigationConsumerSnapshot consumer = original.Snapshot;
        NavigationWorkspaceSnapshot current = original.CurrentSnapshot;
        NavigationPublication publication = original.Publication;
        string originalJson = Json(fixture.Session.Initialization);
        NavigationAction action = original.Snapshot.Types[0].Navigation.Action!;
        var exact = new NavigationLensIdentity(current.ActiveSubject, new ViewFacetId("library.metadata"));

        NavigationTransition first = Begin();
        NavigationTransition second = Begin();
        Assert.NotSame(first.State, second.State);
        Assert.Same(consumer, first.State.Snapshot);
        Assert.Same(current, first.State.CurrentSnapshot);
        Assert.NotSame(first.Request, second.Request);
        Assert.NotSame(first.Work, second.Work);
        Assert.Equal(first.Request!.Id, second.Request!.Id);
        Assert.Equal(first.Work!.Attempt, second.Work!.Attempt);
        Assert.Equal(first.Work.Intent, second.Work.Intent);
        Assert.Same(first.Request, first.Work.Identity);
        Assert.True(NavigationTransitions.CanCommit(original, first));
        Assert.True(NavigationTransitions.CanCommit(original, second));

        NavigationPreparation preparation = fixture.Ready(first.Work);
        NavigationEvaluationResult evaluated = NavigationTransitions.Evaluate(first.Work, preparation, fixture.Registry);
        NavigationEvaluationResult reevaluated = NavigationTransitions.Evaluate(first.Work, preparation, fixture.Registry);
        NavigationEvaluationResult independent = NavigationTransitions.Evaluate(second.Work, preparation, fixture.Registry);
        Assert.NotSame(evaluated, reevaluated);
        Assert.Same(first.Work, evaluated.Request);
        Assert.True(NavigationWorkspaceSnapshotEquality.Equals(evaluated.Snapshot, reevaluated.Snapshot));

        NavigationTransition completed = NavigationTransitions.Complete(first.State, first.Work, evaluated);
        NavigationTransition replayed = NavigationTransitions.Complete(first.State, first.Work, evaluated);
        NavigationTransition equivalent = NavigationTransitions.Complete(second.State, second.Work, independent);
        Assert.Null(completed.Rejection);
        Assert.Null(completed.Work);
        Assert.Null(completed.AuthorityResult);
        Assert.NotNull(completed.Result);
        Assert.Equal(Json(completed.Result.Consumer), Json(replayed.Result!.Consumer));
        Assert.Equal(Json(completed.Result.Consumer), Json(equivalent.Result!.Consumer));
        Assert.Equal(completed.State.Publication, equivalent.State.Publication);
        Assert.True(NavigationWorkspaceSnapshotEquality.Equals(
            completed.State.CurrentSnapshot, equivalent.State.CurrentSnapshot));
        if (lens)
        {
            Assert.Same(evaluated.Resolution, completed.Result.LensResolution!.Activation);
            Assert.Same(exact, completed.Result.LensResolution.Activation.Request);
            Assert.Equal(completed.Result.Consumer.Authority, completed.Result.LensResolution.Authority);
        }
        else
        {
            Assert.Null(completed.Result.LensResolution);
            NavigationTransition transported = NavigationTransitions.Begin(original, action.Generation, action.Id);
            Assert.Equal(first.Work.Attempt, transported.Work!.Attempt);
            Assert.Equal(first.Work.Intent, transported.Work.Intent);
        }

        Assert.Same(consumer, original.Snapshot);
        Assert.Same(current, original.CurrentSnapshot);
        Assert.Equal(publication, original.Publication);
        Assert.Equal(originalJson, Json(fixture.Session.Initialization));
        Assert.True(NavigationTransitions.ValidateAuthority(original, fixture.Session.Initialization.Authority));
        Assert.False(NavigationTransitions.ValidateAuthority(first.State, fixture.Session.Initialization.Authority));
        Assert.Equal(first.Work.Attempt, Begin().Work!.Attempt);

        NavigationTransition Begin() => lens
            ? NavigationTransitions.BeginLens(original, exact)
            : NavigationTransitions.Begin(original, action);
    }

    [Fact]
    public async Task IndependentStates_InterleaveWithoutSharingHistoryOrAuthority()
    {
        await using Fixture fixture = await Fixture.CreateAsync();
        NavigationState first = fixture.Session.State;
        NavigationOperationInitialization initialized = NavigationTransitions.Initialize(
            fixture.Workspace.Identity, new(fixture.Scope, fixture.Packages[0], fixture.Availability), fixture.Registry);
        NavigationState second = initialized.State;
        NavigationTransition firstBegin = NavigationTransitions.Begin(first, first.Snapshot.Types[0].Navigation.Action!);
        NavigationTransition secondBegin = NavigationTransitions.Begin(second, second.Snapshot.Types[1].Navigation.Action!);
        NavigationEvaluationResult firstFacts = NavigationTransitions.Evaluate(
            firstBegin.Work!, fixture.Ready(firstBegin.Work!), fixture.Registry);
        NavigationEvaluationResult secondFacts = NavigationTransitions.Evaluate(
            secondBegin.Work!, fixture.Ready(secondBegin.Work!), fixture.Registry);
        NavigationTransition secondCompleted = NavigationTransitions.Complete(secondBegin.State, secondBegin.Work!, secondFacts);
        NavigationTransition firstCompleted = NavigationTransitions.Complete(firstBegin.State, firstBegin.Work!, firstFacts);

        Assert.Equal(first.Snapshot.Types[0].Navigation.Subject, firstCompleted.State.Snapshot.ActiveSubject);
        Assert.Equal(second.Snapshot.Types[1].Navigation.Subject, secondCompleted.State.Snapshot.ActiveSubject);
        Assert.True(NavigationTransitions.ValidateAuthority(firstCompleted.State, firstCompleted.Result!.Consumer.Authority));
        Assert.True(NavigationTransitions.ValidateAuthority(secondCompleted.State, secondCompleted.Result!.Consumer.Authority));
        Assert.False(NavigationTransitions.ValidateAuthority(firstCompleted.State, secondCompleted.Result.Consumer.Authority));
        Assert.False(NavigationTransitions.CanCommit(firstBegin.State, secondCompleted));
        Assert.Equal(Json(firstCompleted.Result.Consumer), Json(NavigationTransitions.Complete(
            firstBegin.State, firstBegin.Work!, firstFacts).Result!.Consumer));
        Assert.Same(first, fixture.Session.State);
        Assert.Same(second.Snapshot, initialized.Result.Consumer.Snapshot);
    }

    [Fact]
    public async Task Complete_RejectsWrongTicketAttemptSessionAndWorkspaceWithoutChangingState()
    {
        await using Fixture fixture = await Fixture.CreateAsync();
        await using Fixture foreign = await Fixture.CreateAsync();
        NavigationState original = fixture.Session.State;
        NavigationAction action = original.Snapshot.Types[0].Navigation.Action!;
        NavigationTransition admitted = NavigationTransitions.Begin(original, action);
        NavigationTransition sameInputs = NavigationTransitions.Begin(original, action);
        NavigationEvaluationRequest work = admitted.Work!;
        NavigationEvaluationRequest otherTicket = sameInputs.Work!;
        NavigationEvaluationResult evaluation = NavigationTransitions.Evaluate(work, fixture.Ready(work), fixture.Registry);
        NavigationEvaluationResult otherEvaluation = NavigationTransitions.Evaluate(
            otherTicket, fixture.Ready(otherTicket), fixture.Registry);
        NavigationOperationInitialization anotherSession = NavigationTransitions.Initialize(
            fixture.Workspace.Identity, new(fixture.Scope, fixture.Packages[0], fixture.Availability), fixture.Registry);

        Assert.Equal(work.Attempt, otherTicket.Attempt);
        Assert.NotSame(work, otherTicket);
        AssertRejected(admitted.State, NavigationTransitions.Complete(admitted.State, work, otherEvaluation),
            NavigationCompletionRejection.WrongTicket);
        AssertRejected(admitted.State, NavigationTransitions.Complete(admitted.State, otherTicket, otherEvaluation),
            NavigationCompletionRejection.StaleAttempt);
        AssertRejected(anotherSession.State, NavigationTransitions.Complete(anotherSession.State, work, evaluation),
            NavigationCompletionRejection.ForeignSession);
        AssertRejected(foreign.Session.State, NavigationTransitions.Complete(foreign.Session.State, work, evaluation),
            NavigationCompletionRejection.ForeignWorkspace);

        NavigationTransition accepted = NavigationTransitions.Complete(admitted.State, work, evaluation);
        Assert.Equal(NavigationOutcomeKind.Applied, accepted.Result!.Consumer.Outcome.Kind);
        Assert.True(NavigationTransitions.ValidateAuthority(accepted.State, accepted.Result.Consumer.Authority));
    }

    [Fact]
    public async Task Complete_DuplicateCannotRepublishOrCommitAgainstReplacedCurrentSlot()
    {
        await using Fixture fixture = await Fixture.CreateAsync();
        NavigationState original = fixture.Session.State;
        NavigationTransition beginning = NavigationTransitions.Begin(original, original.Snapshot.Types[0].Navigation.Action!);
        NavigationEvaluationRequest work = beginning.Work!;
        NavigationEvaluationResult evaluation = NavigationTransitions.Evaluate(work, fixture.Ready(work), fixture.Registry);
        NavigationTransition completed = NavigationTransitions.Complete(beginning.State, work, evaluation);
        NavigationTransition raced = NavigationTransitions.Complete(beginning.State, work, evaluation);
        NavigationState current = beginning.State;
        Assert.True(NavigationTransitions.CanCommit(current, completed));
        current = completed.State;

        Assert.False(NavigationTransitions.CanCommit(current, raced));
        Assert.False(NavigationTransitions.CanCommit(current, completed));
        Assert.False(NavigationTransitions.CanCommit(current, beginning));
        AssertRejected(current, NavigationTransitions.Complete(current, work, evaluation),
            NavigationCompletionRejection.StaleAttempt);
        Assert.Same(completed.Result!.Consumer.Snapshot, current.Snapshot);
        Assert.True(NavigationTransitions.ValidateAuthority(current, completed.Result.Consumer.Authority));

        NavigationTransition posting = NavigationTransitions.RecordConsumerPosting(
            current, completed.Result.Consumer.Authority!);
        NavigationTransition competing = NavigationTransitions.Abandon(current, completed.Result.Consumer.Authority!);
        Assert.True(NavigationTransitions.CanCommit(current, posting));
        current = posting.State;
        Assert.False(NavigationTransitions.CanCommit(current, competing));
        Assert.Equal(NavigationAuthorityResult.Accepted,
            NavigationTransitions.Acknowledge(current, completed.Result.Consumer.Authority!).AuthorityResult);
    }

    [Fact]
    public async Task Maintenance_RegatherPreservesRequestIdentityButRejectsRetiredAttempt()
    {
        await using Fixture fixture = await Fixture.CreateAsync();
        NavigationState initial = Acknowledge(fixture.Session.State, fixture.Session.Initialization.Authority!);
        NavigationTransition queued = NavigationTransitions.QueueMaintenance(initial);
        NavigationTransition begun = NavigationTransitions.Advance(queued.State);
        NavigationEvaluationRequest first = begun.Work!;
        NavigationEvaluationResult obsolete = NavigationTransitions.Evaluate(first, fixture.Ready(first), fixture.Registry);
        NavigationTransition explicitBegin = NavigationTransitions.Begin(begun.State, begun.State.Snapshot.Types[1].Navigation.Action!);
        NavigationEvaluationResult selected = NavigationTransitions.Evaluate(
            explicitBegin.Work!, fixture.Ready(explicitBegin.Work!), fixture.Registry);
        NavigationTransition completed = NavigationTransitions.Complete(explicitBegin.State, explicitBegin.Work!, selected);
        NavigationTransition invalidated = NavigationTransitions.Complete(completed.State, first, obsolete);

        Assert.Same(queued.Request, begun.Request);
        Assert.Same(first.Identity, invalidated.Request);
        Assert.Null(invalidated.Result);
        Assert.Null(invalidated.Work);
        Assert.Null(invalidated.Rejection);
        Assert.Same(completed.State.CurrentSnapshot, invalidated.State.CurrentSnapshot);
        Assert.Same(completed.State.Snapshot, invalidated.State.Snapshot);
        NavigationTransition blocked = NavigationTransitions.Advance(invalidated.State);
        Assert.Same(invalidated.State, blocked.State);
        Assert.Null(blocked.Work);

        NavigationTransition released = NavigationTransitions.Abandon(
            invalidated.State, completed.Result!.Consumer.Authority!);
        NavigationTransition regather = NavigationTransitions.Advance(released.State);
        NavigationEvaluationRequest retry = regather.Work!;
        Assert.Same(first.Identity, retry.Identity);
        Assert.NotEqual(first.Attempt, retry.Attempt);
        Assert.NotEqual(first.Intent, retry.Intent);
        Assert.Equal(completed.State.Publication, retry.Publication);
        Assert.Same(completed.State.CurrentSnapshot, retry.Basis);
        AssertRejected(regather.State, NavigationTransitions.Complete(regather.State, first, obsolete),
            NavigationCompletionRejection.StaleAttempt);
        NavigationEvaluationResult refreshed = NavigationTransitions.Evaluate(retry, fixture.Ready(retry), fixture.Registry);
        NavigationTransition completion = NavigationTransitions.Complete(regather.State, retry, refreshed);
        Assert.Same(first.Identity, completion.Request);
        Assert.Equal(first.Request, completion.Result!.Consumer.Request);
        Assert.Same(completed.State.Snapshot, completion.State.Snapshot);
        Assert.Equal(completed.State.Publication, completion.State.Publication);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Cancel_UsesExactQueuedRequestIdentityAndPreservesNextRequest(bool maintenance)
    {
        await using Fixture fixture = await Fixture.CreateAsync();
        NavigationState original = fixture.Session.State;
        NavigationTransition queued = Queue(original);
        NavigationTransition sameInput = Queue(original);
        NavigationTransition next = Queue(queued.State);
        Assert.Equal(queued.Request!.Id, sameInput.Request!.Id);
        Assert.NotSame(queued.Request, sameInput.Request);
        AssertRejected(next.State, NavigationTransitions.Cancel(next.State, sameInput.Request),
            NavigationCompletionRejection.StaleAttempt);

        NavigationTransition cancelled = NavigationTransitions.Cancel(next.State, queued.Request);
        Assert.Same(queued.Request, cancelled.Request);
        Assert.Null(cancelled.Rejection);
        Assert.Null(cancelled.Result);
        AssertRejected(cancelled.State, NavigationTransitions.Cancel(cancelled.State, queued.Request),
            NavigationCompletionRejection.StaleAttempt);
        NavigationState released = NavigationTransitions.Abandon(
            cancelled.State, fixture.Session.Initialization.Authority!).State;
        NavigationTransition advanced = NavigationTransitions.Advance(released);
        Assert.Same(next.Request, advanced.Request);
        if (maintenance)
        {
            Assert.Same(next.Request, advanced.Work!.Identity);
            Assert.Null(advanced.Result);
            NavigationEvaluationResult evaluation = NavigationTransitions.Evaluate(
                advanced.Work, fixture.Ready(advanced.Work), fixture.Registry);
            NavigationTransition aborted = NavigationTransitions.Cancel(advanced.State, next.Request!);
            AssertRejected(aborted.State, NavigationTransitions.Complete(aborted.State, advanced.Work, evaluation),
                NavigationCompletionRejection.StaleAttempt);
            Assert.Null(NavigationTransitions.Advance(aborted.State).Work);
        }
        else
        {
            Assert.Null(advanced.Work);
            Assert.Equal(NavigationOutcomeKind.Synchronized, advanced.Result!.Consumer.Outcome.Kind);
            Assert.Equal(next.Request!.Id, advanced.Result.Consumer.Request);
        }

        NavigationTransition Queue(NavigationState state) => maintenance
            ? NavigationTransitions.QueueMaintenance(state)
            : NavigationTransitions.QueueSynchronization(state);
    }

    [Fact]
    public async Task InvocationAvailabilityTarget_IsNotRetainedByStateTicketEvaluationOrResult()
    {
        await using Fixture fixture = await Fixture.CreateAsync();
        var specimen = CreateDetachmentSpecimen(fixture);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        Assert.False(specimen.AvailabilityTarget.IsAlive);
        Assert.Equal(NavigationOutcomeKind.Applied, specimen.Completed.Result!.Consumer.Outcome.Kind);
        Assert.True(NavigationTransitions.ValidateAuthority(
            specimen.Completed.State, specimen.Completed.Result.Consumer.Authority));
        GC.KeepAlive(specimen);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    static (
        NavigationOperationInitialization Initialized,
        NavigationTransition Begun,
        NavigationEvaluationResult Evaluation,
        NavigationTransition Completed,
        WeakReference AvailabilityTarget) CreateDetachmentSpecimen(Fixture fixture)
    {
        var target = new AvailabilitySpecimen(fixture.Registry);
        var facts = new NavigationEvaluationFacts(fixture.Scope, fixture.Packages[0], target.Get);
        NavigationOperationInitialization initialized = NavigationTransitions.Initialize(
            fixture.Workspace.Identity, facts, fixture.Registry);
        NavigationTransition begun = NavigationTransitions.BeginLens(initialized.State,
            new(initialized.State.CurrentSnapshot.ActiveSubject, new ViewFacetId("library.metadata")));
        NavigationEvaluationResult evaluation = NavigationTransitions.Evaluate(
            begun.Work!, new NavigationPreparation.Ready(facts), fixture.Registry);
        NavigationTransition completed = NavigationTransitions.Complete(begun.State, begun.Work!, evaluation);
        return (initialized, begun, evaluation, completed, new(target));
    }

    sealed class AvailabilitySpecimen(ViewFacetRegistry registry)
    {
        internal IViewFacetAvailabilityFacts Get(StructuralSubjectIdentity subject, NavigationSubjectInventory? inventory) =>
            new ViewFacetAvailabilitySnapshot(registry.Descriptors.Select(descriptor =>
                new ViewFacetAvailabilityFact(descriptor.Id, ViewFacetAvailability.Available.Instance)));
    }

    static NavigationState Acknowledge(NavigationState state, NavigationEffectAuthority authority)
    {
        NavigationTransition posted = NavigationTransitions.RecordConsumerPosting(state, authority);
        Assert.Equal(NavigationAuthorityResult.Accepted, posted.AuthorityResult);
        NavigationTransition acknowledged = NavigationTransitions.Acknowledge(posted.State, authority);
        Assert.Equal(NavigationAuthorityResult.Accepted, acknowledged.AuthorityResult);
        return acknowledged.State;
    }

    static void AssertRejected(
        NavigationState state, NavigationTransition transition, NavigationCompletionRejection rejection)
    {
        Assert.Same(state, transition.State);
        Assert.True(NavigationTransitions.CanCommit(state, transition));
        Assert.Equal(rejection, transition.Rejection);
        Assert.Null(transition.Request);
        Assert.Null(transition.Work);
        Assert.Null(transition.Result);
        Assert.Null(transition.AuthorityResult);
    }

    static string Json(NavigationConsumerResult result) =>
        JsonSerializer.Serialize(result, NavigationConsumerJsonContext.Default.NavigationConsumerResult);
}
