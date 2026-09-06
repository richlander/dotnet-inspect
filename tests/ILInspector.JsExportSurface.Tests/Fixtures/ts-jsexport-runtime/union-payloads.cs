#:project ../../../../fixtures/js-export/ILInspector.JsExportSurface.TypeScriptFixtures/ILInspector.JsExportSurface.TypeScriptFixtures.csproj
#:property NoWarn=CA1416

// Executes the compiled TypeScript fixture's exports so the runtime probe
// asserts against real source-generated System.Text.Json union payloads.

using System.Text.Json;
using ILInspector.JsExportSurface.TypeScriptFixtures;

if (args is not [string outputPath])
{
    throw new ArgumentException(
        "Usage: dotnet run union-payloads.cs -- <output.json>");
}

(string Name, string Payload)[] payloads =
[
    ("widgetSelectionDto", TypeScriptFixtureExports.GetWidgetSelection(true)),
    ("widgetSelectionString", TypeScriptFixtureExports.GetWidgetSelection(false)),
    ("defaultSelection", TypeScriptFixtureExports.GetDefaultSelection()),
    ("flagSelectionTrue", TypeScriptFixtureExports.GetFlagSelection(true)),
    ("flagSelectionWidget", TypeScriptFixtureExports.GetFlagSelection(false)),
    ("outcomeNested", TypeScriptFixtureExports.GetOutcomeSelection(true)),
    ("outcomeBoolean", TypeScriptFixtureExports.GetOutcomeSelection(false)),
    ("kindDeclared", TypeScriptFixtureExports.GetKindSelection(true)),
    ("kindString", TypeScriptFixtureExports.GetKindSelection(false)),
    ("collectionArray", TypeScriptFixtureExports.GetCollectionSelection(0)),
    ("collectionMap", TypeScriptFixtureExports.GetCollectionSelection(1)),
    ("collectionNumber", TypeScriptFixtureExports.GetCollectionSelection(2)),
    ("collectionDefault", TypeScriptFixtureExports.GetCollectionSelection(3)),
    ("boxedCount", TypeScriptFixtureExports.GetBoxedCount(11)),
    ("boxedWidget", TypeScriptFixtureExports.GetBoxedWidget("boxed")),
    ("wrappedBlob", TypeScriptFixtureExports.GetWrappedBlob()),
    (
        "selectionEnvelope",
        await TypeScriptFixtureExports.GetSelectionEnvelopeAsync("envelope")),
];

using (var stream = File.Create(outputPath))
using (var writer = new Utf8JsonWriter(
    stream,
    new JsonWriterOptions { Indented = true }))
{
    writer.WriteStartObject();
    foreach ((string name, string payload) in payloads)
    {
        writer.WriteString(name, payload);
    }
    writer.WriteEndObject();
}

foreach ((string name, string payload) in payloads)
{
    Console.WriteLine($"{name}: {payload}");
}

return 0;
