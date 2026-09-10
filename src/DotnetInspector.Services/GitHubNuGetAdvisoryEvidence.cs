using System.Collections.Immutable;
using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using DotnetInspector.Core;
using DotnetInspector.Packages;
using NuGetFetch;
using NuGet.Versioning;

namespace DotnetInspector.Services;

/// <summary>Severity assigned by a GitHub-reviewed advisory.</summary>
public enum GitHubNuGetAdvisorySeverity
{
    Unknown,
    Low,
    Medium,
    High,
    Critical,
}

/// <summary>Coverage of one evidence category for one exact coordinate.</summary>
public enum GitHubNuGetAdvisoryAvailability
{
    Complete,
    Partial,
    Unavailable,
}

/// <summary>Why a reviewed-advisory acquisition was incomplete.</summary>
public enum GitHubNuGetAdvisoryFailureKind
{
    RequestLimitReached,
    ResponseByteLimitReached,
    AggregateResponseByteLimitReached,
    DeadlineReached,
    RateLimitOrForbidden,
    SourceUnavailable,
    InvalidData,
    InvalidContinuation,
}

/// <summary>Bounds one GitHub reviewed-NuGet-advisory acquisition.</summary>
public sealed record GitHubNuGetAdvisoryOptions
{
    private static readonly TimeSpan MaximumOperationTimeout =
        TimeSpan.FromMilliseconds(uint.MaxValue - 1d);

    /// <summary>Largest coordinate set accepted by one request.</summary>
    public const int MaximumCoordinates = 1_000;

    /// <summary>Default package-name count in one API query.</summary>
    public const int DefaultMaxPackageIdsPerRequest = 100;

    /// <summary>Default UTF-8 byte limit for one API request URI.</summary>
    public const int DefaultMaxRequestUriBytes = 7_000;

    /// <summary>Default number of API documents admitted per acquisition.</summary>
    public const int DefaultMaxApiRequests = 32;

    /// <summary>Default byte limit for one response body.</summary>
    public const long DefaultMaxResponseBytes = 4 * 1024 * 1024;

    /// <summary>Default response-byte limit across one acquisition.</summary>
    public const long DefaultMaxAggregateResponseBytes = 32 * 1024 * 1024;

    /// <summary>Gets the package-name count admitted to one API query.</summary>
    public int MaxPackageIdsPerRequest { get; init; } =
        DefaultMaxPackageIdsPerRequest;

    /// <summary>Gets the UTF-8 byte limit for one API request URI.</summary>
    public int MaxRequestUriBytes { get; init; } =
        DefaultMaxRequestUriBytes;

    /// <summary>Gets the API document limit for one acquisition.</summary>
    public int MaxApiRequests { get; init; } =
        DefaultMaxApiRequests;

    /// <summary>Gets the response-body byte limit for one API document.</summary>
    public long MaxResponseBytes { get; init; } =
        DefaultMaxResponseBytes;

    /// <summary>Gets the response-byte limit across one acquisition.</summary>
    public long MaxAggregateResponseBytes { get; init; } =
        DefaultMaxAggregateResponseBytes;

    /// <summary>Gets the wall-clock ceiling for one acquisition.</summary>
    public TimeSpan OperationTimeout { get; init; } =
        TimeSpan.FromSeconds(120);

    internal static GitHubNuGetAdvisoryOptions Validate(
        GitHubNuGetAdvisoryOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (options.MaxPackageIdsPerRequest is < 1 or > MaximumCoordinates)
        {
            throw new ArgumentOutOfRangeException(
                nameof(MaxPackageIdsPerRequest));
        }

        if (options.MaxRequestUriBytes < 512)
            throw new ArgumentOutOfRangeException(nameof(MaxRequestUriBytes));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(
            options.MaxApiRequests);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(
            options.MaxResponseBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(
            options.MaxAggregateResponseBytes);
        if (options.OperationTimeout <= TimeSpan.Zero
            || options.OperationTimeout > MaximumOperationTimeout)
        {
            throw new ArgumentOutOfRangeException(nameof(OperationTimeout));
        }

        return options;
    }
}

/// <summary>
/// A bounded set of exact NuGet.org coordinates to evaluate against
/// GitHub-reviewed advisories.
/// </summary>
public sealed class GitHubNuGetAdvisoryRequest
{
    internal sealed record Coordinate(
        PackageSourceCoordinate Value,
        NuGetVersion Version,
        string Key);

    internal sealed record Package(
        string PackageId,
        ImmutableArray<Coordinate> Coordinates);

    /// <summary>Creates a request bound to the canonical NuGet.org producer.</summary>
    public GitHubNuGetAdvisoryRequest(
        PackageProducerIdentity producer,
        IEnumerable<PackageSourceCoordinate> coordinates)
    {
        ArgumentNullException.ThrowIfNull(producer);
        ArgumentNullException.ThrowIfNull(coordinates);
        if (producer != PackageProducerIdentity.NuGetOrg)
        {
            throw new ArgumentException(
                "GitHub NuGet advisory evidence is defined only for NuGet.org package coordinates.",
                nameof(producer));
        }

        Producer = producer;
        var ordered = new List<Coordinate>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (PackageSourceCoordinate coordinate in coordinates)
        {
            ArgumentNullException.ThrowIfNull(coordinate);
            if (!NuGetVersion.TryParse(
                    coordinate.Version,
                    out NuGetVersion? parsedVersion))
            {
                throw new ArgumentException(
                    "Advisory coordinates must have an exact NuGet version.",
                    nameof(coordinates));
            }

            string normalizedVersion = parsedVersion.ToNormalizedString();
            string key = $"{coordinate.PackageId}\0{normalizedVersion}";
            if (!seen.Add(key))
                continue;

            if (ordered.Count == GitHubNuGetAdvisoryOptions.MaximumCoordinates)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(coordinates),
                    $"An advisory request cannot exceed "
                    + $"{GitHubNuGetAdvisoryOptions.MaximumCoordinates} coordinates.");
            }

