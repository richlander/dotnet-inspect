using ILInspector.Metadata;

namespace DotnetInspector.Queries;

/// <summary>
/// One product-owned filter facet for an API inventory.
/// </summary>
/// <param name="Id">
/// Stable opaque identity. Consumers retain and submit this value without interpreting it.
/// </param>
/// <param name="SingularLabel">Display label for one result.</param>
/// <param name="PluralLabel">Display label for zero or multiple results.</param>
/// <param name="Weight">Producer-owned display order.</param>
/// <param name="Count">Number of items in the unfiltered inventory that belong to this facet.</param>
/// <param name="IsDefault">Whether this facet participates when no explicit selection is supplied.</param>
public sealed record ApiFacetDescriptor(
    string Id,
    string SingularLabel,
    string PluralLabel,
    int Weight,
    int Count,
    bool IsDefault);

/// <summary>
/// Selects type-kind facets. Null or empty means the producer-declared defaults.
/// </summary>
public sealed record ApiTypeInventoryRequest(
    IReadOnlyCollection<string>? KindFacetIds = null);

/// <summary>
/// Type inventory and the available type-kind facets for its unfiltered input.
/// </summary>
public sealed record ApiTypeInventoryResult(
    IReadOnlyList<ApiType> Types,
    IReadOnlyList<ApiFacetDescriptor> KindFacets);

/// <summary>
/// Selects member-kind facets. Null or empty means the producer-declared defaults.
/// </summary>
public sealed record ApiMemberInventoryRequest(
    IReadOnlyCollection<string>? KindFacetIds = null);

/// <summary>
/// Member inventory and the available member-kind facets for its unfiltered input.
/// </summary>
public sealed record ApiMemberInventoryResult(
    IReadOnlyList<ApiMember> Members,
    IReadOnlyList<ApiFacetDescriptor> KindFacets);

/// <summary>
/// Product-owned type and member inventory queries.
/// </summary>
/// <remarks>
/// Raw <see cref="ApiType.Kind"/> and <see cref="ApiMember.Kind"/> values remain metadata facts.
/// This query owns the filter identity, grouping, labels, order, defaults, and application so a
/// consumer never has to parse or classify those facts.
/// </remarks>
public static class ApiInventoryQuery
{
    sealed record FacetDefinition<T>(
        string Id,
        string SingularLabel,
        string PluralLabel,
        int Weight,
        bool IsDefault,
        Func<T, bool> Matches);

    static readonly IReadOnlyList<FacetDefinition<ApiType>> TypeKindFacets =
    [
        new("api.type-kind.class", "class", "classes", 100, true, type => type.Kind == "class"),
        new("api.type-kind.struct", "struct", "structs", 200, true, type => type.Kind == "struct"),
        new("api.type-kind.interface", "interface", "interfaces", 300, true, type => type.Kind == "interface"),
        new("api.type-kind.enum", "enum", "enums", 400, true, type => type.Kind == "enum"),
        new("api.type-kind.delegate", "delegate", "delegates", 500, true, type => type.Kind == "delegate"),
    ];

    static readonly IReadOnlyList<FacetDefinition<ApiMember>> MemberKindFacets =
    [
        new("api.member-kind.constructor", "constructor", "constructors", 100, true,
            member => member.Kind == "constructor"),
        new("api.member-kind.finalizer", "finalizer", "finalizers", 200, true,
            member => member.Kind == "finalizer"),
        new("api.member-kind.constant", "constant", "constants", 300, true,
            member => member.IsConst),
        new("api.member-kind.field", "field", "fields", 400, true,
            member => member.Kind == "field" && !member.IsConst),
        new("api.member-kind.property", "property", "properties", 500, true,
            member => member.Kind == "property"),
        new("api.member-kind.method", "method", "methods", 600, true,
            member => member.Kind == "method" && !member.IsExtension),
        new("api.member-kind.operator", "operator", "operators", 700, true,
            member => member.Kind == "operator"),
        new("api.member-kind.extension-method", "extension method", "extension methods", 800, true,
            member => member.Kind == "extension-method" || member.IsExtension),
        new("api.member-kind.explicit-implementation", "explicit implementation", "explicit implementations", 900, true,
            member => member.Kind == "explicit-interface-implementation"),
        new("api.member-kind.event", "event", "events", 1000, true,
            member => member.Kind == "event"),
    ];

