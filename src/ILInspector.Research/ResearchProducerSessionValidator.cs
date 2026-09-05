using System.Collections.Immutable;

using ILInspector.Decompiler;
using ILInspector.Findings;
using ILInspector.Instructions;

namespace ILInspector.Research;

internal static class ResearchProducerSessionValidator
{
    internal static ResearchProducerRejection? ValidateRequest(
        ResearchProducerSessionRequest request,
        out ImmutableArray<ResearchProducerKind> selected)
    {
        selected = [];
        if (request.Population.Profile
            != ResearchComparisonProfile.ImplementationComparison)
        {
            return Reject(
                ResearchProducerRejectionKind.UnsupportedProfile,
                "The local producer session requires an implementation-comparison population.");
        }

        if (!ReferenceEquals(
                request.Population.Operation,
                request.Resolution.Operation))
        {
            return Reject(
                ResearchProducerRejectionKind.ForeignResolution,
                "The population and target resolution name different operations.");
        }

        if (!HasValidIdentityClosure(request.Population, request.Resolution))
        {
            return Reject(
                ResearchProducerRejectionKind.InvalidIdentityClosure,
                "The population or target resolution has an invalid identity closure.");
        }

        if (request.Producers.IsEmpty)
        {
            return Reject(
                ResearchProducerRejectionKind.EmptyProducerSelection,
                "At least one local producer must be selected.");
        }

        var requested = new HashSet<ResearchProducerKind>();
        foreach (ResearchProducerKind producer in request.Producers)
        {
            if (!ResearchProducerCatalog.Kinds.Contains(producer))
            {
                return Reject(
                    ResearchProducerRejectionKind.UnknownProducerKind,
                    "The request names a producer outside the local catalog.");
            }
            if (!requested.Add(producer))
            {
                return Reject(
                    ResearchProducerRejectionKind.DuplicateProducerKind,
                    "A local producer kind was selected more than once.");
            }
        }

        selected =
        [
            .. ResearchProducerCatalog.Kinds.Where(requested.Contains),
        ];
        return null;
    }

    internal static bool TryCreateCompletion(
        ResearchProducerSessionRequest request,
        ResearchProducerSessionId session,
        ImmutableArray<ResearchProducerWorkItem> workItems,
        ImmutableArray<ResearchProducerWorkResult> results,
        ImmutableArray<ResearchComparisonInputId> acquisitionOrder,
        ImmutableArray<ResearchProducerCleanupOutcome> cleanup,
        out ResearchProducerCompletion? completion)
    {
        completion = null;
        if (ValidateRequest(request, out ImmutableArray<ResearchProducerKind>
                producers) is not null
            || !ReferenceEquals(session.Operation, request.Population.Operation)
            || !session.BelongsTo(request.Identity)
            || workItems.Length
                != request.WorkBases.Length * producers.Length
            || results.Length != workItems.Length
            || cleanup.Length != acquisitionOrder.Length)
        {
            return false;
        }

        var identities = new HashSet<ResearchProducerWorkItemId>(
            ReferenceEqualityComparer.Instance);
        int index = 0;
        foreach (ResearchProducerWorkBasis basis in request.WorkBases)
        {
            foreach (ResearchProducerKind producer in producers)
            {
                ResearchProducerWorkItem item = workItems[index];
                ResearchProducerWorkResult result = results[index];
                if (!ReferenceEquals(item.Id.Session, session)
                    || !identities.Add(item.Id)
                    || !ReferenceEquals(
                        item.Basis,
                        basis)
                    || item.Producer != producer
                    || !ReferenceEquals(result.Item, item)
                    || !ValidateOutcome(
                        request.Resolution,
                        item,
                        result.Outcome))
                {
                    return false;
                }
                index++;
            }
        }

        for (int cleanupIndex = 0;
            cleanupIndex < cleanup.Length;
            cleanupIndex++)
        {
            if (cleanup[cleanupIndex]
                    is not ResearchProducerCleanupOutcome.Succeeded succeeded
                || !ReferenceEquals(
                    succeeded.Input,
                    acquisitionOrder[^(cleanupIndex + 1)]))
            {
                return false;
            }
        }

        completion = new ResearchProducerCompletion(
            request.Population.Operation,
            session,
            workItems,
            results,
            cleanup);
        return true;
    }

    internal static bool HasValidIdentityClosure(
        ResearchAdmittedPopulation population,
        ResearchTargetResolution resolution)
        => ValidatePopulation(population) && ValidateResolution(population, resolution);

