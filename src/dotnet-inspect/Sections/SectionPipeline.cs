using DotnetInspector.Options;

namespace DotnetInspector.Sections;

/// <summary>
/// Non-generic descriptor for storage in collections.
/// Created from <see cref="ISectionDescriptor{TModel}"/> implementations.
/// </summary>
public sealed class SectionEntry<TModel>
{
    public required string Name { get; init; }
    public required bool IsExpensive { get; init; }
    public bool ExplicitOnly { get; init; }
    public SectionCapabilities Capabilities { get; init; }
    public required string? ScannerKey { get; init; }
    public required Func<TModel, bool> CanRender { get; init; }
}

/// <summary>
/// Pipeline that computes the effective set of sections to render
/// based on registered descriptors, verbosity, and <c>-S</c> filters.
/// Verbosity is mapped to section selection via two axes:
/// <list type="bullet">
///   <item><b>Position</b>: index 0 is the primary section (index 0–1 if the first entry is named "Summary").</item>
///   <item><b>IsExpensive</b>: sections requiring network or heavy computation are only shown at Detailed.</item>
/// </list>
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
            IsExpensive = TDescriptor.IsExpensive,
            ExplicitOnly = TDescriptor.ExplicitOnly,
            Capabilities = TDescriptor.Capabilities,
            ScannerKey = TDescriptor.ScannerKey,
            CanRender = TDescriptor.CanRender,
        });
        return this;
    }

    /// <summary>All registered section names, in registration order.</summary>
    public string[] AllSectionNames => _entries.Select(e => e.Name).ToArray();

    /// <summary>
    /// Maps each section name to a short cost-tier annotation for discovery output:
    /// <c>"opt-in"</c> for <see cref="SectionEntry{TModel}.ExplicitOnly"/> sections (never shown
    /// in a default flow), <c>"expensive"</c> for sections that touch the network or do heavy work
    /// and so only appear at Detailed verbosity. Cheap default sections are omitted (no annotation).
    /// </summary>
    public Dictionary<string, string> GetCostAnnotations()
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var e in _entries)
        {
            if (e.ExplicitOnly)
                map[e.Name] = "opt-in";
            else if (e.IsExpensive)
                map[e.Name] = "expensive";
        }
        return map;
    }

    /// <summary>
    /// Returns the names of sections that would produce output for the given model,
    /// filtered by verbosity and <c>-S</c>.
    /// </summary>
    public List<string> GetEffectiveSections(TModel model, Verbosity verbosity,
        HashSet<string>? include = null)
    {
        List<string> result = [];
        for (int i = 0; i < _entries.Count; i++)
        {
            var entry = _entries[i];
            if (!IsRequested(entry, i, verbosity, include))
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
    public (List<string> Empty, int RequestedCount) GetEmptySections(TModel model, Verbosity verbosity,
        HashSet<string>? include = null)
    {
        if (include is not { Count: > 0 })
            return ([], 0);

        List<string> empty = [];
        int requested = 0;
        for (int i = 0; i < _entries.Count; i++)
        {
            var entry = _entries[i];
            if (!IsRequested(entry, i, verbosity, include))
                continue;
            requested++;
            if (!entry.CanRender(model))
                empty.Add(entry.Name);
        }
        return (empty, requested);
    }

    /// <summary>
    /// Lists sections that have content at <see cref="Verbosity.Detailed"/>.
    /// Used by bare <c>-S</c> with input to discover which sections have data.
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
        HashSet<string>? include = null)
    {
        var effective = GetEffectiveSections(model, verbosity, include);

        // If all registered sections are effective, no filter needed
        if (effective.Count == _entries.Count)
            return null;

        return [.. effective];
    }

    /// <summary>
    /// Returns the minimum verbosity needed to show all sections in the
    /// <paramref name="include"/> set. Used to auto-promote verbosity when
    /// <c>-S</c> targets specific sections.
    /// </summary>
    public Verbosity GetRequiredVerbosity(HashSet<string>? include)
    {
        if (include == null || include.Count == 0)
            return Verbosity.Quiet;

        int primaryThreshold = GetPrimaryThreshold();
        var maxVerbosity = Verbosity.Quiet;

        for (int i = 0; i < _entries.Count; i++)
        {
            var entry = _entries[i];
            if (!include.Contains(entry.Name))
                continue;

            Verbosity required;
            if (entry.IsExpensive)
                required = Verbosity.Detailed;
            else if (i > primaryThreshold)
                required = Verbosity.Normal;
            else
                required = Verbosity.Quiet;

            if (required > maxVerbosity)
                maxVerbosity = required;
        }
        return maxVerbosity;
    }

    /// <summary>
    /// Returns the set of scanner keys needed to satisfy all requested sections.
    /// Sections with a null scanner key are always collected and not included.
    /// </summary>
    public HashSet<string> GetRequiredScanners(Verbosity verbosity,
        HashSet<string>? include = null)
    {
        HashSet<string> scanners = [];
        for (int i = 0; i < _entries.Count; i++)
        {
            var entry = _entries[i];
            if (entry.ScannerKey == null)
                continue;
            if (IsRequested(entry, i, verbosity, include))
                scanners.Add(entry.ScannerKey);
        }
        return scanners;
    }

    /// <summary>
    /// The primary threshold index. Quiet renders no sections (hero line from view model).
    /// Minimal shows entries at index ≤ this threshold.
    /// If the first entry is named "Summary" (headless preamble), the threshold is 1
    /// (Summary + the next entry). Otherwise, the threshold is everything before the
    /// first expensive entry — or all entries if nothing is expensive.
    /// </summary>
    private int GetPrimaryThreshold()
    {
        if (_entries.Count == 0)
            return 0;

        // Summary convention: headless preamble extends primary to include next entry
        if (_entries[0].Name == "Summary")
            return Math.Min(1, _entries.Count - 1);

        // Primary is everything before the first expensive entry
        for (int i = 0; i < _entries.Count; i++)
        {
            if (_entries[i].IsExpensive)
                return Math.Max(0, i - 1);
        }

        // No expensive entries: all are primary
        return _entries.Count - 1;
    }

    private bool IsRequested(SectionEntry<TModel> entry, int index, Verbosity verbosity,
        HashSet<string>? include)
    {
        // Explicit include overrides verbosity (and is the only way to select ExplicitOnly sections)
        if (include is { Count: > 0 })
            return include.Contains(entry.Name);

        // Not explicitly included: ExplicitOnly sections are never auto-selected by verbosity
        if (entry.ExplicitOnly)
            return false;

        // Verbosity-based selection using position and IsExpensive
        return verbosity switch
        {
            Verbosity.Quiet => index == 0 && entry.Name == "Summary", // Include headless summary at quiet
            Verbosity.Minimal => index <= GetPrimaryThreshold(),
            Verbosity.Normal => !entry.IsExpensive,
            _ => true, // Detailed: all non-ExplicitOnly sections
        };
    }

    /// <summary>
    /// Returns the names of requested sections (selection only — independent of <c>CanRender</c>)
    /// that declare any of the given <paramref name="capabilities"/> AND are authorized to use them.
    /// Authorization rule (keys off the user's verbosity, never an internally force-bumped value):
    /// <list type="bullet">
    ///   <item><b>MayDownloadPdb</b>: section is in the explicit include set OR <paramref name="userVerbosity"/> &gt;= Detailed.</item>
    ///   <item><b>MayFetchSources</b>: section is in the explicit include set (never by verbosity).</item>
    /// </list>
    /// Selection (not <c>CanRender</c>) is used deliberately so the work that *produces* a section's
    /// data can run before that data exists.
    /// </summary>
    public HashSet<string> GetAuthorizedSections(SectionCapabilities capabilities,
        Verbosity userVerbosity, HashSet<string>? include)
    {
        HashSet<string> result = new(StringComparer.OrdinalIgnoreCase);
        bool explicitInclude = include is { Count: > 0 };
        for (int i = 0; i < _entries.Count; i++)
        {
            var entry = _entries[i];
            if ((entry.Capabilities & capabilities) == 0)
                continue;
            if (!IsRequested(entry, i, userVerbosity, include))
                continue;

            bool inInclude = explicitInclude && include!.Contains(entry.Name);
            // MayFetchSources requires explicit include; MayDownloadPdb also allowed at -v:d.
            bool wantsFetch = (capabilities & SectionCapabilities.MayFetchSources) != 0;
            bool authorized = inInclude
                || (!wantsFetch && userVerbosity >= Verbosity.Detailed);
            if (authorized)
                result.Add(entry.Name);
        }
        return result;
    }
}
