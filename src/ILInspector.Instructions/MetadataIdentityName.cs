using System.Reflection.Metadata;
using ILInspector.Metadata;

namespace ILInspector.Instructions;

static class MetadataIdentityName
{
    public static MetadataTypeNameResult TypeDefinition(
        MetadataReader reader,
        TypeDefinitionHandle handle,
        bool includeAssembly)
    {
        var result = MetadataRelationshipTraversal.WalkTypeDefinitionDeclaringChain(reader, handle);
        if (result is RelationshipTraversalResult<RelationshipChain<TypeDefinitionHandle>>.Rejected rejected)
            return new MetadataTypeNameResult.Rejected(MetadataTypeNameFailure.From(rejected.Rejection));

        var completed = (RelationshipTraversalResult<RelationshipChain<TypeDefinitionHandle>>.Completed)result;
        try
        {
            var root = reader.GetTypeDefinition(completed.Value.Handles[0]);
            string ns = reader.GetString(root.Namespace);
            string name = string.Join(
                "+",
                completed.Value.Handles.Select(current =>
                    reader.GetString(reader.GetTypeDefinition(current).Name)));
            return new MetadataTypeNameResult.Resolved(
                QualifyDefinition(reader, ns, name, includeAssembly));
        }
        catch (Exception ex) when (ex is BadImageFormatException or ArgumentOutOfRangeException)
        {
            return ProjectionRejected(handle, completed.ConsumedNodes, ex);
        }
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

    public static MetadataTypeNameResult TypeReference(
        MetadataReader reader,
        TypeReferenceHandle handle)
    {
        var result = MetadataRelationshipTraversal.WalkTypeReferenceResolutionScope(reader, handle);
        if (result is RelationshipTraversalResult<RelationshipChain<TypeReferenceHandle>>.Rejected rejected)
            return new MetadataTypeNameResult.Rejected(MetadataTypeNameFailure.From(rejected.Rejection));

        var completed = (RelationshipTraversalResult<RelationshipChain<TypeReferenceHandle>>.Completed)result;
        try
        {
            var root = reader.GetTypeReference(completed.Value.Handles[0]);
            string ns = reader.GetString(root.Namespace);
            string name = string.Join(
                "+",
                completed.Value.Handles.Select(current =>
                    reader.GetString(reader.GetTypeReference(current).Name)));
            string fullName = ns.Length == 0 ? name : $"{ns}.{name}";
            return new MetadataTypeNameResult.Resolved(
                QualifyReference(reader, completed.Value.Terminal, fullName));
        }
        catch (Exception ex) when (ex is BadImageFormatException or ArgumentOutOfRangeException)
        {
            return ProjectionRejected(handle, completed.ConsumedNodes, ex);
        }
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

    static MetadataTypeNameResult ProjectionRejected(
        EntityHandle subject,
        int consumedNodes,
        Exception exception)
        => new MetadataTypeNameResult.Rejected(
            MetadataTypeNameFailure.From(
                new RelationshipTraversalRejection(
                    RelationshipTraversalRejectionKind.MalformedMetadata,
                    exception.Message,
                    subject,
                    consumedNodes)));
}
