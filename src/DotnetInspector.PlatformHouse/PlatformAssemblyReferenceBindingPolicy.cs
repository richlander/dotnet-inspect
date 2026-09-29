using ILInspector.Metadata;

namespace DotnetInspector.PlatformHouse;

/// <summary>
/// Platform identity policy for binding one arbitrary source assembly
/// reference against an already selected target.
/// </summary>
public static class PlatformAssemblyReferenceBindingPolicy
{
    public static bool MatchesCandidate(
        PlatformLibraryDemand.AssemblyReferenceBinding demand,
        AssemblyReferenceIdentity candidate)
    {
        ArgumentNullException.ThrowIfNull(demand);
        ArgumentNullException.ThrowIfNull(candidate);
        return demand.Identity.MatchesCandidate(
            candidate,
            ignoreVersion: true);
    }
}
