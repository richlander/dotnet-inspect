using DotnetInspector.Sections;
using SectionRegistrySpike.Capabilities;

namespace SectionRegistrySpike.Sections;

/// <summary>
/// Spike-only extension of the real <see cref="ISectionDescriptor{TModel}"/>. Adds a static list
/// of typed <see cref="CapabilityKey"/> requirements alongside the existing selection metadata
/// (<c>Name</c>, <c>IsExpensive</c>, <c>ScannerKey</c>, <c>Capabilities</c>, <c>CanRender</c>, ...).
/// A descriptor still never needs to be instantiated — <see cref="RequiredCapabilities"/> is read
/// the same way as every other static member on <see cref="ISectionDescriptor{TModel}"/>.
/// </summary>
/// <typeparam name="TModel">The model type this section inspects.</typeparam>
public interface ICapabilitySectionDescriptor<TModel> : ISectionDescriptor<TModel>
{
    /// <summary>
    /// Capabilities this section requires to populate its render data. The registry resolves the
    /// full transitive, deduplicated, topologically ordered closure — descriptors only declare
    /// their own direct requirements.
    /// </summary>
    static abstract CapabilityKey[] RequiredCapabilities { get; }
}
