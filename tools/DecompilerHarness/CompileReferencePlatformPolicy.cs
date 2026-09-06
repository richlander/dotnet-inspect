using System.Collections.Immutable;
using DotnetInspector.Artifacts;
using DotnetInspector.Artifacts.Workspaces;
using DotnetInspector.Services;
using ILInspector.Metadata;
using InertText;

namespace ILInspector.DecompilerHarness;

public sealed class CompilePlatformBindingEvidence
{
    internal CompilePlatformBindingEvidence(
        AssemblyBindingRequest request,
        AssemblyBindingSelectionSnapshot platform,
        AssemblyBindingSelectionSnapshot agreement,
        ArtifactIdentity? origin,
        ArtifactIdentity platformArtifact,
        ArtifactIdentity agreementArtifact)
    {
        Request = request;
        PlatformSelection = platform;
        AgreementSelection = agreement;
        Origin = origin;
        PlatformArtifact = platformArtifact;
        AgreementArtifact = agreementArtifact;
    }

    public AssemblyBindingRequest Request { get; }
    public AssemblyBindingSelectionSnapshot PlatformSelection { get; }
    public AssemblyBindingSelectionSnapshot AgreementSelection { get; }
    public ArtifactIdentity? Origin { get; }
    public ArtifactIdentity PlatformArtifact { get; }
    public ArtifactIdentity AgreementArtifact { get; }
}

/// <summary>
/// Explicit, finite platform preparation. Supporting acquisitions are retained
/// before this helper returns; the caller seals and owns the artifact session.
/// No Services resolver is retained by the frozen binding view.
/// </summary>
public sealed class CompileReferencePlatformPolicy
{
    readonly ArtifactSetSession _owner;
    readonly CompileReferenceInput _source;
    readonly ImmutableArray<CompileReferenceInput> _candidates;

    CompileReferencePlatformPolicy(
        ArtifactSetSession owner,
        AssemblyBindingPolicyVersion ownerPolicyVersion,
        CompileReferenceInput source,
        ImmutableArray<CompileReferenceInput> candidates,
        ImmutableArray<CompilePlatformBindingEvidence> bindings)
    {
        _owner = owner;
        OwnerPolicyVersion = ownerPolicyVersion;
        _source = source;
        _candidates = candidates;
        Bindings = bindings;
    }

    public AssemblyBindingPolicyVersion OwnerPolicyVersion { get; }
    public ImmutableArray<CompilePlatformBindingEvidence> Bindings { get; }

