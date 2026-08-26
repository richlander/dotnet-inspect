using System.Collections.Immutable;
using System.Text;
using System.Xml.Linq;
using DotnetInspector.PackageManifestCorpus;
using NuGetFetch;

namespace DotnetInspector.Queries.Tests;

public sealed class PackageManifestCorpusTests
{
    [Fact]
    public void Catalog_PinsExpectedCoordinatesAndCoversEveryShape()
    {
        PackageManifestCorpusCatalog catalog = LoadCatalog();

        Assert.Equal(
            [
                "Newtonsoft.Json@3.5.8",
                "dotnet-ef@9.0.0",
                "Spectre.Console@0.49.1",
                "Microsoft.SourceLink.GitHub@8.0.0",
            ],
            catalog.Packages.Select(entry =>
                $"{entry.Id}@{entry.Version}"));
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
