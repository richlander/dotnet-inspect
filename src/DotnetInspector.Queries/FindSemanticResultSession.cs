using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using DotnetInspector.SourceSelection;
using ILInspector.Metadata;
using ILInspector.MetadataPrimitives;

namespace DotnetInspector.Queries;

/// <summary>
/// Exact identity of one semantic Find result. Pattern identity is retained so
/// independently truthful matches from repeated or overlapping patterns remain
/// distinct.
/// </summary>
public abstract record FindSemanticFindingIdentity
{
    private FindSemanticFindingIdentity() { }

    public sealed record Type : FindSemanticFindingIdentity
    {
        internal Type(
            ExactLibrarySourceCoordinate source,
            MetadataTypeDefinitionName declaration,
            Guid moduleVersionId,
            int patternOrdinal)
        {
            Source = source;
            Declaration = declaration;
            ModuleVersionId = moduleVersionId;
            PatternOrdinal = patternOrdinal;
        }

        public ExactLibrarySourceCoordinate Source { get; }
        public MetadataTypeDefinitionName Declaration { get; }
        public Guid ModuleVersionId { get; }
        public int PatternOrdinal { get; }
    }

    public sealed record Member : FindSemanticFindingIdentity
    {
        internal Member(
            ExactLibrarySourceCoordinate source,
            MemberAnchor declaration,
            int patternOrdinal)
        {
            Source = source;
            Declaration = declaration;
            PatternOrdinal = patternOrdinal;
        }

        public ExactLibrarySourceCoordinate Source { get; }
        public MemberAnchor Declaration { get; }
        public int PatternOrdinal { get; }
    }
}

/// <summary>One typed semantic finding published by an ordered Find operation.</summary>
[JsonConverter(typeof(FindSemanticFindingJsonConverter))]
public abstract record FindSemanticFinding
{
    private FindSemanticFinding() { }

    public abstract FindSemanticFindingIdentity Identity { get; }

    public sealed record Type : FindSemanticFinding
    {
        internal Type(TypeFindSemanticMatch match)
        {
            ArgumentNullException.ThrowIfNull(match);
            Match = match;
            Identity = new FindSemanticFindingIdentity.Type(
                Match.Declaration.Source.Coordinate,
                Match.Declaration.Name,
                Match.Declaration.ModuleVersionId,
                Match.Pattern.Ordinal);
        }

        public TypeFindSemanticMatch Match { get; }
        public override FindSemanticFindingIdentity Identity { get; }
    }

    public sealed record Member : FindSemanticFinding
    {
        internal Member(MemberFindSemanticMatch match)
        {
            ArgumentNullException.ThrowIfNull(match);
            Match = match;
            Identity = new FindSemanticFindingIdentity.Member(
                Match.Declaration.Source.Coordinate,
                Match.Declaration.Member,
                Match.Pattern.Ordinal);
        }

        public MemberFindSemanticMatch Match { get; }
        public override FindSemanticFindingIdentity Identity { get; }
    }
}

public sealed class FindSemanticFindingJsonConverter
    : JsonConverter<FindSemanticFinding>
{
    public override FindSemanticFinding Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        using JsonDocument document = JsonDocument.ParseValue(ref reader);
        JsonElement root = document.RootElement;
        if (!root.TryGetProperty("finding_kind", out JsonElement kind)
            || kind.ValueKind is not JsonValueKind.String
            || !root.TryGetProperty("match", out JsonElement match))
        {
            throw new JsonException(
                "A semantic Find finding requires a kind and match.");
        }

        return kind.GetString() switch
        {
            "type" => new FindSemanticFinding.Type(
                Deserialize(
                    match,
                    FindSemanticFindingJsonContext.Default
                        .TypeFindSemanticMatch)),
            "member" => new FindSemanticFinding.Member(
                Deserialize(
                    match,
                    FindSemanticFindingJsonContext.Default
                        .MemberFindSemanticMatch)),
            _ => throw new JsonException(
                "The semantic Find finding kind is unknown."),
        };
    }

    public override void Write(
        Utf8JsonWriter writer,
        FindSemanticFinding value,
        JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        switch (value)
        {
            case FindSemanticFinding.Type type:
                writer.WriteString("finding_kind", "type");
                writer.WritePropertyName("match");
                JsonSerializer.Serialize(
                    writer,
                    type.Match,
                    FindSemanticFindingJsonContext.Default
                        .TypeFindSemanticMatch);
                break;
            case FindSemanticFinding.Member member:
                writer.WriteString("finding_kind", "member");
                writer.WritePropertyName("match");
                JsonSerializer.Serialize(
                    writer,
                    member.Match,
                    FindSemanticFindingJsonContext.Default
                        .MemberFindSemanticMatch);
                break;
            default:
                throw new JsonException(
                    "The semantic Find finding type is unknown.");
        }
        writer.WriteEndObject();
    }

    private static T Deserialize<T>(
        JsonElement element,
        JsonTypeInfo<T> typeInfo) =>
        element.Deserialize(typeInfo)
        ?? throw new JsonException(
            "The semantic Find finding match cannot be null.");
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(TypeFindSemanticMatch))]
[JsonSerializable(typeof(MemberFindSemanticMatch))]
[JsonSerializable(
    typeof(TypeFindPattern.Glob),
    TypeInfoPropertyName = "TypeFindPatternGlob")]
