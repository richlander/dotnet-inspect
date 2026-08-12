using System.Text;
using System.Text.RegularExpressions;
using System.Xml;

namespace CSharpText;

/// <summary>
/// Shared plain-text extraction from XML documentation nodes: flattens see/seealso/paramref/
/// typeparamref cross-references to their simple names, unwraps inline code, and normalizes
/// whitespace. Used by both the C# source doc-comment parser (DocCommentParser) and the compiler
/// .xml doc-file parser (XmlDocFileParser) so cref rendering stays identical regardless of source.
/// </summary>
public static partial class XmlDocText
{
    /// <summary>
    /// The maximum element nesting accepted while flattening XML documentation.
    /// </summary>
    public const int MaxElementDepth = 256;

    /// <summary>
    /// Extracts text from a node, replacing see/paramref/typeparamref elements with their
    /// referenced names.
    /// </summary>
    public static string GetNodeTextWithRefs(XmlNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        using var reader = new XmlNodeReader(node);
        reader.MoveToContent();
        return GetElementTextWithRefs(reader);
    }

    /// <summary>
    /// Iteratively extracts text from the element at the reader's current position. Excessive
    /// nesting is rejected before it can exhaust the process stack.
    /// </summary>
    public static string GetElementTextWithRefs(
        XmlReader reader,
        int maxElementDepth = MaxElementDepth)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentOutOfRangeException.ThrowIfNegative(maxElementDepth);
        if (reader.NodeType != XmlNodeType.Element)
            throw new ArgumentException("The XML reader must be positioned on an element.", nameof(reader));
        if (reader.IsEmptyElement)
            return "";

        var builder = new StringBuilder();
        int rootDepth = reader.Depth;
        int? literalDepth = null;
        int? suppressedDepth = null;
        while (reader.Read())
        {
            if (reader.Depth - rootDepth > maxElementDepth)
            {
                throw new XmlException(
                    $"XML documentation exceeds the supported element depth of {maxElementDepth}.");
            }

            if (suppressedDepth is int suppressed)
            {
                if (reader.NodeType == XmlNodeType.EndElement && reader.Depth == suppressed)
                    suppressedDepth = null;
                continue;
            }

            if (literalDepth is int literal)
            {
                if (reader.NodeType == XmlNodeType.EndElement && reader.Depth == literal)
                {
                    literalDepth = null;
                }
                else if (reader.NodeType is XmlNodeType.Text
                    or XmlNodeType.CDATA
                    or XmlNodeType.Whitespace
                    or XmlNodeType.SignificantWhitespace)
                {
                    builder.Append(reader.Value);
                }
                continue;
            }

            if (reader.NodeType == XmlNodeType.EndElement && reader.Depth == rootDepth)
                break;

            if (reader.NodeType is XmlNodeType.Text
                or XmlNodeType.CDATA
                or XmlNodeType.Whitespace
                or XmlNodeType.SignificantWhitespace)
            {
                builder.Append(reader.Value);
                continue;
            }
            if (reader.NodeType != XmlNodeType.Element)
                continue;

            switch (reader.LocalName)
            {
                case "see":
                case "seealso":
                    string? cref = reader.GetAttribute("cref");
                    if (cref is not null)
                    {
                        builder.Append(SimplifyTypeName(cref));
                        if (!reader.IsEmptyElement)
                            suppressedDepth = reader.Depth;
                    }
                    else if (reader.GetAttribute("langword") is string languageKeyword)
                    {
                        builder.Append(languageKeyword);
                        if (!reader.IsEmptyElement)
                            suppressedDepth = reader.Depth;
                    }
                    else if (!reader.IsEmptyElement)
                    {
                        literalDepth = reader.Depth;
                    }
                    break;

                case "paramref":
                case "typeparamref":
                    builder.Append(reader.GetAttribute("name"));
                    if (!reader.IsEmptyElement)
                        suppressedDepth = reader.Depth;
                    break;

                case "c":
                    if (!reader.IsEmptyElement)
                        literalDepth = reader.Depth;
                    break;
            }
        }

        return builder.ToString();
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
