using System.Collections.Immutable;

using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;

using ILInspector.Analysis;
using ILInspector.Metadata;
using ILInspector.MetadataPrimitives;

namespace ILInspector.Research;

/// <summary>
/// The final construction boundary for one <see cref="ResearchTargetResolution"/>.
/// </summary>
/// <remarks>
/// <para>
/// This validator does not trust the nullable field shape of the result it is
/// handed. It re-derives the expected scope, domain, request, attempt, census,
/// key, proof, taint, and correspondence sets from the caller's planning
/// request and the admitted population, then rejects both missing and stale
/// entries. It then re-runs parent identity, exact-once accounting,
/// module/address/token/relationship-role binding, the diagnostic-kind to
/// outcome-arm mapping, candidate retention, and request-to-result binding.
/// <c>ResearchTargetFinalValidation_RejectsBrokenSemanticBindings</c> is the
/// named non-vacuity gate for this construction boundary.
/// </para>
/// <para>
/// A violation is a Research defect, not an expected caller shape, so it throws
/// rather than producing a typed rejection. Every invalid caller shape has
/// already become a <see cref="ResearchTargetPlanningRejection"/> before this
/// boundary runs.
/// </para>
/// </remarks>
static class ResearchTargetResolutionValidator
{
    internal static void Validate(
        ResearchTargetPlanningRequest request,
        ResearchTargetResolution resolution,
        ImmutableArray<ResearchTargetValidationEvidence> evidence)
    {
        ResearchAdmittedPopulation population = request.Population;
        Require(
            ReferenceEquals(resolution.Operation, population.Operation),
            "The resolution must name the admitted operation.");

        Dictionary<ResearchAdmittedInput, ResearchTargetInputRole> roles = new(
            ReferenceEqualityComparer.Instance);
        foreach (ResearchTargetInputRoleAssignment? assignment in
            request.InputRoles)
        {
            Require(
                assignment is not null
                    && roles.TryAdd(assignment.Input, assignment.Role),
                "Every admitted input must carry exactly one validated role.");
        }

        Require(
            roles.Count == population.Inputs.Length,
            "The role assignment must cover every admitted input exactly once.");

        Dictionary<ResearchTargetRequestId, ResearchTargetValidationEvidence>
            evidenceByRequest = new(ReferenceEqualityComparer.Instance);
        foreach (ResearchTargetValidationEvidence item in evidence)
        {
            Require(
                evidenceByRequest.TryAdd(item.Request.Id, item),
                "Every request must carry exactly one validation-evidence entry.");
        }

        Dictionary<ResearchComparisonQuestionId, ResearchAdmittedQuestion>
            byQuestion = new(ReferenceEqualityComparer.Instance);
        foreach (ResearchAdmittedQuestion admitted in population.Questions)
            byQuestion.Add(admitted.Id, admitted);

        // Scopes derive bijectively from the selection occurrences.
        Require(
            resolution.Scopes.Length == request.Selections.Length,
            "Every selection occurrence must mint exactly one scope.");
        HashSet<ResearchTargetScopeId> scopeIds = new(
            ReferenceEqualityComparer.Instance);
        HashSet<ResearchTargetDomainId> domainIds = new(
            ReferenceEqualityComparer.Instance);
        HashSet<ResearchTargetRequestId> requestIds = new(
            ReferenceEqualityComparer.Instance);
        HashSet<ResearchTargetAttemptId> attemptIds = new(
            ReferenceEqualityComparer.Instance);

        for (int index = 0; index < resolution.Scopes.Length; index++)
        {
            ResearchTargetScope scope = resolution.Scopes[index];
            ResearchMemberSelectionOccurrence selection =
                request.Selections[index]
                ?? throw Violation("A validated selection must not be null.");

            Require(scopeIds.Add(scope.Id), "Scope identities must be distinct.");
            Require(
                ReferenceEquals(scope.Id.Question, selection.Question),
                "A scope must be parented by its selection's question.");
            Require(
                ReferenceEquals(scope.Question, selection.Question)
                    && ReferenceEquals(
                        scope.Id.Operation,
                        population.Operation),
                "A scope must be parented by the admitted operation and question.");
            Require(
                string.Equals(
                    scope.DeclaringTypeFullName,
                    selection.DeclaringTypeFullName,
                    StringComparison.Ordinal)
                    && scope.Selector == selection.Selector
                    && scope.Kind == selection.Kind,
                "A scope must retain its selection's exact intent.");

            ValidateScope(
                scope,
                selection,
                byQuestion[selection.Question],
                roles,
                evidenceByRequest,
                domainIds,
                requestIds,
                attemptIds);
        }

        // Nothing stale: the flattened views must equal the per-scope contents.
        Require(
            resolution.Domains.Length == domainIds.Count
                && resolution.Requests.Length == requestIds.Count
                && resolution.Attempts.Length == attemptIds.Count,
            "The flattened resolution views must contain exactly the planned entries.");
        foreach (ResearchTargetAttempt attempt in resolution.Attempts)
        {
            Require(
                resolution.TryGetAttempt(
                        attempt.Request.Id,
                        out ResearchTargetAttempt? bound)
                    && ReferenceEquals(bound, attempt),
                "Every request must bind to exactly its own attempt.");
        }

        Require(
            evidenceByRequest.Count == requestIds.Count,
            "Validation evidence must account for every request exactly once.");

        ValidateCensusAndCorrespondence(resolution);
    }

    static void ValidateCensusAndCorrespondence(
        ResearchTargetResolution resolution)
    {
        Require(
            resolution.Censuses.Length == resolution.Domains.Length * 2,
            "Every domain must retain exactly two side-local censuses.");

        int outcomeCount = 0;
        foreach (ResearchTargetDomain domain in resolution.Domains)
        {
            ImmutableArray<ResearchTargetDomainSideCensus> domainCensuses =
            [
                .. resolution.Censuses.Where(
                    census => ReferenceEquals(census.Domain, domain)),
            ];
            Require(
                domainCensuses.Length == 2
                    && domainCensuses[0].Side
                        == ResearchComparisonSide.Before
                    && domainCensuses[1].Side
                        == ResearchComparisonSide.After,
                "Every domain must retain ordered before and after censuses.");

            ResearchTargetDomainSideCensus before = domainCensuses[0];
            ResearchTargetDomainSideCensus after = domainCensuses[1];
            ValidateCensus(domain, before);
            ValidateCensus(domain, after);

            ImmutableArray<ResearchTargetCorrespondenceOutcome> outcomes =
            [
                .. resolution.Correspondences.Where(
                    outcome => ReferenceEquals(outcome.Domain, domain)),
            ];
            outcomeCount += outcomes.Length;
            ValidateDomainCorrespondence(domain, before, after, outcomes);
        }

        Require(
            outcomeCount == resolution.Correspondences.Length,
            "Every correspondence outcome must belong to one exact domain.");
    }

