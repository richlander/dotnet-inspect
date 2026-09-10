using DotnetInspector.Packages;
using DotnetInspector.Queries;
using InertText;
using NuGetFetch;

namespace DotnetInspect.Cli.Commands;

internal sealed class ConfiguredPackageRootPayloadProvider
    : IPackageRootPayloadProvider, IAsyncDisposable
{
    readonly DesktopPackageSourceComposition _composition;
    readonly NuGetSourceOptions? _sourceOptions;

    internal ConfiguredPackageRootPayloadProvider(
        TimeSpan requestTimeout,
        NuGetSourceOptions? sourceOptions)
    {
        _composition = new DesktopPackageSourceComposition(requestTimeout);
        _sourceOptions = sourceOptions;
    }

    public async ValueTask<PackageRootPayloadResult> GetPayloadAsync(
        PackageSourceCoordinate coordinate,
        string? requiredProducerKey,
        PackagePayloadLimits limits,
        CancellationToken cancellationToken)
    {
        ConfiguredPackagePayloadResult result =
            await _composition.AcquirePinnedAsync(
                coordinate.PackageId,
                coordinate.Version,
                static (_, _) => new InMemoryPackageStore(),
                _sourceOptions,
                cancellationToken: cancellationToken,
                limits: limits,
                requiredProducerKey: requiredProducerKey).ConfigureAwait(false);
        if (result.Payload is { } payload)
            return new PackageRootPayloadResult.Available(payload);

        PackageAuthorityFailure? first = result.Failures.FirstOrDefault();
        ConfiguredPackageAuthority? firstNotFound =
            result.NotFoundAuthorities.FirstOrDefault();
        InertString producer = first?.Authority
            ?? (firstNotFound is null
                ? new InertString(
                    TextPolicy.Field,
                    "configured package sources")
                : PackageSourceDisplay.ForDiagnostics(firstNotFound.Source));
        string message = result.Failures.Count == 0
            ? result.NotFoundAuthorities.Count == 0
                ? "No configured package source supplied the selected package archive."
                : "The selected package was not found on "
                    + string.Join(
                        ", ",
                        result.NotFoundAuthorities.Select(authority =>
                            PackageSourceDisplay.ForDiagnostics(authority.Source)))
                    + "."
            : string.Join(
                "; ",
                result.Failures.Select(failure => failure.Message));
        PackageRootAcquisitionFailureKind failureKind =
            requiredProducerKey is not null
            && result.Failures.Any(failure =>
                failure.IsRequiredProducerUnavailable)
                ? PackageRootAcquisitionFailureKind.ProducerNotAuthorized
                : PackageRootAcquisitionFailureKind.PackageUnavailable;
        return new PackageRootPayloadResult.Unavailable(
            producer,
            message,
            failureKind,
            result.Failures.Count == 1
                ? first?.SourceFailure?.Kind
                : result.Failures.Count == 0
                    && result.NotFoundAuthorities.Count > 0
                    ? PackageSourceFailureKind.NotFound
                : null);
    }

    public ValueTask DisposeAsync() => _composition.DisposeAsync();
}
