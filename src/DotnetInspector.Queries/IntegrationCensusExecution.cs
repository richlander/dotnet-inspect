using System.Collections.Immutable;

using ILInspector.Metadata;

namespace DotnetInspector.Queries;

/// <summary>
/// Owner-issued identity for the Type-identity table shared by population and
/// exact-resolution access.
/// </summary>
public interface IIntegrationTypeIdentityDomain
{
}

/// <summary>Ordered selected-Type population for one executable Census.</summary>
public sealed class IntegrationSelectedTypeAccess
{
    public IntegrationSelectedTypeAccess(
        IIntegrationTypeIdentityDomain identityDomain,
        IEnumerable<IntegrationTypeIdentity> selectedTypes)
    {
        ArgumentNullException.ThrowIfNull(identityDomain);
        ArgumentNullException.ThrowIfNull(selectedTypes);
        IdentityDomain = identityDomain;
        SelectedTypes = CopyUnique(
            selectedTypes,
            nameof(selectedTypes));
    }

    public IIntegrationTypeIdentityDomain IdentityDomain { get; }
    public ImmutableArray<IntegrationTypeIdentity> SelectedTypes { get; }

    static ImmutableArray<IntegrationTypeIdentity> CopyUnique(
        IEnumerable<IntegrationTypeIdentity> values,
        string parameterName)
    {
        ImmutableArray<IntegrationTypeIdentity> copied = [.. values];
        var seen = new HashSet<IntegrationTypeIdentity>();
        foreach (IntegrationTypeIdentity value in copied)
        {
            if (value is null || !seen.Add(value))
            {
                throw new ArgumentException(
                    "Selected Types cannot contain null or duplicate identities.",
                    parameterName);
            }
        }
        return copied;
    }
}

/// <summary>
/// Ordered source participants and their owner-issued terminal availability
/// receipts.
/// </summary>
public sealed class IntegrationSourceParticipantAccess
{
    public IntegrationSourceParticipantAccess(
        IEnumerable<IntegrationSourceParticipantIdentity> participants,
        IEnumerable<IntegrationSourceParticipantAttempt> attempts)
    {
        ArgumentNullException.ThrowIfNull(participants);
        ArgumentNullException.ThrowIfNull(attempts);
        Participants = CopyUnique(participants, nameof(participants));

        ImmutableArray<IntegrationSourceParticipantAttempt> supplied =
            [.. attempts];
        if (supplied.Any(attempt => attempt is null))
        {
            throw new ArgumentException(
                "Source-participant attempts cannot contain null.",
                nameof(attempts));
        }

        var byParticipant = new Dictionary<
            IntegrationSourceParticipantIdentity,
            IntegrationSourceParticipantAttempt>();
        foreach (IntegrationSourceParticipantAttempt attempt in supplied)
        {
            if (!byParticipant.TryAdd(attempt.Participant, attempt))
            {
                throw new ArgumentException(
                    "Source-participant attempts cannot repeat a participant.",
                    nameof(attempts));
            }
        }

        var ordered =
            ImmutableArray.CreateBuilder<IntegrationSourceParticipantAttempt>(
                Participants.Length);
        foreach (IntegrationSourceParticipantIdentity participant
            in Participants)
        {
            if (!byParticipant.Remove(
                    participant,
                    out IntegrationSourceParticipantAttempt? attempt))
            {
                throw new ArgumentException(
                    "Source-participant attempts must exactly cover the participant roster.",
                    nameof(attempts));
            }
            ordered.Add(attempt);
        }
        if (byParticipant.Count != 0)
        {
            throw new ArgumentException(
                "Source-participant attempts contain an extraneous participant.",
                nameof(attempts));
        }

        Attempts = ordered.MoveToImmutable();
    }

    public ImmutableArray<IntegrationSourceParticipantIdentity> Participants
        { get; }
    public ImmutableArray<IntegrationSourceParticipantAttempt> Attempts
        { get; }

    static ImmutableArray<IntegrationSourceParticipantIdentity> CopyUnique(
        IEnumerable<IntegrationSourceParticipantIdentity> values,
        string parameterName)
    {
        ImmutableArray<IntegrationSourceParticipantIdentity> copied =
            [.. values];
        var seen = new HashSet<IntegrationSourceParticipantIdentity>();
        foreach (IntegrationSourceParticipantIdentity value in copied)
        {
            if (value is null || !seen.Add(value))
            {
                throw new ArgumentException(
                    "Source participants cannot contain null or duplicate identities.",
                    parameterName);
            }
        }
        return copied;
    }
}

/// <summary>
/// Executable completeness evidence corresponding to the exact universe
/// description.
/// </summary>
public sealed class IntegrationCompletenessAccess
{
    public IntegrationCompletenessAccess(
        IAnalysisUniverseCompleteness completeness,
        IEnumerable<IAnalysisUniverseFailure>? failures = null)
    {
        ArgumentNullException.ThrowIfNull(completeness);
        Completeness = completeness;
        Failures = [.. failures ?? []];
        if (Failures.Any(failure => failure is null))
        {
            throw new ArgumentException(
                "Universe failures cannot contain null.",
                nameof(failures));
        }
    }

