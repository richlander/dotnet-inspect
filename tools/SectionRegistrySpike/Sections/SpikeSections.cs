using SectionRegistrySpike.Capabilities;

namespace SectionRegistrySpike.Sections;

/// <summary>
/// SmoothMarkdown-style static table: section metadata, applicability, rendering predicates,
/// execution lambdas, dependency order, and authorization live in one reusable definition.
/// </summary>
public static class SpikeSections
{
    static SpikeSections()
    {
    }

    private static readonly CapabilityPlanEntry<SpikeContext> s_metadata = new(
        0,
        "Metadata",
        CapabilityExecutionModes.All,
        static context =>
        {
            context.WorkCount++;
            context.Model.MetadataLoaded = true;
            return ValueTask.CompletedTask;
        });

    private static readonly CapabilityPlanEntry<SpikeContext> s_decompile = new(
        1,
        "Decompile",
        CapabilityExecutionModes.Explicit,
        static context =>
        {
            context.WorkCount++;
            context.Model.DecompiledSource = "// decompiled source (representative)";
            return ValueTask.CompletedTask;
        });

    private static readonly CapabilityPlanEntry<SpikeContext> s_acquirePdb = new(
        2,
        "AcquirePdb",
        CapabilityExecutionModes.Detailed | CapabilityExecutionModes.Explicit,
        static context =>
        {
            context.WorkCount++;
            context.Model.PdbAcquired = true;
            return ValueTask.CompletedTask;
        });

    private static readonly CapabilityPlanEntry<SpikeContext> s_fetchSource = new(
        3,
        "FetchSource",
        CapabilityExecutionModes.Explicit,
        static async context =>
        {
            if (!context.Model.PdbAcquired)
                throw new InvalidOperationException("FetchSource ran before its AcquirePdb prerequisite.");

            await Task.Yield();
            context.WorkCount++;
            context.Model.OriginalSource = "// original source text (representative)";
        });

    private static readonly CapabilityPlanEntry<SpikeContext> s_bodyIndex = new(
        4,
        "BodyIndex",
        CapabilityExecutionModes.Explicit,
        static context =>
        {
            context.WorkCount++;
            context.BodyIndex = 42;
            return ValueTask.CompletedTask;
        });

    private static readonly CapabilityPlanEntry<SpikeContext> s_calls = new(
        5,
        "Calls",
        CapabilityExecutionModes.Explicit,
        static context =>
        {
            context.WorkCount++;
            context.Model.Calls = context.BodyIndex;
            return ValueTask.CompletedTask;
        });

    private static readonly CapabilityPlanEntry<SpikeContext> s_facts = new(
        6,
        "Facts",
        CapabilityExecutionModes.Explicit,
        static context =>
        {
            context.WorkCount++;
            context.Model.Facts = context.BodyIndex;
            return ValueTask.CompletedTask;
        });

    private static readonly CapabilityPlanEntry<SpikeContext> s_deepScan = new(
        7,
        "DeepScan",
        CapabilityExecutionModes.Explicit,
        static context =>
        {
            context.WorkCount++;
            context.Model.DeepScanRan = true;
            return ValueTask.CompletedTask;
        });

    private static readonly CapabilityPlan<SpikeContext> s_deepScanPlan = new(s_deepScan);
    private static readonly CapabilityPlan<SpikeContext> s_pdbPlan = new(s_acquirePdb);
    private static readonly CapabilityPlan<SpikeContext> s_sourcePlan =
        new(s_decompile, s_acquirePdb, s_fetchSource);
    private static readonly CapabilityPlan<SpikeContext> s_projectionPlan =
        new(s_bodyIndex, s_calls, s_facts);

    private static readonly CapabilitySectionDefinition<SpikeModel, SpikeContext>[] s_sections =
    [
        new(
            "Metadata",
            IsExpensive: false,
            ExplicitOnly: false,
            Info: true,
            static model => model.IsManagedAssembly,
            static model => model.MetadataLoaded,
            new CapabilityPlan<SpikeContext>(s_metadata)),
        new(
            "Decompiled Source",
            IsExpensive: true,
            ExplicitOnly: true,
            Info: false,
            static model => model.IsManagedAssembly,
            static model => model.DecompiledSource is not null,
            new CapabilityPlan<SpikeContext>(s_decompile)),
        new(
            "Original Source",
            IsExpensive: true,
            ExplicitOnly: true,
            Info: false,
            static model => model.HasSourceLink,
            static model => model.OriginalSource is not null,
            new CapabilityPlan<SpikeContext>(s_acquirePdb, s_fetchSource)),
        new(
            "Calls",
            IsExpensive: false,
            ExplicitOnly: true,
            Info: false,
            static model => model.HasMethodBodies,
            static model => model.Calls > 0,
            new CapabilityPlan<SpikeContext>(s_bodyIndex, s_calls)),
        new(
            "Facts",
            IsExpensive: false,
            ExplicitOnly: true,
            Info: false,
            static model => model.HasMethodBodies,
            static model => model.Facts > 0,
            new CapabilityPlan<SpikeContext>(s_bodyIndex, s_facts)),
    ];

    private static readonly CapabilityCategoryDefinition[] s_categories =
    [
        new("@Projections", ["Calls", "Facts"]),
        new("@Source", ["Decompiled Source", "Original Source"]),
    ];

    public static CapabilitySectionRegistry<SpikeModel, SpikeContext> Registry { get; } =
        new(
            s_sections,
            s_categories,
            static selection => selection switch
            {
                0b00110 => s_sourcePlan,
                0b11000 => s_projectionPlan,
                _ => null,
            });

    public static CapabilityPlan<SpikeContext> PdbPlan => s_pdbPlan;

    public static CapabilitySectionRegistry<SpikeModel, SpikeContext> CreateProbeTestRegistry()
        => new(
            [
                new(
                    "Misleading Probe",
                    IsExpensive: false,
                    ExplicitOnly: true,
                    Info: false,
                    static _ => true,
                    static model => model.DeepScanRan,
                    s_deepScanPlan),
            ],
            [],
            static _ => null);
}
