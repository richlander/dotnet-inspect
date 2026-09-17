using System.Collections.Immutable;
using System.Runtime.ExceptionServices;
using DotnetInspector.Libraries;
using ILInspector.Metadata;
using Inspector.Artifacts;
using Inspector.Artifacts.Workspaces;

namespace DotnetInspector.Queries;

/// <summary>
/// The Library roles supplied by one adapted assembly-context image.
/// </summary>
public enum AssemblyContextLibraryRole
{
    ApiOnly,
    Implementation,
}

/// <summary>
/// Finite per-image bounds for one assembly-context Library materialization.
/// </summary>
/// <remarks>
/// The bounds apply separately to the adapter's captured input and the
/// Artifact-owned retained images. They are not a process-RSS limit or a
/// zero-copy guarantee.
/// </remarks>
public sealed class AssemblyContextLibraryMaterializationLimits
{
    public AssemblyContextLibraryMaterializationLimits(
        long maxCapturedImageBytes,
        long maxRetainedArtifactBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(
            maxCapturedImageBytes);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(
            maxCapturedImageBytes,
            int.MaxValue);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(
            maxRetainedArtifactBytes);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(
            maxRetainedArtifactBytes,
            int.MaxValue);

        MaxCapturedImageBytes = maxCapturedImageBytes;
        MaxRetainedArtifactBytes = maxRetainedArtifactBytes;
    }

    /// <summary>
    /// Maximum bytes in each independently captured assembly or Portable PDB image.
    /// </summary>
    public long MaxCapturedImageBytes { get; }

    /// <summary>
    /// Maximum bytes retained by the new Artifact generation.
    /// </summary>
    public long MaxRetainedArtifactBytes { get; }
}

/// <summary>Already acquired Portable PDB content and its source provenance.</summary>
public sealed class AssemblyContextLibraryPortablePdb
{
    public AssemblyContextLibraryPortablePdb(
        ImmutableArray<byte> image,
        IArtifactProvenance provenance)
    {
        if (image.IsDefault)
            throw new ArgumentException("A Portable PDB image must be supplied.", nameof(image));
        ArgumentNullException.ThrowIfNull(provenance);
        Image = image;
        Provenance = provenance;
    }

    public ImmutableArray<byte> Image { get; }
    public IArtifactProvenance Provenance { get; }
}

/// <summary>
/// Resource-free provenance from one exact assembly-context input to its new
/// Artifact registration.
/// </summary>
public sealed class AssemblyContextLibraryArtifactProvenance :
    IArtifactProvenance
{
    public AssemblyContextLibraryArtifactProvenance(
        AssemblyAcquisitionRegistration sourceRegistration,
        AssemblyBindingPolicyVersion bindingPolicyVersion,
        AssemblyResolutionProvenance sourceProvenance)
    {
        ArgumentNullException.ThrowIfNull(sourceRegistration);
        ArgumentNullException.ThrowIfNull(bindingPolicyVersion);
        ArgumentNullException.ThrowIfNull(sourceProvenance);

        SourceRegistration = sourceRegistration;
        BindingPolicyVersion = bindingPolicyVersion;
        SourceProvenance = sourceProvenance;
    }

    public AssemblyAcquisitionRegistration SourceRegistration { get; }
    public AssemblyBindingPolicyVersion BindingPolicyVersion { get; }
    public AssemblyResolutionProvenance SourceProvenance { get; }
}

