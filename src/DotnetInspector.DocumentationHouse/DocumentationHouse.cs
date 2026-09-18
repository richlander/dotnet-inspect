using System.Xml;

using CSharpText;
using DotnetInspector.Libraries;

namespace DotnetInspector.DocumentationHouse;

/// <summary>
/// Settles exact Library-scoped documentation into detached typed evidence.
/// </summary>
public static class DocumentationHouse
{
    public static ValueTask<DocumentationHouseOutcome> ExecuteAsync(
        DocumentationHouseRequest request,
        LibraryOperationLease operationLease,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(operationLease);

        ProvisionalOutcome provisional;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            provisional = ExecuteCore(
                request,
                operationLease,
                cancellationToken);
        }
        finally
        {
            operationLease.Dispose();
        }

        cancellationToken.ThrowIfCancellationRequested();
        var settlement = new DocumentationLibraryLeaseSettlement(
            DocumentationLibraryLeaseConsumer.DocumentationHouse);
        return ValueTask.FromResult(
            provisional.Complete(request, settlement));
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

        ProvisionalOutcome[] provisional =
            new ProvisionalOutcome[requests.Count];
        var batch = new CompiledXmlBatch(requests);
        try
        {
            for (int index = 0; index < requests.Count; index++)
            {
                provisional[index] = ExecuteCore(
                    requests[index],
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
            outcomes[index] = provisional[index].Complete(
                requests[index],
                new DocumentationLibraryLeaseSettlement(
                    DocumentationLibraryLeaseConsumer
                        .DocumentationHouse));
        }
        return ValueTask.FromResult<
            IReadOnlyList<DocumentationHouseOutcome>>(outcomes);
    }

    private static ProvisionalOutcome ExecuteCore(
        DocumentationHouseRequest request,
        LibraryOperationLease operationLease,
        CancellationToken cancellationToken,
        CompiledXmlBatch? batch = null)
    {
        DocumentationSubjectReference subject = request.Subject;
        DocumentationHouseOperationPlan plan = request.Plan;
        var emptyWork = new DocumentationHouseWorkCharge(
            ContributionsObserved: 0,
            CompiledXmlBytesObserved: 0,
            ParsedCompiledXml: false);
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
        if (DateTimeOffset.UtcNow >= plan.Deadline)
        {
            return new IncompleteOutcome(
                DocumentationIncompleteBoundary.Deadline,
                emptyWork);
        }

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
            ?? new(
                ReadCompiledXml(
                    selected,
                    operationLease,
                    plan.Limits,
                    [subject.CompiledXmlIdentity],
                    cancellationToken),
                PerformedRead: true);
        CompiledXmlRead read = batchRead.Read;

        var snapshotWork = new DocumentationHouseWorkCharge(
            contributionCount,
            batchRead.PerformedRead ? read.Length : 0,
            ParsedCompiledXml: false);
        cancellationToken.ThrowIfCancellationRequested();
        if (DateTimeOffset.UtcNow >= plan.Deadline)
        {
            return new CompletedOutcome(
                new DocumentationCompiledXmlAttempt.Incomplete(
                    DocumentationIncompleteBoundary.Deadline,
                    selected,
                    contributions),
                snapshotWork);
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
                snapshotWork);
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
                snapshotWork);
        }

        cancellationToken.ThrowIfCancellationRequested();
        var parsedWork = new DocumentationHouseWorkCharge(
            contributionCount,
            batchRead.PerformedRead ? read.Length : 0,
            ParsedCompiledXml: batchRead.PerformedRead);
        if (DateTimeOffset.UtcNow >= plan.Deadline)
        {
            return new CompletedOutcome(
                new DocumentationCompiledXmlAttempt.Incomplete(
                    DocumentationIncompleteBoundary.Deadline,
                    selected,
                    contributions),
                parsedWork);
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
        return new CompletedOutcome(completed, parsedWork);
    }

