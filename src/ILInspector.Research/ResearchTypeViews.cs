using ILInspector.Decompiler.Pipeline;
using ILInspector.Metadata;

namespace ILInspector.Research;

/// <summary>
/// Presentation-neutral, type-level projection that mirrors the member-level
/// <see cref="ProjectMember(MemberProjectionRequest)"/> seam. Every front end (the CLI's
/// Markdown/terminal renderer and the WASM web UI) composes type-level metadata from this one
/// source of truth instead of re-deriving modifiers, base/interface relationships, or
/// composition counts. The result is Markout-free and HTML-free: each host projects its own
/// presentation (terminal &#8594; Markdown/Mermaid, web &#8594; interactive SVG).
/// </summary>
public static partial class ResearchViews
{
    /// <summary>Role a node plays in a <see cref="TypeRelationshipGraph"/>.</summary>
    public enum TypeRelationshipRole
    {
        /// <summary>The projected type itself.</summary>
        Self,

        /// <summary>The projected type's (non-trivial) base class.</summary>
        Base,

        /// <summary>An interface the projected type implements.</summary>
        Interface,

        /// <summary>A type in the same assembly that derives from / implements the projected type.</summary>
        Derived,
    }

    /// <summary>Relation an edge expresses in a <see cref="TypeRelationshipGraph"/>.</summary>
    public enum TypeRelationshipKind
    {
        /// <summary>Class inheritance (<c>self &#8594; base</c>).</summary>
        Inherits,

        /// <summary>Interface implementation (<c>self &#8594; interface</c>).</summary>
        Implements,

        /// <summary>A derived type inheriting/implementing the projected type (<c>derived &#8594; self</c>).</summary>
        DerivedFrom,
    }

    /// <summary>
    /// Type-level identity: kind, C#-appropriate modifier keywords, accessibility, and location.
    /// </summary>
    public sealed record TypeIdentityView(
        string FullName,
        string? Namespace,
        string Name,
        string Kind,
        IReadOnlyList<string> Modifiers,
        string? Accessibility,
        string? Assembly);

    /// <summary>A generic type parameter with its variance and neutral (unspelled) constraint list.</summary>
    public sealed record TypeParameterView(
        string Name,
        string? Variance,
        IReadOnlyList<string> Constraints);

    /// <summary>
    /// Aggregate member counts for the projected type: members by kind plus flagged totals. All
    /// values are derived from <see cref="ApiMember"/> facts. <c>Async</c> is only meaningful when
    /// the members carry the body-gated async fact — the <see cref="ProjectType(TypeProjectionRequest)"/>
    /// path recovers it from live metadata before composing.
    /// </summary>
    public sealed record TypeCompositionView(
        int Methods,
        int Properties,
        int Fields,
        int Events,
        int Constructors,
        int Operators,
        int ExplicitInterfaceImplementations,
        int ExtensionMethods,
        int Static,
        int Unsafe,
        int Async,
        int Virtual,
        int Abstract,
        int Override,
        int Extension,
        int Obsolete,
        int Total);

    /// <summary>One node in a <see cref="TypeRelationshipGraph"/>, keyed by full type name.</summary>
    public sealed record TypeRelationshipNode(string Id, string DisplayName, TypeRelationshipRole Role);

    /// <summary>One directed relationship between two nodes.</summary>
    public sealed record TypeRelationshipEdge(string FromId, string ToId, TypeRelationshipKind Kind);

    /// <summary>
    /// A structured type-relationship graph (nodes carry identity + role, edges carry relation
    /// kind) so each host projects its own view. Deliberately NOT a baked Mermaid string — see
    /// docs/design/call-graph-mermaid-projection.md for the neutral-model &#8594; Mermaid pattern.
    /// </summary>
    public sealed record TypeRelationshipGraph(
        IReadOnlyList<TypeRelationshipNode> Nodes,
        IReadOnlyList<TypeRelationshipEdge> Edges);

    /// <summary>Knobs for <see cref="ProjectType(ApiType, ApiSurface?, TypeProjectionOptions?)"/>.</summary>
    public sealed record TypeProjectionOptions(
        bool PublicOnly = false,
        bool Composition = true,
        bool RelationshipGraph = true);

    /// <summary>
    /// A <see cref="MetadataSource"/>-backed projection request, parallel to
    /// <see cref="MemberProjectionRequest"/>. Resolves the type by exact <c>FullName</c> from the
    /// source's whole-assembly surface (also used for the derived-type scan) and recovers the
    /// body-gated async fact.
    /// </summary>
    public sealed record TypeProjectionRequest(
        MetadataSource Source,
        string Type,
        bool PublicOnly = false,
        bool Composition = true,
        bool RelationshipGraph = true);

