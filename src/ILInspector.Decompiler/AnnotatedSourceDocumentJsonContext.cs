using System.Text.Json.Serialization;

namespace ILInspector.Decompiler;

/// <summary>The direct annotated-source document wire contract.</summary>
[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(AnnotatedSourceDocument))]
public partial class AnnotatedSourceDocumentJsonContext : JsonSerializerContext
{
}

/// <summary>The compact direct annotated-source document wire contract.</summary>
[JsonSourceGenerationOptions(
    WriteIndented = false,
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(AnnotatedSourceDocument))]
public partial class AnnotatedSourceDocumentCompactJsonContext : JsonSerializerContext
{
}