            ordered.Add(new Coordinate(
                PackageSourceCoordinate.Create(
                    coordinate.PackageId,
                    normalizedVersion),
                parsedVersion,
                key));
        }

        Coordinates = ordered.Select(static item => item.Value).ToImmutableArray();
        NormalizedCoordinates = [.. ordered];
        Packages = [.. ordered
            .GroupBy(
                static item => item.Value.PackageId,
                StringComparer.OrdinalIgnoreCase)
            .Select(static group => new Package(
                group.First().Value.PackageId,
                [.. group]))];
    }

    /// <summary>Gets the package producer whose coordinates are being evaluated.</summary>
    public PackageProducerIdentity Producer { get; }

    /// <summary>Gets the normalized, de-duplicated coordinates in stable input order.</summary>
    public IReadOnlyList<PackageSourceCoordinate> Coordinates { get; }

    internal ImmutableArray<Coordinate> NormalizedCoordinates { get; }

    internal ImmutableArray<Package> Packages { get; }
}

/// <summary>
/// Resource-free identity, severity, and advisory-document time basis for one
/// GitHub-reviewed advisory.
/// </summary>
public sealed record GitHubNuGetAdvisoryReference(
    string GhsaId,
    string? CveId,
    GitHubNuGetAdvisorySeverity Severity,
    Uri AdvisoryUrl,
    DateTimeOffset PublishedAt,
    DateTimeOffset UpdatedAt);

/// <summary>
/// Current-context and explicit fixed-version evidence for one exact package
/// coordinate.
/// </summary>
public sealed class GitHubNuGetAdvisoryPackageEvidence
{
    internal GitHubNuGetAdvisoryPackageEvidence(
        PackageSourceCoordinate coordinate,
        GitHubNuGetAdvisoryAvailability currentContextAvailability,
        ImmutableArray<GitHubNuGetAdvisoryReference> currentAdvisories,
        GitHubNuGetAdvisoryAvailability fixedVersionAvailability,
        ImmutableArray<GitHubNuGetAdvisoryReference> fixedVersionAdvisories)
    {
        Coordinate = coordinate;
        CurrentContextAvailability = currentContextAvailability;
        CurrentAdvisories = currentAdvisories;
        FixedVersionAvailability = fixedVersionAvailability;
        FixedVersionAdvisories = fixedVersionAdvisories;
    }

    /// <summary>Gets the exact normalized coordinate.</summary>
    public PackageSourceCoordinate Coordinate { get; }

    /// <summary>Gets current-context coverage for the coordinate.</summary>
    public GitHubNuGetAdvisoryAvailability CurrentContextAvailability { get; }

    /// <summary>Gets acquired advisories whose affected range includes the coordinate.</summary>
    public IReadOnlyList<GitHubNuGetAdvisoryReference> CurrentAdvisories { get; }

    /// <summary>Gets fixed-version coverage for the coordinate.</summary>
    public GitHubNuGetAdvisoryAvailability FixedVersionAvailability { get; }

    /// <summary>Gets advisories that explicitly name the coordinate as first patched.</summary>
    public IReadOnlyList<GitHubNuGetAdvisoryReference> FixedVersionAdvisories { get; }
}

/// <summary>
/// Resource-free result of one bounded GitHub reviewed-NuGet-advisory
/// acquisition.
/// </summary>
public sealed class GitHubNuGetAdvisoryAcquisition
{
    internal GitHubNuGetAdvisoryAcquisition(
        ImmutableArray<GitHubNuGetAdvisoryPackageEvidence> packages,
        ImmutableArray<GitHubNuGetAdvisoryFailureKind> failures,
        PackageProducerIdentity packageProducer,
        DateTimeOffset observedAt,
        int apiRequests,
        long responseBytes)
    {
        Packages = packages;
        Failures = failures;
        PackageProducer = packageProducer;
        ObservedAt = observedAt;
        ApiRequests = apiRequests;
        ResponseBytes = responseBytes;
    }

    /// <summary>Gets the fixed advisory-document producer endpoint.</summary>
    public static Uri AdvisoryProducer { get; } =
        new("https://api.github.com/advisories");

    /// <summary>Gets the package producer to which the coordinates belong.</summary>
    public PackageProducerIdentity PackageProducer { get; }

    /// <summary>Gets evidence in normalized request order.</summary>
    public IReadOnlyList<GitHubNuGetAdvisoryPackageEvidence> Packages { get; }

    /// <summary>Gets every terminal or data failure observed by the acquisition.</summary>
    public IReadOnlyList<GitHubNuGetAdvisoryFailureKind> Failures { get; }

    /// <summary>Gets the UTC time at which the acquisition result was observed.</summary>
    public DateTimeOffset ObservedAt { get; }

    /// <summary>Gets the number of API document requests attempted.</summary>
    public int ApiRequests { get; }

    /// <summary>Gets the response-body bytes consumed across the acquisition.</summary>
    public long ResponseBytes { get; }

