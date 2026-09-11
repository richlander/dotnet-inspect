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
        IReadOnlyList<PackageAuthorityFailure> failures,
        IReadOnlyList<ConfiguredPackageAuthority>? notFoundAuthorities = null,
        IReadOnlyList<ConfiguredPackageAuthority>? reportingAuthorities = null,
        bool selectionUsesOriginalSources = false)
    {
        Authority = authority;
        Payload = payload;
        Failures = new ReadOnlyCollection<PackageAuthorityFailure>([.. failures]);
        NotFoundAuthorities = new ReadOnlyCollection<ConfiguredPackageAuthority>(
            [.. notFoundAuthorities ?? []]);
        ReportingAuthorities = reportingAuthorities is null
            ? null
            : new ReadOnlyCollection<ConfiguredPackageAuthority>(
                [.. reportingAuthorities]);
        SelectionUsesOriginalSources = selectionUsesOriginalSources;
    }

    public ConfiguredPackageAuthority? Authority { get; }
    public AcquiredPackageSourcePayload? Payload { get; }
    public IReadOnlyList<PackageAuthorityFailure> Failures { get; }
    public IReadOnlyList<ConfiguredPackageAuthority> NotFoundAuthorities { get; }
    internal IReadOnlyList<ConfiguredPackageAuthority>? ReportingAuthorities
        { get; }
    internal bool SelectionUsesOriginalSources { get; }
}

/// <summary>
/// Acquires admitted retained payloads only through candidates issued by one
/// package-owned candidate issuer.
/// </summary>
internal sealed class PackageAcquisitionCandidatePayloadAcquirer
{
    private readonly PackageAcquisitionCandidateIssuer _issuer;
    private readonly Func<
        ConfiguredPackageAuthority,
        IPackageSourceClient> _getClient;

    public PackageAcquisitionCandidatePayloadAcquirer(
        PackageAcquisitionCandidateIssuer issuer,
        Func<ConfiguredPackageAuthority, IPackageSourceClient> getClient)
    {
        ArgumentNullException.ThrowIfNull(issuer);
        ArgumentNullException.ThrowIfNull(getClient);
        _issuer = issuer;
        _getClient = getClient;
    }

    public Task<ConfiguredPackagePayloadResult> AcquireAsync(
        PackageAcquisitionCandidate candidate,
        Func<
            ConfiguredPackageAuthority,
            PackageProducerIdentity,
            IPackageStore> createStore,
        Action<string>? log = null,
        PackagePayloadLimits? limits = null,
        CancellationToken cancellationToken = default,
        IPackagePayloadTransferPolicy? transferPolicy = null,
        NuGetOperationContext? operationContext = null) =>
        AcquireAsync(
            candidate,
            createStore,
            log,
            limits,
            cancellationToken,
            transferPolicy,
            operationContext,
            failures: [],
            selectionUsesOriginalSources: false);

    internal async Task<ConfiguredPackagePayloadResult> AcquireAsync(
        PackageAcquisitionCandidate candidate,
        Func<
            ConfiguredPackageAuthority,
            PackageProducerIdentity,
            IPackageStore> createStore,
        Action<string>? log,
        PackagePayloadLimits? limits,
        CancellationToken cancellationToken,
        IPackagePayloadTransferPolicy? transferPolicy,
        NuGetOperationContext? operationContext,
        List<PackageAuthorityFailure> failures,
        bool selectionUsesOriginalSources)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(createStore);
        ArgumentNullException.ThrowIfNull(failures);
        if (!_issuer.OwnsCandidate(candidate))
        {
            throw new InvalidOperationException(
                "The package acquisition candidate belongs to another issuer.");
        }

