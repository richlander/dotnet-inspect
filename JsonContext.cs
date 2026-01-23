using System.Text.Json.Serialization;

namespace DotnetInspector;

[JsonSourceGenerationOptions(
    WriteIndented = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(InspectionResult))]
[JsonSerializable(typeof(RidPackageReference))]
public partial class JsonContext : JsonSerializerContext
{
}
