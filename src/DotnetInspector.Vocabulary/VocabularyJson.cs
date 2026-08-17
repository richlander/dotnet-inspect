using System.Text;
using System.Text.Json;

namespace DotnetInspector.Vocabulary;

/// <summary>NativeAOT-safe JSON projection for vocabulary documents.</summary>
public static class VocabularyJson
{
    /// <summary>Serializes the selected vocabulary sections as one structured document.</summary>
    public static string Serialize(
        VocabularyDocument document,
        IEnumerable<VocabularySection>? sections = null,
        bool indented = true)
    {
        ArgumentNullException.ThrowIfNull(document);
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(
            stream,
            new JsonWriterOptions { Indented = indented }))
        {
            Write(writer, document, sections ?? document.Sections);
        }
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    /// <summary>Writes the selected vocabulary sections as one structured document.</summary>
    public static void Write(
        Utf8JsonWriter writer,
        VocabularyDocument document,
        IEnumerable<VocabularySection> sections)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(sections);

        writer.WriteStartObject();
        writer.WriteNumber("schema_version", document.SchemaVersion);
        writer.WriteStartArray("sections");
        foreach (VocabularySection section in sections)
        {
            writer.WriteStartObject();
            writer.WriteString("id", section.Id);
            writer.WriteString("name", section.Name);
            writer.WriteString("summary", section.Summary);
            WriteStrings(writer, "categories", section.Categories);
            WriteStrings(writer, "accepted_by", section.AcceptedBy);

            writer.WriteStartArray("fields");
            foreach (VocabularyField field in section.Fields)
            {
                writer.WriteStartObject();
                writer.WriteString("id", field.Id);
                writer.WriteString("label", field.Label);
                writer.WriteString("summary", field.Summary);
                writer.WriteString("type", Name(field.Kind));
                writer.WriteStartArray("operators");
                foreach (VocabularyOperator value in field.Operators)
                    writer.WriteStringValue(Name(value));
                writer.WriteEndArray();
                writer.WriteEndObject();
            }
            writer.WriteEndArray();

            writer.WriteStartArray("values");
            foreach (VocabularyRow row in section.Values)
            {
                writer.WriteStartObject();
                foreach (VocabularyField field in section.Fields)
                {
                    if (!row.TryGetValue(field.Id, out VocabularyValue value))
                        continue;
                    writer.WritePropertyName(field.Id);
                    WriteValue(writer, value);
                }
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteEndObject();
        }
        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    private static void WriteValue(Utf8JsonWriter writer, VocabularyValue value)
    {
        switch (value.Kind)
        {
            case VocabularyValueKind.Text:
                writer.WriteStringValue(value.Text);
                break;
            case VocabularyValueKind.Integer:
                writer.WriteNumberValue(value.Integer);
                break;
            case VocabularyValueKind.Boolean:
                writer.WriteBooleanValue(value.Boolean);
                break;
            case VocabularyValueKind.TextList:
                writer.WriteStartArray();
                foreach (string item in value.TextList)
                    writer.WriteStringValue(item);
                writer.WriteEndArray();
                break;
            default:
                throw new InvalidOperationException($"Unsupported vocabulary value kind '{value.Kind}'.");
        }
    }

    private static void WriteStrings(
        Utf8JsonWriter writer,
        string property,
        IEnumerable<string> values)
    {
        writer.WriteStartArray(property);
        foreach (string value in values)
            writer.WriteStringValue(value);
        writer.WriteEndArray();
    }

    private static string Name(VocabularyValueKind value) => value switch
    {
        VocabularyValueKind.Text => "text",
        VocabularyValueKind.Integer => "integer",
        VocabularyValueKind.Boolean => "boolean",
        VocabularyValueKind.TextList => "text-list",
        _ => throw new InvalidOperationException($"Unsupported vocabulary value kind '{value}'."),
    };

    private static string Name(VocabularyOperator value) => value switch
    {
        VocabularyOperator.Equals => "equals",
        VocabularyOperator.NotEquals => "not-equals",
        VocabularyOperator.In => "in",
        VocabularyOperator.LessThan => "less-than",
        VocabularyOperator.GreaterThan => "greater-than",
        VocabularyOperator.Glob => "glob",
        VocabularyOperator.Contains => "contains",
        _ => throw new InvalidOperationException($"Unsupported vocabulary operator '{value}'."),
    };
}
