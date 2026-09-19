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

    [MarkoutSection(Name = LibraryQuerySections.LibrariesName)]
    public required List<LibraryQueryRow> Results { get; init; }

    [MarkoutSection(Name = LibraryQuerySections.QuerySummaryName)]
    public required List<LibraryQuerySummaryRow> QuerySummary { get; init; }
}

[MarkoutSerializable]
public sealed class LibraryQueryRow
{
    public LibraryQueryRow(LibraryQueryMatch match)
    {
        Occurrence = match.Occurrence;
        LibraryText = Contain(match.Library.Name);
        VersionText = Contain(match.Library.Version?.ToString() ?? "");
        SourceText = Contain(match.Source ?? match.Provenance);
        AnswerText = Contain(string.Join("; ", match.Answers));
    }

    [MarkoutIgnore]
    public InertString LibraryText { get; }

    [MarkoutIgnore]
    public InertString VersionText { get; }

    [MarkoutIgnore]
    public InertString SourceText { get; }

    [MarkoutIgnore]
    public InertString AnswerText { get; }

    [MarkoutIgnore]
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
