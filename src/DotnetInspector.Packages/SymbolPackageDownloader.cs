using DotnetInspector.Core;

namespace DotnetInspector.Packages;

/// <summary>
/// Result of downloading a PDB file (no SRM types).
/// </summary>
public record PdbDownloadResult(
    string? PdbFilePath,
    bool WindowsPdbDetected = false,
    string? SymbolServer = null,
    PortablePdbStoreFailureKind? StoreFailure = null
);

/// <summary>Why the PDB store could not provide verified content.</summary>
public enum PortablePdbStoreFailureKind
{
    /// <summary>A cached entry was malformed or did not match the requested identity.</summary>
    InvalidCachedContent,

    /// <summary>The store did not retain content after accepting a write.</summary>
    PublicationNotRetained,
}

/// <summary>
/// Repeatable access to one acquired Portable PDB payload.
/// </summary>
/// <remarks>
/// The backing store owns persistence. <see cref="OpenReadAsync"/> returns a
/// fresh readable, seekable stream positioned at zero; the caller owns that
/// stream. <see cref="LocalPath"/> is available only when the store is
/// filesystem-backed.
/// </remarks>
public sealed class AcquiredPortablePdb
{
    private readonly IPdbStore _store;
    private readonly string _storeKey;

    internal AcquiredPortablePdb(
        IPdbStore store,
        string storeKey,
        string symbolServer,
        bool fromCache)
    {
        _store = store;
        _storeKey = storeKey;
        SymbolServer = symbolServer;
        FromCache = fromCache;
    }

    public string SymbolServer { get; }
    public bool FromCache { get; }
    public string? LocalPath => _store.TryGetLocalPath(_storeKey);

    public async ValueTask<Stream> OpenReadAsync(
        CancellationToken cancellationToken = default)
    {
        Stream? stream =
            await _store.TryOpenAsync(
                _storeKey,
                cancellationToken).ConfigureAwait(false);
        if (stream is null)
        {
            throw new IOException(
                "The acquired Portable PDB content is no longer available.");
        }

        if (!stream.CanRead || !stream.CanSeek)
        {
            await stream.DisposeAsync().ConfigureAwait(false);
            throw new IOException(
                "The acquired Portable PDB store returned an unreadable or non-seekable stream.");
        }

        stream.Position = 0;
        return stream;
    }
}

/// <summary>The typed result of content-shaped Portable PDB acquisition.</summary>
public abstract record PortablePdbAcquisitionResult
{
    private protected PortablePdbAcquisitionResult(
        bool windowsPdbDetected,
        PortablePdbStoreFailureKind? storeFailure)
    {
        WindowsPdbDetected = windowsPdbDetected;
        StoreFailure = storeFailure;
    }

    public bool WindowsPdbDetected { get; }
    public PortablePdbStoreFailureKind? StoreFailure { get; }

    public sealed record Acquired : PortablePdbAcquisitionResult
    {
        internal Acquired(
            AcquiredPortablePdb pdb,
            bool windowsPdbDetected)
            : base(windowsPdbDetected, storeFailure: null)
            => Pdb = pdb;

        public AcquiredPortablePdb Pdb { get; }
    }

    public sealed record Unavailable : PortablePdbAcquisitionResult
    {
        internal Unavailable(
            bool windowsPdbDetected,
            PortablePdbStoreFailureKind? storeFailure = null)
            : base(windowsPdbDetected, storeFailure)
        {
        }
    }
}

