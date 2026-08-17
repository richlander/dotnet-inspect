namespace DotnetInspector.Packages;

/// <summary>
/// Host-owned limits for untrusted symbol responses and symbol-package
/// expansion.
/// </summary>
public sealed class SymbolAcquisitionLimits
{
    public SymbolAcquisitionLimits(
        long maxSymbolPackageBytes,
        long maxPortablePdbBytes,
        int maxSymbolPackageEntries)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(
            maxSymbolPackageBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(
            maxPortablePdbBytes);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(
            maxSymbolPackageBytes,
            Array.MaxLength);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(
            maxPortablePdbBytes,
            Array.MaxLength);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(
            maxSymbolPackageEntries);

        MaxSymbolPackageBytes = maxSymbolPackageBytes;
        MaxPortablePdbBytes = maxPortablePdbBytes;
        MaxSymbolPackageEntries = maxSymbolPackageEntries;
    }

    public long MaxSymbolPackageBytes { get; }
    public long MaxPortablePdbBytes { get; }
    public int MaxSymbolPackageEntries { get; }
}
