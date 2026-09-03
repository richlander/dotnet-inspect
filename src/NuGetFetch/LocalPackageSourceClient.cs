using System.Text;
using InertText;
using NuGet.Versioning;

namespace NuGetFetch;

public static partial class PackageSourceClientFactory
{
    /// <summary>
    /// Creates a bounded client for one canonical local-folder source.
    /// </summary>
    public static IPackageSourceClient Create(
        LocalPackageSourceIdentity source,
        PackageSourceAssociation association,
        LocalPackageSourceOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(association);
        ILocalPackageSourceFileSystem host = OperatingSystem.IsBrowser()
            ? UnavailableLocalPackageSourceFileSystem.Instance
            : PhysicalLocalPackageSourceFileSystem.Instance;
        return Create(source, association, host, options);
    }

    internal static IPackageSourceClient Create(
        LocalPackageSourceIdentity source,
        PackageSourceAssociation association,
        ILocalPackageSourceFileSystem host,
        LocalPackageSourceOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(association);
        ArgumentNullException.ThrowIfNull(host);
        PackageProducerIdentity producer = CreateLocalProducer(source);
        PackageSourceResultFactory results = CreateResultFactory(
            producer,
            association,
            PackageSourceKind.LocalFolder);
        return new LocalFolderPackageSourceClient(
            source,
            results,
            host,
            LocalPackageSourceOptions.Validate(
                options ?? new LocalPackageSourceOptions()));
    }

    private static PackageProducerIdentity CreateLocalProducer(
        LocalPackageSourceIdentity source)
    {
        byte[] identity = Encoding.UTF8.GetBytes(source.PersistentValue);
        string key = "nfs-local-1."
            + Convert.ToBase64String(identity)
                .TrimEnd('=')
                .Replace('+', '-')
                .Replace('/', '_');
        return new PackageProducerIdentity(
            OwnerCapability,
            key,
            new InertString(TextPolicy.Field, source.CanonicalPath));
    }
}

