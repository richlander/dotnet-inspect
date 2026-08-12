using System.Xml;
using CSharpText;

namespace CSharpText.Tests;

public class XmlDocTextTests
{
    [Fact]
    public void GetNodeTextWithRefs_AcceptsTheDepthLimitAndRejectsTheNextElement()
    {
        var document = new XmlDocument();
        document.LoadXml(NestedSummary(XmlDocText.MaxElementDepth));

        Assert.Equal(
            "value",
            XmlDocText.GetNodeTextWithRefs(document.DocumentElement!));

        document.LoadXml(NestedSummary(XmlDocText.MaxElementDepth + 1));

        XmlException failure = Assert.Throws<XmlException>(
            () => XmlDocText.GetNodeTextWithRefs(document.DocumentElement!));

        Assert.Contains("supported element depth", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void GetNodeTextWithRefs_PreservesReferenceAndLiteralSemantics()
    {
        var document = new XmlDocument();
        document.LoadXml(
            "<summary>Use <see cref=\"T:System.String\"/> with <paramref name=\"value\"/> "
            + "and <c>literal <paramref name=\"ignored\"/></c>; "
            + "<see langword=\"null\"/> is accepted.</summary>");

        string text = XmlDocText.GetNodeTextWithRefs(document.DocumentElement!);

        Assert.Equal("Use String with value and literal ; null is accepted.", text);
    }

    static string NestedSummary(int depth)
    {
        string nested = string.Concat(Enumerable.Repeat("<b>", depth));
        string close = string.Concat(Enumerable.Repeat("</b>", depth));
        return $"<summary>{nested}value{close}</summary>";
    }
}
