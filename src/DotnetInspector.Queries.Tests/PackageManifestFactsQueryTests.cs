using System.Text;
using NuGetFetch;

namespace DotnetInspector.Queries.Tests;

public sealed class PackageManifestFactsQueryTests
{
    [Fact]
    public void Execute_ProjectsImmutableValidatedFacts()
    {
        PackageSourceCoordinate coordinate =
            PackageSourceCoordinate.Create(
                "Example.Package",
                "1.0");
        PackageManifestFacts facts = Available(
            PackageManifestFactsQuery.Execute(
                Encoding.UTF8.GetBytes(
                    """
                    <package xmlns="http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd">
                      <metadata>
                        <id>Example.Package</id>
                        <version>1.0.0</version>
                        <authors>Example Authors</authors>
                        <description>Example description</description>
                        <repository type="git" url="https://example.test/repo" commit="abc123" />
                        <license type="expression">MIT</license>
                        <licenseUrl>https://licenses.nuget.org/MIT</licenseUrl>
                        <packageTypes>
                          <packageType name="DotnetTool" />
                        </packageTypes>
                        <readme>docs/README.md</readme>
                        <dependencies>
                          <dependency id="Example.Dependency" version="[2.0.0]" />
                        </dependencies>
                      </metadata>
                    </package>
                    """),
                coordinate));

        Assert.Equal(coordinate, facts.Coordinate);
        Assert.Equal("2013/05", facts.ManifestVersion);
        Assert.Equal("Example Authors", facts.Authors);
        Assert.Equal("Example description", facts.Description?.ToString());
        Assert.Equal("https://example.test/repo", facts.Repository);
        Assert.Equal("git", facts.RepositoryType);
        Assert.Equal("abc123", facts.RepositoryCommit);
        Assert.Equal("MIT", facts.License);
        Assert.Equal("https://licenses.nuget.org/MIT", facts.LicenseUrl);
        Assert.Equal(["DotnetTool"], facts.PackageTypes);
        Assert.True(facts.IsToolPackage);
        Assert.Equal("docs/README.md", facts.ReadmeFile);

        DeclaredPackageDependencyGroup group =
            Assert.Single(facts.DependencyGroups);
        Assert.Equal("any", group.TargetFramework);
        Assert.True(group.IsImplicitManifestGroup);
        DeclaredPackageDependency dependency =
            Assert.Single(group.Dependencies);
        Assert.Equal("Example.Dependency", dependency.Id);
        Assert.Equal("[2.0.0]", dependency.VersionRange);
    }

