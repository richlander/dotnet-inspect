using SectionRegistrySpike.Capabilities;

namespace SectionRegistrySpike.Sections;

public readonly struct MetadataCapability : ICapability<SpikeContext>
{
    public static string Name => "Metadata";
    public static CapabilityExecutionModes AllowedModes => CapabilityExecutionModes.All;
    public static CapabilityKey[] DependsOn => [];

    public static ValueTask ExecuteAsync(SpikeContext context)
    {
        context.WorkCount++;
        context.Model.MetadataLoaded = true;
        return ValueTask.CompletedTask;
    }
}

public readonly struct DecompileCapability : ICapability<SpikeContext>
{
    public static string Name => "Decompile";
    public static CapabilityExecutionModes AllowedModes => CapabilityExecutionModes.Explicit;
    public static CapabilityKey[] DependsOn => [];

    public static ValueTask ExecuteAsync(SpikeContext context)
    {
        context.WorkCount++;
        context.Model.DecompiledSource = "// decompiled source (representative)";
        return ValueTask.CompletedTask;
    }
}

public readonly struct AcquirePdbCapability : ICapability<SpikeContext>
{
    public static string Name => "AcquirePdb";
    public static CapabilityExecutionModes AllowedModes =>
        CapabilityExecutionModes.Detailed | CapabilityExecutionModes.Explicit;
    public static CapabilityKey[] DependsOn => [];

    public static ValueTask ExecuteAsync(SpikeContext context)
    {
        context.WorkCount++;
        context.Model.PdbAcquired = true;
        return ValueTask.CompletedTask;
    }
}

public readonly struct FetchSourceCapability : ICapability<SpikeContext>
{
    public static string Name => "FetchSource";
    public static CapabilityExecutionModes AllowedModes => CapabilityExecutionModes.Explicit;
    public static CapabilityKey[] DependsOn => [CapabilityKey.Of<AcquirePdbCapability>()];

    public static async ValueTask ExecuteAsync(SpikeContext context)
    {
        if (!context.Model.PdbAcquired)
            throw new InvalidOperationException("FetchSource ran before its AcquirePdb dependency completed.");

        await Task.Yield();
        context.WorkCount++;
        context.Model.OriginalSource = "// original source text (representative)";
    }
}

public readonly struct BodyIndexCapability : ICapability<SpikeContext>
{
    public static string Name => "BodyIndex";
    public static CapabilityExecutionModes AllowedModes => CapabilityExecutionModes.Explicit;
    public static CapabilityKey[] DependsOn => [];

    public static ValueTask ExecuteAsync(SpikeContext context)
    {
        context.WorkCount++;
        context.BodyIndex = 42;
        return ValueTask.CompletedTask;
    }
}

public readonly struct CallsCapability : ICapability<SpikeContext>
{
    public static string Name => "Calls";
    public static CapabilityExecutionModes AllowedModes => CapabilityExecutionModes.Explicit;
    public static CapabilityKey[] DependsOn => [CapabilityKey.Of<BodyIndexCapability>()];

    public static ValueTask ExecuteAsync(SpikeContext context)
    {
        context.WorkCount++;
        context.Model.Calls = context.BodyIndex;
        return ValueTask.CompletedTask;
    }
}

public readonly struct FactsCapability : ICapability<SpikeContext>
{
    public static string Name => "Facts";
    public static CapabilityExecutionModes AllowedModes => CapabilityExecutionModes.Explicit;
    public static CapabilityKey[] DependsOn => [CapabilityKey.Of<BodyIndexCapability>()];

    public static ValueTask ExecuteAsync(SpikeContext context)
    {
        context.WorkCount++;
        context.Model.Facts = context.BodyIndex;
        return ValueTask.CompletedTask;
    }
}

public readonly struct DeepScanCapability : ICapability<SpikeContext>
{
    public static string Name => "DeepScan";
    public static CapabilityExecutionModes AllowedModes => CapabilityExecutionModes.Explicit;
    public static CapabilityKey[] DependsOn => [];

    public static ValueTask ExecuteAsync(SpikeContext context)
    {
        context.WorkCount++;
        context.Model.DeepScanRan = true;
        return ValueTask.CompletedTask;
    }
}