    static bool ValidatePopulation(ResearchAdmittedPopulation population)
    {
        var questionIds = new HashSet<ResearchComparisonQuestionId>(
            ReferenceEqualityComparer.Instance);
        var inputIds = new HashSet<ResearchComparisonInputId>(
            ReferenceEqualityComparer.Instance);
        var expectedInputs = new List<ResearchAdmittedInput>();

        foreach (ResearchAdmittedQuestion question in population.Questions)
        {
            if (!questionIds.Add(question.Id)
                || !ReferenceEquals(question.Operation, population.Operation)
                || !SameReferences(
                    question.Inputs,
                    question.Before.Concat(question.After)))
            {
                return false;
            }

            foreach (ResearchAdmittedInput input in question.Before)
            {
                if (!ValidateInput(
                        population,
                        question,
                        input,
                        ResearchComparisonSide.Before,
                        inputIds))
                {
                    return false;
                }
                expectedInputs.Add(input);
            }
            foreach (ResearchAdmittedInput input in question.After)
            {
                if (!ValidateInput(
                        population,
                        question,
                        input,
                        ResearchComparisonSide.After,
                        inputIds))
                {
                    return false;
                }
                expectedInputs.Add(input);
            }
        }

        return SameReferences(population.Inputs, expectedInputs);
    }

    static bool ValidateInput(
        ResearchAdmittedPopulation population,
        ResearchAdmittedQuestion question,
        ResearchAdmittedInput input,
        ResearchComparisonSide side,
        HashSet<ResearchComparisonInputId> inputIds)
        => inputIds.Add(input.Id)
            && ReferenceEquals(input.Operation, population.Operation)
            && ReferenceEquals(input.Question, question.Id)
            && input.Side == side
            && population.TryGetInput(
                input.Id,
                out ResearchAdmittedInput? byId)
            && ReferenceEquals(byId, input)
            && population.TryGetInput(
                input.Occurrence,
                out ResearchAdmittedInput? byOccurrence)
            && ReferenceEquals(byOccurrence, input);

    static bool ValidateResolution(
        ResearchAdmittedPopulation population,
        ResearchTargetResolution resolution)
    {
        var questions = new HashSet<ResearchComparisonQuestionId>(
            population.Questions.Select(static question => question.Id),
            ReferenceEqualityComparer.Instance);
        var inputs = new HashSet<ResearchComparisonInputId>(
            population.Inputs.Select(static input => input.Id),
            ReferenceEqualityComparer.Instance);
        var scopeIds = new HashSet<ResearchTargetScopeId>(
            ReferenceEqualityComparer.Instance);
        var domainIds = new HashSet<ResearchTargetDomainId>(
            ReferenceEqualityComparer.Instance);
        var requestIds = new HashSet<ResearchTargetRequestId>(
            ReferenceEqualityComparer.Instance);
        var attemptIds = new HashSet<ResearchTargetAttemptId>(
            ReferenceEqualityComparer.Instance);
        var domains = new List<ResearchTargetDomain>();
        var requests = new List<ResearchTargetRequest>();
        var attempts = new List<ResearchTargetAttempt>();

        foreach (ResearchTargetScope scope in resolution.Scopes)
        {
            if (!scopeIds.Add(scope.Id)
                || !ReferenceEquals(
                    scope.Id.Operation,
                    population.Operation)
                || !questions.Contains(scope.Question))
            {
                return false;
            }

            foreach (ResearchTargetDomain domain in scope.Domains)
            {
                if (!domainIds.Add(domain.Id)
                    || !ReferenceEquals(domain.Scope, scope.Id))
                {
                    return false;
                }
                domains.Add(domain);

                foreach (ResearchTargetInputDisposition disposition in
                    domain.Inputs)
                {
                    if (!inputs.Contains(disposition.Input)
                        || !ReferenceEquals(
                            disposition.Input.Question,
                            scope.Question))
                    {
                        return false;
                    }
                }

                foreach (ResearchTargetRequest request in domain.Requests)
                {
                    if (!requestIds.Add(request.Id)
                        || !ReferenceEquals(request.Operation, population.Operation)
                        || !ReferenceEquals(request.Question, scope.Question)
                        || !ReferenceEquals(request.Scope, scope.Id)
                        || !ReferenceEquals(request.Domain, domain.Id)
                        || !inputs.Contains(request.Input)
                        || !ReferenceEquals(
                            request.Input.Question,
                            scope.Question))
                    {
                        return false;
                    }
                    requests.Add(request);
                }

                foreach (ResearchTargetAttempt attempt in domain.Attempts)
                {
                    if (!attemptIds.Add(attempt.Id)
                        || !ReferenceEquals(
                            attempt.Id.Request,
                            attempt.Request.Id)
                        || !domain.Requests.Any(
                            request => ReferenceEquals(
                                request,
                                attempt.Request)))
                    {
                        return false;
                    }
                    attempts.Add(attempt);
                }
            }
        }

        if (!SameReferences(resolution.Domains, domains)
            || !SameReferences(resolution.Requests, requests)
            || !SameReferences(resolution.Attempts, attempts)
            || requests.Count != attempts.Count)
        {
            return false;
        }

        foreach (ResearchTargetRequest request in requests)
        {
            if (!resolution.TryGetAttempt(
                    request.Id,
                    out ResearchTargetAttempt? attempt)
                || !ReferenceEquals(attempt.Request, request))
            {
                return false;
            }
        }

        ResearchTargetCorrespondenceProjection expected =
            ResearchTargetCorrespondenceBuilder.Build(resolution.Scopes);
        return SameCensuses(expected.Censuses, resolution.Censuses)
            && SameCorrespondences(
                expected.Outcomes,
                resolution.Correspondences)
            && resolution.Correspondences.All(
                outcome => ReferencesResolution(resolution, outcome));
    }