    static void ValidateCensus(
        ResearchTargetDomain domain,
        ResearchTargetDomainSideCensus census)
    {
        Require(
            ReferenceEquals(census.Domain, domain)
                && ReferenceEquals(census.DomainId, domain.Id)
                && ReferenceEquals(census.Scope, domain.Scope),
            "A census must retain its exact domain parentage.");

        ImmutableArray<ResearchTargetInputDisposition> expectedInputs =
            domain.Side(census.Side);
        ImmutableArray<ResearchTargetAttempt> expectedAttempts =
        [
            .. domain.Attempts.Where(
                attempt => attempt.Request.Side == census.Side),
        ];
        Require(
            SameReferences(census.Inputs, expectedInputs)
                && SameReferences(census.Attempts, expectedAttempts),
            "A census must retain the complete ordered side-local evidence.");

        bool healthy =
            expectedInputs.All(
                input =>
                    input.Kind == ResearchTargetDispositionKind.Requested)
            && expectedAttempts.Length == expectedInputs.Length
            && expectedAttempts.All(
                attempt => attempt.Outcome.Kind
                    is ResearchTargetOutcomeKind.Resolved
                        or ResearchTargetOutcomeKind.NotFound);
        Require(
            census.Health
                == (healthy
                    ? ResearchTargetCensusHealth.Healthy
                    : ResearchTargetCensusHealth.Blocked),
            "Census health must derive from complete terminal evidence.");
    }

    static void ValidateDomainCorrespondence(
        ResearchTargetDomain domain,
        ResearchTargetDomainSideCensus before,
        ResearchTargetDomainSideCensus after,
        ImmutableArray<ResearchTargetCorrespondenceOutcome> outcomes)
    {
        ImmutableArray<ResearchTargetAttempt> resolved =
        [
            .. domain.Attempts.Where(
                attempt =>
                    attempt.Outcome.Kind
                        == ResearchTargetOutcomeKind.Resolved),
        ];

        if (before.Health == ResearchTargetCensusHealth.Blocked
            || after.Health == ResearchTargetCensusHealth.Blocked)
        {
            if (resolved.IsEmpty)
            {
                var unavailable = RequireSingle<
                    ResearchTargetCorrespondenceOutcome.DomainUnavailable>(
                        outcomes,
                        "A blocked domain without a target must be unavailable.");
                ValidateTaint(
                    unavailable.Taint,
                    domain,
                    ResearchTargetTaintKind.BlockedDomain,
                    BlockingAttempts(domain),
                    [],
                    IncompleteInputs(domain));
                return;
            }

            Require(
                outcomes.Length == resolved.Length
                    && outcomes.All(
                        outcome => outcome
                            is ResearchTargetCorrespondenceOutcome
                                .CounterpartUnavailable),
                "A blocked domain must retain one unavailable counterpart per resolved endpoint.");
            for (int index = 0; index < resolved.Length; index++)
            {
                var unavailable =
                    (ResearchTargetCorrespondenceOutcome
                        .CounterpartUnavailable)outcomes[index];
                ValidateUnavailableTarget(
                    unavailable,
                    resolved[index],
                    expectKeys: false);
                ValidateTaint(
                    unavailable.Taint,
                    domain,
                    ResearchTargetTaintKind.BlockedDomain,
                    BlockingAttempts(domain),
                    [],
                    IncompleteInputs(domain));
            }

            return;
        }

        ResearchTargetAttempt? beforeTarget =
            resolved.SingleOrDefault(
                attempt =>
                    attempt.Request.Side
                        == ResearchComparisonSide.Before);
        ResearchTargetAttempt? afterTarget =
            resolved.SingleOrDefault(
                attempt =>
                    attempt.Request.Side
                        == ResearchComparisonSide.After);

        if (beforeTarget is not null && afterTarget is not null)
        {
            ResearchTargetCorrespondenceKey beforeKey =
                ExpectedCorrespondenceKey(beforeTarget);
            ResearchTargetCorrespondenceKey afterKey =
                ExpectedCorrespondenceKey(afterTarget);
            if (beforeKey.Equals(afterKey))
            {
                var paired = RequireSingle<
                    ResearchTargetCorrespondenceOutcome.Paired>(
                        outcomes,
                        "Equal side-local targets must produce one paired outcome.");
                ValidateTarget(paired.Before, beforeTarget);
                ValidateTarget(paired.After, afterTarget);
                return;
            }

            Require(
                outcomes.Length == 2
                    && outcomes.All(
                        outcome => outcome
                            is ResearchTargetCorrespondenceOutcome
                                .CounterpartUnavailable),
                "Selection drift must produce two unavailable counterparts.");
            ResearchTargetAttempt[] attempts = [beforeTarget, afterTarget];
            ImmutableArray<ResearchStrictTargetKey> keys =
                [.. attempts.Select(ExpectedStrictKey)];
            for (int index = 0; index < attempts.Length; index++)
            {
                var unavailable =
                    (ResearchTargetCorrespondenceOutcome
                        .CounterpartUnavailable)outcomes[index];
                ValidateUnavailableTarget(
                    unavailable,
                    attempts[index],
                    expectKeys: true);
                ValidateTaint(
                    unavailable.Taint,
                    domain,
                    ResearchTargetTaintKind.SelectionDrift,
                    [beforeTarget, afterTarget],
                    keys,
                    []);
            }

            return;
        }

        if (beforeTarget is not null)
        {
            ValidateOneSided(
                domain,
                beforeTarget,
                after,
                beforeSide: true,
                outcomes);
            return;
        }

        if (afterTarget is not null)
        {
            ValidateOneSided(
                domain,
                afterTarget,
                before,
                beforeSide: false,
                outcomes);
            return;
        }

        var absent =
            RequireSingle<ResearchTargetCorrespondenceOutcome.Absent>(
                outcomes,
                "A healthy domain without targets must be proven absent.");
        ValidateDomainAbsenceProof(absent.BeforeAbsence, before);
        ValidateDomainAbsenceProof(absent.AfterAbsence, after);
    }

