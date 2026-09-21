using System.Diagnostics.CodeAnalysis;

using ILInspector.Metadata;

namespace DotnetInspector.Queries;

public enum ApiCoordinateCorrespondenceStatus
{
    Exact,
    Absent,
    Ambiguous,
    Refused,
    Failed,
}

public enum ApiCoordinateCorrespondenceFailureKind
{
    ForeignWorkspace,
    InvalidSourceAssociation,
    SourceRootUnavailable,
    DestinationRootUnavailable,
    SourcePopulationUnavailable,
    DestinationPopulationUnavailable,
    SourceImageUnavailable,
    TerminalOutsideDestinationPopulation,
    TerminalAssociationMismatch,
}

public sealed record ApiCoordinateCorrespondenceFailure(
    ApiCoordinateCorrespondenceFailureKind Kind,
    string Detail,
    ArtifactRootFailure? RootFailure = null,
    CandidateOpenFailure? ImageFailure = null);

/// <summary>
/// Live result for one source declaration through an exact destination entry
/// Library and, when forwarded, its actual defining Library.
/// </summary>
public sealed class ApiCoordinateCorrespondenceResult
{
    internal ApiCoordinateCorrespondenceResult(
        ApiCoordinateCorrespondenceStatus status,
        StructuralSubjectIdentity source,
        ApiDeclarationKind sourceKind,
        CoordinateLibraryPairingResult libraryPairing,
        ApiDeclarationBindingResult? sourceBinding = null,
        CoordinateTypeResolutionEvidence? resolution = null,
        ApiDeclarationCorrespondenceResult? correspondence = null,
        StructuralSubjectIdentity? destination = null,
        ApiCoordinateCorrespondenceFailure? failure = null)
    {
        Status = status;
        Source = source;
        SourceKind = sourceKind;
        LibraryPairing = libraryPairing;
        SourceBinding = sourceBinding;
        Resolution = resolution;
        Correspondence = correspondence;
        Destination = destination;
        Failure = failure;
    }

    public ApiCoordinateCorrespondenceStatus Status { get; }
    public StructuralSubjectIdentity Source { get; }
    public ApiDeclarationKind SourceKind { get; }
    public CoordinateLibraryPairingResult LibraryPairing { get; }
    public ApiDeclarationBindingResult? SourceBinding { get; }
    public CoordinateTypeResolutionEvidence? Resolution { get; }
    public ApiDeclarationCorrespondenceResult? Correspondence { get; }
    public StructuralSubjectIdentity? Destination { get; }
    public ApiCoordinateCorrespondenceFailure? Failure { get; }

    /// <summary>
    /// Projects the completed result before either endpoint Workspace closes.
    /// The returned evidence retains no Workspace-local subject or Package
    /// observation.
    /// </summary>
    public ApiCoordinateCorrespondenceEvidence Detach() =>
        ApiCoordinateCorrespondenceEvidenceProjector.Project(this);
}

/// <summary>
/// Composes exact Library pairing, closed-world destination Type resolution,
/// and strict Metadata declaration correspondence.
/// </summary>
public static class ApiCoordinateCorrespondenceQuery
{
    public static ValueTask<ApiCoordinateCorrespondenceResult> ExecuteAsync(
        InspectionWorkspace workspace,
        StructuralSubjectIdentity.TypeSubject source,
        CoordinatePackageObservation before,
        CoordinatePackageObservation after,
        CancellationToken cancellationToken = default)
        => ExecuteAsync(
            workspace,
            workspace,
            source,
            before,
            after,
            cancellationToken);

    public static ValueTask<ApiCoordinateCorrespondenceResult> ExecuteAsync(
        InspectionWorkspace sourceWorkspace,
        InspectionWorkspace destinationWorkspace,
        StructuralSubjectIdentity.TypeSubject source,
        CoordinatePackageObservation before,
        CoordinatePackageObservation after,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        return ExecuteAsync(
            sourceWorkspace, destinationWorkspace,
            source, source.Library, source.Identity.Type,
            member: null, before, after, admittedSource: null,
            cancellationToken);
    }

