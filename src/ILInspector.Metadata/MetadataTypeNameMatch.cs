using System.Reflection.Metadata;

using ILInspector.MetadataPrimitives;

namespace ILInspector.Metadata;

/// <summary>How an in-place type-name comparison ended.</summary>
public enum MetadataTypeNameMatchResult
{
    /// <summary>The type's full name equals the target.</summary>
    Match,

    /// <summary>The type's full name differs from the target.</summary>
    NoMatch,

    /// <summary>A row, name handle, or TypeSpec blob could not be read.</summary>
    Malformed,

    /// <summary>
    /// A nested chain repeated a handle or exceeded
    /// <see cref="MetadataSafetyPolicy.MaxRelationshipNodes"/>.
    /// </summary>
    ChainRejected,
}

/// <summary>
/// A full type name to compare against. Every segment an in-place comparison
/// can try is cut once, at construction, so a target is immutable and can be
/// shared by concurrent comparisons.
/// </summary>
public sealed class MetadataTypeNameTarget
{
    readonly Dictionary<(int Start, int End), string> _segments = [];

    public MetadataTypeNameTarget(string fullName)
    {
        ArgumentNullException.ThrowIfNull(fullName);
        FullName = fullName;

        // A part starts at 0 or after a dot, and ends where it starts (an
        // empty part), at a dot, or at the end of the name.
        var starts = new List<int> { 0 };
        var ends = new List<int> { fullName.Length };
        for (int i = 0; i < fullName.Length; i++)
        {
            if (fullName[i] != '.')
                continue;
            starts.Add(i + 1);
            ends.Add(i);
        }

        foreach (int start in starts)
        {
            _segments[(start, start)] = string.Empty;
            foreach (int end in ends)
            {
                if (end >= start)
                    _segments[(start, end)] = fullName[start..end];
            }
        }
    }

    /// <summary>The dotted full name, as <see cref="TypeResolver.GetTypeName(MetadataReader, EntityHandle, GenericContext?)"/> spells it.</summary>
    public string FullName { get; }

    internal string Segment(int start, int end) => _segments[(start, end)];
}

/// <summary>
/// Compares a type's full name to a target in place: namespace and name
/// handles are compared with <see cref="MetadataStringComparer"/>, and no
/// metadata name is materialized. The comparison spells the name as
/// <see cref="TypeResolver"/> does: the outermost type's namespace, then each
/// type name from outermost to innermost, joined with dots, with no dot
/// before an empty namespace.
/// </summary>
/// <remarks>
/// A TypeSpec is read as <see cref="TypeResolver"/> decodes it: custom
/// modifiers and <c>pinned</c> spell nothing, and <c>class</c> or
/// <c>valuetype</c> spells its TypeDef or TypeRef; naming a TypeSpec there is
/// refused by the decoder, so it spells no name. Every other element type
/// spells a keyword, a suffix, a bracket, or a generic argument list, so it
/// never equals a plain dotted name and is answered without decoding. A
/// generic instantiation is answered the same way.
/// </remarks>
public static class MetadataTypeNameMatch
{
    const byte ValueType = 0x11;
    const byte Class = 0x12;
    const byte RequiredModifier = 0x1F;
    const byte OptionalModifier = 0x20;
    const byte Pinned = 0x45;

