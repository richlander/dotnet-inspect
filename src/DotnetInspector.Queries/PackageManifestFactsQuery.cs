using System.Collections.Immutable;
using DotnetInspector.Packages;
using DotnetInspector.Services;
using InertText;
using NuGet.Versioning;
using NuGetFetch;

namespace DotnetInspector.Queries;

/// <summary>Identifies the evidence used to establish a manifest coordinate.</summary>
public enum PackageManifestIdentityProvenance
{
    /// <summary>The manifest identity matched an independently supplied coordinate.</summary>
    ExpectedCoordinate,

    /// <summary>The coordinate was validated and normalized from the manifest itself.</summary>
    SelfAttested,
}

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
    ImmutableArray<DeclaredPackageDependencyGroup> DependencyGroups)
{
    public PackageManifestIdentityProvenance IdentityProvenance { get; init; } =
        PackageManifestIdentityProvenance.ExpectedCoordinate;
}

/// <summary>
/// The stable reason one package manifest could not be projected.
/// </summary>
public enum PackageManifestFailureReason
{
    MalformedXml,
    UnsupportedDocumentShape,
    IdentityMismatch,
    InvalidDependencyContract,
    ConfiguredLimitExceeded,
    InvalidIdentityContract,
}

/// <summary>
/// A content-free package-manifest projection failure.
/// </summary>
/// <remarks>
/// <c>FailureMessage_IsStableForEveryReason</c>,
/// <c>FailureMessage_IsSafeForUnknownFutureReason</c>, and the hostile-input
/// execution tests gate this diagnostic contract.
/// </remarks>
public sealed record PackageManifestFailure
{
    public PackageManifestFailure(
        PackageManifestFailureReason reason,
        int lineNumber = 0,
        int linePosition = 0)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(lineNumber);
        ArgumentOutOfRangeException.ThrowIfNegative(linePosition);
        Reason = reason;
        LineNumber = lineNumber;
        LinePosition = linePosition;
    }

    public PackageManifestFailureReason Reason { get; }

    /// <summary>
    /// The one-based XML line where parsing failed, or zero when unavailable.
    /// </summary>
    public int LineNumber { get; }

    /// <summary>
    /// The one-based XML position where parsing failed, or zero when unavailable.
    /// </summary>
    public int LinePosition { get; }

    public string Message => Reason switch
    {
        PackageManifestFailureReason.MalformedXml
            when LineNumber > 0 && LinePosition > 0 =>
            $"Package manifest is not well-formed XML at line {LineNumber}, position {LinePosition}.",
        PackageManifestFailureReason.MalformedXml =>
            "Package manifest is not well-formed XML.",
        PackageManifestFailureReason.UnsupportedDocumentShape =>
            "The package manifest has an unsupported document shape or namespace.",
        PackageManifestFailureReason.IdentityMismatch =>
            "The package manifest identity does not match the requested package.",
        PackageManifestFailureReason.InvalidIdentityContract =>
            "The package manifest contains an invalid package identity.",
        PackageManifestFailureReason.InvalidDependencyContract =>
            "The package manifest contains an invalid dependency declaration.",
        PackageManifestFailureReason.ConfiguredLimitExceeded =>
            "The package manifest exceeds a configured resource limit.",
        _ => "The package manifest could not be projected.",
    };
}

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
        PackageManifestFailure Failure) : PackageManifestFactsResult;
}

