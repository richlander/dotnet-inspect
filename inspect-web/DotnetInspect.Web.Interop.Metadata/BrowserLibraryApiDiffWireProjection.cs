using System.Runtime.Versioning;
using System.Text.Json;
using DotnetInspector.Presentation;
using DotnetInspector.Queries;
using DotnetInspector.Sections;
using Inspector.Findings;
using ILInspector.Metadata;
using static DotnetInspect.Web.BrowserOrdinaryWorkerJsonBudget;

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
/// document, display, structured endpoint Type identities, and exact changed-
/// Member identities. It admits the complete service baseline before building
/// the repeated Browser inventory, then checks the final result against the
/// ordinary Worker's collection-entry and <c>JSON.stringify</c> character
/// limits. An excess rejects the whole result and never truncates the baseline
/// or inventory.
/// </remarks>
[SupportedOSPlatform("browser")]
internal static class BrowserLibraryApiDiffWireProjection
{
    internal const int MaxChangedTypes = 10_000;
    internal const int MaxTypeTextCharacters = 6_000_000;
    internal const int MaxOrdinaryWorkerJsonCharacters =
        BrowserOrdinaryWorkerJsonBudget.MaxOrdinaryWorkerJsonCharacters;
    internal const int MaxOrdinaryWorkerCollectionEntries =
        BrowserOrdinaryWorkerJsonBudget.MaxOrdinaryWorkerCollectionEntries;
    internal const int OrdinaryWorkerResultTupleOverhead =
        BrowserOrdinaryWorkerJsonBudget.OrdinaryWorkerResultTupleOverhead;

    internal static BrowserLibraryApiDiffResult Project(
        BrowserLibraryApiDiffRequest request,
        InspectionEnvelope<LibraryApiDiffOutcome> inspection,
        BrowserLibraryApiDiffEndpointContext target,
        BrowserLibraryApiDiffEndpointContext current)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(inspection);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(current);

        var wireInspection = new InspectionEnvelope<JsonElement>(
            inspection.ResourcePath,
            inspection.ContentKind,
            JsonSerializer.SerializeToElement(
                inspection.Content,
                LibraryApiDiffJsonContext.Default.LibraryApiDiffOutcome),
            inspection.PortableProjection,
            inspection.Diagnostics);
        BrowserLibraryApiDiffResult? inspectionRejection =
            AdmitInspectionTransport(request, wireInspection);
        if (inspectionRejection is not null)
        {
            return inspectionRejection;
        }

        BrowserLibraryApiDiffResult projected = inspection.Content switch
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
        return AdmitTransport(
            request,
            projected with { Inspection = wireInspection });
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
        using JsonDocument document = JsonSerializer.SerializeToDocument(
            result,
            BrowserMetadataJsonContext.Default.BrowserLibraryApiDiffResult);
        long collectionEntries = OrdinaryWorkerResultTupleOverhead
            + CollectionEntries(document.RootElement);
        if (collectionEntries > MaxOrdinaryWorkerCollectionEntries)
        {
            return TransportRejected(
                request,
                BrowserLibraryApiDiffRejectionKind
                    .CollectionEntryLimitExceeded,
                MaxOrdinaryWorkerCollectionEntries,
                collectionEntries);
        }
        long transportedCharacters =
            JsonStringifyCharacters(document.RootElement)
            + OrdinaryWorkerResultTupleOverhead;
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