internal sealed class LocalFolderPackageSourceClient
    : IPackageSourceClient
{
    private readonly LocalPackageSourceIdentity _identity;
    private readonly PackageSourceResultFactory _results;
    private readonly ILocalPackageSourceFileSystem _host;
    private readonly LocalPackageSourceOptions _options;

    public LocalFolderPackageSourceClient(
        LocalPackageSourceIdentity identity,
        PackageSourceResultFactory results,
        ILocalPackageSourceFileSystem host,
        LocalPackageSourceOptions options)
    {
        _identity = identity;
        _results = results;
        _host = host;
        _options = options;
    }

    public PackageSourceResultIdentity Source => _results.Source;

    public PackageSourceCapabilities Capabilities
    {
        get
        {
            LocalPackageSourceHostCapabilities capabilities =
                _host.Capabilities;
            PackageSourceCapabilities result =
                PackageSourceCapabilities.None;
            if (capabilities.HasFlag(
                    LocalPackageSourceHostCapabilities.List
                    | LocalPackageSourceHostCapabilities.Read))
            {
                result |= PackageSourceCapabilities.Search
                    | PackageSourceCapabilities.VersionEnumeration
                    | PackageSourceCapabilities.Manifest;
            }

            if (capabilities.HasFlag(
                    LocalPackageSourceHostCapabilities.List
                    | LocalPackageSourceHostCapabilities.Read
                    | LocalPackageSourceHostCapabilities.Transfer))
            {
                result |= PackageSourceCapabilities.PackagePayload;
            }

            return result;
        }
    }

    public Task<PackageSourceOperationResult<PackageSearchResult>> SearchAsync(
        string query,
        int take = 20,
        bool prerelease = false,
        CancellationToken cancellationToken = default,
        NuGetOperationContext? operationContext = null) =>
        SearchCoreAsync(
            query,
            take,
            prerelease,
            prefix: false,
            cancellationToken,
            operationContext);

    public Task<PackageSourceOperationResult<PackageSearchResult>>
        SearchByPrefixAsync(
        string prefix,
        int take = 100,
        bool prerelease = false,
        CancellationToken cancellationToken = default,
        NuGetOperationContext? operationContext = null) =>
        SearchCoreAsync(
            prefix,
            take,
            prerelease,
            prefix: true,
            cancellationToken,
            operationContext);

    public async Task<PackageSourceOperationResult<PackageVersionResult>>
        GetVersionsAsync(
        string packageId,
        CancellationToken cancellationToken = default,
        NuGetOperationContext? operationContext = null)
    {
        PackageCoordinateValidation.ValidatePackageId(
            packageId,
            nameof(packageId));
        using NuGetOperationDeadline operation =
            CreateOperation(cancellationToken, operationContext);
        return await PackageSourceOperation.CaptureVersionsAsync(
            _results,
            async () =>
            {
                operation.ThrowIfExpired();
                RequireCapability(
                    PackageSourceCapabilities.VersionEnumeration);
                var engine = CreateEngine(operation);
                IReadOnlyList<LocalPackageObservation> observations =
                    await engine.ObserveAsync(
                        packageId.ToLowerInvariant(),
                        version: null).ConfigureAwait(false);
                PackageCandidateObservation[] candidates = observations
                    .Where(
                        candidate => candidate.Archive.Coordinate.PackageId
                            .Equals(
                                packageId,
                                StringComparison.OrdinalIgnoreCase))
                    .OrderBy(
                        candidate => NuGetVersion.Parse(
                            candidate.Archive.Coordinate.Version),
                        VersionComparer.VersionRelease)
                    .Select(
                        candidate => _results.Candidate(
                            candidate.Archive.Coordinate,
                            PackageDiscoveryContract
                                .CompleteVersionEnumeration,
                            PackageListingState.NotApplicable))
                    .ToArray();
                return _results.Versions(
                    candidates,
                    hasAuthoritativeListingState: false,
                    operation);
            },
            cancellationToken,
            operationContext,
            operation).ConfigureAwait(false);
    }

    public async Task<PackageSourceOperationResult<PackageSourcePayload>>
        GetPackageAsync(
        string packageId,
        string version,
        CancellationToken cancellationToken = default,
        NuGetOperationContext? operationContext = null)
    {
        PackageSourceCoordinate coordinate =
            PackageSourceCoordinate.Create(packageId, version);
        return await PackageSourceOperation.CapturePackageAsync(
            _results,
            coordinate,
            async () =>
            {
                NuGetOperationDeadline? operation =
                    CreateOperation(cancellationToken, operationContext);
                try
                {
                    operation.ThrowIfExpired();
                    RequireCapability(
                        PackageSourceCapabilities.PackagePayload);
                    var engine = CreateEngine(operation);
                    LocalPackageObservation observation =
                        await engine.ObserveExactAsync(
                            coordinate,
                            transferStream: true).ConfigureAwait(false);
                    Stream content;
                    try
                    {
                        operation.ThrowIfExpired();
                        content = new LocalPackagePayloadStream(
                            observation.Content!,
                            operation,
                            Source);
                    }
                    catch
                    {
                        await LocalPackageSourceCleanup.DisposeAsync(
                            observation.Content!,
                            operation).ConfigureAwait(false);
                        throw;
                    }

                    try
                    {
                        PackageSourcePayload payload = _results.Payload(
                            coordinate,
                            PackageSourcePayloadKind.Package,
                            content,
                            observation.Length);
                        operation = null;
                        return payload;
                    }
                    catch
                    {
                        await content.DisposeAsync().ConfigureAwait(false);
                        throw;
                    }
                }
                finally
                {
                    operation?.Dispose();
                }
            },
            cancellationToken,
            operationContext).ConfigureAwait(false);
    }

    public async Task<PackageSourceOperationResult<PackageSourceManifest>>
        GetManifestAsync(
        string packageId,
        string version,
        CancellationToken cancellationToken = default,
        NuGetOperationContext? operationContext = null)
    {
        PackageSourceCoordinate coordinate =
            PackageSourceCoordinate.Create(packageId, version);
        using NuGetOperationDeadline operation =
            CreateOperation(cancellationToken, operationContext);
        return await PackageSourceOperation.CaptureManifestAsync(
            _results,
            coordinate,
            async () =>
            {
                operation.ThrowIfExpired();
                RequireCapability(PackageSourceCapabilities.Manifest);
                var engine = CreateEngine(operation);
                LocalPackageObservation observation =
                    await engine.ObserveExactAsync(
                        coordinate,
                        transferStream: false).ConfigureAwait(false);
                operation.ThrowIfExpired();
                return _results.Manifest(
                    coordinate,
                    observation.Archive.Manifest);
            },
            cancellationToken,
            operationContext).ConfigureAwait(false);
    }

    public async Task<PackageSourceOperationResult<PackageSourcePayload>>
        TryGetSymbolsAsync(
        string packageId,
        string version,
        CancellationToken cancellationToken = default,
        NuGetOperationContext? operationContext = null)
    {
        PackageSourceCoordinate coordinate =
            PackageSourceCoordinate.Create(packageId, version);
        using NuGetOperationDeadline operation =
            CreateOperation(cancellationToken, operationContext);
        return await PackageSourceOperation.CaptureSymbolsAsync(
            _results,
            coordinate,
            () =>
            {
                operation.ThrowIfExpired();
                return Task.FromException<PackageSourcePayload>(
                    new NuGetSourceCapabilityUnavailableException());
            },
            cancellationToken,
            operationContext).ConfigureAwait(false);
    }

    public void Dispose()
    {
    }

    private async Task<PackageSourceOperationResult<PackageSearchResult>>
        SearchCoreAsync(
        string query,
        int take,
        bool prerelease,
        bool prefix,
        CancellationToken cancellationToken,
        NuGetOperationContext? operationContext)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentOutOfRangeException.ThrowIfNegative(take);
        using NuGetOperationDeadline operation =
            CreateOperation(cancellationToken, operationContext);
        return await PackageSourceOperation.CaptureSearchAsync(
            _results,
            async () =>
            {
                operation.ThrowIfExpired();
                RequireCapability(PackageSourceCapabilities.Search);
                var engine = CreateEngine(operation);
                IReadOnlyList<LocalPackageObservation> observations =
                    await engine.ObserveAsync(
                        packageId: null,
                        version: null).ConfigureAwait(false);
                IEnumerable<LocalPackageObservation> matches =
                    observations.Where(
                        candidate =>
                            (prerelease
                                || !NuGetVersion.Parse(
                                        candidate.Archive.Coordinate.Version)
                                    .IsPrerelease)
                            && IsMatch(candidate.Archive, query, prefix));
                LocalPackageObservation[] complete = matches
                    .GroupBy(
                        candidate =>
                            candidate.Archive.Coordinate.PackageId,
                        StringComparer.Ordinal)
                    .Select(
                        group => group.MaxBy(
                            candidate => NuGetVersion.Parse(
                                candidate.Archive.Coordinate.Version),
                            VersionComparer.VersionRelease)!)
                    .OrderBy(
                        candidate =>
                            candidate.Archive.Coordinate.PackageId,
                        StringComparer.Ordinal)
                    .ToArray();
                PackageSearchTruncationReason truncation =
                    complete.Length > take
                        ? PackageSearchTruncationReason.RequestedLimit
                        : PackageSearchTruncationReason.None;
                SearchResult[] results = complete
                    .Take(take)
                    .Select(
                        candidate => new SearchResult(
                            candidate.Archive.AuthoredId,
                            candidate.Archive.AuthoredVersion,
                            candidate.Archive.Description))
                    .ToArray();
                return _results.Search(
                    results,
                    truncation,
                    PackageListingState.NotApplicable,
                    operation);
            },
            cancellationToken,
            operationContext,
            operation).ConfigureAwait(false);
    }

    private static bool IsMatch(
        LocalPackageArchive archive,
        string query,
        bool prefix)
    {
        if (query.Length == 0)
            return true;

        StringComparison comparison =
            StringComparison.OrdinalIgnoreCase;
        if (prefix)
            return archive.Coordinate.PackageId.StartsWith(query, comparison);

        return archive.Coordinate.PackageId.Contains(query, comparison)
            || archive.Description?.Contains(query, comparison) == true
            || archive.Tags?.Contains(query, comparison) == true;
    }

    private void RequireCapability(PackageSourceCapabilities capability)
    {
        if (!Capabilities.HasFlag(capability))
            throw new NuGetSourceCapabilityUnavailableException();
    }

    private LocalPackageSourceEngine CreateEngine(
        NuGetOperationDeadline operation) =>
        new(_identity, _host, _options, operation);

    private NuGetOperationDeadline CreateOperation(
        CancellationToken cancellationToken,
        NuGetOperationContext? operationContext) =>
        operationContext is null
            ? new NuGetOperationDeadline(
                new NuGetFetchOptions(),
                Timeout.InfiniteTimeSpan,
                cancellationToken,
                Source)
            : operationContext.CreateDeadline(
                Timeout.InfiniteTimeSpan,
                cancellationToken,
                Source);
}

