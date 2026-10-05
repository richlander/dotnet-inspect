using System.Text;
using System.Text.Json;
using DotnetInspector.JsonSchema;
using DotnetInspector.Sections;
using ILInspector.JsExportSurface;
using ILInspector.TypeScriptGeneration;
using QuerySpace.Vocabulary;

namespace TsJsExport;

internal static class DeclaredJsonSchemaExports
{
    public const string ExportName =
        "jsonSchemaVocabularyDescriptors";

    public static IReadOnlyList<TypeScriptStaticJsonExport> Create(
        global::ILInspector.JsExportSurface.JsExportSurface surface,
        out JsonWireDeclarationPlan declarationPlan)
    {
        IReadOnlyList<
            InspectionEnvelope<JsonSchemaVocabularyDescriptor>> inspections;
        try
        {
            declarationPlan = JsonWireDeclarationPlan.Create(surface);
            var operation = new JsonSchemaVocabularyInspection(
                    surface,
                    declarationPlan,
                    JsExportContractIdentity.Api,
                    VocabularySnapshotReference.FromSnapshot(
                        TsJsExportVocabularyComposition.Snapshot));
            inspections = [
                .. operation.Requests.Select(operation.Inspect),
            ];
        }
        catch (JsonSchemaVocabularyException exception)
        {
            throw new UnsupportedWireContractException(
                exception.Location,
                exception.Reason);
        }
        catch (UnsupportedJsExportSurfaceException exception)
        {
            throw new UnsupportedWireContractException(
                exception.Location,
                exception.Reason);
        }
        if (inspections.Count == 0)
            return [];

        string json = "["
            + string.Join(
                ",",
                inspections.Select(inspection =>
                    Encoding.UTF8.GetString(
                        JsonSchemaVocabularyDescriptorBuilder
                            .SerializeCanonical(
                                inspection.Content))))
            + "]";
        using JsonDocument document = JsonDocument.Parse(json);
        return [
            new(
                ExportName,
                document.RootElement.Clone()),
        ];
    }
}
