using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using DotnetInspector.Packages;
using DotnetInspector.Queries;
using ILInspector.Metadata;
using NuGetFetch;
using PackageExtractor = DotnetInspector.Packages.PackageExtractor;

namespace InspectWeb.Engine;

/// <summary>
/// Centralized acquisition: the single owner of package download, the session package cache, the
/// exact package/version/framework identity every browser query is answered against, and the
/// bounded registry of open workspaces. Nothing above this type builds a feed URL, ranks a target
/// framework, or chooses between <c>ref/</c> and <c>lib/</c>; identity comes from
/// <see cref="PackageCompileAssetSelector"/>.
/// </summary>
/// <remarks>
/// <para>
/// Acquisition mints typed <see cref="ResolvedAssemblyReference"/> participants; it never inspects
/// one. Inspection happens only inside a <see cref="BrowserInspectionScope"/>, and only through a
/// public product query that takes the scope's <see cref="AssemblyContextGroup"/>. Browser/Wasm is
/// single-threaded, so both caches are deliberately lock-free.
/// </para>
/// <para>
/// A workspace is keyed by its <em>complete</em> exact coordinate set, so the package surface, a
/// type projection, an annotated member, Integrations, and a composite call-graph workspace over
/// several packages each reuse one open group instead of reacquiring every image. The registry is
/// bounded and disposes the least recently used scope on eviction, which is what returns its
/// retained image bytes. Because a scope is reused, nothing here releases a participant
/// terminally: <c>AssemblyContextIntegrationsQuery.ExecuteParticipantAsync</c>'s release ends that
/// participant's availability for the whole group, so it is only correct for a group that is
/// discarded immediately afterwards.
/// </para>
/// <para>
/// <c>BrowserEngineBoundaryTests.WorkspaceOwnership_AccountsArchivesAndCarriesSelectedFailures</c>
/// gates the aggregate package-cache and scope-retention budget.
/// </para>
/// </remarks>
[SupportedOSPlatform("browser")]
internal static class BrowserPackageWorkspace
{
    const int MaxCachedPackages = 12;
    const long MaxCachedPackageBytes = 128L * 1024 * 1024;
    const long MaxVersionIndexBytes = 1024 * 1024;
    const int MaxOpenScopes = 4;

    static readonly HttpClient Http = new();
    static readonly Dictionary<string, CacheEntry> Cache = new(StringComparer.Ordinal);
    static readonly Dictionary<string, ScopeEntry> Scopes = new(StringComparer.Ordinal);
    static readonly Dictionary<string, long> Reservations = new(StringComparer.Ordinal);
    static readonly Dictionary<string, int> Leases = new(StringComparer.Ordinal);
    static readonly Dictionary<string, Task<byte[]>> PendingDownloads = new(StringComparer.Ordinal);
    static readonly HashSet<string> Downloaded = new(StringComparer.Ordinal);
    static long _clock;

    sealed record CacheEntry(byte[] Bytes, long LastAccess);

    sealed record ScopeEntry(
        BrowserInspectionScope Scope,
        ImmutableHashSet<string> PackageKeys,
        long LastAccess);

    public static BrowserPackageCacheStats Stats() =>
        new(
            Downloaded.Count,
            Cache.Count,
            Scopes.Count,
            Cache.Values.Sum(entry => entry.Bytes.LongLength)
                + Reservations.Values.Sum());

