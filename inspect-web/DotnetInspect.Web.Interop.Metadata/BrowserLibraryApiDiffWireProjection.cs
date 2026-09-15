using System.Runtime.Versioning;
using System.Text.Json;
using DotnetInspector.Presentation;
using DotnetInspector.Queries;
using Inspector.Findings;
using ILInspector.Metadata;

namespace DotnetInspect.Web.Interop.Metadata;

internal sealed record BrowserLibraryApiDiffEndpointContext(
    string PackageId,
    string Version,
    string Framework,
    string CompileAssetId,
    string AssetPath,
    string AssetAssemblyName);

/// <summary>
/// Projects the complete portable Library API diff into the bounded Library
/// Compare wire inventory.
/// </summary>
/// <remarks>
/// The Browser API-surface owner admits at most 100,000 Types and 32,000,000
/// retained text characters per endpoint. This narrower wire boundary admits
/// at most 10,000 changed Types and 6,000,000 characters across the repeated
/// document, display, and structured endpoint Type identities. It then
/// checks the exact collection-entry population, source-generates the exact
/// result JSON, and reserves the one-element tuple framing used by the ordinary
/// Worker. Admission examines the complete producer-ordered inventory before
/// publication; an excess rejects the whole result and never truncates it.
/// </remarks>
[SupportedOSPlatform("browser")]
internal static class BrowserLibraryApiDiffWireProjection
{
    internal const int MaxChangedTypes = 10_000;
    internal const int MaxTypeTextCharacters = 6_000_000;
    internal const int MaxOrdinaryWorkerJsonCharacters = 16_777_216;
    internal const int MaxOrdinaryWorkerCollectionEntries = 524_288;
    internal const int OrdinaryWorkerResultTupleOverhead = 2;

    internal static BrowserLibraryApiDiffResult Project(
        BrowserLibraryApiDiffRequest request,
        LibraryApiDiffOutcome outcome,
        BrowserLibraryApiDiffEndpointContext target,
        BrowserLibraryApiDiffEndpointContext current)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(outcome);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(current);

