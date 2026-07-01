using System.Text.Json;
using ILInspector.Decompiler.Annotations;
using ILInspector.Decompiler.Pipeline;
using ILInspector.Research;

namespace ILInspector.Decompiler.Tests;

public class AnnotationStructuredViewTests
{
    static IrFunction Import(string methodName)
    {
        var source = MetadataSource.Open(typeof(AllocSampleClass).Assembly.Location);
        return IrImporter.Import(source, typeof(AllocSampleClass).FullName!, methodName)!;
    }

    static IReadOnlyList<Annotation> Collect(string methodName)
    {
        var source = MetadataSource.Open(typeof(AllocSampleClass).Assembly.Location);
        return ResearchViews.CollectFacts(source, typeof(AllocSampleClass).FullName!, methodName);
    }

    [Fact]
    public void Json_CarriesTheFactWithItsOffsetForCrossReference()
    {
        var json = AnnotationStructuredView.Json(Collect(nameof(AllocSampleClass.BoxInt)));
        using var doc = JsonDocument.Parse(json);

        var fact = Assert.Single(doc.RootElement.EnumerateArray());
        Assert.Equal("alloc.box", fact.GetProperty("id").GetString());
        Assert.Equal("Allocation", fact.GetProperty("category").GetString());
        Assert.Equal("int; alloc=boxed System.Int32; path=straight-line; path-confidence=dominates-return; escape=escapes", fact.GetProperty("detail").GetString());
        Assert.Equal("IL_0001", fact.GetProperty("il").GetString());
        Assert.True(fact.GetProperty("offset").GetInt32() >= 0);
    }

    [Fact]
    public void Json_IsValidEvenWithGenericDetailContainingAngleBracketsAndCommas()
    {
        // Func<int, int> has characters a naive serializer might mangle; the
        // Utf8JsonWriter path keeps it valid.
        var json = AnnotationStructuredView.Json(Collect(nameof(AllocSampleClass.Capture)));
        using var doc = JsonDocument.Parse(json);
        Assert.Contains(doc.RootElement.EnumerateArray(),
            f => f.GetProperty("detail").GetString() == "Func<int, int>; alloc=System.Func<System.Int32, System.Int32>; path=straight-line; path-confidence=dominates-return; escape=escapes");
    }

    [Fact]
    public void Tsv_HasAHeaderAndOneLinePerFact()
    {
        var tsv = AnnotationStructuredView.Tsv(Collect(nameof(AllocSampleClass.Capture)));
        var lines = tsv.TrimEnd('\n').Split('\n');

        Assert.Equal("id\tcategory\tconditionality\toffset\tdetail", lines[0]);
        Assert.Equal(2, lines.Length - 1);  // closure + delegate
        Assert.All(lines, l => Assert.Equal(4, l.Count(c => c == '\t')));
    }

    [Fact]
    public void Tsv_FieldsNeverContainRawTabsOrNewlines()
    {
        // Every data row must stay a single line with exactly five columns.
        var tsv = AnnotationStructuredView.Tsv(Collect(nameof(AllocSampleClass.Cached)));
        foreach (var line in tsv.TrimEnd('\n').Split('\n').Skip(1))
            Assert.Equal(4, line.Count(c => c == '\t'));
    }

    [Fact]
    public void Collect_OrdersByOffset()
    {
        var facts = Collect(nameof(AllocSampleClass.Capture));
        var offsets = facts.Select(f => f.SourceOffset).ToList();
        Assert.Equal(offsets.OrderBy(o => o), offsets);
    }
}
