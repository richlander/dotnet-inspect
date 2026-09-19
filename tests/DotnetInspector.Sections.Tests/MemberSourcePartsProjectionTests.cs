using CSharpText;

namespace DotnetInspector.Sections.Tests;

// PR-fast: catalog lowering over one producer-issued lexical declaration.
public class MemberSourcePartsProjectionTests
{
    [Fact]
    public void CatalogRetainsDiscontiguousOriginalSpansAndStableNames()
    {
        const string text = """
            class Counter
            {
                /// <summary>Increment.</summary>
                // Commentary separates documentation fragments.
                /** <remarks>Original text.</remarks> */
                [First][Second]
                public int Add(int value) => value + 1;
            }
            """;
        var index = DeclarationIndex.Build(text);
        var declaration = Assert.Single(index.Declarations, item => item.Name == "Add");
        var parts = Assert.IsType<MemberTextParts>(index.GetMemberTextParts(declaration));
        var catalog = MemberSourcePartsProjection.CreateCatalog(parts);

        Assert.Equal(["member", "xml-docs", "attributes", "signature", "body"],
            catalog.Select(part => MemberSourcePartsProjection.Name(part.Kind)));
        Assert.Equal(parts.XmlDocumentation, catalog[1].Spans);
        Assert.Equal(2, catalog[1].Spans.Length);
        Assert.Equal(parts.Attributes, catalog[2].Spans);
        Assert.Equal(2, catalog[2].Spans.Length);
        Assert.Equal(parts.Member, Assert.Single(catalog[0].Spans));
    }

    [Fact]
    public void BodylessCatalogOmitsAbsentParts()
    {
        var index = DeclarationIndex.Build("interface I { void Run(); }");
        var declaration = Assert.Single(index.Declarations, item => item.Name == "Run");
        var parts = Assert.IsType<MemberTextParts>(index.GetMemberTextParts(declaration));

        Assert.Equal([MemberSourcePartKind.Member, MemberSourcePartKind.Signature],
            MemberSourcePartsProjection.CreateCatalog(parts).Select(part => part.Kind));
        Assert.True(MemberSourcePartsProjection.TryParse("XML-DOCS", out var kind));
        Assert.Equal(MemberSourcePartKind.XmlDocs, kind);
        Assert.False(MemberSourcePartsProjection.TryParse("unknown", out _));
    }
}
