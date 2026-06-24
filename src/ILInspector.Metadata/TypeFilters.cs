namespace ILInspector.Metadata;

public static class TypeFilters
{
    /// <summary>
    /// True when a metadata type name is compiler-generated, identified by the
    /// reserved name prefixes the runtime/Roslyn use for synthesized types:
    /// <c>&lt;</c> (closures, state machines, display classes) and <c>__</c>
    /// (e.g. <c>__StaticArrayInitTypeSize=...</c>).
    /// This is the type-name counterpart to
    /// <see cref="MemberFilters.IsCompilerGenerated"/>, which uses a broader
    /// rule appropriate for member names.
    /// </summary>
    public static bool IsCompilerGenerated(string typeName)
        => typeName.StartsWith('<') || typeName.StartsWith("__", System.StringComparison.Ordinal);

    /// <summary>
    /// True when any segment of a possibly nested-qualified, <c>+</c>-separated
    /// type name is compiler-generated. Metadata readers qualify nested types as
    /// <c>Outer+Inner</c>, so a synthesized display class or state machine appears
    /// as <c>Outer+&lt;Method&gt;d__0</c>; the plain <see cref="IsCompilerGenerated(string)"/>
    /// would only inspect the leading <c>Outer</c> segment and miss it.
    /// </summary>
    public static bool IsCompilerGeneratedNested(string nestedTypeName)
    {
        foreach (var segment in nestedTypeName.Split('+'))
        {
            if (IsCompilerGenerated(segment))
                return true;
        }
        return false;
    }
}