    static BrowserLibraryApiDiffResult? AdmitInspectionTransport(
        BrowserLibraryApiDiffRequest request,
        InspectionEnvelope<JsonElement> inspection)
    {
        using JsonDocument document = JsonSerializer.SerializeToDocument(
            inspection,
            BrowserMetadataJsonContext.Default.JsonInspectionEnvelope);
        long collectionEntries = OrdinaryWorkerResultTupleOverhead
            + CollectionEntries(document.RootElement);
        if (collectionEntries > MaxOrdinaryWorkerCollectionEntries)
        {
            return TransportRejected(
                request,
                BrowserLibraryApiDiffRejectionKind
                    .CollectionEntryLimitExceeded,
                MaxOrdinaryWorkerCollectionEntries,
                collectionEntries);
        }

        long transportedCharacters =
            JsonStringifyCharacters(document.RootElement)
            + OrdinaryWorkerResultTupleOverhead;
        if (transportedCharacters > MaxOrdinaryWorkerJsonCharacters)
        {
            return TransportRejected(
                request,
                BrowserLibraryApiDiffRejectionKind
                    .SerializedResultLimitExceeded,
                MaxOrdinaryWorkerJsonCharacters,
                transportedCharacters);
        }

        return null;
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
        using JsonDocument document = JsonSerializer.SerializeToDocument(
            result,
            BrowserMetadataJsonContext.Default.BrowserLibraryApiDiffResult);
        if (OrdinaryWorkerResultTupleOverhead + CollectionEntries(document.RootElement)
                > MaxOrdinaryWorkerCollectionEntries
            || JsonStringifyCharacters(document.RootElement)
                + OrdinaryWorkerResultTupleOverhead
                > MaxOrdinaryWorkerJsonCharacters)
        {
            throw new InvalidOperationException(
                "The bounded Library API diff transport rejection exceeds "
                    + "the ordinary Worker admission limits.");
        }
        return result;
    }

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
            type.After is null ? null : Project(type.After),
            [.. type.Members.Select(member => Project(member, type))],
            [
                .. type.CompatibilityChanges
                    .Where(change =>
                        change.Subject.Kind == ApiChangeSubjectKind.Type)
                    .Select(Project),
            ]);
    }

    // A member-level change belongs to the relation whose Before or After
    // endpoint is the change subject's endpoint: exact declaring-Type identity
    // plus anchor, never display text.
    static bool Describes(
        LibraryApiCompatibilityChange change,
        LibraryApiMemberRelation relation)
    {
        if (change.Subject.Kind != ApiChangeSubjectKind.Member)
            return false;
        return SameEndpoint(change.Subject.BeforeMember, relation.Before)
            || SameEndpoint(change.Subject.AfterMember, relation.After);
    }

    static bool SameEndpoint(
        LibraryApiMemberIdentity? left,
        LibraryApiMemberIdentity? right) =>
        left is not null
        && right is not null
        && StringComparer.Ordinal.Equals(
            left.DeclaringType.Identifier,
            right.DeclaringType.Identifier)
        && left.Anchor == right.Anchor;

    static BrowserLibraryApiDiffChange Project(
        LibraryApiCompatibilityChange change) =>
        new(
            Project(change.Kind),
            change.Classification switch
            {
                ChangeClassification.Additive =>
                    BrowserLibraryApiDiffChangeClassification.Additive,
                ChangeClassification.Breaking =>
                    BrowserLibraryApiDiffChangeClassification.Breaking,
                ChangeClassification.PotentiallyBreaking =>
                    BrowserLibraryApiDiffChangeClassification
                        .PotentiallyBreaking,
                _ => throw new ArgumentOutOfRangeException(
                    nameof(change),
                    change.Classification,
                    "Unknown Library API change classification."),
            },
            change.Category switch
            {
                ApiChangeCategory.Signature =>
                    BrowserLibraryApiDiffChangeCategory.Signature,
                ApiChangeCategory.Attribute =>
                    BrowserLibraryApiDiffChangeCategory.Attribute,
                _ => throw new ArgumentOutOfRangeException(
                    nameof(change),
                    change.Category,
                    "Unknown Library API change category."),
            },
            change.Message.ToString(),
            change.OldValue?.ToString(),
            change.NewValue?.ToString());

    static BrowserLibraryApiDiffChangeKind Project(ChangeKind kind) =>
        Enum.IsDefined(kind)
            && Enum.TryParse(
                kind.ToString(),
                ignoreCase: false,
                out BrowserLibraryApiDiffChangeKind projected)
            ? projected
            : throw new ArgumentOutOfRangeException(
                nameof(kind),
                kind,
                "Unknown Library API change kind.");

    static BrowserLibraryApiDiffTypeIdentity Project(
        LibraryApiTypeIdentity identity) =>
        new(
            identity.Identifier,
            identity.DefinitionName.Namespace,
            [.. identity.DefinitionName.Segments],
            identity.Display);

    static BrowserLibraryApiDiffMember Project(
        LibraryApiMemberDiff member,
        LibraryApiTypeDiff type) =>
        new(
            member.Relation.Identifier,
            member.Relation.PairKind switch
            {
                LibraryApiMemberPairKind.Changed =>
                    BrowserLibraryApiDiffMemberPairKind.Changed,
                LibraryApiMemberPairKind.Added =>
                    BrowserLibraryApiDiffMemberPairKind.Added,
                LibraryApiMemberPairKind.Removed =>
                    BrowserLibraryApiDiffMemberPairKind.Removed,
                _ => throw new ArgumentOutOfRangeException(
                    nameof(member),
                    member.Relation.PairKind,
                    "Unknown Library API member pair kind."),
            },
            member.Role switch
            {
                LibraryApiMemberRelationRole.Before =>
                    BrowserLibraryApiDiffMemberRelationRole.Before,
                LibraryApiMemberRelationRole.After =>
                    BrowserLibraryApiDiffMemberRelationRole.After,
                LibraryApiMemberRelationRole.Both =>
                    BrowserLibraryApiDiffMemberRelationRole.Both,
                _ => throw new ArgumentOutOfRangeException(
                    nameof(member),
                    member.Role,
                    "Unknown Library API member relation role."),
            },
            member.Relation.Before is null
                ? null
                : Project(member.Relation.Before),
            member.Relation.After is null
                ? null
                : Project(member.Relation.After),
            [
                .. type.CompatibilityChanges
                    .Where(change => Describes(change, member.Relation))
                    .Select(Project),
            ],
            member.Relation.Match is null
                ? null
                : new BrowserLibraryApiDiffMatch(
                    member.Relation.Match.Tier.Id,
                    member.Relation.Match.Confidence));

    static BrowserLibraryApiDiffMemberIdentity Project(
        LibraryApiMemberIdentity identity) =>
        new(
            identity.DeclaringType.Identifier,
            identity.Anchor.StableSelector,
            identity.Anchor.CanonicalSignature,
            identity.Anchor.Fingerprint,
            identity.Anchor.TypeFullName,
            identity.Anchor.MemberName,
            identity.Display);

    static long TypeTextCharacters(
        ComparisonSubject<LibraryApiTypeDiff> subject)
    {
        long count = subject.Identifier.Length + subject.Display.Length;
        AddTypeIdentity(subject.Comparison.Before);
        AddTypeIdentity(subject.Comparison.After);
        foreach (LibraryApiMemberDiff member in subject.Comparison.Members)
        {
            count += member.Relation.Identifier.Length;
            AddMemberIdentity(member.Relation.Before);
            AddMemberIdentity(member.Relation.After);
        }
        foreach (LibraryApiCompatibilityChange change
            in subject.Comparison.CompatibilityChanges)
        {
            count += change.Message.Length;
            count += change.OldValue?.Length ?? 0;
            count += change.NewValue?.Length ?? 0;
        }
        return count;

        void AddTypeIdentity(LibraryApiTypeIdentity? identity)
        {
            if (identity is null)
                return;
            count += identity.Identifier.Length;
            count += identity.DefinitionName.Namespace.Length;
            count += identity.DefinitionName.Segments.Sum(segment => segment.Length);
            count += identity.Display.Length;
        }

        void AddMemberIdentity(LibraryApiMemberIdentity? identity)
        {
            if (identity is null)
                return;
            count += identity.DeclaringType.Identifier.Length;
            count += identity.Anchor.StableSelector.Length;
            count += identity.Anchor.CanonicalSignature.Length;
            count += identity.Anchor.Fingerprint.Length;
            count += identity.Anchor.TypeFullName.Length;
            count += identity.Anchor.MemberName.Length;
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
