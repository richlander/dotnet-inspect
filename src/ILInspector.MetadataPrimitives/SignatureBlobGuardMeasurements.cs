namespace ILInspector.MetadataPrimitives;

/// <summary>
/// Nonnegative array-count observations from the actual guard scan, including
/// the first refused count. No further input is scanned to complete a report.
/// </summary>
internal struct SignatureBlobGuardMeasurements
{
    internal SignatureBlobGuardCount Sizes;
    internal SignatureBlobGuardCount LowerBounds;
}

internal readonly record struct SignatureBlobGuardCount(int Count, long Total, int Largest)
{
    internal SignatureBlobGuardCount Observe(int amount) =>
        new(Count + 1, Total + amount, Math.Max(Largest, amount));
}