    static void ValidateOneSided(
        ResearchTargetDomain domain,
        ResearchTargetAttempt target,
        ResearchTargetDomainSideCensus opposite,
        bool beforeSide,
        ImmutableArray<ResearchTargetCorrespondenceOutcome> outcomes)
    {
        bool proven = KeyAbsenceIsProven(opposite, target);
        if (proven && beforeSide)
        {
            var beforeOnly = RequireSingle<
                ResearchTargetCorrespondenceOutcome.BeforeOnly>(
                    outcomes,
                    "A resolved before target with positive after absence must be before-only.");
            ValidateTarget(beforeOnly.Before, target);
            ValidateKeyAbsenceProof(
                beforeOnly.AfterAbsence,
                opposite,
                target);
            return;
        }

        if (proven)
        {
            var afterOnly = RequireSingle<
                ResearchTargetCorrespondenceOutcome.AfterOnly>(
                    outcomes,
                    "A resolved after target with positive before absence must be after-only.");
            ValidateTarget(afterOnly.After, target);
            ValidateKeyAbsenceProof(
                afterOnly.BeforeAbsence,
                opposite,
                target);
            return;
        }

        var unavailable = RequireSingle<
            ResearchTargetCorrespondenceOutcome.CounterpartUnavailable>(
                outcomes,
                "A one-sided target without covering absence must be unavailable.");
        ValidateUnavailableTarget(unavailable, target, expectKeys: true);
        ValidateTaint(
            unavailable.Taint,
            domain,
            ResearchTargetTaintKind.AbsenceNotProven,
            [target, .. opposite.Attempts],
            [ExpectedStrictKey(target)],
            []);
    }

    static void ValidateUnavailableTarget(
        ResearchTargetCorrespondenceOutcome.CounterpartUnavailable unavailable,
        ResearchTargetAttempt attempt,
        bool expectKeys)
    {
        Require(
            ReferenceEquals(unavailable.Attempt, attempt)
                && ReferenceEquals(
                    unavailable.Target,
                    attempt.Outcome as ResearchTargetOutcome.Resolved),
            "Unavailable target evidence must retain its exact resolved attempt.");
        Require(
            expectKeys
                ? unavailable.StrictKey?.Equals(
                        ExpectedStrictKey(attempt)) == true
                    && unavailable.CorrespondenceKey?.Equals(
                        ExpectedCorrespondenceKey(attempt)) == true
                : unavailable.StrictKey is null
                    && unavailable.CorrespondenceKey is null,
            "Unavailable target keys must exist exactly for a healthy domain.");
    }

    static void ValidateTarget(
        ResearchCorrespondingTarget target,
        ResearchTargetAttempt attempt)
    {
        Require(
            ReferenceEquals(target.Attempt, attempt)
                && ReferenceEquals(
                    target.Target,
                    attempt.Outcome as ResearchTargetOutcome.Resolved),
            "Endpoint evidence must retain its exact resolved attempt.");
        Require(
            target.StrictKey.Equals(ExpectedStrictKey(attempt))
                && target.CorrespondenceKey.Equals(
                    ExpectedCorrespondenceKey(attempt)),
            "Endpoint keys must independently re-derive from the exact target.");
    }

    static ResearchStrictTargetKey ExpectedStrictKey(
        ResearchTargetAttempt attempt)
    {
        var resolved =
            attempt.Outcome as ResearchTargetOutcome.Resolved
            ?? throw Violation("A strict key requires a resolved attempt.");
        return new(
            attempt.Request.Scope,
            attempt.Request.Domain,
            attempt.Request.Input,
            resolved.Role,
            resolved.Role == ResearchTargetRelationshipRole.None
                ? null
                : resolved.Address,
            resolved.Role == ResearchTargetRelationshipRole.None
                ? resolved.Anchor
                : null);
    }

    static ResearchTargetCorrespondenceKey ExpectedCorrespondenceKey(
        ResearchTargetAttempt attempt)
    {
        var resolved =
            attempt.Outcome as ResearchTargetOutcome.Resolved
            ?? throw Violation(
                "A correspondence key requires a resolved attempt.");
        string canonical =
            resolved.Role == ResearchTargetRelationshipRole.None
                ? resolved.Anchor.CanonicalSignature
                : ResearchMemberIdentity.CanonicalBodyIdentity(
                    resolved.Target);
        return new(
            attempt.Request.Scope,
            attempt.Request.Domain,
            resolved.Role,
            canonical);
    }

    static void ValidateKeyAbsenceProof(
        ResearchTargetKeyAbsenceProof proof,
        ResearchTargetDomainSideCensus census,
        ResearchTargetAttempt target)
    {
        Require(
            ReferenceEquals(proof.Census, census)
                && proof.Key.Equals(ExpectedCorrespondenceKey(target)),
            "A key-absence proof must bind the exact census and opposite key.");
        ValidateAbsenceEvidence(
            proof.EvidenceKind,
            proof.NotFoundAttempt,
            census);
        Require(
            KeyAbsenceIsProven(census, target),
            "A key-absence proof must positively cover its exact target.");
    }

    static void ValidateDomainAbsenceProof(
        ResearchTargetDomainAbsenceProof proof,
        ResearchTargetDomainSideCensus census)
    {
        Require(
            ReferenceEquals(proof.Census, census),
            "A domain-absence proof must bind the exact census.");
        ValidateAbsenceEvidence(
            proof.EvidenceKind,
            proof.NotFoundAttempt,
            census);
    }

    static void ValidateAbsenceEvidence(
        ResearchTargetAbsenceEvidenceKind kind,
        ResearchTargetAttempt? notFoundAttempt,
        ResearchTargetDomainSideCensus census)
    {
        if (census.Inputs.IsEmpty)
        {
            Require(
                kind == ResearchTargetAbsenceEvidenceKind.NoAdmittedInput
                    && notFoundAttempt is null,
                "An empty domain side proves absence only by no admitted input.");
            return;
        }

        ResearchTargetAttempt expected = census.Attempts.Single();
        Require(
            kind == ResearchTargetAbsenceEvidenceKind.NotFound
                && ReferenceEquals(notFoundAttempt, expected)
                && expected.Outcome is ResearchTargetOutcome.NotFound,
            "A populated domain side proves absence only through its exact NotFound attempt.");
    }

    static bool KeyAbsenceIsProven(
        ResearchTargetDomainSideCensus census,
        ResearchTargetAttempt target)
    {
        if (census.Inputs.IsEmpty)
            return true;

        ResearchTargetAttempt? attempt =
            census.Attempts.SingleOrDefault();
        if (attempt?.Outcome is not ResearchTargetOutcome.NotFound notFound)
            return false;
        if (notFound.ResearchDiagnostic?.Kind
            == ResearchTargetDiagnosticKind.DeclaringTypeAbsent)
        {
            return true;
        }

        if (notFound.MetadataDiagnostic?.Kind
            == MemberTargetDiagnosticKind.MissingMember)
        {
            return true;
        }

        if (notFound.MetadataDiagnostic?.Kind
                != MemberTargetDiagnosticKind.DigestNotFound
            || attempt.Request.Selector.DigestPrefix
                is not { Length: > 0 } digest
            || target.Outcome is not ResearchTargetOutcome.Resolved resolved)
        {
            return false;
        }

        return resolved.Anchor.Fingerprint.StartsWith(
            digest,
            StringComparison.OrdinalIgnoreCase);
    }

