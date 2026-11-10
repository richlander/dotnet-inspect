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
/// <paramref name="memberKeys"/> are the expanded census keys, as
/// <see cref="FindingDiffAnyMemberState.Key"/> spells them, of the Members that
/// own those differences, or of every Member of either side when the Type owns
/// one; null when there is none, which asks for the Type's own entry alone.
/// The settler answers as Exists answers.
/// </summary>
public delegate FindingExistsDecision FindingDiffAnyApiSettler(
    PEReader before,
    TypeDefinitionHandle beforeType,
    PEReader after,
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
    /// <paramref name="type"/>. Without <paramref name="memberKeys"/>, or with
    /// none, only the Type's own entry is produced; a key absent from the
    /// Type's census selects nothing.
    /// </summary>
    public static FindingDiffAnyApiShape Produce(
        PEReader image,
        TypeDefinitionHandle type,
        IReadOnlyCollection<OpaqueByteRange>? memberKeys = null)
    {
        ArgumentNullException.ThrowIfNull(image);
        if (type.IsNil)
            throw new ArgumentException("The Type definition is nil.", nameof(type));
        var side = new FindingDiffAny.Side(image, new SymbolKeyInterner<FindingDiffAnyPart>());
        try
        {
            var selected = new List<(EntityHandle Declaration, OpaqueByteRange Key)>();
            if (memberKeys is { Count: > 0 })
            {
                var wanted = new HashSet<OpaqueByteRange>(memberKeys);
                foreach ((EntityHandle declaration, SymbolKey<FindingDiffAnyPart> key) in DeclaredMembers(side, type))
                {
                    OpaqueByteRange expanded = side.Interner.Expand(key, side.Transient).Detach().Range;
                    if (wanted.Contains(expanded))
                        selected.Add((declaration, expanded));
                }
            }
            var declarations = new HashSet<EntityHandle>(selected.Count);
            foreach ((EntityHandle declaration, _) in selected)
                declarations.Add(declaration);
            ApiSurface surface = Extract(side, type, declarations);
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
        finally
        {
            side.Complete();
        }
    }

    /// <summary>
    /// Each Member the Type declares, with its census key in the side's
    /// interner: accessors raised into their property or event, as
    /// <see cref="ApiSurfaceExtractor.DeclaredMemberOwners"/> owns them.
    /// </summary>
    internal static IEnumerable<(EntityHandle Declaration, SymbolKey<FindingDiffAnyPart> Key)> DeclaredMembers(
        FindingDiffAny.Side side, TypeDefinitionHandle type)
    {
        var seen = new HashSet<EntityHandle>();
        foreach (EntityHandle declaration in ApiSurfaceExtractor.DeclaredMemberOwners(side.Md, type).Values)
        {
            if (seen.Add(declaration))
                yield return (declaration, side.MemberKey(type, declaration));
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
