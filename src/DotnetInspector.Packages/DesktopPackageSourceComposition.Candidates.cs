using InertText;
using NuGetFetch;

namespace DotnetInspector.Packages;

public sealed partial class DesktopPackageSourceComposition
{
    /// <summary>
    /// Authorizes one caller-pinned exact coordinate without enumerating peer
    /// versions or acquiring payload bytes.
    /// </summary>
    public PackageAcquisitionCandidateResult ResolvePinnedCandidate(
        PackageSourceCoordinate coordinate,
        NuGetSourceOptions? sourceOptions = null,
        CancellationToken cancellationToken = default,
        NuGetOperationContext? operationContext = null)
    {
        ObjectDisposedException.ThrowIf(
            Volatile.Read(ref _disposed) != 0,
            this);
        ArgumentNullException.ThrowIfNull(coordinate);
        cancellationToken = operationContext?.ResolveInvocationToken(
            cancellationToken) ?? cancellationToken;
        var failures = new List<PackageAuthorityFailure>();
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            operationContext?.ThrowIfExpired();
            IReadOnlyList<PackageSource> sources = ResolveEligibleSources(
                coordinate.PackageId,
                sourceOptions,
                failures);
            var authorities = new List<ConfiguredPackageAuthority>();
            var seen = new HashSet<ConfiguredPackageAuthority>(
                ReferenceEqualityComparer.Instance);
            foreach (PackageSource source in sources)
            {
                cancellationToken.ThrowIfCancellationRequested();
                operationContext?.ThrowIfExpired();
                if (TryGetEligibleAuthority(source, failures) is { } authority
                    && seen.Add(authority.Authority))
                {
                    authorities.Add(authority.Authority);
                }
            }
            operationContext?.ThrowIfExpired();

            PackageAcquisitionCandidate? candidate = authorities.Count == 0
                ? null
                : PackageAcquisitionCandidate.CreatePinned(
                    _candidateIssuer,
                    coordinate,
                    authorities);
            return new PackageAcquisitionCandidateResult(
                candidate is null
                    ? PackageAcquisitionCandidateResultState.Denied
                    : PackageAcquisitionCandidateResultState.Resolved,
                candidate,
                failures);
        }
        catch (NuGetOperationTimeoutException)
        {
            failures.Add(new PackageAuthorityFailure(
                InertString.Empty,
                PackageAuthorityFailureKind.Timeout,
                "The package candidate operation deadline expired before authorization completed.")
            {
                Timeout = operationContext is null
                    ? null
                    : new(
                        PackageSourceTimeoutKind.Operation,
                        operationContext.OperationTimeout),
            });
            return new PackageAcquisitionCandidateResult(
                PackageAcquisitionCandidateResultState.Incomplete,
                candidate: null,
                failures);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw new OperationCanceledException(cancellationToken);
        }
    }
}
