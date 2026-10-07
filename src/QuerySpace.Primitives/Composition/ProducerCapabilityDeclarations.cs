using System.Collections.Immutable;

namespace QuerySpace.Composition;

/// <summary>
/// Opaque identity for one producer-owned capability domain.
/// </summary>
public sealed class ProducerCapabilityDomainIdentity
{
    ProducerCapabilityDomainIdentity()
    {
    }

    /// <summary>Creates one producer-owned capability domain.</summary>
    public static ProducerCapabilityDomainIdentity Create() => new();
}

/// <summary>
/// Opaque identity for one structural scope in a producer resource.
/// </summary>
public sealed class ProducerCapabilityScopeIdentity
{
    ProducerCapabilityScopeIdentity(
        ProducerCapabilityDomainIdentity domain,
        QuerySpaceResourceIdentity resource)
    {
        Domain = domain;
        Resource = resource;
    }

    /// <summary>Creates one scope in the supplied resource.</summary>
    public static ProducerCapabilityScopeIdentity Create(
        ProducerCapabilityDomainIdentity domain,
        QuerySpaceResourceIdentity resource)
    {
        ArgumentNullException.ThrowIfNull(domain);
        ArgumentNullException.ThrowIfNull(resource);
        return new(domain, resource);
    }

    internal ProducerCapabilityDomainIdentity Domain { get; }

    internal QuerySpaceResourceIdentity Resource { get; }
}

/// <summary>Opaque owner-issued capability identity.</summary>
public sealed class ProducerCapabilityIdentity
{
    ProducerCapabilityIdentity(
        ProducerCapabilityDomainIdentity domain)
    {
        Domain = domain;
    }

    /// <summary>Creates one capability identity.</summary>
    public static ProducerCapabilityIdentity Create(
        ProducerCapabilityDomainIdentity domain)
    {
        ArgumentNullException.ThrowIfNull(domain);
        return new(domain);
    }

    internal ProducerCapabilityDomainIdentity Domain { get; }
}

/// <summary>
/// Opaque owner-issued identity for one immutable capability-parameter set.
/// </summary>
public sealed class ProducerCapabilityParameterIdentity
{
    ProducerCapabilityParameterIdentity(
        ProducerCapabilityDomainIdentity domain)
    {
        Domain = domain;
    }

    /// <summary>Creates one capability-parameter identity.</summary>
    public static ProducerCapabilityParameterIdentity Create(
        ProducerCapabilityDomainIdentity domain)
    {
        ArgumentNullException.ThrowIfNull(domain);
        return new(domain);
    }

    internal ProducerCapabilityDomainIdentity Domain { get; }
}

/// <summary>Opaque owner-issued completion identity.</summary>
public sealed class ProducerCapabilityCompletionIdentity
{
    ProducerCapabilityCompletionIdentity(
        ProducerCapabilityDomainIdentity domain)
    {
        Domain = domain;
    }

    /// <summary>Creates one completion identity.</summary>
    public static ProducerCapabilityCompletionIdentity Create(
        ProducerCapabilityDomainIdentity domain)
    {
        ArgumentNullException.ThrowIfNull(domain);
        return new(domain);
    }

    internal ProducerCapabilityDomainIdentity Domain { get; }
}

/// <summary>
/// Opaque owner-issued identity for a result and non-success contract.
/// </summary>
public sealed class ProducerCapabilityOutcomeIdentity
{
    ProducerCapabilityOutcomeIdentity(
        ProducerCapabilityDomainIdentity domain)
    {
        Domain = domain;
    }

    /// <summary>Creates one outcome-contract identity.</summary>
    public static ProducerCapabilityOutcomeIdentity Create(
        ProducerCapabilityDomainIdentity domain)
    {
        ArgumentNullException.ThrowIfNull(domain);
        return new(domain);
    }

    internal ProducerCapabilityDomainIdentity Domain { get; }
}

/// <summary>Opaque owner-issued resource-lifetime identity.</summary>
public sealed class ProducerCapabilityResourceLifetimeIdentity
{
    ProducerCapabilityResourceLifetimeIdentity(
        ProducerCapabilityDomainIdentity domain)
    {
        Domain = domain;
    }

    /// <summary>Creates one execution resource-lifetime identity.</summary>
    public static ProducerCapabilityResourceLifetimeIdentity Create(
        ProducerCapabilityDomainIdentity domain)
    {
        ArgumentNullException.ThrowIfNull(domain);
        return new(domain);
    }

    internal ProducerCapabilityDomainIdentity Domain { get; }
}

