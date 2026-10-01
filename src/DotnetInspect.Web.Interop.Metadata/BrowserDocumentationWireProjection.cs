using Query = DotnetInspector.Queries;
using Wire = DotnetInspect.Web.Interop.Metadata.Wire;

namespace DotnetInspect.Web.Interop.Metadata;

internal static class BrowserDocumentationWireProjection
{
    internal static Wire.DocumentationQueryOutcome Project(
        Query.DocumentationQueryOutcome outcome) =>
        outcome switch
        {
            Query.DocumentationQueryOutcome.Completed completed =>
                new Wire.DocumentationQueryOutcome.Completed(
                    Project(completed.Subject),
                    completed.CompiledXml is null
                        ? null
                        : Project(completed.CompiledXml),
                    completed.AuthoredSource is null
                        ? null
                        : Project(completed.AuthoredSource),
                    Project(completed.Fields)),
            Query.DocumentationQueryOutcome.RequestRejected rejected =>
                new Wire.DocumentationQueryOutcome.RequestRejected(
                    Project(rejected.Subject),
                    Project(rejected.Reason)),
            Query.DocumentationQueryOutcome.Failed failed =>
                new Wire.DocumentationQueryOutcome.Failed(
                    Project(failed.Subject),
                    Project(failed.Reason),
                    Project(failed.Source)),
            Query.DocumentationQueryOutcome.Incomplete incomplete =>
                new Wire.DocumentationQueryOutcome.Incomplete(
                    Project(incomplete.Subject),
                    Project(incomplete.Reason)),
            _ => throw new InvalidOperationException(
                "Unknown documentation query outcome."),
        };

    private static Wire.DocumentationQueryFieldSettlement Project(
        Query.DocumentationQueryFieldSettlement fields) =>
        new(
            Project(fields.Summary),
            Project(fields.Remarks),
            Project(fields.Returns),
            [.. fields.Parameters.Select(static parameter =>
                new Wire.DocumentationQueryParameterField(
                    parameter.Name,
                    Project(parameter.Evidence)))],
            new(
                Project(fields.Exceptions.Kind),
                [.. fields.Exceptions.RequestedChannels.Select(Project)],
                [.. fields.Exceptions.Contributions.Select(
                    static contribution =>
                        new Wire
                            .DocumentationQueryExceptionFieldContribution(
                                Project(contribution.Channel),
                                [.. contribution.Value.Select(
                                    static exception =>
                                        new Wire
                                            .CompiledDocumentationException(
                                                exception.Reference,
                                                exception.Description))]))]),
            new(
                Project(fields.Samples.Kind),
                [.. fields.Samples.RequestedChannels.Select(Project)],
                [.. fields.Samples.Contributions.Select(
                    static contribution =>
                        new Wire
                            .DocumentationQuerySampleFieldContribution(
                                Project(contribution.Channel),
                                [.. contribution.Value.Select(
                                    static sample =>
                                        new Wire.CompiledDocumentationSample(
                                            sample.Code,
                                            sample.Title,
                                            sample.Region))]))]));

    private static Wire.DocumentationQueryTextFieldEvidence Project(
        Query.DocumentationQueryTextFieldEvidence evidence) =>
        new(
            Project(evidence.Kind),
            [.. evidence.RequestedChannels.Select(Project)],
            [.. evidence.Contributions.Select(static contribution =>
                new Wire.DocumentationQueryTextFieldContribution(
                    Project(contribution.Channel),
                    contribution.Value))]);

