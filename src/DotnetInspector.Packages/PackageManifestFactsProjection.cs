using System.Collections.Immutable;
using NuGet.Frameworks;
using NuGet.Versioning;
using NuGetFetch;

namespace DotnetInspector.Packages;

/// <summary>
/// Projects immutable package facts from bounded exact nuspec content.
/// </summary>
public static class PackageManifestFactsProjection
{
    public const int MaxManifestBytes = 1024 * 1024;
    public const int MaxManifestCharacters = 512 * 1024;
    public const int MaxScalarCharacters = 32 * 1024;
    public const int MaxPackageTypes = 128;
    public const int MaxDependencyGroups = 1024;
    public const int MaxDependencies = 4096;
    public const int MaxFrameworkReferenceGroups = 1024;
    public const int MaxFrameworkReferences = 4096;

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
            PackageManifestFrameworkReferenceFactsResult frameworkReferences =
                ProjectFrameworkReferences(nuspec.FrameworkReferenceGroups);
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
                    LicenseDeclaration = nuspec.LicenseDeclaration,
                    IconFile = nuspec.IconFile,
                    IconUrl = nuspec.IconUrl,
                    IdentityProvenance = identityProvenance,
                    FrameworkReferences = frameworkReferences,
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

    private static PackageManifestFrameworkReferenceFactsResult
        ProjectFrameworkReferences(
            List<NuspecFrameworkReferenceGroup>? groups)
    {
        if (groups is null || groups.Count == 0)
        {
            return AvailableFrameworkReferences([]);
        }
        if (groups.Count > MaxFrameworkReferenceGroups)
        {
            return FailedFrameworkReferences(
                PackageManifestFrameworkReferenceFailureReason
                    .ConfiguredLimitExceeded);
        }

        var groupBuilder = ImmutableArray.CreateBuilder<
            PackageManifestFrameworkReferenceGroup>(groups.Count);
        int referenceCount = 0;
        foreach (NuspecFrameworkReferenceGroup group in groups)
        {
            string? sourceTarget = group.TargetFramework;
            if (sourceTarget is { Length: > MaxScalarCharacters })
            {
                return FailedFrameworkReferences(
                    PackageManifestFrameworkReferenceFailureReason
                        .ConfiguredLimitExceeded);
            }
            if (!TryGetCanonicalFramework(
                    sourceTarget,
                    out string canonicalTarget))
            {
                return FailedFrameworkReferences(
                    PackageManifestFrameworkReferenceFailureReason
                        .InvalidTargetFramework);
            }

            var identities = new Dictionary<
                string,
                PackageFrameworkReferenceIdentity>(
                    StringComparer.OrdinalIgnoreCase);
            var references = ImmutableArray.CreateBuilder<
                PackageFrameworkReferenceIdentity>();
            var occurrences = ImmutableArray.CreateBuilder<
                PackageFrameworkReferenceOccurrence>(
                    group.References.Count);
            foreach (NuspecFrameworkReference reference in group.References)
            {
                referenceCount++;
                if (referenceCount > MaxFrameworkReferences)
                {
                    return FailedFrameworkReferences(
                        PackageManifestFrameworkReferenceFailureReason
                            .ConfiguredLimitExceeded);
                }

                string? sourceName = reference.Name;
                if (sourceName is { Length: > MaxScalarCharacters })
                {
                    return FailedFrameworkReferences(
                        PackageManifestFrameworkReferenceFailureReason
                            .ConfiguredLimitExceeded);
                }
                if (string.IsNullOrWhiteSpace(sourceName))
                {
                    return FailedFrameworkReferences(
                        PackageManifestFrameworkReferenceFailureReason
                            .InvalidReferenceName);
                }

                if (!identities.TryGetValue(
                        sourceName,
                        out PackageFrameworkReferenceIdentity? identity))
                {
                    identity = new PackageFrameworkReferenceIdentity(
                        sourceName);
                    identities.Add(sourceName, identity);
                    references.Add(identity);
                }

                occurrences.Add(
                    new PackageFrameworkReferenceOccurrence(
                        sourceName,
                        identity));
            }

            groupBuilder.Add(
                new PackageManifestFrameworkReferenceGroup(
                    sourceTarget!,
                    canonicalTarget,
                    occurrences.MoveToImmutable(),
                    references.ToImmutable()));
        }

        return AvailableFrameworkReferences(
            groupBuilder.MoveToImmutable());
    }

    private static bool TryGetCanonicalFramework(
        string? source,
        out string canonical)
    {
        canonical = "";
        if (string.IsNullOrWhiteSpace(source))
            return false;

        try
        {
            NuGetFramework framework = NuGetFramework.Parse(
                source,
                DefaultFrameworkNameProvider.Instance);
            if (framework.IsUnsupported)
                return false;

            canonical = framework.GetShortFolderName().ToLowerInvariant();
            return !string.IsNullOrWhiteSpace(canonical)
                && !canonical.Equals(
                    "unsupported",
                    StringComparison.OrdinalIgnoreCase);
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (FrameworkException)
        {
            return false;
        }
    }

    private static PackageManifestFrameworkReferenceFactsResult
        AvailableFrameworkReferences(
            ImmutableArray<PackageManifestFrameworkReferenceGroup> groups) =>
        new PackageManifestFrameworkReferenceFactsResult.Available(
            new PackageManifestFrameworkReferenceFacts(groups));

    private static PackageManifestFrameworkReferenceFactsResult
        FailedFrameworkReferences(
            PackageManifestFrameworkReferenceFailureReason reason) =>
        new PackageManifestFrameworkReferenceFactsResult.Failed(
            new PackageManifestFrameworkReferenceFailure(reason));

    private static void ValidateScalarFacts(NuspecData nuspec)
    {
        ValidateScalar(nuspec.ManifestVersion);
        ValidateScalar(nuspec.Authors);
        ValidateScalar(nuspec.Repository);
        ValidateScalar(nuspec.RepositoryType);
        ValidateScalar(nuspec.RepositoryCommit);
        ValidateScalar(nuspec.License);
        ValidateScalar(nuspec.LicenseUrl);
        ValidateScalar(nuspec.LicenseDeclaration?.Value);
        ValidateScalar(nuspec.IconFile);
        ValidateScalar(nuspec.IconUrl);
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