    /// <summary>Gets whether every coordinate and category has complete coverage.</summary>
    public bool Complete =>
        Failures.Count == 0
        && Packages.All(static package =>
            package.CurrentContextAvailability
                == GitHubNuGetAdvisoryAvailability.Complete
            && package.FixedVersionAvailability
                == GitHubNuGetAdvisoryAvailability.Complete);
}

/// <summary>
/// Acquires and evaluates GitHub-reviewed NuGet advisories for exact NuGet.org
/// package coordinates.
/// </summary>
public sealed class GitHubNuGetAdvisoryService
{
    private const string ApiRoot = "https://api.github.com/advisories";
    private const string QueryPrefix =
        ApiRoot
        + "?ecosystem=nuget&type=reviewed&is_withdrawn=false"
        + "&per_page=100&affects=";

    private readonly HttpClient _client;
    private readonly GitHubNuGetAdvisoryOptions _options;
    private readonly TimeProvider _timeProvider;

    /// <summary>Creates a bounded advisory service over the supplied HTTP client.</summary>
    public GitHubNuGetAdvisoryService(
        HttpClient client,
        GitHubNuGetAdvisoryOptions? options = null,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(client);
        _client = client;
        _options = GitHubNuGetAdvisoryOptions.Validate(
            options ?? new GitHubNuGetAdvisoryOptions());
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <summary>Acquires category-qualified evidence for the requested coordinates.</summary>
    public async Task<GitHubNuGetAdvisoryAcquisition> AcquireAsync(
        GitHubNuGetAdvisoryRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var state = new AcquisitionState(request);
        if (request.Coordinates.Count == 0)
            return state.CreateResult(
                _timeProvider.GetUtcNow().ToUniversalTime());

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken);
        deadline.CancelAfter(_options.OperationTimeout);
        var operationDeadline = new OperationDeadline(
            _timeProvider,
            _options.OperationTimeout);

        foreach (ImmutableArray<GitHubNuGetAdvisoryRequest.Package> batch
            in CreateBatches(request.Packages))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (operationDeadline.IsExpired(deadline.Token))
            {
                cancellationToken.ThrowIfCancellationRequested();
                state.AddFailure(
                    GitHubNuGetAdvisoryFailureKind.DeadlineReached);
                break;
            }
            if (state.ApiRequests == _options.MaxApiRequests)
            {
                state.AddFailure(
                    GitHubNuGetAdvisoryFailureKind.RequestLimitReached);
                break;
            }

            string? next = CreateRequestUri(batch);
            bool batchComplete = false;
            bool batchUsable = false;
            bool followingContinuation = false;

            while (next is not null)
            {
                if (operationDeadline.IsExpired(deadline.Token))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    state.AddFailure(
                        GitHubNuGetAdvisoryFailureKind.DeadlineReached);
                    break;
                }
                if (state.ApiRequests == _options.MaxApiRequests)
                {
                    state.AddFailure(
                        GitHubNuGetAdvisoryFailureKind.RequestLimitReached);
                    break;
                }

                long remainingBytes =
                    _options.MaxAggregateResponseBytes - state.ResponseBytes;
                if (remainingBytes <= 0)
                {
                    state.AddFailure(
                        GitHubNuGetAdvisoryFailureKind.AggregateResponseByteLimitReached);
                    break;
                }

                state.ApiRequests++;
                LinkResult links = default;
                HttpRetryHelper.HttpBodyFetchResult response;
                try
                {
                    response =
                        await HttpRetryHelper.GetBytesAfterHeadersWithRetryAsync(
                            _client,
                            next,
                            responseMessage =>
                            {
                                if (!IsAcceptedResponse(responseMessage))
                                    return false;
                                links = ReadLinks(responseMessage);
                                return true;
                            },
                            retryCount: 0,
                            cancellationToken: deadline.Token,
                            trafficKind: NetworkTrafficKind.AdvisoryData,
                            maxDownloadSize: Math.Min(
                                _options.MaxResponseBytes,
                                remainingBytes),
                            configureRequest: static message =>
                            {
                                message.Headers.TryAddWithoutValidation(
                                    "User-Agent",
                                    "dotnet-inspect");
                                message.Headers.TryAddWithoutValidation(
                                    "Accept",
                                    "application/vnd.github+json");
                                message.Headers.TryAddWithoutValidation(
                                    "X-GitHub-Api-Version",
                                    "2022-11-28");
                            },
                            preservePathAndQuery: true)
                            .ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                    when (!cancellationToken.IsCancellationRequested)
                {
                    state.AddFailure(
                        GitHubNuGetAdvisoryFailureKind.DeadlineReached);
                    break;
                }

                state.ResponseBytes += response.BodyBytesRead;
                if (response.Status
                    != HttpRetryHelper.HttpBodyFetchStatus.Success
                    || response.Bytes is null)
                {
                    GitHubNuGetAdvisoryFailureKind failure =
                        response.StatusCode is HttpStatusCode.Forbidden
                            or HttpStatusCode.TooManyRequests
                                ? GitHubNuGetAdvisoryFailureKind
                                    .RateLimitOrForbidden
                            : response.Status
                                == HttpRetryHelper.HttpBodyFetchStatus.TooLarge
                                ? remainingBytes < _options.MaxResponseBytes
                                    || response.BodyBytesRead > remainingBytes
                                    ? GitHubNuGetAdvisoryFailureKind
                                        .AggregateResponseByteLimitReached
                                    : GitHubNuGetAdvisoryFailureKind
                                        .ResponseByteLimitReached
                            : response.Status
                                == HttpRetryHelper.HttpBodyFetchStatus.ResponseRejected
                                ? followingContinuation
                                    ? GitHubNuGetAdvisoryFailureKind
                                        .InvalidContinuation
                                    : GitHubNuGetAdvisoryFailureKind
                                        .SourceUnavailable
                            : GitHubNuGetAdvisoryFailureKind.SourceUnavailable;
                    state.AddFailure(failure);
                    break;
                }

                batchUsable |= ParsePage(
                    response.Bytes,
                    batch,
                    state,
                    deadline.Token,
                    operationDeadline,
                    out bool localDeadlineReached);
                if (localDeadlineReached
                    || operationDeadline.IsExpired(deadline.Token))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    state.AddFailure(
                        GitHubNuGetAdvisoryFailureKind.DeadlineReached);
                    break;
                }

                if (links.Invalid)
                {
                    state.AddFailure(
                        GitHubNuGetAdvisoryFailureKind.InvalidContinuation);
                    break;
                }

                if (links.Next is null)
                {
                    batchComplete = true;
                    break;
                }

                if (!TryValidateContinuation(links.Next, batch, out next))
                {
                    state.AddFailure(
                        GitHubNuGetAdvisoryFailureKind.InvalidContinuation);
                    break;
                }

                followingContinuation = true;
            }

