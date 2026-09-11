using System.Reflection.Metadata;
using CSharpText;

namespace ILInspector.Metadata;

/// <summary>
/// Resolves metadata parameter rows to stable presentation names.
/// </summary>
public static class MetadataParameterNames
{
    /// <summary>
    /// A resolved presentation name plus whether it was synthesized because the
    /// metadata omitted a Param row name or recorded an empty one.
    /// </summary>
    public readonly record struct ResolvedName(string Name, bool IsSynthesized);

    /// <summary>
    /// Preserves the first non-return Param row for each ordinal, then allocates
    /// collision-free fallbacks for absent or empty names.
    /// </summary>
    public static string[] Resolve(
        MetadataReader reader,
        ParameterHandleCollection parameterHandles,
        int parameterCount,
        IEnumerable<string>? reservedNames = null)
        => [.. ResolveWithProvenance(
            reader,
            parameterHandles,
            parameterCount,
            reservedNames).Select(name => name.Name)];

    /// <summary>
    /// Resolves names as <see cref="Resolve"/> while retaining whether each
    /// result came from artifact identity or fallback synthesis.
    /// </summary>
    public static ResolvedName[] ResolveWithProvenance(
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

        string[] names = CSharpParameterNames.Allocate(artifactNames, reservedNames);
        var result = new ResolvedName[names.Length];
        for (var index = 0; index < names.Length; index++)
        {
            result[index] = new ResolvedName(
                names[index],
                string.IsNullOrEmpty(artifactNames[index]));
        }
        return result;
    }
}
