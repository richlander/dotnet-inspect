using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
namespace ILInspector.Decompiler.Annotations;

/// <summary>
/// The third, agent-facing projection of the one annotation set: the same facts
/// the C# and IL views render for humans, serialized as JSON or normalized TSV.
/// Humans and agents are co-equal audiences, so this shares the engine and the
/// <see cref="AnnotationText"/>-adjacent vocabulary rather than re-deriving facts.
/// Each row carries the IL offset, so an agent can cross-reference a fact to the
/// exact opcode in the IL view or the anchored statement in the C# view.
///
/// AOT-safe: JSON is written field by field with <see cref="Utf8JsonWriter"/>
/// (no reflection serializer), and TSV escapes tabs and newlines so every row is
/// a single line with stable columns.
/// </summary>
public static class AnnotationStructuredView
{
    public static string Json(IReadOnlyList<Annotation> annotations)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions
        {
            Indented = true,
            // CLI output, not HTML: keep generic brackets and ampersands literal
            // (Func<int, int>, not Func\u003Cint, int\u003E) so the rows read.
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        }))
        {
            writer.WriteStartArray();
            foreach (var annotation in annotations)
            {
                writer.WriteStartObject();
                writer.WriteString("id", annotation.Descriptor.Id);
                writer.WriteString("category", annotation.Descriptor.Category.ToString());
                writer.WriteString("title", annotation.Descriptor.Title);
                writer.WriteString("conditionality", annotation.Conditionality.ToString());
                writer.WriteNumber("offset", annotation.SourceOffset);
                writer.WriteString("il", Offset(annotation.SourceOffset));
                if (annotation.Detail is { } detail)
                    writer.WriteString("detail", detail);
                else
                    writer.WriteNull("detail");
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
        }
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    public static string Tsv(IReadOnlyList<Annotation> annotations)
    {
        var sb = new StringBuilder();
        sb.Append("id\tcategory\tconditionality\toffset\tdetail\n");
        foreach (var annotation in annotations)
        {
            sb.Append(Field(annotation.Descriptor.Id)).Append('\t')
                .Append(Field(annotation.Descriptor.Category.ToString())).Append('\t')
                .Append(Field(annotation.Conditionality.ToString())).Append('\t')
                .Append(Field(Offset(annotation.SourceOffset))).Append('\t')
                .Append(Field(annotation.Detail ?? "")).Append('\n');
        }
        return sb.ToString();
    }

    static string Offset(int offset) => offset < 0 ? "" : $"IL_{offset:X4}";

    /// <summary>Normalizes a field to a single TSV cell: tabs and newlines escaped, never raw, so rows stay one-per-line with stable columns.</summary>
    static string Field(string value)
        => value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\t", "\\t", StringComparison.Ordinal)
            .Replace("\r", "\\r", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal);
}