/// <summary>
/// Resource-free association from one exact assembly-context occurrence to
/// its direct Artifact-backed Library.
/// </summary>
public sealed class AssemblyContextLibraryAssociation
{
    internal AssemblyContextLibraryAssociation(
        AssemblyAcquisitionRegistration sourceRegistration,
        AssemblyBindingPolicyVersion bindingPolicyVersion,
        ArtifactContentReference publishedContent,
        ArtifactAssemblyProjection projection,
        LibraryReference publishedReference)
    {
        ArgumentNullException.ThrowIfNull(sourceRegistration);
        ArgumentNullException.ThrowIfNull(bindingPolicyVersion);
        ArgumentNullException.ThrowIfNull(publishedContent);
        ArgumentNullException.ThrowIfNull(projection);
        ArgumentNullException.ThrowIfNull(publishedReference);

        if (publishedContent.Provenance
                is not AssemblyContextLibraryArtifactProvenance provenance
            || !ReferenceEquals(
                provenance.SourceRegistration,
                sourceRegistration)
            || !ReferenceEquals(
                provenance.BindingPolicyVersion,
                bindingPolicyVersion))
        {
            throw new ArgumentException(
                "Published content must preserve the exact assembly-context input identity.",
                nameof(publishedContent));
        }
        if (!ReferenceEquals(
                projection.Registration.Generation,
                publishedContent.Generation)
            || !ReferenceEquals(
                projection.Registration.Artifact,
                publishedContent.Artifact))
        {
            throw new ArgumentException(
                "The Metadata projection must describe the exact published Artifact.",
                nameof(projection));
        }
        if (!publishedReference.IsDirectArtifact
            || !ReferenceEquals(
                publishedReference.ApiAssembly.ArtifactReference,
                publishedContent)
            || !AssemblyReferenceIdentity.EquivalentComparer.Equals(
                projection.Identity,
                publishedReference.ApiAssembly.AssemblyIdentity!.Identity))
        {
            throw new ArgumentException(
                "The published Library must directly reference the exact adapted Artifact.",
                nameof(publishedReference));
        }

        SourceRegistration = sourceRegistration;
        BindingPolicyVersion = bindingPolicyVersion;
        PublishedContent = publishedContent;
        Projection = projection;
        PublishedReference = publishedReference;
    }

    public AssemblyAcquisitionRegistration SourceRegistration { get; }
    public AssemblyBindingPolicyVersion BindingPolicyVersion { get; }
    public ArtifactContentReference PublishedContent { get; }
    public ArtifactAcquisitionRegistration PublishedRegistration =>
        PublishedContent.Registration;
    public ArtifactAssemblyProjection Projection { get; }
    public LibraryReference PublishedReference { get; }
}

/// <summary>
/// Result of independently materializing one exact assembly-context
/// participant as a direct Library.
/// </summary>
public abstract class AssemblyContextLibraryAdapterResult
{
    private protected AssemblyContextLibraryAdapterResult()
    {
    }

    /// <summary>
    /// Transfers Library content ownership and the adjacent Artifact session
    /// as separate authorities.
    /// </summary>
    public sealed class Completed : AssemblyContextLibraryAdapterResult
    {
        internal Completed(
            AssemblyContextLibraryAssociation association,
            LibraryContentOwner owner,
            ArtifactSetSession artifacts)
        {
            if (!ReferenceEquals(
                    owner.Reference,
                    association.PublishedReference))
            {
                throw new ArgumentException(
                    "The Library owner must own the exact published reference.",
                    nameof(owner));
            }
            if (!ReferenceEquals(
                    association.PublishedContent.Generation,
                    artifacts.Generation))
            {
                throw new ArgumentException(
                    "The adjacent Artifact session must own the published Library content.",
                    nameof(artifacts));
            }

            Association = association;
            Owner = owner;
            Artifacts = artifacts;
        }

        public AssemblyContextLibraryAssociation Association { get; }
        public LibraryReference Reference =>
            Association.PublishedReference;
        public LibraryContentOwner Owner { get; }
        public ArtifactSetSession Artifacts { get; }
    }

    /// <summary>
    /// A terminal result whose created authorities have already been settled.
    /// </summary>
    public abstract class Terminal : AssemblyContextLibraryAdapterResult
    {
        private protected Terminal(
            IReadOnlyList<Exception> cleanupFailures) =>
            CleanupFailures = cleanupFailures;

