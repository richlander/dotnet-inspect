using System.Runtime.Versioning;
using System.Xml;
using CSharpText;

namespace InspectWeb.Engine.PackageFacade;

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
        while (reader.Read())
        {
            if (reader.NodeType == XmlNodeType.Element
                && reader.LocalName == "member"
                && reader.GetAttribute("name") == documentationId)
            {
                return ReadMember(reader);
            }
        }
        return Empty;
    }

    static BrowserMemberDocumentation ReadMember(XmlReader reader)
    {
        var parameters = new Dictionary<string, string>(StringComparer.Ordinal);
        var exceptions = new List<BrowserExceptionSurface>();
        string? summary = null;
        string? returns = null;
        int memberDepth = reader.Depth;
        if (reader.IsEmptyElement)
            return Empty;

        while (reader.Read())
        {
            if (reader.NodeType == XmlNodeType.EndElement && reader.Depth == memberDepth)
                break;
            if (reader.NodeType != XmlNodeType.Element || reader.Depth != memberDepth + 1)
                continue;

            switch (reader.LocalName)
            {
                case "summary":
                    string summaryText = Text(reader);
                    summary ??= summaryText;
                    break;

                case "returns":
                    string returnsText = Text(reader);
                    returns ??= returnsText;
                    break;

                case "param":
                    string? name = reader.GetAttribute("name");
                    string parameterText = Text(reader);
                    if (name is not null)
                        parameters[name] = parameterText;
                    break;

                case "exception":
                    string exceptionType = Reference(reader.GetAttribute("cref"));
                    exceptions.Add(new BrowserExceptionSurface(exceptionType, Text(reader)));
                    break;
            }
        }

        return new BrowserMemberDocumentation(
            summary,
            returns,
            parameters,
            [.. exceptions]);
    }

    public static BrowserMemberDocumentation Empty { get; } =
        new(null, null, new Dictionary<string, string>(StringComparer.Ordinal), []);

    static string Text(XmlReader reader) =>
        XmlDocText.NormalizeWhitespace(XmlDocText.GetElementTextWithRefs(reader));

    static string Reference(string? reference)
    {
        if (string.IsNullOrWhiteSpace(reference))
            return "";
        string value = reference.Length > 2 && reference[1] == ':' ? reference[2..] : reference;
        return value.Replace('#', '.');
    }
}
