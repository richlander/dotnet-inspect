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
    public bool Info { get; init; }
    public bool Noisy { get; init; }
    public bool ListedInCatalog { get; init; } = true;
    public bool ProbeEffectiveness { get; init; } = true;
    public SectionCapabilities Capabilities { get; init; }
    public SectionSizeClass SizeClass { get; init; }
    public SectionCost Cost { get; init; }
    public required string? ScannerKey { get; init; }
    public bool HasExplicitApplicability { get; init; }
    public required Func<TModel, bool> IsApplicable { get; init; }
    public required Func<TModel, bool> CanRender { get; init; }
}

public sealed record SectionCategory(string Name, string[] Sections);

public static class SectionAnnotations
{
    public const string OptIn = "opt-in";
    public const string Verbose = "verbose";
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
    private readonly List<SectionCategory> _categories = [];
    private bool _curatedCatalog;

    public const string DefaultCategory = "@Default";
    public const string AllCategory = "@All";
    public const string HiddenCategory = "@Hidden";

    /// <summary>
    /// Opts this pipeline into the curated-catalog taxonomy: <c>@All</c> is the visible pole
    /// (Default + Terse + <see cref="SectionEntry{TModel}.Noisy"/> sections, excluding expensive and
    /// feeder sections), the top-level <c>-D</c> catalog lists exactly the <c>@All</c> members, and
    /// <c>@Hidden</c> is the computed complement (sections surfaced by no listed category), reachable
    /// only via <c>--schema</c> or by exact name. Pipelines that do not opt in keep the legacy model
    /// where <c>@All</c> is every renderable section. Transitional: removed once every command migrates.
    /// </summary>
    public SectionPipeline<TModel> UseCuratedCatalog()
    {
        _curatedCatalog = true;
        return this;
    }

    /// <summary>
    /// Membership test for the visible <c>@All</c> pole (curated catalogs only), computed from flags
    /// alone so it is independent of any model: a section is included when it is cheap and is either
    /// auto-selectable by verbosity or an explicitly opt-in <see cref="SectionEntry{TModel}.Noisy"/>
    /// surface section. Expensive sections and non-noisy feeders are excluded.
    /// </summary>
    private static bool IsAllMember(SectionEntry<TModel> entry)
        => !entry.IsExpensive && (!entry.ExplicitOnly || entry.Noisy);

    /// <summary>
    /// Registers a section descriptor. The descriptor type is never instantiated —
    /// only its static members are accessed.
    /// </summary>
    public SectionPipeline<TModel> Add<TDescriptor>(
        Func<TModel, bool>? isApplicable = null) where TDescriptor : ISectionDescriptor<TModel>
    {
        return Add(new SectionEntry<TModel>
        {
            Name = TDescriptor.Name,
            IsExpensive = TDescriptor.IsExpensive,
            ExplicitOnly = TDescriptor.ExplicitOnly,
            Info = TDescriptor.Info,
            Noisy = TDescriptor.Noisy,
            ListedInCatalog = TDescriptor.ListedInCatalog,
            ProbeEffectiveness = TDescriptor.ProbeEffectiveness,
            Capabilities = TDescriptor.Capabilities,
            SizeClass = TDescriptor.SizeClass,
            Cost = TDescriptor.Cost,
            ScannerKey = TDescriptor.ScannerKey,
            HasExplicitApplicability = isApplicable != null,
            IsApplicable = isApplicable ?? TDescriptor.CanRender,
            CanRender = TDescriptor.CanRender,
        });
    }

    /// <summary>
    /// Registers an already-materialized section entry. Registry adapters use this overload to
    /// derive runtime selection metadata from a richer descriptor without duplicating it on
    /// <see cref="ISectionDescriptor{TModel}"/>.
    /// </summary>
    public SectionPipeline<TModel> Add(SectionEntry<TModel> entry)
    {
        if (!entry.ProbeEffectiveness && !entry.ExplicitOnly && !entry.HasExplicitApplicability)
            throw new InvalidOperationException(
                $"{entry.Name} sets ProbeEffectiveness=false and must be explicit-only or " +
                "provide a structural applicability predicate.");

        _entries.Add(entry);
        return this;
    }

