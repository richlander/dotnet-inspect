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
        Action<string>? log = null)
    {
        ArgumentNullException.ThrowIfNull(getStore);
        _getStore = getStore;
        Limits = limits;
        TransferPolicy = transferPolicy;
        Log = log;
    }

    public PackagePayloadLimits? Limits { get; }

    public IPackagePayloadTransferPolicy? TransferPolicy { get; }

    public Action<string>? Log { get; }

    internal IPackageStore GetStore(
        ConfiguredPackageAuthority authority,
        PackageProducerIdentity producer) =>
        _getStore(authority, producer)
        ?? throw new InvalidOperationException(
            "The package store provider returned null.");
}
