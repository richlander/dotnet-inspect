using NuGetFetch;

namespace DotnetInspector.Packages;

internal sealed class PinnedPackageExtractionSession(
    TimeSpan requestTimeout,
    string tempDirPrefix,
    Func<DesktopPackageSourceComposition>? createComposition) : IAsyncDisposable
{
    private readonly Dictionary<ConfiguredPackageAuthority, IPackageStore> _stores = [];
    private DesktopPackageSourceComposition? _composition;
    private NuGetOperationContext? _operation;
    private string? _temporaryRoot;

    public async Task<PackageExtractionOutcome> AcquireAsync(
        string packageId, string version,
        NuGetSourceOptions? sourceOptions, Action<string>? log)
    {
        TimeSpan timeout = requestTimeout == Timeout.InfiniteTimeSpan
            ? NuGetFetchOptions.DefaultRequestTimeout
            : requestTimeout;
        _composition ??= createComposition?.Invoke() ?? new DesktopPackageSourceComposition(timeout);
        _operation ??= _composition.CreateOperationContext();
        ConfiguredPackagePayloadResult result = await _composition.AcquirePinnedAsync(
            packageId, version, GetStore, sourceOptions, log,
            operationContext: _operation).ConfigureAwait(false);

        foreach (PackageAuthorityFailure failure in result.Failures)
            log?.Invoke($"{failure.Authority}: {failure.Message}");
        if (result.Payload is not { } payload)
        {
            string reason = result.Failures.Count == 0
                ? "No eligible source supplied this exact coordinate."
                : string.Join(Environment.NewLine,
                    result.Failures.Select(failure => $"{failure.Authority}: {failure.Message}"));
            return PackageExtractionOutcome.Error(
                $"Package '{packageId}' version '{version}' could not be acquired. {reason}");
        }

        return new PackageExtractionResult(
            payload.Content.RootPath
                ?? throw new InvalidOperationException("Desktop package acquisition requires filesystem content."),
            TempDir: null,
            payload.Coordinate.PackageId,
            payload.Coordinate.Version,
            payload.Content.NupkgPath,
            FromCache: payload.Origin == PackagePayloadOrigin.Cache,
            payload.ProducerKey)
        {
            Authority = result.Authority,
            AcquiredPayload = payload,
        };
    }

    public PackageExtractionResult Complete(PackageExtractionResult result)
    {
        PackageExtractionResult completed = result with { TempDir = _temporaryRoot ?? result.TempDir };
        _temporaryRoot = null;
        return completed;
    }

    private IPackageStore GetStore(ConfiguredPackageAuthority authority, PackageProducerIdentity producer)
    {
        if (!_stores.TryGetValue(authority, out IPackageStore? store))
        {
            store = new AuthorityScopedFileSystemPackageStore(
                authority, producer,
                () => _temporaryRoot ??= Directory.CreateTempSubdirectory(tempDirPrefix).FullName);
            _stores.Add(authority, store);
        }
        return store;
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (_composition is not null)
                await _composition.DisposeAsync().ConfigureAwait(false);
        }
        finally
        {
            _operation?.Dispose();
            PackageExtractor.Cleanup(_temporaryRoot);
        }
    }
}
