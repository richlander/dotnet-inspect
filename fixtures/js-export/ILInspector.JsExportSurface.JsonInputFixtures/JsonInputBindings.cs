using TsJsExport;

namespace ILInspector.JsExportSurface.JsonInputFixtures;

[JsExportJsonInput(
    nameof(JsonInputExports.RenameWidget),
    "widgetJson",
    typeof(JsonInputWidget))]
[JsExportJsonInput(
    nameof(JsonInputExports.RenameNormalizedWidget),
    "widgetJson",
    typeof(JsonInputWidget))]
[JsExportJsonInput(
    nameof(JsonInputExports.WidgetMatchesAudit),
    "widgetJson",
    typeof(JsonInputWidget))]
public static partial class JsonInputExports;
