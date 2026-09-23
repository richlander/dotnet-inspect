using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ILInspector.JsExportSurface.JsonInputFixtures;

[SupportedOSPlatform("browser")]
public static partial class JsonInputExports
{
    [JSExport]
    public static string RenameWidget(
        string widgetJson,
        string newName)
    {
        JsonInputWidget widget = JsonSerializer.Deserialize(
            widgetJson,
            JsonInputJsonContext.Default.JsonInputWidget)!;
        return JsonSerializer.Serialize(
            widget with { Name = newName },
            JsonInputJsonContext.Default.JsonInputWidget);
    }

    [JSExport]
    public static string RenameNormalizedWidget(
        string widgetJson,
        string newName)
    {
        JsonInputWidget widget = JsonSerializer.Deserialize(
            widgetJson.Trim(),
            JsonInputJsonContext.Default.JsonInputWidget)!;
        return JsonSerializer.Serialize(
            widget with { Name = newName },
            JsonInputJsonContext.Default.JsonInputWidget);
    }

    [JSExport]
    public static bool WidgetMatchesAudit(
        string widgetJson,
        string auditJson)
    {
        JsonInputWidget widget = JsonSerializer.Deserialize(
            widgetJson,
            JsonInputJsonContext.Default.JsonInputWidget)!;
        JsonInputAudit audit = JsonSerializer.Deserialize(
            auditJson,
            JsonInputJsonContext.Default.JsonInputAudit)!;
        return widget.Name == audit.Name;
    }

    [JSExport]
    public static byte[] EchoBytes(byte[] value) => value;

    [JSExport]
    public static string RenameAmbiguous(string widgetJson) =>
        widgetJson;

    [JSExport]
    public static string RenameAmbiguous(
        string widgetJson,
        string suffix) =>
        widgetJson + suffix;
}

public sealed record JsonInputWidget(string Name);

public sealed record JsonInputAudit(string Name);

[JsonSerializable(typeof(JsonInputWidget))]
[JsonSerializable(typeof(JsonInputAudit))]
public sealed partial class JsonInputJsonContext : JsonSerializerContext;
