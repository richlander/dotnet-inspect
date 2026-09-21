using System.Collections.Immutable;
using DotnetInspect.Cli.Sections;
using DotnetInspector.Queries;
using InertText;
using Markout;

namespace DotnetInspect.Cli.Views;

[MarkoutSerializable(TitleProperty = nameof(Title))]
public sealed class LibraryQueryView
{
    [MarkoutIgnore] public required InertString TitleText { get; init; }
    [MarkoutIgnore] public string Title => TitleText.ToString();

    [MarkoutSection(Name = LibraryQuerySections.LibrariesName)]
    public required List<LibraryQueryRow> Results { get; init; }

    [MarkoutSection(Name = LibraryQuerySections.QuerySummaryName)]
    public required List<LibraryQuerySummaryRow> QuerySummary { get; init; }
}

[MarkoutSerializable]
public sealed class LibraryQuerySummaryRow
{
    public LibraryQuerySummaryRow(
        int population,
        int scanned,
        int matches,
        int failures,
        string completion)
    {
        Population = population;
        Scanned = scanned;
        Matches = matches;
        Failures = failures;
        CompletionText = new(TextPolicy.Field, completion);
    }

    public int Population { get; }
    public int Scanned { get; }
    public int Matches { get; }
    public int Failures { get; }
    [MarkoutIgnore] public InertString CompletionText { get; }
    public string Completion => CompletionText.ToString();
}

[MarkoutSerializable(TitleProperty = nameof(Title))]
public sealed class EmptyLibraryQueryView
{
    [MarkoutIgnore] public required InertString TitleText { get; init; }
    [MarkoutIgnore] public string Title => TitleText.ToString();

    [MarkoutSection(Name = LibraryQuerySections.LibrariesName)]
    [MarkoutIgnoreInTable]
    public MarkoutTable Results { get; } = new(
        ["Library", "Version", "TFM", "Source", "References", "Path"],
        ["library", "version", "tfm", "source", "references", "path"],
        []);

    [MarkoutSection(Name = LibraryQuerySections.QuerySummaryName)]
    public required List<LibraryQuerySummaryRow> QuerySummary { get; init; }

    public static EmptyLibraryQueryView From(LibraryQueryView view) =>
        new()
        {
            TitleText = view.TitleText,
            QuerySummary = view.QuerySummary,
        };
}

[MarkoutSerializable]
public sealed class LibraryQueryRow
{
    public LibraryQueryRow(LibraryQueryMatch match)
    {
        LibraryText = match.Library;
        VersionText = match.Version;
        TargetFrameworkText = match.TargetFramework;
        SourceText = match.Source;
        PathText = match.Path;
        ReferenceItems = match.MatchedReferences;
    }

    [MarkoutIgnore] public InertString LibraryText { get; }
    [MarkoutIgnore] public InertString? VersionText { get; }
    [MarkoutIgnore] public InertString? TargetFrameworkText { get; }
    [MarkoutIgnore] public InertString SourceText { get; }
    [MarkoutIgnore] public InertString PathText { get; }
    [MarkoutIgnore]
    public ImmutableArray<InertString> ReferenceItems { get; }

    public string Library => LibraryText.ToString();
    public string Version => VersionText?.ToString() ?? "";
    [MarkoutPropertyName("TFM")]
    public string Tfm => TargetFrameworkText?.ToString() ?? "";
    public string Source => SourceText.ToString();
    public string References => string.Join(
        ", ",
        ReferenceItems.Select(item => item.ToString()));
    public string Path => PathText.ToString();
}
