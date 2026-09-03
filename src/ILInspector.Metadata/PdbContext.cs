using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Runtime.ExceptionServices;
using ILInspector.MetadataPrimitives;

namespace ILInspector.Metadata;

/// <summary>
/// CodeView debug info needed for symbol server lookup (no SRM types in signature).
/// </summary>
public record CodeViewInfo(
    Guid Guid,
    int Age,
    string PdbFileName,
    bool IsPortable,
    uint Stamp = 0);

/// <summary>
/// Source document info for strict verification (no SRM types in signature).
/// <paramref name="Checksum"/> is the document hash recorded in the PDB and
/// <paramref name="ChecksumAlgorithm"/> its algorithm name (e.g. "SHA256"); both may be null.
/// </summary>
public record PdbDocumentInfo(
    string FilePath,
    byte[]? Checksum = null,
    string? ChecksumAlgorithm = null,
    int DocumentRowId = 0);

public enum PdbCustomDebugInformationStatus
{
    Absent,
    Present,
    Duplicate,
}

/// <summary>
/// One custom-debug-information value selected by parent and kind.
/// A duplicate is reported without choosing or materializing either value.
/// </summary>
public sealed record PdbCustomDebugInformationResult(
    PdbCustomDebugInformationStatus Status,
    byte[]? Value,
    string? Error = null,
    int ValueLength = 0,
    bool LimitExceeded = false);

/// <summary>A PDB resource exceeded a pre-materialization limit.</summary>
public sealed class PdbResourceLimitException(
    string message,
    long actualBytes,
    long limitBytes)
    : IOException(message)
{
    public long ActualBytes { get; } = actualBytes;
    public long LimitBytes { get; } = limitBytes;
}

/// <summary>A shared pre-decompression budget for one or more embedded PDBs.</summary>
public sealed class PdbExpansionBudget
{
    private readonly object _gate = new();
    private long _reservedBytes;

    public PdbExpansionBudget(long maxBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maxBytes);
        MaxBytes = maxBytes;
    }

    public long MaxBytes { get; }

    public long ReservedBytes
    {
        get
        {
            lock (_gate)
                return _reservedBytes;
        }
    }

    public long RemainingBytes => MaxBytes - ReservedBytes;

    internal void Reserve(int bytes)
    {
        lock (_gate)
        {
            long remaining = MaxBytes - _reservedBytes;
            if (bytes > remaining)
            {
                int reportedLimit =
                    remaining > int.MaxValue
                        ? int.MaxValue
                        : (int)remaining;
                throw new PdbResourceLimitException(
                    $"The embedded portable PDB's declared {bytes} decompressed bytes "
                    + $"exceed the aggregate budget's {remaining} remaining bytes.",
                    bytes,
                    reportedLimit);
            }

            _reservedBytes += bytes;
        }
    }
}

/// <summary>
/// A method-to-document relationship extracted from portable-PDB sequence points.
/// The metadata token and document row identify same-version coordinates; the
/// member anchor provides cross-version identity.
/// </summary>
public sealed record PdbMemberDocumentInfo(
    MemberAnchor Anchor,
    int MetadataToken,
    int DocumentRowId,
    string FilePath,
    int StartLine,
    int EndLine,
    bool IsPrimaryDocument = false,
    bool IsFinalizer = false)
{
    /// <summary>
    /// Sorted distinct 1-based start lines of this method's visible sequence points in this
    /// document. Points from another document are never mixed into this collection.
    /// </summary>
    public ImmutableArray<int> SequencePointStartLines { get; init; } = [];
}

/// <summary>A method's portable-PDB document and visible source range.</summary>
public sealed record PdbMethodDocumentInfo(
    string FilePath,
    int StartLine,
    int EndLine,
    byte[]? Checksum = null,
    string? ChecksumAlgorithm = null)
{
    /// <summary>Sorted distinct visible sequence-point start lines in <see cref="FilePath"/>.</summary>
    public ImmutableArray<int> SequencePointStartLines { get; init; } = [];
}

/// <summary>A source location recovered from portable-PDB sequence points.</summary>
public sealed record PdbILOffsetLocation(
    string? MethodName,
    string FilePath,
    int Line,
    int MatchedOffset,
    int DocumentRowId);

/// <summary>A portable-PDB document identity and its authored path.</summary>
public sealed record PdbDocumentReference(
    int DocumentRowId,
    string FilePath);

/// <summary>Documents associated with one metadata type through method debug information.</summary>
public sealed record PdbTypeDocumentInfo(
    string TypeFullName,
    string TypeSimpleName,
    IReadOnlyList<PdbDocumentReference> Documents)
{
    public MetadataTypeDefinitionName? DefinitionName { get; init; }

    public IReadOnlyList<string> FilePaths =>
        Documents.Select(static document => document.FilePath).ToArray();
}

public record ILOffsetMemberContextInfo(
    string? Assembly,
    string Type,
    string TypeKind,
    string Member,
    string Signature,
    string MemberKind,
    string Visibility,
    bool Static,
    string? Async,
    int MetadataToken,
    int ILOffset);

public record ILOffsetExceptionContextInfo(
    int Region,
    string Context,
    string Clause,
    int TryStart,
    int TryEnd,
    int HandlerStart,
    int HandlerEnd,
    int? FilterStart,
    int? FilterEnd,
    string? CaughtType);

public record MethodExceptionRegionInfo(
    int Region,
    string Clause,
    int TryStart,
    int TryEnd,
    int HandlerStart,
    int HandlerEnd,
    int? FilterStart,
    int? FilterEnd,
    string? CaughtType);

/// <summary>
/// Wraps PE + PDB readers, exposes high-level operations with no SRM in public signatures.
/// CLI orchestrates PDB acquisition (download via Packages), then calls back into this context.
/// </summary>
public class PdbContext : IDisposable
{
    private const int DebugDirectoryEntrySize = 28;
    internal const int MaxDebugDirectoryEntries = 64;
    internal const int MaxCodeViewDataBytes = 4 * 1024;

    private readonly PEReader _peReader;
    private readonly Stream _peStream;
    private readonly long _peImageStart;
    private readonly bool _entireImagePrefetched;
    private readonly Action<string>? _log;
    private readonly string? _assemblyPath;
    private readonly string _assemblyDisplayName;

    private MetadataReaderProvider? _pdbProvider;
    private MetadataReader? _pdbReader;
    private Exception? _deferredDisposalFailure;
    private bool? _isReferenceAssembly;
    private readonly List<IDisposable> _disposables = [];
    private MethodBodySource? _methodBodies;
    private bool _disposed;

    /// <summary>
    /// The path to the assembly file that was opened.
    /// </summary>
    public string AssemblyPath => _assemblyPath
        ?? throw new InvalidOperationException(
            "This assembly was opened from a descriptor without a filesystem path.");

    /// <summary>
    /// The acquisition-owned path, when the descriptor supplied one.
    /// </summary>
    public string? AssemblyPathOrNull => _assemblyPath;

    /// <summary>
    /// The log callback, if any.
    /// </summary>
    internal Action<string>? Log => _log;

    /// <summary>
    /// Session-bound method-body and operand access without exposing raw readers.
    /// </summary>
    public MethodBodySource MethodBodies
    {
        get
        {
            EnsureAlive();
            return _methodBodies ??= new MethodBodySource(_peReader, EnsureAlive);
        }
    }

    /// <summary>
    /// Gets immutable content for the fully prefetched PE image. Consumers can
    /// inspect this snapshot without reopening the target file or receiving the
    /// context-owned reader.
    /// </summary>
    public ImmutableArray<byte> GetPrefetchedImage()
    {
        EnsureAlive();
        if (!_entireImagePrefetched)
        {
            throw new InvalidOperationException(
                "Parallel body analysis requires a fully prefetched PE image.");
        }

        return _peReader.GetEntireImage().GetContent();
    }

    // --- PE/Assembly ---
    public bool HasMetadata => _peReader.HasMetadata;

    /// <summary>
    /// File size captured at open time (avoids repeated fstat syscalls).
    /// </summary>
    public long FileSize { get; }

    /// <summary>
    /// Last write time retained from the authoritative acquisition or captured
    /// from the open file handle.
    /// </summary>
    public DateTime LastWriteTimeUtc { get; }

    // --- Debug directory (POCO) ---
    public bool HasReproducibleFlag { get; private set; }
    public bool HasEmbeddedPdb { get; private set; }
    public int EmbeddedPdbSize { get; private set; }
    public string? CodeViewPdbPath { get; private set; }

    /// <summary>
    /// On-disk path to a successfully loaded *portable* PDB file (standalone or downloaded from a
    /// symbol server), or null for embedded/Windows/no PDB. Lets callers (e.g. the decompiler)
    /// reuse the acquired symbols for local-variable names after this context is disposed.
    /// </summary>
    public string? PortablePdbPath { get; private set; }
    public bool? HasNormalizedPaths { get; private set; }
    public List<string>? NonNormalizedPaths { get; private set; }

