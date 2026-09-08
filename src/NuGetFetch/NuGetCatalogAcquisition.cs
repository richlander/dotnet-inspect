using System.Collections.Immutable;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace NuGetFetch;

internal sealed partial class NuGetV3PackageSourceClient
{
    public async IAsyncEnumerable<PackageSourceOperationResult<NuGetCatalogPage>>
        AcquireCatalogAsync(
            NuGetCatalogRequest request,
            [EnumeratorCancellation] CancellationToken cancellationToken = default,
            NuGetOperationContext? operationContext = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken = operationContext?.ResolveInvocationToken(
            cancellationToken) ?? cancellationToken;
        var acquisition = new NuGetCatalogAcquisition(
            _results,
            _client,
            _endpoint,
            _credential,
            _options,
            _clientTimeout,
            request,
            cancellationToken,
            operationContext);

        IReadOnlyList<string>? endpoints = null;
        Exception? failure = null;
        try
        {
            endpoints = await acquisition.DiscoverCatalogEndpointsAsync()
                .ConfigureAwait(false);
        }
        catch (Exception exception)
            when (acquisition.TryCaptureFailure(exception, out _))
        {
            failure = exception;
        }

        if (failure is not null)
        {
            yield return acquisition.Failed(failure);
            yield break;
        }

        if (endpoints!.Count == 0)
        {
            yield return _results.FailedCatalog(
                PackageSourceFailureKind.Unsupported);
            yield break;
        }

        Exception? lastEndpointFailure = null;
        foreach (string endpoint in endpoints)
        {
            NuGetCatalogIndex? index = null;
            failure = null;
            try
            {
                NuGetCatalogDocumentResult<NuGetCatalogIndex> indexRead =
                    await acquisition.ReadCatalogIndexAsync(endpoint)
                        .ConfigureAwait(false);
                if (indexRead.Completion is not null)
                {
                    throw new NuGetCatalogResourceLimitExceededException(
                        "A Catalog acquisition bound was reached before the Catalog horizon was captured.");
                }

                index = indexRead.Value!;
            }
            catch (Exception exception)
                when (acquisition.TryCaptureFailure(exception, out _))
            {
                failure = exception;
            }

            if (failure is not null)
            {
                if (acquisition.CanFailOver(failure))
                {
                    lastEndpointFailure = failure;
                    continue;
                }

                yield return acquisition.Failed(failure);
                yield break;
            }

            ImmutableArray<NuGetCatalogPageDescriptor> pages =
                acquisition.SelectPages(index!);
            if (pages.IsEmpty)
            {
                yield return await acquisition.CreateTerminalAsync(
                    index!.Horizon,
                    acquisition.CoverageCompletion(index.Horizon))
                    .ConfigureAwait(false);
                yield break;
            }

            NuGetCatalogDocumentResult<ImmutableArray<NuGetCatalogEvent>>
                firstRead = default;
            failure = null;
            try
            {
                firstRead = await acquisition.ReadPageAsync(
                    index!.Horizon,
                    pages[0]).ConfigureAwait(false);
            }
            catch (Exception exception)
                when (acquisition.TryCaptureFailure(exception, out _))
            {
                failure = exception;
            }

            if (failure is not null)
            {
                if (acquisition.CanFailOver(failure))
                {
                    lastEndpointFailure = failure;
                    continue;
                }

                yield return acquisition.Failed(failure);
                yield break;
            }

            if (firstRead.Completion is { } firstLimit)
            {
                yield return await acquisition.CreateTerminalAsync(
                    index!.Horizon,
                    firstLimit).ConfigureAwait(false);
                yield break;
            }

            PackageSourceOperationResult<NuGetCatalogPage> first =
                await acquisition.AdmitPageAsync(
                    index!.Horizon,
                    pages,
                    pageIndex: 0,
                    firstRead.Value!).ConfigureAwait(false);
            yield return first;
            if (first.Failure is not null
                || first.Value!.Completion is not null)
                yield break;

            for (int pageIndex = 1; pageIndex < pages.Length; pageIndex++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                NuGetCatalogDocumentResult<ImmutableArray<NuGetCatalogEvent>>
                    pageRead = default;
                failure = null;
                try
                {
                    pageRead = await acquisition.ReadPageAsync(
                        index.Horizon,
                        pages[pageIndex]).ConfigureAwait(false);
                }
                catch (Exception exception)
                    when (acquisition.TryCaptureFailure(exception, out _))
                {
                    failure = exception;
                }

                if (failure is not null)
                {
                    yield return acquisition.Failed(failure);
                    yield break;
                }

                if (pageRead.Completion is { } limit)
                {
                    yield return await acquisition.CreateTerminalAsync(
                        index.Horizon,
                        limit).ConfigureAwait(false);
                    yield break;
                }

                PackageSourceOperationResult<NuGetCatalogPage> outcome =
                    await acquisition.AdmitPageAsync(
                        index.Horizon,
                        pages,
                        pageIndex,
                        pageRead.Value!).ConfigureAwait(false);
                yield return outcome;
                if (outcome.Failure is not null
                    || outcome.Value!.Completion is not null)
                    yield break;
            }

            yield break;
        }

        yield return acquisition.Failed(
            lastEndpointFailure
            ?? new NuGetSourceResponseException(
                "The package source did not provide a usable Catalog endpoint."));
    }
}

