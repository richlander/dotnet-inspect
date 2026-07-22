using System.Reflection.Metadata;

namespace ILInspector.Metadata;

/// <summary>
/// Reads <c>DynamicAttribute</c> transform flags from assembly custom-attribute
/// collections. The flags record which <c>System.Object</c> positions in a
/// signature were authored as <c>dynamic</c>, so the type view can render
/// <c>dynamic</c> instead of <c>object</c>.
/// </summary>
/// <remarks>
/// Unlike nullability there is no <c>DynamicContextAttribute</c>: a position is
/// dynamic only when <c>DynamicAttribute</c> is present and its flag is set.
/// Flags are returned as a <c>byte[]</c> of 0/1 so the same preorder walk that
/// <see cref="TypeNode.ApplyNullability"/> uses can consume them via
/// <c>ApplyDynamic</c>. The marker form <c>[Dynamic]</c> (no constructor
/// argument) is emitted only for a bare <c>dynamic</c> and is returned as a
/// single-element array, which the walk consumes at the single (bare object)
/// position without broadcasting to inner nodes.
/// </remarks>
public static class DynamicReader
{
    /// <summary>
    /// Gets the DynamicAttribute transform-flags array (0 = object, 1 = dynamic)
    /// from custom attributes. Returns null when the attribute is not present.
    /// The no-argument marker form returns a one-element array.
    /// </summary>
    public static byte[]? GetDynamicFlags(MetadataReader reader, CustomAttributeHandleCollection attributes)
    {
        foreach (var attrHandle in attributes)
        {
            var attr = reader.GetCustomAttribute(attrHandle);
            var attrTypeName = AttributeReader.GetAttributeTypeName(reader, attr.Constructor);
            if (attrTypeName != KnownAttributeNames.DynamicAttribute) continue;

            var blob = reader.GetBlobReader(attr.Value);
            if (blob.Length < 2) return null;
            blob.ReadUInt16(); // prolog

            // DynamicAttribute():        prolog(2) + namedArgs(2) = 4          -> marker form
            // DynamicAttribute(bool[]):  prolog(2) + count(4) + N bytes + namedArgs(2) = 8+N
            if (blob.RemainingBytes == 2)
            {
                // Marker form: the whole (bare object) type is dynamic.
                return [1];
            }

            if (blob.RemainingBytes >= 6)
            {
                int count = blob.ReadInt32();
                if (count < 0 || count > blob.RemainingBytes - 2) return null;
                var flags = new byte[count];
                for (int i = 0; i < count; i++)
                    flags[i] = (byte)(blob.ReadByte() != 0 ? 1 : 0);
                return flags;
            }

            return null;
        }
        return null;
    }

    /// <summary>
    /// Gets DynamicAttribute transform flags for a specific parameter by sequence
    /// number. Sequence 0 = return type, 1+ = parameters.
    /// </summary>
    public static byte[]? GetParameterDynamicFlags(
        MetadataReader reader, ParameterHandleCollection paramHandles, int sequenceNumber)
    {
        foreach (var handle in paramHandles)
        {
            var param = reader.GetParameter(handle);
            if (param.SequenceNumber == sequenceNumber)
                return GetDynamicFlags(reader, param.GetCustomAttributes());
        }
        return null;
    }
}
