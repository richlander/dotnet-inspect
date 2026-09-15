using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Xml;
using System.Xml.Linq;
using DotnetInspector.Queries;
using NuGet.Frameworks;
using NuGet.Packaging;
using NuGet.Packaging.Core;
using NuGet.Versioning;
using NuGetFetch;

namespace DotnetInspector.PackageManifestCorpus;

public enum PackageManifestCorpusCoverage
{
    NamespaceFreeRoot,
    RootSchemaNamespace,
    LegacyMetadataNamespace,
    GroupedDependencies,
    UngroupedDependencies,
    PackageTypes,
    Repository,
    License,
    Readme,
    OlderPublicationShape,
}

public sealed record PackageManifestCorpusCatalog(
    int SchemaVersion,
    IReadOnlyList<PackageManifestCorpusEntry> Packages);

public sealed record PackageManifestCorpusEntry(
    string Id,
    string Version,
    string Sha256,
    IReadOnlyList<PackageManifestCorpusCoverage> Coverage);

public sealed record PackageManifestCorpusObservation(
    string Id,
    string Version,
    string Sha256,
    ImmutableArray<PackageManifestCorpusCoverage> Coverage);

public static class PackageManifestCorpusVerifier
{
    public const int CurrentSchemaVersion = 1;

    private static readonly JsonSerializerOptions SerializerOptions = CreateOptions();

    public static PackageManifestCorpusCatalog LoadCatalog(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        PackageManifestCorpusCatalog catalog =
            JsonSerializer.Deserialize<PackageManifestCorpusCatalog>(
                stream,
                SerializerOptions)
            ?? throw new InvalidDataException(
                "The package-manifest corpus catalog is empty.");
        ValidateCatalog(catalog);
        return catalog;
    }

    public static string SerializeCatalog(
        PackageManifestCorpusCatalog catalog)
    {
        ValidateCatalog(catalog);
        return JsonSerializer.Serialize(catalog, SerializerOptions) + "\n";
    }

    public static void ValidateCatalog(
        PackageManifestCorpusCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        if (catalog.SchemaVersion != CurrentSchemaVersion)
        {
            throw new InvalidDataException(
                "The package-manifest corpus catalog schema is unsupported.");
        }

        if (catalog.Packages.Count == 0)
        {
            throw new InvalidDataException(
                "The package-manifest corpus catalog contains no packages.");
        }

        var coordinates = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase);
        var covered = new HashSet<PackageManifestCorpusCoverage>();
        foreach (PackageManifestCorpusEntry entry in catalog.Packages)
        {
            PackageSourceCoordinate coordinate =
                PackageSourceCoordinate.Create(entry.Id, entry.Version);
            if (!coordinate.Version.Equals(
                    entry.Version,
                    StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    "Package-manifest corpus versions must be normalized.");
            }

            if (!coordinates.Add(
                    $"{coordinate.PackageId}@{coordinate.Version}"))
            {
                throw new InvalidDataException(
                    "The package-manifest corpus contains a duplicate coordinate.");
            }

            if (!IsLowercaseSha256(entry.Sha256))
            {
                throw new InvalidDataException(
                    "Package-manifest corpus hashes must be lowercase SHA-256 values.");
            }

            if (entry.Coverage.Count == 0
                || entry.Coverage.Count
                    != entry.Coverage.Distinct().Count())
            {
                throw new InvalidDataException(
                    "Each package-manifest corpus entry must name distinct coverage.");
            }

            covered.UnionWith(entry.Coverage);
        }