    public static ValueTask<ApiCoordinateCorrespondenceResult> ExecuteAsync(
        InspectionWorkspace workspace,
        StructuralSubjectIdentity.MemberSubject source,
        ApiDeclarationKind sourceKind,
        CoordinatePackageObservation before,
        CoordinatePackageObservation after,
        CancellationToken cancellationToken = default)
        => ExecuteAsync(
            workspace,
            workspace,
            source,
            sourceKind,
            before,
            after,
            cancellationToken);

    public static ValueTask<ApiCoordinateCorrespondenceResult> ExecuteAsync(
        InspectionWorkspace sourceWorkspace,
        InspectionWorkspace destinationWorkspace,
        StructuralSubjectIdentity.MemberSubject source,
        ApiDeclarationKind sourceKind,
        CoordinatePackageObservation before,
        CoordinatePackageObservation after,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        return ExecuteAsync(
            sourceWorkspace, destinationWorkspace,
            source, source.DeclaringType.Library,
            source.Identity.DeclaringType,
            new ApiDeclarationMemberSelection(sourceKind, source.Identity.Member),
            before, after, admittedSource: null, cancellationToken);
    }

    internal static ValueTask<ApiCoordinateCorrespondenceResult>
        ExecuteAdmittedSourceAsync(
            InspectionWorkspace sourceWorkspace,
            InspectionWorkspace destinationWorkspace,
            StructuralSubjectIdentity.TypeSubject source,
            CoordinatePackageObservation before,
            PackageAssemblyContextRealization sourceRealization,
            CoordinatePackageObservation after,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(sourceRealization);
        return ExecuteAsync(
            sourceWorkspace,
            destinationWorkspace,
            source,
            source.Library,
            source.Identity.Type,
            member: null,
            before,
            after,
            sourceRealization,
            cancellationToken);
    }

    internal static ValueTask<ApiCoordinateCorrespondenceResult>
        ExecuteAdmittedSourceAsync(
            InspectionWorkspace sourceWorkspace,
            InspectionWorkspace destinationWorkspace,
            StructuralSubjectIdentity.MemberSubject source,
            ApiDeclarationKind sourceKind,
            CoordinatePackageObservation before,
            PackageAssemblyContextRealization sourceRealization,
            CoordinatePackageObservation after,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(sourceRealization);
        return ExecuteAsync(
            sourceWorkspace,
            destinationWorkspace,
            source,
            source.DeclaringType.Library,
            source.Identity.DeclaringType,
            new ApiDeclarationMemberSelection(
                sourceKind,
                source.Identity.Member),
            before,
            after,
            sourceRealization,
            cancellationToken);
    }

