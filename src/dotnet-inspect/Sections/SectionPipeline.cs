using DotnetInspector.Options;

namespace DotnetInspector.Sections;

/// <summary>
/// Non-generic descriptor for storage in collections.
/// Created from <see cref="ISectionDescriptor{TModel}"/> implementations.
/// </summary>
public sealed class SectionEntry<TModel>
{
    public required string Name { get; init; }
    public required Verbosity MinVerbosity { get; init; }
    public required string? ScannerKey { get; init; }
    public required Func<TModel, bool> CanRender { get; init; }
}

/// <summary>
/// Pipeline that computes the effective set of sections to render
/// based on registered descriptors, verbosity, and <c>-s</c>/<c>-x</c> filters.
/// </summary>
public sealed class SectionPipeline<TModel>
{
    private readonly List<SectionEntry<TModel>> _entries = [];

    /// <summary>
    /// Registers a section descriptor. The descriptor type is never instantiated —
    /// only its static members are accessed.
    /// </summary>
    public SectionPipeline<TModel> Add<TDescriptor>() where TDescriptor : ISectionDescriptor<TModel>
    {
        _entries.Add(new SectionEntry<TModel>
        {
            Name = TDescriptor.Name,
            MinVerbosity = TDescriptor.MinVerbosity,
            ScannerKey = TDescriptor.ScannerKey,
            CanRender = TDescriptor.CanRender,
        });
        return this;
    }

    /// <summary>All registered section names, in registration order.</summary>
    public string[] AllSectionNames => _entries.Select(e => e.Name).ToArray();

    /// <summary>
    /// Returns the names of sections that would produce output for the given model,
    /// filtered by verbosity and <c>-s</c>/<c>-x</c>.
    /// </summary>
    public List<string> GetEffectiveSections(TModel model, Verbosity verbosity,
        HashSet<string>? include = null, HashSet<string>? exclude = null)
    {
        List<string> result = [];
        foreach (var entry in _entries)
        {
            if (!IsRequested(entry, verbosity, include, exclude))
                continue;
            if (entry.CanRender(model))
                result.Add(entry.Name);
        }
        return result;
    }

    /// <summary>
    /// Returns sections that were requested via <paramref name="include"/> but
    /// filtered out by <see cref="SectionEntry{TModel}.CanRender"/> (no data).
    /// Empty when no explicit include was set or all requested sections have data.
    /// </summary>
    public List<string> GetEmptySections(TModel model, Verbosity verbosity,
        HashSet<string>? include = null, HashSet<string>? exclude = null)
    {
        if (include is not { Count: > 0 })
            return [];

        List<string> empty = [];
        foreach (var entry in _entries)
        {
            if (!IsRequested(entry, verbosity, include, exclude))
                continue;
            if (!entry.CanRender(model))
                empty.Add(entry.Name);
        }
        return empty;
    }

    /// <summary>
    /// Lists sections that have content at <see cref="Verbosity.Detailed"/>.
    /// Used by bare <c>-s</c> with input to discover which sections have data.
    /// </summary>
    public void ListEffectiveSections(TModel model)
    {
        foreach (var name in GetEffectiveSections(model, Verbosity.Detailed))
            Console.WriteLine(name);
    }

    /// <summary>
    /// Computes the <see cref="HashSet{String}"/> to pass as
    /// <c>MarkoutWriterOptions.IncludeSections</c>. Returns <c>null</c> when
    /// all sections should be rendered (no filtering needed).
    /// </summary>
    public HashSet<string>? ComputeIncludeSections(TModel model, Verbosity verbosity,
        HashSet<string>? include = null, HashSet<string>? exclude = null)
    {
        var effective = GetEffectiveSections(model, verbosity, include, exclude);

        // If all registered sections are effective, no filter needed
        if (effective.Count == _entries.Count)
            return null;

        return [.. effective];
    }

    /// <summary>
    /// Returns the minimum verbosity needed to show all sections in the
    /// <paramref name="include"/> set. Used to auto-promote verbosity when
    /// <c>-s</c> targets specific sections.
    /// </summary>
    public Verbosity GetRequiredVerbosity(HashSet<string>? include)
    {
        if (include == null || include.Count == 0)
            return Verbosity.Quiet;

        var maxVerbosity = Verbosity.Quiet;
        foreach (var entry in _entries)
        {
            if (include.Contains(entry.Name) && entry.MinVerbosity > maxVerbosity)
                maxVerbosity = entry.MinVerbosity;
        }
        return maxVerbosity;
    }

    /// <summary>
    /// Returns the set of scanner keys needed to satisfy all requested sections.
    /// Sections with a null scanner key are always collected and not included.
    /// </summary>
    public HashSet<string> GetRequiredScanners(Verbosity verbosity,
        HashSet<string>? include = null, HashSet<string>? exclude = null)
    {
        HashSet<string> scanners = [];
        foreach (var entry in _entries)
        {
            if (entry.ScannerKey == null)
                continue;
            if (IsRequested(entry, verbosity, include, exclude))
                scanners.Add(entry.ScannerKey);
        }
        return scanners;
    }

    private static bool IsRequested(SectionEntry<TModel> entry, Verbosity verbosity,
        HashSet<string>? include, HashSet<string>? exclude)
    {
        // Explicit include overrides verbosity
        if (include is { Count: > 0 })
            return include.Contains(entry.Name);

        // Explicit exclude
        if (exclude?.Contains(entry.Name) == true)
            return false;

        // Verbosity gate
        return verbosity >= entry.MinVerbosity;
    }
}