    private static Wire.CompiledDocumentationOutcome Project(
        Query.CompiledDocumentationOutcome outcome) =>
        outcome switch
        {
            Query.CompiledDocumentationOutcome.Available available =>
                new Wire.CompiledDocumentationOutcome.Available(
                    Project(available.Subject),
                    Project(available.Source),
                    Project(available.Documentation)),
            Query.CompiledDocumentationOutcome.Absent absent =>
                new Wire.CompiledDocumentationOutcome.Absent(
                    Project(absent.Subject),
                    [.. absent.Sources.Select(Project)],
                    absent.SourcesTruncated),
            Query.CompiledDocumentationOutcome.Unavailable unavailable =>
                new Wire.CompiledDocumentationOutcome.Unavailable(
                    Project(unavailable.Subject),
                    [.. unavailable.Sources.Select(Project)],
                    unavailable.SourcesTruncated),
            Query.CompiledDocumentationOutcome.Ambiguous ambiguous =>
                new Wire.CompiledDocumentationOutcome.Ambiguous(
                    Project(ambiguous.Subject),
                    [.. ambiguous.Candidates.Select(Project)],
                    ambiguous.CandidatesTruncated),
            Query.CompiledDocumentationOutcome.ContributionsRejected
                rejected =>
                    new Wire.CompiledDocumentationOutcome
                        .ContributionsRejected(
                            Project(rejected.Subject),
                            [.. rejected.Rejections.Select(
                                static rejection =>
                                    new Wire
                                        .CompiledDocumentationSourceRejection(
                                            Project(rejection.Source),
                                            Project(rejection.Reason)))],
                            rejected.RejectionsTruncated),
            Query.CompiledDocumentationOutcome.MalformedOrUnreadableDocument
                malformed =>
                    new Wire.CompiledDocumentationOutcome
                        .MalformedOrUnreadableDocument(
                            Project(malformed.Subject),
                            Project(malformed.Source)),
            Query.CompiledDocumentationOutcome.Incomplete incomplete =>
                new Wire.CompiledDocumentationOutcome.Incomplete(
                    Project(incomplete.Subject),
                    Project(incomplete.Reason),
                    [.. incomplete.Sources.Select(Project)],
                    incomplete.SourcesTruncated),
            Query.CompiledDocumentationOutcome.RequestRejected rejected =>
                new Wire.CompiledDocumentationOutcome.RequestRejected(
                    Project(rejected.Subject),
                    Project(rejected.Reason)),
            Query.CompiledDocumentationOutcome.ContentAccessFailed failed =>
                new Wire.CompiledDocumentationOutcome.ContentAccessFailed(
                    Project(failed.Subject),
                    Project(failed.Source)),
            _ => throw new InvalidOperationException(
                "Unknown compiled documentation outcome."),
        };

    private static Wire.AuthoredDocumentationOutcome Project(
        Query.AuthoredDocumentationOutcome outcome) =>
        outcome switch
        {
            Query.AuthoredDocumentationOutcome.Available available =>
                new Wire.AuthoredDocumentationOutcome.Available(
                    Project(available.Documentation)),
            Query.AuthoredDocumentationOutcome.Absent =>
                new Wire.AuthoredDocumentationOutcome.Absent(),
            Query.AuthoredDocumentationOutcome.Unavailable unavailable =>
                new Wire.AuthoredDocumentationOutcome.Unavailable(
                    Project(unavailable.Reason),
                    Project(unavailable.Observation)),
            Query.AuthoredDocumentationOutcome.Ambiguous ambiguous =>
                new Wire.AuthoredDocumentationOutcome.Ambiguous(
                    Project(ambiguous.Reason),
                    Project(ambiguous.Observation)),
            Query.AuthoredDocumentationOutcome.Rejected rejected =>
                new Wire.AuthoredDocumentationOutcome.Rejected(
                    Project(rejected.Reason),
                    Project(rejected.Observation)),
            Query.AuthoredDocumentationOutcome.Failed failed =>
                new Wire.AuthoredDocumentationOutcome.Failed(
                    Project(failed.Reason),
                    Project(failed.Observation)),
            Query.AuthoredDocumentationOutcome.Incomplete incomplete =>
                new Wire.AuthoredDocumentationOutcome.Incomplete(
                    Project(incomplete.Reason),
                    Project(incomplete.Observation)),
            _ => throw new InvalidOperationException(
                "Unknown authored documentation outcome."),
        };

    private static Wire.AuthoredDocumentationObservation? Project(
        Query.AuthoredDocumentationObservation? observation) =>
        observation is null
            ? null
            : new(
                observation.Code,
                observation.Detail,
                observation.DetailWasTruncated);

    private static Wire.CompiledDocumentationSubject Project(
        Query.CompiledDocumentationSubject subject) =>
        new(
            new(
                subject.Assembly.Name,
                subject.Assembly.Version,
                subject.Assembly.Culture,
                subject.Assembly.PublicKeyToken),
            subject.DocumentationId);

    private static Wire.CompiledDocumentationSource Project(
        Query.CompiledDocumentationSource source) =>
        new(
            Project(source.Kind),
            source.Name,
            source.Precedence);

