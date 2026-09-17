using ILInspector.Metadata;
using ILInspector.MetadataPrimitives;

namespace DotnetInspector.Queries;

/// <summary>
/// Resource-free coordinate evidence for one Type or Member declaration.
/// The optional declaration is physical evidence, not reopening authority.
/// </summary>
public sealed class ApiCoordinateDeclarationEvidence
{
    internal ApiCoordinateDeclarationEvidence(
        CoordinateApiLibraryEvidence library,
        MetadataTypeDefinitionName declaringType,
        ApiDeclarationKind kind,
        MemberAnchor? member,
        ApiDeclarationReference? declaration)
    {
        Library = library;
        DeclaringType = declaringType;
        Kind = kind;
        Member = member;
        Declaration = declaration;
    }

    public CoordinateApiLibraryEvidence Library { get; }
    public MetadataTypeDefinitionName DeclaringType { get; }
    public ApiDeclarationKind Kind { get; }
    public MemberAnchor? Member { get; }
    public ApiDeclarationReference? Declaration { get; }
}

/// <summary>
/// Resource-free projection of one completed API coordinate correspondence.
/// <see cref="ApiCoordinateCorrespondenceStatus.Absent"/> is an owner-issued
/// complete destination-subject absence classification.
/// </summary>
public sealed class ApiCoordinateCorrespondenceEvidence
{
    internal ApiCoordinateCorrespondenceEvidence(
        ApiCoordinateCorrespondenceStatus status,
        ApiCoordinateDeclarationEvidence source,
        CoordinateLibraryPairingEvidence libraryPairing,
        ApiDeclarationBindingResult? sourceBinding,
        CoordinateTypeResolutionEvidence? resolution,
        ApiDeclarationCorrespondenceResult? correspondence,
        ApiCoordinateDeclarationEvidence? destination,
        ApiCoordinateCorrespondenceFailure? failure)
    {
        Status = status;
        Source = source;
        LibraryPairing = libraryPairing;
        SourceBinding = sourceBinding;
        Resolution = resolution;
        Correspondence = correspondence;
        Destination = destination;
        Failure = failure;
    }

    public ApiCoordinateCorrespondenceStatus Status { get; }
    public ApiCoordinateDeclarationEvidence Source { get; }
    public CoordinateLibraryPairingEvidence LibraryPairing { get; }
    public ApiDeclarationBindingResult? SourceBinding { get; }
    public CoordinateTypeResolutionEvidence? Resolution { get; }
    public ApiDeclarationCorrespondenceResult? Correspondence { get; }
    public ApiCoordinateDeclarationEvidence? Destination { get; }
    public ApiCoordinateCorrespondenceFailure? Failure { get; }

    public bool IsCompleteDestinationAbsence =>
        Status == ApiCoordinateCorrespondenceStatus.Absent;
}

static class ApiCoordinateCorrespondenceEvidenceProjector
{
    internal static ApiCoordinateCorrespondenceEvidence Project(
        ApiCoordinateCorrespondenceResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        CoordinateLibraryPairingEvidence pairing = result.LibraryPairing.Detach();
        ApiCoordinateDeclarationEvidence source =
            ApiCoordinateDeclarationEvidenceProjector.Project(
            result.Source,
            result.SourceKind,
            result.LibraryPairing.Before,
            result.SourceBinding?.Declaration);
        ApiCoordinateDeclarationEvidence? destination =
            result.Destination is { } destinationSubject
            && result.Correspondence?.Target is { } target
                ? ApiCoordinateDeclarationEvidenceProjector.Project(
                    destinationSubject,
                    target.Kind,
                    result.LibraryPairing.After,
                    target)
                : null;

        return new(
            result.Status,
            source,
            pairing,
            result.SourceBinding,
            result.Resolution,
            result.Correspondence,
            destination,
            result.Failure);
    }

}

static class ApiCoordinateDeclarationEvidenceProjector
{
    internal static ApiCoordinateDeclarationEvidence Project(
        StructuralSubjectIdentity subject,
        ApiDeclarationKind kind,
        CoordinatePackageObservation observation,
        ApiDeclarationReference? declaration)
    {
        (MetadataTypeDefinitionName declaringType, MemberAnchor? member) =
            subject switch
            {
                StructuralSubjectIdentity.TypeSubject type =>
                    (type.Identity.Type, null),
                StructuralSubjectIdentity.MemberSubject selected =>
                    (selected.Identity.DeclaringType, selected.Identity.Member),
                _ => throw new InvalidOperationException(
                    "An API coordinate must identify a Type or Member."),
            };
        return new(
            FindLibrary(subject, observation),
            declaringType,
            kind,
            member,
            declaration);
    }

    internal static ApiCoordinateDeclarationEvidence Project(
        StructuralSubjectIdentity subject,
        ApiDeclarationKind kind,
        CoordinatePackageObservation observation) =>
        Project(subject, kind, observation, null);

    static CoordinateApiLibraryEvidence FindLibrary(
        StructuralSubjectIdentity subject,
        CoordinatePackageObservation observation)
    {
        StructuralSubjectIdentity.LibrarySubject library = subject switch
        {
            StructuralSubjectIdentity.TypeSubject type => type.Library,
            StructuralSubjectIdentity.MemberSubject member =>
                member.DeclaringType.Library,
            _ => throw new InvalidOperationException(
                "An API coordinate must identify a Type or Member."),
        };
        CoordinateApiLibraryObservation? selected =
            observation.Libraries.FirstOrDefault(candidate =>
                ReferenceEquals(
                    candidate.Subject.Identity.Registration,
                    library.Identity.Registration));
        return selected?.Detach()
            ?? throw new InvalidOperationException(
                "An exact destination declaration must identify its selected Package Library.");
    }
}