internal sealed class LocalPackageSourceEngine
{
    private readonly LocalPackageSourceIdentity _identity;
    private readonly ILocalPackageSourceFileSystem _host;
    private readonly LocalPackageSourceOptions _options;
    private readonly NuGetOperationDeadline _operation;
    private readonly LocalPackageSourceLedger _ledger;

    public LocalPackageSourceEngine(
        LocalPackageSourceIdentity identity,
        ILocalPackageSourceFileSystem host,
        LocalPackageSourceOptions options,
        NuGetOperationDeadline operation)
    {
        _identity = identity;
        _host = host;
        _options = options;
        _operation = operation;
        _ledger = new LocalPackageSourceLedger(options);
    }

    public async Task<IReadOnlyList<LocalPackageObservation>> ObserveAsync(
        string? packageId,
        string? version) =>
        await ObserveCoreAsync(
            packageId,
            version,
            transferMatchingStream: false).ConfigureAwait(false);

    private async Task<IReadOnlyList<LocalPackageObservation>> ObserveCoreAsync(
        string? packageId,
        string? version,
        bool transferMatchingStream)
    {
        LocalPackageSourceDirectory root = GetRoot();
        IReadOnlyList<LocalPackageCandidate> candidates =
            Discover(root, packageId, version);
        var observations =
            new List<LocalPackageObservation>(candidates.Count);
        var coordinates = new HashSet<PackageSourceCoordinate>();
        try
        {
            foreach (LocalPackageCandidate candidate in candidates)
            {
                _operation.ThrowIfExpired();
                LocalPackageObservation observation =
                    await ReadCandidateAsync(
                        candidate,
                        transferStream: transferMatchingStream)
                        .ConfigureAwait(false);
                if (!CandidateMatchesRequest(
                        observation.Archive.Coordinate,
                        packageId,
                        version))
                {
                    if (observation.Content is not null)
                    {
                        await LocalPackageSourceCleanup.DisposeAsync(
                            observation.Content,
                            _operation).ConfigureAwait(false);
                    }

                    continue;
                }

                if (!coordinates.Add(observation.Archive.Coordinate))
                {
                    if (observation.Content is not null)
                    {
                        await LocalPackageSourceCleanup.DisposeAsync(
                            observation.Content,
                            _operation).ConfigureAwait(false);
                    }

                    throw new InvalidDataException(
                        "The local package source contains a duplicate package coordinate.");
                }

                observations.Add(observation);
            }

            _operation.ThrowIfExpired();
            return observations;
        }
        catch
        {
            foreach (LocalPackageObservation retained in observations)
            {
                if (retained.Content is not null)
                {
                    await LocalPackageSourceCleanup.DisposeAsync(
                        retained.Content,
                        _operation).ConfigureAwait(false);
                }
            }

            throw;
        }
    }

