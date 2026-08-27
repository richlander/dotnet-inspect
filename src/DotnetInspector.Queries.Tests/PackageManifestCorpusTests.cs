using System.Collections.Immutable;
using System.Text;
using System.Xml.Linq;
using DotnetInspector.PackageManifestCorpus;
using NuGetFetch;

namespace DotnetInspector.Queries.Tests;

public sealed class PackageManifestCorpusTests
{
    [Fact]
    public void Catalog_PinsExpectedCoordinatesHashesAndCoversEveryShape()
    {
        PackageManifestCorpusCatalog catalog = LoadCatalog();

        Assert.Equal(
            [
                "Newtonsoft.Json@3.5.8:7990b971ca0f217da4c82a9a4606e5cbf08746857ad7ee7541559c80750fdfdb",
                "dotnet-ef@9.0.0:7a6d4b662a24af6192ac0262c433f7a11d95f9a79a705888554ec242799160a3",
                "Spectre.Console@0.49.1:12a7877ded4a2d3d96db03432d65afeeb8b8b7936894a722ef6b2a507a679379",
                "Microsoft.SourceLink.GitHub@8.0.0:b1081e636501a0cdcf7b6e93ff97783d263d30b7b745f12ef4a266d3aa402cd6",
            ],
            catalog.Packages.Select(entry =>
                $"{entry.Id}@{entry.Version}:{entry.Sha256}"));
        Assert.Equal(
            Enum.GetValues<PackageManifestCorpusCoverage>()
                .Order(),
            catalog.Packages.SelectMany(entry => entry.Coverage)
                .Distinct()
                .Order());
    }

