using System.Collections.Immutable;
using ILInspector.Metadata;
using ILInspector.MetadataPrimitives;

namespace DotnetInspector.Queries;

/// <summary>Resource-free evidence for one completed source selection.</summary>
public sealed class ApiCoordinateSourceSelectionEvidence
{
    internal ApiCoordinateSourceSelectionEvidence(
        ApiCoordinateSourceSelectionStatus status,
        ApiCoordinateDeclarationEvidence? selected,
        ImmutableArray<ApiCoordinateDeclarationEvidence> typeCandidates,
        ImmutableArray<MemberAnchor> memberCandidates,
        ApiCoordinateSourceSelectionFailure? failure,
        ImmutableArray<ExactTypeApiInspectionFailure> inspectionFailures,
        MemberTargetKind? memberKind)
    {
        Status = status;
        Selected = selected;
        TypeCandidates = typeCandidates;
        MemberCandidates = memberCandidates;
        Failure = failure;
        InspectionFailures = inspectionFailures;
        MemberKind = memberKind;
    }

    public ApiCoordinateSourceSelectionStatus Status { get; }
    public ApiCoordinateDeclarationEvidence? Selected { get; }
    public ImmutableArray<ApiCoordinateDeclarationEvidence> TypeCandidates { get; }
    public ImmutableArray<MemberAnchor> MemberCandidates { get; }
    public ApiCoordinateSourceSelectionFailure? Failure { get; }
    public ImmutableArray<ExactTypeApiInspectionFailure> InspectionFailures { get; }
    public MemberTargetKind? MemberKind { get; }
}

static class ApiCoordinateSourceSelectionEvidenceProjector
{
    internal static ApiCoordinateSourceSelectionEvidence Project(
        ApiCoordinateSourceSelectionResult result,
        CoordinatePackageObservation source)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(source);

        ApiCoordinateDeclarationEvidence? selected = result.Subject is { } subject
            ? ApiCoordinateDeclarationEvidenceProjector.Project(
                subject,
                subject is StructuralSubjectIdentity.MemberSubject
                    ? ApiDeclarationKindClassifier.FromMemberTarget(result.MemberKind)
                    : ApiDeclarationKind.Type,
                source)
            : null;
        ImmutableArray<ApiCoordinateDeclarationEvidence> candidates =
        [
            .. result.Candidates.Select(candidate =>
                ApiCoordinateDeclarationEvidenceProjector.Project(
                    candidate,
                    ApiDeclarationKind.Type,
                    source)),
        ];

        return new(
            result.Status,
            selected,
            candidates,
            result.MemberCandidates,
            result.Failure,
            result.InspectionFailures,
            result.MemberKind);
    }
}

static class ApiDeclarationKindClassifier
{
    internal static ApiDeclarationKind FromMemberTarget(
        MemberTargetKind? kind) => kind switch
    {
        MemberTargetKind.Field => ApiDeclarationKind.Field,
        MemberTargetKind.Property => ApiDeclarationKind.Property,
        MemberTargetKind.Event => ApiDeclarationKind.Event,
        MemberTargetKind.Constructor or MemberTargetKind.Finalizer
            or MemberTargetKind.Method or MemberTargetKind.Operator
            or MemberTargetKind.ExplicitInterfaceImplementation
            or MemberTargetKind.ExtensionMethod => ApiDeclarationKind.Method,
        _ => throw new InvalidOperationException(
            "The selected Member has no supported declaration kind."),
    };
}
