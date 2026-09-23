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
[JsExportJsonInput(
    nameof(JsonInputExports.WidgetsMatch),
    "leftJson",
    typeof(JsonInputWidget))]
[JsExportJsonInput(
    nameof(JsonInputExports.WidgetsMatch),
    "rightJson",
    typeof(JsonInputWidget))]
public static partial class JsonInputExports;