    public static async ValueTask<CompileReferenceResult<CompileReferencePlatformPolicy>> PrepareAsync(
        ArtifactSetSession owner,
        AssemblyDependencyResolver resolver,
        ResolvedAssemblyReference source,
        IEnumerable<AssemblyBindingRequest> requests,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(resolver);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(requests);
        cancellationToken.ThrowIfCancellationRequested();
        AssemblyBindingRequest[] declared = requests.ToArray();
        var capture = new CapturePolicy(resolver, cancellationToken);
        try
        {
            ResolvedAssemblyReference retainedSource = capture.Retain(source);
            var roots = new List<ResolvedAssemblyReference> { retainedSource };
            foreach (AssemblyBindingRequest request in declared)
            {
                ArgumentNullException.ThrowIfNull(request);
                CapturePolicy.RequireSupported(request);
                if (request.Origin is AssemblyBindingOrigin.RequestingAssembly origin)
                    roots.Add(capture.Retain(origin.Assembly));
            }
            using (var catalog = new TypeResolutionCatalog())
            using (catalog.CreateContextWithCancellation(
                capture, roots.DistinctBy(root => root.Registration), declared, [], cancellationToken))
            {
                // Metadata owns forwarding discovery and its scope/occurrence transitions.
            }
            if (!ReferenceEquals(capture.Version, resolver.Version))
                return Reject(new(CompileReferenceFailureKind.ReferencePlatformPolicyMismatch));

            var inputs = new Dictionary<AssemblyAcquisitionRegistration, CompileReferenceInput>();
            await owner.AddRequiredAcquisitionAsync((scope, _) =>
            {
                var contributions = new List<ArtifactContribution>();
                foreach (ResolvedAssemblyReference assembly in capture.Retained.Values)
                {
                    ArtifactContribution contribution = scope.Register(
                        new ServicesAcquisitionProvenance(assembly.Registration, assembly.Provenance),
                        _ => assembly.OpenRead());
                    contributions.Add(contribution);
                    inputs.Add(assembly.Registration, new CompileReferenceInput(
                        contribution.Descriptor.Identity, assembly.Provenance,
                        assembly.Path is { } path ? new InertString(TextPolicy.Field, path) : null));
                }
                return ValueTask.FromResult<ArtifactAcquisitionOutcome>(
                    new ArtifactAcquisitionOutcome.Acquired(contributions, ArtifactAcquisitionLeases.None));
            }, cancellationToken: cancellationToken);

            return new CompileReferenceResult<CompileReferencePlatformPolicy>.Ready(new(
                owner, capture.Version, inputs[source.Registration],
                [.. inputs.Where(entry => !ReferenceEquals(entry.Key, source.Registration)).Select(entry => entry.Value)],
                [.. capture.Bindings.Select(binding => new CompilePlatformBindingEvidence(
                    binding.Request, binding.Platform, binding.Agreement,
                    binding.Request.Origin is AssemblyBindingOrigin.RequestingAssembly origin
                        ? inputs[origin.Registration].Artifact : null,
                    inputs[((AssemblyBindingSelection.Selected)binding.Platform.Selection).Assembly.Registration].Artifact,
                    inputs[((AssemblyBindingSelection.Selected)binding.Agreement.Selection).Assembly.Registration].Artifact))]));
        }
        catch (PreparationFailure failure)
        {
            return Reject(failure.Failure);
        }
    }

    public CompileReferenceResult<CompileReferenceInventory> Discover(
        ArtifactQueryLease lease,
        Action<long> chargeWork,
        CancellationToken cancellationToken = default) =>
        CompileReferenceInventory.Discover(_owner, lease, _source, _candidates, chargeWork, cancellationToken);

    public CompileReferenceResult<CompileReferenceSet> Select(
        CompileReferenceInventory inventory,
        IEnumerable<CompileReferenceRequest> exactRequests,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(inventory);
        ArgumentNullException.ThrowIfNull(exactRequests);
        if (!ReferenceEquals(_owner, inventory.Owner)
            || !ReferenceEquals(_source.Artifact, inventory.Source.InventoryId))
            return RejectSet(new(CompileReferenceFailureKind.ReferencePlatformPolicyMismatch));

        var images = inventory.Candidates.Prepend(inventory.Source).Distinct()
            .ToDictionary(image => image.InventoryId);
        foreach (CompileReferenceInput input in _candidates.Prepend(_source))
        {
            if (!images.TryGetValue(input.Artifact, out CompileReferenceImage? image)
                || image.ArtifactRegistration.Provenance is not ServicesAcquisitionProvenance provenance
                || provenance.Provenance != input.Provenance
                || image.Provenance != input.Provenance)
                return RejectSet(new(CompileReferenceFailureKind.ReferencePlatformPolicyMismatch, input.Artifact));
            if (_owner.WithQueryContent(input.Artifact, inventory.Lease, static (_, _) => true, cancellationToken)
                is not ArtifactContentAccessOutcome<bool>.Accessed)
                return RejectSet(new(CompileReferenceFailureKind.ReferenceAuthorityUnavailable, input.Artifact));
        }
        foreach (CompilePlatformBindingEvidence binding in Bindings)
        {
            CompileReferenceImage platform = images[binding.PlatformArtifact];
            CompileReferenceImage agreement = images[binding.AgreementArtifact];
            if (!ReferenceEquals(
                    ((ServicesAcquisitionProvenance)platform.ArtifactRegistration.Provenance).Registration,
                    ((AssemblyBindingSelection.Selected)binding.PlatformSelection.Selection).Assembly.Registration)
                || !ReferenceEquals(
                    ((ServicesAcquisitionProvenance)agreement.ArtifactRegistration.Provenance).Registration,
                    ((AssemblyBindingSelection.Selected)binding.AgreementSelection.Selection).Assembly.Registration)
                || binding.Origin is { } origin
                    && !ReferenceEquals(
                        ((ServicesAcquisitionProvenance)images[origin].ArtifactRegistration.Provenance).Registration,
                        ((AssemblyBindingOrigin.RequestingAssembly)binding.Request.Origin).Registration))
                return RejectSet(new(CompileReferenceFailureKind.ReferencePlatformPolicyMismatch));
            if (!platform.Identity.IsEquivalentTo(agreement.Identity)
                || platform.ModuleVersionId != agreement.ModuleVersionId
                || platform.ContentDigest.Algorithm != agreement.ContentDigest.Algorithm
                || platform.ContentDigest.HexValue != agreement.ContentDigest.HexValue)
            {
                return RejectSet(new(CompileReferenceFailureKind.ReferencePlatformAgreementMismatch,
                    binding.AgreementArtifact,
                    ((AssemblyBindingTarget.AssemblyReference)binding.Request.Target).Identity,
                    [binding.PlatformArtifact, binding.AgreementArtifact]));
            }
        }
        var frozen = new FrozenPlatformBindings(OwnerPolicyVersion, Bindings, images);
        return CompileReferenceSet.Select(inventory, exactRequests, cancellationToken, frozen);
    }

