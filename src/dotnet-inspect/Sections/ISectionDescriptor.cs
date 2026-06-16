namespace DotnetInspector.Sections;

/// <summary>
/// Metadata descriptor for a named section. Declares the section's name,
/// whether it requires expensive operations (network or heavy computation),
/// scanner dependency, and a static check for whether the model has data
/// that the section can render.
/// </summary>
/// <typeparam name="TModel">The model type this section inspects.</typeparam>
public interface ISectionDescriptor<TModel>
{
    /// <summary>Section display name (must match the MarkoutSection Name).</summary>
    static abstract string Name { get; }

    /// <summary>
    /// Whether this section requires expensive operations (network access or
    /// heavyweight computation like decompilation). Expensive sections are
    /// only shown at <see cref="Verbosity.Detailed"/>. Note: "expensive" means
    /// "show late", not "touches network" — e.g. decompiler sections are expensive but local.
    /// </summary>
    static abstract bool IsExpensive { get; }

    /// <summary>
    /// When true, this section is never auto-selected by any verbosity (not even
    /// <see cref="Verbosity.Detailed"/>); it renders only when explicitly requested via
    /// <c>-S</c>/<c>-D</c>. Use for slow, opt-in work the default flow must never trigger.
    /// </summary>
    static virtual bool ExplicitOnly => false;

    /// <summary>
    /// When true, this section is part of the command/context's curated @Default preset
    /// selected by bare <c>-S</c>.
    /// </summary>
    static virtual bool Info => false;

    /// <summary>
    /// When false, effective discovery (<c>-D</c>) lists this section using only its
    /// structural <see cref="CanRender"/> gate and never renders it to confirm it produces
    /// content. Use for sections whose content probe is heavy (e.g. opening a whole-assembly
    /// IL index): listing them structurally keeps them discoverable without paying the scan
    /// cost during discovery. The tradeoff is the section may render empty when queried.
    /// </summary>
    static virtual bool ProbeEffectiveness => true;

    /// <summary>
    /// Optional network/work capabilities this section may use to enrich itself, independent of
    /// its render behavior. Drives optional work (PDB download, source fetch) only when authorized.
    /// </summary>
    static virtual SectionCapabilities Capabilities => SectionCapabilities.None;

    /// <summary>
    /// Scanner key identifying the data collection step this section requires.
    /// Null means the section's data is always collected (core metadata).
    /// Multiple sections may share a scanner key (e.g., Unsafe and P/Invoke
    /// both require "ClassifiedMethods").
    /// </summary>
    static abstract string? ScannerKey { get; }

    /// <summary>
    /// Returns <c>true</c> if the model contains data this section can render.
    /// Called without allocating a renderer instance.
    /// </summary>
    static abstract bool CanRender(TModel model);
}
