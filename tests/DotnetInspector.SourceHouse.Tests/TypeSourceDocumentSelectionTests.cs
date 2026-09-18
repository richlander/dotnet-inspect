using ILInspector.Metadata;
using ILInspector.SourceLink;

namespace DotnetInspector.SourceHouse.Tests;

public sealed class TypeSourceDocumentSelectionTests
{
    [Theory]
    [InlineData("Widget", "/_/A.cs", "/_/Widget.cs", "/_/Widget.cs")]
    [InlineData("Widget", "/_/A.cs", "/_/WIDGET.cs", "/_/WIDGET.cs")]
    [InlineData("Widget", "/_/Generated.Widget.cs", "/_/A.cs", "/_/A.cs")]
    [InlineData("Widget", "/_/Z.cs", "/_/A.cs", "/_/Z.cs")]
    [InlineData("Widget`1", "/_/Widget.g.cs", "/_/Widget.cs", "/_/Widget.cs")]
    public void SelectDefault_PreservesExistingPreferenceAndDiscoveryOrderTies(
        string typeName, string first, string second, string expected)
    {
        var type = Assert.IsType<MetadataTypeDefinitionNameResult.Valid>(
            MetadataTypeDefinitionName.Create("Example", [typeName])).Name;
        var mapping = new SourceLinkResolver.TypeSourceInfo(type,
        [
            new(first, null, null),
            new(second, null, null),
        ]);

        var selected = TypeSourceDocumentSelection.SelectDefault(mapping);

        Assert.Equal(expected, selected!.FilePath);
        Assert.Same(Assert.Single(mapping.Documents, document => document.FilePath == expected), selected);
        Assert.Equal([first, second], mapping.Documents.Select(document => document.FilePath));
    }

    [Fact]
    public void RealPartialType_ReorderedEvidenceDoesNotChangeNamedDefault()
    {
        using var source = SourceLinkService.Open(typeof(SourceLinkService).Assembly.Location);
        var mapping = Assert.IsType<SourceLinkResolver.TypeSourceInfo>(
            source.ResolveTypeSource(typeof(SourceLinkService).FullName!));
        var primary = Assert.IsType<SourceLinkResolver.TypeSourceDocument>(
            TypeSourceDocumentSelection.SelectDefault(mapping));
        var reordered = mapping with
        {
            Documents = [.. mapping.Documents.Where(document => document != primary), primary],
        };

        Assert.Equal("SourceLinkService.cs", Path.GetFileName(primary.FilePath));
        Assert.NotSame(primary, reordered.Documents[0]);
        Assert.Same(primary, TypeSourceDocumentSelection.SelectDefault(reordered));
        Assert.Same(primary, reordered.Documents[^1]);
    }
}
