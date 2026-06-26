namespace ILInspector.Analysis;

internal static class FrameworkIdentity
{
    public static bool IsCoreLibraryType(TypeRef type, string ns, string name)
    {
        var definition = NamedDefinition(type);
        return definition.Kind != TypeRefKind.Unsupported
            && definition.Assembly == TypeRef.CoreLibrary
            && definition.Namespace == ns
            && definition.Name == name;
    }

    public static bool IsKnownFrameworkType(TypeRef type, string assembly, string ns, string name)
    {
        var definition = NamedDefinition(type);
        return definition.Kind != TypeRefKind.Unsupported
            && definition.Assembly == assembly
            && definition.Namespace == ns
            && definition.Name == name;
    }

    public static bool IsKnownFrameworkNamespace(TypeRef type, string assembly, string ns)
    {
        var definition = NamedDefinition(type);
        return definition.Kind != TypeRefKind.Unsupported
            && definition.Namespace == ns
            && IsCoreLibraryOrAssembly(definition.Assembly, assembly);
    }

    public static bool IsKnownFrameworkNamespacePrefix(TypeRef type, string assemblyPrefix, string nsPrefix)
    {
        var definition = NamedDefinition(type);
        return definition.Kind != TypeRefKind.Unsupported
            && (definition.Namespace == nsPrefix || definition.Namespace.StartsWith(nsPrefix + ".", StringComparison.Ordinal))
            && (definition.Assembly == TypeRef.CoreLibrary
                || definition.Assembly == assemblyPrefix
                || definition.Assembly.StartsWith(assemblyPrefix + ".", StringComparison.Ordinal));
    }

    static TypeRef NamedDefinition(TypeRef type)
        => type.Kind == TypeRefKind.GenericInstance ? type.ElementType ?? type : type;

    static bool IsCoreLibraryOrAssembly(string actual, string expected)
        => actual == TypeRef.CoreLibrary || actual == expected;
}