    /// <summary>
    /// Declares a topical category door over already-registered sections. Members are validated
    /// against the registered section names, so a rename that misses a membership list fails at
    /// construction instead of silently dropping the section out of its category.
    /// </summary>
    public SectionPipeline<TModel> AddCategory(string name, params string[] sections)
    {
        if (!name.StartsWith("@", StringComparison.Ordinal))
            throw new ArgumentException("Section category names must start with '@'.", nameof(name));

        var known = _entries.Select(e => e.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var unknown = sections.Where(s => !known.Contains(s)).ToArray();
        if (unknown.Length > 0)
            throw new InvalidOperationException(
                $"Category {name} lists unregistered section(s): {string.Join(", ", unknown)}. " +
                "Category membership must name a registered section; use the SectionNames constant " +
                "the descriptor returns so renames move both together.");

        _categories.Add(new SectionCategory(name, sections));
        return this;
    }

    /// <summary>All registered section names, in registration order.</summary>
    public string[] AllSectionNames => _entries.Select(e => e.Name).ToArray();

    /// <summary>
    /// All distinct section names in alphabetical order (case-insensitive). This is the canonical
    /// render order: sections always appear alphabetically regardless of their registration or
    /// view-model declaration order, so the same sections sort the same way in every view and
    /// selection (default ladder, category doors, <c>@All</c>).
    /// </summary>
    public IReadOnlyList<string> AlphabeticalSectionOrder => _entries
        .Select(e => e.Name)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
        .ToList();

    /// <summary>
    /// The authored topical category doors (e.g. <c>@Audit</c>, <c>@Source</c>). Excludes the
    /// computed/selector-only poles <c>@Default</c>, <c>@All</c>, and <c>@Hidden</c>. These are the
    /// only categories the curated <c>-D</c> catalog lists as doors.
    /// </summary>
    public IReadOnlySet<string> GetListedCategoryDoors()
        => _categories.Select(c => c.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Section names that are independently selectable with <c>-S</c>. Headless context
    /// sections such as "Summary" are registered so renderers can include compact preambles,
    /// but they are not standalone output sections and must not be advertised by discovery
    /// or accepted as direct selectors.
    /// </summary>
    public string[] SelectableSectionNames => _entries
        .Where(IsSelectable)
        .Select(e => e.Name)
        .ToArray();

    /// <summary>Sections in the curated @Default preset, in registration order.</summary>
    public string[] InfoSectionNames => _entries.Where(e => e.Info && IsSelectable(e)).Select(e => e.Name).ToArray();

    public IReadOnlyDictionary<string, string[]> GetCategoryMap()
    {
        Dictionary<string, string[]> categories = new(StringComparer.OrdinalIgnoreCase)
        {
            [DefaultCategory] = InfoSectionNames,
            [AllCategory] = _curatedCatalog
                ? _entries.Where(e => IsSelectable(e) && IsAllMember(e)).Select(e => e.Name).ToArray()
                : SelectableSectionNames
        };

        foreach (var category in _categories)
            categories[category.Name] = category.Sections;

        if (_curatedCatalog)
            categories[HiddenCategory] = GetHiddenSections().ToArray();

        return categories;
    }

    /// <summary>
    /// The computed <c>@Hidden</c> pole (curated catalogs only): selectable sections that are not
    /// <see cref="IsAllMember"/> and are surfaced by no registered category door. They are reachable
    /// only via <c>--schema</c>, <c>-S @Hidden</c>, or by exact name. Never hand-authored.
    /// </summary>
    private IEnumerable<SectionEntry<TModel>> GetHiddenSectionEntries()
    {
        var listed = _categories
            .SelectMany(c => c.Sections)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return _entries.Where(e => IsSelectable(e) && !IsAllMember(e) && !listed.Contains(e.Name));
    }

    private IEnumerable<string> GetHiddenSections()
        => GetHiddenSectionEntries().Select(e => e.Name);

    /// <summary>
    /// Names of sections omitted from the top-level discovery catalog
    /// (<see cref="SectionEntry{TModel}.ListedInCatalog"/> is false). Discovery lists these only
    /// under their curated <c>@category</c>; they remain selectable and drillable by exact name.
    /// </summary>
    public IReadOnlySet<string> GetCatalogHiddenSections()
        => _curatedCatalog
            ? _entries.Where(e => IsSelectable(e) && (!IsAllMember(e) || !e.ListedInCatalog))
                .Select(e => e.Name)
                .ToHashSet(StringComparer.OrdinalIgnoreCase)
            : _entries.Where(e => !e.ListedInCatalog)
                .Select(e => e.Name)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Maps each section name to a short annotation for discovery output:
    /// <c>"opt-in"</c> for <see cref="SectionEntry{TModel}.ExplicitOnly"/> sections (never shown
    /// in a default flow), and <c>"verbose"</c> for explicitly applicable alternate
    /// sections that render only outside the compact <c>@Default</c> preset.
    /// Default sections are omitted (no annotation).
    /// </summary>
    public Dictionary<string, string> GetCostAnnotations()
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var e in _entries)
        {
            if (e.ExplicitOnly)
            {
                map[e.Name] = SectionAnnotations.OptIn;
                continue;
            }

            if (e.HasExplicitApplicability && !e.Info)
                map[e.Name] = SectionAnnotations.Verbose;
        }
        return map;
    }

