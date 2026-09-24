namespace DotnetInspector.Packages;

/// <summary>
/// Which package assets a consumer reads. Owned by
/// <c>docs/design/package-read-demand.md</c>.
/// </summary>
public enum PackageAssetDemand
{
    /// <summary>
    /// The compile surface and its implementation universe: depth commands
    /// such as <c>type</c>, <c>member</c>, <c>library</c>, and <c>graph</c>.
    /// </summary>
    SurfaceAndImplementation,

    /// <summary>
    /// The compile surface only (<c>ref/</c> when the package has it, else
    /// <c>lib/</c>): broad public-surface commands such as <c>find</c>.
    /// </summary>
    Surface,
}