    static CompileReferenceResult<CompileReferencePlatformPolicy> Reject(CompileReferenceFailure failure) =>
        new CompileReferenceResult<CompileReferencePlatformPolicy>.Rejected(failure);

    static CompileReferenceResult<CompileReferenceSet> RejectSet(CompileReferenceFailure failure) =>
        new CompileReferenceResult<CompileReferenceSet>.Rejected(failure);

    sealed record ServicesAcquisitionProvenance(
        AssemblyAcquisitionRegistration Registration,
        AssemblyResolutionProvenance Provenance) : IArtifactProvenance;

    sealed class PreparationFailure(CompileReferenceFailure failure) : Exception
    {
        public CompileReferenceFailure Failure { get; } = failure;
    }

    sealed record CapturedBinding(
        AssemblyBindingRequest Request,
        AssemblyBindingSelectionSnapshot Platform,
        AssemblyBindingSelectionSnapshot Agreement);

    sealed class CapturePolicy(
        AssemblyDependencyResolver resolver,
        CancellationToken cancellationToken) : IAssemblyBindingPolicy
    {
        long _retainedBytes;
        public AssemblyBindingPolicyVersion Version { get; } = resolver.Version;
        public Dictionary<AssemblyAcquisitionRegistration, ResolvedAssemblyReference> Retained { get; } = [];
        public List<CapturedBinding> Bindings { get; } = [];

        public static void RequireSupported(AssemblyBindingRequest request)
        {
            if (request.Scope != AssemblyResolutionScope.Platform
                || request.Target is not AssemblyBindingTarget.AssemblyReference { Identity.Version: not null }
                || request.Origin is AssemblyBindingOrigin.RequestingAssembly origin
                    && !FrozenPlatformBindings.IsSeed(origin.Lineage))
                throw new PreparationFailure(new(CompileReferenceFailureKind.ReferencePlatformRequestUnsupported,
                    BindingRequest: request));
        }

        public AssemblyBindingSelectionSnapshot Select(AssemblyBindingRequest request)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RequireSupported(request);
            if (request.Origin is AssemblyBindingOrigin.RequestingAssembly origin)
                Retain(origin.Assembly);
            AssemblyBindingSelectionSnapshot platform = resolver.Select(request);
            var requested = ((AssemblyBindingTarget.AssemblyReference)request.Target).Identity;
            if (!ReferenceEquals(Version, platform.Version))
                throw new PreparationFailure(new(CompileReferenceFailureKind.ReferencePlatformPolicyMismatch));
            if (platform.Selection is not AssemblyBindingSelection.Selected selected
                || selected.Assembly.Provenance is not AssemblyResolutionProvenance.PlatformAsset)
                throw new PreparationFailure(new(CompileReferenceFailureKind.ReferencePlatformSelectionUnavailable,
                    RequestedIdentity: requested, BindingRequest: request, PolicySelection: platform));
            if (!FrozenPlatformBindings.IsSeed(selected.Occurrence.Lineage)
                || selected.Assembly.Identity.Version is not { } version
                || version < requested.Version
                || !(requested with { Version = version }).IsEquivalentTo(selected.Assembly.Identity))
                throw new PreparationFailure(new(CompileReferenceFailureKind.ReferencePlatformIdentityMismatch,
                    RequestedIdentity: requested, BindingRequest: request, PolicySelection: platform));

            var agreementRequest = new AssemblyBindingRequest(
                AssemblyBindingTarget.Reference(selected.Assembly.Identity), request.Origin, AssemblyResolutionScope.Any);
            AssemblyBindingSelectionSnapshot agreement = resolver.Select(agreementRequest);
            if (!ReferenceEquals(Version, agreement.Version))
                throw new PreparationFailure(new(CompileReferenceFailureKind.ReferencePlatformPolicyMismatch));
            if (agreement.Selection is not AssemblyBindingSelection.Selected compiler
                || !FrozenPlatformBindings.IsSeed(compiler.Occurrence.Lineage))
                throw new PreparationFailure(new(CompileReferenceFailureKind.ReferencePlatformSelectionUnavailable,
                    RequestedIdentity: requested, BindingRequest: agreementRequest, PolicySelection: agreement));

            Bindings.Add(new(request, platform, agreement));
            Retain(compiler.Assembly);
            foreach (ResolvedAssemblyReference shadow in selected.ShadowedAssemblies.Concat(compiler.ShadowedAssemblies))
                Retain(shadow);
            // Services currently issues Seed occurrences. Retention preserves both
            // that continuation and the original acquisition registration.
            return new(Version, AssemblyBindingSelection.Found(Retain(selected.Assembly),
                [.. selected.ShadowedAssemblies.Select(Retain)]));
        }