    /// <summary>
    /// Names of sections whose effectiveness must not be content-probed during discovery
    /// (<see cref="ISectionDescriptor{TModel}.ProbeEffectiveness"/> is false). Effective
    /// discovery lists these structurally via <c>IsApplicable</c> instead of rendering them,
    /// avoiding heavy content probes (e.g. opening a whole-assembly IL index).
    /// </summary>
    public HashSet<string> GetUnprobedSections()
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in _entries)
        {
            if (!e.ProbeEffectiveness)
                set.Add(e.Name);
        }
        return set;
    }

    /// <summary>
    /// Returns the names of sections that would produce output for the given model,
    /// filtered by verbosity and <c>-S</c>.
    /// </summary>
    public List<string> GetEffectiveSections(TModel model, Verbosity verbosity,
        HashSet<string>? include = null, bool fixedOverview = false)
    {
        List<string> result = [];
        for (int i = 0; i < _entries.Count; i++)
        {
            var entry = _entries[i];
            if (!IsRequested(entry, i, verbosity, include, fixedOverview))
                continue;
            if (entry.CanRender(model))
                result.Add(entry.Name);
        }
        return result;
    }

    /// <summary>
    /// Returns sections that are available for this model, independent of whether
    /// verbosity would auto-render them. Explicit <paramref name="include"/> still
    /// narrows the result.
    /// </summary>
    public List<string> GetAvailableSections(TModel model, HashSet<string>? include = null)
    {
        List<string> result = [];
        foreach (var entry in _entries)
        {
            if (include is { Count: > 0 } && !include.Contains(entry.Name))
                continue;
            if (entry.CanRender(model))
                result.Add(entry.Name);
        }
        return result;
    }

    /// <summary>
    /// Returns sections that are structurally applicable for this target,
    /// independent of whether their post-execution data has been collected.
    /// Discovery uses this over-rendering direction so selectable sections do not
    /// disappear just because their <see cref="SectionEntry{TModel}.CanRender"/>
    /// predicate depends on the section's own work.
    /// </summary>
    public List<string> GetApplicableSections(TModel model, HashSet<string>? include = null)
    {
        List<string> result = [];
        foreach (var entry in _entries)
        {
            if (include is { Count: > 0 } && !include.Contains(entry.Name))
                continue;
            if (entry.IsApplicable(model))
                result.Add(entry.Name);
        }
        return result;
    }

    /// <summary>
    /// The canonical discovery superset for <c>-D</c>, uniform across every command: a section is
    /// discoverable when it is structurally applicable (<see cref="SectionEntry{TModel}.IsApplicable"/>).
    /// Unprobed sections (<see cref="SectionEntry{TModel}.ProbeEffectiveness"/> false) still require
    /// structural applicability; they merely skip render probing so discovery never opens a
    /// whole-assembly index. Registration order is preserved.
    /// </summary>
    public List<string> GetDiscoverableSections(TModel model, HashSet<string>? include = null)
    {
        List<string> result = [];
        foreach (var entry in _entries)
        {
            if (!IsSelectable(entry))
                continue;
            if (include is { Count: > 0 } && !include.Contains(entry.Name))
                continue;
            if (entry.IsApplicable(model))
                result.Add(entry.Name);
        }
        return result;
    }

    /// <summary>
    /// Returns structurally applicable sections that were registered with an explicit
    /// applicability gate. Effective discovery preserves these across render probes
    /// because their renderability may depend on section selection, verbosity, or work
    /// triggered only after the section is chosen.
    /// </summary>
    public HashSet<string> GetExplicitlyApplicableSections(TModel model, HashSet<string>? include = null)
    {
        HashSet<string> result = new(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in _entries)
        {
            if (!entry.HasExplicitApplicability)
                continue;
            if (include is { Count: > 0 } && !include.Contains(entry.Name))
                continue;
            if (entry.IsApplicable(model))
                result.Add(entry.Name);
        }
        return result;
    }

    /// <summary>
    /// Computes the canonical render order for <c>-S @All</c>: the Minimal/default
    /// sections first (excluding headless Summary context), then every remaining
    /// renderable section in alpha order.
    /// </summary>
    public List<string> GetAllSelectorSections(TModel model)
    {
        var all = _entries
            .Where(entry => entry.CanRender(model))
            .Where(entry => !_curatedCatalog || IsAllMember(entry))
            .Select(entry => entry.Name)
            .Where(name => !string.Equals(name, "Summary", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var primary = GetEffectiveSections(model, Verbosity.Minimal)
            .Where(name => !string.Equals(name, "Summary", StringComparison.OrdinalIgnoreCase))
            .Where(name => all.Contains(name, StringComparer.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var remaining = all
            .Where(name => !primary.Contains(name, StringComparer.OrdinalIgnoreCase))
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase);

        return [.. primary, .. remaining];
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
        HashSet<string>? include = null, bool allSelector = false, bool fixedOverview = false)
    {
        var effective = allSelector
            ? GetAllSelectorSections(model)
            : GetEffectiveSections(model, verbosity, include, fixedOverview);

        if (allSelector)
            return [.. effective];

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
            if (_curatedCatalog)
                required = CuratedRequiredVerbosity(entry);
            else if (entry.IsExpensive)
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
    /// The lowest verbosity whose curated ladder would render <paramref name="entry"/>, used to
    /// auto-promote the render level when <c>-S</c> targets it. Mirrors
    /// <see cref="IsCuratedAutoRendered"/>: Verbose size or non-network-free cost needs Detailed;
    /// the target (<see cref="SectionEntry{TModel}.Info"/>) section renders from Minimal; every
    /// other bounded network-free section (Terse or Informative) first renders at Normal, since
    /// Minimal shows the target only.
    /// </summary>
    /// <remarks>
    /// For an <see cref="SectionCost.Unbounded"/> section this returns Detailed as a nominal
    /// high-water mark even though the ladder never auto-renders it at any verbosity (see
    /// <see cref="IsCuratedAutoRendered"/>). That is harmless: an Unbounded section is also
    /// <see cref="SectionEntry{TModel}.ExplicitOnly"/>, so it is reached only through an explicit
    /// include, and an explicit include overrides the ladder in <see cref="IsRequested"/>. The
    /// promoted verbosity therefore never causes it (or anything else) to auto-render.
    /// </remarks>
    private static Verbosity CuratedRequiredVerbosity(SectionEntry<TModel> entry)
    {
        if (entry.SizeClass == SectionSizeClass.Verbose || entry.Cost != SectionCost.NetworkFree)
            return Verbosity.Detailed;
        if (entry.Info)
            return Verbosity.Minimal;
        return Verbosity.Normal;
    }

    /// <summary>
    /// Returns the set of scanner keys needed to satisfy all requested sections.
    /// Sections with a null scanner key are always collected and not included.
    /// </summary>
    public HashSet<string> GetRequiredScanners(Verbosity verbosity,
        HashSet<string>? include = null, bool fixedOverview = false)
    {
        HashSet<string> scanners = [];
        for (int i = 0; i < _entries.Count; i++)
        {
            var entry = _entries[i];
            if (entry.ScannerKey == null)
                continue;
            if (IsRequested(entry, i, verbosity, include, fixedOverview))
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
        HashSet<string>? include, bool fixedOverview = false)
    {
        // Explicit include overrides verbosity (and is the only way to select ExplicitOnly sections)
        if (include is { Count: > 0 })
            return include.Contains(entry.Name);

        // Not explicitly included: ExplicitOnly sections are never auto-selected by verbosity.
        // In the curated model this covers coordinate-gated sections (IL context) only; the old
        // "keep out of the default view" reasons are expressed by size class and cost instead.
        if (entry.ExplicitOnly)
            return false;

        // Bare -S: the network-free "fixed" overview. Membership is a function of the section's
        // declared growth class + cost only (never measured length), so the set is identical for
        // every package: structurally Fixed sections that touch no network. This is deliberately
        // narrower than the -v:n ladder (which also admits package-growing Terse/Informative rows).
        if (fixedOverview && _curatedCatalog)
            return entry.SizeClass == SectionSizeClass.Fixed
                && entry.Cost == SectionCost.NetworkFree;

        // Curated catalog: the verbosity ladder is driven by declared size class + cost, not
        // section position. Everything else (@All/@Hidden, catalog listing) is computed from these.
        if (_curatedCatalog)
            return IsCuratedAutoRendered(entry, verbosity);

        // Legacy pipelines: verbosity-based selection using position and IsExpensive
        return verbosity switch
        {
            Verbosity.Quiet => index == 0 && entry.Name == "Summary", // Include headless summary at quiet
            Verbosity.Minimal => index <= GetPrimaryThreshold(),
            Verbosity.Normal => !entry.IsExpensive,
            _ => true, // Detailed: all non-ExplicitOnly sections
        };
    }

    /// <summary>
    /// Curated verbosity ladder. A section auto-renders (no explicit <c>-S</c>) when its declared
    /// <see cref="SectionEntry{TModel}.SizeClass"/> and <see cref="SectionEntry{TModel}.Cost"/>
    /// fit the view:
    /// <list type="bullet">
    ///   <item><b>Quiet</b>: no sections (the identity line is rendered by the view model).</item>
    ///   <item><b>Minimal</b>: the target section(s) only (<see cref="SectionEntry{TModel}.Info"/>).</item>
    ///   <item><b>Normal</b>: Terse + Informative, network-free.</item>
    ///   <item><b>Detailed</b>: all size classes, network-free or moderated cost (never unbounded).</item>
    /// </list>
    /// Unbounded-cost sections never auto-render at any verbosity; they are reached by exact name
    /// or an explicit category door.
    /// </summary>
    private static bool IsCuratedAutoRendered(SectionEntry<TModel> entry, Verbosity verbosity)
        => verbosity switch
        {
            Verbosity.Quiet => false,
            Verbosity.Minimal => entry.Info,
            Verbosity.Normal => entry.SizeClass <= SectionSizeClass.Informative
                && entry.Cost == SectionCost.NetworkFree,
            _ => entry.Cost != SectionCost.Unbounded, // Detailed: all sizes, bounded cost
        };

    private static bool IsSelectable(SectionEntry<TModel> entry)
        => !string.Equals(entry.Name, SectionNames.Summary, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Returns the names of requested sections (selection only — independent of <c>CanRender</c>)
    /// that declare any of the given <paramref name="capabilities"/> AND are authorized to use them.
    /// Authorization rule (keys off the user's verbosity, never an internally force-bumped value):
    /// <list type="bullet">
    ///   <item><b>MayDownloadPdb</b>/<b>MayAuditSources</b>: section is in the explicit include set OR <paramref name="userVerbosity"/> &gt;= Detailed.</item>
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
            // MayFetchSources requires explicit include; lighter network work is also allowed at -v:d.
            bool wantsFetch = (capabilities & SectionCapabilities.MayFetchSources) != 0;
            bool authorized = inInclude
                || (!wantsFetch && userVerbosity >= Verbosity.Detailed);
            if (authorized)
                result.Add(entry.Name);
        }
        return result;
    }
}
