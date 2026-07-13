namespace SectionRegistrySpike;

/// <summary>
/// Shared execution context passed to every capability. <see cref="NetworkAuthorized"/> mirrors
/// production's authorization derived from <c>SectionPipeline.GetAuthorizedSections</c> — network
/// capabilities must check it and refuse to run when it is false rather than silently no-op.
/// </summary>
public sealed class SpikeContext
{
    public required SpikeModel Model { get; init; }
    public required bool NetworkAuthorized { get; init; }
}