    static void ValidateTaint(
        ResearchTargetTaintEvidence taint,
        ResearchTargetDomain domain,
        ResearchTargetTaintKind kind,
        ImmutableArray<ResearchTargetAttempt> attempts,
        ImmutableArray<ResearchStrictTargetKey> strictKeys,
        ImmutableArray<ResearchTargetInputDisposition> incompleteInputs)
    {
        Require(
            taint.Kind == kind
                && ReferenceEquals(taint.Domain, domain)
                && SameReferences(taint.Attempts, attempts)
                && SameReferences(taint.IncompleteInputs, incompleteInputs)
                && taint.StrictKeys.SequenceEqual(strictKeys),
            "Taint evidence must retain the complete exact blocking set.");
    }

    static ImmutableArray<ResearchTargetAttempt> BlockingAttempts(
        ResearchTargetDomain domain)
        =>
        [
            .. domain.Attempts.Where(
                attempt => attempt.Outcome.Kind
                    is not (ResearchTargetOutcomeKind.Resolved
                        or ResearchTargetOutcomeKind.NotFound)),
        ];

    static ImmutableArray<ResearchTargetInputDisposition> IncompleteInputs(
        ResearchTargetDomain domain)
        =>
        [
            .. domain.Inputs.Where(
                input =>
                    input.Kind == ResearchTargetDispositionKind.NotRequested),
        ];

    static T RequireSingle<T>(
        ImmutableArray<ResearchTargetCorrespondenceOutcome> outcomes,
        string message)
        where T : ResearchTargetCorrespondenceOutcome
    {
        Require(outcomes.Length == 1 && outcomes[0] is T, message);
        return (T)outcomes[0];
    }

    static bool SameReferences<T>(
        ImmutableArray<T> actual,
        ImmutableArray<T> expected)
        where T : class
    {
        if (actual.IsDefault
            || expected.IsDefault
            || actual.Length != expected.Length)
        {
            return false;
        }

        for (int index = 0; index < actual.Length; index++)
        {
            if (!ReferenceEquals(actual[index], expected[index]))
                return false;
        }

        return true;
    }

    static void ValidateScope(
        ResearchTargetScope scope,
        ResearchMemberSelectionOccurrence selection,
        ResearchAdmittedQuestion question,
        IReadOnlyDictionary<ResearchAdmittedInput, ResearchTargetInputRole> roles,
        IReadOnlyDictionary<ResearchTargetRequestId,
            ResearchTargetValidationEvidence> evidenceByRequest,
        HashSet<ResearchTargetDomainId> domainIds,
        HashSet<ResearchTargetRequestId> requestIds,
        HashSet<ResearchTargetAttemptId> attemptIds)
    {
        IReadOnlyList<ResearchTargetResolver.DomainCandidates> expected =
            ResearchTargetResolver.GroupByDomain(question);
        Require(
            scope.Domains.Length == expected.Count,
            "Every version-erased assembly domain must be planned exactly once.");

        ResearchAdmittedInput? designated =
            (selection as ResearchExactAddressMemberSelection)?.Input;
        int accounted = 0;

        for (int index = 0; index < expected.Count; index++)
        {
            ResearchTargetResolver.DomainCandidates candidates = expected[index];
            ResearchTargetDomain domain = scope.Domains[index];

            Require(
                domainIds.Add(domain.Id),
                "Domain identities must be distinct.");
            Require(
                ReferenceEquals(domain.Id.Scope, scope.Id)
                    && ReferenceEquals(domain.Scope, scope.Id),
                "A domain must be parented by its scope.");
            Require(
                domain.Key.Equals(candidates.Key)
                    && domain.Key.Identity.Version is null,
                "A domain key must be the version-erased admitted identity.");
            Require(
                domain.ConflictingInputs.Length == candidates.Conflicting.Count
                    && domain.ConflictingInputs.SequenceEqual(
                        candidates.Conflicting,
                        (IEqualityComparer<ResearchComparisonInputId>)
                            ReferenceEqualityComparer.Instance)
                    && domain.IsAmbiguous == candidates.Conflicting.Count > 0,
                "A domain must retain the complete conflicting input-ID set.");

            // Domain-side planning is total: one closed disposition per
            // admitted input in this domain, and nothing else.
            Require(
                domain.Inputs.Length == candidates.Inputs.Count,
                "Every admitted input in a domain must carry one disposition.");
            HashSet<ResearchComparisonInputId> dispositioned = new(
                ReferenceEqualityComparer.Instance);
            for (int member = 0; member < candidates.Inputs.Count; member++)
            {
                ResearchAdmittedInput input = candidates.Inputs[member];
                ResearchTargetInputDisposition disposition =
                    domain.Inputs[member];
                Require(
                    ReferenceEquals(disposition.Input, input.Id)
                        && dispositioned.Add(disposition.Input),
                    "A disposition must name its admitted input exactly once.");
                Require(
                    disposition.Role == roles[input]
                        && disposition.Side == input.Side,
                    "A disposition must retain its input's validated role and side.");

                bool requested = designated is null
                    || ReferenceEquals(designated, input);
                Require(
                    disposition.Kind
                        == (requested
                            ? ResearchTargetDispositionKind.Requested
                            : ResearchTargetDispositionKind.NotRequested),
                    "A disposition must record the planned evaluation decision.");
                Require(
                    requested
                        ? disposition.NotRequestedReason is null
                            && disposition.Request is not null
                        : disposition.NotRequestedReason
                            == ResearchTargetNotRequestedReason
                                .ExactAddressDesignatesAnotherInput
                            && disposition.Request is null,
                    "A disposition must expose a request exactly when it is requested.");
                accounted++;
            }

            ValidateDomainAttempts(
                domain,
                selection,
                candidates,
                roles,
                evidenceByRequest,
                designated,
                requestIds,
                attemptIds);
        }

        Require(
            accounted == question.Inputs.Length,
            "Domain planning must account for every admitted input in the question.");
    }

