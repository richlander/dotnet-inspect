using DotnetInspector.Sections;
using SectionRegistrySpike.Capabilities;

namespace SectionRegistrySpike.Sections;

/// <summary>
/// One authority for section selection metadata and executable requirements. It compiles each
/// descriptor's capability closure at registration time, derives probe safety, and feeds the real
/// <see cref="SectionPipeline{TModel}"/> a runtime entry.
/// </summary>
public sealed class CapabilitySectionRegistry<TModel, TContext>
{
    private sealed record RegisteredSection(
        string Name,
        CapabilityKey[] RequiredCapabilities,
        CapabilityPlan<TContext> Plan);

    private readonly Dictionary<string, RegisteredSection> _sections =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly List<RegisteredSection> _sectionOrder = [];

    public CapabilitySectionRegistry(CapabilityRegistry<TContext> capabilities)
    {
        Capabilities = capabilities;
    }

    public SectionPipeline<TModel> Pipeline { get; } = new();

    public CapabilityRegistry<TContext> Capabilities { get; }

    public CapabilitySectionRegistry<TModel, TContext> Add<TDescriptor>(
        Func<TModel, bool>? isApplicable = null)
        where TDescriptor : ICapabilitySectionDescriptor<TModel, TContext>
    {
        if (_sections.ContainsKey(TDescriptor.Name))
            throw new InvalidOperationException($"Section '{TDescriptor.Name}' is already registered.");

        CapabilityKey[] requirements = [.. TDescriptor.RequiredCapabilities];
        var plan = Capabilities.ResolvePlan(requirements);
        var section = new RegisteredSection(TDescriptor.Name, requirements, plan);

        Pipeline.Add(new SectionEntry<TModel>
        {
            Name = TDescriptor.Name,
            IsExpensive = TDescriptor.IsExpensive,
            ExplicitOnly = TDescriptor.ExplicitOnly,
            Info = TDescriptor.Info,
            ProbeEffectiveness = plan.CanExecute(CapabilityExecutionModes.Probe),
            Capabilities = SectionCapabilities.None,
            ScannerKey = null,
            HasExplicitApplicability = isApplicable != null,
            IsApplicable = isApplicable ?? TDescriptor.CanRender,
            CanRender = TDescriptor.CanRender,
        });

        _sections.Add(section.Name, section);
        _sectionOrder.Add(section);
        return this;
    }

    public CapabilitySectionRegistry<TModel, TContext> AddCategory(string name, params string[] sections)
    {
        Pipeline.AddCategory(name, sections);
        return this;
    }

    public IReadOnlyList<CapabilityKey> RequiredCapabilitiesFor(string sectionName)
        => GetSection(sectionName).RequiredCapabilities;

    /// <summary>
    /// Returns a precompiled plan without allocation for one section. Multi-section requests are
    /// canonicalized by registration order and compiled into one deduplicated plan.
    /// </summary>
    public CapabilityPlan<TContext> PlanFor(IEnumerable<string> sectionNames)
    {
        if (sectionNames is IReadOnlyCollection<string> { Count: 0 })
            return Capabilities.ResolvePlan([]);

        if (sectionNames is IReadOnlyList<string> { Count: 1 } one)
            return GetSection(one[0]).Plan;

        HashSet<string> selected = new(StringComparer.OrdinalIgnoreCase);
        foreach (var name in sectionNames)
        {
            _ = GetSection(name);
            selected.Add(name);
        }

        List<CapabilityKey> requested = [];
        foreach (var section in _sectionOrder)
        {
            if (selected.Contains(section.Name))
                requested.AddRange(section.RequiredCapabilities);
        }

        return Capabilities.ResolvePlan(requested);
    }

    private RegisteredSection GetSection(string name)
        => _sections.TryGetValue(name, out var section)
            ? section
            : throw new KeyNotFoundException($"Section '{name}' is not registered.");
}
