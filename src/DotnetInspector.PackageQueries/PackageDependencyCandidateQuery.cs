using System.Collections.Immutable;
using DotnetInspector.Packages;
using DotnetInspector.Queries;
using NuGetFetch;

namespace DotnetInspector.PackageQueries;

/// <summary>
/// One caller-approved declaration resolution request.
/// </summary>
public abstract record PackageDependencyCandidateRequest(
    PackageDependencyEvidenceDeclaration Declaration)
{
    public sealed record Declared(
        PackageDependencyEvidenceDeclaration Value) :
        PackageDependencyCandidateRequest(Value);

    public sealed record Restored(
        PackageDependencyEvidenceDeclaration Value,
        RestoredProjectPackageNodeIdentity ResolvedPackage) :
        PackageDependencyCandidateRequest(Value);
}

/// <summary>Why one exact dependency candidate could not be issued.</summary>
public abstract record PackageDependencyCandidateFailure
{
    private PackageDependencyCandidateFailure()
    {
    }

    public sealed record AuthorizationDenied(
        ImmutableArray<PackageAuthorityFailure> Failures) :
        PackageDependencyCandidateFailure;

    public sealed record NoMatchingVersion :
        PackageDependencyCandidateFailure;

    public sealed record ResolvedCoordinateMismatch(
        RestoredProjectPackageNodeIdentity ResolvedPackage) :
        PackageDependencyCandidateFailure;
}

/// <summary>Typed evidence for an incomplete candidate resolution.</summary>
public abstract record PackageDependencyCandidateIncomplete
{
    private PackageDependencyCandidateIncomplete()
    {
    }

    public sealed record PinnedAuthorization(
        ImmutableArray<PackageAuthorityFailure> Failures) :
        PackageDependencyCandidateIncomplete;

    public sealed record VersionDiscovery(
        PackageVersionDiscoveryState State,
        PackageVersionDiscoveryContract Contract,
        int CandidateObservationCount,
        ImmutableArray<PackageAuthorityFailure> Failures) :
        PackageDependencyCandidateIncomplete;
}

/// <summary>The closed result of resolving one normalized declaration.</summary>
public abstract record PackageDependencyCandidateResult(
    PackageDependencyEvidenceDeclaration Declaration)
{
    public sealed record Resolved(
        PackageDependencyEvidenceDeclaration Value,
        PackageAcquisitionCandidate Candidate,
        ImmutableArray<PackageAuthorityFailure> Diagnostics) :
        PackageDependencyCandidateResult(Value);

    public sealed record Failed(
        PackageDependencyEvidenceDeclaration Value,
        PackageDependencyCandidateFailure Failure) :
        PackageDependencyCandidateResult(Value);

    public sealed record Incomplete(
        PackageDependencyEvidenceDeclaration Value,
        PackageDependencyCandidateIncomplete Evidence) :
        PackageDependencyCandidateResult(Value);
}

/// <summary>
/// The package-source operations needed to resolve one dependency declaration.
/// </summary>
public interface IPackageDependencyCandidateSource
{
    ValueTask<PackageAcquisitionCandidateResult> ResolvePinnedCandidateAsync(
        PackageSourceCoordinate coordinate,
        CancellationToken cancellationToken = default,
        NuGetOperationContext? operationContext = null);

    Task<PackageVersionDiscoveryResult> DiscoverDependencyVersionsAsync(
        string packageId,
        CancellationToken cancellationToken = default,
        NuGetOperationContext? operationContext = null);
}

