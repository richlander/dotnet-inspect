using System.Collections.Immutable;
using DotnetInspector.Packages;
using ILInspector.Metadata;
using NuGetFetch;

namespace DotnetInspector.Queries;

public enum CoordinateLibraryPairingStatus
{
    Exact,
    Absent,
    Ambiguous,
    Refused,
    Failed,
}

public enum CoordinateLibraryPairingFailureKind
{
    ForeignWorkspace,
    InvalidEndpointAssociation,
    MissingProducer,
    DifferentProducer,
    DifferentPackage,
    UnsupportedSelection,
    IncompletePopulation,
    RootUnavailable,
    ImageUnavailable,
    SourceLibraryUnavailable,
}

public sealed record CoordinateLibraryPairingFailure(
    CoordinateLibraryPairingFailureKind Kind,
    string Detail,
    ArtifactRootFailure? RootFailure = null,
    CandidateOpenFailure? ImageFailure = null,
    PackageCompileAsset? Asset = null);

/// <summary>Resource-free evidence for one Package Library.</summary>
public sealed record CoordinateApiLibraryEvidence(
    WorkspacePackageDescriptor Package,
    PackageCompileAsset? Asset,
    NavigationAssemblyIdentity Assembly);

/// <summary>One selected API asset and its exact, resource-free Library identity.</summary>
public sealed class CoordinateApiLibraryObservation
{
    internal CoordinateApiLibraryObservation(
        StructuralSubjectIdentity.LibrarySubject subject,
        PackageCompileAsset asset,
        AssemblyReferenceIdentity assembly)
    {
        Subject = subject;
        Asset = asset;
        Assembly = assembly;
    }

    public StructuralSubjectIdentity.LibrarySubject Subject { get; }
    public PackageCompileAsset Asset { get; }
    public AssemblyReferenceIdentity Assembly { get; }

    internal CoordinateApiLibraryEvidence Detach() =>
        new(Subject.Package.Descriptor, Asset, Subject.Identity);
}

/// <summary>
/// Complete API population evidence for one exact observed Package occurrence.
/// This value contains no image opener and grants no access to a retired Root.
/// </summary>
public sealed class CoordinatePackageObservation
{
    internal CoordinatePackageObservation(
        WorkspacePackageOccurrence occurrence,
        PackageArtifactRootCorrespondence correspondence,
        ArtifactRootGenerationReference generation,
        PackageProducerIdentity producer,
        PackageContentGenerationIdentity contentGeneration,
        PackageRootSelectionIdentity selection,
        AssemblyBindingPolicyVersion? bindingPolicy,
        ImmutableArray<CoordinateApiLibraryObservation> libraries)
    {
        Occurrence = occurrence;
        Correspondence = correspondence;
        Generation = generation;
        Producer = producer;
        ContentGeneration = contentGeneration;
        Selection = selection;
        BindingPolicy = bindingPolicy;
        Libraries = libraries;
    }

    public WorkspacePackageOccurrence Occurrence { get; }
    public PackageArtifactRootCorrespondence Correspondence { get; }
    public ArtifactRootGenerationReference Generation { get; }
    public PackageContentGenerationIdentity ContentGeneration { get; }
    public PackageRootSelectionIdentity Selection { get; }
    public AssemblyBindingPolicyVersion? BindingPolicy { get; }
    public ImmutableArray<CoordinateApiLibraryObservation> Libraries { get; }
    internal PackageProducerIdentity Producer { get; }
}

public abstract record CoordinatePackageObservationResult
{
    private protected CoordinatePackageObservationResult() { }

    public sealed record Available(CoordinatePackageObservation Observation)
        : CoordinatePackageObservationResult;

    public sealed record Unavailable(CoordinateLibraryPairingFailure Failure)
        : CoordinatePackageObservationResult;
}