        public IReadOnlyList<Exception> CleanupFailures { get; }
    }

    public sealed class SnapshotRejected : Terminal
    {
        internal SnapshotRejected(
            AssemblyAcquisitionRegistration sourceRegistration,
            AssemblyBindingPolicyVersion bindingPolicyVersion,
            CandidateOpenFailure failure)
            : base([])
        {
            SourceRegistration = sourceRegistration;
            BindingPolicyVersion = bindingPolicyVersion;
            Failure = failure;
        }

        public AssemblyAcquisitionRegistration SourceRegistration { get; }
        public AssemblyBindingPolicyVersion BindingPolicyVersion { get; }
        public CandidateOpenFailure Failure { get; }
    }

    public sealed class Incomplete : Terminal
    {
        internal Incomplete(
            AssemblyAcquisitionRegistration sourceRegistration,
            AssemblyBindingPolicyVersion bindingPolicyVersion,
            long requiredImageBytes,
            long maxCapturedImageBytes,
            LibraryContentRole contentRole)
            : base([])
        {
            SourceRegistration = sourceRegistration;
            BindingPolicyVersion = bindingPolicyVersion;
            RequiredImageBytes = requiredImageBytes;
            MaxCapturedImageBytes = maxCapturedImageBytes;
            ContentRole = contentRole;
        }

        public AssemblyAcquisitionRegistration SourceRegistration { get; }
        public AssemblyBindingPolicyVersion BindingPolicyVersion { get; }
        public long RequiredImageBytes { get; }
        public long MaxCapturedImageBytes { get; }
        public LibraryContentRole ContentRole { get; }
    }

    public sealed class PortablePdbRejected : Terminal
    {
        internal PortablePdbRejected(
            AssemblyAcquisitionRegistration sourceRegistration,
            AssemblyBindingPolicyVersion bindingPolicyVersion,
            IArtifactProvenance provenance,
            IReadOnlyList<string> observations,
            IReadOnlyList<Exception> cleanupFailures)
            : base(cleanupFailures)
        {
            SourceRegistration = sourceRegistration;
            BindingPolicyVersion = bindingPolicyVersion;
            Provenance = provenance;
            Observations = observations;
        }

        public AssemblyAcquisitionRegistration SourceRegistration { get; }
        public AssemblyBindingPolicyVersion BindingPolicyVersion { get; }
        public IArtifactProvenance Provenance { get; }
        public IReadOnlyList<string> Observations { get; }
    }

    public sealed class ArtifactNotPublished : Terminal
    {
        internal ArtifactNotPublished(
            AssemblyAcquisitionRegistration sourceRegistration,
            AssemblyBindingPolicyVersion bindingPolicyVersion,
            ArtifactSetPublicationOutcome.NotPublished publication,
            IReadOnlyList<Exception> cleanupFailures)
            : base(cleanupFailures)
        {
            SourceRegistration = sourceRegistration;
            BindingPolicyVersion = bindingPolicyVersion;
            Publication = publication;
        }

        public AssemblyAcquisitionRegistration SourceRegistration { get; }
        public AssemblyBindingPolicyVersion BindingPolicyVersion { get; }
        public ArtifactSetPublicationOutcome.NotPublished Publication
        { get; }
    }

    public sealed class MetadataNotProjected : Terminal
    {
        internal MetadataNotProjected(
            AssemblyAcquisitionRegistration sourceRegistration,
            AssemblyBindingPolicyVersion bindingPolicyVersion,
            ArtifactAssemblyProjectionOutcome projection,
            IReadOnlyList<Exception> cleanupFailures)
            : base(cleanupFailures)
        {
            SourceRegistration = sourceRegistration;
            BindingPolicyVersion = bindingPolicyVersion;
            Projection = projection;
        }

        public AssemblyAcquisitionRegistration SourceRegistration { get; }
        public AssemblyBindingPolicyVersion BindingPolicyVersion { get; }
        public ArtifactAssemblyProjectionOutcome Projection { get; }
    }
}