internal sealed class NuGetCatalogAcquisition
{
    private const int MaxEquivalentCatalogEndpoints = 4;
    private const int TraversalCheckpointInterval = 128;

    private static JsonDocumentOptions DocumentOptions =>
        new()
        {
            AllowDuplicateProperties = false,
            MaxDepth = 64,
        };

    private readonly PackageSourceResultFactory _results;
    private readonly HttpClient _client;
    private readonly string _serviceIndexUrl;
    private readonly PackageSourceCredential? _credential;
    private readonly NuGetFetchOptions _options;
    private readonly TimeSpan _clientTimeout;
    private readonly NuGetCatalogRequest _request;
    private readonly CancellationToken _cancellationToken;
    private readonly NuGetOperationContext? _operationContext;
    private readonly NuGetCatalogBudget _budget;
    private TimeSpan _remaining;
    private NuGetCatalogEvent? _lastEvent;

    internal NuGetCatalogAcquisition(
        PackageSourceResultFactory results,
        HttpClient client,
        Uri serviceIndex,
        PackageSourceCredential? credential,
        NuGetFetchOptions options,
        TimeSpan clientTimeout,
        NuGetCatalogRequest request,
        CancellationToken cancellationToken,
        NuGetOperationContext? operationContext)
    {
        _results = results;
        _client = client;
        _serviceIndexUrl = NuGetSourceRequest.EndpointUrl(serviceIndex);
        _credential = credential;
        _options = options;
        _clientTimeout = clientTimeout;
        _request = request;
        _cancellationToken = cancellationToken;
        _operationContext = operationContext;
        _budget = new NuGetCatalogBudget(options);
        _remaining = options.OperationTimeout;
    }

    internal async Task<IReadOnlyList<string>>
        DiscoverCatalogEndpointsAsync()
    {
        NuGetCatalogDocumentResult<IReadOnlyList<string>> result =
            await ReadDocumentAsync(
                _serviceIndexUrl,
                _credential,
                ReadServiceIndexAsync).ConfigureAwait(false);
        if (result.Completion is not null)
        {
            throw new NuGetCatalogResourceLimitExceededException(
                "A Catalog acquisition bound was reached while reading the service index.");
        }

        return result.Value!;
    }

    internal Task<NuGetCatalogDocumentResult<NuGetCatalogIndex>>
        ReadCatalogIndexAsync(string endpoint) =>
        ReadDocumentAsync(
            endpoint,
            NuGetSourceRequest.CredentialForEndpoint(
                _serviceIndexUrl,
                endpoint,
                _credential),
            (json, operation, cancellationToken) =>
                ReadCatalogIndexCoreAsync(
                    json,
                    operation,
                    cancellationToken));

    internal Task<
        NuGetCatalogDocumentResult<ImmutableArray<NuGetCatalogEvent>>>
        ReadPageAsync(
            DateTimeOffset capturedHorizon,
            NuGetCatalogPageDescriptor page) =>
        ReadDocumentAsync(
            page.Url,
            NuGetSourceRequest.CredentialForEndpoint(
                _serviceIndexUrl,
                page.Url,
                _credential),
            (json, operation, cancellationToken) =>
                ReadPageCoreAsync(
                    json,
                    capturedHorizon,
                    page,
                    operation,
                    cancellationToken));

    internal ImmutableArray<NuGetCatalogPageDescriptor> SelectPages(
        NuGetCatalogIndex index)
    {
        DateTimeOffset upper = index.Horizon < _request.ThroughInclusive
            ? index.Horizon
            : _request.ThroughInclusive;
        var selected =
            ImmutableArray.CreateBuilder<NuGetCatalogPageDescriptor>();
        foreach (NuGetCatalogPageDescriptor page in index.Pages)
        {
            if (page.CommitTimestamp <= _request.FromExclusive)
                continue;

            selected.Add(page);
            if (page.CommitTimestamp > upper)
                break;
        }

        return selected.ToImmutable();
    }

