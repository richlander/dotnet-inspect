using Markout;

namespace DotnetInspect.Cli.Views;

/// <summary>
/// Row for discovery output (sections or items within a section).
/// </summary>
[MarkoutSerializable]
public record DiscoveryRow(string Name, string Kind);

/// <summary>
/// Detailed structural discovery row.
/// </summary>
[MarkoutSerializable]
public sealed record DetailedDiscoveryRow(
    string Name,
    string Kind,
    [property: MarkoutJoin(", ")] List<string> Formats);

/// <summary>
/// List view for discovery results. Rendered as a compact table, markdown table, or JSON array.
/// </summary>
[MarkoutSerializable(AutoFields = false)]
public class DiscoveryListView
{
    [MarkoutSection(Headless = true)]
    public List<DiscoveryRow> Items { get; set; } = [];
}

/// <summary>
/// List view for detailed structural discovery.
/// </summary>
[MarkoutSerializable(AutoFields = false)]
public sealed class DetailedDiscoveryView
{
    [MarkoutSection(Headless = true)]
    public List<DetailedDiscoveryRow> Items { get; set; } = [];
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
