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

/// <summary>One namespace and its public Type count.</summary>
public sealed record ApiNamespaceDescriptor(
    string Name,
    int Count);

/// <summary>
/// Selects Type-kind and Type-trait facets. Null or empty Kind selection means the
/// producer-declared Kind defaults. Null or empty Trait selection means no Trait narrowing.
/// </summary>
public sealed record ApiTypeInventoryRequest(
    IReadOnlyCollection<string>? KindFacetIds = null,
    IReadOnlyCollection<string>? TraitFacetIds = null);

/// <summary>
/// Type inventory and the available type-kind facets for its unfiltered input.
/// </summary>
public sealed record ApiTypeInventoryResult(
    IReadOnlyList<ApiType> Types,
    IReadOnlyList<ApiFacetDescriptor> KindFacets,
    IReadOnlyList<ApiFacetDescriptor> TraitFacets,
    IReadOnlyList<ApiSurfaceInspectionFailure> InspectionFailures);

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

    static readonly IReadOnlyList<FacetDefinition<ApiType>> TypeTraitFacets =
    [
        new("api.type-trait.abstract", "abstract", "abstract", 100, false,
            type => type.Kind == "class" && type.IsAbstract && !type.IsStatic),
        new("api.type-trait.static", "static", "static", 200, false,
            type => type.IsStatic),
        new("api.type-trait.object", "object", "objects", 300, false,
            IsInstantiableType),
    ];

    static readonly IReadOnlyList<FacetDefinition<ApiMember>> MemberKindFacets =
    [
        new("api.member-kind.constructor", "constructor", "constructors", 100, true,
            IsConstructor),
        new("api.member-kind.finalizer", "finalizer", "finalizers", 200, true,
            member => member.Kind == "finalizer"),
        new("api.member-kind.constant", "constant", "constants", 300, true,
            member => member.Kind == "field" && member.IsConst),
        new("api.member-kind.field", "field", "fields", 400, true,
            member => member.Kind == "field" && !member.IsConst),
        new("api.member-kind.property", "property", "properties", 500, true,
            member => member.Kind == "property"),
        new("api.member-kind.method", "method", "methods", 600, true,
            member => member.Kind == "method" && !member.IsExtension && !IsStaticConstructor(member)),
        new("api.member-kind.operator", "operator", "operators", 700, true,
            member => member.Kind == "operator"),
        new("api.member-kind.extension-method", "extension method", "extension methods", 800, true,
            member => member.Kind == "extension-method"
                || (member.IsExtension && member.Kind == "method")),
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

        var kindDescriptors = DescribeExclusive(
            surface.Types,
            TypeKindFacets,
            "type");
        var traitDescriptors = DescribeOverlapping(
            surface.Types,
            TypeTraitFacets);
        var selectedKinds = SelectIds(
            request?.KindFacetIds,
            TypeKindFacets);
        var selectedTraits = SelectOptionalIds(
            request?.TraitFacetIds,
            TypeTraitFacets);
        var types = surface.Types
            .Where(type =>
                selectedKinds.Contains(
                    ClassifyExclusive(type, TypeKindFacets, "type"))
                && (selectedTraits is null
                    || TypeTraitFacets.Any(definition =>
                        selectedTraits.Contains(definition.Id)
                        && definition.Matches(type))))
            .ToList();
        return new ApiTypeInventoryResult(
            types,
            kindDescriptors,
            traitDescriptors,
            [.. surface.InspectionFailures]);
    }

    /// <summary>
    /// Returns namespace counts in ordinal display order. The empty name represents
    /// the global namespace.
    /// </summary>
    public static IReadOnlyList<ApiNamespaceDescriptor> Namespaces(
        ApiSurface surface)
    {
        ArgumentNullException.ThrowIfNull(surface);

        return surface.Types
            .GroupBy(type => type.Namespace ?? "", StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => new ApiNamespaceDescriptor(
                group.Key,
                group.Count()))
            .ToList();
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

        var descriptors = DescribeExclusive(
            type.Members,
            MemberKindFacets,
            "member");
        var selected = SelectIds(request?.KindFacetIds, MemberKindFacets);
        var members = type.Members
            .Where(member => selected.Contains(
                ClassifyExclusive(member, MemberKindFacets, "member")))
            .ToList();
        return new ApiMemberInventoryResult(members, descriptors);
    }

    public static string TypeKindFacetId(ApiType type)
    {
        ArgumentNullException.ThrowIfNull(type);
        return ClassifyExclusive(type, TypeKindFacets, "type");
    }

    public static IReadOnlyList<string> TypeTraitFacetIds(ApiType type)
    {
        ArgumentNullException.ThrowIfNull(type);
        return TypeTraitFacets
            .Where(definition => definition.Matches(type))
            .Select(definition => definition.Id)
            .ToList();
    }

    static IReadOnlyList<ApiFacetDescriptor> DescribeExclusive<T>(
        IReadOnlyList<T> items,
        IReadOnlyList<FacetDefinition<T>> definitions,
        string subject)
    {
        Dictionary<string, int> counts = new(StringComparer.Ordinal);
        foreach (var item in items)
        {
            var id = ClassifyExclusive(item, definitions, subject);
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

    static IReadOnlyList<ApiFacetDescriptor> DescribeOverlapping<T>(
        IReadOnlyList<T> items,
        IReadOnlyList<FacetDefinition<T>> definitions) =>
        definitions
            .Select(definition => new ApiFacetDescriptor(
                definition.Id,
                definition.SingularLabel,
                definition.PluralLabel,
                definition.Weight,
                items.Count(definition.Matches),
                definition.IsDefault))
            .ToList();

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

    static HashSet<string>? SelectOptionalIds<T>(
        IReadOnlyCollection<string>? requested,
        IReadOnlyList<FacetDefinition<T>> definitions)
    {
        if (requested is null || requested.Count == 0)
            return null;

        var known = definitions
            .Select(definition => definition.Id)
            .ToHashSet(StringComparer.Ordinal);
        var selected = requested.ToHashSet(StringComparer.Ordinal);
        var unknown = selected
            .Where(id => !known.Contains(id))
            .Order(StringComparer.Ordinal)
            .ToList();
        if (unknown.Count > 0)
        {
            throw new ArgumentException(
                $"Unknown facet ID{(unknown.Count == 1 ? "" : "s")}: "
                    + $"{string.Join(", ", unknown)}.",
                nameof(requested));
        }

        return selected;
    }

    static string ClassifyExclusive<T>(
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

    static bool IsInstantiableType(ApiType type) =>
        !type.IsStatic
        && !type.IsAbstract
        && type.Kind is "class" or "struct" or "enum" or "delegate";

    static bool IsConstructor(ApiMember member)
        => member.Kind == "constructor" || IsStaticConstructor(member);

    static bool IsStaticConstructor(ApiMember member)
        => member is { Kind: "method", Name: ".cctor" };
}