/// <summary>Directional Library pairing over two complete owner-issued observations.</summary>
public sealed class CoordinateLibraryPairingResult
{
    internal CoordinateLibraryPairingResult(
        StructuralSubjectIdentity.LibrarySubject source,
        CoordinatePackageObservation before,
        CoordinatePackageObservation after,
        CoordinateLibraryPairingStatus status,
        ImmutableArray<CoordinateApiLibraryObservation> candidates,
        CoordinateLibraryPairingFailure? failure = null)
    {
        Source = source;
        Before = before;
        After = after;
        Status = status;
        Candidates = candidates;
        Failure = failure;
    }

    public StructuralSubjectIdentity.LibrarySubject Source { get; }
    public CoordinatePackageObservation Before { get; }
    public CoordinatePackageObservation After { get; }
    public CoordinateLibraryPairingStatus Status { get; }
    public ImmutableArray<CoordinateApiLibraryObservation> Candidates { get; }
    public CoordinateLibraryPairingFailure? Failure { get; }
    public CoordinateApiLibraryObservation? Destination =>
        Status == CoordinateLibraryPairingStatus.Exact ? Candidates[0] : null;

    internal CoordinateLibraryPairingEvidence Detach()
    {
        CoordinateApiLibraryObservation? source =
            Before.Libraries.FirstOrDefault(candidate =>
                ReferenceEquals(
                    candidate.Subject.Identity.Registration,
                    Source.Identity.Registration));
        return new(
            new(
                Source.Package.Descriptor,
                source?.Asset,
                Source.Identity),
            Before.Occurrence.Package,
            After.Occurrence.Package,
            Status,
            [.. Candidates.Select(candidate => candidate.Detach())],
            Failure);
    }
}

/// <summary>
/// Resource-free directional Library-pairing evidence. It retains no
/// Workspace-local subject or Package observation.
/// </summary>
public sealed class CoordinateLibraryPairingEvidence
{
    internal CoordinateLibraryPairingEvidence(
        CoordinateApiLibraryEvidence source,
        WorkspacePackageDescriptor before,
        WorkspacePackageDescriptor after,
        CoordinateLibraryPairingStatus status,
        ImmutableArray<CoordinateApiLibraryEvidence> candidates,
        CoordinateLibraryPairingFailure? failure)
    {
        Source = source;
        Before = before;
        After = after;
        Status = status;
        Candidates = candidates;
        Failure = failure;
    }

    public CoordinateApiLibraryEvidence Source { get; }
    public WorkspacePackageDescriptor Before { get; }
    public WorkspacePackageDescriptor After { get; }
    public CoordinateLibraryPairingStatus Status { get; }
    public ImmutableArray<CoordinateApiLibraryEvidence> Candidates { get; }
    public CoordinateLibraryPairingFailure? Failure { get; }
    public CoordinateApiLibraryEvidence? Destination =>
        Status == CoordinateLibraryPairingStatus.Exact ? Candidates[0] : null;
}

public static class CoordinateLibraryPairingQuery
{
    /// <summary>
    /// Observes a complete selected API population through the existing scoped
    /// Root query boundary, retaining the exact binding and occurrence association.
    /// </summary>
    public static async ValueTask<CoordinatePackageObservationResult> ObserveAsync(
        InspectionWorkspace workspace,
        PackageRootBinding binding,
        WorkspacePackageOccurrenceDescriptor occurrence,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(binding);
        ArgumentNullException.ThrowIfNull(occurrence);
        cancellationToken.ThrowIfCancellationRequested();

        CoordinateLibraryPairingFailure? invalid =
            ValidateObservationEndpoint(
                workspace,
                binding,
                occurrence,
                out PackageArtifactRootCorrespondence? correspondence,
                out PackageProducerIdentity? producer,
                out ArtifactRootRealizationStatus.Ready? ready);
        if (invalid is not null)
            return new CoordinatePackageObservationResult.Unavailable(invalid);

        ArtifactRootResult<CoordinatePackageObservationResult> result =
            await workspace.ExecutePackageRootQueryAsync(
                correspondence!,
                ready!.Generation,
                (realization, token) => ValueTask.FromResult(
                    Observe(
                        workspace.Identity, binding, occurrence.Occurrence,
                        correspondence!, ready.Generation, producer!, realization, token)),
                cancellationToken: cancellationToken).ConfigureAwait(false);
        return result switch
        {
            ArtifactRootResult<CoordinatePackageObservationResult>.Available available =>
                available.Value,
            ArtifactRootResult<CoordinatePackageObservationResult>.Rejected rejected =>
                new CoordinatePackageObservationResult.Unavailable(new(
                    CoordinateLibraryPairingFailureKind.RootUnavailable,
                    "The exact Package Root could not admit observation.",
                    rejected.Failure)),
            _ => throw new InvalidOperationException("Unknown Package Root query outcome."),
        };
    }

