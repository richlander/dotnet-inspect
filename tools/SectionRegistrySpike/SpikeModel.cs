namespace SectionRegistrySpike;

/// <summary>
/// Representative synthetic inspection model for the spike — NOT a production descriptor set.
/// Structural fields mirror facts a real assembly inspection could read directly from IL/PE
/// metadata (cheap, always known); populated fields are only set once the matching capability
/// executes, mirroring how <c>LibraryInspection</c> fields stay null/zero until their scanner runs.
/// </summary>
public sealed class SpikeModel
{
    // Structural facts — known before any capability executes. Descriptor applicability gates
    // read these directly, the same way production applicability predicates (e.g.
    // `HasReferenceData`) read structural model state rather than the field a scanner populates.
    public bool IsManagedAssembly { get; init; } = true;
    public bool HasSourceLink { get; init; } = true;
    public bool HasMethodBodies { get; init; } = true;

    // Populated only by capability execution — these mirror scanner-populated LibraryInspection fields.
    public bool MetadataLoaded { get; set; }
    public string? DecompiledSource { get; set; }
    public bool PdbAcquired { get; set; }
    public string? OriginalSource { get; set; }
    public int Calls { get; set; }
    public int Facts { get; set; }
    public bool DeepScanRan { get; set; }

}