    // --- PDB acquisition ---
    public CodeViewInfo? PdbId { get; private set; }
    public bool NeedsPdb => PdbId != null && !HasPdb;
    public bool HasPdb { get; private set; }
    public int PdbVersion { get; private set; }
    public bool WindowsPdbDetected { get; set; }
    public string? PdbFormat { get; private set; }
    public string? PdbLocation { get; private set; }
    public string? SymbolServer { get; private set; }

    private PdbContext(
        Stream peStream,
        PEReader peReader,
        long peImageStart,
        string? assemblyPath,
        string assemblyDisplayName,
        Action<string>? log,
        bool entireImagePrefetched,
        DateTime? lastWriteTimeUtc)
    {
        _peStream = peStream;
        _peReader = peReader;
        _peImageStart = peImageStart;
        _entireImagePrefetched = entireImagePrefetched;
        _assemblyPath = assemblyPath;
        _assemblyDisplayName = assemblyDisplayName;
        _log = log;
        FileSize = peStream.Length - peImageStart;
        LastWriteTimeUtc = peStream is FileStream fileStream
            ? File.GetLastWriteTimeUtc(fileStream.SafeFileHandle)
            : lastWriteTimeUtc ?? default;
    }

    /// <summary>
    /// Opens a PE file and probes for PDB (embedded, then standalone adjacent).
    /// After return, check NeedsPdb to see if CLI should download a PDB.
    /// </summary>
    public static PdbContext Open(string assemblyPath, Action<string>? log = null)
        => Open(assemblyPath, log, PEStreamOptions.Default);

    /// <summary>
    /// Opens the PE image and reads its debug directory without loading an embedded or adjacent
    /// PDB. Used by latency-bounded metadata discovery that does not need source documents.
    /// </summary>
    public static PdbContext OpenMetadataOnly(string assemblyPath, Action<string>? log = null)
        => Open(
            assemblyPath,
            log,
            PEStreamOptions.Default,
            loadLocalPdb: false,
            loadEmbeddedPdb: false);

    /// <summary>
    /// Opens PE metadata and an embedded portable PDB, but never probes an adjacent PDB.
    /// </summary>
    /// <remarks>
    /// <c>PdbContextDescriptorTests.MetadataOnlyAndEmbeddedOnly_KeepTheirPdbAcquisitionBoundaries</c>
    /// gates the embedded-only acquisition boundary and the metadata-only close negative.
    /// </remarks>
    public static PdbContext OpenEmbeddedPdbOnly(
        string assemblyPath,
        Action<string>? log = null)
        => OpenEmbeddedPdbOnly(assemblyPath, int.MaxValue, log);

