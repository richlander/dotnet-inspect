using System.Text.Json.Serialization;

namespace DotnetInspector.Sections;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(TypeDependencySectionResult))]
public partial class TypeDependencySectionJsonContext : JsonSerializerContext;
