using System.Collections.ObjectModel;
using InertText;
using NuGetFetch;

namespace DotnetInspector.Packages;

/// <summary>An exact payload and its configured authority, or attributed failures.</summary>
public sealed class ConfiguredPackagePayloadResult
{
    internal ConfiguredPackagePayloadResult(
        ConfiguredPackageAuthority? authority,
        AcquiredPackageSourcePayload? payload,
        IReadOnlyList<PackageAuthorityFailure> failures)
    {
        Authority = authority;
        Payload = payload;
        Failures = new ReadOnlyCollection<PackageAuthorityFailure>([.. failures]);
    }

    public ConfiguredPackageAuthority? Authority { get; }
    public AcquiredPackageSourcePayload? Payload { get; }
    public IReadOnlyList<PackageAuthorityFailure> Failures { get; }
}

public sealed partial class DesktopPackageSourceComposition
{
    /// <summary>
    /// Acquires a caller-pinned coordinate from its eligible authorities.
    /// The store factory must return a store scoped to the supplied authority.
    /// An external operation remains caller-owned through payload consumption.
    /// </summary>
    public async Task<ConfiguredPackagePayloadResult> AcquirePinnedAsync(
        string packageId,
        string version,
        Func<ConfiguredPackageAuthority, PackageProducerIdentity, IPackageStore> createStore,
        NuGetSourceOptions? sourceOptions = null,
        Action<string>? log = null,
        CancellationToken cancellationToken = default,
        NuGetOperationContext? operationContext = null,
        PackagePayloadLimits? limits = null,
        IPackagePayloadTransferPolicy? transferPolicy = null)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        ArgumentNullException.ThrowIfNull(createStore);
        var failures = new List<PackageAuthorityFailure>();
        if (!PackageExtractor.IsValidPackageId(packageId)
            || !PackageExtractor.TryNormalizePackageVersion(version, out string normalizedVersion))
        {
            failures.Add(new PackageAuthorityFailure(
                InertString.Empty, PackageAuthorityFailureKind.Input,
                "Payload acquisition requires a valid package ID and an exact version."));
            return new(null, null, failures);
        }

        using NuGetOperationContext? ownedOperation = operationContext is null
            ? CreateOperationContext(cancellationToken)
            : null;
        NuGetOperationContext operation = operationContext ?? ownedOperation!;
        _ = operation.ResolveInvocationToken(cancellationToken);
        PackageSourceCoordinate coordinate = PackageSourceCoordinate.Create(packageId, normalizedVersion);

