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

    private static ProvisionalOutcome ExecuteCore(
        DocumentationHouseRequest request,
        LibraryOperationLease operationLease,
        CancellationToken cancellationToken)
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
        XmlSnapshot snapshot;
        try
        {
            snapshot = operationLease.Snapshot(
                selected.CompiledXmlContent!,
                plan.Limits.MaximumCompiledXmlBytes,
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
            cancellationToken.ThrowIfCancellationRequested();
            if (DateTimeOffset.UtcNow >= plan.Deadline)
            {
                return new CompletedOutcome(
                    new DocumentationCompiledXmlAttempt.Incomplete(
                        DocumentationIncompleteBoundary.Deadline,
                        selected,
                        contributions),
                    selectionWork);
            }
            return new FailedOutcome(
                new(
                    DocumentationHouseFailureStage
                        .CompiledXmlSnapshot,
                    DocumentationCompiledXmlFailureKind
                        .ContentAccessFailed),
                selectionWork);
        }

        var snapshotWork = new DocumentationHouseWorkCharge(
            contributionCount,
            snapshot.Length,
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
        if (snapshot.Bytes is null)
        {
            return new CompletedOutcome(
                new DocumentationCompiledXmlAttempt.Incomplete(
                    DocumentationIncompleteBoundary
                        .CompiledXmlByteLimit,
                    selected,
                    contributions),
                snapshotWork);
        }
        XmlDocumentationEntry? documentation;
        try
        {
            using var stream = new MemoryStream(
                snapshot.Bytes,
                writable: false);
            documentation = XmlDocumentationReader.ReadMember(
                stream,
                subject.CompiledXmlIdentity,
                plan.Limits.XmlReadLimits);
        }
        catch (Exception failure) when (
            failure is XmlException or IOException)
        {
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
            snapshot.Length,
            ParsedCompiledXml: true);
        if (DateTimeOffset.UtcNow >= plan.Deadline)
        {
            return new CompletedOutcome(
                new DocumentationCompiledXmlAttempt.Incomplete(
                    DocumentationIncompleteBoundary.Deadline,
                    selected,
                    contributions),
                parsedWork);
        }

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