        using NuGetOperationContext? ownedOperation =
            operationContext is null
                ? new NuGetOperationContext(cancellationToken)
                : null;
        NuGetOperationContext operation =
            operationContext ?? ownedOperation!;
        cancellationToken = ResolveInvocationToken(
            operation,
            cancellationToken);
        var notFoundAuthorities = new List<ConfiguredPackageAuthority>();
        try
        {
            operation.ThrowIfExpired();
            List<(
                ConfiguredPackageAuthority Authority,
                IPackageSourceClient Client,
                IPackageStore Store)> entries = [];
            foreach (PackageAcquisitionAuthorityEvidence evidence in
                     PackageAcquisitionCandidateManifestAcquirer
                         .OrderAuthorities(candidate.Authorities))
            {
                operation.ThrowIfExpired();
                ConfiguredPackageAuthority authority = evidence.Authority;
                IPackageSourceClient client = _getClient(authority)
                    ?? throw new InvalidOperationException(
                        "The package source client factory returned null.");
                RequireAuthority(client.Source, authority);
                IPackageStore store = createStore(
                    authority,
                    client.Source.Producer)
                    ?? throw new InvalidOperationException(
                        "The package store factory returned null.");
                entries.Add((authority, client, store));
            }
            ConfiguredPackageAuthority[]? selectedAuthorities =
                candidate.Kind == PackageAcquisitionCandidateKind.Discovered
                    ? [.. entries.Select(item => item.Authority)]
                    : null;

            // Every authorized cache is consulted before cold acquisition.
            foreach (var (authority, client, store) in entries)
            {
                operation.ThrowIfExpired();
                AcquiredPackageSourcePayload? cached =
                    await PackagePayloadAcquisition.TryGetCachedAsync(
                        candidate.Coordinate,
                        client.Source.Producer.Key,
                        store,
                        limits,
                        log,
                        operation.OperationToken).ConfigureAwait(false);
                operation.ThrowIfExpired();
                RequireAuthority(client.Source, authority);
                if (cached is not null)
                {
                    return new(
                        authority,
                        cached,
                        failures,
                        reportingAuthorities: selectedAuthorities,
                        selectionUsesOriginalSources:
                            selectionUsesOriginalSources);
                }
            }

            foreach (var (authority, client, store) in entries)
            {
                operation.ThrowIfExpired();
                log?.Invoke(
                    $"Acquiring {candidate.Coordinate.PackageId} "
                    + $"{candidate.Coordinate.Version} from "
                    + $"{PackageSourceDisplay.ForDiagnostics(authority.Source)}.");
                try
                {
                    PackageSourcePayloadResult result =
                        await PackagePayloadAcquisition.AcquireAuthorizedAsync(
                            client,
                            candidate.Coordinate,
                            store,
                            operation,
                            log,
                            limits,
                            transferPolicy).ConfigureAwait(false);
                    operation.ThrowIfExpired();
                    RequireAuthority(client.Source, authority);
                    if (result is PackageSourcePayloadResult.Acquired acquired)
                    {
                        return new(
                            authority,
                            acquired.Payload,
                            failures,
                            notFoundAuthorities,
                            selectedAuthorities,
                            selectionUsesOriginalSources);
                    }
                    if (result is PackageSourcePayloadResult.Failed failed)
                    {
                        RequireAuthority(
                            failed.Failure.Source,
                            authority,
                            client.Source);
                        failures.Add(
                            DescribePayloadFailure(
                                authority.Source,
                                failed.Failure));
                    }
                    else if (result is PackageSourcePayloadResult.Unavailable
                             unavailable)
                    {
                        if (unavailable.IsNotFound)
                        {
                            notFoundAuthorities.Add(authority);
                        }
                        else
                        {
                            failures.Add(new PackageAuthorityFailure(
                                PackageSourceDisplay.ForDiagnostics(
                                    authority.Source),
                                PackageAuthorityFailureKind.ResponseRejected,
                                "The selected source did not supply a payload satisfying the package policy.")
                            {
                                ResultSource = client.Source,
                            });
                        }
                    }
                }
                catch (PackageSourceStreamException exception)
                {
                    RequireAuthority(
                        exception.ResultSource,
                        authority,
                        client.Source);
                    failures.Add(new PackageAuthorityFailure(
                        PackageSourceDisplay.ForDiagnostics(authority.Source),
                        ClassifySourceFailure(exception.Kind),
                        exception.Message)
                    {
                        ResultSource = exception.ResultSource,
                        Timeout = exception.Timeout,
                    });
                    if (exception.Timeout?.Kind
                        == PackageSourceTimeoutKind.Operation)
                    {
                        return new(
                            null,
                            null,
                            failures,
                            notFoundAuthorities);
                    }
                }
            }
            operation.ThrowIfExpired();
            return new(null, null, failures, notFoundAuthorities);
        }
        catch (NuGetOperationTimeoutException)
        {
            return PayloadOperationTimedOut(
                operation,
                failures,
                notFoundAuthorities);
        }
        catch (OperationCanceledException)
            when (operation.CancellationToken.IsCancellationRequested)
        {
            throw new OperationCanceledException(
                operation.CancellationToken);
        }
        catch (OperationCanceledException)
            when (operation.OperationToken.IsCancellationRequested)
        {
            return PayloadOperationTimedOut(
                operation,
                failures,
                notFoundAuthorities);
        }
    }

    private static ConfiguredPackagePayloadResult PayloadOperationTimedOut(
        NuGetOperationContext operation,
        List<PackageAuthorityFailure> failures,
        IReadOnlyList<ConfiguredPackageAuthority>? notFoundAuthorities = null)
    {
        failures.Add(new PackageAuthorityFailure(
            InertString.Empty,
            PackageAuthorityFailureKind.Timeout,
            "The package payload operation deadline expired before acquisition completed.")
        {
            Timeout = new(
                PackageSourceTimeoutKind.Operation,
                operation.OperationTimeout),
        });
        return new(null, null, failures, notFoundAuthorities);
    }

    private static PackageAuthorityFailure DescribePayloadFailure(
        PackageSource source,
        PackageSourceFailure failure) =>
        new(
            PackageSourceDisplay.ForDiagnostics(source),
            ClassifySourceFailure(failure.Kind),
            failure.Message)
        {
            SourceFailure = failure,
            ResultSource = failure.Source,
        };

    private static PackageAuthorityFailureKind ClassifySourceFailure(
        PackageSourceFailureKind kind) =>
        kind switch
        {
            PackageSourceFailureKind.AuthenticationRequired =>
                PackageAuthorityFailureKind.AuthenticationRequired,
            PackageSourceFailureKind.Timeout =>
                PackageAuthorityFailureKind.Timeout,
            PackageSourceFailureKind.Unsupported =>
                PackageAuthorityFailureKind.Unsupported,
            PackageSourceFailureKind.InvalidResponse =>
                PackageAuthorityFailureKind.InvalidResponse,
            PackageSourceFailureKind.ResponseRejected =>
                PackageAuthorityFailureKind.ResponseRejected,
            PackageSourceFailureKind.Transport =>
                PackageAuthorityFailureKind.Transport,
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };

    private static void RequireAuthority(
        PackageSourceResultIdentity result,
        ConfiguredPackageAuthority authority,
        PackageSourceResultIdentity? expectedSource = null)
    {
        if (!ReferenceEquals(
                result.Association,
                authority.Association)
            || (expectedSource is not null
                && !ReferenceEquals(result, expectedSource)))
        {
            throw new InvalidOperationException(
                "The package source result belongs to another configured authority or client.");
        }
    }

    private static CancellationToken ResolveInvocationToken(
        NuGetOperationContext operationContext,
        CancellationToken invocationToken)
    {
        if (invocationToken != default
            && invocationToken != operationContext.CancellationToken)
        {
            throw new ArgumentException(
                "The invocation token must match the operation context's caller token.",
                nameof(invocationToken));
        }

        return operationContext.CancellationToken;
    }
}
