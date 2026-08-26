using System.Diagnostics.CodeAnalysis;
using System.Reflection.Metadata;

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
    Guid? _contentModuleVersionId;

    internal AssemblyAcquisitionRegistration()
    {
    }

    internal Guid? ContentModuleVersionId
    {
        get
        {
            lock (_gate)
                return _contentModuleVersionId;
        }
    }

    internal void BindContentModuleVersionId(Guid moduleVersionId)
    {
        lock (_gate)
        {
            if (_contentModuleVersionId is null)
            {
                _contentModuleVersionId = moduleVersionId;
                return;
            }

            if (_contentModuleVersionId != moduleVersionId)
            {
                throw new BadImageFormatException(
                    "The opened image does not match the acquisition "
                    + $"registration MVID '{_contentModuleVersionId}'.");
            }
        }
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
        Guid? moduleVersionId,
        string? path,
        Func<Stream> openRead,
        AssemblyResolutionProvenance provenance,
        DateTime? lastWriteTimeUtc)
    {
        Registration = registration;
        Identity = identity;
        ModuleVersionId = moduleVersionId;
        Path = path;
        OpenRead = openRead;
        Provenance = provenance;
        LastWriteTimeUtc = lastWriteTimeUtc;
        if (moduleVersionId is { } selectedModuleVersionId)
            Registration.BindContentModuleVersionId(selectedModuleVersionId);
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
            moduleVersionId: null,
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
    /// Creates a descriptor for a managed assembly path, or returns
    /// <see langword="null"/> when the PE image has no managed metadata.
    /// Malformed managed metadata remains a visible failure.
    /// </summary>
    public static ResolvedAssemblyReference? CreateFromPathIfManaged(
        string path,
        AssemblyResolutionProvenance provenance)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(provenance);

        string fullPath = System.IO.Path.GetFullPath(path);
        using FileStream stream = File.OpenRead(fullPath);
        System.Reflection.PortableExecutable.PEReader? peReader = null;
        try
        {
            peReader =
                new System.Reflection.PortableExecutable.PEReader(stream);
            if (!peReader.HasMetadata)
            {
                peReader.Dispose();
                return null;
            }
        }
        catch (BadImageFormatException)
        {
            peReader?.Dispose();
            return null;
        }

        using (peReader)
        {
            MetadataReader metadata = peReader.GetMetadataReader();
            if (!metadata.IsAssembly)
                return null;

            AssemblyReferenceIdentity identity =
                AssemblyReferenceIdentity.FromAssemblyDefinition(
                    metadata);
            if (string.IsNullOrWhiteSpace(identity.Name))
                return null;

            ResolvedAssemblyReference reference = Create(
                identity,
                fullPath,
                () => File.OpenRead(fullPath),
                provenance,
                File.GetLastWriteTimeUtc(stream.SafeFileHandle));
            reference.Registration.BindContentModuleVersionId(
                metadata.GetGuid(metadata.GetModuleDefinition().Mvid));
            return reference;
        }
    }

    /// <summary>
    /// Creates a descriptor for a managed netmodule path, or returns
    /// <see langword="null"/> when the image is an assembly or has no managed
    /// metadata. The module name is a diagnostic label, not an assembly
    /// binding identity; <see cref="ModuleVersionId"/> binds immutable
    /// snapshots to the selected module.
    /// </summary>
    public static ResolvedAssemblyReference? CreateFromModulePathIfManaged(
        string path,
        AssemblyResolutionProvenance provenance)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(provenance);

        string fullPath = System.IO.Path.GetFullPath(path);
        using FileStream stream = File.OpenRead(fullPath);
        try
        {
            using var peReader =
                new System.Reflection.PortableExecutable.PEReader(stream);
            if (!peReader.HasMetadata)
                return null;

            MetadataReader metadata = peReader.GetMetadataReader();
            if (metadata.IsAssembly)
                return null;

            ModuleDefinition module = metadata.GetModuleDefinition();
            string moduleName = metadata.GetString(module.Name);
            if (string.IsNullOrWhiteSpace(moduleName))
                return null;

            return new ResolvedAssemblyReference(
                new AssemblyAcquisitionRegistration(),
                new AssemblyReferenceIdentity(
                    moduleName,
                    Version: null,
                    Culture: null,
                    PublicKeyToken: null),
                metadata.GetGuid(module.Mvid),
                fullPath,
                () => File.OpenRead(fullPath),
                provenance,
                File.GetLastWriteTimeUtc(stream.SafeFileHandle));
        }
        catch (BadImageFormatException)
        {
            return null;
        }
    }

    /// <summary>
    /// Creates the typed acquisition carrier used by inspection roots for
    /// either an assembly or a managed netmodule.
    /// </summary>
    public static ResolvedAssemblyReference? CreateInspectionReferenceFromPathIfManaged(
        string path,
        AssemblyResolutionProvenance provenance) =>
        CreateFromPathIfManaged(path, provenance)
        ?? CreateFromModulePathIfManaged(path, provenance);

    /// <summary>
    /// Creates a descriptor for a managed assembly served by a repeatable
    /// stream factory, or returns <see langword="null"/> when the image has no
    /// managed metadata. Malformed managed metadata remains a visible failure.
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
                new System.Reflection.PortableExecutable.PEReader(stream);
            if (!peReader.HasMetadata)
            {
                peReader.Dispose();
                return null;
            }
        }
        catch (BadImageFormatException)
        {
            peReader?.Dispose();
            return null;
        }

        using (peReader)
        {
            AssemblyReferenceIdentity identity =
                AssemblyReferenceIdentity.FromAssemblyDefinition(
                    peReader.GetMetadataReader());
            if (string.IsNullOrWhiteSpace(identity.Name))
                return null;

            ResolvedAssemblyReference reference = Create(
                identity,
                path: null,
                openRead,
                provenance,
                lastWriteTimeUtc);
            reference.Registration.BindContentModuleVersionId(
                peReader.GetMetadataReader().GetGuid(
                    peReader.GetMetadataReader().GetModuleDefinition().Mvid));
            return reference;
        }
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
    /// <summary>
    /// The selected netmodule's MVID, or <see langword="null"/> for an
    /// assembly descriptor.
    /// </summary>
    public Guid? ModuleVersionId { get; }
    /// <summary>
    /// The module generation bound to this acquisition registration. Assembly
    /// descriptors bind it when selected or on their first verified open;
    /// netmodule descriptors bind it at creation.
    /// </summary>
    public Guid? ContentModuleVersionId =>
        Registration.ContentModuleVersionId;
    public bool IsAssembly => ModuleVersionId is null;
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
                ModuleVersionId,
                path: null,
                OpenRead,
                Provenance,
                LastWriteTimeUtc);

    /// <summary>
    /// Returns an immutable-content view of this acquisition after verifying
    /// that the supplied image has the selected assembly identity and bound
    /// module generation.
    /// </summary>
    /// <remarks>
    /// `InspectionAcquisitionPlanTests.WithContentSnapshot_PreservesRegistrationAndAcquisition`
    /// and
    /// `InspectionAcquisitionPlanTests.WithContentSnapshot_RejectsDifferentAssemblyIdentity`,
    /// plus
    /// `InspectionAcquisitionPlanTests.WithContentSnapshot_RejectsDifferentBoundModuleGeneration`
    /// gate the two properties.
    /// </remarks>
    public ResolvedAssemblyReference WithContentSnapshot(
        System.Collections.Immutable.ImmutableArray<byte> image)
    {
        if (image.IsDefaultOrEmpty)
        {
            throw new ArgumentException(
                "The prefetched PE image must not be empty.",
                nameof(image));
        }

        using var peReader =
            new System.Reflection.PortableExecutable.PEReader(image);
        if (!peReader.HasMetadata)
        {
            throw new BadImageFormatException(
                $"No managed metadata: {Path ?? Identity.Name}");
        }

        MetadataReader metadata = peReader.GetMetadataReader();
        ValidateOpenedMetadata(metadata);

        return new ResolvedAssemblyReference(
            Registration,
            Identity,
            ModuleVersionId,
            Path,
            () => new MemoryStream(image.ToArray(), writable: false),
            Provenance,
            LastWriteTimeUtc);
    }

    /// <summary>
    /// Verifies that opened metadata still represents this acquired assembly
    /// or netmodule.
    /// </summary>
    /// <remarks>
    /// `DescriptorContentIdentityTests` gates descriptor-backed decompiler
    /// opens; `InspectionAcquisitionPlanTests.WithContentSnapshot_*` and
    /// `InspectionAcquisitionPlanTests.ModuleContentSnapshot_*` gate immutable
    /// snapshots.
    /// </remarks>
    public void ValidateOpenedMetadata(MetadataReader metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        Guid openedModuleVersionId = metadata.GetGuid(
            metadata.GetModuleDefinition().Mvid);
        if (ModuleVersionId is { } expectedModuleVersionId)
        {
            if (metadata.IsAssembly
                || openedModuleVersionId != expectedModuleVersionId)
            {
                throw new BadImageFormatException(
                    "The opened image does not match the acquired "
                    + $"netmodule MVID '{expectedModuleVersionId}'.");
            }
        }
        else
        {
            if (!metadata.IsAssembly)
            {
                throw new BadImageFormatException(
                    "The opened image is a netmodule, not the acquired "
                    + $"assembly '{Identity}'.");
            }

            AssemblyReferenceIdentity actual =
                AssemblyReferenceIdentity.FromAssemblyDefinition(metadata);
            if (!Identity.IsEquivalentTo(actual))
            {
                throw new BadImageFormatException(
                    $"The opened image identity '{actual}' does not match "
                    + $"the acquired assembly identity '{Identity}'.");
            }
        }
        Registration.BindContentModuleVersionId(openedModuleVersionId);
    }

    internal ResolvedAssemblyReference WithOpenRead(
        Func<Stream> openRead,
        DateTime? lastWriteTimeUtc)
    {
        ArgumentNullException.ThrowIfNull(openRead);
        return new ResolvedAssemblyReference(
            Registration,
            Identity,
            ModuleVersionId,
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
