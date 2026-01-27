using System.Text;
using System.Text.RegularExpressions;
using System.Xml;

namespace DotnetInspector.Inspectors;

/// <summary>
/// Parses XML doc comments (///) from C# source files.
/// </summary>
public class DocCommentParser
{
    public record DocComment(
        string? Summary,
        string? Remarks,
        Dictionary<string, string>? Parameters,
        string? Returns
    );

    /// <summary>
    /// Extracts doc comment for a type declaration.
    /// </summary>
    public DocComment? ExtractTypeDocComment(string sourceContent, string typeName)
    {
        // Handle generic types: List`1 -> List
        var backtickIndex = typeName.IndexOf('`');
        var cleanName = backtickIndex >= 0 ? typeName[..backtickIndex] : typeName;

        // Find type declaration patterns:
        // class Foo, struct Foo, interface IFoo, enum Foo, record Foo
        // Allow any combination of modifiers before the type keyword
        var modifiers = @"(?:(?:public|internal|private|protected|static|partial|abstract|sealed|readonly|unsafe|new|file)\s+)*";
        var typeKeywords = @"(?:class|struct|interface|enum|record(?:\s+struct)?(?:\s+class)?)";

        var pattern = $@"((?:^\s*///.*$\s*)+)^\s*{modifiers}{typeKeywords}\s+{Regex.Escape(cleanName)}(?:<[^>]+>)?(?:\s|:|$|\{{)";

        var match = Regex.Match(sourceContent, pattern, RegexOptions.Multiline);
        if (match.Success && match.Groups.Count > 1)
        {
            string commentBlock = match.Groups[1].Value;
            return ParseXmlDocComment(commentBlock);
        }

        return null;
    }

    /// <summary>
    /// Extracts doc comment for a member within a type.
    /// </summary>
    public DocComment? ExtractMemberDocComment(string sourceContent, string typeName, string memberName)
    {
        // Handle constructor names
        if (memberName == ".ctor")
        {
            var backtickIndex = typeName.IndexOf('`');
            memberName = backtickIndex >= 0 ? typeName[..backtickIndex] : typeName;
        }

        // Allow any combination of modifiers before member declarations
        var modifiers = @"(?:(?:public|private|protected|internal|static|virtual|override|abstract|sealed|async|readonly|new|extern|unsafe|volatile|partial)\s+)*";

        // Find member declaration patterns:
        // Method: protected abstract void Foo(...)
        // Property: public virtual int Bar { get; set; }
        // Field: private readonly int _field;
        // Event: public event EventHandler OnFoo;
        var patterns = new[]
        {
            // Method or constructor with params (return type is optional for constructors)
            $@"((?:^\s*///.*$\s*)+)^\s*(?:\[[^\]]*\]\s*)*{modifiers}(?:\S+\s+)?{Regex.Escape(memberName)}(?:<[^>]+>)?\s*\(",
            // Property with getter/setter
            $@"((?:^\s*///.*$\s*)+)^\s*(?:\[[^\]]*\]\s*)*{modifiers}(?:\S+\s+){Regex.Escape(memberName)}\s*\{{",
            // Field or auto-property with initializer
            $@"((?:^\s*///.*$\s*)+)^\s*(?:\[[^\]]*\]\s*)*{modifiers}(?:\S+\s+){Regex.Escape(memberName)}\s*[;=]",
            // Event
            $@"((?:^\s*///.*$\s*)+)^\s*(?:\[[^\]]*\]\s*)*{modifiers}event\s+\S+\s+{Regex.Escape(memberName)}\s*;",
        };

        foreach (var pattern in patterns)
        {
            var match = Regex.Match(sourceContent, pattern, RegexOptions.Multiline);
            if (match.Success && match.Groups.Count > 1)
            {
                string commentBlock = match.Groups[1].Value;
                return ParseXmlDocComment(commentBlock);
            }
        }

        return null;
    }

    /// <summary>
    /// Parses a block of /// comments into structured DocComment.
    /// </summary>
    private DocComment? ParseXmlDocComment(string commentBlock)
    {
        // Extract just the content after /// on each line
        var lines = commentBlock.Split('\n')
            .Select(line =>
            {
                var trimmed = line.Trim();
                if (trimmed.StartsWith("///"))
                {
                    return trimmed[3..].TrimStart();
                }
                return null;
            })
            .Where(line => line != null)
            .ToList();

        if (lines.Count == 0)
            return null;

        // Reconstruct XML content
        string xmlContent = string.Join("\n", lines);

        // Wrap in a root element for parsing
        xmlContent = $"<doc>{xmlContent}</doc>";

        try
        {
            var doc = new XmlDocument();
            doc.LoadXml(xmlContent);

            string? summary = GetElementText(doc, "//summary");
            string? remarks = GetElementText(doc, "//remarks");
            string? returns = GetElementText(doc, "//returns");

            Dictionary<string, string>? parameters = null;
            var paramNodes = doc.SelectNodes("//param");
            if (paramNodes != null && paramNodes.Count > 0)
            {
                parameters = new Dictionary<string, string>();
                foreach (XmlNode node in paramNodes)
                {
                    string? name = node.Attributes?["name"]?.Value;
                    if (name != null)
                    {
                        parameters[name] = NormalizeWhitespace(GetNodeTextWithRefs(node));
                    }
                }
            }

            if (summary == null && remarks == null && returns == null && parameters == null)
                return null;

            return new DocComment(summary, remarks, parameters, returns);
        }
        catch
        {
            // If XML parsing fails, try to extract summary as plain text
            string plainText = string.Join(" ", lines);
            plainText = NormalizeWhitespace(plainText);
            if (!string.IsNullOrWhiteSpace(plainText))
            {
                return new DocComment(plainText, null, null, null);
            }
            return null;
        }
    }

    private static string? GetElementText(XmlDocument doc, string xpath)
    {
        var node = doc.SelectSingleNode(xpath);
        if (node == null)
            return null;

        string text = GetNodeTextWithRefs(node);
        text = NormalizeWhitespace(text);
        return string.IsNullOrWhiteSpace(text) ? null : text;
    }

    /// <summary>
    /// Recursively extracts text from a node, replacing see/paramref/typeparamref
    /// elements with their referenced names.
    /// </summary>
    private static string GetNodeTextWithRefs(XmlNode node)
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
                            // Extract cref="Type" or href="url"
                            var cref = child.Attributes?["cref"]?.Value;
                            if (cref != null)
                            {
                                sb.Append(SimplifyTypeName(cref));
                            }
                            else
                            {
                                // Might have inner text or href
                                var innerText = child.InnerText;
                                if (!string.IsNullOrWhiteSpace(innerText))
                                {
                                    sb.Append(innerText);
                                }
                            }
                            break;

                        case "paramref":
                        case "typeparamref":
                            // Extract name="paramName"
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
                            // Recursively process other elements
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
    /// E.g., "T:System.String" -> "String", "M:Foo.Bar(System.Int32)" -> "Bar"
    /// </summary>
    private static string SimplifyTypeName(string cref)
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

    private static string NormalizeWhitespace(string text)
    {
        // Collapse multiple whitespace into single space
        return Regex.Replace(text.Trim(), @"\s+", " ");
    }
}
