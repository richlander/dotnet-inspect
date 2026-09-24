using NuGetFetch;

namespace DotnetInspector.Packages;

/// <summary>
/// Supplies a caller-owned package store for one configured authority and
/// producer.
/// </summary>
public delegate IPackageStore PackageStoreProvider(
    ConfiguredPackageAuthority authority,
    PackageProducerIdentity producer);

/// <summary>
/// Stable host capability and policy for package payload acquisition.
/// </summary>
/// <remarks>
/// This plan carries no source-settlement lease, operation context, payload,
/// or release obligation. It does not take ownership of stores returned by
/// the provider.
/// </remarks>
public sealed class PackagePayloadAcquisitionPlan
{
    private readonly PackageStoreProvider _getStore;

    public PackagePayloadAcquisitionPlan(
        PackageStoreProvider getStore,
        PackagePayloadLimits? limits = null,
        IPackagePayloadTransferPolicy? transferPolicy = null,
        Action<string>? log = null,
        PackagePayloadAccess access = PackagePayloadAccess.Complete,
        long rangedSizeCut = PackageRangedRead.DefaultSizeCut)
    {
        ArgumentNullException.ThrowIfNull(getStore);
        if (!Enum.IsDefined(access))
            throw new ArgumentOutOfRangeException(nameof(access));
        _getStore = getStore;
        Limits = limits;
        TransferPolicy = transferPolicy;
        Log = log;
        Access = access;
        ArgumentOutOfRangeException.ThrowIfNegative(rangedSizeCut);
        RangedSizeCut = rangedSizeCut;
    }

    public PackagePayloadLimits? Limits { get; }

    public IPackagePayloadTransferPolicy? TransferPolicy { get; }

    public Action<string>? Log { get; }

    /// <summary>
    /// How an uncached payload is transferred. <see cref="PackagePayloadAccess.Ranged"/>
    /// requires a Realize operation, whose selection bounds the read.
    /// </summary>
    public PackagePayloadAccess Access { get; }

    /// <summary>
    /// Under ranged access, archives at or under this advertised length are
    /// acquired complete and cached; larger ones are read by range.
    /// </summary>
    public long RangedSizeCut { get; }

    /// <summary>
    /// Gets the caller-owned store for one authority and producer.
    /// </summary>
    public IPackageStore GetStore(
        ConfiguredPackageAuthority authority,
        PackageProducerIdentity producer) =>
        _getStore(authority, producer)
        ?? throw new InvalidOperationException(
            "The package store provider returned null.");
}