    private static Wire.CompiledDocumentationSourceEvidence Project(
        Query.CompiledDocumentationSourceEvidence evidence) =>
        new(
            Project(evidence.Source),
            Project(evidence.Kind));

    private static Wire.CompiledDocumentationEntry Project(
        Query.CompiledDocumentationEntry documentation) =>
        new(
            documentation.Summary,
            documentation.Remarks,
            documentation.Returns,
            [.. documentation.Parameters.Select(static parameter =>
                new Wire.CompiledDocumentationParameter(
                    parameter.Name,
                    parameter.Description))],
            [.. documentation.Exceptions.Select(static exception =>
                new Wire.CompiledDocumentationException(
                    exception.Reference,
                    exception.Description))],
            [.. documentation.Samples.Select(static sample =>
                new Wire.CompiledDocumentationSample(
                    sample.Code,
                    sample.Title,
                    sample.Region))]);

    private static Wire.DocumentationQueryChannel Project(
        Query.DocumentationQueryChannel value) =>
        value switch
        {
            Query.DocumentationQueryChannel.CompiledXml =>
                Wire.DocumentationQueryChannel.CompiledXml,
            Query.DocumentationQueryChannel.AuthoredSource =>
                Wire.DocumentationQueryChannel.AuthoredSource,
            _ => throw new InvalidOperationException(
                "Unknown documentation query channel."),
        };

    private static Wire.DocumentationQueryFieldEvidenceKind Project(
        Query.DocumentationQueryFieldEvidenceKind value) =>
        value switch
        {
            Query.DocumentationQueryFieldEvidenceKind.Selected =>
                Wire.DocumentationQueryFieldEvidenceKind.Selected,
            Query.DocumentationQueryFieldEvidenceKind.Corroborated =>
                Wire.DocumentationQueryFieldEvidenceKind.Corroborated,
            Query.DocumentationQueryFieldEvidenceKind.Conflict =>
                Wire.DocumentationQueryFieldEvidenceKind.Conflict,
            Query.DocumentationQueryFieldEvidenceKind.Absent =>
                Wire.DocumentationQueryFieldEvidenceKind.Absent,
            _ => throw new InvalidOperationException(
                "Unknown documentation field evidence kind."),
        };

    private static Wire.CompiledDocumentationSourceKind Project(
        Query.CompiledDocumentationSourceKind value) =>
        value switch
        {
            Query.CompiledDocumentationSourceKind.Package =>
                Wire.CompiledDocumentationSourceKind.Package,
            Query.CompiledDocumentationSourceKind.Platform =>
                Wire.CompiledDocumentationSourceKind.Platform,
            Query.CompiledDocumentationSourceKind.DirectLibrary =>
                Wire.CompiledDocumentationSourceKind.DirectLibrary,
            Query.CompiledDocumentationSourceKind.SourceHouse =>
                Wire.CompiledDocumentationSourceKind.SourceHouse,
            _ => throw new InvalidOperationException(
                "Unknown compiled documentation source kind."),
        };

    private static Wire.CompiledDocumentationSourceEvidenceKind Project(
        Query.CompiledDocumentationSourceEvidenceKind value) =>
        value switch
        {
            Query.CompiledDocumentationSourceEvidenceKind.Candidate =>
                Wire.CompiledDocumentationSourceEvidenceKind.Candidate,
            Query.CompiledDocumentationSourceEvidenceKind.Absent =>
                Wire.CompiledDocumentationSourceEvidenceKind.Absent,
            Query.CompiledDocumentationSourceEvidenceKind.Partial =>
                Wire.CompiledDocumentationSourceEvidenceKind.Partial,
            Query.CompiledDocumentationSourceEvidenceKind.Unavailable =>
                Wire.CompiledDocumentationSourceEvidenceKind.Unavailable,
            _ => throw new InvalidOperationException(
                "Unknown compiled documentation source evidence kind."),
        };

    private static Wire.CompiledDocumentationSourceRejectionKind Project(
        Query.CompiledDocumentationSourceRejectionKind value) =>
        value switch
        {
            Query.CompiledDocumentationSourceRejectionKind.SubjectMismatch =>
                Wire.CompiledDocumentationSourceRejectionKind.SubjectMismatch,
            Query.CompiledDocumentationSourceRejectionKind.LibraryMismatch =>
                Wire.CompiledDocumentationSourceRejectionKind.LibraryMismatch,
            Query.CompiledDocumentationSourceRejectionKind.ApiContentMismatch =>
                Wire.CompiledDocumentationSourceRejectionKind
                    .ApiContentMismatch,
            Query.CompiledDocumentationSourceRejectionKind.CompanionMismatch =>
                Wire.CompiledDocumentationSourceRejectionKind
                    .CompanionMismatch,
            _ => throw new InvalidOperationException(
                "Unknown compiled documentation source rejection kind."),
        };

