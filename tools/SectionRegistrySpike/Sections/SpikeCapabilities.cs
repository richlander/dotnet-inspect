using SectionRegistrySpike.Capabilities;

namespace SectionRegistrySpike.Sections;

/// <summary>Cheap, safe-to-probe metadata read. No dependencies.</summary>
public sealed class MetadataCapability : ICapability<SpikeContext>
{
    public static string Name => "Metadata";
    public static bool SafeToProbe => true;
    public static CapabilityKey[] DependsOn => [];

    public ValueTask ExecuteAsync(SpikeContext context, CapabilitySession<SpikeContext> session)
    {
        context.Model.MetadataLoaded = true;
        return ValueTask.CompletedTask;
    }
}

/// <summary>Local heavy work (decompilation). Not safe to probe — expensive, but not network.</summary>
public sealed class DecompileCapability : ICapability<SpikeContext>
{
    public static string Name => "Decompile";
    public static bool SafeToProbe => false;
    public static CapabilityKey[] DependsOn => [];

    public ValueTask ExecuteAsync(SpikeContext context, CapabilitySession<SpikeContext> session)
    {
        context.Model.DecompiledSource = "// decompiled source (representative)";
        return ValueTask.CompletedTask;
    }
}

/// <summary>Network PDB acquisition. Rejects execution when the context is not authorized.</summary>
public sealed class AcquirePdbCapability : ICapability<SpikeContext>
{
    public static string Name => "AcquirePdb";
    public static bool SafeToProbe => false;
    public static CapabilityKey[] DependsOn => [];

    public ValueTask ExecuteAsync(SpikeContext context, CapabilitySession<SpikeContext> session)
    {
        if (!context.NetworkAuthorized)
            throw new InvalidOperationException(
                "AcquirePdb requires network authorization; the section was not explicitly selected.");

        context.Model.PdbAcquired = true;
        return ValueTask.CompletedTask;
    }
}

/// <summary>
/// Network source-body fetch. Depends on <see cref="AcquirePdbCapability"/> (source fetch needs
/// SourceLink URLs resolved from the PDB first). Uses <c>Task.Yield</c> to demonstrate the plan
/// executes real async work, not just synchronous stand-ins.
/// </summary>
public sealed class FetchSourceCapability : ICapability<SpikeContext>
{
    public static string Name => "FetchSource";
    public static bool SafeToProbe => false;
    public static CapabilityKey[] DependsOn => [CapabilityKey.Of<AcquirePdbCapability>()];

    public async ValueTask ExecuteAsync(SpikeContext context, CapabilitySession<SpikeContext> session)
    {
        if (!context.NetworkAuthorized)
            throw new InvalidOperationException(
                "FetchSource requires network authorization; the section was not explicitly selected.");

        _ = session.GetExecuted<AcquirePdbCapability>();
        if (!context.Model.PdbAcquired)
            throw new InvalidOperationException("FetchSource ran before its AcquirePdb dependency completed.");

        await Task.Yield();
        context.Model.OriginalSource = "// original source text (representative)";
    }
}

/// <summary>
/// Shared method-body index. Analogous to <c>ScannerContext.BodyIndex()</c> — built once and
/// reused by every capability that depends on it (<see cref="CallsCapability"/>,
/// <see cref="FactsCapability"/>).
/// </summary>
public sealed class BodyIndexCapability : ICapability<SpikeContext>
{
    public static string Name => "BodyIndex";
    public static bool SafeToProbe => false;
    public static CapabilityKey[] DependsOn => [];

    public int MethodCount { get; private set; }

    public ValueTask ExecuteAsync(SpikeContext context, CapabilitySession<SpikeContext> session)
    {
        MethodCount = 42; // representative shared computed index value
        return ValueTask.CompletedTask;
    }
}

/// <summary>Calls projection over the shared body index.</summary>
public sealed class CallsCapability : ICapability<SpikeContext>
{
    public static string Name => "Calls";
    public static bool SafeToProbe => false;
    public static CapabilityKey[] DependsOn => [CapabilityKey.Of<BodyIndexCapability>()];

    public ValueTask ExecuteAsync(SpikeContext context, CapabilitySession<SpikeContext> session)
    {
        var body = session.GetExecuted<BodyIndexCapability>();
        context.Model.Calls = body.MethodCount;
        return ValueTask.CompletedTask;
    }
}

/// <summary>Facts projection over the shared body index.</summary>
public sealed class FactsCapability : ICapability<SpikeContext>
{
    public static string Name => "Facts";
    public static bool SafeToProbe => false;
    public static CapabilityKey[] DependsOn => [CapabilityKey.Of<BodyIndexCapability>()];

    public ValueTask ExecuteAsync(SpikeContext context, CapabilitySession<SpikeContext> session)
    {
        var body = session.GetExecuted<BodyIndexCapability>();
        context.Model.Facts = body.MethodCount;
        return ValueTask.CompletedTask;
    }
}

/// <summary>
/// Heavy, not-safe-to-probe capability used only by the negative probe-safety check — kept out of
/// the representative production-like descriptor set so it does not misstate current descriptor
/// configuration.
/// </summary>
public sealed class DeepScanCapability : ICapability<SpikeContext>
{
    public static string Name => "DeepScan";
    public static bool SafeToProbe => false;
    public static CapabilityKey[] DependsOn => [];

    public ValueTask ExecuteAsync(SpikeContext context, CapabilitySession<SpikeContext> session)
    {
        context.Model.DeepScanRan = true;
        return ValueTask.CompletedTask;
    }
}