    /// <summary>The composed, presentation-neutral type view.</summary>
    /// <param name="InspectionFailures">
    /// Metadata rows the surface extractor rejected and excluded from the projected relationships
    /// (a non-empty list means the derived-type/relationship view may be incomplete). The CLI
    /// surfaces these independently at the command layer, but a front end that consumes this seam
    /// directly (e.g. the WASM web UI) relies on this list so incompleteness is never rendered as
    /// success-shaped complete output. Empty when no surface was supplied or none were rejected.
    /// </param>
    public sealed record TypeProjectionResult(
        TypeIdentityView Identity,
        string? BaseType,
        IReadOnlyList<string> Interfaces,
        IReadOnlyList<string> DerivedTypes,
        IReadOnlyList<TypeParameterView> TypeParameters,
        IReadOnlyList<string> Attributes,
        string? EnumUnderlyingType,
        TypeCompositionView? Composition,
        TypeRelationshipGraph? Graph,
        IReadOnlyList<ApiSurfaceInspectionFailure> InspectionFailures);

    /// <summary>
    /// The C#-appropriate modifier keywords for a type, in declaration order
    /// (<c>static</c> | <c>abstract</c>/<c>sealed</c> for classes, <c>readonly</c>/<c>ref</c> for
    /// structs). The single source of truth for type modifiers across front ends.
    /// </summary>
    public static IReadOnlyList<string> TypeModifiers(ApiType type)
    {
        ArgumentNullException.ThrowIfNull(type);

        List<string> modifiers = [];
        if (type.IsStatic)
        {
            modifiers.Add("static");
        }
        else
        {
            if (type.IsAbstract && type.Kind == "class") modifiers.Add("abstract");
            if (type.IsSealed && type.Kind == "class") modifiers.Add("sealed");
        }

        if (type.IsReadOnly && type.Kind == "struct") modifiers.Add("readonly");
        if (type.IsByRefLike && type.Kind == "struct") modifiers.Add("ref");
        return modifiers;
    }

    /// <summary>
    /// Composes the neutral type projection from already-extracted metadata facts. When
    /// <paramref name="surface"/> is supplied it is scanned to populate derived types (a
    /// whole-assembly fact); when null, <see cref="ApiType.DerivedTypes"/> is used as-is and no
    /// scan is performed. This overload takes no <see cref="MetadataReader"/>, so the async
    /// composition count reflects whatever <see cref="ApiMember.IsAsync"/> the members already
    /// carry — use <see cref="ProjectType(TypeProjectionRequest)"/> for an accurate async count.
    /// </summary>
    public static TypeProjectionResult ProjectType(ApiType type, ApiSurface? surface = null, TypeProjectionOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(type);
        options ??= new TypeProjectionOptions();

        var baseType = SelectBaseType(type);
        var interfaces = type.Interfaces.Order().ToList();

        if (surface is not null)
            ApiSurfaceExtractor.PopulateDerivedTypes(surface, type);
        var derivedTypes = type.DerivedTypes.ToList();

        var identity = new TypeIdentityView(
            FullName: type.FullName,
            Namespace: string.IsNullOrEmpty(type.Namespace) ? null : type.Namespace,
            Name: type.Name,
            Kind: type.Kind,
            Modifiers: TypeModifiers(type),
            Accessibility: type.Accessibility,
            Assembly: surface?.Name ?? surface?.Library);

        var typeParameters = type.TypeParameters
            .Select(tp => new TypeParameterView(tp.Name, tp.Variance, tp.Constraints.ToList()))
            .ToList();

        var composition = options.Composition ? BuildComposition(type) : null;
        var graph = options.RelationshipGraph ? BuildRelationshipGraph(type, baseType, interfaces, derivedTypes) : null;

        return new TypeProjectionResult(
            identity,
            baseType,
            interfaces,
            derivedTypes,
            typeParameters,
            type.Attributes.ToList(),
            type.EnumUnderlyingType,
            composition,
            graph,
            surface?.InspectionFailures.ToList() ?? []);
    }

