using DotnetInspector.Platforms;

namespace DotnetInspector.PlatformHouse;

/// <summary>How a framework-scoped demand obtains an exact platform version.</summary>
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
}

/// <summary>Framework range queried by one target-discovery stage.</summary>
public abstract class PlatformTargetDiscoveryScope
{
    private protected PlatformTargetDiscoveryScope()
    {
    }

    public sealed class AllFrameworks : PlatformTargetDiscoveryScope
    {
        public AllFrameworks()
        {
        }
    }

    public sealed class ExactFramework : PlatformTargetDiscoveryScope
    {
        public ExactFramework(PlatformTargetFramework targetFramework)
        {
            ArgumentNullException.ThrowIfNull(targetFramework);
            TargetFramework = targetFramework;
        }

        public PlatformTargetFramework TargetFramework { get; }
    }
}

/// <summary>One typed stage and its exact authorized discovery capabilities.</summary>
public sealed class PlatformTargetDiscoveryStage
{
    public PlatformTargetDiscoveryStage(
        PlatformTargetDiscoveryScope scope,
        IEnumerable<PlatformSourceCapabilityIdentity> capabilities)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(capabilities);

        PlatformSourceCapabilityIdentity[] snapshot = [.. capabilities];
        if (snapshot.Length == 0)
        {
            throw new ArgumentException(
                "A target-discovery stage requires at least one capability.",
                nameof(capabilities));
        }

        var seen = new HashSet<PlatformSourceCapabilityIdentity>(
            ReferenceEqualityComparer.Instance);
        for (int index = 0; index < snapshot.Length; index++)
        {
            PlatformSourceCapabilityIdentity capability = snapshot[index];
            ArgumentNullException.ThrowIfNull(
                capability,
                nameof(capabilities));
            if (!seen.Add(capability))
            {
                throw new ArgumentException(
                    $"Discovery capability at position {index} appears more than once.",
                    nameof(capabilities));
            }
        }

        Scope = scope;
        Capabilities = Array.AsReadOnly(snapshot);
    }

    public PlatformTargetDiscoveryScope Scope { get; }
    public IReadOnlyList<PlatformSourceCapabilityIdentity> Capabilities { get; }

    internal bool Contains(PlatformSourceCapabilityIdentity capability) =>
        Capabilities.Any(
            candidate => ReferenceEquals(candidate, capability));
}

/// <summary>
/// Named immutable versionless runtime policy with installed-preferred and
/// stable framework-scoped fallback stages.
/// </summary>
public sealed class PlatformVersionlessRuntimeTargetPolicy
{
    public PlatformVersionlessRuntimeTargetPolicy(
        PlatformTargetSelectionPolicyIdentity identity,
        PlatformTargetSelectionPolicyGeneration generation,
        PlatformVersion minimumPreferredVersion,
        PlatformTargetDiscoveryStage? preferred,
        PlatformTargetDiscoveryStage fallback)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(generation);
        ArgumentNullException.ThrowIfNull(minimumPreferredVersion);
        ArgumentNullException.ThrowIfNull(fallback);
        if (minimumPreferredVersion.IsPrerelease
            || minimumPreferredVersion.BuildMetadata is not null)
        {
            throw new ArgumentException(
                "The versionless runtime floor must be a stable version without build metadata.",
                nameof(minimumPreferredVersion));
        }
        if (preferred is not null
            && preferred.Scope
                is not PlatformTargetDiscoveryScope.AllFrameworks)
        {
            throw new ArgumentException(
                "The preferred runtime stage must discover every framework.",
                nameof(preferred));
        }
        if (fallback.Scope
            is not PlatformTargetDiscoveryScope.ExactFramework fallbackScope)
        {
            throw new ArgumentException(
                "The stable fallback stage requires one exact framework.",
                nameof(fallback));
        }
        if (fallbackScope.TargetFramework.Major
                != minimumPreferredVersion.Major
            || fallbackScope.TargetFramework.Minor
                != minimumPreferredVersion.Minor)
        {
            throw new ArgumentException(
                "The stable fallback framework must match the minimum version release band.",
                nameof(fallback));
        }

