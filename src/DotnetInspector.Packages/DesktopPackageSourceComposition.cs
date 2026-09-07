using System.Collections.ObjectModel;
using System.Runtime.ExceptionServices;
using DotnetInspector.Core;
using InertText;
using NuGet.Versioning;
using NuGetFetch;
using NuGetFetch.Plugins;

namespace DotnetInspector.Packages;

/// <summary>The completeness of one package version discovery operation.</summary>
public enum PackageVersionDiscoveryState
{
    Authoritative,
    Partial,
    Failed,
}

/// <summary>Why one configured package authority did not provide evidence.</summary>
public enum PackageAuthorityFailureKind
{
    Configuration,
    Unsupported,
    IncompleteMetadata,
    AuthenticationRequired,
    Timeout,
    InvalidResponse,
    ResponseRejected,
    Transport,
    Input,
}

/// <summary>A credential-safe failure attributed to one configured authority.</summary>
public sealed record PackageAuthorityFailure(
    InertString Authority,
    PackageAuthorityFailureKind Kind,
    string Message)
{
    public PackageSourceFailure? SourceFailure { get; init; }
    public PackageSourceResultIdentity? ResultSource { get; init; }
    public PackageSourceTimeout? Timeout { get; init; }
}

internal sealed class ConfiguredPackageCandidateObservation
{
    internal ConfiguredPackageCandidateObservation(
        ConfiguredPackageAuthority authority,
        PackageCandidateObservation observation)
    {
        ArgumentNullException.ThrowIfNull(authority);
        ArgumentNullException.ThrowIfNull(observation);
        if (!ReferenceEquals(
                observation.Source.Association,
                authority.Association))
        {
            throw new InvalidOperationException(
                "The package candidate observation belongs to another configured authority.");
        }

        Authority = authority;
        Observation = observation;
    }

    public ConfiguredPackageAuthority Authority { get; }
    public PackageCandidateObservation Observation { get; }
}

/// <summary>
/// The package-owned aggregate of version evidence from every eligible
/// configured authority.
/// </summary>
public sealed class PackageVersionDiscoveryResult
{
    internal PackageVersionDiscoveryResult(
        PackageVersionDiscoveryState state,
        IReadOnlyList<PackageVersionSourceInfo> sourceListings,
        IReadOnlyList<PackageAuthorityFailure> failures,
        bool hasAnyCandidate,
        IReadOnlyList<ConfiguredPackageCandidateObservation>? candidates = null,
        PackageVersionDiscoveryContract? contract = null,
        object? candidateIssuer = null)
    {
        State = state;
        SourceListings = new ReadOnlyCollection<PackageVersionSourceInfo>([.. sourceListings]);
        Listings = new ReadOnlyCollection<PackageVersionInfo>(
            [.. sourceListings
                .GroupBy(row => row.Version, StringComparer.OrdinalIgnoreCase)
                .Select(group => new PackageVersionInfo(group.Key, group.Any(row => row.Listed)))]);
        Versions = new ReadOnlyCollection<string>([.. Listings.Select(row => row.Version)]);
        Failures =
            new ReadOnlyCollection<PackageAuthorityFailure>([.. failures]);
        HasAnyCandidate = hasAnyCandidate;
        Candidates = new ReadOnlyCollection<ConfiguredPackageCandidateObservation>(
            candidates is null ? [] : [.. candidates]);
        Contract = contract ?? PackageVersionDiscoveryContract.Unspecified;
        CandidateIssuer = candidateIssuer ?? new object();
    }

    public PackageVersionDiscoveryState State { get; }
    public IReadOnlyList<string> Versions { get; }
    public IReadOnlyList<PackageVersionInfo> Listings { get; }

    /// <summary>Per-authority display rows, not payload authorization receipts.</summary>
    public IReadOnlyList<PackageVersionSourceInfo> SourceListings { get; }
    public IReadOnlyList<PackageAuthorityFailure> Failures { get; }
    public bool HasAnyCandidate { get; }
    public int CandidateObservationCount => Candidates.Count;
    public PackageVersionDiscoveryContract Contract { get; }
    internal IReadOnlyList<ConfiguredPackageCandidateObservation> Candidates { get; }
    private object CandidateIssuer { get; }
    internal bool HasCandidateIssuer(object issuer) =>
        ReferenceEquals(CandidateIssuer, issuer);

