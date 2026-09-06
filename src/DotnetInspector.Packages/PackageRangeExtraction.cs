using DotnetInspector.Core;
using NuGetFetch;

namespace DotnetInspector.Packages;

/// <summary>
/// A desktop range whose immutable version vector and configured-authority discovery
/// remain associated for its lifetime. Payload extractions are serialized and caller-owned;
/// disposing the range does not remove successful extraction results.
/// </summary>
public sealed class PackageRangeExtraction : IAsyncDisposable
{
    private readonly HttpClient _client;
    private readonly ConfiguredPackageExtractionSession _session;
    private readonly PackageVersionDiscoveryResult _discovery;
    private readonly NuGetSourceOptions? _sourceOptions;
    private readonly Action<string>? _log;
    private readonly string _tempDirPrefix;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _disposed;

    private PackageRangeExtraction(
        HttpClient client, ConfiguredPackageExtractionSession session,
        PackageVersionDiscoveryResult discovery, PackageVersionVector vector,
        NuGetSourceOptions? sourceOptions, Action<string>? log, string tempDirPrefix)
    {
        _client = client;
        _session = session;
        _discovery = discovery;
        Vector = vector;
        _sourceOptions = sourceOptions;
        _log = log;
        _tempDirPrefix = tempDirPrefix;
    }

    public PackageVersionVector Vector { get; }

    internal static async Task<PackageRangeExtraction> OpenAsync(
        HttpClient client, PackageVersionRange range, Action<string>? log,
        string tempDirPrefix, NuGetSourceOptions? sourceOptions,
        bool includePrerelease, Func<DesktopPackageSourceComposition>? createComposition)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(range);
        if (HttpClientFactory.IsOffline)
            throw new InvalidOperationException("Configured-authority range extraction requires online mode.");
        if (!PackageExtractor.IsValidPackageId(range.PackageId))
            throw new ArgumentException("The package ID must use the NuGet package ID grammar.", nameof(range));
        if (sourceOptions?.AuthorizedSourceKeys is not null
            || sourceOptions?.ResolvedSources is not null)
            throw new ArgumentException(
                "Range extraction requires configured sources, not legacy producer or resolved-source restrictions.",
                nameof(sourceOptions));

        sourceOptions = sourceOptions is null ? null : sourceOptions with
        {
            Sources = [.. sourceOptions.Sources],
            AdditionalSources = [.. sourceOptions.AdditionalSources],
        };
        var session = new ConfiguredPackageExtractionSession(
            client.Timeout, tempDirPrefix, createComposition);
        try
        {
            PackageVersionDiscoveryResult discovery;
            using (FeedFailureTelemetry.Scope())
            {
                discovery = await session.DiscoverRangeAsync(
                    range, sourceOptions, log, includePrerelease).ConfigureAwait(false);
            }
            if (discovery.State != PackageVersionDiscoveryState.Authoritative)
            {
                string causes = string.Join(Environment.NewLine,
                    discovery.Failures.Select(failure => $"{failure.Authority}: {failure.Message}"));
                throw new InvalidOperationException(
                    $"Package '{range.PackageId}' range discovery is {discovery.State.ToString().ToLowerInvariant()}; "
                    + $"complete discovery is required. {causes}");
            }

            PackageVersionVector vector = PackageVersionVector.Create(
                range,
                discovery.Candidates.Select(candidate => candidate.Observation.Coordinate.Version),
                includePrerelease);
            return new(client, session, discovery, vector, sourceOptions, log, tempDirPrefix);
        }
        catch
        {
            await session.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>
    /// Extracts an exact version, one-based #N address, first, or last from the retained
    /// discovery. Successful results remain valid after this range is disposed.
    /// </summary>
    public async Task<PackageExtractionOutcome> ExtractAsync(string addressSelector)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (HttpClientFactory.IsOffline)
                return PackageExtractionOutcome.Error("Configured-authority range extraction requires online mode.");
            if (!Vector.TrySelect(addressSelector, out PackageVersionAddress? address, out string? error))
                return PackageExtractionOutcome.Error(error!);

            PackageExtractionOutcome selected;
            using (FeedFailureTelemetry.Scope())
            {
                selected = await _session.AcquireDiscoveredAsync(
                    _discovery,
                    PackageSourceCoordinate.Create(Vector.PackageId, address!.Version.ToNormalizedString()),
                    _sourceOptions, _log).ConfigureAwait(false);
            }
            if (!selected.IsSuccess)
                return selected;

            return await PackageExtractor.ExtractPackageCoreAsync(
                _client, Vector.PackageId, _log, _tempDirPrefix, _sourceOptions,
                selected.Result!.Version, forceLatest: false, includePrerelease: false,
                _session, selected).ConfigureAwait(false);
        }
        finally
        {
            _session.CleanupAttempt();
            _gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_disposed)
                return;
            _disposed = true;
            await _session.DisposeAsync().ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }
}
