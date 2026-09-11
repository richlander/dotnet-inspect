using Inspector.Resources;
using NuGetFetch;

namespace DotnetInspector.Packages;

/// <summary>Issues Package Source root resources.</summary>
public static class PackageSourceSettlementService
{
    /// <summary>
    /// Issues a root over caller-owned clients. The root creates operation
    /// contexts when it issues operation leases.
    /// </summary>
    public static PackageSourceSettlementLease IssueLease(
        Func<ConfiguredPackageAuthority, IPackageSourceClient> getClient)
    {
        ArgumentNullException.ThrowIfNull(getClient);
        return new(new(getClient));
    }
}

/// <summary>
/// Owns one settlement generation, but not its clients, stores or retained content.
/// Await successful settlement before releasing the caller-owned clients.
/// </summary>
[ResourceOwnership]
public sealed partial class PackageSourceSettlementLease : IAsyncDisposable
{
    private readonly PackageSourceSettlementGeneration _generation;

    internal PackageSourceSettlementLease(
        PackageSourceSettlementGeneration generation) => _generation = generation;

    /// <summary>
    /// Registers one operation and transfers ownership of its deadline context
    /// to the returned lease.
    /// </summary>
    public PackageSourceOperationLease IssueOperationLease(
        CancellationToken cancellationToken = default,
        TimeSpan? requestTimeout = null,
        TimeSpan? operationTimeout = null)
    {
        _generation.RegisterOperation();
        NuGetOperationContext? context = null;
        bool transferred = false;
        try
        {
            context = new(
                requestTimeout ?? NuGetFetchOptions.DefaultRequestTimeout,
                operationTimeout ?? NuGetFetchOptions.DefaultOperationTimeout,
                cancellationToken);
            var operation = new PackageSourceOperationLease(
                _generation,
                context);
            transferred = true;
            return operation;
        }
        finally
        {
            if (!transferred)
            {
                try
                {
                    context?.Dispose();
                }
                finally
                {
                    _generation.ReleaseOperation();
                }
            }
        }
    }

    internal PackageSourceSettlementGeneration RegisterCompatibilityOperation()
    {
        _generation.RegisterOperation();
        return _generation;
    }

    /// <summary>Revokes issuance immediately and waits for every issued operation.</summary>
    public ValueTask DisposeAsync() => _generation.SettleAsync();
}
