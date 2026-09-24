using System.Text.Json.Serialization;

namespace DotnetInspector.Sections;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(InspectionEnvelope<TypeInspectionContent>))]
public partial class TypeInspectionJsonContext : JsonSerializerContext;
