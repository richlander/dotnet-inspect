using DotnetInspector.Packages;
using DotnetInspector.Platforms;

namespace DotnetInspector.Ecosystems;

/// <summary>One product ecosystem owning a discovered Package.</summary>
public sealed record EcosystemPackage
{
    internal EcosystemPackage(
        EcosystemPackDescriptor ecosystem,
        PlatformOwnedInfo? platformOwnedInfo)
    {
        Ecosystem = ecosystem;
        PlatformOwnedInfo = platformOwnedInfo;
    }

    public EcosystemPackDescriptor Ecosystem { get; }

    public PlatformOwnedInfo? PlatformOwnedInfo { get; }
}

/// <summary>Platform membership and traversal-target subsumption evidence.</summary>
public sealed record PlatformOwnedInfo
{
    internal PlatformOwnedInfo(PlatformFamily layer, PlatformSupplyReceipt evidence)
    {
        Layer = layer;
        Evidence = evidence;
    }

    public PlatformFamily Layer { get; }

    /// <summary>
    /// Whether the traversal platform subsumes the selected package version;
    /// null when the comparison is unavailable. This is not a restore observation.
    /// </summary>
    public bool? IsPruned => Evidence.Supply.Subsumption switch
    {
        PlatformSubsumption.Subsumed => true,
        PlatformSubsumption.NotSubsumed => false,
        PlatformSubsumption.NotComparable => null,
        _ => throw new InvalidOperationException("Unknown platform subsumption."),
    };

    public PlatformSupplyReceipt Evidence { get; }
}

/// <summary>The result of one product-relative Package classification.</summary>
public abstract record EcosystemPackageResult
{
    private protected EcosystemPackageResult()
    {
    }

    public sealed record Known : EcosystemPackageResult
    {
        internal Known(EcosystemPackage package) => Package = package;

        public EcosystemPackage Package { get; }
    }

    public sealed record NotEcosystem : EcosystemPackageResult
    {
        internal NotEcosystem()
        {
        }
    }

    public sealed record Unavailable : EcosystemPackageResult
    {
        internal Unavailable(
            EcosystemPackageUnavailableReason reason,
            EcosystemPackDescriptor? ecosystem)
        {
            Reason = reason;
            Ecosystem = ecosystem;
        }

        public EcosystemPackageUnavailableReason Reason { get; }

        public EcosystemPackDescriptor? Ecosystem { get; }
    }
}

public enum EcosystemPackageUnavailableReason
{
    InventoryUnavailable,
    InventoryTargetMismatch,
}