    /// <summary>
    /// Issues the exact acquisition candidate for one version retained by an
    /// authoritative discovery result.
    /// </summary>
    public PackageAcquisitionCandidate SelectCandidate(string version)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(version);
        if (State != PackageVersionDiscoveryState.Authoritative)
        {
            throw new InvalidOperationException(
                "Only authoritative version discovery can issue an acquisition candidate.");
        }

        string normalized = PackageSourceCoordinate.Create(
            Candidates.FirstOrDefault()?.Observation.Coordinate.PackageId
                ?? throw new InvalidOperationException(
                    "The discovery result contains no candidate observations."),
            version).Version;
        var reporters = new List<ConfiguredPackageCandidateObservation>();
        var seen = new HashSet<ConfiguredPackageAuthority>(
            ReferenceEqualityComparer.Instance);
        PackageSourceCoordinate? coordinate = null;
        foreach (ConfiguredPackageCandidateObservation candidate in Candidates)
        {
            if (!candidate.Observation.Coordinate.Version.Equals(
                    normalized,
                    StringComparison.OrdinalIgnoreCase)
                || !seen.Add(candidate.Authority))
            {
                continue;
            }

            coordinate ??= candidate.Observation.Coordinate;
            reporters.Add(candidate);
        }

        if (coordinate is null)
        {
            throw new ArgumentException(
                "The selected version is not present in the discovery result.",
                nameof(version));
        }

        return PackageAcquisitionCandidate.CreateDiscovered(
            CandidateIssuer,
            coordinate,
            Contract,
            reporters);
    }
}

/// <summary>
/// Owns source associations, HTTP and local routes, and HTTP authentication
/// contexts for one desktop package-composition lifetime.
/// </summary>
public sealed partial class DesktopPackageSourceComposition : IAsyncDisposable
{
    internal delegate HttpMessageHandler SourceTransportFactory(
        PackageSource source,
        bool isGallery);

    private readonly NuGetFetchOptions _options;
    private readonly ICredentialSource _credentialSource;
    private readonly IAsyncDisposable? _ownedCredentialSource;
    private readonly SourceTransportFactory _createTransport;
    private readonly Dictionary<ConfiguredPackageAuthorityKey, AuthorityEntry>
        _authorities = [];
    private readonly Dictionary<PackageSourceAssociation, AuthorityEntry>
        _authoritiesByAssociation =
            new(ReferenceEqualityComparer.Instance);
    private readonly object _candidateIssuer = new();
    private int _disposed;

    /// <summary>
    /// Creates a desktop composition using the installed NuGet credential
    /// providers and the CLI's configured request deadline.
    /// </summary>
    public DesktopPackageSourceComposition(TimeSpan requestTimeout)
    {
        _options = NuGetFetchOptions.FromRequestTimeout(requestTimeout);
        var provider = new PluginCredentialProvider();
        _credentialSource = provider;
        _ownedCredentialSource = provider;
        _createTransport = CreateProductionTransport;
    }

    internal DesktopPackageSourceComposition(
        TimeSpan requestTimeout,
        ICredentialSource credentialSource,
        SourceTransportFactory createTransport)
    {
        ArgumentNullException.ThrowIfNull(credentialSource);
        ArgumentNullException.ThrowIfNull(createTransport);
        _options = NuGetFetchOptions.FromRequestTimeout(requestTimeout);
        _credentialSource = credentialSource;
        _createTransport = createTransport;
    }

    internal NuGetOperationContext CreateOperationContext(CancellationToken cancellationToken = default) =>
        new(_options.RequestTimeout, _options.OperationTimeout, cancellationToken);

