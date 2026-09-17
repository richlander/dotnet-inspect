using System.Collections.Immutable;
using System.Text.Json.Serialization;

using DotnetInspector.DocumentationHouse;
using DotnetInspector.Libraries;

namespace DotnetInspector.Queries;

public enum CompiledDocumentationQueryOutcomeKind
{
    Completed,
    Rejected,
    Failed,
    Incomplete,
}

public sealed record CompiledDocumentationAssemblySnapshot(
    string Name,
    string? Version,
    string? Culture,
    string? PublicKeyToken);

public sealed record CompiledDocumentationTypeSnapshot(
    string Namespace,
    ImmutableArray<string> Segments);

public sealed record CompiledDocumentationMemberSnapshot(
    string StableSelector,
    string CanonicalSignature,
    string Fingerprint,
    string TypeFullName,
    string MemberName);

public sealed record CompiledDocumentationSubjectSnapshot(
    CompiledDocumentationAssemblySnapshot Assembly,
    CompiledDocumentationTypeSnapshot Type,
    CompiledDocumentationMemberSnapshot? Member,
    string CompiledXmlIdentity);

public sealed record CompiledDocumentationContributionSnapshot(
    CompiledXmlContributionKind Kind,
    DocumentationSourceKind SourceKind,
    string Source,
    int? Precedence);

public sealed record CompiledDocumentationRejectionSnapshot(
    CompiledDocumentationContributionSnapshot Contribution,
    DocumentationCompiledXmlRejectionKind Kind);

public sealed record CompiledDocumentationParameterSnapshot(
    string Name,
    string Description);

public sealed record CompiledDocumentationExceptionSnapshot(
    string? Cref,
    string? Description);

public sealed record CompiledDocumentationSampleSnapshot(
    string Source,
    string? Title,
    string? Region);

public sealed record CompiledDocumentationEntrySnapshot(
    string? Summary,
    string? Remarks,
    string? Returns,
    ImmutableArray<CompiledDocumentationParameterSnapshot> Parameters,
    ImmutableArray<CompiledDocumentationExceptionSnapshot> Exceptions,
    ImmutableArray<CompiledDocumentationSampleSnapshot> Samples);

public sealed record CompiledDocumentationAttemptSnapshot(
    DocumentationCompiledXmlAttemptKind Kind,
    CompiledDocumentationContributionSnapshot? Selected,
    ImmutableArray<CompiledDocumentationContributionSnapshot> Candidates,
    ImmutableArray<CompiledDocumentationContributionSnapshot> Contributions,
    ImmutableArray<CompiledDocumentationRejectionSnapshot> Rejections,
    CompiledDocumentationEntrySnapshot? Documentation,
    DocumentationCompiledXmlFailureKind? Failure,
    DocumentationIncompleteBoundary? IncompleteBoundary);

public sealed record CompiledDocumentationHouseFailureSnapshot(
    DocumentationHouseFailureStage Stage,
    DocumentationCompiledXmlFailureKind Kind);

public sealed record CompiledDocumentationWorkSnapshot(
    int ContributionsObserved,
    long CompiledXmlBytesObserved,
    bool ParsedCompiledXml);

/// <summary>
/// Queries-owned portable snapshot of one completed DocumentationHouse
/// operation. It contains no Library or Artifact authority.
/// </summary>
public sealed record CompiledDocumentationQuerySnapshot(
    string Request,
    string OperationPlan,
    string PolicyGeneration,
    DocumentationDemand Demand,
    CompiledDocumentationSubjectSnapshot Subject,
    CompiledDocumentationQueryOutcomeKind Outcome,
    CompiledDocumentationAttemptSnapshot? CompiledXml,
    DocumentationHouseRejectionKind? Rejection,
    CompiledDocumentationHouseFailureSnapshot? Failure,
    DocumentationIncompleteBoundary? IncompleteBoundary,
    CompiledDocumentationWorkSnapshot Work,
    DocumentationLibraryLeaseConsumer LeaseConsumer);