    internal NuGetCatalogCompletion CoverageCompletion(
        DateTimeOffset horizon) =>
        horizon >= _request.ThroughInclusive
            ? NuGetCatalogCompletion.WindowExhausted
            : NuGetCatalogCompletion.SourceHorizonReached;

    internal bool TryCaptureFailure(
        Exception exception,
        out PackageSourceFailureKind kind) =>
        PackageSourceOperation.TryClassify(
            exception,
            allowNotFound: false,
            out kind);

    internal bool CanFailOver(Exception exception)
    {
        _cancellationToken.ThrowIfCancellationRequested();
        if (!TryCaptureFailure(exception, out PackageSourceFailureKind kind)
            || kind == PackageSourceFailureKind.AuthenticationRequired
            || exception is NuGetOperationTimeoutException
            || !_budget.CanStartRequest
            || !_budget.CanReadDecodedByte)
        {
            return false;
        }

        return kind is PackageSourceFailureKind.Timeout
            or PackageSourceFailureKind.InvalidResponse
            or PackageSourceFailureKind.ResponseRejected
            or PackageSourceFailureKind.Transport;
    }

    internal PackageSourceOperationResult<NuGetCatalogPage> Failed(
        Exception exception)
    {
        _cancellationToken.ThrowIfCancellationRequested();
        if (!TryCaptureFailure(exception, out PackageSourceFailureKind kind))
            throw exception;
        return _results.FailedCatalog(kind);
    }

    internal async Task<PackageSourceOperationResult<NuGetCatalogPage>>
        CreateTerminalAsync(
            DateTimeOffset horizon,
            NuGetCatalogCompletion completion)
    {
        try
        {
            return await CreatePageAsync(
                horizon,
                [],
                completion).ConfigureAwait(false);
        }
        catch (Exception exception)
            when (TryCaptureFailure(exception, out _))
        {
            return Failed(exception);
        }
    }

    internal async Task<PackageSourceOperationResult<NuGetCatalogPage>>
        AdmitPageAsync(
            DateTimeOffset horizon,
            ImmutableArray<NuGetCatalogPageDescriptor> selectedPages,
            int pageIndex,
            ImmutableArray<NuGetCatalogEvent> events)
    {
        try
        {
            ValidatePageOrder(events);
            _budget.AdmitPage(events.Length);
            NuGetCatalogCompletion? completion =
                pageIndex == selectedPages.Length - 1
                    ? CoverageCompletion(horizon)
                    : _budget.PagesAcquired >= _options.MaxCatalogPages
                        ? NuGetCatalogCompletion.PageLimitReached
                        : !_budget.CanStartRequest
                            ? NuGetCatalogCompletion.RequestLimitReached
                            : !_budget.CanReadDecodedByte
                                ? NuGetCatalogCompletion.DecodedByteLimitReached
                                : null;
            return await CreatePageAsync(
                horizon,
                events,
                completion).ConfigureAwait(false);
        }
        catch (Exception exception)
            when (TryCaptureFailure(exception, out _))
        {
            return Failed(exception);
        }
    }

    private async Task<PackageSourceOperationResult<NuGetCatalogPage>>
        CreatePageAsync(
            DateTimeOffset horizon,
            ImmutableArray<NuGetCatalogEvent> events,
            NuGetCatalogCompletion? completion)
    {
        return await RunStepAsync(
            operation =>
            {
                NuGetCatalogPage page = _results.CatalogPage(
                    _request,
                    events,
                    horizon,
                    _budget.PagesAcquired,
                    _budget.HttpAttempts,
                    _budget.DecodedBytes,
                    _budget.InWindowEventCount,
                    completion,
                    operation);
                return Task.FromResult(
                    _results.SucceededCatalog(page, operation));
            }).ConfigureAwait(false);
    }

    private void ValidatePageOrder(
        ImmutableArray<NuGetCatalogEvent> events)
    {
        if (events.IsEmpty)
            return;

        NuGetCatalogEvent first = events[0];
        if (_lastEvent is not null
            && _lastEvent.CommitTimestamp > first.CommitTimestamp)
        {
            throw new NuGetSourceResponseException(
                "Catalog pages did not produce chronological event order.");
        }

        _lastEvent = events[^1];
    }

    private async Task<NuGetCatalogDocumentResult<T>> ReadDocumentAsync<T>(
        string url,
        PackageSourceCredential? credential,
        Func<
            Stream,
            NuGetOperationDeadline,
            CancellationToken,
            ValueTask<T>> deserialize)
        =>
        await RunStepAsync(
            operation => ReadDocumentWithRetryAsync(
                url,
                credential,
                deserialize,
                operation)).ConfigureAwait(false);