            foreach (GitHubNuGetAdvisoryRequest.Package package in batch)
            {
                PackageState packageState = state.Packages[package.PackageId];
                packageState.AcquiredUsablePage |= batchUsable;
                packageState.AcquisitionComplete |= batchComplete;
            }

            if (state.Failures.Contains(
                GitHubNuGetAdvisoryFailureKind.RequestLimitReached)
                || state.Failures.Contains(
                    GitHubNuGetAdvisoryFailureKind.AggregateResponseByteLimitReached)
                || state.Failures.Contains(
                    GitHubNuGetAdvisoryFailureKind.DeadlineReached)
                || state.Failures.Contains(
                    GitHubNuGetAdvisoryFailureKind.RateLimitOrForbidden))
            {
                break;
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        return state.CreateResult(_timeProvider.GetUtcNow().ToUniversalTime());
    }

    private IEnumerable<ImmutableArray<GitHubNuGetAdvisoryRequest.Package>>
        CreateBatches(
            ImmutableArray<GitHubNuGetAdvisoryRequest.Package> packages)
    {
        foreach (GitHubNuGetAdvisoryRequest.Package package in packages)
        {
            if (Encoding.UTF8.GetByteCount(CreateRequestUri([package]))
                > _options.MaxRequestUriBytes)
            {
                throw new InvalidOperationException(
                    "A canonical package ID exceeded the advisory request URI bound.");
            }
        }

        var batch = ImmutableArray.CreateBuilder<
            GitHubNuGetAdvisoryRequest.Package>();
        foreach (GitHubNuGetAdvisoryRequest.Package package in packages)
        {
            batch.Add(package);
            bool tooMany = batch.Count > _options.MaxPackageIdsPerRequest;
            bool tooLong = Encoding.UTF8.GetByteCount(
                CreateRequestUri([.. batch])) > _options.MaxRequestUriBytes;
            if (!tooMany && !tooLong)
                continue;

            batch.RemoveAt(batch.Count - 1);
            if (batch.Count == 0)
            {
                throw new InvalidOperationException(
                    "A canonical package ID exceeded the advisory request URI bound.");
            }

            yield return batch.ToImmutable();
            batch.Clear();
            batch.Add(package);
        }

        if (batch.Count > 0)
            yield return batch.ToImmutable();
    }

    private static string CreateRequestUri(
        ImmutableArray<GitHubNuGetAdvisoryRequest.Package> packages)
        => QueryPrefix + Uri.EscapeDataString(
            string.Join(',', packages.Select(static package => package.PackageId)));

    private static bool IsAcceptedResponse(HttpResponseMessage response)
    {
        Uri? endpoint = response.RequestMessage?.RequestUri;
        return endpoint is not null
            && endpoint.Scheme == Uri.UriSchemeHttps
            && endpoint.Host.Equals("api.github.com", StringComparison.OrdinalIgnoreCase)
            && endpoint.Port == 443
            && endpoint.AbsolutePath.Equals(
                "/advisories",
                StringComparison.Ordinal);
    }

    private static LinkResult ReadLinks(HttpResponseMessage response)
    {
        if (!response.Headers.TryGetValues("Link", out IEnumerable<string>? values))
            return default;

        string? next = null;
        foreach (string value in values)
        {
            foreach (string segment in value.Split(','))
            {
                string candidate = segment.Trim();
                bool mentionsNext = candidate.Contains(
                    "rel=\"next\"",
                    StringComparison.OrdinalIgnoreCase)
                    || candidate.Contains(
                        "rel=next",
                        StringComparison.OrdinalIgnoreCase);
                int close = candidate.IndexOf('>');
                if (close < 0)
                {
                    if (mentionsNext)
                        return new LinkResult(null, Invalid: true);
                    continue;
                }
                bool isNext = candidate[(close + 1)..]
                    .Split(';', StringSplitOptions.TrimEntries)
                    .Any(static parameter =>
                        parameter.Equals(
                            "rel=\"next\"",
                            StringComparison.OrdinalIgnoreCase)
                        || parameter.Equals(
                            "rel=next",
                            StringComparison.OrdinalIgnoreCase));
                if (!isNext)
                {
                    continue;
                }

                int open = candidate.IndexOf('<');
                if (next is not null || open != 0 || close <= open + 1)
                    return new LinkResult(null, Invalid: true);
                next = candidate[(open + 1)..close];
            }
        }

        return new LinkResult(next, Invalid: false);
    }