        if (!covered.SetEquals(
                Enum.GetValues<PackageManifestCorpusCoverage>()))
        {
            throw new InvalidDataException(
                "The package-manifest corpus does not cover every required shape.");
        }
    }

    public static PackageManifestCorpusObservation Verify(
        PackageManifestCorpusEntry entry,
        ReadOnlyMemory<byte> manifestBytes,
        bool verifyHash = true)
    {
        ArgumentNullException.ThrowIfNull(entry);
        string actualHash = ComputeSha256(manifestBytes.Span);
        if (verifyHash
            && !actualHash.Equals(
                entry.Sha256,
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"Package-manifest corpus hash mismatch for {entry.Id}@{entry.Version}.");
        }

        XDocument document = LoadDocument(manifestBytes);
        PackageManifestOracleFacts oracle = ProjectOracle(
            manifestBytes,
            document);
        PackageManifestFactsResult productResult =
            PackageManifestFactsQuery.Execute(
                manifestBytes,
                PackageSourceCoordinate.Create(entry.Id, entry.Version));
        if (productResult is PackageManifestFactsResult.Failed failed)
        {
            throw new InvalidDataException(
                $"Package-manifest corpus projection failed for {entry.Id}@{entry.Version}: {failed.Failure.Reason}.");
        }

        PackageManifestFacts product =
            ((PackageManifestFactsResult.Available)productResult).Value;
        Compare(product, oracle, entry);

        ImmutableArray<PackageManifestCorpusCoverage> observedCoverage =
            ObserveCoverage(document, oracle);
        if (!observedCoverage.ToHashSet().SetEquals(entry.Coverage))
        {
            throw new InvalidDataException(
                $"Package-manifest corpus coverage drifted for {entry.Id}@{entry.Version}.");
        }

        return new PackageManifestCorpusObservation(
            entry.Id,
            entry.Version,
            actualHash,
            observedCoverage);
    }

    internal static void Compare(
        PackageManifestFacts product,
        PackageManifestOracleFacts oracle,
        PackageManifestCorpusEntry entry)
    {
        RequireEqual(
            entry,
            "package id",
            product.Coordinate.PackageId,
            oracle.PackageId,
            StringComparer.OrdinalIgnoreCase);
        RequireEqual(
            entry,
            "package version",
            product.Coordinate.Version,
            oracle.PackageVersion,
            StringComparer.OrdinalIgnoreCase);
        RequireEqual(
            entry,
            "manifest version",
            product.ManifestVersion,
            oracle.ManifestVersion);
        RequireEqual(entry, "description", product.Description?.ToString(), oracle.Description);
        RequireEqual(entry, "authors", product.Authors, oracle.Authors);
        RequireEqual(entry, "repository URL", product.Repository, oracle.Repository);
        RequireEqual(entry, "repository type", product.RepositoryType, oracle.RepositoryType);
        RequireEqual(entry, "repository commit", product.RepositoryCommit, oracle.RepositoryCommit);
        if (oracle.HasLicenseMetadata)
            RequireEqual(entry, "license", product.License, oracle.License);
        RequireEqual(entry, "license URL", product.LicenseUrl, oracle.LicenseUrl);
        RequireEqual(entry, "readme", product.ReadmeFile, oracle.ReadmeFile);
        RequireEqual(entry, "tool classification", product.IsToolPackage, oracle.IsToolPackage);
        RequireSequenceEqual(
            entry,
            "package types",
            product.PackageTypes,
            oracle.PackageTypes,
            StringComparer.Ordinal);

        if (product.DependencyGroups.Length != oracle.DependencyGroups.Length)
            ThrowDisagreement(entry, "dependency-group count");

        for (int groupIndex = 0;
            groupIndex < product.DependencyGroups.Length;
            groupIndex++)
        {
            DeclaredPackageDependencyGroup productGroup =
                product.DependencyGroups[groupIndex];
            PackageManifestOracleDependencyGroup oracleGroup =
                oracle.DependencyGroups[groupIndex];
            NuGetFramework productFramework =
                NuGetFramework.ParseFolder(productGroup.TargetFramework);
            RequireEqual(
                entry,
                $"dependency group {groupIndex} target framework",
                productFramework,
                oracleGroup.TargetFramework);
            RequireEqual(
                entry,
                $"dependency group {groupIndex} layout",
                productGroup.IsImplicitManifestGroup,
                oracleGroup.IsImplicitManifestGroup);
            if (productGroup.Dependencies.Length
                != oracleGroup.Dependencies.Length)
            {
                ThrowDisagreement(
                    entry,
                    $"dependency group {groupIndex} count");
            }

            for (int dependencyIndex = 0;
                dependencyIndex < productGroup.Dependencies.Length;
                dependencyIndex++)
            {
                DeclaredPackageDependency productDependency =
                    productGroup.Dependencies[dependencyIndex];
                PackageManifestOracleDependency oracleDependency =
                    oracleGroup.Dependencies[dependencyIndex];
                RequireEqual(
                    entry,
                    $"dependency group {groupIndex} item {dependencyIndex} id",
                    productDependency.Id,
                    oracleDependency.Id,
                    StringComparer.OrdinalIgnoreCase);
                VersionRange productRange =
                    string.IsNullOrWhiteSpace(
                        productDependency.VersionRange)
                        ? VersionRange.All
                        : VersionRange.Parse(
                            productDependency.VersionRange);
                RequireEqual(
                    entry,
                    $"dependency group {groupIndex} item {dependencyIndex} range",
                    productRange.ToNormalizedString(),
                    oracleDependency.VersionRange.ToNormalizedString());
            }
        }
    }

    internal static PackageManifestOracleFacts ProjectOracle(
        ReadOnlyMemory<byte> manifestBytes,
        XDocument document)
    {
        using var stream = new MemoryStream(
            manifestBytes.ToArray(),
            writable: false);
        var reader = new NuspecReader(stream);
        XElement root = document.Root
            ?? throw new InvalidDataException(
                "The package-manifest corpus entry has no document root.");
        XElement metadata = root.Elements().Single(element =>
            element.Name.LocalName.Equals(
                "metadata",
                StringComparison.Ordinal));
        XNamespace ns = metadata.Name.Namespace;
        XElement? dependencies = metadata.Element(ns + "dependencies");
        bool hasUngroupedDependencies =
            dependencies?.Elements(ns + "dependency").Any() == true;
        bool hasGroupedDependencies =
            dependencies?.Elements(ns + "group").Any() == true;
        if (hasGroupedDependencies && hasUngroupedDependencies)
        {
            throw new InvalidDataException(
                "The package-manifest corpus oracle does not support mixed grouped and ungrouped dependencies.");
        }

        RepositoryMetadata repository = reader.GetRepositoryMetadata();
        LicenseMetadata? license = reader.GetLicenseMetadata();
        string? licenseText = license?.Type switch
        {
            LicenseType.Expression => license.License,
            LicenseType.File => $"(file: {license.License})",
            _ => null,
        };
        string? licenseUrl = NullIfEmpty(reader.GetLicenseUrl());
        ImmutableArray<PackageManifestOracleDependencyGroup>
            dependencyGroups =
            [
                .. reader.GetDependencyGroups().Select(group =>
                    new PackageManifestOracleDependencyGroup(
                        group.TargetFramework,
                        group.TargetFramework.IsAny
                            && hasUngroupedDependencies,
                        [
                            .. group.Packages.Select(dependency =>
                                new PackageManifestOracleDependency(
                                    dependency.Id,
                                    dependency.VersionRange)),
                        ])),
            ];

        ImmutableArray<string> packageTypes =
        [
            .. reader.GetPackageTypes().Select(type => type.Name),
        ];
        return new PackageManifestOracleFacts(
            reader.GetId(),
            reader.GetVersion().ToNormalizedString(),
            GetManifestVersion(ns),
            reader.GetDescription(),
            reader.GetAuthors(),
            NullIfEmpty(repository?.Url),
            NullIfEmpty(repository?.Type),
            NullIfEmpty(repository?.Commit),
            licenseText,
            license is not null,
            licenseUrl,
            packageTypes,
            packageTypes.Any(type => type.Equals(
                "DotnetTool",
                StringComparison.OrdinalIgnoreCase)),
            NullIfEmpty(reader.GetReadme()),
            dependencyGroups);
    }

    public static string ComputeSha256(ReadOnlySpan<byte> bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static ImmutableArray<PackageManifestCorpusCoverage>
        ObserveCoverage(
            XDocument document,
            PackageManifestOracleFacts oracle)
    {
        XElement root = document.Root!;
        XElement metadata = root.Elements().Single(element =>
            element.Name.LocalName.Equals(
                "metadata",
                StringComparison.Ordinal));
        XNamespace ns = metadata.Name.Namespace;
        XElement? dependencies = metadata.Element(ns + "dependencies");
        var coverage =
            ImmutableArray.CreateBuilder<PackageManifestCorpusCoverage>();
        if (string.IsNullOrEmpty(root.Name.NamespaceName))
        {
            coverage.Add(
                PackageManifestCorpusCoverage.NamespaceFreeRoot);
        }
        else
        {
            coverage.Add(
                PackageManifestCorpusCoverage.RootSchemaNamespace);
        }

        if (string.IsNullOrEmpty(root.Name.NamespaceName)
            && !string.IsNullOrEmpty(metadata.Name.NamespaceName))
        {
            coverage.Add(
                PackageManifestCorpusCoverage.LegacyMetadataNamespace);
        }

        if (dependencies?.Elements(ns + "group").Any() == true)
        {
            coverage.Add(
                PackageManifestCorpusCoverage.GroupedDependencies);
        }

        if (dependencies?.Elements(ns + "dependency").Any() == true)
        {
            coverage.Add(
                PackageManifestCorpusCoverage.UngroupedDependencies);
        }

        if (!oracle.PackageTypes.IsEmpty)
            coverage.Add(PackageManifestCorpusCoverage.PackageTypes);
        if (!string.IsNullOrEmpty(oracle.Repository))
            coverage.Add(PackageManifestCorpusCoverage.Repository);
        if (metadata.Element(ns + "license") is not null)
            coverage.Add(PackageManifestCorpusCoverage.License);
        if (!string.IsNullOrEmpty(oracle.ReadmeFile))
            coverage.Add(PackageManifestCorpusCoverage.Readme);
        if (metadata.Name.NamespaceName.Contains(
                "/2010/07/",
                StringComparison.Ordinal))
        {
            coverage.Add(
                PackageManifestCorpusCoverage.OlderPublicationShape);
        }

        coverage.Sort();
        return coverage.ToImmutable();
    }

    private static XDocument LoadDocument(
        ReadOnlyMemory<byte> manifestBytes)
    {
        using var stream = new MemoryStream(
            manifestBytes.ToArray(),
            writable: false);
        using XmlReader reader = XmlReader.Create(
            stream,
            new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                MaxCharactersInDocument =
                    PackageManifestFactsQuery.MaxManifestCharacters,
                XmlResolver = null,
            });
        return XDocument.Load(reader, LoadOptions.None);
    }

    private static bool IsLowercaseSha256(string value) =>
        value.Length == 64
        && value.All(character =>
            character is >= '0' and <= '9'
                or >= 'a' and <= 'f');

    private static string GetManifestVersion(XNamespace ns)
    {
        if (string.IsNullOrEmpty(ns.NamespaceName))
            return "nuspec";

        if (!Uri.TryCreate(
                ns.NamespaceName,
                UriKind.Absolute,
                out Uri? uri)
            || !uri.Scheme.Equals(
                Uri.UriSchemeHttp,
                StringComparison.OrdinalIgnoreCase)
            || !uri.Host.Equals(
                "schemas.microsoft.com",
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "The package-manifest corpus oracle found an unsupported namespace.");
        }

        string[] segments = uri.Segments;
        if (segments.Length < 4
            || !segments[1].Equals(
                "packaging/",
                StringComparison.Ordinal)
            || !segments[^1].Equals(
                "nuspec.xsd",
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "The package-manifest corpus oracle found an unsupported namespace.");
        }

        return string.Concat(segments[2..^1]).TrimEnd('/');
    }

    private static string? NullIfEmpty(string? value) =>
        string.IsNullOrEmpty(value) ? null : value;

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            NewLine = "\n",
            TypeInfoResolver =
                PackageManifestCorpusJsonContext.Default,
            WriteIndented = true,
        };
        options.Converters.Add(
            new JsonStringEnumConverter(
                JsonNamingPolicy.KebabCaseLower));
        return options;
    }

    private static void RequireSequenceEqual(
        PackageManifestCorpusEntry entry,
        string field,
        ImmutableArray<string> product,
        ImmutableArray<string> oracle,
        StringComparer comparer)
    {
        if (!product.SequenceEqual(oracle, comparer))
            ThrowDisagreement(entry, field);
    }

    private static void RequireEqual<T>(
        PackageManifestCorpusEntry entry,
        string field,
        T product,
        T oracle,
        IEqualityComparer<T>? comparer = null)
    {
        if (!(comparer ?? EqualityComparer<T>.Default).Equals(
                product,
                oracle))
        {
            ThrowDisagreement(entry, field);
        }
    }

    private static void ThrowDisagreement(
        PackageManifestCorpusEntry entry,
        string field) =>
        throw new InvalidDataException(
            $"Package-manifest corpus oracle disagreement for {entry.Id}@{entry.Version}: {field}.");
}

internal sealed record PackageManifestOracleFacts(
    string PackageId,
    string PackageVersion,
    string ManifestVersion,
    string? Description,
    string? Authors,
    string? Repository,
    string? RepositoryType,
    string? RepositoryCommit,
    string? License,
    bool HasLicenseMetadata,
    string? LicenseUrl,
    ImmutableArray<string> PackageTypes,
    bool IsToolPackage,
    string? ReadmeFile,
    ImmutableArray<PackageManifestOracleDependencyGroup> DependencyGroups);

internal sealed record PackageManifestOracleDependencyGroup(
    NuGetFramework TargetFramework,
    bool IsImplicitManifestGroup,
    ImmutableArray<PackageManifestOracleDependency> Dependencies);

internal sealed record PackageManifestOracleDependency(
    string Id,
    VersionRange VersionRange);

[JsonSerializable(typeof(PackageManifestCorpusCatalog))]
internal sealed partial class PackageManifestCorpusJsonContext
    : JsonSerializerContext;