    private async Task<NuGetCatalogDocumentResult<T>>
        ReadDocumentWithRetryAsync<T>(
            string url,
            PackageSourceCredential? credential,
            Func<
                Stream,
                NuGetOperationDeadline,
                CancellationToken,
                ValueTask<T>> deserialize,
            NuGetOperationDeadline operation)
    {
        for (int retry = 0; ; retry++)
        {
            operation.ThrowIfExpired();
            if (!_budget.CanReadDecodedByte)
            {
                return NuGetCatalogDocumentResult<T>.Limited(
                    NuGetCatalogCompletion.DecodedByteLimitReached);
            }

            if (!_budget.TryBeginRequest())
            {
                return NuGetCatalogDocumentResult<T>.Limited(
                    NuGetCatalogCompletion.RequestLimitReached);
            }

            try
            {
                T value = await operation.RunRequestAsync(
                    async requestToken =>
                    {
                        using HttpRequestMessage request =
                            NuGetHttpRequest
                                .CreateGetPreservingPathAndQuery(url);
                        NuGetSourceRequest.ApplyCredential(
                            request,
                            credential);
                        using HttpResponseMessage response =
                            await _client.SendAsync(
                                request,
                                HttpCompletionOption.ResponseHeadersRead,
                                requestToken).ConfigureAwait(false);
                        response.EnsureSuccessStatusCode();
                        return await NuGetMetadataReader.ReadResponseAsync(
                            response,
                            (stream, cancellationToken) =>
                                deserialize(
                                    _budget.LimitDecodedBytes(stream),
                                    operation,
                                    cancellationToken),
                            _options,
                            operation.RequestTimeout,
                            requestToken).ConfigureAwait(false);
                    }).ConfigureAwait(false);
                return NuGetCatalogDocumentResult<T>.Succeeded(value);
            }
            catch (NuGetCatalogDecodedByteLimitExceededException)
            {
                return NuGetCatalogDocumentResult<T>.Limited(
                    NuGetCatalogCompletion.DecodedByteLimitReached);
            }
            catch (Exception exception)
                when (retry < NuGetHttpRetry.MaximumRetries
                    && NuGetHttpRetry.IsTransient(exception))
            {
                if (!_budget.CanStartRequest)
                {
                    return NuGetCatalogDocumentResult<T>.Limited(
                        NuGetCatalogCompletion.RequestLimitReached);
                }

                if (!_budget.CanReadDecodedByte)
                {
                    return NuGetCatalogDocumentResult<T>.Limited(
                        NuGetCatalogCompletion.DecodedByteLimitReached);
                }

                await NuGetHttpRetry.DelayAsync(operation, retry)
                    .ConfigureAwait(false);
            }
        }
    }

    private async Task<T> RunStepAsync<T>(
        Func<NuGetOperationDeadline, Task<T>> action)
    {
        _cancellationToken.ThrowIfCancellationRequested();
        long started = Stopwatch.GetTimestamp();
        try
        {
            using NuGetOperationDeadline operation = CreateOperation();
            return await action(operation).ConfigureAwait(false);
        }
        finally
        {
            if (_operationContext is null)
                _remaining -= Stopwatch.GetElapsedTime(started);
        }
    }

    private NuGetOperationDeadline CreateOperation()
    {
        if (_operationContext is not null)
        {
            return _operationContext.CreateDeadline(
                _clientTimeout,
                _cancellationToken,
                _results.Source);
        }

        if (_remaining <= TimeSpan.Zero)
        {
            throw new NuGetOperationTimeoutException(
                _options.OperationTimeout,
                new OperationCanceledException(
                    "NuGet Catalog operation deadline expired."));
        }

        return new NuGetOperationDeadline(
            _options with { OperationTimeout = _remaining },
            _clientTimeout,
            _cancellationToken,
            _results.Source);
    }

