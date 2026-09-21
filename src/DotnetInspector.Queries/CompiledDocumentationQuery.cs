using System.Collections.Immutable;
using System.Text.Json.Serialization;

using DotnetInspector.DocumentationHouse;
using DotnetInspector.Libraries;

namespace DotnetInspector.Queries;

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
    ImmutableArray<CompiledDocumentationParameter> Parameters,
    ImmutableArray<CompiledDocumentationException> Exceptions,
    ImmutableArray<CompiledDocumentationSample> Samples);

/// <summary>
/// Queries-owned portable terminal outcome for one compiled-documentation
/// request. It contains no Library or Artifact authority.
/// </summary>
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
        ImmutableArray<CompiledDocumentationSourceEvidence> Sources,
        [property: JsonIgnore(
            Condition = JsonIgnoreCondition.WhenWritingDefault)]
        bool SourcesTruncated)
        : CompiledDocumentationOutcome(Subject);

    public sealed record Unavailable(
        CompiledDocumentationSubject Subject,
        ImmutableArray<CompiledDocumentationSourceEvidence> Sources,
        [property: JsonIgnore(
            Condition = JsonIgnoreCondition.WhenWritingDefault)]
        bool SourcesTruncated)
        : CompiledDocumentationOutcome(Subject);

    public sealed record Ambiguous(
        CompiledDocumentationSubject Subject,
        ImmutableArray<CompiledDocumentationSource> Candidates,
        [property: JsonIgnore(
            Condition = JsonIgnoreCondition.WhenWritingDefault)]
        bool CandidatesTruncated)
        : CompiledDocumentationOutcome(Subject);

    public sealed record ContributionsRejected(
        CompiledDocumentationSubject Subject,
        ImmutableArray<CompiledDocumentationSourceRejection> Rejections,
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
        ImmutableArray<CompiledDocumentationSourceEvidence> Sources,
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

/// <summary>
/// Exact in-process settlement plus the producer-owned content used for
/// serialization. Consumers serialize <see cref="Content"/>, not
/// <see cref="Outcome"/>.
/// </summary>
public sealed class CompiledDocumentationQueryResult
{
    internal CompiledDocumentationQueryResult(
        DocumentationHouseOutcome outcome,
        CompiledDocumentationOutcome content)
    {
        Outcome = outcome;
        Content = content;
    }

    [JsonIgnore]
    public DocumentationHouseOutcome Outcome { get; }

    public CompiledDocumentationOutcome Content { get; }
}

/// <summary>
/// Executes one already-authorized DocumentationHouse request and publishes
/// settled portable content before returning control to a consumer.
/// </summary>
public static class CompiledDocumentationQuery
{
    private const int MaximumPublishedSourceEvidence = 8;

    public static async ValueTask<CompiledDocumentationQueryResult>
        ExecuteAsync(
            DocumentationHouseRequest request,
            LibraryOperationLease operationLease,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(operationLease);

        DocumentationHouseOutcome outcome =
            await DocumentationHouse.DocumentationHouse.ExecuteAsync(
                    request,
                    operationLease,
                    cancellationToken)
                .ConfigureAwait(false);
        return new(outcome, Content(outcome));
    }

    public static async ValueTask<
        IReadOnlyList<CompiledDocumentationQueryResult>> ExecuteManyAsync(
            IReadOnlyList<DocumentationHouseRequest> requests,
            LibraryOperationLease operationLease,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(requests);
        ArgumentNullException.ThrowIfNull(operationLease);
        IReadOnlyList<DocumentationHouseOutcome> outcomes =
            await DocumentationHouse.DocumentationHouse.ExecuteManyAsync(
                    requests,
                    operationLease,
                    cancellationToken)
                .ConfigureAwait(false);
        return
        [
            .. outcomes.Select(
                static outcome =>
                    new CompiledDocumentationQueryResult(
                        outcome,
                        Content(outcome))),
        ];
    }

    private static CompiledDocumentationOutcome Content(
        DocumentationHouseOutcome outcome)
    {
        CompiledDocumentationSubject subject =
            Snapshot(outcome.Request.Subject);
        return outcome switch
        {
            DocumentationHouseOutcome.Completed completed =>
                Content(
                    subject,
                    completed.CompiledXmlAttempt
                        ?? throw new InvalidOperationException(
                            "Compiled documentation settlement did not return a compiled-XML attempt.")),
            DocumentationHouseOutcome.Rejected rejected =>
                new CompiledDocumentationOutcome.RequestRejected(
                    subject,
                    Snapshot(rejected.Rejection.Kind)),
            DocumentationHouseOutcome.Failed failed =>
                ContentAccessFailed(subject, failed.Failure),
            DocumentationHouseOutcome.Incomplete incomplete =>
                Incomplete(
                    subject,
                    incomplete.Boundary,
                    outcome.Request.Plan.CompiledXmlContributions
                        .Take(outcome.Work.ContributionsObserved)),
            _ => throw new InvalidOperationException(
                "Unknown DocumentationHouse outcome."),
        };
    }

    internal static CompiledDocumentationSubject ProjectSubject(
        DocumentationSubjectReference subject) =>
        Snapshot(subject);

    internal static CompiledDocumentationOutcome ProjectAttempt(
        CompiledDocumentationSubject subject,
        DocumentationCompiledXmlAttempt attempt) =>
        Content(subject, attempt);

    internal static CompiledDocumentationSource ProjectSource(
        CompiledXmlContribution contribution) =>
        Snapshot(contribution);

    internal static CompiledDocumentationIncompleteReason
        ProjectIncompleteReason(
            DocumentationIncompleteBoundary boundary) =>
        Snapshot(boundary);

    private static CompiledDocumentationOutcome Content(
        CompiledDocumentationSubject subject,
        DocumentationCompiledXmlAttempt attempt) =>
        attempt switch
        {
            DocumentationCompiledXmlAttempt.Available available =>
                new CompiledDocumentationOutcome.Available(
                    subject,
                    Snapshot(available.Selected),
                    Snapshot(available.Documentation)),
            DocumentationCompiledXmlAttempt.Absent absent =>
                Absent(subject, absent),
            DocumentationCompiledXmlAttempt.Unavailable unavailable =>
                Unavailable(subject, unavailable.Contributions),
            DocumentationCompiledXmlAttempt.Ambiguous ambiguous =>
                Ambiguous(subject, ambiguous.Candidates),
            DocumentationCompiledXmlAttempt.Rejected rejected =>
                ContributionsRejected(subject, rejected.Rejections),
            DocumentationCompiledXmlAttempt.Failed failed =>
                MalformedOrUnreadableDocument(subject, failed),
            DocumentationCompiledXmlAttempt.Incomplete incomplete =>
                Incomplete(
                    subject,
                    incomplete.Boundary,
                    incomplete.Contributions,
                    incomplete.Selected),
            _ => throw new InvalidOperationException(
                "Unknown compiled-XML attempt."),
        };

    private static CompiledDocumentationOutcome.ContentAccessFailed
        ContentAccessFailed(
            CompiledDocumentationSubject subject,
            DocumentationHouseFailure failure) =>
        failure.Kind switch
        {
            DocumentationCompiledXmlFailureKind.ContentAccessFailed =>
                new(subject, Snapshot(failure.Selected)),
            _ => throw new InvalidOperationException(
                "Unexpected top-level compiled-XML failure kind."),
        };

    private static CompiledDocumentationOutcome.MalformedOrUnreadableDocument
        MalformedOrUnreadableDocument(
            CompiledDocumentationSubject subject,
            DocumentationCompiledXmlAttempt.Failed failed) =>
        failed.Failure.Kind switch
        {
            DocumentationCompiledXmlFailureKind
                .MalformedOrUnreadableDocument =>
                new(subject, Snapshot(failed.Selected)),
            _ => throw new InvalidOperationException(
                "Unexpected contribution failure kind."),
        };

    private static CompiledDocumentationOutcome.Absent Absent(
        CompiledDocumentationSubject subject,
        DocumentationCompiledXmlAttempt.Absent absent)
    {
        (ImmutableArray<CompiledDocumentationSourceEvidence> sources,
            bool truncated) =
            absent.Selected is { } selected
                ? BoundedSourceEvidence([selected])
                : BoundedSourceEvidence(
                    absent.Contributions,
                    absent.Contributions.Where(
                        static contribution =>
                            contribution.Kind
                                == CompiledXmlContributionKind.Absent));
        return new(subject, sources, truncated);
    }

    private static CompiledDocumentationOutcome.Unavailable Unavailable(
        CompiledDocumentationSubject subject,
        IEnumerable<CompiledXmlContribution> contributions)
    {
        (ImmutableArray<CompiledDocumentationSourceEvidence> sources,
            bool truncated) =
            BoundedSourceEvidence(contributions);
        return new(subject, sources, truncated);
    }

    private static CompiledDocumentationOutcome.Ambiguous Ambiguous(
        CompiledDocumentationSubject subject,
        IEnumerable<CompiledXmlContribution> candidates)
    {
        (ImmutableArray<CompiledDocumentationSource> sources,
            bool truncated) =
            TakeDistinct(candidates.Select(Snapshot));
        return new(subject, sources, truncated);
    }

    private static CompiledDocumentationOutcome.ContributionsRejected
        ContributionsRejected(
            CompiledDocumentationSubject subject,
            IEnumerable<DocumentationCompiledXmlRejection> rejections)
    {
        (ImmutableArray<CompiledDocumentationSourceRejection> snapshots,
            bool truncated) =
            TakeDistinct(
                rejections.Select(
                    static rejection =>
                        new CompiledDocumentationSourceRejection(
                            Snapshot(rejection.Contribution),
                            Snapshot(rejection.Kind))));
        return new(subject, snapshots, truncated);
    }

    private static CompiledDocumentationOutcome.Incomplete Incomplete(
        CompiledDocumentationSubject subject,
        DocumentationIncompleteBoundary reason,
        IEnumerable<CompiledXmlContribution> contributions,
        CompiledXmlContribution? selected = null)
    {
        IEnumerable<CompiledXmlContribution>? decisiveContributions =
            selected is not null
                ? [selected]
                : reason
                    == DocumentationIncompleteBoundary
                        .CompanionSelectionPartial
                            ? contributions.Where(
                                static contribution =>
                                    contribution.Kind
                                        == CompiledXmlContributionKind.Partial)
                            : null;
        (ImmutableArray<CompiledDocumentationSourceEvidence> sources,
            bool truncated) =
            BoundedSourceEvidence(
                contributions,
                decisiveContributions);
        return new(subject, Snapshot(reason), sources, truncated);
    }

    private static (
        ImmutableArray<CompiledDocumentationSourceEvidence> Sources,
        bool Truncated)
        BoundedSourceEvidence(
            IEnumerable<CompiledXmlContribution> contributions,
            IEnumerable<CompiledXmlContribution>?
                decisiveContributions = null)
    {
        IEnumerable<CompiledXmlContribution> orderedContributions =
            decisiveContributions is null
                ? contributions
                : decisiveContributions.Concat(contributions);
        return TakeDistinct(
            orderedContributions.Select(SnapshotEvidence));
    }

    private static (
        ImmutableArray<T> Items,
        bool Truncated)
        TakeDistinct<T>(IEnumerable<T> values)
        where T : notnull
    {
        var seen = new HashSet<T>();
        var items =
            ImmutableArray.CreateBuilder<T>(
                MaximumPublishedSourceEvidence);
        foreach (T value in values)
        {
            if (!seen.Add(value))
                continue;
            if (items.Count == MaximumPublishedSourceEvidence)
                return (items.ToImmutable(), true);

            items.Add(value);
        }

        return (items.ToImmutable(), false);
    }

    private static CompiledDocumentationSubject Snapshot(
        DocumentationSubjectReference subject) =>
        new(
            new(
                subject.MetadataAssembly.Name,
                subject.MetadataAssembly.Version?.ToString(),
                subject.MetadataAssembly.Culture,
                subject.MetadataAssembly.PublicKeyToken),
            subject.CompiledXmlIdentity.Value);

    private static CompiledDocumentationSource Snapshot(
        CompiledXmlContribution contribution) =>
        new(
            Snapshot(contribution.Source.Kind),
            contribution.Source.Name,
            contribution.Precedence);

    private static CompiledDocumentationSourceEvidence SnapshotEvidence(
        CompiledXmlContribution contribution) =>
        new(
            Snapshot(contribution),
            Snapshot(contribution.Kind));

    private static CompiledDocumentationEntry Snapshot(
        CSharpText.XmlDocumentationEntry documentation) =>
        DocumentationQueryProjection.Snapshot(documentation);

    private static CompiledDocumentationSourceKind Snapshot(
        DocumentationSourceKind kind) =>
        kind switch
        {
            DocumentationSourceKind.Package =>
                CompiledDocumentationSourceKind.Package,
            DocumentationSourceKind.Platform =>
                CompiledDocumentationSourceKind.Platform,
            DocumentationSourceKind.DirectLibrary =>
                CompiledDocumentationSourceKind.DirectLibrary,
            DocumentationSourceKind.SourceHouse =>
                CompiledDocumentationSourceKind.SourceHouse,
            _ => throw new InvalidOperationException(
                "Unknown documentation source kind."),
        };

    private static CompiledDocumentationSourceEvidenceKind Snapshot(
        CompiledXmlContributionKind kind) =>
        kind switch
        {
            CompiledXmlContributionKind.Candidate =>
                CompiledDocumentationSourceEvidenceKind.Candidate,
            CompiledXmlContributionKind.Absent =>
                CompiledDocumentationSourceEvidenceKind.Absent,
            CompiledXmlContributionKind.Partial =>
                CompiledDocumentationSourceEvidenceKind.Partial,
            CompiledXmlContributionKind.Unavailable =>
                CompiledDocumentationSourceEvidenceKind.Unavailable,
            _ => throw new InvalidOperationException(
                "Unknown compiled-XML contribution kind."),
        };

    private static CompiledDocumentationSourceRejectionKind Snapshot(
        DocumentationCompiledXmlRejectionKind kind) =>
        kind switch
        {
            DocumentationCompiledXmlRejectionKind.SubjectMismatch =>
                CompiledDocumentationSourceRejectionKind.SubjectMismatch,
            DocumentationCompiledXmlRejectionKind.LibraryMismatch =>
                CompiledDocumentationSourceRejectionKind.LibraryMismatch,
            DocumentationCompiledXmlRejectionKind.ApiContentMismatch =>
                CompiledDocumentationSourceRejectionKind.ApiContentMismatch,
            DocumentationCompiledXmlRejectionKind.CompanionMismatch =>
                CompiledDocumentationSourceRejectionKind.CompanionMismatch,
            _ => throw new InvalidOperationException(
                "Unknown compiled-XML rejection kind."),
        };

    private static CompiledDocumentationRequestRejectionKind Snapshot(
        DocumentationHouseRejectionKind kind) =>
        kind switch
        {
            DocumentationHouseRejectionKind.LibraryReferenceMismatch =>
                CompiledDocumentationRequestRejectionKind
                    .LibraryReferenceMismatch,
            DocumentationHouseRejectionKind.ApiContentMismatch =>
                CompiledDocumentationRequestRejectionKind.ApiContentMismatch,
            DocumentationHouseRejectionKind.LeaseReferenceMismatch =>
                CompiledDocumentationRequestRejectionKind
                    .LeaseReferenceMismatch,
            _ => throw new InvalidOperationException(
                "Unknown DocumentationHouse rejection kind."),
        };

    private static CompiledDocumentationIncompleteReason Snapshot(
        DocumentationIncompleteBoundary boundary) =>
        boundary switch
        {
            DocumentationIncompleteBoundary.Deadline =>
                CompiledDocumentationIncompleteReason.Deadline,
            DocumentationIncompleteBoundary.ContributionLimit =>
                CompiledDocumentationIncompleteReason.ContributionLimit,
            DocumentationIncompleteBoundary.CompanionSelectionPartial =>
                CompiledDocumentationIncompleteReason
                    .CompanionSelectionPartial,
            DocumentationIncompleteBoundary.CompiledXmlByteLimit =>
                CompiledDocumentationIncompleteReason.CompiledXmlByteLimit,
            _ => throw new InvalidOperationException(
                "Unknown DocumentationHouse incomplete boundary."),
        };
}

/// <summary>
/// Source-generated JSON contract for the Queries-owned portable outcome.
/// The exact in-process DocumentationHouse outcome is intentionally excluded.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(CompiledDocumentationOutcome))]
public partial class CompiledDocumentationQueryJsonContext :
    JsonSerializerContext;
