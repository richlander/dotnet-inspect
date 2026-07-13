namespace SectionRegistrySpike.Capabilities;

/// <summary>
/// Identity for a registered capability type. Created from <c>typeof(TCapability)</c> — no
/// reflection scanning, <c>Activator</c>, or dynamic code, matching the spike's NativeAOT
/// constraint. A <see cref="Type"/> token is already retained by the JIT/AOT compiler for any
/// generic instantiation that is actually used, so this is a direct type-token lookup rather
/// than a scan.
/// </summary>
public readonly record struct CapabilityKey(Type Type)
{
    public static CapabilityKey Of<TCapability>() => new(typeof(TCapability));

    public override string ToString() => Type.Name;
}
