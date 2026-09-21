using System.Collections.Immutable;
using System.Text.Json.Serialization;
using InertText;

namespace DotnetInspector.Sections;

/// <summary>
/// A completed inspection and the supplemental evidence captured by the same
/// operation.
/// </summary>
/// <typeparam name="TContent">The owner-issued primary content result.</typeparam>
/// <typeparam name="TEvidence">The owner-issued supplemental evidence.</typeparam>
public sealed record EvidenceInspectionEnvelope<TContent, TEvidence>
{
    [JsonConstructor]
    public EvidenceInspectionEnvelope(
        InspectionEnvelope<TContent> inspection,
        TEvidence evidence)
    {
        Inspection = inspection
            ?? throw new ArgumentNullException(nameof(inspection));
        Evidence = evidence
            ?? throw new ArgumentNullException(nameof(evidence));
    }

    public InspectionEnvelope<TContent> Inspection { get; }

    public TEvidence Evidence { get; }
}

/// <summary>
/// The host-neutral handoff for one completed inspection operation.
/// </summary>
/// <typeparam name="TContent">The owner-issued primary content result.</typeparam>
public sealed record InspectionEnvelope<TContent>
{
    public InspectionEnvelope(
        InspectionContentKind contentKind,
        TContent content,
        InspectionPortableProjection portableProjection,
        IEnumerable<InspectionDiagnostic>? diagnostics = null)
        : this(
            contentKind,
            content,
            portableProjection,
            (diagnostics ?? []).ToImmutableArray())
    {
    }

    [JsonConstructor]
    public InspectionEnvelope(
        InspectionContentKind contentKind,
        TContent content,
        InspectionPortableProjection portableProjection,
        ImmutableArray<InspectionDiagnostic> diagnostics)
    {
        if (!Enum.IsDefined(contentKind))
            throw new ArgumentOutOfRangeException(nameof(contentKind));

        ArgumentNullException.ThrowIfNull(content);
        ContentKind = contentKind;
        Content = content;
        PortableProjection = portableProjection
            ?? throw new ArgumentNullException(nameof(portableProjection));
        Diagnostics = diagnostics.IsDefault
            ? []
            : diagnostics;
    }

    public InspectionContentKind ContentKind { get; }

    public TContent Content { get; }

    public InspectionPortableProjection PortableProjection { get; }

    public ImmutableArray<InspectionDiagnostic> Diagnostics { get; }

    public bool Equals(InspectionEnvelope<TContent>? other)
    {
        if (ReferenceEquals(this, other))
            return true;
        if (other is null
            || ContentKind != other.ContentKind
            || !EqualityComparer<TContent>.Default.Equals(
                Content,
                other.Content)
            || !PortableProjectionEquals(
                PortableProjection,
                other.PortableProjection)
            || Diagnostics.Length != other.Diagnostics.Length)
        {
            return false;
        }

        return Diagnostics
            .Zip(other.Diagnostics)
            .All(static pair =>
                pair.First.Code == pair.Second.Code
                && pair.First.Severity == pair.Second.Severity
                && pair.First.Summary.ToString()
                    == pair.Second.Summary.ToString()
                && pair.First.Correspondence?.ToString()
                    == pair.Second.Correspondence?.ToString());
    }

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(ContentKind);
        hash.Add(Content);
        hash.Add(PortableProjectionHashCode(PortableProjection));
        foreach (InspectionDiagnostic diagnostic in Diagnostics)
        {
            hash.Add(diagnostic.Code, StringComparer.Ordinal);
            hash.Add(diagnostic.Severity);
            hash.Add(diagnostic.Summary.ToString(), StringComparer.Ordinal);
            hash.Add(
                diagnostic.Correspondence?.ToString(),
                StringComparer.Ordinal);
        }

        return hash.ToHashCode();
    }

    private static bool PortableProjectionEquals(
        InspectionPortableProjection left,
        InspectionPortableProjection right) =>
        (left, right) switch
        {
            (
                InspectionPortableProjection.Available a,
                InspectionPortableProjection.Available b) =>
                a.FullUrl == b.FullUrl
                && a.Packet == b.Packet,
            (
                InspectionPortableProjection.NonProjectable a,
                InspectionPortableProjection.NonProjectable b) =>
                a.Path == b.Path
                && a.Reason == b.Reason
                && a.Explanation == b.Explanation,
            _ => false,
        };

    private static int PortableProjectionHashCode(
        InspectionPortableProjection portableProjection) =>
        portableProjection switch
        {
            InspectionPortableProjection.Available available =>
                HashCode.Combine(0, available.FullUrl, available.Packet),
            InspectionPortableProjection.NonProjectable nonProjectable =>
                HashCode.Combine(
                    1,
                    nonProjectable.Path,
                    nonProjectable.Reason,
                    nonProjectable.Explanation),
            _ => throw new InvalidOperationException(
                "Unknown inspection portable projection."),
        };
}

