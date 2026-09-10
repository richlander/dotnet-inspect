using System.Runtime.Versioning;
using DotnetInspector.Presentation;
using DotnetInspector.Queries;
using ILInspector.Metadata;
using ILInspector.MetadataPrimitives;
using Inspector.Findings;

namespace InspectWeb.Engine.MetadataFacade;

/// <summary>
/// Projects the shared <see cref="LibraryApiDiffPresentationResult"/> — produced once by
/// <see cref="LibraryApiDiffPresentationAdapter"/> from one <see cref="AssemblyContextApiComparisonResult"/>
/// — onto this facade's own source-generated wire contract (#6423). No reconstruction happens
/// here: every count, identity, relation, and classification is carried straight from the
/// producer-owned presentation model.
/// </summary>
[SupportedOSPlatform("browser")]
internal static class BrowserLibraryApiDiffWireProjection
{
    internal static BrowserLibraryApiDiff Project(
        BrowserLibraryApiDiffRequest request,
        LibraryApiDiffPresentationResult result) =>
        result switch
        {
            LibraryApiDiffPresentationResult.Available available =>
                new(
                    request,
                    BrowserLibraryApiDiffPresentationKind.Available,
                    Project(available.Before),
                    Project(available.After),
                    Project(available.Summary),
                    Project(available.Document),
                    UnavailableKind: null,
                    RejectionKind: null),
            LibraryApiDiffPresentationResult.Unavailable unavailable =>
                new(
                    request,
                    BrowserLibraryApiDiffPresentationKind.Unavailable,
                    Project(unavailable.Before),
                    Project(unavailable.After),
                    Summary: null,
                    Document: null,
                    Project(unavailable.Kind),
                    RejectionKind: null),
            LibraryApiDiffPresentationResult.Rejected rejected =>
                new(
                    request,
                    BrowserLibraryApiDiffPresentationKind.Rejected,
                    Project(rejected.Before),
                    Project(rejected.After),
                    Summary: null,
                    Document: null,
                    UnavailableKind: null,
                    Project(rejected.Kind)),
            _ => throw new InvalidOperationException(
                "Unknown Library API diff presentation result."),
        };

    static BrowserLibraryApiDiffEndpointSummary Project(LibraryApiDiffEndpointSummary summary) =>
        new(
            Project(summary.Identity),
            Project(summary.Scope),
            summary.IsComplete,
            [.. summary.Issues.Select(Project)]);

    static BrowserLibraryApiDiffAssemblyIdentity Project(AssemblyReferenceIdentity identity) =>
        new(identity.Name, identity.Version?.ToString(), identity.Culture, identity.PublicKeyToken);

    static BrowserLibraryApiSurfaceScope Project(ApiSurfaceScope scope) =>
        scope switch
        {
            ApiSurfaceScope.Public => BrowserLibraryApiSurfaceScope.Public,
            ApiSurfaceScope.IncludeAll => BrowserLibraryApiSurfaceScope.IncludeAll,
            ApiSurfaceScope.PublicWithNonPublicTypes =>
                BrowserLibraryApiSurfaceScope.PublicWithNonPublicTypes,
            _ => throw new ArgumentOutOfRangeException(nameof(scope)),
        };