/// <summary>
/// Downloads and manages symbol packages (.snupkg) from NuGet for SourceLink resolution.
/// Only supports Portable PDBs (embedded or standalone) and snupkg files.
/// </summary>
/// <remarks>
/// <para>
/// Transport is injectable via <see cref="HttpClient"/> and persistence via
/// <see cref="IPdbStore"/>. The host-neutral snupkg parsing lives in
/// <see cref="SnupkgPdbReader"/>. <see cref="AcquirePdbAsync"/> returns
/// repeatable content for either filesystem or in-memory stores;
/// <see cref="DownloadPdbAsync"/> is the desktop compatibility projection that
/// returns only an on-disk path.
/// </para>
/// <para>
/// Symbol server key generation follows the conventions from dotnet/symstore:
/// https://github.com/dotnet/symstore/blob/d66992e7c2f32288fbf1acf08cdea43098025c7c/src/Microsoft.SymbolStore/KeyGenerators/PortablePDBFileKeyGenerator.cs
/// Portable PDBs use {GUID}FFFFFFFF, Windows PDBs use {GUID}{age:x}.
/// </para>
/// </remarks>
public partial class SymbolPackageDownloader
{
    private const string SymbolMissCacheCategory = "symbol-misses";
    private static readonly TimeSpan SymbolMissCacheTtl = TimeSpan.FromDays(1);
    private static readonly TimeSpan SymbolForbiddenCacheTtl = TimeSpan.FromDays(7);
    private readonly HttpClient _client;
    private readonly IPdbStore _pdbStore;
    private readonly IPackageSourceAuthorization? _sourceAuthorization;
    private readonly bool _usePersistentMissCache;
    private readonly SymbolAcquisitionLimits? _limits;
    internal const long DefaultMaximumSymbolBytes = 500_000_000;

    /// <summary>
    /// Creates a downloader backed by the default filesystem PDB cache
    /// (<c>{app-cache}/packages/symbols</c>).
    /// </summary>
    public SymbolPackageDownloader(HttpClient client)
    {
        ArgumentNullException.ThrowIfNull(client);
        _client = client;
        _pdbStore = FileSystemPdbStore.CreateDefault();
        _sourceAuthorization = null;
        _usePersistentMissCache = true;
    }