    /// <summary>Acquires one package's content at an exact resolved version.</summary>
    public static async Task<BrowserPackage> AcquireAsync(string packageId, string? version)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId);

        string normalizedId = packageId.ToLowerInvariant();
        string resolvedVersion = await ResolveVersionAsync(normalizedId, version);
        string normalizedVersion = resolvedVersion.ToLowerInvariant();
        string key = $"{normalizedId}@{normalizedVersion}";
        (byte[] bytes, bool fromCache) = await GetBytesAsync(
            normalizedId,
            normalizedVersion,
            key);
        return new BrowserPackage(
            packageId,
            resolvedVersion,
            bytes,
            fromCache);
    }

    /// <summary>
    /// Resolves one exact package/version/framework identity into a selected, acquirable
    /// coordinate. The result carries typed participants but performs no inspection.
    /// </summary>
    public static async Task<BrowserPackageCoordinate> ResolveAsync(
        string packageId,
        string? version,
        string? targetFramework)
    {
        BrowserPackage package = await AcquireAsync(packageId, version);
        PackageCompileAssetSelection selection = PackageCompileAssetSelector.Select(
            package.Content,
            packageId,
            targetFramework);
        return selection.Status switch
        {
            PackageCompileAssetSelectionStatus.NoCompileAssets =>
                throw new InvalidOperationException(
                    $"{package.PackageId} {package.Version} has no compile-time assemblies, so it "
                    + "has no inspection workspace."),
            PackageCompileAssetSelectionStatus.NoMatchingTargetFramework =>
                throw new InvalidOperationException(
                    $"Framework '{targetFramework}' is not present. Available frameworks: "
                    + string.Join(", ", selection.AvailableTargetFrameworks)
                    + "."),
            _ => new BrowserPackageCoordinate(package, selection),
        };
    }

    /// <summary>
    /// Opens — or reuses — the one workspace for an exact set of package coordinates. Several
    /// coordinates produce binding-consistent compile and implementation groups. A workspace-wide
    /// interaction such as the member call graph uses the implementation group: callers in a
    /// sibling package are only visible when that package is a participant of that same group.
    /// </summary>
    /// <remarks>
    /// The returned scope is owned by this registry, not by the caller: it is reused by every
    /// later query over the same coordinate set and disposed when the registry evicts it. Callers
    /// must not dispose it, and must not run a query that releases a participant terminally.
    /// </remarks>
    public static BrowserInspectionScope OpenScope(
        IReadOnlyList<BrowserPackageCoordinate> coordinates)
    {
        ArgumentNullException.ThrowIfNull(coordinates);
        if (coordinates.Count == 0)
            throw new ArgumentException("A workspace requires at least one package coordinate.");

        string key = string.Join(
            "|",
            coordinates.Select(coordinate => coordinate.Key).Order(StringComparer.Ordinal));
        if (Scopes.TryGetValue(key, out ScopeEntry? entry))
        {
            Scopes[key] = entry with { LastAccess = ++_clock };
            TouchPackages(entry.PackageKeys);
            return entry.Scope;
        }

        ImmutableHashSet<string> packageKeys = RetainCoordinatePackages(coordinates);
        var scope = new BrowserInspectionScope(coordinates);
        while (Scopes.Count >= MaxOpenScopes)
        {
            string? oldest = Scopes
                .OrderBy(candidate => candidate.Value.LastAccess)
                .Select(candidate => candidate.Key)
                .FirstOrDefault();
            if (oldest is null)
                break;
            Scopes[oldest].Scope.Dispose();
            Scopes.Remove(oldest);
        }

        Scopes[key] = new ScopeEntry(scope, packageKeys, ++_clock);
        return scope;
    }

    /// <summary>Opens — or reuses — the workspace for one exact package coordinate.</summary>
    public static async Task<BrowserInspectionScope> OpenScopeAsync(
        string packageId,
        string? version,
        string? targetFramework)
        => (await ResolveAndOpenScopeAsync(
            [new BrowserPackageRequest(packageId, version, targetFramework)])).Scope;

    /// <summary>
    /// Resolves and temporarily leases every requested coordinate until the aggregate scope owns
    /// them. A later package acquisition cannot evict an earlier coordinate while a composite
    /// workspace is still being assembled.
    /// </summary>
    public static async Task<BrowserScopeResolution> ResolveAndOpenScopeAsync(
        IReadOnlyList<BrowserPackageRequest> requests)
    {
        ArgumentNullException.ThrowIfNull(requests);
        if (requests.Count == 0)
            throw new ArgumentException("A workspace requires at least one package request.");

        var coordinates = new List<BrowserPackageCoordinate>();
        var coordinateKeys = new HashSet<string>(StringComparer.Ordinal);
        var leasedPackages = new HashSet<string>(StringComparer.Ordinal);
        try
        {
            foreach (BrowserPackageRequest request in requests)
            {
                BrowserPackageCoordinate coordinate = await ResolveAsync(
                    request.PackageId,
                    request.Version,
                    request.TargetFramework);
                string packageKey = PackageKey(coordinate);
                if (leasedPackages.Add(packageKey))
                    LeasePackage(packageKey);
                if (coordinateKeys.Add(coordinate.Key))
                    coordinates.Add(coordinate);
            }

            BrowserInspectionScope scope = OpenScope(coordinates);
            return new BrowserScopeResolution(scope, [.. coordinates]);
        }
        finally
        {
            foreach (string packageKey in leasedPackages)
                ReleasePackageLease(packageKey);
        }
    }

    static async Task<string> ResolveVersionAsync(string normalizedId, string? requestedVersion)
    {
        if (!string.IsNullOrWhiteSpace(requestedVersion)
            && !requestedVersion.Equals("latest", StringComparison.OrdinalIgnoreCase))
        {
            return requestedVersion;
        }

        // The product's listed-version owners resolve NuGet.config and the on-disk content cache
        // before they answer, neither of which exists in a browser. Read the flat-container index
        // for the product-owned nuget.org base address instead.
        string index = $"{FlatContainer()}/{Uri.EscapeDataString(normalizedId)}/index.json";
        byte[] bytes = await DownloadBytesAsync(
            index,
            MaxVersionIndexBytes,
            $"The version index for package '{normalizedId}'");
        using var document = System.Text.Json.JsonDocument.Parse(bytes);
        return document.RootElement.GetProperty("versions")
            .EnumerateArray()
            .Select(element => element.GetString())
            .OfType<string>()
            .LastOrDefault(candidate => !candidate.Contains('-'))
            ?? throw new InvalidOperationException(
                $"Package '{normalizedId}' has no stable published version. "
                + "Specify a prerelease version explicitly.");
    }

    static string FlatContainer() =>
        PackageSource.NuGetOrg.GetFlatContainerUrl()
        ?? throw new InvalidOperationException(
            "nuget.org exposes no flat-container endpoint.");

    static async Task<(byte[] Bytes, bool FromCache)> GetBytesAsync(
        string normalizedId,
        string normalizedVersion,
        string key)
    {
        if (Cache.TryGetValue(key, out CacheEntry? cached))
        {
            Cache[key] = cached with { LastAccess = ++_clock };
            return (cached.Bytes, true);
        }
        if (PendingDownloads.TryGetValue(key, out Task<byte[]>? pending))
            return (await pending, true);

        Task<byte[]> download = DownloadAndCacheAsync(
            normalizedId,
            normalizedVersion,
            key);
        PendingDownloads.Add(key, download);
        try
        {
            return (await download, false);
        }
        finally
        {
            PendingDownloads.Remove(key);
        }
    }

    static async Task<byte[]> DownloadAndCacheAsync(
        string normalizedId,
        string normalizedVersion,
        string key)
    {
        string url = await PackageExtractor.GetPackageDownloadUrlAsync(
                Http,
                PackageSource.NuGetOrg,
                normalizedId,
                normalizedVersion,
                log: null)
            ?? throw new InvalidOperationException(
                $"nuget.org exposes no download address for {normalizedId} {normalizedVersion}.");
        using HttpResponseMessage response = await Http.GetAsync(
            url,
            HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();
        long declaredLength = response.Content.Headers.ContentLength
            ?? throw new InvalidOperationException(
                $"Package '{normalizedId}' {normalizedVersion} did not declare its byte length, "
                + "so the browser cannot reserve its package-cache budget before download.");

        using PackageDownloadReservation reservation = ReservePackageDownload(
            key,
            declaredLength);
        byte[] bytes;
        try
        {
            using Stream source = await response.Content.ReadAsStreamAsync();
            bytes = await BoundedContentReader.ReadAllBytesAsync(
                source,
                MaxCachedPackageBytes,
                declaredLength);
        }
        catch (InvalidDataException ex)
        {
            throw new InvalidOperationException(
                $"Package '{normalizedId}' {normalizedVersion} exceeds the browser byte limit.",
                ex);
        }

        try
        {
            BrowserPackageArchiveValidator.Validate(bytes);
        }
        catch (InvalidDataException ex)
        {
            throw new InvalidOperationException(
                $"Package '{normalizedId}' {normalizedVersion} is not a supported ZIP archive.",
                ex);
        }
        reservation.Commit(bytes);
        Downloaded.Add(key);
        return bytes;
    }

    static ImmutableHashSet<string> RetainCoordinatePackages(
        IReadOnlyList<BrowserPackageCoordinate> coordinates)
    {
        Dictionary<string, BrowserPackage> packages = coordinates
            .GroupBy(PackageKey, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.First().Package,
                StringComparer.Ordinal);
        if (packages.Count > MaxCachedPackages)
        {
            throw new InvalidOperationException(
                "The requested workspace's package count exceeds the browser package-cache limit.");
        }

        ImmutableHashSet<string> packageKeys =
            packages.Keys.ToImmutableHashSet(StringComparer.Ordinal);
        foreach ((string packageKey, BrowserPackage package) in packages)
        {
            if (!Cache.TryGetValue(packageKey, out CacheEntry? entry)
                || !ReferenceEquals(entry.Bytes, package.RetainedBytes))
            {
                throw new InvalidOperationException(
                    "A resolved browser package escaped aggregate cache accounting before its "
                    + "workspace opened.");
            }
        }

        TouchPackages(packageKeys);
        return packageKeys;
    }

    static void MakeCacheRoom(
        long additionalBytes,
        int additionalEntries)
    {
        while (Cache.Count + Reservations.Count + additionalEntries > MaxCachedPackages
            || Cache.Values.Sum(entry => entry.Bytes.LongLength)
                + Reservations.Values.Sum()
                + additionalBytes
                > MaxCachedPackageBytes)
        {
            string? oldest = Cache
                .Where(entry => !Leases.ContainsKey(entry.Key))
                .OrderBy(entry => entry.Value.LastAccess)
                .Select(entry => entry.Key)
                .FirstOrDefault();
            if (oldest is null)
            {
                throw new InvalidOperationException(
                    "The browser package-cache limit cannot accommodate the requested workspace.");
            }

            EvictPackage(oldest);
        }
    }

    static void EvictPackage(string packageKey)
    {
        string[] retainedScopes =
        [
            .. Scopes
                .Where(entry => entry.Value.PackageKeys.Contains(packageKey))
                .Select(entry => entry.Key),
        ];
        foreach (string scopeKey in retainedScopes)
        {
            Scopes[scopeKey].Scope.Dispose();
            Scopes.Remove(scopeKey);
        }

        Cache.Remove(packageKey);
    }

    internal static PackageDownloadReservation ReservePackageDownload(
        string packageKey,
        long declaredLength)
    {
        if (declaredLength < 0 || declaredLength > MaxCachedPackageBytes)
        {
            throw new InvalidOperationException(
                "The package exceeds the browser package-cache byte limit.");
        }
        if (Reservations.ContainsKey(packageKey))
            throw new InvalidOperationException("The package download is already reserved.");

        MakeCacheRoom(declaredLength, additionalEntries: 1);
        Reservations.Add(packageKey, declaredLength);
        return new PackageDownloadReservation(packageKey, declaredLength);
    }

    static void LeasePackage(string packageKey)
    {
        if (!Cache.ContainsKey(packageKey))
            throw new InvalidOperationException("A package must be cached before it can be leased.");
        Leases[packageKey] = Leases.TryGetValue(packageKey, out int count) ? count + 1 : 1;
    }

    static void ReleasePackageLease(string packageKey)
    {
        if (!Leases.TryGetValue(packageKey, out int count))
            throw new InvalidOperationException("The package lease is not active.");
        if (count == 1)
            Leases.Remove(packageKey);
        else
            Leases[packageKey] = count - 1;
    }

    internal static void RegisterAcquiredPackage(BrowserPackage package)
    {
        ArgumentNullException.ThrowIfNull(package);
        string key = PackageKey(package.PackageId, package.Version);
        Cache.Remove(key);
        MakeCacheRoom(package.RetainedBytes.LongLength, additionalEntries: 1);
        Cache[key] = new CacheEntry(package.RetainedBytes, ++_clock);
    }

    static void TouchPackages(IEnumerable<string> packageKeys)
    {
        foreach (string packageKey in packageKeys)
        {
            if (!Cache.TryGetValue(packageKey, out CacheEntry? entry))
            {
                throw new InvalidOperationException(
                    "An open browser workspace lost its retained package-cache entry.");
            }

            Cache[packageKey] = entry with { LastAccess = ++_clock };
        }
    }

    static string PackageKey(BrowserPackageCoordinate coordinate) =>
        PackageKey(coordinate.PackageId, coordinate.Version);

    static string PackageKey(string packageId, string version) =>
        $"{packageId.ToLowerInvariant()}@{version.ToLowerInvariant()}";

    static async Task<byte[]> DownloadBytesAsync(
        string url,
        long maxBytes,
        string description)
    {
        using HttpResponseMessage response = await Http.GetAsync(
            url,
            HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();
        try
        {
            using Stream source = await response.Content.ReadAsStreamAsync();
            return await BoundedContentReader.ReadAllBytesAsync(
                source,
                maxBytes,
                response.Content.Headers.ContentLength);
        }
        catch (InvalidDataException ex)
        {
            throw new InvalidOperationException(
                $"{description} exceeds the browser byte limit.",
                ex);
        }
    }

    internal sealed class PackageDownloadReservation(string packageKey, long reservedBytes)
        : IDisposable
    {
        bool _completed;

        public void Commit(byte[] bytes)
        {
            ArgumentNullException.ThrowIfNull(bytes);
            if (_completed)
                throw new InvalidOperationException("The package reservation is complete.");
            if (bytes.LongLength != reservedBytes)
            {
                throw new InvalidDataException(
                    "The downloaded package length does not match its reservation.");
            }

            Reservations.Remove(packageKey);
            Cache[packageKey] = new CacheEntry(bytes, ++_clock);
            _completed = true;
        }

        public void Dispose()
        {
            if (_completed)
                return;
            Reservations.Remove(packageKey);
            _completed = true;
        }
    }
}

/// <summary>One exact package coordinate request used to assemble a browser workspace.</summary>
internal sealed record BrowserPackageRequest(
    string PackageId,
    string? Version,
    string? TargetFramework);

/// <summary>
/// A registry-owned scope together with the coordinates resolved for this request, in request
/// order. The scope may have been opened earlier with the same coordinate set in another order.
/// </summary>
internal sealed record BrowserScopeResolution(
    BrowserInspectionScope Scope,
    ImmutableArray<BrowserPackageCoordinate> RequestedCoordinates);

/// <summary>One acquired package: its exact identity and its content.</summary>
[SupportedOSPlatform("browser")]
internal sealed class BrowserPackage
{
    const long MaxAssemblyEntryBytes = BrowserInspectionScope.MaxRetainedImageBytes;
    const long MaxTextEntryBytes = 16L * 1024 * 1024;

    public BrowserPackage(
        string packageId,
        string version,
        byte[] retainedBytes,
        bool fromCache)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId);
        ArgumentException.ThrowIfNullOrWhiteSpace(version);
        ArgumentNullException.ThrowIfNull(retainedBytes);
        BrowserPackageArchiveValidator.Validate(retainedBytes);
        PackageId = packageId;
        Version = version;
        RetainedBytes = retainedBytes;
        Content = new InMemoryPackageContent(retainedBytes, fromCache, "nuget.org");
    }

    public string PackageId { get; }

    public string Version { get; }

    public InMemoryPackageContent Content { get; }

    internal byte[] RetainedBytes { get; }

    /// <summary>
    /// The package's browsable Markdown: a root <c>README.md</c>/<c>PACKAGE.md</c> and any
    /// <c>*.md</c> under a <c>skills</c> directory. Presence and size only; bodies are served by
    /// <see cref="ReadDocument"/>, which accepts only a path from this list, so no caller can
    /// coax an arbitrary entry — an assembly, a signature — out of the package.
    /// </summary>
    public IReadOnlyList<BrowserPackageDocument> Documents()
    {
        var documents = new List<BrowserPackageDocument>();
        foreach (string entry in Content.EnumerateEntries())
        {
            string[] segments = entry.Split('/');
            string fileName = segments[^1];
            bool isRoot = segments.Length == 1;
            string? kind =
                isRoot && fileName.Equals("README.md", StringComparison.OrdinalIgnoreCase) ? "readme"
                : isRoot && fileName.Equals("PACKAGE.md", StringComparison.OrdinalIgnoreCase) ? "package"
                : fileName.EndsWith(".md", StringComparison.OrdinalIgnoreCase)
                    && IsUnderSkillsDirectory(segments) ? "skill"
                : null;
            if (kind is null)
                continue;
            if (!Content.TryGetEntryLength(entry, out long length))
                throw new InvalidOperationException("A listed package document disappeared.");
            if (length > MaxTextEntryBytes || length > int.MaxValue)
            {
                throw new InvalidOperationException(
                    $"A browsable document in {PackageId} {Version} exceeds the browser byte "
                    + "limit.");
            }

            documents.Add(new BrowserPackageDocument(
                kind,
                kind == "skill" ? SkillDisplayName(segments) : fileName,
                entry,
                (int)length));
        }

        return
        [
            .. documents
                .OrderBy(document => document.Kind switch { "readme" => 0, "package" => 1, _ => 2 })
                .ThenBy(document => document.Name, StringComparer.OrdinalIgnoreCase),
        ];
    }

    public BrowserPackageDocumentContent ReadDocument(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        BrowserPackageDocument document = Documents()
            .FirstOrDefault(candidate => candidate.Path.Equals(path, StringComparison.Ordinal))
            ?? throw new InvalidOperationException(
                $"'{path}' is not a browsable document in {PackageId} {Version}.");
        return new BrowserPackageDocumentContent(
            document.Kind,
            document.Name,
            document.Path,
            Encoding.UTF8.GetString(Read(document.Path, MaxTextEntryBytes)));
    }

    internal Stream OpenEntry(string path, long maxExpandedBytes)
        => Content.TryOpenEntry(path, maxExpandedBytes, out Stream? stream)
            ? stream
            : throw new InvalidOperationException($"'{path}' was not found in {PackageId} {Version}.");

    internal byte[] Read(string path, long maxExpandedBytes)
    {
        using Stream stream = OpenEntry(path, maxExpandedBytes);
        if (stream is MemoryStream memory
            && memory.TryGetBuffer(out ArraySegment<byte> segment)
            && segment.Offset == 0
            && segment.Count == segment.Array!.Length)
        {
            return segment.Array;
        }

        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }

    internal bool TryRead(string path, long maxExpandedBytes, out byte[] bytes)
    {
        if (!Content.TryOpenEntry(path, maxExpandedBytes, out Stream? stream))
        {
            bytes = [];
            return false;
        }

        using (stream)
        {
            if (stream is MemoryStream memory
                && memory.TryGetBuffer(out ArraySegment<byte> segment)
                && segment.Offset == 0
                && segment.Count == segment.Array!.Length)
            {
                bytes = segment.Array;
                return true;
            }

            using var buffer = new MemoryStream();
            stream.CopyTo(buffer);
            bytes = buffer.ToArray();
            return true;
        }
    }

    internal bool TryReadText(string path, out byte[] bytes) =>
        TryRead(path, MaxTextEntryBytes, out bytes);

    /// <summary>
    /// Mints one typed acquisition participant for a selected package entry. A healthy image uses
    /// its real metadata identity. A malformed, native, or module image uses its selected asset
    /// name only as a rejection carrier, so the workspace query reports that participant's typed
    /// acquisition failure instead of silently shortening the selected assembly set.
    /// </summary>
    internal ResolvedAssemblyReference CreateReference(
        string path,
        AssemblyResolutionProvenance provenance)
    {
        AssemblyReferenceIdentity? identity = null;
        try
        {
            using var peReader = new PEReader(
                ImmutableCollectionsMarshal.AsImmutableArray(
                    Read(path, MaxAssemblyEntryBytes)));
            if (peReader.HasMetadata)
            {
                MetadataReader reader = peReader.GetMetadataReader();
                if (reader.IsAssembly)
                    identity = AssemblyReferenceIdentity.FromAssemblyDefinition(reader);
            }
        }
        catch (BadImageFormatException)
        {
        }

        return ResolvedAssemblyReference.Create(
            identity ?? new AssemblyReferenceIdentity(
                Path.GetFileNameWithoutExtension(path),
                Version: null,
                Culture: null,
                PublicKeyToken: null),
            path: null,
            () => OpenEntry(path, MaxAssemblyEntryBytes),
            provenance);
    }

    static bool IsUnderSkillsDirectory(string[] segments)
    {
        for (int index = 0; index < segments.Length - 1; index++)
            if (segments[index].Equals("skills", StringComparison.OrdinalIgnoreCase))
                return true;
        return false;
    }

    static string SkillDisplayName(string[] segments)
    {
        for (int index = 0; index < segments.Length - 1; index++)
        {
            if (!segments[index].Equals("skills", StringComparison.OrdinalIgnoreCase))
                continue;
            return index + 2 < segments.Length ? segments[index + 1] : segments[^1];
        }
        return segments[^1];
    }
}

