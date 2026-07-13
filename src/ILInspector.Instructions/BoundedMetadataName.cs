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
        var segments = new List<string>();
        var visited = new HashSet<TypeDefinitionHandle>();
        string ns = "";
        var current = handle;

        while (segments.Count < MaxSegments && visited.Add(current))
        {
            var type = reader.GetTypeDefinition(current);
            segments.Add(reader.GetString(type.Name));
            ns = reader.GetString(type.Namespace);

            var declaring = type.GetDeclaringType();
            if (declaring.IsNil)
                break;
            current = declaring;
        }

        segments.Reverse();
        string name = string.Join("+", segments);
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
        var segments = new List<string>();
        var visited = new HashSet<TypeReferenceHandle>();
        EntityHandle scope = default;
        var current = handle;

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
        string fullName = string.Join("+", segments);
        return scope.Kind == HandleKind.AssemblyReference
            ? $"[{reader.GetString(reader.GetAssemblyReference((AssemblyReferenceHandle)scope).Name)}]{fullName}"
            : fullName;
    }
}
