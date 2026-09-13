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
            ValidateDepth(reader);
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
            if (memberId?.Length > limits.MaxMemberIdCharacters)
            {
                throw new XmlException(
                    "An XML documentation member ID exceeds the supported character limit.");
            }

            bool retain = memberId is not null && select(memberId);
            if (retain)
                budget.Retain(memberId);
            XmlDocumentationEntry? entry =
                ReadEntry(reader, limits, budget, retain);
            if (entry is not null)
                accept(memberId!, entry);
        }
    }

    static XmlDocumentationEntry? ReadEntry(
        XmlReader reader,
        XmlDocumentationReadLimits limits,
        RetainedTextBudget budget,
        bool retain)
    {
        if (reader.IsEmptyElement)
            return retain ? XmlDocumentationEntry.Empty : null;

        Dictionary<string, string>? parameters = retain
            ? new(StringComparer.Ordinal)
            : null;
        List<XmlDocumentationException>? exceptions = retain ? [] : null;
        List<XmlDocumentationSampleReference>? samples = retain ? [] : null;
        string? summary = null;
        string? remarks = null;
        string? returns = null;
        int memberDepth = reader.Depth;
        int parameterCount = 0;
        int exceptionCount = 0;
        int sampleCount = 0;

        while (reader.Read())
        {
            ValidateDepth(reader);
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
                    summary ??= ReadText(reader, budget, retain);
                    break;

                case "remarks":
                    remarks ??= ReadText(reader, budget, retain);
                    break;

                case "returns":
                    returns ??= ReadText(reader, budget, retain);
                    break;

                case "param":
                    parameterCount++;
                    if (parameterCount > limits.MaxParametersPerMember)
                    {
                        throw new XmlException(
                            "An XML documentation member exceeds the parameter limit.");
                    }
                    string? parameterName =
                        retain ? reader.GetAttribute("name") : null;
                    string? parameterText = ReadText(reader, budget, retain);
                    if (parameterName is not null
                        && parameterText is not null)
                    {
                        budget.Retain(parameterName);
                        parameters![parameterName] = parameterText;
                    }
                    break;

                case "exception":
                    exceptionCount++;
                    if (exceptionCount > limits.MaxExceptionsPerMember)
                    {
                        throw new XmlException(
                            "An XML documentation member exceeds the exception limit.");
                    }
                    string? cref =
                        retain ? reader.GetAttribute("cref") : null;
                    string? description = ReadText(reader, budget, retain);
                    if (retain)
                    {
                        budget.Retain(cref);
                        exceptions!.Add(
                            new XmlDocumentationException(
                                cref,
                                description));
                    }
                    break;

                case "example":
                    ReadSamples(
                        reader,
                        samples,
                        ref sampleCount,
                        limits,
                        budget,
                        retain);
                    break;
            }
        }

        if (!retain)
            return null;

        return new XmlDocumentationEntry(
            summary,
            remarks,
            returns,
            new ReadOnlyDictionary<string, string>(parameters!),
            exceptions!.AsReadOnly(),
            samples!.AsReadOnly());
    }

    static string? ReadText(
        XmlReader reader,
        RetainedTextBudget budget,
        bool retain)
    {
        if (!retain)
        {
            SkipElement(reader);
            return null;
        }

        string text = XmlDocText.NormalizeWhitespace(
            XmlDocText.GetElementTextWithRefs(reader));
        if (text.Length == 0)
            return null;
        budget.Retain(text);
        return text;
    }

    static void ReadSamples(
        XmlReader reader,
        List<XmlDocumentationSampleReference>? samples,
        ref int sampleCount,
        XmlDocumentationReadLimits limits,
        RetainedTextBudget budget,
        bool retain)
    {
        if (reader.IsEmptyElement)
            return;

        int exampleDepth = reader.Depth;
        while (reader.Read())
        {
            ValidateDepth(reader);
            if (reader.NodeType == XmlNodeType.EndElement
                && reader.Depth == exampleDepth)
            {
                break;
            }
            if (reader.NodeType != XmlNodeType.Element
                || reader.LocalName != "code"
                || reader.GetAttribute("source") is not { Length: > 0 } source)
            {
                continue;
            }
            sampleCount++;
            if (sampleCount > limits.MaxSamplesPerMember)
            {
                throw new XmlException(
                    "An XML documentation member exceeds the sample-reference limit.");
            }

            if (!retain)
                continue;

            string? title = reader.GetAttribute("title");
            string? region = reader.GetAttribute("region");
            budget.Retain(source);
            budget.Retain(title);
            budget.Retain(region);
            samples!.Add(
                new XmlDocumentationSampleReference(source, title, region));
        }
    }

    static void SkipElement(XmlReader reader)
    {
        if (reader.IsEmptyElement)
            return;

        int elementDepth = reader.Depth;
        while (reader.Read())
        {
            ValidateDepth(reader);
            if (reader.NodeType == XmlNodeType.EndElement
                && reader.Depth == elementDepth)
            {
                return;
            }
        }
    }

    static void ValidateDepth(XmlReader reader)
    {
        if (reader.NodeType == XmlNodeType.Element
            && reader.Depth > XmlDocText.MaxElementDepth + 3)
        {
            throw new XmlException(
                $"XML documentation exceeds the supported element depth of "
                    + $"{XmlDocText.MaxElementDepth}.");
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