        public ResolvedAssemblyReference Retain(ResolvedAssemblyReference assembly)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Retained.TryGetValue(assembly.Registration, out ResolvedAssemblyReference? retained))
                return retained;
            AssemblyImageSnapshotResult snapshot = AssemblyImageSnapshot.Open(
                assembly,
                bytes =>
                {
                    if (bytes > AssemblyImageSnapshot.DefaultMaxRetainedImageBytes - _retainedBytes)
                        return false;
                    _retainedBytes += bytes;
                    return true;
                },
                bytes => _retainedBytes -= bytes);
            if (snapshot is not AssemblyImageSnapshotResult.Ready ready)
                throw new PreparationFailure(new(CompileReferenceFailureKind.ReferenceContentUnavailable,
                    ContentFailure: ((AssemblyImageSnapshotResult.Rejected)snapshot).Failure));
            retained = ready.Snapshot.RetainAssemblyReference(assembly);
            Retained.Add(assembly.Registration, retained);
            return retained;
        }
    }
}

internal sealed class FrozenPlatformBindings
{
    readonly Dictionary<BindingKey, CompileReferenceImage> _bindings = new(new BindingKeyComparer());
    readonly IReadOnlyDictionary<ArtifactIdentity, CompileReferenceImage> _images;

    internal FrozenPlatformBindings(
        AssemblyBindingPolicyVersion ownerVersion,
        ImmutableArray<CompilePlatformBindingEvidence> evidence,
        IReadOnlyDictionary<ArtifactIdentity, CompileReferenceImage> images)
    {
        OwnerVersion = ownerVersion;
        _images = images;
        var unique = ImmutableArray.CreateBuilder<CompilePlatformBindingEvidence>();
        foreach (CompilePlatformBindingEvidence binding in evidence)
        {
            var identity = ((AssemblyBindingTarget.AssemblyReference)binding.Request.Target).Identity;
            var key = new BindingKey(identity, binding.Origin, binding.Request.Scope);
            if (_bindings.TryAdd(key, images[binding.PlatformArtifact]))
                unique.Add(binding);
            else if (!ReferenceEquals(_bindings[key].InventoryId, binding.PlatformArtifact))
                throw new InvalidOperationException("Services returned conflicting equivalent platform bindings.");
        }
        Evidence = unique.ToImmutable();
        PlatformImages = [.. evidence.Select(binding => images[binding.PlatformArtifact]).Distinct()];
    }