/// <summary>Opaque owner-issued detached-result lifetime identity.</summary>
public sealed class ProducerCapabilityResultLifetimeIdentity
{
    ProducerCapabilityResultLifetimeIdentity(
        ProducerCapabilityDomainIdentity domain)
    {
        Domain = domain;
    }

    /// <summary>Creates one result-lifetime identity.</summary>
    public static ProducerCapabilityResultLifetimeIdentity Create(
        ProducerCapabilityDomainIdentity domain)
    {
        ArgumentNullException.ThrowIfNull(domain);
        return new(domain);
    }

    internal ProducerCapabilityDomainIdentity Domain { get; }
}

/// <summary>Opaque owner-issued provision identity.</summary>
public sealed class ProducerCapabilityProvisionIdentity
{
    ProducerCapabilityProvisionIdentity(
        ProducerCapabilityDomainIdentity domain)
    {
        Domain = domain;
    }

    /// <summary>Creates one provision identity.</summary>
    public static ProducerCapabilityProvisionIdentity Create(
        ProducerCapabilityDomainIdentity domain)
    {
        ArgumentNullException.ThrowIfNull(domain);
        return new(domain);
    }

    internal ProducerCapabilityDomainIdentity Domain { get; }
}

/// <summary>Opaque owner-issued covering-relationship identity.</summary>
public sealed class ProducerCapabilityCoverageIdentity
{
    ProducerCapabilityCoverageIdentity(
        ProducerCapabilityDomainIdentity domain)
    {
        Domain = domain;
    }

    /// <summary>Creates one covering-relationship identity.</summary>
    public static ProducerCapabilityCoverageIdentity Create(
        ProducerCapabilityDomainIdentity domain)
    {
        ArgumentNullException.ThrowIfNull(domain);
        return new(domain);
    }

    internal ProducerCapabilityDomainIdentity Domain { get; }
}

/// <summary>Opaque owner-issued projection identity.</summary>
public sealed class ProducerCapabilityProjectionIdentity
{
    ProducerCapabilityProjectionIdentity(
        ProducerCapabilityDomainIdentity domain)
    {
        Domain = domain;
    }

    /// <summary>Creates one covering projection identity.</summary>
    public static ProducerCapabilityProjectionIdentity Create(
        ProducerCapabilityDomainIdentity domain)
    {
        ArgumentNullException.ThrowIfNull(domain);
        return new(domain);
    }

    internal ProducerCapabilityDomainIdentity Domain { get; }
}

/// <summary>Opaque owner-issued strategy identity.</summary>
public sealed class ProducerCapabilityStrategyIdentity
{
    ProducerCapabilityStrategyIdentity(
        ProducerCapabilityDomainIdentity domain)
    {
        Domain = domain;
    }

    /// <summary>Creates one producer strategy identity.</summary>
    public static ProducerCapabilityStrategyIdentity Create(
        ProducerCapabilityDomainIdentity domain)
    {
        ArgumentNullException.ThrowIfNull(domain);
        return new(domain);
    }

    internal ProducerCapabilityDomainIdentity Domain { get; }
}

/// <summary>
/// Structural properties a provision or covering relationship certifies.
/// </summary>
[Flags]
public enum ProducerCapabilityProperties
{
    /// <summary>No additional structural property is required.</summary>
    None = 0,

    /// <summary>
    /// On a provision, its result has exact source cardinality. On a covering
    /// edge, the projection preserves the source's exact cardinality. A
    /// covering path has the property only when its provision and every edge
    /// declare it.
    /// </summary>
    ExactCardinality = 1,
}

/// <summary>
/// One not-yet-validated requirement in a complete producer demand set.
/// </summary>
public sealed record ProducerCapabilityRequirementCandidate(
    QuerySpaceRequestAssociationIdentity? Association,
    QuerySpaceResourceIdentity? Resource,
    ProducerCapabilityScopeIdentity? Scope,
    ProducerCapabilityIdentity? Capability,
    ProducerCapabilityCompletionIdentity? Completion,
    ProducerCapabilityOutcomeIdentity? Outcome,
    ProducerCapabilityProperties Properties = ProducerCapabilityProperties.None,
    ProducerCapabilityParameterIdentity? Parameters = null);

/// <summary>One validated producer capability requirement.</summary>
public sealed record ProducerCapabilityRequirement(
    QuerySpaceRequestAssociationIdentity Association,
    QuerySpaceResourceIdentity Resource,
    ProducerCapabilityScopeIdentity Scope,
    ProducerCapabilityIdentity Capability,
    ProducerCapabilityCompletionIdentity Completion,
    ProducerCapabilityOutcomeIdentity Outcome,
    ProducerCapabilityProperties Properties,
    ProducerCapabilityParameterIdentity? Parameters);

