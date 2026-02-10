using System.Buffers;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;

namespace DotnetInspector.Metadata;

/// <summary>
/// Parses XML doc comments (///) from C# source files.
/// Uses string-based search with compiled regex fallback for performance.
/// </summary>
public partial class DocCommentParser
{
    // Source-generated regex for normalizing whitespace
    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();
    
    // SearchValues for fast multi-string search of type keywords
    private static readonly SearchValues<string> TypeKeywords = 
        SearchValues.Create(["class ", "struct ", "interface ", "enum ", "record "], StringComparison.Ordinal);
    
    public record DocComment(
        string? Summary,
        string? Remarks,
        Dictionary<string, string>? Parameters,
        string? Returns,
        List<SampleReference>? Samples = null
    );

    public record SampleReference(
        string RelativePath,
        string? Description,
        string? Region
    );

    /// <summary>
    /// Extracts doc comment for a type declaration using fast string search.
    /// </summary>
    public DocComment? ExtractTypeDocComment(string sourceContent, string typeName)
    {
        // Handle generic types: List`1 -> List
        var backtickIndex = typeName.IndexOf('`');
        var cleanName = backtickIndex >= 0 ? typeName[..backtickIndex] : typeName;

        // Use SearchValues to find all type keyword positions
        var span = sourceContent.AsSpan();
        var searchStart = 0;
        
        while (searchStart < span.Length)
        {
            var idx = span[searchStart..].IndexOfAny(TypeKeywords);
            if (idx < 0) break;
            
            idx += searchStart; // Adjust to absolute position
            
            // Find which keyword matched and get its length
            int keywordLen = 0;
            if (span[idx..].StartsWith("interface ")) keywordLen = 10;
            else if (span[idx..].StartsWith("struct ")) keywordLen = 7;
            else if (span[idx..].StartsWith("record ")) keywordLen = 7;
            else if (span[idx..].StartsWith("class ")) keywordLen = 6;
            else if (span[idx..].StartsWith("enum ")) keywordLen = 5;
            
            if (keywordLen == 0) { searchStart = idx + 1; continue; }
            
            // Check if type name follows the keyword
            var afterKeyword = idx + keywordLen;
            if (afterKeyword + cleanName.Length <= span.Length &&
                span.Slice(afterKeyword, cleanName.Length).SequenceEqual(cleanName.AsSpan()))
            {
                // Verify word boundary
                var endIdx = afterKeyword + cleanName.Length;
                if (endIdx >= span.Length || (!char.IsLetterOrDigit(span[endIdx]) && span[endIdx] != '_'))
                {
                    // Found type declaration, search backwards for /// comments
                    var commentBlock = ExtractPrecedingDocComment(sourceContent, idx);
                    if (commentBlock != null)
                    {
                        return ParseXmlDocComment(commentBlock);
                    }
                }
            }
            
            searchStart = idx + 1;
        }

        return null;
    }

    /// <summary>
    /// Extracts doc comment for a member within a type using fast string search.
    /// </summary>
    public DocComment? ExtractMemberDocComment(string sourceContent, string typeName, string memberName)
    {
        // Handle constructor names
        if (memberName == ".ctor")
        {
            var backtickIndex = typeName.IndexOf('`');
            memberName = backtickIndex >= 0 ? typeName[..backtickIndex] : typeName;
        }

        // Fast path: find member by name with common patterns
        var patterns = new[]
        {
            memberName + "(",   // Method/constructor
            memberName + "<",   // Generic method
            memberName + " {",  // Property
            memberName + "\n",  // Property (newline before brace)
            memberName + "\r",  // Property (Windows newline)
            " " + memberName + " ",  // Field with spaces
            " " + memberName + ";",  // Field ending with semicolon
            " " + memberName + "=",  // Field with initializer
        };

        foreach (var pattern in patterns)
        {
            var idx = sourceContent.IndexOf(pattern, StringComparison.Ordinal);
            
            while (idx >= 0)
            {
                // Check if this is inside a doc comment or string (skip if so)
                if (!IsInsideDocComment(sourceContent, idx))
                {
                    // Found potential member, search backwards for /// comments
                    var commentBlock = ExtractPrecedingDocComment(sourceContent, idx);
                    if (commentBlock != null)
                    {
                        return ParseXmlDocComment(commentBlock);
                    }
                }
                
                // Keep searching for other occurrences
                idx = sourceContent.IndexOf(pattern, idx + pattern.Length, StringComparison.Ordinal);
            }
        }

        return null;
    }

    /// <summary>
    /// Checks if the given index is inside a doc comment line (starts with ///).
    /// </summary>
    private static bool IsInsideDocComment(string sourceContent, int index)
    {
        // Find the start of the line containing this index
        var lineStart = sourceContent.LastIndexOf('\n', index);
        if (lineStart < 0) lineStart = 0;
        else lineStart++; // Skip the newline char
        
        // Get the line content up to the index
        var linePrefix = sourceContent.Substring(lineStart, index - lineStart).TrimStart();
        
        // If the line starts with ///, we're inside a doc comment
        return linePrefix.StartsWith("///") || linePrefix.StartsWith("//");
    }

