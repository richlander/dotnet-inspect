using System.Collections.ObjectModel;
using System.Xml;

namespace CSharpText;

/// <summary>
/// Resource limits for compiler XML-documentation parsing.
/// </summary>
public sealed record XmlDocumentationReadLimits
{
    public static XmlDocumentationReadLimits Default { get; } = new();

    public long MaxCharactersInDocument { get; init; } = 64L * 1024 * 1024;
    public int MaxMembers { get; init; } = 250_000;
    public int MaxMemberIdCharacters { get; init; } = 32_768;
    public long MaxRetainedTextCharacters { get; init; } = 64L * 1024 * 1024;
    public int MaxParametersPerMember { get; init; } = 4_096;
    public int MaxExceptionsPerMember { get; init; } = 4_096;
    public int MaxSamplesPerMember { get; init; } = 4_096;

    internal void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaxCharactersInDocument);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaxMembers);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaxMemberIdCharacters);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaxRetainedTextCharacters);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaxParametersPerMember);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaxExceptionsPerMember);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaxSamplesPerMember);
    }
}

/// <summary>One exception entry from compiler XML documentation.</summary>
public sealed record XmlDocumentationException(string? Cref, string? Description);

/// <summary>One external sample reference from compiler XML documentation.</summary>
public sealed record XmlDocumentationSampleReference(
    string Source,
    string? Title,
    string? Region);

/// <summary>Parsed fields for one exact compiler XML-documentation ID.</summary>
public sealed record XmlDocumentationEntry(
    string? Summary,
    string? Remarks,
    string? Returns,
    IReadOnlyDictionary<string, string> Parameters,
    IReadOnlyList<XmlDocumentationException> Exceptions,
    IReadOnlyList<XmlDocumentationSampleReference> Samples)
{
    internal static XmlDocumentationEntry Empty { get; } = new(
        null,
        null,
        null,
        new ReadOnlyDictionary<string, string>(
            new Dictionary<string, string>(StringComparer.Ordinal)),
        [],
        []);
}

/// <summary>
/// Reads one exact member while validating the complete XML document.
/// </summary>
public static class XmlDocumentationReader
{
    public static XmlDocumentationEntry? ReadMember(
        Stream stream,
        XmlDocMemberIdentity identity,
        XmlDocumentationReadLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(identity);
        limits ??= XmlDocumentationReadLimits.Default;

        XmlDocumentationEntry? result = null;
        XmlDocumentationParser.Scan(
            stream,
            limits,
            memberId => string.Equals(
                memberId,
                identity.Value,
                StringComparison.Ordinal),
            (_, entry) => result = entry);
        return result;
    }
}

/// <summary>
/// An in-memory exact-ID catalog for repeated compiler XML-documentation lookup.
/// </summary>
public sealed class XmlDocumentationCatalog
{
    readonly Dictionary<string, XmlDocumentationEntry> members;

    XmlDocumentationCatalog(Dictionary<string, XmlDocumentationEntry> members) =>
        this.members = members;

    public static XmlDocumentationCatalog Load(
        Stream stream,
        XmlDocumentationReadLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(stream);
        limits ??= XmlDocumentationReadLimits.Default;

        var members = new Dictionary<string, XmlDocumentationEntry>(
            StringComparer.Ordinal);
        XmlDocumentationParser.Scan(
            stream,
            limits,
            static _ => true,
            (memberId, entry) => members[memberId] = entry);
        return new XmlDocumentationCatalog(members);
    }

    public XmlDocumentationEntry? Find(XmlDocMemberIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(identity);
        return members.GetValueOrDefault(identity.Value);
    }
}

static class XmlDocumentationParser
{
    public static void Scan(
        Stream stream,
        XmlDocumentationReadLimits limits,
        Func<string, bool> select,
        Action<string, XmlDocumentationEntry> accept)
    {
        limits.Validate();
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersInDocument = limits.MaxCharactersInDocument,
        };
        using XmlReader reader = XmlReader.Create(stream, settings);
        var budget = new RetainedTextBudget(limits.MaxRetainedTextCharacters);
        int? docDepth = null;
        int? membersDepth = null;
        int memberCount = 0;