        var seen = new HashSet<PlatformSourceCapabilityIdentity>(
            ReferenceEqualityComparer.Instance);
        if (preferred is not null)
        {
            foreach (PlatformSourceCapabilityIdentity capability
                in preferred.Capabilities)
            {
                seen.Add(capability);
            }
        }
        foreach (PlatformSourceCapabilityIdentity capability
            in fallback.Capabilities)
        {
            if (!seen.Add(capability))
            {
                throw new ArgumentException(
                    "A discovery capability cannot occur in both target-selection stages.",
                    nameof(fallback));
            }
        }

        Identity = identity;
        Generation = generation;
        MinimumPreferredVersion = minimumPreferredVersion;
        Preferred = preferred;
        Fallback = fallback;
        DiscoveryCapabilities = Array.AsReadOnly(
            [
                .. preferred?.Capabilities
                    ?? Array.Empty<PlatformSourceCapabilityIdentity>(),
                .. fallback.Capabilities,
            ]);
    }

    public PlatformTargetSelectionPolicyIdentity Identity { get; }
    public PlatformTargetSelectionPolicyGeneration Generation { get; }
    public PlatformVersion MinimumPreferredVersion { get; }
    public PlatformTargetDiscoveryStage? Preferred { get; }
    public PlatformTargetDiscoveryStage Fallback { get; }
    public IReadOnlyList<PlatformSourceCapabilityIdentity>
        DiscoveryCapabilities { get; }

    internal PlatformTargetDiscoveryStage? StageFor(
        PlatformSourceCapabilityIdentity capability)
    {
        if (Preferred?.Contains(capability) == true)
            return Preferred;
        return Fallback.Contains(capability) ? Fallback : null;
    }
}

/// <summary>An exact target or an explicit request to select one.</summary>
public abstract class PlatformTargetDemand
{
    private protected PlatformTargetDemand(PlatformFamily family)
    {
        if (!Enum.IsDefined(family))
            throw new ArgumentOutOfRangeException(nameof(family));
        Family = family;
    }

    public PlatformFamily Family { get; }

    internal virtual IReadOnlyList<PlatformSourceCapabilityIdentity>
        AuthorizedDiscoveryCapabilities =>
            Array.Empty<PlatformSourceCapabilityIdentity>();
    internal virtual PlatformTargetDiscoveryBudget? DiscoveryWork => null;
    internal bool RequiresDiscovery => DiscoveryWork is not null;

    internal bool AuthorizesDiscoveryCapability(
        PlatformSourceCapabilityIdentity capability) =>
        AuthorizedDiscoveryCapabilities.Any(
            candidate => ReferenceEquals(candidate, capability));

    internal abstract bool CorrespondsToTarget(PlatformFamilyTarget target);

    internal virtual bool CorrespondsToDiscoveryCandidate(
        PlatformSourceCapabilityIdentity capability,
        PlatformFamilyTarget target) =>
        false;

    internal virtual bool IsEligibleSelection(
        PlatformSourceCapabilityIdentity capability,
        PlatformFamilyTarget target) =>
        CorrespondsToDiscoveryCandidate(capability, target);

    public sealed class Exact : PlatformTargetDemand
    {
        public Exact(PlatformFamilyTarget target)
            : base(ValidateTarget(target).Family)
        {
            Target = target;
        }

        public PlatformFamilyTarget Target { get; }

        internal override bool CorrespondsToTarget(
            PlatformFamilyTarget target) => Target == target;

        static PlatformFamilyTarget ValidateTarget(PlatformFamilyTarget target)
        {
            ArgumentNullException.ThrowIfNull(target);
            return target;
        }
    }

