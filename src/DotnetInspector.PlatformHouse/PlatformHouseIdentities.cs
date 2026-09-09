namespace DotnetInspector.PlatformHouse;

/// <summary>Opaque identity for one PlatformHouse request.</summary>
public sealed class PlatformHouseRequestIdentity
{
    private PlatformHouseRequestIdentity(string name) => Name = name;

    public string Name { get; }

    public static PlatformHouseRequestIdentity Create(string name) =>
        new(PlatformHouseIdentityName.Validate(name));

    public override string ToString() => Name;
}

/// <summary>Opaque identity for one standalone operation.</summary>
public sealed class PlatformStandaloneOperationIdentity
{
    private PlatformStandaloneOperationIdentity(string name) => Name = name;

    public string Name { get; }

    public static PlatformStandaloneOperationIdentity Create(string name) =>
        new(PlatformHouseIdentityName.Validate(name));

    public override string ToString() => Name;
}

/// <summary>Opaque identity for one host-authorized source plan.</summary>
public sealed class PlatformSourcePlanIdentity
{
    private PlatformSourcePlanIdentity(string name) => Name = name;

    public string Name { get; }

    public static PlatformSourcePlanIdentity Create(string name) =>
        new(PlatformHouseIdentityName.Validate(name));

    public override string ToString() => Name;
}

/// <summary>Opaque identity for one immutable source-policy generation.</summary>
public sealed class PlatformSourcePolicyGeneration
{
    private PlatformSourcePolicyGeneration(string name) => Name = name;

    public string Name { get; }

    public static PlatformSourcePolicyGeneration Create(string name) =>
        new(PlatformHouseIdentityName.Validate(name));

    public override string ToString() => Name;
}

/// <summary>Opaque identity for one host-authorized source capability.</summary>
public sealed class PlatformSourceCapabilityIdentity
{
    private PlatformSourceCapabilityIdentity(string name) => Name = name;

    public string Name { get; }

    public static PlatformSourceCapabilityIdentity Create(string name) =>
        new(PlatformHouseIdentityName.Validate(name));

    public override string ToString() => Name;
}

/// <summary>Opaque identity for one immutable source generation.</summary>
public sealed class PlatformSourceGeneration
{
    private PlatformSourceGeneration(string name) => Name = name;

    public string Name { get; }

    public static PlatformSourceGeneration Create(string name) =>
        new(PlatformHouseIdentityName.Validate(name));

    public override string ToString() => Name;
}

/// <summary>Opaque identity for one named target-selection policy.</summary>
public sealed class PlatformTargetSelectionPolicyIdentity
{
    private PlatformTargetSelectionPolicyIdentity(string name) => Name = name;

    public string Name { get; }

    public static PlatformTargetSelectionPolicyIdentity Create(string name) =>
        new(PlatformHouseIdentityName.Validate(name));

    public override string ToString() => Name;
}

/// <summary>Opaque identity for one immutable target-selection policy generation.</summary>
public sealed class PlatformTargetSelectionPolicyGeneration
{
    private PlatformTargetSelectionPolicyGeneration(string name) => Name = name;

    public string Name { get; }

    public static PlatformTargetSelectionPolicyGeneration Create(string name) =>
        new(PlatformHouseIdentityName.Validate(name));

    public override string ToString() => Name;
}

/// <summary>Resource-free identity for an owner-defined version requirement.</summary>
public sealed class PlatformVersionRequirementIdentity
{
    private PlatformVersionRequirementIdentity(string name) => Name = name;

    public string Name { get; }

    public static PlatformVersionRequirementIdentity Create(string name) =>
        new(PlatformHouseIdentityName.Validate(name));

    public override string ToString() => Name;
}

/// <summary>Resource-free identity for one Metadata-owned request.</summary>
public sealed class PlatformMetadataRequestIdentity
{
    private PlatformMetadataRequestIdentity(string name) => Name = name;

    public string Name { get; }

    internal static PlatformMetadataRequestIdentity Issue(string name) =>
        new(PlatformHouseIdentityName.Validate(name));

    public override string ToString() => Name;
}

/// <summary>Resource-free identity for platform-route prerequisites.</summary>
public sealed class PlatformRoutePrerequisitesIdentity
{
    private PlatformRoutePrerequisitesIdentity(string name) => Name = name;

    public string Name { get; }

    internal static PlatformRoutePrerequisitesIdentity Issue(string name) =>
        new(PlatformHouseIdentityName.Validate(name));

