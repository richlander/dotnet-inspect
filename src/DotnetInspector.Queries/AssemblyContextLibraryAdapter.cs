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
/// Artifact-owned retained image. They are not a process-RSS limit or a
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
    /// Maximum bytes retained by the adapter's independent input capture.
    /// </summary>
    public long MaxCapturedImageBytes { get; }

    /// <summary>
    /// Maximum bytes retained by the new Artifact generation.
    /// </summary>
    public long MaxRetainedArtifactBytes { get; }
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
            || publishedReference.Contents.Count != 1
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
            long maxCapturedImageBytes)
            : base([])
        {
            SourceRegistration = sourceRegistration;
            BindingPolicyVersion = bindingPolicyVersion;
            RequiredImageBytes = requiredImageBytes;
            MaxCapturedImageBytes = maxCapturedImageBytes;
        }

        public AssemblyAcquisitionRegistration SourceRegistration { get; }
        public AssemblyBindingPolicyVersion BindingPolicyVersion { get; }
        public long RequiredImageBytes { get; }
        public long MaxCapturedImageBytes { get; }
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
    public static async ValueTask<AssemblyContextLibraryAdapterResult>
        MaterializeAsync(
            AssemblyContextGroup group,
            AssemblyContextParticipant participant,
            AssemblyContextLibraryRole role,
            AssemblyContextLibraryMaterializationLimits limits,
            CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(group);
        ArgumentNullException.ThrowIfNull(participant);
        ArgumentNullException.ThrowIfNull(limits);
        if (!Enum.IsDefined(role))
            throw new ArgumentOutOfRangeException(nameof(role));

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
                limits.MaxCapturedImageBytes);
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
                MaxArtifacts = 1,
                MaxArtifactBytes =
                    limits.MaxRetainedArtifactBytes,
                MaxRetainedBytes =
                    limits.MaxRetainedArtifactBytes,
            });
        ArtifactQueryLease? queryLease = null;
        ArtifactContentLease? contentLease = null;
        LibraryContentOwner? owner = null;
        try
        {
            var provenance =
                new AssemblyContextLibraryArtifactProvenance(
                    ready.SourceRegistration,
                    ready.BindingPolicyVersion,
                    ready.SourceProvenance);
            ArtifactContribution? contribution = null;
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
                        return ValueTask.FromResult<
                            ArtifactAcquisitionOutcome>(
                                new ArtifactAcquisitionOutcome.Acquired(
                                    [contribution],
                                    ArtifactAcquisitionLeases.None));
                    },
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            ArtifactAssemblyProjectionOutcome? projection = null;
            ArtifactSetPublicationOutcome publication =
                await session.SealWithProjectionAsync(
                        (view, token) =>
                        {
                            projection =
                                ArtifactAssemblyInspection.Project(
                                    view,
                                    token);
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
                            contentLease,
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
                            contentLease,
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

            cancellationToken.ThrowIfCancellationRequested();
            ArtifactQueryAuthorization authorization =
                session.CreateQueryAuthorization();
            queryLease = session.IssueLease(authorization);
            ArtifactContentReference content =
                session.GetContentReference(
                    contribution!.Descriptor.Identity,
                    queryLease);
            contentLease =
                session.IssueContentLease(content, queryLease);
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
                    assemblyCorrespondence);
            var association =
                new AssemblyContextLibraryAssociation(
                    ready.SourceRegistration,
                    ready.BindingPolicyVersion,
                    content,
                    projected.Value,
                    library);
            owner = new LibraryContentOwner(
                library,
                [contentLease]);
            contentLease = null;

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
                        contentLease,
                        session)
                    .ConfigureAwait(false);
            ArtifactSetSession.AttachCleanupFailures(
                failure,
                cleanup);
            throw;
        }
    }

    static CapturedInput Capture(
        AssemblyImageSnapshot snapshot,
        AssemblyResolutionProvenance sourceProvenance,
        AssemblyBindingPolicyVersion bindingPolicyVersion,
        AssemblyContextLibraryMaterializationLimits limits,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (snapshot.Length > limits.MaxCapturedImageBytes)
        {
            return new CapturedInput.Incomplete(
                snapshot.Registration,
                bindingPolicyVersion,
                snapshot.Length);
        }

        byte[] content = snapshot.Content.AsSpan().ToArray();
        cancellationToken.ThrowIfCancellationRequested();
        return new CapturedInput.Ready(
            snapshot.Registration,
            bindingPolicyVersion,
            sourceProvenance,
            content);
    }

    static async ValueTask<IReadOnlyList<Exception>> CleanupAsync(
        LibraryContentOwner? owner,
        ArtifactQueryLease? queryLease,
        ArtifactContentLease? contentLease,
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
            try
            {
                contentLease?.Dispose();
            }
            catch (Exception failure)
            {
                failures.Add(failure);
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
            long requiredImageBytes)
            : CapturedInput(
                sourceRegistration,
                bindingPolicyVersion)
        {
            internal long RequiredImageBytes { get; } =
                requiredImageBytes;
        }

        internal sealed class Ready(
            AssemblyAcquisitionRegistration sourceRegistration,
            AssemblyBindingPolicyVersion bindingPolicyVersion,
            AssemblyResolutionProvenance sourceProvenance,
            byte[] content)
            : CapturedInput(
                sourceRegistration,
                bindingPolicyVersion)
        {
            internal AssemblyResolutionProvenance SourceProvenance
            { get; } = sourceProvenance;
            internal byte[] Content { get; } = content;
        }
    }
}
