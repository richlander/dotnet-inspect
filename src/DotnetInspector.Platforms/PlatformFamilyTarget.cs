namespace DotnetInspector.Platforms;

/// <summary>Identifies one exact platform family, framework, and version.</summary>
public sealed class PlatformFamilyTarget : IEquatable<PlatformFamilyTarget>
{
    /// <summary>Creates one exact platform family target.</summary>
    public PlatformFamilyTarget(
        PlatformFamily family,
        PlatformTargetFramework targetFramework,
        PlatformVersion version)
    {
        if (!Enum.IsDefined(family))
            throw new ArgumentOutOfRangeException(nameof(family));

        ArgumentNullException.ThrowIfNull(targetFramework);
        ArgumentNullException.ThrowIfNull(version);

        if (version.Major != targetFramework.Major
            || version.Minor != targetFramework.Minor)
        {
            throw new ArgumentException(
                "The platform version release band must match the target framework.",
                nameof(version));
        }

        Family = family;
        TargetFramework = targetFramework;
        Version = version;
    }

    /// <summary>The logical platform family.</summary>
    public PlatformFamily Family { get; }

    /// <summary>The canonical base target framework.</summary>
    public PlatformTargetFramework TargetFramework { get; }

    /// <summary>The exact platform version.</summary>
    public PlatformVersion Version { get; }

    /// <inheritdoc />
    public bool Equals(PlatformFamilyTarget? other) =>
        other is not null
        && Family == other.Family
        && TargetFramework == other.TargetFramework
        && Version == other.Version;

    /// <inheritdoc />
    public override bool Equals(object? obj) =>
        obj is PlatformFamilyTarget other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() =>
        HashCode.Combine(Family, TargetFramework, Version);

    /// <inheritdoc />
    public override string ToString() =>
        $"{Family}/{TargetFramework}/{Version}";

    /// <summary>Tests two exact family targets for equality.</summary>
    public static bool operator ==(
        PlatformFamilyTarget? left,
        PlatformFamilyTarget? right) =>
        Equals(left, right);

    /// <summary>Tests two exact family targets for inequality.</summary>
    public static bool operator !=(
        PlatformFamilyTarget? left,
        PlatformFamilyTarget? right) =>
        !Equals(left, right);
}