        try
        {
            operation.ThrowIfExpired();
            IReadOnlyList<PackageSource> sources = ResolveEligibleSources(
                packageId, sourceOptions, failures);
            List<(AuthorityEntry Entry, IPackageStore Store)> entries = [];
            foreach (PackageSource source in sources
                         .OrderBy(source => LocalPackageSourceIdentity.IsLocalSource(source.Url) ? 0 : 1)
                         .ThenBy(source => source.Url, StringComparer.Ordinal))
            {
                operation.ThrowIfExpired();
                if (TryGetEligibleAuthority(source, failures) is not { } entry)
                    continue;
                RequireAuthority(entry.Client.Source, entry);
                IPackageStore store = createStore(entry.Authority, entry.Client.Source.Producer);
                entries.Add((entry, store));
            }

            // Every authorized cache is consulted before cold acquisition.
            // Stable consultation order is not configured declaration precedence.
            foreach (var (entry, store) in entries)
            {
                operation.ThrowIfExpired();
                AcquiredPackageSourcePayload? cached =
                    await PackagePayloadAcquisition.TryGetCachedAsync(
                        coordinate, entry.Client.Source.Producer.Key, store,
                        limits, log, operation.OperationToken).ConfigureAwait(false);
                operation.ThrowIfExpired();
                if (cached is not null)
                    return new(entry.Authority, cached, failures);
            }

            foreach (var (entry, store) in entries)
            {
                operation.ThrowIfExpired();
                log?.Invoke(
                    $"Acquiring {coordinate.PackageId} {coordinate.Version} from "
                    + $"{PackageSourceDisplay.ForDiagnostics(entry.Source)}.");
                try
                {
                    PackageSourcePayloadResult result =
                        await PackagePayloadAcquisition.AcquireAuthorizedAsync(
                            entry.Client, coordinate, store, operation,
                            log, limits, transferPolicy).ConfigureAwait(false);
                    operation.ThrowIfExpired();
                    RequireAuthority(entry.Client.Source, entry);
                    if (result is PackageSourcePayloadResult.Acquired acquired)
                        return new(entry.Authority, acquired.Payload, failures);
                    if (result is PackageSourcePayloadResult.Failed failed)
                    {
                        RequireAuthority(failed.Failure.Source, entry);
                        failures.Add(DescribePayloadFailure(entry.Source, failed.Failure));
                    }
                    else if (result is PackageSourcePayloadResult.Unavailable { IsNotFound: false })
                    {
                        failures.Add(new PackageAuthorityFailure(
                            PackageSourceDisplay.ForDiagnostics(entry.Source),
                            PackageAuthorityFailureKind.ResponseRejected,
                            "The selected source did not supply a payload satisfying the package policy.")
                        {
                            ResultSource = entry.Client.Source,
                        });
                    }
                }
                catch (PackageSourceStreamException exception)
                {
                    RequireAuthority(exception.ResultSource, entry);
                    failures.Add(new PackageAuthorityFailure(
                        PackageSourceDisplay.ForDiagnostics(entry.Source),
                        ClassifySourceFailure(exception.Kind), exception.Message)
                    {
                        ResultSource = exception.ResultSource,
                        Timeout = exception.Timeout,
                    });
                    if (exception.Timeout?.Kind == PackageSourceTimeoutKind.Operation)
                        return new(null, null, failures);
                }
            }
            operation.ThrowIfExpired();
            return new(null, null, failures);
        }
        catch (NuGetOperationTimeoutException)
        {
            return TimedOut();
        }
        catch (OperationCanceledException) when (operation.CancellationToken.IsCancellationRequested)
        {
            throw new OperationCanceledException(operation.CancellationToken);
        }
        catch (OperationCanceledException) when (operation.OperationToken.IsCancellationRequested)
        {
            return TimedOut();
        }

        ConfiguredPackagePayloadResult TimedOut()
        {
            failures.Add(new PackageAuthorityFailure(
                InertString.Empty, PackageAuthorityFailureKind.Timeout,
                "The package payload operation deadline expired before acquisition completed.")
            {
                Timeout = new(PackageSourceTimeoutKind.Operation, operation.OperationTimeout),
            });
            return new(null, null, failures);
        }
    }

    private static PackageAuthorityFailure DescribePayloadFailure(
        PackageSource source, PackageSourceFailure failure) =>
        new(PackageSourceDisplay.ForDiagnostics(source),
            ClassifySourceFailure(failure.Kind), failure.Message)
        {
            SourceFailure = failure,
            ResultSource = failure.Source,
        };

    private static PackageAuthorityFailureKind ClassifySourceFailure(PackageSourceFailureKind kind) =>
        kind switch
        {
            PackageSourceFailureKind.AuthenticationRequired => PackageAuthorityFailureKind.AuthenticationRequired,
            PackageSourceFailureKind.Timeout => PackageAuthorityFailureKind.Timeout,
            PackageSourceFailureKind.Unsupported => PackageAuthorityFailureKind.Unsupported,
            PackageSourceFailureKind.InvalidResponse => PackageAuthorityFailureKind.InvalidResponse,
            PackageSourceFailureKind.ResponseRejected => PackageAuthorityFailureKind.ResponseRejected,
            PackageSourceFailureKind.Transport => PackageAuthorityFailureKind.Transport,
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
}
