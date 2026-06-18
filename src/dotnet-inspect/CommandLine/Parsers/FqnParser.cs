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

        // Normalize generics: List<T> → List`1, Dictionary<TKey,TValue> → Dictionary`2
        // Also normalizes primitive types: string → System.String, int → System.Int32
        var normalized = TypeMatcher.Normalize(input);

        // Extract overload index suffix (e.g., ":3" from "IndexOf:3")
        int? overloadIndex = null;
        var colonIdx = normalized.LastIndexOf(':');
        if (colonIdx > 0 && int.TryParse(normalized[(colonIdx + 1)..], out var idx))
        {
            overloadIndex = idx;
            normalized = normalized[..colonIdx];
        }

        // Strategy: look for Type.Member pattern
        // The member separator is the last dot where the right side looks like a member name
        // (doesn't contain backticks or dots, starts with uppercase letter)
        
        string? qualifiedPrefix = null;
        string typeName;
        string? memberName = null;

        var lastDot = normalized.LastIndexOf('.');
        if (lastDot <= 0)
        {
            // No dots - just a simple type name
            return new ParseResult(null, normalized, null, overloadIndex);
        }

        // Check if this might be Type.Member
        var rightSegment = normalized[(lastDot + 1)..];
        var leftSegment = normalized[..lastDot];

        // A member name is a simple identifier: no dots, no backticks, starts with uppercase
        bool rightLookLikeMember = !rightSegment.Contains('`') && 
                                   !rightSegment.Contains('.') && 
                                   !string.IsNullOrWhiteSpace(rightSegment) &&
                                   char.IsUpper(rightSegment[0]);

        if (rightLookLikeMember)
        {
            // This is Type.Member or Namespace.Type.Member
            // Try to find if there's another dot to the left for the namespace/type split
            var secondLastDot = leftSegment.LastIndexOf('.');
            if (secondLastDot > 0)
            {
                // We have: [prefix].[type].[member]
                qualifiedPrefix = leftSegment[..secondLastDot];
                typeName = leftSegment[(secondLastDot + 1)..];
                memberName = rightSegment;
            }
            else
            {
                // We have: [type].[member]
                typeName = leftSegment;
                memberName = rightSegment;
            }
        }
        else
        {
            // NOT a Type.Member pattern - this is just a (possibly qualified) type name
            // Don't try to split namespace from type; return the whole thing as TypeName
            // Let the type resolution logic elsewhere handle namespace splitting
            typeName = normalized;
        }

        return new ParseResult(qualifiedPrefix, typeName, memberName, overloadIndex);
    }

    /// <summary>
    /// Parses a member filter value, extracting the overload index if present.
    /// </summary>
    /// <param name="value">Member filter (e.g., "IndexOf:3", "Deserialize")</param>
    /// <returns>Normalized member name and optional overload index.</returns>
    public static (string Name, int? Index) ParseMemberFilter(string value)
    {
        var colonIdx = value.LastIndexOf(':');
        if (colonIdx > 0 && int.TryParse(value[(colonIdx + 1)..], out var idx))
            return (TypeMatcher.NormalizeMemberName(value[..colonIdx]), idx);
        return (TypeMatcher.NormalizeMemberName(value), null);
    }
}