    public override string ToString() => Name;
}

/// <summary>Resource-free identity for one starting reference candidate.</summary>
public sealed class PlatformReferenceCandidateIdentity
{
    private PlatformReferenceCandidateIdentity(string name) => Name = name;

    public string Name { get; }

    internal static PlatformReferenceCandidateIdentity Issue(string name) =>
        new(PlatformHouseIdentityName.Validate(name));

    public override string ToString() => Name;
}

/// <summary>Resource-free identity for one documentation subject.</summary>
public sealed class PlatformDocumentationSubjectIdentity
{
    private PlatformDocumentationSubjectIdentity(string name) => Name = name;

    public string Name { get; }

    internal static PlatformDocumentationSubjectIdentity Issue(string name) =>
        new(PlatformHouseIdentityName.Validate(name));

    public override string ToString() => Name;
}

/// <summary>Resource-free identity for settled reference evidence.</summary>
public sealed class PlatformReferenceEvidenceIdentity
{
    private PlatformReferenceEvidenceIdentity(string name) => Name = name;

    public string Name { get; }

    internal static PlatformReferenceEvidenceIdentity Issue(string name) =>
        new(PlatformHouseIdentityName.Validate(name));

    public override string ToString() => Name;
}

/// <summary>Resource-free identity for one source coordinate.</summary>
public sealed class PlatformSourceCoordinateIdentity
{
    private PlatformSourceCoordinateIdentity(string name) => Name = name;

    public string Name { get; }

    public static PlatformSourceCoordinateIdentity Create(string name) =>
        new(PlatformHouseIdentityName.Validate(name));

    public override string ToString() => Name;
}

/// <summary>Resource-free identity for source-to-target correspondence.</summary>
public sealed class PlatformTargetCorrespondenceIdentity
{
    private PlatformTargetCorrespondenceIdentity(string name) => Name = name;

    public string Name { get; }

    public static PlatformTargetCorrespondenceIdentity Create(string name) =>
        new(PlatformHouseIdentityName.Validate(name));

    public override string ToString() => Name;
}

/// <summary>Resource-free identity for source-owner evidence.</summary>
public sealed class PlatformSourceEvidenceIdentity
{
    private PlatformSourceEvidenceIdentity(string name) => Name = name;

    public string Name { get; }

    public static PlatformSourceEvidenceIdentity Create(string name) =>
        new(PlatformHouseIdentityName.Validate(name));

    public override string ToString() => Name;
}

/// <summary>Opaque owner-issued reference-to-implementation correspondence.</summary>
public sealed class PlatformViewCorrespondenceIdentity
{
    private PlatformViewCorrespondenceIdentity(string name) => Name = name;

    public string Name { get; }

    internal static PlatformViewCorrespondenceIdentity Issue(string name) =>
        new(PlatformHouseIdentityName.Validate(name));

    public override string ToString() => Name;
}

/// <summary>Resource-free identity for one Metadata terminal outcome.</summary>
public sealed class PlatformMetadataOutcomeIdentity
{
    private PlatformMetadataOutcomeIdentity(string name) => Name = name;

    public string Name { get; }

    internal static PlatformMetadataOutcomeIdentity Issue(string name) =>
        new(PlatformHouseIdentityName.Validate(name));

    public override string ToString() => Name;
}

/// <summary>Resource-free identity for terminal non-success evidence.</summary>
public sealed class PlatformHouseTerminalEvidenceIdentity
{
    private PlatformHouseTerminalEvidenceIdentity(string name) => Name = name;

    public string Name { get; }

    public static PlatformHouseTerminalEvidenceIdentity Create(string name) =>
        new(PlatformHouseIdentityName.Validate(name));

    public override string ToString() => Name;
}

/// <summary>Resource-free identity for one ambiguous candidate.</summary>
public sealed class PlatformHouseCandidateIdentity
{
    private PlatformHouseCandidateIdentity(string name) => Name = name;

    public string Name { get; }

    public static PlatformHouseCandidateIdentity Create(string name) =>
        new(PlatformHouseIdentityName.Validate(name));

    public override string ToString() => Name;
}

/// <summary>
/// Opaque identity binding one resource-free completion receipt to its live
/// operation value.
/// </summary>
public sealed class PlatformHouseCompletionIdentity
{
    internal PlatformHouseCompletionIdentity()
    {
    }
}

static class PlatformHouseIdentityName
{
    public static string Validate(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return name;
    }
}
