using System.Collections.Immutable;
using DotnetInspector.Artifacts;
using DotnetInspector.Artifacts.Workspaces;
using ILInspector.Metadata;
using InertText;

namespace ILInspector.DecompilerHarness;

public enum CompileReferenceFailureKind
{
    ReferenceDigestUnavailable,
    ReferenceImageInvalid,
    ReferenceAuthorityUnavailable,
    ReferenceContentUnavailable,
    ReferenceCandidateConflict,
    ReferenceNotFound,
    ReferenceSelectionAmbiguous,
    SourceReferenceExcluded,
    ReferenceRoleConflict,
    ReferencePlatformRequestUnsupported,
    ReferencePlatformSelectionUnavailable,
    ReferencePlatformIdentityMismatch,
    ReferencePlatformAgreementMismatch,
    ReferencePlatformPolicyMismatch,
}

public sealed record CompileReferenceFailure(
    CompileReferenceFailureKind Kind,
    ArtifactIdentity? Artifact = null,
    AssemblyReferenceIdentity? RequestedIdentity = null,
    ImmutableArray<ArtifactIdentity> Candidates = default,
    AssemblyBindingRequest? BindingRequest = null,
    AssemblyBindingSelectionSnapshot? PolicySelection = null,
    CandidateOpenFailure? ContentFailure = null);

public abstract class CompileReferenceResult<T>
{
    private protected CompileReferenceResult() { }

    public sealed class Ready : CompileReferenceResult<T>
    {
        internal Ready(T value) => Value = value;
        public T Value { get; }
    }

    public sealed class Rejected : CompileReferenceResult<T>
    {
        internal Rejected(CompileReferenceFailure failure) => Failure = failure;
        public CompileReferenceFailure Failure { get; }
    }
}

/// <summary>One considered artifact, not a path to reopen or a compiler selection.</summary>
public sealed record CompileReferenceInput(
    ArtifactIdentity Artifact,
    AssemblyResolutionProvenance Provenance,
    InertString? Location = null);

/// <summary>Artifact- and Metadata-owned evidence for one inventory entry.</summary>
public sealed class CompileReferenceImage
{
    internal CompileReferenceImage(
        CompileReferenceInput input,
        ResolvedAssemblyReference assembly,
        ArtifactContentDigest digest,
        AssemblyImageSnapshot snapshot)
    {
        Assembly = assembly;
        ContentDigest = digest;
        Snapshot = snapshot;
        Location = input.Location;
    }

    internal ResolvedAssemblyReference Assembly { get; }
    // The owner-issued identity is stable across inventories in this generation.
    public ArtifactIdentity InventoryId => ArtifactRegistration.Artifact;
    public ArtifactAcquisitionRegistration ArtifactRegistration =>
        MetadataRegistration.ArtifactRegistration!;
    public AssemblyAcquisitionRegistration MetadataRegistration => Snapshot.Registration;
    public AssemblyResolutionProvenance Provenance => Assembly.Provenance;
    public ArtifactContentDigest ContentDigest { get; }
    public AssemblyImageSnapshot Snapshot { get; }
    public AssemblyReferenceIdentity Identity => Snapshot.Identity;
    public Guid ModuleVersionId => Snapshot.ModuleVersionId;
    public InertString? Location { get; }
}

/// <summary>
/// All considered candidates under one caller-owned query lease. Discovery
/// obtains every owner digest before asking Metadata to construct any descriptor.
/// </summary>
public sealed class CompileReferenceInventory
{
    internal ArtifactSetSession Owner { get; }
    internal ArtifactQueryLease Lease { get; }

    CompileReferenceInventory(
        ArtifactSetSession owner,
        ArtifactQueryLease lease,
        CompileReferenceImage source,
        ImmutableArray<CompileReferenceImage> candidates)
    {
        Owner = owner;
        Lease = lease;
        Source = source;
        Candidates = candidates;
    }

    public CompileReferenceImage Source { get; }
    public ImmutableArray<CompileReferenceImage> Candidates { get; }

