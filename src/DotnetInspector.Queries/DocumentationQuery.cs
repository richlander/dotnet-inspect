using CSharpText;
using DotnetInspector.DocumentationHouse;
using DotnetInspector.Libraries;

namespace DotnetInspector.Queries;

public static partial class DocumentationQuery
{
    /// <summary>
    /// Executes one QuerySpace-resolved demand against an already-authorized
    /// DocumentationHouse operation plan.
    /// </summary>
    public static async ValueTask<DocumentationQueryResult> ExecuteAsync(
        DocumentationQueryPlan query,
        DocumentationHouseRequestIdentity requestIdentity,
        DocumentationSubjectReference subject,
        DocumentationHouseOperationPlan operationPlan,
        LibraryOperationLease operationLease,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(requestIdentity);
        ArgumentNullException.ThrowIfNull(subject);
        ArgumentNullException.ThrowIfNull(operationPlan);
        ArgumentNullException.ThrowIfNull(operationLease);

        var request = new DocumentationHouseRequest(
            requestIdentity,
            subject,
            query.Demand,
            operationPlan);
        DocumentationHouseOutcome outcome =
            await DocumentationHouse.DocumentationHouse.ExecuteAsync(
                    request,
                    operationLease,
                    cancellationToken)
                .ConfigureAwait(false);
        return new(query.Request, outcome, Content(outcome));
    }

    private static DocumentationQueryOutcome Content(
        DocumentationHouseOutcome outcome)
    {
        CompiledDocumentationSubject subject =
            CompiledDocumentationQuery.ProjectSubject(
                outcome.Request.Subject);
        return outcome switch
        {
            DocumentationHouseOutcome.Completed completed =>
                new DocumentationQueryOutcome.Completed(
                    subject,
                    completed.CompiledXmlAttempt is { } compiled
                        ? CompiledDocumentationQuery.ProjectAttempt(
                            subject,
                            compiled)
                        : null,
                    completed.AuthoredSourceAttempt is { } authored
                        ? Snapshot(authored)
                        : null,
                    Snapshot(completed.Fields)),
            DocumentationHouseOutcome.Rejected rejected =>
                new DocumentationQueryOutcome.RequestRejected(
                    subject,
                    Snapshot(rejected.Rejection.Kind)),
            DocumentationHouseOutcome.Failed failed =>
                new DocumentationQueryOutcome.Failed(
                    subject,
                    Snapshot(failed.Failure.Kind),
                    CompiledDocumentationQuery.ProjectSource(
                        failed.Failure.Selected)),
            DocumentationHouseOutcome.Incomplete incomplete =>
                new DocumentationQueryOutcome.Incomplete(
                    subject,
                    CompiledDocumentationQuery.ProjectIncompleteReason(
                        incomplete.Boundary)),
            _ => throw new InvalidOperationException(
                "Unknown DocumentationHouse outcome."),
        };
    }

    private static AuthoredDocumentationOutcome Snapshot(
        DocumentationAuthoredSourceAttempt attempt) =>
        attempt switch
        {
            DocumentationAuthoredSourceAttempt.Available available =>
                new AuthoredDocumentationOutcome.Available(
                    DocumentationQueryProjection.Snapshot(
                        ((CSharpAuthoredDocumentationOutcome.Available)
                            available.Contribution.Documentation)
                            .Documentation)),
            DocumentationAuthoredSourceAttempt.Absent =>
                new AuthoredDocumentationOutcome.Absent(),
            DocumentationAuthoredSourceAttempt.Unavailable unavailable =>
                new AuthoredDocumentationOutcome.Unavailable(
                    Snapshot(unavailable.UnavailableKind),
                    SnapshotObservation(unavailable.OperationOutcome)),
            DocumentationAuthoredSourceAttempt.Ambiguous ambiguous =>
                new AuthoredDocumentationOutcome.Ambiguous(
                    Snapshot(ambiguous.Ambiguity),
                    SnapshotObservation(ambiguous.OperationOutcome)),
            DocumentationAuthoredSourceAttempt.Rejected rejected =>
                new AuthoredDocumentationOutcome.Rejected(
                    SnapshotRejection(rejected),
                    SnapshotObservation(rejected.OperationOutcome)),
            DocumentationAuthoredSourceAttempt.Failed failed =>
                new AuthoredDocumentationOutcome.Failed(
                    Snapshot(
                        ((DocumentationAuthoredSourceOperationOutcome.Failed)
                            failed.OperationOutcome!)
                            .Failure),
                    SnapshotObservation(failed.OperationOutcome)),
            DocumentationAuthoredSourceAttempt.Incomplete incomplete =>
                new AuthoredDocumentationOutcome.Incomplete(
                    SnapshotIncomplete(incomplete.OperationOutcome!),
                    SnapshotObservation(incomplete.OperationOutcome)),
            _ => throw new InvalidOperationException(
                "Unknown authored-source attempt."),
        };