    [Fact]
    public void Verifier_RejectsHashMismatchBeforeProjection()
    {
        var entry = new PackageManifestCorpusEntry(
            "Example.Package",
            "1.0.0",
            new string('0', 64),
            []);

        InvalidDataException exception = Assert.Throws<
            InvalidDataException>(() =>
                PackageManifestCorpusVerifier.Verify(
                    entry,
                    Encoding.UTF8.GetBytes(
                        "<not-a-package>SHOULD-NOT-REACH-THE-DIAGNOSTIC</not-a-package>")));

        Assert.Contains(
            "hash mismatch",
            exception.Message,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "SHOULD-NOT-REACH-THE-DIAGNOSTIC",
            exception.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Verifier_ExercisesProductAndIndependentOracle()
    {
        byte[] manifestBytes = ManifestBytes();
        var entry = new PackageManifestCorpusEntry(
            "Example.Package",
            "1.0.0",
            PackageManifestCorpusVerifier.ComputeSha256(
                manifestBytes),
            [
                PackageManifestCorpusCoverage.NamespaceFreeRoot,
                PackageManifestCorpusCoverage
                    .UngroupedDependencies,
                PackageManifestCorpusCoverage.PackageTypes,
                PackageManifestCorpusCoverage.Repository,
                PackageManifestCorpusCoverage.License,
                PackageManifestCorpusCoverage.Readme,
            ]);

        PackageManifestCorpusObservation observation =
            PackageManifestCorpusVerifier.Verify(
                entry,
                manifestBytes);

        Assert.Equal(entry.Sha256, observation.Sha256);
        Assert.Equal(
            entry.Coverage.Order(),
            observation.Coverage.Order());
    }

    [Fact]
    public void Comparer_ReportsOracleDisagreementWithoutValues()
    {
        byte[] manifestBytes = ManifestBytes();
        var entry = new PackageManifestCorpusEntry(
            "Example.Package",
            "1.0.0",
            PackageManifestCorpusVerifier.ComputeSha256(
                manifestBytes),
            []);
        PackageManifestFacts facts = Assert.IsType<
            PackageManifestFactsResult.Available>(
                PackageManifestFactsQuery.Execute(
                    manifestBytes,
                    PackageSourceCoordinate.Create(
                        entry.Id,
                        entry.Version))).Value;
        PackageManifestOracleFacts oracle =
            PackageManifestCorpusVerifier.ProjectOracle(
                manifestBytes,
                XDocument.Parse(
                    Encoding.UTF8.GetString(manifestBytes)));

        InvalidDataException exception = Assert.Throws<
            InvalidDataException>(() =>
                PackageManifestCorpusVerifier.Compare(
                    facts,
                    oracle with
                    {
                        Authors =
                            "SHOULD-NOT-REACH-THE-DIAGNOSTIC",
                    },
                    entry));

        Assert.Contains(
            "authors",
            exception.Message,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "SHOULD-NOT-REACH-THE-DIAGNOSTIC",
            exception.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Comparer_RejectsManifestVersionDisagreement()
    {
        byte[] manifestBytes = ManifestBytes();
        var entry = new PackageManifestCorpusEntry(
            "Example.Package",
            "1.0.0",
            PackageManifestCorpusVerifier.ComputeSha256(
                manifestBytes),
            []);
        PackageManifestFacts facts = Assert.IsType<
            PackageManifestFactsResult.Available>(
                PackageManifestFactsQuery.Execute(
                    manifestBytes,
                    PackageSourceCoordinate.Create(
                        entry.Id,
                        entry.Version))).Value;
        PackageManifestOracleFacts oracle =
            PackageManifestCorpusVerifier.ProjectOracle(
                manifestBytes,
                XDocument.Parse(
                    Encoding.UTF8.GetString(manifestBytes)));

        InvalidDataException exception = Assert.Throws<
            InvalidDataException>(() =>
                PackageManifestCorpusVerifier.Compare(
                    facts with
                    {
                        ManifestVersion = "SHOULD-NOT-REACH-THE-DIAGNOSTIC",
                    },
                    oracle,
                    entry));

        Assert.Contains(
            "manifest version",
            exception.Message,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "SHOULD-NOT-REACH-THE-DIAGNOSTIC",
            exception.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Serializer_UsesCanonicalLf()
    {
        PackageManifestCorpusCatalog catalog = LoadCatalog();

        string serialized =
            PackageManifestCorpusVerifier.SerializeCatalog(
                catalog);

        Assert.DoesNotContain(
            "\r",
            serialized,
            StringComparison.Ordinal);
        using var stream = new MemoryStream(
            Encoding.UTF8.GetBytes(serialized));
        PackageManifestCorpusCatalog roundTripped =
            PackageManifestCorpusVerifier.LoadCatalog(stream);
        Assert.Equal(
            catalog.Packages.Select(entry =>
                $"{entry.Id}@{entry.Version}:{entry.Sha256}"),
            roundTripped.Packages.Select(entry =>
                $"{entry.Id}@{entry.Version}:{entry.Sha256}"));
    }

    [Fact]
    public void Oracle_RejectsMixedDependencyLayoutsExplicitly()
    {
        byte[] manifestBytes = Encoding.UTF8.GetBytes(
            """
            <package>
              <metadata>
                <id>Example.Package</id>
                <version>1.0.0</version>
                <authors>Example Authors</authors>
                <description>Example description</description>
                <dependencies>
                  <group targetFramework="net8.0">
                    <dependency id="Grouped.Dependency" version="1.0.0" />
                  </group>
                  <dependency id="Ungrouped.Dependency" version="2.0.0" />
                </dependencies>
              </metadata>
            </package>
            """);

        InvalidDataException exception = Assert.Throws<
            InvalidDataException>(() =>
                PackageManifestCorpusVerifier.ProjectOracle(
                    manifestBytes,
                    XDocument.Parse(
                        Encoding.UTF8.GetString(manifestBytes))));

        Assert.Contains(
            "does not support mixed grouped and ungrouped dependencies",
            exception.Message,
            StringComparison.Ordinal);
    }

    private static PackageManifestCorpusCatalog LoadCatalog()
    {
        using Stream stream = typeof(PackageManifestCorpusTests)
            .Assembly.GetManifestResourceStream(
                "DotnetInspector.Queries.Tests.PackageManifestCorpus.json")
            ?? throw new InvalidOperationException(
                "The package-manifest corpus catalog is not embedded.");
        return PackageManifestCorpusVerifier.LoadCatalog(stream);
    }

    private static byte[] ManifestBytes() =>
        Encoding.UTF8.GetBytes(
            """
            <package>
              <metadata>
                <id>Example.Package</id>
                <version>1.0.0</version>
                <authors>Example Authors</authors>
                <description>Example description</description>
                <repository type="git" url="https://example.test/repository" commit="abc123" />
                <license type="expression">MIT</license>
                <readme>README.md</readme>
                <packageTypes>
                  <packageType name="DotnetTool" />
                </packageTypes>
                <dependencies>
                  <dependency id="Example.Dependency" version="[2.0.0]" />
                </dependencies>
              </metadata>
            </package>
            """);
}
