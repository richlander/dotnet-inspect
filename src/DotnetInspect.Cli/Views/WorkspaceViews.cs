using Markout;

namespace DotnetInspect.Cli.Views;

[
    MarkoutSerializable(
        TitleProperty = nameof(Title),
        DescriptionProperty = nameof(Description))
]
public sealed class WorkspaceTopLevelInventoryView
{
    string? _description;

    [MarkoutIgnore]
    public string Title => "Workspace";

    [MarkoutIgnore]
    public string? Description
    {
        get => _description;
        init => _description = LibraryViewText.Contain(value);
    }

    [MarkoutSection(Headless = true)]
    public List<WorkspaceTopLevelInventoryRow> Entries { get; init; } = [];
}

[MarkoutSerializable]
public sealed record WorkspaceTopLevelInventoryRow
{
    public WorkspaceTopLevelInventoryRow(
        string kind,
        string location,
        string state)
    {
        Kind = LibraryViewText.Contain(kind) ?? "";
        Location = LibraryViewText.Contain(location) ?? "";
        State = LibraryViewText.Contain(state) ?? "";
    }

    public string Kind { get; }

    public string Location { get; }

    public string State { get; }
}

[MarkoutContext(typeof(WorkspaceTopLevelInventoryView))]
[MarkoutContext(typeof(WorkspaceTopLevelInventoryRow))]
public partial class WorkspaceViewContext : MarkoutSerializerContext;
