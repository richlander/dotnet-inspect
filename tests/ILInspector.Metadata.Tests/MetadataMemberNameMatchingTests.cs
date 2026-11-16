using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;

namespace ILInspector.Metadata.Tests;

public sealed class MetadataMemberNameMatchingTests
{
    [Theory]
    [InlineData("Concat", "Concat", true)]
    [InlineData("Concat", "concat", true)]
    [InlineData("Concat", "Con*", false)]
    [InlineData("Item", "this[]", true)]
    [InlineData("Chars", "THIS[]", true)]
    [InlineData("Other", "this[]", false)]
    [InlineData("this[]", "this[]", false)]
    [InlineData(".ctor", ".CTOR", true)]
    [InlineData("I.Foo", "i.foo", true)]
    [InlineData("ΣMethod", "σmethod", true)]
    [InlineData("İMethod", "imethod", false)]
    [InlineData("Méthode", "MÉTHODE", true)]
    [InlineData("", "", true)]
    public void HeapComparisonPreservesMemberNameSelection(string name, string query, bool expected)
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(0, metadata.GetOrAddString(name), default, default, default);
        var blob = new BlobBuilder();
        new MetadataRootBuilder(metadata).Serialize(blob, 0, 0);
        using var provider = MetadataReaderProvider.FromMetadataImage(blob.ToImmutableArray());
        MetadataReader reader = provider.GetMetadataReader();
        StringHandle handle = reader.GetModuleDefinition().Name;

        Assert.Equal(expected, TypeMatcher.MatchesMemberName(reader.GetString(handle), query));
        Assert.Equal(expected, TypeMatcher.MatchesMemberName(reader.StringComparer, handle, query));
    }
}