    private static async ValueTask<IReadOnlyList<string>>
        ReadServiceIndexAsync(
            Stream json,
            NuGetOperationDeadline operation,
            CancellationToken cancellationToken)
    {
        using JsonDocument document = await ParseDocumentAsync(
            json,
            cancellationToken).ConfigureAwait(false);
        JsonElement root = RequiredObject(
            document.RootElement,
            "The package source service index must be an object.");
        _ = RequiredText(root, "version", "service index");
        JsonElement resources = RequiredArray(
            root,
            "resources",
            "service index");

        var endpoints = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        bool malformedSupportedResource = false;
        int observed = 0;
        foreach (JsonElement resource in resources.EnumerateArray())
        {
            if (resource.ValueKind != JsonValueKind.Object)
            {
                observed++;
                await CheckTraversalAsync(
                    observed,
                    operation,
                    cancellationToken).ConfigureAwait(false);
                continue;
            }

            bool validTypeShape = TryHasCatalogType(
                Optional(resource, "@type"),
                out bool hasCatalogType);
            if (hasCatalogType)
            {
                if (!validTypeShape
                    || Optional(resource, "@id") is not
                        { ValueKind: JsonValueKind.String } id
                    || ReadText(
                        id,
                        "service index Catalog resource")
                        is not { } declared
                    || !TryNormalizeAdvertisedUrl(
                        declared,
                        out string normalized))
                {
                    malformedSupportedResource = true;
                }
                else if (seen.Add(normalized)
                    && endpoints.Count < MaxEquivalentCatalogEndpoints)
                {
                    endpoints.Add(normalized);
                }
            }

            observed++;
            await CheckTraversalAsync(
                observed,
                operation,
                cancellationToken).ConfigureAwait(false);
        }

        if (endpoints.Count == 0 && malformedSupportedResource)
        {
            throw new NuGetSourceResponseException(
                "The package source service index advertised an unusable Catalog endpoint.");
        }

        operation.ThrowIfExpired();
        return endpoints;
    }

    private async ValueTask<NuGetCatalogIndex>
        ReadCatalogIndexCoreAsync(
            Stream json,
            NuGetOperationDeadline operation,
            CancellationToken cancellationToken)
    {
        using JsonDocument document = await ParseDocumentAsync(
            json,
            cancellationToken).ConfigureAwait(false);
        JsonElement root = RequiredObject(
            document.RootElement,
            "The Catalog index must be an object.");
        _ = RequiredText(root, "commitId", "Catalog index");
        DateTimeOffset horizon = RequiredTimestamp(
            root,
            "commitTimeStamp",
            "Catalog index");
        _ = RequiredNonNegativeInteger(root, "count", "Catalog index");
        JsonElement items = RequiredArray(root, "items", "Catalog index");
        var pages =
            ImmutableArray.CreateBuilder<NuGetCatalogPageDescriptor>(
                items.GetArrayLength());
        var urls = new HashSet<string>(StringComparer.Ordinal);
        int observed = 0;
        foreach (JsonElement item in items.EnumerateArray())
        {
            JsonElement page = RequiredObject(
                item,
                "A Catalog index page descriptor must be an object.");
            string url = RequiredUrl(
                page,
                "@id",
                "Catalog index page descriptor");
            string commitId = RequiredText(
                page,
                "commitId",
                "Catalog index page descriptor");
            DateTimeOffset commitTimestamp = RequiredTimestamp(
                page,
                "commitTimeStamp",
                "Catalog index page descriptor");
            _ = RequiredNonNegativeInteger(
                page,
                "count",
                "Catalog index page descriptor");
            if (commitTimestamp > horizon)
            {
                throw Invalid(
                    "A Catalog page descriptor exceeded the captured Catalog horizon.");
            }

            if (!urls.Add(url))
            {
                throw Invalid(
                    "The Catalog index repeated a page URL.");
            }

            pages.Add(
                new NuGetCatalogPageDescriptor(
                    url,
                    commitId,
                    commitTimestamp));
            observed++;
            await CheckTraversalAsync(
                observed,
                operation,
                cancellationToken).ConfigureAwait(false);
        }

        NuGetCatalogPageDescriptor[] sorted = pages.ToArray();
        Array.Sort(
            sorted,
            static (left, right) =>
            {
                int timestamp = left.CommitTimestamp.CompareTo(
                    right.CommitTimestamp);
                return timestamp != 0
                    ? timestamp
                    : StringComparer.Ordinal.Compare(
                        left.Url,
                        right.Url);
            });
        if (sorted.Length == 0)
        {
            if (horizon != DateTimeOffset.MinValue)
            {
                throw Invalid(
                    "An empty Catalog index advertised a nonempty horizon.");
            }
        }
        else if (sorted[^1].CommitTimestamp != horizon)
        {
            throw Invalid(
                "The Catalog index horizon did not match its latest page descriptor.");
        }

        operation.ThrowIfExpired();
        return new NuGetCatalogIndex(
            horizon,
            ImmutableArray.CreateRange(sorted));
    }