    static BrowserLibraryApiDiffEndpointIssue Project(LibraryApiDiffEndpointIssue issue) =>
        issue switch
        {
            LibraryApiDiffEndpointIssue.Truncated truncated =>
                new(
                    BrowserLibraryApiDiffEndpointIssueKind.Truncated,
                    Project(truncated.Truncation),
                    OpenFailureKind: null,
                    Detail: null,
                    MetadataRootReason: null,
                    Count: null),
            LibraryApiDiffEndpointIssue.Rejected rejected =>
                new(
                    BrowserLibraryApiDiffEndpointIssueKind.Rejected,
                    Truncation: null,
                    Project(rejected.Kind),
                    rejected.Detail.ToString(),
                    rejected.MetadataRootReason is { } reason ? Project(reason) : null,
                    Count: null),
            LibraryApiDiffEndpointIssue.Failed failed =>
                new(
                    BrowserLibraryApiDiffEndpointIssueKind.Failed,
                    Truncation: null,
                    OpenFailureKind: null,
                    failed.Detail.ToString(),
                    MetadataRootReason: null,
                    Count: null),
            LibraryApiDiffEndpointIssue.InspectionFailures inspectionFailures =>
                new(
                    BrowserLibraryApiDiffEndpointIssueKind.InspectionFailures,
                    Truncation: null,
                    OpenFailureKind: null,
                    Detail: null,
                    MetadataRootReason: null,
                    inspectionFailures.Count),
            LibraryApiDiffEndpointIssue.DegradedSignatures degradedSignatures =>
                new(
                    BrowserLibraryApiDiffEndpointIssueKind.DegradedSignatures,
                    Truncation: null,
                    OpenFailureKind: null,
                    Detail: null,
                    MetadataRootReason: null,
                    degradedSignatures.Count),
            LibraryApiDiffEndpointIssue.UnexpectedAssemblyPopulation unexpectedAssemblyPopulation =>
                new(
                    BrowserLibraryApiDiffEndpointIssueKind.UnexpectedAssemblyPopulation,
                    Truncation: null,
                    OpenFailureKind: null,
                    Detail: null,
                    MetadataRootReason: null,
                    unexpectedAssemblyPopulation.Count),
            _ => throw new InvalidOperationException(
                "Unknown Library API diff endpoint issue."),
        };

    static BrowserLibraryApiDiffTruncation Project(ApiSurfaceProjectionTruncation truncation) =>
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

    static BrowserLibraryApiDiffTruncationLimit Project(ApiSurfaceProjectionLimit limit) =>
        limit switch
        {
            ApiSurfaceProjectionLimit.Participants =>
                BrowserLibraryApiDiffTruncationLimit.Participants,
            ApiSurfaceProjectionLimit.Types => BrowserLibraryApiDiffTruncationLimit.Types,
            ApiSurfaceProjectionLimit.Members => BrowserLibraryApiDiffTruncationLimit.Members,
            ApiSurfaceProjectionLimit.InspectionFailures =>
                BrowserLibraryApiDiffTruncationLimit.InspectionFailures,
            ApiSurfaceProjectionLimit.TypeForwarders =>
                BrowserLibraryApiDiffTruncationLimit.TypeForwarders,
            ApiSurfaceProjectionLimit.MetadataRows =>
                BrowserLibraryApiDiffTruncationLimit.MetadataRows,
            ApiSurfaceProjectionLimit.RetainedTextCharacters =>
                BrowserLibraryApiDiffTruncationLimit.RetainedTextCharacters,
            _ => throw new ArgumentOutOfRangeException(nameof(limit)),
        };

    static BrowserLibraryApiDiffOpenFailureKind Project(CandidateOpenFailureKind kind) =>
        kind switch
        {
            CandidateOpenFailureKind.Unreadable => BrowserLibraryApiDiffOpenFailureKind.Unreadable,
            CandidateOpenFailureKind.InvalidImage =>
                BrowserLibraryApiDiffOpenFailureKind.InvalidImage,
            CandidateOpenFailureKind.ResourceBudget =>
                BrowserLibraryApiDiffOpenFailureKind.ResourceBudget,
            CandidateOpenFailureKind.UnsupportedMetadataFormat =>
                BrowserLibraryApiDiffOpenFailureKind.UnsupportedMetadataFormat,
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };

    static BrowserLibraryApiDiffMetadataRootReason Project(MetadataRootMalformedReason reason) =>
        reason switch
        {
            MetadataRootMalformedReason.UnmappableMetadataDirectory =>
                BrowserLibraryApiDiffMetadataRootReason.UnmappableMetadataDirectory,
            MetadataRootMalformedReason.TruncatedFixedPrefix =>
                BrowserLibraryApiDiffMetadataRootReason.TruncatedFixedPrefix,
            MetadataRootMalformedReason.InvalidSignature =>
                BrowserLibraryApiDiffMetadataRootReason.InvalidSignature,
            MetadataRootMalformedReason.InvalidVersionLength =>
                BrowserLibraryApiDiffMetadataRootReason.InvalidVersionLength,
            MetadataRootMalformedReason.TruncatedVersionField =>
                BrowserLibraryApiDiffMetadataRootReason.TruncatedVersionField,
            MetadataRootMalformedReason.MissingVersionTerminator =>
                BrowserLibraryApiDiffMetadataRootReason.MissingVersionTerminator,
            _ => throw new ArgumentOutOfRangeException(nameof(reason)),
        };