    private static CompiledXmlRead ReadCompiledXml(
        CompiledXmlContribution selected,
        LibraryOperationLease operationLease,
        DocumentationHouseLimits limits,
        IReadOnlyCollection<XmlDocMemberIdentity> identities,
        CancellationToken cancellationToken)
    {
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
                CompiledXmlReadStatus.ContentAccessFailed,
                Length: 0,
                EmptyEntries);
        }

        if (snapshot.Bytes is null)
        {
            return new(
                CompiledXmlReadStatus.ByteLimit,
                snapshot.Length,
                EmptyEntries);
        }

        try
        {
            using var stream = new MemoryStream(
                snapshot.Bytes,
                writable: false);
            IReadOnlyDictionary<string, XmlDocumentationEntry> entries =
                XmlDocumentationReader.ReadMembers(
                    stream,
                    identities,
                    limits.XmlReadLimits);
            return new(
                CompiledXmlReadStatus.Completed,
                snapshot.Length,
                entries);
        }
        catch (Exception failure) when (
            failure is XmlException or IOException)
        {
            return new(
                CompiledXmlReadStatus.Malformed,
                snapshot.Length,
                EmptyEntries);
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

    private enum CompiledXmlReadStatus
    {
        Completed,
        ContentAccessFailed,
        ByteLimit,
        Malformed,
    }

    private sealed record CompiledXmlRead(
        CompiledXmlReadStatus Status,
        int Length,
        IReadOnlyDictionary<string, XmlDocumentationEntry> Entries);

    private sealed record CompiledXmlBatchRead(
        CompiledXmlRead Read,
        bool PerformedRead);

    private sealed class CompiledXmlBatch
    {
        private readonly IReadOnlyList<DocumentationHouseRequest> _requests;
        private readonly Dictionary<
            BatchKey,
            CompiledXmlRead> _reads = [];

        internal CompiledXmlBatch(
            IReadOnlyList<DocumentationHouseRequest> requests) =>
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
                return new(read, PerformedRead: false);

            var identities = new List<XmlDocMemberIdentity>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (DocumentationHouseRequest request in _requests)
            {
                if (TryGetKey(
                        request,
                        operationLease,
                        out BatchKey? requestKey)
                    && requestKey == key
                    && seen.Add(
                        request.Subject.CompiledXmlIdentity.Value))
                {
                    identities.Add(
                        request.Subject.CompiledXmlIdentity);
                }
            }
            read = ReadCompiledXml(
                selected,
                operationLease,
                limits,
                identities,
                cancellationToken);
            _reads.Add(key, read);
            return new(read, PerformedRead: true);
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

    private abstract record ProvisionalOutcome(
        DocumentationHouseWorkCharge Work)
    {
        internal abstract DocumentationHouseOutcome Complete(
            DocumentationHouseRequest request,
            DocumentationLibraryLeaseSettlement settlement);
    }

    private sealed record CompletedOutcome(
        DocumentationCompiledXmlAttempt Attempt,
        DocumentationHouseWorkCharge Work)
        : ProvisionalOutcome(Work)
    {
        internal override DocumentationHouseOutcome Complete(
            DocumentationHouseRequest request,
            DocumentationLibraryLeaseSettlement settlement) =>
            new DocumentationHouseOutcome.Completed(
                request,
                Attempt,
                Work,
                settlement);
    }

    private sealed record RejectedOutcome(
        DocumentationHouseRejection Rejection,
        DocumentationHouseWorkCharge Work)
        : ProvisionalOutcome(Work)
    {
        internal override DocumentationHouseOutcome Complete(
            DocumentationHouseRequest request,
            DocumentationLibraryLeaseSettlement settlement) =>
            new DocumentationHouseOutcome.Rejected(
                request,
                Rejection,
                Work,
                settlement);
    }

    private sealed record FailedOutcome(
        DocumentationHouseFailure Failure,
        DocumentationHouseWorkCharge Work)
        : ProvisionalOutcome(Work)
    {
        internal override DocumentationHouseOutcome Complete(
            DocumentationHouseRequest request,
            DocumentationLibraryLeaseSettlement settlement) =>
            new DocumentationHouseOutcome.Failed(
                request,
                Failure,
                Work,
                settlement);
    }

    private sealed record IncompleteOutcome(
        DocumentationIncompleteBoundary Boundary,
        DocumentationHouseWorkCharge Work)
        : ProvisionalOutcome(Work)
    {
        internal override DocumentationHouseOutcome Complete(
            DocumentationHouseRequest request,
            DocumentationLibraryLeaseSettlement settlement) =>
            new DocumentationHouseOutcome.Incomplete(
                request,
                Boundary,
                Work,
                settlement);
    }
}
