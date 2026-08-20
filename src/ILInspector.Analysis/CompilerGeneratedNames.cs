using ILInspector.Metadata;

namespace ILInspector.Analysis;

/// <summary>
/// Metadata-free grammar for the unspeakable type names the C# compiler emits.
/// Shared so allocation escape classification and optimization-opportunity
/// classification read one spelling of the same names rather than two.
/// </summary>
public static class CompilerGeneratedNames
{
    /// <summary>Closure environment types: <c>&lt;&gt;c__DisplayClass...</c>.</summary>
    internal const string DisplayClassPrefix = "<>c__DisplayClass";

    /// <summary>Iterator/async state-machine types: <c>&lt;...&gt;d__...</c>.</summary>
    internal const string StateMachineInfix = ">d__";

    /// <summary>
    /// The innermost segment of a type's metadata name, with any
    /// <c>Outer+Inner</c> nesting and generic instantiation peeled away. Generic
    /// arity is retained.
    /// </summary>
    internal static string LeafName(TypeRef type)
    {
        string name = type.Kind == TypeRefKind.GenericInstance
            ? type.ElementType?.Name ?? ""
            : type.Name;
        int nested = name.LastIndexOf('+');
        return nested < 0 ? name : name[(nested + 1)..];
    }

    /// <summary>
    /// True when the type is a compiler-generated closure environment. The
    /// non-capturing lambda cache type is named exactly <c>&lt;&gt;c</c>, and
    /// method-group targets live on ordinary types, so neither matches.
    /// </summary>
    internal static bool IsDisplayClass(TypeRef? type)
        => type is not null
            && LeafName(type)
                .StartsWith(DisplayClassPrefix, StringComparison.Ordinal);

    /// <summary>Source-authored local-function and lambda method bodies.</summary>
    internal static bool IsLocalFunctionOrLambda(string methodName)
        => methodName.Contains(">g__", StringComparison.Ordinal)
            || methodName.Contains(">b__", StringComparison.Ordinal);

    /// <summary>
    /// Returns the qualified display name of the immediate containing type for
    /// a nested compiler-generated implementation type, or
    /// <see langword="null"/> when the relationship is absent or ambiguous.
    /// </summary>
    /// <remarks>
    /// <c>CompilerGeneratedNamesTests.ContainingTypeDisplayName_UsesExactSegmentsAndConservativeFlatFallback</c>
    /// gates the exact and legacy-flat paths.
    /// </remarks>
    public static string? ContainingTypeDisplayName(TypeRef type)
    {
        ArgumentNullException.ThrowIfNull(type);

        TypeRef definition = type.Kind == TypeRefKind.GenericInstance
            && type.ElementType is { } element
                ? element
                : type;
        if (definition.Kind != TypeRefKind.Definition)
            return null;

        string? containingName;
        string @namespace;
        if (definition.Resolution?.Type is { } exactName)
        {
            if (exactName.Segments.Length < 2
                || !IsGeneratedTypeName(exactName.Segments[^1]))
            {
                return null;
            }

            containingName = TypeRef.RenderExactSegments(
                exactName.Segments[..^1],
                stripArity: true);
            @namespace = exactName.Namespace;
        }
        else
        {
            int boundary = definition.Name.LastIndexOf('+');
            if (boundary <= 0
                || definition.Name.IndexOf('+') != boundary
                || boundary == definition.Name.Length - 1
                || !IsGeneratedTypeName(definition.Name[(boundary + 1)..]))
            {
                return null;
            }

            containingName =
                MetadataNameArity.StripFromSegment(definition.Name[..boundary]);
            @namespace = definition.Namespace;
        }

        return @namespace.Length == 0
            ? containingName
            : $"{@namespace}.{containingName}";
    }

    static bool IsGeneratedTypeName(string name)
        => name.StartsWith('<')
            || name.StartsWith("__", StringComparison.Ordinal);

    internal static bool RequiresDeclaredOwner(
        MethodIdentity method)
        => IsLocalFunctionOrLambda(method.Name)
            || method.Name == "MoveNext"
                && LeafName(method.DeclaringType)
                    .Contains(
                        StateMachineInfix,
                        StringComparison.Ordinal);
}
