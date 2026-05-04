using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

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
                if (IsEditorBrowsableNever(reader, attr))
                    return true;
            }
            else if (attrTypeName == ObsoleteAttributeName)
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Checks if the member has the [EditorBrowsable(Never)] attribute.
    /// </summary>
    public static bool HasEditorBrowsableNeverAttribute(MetadataReader reader, CustomAttributeHandleCollection attributes)
    {
        foreach (var attrHandle in attributes)
        {
            var attr = reader.GetCustomAttribute(attrHandle);
            var attrTypeName = GetAttributeTypeName(reader, attr.Constructor);
            if (attrTypeName == EditorBrowsableAttributeName && IsEditorBrowsableNever(reader, attr))
                return true;
        }
        return false;
    }

    /// <summary>
    /// Checks if the member has the [Obsolete] attribute, returning the optional message.
    /// </summary>
    public static bool TryGetObsoleteAttribute(MetadataReader reader, CustomAttributeHandleCollection attributes, out string? message)
    {
        foreach (var attrHandle in attributes)
        {
            var attr = reader.GetCustomAttribute(attrHandle);
            var attrTypeName = GetAttributeTypeName(reader, attr.Constructor);
            if (attrTypeName == ObsoleteAttributeName)
            {
                message = TryGetAttributeDisplayValue(reader, attr);
                return true;
            }
        }
        message = null;
        return false;
    }

    private static bool IsEditorBrowsableNever(MetadataReader reader, CustomAttribute attr)
    {
        // Check if the value is EditorBrowsableState.Never (value = 1)
        var value = reader.GetBlobBytes(attr.Value);
        // Attribute blob format: 2-byte prolog (0x0001), then the enum value as int32
        if (value.Length >= 6)
        {
            int enumValue = value[2] | (value[3] << 8) | (value[4] << 16) | (value[5] << 24);
            return enumValue == 1; // EditorBrowsableState.Never
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

    /// <summary>
    /// Gets custom attributes for a specific method, identified by type name, method name, and overload index.
    /// Returns short attribute names with optional display values. Filters out compiler-generated noise.
    /// </summary>
    public static List<(string Name, string? Value)> GetMethodAttributes(
        PEReader peReader, string fullTypeName, string methodName, int overloadIndex, bool publicOnly = true)
    {
        List<(string Name, string? Value)> results = [];
        if (!peReader.HasMetadata) return results;

        var reader = peReader.GetMetadataReader();
        var methodHandle = FindMethodHandle(reader, fullTypeName, methodName, overloadIndex, publicOnly);
        if (methodHandle.IsNil) return results;

        var method = reader.GetMethodDefinition(methodHandle);
        foreach (var attrHandle in method.GetCustomAttributes())
        {
            var attr = reader.GetCustomAttribute(attrHandle);
            var attrTypeName = GetAttributeTypeName(reader, attr.Constructor);
            if (attrTypeName == null || IsMethodNoiseAttribute(attrTypeName))
                continue;

            var shortName = GetShortAttributeName(attrTypeName);
            var value = TryGetAttributeDisplayValue(reader, attr);
            results.Add((shortName, value));
        }

        return results;
    }

    private static MethodDefinitionHandle FindMethodHandle(
        MetadataReader reader, string fullTypeName, string methodName, int overloadIndex, bool publicOnly)
    {
        foreach (var typeHandle in reader.TypeDefinitions)
        {
            var typeDef = reader.GetTypeDefinition(typeHandle);
            var name = TypeResolver.GetFullName(reader, typeDef);
            if (name != fullTypeName) continue;

            int matchIndex = 0;
            foreach (var mHandle in typeDef.GetMethods())
            {
                var method = reader.GetMethodDefinition(mHandle);
                if (publicOnly && (method.Attributes & MethodAttributes.Public) == 0)
                    continue;
                if (reader.GetString(method.Name) != methodName)
                    continue;

                if (matchIndex == overloadIndex)
                    return mHandle;
                matchIndex++;
            }
        }

        return default;
    }

    /// <summary>
    /// Compiler-generated attributes that are noise for method-level display.
    /// </summary>
    private static bool IsMethodNoiseAttribute(string name) => name switch
    {
        ExtensionAttributeName => true,
        "System.Runtime.CompilerServices.NullableContextAttribute" => true,
        "System.Runtime.CompilerServices.NullableAttribute" => true,
        "System.Runtime.CompilerServices.CompilerGeneratedAttribute" => true,
        "System.Runtime.CompilerServices.AsyncStateMachineAttribute" => true,
        "System.Runtime.CompilerServices.IteratorStateMachineAttribute" => true,
        "System.Diagnostics.DebuggerStepThroughAttribute" => true,
        "System.Diagnostics.DebuggerHiddenAttribute" => true,
        "System.Runtime.CompilerServices.MethodImplAttribute" => false, // interesting to see
        _ => false
    };

    private static string GetShortAttributeName(string fullName)
    {
        var name = fullName;
        if (name.EndsWith("Attribute", StringComparison.Ordinal))
            name = name[..^9];
        var lastDot = name.LastIndexOf('.');
        return lastDot >= 0 ? name[(lastDot + 1)..] : name;
    }

    private static string? TryGetAttributeDisplayValue(MetadataReader reader, CustomAttribute attr)
    {
        try
        {
            var blob = reader.GetBlobReader(attr.Value);
            if (blob.Length < 2) return null;
            blob.ReadUInt16(); // prolog

            var value = blob.ReadSerializedString();
            if (value == null) return null;

            foreach (char c in value)
            {
                if (char.IsControl(c) && c != '\t' && c != '\n' && c != '\r')
                    return null;
            }

            return string.IsNullOrWhiteSpace(value) ? null : value;
        }
        catch
        {
            return null;
        }
    }
}
