using System.Text.Json;
using DotnetInspector.Vocabulary;

namespace InspectWeb.Engine.CatalogFacade;

/// <summary>
/// Maps the product-owned <see cref="VocabularyWireDocument"/> to the browser-local
/// <see cref="BrowserVocabularyDocument"/> shape, verbatim. The mapping exists solely so
/// TypeScript facade JSON-wire-contract discovery — which only walks types physically defined in
/// <c>InspectWeb.Engine</c> — can generate a real TypeScript interface for the vocabulary document
/// instead of collapsing it to <c>unknown</c>; see <see cref="BrowserVocabularyField"/>'s remarks.
/// </summary>
internal static class BrowserVocabulary
{
    internal static BrowserVocabularyDocument ToBrowserDocument(VocabularyWireDocument document) =>
        new(
            document.SchemaVersion,
            [.. document.Sections.Select(ToBrowserSection)]);

    private static BrowserVocabularySection ToBrowserSection(VocabularyWireSection section) =>
        new(
            section.Id,
            section.Name,
            section.Summary,
            section.Categories,
            section.AcceptedBy,
            [.. section.Fields.Select(ToBrowserField)],
            [.. section.Values.Select(ToBrowserRow)]);

    private static BrowserVocabularyField ToBrowserField(VocabularyWireField field) =>
        new(
            field.Id,
            field.Label,
            field.Summary,
            field.Kind,
            field.Operators);

    private static JsonElement ToBrowserRow(Dictionary<string, System.Text.Json.Nodes.JsonNode?> row) =>
        JsonSerializer.SerializeToElement(row, VocabularyWireJsonContext.Default.Options.GetTypeInfo(
            typeof(Dictionary<string, System.Text.Json.Nodes.JsonNode?>)));
}