        BrowserLibraryApiDiffResult projected = outcome switch
        {
            LibraryApiDiffOutcome.Available available =>
                ProjectAvailable(request, available.Document, target, current),
            LibraryApiDiffOutcome.Unavailable unavailable =>
                new BrowserLibraryApiDiffResult(
                    1,
                    request,
                    BrowserLibraryApiDiffResultKind.Unavailable,
                    Value: null,
                    new BrowserLibraryApiDiffUnavailable(
                        Project(unavailable.Kind),
                        Project(target, unavailable.Before),
                        Project(current, unavailable.After)),
                    Rejected: null,
                    FailureKind: null,
                    Error: null,
                    Diagnostic: null,
                    Reason: null),
            LibraryApiDiffOutcome.Rejected rejected =>
                Rejected(
                    request,
                    Project(rejected.Kind),
                    Project(target, rejected.Before),
                    Project(current, rejected.After)),
            _ => throw new InvalidOperationException(
                "Unknown Library API diff outcome."),
        };
        return AdmitTransport(request, projected);
    }

    static BrowserLibraryApiDiffResult ProjectAvailable(
        BrowserLibraryApiDiffRequest request,
        LibraryApiDiffDocument document,
        BrowserLibraryApiDiffEndpointContext target,
        BrowserLibraryApiDiffEndpointContext current)
    {
        BrowserLibraryApiDiffEndpoint targetEndpoint =
            Project(target, document.Before);
        BrowserLibraryApiDiffEndpoint currentEndpoint =
            Project(current, document.After);
        if (document.Comparison.Subjects.Length > MaxChangedTypes)
        {
            return Rejected(
                request,
                BrowserLibraryApiDiffRejectionKind
                    .ChangedTypeCountLimitExceeded,
                targetEndpoint,
                currentEndpoint,
                MaxChangedTypes,
                document.Comparison.Subjects.Length);
        }

        long textCharacters = document.Comparison.Subjects.Sum(TypeTextCharacters);
        if (textCharacters > MaxTypeTextCharacters)
        {
            return Rejected(
                request,
                BrowserLibraryApiDiffRejectionKind.TypeTextLimitExceeded,
                targetEndpoint,
                currentEndpoint,
                MaxTypeTextCharacters,
                textCharacters);
        }

        BrowserLibraryApiDiffType[] types =
        [
            .. document.Comparison.Subjects.Select(Project),
        ];
        LibraryApiDiffSummary summary = document.Summary;
        return new BrowserLibraryApiDiffResult(
            1,
            request,
            BrowserLibraryApiDiffResultKind.Succeeded,
            new BrowserLibraryApiDiffSucceeded(
                document.Comparison.Identifier,
                document.Comparison.Display,
                targetEndpoint,
                currentEndpoint,
                new BrowserLibraryApiDiffAggregate(
                    summary.ChangedTypeCount,
                    summary.AddedTypeCount,
                    summary.RemovedTypeCount,
                    summary.ChangedMemberCount,
                    summary.BreakingCount,
                    summary.AdditiveCount,
                    summary.PotentiallyBreakingCount),
                types),
            Unavailable: null,
            Rejected: null,
            FailureKind: null,
            Error: null,
            Diagnostic: null,
            Reason: null);
    }

    static BrowserLibraryApiDiffResult AdmitTransport(
        BrowserLibraryApiDiffRequest request,
        BrowserLibraryApiDiffResult result)
    {
        long collectionEntries = CollectionEntries(result);
        if (collectionEntries > MaxOrdinaryWorkerCollectionEntries)
        {
            return TransportRejected(
                request,
                BrowserLibraryApiDiffRejectionKind
                    .CollectionEntryLimitExceeded,
                MaxOrdinaryWorkerCollectionEntries,
                collectionEntries);
        }

        int serializedCharacters = JsonSerializer.Serialize(
            result,
            BrowserMetadataJsonContext.Default.BrowserLibraryApiDiffResult)
            .Length;
        long transportedCharacters =
            (long)serializedCharacters + OrdinaryWorkerResultTupleOverhead;
        if (transportedCharacters <= MaxOrdinaryWorkerJsonCharacters)
        {
            return result;
        }

        return TransportRejected(
            request,
            BrowserLibraryApiDiffRejectionKind.SerializedResultLimitExceeded,
            MaxOrdinaryWorkerJsonCharacters,
            transportedCharacters);
    }

    static BrowserLibraryApiDiffResult TransportRejected(
        BrowserLibraryApiDiffRequest request,
        BrowserLibraryApiDiffRejectionKind kind,
        long bound,
        long observed)
    {
        var result = new BrowserLibraryApiDiffResult(
            1,
            request,
            BrowserLibraryApiDiffResultKind.Rejected,
            Value: null,
            Unavailable: null,
            new BrowserLibraryApiDiffRejected(
                kind,
                Target: null,
                Current: null,
                bound,
                observed),
            FailureKind: null,
            Error: null,
            Diagnostic: null,
            Reason: null);
        int serializedCharacters = JsonSerializer.Serialize(
            result,
            BrowserMetadataJsonContext.Default.BrowserLibraryApiDiffResult)
            .Length;
        if (CollectionEntries(result) > MaxOrdinaryWorkerCollectionEntries
            || (long)serializedCharacters + OrdinaryWorkerResultTupleOverhead
                > MaxOrdinaryWorkerJsonCharacters)
        {
            throw new InvalidOperationException(
                "The bounded Library API diff transport rejection exceeds "
                    + "the ordinary Worker admission limits.");
        }
        return result;
    }

    static long CollectionEntries(BrowserLibraryApiDiffResult result) =>
        OrdinaryWorkerResultTupleOverhead
        + 12
        + (result.Request is null ? 0 : 7)
        + (result.Value is null ? 0 : CollectionEntries(result.Value))
        + (result.Unavailable is null
            ? 0
            : CollectionEntries(result.Unavailable))
        + (result.Rejected is null ? 0 : CollectionEntries(result.Rejected));

    static long CollectionEntries(BrowserLibraryApiDiffSucceeded value) =>
        7
        + CollectionEntries(value.Target)
        + CollectionEntries(value.Current)
        + 8
        + value.Types.Length + 1
        + value.Types.Sum(CollectionEntries);

    static long CollectionEntries(BrowserLibraryApiDiffUnavailable value) =>
        4
        + CollectionEntries(value.Target)
        + CollectionEntries(value.Current);

    static long CollectionEntries(BrowserLibraryApiDiffRejected value) =>
        6
        + (value.Target is null ? 0 : CollectionEntries(value.Target))
        + (value.Current is null ? 0 : CollectionEntries(value.Current));

    static long CollectionEntries(BrowserLibraryApiDiffEndpoint endpoint) =>
        9
        + 4
        + 5
        + endpoint.Issues.Length + 1
        + endpoint.Issues.Sum(CollectionEntries);

    static long CollectionEntries(BrowserLibraryApiDiffEndpointIssue issue) =>
        8
        + (issue.Truncation is null ? 0 : 11)
        + (issue.InspectionFailures is null
            ? 0
            : issue.InspectionFailures.Length + 1
                + issue.InspectionFailures.Sum(CollectionEntries));

    static long CollectionEntries(
        BrowserLibraryApiDiffInspectionFailure failure) =>
        8
        + (failure.SubjectAssembly is null ? 0 : 5)
        + (failure.DependencyAssembly is null ? 0 : 5);

    static long CollectionEntries(BrowserLibraryApiDiffType type) =>
        11
        + (type.Before is null ? 0 : CollectionEntries(type.Before))
        + (type.After is null ? 0 : CollectionEntries(type.After));

    static long CollectionEntries(BrowserLibraryApiDiffTypeIdentity identity) =>
        6 + identity.Segments.Length;

    static BrowserLibraryApiDiffResult Rejected(
        BrowserLibraryApiDiffRequest request,
        BrowserLibraryApiDiffRejectionKind kind,
        BrowserLibraryApiDiffEndpoint target,
        BrowserLibraryApiDiffEndpoint current,
        long? bound = null,
        long? observed = null) =>
        new(
            1,
            request,
            BrowserLibraryApiDiffResultKind.Rejected,
            Value: null,
            Unavailable: null,
            new BrowserLibraryApiDiffRejected(
                kind,
                target,
                current,
                bound,
                observed),
            FailureKind: null,
            Error: null,
            Diagnostic: null,
            Reason: null);

    static BrowserLibraryApiDiffEndpoint Project(
        BrowserLibraryApiDiffEndpointContext context,
        LibraryApiDiffEndpointSummary summary) =>
        new(
            context.PackageId,
            context.Version,
            context.Framework,
            new BrowserLibraryApiDiffCompileAsset(
                context.CompileAssetId,
                context.AssetPath,
                context.AssetAssemblyName),
            Project(summary.Identity),
            Project(summary.Scope),
            summary.IsComplete,
            [.. summary.Issues.Select(Project)]);

    static BrowserLibraryApiDiffAssemblyIdentity Project(
        AssemblyReferenceIdentity identity) =>
        new(
            identity.Name,
            identity.Version?.ToString(),
            identity.Culture,
            identity.PublicKeyToken);

    static BrowserLibraryApiDiffEndpointIssue Project(
        LibraryApiDiffEndpointIssue issue) =>
        issue switch
        {
            LibraryApiDiffEndpointIssue.Truncated truncated =>
                new(
                    BrowserLibraryApiDiffEndpointIssueKind.Truncated,
                    Project(truncated.Truncation)),
            LibraryApiDiffEndpointIssue.Rejected rejected =>
                new(
                    BrowserLibraryApiDiffEndpointIssueKind.Rejected,
                    OpenFailureKind: Project(rejected.Kind),
                    Detail: rejected.Detail.ToString(),
                    MetadataRootReason: rejected.MetadataRootReason is { } reason
                        ? Project(reason)
                        : null),
            LibraryApiDiffEndpointIssue.Failed failed =>
                new(
                    BrowserLibraryApiDiffEndpointIssueKind.Failed,
                    Detail: failed.Detail.ToString()),
            LibraryApiDiffEndpointIssue.InspectionFailures failures =>
                new(
                    BrowserLibraryApiDiffEndpointIssueKind.InspectionFailures,
                    Count: failures.Count,
                    InspectionFailures:
                    [
                        .. failures.Details.Select(Project),
                    ]),
            LibraryApiDiffEndpointIssue.DegradedSignatures degraded =>
                new(
                    BrowserLibraryApiDiffEndpointIssueKind.DegradedSignatures,
                    Count: degraded.Count),
            LibraryApiDiffEndpointIssue.UnexpectedAssemblyPopulation population =>
                new(
                    BrowserLibraryApiDiffEndpointIssueKind
                        .UnexpectedAssemblyPopulation,
                    Count: population.Count),
            _ => throw new InvalidOperationException(
                "Unknown Library API diff endpoint issue."),
        };

    static BrowserLibraryApiDiffInspectionFailure Project(
        LibraryApiDiffInspectionFailure failure) =>
        new(
            failure.Operation.ToString(),
            failure.SubjectToken,
            failure.Mechanism switch
            {
                MetadataTypeNameFailureMechanism.Metadata =>
                    BrowserLibraryApiDiffInspectionFailureMechanism.Metadata,
                MetadataTypeNameFailureMechanism.Relationship =>
                    BrowserLibraryApiDiffInspectionFailureMechanism.Relationship,
                MetadataTypeNameFailureMechanism.Signature =>
                    BrowserLibraryApiDiffInspectionFailureMechanism.Signature,
                MetadataTypeNameFailureMechanism.TypeSpecification =>
                    BrowserLibraryApiDiffInspectionFailureMechanism.TypeSpecification,
                _ => throw new ArgumentOutOfRangeException(nameof(failure)),
            },
            failure.Kind.ToString(),
            failure.Detail.ToString(),
            failure.SubjectAssembly is null
                ? null
                : Project(failure.SubjectAssembly),
            failure.DependencyAssembly is null
                ? null
                : Project(failure.DependencyAssembly));

    static BrowserLibraryApiDiffProjectionTruncation Project(
        ApiSurfaceProjectionTruncation truncation) =>
        new(
            Project(truncation.Limit),
            truncation.Bound,
            truncation.ProjectedParticipants,
            truncation.OmittedParticipants,
            truncation.ProjectedTypes,
            truncation.ProjectedMembers,
            truncation.ProjectedInspectionFailures,
            truncation.ProjectedTypeForwarders,
            truncation.InspectedMetadataRows,
            truncation.ProjectedRetainedTextCharacters);

    static BrowserLibraryApiDiffType Project(
        ComparisonSubject<LibraryApiTypeDiff> subject)
    {
        LibraryApiTypeDiff type = subject.Comparison;
        return new(
            subject.Identifier,
            subject.Display,
            subject.Change switch
            {
                ComparisonSubjectChange.Diff =>
                    BrowserLibraryApiDiffTypeState.Diff,
                ComparisonSubjectChange.Addition =>
                    BrowserLibraryApiDiffTypeState.Addition,
                ComparisonSubjectChange.Deletion =>
                    BrowserLibraryApiDiffTypeState.Deletion,
                _ => throw new InvalidOperationException(
                    "The Library Type inventory contains an unsupported "
                        + "exceptional coordinate change."),
            },
            type.TypeDefinitionChanged,
            type.ChangedMemberCount,
            type.BreakingCount,
            type.AdditiveCount,
            type.PotentiallyBreakingCount,
            type.Before is null ? null : Project(type.Before),
            type.After is null ? null : Project(type.After));
    }

    static BrowserLibraryApiDiffTypeIdentity Project(
        LibraryApiTypeIdentity identity) =>
        new(
            identity.Identifier,
            identity.DefinitionName.Namespace,
            [.. identity.DefinitionName.Segments],
            identity.Display);

    static long TypeTextCharacters(
        ComparisonSubject<LibraryApiTypeDiff> subject)
    {
        long count = subject.Identifier.Length + subject.Display.Length;
        Add(subject.Comparison.Before);
        Add(subject.Comparison.After);
        return count;

        void Add(LibraryApiTypeIdentity? identity)
        {
            if (identity is null)
                return;
            count += identity.Identifier.Length;
            count += identity.DefinitionName.Namespace.Length;
            count += identity.DefinitionName.Segments.Sum(segment => segment.Length);
            count += identity.Display.Length;
        }
    }

    static BrowserLibraryApiDiffUnavailableKind Project(
        LibraryApiDiffUnavailableKind kind) =>
        kind switch
        {
            LibraryApiDiffUnavailableKind.BeforeIncomplete =>
                BrowserLibraryApiDiffUnavailableKind.TargetIncomplete,
            LibraryApiDiffUnavailableKind.AfterIncomplete =>
                BrowserLibraryApiDiffUnavailableKind.CurrentIncomplete,
            LibraryApiDiffUnavailableKind.BothIncomplete =>
                BrowserLibraryApiDiffUnavailableKind.BothIncomplete,
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };

    static BrowserLibraryApiDiffRejectionKind Project(
        LibraryApiDiffRejectionKind kind) =>
        kind switch
        {
            LibraryApiDiffRejectionKind.LogicalLibraryMismatch =>
                BrowserLibraryApiDiffRejectionKind.LogicalLibraryMismatch,
            LibraryApiDiffRejectionKind.FindingComparisonFailed =>
                BrowserLibraryApiDiffRejectionKind.FindingComparisonFailed,
            LibraryApiDiffRejectionKind.CompatibilityInspectionFailed =>
                BrowserLibraryApiDiffRejectionKind
                    .CompatibilityInspectionFailed,
            LibraryApiDiffRejectionKind.MissingExactTypeIdentity =>
                BrowserLibraryApiDiffRejectionKind.MissingExactTypeIdentity,
            LibraryApiDiffRejectionKind.MissingMemberAnchor =>
                BrowserLibraryApiDiffRejectionKind.MissingMemberAnchor,
            LibraryApiDiffRejectionKind.DuplicateExactTypeIdentity =>
                BrowserLibraryApiDiffRejectionKind.DuplicateExactTypeIdentity,
            LibraryApiDiffRejectionKind.UnassociatedStructuredSubject =>
                BrowserLibraryApiDiffRejectionKind
                    .UnassociatedStructuredSubject,
            LibraryApiDiffRejectionKind.ContradictoryOccupiedSideTopology =>
                BrowserLibraryApiDiffRejectionKind
                    .ContradictoryOccupiedSideTopology,
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };

    static BrowserLibraryApiDiffSurfaceScope Project(
        ApiSurfaceScope scope) =>
        scope switch
        {
            ApiSurfaceScope.Public =>
                BrowserLibraryApiDiffSurfaceScope.Public,
            ApiSurfaceScope.IncludeAll =>
                BrowserLibraryApiDiffSurfaceScope.IncludeAll,
            ApiSurfaceScope.PublicWithNonPublicTypes =>
                BrowserLibraryApiDiffSurfaceScope.PublicWithNonPublicTypes,
            _ => throw new ArgumentOutOfRangeException(nameof(scope)),
        };

    static BrowserLibraryApiDiffProjectionLimit Project(
        ApiSurfaceProjectionLimit limit) =>
        limit switch
        {
            ApiSurfaceProjectionLimit.Participants =>
                BrowserLibraryApiDiffProjectionLimit.Participants,
            ApiSurfaceProjectionLimit.Types =>
                BrowserLibraryApiDiffProjectionLimit.Types,
            ApiSurfaceProjectionLimit.Members =>
                BrowserLibraryApiDiffProjectionLimit.Members,
            ApiSurfaceProjectionLimit.InspectionFailures =>
                BrowserLibraryApiDiffProjectionLimit.InspectionFailures,
            ApiSurfaceProjectionLimit.TypeForwarders =>
                BrowserLibraryApiDiffProjectionLimit.TypeForwarders,
            ApiSurfaceProjectionLimit.MetadataRows =>
                BrowserLibraryApiDiffProjectionLimit.MetadataRows,
            ApiSurfaceProjectionLimit.RetainedTextCharacters =>
                BrowserLibraryApiDiffProjectionLimit.RetainedTextCharacters,
            _ => throw new ArgumentOutOfRangeException(nameof(limit)),
        };

    static BrowserLibraryApiDiffOpenFailureKind Project(
        CandidateOpenFailureKind kind) =>
        kind switch
        {
            CandidateOpenFailureKind.Unreadable =>
                BrowserLibraryApiDiffOpenFailureKind.Unreadable,
            CandidateOpenFailureKind.InvalidImage =>
                BrowserLibraryApiDiffOpenFailureKind.InvalidImage,
            CandidateOpenFailureKind.ResourceBudget =>
                BrowserLibraryApiDiffOpenFailureKind.ResourceBudget,
            CandidateOpenFailureKind.UnsupportedMetadataFormat =>
                BrowserLibraryApiDiffOpenFailureKind.UnsupportedMetadataFormat,
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };

    static BrowserLibraryApiDiffMetadataRootMalformedReason Project(
        MetadataRootMalformedReason reason) =>
        reason switch
        {
            MetadataRootMalformedReason.UnmappableMetadataDirectory =>
                BrowserLibraryApiDiffMetadataRootMalformedReason
                    .UnmappableMetadataDirectory,
            MetadataRootMalformedReason.TruncatedFixedPrefix =>
                BrowserLibraryApiDiffMetadataRootMalformedReason
                    .TruncatedFixedPrefix,
            MetadataRootMalformedReason.InvalidSignature =>
                BrowserLibraryApiDiffMetadataRootMalformedReason.InvalidSignature,
            MetadataRootMalformedReason.InvalidVersionLength =>
                BrowserLibraryApiDiffMetadataRootMalformedReason
                    .InvalidVersionLength,
            MetadataRootMalformedReason.TruncatedVersionField =>
                BrowserLibraryApiDiffMetadataRootMalformedReason
                    .TruncatedVersionField,
            MetadataRootMalformedReason.MissingVersionTerminator =>
                BrowserLibraryApiDiffMetadataRootMalformedReason
                    .MissingVersionTerminator,
            _ => throw new ArgumentOutOfRangeException(nameof(reason)),
        };
}