/// <summary>
/// Materializes one exact assembly-context participant into an independently
/// owned direct Library.
/// </summary>
public static class AssemblyContextLibraryAdapter
{
    public static ValueTask<AssemblyContextLibraryAdapterResult>
        MaterializeAsync(
            AssemblyContextGroup group,
            AssemblyContextParticipant participant,
            AssemblyContextLibraryRole role,
            AssemblyContextLibraryMaterializationLimits limits,
            CancellationToken cancellationToken) =>
        MaterializeAsync(group, participant, role, limits, null, cancellationToken);

    public static async ValueTask<AssemblyContextLibraryAdapterResult>
        MaterializeAsync(
            AssemblyContextGroup group,
            AssemblyContextParticipant participant,
            AssemblyContextLibraryRole role,
            AssemblyContextLibraryMaterializationLimits limits,
            AssemblyContextLibraryPortablePdb? portablePdb,
            CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(group);
        ArgumentNullException.ThrowIfNull(participant);
        ArgumentNullException.ThrowIfNull(limits);
        if (!Enum.IsDefined(role))
            throw new ArgumentOutOfRangeException(nameof(role));
        if (portablePdb is not null && role != AssemblyContextLibraryRole.Implementation)
        {
            throw new ArgumentException(
                "A Portable PDB companion requires the selected implementation assembly.",
                nameof(portablePdb));
        }

        AssemblyBindingPolicyVersion bindingPolicyVersion =
            group.BindingPolicyVersion;
        AssemblyImageAccessResult<CapturedInput> access =
            group.UseSnapshot(
                participant,
                cancellationToken,
                snapshot => Capture(
                    snapshot,
                    participant.Assembly.Provenance,
                    bindingPolicyVersion,
                    limits,
                    portablePdb,
                    cancellationToken));
        if (access
            is AssemblyImageAccessResult<CapturedInput>.Rejected rejected)
        {
            return new AssemblyContextLibraryAdapterResult.SnapshotRejected(
                participant.Assembly.Registration,
                bindingPolicyVersion,
                rejected.Failure);
        }

        CapturedInput captured =
            ((AssemblyImageAccessResult<CapturedInput>.Available)access)
                .Value;
        if (captured is CapturedInput.Incomplete incomplete)
        {
            return new AssemblyContextLibraryAdapterResult.Incomplete(
                incomplete.SourceRegistration,
                incomplete.BindingPolicyVersion,
                incomplete.RequiredImageBytes,
                limits.MaxCapturedImageBytes,
                incomplete.ContentRole);
        }

        return await MaterializeCapturedAsync(
                (CapturedInput.Ready)captured,
                role,
                limits,
                cancellationToken)
            .ConfigureAwait(false);
    }