    /// <summary>
    /// Returns the available ordered type-kind descriptors and the projection selected by their
    /// opaque IDs.
    /// </summary>
    public static ApiTypeInventoryResult Types(
        ApiSurface surface,
        ApiTypeInventoryRequest? request = null)
    {
        ArgumentNullException.ThrowIfNull(surface);

        var descriptors = Describe(surface.Types, TypeKindFacets, "type");
        var selected = SelectIds(request?.KindFacetIds, TypeKindFacets);
        var types = surface.Types
            .Where(type => selected.Contains(Classify(type, TypeKindFacets, "type")))
            .ToList();
        return new ApiTypeInventoryResult(types, descriptors);
    }

    /// <summary>
    /// Returns the available ordered member-kind descriptors and the projection selected by their
    /// opaque IDs.
    /// </summary>
    public static ApiMemberInventoryResult Members(
        ApiType type,
        ApiMemberInventoryRequest? request = null)
    {
        ArgumentNullException.ThrowIfNull(type);

        var descriptors = Describe(type.Members, MemberKindFacets, "member");
        var selected = SelectIds(request?.KindFacetIds, MemberKindFacets);
        var members = type.Members
            .Where(member => selected.Contains(Classify(member, MemberKindFacets, "member")))
            .ToList();
        return new ApiMemberInventoryResult(members, descriptors);
    }

    static IReadOnlyList<ApiFacetDescriptor> Describe<T>(
        IReadOnlyList<T> items,
        IReadOnlyList<FacetDefinition<T>> definitions,
        string subject)
    {
        Dictionary<string, int> counts = new(StringComparer.Ordinal);
        foreach (var item in items)
        {
            var id = Classify(item, definitions, subject);
            counts[id] = counts.GetValueOrDefault(id) + 1;
        }

        return definitions
            .Where(definition => counts.ContainsKey(definition.Id))
            .Select(definition => new ApiFacetDescriptor(
                definition.Id,
                definition.SingularLabel,
                definition.PluralLabel,
                definition.Weight,
                counts[definition.Id],
                definition.IsDefault))
            .ToList();
    }

    static HashSet<string> SelectIds<T>(
        IReadOnlyCollection<string>? requested,
        IReadOnlyList<FacetDefinition<T>> definitions)
    {
        var known = definitions.Select(definition => definition.Id).ToHashSet(StringComparer.Ordinal);
        if (requested is null || requested.Count == 0)
        {
            return definitions
                .Where(definition => definition.IsDefault)
                .Select(definition => definition.Id)
                .ToHashSet(StringComparer.Ordinal);
        }

        var selected = requested.ToHashSet(StringComparer.Ordinal);
        var unknown = selected.Where(id => !known.Contains(id)).Order(StringComparer.Ordinal).ToList();
        if (unknown.Count > 0)
        {
            throw new ArgumentException(
                $"Unknown facet ID{(unknown.Count == 1 ? "" : "s")}: {string.Join(", ", unknown)}.",
                nameof(requested));
        }

        return selected;
    }

    static string Classify<T>(
        T item,
        IReadOnlyList<FacetDefinition<T>> definitions,
        string subject)
    {
        FacetDefinition<T>? match = null;
        foreach (var definition in definitions)
        {
            if (!definition.Matches(item))
                continue;
            if (match is not null)
            {
                throw new InvalidOperationException(
                    $"The product-owned facet catalog classifies this {subject} more than once.");
            }
            match = definition;
        }

        return match?.Id
            ?? throw new InvalidOperationException(
                $"The product-owned facet catalog does not classify this {subject}.");
    }
}