    /// <summary>One framework-scoped explicit version-selection demand.</summary>
    public sealed class Selecting : PlatformTargetDemand
    {
        public Selecting(
            PlatformFamily family,
            PlatformTargetFramework targetFramework,
            PlatformVersionSelectionDemand version,
            IEnumerable<PlatformSourceCapabilityIdentity>
                discoveryCapabilities,
            PlatformTargetDiscoveryBudget work)
            : base(family)
        {
            ArgumentNullException.ThrowIfNull(targetFramework);
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

            TargetFramework = targetFramework;
            Version = version;
            DiscoveryCapabilities = Array.AsReadOnly(capabilities);
            Work = work;
        }

        public PlatformTargetFramework TargetFramework { get; }
        public PlatformVersionSelectionDemand Version { get; }
        public IReadOnlyList<PlatformSourceCapabilityIdentity>
            DiscoveryCapabilities { get; }
        public PlatformTargetDiscoveryBudget Work { get; }

        internal override IReadOnlyList<PlatformSourceCapabilityIdentity>
            AuthorizedDiscoveryCapabilities => DiscoveryCapabilities;
        internal override PlatformTargetDiscoveryBudget DiscoveryWork => Work;
        internal override bool CorrespondsToTarget(
            PlatformFamilyTarget target) =>
            target.Family == Family
            && target.TargetFramework == TargetFramework;
        internal override bool CorrespondsToDiscoveryCandidate(
            PlatformSourceCapabilityIdentity capability,
            PlatformFamilyTarget target) =>
            AuthorizesDiscoveryCapability(capability)
            && CorrespondsToTarget(target);
    }

    /// <summary>One family-scoped named host-default demand.</summary>
    public sealed class FamilyDefault : PlatformTargetDemand
    {
        public FamilyDefault(
            PlatformFamily family,
            PlatformVersionlessRuntimeTargetPolicy policy,
            PlatformTargetDiscoveryBudget work)
            : base(family)
        {
            if (family != PlatformFamily.DotNetRuntime)
            {
                throw new ArgumentException(
                    "The versionless runtime policy requires the runtime family.",
                    nameof(family));
            }
            ArgumentNullException.ThrowIfNull(policy);
            ArgumentNullException.ThrowIfNull(work);

            Policy = policy;
            Work = work;
        }

        public PlatformVersionlessRuntimeTargetPolicy Policy { get; }
        public PlatformTargetDiscoveryBudget Work { get; }

        internal override IReadOnlyList<PlatformSourceCapabilityIdentity>
            AuthorizedDiscoveryCapabilities =>
                Policy.DiscoveryCapabilities;
        internal override PlatformTargetDiscoveryBudget DiscoveryWork => Work;
        internal override bool CorrespondsToTarget(
            PlatformFamilyTarget target) =>
            target.Family == Family;
        internal override bool CorrespondsToDiscoveryCandidate(
            PlatformSourceCapabilityIdentity capability,
            PlatformFamilyTarget target)
        {
            PlatformTargetDiscoveryStage? stage =
                Policy.StageFor(capability);
            if (stage is null || target.Family != Family)
                return false;
            return stage.Scope
                is PlatformTargetDiscoveryScope.AllFrameworks
                || stage.Scope
                    is PlatformTargetDiscoveryScope.ExactFramework exact
                    && target.TargetFramework == exact.TargetFramework;
        }

        internal override bool IsEligibleSelection(
            PlatformSourceCapabilityIdentity capability,
            PlatformFamilyTarget target)
        {
            PlatformTargetDiscoveryStage? stage =
                Policy.StageFor(capability);
            if (stage is null
                || !CorrespondsToDiscoveryCandidate(capability, target)
                || PlatformVersion.SemanticPrecedenceComparer.Compare(
                    target.Version,
                    Policy.MinimumPreferredVersion) < 0)
            {
                return false;
            }

            return ReferenceEquals(stage, Policy.Preferred)
                || !target.Version.IsPrerelease
                    && target.Version.BuildMetadata is null;
        }
    }
}
