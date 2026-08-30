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
/// handed. It re-derives the expected scope, domain, request, and attempt sets
/// from the caller's planning request and the admitted population, then rejects
/// both missing and stale entries. It then re-runs parent identity, exact-once
/// accounting, module/address/token/relationship-role binding, the
/// diagnostic-kind to outcome-arm mapping, candidate retention, and
/// request-to-result binding.
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

            Require(
                requestIds.Add(request.Id) && attemptIds.Add(attempt.Id),
                "Request and attempt identities must be distinct.");
            Require(
                ReferenceEquals(attempt.Request, request)
                    && ReferenceEquals(attempt.Id.Request, request.Id),
                "An attempt must bind to exactly its own request.");
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
                        ResearchTargetSurfaceScope.AllDeclaredMembers),
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
        Require(outcome is not null, "Every request must reach a terminal outcome.");
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
                evidence.DeclaringType is null
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
                evidence.DeclaringType is null
                    && evidence.MetadataResolution is null,
                "A reference-only request must terminate before Metadata resolution.");
            return;
        }

        switch (outcome)
        {
            case ResearchTargetOutcome.Resolved resolved:
                ValidateResolved(resolved, request, input, evidence);
                break;

            case ResearchTargetOutcome.NotFound notFound:
                ValidateNotFound(notFound, request, evidence);
                break;

            case ResearchTargetOutcome.Ambiguous ambiguous:
                Require(
                    ambiguous.Diagnostic.Kind
                        is MemberTargetDiagnosticKind.AmbiguousMember
                        or MemberTargetDiagnosticKind.DigestAmbiguous,
                    "Ambiguous retains only an ambiguity diagnostic.");
                ValidateMetadataDiagnostic(
                    ambiguous.Diagnostic,
                    ambiguous.Candidates,
                    request,
                    evidence);
                break;

            case ResearchTargetOutcome.Rejected rejected:
                Require(
                    rejected.Diagnostic.Kind
                        is MemberTargetDiagnosticKind.ConflictingSelectors
                        or MemberTargetDiagnosticKind.OverloadOutOfRange,
                    "Rejected retains only an invalid-selector diagnostic.");
                ValidateMetadataDiagnostic(
                    rejected.Diagnostic,
                    rejected.Candidates,
                    request,
                    evidence);
                break;

            case ResearchTargetOutcome.Unavailable unavailable:
                Require(
                    unavailable.Diagnostic.Kind
                        == ResearchTargetDiagnosticKind.DeclaringTypeForwarded,
                    "An implementation request is unavailable only when its declaring type is forwarded.");
                Require(
                    evidence.DeclaringType is null
                        && evidence.MetadataResolution is null,
                    "A forwarded declaring type must terminate before Metadata member resolution.");
                break;

            case ResearchTargetOutcome.Failed failed:
                Require(
                    ExpectedArm(failed.Diagnostic.Kind)
                        == ResearchTargetOutcomeKind.Failed,
                    "A Failed outcome must carry a failure diagnostic.");
                if (evidence.MetadataResolution is { })
                    ValidateMetadataResolution(request, evidence);
                break;

            default:
                throw Violation("Unknown terminal target outcome arm.");
        }
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

        if (token == member.MetadataToken)
            return ResearchTargetRelationshipRole.Method;
        if (token == member.GetterToken)
            return ResearchTargetRelationshipRole.Getter;
        if (token == member.SetterToken)
            return ResearchTargetRelationshipRole.Setter;
        if (token == member.AdderToken)
            return ResearchTargetRelationshipRole.Adder;
        if (token == member.RemoverToken)
            return ResearchTargetRelationshipRole.Remover;
        return null;
    }

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
