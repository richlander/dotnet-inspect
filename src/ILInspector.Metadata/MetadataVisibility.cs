using System.Reflection;
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
            TypeDefinitionHandle declaringType =
                definition.GetDeclaringType();
            switch (definition.Attributes
                & TypeAttributes.VisibilityMask)
            {
                case TypeAttributes.Public:
                    if (!declaringType.IsNil)
                    {
                        throw new BadImageFormatException(
                            "A top-level public type has a declaring "
                                + "type.");
                    }

                    return true;
                case TypeAttributes.NestedPublic:
                    if (declaringType.IsNil)
                    {
                        throw new BadImageFormatException(
                            "A nested public type has no declaring "
                                + "type.");
                    }

                    handle = declaringType;
                    break;
                default:
                    return false;
            }
        }

        return false;
    }
}
