using System.Text.Json.Serialization;

namespace ILInspector.Decompiler;

/// <summary>
/// The serialization contract for the portable <see cref="AnnotatedSourceDocument"/>: snake-case
/// property names, string-spelled enums, and omitted nulls.
/// </summary>
/// <remarks>
/// The document is a portable artifact with more than one producer — the CLI's
/// <c>Annotated Source Document</c> section and <c>--json</c> output, and the browser engine's
/// annotated-source export — and one consumer contract, the viewer in
/// <c>prototypes/annotated-source-viewer</c>. Owning the options here rather than in each
/// producer keeps the wire shape a single rule: a second copy of these options is a second place
/// for the document's field names to drift.
/// </remarks>
[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(AnnotatedSourceDocument))]
[JsonSerializable(typeof(CSharpNodeCorrespondenceResult))]
public partial class AnnotatedSourceDocumentJsonContext : JsonSerializerContext;

/// <inheritdoc cref="AnnotatedSourceDocumentJsonContext"/>
[JsonSourceGenerationOptions(
    WriteIndented = false,
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(AnnotatedSourceDocument))]
[JsonSerializable(typeof(CSharpNodeCorrespondenceResult))]
public partial class AnnotatedSourceDocumentCompactJsonContext : JsonSerializerContext;
