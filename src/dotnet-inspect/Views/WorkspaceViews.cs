using Markout;

namespace DotnetInspector.Views;

[MarkoutSerializable(
    TitleProperty = nameof(Title),
    DescriptionProperty = nameof(Description))]
public sealed class WorkspacePackageOccurrenceView
{
    [MarkoutIgnore]
    public string Title => "Workspace";

    [MarkoutIgnore]
    public string? Description =>
        Packages.Count == 0
            ? "No package occurrences."
            : null;

    [MarkoutSection(Headless = true)]
    public List<WorkspacePackageOccurrenceRow> Packages { get; init; } = [];
}

[MarkoutSerializable]
public sealed record WorkspacePackageOccurrenceRow
{
    public WorkspacePackageOccurrenceRow(
        string package,
        string version,
        string framework)
    {
        Package = LibraryViewText.Contain(package) ?? "";
        Version = LibraryViewText.Contain(version) ?? "";
        Framework = LibraryViewText.Contain(framework) ?? "";
    }

    public string Package { get; }

    public string Version { get; }

    public string Framework { get; }
}

[MarkoutContext(typeof(WorkspacePackageOccurrenceView))]
public partial class WorkspaceViewContext : MarkoutSerializerContext;
