namespace DotnetInspector.Services;

/// <summary>
/// The row lowering of a resolved package dependency graph.
/// </summary>
/// <remarks>
/// <para>
/// A dependency graph renders as a tree, but a tree is a rendering, not the payload. The row
/// projections (<c>--count</c> today, <c>--row</c>/<c>--rows</c> once the renderer can carry row
/// identity) address the graph's row lowering, and that lowering has to be declared by the model
/// rather than recovered by counting rendered lines.
/// </para>
/// <para>
/// One row is one dependency edge: the package depends on each root, and each node depends on each
/// of its children. That is the same rule the call graph uses, and it is what makes the two
/// renderings agree — every line of the rendered tree except the root line is exactly one edge, so
/// the tree a reader counts and the rows a projection addresses are the same sequence in the same
/// order.
/// </para>
/// <para>
/// This deliberately does not count the tree's top level only, which would answer a question about
/// the rendering rather than the payload, and it does not count the flat <c>Dependencies</c>
/// section, which is a different selection: that section lists declared dependencies across every
/// target framework group, while this lens resolves the transitive graph of one group.
/// </para>
/// </remarks>
public static class DependencyGraphRows
{
    /// <summary>
    /// The number of rows the graph rooted at <paramref name="roots"/> lowers to — one per
    /// dependency edge, counted over the whole resolved forest rather than its top level.
    /// </summary>
    public static int CountRows(IReadOnlyList<DependencyNode>? roots)
    {
        if (roots is null || roots.Count == 0)
            return 0;

        var total = 0;
        foreach (var node in roots)
            total += 1 + CountRows(node.Children);

        return total;
    }
}