    private async ValueTask<ImmutableArray<NuGetCatalogEvent>>
        ReadPageCoreAsync(
            Stream json,
            DateTimeOffset capturedHorizon,
            NuGetCatalogPageDescriptor descriptor,
            NuGetOperationDeadline operation,
            CancellationToken cancellationToken)
    {
        using JsonDocument document = await ParseDocumentAsync(
            json,
            cancellationToken).ConfigureAwait(false);
        JsonElement root = RequiredObject(
            document.RootElement,
            "A Catalog page must be an object.");
        _ = RequiredText(root, "commitId", "Catalog page");
        DateTimeOffset pageHorizon = RequiredTimestamp(
            root,
            "commitTimeStamp",
            "Catalog page");
        if (pageHorizon < descriptor.CommitTimestamp)
        {
            throw new NuGetCatalogStalePageException(
                "A Catalog page preceded its advertised page horizon.");
        }

        _ = RequiredNonNegativeInteger(root, "count", "Catalog page");
        _ = RequiredUrl(root, "parent", "Catalog page");

        JsonElement items = RequiredArray(root, "items", "Catalog page");
        var allEvents =
            ImmutableArray.CreateBuilder<NuGetCatalogEvent>(
                items.GetArrayLength());
        var urls = new HashSet<string>(StringComparer.Ordinal);
        int observed = 0;
        foreach (JsonElement value in items.EnumerateArray())
        {
            JsonElement item = RequiredObject(
                value,
                "A Catalog page item must be an object.");
            string leafUrl = RequiredUrl(
                item,
                "@id",
                "Catalog page item");
            if (!urls.Add(leafUrl))
                throw Invalid("A Catalog page repeated a leaf URL.");

            NuGetCatalogEventKind kind = RequiredEventKind(item);
            string commitId = RequiredText(
                item,
                "commitId",
                "Catalog page item");
            DateTimeOffset commitTimestamp = RequiredTimestamp(
                item,
                "commitTimeStamp",
                "Catalog page item");
            if (commitTimestamp > pageHorizon)
            {
                throw Invalid(
                    "A Catalog page item exceeded its page horizon.");
            }

            string packageId = RequiredText(
                item,
                "nuget:id",
                "Catalog page item");
            string version = RequiredText(
                item,
                "nuget:version",
                "Catalog page item");
            PackageSourceCoordinate coordinate;
            try
            {
                coordinate = PackageSourceCoordinate.Create(
                    packageId,
                    version);
            }
            catch (ArgumentException exception)
            {
                throw Invalid(
                    "A Catalog page item contained an invalid package coordinate.",
                    exception);
            }

            allEvents.Add(
                _results.CatalogEvent(
                    coordinate,
                    packageId,
                    version,
                    leafUrl,
                    commitId,
                    commitTimestamp,
                    kind));
            observed++;
            await CheckTraversalAsync(
                observed,
                operation,
                cancellationToken).ConfigureAwait(false);
        }

        NuGetCatalogEvent[] sorted = allEvents.ToArray();
        Array.Sort(sorted, CompareEvents);
        DateTimeOffset upper =
            _request.ThroughInclusive < capturedHorizon
                ? _request.ThroughInclusive
                : capturedHorizon;
        var selected = ImmutableArray.CreateBuilder<NuGetCatalogEvent>();
        foreach (NuGetCatalogEvent item in sorted)
        {
            operation.ThrowIfExpired();
            if (item.CommitTimestamp > _request.FromExclusive
                && item.CommitTimestamp <= upper)
            {
                selected.Add(item);
            }
        }

        operation.ThrowIfExpired();
        return selected.ToImmutable();
    }

    private static int CompareEvents(
        NuGetCatalogEvent left,
        NuGetCatalogEvent right)
    {
        int timestamp = left.CommitTimestamp.CompareTo(
            right.CommitTimestamp);
        return timestamp != 0
            ? timestamp
            : StringComparer.Ordinal.Compare(
                left.LeafUrl,
                right.LeafUrl);
    }

    private static bool TryHasCatalogType(
        JsonElement type,
        out bool hasCatalogType)
    {
        hasCatalogType = false;
        if (type.ValueKind == JsonValueKind.String)
        {
            hasCatalogType = ReadText(
                type,
                "service index resource type")?.Equals(
                "Catalog/3.0.0",
                StringComparison.OrdinalIgnoreCase) == true;
            return true;
        }

        if (type.ValueKind != JsonValueKind.Array)
            return false;

        bool valid = true;
        foreach (JsonElement item in type.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String
                || ReadText(
                    item,
                    "service index resource type")
                    is not { } value)
            {
                valid = false;
                continue;
            }

            if (value.Equals(
                "Catalog/3.0.0",
                StringComparison.OrdinalIgnoreCase))
            {
                hasCatalogType = true;
            }
        }

