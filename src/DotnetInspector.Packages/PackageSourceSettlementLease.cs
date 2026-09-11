using Inspector.Resources;
using NuGetFetch;

namespace DotnetInspector.Packages;

/// <summary>Issues Package Source root and operation resources.</summary>
public static class PackageSourceSettlementService
{
    /// <summary>
    /// Issues a root over caller-owned clients. Operation contexts are created
    /// by this service when an operation lease is issued.
    /// </summary>
    public static PackageSourceSettlementLease IssueLease(
        Func<ConfiguredPackageAuthority, IPackageSourceClient> getClient)
    {
        ArgumentNullException.ThrowIfNull(getClient);
        return new(new(getClient));
    }

    /// <summary>
    /// Registers one operation and transfers ownership of its deadline context
    /// to the returned lease. Authorization is revoked when root settlement starts.
    /// </summary>
    public static PackageSourceOperationLease IssueOperationLease(
        PackageSourceSettlementAuthorization authorization,
        CancellationToken cancellationToken = default,
        TimeSpan? requestTimeout = null,
        TimeSpan? operationTimeout = null)
    {
        ArgumentNullException.ThrowIfNull(authorization);
        PackageSourceSettlementGeneration generation = authorization.Generation;
        generation.RegisterOperation();
        NuGetOperationContext? context = null;
        bool transferred = false;
        try
        {
            context = new(
                requestTimeout ?? NuGetFetchOptions.DefaultRequestTimeout,
                operationTimeout ?? NuGetFetchOptions.DefaultOperationTimeout,
                cancellationToken);
            var operation = new PackageSourceOperationLease(generation, context);
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
                    generation.ReleaseOperation();
                }
            }
        }
    }
}

/// <summary>
/// Revocable permission to request operations for one exact root generation.
/// This value has no release obligation and is not durable package evidence.
/// </summary>
public sealed class PackageSourceSettlementAuthorization
{
    internal PackageSourceSettlementAuthorization(
        PackageSourceSettlementGeneration generation) => Generation = generation;

    internal PackageSourceSettlementGeneration Generation { get; }
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

    /// <summary>Synchronously borrows the live root to issue revocable permission.</summary>
    public PackageSourceSettlementAuthorization CreateAuthorization() =>
        _generation.CreateAuthorization();

    /// <summary>Revokes issuance immediately and waits for every issued operation.</summary>
    public ValueTask DisposeAsync() => _generation.SettleAsync();
}