    static BrowserLibraryApiDiffUnavailableKind Project(LibraryApiDiffUnavailableKind kind) =>
        kind switch
        {
            LibraryApiDiffUnavailableKind.BeforeIncomplete =>
                BrowserLibraryApiDiffUnavailableKind.BeforeIncomplete,
            LibraryApiDiffUnavailableKind.AfterIncomplete =>
                BrowserLibraryApiDiffUnavailableKind.AfterIncomplete,
            LibraryApiDiffUnavailableKind.BothIncomplete =>
                BrowserLibraryApiDiffUnavailableKind.BothIncomplete,
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };

    static BrowserLibraryApiDiffRejectionKind Project(LibraryApiDiffRejectionKind kind) =>
        kind switch
        {
            LibraryApiDiffRejectionKind.LogicalLibraryMismatch =>
                BrowserLibraryApiDiffRejectionKind.LogicalLibraryMismatch,
            LibraryApiDiffRejectionKind.FindingComparisonFailed =>
                BrowserLibraryApiDiffRejectionKind.FindingComparisonFailed,
            LibraryApiDiffRejectionKind.CompatibilityInspectionFailed =>
                BrowserLibraryApiDiffRejectionKind.CompatibilityInspectionFailed,
            LibraryApiDiffRejectionKind.MissingExactTypeIdentity =>
                BrowserLibraryApiDiffRejectionKind.MissingExactTypeIdentity,
            LibraryApiDiffRejectionKind.MissingMemberAnchor =>
                BrowserLibraryApiDiffRejectionKind.MissingMemberAnchor,
            LibraryApiDiffRejectionKind.DuplicateExactTypeIdentity =>
                BrowserLibraryApiDiffRejectionKind.DuplicateExactTypeIdentity,
            LibraryApiDiffRejectionKind.UnassociatedStructuredSubject =>
                BrowserLibraryApiDiffRejectionKind.UnassociatedStructuredSubject,
            LibraryApiDiffRejectionKind.ContradictoryOccupiedSideTopology =>
                BrowserLibraryApiDiffRejectionKind.ContradictoryOccupiedSideTopology,
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };

    static BrowserLibraryApiDiffSummary Project(LibraryApiDiffSummary summary) =>
        new(
            summary.ChangedTypeCount,
            summary.AddedTypeCount,
            summary.RemovedTypeCount,
            summary.ChangedMemberCount,
            summary.BreakingCount,
            summary.AdditiveCount,
            summary.PotentiallyBreakingCount);

    static BrowserLibraryApiDiffDocument Project(ComparisonDocument<LibraryApiTypeDiff> document) =>
        new(
            document.Identifier,
            document.Display,
            [.. document.Subjects.Select(Project)]);

    static BrowserLibraryApiTypeSubject Project(ComparisonSubject<LibraryApiTypeDiff> subject) =>
        new(
            subject.Identifier,
            subject.Display,
            Project(subject.Change),
            Project(subject.Comparison));

    static BrowserLibraryApiSubjectChangeKind Project(ComparisonSubjectChange change) =>
        change switch
        {
            ComparisonSubjectChange.Diff => BrowserLibraryApiSubjectChangeKind.Diff,
            ComparisonSubjectChange.Addition => BrowserLibraryApiSubjectChangeKind.Addition,
            ComparisonSubjectChange.Deletion => BrowserLibraryApiSubjectChangeKind.Deletion,
            _ => throw new InvalidOperationException(
                "Unknown Library API diff subject change."),
        };

