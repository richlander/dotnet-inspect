namespace SectionRegistrySpike;

/// <summary>
/// Caller-owned per-run state. Capability executors are static and place reusable results here,
/// matching the product's existing <c>ScannerContext</c> ownership.
/// </summary>
public sealed class SpikeContext
{
    public required SpikeModel Model { get; init; }
    public int BodyIndex { get; set; }
    public int WorkCount { get; set; }

    public void Reset()
    {
        Model.Reset();
        BodyIndex = 0;
        WorkCount = 0;
    }
}
