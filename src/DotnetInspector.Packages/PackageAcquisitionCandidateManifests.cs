using System.Collections.ObjectModel;
using InertText;
using NuGetFetch;

namespace DotnetInspector.Packages;

/// <summary>An exact manifest and its configured authority, or attributed failures.</summary>
public sealed class ConfiguredPackageManifestResult
{
    internal ConfiguredPackageManifestResult(
        ConfiguredPackageAuthority? authority,
        PackageSourceManifest? manifest,
        IReadOnlyList<PackageAuthorityFailure> failures)
    {
        Authority = authority;
        Manifest = manifest;
        Failures = new ReadOnlyCollection<PackageAuthorityFailure>(
            [.. failures]);
    }

    public ConfiguredPackageAuthority? Authority { get; }
    public PackageSourceManifest? Manifest { get; }
    public IReadOnlyList<PackageAuthorityFailure> Failures { get; }
}

/// <summary>
/// Acquires exact manifests only through candidates issued by one package-owned
/// candidate issuer.
/// </summary>
public sealed class PackageAcquisitionCandidateManifestAcquirer
{
    private readonly PackageAcquisitionCandidateIssuer _issuer;
    private readonly Func<
        ConfiguredPackageAuthority,
        IPackageSourceClient> _getClient;

    public PackageAcquisitionCandidateManifestAcquirer(
        PackageAcquisitionCandidateIssuer issuer,
        Func<ConfiguredPackageAuthority, IPackageSourceClient> getClient)
    {
        ArgumentNullException.ThrowIfNull(issuer);
        ArgumentNullException.ThrowIfNull(getClient);
        _issuer = issuer;
        _getClient = getClient;
    }

    public async Task<ConfiguredPackageManifestResult> AcquireAsync(
        PackageAcquisitionCandidate candidate,
        CancellationToken cancellationToken = default,
        NuGetOperationContext? operationContext = null)
    {
        ArgumentNullException.ThrowIfNull(candidate);
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
        var failures = new List<PackageAuthorityFailure>();
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            operation.ThrowIfExpired();
            foreach (PackageAcquisitionAuthorityEvidence evidence in
                     OrderAuthorities(candidate.Authorities))
            {
                cancellationToken.ThrowIfCancellationRequested();
                operation.ThrowIfExpired();
                ConfiguredPackageAuthority authority = evidence.Authority;
                IPackageSourceClient client = _getClient(authority)
                    ?? throw new InvalidOperationException(
                        "The package source client factory returned null.");
                RequireAuthority(client.Source, authority);

                PackageSourceOperationResult<PackageSourceManifest> outcome =
                    await client.GetManifestAsync(
                        candidate.Coordinate.PackageId,
                        candidate.Coordinate.Version,
                        cancellationToken,
                        operation).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                operation.ThrowIfExpired();
                if (outcome.Failure is { } failure)
                {
                    RequireAuthority(
                        failure.Source,
                        authority,
                        client.Source);
                    failures.Add(DescribeFailure(authority.Source, failure));
                    continue;
                }

                PackageSourceManifest manifest = outcome.Value
                    ?? throw new InvalidOperationException(
                        "The package source manifest operation returned neither a value nor a failure.");
                RequireAuthority(
                    manifest.Source,
                    authority,
                    client.Source);
                if (manifest.Coordinate != candidate.Coordinate)
                {
                    failures.Add(new PackageAuthorityFailure(
                        PackageSourceDisplay.ForDiagnostics(authority.Source),
                        PackageAuthorityFailureKind.InvalidResponse,
                        "The package source returned a manifest for another coordinate.")
                    {
                        ResultSource = manifest.Source,
                    });
                    continue;
                }

                return new(authority, manifest, failures);
            }

            operation.ThrowIfExpired();
            return new(null, null, failures);
        }
        catch (NuGetOperationTimeoutException)
        {
            return OperationTimedOut(operation, failures);
        }
        catch (OperationCanceledException)
            when (operation.CancellationToken.IsCancellationRequested)
        {
            throw new OperationCanceledException(operation.CancellationToken);
        }
        catch (OperationCanceledException)
            when (operation.OperationToken.IsCancellationRequested)
        {
            return OperationTimedOut(operation, failures);
        }
    }

    internal static IEnumerable<PackageAcquisitionAuthorityEvidence>
        OrderAuthorities(
            IReadOnlyList<PackageAcquisitionAuthorityEvidence> authorities) =>
        authorities
            .OrderBy(evidence =>
                evidence.Authority.Kind
                    == ConfiguredPackageAuthorityKind.LocalFolder
                    ? 0
                    : 1)
            .ThenBy(
                evidence => evidence.Authority.Source.Url,
                StringComparer.Ordinal);

    internal static PackageAuthorityFailure DescribeFailure(
        PackageSource source,
        PackageSourceFailure failure)
    {
        InertString sourceDisplay =
            PackageSourceDisplay.ForDiagnostics(source);
        return new PackageAuthorityFailure(
            sourceDisplay,
            ClassifyFailure(failure.Kind),
            failure.Kind == PackageSourceFailureKind.NotFound
                ? $"Package source {sourceDisplay} did not supply a manifest for the requested coordinate."
                : failure.Message)
        {
            SourceFailure = failure,
            ResultSource = failure.Source,
        };
    }

    internal static ConfiguredPackageManifestResult OperationTimedOut(
        NuGetOperationContext operation,
        List<PackageAuthorityFailure> failures)
    {
        failures.Add(new PackageAuthorityFailure(
            InertString.Empty,
            PackageAuthorityFailureKind.Timeout,
            "The package manifest operation deadline expired before acquisition completed.")
        {
            Timeout = new(
                PackageSourceTimeoutKind.Operation,
                operation.OperationTimeout),
        });
        return new(null, null, failures);
    }

    private static PackageAuthorityFailureKind ClassifyFailure(
        PackageSourceFailureKind kind) => kind switch
    {
        PackageSourceFailureKind.AuthenticationRequired =>
            PackageAuthorityFailureKind.AuthenticationRequired,
        PackageSourceFailureKind.Timeout =>
            PackageAuthorityFailureKind.Timeout,
        PackageSourceFailureKind.Unsupported =>
            PackageAuthorityFailureKind.Unsupported,
        PackageSourceFailureKind.InvalidResponse =>
            PackageAuthorityFailureKind.InvalidResponse,
        PackageSourceFailureKind.ResponseRejected
            or PackageSourceFailureKind.NotFound =>
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
