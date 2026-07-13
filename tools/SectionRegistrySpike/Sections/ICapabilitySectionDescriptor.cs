using SectionRegistrySpike.Capabilities;

namespace SectionRegistrySpike.Sections;

/// <summary>
/// Static section metadata plus typed executable requirements. Legacy scanner keys, network flags,
/// and probe booleans are intentionally absent: the registry derives execution policy from the
/// compiled capability plan and materializes a normal <c>SectionEntry</c> for SectionPipeline.
/// </summary>
public interface ICapabilitySectionDescriptor<TModel, TContext>
{
    static abstract string Name { get; }
    static abstract bool IsExpensive { get; }
    static virtual bool ExplicitOnly => false;
    static virtual bool Info => false;
    static abstract bool CanRender(TModel model);
    static abstract CapabilityKey[] RequiredCapabilities { get; }
}
