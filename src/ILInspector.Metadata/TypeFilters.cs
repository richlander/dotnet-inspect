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
}
