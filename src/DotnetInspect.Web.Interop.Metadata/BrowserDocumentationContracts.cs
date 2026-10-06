using System.Text.Json.Serialization;

namespace DotnetInspect.Web.Interop.Metadata.Wire;

public enum DocumentationQueryChannel
{
    CompiledXml,
    AuthoredSource,
}

public enum DocumentationQueryFieldEvidenceKind
{
    Selected,
    Corroborated,
    Conflict,
    Absent,
}

public sealed record DocumentationQueryTextFieldContribution(
    DocumentationQueryChannel Channel,
    string Value);

public sealed record DocumentationQueryTextFieldEvidence(
    DocumentationQueryFieldEvidenceKind Kind,
    DocumentationQueryChannel[] RequestedChannels,
    DocumentationQueryTextFieldContribution[] Contributions);

public sealed record DocumentationQueryExceptionFieldContribution(
    DocumentationQueryChannel Channel,
    CompiledDocumentationException[] Value);

public sealed record DocumentationQueryExceptionFieldEvidence(
    DocumentationQueryFieldEvidenceKind Kind,
    DocumentationQueryChannel[] RequestedChannels,
    DocumentationQueryExceptionFieldContribution[] Contributions);

public sealed record DocumentationQuerySampleFieldContribution(
    DocumentationQueryChannel Channel,
    CompiledDocumentationSample[] Value);

public sealed record DocumentationQuerySampleFieldEvidence(
    DocumentationQueryFieldEvidenceKind Kind,
    DocumentationQueryChannel[] RequestedChannels,
    DocumentationQuerySampleFieldContribution[] Contributions);

public sealed record DocumentationQueryParameterField(
    string Name,
    DocumentationQueryTextFieldEvidence Evidence);

public sealed record DocumentationQueryFieldSettlement(
    DocumentationQueryTextFieldEvidence Summary,
    DocumentationQueryTextFieldEvidence Remarks,
    DocumentationQueryTextFieldEvidence Returns,
    DocumentationQueryParameterField[] Parameters,
    DocumentationQueryExceptionFieldEvidence Exceptions,
    DocumentationQuerySampleFieldEvidence Samples);

public sealed record AuthoredDocumentationObservation(
    string Code,
    string? Detail,
    bool DetailWasTruncated);

public enum AuthoredDocumentationUnavailableReason
{
    OperationUnavailable,
    SourceUnavailable,
    DeclarationNotFound,
}

public enum AuthoredDocumentationAmbiguityReason
{
    DeclarationAmbiguous,
}

public enum AuthoredDocumentationRejectionReason
{
    OperationEvidenceMismatch,
    AlreadyInvoked,
    BindingMismatch,
    LeaseReferenceMismatch,
    SourceRejected,
    SourceEvidenceMismatch,
}

public enum AuthoredDocumentationFailureReason
{
    SourceFailed,
    MalformedDocumentation,
}

public enum AuthoredDocumentationIncompleteReason
{
    DeclarationUncertain,
    ImplementationSurface,
    Deadline,
    SourceDocuments,
    SourceBytes,
    SourceCharacters,
    SourceHouse,
    Documentation,
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(AuthoredDocumentationOutcome.Available), "available")]
[JsonDerivedType(typeof(AuthoredDocumentationOutcome.Absent), "absent")]
[JsonDerivedType(
    typeof(AuthoredDocumentationOutcome.Unavailable),
    "unavailable")]
[JsonDerivedType(
    typeof(AuthoredDocumentationOutcome.Ambiguous),
    "ambiguous")]
[JsonDerivedType(typeof(AuthoredDocumentationOutcome.Rejected), "rejected")]
[JsonDerivedType(typeof(AuthoredDocumentationOutcome.Failed), "failed")]
[JsonDerivedType(
    typeof(AuthoredDocumentationOutcome.Incomplete),
    "incomplete")]
public abstract record AuthoredDocumentationOutcome
{
    private AuthoredDocumentationOutcome()
    {
    }

    public sealed record Available(CompiledDocumentationEntry Documentation)
        : AuthoredDocumentationOutcome;

    public sealed record Absent : AuthoredDocumentationOutcome;