    public IAnalysisUniverseCompleteness Completeness { get; }
    public ImmutableArray<IAnalysisUniverseFailure> Failures { get; }
}

/// <summary>Executable operation for one exact Integration producer policy.</summary>
public sealed class IntegrationProducerPolicyAccess
{
    readonly Func<
        IntegrationProducerPolicyAttemptAddress,
        CancellationToken,
        IntegrationProducerPolicyAttempt> _execute;

    public IntegrationProducerPolicyAccess(
        IntegrationProducerPolicyBinding policy,
        Func<
            IntegrationProducerPolicyAttemptAddress,
            CancellationToken,
            IntegrationProducerPolicyAttempt> execute)
    {
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(execute);
        Policy = policy;
        _execute = execute;
    }

    public IntegrationProducerPolicyBinding Policy { get; }

    public IntegrationProducerPolicyAttempt Execute(
        IntegrationProducerPolicyAttemptAddress address,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(address);
        if (!ReferenceEquals(address.Policy, Policy))
        {
            throw new ArgumentException(
                "The producer address must use this access's exact policy.",
                nameof(address));
        }
        return _execute(address, cancellationToken);
    }
}

/// <summary>Opaque owner currency for one bound peer-evaluation request.</summary>
public interface IIntegrationPeerBinding
{
}

/// <summary>One terminal peer-binding receipt.</summary>
public abstract class IntegrationPeerBindingAttempt
{
    private protected IntegrationPeerBindingAttempt(
        IntegrationCandidateAttemptAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);
        Address = address;
    }

    public IntegrationCandidateAttemptAddress Address { get; }

    public sealed class Bound : IntegrationPeerBindingAttempt
    {
        public Bound(
            IntegrationCandidateAttemptAddress address,
            IIntegrationPeerBinding binding)
            : base(address)
        {
            ArgumentNullException.ThrowIfNull(binding);
            Binding = binding;
        }

        public IIntegrationPeerBinding Binding { get; }
    }

    public sealed class Failed : IntegrationPeerBindingAttempt
    {
        public Failed(
            IntegrationCandidateAttemptAddress address,
            IIntegrationCandidateFailure failure)
            : base(address)
        {
            ArgumentNullException.ThrowIfNull(failure);
            Failure = failure;
        }

        public IIntegrationCandidateFailure Failure { get; }
    }
}

/// <summary>
/// One candidate evaluation request plus every producer-issued source lookup
/// needed by Integration suppression policy.
/// </summary>
public sealed class IntegrationCandidateEvaluationRequest
{
    internal IntegrationCandidateEvaluationRequest(
        IntegrationCandidateAttemptAddress address,
        ImmutableArray<IntegrationCandidateEvidence> evidence)
    {
        Address = address;
        Evidence = evidence;
    }

    public IntegrationCandidateAttemptAddress Address { get; }
    public ImmutableArray<IntegrationCandidateEvidence> Evidence { get; }
}

/// <summary>
/// Complete peer-binding result for one context-local frozen request batch.
/// </summary>
public sealed class IntegrationPeerBindingBatch
{
    public IntegrationPeerBindingBatch(
        IIntegrationBindingContextIdentity bindingContext,
        IEnumerable<IntegrationPeerBindingAttempt> attempts)
    {
        ArgumentNullException.ThrowIfNull(bindingContext);
        ArgumentNullException.ThrowIfNull(attempts);
        BindingContext = bindingContext;
        Attempts = [.. attempts];
        if (Attempts.Any(attempt => attempt is null))
        {
            throw new ArgumentException(
                "Peer-binding attempts cannot contain null.",
                nameof(attempts));
        }
    }

    public IIntegrationBindingContextIdentity BindingContext { get; }
    public ImmutableArray<IntegrationPeerBindingAttempt> Attempts { get; }
}

/// <summary>
/// Context-batched peer binding over the complete candidate request set.
/// </summary>
public sealed class IntegrationPeerBindingAccess
{
    readonly Func<
        IIntegrationBindingContextIdentity,
        ImmutableArray<IntegrationCandidateEvaluationRequest>,
        CancellationToken,
        IntegrationPeerBindingBatch> _bind;

    public IntegrationPeerBindingAccess(
        IEnumerable<IIntegrationBindingContextIdentity> bindingContexts,
        Func<
            IIntegrationBindingContextIdentity,
            ImmutableArray<IntegrationCandidateEvaluationRequest>,
            CancellationToken,
            IntegrationPeerBindingBatch> bind)
    {
        ArgumentNullException.ThrowIfNull(bindingContexts);
        ArgumentNullException.ThrowIfNull(bind);
        BindingContexts = CopyContexts(bindingContexts);
        _bind = bind;
    }

    public ImmutableArray<IIntegrationBindingContextIdentity> BindingContexts
        { get; }

