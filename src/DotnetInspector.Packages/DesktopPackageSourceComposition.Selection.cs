using InertText;
using NuGet.Versioning;
using NuGetFetch;

namespace DotnetInspector.Packages;

public sealed partial class DesktopPackageSourceComposition
{
    /// <summary>
    /// Selects a coordinate from complete current discovery and acquires it only
    /// from authorities whose admitted observations reported that coordinate.
    /// An external operation remains caller-owned through payload consumption.
    /// </summary>
    public async Task<ConfiguredPackagePayloadResult> AcquireSelectedAsync(
        string packageId,
        string? versionSelector,
        Func<ConfiguredPackageAuthority, PackageProducerIdentity, IPackageStore> createStore,
        NuGetSourceOptions? sourceOptions = null,
        Action<string>? log = null,
        bool includePrerelease = false,
        string? rangeAddress = null,
        CancellationToken cancellationToken = default,
        NuGetOperationContext? operationContext = null,
        PackagePayloadLimits? limits = null,
        IPackagePayloadTransferPolicy? transferPolicy = null)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        ArgumentNullException.ThrowIfNull(createStore);
        if (!PackageExtractor.IsValidPackageId(packageId))
            return InvalidSelection("The package ID must use the NuGet package ID grammar.");
        if (sourceOptions?.AuthorizedSourceKeys is not null
            || sourceOptions?.ResolvedSources is not null)
            return InvalidSelection("Selected payload acquisition requires configured sources, not legacy producer or resolved-source restrictions.");

        PackageVersionRange? range = null;
        string? prefix = null;
        if (versionSelector?.Contains("..", StringComparison.Ordinal) == true)
        {
            if (!PackageVersionRange.TryParse(
                    $"{packageId}@{versionSelector}", out range, out string? rangeError))
                return InvalidSelection(rangeError ?? "A valid package version range is required.");
            if (range!.PackageId != packageId)
                return InvalidSelection("The version selector must contain only the range endpoints.");
            if (!IsRangeAddressSyntaxValid(rangeAddress))
                return InvalidSelection("A package range address must be an exact version, #N, first, or last.");
        }
        else
        {
            if (rangeAddress is not null)
                return InvalidSelection("A range address requires a package version range.");
            if (versionSelector?.Contains('*') == true)
                prefix = versionSelector.Replace("*", "");
            else if (!string.IsNullOrEmpty(versionSelector)
                     && !versionSelector.Equals("latest", StringComparison.OrdinalIgnoreCase))
                return InvalidSelection("Selected payload acquisition requires latest, a wildcard, or a range; use pinned acquisition for an exact version.");
        }

        using NuGetOperationContext? ownedOperation = operationContext is null
            ? CreateOperationContext(cancellationToken)
            : null;
        NuGetOperationContext operation = operationContext ?? ownedOperation!;
        cancellationToken = operation.ResolveInvocationToken(cancellationToken);
        var failures = new List<PackageAuthorityFailure>();
        try
        {
            operation.ThrowIfExpired();
            PackageVersionDiscoveryResult discovery = await GetVersionsAsync(
                packageId,
                includePrerelease || prefix is not null || range?.IncludesPrerelease == true,
                limit: null, sourceOptions, log, cancellationToken,
                includeUnlisted: false, operationContext: operation).ConfigureAwait(false);
            failures.AddRange(discovery.Failures);
            if (discovery.State != PackageVersionDiscoveryState.Authoritative)
                return new(null, null, failures);
            operation.ThrowIfExpired();

            PackageSourceCoordinate? coordinate;
            if (range is not null)
            {
                PackageVersionVector vector;
                try
                {
                    vector = PackageVersionVector.Create(
                        range,
                        discovery.Candidates.Select(candidate => candidate.Observation.Coordinate.Version),
                        includePrerelease);
                }
                catch (ArgumentException exception)
                {
                    return InvalidSelection(exception.Message);
                }
                if (!vector.TrySelect(rangeAddress!, out PackageVersionAddress? address, out string? addressError))
                    return InvalidSelection(addressError!);
                coordinate = PackageSourceCoordinate.Create(packageId, address!.Version.ToNormalizedString());
            }
            else
            {
                coordinate = discovery.Candidates
                    .Select(candidate => candidate.Observation.Coordinate)
                    .Where(candidate => prefix is null || candidate.Version.StartsWith(
                        prefix, StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(candidate => NuGetVersion.Parse(candidate.Version))
                    .FirstOrDefault();
            }

            operation.ThrowIfExpired();
            if (coordinate is null)
                return new(null, null, failures);

            return await AcquireDiscoveredAsync(
                discovery, coordinate, createStore, sourceOptions, log, operation,
                limits, transferPolicy).ConfigureAwait(false);
        }
        catch (NuGetOperationTimeoutException)
        {
            return PayloadOperationTimedOut(operation, failures);
        }
        catch (OperationCanceledException) when (operation.CancellationToken.IsCancellationRequested)
        {
            throw new OperationCanceledException(operation.CancellationToken);
        }
        catch (OperationCanceledException) when (operation.OperationToken.IsCancellationRequested)
        {
            return PayloadOperationTimedOut(operation, failures);
        }
    }

    internal Task<ConfiguredPackagePayloadResult> AcquireDiscoveredAsync(
        PackageVersionDiscoveryResult discovery,
        PackageSourceCoordinate coordinate,
        Func<ConfiguredPackageAuthority, PackageProducerIdentity, IPackageStore> createStore,
        NuGetSourceOptions? sourceOptions,
        Action<string>? log,
        NuGetOperationContext operation,
        PackagePayloadLimits? limits = null,
        IPackagePayloadTransferPolicy? transferPolicy = null)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        ArgumentNullException.ThrowIfNull(createStore);
        var failures = new List<PackageAuthorityFailure>(discovery.Failures);
        if (discovery.State != PackageVersionDiscoveryState.Authoritative)
            return Task.FromResult(new ConfiguredPackagePayloadResult(null, null, failures));
        if (sourceOptions?.AuthorizedSourceKeys is not null
            || sourceOptions?.ResolvedSources is not null)
            return Task.FromResult(InvalidSelection(
                "Selected payload acquisition requires configured sources, not legacy producer or resolved-source restrictions."));

        var reporters = new HashSet<ConfiguredPackageAuthority>(ReferenceEqualityComparer.Instance);
        foreach (ConfiguredPackageCandidateObservation candidate in discovery.Candidates)
        {
            if (candidate.Observation.Coordinate == coordinate)
                reporters.Add(candidate.Authority);
        }
        return AcquireCoordinateAsync(
            coordinate.PackageId, coordinate, createStore, sourceOptions, log, operation,
            limits, transferPolicy, failures, reporters);
    }

    private static bool IsRangeAddressSyntaxValid(string? address) =>
        !string.IsNullOrWhiteSpace(address)
        && (address.Equals("first", StringComparison.OrdinalIgnoreCase)
            || address.Equals("last", StringComparison.OrdinalIgnoreCase)
            || (address[0] == '#'
                ? int.TryParse(address.AsSpan(1), out int ordinal) && ordinal > 0
                : NuGetVersion.TryParse(address, out _)));

    private static ConfiguredPackagePayloadResult InvalidSelection(string message) =>
        new(null, null,
        [
            new PackageAuthorityFailure(InertString.Empty, PackageAuthorityFailureKind.Input, message),
        ]);
}
