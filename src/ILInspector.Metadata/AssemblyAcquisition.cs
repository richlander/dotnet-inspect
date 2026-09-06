using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using DotnetInspector.Artifacts;

namespace ILInspector.Metadata;

/// <summary>Structured evidence describing how an assembly candidate was selected.</summary>
public abstract record AssemblyResolutionProvenance
{
    private protected AssemblyResolutionProvenance()
    {
    }

    private protected abstract int Discriminator { get; }

    public static AssemblyResolutionProvenance Package(
        string packageId,
        string packageVersion,
        string? tfm,
        string? rid) =>
        new PackageAsset(packageId, packageVersion, tfm, rid);

    public static AssemblyResolutionProvenance Platform(
        string framework,
        string? frameworkVersion,
        string resolverSource) =>
        new PlatformAsset(framework, frameworkVersion, resolverSource);

    public static AssemblyResolutionProvenance Project(
        string project,
        string? tfm,
        string? rid) =>
        new ProjectAsset(project, tfm, rid);

    public static AssemblyResolutionProvenance Local(string resolverSource) =>
        new LocalAsset(resolverSource);

    /// <summary>
    /// An assembly the caller enumerated or named explicitly, rather than one
    /// the resolver discovered on its own. Designation is a statement of trust
    /// by the caller: it distinguishes a set the caller listed, or a file the
    /// caller named outright, from a file that merely happened to sit beside an
    /// inspected artifact. Corpus enumeration designates, and so does opening a
    /// path or image the caller named directly — see <c>MetadataContext</c> and
    /// <c>MetadataSource</c>, which state the designation as provenance so that
    /// <c>CoreLibraryIdentityTrust</c> decides their entitlement rather than
    /// being bypassed by it. Naming a <em>directory</em> on the command line
    /// still does not designate, because platform-scope resolution already
    /// covers build-layout inspection and a loose directory is not a coherent
    /// closure.
    /// </summary>
    public static AssemblyResolutionProvenance Designated(string resolverSource) =>
        new DesignatedAsset(resolverSource);

    public static AssemblyResolutionProvenance Embedded(
        string contentRef,
        string digest,
        string declaredName) =>
        new EmbeddedAsset(contentRef, digest, declaredName);

    public sealed record PackageAsset : AssemblyResolutionProvenance
    {
        public PackageAsset(
            string packageId,
            string packageVersion,
            string? tfm,
            string? rid)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(packageId);
            ArgumentException.ThrowIfNullOrWhiteSpace(packageVersion);
            PackageId = packageId;
            PackageVersion = packageVersion;
            Tfm = tfm;
            Rid = rid;
        }

        private protected override int Discriminator => 0;
        public string PackageId { get; }
        public string PackageVersion { get; }
        public string? Tfm { get; }
        public string? Rid { get; }
    }

    public sealed record PlatformAsset : AssemblyResolutionProvenance
    {
        public PlatformAsset(
            string framework,
            string? frameworkVersion,
            string resolverSource)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(framework);
            ArgumentException.ThrowIfNullOrWhiteSpace(resolverSource);
            Framework = framework;
            FrameworkVersion = frameworkVersion;
            ResolverSource = resolverSource;
        }

        private protected override int Discriminator => 1;
        public string Framework { get; }
        public string? FrameworkVersion { get; }
        public string ResolverSource { get; }
    }

    public sealed record ProjectAsset : AssemblyResolutionProvenance
    {
        public ProjectAsset(
            string project,
            string? tfm,
            string? rid)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(project);
            Project = project;
            Tfm = tfm;
            Rid = rid;
        }

        private protected override int Discriminator => 2;
        public new string Project { get; }
        public string? Tfm { get; }
        public string? Rid { get; }
    }

    public sealed record LocalAsset : AssemblyResolutionProvenance
    {
        public LocalAsset(string resolverSource)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(resolverSource);
            ResolverSource = resolverSource;
        }

        private protected override int Discriminator => 3;
        public string ResolverSource { get; }
    }

    public sealed record EmbeddedAsset : AssemblyResolutionProvenance
    {
        public EmbeddedAsset(
            string contentRef,
            string digest,
            string declaredName)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(contentRef);
            ArgumentException.ThrowIfNullOrWhiteSpace(digest);
            ArgumentException.ThrowIfNullOrWhiteSpace(declaredName);
            ContentRef = contentRef;
            Digest = digest;
            DeclaredName = declaredName;
        }

        private protected override int Discriminator => 4;
        public string ContentRef { get; }
        public string Digest { get; }
        public string DeclaredName { get; }
    }

    /// <summary>
    /// An assembly supplied explicitly by the caller — a corpus path, or a
    /// directory the user named — as opposed to one the resolver discovered
    /// beside the inspected artifact. See <see cref="Designated(string)"/>.
    /// </summary>
    public sealed record DesignatedAsset : AssemblyResolutionProvenance
    {
        public DesignatedAsset(string resolverSource)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(resolverSource);
            ResolverSource = resolverSource;
        }

        private protected override int Discriminator => 5;
        public string ResolverSource { get; }
    }
}