    internal static CoordinatePackageObservationResult ObserveAdmitted(
        InspectionWorkspace workspace,
        PackageRootBinding binding,
        WorkspacePackageOccurrenceDescriptor occurrence,
        PackageAssemblyContextRealization realization,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(binding);
        ArgumentNullException.ThrowIfNull(occurrence);
        ArgumentNullException.ThrowIfNull(realization);
        cancellationToken.ThrowIfCancellationRequested();

        CoordinateLibraryPairingFailure? invalid =
            ValidateObservationEndpoint(
                workspace,
                binding,
                occurrence,
                out PackageArtifactRootCorrespondence? correspondence,
                out PackageProducerIdentity? producer,
                out ArtifactRootRealizationStatus.Ready? ready);
        return invalid is not null
            ? new CoordinatePackageObservationResult.Unavailable(invalid)
            : Observe(
                workspace.Identity,
                binding,
                occurrence.Occurrence,
                correspondence!,
                ready!.Generation,
                producer!,
                realization,
                cancellationToken);
    }

    public static CoordinateLibraryPairingResult Execute(
        StructuralSubjectIdentity.LibrarySubject source,
        CoordinatePackageObservation before,
        CoordinatePackageObservation after)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);

        if (!ReferenceEquals(
                source.Workspace.Identity,
                before.Occurrence.Identity.WorkspaceIdentity))
        {
            return Refused(
                CoordinateLibraryPairingFailureKind.ForeignWorkspace,
                "The source Library and source observation belong to different Workspaces.");
        }
        if (!ReferenceEquals(source.Package.Occurrence.Identity, before.Occurrence.Identity))
        {
            return Refused(
                CoordinateLibraryPairingFailureKind.InvalidEndpointAssociation,
                "The source Library belongs to a different Package occurrence.");
        }
        if (!StringComparer.OrdinalIgnoreCase.Equals(
                before.Occurrence.Package.PackageId, after.Occurrence.Package.PackageId))
        {
            return Refused(
                CoordinateLibraryPairingFailureKind.DifferentPackage,
                "Library pairing requires the same Package identity at both endpoints.");
        }
        if (!before.Producer.Equals(after.Producer))
        {
            return Refused(
                CoordinateLibraryPairingFailureKind.DifferentProducer,
                "Library pairing requires the same complete Package Source producer identity.");
        }

        CoordinateApiLibraryObservation? original = before.Libraries.FirstOrDefault(
            library => ReferenceEquals(
                library.Subject.Identity.Registration, source.Identity.Registration));
        if (original is null)
        {
            return Refused(
                CoordinateLibraryPairingFailureKind.SourceLibraryUnavailable,
                "The source Library is not in the exact observed API population.");
        }

        var registrations = new HashSet<NavigationRegistrationIdentity>(
            ReferenceEqualityComparer.Instance);
        ImmutableArray<CoordinateApiLibraryObservation> candidates =
        [
            .. after.Libraries.Where(library =>
                SameLibraryProfile(original.Assembly, library.Assembly)
                && registrations.Add(library.Subject.Identity.Registration)),
        ];
        return new(source, before, after, candidates.Length switch
        {
            0 => CoordinateLibraryPairingStatus.Absent,
            1 => CoordinateLibraryPairingStatus.Exact,
            _ => CoordinateLibraryPairingStatus.Ambiguous,
        }, candidates);

        CoordinateLibraryPairingResult Refused(
            CoordinateLibraryPairingFailureKind kind, string detail) =>
            new(source, before, after, CoordinateLibraryPairingStatus.Refused, [],
                new(kind, detail));
    }

    static CoordinatePackageObservationResult Observe(
        InspectionWorkspaceIdentity workspace,
        PackageRootBinding binding,
        WorkspacePackageOccurrence occurrence,
        PackageArtifactRootCorrespondence correspondence,
        ArtifactRootGenerationReference generation,
        PackageProducerIdentity producer,
        PackageAssemblyContextRealization realization,
        CancellationToken cancellationToken)
    {
        PackageCompileAssetSelection selection = binding.Root.AssetSelection;
        if (selection.Status is not (
            PackageCompileAssetSelectionStatus.Selected
            or PackageCompileAssetSelectionStatus.EmptyCompileGroup
            or PackageCompileAssetSelectionStatus.NoCompileAssets))
        {
            return Unavailable(
                CoordinateLibraryPairingFailureKind.UnsupportedSelection,
                $"The selected API population is unavailable: {selection.Status}.");
        }
        if (selection.Assets.Count != realization.SurfaceParticipants.Length
            || realization.SurfaceParticipants.Any(participant =>
                !ReferenceEquals(participant.Package, binding.Root.Identity)
                || !selection.Assets.Any(asset => ReferenceEquals(asset, participant.Asset))))
        {
            return Unavailable(
                CoordinateLibraryPairingFailureKind.InvalidEndpointAssociation,
                "The realization does not cover the binding's exact selected API assets.");
        }
        if (!realization.HasAssemblyContexts)
        {
            return selection.Assets.Count == 0
                ? Available(null, [])
                : Unavailable(
                    CoordinateLibraryPairingFailureKind.IncompletePopulation,
                    "Selected API assets have no admitted assembly context.");
        }

        AssemblyContextGroup group = realization.SurfaceGroup;
        AssemblyBindingPolicyVersion policy = group.BindingPolicyVersion;
        if (group.Participants.Length != realization.SurfaceParticipants.Length
            || group.Participants.Any(participant =>
                !realization.SurfaceParticipants.Any(selected =>
                    ReferenceEquals(
                        selected.Participant.Assembly.Registration,
                        participant.Assembly.Registration))))
        {
            return Unavailable(
                CoordinateLibraryPairingFailureKind.IncompletePopulation,
                "The API context contains a different population from the selected Package.");
        }

        var libraries = ImmutableArray.CreateBuilder<CoordinateApiLibraryObservation>();
        var package = StructuralSubjectIdentity.ForPackage(
            StructuralSubjectIdentity.ForWorkspace(workspace), occurrence);
        foreach (PackageAssemblyRoleParticipant selected in realization.SurfaceParticipants)
        {
            cancellationToken.ThrowIfCancellationRequested();
            AssemblyContextParticipant participant = selected.Participant;
            AssemblyImageAccessResult<ResolvedAssemblyReference> image =
                group.RetainAssemblyReference(participant.Assembly);
            if (image is AssemblyImageAccessResult<ResolvedAssemblyReference>.Rejected rejected)
            {
                return new CoordinatePackageObservationResult.Unavailable(new(
                    CoordinateLibraryPairingFailureKind.ImageUnavailable,
                    "A selected API asset could not supply decoded assembly evidence.",
                    ImageFailure: rejected.Failure,
                    Asset: selected.Asset));
            }
            if (image is not AssemblyImageAccessResult<ResolvedAssemblyReference>.Available available)
                throw new InvalidOperationException("Unknown assembly image-access outcome.");

            var member = new WorkspaceContextMember(
                WorkspaceMemberCoordinate.Package(
                    binding.Root.PackageId, binding.Root.PackageVersion,
                    binding.Coordinate.Framework, binding.Coordinate.RuntimeIdentifier),
                binding.Coordinate,
                participant);
            libraries.Add(new(
                StructuralSubjectIdentity.ForLibrary(package, member),
                selected.Asset,
                available.Value.Identity));
        }
        if (!ReferenceEquals(policy, group.BindingPolicyVersion))
        {
            return Unavailable(
                CoordinateLibraryPairingFailureKind.InvalidEndpointAssociation,
                "The binding-policy observation changed during API population evaluation.");
        }
        return Available(policy, libraries.ToImmutable());

        CoordinatePackageObservationResult Available(
            AssemblyBindingPolicyVersion? policyVersion,
            ImmutableArray<CoordinateApiLibraryObservation> values) =>
            new CoordinatePackageObservationResult.Available(new(
                occurrence, correspondence, generation, producer,
                binding.ContentGenerationIdentity, binding.SelectionIdentity,
                policyVersion, values));
    }

    static CoordinateLibraryPairingFailure? ValidateObservationEndpoint(
        InspectionWorkspace workspace,
        PackageRootBinding binding,
        WorkspacePackageOccurrenceDescriptor occurrence,
        out PackageArtifactRootCorrespondence? correspondence,
        out PackageProducerIdentity? producer,
        out ArtifactRootRealizationStatus.Ready? ready)
    {
        correspondence = null;
        producer = null;
        ready = null;
        if (!ReferenceEquals(
                workspace.Identity,
                occurrence.Occurrence.Identity.WorkspaceIdentity))
        {
            return new(
                CoordinateLibraryPairingFailureKind.ForeignWorkspace,
                "The Package occurrence belongs to a different Workspace.");
        }
        if (occurrence.Occurrence.Correspondence
                is not PackageArtifactRootCorrespondence candidate
            || !candidate.Matches(PackageArtifactRootRequest.From(binding))
            || !occurrence.Occurrence.Package.Matches(binding)
            || !binding.ReferencesRetainedContent())
        {
            return new(
                CoordinateLibraryPairingFailureKind.InvalidEndpointAssociation,
                "The Package binding does not identify the observed occurrence and retained content.");
        }
        correspondence = candidate;
        if (binding.SourceProducer is not { } sourceProducer)
        {
            return new(
                CoordinateLibraryPairingFailureKind.MissingProducer,
                "Library pairing requires the complete Package Source producer identity.");
        }
        producer = sourceProducer;
        if (occurrence.Realization.Status
                is not ArtifactRootRealizationStatus.Ready readyStatus)
        {
            return new(
                CoordinateLibraryPairingFailureKind.RootUnavailable,
                "The observed Package Root is not ready.",
                occurrence.Realization.Status
                    is ArtifactRootRealizationStatus.Failed failed
                        ? failed.Failure
                        : null);
        }
        ready = readyStatus;
        return null;
    }

    static bool SameLibraryProfile(
        AssemblyReferenceIdentity left, AssemblyReferenceIdentity right) =>
        StringComparer.OrdinalIgnoreCase.Equals(left.Name, right.Name)
        && StringComparer.OrdinalIgnoreCase.Equals(
            AssemblyReferenceIdentity.NormalizeCulture(left.Culture),
            AssemblyReferenceIdentity.NormalizeCulture(right.Culture))
        && StringComparer.OrdinalIgnoreCase.Equals(
            left.PublicKeyToken ?? "", right.PublicKeyToken ?? "");

    static CoordinatePackageObservationResult Unavailable(
        CoordinateLibraryPairingFailureKind kind, string detail) =>
        new CoordinatePackageObservationResult.Unavailable(new(kind, detail));
}