    static void ValidateDomainAttempts(
        ResearchTargetDomain domain,
        ResearchMemberSelectionOccurrence selection,
        ResearchTargetResolver.DomainCandidates candidates,
        IReadOnlyDictionary<ResearchAdmittedInput, ResearchTargetInputRole> roles,
        IReadOnlyDictionary<ResearchTargetRequestId,
            ResearchTargetValidationEvidence> evidenceByRequest,
        ResearchAdmittedInput? designated,
        HashSet<ResearchTargetRequestId> requestIds,
        HashSet<ResearchTargetAttemptId> attemptIds)
    {
        List<ResearchAdmittedInput> requestedInputs =
        [
            .. candidates.Inputs.Where(
                input => designated is null || ReferenceEquals(designated, input)),
        ];

        Require(
            domain.Requests.Length == requestedInputs.Count
                && domain.Attempts.Length == requestedInputs.Count,
            "Every requested input must mint exactly one request and one attempt.");

        for (int index = 0; index < requestedInputs.Count; index++)
        {
            ResearchAdmittedInput input = requestedInputs[index];
            ResearchTargetRequest request = domain.Requests[index];
            ResearchTargetAttempt attempt = domain.Attempts[index];
            ResearchTargetInputDisposition disposition = domain.Inputs.Single(
                candidate => ReferenceEquals(candidate.Input, input.Id));

            Require(
                requestIds.Add(request.Id) && attemptIds.Add(attempt.Id),
                "Request and attempt identities must be distinct.");
            Require(
                ReferenceEquals(attempt.Request, request)
                    && ReferenceEquals(attempt.Id.Request, request.Id),
                "An attempt must bind to exactly its own request.");
            Require(
                ReferenceEquals(disposition.Request, request.Id),
                "A requested disposition must bind to its exact request identity.");
            Require(
                ReferenceEquals(request.Id.Domain, domain.Id)
                    && ReferenceEquals(request.Id.Input, input.Id)
                    && request.Side == input.Side
                    && ReferenceEquals(request.Question, input.Question),
                "A request must be strictly side-, input-, scope-, and domain-local.");
            Require(
                string.Equals(
                    request.DeclaringTypeFullName,
                    selection.DeclaringTypeFullName,
                    StringComparison.Ordinal)
                    && request.Selector == selection.Selector
                    && request.Kind == selection.Kind
                    && ReferenceEquals(
                        request.Surface,
                        ResearchTargetSurfaceScope.MetadataApiSurface),
                "A request must retain its selection intent and the pinned surface scope.");

            ResearchExactAddressMemberSelection? exact =
                selection as ResearchExactAddressMemberSelection;
            Require(
                exact is null
                    ? request.AssertedAddress is null
                        && request.AssertedRole is null
                    : request.AssertedAddress == exact.Address
                        && request.AssertedRole == exact.AssertedRole
                        && ReferenceEquals(designated, input),
                "Asserted address evidence belongs only to its designated exact request.");

            Require(
                evidenceByRequest.TryGetValue(
                    request.Id,
                    out ResearchTargetValidationEvidence? evidence),
                "Every request must have short-lived validation evidence.");
            ValidateOutcome(
                attempt.Outcome,
                request,
                input,
                roles[input],
                domain.IsAmbiguous,
                evidence!);
        }
    }