    public IntegrationPeerBindingBatch Bind(
        IIntegrationBindingContextIdentity bindingContext,
        ImmutableArray<IntegrationCandidateEvaluationRequest> requests,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(bindingContext);
        if (!BindingContexts.Contains(bindingContext)
            || requests.Any(request =>
                request is null
                || !EqualityComparer<IIntegrationBindingContextIdentity>
                    .Default.Equals(
                        request.Address.BindingContext,
                        bindingContext)))
        {
            throw new ArgumentException(
                "Peer-binding requests must belong to one declared binding context.",
                nameof(requests));
        }
        return _bind(bindingContext, requests, cancellationToken);
    }

    internal static ImmutableArray<IIntegrationBindingContextIdentity>
        CopyContexts(
            IEnumerable<IIntegrationBindingContextIdentity> contexts)
    {
        ImmutableArray<IIntegrationBindingContextIdentity> copied =
            [.. contexts];
        var seen = new HashSet<IIntegrationBindingContextIdentity>();
        foreach (IIntegrationBindingContextIdentity context in copied)
        {
            if (context is null || !seen.Add(context))
            {
                throw new ArgumentException(
                    "Binding contexts cannot contain null or duplicate identities.",
                    nameof(contexts));
            }
        }
        return copied;
    }
}

/// <summary>One terminal exact-resolution receipt.</summary>
public abstract class IntegrationCandidateResolutionAttempt
{
    private protected IntegrationCandidateResolutionAttempt(
        IntegrationPeerBindingAttempt.Bound binding)
    {
        ArgumentNullException.ThrowIfNull(binding);
        Binding = binding;
    }

    public IntegrationPeerBindingAttempt.Bound Binding { get; }
    public IntegrationCandidateAttemptAddress Address => Binding.Address;

    public sealed class Resolved : IntegrationCandidateResolutionAttempt
    {
        public Resolved(
            IntegrationPeerBindingAttempt.Bound binding,
            IntegrationResolvedPeer peer,
            IEnumerable<IntegrationResolvedPeer>?
                fulfillmentSourceResolutions = null)
            : base(binding)
        {
            ArgumentNullException.ThrowIfNull(peer);
            Peer = peer;
            FulfillmentSourceResolutions =
                [.. fulfillmentSourceResolutions ?? []];
            if (FulfillmentSourceResolutions.Any(
                    resolution => resolution is null))
            {
                throw new ArgumentException(
                    "Fulfillment-source resolutions cannot contain null.",
                    nameof(fulfillmentSourceResolutions));
            }
            if (FulfillmentSourceResolutions
                    .Select(resolution => resolution.Lookup)
                    .Distinct()
                    .Count()
                != FulfillmentSourceResolutions.Length)
            {
                throw new ArgumentException(
                    "Fulfillment-source resolutions cannot repeat a lookup.",
                    nameof(fulfillmentSourceResolutions));
            }
        }

        public IntegrationResolvedPeer Peer { get; }
        public ImmutableArray<IntegrationResolvedPeer>
            FulfillmentSourceResolutions { get; }
    }

    public sealed class Failed : IntegrationCandidateResolutionAttempt
    {
        public Failed(
            IntegrationPeerBindingAttempt.Bound binding,
            IIntegrationCandidateFailure failure)
            : base(binding)
        {
            ArgumentNullException.ThrowIfNull(failure);
            Failure = failure;
        }

        public IIntegrationCandidateFailure Failure { get; }
    }
}

/// <summary>
/// Exact-resolution receipts for every successfully bound request in one
/// context batch.
/// </summary>
public sealed class IntegrationCandidateResolutionBatch
{
    public IntegrationCandidateResolutionBatch(
        IntegrationPeerBindingBatch bindingBatch,
        IEnumerable<IntegrationCandidateResolutionAttempt> attempts)
    {
        ArgumentNullException.ThrowIfNull(bindingBatch);
        ArgumentNullException.ThrowIfNull(attempts);
        BindingBatch = bindingBatch;
        Attempts = [.. attempts];
        if (Attempts.Any(attempt => attempt is null))
        {
            throw new ArgumentException(
                "Candidate-resolution attempts cannot contain null.",
                nameof(attempts));
        }
    }

    public IntegrationPeerBindingBatch BindingBatch { get; }
    public ImmutableArray<IntegrationCandidateResolutionAttempt> Attempts
        { get; }
}

/// <summary>
/// Exact context-batched peer resolution over one owner-issued Type identity
/// domain.
/// </summary>
public sealed class IntegrationExactPeerResolutionAccess
{
    readonly Func<
        IntegrationPeerBindingBatch,
        CancellationToken,
        IntegrationCandidateResolutionBatch> _resolve;

    public IntegrationExactPeerResolutionAccess(
        IIntegrationTypeIdentityDomain identityDomain,
        IEnumerable<IIntegrationBindingContextIdentity> bindingContexts,
        Func<
            IntegrationPeerBindingBatch,
            CancellationToken,
            IntegrationCandidateResolutionBatch> resolve)
    {
        ArgumentNullException.ThrowIfNull(identityDomain);
        ArgumentNullException.ThrowIfNull(resolve);
        IdentityDomain = identityDomain;
        BindingContexts =
            IntegrationPeerBindingAccess.CopyContexts(bindingContexts);
        _resolve = resolve;
    }