    static bool SameCensuses(
        ImmutableArray<ResearchTargetDomainSideCensus> expected,
        ImmutableArray<ResearchTargetDomainSideCensus> actual)
    {
        if (expected.Length != actual.Length)
            return false;

        for (int index = 0; index < expected.Length; index++)
        {
            ResearchTargetDomainSideCensus left = expected[index];
            ResearchTargetDomainSideCensus right = actual[index];
            if (!ReferenceEquals(left.Domain, right.Domain)
                || left.Side != right.Side
                || left.Health != right.Health
                || !SameReferences(left.Inputs, right.Inputs)
                || !SameReferences(left.Attempts, right.Attempts))
            {
                return false;
            }
        }
        return true;
    }

    static bool SameCorrespondences(
        ImmutableArray<ResearchTargetCorrespondenceOutcome> expected,
        ImmutableArray<ResearchTargetCorrespondenceOutcome> actual)
    {
        if (expected.Length != actual.Length)
            return false;

        for (int index = 0; index < expected.Length; index++)
        {
            ResearchTargetCorrespondenceOutcome left = expected[index];
            ResearchTargetCorrespondenceOutcome right = actual[index];
            if (left.Kind != right.Kind
                || !ReferenceEquals(left.Domain, right.Domain)
                || !SameCorrespondence(left, right))
            {
                return false;
            }
        }
        return true;
    }

    static bool SameCorrespondence(
        ResearchTargetCorrespondenceOutcome expected,
        ResearchTargetCorrespondenceOutcome actual)
        => (expected, actual) switch
        {
            (ResearchTargetCorrespondenceOutcome.Paired left,
                ResearchTargetCorrespondenceOutcome.Paired right) =>
                SameTarget(left.Before, right.Before)
                && SameTarget(left.After, right.After),
            (ResearchTargetCorrespondenceOutcome.BeforeOnly left,
                ResearchTargetCorrespondenceOutcome.BeforeOnly right) =>
                SameTarget(left.Before, right.Before)
                && SameKeyAbsence(
                    left.AfterAbsence,
                    right.AfterAbsence),
            (ResearchTargetCorrespondenceOutcome.AfterOnly left,
                ResearchTargetCorrespondenceOutcome.AfterOnly right) =>
                SameKeyAbsence(
                    left.BeforeAbsence,
                    right.BeforeAbsence)
                && SameTarget(left.After, right.After),
            (ResearchTargetCorrespondenceOutcome.Absent left,
                ResearchTargetCorrespondenceOutcome.Absent right) =>
                SameDomainAbsence(
                    left.BeforeAbsence,
                    right.BeforeAbsence)
                && SameDomainAbsence(
                    left.AfterAbsence,
                    right.AfterAbsence),
            (ResearchTargetCorrespondenceOutcome.CounterpartUnavailable left,
                ResearchTargetCorrespondenceOutcome.CounterpartUnavailable right) =>
                ReferenceEquals(left.Attempt, right.Attempt)
                && Equal(left.StrictKey, right.StrictKey)
                && Equal(
                    left.CorrespondenceKey,
                    right.CorrespondenceKey)
                && SameTaint(left.Taint, right.Taint),
            (ResearchTargetCorrespondenceOutcome.DomainUnavailable left,
                ResearchTargetCorrespondenceOutcome.DomainUnavailable right) =>
                SameTaint(left.Taint, right.Taint),
            _ => false,
        };