    // Known C# modifiers that can appear before type/member declarations
    private static readonly HashSet<string> Modifiers = new(StringComparer.Ordinal)
    {
        "public", "private", "protected", "internal",
        "static", "virtual", "override", "abstract", "sealed",
        "async", "readonly", "new", "extern", "unsafe", "volatile",
        "partial", "file", "required"
    };

    /// <summary>
    /// Extracts the /// comment block immediately preceding a declaration.
    /// </summary>
    private static string? ExtractPrecedingDocComment(string sourceContent, int declarationIndex)
    {
        // First, find the start of the line containing the declaration
        var declLineStart = sourceContent.LastIndexOf('\n', declarationIndex);
        if (declLineStart < 0) declLineStart = 0;
        else declLineStart++; // Skip the newline char
        
        // Start searching from the line before the declaration
        var currentIdx = declLineStart - 2;
        if (currentIdx < 0) return null;
        
        // Search backwards to find /// comment lines
        List<string> lines = [];
        
        while (currentIdx >= 0)
        {
            var lineStart = sourceContent.LastIndexOf('\n', currentIdx);
            if (lineStart < 0) lineStart = 0;
            else lineStart++; // Skip the newline char
            
            var lineEnd = sourceContent.IndexOf('\n', lineStart);
            if (lineEnd < 0) lineEnd = sourceContent.Length;
            
            var line = sourceContent.Substring(lineStart, lineEnd - lineStart).Trim();
            
            if (line.StartsWith("///"))
            {
                lines.Insert(0, line);
                currentIdx = lineStart - 2; // Move to previous line
            }
            else if (line.Length == 0 || 
                     line.StartsWith("[") || 
                     (line.StartsWith("//") && !line.StartsWith("///")))
            {
                // Empty line, attribute, or regular comment - skip and continue looking
                currentIdx = lineStart - 2;
            }
            else
            {
                // Non-comment, non-attribute line - stop searching
                break;
            }
            
            if (currentIdx < 0) break;
        }
        
        if (lines.Count == 0)
            return null;
            
        return string.Join("\n", lines);
    }

    /// <summary>
    /// Checks if a line contains only modifiers (part of declaration on previous lines).
    /// </summary>
    private static bool IsModifierLine(string line)
    {
        // Split by whitespace and check if all tokens are modifiers
        var tokens = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return tokens.Length > 0 && tokens.All(t => Modifiers.Contains(t));
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

            // Extract sample references from <example><code source="..." .../> and <seealso href="...">
            List<SampleReference>? samples = ExtractSampleReferences(doc);

            if (summary == null && remarks == null && returns == null && parameters == null && samples == null)
                return null;

            return new DocComment(summary, remarks, parameters, returns, samples);
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

    /// <summary>
    /// Extracts sample references from doc comments.
    /// Supports two patterns:
    /// 1. Sandcastle-style: <example><code source="..." region="..." title="..."/></example>
    /// 2. Simple seealso: <seealso href="path/to/file.cs">description</seealso>
    /// </summary>
    private static List<SampleReference>? ExtractSampleReferences(XmlDocument doc)
    {
        List<SampleReference> samples = [];

        // Pattern 1: Sandcastle-style <code source="..." region="..." title="..."/>
        var codeNodes = doc.SelectNodes("//example/code[@source]");
        if (codeNodes != null)
        {
            foreach (XmlNode node in codeNodes)
            {
                var source = node.Attributes?["source"]?.Value;
                if (string.IsNullOrEmpty(source))
                    continue;

                var region = node.Attributes?["region"]?.Value;
                var title = node.Attributes?["title"]?.Value;

                samples.Add(new SampleReference(
                    NormalizePath(source),
                    title,
                    region
                ));
            }
        }

        // Pattern 2: <seealso href="path/to/file.cs">description</seealso>
        var seealsoNodes = doc.SelectNodes("//seealso[@href]");
        if (seealsoNodes != null)
        {
            foreach (XmlNode node in seealsoNodes)
            {
                var href = node.Attributes?["href"]?.Value;
                if (string.IsNullOrEmpty(href))
                    continue;

                // Only process relative paths to code files, not URLs
                if (href.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                    href.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                    continue;

                // Check if it's a code file
                if (!IsCodeFile(href))
                    continue;

                var description = node.InnerText?.Trim();
                var region = node.Attributes?["region"]?.Value;

                samples.Add(new SampleReference(
                    NormalizePath(href),
                    string.IsNullOrEmpty(description) ? null : description,
                    region
                ));
            }
        }

        return samples.Count > 0 ? samples : null;
    }

    /// <summary>
    /// Checks if a path points to a code file based on extension.
    /// </summary>
    private static bool IsCodeFile(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext is ".cs" or ".fs" or ".vb" or ".fsx" or ".csx";
    }

    /// <summary>
    /// Normalizes a path by converting backslashes to forward slashes.
    /// </summary>
    private static string NormalizePath(string path)
    {
        return path.Replace('\\', '/');
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
        // Collapse multiple whitespace into single space using source-generated regex
        return WhitespaceRegex().Replace(text.Trim(), " ");
    }
}
