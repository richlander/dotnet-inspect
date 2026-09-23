using System.Collections.Immutable;
using System.Text.Json.Serialization;

using CSharpText;
using DotnetInspector.DocumentationHouse;

namespace DotnetInspector.Queries;

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
    ImmutableArray<DocumentationQueryChannel> RequestedChannels,
    ImmutableArray<DocumentationQueryTextFieldContribution> Contributions);

public sealed record DocumentationQueryExceptionFieldContribution(
    DocumentationQueryChannel Channel,
    ImmutableArray<CompiledDocumentationException> Value);

public sealed record DocumentationQueryExceptionFieldEvidence(
    DocumentationQueryFieldEvidenceKind Kind,
    ImmutableArray<DocumentationQueryChannel> RequestedChannels,
    ImmutableArray<DocumentationQueryExceptionFieldContribution>
        Contributions);

public sealed record DocumentationQuerySampleFieldContribution(
    DocumentationQueryChannel Channel,
    ImmutableArray<CompiledDocumentationSample> Value);

public sealed record DocumentationQuerySampleFieldEvidence(
    DocumentationQueryFieldEvidenceKind Kind,
    ImmutableArray<DocumentationQueryChannel> RequestedChannels,
    ImmutableArray<DocumentationQuerySampleFieldContribution>
        Contributions);

public sealed record DocumentationQueryParameterField(
    string Name,
    DocumentationQueryTextFieldEvidence Evidence);

public sealed record DocumentationQueryFieldSettlement(
    DocumentationQueryTextFieldEvidence Summary,
    DocumentationQueryTextFieldEvidence Remarks,
    DocumentationQueryTextFieldEvidence Returns,
    ImmutableArray<DocumentationQueryParameterField> Parameters,
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

/// <summary>
/// Queries-owned portable terminal evidence for one authored-source channel
/// attempt.
/// </summary>
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

/// <summary>
/// Queries-owned portable terminal outcome for one unified documentation
/// request.
/// </summary>
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

/// <summary>
/// Exact in-process settlement plus the resource-free structural request and
/// Queries-owned portable content.
/// </summary>
public sealed class DocumentationQueryResult
{
    internal DocumentationQueryResult(
        QuerySpace.Composition.QuerySpaceRequest request,
        DocumentationHouseOutcome outcome,
        DocumentationQueryOutcome content)
    {
        Request = request;
        Outcome = outcome;
        Content = content;
    }

    [JsonIgnore]
    public QuerySpace.Composition.QuerySpaceRequest Request { get; }

    [JsonIgnore]
    public DocumentationHouseOutcome Outcome { get; }

    public DocumentationQueryOutcome Content { get; }
}

/// <summary>
/// Source-generated JSON contract for the Queries-owned unified portable
/// outcome.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    UseStringEnumConverter = true)]
[JsonSerializable(
    typeof(DocumentationQueryOutcome),
    TypeInfoPropertyName = "DocumentationQueryOutcome")]
[JsonSerializable(
    typeof(DocumentationQueryOutcome.Completed),
    TypeInfoPropertyName = "DocumentationQueryCompleted")]
[JsonSerializable(
    typeof(DocumentationQueryOutcome.RequestRejected),
    TypeInfoPropertyName = "DocumentationQueryRequestRejected")]
[JsonSerializable(
    typeof(DocumentationQueryOutcome.Failed),
    TypeInfoPropertyName = "DocumentationQueryFailed")]
[JsonSerializable(
    typeof(DocumentationQueryOutcome.Incomplete),
    TypeInfoPropertyName = "DocumentationQueryIncomplete")]
[JsonSerializable(
    typeof(AuthoredDocumentationOutcome.Available),
    TypeInfoPropertyName = "AuthoredDocumentationAvailable")]
[JsonSerializable(
    typeof(AuthoredDocumentationOutcome.Absent),
    TypeInfoPropertyName = "AuthoredDocumentationAbsent")]
[JsonSerializable(
    typeof(AuthoredDocumentationOutcome.Unavailable),
    TypeInfoPropertyName = "AuthoredDocumentationUnavailable")]
[JsonSerializable(
    typeof(AuthoredDocumentationOutcome.Ambiguous),
    TypeInfoPropertyName = "AuthoredDocumentationAmbiguous")]
[JsonSerializable(
    typeof(AuthoredDocumentationOutcome.Rejected),
    TypeInfoPropertyName = "AuthoredDocumentationRejected")]
[JsonSerializable(
    typeof(AuthoredDocumentationOutcome.Failed),
    TypeInfoPropertyName = "AuthoredDocumentationFailed")]
[JsonSerializable(
    typeof(AuthoredDocumentationOutcome.Incomplete),
    TypeInfoPropertyName = "AuthoredDocumentationIncomplete")]
public partial class DocumentationQueryJsonContext :
    JsonSerializerContext;

internal static class DocumentationQueryProjection
{
    internal static CompiledDocumentationEntry Snapshot(
        XmlDocumentationEntry documentation) =>
        new(
            documentation.Summary,
            documentation.Remarks,
            documentation.Returns,
            [.. documentation.Parameters.Select(
                static parameter => new CompiledDocumentationParameter(
                    parameter.Key,
                    parameter.Value))],
            [.. documentation.Exceptions.Select(
                static exception => new CompiledDocumentationException(
                    exception.Cref,
                    exception.Description))],
            [.. documentation.Samples.Select(
                static sample => new CompiledDocumentationSample(
                    sample.Source,
                    sample.Title,
                    sample.Region))]);
}