    static async ValueTask<AssemblyContextLibraryAdapterResult>
        MaterializeCapturedAsync(
            CapturedInput.Ready ready,
            AssemblyContextLibraryRole role,
            AssemblyContextLibraryMaterializationLimits limits,
            CancellationToken cancellationToken)
    {
        var session = new ArtifactSetSession(
            new ArtifactSetSessionLimits
            {
                MaxArtifacts = ready.PortablePdb is null ? 1 : 2,
                MaxArtifactBytes =
                    limits.MaxRetainedArtifactBytes,
                MaxRetainedBytes =
                    limits.MaxRetainedArtifactBytes,
            });
        ArtifactQueryLease? queryLease = null;
        var contentLeases = new List<ArtifactContentLease>();
        LibraryContentOwner? owner = null;
        try
        {
            var provenance =
                new AssemblyContextLibraryArtifactProvenance(
                    ready.SourceRegistration,
                    ready.BindingPolicyVersion,
                    ready.SourceProvenance);
            ArtifactContribution? contribution = null;
            ArtifactContribution? pdbContribution = null;
            await session.AddRequiredAcquisitionAsync(
                    (scope, generationEnd) =>
                    {
                        generationEnd.ThrowIfCancellationRequested();
                        contribution = scope.Register(
                            provenance,
                            token =>
                            {
                                token.ThrowIfCancellationRequested();
                                return new MemoryStream(
                                    ready.Content,
                                    writable: false);
                            },
                            kind: "assembly-context-library");
                        if (ready.PortablePdb is { } pdb)
                        {
                            pdbContribution = scope.Register(
                                ready.PortablePdbProvenance!,
                                token =>
                                {
                                    token.ThrowIfCancellationRequested();
                                    return new MemoryStream(pdb, writable: false);
                                },
                                kind: "assembly-context-library-portable-pdb");
                        }
                        return ValueTask.FromResult<
                            ArtifactAcquisitionOutcome>(
                                new ArtifactAcquisitionOutcome.Acquired(
                                    pdbContribution is null
                                        ? [contribution]
                                        : [contribution, pdbContribution],
                                    ArtifactAcquisitionLeases.None));
                    },
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            ArtifactAssemblyProjectionOutcome? projection = null;
            ArtifactSetPublicationOutcome publication =
                await session.SealWithProjectionAsync(
                        (view, token) =>
                        {
                            if (ReferenceEquals(view.Artifact, contribution!.Descriptor.Identity))
                            {
                                projection =
                                    ArtifactAssemblyInspection.Project(
                                        view,
                                        token);
                            }
                            return null;
                        },
                        cancellationToken)
                    .ConfigureAwait(false);
            if (publication
                is ArtifactSetPublicationOutcome.NotPublished notPublished)
            {
                IReadOnlyList<Exception> cleanup =
                    await CleanupAsync(
                            owner,
                            queryLease,
                            contentLeases,
                            session)
                        .ConfigureAwait(false);
                return new AssemblyContextLibraryAdapterResult
                    .ArtifactNotPublished(
                        ready.SourceRegistration,
                        ready.BindingPolicyVersion,
                        notPublished,
                        MergeCleanup(
                            notPublished.CleanupFailures,
                            cleanup));
            }
            if (projection
                is not ArtifactAssemblyProjectionOutcome.Projected projected)
            {
                IReadOnlyList<Exception> cleanup =
                    await CleanupAsync(
                            owner,
                            queryLease,
                            contentLeases,
                            session)
                        .ConfigureAwait(false);
                return new AssemblyContextLibraryAdapterResult
                    .MetadataNotProjected(
                        ready.SourceRegistration,
                        ready.BindingPolicyVersion,
                        projection
                            ?? throw new InvalidOperationException(
                                "Artifact publication completed without a Metadata projection outcome."),
                        cleanup);
            }

            if (ready.PortablePdb is { } portablePdb
                && ValidatePortablePdb(
                    ready,
                    projected.Value.Identity,
                    portablePdb,
                    cancellationToken) is { } observations)
            {
                IReadOnlyList<Exception> cleanup = await CleanupAsync(
                    owner, queryLease, contentLeases, session).ConfigureAwait(false);
                return new AssemblyContextLibraryAdapterResult.PortablePdbRejected(
                    ready.SourceRegistration,
                    ready.BindingPolicyVersion,
                    ready.PortablePdbProvenance!,
                    observations,
                    cleanup);
            }

            cancellationToken.ThrowIfCancellationRequested();
            ArtifactQueryAuthorization authorization =
                session.CreateQueryAuthorization();
            queryLease = session.IssueLease(authorization);
            ArtifactContentReference content =
                session.GetContentReference(
                    contribution!.Descriptor.Identity,
                    queryLease);
            contentLeases.Add(session.IssueContentLease(content, queryLease));
            ArtifactContentReference? pdbContent = null;
            if (pdbContribution is not null)
            {
                pdbContent = session.GetContentReference(
                    pdbContribution.Descriptor.Identity, queryLease);
                contentLeases.Add(session.IssueContentLease(pdbContent, queryLease));
            }
            queryLease.Dispose();
            queryLease = null;

            var identity =
                new ManagedMetadataIdentity.Assembly(
                    projected.Value.Identity);
            var assemblyCorrespondence =
                role == AssemblyContextLibraryRole.ApiOnly
                    ? new LibraryAssemblyCorrespondence(
                        content,
                        identity)
                    : new LibraryAssemblyCorrespondence(
                        content,
                        identity,
                        content,
                        identity);
            LibraryReference library =
                LibraryReference.CreateDirect(
                    assemblyCorrespondence,
                    pdbContent is null
                        ? null
                        : [new LibraryCompanionCorrespondence(
                            pdbContent, LibraryContentRole.PortablePdb, content)]);
            var association =
                new AssemblyContextLibraryAssociation(
                    ready.SourceRegistration,
                    ready.BindingPolicyVersion,
                    content,
                    projected.Value,
                    library);
            owner = new LibraryContentOwner(
                library,
                contentLeases);
            contentLeases.Clear();

            return new AssemblyContextLibraryAdapterResult.Completed(
                association,
                owner,
                session);
        }
        catch (Exception failure)
        {
            IReadOnlyList<Exception> cleanup =
                await CleanupAsync(
                        owner,
                        queryLease,
                        contentLeases,
                        session)
                    .ConfigureAwait(false);
            ArtifactSetSession.AttachCleanupFailures(
                failure,
                cleanup);
            throw;
        }
    }

    static IReadOnlyList<string>? ValidatePortablePdb(
        CapturedInput.Ready ready,
        AssemblyReferenceIdentity identity,
        byte[] portablePdb,
        CancellationToken cancellationToken)
    {
        var observations = new List<string>();
        ResolvedAssemblyReference assembly = ResolvedAssemblyReference.Create(
            identity,
            path: null,
            () => new MemoryStream(ready.Content, writable: false),
            ready.SourceProvenance);
        PdbContext context = PdbContext.OpenMetadataOnly(assembly, observations.Add);
        Exception? primaryFailure = null;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (context.PdbId is not { IsPortable: true })
            {
                observations.Add("The selected assembly has no Portable CodeView identity.");
                return observations.AsReadOnly();
            }

            try
            {
                context.LoadPdbFromStream(
                    new MemoryStream(portablePdb, writable: false),
                    throwOnReadFailure: true);
            }
            catch (IOException failure)
            {
                observations.Add(failure.Message);
            }
            cancellationToken.ThrowIfCancellationRequested();
            if (context.HasPdb)
                return null;

            observations.Add("Supplied content was not accepted as a matching Portable PDB.");
            return observations.AsReadOnly();
        }
        catch (Exception failure)
        {
            primaryFailure = failure;
            throw;
        }
        finally
        {
            if (context.DisposeWithFailure() is { } cleanupFailure)
            {
                if (primaryFailure is not null)
                    ArtifactSetSession.AttachCleanupFailures(primaryFailure, [cleanupFailure]);
                else
                    ExceptionDispatchInfo.Throw(cleanupFailure);
            }
        }
    }

