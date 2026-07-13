namespace SectionRegistrySpike.Capabilities;

/// <summary>
/// Non-generic runtime interface for a capability instance — the same bridge shape as Markout's
/// <c>IMarkoutTypeInfo</c> over <c>MarkoutTypeInfo&lt;T&gt;</c>. C# does not allow an interface with
/// static abstract members to be used as a type argument (<c>Dictionary&lt;K, ICapability&lt;T&gt;&gt;</c>
/// fails to compile with CS8920), so runtime storage/dispatch goes through this interface while
/// <see cref="ICapability{TContext}"/> carries the static declarative metadata and is used only as
/// a generic constraint.
/// </summary>
/// <typeparam name="TContext">The execution context type shared by all capabilities in a registry.</typeparam>
public interface ICapabilityWork<TContext>
{
    /// <summary>
    /// Executes this capability's work against <paramref name="context"/>. Dependencies are
    /// guaranteed to have already executed in <paramref name="session"/> by the time this runs —
    /// fetch their results with <see cref="CapabilitySession{TContext}.GetExecuted{TCapability}"/>.
    /// </summary>
    ValueTask ExecuteAsync(TContext context, CapabilitySession<TContext> session);
}

/// <summary>
/// A concrete unit of executable work. Capabilities are registered manually with an explicit
/// <c>new()</c> factory constraint (see <see cref="CapabilityRegistry{TContext}.Register{TCapability}"/>)
/// — the registry never uses <c>Activator</c> or reflection to construct them. The static members
/// below are declarative metadata read once at registration time (mirroring
/// <c>ISectionDescriptor&lt;TModel&gt;</c>'s static abstract members); <see cref="ICapabilityWork{TContext}.ExecuteAsync"/>
/// is the only instance behavior, and it only runs when a plan actually selects this capability.
/// This interface is used only as a generic constraint — never as a stored/instance type — because
/// static abstract members cannot appear on a type used as a type argument.
/// </summary>
/// <typeparam name="TContext">The execution context type shared by all capabilities in a registry.</typeparam>
public interface ICapability<TContext> : ICapabilityWork<TContext>
{
    /// <summary>Capability display name, used in traces and diagnostics.</summary>
    static abstract string Name { get; }

    /// <summary>
    /// Whether this capability may run during effective discovery probing without explicit
    /// selection. Discovery only executes a section's capability closure when every capability in
    /// the closure (this capability and everything it transitively depends on) is safe to probe.
    /// </summary>
    static abstract bool SafeToProbe { get; }

    /// <summary>Capabilities that must execute (and be memoized) before this one.</summary>
    static abstract CapabilityKey[] DependsOn { get; }
}