    /// <summary>
    /// Enumerates versions from every configured authority eligible for one
    /// package ID and adopts their results through exact association lookup.
    /// A supplied operation context remains caller-owned.
    /// </summary>
    public async Task<PackageVersionDiscoveryResult> GetVersionsAsync(
        string packageId,
        bool includePrerelease,
        int? limit,
        NuGetSourceOptions? sourceOptions = null,
        Action<string>? log = null,
        CancellationToken cancellationToken = default,
        bool includeUnlisted = false,
        NuGetOperationContext? operationContext = null)
    {
        PackageVersionDiscoveryContract contract =
            PackageVersionDiscoveryContract.Create(
                includePrerelease,
                includeUnlisted,
                limit);
        ObjectDisposedException.ThrowIf(
            Volatile.Read(ref _disposed) != 0,
            this);
        if (!PackageExtractor.IsValidPackageId(packageId))
        {
            return Failed(
                new PackageAuthorityFailure(
                    InertString.Empty,
                    PackageAuthorityFailureKind.Input,
                    "The package ID must use the NuGet package ID grammar."),
                contract);
        }

        if (limit <= 0)
        {
            return Failed(
                new PackageAuthorityFailure(
                    InertString.Empty,
                    PackageAuthorityFailureKind.Input,
                    "The package version limit must be greater than zero."),
                contract);
        }

        var failures = new List<PackageAuthorityFailure>();
        IReadOnlyList<PackageSource> sources = ResolveEligibleSources(
            packageId, sourceOptions, failures);
        if (sources.Count == 0)
        {
            return new PackageVersionDiscoveryResult(
                PackageVersionDiscoveryState.Failed,
                [],
                failures,
                hasAnyCandidate: false,
                contract: contract,
                candidateIssuer: _candidateIssuer);
        }

        using NuGetOperationContext? ownedOperation = operationContext is null
            ? CreateOperationContext(cancellationToken)
            : null;
        NuGetOperationContext operation = operationContext ?? ownedOperation!;
        cancellationToken = operation.ResolveInvocationToken(cancellationToken);
        IReadOnlyList<InertString> feedLabels = PackageSourceDisplay.ForVersionListings(sources);
        var versions = new Dictionary<string, List<PackageVersionSourceInfo>>(StringComparer.OrdinalIgnoreCase);
        var candidates = new List<ConfiguredPackageCandidateObservation>();
        bool hasAnyCandidate = false;
        bool operationTimedOut = false;

        for (int sourceIndex = 0;
             sourceIndex < sources.Count;
             sourceIndex++)
        {
            PackageSource source = sources[sourceIndex];
            try
            {
                operation.ThrowIfExpired();
            }
            catch (NuGetOperationTimeoutException)
            {
                operationTimedOut = true;
                AddOperationTimeoutFailure(failures, operation);
                for (int remainingIndex = sourceIndex;
                     remainingIndex < sources.Count;
                     remainingIndex++)
                {
                    PackageSource remaining =
                        sources[remainingIndex];
                    failures.Add(new PackageAuthorityFailure(
                        PackageSourceDisplay.ForDiagnostics(remaining),
                        PackageAuthorityFailureKind.Timeout,
                        $"Package source {PackageSourceDisplay.ForDiagnostics(remaining)} was not consulted before the package operation deadline."));
                }
                break;
            }

            AuthorityEntry? authority = TryGetEligibleAuthority(source, failures);
            if (authority is null)
                continue;

            log?.Invoke(
                $"Fetching versions from {PackageSourceDisplay.ForDiagnostics(source)}.");
            PackageSourceOperationResult<PackageVersionResult> outcome =
                await authority.Client.GetVersionsAsync(
                    packageId,
                    cancellationToken,
                    operation).ConfigureAwait(false);

            if (outcome.Failure is { } failure)
            {
                RequireAuthority(failure.Source, authority);
                failures.Add(DescribeFailure(source, failure));
            }
            else
            {
                PackageVersionResult value =
                    outcome.Value
                    ?? throw new InvalidOperationException(
                        "The package source version operation returned neither a value nor a failure.");
                RequireAuthority(value.Source, authority);
                if (value.Source.TransportKind
                        == PackageSourceKind.NuGetGallery
                    && !value.HasAuthoritativeListingState)
                {
                    failures.Add(new PackageAuthorityFailure(
                        PackageSourceDisplay.ForDiagnostics(source),
                        PackageAuthorityFailureKind.IncompleteMetadata,
                        $"Package source {PackageSourceDisplay.ForDiagnostics(source)} did not provide authoritative version listing state."));
                }

                foreach (PackageCandidateObservation candidate in
                         value.Candidates)
                {
                    if (!ReferenceEquals(candidate.Source, value.Source)
                        || !candidate.Coordinate.PackageId.Equals(
                            packageId,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidOperationException(
                            "The package source returned version evidence for another authority or package.");
                    }

                    hasAnyCandidate = true;
                    NuGetVersion parsed =
                        NuGetVersion.Parse(candidate.Coordinate.Version);
                    bool listed = !value.HasAuthoritativeListingState
                        || candidate.ListingState
                            != PackageListingState.Unlisted;
                    if ((listed || includeUnlisted)
                        && (includePrerelease || !parsed.IsPrerelease))
                    {
                        candidates.Add(new(authority.Authority, candidate));
                        string version = candidate.Coordinate.Version;
                        if (!versions.TryGetValue(version, out var rows))
                        {
                            rows = [];
                            versions.Add(version, rows);
                        }
                        rows.Add(new PackageVersionSourceInfo(
                            version, feedLabels[sourceIndex].ToString(), listed));
                    }
                }
            }

            try
            {
                operation.ThrowIfExpired();
            }
            catch (NuGetOperationTimeoutException)
            {
                operationTimedOut = true;
                AddOperationTimeoutFailure(failures, operation);
                for (int remainingIndex = sourceIndex + 1;
                     remainingIndex < sources.Count;
                     remainingIndex++)
                {
                    PackageSource remaining =
                        sources[remainingIndex];
                    failures.Add(new PackageAuthorityFailure(
                        PackageSourceDisplay.ForDiagnostics(remaining),
                        PackageAuthorityFailureKind.Timeout,
                        $"Package source {PackageSourceDisplay.ForDiagnostics(remaining)} was not consulted before the package operation deadline."));
                }
                break;
            }
        }

        List<PackageVersionSourceInfo> ordered =
        [
            .. versions.Keys
                .Select(version => (
                    Parsed: NuGetVersion.Parse(version),
                    Original: version))
                .OrderByDescending(candidate => candidate.Parsed)
                .Take(limit ?? int.MaxValue)
                .SelectMany(candidate => versions[candidate.Original]),
        ];
        HashSet<string> retainedVersions =
            ordered.Select(row => row.Version)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        List<ConfiguredPackageCandidateObservation> retainedCandidates =
        [
            .. candidates.Where(candidate =>
                retainedVersions.Contains(
                    candidate.Observation.Coordinate.Version)),
        ];
        try
        {
            operation.ThrowIfExpired();
        }
        catch (NuGetOperationTimeoutException)
        {
            operationTimedOut = true;
            AddOperationTimeoutFailure(failures, operation);
        }

        PackageVersionDiscoveryState state = operationTimedOut
            ? PackageVersionDiscoveryState.Failed
            : failures.Count switch
        {
            0 => PackageVersionDiscoveryState.Authoritative,
            _ when ordered.Count > 0 =>
                PackageVersionDiscoveryState.Partial,
            _ => PackageVersionDiscoveryState.Failed,
        };
        return new PackageVersionDiscoveryResult(
            state,
            ordered,
            failures,
            hasAnyCandidate,
            retainedCandidates,
            contract,
            _candidateIssuer);
    }

    /// <summary>
    /// Enumerates the complete listed candidate set required for one NuGet
    /// dependency version constraint.
    /// </summary>
    public Task<PackageVersionDiscoveryResult> GetDependencyVersionsAsync(
        string packageId,
        NuGetSourceOptions? sourceOptions = null,
        Action<string>? log = null,
        CancellationToken cancellationToken = default,
        NuGetOperationContext? operationContext = null) =>
        GetVersionsAsync(
            packageId,
            includePrerelease: true,
            limit: null,
            sourceOptions,
            log,
            cancellationToken,
            includeUnlisted: false,
            operationContext);

    private static IReadOnlyList<PackageSource> ResolveEligibleSources(
        string packageId,
        NuGetSourceOptions? sourceOptions,
        List<PackageAuthorityFailure> failures)
    {
        if (sourceOptions?.ConfigFile is { } configFile
            && NuGetSourceResolver.DescribeConfigProblem(configFile)
                is string configProblem)
        {
            failures.Add(new PackageAuthorityFailure(
                InertString.Empty,
                PackageAuthorityFailureKind.Configuration,
                configProblem));
            return [];
        }

        PackageSourceResolution resolution;
        try
        {
            resolution =
                NuGetSourceResolver.ResolveSourcesForPackageWithFailures(
                    sourceOptions,
                    packageId);
        }
        catch (PackageSourceMappingException exception)
        {
            failures.Add(new PackageAuthorityFailure(
                InertString.Empty,
                PackageAuthorityFailureKind.Configuration,
                exception.Message));
            return [];
        }
        catch (InvalidDataException)
        {
            failures.Add(new PackageAuthorityFailure(
                InertString.Empty,
                PackageAuthorityFailureKind.Configuration,
                "The NuGet package source mapping configuration is malformed, so no source can be authorized."));
            return [];
        }

        IReadOnlyList<PackageSource> sources =
            NuGetSourceResolver.ResolveAuthorizedSources(
                sourceOptions,
                resolution.Sources);
        failures.AddRange(resolution.Failures
            .Select(failure => new PackageAuthorityFailure(
                failure.Authority,
                PackageAuthorityFailureKind.Configuration,
                failure.Message)));
        if (sources.Count == 0 && failures.Count == 0)
        {
            failures.Add(new PackageAuthorityFailure(
                InertString.Empty,
                PackageAuthorityFailureKind.Configuration,
                $"No configured package source is authorized for '{packageId}'."));
        }

        return sources;
    }

    private AuthorityEntry? TryGetEligibleAuthority(
        PackageSource source,
        List<PackageAuthorityFailure> failures)
    {
        if (!ConfiguredPackageAuthorityKey.TryCreate(
                source,
                out ConfiguredPackageAuthorityKey? authorityKey,
                out string? authorityProblem))
        {
            InertString sourceDisplay =
                PackageSourceDisplay.ForDiagnostics(source);
            failures.Add(new PackageAuthorityFailure(
                sourceDisplay,
                PackageAuthorityFailureKind.Configuration,
                $"Package source {sourceDisplay} is unusable. {authorityProblem}"));
            return null;
        }

        bool isGallery =
            authorityKey.IsNuGetOrg && source.Credential is null;
        if (authorityKey.HttpEndpoint is { } endpoint
            && !isGallery
            && source.Credential is null
            && !PluginAuthenticationContext.CanScopeProviderQuery(endpoint))
        {
            InertString sourceDisplay =
                PackageSourceDisplay.ForDiagnostics(source);
            failures.Add(new PackageAuthorityFailure(
                sourceDisplay,
                PackageAuthorityFailureKind.Configuration,
                $"Package source {sourceDisplay} cannot be used with credential-provider authentication because its service-index scope is unusable."));
            return null;
        }

        return GetOrCreateAuthority(source, authorityKey, isGallery);
    }

    /// <summary>
    /// Acquires one exact manifest through the desktop transport and authentication policy
    /// while preserving the configured authority's owner-issued association.
    /// </summary>
    public async Task<PackageSourceOperationResult<PackageSourceManifest>>
        GetManifestAsync(
            ConfiguredPackageAuthority authority,
            PackageSourceCoordinate coordinate,
            CancellationToken cancellationToken = default,
            NuGetOperationContext? operationContext = null)
    {
        ObjectDisposedException.ThrowIf(
            Volatile.Read(ref _disposed) != 0,
            this);
        ArgumentNullException.ThrowIfNull(authority);
        ArgumentNullException.ThrowIfNull(coordinate);

        bool isGallery =
            authority.Key.IsNuGetOrg
            && authority.Source.Credential is null;
        using AuthorityEntry runtime = CreateAuthorityEntry(
            authority,
            isGallery);
        PackageSourceOperationResult<PackageSourceManifest> outcome =
            await runtime.Client.GetManifestAsync(
                coordinate.PackageId,
                coordinate.Version,
                cancellationToken,
                operationContext).ConfigureAwait(false);

        if (outcome.Failure is { } failure)
        {
            RequireAuthorityIdentity(failure.Source, runtime);
        }
        else
        {
            PackageSourceManifest value =
                outcome.Value
                ?? throw new InvalidOperationException(
                    "The package source manifest operation returned neither a value nor a failure.");
            RequireAuthorityIdentity(value.Source, runtime);
        }

        return outcome;
    }

    private AuthorityEntry GetOrCreateAuthority(
        PackageSource source,
        ConfiguredPackageAuthorityKey key,
        bool isGallery)
    {
        if (_authorities.TryGetValue(key, out AuthorityEntry? existing))
        {
            if (existing.Source.Credential == source.Credential)
                return existing;

            _authorities.Remove(key);
            _authoritiesByAssociation.Remove(existing.Association);
            existing.Dispose();
        }

        var configuredAuthority = new ConfiguredPackageAuthority(source);
        PackageSourceAssociation association = configuredAuthority.Association;
        AuthorityEntry authority = CreateAuthorityEntry(
            configuredAuthority,
            isGallery);
        _authorities.Add(key, authority);
        _authoritiesByAssociation.Add(association, authority);
        return authority;
    }

    private AuthorityEntry CreateAuthorityEntry(
        ConfiguredPackageAuthority configuredAuthority,
        bool isGallery)
    {
        PackageSource source = configuredAuthority.Source;
        ConfiguredPackageAuthorityKey key = configuredAuthority.Key;
        PackageSourceAssociation association = configuredAuthority.Association;
        PluginAuthenticationContextOwner? owner = null;
        HttpMessageHandler? transport = null;
        IPackageSourceClient? client = null;
        try
        {
            if (key.LocalIdentity is { } local)
            {
                client = PackageSourceClientFactory.Create(local, association);
            }
            else
            {
                transport = _createTransport(source, isGallery);
                if (isGallery)
                {
                    client = PackageSourceClientFactory.CreateGallery(
                        association,
                        transport,
                        _options);
                }
                else
                {
                    if (source.Credential is null
                        && PluginAuthenticationContext.CanScopeProviderQuery(
                            key.HttpEndpoint!))
                    {
                        owner = PluginAuthenticationContextOwner.Create(
                            association,
                            key.HttpEndpoint!,
                            _credentialSource);
                    }
                    client = PackageSourceClientFactory.Create(
                        source,
                        association,
                        transport,
                        _options,
                        owner?.Context);
                }
            }

            return new AuthorityEntry(
                configuredAuthority,
                owner,
                client);
        }
        catch (Exception creationFailure)
        {
            var failures = new List<Exception> { creationFailure };
            try
            {
                if (client is not null)
                    client.Dispose();
                else
                    transport?.Dispose();
            }
            catch (Exception disposalFailure)
            {
                failures.Add(disposalFailure);
            }

            try
            {
                owner?.Dispose();
            }
            catch (Exception disposalFailure)
            {
                failures.Add(disposalFailure);
            }

            if (failures.Count > 1)
                throw new AggregateException(failures);

            ExceptionDispatchInfo.Capture(creationFailure).Throw();
            throw;
        }
    }

    private void RequireAuthority(
        PackageSourceResultIdentity resultSource,
        AuthorityEntry expected)
    {
        if (!_authoritiesByAssociation.TryGetValue(
                resultSource.Association,
                out AuthorityEntry? authority)
            || !ReferenceEquals(authority, expected)
            || !ReferenceEquals(authority.Client.Source, resultSource))
        {
            throw new InvalidOperationException(
                "The package source result belongs to an unknown or retired configured authority.");
        }

        RequireAuthorityIdentity(resultSource, expected);
    }

    private static void RequireAuthorityIdentity(
        PackageSourceResultIdentity resultSource,
        AuthorityEntry expected)
    {
        if (!ReferenceEquals(resultSource.Association, expected.Association)
            || !ReferenceEquals(resultSource, expected.Client.Source))
        {
            throw new InvalidOperationException(
                "The package source result does not belong to the configured authority runtime.");
        }
    }

    private static HttpMessageHandler CreateProductionTransport(
        PackageSource source,
        bool isGallery) =>
        isGallery
            ? HttpClientFactory.CreateCredentialFreeHandler()
            : HttpClientFactory.CreateCredentialFreePackageSourceHandler(
                source.Url);

    internal static PackageAuthorityFailure DescribeFailure(
        PackageSource source,
        PackageSourceFailure failure)
    {
        InertString authority = PackageSourceDisplay.ForDiagnostics(source);
        PackageAuthorityFailureKind kind = ClassifySourceFailure(failure.Kind);
        string message = kind switch
        {
            PackageAuthorityFailureKind.AuthenticationRequired =>
                $"Package source {authority} requires credentials or rejected the supplied credentials.",
            PackageAuthorityFailureKind.Timeout =>
                $"Package source {authority} timed out while enumerating versions.",
            PackageAuthorityFailureKind.Unsupported =>
                $"Package source {authority} does not support version enumeration.",
            PackageAuthorityFailureKind.IncompleteMetadata =>
                $"Package source {authority} did not provide complete version metadata.",
            PackageAuthorityFailureKind.InvalidResponse =>
                $"Package source {authority} returned invalid version metadata.",
            PackageAuthorityFailureKind.ResponseRejected =>
                $"Package source {authority} returned version metadata outside the configured safety limits.",
            PackageAuthorityFailureKind.Transport =>
                $"Package source {authority} could not be reached while enumerating versions.",
            _ => failure.Message,
        };
        return new PackageAuthorityFailure(authority, kind, message)
        {
            SourceFailure = failure,
            ResultSource = failure.Source,
        };
    }

    private static PackageVersionDiscoveryResult Failed(
        PackageAuthorityFailure failure,
        PackageVersionDiscoveryContract contract) =>
        new(
            PackageVersionDiscoveryState.Failed,
            [],
            [failure],
            hasAnyCandidate: false,
            contract: contract);

    private static void AddOperationTimeoutFailure(
        List<PackageAuthorityFailure> failures,
        NuGetOperationContext operation)
    {
        if (failures.Any(failure =>
                failure.Authority == InertString.Empty
                && failure.Kind == PackageAuthorityFailureKind.Timeout))
        {
            return;
        }

        failures.Add(new PackageAuthorityFailure(
            InertString.Empty,
            PackageAuthorityFailureKind.Timeout,
            "The package version operation deadline expired before the aggregate completed.")
        {
            Timeout = new(PackageSourceTimeoutKind.Operation, operation.OperationTimeout),
        });
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        List<Exception>? failures = null;
        foreach (AuthorityEntry authority in _authorities.Values)
        {
            try
            {
                authority.Dispose();
            }
            catch (Exception exception)
            {
                (failures ??= []).Add(exception);
            }
        }
        _authorities.Clear();
        _authoritiesByAssociation.Clear();

        if (_ownedCredentialSource is not null)
        {
            try
            {
                await _ownedCredentialSource.DisposeAsync()
                    .ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                (failures ??= []).Add(exception);
            }
        }

        if (failures is not null)
            throw new AggregateException(failures);
    }

    private sealed class AuthorityEntry(
        ConfiguredPackageAuthority authority,
        PluginAuthenticationContextOwner? authenticationOwner,
        IPackageSourceClient client) : IDisposable
    {
        public ConfiguredPackageAuthority Authority { get; } = authority;
        public PackageSource Source => Authority.Source;
        public PackageSourceAssociation Association => Authority.Association;
        public IPackageSourceClient Client { get; } = client;

        public void Dispose()
        {
            Exception? clientFailure = null;
            try
            {
                Client.Dispose();
            }
            catch (Exception exception)
            {
                clientFailure = exception;
            }

            try
            {
                authenticationOwner?.Dispose();
            }
            catch (Exception authenticationFailure)
            {
                if (clientFailure is not null)
                {
                    throw new AggregateException(
                        clientFailure,
                        authenticationFailure);
                }

                throw;
            }

            if (clientFailure is not null)
                ExceptionDispatchInfo.Capture(clientFailure).Throw();
        }
    }
}
