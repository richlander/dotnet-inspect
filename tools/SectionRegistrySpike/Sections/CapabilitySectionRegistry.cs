using DotnetInspector.Sections;
using SectionRegistrySpike.Capabilities;

namespace SectionRegistrySpike.Sections;

public readonly record struct CapabilitySectionDefinition<TModel, TContext>(
    string Name,
    bool IsExpensive,
    bool ExplicitOnly,
    bool Info,
    Func<TModel, bool> IsApplicable,
    Func<TModel, bool> CanRender,
    CapabilityPlan<TContext> Plan);

public readonly record struct CapabilityCategoryDefinition(string Name, string[] Sections);

/// <summary>
/// Reusable section registry materialized from a static lambda table. Single-section plans are
/// precompiled, named/common combinations use a generated-style selection lambda, and uncommon
/// arbitrary combinations take the explicit cold compile path.
/// </summary>
public sealed class CapabilitySectionRegistry<TModel, TContext>
{
    private readonly Dictionary<string, int> _sectionIndexes;
    private readonly CapabilitySectionDefinition<TModel, TContext>[] _sectionOrder;
    private readonly Func<ulong, CapabilityPlan<TContext>?> _precompiledPlan;
    private readonly CapabilityPlan<TContext> _emptyPlan = new();

    public CapabilitySectionRegistry(
        CapabilitySectionDefinition<TModel, TContext>[] sections,
        CapabilityCategoryDefinition[] categories,
        Func<ulong, CapabilityPlan<TContext>?> precompiledPlan)
    {
        if (sections.Length > 64)
            throw new ArgumentException("The spike selection mask supports at most 64 sections.", nameof(sections));

        _sectionOrder = sections;
        _precompiledPlan = precompiledPlan;
        _sectionIndexes = new Dictionary<string, int>(sections.Length, StringComparer.OrdinalIgnoreCase);

        for (int index = 0; index < sections.Length; index++)
        {
            var section = sections[index];
            if (!_sectionIndexes.TryAdd(section.Name, index))
                throw new InvalidOperationException($"Section '{section.Name}' is already registered.");

            Pipeline.Add(new SectionEntry<TModel>
            {
                Name = section.Name,
                IsExpensive = section.IsExpensive,
                ExplicitOnly = section.ExplicitOnly,
                Info = section.Info,
                ProbeEffectiveness = section.Plan.CanExecute(CapabilityExecutionModes.Probe),
                Capabilities = SectionCapabilities.None,
                ScannerKey = null,
                HasExplicitApplicability = true,
                IsApplicable = section.IsApplicable,
                CanRender = section.CanRender,
            });
        }

        foreach (var category in categories)
            Pipeline.AddCategory(category.Name, category.Sections);
    }

    public SectionPipeline<TModel> Pipeline { get; } = new();

    public CapabilityPlan<TContext> PlanFor(IEnumerable<string> sectionNames)
    {
        ulong selection = 0;
        if (sectionNames is IReadOnlyList<string> names)
        {
            for (int index = 0; index < names.Count; index++)
                selection |= SelectionBit(names[index]);
        }
        else
        {
            foreach (var name in sectionNames)
                selection |= SelectionBit(name);
        }

        if (selection == 0)
            return _emptyPlan;
        if ((selection & (selection - 1)) == 0)
            return _sectionOrder[System.Numerics.BitOperations.TrailingZeroCount(selection)].Plan;
        if (_precompiledPlan(selection) is { } plan)
            return plan;
        return CompilePlan(selection);
    }

    private ulong SelectionBit(string name)
    {
        if (!_sectionIndexes.TryGetValue(name, out int index))
            throw new KeyNotFoundException($"Section '{name}' is not registered.");
        return 1UL << index;
    }

    private CapabilityPlan<TContext> CompilePlan(ulong selection)
    {
        int entryCount = 0;
        for (int sectionIndex = 0; sectionIndex < _sectionOrder.Length; sectionIndex++)
        {
            if ((selection & (1UL << sectionIndex)) == 0)
                continue;

            var sectionEntries = _sectionOrder[sectionIndex].Plan.Entries;
            for (int entryIndex = 0; entryIndex < sectionEntries.Length; entryIndex++)
            {
                if (!AppearsInEarlierSelectedSection(
                    selection,
                    sectionIndex,
                    sectionEntries[entryIndex].Id))
                {
                    entryCount++;
                }
            }
        }

        var entries = new CapabilityPlanEntry<TContext>[entryCount];
        int destination = 0;
        for (int sectionIndex = 0; sectionIndex < _sectionOrder.Length; sectionIndex++)
        {
            if ((selection & (1UL << sectionIndex)) == 0)
                continue;

            var sectionEntries = _sectionOrder[sectionIndex].Plan.Entries;
            for (int entryIndex = 0; entryIndex < sectionEntries.Length; entryIndex++)
            {
                var entry = sectionEntries[entryIndex];
                if (!AppearsInEarlierSelectedSection(selection, sectionIndex, entry.Id))
                    entries[destination++] = entry;
            }
        }

        SortById(entries);
        return new CapabilityPlan<TContext>(entries);
    }

    private static void SortById(CapabilityPlanEntry<TContext>[] entries)
    {
        for (int current = 1; current < entries.Length; current++)
        {
            var entry = entries[current];
            int destination = current;
            while (destination > 0 && entries[destination - 1].Id > entry.Id)
            {
                entries[destination] = entries[destination - 1];
                destination--;
            }
            entries[destination] = entry;
        }
    }

    private bool AppearsInEarlierSelectedSection(ulong selection, int sectionIndex, int entryId)
    {
        for (int priorSection = 0; priorSection < sectionIndex; priorSection++)
        {
            if ((selection & (1UL << priorSection)) == 0)
                continue;

            var priorEntries = _sectionOrder[priorSection].Plan.Entries;
            for (int priorEntry = 0; priorEntry < priorEntries.Length; priorEntry++)
            {
                if (priorEntries[priorEntry].Id == entryId)
                    return true;
            }
        }

        return false;
    }
}
