using System.Reflection.Metadata;

namespace ILInspector.Metadata;

/// <summary>
/// Reads <c>TupleElementNamesAttribute</c> element names from custom-attribute
/// collections. The names record the C# element names of a <c>System.ValueTuple</c>
/// instantiation authored with named elements (e.g. the <c>count</c>/<c>name</c>
/// in <c>(int count, string name)</c>), so the type view can render tuple syntax
/// with names instead of a bare <c>System.ValueTuple&lt;int, string&gt;</c>.
/// </summary>
/// <remarks>
/// The attribute stores a single <c>string[]</c> argument: a flat, breadth-first
/// stream of element names across every tuple in the type (a tuple's own element
/// names first, then its nested tuples), with <c>null</c> for unnamed positions
/// and trailing <c>null</c> padding per tuple for its 8+ arity "Rest" nesting.
/// A fully unnamed tuple emits no attribute at all. The consuming walk lives in
/// <see cref="TypeNode.ApplyTupleNames"/>, which mirrors this ordering.
/// </remarks>
public static class TupleElementNamesReader
{
    /// <summary>
    /// Gets the TupleElementNamesAttribute names array (null entries for unnamed
    /// positions) from custom attributes. Returns null when the attribute is
    /// absent or its <c>string[]</c> argument is itself null.
    /// </summary>
    public static string?[]? GetTupleElementNames(MetadataReader reader, CustomAttributeHandleCollection attributes)
    {
        foreach (var attrHandle in attributes)
        {
            var attr = reader.GetCustomAttribute(attrHandle);
            var attrTypeName = AttributeReader.GetAttributeTypeName(reader, attr.Constructor);
            if (attrTypeName != KnownAttributeNames.TupleElementNamesAttribute) continue;

            var blob = reader.GetBlobReader(attr.Value);
            if (blob.RemainingBytes < 2) return null;
            blob.ReadUInt16(); // prolog

            if (blob.RemainingBytes < 4) return null;
            uint count = blob.ReadUInt32();
            if (count == 0xFFFF_FFFF) return null; // null array
            // Each serialized string is at least one byte (length or 0xFF null marker).
            if (count > (uint)blob.RemainingBytes) return null;

            var names = new string?[count];
            for (uint i = 0; i < count; i++)
                names[i] = blob.ReadSerializedString();
            return names;
        }
        return null;
    }

    /// <summary>
    /// Gets TupleElementNamesAttribute names for a specific parameter by sequence
    /// number. Sequence 0 = return type, 1+ = parameters.
    /// </summary>
    public static string?[]? GetParameterTupleElementNames(
        MetadataReader reader, ParameterHandleCollection paramHandles, int sequenceNumber)
    {
        foreach (var handle in paramHandles)
        {
            var param = reader.GetParameter(handle);
            if (param.SequenceNumber == sequenceNumber)
                return GetTupleElementNames(reader, param.GetCustomAttributes());
        }
        return null;
    }
}