    private static Wire.CompiledDocumentationIncompleteReason Project(
        Query.CompiledDocumentationIncompleteReason value) =>
        value switch
        {
            Query.CompiledDocumentationIncompleteReason.Deadline =>
                Wire.CompiledDocumentationIncompleteReason.Deadline,
            Query.CompiledDocumentationIncompleteReason.ContributionLimit =>
                Wire.CompiledDocumentationIncompleteReason.ContributionLimit,
            Query.CompiledDocumentationIncompleteReason
                .CompanionSelectionPartial =>
                    Wire.CompiledDocumentationIncompleteReason
                        .CompanionSelectionPartial,
            Query.CompiledDocumentationIncompleteReason
                .CompiledXmlByteLimit =>
                    Wire.CompiledDocumentationIncompleteReason
                        .CompiledXmlByteLimit,
            _ => throw new InvalidOperationException(
                "Unknown compiled documentation incomplete reason."),
        };

    private static Wire.CompiledDocumentationRequestRejectionKind Project(
        Query.CompiledDocumentationRequestRejectionKind value) =>
        value switch
        {
            Query.CompiledDocumentationRequestRejectionKind
                .LibraryReferenceMismatch =>
                    Wire.CompiledDocumentationRequestRejectionKind
                        .LibraryReferenceMismatch,
            Query.CompiledDocumentationRequestRejectionKind
                .ApiContentMismatch =>
                    Wire.CompiledDocumentationRequestRejectionKind
                        .ApiContentMismatch,
            Query.CompiledDocumentationRequestRejectionKind
                .LeaseReferenceMismatch =>
                    Wire.CompiledDocumentationRequestRejectionKind
                        .LeaseReferenceMismatch,
            _ => throw new InvalidOperationException(
                "Unknown compiled documentation request rejection kind."),
        };

    private static Wire.DocumentationQueryRequestRejectionReason Project(
        Query.DocumentationQueryRequestRejectionReason value) =>
        value switch
        {
            Query.DocumentationQueryRequestRejectionReason
                .LibraryReferenceMismatch =>
                    Wire.DocumentationQueryRequestRejectionReason
                        .LibraryReferenceMismatch,
            Query.DocumentationQueryRequestRejectionReason
                .ApiContentMismatch =>
                    Wire.DocumentationQueryRequestRejectionReason
                        .ApiContentMismatch,
            Query.DocumentationQueryRequestRejectionReason
                .LeaseReferenceMismatch =>
                    Wire.DocumentationQueryRequestRejectionReason
                        .LeaseReferenceMismatch,
            Query.DocumentationQueryRequestRejectionReason
                .AuthoredSourceBindingMismatch =>
                    Wire.DocumentationQueryRequestRejectionReason
                        .AuthoredSourceBindingMismatch,
            _ => throw new InvalidOperationException(
                "Unknown documentation request rejection reason."),
        };

    private static Wire.DocumentationQueryFailureReason Project(
        Query.DocumentationQueryFailureReason value) =>
        value switch
        {
            Query.DocumentationQueryFailureReason
                .CompiledXmlMalformedOrUnreadableDocument =>
                    Wire.DocumentationQueryFailureReason
                        .CompiledXmlMalformedOrUnreadableDocument,
            Query.DocumentationQueryFailureReason
                .CompiledXmlContentAccessFailed =>
                    Wire.DocumentationQueryFailureReason
                        .CompiledXmlContentAccessFailed,
            _ => throw new InvalidOperationException(
                "Unknown documentation query failure reason."),
        };

