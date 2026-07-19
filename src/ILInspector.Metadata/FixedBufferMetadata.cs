using System.Reflection.Metadata;

namespace ILInspector.Metadata;

/// <summary>Compiler fixed-buffer source-field metadata decoded from <c>FixedBufferAttribute</c>.</summary>
public sealed record FixedBufferMetadataInfo(string ElementTypeFullName, int Length);

public static class FixedBufferMetadata
{
    const string AttributeName = "System.Runtime.CompilerServices.FixedBufferAttribute";

    public static FixedBufferMetadataInfo? Read(MetadataReader reader, CustomAttributeHandleCollection attributes)
    {
        foreach (var handle in attributes)
        {
            var attribute = reader.GetCustomAttribute(handle);
            if (AttributeReader.GetAttributeTypeName(reader, attribute.Constructor) != AttributeName)
                continue;

            try
            {
                var blob = reader.GetBlobReader(attribute.Value);
                if (blob.ReadUInt16() != 1)
                    return null;
                string? elementTypeName = blob.ReadSerializedString();
                int length = blob.ReadInt32();
                if (length <= 0 || ElementTypeFullName(elementTypeName) is not { } elementType)
                    return null;
                return new FixedBufferMetadataInfo(elementType, length);
            }
            catch (BadImageFormatException)
            {
                return null;
            }
        }

        return null;
    }

    public static string? ElementTypeFullName(string? assemblyQualifiedName)
    {
        if (string.IsNullOrWhiteSpace(assemblyQualifiedName))
            return null;
        int comma = assemblyQualifiedName.IndexOf(',');
        string name = comma < 0 ? assemblyQualifiedName : assemblyQualifiedName[..comma];
        return IsSupportedElementType(name) ? name : null;
    }

    public static bool IsSupportedElementType(string fullName)
        => fullName is
            "System.Boolean" or
            "System.Byte" or
            "System.SByte" or
            "System.Char" or
            "System.Int16" or
            "System.UInt16" or
            "System.Int32" or
            "System.UInt32" or
            "System.Int64" or
            "System.UInt64" or
            "System.Single" or
            "System.Double";
}
