using ILInspector.Metadata;

namespace DotnetInspector.CommandLine;

/// <summary>
/// Parses fully-qualified names with optional namespace, type, member, and overload notation.
/// Handles patterns like: Type, Type.Member, Type&lt;T&gt;.Member:N, Namespace.Type.Member, etc.
/// </summary>
public static class FqnParser
{
    /// <summary>
    /// Result of parsing an FQN string.
    /// </summary>
    public record ParseResult(
        string? QualifiedPrefix,    // Namespace or package prefix (e.g., "System.Collections.Generic")
        string TypeName,             // Type name (e.g., "List`1", "JsonSerializer")
        string? MemberName,          // Member name if present (e.g., "IndexOf", "Deserialize")
        int? OverloadIndex);         // Overload index if present (e.g., 3 from "IndexOf:3")

    /// <summary>
    /// Parses a fully-qualified name into its components.
    /// </summary>
    /// <param name="input">The FQN string to parse (e.g., "List&lt;T&gt;.IndexOf:3", "System.Text.Json.JsonSerializer.Deserialize")</param>
    /// <returns>Parsed components, or null if the input doesn't match any known pattern.</returns>
    /// <remarks>
    /// This parser focuses on structural parsing only. It identifies Type.Member patterns
    /// and extracts overload indices, but does NOT attempt to split namespaces from type names
    /// for standalone type references (that's handled by type resolution logic elsewhere).
    /// </remarks>
    public static ParseResult? Parse(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return null;

        // Extract overload index suffix (e.g., ":3" from "IndexOf:3") from the raw input,
        // before any normalization runs.
        var (work, overloadIndex) = TrySplitOverload(input.Trim());

        // Determine the member-split point using the user's own dots. Normalization can
        // introduce dots (e.g. primitive aliases: "string" → "System.String"), so it must
        // not influence whether the input has a Type.Member shape. Only top-level dots
        // (outside generic angle brackets) are candidate separators.
        var lastDot = LastTopLevelDot(work);
        if (lastDot <= 0)
        {
            // No member separator - the whole thing is a (possibly primitive/generic) type name.
            return new ParseResult(null, TypeMatcher.Normalize(work), null, overloadIndex);
        }

        var rightSegment = work[(lastDot + 1)..];
        var leftSegment = work[..lastDot];

        // A member name is a simple identifier: no dots, no backticks/generics, starts with uppercase.
        bool rightLooksLikeMember = !rightSegment.Contains('`') &&
                                    !rightSegment.Contains('.') &&
                                    !rightSegment.Contains('<') &&
                                    !string.IsNullOrWhiteSpace(rightSegment) &&
                                    char.IsUpper(rightSegment[0]);

        if (!rightLooksLikeMember)
        {
            // NOT a Type.Member pattern - this is just a (possibly qualified) type name.
            // Don't try to split namespace from type; return the whole thing as TypeName
            // and let the type resolution logic elsewhere handle namespace splitting.
            return new ParseResult(null, TypeMatcher.Normalize(work), null, overloadIndex);
        }

        // This is Type.Member or Namespace.Type.Member. Look for another top-level dot to the
        // left to separate an optional namespace/package prefix from the type name.
        string? qualifiedPrefix = null;
        string typePart = leftSegment;
        var secondLastDot = LastTopLevelDot(leftSegment);
        if (secondLastDot > 0)
        {
            qualifiedPrefix = leftSegment[..secondLastDot];
            typePart = leftSegment[(secondLastDot + 1)..];
        }

        return new ParseResult(qualifiedPrefix, TypeMatcher.Normalize(typePart), rightSegment, overloadIndex);
    }

    // Returns the index of the last '.' that sits outside any generic angle-bracket group,
    // or -1 if there is none. Prevents splitting inside type arguments like
    // "Dictionary&lt;System.Int32,string&gt;".
    internal static int LastTopLevelDot(string value)
    {
        var depth = 0;
        for (var i = value.Length - 1; i >= 0; i--)
        {
            var c = value[i];
            if (c == '>')
                depth++;
            else if (c == '<')
                depth--;
            else if (c == '.' && depth == 0)
                return i;
        }
        return -1;
    }

    /// <summary>
    /// Splits a trailing <c>:N</c> overload selector from a value, without normalizing the head.
    /// This is the single structural primitive for overload notation used across the parsers.
    /// </summary>
    /// <param name="value">A member or FQN string, possibly ending in <c>:N</c>.</param>
    /// <returns>The value without the suffix, and the 1-based overload index if present.</returns>
    internal static (string Head, int? Index) TrySplitOverload(string value)
    {
        var colonIdx = value.LastIndexOf(':');
        if (colonIdx > 0 && colonIdx < value.Length - 1
            && int.TryParse(value[(colonIdx + 1)..], out var idx) && idx > 0)
            return (value[..colonIdx], idx);
        return (value, null);
    }

    /// <summary>
    /// Parses a member filter value, extracting the overload index if present.
    /// </summary>
    /// <param name="value">Member filter (e.g., "IndexOf:3", "Deserialize")</param>
    /// <returns>Normalized member name and optional overload index.</returns>
    public static (string Name, int? Index) ParseMemberFilter(string value)
    {
        var (head, index) = TrySplitOverload(value);
        return (TypeMatcher.NormalizeMemberName(head), index);
    }
}