    static void ValidateOutcome(
        ResearchTargetOutcome outcome,
        ResearchTargetRequest request,
        ResearchAdmittedInput input,
        ResearchTargetInputRole role,
        bool domainAmbiguous,
        ResearchTargetValidationEvidence evidence)
    {
        if (outcome is null)
            throw Violation("Every request must reach a terminal outcome.");
        Require(
            ReferenceEquals(evidence.Request, request)
                && ReferenceEquals(evidence.Input, input)
                && evidence.Role == role
                && ReferenceEquals(evidence.Outcome, outcome),
            "Short-lived validation evidence must bind to its exact request, input, role, and outcome.");

        if (domainAmbiguous)
        {
            Require(
                outcome is ResearchTargetOutcome.Unavailable
                {
                    Diagnostic.Kind: ResearchTargetDiagnosticKind.DomainAmbiguous,
                },
                "An ambiguous domain must block every one of its own requests.");
            Require(
                evidence.InputEvidence is null
                    && evidence.DeclaringType is null
                    && !evidence.TargetResolutionFailed
                    && evidence.MetadataResolution is null,
                "An ambiguous domain must terminate before Metadata resolution.");
            return;
        }

        if (role == ResearchTargetInputRole.ReferenceOnly)
        {
            Require(
                outcome is ResearchTargetOutcome.Unavailable
                {
                    Diagnostic.Kind:
                        ResearchTargetDiagnosticKind.ReferenceOnlyInput,
                },
                "A reference-only request must terminate Unavailable.");
            Require(
                evidence.InputEvidence is null
                    && evidence.DeclaringType is null
                    && !evidence.TargetResolutionFailed
                    && evidence.MetadataResolution is null,
                "A reference-only request must terminate before Metadata resolution.");
            return;
        }

        if (evidence.InputEvidence is not
            ResearchTargetInputValidationEvidence inputEvidence)
        {
            throw Violation(
                "An implementation request must retain short-lived input evidence.");
        }

        if (inputEvidence.ReadFailed)
        {
            RequireNoMetadataResolution(evidence);
            RequireFailure(
                outcome,
                ResearchTargetDiagnosticKind.InputUnreadable);
            return;
        }

        if (ValidateImage(inputEvidence, input) is
            ResearchTargetDiagnosticKind imageFailure)
        {
            RequireNoMetadataResolution(evidence);
            RequireFailure(outcome, imageFailure);
            return;
        }

        ApiSurface surface = inputEvidence.Surface
            ?? throw Violation(
                "A readable validated input must retain its short-lived Metadata surface.");
        string intent = request.DeclaringTypeFullName;
        List<ApiType> declaringTypes =
        [
            .. surface.Types.Where(
                candidate => string.Equals(
                    MetadataFullName(candidate),
                    intent,
                    StringComparison.Ordinal)),
        ];
        int forwarders = surface.TypeForwarders.Count(
            forwarder => string.Equals(
                MetadataFullName(forwarder),
                intent,
                StringComparison.Ordinal));
        int failedTypeDefinitions =
            CountFailedTypeDefinitions(surface, intent);
        bool nestedUnderForwarder = surface.TypeForwarders.Any(
            forwarder => IsNestedUnder(
                intent,
                MetadataFullName(forwarder)));
        int exactDeclarations =
            declaringTypes.Count + forwarders + failedTypeDefinitions;
        if (exactDeclarations > 1
            || (declaringTypes.Count + failedTypeDefinitions != 0
                && nestedUnderForwarder))
        {
            RequireNoMetadataResolution(evidence);
            RequireFailure(
                outcome,
                ResearchTargetDiagnosticKind.DeclaringTypeAmbiguous);
            return;
        }

        if (declaringTypes.Count == 0)
        {
            if (forwarders == 1 || nestedUnderForwarder)
            {
                RequireNoMetadataResolution(evidence);
                Require(
                    outcome is ResearchTargetOutcome.Unavailable
                    {
                        Diagnostic.Kind:
                            ResearchTargetDiagnosticKind
                                .DeclaringTypeForwarded,
                    },
                    "Retained forwarding evidence must terminate Unavailable.");
                return;
            }

            if (FindPotentiallyCoveringFailure(
                    surface,
                    intent,
                    memberAbsence: false) is not null)
            {
                RequireNoMetadataResolution(evidence);
                RequireFailure(
                    outcome,
                    ResearchTargetDiagnosticKind.IncompleteMetadataSurface);
                return;
            }

            Require(
                outcome is ResearchTargetOutcome.NotFound
                {
                    MetadataDiagnostic: null,
                    ResearchDiagnostic.Kind:
                        ResearchTargetDiagnosticKind.DeclaringTypeAbsent,
                    Candidates.IsEmpty: true,
                },
                "A complete surface with no type or forwarder must retain declaring-type absence.");

            Require(
                evidence.DeclaringType is null
                    && evidence.MetadataResolution is null
                    && !evidence.TargetResolutionFailed,
                "A missing declaring type must terminate before Metadata member resolution.");
            return;
        }

        Require(
            ReferenceEquals(evidence.DeclaringType, declaringTypes[0]),
            "Metadata resolution must use the exact selected declaring type.");

        if (evidence.TargetResolutionFailed)
        {
            Require(
                evidence.MetadataResolution is null
                    && ReplaysAsExpectedTargetFailure(
                        declaringTypes[0],
                        request.Selector),
                "A bounded target-resolution failure must reproduce from the exact Metadata input.");
            RequireFailure(
                outcome,
                ResearchTargetDiagnosticKind.ResolutionFailed);
            return;
        }

        MemberTargetResolution metadata =
            ValidateMetadataResolution(request, evidence);
        if (metadata.Diagnostic is { } diagnostic)
        {
            ResearchTargetOutcomeKind expected =
                ResearchTargetResolver.MapDiagnosticKind(diagnostic.Kind);
            if (expected == ResearchTargetOutcomeKind.NotFound
                && FindPotentiallyCoveringFailure(
                    surface,
                    intent,
                    memberAbsence: true) is not null)
            {
                RequireFailure(
                    outcome,
                    ResearchTargetDiagnosticKind.IncompleteMetadataSurface);
                return;
            }

            switch (expected)
            {
                case ResearchTargetOutcomeKind.NotFound:
                    ValidateNotFound(
                        outcome as ResearchTargetOutcome.NotFound
                            ?? throw Violation(
                                "A missing Metadata target must terminate NotFound."),
                        request,
                        evidence);
                    return;
                case ResearchTargetOutcomeKind.Ambiguous:
                    var ambiguous =
                        outcome as ResearchTargetOutcome.Ambiguous
                        ?? throw Violation(
                            "An ambiguous Metadata target must terminate Ambiguous.");
                    ValidateMetadataDiagnostic(
                        ambiguous.Diagnostic,
                        ambiguous.Candidates,
                        request,
                        evidence);
                    return;
                case ResearchTargetOutcomeKind.Rejected:
                    var rejected =
                        outcome as ResearchTargetOutcome.Rejected
                        ?? throw Violation(
                            "A rejected Metadata selector must terminate Rejected.");
                    ValidateMetadataDiagnostic(
                        rejected.Diagnostic,
                        rejected.Candidates,
                        request,
                        evidence);
                    return;
                default:
                    throw Violation(
                        "A Metadata diagnostic mapped to an invalid terminal arm.");
            }
        }

        if (metadata.Target is not { } target)
        {
            RequireFailure(
                outcome,
                ResearchTargetDiagnosticKind.ResolutionFailed);
            return;
        }

        ResearchTargetRelationshipRole? derivedRole = DeriveRole(
            target.ApiMember.Member,
            target.Body?.MetadataToken);
        if (derivedRole is null)
        {
            RequireFailure(
                outcome,
                ResearchTargetDiagnosticKind
                    .RelationshipRoleEvidenceMismatch);
            return;
        }

        MetadataMethodAddress? derivedAddress = null;
        if (derivedRole != ResearchTargetRelationshipRole.None)
        {
            if (!TryCreateAddress(
                    inputEvidence,
                    target.Body!.MetadataToken!.Value,
                    out derivedAddress))
            {
                RequireFailure(
                    outcome,
                    ResearchTargetDiagnosticKind.InvalidMethodDefinitionToken);
                return;
            }
        }

        if (request.Kind == ResearchTargetRequestKind.ExactAddress)
        {
            if (derivedAddress != request.AssertedAddress)
            {
                RequireFailure(
                    outcome,
                    ResearchTargetDiagnosticKind.AddressEvidenceMismatch);
                return;
            }

            if (derivedRole != request.AssertedRole)
            {
                RequireFailure(
                    outcome,
                    ResearchTargetDiagnosticKind
                        .RelationshipRoleEvidenceMismatch);
                return;
            }
        }

        ValidateResolved(
            outcome as ResearchTargetOutcome.Resolved
                ?? throw Violation(
                    "A validated Metadata target must terminate Resolved."),
            request,
            input,
            evidence);
    }

