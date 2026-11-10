using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using InertText;
using Inspector.Findings;

namespace ILInspector.Metadata;

/// <summary>
/// Settles the API state of one paired Type whose census left uncertain
/// differences (docs/design/annotated-type-map-api-family.md#settlement-through-the-family).
/// Each side's image is handed as the comparison's API shape producer for it,
/// valid for this call only. <paramref name="memberKeys"/> are the expanded
/// census keys, as <see cref="FindingDiffAnyMemberState.Key"/> spells them, of
/// the Members that own those differences, or of every Member of either side
/// when the Type owns one; null when there is none, which asks for the Type's
/// own entry alone. The settler answers as Exists answers.
/// </summary>
public delegate FindingExistsDecision FindingDiffAnyApiSettler(
    FindingDiffAnyApiShapeProducer before,
    TypeDefinitionHandle beforeType,
    FindingDiffAnyApiShapeProducer after,
    TypeDefinitionHandle afterType,
    IReadOnlyList<OpaqueByteRange>? memberKeys);

/// <summary>One selected Member's API shape on one side.</summary>
/// <param name="Key">The Member's expanded census key.</param>
/// <param name="Declaration">Its method, field, property, or event definition.</param>
/// <param name="Projection">Its projection in the API surface, or null when the surface does not admit it.</param>
public sealed record FindingDiffAnyApiShapeMember(
    OpaqueByteRange Key,
    EntityHandle Declaration,
    ApiMember? Projection);

/// <summary>
/// One side's API shape facts for one Type
/// (docs/design/annotated-type-map-api-family.md#producer): the Type's
/// admission under the <see cref="ApiSurfaceExtractionScope.ApiAxis"/> scope,
/// its detached projection holding the Type row and the selected Members, and
/// each selected key present in the Type's census.
/// </summary>
/// <param name="Surface">The projection the complete API comparison reads; it holds no Type when the Type is not admitted.</param>
/// <param name="Members">The selected keys present in the Type's census, in the order the producer reads its declarations.</param>
public sealed record FindingDiffAnyApiShape(
    ApiSurface Surface,
    ImmutableArray<FindingDiffAnyApiShapeMember> Members)
{
    /// <summary>Whether the API surface admits the Type.</summary>
    public bool Admitted => Surface.Types.Count != 0;
}

/// <summary>
/// The API shape producer: the only code that maps an API Member to its
/// census key. Finding Diff Any's inline settlement and the API shape family
/// both read through it.
/// </summary>
public static class FindingDiffAnyApiShapes
{
    /// <summary>
    /// Produces <paramref name="image"/>'s API shape facts for
    /// <paramref name="type"/> once. Without <paramref name="memberKeys"/>, or
    /// with none, only the Type's own entry is produced; a key absent from the
    /// Type's census selects nothing. A caller producing for several Types of
    /// one image uses one <see cref="FindingDiffAnyApiShapeProducer"/>.
    /// </summary>
    public static FindingDiffAnyApiShape Produce(
        PEReader image,
        TypeDefinitionHandle type,
        IReadOnlyCollection<OpaqueByteRange>? memberKeys = null)
    {
        using var producer = new FindingDiffAnyApiShapeProducer(image);
        return producer.Produce(type, memberKeys);
    }

    /// <summary>
    /// Each Member the Type declares, with its census key in the side's
    /// interner: accessors raised into their property or event, as
    /// <see cref="ApiSurfaceExtractor.DeclaredMemberOwners"/> owns them.
    /// </summary>
    internal static IEnumerable<(EntityHandle Declaration, SymbolKey<FindingDiffAnyPart> Key)> DeclaredMembers(
        FindingDiffAny.Side side, TypeDefinitionHandle type)
    {
        foreach (EntityHandle declaration in DeclaredMemberDeclarations(side, type))
            yield return (declaration, side.MemberKey(type, declaration));
    }

    /// <summary>
    /// Each Member the Type declares, once: accessors raised into their
    /// property or event, as <see cref="ApiSurfaceExtractor.DeclaredMemberOwners"/>
    /// owns them.
    /// </summary>
    internal static IEnumerable<EntityHandle> DeclaredMemberDeclarations(
        FindingDiffAny.Side side, TypeDefinitionHandle type)
    {
        var seen = new HashSet<EntityHandle>();
        foreach (EntityHandle declaration in ApiSurfaceExtractor.DeclaredMemberOwners(side.Md, type).Values)
        {
            if (seen.Add(declaration))
                yield return declaration;
        }
    }