    internal AssemblyBindingPolicyVersion OwnerVersion { get; }
    internal ImmutableArray<CompilePlatformBindingEvidence> Evidence { get; }
    internal ImmutableArray<CompileReferenceImage> PlatformImages { get; }

    internal static bool IsSeed(AssemblyBindingLineage? lineage) =>
        lineage is null || lineage == AssemblyBindingLineage.Seed;

    internal bool OwnsOrigin(ArtifactAcquisitionRegistration registration) =>
        _images.TryGetValue(registration.Artifact, out CompileReferenceImage? image)
        && ReferenceEquals(image.ArtifactRegistration, registration);

    internal AssemblyBindingSelection Select(AssemblyBindingRequest request)
    {
        ArtifactIdentity? origin = null;
        if (request.Origin is AssemblyBindingOrigin.RequestingAssembly requesting)
        {
            if (!IsSeed(requesting.Lineage)
                || requesting.Registration.ArtifactRegistration is not { } registration
                || !OwnsOrigin(registration))
                return AssemblyBindingSelection.Invalid(new(AssemblyBindingFailureKind.InvalidBindingOrigin));
            origin = registration.Artifact;
        }
        if (request.Target is AssemblyBindingTarget.AssemblyReference reference
            && _bindings.TryGetValue(new(reference.Identity, origin, request.Scope), out CompileReferenceImage? selected))
            return AssemblyBindingSelection.Found(selected.Assembly);
        return AssemblyBindingSelection.CannotSelect(new(AssemblyBindingFailureKind.IdentityPolicyRequired));
    }

    internal void WriteEncoding(BinaryWriter writer)
    {
        CompileReferenceSet.WriteText(writer, "platform-compatibility/v1");
        var ordered = Evidence.Select(binding => (
                Binding: binding,
                // Strict family validation permits the retained spelling to canonicalize
                // equivalent request spellings without changing the requested version.
                Identity: _images[binding.PlatformArtifact].Identity with
                {
                    Version = ((AssemblyBindingTarget.AssemblyReference)binding.Request.Target).Identity.Version,
                }))
            .OrderBy(item => item.Binding.Origin?.Ordinal ?? -1)
            .ThenBy(item => item.Binding.Request.Scope)
            .ThenBy(item => item.Identity.Name, StringComparer.Ordinal)
            .ThenBy(item => item.Identity.Version)
            .ThenBy(item => item.Identity.Culture, StringComparer.Ordinal)
            .ThenBy(item => item.Identity.PublicKeyToken, StringComparer.Ordinal)
            .ToArray();
        writer.Write(ordered.Length);
        foreach (var (binding, identity) in ordered)
        {
            writer.Write(binding.Origin?.Ordinal ?? -1);
            writer.Write((int)binding.Request.Scope);
            CompileReferenceSet.WriteIdentity(writer, identity);
            writer.Write(binding.PlatformArtifact.Ordinal);
            CompileReferenceSet.WriteImage(writer, _images[binding.AgreementArtifact]);
        }
    }

    readonly record struct BindingKey(
        AssemblyReferenceIdentity Identity,
        ArtifactIdentity? Origin,
        AssemblyResolutionScope Scope);

    sealed class BindingKeyComparer : IEqualityComparer<BindingKey>
    {
        public bool Equals(BindingKey x, BindingKey y) =>
            x.Identity.IsEquivalentTo(y.Identity) && ReferenceEquals(x.Origin, y.Origin) && x.Scope == y.Scope;

        public int GetHashCode(BindingKey value) => HashCode.Combine(
            AssemblyReferenceIdentity.EquivalentComparer.GetHashCode(value.Identity), value.Origin, value.Scope);
    }
}
