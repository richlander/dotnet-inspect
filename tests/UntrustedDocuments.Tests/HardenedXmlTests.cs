using System.Text;
using System.Xml;

namespace UntrustedDocuments.Tests;

public class HardenedXmlTests
{
    [Fact]
    public void ParseXDocument_RejectsDtd()
    {
        const string xml = """
            <!DOCTYPE root [<!ENTITY value "expanded">]>
            <root>&value;</root>
            """;

        Assert.Throws<XmlException>(() => HardenedXml.ParseXDocument(xml));
    }

    [Fact]
    public void LoadXDocument_StreamRejectsExternalDtd()
    {
        const string xml = """
            <!DOCTYPE root SYSTEM "https://example.invalid/external.dtd">
            <root />
            """;
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(xml));

        Assert.Throws<XmlException>(() =>
            HardenedXml.LoadXDocument(stream, maxCharactersInDocument: 1_024));
    }

    [Fact]
    public void LoadXDocument_StreamEnforcesDecodedCharacterBudget()
    {
        using var stream = new MemoryStream(
            Encoding.UTF8.GetBytes("<root>0123456789</root>"));

        Assert.Throws<XmlException>(() =>
            HardenedXml.LoadXDocument(stream, maxCharactersInDocument: 8));
    }

    [Fact]
    public void FileLoadersRejectDtd()
    {
        string path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(
                path,
                """
                <!DOCTYPE root [<!ENTITY value "expanded">]>
                <root>&value;</root>
                """);

            Assert.Throws<XmlException>(() => HardenedXml.LoadXDocument(path));
            Assert.Throws<XmlException>(() => HardenedXml.LoadXmlDocument(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void LoadersAcceptWellFormedDocuments()
    {
        const string xml = "<root><value>accepted</value></root>";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(xml));
        string path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, xml);

            Assert.Equal(
                "accepted",
                HardenedXml.ParseXDocument(xml).Root?.Element("value")?.Value);
            Assert.Equal(
                "accepted",
                HardenedXml.LoadXDocument(
                    stream,
                    maxCharactersInDocument: 1_024)
                    .Root?
                    .Element("value")?
                    .Value);
            Assert.Equal(
                "accepted",
                HardenedXml.LoadXDocument(path).Root?.Element("value")?.Value);
            Assert.Equal(
                "accepted",
                HardenedXml.LoadXmlDocument(path)
                    .DocumentElement?
                    .SelectSingleNode("value")?
                    .InnerText);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
