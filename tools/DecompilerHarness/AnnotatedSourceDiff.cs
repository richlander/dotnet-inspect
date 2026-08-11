using System.Text.Json;
using System.Text.Json.Serialization;
using ILInspector.Decompiler;

namespace ILInspector.DecompilerHarness;

static class AnnotatedSourceDiff
{
    static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static int Run(string beforePath, string afterPath)
    {
        var before = Read(beforePath);
        var after = Read(afterPath);
        Console.WriteLine(AnnotatedSourceComparisonRenderer.RenderMarkdown(
            AnnotatedSourceComparer.Compare(before, after)));
        return 0;
    }

    static AnnotatedSourceDocument Read(string path)
        => JsonSerializer.Deserialize<AnnotatedSourceDocument>(
            File.ReadAllText(path),
            Options)
            ?? throw new JsonException($"Annotated-source document '{path}' contained JSON null.");
}