    static bool SameTarget(
        ResearchCorrespondingTarget expected,
        ResearchCorrespondingTarget actual)
        => ReferenceEquals(expected.Attempt, actual.Attempt)
            && expected.StrictKey.Equals(actual.StrictKey)
            && expected.CorrespondenceKey.Equals(actual.CorrespondenceKey);

    static bool SameKeyAbsence(
        ResearchTargetKeyAbsenceProof expected,
        ResearchTargetKeyAbsenceProof actual)
        => expected.Side == actual.Side
            && expected.EvidenceKind == actual.EvidenceKind
            && expected.Key.Equals(actual.Key)
            && ReferenceEquals(
                expected.NotFoundAttempt,
                actual.NotFoundAttempt);

    static bool SameDomainAbsence(
        ResearchTargetDomainAbsenceProof expected,
        ResearchTargetDomainAbsenceProof actual)
        => expected.Side == actual.Side
            && expected.EvidenceKind == actual.EvidenceKind
            && ReferenceEquals(
                expected.NotFoundAttempt,
                actual.NotFoundAttempt);

    static bool SameTaint(
        ResearchTargetTaintEvidence expected,
        ResearchTargetTaintEvidence actual)
        => expected.Kind == actual.Kind
            && ReferenceEquals(expected.Domain, actual.Domain)
            && SameReferences(expected.Attempts, actual.Attempts)
            && SameValues(expected.StrictKeys, actual.StrictKeys)
            && SameReferences(
                expected.IncompleteInputs,
                actual.IncompleteInputs);

    static bool ReferencesResolution(
        ResearchTargetResolution resolution,
        ResearchTargetCorrespondenceOutcome outcome)
        => outcome switch
        {
            ResearchTargetCorrespondenceOutcome.Paired paired =>
                HasAttempt(resolution, paired.Before.Attempt)
                && HasAttempt(resolution, paired.After.Attempt),
            ResearchTargetCorrespondenceOutcome.BeforeOnly beforeOnly =>
                HasAttempt(resolution, beforeOnly.Before.Attempt)
                && HasCensus(
                    resolution,
                    beforeOnly.AfterAbsence.Census)
                && HasOptionalAttempt(
                    resolution,
                    beforeOnly.AfterAbsence.NotFoundAttempt),
            ResearchTargetCorrespondenceOutcome.AfterOnly afterOnly =>
                HasCensus(
                    resolution,
                    afterOnly.BeforeAbsence.Census)
                && HasOptionalAttempt(
                    resolution,
                    afterOnly.BeforeAbsence.NotFoundAttempt)
                && HasAttempt(resolution, afterOnly.After.Attempt),
            ResearchTargetCorrespondenceOutcome.Absent absent =>
                HasCensus(
                    resolution,
                    absent.BeforeAbsence.Census)
                && HasCensus(
                    resolution,
                    absent.AfterAbsence.Census)
                && HasOptionalAttempt(
                    resolution,
                    absent.BeforeAbsence.NotFoundAttempt)
                && HasOptionalAttempt(
                    resolution,
                    absent.AfterAbsence.NotFoundAttempt),
            ResearchTargetCorrespondenceOutcome.CounterpartUnavailable
                unavailable =>
                HasAttempt(resolution, unavailable.Attempt)
                && TaintReferencesResolution(
                    resolution,
                    unavailable.Taint),
            ResearchTargetCorrespondenceOutcome.DomainUnavailable
                unavailable =>
                TaintReferencesResolution(
                    resolution,
                    unavailable.Taint),
            _ => false,
        };

    static bool HasAttempt(
        ResearchTargetResolution resolution,
        ResearchTargetAttempt attempt)
        => resolution.Attempts.Any(
            candidate => ReferenceEquals(candidate, attempt));

    static bool HasOptionalAttempt(
        ResearchTargetResolution resolution,
        ResearchTargetAttempt? attempt)
        => attempt is null || HasAttempt(resolution, attempt);

    static bool HasCensus(
        ResearchTargetResolution resolution,
        ResearchTargetDomainSideCensus census)
        => resolution.Censuses.Any(
            candidate => ReferenceEquals(candidate, census));

