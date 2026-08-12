using System.Xml;
using CSharpText;

namespace CSharpText.Tests;

public class XmlDocTextTests
{
    [Fact]
    public void GetNodeTextWithRefs_RejectsExcessiveElementDepth()
    {
        string nested = string.Concat(Enumerable.Repeat("<b>", XmlDocText.MaxElementDepth + 1));
        string close = string.Concat(Enumerable.Repeat("</b>", XmlDocText.MaxElementDepth + 1));
        var document = new XmlDocument();
        document.LoadXml($"<summary>{nested}value{close}</summary>");

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
}