/// <summary>
/// One resolved package/version/framework coordinate and the compile assets the product selector
/// chose for it. This is acquisition state: it names what a workspace would contain, and inspects
/// nothing.
/// </summary>
[SupportedOSPlatform("browser")]
internal sealed class BrowserPackageCoordinate(
    BrowserPackage package,
    PackageCompileAssetSelection selection)
{
    public BrowserPackage Package { get; } = package;

    public PackageCompileAssetSelection Selection { get; } = selection;

    public string PackageId => Package.PackageId;

    public string Version => Package.Version;

    public string Framework => Selection.TargetFramework!;

    /// <summary>
    /// The exact coordinate this workspace answers for. It is the registry key, so two requests
    /// for the same package, resolved version, and framework reuse one open workspace rather than
    /// reacquiring every image.
    /// </summary>
    public string Key =>
        $"{PackageId.ToLowerInvariant()}@{Version.ToLowerInvariant()}/{Framework.ToLowerInvariant()}";

    public PackageCompileAsset DefaultAsset => Selection.DefaultAsset!;

    /// <summary>Every assembly the package ships for the selected framework.</summary>
    public IReadOnlyList<PackageCompileAsset> FrameworkAssets => Selection.FrameworkAssets;

    /// <summary>Every implementation assembly the package ships for the selected framework.</summary>
    public IReadOnlyList<PackageCompileAsset> ImplementationAssets =>
        Selection.ImplementationAssets;

    /// <summary>The selected compile asset for one assembly, by product-owned identity or name.</summary>
    public PackageCompileAsset CompileAsset(string assemblyIdOrName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assemblyIdOrName);
        return Selection.FindAsset(assemblyIdOrName)
            ?? Selection.Assets.FirstOrDefault(asset => MatchesAssembly(asset, assemblyIdOrName))
            ?? throw new InvalidOperationException(
                $"'{assemblyIdOrName}' is not a selected compile assembly of "
                + $"{PackageId} {Version} for {Framework}.");
    }

    /// <summary>
    /// The implementation assembly for one assembly name in the selected framework. Reference
    /// assemblies carry no method bodies, so body-backed work resolves the <c>lib/</c> asset from
    /// the same discovered set rather than reasoning about package paths.
    /// </summary>
    public PackageCompileAsset ImplementationAsset(string assemblyIdOrName)
    {
        PackageCompileAsset selected = CompileAsset(assemblyIdOrName);
        if (selected.Kind == PackageCompileAssetKind.Library)
            return selected;

        return Selection.FindImplementationAsset(selected)
            ?? throw new InvalidOperationException(
                $"{PackageId} {Version} ships {selected.AssemblyName} for {Framework} as a "
                + "reference assembly only, so it carries no method bodies.");
    }

    internal static bool MatchesAssembly(PackageCompileAsset asset, string name) =>
        asset.AssemblyName.Equals(name, StringComparison.OrdinalIgnoreCase)
        || Path.GetFileNameWithoutExtension(asset.AssemblyName)
            .Equals(Path.GetFileNameWithoutExtension(name), StringComparison.OrdinalIgnoreCase);
}