    public IIntegrationTypeIdentityDomain IdentityDomain { get; }
    public ImmutableArray<IIntegrationBindingContextIdentity> BindingContexts
        { get; }

    public IntegrationCandidateResolutionBatch Resolve(
        IntegrationPeerBindingBatch bindingBatch,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(bindingBatch);
        if (!BindingContexts.Contains(bindingBatch.BindingContext))
        {
            throw new ArgumentException(
                "The binding batch must belong to this resolution access.",
                nameof(bindingBatch));
        }
        return _resolve(bindingBatch, cancellationToken);
    }
}

/// <summary>
/// Why executable Integration capability access could not produce a Census.
/// </summary>
public enum IntegrationCensusExecutionRejectionReason
{
    ExecutableBindingTypeMismatch,
    ParticipantContextMismatch,
    SelectedTypeParticipantMismatch,
    TypeIdentityDomainMismatch,
    BindingContextDomainMismatch,
    CompletenessMismatch,
    ProducerPolicyMismatch,
    InvalidProducerReceipt,
    InvalidPeerBindingBatch,
    InvalidResolutionBatch,
    InvalidResolutionEvidence,
}

/// <summary>Typed Integration execution rejection.</summary>
public sealed class IntegrationCensusExecutionRejection
{
    internal IntegrationCensusExecutionRejection(
        IntegrationCensusExecutionRejectionReason reason,
        AnalysisUniverseRequirementDescriptor? requirement = null,
        IntegrationProducerPolicyAttemptAddress? producerAddress = null,
        IntegrationCandidateAttemptAddress? candidateAddress = null,
        IIntegrationBindingContextIdentity? bindingContext = null)
    {
        if (!Enum.IsDefined(reason))
            throw new ArgumentOutOfRangeException(nameof(reason));
        Reason = reason;
        Requirement = requirement;
        ProducerAddress = producerAddress;
        CandidateAddress = candidateAddress;
        BindingContext = bindingContext;
    }

    public IntegrationCensusExecutionRejectionReason Reason { get; }
    public AnalysisUniverseRequirementDescriptor? Requirement { get; }
    public IntegrationProducerPolicyAttemptAddress? ProducerAddress { get; }
    public IntegrationCandidateAttemptAddress? CandidateAddress { get; }
    public IIntegrationBindingContextIdentity? BindingContext { get; }
}

/// <summary>One terminal sequential Integration Census execution outcome.</summary>
public abstract class IntegrationCensusExecutionResult
{
    private protected IntegrationCensusExecutionResult()
    {
    }

    public sealed class Ready : IntegrationCensusExecutionResult
    {
        internal Ready(IntegrationCensusSnapshot snapshot) =>
            Snapshot = snapshot;

        public IntegrationCensusSnapshot Snapshot { get; }
    }

    public sealed class IssuanceRejected : IntegrationCensusExecutionResult
    {
        internal IssuanceRejected(
            AnalysisUniverseIssuanceRejection rejection) =>
            Rejection = rejection;

        public AnalysisUniverseIssuanceRejection Rejection { get; }
    }

    public sealed class ExecutionRejected : IntegrationCensusExecutionResult
    {
        internal ExecutionRejected(
            IntegrationCensusExecutionRejection rejection) =>
            Rejection = rejection;

        public IntegrationCensusExecutionRejection Rejection { get; }
    }

    public sealed class Cancelled : IntegrationCensusExecutionResult
    {
        internal Cancelled()
        {
        }
    }
}

/// <summary>
/// A producer-policy receipt synthesized when its source participant cannot be
/// executed.
/// </summary>
public sealed class IntegrationParticipantProducerUnavailable :
    IIntegrationProducerPolicyUnavailable
{
    internal IntegrationParticipantProducerUnavailable(
        IntegrationSourceParticipantAttempt sourceAttempt) =>
        SourceAttempt = sourceAttempt;

    public IntegrationSourceParticipantAttempt SourceAttempt { get; }
}

/// <summary>
/// Executes one Workspace-backed Integration Census deterministically and
/// sequentially.
/// </summary>
public static class IntegrationCensusExecutor
{
    public static IntegrationCensusExecutionResult Execute(
        AnalysisUniverseOffer offer,
        AnalysisRequestPlan plan,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(offer);
        ArgumentNullException.ThrowIfNull(plan);
        IntegrationCensusSnapshot.ValidatePlan(plan);
        ImmutableArray<IntegrationProducerPolicyBinding> requiredPolicies =
            IntegrationCensusSnapshot.RequiredPolicies(plan);

        AnalysisUniverseIssuanceResult issuance =
            offer.IssueExecutionAccess(plan, cancellationToken);
        if (issuance is AnalysisUniverseIssuanceResult.Rejected rejected)
        {
            return new IntegrationCensusExecutionResult.IssuanceRejected(
                rejected.Rejection);
        }
        if (issuance is AnalysisUniverseIssuanceResult.Cancelled)
            return new IntegrationCensusExecutionResult.Cancelled();
        if (issuance is not AnalysisUniverseIssuanceResult.Ready ready)
        {
            throw new InvalidOperationException(
                "Unknown analysis-universe issuance outcome.");
        }

        using (ready.Access)
        {
            try
            {
                return ExecuteCore(
                    ready.Access,
                    requiredPolicies,
                    cancellationToken);
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested)
            {
                return new IntegrationCensusExecutionResult.Cancelled();
            }
        }
    }