    static BrowserLibraryApiTypeDiff Project(LibraryApiTypeDiff type) =>
        new(
            type.Before is { } before ? Project(before) : null,
            type.After is { } after ? Project(after) : null,
            Project(type.PairKind),
            type.TypeDefinitionChanged,
            [.. type.CompatibilityChanges.Select(Project)],
            [.. type.Members.Select(Project)],
            type.BreakingCount,
            type.AdditiveCount,
            type.PotentiallyBreakingCount,
            type.ChangedMemberCount);

    static BrowserLibraryApiTypePairKind Project(LibraryApiTypePairKind kind) =>
        kind switch
        {
            LibraryApiTypePairKind.Present => BrowserLibraryApiTypePairKind.Present,
            LibraryApiTypePairKind.Changed => BrowserLibraryApiTypePairKind.Changed,
            LibraryApiTypePairKind.Added => BrowserLibraryApiTypePairKind.Added,
            LibraryApiTypePairKind.Removed => BrowserLibraryApiTypePairKind.Removed,
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };

    static BrowserLibraryApiTypeIdentity Project(LibraryApiTypeIdentity identity) =>
        new(identity.Identifier, identity.Display);

    static BrowserLibraryApiMemberIdentity Project(LibraryApiMemberIdentity identity) =>
        new(Project(identity.DeclaringType), Project(identity.Anchor), identity.Display);

    static BrowserLibraryApiMemberAnchor Project(MemberAnchor anchor) =>
        new(
            anchor.StableSelector,
            anchor.CanonicalSignature,
            anchor.Fingerprint,
            anchor.TypeFullName,
            anchor.MemberName);

    static BrowserLibraryApiMemberDiff Project(LibraryApiMemberDiff member) =>
        new(Project(member.Relation), Project(member.Role));

    static BrowserLibraryApiMemberRelationRole Project(LibraryApiMemberRelationRole role) =>
        role switch
        {
            LibraryApiMemberRelationRole.Before => BrowserLibraryApiMemberRelationRole.Before,
            LibraryApiMemberRelationRole.After => BrowserLibraryApiMemberRelationRole.After,
            LibraryApiMemberRelationRole.Both => BrowserLibraryApiMemberRelationRole.Both,
            _ => throw new ArgumentOutOfRangeException(nameof(role)),
        };

    static BrowserLibraryApiMemberRelation Project(LibraryApiMemberRelation relation) =>
        new(
            relation.Identifier,
            Project(relation.PairKind),
            relation.Before is { } before ? Project(before) : null,
            relation.After is { } after ? Project(after) : null,
            relation.Match is { } match ? Project(match) : null);

    static BrowserLibraryApiMemberPairKind Project(LibraryApiMemberPairKind kind) =>
        kind switch
        {
            LibraryApiMemberPairKind.Changed => BrowserLibraryApiMemberPairKind.Changed,
            LibraryApiMemberPairKind.Added => BrowserLibraryApiMemberPairKind.Added,
            LibraryApiMemberPairKind.Removed => BrowserLibraryApiMemberPairKind.Removed,
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };

    static BrowserLibraryApiMatchProvenance Project(FindingMatchProvenance match) =>
        new(match.Tier.Id, match.Tier.Confidence, match.Confidence);

    static BrowserLibraryApiCompatibilityChange Project(LibraryApiCompatibilityChange change) =>
        new(
            Project(change.Kind),
            Project(change.Classification),
            Project(change.Category),
            change.Message.ToString(),
            change.OldValue is { } oldValue ? oldValue.ToString() : null,
            change.NewValue is { } newValue ? newValue.ToString() : null,
            Project(change.Subject));

