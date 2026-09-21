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
/// Results from one scan with independent retained-text budgets per exact ID.
/// </summary>
public sealed record XmlDocumentationReadManyResult(
    IReadOnlyDictionary<string, XmlDocumentationEntry> Entries,
    IReadOnlySet<string> RetainedTextLimitExceeded);

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

    /// <summary>
    /// Reads several exact members in one complete, bounded document scan.
    /// </summary>
    public static XmlDocumentationReadManyResult
        ReadMembers(
            Stream stream,
            IReadOnlyCollection<XmlDocMemberIdentity> identities,
            XmlDocumentationReadLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(identities);
        limits ??= XmlDocumentationReadLimits.Default;

        var selected = new HashSet<string>(
            identities.Select(static identity =>
                (identity
                    ?? throw new ArgumentException(
                        "Documentation identities cannot contain null.",
                        nameof(identities)))
                    .Value),
            StringComparer.Ordinal);
        if (selected.Count != identities.Count)
        {
            throw new ArgumentException(
                "Documentation identities must be unique.",
                nameof(identities));
        }

        var results =
            new Dictionary<string, XmlDocumentationEntry>(
                selected.Count,
                StringComparer.Ordinal);
        var retainedTextLimitExceeded =
            new HashSet<string>(StringComparer.Ordinal);
        XmlDocumentationParser.Scan(
            stream,
            limits,
            selected.Contains,
            (memberId, entry) => results[memberId] = entry,
            memberId =>
            {
                results.Remove(memberId);
                retainedTextLimitExceeded.Add(memberId);
            });
        return new(
            new ReadOnlyDictionary<string, XmlDocumentationEntry>(
                results),
            new ReadOnlySet<string>(retainedTextLimitExceeded));
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
        Action<string, XmlDocumentationEntry> accept,
        Action<string>? rejectRetainedTextLimit = null)
    {
        limits.Validate();
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersInDocument = limits.MaxCharactersInDocument,
        };
        using XmlReader reader = XmlReader.Create(stream, settings);
        try
        {
            Scan(
                reader,
                limits,
                select,
                accept,
                rejectRetainedTextLimit,
                observe: null);
        }
        catch (XmlDocumentationLimitException exception)
        {
            throw new XmlException(exception.Message, exception);
        }
    }

    internal static void Scan(
        XmlReader reader,
        XmlDocumentationReadLimits limits,
        Func<string, bool> select,
        Action<string, XmlDocumentationEntry> accept,
        Action<string>? rejectRetainedTextLimit,
        Action<XmlReader>? observe)
    {
        var sharedBudget = new RetainedTextBudget(
            limits.MaxRetainedTextCharacters,
            throwOnExceeded: true);
        Dictionary<string, RetainedTextBudget>? budgetsByIdentity =
            rejectRetainedTextLimit is null
                ? null
                : new(StringComparer.Ordinal);
        int? docDepth = null;
        int? membersDepth = null;
        int memberCount = 0;

        while (Read(reader, observe))
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
                throw new XmlDocumentationLimitException(
                    XmlDocumentationLimitKind.Members,
                    $"XML documentation exceeds the member limit of {limits.MaxMembers}.");
            }

            string? memberId = reader.GetAttribute("name");
            if (memberId?.Length > limits.MaxMemberIdCharacters)
            {
                throw new XmlDocumentationLimitException(
                    XmlDocumentationLimitKind.MemberIdCharacters,
                    "An XML documentation member ID exceeds the supported character limit.");
            }

            bool retain = memberId is not null && select(memberId);
            RetainedTextBudget budget = sharedBudget;
            if (retain
                && budgetsByIdentity is not null
                && !budgetsByIdentity.TryGetValue(memberId!, out budget!))
            {
                budget = new RetainedTextBudget(
                    limits.MaxRetainedTextCharacters,
                    throwOnExceeded: false);
                budgetsByIdentity.Add(memberId!, budget);
            }
            if (retain)
                budget.Retain(memberId);
            XmlDocumentationEntry? entry =
                ReadEntry(reader, limits, budget, retain, observe);
            if (retain && budget.Exceeded)
                rejectRetainedTextLimit!(memberId!);
            else if (entry is not null)
                accept(memberId!, entry);
        }
    }

    static XmlDocumentationEntry? ReadEntry(
        XmlReader reader,
        XmlDocumentationReadLimits limits,
        RetainedTextBudget budget,
        bool retain,
        Action<XmlReader>? observe)
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

        while (Read(reader, observe))
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
                    summary ??= ReadText(reader, budget, retain, observe);
                    break;

                case "remarks":
                    remarks ??= ReadText(reader, budget, retain, observe);
                    break;

                case "returns":
                    returns ??= ReadText(reader, budget, retain, observe);
                    break;

                case "param":
                    parameterCount++;
                    if (parameterCount > limits.MaxParametersPerMember)
                    {
                        throw new XmlDocumentationLimitException(
                            XmlDocumentationLimitKind.Parameters,
                            "An XML documentation member exceeds the parameter limit.");
                    }
                    string? parameterName =
                        retain ? reader.GetAttribute("name") : null;
                    string? parameterText =
                        ReadText(
                            reader,
                            budget,
                            retain,
                            observe,
                            chargeBudget: false);
                    if (parameterName is not null
                        && parameterText is not null)
                    {
                        parameters!.TryGetValue(
                            parameterName,
                            out string? previousText);
                        if (budget.Replace(
                                previousText is null ? null : parameterName,
                                previousText,
                                parameterName,
                                parameterText))
                        {
                            parameters![parameterName] = parameterText;
                        }
                    }
                    break;

                case "exception":
                    exceptionCount++;
                    if (exceptionCount > limits.MaxExceptionsPerMember)
                    {
                        throw new XmlDocumentationLimitException(
                            XmlDocumentationLimitKind.Exceptions,
                            "An XML documentation member exceeds the exception limit.");
                    }
                    string? cref =
                        retain ? reader.GetAttribute("cref") : null;
                    string? description =
                        ReadText(reader, budget, retain, observe);
                    if (retain)
                    {
                        if (budget.Retain(cref))
                        {
                            exceptions!.Add(
                                new XmlDocumentationException(
                                    cref,
                                    description));
                        }
                    }
                    break;

                case "example":
                    ReadSamples(
                        reader,
                        samples,
                        ref sampleCount,
                        limits,
                        budget,
                        retain,
                        observe);
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
        bool retain,
        Action<XmlReader>? observe,
        bool chargeBudget = true)
    {
        if (!retain)
        {
            SkipElement(reader, observe);
            return null;
        }

        string text = XmlDocText.NormalizeWhitespace(
            XmlDocText.GetElementTextWithRefs(
                reader,
                XmlDocText.MaxElementDepth,
                observe));
        if (text.Length == 0)
            return null;
        return !chargeBudget || budget.Retain(text) ? text : null;
    }

    static void ReadSamples(
        XmlReader reader,
        List<XmlDocumentationSampleReference>? samples,
        ref int sampleCount,
        XmlDocumentationReadLimits limits,
        RetainedTextBudget budget,
        bool retain,
        Action<XmlReader>? observe)
    {
        if (reader.IsEmptyElement)
            return;

        int exampleDepth = reader.Depth;
        while (Read(reader, observe))
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
                throw new XmlDocumentationLimitException(
                    XmlDocumentationLimitKind.Samples,
                    "An XML documentation member exceeds the sample-reference limit.");
            }

            if (!retain)
                continue;

            string? title = reader.GetAttribute("title");
            string? region = reader.GetAttribute("region");
            if (budget.Retain(source)
                && budget.Retain(title)
                && budget.Retain(region))
            {
                samples!.Add(
                    new XmlDocumentationSampleReference(
                        source,
                        title,
                        region));
            }
        }
    }

    static void SkipElement(
        XmlReader reader,
        Action<XmlReader>? observe)
    {
        if (reader.IsEmptyElement)
            return;

        int elementDepth = reader.Depth;
        while (Read(reader, observe))
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
            throw new XmlDocumentationLimitException(
                XmlDocumentationLimitKind.Depth,
                $"XML documentation exceeds the supported element depth of "
                    + $"{XmlDocText.MaxElementDepth}.");
        }
    }

    static bool Read(
        XmlReader reader,
        Action<XmlReader>? observe)
    {
        bool read = reader.Read();
        if (read)
            observe?.Invoke(reader);
        return read;
    }

    sealed class RetainedTextBudget(
        long maximum,
        bool throwOnExceeded)
    {
        long retained;

        public bool Exceeded { get; private set; }

        public bool Retain(string? value)
        {
            if (value is null)
                return !Exceeded;
            if (Exceeded)
                return false;
            long completed = retained;
            long observed = checked(retained + value.Length);
            if (observed > maximum)
            {
                return Exceed(observed, completed);
            }
            retained = observed;
            return true;
        }

        public bool Replace(
            string? oldFirst,
            string? oldSecond,
            string? newFirst,
            string? newSecond)
        {
            if (Exceeded)
                return false;

            long completed = retained;
            long observed = checked(
                retained
                    - Length(oldFirst)
                    - Length(oldSecond)
                    + Length(newFirst)
                    + Length(newSecond));
            if (observed > maximum)
                return Exceed(observed, completed);

            retained = observed;
            return true;
        }

        bool Exceed(long observed, long completed)
        {
            Exceeded = true;
            if (throwOnExceeded)
            {
                throw new XmlDocumentationLimitException(
                    XmlDocumentationLimitKind.RetainedText,
                    "XML documentation exceeds the retained-text character limit.",
                    maximum,
                    observed,
                    completed);
            }
            return false;
        }

        static int Length(string? value) => value?.Length ?? 0;
    }

    internal enum XmlDocumentationLimitKind
    {
        Members,
        MemberIdCharacters,
        Parameters,
        Exceptions,
        Samples,
        Depth,
        RetainedText,
    }

    internal sealed class XmlDocumentationLimitException(
        XmlDocumentationLimitKind kind,
        string message,
        long limit = 0,
        long observed = 0,
        long completed = 0)
        : XmlException(message)
    {
        public XmlDocumentationLimitKind Kind { get; } = kind;
        public long Limit { get; } = limit;
        public long Observed { get; } = observed;
        public long Completed { get; } = completed;
    }
}
