using System.Runtime.CompilerServices;

using ILInspector.Metadata;
using ILInspector.MetadataPrimitives;

namespace DotnetInspector.Queries;

/// <summary>
/// Erasing identity of one exact Metadata acquisition registration. The weak
/// exact-object memoizer preserves reference equality without retaining the
/// registration's artifact authority. It never resolves or substitutes content.
/// </summary>
public sealed class NavigationRegistrationIdentity
{
    static readonly ConditionalWeakTable<AssemblyAcquisitionRegistration, NavigationRegistrationIdentity> Identities = new();

    NavigationRegistrationIdentity() { }

    internal static NavigationRegistrationIdentity From(AssemblyAcquisitionRegistration registration) =>
        Identities.GetValue(registration, static _ => new());
}

public sealed record NavigationAssemblyIdentity
{
    internal NavigationAssemblyIdentity(
        AssemblyAcquisitionRegistration registration,
        AssemblyReferenceIdentity assembly,
        AssemblyResolutionProvenance provenance)
    {
        Registration = NavigationRegistrationIdentity.From(registration);
        Assembly = assembly;
        Provenance = provenance;
    }

    public NavigationRegistrationIdentity Registration { get; }
    public AssemblyReferenceIdentity Assembly { get; }
    public AssemblyResolutionProvenance Provenance { get; }
}

public sealed record NavigationTypeIdentity
{
    internal NavigationTypeIdentity(NavigationRegistrationIdentity registration, MetadataTypeDefinitionName type)
    {
        Registration = registration;
        Type = type;
    }

    public NavigationRegistrationIdentity Registration { get; }
    public MetadataTypeDefinitionName Type { get; }
}

public sealed record NavigationMemberIdentity
{
    internal NavigationMemberIdentity(
        NavigationRegistrationIdentity registration, MetadataTypeDefinitionName declaringType, MemberAnchor member)
    {
        Registration = registration;
        DeclaringType = declaringType;
        Member = member;
    }

    public NavigationRegistrationIdentity Registration { get; }
    public MetadataTypeDefinitionName DeclaringType { get; }
    public MemberAnchor Member { get; }
}

/// <summary>Exact failure identity without retaining an exception's arbitrary object graph.</summary>
public sealed class NavigationExceptionIdentity
{
    static readonly ConditionalWeakTable<Exception, NavigationExceptionIdentity> Identities = new();

    NavigationExceptionIdentity() { }

    internal static NavigationExceptionIdentity From(Exception error) =>
        Identities.GetValue(error, static _ => new());
}

public sealed record NavigationExceptionEvidence(
    NavigationExceptionIdentity Identity,
    string Type,
    int HResult,
    string Message,
    string Detail);
