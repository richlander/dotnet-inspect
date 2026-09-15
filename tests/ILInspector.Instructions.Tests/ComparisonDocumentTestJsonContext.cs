using System.Text.Json.Serialization;

namespace ILInspector.Instructions.Tests;

[JsonSourceGenerationOptions(
    MaxDepth = 128,
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower)]
[JsonSerializable(typeof(ComparisonDocumentTestPayload))]
[JsonSerializable(typeof(ComparisonDocumentNestedPayload))]
internal partial class ComparisonDocumentTestJsonContext : JsonSerializerContext;

internal sealed record ComparisonDocumentTestPayload(string Text, string Orientation);

internal sealed record ComparisonDocumentNestedPayload(
    int Depth,
    ComparisonDocumentNestedPayload? Child);