    /// <summary>
    /// Creates a downloader with an explicit <see cref="IPdbStore"/> for PDB
    /// persistence while retaining the desktop's ambient package-source policy.
    /// </summary>
    /// <remarks>
    /// Persistent negative-result caching is disabled by default for an
    /// explicit store. Set
    /// <paramref name="usePersistentMissCache"/> only when the host has
    /// initialized that disk cache.
    /// </remarks>
    public SymbolPackageDownloader(
        HttpClient client,
        IPdbStore pdbStore,
        bool usePersistentMissCache = false)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(pdbStore);
        _client = client;
        _pdbStore = pdbStore;
        _sourceAuthorization = null;
        _usePersistentMissCache = usePersistentMissCache;
    }

    /// <summary>
    /// Creates a host-neutral downloader with explicit PDB persistence and
    /// package-source authorization.
    /// </summary>
    /// <remarks>
    /// This overload performs no ambient NuGet configuration discovery.
    /// Persistent negative-result caching remains disabled unless explicitly
    /// enabled. <c>SymbolPackageDownloaderTests.AcquirePdbAsync_ExplicitStore_DoesNotUseAmbientCaches</c>
    /// gates both filesystem-free defaults.
    /// </remarks>
    public SymbolPackageDownloader(
        HttpClient client,
        IPdbStore pdbStore,
        IPackageSourceAuthorization sourceAuthorization,
        bool usePersistentMissCache = false)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(pdbStore);
        ArgumentNullException.ThrowIfNull(sourceAuthorization);
        _client = client;
        _pdbStore = pdbStore;
        _sourceAuthorization = sourceAuthorization;
        _usePersistentMissCache = usePersistentMissCache;
    }

    /// <summary>
    /// Creates a host-neutral downloader with explicit persistence,
    /// authorization, and untrusted-content limits.
    /// </summary>
    public SymbolPackageDownloader(
        HttpClient client,
        IPdbStore pdbStore,
        IPackageSourceAuthorization sourceAuthorization,
        SymbolAcquisitionLimits limits,
        bool usePersistentMissCache = false)
        : this(
            client,
            pdbStore,
            sourceAuthorization,
            usePersistentMissCache)
    {
        _limits =
            limits
            ?? throw new ArgumentNullException(nameof(limits));
    }

    /// <summary>
    /// Acquires a Portable PDB through the configured store without requiring a
    /// filesystem path.
    /// </summary>
    public async Task<PortablePdbAcquisitionResult> AcquirePdbAsync(
        Guid pdbGuid, int pdbAge, string pdbFileName, bool isPortable,
        string? assemblyName = null,
        string? packageName = null,
        string? packageVersion = null,
        Action<string>? log = null,
        bool isPlatformAssembly = false,
        bool cacheOnly = false,
        NuGetSourceOptions? sourceOptions = null,
        CancellationToken cancellationToken = default,
        uint? portablePdbStamp = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        bool windowsPdbDetected = false;
        PortablePdbStoreFailureKind? storeFailure = null;

        pdbFileName = GetSymbolFileName(pdbFileName);
        // The PDB file name comes from untrusted PE debug metadata. Only the
        // symbol-server paths (MSDL and the NuGet symbol server) use it to build
        // a cache key and request URL; snupkg acquisition is keyed off the
        // assembly name and debug GUID instead. If the name is not a usable
        // single path segment, skip only those symbol-server paths rather than
        // abandoning snupkg recovery, and never let an invalid store key throw.
        bool pdbFileNameUsable = StorePath.IsSafeSegment(pdbFileName);
        if (!pdbFileNameUsable)
            log?.Invoke("Unusable PDB file name; skipping symbol-server paths");
        string? snupkgAssemblyName =
            GetSnupkgAssemblyName(
                pdbFileName,
                pdbFileNameUsable,
                assemblyName,
                log);

        var guid = pdbGuid.ToString("N").ToUpperInvariant();
        var symbolKey = isPortable
            ? $"{guid}FFFFFFFF"
            : $"{guid}{pdbAge:x}";
        var storeIdentity =
            isPortable && portablePdbStamp is { } stamp
                ? $"{guid}{stamp:X8}"
                : symbolKey;

        // For Microsoft packages or platform assemblies, try MSDL first
        bool isMicrosoftPackage = isPlatformAssembly || IsMicrosoftPackage(packageName);
        if (isMicrosoftPackage && pdbFileNameUsable)
        {
            log?.Invoke(isPlatformAssembly ? "Platform library, trying MSDL symbol server" : "Microsoft package detected, trying MSDL symbol server first");
            var msdlResult = await TryLocateFromMsdlAsync(
                pdbFileName, symbolKey, storeIdentity, pdbGuid, portablePdbStamp,
                isPortable, log, cacheOnly, cancellationToken).ConfigureAwait(false);
            if (msdlResult.Pdb is not null)
            {
                return new PortablePdbAcquisitionResult.Acquired(
                    msdlResult.Pdb,
                    windowsPdbDetected || msdlResult.WindowsPdbDetected);
            }
            if (msdlResult.WindowsPdbDetected)
                windowsPdbDetected = true;
            storeFailure ??= msdlResult.StoreFailure;
        }

        // Try downloading symbol package (.snupkg)
        if (!string.IsNullOrEmpty(packageName)
            && !string.IsNullOrEmpty(packageVersion)
            && snupkgAssemblyName is not null
            && IsNuGetOrgEligibleForPackage(sourceOptions, packageName))
        {
            var snupkgResult = await TryLocateFromSymbolPackageAsync(
                packageName, packageVersion, snupkgAssemblyName, symbolKey,
                storeIdentity,
                pdbGuid, portablePdbStamp, isPortable, log, cacheOnly,
                cancellationToken).ConfigureAwait(false);
            if (snupkgResult.Pdb is not null)
            {
                return new PortablePdbAcquisitionResult.Acquired(
                    snupkgResult.Pdb,
                    windowsPdbDetected || snupkgResult.WindowsPdbDetected);
            }
            if (snupkgResult.WindowsPdbDetected)
                windowsPdbDetected = true;
            storeFailure ??= snupkgResult.StoreFailure;
        }

        // Try NuGet symbol server, then MSDL as fallback (for non-Microsoft packages)
        if (!isMicrosoftPackage && pdbFileNameUsable)
        {
            var symbolResult = await TryLocateFromSymbolServerAsync(
                pdbFileName, symbolKey, storeIdentity, pdbGuid, portablePdbStamp,
                isPortable, log, cacheOnly, cancellationToken).ConfigureAwait(false);
            if (symbolResult.Pdb is not null)
            {
                return new PortablePdbAcquisitionResult.Acquired(
                    symbolResult.Pdb,
                    windowsPdbDetected || symbolResult.WindowsPdbDetected);
            }
            if (symbolResult.WindowsPdbDetected)
                windowsPdbDetected = true;
            storeFailure ??= symbolResult.StoreFailure;
        }

        log?.Invoke(cacheOnly ? "No cached Portable PDB available" : "No Portable PDB available");
        return new PortablePdbAcquisitionResult.Unavailable(
            windowsPdbDetected,
            storeFailure);
    }

    /// <summary>
    /// Downloads a PDB file and returns its path on disk. This compatibility
    /// wrapper is meaningful only with a filesystem-backed store.
    /// </summary>
    public async Task<PdbDownloadResult> DownloadPdbAsync(
        Guid pdbGuid, int pdbAge, string pdbFileName, bool isPortable,
        string assemblyPath,
        string? packageName = null,
        string? packageVersion = null,
        Action<string>? log = null,
        bool isPlatformAssembly = false,
        bool cacheOnly = false,
        NuGetSourceOptions? sourceOptions = null,
        CancellationToken cancellationToken = default,
        uint? portablePdbStamp = null)
    {
        PortablePdbAcquisitionResult result =
            await AcquirePdbAsync(
                pdbGuid,
                pdbAge,
                pdbFileName,
                isPortable,
                Path.GetFileNameWithoutExtension(assemblyPath),
                packageName,
                packageVersion,
                log,
                isPlatformAssembly,
                cacheOnly,
                sourceOptions,
                cancellationToken,
                portablePdbStamp).ConfigureAwait(false);

        if (result is not PortablePdbAcquisitionResult.Acquired acquired)
        {
            return new PdbDownloadResult(
                null,
                result.WindowsPdbDetected,
                StoreFailure: result.StoreFailure);
        }

        string? localPath = acquired.Pdb.LocalPath;
        if (localPath is null)
        {
            log?.Invoke(
                "Portable PDB content was acquired, but the configured store exposes no filesystem path.");
        }

        return new PdbDownloadResult(
            localPath,
            result.WindowsPdbDetected,
            acquired.Pdb.SymbolServer);
    }

    /// <summary>
    /// Checks if a package name indicates it's a Microsoft package.
    /// </summary>
    private static bool IsMicrosoftPackage(string? packageName)
    {
        if (string.IsNullOrEmpty(packageName))
            return false;

        return packageName.StartsWith("Microsoft.", StringComparison.OrdinalIgnoreCase) ||
               packageName.StartsWith("System.", StringComparison.OrdinalIgnoreCase) ||
               packageName.StartsWith("Azure.", StringComparison.OrdinalIgnoreCase) ||
               packageName.Equals("WindowsAzure.Storage", StringComparison.OrdinalIgnoreCase);
    }

    private static string? GetSnupkgAssemblyName(
        string pdbFileName,
        bool pdbFileNameUsable,
        string? fallbackAssemblyName,
        Action<string>? log)
    {
        if (pdbFileNameUsable)
        {
            string fromCodeView =
                Path.GetFileNameWithoutExtension(pdbFileName);
            if (StorePath.IsSafeSegment(fromCodeView))
                return fromCodeView;
        }

        if (!string.IsNullOrWhiteSpace(fallbackAssemblyName)
            && StorePath.IsSafeSegment(fallbackAssemblyName))
        {
            return fallbackAssemblyName;
        }

        log?.Invoke(
            "No usable assembly name is available; skipping the symbol-package path.");
        return null;
    }

    private static string GetSymbolFileName(string pdbPath)
    {
        var normalized = pdbPath.Replace('\\', '/');
        var slash = normalized.LastIndexOf('/');
        return slash >= 0 ? normalized[(slash + 1)..] : normalized;
    }
}
