using Markout;

namespace DotnetInspector.Views;

/// <summary>
/// View model for the source command: all-types listing mode.
/// Shows a table of types mapped to their source file URLs.
/// </summary>
[MarkoutSerializable(TitleProperty = nameof(Title), DescriptionProperty = nameof(Description), FieldLayout = FieldLayout.Inline)]
public class SourceListView
{
    [MarkoutIgnore] public string Title { get; set; } = "";
    [MarkoutIgnore] public string? Description { get; set; }

    // Header fields (shown at -v:m+)
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

    // Source file table
    [MarkoutSection(Name = "Source Files")]
    public List<SourceFileRow>? SourceFiles { get; set; }

    // With verify: adds Status column
    [MarkoutSection(Name = "Source Files")]
    public List<VerifiedSourceFileRow>? VerifiedSourceFiles { get; set; }
}

/// <summary>
/// View model for the source command: single-type mode.
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

    // Source metadata (shown at -v:m+)
    [MarkoutSkipNull] public string? Repository { get; set; }
    [MarkoutSkipNull] public string? Commit { get; set; }

    [MarkoutSkipNull]
    [MarkoutPropertyName("PDB")]
    public string? PdbStatus { get; set; }

    [MarkoutSkipNull] public string? Resolution { get; set; }

    // Source files table
    [MarkoutSection(Name = "Source Files")]
    public List<TypeSourceFileRow>? SourceFiles { get; set; }

    // With verify: adds Status column
    [MarkoutSection(Name = "Source Files")]
    public List<VerifiedTypeSourceFileRow>? VerifiedSourceFiles { get; set; }

    // Docs section (shown at -v:n+)
    [MarkoutSection(Name = "Documentation")]
    public List<MemberDocRow>? MemberDocs { get; set; }

    // Samples section (shown at -v:d)
    [MarkoutSection(Name = "Samples")]
    public List<SampleRow>? Samples { get; set; }
}

/// <summary>
/// Row in the all-types source file table.
/// </summary>
[MarkoutSerializable]
public record SourceFileRow(string Type, string File, [property: MarkoutSkipNull] string? Url);

/// <summary>
/// Row in the all-types source file table with verification status.
/// </summary>
[MarkoutSerializable]
public record VerifiedSourceFileRow(string Type, string File, [property: MarkoutSkipNull] string? Url, string Status);

/// <summary>
/// Row in the single-type source file table.
/// </summary>
[MarkoutSerializable]
public record TypeSourceFileRow(string File, [property: MarkoutSkipNull] string? Url, [property: MarkoutSkipNull] int? Line);

/// <summary>
/// Row in the single-type source file table with verification status.
/// </summary>
[MarkoutSerializable]
public record VerifiedTypeSourceFileRow(string File, [property: MarkoutSkipNull] string? Url, [property: MarkoutSkipNull] int? Line, string Status);

/// <summary>
/// Row for member documentation in the source detail view.
/// </summary>
[MarkoutSerializable]
public record MemberDocRow(string Member, string? Summary);
