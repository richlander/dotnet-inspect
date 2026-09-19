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
        Assert.Equal(
            "    /// <summary>Increment.</summary>\n    /** <remarks>Original text.</remarks> */",
            MemberSourcePartsProjection.GetDisplayText(text, catalog[1]));
        Assert.Equal(
            "    [First]\n    [Second]",
            MemberSourcePartsProjection.GetDisplayText(text, catalog[2]));
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

    [Theory]
    [InlineData("\n", "    ")]
    [InlineData("\r\n", "\t")]
    [InlineData("\r", "\t  ")]
    [InlineData("\u0085", "  ")]
    [InlineData("\u2028", "    ")]
    [InlineData("\u2029", "\t")]
    public void MarkoutHeadingDocumentationRetainsEveryLinesIndentation(
        string newline,
        string indentation)
    {
        string[] lines =
        [
            "class MarkoutWriter",
            "{",
            indentation + "/// <summary>",
            indentation + "/// Writes a heading at the specified level.",
            indentation + "/// </summary>",
            indentation + "public bool WriteHeading(int level, string text) => WriteHeading(level, text, null);",
            "}",
        ];
        string text = string.Join(newline, lines);
        var index = DeclarationIndex.Build(text);
        var declaration = Assert.Single(index.Declarations, item => item.Name == "WriteHeading");
        var parts = Assert.IsType<MemberTextParts>(index.GetMemberTextParts(declaration));
        var catalog = MemberSourcePartsProjection.CreateCatalog(parts);

        Assert.Equal(
            string.Join(newline, lines[2..5]),
            MemberSourcePartsProjection.GetDisplayText(text, catalog[1]));
        Assert.Equal(
            string.Join(newline, lines[2..6]),
            MemberSourcePartsProjection.GetDisplayText(text, catalog[0]));
        Assert.StartsWith("///", text.Substring(parts.XmlDocumentation[0].Start,
            parts.XmlDocumentation[0].Length));
        Assert.Equal(
            indentation + "=> WriteHeading(level, text, null);",
            MemberSourcePartsProjection.GetDisplayText(text, catalog[^1]));
    }

    [Fact]
    public void DisplayIndentationDoesNotRewriteMultilineLiteralContents()
    {
        const string text = "class Message\n{\n    public string Text() => @\"first\n  second\";\n}";
        var index = DeclarationIndex.Build(text);
        var declaration = Assert.Single(index.Declarations, item => item.Name == "Text");
        var parts = Assert.IsType<MemberTextParts>(index.GetMemberTextParts(declaration));
        var body = MemberSourcePartsProjection.CreateCatalog(parts)[^1];

        Assert.Equal("    => @\"first\n  second\";",
            MemberSourcePartsProjection.GetDisplayText(text, body));
    }
}