/// <summary>
/// Composes one normalized declaration with package-owned source evidence.
/// </summary>
public static class PackageDependencyCandidateQuery
{
    public static async ValueTask<PackageDependencyCandidateResult> ExecuteAsync(
        PackageDependencyCandidateRequest request,
        IPackageDependencyCandidateSource source,
        CancellationToken cancellationToken = default,
        NuGetOperationContext? operationContext = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(source);
        using NuGetOperationContext? ownedOperation =
            operationContext is null
                ? new NuGetOperationContext(cancellationToken)
                : null;
        NuGetOperationContext operation =
            operationContext ?? ownedOperation!;
        CancellationToken effectiveCancellationToken =
            ResolveInvocationToken(
                operation,
                cancellationToken);
        effectiveCancellationToken.ThrowIfCancellationRequested();

        PackageDependencyEvidenceDeclaration declaration =
            request.Declaration;
        PackageSourceCoordinate? exactCoordinate = request switch
        {
            PackageDependencyCandidateRequest.Restored restored =>
                ValidateRestoredCoordinate(
                    declaration,
                    restored.ResolvedPackage),
            PackageDependencyCandidateRequest.Declared =>
                ExactDeclaredCoordinate(declaration),
            _ => throw new ArgumentOutOfRangeException(nameof(request)),
        };

        if (request is PackageDependencyCandidateRequest.Restored
                restoredRequest
            && exactCoordinate is null)
        {
            return new PackageDependencyCandidateResult.Failed(
                declaration,
                new PackageDependencyCandidateFailure
                    .ResolvedCoordinateMismatch(
                        restoredRequest.ResolvedPackage));
        }

        if (exactCoordinate is not null)
        {
            PackageAcquisitionCandidateResult authorization =
                await source.ResolvePinnedCandidateAsync(
                    exactCoordinate,
                    cancellationToken,
                    operation).ConfigureAwait(false);
            effectiveCancellationToken.ThrowIfCancellationRequested();
            if (authorization.State
                    == PackageAcquisitionCandidateResultState.Incomplete)
            {
                return new PackageDependencyCandidateResult.Incomplete(
                    declaration,
                    new PackageDependencyCandidateIncomplete
                        .PinnedAuthorization(
                            [.. authorization.Failures]));
            }
            PackageDependencyCandidateResult.Incomplete?
                pinnedTimeout = IncompleteIfExpired(
                    declaration,
                    authorization.Failures,
                    operation);
            if (pinnedTimeout is not null)
                return pinnedTimeout;
            if (authorization.State
                    == PackageAcquisitionCandidateResultState.Denied)
            {
                return new PackageDependencyCandidateResult.Failed(
                    declaration,
                    new PackageDependencyCandidateFailure.AuthorizationDenied(
                        [.. authorization.Failures]));
            }
            PackageAcquisitionCandidate candidate =
                authorization.Candidate
                ?? throw new InvalidOperationException(
                    "A resolved candidate result did not carry a candidate.");
            if (candidate.Kind
                    != PackageAcquisitionCandidateKind.CallerPinned
                || candidate.Coordinate != exactCoordinate)
            {
                throw new InvalidOperationException(
                    "The package source returned a pinned candidate for another coordinate or candidate kind.");
            }

            return new PackageDependencyCandidateResult.Resolved(
                declaration,
                candidate,
                [.. authorization.Failures]);
        }

        PackageVersionDiscoveryResult discovery =
            await source.DiscoverDependencyVersionsAsync(
                declaration.CanonicalPackageId,
                cancellationToken,
                operation).ConfigureAwait(false);
        effectiveCancellationToken.ThrowIfCancellationRequested();
        if (!discovery.Contract.SupportsDependencyRangeResolution)
        {
            throw new InvalidOperationException(
                "Dependency candidate resolution requires complete dependency-range version discovery.");
        }
        if (discovery.State != PackageVersionDiscoveryState.Authoritative)
        {
            return new PackageDependencyCandidateResult.Incomplete(
                declaration,
                new PackageDependencyCandidateIncomplete.VersionDiscovery(
                    discovery.State,
                    discovery.Contract,
                    discovery.CandidateObservationCount,
                    [.. discovery.Failures]));
        }

        string? selectedVersion =
            PackageDependencyVersionRange.SelectBestSatisfying(
                discovery.Versions,
                declaration.CanonicalVersionConstraint);
        PackageDependencyCandidateResult.Incomplete?
            selectionTimeout = IncompleteIfExpired(
                declaration,
                discovery,
                operation);
        if (selectionTimeout is not null)
            return selectionTimeout;
        if (selectedVersion is null)
        {
            return new PackageDependencyCandidateResult.Failed(
                declaration,
                new PackageDependencyCandidateFailure.NoMatchingVersion());
        }

        PackageAcquisitionCandidate selected =
            discovery.SelectCandidate(selectedVersion);
        selectionTimeout = IncompleteIfExpired(
            declaration,
            discovery,
            operation);
        if (selectionTimeout is not null)
            return selectionTimeout;
        if (selected.Kind != PackageAcquisitionCandidateKind.Discovered
            || !selected.Coordinate.PackageId.Equals(
                declaration.CanonicalPackageId,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "The package source issued a discovered candidate for another package or candidate kind.");
        }

        return new PackageDependencyCandidateResult.Resolved(
            declaration,
            selected,
            [.. discovery.Failures]);
    }

