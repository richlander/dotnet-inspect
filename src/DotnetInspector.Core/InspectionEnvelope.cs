using System.Collections.Immutable;
using InertText;

namespace DotnetInspector.Core;

/// <summary>
/// The host-neutral handoff for one completed inspection operation.
/// </summary>
/// <typeparam name="TContent">The owner-issued primary content result.</typeparam>
public sealed record InspectionEnvelope<TContent>
{
    public InspectionEnvelope(
        TContent content,
        InspectionShare share,
        IEnumerable<InspectionDiagnostic>? diagnostics = null)
    {
        ArgumentNullException.ThrowIfNull(content);
        Content = content;
        Share = share ?? throw new ArgumentNullException(nameof(share));
        Diagnostics = (diagnostics ?? [])
            .ToImmutableArray();
    }

    public TContent Content { get; }

    public InspectionShare Share { get; }

    public ImmutableArray<InspectionDiagnostic> Diagnostics { get; }

    public bool Equals(InspectionEnvelope<TContent>? other)
    {
        if (ReferenceEquals(this, other))
            return true;
        if (other is null
            || !EqualityComparer<TContent>.Default.Equals(
                Content,
                other.Content)
            || !ShareEquals(Share, other.Share)
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
        hash.Add(Content);
        hash.Add(ShareHashCode(Share));
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

    private static bool ShareEquals(
        InspectionShare left,
        InspectionShare right) =>
        (left, right) switch
        {
            (InspectionShare.Available a, InspectionShare.Available b) =>
                a.FullUrl == b.FullUrl,
            (
                InspectionShare.NonProjectable a,
                InspectionShare.NonProjectable b) =>
                a.Path == b.Path
                && a.Reason.ToString() == b.Reason.ToString(),
            _ => false,
        };

    private static int ShareHashCode(InspectionShare share) =>
        share switch
        {
            InspectionShare.Available available =>
                HashCode.Combine(0, available.FullUrl),
            InspectionShare.NonProjectable nonProjectable =>
                HashCode.Combine(
                    1,
                    nonProjectable.Path,
                    nonProjectable.Reason.ToString()),
            _ => throw new InvalidOperationException(
                "Unknown inspection Share outcome."),
        };
}

/// <summary>
/// The required portable-share outcome for an inspection.
/// </summary>
public abstract record InspectionShare
{
    private InspectionShare()
    {
    }

    /// <summary>
    /// A complete canonical production URL for the same inspection plan.
    /// </summary>
    public sealed record Available : InspectionShare
    {
        public Available(string fullUrl)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(fullUrl);
            if (!Uri.TryCreate(fullUrl, UriKind.Absolute, out Uri? uri)
                || !string.Equals(
                    uri.Scheme,
                    Uri.UriSchemeHttps,
                    StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    "An available inspection share must be a complete HTTPS URL.",
                    nameof(fullUrl));
            }

            FullUrl = fullUrl;
        }

        public string FullUrl { get; }
    }

    /// <summary>
    /// The semantic path and contained reason that could not be projected.
    /// </summary>
    public sealed record NonProjectable : InspectionShare
    {
        public NonProjectable(string path, string reason)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(path);
            ArgumentException.ThrowIfNullOrWhiteSpace(reason);

            Path = path;
            Reason = new InertString(TextPolicy.Field, reason);
        }

        public string Path { get; }

        public InertString Reason { get; }
    }
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
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(summary);
        if (correspondence is not null)
            ArgumentException.ThrowIfNullOrWhiteSpace(correspondence);

        Code = code;
        Severity = severity;
        Summary = new InertString(TextPolicy.Field, summary);
        Correspondence = correspondence is null
            ? null
            : new InertString(TextPolicy.Field, correspondence);
    }

    public string Code { get; }

    public InspectionDiagnosticSeverity Severity { get; }

    public InertString Summary { get; }

    public InertString? Correspondence { get; }
}
