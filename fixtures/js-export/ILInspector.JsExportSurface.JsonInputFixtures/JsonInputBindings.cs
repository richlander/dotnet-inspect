using TsJsExport;

namespace ILInspector.JsExportSurface.JsonInputFixtures;

[JsExportJsonInput(
    nameof(JsonInputExports.RenameWidget),
    "widgetJson",
    typeof(JsonInputWidget))]
[JsExportJsonOutput(
    nameof(JsonInputExports.RenameWidget),
    typeof(JsonInputWidget))]
[JsExportJsonInput(
    nameof(JsonInputExports.RenameNormalizedWidget),
    "widgetJson",
    typeof(JsonInputWidget))]
[JsExportJsonOutput(
    nameof(JsonInputExports.RenameNormalizedWidget),
    typeof(JsonInputWidget))]
[JsExportJsonOutput(
    nameof(JsonInputExports.CreateWidget),
    typeof(JsonInputWidget),
    deferParsing: true)]
[JsExportJsonOutput(
    nameof(JsonInputExports.CreateNullableWidgetParsed),
    typeof(JsonInputWidget))]
[JsExportJsonOutput(
    nameof(JsonInputExports.CreateNullableWidgetParsedAsync),
    typeof(JsonInputWidget))]
[JsExportJsonOutput(
    nameof(JsonInputExports.CreateNullableWidgetDeferred),
    typeof(JsonInputWidget),
    deferParsing: true)]
[JsExportJsonOutput(
    nameof(JsonInputExports.CreateNullableWidgetDeferredAsync),
    typeof(JsonInputWidget),
    deferParsing: true)]
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
