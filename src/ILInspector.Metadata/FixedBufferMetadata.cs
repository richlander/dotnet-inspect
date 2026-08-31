using System.Reflection.Metadata;

namespace ILInspector.Metadata;

/// <summary>Compiler fixed-buffer source-field metadata decoded from <c>FixedBufferAttribute</c>.</summary>
public sealed record FixedBufferMetadataInfo(string ElementTypeFullName, int Length);

internal enum FixedBufferMetadataReadState
{
    Absent,
    Present,
    Malformed,
    Unavailable,
}

internal readonly record struct FixedBufferMetadataReadResult(
    FixedBufferMetadataReadState State,
    FixedBufferMetadataInfo? Info);

public static class FixedBufferMetadata
{
    const string AttributeName = "System.Runtime.CompilerServices.FixedBufferAttribute";

    public static FixedBufferMetadataInfo? Read(MetadataReader reader, CustomAttributeHandleCollection attributes)
        => Read(
            reader,
            attributes,
            maxAttributeRows: int.MaxValue,
            beforeMaterialize: null).Info;

    internal static FixedBufferMetadataReadResult Read(
        MetadataReader reader,
        CustomAttributeHandleCollection attributes,
        int maxAttributeRows,
        Action<int>? beforeMaterialize)
    {
        if (attributes.Count > maxAttributeRows)
        {
            return new(
                FixedBufferMetadataReadState.Unavailable,
                Info: null);
        }

        FixedBufferMetadataInfo? found = null;
        bool malformed = false;
        foreach (var handle in attributes)
        {
            try
            {
                var attribute = reader.GetCustomAttribute(handle);
                if (AttributeReader.GetAttributeTypeName(
                        reader,
                        attribute.Constructor,
                        beforeMaterialize) != AttributeName)
                {
                    continue;
                }
                if (!AttributeReader.IsPlatformAttributeType(
                        reader,
                        attribute.Constructor,
                        AttributeName,
                        beforeMaterialize))
                {
                    continue;
                }
                if (!AttributeReader.HasExpectedSystemTypeInt32Constructor(
                        reader,
                        attribute.Constructor,
                        beforeMaterialize)
                    || AttributeDecoder
                        .TryDecodePreservingSerializedTypeNames(
                            reader,
                            attribute,
                            beforeMaterialize) is not
                        {
                            FixedArguments:
                            [
                                {
                                    Type: "System.Type",
                                    Value: string serializedType,
                                },
                                {
                                    Type: "int",
                                    Value: int length,
                                },
                            ],
                            NamedArguments.Length: 0,
                        }
                    || length <= 0
                    || ElementTypeFullName(serializedType) is not
                        { } elementType)
                {
                    malformed = true;
                    continue;
                }

                var current =
                    new FixedBufferMetadataInfo(elementType, length);
                if (found is not null && found != current)
                {
                    malformed = true;
                    continue;
                }
                found = current;
            }
            catch (Exception ex) when (
                ex is BadImageFormatException
                    or ArgumentOutOfRangeException
                    or InvalidOperationException)
            {
                return new(
                    FixedBufferMetadataReadState.Unavailable,
                    Info: null);
            }
        }

        if (malformed)
        {
            return new(
                FixedBufferMetadataReadState.Malformed,
                Info: null);
        }
        return found is null
            ? new(
                FixedBufferMetadataReadState.Absent,
                Info: null)
            : new(
                FixedBufferMetadataReadState.Present,
                found);
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
