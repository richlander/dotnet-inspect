using InertText;
using NuGetFetch;

namespace DotnetInspector.Packages;

public sealed partial class DesktopPackageSourceComposition
{
    /// <summary>
    /// Acquires the exact manifest for one candidate-authorized coordinate from its
    /// admitted authorities, consulted in the same stable order payload acquisition
    /// uses. The candidate must have been issued by this composition; this is the
    /// package-owned exact manifest capability the package dependency traversal query
    /// composes for its recursive expansion, and it never downloads a package archive.
    /// </summary>
    public async Task<ConfiguredPackageManifestResult> AcquireCandidateManifestAsync(
        PackageAcquisitionCandidate candidate,
        CancellationToken cancellationToken = default,
        NuGetOperationContext? operationContext = null)
    {
        ObjectDisposedException.ThrowIf(
            Volatile.Read(ref _disposed) != 0,
            this);
        ArgumentNullException.ThrowIfNull(candidate);
        if (!candidate.HasIssuer(_candidateIssuer))
        {
            throw new InvalidOperationException(
                "The package acquisition candidate belongs to another source composition.");
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
            foreach (PackageAcquisitionAuthorityEvidence evidence in
                candidate.Authorities
                    .OrderBy(evidence =>
                        evidence.Authority.Kind
                            == ConfiguredPackageAuthorityKind.LocalFolder
                            ? 0
                            : 1)
                    .ThenBy(
                        evidence => evidence.Authority.Source.Url,
                        StringComparer.Ordinal))
            {
                operation.ThrowIfExpired();
                ConfiguredPackageAuthority authority = evidence.Authority;
                if (!_authoritiesByAssociation.TryGetValue(
                        authority.Association,
                        out AuthorityEntry? currentAuthority)
                    || !ReferenceEquals(
                        currentAuthority.Authority,
                        authority))
                {
                    throw new InvalidOperationException(
                        "The package acquisition candidate refers to an inactive configured authority.");
                }

                PackageSourceOperationResult<PackageSourceManifest> outcome =
                    await GetManifestAsync(
                        authority,
                        candidate.Coordinate,
                        cancellationToken,
                        operation).ConfigureAwait(false);
                operation.ThrowIfExpired();
                if (outcome.Failure is { } failure)
                {
                    failures.Add(
                        DescribeManifestFailure(authority.Source, failure));
                    continue;
                }

                PackageSourceManifest manifest = outcome.Value
                    ?? throw new InvalidOperationException(
                        "The package source manifest operation returned neither a value nor a failure.");
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
            return ManifestOperationTimedOut(operation, failures);
        }
        catch (OperationCanceledException)
            when (operation.CancellationToken.IsCancellationRequested)
        {
            throw new OperationCanceledException(operation.CancellationToken);
        }
        catch (OperationCanceledException)
            when (operation.OperationToken.IsCancellationRequested)
        {
            return ManifestOperationTimedOut(operation, failures);
        }
    }

    /// <summary>
    /// Describes one manifest-source failure, including the <c>NotFound</c> case a
    /// payload failure never carries.
    /// </summary>
    private static PackageAuthorityFailure DescribeManifestFailure(
        PackageSource source,
        PackageSourceFailure failure)
    {
        if (failure.Kind == PackageSourceFailureKind.NotFound)
        {
            InertString sourceDisplay = PackageSourceDisplay.ForDiagnostics(source);
            return new PackageAuthorityFailure(
                sourceDisplay,
                PackageAuthorityFailureKind.ResponseRejected,
                $"Package source {sourceDisplay} did not supply a manifest for the requested coordinate.")
            {
                SourceFailure = failure,
                ResultSource = failure.Source,
            };
        }

        return DescribePayloadFailure(source, failure);
    }

    private static ConfiguredPackageManifestResult ManifestOperationTimedOut(
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
}
