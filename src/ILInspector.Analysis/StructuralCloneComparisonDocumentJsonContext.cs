using System.Text.Json.Serialization;

namespace ILInspector.Analysis;

/// <summary>
/// The serialization contract for the portable <see cref="StructuralCloneComparisonDocument"/>:
/// snake-case property names, string-spelled enums, and omitted nulls.
/// </summary>
/// <remarks>
/// See <c>AnnotatedSourceDocumentJsonContext</c>
/// (<c>src/ILInspector.Decompiler/AnnotatedSourceDocumentJsonContext.cs</c>) for the sibling
/// document contract this mirrors: one owned set of options avoids a second place for the
/// document's field names to drift.
/// </remarks>
[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(StructuralCloneComparisonDocument))]
[JsonSerializable(typeof(StructuralCloneModuleIdentity))]
public partial class StructuralCloneComparisonDocumentJsonContext : JsonSerializerContext;

/// <inheritdoc cref="StructuralCloneComparisonDocumentJsonContext"/>
[JsonSourceGenerationOptions(
    WriteIndented = false,
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(StructuralCloneComparisonDocument))]
[JsonSerializable(typeof(StructuralCloneModuleIdentity))]
public partial class StructuralCloneComparisonDocumentCompactJsonContext : JsonSerializerContext;
