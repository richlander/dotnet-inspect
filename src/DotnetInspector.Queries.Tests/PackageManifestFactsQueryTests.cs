using System.Text;
using InertText;
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
        Assert.Equal(
            PackageManifestIdentityProvenance.ExpectedCoordinate,
            facts.IdentityProvenance);
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
    public void ExecuteSelfAttested_ProjectsEquivalentFactsWithTypedProvenance()
    {
        byte[] manifestBytes = Encoding.UTF8.GetBytes(
            """
            <package>
              <metadata>
                <id>Example.Package</id>
                <version>1.0</version>
                <authors>Example Authors</authors>
                <description>Example description</description>
                <dependencies>
                  <group targetFramework="net8.0">
                    <dependency id="Example.Dependency" version="[2.0.0]" />
                  </group>
                </dependencies>
              </metadata>
            </package>
            """);
        PackageManifestFacts expected = Available(
            PackageManifestFactsQuery.Execute(
                manifestBytes,
                PackageSourceCoordinate.Create(
                    "Example.Package",
                    "1.0.0")));
        PackageManifestFacts selfAttested = Available(
            PackageManifestFactsQuery.ExecuteSelfAttested(
                manifestBytes));

        Assert.Equal(
            PackageManifestIdentityProvenance.ExpectedCoordinate,
            expected.IdentityProvenance);
        Assert.Equal(
            PackageManifestIdentityProvenance.SelfAttested,
            selfAttested.IdentityProvenance);
        AssertEquivalentFacts(expected, selfAttested);
    }

    [Fact]
    public void ExecuteSelfAttested_PreservesHostileDescriptionAsInertText()
    {
        const string Hostile = "first\u202E\nsecond";
        PackageManifestFacts facts = Available(
            PackageManifestFactsQuery.ExecuteSelfAttested(
                Encoding.UTF8.GetBytes(
                    $$"""
                    <package>
                      <metadata>
                        <id>Example.Package</id>
                        <version>1.0.0</version>
                        <description>{{Hostile}}</description>
                      </metadata>
                    </package>
                    """)));

        InertString description = Assert.IsType<InertString>(
            facts.Description);
        string text = description.ToString();
        Assert.True(
            InertString.IsPermitted(
                TextPolicy.Prose,
                text));
        Assert.DoesNotContain(
            '\u202E',
            text);
        Assert.Contains(
            @"\u202E",
            text,
            StringComparison.Ordinal);
        Assert.Contains(
            '\n',
            text);
    }

    [Theory]
    [InlineData("$id$", "1.0.0")]
    [InlineData("Example.Package", "$version$")]
    [InlineData("evil/../other", "1.0.0")]
    [InlineData("Example.Package", "not-a-version")]
    public void ExecuteSelfAttested_RejectsInvalidIdentity(
        string packageId,
        string version)
    {
        PackageManifestFactsResult.Failed failure = Assert.IsType<
            PackageManifestFactsResult.Failed>(
                PackageManifestFactsQuery.ExecuteSelfAttested(
                    Encoding.UTF8.GetBytes(
                        $$"""
                        <package>
                          <metadata>
                            <id>{{packageId}}</id>
                            <version>{{version}}</version>
                          </metadata>
                        </package>
                        """)));

        Assert.Equal(
            PackageManifestFailureReason.InvalidIdentityContract,
            failure.Failure.Reason);
        Assert.DoesNotContain(
            packageId,
            failure.Failure.Message,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            version,
            failure.Failure.Message,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("<version>1.0.0</version>")]
    [InlineData("<id>Example.Package</id>")]
    public void ExecuteSelfAttested_RejectsMissingIdentity(
        string identityElement)
    {
        PackageManifestFactsResult.Failed failure = Assert.IsType<
            PackageManifestFactsResult.Failed>(
                PackageManifestFactsQuery.ExecuteSelfAttested(
                    Encoding.UTF8.GetBytes(
                        $$"""
                        <package>
                          <metadata>
                            {{identityElement}}
                          </metadata>
                        </package>
                        """)));

        Assert.Equal(
            PackageManifestFailureReason.UnsupportedDocumentShape,
            failure.Failure.Reason);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ExecuteSelfAttested_EnforcesIdentityScalarLimit(
        bool oversizedPackageId)
    {
        string oversized = new(
            'a',
            PackageManifestFactsQuery.MaxScalarCharacters + 1);
        string packageId = oversizedPackageId
            ? oversized
            : "Example.Package";
        string version = oversizedPackageId
            ? "1.0.0"
            : oversized;
        PackageManifestFactsResult.Failed failure = Assert.IsType<
            PackageManifestFactsResult.Failed>(
                PackageManifestFactsQuery.ExecuteSelfAttested(
                    Encoding.UTF8.GetBytes(
                        $$"""
                        <package>
                          <metadata>
                            <id>{{packageId}}</id>
                            <version>{{version}}</version>
                          </metadata>
                        </package>
                        """)));

        Assert.Equal(
            PackageManifestFailureReason.ConfiguredLimitExceeded,
            failure.Failure.Reason);
    }

    [Fact]
    public void ExecuteSelfAttested_EnforcesManifestByteLimit()
    {
        PackageManifestFactsResult.Failed failure = Assert.IsType<
            PackageManifestFactsResult.Failed>(
                PackageManifestFactsQuery.ExecuteSelfAttested(
                    new byte[
                        PackageManifestFactsQuery.MaxManifestBytes + 1]));

        Assert.Equal(
            PackageManifestFailureReason.ConfiguredLimitExceeded,
            failure.Failure.Reason);
    }

    [Fact]
    public void ExecuteSelfAttested_RejectsManifestBeyondDecodedCharacterLimit()
    {
        string description = new(
            'a',
            PackageManifestFactsQuery.MaxManifestCharacters + 1);
        PackageManifestFactsResult.Failed failure = Assert.IsType<
            PackageManifestFactsResult.Failed>(
                PackageManifestFactsQuery.ExecuteSelfAttested(
                    Encoding.UTF8.GetBytes(
                        $$"""
                        <package>
                          <metadata>
                            <id>Example.Package</id>
                            <version>1.0.0</version>
                            <description>{{description}}</description>
                          </metadata>
                        </package>
                        """)));

        Assert.Equal(
            PackageManifestFailureReason.MalformedXml,
            failure.Failure.Reason);
    }

    [Fact]
    public void ExecuteSelfAttested_RejectsMalformedXml()
    {
        PackageManifestFactsResult.Failed failure = Assert.IsType<
            PackageManifestFactsResult.Failed>(
                PackageManifestFactsQuery.ExecuteSelfAttested(
                    Encoding.UTF8.GetBytes(
                        """
                        <package>
                          <metadata>
                            <id>Example.Package</id>
                        </package>
                        """)));

        Assert.Equal(
            PackageManifestFailureReason.MalformedXml,
            failure.Failure.Reason);
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
    public void Execute_AcceptsManifestAtExactByteLimit()
    {
        byte[] manifestBytes = Encoding.UTF8.GetBytes(
            PadToManifestByteCount(
                MinimalManifest(),
                PackageManifestFactsQuery.MaxManifestBytes));

        PackageManifestFacts facts = Available(
            PackageManifestFactsQuery.Execute(
                manifestBytes,
                PackageSourceCoordinate.Create(
                    "Example.Package",
                    "1.0.0")));

        Assert.Equal(
            PackageManifestFactsQuery.MaxManifestBytes,
            manifestBytes.Length);
        Assert.Equal(
            PackageSourceCoordinate.Create(
                "Example.Package",
                "1.0.0"),
            facts.Coordinate);
    }

    [Fact]
    public void Execute_AcceptsManifestAtExactDecodedCharacterLimit()
    {
        string manifest = PadToCharacterCount(
            MinimalManifest(),
            PackageManifestFactsQuery.MaxManifestCharacters);

        PackageManifestFacts facts = Available(
            PackageManifestFactsQuery.Execute(
                Encoding.UTF8.GetBytes(manifest),
                PackageSourceCoordinate.Create(
                    "Example.Package",
                    "1.0.0")));

        Assert.Equal(
            PackageManifestFactsQuery.MaxManifestCharacters,
            manifest.Length);
        Assert.Equal(
            PackageSourceCoordinate.Create(
                "Example.Package",
                "1.0.0"),
            facts.Coordinate);
    }

    [Fact]
    public void Execute_RejectsManifestBeyondDecodedCharacterLimit()
    {
        string manifest = PadToCharacterCount(
            MinimalManifest(),
            PackageManifestFactsQuery.MaxManifestCharacters + 1);
        PackageManifestFactsResult.Failed failure = Assert.IsType<
            PackageManifestFactsResult.Failed>(
                PackageManifestFactsQuery.Execute(
                    Encoding.UTF8.GetBytes(manifest),
                    PackageSourceCoordinate.Create(
                        "Example.Package",
                        "1.0.0")));

        Assert.Equal(
            PackageManifestFailureReason.MalformedXml,
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
        PackageManifestFailureReason.InvalidIdentityContract,
        "The package manifest contains an invalid package identity.")]
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

    private static void AssertEquivalentFacts(
        PackageManifestFacts expected,
        PackageManifestFacts actual)
    {
        Assert.Equal(expected.Coordinate, actual.Coordinate);
        Assert.Equal(expected.ManifestVersion, actual.ManifestVersion);
        Assert.Equal(
            expected.Description?.ToString(),
            actual.Description?.ToString());
        Assert.Equal(expected.Authors, actual.Authors);
        Assert.Equal(expected.Repository, actual.Repository);
        Assert.Equal(expected.RepositoryType, actual.RepositoryType);
        Assert.Equal(expected.RepositoryCommit, actual.RepositoryCommit);
        Assert.Equal(expected.License, actual.License);
        Assert.Equal(expected.LicenseUrl, actual.LicenseUrl);
        Assert.Equal(expected.PackageTypes, actual.PackageTypes);
        Assert.Equal(expected.IsToolPackage, actual.IsToolPackage);
        Assert.Equal(expected.ReadmeFile, actual.ReadmeFile);
        Assert.Equal(
            expected.DependencyGroups.Length,
            actual.DependencyGroups.Length);
        for (int i = 0; i < expected.DependencyGroups.Length; i++)
        {
            DeclaredPackageDependencyGroup expectedGroup =
                expected.DependencyGroups[i];
            DeclaredPackageDependencyGroup actualGroup =
                actual.DependencyGroups[i];
            Assert.Equal(
                expectedGroup.TargetFramework,
                actualGroup.TargetFramework);
            Assert.Equal(
                expectedGroup.IsImplicitManifestGroup,
                actualGroup.IsImplicitManifestGroup);
            Assert.Equal(
                expectedGroup.Dependencies,
                actualGroup.Dependencies);
        }
    }

    private static string MinimalManifest() =>
        """
        <package>
          <metadata>
            <id>Example.Package</id>
            <version>1.0.0</version>
          </metadata>
        </package>
        """;

    private static string PadToManifestByteCount(
        string manifest,
        int byteCount)
    {
        int remaining = byteCount - Encoding.UTF8.GetByteCount(manifest);
        Assert.True(remaining >= 7);
        int multibyteCharacters = (remaining - 7) / 3;
        int singleBytePadding =
            remaining - 7 - (multibyteCharacters * 3);
        string padded = manifest
            + "<!--"
            + new string('漢', multibyteCharacters)
            + new string(' ', singleBytePadding)
            + "-->";
        Assert.Equal(
            byteCount,
            Encoding.UTF8.GetByteCount(padded));
        return padded;
    }

    private static string PadToCharacterCount(
        string manifest,
        int characterCount)
    {
        int remaining = characterCount - manifest.Length;
        Assert.True(remaining >= 7);
        string padded =
            manifest + "<!--" + new string('a', remaining - 7) + "-->";
        Assert.Equal(characterCount, padded.Length);
        return padded;
    }
}
