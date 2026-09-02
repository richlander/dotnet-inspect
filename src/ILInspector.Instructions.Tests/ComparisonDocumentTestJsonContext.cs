using System.Text.Json.Serialization;

namespace ILInspector.Instructions.Tests;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower)]
[JsonSerializable(typeof(ComparisonDocumentTestPayload))]
internal partial class ComparisonDocumentTestJsonContext : JsonSerializerContext;

internal sealed record ComparisonDocumentTestPayload(string Text, string Orientation);
