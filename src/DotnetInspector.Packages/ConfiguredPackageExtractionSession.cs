using NuGetFetch;

namespace DotnetInspector.Packages;

internal sealed class ConfiguredPackageExtractionSession(
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
        DesktopPackageSourceComposition composition = GetComposition();
        _operation ??= composition.CreateOperationContext();
        ConfiguredPackagePayloadResult result = await composition.AcquirePinnedAsync(
            packageId, version, GetStore, sourceOptions, log,
            operationContext: _operation).ConfigureAwait(false);

        return ConvertResult(result,
            $"Package '{packageId}' version '{version}'",
            "No eligible source supplied this exact coordinate.", log);
    }

    public async Task<PackageExtractionOutcome> AcquireSelectedAsync(
        string packageId, string? versionSelector,
        NuGetSourceOptions? sourceOptions, Action<string>? log,
        bool includePrerelease, string? rangeAddress)
    {
        DesktopPackageSourceComposition composition = GetComposition();
        _operation ??= composition.CreateOperationContext();
        ConfiguredPackagePayloadResult result = await composition.AcquireSelectedAsync(
            packageId, versionSelector, GetStore, sourceOptions, log,
            includePrerelease, rangeAddress, operationContext: _operation).ConfigureAwait(false);

        return ConvertResult(result,
            $"Package '{packageId}' selection '{(string.IsNullOrEmpty(versionSelector) ? "latest" : versionSelector)}'",
            "No eligible reporting source supplied a matching payload.", log);
    }

    private DesktopPackageSourceComposition GetComposition() =>
        _composition ??= createComposition?.Invoke() ?? new DesktopPackageSourceComposition(
            requestTimeout == Timeout.InfiniteTimeSpan
                ? NuGetFetchOptions.DefaultRequestTimeout
                : requestTimeout);

    private static PackageExtractionOutcome ConvertResult(
        ConfiguredPackagePayloadResult result, string request,
        string noMatch, Action<string>? log)
    {
        foreach (PackageAuthorityFailure failure in result.Failures)
            log?.Invoke($"{failure.Authority}: {failure.Message}");
        if (result.Payload is not { } payload)
        {
            string reason = result.Failures.Count == 0
                ? noMatch
                : string.Join(Environment.NewLine,
                    result.Failures.Select(failure => $"{failure.Authority}: {failure.Message}"));
            return PackageExtractionOutcome.Error(
                $"{request} could not be acquired. {reason}");
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
