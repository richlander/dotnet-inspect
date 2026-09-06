using System.Collections.Immutable;
using DotnetInspector.Options;
using DotnetInspector.Queries;

namespace DotnetInspector.Sections;

/// <summary>
/// Non-generic descriptor for storage in collections.
/// Created from <see cref="ISectionDescriptor{TModel}"/> implementations.
/// A record so the pipeline can raise <see cref="Cost"/> with <c>with</c>: a hand-written copy
/// would silently drop any property added later, and this type is never compared or used as a
/// key, so the generated equality is unused rather than load-bearing.
/// </summary>
public sealed record SectionEntry<TModel>
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
    public ImmutableArray<InspectionQueryDefinition> Queries { get; init; } = [];
    public bool HasExplicitApplicability { get; init; }
    public required Func<TModel, bool> IsApplicable { get; init; }
    public required Func<TModel, bool> CanRender { get; init; }
}

public enum SectionCategoryRole
{
    Base,
    Domain
}

public sealed record SectionCategory(string Name, SectionCategoryRole Role, string[] Sections);

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
    private bool _computedPoles = true;
    private Func<InspectionQueryDefinition, InspectionCost>? _queryCost;
    private bool _queryCostsCompiled;
    private SectionCatalog<TModel>? _compiledCatalog;

    public const string AllCategory = "@All";
    public const string HiddenCategory = "@Hidden";

    internal IReadOnlyList<SectionCategory> RegisteredCategories => _categories;

    public SectionCatalog<TModel> Compile()
    {
        if (_compiledCatalog is not null)
            return _compiledCatalog;

        for (int i = 0; i < _categories.Count; i++)
        {
            SectionCategory category = _categories[i];
            _categories[i] = category with { Sections = [.. category.Sections] };
        }

        _compiledCatalog = new SectionCatalog<TModel>(this);
        return _compiledCatalog;
    }

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
        EnsureMutable();
        _curatedCatalog = true;
        return this;
    }

    /// <summary>
    /// Binds this pipeline to the typed query registry's prerequisite-aware costs. A section
    /// backed by multiple queries inherits the maximum cost among their prerequisite closures.
    /// </summary>
    public SectionPipeline<TModel> UseQueryCosts(
        Func<InspectionQueryDefinition, InspectionCost> costOf)
    {
        EnsureMutable();
        ArgumentNullException.ThrowIfNull(costOf);
        if (_queryCostsCompiled)
        {
            throw new InvalidOperationException(
                "Query costs are owned by the compiled inspection domain.");
        }
        if (_entries.Count > 0)
            throw new InvalidOperationException(
                "UseQueryCosts must be called before any section is registered; " +
                $"{_entries.Count} section(s) are already registered and would keep their " +
                "declared cost.");

        _queryCost = costOf;
        return this;
    }

    internal SectionPipeline<TModel> UseCompiledQueryCosts(
        Func<InspectionQueryDefinition, InspectionCost> costOf)
    {
        UseQueryCosts(costOf);
        _queryCostsCompiled = true;
        return this;
    }

    /// <summary>
    /// Drops the computed <c>@All</c> and <c>@Hidden</c> poles from this pipeline's category map,
    /// making them unresolvable as selectors rather than merely undiscoverable. A command whose
    /// sections are reached through authored category doors, automatic presets, or deliberately
    /// exact-only selectors does not need computed complements or supersets. Keeping the computed
    /// poles resolvable-but-unlisted would leave a surface no discovery output describes.
    /// </summary>
    public SectionPipeline<TModel> WithoutComputedPoles()
    {
        EnsureMutable();
        _computedPoles = false;
        return this;
    }

    /// <summary>
    /// Membership test for the visible <c>@All</c> pole (curated catalogs only), computed from flags
    /// alone so it is independent of any model: a section is included when it is cheap and is either
    /// auto-selectable by verbosity or an explicitly opt-in <see cref="SectionEntry{TModel}.Noisy"/>
    /// surface section. Expensive sections and non-noisy feeders are excluded.
    /// </summary>
    /// <summary>
    /// Whether a section joins the <c>@All</c> pole, which renders every member. A
    /// <see cref="SectionEntry{TModel}.Noisy"/> section is deliberately excluded: it is a
    /// superset of narrower sections, so rendering it alongside them would emit the same
    /// rows twice. Noisy sections stay listed in the discovery catalog
    /// (<see cref="GetCatalogHiddenSections"/>) so they remain reachable by name.
    /// </summary>
    private static bool IsAllMemberIgnoringCost(SectionEntry<TModel> entry)
        => !entry.IsExpensive && !entry.ExplicitOnly;

    /// <summary>
    /// Whether a section joins the <c>@All</c> pole, which renders every member. A
    /// <see cref="SectionEntry{TModel}.Noisy"/> section is deliberately excluded: it is a
    /// superset of narrower sections, so rendering it alongside them would emit the same
    /// rows twice. Noisy sections stay listed in the discovery catalog
    /// (<see cref="GetCatalogHiddenSections"/>) so they remain reachable by name.
    ///
    /// In a curated catalog an <see cref="SectionCost.Unbounded"/> section is excluded too.
    /// <c>@All</c> renders every member, so admitting one would run unbounded work under a
    /// selector that reads as a convenience. This used to be enforced by requiring each Unbounded
    /// descriptor to *also* set IsExpensive or ExplicitOnly, which made the declaration redundant
    /// and left the two axes free to disagree; reading Cost here makes the disagreement
    /// unrepresentable instead.
    /// </summary>
    private bool IsAllMember(SectionEntry<TModel> entry)
        => IsAllMemberIgnoringCost(entry)
            && !(_curatedCatalog && entry.Cost == SectionCost.Unbounded);

    /// <summary>
    /// Registers a section descriptor. The descriptor type is never instantiated —
    /// only its static members are accessed.
    /// </summary>
    public SectionPipeline<TModel> Add<TDescriptor>(
        Func<TModel, bool>? isApplicable = null) where TDescriptor : ISectionDescriptor<TModel>
        => AddDescriptor<TDescriptor>(queries: [], isApplicable, canRender: null);

    /// <summary>
    /// Registers a descriptor with runtime applicability and renderability that
    /// depend on command context captured by the supplied predicates.
    /// </summary>
    public SectionPipeline<TModel> Add<TDescriptor>(
        Func<TModel, bool> isApplicable,
        Func<TModel, bool> canRender) where TDescriptor : ISectionDescriptor<TModel>
    {
        ArgumentNullException.ThrowIfNull(isApplicable);
        ArgumentNullException.ThrowIfNull(canRender);
        return AddDescriptor<TDescriptor>(queries: [], isApplicable, canRender);
    }

    /// <summary>
    /// Registers a section descriptor whose data is supplied by a typed query.
    /// </summary>
    public SectionPipeline<TModel> Add<TDescriptor>(
        InspectionQueryDefinition query,
        Func<TModel, bool>? isApplicable = null) where TDescriptor : ISectionDescriptor<TModel>
    {
        ArgumentNullException.ThrowIfNull(query);
        return AddDescriptor<TDescriptor>([query], isApplicable, canRender: null);
    }

    /// <summary>
    /// Registers a section whose data is supplied by multiple typed queries.
    /// </summary>
    public SectionPipeline<TModel> Add<TDescriptor>(
        IReadOnlyList<InspectionQueryDefinition> queries,
        Func<TModel, bool>? isApplicable = null) where TDescriptor : ISectionDescriptor<TModel>
    {
        ArgumentNullException.ThrowIfNull(queries);
        foreach (InspectionQueryDefinition query in queries)
            ArgumentNullException.ThrowIfNull(query);

        return AddDescriptor<TDescriptor>(queries, isApplicable, canRender: null);
    }

    private SectionPipeline<TModel> AddDescriptor<TDescriptor>(
        IReadOnlyList<InspectionQueryDefinition> queries,
        Func<TModel, bool>? isApplicable,
        Func<TModel, bool>? canRender) where TDescriptor : ISectionDescriptor<TModel>
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
            Queries = [.. queries],
            HasExplicitApplicability = isApplicable != null || canRender != null,
            IsApplicable = isApplicable ?? canRender ?? TDescriptor.CanRender,
            CanRender = canRender ?? TDescriptor.CanRender,
        });
    }

    /// <summary>
    /// Registers an already-materialized section entry. Registry adapters use this overload to
    /// derive runtime selection metadata from a richer descriptor without duplicating it on
    /// <see cref="ISectionDescriptor{TModel}"/>.
    /// </summary>
    public SectionPipeline<TModel> Add(SectionEntry<TModel> entry)
    {
        EnsureMutable();
        if (entry.Queries.IsDefault)
            throw new InvalidOperationException($"{entry.Name} has an uninitialized query set.");
        if (entry.Queries.Any(query => query is null))
            throw new ArgumentException(
                $"{entry.Name} has a null typed query binding.",
                nameof(entry));
        if (entry.Queries.Length != entry.Queries.Distinct().Count())
            throw new ArgumentException(
                $"{entry.Name} binds the same typed query more than once.",
                nameof(entry));

        if (!entry.ProbeEffectiveness && !entry.ExplicitOnly && !entry.HasExplicitApplicability)
            throw new InvalidOperationException(
                $"{entry.Name} sets ProbeEffectiveness=false and must be explicit-only or " +
                "provide a structural applicability predicate.");

        if (_queryCost is { } queryCostOf)
        {
            foreach (InspectionQueryDefinition query in entry.Queries)
            {
                SectionCost queryCost = queryCostOf(query).ToSectionCost(query);
                if (queryCost > entry.Cost)
                    entry = entry with { Cost = queryCost };
            }
        }

        // @All renders every member, so an Unbounded section must not be able to join it. Curated
        // pipelines get this from IsAllMember, which reads Cost directly. Legacy pipelines select
        // on position and IsExpensive and never consult Cost, so there the implication still has
        // to be declared.
        if (!_curatedCatalog && entry.Cost == SectionCost.Unbounded && !entry.IsExpensive && !entry.ExplicitOnly)
            throw new InvalidOperationException(
                $"{entry.Name} declares Cost=Unbounded and must also declare IsExpensive=true " +
                "or ExplicitOnly=true, otherwise it joins the @All pole.");

        _entries.Add(entry);
        return this;
    }

    /// <summary>
    /// Declares a topical category door over already-registered sections. Members are validated
    /// against the registered section names, so a rename that misses a membership list fails at
    /// construction instead of silently dropping the section out of its category.
    /// </summary>
    public SectionPipeline<TModel> AddCategory(string name, params string[] sections)
        => AddCategory(name, SectionCategoryRole.Domain, sections);

    /// <summary>
    /// Declares a category whose members form part of the command's ordinary evidence scope.
    /// Default discovery and automatic render presets derive their candidate set from the union
    /// of base categories; separate domains remain reachable through their category doors.
    /// </summary>
    public SectionPipeline<TModel> AddBaseCategory(string name, params string[] sections)
        => AddCategory(name, SectionCategoryRole.Base, sections);

    private SectionPipeline<TModel> AddCategory(
        string name,
        SectionCategoryRole role,
        params string[] sections)
    {
        EnsureMutable();
        if (!name.StartsWith("@", StringComparison.Ordinal))
            throw new ArgumentException("Section category names must start with '@'.", nameof(name));

        var known = _entries.Select(e => e.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var unknown = sections.Where(s => !known.Contains(s)).ToArray();
        if (unknown.Length > 0)
            throw new InvalidOperationException(
                $"Category {name} lists unregistered section(s): {string.Join(", ", unknown)}. " +
                "Category membership must name a registered section; use the SectionNames constant " +
                "the descriptor returns so renames move both together.");

        _categories.Add(new SectionCategory(name, role, [.. sections]));
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
    /// Every typed query declared by a registered section, independent of selection.
    /// Query identity is the instance, not its diagnostic name.
    /// </summary>
    public IReadOnlySet<InspectionQueryDefinition> DeclaredQueries => _entries
        .SelectMany(e => e.Queries)
        .ToHashSet();

    /// <summary>
    /// Section names paired with the typed query each declares, for sections that declare one.
    /// </summary>
    public IEnumerable<(string Name, InspectionQueryDefinition Query)> QueryBoundSections => _entries
        .SelectMany(e => e.Queries.Select(query => (e.Name, Query: query)));

    /// <summary>
    /// Section names paired with the <b>effective</b> cost the verbosity ladder consults after
    /// typed query costs raise their bound sections. A descriptor may still declare a higher cost
    /// than its producer and move itself off the ladder independently.
    /// </summary>
    public IEnumerable<(string Name, SectionCost Cost)> SectionCosts => _entries
        .Select(e => (e.Name, e.Cost));

    /// <summary>
    /// The authored topical category doors (e.g. <c>@Audit</c>, <c>@Source</c>). Excludes the
    /// computed/selector-only poles <c>@Default</c>, <c>@All</c>, and <c>@Hidden</c>. These are the
    /// only categories the curated <c>-D</c> catalog lists as doors.
    /// </summary>
    public IReadOnlySet<string> GetListedCategoryDoors()
        => _categories.Select(c => c.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>The authored categories that contribute members to the command's base scope.</summary>
    public IReadOnlySet<string> GetBaseCategoryDoors()
        => _categories
            .Where(category => category.Role == SectionCategoryRole.Base)
            .Select(category => category.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Selectable sections in the union of the command's base categories, preserving section
    /// registration order and de-duplicating sections that belong to more than one base category.
    /// </summary>
    public IReadOnlyList<string> BaseSectionNames
    {
        get
        {
            var baseMembers = _categories
                .Where(category => category.Role == SectionCategoryRole.Base)
                .SelectMany(category => category.Sections)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            return _entries
                .Where(entry => IsSelectable(entry) && baseMembers.Contains(entry.Name))
                .Select(entry => entry.Name)
                .ToArray();
        }
    }

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

    /// <summary>Sections in the curated default preset, in registration order.</summary>
    public string[] InfoSectionNames => _entries
        .Where(e => e.Info && IsSelectable(e) && IsCuratedAutoRendered(e, Verbosity.Minimal))
        .Select(e => e.Name)
        .ToArray();

    /// <summary>
    /// Fixed-overview membership, in registration order: base-scope sections whose row set does
    /// not grow with the target and that touch no network. This is the membership question, not
    /// "what does bare <c>-S</c> render" - a curated pipeline can mark a member
    /// <c>ExplicitOnly</c>, which keeps it out of the render. Use
    /// <see cref="BareSelectSectionNames"/> for the latter.
    /// </summary>
    /// <remarks>
    /// Curated pipelines reach this membership through the <c>fixedOverview</c> flag, which
    /// <see cref="IsRequested"/> evaluates in place. Pipelines that are not curated cannot, because
    /// their verbosity ladder is positional and the sections kept out of their default view are
    /// marked <see cref="ISectionDescriptor{TModel}.ExplicitOnly"/> - which <see cref="IsRequested"/>
    /// honours before it considers the overview at all. Those commands resolve bare <c>-S</c> to an
    /// explicit section set instead, and take it from here so both routes share one definition of
    /// what "fixed overview" means.
    /// </remarks>
    public string[] FixedOverviewSectionNames => _entries
        .Where(e => IsSelectable(e) && IsInAutomaticScope(e) && IsFixedOverviewMember(e))
        .Select(e => e.Name)
        .ToArray();

    /// <summary>
    /// The sections bare <c>-S</c> actually requests on this pipeline, in registration order, and
    /// independent of any one target: a section that is requested but has no rows belongs here and
    /// reports zero, exactly as a category member does.
    /// </summary>
    /// <remarks>
    /// This can differ from <see cref="FixedOverviewSectionNames"/> for a curated pipeline,
    /// because <see cref="IsRequested"/> rejects an
    /// <see cref="ISectionDescriptor{TModel}.ExplicitOnly"/> base member before it considers the
    /// overview. Asking <see cref="IsRequested"/> rather than restating its precedence keeps the
    /// answer correct if such a section is added. Non-curated pipelines install
    /// <see cref="FixedOverviewSectionNames"/> as an explicit include set, so for them the request
    /// is that set. Gated by <c>BareSelect_MatchesAuthoredBaseFixedOverview</c>.
    /// </remarks>
    public string[] BareSelectSectionNames => _curatedCatalog
        ? [.. _entries
            .Select((entry, index) => (entry, index))
            .Where(e => IsSelectable(e.entry)
                && IsRequested(e.entry, e.index, Verbosity.Normal, include: null, fixedOverview: true))
            .Select(e => e.entry.Name)]
        : FixedOverviewSectionNames;

    private static bool IsFixedOverviewMember(SectionEntry<TModel> entry)
        => entry.SizeClass == SectionSizeClass.Fixed && entry.Cost == SectionCost.NetworkFree;

    public IReadOnlyDictionary<string, string[]> GetCategoryMap()
    {
        Dictionary<string, string[]> categories = new(StringComparer.OrdinalIgnoreCase);
        if (_computedPoles)
        {
            categories[AllCategory] = _curatedCatalog
                ? _entries.Where(e => IsSelectable(e) && IsAllMember(e)).Select(e => e.Name).ToArray()
                : SelectableSectionNames;
        }

        foreach (var category in _categories)
            categories[category.Name] = [.. category.Sections];

        if (_curatedCatalog && _computedPoles)
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
        => _curatedCatalog && HasBaseCategoryScope
            ? _entries.Where(e => IsSelectable(e) && !IsInAutomaticScope(e))
                .Select(e => e.Name)
                .ToHashSet(StringComparer.OrdinalIgnoreCase)
            : _curatedCatalog
            ? _entries.Where(e => IsSelectable(e) && ((!IsAllMember(e) && !e.Noisy) || !e.ListedInCatalog))
                .Select(e => e.Name)
                .ToHashSet(StringComparer.OrdinalIgnoreCase)
            : _entries.Where(e => !e.ListedInCatalog)
                .Select(e => e.Name)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Maps each section name to a short annotation for discovery output:
    /// <c>"verbose"</c> for explicitly applicable alternate sections that render only outside
    /// the compact default preset. <see cref="SectionEntry{TModel}.ExplicitOnly"/> remains an
    /// execution policy and is deliberately not exposed as section identity.
    /// Default sections are omitted (no annotation).
    /// </summary>
    public Dictionary<string, string> GetCostAnnotations()
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var e in _entries)
        {
            if (!e.ExplicitOnly && e.HasExplicitApplicability && !e.Info)
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
        HashSet<string>? include = null, bool fixedOverview = false, bool explicitInclude = false)
    {
        List<string> result = [];
        for (int i = 0; i < _entries.Count; i++)
        {
            var entry = _entries[i];
            if (!IsRequested(entry, i, verbosity, include, fixedOverview, explicitInclude))
                continue;
            if (entry.CanRender(model))
                result.Add(entry.Name);
        }
        return result;
    }

    /// <summary>
    /// Returns the structural candidate set before producer-backed effectiveness is known.
    /// Commands use this to plan typed query prerequisites before production.
    /// </summary>
    public HashSet<string> GetCandidateSections(Verbosity verbosity,
        HashSet<string>? include = null, bool fixedOverview = false)
    {
        HashSet<string> result = new(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < _entries.Count; i++)
        {
            var entry = _entries[i];
            if (IsRequested(entry, i, verbosity, include, fixedOverview))
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
    public List<string> GetDiscoverableSections(
        TModel model,
        HashSet<string>? include = null,
        bool explicitInclude = false)
    {
        List<string> result = [];
        foreach (var entry in _entries)
        {
            if (!IsSelectable(entry))
                continue;
            if ((explicitInclude || include is { Count: > 0 })
                && include?.Contains(entry.Name) != true)
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
        HashSet<string>? include = null, bool allSelector = false, bool fixedOverview = false,
        bool explicitInclude = false)
    {
        var effective = allSelector
            ? GetAllSelectorSections(model)
            : GetEffectiveSections(model, verbosity, include, fixedOverview, explicitInclude);

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
    /// <see cref="IsCuratedAutoRendered"/>). That is harmless: an Unbounded section is reached
    /// only through an explicit include, and an explicit include overrides the ladder in
    /// <see cref="IsRequested"/>. The promoted verbosity therefore never causes it (or anything
    /// else) to auto-render.
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
    /// Returns the typed queries needed to satisfy all requested sections.
    /// </summary>
    /// <param name="excludeUnbounded">
    /// Keeps explicitly included unbounded sections from demanding their queries. Effective
    /// discovery uses this because <c>-S</c> narrows the discovered rows but must not turn
    /// discovery into execution of the selected section.
    /// </param>
    public HashSet<InspectionQueryDefinition> GetRequiredQueries(
        Verbosity verbosity,
        HashSet<string>? include = null,
        bool fixedOverview = false,
        InspectionTrace? trace = null,
        IReadOnlyList<HostQueryDemand>? commandDemand = null,
        bool excludeUnbounded = false)
    {
        HashSet<InspectionQueryDefinition> queries = [];
        CollectRequiredQueries(
            verbosity,
            include,
            fixedOverview,
            excludeUnbounded,
            queries,
            orderedQueries: null,
            demands: null,
            trace);

        if (commandDemand is not null)
        {
            foreach (HostQueryDemand demand in commandDemand)
            {
                queries.Add(demand.Query);
                trace?.RecordCommandQueryDemand(demand.Reason, demand.Query);
            }
        }

        trace?.RecordRequestedQueries(queries);
        return queries;
    }

    internal SectionQueryPlan CreateQueryPlan(
        Verbosity verbosity,
        HashSet<string>? include,
        bool fixedOverview,
        bool excludeUnbounded)
    {
        HashSet<InspectionQueryDefinition> queries = [];
        ImmutableArray<InspectionQueryDefinition>.Builder orderedQueries =
            ImmutableArray.CreateBuilder<InspectionQueryDefinition>();
        ImmutableArray<SectionQueryDemand>.Builder demands =
            ImmutableArray.CreateBuilder<SectionQueryDemand>();

        CollectRequiredQueries(
            verbosity,
            include,
            fixedOverview,
            excludeUnbounded,
            queries,
            orderedQueries,
            demands,
            trace: null);

        return new SectionQueryPlan(orderedQueries.ToImmutable(), demands.ToImmutable());
    }

    private void CollectRequiredQueries(
        Verbosity verbosity,
        HashSet<string>? include,
        bool fixedOverview,
        bool excludeUnbounded,
        HashSet<InspectionQueryDefinition> queries,
        ImmutableArray<InspectionQueryDefinition>.Builder? orderedQueries,
        ImmutableArray<SectionQueryDemand>.Builder? demands,
        InspectionTrace? trace)
    {
        for (int i = 0; i < _entries.Count; i++)
        {
            SectionEntry<TModel> entry = _entries[i];
            if (entry.Queries.IsDefaultOrEmpty)
                continue;
            if (excludeUnbounded && entry.Cost == SectionCost.Unbounded)
                continue;
            if (IsRequested(entry, i, verbosity, include, fixedOverview))
            {
                foreach (InspectionQueryDefinition query in entry.Queries)
                {
                    if (queries.Add(query))
                        orderedQueries?.Add(query);

                    demands?.Add(new SectionQueryDemand(entry.Name, query));
                    trace?.RecordQueryDemand(entry.Name, query);
                }
            }
        }
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

    private void EnsureMutable()
    {
        if (_compiledCatalog is not null)
        {
            throw new InvalidOperationException(
                "A compiled section pipeline is immutable. Create a new pipeline to author another catalog.");
        }
    }

    private bool IsRequested(SectionEntry<TModel> entry, int index, Verbosity verbosity,
        HashSet<string>? include, bool fixedOverview = false, bool explicitInclude = false)
    {
        // Explicit include overrides verbosity (and is the only way to select ExplicitOnly sections)
        if (explicitInclude || include is { Count: > 0 })
            return include?.Contains(entry.Name) == true;

        // Not explicitly included: ExplicitOnly sections are never auto-selected by verbosity.
        // In the curated model this covers coordinate-gated sections (IL context) only; the old
        // "keep out of the default view" reasons are expressed by size class and cost instead.
        if (entry.ExplicitOnly)
            return false;

        // Bare -S: the network-free "fixed" overview. Membership is a function of the section's
        // base-category scope + declared growth class + cost (never measured length). This is
        // deliberately narrower than the -v:n ladder, which also admits package-growing
        // Terse/Informative rows.
        if (fixedOverview && _curatedCatalog)
            return IsInAutomaticScope(entry) && IsFixedOverviewMember(entry);

        // Curated catalog: base categories define the automatic candidate scope, then the
        // verbosity ladder filters that scope by declared size class + cost.
        if (_curatedCatalog)
            return IsInAutomaticScope(entry) && IsCuratedAutoRendered(entry, verbosity);

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
    ///   <item><b>Quiet</b>: the headless <c>Summary</c> preamble only, for commands that
    ///   register one. <c>Summary</c> carries the compact identity fields and is not selectable,
    ///   so it sits outside the size/cost ladder rather than on it; commands with no
    ///   <c>Summary</c> section render their identity line from the view model instead.</item>
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
            Verbosity.Quiet => IsHeadlessSummary(entry),
            Verbosity.Minimal => entry.Info && entry.Cost != SectionCost.Unbounded,
            Verbosity.Normal => entry.SizeClass <= SectionSizeClass.Informative
                && entry.Cost == SectionCost.NetworkFree,
            _ => entry.Cost != SectionCost.Unbounded, // Detailed: all sizes, bounded cost
        };

    private static bool IsHeadlessSummary(SectionEntry<TModel> entry)
        => string.Equals(entry.Name, SectionNames.Summary, StringComparison.OrdinalIgnoreCase);

    private bool HasBaseCategoryScope
        => _categories.Any(category => category.Role == SectionCategoryRole.Base);

    private bool IsInAutomaticScope(SectionEntry<TModel> entry)
    {
        // Headless Summary is rendering context rather than selectable evidence, so it does not
        // belong to an authored category. Commands that register one still need it at -v:q after
        // adopting base categories.
        if (IsHeadlessSummary(entry))
            return true;

        if (!HasBaseCategoryScope)
            return true;

        return _categories
            .Where(category => category.Role == SectionCategoryRole.Base)
            .Any(category => category.Sections.Contains(entry.Name, StringComparer.OrdinalIgnoreCase));
    }

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