    private static AuthoredDocumentationObservation? SnapshotObservation(
        DocumentationAuthoredSourceOperationOutcome? outcome)
    {
        DocumentationAuthoredSourceOperationObservation? observation =
            outcome switch
            {
                DocumentationAuthoredSourceOperationOutcome.Unavailable
                    unavailable => unavailable.Observation,
                DocumentationAuthoredSourceOperationOutcome.Rejected
                    rejected => rejected.Observation,
                DocumentationAuthoredSourceOperationOutcome.Failed
                    failed => failed.Observation,
                DocumentationAuthoredSourceOperationOutcome.Incomplete
                    incomplete => incomplete.Observation,
                _ => null,
            };
        return observation is null
            ? null
            : new(
                observation.Code,
                observation.Detail,
                observation.DetailWasTruncated);
    }

    private static AuthoredDocumentationRejectionReason SnapshotRejection(
        DocumentationAuthoredSourceAttempt.Rejected rejected)
    {
        if (rejected.Rejection
            == DocumentationAuthoredSourceAttemptRejectionKind
                .OperationEvidenceMismatch)
        {
            return AuthoredDocumentationRejectionReason
                .OperationEvidenceMismatch;
        }

        if (rejected.OperationOutcome
            is not DocumentationAuthoredSourceOperationOutcome.Rejected
                operationRejected)
        {
            throw new InvalidOperationException(
                "An operation rejection did not retain its rejected operation outcome.");
        }

        return Snapshot(operationRejected.Rejection);
    }

    private static AuthoredDocumentationIncompleteReason SnapshotIncomplete(
        DocumentationAuthoredSourceOperationOutcome outcome) =>
        outcome switch
        {
            DocumentationAuthoredSourceOperationOutcome.Unavailable
                {
                    UnavailableKind:
                        DocumentationAuthoredUnavailableKind
                            .DeclarationUncertain,
                } =>
                AuthoredDocumentationIncompleteReason.DeclarationUncertain,
            DocumentationAuthoredSourceOperationOutcome.Incomplete
                incomplete => Snapshot(incomplete.Boundary),
            _ => throw new InvalidOperationException(
                "An incomplete authored attempt retained an unexpected operation outcome."),
        };

    private static DocumentationQueryFieldSettlement Snapshot(
        DocumentationFieldSettlement fields) =>
        new(
            Snapshot(fields.Summary),
            Snapshot(fields.Remarks),
            Snapshot(fields.Returns),
            [
                .. fields.Parameters
                    .OrderBy(
                        static parameter => parameter.Key,
                        StringComparer.Ordinal)
                    .Select(
                        static parameter =>
                            new DocumentationQueryParameterField(
                                parameter.Key,
                                Snapshot(parameter.Value))),
            ],
                SnapshotExceptions(fields.Exceptions),
                SnapshotSamples(fields.Samples));

    private static DocumentationQueryTextFieldEvidence Snapshot(
            DocumentationFieldEvidence<string> field) =>
            new(
                Snapshot(field.Kind),
                [.. field.RequestedChannels.Select(Snapshot)],
                [
                    .. field.Contributions.Select(
                        static contribution =>
                            new DocumentationQueryTextFieldContribution(
                                Snapshot(contribution.Channel),
                                contribution.Value)),
                ]);

