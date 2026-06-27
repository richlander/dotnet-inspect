namespace ILInspector.Analysis;

internal static class FrameworkIdentity
{
    // Legacy-TFM facade for the modern split assemblies (#1708 Row B). On .NET
    // Framework, namespaces that modern .NET ships as separate assemblies
    // (System.Linq, System.Linq.Expressions, System.Collections, ...) live in
    // System.Core, so a real framework type can carry that assembly identity.
    internal const string LegacyCombinedFacadeAssembly = "System.Core";

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
            && MatchesFrameworkAssembly(definition.Assembly, assembly)
            && definition.Namespace == ns
            && definition.Name == name;
    }

    public static bool IsKnownFrameworkNamespace(TypeRef type, string assembly, string ns)
    {
        var definition = NamedDefinition(type);
        return definition.Kind != TypeRefKind.Unsupported
            && definition.Namespace == ns
            && MatchesFrameworkAssembly(definition.Assembly, assembly);
    }

    public static bool IsKnownFrameworkNamespacePrefix(TypeRef type, string assemblyPrefix, string nsPrefix)
    {
        var definition = NamedDefinition(type);
        return definition.Kind != TypeRefKind.Unsupported
            && (definition.Namespace == nsPrefix || definition.Namespace.StartsWith(nsPrefix + ".", StringComparison.Ordinal))
            && (definition.Assembly == TypeRef.CoreLibrary
                || definition.Assembly == assemblyPrefix
                || definition.Assembly.StartsWith(assemblyPrefix + ".", StringComparison.Ordinal)
                || definition.Assembly == LegacyCombinedFacadeAssembly);
    }

    static TypeRef NamedDefinition(TypeRef type)
        => type.Kind == TypeRefKind.GenericInstance ? type.ElementType ?? type : type;

    // True when an actual declaring-assembly identity matches an expected framework
    // assembly, tolerating legacy-TFM facades (#1708 Row B). A non-corelib framework
    // type can surface as corelib (netstandard / mscorlib / System.Runtime facades
    // canonicalize to corelib in TypeRef.CanonicalAssembly) or as System.Core (.NET
    // Framework). Namespace and name still guard against user lookalikes, and the
    // facades only widen matching for non-corelib expectations so corelib checks stay
    // exact.
    internal static bool MatchesFrameworkAssembly(string actual, string expected)
        => actual == expected
            || actual == TypeRef.CoreLibrary
            || (expected != TypeRef.CoreLibrary && actual == LegacyCombinedFacadeAssembly);
}
