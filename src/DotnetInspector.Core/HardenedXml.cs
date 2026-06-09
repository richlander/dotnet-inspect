using System.Xml;
using System.Xml.Linq;

namespace DotnetInspector.Core;

/// <summary>
/// Loads XML from untrusted package contents (.nuspec, compiler .xml docs, tool settings) with
/// DTD processing disabled. This blocks entity-expansion ("billion laughs") denial-of-service and
/// external-entity (XXE) attacks: every input we parse ships inside an attacker-controllable package.
/// </summary>
public static class HardenedXml
{
    private static readonly XmlReaderSettings Settings = new()
    {
        DtdProcessing = DtdProcessing.Prohibit,
        XmlResolver = null,
    };

    /// <summary>Loads an <see cref="XDocument"/> from a file with DTD processing prohibited.</summary>
    public static XDocument LoadXDocument(string path)
    {
        using var reader = XmlReader.Create(path, Settings);
        return XDocument.Load(reader);
    }

    /// <summary>Loads an <see cref="XmlDocument"/> from a file with DTD processing prohibited.</summary>
    public static XmlDocument LoadXmlDocument(string path)
    {
        using var reader = XmlReader.Create(path, Settings);
        var doc = new XmlDocument { XmlResolver = null };
        doc.Load(reader);
        return doc;
    }
}
