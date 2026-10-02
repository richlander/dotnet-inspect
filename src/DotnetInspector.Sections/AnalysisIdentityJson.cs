using System.Text.Json;

using ILInspector.Analysis;

namespace DotnetInspector.Sections;

/// <summary>Shared JSON lowering for Analysis-owned type identities.</summary>
public static class AnalysisIdentityJson
{
    public static void WriteType(
        Utf8JsonWriter writer,
        TypeRef type)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(type);
        JsonSerializer.Serialize(
            writer,
            CallGraphJsonType.From(type),
            CallGraphJsonContext.Default.CallGraphJsonType);
    }
}