    /// <summary>The declarations whose census keys <paramref name="selected"/> holds.</summary>
    internal static HashSet<EntityHandle> Select(
        FindingDiffAny.Side side, TypeDefinitionHandle type, IReadOnlySet<SymbolKey<FindingDiffAnyPart>> selected)
    {
        var declarations = new HashSet<EntityHandle>();
        if (selected.Count == 0)
            return declarations;
        foreach ((EntityHandle declaration, SymbolKey<FindingDiffAnyPart> key) in DeclaredMembers(side, type))
        {
            if (selected.Contains(key))
                declarations.Add(declaration);
        }
        return declarations;
    }

    /// <summary>The API-equality projection of the Type row and <paramref name="declarations"/>.</summary>
    internal static ApiSurface Extract(
        FindingDiffAny.Side side, TypeDefinitionHandle type, IReadOnlySet<EntityHandle> declarations)
        => ApiSurfaceExtractor.ExtractApiEquality(
            side.Pe, ApiSurfaceExtractionScope.ApiAxis, candidate => candidate == type, declarations);
}

/// <summary>
/// The API shape producer bound to one image, for producing several Types'
/// facts: the census keys it spells are memoized across calls. One a caller
/// constructs owns its key tables until disposed. One Finding Diff Any hands a
/// settler reads the comparison's own key tables, warm with the census, and is
/// valid only during that call. A producer is not safe for concurrent use.
/// </summary>
public sealed class FindingDiffAnyApiShapeProducer : IDisposable
{
    private readonly FindingDiffAny.Side _side;
    private readonly bool _owned;
    private bool _disposed;

    public FindingDiffAnyApiShapeProducer(PEReader image)
        : this(new FindingDiffAny.Side(
            image ?? throw new ArgumentNullException(nameof(image)),
            new SymbolKeyInterner<FindingDiffAnyPart>()), owned: true)
    {
    }

    private FindingDiffAnyApiShapeProducer(FindingDiffAny.Side side, bool owned)
    {
        _side = side;
        _owned = owned;
    }

    /// <summary>A producer over one comparison side's key tables, which the comparison keeps.</summary>
    internal static FindingDiffAnyApiShapeProducer Borrow(FindingDiffAny.Side side) => new(side, owned: false);

    /// <summary>The image this producer reads.</summary>
    public PEReader Image => _side.Pe;

    /// <summary>The image's metadata, read once for this producer.</summary>
    public MetadataReader Metadata => _side.Md;

    /// <summary>
    /// Produces the image's API shape facts for <paramref name="type"/>, as
    /// <see cref="FindingDiffAnyApiShapes.Produce"/> does.
    /// </summary>
    public FindingDiffAnyApiShape Produce(
        TypeDefinitionHandle type,
        IReadOnlyCollection<OpaqueByteRange>? memberKeys = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (type.IsNil)
            throw new ArgumentException("The Type definition is nil.", nameof(type));
        if (!_owned)
        {
            // The comparison's side is inside its unit; the keys spelled here
            // join that unit and are discarded with it.
            return Produce(_side, type, memberKeys);
        }

        try
        {
            return Produce(_side, type, memberKeys);
        }
        catch
        {
            _side.Abandon();
            throw;
        }
        finally
        {
            // Each production is one unit: its transient keys are discarded,
            // and the memoized keys it retained serve the next.
            _side.EndUnit();
        }
    }

