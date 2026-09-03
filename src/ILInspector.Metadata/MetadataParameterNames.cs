using System.Reflection.Metadata;
using CSharpText;

namespace ILInspector.Metadata;

/// <summary>
/// Resolves metadata parameter rows to stable presentation names.
/// </summary>
public static class MetadataParameterNames
{
    /// <summary>
    /// Preserves the first non-return Param row for each ordinal, then allocates
    /// collision-free fallbacks for absent or empty names.
    /// </summary>
    public static string[] Resolve(
        MetadataReader reader,
        ParameterHandleCollection parameterHandles,
        int parameterCount,
        IEnumerable<string>? reservedNames = null)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentOutOfRangeException.ThrowIfNegative(parameterCount);

        var artifactNames = new string?[parameterCount];
        foreach (var handle in parameterHandles)
        {
            var parameter = reader.GetParameter(handle);
            int index = parameter.SequenceNumber - 1;
            if ((uint)index < (uint)artifactNames.Length
                && artifactNames[index] is null)
            {
                artifactNames[index] = reader.GetString(parameter.Name);
            }
        }

        return CSharpParameterNames.Allocate(artifactNames, reservedNames);
    }
}