/// <summary>
/// Exact in-process settlement plus the producer-owned snapshot used for
/// serialization. Consumers serialize <see cref="Snapshot"/>, not
/// <see cref="Outcome"/>.
/// </summary>
public sealed class CompiledDocumentationQueryResult
{
    internal CompiledDocumentationQueryResult(
        DocumentationHouseOutcome outcome,
        CompiledDocumentationQuerySnapshot snapshot)
    {
        Outcome = outcome;
        Snapshot = snapshot;
    }

    [JsonIgnore]
    public DocumentationHouseOutcome Outcome { get; }

    public CompiledDocumentationQuerySnapshot Snapshot { get; }
}

/// <summary>
/// Executes one already-authorized DocumentationHouse request and publishes a
/// settled snapshot before returning control to a consumer.
/// </summary>
public static class CompiledDocumentationQuery
{
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
        return new(outcome, Snapshot(outcome));
    }

    private static CompiledDocumentationQuerySnapshot Snapshot(
        DocumentationHouseOutcome outcome)
    {
        DocumentationHouseRequest request = outcome.Request;
        return outcome switch
        {
            DocumentationHouseOutcome.Completed completed => Create(
                completed,
                CompiledDocumentationQueryOutcomeKind.Completed,
                Snapshot(completed.CompiledXmlAttempt),
                rejection: null,
                failure: null,
                incompleteBoundary: null),
            DocumentationHouseOutcome.Rejected rejected => Create(
                rejected,
                CompiledDocumentationQueryOutcomeKind.Rejected,
                compiledXml: null,
                rejected.Rejection.Kind,
                failure: null,
                incompleteBoundary: null),
            DocumentationHouseOutcome.Failed failed => Create(
                failed,
                CompiledDocumentationQueryOutcomeKind.Failed,
                compiledXml: null,
                rejection: null,
                new(
                    failed.Failure.Stage,
                    failed.Failure.Kind),
                incompleteBoundary: null),
            DocumentationHouseOutcome.Incomplete incomplete => Create(
                incomplete,
                CompiledDocumentationQueryOutcomeKind.Incomplete,
                compiledXml: null,
                rejection: null,
                failure: null,
                incomplete.Boundary),
            _ => throw new InvalidOperationException(
                "Unknown DocumentationHouse outcome."),
        };

        CompiledDocumentationQuerySnapshot Create(
            DocumentationHouseOutcome source,
            CompiledDocumentationQueryOutcomeKind kind,
            CompiledDocumentationAttemptSnapshot? compiledXml,
            DocumentationHouseRejectionKind? rejection,
            CompiledDocumentationHouseFailureSnapshot? failure,
            DocumentationIncompleteBoundary? incompleteBoundary) =>
            new(
                request.Identity.Name,
                request.Plan.Identity.Name,
                request.Plan.PolicyGeneration.Name,
                request.Demand,
                Snapshot(request.Subject),
                kind,
                compiledXml,
                rejection,
                failure,
                incompleteBoundary,
                new(
                    source.Work.ContributionsObserved,
                    source.Work.CompiledXmlBytesObserved,
                    source.Work.ParsedCompiledXml),
                source.LeaseSettlement.Consumer);
    }

    private static CompiledDocumentationSubjectSnapshot Snapshot(
        DocumentationSubjectReference subject)
    {
        var assembly = new CompiledDocumentationAssemblySnapshot(
            subject.MetadataAssembly.Name,
            subject.MetadataAssembly.Version?.ToString(),
            subject.MetadataAssembly.Culture,
            subject.MetadataAssembly.PublicKeyToken);
        var type = new CompiledDocumentationTypeSnapshot(
            subject.TypeIdentity.Namespace,
            [.. subject.TypeIdentity.Segments]);
        CompiledDocumentationMemberSnapshot? member =
            subject.MemberIdentity is { } value
                ? new(
                    value.StableSelector,
                    value.CanonicalSignature,
                    value.Fingerprint,
                    value.TypeFullName,
                    value.MemberName)
                : null;
        return new(
            assembly,
            type,
            member,
            subject.CompiledXmlIdentity.Value);
    }

    private static CompiledDocumentationAttemptSnapshot Snapshot(
        DocumentationCompiledXmlAttempt attempt)
    {
        CompiledDocumentationContributionSnapshot? selected =
            attempt switch
            {
                DocumentationCompiledXmlAttempt.Available available =>
                    Snapshot(available.Selected),
                DocumentationCompiledXmlAttempt.Absent absent =>
                    SnapshotOptional(absent.Selected),
                DocumentationCompiledXmlAttempt.Failed failed =>
                    Snapshot(failed.Selected),
                DocumentationCompiledXmlAttempt.Incomplete incomplete =>
                    SnapshotOptional(incomplete.Selected),
                _ => null,
            };
        ImmutableArray<CompiledDocumentationContributionSnapshot> candidates =
            attempt is DocumentationCompiledXmlAttempt.Ambiguous ambiguous
                ? [.. ambiguous.Candidates.Select(Snapshot)]
                : [];
        ImmutableArray<CompiledDocumentationRejectionSnapshot> rejections =
            attempt is DocumentationCompiledXmlAttempt.Rejected rejected
                ? [.. rejected.Rejections.Select(
                    static rejection => new CompiledDocumentationRejectionSnapshot(
                        Snapshot(rejection.Contribution),
                        rejection.Kind))]
                : [];
        CompiledDocumentationEntrySnapshot? documentation =
            attempt is DocumentationCompiledXmlAttempt.Available availableAttempt
                ? Snapshot(availableAttempt.Documentation)
                : null;
        DocumentationCompiledXmlFailureKind? failure =
            attempt is DocumentationCompiledXmlAttempt.Failed failedAttempt
                ? failedAttempt.Failure.Kind
                : null;
        DocumentationIncompleteBoundary? incompleteBoundary =
            attempt is DocumentationCompiledXmlAttempt.Incomplete incompleteAttempt
                ? incompleteAttempt.Boundary
                : null;

        return new(
            attempt.Kind,
            selected,
            candidates,
            [.. attempt.Contributions.Select(Snapshot)],
            rejections,
            documentation,
            failure,
            incompleteBoundary);
    }

    private static CompiledDocumentationContributionSnapshot Snapshot(
        CompiledXmlContribution contribution) =>
        new(
            contribution.Kind,
            contribution.Source.Kind,
            contribution.Source.Name,
            contribution.Precedence);

    private static CompiledDocumentationContributionSnapshot? SnapshotOptional(
        CompiledXmlContribution? contribution) =>
        contribution is null ? null : Snapshot(contribution);

    private static CompiledDocumentationEntrySnapshot Snapshot(
        CSharpText.XmlDocumentationEntry documentation) =>
        new(
            documentation.Summary,
            documentation.Remarks,
            documentation.Returns,
            [.. documentation.Parameters.Select(
                static parameter => new CompiledDocumentationParameterSnapshot(
                    parameter.Key,
                    parameter.Value))],
            [.. documentation.Exceptions.Select(
                static exception => new CompiledDocumentationExceptionSnapshot(
                    exception.Cref,
                    exception.Description))],
            [.. documentation.Samples.Select(
                static sample => new CompiledDocumentationSampleSnapshot(
                    sample.Source,
                    sample.Title,
                    sample.Region))]);
}

/// <summary>
/// Source-generated JSON contract for the Queries-owned portable snapshot.
/// The exact in-process DocumentationHouse outcome is intentionally excluded.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(CompiledDocumentationQuerySnapshot))]
public partial class CompiledDocumentationQueryJsonContext :
    JsonSerializerContext;