    static BrowserLibraryApiChangeKind Project(ChangeKind kind) =>
        kind switch
        {
            ChangeKind.TypeAdded => BrowserLibraryApiChangeKind.TypeAdded,
            ChangeKind.TypeRemoved => BrowserLibraryApiChangeKind.TypeRemoved,
            ChangeKind.TypeKindChanged => BrowserLibraryApiChangeKind.TypeKindChanged,
            ChangeKind.SealedAdded => BrowserLibraryApiChangeKind.SealedAdded,
            ChangeKind.SealedRemoved => BrowserLibraryApiChangeKind.SealedRemoved,
            ChangeKind.AbstractAdded => BrowserLibraryApiChangeKind.AbstractAdded,
            ChangeKind.AbstractRemoved => BrowserLibraryApiChangeKind.AbstractRemoved,
            ChangeKind.BaseTypeChanged => BrowserLibraryApiChangeKind.BaseTypeChanged,
            ChangeKind.InterfaceAdded => BrowserLibraryApiChangeKind.InterfaceAdded,
            ChangeKind.InterfaceRemoved => BrowserLibraryApiChangeKind.InterfaceRemoved,
            ChangeKind.TypeParameterCountChanged =>
                BrowserLibraryApiChangeKind.TypeParameterCountChanged,
            ChangeKind.TypeParameterVarianceChanged =>
                BrowserLibraryApiChangeKind.TypeParameterVarianceChanged,
            ChangeKind.TypeParameterConstraintTightened =>
                BrowserLibraryApiChangeKind.TypeParameterConstraintTightened,
            ChangeKind.TypeParameterConstraintLoosened =>
                BrowserLibraryApiChangeKind.TypeParameterConstraintLoosened,
            ChangeKind.MemberAdded => BrowserLibraryApiChangeKind.MemberAdded,
            ChangeKind.MemberRemoved => BrowserLibraryApiChangeKind.MemberRemoved,
            ChangeKind.MemberSignatureChanged =>
                BrowserLibraryApiChangeKind.MemberSignatureChanged,
            ChangeKind.VirtualRemoved => BrowserLibraryApiChangeKind.VirtualRemoved,
            ChangeKind.AbstractMemberAdded => BrowserLibraryApiChangeKind.AbstractMemberAdded,
            ChangeKind.EnumValueChanged => BrowserLibraryApiChangeKind.EnumValueChanged,
            ChangeKind.TypeAttributeAdded => BrowserLibraryApiChangeKind.TypeAttributeAdded,
            ChangeKind.TypeAttributeRemoved => BrowserLibraryApiChangeKind.TypeAttributeRemoved,
            ChangeKind.MemberAttributeAdded => BrowserLibraryApiChangeKind.MemberAttributeAdded,
            ChangeKind.MemberAttributeRemoved =>
                BrowserLibraryApiChangeKind.MemberAttributeRemoved,
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };

    static BrowserLibraryApiChangeClassification Project(ChangeClassification classification) =>
        classification switch
        {
            ChangeClassification.Additive => BrowserLibraryApiChangeClassification.Additive,
            ChangeClassification.Breaking => BrowserLibraryApiChangeClassification.Breaking,
            ChangeClassification.PotentiallyBreaking =>
                BrowserLibraryApiChangeClassification.PotentiallyBreaking,
            _ => throw new ArgumentOutOfRangeException(nameof(classification)),
        };

    static BrowserLibraryApiChangeCategory Project(ApiChangeCategory category) =>
        category switch
        {
            ApiChangeCategory.Signature => BrowserLibraryApiChangeCategory.Signature,
            ApiChangeCategory.Attribute => BrowserLibraryApiChangeCategory.Attribute,
            _ => throw new ArgumentOutOfRangeException(nameof(category)),
        };

    static BrowserLibraryApiChangeSubject Project(LibraryApiChangeSubject subject) =>
        new(
            Project(subject.Kind),
            subject.BeforeType is { } beforeType ? Project(beforeType) : null,
            subject.AfterType is { } afterType ? Project(afterType) : null,
            subject.BeforeMember is { } beforeMember ? Project(beforeMember) : null,
            subject.AfterMember is { } afterMember ? Project(afterMember) : null);

    static BrowserLibraryApiChangeSubjectKind Project(ApiChangeSubjectKind kind) =>
        kind switch
        {
            ApiChangeSubjectKind.Type => BrowserLibraryApiChangeSubjectKind.Type,
            ApiChangeSubjectKind.Member => BrowserLibraryApiChangeSubjectKind.Member,
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
}