    static bool TaintReferencesResolution(
        ResearchTargetResolution resolution,
        ResearchTargetTaintEvidence taint)
        => resolution.Domains.Any(
                domain => ReferenceEquals(domain, taint.Domain))
            && taint.Attempts.All(
                attempt => HasAttempt(resolution, attempt));

    static bool Equal<T>(T? left, T? right)
        where T : class, IEquatable<T>
        => left is null ? right is null : left.Equals(right);

    static bool SameValues<T>(
        IEnumerable<T> left,
        IEnumerable<T> right)
        where T : IEquatable<T>
        => left.SequenceEqual(right);

    static bool ValidateOutcome(
        ResearchTargetResolution resolution,
        ResearchProducerWorkItem item,
        ResearchProducerWorkOutcome outcome)
    {
        ResearchTargetCorrespondenceOutcome? correspondence =
            (item.Basis as ResearchProducerWorkBasis.Correspondence)?.Outcome;
        bool correspondenceUnavailable = correspondence
            is ResearchTargetCorrespondenceOutcome.CounterpartUnavailable
                or ResearchTargetCorrespondenceOutcome.DomainUnavailable;
        return outcome switch
        {
            ResearchProducerWorkOutcome.ProducedCSharp produced =>
                item.Producer == ResearchProducerKind.CSharp
                && !correspondenceUnavailable
                && NativeMatches(
                    resolution,
                    item.Basis,
                    produced.Result),
            ResearchProducerWorkOutcome.ProducedIlBody produced =>
                item.Producer == ResearchProducerKind.IlBody
                && !correspondenceUnavailable
                && NativeMatches(
                    resolution,
                    item.Basis,
                    produced.Result),
            ResearchProducerWorkOutcome.Unavailable unavailable =>
                ValidateUnavailable(
                    item.Basis,
                    correspondenceUnavailable,
                    unavailable.Reason),
            ResearchProducerWorkOutcome.Failed failed =>
                !correspondenceUnavailable
                && failed.Diagnostic.Kind
                    == ResearchProducerDiagnosticKind.ProducerException
                && failed.Diagnostic.Producer == item.Producer,
            _ => false,
        };
    }

    static bool ValidateUnavailable(
        ResearchProducerWorkBasis basis,
        bool correspondenceUnavailable,
        ResearchProducerUnavailable unavailable)
    {
        if (correspondenceUnavailable)
        {
            return unavailable.Kind
                    == ResearchProducerUnavailableKind
                        .CorrespondenceUnavailable
                && unavailable.Input is null;
        }
        if (unavailable.Kind
            == ResearchProducerUnavailableKind.CorrespondenceUnavailable)
        {
            return false;
        }

        ImmutableArray<ResearchComparisonInputId> endpointInputs =
            basis switch
            {
                ResearchProducerWorkBasis.DesignatedPair designated =>
                    [
                        designated.Pair.Before.Request.Input,
                        designated.Pair.After.Request.Input,
                    ],
                ResearchProducerWorkBasis.Correspondence
                {
                    Outcome: ResearchTargetCorrespondenceOutcome.Paired paired,
                } =>
                    [
                        paired.Before.Attempt.Request.Input,
                        paired.After.Attempt.Request.Input,
                    ],
                ResearchProducerWorkBasis.Correspondence
                {
                    Outcome: ResearchTargetCorrespondenceOutcome.BeforeOnly beforeOnly,
                } =>
                    [beforeOnly.Before.Attempt.Request.Input],
                ResearchProducerWorkBasis.Correspondence
                {
                    Outcome: ResearchTargetCorrespondenceOutcome.AfterOnly afterOnly,
                } =>
                    [afterOnly.After.Attempt.Request.Input],
                _ => [],
            };
        return unavailable.Input is { } input
            && endpointInputs.Any(
                candidate => ReferenceEquals(candidate, input));
    }

    static bool NativeMatches(
        ResearchTargetResolution resolution,
        ResearchProducerWorkBasis basis,
        CSharpMemberEndpointComparison result)
    {
        (string before, string after) = SubjectIdentities(resolution, basis);
        return string.Equals(result.Old.Key, before, StringComparison.Ordinal)
            && string.Equals(result.New.Key, after, StringComparison.Ordinal)
            && AbsenceMatches(
                basis,
                result.Findings.OldInspection,
                result.Findings.NewInspection);
    }

