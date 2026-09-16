using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Text.Json.Serialization;

namespace ILInspector.Metadata;

/// <summary>
/// Reader-independent MethodDef flags and RVA presence, not a C# extern
/// decision or proof that an implementation body is available.
/// </summary>
public sealed record ApiMethodImplementationFacts(
    Guid ModuleVersionId,
    int MethodToken,
    [property: JsonConverter(typeof(JsonStringEnumConverter<MethodAttributes>))]
    MethodAttributes Attributes,
    [property: JsonConverter(typeof(JsonStringEnumConverter<MethodImplAttributes>))]
    MethodImplAttributes ImplAttributes,
    bool HasBodyRva)
{
    internal static ApiMethodImplementationFacts Read(
        MetadataReader reader,
        Guid moduleVersionId,
        MethodDefinitionHandle handle)
    {
        MethodDefinition method = reader.GetMethodDefinition(handle);
        return new(
            moduleVersionId,
            MetadataTokens.GetToken(handle),
            method.Attributes,
            method.ImplAttributes,
            method.RelativeVirtualAddress != 0);
    }

    internal static ImmutableArray<ApiMethodImplementationFacts> ReadAccessors(
        MetadataReader reader,
        Guid moduleVersionId,
        MethodDefinitionHandle[] handles)
        => [.. handles.Where(handle => !handle.IsNil).Distinct()
            .Select(handle => Read(reader, moduleVersionId, handle))];
}
