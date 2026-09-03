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
    AuthenticationRequired,
    Timeout,
    InvalidResponse,
    ResponseRejected,
    Transport,
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
        PackageSource source);

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
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId);
        if (limit <= 0)
            throw new ArgumentOutOfRangeException(nameof(limit));

        PackageSourceAuthorization authorization =
            new SourcePolicyPackageSourceAuthorization(sourceOptions)
                .AuthorizeSourcesFor(packageId);
        if (authorization.Sources.Count == 0)
        {
            string message = authorization.DenialReason
                ?? $"No configured package source is authorized for '{packageId}'.";
            return Failed(
                new PackageAuthorityFailure(
                    InertString.Empty,
                    PackageAuthorityFailureKind.Configuration,
                    message));
        }

        using var operation = new NuGetOperationContext(
            _options.RequestTimeout,
            _options.OperationTimeout,
            cancellationToken);
        var versions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var failures = new List<PackageAuthorityFailure>();
        bool hasAnyCandidate = false;
        bool operationTimedOut = false;

        for (int sourceIndex = 0;
             sourceIndex < authorization.Sources.Count;
             sourceIndex++)
        {
            PackageSource source = authorization.Sources[sourceIndex];
            try
            {
                operation.ThrowIfExpired();
            }
            catch (NuGetOperationTimeoutException)
            {
                operationTimedOut = true;
                AddOperationTimeoutFailure(failures);
                for (int remainingIndex = sourceIndex;
                     remainingIndex < authorization.Sources.Count;
                     remainingIndex++)
                {
                    PackageSource remaining =
                        authorization.Sources[remainingIndex];
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

            AuthorityEntry authority = GetOrCreateAuthority(source);
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
                     remainingIndex < authorization.Sources.Count;
                     remainingIndex++)
                {
                    PackageSource remaining =
                        authorization.Sources[remainingIndex];
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

    private AuthorityEntry GetOrCreateAuthority(PackageSource source)
    {
        ConfiguredPackageAuthorityKey key =
            ConfiguredPackageAuthorityKey.Create(source);
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
            transport = _createTransport(source);
            if (source.IsNuGetOrg && source.Credential is null)
            {
                client = PackageSourceClientFactory.CreateGallery(
                    association,
                    transport,
                    _options);
            }
            else
            {
                owner = PluginAuthenticationContextOwner.Create(
                    association,
                    new Uri(source.Url),
                    _credentialSource);
                client = PackageSourceClientFactory.Create(
                    source,
                    association,
                    transport,
                    _options,
                    owner.Context);
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
        PackageSource source) =>
        source.IsNuGetOrg && source.Credential is null
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
    private readonly string _value;

    private ConfiguredPackageAuthorityKey(string value) => _value = value;

    public static ConfiguredPackageAuthorityKey Create(PackageSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (LocalPackageSourceIdentity.IsLocalSource(source.Url))
        {
            return new ConfiguredPackageAuthorityKey(
                $"local\n{LocalPackageSourceIdentity.CreateAbsolute(source.Url).PersistentValue}");
        }

        if (!Uri.TryCreate(source.Url, UriKind.Absolute, out Uri? endpoint))
            throw new ArgumentException("The package source is unusable.", nameof(source));

        return new ConfiguredPackageAuthorityKey(
            NuGetCredentialScope.CanonicalizeEndpoint(endpoint));
    }

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
