namespace DotnetInspector.Queries;

/// <summary>
/// The acquisition cost a query authorizes.
/// </summary>
public enum InspectionCost
{
    /// <summary>Cheap, bounded, offline work.</summary>
    NetworkFree,

    /// <summary>Bounded work that may touch the network or warm optional data.</summary>
    Moderated,

    /// <summary>Potentially large, slow, or fan-out work that must be explicitly requested.</summary>
    Unbounded,
}
