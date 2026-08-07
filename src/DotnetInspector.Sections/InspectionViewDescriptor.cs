namespace DotnetInspector.Sections;

/// <summary>
/// Product-owned description of one selectable inspection view for a specific target.
/// </summary>
/// <remarks>
/// Section names are the existing stable selection contract, so they also serve as opaque IDs.
/// Consumers must round-trip <see cref="Id"/> rather than infer selection from <see cref="Label"/>.
/// </remarks>
public sealed record InspectionViewDescriptor(
    string Id,
    string Label,
    int Weight,
    bool IsApplicable,
    bool IsAvailable,
    bool CanRender,
    bool RenderProbeDeferred,
    bool IsDefault,
    bool IsHighValue,
    bool IsExpensive,
    bool IsExplicitOnly,
    bool IsListed,
    SectionSizeClass SizeClass,
    SectionCost Cost,
    SectionCapabilities Capabilities)
{
    /// <summary>
    /// Whether producing this view may use a network-bound budget or capability.
    /// </summary>
    public bool MayUseNetwork =>
        Cost == SectionCost.Moderated || Capabilities != SectionCapabilities.None;

    /// <summary>
    /// Whether producing this view may fetch source file bodies.
    /// </summary>
    public bool MayFetchSourceContent =>
        Capabilities.HasFlag(SectionCapabilities.MayFetchSources);

    /// <summary>
    /// Whether producing this view may perform work outside bounded default budgets.
    /// </summary>
    public bool MayDoExhaustiveWork => Cost == SectionCost.Unbounded;

    /// <summary>
    /// Whether producing this view is deferred by either the legacy or curated cost model.
    /// </summary>
    public bool MayDoExpensiveWork =>
        IsExpensive || Cost != SectionCost.NetworkFree;
}

/// <summary>
/// A validated view selection and the section names the owning pipeline consumes.
/// </summary>
public sealed record InspectionViewSelection(
    IReadOnlyList<InspectionViewDescriptor> Views,
    IReadOnlySet<string> SectionNames);
