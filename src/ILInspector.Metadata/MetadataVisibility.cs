using System.Reflection.Metadata;

namespace ILInspector.Metadata;

internal static class MetadataVisibility
{
    internal static bool IsExternallyVisible(
        MetadataReader reader,
        TypeDefinitionHandle handle)
    {
        int remaining = reader.TypeDefinitions.Count;
        while (!handle.IsNil)
        {
            if (remaining-- == 0)
            {
                throw new BadImageFormatException(
                    "The nested type chain contains a cycle.");
            }

            TypeDefinition definition =
                reader.GetTypeDefinition(handle);
            if (!definition.IsPublic)
                return false;

            handle = definition.GetDeclaringType();
        }

        return true;
    }
}
