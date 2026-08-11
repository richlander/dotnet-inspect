using System.Runtime.Versioning;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace InspectWeb.Engine;

/// <summary>
/// Reads one member's entry from a package-shipped XML documentation file. The file is untrusted
/// feed content, so it is parsed with DTD processing prohibited to block entity-expansion and
/// external-entity attacks.
/// </summary>
[SupportedOSPlatform("browser")]
internal static class BrowserXmlDocumentation
{
    public static BrowserMemberDocumentation Read(byte[] xml, string documentationId)
    {
        ArgumentNullException.ThrowIfNull(xml);
        ArgumentException.ThrowIfNullOrWhiteSpace(documentationId);

        using var stream = new MemoryStream(xml, writable: false);
        using XmlReader reader = XmlReader.Create(
            stream,
            new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
        XElement? element = XDocument.Load(reader, LoadOptions.None)
            .Descendants("member")
            .FirstOrDefault(candidate =>
                candidate.Attribute("name")?.Value == documentationId);
        if (element is null)
            return Empty;

        var parameters = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (XElement parameter in element.Elements("param"))
        {
            string? name = parameter.Attribute("name")?.Value;
            if (name is not null)
                parameters[name] = Text(parameter) ?? "";
        }

        return new BrowserMemberDocumentation(
            Text(element.Element("summary")),
            Text(element.Element("returns")),
            parameters,
            [
                .. element.Elements("exception").Select(exception => new BrowserExceptionSurface(
                    Reference(exception.Attribute("cref")?.Value),
                    Text(exception) ?? "")),
            ]);
    }

    public static BrowserMemberDocumentation Empty { get; } =
        new(null, null, new Dictionary<string, string>(StringComparer.Ordinal), []);

    static string? Text(XElement? element)
    {
        if (element is null)
            return null;

        var builder = new StringBuilder();
        foreach (XNode node in element.Nodes())
            Append(builder, node);
        return string.Join(
            " ",
            builder.ToString().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }

    static void Append(StringBuilder builder, XNode node)
    {
        if (node is XText text)
        {
            builder.Append(text.Value);
            return;
        }

        if (node is not XElement element)
            return;

        builder.Append(element.Name.LocalName switch
        {
            "see" => element.Attribute("langword")?.Value
                ?? Reference(element.Attribute("cref")?.Value),
            "paramref" or "typeparamref" => element.Attribute("name")?.Value,
            _ => null,
        });
        foreach (XNode child in element.Nodes())
            Append(builder, child);
    }

    static string Reference(string? reference)
    {
        if (string.IsNullOrWhiteSpace(reference))
            return "";
        string value = reference.Length > 2 && reference[1] == ':' ? reference[2..] : reference;
        return value.Replace('#', '.');
    }
}