    public async Task<LocalPackageObservation> ObserveExactAsync(
        PackageSourceCoordinate coordinate,
        bool transferStream)
    {
        IReadOnlyList<LocalPackageObservation> observations =
            await ObserveCoreAsync(
                coordinate.PackageId,
                coordinate.Version,
                transferMatchingStream: transferStream)
                .ConfigureAwait(false);
        if (observations.Count == 0)
            throw new LocalPackageSourceNotFoundException();

        return observations[0];
    }

    private LocalPackageSourceDirectory GetRoot()
    {
        _operation.ThrowIfExpired();
        LocalPackageSourceDirectory? root;
        bool found;
        try
        {
            found = _host.TryGetDirectory(_identity, out root);
        }
        catch
        {
            _operation.ThrowIfExpired();
            throw;
        }

        _operation.ThrowIfExpired();
        if (!found || root is null)
        {
            throw new DirectoryNotFoundException(
                "The local package-source root is unavailable.");
        }

        _operation.ThrowIfExpired();
        return root;
    }

    private IReadOnlyList<LocalPackageCandidate> Discover(
        LocalPackageSourceDirectory root,
        string? packageId,
        string? version)
    {
        var candidates = new List<LocalPackageCandidate>();
        LocalPackageSourceDirectoryListing rootEntries = List(root);
        foreach (LocalPackageSourceFile file in rootEntries.Files)
        {
            AddV2Candidate(
                candidates,
                file,
                packageId,
                root.Name,
                directoryDepth: 0);
        }

        foreach (LocalPackageSourceDirectory child
                 in rootEntries.Directories)
        {
            _operation.ThrowIfExpired();
            LocalPackageSourceDirectoryListing childEntries = List(child);
            foreach (LocalPackageSourceFile file in childEntries.Files)
            {
                AddV2Candidate(
                    candidates,
                    file,
                    packageId,
                    child.Name,
                    directoryDepth: 1);
            }

            if (!IsCanonicalPackageIdDirectory(child.Name, packageId))
                continue;

            foreach (LocalPackageSourceDirectory versionDirectory
                     in childEntries.Directories)
            {
                _operation.ThrowIfExpired();
                if (!IsCanonicalVersionDirectory(
                        versionDirectory.Name,
                        version))
                {
                    continue;
                }

                LocalPackageSourceDirectoryListing versionEntries =
                    List(versionDirectory);
                string expectedName =
                    $"{child.Name}.{versionDirectory.Name}.nupkg";
                foreach (LocalPackageSourceFile file in versionEntries.Files)
                {
                    if (file.Name.Equals(
                            expectedName,
                            StringComparison.Ordinal))
                    {
                        AddCandidate(
                            candidates,
                            new LocalPackageCandidate(
                                file,
                                LocalPackageLayout.V3,
                                child.Name,
                                versionDirectory.Name,
                                DirectoryDepth: 2));
                    }
                }
            }
        }

        candidates.Sort(
            static (left, right) =>
                StringComparer.Ordinal.Compare(
                    left.SortKey,
                    right.SortKey));
        return candidates;
    }

