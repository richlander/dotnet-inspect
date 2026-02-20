using System.Reflection.Metadata;

namespace DotnetInspector.Metadata;

/// <summary>
/// Reads nullability annotation metadata (NullableAttribute and NullableContextAttribute)
/// from assembly custom attribute collections.
/// </summary>
public static class NullabilityReader
{
    private const string NullableAttributeName = "System.Runtime.CompilerServices.NullableAttribute";
    private const string NullableContextAttributeName = "System.Runtime.CompilerServices.NullableContextAttribute";

    /// <summary>
    /// Gets the NullableContextAttribute default byte from custom attributes.
    /// Returns 0 (oblivious) if not found.
    /// </summary>
    public static byte GetNullableContext(MetadataReader reader, CustomAttributeHandleCollection attributes)
    {
        foreach (var attrHandle in attributes)
        {
            var attr = reader.GetCustomAttribute(attrHandle);
            var attrTypeName = AttributeReader.GetAttributeTypeName(reader, attr.Constructor);
            if (attrTypeName != NullableContextAttributeName) continue;

            var blob = reader.GetBlobReader(attr.Value);
            if (blob.Length < 3) continue;
            blob.ReadUInt16(); // prolog
            return blob.ReadByte();
        }
        return 0;
    }

    /// <summary>
    /// Gets the NullableAttribute byte array from custom attributes.
    /// Returns null if the attribute is not present.
    /// Single-byte constructor returns a one-element array.
    /// </summary>
    public static byte[]? GetNullableBytes(MetadataReader reader, CustomAttributeHandleCollection attributes)
    {
        foreach (var attrHandle in attributes)
        {
            var attr = reader.GetCustomAttribute(attrHandle);
            var attrTypeName = AttributeReader.GetAttributeTypeName(reader, attr.Constructor);
            if (attrTypeName != NullableAttributeName) continue;

            var blob = reader.GetBlobReader(attr.Value);
            if (blob.Length < 3) return null;
            blob.ReadUInt16(); // prolog

            // NullableAttribute(byte):   prolog(2) + byte(1) + namedArgs(2) = 5
            // NullableAttribute(byte[]): prolog(2) + count(4) + N bytes + namedArgs(2) = 8+N
            if (blob.RemainingBytes == 3)
            {
                return [blob.ReadByte()];
            }
            else if (blob.RemainingBytes >= 6)
            {
                int count = blob.ReadInt32();
                if (count < 0 || count > blob.RemainingBytes - 2) return null;
                var bytes = new byte[count];
                for (int i = 0; i < count; i++)
                    bytes[i] = blob.ReadByte();
                return bytes;
            }

            return null;
        }
        return null;
    }

    /// <summary>
    /// Gets NullableAttribute bytes for a specific parameter by sequence number.
    /// Sequence 0 = return type, 1+ = parameters.
    /// </summary>
    public static byte[]? GetParameterNullableBytes(
        MetadataReader reader, ParameterHandleCollection paramHandles, int sequenceNumber)
    {
        foreach (var handle in paramHandles)
        {
            var param = reader.GetParameter(handle);
            if (param.SequenceNumber == sequenceNumber)
                return GetNullableBytes(reader, param.GetCustomAttributes());
        }
        return null;
    }
}