        return valid;
    }

    private static NuGetCatalogEventKind RequiredEventKind(
        JsonElement item)
    {
        string kind = RequiredText(
            item,
            "@type",
            "Catalog page item");
        return kind switch
        {
            "nuget:PackageDetails" =>
                NuGetCatalogEventKind.Details,
            "nuget:PackageDelete" =>
                NuGetCatalogEventKind.Delete,
            _ => throw Invalid(
                "A Catalog page item had an unsupported event kind."),
        };
    }

    private static JsonElement RequiredObject(
        JsonElement value,
        string message) =>
        value.ValueKind == JsonValueKind.Object
            ? value
            : throw Invalid(message);

    private static JsonElement RequiredArray(
        JsonElement parent,
        string name,
        string document)
    {
        JsonElement value = Optional(parent, name);
        if (value.ValueKind != JsonValueKind.Array)
        {
            throw Invalid(
                $"The {document} must contain an '{name}' array.");
        }

        return value;
    }

    private static string RequiredText(
        JsonElement parent,
        string name,
        string document)
    {
        JsonElement value = Optional(parent, name);
        if (value.ValueKind != JsonValueKind.String)
        {
            throw Invalid(
                $"The {document} must contain nonempty '{name}' text.");
        }

        string? text = ReadText(value, document);
        if (string.IsNullOrWhiteSpace(text))
        {
            throw Invalid(
                $"The {document} must contain nonempty '{name}' text.");
        }

        return text;
    }

    private static string? ReadText(
        JsonElement value,
        string document)
    {
        try
        {
            return value.GetString();
        }
        catch (InvalidOperationException exception)
            when (value.ValueKind == JsonValueKind.String)
        {
            throw Invalid(
                $"The {document} contained invalid UTF-16 text.",
                exception);
        }
    }

    private static async ValueTask<JsonDocument> ParseDocumentAsync(
        Stream json,
        CancellationToken cancellationToken)
    {
        try
        {
            return await JsonDocument.ParseAsync(
                json,
                DocumentOptions,
                cancellationToken).ConfigureAwait(false);
        }
        catch (InvalidOperationException exception)
        {
            throw Invalid(
                "NuGet Catalog metadata contained invalid UTF-16 text.",
                exception);
        }
    }

    private static string RequiredUrl(
        JsonElement parent,
        string name,
        string document)
    {
        string declared = RequiredText(parent, name, document);
        if (!TryNormalizeAdvertisedUrl(
                declared,
                out string normalized))
        {
            throw Invalid(
                $"The {document} contained an unusable '{name}' URL.");
        }

        return normalized;
    }

    private static bool TryNormalizeAdvertisedUrl(
        string declared,
        out string normalized)
    {
        normalized = "";
        if (!NuGetSourceRequest.TryEndpointUrl(
                declared,
                out string endpoint)
            || !NuGetHttpRequest.TryCreatePreservingPathAndQuery(
                endpoint,
                out Uri? parsed)
            || parsed is null
            || parsed.UserInfo.Length > 0)
        {
            return false;
        }

        normalized = endpoint;
        return true;
    }

    private static DateTimeOffset RequiredTimestamp(
        JsonElement parent,
        string name,
        string document)
    {
        string text = RequiredText(parent, name, document);
        if (!DateTimeOffset.TryParse(
                text,
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out DateTimeOffset value)
            || value.Offset != TimeSpan.Zero
            || text.AsSpan().Trim().Length != text.Length
            || !HasExplicitUtcSuffix(text))
        {
            throw Invalid(
                $"The {document} contained an invalid UTC '{name}' timestamp.");
        }

        return value.ToUniversalTime();
    }

    private static bool HasExplicitUtcSuffix(string value) =>
        value.EndsWith("Z", StringComparison.OrdinalIgnoreCase)
        || value.EndsWith("+00:00", StringComparison.Ordinal);

    private static long RequiredNonNegativeInteger(
        JsonElement parent,
        string name,
        string document)
    {
        JsonElement value = Optional(parent, name);
        if (value.ValueKind != JsonValueKind.Number
            || !value.TryGetInt64(out long count)
            || count < 0)
        {
            throw Invalid(
                $"The {document} contained an invalid '{name}' count.");
        }

        return count;
    }

    private static JsonElement Optional(
        JsonElement parent,
        string name) =>
        parent.ValueKind == JsonValueKind.Object
            && parent.TryGetProperty(name, out JsonElement value)
                ? value
                : default;

    private static async ValueTask CheckTraversalAsync(
        int observed,
        NuGetOperationDeadline operation,
        CancellationToken cancellationToken)
    {
        if (observed % TraversalCheckpointInterval != 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            operation.ThrowIfExpired();
            return;
        }

        cancellationToken.ThrowIfCancellationRequested();
        operation.ThrowIfExpired();
        if (OperatingSystem.IsBrowser())
            await Task.Yield();
        cancellationToken.ThrowIfCancellationRequested();
        operation.ThrowIfExpired();
    }

    private static JsonException Invalid(
        string message,
        Exception? innerException = null) =>
        new(message, innerException);
}

