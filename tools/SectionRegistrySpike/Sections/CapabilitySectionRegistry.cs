using DotnetInspector.Sections;
using SectionRegistrySpike.Capabilities;

namespace SectionRegistrySpike.Sections;

/// <summary>
/// Bridges <see cref="ICapabilitySectionDescriptor{TModel}"/> registrations into the real
/// <see cref="SectionPipeline{TModel}"/> (selection, verbosity, <c>-D</c>/<c>-S</c>, categories,
/// cost annotations, <c>ProbeEffectiveness</c> all stay exactly the current pipeline behavior)
/// while also recording each section's typed capability requirements for planning.
/// <c>Add&lt;TDescriptor&gt;</c> is the manual registration mechanism today. A source generator
/// could later emit the same <c>Add</c> calls and the non-generic runtime entries they produce —
/// the same relationship Markout's generated <c>MarkoutSerializerContext</c> has to
/// <c>MarkoutTypeInfo&lt;T&gt;</c> — without changing this registry's shape.
/// </summary>
/// <typeparam name="TModel">The model type sections inspect.</typeparam>
/// <typeparam name="TContext">The capability execution context type.</typeparam>
public sealed class CapabilitySectionRegistry<TModel, TContext>
{
    private readonly Dictionary<string, CapabilityKey[]> _sectionCapabilities =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _sectionOrder = [];

    public CapabilitySectionRegistry(CapabilityRegistry<TContext> capabilities)
    {
        Capabilities = capabilities;
    }

    /// <summary>The real section pipeline. Selection/schema/render-filter questions go through this unchanged.</summary>
    public SectionPipeline<TModel> Pipeline { get; } = new();

    /// <summary>The capability registry backing this section registry's typed execution planning.</summary>
    public CapabilityRegistry<TContext> Capabilities { get; }

    /// <summary>
    /// Registers a section descriptor: calls the real <see cref="SectionPipeline{TModel}.Add{TDescriptor}"/>
    /// (so registration order, categories, verbosity, and probe semantics are the real pipeline's),
    /// then records the descriptor's <see cref="ICapabilitySectionDescriptor{TModel}.RequiredCapabilities"/>
    /// for later planning.
    /// </summary>
    public CapabilitySectionRegistry<TModel, TContext> Add<TDescriptor>(Func<TModel, bool>? isApplicable = null)
        where TDescriptor : ICapabilitySectionDescriptor<TModel>
    {
        if (_sectionCapabilities.ContainsKey(TDescriptor.Name))
            throw new InvalidOperationException($"Section '{TDescriptor.Name}' is already registered.");

        Pipeline.Add<TDescriptor>(isApplicable);
        _sectionCapabilities.Add(TDescriptor.Name, [.. TDescriptor.RequiredCapabilities]);
        _sectionOrder.Add(TDescriptor.Name);
        return this;
    }

    /// <summary>Delegates directly to the real pipeline's category registration.</summary>
    public CapabilitySectionRegistry<TModel, TContext> AddCategory(string name, params string[] sections)
    {
        Pipeline.AddCategory(name, sections);
        return this;
    }

    /// <summary>Direct capability requirements declared by a single registered section.</summary>
    public IReadOnlyList<CapabilityKey> RequiredCapabilitiesFor(string sectionName)
        => _sectionCapabilities.TryGetValue(sectionName, out var caps)
            ? caps
            : throw new KeyNotFoundException($"Section '{sectionName}' is not registered.");

    /// <summary>
    /// Resolves the full dependency-ordered, deduplicated capability plan needed to populate every
    /// section in <paramref name="sectionNames"/>.
    /// </summary>
    public IReadOnlyList<CapabilityKey> PlanFor(IEnumerable<string> sectionNames)
    {
        HashSet<string> selected = new(StringComparer.OrdinalIgnoreCase);
        foreach (var name in sectionNames)
        {
            if (!_sectionCapabilities.ContainsKey(name))
                throw new KeyNotFoundException($"Section '{name}' is not registered.");
            selected.Add(name);
        }

        List<CapabilityKey> requested = [];
        foreach (var name in _sectionOrder)
        {
            if (selected.Contains(name))
                requested.AddRange(_sectionCapabilities[name]);
        }

        return Capabilities.ResolvePlan(requested);
    }
}