    public static CompileReferenceResult<CompileReferenceInventory> Discover(
        ArtifactSetSession owner,
        ArtifactQueryLease lease,
        CompileReferenceInput source,
        IEnumerable<CompileReferenceInput> candidates,
        Action<long> chargeWork,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(lease);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(chargeWork);
        cancellationToken.ThrowIfCancellationRequested();
        CompileReferenceInput[] considered = candidates.ToArray();
        CompileReferenceInput[] inputs = [source, .. considered];
        IReadOnlyList<ArtifactDescriptor> catalog;
        try
        {
            catalog = owner.GetCatalog(lease);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or InvalidOperationException)
        {
            return Reject(CompileReferenceFailureKind.ReferenceDigestUnavailable, source.Artifact);
        }
        var digests = new Dictionary<ArtifactIdentity, ArtifactContentDigest>();

        foreach (CompileReferenceInput input in inputs)
        {
            ArgumentNullException.ThrowIfNull(input);
            ArgumentNullException.ThrowIfNull(input.Artifact);
            ArgumentNullException.ThrowIfNull(input.Provenance);
            cancellationToken.ThrowIfCancellationRequested();
            if (!catalog.Any(descriptor => ReferenceEquals(descriptor.Identity, input.Artifact)))
                return Reject(CompileReferenceFailureKind.ReferenceDigestUnavailable, input.Artifact);
            // Even a repeated occurrence must retain current authority on warm access.
            if (owner.GetContentDigest(input.Artifact, lease, chargeWork, cancellationToken)
                is not ArtifactContentAccessOutcome<ArtifactContentDigest>.Accessed accessed)
            {
                return Reject(CompileReferenceFailureKind.ReferenceDigestUnavailable, input.Artifact);
            }
            digests[input.Artifact] = accessed.Value;
        }

        var images = new Dictionary<ArtifactIdentity, CompileReferenceImage>();
        var declarations = new Dictionary<ArtifactIdentity, CompileReferenceInput>();
        foreach (CompileReferenceInput input in inputs)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (declarations.TryGetValue(input.Artifact, out CompileReferenceInput? previous))
            {
                if (previous != input)
                    return Reject(CompileReferenceFailureKind.ReferenceCandidateConflict, input.Artifact);
                continue;
            }
            declarations.Add(input.Artifact, input);
            try
            {
                ArtifactContentReference content = owner.GetContentReference(input.Artifact, lease);
                ResolvedAssemblyReference? assembly =
                    ResolvedAssemblyReference.CreateFromArtifactIfManaged(
                        content.Registration, content.OpenRead, input.Provenance);
                if (assembly is null)
                    return Reject(CompileReferenceFailureKind.ReferenceImageInvalid, input.Artifact);

                var retained = owner.WithQueryContent(
                    input.Artifact, lease,
                    (view, _) => AssemblyImageSnapshot.FromRetainedContent(
                        assembly, ImmutableArray.Create(view.Content)),
                    cancellationToken);
                if (retained is not ArtifactContentAccessOutcome<AssemblyImageSnapshotResult>.Accessed accessed)
                    return Reject(CompileReferenceFailureKind.ReferenceAuthorityUnavailable, input.Artifact);
                if (accessed.Value is not AssemblyImageSnapshotResult.Ready ready)
                    return Reject(CompileReferenceFailureKind.ReferenceImageInvalid, input.Artifact);
                images.Add(input.Artifact, new CompileReferenceImage(
                    input, assembly, digests[input.Artifact], ready.Snapshot));
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or ObjectDisposedException)
            {
                return Reject(CompileReferenceFailureKind.ReferenceAuthorityUnavailable, input.Artifact);
            }
            catch (BadImageFormatException)
            {
                return Reject(CompileReferenceFailureKind.ReferenceImageInvalid, input.Artifact);
            }
            catch (IOException)
            {
                return Reject(CompileReferenceFailureKind.ReferenceContentUnavailable, input.Artifact);
            }
        }

        return new CompileReferenceResult<CompileReferenceInventory>.Ready(
            new CompileReferenceInventory(owner, lease, images[source.Artifact],
                [.. considered.Select(input => images[input.Artifact])
                    .Distinct().OrderBy(image => image.InventoryId.Ordinal)]));
    }

    public CompileReferenceResult<CompileReferenceSet> Select(
        IEnumerable<CompileReferenceRequest> requests,
        CancellationToken cancellationToken = default) =>
        CompileReferenceSet.Select(this, requests, cancellationToken);

    static CompileReferenceResult<CompileReferenceInventory> Reject(
        CompileReferenceFailureKind kind, ArtifactIdentity artifact) =>
        new CompileReferenceResult<CompileReferenceInventory>.Rejected(new(kind, artifact));
}