/// <summary>
/// Opaque reference-identity handle minted with one canonical acquisition
/// descriptor.
/// </summary>
public sealed class AssemblyAcquisitionRegistration
{
    readonly object _gate = new();
    Guid? _moduleVersionId;

    internal AssemblyAcquisitionRegistration(
        ArtifactAcquisitionRegistration? artifactRegistration = null)
    {
        ArtifactRegistration = artifactRegistration;
    }

    /// <summary>
    /// Exact artifact acquisition registration that authorized this assembly
    /// descriptor, when the descriptor was projected from an artifact.
    /// </summary>
    public ArtifactAcquisitionRegistration? ArtifactRegistration { get; }

    /// <summary>
    /// Module generation bound to the artifact-backed descriptor.
    /// </summary>
    public Guid? ModuleVersionId
    {
        get
        {
            lock (_gate)
                return _moduleVersionId;
        }
    }

    internal void BindModuleVersionId(Guid moduleVersionId)
    {
        if (moduleVersionId == Guid.Empty)
        {
            throw new BadImageFormatException(
                "The selected assembly has an empty module version identifier.");
        }

        lock (_gate)
        {
            if (_moduleVersionId is Guid existing
                && existing != moduleVersionId)
            {
                throw new BadImageFormatException(
                    "The opened assembly module version identifier does not "
                    + "match the artifact-bound acquisition descriptor.");
            }

            _moduleVersionId = moduleVersionId;
        }
    }
}

/// <summary>
/// Typed result of selecting an assembly acquisition descriptor from one
/// compatibility path or stream.
/// </summary>
public abstract class AssemblyDescriptorSelectionResult
{
    private protected AssemblyDescriptorSelectionResult()
    {
    }

    /// <summary>The selected image produced a valid assembly descriptor.</summary>
    public sealed class Ready : AssemblyDescriptorSelectionResult
    {
        internal Ready(ResolvedAssemblyReference reference)
        {
            ArgumentNullException.ThrowIfNull(reference);
            Reference = reference;
        }

        public ResolvedAssemblyReference Reference { get; }
    }

    /// <summary>
    /// The selected image is not a managed assembly and remains eligible for a
    /// descriptor-less compatibility path.
    /// </summary>
    public sealed class Descriptorless : AssemblyDescriptorSelectionResult
    {
        internal Descriptorless(Exception? compatibilityException)
        {
            CompatibilityException = compatibilityException;
        }

        internal Exception? CompatibilityException { get; }
    }

    /// <summary>
    /// The selected image has managed assembly metadata that could not produce
    /// a valid descriptor.
    /// </summary>
    public sealed class Rejected : AssemblyDescriptorSelectionResult
    {
        internal Rejected(
            CandidateOpenFailure failure,
            Exception? compatibilityException)
        {
            ArgumentNullException.ThrowIfNull(failure);
            Failure = failure;
            CompatibilityException = compatibilityException;
        }

        public CandidateOpenFailure Failure { get; }
        internal Exception? CompatibilityException { get; }
    }
}

/// <summary>
/// Roslyn-free descriptor for one assembly selected by an acquisition owner.
/// </summary>
public sealed class ResolvedAssemblyReference
{
    ResolvedAssemblyReference(
        AssemblyAcquisitionRegistration registration,
        AssemblyReferenceIdentity identity,
        string? path,
        Func<Stream> openRead,
        AssemblyResolutionProvenance provenance,
        DateTime? lastWriteTimeUtc)
    {
        Registration = registration;
        Identity = identity;
        Path = path;
        OpenRead = openRead;
        Provenance = provenance;
        LastWriteTimeUtc = lastWriteTimeUtc;
    }

    public static ResolvedAssemblyReference Create(
        AssemblyReferenceIdentity selectedIdentity,
        string? path,
        Func<Stream> openRead,
        AssemblyResolutionProvenance provenance,
        DateTime? lastWriteTimeUtc = null)
    {
        ArgumentNullException.ThrowIfNull(selectedIdentity);
        ArgumentException.ThrowIfNullOrWhiteSpace(selectedIdentity.Name);
        ArgumentNullException.ThrowIfNull(openRead);
        ArgumentNullException.ThrowIfNull(provenance);

        return new ResolvedAssemblyReference(
            new AssemblyAcquisitionRegistration(),
            selectedIdentity,
            path,
            openRead,
            provenance,
            lastWriteTimeUtc);
    }

    public static ResolvedAssemblyReference CreateFromPath(
        string path,
        AssemblyResolutionProvenance provenance)
        => CreateFromPathIfManaged(path, provenance)
            ?? throw new BadImageFormatException(
                "The selected image has no managed metadata.");