    static CapturedInput Capture(
        AssemblyImageSnapshot snapshot,
        AssemblyResolutionProvenance sourceProvenance,
        AssemblyBindingPolicyVersion bindingPolicyVersion,
        AssemblyContextLibraryMaterializationLimits limits,
        AssemblyContextLibraryPortablePdb? portablePdb,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (snapshot.Length > limits.MaxCapturedImageBytes)
        {
            return new CapturedInput.Incomplete(
                snapshot.Registration,
                bindingPolicyVersion,
                snapshot.Length,
                LibraryContentRole.ApiAssembly);
        }
        if (portablePdb is not null
            && portablePdb.Image.Length > limits.MaxCapturedImageBytes)
        {
            return new CapturedInput.Incomplete(
                snapshot.Registration,
                bindingPolicyVersion,
                portablePdb.Image.Length,
                LibraryContentRole.PortablePdb);
        }

        byte[] content = snapshot.Content.AsSpan().ToArray();
        cancellationToken.ThrowIfCancellationRequested();
        return new CapturedInput.Ready(
            snapshot.Registration,
            bindingPolicyVersion,
            sourceProvenance,
            content,
            portablePdb?.Image.AsSpan().ToArray(),
            portablePdb?.Provenance);
    }

    static async ValueTask<IReadOnlyList<Exception>> CleanupAsync(
        LibraryContentOwner? owner,
        ArtifactQueryLease? queryLease,
        IReadOnlyList<ArtifactContentLease> contentLeases,
        ArtifactSetSession session)
    {
        var failures = new List<Exception>();
        if (owner is not null)
        {
            try
            {
                await owner.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception failure)
            {
                failures.Add(failure);
            }
        }
        else
        {
            foreach (ArtifactContentLease contentLease in contentLeases)
            {
                try
                {
                    contentLease.Dispose();
                }
                catch (Exception failure)
                {
                    failures.Add(failure);
                }
            }
        }

        try
        {
            queryLease?.Dispose();
        }
        catch (Exception failure)
        {
            failures.Add(failure);
        }

        try
        {
            await session.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception failure)
        {
            failures.Add(failure);
        }
        foreach (Exception failure in session.CleanupFailures)
        {
            if (!failures.Contains(
                    failure,
                    ReferenceEqualityComparer.Instance))
            {
                failures.Add(failure);
            }
        }
        return failures.AsReadOnly();
    }

