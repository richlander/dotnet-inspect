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

    /// <summary>A row or name handle could not be read.</summary>
    Malformed,

    /// <summary>
    /// The nested chain repeated a handle or exceeded
    /// <see cref="MetadataSafetyPolicy.MaxRelationshipNodes"/>.
    /// </summary>
    ChainRejected,
}

/// <summary>
/// A full type name to compare against, split once at its dots so an
/// in-place comparison can try each segment boundary without allocating.
/// </summary>
public sealed class MetadataTypeNameTarget
{
    readonly Dictionary<(int Start, int End), string> _segments = [];

    public MetadataTypeNameTarget(string fullName)
    {
        ArgumentNullException.ThrowIfNull(fullName);
        FullName = fullName;
    }

    /// <summary>The dotted full name, as <see cref="TypeResolver.GetTypeName(MetadataReader, EntityHandle, GenericContext?)"/> spells it.</summary>
    public string FullName { get; }

    internal string Segment(int start, int end)
    {
        if (!_segments.TryGetValue((start, end), out string? segment))
        {
            segment = FullName.Substring(start, end - start);
            _segments[(start, end)] = segment;
        }

        return segment;
    }
}

/// <summary>
/// Compares a TypeDef's or TypeRef's full name to a target in place: the
/// namespace and name handles are compared with
/// <see cref="MetadataStringComparer"/>, and no name string is materialized.
/// The comparison spells the name as <see cref="TypeResolver"/> does: the
/// outermost type's namespace, then each type name from outermost to
/// innermost, joined with dots, with no dot before an empty namespace.
/// </summary>
public static class MetadataTypeNameMatch
{
    public static MetadataTypeNameMatchResult Matches(
        MetadataReader reader,
        EntityHandle type,
        MetadataTypeNameTarget target)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(target);
        try
        {
            return type.Kind switch
            {
                HandleKind.TypeReference =>
                    MatchReference(reader, (TypeReferenceHandle)type, target),
                HandleKind.TypeDefinition =>
                    MatchDefinition(reader, (TypeDefinitionHandle)type, target),
                _ => MetadataTypeNameMatchResult.NoMatch,
            };
        }
        catch (Exception ex) when (ex is BadImageFormatException or ArgumentOutOfRangeException)
        {
            return MetadataTypeNameMatchResult.Malformed;
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

        var names = new StringHandle[count];
        for (int i = 0; i < count; i++)
            names[i] = reader.GetTypeReference(chain[i]).Name;
        return MatchParts(
            reader,
            reader.GetTypeReference(chain[0]).Namespace,
            names,
            target);
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

        var names = new StringHandle[count];
        for (int i = 0; i < count; i++)
            names[i] = reader.GetTypeDefinition(chain[i]).Name;
        return MatchParts(
            reader,
            reader.GetTypeDefinition(chain[0]).Namespace,
            names,
            target);
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
