using DotnetInspector.Libraries;
using DotnetInspector.Platforms;

namespace DotnetInspector.PlatformHouse;

/// <summary>The source-issued role of one Library in a Platform population.</summary>
public enum PlatformPopulationMemberRole
{
    Focus,
    BindingSupport,
}

/// <summary>
/// Source-neutral exact family attribution for one Platform population member.
/// </summary>
public sealed class PlatformPopulationMemberAttribution :
    IEquatable<PlatformPopulationMemberAttribution>
{
    public PlatformPopulationMemberAttribution(
        PlatformFamilyTarget target,
        PlatformPopulationMemberRole role)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (!Enum.IsDefined(role))
            throw new ArgumentOutOfRangeException(nameof(role));

        Target = target;
        Role = role;
    }

    public PlatformFamilyTarget Target { get; }
    public PlatformPopulationMemberRole Role { get; }

    public bool Equals(PlatformPopulationMemberAttribution? other) =>
        other is not null
        && Target == other.Target
        && Role == other.Role;

    public override bool Equals(object? obj) =>
        obj is PlatformPopulationMemberAttribution other
        && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Target, Role);
}

/// <summary>
/// One realized Library and its exact source-issued Platform membership.
/// </summary>
public sealed class PlatformPopulationMember
{
    internal PlatformPopulationMember(
        LibraryReference library,
        PlatformPopulationMemberAttribution attribution)
    {
        ArgumentNullException.ThrowIfNull(library);
        ArgumentNullException.ThrowIfNull(attribution);
        Library = library;
        Attribution = attribution;
    }

    public LibraryReference Library { get; }
    public PlatformPopulationMemberAttribution Attribution { get; }
    public PlatformFamilyTarget Target => Attribution.Target;
    public PlatformPopulationMemberRole Role => Attribution.Role;
}
