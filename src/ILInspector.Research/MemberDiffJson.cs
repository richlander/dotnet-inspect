using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ILInspector.Research;

/// <summary>
/// The agent JSON of Member diff documents: every line's kind, each side's
/// enclosure, the carets, and the notes.
/// </summary>
public static class MemberDiffJson
{
    // Declarations are full of quotes, angle brackets, and apostrophes; this
    // JSON is read as text, not embedded in HTML, so they stay unescaped.
    static readonly MemberDiffJsonContext Context = new(
        new JsonSerializerOptions(MemberDiffJsonContext.Default.Options)
        {
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        });

    public static string Serialize(MemberDiffDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return JsonSerializer.Serialize(document, Context.MemberDiffDocument);
    }

    public static string Serialize(IReadOnlyList<MemberDiffDocument> documents)
    {
        ArgumentNullException.ThrowIfNull(documents);
        return JsonSerializer.Serialize(documents, Context.IReadOnlyListMemberDiffDocument);
    }
}

[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(MemberDiffDocument))]
[JsonSerializable(typeof(IReadOnlyList<MemberDiffDocument>))]
public partial class MemberDiffJsonContext : JsonSerializerContext;
