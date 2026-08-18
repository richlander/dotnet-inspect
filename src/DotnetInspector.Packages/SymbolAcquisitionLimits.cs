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
        int maxSymbolPackageEntries,
        long? maxExpandedPdbBytes = null)
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
        if (maxExpandedPdbBytes is <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxExpandedPdbBytes));
        }

        MaxSymbolPackageBytes = maxSymbolPackageBytes;
        MaxPortablePdbBytes = maxPortablePdbBytes;
        MaxSymbolPackageEntries = maxSymbolPackageEntries;
        MaxExpandedPdbBytes =
            maxExpandedPdbBytes ?? maxSymbolPackageBytes;
    }

    public long MaxSymbolPackageBytes { get; }
    public long MaxPortablePdbBytes { get; }
    public int MaxSymbolPackageEntries { get; }
    public long MaxExpandedPdbBytes { get; }
}
