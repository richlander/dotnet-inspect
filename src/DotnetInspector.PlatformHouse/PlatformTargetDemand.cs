using DotnetInspector.Platforms;

namespace DotnetInspector.PlatformHouse;

/// <summary>How a selecting target demand obtains an exact platform version.</summary>
public abstract class PlatformVersionSelectionDemand
{
    private protected PlatformVersionSelectionDemand()
    {
    }

    public sealed class Requirement : PlatformVersionSelectionDemand
    {
        public Requirement(PlatformVersionRequirementIdentity identity)
        {
            ArgumentNullException.ThrowIfNull(identity);
            Identity = identity;
        }

        public PlatformVersionRequirementIdentity Identity { get; }
    }

    public sealed class HostDefault : PlatformVersionSelectionDemand
    {
        public HostDefault(
            PlatformTargetSelectionPolicyIdentity policy,
            PlatformTargetSelectionPolicyGeneration generation)
        {
            ArgumentNullException.ThrowIfNull(policy);
            ArgumentNullException.ThrowIfNull(generation);
            Policy = policy;
            Generation = generation;
        }

        public PlatformTargetSelectionPolicyIdentity Policy { get; }
        public PlatformTargetSelectionPolicyGeneration Generation { get; }
    }
}

/// <summary>An exact target or an explicit request to select one.</summary>
public abstract class PlatformTargetDemand
{
    private protected PlatformTargetDemand(
        PlatformFamily family,
        PlatformTargetFramework targetFramework)
    {
        if (!Enum.IsDefined(family))
            throw new ArgumentOutOfRangeException(nameof(family));

        ArgumentNullException.ThrowIfNull(targetFramework);
        Family = family;
        TargetFramework = targetFramework;
    }

    public PlatformFamily Family { get; }
    public PlatformTargetFramework TargetFramework { get; }

    public sealed class Exact : PlatformTargetDemand
    {
        public Exact(PlatformFamilyTarget target)
            : base(
                ValidateTarget(target).Family,
                target.TargetFramework)
        {
            Target = target;
        }

        public PlatformFamilyTarget Target { get; }

        static PlatformFamilyTarget ValidateTarget(PlatformFamilyTarget target)
        {
            ArgumentNullException.ThrowIfNull(target);
            return target;
        }
    }

    public sealed class Selecting : PlatformTargetDemand
    {
        public Selecting(
            PlatformFamily family,
            PlatformTargetFramework targetFramework,
            PlatformVersionSelectionDemand version,
            IEnumerable<PlatformSourceCapabilityIdentity> discoveryCapabilities,
            PlatformTargetDiscoveryBudget work)
            : base(family, targetFramework)
        {
            ArgumentNullException.ThrowIfNull(version);
            ArgumentNullException.ThrowIfNull(discoveryCapabilities);
            ArgumentNullException.ThrowIfNull(work);

            PlatformSourceCapabilityIdentity[] capabilities =
                [.. discoveryCapabilities];
            var seen = new HashSet<PlatformSourceCapabilityIdentity>(
                ReferenceEqualityComparer.Instance);
            for (int index = 0; index < capabilities.Length; index++)
            {
                PlatformSourceCapabilityIdentity capability =
                    capabilities[index];
                ArgumentNullException.ThrowIfNull(
                    capability,
                    nameof(discoveryCapabilities));
                if (!seen.Add(capability))
                {
                    throw new ArgumentException(
                        $"Discovery capability at position {index} appears more than once.",
                        nameof(discoveryCapabilities));
                }
            }

            Version = version;
            DiscoveryCapabilities = Array.AsReadOnly(capabilities);
            Work = work;
        }

        public PlatformVersionSelectionDemand Version { get; }
        public IReadOnlyList<PlatformSourceCapabilityIdentity>
            DiscoveryCapabilities { get; }
        public PlatformTargetDiscoveryBudget Work { get; }
    }
}