    [Fact]
    public void Execute_RejectsMismatchedIdentityWithoutLeakingIt()
    {
        PackageManifestFactsResult.Failed failure = Assert.IsType<
            PackageManifestFactsResult.Failed>(
                PackageManifestFactsQuery.Execute(
                    Encoding.UTF8.GetBytes(
                        """
                        <package>
                          <metadata>
                            <id>SHOULD-NOT-REACH-THE-DIAGNOSTIC</id>
                            <version>1.0.0</version>
                          </metadata>
                        </package>
                        """),
                    PackageSourceCoordinate.Create(
                        "Example.Package",
                        "1.0.0")));

        Assert.Equal(
            PackageManifestFailureReason.IdentityMismatch,
            failure.Failure.Reason);
        Assert.DoesNotContain(
            "SHOULD-NOT-REACH-THE-DIAGNOSTIC",
            failure.Failure.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_AcceptsLegacyMetadataNamespacePlacement()
    {
        PackageManifestFacts facts = Available(
            PackageManifestFactsQuery.Execute(
                Encoding.UTF8.GetBytes(
                    """
                    <package xmlns:xsd="http://www.w3.org/2001/XMLSchema"
                             xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance">
                      <metadata xmlns="http://schemas.microsoft.com/packaging/2010/07/nuspec.xsd">
                        <id>Example.Package</id>
                        <version>1.0.0</version>
                        <authors>Legacy Author</authors>
                        <dependencies>
                          <dependency id="Example.Dependency" version="2.0.0" />
                        </dependencies>
                      </metadata>
                    </package>
                    """),
                PackageSourceCoordinate.Create(
                    "Example.Package",
                    "1.0.0")));

        Assert.Equal("2010/07", facts.ManifestVersion);
        Assert.Equal("Legacy Author", facts.Authors);
        Assert.Equal(
            "Example.Dependency",
            Assert.Single(
                Assert.Single(facts.DependencyGroups).Dependencies).Id);
    }

    [Fact]
    public void Execute_RejectsNonNuspecDocumentRoot()
    {
        PackageManifestFactsResult.Failed failure = Assert.IsType<
            PackageManifestFactsResult.Failed>(
                PackageManifestFactsQuery.Execute(
                    Encoding.UTF8.GetBytes(
                        """
                        <notpackage>
                          <metadata>
                            <id>Example.Package</id>
                            <version>1.0.0</version>
                          </metadata>
                        </notpackage>
                        """),
                    PackageSourceCoordinate.Create(
                        "Example.Package",
                        "1.0.0")));

        Assert.Equal(
            PackageManifestFailureReason.UnsupportedDocumentShape,
            failure.Failure.Reason);
    }

    [Fact]
    public void Execute_DoesNotDiscoverNestedMetadata()
    {
        PackageManifestFactsResult.Failed failure = Assert.IsType<
            PackageManifestFactsResult.Failed>(
                PackageManifestFactsQuery.Execute(
                    Encoding.UTF8.GetBytes(
                        """
                        <package>
                          <container>
                            <metadata>
                              <id>Example.Package</id>
                              <version>1.0.0</version>
                            </metadata>
                          </container>
                        </package>
                        """),
                    PackageSourceCoordinate.Create(
                        "Example.Package",
                        "1.0.0")));

        Assert.Equal(
            PackageManifestFailureReason.UnsupportedDocumentShape,
            failure.Failure.Reason);
    }

    [Fact]
    public void Execute_ReportsIncompatibleMetadataNamespaceAsUnsupportedDocumentShape()
    {
        PackageManifestFactsResult.Failed failure = Assert.IsType<
            PackageManifestFactsResult.Failed>(
                PackageManifestFactsQuery.Execute(
                    Encoding.UTF8.GetBytes(
                        """
                        <package xmlns="http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd">
                          <metadata xmlns="http://schemas.microsoft.com/packaging/2010/07/nuspec.xsd">
                            <id>Example.Package</id>
                            <version>1.0.0</version>
                          </metadata>
                        </package>
                        """),
                    PackageSourceCoordinate.Create(
                        "Example.Package",
                        "1.0.0")));

        Assert.Equal(
            PackageManifestFailureReason.UnsupportedDocumentShape,
            failure.Failure.Reason);
    }

    [Fact]
    public void Execute_RejectsInvalidDependencyContract()
    {
        PackageManifestFactsResult.Failed failure = Assert.IsType<
            PackageManifestFactsResult.Failed>(
                PackageManifestFactsQuery.Execute(
                    Encoding.UTF8.GetBytes(
                        """
                        <package>
                          <metadata>
                            <id>Example.Package</id>
                            <version>1.0.0</version>
                            <dependencies>
                              <dependency id="evil/../other" version="not-a-range" />
                            </dependencies>
                          </metadata>
                        </package>
                        """),
                    PackageSourceCoordinate.Create(
                        "Example.Package",
                        "1.0.0")));

        Assert.Equal(
            PackageManifestFailureReason.InvalidDependencyContract,
            failure.Failure.Reason);
        Assert.DoesNotContain(
            "evil/../other",
            failure.Failure.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_RejectsInvalidDependencyRangeWithTypedReason()
    {
        PackageManifestFactsResult.Failed failure = Assert.IsType<
            PackageManifestFactsResult.Failed>(
                PackageManifestFactsQuery.Execute(
                    Encoding.UTF8.GetBytes(
                        """
                        <package>
                          <metadata>
                            <id>Example.Package</id>
                            <version>1.0.0</version>
                            <dependencies>
                              <dependency id="Example.Dependency" version="SHOULD-NOT-REACH-THE-DIAGNOSTIC" />
                            </dependencies>
                          </metadata>
                        </package>
                        """),
                    PackageSourceCoordinate.Create(
                        "Example.Package",
                        "1.0.0")));

        Assert.Equal(
            PackageManifestFailureReason.InvalidDependencyContract,
            failure.Failure.Reason);
        Assert.DoesNotContain(
            "SHOULD-NOT-REACH-THE-DIAGNOSTIC",
            failure.Failure.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_ReportsMalformedXmlWithSafeLocation()
    {
        PackageManifestFactsResult.Failed failure = Assert.IsType<
            PackageManifestFactsResult.Failed>(
                PackageManifestFactsQuery.Execute(
                    Encoding.UTF8.GetBytes(
                        """
                        <package>
                          <metadata>
                            <id>SHOULD-NOT-REACH-THE-DIAGNOSTIC</id>
                          </package>
                        """),
                    PackageSourceCoordinate.Create(
                        "Example.Package",
                        "1.0.0")));

        Assert.Equal(
            PackageManifestFailureReason.MalformedXml,
            failure.Failure.Reason);
        Assert.True(failure.Failure.LineNumber > 0);
        Assert.True(failure.Failure.LinePosition > 0);
        Assert.DoesNotContain(
            "SHOULD-NOT-REACH-THE-DIAGNOSTIC",
            failure.Failure.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_EnforcesManifestByteLimit()
    {
        PackageManifestFactsResult.Failed failure = Assert.IsType<
            PackageManifestFactsResult.Failed>(
                PackageManifestFactsQuery.Execute(
                    new byte[
                        PackageManifestFactsQuery.MaxManifestBytes + 1],
                    PackageSourceCoordinate.Create(
                        "Example.Package",
                        "1.0.0")));

        Assert.Equal(
            PackageManifestFailureReason.ConfiguredLimitExceeded,
            failure.Failure.Reason);
    }

    [Fact]
    public void Execute_RejectsOversizedScalarFact()
    {
        string authors = new(
            'a',
            PackageManifestFactsQuery.MaxScalarCharacters + 1);
        PackageManifestFactsResult.Failed failure = Assert.IsType<
            PackageManifestFactsResult.Failed>(
                PackageManifestFactsQuery.Execute(
                    Encoding.UTF8.GetBytes(
                        $$"""
                        <package>
                          <metadata>
                            <id>Example.Package</id>
                            <version>1.0.0</version>
                            <authors>{{authors}}</authors>
                          </metadata>
                        </package>
                        """),
                    PackageSourceCoordinate.Create(
                        "Example.Package",
                        "1.0.0")));

        Assert.Equal(
            PackageManifestFailureReason.ConfiguredLimitExceeded,
            failure.Failure.Reason);
    }

    [Fact]
    public void Execute_RejectsExcessiveDependencyCardinality()
    {
        var dependencies = new StringBuilder();
        for (int i = 0;
            i <= PackageManifestFactsQuery.MaxDependencies;
            i++)
        {
            dependencies.Append(
                $"""<dependency id="Dependency.{i}" version="1.0.0" />""");
        }

        PackageManifestFactsResult.Failed failure = Assert.IsType<
            PackageManifestFactsResult.Failed>(
                PackageManifestFactsQuery.Execute(
                    Encoding.UTF8.GetBytes(
                        $$"""
                        <package>
                          <metadata>
                            <id>Example.Package</id>
                            <version>1.0.0</version>
                            <dependencies>{{dependencies}}</dependencies>
                          </metadata>
                        </package>
                        """),
                    PackageSourceCoordinate.Create(
                        "Example.Package",
                        "1.0.0")));

        Assert.Equal(
            PackageManifestFailureReason.ConfiguredLimitExceeded,
            failure.Failure.Reason);
    }

    [Fact]
    public void Execute_RejectsExcessiveDependencyGroupCardinality()
    {
        var groups = new StringBuilder();
        for (int i = 0;
            i <= PackageManifestFactsQuery.MaxDependencyGroups;
            i++)
        {
            groups.Append(
                $"""<group targetFramework="net{i}" />""");
        }

        PackageManifestFactsResult.Failed failure = Assert.IsType<
            PackageManifestFactsResult.Failed>(
                PackageManifestFactsQuery.Execute(
                    Encoding.UTF8.GetBytes(
                        $$"""
                        <package>
                          <metadata>
                            <id>Example.Package</id>
                            <version>1.0.0</version>
                            <dependencies>{{groups}}</dependencies>
                          </metadata>
                        </package>
                        """),
                    PackageSourceCoordinate.Create(
                        "Example.Package",
                        "1.0.0")));

        Assert.Equal(
            PackageManifestFailureReason.ConfiguredLimitExceeded,
            failure.Failure.Reason);
    }

    [Fact]
    public void Execute_RejectsExcessivePackageTypeCardinality()
    {
        var packageTypes = new StringBuilder();
        for (int i = 0;
            i <= PackageManifestFactsQuery.MaxPackageTypes;
            i++)
        {
            packageTypes.Append(
                $"""<packageType name="Type{i}" />""");
        }

        PackageManifestFactsResult.Failed failure = Assert.IsType<
            PackageManifestFactsResult.Failed>(
                PackageManifestFactsQuery.Execute(
                    Encoding.UTF8.GetBytes(
                        $$"""
                        <package>
                          <metadata>
                            <id>Example.Package</id>
                            <version>1.0.0</version>
                            <packageTypes>{{packageTypes}}</packageTypes>
                          </metadata>
                        </package>
                        """),
                    PackageSourceCoordinate.Create(
                        "Example.Package",
                        "1.0.0")));

        Assert.Equal(
            PackageManifestFailureReason.ConfiguredLimitExceeded,
            failure.Failure.Reason);
    }

    [Fact]
    public void Execute_AcceptsScalarAndCollectionLimits()
    {
        var dependencies = new StringBuilder();
        for (int i = 0;
            i < PackageManifestFactsQuery.MaxDependencies;
            i++)
        {
            dependencies.Append(
                $"""<dependency id="Dependency.{i}" version="1.0.0" />""");
        }

        var dependencyGroups = new StringBuilder()
            .Append("""<group targetFramework="net8.0">""")
            .Append(dependencies)
            .Append("</group>");
        for (int i = 1;
            i < PackageManifestFactsQuery.MaxDependencyGroups;
            i++)
        {
            dependencyGroups.Append(
                $"""<group targetFramework="net{i}" />""");
        }

        var packageTypes = new StringBuilder();
        for (int i = 0;
            i < PackageManifestFactsQuery.MaxPackageTypes;
            i++)
        {
            packageTypes.Append(
                $"""<packageType name="Type{i}" />""");
        }

        string authors = new(
            'a',
            PackageManifestFactsQuery.MaxScalarCharacters);
        PackageManifestFacts facts = Available(
            PackageManifestFactsQuery.Execute(
                Encoding.UTF8.GetBytes(
                    $$"""
                    <package>
                      <metadata>
                        <id>Example.Package</id>
                        <version>1.0.0</version>
                        <authors>{{authors}}</authors>
                        <packageTypes>{{packageTypes}}</packageTypes>
                        <dependencies>{{dependencyGroups}}</dependencies>
                      </metadata>
                    </package>
                    """),
                PackageSourceCoordinate.Create(
                    "Example.Package",
                    "1.0.0")));

        Assert.Equal(
            PackageManifestFactsQuery.MaxScalarCharacters,
            facts.Authors!.Length);
        Assert.Equal(
            PackageManifestFactsQuery.MaxPackageTypes,
            facts.PackageTypes.Length);
        Assert.Equal(
            PackageManifestFactsQuery.MaxDependencyGroups,
            facts.DependencyGroups.Length);
        Assert.Equal(
            PackageManifestFactsQuery.MaxDependencies,
            facts.DependencyGroups.Sum(group =>
                group.Dependencies.Length));
    }

    [Theory]
    [InlineData(
        PackageManifestFailureReason.MalformedXml,
        "Package manifest is not well-formed XML.")]
    [InlineData(
        PackageManifestFailureReason.UnsupportedDocumentShape,
        "The package manifest has an unsupported document shape or namespace.")]
    [InlineData(
        PackageManifestFailureReason.IdentityMismatch,
        "The package manifest identity does not match the requested package.")]
    [InlineData(
        PackageManifestFailureReason.InvalidDependencyContract,
        "The package manifest contains an invalid dependency declaration.")]
    [InlineData(
        PackageManifestFailureReason.ConfiguredLimitExceeded,
        "The package manifest exceeds a configured resource limit.")]
    public void FailureMessage_IsStableForEveryReason(
        PackageManifestFailureReason reason,
        string expectedMessage)
    {
        var failure = new PackageManifestFailure(reason);

        Assert.Equal(expectedMessage, failure.Message);
    }

    [Fact]
    public void FailureMessage_IsSafeForUnknownFutureReason()
    {
        var failure = new PackageManifestFailure(
            (PackageManifestFailureReason)int.MaxValue);

        Assert.Equal(
            "The package manifest could not be projected.",
            failure.Message);
    }

    private static PackageManifestFacts Available(
        PackageManifestFactsResult result) =>
        Assert.IsType<PackageManifestFactsResult.Available>(result).Value;
}
