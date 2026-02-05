using System.Reflection.Metadata;

namespace DotnetInspector.Metadata;

/// <summary>
/// Reads and checks custom attributes on types and members.
/// </summary>
public static class AttributeReader
{
    private const string ExtensionAttributeName = "System.Runtime.CompilerServices.ExtensionAttribute";
    private const string EditorBrowsableAttributeName = "System.ComponentModel.EditorBrowsableAttribute";
    private const string ObsoleteAttributeName = "System.ObsoleteAttribute";

    /// <summary>
    /// Checks if the member has the [Extension] attribute.
    /// </summary>
    public static bool HasExtensionAttribute(MetadataReader reader, CustomAttributeHandleCollection attributes)
    {
        foreach (var attrHandle in attributes)
        {
            var attr = reader.GetCustomAttribute(attrHandle);
            var attrTypeName = GetAttributeTypeName(reader, attr.Constructor);
            if (attrTypeName == ExtensionAttributeName)
                return true;
        }
        return false;
    }

    /// <summary>
    /// Checks if the member has EditorBrowsable(Never) or [Obsolete] attribute.
    /// </summary>
    public static bool HasHiddenAttribute(MetadataReader reader, CustomAttributeHandleCollection attributes)
    {
        foreach (var attrHandle in attributes)
        {
            var attr = reader.GetCustomAttribute(attrHandle);
            var attrTypeName = GetAttributeTypeName(reader, attr.Constructor);

            if (attrTypeName == EditorBrowsableAttributeName)
            {
                // Check if the value is EditorBrowsableState.Never (value = 1)
                var value = reader.GetBlobBytes(attr.Value);
                // Attribute blob format: 2-byte prolog (0x0001), then the enum value as int32
                if (value.Length >= 6)
                {
                    int enumValue = value[2] | (value[3] << 8) | (value[4] << 16) | (value[5] << 24);
                    if (enumValue == 1) // EditorBrowsableState.Never
                        return true;
                }
            }
            else if (attrTypeName == ObsoleteAttributeName)
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Checks if the member has a specific attribute by full type name.
    /// </summary>
    public static bool HasAttribute(MetadataReader reader, CustomAttributeHandleCollection attributes, string attributeTypeName)
    {
        foreach (var attrHandle in attributes)
        {
            var attr = reader.GetCustomAttribute(attrHandle);
            var attrName = GetAttributeTypeName(reader, attr.Constructor);
            if (attrName == attributeTypeName)
                return true;
        }
        return false;
    }

    /// <summary>
    /// Gets the fully qualified type name of an attribute from its constructor handle.
    /// </summary>
    public static string? GetAttributeTypeName(MetadataReader reader, EntityHandle constructorHandle)
    {
        if (constructorHandle.Kind == HandleKind.MemberReference)
        {
            var memberRef = reader.GetMemberReference((MemberReferenceHandle)constructorHandle);
            return TypeResolver.GetTypeName(reader, memberRef.Parent);
        }
        else if (constructorHandle.Kind == HandleKind.MethodDefinition)
        {
            var methodDef = reader.GetMethodDefinition((MethodDefinitionHandle)constructorHandle);
            var typeDef = reader.GetTypeDefinition(methodDef.GetDeclaringType());
            return TypeResolver.GetFullName(reader, typeDef);
        }
        return null;
    }
}
