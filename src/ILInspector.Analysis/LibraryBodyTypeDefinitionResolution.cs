using System.Reflection.Metadata;
using ILInspector.Metadata;

namespace ILInspector.Analysis;

internal static class LibraryBodyTypeDefinitionResolution
{
    internal static (MetadataReader DefiningReader, TypeDefinitionHandle Definition)?
        Resolve(
            MetadataReader sourceReader,
            TypeRef type,
            Func<AssemblyReferenceIdentity, AssemblyResolutionScope,
                MetadataTypeDefinitionName,
                (MetadataReader DefiningReader, TypeDefinitionHandle Definition)?>
                resolveExternal)
    {
        TypeRef definition = type.Kind == TypeRefKind.GenericInstance
            ? type.ElementType ?? type
            : type;
        if (definition.Resolution is not { } resolution)
            return null;

        if (resolution.Origin is TypeReferenceOrigin.CurrentAssembly)
        {
            TypeDefinitionHandle match = default;
            foreach (var handle in sourceReader.TypeDefinitions)
            {
                TypeRef candidate = TypeRefDecoder.Instance
                    .GetTypeFromDefinition(sourceReader, handle, 0);
                if (candidate.Resolution?.Type != resolution.Type)
                    continue;
                if (!match.IsNil)
                    return null;
                match = handle;
            }
            return match.IsNil ? null : (sourceReader, match);
        }

        return resolution.Origin is TypeReferenceOrigin.AssemblyReference assembly
            ? resolveExternal(
                assembly.Assembly,
                TypeResolutionRequestFactory.Scope(assembly.Assembly),
                resolution.Type)
            : null;
    }

    internal static TypeRef Decode(MetadataReader reader, EntityHandle handle)
        => handle.Kind switch
        {
            HandleKind.TypeDefinition => TypeRefDecoder.Instance
                .GetTypeFromDefinition(reader, (TypeDefinitionHandle)handle, 0),
            HandleKind.TypeReference => TypeRefDecoder.Instance
                .GetTypeFromReference(reader, (TypeReferenceHandle)handle, 0),
            HandleKind.TypeSpecification => TypeRefDecoder.Instance
                .GetTypeFromSpecification(
                    reader, GenericScope.Empty, (TypeSpecificationHandle)handle, 0),
            _ => TypeRef.Unsupported("base type handle is unsupported"),
        };
}