/// <summary>One producer-owned provision declaration.</summary>
public sealed record ProducerCapabilityProvisionDeclaration(
    ProducerCapabilityProvisionIdentity Identity,
    ProducerCapabilityScopeIdentity Scope,
    ProducerCapabilityIdentity Capability,
    ProducerCapabilityCompletionIdentity Completion,
    ProducerCapabilityOutcomeIdentity Outcome,
    ProducerCapabilityResourceLifetimeIdentity ResourceLifetime,
    ProducerCapabilityResultLifetimeIdentity ResultLifetime,
    ProducerCapabilityProperties Properties,
    ImmutableArray<ProducerCapabilityProvisionIdentity> Dependencies)
{
    /// <summary>Creates an immutable provision declaration.</summary>
    public static ProducerCapabilityProvisionDeclaration Create(
        ProducerCapabilityProvisionIdentity identity,
        ProducerCapabilityScopeIdentity scope,
        ProducerCapabilityIdentity capability,
        ProducerCapabilityCompletionIdentity completion,
        ProducerCapabilityOutcomeIdentity outcome,
        ProducerCapabilityResourceLifetimeIdentity resourceLifetime,
        ProducerCapabilityResultLifetimeIdentity resultLifetime,
        ProducerCapabilityProperties properties =
            ProducerCapabilityProperties.None,
        IReadOnlyList<ProducerCapabilityProvisionIdentity>? dependencies = null)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(capability);
        ArgumentNullException.ThrowIfNull(completion);
        ArgumentNullException.ThrowIfNull(outcome);
        ArgumentNullException.ThrowIfNull(resourceLifetime);
        ArgumentNullException.ThrowIfNull(resultLifetime);
        return new(
            identity,
            scope,
            capability,
            completion,
            outcome,
            resourceLifetime,
            resultLifetime,
            properties,
            dependencies is null
                ? []
                : [.. dependencies]);
    }
}

/// <summary>
/// One owner-certified directed covering relationship.
/// </summary>
public sealed record ProducerCapabilityCoverageDeclaration(
    ProducerCapabilityCoverageIdentity Identity,
    ProducerCapabilityScopeIdentity SourceScope,
    ProducerCapabilityIdentity SourceCapability,
    ProducerCapabilityCompletionIdentity SourceCompletion,
    ProducerCapabilityOutcomeIdentity SourceOutcome,
    ProducerCapabilityScopeIdentity TargetScope,
    ProducerCapabilityIdentity TargetCapability,
    ProducerCapabilityCompletionIdentity TargetCompletion,
    ProducerCapabilityOutcomeIdentity TargetOutcome,
    ProducerCapabilityProjectionIdentity Projection,
    ProducerCapabilityProperties Properties)
{
    /// <summary>Creates one immutable covering declaration.</summary>
    public static ProducerCapabilityCoverageDeclaration Create(
        ProducerCapabilityCoverageIdentity identity,
        ProducerCapabilityScopeIdentity sourceScope,
        ProducerCapabilityIdentity sourceCapability,
        ProducerCapabilityCompletionIdentity sourceCompletion,
        ProducerCapabilityOutcomeIdentity sourceOutcome,
        ProducerCapabilityScopeIdentity targetScope,
        ProducerCapabilityIdentity targetCapability,
        ProducerCapabilityCompletionIdentity targetCompletion,
        ProducerCapabilityOutcomeIdentity targetOutcome,
        ProducerCapabilityProjectionIdentity projection,
        ProducerCapabilityProperties properties =
            ProducerCapabilityProperties.None)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(sourceScope);
        ArgumentNullException.ThrowIfNull(sourceCapability);
        ArgumentNullException.ThrowIfNull(sourceCompletion);
        ArgumentNullException.ThrowIfNull(sourceOutcome);
        ArgumentNullException.ThrowIfNull(targetScope);
        ArgumentNullException.ThrowIfNull(targetCapability);
        ArgumentNullException.ThrowIfNull(targetCompletion);
        ArgumentNullException.ThrowIfNull(targetOutcome);
        ArgumentNullException.ThrowIfNull(projection);
        return new(
            identity,
            sourceScope,
            sourceCapability,
            sourceCompletion,
            sourceOutcome,
            targetScope,
            targetCapability,
            targetCompletion,
            targetOutcome,
            projection,
            properties);
    }
}