    public sealed record Unavailable(
        AuthoredDocumentationUnavailableReason Reason,
        AuthoredDocumentationObservation? Observation)
        : AuthoredDocumentationOutcome;

    public sealed record Ambiguous(
        AuthoredDocumentationAmbiguityReason Reason,
        AuthoredDocumentationObservation? Observation)
        : AuthoredDocumentationOutcome;

    public sealed record Rejected(
        AuthoredDocumentationRejectionReason Reason,
        AuthoredDocumentationObservation? Observation)
        : AuthoredDocumentationOutcome;

    public sealed record Failed(
        AuthoredDocumentationFailureReason Reason,
        AuthoredDocumentationObservation? Observation)
        : AuthoredDocumentationOutcome;

    public sealed record Incomplete(
        AuthoredDocumentationIncompleteReason Reason,
        AuthoredDocumentationObservation? Observation)
        : AuthoredDocumentationOutcome;
}

public enum DocumentationQueryRequestRejectionReason
{
    LibraryReferenceMismatch,
    ApiContentMismatch,
    LeaseReferenceMismatch,
    AuthoredSourceBindingMismatch,
}

public enum DocumentationQueryFailureReason
{
    CompiledXmlMalformedOrUnreadableDocument,
    CompiledXmlContentAccessFailed,
}

public enum CompiledDocumentationSourceKind
{
    Package,
    Platform,
    DirectLibrary,
    SourceHouse,
}

public enum CompiledDocumentationSourceEvidenceKind
{
    Candidate,
    Absent,
    Partial,
    Unavailable,
}

public enum CompiledDocumentationSourceRejectionKind
{
    SubjectMismatch,
    LibraryMismatch,
    ApiContentMismatch,
    CompanionMismatch,
}

public enum CompiledDocumentationRequestRejectionKind
{
    LibraryReferenceMismatch,
    ApiContentMismatch,
    LeaseReferenceMismatch,
}

public enum CompiledDocumentationIncompleteReason
{
    Deadline,
    ContributionLimit,
    CompanionSelectionPartial,
    CompiledXmlByteLimit,
}

public sealed record CompiledDocumentationAssemblyIdentity(
    string Name,
    string? Version,
    string? Culture,
    string? PublicKeyToken);

public sealed record CompiledDocumentationSubject(
    CompiledDocumentationAssemblyIdentity Assembly,
    string DocumentationId);

public sealed record CompiledDocumentationSource(
    CompiledDocumentationSourceKind Kind,
    string Name,
    int? Precedence);

public sealed record CompiledDocumentationSourceEvidence(
    CompiledDocumentationSource Source,
    CompiledDocumentationSourceEvidenceKind Kind);

public sealed record CompiledDocumentationSourceRejection(
    CompiledDocumentationSource Source,
    CompiledDocumentationSourceRejectionKind Reason);

public sealed record CompiledDocumentationParameter(
    string Name,
    string Description);

public sealed record CompiledDocumentationException(
    string? Reference,
    string? Description);

public sealed record CompiledDocumentationSample(
    string Code,
    string? Title,
    string? Region);

public sealed record CompiledDocumentationEntry(
    string? Summary,
    string? Remarks,
    string? Returns,
    CompiledDocumentationParameter[] Parameters,
    CompiledDocumentationException[] Exceptions,
    CompiledDocumentationSample[] Samples);

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(
    typeof(CompiledDocumentationOutcome.Available),
    "available")]
[JsonDerivedType(
    typeof(CompiledDocumentationOutcome.Absent),
    "absent")]
[JsonDerivedType(
    typeof(CompiledDocumentationOutcome.Unavailable),
    "unavailable")]
[JsonDerivedType(
    typeof(CompiledDocumentationOutcome.Ambiguous),
    "ambiguous")]
[JsonDerivedType(
    typeof(CompiledDocumentationOutcome.ContributionsRejected),
    "contributionsRejected")]
[JsonDerivedType(
    typeof(CompiledDocumentationOutcome.MalformedOrUnreadableDocument),
    "malformedOrUnreadableDocument")]
[JsonDerivedType(
    typeof(CompiledDocumentationOutcome.Incomplete),
    "incomplete")]
