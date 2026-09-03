using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
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
    string Message);

/// <summary>
/// The package-owned aggregate of version evidence from every eligible
/// configured authority.
/// </summary>
public sealed class PackageVersionDiscoveryResult
{
    internal PackageVersionDiscoveryResult(
        PackageVersionDiscoveryState state,
        IReadOnlyList<string> versions,
        IReadOnlyList<PackageAuthorityFailure> failures,
        bool hasAnyCandidate)
    {
        State = state;
        Versions = new ReadOnlyCollection<string>([.. versions]);
        Failures =
            new ReadOnlyCollection<PackageAuthorityFailure>([.. failures]);
        HasAnyCandidate = hasAnyCandidate;
    }

    public PackageVersionDiscoveryState State { get; }
    public IReadOnlyList<string> Versions { get; }
    public IReadOnlyList<PackageAuthorityFailure> Failures { get; }
    public bool HasAnyCandidate { get; }
}

/// <summary>
/// Owns source associations, plugin-authentication contexts, and V3 routes for
/// one desktop package-composition lifetime.
/// </summary>
public sealed class DesktopPackageSourceComposition : IAsyncDisposable
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

    /// <summary>
    /// Enumerates versions from every configured authority eligible for one
    /// package ID and adopts their results through exact association lookup.
    /// </summary>
    public async Task<PackageVersionDiscoveryResult> GetVersionsAsync(
        string packageId,
        bool includePrerelease,
        int? limit,
        NuGetSourceOptions? sourceOptions = null,
        Action<string>? log = null,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(
            Volatile.Read(ref _disposed) != 0,
            this);
        if (!PackageExtractor.IsValidPackageId(packageId))
        {
            return Failed(
                new PackageAuthorityFailure(
                    InertString.Empty,
                    PackageAuthorityFailureKind.Input,
                    "The package ID must use the NuGet package ID grammar."));
        }

        if (limit <= 0)
        {
            return Failed(
                new PackageAuthorityFailure(
                    InertString.Empty,
                    PackageAuthorityFailureKind.Input,
                    "The package version limit must be greater than zero."));
        }

        if (sourceOptions?.ConfigFile is { } configFile
            && NuGetSourceResolver.DescribeConfigProblem(configFile)
                is string configProblem)
        {
            return Failed(
                new PackageAuthorityFailure(
                    InertString.Empty,
                    PackageAuthorityFailureKind.Configuration,
                    configProblem));
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
            return Failed(
                new PackageAuthorityFailure(
                    InertString.Empty,
                    PackageAuthorityFailureKind.Configuration,
                    exception.Message));
        }
        catch (InvalidDataException)
        {
            return Failed(
                new PackageAuthorityFailure(
                    InertString.Empty,
                    PackageAuthorityFailureKind.Configuration,
                    "The NuGet package source mapping configuration is malformed, so no source can be authorized."));
        }

        IReadOnlyList<PackageSource> sources =
            NuGetSourceResolver.ResolveAuthorizedSources(
                sourceOptions,
                resolution.Sources);
        var failures = resolution.Failures
            .Select(failure => new PackageAuthorityFailure(
                failure.Authority,
                PackageAuthorityFailureKind.Configuration,
                failure.Message))
            .ToList();
        if (sources.Count == 0)
        {
            if (failures.Count > 0)
            {
                return new PackageVersionDiscoveryResult(
                    PackageVersionDiscoveryState.Failed,
                    [],
                    failures,
                    hasAnyCandidate: false);
            }

            return Failed(
                new PackageAuthorityFailure(
                    InertString.Empty,
                    PackageAuthorityFailureKind.Configuration,
                    $"No configured package source is authorized for '{packageId}'."));
        }

        using var operation = new NuGetOperationContext(
            _options.RequestTimeout,
            _options.OperationTimeout,
            cancellationToken);
        var versions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
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
                AddOperationTimeoutFailure(failures);
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

            if (!Uri.TryCreate(
                    source.Url,
                    UriKind.Absolute,
                    out Uri? endpoint)
                || endpoint.Scheme is not ("http" or "https"))
            {
                failures.Add(new PackageAuthorityFailure(
                    PackageSourceDisplay.ForDiagnostics(source),
                    PackageAuthorityFailureKind.Unsupported,
                    $"Package source {PackageSourceDisplay.ForDiagnostics(source)} does not support version enumeration in this host."));
                continue;
            }

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
                continue;
            }

            bool isGallery =
                authorityKey.IsNuGetOrg && source.Credential is null;
            if (!isGallery
                && source.Credential is null
                && !PluginAuthenticationContext.CanScopeProviderQuery(
                    endpoint))
            {
                InertString sourceDisplay =
                    PackageSourceDisplay.ForDiagnostics(source);
                failures.Add(new PackageAuthorityFailure(
                    sourceDisplay,
                    PackageAuthorityFailureKind.Configuration,
                    $"Package source {sourceDisplay} cannot be used with credential-provider authentication because its service-index scope is unusable."));
                continue;
            }

            AuthorityEntry authority = GetOrCreateAuthority(
                source,
                authorityKey,
                endpoint,
                isGallery);
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
                    if (listed
                        && (includePrerelease || !parsed.IsPrerelease))
                    {
                        versions.Add(candidate.Coordinate.Version);
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
                AddOperationTimeoutFailure(failures);
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

        List<string> ordered =
        [
            .. versions
                .Select(version => (
                    Parsed: NuGetVersion.Parse(version),
                    Original: version))
                .OrderByDescending(candidate => candidate.Parsed)
                .Take(limit ?? int.MaxValue)
                .Select(candidate => candidate.Original),
        ];
        try
        {
            operation.ThrowIfExpired();
        }
        catch (NuGetOperationTimeoutException)
        {
            operationTimedOut = true;
            AddOperationTimeoutFailure(failures);
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
            hasAnyCandidate);
    }

    private AuthorityEntry GetOrCreateAuthority(
        PackageSource source,
        ConfiguredPackageAuthorityKey key,
        Uri endpoint,
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

        PackageSourceAssociation association =
            PackageSourceAssociation.Create();
        PluginAuthenticationContextOwner? owner = null;
        HttpMessageHandler? transport = null;
        IPackageSourceClient? client = null;
        try
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
                if (source.Credential is null)
                {
                    owner = PluginAuthenticationContextOwner.Create(
                        association,
                        endpoint,
                        _credentialSource);
                }
                client = PackageSourceClientFactory.Create(
                    source,
                    association,
                    transport,
                    _options,
                    owner?.Context);
            }

            var authority =
                new AuthorityEntry(source, association, owner, client);
            _authorities.Add(key, authority);
            _authoritiesByAssociation.Add(association, authority);
            return authority;
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
    }

    private static HttpMessageHandler CreateProductionTransport(
        PackageSource source,
        bool isGallery) =>
        isGallery
            ? HttpClientFactory.CreateCredentialFreeHandler()
            : HttpClientFactory.CreateCredentialFreePackageSourceHandler(
                source.Url);

    private static PackageAuthorityFailure DescribeFailure(
        PackageSource source,
        PackageSourceFailure failure)
    {
        InertString authority = PackageSourceDisplay.ForDiagnostics(source);
        PackageAuthorityFailureKind kind = failure.Kind switch
        {
            PackageSourceFailureKind.Unsupported =>
                PackageAuthorityFailureKind.Unsupported,
            PackageSourceFailureKind.AuthenticationRequired =>
                PackageAuthorityFailureKind.AuthenticationRequired,
            PackageSourceFailureKind.Timeout =>
                PackageAuthorityFailureKind.Timeout,
            PackageSourceFailureKind.InvalidResponse =>
                PackageAuthorityFailureKind.InvalidResponse,
            PackageSourceFailureKind.ResponseRejected =>
                PackageAuthorityFailureKind.ResponseRejected,
            PackageSourceFailureKind.Transport =>
                PackageAuthorityFailureKind.Transport,
            PackageSourceFailureKind.NotFound =>
                throw new InvalidOperationException(
                    "Version enumeration cannot return a payload-not-found failure."),
            _ => throw new ArgumentOutOfRangeException(
                nameof(failure)),
        };
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
        return new PackageAuthorityFailure(authority, kind, message);
    }

    private static PackageVersionDiscoveryResult Failed(
        PackageAuthorityFailure failure) =>
        new(
            PackageVersionDiscoveryState.Failed,
            [],
            [failure],
            hasAnyCandidate: false);

    private static void AddOperationTimeoutFailure(
        List<PackageAuthorityFailure> failures)
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
            "The package version operation deadline expired before the aggregate completed."));
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
        PackageSource source,
        PackageSourceAssociation association,
        PluginAuthenticationContextOwner? authenticationOwner,
        IPackageSourceClient client) : IDisposable
    {
        public PackageSource Source { get; } = source;
        public PackageSourceAssociation Association { get; } = association;
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

internal sealed class ConfiguredPackageAuthorityKey :
    IEquatable<ConfiguredPackageAuthorityKey>
{
    private static readonly ConfiguredPackageAuthorityKey NuGetOrg =
        Create(PackageSource.NuGetOrg);

    private readonly string _value;

    private ConfiguredPackageAuthorityKey(string value) => _value = value;

    public static ConfiguredPackageAuthorityKey Create(PackageSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (TryCreate(source, out ConfiguredPackageAuthorityKey? key, out _))
            return key;

        throw new ArgumentException(
            "The package source endpoint is unusable.",
            nameof(source));
    }

    public static bool TryCreate(
        PackageSource source,
        [NotNullWhen(true)] out ConfiguredPackageAuthorityKey? key,
        [NotNullWhen(false)] out string? problem)
    {
        ArgumentNullException.ThrowIfNull(source);
        key = null;
        problem = null;
        if (LocalPackageSourceIdentity.IsLocalSource(source.Url))
        {
            try
            {
                key = new ConfiguredPackageAuthorityKey(
                    $"local\n{LocalPackageSourceIdentity.CreateAbsolute(source.Url).PersistentValue}");
                return true;
            }
            catch (Exception exception) when (exception is
                ArgumentException
                or IOException
                or NotSupportedException)
            {
                problem = "The local package source path is unusable.";
                return false;
            }
        }

        int schemeEnd = source.Url.IndexOf(
            "://",
            StringComparison.Ordinal);
        if (schemeEnd <= 0
            || !Uri.TryCreate(
                source.Url,
                UriKind.Absolute,
                out Uri? endpoint)
            || endpoint.Scheme is not ("http" or "https")
            || !NuGetHttpRequest.HasValidRawText(
                source.Url,
                allowNonAscii: true)
            || !NuGetSourceRequest.TryEndpointUrl(source.Url, out _))
        {
            problem =
                "The package source service-index endpoint is unusable.";
            return false;
        }

        string host;
        try
        {
            host = endpoint.HostNameType == UriHostNameType.IPv6
                ? $"[{endpoint.IdnHost}]"
                : endpoint.IdnHost.ToLowerInvariant();
        }
        catch (UriFormatException)
        {
            problem =
                "The package source service-index endpoint has an unusable host.";
            return false;
        }

        int suffixStart = source.Url.IndexOfAny(
            ['/', '?', '#'],
            schemeEnd + 3);
        string suffix = suffixStart < 0
            ? string.Empty
            : source.Url[suffixStart..];
        int pathEnd = suffix.IndexOfAny(['?', '#']);
        if (pathEnd < 0)
            pathEnd = suffix.Length;
        string path = suffix[..pathEnd];
        if (path.EndsWith("/", StringComparison.Ordinal))
            path = path[..^1];
        string remainder = suffix[pathEnd..];
        string origin =
            $"{endpoint.Scheme.ToLowerInvariant()}://{host}:{endpoint.Port}";
        key = new ConfiguredPackageAuthorityKey(
            $"{origin}{NuGetCredentialScope.NormalizeEscapes(path)}"
            + NuGetCredentialScope.NormalizeEscapes(remainder));
        return true;
    }

    public bool IsNuGetOrg => Equals(NuGetOrg);

    public bool Equals(ConfiguredPackageAuthorityKey? other) =>
        other is not null
        && string.Equals(_value, other._value, StringComparison.Ordinal);

    public override bool Equals(object? obj) =>
        obj is ConfiguredPackageAuthorityKey other && Equals(other);

    public override int GetHashCode() =>
        StringComparer.Ordinal.GetHashCode(_value);

    public override string ToString() =>
        nameof(ConfiguredPackageAuthorityKey);

}