    private static DocumentationQueryExceptionFieldEvidence
            SnapshotExceptions(
                DocumentationFieldEvidence<
                    IReadOnlyList<XmlDocumentationException>> field) =>
            new(
                Snapshot(field.Kind),
                [.. field.RequestedChannels.Select(Snapshot)],
                [
                    .. field.Contributions.Select(
                        static contribution =>
                            new DocumentationQueryExceptionFieldContribution(
                                Snapshot(contribution.Channel),
                                [
                                    .. contribution.Value.Select(
                                        static exception =>
                                            new CompiledDocumentationException(
                                                exception.Cref,
                                                exception.Description)),
                                ])),
            ]);

    private static DocumentationQuerySampleFieldEvidence
            SnapshotSamples(
                DocumentationFieldEvidence<
                    IReadOnlyList<XmlDocumentationSampleReference>> field) =>
            new(
                Snapshot(field.Kind),
                [.. field.RequestedChannels.Select(Snapshot)],
                [
                    .. field.Contributions.Select(
                        static contribution =>
                            new DocumentationQuerySampleFieldContribution(
                                Snapshot(contribution.Channel),
                                [
                                    .. contribution.Value.Select(
                                        static sample =>
                                            new CompiledDocumentationSample(
                                                sample.Source,
                                                sample.Title,
                                                sample.Region)),
                                ])),
                ]);

    private static DocumentationQueryChannel Snapshot(
        DocumentationChannel channel) =>
        channel switch
        {
            DocumentationChannel.CompiledXml =>
                DocumentationQueryChannel.CompiledXml,
            DocumentationChannel.AuthoredSource =>
                DocumentationQueryChannel.AuthoredSource,
            _ => throw new InvalidOperationException(
                "Unknown documentation channel."),
        };

    private static DocumentationQueryFieldEvidenceKind Snapshot(
        DocumentationFieldEvidenceKind kind) =>
        kind switch
        {
            DocumentationFieldEvidenceKind.Selected =>
                DocumentationQueryFieldEvidenceKind.Selected,
            DocumentationFieldEvidenceKind.Corroborated =>
                DocumentationQueryFieldEvidenceKind.Corroborated,
            DocumentationFieldEvidenceKind.Conflict =>
                DocumentationQueryFieldEvidenceKind.Conflict,
            DocumentationFieldEvidenceKind.Absent =>
                DocumentationQueryFieldEvidenceKind.Absent,
            _ => throw new InvalidOperationException(
                "Unknown documentation field evidence kind."),
        };

    private static DocumentationQueryRequestRejectionReason Snapshot(
        DocumentationHouseRejectionKind kind) =>
        kind switch
        {
            DocumentationHouseRejectionKind.LibraryReferenceMismatch =>
                DocumentationQueryRequestRejectionReason
                    .LibraryReferenceMismatch,
            DocumentationHouseRejectionKind.ApiContentMismatch =>
                DocumentationQueryRequestRejectionReason.ApiContentMismatch,
            DocumentationHouseRejectionKind.LeaseReferenceMismatch =>
                DocumentationQueryRequestRejectionReason
                    .LeaseReferenceMismatch,
            DocumentationHouseRejectionKind.AuthoredSourceBindingMismatch =>
                DocumentationQueryRequestRejectionReason
                    .AuthoredSourceBindingMismatch,
            _ => throw new InvalidOperationException(
                "Unknown DocumentationHouse rejection kind."),
        };

    private static DocumentationQueryFailureReason Snapshot(
        DocumentationCompiledXmlFailureKind kind) =>
        kind switch
        {
            DocumentationCompiledXmlFailureKind
                .MalformedOrUnreadableDocument =>
                DocumentationQueryFailureReason
                    .CompiledXmlMalformedOrUnreadableDocument,
            DocumentationCompiledXmlFailureKind.ContentAccessFailed =>
                DocumentationQueryFailureReason
                    .CompiledXmlContentAccessFailed,
            _ => throw new InvalidOperationException(
                "Unknown DocumentationHouse failure kind."),
        };

    private static AuthoredDocumentationUnavailableReason Snapshot(
        DocumentationAuthoredSourceUnavailableKind kind) =>
        kind switch
        {
            DocumentationAuthoredSourceUnavailableKind.OperationUnavailable =>
                AuthoredDocumentationUnavailableReason.OperationUnavailable,
            DocumentationAuthoredSourceUnavailableKind.SourceUnavailable =>
                AuthoredDocumentationUnavailableReason.SourceUnavailable,
            DocumentationAuthoredSourceUnavailableKind
                .PhysicalDeclarationUnavailable =>
                AuthoredDocumentationUnavailableReason
                    .PhysicalDeclarationUnavailable,
            DocumentationAuthoredSourceUnavailableKind.DeclarationNotFound =>
                AuthoredDocumentationUnavailableReason.DeclarationNotFound,
            _ => throw new InvalidOperationException(
                "Unknown authored-source unavailable kind."),
        };