    static IntegrationCensusExecutionResult ExecuteCore(
        AnalysisUniverseExecutionAccess execution,
        ImmutableArray<IntegrationProducerPolicyBinding> requiredPolicies,
        CancellationToken cancellationToken)
    {
        if (!TryGetAccess(
                execution,
                IntegrationAnalysisCatalog.SelectedTypesRequirement,
                out IntegrationSelectedTypeAccess? selected,
                out IntegrationCensusExecutionResult? rejection)
            || !TryGetAccess(
                execution,
                IntegrationAnalysisCatalog.OrderedParticipantsRequirement,
                out IntegrationSourceParticipantAccess? participants,
                out rejection)
            || !TryGetAccess(
                execution,
                IntegrationAnalysisCatalog.BindingContextsRequirement,
                out IntegrationBindingContextAccess? contexts,
                out rejection)
            || !TryGetAccess(
                execution,
                IntegrationAnalysisCatalog.PeerBindingRequirement,
                out IntegrationPeerBindingAccess? binding,
                out rejection)
            || !TryGetAccess(
                execution,
                IntegrationAnalysisCatalog.ExactPeerResolutionRequirement,
                out IntegrationExactPeerResolutionAccess? resolution,
                out rejection)
            || !TryGetAccess(
                execution,
                IntegrationAnalysisCatalog.CompletenessRequirement,
                out IntegrationCompletenessAccess? completeness,
                out rejection))
        {
            return rejection!;
        }

        var producerAccesses =
            new Dictionary<
                IntegrationProducerPolicyBinding,
                IntegrationProducerPolicyAccess>(
                    ReferenceEqualityComparer.Instance);
        foreach (IntegrationProducerPolicyBinding policy
            in requiredPolicies)
        {
            if (!TryGetAccess(
                    execution,
                    policy.UniverseRequirement,
                    out IntegrationProducerPolicyAccess? access,
                    out rejection))
            {
                return rejection!;
            }
            if (!ReferenceEquals(access.Policy, policy))
            {
                return Reject(
                    IntegrationCensusExecutionRejectionReason
                        .ProducerPolicyMismatch,
                    policy.UniverseRequirement);
            }
            producerAccesses.Add(policy, access);
        }

        if (!ReferenceEquals(
                selected.IdentityDomain,
                resolution.IdentityDomain))
        {
            return Reject(
                IntegrationCensusExecutionRejectionReason
                    .TypeIdentityDomainMismatch);
        }
        if (!SameIdentities(
                contexts.BindingContexts,
                binding.BindingContexts)
            || !SameIdentities(
                contexts.BindingContexts,
                resolution.BindingContexts))
        {
            return Reject(
                IntegrationCensusExecutionRejectionReason
                    .BindingContextDomainMismatch);
        }
        if (!ReferenceEquals(
                completeness.Completeness,
                execution.Plan.UniverseCompleteness)
            || !SameReferences(
                completeness.Failures,
                execution.Plan.UniverseFailures))
        {
            return Reject(
                IntegrationCensusExecutionRejectionReason
                    .CompletenessMismatch);
        }

        ImmutableArray<IntegrationSourceBindingContextIncidence>
            canonicalIncidence;
        try
        {
            canonicalIncidence =
                IntegrationCensusSnapshot.CanonicalizeIncidence(
                    participants.Participants,
                    contexts.SourceIncidence,
                    nameof(contexts));
        }
        catch (ArgumentException)
        {
            return Reject(
                IntegrationCensusExecutionRejectionReason
                    .ParticipantContextMismatch);
        }

        var participantSet =
            participants.Participants.ToHashSet();
        if (selected.SelectedTypes.Any(type =>
                !participantSet.Contains(type.Participant)))
        {
            return Reject(
                IntegrationCensusExecutionRejectionReason
                    .SelectedTypeParticipantMismatch);
        }

        cancellationToken.ThrowIfCancellationRequested();
        var producerAttempts =
            ImmutableArray.CreateBuilder<IntegrationProducerPolicyAttempt>(
                participants.Participants.Length * requiredPolicies.Length);
        var selectedTypeSet = selected.SelectedTypes.ToHashSet();
        for (int participantIndex = 0;
            participantIndex < participants.Participants.Length;
            participantIndex++)
        {
            IntegrationSourceParticipantIdentity participant =
                participants.Participants[participantIndex];
            IntegrationSourceParticipantAttempt sourceAttempt =
                participants.Attempts[participantIndex];
            foreach (IntegrationProducerPolicyBinding policy
                in requiredPolicies)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var address =
                    new IntegrationProducerPolicyAttemptAddress(
                        participant,
                        policy);
                if (sourceAttempt
                    is not IntegrationSourceParticipantAttempt.Available)
                {
                    producerAttempts.Add(
                        new IntegrationProducerPolicyAttempt.Unavailable(
                            address,
                            new IntegrationParticipantProducerUnavailable(
                                sourceAttempt)));
                    continue;
                }

                cancellationToken.ThrowIfCancellationRequested();
                IntegrationProducerPolicyAttempt? attempt =
                    producerAccesses[policy].Execute(
                        address,
                        cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                if (attempt is null
                    || !attempt.Address.Equals(address)
                    || attempt
                        is IntegrationProducerPolicyAttempt.Completed completed
                        && completed.Candidates.Any(candidate =>
                            !selectedTypeSet.Contains(
                                IntegrationCensusSnapshot.SourceTypeOf(
                                    candidate))))
                {
                    return Reject(
                        IntegrationCensusExecutionRejectionReason
                            .InvalidProducerReceipt,
                        policy.UniverseRequirement,
                        producerAddress: address);
                }
                producerAttempts.Add(attempt);
            }
        }

        ImmutableArray<IntegrationProducerPolicyAttempt> produced =
            producerAttempts.MoveToImmutable();
        ImmutableArray<IntegrationCensusCandidate> candidates =
            IntegrationCensusSnapshot.BuildCandidates(produced);
        Dictionary<
            IntegrationCandidateIdentity,
            IntegrationCensusCandidate> candidateByIdentity =
                candidates.ToDictionary(static candidate => candidate.Identity);
        var incidenceByParticipant =
            canonicalIncidence.ToDictionary(
                static incidence => incidence.Participant);
        var requestsByContext = contexts.BindingContexts.ToDictionary(
            static context => context,
            static _ =>
                ImmutableArray.CreateBuilder<
                    IntegrationCandidateEvaluationRequest>());
        foreach (IntegrationCensusCandidate candidate in candidates)
        {
            foreach (IIntegrationBindingContextIdentity context
                in incidenceByParticipant[
                    candidate.Identity.Source.Participant].BindingContexts)
            {
                var address = new IntegrationCandidateAttemptAddress(
                    candidate.Identity,
                    context);
                requestsByContext[context].Add(
                    new IntegrationCandidateEvaluationRequest(
                        address,
                        candidate.Evidence));
            }
        }

        var candidateFailures =
            new Dictionary<
                IntegrationCandidateAttemptAddress,
                IIntegrationCandidateFailure>();
        var candidateResolutions =
            new Dictionary<
                IntegrationCandidateAttemptAddress,
                IntegrationCandidateResolutionAttempt.Resolved>();
        foreach (IIntegrationBindingContextIdentity context
            in contexts.BindingContexts)
        {
            ImmutableArray<IntegrationCandidateEvaluationRequest> requests =
                requestsByContext[context].ToImmutable();
            if (requests.IsEmpty)
                continue;

            cancellationToken.ThrowIfCancellationRequested();
            IntegrationPeerBindingBatch? batch =
                binding.Bind(
                    context,
                    requests,
                    cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (batch is null
                || !EqualityComparer<IIntegrationBindingContextIdentity>
                    .Default.Equals(batch.BindingContext, context)
                || !TryCanonicalize(
                    requests.Select(static request => request.Address),
                    batch.Attempts,
                    static attempt => attempt.Address,
                    out ImmutableArray<IntegrationPeerBindingAttempt>
                        bindingAttempts))
            {
                return Reject(
                    IntegrationCensusExecutionRejectionReason
                        .InvalidPeerBindingBatch,
                    bindingContext: context);
            }

            var boundAddresses =
                ImmutableArray.CreateBuilder<
                    IntegrationCandidateAttemptAddress>();
            foreach (IntegrationPeerBindingAttempt attempt
                in bindingAttempts)
            {
                switch (attempt)
                {
                    case IntegrationPeerBindingAttempt.Bound:
                        boundAddresses.Add(attempt.Address);
                        break;
                    case IntegrationPeerBindingAttempt.Failed failed:
                        candidateFailures.Add(
                            failed.Address,
                            failed.Failure);
                        break;
                    default:
                        return Reject(
                            IntegrationCensusExecutionRejectionReason
                                .InvalidPeerBindingBatch,
                            candidateAddress: attempt.Address,
                            bindingContext: context);
                }
            }

            if (boundAddresses.Count == 0)
                continue;

            cancellationToken.ThrowIfCancellationRequested();
            IntegrationCandidateResolutionBatch? resolved =
                resolution.Resolve(batch, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (resolved is null
                || !ReferenceEquals(resolved.BindingBatch, batch)
                || !TryCanonicalize(
                    bindingAttempts.OfType<
                        IntegrationPeerBindingAttempt.Bound>(),
                    resolved.Attempts,
                    static attempt => attempt.Binding,
                    out ImmutableArray<
                        IntegrationCandidateResolutionAttempt>
                        resolutionAttempts))
            {
                return Reject(
                    IntegrationCensusExecutionRejectionReason
                        .InvalidResolutionBatch,
                    bindingContext: context);
            }

            foreach (IntegrationCandidateResolutionAttempt attempt
                in resolutionAttempts)
            {
                switch (attempt)
                {
                    case IntegrationCandidateResolutionAttempt.Resolved success:
                        if (!ValidateResolutionEvidence(
                                candidateByIdentity,
                                success))
                        {
                            return Reject(
                                IntegrationCensusExecutionRejectionReason
                                    .InvalidResolutionEvidence,
                                candidateAddress: success.Address,
                                bindingContext: context);
                        }
                        candidateResolutions.Add(
                            success.Address,
                            success);
                        break;
                    case IntegrationCandidateResolutionAttempt.Failed failed:
                        candidateFailures.Add(
                            failed.Address,
                            failed.Failure);
                        break;
                    default:
                        return Reject(
                            IntegrationCensusExecutionRejectionReason
                                .InvalidResolutionBatch,
                            candidateAddress: attempt.Address,
                            bindingContext: context);
                }
            }
        }

        var observationsByContextAndConcept =
            new Dictionary<ObservationKey, List<
                IntegrationCandidateResolutionAttempt.Resolved>>();
        foreach (IntegrationCensusCandidate candidate in candidates)
        {
            if (!ReferenceEquals(
                    candidate.Identity.Relationship,
                    InspectionGraphIntegrationsCatalog.IntegrationObserved))
            {
                continue;
            }
            foreach (IIntegrationBindingContextIdentity context
                in incidenceByParticipant[
                    candidate.Identity.Source.Participant].BindingContexts)
            {
                var address = new IntegrationCandidateAttemptAddress(
                    candidate.Identity,
                    context);
                if (!candidateResolutions.TryGetValue(
                        address,
                        out IntegrationCandidateResolutionAttempt.Resolved?
                            resolved))
                {
                    continue;
                }

                var key = new ObservationKey(
                    candidate.Identity.Concept,
                    context);
                if (!observationsByContextAndConcept.TryGetValue(
                        key,
                        out List<
                            IntegrationCandidateResolutionAttempt.Resolved>?
                            observations))
                {
                    observations = [];
                    observationsByContextAndConcept.Add(key, observations);
                }
                observations.Add(resolved);
            }
        }

        var candidateAttempts =
            ImmutableArray.CreateBuilder<IntegrationCandidateAttempt>();
        foreach (IntegrationCensusCandidate candidate in candidates)
        {
            foreach (IIntegrationBindingContextIdentity context
                in incidenceByParticipant[
                    candidate.Identity.Source.Participant].BindingContexts)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var address = new IntegrationCandidateAttemptAddress(
                    candidate.Identity,
                    context);
                if (candidateFailures.TryGetValue(
                        address,
                        out IIntegrationCandidateFailure? failure))
                {
                    candidateAttempts.Add(
                        new IntegrationCandidateAttempt.Failed(
                            address,
                            failure));
                    continue;
                }

                IntegrationCandidateResolutionAttempt.Resolved resolved =
                    candidateResolutions[address];
                IntegrationCandidateAttempt.Suppressed? suppressed =
                    TrySuppress(
                        address,
                        resolved,
                        observationsByContextAndConcept);
                if (suppressed is not null)
                {
                    candidateAttempts.Add(suppressed);
                    continue;
                }

                IntegrationCandidateDisposition disposition =
                    selectedTypeSet.Contains(resolved.Peer.Terminal)
                        ? new IntegrationCandidateDisposition.In(
                            resolved.Peer)
                        : new IntegrationCandidateDisposition.Out(
                            resolved.Peer);
                candidateAttempts.Add(
                    new IntegrationCandidateAttempt.Classified(
                        address,
                        disposition,
                        resolved.FulfillmentSourceResolutions));
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        return new IntegrationCensusExecutionResult.Ready(
            new IntegrationCensusSnapshot(
                execution.Plan,
                participants.Participants,
                selected.SelectedTypes,
                contexts,
                participants.Attempts,
                produced,
                candidateAttempts.ToImmutable()));
    }

    static IntegrationCandidateAttempt.Suppressed? TrySuppress(
        IntegrationCandidateAttemptAddress address,
        IntegrationCandidateResolutionAttempt.Resolved resolved,
        Dictionary<
            ObservationKey,
            List<IntegrationCandidateResolutionAttempt.Resolved>>
            observationsByContextAndConcept)
    {
        IntegrationCandidateIdentity candidate = address.Candidate;
        if (!ReferenceEquals(
                candidate.Relationship,
                InspectionGraphIntegrationsCatalog.IntegrationOpportunity))
        {
            return null;
        }

        IntegrationTypeIdentity source =
            IntegrationCensusSnapshot.SourceTypeOf(candidate);
        if (!observationsByContextAndConcept.TryGetValue(
                new ObservationKey(
                    candidate.Concept,
                    address.BindingContext),
                out List<
                    IntegrationCandidateResolutionAttempt.Resolved>?
                    observations))
        {
            return null;
        }

        foreach (IntegrationCandidateResolutionAttempt.Resolved fulfilling
            in observations)
        {
            if (!fulfilling.Peer.Terminal.Equals(
                    resolved.Peer.Terminal)
                || !fulfilling.FulfillmentSourceResolutions.Any(
                    receiver => receiver.Terminal.Equals(source)))
            {
                continue;
            }

            return new IntegrationCandidateAttempt.Suppressed(
                address,
                fulfilling.Address,
                new IntegrationOpportunityFulfillment(
                    source,
                    resolved.Peer));
        }

        return null;
    }

    static bool ValidateResolutionEvidence(
        Dictionary<
            IntegrationCandidateIdentity,
            IntegrationCensusCandidate> candidates,
        IntegrationCandidateResolutionAttempt.Resolved resolved)
    {
        try
        {
            IntegrationCensusSnapshot.ValidateResolution(
                resolved.Address.Candidate,
                resolved.Peer);
            IntegrationCensusCandidate candidate =
                candidates[resolved.Address.Candidate];
            if (!ReferenceEquals(
                    candidate.Identity.Relationship,
                    InspectionGraphIntegrationsCatalog.IntegrationObserved)
                && !resolved.FulfillmentSourceResolutions.IsEmpty)
            {
                return false;
            }
            IntegrationCandidatePeerIdentity.NamedType[] declaredSources =
            [
                .. candidate.Evidence
                    .SelectMany(evidence =>
                        evidence.FulfillmentSourceLookups)
                    .Distinct(),
            ];
            if (declaredSources.Length
                != resolved.FulfillmentSourceResolutions.Length)
            {
                return false;
            }
            foreach (IntegrationResolvedPeer source
                in resolved.FulfillmentSourceResolutions)
            {
                if (!candidate.Evidence.Any(evidence =>
                        evidence.FulfillmentSourceLookups.Any(
                            lookup => lookup.Equals(source.Lookup))))
                {
                    return false;
                }
                IntegrationCensusSnapshot.ValidateResolvedLookup(
                    source.Lookup,
                    source);
            }
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    static bool TryGetAccess<TAccess>(
        AnalysisUniverseExecutionAccess execution,
        AnalysisUniverseRequirementDescriptor requirement,
        [System.Diagnostics.CodeAnalysis.NotNullWhen(true)]
        out TAccess? access,
        [System.Diagnostics.CodeAnalysis.NotNullWhen(false)]
        out IntegrationCensusExecutionResult? rejection)
        where TAccess : class
    {
        AnalysisUniverseRequirementBinding? binding =
            execution.Bindings.FirstOrDefault(candidate =>
                ReferenceEquals(candidate.Requirement, requirement));
        if (binding
            is AnalysisUniverseRequirementBinding<TAccess> typed)
        {
            access = typed.Access;
            rejection = null;
            return true;
        }

        access = null;
        rejection = Reject(
            IntegrationCensusExecutionRejectionReason
                .ExecutableBindingTypeMismatch,
            requirement);
        return false;
    }

    static bool TryCanonicalize<TAddress, TAttempt>(
        IEnumerable<TAddress> expected,
        IEnumerable<TAttempt> supplied,
        Func<TAttempt, TAddress> addressOf,
        out ImmutableArray<TAttempt> canonical)
        where TAddress : class
        where TAttempt : class
    {
        var byAddress = new Dictionary<TAddress, TAttempt>();
        foreach (TAttempt attempt in supplied)
        {
            if (attempt is null
                || !byAddress.TryAdd(addressOf(attempt), attempt))
            {
                canonical = [];
                return false;
            }
        }

        var ordered = ImmutableArray.CreateBuilder<TAttempt>();
        foreach (TAddress address in expected)
        {
            if (!byAddress.Remove(address, out TAttempt? attempt))
            {
                canonical = [];
                return false;
            }
            ordered.Add(attempt);
        }
        if (byAddress.Count != 0)
        {
            canonical = [];
            return false;
        }

        canonical = ordered.ToImmutable();
        return true;
    }

    static bool SameIdentities<T>(
        ImmutableArray<T> left,
        ImmutableArray<T> right)
        where T : class
    {
        if (left.Length != right.Length)
            return false;
        for (int index = 0; index < left.Length; index++)
        {
            if (!EqualityComparer<T>.Default.Equals(
                    left[index],
                    right[index]))
            {
                return false;
            }
        }
        return true;
    }

    static bool SameReferences<T>(
        ImmutableArray<T> left,
        ImmutableArray<T> right)
        where T : class
    {
        if (left.Length != right.Length)
            return false;
        for (int index = 0; index < left.Length; index++)
        {
            if (!ReferenceEquals(left[index], right[index]))
                return false;
        }
        return true;
    }

    static IntegrationCensusExecutionResult.ExecutionRejected Reject(
        IntegrationCensusExecutionRejectionReason reason,
        AnalysisUniverseRequirementDescriptor? requirement = null,
        IntegrationProducerPolicyAttemptAddress? producerAddress = null,
        IntegrationCandidateAttemptAddress? candidateAddress = null,
        IIntegrationBindingContextIdentity? bindingContext = null) =>
        new(
            new IntegrationCensusExecutionRejection(
                reason,
                requirement,
                producerAddress,
                candidateAddress,
                bindingContext));

    readonly record struct ObservationKey(
        IntegrationConceptDescriptor Concept,
        IIntegrationBindingContextIdentity BindingContext);
}
