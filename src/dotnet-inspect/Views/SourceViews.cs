using Markout;

namespace DotnetInspector.Views;

/// <summary>
/// View model for the source command: all-types listing mode (markdown, -v:q+).
/// Shows header fields and a table of types → URLs.
/// </summary>
[MarkoutSerializable(TitleProperty = nameof(Title), DescriptionProperty = nameof(Description), FieldLayout = FieldLayout.Inline)]
public class SourceListView
{
    [MarkoutIgnore] public string Title { get; set; } = "";
    [MarkoutIgnore] public string? Description { get; set; }

    [MarkoutSkipNull] public string? Repository { get; set; }
    [MarkoutSkipNull] public string? Commit { get; set; }

    [MarkoutSkipNull]
    [MarkoutPropertyName("PDB")]
    public string? PdbStatus { get; set; }

    [MarkoutSkipNull] public string? Package { get; set; }
    [MarkoutSkipNull] public string? Version { get; set; }

    [MarkoutSkipNull]
    [MarkoutPropertyName("TFM")]
    public string? Tfm { get; set; }

    [MarkoutSkipNull] public int? Types { get; set; }

    // Source file table (without verify)
    [MarkoutSection(Name = "Source Files")]
    public List<SourceFileRow>? SourceFiles { get; set; }

    // With verify: adds Status column
    [MarkoutSection(Name = "Source Files")]
    public List<VerifiedSourceFileRow>? VerifiedSourceFiles { get; set; }

}

/// <summary>
/// View model for the source command: all-types oneline (default).
/// Just the table, no header fields.
/// </summary>
[MarkoutSerializable]
public class SourceOneLineView
{
    [MarkoutSection(Name = "Source Files")]
    public List<SourceFileRow>? SourceFiles { get; set; }

    [MarkoutSection(Name = "Source Files")]
    public List<VerifiedSourceFileRow>? VerifiedSourceFiles { get; set; }
}

/// <summary>
/// View model for the source command: single-type oneline (default).
/// URL-only rows (no Type column since the type is already known).
/// </summary>
[MarkoutSerializable]
public class SourceDetailOneLineView
{
    [MarkoutSection(Name = "Source Files")]
    public List<SourceUrlRow>? SourceFiles { get; set; }

    [MarkoutSection(Name = "Source Files")]
    public List<VerifiedSourceUrlRow>? VerifiedSourceFiles { get; set; }
}

/// <summary>
/// View model for the source command: single-type mode (-v:q+).
/// Shows detailed source info for one type including partial files.
/// </summary>
[MarkoutSerializable(TitleProperty = nameof(Title), DescriptionProperty = nameof(Description), FieldLayout = FieldLayout.Inline)]
public class SourceDetailView
{
    [MarkoutIgnore] public string Title { get; set; } = "";
    [MarkoutIgnore] public string? Description { get; set; }

    [MarkoutSkipNull] public string? Kind { get; set; }

    [MarkoutSkipNull]
    [MarkoutPropertyName("Library")]
    public string? Assembly { get; set; }

    [MarkoutSkipNull] public string? Package { get; set; }
    [MarkoutSkipNull] public string? Version { get; set; }
    [MarkoutSkipNull] public string? Source { get; set; }
    [MarkoutSkipNull] public int? Files { get; set; }

    [MarkoutSkipNull] public string? Repository { get; set; }
    [MarkoutSkipNull] public string? Commit { get; set; }

    [MarkoutSkipNull]
    [MarkoutPropertyName("PDB")]
    public string? PdbStatus { get; set; }

    [MarkoutSkipNull] public string? Resolution { get; set; }

    // Additional source files for partial types (primary is the Source field)
    [MarkoutSection(Name = "Source Files")]
    public List<SourceUrlRow>? AdditionalSourceFiles { get; set; }

    // With verify: show all files with status
    [MarkoutSection(Name = "Source Files")]
    public List<VerifiedSourceUrlRow>? VerifiedSourceFiles { get; set; }

    // Docs section (shown at -v:n+)
    [MarkoutSection(Name = "Documentation")]
    public List<MemberDocRow>? MemberDocs { get; set; }

    // Samples section (shown at -v:d)
    [MarkoutSection(Name = "Samples")]
    public List<SampleRow>? Samples { get; set; }
}

/// <summary>
/// Row in the all-types source file table: Type + Url.
/// </summary>
[MarkoutSerializable]
public record SourceFileRow(string Type, [property: MarkoutSkipNull] string? Url);

/// <summary>
/// Row in the all-types source file table with verification.
/// </summary>
[MarkoutSerializable]
public record VerifiedSourceFileRow(string Type, [property: MarkoutSkipNull] string? Url, string Status);

/// <summary>
/// Row for additional source files (partial types).
/// </summary>
[MarkoutSerializable]
public record SourceUrlRow(string Url);

/// <summary>
/// Row for verified source files with status.
/// </summary>
[MarkoutSerializable]
public record VerifiedSourceUrlRow(string Url, string Status);

/// <summary>
/// Row for member documentation in the source detail view.
/// </summary>
[MarkoutSerializable]
public record MemberDocRow(string Member, string? Summary);

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
[MarkoutContext(typeof(SourceListView))]
[MarkoutContext(typeof(SourceOneLineView))]
[MarkoutContext(typeof(SourceDetailOneLineView))]
[MarkoutContext(typeof(SourceDetailView))]
[MarkoutContext(typeof(SourceFileRow))]
[MarkoutContext(typeof(VerifiedSourceFileRow))]
[MarkoutContext(typeof(SourceUrlRow))]
[MarkoutContext(typeof(VerifiedSourceUrlRow))]
[MarkoutContext(typeof(MemberDocRow))]
[MarkoutContext(typeof(SourceILOffsetView))]
[MarkoutContext(typeof(ILOffsetSourceRow))]
[MarkoutContext(typeof(ILOffsetInfoSection))]
public partial class SourceViewContext : MarkoutSerializerContext
{
}