    private static AuthoredDocumentationAmbiguityReason Snapshot(
        DocumentationAuthoredSourceAmbiguityKind kind) =>
        kind switch
        {
            DocumentationAuthoredSourceAmbiguityKind
                .PhysicalDeclarationConflict =>
                AuthoredDocumentationAmbiguityReason
                    .PhysicalDeclarationConflict,
            DocumentationAuthoredSourceAmbiguityKind.DeclarationAmbiguous =>
                AuthoredDocumentationAmbiguityReason.DeclarationAmbiguous,
            _ => throw new InvalidOperationException(
                "Unknown authored-source ambiguity kind."),
        };

    private static AuthoredDocumentationRejectionReason Snapshot(
        DocumentationAuthoredRejectionKind kind) =>
        kind switch
        {
            DocumentationAuthoredRejectionKind.AlreadyInvoked =>
                AuthoredDocumentationRejectionReason.AlreadyInvoked,
            DocumentationAuthoredRejectionKind.BindingMismatch =>
                AuthoredDocumentationRejectionReason.BindingMismatch,
            DocumentationAuthoredRejectionKind.LeaseReferenceMismatch =>
                AuthoredDocumentationRejectionReason.LeaseReferenceMismatch,
            DocumentationAuthoredRejectionKind.SourceRejected =>
                AuthoredDocumentationRejectionReason.SourceRejected,
            DocumentationAuthoredRejectionKind.SourceEvidenceMismatch =>
                AuthoredDocumentationRejectionReason
                    .SourceEvidenceMismatch,
            DocumentationAuthoredRejectionKind
                .PhysicalDeclarationRejected =>
                AuthoredDocumentationRejectionReason
                    .PhysicalDeclarationRejected,
            _ => throw new InvalidOperationException(
                "Unknown authored-source rejection kind."),
        };

    private static AuthoredDocumentationFailureReason Snapshot(
        DocumentationAuthoredFailureKind kind) =>
        kind switch
        {
            DocumentationAuthoredFailureKind.SourceFailed =>
                AuthoredDocumentationFailureReason.SourceFailed,
            DocumentationAuthoredFailureKind.PhysicalDeclarationFailed =>
                AuthoredDocumentationFailureReason
                    .PhysicalDeclarationFailed,
            DocumentationAuthoredFailureKind.MalformedDocumentation =>
                AuthoredDocumentationFailureReason.MalformedDocumentation,
            _ => throw new InvalidOperationException(
                "Unknown authored-source failure kind."),
        };

    private static AuthoredDocumentationIncompleteReason Snapshot(
        DocumentationAuthoredIncompleteBoundary boundary) =>
        boundary switch
        {
            DocumentationAuthoredIncompleteBoundary.Deadline =>
                AuthoredDocumentationIncompleteReason.Deadline,
            DocumentationAuthoredIncompleteBoundary.SourceDocuments =>
                AuthoredDocumentationIncompleteReason.SourceDocuments,
            DocumentationAuthoredIncompleteBoundary.SourceBytes =>
                AuthoredDocumentationIncompleteReason.SourceBytes,
            DocumentationAuthoredIncompleteBoundary.SourceCharacters =>
                AuthoredDocumentationIncompleteReason.SourceCharacters,
            DocumentationAuthoredIncompleteBoundary.SourceHouse =>
                AuthoredDocumentationIncompleteReason.SourceHouse,
            DocumentationAuthoredIncompleteBoundary.PhysicalDeclaration =>
                AuthoredDocumentationIncompleteReason.PhysicalDeclaration,
            DocumentationAuthoredIncompleteBoundary.Documentation =>
                AuthoredDocumentationIncompleteReason.Documentation,
            _ => throw new InvalidOperationException(
                "Unknown authored-source incomplete boundary."),
        };
}
