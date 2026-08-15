using System.Reflection;
using System.Reflection.Metadata;

namespace ILInspector.Metadata;

internal static class CoreLibraryRootAuthentication
{
    static readonly System.Runtime.CompilerServices
        .ConditionalWeakTable<MetadataReader, object>
        s_declaresRoot = new();

    static readonly object s_true = new();
    static readonly object s_false = new();

    internal static bool DeclaresUniqueTopLevelCoreLibraryRoot(
        MetadataReader reader) =>
        s_declaresRoot.GetValue(
            reader,
            static current =>
                Scan(current) ? s_true : s_false)
        == s_true;

    internal static bool IsUniqueTopLevelCoreLibraryRoot(
        MetadataReader reader,
        TypeDefinitionHandle handle)
    {
        try
        {
            return IsValidTopLevelCoreLibraryRoot(
                       reader,
                       reader.GetTypeDefinition(handle))
                && DeclaresUniqueTopLevelCoreLibraryRoot(reader);
        }
        catch (Exception ex) when (
            ex is BadImageFormatException
                or ArgumentOutOfRangeException)
        {
            return false;
        }
    }

    internal static bool IsValidTopLevelCoreLibraryRoot(
        MetadataReader reader,
        TypeDefinition definition) =>
        definition.BaseType.IsNil
        && (definition.Attributes
            & TypeAttributes.Interface) == 0
        && IsTopLevel(definition.Attributes)
        && reader.StringComparer.Equals(
            definition.Namespace,
            "System")
        && reader.StringComparer.Equals(
            definition.Name,
            "Object");

    static bool IsTopLevel(TypeAttributes attributes) =>
        (attributes & TypeAttributes.VisibilityMask)
            is TypeAttributes.NotPublic
                or TypeAttributes.Public;

    static bool Scan(MetadataReader reader)
    {
        int matches = 0;
        try
        {
            foreach (TypeDefinitionHandle handle
                in reader.TypeDefinitions)
            {
                if (!IsValidTopLevelCoreLibraryRoot(
                        reader,
                        reader.GetTypeDefinition(handle)))
                {
                    continue;
                }

                if (++matches > 1)
                    return false;
            }
        }
        catch (Exception ex) when (
            ex is BadImageFormatException
                or ArgumentOutOfRangeException)
        {
            return false;
        }

        return matches == 1;
    }
}