    static void ValidateResolved(
        ResearchTargetOutcome.Resolved resolved,
        ResearchTargetRequest request,
        ResearchAdmittedInput input,
        ResearchTargetValidationEvidence evidence)
    {
        MemberTargetResolution metadata =
            ValidateMetadataResolution(request, evidence);
        Require(
            metadata.Diagnostic is null
                && ReferenceEquals(metadata.Target, resolved.Target),
            "A resolved outcome must retain the exact Metadata target.");
        ValidateCandidateRetention(
            resolved.Candidates,
            metadata.Candidates);

        Require(
            ReferenceEquals(resolved.Anchor, resolved.Target.Anchor),
            "A resolved outcome retains the exact anchor of its target.");

        LibraryBodyModuleIdentity expectedModule =
            ((ImplementationComparisonInputOccurrence)input.Occurrence)
                .BodyIndex.ModuleIdentity;
        Require(
            ReferenceEquals(resolved.Module, expectedModule)
                && resolved.Module.AssemblyIdentity is not null,
            "A resolved target must retain the exact Analysis-issued module identity for its input.");

        ResearchTargetRelationshipRole? expectedRole =
            DeriveRole(
                resolved.Target.ApiMember.Member,
                resolved.Target.Body?.MetadataToken);
        Require(
            expectedRole is { } derived && resolved.Role == derived,
            "A resolved relationship role must derive from the selected member's MethodDef tokens.");
        Require(
            (resolved.Address is null)
                == (expectedRole == ResearchTargetRelationshipRole.None),
            "A durable address exists exactly when the derived relationship is physical.");

        if (resolved.Address is { } address)
        {
            Require(
                address.ModuleVersionId == resolved.Module.ModuleVersionId,
                "A durable address must belong to the resolved target's module.");
            Require(
                address.Token == resolved.Target.Body?.MetadataToken,
                "A durable address must retain the resolved body's MethodDef token.");
            Require(
                !address.Handle.IsNil
                    && MetadataTokens.GetRowNumber(address.Handle) >= 1,
                "A durable address must name a real MethodDef row.");
        }

        Require(
            Enum.IsDefined(resolved.Role),
            "A resolved outcome must carry a declared relationship role.");

        if (request.Kind == ResearchTargetRequestKind.ExactAddress)
        {
            Require(
                resolved.Address == request.AssertedAddress
                    && resolved.Role == request.AssertedRole,
                "A resolved exact request must equal its asserted address and role.");
        }
    }

    static void ValidateNotFound(
        ResearchTargetOutcome.NotFound notFound,
        ResearchTargetRequest request,
        ResearchTargetValidationEvidence evidence)
    {
        Require(
            notFound.MetadataDiagnostic is null
                != notFound.ResearchDiagnostic is null,
            "NotFound retains exactly one Metadata or Research diagnostic.");

        if (notFound.MetadataDiagnostic is { } metadataDiagnostic)
        {
            Require(
                metadataDiagnostic.Kind
                    is MemberTargetDiagnosticKind.MissingMember
                    or MemberTargetDiagnosticKind.DigestNotFound,
                "NotFound retains only a missing-member or digest-not-found diagnostic.");
            ValidateMetadataDiagnostic(
                metadataDiagnostic,
                notFound.Candidates,
                request,
                evidence);
            return;
        }

        Require(
            notFound.ResearchDiagnostic?.Kind
                == ResearchTargetDiagnosticKind.DeclaringTypeAbsent
                && evidence.DeclaringType is null
                && evidence.MetadataResolution is null
                && notFound.Candidates.IsEmpty,
            "A Research NotFound outcome must be an empty declaring-type-absence result.");
    }

    static void ValidateMetadataDiagnostic(
        MemberTargetDiagnostic diagnostic,
        ImmutableArray<MemberTargetCandidate> candidates,
        ResearchTargetRequest request,
        ResearchTargetValidationEvidence evidence)
    {
        MemberTargetResolution metadata =
            ValidateMetadataResolution(request, evidence);
        Require(
            metadata.Target is null
                && ReferenceEquals(metadata.Diagnostic, diagnostic),
            "A diagnostic outcome must retain the exact Metadata diagnostic.");
        ValidateCandidateRetention(candidates, metadata.Candidates);
        Require(
            diagnostic.Candidates.All(
                diagnosticCandidate => candidates.Any(
                    candidate => ReferenceEquals(
                        candidate,
                        diagnosticCandidate))),
            "Every diagnostic candidate must be one of the exact retained Metadata candidates.");
    }

    static MemberTargetResolution ValidateMetadataResolution(
        ResearchTargetRequest request,
        ResearchTargetValidationEvidence evidence)
    {
        if (evidence.DeclaringType is not ApiType declaring
            || evidence.MetadataResolution is not MemberTargetResolution metadata)
        {
            throw Violation(
                "A completed Metadata resolution requires its short-lived declaring-type evidence.");
        }

        MemberTargetResolution repeated = MemberTargetResolver.Resolve(
            declaring,
            request.Selector,
            kindFilter: null);

        Require(
            EquivalentTarget(metadata.Target, repeated.Target)
                && EquivalentDiagnostic(
                    metadata.Diagnostic,
                    repeated.Diagnostic)
                && metadata.Candidates.SequenceEqual(repeated.Candidates),
            "Metadata resolution must be repeatable from the exact request intent and declaring type.");
        return metadata;
    }

    static bool EquivalentTarget(
        ResolvedMemberTarget? left,
        ResolvedMemberTarget? right)
        => left is null
            ? right is null
            : right is not null && left == right;

    static bool EquivalentDiagnostic(
        MemberTargetDiagnostic? left,
        MemberTargetDiagnostic? right)
        => left is null
            ? right is null
            : right is not null
                && left.Kind == right.Kind
                && string.Equals(
                    left.Message,
                    right.Message,
                    StringComparison.Ordinal)
                && left.Candidates.SequenceEqual(right.Candidates);

    static void ValidateCandidateRetention(
        ImmutableArray<MemberTargetCandidate> actual,
        IReadOnlyList<MemberTargetCandidate> expected)
    {
        Require(
            !actual.IsDefault && actual.Length == expected.Count,
            "A Metadata outcome must retain the complete candidate sequence.");
        for (int index = 0; index < actual.Length; index++)
        {
            Require(
                ReferenceEquals(actual[index], expected[index]),
                "A Metadata outcome must retain each exact candidate reference in order.");
        }
    }

    static ResearchTargetRelationshipRole? DeriveRole(
        ApiMember member,
        int? bodyToken)
    {
        if (bodyToken is not { } token)
        {
            return member.MetadataToken is null
                && member.GetterToken is null
                && member.SetterToken is null
                && member.AdderToken is null
                && member.RemoverToken is null
                    ? ResearchTargetRelationshipRole.None
                    : null;
        }

        ResearchTargetRelationshipRole? role = null;
        int matches = 0;
        Match(member.MetadataToken, ResearchTargetRelationshipRole.Method);
        Match(member.GetterToken, ResearchTargetRelationshipRole.Getter);
        Match(member.SetterToken, ResearchTargetRelationshipRole.Setter);
        Match(member.AdderToken, ResearchTargetRelationshipRole.Adder);
        Match(member.RemoverToken, ResearchTargetRelationshipRole.Remover);
        return matches == 1 ? role : null;

        void Match(
            int? candidate,
            ResearchTargetRelationshipRole candidateRole)
        {
            if (candidate != token)
                return;

            matches++;
            role = candidateRole;
        }
    }

