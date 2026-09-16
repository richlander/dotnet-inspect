using System.Text.Json;
using System.Text.Json.Serialization;
using DotnetInspector.Sections;
using Inspector.Findings;

namespace DotnetInspector.Presentation;

[JsonSourceGenerationOptions(
    Converters = [typeof(InertStringJsonConverter)],
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(ApiCoordinateMatchContent))]
public sealed partial class ApiCoordinateMatchJsonContext : JsonSerializerContext;
