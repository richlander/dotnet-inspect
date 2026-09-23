using System.Xml;

using CSharpText;
using DotnetInspector.Libraries;

namespace DotnetInspector.DocumentationHouse;

/// <summary>
/// Settles exact Library-scoped documentation into detached typed evidence.
/// </summary>
public static class DocumentationHouse
{
    public static async ValueTask<DocumentationHouseOutcome> ExecuteAsync(
        DocumentationHouseRequest request,
        LibraryOperationLease operationLease,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(operationLease);

        bool houseOwnsLease = true;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            DocumentationAuthoredSourceChannelPlan? authoredPlan =
                RequestsAuthoredSource(request.Demand)
                    ? request.Plan.AuthoredSource
                    : null;
            IDocumentationAuthoredSourceOperation? authoredOperation =
                authoredPlan?.Operation;
            ProvisionalOutcome? validation =
                ValidateRequest(request, operationLease);
            if (validation is not null)
            {
                return CompleteTopLevel(
                    request,
                    validation,
                    HouseSettlement());
            }

            DocumentationCompiledXmlAttempt? compiledXmlAttempt = null;
            DocumentationHouseWorkCharge work = EmptyWork();
            if (RequestsCompiledXml(request.Demand))
            {
                ProvisionalOutcome compiled = ExecuteCompiledCore(
                    request,
                    operationLease,
                    cancellationToken);
                if (compiled is not CompletedOutcome completed)
                {
                    return CompleteTopLevel(
                        request,
                        compiled,
                        HouseSettlement());
                }

                compiledXmlAttempt = completed.Attempt;
                work = completed.Work;
            }

            if (!RequestsAuthoredSource(request.Demand))
            {
                return Complete(
                    request,
                    compiledXmlAttempt,
                    authoredSourceAttempt: null,
                    work,
                    HouseSettlement());
            }

            if (authoredPlan is null || authoredOperation is null)
            {
                return Complete(
                    request,
                    compiledXmlAttempt,
                    new DocumentationAuthoredSourceAttempt.Unavailable(
                        DocumentationAuthoredSourceUnavailableKind
                            .OperationUnavailable,
                        outcome: null),
                    work,
                    HouseSettlement());
            }

            var invocation =
                new DocumentationAuthoredSourceOperationInvocation(
                    authoredPlan.Binding,
                    authoredPlan.Limits,
                    request.Plan.Deadline);
            houseOwnsLease = false;
            DocumentationAuthoredSourceOperationOutcome authoredOutcome =
                await authoredOperation.InvokeAsync(
                        invocation,
                        operationLease,
                        cancellationToken)
                    .ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();

            DocumentationAuthoredSourceAttempt authoredAttempt =
                MapAuthoredAttempt(invocation, authoredOutcome);
            work = new(
                work.ContributionsObserved,
                work.CompiledXmlBytesObserved,
                work.ParsedCompiledXml,
                authoredOutcome.Work);
            return Complete(
                request,
                compiledXmlAttempt,
                authoredAttempt,
                work,
                AuthoredSettlement(authoredOutcome.LeaseSettlement));
        }
        finally
        {
            if (houseOwnsLease)
                operationLease.Dispose();
        }
    }

    /// <summary>
    /// Settles several exact subjects while scanning each selected compiled
    /// XML companion once.
    /// </summary>
    public static ValueTask<IReadOnlyList<DocumentationHouseOutcome>>
        ExecuteManyAsync(
            IReadOnlyList<DocumentationHouseRequest> requests,
            LibraryOperationLease operationLease,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(requests);
        ArgumentNullException.ThrowIfNull(operationLease);
        if (requests.Count == 0)
            throw new ArgumentException(
                "At least one documentation request is required.",
                nameof(requests));
        if (requests.Any(static request => request is null))
        {
            throw new ArgumentException(
                "Documentation requests cannot contain null.",
                nameof(requests));
        }
        if (requests.Any(static request =>
                request.Demand != DocumentationDemand.CompiledXml))
        {
            throw new ArgumentException(
                "Batch documentation settlement supports compiled XML demand only.",
                nameof(requests));
        }

        ProvisionalOutcome[] provisional =
            new ProvisionalOutcome[requests.Count];
        var batch = new CompiledXmlBatch(
            [
                .. requests.Select(
                    request => new CompiledXmlBatchRequest(
                        request,
                        operationLease)),
            ]);
        try
        {
            for (int index = 0; index < requests.Count; index++)
            {
                DocumentationHouseRequest request = requests[index];
                provisional[index] =
                    ValidateRequest(request, operationLease)
                    ?? ExecuteCompiledCore(
                        request,
                        operationLease,
                        cancellationToken,
                        batch);
            }
        }
        finally
        {
            operationLease.Dispose();
        }

        var outcomes =
            new DocumentationHouseOutcome[requests.Count];
        for (int index = 0; index < requests.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            outcomes[index] = CompleteTopLevel(
                requests[index],
                provisional[index],
                new DocumentationLibraryLeaseSettlement(
                    DocumentationLibraryLeaseConsumer
                        .DocumentationHouse));
        }
        return ValueTask.FromResult<
            IReadOnlyList<DocumentationHouseOutcome>>(outcomes);
    }

    /// <summary>
    /// Settles compiled or combined requests with one Library lease per
    /// subject while scanning each selected compiled XML companion once.
    /// </summary>
    public static async ValueTask<
        IReadOnlyList<DocumentationHouseOutcome>>
        ExecuteManyAsync(
            IReadOnlyList<DocumentationHouseRequest> requests,
            IReadOnlyList<LibraryOperationLease> operationLeases,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(requests);
        ArgumentNullException.ThrowIfNull(operationLeases);
        if (requests.Count == 0)
            throw new ArgumentException(
                "At least one documentation request is required.",
                nameof(requests));
        if (requests.Count != operationLeases.Count)
        {
            throw new ArgumentException(
                "Each documentation request requires one Library operation lease.",
                nameof(operationLeases));
        }
        if (requests.Any(static request => request is null))
        {
            throw new ArgumentException(
                "Documentation requests cannot contain null.",
                nameof(requests));
        }
        if (operationLeases.Any(static lease => lease is null))
        {
            throw new ArgumentException(
                "Documentation operation leases cannot contain null.",
                nameof(operationLeases));
        }
        if (operationLeases.Distinct(
                ReferenceEqualityComparer.Instance).Count()
            != operationLeases.Count)
        {
            throw new ArgumentException(
                "Batch documentation operation leases must be unique.",
                nameof(operationLeases));
        }
        if (requests.Any(static request =>
                !RequestsCompiledXml(request.Demand)))
        {
            throw new ArgumentException(
                "Batch documentation settlement requires compiled XML demand.",
                nameof(requests));
        }

        var houseOwnsLease =
            Enumerable.Repeat(true, requests.Count).ToArray();
        try
        {
            ProvisionalOutcome[] provisional =
                new ProvisionalOutcome[requests.Count];
            for (int index = 0; index < requests.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                DocumentationHouseRequest request = requests[index];
                LibraryOperationLease operationLease =
                    operationLeases[index];
                provisional[index] =
                    ValidateRequest(request, operationLease)!;
            }
            var batch = new CompiledXmlBatch(
                [
                    .. requests
                        .Select(
                            (request, index) => (
                                Request: request,
                                Operation: operationLeases[index],
                                Validation: provisional[index]))
                        .Where(static item => item.Validation is null)
                        .Select(
                            static item =>
                                new CompiledXmlBatchRequest(
                                    item.Request,
                                    item.Operation)),
                ]);
            for (int index = 0; index < requests.Count; index++)
            {
                if (provisional[index] is null)
                {
                    provisional[index] = ExecuteCompiledCore(
                        requests[index],
                        operationLeases[index],
                        cancellationToken,
                        batch);
                }
            }

            var outcomes =
                new DocumentationHouseOutcome[requests.Count];
            for (int index = 0; index < requests.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                DocumentationHouseRequest request = requests[index];
                LibraryOperationLease operationLease =
                    operationLeases[index];
                if (provisional[index]
                    is not CompletedOutcome completed)
                {
                    outcomes[index] = CompleteTopLevel(
                        request,
                        provisional[index],
                        HouseSettlement());
                    continue;
                }

                if (!RequestsAuthoredSource(request.Demand))
                {
                    outcomes[index] = Complete(
                        request,
                        completed.Attempt,
                        authoredSourceAttempt: null,
                        completed.Work,
                        HouseSettlement());
                    continue;
                }

                DocumentationAuthoredSourceChannelPlan? authoredPlan =
                    request.Plan.AuthoredSource;
                IDocumentationAuthoredSourceOperation? authoredOperation =
                    authoredPlan?.Operation;
                if (authoredPlan is null || authoredOperation is null)
                {
                    outcomes[index] = Complete(
                        request,
                        completed.Attempt,
                        new DocumentationAuthoredSourceAttempt.Unavailable(
                            DocumentationAuthoredSourceUnavailableKind
                                .OperationUnavailable,
                            outcome: null),
                        completed.Work,
                        HouseSettlement());
                    continue;
                }

                var invocation =
                    new DocumentationAuthoredSourceOperationInvocation(
                        authoredPlan.Binding,
                        authoredPlan.Limits,
                        request.Plan.Deadline);
                houseOwnsLease[index] = false;
                DocumentationAuthoredSourceOperationOutcome authoredOutcome =
                    await authoredOperation.InvokeAsync(
                            invocation,
                            operationLease,
                            cancellationToken)
                        .ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                DocumentationAuthoredSourceAttempt authoredAttempt =
                    MapAuthoredAttempt(invocation, authoredOutcome);
                DocumentationHouseWorkCharge work = new(
                    completed.Work.ContributionsObserved,
                    completed.Work.CompiledXmlBytesObserved,
                    completed.Work.ParsedCompiledXml,
                    authoredOutcome.Work);
                outcomes[index] = Complete(
                    request,
                    completed.Attempt,
                    authoredAttempt,
                    work,
                    AuthoredSettlement(
                        authoredOutcome.LeaseSettlement));
            }

            return outcomes;
        }
        finally
        {
            for (int index = 0; index < operationLeases.Count; index++)
            {
                if (houseOwnsLease[index])
                    operationLeases[index].Dispose();
            }
        }
    }

    private static ProvisionalOutcome? ValidateRequest(
        DocumentationHouseRequest request,
        LibraryOperationLease operationLease)
    {
        DocumentationSubjectReference subject = request.Subject;
        DocumentationHouseOperationPlan plan = request.Plan;
        DocumentationHouseWorkCharge emptyWork = EmptyWork();
        if (!ReferenceEquals(subject.ApiContent.Library, subject.Library))
        {
            return new RejectedOutcome(
                new(
                    DocumentationHouseRejectionKind
                        .LibraryReferenceMismatch),
                emptyWork);
        }
        if (!ReferenceEquals(
                subject.ApiContent,
                subject.Library.ApiAssembly)
            || !subject.ApiContent.HasRole(
                LibraryContentRole.ApiAssembly))
        {
            return new RejectedOutcome(
                new(
                    DocumentationHouseRejectionKind.ApiContentMismatch),
                emptyWork);
        }
        if (!ReferenceEquals(
                operationLease.Reference,
                subject.Library))
        {
            return new RejectedOutcome(
                new(
                    DocumentationHouseRejectionKind
                        .LeaseReferenceMismatch),
                emptyWork);
        }
        if (RequestsAuthoredSource(request.Demand)
            && plan.AuthoredSource is { } authored
            && !MatchesAuthoredBinding(request, authored.Binding))
        {
            return new RejectedOutcome(
                new(
                    DocumentationHouseRejectionKind
                        .AuthoredSourceBindingMismatch),
                emptyWork);
        }
        if (DateTimeOffset.UtcNow >= plan.Deadline)
        {
            return new IncompleteOutcome(
                DocumentationIncompleteBoundary.Deadline,
                emptyWork);
        }

        return null;
    }

    private static bool MatchesAuthoredBinding(
        DocumentationHouseRequest request,
        DocumentationAuthoredSourceOperationBinding binding) =>
        ReferenceEquals(binding.Request, request.Identity)
        && ReferenceEquals(binding.OperationPlan, request.Plan.Identity)
        && ReferenceEquals(
            binding.PolicyGeneration,
            request.Plan.PolicyGeneration)
        && ReferenceEquals(binding.Subject, request.Subject)
        && ReferenceEquals(binding.Library, request.Subject.Library)
        && ReferenceEquals(
            binding.ImplementationContent,
            request.Subject.Library.ImplementationAssembly);

    private static ProvisionalOutcome ExecuteCompiledCore(
        DocumentationHouseRequest request,
        LibraryOperationLease operationLease,
        CancellationToken cancellationToken,
        CompiledXmlBatch? batch = null)
    {
        DocumentationSubjectReference subject = request.Subject;
        DocumentationHouseOperationPlan plan = request.Plan;

        IReadOnlyList<CompiledXmlContribution> contributions =
            plan.CompiledXmlContributions;
        int contributionCount =
            plan.SuppliedCompiledXmlContributionCount;
        var selectionWork = new DocumentationHouseWorkCharge(
            contributionCount,
            CompiledXmlBytesObserved: 0,
            ParsedCompiledXml: false);
        if (plan.ExceedsCompiledXmlContributionLimit)
        {
            return new CompletedOutcome(
                new DocumentationCompiledXmlAttempt.Incomplete(
                    DocumentationIncompleteBoundary.ContributionLimit,
                    selected: null,
                    contributions),
                selectionWork);
        }

        IReadOnlyList<DocumentationCompiledXmlRejection> rejections =
            ValidateContributions(request, contributions);
        cancellationToken.ThrowIfCancellationRequested();
        if (DateTimeOffset.UtcNow >= plan.Deadline)
        {
            return new IncompleteOutcome(
                DocumentationIncompleteBoundary.Deadline,
                selectionWork);
        }
        if (rejections.Count > 0)
        {
            return new CompletedOutcome(
                new DocumentationCompiledXmlAttempt.Rejected(
                    rejections,
                    contributions),
                selectionWork);
        }

        bool selectionIsPartial = false;
        bool selectionHasAbsence = false;
        var candidateBuilder =
            new List<CompiledXmlContribution>();
        foreach (CompiledXmlContribution contribution in contributions)
        {
            switch (contribution.Kind)
            {
                case CompiledXmlContributionKind.Candidate:
                    candidateBuilder.Add(contribution);
                    break;
                case CompiledXmlContributionKind.Absent:
                    selectionHasAbsence = true;
                    break;
                case CompiledXmlContributionKind.Partial:
                    selectionIsPartial = true;
                    break;
                case CompiledXmlContributionKind.Unavailable:
                    break;
                default:
                    throw new InvalidOperationException(
                        "Unknown compiled XML contribution kind.");
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (DateTimeOffset.UtcNow >= plan.Deadline)
        {
            return new IncompleteOutcome(
                DocumentationIncompleteBoundary.Deadline,
                selectionWork);
        }
        if (selectionIsPartial)
        {
            return new CompletedOutcome(
                new DocumentationCompiledXmlAttempt.Incomplete(
                    DocumentationIncompleteBoundary
                        .CompanionSelectionPartial,
                    selected: null,
                    contributions),
                selectionWork);
        }

        CompiledXmlContribution[] candidates =
            [.. candidateBuilder];
        CompiledXmlContribution? selected =
            candidates.Length == 0
                ? null
                : SelectCandidate(candidates);
        cancellationToken.ThrowIfCancellationRequested();
        if (DateTimeOffset.UtcNow >= plan.Deadline)
        {
            return new IncompleteOutcome(
                DocumentationIncompleteBoundary.Deadline,
                selectionWork);
        }
        if (candidates.Length == 0)
        {
            DocumentationCompiledXmlAttempt attempt =
                selectionHasAbsence
                    ? new DocumentationCompiledXmlAttempt.Absent(
                        selected: null,
                        contributions)
                    : new DocumentationCompiledXmlAttempt.Unavailable(
                        contributions);
            return new CompletedOutcome(attempt, selectionWork);
        }

        if (selected is null)
        {
            return new CompletedOutcome(
                new DocumentationCompiledXmlAttempt.Ambiguous(
                    Array.AsReadOnly(candidates),
                    contributions),
                selectionWork);
        }

        cancellationToken.ThrowIfCancellationRequested();
        CompiledXmlBatchRead batchRead =
            batch?.Read(
                selected,
                operationLease,
                plan.Limits,
                cancellationToken)
            ?? ReadCompiledXml(
                selected,
                operationLease,
                plan.Limits,
                [subject.CompiledXmlIdentity],
                plan.Deadline,
                cancellationToken);
        CompiledXmlRead read = batchRead.Read;

        var readWork = new DocumentationHouseWorkCharge(
            contributionCount,
            batchRead.PerformedSnapshot ? read.Length : 0,
            ParsedCompiledXml: batchRead.PerformedParse);
        cancellationToken.ThrowIfCancellationRequested();
        if (DateTimeOffset.UtcNow >= plan.Deadline)
        {
            return new CompletedOutcome(
                new DocumentationCompiledXmlAttempt.Incomplete(
                    DocumentationIncompleteBoundary.Deadline,
                    selected,
                    contributions),
                readWork);
        }
        if (read.Status == CompiledXmlReadStatus.Deadline)
        {
            return new CompletedOutcome(
                new DocumentationCompiledXmlAttempt.Incomplete(
                    DocumentationIncompleteBoundary.Deadline,
                    selected,
                    contributions),
                readWork);
        }
        if (read.Status == CompiledXmlReadStatus.ContentAccessFailed)
        {
            return new FailedOutcome(
                new(
                    DocumentationHouseFailureStage
                        .CompiledXmlSnapshot,
                    DocumentationCompiledXmlFailureKind
                        .ContentAccessFailed,
                    selected),
                selectionWork);
        }
        if (read.Status == CompiledXmlReadStatus.ByteLimit)
        {
            return new CompletedOutcome(
                new DocumentationCompiledXmlAttempt.Incomplete(
                    DocumentationIncompleteBoundary
                        .CompiledXmlByteLimit,
                    selected,
                    contributions),
                readWork);
        }
        if (read.Status == CompiledXmlReadStatus.Malformed)
        {
            return new CompletedOutcome(
                new DocumentationCompiledXmlAttempt.Failed(
                    selected,
                    new(
                        DocumentationCompiledXmlFailureKind
                            .MalformedOrUnreadableDocument),
                    contributions),
                readWork);
        }

        if (read.RetainedTextLimitExceeded.Contains(
                subject.CompiledXmlIdentity.Value))
        {
            return new CompletedOutcome(
                new DocumentationCompiledXmlAttempt.Failed(
                    selected,
                    new(
                        DocumentationCompiledXmlFailureKind
                            .MalformedOrUnreadableDocument),
                    contributions),
                readWork);
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (DateTimeOffset.UtcNow >= plan.Deadline)
        {
            return new CompletedOutcome(
                new DocumentationCompiledXmlAttempt.Incomplete(
                    DocumentationIncompleteBoundary.Deadline,
                    selected,
                    contributions),
                readWork);
        }

        read.Entries.TryGetValue(
            subject.CompiledXmlIdentity.Value,
            out XmlDocumentationEntry? documentation);
        DocumentationCompiledXmlAttempt completed =
            documentation is null
                ? new DocumentationCompiledXmlAttempt.Absent(
                    selected,
                    contributions)
                : new DocumentationCompiledXmlAttempt.Available(
                    selected,
                    documentation,
                    contributions);
        return new CompletedOutcome(completed, readWork);
    }

    private static DocumentationAuthoredSourceAttempt MapAuthoredAttempt(
        DocumentationAuthoredSourceOperationInvocation invocation,
        DocumentationAuthoredSourceOperationOutcome outcome)
    {
        if (!ReferenceEquals(outcome.Invocation, invocation)
            || !ReferenceEquals(
                outcome.Receipt.Invocation,
                invocation)
            || !ReferenceEquals(
                outcome.Receipt.LeaseSettlement,
                outcome.LeaseSettlement)
            || !ReferenceEquals(outcome.Receipt.Work, outcome.Work)
            || !ReferenceEquals(outcome.Receipt.Evidence, outcome.Evidence)
            || outcome
                is DocumentationAuthoredSourceOperationOutcome.Produced
                    producedOutcome
                && !ReferenceEquals(
                    producedOutcome.Contribution.Binding,
                    invocation.Binding))
        {
            return new DocumentationAuthoredSourceAttempt.Rejected(
                DocumentationAuthoredSourceAttemptRejectionKind
                    .OperationEvidenceMismatch,
                outcome);
        }

        return outcome switch
        {
            DocumentationAuthoredSourceOperationOutcome.Produced produced
                when produced.Contribution.Documentation
                    is CSharpAuthoredDocumentationOutcome.Available =>
                new DocumentationAuthoredSourceAttempt.Available(
                    produced),
            DocumentationAuthoredSourceOperationOutcome.Produced produced
                when produced.Contribution.Documentation
                    is CSharpAuthoredDocumentationOutcome.Absent =>
                new DocumentationAuthoredSourceAttempt.Absent(
                    produced),
            DocumentationAuthoredSourceOperationOutcome.Unavailable
                {
                    UnavailableKind:
                        DocumentationAuthoredUnavailableKind
                            .DeclarationAmbiguous,
                } unavailable =>
                new DocumentationAuthoredSourceAttempt.Ambiguous(
                    DocumentationAuthoredSourceAmbiguityKind
                        .DeclarationAmbiguous,
                    unavailable),
            DocumentationAuthoredSourceOperationOutcome.Unavailable
                {
                    UnavailableKind:
                        DocumentationAuthoredUnavailableKind
                            .DeclarationUncertain,
                } unavailable =>
                new DocumentationAuthoredSourceAttempt.Incomplete(
                    unavailable),
            DocumentationAuthoredSourceOperationOutcome.Unavailable
                unavailable =>
                new DocumentationAuthoredSourceAttempt.Unavailable(
                    MapUnavailable(unavailable.UnavailableKind),
                    unavailable),
            DocumentationAuthoredSourceOperationOutcome.Rejected rejected =>
                new DocumentationAuthoredSourceAttempt.Rejected(
                    DocumentationAuthoredSourceAttemptRejectionKind
                        .OperationRejected,
                    rejected),
            DocumentationAuthoredSourceOperationOutcome.Failed failed =>
                new DocumentationAuthoredSourceAttempt.Failed(failed),
            DocumentationAuthoredSourceOperationOutcome.Incomplete
                incomplete =>
                new DocumentationAuthoredSourceAttempt.Incomplete(
                    incomplete),
            _ => throw new InvalidOperationException(
                "Unknown authored-source operation outcome."),
        };
    }

    private static DocumentationAuthoredSourceUnavailableKind MapUnavailable(
        DocumentationAuthoredUnavailableKind unavailable) =>
        unavailable switch
        {
            DocumentationAuthoredUnavailableKind.SourceUnavailable =>
                DocumentationAuthoredSourceUnavailableKind
                    .SourceUnavailable,
            DocumentationAuthoredUnavailableKind.DeclarationNotFound =>
                DocumentationAuthoredSourceUnavailableKind
                    .DeclarationNotFound,
            _ => throw new InvalidOperationException(
                "The authored unavailable result belongs to another channel attempt kind."),
        };

    private static DocumentationHouseOutcome Complete(
        DocumentationHouseRequest request,
        DocumentationCompiledXmlAttempt? compiledXmlAttempt,
        DocumentationAuthoredSourceAttempt? authoredSourceAttempt,
        DocumentationHouseWorkCharge work,
        DocumentationLibraryLeaseSettlement settlement) =>
        new DocumentationHouseOutcome.Completed(
            request,
            compiledXmlAttempt,
            authoredSourceAttempt,
            SettleFields(
                request.Demand,
                compiledXmlAttempt,
                authoredSourceAttempt),
            work,
            settlement);

    private static DocumentationHouseOutcome CompleteTopLevel(
        DocumentationHouseRequest request,
        ProvisionalOutcome provisional,
        DocumentationLibraryLeaseSettlement settlement) =>
        provisional switch
        {
            CompletedOutcome completed => Complete(
                request,
                completed.Attempt,
                authoredSourceAttempt: null,
                completed.Work,
                settlement),
            RejectedOutcome rejected =>
                new DocumentationHouseOutcome.Rejected(
                    request,
                    rejected.Rejection,
                    rejected.Work,
                    settlement),
            FailedOutcome failed =>
                new DocumentationHouseOutcome.Failed(
                    request,
                    failed.Failure,
                    failed.Work,
                    settlement),
            IncompleteOutcome incomplete =>
                new DocumentationHouseOutcome.Incomplete(
                    request,
                    incomplete.Boundary,
                    incomplete.Work,
                    settlement),
            _ => throw new InvalidOperationException(
                "Unknown DocumentationHouse provisional outcome."),
        };

    private static DocumentationFieldSettlement SettleFields(
        DocumentationDemand demand,
        DocumentationCompiledXmlAttempt? compiledXmlAttempt,
        DocumentationAuthoredSourceAttempt? authoredSourceAttempt)
    {
        IReadOnlyList<DocumentationChannel> requestedChannels =
            demand switch
            {
                DocumentationDemand.CompiledXml =>
                    [DocumentationChannel.CompiledXml],
                DocumentationDemand.AuthoredSourceDocumentation =>
                    [DocumentationChannel.AuthoredSource],
                DocumentationDemand
                    .CompiledXmlAndAuthoredSourceDocumentation =>
                    [
                        DocumentationChannel.CompiledXml,
                        DocumentationChannel.AuthoredSource,
                    ],
                _ => throw new InvalidOperationException(
                    "Unknown documentation demand."),
            };
        var documents =
            new List<ChannelDocumentation>(requestedChannels.Count);
        if (compiledXmlAttempt
            is DocumentationCompiledXmlAttempt.Available compiled)
        {
            documents.Add(
                new(
                    DocumentationChannel.CompiledXml,
                    compiled.Documentation));
        }
        if (authoredSourceAttempt
            is DocumentationAuthoredSourceAttempt.Available authored)
        {
            var available =
                (CSharpAuthoredDocumentationOutcome.Available)
                    authored.Contribution.Documentation;
            documents.Add(
                new(
                    DocumentationChannel.AuthoredSource,
                    available.Documentation));
        }

        string[] parameterNames =
        [
            .. documents
                .SelectMany(static document =>
                    document.Documentation.Parameters.Keys)
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal),
        ];
        var parameters =
            new Dictionary<
                string,
                DocumentationFieldEvidence<string>>(
                    parameterNames.Length,
                    StringComparer.Ordinal);
        foreach (string name in parameterNames)
        {
            parameters.Add(
                name,
                SettleField(
                    requestedChannels,
                    documents
                        .Select(document =>
                            (
                                document.Channel,
                                Value: document.Documentation.Parameters
                                    .GetValueOrDefault(name)))
                        .Where(static candidate =>
                            !string.IsNullOrEmpty(candidate.Value))
                        .Select(static candidate =>
                            new DocumentationFieldContribution<string>(
                                candidate.Channel,
                                candidate.Value!))
                        .ToArray(),
                    static (left, right) =>
                        string.Equals(
                            left,
                            right,
                            StringComparison.Ordinal)));
        }

        return new(
            Scalar(
                static document => document.Documentation.Summary),
            Scalar(
                static document => document.Documentation.Remarks),
            Scalar(
                static document => document.Documentation.Returns),
            parameters,
            SettleField(
                requestedChannels,
                documents
                    .Where(static document =>
                        document.Documentation.Exceptions.Count > 0)
                    .Select(static document =>
                        new DocumentationFieldContribution<
                            IReadOnlyList<XmlDocumentationException>>(
                                document.Channel,
                                Array.AsReadOnly(
                                    document.Documentation.Exceptions
                                        .ToArray())))
                    .ToArray(),
                static (left, right) => left.SequenceEqual(right)),
            SettleField(
                requestedChannels,
                documents
                    .Where(static document =>
                        document.Documentation.Samples.Count > 0)
                    .Select(static document =>
                        new DocumentationFieldContribution<
                            IReadOnlyList<XmlDocumentationSampleReference>>(
                                document.Channel,
                                Array.AsReadOnly(
                                    document.Documentation.Samples
                                        .ToArray())))
                    .ToArray(),
                static (left, right) => left.SequenceEqual(right)));

        DocumentationFieldEvidence<string> Scalar(
            Func<ChannelDocumentation, string?> select) =>
            SettleField(
                requestedChannels,
                documents
                    .Select(document =>
                        (
                            document.Channel,
                            Value: select(document)))
                    .Where(static candidate =>
                        !string.IsNullOrEmpty(candidate.Value))
                    .Select(static candidate =>
                        new DocumentationFieldContribution<string>(
                            candidate.Channel,
                            candidate.Value!))
                    .ToArray(),
                static (left, right) =>
                    string.Equals(
                        left,
                        right,
                        StringComparison.Ordinal));
    }

    private static DocumentationFieldEvidence<T> SettleField<T>(
        IReadOnlyList<DocumentationChannel> requestedChannels,
        IReadOnlyList<DocumentationFieldContribution<T>> contributions,
        Func<T, T, bool> equals)
        where T : notnull
    {
        DocumentationFieldEvidenceKind kind =
            contributions.Count switch
            {
                0 => DocumentationFieldEvidenceKind.Absent,
                1 => DocumentationFieldEvidenceKind.Selected,
                _ when contributions
                    .Skip(1)
                    .All(contribution =>
                        equals(
                            contributions[0].Value,
                            contribution.Value)) =>
                    DocumentationFieldEvidenceKind.Corroborated,
                _ => DocumentationFieldEvidenceKind.Conflict,
            };
        return new(kind, requestedChannels, contributions);
    }

    private static DocumentationLibraryLeaseSettlement HouseSettlement() =>
        new(DocumentationLibraryLeaseConsumer.DocumentationHouse);

    private static DocumentationLibraryLeaseSettlement AuthoredSettlement(
        DocumentationAuthoredLeaseSettlement settlement) =>
        new(
            settlement.Consumer switch
            {
                DocumentationAuthoredLeaseConsumer.Operation =>
                    DocumentationLibraryLeaseConsumer.Operation,
                DocumentationAuthoredLeaseConsumer.SourceHouse =>
                    DocumentationLibraryLeaseConsumer.SourceHouse,
                _ => throw new InvalidOperationException(
                    "Unknown authored-source lease consumer."),
            },
            settlement);

    private static bool RequestsCompiledXml(DocumentationDemand demand) =>
        demand is DocumentationDemand.CompiledXml
            or DocumentationDemand
                .CompiledXmlAndAuthoredSourceDocumentation;

    private static bool RequestsAuthoredSource(DocumentationDemand demand) =>
        demand is DocumentationDemand.AuthoredSourceDocumentation
            or DocumentationDemand
                .CompiledXmlAndAuthoredSourceDocumentation;

    private static DocumentationHouseWorkCharge EmptyWork() =>
        new(
            ContributionsObserved: 0,
            CompiledXmlBytesObserved: 0,
            ParsedCompiledXml: false);

    private sealed record ChannelDocumentation(
        DocumentationChannel Channel,
        XmlDocumentationEntry Documentation);

    private static CompiledXmlBatchRead ReadCompiledXml(
        CompiledXmlContribution selected,
        LibraryOperationLease operationLease,
        DocumentationHouseLimits limits,
        IReadOnlyCollection<XmlDocMemberIdentity> identities,
        DateTimeOffset parseDeadline,
        CancellationToken cancellationToken)
    {
        if (DateTimeOffset.UtcNow >= parseDeadline)
        {
            return new(
                new(
                    CompiledXmlReadStatus.Deadline,
                    Length: 0,
                    EmptyEntries,
                    EmptyIdentities),
                PerformedSnapshot: false,
                PerformedParse: false);
        }

        XmlSnapshot snapshot;
        try
        {
            snapshot = operationLease.Snapshot(
                selected.CompiledXmlContent!,
                limits.MaximumCompiledXmlBytes,
                static (view, maximumBytes, token) =>
                {
                    token.ThrowIfCancellationRequested();
                    int length = view.Content.Length;
                    return length > maximumBytes
                        ? new XmlSnapshot(Bytes: null, length)
                        : new XmlSnapshot(
                            view.Content.ToArray(),
                            length);
                },
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception failure) when (
            failure is ObjectDisposedException
                or UnauthorizedAccessException
                or InvalidOperationException
                or ArgumentException)
        {
            return new(
                new(
                    CompiledXmlReadStatus.ContentAccessFailed,
                    Length: 0,
                    EmptyEntries,
                    EmptyIdentities),
                PerformedSnapshot: false,
                PerformedParse: false);
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (DateTimeOffset.UtcNow >= parseDeadline)
        {
            return new(
                new(
                    CompiledXmlReadStatus.Deadline,
                    snapshot.Length,
                    EmptyEntries,
                    EmptyIdentities),
                PerformedSnapshot: true,
                PerformedParse: false);
        }

        if (snapshot.Bytes is null)
        {
            return new(
                new(
                    CompiledXmlReadStatus.ByteLimit,
                    snapshot.Length,
                    EmptyEntries,
                    EmptyIdentities),
                PerformedSnapshot: true,
                PerformedParse: false);
        }

        try
        {
            using var stream = new MemoryStream(
                snapshot.Bytes,
                writable: false);
            XmlDocumentationReadManyResult result =
                XmlDocumentationReader.ReadMembers(
                    stream,
                    identities,
                    limits.XmlReadLimits);
            return new(
                new(
                    CompiledXmlReadStatus.Completed,
                    snapshot.Length,
                    result.Entries,
                    result.RetainedTextLimitExceeded),
                PerformedSnapshot: true,
                PerformedParse: true);
        }
        catch (Exception failure) when (
            failure is XmlException or IOException)
        {
            return new(
                new(
                    CompiledXmlReadStatus.Malformed,
                    snapshot.Length,
                    EmptyEntries,
                    EmptyIdentities),
                PerformedSnapshot: true,
                PerformedParse: false);
        }
    }

    private static IReadOnlyList<DocumentationCompiledXmlRejection>
        ValidateContributions(
            DocumentationHouseRequest request,
            IReadOnlyList<CompiledXmlContribution> contributions)
    {
        var rejections =
            new List<DocumentationCompiledXmlRejection>();
        DocumentationSubjectReference subject = request.Subject;
        foreach (CompiledXmlContribution contribution in contributions)
        {
            DocumentationCompiledXmlRejectionKind? rejection =
                !ReferenceEquals(contribution.Subject, subject)
                    ? DocumentationCompiledXmlRejectionKind
                        .SubjectMismatch
                    : !ReferenceEquals(
                        contribution.Library,
                        subject.Library)
                        ? DocumentationCompiledXmlRejectionKind
                            .LibraryMismatch
                        : !ReferenceEquals(
                            contribution.ApiContent,
                            subject.ApiContent)
                            ? DocumentationCompiledXmlRejectionKind
                                .ApiContentMismatch
                            : ValidateCompanion(contribution);
            if (rejection is { } kind)
            {
                rejections.Add(
                    new DocumentationCompiledXmlRejection(
                        contribution,
                        kind));
            }
        }

        return rejections.Count == 0
            ? []
            : rejections.AsReadOnly();
    }

    private static DocumentationCompiledXmlRejectionKind?
        ValidateCompanion(CompiledXmlContribution contribution)
    {
        if (contribution.Kind != CompiledXmlContributionKind.Candidate)
            return null;

        LibraryContentReference content =
            contribution.CompiledXmlContent!;
        return !ReferenceEquals(content.Library, contribution.Library)
            || !content.HasRole(
                LibraryContentRole.CompiledXmlDocumentation)
            || !ReferenceEquals(
                content.AssociatedAssembly,
                contribution.ApiContent)
                ? DocumentationCompiledXmlRejectionKind.CompanionMismatch
                : null;
    }

    private static CompiledXmlContribution? SelectCandidate(
        IReadOnlyList<CompiledXmlContribution> candidates)
    {
        if (candidates.Count == 1)
            return candidates[0];
        LibraryContentReference firstContent =
            candidates[0].CompiledXmlContent!;
        if (candidates.All(
                candidate => ReferenceEquals(
                    candidate.CompiledXmlContent,
                    firstContent)))
        {
            return candidates[0];
        }

        var candidatesByContent =
            new Dictionary<
                LibraryContentReference,
                CompiledXmlContribution>(
                    ReferenceEqualityComparer.Instance);
        foreach (CompiledXmlContribution candidate in candidates)
        {
            if (candidate.Precedence is null)
                return null;

            LibraryContentReference content =
                candidate.CompiledXmlContent!;
            if (candidatesByContent.TryGetValue(
                    content,
                    out CompiledXmlContribution? observed))
            {
                if (observed.Precedence != candidate.Precedence)
                    return null;
                continue;
            }

            candidatesByContent.Add(content, candidate);
        }

        CompiledXmlContribution[] ordered =
            candidatesByContent.Values
                .OrderBy(static candidate => candidate.Precedence)
                .ToArray();
        return ordered[0].Precedence == ordered[1].Precedence
            ? null
            : ordered[0];
    }

    private sealed record XmlSnapshot(byte[]? Bytes, int Length);

    private static IReadOnlyDictionary<string, XmlDocumentationEntry>
        EmptyEntries { get; } =
        new Dictionary<string, XmlDocumentationEntry>();

    private static IReadOnlySet<string> EmptyIdentities { get; } =
        new HashSet<string>(StringComparer.Ordinal);

    private enum CompiledXmlReadStatus
    {
        Completed,
        Deadline,
        ContentAccessFailed,
        ByteLimit,
        Malformed,
    }

    private sealed record CompiledXmlRead(
        CompiledXmlReadStatus Status,
        int Length,
        IReadOnlyDictionary<string, XmlDocumentationEntry> Entries,
        IReadOnlySet<string> RetainedTextLimitExceeded);

    private sealed record CompiledXmlBatchRead(
        CompiledXmlRead Read,
        bool PerformedSnapshot,
        bool PerformedParse);

    private sealed class CompiledXmlBatch
    {
        private readonly IReadOnlyList<CompiledXmlBatchRequest> _requests;
        private readonly Dictionary<
            BatchKey,
            CompiledXmlRead> _reads = [];

        internal CompiledXmlBatch(
            IReadOnlyList<CompiledXmlBatchRequest> requests) =>
            _requests = requests;

        internal CompiledXmlBatchRead Read(
            CompiledXmlContribution selected,
            LibraryOperationLease operationLease,
            DocumentationHouseLimits limits,
            CancellationToken cancellationToken)
        {
            var key = new BatchKey(
                selected.CompiledXmlContent!,
                limits.MaximumCompiledXmlBytes,
                limits.XmlReadLimits);
            if (_reads.TryGetValue(key, out CompiledXmlRead? read))
            {
                return new(
                    read,
                    PerformedSnapshot: false,
                    PerformedParse: false);
            }

            var identities = new List<XmlDocMemberIdentity>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            DateTimeOffset parseDeadline = DateTimeOffset.MinValue;
            foreach (CompiledXmlBatchRequest paired in _requests)
            {
                if (TryGetKey(
                        paired.Request,
                        paired.OperationLease,
                        out BatchKey? requestKey)
                    && requestKey == key
                    && seen.Add(
                        paired.Request.Subject
                            .CompiledXmlIdentity.Value))
                {
                    identities.Add(
                        paired.Request.Subject
                            .CompiledXmlIdentity);
                }
                if (requestKey == key
                    && paired.Request.Plan.Deadline > parseDeadline)
                {
                    parseDeadline = paired.Request.Plan.Deadline;
                }
            }
            CompiledXmlBatchRead performed = ReadCompiledXml(
                selected,
                operationLease,
                limits,
                identities,
                parseDeadline,
                cancellationToken);
            _reads.Add(key, performed.Read);
            return performed;
        }

        private static bool TryGetKey(
            DocumentationHouseRequest request,
            LibraryOperationLease operationLease,
            out BatchKey? key)
        {
            key = null;
            DocumentationSubjectReference subject = request.Subject;
            DocumentationHouseOperationPlan plan = request.Plan;
            if (!ReferenceEquals(
                    subject.ApiContent.Library,
                    subject.Library)
                || !ReferenceEquals(
                    subject.ApiContent,
                    subject.Library.ApiAssembly)
                || !subject.ApiContent.HasRole(
                    LibraryContentRole.ApiAssembly)
                || !ReferenceEquals(
                    operationLease.Reference,
                    subject.Library)
                || DateTimeOffset.UtcNow >= plan.Deadline
                || plan.ExceedsCompiledXmlContributionLimit)
            {
                return false;
            }

            IReadOnlyList<CompiledXmlContribution> contributions =
                plan.CompiledXmlContributions;
            if (ValidateContributions(request, contributions).Count > 0
                || contributions.Any(static contribution =>
                    contribution.Kind
                        == CompiledXmlContributionKind.Partial))
            {
                return false;
            }

            CompiledXmlContribution[] candidates =
            [
                .. contributions.Where(static contribution =>
                    contribution.Kind
                        == CompiledXmlContributionKind.Candidate),
            ];
            CompiledXmlContribution? selected =
                candidates.Length == 0
                    ? null
                    : SelectCandidate(candidates);
            if (selected is null)
                return false;

            key = new BatchKey(
                selected.CompiledXmlContent!,
                plan.Limits.MaximumCompiledXmlBytes,
                plan.Limits.XmlReadLimits);
            return true;
        }

        private sealed record BatchKey(
            LibraryContentReference Content,
            int MaximumCompiledXmlBytes,
            XmlDocumentationReadLimits XmlReadLimits);
    }

    private sealed record CompiledXmlBatchRequest(
        DocumentationHouseRequest Request,
        LibraryOperationLease OperationLease);

    private abstract record ProvisionalOutcome(
        DocumentationHouseWorkCharge Work);

    private sealed record CompletedOutcome(
        DocumentationCompiledXmlAttempt Attempt,
        DocumentationHouseWorkCharge Work)
        : ProvisionalOutcome(Work);

    private sealed record RejectedOutcome(
        DocumentationHouseRejection Rejection,
        DocumentationHouseWorkCharge Work)
        : ProvisionalOutcome(Work);

    private sealed record FailedOutcome(
        DocumentationHouseFailure Failure,
        DocumentationHouseWorkCharge Work)
        : ProvisionalOutcome(Work);

    private sealed record IncompleteOutcome(
        DocumentationIncompleteBoundary Boundary,
        DocumentationHouseWorkCharge Work)
        : ProvisionalOutcome(Work);
}
