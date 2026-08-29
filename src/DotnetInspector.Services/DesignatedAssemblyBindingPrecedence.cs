using System.Collections.Immutable;
using ILInspector.Metadata;

namespace DotnetInspector.Services;

internal static class DesignatedAssemblyBindingPrecedence
{
    internal static AssemblyBindingSelection? TrySelect(
        AssemblyReferenceIdentity requested,
        IEnumerable<ResolvedAssemblyReference> candidates,
        bool allowPlatformVersionRollForward = false,
        bool ignorePlatformVersion = false)
    {
        ArgumentNullException.ThrowIfNull(requested);
        ArgumentNullException.ThrowIfNull(candidates);

        var seen = new HashSet<AssemblyAcquisitionRegistration>(
            ReferenceEqualityComparer.Instance);
        ImmutableArray<ResolvedAssemblyReference> eligible =
        [
            .. candidates.Where(candidate =>
                candidate.Provenance switch
                {
                    AssemblyResolutionProvenance.DesignatedAsset =>
                        requested.MatchesCandidate(
                            candidate.Identity,
                            allowVersionRollForward: false,
                            ignoreVersion: true),
                    AssemblyResolutionProvenance.PlatformAsset =>
                        requested.MatchesCandidate(
                            candidate.Identity,
                            allowPlatformVersionRollForward,
                            ignorePlatformVersion),
                    _ => false,
                }
                && seen.Add(candidate.Registration)),
        ];
        ImmutableArray<ResolvedAssemblyReference> designated =
        [
            .. eligible.Where(candidate =>
                candidate.Provenance
                    is AssemblyResolutionProvenance.DesignatedAsset),
        ];

        return designated.Length switch
        {
            0 => null,
            1 => AssemblyBindingSelection.Found(
                designated[0],
                [
                    .. eligible.Where(candidate =>
                        candidate.Provenance
                            is AssemblyResolutionProvenance.PlatformAsset),
                ]),
            _ => AssemblyBindingSelection.Multiple(designated),
        };
    }
}