/// <summary>
/// Projects immutable package facts from bounded exact nuspec content.
/// </summary>
/// <remarks>
/// The query owns manifest identity and dependency-contract validation, but
/// not acquisition. Package/source callers supply an expected coordinate;
/// direct-content callers explicitly request self-attested identity. Both
/// paths supply exact manifest bytes so Browser/Wasm and CLI hosts share one
/// projection without granting this query network or package-payload
/// capabilities.
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
        return ExecuteCore(manifestBytes, expectedCoordinate);
    }

    /// <summary>
    /// Projects manifest facts using a validated coordinate declared by the
    /// manifest itself.
    /// </summary>
    public static PackageManifestFactsResult ExecuteSelfAttested(
        ReadOnlyMemory<byte> manifestBytes) =>
        ExecuteCore(manifestBytes, expectedCoordinate: null);

    private static PackageManifestFactsResult ExecuteCore(
        ReadOnlyMemory<byte> manifestBytes,
        PackageSourceCoordinate? expectedCoordinate)
    {
        try
        {
            if (manifestBytes.Length > MaxManifestBytes)
            {
                throw Failure(
                    PackageManifestFailureReason.ConfiguredLimitExceeded);
            }

            using var buffer = new MemoryStream(
                manifestBytes.ToArray(),
                writable: false);
            NuspecData nuspec = NuspecParser.Parse(
                buffer,
                MaxManifestCharacters);
            PackageSourceCoordinate coordinate;
            PackageManifestIdentityProvenance identityProvenance;
            if (expectedCoordinate is null)
            {
                coordinate = CreateSelfAttestedCoordinate(nuspec);
                identityProvenance =
                    PackageManifestIdentityProvenance.SelfAttested;
            }
            else
            {
                ValidateIdentity(nuspec, expectedCoordinate);
                coordinate = expectedCoordinate;
                identityProvenance =
                    PackageManifestIdentityProvenance.ExpectedCoordinate;
            }

            ValidateScalarFacts(nuspec);

            ImmutableArray<DeclaredPackageDependencyGroup> dependencyGroups =
                ProjectDependencyGroups(nuspec.DependencyGroups);
            return new PackageManifestFactsResult.Available(
                new PackageManifestFacts(
                    coordinate,
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
                    dependencyGroups)
                {
                    IdentityProvenance = identityProvenance,
                });
        }
        catch (ManifestValidationException exception)
        {
            return Failed(exception.Reason);
        }
        catch (NuspecParseException exception)
        {
            return Failed(
                PackageManifestFailureReason.MalformedXml,
                exception.LineNumber,
                exception.LinePosition);
        }
        catch (InvalidDataException)
        {
            return Failed(
                PackageManifestFailureReason.UnsupportedDocumentShape);
        }
    }

    private static PackageSourceCoordinate CreateSelfAttestedCoordinate(
        NuspecData nuspec)
    {
        if (string.IsNullOrWhiteSpace(nuspec.PackageName)
            || string.IsNullOrWhiteSpace(nuspec.Version))
        {
            throw Failure(
                PackageManifestFailureReason.UnsupportedDocumentShape);
        }

        ValidateScalar(nuspec.PackageName);
        ValidateScalar(nuspec.Version);

        try
        {
            return PackageSourceCoordinate.Create(
                nuspec.PackageName,
                nuspec.Version.Trim());
        }
        catch (ArgumentException)
        {
            throw Failure(
                PackageManifestFailureReason.InvalidIdentityContract);
        }
    }

    private static void ValidateIdentity(
        NuspecData nuspec,
        PackageSourceCoordinate expectedCoordinate)
    {
        if (string.IsNullOrWhiteSpace(nuspec.PackageName)
            || string.IsNullOrWhiteSpace(nuspec.Version))
        {
            throw Failure(
                PackageManifestFailureReason.UnsupportedDocumentShape);
        }

        if (!nuspec.PackageName.Equals(
                expectedCoordinate.PackageId,
                StringComparison.OrdinalIgnoreCase)
            || !VersionsEqual(
                nuspec.Version,
                expectedCoordinate.Version))
        {
            throw Failure(
                PackageManifestFailureReason.IdentityMismatch);
        }
    }

    private static ImmutableArray<DeclaredPackageDependencyGroup>
        ProjectDependencyGroups(List<DependencyGroup>? groups)
    {
        if (groups is null)
            return [];
        if (groups.Count > MaxDependencyGroups)
        {
            throw Failure(
                PackageManifestFailureReason.ConfiguredLimitExceeded);
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
                    throw Failure(
                        PackageManifestFailureReason.ConfiguredLimitExceeded);
                }

                ValidateScalar(dependency.Id);
                ValidateScalar(dependency.Version);
                if (!PackageCoordinateResolver.IsCanonicalPackageId(
                        dependency.Id))
                {
                    throw Failure(
                        PackageManifestFailureReason.InvalidDependencyContract);
                }

                try
                {
                    PackageDependencyVersionRange.Validate(dependency.Version);
                }
                catch (InvalidDataException)
                {
                    throw Failure(
                        PackageManifestFailureReason
                            .InvalidDependencyContract);
                }
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
            throw Failure(
                PackageManifestFailureReason.ConfiguredLimitExceeded);
        }

        foreach (string packageType in nuspec.PackageTypes ?? [])
            ValidateScalar(packageType);
    }

    private static void ValidateScalar(string? value)
    {
        if (value is { Length: > MaxScalarCharacters })
        {
            throw Failure(
                PackageManifestFailureReason.ConfiguredLimitExceeded);
        }
    }

    private static PackageManifestFactsResult.Failed Failed(
        PackageManifestFailureReason reason,
        int lineNumber = 0,
        int linePosition = 0) =>
        new(new PackageManifestFailure(
            reason,
            lineNumber,
            linePosition));

    private static ManifestValidationException Failure(
        PackageManifestFailureReason reason) =>
        new(reason);

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

    private sealed class ManifestValidationException(
        PackageManifestFailureReason reason) : Exception
    {
        public PackageManifestFailureReason Reason { get; } = reason;
    }
}