    static bool NativeMatches(
        ResearchTargetResolution resolution,
        ResearchProducerWorkBasis basis,
        IlMemberEndpointComparison result)
    {
        (string before, string after) = SubjectIdentities(resolution, basis);
        return string.Equals(
                result.Old.Identity,
                before,
                StringComparison.Ordinal)
            && string.Equals(
                result.New.Identity,
                after,
                StringComparison.Ordinal)
            && AbsenceMatches(
                basis,
                result.Findings.OldInspection,
                result.Findings.NewInspection);
    }

    static bool AbsenceMatches<T>(
        ResearchProducerWorkBasis basis,
        FindingInspection<T> oldInspection,
        FindingInspection<T> newInspection)
        where T : notnull
    {
        if (basis is ResearchProducerWorkBasis.DesignatedPair)
            return !IsSubjectAbsent(oldInspection) && !IsSubjectAbsent(newInspection);

        var correspondence =
            ((ResearchProducerWorkBasis.Correspondence)basis).Outcome;
        (bool oldAbsent, bool newAbsent) = correspondence switch
        {
            ResearchTargetCorrespondenceOutcome.Paired => (false, false),
            ResearchTargetCorrespondenceOutcome.BeforeOnly => (false, true),
            ResearchTargetCorrespondenceOutcome.AfterOnly => (true, false),
            ResearchTargetCorrespondenceOutcome.Absent => (true, true),
            _ => throw new InvalidOperationException(
                "Unavailable correspondence cannot carry a native result."),
        };
        return IsSubjectAbsent(oldInspection) == oldAbsent
            && IsSubjectAbsent(newInspection) == newAbsent;
    }

    static bool IsSubjectAbsent<T>(FindingInspection<T> inspection)
        where T : notnull
        => inspection is FindingInspection<T>.Absent
        {
            Kind: FindingInspectionAbsenceKind.SubjectAbsent,
        };

    static (string Before, string After) SubjectIdentities(
        ResearchTargetResolution resolution,
        ResearchProducerWorkBasis basis)
    {
        if (basis is ResearchProducerWorkBasis.DesignatedPair designated)
        {
            return (
                ((ResearchTargetOutcome.Resolved)designated.Pair.Before.Outcome)
                    .Anchor.CanonicalSignature,
                ((ResearchTargetOutcome.Resolved)designated.Pair.After.Outcome)
                    .Anchor.CanonicalSignature);
        }

        string subject = SubjectIdentity(
            resolution,
            ((ResearchProducerWorkBasis.Correspondence)basis).Outcome);
        return (subject, subject);
    }

    static string SubjectIdentity(
        ResearchTargetResolution resolution,
        ResearchTargetCorrespondenceOutcome correspondence)
        => correspondence switch
        {
            ResearchTargetCorrespondenceOutcome.Paired paired =>
                paired.Before.CorrespondenceKey.CanonicalIdentity,
            ResearchTargetCorrespondenceOutcome.BeforeOnly beforeOnly =>
                beforeOnly.Before.CorrespondenceKey.CanonicalIdentity,
            ResearchTargetCorrespondenceOutcome.AfterOnly afterOnly =>
                afterOnly.After.CorrespondenceKey.CanonicalIdentity,
            ResearchTargetCorrespondenceOutcome.Absent =>
                AbsentSubject(resolution, correspondence.Scope),
            _ => throw new InvalidOperationException(
                "Unavailable correspondence has no producer subject."),
        };

    static string AbsentSubject(
        ResearchTargetResolution resolution,
        ResearchTargetScopeId scopeId)
    {
        ResearchTargetScope? scope = resolution.Scopes.FirstOrDefault(
            candidate => ReferenceEquals(candidate.Id, scopeId));
        return scope is null
            ? ""
            : $"{scope.DeclaringTypeFullName}::{scope.Selector.NormalizedSelector}";
    }

    static bool SameReferences<T>(
        IEnumerable<T> left,
        IEnumerable<T> right)
        where T : class
    {
        using IEnumerator<T> leftEnumerator = left.GetEnumerator();
        using IEnumerator<T> rightEnumerator = right.GetEnumerator();
        while (true)
        {
            bool leftNext = leftEnumerator.MoveNext();
            bool rightNext = rightEnumerator.MoveNext();
            if (leftNext != rightNext)
                return false;
            if (!leftNext)
                return true;
            if (!ReferenceEquals(
                    leftEnumerator.Current,
                    rightEnumerator.Current))
            {
                return false;
            }
        }
    }

    static ResearchProducerRejection Reject(
        ResearchProducerRejectionKind kind,
        string summary)
        => new(kind, summary);
}