    /// <summary>
    /// Selects an assembly descriptor from a path while preserving a typed
    /// distinction between descriptor-less compatibility and rejected managed
    /// assembly metadata.
    /// </summary>
    public static AssemblyDescriptorSelectionResult SelectFromPath(
        string path,
        AssemblyResolutionProvenance provenance)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(provenance);

        string fullPath = System.IO.Path.GetFullPath(path);
        using FileStream stream = File.OpenRead(fullPath);
        return SelectDescriptor(
            stream,
            identity => Create(
                identity,
                fullPath,
                () => File.OpenRead(fullPath),
                provenance,
                File.GetLastWriteTimeUtc(stream.SafeFileHandle)));
    }

    /// <summary>
    /// Creates a descriptor for a managed assembly path, or returns
    /// <see langword="null"/> for a descriptor-less compatibility image.
    /// Rejected managed assembly metadata remains a visible failure.
    /// </summary>
    public static ResolvedAssemblyReference? CreateFromPathIfManaged(
        string path,
        AssemblyResolutionProvenance provenance)
        => DescriptorOrNull(SelectFromPath(path, provenance));

    /// <summary>
    /// Selects an assembly descriptor from a repeatable stream while
    /// preserving a typed distinction between descriptor-less compatibility
    /// and rejected managed assembly metadata.
    /// </summary>
    public static AssemblyDescriptorSelectionResult SelectFromStream(
        Func<Stream> openRead,
        AssemblyResolutionProvenance provenance,
        DateTime? lastWriteTimeUtc = null)
    {
        ArgumentNullException.ThrowIfNull(openRead);
        ArgumentNullException.ThrowIfNull(provenance);

        Stream? source = openRead();
        if (source is null || !source.CanRead)
        {
            source?.Dispose();
            throw new IOException(
                "The assembly opener did not return a readable stream.");
        }

        Stream stream = source;
        try
        {
            return SelectDescriptor(
                stream,
                identity => Create(
                    identity,
                    path: null,
                    openRead,
                    provenance,
                    lastWriteTimeUtc));
        }
        finally
        {
            // A failing Dispose must not replace the selection outcome, which
            // may already carry a typed rejection.
            OwnedResourceCleanup.DisposeWithoutReplacingOutcome(stream);
        }
    }

    /// <summary>
    /// Creates a descriptor for a managed assembly served by a repeatable
    /// stream factory, or returns <see langword="null"/> for a descriptor-less
    /// compatibility image. Rejected managed assembly metadata remains a
    /// visible failure.
    /// </summary>
    /// <remarks>
    /// This is the stream-only peer of
    /// <see cref="CreateFromPathIfManaged"/>, for acquisition owners whose
    /// content has no filesystem path (package archive entries, bundle
    /// content). <paramref name="openRead"/> must return a fresh, readable,
    /// seekable stream on every call for as long as the descriptor is used;
    /// the identity read here is revalidated against the image the consumer
    /// opens.
    /// </remarks>
    public static ResolvedAssemblyReference? CreateFromStreamIfManaged(
        Func<Stream> openRead,
        AssemblyResolutionProvenance provenance,
        DateTime? lastWriteTimeUtc = null)
        => DescriptorOrNull(
            SelectFromStream(
                openRead,
                provenance,
                lastWriteTimeUtc));

    /// <summary>
    /// Projects one authorized artifact registration into a managed assembly
    /// descriptor while preserving artifact correspondence.
    /// </summary>
    /// <remarks>
    /// <paramref name="openRead"/> remains the caller-supplied guarded content
    /// capability. Artifact access and lease validation stay with the artifact
    /// owner; this boundary decodes assembly identity and binds the non-empty
    /// module version identifier.
    /// </remarks>
    public static ResolvedAssemblyReference? CreateFromArtifactIfManaged(
        ArtifactAcquisitionRegistration artifactRegistration,
        Func<Stream> openRead,
        AssemblyResolutionProvenance provenance,
        DateTime? lastWriteTimeUtc = null)
    {
        ArgumentNullException.ThrowIfNull(artifactRegistration);
        return CreateFromStreamIfManagedCore(
            artifactRegistration,
            openRead,
            provenance,
            lastWriteTimeUtc);
    }

    static ResolvedAssemblyReference? CreateFromStreamIfManagedCore(
        ArtifactAcquisitionRegistration? artifactRegistration,
        Func<Stream> openRead,
        AssemblyResolutionProvenance provenance,
        DateTime? lastWriteTimeUtc)
    {
        ArgumentNullException.ThrowIfNull(openRead);
        ArgumentNullException.ThrowIfNull(provenance);

        Stream? source = openRead();
        if (source is null || !source.CanRead)
        {
            source?.Dispose();
            throw new IOException(
                "The assembly opener did not return a readable stream.");
        }

        using Stream stream = source;
        System.Reflection.PortableExecutable.PEReader? peReader = null;
        try
        {
            peReader =
                new System.Reflection.PortableExecutable.PEReader(
                    stream,
                    System.Reflection.PortableExecutable
                        .PEStreamOptions.LeaveOpen);
            if (!MetadataFormatAdmission.AdmitImage(peReader))
            {
                peReader.Dispose();
                return null;
            }
        }
        catch (Exception ex) when (
            ex is UnsupportedMetadataFormatException
                or MalformedMetadataRootException)
        {
            // This shape has no failure arm, so the mechanism propagates.
            peReader?.Dispose();
            throw;
        }
        catch (BadImageFormatException)
        {
            peReader?.Dispose();
            return null;
        }

        using (peReader)
        {
            MetadataReader metadata =
                MetadataFormatAdmission.GetMetadataReader(peReader);
            if (artifactRegistration is not null
                && !metadata.IsAssembly)
            {
                return null;
            }

            AssemblyReferenceIdentity identity =
                AssemblyReferenceIdentity.FromAssemblyDefinition(
                    metadata);
            if (string.IsNullOrWhiteSpace(identity.Name))
                return null;

            var registration =
                new AssemblyAcquisitionRegistration(artifactRegistration);
            if (artifactRegistration is not null)
            {
                ModuleDefinition module = metadata.GetModuleDefinition();
                registration.BindModuleVersionId(
                    metadata.GetGuid(module.Mvid));
            }

            return new ResolvedAssemblyReference(
                registration,
                identity,
                path: null,
                openRead,
                provenance,
                lastWriteTimeUtc);
        }
    }

    static AssemblyDescriptorSelectionResult SelectDescriptor(
        Stream stream,
        Func<AssemblyReferenceIdentity, ResolvedAssemblyReference>
            createDescriptor)
    {
        if (stream.CanSeek && !HasPortableExecutableSignature(stream))
        {
            return new AssemblyDescriptorSelectionResult.Descriptorless(
                compatibilityException: null);
        }

        System.Reflection.PortableExecutable.PEReader peReader;
        try
        {
            peReader =
                new System.Reflection.PortableExecutable.PEReader(
                    stream,
                    System.Reflection.PortableExecutable
                        .PEStreamOptions.LeaveOpen);
        }
        catch (BadImageFormatException)
        {
            return RejectDescriptorSelection(
                "The selected PE image has invalid headers.",
                compatibilityException: null);
        }

        using (peReader)
        {
            bool hasMetadata;
            try
            {
                hasMetadata = MetadataFormatAdmission.AdmitImage(peReader);
            }
            catch (UnsupportedMetadataFormatException unsupported)
            {
                // Selection has a failure arm, so the mechanism travels as the
                // compatibility exception rather than unwinding here. Callers
                // whose shape has no failure arm rethrow it unchanged.
                return RejectDescriptorSelection(
                    "The selected image uses an unsupported metadata format.",
                    unsupported);
            }
            catch (MalformedMetadataRootException malformed)
            {
                return RejectDescriptorSelection(
                    "The selected image has a malformed metadata root.",
                    malformed);
            }
            catch (BadImageFormatException)
            {
                return RejectDescriptorSelection(
                    "The selected PE image has invalid CLR or metadata structure.",
                    compatibilityException: null);
            }
            if (!hasMetadata)
            {
                if (MetadataFormatAdmission.HasDeclaredClrHeader(peReader))
                {
                    return RejectDescriptorSelection(
                        "The selected PE image has an invalid CLR header.",
                        compatibilityException: null);
                }

                return new AssemblyDescriptorSelectionResult.Descriptorless(
                    compatibilityException: null);
            }

            AssemblyReferenceIdentity identity;
            try
            {
                MetadataReader metadata =
                    MetadataFormatAdmission.GetMetadataReader(peReader);
                if (!metadata.IsAssembly)
                {
                    return new AssemblyDescriptorSelectionResult
                        .Descriptorless(
                            new BadImageFormatException(
                                "The metadata image is not an assembly."));
                }

                identity =
                    AssemblyReferenceIdentity.FromAssemblyDefinition(
                        metadata);
                if (string.IsNullOrWhiteSpace(identity.Name))
                {
                    return RejectDescriptorSelection(
                        "The selected managed assembly has no usable identity.",
                        compatibilityException: null);
                }
            }
            catch (Exception ex) when (
                ex is BadImageFormatException
                    or ArgumentOutOfRangeException
                    or OverflowException)
            {
                return RejectDescriptorSelection(
                    "The selected managed assembly contains invalid metadata.",
                    ex);
            }

            return new AssemblyDescriptorSelectionResult.Ready(
                createDescriptor(identity));
        }
    }

    static bool HasPortableExecutableSignature(Stream stream)
    {
        const int PeHeaderOffsetLocation = 0x3c;
        const uint PeSignature = 0x00004550;

        long position = stream.Position;
        try
        {
            if (stream.ReadByte() != 'M'
                || stream.ReadByte() != 'Z')
            {
                return false;
            }

            if (stream.Length - position
                < PeHeaderOffsetLocation + sizeof(int))
            {
                return false;
            }

            stream.Position = position + PeHeaderOffsetLocation;
            Span<byte> offsetBytes = stackalloc byte[sizeof(int)];
            stream.ReadExactly(offsetBytes);
            int peHeaderOffset =
                BinaryPrimitives.ReadInt32LittleEndian(offsetBytes);
            if (peHeaderOffset < 0
                || peHeaderOffset > stream.Length - position - sizeof(uint))
            {
                return false;
            }

            stream.Position = position + peHeaderOffset;
            Span<byte> signatureBytes = stackalloc byte[sizeof(uint)];
            stream.ReadExactly(signatureBytes);
            return BinaryPrimitives.ReadUInt32LittleEndian(signatureBytes)
                == PeSignature;
        }
        finally
        {
            stream.Position = position;
        }
    }

    static AssemblyDescriptorSelectionResult.Rejected
        RejectDescriptorSelection(
            string detail,
            Exception? compatibilityException) =>
        new(
            new CandidateOpenFailure(
                CandidateOpenFailureKind.InvalidImage,
                detail),
            compatibilityException);

    static ResolvedAssemblyReference? DescriptorOrNull(
        AssemblyDescriptorSelectionResult result) =>
        result switch
        {
            AssemblyDescriptorSelectionResult.Ready ready =>
                ready.Reference,
            AssemblyDescriptorSelectionResult.Descriptorless descriptorless =>
                PreserveCompatibilityResult(
                    descriptorless.CompatibilityException),
            AssemblyDescriptorSelectionResult.Rejected rejected =>
                PreserveCompatibilityResult(
                    rejected.CompatibilityException),
            _ => throw new InvalidOperationException(
                "Unknown assembly descriptor selection result."),
        };

    static ResolvedAssemblyReference? PreserveCompatibilityResult(
        Exception? compatibilityException)
    {
        if (compatibilityException is null)
            return null;

        System.Runtime.ExceptionServices.ExceptionDispatchInfo
            .Capture(compatibilityException)
            .Throw();
        return null;
    }

    /// <summary>
    /// Creates a stream-backed descriptor, using the image's real assembly
    /// identity when one can be decoded and <paramref name="fallbackIdentity"/>
    /// otherwise.
    /// </summary>
    /// <remarks>
    /// The fallback keeps a selected malformed, native, or module image visible
    /// as a participant. Opening that participant still validates the image and
    /// reports its typed acquisition failure; the fallback is not evidence that
    /// the image is a managed assembly.
    /// <c>PackageAssemblyContextRealizationTests.MalformedSelectedAsset_RemainsARejectedParticipant</c>
    /// gates that behavior through the package realization consumer.
    /// </remarks>
    public static ResolvedAssemblyReference CreateFromStreamWithFallbackIdentity(
        Func<Stream> openRead,
        AssemblyReferenceIdentity fallbackIdentity,
        AssemblyResolutionProvenance provenance,
        out bool usedFallbackIdentity,
        DateTime? lastWriteTimeUtc = null)
        => CreateFromStreamWithFallbackIdentityCore(
            artifactRegistration: null,
            openRead,
            fallbackIdentity,
            provenance,
            out usedFallbackIdentity,
            lastWriteTimeUtc);

    /// <summary>
    /// Projects one authorized artifact registration into an assembly
    /// descriptor, retaining decoded assembly identity and a non-empty MVID
    /// when available, or a fallback identity when identity cannot be decoded.
    /// </summary>
    /// <remarks>
    /// The fallback keeps a selected malformed, native, or module image visible
    /// as a rejection carrier while preserving exact artifact correspondence.
    /// An empty-MVID assembly keeps its decoded identity with no bound MVID.
    /// Neither case is successful assembly evidence; artifact-backed opens
    /// still validate and reject the image.
    /// </remarks>
    public static ResolvedAssemblyReference CreateFromArtifactWithFallbackIdentity(
        ArtifactAcquisitionRegistration artifactRegistration,
        Func<Stream> openRead,
        AssemblyReferenceIdentity fallbackIdentity,
        AssemblyResolutionProvenance provenance,
        out bool usedFallbackIdentity,
        DateTime? lastWriteTimeUtc = null)
    {
        ArgumentNullException.ThrowIfNull(artifactRegistration);
        return CreateFromStreamWithFallbackIdentityCore(
            artifactRegistration,
            openRead,
            fallbackIdentity,
            provenance,
            out usedFallbackIdentity,
            lastWriteTimeUtc);
    }

    static ResolvedAssemblyReference CreateFromStreamWithFallbackIdentityCore(
        ArtifactAcquisitionRegistration? artifactRegistration,
        Func<Stream> openRead,
        AssemblyReferenceIdentity fallbackIdentity,
        AssemblyResolutionProvenance provenance,
        out bool usedFallbackIdentity,
        DateTime? lastWriteTimeUtc)
    {
        ArgumentNullException.ThrowIfNull(openRead);
        ArgumentNullException.ThrowIfNull(fallbackIdentity);
        ArgumentNullException.ThrowIfNull(provenance);

        Stream? source = openRead();
        if (source is null || !source.CanRead)
        {
            source?.Dispose();
            throw new IOException(
                "The assembly opener did not return a readable stream.");
        }

        AssemblyReferenceIdentity? identity = null;
        Guid? moduleVersionId = null;
        Stream stream = source;
        try
        {
            try
            {
                using var peReader =
                    new System.Reflection.PortableExecutable.PEReader(
                        stream,
                        System.Reflection.PortableExecutable
                            .PEStreamOptions.LeaveOpen);
                if (MetadataFormatAdmission.AdmitImage(peReader))
                {
                    MetadataReader metadata =
                        MetadataFormatAdmission.GetMetadataReader(peReader);
                    if (metadata.IsAssembly)
                    {
                        AssemblyReferenceIdentity candidate =
                            AssemblyReferenceIdentity.FromAssemblyDefinition(
                                metadata);
                        if (!string.IsNullOrWhiteSpace(candidate.Name))
                        {
                            identity = candidate;
                            if (artifactRegistration is not null)
                            {
                                ModuleDefinition module =
                                    metadata.GetModuleDefinition();
                                Guid candidateModuleVersionId =
                                    metadata.GetGuid(module.Mvid);
                                if (candidateModuleVersionId != Guid.Empty)
                                {
                                    moduleVersionId =
                                        candidateModuleVersionId;
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex) when (
                ex is BadImageFormatException
                    or UnsupportedMetadataFormatException
                    or OverflowException)
            {
                // The fallback path exists to keep a supplied identity usable
                // when the image cannot supply one. The descriptor retains the
                // selected image as a rejection carrier.
            }
        }
        finally
        {
            // A failing Dispose must not prevent the fallback descriptor.
            OwnedResourceCleanup.DisposeWithoutReplacingOutcome(stream);
        }

        usedFallbackIdentity = identity is null;
        if (usedFallbackIdentity)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(
                fallbackIdentity.Name);
        }
        var registration =
            new AssemblyAcquisitionRegistration(artifactRegistration);
        if (moduleVersionId is Guid value)
            registration.BindModuleVersionId(value);
        return new ResolvedAssemblyReference(
            registration,
            identity ?? fallbackIdentity,
            path: null,
            openRead,
            provenance,
            lastWriteTimeUtc);
    }

    public static bool TryCreateFromPath(
        string path,
        AssemblyResolutionProvenance provenance,
        [NotNullWhen(true)] out ResolvedAssemblyReference? reference)
        => TryCreateFromPath(
            path,
            provenance,
            out reference,
            out _);

    public static bool TryCreateFromPath(
        string path,
        AssemblyResolutionProvenance provenance,
        [NotNullWhen(true)] out ResolvedAssemblyReference? reference,
        out Exception? failure)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(provenance);

        try
        {
            reference = CreateFromPath(path, provenance);
            failure = null;
            return true;
        }
        catch (Exception ex) when (
            ex is IOException
                or UnauthorizedAccessException
                or NotSupportedException
                or ObjectDisposedException
                or BadImageFormatException
                or ArgumentOutOfRangeException
                or OverflowException)
        {
            reference = null;
            failure = ex;
            return false;
        }
    }

    public AssemblyAcquisitionRegistration Registration { get; }
    public AssemblyReferenceIdentity Identity { get; }
    public string? Path { get; }
    /// <summary>
    /// Opens a fresh readable stream for this descriptor.
    /// </summary>
    /// <remarks>
    /// The acquisition callback opens content only. It must not perform
    /// inspection or reenter a consumer of this descriptor.
    /// </remarks>
    public Func<Stream> OpenRead { get; }
    public AssemblyResolutionProvenance Provenance { get; }
    /// <summary>
    /// Last write time captured by the acquisition owner for the content
    /// returned by <see cref="OpenRead"/>, when available.
    /// </summary>
    public DateTime? LastWriteTimeUtc { get; }

    internal void ValidateArtifactContent(PEReader peReader)
    {
        ArgumentNullException.ThrowIfNull(peReader);
        if (Registration.ArtifactRegistration is null)
            return;
        if (!MetadataFormatAdmission.AdmitImage(peReader))
        {
            throw new BadImageFormatException(
                "The artifact-bound assembly image has no managed metadata.");
        }

        MetadataReader metadata =
            MetadataFormatAdmission.GetMetadataReader(peReader);
        if (!metadata.IsAssembly)
        {
            throw new BadImageFormatException(
                "The artifact-bound image is a module, not an assembly.");
        }

        AssemblyReferenceIdentity actual =
            AssemblyReferenceIdentity.FromAssemblyDefinition(metadata);
        if (actual != Identity)
        {
            throw new BadImageFormatException(
                "The opened assembly identity does not match the "
                + "artifact-bound acquisition descriptor.");
        }

        ModuleDefinition module = metadata.GetModuleDefinition();
        Registration.BindModuleVersionId(metadata.GetGuid(module.Mvid));
    }

    /// <summary>
    /// Returns this descriptor with the same acquisition registration and an
    /// observer for cancellation raised while opening or using its content
    /// stream.
    /// </summary>
    /// <remarks>
    /// `InspectionAcquisitionPlanTests.ObserveOpenReadCancellation_PreservesRegistrationAndReportsStreamOperationCancellation`
    /// gates both properties.
    /// </remarks>
    public ResolvedAssemblyReference ObserveOpenReadCancellation(
        Action<OperationCanceledException> observer)
    {
        ArgumentNullException.ThrowIfNull(observer);
        return WithOpenRead(
            () =>
            {
                try
                {
                    return new CancellationObservingStream(
                        OpenRead(),
                        observer);
                }
                catch (OperationCanceledException ex)
                {
                    observer(ex);
                    throw;
                }
            },
            LastWriteTimeUtc);
    }

    /// <summary>
    /// Returns a content-only view with the same acquisition registration and
    /// no filesystem path.
    /// </summary>
    /// <remarks>
    /// `InspectionAcquisitionPlanTests.WithoutLocalPath_PreservesRegistrationAndAcquisition`
    /// gates the preserved descriptor properties.
    /// </remarks>
    public ResolvedAssemblyReference WithoutLocalPath()
        => Path is null
            ? this
            : new ResolvedAssemblyReference(
                Registration,
                Identity,
                path: null,
                OpenRead,
                Provenance,
                LastWriteTimeUtc);

    internal ResolvedAssemblyReference WithOpenRead(
        Func<Stream> openRead,
        DateTime? lastWriteTimeUtc)
    {
        ArgumentNullException.ThrowIfNull(openRead);
        return new ResolvedAssemblyReference(
            Registration,
            Identity,
            Path,
            openRead,
            Provenance,
            lastWriteTimeUtc);
    }

    sealed class CancellationObservingStream(
        Stream inner,
        Action<OperationCanceledException> observer)
        : Stream
    {
        public override bool CanRead =>
            Observe(() => inner.CanRead);
        public override bool CanSeek =>
            Observe(() => inner.CanSeek);
        public override bool CanWrite =>
            Observe(() => inner.CanWrite);
        public override bool CanTimeout =>
            Observe(() => inner.CanTimeout);
        public override long Length =>
            Observe(() => inner.Length);
        public override long Position
        {
            get => Observe(() => inner.Position);
            set => Observe(() => inner.Position = value);
        }
        public override int ReadTimeout
        {
            get => Observe(() => inner.ReadTimeout);
            set => Observe(() => inner.ReadTimeout = value);
        }
        public override int WriteTimeout
        {
            get => Observe(() => inner.WriteTimeout);
            set => Observe(() => inner.WriteTimeout = value);
        }

        public override void Flush() =>
            Observe(inner.Flush);

        public override Task FlushAsync(
            CancellationToken cancellationToken) =>
            ObserveAsync(
                () => inner.FlushAsync(cancellationToken));

        public override void CopyTo(
            Stream destination,
            int bufferSize) =>
            Observe(
                () => inner.CopyTo(
                    destination,
                    bufferSize));

        public override Task CopyToAsync(
            Stream destination,
            int bufferSize,
            CancellationToken cancellationToken) =>
            ObserveAsync(
                () => inner.CopyToAsync(
                    destination,
                    bufferSize,
                    cancellationToken));

        public override int Read(
            byte[] buffer,
            int offset,
            int count) =>
            Observe(
                () => inner.Read(buffer, offset, count));

        public override int Read(Span<byte> buffer)
        {
            try
            {
                return inner.Read(buffer);
            }
            catch (OperationCanceledException ex)
            {
                observer(ex);
                throw;
            }
        }

        public override int ReadByte() =>
            Observe(inner.ReadByte);

        public override Task<int> ReadAsync(
            byte[] buffer,
            int offset,
            int count,
            CancellationToken cancellationToken) =>
            ObserveAsync(
                () => inner.ReadAsync(
                    buffer,
                    offset,
                    count,
                    cancellationToken));

        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default) =>
            ObserveAsync(
                () => inner.ReadAsync(
                    buffer,
                    cancellationToken));

        public override IAsyncResult BeginRead(
            byte[] buffer,
            int offset,
            int count,
            AsyncCallback? callback,
            object? state) =>
            Observe(
                () => inner.BeginRead(
                    buffer,
                    offset,
                    count,
                    callback,
                    state));

        public override int EndRead(
            IAsyncResult asyncResult) =>
            Observe(() => inner.EndRead(asyncResult));

        public override long Seek(
            long offset,
            SeekOrigin origin) =>
            Observe(() => inner.Seek(offset, origin));

        public override void SetLength(long value) =>
            Observe(() => inner.SetLength(value));

        public override void Write(
            byte[] buffer,
            int offset,
            int count) =>
            Observe(
                () => inner.Write(buffer, offset, count));

        public override void Write(
            ReadOnlySpan<byte> buffer)
        {
            try
            {
                inner.Write(buffer);
            }
            catch (OperationCanceledException ex)
            {
                observer(ex);
                throw;
            }
        }

        public override Task WriteAsync(
            byte[] buffer,
            int offset,
            int count,
            CancellationToken cancellationToken) =>
            ObserveAsync(
                () => inner.WriteAsync(
                    buffer,
                    offset,
                    count,
                    cancellationToken));

        public override ValueTask WriteAsync(
            ReadOnlyMemory<byte> buffer,
            CancellationToken cancellationToken = default) =>
            ObserveAsync(
                () => inner.WriteAsync(
                    buffer,
                    cancellationToken));

        public override IAsyncResult BeginWrite(
            byte[] buffer,
            int offset,
            int count,
            AsyncCallback? callback,
            object? state) =>
            Observe(
                () => inner.BeginWrite(
                    buffer,
                    offset,
                    count,
                    callback,
                    state));

        public override void EndWrite(
            IAsyncResult asyncResult) =>
            Observe(() => inner.EndWrite(asyncResult));

        public override void WriteByte(byte value) =>
            Observe(() => inner.WriteByte(value));

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                Observe(inner.Dispose);
            base.Dispose(disposing);
        }

        public override ValueTask DisposeAsync() =>
            ObserveDisposeCompletionAsync(
                Observe(inner.DisposeAsync));

        T Observe<T>(Func<T> operation)
        {
            try
            {
                return operation();
            }
            catch (OperationCanceledException ex)
            {
                observer(ex);
                throw;
            }
        }

        void Observe(Action operation)
        {
            try
            {
                operation();
            }
            catch (OperationCanceledException ex)
            {
                observer(ex);
                throw;
            }
        }

        Task ObserveAsync(Func<Task> operation) =>
            ObserveCompletionAsync(Observe(operation));

        Task<int> ObserveAsync(
            Func<Task<int>> operation) =>
            ObserveCompletionAsync(Observe(operation));

        ValueTask ObserveAsync(
            Func<ValueTask> operation) =>
            ObserveCompletionAsync(Observe(operation));

        ValueTask<int> ObserveAsync(
            Func<ValueTask<int>> operation) =>
            ObserveCompletionAsync(Observe(operation));

        async Task ObserveCompletionAsync(Task operation)
        {
            try
            {
                await operation.ConfigureAwait(false);
            }
            catch (OperationCanceledException ex)
            {
                observer(ex);
                throw;
            }
        }

        async Task<int> ObserveCompletionAsync(Task<int> operation)
        {
            try
            {
                return await operation.ConfigureAwait(false);
            }
            catch (OperationCanceledException ex)
            {
                observer(ex);
                throw;
            }
        }

        async ValueTask ObserveCompletionAsync(ValueTask operation)
        {
            try
            {
                await operation.ConfigureAwait(false);
            }
            catch (OperationCanceledException ex)
            {
                observer(ex);
                throw;
            }
        }

        async ValueTask<int> ObserveCompletionAsync(
            ValueTask<int> operation)
        {
            try
            {
                return await operation.ConfigureAwait(false);
            }
            catch (OperationCanceledException ex)
            {
                observer(ex);
                throw;
            }
        }

        async ValueTask ObserveDisposeCompletionAsync(
            ValueTask operation)
        {
            try
            {
                await operation.ConfigureAwait(false);
            }
            catch (OperationCanceledException ex)
            {
                observer(ex);
                throw;
            }
            GC.SuppressFinalize(this);
        }
    }
}

/// <summary>Identifies one acquisition catalog's candidate key space.</summary>
public readonly record struct AssemblyCatalogId(Guid Value);

/// <summary>Opaque identity for one frozen generation in a catalog.</summary>
public sealed class AssemblyCatalogGenerationId
{
    internal AssemblyCatalogGenerationId()
    {
    }
}

internal readonly record struct AssemblyCandidateId(Guid Value);

/// <summary>One descriptor interned by a Metadata-owned acquisition catalog.</summary>
public sealed class ResolvedAssemblyCandidate
{
    internal ResolvedAssemblyCandidate(
        AssemblyCatalogId catalog,
        AssemblyCandidateId id,
        ResolvedAssemblyReference assembly)
    {
        Catalog = catalog;
        Id = id;
        Assembly = assembly;
    }

    internal AssemblyCatalogId Catalog { get; }
    internal AssemblyCandidateId Id { get; }
    public ResolvedAssemblyReference Assembly { get; }
}
