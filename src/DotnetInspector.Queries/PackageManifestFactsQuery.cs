using System.Collections.Immutable;
using DotnetInspector.Packages;
using DotnetInspector.Services;
using InertText;
using NuGet.Versioning;
using NuGetFetch;

namespace DotnetInspector.Queries;

/// <summary>
/// Immutable facts declared by one validated package manifest.
/// </summary>
public sealed record PackageManifestFacts(
    PackageSourceCoordinate Coordinate,
    string ManifestVersion,
    InertString? Description,
    string? Authors,
    string? Repository,
    string? RepositoryType,
    string? RepositoryCommit,
    string? License,
    string? LicenseUrl,
    ImmutableArray<string> PackageTypes,
    bool IsToolPackage,
    string? ReadmeFile,
    ImmutableArray<DeclaredPackageDependencyGroup> DependencyGroups);

/// <summary>
/// The typed outcome of projecting facts from one exact package manifest.
/// </summary>
public abstract record PackageManifestFactsResult
{
    private PackageManifestFactsResult()
    {
    }

    public sealed record Available(
        PackageManifestFacts Value) : PackageManifestFactsResult;

    public sealed record Failed(
        Exception Error) : PackageManifestFactsResult;
}

/// <summary>
/// Projects immutable package facts from bounded exact nuspec content.
/// </summary>
/// <remarks>
/// The query owns manifest identity and dependency-contract validation, but
/// not acquisition. Callers supply the expected coordinate and exact manifest
/// bytes so Browser/Wasm and CLI hosts share one projection without granting
/// this query network or package-payload capabilities.
/// </remarks>
public static class PackageManifestFactsQuery
{
    public const int MaxManifestBytes = 1024 * 1024;
    public const int MaxManifestCharacters = 512 * 1024;
    public const int MaxScalarCharacters = 32 * 1024;
    public const int MaxPackageTypes = 128;
    public const int MaxDependencyGroups = 1024;
    public const int MaxDependencies = 4096;

    public static InspectionQuery<PackageManifestFactsResult> Definition
        { get; } =
        new("Package manifest facts", InspectionCost.NetworkFree);

    public static PackageManifestFactsResult Execute(
        ReadOnlyMemory<byte> manifestBytes,
        PackageSourceCoordinate expectedCoordinate)
    {
        ArgumentNullException.ThrowIfNull(expectedCoordinate);

        try
        {
            if (manifestBytes.Length > MaxManifestBytes)
            {
                throw new InvalidDataException(
                    "The package manifest exceeds the configured size limit.");
            }

            using var buffer = new MemoryStream(
                manifestBytes.ToArray(),
                writable: false);
            NuspecData nuspec = NuspecParser.Parse(
                buffer,
                MaxManifestCharacters);
            ValidateIdentity(nuspec, expectedCoordinate);
            ValidateScalarFacts(nuspec);

            ImmutableArray<DeclaredPackageDependencyGroup> dependencyGroups =
                ProjectDependencyGroups(nuspec.DependencyGroups);
            return new PackageManifestFactsResult.Available(
                new PackageManifestFacts(
                    expectedCoordinate,
                    nuspec.ManifestVersion ?? "nuspec",
                    nuspec.Description,
                    nuspec.Authors,
                    nuspec.Repository,
                    nuspec.RepositoryType,
                    nuspec.RepositoryCommit,
                    nuspec.License,
                    nuspec.LicenseUrl,
                    [.. nuspec.PackageTypes ?? []],
                    nuspec.IsToolPackage,
                    nuspec.ReadmeFile,
                    dependencyGroups));
        }
        catch (Exception exception) when (
            exception is InvalidDataException
                or NuspecParseException)
        {
            return new PackageManifestFactsResult.Failed(exception);
        }
    }

    private static void ValidateIdentity(
        NuspecData nuspec,
        PackageSourceCoordinate expectedCoordinate)
    {
        if (string.IsNullOrWhiteSpace(nuspec.PackageName)
            || !nuspec.PackageName.Equals(
                expectedCoordinate.PackageId,
                StringComparison.OrdinalIgnoreCase)
            || !VersionsEqual(
                nuspec.Version,
                expectedCoordinate.Version))
        {
            throw new InvalidDataException(
                "The package manifest identity does not match the requested package.");
        }
    }

    private static ImmutableArray<DeclaredPackageDependencyGroup>
        ProjectDependencyGroups(List<DependencyGroup>? groups)
    {
        if (groups is null)
            return [];
        if (groups.Count > MaxDependencyGroups)
        {
            throw new InvalidDataException(
                "The package manifest contains too many dependency groups.");
        }

        var builder =
            ImmutableArray.CreateBuilder<DeclaredPackageDependencyGroup>(
                groups.Count);
        int dependencyCount = 0;
        foreach (DependencyGroup group in groups)
        {
            ValidateScalar(group.TargetFramework);
            var dependencies =
                ImmutableArray.CreateBuilder<DeclaredPackageDependency>(
                    group.Dependencies.Count);
            foreach (PackageDependency dependency in group.Dependencies)
            {
                dependencyCount++;
                if (dependencyCount > MaxDependencies)
                {
                    throw new InvalidDataException(
                        "The package manifest contains too many dependencies.");
                }

                if (!PackageCoordinateResolver.IsCanonicalPackageId(
                        dependency.Id))
                {
                    throw new InvalidDataException(
                        "The package manifest contains an invalid dependency id.");
                }

                ValidateScalar(dependency.Id);
                ValidateScalar(dependency.Version);
                PackageDependencyVersionRange.Validate(dependency.Version);
                dependencies.Add(
                    new DeclaredPackageDependency(
                        dependency.Id,
                        dependency.Version));
            }

            builder.Add(
                new DeclaredPackageDependencyGroup(
                    group.TargetFramework,
                    dependencies.MoveToImmutable(),
                    group.IsImplicitManifestGroup));
        }

        return builder.MoveToImmutable();
    }

    private static void ValidateScalarFacts(NuspecData nuspec)
    {
        ValidateScalar(nuspec.ManifestVersion);
        ValidateScalar(nuspec.Authors);
        ValidateScalar(nuspec.Repository);
        ValidateScalar(nuspec.RepositoryType);
        ValidateScalar(nuspec.RepositoryCommit);
        ValidateScalar(nuspec.License);
        ValidateScalar(nuspec.LicenseUrl);
        ValidateScalar(nuspec.ReadmeFile);
        ValidateScalar(nuspec.Description?.ToString());

        if (nuspec.PackageTypes is { Count: > MaxPackageTypes })
        {
            throw new InvalidDataException(
                "The package manifest contains too many package types.");
        }

        foreach (string packageType in nuspec.PackageTypes ?? [])
            ValidateScalar(packageType);
    }

    private static void ValidateScalar(string? value)
    {
        if (value is { Length: > MaxScalarCharacters })
        {
            throw new InvalidDataException(
                "The package manifest contains a scalar value that exceeds the configured limit.");
        }
    }

    private static bool VersionsEqual(
        string? declaredVersion,
        string requestedVersion) =>
        NuGetVersion.TryParse(
            declaredVersion,
            out NuGetVersion? declared)
        && NuGetVersion.TryParse(
            requestedVersion,
            out NuGetVersion? requested)
        && declared.ToNormalizedString().Equals(
            requested.ToNormalizedString(),
            StringComparison.OrdinalIgnoreCase);
}