        while (reader.Read())
        {
            if (reader.NodeType == XmlNodeType.Element
                && reader.Depth > XmlDocText.MaxElementDepth + 3)
            {
                throw new XmlException(
                    $"XML documentation exceeds the supported element depth of "
                        + $"{XmlDocText.MaxElementDepth}.");
            }
            if (reader.NodeType == XmlNodeType.Element
                && reader.LocalName == "doc"
                && reader.Depth == 0
                && docDepth is null)
            {
                docDepth = reader.Depth;
                continue;
            }
            if (reader.NodeType == XmlNodeType.Element
                && reader.LocalName == "members"
                && docDepth is { } rootDepth
                && reader.Depth == rootDepth + 1)
            {
                membersDepth = reader.Depth;
                continue;
            }
            if (reader.NodeType == XmlNodeType.EndElement
                && reader.LocalName == "members"
                && reader.Depth == membersDepth)
            {
                membersDepth = null;
                continue;
            }
            if (reader.NodeType != XmlNodeType.Element
                || reader.LocalName != "member"
                || membersDepth is not { } containerDepth
                || reader.Depth != containerDepth + 1)
            {
                continue;
            }

            memberCount++;
            if (memberCount > limits.MaxMembers)
            {
                throw new XmlException(
                    $"XML documentation exceeds the member limit of {limits.MaxMembers}.");
            }

            string? memberId = reader.GetAttribute("name");
            if (memberId is null)
                continue;
            if (memberId.Length > limits.MaxMemberIdCharacters)
            {
                throw new XmlException(
                    "An XML documentation member ID exceeds the supported character limit.");
            }
            if (!select(memberId))
                continue;

            budget.Retain(memberId);
            XmlDocumentationEntry entry = ReadEntry(reader, limits, budget);
            accept(memberId, entry);
        }
    }

    static XmlDocumentationEntry ReadEntry(
        XmlReader reader,
        XmlDocumentationReadLimits limits,
        RetainedTextBudget budget)
    {
        if (reader.IsEmptyElement)
            return XmlDocumentationEntry.Empty;

        var parameters = new Dictionary<string, string>(StringComparer.Ordinal);
        var exceptions = new List<XmlDocumentationException>();
        var samples = new List<XmlDocumentationSampleReference>();
        string? summary = null;
        string? remarks = null;
        string? returns = null;
        int memberDepth = reader.Depth;
        int parameterCount = 0;

        while (reader.Read())
        {
            if (reader.NodeType == XmlNodeType.EndElement
                && reader.Depth == memberDepth)
            {
                break;
            }
            if (reader.NodeType != XmlNodeType.Element
                || reader.Depth != memberDepth + 1)
            {
                continue;
            }

            switch (reader.LocalName)
            {
                case "summary":
                    summary ??= ReadText(reader, budget);
                    break;

                case "remarks":
                    remarks ??= ReadText(reader, budget);
                    break;

                case "returns":
                    returns ??= ReadText(reader, budget);
                    break;

                case "param":
                    parameterCount++;
                    if (parameterCount > limits.MaxParametersPerMember)
                    {
                        throw new XmlException(
                            "An XML documentation member exceeds the parameter limit.");
                    }
                    string? parameterName = reader.GetAttribute("name");
                    string? parameterText = ReadText(reader, budget);
                    if (parameterName is not null && parameterText is not null)
                    {
                        budget.Retain(parameterName);
                        parameters[parameterName] = parameterText;
                    }
                    break;

                case "exception":
                    if (exceptions.Count == limits.MaxExceptionsPerMember)
                    {
                        throw new XmlException(
                            "An XML documentation member exceeds the exception limit.");
                    }
                    string? cref = reader.GetAttribute("cref");
                    string? description = ReadText(reader, budget);
                    budget.Retain(cref);
                    exceptions.Add(new XmlDocumentationException(cref, description));
                    break;

                case "example":
                    ReadSamples(reader, samples, limits, budget);
                    break;
            }
        }

        return new XmlDocumentationEntry(
            summary,
            remarks,
            returns,
            new ReadOnlyDictionary<string, string>(parameters),
            exceptions.AsReadOnly(),
            samples.AsReadOnly());
    }

    static string? ReadText(XmlReader reader, RetainedTextBudget budget)
    {
        string text = XmlDocText.NormalizeWhitespace(
            XmlDocText.GetElementTextWithRefs(reader));
        if (text.Length == 0)
            return null;
        budget.Retain(text);
        return text;
    }

    static void ReadSamples(
        XmlReader reader,
        List<XmlDocumentationSampleReference> samples,
        XmlDocumentationReadLimits limits,
        RetainedTextBudget budget)
    {
        if (reader.IsEmptyElement)
            return;

        int exampleDepth = reader.Depth;
        while (reader.Read())
        {
            if (reader.NodeType == XmlNodeType.EndElement
                && reader.Depth == exampleDepth)
            {
                break;
            }
            if (reader.NodeType == XmlNodeType.Element
                && reader.Depth - exampleDepth > XmlDocText.MaxElementDepth)
            {
                throw new XmlException(
                    $"XML documentation exceeds the supported element depth of "
                        + $"{XmlDocText.MaxElementDepth}.");
            }
            if (reader.NodeType != XmlNodeType.Element
                || reader.LocalName != "code"
                || reader.GetAttribute("source") is not { Length: > 0 } source)
            {
                continue;
            }
            if (samples.Count == limits.MaxSamplesPerMember)
            {
                throw new XmlException(
                    "An XML documentation member exceeds the sample-reference limit.");
            }

            string? title = reader.GetAttribute("title");
            string? region = reader.GetAttribute("region");
            budget.Retain(source);
            budget.Retain(title);
            budget.Retain(region);
            samples.Add(new XmlDocumentationSampleReference(source, title, region));
        }
    }

    sealed class RetainedTextBudget(long maximum)
    {
        long retained;

        public void Retain(string? value)
        {
            if (value is null)
                return;
            retained += value.Length;
            if (retained > maximum)
            {
                throw new XmlException(
                    "XML documentation exceeds the retained-text character limit.");
            }
        }
    }
}