    /// <summary>
    /// Opens PE metadata and an embedded portable PDB up to
    /// <paramref name="maxEmbeddedPdbBytes"/>, but never probes an adjacent PDB.
    /// </summary>
    public static PdbContext OpenEmbeddedPdbOnly(
        string assemblyPath,
        int maxEmbeddedPdbBytes,
        Action<string>? log = null,
        PdbExpansionBudget? expansionBudget = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maxEmbeddedPdbBytes);
        return Open(
            assemblyPath,
            log,
            PEStreamOptions.Default,
            loadLocalPdb: false,
            loadEmbeddedPdb: true,
            maxEmbeddedPdbBytes: maxEmbeddedPdbBytes,
            expansionBudget: expansionBudget);
    }

    /// <summary>
    /// Opens descriptor-owned PE metadata and an embedded portable PDB, but
    /// never probes an adjacent PDB.
    /// </summary>
    public static PdbContext OpenEmbeddedPdbOnly(
        ResolvedAssemblyReference assembly,
        Action<string>? log = null)
        => OpenEmbeddedPdbOnly(assembly, int.MaxValue, log);

    /// <summary>
    /// Opens descriptor-owned PE metadata and an embedded portable PDB up to
    /// <paramref name="maxEmbeddedPdbBytes"/>, but never probes an adjacent PDB.
    /// </summary>
    public static PdbContext OpenEmbeddedPdbOnly(
        ResolvedAssemblyReference assembly,
        int maxEmbeddedPdbBytes,
        Action<string>? log = null,
        PdbExpansionBudget? expansionBudget = null)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        ArgumentOutOfRangeException.ThrowIfNegative(maxEmbeddedPdbBytes);
        return Open(
            assembly.OpenRead(),
            assembly.Path,
            assembly.Identity.Name,
            log,
            PEStreamOptions.Default,
            assembly.LastWriteTimeUtc,
            loadLocalPdb: false,
            loadEmbeddedPdb: true,
            maxEmbeddedPdbBytes: maxEmbeddedPdbBytes,
            expansionBudget: expansionBudget,
            assemblyRegistration: assembly);
    }

    /// <summary>
    /// Prefetches the complete PE image and loads an embedded PDB up to
    /// <paramref name="maxEmbeddedPdbBytes"/> without probing for an adjacent PDB.
    /// </summary>
    /// <remarks>
    /// Gate:
    /// <c>PdbContext_EmbeddedOnlyPrefetch_RetainsImageWithoutLoadingAdjacentPdb</c>.
    /// </remarks>
    public static PdbContext OpenEmbeddedPdbOnlyPrefetched(
        string assemblyPath,
        int maxEmbeddedPdbBytes,
        Action<string>? log = null,
        PdbExpansionBudget? expansionBudget = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maxEmbeddedPdbBytes);
        return Open(
            assemblyPath,
            log,
            PEStreamOptions.PrefetchEntireImage
                | PEStreamOptions.LeaveOpen,
            loadLocalPdb: false,
            loadEmbeddedPdb: true,
            maxEmbeddedPdbBytes: maxEmbeddedPdbBytes,
            expansionBudget: expansionBudget);
    }

    /// <summary>
    /// Opens descriptor-owned PE metadata without loading an embedded or
    /// adjacent PDB.
    /// </summary>
    public static PdbContext OpenMetadataOnly(
        ResolvedAssemblyReference assembly,
        Action<string>? log = null)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        return Open(
            assembly.OpenRead(),
            assembly.Path,
            assembly.Identity.Name,
            log,
            PEStreamOptions.Default,
            assembly.LastWriteTimeUtc,
            loadLocalPdb: false,
            loadEmbeddedPdb: false,
            assemblyRegistration: assembly);
    }

    /// <summary>
    /// Prefetches descriptor-owned PE content and loads an embedded PDB up to
    /// <paramref name="maxEmbeddedPdbBytes"/> without probing for an adjacent PDB.
    /// </summary>
    /// <remarks>
    /// Gate:
    /// <c>PdbContext_EmbeddedOnlyPrefetch_RetainsImageWithoutLoadingAdjacentPdb</c>.
    /// </remarks>
    public static PdbContext OpenEmbeddedPdbOnlyPrefetched(
        ResolvedAssemblyReference assembly,
        int maxEmbeddedPdbBytes,
        Action<string>? log = null,
        PdbExpansionBudget? expansionBudget = null)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        ArgumentOutOfRangeException.ThrowIfNegative(maxEmbeddedPdbBytes);
        return Open(
            assembly.OpenRead(),
            assembly.Path,
            assembly.Identity.Name,
            log,
            PEStreamOptions.PrefetchEntireImage
                | PEStreamOptions.LeaveOpen,
            assembly.LastWriteTimeUtc,
            loadLocalPdb: false,
            loadEmbeddedPdb: true,
            maxEmbeddedPdbBytes: maxEmbeddedPdbBytes,
            expansionBudget: expansionBudget,
            assemblyRegistration: assembly);
    }

    /// <summary>
    /// Opens an acquisition descriptor through its authoritative stream factory.
    /// The optional path is used only for adjacent PDB discovery.
    /// </summary>
    public static PdbContext Open(
        ResolvedAssemblyReference assembly,
        Action<string>? log = null)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        return Open(
            assembly.OpenRead(),
            assembly.Path,
            assembly.Identity.Name,
            log,
            PEStreamOptions.Default,
            assembly.LastWriteTimeUtc,
            assemblyRegistration: assembly);
    }

    /// <summary>
    /// Runs one synchronous product-layer inspection against this context's
    /// open reader without transferring ownership.
    /// </summary>
    /// <remarks>
    /// The callback must not retain or dispose the reader. The reader remains
    /// owned by this context. Gates:
    /// <c>UnsafeEvidencePresenceQuery_ConsumesBorrowedNonPrefetchedContext</c>
    /// and <c>Metadata_FriendsOnlyTestAssemblies</c>.
    /// </remarks>
    public TResult InspectImage<TResult>(
        Func<PEReader, TResult> inspect)
    {
        ArgumentNullException.ThrowIfNull(inspect);
        EnsureAlive();
        return inspect(_peReader);
    }

    /// <summary>
    /// This context's open reader, lent to
    /// <see cref="AssemblyInspectionSession.Borrow"/> so Metadata facets read
    /// the same bytes without reopening the source.
    /// </summary>
    internal PEReader BorrowedPEReader
    {
        get
        {
            EnsureAlive();
            return _peReader;
        }
    }

    /// <summary>
    /// This context's liveness check, lent to a borrowing session so the borrow fails loudly
    /// instead of reading through a released handle. See
    /// <see cref="AssemblyInspectionSession.Borrow"/>.
    /// </summary>
    internal void EnsureAliveForBorrower() => EnsureAlive();

    /// <summary>
    /// Opens a PE file with its complete image prefetched so downstream body
    /// producers can safely share the reader during parallel analysis.
    /// </summary>
    public static PdbContext OpenPrefetched(
        string assemblyPath,
        Action<string>? log = null)
        => Open(
            assemblyPath,
            log,
            PEStreamOptions.PrefetchEntireImage | PEStreamOptions.LeaveOpen,
            loadLocalPdb: true);

    /// <summary>
    /// Opens an acquisition descriptor with its complete authoritative image prefetched.
    /// </summary>
    public static PdbContext OpenPrefetched(
        ResolvedAssemblyReference assembly,
        Action<string>? log = null)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        return Open(
            assembly.OpenRead(),
            assembly.Path,
            assembly.Identity.Name,
            log,
            PEStreamOptions.PrefetchEntireImage | PEStreamOptions.LeaveOpen,
            assembly.LastWriteTimeUtc,
            loadLocalPdb: true,
            assemblyRegistration: assembly);
    }

    static PdbContext Open(
        string assemblyPath,
        Action<string>? log,
        PEStreamOptions streamOptions,
        bool loadLocalPdb = true,
        bool loadEmbeddedPdb = true,
        int maxEmbeddedPdbBytes = int.MaxValue,
        PdbExpansionBudget? expansionBudget = null)
        => Open(
            File.OpenRead(assemblyPath),
            assemblyPath,
            Path.GetFileName(assemblyPath),
            log,
            streamOptions,
            lastWriteTimeUtc: null,
            loadLocalPdb,
            loadEmbeddedPdb,
            maxEmbeddedPdbBytes,
            expansionBudget);

    static PdbContext Open(
        Stream stream,
        string? assemblyPath,
        string assemblyDisplayName,
        Action<string>? log,
        PEStreamOptions streamOptions,
        DateTime? lastWriteTimeUtc,
        bool loadLocalPdb = true,
        bool loadEmbeddedPdb = true,
        int maxEmbeddedPdbBytes = int.MaxValue,
        PdbExpansionBudget? expansionBudget = null,
        ResolvedAssemblyReference? assemblyRegistration = null)
    {
        PEReader? peReader = null;
        PdbContext? context = null;
        try
        {
            long peImageStart =
                stream.CanSeek
                    ? stream.Position
                    : 0;
            // PdbContext is the sole stream owner.
            peReader = new PEReader(
                stream,
                streamOptions | PEStreamOptions.LeaveOpen);
            assemblyRegistration?.ValidateArtifactContent(peReader);
            context = new PdbContext(
                stream,
                peReader,
                peImageStart,
                assemblyPath,
                assemblyDisplayName,
                log,
                (streamOptions & PEStreamOptions.PrefetchEntireImage) != 0,
                lastWriteTimeUtc);
            if (!peReader.HasMetadata)
                return context;

            context.ReadDebugDirectory(
                loadEmbeddedPdb,
                maxEmbeddedPdbBytes,
                expansionBudget);
            if (loadLocalPdb)
                context.TryLoadLocalPdb();

            return context;
        }
        catch (Exception ex)
        {
            if (context is not null)
            {
                context.Dispose();
            }
            else
            {
                OwnedResourceCleanup.DisposeAfterFailure(
                    peReader,
                    ex);
                OwnedResourceCleanup.DisposeAfterFailure(
                    stream,
                    ex);
            }
            throw;
        }
    }

    /// <summary>
    /// Extracts assembly info from the PE reader.
    /// </summary>
    public AssemblyInfo ExtractAssemblyInfo(bool includeReferences = false)
        => AssemblyInspector.ExtractAssemblyInfo(_peReader, includeReferences);

    /// <summary>Extracts full assembly info from the already-open PE image.</summary>
    public AssemblyInfo ExtractFullAssemblyInfo()
        => AssemblyInspector.ExtractFullAssemblyInfo(_peReader);

    /// <summary>Extracts an API surface from the already-open PE image.</summary>
    public ApiSurface ExtractApiSurface(
        bool includeAll = false,
        bool typesOnly = false)
        => ApiSurfaceExtractor.Extract(_peReader, includeAll, typesOnly);

    /// <summary>
    /// Creates an AssemblyInfo for a native (non-managed) binary.
    /// </summary>
    public AssemblyInfo CreateNativeInfo()
    {
        var info = AssemblyInspector.CreateNativeInfo(_peReader);
        bool isNativeAot = AssemblyInspector.DetectNativeAot(_peReader);
        info.IsNativeAot = isNativeAot;
        info.CompilationType = isNativeAot ? "NativeAOT" : "Native";
        return info;
    }

    /// <summary>
    /// Loads a PDB from a file path (called by CLI after downloading).
    /// </summary>
    public void LoadPdbFromFile(string pdbFilePath, string? pdbLocation = null, string? symbolServer = null)
    {
        try
        {
            LoadPdbFromStream(
                File.OpenRead(pdbFilePath),
                pdbLocation,
                symbolServer,
                pdbFilePath);
        }
        catch (Exception ex)
            when (ex is IOException or UnauthorizedAccessException)
        {
            _log?.Invoke($"Error loading PDB: {ex.Message}");
        }
    }

    /// <summary>
    /// Loads a Portable PDB from caller-supplied content. This method takes
    /// ownership of <paramref name="pdbStream"/> on every outcome.
    /// </summary>
    /// <remarks>
    /// <c>PdbIdentityTests.LoadPdbFromStream_WindowsHeaderDisposesContentBeforeSrm</c>
    /// gates ownership on the early-return path before SRM can dispose the
    /// stream itself. <paramref name="throwOnReadFailure"/> lets an acquisition
    /// boundary keep store read failures visible after the content has already
    /// been accepted; malformed-content failures retain the existing logged
    /// outcome.
    /// </remarks>
    public void LoadPdbFromStream(
        Stream pdbStream,
        string? pdbLocation = null,
        string? symbolServer = null,
        string? portablePdbPath = null,
        bool throwOnReadFailure = false)
    {
        ArgumentNullException.ThrowIfNull(pdbStream);

        MetadataReaderProvider? provider = null;
        bool retained = false;
        bool pdbStreamReleaseAttempted = false;
        Exception? primaryFailure = null;
        try
        {
            try
            {
                if (!pdbStream.CanRead || !pdbStream.CanSeek)
                {
                    throw new IOException(
                        "Portable PDB content must be readable and seekable.");
                }

                // Check for Portable PDB magic header (BSJB)
                byte[] header = new byte[4];
                pdbStream.ReadExactly(header, 0, 4);
                pdbStream.Position = 0;

                if (header[0] != 'B' || header[1] != 'S' || header[2] != 'J' || header[3] != 'B')
                {
                    bool isWindowsPdb = header[0] == 'M' && header[1] == 'i' && header[2] == 'c' && header[3] == 'r';
                    if (isWindowsPdb)
                    {
                        WindowsPdbDetected = true;
                        PdbFormat = "Windows";
                        _log?.Invoke("Windows PDB detected (not supported)");
                    }
                    return;
                }

                provider = MetadataReaderProvider.FromPortablePdbStream(
                    pdbStream,
                    MetadataStreamOptions.PrefetchMetadata
                        | MetadataStreamOptions.LeaveOpen);
                var reader = provider.GetMetadataReader();
                pdbStreamReleaseAttempted = true;
                try
                {
                    pdbStream.Dispose();
                }
                catch (Exception ex)
                {
                    _deferredDisposalFailure ??= ex;
                }
                if (!PdbMatchesAssembly(reader))
                {
                    string suppliedName = portablePdbPath is null
                        ? "supplied content"
                        : Path.GetFileName(portablePdbPath);
                    _log?.Invoke(
                        $"Portable PDB identity mismatch: {suppliedName} does not match {_assemblyDisplayName}");
                    return;
                }

                _disposables.Add(provider);
                _pdbProvider = provider;
                _pdbReader = reader;
                retained = true;

                HasPdb = true;
                PdbVersion++;
                PdbFormat = "Portable";
                PdbLocation = pdbLocation ?? "Standalone";
                PortablePdbPath = portablePdbPath;
                SymbolServer = symbolServer;

                _log?.Invoke($"Loaded PDB: {PdbFormat}, {PdbLocation}");
            }
            catch (Exception ex)
                when ((!throwOnReadFailure && ex is IOException)
                    || ex is BadImageFormatException
                    || ex is InvalidOperationException
                    || ex is ArgumentException)
            {
                _log?.Invoke($"Error loading PDB: {ex.Message}");
            }
        }
        catch (Exception ex)
        {
            primaryFailure = ex;
            throw;
        }
        finally
        {
            if (!retained)
            {
                if (primaryFailure is null)
                {
                    provider?.Dispose();
                    if (!pdbStreamReleaseAttempted)
                        pdbStream.Dispose();
                }
                else
                {
                    OwnedResourceCleanup.DisposeAfterFailure(
                        provider,
                        primaryFailure);
                    if (!pdbStreamReleaseAttempted)
                    {
                        OwnedResourceCleanup.DisposeAfterFailure(
                            pdbStream,
                            primaryFailure);
                    }
                }
            }
        }
    }

    /// <summary>
    /// Whether the MethodDef <paramref name="methodToken"/> addresses carries an IL body:
    /// <see langword="true"/> when it does, <see langword="false"/> when it does not — an
    /// abstract, interface, extern, or runtime-implemented method — and <see langword="null"/>
    /// when the token cannot be read as a MethodDef in this assembly, which is not the same
    /// answer as "no body" and must not be reported as one (issue #3299).
    /// </summary>
    /// <remarks>
    /// A reference assembly's RVA describes a synthesized body rather than the implementation
    /// member's body, so it is not evidence in either direction. Abstract, P/Invoke, non-IL
    /// code-type, internal-call, and forward-reference flags still prove that a method has no IL
    /// body; other reference methods remain unknown.
    /// <c>MethodHasBodyTests.ReferenceAssembly_ReportsOnlyDefiniteBodylessness</c> gates this
    /// distinction.
    /// </remarks>
    public bool? MethodHasBody(int methodToken)
    {
        if (!_peReader.HasMetadata)
            return null;

        try
        {
            var reader = _peReader.GetMetadataReader();
            MethodDefinitionHandle handle = ResolveMethodHandle(
                reader,
                typeName: "",
                methodName: "",
                overloadIndex: 0,
                publicOnly: false,
                metadataToken: methodToken);
            return handle.IsNil ? null : MethodHasBody(reader, handle);
        }
        catch (Exception ex) when (ex is BadImageFormatException or ArgumentException)
        {
            return null;
        }
    }

    /// <summary>
    /// Whether the selected method carries an IL body, resolving by type, name, and overload.
    /// </summary>
    /// <remarks>
    /// Cross-image callers must not treat an overload ordinal as a member identity: declaration
    /// order can differ between reference and runtime images.
    /// <c>CommandExecutionTests.MemberBodyState_CrossImageOverloadOrderMismatch_IsUnknown</c>
    /// gates that caller boundary. <c>MethodHasBodyTests.MethodResolvedByName_ReportsBodyState</c>
    /// gates same-image name resolution.
    /// </remarks>
    public bool? MethodHasBody(
        string typeName,
        string methodName,
        int overloadIndex,
        bool publicOnly = false)
    {
        if (!_peReader.HasMetadata)
            return null;

        try
        {
            var reader = _peReader.GetMetadataReader();
            MethodDefinitionHandle handle = ResolveMethodHandle(
                reader,
                typeName,
                methodName,
                overloadIndex,
                publicOnly,
                metadataToken: 0);
            return handle.IsNil ? null : MethodHasBody(reader, handle);
        }
        catch (Exception ex) when (ex is BadImageFormatException or ArgumentException)
        {
            return null;
        }
    }

    private bool? MethodHasBody(
        MetadataReader reader,
        MethodDefinitionHandle methodHandle)
    {
        MethodDefinition method = reader.GetMethodDefinition(methodHandle);
        if (DefinitelyHasNoIlBody(method))
            return false;
        if (IsReferenceAssembly(reader))
            return null;

        return method.RelativeVirtualAddress != 0;
    }

    private static bool DefinitelyHasNoIlBody(MethodDefinition method)
        => (method.Attributes
                & (MethodAttributes.Abstract | MethodAttributes.PinvokeImpl)) != 0
            || (method.ImplAttributes & MethodImplAttributes.CodeTypeMask)
                != MethodImplAttributes.IL
            || (method.ImplAttributes
                & (MethodImplAttributes.InternalCall
                    | MethodImplAttributes.ForwardRef)) != 0;

    /// <summary>
    /// Whether the assembly carries <c>ReferenceAssemblyAttribute</c>, cached because the answer
    /// is fixed for the image.
    /// </summary>
    private bool IsReferenceAssembly(MetadataReader reader)
    {
        if (_isReferenceAssembly is { } cached)
            return cached;

        bool result = false;
        if (reader.IsAssembly)
        {
            foreach (var handle in reader.GetAssemblyDefinition().GetCustomAttributes())
            {
                if (IsReferenceAssemblyAttribute(reader, reader.GetCustomAttribute(handle)))
                {
                    result = true;
                    break;
                }
            }
        }

        _isReferenceAssembly = result;
        return result;
    }

    /// <summary>
    /// Whether <paramref name="attribute"/> is <c>ReferenceAssemblyAttribute</c>. The constructor
    /// is a MemberReference when the attribute type lives in another assembly and a MethodDef when
    /// it is defined in this one — which is the common case, since the reference assemblies that
    /// most need this test define the attribute themselves.
    /// </summary>
    private static bool IsReferenceAssemblyAttribute(MetadataReader reader, CustomAttribute attribute)
    {
        StringHandle name;
        StringHandle @namespace;

        switch (attribute.Constructor.Kind)
        {
            case HandleKind.MemberReference:
                var parent = reader.GetMemberReference((MemberReferenceHandle)attribute.Constructor).Parent;
                if (parent.Kind != HandleKind.TypeReference)
                    return false;

                var typeRef = reader.GetTypeReference((TypeReferenceHandle)parent);
                name = typeRef.Name;
                @namespace = typeRef.Namespace;
                break;

            case HandleKind.MethodDefinition:
                var method = reader.GetMethodDefinition((MethodDefinitionHandle)attribute.Constructor);
                var typeDef = reader.GetTypeDefinition(method.GetDeclaringType());
                name = typeDef.Name;
                @namespace = typeDef.Namespace;
                break;

            default:
                return false;
        }

        return reader.StringComparer.Equals(name, "ReferenceAssemblyAttribute")
            && reader.StringComparer.Equals(@namespace, "System.Runtime.CompilerServices");
    }

    public ILOffsetMemberContextInfo? ResolveMemberContext(int methodToken, int ilOffset)
    {
        if (!_peReader.HasMetadata)
            return null;

        var handle = MetadataTokens.Handle(methodToken);
        if (handle.Kind != HandleKind.MethodDefinition)
            return null;

        try
        {
            var reader = _peReader.GetMetadataReader();
            var methodHandle = (MethodDefinitionHandle)handle;
            var method = reader.GetMethodDefinition(methodHandle);
            var type = reader.GetTypeDefinition(method.GetDeclaringType());
            var assembly = reader.GetAssemblyDefinition();
            var typeName = reader.GetFullTypeName(type);
            var methodName = reader.GetString(method.Name);
            var signature = FormatMemberSignature(reader, type, method, methodName);
            var async = MethodClassificationScanner.ClassifyAsyncMethod(reader, method) switch
            {
                MethodClassification.RuntimeAsync => "Runtime",
                MethodClassification.StateMachineAsync => "State machine",
                _ => null
            };

            return new ILOffsetMemberContextInfo(
                Assembly: reader.GetString(assembly.Name),
                Type: typeName,
                TypeKind: GetTypeKind(reader, type),
                Member: $"{typeName}.{methodName}",
                Signature: signature,
                MemberKind: GetMemberKind(methodName),
                Visibility: GetVisibility(method.Attributes),
                Static: (method.Attributes & MethodAttributes.Static) != 0,
                Async: async,
                MetadataToken: methodToken,
                ILOffset: ilOffset);
        }
        catch
        {
            return null;
        }
    }

    public IReadOnlyList<ILOffsetExceptionContextInfo> ResolveExceptionContext(int methodToken, int ilOffset, out string? error)
    {
        error = null;
        if (!_peReader.HasMetadata)
            return [];

        var handle = MetadataTokens.Handle(methodToken);
        if (handle.Kind != HandleKind.MethodDefinition)
        {
            error = $"Token 0x{methodToken:X} is not a MethodDef token.";
            return [];
        }

        try
        {
            var reader = _peReader.GetMetadataReader();
            var method = reader.GetMethodDefinition((MethodDefinitionHandle)handle);
            if (method.RelativeVirtualAddress == 0)
            {
                error = $"Method token 0x{methodToken:X} has no IL body.";
                return [];
            }

            var body = _peReader.GetMethodBody(method.RelativeVirtualAddress);
            List<ILOffsetExceptionContextInfo> rows = [];
            var regions = body.ExceptionRegions;
            for (var i = 0; i < regions.Length; i++)
            {
                var region = regions[i];
                var tryEnd = region.TryOffset + region.TryLength;
                var handlerEnd = region.HandlerOffset + region.HandlerLength;
                int? filterStart = region.Kind == ExceptionRegionKind.Filter ? region.FilterOffset : null;
                int? filterEnd = region.Kind == ExceptionRegionKind.Filter ? region.HandlerOffset : null;
                var context = GetExceptionContext(region, ilOffset, tryEnd, handlerEnd, filterStart, filterEnd);
                if (context is null)
                    continue;

                rows.Add(new ILOffsetExceptionContextInfo(
                    Region: i + 1,
                    Context: context,
                    Clause: FormatExceptionClause(region.Kind),
                    TryStart: region.TryOffset,
                    TryEnd: tryEnd,
                    HandlerStart: region.HandlerOffset,
                    HandlerEnd: handlerEnd,
                    FilterStart: filterStart,
                    FilterEnd: filterEnd,
                    CaughtType: ResolveCatchType(reader, region)));
            }

            return rows;
        }
        catch (Exception ex) when (ex is BadImageFormatException or InvalidOperationException or ArgumentOutOfRangeException)
        {
            error = $"Could not resolve exception context for token 0x{methodToken:X}+0x{ilOffset:X}.";
            return [];
        }
    }

    public IReadOnlyList<MethodExceptionRegionInfo> ResolveExceptionRegions(int methodToken, out string? error)
    {
        error = null;
        if (!_peReader.HasMetadata)
            return [];

        var handle = MetadataTokens.Handle(methodToken);
        if (handle.Kind != HandleKind.MethodDefinition)
        {
            error = $"Token 0x{methodToken:X} is not a MethodDef token.";
            return [];
        }

        try
        {
            var reader = _peReader.GetMetadataReader();
            var method = reader.GetMethodDefinition((MethodDefinitionHandle)handle);
            if (method.RelativeVirtualAddress == 0)
            {
                error = $"Method token 0x{methodToken:X} has no IL body.";
                return [];
            }

            var body = _peReader.GetMethodBody(method.RelativeVirtualAddress);
            List<MethodExceptionRegionInfo> rows = [];
            var regions = body.ExceptionRegions;
            for (var i = 0; i < regions.Length; i++)
            {
                var region = regions[i];
                var tryEnd = region.TryOffset + region.TryLength;
                var handlerEnd = region.HandlerOffset + region.HandlerLength;
                int? filterStart = region.Kind == ExceptionRegionKind.Filter ? region.FilterOffset : null;
                int? filterEnd = region.Kind == ExceptionRegionKind.Filter ? region.HandlerOffset : null;
                rows.Add(new MethodExceptionRegionInfo(
                    Region: i + 1,
                    Clause: FormatExceptionClause(region.Kind),
                    TryStart: region.TryOffset,
                    TryEnd: tryEnd,
                    HandlerStart: region.HandlerOffset,
                    HandlerEnd: handlerEnd,
                    FilterStart: filterStart,
                    FilterEnd: filterEnd,
                    CaughtType: ResolveCatchType(reader, region)));
            }

            return rows;
        }
        catch (Exception ex) when (ex is BadImageFormatException or InvalidOperationException or ArgumentOutOfRangeException)
        {
            error = $"Could not resolve exception regions for token 0x{methodToken:X}.";
            return [];
        }
    }

    /// <summary>Resolves a method to its portable-PDB document and visible line range.</summary>
    public PdbMethodDocumentInfo? ResolveMethodDocument(
        string typeName,
        string methodName,
        int overloadIndex,
        bool publicOnly = false,
        int metadataToken = 0)
    {
        if (_pdbReader == null || !_peReader.HasMetadata)
            return null;

        var reader = _peReader.GetMetadataReader();
        MethodDefinitionHandle methodHandle = ResolveMethodHandle(
            reader,
            typeName,
            methodName,
            overloadIndex,
            publicOnly,
            metadataToken);
        return methodHandle.IsNil
            ? null
            : ResolveMethodDocumentRange(methodHandle);
    }

    private static MethodDefinitionHandle ResolveMethodHandle(
        MetadataReader reader,
        string typeName,
        string methodName,
        int overloadIndex,
        bool publicOnly,
        int metadataToken)
    {
        if (metadataToken != 0)
        {
            // Handle() rejects invalid tokens by throwing. Callers that accept untrusted token
            // values must guard this decode.
            var tokenHandle = MetadataTokens.Handle(metadataToken);
            return tokenHandle.Kind == HandleKind.MethodDefinition
                ? (MethodDefinitionHandle)tokenHandle
                : default;
        }

        foreach (var typeDefHandle in reader.TypeDefinitions)
        {
            var typeDef = reader.GetTypeDefinition(typeDefHandle);
            if (reader.GetFullTypeName(typeDef) != typeName)
                continue;

            int matchCount = 0;
            foreach (var methodHandle in typeDef.GetMethods())
            {
                var method = reader.GetMethodDefinition(methodHandle);
                if (reader.GetString(method.Name) != methodName)
                    continue;
                if (publicOnly
                    && (method.Attributes & MethodAttributes.MemberAccessMask) != MethodAttributes.Public)
                {
                    continue;
                }
                if (matchCount++ == overloadIndex)
                    return methodHandle;
            }
        }

        return default;
    }

    PdbMethodDocumentInfo? ResolveMethodDocumentRange(MethodDefinitionHandle methodHandle)
    {
        try
        {
            var ranges = ReadVisibleSequencePointDocuments(methodHandle);
            var primary = ranges.FirstOrDefault(static range => range.IsPrimaryDocument);
            if (primary is null)
                return null;

            var document = _pdbReader!.GetDocument(primary.Document);
            return new PdbMethodDocumentInfo(
                _pdbReader.GetString(document.Name),
                primary.StartLine,
                primary.EndLine,
                document.Hash.IsNil ? null : _pdbReader.GetBlobBytes(document.Hash),
                document.Hash.IsNil
                    ? null
                    : MapHashAlgorithm(_pdbReader.GetGuid(document.HashAlgorithm)))
            {
                SequencePointStartLines = primary.StartLines,
            };
        }
        catch (Exception ex) when (ex is BadImageFormatException
            or InvalidOperationException
            or ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    private static string FormatMemberSignature(
        MetadataReader reader,
        TypeDefinition type,
        MethodDefinition method,
        string methodName)
    {
        try
        {
            var context = GenericContext.ForMethod(reader, type, method);
            var signature = GuardedSignatureText.MethodText(reader, method, context)
                .GetValueOrThrow();
            return SignatureRenderer.RenderDecodedSignature(
                reader,
                method,
                methodName,
                signature,
                context);
        }
        catch
        {
            return methodName + "(...)";
        }
    }

    private static string GetMemberKind(string methodName)
        => methodName switch
        {
            ".ctor" => "constructor",
            ".cctor" => "static constructor",
            _ => "method"
        };

    private static string GetVisibility(MethodAttributes attributes)
        => (attributes & MethodAttributes.MemberAccessMask) switch
        {
            MethodAttributes.Public => "public",
            MethodAttributes.Private => "private",
            MethodAttributes.Family => "protected",
            MethodAttributes.Assembly => "internal",
            MethodAttributes.FamORAssem => "protected internal",
            MethodAttributes.FamANDAssem => "private protected",
            _ => "private"
        };

    private static string GetTypeKind(MetadataReader reader, TypeDefinition type)
    {
        if ((type.Attributes & TypeAttributes.Interface) != 0)
            return "interface";

        var baseName = GetBaseTypeName(reader, type);
        if (baseName == "System.Enum")
            return "enum";
        if (baseName == "System.ValueType")
            return "struct";
        if (baseName == "System.MulticastDelegate")
            return "delegate";

        return "class";
    }

    private static string? GetBaseTypeName(MetadataReader reader, TypeDefinition type)
        => type.BaseType.Kind switch
        {
            HandleKind.TypeReference => reader.GetFullTypeName(reader.GetTypeReference((TypeReferenceHandle)type.BaseType)),
            HandleKind.TypeDefinition => reader.GetFullTypeName(reader.GetTypeDefinition((TypeDefinitionHandle)type.BaseType)),
            _ => null
        };

    private static string? GetExceptionContext(
        ExceptionRegion region,
        int offset,
        int tryEnd,
        int handlerEnd,
        int? filterStart,
        int? filterEnd)
    {
        if (filterStart is { } fs && filterEnd is { } fe && offset >= fs && offset < fe)
            return "filter";
        if (offset >= region.HandlerOffset && offset < handlerEnd)
            return region.Kind switch
            {
                ExceptionRegionKind.Catch => "catch handler",
                ExceptionRegionKind.Filter => "filter handler",
                ExceptionRegionKind.Finally => "finally handler",
                ExceptionRegionKind.Fault => "fault handler",
                _ => "handler"
            };
        if (offset >= region.TryOffset && offset < tryEnd)
            return "try";
        return null;
    }

    private static string FormatExceptionClause(ExceptionRegionKind kind)
        => kind switch
        {
            ExceptionRegionKind.Catch => "catch",
            ExceptionRegionKind.Filter => "filter",
            ExceptionRegionKind.Finally => "finally",
            ExceptionRegionKind.Fault => "fault",
            _ => kind.ToString()
        };

    private static string? ResolveCatchType(MetadataReader reader, ExceptionRegion region)
        => region.Kind == ExceptionRegionKind.Catch && !region.CatchType.IsNil
            ? TypeResolver.GetTypeName(reader, region.CatchType)
            : null;

    /// <summary>Enumerates all named documents in the portable PDB.</summary>
    public IEnumerable<PdbDocumentInfo> EnumeratePdbDocuments()
    {
        if (_pdbReader == null)
            yield break;

        foreach (var docHandle in _pdbReader.Documents)
        {
            var document = _pdbReader.GetDocument(docHandle);
            string filePath = _pdbReader.GetString(document.Name);

            byte[]? checksum = null;
            string? checksumAlgorithm = null;
            if (!document.Hash.IsNil)
            {
                checksum = _pdbReader.GetBlobBytes(document.Hash);
                checksumAlgorithm = MapHashAlgorithm(_pdbReader.GetGuid(document.HashAlgorithm));
            }

            yield return new PdbDocumentInfo(
                filePath,
                checksum,
                checksumAlgorithm,
                MetadataTokens.GetRowNumber(docHandle));
        }
    }

    /// <summary>
    /// Enumerates portable-PDB document paths without reading their checksum blobs.
    /// </summary>
    public IEnumerable<string> EnumeratePdbDocumentPaths()
    {
        if (_pdbReader == null)
            yield break;

        foreach (var docHandle in _pdbReader.Documents)
            yield return _pdbReader.GetString(_pdbReader.GetDocument(docHandle).Name);
    }

    /// <summary>
    /// Enumerates method-to-document mappings from visible portable-PDB sequence points.
    /// A method may produce multiple rows when sequence points span multiple documents.
    /// </summary>
    public IEnumerable<PdbMemberDocumentInfo> EnumerateMemberDocuments(
        IReadOnlySet<int>? metadataTokens = null)
    {
        if (_pdbReader == null || !_peReader.HasMetadata)
            yield break;

        var metadata = _peReader.GetMetadataReader();
        foreach (var methodHandle in EnumerateSelectedMethods(metadata, metadataTokens))
        {
            int metadataToken = MetadataTokens.GetToken(methodHandle);
            var ranges = ReadVisibleSequencePointDocuments(methodHandle);

            if (ranges.Count == 0)
                continue;

            var method = metadata.GetMethodDefinition(methodHandle);
            var anchor = ApiMemberIdentity.CreateMethodAnchor(
                metadata,
                method.GetDeclaringType(),
                method);
            bool isFinalizer = ApiSurfaceExtractor.IsFinalizerMethod(metadata, methodHandle);

            foreach (var range in ranges)
            {
                var documentHandle = range.Document;
                var document = _pdbReader.GetDocument(documentHandle);
                string filePath = _pdbReader.GetString(document.Name);
                yield return new PdbMemberDocumentInfo(
                    anchor,
                    metadataToken,
                    MetadataTokens.GetRowNumber(documentHandle),
                    filePath,
                    range.StartLine,
                    range.EndLine,
                    IsPrimaryDocument: range.IsPrimaryDocument,
                    IsFinalizer: isFinalizer)
                {
                    SequencePointStartLines = range.StartLines,
                };
            }
        }
    }

    private IReadOnlyList<SequencePointDocumentRange> ReadVisibleSequencePointDocuments(
        MethodDefinitionHandle methodHandle)
    {
        var debugInfo = _pdbReader!.GetMethodDebugInformation(
            methodHandle.ToDebugInformationHandle());
        var currentDocument = debugInfo.Document;
        var firstVisibleDocument = default(DocumentHandle);
        Dictionary<DocumentHandle, MutableSequencePointDocumentRange> byDocument = [];

        foreach (var point in debugInfo.GetSequencePoints())
        {
            if (!point.Document.IsNil)
                currentDocument = point.Document;
            if (point.IsHidden || currentDocument.IsNil)
                continue;

            if (firstVisibleDocument.IsNil)
                firstVisibleDocument = currentDocument;
            if (!byDocument.TryGetValue(currentDocument, out var range))
            {
                range = new MutableSequencePointDocumentRange();
                byDocument.Add(currentDocument, range);
            }

            range.StartLine = Math.Min(range.StartLine, point.StartLine);
            range.EndLine = Math.Max(range.EndLine, point.EndLine);
            range.StartLines.Add(point.StartLine);
        }

        if (byDocument.Count == 0)
            return [];

        var primaryDocument = !debugInfo.Document.IsNil
            && byDocument.ContainsKey(debugInfo.Document)
                ? debugInfo.Document
                : firstVisibleDocument;

        return
        [
            .. byDocument
                .OrderBy(static item => MetadataTokens.GetRowNumber(item.Key))
                .Select(item => new SequencePointDocumentRange(
                    item.Key,
                    item.Value.StartLine,
                    item.Value.EndLine,
                    [.. item.Value.StartLines.Distinct().Order()],
                    item.Key == primaryDocument)),
        ];
    }

    private sealed class MutableSequencePointDocumentRange
    {
        public int StartLine = int.MaxValue;
        public int EndLine;
        public List<int> StartLines { get; } = [];
    }

    private sealed record SequencePointDocumentRange(
        DocumentHandle Document,
        int StartLine,
        int EndLine,
        ImmutableArray<int> StartLines,
        bool IsPrimaryDocument);

    /// <summary>
    /// Enumerates type-to-document relationships recovered from method debug information.
    /// </summary>
    public IEnumerable<PdbTypeDocumentInfo> EnumerateTypeDocuments()
    {
        if (_pdbReader == null || !_peReader.HasMetadata)
            yield break;

        var metadata = _peReader.GetMetadataReader();
        foreach (var typeHandle in metadata.TypeDefinitions)
        {
            var type = metadata.GetTypeDefinition(typeHandle);
            string fullName = metadata.GetFullTypeName(type);
            if (fullName == "<Module>")
                continue;
            MetadataTypeDefinitionName definitionName =
                MetadataTypeDefinitionNameReader.Read(
                    metadata,
                    typeHandle)
                switch
                {
                    MetadataTypeDefinitionNameReadResult.Read read =>
                        read.Name,
                    MetadataTypeDefinitionNameReadResult.Rejected rejected =>
                        throw new BadImageFormatException(
                            rejected.Failure.Detail),
                    _ => throw new InvalidOperationException(
                        "Unknown metadata type-definition name result."),
                };

            List<PdbDocumentReference> documents = [];
            HashSet<int> seenDocumentRows = [];
            foreach (var methodHandle in type.GetMethods())
            {
                if (metadata.GetMethodDefinition(methodHandle).RelativeVirtualAddress == 0)
                    continue;

                var ranges =
                    ReadVisibleSequencePointDocuments(
                        methodHandle);
                if (ranges.Count > 0)
                {
                    foreach (var range in ranges)
                        AddDocument(range.Document);
                    continue;
                }

                var debugInfo =
                    _pdbReader.GetMethodDebugInformation(
                        methodHandle.ToDebugInformationHandle());
                if (!debugInfo.Document.IsNil)
                    AddDocument(debugInfo.Document);

                void AddDocument(DocumentHandle handle)
                {
                    var document =
                        _pdbReader.GetDocument(handle);
                    string path =
                        _pdbReader.GetString(document.Name);
                    if (string.IsNullOrEmpty(path))
                    {
                        throw new BadImageFormatException(
                            "A portable-PDB source document has an empty path.");
                    }
                    int documentRowId =
                        MetadataTokens.GetRowNumber(handle);
                    if (seenDocumentRows.Add(documentRowId))
                    {
                        documents.Add(
                            new PdbDocumentReference(
                                documentRowId,
                                path));
                    }
                }
            }

            yield return new PdbTypeDocumentInfo(
                fullName,
                metadata.GetString(type.Name),
                documents)
            {
                DefinitionName = definitionName,
            };
        }
    }

    /// <summary>
    /// Reads the unique module custom-debug-information value having
    /// <paramref name="kind"/>.
    /// </summary>
    public PdbCustomDebugInformationResult ReadModuleCustomDebugInformation(
        Guid kind,
        int maxValueBytes = int.MaxValue)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maxValueBytes);
        return ReadCustomDebugInformation(
            EntityHandle.ModuleDefinition,
            kind,
            maxValueBytes);
    }

    /// <summary>
    /// Reads one module custom-debug-information value directly from a standalone portable PDB.
    /// This path deliberately does not claim assembly identity: consumers use it to inspect
    /// package-local authored PDB content, not to map methods or source documents to an assembly.
    /// </summary>
    public static PdbCustomDebugInformationResult ReadPortablePdbModuleCustomDebugInformation(
        string pdbPath,
        Guid kind,
        int maxValueBytes = int.MaxValue)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pdbPath);
        ArgumentOutOfRangeException.ThrowIfNegative(maxValueBytes);

        using FileStream stream = File.OpenRead(pdbPath);
        using MetadataReaderProvider provider = MetadataReaderProvider.FromPortablePdbStream(
            stream,
            MetadataStreamOptions.PrefetchMetadata);
        MetadataReader reader = provider.GetMetadataReader();
        return ReadCustomDebugInformation(
            reader,
            EntityHandle.ModuleDefinition,
            kind,
            maxValueBytes);
    }

    /// <summary>
    /// Reads the unique document custom-debug-information value having
    /// <paramref name="kind"/>.
    /// </summary>
    public PdbCustomDebugInformationResult ReadDocumentCustomDebugInformation(
        int documentRowId,
        Guid kind)
    {
        if (_pdbReader == null
            || documentRowId <= 0
            || documentRowId > _pdbReader.GetTableRowCount(TableIndex.Document))
        {
            return new(PdbCustomDebugInformationStatus.Absent, null);
        }

        return ReadCustomDebugInformation(
            MetadataTokens.DocumentHandle(documentRowId),
            kind);
    }

    /// <summary>
    /// Whether a document carries custom debug information having <paramref name="kind"/>.
    /// </summary>
    public bool HasDocumentCustomDebugInformation(int documentRowId, Guid kind)
    {
        if (_pdbReader == null
            || documentRowId <= 0
            || documentRowId > _pdbReader.GetTableRowCount(TableIndex.Document))
        {
            return false;
        }

        foreach (var handle in _pdbReader.GetCustomDebugInformation(
            MetadataTokens.DocumentHandle(documentRowId)))
        {
            var info = _pdbReader.GetCustomDebugInformation(handle);
            if (_pdbReader.GetGuid(info.Kind) == kind)
                return true;
        }

        return false;
    }

    PdbCustomDebugInformationResult ReadCustomDebugInformation(
        EntityHandle parent,
        Guid kind,
        int maxValueBytes = int.MaxValue)
    {
        if (_pdbReader == null)
            return new(PdbCustomDebugInformationStatus.Absent, null);

        return ReadCustomDebugInformation(
            _pdbReader,
            parent,
            kind,
            maxValueBytes);
    }

    static PdbCustomDebugInformationResult ReadCustomDebugInformation(
        MetadataReader pdbReader,
        EntityHandle parent,
        Guid kind,
        int maxValueBytes = int.MaxValue)
    {
        BlobHandle value = default;
        bool found = false;
        Exception? scanError = null;
        try
        {
            foreach (var handle in pdbReader.GetCustomDebugInformation(parent))
            {
                CustomDebugInformation info;
                try
                {
                    info = pdbReader.GetCustomDebugInformation(handle);
                    if (pdbReader.GetGuid(info.Kind) != kind)
                        continue;
                }
                catch (Exception ex) when (IsCustomDebugInformationReadFailure(ex))
                {
                    scanError ??= ex;
                    continue;
                }

                if (found)
                    return new(PdbCustomDebugInformationStatus.Duplicate, null);

                found = true;
                value = info.Value;
            }
        }
        catch (Exception ex) when (IsCustomDebugInformationReadFailure(ex))
        {
            scanError ??= ex;
        }

        if (scanError is not null)
        {
            if (found)
            {
                return new(
                    PdbCustomDebugInformationStatus.Present,
                    null,
                    scanError.Message);
            }

            ExceptionDispatchInfo.Capture(scanError).Throw();
        }

        if (!found)
            return new(PdbCustomDebugInformationStatus.Absent, null);

        try
        {
            int valueLength = pdbReader.GetBlobReader(value).Length;
            if (valueLength > maxValueBytes)
            {
                return new(
                    PdbCustomDebugInformationStatus.Present,
                    null,
                    "The custom debug information exceeded the caller's byte limit.",
                    valueLength,
                    LimitExceeded: true);
            }

            return new(
                PdbCustomDebugInformationStatus.Present,
                pdbReader.GetBlobBytes(value),
                ValueLength: valueLength);
        }
        catch (Exception ex) when (IsCustomDebugInformationReadFailure(ex))
        {
            return new(
                PdbCustomDebugInformationStatus.Present,
                null,
                ex.Message);
        }
    }

    static bool IsCustomDebugInformationReadFailure(Exception exception)
        => exception is BadImageFormatException
            or InvalidOperationException
            or ArgumentOutOfRangeException;

    private static IEnumerable<MethodDefinitionHandle> EnumerateSelectedMethods(
        MetadataReader metadata,
        IReadOnlySet<int>? metadataTokens)
    {
        if (metadataTokens is null)
        {
            foreach (var methodHandle in metadata.MethodDefinitions)
                yield return methodHandle;
            yield break;
        }

        int methodCount = metadata.GetTableRowCount(TableIndex.MethodDef);
        foreach (int token in metadataTokens.Order())
        {
            const int methodDefinitionToken = 0x06000000;
            const int tokenTypeMask = unchecked((int)0xFF000000);
            const int rowMask = 0x00FFFFFF;
            int row = token & rowMask;
            if ((token & tokenTypeMask) != methodDefinitionToken || row == 0 || row > methodCount)
                continue;

            yield return MetadataTokens.MethodDefinitionHandle(row);
        }
    }

    /// <summary>
    /// Reads compiler options recorded in portable-PDB module custom debug information.
    /// </summary>
    public IReadOnlyList<CompilationOptionInfo> GetCompilationOptions()
        => _pdbReader is null
            ? []
            : PdbCompilationInfoReader.ReadOptions(_pdbReader);

    /// <summary>
    /// Reads compiler references recorded in portable-PDB module custom debug information.
    /// </summary>
    public IReadOnlyList<CompilationReferenceInfo> GetCompilationReferences()
        => _pdbReader is null
            ? []
            : PdbCompilationInfoReader.ReadReferences(_pdbReader);

    // Well-known source document hash algorithm GUIDs (System.Reflection.Metadata).
    private static readonly Guid s_hashSha1 = new("ff1816ec-aa5e-4d10-87f7-6f4963833460");
    private static readonly Guid s_hashSha256 = new("8829d00f-11b8-4213-878b-770e8597ac16");

    internal static string? MapHashAlgorithm(Guid algorithm)
    {
        if (algorithm == s_hashSha256) return "SHA256";
        if (algorithm == s_hashSha1) return "SHA1";
        return null;
    }

    /// <summary>
    /// Resolves a method token and IL offset through portable-PDB sequence points.
    /// </summary>
    public PdbILOffsetLocation? ResolvePdbLocation(int methodToken, int ilOffset)
    {
        if (_pdbReader == null || !_peReader.HasMetadata)
            return null;

        try
        {
            var handle = MetadataTokens.Handle(methodToken);
            if (handle.Kind != HandleKind.MethodDefinition)
                return null;

            var metadata = _peReader.GetMetadataReader();
            var methodHandle = (MethodDefinitionHandle)handle;
            var method = metadata.GetMethodDefinition(methodHandle);
            var type = metadata.GetTypeDefinition(method.GetDeclaringType());
            string methodName =
                $"{metadata.GetFullTypeName(type)}.{metadata.GetString(method.Name)}";

            var debugInfo = _pdbReader.GetMethodDebugInformation(
                methodHandle.ToDebugInformationHandle());
            if (debugInfo.SequencePointsBlob.IsNil)
                return null;

            SequencePoint? bestPoint = null;
            foreach (var point in debugInfo.GetSequencePoints())
            {
                if (point.Offset > ilOffset)
                    break;
                if (!point.IsHidden)
                    bestPoint = point;
            }
            if (bestPoint is not { } matched)
                return null;

            var document = _pdbReader.GetDocument(matched.Document);
            return new PdbILOffsetLocation(
                methodName,
                _pdbReader.GetString(document.Name),
                matched.StartLine,
                matched.Offset,
                MetadataTokens.GetRowNumber(matched.Document));
        }
        catch (Exception ex) when (ex is BadImageFormatException
            or ArgumentException
            or InvalidOperationException
            or ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    /// <summary>
    /// Cheap metadata-backed presence flags for section discovery, using the
    /// already-open PEReader. IL-backed presence is outside this scan.
    /// </summary>
    public PresenceFlags ScanPresenceFlags()
        => AssemblyDetailScanner.ScanPresenceFlags(_peReader);

    public PresenceFlags ScanPresenceFlags(
        EcosystemIntegrationPresence integrationPresence)
        => AssemblyDetailScanner.ScanPresenceFlags(
            _peReader,
            integrationPresence);

    public PresenceFlags ScanPresenceFlagsWithoutIntegrations()
        => AssemblyDetailScanner.ScanPresenceFlagsWithoutIntegrations(
            _peReader);

    public void Dispose() =>
        _ = DisposeWithFailure();

    /// <summary>
    /// Disposes every owned resource and returns the first disposal failure.
    /// </summary>
    /// <remarks>
    /// The compatibility <see cref="Dispose()"/> path suppresses the returned
    /// failure. Strict ownership boundaries inspect it after all cleanup has
    /// been attempted.
    /// </remarks>
    public Exception? DisposeWithFailure()
    {
        if (_disposed)
            return null;
        _disposed = true;
        Exception? failure = _deferredDisposalFailure;
        _deferredDisposalFailure = null;
        foreach (IDisposable disposable in _disposables)
            DisposeOwned(disposable);
        _disposables.Clear();
        _pdbProvider = null;
        _pdbReader = null;
        DisposeOwned(_peReader);
        DisposeOwned(_peStream);
        return failure;

        void DisposeOwned(IDisposable disposable)
        {
            try
            {
                disposable.Dispose();
            }
            catch (Exception ex)
            {
                failure ??= ex;
            }
        }
    }

    void EnsureAlive()
        => ObjectDisposedException.ThrowIf(_disposed, this);

    // --- Private implementation ---

    private void ReadDebugDirectory(
        bool loadEmbeddedPdb,
        int maxEmbeddedPdbBytes,
        PdbExpansionBudget? expansionBudget)
    {
        uint debugDirectorySize = unchecked((uint)(
            _peReader.PEHeaders.PEHeader?.DebugTableDirectory.Size ?? 0));
        uint maxDebugDirectoryBytes =
            DebugDirectoryEntrySize * MaxDebugDirectoryEntries;
        if (debugDirectorySize > maxDebugDirectoryBytes)
        {
            throw new PdbResourceLimitException(
                $"The PE debug directory's {debugDirectorySize} bytes exceed "
                + $"the {MaxDebugDirectoryEntries}-entry limit.",
                debugDirectorySize,
                maxDebugDirectoryBytes);
        }

        CodeViewDebugDirectoryData? portableCodeView = null;
        CodeViewDebugDirectoryData? windowsCodeView = null;
        bool embeddedPdbLoaded = false;

        foreach (var entry in _peReader.ReadDebugDirectory())
        {
            if (entry.Type == DebugDirectoryEntryType.Reproducible)
            {
                HasReproducibleFlag = true;
            }

            if (entry.Type == DebugDirectoryEntryType.CodeView)
            {
                uint codeViewDataSize = unchecked((uint)entry.DataSize);
                if (codeViewDataSize > MaxCodeViewDataBytes)
                {
                    throw new PdbResourceLimitException(
                        $"A CodeView debug record's {codeViewDataSize} bytes exceed "
                        + $"the {MaxCodeViewDataBytes}-byte limit.",
                        codeViewDataSize,
                        MaxCodeViewDataBytes);
                }

                var cvData = _peReader.ReadCodeViewDebugDirectoryData(entry);
                bool isPortable = entry.MinorVersion == 0x504d;

                CodeViewPdbPath = cvData.Path;

                if (!cvData.Path.StartsWith("/_/", StringComparison.Ordinal) &&
                    Path.GetDirectoryName(cvData.Path) is string dir && !string.IsNullOrEmpty(dir))
                {
                    HasNormalizedPaths = false;
                    NonNormalizedPaths ??= [];
                    NonNormalizedPaths.Add($"PDB Path: {cvData.Path}");
                }
                else
                {
                    HasNormalizedPaths = true;
                }

                if (isPortable)
                {
                    portableCodeView = cvData;
                    PdbId = new CodeViewInfo(
                        cvData.Guid,
                        cvData.Age,
                        Path.GetFileName(cvData.Path),
                        true,
                        entry.Stamp);
                }
                else
                {
                    windowsCodeView = cvData;
                    if (portableCodeView == null)
                    {
                        // Only use Windows PDB as fallback
                        PdbId = new CodeViewInfo(
                            cvData.Guid,
                            cvData.Age,
                            Path.GetFileName(cvData.Path),
                            false,
                            entry.Stamp);
                    }
                }
            }

            if (entry.Type == DebugDirectoryEntryType.EmbeddedPortablePdb)
            {
                HasEmbeddedPdb = true;
                if (!loadEmbeddedPdb)
                    continue;
                if (embeddedPdbLoaded)
                {
                    throw new BadImageFormatException(
                        "The PE image carries multiple embedded portable PDB entries.");
                }
                embeddedPdbLoaded = true;

                int embeddedPdbBytes = ReadEmbeddedPortablePdbDeclaredSize(entry);
                if (embeddedPdbBytes > maxEmbeddedPdbBytes)
                {
                    throw new PdbResourceLimitException(
                        $"The embedded portable PDB's declared {embeddedPdbBytes} decompressed bytes "
                        + $"exceed the caller's {maxEmbeddedPdbBytes}-byte limit.",
                        embeddedPdbBytes,
                        maxEmbeddedPdbBytes);
                }
                expansionBudget?.Reserve(embeddedPdbBytes);
                EmbeddedPdbSize = embeddedPdbBytes;

                PdbFormat = "Portable";
                PdbLocation = "Embedded";

                var provider = _peReader.ReadEmbeddedPortablePdbDebugDirectoryData(entry);
                _disposables.Add(provider);
                _pdbProvider = provider;
                _pdbReader = provider.GetMetadataReader();
                HasPdb = true;
                PdbVersion++;

                _log?.Invoke("Using embedded PDB");
            }
        }

        if (windowsCodeView != null && portableCodeView != null)
        {
            _log?.Invoke("Found both Windows (.ni.pdb) and Portable PDB entries, using Portable");
        }
    }

    private int ReadEmbeddedPortablePdbDeclaredSize(DebugDirectoryEntry entry)
    {
        const uint EmbeddedPortablePdbSignature = 0x4244504D;
        const int HeaderSize = sizeof(uint) + sizeof(int);

        if (entry.DataSize < HeaderSize)
            throw new BadImageFormatException("The embedded portable PDB header is truncated.");

        Span<byte> header = stackalloc byte[HeaderSize];
        if (_peReader.IsLoadedImage)
        {
            PEMemoryBlock block =
                _peReader.GetSectionData(entry.DataRelativeVirtualAddress);
            if (block.Length < entry.DataSize)
                throw new BadImageFormatException("The embedded portable PDB data is truncated.");
            block.GetContent(0, HeaderSize).CopyTo(header);
        }
        else
        {
            long dataStart;
            try
            {
                dataStart = checked(_peImageStart + entry.DataPointer);
            }
            catch (OverflowException ex)
            {
                throw new BadImageFormatException(
                    "The embedded portable PDB file pointer is invalid.",
                    ex);
            }

            if (!_peStream.CanSeek
                || entry.DataPointer < 0
                || dataStart < _peImageStart
                || dataStart > _peStream.Length - entry.DataSize)
            {
                throw new BadImageFormatException(
                    "The embedded portable PDB file pointer is invalid.");
            }

            long originalPosition = _peStream.Position;
            try
            {
                _peStream.Position = dataStart;
                _peStream.ReadExactly(header);
            }
            catch (EndOfStreamException ex)
            {
                throw new BadImageFormatException(
                    "The embedded portable PDB data is truncated.",
                    ex);
            }
            finally
            {
                _peStream.Position = originalPosition;
            }
        }

        if (BinaryPrimitives.ReadUInt32LittleEndian(header)
            != EmbeddedPortablePdbSignature)
        {
            throw new BadImageFormatException("The embedded portable PDB signature is invalid.");
        }

        int decompressedSize =
            BinaryPrimitives.ReadInt32LittleEndian(header[sizeof(uint)..]);
        if (decompressedSize < 0)
            throw new BadImageFormatException("The embedded portable PDB size is invalid.");

        return decompressedSize;
    }

    private void TryLoadLocalPdb()
    {
        if (HasPdb)
            return;
        if (_assemblyPath is null)
            return;

        var pdbPath = Path.ChangeExtension(_assemblyPath, ".pdb");
        if (!File.Exists(pdbPath))
            return;

        // Check header
        try
        {
            using var checkStream = File.OpenRead(pdbPath);
            byte[] header = new byte[4];
            checkStream.ReadExactly(header, 0, 4);

            if (header[0] == 'M' && header[1] == 'i' && header[2] == 'c' && header[3] == 'r')
            {
                WindowsPdbDetected = true;
                PdbFormat = "Windows";
                PdbLocation = "Standalone";
                _log?.Invoke("Standalone Windows PDB found (not supported)");
                return;
            }

            if (header[0] != 'B' || header[1] != 'S' || header[2] != 'J' || header[3] != 'B')
                return;
        }
        catch
        {
            return;
        }

        // It's a Portable PDB — open it
        LoadPdbFromFile(pdbPath, "Standalone");
    }

    private bool PdbMatchesAssembly(MetadataReader pdbReader)
        => PortablePdbIdentityMatches(
            PdbId,
            pdbReader.DebugMetadataHeader?.Id,
            _log);

    internal static bool PortablePdbIdentityMatches(
        CodeViewInfo? expected,
        ImmutableArray<byte>? pdbContentId,
        Action<string>? log)
    {
        if (expected is null)
            return true;
        if (!expected.IsPortable)
        {
            log?.Invoke(
                "Portable PDB identity cannot be verified because the assembly has no Portable CodeView entry");
            return false;
        }

        if (pdbContentId is not { Length: >= 20 } id)
        {
            log?.Invoke("PDB identity missing or too short to verify");
            return false;
        }

        Span<byte> guidBytes = stackalloc byte[16];
        id.AsSpan(0, 16).CopyTo(guidBytes);
        var actual = new Guid(guidBytes);
        uint actualStamp =
            BinaryPrimitives.ReadUInt32LittleEndian(
                id.AsSpan(16, 4));
        if (actual == expected.Guid
            && actualStamp == expected.Stamp)
        {
            return true;
        }

        log?.Invoke(
            "PDB identity mismatch: assembly expects "
            + $"{expected.Guid:D}/{expected.Stamp:x8}; PDB has "
            + $"{actual:D}/{actualStamp:x8}");
        return false;
    }
}