    static async ValueTask<ApiCoordinateCorrespondenceResult> ExecuteAsync(
        InspectionWorkspace sourceWorkspace,
        InspectionWorkspace destinationWorkspace,
        StructuralSubjectIdentity source,
        StructuralSubjectIdentity.LibrarySubject sourceLibrary,
        MetadataTypeDefinitionName declaringType,
        ApiDeclarationMemberSelection? member,
        CoordinatePackageObservation before,
        CoordinatePackageObservation after,
        PackageAssemblyContextRealization? admittedSource,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sourceWorkspace);
        ArgumentNullException.ThrowIfNull(destinationWorkspace);
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);
        cancellationToken.ThrowIfCancellationRequested();
        ApiDeclarationKind sourceKind = member?.Kind ?? ApiDeclarationKind.Type;

        CoordinateLibraryPairingResult pairing =
            CoordinateLibraryPairingQuery.Execute(sourceLibrary, before, after);
        if (!ReferenceEquals(
                sourceWorkspace.Identity,
                source.Workspace.Identity))
        {
            return Stop(
                ApiCoordinateCorrespondenceStatus.Refused,
                pairing,
                ApiCoordinateCorrespondenceFailureKind.ForeignWorkspace,
                "The source declaration belongs to a different Workspace.");
        }
        if (!ReferenceEquals(
                destinationWorkspace.Identity,
                after.Occurrence.Identity.WorkspaceIdentity))
        {
            return Stop(
                ApiCoordinateCorrespondenceStatus.Refused,
                pairing,
                ApiCoordinateCorrespondenceFailureKind.ForeignWorkspace,
                "The destination observation belongs to a different Workspace.");
        }
        if (pairing.Status is CoordinateLibraryPairingStatus.Refused
            or CoordinateLibraryPairingStatus.Failed)
        {
            return new(
                PairingStatus(pairing.Status),
                source,
                sourceKind,
                pairing);
        }

        if (admittedSource is not null)
        {
            return await ExecuteWithSourceAsync(
                admittedSource,
                cancellationToken).ConfigureAwait(false);
        }

        ArtifactRootResult<ApiCoordinateCorrespondenceResult> sourceAccess =
            await sourceWorkspace.ExecutePackageRootQueryAsync(
                before.Correspondence,
                before.Generation,
                ExecuteWithSourceAsync,
                before.BindingPolicy,
                cancellationToken).ConfigureAwait(false);

        return sourceAccess switch
        {
            ArtifactRootResult<ApiCoordinateCorrespondenceResult>.Available available =>
                available.Value,
            ArtifactRootResult<ApiCoordinateCorrespondenceResult>.Rejected rejected =>
                Stop(
                    ApiCoordinateCorrespondenceStatus.Failed,
                    pairing,
                    ApiCoordinateCorrespondenceFailureKind.SourceRootUnavailable,
                    "The exact source Package Root is unavailable.",
                    rootFailure: rejected.Failure),
            _ => throw new InvalidOperationException(
                "Unknown source Root access outcome."),
        };

        async ValueTask<ApiCoordinateCorrespondenceResult>
            ExecuteWithSourceAsync(
                PackageAssemblyContextRealization sourceRealization,
                CancellationToken token)
        {
            if (!TryGetParticipant(
                    sourceRealization,
                    before,
                    sourceLibrary.Identity.Registration,
                    out PackageAssemblyRoleParticipant? sourceParticipant)
                || sourceParticipant is null)
            {
                return Stop(
                    ApiCoordinateCorrespondenceStatus.Failed,
                    pairing,
                    ApiCoordinateCorrespondenceFailureKind
                        .SourcePopulationUnavailable,
                    "The exact source Library is unavailable in the pinned source API population.");
            }

            AssemblyImageAccessResult<ResolvedAssemblyReference> sourceImage =
                sourceRealization.SurfaceGroup.RetainAssemblyReference(
                    sourceParticipant.Participant.Assembly);
            if (sourceImage is AssemblyImageAccessResult<
                    ResolvedAssemblyReference>.Rejected sourceRejected)
            {
                return Stop(
                    ApiCoordinateCorrespondenceStatus.Failed,
                    pairing,
                    ApiCoordinateCorrespondenceFailureKind
                        .SourceImageUnavailable,
                    "The exact source Library image is unavailable.",
                    imageFailure: sourceRejected.Failure);
            }
            if (sourceImage is not AssemblyImageAccessResult<
                    ResolvedAssemblyReference>.Available sourceAvailable)
            {
                throw new InvalidOperationException(
                    "Unknown source assembly image-access outcome.");
            }

            ApiDeclarationBindingResult binding =
                ApiDeclarationCorrespondence.BindSource(
                    sourceAvailable.Value,
                    declaringType,
                    member,
                    token);
            if (!binding.IsExact)
            {
                return new(
                    SourceBindingStatus(binding.Status),
                    source,
                    sourceKind,
                    pairing,
                    sourceBinding: binding);
            }
            if (binding.Declaration is not { } sourceDeclaration)
            {
                return Stop(
                    ApiCoordinateCorrespondenceStatus.Failed,
                    pairing,
                    ApiCoordinateCorrespondenceFailureKind
                        .InvalidSourceAssociation,
                    "The exact source binding did not identify a declaration.",
                    sourceBinding: binding);
            }
            if (pairing.Status != CoordinateLibraryPairingStatus.Exact)
            {
                return new(
                    PairingStatus(pairing.Status),
                    source,
                    sourceKind,
                    pairing,
                    sourceBinding: binding);
            }

            ArtifactRootResult<ApiCoordinateCorrespondenceResult>
                destinationAccess =
                await destinationWorkspace.ExecutePackageRootQueryAsync(
                    after.Correspondence,
                    after.Generation,
                    (destinationRealization, innerToken) =>
                        ValueTask.FromResult(ExecutePinned(
                            source,
                            sourceKind,
                            declaringType,
                            pairing,
                            binding,
                            sourceDeclaration,
                            sourceAvailable.Value,
                            destinationRealization,
                            after,
                            innerToken)),
                    after.BindingPolicy,
                    token).ConfigureAwait(false);
            return destinationAccess switch
            {
                ArtifactRootResult<
                    ApiCoordinateCorrespondenceResult>.Available available =>
                    available.Value,
                ArtifactRootResult<
                    ApiCoordinateCorrespondenceResult>.Rejected rejected =>
                    Stop(
                        ApiCoordinateCorrespondenceStatus.Failed,
                        pairing,
                        ApiCoordinateCorrespondenceFailureKind
                            .DestinationRootUnavailable,
                        "The exact destination Package Root is unavailable.",
                        sourceBinding: binding,
                        rootFailure: rejected.Failure),
                _ => throw new InvalidOperationException(
                    "Unknown destination Root access outcome."),
            };
        }

        ApiCoordinateCorrespondenceResult Stop(
            ApiCoordinateCorrespondenceStatus status,
            CoordinateLibraryPairingResult libraryPairing,
            ApiCoordinateCorrespondenceFailureKind kind,
            string detail,
            ApiDeclarationBindingResult? sourceBinding = null,
            ArtifactRootFailure? rootFailure = null,
            CandidateOpenFailure? imageFailure = null) =>
            new(
                status,
                source,
                sourceKind,
                libraryPairing,
                sourceBinding: sourceBinding,
                failure: new(
                    kind, detail, rootFailure, imageFailure));
    }

    static ApiCoordinateCorrespondenceResult ExecutePinned(
        StructuralSubjectIdentity source,
        ApiDeclarationKind sourceKind,
        MetadataTypeDefinitionName declaringType,
        CoordinateLibraryPairingResult pairing,
        ApiDeclarationBindingResult sourceBinding,
        ApiDeclarationReference sourceDeclaration,
        ResolvedAssemblyReference sourceAssembly,
        PackageAssemblyContextRealization destinationRealization,
        CoordinatePackageObservation after,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        CoordinateApiLibraryObservation entry =
            pairing.Destination
            ?? throw new InvalidOperationException(
                "An exact Library pairing has no destination.");
        if (!TryGetParticipant(
                destinationRealization,
                after,
                entry.Subject.Identity.Registration,
                out PackageAssemblyRoleParticipant? entryParticipant)
            || entryParticipant is null)
        {
            return Stop(
                ApiCoordinateCorrespondenceStatus.Failed,
                ApiCoordinateCorrespondenceFailureKind
                    .DestinationPopulationUnavailable,
                "The exact destination entry Library is unavailable in the pinned API population.");
        }

        AssemblyContextTypeResolutionResult resolution =
            AssemblyContextTypeResolutionQuery.Execute(
                destinationRealization.SurfaceGroup,
                entryParticipant.Participant,
                declaringType,
                AssemblyResolutionScope.Any);
        CoordinateTypeResolutionEvidence detached =
            CoordinateTypeResolutionEvidence.Project(resolution, after);

        if (resolution is AssemblyContextTypeResolutionResult.Rejected)
        {
            return new(
                ApiCoordinateCorrespondenceStatus.Failed,
                source,
                sourceKind,
                pairing,
                sourceBinding,
                detached);
        }
        if (resolution is
            AssemblyContextTypeResolutionResult.UnsupportedBindingPolicy)
        {
            return new(
                ApiCoordinateCorrespondenceStatus.Refused,
                source,
                sourceKind,
                pairing,
                sourceBinding,
                detached);
        }
        TypeResolutionOutcome outcome =
            ((AssemblyContextTypeResolutionResult.Available)resolution).Outcome;
        if (outcome is not TypeResolutionOutcome.Resolved resolved)
        {
            return new(
                ResolutionStatus(outcome),
                source,
                sourceKind,
                pairing,
                sourceBinding,
                detached);
        }

        ResolvedAssemblyReference destinationAssembly =
            resolved.Definition.Assembly.Assembly;
        NavigationRegistrationIdentity terminalRegistration =
            NavigationRegistrationIdentity.From(
                destinationAssembly.Registration);
        CoordinateApiLibraryObservation? definingLibrary =
            after.Libraries.FirstOrDefault(candidate =>
                ReferenceEquals(
                    candidate.Subject.Identity.Registration,
                    terminalRegistration));
        PackageAssemblyRoleParticipant? definingParticipant =
            destinationRealization.SurfaceParticipants.FirstOrDefault(candidate =>
                ReferenceEquals(
                    candidate.Participant.Assembly.Registration,
                    destinationAssembly.Registration));
        if (definingLibrary is null
            || definingParticipant is null
            || !ReferenceEquals(
                definingLibrary.Asset,
                definingParticipant.Asset))
        {
            return Stop(
                ApiCoordinateCorrespondenceStatus.Refused,
                ApiCoordinateCorrespondenceFailureKind
                    .TerminalOutsideDestinationPopulation,
                "The resolved terminal is not the exact selected API Library in the destination Package.",
                detached);
        }

        ApiDeclarationCorrespondenceResult correspondence =
            ApiDeclarationCorrespondence.Match(
                sourceAssembly,
                sourceDeclaration,
                destinationAssembly,
                cancellationToken);
        if (correspondence.Target is not { } target)
        {
            return new(
                DeclarationStatus(correspondence.Status),
                source,
                sourceKind,
                pairing,
                sourceBinding,
                detached,
                correspondence);
        }

        if (correspondence.Destination is not { } destinationEndpoint
            || !destinationEndpoint.Registration.Matches(
                destinationAssembly.Registration)
            || target.DeclaringType != resolved.Definition.Type
            || target.Location.ModuleVersionId
                != resolved.Definition.Address.ModuleVersionId
            || source is StructuralSubjectIdentity.TypeSubject
                && target.Location.TypeAddress
                    != resolved.Definition.Address
            || source is StructuralSubjectIdentity.TypeSubject
                && target.Member is not null
            || source is StructuralSubjectIdentity.MemberSubject
                && target.Member is null)
        {
            return Stop(
                ApiCoordinateCorrespondenceStatus.Failed,
                ApiCoordinateCorrespondenceFailureKind
                    .TerminalAssociationMismatch,
                "Strict correspondence did not identify the resolved terminal Type in the exact destination image.",
                detached,
                correspondence);
        }

        StructuralSubjectIdentity destination =
            source switch
            {
                StructuralSubjectIdentity.TypeSubject =>
                    StructuralSubjectIdentity.ForType(
                        definingLibrary.Subject,
                        target.DeclaringType),
                StructuralSubjectIdentity.MemberSubject =>
                    StructuralSubjectIdentity.ForMember(
                        StructuralSubjectIdentity.ForType(
                            definingLibrary.Subject,
                            target.DeclaringType),
                        target.Member!),
                _ => throw new InvalidOperationException(
                    "Unknown coordinate source declaration kind."),
            };
        return new(
            ApiCoordinateCorrespondenceStatus.Exact,
            source,
            sourceKind,
            pairing,
            sourceBinding,
            detached,
            correspondence,
            destination);

        ApiCoordinateCorrespondenceResult Stop(
            ApiCoordinateCorrespondenceStatus status,
            ApiCoordinateCorrespondenceFailureKind kind,
            string detail,
            CoordinateTypeResolutionEvidence? resolutionEvidence = null,
            ApiDeclarationCorrespondenceResult? correspondenceEvidence = null) =>
            new(
                status,
                source,
                sourceKind,
                pairing,
                sourceBinding,
                resolutionEvidence,
                correspondenceEvidence,
                failure: new(kind, detail));
    }

    static bool TryGetParticipant(
        PackageAssemblyContextRealization realization,
        CoordinatePackageObservation observation,
        NavigationRegistrationIdentity registration,
        [NotNullWhen(true)] out PackageAssemblyRoleParticipant? participant)
    {
        participant = null;
        if (!realization.HasAssemblyContexts
            || realization.SurfaceParticipants.Length
                != observation.Libraries.Length)
        {
            return false;
        }

        foreach (PackageAssemblyRoleParticipant current
            in realization.SurfaceParticipants)
        {
            NavigationRegistrationIdentity currentRegistration =
                NavigationRegistrationIdentity.From(
                    current.Participant.Assembly.Registration);
            CoordinateApiLibraryObservation? library =
                observation.Libraries.FirstOrDefault(candidate =>
                    ReferenceEquals(
                        candidate.Subject.Identity.Registration,
                        currentRegistration));
            if (library is null || !ReferenceEquals(library.Asset, current.Asset))
                return false;
            if (ReferenceEquals(currentRegistration, registration))
                participant = current;
        }
        return participant is not null;
    }

    static ApiCoordinateCorrespondenceStatus PairingStatus(
        CoordinateLibraryPairingStatus status) =>
        status switch
        {
            CoordinateLibraryPairingStatus.Exact =>
                ApiCoordinateCorrespondenceStatus.Exact,
            CoordinateLibraryPairingStatus.Absent =>
                ApiCoordinateCorrespondenceStatus.Absent,
            CoordinateLibraryPairingStatus.Ambiguous =>
                ApiCoordinateCorrespondenceStatus.Ambiguous,
            CoordinateLibraryPairingStatus.Refused =>
                ApiCoordinateCorrespondenceStatus.Refused,
            CoordinateLibraryPairingStatus.Failed =>
                ApiCoordinateCorrespondenceStatus.Failed,
            _ => throw new InvalidOperationException(
                "Unknown Library-pairing status."),
        };

    static ApiCoordinateCorrespondenceStatus DeclarationStatus(
        ApiDeclarationCorrespondenceStatus status) =>
        status switch
        {
            ApiDeclarationCorrespondenceStatus.Exact =>
                ApiCoordinateCorrespondenceStatus.Exact,
            ApiDeclarationCorrespondenceStatus.Absent =>
                ApiCoordinateCorrespondenceStatus.Absent,
            ApiDeclarationCorrespondenceStatus.Ambiguous =>
                ApiCoordinateCorrespondenceStatus.Ambiguous,
            ApiDeclarationCorrespondenceStatus.Refused =>
                ApiCoordinateCorrespondenceStatus.Refused,
            ApiDeclarationCorrespondenceStatus.Failed =>
                ApiCoordinateCorrespondenceStatus.Failed,
            _ => throw new InvalidOperationException(
                "Unknown declaration-correspondence status."),
        };

    static ApiCoordinateCorrespondenceStatus SourceBindingStatus(
        ApiDeclarationCorrespondenceStatus status) =>
        status switch
        {
            ApiDeclarationCorrespondenceStatus.Exact =>
                ApiCoordinateCorrespondenceStatus.Exact,
            ApiDeclarationCorrespondenceStatus.Failed =>
                ApiCoordinateCorrespondenceStatus.Failed,
            ApiDeclarationCorrespondenceStatus.Absent
                or ApiDeclarationCorrespondenceStatus.Ambiguous
                or ApiDeclarationCorrespondenceStatus.Refused =>
                ApiCoordinateCorrespondenceStatus.Refused,
            _ => throw new InvalidOperationException(
                "Unknown source declaration-binding status."),
        };

    static ApiCoordinateCorrespondenceStatus ResolutionStatus(
        TypeResolutionOutcome outcome) =>
        outcome switch
        {
            TypeResolutionOutcome.NotFound notFound =>
                notFound.Hops.IsEmpty
                    ? ApiCoordinateCorrespondenceStatus.Absent
                    : ApiCoordinateCorrespondenceStatus.Failed,
            TypeResolutionOutcome.Ambiguous =>
                ApiCoordinateCorrespondenceStatus.Ambiguous,
            TypeResolutionOutcome.UnboundBinding =>
                ApiCoordinateCorrespondenceStatus.Refused,
            TypeResolutionOutcome.Unavailable =>
                ApiCoordinateCorrespondenceStatus.Failed,
            TypeResolutionOutcome.Rejected
            {
                Failure:
                    TypeResolutionFailure.UnsupportedModuleExport
                    or TypeResolutionFailure.UnsupportedModuleReference
                    or TypeResolutionFailure.InvalidBindingPolicy,
            } => ApiCoordinateCorrespondenceStatus.Refused,
            TypeResolutionOutcome.Rejected =>
                ApiCoordinateCorrespondenceStatus.Failed,
            TypeResolutionOutcome.Resolved =>
                ApiCoordinateCorrespondenceStatus.Exact,
            _ => throw new InvalidOperationException(
                "Unknown type-resolution outcome."),
        };
}