    private static Wire.AuthoredDocumentationUnavailableReason Project(
        Query.AuthoredDocumentationUnavailableReason value) =>
        value switch
        {
            Query.AuthoredDocumentationUnavailableReason
                .OperationUnavailable =>
                    Wire.AuthoredDocumentationUnavailableReason
                        .OperationUnavailable,
            Query.AuthoredDocumentationUnavailableReason.SourceUnavailable =>
                Wire.AuthoredDocumentationUnavailableReason.SourceUnavailable,
            Query.AuthoredDocumentationUnavailableReason
                .DeclarationNotFound =>
                    Wire.AuthoredDocumentationUnavailableReason
                        .DeclarationNotFound,
            _ => throw new InvalidOperationException(
                "Unknown authored documentation unavailable reason."),
        };

    private static Wire.AuthoredDocumentationAmbiguityReason Project(
        Query.AuthoredDocumentationAmbiguityReason value) =>
        value switch
        {
            Query.AuthoredDocumentationAmbiguityReason
                .DeclarationAmbiguous =>
                    Wire.AuthoredDocumentationAmbiguityReason
                        .DeclarationAmbiguous,
            _ => throw new InvalidOperationException(
                "Unknown authored documentation ambiguity reason."),
        };

    private static Wire.AuthoredDocumentationRejectionReason Project(
        Query.AuthoredDocumentationRejectionReason value) =>
        value switch
        {
            Query.AuthoredDocumentationRejectionReason
                .OperationEvidenceMismatch =>
                    Wire.AuthoredDocumentationRejectionReason
                        .OperationEvidenceMismatch,
            Query.AuthoredDocumentationRejectionReason.AlreadyInvoked =>
                Wire.AuthoredDocumentationRejectionReason.AlreadyInvoked,
            Query.AuthoredDocumentationRejectionReason.BindingMismatch =>
                Wire.AuthoredDocumentationRejectionReason.BindingMismatch,
            Query.AuthoredDocumentationRejectionReason
                .LeaseReferenceMismatch =>
                    Wire.AuthoredDocumentationRejectionReason
                        .LeaseReferenceMismatch,
            Query.AuthoredDocumentationRejectionReason.SourceRejected =>
                Wire.AuthoredDocumentationRejectionReason.SourceRejected,
            Query.AuthoredDocumentationRejectionReason
                .SourceEvidenceMismatch =>
                    Wire.AuthoredDocumentationRejectionReason
                        .SourceEvidenceMismatch,
            _ => throw new InvalidOperationException(
                "Unknown authored documentation rejection reason."),
        };

    private static Wire.AuthoredDocumentationFailureReason Project(
        Query.AuthoredDocumentationFailureReason value) =>
        value switch
        {
            Query.AuthoredDocumentationFailureReason.SourceFailed =>
                Wire.AuthoredDocumentationFailureReason.SourceFailed,
            Query.AuthoredDocumentationFailureReason.MalformedDocumentation =>
                Wire.AuthoredDocumentationFailureReason
                    .MalformedDocumentation,
            _ => throw new InvalidOperationException(
                "Unknown authored documentation failure reason."),
        };

    private static Wire.AuthoredDocumentationIncompleteReason Project(
        Query.AuthoredDocumentationIncompleteReason value) =>
        value switch
        {
            Query.AuthoredDocumentationIncompleteReason
                .DeclarationUncertain =>
                    Wire.AuthoredDocumentationIncompleteReason
                        .DeclarationUncertain,
            Query.AuthoredDocumentationIncompleteReason
                .ImplementationSurface =>
                    Wire.AuthoredDocumentationIncompleteReason
                        .ImplementationSurface,
            Query.AuthoredDocumentationIncompleteReason.Deadline =>
                Wire.AuthoredDocumentationIncompleteReason.Deadline,
            Query.AuthoredDocumentationIncompleteReason.SourceDocuments =>
                Wire.AuthoredDocumentationIncompleteReason.SourceDocuments,
            Query.AuthoredDocumentationIncompleteReason.SourceBytes =>
                Wire.AuthoredDocumentationIncompleteReason.SourceBytes,
            Query.AuthoredDocumentationIncompleteReason.SourceCharacters =>
                Wire.AuthoredDocumentationIncompleteReason.SourceCharacters,
            Query.AuthoredDocumentationIncompleteReason.SourceHouse =>
                Wire.AuthoredDocumentationIncompleteReason.SourceHouse,
            Query.AuthoredDocumentationIncompleteReason.Documentation =>
                Wire.AuthoredDocumentationIncompleteReason.Documentation,
            _ => throw new InvalidOperationException(
                "Unknown authored documentation incomplete reason."),
        };
}