    private static FindingDiffAnyApiShape Produce(
        FindingDiffAny.Side side,
        TypeDefinitionHandle type,
        IReadOnlyCollection<OpaqueByteRange>? memberKeys)
    {
        var selected = new List<(EntityHandle Declaration, OpaqueByteRange Key)>();
        if (memberKeys is { Count: > 0 })
        {
            // Keys this side's table expanded, as the comparison's do, are
            // compared in its interned form, as the inline settlement compares
            // them; any other key set is compared expanded.
            HashSet<SymbolKey<FindingDiffAnyPart>>? interned = new(memberKeys.Count);
            foreach (OpaqueByteRange key in memberKeys)
            {
                if (!side.Interner.TryGetExpandedSource(key, out SymbolKey<FindingDiffAnyPart> source))
                {
                    interned = null;
                    break;
                }
                interned.Add(source);
            }
            HashSet<OpaqueByteRange>? wanted = interned is null ? new(memberKeys) : null;
            // A Member key carries its Member's name, so a declaration whose
            // name no requested key carries cannot match; only the rest are
            // spelled for the exact key comparison.
            HashSet<int>? names = RequestedNames(memberKeys);
            foreach (EntityHandle declaration in FindingDiffAnyApiShapes.DeclaredMemberDeclarations(side, type))
            {
                if (names is not null && !names.Contains(NameHash(side.Names.Bytes(NameOf(side.Md, declaration)))))
                    continue;
                SymbolKey<FindingDiffAnyPart> key = side.MemberKey(type, declaration);
                if (interned is not null)
                {
                    if (interned.Contains(key))
                        selected.Add((declaration, side.Interner.ExpandDetached(key, side.Transient)));
                    continue;
                }
                OpaqueByteRange expanded = side.Interner.ExpandDetached(key, side.Transient);
                if (wanted!.Contains(expanded))
                    selected.Add((declaration, expanded));
            }
        }
        var declarations = new HashSet<EntityHandle>(selected.Count);
        foreach ((EntityHandle declaration, _) in selected)
            declarations.Add(declaration);
        ApiSurface surface = FindingDiffAnyApiShapes.Extract(side, type, declarations);
        Dictionary<int, ApiMember> projections = [];
        foreach (ApiType apiType in surface.Types)
        {
            // A property, field, or event names its definition in the
            // declaration token; a method's own token is its definition.
            foreach (ApiMember member in apiType.Members)
            {
                if ((member.DeclarationMetadataToken ?? member.MetadataToken) is int token)
                    projections[token] = member;
            }
        }
        var members = ImmutableArray.CreateBuilder<FindingDiffAnyApiShapeMember>(selected.Count);
        foreach ((EntityHandle declaration, OpaqueByteRange key) in selected)
        {
            members.Add(new FindingDiffAnyApiShapeMember(
                key, declaration, projections.GetValueOrDefault(MetadataTokens.GetToken(declaration))));
        }
        return new FindingDiffAnyApiShape(surface, members.MoveToImmutable());
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        if (_owned)
            _side.Complete();
    }

    // The name hashes of the requested keys, read from each key's name part
    // after its kind and declaring Type; null when a key has no such part, so
    // every declaration is compared.
    private static HashSet<int>? RequestedNames(IReadOnlyCollection<OpaqueByteRange> memberKeys)
    {
        var names = new HashSet<int>();
        foreach (OpaqueByteRange key in memberKeys)
        {
            SymbolKeyPartReader<FindingDiffAnyPart> parts = new SymbolKey<FindingDiffAnyPart>(key).Parts;
            if (!parts.Read()
                || parts.Kind != SymbolKeyPartKind.Marker
                || parts.Marker is not (FindingDiffAnyPart.Method or FindingDiffAnyPart.Field
                    or FindingDiffAnyPart.Property or FindingDiffAnyPart.Event)
                || !parts.Read()
                || !parts.Read()
                || parts.Kind != SymbolKeyPartKind.Name)
            {
                return null;
            }
            names.Add(NameHash(parts.CopyPayload()));
        }
        return names;
    }

    private static StringHandle NameOf(MetadataReader md, EntityHandle declaration) => declaration.Kind switch
    {
        HandleKind.MethodDefinition => md.GetMethodDefinition((MethodDefinitionHandle)declaration).Name,
        HandleKind.FieldDefinition => md.GetFieldDefinition((FieldDefinitionHandle)declaration).Name,
        HandleKind.PropertyDefinition => md.GetPropertyDefinition((PropertyDefinitionHandle)declaration).Name,
        HandleKind.EventDefinition => md.GetEventDefinition((EventDefinitionHandle)declaration).Name,
        _ => throw new ArgumentOutOfRangeException(nameof(declaration)),
    };

    // FNV-1a over a name's stored bytes; a collision only spells one more key.
    private static int NameHash(ReadOnlySpan<byte> name)
    {
        uint hash = 2166136261;
        foreach (byte value in name)
            hash = (hash ^ value) * 16777619;
        return (int)hash;
    }
}
