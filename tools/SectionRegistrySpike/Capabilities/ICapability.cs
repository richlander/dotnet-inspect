namespace SectionRegistrySpike.Capabilities;

/// <summary>
/// Execution strategies that may authorize a capability. Describe and structural discovery never
/// execute plans, so they do not need enum values.
/// </summary>
[Flags]
public enum CapabilityExecutionModes
{
    None = 0,
    Probe = 1 << 0,
    Detailed = 1 << 1,
    Explicit = 1 << 2,
    All = Probe | Detailed | Explicit,
}

/// <summary>
/// A stateless executable capability. Registration captures the static executor in a compiled
/// runtime plan; no capability object, reflection, or dynamic-code factory exists at execution
/// time. Per-run values remain on <typeparamref name="TContext"/>, matching the product's existing
/// scanner-context ownership.
/// </summary>
/// <typeparam name="TContext">The execution context type shared by all capabilities in a registry.</typeparam>
public interface ICapability<TContext>
{
    /// <summary>Capability display name, used in traces and diagnostics.</summary>
    static abstract string Name { get; }

    /// <summary>
    /// Strategies authorized to execute this capability. Probe safety and network authorization
    /// are derived from the complete plan instead of duplicated on each section.
    /// </summary>
    static abstract CapabilityExecutionModes AllowedModes { get; }

    /// <summary>Capabilities that must execute (and be memoized) before this one.</summary>
    static abstract CapabilityKey[] DependsOn { get; }

    /// <summary>Executes the capability against caller-owned per-run state.</summary>
    static abstract ValueTask ExecuteAsync(TContext context);
}