    static ResearchTargetDiagnosticKind? ValidateImage(
        ResearchTargetInputValidationEvidence evidence,
        ResearchAdmittedInput input)
    {
        var occurrence =
            (ImplementationComparisonInputOccurrence)input.Occurrence;
        LibraryBodyModuleIdentity analysis =
            occurrence.BodyIndex.ModuleIdentity;
        if (!evidence.IsAssembly)
            return ResearchTargetDiagnosticKind.StandaloneModule;
        if (analysis.AssemblyIdentity is null
            || evidence.LiveAssemblyIdentity is not { } live
            || !AssemblyReferenceIdentity.EquivalentComparer.Equals(
                live,
                occurrence.Assembly.Identity)
            || !AssemblyReferenceIdentity.EquivalentComparer.Equals(
                live,
                analysis.AssemblyIdentity))
        {
            return ResearchTargetDiagnosticKind.AssemblyIdentityMismatch;
        }

        return evidence.LiveModuleVersionId == analysis.ModuleVersionId
                && (evidence.ArtifactModuleVersionId is not Guid artifact
                    || artifact == evidence.LiveModuleVersionId)
            ? null
            : ResearchTargetDiagnosticKind.ModuleIdentityMismatch;
    }

    static bool TryCreateAddress(
        ResearchTargetInputValidationEvidence evidence,
        int token,
        out MetadataMethodAddress? address)
    {
        address = null;
        EntityHandle entity;
        try
        {
            entity = MetadataTokens.EntityHandle(token);
        }
        catch (ArgumentException)
        {
            return false;
        }

        if (entity.IsNil || entity.Kind != HandleKind.MethodDefinition)
            return false;

        int row = MetadataTokens.GetRowNumber(entity);
        if (row < 1 || row > evidence.MethodDefinitionCount)
            return false;

        address = new MetadataMethodAddress(
            evidence.LiveModuleVersionId,
            (MethodDefinitionHandle)entity);
        return true;
    }

    static bool ReplaysAsExpectedTargetFailure(
        ApiType declaringType,
        MemberTargetSelector selector)
    {
        try
        {
            MemberTargetResolver.Resolve(
                declaringType,
                selector,
                kindFilter: null);
            return false;
        }
        catch (Exception exception) when (
            exception is BadImageFormatException
                or FormatException
                or OverflowException)
        {
            return true;
        }
    }

    static ApiSurfaceInspectionFailure? FindPotentiallyCoveringFailure(
        ApiSurface surface,
        string declaringTypeFullName,
        bool memberAbsence)
        => surface.InspectionFailures.FirstOrDefault(
            failure =>
                failure.Operation
                    != ApiSurfaceInspectionFailure
                        .GenericParameterConstraintResolutionOperation
                && failure.Operation
                    != ApiSurfaceInspectionFailure
                        .EnumAttributeTypeIndexOperation
                && ((memberAbsence
                        && failure.OwningTypeDefinition is not null)
                    || MayAffectType(failure, declaringTypeFullName)));

    static int CountFailedTypeDefinitions(
        ApiSurface surface,
        string declaringTypeFullName)
        => surface.InspectionFailures.Count(
            failure =>
                failure.OwningTypeDefinition is { } owner
                && string.Equals(
                    owner.ToMetadataFullName(),
                    declaringTypeFullName,
                    StringComparison.Ordinal));

    static bool MayAffectType(
        ApiSurfaceInspectionFailure failure,
        string declaringTypeFullName)
    {
        if (failure.OwningTypeDefinition is { } owner)
        {
            return string.Equals(
                owner.ToMetadataFullName(),
                declaringTypeFullName,
                StringComparison.Ordinal);
        }

        if (!failure.AffectedTypeDefinitions.IsDefaultOrEmpty)
        {
            return failure.AffectedTypeDefinitions.Any(
                affected => string.Equals(
                    affected.ToMetadataFullName(),
                    declaringTypeFullName,
                    StringComparison.Ordinal));
        }

        return true;
    }

    static string MetadataFullName(ApiType type)
        => type.DefinitionName?.ToMetadataFullName() ?? type.FullName;

    static string MetadataFullName(TypeForwarder forwarder)
        => forwarder.DefinitionName?.ToMetadataFullName() ?? forwarder.TypeName;

    static bool IsNestedUnder(string candidate, string potentialRoot)
        => candidate.Length > potentialRoot.Length
            && candidate.StartsWith(potentialRoot, StringComparison.Ordinal)
            && candidate[potentialRoot.Length] == '.';

    static void RequireFailure(
        ResearchTargetOutcome outcome,
        ResearchTargetDiagnosticKind kind)
        => Require(
            outcome is ResearchTargetOutcome.Failed
            {
                Diagnostic.Kind: var actual,
            } && actual == kind,
            $"The independently derived Research failure must be {kind}.");

    static void RequireNoMetadataResolution(
        ResearchTargetValidationEvidence evidence)
        => Require(
            evidence.DeclaringType is null
                && evidence.MetadataResolution is null
                && !evidence.TargetResolutionFailed,
            "An input-level terminal outcome must precede Metadata resolution.");

    /// <summary>
    /// The single terminal arm each bounded Research diagnostic may occupy.
    /// </summary>
    internal static ResearchTargetOutcomeKind ExpectedArm(
        ResearchTargetDiagnosticKind kind)
        => kind switch
        {
            ResearchTargetDiagnosticKind.DeclaringTypeAbsent =>
                ResearchTargetOutcomeKind.NotFound,
            ResearchTargetDiagnosticKind.DeclaringTypeForwarded
                or ResearchTargetDiagnosticKind.ReferenceOnlyInput
                or ResearchTargetDiagnosticKind.DomainAmbiguous =>
                ResearchTargetOutcomeKind.Unavailable,
            ResearchTargetDiagnosticKind.AssemblyIdentityMismatch
                or ResearchTargetDiagnosticKind.ModuleIdentityMismatch
                or ResearchTargetDiagnosticKind.StandaloneModule
                or ResearchTargetDiagnosticKind.DeclaringTypeAmbiguous
                or ResearchTargetDiagnosticKind.IncompleteMetadataSurface
                or ResearchTargetDiagnosticKind.InvalidMethodDefinitionToken
                or ResearchTargetDiagnosticKind.AddressEvidenceMismatch
                or ResearchTargetDiagnosticKind
                    .RelationshipRoleEvidenceMismatch
                or ResearchTargetDiagnosticKind.InputUnreadable
                or ResearchTargetDiagnosticKind.ResolutionFailed =>
                ResearchTargetOutcomeKind.Failed,
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };

    static void Require(bool condition, string message)
    {
        if (!condition)
            throw Violation(message);
    }

    static InvalidOperationException Violation(string message)
        => new($"Research target resolution is inconsistent: {message}");
}
