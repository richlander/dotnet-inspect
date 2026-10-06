namespace DotnetInspector.Sections;

/// <summary>
/// Selects an owner-admitted hierarchy projection.
/// </summary>
public sealed record InspectionHierarchyRequest;

/// <summary>
/// Receives one ordered typed hierarchy synchronously without requiring the
/// owner to construct a retained presentation tree.
/// </summary>
/// <typeparam name="TNode">
/// The adopting owner's typed hierarchy-node union.
/// </typeparam>
public interface IInspectionHierarchySink<TNode>
{
    /// <summary>
    /// Writes one node and, when present, its direct children.
    /// </summary>
    /// <param name="node">The owner-issued node value.</param>
    /// <param name="isLastSibling">
    /// Whether this is the final node in the current child scope.
    /// </param>
    /// <param name="writeChildren">
    /// A synchronous callback that writes only this node's direct children.
    /// The sink must invoke a non-null callback exactly once before returning
    /// and must not retain it.
    /// </param>
    void WriteNode(
        TNode node,
        bool isLastSibling,
        Action<IInspectionHierarchySink<TNode>>? writeChildren = null);
}
