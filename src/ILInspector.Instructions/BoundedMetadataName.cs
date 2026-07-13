using System.Reflection.Metadata;

namespace ILInspector.Instructions;

static class BoundedMetadataName
{
    const int MaxSegments = 256;

    public static string TypeDefinition(
        MetadataReader reader,
        TypeDefinitionHandle handle,
        bool includeAssembly)
    {
        var first = reader.GetTypeDefinition(handle);
        string firstName = reader.GetString(first.Name);
        string ns = reader.GetString(first.Namespace);
        var declaring = first.GetDeclaringType();
        if (declaring.IsNil)
            return QualifyDefinition(reader, ns, firstName, includeAssembly);

        var segments = new List<string> { firstName };
        var visited = new HashSet<TypeDefinitionHandle> { handle };
        var current = declaring;
        while (segments.Count < MaxSegments && visited.Add(current))
        {
            var type = reader.GetTypeDefinition(current);
            segments.Add(reader.GetString(type.Name));
            ns = reader.GetString(type.Namespace);

            declaring = type.GetDeclaringType();
            if (declaring.IsNil)
                break;
            current = declaring;
        }

        segments.Reverse();
        return QualifyDefinition(reader, ns, string.Join("+", segments), includeAssembly);
    }

    static string QualifyDefinition(
        MetadataReader reader,
        string ns,
        string name,
        bool includeAssembly)
    {
        string fullName = ns.Length == 0 ? name : $"{ns}.{name}";
        if (!includeAssembly)
            return fullName;

        string assembly = reader.IsAssembly ? reader.GetString(reader.GetAssemblyDefinition().Name) : "";
        return $"[{assembly}]{fullName}";
    }

    public static string TypeReference(
        MetadataReader reader,
        TypeReferenceHandle handle)
    {
        var first = reader.GetTypeReference(handle);
        string firstName = reader.GetString(first.Name);
        string firstNamespace = reader.GetString(first.Namespace);
        string firstFullName = firstNamespace.Length == 0 ? firstName : $"{firstNamespace}.{firstName}";
        var scope = first.ResolutionScope;
        if (scope.Kind != HandleKind.TypeReference)
            return QualifyReference(reader, scope, firstFullName);

        var segments = new List<string> { firstFullName };
        var visited = new HashSet<TypeReferenceHandle> { handle };
        var current = (TypeReferenceHandle)scope;
        while (segments.Count < MaxSegments && visited.Add(current))
        {
            var type = reader.GetTypeReference(current);
            string name = reader.GetString(type.Name);
            string ns = reader.GetString(type.Namespace);
            segments.Add(ns.Length == 0 ? name : $"{ns}.{name}");
            scope = type.ResolutionScope;

            if (scope.Kind != HandleKind.TypeReference)
                break;
            current = (TypeReferenceHandle)scope;
        }

        segments.Reverse();
        return QualifyReference(reader, scope, string.Join("+", segments));
    }

    static string QualifyReference(
        MetadataReader reader,
        EntityHandle scope,
        string fullName)
    {
        return scope.Kind == HandleKind.AssemblyReference
            ? $"[{reader.GetString(reader.GetAssemblyReference((AssemblyReferenceHandle)scope).Name)}]{fullName}"
            : fullName;
    }
}
