namespace ILInspector.Analysis;

/// <summary>
/// Metadata-free grammar for the unspeakable type names the C# compiler emits.
/// Shared so allocation escape classification and optimization-opportunity
/// classification read one spelling of the same names rather than two.
/// </summary>
internal static class CompilerGeneratedNames
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
}