[JsonDerivedType(
    typeof(CompiledDocumentationOutcome.RequestRejected),
    "requestRejected")]
[JsonDerivedType(
    typeof(CompiledDocumentationOutcome.ContentAccessFailed),
    "contentAccessFailed")]
public abstract record CompiledDocumentationOutcome(
    CompiledDocumentationSubject Subject)
{
    public sealed record Available(
        CompiledDocumentationSubject Subject,
        CompiledDocumentationSource Source,
        CompiledDocumentationEntry Documentation)
        : CompiledDocumentationOutcome(Subject);

    public sealed record Absent(
        CompiledDocumentationSubject Subject,
        CompiledDocumentationSourceEvidence[] Sources,
        [property: JsonIgnore(
            Condition = JsonIgnoreCondition.WhenWritingDefault)]
        bool SourcesTruncated)
        : CompiledDocumentationOutcome(Subject);

    public sealed record Unavailable(
        CompiledDocumentationSubject Subject,
        CompiledDocumentationSourceEvidence[] Sources,
        [property: JsonIgnore(
            Condition = JsonIgnoreCondition.WhenWritingDefault)]
        bool SourcesTruncated)
        : CompiledDocumentationOutcome(Subject);

    public sealed record Ambiguous(
        CompiledDocumentationSubject Subject,
        CompiledDocumentationSource[] Candidates,
        [property: JsonIgnore(
            Condition = JsonIgnoreCondition.WhenWritingDefault)]
        bool CandidatesTruncated)
        : CompiledDocumentationOutcome(Subject);

    public sealed record ContributionsRejected(
        CompiledDocumentationSubject Subject,
        CompiledDocumentationSourceRejection[] Rejections,
        [property: JsonIgnore(
            Condition = JsonIgnoreCondition.WhenWritingDefault)]
        bool RejectionsTruncated)
        : CompiledDocumentationOutcome(Subject);

    public sealed record MalformedOrUnreadableDocument(
        CompiledDocumentationSubject Subject,
        CompiledDocumentationSource Source)
        : CompiledDocumentationOutcome(Subject);

    public sealed record Incomplete(
        CompiledDocumentationSubject Subject,
        CompiledDocumentationIncompleteReason Reason,
        CompiledDocumentationSourceEvidence[] Sources,
        [property: JsonIgnore(
            Condition = JsonIgnoreCondition.WhenWritingDefault)]
        bool SourcesTruncated)
        : CompiledDocumentationOutcome(Subject);

    public sealed record RequestRejected(
        CompiledDocumentationSubject Subject,
        CompiledDocumentationRequestRejectionKind Reason)
        : CompiledDocumentationOutcome(Subject);

    public sealed record ContentAccessFailed(
        CompiledDocumentationSubject Subject,
        CompiledDocumentationSource Source)
        : CompiledDocumentationOutcome(Subject);
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(DocumentationQueryOutcome.Completed), "completed")]
[JsonDerivedType(
    typeof(DocumentationQueryOutcome.RequestRejected),
    "requestRejected")]
[JsonDerivedType(typeof(DocumentationQueryOutcome.Failed), "failed")]
[JsonDerivedType(typeof(DocumentationQueryOutcome.Incomplete), "incomplete")]
public abstract record DocumentationQueryOutcome(
    CompiledDocumentationSubject Subject)
{
    public sealed record Completed(
        CompiledDocumentationSubject Subject,
        CompiledDocumentationOutcome? CompiledXml,
        AuthoredDocumentationOutcome? AuthoredSource,
        DocumentationQueryFieldSettlement Fields)
        : DocumentationQueryOutcome(Subject);

    public sealed record RequestRejected(
        CompiledDocumentationSubject Subject,
        DocumentationQueryRequestRejectionReason Reason)
        : DocumentationQueryOutcome(Subject);

    public sealed record Failed(
        CompiledDocumentationSubject Subject,
        DocumentationQueryFailureReason Reason,
        CompiledDocumentationSource Source)
        : DocumentationQueryOutcome(Subject);

    public sealed record Incomplete(
        CompiledDocumentationSubject Subject,
        CompiledDocumentationIncompleteReason Reason)
        : DocumentationQueryOutcome(Subject);
}