/// <summary>
/// The semantic extent of the owner-issued content.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<InspectionContentKind>))]
public enum InspectionContentKind
{
    [JsonStringEnumMemberName("result")]
    Result,

    [JsonStringEnumMemberName("document")]
    Document,

    [JsonStringEnumMemberName("outcome")]
    Outcome,
}

/// <summary>
/// The required portable projection of an inspection plan.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(
    typeof(InspectionPortableProjection.Available),
    "available")]
[JsonDerivedType(
    typeof(InspectionPortableProjection.NonProjectable),
    "nonProjectable")]
public abstract record InspectionPortableProjection
{
    private InspectionPortableProjection()
    {
    }

    /// <summary>
    /// The complete canonical production URL when this projection is available.
    /// </summary>
    public abstract string? FullUrl { get; }

    /// <summary>
    /// The canonical encoded Workspace packet when this projection is available.
    /// </summary>
    public abstract string? Packet { get; }

    /// <summary>
    /// The complete canonical production URL and encoded Workspace packet for
    /// the same inspection plan.
    /// </summary>
    public sealed record Available : InspectionPortableProjection
    {
        public Available(string fullUrl, string packet)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(fullUrl);
            ArgumentException.ThrowIfNullOrWhiteSpace(packet);
            if (!Uri.TryCreate(fullUrl, UriKind.Absolute, out Uri? uri)
                || !string.Equals(
                    uri.Scheme,
                    Uri.UriSchemeHttps,
                    StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    "An available portable projection must be a complete HTTPS URL.",
                    nameof(fullUrl));
            }

            FullUrl = fullUrl;
            Packet = packet;
        }

        public override string FullUrl { get; }

        public override string Packet { get; }
    }

    /// <summary>
    /// The semantic path and typed reason that could not be projected.
    /// </summary>
    public sealed record NonProjectable : InspectionPortableProjection
    {
        [JsonConstructor]
        public NonProjectable(
            string path,
            InspectionPortableProjectionFailureReason reason,
            string? explanation = null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(path);
            if (!Enum.IsDefined(reason))
                throw new ArgumentOutOfRangeException(nameof(reason));
            if (explanation is not null)
                ArgumentException.ThrowIfNullOrWhiteSpace(explanation);

            Path = path;
            Reason = reason;
            Explanation = explanation;
        }

        public string Path { get; }

        public InspectionPortableProjectionFailureReason Reason { get; }

        public string? Explanation { get; }

        public override string? FullUrl => null;

        public override string? Packet => null;
    }
}

/// <summary>
/// Why an inspection plan has no faithful portable projection.
/// </summary>
[JsonConverter(
    typeof(JsonStringEnumConverter<
        InspectionPortableProjectionFailureReason>))]
public enum InspectionPortableProjectionFailureReason
{
    [JsonStringEnumMemberName("notSupported")]
    NotSupported,

    [JsonStringEnumMemberName("invalid")]
    Invalid,

    [JsonStringEnumMemberName("incomplete")]
    Incomplete,

    [JsonStringEnumMemberName("unavailable")]
    Unavailable,

    [JsonStringEnumMemberName("failed")]
    Failed,
}

/// <summary>
/// The severity of one cross-host inspection diagnostic.
/// </summary>
public enum InspectionDiagnosticSeverity
{
    Information,
    Warning,
    Error,
}

/// <summary>
/// Typed supplemental evidence disclosed consistently by each host.
/// </summary>
public sealed record InspectionDiagnostic
{
    public InspectionDiagnostic(
        string code,
        InspectionDiagnosticSeverity severity,
        string summary,
        string? correspondence = null)
        : this(
            code,
            severity,
            new InertString(TextPolicy.Field, summary),
            correspondence is null
                ? null
                : new InertString(TextPolicy.Field, correspondence))
    {
    }

    [JsonConstructor]
    public InspectionDiagnostic(
        string code,
        InspectionDiagnosticSeverity severity,
        InertString summary,
        InertString? correspondence)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);

        Code = code;
        Severity = severity;
        Summary = summary;
        Correspondence = correspondence;
    }

    public string Code { get; }

    public InspectionDiagnosticSeverity Severity { get; }

    [JsonConverter(typeof(InertStringJsonConverter))]
    public InertString Summary { get; }

    [JsonConverter(typeof(InertStringJsonConverter))]
    public InertString? Correspondence { get; }
}
