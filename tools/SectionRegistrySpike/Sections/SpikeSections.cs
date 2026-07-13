using DotnetInspector.Sections;
using SectionRegistrySpike.Capabilities;

namespace SectionRegistrySpike.Sections;

/// <summary>
/// Representative section descriptors for the spike, reused by both the current-style baseline
/// (plain <c>SectionPipeline&lt;SpikeModel&gt;</c>, string scanner keys) and the capability
/// registry bridge. These are illustrative stand-ins, not production section descriptors.
/// </summary>
public static class SpikeSections
{
    public const string ScannerMetadata = "Metadata";
    public const string ScannerDecompile = "Decompile";
    public const string ScannerCalls = "Calls";
    public const string ScannerFacts = "Facts";

    /// <summary>Cheap, safe-to-probe metadata section — part of the curated default preset.</summary>
    public readonly struct MetadataSection : ICapabilitySectionDescriptor<SpikeModel>
    {
        public static string Name => "Metadata";
        public static bool IsExpensive => false;
        public static bool Info => true;
        public static string? ScannerKey => ScannerMetadata;
        public static bool CanRender(SpikeModel model) => model.MetadataLoaded;
        public static CapabilityKey[] RequiredCapabilities => [CapabilityKey.Of<MetadataCapability>()];
    }

    /// <summary>Local heavy decompiled-source section — expensive but not network.</summary>
    public readonly struct DecompiledSourceSection : ICapabilitySectionDescriptor<SpikeModel>
    {
        public static string Name => "Decompiled Source";
        public static bool IsExpensive => true;
        public static bool ExplicitOnly => true;
        public static string? ScannerKey => ScannerDecompile;
        public static bool CanRender(SpikeModel model) => model.DecompiledSource != null;
        public static CapabilityKey[] RequiredCapabilities => [CapabilityKey.Of<DecompileCapability>()];
    }

    /// <summary>
    /// Network original-source section. <see cref="ScannerKey"/> is null: this mirrors production,
    /// where source/PDB network work runs through the manual
    /// <c>SectionPipeline.GetAuthorizedSections</c> + bool-branch path in
    /// <c>LibraryMetadataService</c>, not through <c>ScannerRegistry</c>.
    /// </summary>
    public readonly struct OriginalSourceSection : ICapabilitySectionDescriptor<SpikeModel>
    {
        public static string Name => "Original Source";
        public static bool IsExpensive => true;
        public static bool ExplicitOnly => true;
        public static SectionCapabilities Capabilities =>
            SectionCapabilities.MayDownloadPdb | SectionCapabilities.MayFetchSources;
        public static string? ScannerKey => null;
        public static bool CanRender(SpikeModel model) => model.OriginalSource != null;
        public static CapabilityKey[] RequiredCapabilities => [CapabilityKey.Of<FetchSourceCapability>()];
    }

    /// <summary>Calls projection — separate section sharing the body-index prerequisite with Facts.</summary>
    public readonly struct CallsSection : ICapabilitySectionDescriptor<SpikeModel>
    {
        public static string Name => "Calls";
        public static bool IsExpensive => false;
        public static bool ExplicitOnly => true;
        public static string? ScannerKey => ScannerCalls;
        public static bool CanRender(SpikeModel model) => model.Calls > 0;
        public static CapabilityKey[] RequiredCapabilities => [CapabilityKey.Of<CallsCapability>()];
    }

    /// <summary>Facts projection — separate section sharing the body-index prerequisite with Calls.</summary>
    public readonly struct FactsSection : ICapabilitySectionDescriptor<SpikeModel>
    {
        public static string Name => "Facts";
        public static bool IsExpensive => false;
        public static bool ExplicitOnly => true;
        public static string? ScannerKey => ScannerFacts;
        public static bool CanRender(SpikeModel model) => model.Facts > 0;
        public static CapabilityKey[] RequiredCapabilities => [CapabilityKey.Of<FactsCapability>()];
    }

    /// <summary>
    /// Negative-only descriptor, kept out of <see cref="CreateCapabilityRegistry"/> and the shared
    /// pipeline factories below. It declares default (true) <c>ProbeEffectiveness</c> — i.e. the
    /// section-level flag alone says "safe to structurally probe" — but its capability closure
    /// contains <see cref="DeepScanCapability"/>, which is not safe to probe. The spike's
    /// closure-derived safety check must defer it anyway; a hand-set per-section boolean could
    /// have missed that drift.
    /// </summary>
    public readonly struct MisleadingProbeSection : ICapabilitySectionDescriptor<SpikeModel>
    {
        public static string Name => "Misleading Probe";
        public static bool IsExpensive => false;
        public static string? ScannerKey => "DeepScan";
        public static bool CanRender(SpikeModel model) => model.DeepScanRan;
        public static CapabilityKey[] RequiredCapabilities => [CapabilityKey.Of<DeepScanCapability>()];
    }

    /// <summary>Registers every capability used by the representative section set (main five only).</summary>
    public static CapabilityRegistry<SpikeContext> CreateCapabilityRegistry() => new CapabilityRegistry<SpikeContext>()
        .Register<MetadataCapability>()
        .Register<DecompileCapability>()
        .Register<AcquirePdbCapability>()
        .Register<FetchSourceCapability>()
        .Register<BodyIndexCapability>()
        .Register<CallsCapability>()
        .Register<FactsCapability>();

    /// <summary>
    /// Builds the capability-bridged registry: same descriptor types, same structural
    /// applicability gates, as <see cref="CurrentBaseline.CurrentBaselinePipelines.CreatePipeline"/>.
    /// </summary>
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
