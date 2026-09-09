using ILInspector.Metadata;

namespace DotnetInspector.Queries;

public sealed partial class InspectionWorkspace
{
    /// <summary>
    /// Executes a query against one exact committed package Root generation.
    /// The Workspace retains ownership of the realization throughout the
    /// awaited callback, including when that Root is removed or replaced.
    /// </summary>
    /// <remarks>
    /// The callback borrows the realization and its groups: it must not dispose
    /// them or retain them beyond the callback. Return materialized query
    /// results instead. A root-only or explicit-empty package reaches the
    /// callback with no assembly contexts; admission failures do not invoke it.
    /// Existing group query and close semantics still apply. Cancellation can
    /// interrupt admission and is passed to the callback for cooperative use.
    /// Callback exceptions and cancellation propagate without becoming typed
    /// admission failures.
    /// </remarks>
    public async ValueTask<ArtifactRootResult<TResult>>
        ExecutePackageRootQueryAsync<TResult>(
            PackageArtifactRootCorrespondence correspondence,
            ArtifactRootGenerationReference generation,
            Func<PackageAssemblyContextRealization, CancellationToken,
                ValueTask<TResult>> query,
            AssemblyBindingPolicyVersion? expectedPolicy = null,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        cancellationToken.ThrowIfCancellationRequested();

        ArtifactRootResult<ArtifactRootQueryLease> admission =
            await EnterArtifactRootQueryAsync(
                _identity, correspondence, generation, expectedPolicy,
                cancellationToken).ConfigureAwait(false);
        if (admission is ArtifactRootResult<ArtifactRootQueryLease>.Rejected rejected)
            return new ArtifactRootResult<TResult>.Rejected(rejected.Failure);

        using ArtifactRootQueryLease lease =
            ((ArtifactRootResult<ArtifactRootQueryLease>.Available)admission).Value;
        cancellationToken.ThrowIfCancellationRequested();
        TResult result = await query(lease.Realization, cancellationToken)
            .ConfigureAwait(false);
        return new ArtifactRootResult<TResult>.Available(result);
    }
}
