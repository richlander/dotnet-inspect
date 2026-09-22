using System.Text.Json.Serialization;
using Markout;

namespace DotnetInspect.Cli.Views;

/// <summary>
/// Row for discovery output (sections or items within a section).
/// </summary>
[MarkoutSerializable]
public record DiscoveryRow(
    string Name,
    string Kind,
    [property: MarkoutSkipNull]
    [property: JsonIgnore(
        Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? Path = null);

/// <summary>
/// Detailed structural discovery row.
/// </summary>
[MarkoutSerializable]
public sealed record DetailedDiscoveryRow(
    string Name,
    string Kind,
    string Path,
    List<string> Formats,
    string? Shape = null,
    List<string>? Terminals = null)
{
    public string Name { get; init; } =
        LibraryViewText.Contain(Name);

    public string Kind { get; init; } =
        LibraryViewText.Contain(Kind);

    public string Path { get; init; } =
        LibraryViewText.Contain(Path);

    [MarkoutJoin(", ")]
    public List<string> Formats { get; init; } =
        [.. Formats.Select(format => LibraryViewText.Contain(format))];

    [MarkoutSkipNull]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Shape { get; init; } =
        Shape is null ? null : LibraryViewText.Contain(Shape);

    [MarkoutSkipNull]
    [MarkoutJoin(", ")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? Terminals { get; init; } =
        Terminals is null
            ? null
            : [.. Terminals.Select(
                terminal => LibraryViewText.Contain(terminal))];
}

/// <summary>
/// List view for discovery results. Rendered as a compact table, markdown table, or JSON array.
/// </summary>
[MarkoutSerializable(AutoFields = false)]
public class DiscoveryListView
{
    [MarkoutSection(Headless = true)]
    [MarkoutIgnoreColumnWhen(
        nameof(PathEmpty),
        nameof(DiscoveryRow.Path))]
    public List<DiscoveryRow> Items { get; set; } = [];

    public static bool PathEmpty(List<DiscoveryRow>? rows) =>
        rows is null
        || rows.All(static row => string.IsNullOrEmpty(row.Path));
}

/// <summary>
/// List view for detailed structural discovery.
/// </summary>
[MarkoutSerializable(AutoFields = false)]
public sealed class DetailedDiscoveryView
{
    [MarkoutSection(Headless = true)]
    [MarkoutIgnoreColumnWhen(
        nameof(CardinalityEmpty),
        nameof(DetailedDiscoveryRow.Shape))]
    [MarkoutIgnoreColumnWhen(
        nameof(CardinalityEmpty),
        nameof(DetailedDiscoveryRow.Terminals))]
    public List<DetailedDiscoveryRow> Items { get; set; } = [];

    public static bool CardinalityEmpty(
        List<DetailedDiscoveryRow>? rows) =>
        rows is null
        || rows.All(static row => row.Shape is null);
}

/// <summary>
/// Tree-mode discovery view showing sections with their items as children.
/// </summary>
[MarkoutSerializable]
public class DiscoveryTreeView
{
    [MarkoutIgnoreInTable]
    public List<TreeNode> Sections { get; set; } = [];
}

[MarkoutContext(typeof(DiscoveryRow))]
[MarkoutContext(typeof(DiscoveryListView))]
[MarkoutContext(typeof(DiscoveryTreeView))]
[MarkoutContext(typeof(DetailedDiscoveryRow))]
[MarkoutContext(typeof(DetailedDiscoveryView))]
public partial class DiscoveryContext : MarkoutSerializerContext
{
}