    static IReadOnlyList<Exception> MergeCleanup(
        IReadOnlyList<Exception> first,
        IReadOnlyList<Exception> second)
    {
        var result = new List<Exception>(
            first.Count + second.Count);
        foreach (Exception failure in first.Concat(second))
        {
            if (!result.Contains(
                    failure,
                    ReferenceEqualityComparer.Instance))
            {
                result.Add(failure);
            }
        }
        return result.AsReadOnly();
    }

    abstract class CapturedInput
    {
        private protected CapturedInput(
            AssemblyAcquisitionRegistration sourceRegistration,
            AssemblyBindingPolicyVersion bindingPolicyVersion)
        {
            SourceRegistration = sourceRegistration;
            BindingPolicyVersion = bindingPolicyVersion;
        }

        internal AssemblyAcquisitionRegistration SourceRegistration
        { get; }
        internal AssemblyBindingPolicyVersion BindingPolicyVersion
        { get; }

        internal sealed class Incomplete(
            AssemblyAcquisitionRegistration sourceRegistration,
            AssemblyBindingPolicyVersion bindingPolicyVersion,
            long requiredImageBytes,
            LibraryContentRole contentRole)
            : CapturedInput(
                sourceRegistration,
                bindingPolicyVersion)
        {
            internal long RequiredImageBytes { get; } =
                requiredImageBytes;
            internal LibraryContentRole ContentRole { get; } = contentRole;
        }

        internal sealed class Ready(
            AssemblyAcquisitionRegistration sourceRegistration,
            AssemblyBindingPolicyVersion bindingPolicyVersion,
            AssemblyResolutionProvenance sourceProvenance,
            byte[] content,
            byte[]? portablePdb,
            IArtifactProvenance? portablePdbProvenance)
            : CapturedInput(
                sourceRegistration,
                bindingPolicyVersion)
        {
            internal AssemblyResolutionProvenance SourceProvenance
            { get; } = sourceProvenance;
            internal byte[] Content { get; } = content;
            internal byte[]? PortablePdb { get; } = portablePdb;
            internal IArtifactProvenance? PortablePdbProvenance { get; } = portablePdbProvenance;
        }
    }
}
