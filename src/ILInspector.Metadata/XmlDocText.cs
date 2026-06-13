using System.Text;
using System.Text.RegularExpressions;
using System.Xml;

namespace ILInspector.Metadata;

/// <summary>
/// Shared plain-text extraction from XML documentation nodes: flattens see/seealso/paramref/
/// typeparamref cross-references to their simple names, unwraps inline code, and normalizes
/// whitespace. Used by both the C# source doc-comment parser (DocCommentParser) and the compiler
/// .xml doc-file parser (XmlDocFileParser) so cref rendering stays identical regardless of source.
/// </summary>
public static partial class XmlDocText
{
    /// <summary>
    /// Recursively extracts text from a node, replacing see/paramref/typeparamref elements with
    /// their referenced names.
    /// </summary>
    public static string GetNodeTextWithRefs(XmlNode node)
    {
        var sb = new StringBuilder();

        foreach (XmlNode child in node.ChildNodes)
        {
            switch (child.NodeType)
            {
                case XmlNodeType.Text:
                case XmlNodeType.Whitespace:
                case XmlNodeType.SignificantWhitespace:
                    sb.Append(child.Value);
                    break;

                case XmlNodeType.Element:
                    switch (child.Name)
                    {
                        case "see":
                        case "seealso":
                            // Extract cref="Type" or fall back to inner text / href
                            var cref = child.Attributes?["cref"]?.Value;
                            if (cref != null)
                            {
                                sb.Append(SimplifyTypeName(cref));
                            }
                            else
                            {
                                var innerText = child.InnerText;
                                if (!string.IsNullOrWhiteSpace(innerText))
                                {
                                    sb.Append(innerText);
                                }
                            }
                            break;

                        case "paramref":
                        case "typeparamref":
                            var name = child.Attributes?["name"]?.Value;
                            if (name != null)
                            {
                                sb.Append(name);
                            }
                            break;

                        case "c":
                            // Inline code - just use the text
                            sb.Append(child.InnerText);
                            break;

                        default:
                            sb.Append(GetNodeTextWithRefs(child));
                            break;
                    }
                    break;
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// Simplifies a cref value to just the type/member name.
    /// E.g., "T:System.String" -> "String", "M:Foo.Bar(System.Int32)" -> "Bar".
    /// </summary>
    public static string SimplifyTypeName(string cref)
    {
        // Remove type prefix (T:, M:, P:, F:, E:, N:)
        if (cref.Length > 2 && cref[1] == ':')
        {
            cref = cref[2..];
        }

        // Remove method parameters
        var parenIndex = cref.IndexOf('(');
        if (parenIndex > 0)
        {
            cref = cref[..parenIndex];
        }

        // Get just the last part (simple name)
        var lastDot = cref.LastIndexOf('.');
        if (lastDot >= 0)
        {
            cref = cref[(lastDot + 1)..];
        }

        // Handle generic arity (e.g., List`1 -> List)
        var backtick = cref.IndexOf('`');
        if (backtick >= 0)
        {
            cref = cref[..backtick];
        }

        return cref;
    }

    /// <summary>Collapses runs of whitespace into a single space and trims.</summary>
    public static string NormalizeWhitespace(string text) => WhitespaceRegex().Replace(text.Trim(), " ");

    /// <summary>Normalizes backslash path separators to forward slashes.</summary>
    public static string NormalizePath(string path) => path.Replace('\\', '/');

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();
}