[JsonSerializable(
    typeof(MemberFindPattern.Glob),
    TypeInfoPropertyName = "MemberFindPatternGlob")]
internal partial class FindSemanticFindingJsonContext
    : JsonSerializerContext;

/// <summary>
/// Receives durable findings synchronously in producer order. Implementations
/// must return promptly and must not retain producer-owned resources.
/// </summary>
public interface IFindSemanticFindingSink
{
    void Publish(FindSemanticFinding finding);
}

public enum FindSemanticFindingPublication
{
    Published,
    Duplicate,
    RowLimitReached,
}

public enum FindSemanticResultCompletion
{
    Exhausted,
    RowLimitReached,
    CandidateLimitReached,
    Partial,
    Failed,
    Canceled,
}

public sealed record FindSemanticResultFailure(
    string Source,
    string Message);

/// <summary>
/// The durable terminal result of one ordered semantic Find execution.
/// </summary>
public sealed record FindSemanticResult(
    ImmutableArray<FindSemanticFinding> Findings,
    FindSemanticResultCompletion Completion,
    ImmutableArray<FindSemanticResultFailure> Failures,
    ImmutableArray<string> Unsearched)
{
    public bool Incomplete =>
        Completion is not FindSemanticResultCompletion.Exhausted;
}

/// <summary>
/// Owns immediate sink publication, exact first-wins duplicate suppression,
/// finite row capacity, and the identical completed finding sequence.
/// </summary>
/// <remarks>
/// A session has one producer and is not thread-safe. Producers may use any
/// execution strategy; consumers observe only semantic findings.
/// </remarks>
public sealed class FindSemanticResultSession
{
    private readonly IFindSemanticFindingSink? _sink;
    private readonly int? _maximumFindings;
    private readonly HashSet<FindSemanticFindingIdentity> _identities = [];
    private readonly ImmutableArray<FindSemanticFinding>.Builder _findings =
        ImmutableArray.CreateBuilder<FindSemanticFinding>();
    private bool _completed;

    public FindSemanticResultSession(
        int? maximumFindings = null,
        IFindSemanticFindingSink? sink = null)
    {
        if (maximumFindings <= 0)
            throw new ArgumentOutOfRangeException(nameof(maximumFindings));
        _maximumFindings = maximumFindings;
        _sink = sink;
    }

    public int Count => _findings.Count;

    public int? RemainingCapacity =>
        _maximumFindings is int maximum
            ? maximum - _findings.Count
            : null;

    public FindSemanticFindingPublication Publish(
        TypeFindSemanticMatch match)
    {
        ArgumentNullException.ThrowIfNull(match);
        return Publish(new FindSemanticFinding.Type(match));
    }

    public FindSemanticFindingPublication Publish(
        MemberFindSemanticMatch match)
    {
        ArgumentNullException.ThrowIfNull(match);
        return Publish(new FindSemanticFinding.Member(match));
    }

    public FindSemanticResult Complete(
        FindSemanticResultCompletion completion,
        IEnumerable<FindSemanticResultFailure>? failures = null,
        IEnumerable<string>? unsearched = null)
    {
        if (_completed)
            throw new InvalidOperationException(
                "The Find semantic result session is already complete.");

        ImmutableArray<FindSemanticResultFailure> retainedFailures =
            failures is null ? [] : [.. failures];
        ImmutableArray<string> retainedUnsearched =
            unsearched is null ? [] : [.. unsearched];
        if (retainedFailures.Any(static failure => failure is null))
        {
            throw new ArgumentException(
                "Find semantic failures cannot contain null.",
                nameof(failures));
        }
        if (retainedUnsearched.Any(static source =>
                string.IsNullOrWhiteSpace(source)))
        {
            throw new ArgumentException(
                "Unsearched Find sources must be named.",
                nameof(unsearched));
        }

        _completed = true;
        return new(
            _findings.DrainToImmutable(),
            completion,
            retainedFailures,
            retainedUnsearched);
    }

    private FindSemanticFindingPublication Publish(
        FindSemanticFinding finding)
    {
        if (_completed)
        {
            throw new InvalidOperationException(
                "The Find semantic result session is already complete.");
        }
        if (_identities.Contains(finding.Identity))
            return FindSemanticFindingPublication.Duplicate;
        if (_maximumFindings is int maximum
            && _findings.Count >= maximum)
        {
            return FindSemanticFindingPublication.RowLimitReached;
        }

        _sink?.Publish(finding);
        _identities.Add(finding.Identity);
        _findings.Add(finding);
        return FindSemanticFindingPublication.Published;
    }
}
