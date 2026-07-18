using ILInspector.Decompiler.Pipeline;
using ILInspector.Metadata;

namespace ILInspector.Decompiler.Tests;

public class ReferencedNamespaceExtractorTests
{
    static IReadOnlySet<string> ExtractFor(string typeFullName, string methodName, int overloadIndex = 0)
    {
        using var source = MetadataSource.Open(typeof(CfgSampleClass).Assembly.Location);
        var function = IrImporter.Import(source, typeFullName, methodName, overloadIndex);
        Assert.NotNull(function);
        return ReferencedNamespaceExtractor.Extract(function);
    }

    [Fact]
    public void ReportsEveryDistinctReferencedNamespace()
    {
        // The body constructs a List<int> and a StringBuilder, so it references
        // System.Collections.Generic and System.Text in addition to System.
        var namespaces = ExtractFor(
            typeof(NamespaceRefSample).FullName!,
            nameof(NamespaceRefSample.ReferencesTextAndGenerics));

        Assert.Contains("System", namespaces);
        Assert.Contains("System.Text", namespaces);
        Assert.Contains("System.Collections.Generic", namespaces);
    }

    [Fact]
    public void OmitsUnreferencedNamespaces()
    {
        // A body that only touches Int32 reports System and nothing more specific.
        var namespaces = ExtractFor(
            typeof(NamespaceRefSample).FullName!,
            nameof(NamespaceRefSample.ReferencesOnlySystem));

        Assert.Contains("System", namespaces);
        Assert.DoesNotContain("System.Text", namespaces);
        Assert.DoesNotContain("System.Collections.Generic", namespaces);
    }

    [Fact]
    public void ResultIsOrdinalSorted()
    {
        // The usings the harness builds from this set depend on a stable ordinal
        // ordering, so the extractor must return its namespaces sorted.
        var namespaces = ExtractFor(
            typeof(NamespaceRefSample).FullName!,
            nameof(NamespaceRefSample.ReferencesTextAndGenerics));

        Assert.Equal(
            namespaces.OrderBy(ns => ns, StringComparer.Ordinal).ToArray(),
            namespaces.ToArray());
    }
}
