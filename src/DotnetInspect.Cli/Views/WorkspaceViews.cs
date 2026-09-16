using Markout;

namespace DotnetInspect.Cli.Views;

[
    MarkoutSerializable(
        TitleProperty = nameof(Title),
        DescriptionProperty = nameof(Description))
]
public sealed class WorkspaceTopLevelInventoryView
{
    [MarkoutIgnore]
    public string Title => "Workspace";

    [MarkoutIgnore]
    public string? Description { get; init; }

    [MarkoutSection(Headless = true)]
    public List<WorkspaceTopLevelInventoryRow> Entries { get; init; } = [];
}

[MarkoutSerializable]
public sealed record WorkspaceTopLevelInventoryRow(
    string Kind,
    string Location,
    string State);

[MarkoutContext(typeof(WorkspaceTopLevelInventoryView))]
[MarkoutContext(typeof(WorkspaceTopLevelInventoryRow))]
public partial class WorkspaceViewContext : MarkoutSerializerContext;