    /// <summary>
    /// Resolves the requested type from the source's whole-assembly surface, recovers the
    /// body-gated async fact for its methods, and composes the neutral projection. Throws when
    /// the type cannot be found.
    /// </summary>
    public static TypeProjectionResult ProjectType(TypeProjectionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var surface = request.Source.ExtractApiSurface(includeAll: !request.PublicOnly);
        var type = ResolveType(surface, request.Type)
            ?? throw new InvalidOperationException(
                $"Type '{request.Type}' was not found in '{request.Source.AssemblyName}'.");

        if (request.Composition)
            EnrichAsync(request.Source, type);

        var options = new TypeProjectionOptions(request.PublicOnly, request.Composition, request.RelationshipGraph);
        var result = ProjectType(type, surface, options);
        return result with { Identity = result.Identity with { Assembly = request.Source.AssemblyName } };
    }

    static string? SelectBaseType(ApiType type)
        => !string.IsNullOrEmpty(type.BaseType)
            && type.BaseType != "System.Object"
            && type.BaseType != "System.ValueType"
            && type.BaseType != "System.Enum"
            ? type.BaseType
            : null;

    static TypeCompositionView BuildComposition(ApiType type)
    {
        var members = type.Members;
        return new TypeCompositionView(
            Methods: members.Count(m => m.Kind == "method"),
            Properties: members.Count(m => m.Kind == "property"),
            // Enum members are `field`-kind with a value; exclude them from the field count to
            // match the CLI's existing type stats (they surface as enum values instead).
            Fields: members.Count(m => m.Kind == "field" && !m.EnumValue.HasValue),
            Events: members.Count(m => m.Kind == "event"),
            Constructors: members.Count(m => m.Kind == "constructor"),
            Operators: members.Count(m => m.Kind == "operator"),
            ExplicitInterfaceImplementations: members.Count(m => m.Kind == "explicit-interface-implementation"),
            ExtensionMethods: members.Count(m => m.Kind == "extension-method"),
            Static: members.Count(m => m.IsStatic),
            Unsafe: members.Count(m => m.IsUnsafe),
            Async: members.Count(m => m.IsAsync),
            Virtual: members.Count(m => m.IsVirtual),
            Abstract: members.Count(m => m.IsAbstract),
            Override: members.Count(m => m.IsOverride),
            Extension: members.Count(m => m.IsExtension),
            Obsolete: members.Count(m => m.IsObsolete),
            Total: members.Count);
    }

    static TypeRelationshipGraph BuildRelationshipGraph(
        ApiType type,
        string? baseType,
        IReadOnlyList<string> interfaces,
        IReadOnlyList<string> derivedTypes)
    {
        List<TypeRelationshipNode> nodes = [];
        List<TypeRelationshipEdge> edges = [];
        HashSet<string> seen = new(StringComparer.Ordinal);
        var selfId = type.FullName;

        void AddNode(string id, TypeRelationshipRole role)
        {
            if (seen.Add(id))
                nodes.Add(new TypeRelationshipNode(id, id, role));
        }

        AddNode(selfId, TypeRelationshipRole.Self);

        if (baseType is not null)
        {
            AddNode(baseType, TypeRelationshipRole.Base);
            edges.Add(new TypeRelationshipEdge(selfId, baseType, TypeRelationshipKind.Inherits));
        }

        foreach (var iface in interfaces)
        {
            AddNode(iface, TypeRelationshipRole.Interface);
            edges.Add(new TypeRelationshipEdge(selfId, iface, TypeRelationshipKind.Implements));
        }

        foreach (var derived in derivedTypes)
        {
            AddNode(derived, TypeRelationshipRole.Derived);
            edges.Add(new TypeRelationshipEdge(derived, selfId, TypeRelationshipKind.DerivedFrom));
        }

        return new TypeRelationshipGraph(nodes, edges);
    }

    static void EnrichAsync(MetadataSource source, ApiType type)
    {
        foreach (var member in type.Members)
        {
            if (member.MetadataToken is { } token && source.ClassifyAsync(token) is not null)
                member.IsAsync = true;
        }
    }

    static ApiType? ResolveType(ApiSurface surface, string typeName)
    {
        // Exact full-name match only, mirroring the product's canonical type resolver
        // (IrImporter.ResolveMethodHandle compares against reader.GetFullTypeName). FullName is
        // unique within a surface, so this cannot silently pick the wrong type the way a
        // namespace-less or case-insensitive fallback could when two types collide on a simpler
        // key. A not-found returns null; the caller turns that into a visible throw.
        foreach (var type in surface.Types)
            if (string.Equals(type.FullName, typeName, StringComparison.Ordinal))
                return type;

        return null;
    }
}