    private bool TryValidateContinuation(
        string value,
        ImmutableArray<GitHubNuGetAdvisoryRequest.Package> batch,
        out string? continuation)
    {
        continuation = null;
        if (Encoding.UTF8.GetByteCount(value) > _options.MaxRequestUriBytes
            || !Uri.TryCreate(value, UriKind.Absolute, out Uri? parsed)
            || parsed.Scheme != Uri.UriSchemeHttps
            || !parsed.Host.Equals(
                "api.github.com",
                StringComparison.OrdinalIgnoreCase)
            || parsed.Port != 443
            || parsed.UserInfo.Length != 0
            || parsed.Fragment.Length != 0
            || !parsed.AbsolutePath.Equals(
                "/advisories",
                StringComparison.Ordinal)
            || !HasExpectedQuery(
                parsed,
                string.Join(
                    ',',
                    batch.Select(static package => package.PackageId))))
        {
            return false;
        }

        continuation = value;
        return true;
    }

    private static bool HasExpectedQuery(Uri continuation, string packageIds)
    {
        if (!TryReadQuery(continuation.Query, out Dictionary<string, string> query))
            return false;

        return query.TryGetValue("ecosystem", out string? ecosystem)
            && ecosystem.Equals("nuget", StringComparison.OrdinalIgnoreCase)
            && query.TryGetValue("type", out string? type)
            && type.Equals("reviewed", StringComparison.OrdinalIgnoreCase)
            && query.TryGetValue("is_withdrawn", out string? withdrawn)
            && withdrawn.Equals("false", StringComparison.OrdinalIgnoreCase)
            && query.TryGetValue("per_page", out string? perPage)
            && perPage == "100"
            && query.TryGetValue("affects", out string? affects)
            && affects.Equals(packageIds, StringComparison.OrdinalIgnoreCase)
            && query.TryGetValue("after", out string? after)
            && !string.IsNullOrWhiteSpace(after)
            && query.Count == 6;
    }

    private static bool TryReadQuery(
        string queryText,
        out Dictionary<string, string> query)
    {
        query = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        ReadOnlySpan<char> text = queryText.AsSpan();
        if (!text.IsEmpty && text[0] == '?')
            text = text[1..];

        foreach (Range pairRange in text.Split('&'))
        {
            ReadOnlySpan<char> pair = text[pairRange];
            int separator = pair.IndexOf('=');
            if (separator <= 0)
                return false;
            string name;
            string value;
            try
            {
                name = Uri.UnescapeDataString(pair[..separator].ToString());
                value = Uri.UnescapeDataString(pair[(separator + 1)..].ToString());
            }
            catch (UriFormatException)
            {
                return false;
            }

            if (!query.TryAdd(name, value))
                return false;
        }

        return true;
    }

    private static bool ParsePage(
        ReadOnlyMemory<byte> json,
        ImmutableArray<GitHubNuGetAdvisoryRequest.Package> batch,
        AcquisitionState state,
        CancellationToken deadlineToken,
        OperationDeadline operationDeadline,
        out bool deadlineReached)
    {
        deadlineReached = false;
        if (operationDeadline.IsExpired(deadlineToken))
        {
            deadlineReached = true;
            return false;
        }

        JsonDocument document;
        try
        {
            document = HardenedJson.Parse(json);
        }
        catch (JsonException)
        {
            state.MarkInvalid(batch);
            return false;
        }

        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                state.MarkInvalid(batch);
                return false;
            }

            var batchIds = batch.Select(static package => package.PackageId)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (operationDeadline.IsExpired(deadlineToken))
            {
                deadlineReached = true;
                return true;
            }

