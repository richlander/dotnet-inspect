using System.Collections.Immutable;
using DotnetInspector.Packages;
using NuGetFetch;

namespace DotnetInspector.Queries;

/// <summary>
/// Why the package dependency traversal query could not obtain an exact candidate
/// for one recursively authorized declaration.
/// </summary>
/// <remarks>
/// This is the traversal-owned mirror of the #5765 candidate-resolution failure
/// algebra. <see cref="DotnetInspector.Queries"/> cannot reference the
/// <c>DotnetInspector.PackageQueries</c> assembly that implements #5765, because that
/// assembly already references <see cref="DotnetInspector.Queries"/>; a thin adapter
/// living in <c>DotnetInspector.PackageQueries</c> maps the real
/// <c>PackageDependencyCandidateFailure</c> onto this traversal-owned shape one to one.
/// </remarks>
public abstract record PackageDependencyTraversalCandidateFailure
{
    private PackageDependencyTraversalCandidateFailure()
    {
    }

    /// <summary>No configured authority authorized this package ID.</summary>
    public sealed record AuthorizationDenied(
        ImmutableArray<PackageAuthorityFailure> Failures) :
        PackageDependencyTraversalCandidateFailure;

    /// <summary>Complete, authoritative version evidence satisfied no acceptable version.</summary>
    public sealed record NoMatchingVersion :
        PackageDependencyTraversalCandidateFailure;
}

/// <summary>
/// Typed evidence for candidate resolution that could not reach an authoritative
/// answer within the shared operation deadline.
/// </summary>
public abstract record PackageDependencyTraversalCandidateIncomplete
{
    private PackageDependencyTraversalCandidateIncomplete()
    {
    }

    /// <summary>An exact, owner-classified pinned coordinate could not be authorized.</summary>
    public sealed record PinnedAuthorization(
        ImmutableArray<PackageAuthorityFailure> Failures) :
        PackageDependencyTraversalCandidateIncomplete;

    /// <summary>Complete dependency-range version discovery did not settle.</summary>
    public sealed record VersionDiscovery(
        PackageVersionDiscoveryState State,
        PackageVersionDiscoveryContract Contract,
        int CandidateObservationCount,
        ImmutableArray<PackageAuthorityFailure> Failures) :
        PackageDependencyTraversalCandidateIncomplete;
}

/// <summary>
/// The closed result of resolving one normalized declaration to an exact,
/// source-authorized candidate for traversal expansion.
/// </summary>
public abstract record PackageDependencyTraversalCandidateResult
{
    private PackageDependencyTraversalCandidateResult()
    {
    }

    public sealed record Resolved(
        PackageAcquisitionCandidate Candidate,
        ImmutableArray<PackageAuthorityFailure> Diagnostics) :
        PackageDependencyTraversalCandidateResult;

    public sealed record Failed(
        PackageDependencyTraversalCandidateFailure Failure) :
        PackageDependencyTraversalCandidateResult;

    public sealed record Incomplete(
        PackageDependencyTraversalCandidateIncomplete Evidence) :
        PackageDependencyTraversalCandidateResult;
}

/// <summary>
/// The traversal-owned capability that resolves one normalized declaration to an
/// exact acquisition candidate. A thin <c>DotnetInspector.PackageQueries</c> adapter
/// binds this to the shared #5765 <c>PackageDependencyCandidateQuery</c> capability
/// so traversal never reimplements source authorization or version selection.
/// </summary>
public interface IPackageDependencyTraversalCandidateResolver
{
    ValueTask<PackageDependencyTraversalCandidateResult> ResolveAsync(
        PackageDependencyEvidenceDeclaration declaration,
        CancellationToken cancellationToken = default,
        NuGetOperationContext? operationContext = null);
}

/// <summary>
/// The closed result of acquiring one candidate-authorized exact manifest for
/// traversal expansion.
/// </summary>
public abstract record PackageDependencyTraversalManifestResult
{
    private PackageDependencyTraversalManifestResult()
    {
    }

    /// <summary>
    /// The exact manifest bytes, their source association, and attributed failures
    /// from earlier authorized sources consulted before the successful source.
    /// </summary>
    public sealed record Acquired(
        PackageSourceManifest Manifest,
        ImmutableArray<PackageAuthorityFailure> Diagnostics) :
        PackageDependencyTraversalManifestResult;

    /// <summary>No admitted authority could supply the manifest.</summary>
    public sealed record Failed(
        ImmutableArray<PackageAuthorityFailure> Failures) :
        PackageDependencyTraversalManifestResult;

    /// <summary>The shared operation deadline expired before acquisition settled.</summary>
    public sealed record Incomplete(
        ImmutableArray<PackageAuthorityFailure> Failures) :
        PackageDependencyTraversalManifestResult;
}

/// <summary>
/// The package-owned capability that acquires one candidate-authorized exact
/// manifest. It never downloads a package archive. A thin
/// <c>DotnetInspector.PackageQueries</c> adapter binds this to
/// <c>DesktopPackageSourceComposition</c> for the desktop CLI host or to explicit
/// caller-owned source clients for the Browser/Wasm host.
/// </summary>
public interface IPackageDependencyTraversalManifestAcquirer
{
    Task<PackageDependencyTraversalManifestResult> AcquireAsync(
        PackageAcquisitionCandidate candidate,
        CancellationToken cancellationToken = default,
        NuGetOperationContext? operationContext = null);
}
