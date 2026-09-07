using DotnetInspector.Packages;
using NuGetFetch;

namespace DotnetInspector.PackageQueries;

/// <summary>
/// Host-neutral dependency-candidate source over explicit package
/// authorization and caller-owned source clients.
/// </summary>
public sealed class AuthorizedPackageDependencyCandidateSource
    : IPackageDependencyCandidateSource
{
    private readonly IPackageSourceAuthorization _authorization;
    private readonly Func<
        ConfiguredPackageAuthority,
        IPackageSourceClient> _getClient;
    private readonly PackageAcquisitionCandidateIssuer _issuer = new();

    public AuthorizedPackageDependencyCandidateSource(
        IPackageSourceAuthorization authorization,
        Func<ConfiguredPackageAuthority, IPackageSourceClient> getClient)
    {
        ArgumentNullException.ThrowIfNull(authorization);
        ArgumentNullException.ThrowIfNull(getClient);
        _authorization = authorization;
        _getClient = getClient;
    }

    public ValueTask<PackageAcquisitionCandidateResult>
        ResolvePinnedCandidateAsync(
        PackageSourceCoordinate coordinate,
        CancellationToken cancellationToken = default,
        NuGetOperationContext? operationContext = null)
    {
        ArgumentNullException.ThrowIfNull(coordinate);
        using NuGetOperationContext? ownedOperation =
            operationContext is null
                ? new NuGetOperationContext(cancellationToken)
                : null;
        NuGetOperationContext operation =
            operationContext ?? ownedOperation!;
        cancellationToken = ResolveInvocationToken(
            operation,
            cancellationToken);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            operation.ThrowIfExpired();
            PackageSourceAuthorization authorization =
                _authorization.AuthorizeSourcesFor(
                    coordinate.PackageId);
            cancellationToken.ThrowIfCancellationRequested();
            operation.ThrowIfExpired();
            PackageAcquisitionCandidateResult result =
                _issuer.ResolvePinnedCandidate(
                    authorization,
                    coordinate);
            cancellationToken.ThrowIfCancellationRequested();
            operation.ThrowIfExpired();
            return ValueTask.FromResult(result);
        }
        catch (NuGetOperationTimeoutException)
        {
            return ValueTask.FromResult(
                _issuer.CreateIncompletePinnedCandidate(
                    [
                        new PackageAuthorityFailure(
                            InertText.InertString.Empty,
                            PackageAuthorityFailureKind.Timeout,
                            "The package candidate operation deadline expired before authorization completed.")
                        {
                            Timeout = new(
                                    PackageSourceTimeoutKind.Operation,
                                    operation.OperationTimeout),
                        },
                    ]));
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw new OperationCanceledException(cancellationToken);
        }
    }

    public async Task<PackageVersionDiscoveryResult>
        DiscoverDependencyVersionsAsync(
            string packageId,
            CancellationToken cancellationToken = default,
            NuGetOperationContext? operationContext = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId);
        using NuGetOperationContext? ownedOperation =
            operationContext is null
                ? new NuGetOperationContext(cancellationToken)
                : null;
        NuGetOperationContext operation =
            operationContext ?? ownedOperation!;
        cancellationToken = ResolveInvocationToken(
            operation,
            cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        PackageSourceAuthorization authorization =
            _authorization.AuthorizeSourcesFor(packageId);
        var outcomes = new List<
            PackageSourceOperationResult<PackageVersionResult>>(
                authorization.Authorities.Count);
        for (int index = 0;
             index < authorization.Authorities.Count;
             index++)
        {
            ConfiguredPackageAuthority authority =
                authorization.Authorities[index];
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                operation.ThrowIfExpired();
            }
            catch (NuGetOperationTimeoutException)
            {
                return _issuer
                    .CreateIncompleteDependencyVersionDiscovery(
                        packageId,
                        authorization,
                        outcomes,
                        OperationTimeoutFailures(
                            authorization,
                            index,
                            operation));
            }

            IPackageSourceClient client = _getClient(authority)
                ?? throw new InvalidOperationException(
                    "The package source client factory returned null.");
            if (!ReferenceEquals(
                    client.Source.Association,
                    authority.Association))
            {
                throw new InvalidOperationException(
                    "The package source client belongs to another configured authority.");
            }

            PackageSourceOperationResult<PackageVersionResult> outcome;
            try
            {
                outcome = await client.GetVersionsAsync(
                    packageId,
                    cancellationToken,
                    operation).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                operation.ThrowIfExpired();
            }
            catch (NuGetOperationTimeoutException)
            {
                return _issuer
                    .CreateIncompleteDependencyVersionDiscovery(
                        packageId,
                        authorization,
                        outcomes,
                        OperationTimeoutFailures(
                            authorization,
                            index,
                            operation));
            }
            outcomes.Add(outcome);
        }

        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            operation.ThrowIfExpired();
        }
        catch (NuGetOperationTimeoutException)
        {
            return _issuer.CreateIncompleteDependencyVersionDiscovery(
                packageId,
                authorization,
                outcomes,
                [
                    OperationTimeoutFailure(
                        authority: null,
                        operation),
                ]);
        }
        PackageVersionDiscoveryResult discovery =
            _issuer.CreateDependencyVersionDiscovery(
            packageId,
            authorization,
            outcomes);
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            operation.ThrowIfExpired();
        }
        catch (NuGetOperationTimeoutException)
        {
            return _issuer.CreateIncompleteDependencyVersionDiscovery(
                discovery,
                [
                    OperationTimeoutFailure(
                        authority: null,
                        operation),
                ]);
        }

        return discovery;
    }

    private static IReadOnlyList<PackageAuthorityFailure>
        OperationTimeoutFailures(
            PackageSourceAuthorization authorization,
            int firstUnsettledAuthority,
            NuGetOperationContext operation)
    {
        var failures = new List<PackageAuthorityFailure>(
            authorization.Authorities.Count - firstUnsettledAuthority);
        for (int index = firstUnsettledAuthority;
             index < authorization.Authorities.Count;
             index++)
        {
            failures.Add(OperationTimeoutFailure(
                authorization.Authorities[index],
                operation));
        }

        return failures;
    }

    private static PackageAuthorityFailure OperationTimeoutFailure(
        ConfiguredPackageAuthority? authority,
        NuGetOperationContext operation) =>
        new(
            authority is null
                ? InertText.InertString.Empty
                : PackageSourceDisplay.ForDiagnostics(authority.Source),
            PackageAuthorityFailureKind.Timeout,
            authority is null
                ? "The package candidate operation deadline expired before discovery could be published."
                : "An authorized package source was not settled before the package candidate operation deadline.")
        {
            Timeout = new(
                PackageSourceTimeoutKind.Operation,
                operation.OperationTimeout),
        };

    private static CancellationToken ResolveInvocationToken(
        NuGetOperationContext? operationContext,
        CancellationToken invocationToken)
    {
        if (operationContext is null)
            return invocationToken;
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