            foreach (JsonElement element
                in document.RootElement.EnumerateArray())
            {
                if (operationDeadline.IsExpired(deadlineToken))
                {
                    deadlineReached = true;
                    return true;
                }

                if (!TryReadAdvisory(
                        element,
                        batchIds,
                        deadlineToken,
                        operationDeadline,
                        out bool advisoryDeadlineReached,
                        out ParsedAdvisory? advisory))
                {
                    if (advisoryDeadlineReached)
                    {
                        deadlineReached = true;
                        return true;
                    }

                    state.MarkInvalid(batch);
                    continue;
                }

                if (operationDeadline.IsExpired(deadlineToken))
                {
                    deadlineReached = true;
                    return true;
                }

                foreach (ParsedVulnerability vulnerability
                    in advisory!.Vulnerabilities)
                {
                    if (operationDeadline.IsExpired(deadlineToken))
                    {
                        deadlineReached = true;
                        return true;
                    }

                    if (!state.Packages.TryGetValue(
                            vulnerability.PackageId,
                            out PackageState? package))
                    {
                        continue;
                    }

                    if (vulnerability.Range is null)
                    {
                        package.CurrentDataValid = false;
                        state.AddFailure(
                            GitHubNuGetAdvisoryFailureKind.InvalidData);
                    }
                    else
                    {
                        foreach (CoordinateState coordinate
                            in package.Coordinates)
                        {
                            if (operationDeadline.IsExpired(deadlineToken))
                            {
                                deadlineReached = true;
                                return true;
                            }

                            if (!TryEvaluateRange(
                                    vulnerability.Range,
                                    coordinate.Version,
                                    out bool affected))
                            {
                                package.CurrentDataValid = false;
                                state.AddFailure(
                                    GitHubNuGetAdvisoryFailureKind.InvalidData);
                                break;
                            }

                            if (affected)
                                coordinate.Current.TryAdd(advisory.Reference);
                        }
                    }

                    if (vulnerability.FirstPatchedVersionMalformed)
                    {
                        package.FixedDataValid = false;
                        state.AddFailure(
                            GitHubNuGetAdvisoryFailureKind.InvalidData);
                    }
                    else if (vulnerability.FirstPatchedVersion
                        is { } fixedVersion)
                    {
                        foreach (CoordinateState coordinate
                            in package.Coordinates)
                        {
                            if (operationDeadline.IsExpired(deadlineToken))
                            {
                                deadlineReached = true;
                                return true;
                            }

                            if (VersionComparer.VersionRelease.Equals(
                                    fixedVersion,
                                    coordinate.Version))
                            {
                                coordinate.Fixed.TryAdd(advisory.Reference);
                            }
                        }
                    }
                }
            }
        }

        return true;
    }

    private static bool TryReadAdvisory(
        JsonElement element,
        IReadOnlySet<string> requestedPackageIds,
        CancellationToken deadlineToken,
        OperationDeadline operationDeadline,
        out bool deadlineReached,
        out ParsedAdvisory? advisory)
    {
        deadlineReached = operationDeadline.IsExpired(deadlineToken);
        advisory = null;
        if (deadlineReached)
            return false;

        if (element.ValueKind != JsonValueKind.Object
            || !TryRequiredString(element, "ghsa_id", out string? ghsaId)
            || !IsGhsaId(ghsaId)
            || !TryRequiredString(element, "severity", out string? severityText)
            || !TrySeverity(severityText!, out GitHubNuGetAdvisorySeverity severity)
            || !TryRequiredString(element, "type", out string? type)
            || !type!.Equals("reviewed", StringComparison.OrdinalIgnoreCase)
            || !element.TryGetProperty(
                "withdrawn_at",
                out JsonElement withdrawn)
            || withdrawn.ValueKind != JsonValueKind.Null
            || !TryUtcTimestamp(element, "published_at", out DateTimeOffset published)
            || !TryUtcTimestamp(element, "updated_at", out DateTimeOffset updated)
            || updated < published
            || !element.TryGetProperty(
                "vulnerabilities",
                out JsonElement vulnerabilities)
            || vulnerabilities.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        string? cveId = null;
        if (element.TryGetProperty("cve_id", out JsonElement cve))
        {
            if (cve.ValueKind == JsonValueKind.String)
            {
                if (!TryReadString(cve, out cveId)
                    || !IsCveId(cveId))
                {
                    return false;
                }
            }
            else if (cve.ValueKind != JsonValueKind.Null)
            {
                return false;
            }
        }

        var parsed = ImmutableArray.CreateBuilder<ParsedVulnerability>();
        foreach (JsonElement vulnerability in vulnerabilities.EnumerateArray())
        {
            if (operationDeadline.IsExpired(deadlineToken))
            {
                deadlineReached = true;
                return false;
            }

            if (!TryReadVulnerability(
                    vulnerability,
                    requestedPackageIds,
                    out ParsedVulnerability? value))
            {
                return false;
            }
            if (value is not null)
                parsed.Add(value);
        }

        advisory = new ParsedAdvisory(
            new GitHubNuGetAdvisoryReference(
                ghsaId!.ToUpperInvariant(),
                cveId?.ToUpperInvariant(),
                severity,
                new Uri(
                    $"https://github.com/advisories/{ghsaId.ToUpperInvariant()}"),
                published,
                updated),
            parsed.ToImmutable());
        return true;
    }

    private static bool TryReadVulnerability(
        JsonElement element,
        IReadOnlySet<string> requestedPackageIds,
        out ParsedVulnerability? vulnerability)
    {
        vulnerability = null;
        if (element.ValueKind != JsonValueKind.Object
            || !element.TryGetProperty("package", out JsonElement package)
            || package.ValueKind != JsonValueKind.Object
            || !TryRequiredString(package, "ecosystem", out string? ecosystem)
            || !TryRequiredString(package, "name", out string? packageId))
        {
            return false;
        }

        if (!ecosystem!.Equals("nuget", StringComparison.OrdinalIgnoreCase))
            return true;
        if (!requestedPackageIds.Contains(packageId!))
            return true;

        string? range = null;
        if (element.TryGetProperty(
                "vulnerable_version_range",
                out JsonElement rangeElement)
            && TryReadString(rangeElement, out string? rangeValue)
            && !string.IsNullOrWhiteSpace(rangeValue))
        {
            range = rangeValue;
        }

        bool hasFirstPatched = element.TryGetProperty(
            "first_patched_version",
            out JsonElement firstPatchedElement);
        NuGetVersion? firstPatched = null;
        bool malformedFirstPatched = !hasFirstPatched;
        if (hasFirstPatched
            && firstPatchedElement.ValueKind != JsonValueKind.Null)
        {
            if (!TryReadString(
                    firstPatchedElement,
                    out string? identifier)
                || string.IsNullOrWhiteSpace(identifier)
                || !NuGetVersion.TryParse(identifier, out firstPatched))
            {
                malformedFirstPatched = true;
            }
        }

        vulnerability = new ParsedVulnerability(
            packageId!,
            range,
            firstPatched,
            malformedFirstPatched);
        return true;
    }

    internal static bool TryEvaluateRange(
        string expression,
        NuGetVersion version,
        out bool affected)
    {
        ArgumentNullException.ThrowIfNull(expression);
        ArgumentNullException.ThrowIfNull(version);
        affected = false;
        string text = expression.Trim();
        if (text.Length == 0)
            return false;

        if (text[0] is '[' or '('
            && VersionRange.TryParse(text, out VersionRange? interval))
        {
            affected = interval.Satisfies(version);
            return true;
        }

        string[] alternatives = text.Split(
            "||",
            StringSplitOptions.TrimEntries);
        foreach (string alternative in alternatives)
        {
            if (alternative.Length == 0)
                return false;

            bool groupMatches = true;
            string[] constraints = alternative.Split(
                ',',
                StringSplitOptions.TrimEntries);
            foreach (string constraint in constraints)
            {
                if (!TryEvaluateConstraint(
                        constraint,
                        version,
                        out bool constraintMatches))
                {
                    return false;
                }

                groupMatches &= constraintMatches;
            }

            if (groupMatches)
            {
                affected = true;
                return true;
            }
        }

        return true;
    }

    private static bool TryEvaluateConstraint(
        string constraint,
        NuGetVersion version,
        out bool matches)
    {
        matches = false;
        string text = constraint.Trim();
        if (text == "*")
        {
            matches = true;
            return true;
        }

        string operation;
        if (text.StartsWith(">=", StringComparison.Ordinal)
            || text.StartsWith("<=", StringComparison.Ordinal)
            || text.StartsWith("!=", StringComparison.Ordinal))
        {
            operation = text[..2];
            text = text[2..].Trim();
        }
        else if (text.StartsWith('>')
            || text.StartsWith('<')
            || text.StartsWith('='))
        {
            operation = text[..1];
            text = text[1..].Trim();
        }
        else
        {
            operation = "=";
        }

        if (!NuGetVersion.TryParse(text, out NuGetVersion? boundary))
            return false;

        int comparison = VersionComparer.VersionRelease.Compare(
            version,
            boundary);
        matches = operation switch
        {
            ">=" => comparison >= 0,
            ">" => comparison > 0,
            "<=" => comparison <= 0,
            "<" => comparison < 0,
            "=" => comparison == 0,
            "!=" => comparison != 0,
            _ => false,
        };
        return true;
    }

    private static bool TryRequiredString(
        JsonElement element,
        string propertyName,
        out string? value)
    {
        value = null;
        if (!element.TryGetProperty(propertyName, out JsonElement property)
            || !TryReadString(property, out value))
        {
            return false;
        }

        return !string.IsNullOrWhiteSpace(value);
    }

    private static bool TryReadString(
        JsonElement element,
        out string? value)
    {
        value = null;
        if (element.ValueKind != JsonValueKind.String)
            return false;

        try
        {
            value = element.GetString();
            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private static bool TryUtcTimestamp(
        JsonElement element,
        string propertyName,
        out DateTimeOffset value)
    {
        value = default;
        return TryRequiredString(element, propertyName, out string? text)
            && text!.EndsWith('Z')
            && DateTimeOffset.TryParse(
                text,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal
                    | DateTimeStyles.AdjustToUniversal,
                out value)
            && value.Offset == TimeSpan.Zero;
    }

    private static bool IsGhsaId(string? value)
        => HasIdentifierShape(value, "GHSA-", 19, 4, 4, 4);

    private static bool IsCveId(string? value)
    {
        if (value is null
            || !value.StartsWith("CVE-", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        string[] parts = value.Split('-');
        return parts.Length == 3
            && parts[1].Length == 4
            && parts[1].All(char.IsAsciiDigit)
            && parts[2].Length >= 4
            && parts[2].All(char.IsAsciiDigit);
    }

    private static bool HasIdentifierShape(
        string? value,
        string prefix,
        int length,
        params int[] groups)
    {
        if (value is null
            || value.Length != length
            || !value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        string[] parts = value[prefix.Length..].Split('-');
        return parts.Length == groups.Length
            && parts.Select(
                    static part => part.All(character =>
                        char.IsAsciiLetterOrDigit(character)))
                .All(static valid => valid)
            && parts.Select(static part => part.Length)
                .SequenceEqual(groups);
    }

    private static bool TrySeverity(
        string value,
        out GitHubNuGetAdvisorySeverity severity)
    {
        severity = value.ToLowerInvariant() switch
        {
            "unknown" => GitHubNuGetAdvisorySeverity.Unknown,
            "low" => GitHubNuGetAdvisorySeverity.Low,
            "medium" => GitHubNuGetAdvisorySeverity.Medium,
            "high" => GitHubNuGetAdvisorySeverity.High,
            "critical" => GitHubNuGetAdvisorySeverity.Critical,
            _ => default,
        };
        return value.Equals("unknown", StringComparison.OrdinalIgnoreCase)
            || value.Equals("low", StringComparison.OrdinalIgnoreCase)
            || value.Equals("medium", StringComparison.OrdinalIgnoreCase)
            || value.Equals("high", StringComparison.OrdinalIgnoreCase)
            || value.Equals("critical", StringComparison.OrdinalIgnoreCase);
    }

    private sealed record ParsedAdvisory(
        GitHubNuGetAdvisoryReference Reference,
        ImmutableArray<ParsedVulnerability> Vulnerabilities);

    private sealed record ParsedVulnerability(
        string PackageId,
        string? Range,
        NuGetVersion? FirstPatchedVersion,
        bool FirstPatchedVersionMalformed);

    private readonly record struct LinkResult(string? Next, bool Invalid);

    private sealed class OperationDeadline
    {
        private readonly TimeProvider _timeProvider;
        private readonly long _startedAt;
        private readonly TimeSpan _timeout;

        internal OperationDeadline(TimeProvider timeProvider, TimeSpan timeout)
        {
            _timeProvider = timeProvider;
            _startedAt = timeProvider.GetTimestamp();
            _timeout = timeout;
        }

        internal bool IsExpired(CancellationToken timerToken) =>
            timerToken.IsCancellationRequested
            || _timeProvider.GetElapsedTime(_startedAt) >= _timeout;
    }

    private sealed class AdvisorySet
    {
        private readonly Dictionary<string, GitHubNuGetAdvisoryReference> _values =
            new(StringComparer.OrdinalIgnoreCase);

        internal void TryAdd(GitHubNuGetAdvisoryReference value) =>
            _values.TryAdd(value.GhsaId, value);

        internal ImmutableArray<GitHubNuGetAdvisoryReference> ToImmutable() =>
            [.. _values.Values.OrderByDescending(
                static value => value.PublishedAt)];
    }

    private sealed class CoordinateState(
        GitHubNuGetAdvisoryRequest.Coordinate coordinate)
    {
        internal GitHubNuGetAdvisoryRequest.Coordinate Coordinate { get; } =
            coordinate;
        internal NuGetVersion Version => Coordinate.Version;
        internal AdvisorySet Current { get; } = new();
        internal AdvisorySet Fixed { get; } = new();
    }

    private sealed class PackageState
    {
        internal PackageState(GitHubNuGetAdvisoryRequest.Package package) =>
            Coordinates = [.. package.Coordinates.Select(
                static coordinate => new CoordinateState(coordinate))];

        internal ImmutableArray<CoordinateState> Coordinates { get; }
        internal bool AcquiredUsablePage { get; set; }
        internal bool AcquisitionComplete { get; set; }
        internal bool CurrentDataValid { get; set; } = true;
        internal bool FixedDataValid { get; set; } = true;
    }

    private sealed class AcquisitionState
    {
        private readonly GitHubNuGetAdvisoryRequest _request;
        private readonly HashSet<GitHubNuGetAdvisoryFailureKind> _failures = [];

        internal AcquisitionState(GitHubNuGetAdvisoryRequest request)
        {
            _request = request;
            Packages = request.Packages.ToDictionary(
                static package => package.PackageId,
                static package => new PackageState(package),
                StringComparer.OrdinalIgnoreCase);
        }

        internal Dictionary<string, PackageState> Packages { get; }
        internal IReadOnlySet<GitHubNuGetAdvisoryFailureKind> Failures =>
            _failures;
        internal int ApiRequests { get; set; }
        internal long ResponseBytes { get; set; }

        internal void AddFailure(GitHubNuGetAdvisoryFailureKind failure) =>
            _failures.Add(failure);

        internal void MarkInvalid(
            ImmutableArray<GitHubNuGetAdvisoryRequest.Package> packages)
        {
            AddFailure(GitHubNuGetAdvisoryFailureKind.InvalidData);
            foreach (GitHubNuGetAdvisoryRequest.Package package in packages)
            {
                PackageState state = Packages[package.PackageId];
                state.CurrentDataValid = false;
                state.FixedDataValid = false;
            }
        }

        internal GitHubNuGetAdvisoryAcquisition CreateResult(
            DateTimeOffset observedAt)
        {
            var byKey = Packages.Values
                .SelectMany(static package => package.Coordinates)
                .ToDictionary(
                    static coordinate => coordinate.Coordinate.Key,
                    StringComparer.OrdinalIgnoreCase);
            var result = ImmutableArray.CreateBuilder<
                GitHubNuGetAdvisoryPackageEvidence>();
            foreach (GitHubNuGetAdvisoryRequest.Coordinate requested
                in _request.NormalizedCoordinates)
            {
                CoordinateState coordinate = byKey[requested.Key];
                PackageState package = Packages[requested.Value.PackageId];
                result.Add(new GitHubNuGetAdvisoryPackageEvidence(
                    requested.Value,
                    Availability(
                        package,
                        package.CurrentDataValid),
                    coordinate.Current.ToImmutable(),
                    Availability(
                        package,
                        package.FixedDataValid),
                    coordinate.Fixed.ToImmutable()));
            }

            return new GitHubNuGetAdvisoryAcquisition(
                result.ToImmutable(),
                [.. _failures.Order()],
                _request.Producer,
                observedAt,
                ApiRequests,
                ResponseBytes);
        }

        private static GitHubNuGetAdvisoryAvailability Availability(
            PackageState package,
            bool dataValid)
        {
            if (package.AcquisitionComplete && dataValid)
                return GitHubNuGetAdvisoryAvailability.Complete;
            return package.AcquiredUsablePage
                ? GitHubNuGetAdvisoryAvailability.Partial
                : GitHubNuGetAdvisoryAvailability.Unavailable;
        }
    }
}
