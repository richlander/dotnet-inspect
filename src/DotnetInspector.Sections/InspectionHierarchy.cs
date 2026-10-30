namespace DotnetInspector.Sections;

/// <summary>
/// Selects the spelling of one owner-issued hierarchy node.
/// </summary>
public enum InspectionHierarchyNodeSpelling
{
    Name,
    FullSpelling,
}

/// <summary>
/// Selects one parent-to-child population terminal.
/// </summary>
public abstract record InspectionHierarchyPopulationRequest
{
    private protected InspectionHierarchyPopulationRequest()
    {
    }

    /// <summary>
    /// Requests only the exact cardinality and ends this branch.
    /// </summary>
    public sealed record Count : InspectionHierarchyPopulationRequest;

    /// <summary>
    /// Requests child rows with one spelling and, optionally, another child
    /// population beneath each returned row.
    /// </summary>
    public sealed record Rows : InspectionHierarchyPopulationRequest
    {
        public Rows(
            InspectionHierarchyNodeSpelling spelling,
            InspectionHierarchyPopulationRequest? children = null)
        {
            if (!Enum.IsDefined(spelling))
                throw new ArgumentOutOfRangeException(nameof(spelling));

            Spelling = spelling;
            Children = children;
        }

        public InspectionHierarchyNodeSpelling Spelling { get; }
        public InspectionHierarchyPopulationRequest? Children { get; }
    }
}

/// <summary>
/// Selects one owner-admitted topology plus orthogonal node spelling and
/// population terminals.
/// </summary>
/// <typeparam name="TTopology">
/// The adopting owner's typed topology vocabulary.
/// </typeparam>
public sealed record InspectionHierarchyRequest<TTopology>
    where TTopology : struct, Enum
{
    public InspectionHierarchyRequest(
        TTopology topology,
        InspectionHierarchyNodeSpelling rootSpelling,
        InspectionHierarchyPopulationRequest children)
    {
        if (!Enum.IsDefined(topology))
            throw new ArgumentOutOfRangeException(nameof(topology));
        if (!Enum.IsDefined(rootSpelling))
        {
            throw new ArgumentOutOfRangeException(
                nameof(rootSpelling));
        }

        Topology = topology;
        RootSpelling = rootSpelling;
        Children = children
            ?? throw new ArgumentNullException(nameof(children));
    }

    public TTopology Topology { get; }
    public InspectionHierarchyNodeSpelling RootSpelling { get; }
    public InspectionHierarchyPopulationRequest Children { get; }
}

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
