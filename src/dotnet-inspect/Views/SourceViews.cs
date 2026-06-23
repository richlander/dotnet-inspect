using Markout;

namespace DotnetInspector.Views;

/// <summary>
/// Row for type-to-SourceLink URL sections on library/package/source views.
/// </summary>
[MarkoutSerializable]
public record SourceFileRow(string Type, [property: MarkoutSkipNull] string? Url);

/// <summary>
/// Row in the IL offset resolution "Source" section: the resolved source location.
/// </summary>
[MarkoutSerializable]
public record ILOffsetSourceRow(
    string File,
    int Line,
    [property: MarkoutSkipNull] string? Source);

/// <summary>
/// Key/value section for the IL offset query: the method token and the requested/matched offsets.
/// </summary>
[MarkoutSerializable(NamingPolicy = NamingPolicy.PascalCaseWords, FieldLayout = FieldLayout.Table)]
[MarkoutSkipNull]
public class ILOffsetInfoSection
{
    public string? Token { get; init; }

    [MarkoutPropertyName("IL Offset")]
    public string? ILOffset { get; init; }

    [MarkoutPropertyName("Matched Offset")]
    public string? MatchedOffset { get; init; }
}

/// <summary>
/// View model for the source command: IL offset resolution mode.
/// Mirrors the library-info view's verbosity discipline:
///   -v:q  → a single compact inline line (no sections)
///   -v:m  → the "Offset" section only (token + offsets)
///   -v:n+ → the "Offset" section plus the "Source" section (resolved file/line/URL)
/// At section verbosity there are no loose inline fields above the sections.
/// </summary>
[MarkoutSerializable(TitleProperty = nameof(Title), FieldLayout = FieldLayout.Inline)]
public class SourceILOffsetView
{
    [MarkoutIgnore] public string Title { get; set; } = "";

    // Compact representation (-v:q): token + offsets rendered inline on a single line.
    [MarkoutSkipNull] public string? Token { get; set; }

    [MarkoutSkipNull]
    [MarkoutPropertyName("IL Offset")]
    public string? ILOffset { get; set; }

    [MarkoutSkipNull]
    [MarkoutPropertyName("Matched Offset")]
    public string? MatchedOffset { get; set; }

    // Offset section (-v:m+): token + requested/matched offsets.
    [MarkoutSection(Name = "Offset")]
    public ILOffsetInfoSection? Offset { get; set; }

    // Source section (-v:n+): resolved file, line, and URL.
    [MarkoutSection(Name = "Source")]
    public List<ILOffsetSourceRow>? Location { get; set; }
}

[MarkoutContextOptions(SuppressTableWarnings = true)]
[MarkoutContext(typeof(SourceFileRow))]
[MarkoutContext(typeof(SourceILOffsetView))]
[MarkoutContext(typeof(ILOffsetSourceRow))]
[MarkoutContext(typeof(ILOffsetInfoSection))]
public partial class SourceViewContext : MarkoutSerializerContext
{
}
