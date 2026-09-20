using DotnetInspect.Cli.Sections;
using DotnetInspector.Queries;
using InertText;
using Markout;

namespace DotnetInspect.Cli.Views;

[MarkoutSerializable(TitleProperty = nameof(Title))]
public sealed class LibraryQueryView
{
    [MarkoutIgnore]
    public string Title => "Library Query";

    [MarkoutSection(
        Name = LibraryQuerySections.LibrariesName,
        IgnoreProperty = nameof(LibraryQueryRow.Occurrence))]
    public required List<LibraryQueryRow> Results { get; init; }

    [MarkoutSection(Name = LibraryQuerySections.QuerySummaryName)]
    public required List<LibraryQuerySummaryRow> QuerySummary { get; init; }
}

[MarkoutSerializable(TitleProperty = nameof(Title))]
public sealed class LibraryQueryStructuredView
{
    [MarkoutIgnore]
    public string Title => "Library Query";

    [MarkoutSection(Name = LibraryQuerySections.LibrariesName)]
    public required List<LibraryQueryRow> Results { get; init; }

    [MarkoutSection(Name = LibraryQuerySections.QuerySummaryName)]
    public required List<LibraryQuerySummaryRow> QuerySummary { get; init; }

    public static LibraryQueryStructuredView From(LibraryQueryView view) =>
        new()
        {
            Results = view.Results,
            QuerySummary = view.QuerySummary,
        };
}

[MarkoutSerializable]
public sealed class LibraryQueryRow
{
    public LibraryQueryRow(
        int occurrence,
        string library,
        string version,
        string source,
        string answer)
    {
        Occurrence = occurrence;
        LibraryText = Contain(library);
        VersionText = Contain(version);
        SourceText = Contain(source);
        AnswerText = Contain(answer);
    }

    [MarkoutIgnore]
    public InertString LibraryText { get; }

    [MarkoutIgnore]
    public InertString VersionText { get; }

    [MarkoutIgnore]
    public InertString SourceText { get; }

    [MarkoutIgnore]
    public InertString AnswerText { get; }

    public int Occurrence { get; }

    public string Library => LibraryText.ToString();
    public string Version => VersionText.ToString();
    public string Source => SourceText.ToString();
    public string Answer => AnswerText.ToString();

    private static InertString Contain(string value) =>
        new(TextPolicy.Field, value);
}

[MarkoutSerializable]
public sealed record LibraryQuerySummaryRow(
    int Population,
    int Evaluated,
    int Matches,
    int Failures,
    int CandidateLimit,
    LibraryQueryCompletionKind Status);

[MarkoutSerializable(TitleProperty = nameof(Title))]
public sealed class EmptyLibraryQueryView
{
    [MarkoutIgnore]
    public string Title => "Library Query";

    [MarkoutSection(Name = LibraryQuerySections.LibrariesName)]
    [MarkoutIgnoreInTable]
    public MarkoutTable Results { get; } = new(
        ["Library", "Version", "Source", "Answer"],
        ["library", "version", "source", "answer"],
        []);

    [MarkoutSection(Name = LibraryQuerySections.QuerySummaryName)]
    public required List<LibraryQuerySummaryRow> QuerySummary { get; init; }
}

[MarkoutSerializable(TitleProperty = nameof(Title))]
public sealed class EmptyLibraryQueryStructuredView
{
    [MarkoutIgnore]
    public string Title => "Library Query";

    [MarkoutSection(Name = LibraryQuerySections.LibrariesName)]
    [MarkoutIgnoreInTable]
    public MarkoutTable Results { get; } = new(
        ["Occurrence", "Library", "Version", "Source", "Answer"],
        ["occurrence", "library", "version", "source", "answer"],
        []);

    [MarkoutSection(Name = LibraryQuerySections.QuerySummaryName)]
    public required List<LibraryQuerySummaryRow> QuerySummary { get; init; }
}
