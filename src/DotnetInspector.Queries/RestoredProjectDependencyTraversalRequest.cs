namespace DotnetInspector.Queries;

/// <summary>
/// The complete immutable request for one restored-project dependency traversal: the optional
/// exact target request the facts owner already defines, and an optional maximum root-relative
/// depth. See <c>docs/design/restored-project-dependency-traversal.md</c> for the contract.
/// </summary>
/// <remarks>
/// Depth is admission over already-materialized assets evidence. It never causes acquisition,
/// filesystem access, restore, or additional parsing.
/// </remarks>
public sealed record RestoredProjectDependencyTraversalRequest
{
    public RestoredProjectDependencyTraversalRequest(
        RestoredProjectTargetRequest? target = null,
        int? maximumDepth = null)
    {
        if (maximumDepth is < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumDepth),
                maximumDepth,
                "A maximum traversal depth cannot be negative.");
        }

        Target = target;
        MaximumDepth = maximumDepth;
    }

    /// <summary>The exact target request handed to the shared restored-project selection rule.</summary>
    public RestoredProjectTargetRequest? Target { get; }

    /// <summary>
    /// The maximum admitted root-relative relationship distance, or <see langword="null"/> for the
    /// whole selected graph. Depth <c>0</c> admits the root node and no relationship.
    /// </summary>
    public int? MaximumDepth { get; }
}
