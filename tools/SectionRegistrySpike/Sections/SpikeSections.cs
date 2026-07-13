using SectionRegistrySpike.Capabilities;

namespace SectionRegistrySpike.Sections;

/// <summary>
/// Representative typed descriptors. They contain no scanner key, network flag, or independent
/// probe-safety declaration; capability requirements are the sole execution metadata.
/// </summary>
public static class SpikeSections
{
    public readonly struct MetadataSection : ICapabilitySectionDescriptor<SpikeModel, SpikeContext>
    {
        public static string Name => "Metadata";
        public static bool IsExpensive => false;
        public static bool Info => true;
        public static bool CanRender(SpikeModel model) => model.MetadataLoaded;
        public static CapabilityKey[] RequiredCapabilities => [CapabilityKey.Of<MetadataCapability>()];
    }

    public readonly struct DecompiledSourceSection : ICapabilitySectionDescriptor<SpikeModel, SpikeContext>
    {
        public static string Name => "Decompiled Source";
        public static bool IsExpensive => true;
        public static bool ExplicitOnly => true;
        public static bool CanRender(SpikeModel model) => model.DecompiledSource != null;
        public static CapabilityKey[] RequiredCapabilities => [CapabilityKey.Of<DecompileCapability>()];
    }

    public readonly struct OriginalSourceSection : ICapabilitySectionDescriptor<SpikeModel, SpikeContext>
    {
        public static string Name => "Original Source";
        public static bool IsExpensive => true;
        public static bool ExplicitOnly => true;
        public static bool CanRender(SpikeModel model) => model.OriginalSource != null;
        public static CapabilityKey[] RequiredCapabilities => [CapabilityKey.Of<FetchSourceCapability>()];
    }

    public readonly struct CallsSection : ICapabilitySectionDescriptor<SpikeModel, SpikeContext>
    {
        public static string Name => "Calls";
        public static bool IsExpensive => false;
        public static bool ExplicitOnly => true;
        public static bool CanRender(SpikeModel model) => model.Calls > 0;
        public static CapabilityKey[] RequiredCapabilities => [CapabilityKey.Of<CallsCapability>()];
    }

    public readonly struct FactsSection : ICapabilitySectionDescriptor<SpikeModel, SpikeContext>
    {
        public static string Name => "Facts";
        public static bool IsExpensive => false;
        public static bool ExplicitOnly => true;
        public static bool CanRender(SpikeModel model) => model.Facts > 0;
        public static CapabilityKey[] RequiredCapabilities => [CapabilityKey.Of<FactsCapability>()];
    }

    public readonly struct MisleadingProbeSection : ICapabilitySectionDescriptor<SpikeModel, SpikeContext>
    {
        public static string Name => "Misleading Probe";
        public static bool IsExpensive => false;
        public static bool ExplicitOnly => true;
        public static bool CanRender(SpikeModel model) => model.DeepScanRan;
        public static CapabilityKey[] RequiredCapabilities => [CapabilityKey.Of<DeepScanCapability>()];
    }

    public static CapabilityRegistry<SpikeContext> CreateCapabilityRegistry() => new CapabilityRegistry<SpikeContext>()
        .Register<MetadataCapability>()
        .Register<DecompileCapability>()
        .Register<AcquirePdbCapability>()
        .Register<FetchSourceCapability>()
        .Register<BodyIndexCapability>()
        .Register<CallsCapability>()
        .Register<FactsCapability>();

    public static CapabilitySectionRegistry<SpikeModel, SpikeContext> CreateCapabilityRegistrySections(
        CapabilityRegistry<SpikeContext> capabilities) => new CapabilitySectionRegistry<SpikeModel, SpikeContext>(capabilities)
        .Add<MetadataSection>(m => m.IsManagedAssembly)
        .Add<DecompiledSourceSection>(m => m.IsManagedAssembly)
        .Add<OriginalSourceSection>(m => m.HasSourceLink)
        .Add<CallsSection>(m => m.HasMethodBodies)
        .Add<FactsSection>(m => m.HasMethodBodies)
        .AddCategory("@Projections", "Calls", "Facts")
        .AddCategory("@Source", "Decompiled Source", "Original Source");
}
