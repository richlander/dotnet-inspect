using System.Collections.Immutable;
using System.Security.Cryptography;
using DotnetInspector.Packages;
using DotnetInspector.Platforms.Formats;
using ILInspector.Metadata;
using NuGetFetch;

namespace DotnetInspector.Platforms.Packages;

/// <summary>An exact RID-specific implementation-distribution coordinate.</summary>
public sealed record PackageImplementationPlatformCoordinate
{
    public const int MaximumRuntimeIdentifierLength = 256;

    public PackageImplementationPlatformCoordinate(
        PlatformFamilyTarget target,
        string runtimeIdentifier)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (!IsRuntimeIdentifier(runtimeIdentifier))
        {
            throw new ArgumentException(
                "The runtime identifier is not portable package-coordinate text.",
                nameof(runtimeIdentifier));
        }

        Target = target;
        RuntimeIdentifier = runtimeIdentifier;
    }

    public PlatformFamilyTarget Target { get; }
    public string RuntimeIdentifier { get; }
    public string PackageId =>
        RuntimePackageId(Target.Family, RuntimeIdentifier);

    internal static string RuntimePackageId(
        PlatformFamily family,
        string runtimeIdentifier) =>
        family switch
        {
            PlatformFamily.DotNetRuntime =>
                "microsoft.netcore.app.runtime." + runtimeIdentifier,
            PlatformFamily.AspNetCore =>
                "microsoft.aspnetcore.app.runtime." + runtimeIdentifier,
            _ => throw new ArgumentOutOfRangeException(nameof(family)),
        };

    private static bool IsRuntimeIdentifier(string? value)
    {
        if (string.IsNullOrEmpty(value)
            || value.Length > MaximumRuntimeIdentifierLength
            || !char.IsAsciiLetterOrDigit(value[0])
            || !char.IsAsciiLetterOrDigit(value[^1]))
        {
            return false;
        }

        foreach (char character in value)
        {
            if (!char.IsAsciiDigit(character)
                && !char.IsAsciiLetterLower(character)
                && character is not '.' and not '_' and not '-')
            {
                return false;
            }
        }

        return true;
    }
}

/// <summary>Finite work allowed for one package implementation closure.</summary>
public sealed record PackageImplementationWorkBudget
{
    public PackageImplementationWorkBudget(
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

/// <summary>SHA-256 evidence for one package-backed immutable snapshot.</summary>
public sealed class PackagePlatformContentDigest :
    IEquatable<PackagePlatformContentDigest>
{
    private PackagePlatformContentDigest(string value) => Value = value;

    public string Value { get; }

    internal static PackagePlatformContentDigest FromBytes(
        ReadOnlySpan<byte> content) =>
        new(Convert.ToHexStringLower(SHA256.HashData(content)));

    public bool Equals(PackagePlatformContentDigest? other) =>
        other is not null
        && string.Equals(Value, other.Value, StringComparison.Ordinal);

    public override bool Equals(object? obj) =>
        obj is PackagePlatformContentDigest other && Equals(other);

    public override int GetHashCode() =>
        StringComparer.Ordinal.GetHashCode(Value);

    public override string ToString() => Value;
}

/// <summary>One exact runtime pack selected into an implementation closure.</summary>
public sealed record PackageImplementationFramework
{
    internal PackageImplementationFramework(
        PlatformFrameworkName name,
        PlatformFamily family,
        PlatformVersion version,
        string packageId,
        string runtimeIdentifier,
        PackageAcquisitionCandidate candidate,
        ConfiguredPackageAuthority authority,
        PackageSourceResultIdentity source,
        PackageContentGenerationIdentity contentGeneration,
        PackagePayloadOrigin origin,
        ImmutableArray<PackageAuthorityFailure> packageFailures,
        PackagePlatformContentDigest runtimeConfigurationDigest,
        PackagePlatformContentDigest dependencyManifestDigest)
    {
        Name = name;
        Family = family;
        Version = version;
        PackageId = packageId;
        RuntimeIdentifier = runtimeIdentifier;
        Candidate = candidate;
        Authority = authority;
        Source = source;
        ContentGeneration = contentGeneration;
        Origin = origin;
        PackageFailures = packageFailures;
        RuntimeConfigurationDigest = runtimeConfigurationDigest;
        DependencyManifestDigest = dependencyManifestDigest;
    }

    public PlatformFrameworkName Name { get; }
    public PlatformFamily Family { get; }
    public PlatformVersion Version { get; }
    public string PackageId { get; }
    public string RuntimeIdentifier { get; }
    public PackageAcquisitionCandidate Candidate { get; }
    public ConfiguredPackageAuthority Authority { get; }
    public PackageSourceResultIdentity Source { get; }
    public PackageContentGenerationIdentity ContentGeneration { get; }
    public PackagePayloadOrigin Origin { get; }
    public ImmutableArray<PackageAuthorityFailure> PackageFailures { get; }
    public PackagePlatformContentDigest RuntimeConfigurationDigest { get; }
    public PackagePlatformContentDigest DependencyManifestDigest { get; }
}

/// <summary>One immutable managed assembly selected by a runtime manifest.</summary>
public sealed class PackageImplementationLibrary
{
    private readonly byte[] _content;

    internal PackageImplementationLibrary(
        PackageImplementationFramework framework,
        PlatformManifestAssetCoordinate manifestCoordinate,
        AssemblyReferenceIdentity identity,
        PackagePlatformContentDigest contentDigest,
        byte[] content)
    {
        Framework = framework;
        ManifestCoordinate = manifestCoordinate;
        Identity = identity;
        ContentDigest = contentDigest;
        _content = content;
    }

    public PackageImplementationFramework Framework { get; }
    public PlatformManifestAssetCoordinate ManifestCoordinate { get; }
    public AssemblyReferenceIdentity Identity { get; }
    public PackagePlatformContentDigest ContentDigest { get; }
    public long ContentLength => _content.LongLength;

    public Stream OpenRead() => new MemoryStream(_content, writable: false);
}

/// <summary>Complete immutable package-backed implementation closure.</summary>
public sealed record PackageImplementationRealization
{
    internal PackageImplementationRealization(
        PackagePlatformSourceGeneration generation,
        PackageImplementationPlatformCoordinate coordinate,
        ImmutableArray<PackageImplementationFramework> frameworks,
        ImmutableArray<PackageImplementationLibrary> libraries,
        long consumedBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(consumedBytes);
        long unassignedBytes = consumedBytes;
        foreach (PackageImplementationLibrary library in libraries)
        {
            if (library.ContentLength > unassignedBytes)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(consumedBytes));
            }
            unassignedBytes -= library.ContentLength;
        }
        Generation = generation;
        Coordinate = coordinate;
        Frameworks = frameworks;
        Libraries = libraries;
        ConsumedBytes = consumedBytes;
    }

    public PackagePlatformSourceGeneration Generation { get; }
    public PackageImplementationPlatformCoordinate Coordinate { get; }
    public ImmutableArray<PackageImplementationFramework> Frameworks { get; }
    public ImmutableArray<PackageImplementationLibrary> Libraries { get; }

    /// <summary>
    /// Bytes consumed by selected manifests and realized assemblies. Package
    /// archive admission and transport are outside this observation.
    /// </summary>
    public long ConsumedBytes { get; }
}
