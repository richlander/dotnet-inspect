using Markout;

namespace DotnetInspector.Views;

/// <summary>
/// Row for discovery output (sections or items within a section).
/// </summary>
[MarkoutSerializable]
public record DiscoveryRow(string Name, string Kind);

/// <summary>
/// Machine-readable discovery row for a query spanning multiple sections.
/// </summary>
[MarkoutSerializable]
public record ScopedDiscoveryRow(string Section, string Name, string Kind)
{
    /// <inheritdoc cref="LibraryViewText"/>
    public string Section { get; init; } = LibraryViewText.Contain(Section);

    /// <inheritdoc cref="LibraryViewText"/>
    public string Name { get; init; } = LibraryViewText.Contain(Name);

    /// <inheritdoc cref="LibraryViewText"/>
    public string Kind { get; init; } = LibraryViewText.Contain(Kind);
}

/// <summary>
/// List view for section-attributed machine discovery rows.
/// </summary>
[MarkoutSerializable(AutoFields = false)]
public class ScopedDiscoveryListView
{
    [MarkoutSection(Headless = true)]
    public List<ScopedDiscoveryRow> Items { get; set; } = [];
}

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
/// Tree-mode discovery view showing sections with their items as children.
/// </summary>
[MarkoutSerializable]
public class DiscoveryTreeView
{
    [MarkoutIgnoreInTable]
    public List<TreeNode> Sections { get; set; } = [];
}

[MarkoutContext(typeof(DiscoveryRow))]
[MarkoutContext(typeof(ScopedDiscoveryRow))]
[MarkoutContext(typeof(ScopedDiscoveryListView))]
[MarkoutContext(typeof(DiscoveryListView))]
[MarkoutContext(typeof(DiscoveryTreeView))]
public partial class DiscoveryContext : MarkoutSerializerContext
{
}