    private static PackageSourceCoordinate? ExactDeclaredCoordinate(
        PackageDependencyEvidenceDeclaration declaration)
    {
        string? version = PackageDependencyVersionRange.GetExactVersion(
            declaration.CanonicalVersionConstraint);
        return version is null
            ? null
            : PackageSourceCoordinate.Create(
                declaration.CanonicalPackageId,
                version);
    }

    private static PackageSourceCoordinate? ValidateRestoredCoordinate(
        PackageDependencyEvidenceDeclaration declaration,
        RestoredProjectPackageNodeIdentity resolvedPackage)
    {
        PackageSourceCoordinate coordinate = resolvedPackage.Coordinate;
        return coordinate.PackageId.Equals(
                    declaration.CanonicalPackageId,
                    StringComparison.Ordinal)
                && PackageDependencyVersionRange.Satisfies(
                    coordinate.Version,
                    declaration.CanonicalVersionConstraint)
            ? coordinate
            : null;
    }

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

    private static PackageDependencyCandidateResult.Incomplete?
        IncompleteIfExpired(
            PackageDependencyEvidenceDeclaration declaration,
            IReadOnlyList<PackageAuthorityFailure> failures,
            NuGetOperationContext operation)
    {
        try
        {
            operation.ThrowIfExpired();
            return null;
        }
        catch (NuGetOperationTimeoutException)
        {
            return new PackageDependencyCandidateResult.Incomplete(
                declaration,
                new PackageDependencyCandidateIncomplete
                    .PinnedAuthorization(
                        [
                            .. failures,
                            OperationTimeoutFailure(operation),
                        ]));
        }
    }

    private static PackageDependencyCandidateResult.Incomplete?
        IncompleteIfExpired(
            PackageDependencyEvidenceDeclaration declaration,
            PackageVersionDiscoveryResult discovery,
            NuGetOperationContext operation)
    {
        try
        {
            operation.ThrowIfExpired();
            return null;
        }
        catch (NuGetOperationTimeoutException)
        {
            return new PackageDependencyCandidateResult.Incomplete(
                declaration,
                new PackageDependencyCandidateIncomplete.VersionDiscovery(
                    PackageVersionDiscoveryState.Failed,
                    discovery.Contract,
                    discovery.CandidateObservationCount,
                    [
                        .. discovery.Failures,
                        OperationTimeoutFailure(operation),
                    ]));
        }
    }

    private static PackageAuthorityFailure OperationTimeoutFailure(
        NuGetOperationContext operation) =>
        new(
            InertText.InertString.Empty,
            PackageAuthorityFailureKind.Timeout,
            "The package candidate operation deadline expired before the result could be published.")
        {
            Timeout = new(
                PackageSourceTimeoutKind.Operation,
                operation.OperationTimeout),
        };
}