    private LocalPackageSourceDirectoryListing List(
        LocalPackageSourceDirectory directory)
    {
        int remaining = _ledger.RemainingDirectoryEntries;
        LocalPackageSourceDirectoryListing listing;
        try
        {
            listing = _host.List(directory, remaining, _operation);
        }
        catch
        {
            _operation.ThrowIfExpired();
            throw;
        }
        _operation.ThrowIfExpired();
        int count = checked(
            listing.Directories.Count + listing.Files.Count);
        _ledger.ChargeDirectoryEntries(count);
        if (listing.HasMoreEntries)
            throw new LocalPackageSourceLimitExceededException();

        LocalPackageSourceDirectory[] directories =
            listing.Directories.ToArray();
        LocalPackageSourceFile[] files = listing.Files.ToArray();
        ValidateAndSort(directories, directory => directory.Name);
        ValidateAndSort(files, file => file.Name);
        return new LocalPackageSourceDirectoryListing(
            directories,
            files,
            HasMoreEntries: false);
    }

    private void AddV2Candidate(
        List<LocalPackageCandidate> candidates,
        LocalPackageSourceFile file,
        string? packageId,
        string parentName,
        int directoryDepth)
    {
        if (!IsPackageArchive(file.Name)
            || packageId is not null
                && !file.Name.StartsWith(
                    packageId + ".",
                    StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        AddCandidate(
            candidates,
            new LocalPackageCandidate(
                file,
                LocalPackageLayout.V2,
                ParentName: parentName,
                VersionDirectoryName: null,
                DirectoryDepth: directoryDepth));
    }

    private void AddCandidate(
        List<LocalPackageCandidate> candidates,
        LocalPackageCandidate candidate)
    {
        _ledger.ChargeCandidate();
        candidates.Add(candidate);
    }

    private async Task<LocalPackageObservation> ReadCandidateAsync(
        LocalPackageCandidate candidate,
        bool transferStream)
    {
        _operation.ThrowIfExpired();
        LocalPackageSourceOpenFile opened;
        try
        {
            opened = _host.OpenRead(candidate.File, _operation);
        }
        catch
        {
            _operation.ThrowIfExpired();
            throw;
        }
        _operation.ThrowIfExpired();
        Stream content = opened.Content;
        bool ownershipTransferred = false;
        try
        {
            if (!content.CanRead || !content.CanSeek)
            {
                throw new IOException(
                    "The local package source returned an unusable archive stream.");
            }

            LocalPackageArchive archive;
            try
            {
                archive = await LocalPackageArchiveReader.ReadAsync(
                        content,
                        opened.Length,
                        _options,
                        _ledger,
                        _operation)
                    .ConfigureAwait(false);
            }
            catch
            {
                _operation.ThrowIfExpired();
                throw;
            }
            ValidateLayout(candidate, archive.Coordinate);
            if (candidate.File.ObservedLength is long observedLength
                && observedLength != opened.Length)
            {
                throw new IOException(
                    "The local package archive changed after it was observed.");
            }

            if (!Equals(
                    candidate.File.StabilityEvidence,
                    opened.StabilityEvidence)
                && candidate.File.StabilityEvidence is not null)
            {
                throw new IOException(
                    "The local package archive changed after it was observed.");
            }

            _operation.ThrowIfExpired();
            if (transferStream)
            {
                content.Position = 0;
                _operation.ThrowIfExpired();
                ownershipTransferred = true;
                return new LocalPackageObservation(
                    candidate,
                    archive,
                    content,
                    opened.Length);
            }

            return new LocalPackageObservation(
                candidate,
                archive,
                Content: null,
                opened.Length);
        }
        finally
        {
            if (!ownershipTransferred)
            {
                await LocalPackageSourceCleanup.DisposeAsync(
                    content,
                    _operation).ConfigureAwait(false);
            }
        }
    }

    private static bool CandidateMatchesRequest(
        PackageSourceCoordinate coordinate,
        string? packageId,
        string? version) =>
        (packageId is null
            || coordinate.PackageId.Equals(
                packageId,
                StringComparison.OrdinalIgnoreCase))
        && (version is null
            || coordinate.Version.Equals(
                version,
                StringComparison.Ordinal));

    private static void ValidateLayout(
        LocalPackageCandidate candidate,
        PackageSourceCoordinate coordinate)
    {
        if (candidate.Layout == LocalPackageLayout.V3)
        {
            string expected =
                $"{coordinate.PackageId}.{coordinate.Version}.nupkg";
            if (!candidate.ParentName!.Equals(
                    coordinate.PackageId,
                    StringComparison.Ordinal)
                || !candidate.VersionDirectoryName!.Equals(
                    coordinate.Version,
                    StringComparison.Ordinal)
                || !candidate.File.Name.Equals(
                    expected,
                    StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    "The package archive path disagrees with its embedded coordinate.");
            }

            return;
        }

        string prefix = coordinate.PackageId + ".";
        if (!candidate.File.Name.StartsWith(
                prefix,
                StringComparison.OrdinalIgnoreCase)
            || !candidate.File.Name.EndsWith(
                ".nupkg",
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "The package archive name disagrees with its embedded coordinate.");
        }

        if (candidate.File.Name.Length
            <= prefix.Length + ".nupkg".Length)
        {
            throw new InvalidDataException(
                "The package archive name has no package version.");
        }

        string version = candidate.File.Name[
            prefix.Length..
            ^".nupkg".Length];
        if (!NuGetVersion.TryParse(version, out NuGetVersion? parsed)
            || parsed.ToNormalizedString().ToLowerInvariant()
                != coordinate.Version)
        {
            throw new InvalidDataException(
                "The package archive name disagrees with its embedded coordinate.");
        }
    }

    private static bool IsPackageArchive(string name) =>
        name.EndsWith(".nupkg", StringComparison.OrdinalIgnoreCase)
        && !name.EndsWith(
            ".symbols.nupkg",
            StringComparison.OrdinalIgnoreCase);

    private static bool IsCanonicalPackageIdDirectory(
        string name,
        string? requestedPackageId) =>
        PackageCoordinateValidation.IsValidPackageId(name)
        && name.Equals(name.ToLowerInvariant(), StringComparison.Ordinal)
        && (requestedPackageId is null
            || name.Equals(
                requestedPackageId,
                StringComparison.Ordinal));

    private static bool IsCanonicalVersionDirectory(
        string name,
        string? requestedVersion) =>
        NuGetVersion.TryParse(name, out NuGetVersion? parsed)
        && name.Equals(
            parsed.ToNormalizedString().ToLowerInvariant(),
            StringComparison.Ordinal)
        && (requestedVersion is null
            || name.Equals(requestedVersion, StringComparison.Ordinal));

    private static void ValidateAndSort<T>(
        T[] entries,
        Func<T, string> getName)
    {
        foreach (T entry in entries)
        {
            string name = getName(entry);
            if (string.IsNullOrEmpty(name)
                || name is "." or ".."
                || name.Contains(Path.DirectorySeparatorChar)
                || Path.AltDirectorySeparatorChar
                    != Path.DirectorySeparatorChar
                    && name.Contains(Path.AltDirectorySeparatorChar))
            {
                throw new IOException(
                    "The local package source returned an invalid directory entry.");
            }
        }

        Array.Sort(
            entries,
            (left, right) => StringComparer.Ordinal.Compare(
                getName(left),
                getName(right)));
    }
}

internal sealed class LocalPackageSourceLedger
{
    private readonly LocalPackageSourceOptions _options;
    private int _directoryEntries;
    private int _candidateArchives;
    private long _manifestBytes;

    public LocalPackageSourceLedger(LocalPackageSourceOptions options)
    {
        _options = options;
    }

    public int RemainingDirectoryEntries =>
        _options.MaxDirectoryEntries - _directoryEntries;

    public long RemainingManifestBytes =>
        _options.MaxAggregateManifestBytes - _manifestBytes;

    public void ChargeDirectoryEntries(int count)
    {
        _directoryEntries = checked(_directoryEntries + count);
        if (_directoryEntries > _options.MaxDirectoryEntries)
            throw new LocalPackageSourceLimitExceededException();
    }

    public void ChargeCandidate()
    {
        _candidateArchives = checked(_candidateArchives + 1);
        if (_candidateArchives > _options.MaxCandidateArchives)
            throw new LocalPackageSourceLimitExceededException();
    }

    public void ChargeManifestBytes(long count)
    {
        _manifestBytes = checked(_manifestBytes + count);
        if (_manifestBytes > _options.MaxAggregateManifestBytes)
            throw new LocalPackageSourceLimitExceededException();
    }
}

internal static class LocalPackageSourceCleanup
{
    public static async ValueTask DisposeAsync(
        Stream content,
        NuGetOperationDeadline operation)
    {
        try
        {
            await content.DisposeAsync().ConfigureAwait(false);
            operation.ThrowIfExpired();
        }
        catch
        {
            operation.ThrowIfExpired();
            throw;
        }
    }
}

internal sealed record LocalPackageCandidate(
    LocalPackageSourceFile File,
    LocalPackageLayout Layout,
    string? ParentName,
    string? VersionDirectoryName,
    int DirectoryDepth)
{
    public string SortKey =>
        $"{DirectoryDepth}:{(int)Layout}:{ParentName}:"
        + $"{VersionDirectoryName}:{File.Name}";
}

internal sealed record LocalPackageObservation(
    LocalPackageCandidate Candidate,
    LocalPackageArchive Archive,
    Stream? Content,
    long Length);

internal enum LocalPackageLayout
{
    V2,
    V3,
}

internal sealed class LocalPackageSourceLimitExceededException
    : IOException
{
    public LocalPackageSourceLimitExceededException()
        : base(
            "The local package source exceeded a configured safety bound.")
    {
    }
}

internal sealed class LocalPackageSourceNotFoundException
    : IOException
{
    public LocalPackageSourceNotFoundException()
        : base(
            "The requested package coordinate was not found in the local source.")
    {
    }
}

internal sealed class LocalPackagePayloadStream : Stream
{
    private readonly Stream _inner;
    private readonly NuGetOperationDeadline _operation;
    private readonly PackageSourceResultIdentity _source;
    private int _disposed;
    private int _endOfStream;

    public LocalPackagePayloadStream(
        Stream inner,
        NuGetOperationDeadline operation,
        PackageSourceResultIdentity source)
    {
        _inner = inner;
        _operation = operation;
        _source = source;
    }

    public override bool CanRead => _inner.CanRead;
    public override bool CanSeek => _inner.CanSeek;
    public override bool CanWrite => false;
    public override long Length => Execute(() => _inner.Length);
    public override long Position
    {
        get => Execute(() => _inner.Position);
        set => ResumeAfterEndOfStream(() =>
        {
            _inner.Position = value;
            return _inner.Position;
        });
    }

    public override int Read(byte[] buffer, int offset, int count) =>
        ReadCore(
            () => _inner.Read(buffer, offset, count),
            zeroIsEndOfStream: count > 0);

    public override int Read(Span<byte> buffer)
    {
        ThrowIfUnavailable();
        int read;
        try
        {
            read = _inner.Read(buffer);
        }
        catch (Exception exception) when (IsStreamFailure(exception))
        {
            throw TranslateReadFailure(exception);
        }

        ThrowIfDisposedDuringRead();
        ThrowIfDeadlineUnavailable();
        if (read == 0 && !buffer.IsEmpty)
            ObserveEndOfStream();

        return read;
    }

    public override async ValueTask<int> ReadAsync(
        Memory<byte> buffer,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfDeadlineUnavailable();
        int read;
        try
        {
            using CancellationTokenSource? linked = IsAtEndOfStream
                ? null
                : CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken,
                    _operation.OperationToken);
            read = await _inner.ReadAsync(
                buffer,
                linked?.Token ?? cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (IsStreamFailure(exception))
        {
            if (IsDisposed)
                throw StreamFailure(exception, cleanupFailed: false);
            if (exception is OperationCanceledException
                && cancellationToken.IsCancellationRequested)
            {
                throw new OperationCanceledException(cancellationToken);
            }

            throw Translate(exception);
        }

        ThrowIfDisposedDuringRead();
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfDeadlineUnavailable();
        if (read == 0 && !buffer.IsEmpty)
            ObserveEndOfStream();

        return read;
    }

    public override Task<int> ReadAsync(
        byte[] buffer,
        int offset,
        int count,
        CancellationToken cancellationToken) =>
        ReadAsync(
            buffer.AsMemory(offset, count),
            cancellationToken).AsTask();

    public override int ReadByte() =>
        ReadCore(
            () => _inner.ReadByte(),
            zeroIsEndOfStream: false);

    public override long Seek(long offset, SeekOrigin origin) =>
        ResumeAfterEndOfStream(() => _inner.Seek(offset, origin));

    public override void Flush()
    {
    }

    public override void SetLength(long value) =>
        throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) =>
        throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing && Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            Exception? failure = null;
            Exception? deadlineFailure = null;
            try
            {
                _inner.Dispose();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
            try
            {
                ThrowIfDeadlineExpired();
            }
            catch (Exception exception)
            {
                deadlineFailure = exception;
            }

            _operation.Dispose();

            if (deadlineFailure is OperationCanceledException cancellation)
                throw cancellation;
            if (deadlineFailure is NuGetOperationTimeoutException timeout)
            {
                throw TimeoutFailure(
                    timeout,
                    cleanupFailed: failure is not null);
            }
            if (failure is not null)
                throw StreamFailure(failure, cleanupFailed: true);
        }

        base.Dispose(disposing);
    }

    public override async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            Exception? failure = null;
            Exception? deadlineFailure = null;
            try
            {
                await _inner.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                failure = exception;
            }
            try
            {
                ThrowIfDeadlineExpired();
            }
            catch (Exception exception)
            {
                deadlineFailure = exception;
            }

            _operation.Dispose();

            GC.SuppressFinalize(this);
            if (deadlineFailure is OperationCanceledException cancellation)
                throw cancellation;
            if (deadlineFailure is NuGetOperationTimeoutException timeout)
            {
                throw TimeoutFailure(
                    timeout,
                    cleanupFailed: failure is not null);
            }
            if (failure is not null)
                throw StreamFailure(failure, cleanupFailed: true);
        }
    }

    private T Execute<T>(Func<T> action)
    {
        ThrowIfUnavailable();
        try
        {
            T result = action();
            ThrowIfDeadlineExpired();
            return result;
        }
        catch (Exception exception) when (IsStreamFailure(exception))
        {
            throw Translate(exception);
        }
    }

    private int ReadCore(
        Func<int> action,
        bool zeroIsEndOfStream)
    {
        ThrowIfUnavailable();
        int result;
        try
        {
            result = action();
        }
        catch (Exception exception) when (IsStreamFailure(exception))
        {
            throw TranslateReadFailure(exception);
        }

        ThrowIfDisposedDuringRead();
        ThrowIfDeadlineUnavailable();
        if (zeroIsEndOfStream ? result == 0 : result < 0)
            ObserveEndOfStream();

        return result;
    }

    private long ResumeAfterEndOfStream(Func<long> action)
    {
        ThrowIfDisposed();
        if (!IsAtEndOfStream)
            ThrowIfDeadlineUnavailable();

        long position;
        bool resumed;
        try
        {
            position = action();
            resumed = position < _inner.Length;
        }
        catch (Exception exception) when (IsStreamFailure(exception))
        {
            throw TranslateReadFailure(exception);
        }

        if (resumed)
        {
            Volatile.Write(ref _endOfStream, 0);
            ThrowIfDeadlineUnavailable();
        }

        return position;
    }

    private void ThrowIfUnavailable()
    {
        ThrowIfDisposed();
        ThrowIfDeadlineUnavailable();
    }

    private void ThrowIfDeadlineUnavailable()
    {
        try
        {
            ThrowIfDeadlineExpired();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw Translate(exception);
        }
    }

    private void ThrowIfDeadlineExpired()
    {
        if (IsAtEndOfStream)
            return;

        _operation.ThrowIfExpired();
    }

    private bool IsAtEndOfStream =>
        Volatile.Read(ref _endOfStream) != 0;

    private void ObserveEndOfStream()
    {
        Volatile.Write(ref _endOfStream, 1);
    }

    private void ThrowIfDisposedDuringRead()
    {
        if (IsDisposed)
        {
            throw StreamFailure(
                new ObjectDisposedException(
                    nameof(LocalPackagePayloadStream)),
                cleanupFailed: false);
        }
    }

    private Exception TranslateReadFailure(Exception exception) =>
        IsDisposed
            ? StreamFailure(exception, cleanupFailed: false)
            : Translate(exception);

    private bool IsDisposed =>
        Volatile.Read(ref _disposed) != 0;

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
    }

    private Exception Translate(Exception exception)
    {
        if (IsDisposed)
            return StreamFailure(exception, cleanupFailed: false);

        try
        {
            ThrowIfDeadlineExpired();
        }
        catch (OperationCanceledException callerCancellation)
        {
            return callerCancellation;
        }
        catch (NuGetOperationTimeoutException operationTimeout)
        {
            return TimeoutFailure(operationTimeout);
        }

        return exception is NuGetOperationTimeoutException timeout
            ? TimeoutFailure(timeout)
            : StreamFailure(exception, cleanupFailed: false);
    }

    private PackageSourceStreamException TimeoutFailure(
        NuGetOperationTimeoutException timeout,
        bool cleanupFailed = false) =>
        new(
            _source,
            PackageSourceFailureKind.Timeout,
            new PackageSourceTimeout(
                PackageSourceTimeoutKind.Operation,
                timeout.Timeout),
            cleanupFailed);

    private PackageSourceStreamException StreamFailure(
        Exception exception,
        bool cleanupFailed) =>
        new(
            _source,
            NuGetTransportFailure.IsTimeout(exception)
                ? PackageSourceFailureKind.Timeout
                : PackageSourceFailureKind.Transport,
            timeout: null,
            cleanupFailed);

    private static bool IsStreamFailure(Exception exception) =>
        exception is IOException
            or InvalidDataException
            or ObjectDisposedException
            or OperationCanceledException
            or TimeoutException;
}
