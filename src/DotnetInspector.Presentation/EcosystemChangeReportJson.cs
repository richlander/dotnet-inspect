using System.Text.Json;
using System.Text.Json.Serialization;

namespace DotnetInspector.Presentation;

/// <summary>NativeAOT-safe structured serialization for ecosystem reports.</summary>
public static class EcosystemChangeReportJson
{
    public static string Serialize(
        EcosystemChangeReportDocument document,
        bool compact = false)
    {
        ArgumentNullException.ThrowIfNull(document);
        return compact
            ? JsonSerializer.Serialize(
                document,
                EcosystemChangeReportCompactJsonContext.Default
                    .EcosystemChangeReportDocument)
            : JsonSerializer.Serialize(
                document,
                EcosystemChangeReportJsonContext.Default
                    .EcosystemChangeReportDocument);
    }

    public static EcosystemChangeReportDocument Deserialize(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        EcosystemChangeReportDocument? document =
            JsonSerializer.Deserialize(
                json,
                EcosystemChangeReportJsonContext.Default
                    .EcosystemChangeReportDocument);
        if (document is null)
            throw new JsonException("The ecosystem report document was null.");
        if (document.SchemaVersion
            != EcosystemChangeReportDocument.CurrentSchemaVersion)
        {
            throw new JsonException(
                $"Unsupported ecosystem report schema version {document.SchemaVersion}.");
        }
        return document;
    }
}

[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(EcosystemChangeReportDocument))]
public partial class EcosystemChangeReportJsonContext
    : JsonSerializerContext;

[JsonSourceGenerationOptions(
    WriteIndented = false,
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(EcosystemChangeReportDocument))]
public partial class EcosystemChangeReportCompactJsonContext
    : JsonSerializerContext;
