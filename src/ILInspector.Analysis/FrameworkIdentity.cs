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

    public static bool IsKnownFrameworkNamespace(TypeRef type, string ns)
    {
        var definition = NamedDefinition(type);
        return definition.Kind != TypeRefKind.Unsupported
            && definition.Namespace == ns
            && IsFrameworkAssembly(definition.Assembly);
    }

    public static bool IsKnownFrameworkNamespacePrefix(TypeRef type, string nsPrefix)
    {
        var definition = NamedDefinition(type);
        return definition.Kind != TypeRefKind.Unsupported
            && (definition.Namespace == nsPrefix || definition.Namespace.StartsWith(nsPrefix + ".", StringComparison.Ordinal))
            && IsFrameworkAssembly(definition.Assembly);
    }

    static TypeRef NamedDefinition(TypeRef type)
        => type.Kind == TypeRefKind.GenericInstance ? type.ElementType ?? type : type;

    static bool IsFrameworkAssembly(string assembly)
        => assembly == TypeRef.CoreLibrary
            || assembly == "System"
            || assembly.StartsWith("System.", StringComparison.Ordinal)
            || assembly == "Microsoft.CSharp";
}