internal sealed record NuGetCatalogIndex(
    DateTimeOffset Horizon,
    ImmutableArray<NuGetCatalogPageDescriptor> Pages);

internal sealed record NuGetCatalogPageDescriptor(
    string Url,
    string CommitId,
    DateTimeOffset CommitTimestamp);

internal readonly record struct NuGetCatalogDocumentResult<T>(
    T Value,
    NuGetCatalogCompletion? Completion)
{
    internal static NuGetCatalogDocumentResult<T> Succeeded(T value) =>
        new(value, Completion: null);

    internal static NuGetCatalogDocumentResult<T> Limited(
        NuGetCatalogCompletion completion) =>
        new(Value: default!, completion);
}

internal sealed class NuGetCatalogBudget
{
    private readonly int _maximumRequests;
    private readonly long _maximumDecodedBytes;

    internal NuGetCatalogBudget(NuGetFetchOptions options)
    {
        _maximumRequests = options.MaxCatalogHttpAttempts;
        _maximumDecodedBytes = options.MaxCatalogDecodedBytes;
    }

    internal int PagesAcquired { get; private set; }
    internal int HttpAttempts { get; private set; }
    internal long DecodedBytes { get; private set; }
    internal long InWindowEventCount { get; private set; }
    internal bool CanStartRequest => HttpAttempts < _maximumRequests;
    internal bool CanReadDecodedByte =>
        DecodedBytes < _maximumDecodedBytes;

    internal bool TryBeginRequest()
    {
        if (!CanStartRequest)
            return false;
        HttpAttempts++;
        return true;
    }

    internal Stream LimitDecodedBytes(Stream stream) =>
        new NuGetCatalogDecodedByteStream(stream, this);

    internal void AdmitPage(int eventCount)
    {
        PagesAcquired++;
        InWindowEventCount += eventCount;
    }

    private sealed class NuGetCatalogDecodedByteStream(
        Stream inner,
        NuGetCatalogBudget budget) : Stream
    {
        public override bool CanRead => inner.CanRead;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count) =>
            Read(buffer.AsSpan(offset, count));

        public override int Read(Span<byte> buffer)
        {
            if (buffer.IsEmpty)
                return 0;

            int read = inner.Read(Limit(buffer));
            AccountFor(read);
            return read;
        }

        public override int ReadByte()
        {
            int value = inner.ReadByte();
            AccountFor(value < 0 ? 0 : 1);
            return value;
        }

        public override Task<int> ReadAsync(
            byte[] buffer,
            int offset,
            int count,
            CancellationToken cancellationToken) =>
            ReadAsync(
                buffer.AsMemory(offset, count),
                cancellationToken).AsTask();

        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            if (buffer.IsEmpty)
                return 0;

            int read = await inner.ReadAsync(
                Limit(buffer),
                cancellationToken).ConfigureAwait(false);
            AccountFor(read);
            return read;
        }

        private Span<byte> Limit(Span<byte> buffer)
        {
            long remaining =
                budget._maximumDecodedBytes - budget.DecodedBytes;
            int allowed = remaining > 0
                ? (int)Math.Min(buffer.Length, remaining)
                : 1;
            return buffer[..allowed];
        }

        private Memory<byte> Limit(Memory<byte> buffer)
        {
            long remaining =
                budget._maximumDecodedBytes - budget.DecodedBytes;
            int allowed = remaining > 0
                ? (int)Math.Min(buffer.Length, remaining)
                : 1;
            return buffer[..allowed];
        }

        private void AccountFor(int read)
        {
            if (read == 0)
                return;

            if (budget.DecodedBytes >= budget._maximumDecodedBytes)
                throw new NuGetCatalogDecodedByteLimitExceededException();
            budget.DecodedBytes += read;
        }

        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException();
        public override void SetLength(long value) =>
            throw new NotSupportedException();
        public override void Write(
            byte[] buffer,
            int offset,
            int count) =>
            throw new NotSupportedException();
    }
}

internal sealed class NuGetCatalogDecodedByteLimitExceededException()
    : IOException(
        "NuGet Catalog metadata exceeded the aggregate decoded-byte limit.");