    public static MetadataTypeNameMatchResult Matches(
        MetadataReader reader,
        EntityHandle type,
        MetadataTypeNameTarget target)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(target);
        try
        {
            if (type.Kind == HandleKind.TypeSpecification
                && !TrySpelledType((TypeSpecificationHandle)type, reader, out type))
            {
                return MetadataTypeNameMatchResult.NoMatch;
            }

            return type.Kind switch
            {
                HandleKind.TypeReference => MatchReference(reader, (TypeReferenceHandle)type, target),
                HandleKind.TypeDefinition => MatchDefinition(reader, (TypeDefinitionHandle)type, target),
                _ => MetadataTypeNameMatchResult.NoMatch,
            };
        }
        catch (Exception ex) when (ex is BadImageFormatException or ArgumentOutOfRangeException)
        {
            return MetadataTypeNameMatchResult.Malformed;
        }
    }

    /// <summary>
    /// The TypeDef or TypeRef a TypeSpec spells by name, or false when it
    /// spells something no plain dotted name can equal.
    /// </summary>
    static bool TrySpelledType(TypeSpecificationHandle handle, MetadataReader reader, out EntityHandle type)
    {
        type = default;
        BlobReader blob = reader.GetBlobReader(reader.GetTypeSpecification(handle).Signature);
        while (true)
        {
            byte code = blob.ReadByte();
            switch (code)
            {
                case RequiredModifier or OptionalModifier:
                    blob.ReadTypeHandle();
                    continue;
                case Pinned:
                    continue;
                case Class or ValueType:
                    type = blob.ReadTypeHandle();
                    return type.Kind is HandleKind.TypeReference or HandleKind.TypeDefinition && !type.IsNil;
                default:
                    return false;
            }
        }
    }

    static MetadataTypeNameMatchResult MatchReference(
        MetadataReader reader,
        TypeReferenceHandle handle,
        MetadataTypeNameTarget target)
    {
        TypeReference typeRef = reader.GetTypeReference(handle);
        if (typeRef.ResolutionScope.Kind != HandleKind.TypeReference)
            return MatchParts(reader, typeRef.Namespace, [typeRef.Name], target);

        Span<TypeReferenceHandle> chain =
            stackalloc TypeReferenceHandle[MetadataSafetyPolicy.MaxRelationshipNodes];
        if (!MetadataRelationshipTraversal.TryWalkTypeReferenceResolutionScope(
                reader,
                handle,
                chain,
                out int count,
                out _,
                out RelationshipTraversalRejection? rejection))
        {
            return Rejected(rejection);
        }

        Span<StringHandle> names = stackalloc StringHandle[count];
        for (int i = 0; i < count; i++)
            names[i] = reader.GetTypeReference(chain[i]).Name;
        return MatchParts(reader, reader.GetTypeReference(chain[0]).Namespace, names, target);
    }

    static MetadataTypeNameMatchResult MatchDefinition(
        MetadataReader reader,
        TypeDefinitionHandle handle,
        MetadataTypeNameTarget target)
    {
        TypeDefinition typeDef = reader.GetTypeDefinition(handle);
        if (typeDef.GetDeclaringType().IsNil)
            return MatchParts(reader, typeDef.Namespace, [typeDef.Name], target);

        Span<TypeDefinitionHandle> chain =
            stackalloc TypeDefinitionHandle[MetadataSafetyPolicy.MaxRelationshipNodes];
        if (!MetadataRelationshipTraversal.TryWalkTypeDefinitionDeclaringChain(
                reader,
                handle,
                chain,
                out int count,
                out _,
                out RelationshipTraversalRejection? rejection))
        {
            return Rejected(rejection);
        }

        Span<StringHandle> names = stackalloc StringHandle[count];
        for (int i = 0; i < count; i++)
            names[i] = reader.GetTypeDefinition(chain[i]).Name;
        return MatchParts(reader, reader.GetTypeDefinition(chain[0]).Namespace, names, target);
    }

    static MetadataTypeNameMatchResult Rejected(RelationshipTraversalRejection? rejection) =>
        rejection?.Kind == RelationshipTraversalRejectionKind.MalformedMetadata
            ? MetadataTypeNameMatchResult.Malformed
            : MetadataTypeNameMatchResult.ChainRejected;

    static MetadataTypeNameMatchResult MatchParts(
        MetadataReader reader,
        StringHandle @namespace,
        ReadOnlySpan<StringHandle> names,
        MetadataTypeNameTarget target)
    {
        string full = target.FullName;
        MetadataStringComparer comparer = reader.StringComparer;
        int position = 0;
        if (!TryConsume(comparer, @namespace, target, ref position))
            return MetadataTypeNameMatchResult.NoMatch;

        for (int i = 0; i < names.Length; i++)
        {
            // TypeResolver renders a dot before a name when a namespace or an
            // outer name has already been written.
            if (i > 0 || position > 0)
            {
                if (position >= full.Length || full[position] != '.')
                    return MetadataTypeNameMatchResult.NoMatch;
                position++;
            }

            if (!TryConsume(comparer, names[i], target, ref position))
                return MetadataTypeNameMatchResult.NoMatch;
        }

        return position == full.Length
            ? MetadataTypeNameMatchResult.Match
            : MetadataTypeNameMatchResult.NoMatch;
    }

    /// <summary>
    /// Consumes the text of <paramref name="handle"/> at <paramref name="position"/>.
    /// A non-empty part ends at a dot or at the end of the target, and a string of one
    /// length can equal at most one candidate, so the first match is the only one.
    /// </summary>
    static bool TryConsume(
        MetadataStringComparer comparer,
        StringHandle handle,
        MetadataTypeNameTarget target,
        ref int position)
    {
        string full = target.FullName;
        for (int end = position; end <= full.Length; end++)
        {
            // An empty part may end anywhere; a non-empty part ends at a dot
            // or at the end of the target.
            if (end > position && end < full.Length && full[end] != '.')
                continue;
            if (comparer.Equals(handle, target.Segment(position, end)))
            {
                position = end;
                return true;
            }
        }

        return false;
    }
}
