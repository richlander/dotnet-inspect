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
