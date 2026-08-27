using System.Globalization;
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
    internal const string DisplayClassPrefix = GeneratedNameGrammar.DisplayClassPrefix;

    /// <summary>Iterator/async state-machine types: <c>&lt;...&gt;d__...</c>.</summary>
    internal const string StateMachineInfix = GeneratedNameGrammar.StateMachineInfix;

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
        return GeneratedNameGrammar.LeafSegment(name);
    }

    /// <summary>
    /// True when the type is a compiler-generated closure environment. The
    /// non-capturing lambda cache type is named exactly <c>&lt;&gt;c</c>, and
    /// method-group targets live on ordinary types, so neither matches.
    /// </summary>
    internal static bool IsDisplayClass(TypeRef? type)
        => type is not null
            && GeneratedNameGrammar.IsDisplayClassLeaf(LeafName(type));

    /// <summary>Source-authored local-function and lambda method bodies.</summary>
    internal static bool IsLocalFunctionOrLambda(string methodName)
        => TryGetLiftedOwnerName(methodName, out _);

    internal static bool HasLiftedMethodMarker(string methodName)
        => LastLiftedMethodMarker(methodName) >= 0;

    internal static bool TryGetLiftedOwnerName(
        string methodName,
        out string ownerName)
    {
        string simpleName =
            MetadataNameArity.StripFromSegment(methodName);
        int close = LastLiftedMethodMarker(simpleName);
        if (simpleName.Length < 4
            || simpleName[0] != '<'
            || close <= 1
            || close + 4 >= simpleName.Length
            || !HasCanonicalLiftedSuffix(
                simpleName,
                close))
        {
            ownerName = "";
            return false;
        }

        ownerName = simpleName[1..close];
        return true;
    }

    static bool HasCanonicalLiftedSuffix(
        string methodName,
        int marker)
    {
        bool localFunction = methodName.AsSpan(marker)
            .StartsWith(
                GeneratedNameGrammar.LocalFunctionInfix,
                StringComparison.Ordinal);
        ReadOnlySpan<char> suffix =
            methodName.AsSpan(marker + 4);
        if (localFunction)
        {
            int separator = suffix.LastIndexOf('|');
            if (separator <= 0)
                return false;
            suffix = suffix[(separator + 1)..];
        }

        int underscore = suffix.IndexOf('_');
        if (underscore < 0)
        {
            return !localFunction
                && IsCanonicalOrdinal(suffix);
        }
        if (underscore == 0
            || underscore == suffix.Length - 1
            || suffix[(underscore + 1)..]
                .Contains('_'))
        {
            return false;
        }

        return IsCanonicalOrdinal(
                suffix[..underscore])
            && IsCanonicalOrdinal(
                suffix[(underscore + 1)..]);
    }

    static bool IsCanonicalOrdinal(
        ReadOnlySpan<char> value)
    {
        if (value.IsEmpty
            || value.Length > 1 && value[0] == '0')
        {
            return false;
        }

        foreach (char c in value)
        {
            if (!char.IsAsciiDigit(c))
                return false;
        }

        return int.TryParse(
            value,
            NumberStyles.None,
            CultureInfo.InvariantCulture,
            out _);
    }

    static int LastLiftedMethodMarker(string methodName) =>
        Math.Max(
            methodName.LastIndexOf(
                GeneratedNameGrammar.LocalFunctionInfix,
                StringComparison.Ordinal),
            methodName.LastIndexOf(
                GeneratedNameGrammar.LambdaInfix,
                StringComparison.Ordinal));

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
        => GeneratedNameGrammar.IsGeneratedName(name);

    internal static bool RequiresDeclaredOwner(
        MethodIdentity method)
        => IsLocalFunctionOrLambda(method.Name)
            || method.Name == "MoveNext"
                && IsStateMachineLeaf(
                    LeafName(method.DeclaringType));

    static bool IsStateMachineLeaf(string leafName)
    {
        string simpleName =
            MetadataNameArity.StripFromSegment(leafName);
        return GeneratedNameGrammar.IsStateMachineLeaf(simpleName)
            || simpleName.StartsWith('<')
                && simpleName.EndsWith(">d", StringComparison.Ordinal)
                && (GeneratedNameGrammar
                        .IsLocalFunctionMethodName(simpleName)
                    || GeneratedNameGrammar
                        .IsLambdaMethodName(simpleName));
    }
}
