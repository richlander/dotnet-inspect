using DotnetInspector.Platforms.Formats;
using ILInspector.Metadata;

namespace DotnetInspector.Platforms.Installed;

/// <summary>One exact installed shared-framework implementation coordinate.</summary>
public sealed record InstalledImplementationPlatformCoordinate
{
    public InstalledImplementationPlatformCoordinate(
        InstalledDotnetHiveIdentity hive,
        InstalledPlatformFamily family,
        PlatformVersion version)
    {
        ArgumentNullException.ThrowIfNull(hive);
        if (!Enum.IsDefined(family))
            throw new ArgumentOutOfRangeException(nameof(family));
        ArgumentNullException.ThrowIfNull(version);

        Hive = hive;
        Family = family;
        Version = version;
    }

    public InstalledDotnetHiveIdentity Hive { get; }
    public InstalledPlatformFamily Family { get; }
    public PlatformVersion Version { get; }
}

/// <summary>Finite work allowed for one implementation-platform realization.</summary>
public sealed record InstalledImplementationWorkBudget
{
    public InstalledImplementationWorkBudget(
        int maxFrameworks,
        int maxResolutionSteps,
        int maxManifestLibraries,
        int maxManifestAssets,
        int maxAssemblies,
        long maxBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maxFrameworks);
        ArgumentOutOfRangeException.ThrowIfNegative(maxResolutionSteps);
        ArgumentOutOfRangeException.ThrowIfNegative(maxManifestLibraries);
        ArgumentOutOfRangeException.ThrowIfNegative(maxManifestAssets);
        ArgumentOutOfRangeException.ThrowIfNegative(maxAssemblies);
        ArgumentOutOfRangeException.ThrowIfNegative(maxBytes);
        MaxFrameworks = maxFrameworks;
        MaxResolutionSteps = maxResolutionSteps;
        MaxManifestLibraries = maxManifestLibraries;
        MaxManifestAssets = maxManifestAssets;
        MaxAssemblies = maxAssemblies;
        MaxBytes = maxBytes;
    }

    public int MaxFrameworks { get; }
    public int MaxResolutionSteps { get; }
    public int MaxManifestLibraries { get; }
    public int MaxManifestAssets { get; }
    public int MaxAssemblies { get; }
    public long MaxBytes { get; }
}

/// <summary>One exact implementation-platform realization request.</summary>
public sealed record InstalledImplementationRealizationRequest
{
    public InstalledImplementationRealizationRequest(
        InstalledImplementationPlatformCoordinate coordinate,
        InstalledImplementationWorkBudget work)
    {
        ArgumentNullException.ThrowIfNull(coordinate);
        ArgumentNullException.ThrowIfNull(work);
        Coordinate = coordinate;
        Work = work;
    }

    public InstalledImplementationPlatformCoordinate Coordinate { get; }
    public InstalledImplementationWorkBudget Work { get; }
}

/// <summary>SHA-256 evidence for one immutable installed-source snapshot.</summary>
public sealed class InstalledPlatformContentDigest :
    IEquatable<InstalledPlatformContentDigest>
{
    private InstalledPlatformContentDigest(string value) => Value = value;

    public string Value { get; }

    internal static InstalledPlatformContentDigest FromBytes(
        ReadOnlySpan<byte> content) =>
        new(
            Convert.ToHexString(
                    System.Security.Cryptography.SHA256.HashData(content))
                .ToLowerInvariant());

    public bool Equals(InstalledPlatformContentDigest? other) =>
        other is not null
        && string.Equals(Value, other.Value, StringComparison.Ordinal);

    public override bool Equals(object? obj) =>
        obj is InstalledPlatformContentDigest other && Equals(other);

    public override int GetHashCode() =>
        StringComparer.Ordinal.GetHashCode(Value);

    public override string ToString() => Value;
}

/// <summary>One selected framework and its frozen manifest evidence.</summary>
public sealed record InstalledImplementationFramework
{
    internal InstalledImplementationFramework(
        PlatformFrameworkName name,
        PlatformVersion version,
        InstalledPlatformContentDigest? runtimeConfigurationDigest,
        InstalledPlatformContentDigest dependencyManifestDigest)
    {
        Name = name;
        Version = version;
        RuntimeConfigurationDigest = runtimeConfigurationDigest;
        DependencyManifestDigest = dependencyManifestDigest;
    }

    public PlatformFrameworkName Name { get; }
    public PlatformVersion Version { get; }
    public InstalledPlatformContentDigest? RuntimeConfigurationDigest { get; }
    public InstalledPlatformContentDigest DependencyManifestDigest { get; }
}

/// <summary>
/// One immutable managed assembly selected by an implementation manifest.
/// </summary>
public sealed class InstalledImplementationLibrary
{
    private readonly byte[] _content;

    internal InstalledImplementationLibrary(
        PlatformFrameworkName frameworkName,
        PlatformVersion frameworkVersion,
        PlatformManifestAssetCoordinate manifestCoordinate,
        AssemblyReferenceIdentity identity,
        InstalledPlatformContentDigest contentDigest,
        byte[] content)
    {
        FrameworkName = frameworkName;
        FrameworkVersion = frameworkVersion;
        ManifestCoordinate = manifestCoordinate;
        Identity = identity;
        ContentDigest = contentDigest;
        _content = content;
    }

    public PlatformFrameworkName FrameworkName { get; }
    public PlatformVersion FrameworkVersion { get; }
    public PlatformManifestAssetCoordinate ManifestCoordinate { get; }
    public AssemblyReferenceIdentity Identity { get; }
    public InstalledPlatformContentDigest ContentDigest { get; }
    public long ContentLength => _content.LongLength;

    public Stream OpenRead() =>
        new MemoryStream(_content, writable: false);
}

/// <summary>
/// Complete immutable installed implementation closure from one source
/// generation.
/// </summary>
public sealed record InstalledImplementationRealization
{
    internal InstalledImplementationRealization(
        InstalledPlatformSourceGeneration generation,
        InstalledImplementationPlatformCoordinate coordinate,
        IReadOnlyList<InstalledImplementationFramework> frameworks,
        IReadOnlyList<InstalledImplementationLibrary> libraries)
    {
        Generation = generation;
        Coordinate = coordinate;
        Frameworks = frameworks;
        Libraries = libraries;
    }

    public InstalledPlatformSourceGeneration Generation { get; }
    public InstalledImplementationPlatformCoordinate Coordinate { get; }
    public IReadOnlyList<InstalledImplementationFramework> Frameworks { get; }
    public IReadOnlyList<InstalledImplementationLibrary> Libraries { get; }
}
