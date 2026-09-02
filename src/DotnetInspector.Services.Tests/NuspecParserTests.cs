namespace DotnetInspector.Services.Tests;

/// <summary>
/// Tests for NuspecParser XML parsing functionality.
/// </summary>
public class NuspecParserTests : IDisposable
{
    private readonly string _tempDir;

    public NuspecParserTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"nuspec-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    private string WriteNuspec(string content)
    {
        var path = Path.Combine(_tempDir, $"test-{Guid.NewGuid():N}.nuspec");
        File.WriteAllText(path, content);
        return path;
    }

    [Fact]
    public void Parse_ExtractsBasicMetadata()
    {
        var nuspec = WriteNuspec("""
            <?xml version="1.0" encoding="utf-8"?>
            <package xmlns="http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd">
              <metadata>
                <id>MyPackage</id>
                <version>1.2.3</version>
                <description>A test package</description>
                <authors>Test Author</authors>
              </metadata>
            </package>
            """);

        var result = NuspecParser.Parse(nuspec);

        Assert.Equal("MyPackage", result.PackageName);
        Assert.Equal("1.2.3", result.Version);
        Assert.Equal("A test package", result.Description?.ToString());
        Assert.Equal("Test Author", result.Authors);
    }

    [Fact]
    public void Parse_ContainsDescriptionAtTheNuspecBoundary()
    {
        var nuspec = WriteNuspec("""
            <package>
              <metadata>
                <id>Hostile.Description</id>
                <version>1.0.0</version>
                <description>first
            ## package heading
            value&#x202E;tail</description>
              </metadata>
            </package>
            """);

        var result = NuspecParser.Parse(nuspec);

        Assert.NotNull(result.Description);
        Assert.Equal(
            "first\n## package heading\nvalue\\u202Etail",
            result.Description.Value.ToString().ReplaceLineEndings("\n"));
    }

    [Fact]
    public void Parse_MalformedXml_RejectsWithoutQuotingArtifactText()
    {
        var nuspec = WriteNuspec("""
            <package>
              <metadata>
                <id>SHOULD-NOT-REACH-THE-DIAGNOSTIC</metadata>
              </metadata>
            </package>
            """);

        var exception = Assert.Throws<NuspecParseException>(() => NuspecParser.Parse(nuspec));

        Assert.True(exception.LineNumber > 0);
        Assert.True(exception.LinePosition > 0);
        Assert.Contains("Package manifest is not well-formed XML", exception.Message);
        Assert.DoesNotContain("SHOULD-NOT-REACH-THE-DIAGNOSTIC", exception.Message);
        Assert.Null(exception.InnerException);
    }

    [Fact]
    public void ParseContent_MalformedXml_UsesTheSameTypedRejection()
    {
        var exception = Assert.Throws<NuspecParseException>(
            () => NuspecParser.ParseContent("<package><metadata></package>"));

        Assert.True(exception.LineNumber > 0);
        Assert.True(exception.LinePosition > 0);
        Assert.DoesNotContain("metadata", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ParseContent_Utf8BomDecodedAsText_IsAccepted()
    {
        NuspecData result = NuspecParser.ParseContent(
            "\uFEFF"
            + """
              <?xml version="1.0" encoding="utf-8"?>
              <package>
                <metadata>
                  <id>Bom.Package</id>
                  <version>1.0.0</version>
                </metadata>
              </package>
              """);

        Assert.Equal("Bom.Package", result.PackageName);
        Assert.Equal("1.0.0", result.Version);
    }

    [Fact]
    public void Parse_ExtractsRepositoryUrl()
    {
        var nuspec = WriteNuspec("""
            <?xml version="1.0" encoding="utf-8"?>
            <package xmlns="http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd">
              <metadata>
                <id>MyPackage</id>
                <version>1.0.0</version>
                <repository type="git" url="https://github.com/test/repo" />
              </metadata>
            </package>
            """);

        var result = NuspecParser.Parse(nuspec);

        Assert.Equal("https://github.com/test/repo", result.Repository);
    }

    [Fact]
    public void Parse_LicenseExpression_IsExtracted()
    {
        var nuspec = WriteNuspec("""
            <?xml version="1.0" encoding="utf-8"?>
            <package xmlns="http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd">
              <metadata>
                <id>MyPackage</id>
                <version>1.0.0</version>
                <license type="expression">MIT</license>
              </metadata>
            </package>
            """);

        var result = NuspecParser.Parse(nuspec);

        Assert.Equal("MIT", result.License);
    }

    [Fact]
    public void Parse_LicenseFile_ShowsFileReference()
    {
        var nuspec = WriteNuspec("""
            <?xml version="1.0" encoding="utf-8"?>
            <package xmlns="http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd">
              <metadata>
                <id>MyPackage</id>
                <version>1.0.0</version>
                <license type="file">LICENSE.txt</license>
              </metadata>
            </package>
            """);

        var result = NuspecParser.Parse(nuspec);

        Assert.Equal("(file: LICENSE.txt)", result.License);
    }

    [Fact]
    public void Parse_LicenseUrl_ExtractsExpressionFromNuGetOrgUrl()
    {
        var nuspec = WriteNuspec("""
            <?xml version="1.0" encoding="utf-8"?>
            <package xmlns="http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd">
              <metadata>
                <id>MyPackage</id>
                <version>1.0.0</version>
                <licenseUrl>https://licenses.nuget.org/Apache-2.0</licenseUrl>
              </metadata>
            </package>
            """);

        var result = NuspecParser.Parse(nuspec);

        Assert.Equal("Apache-2.0", result.License);
    }

    [Fact]
    public void Parse_DotnetToolPackageType_SetsIsToolPackage()
    {
        var nuspec = WriteNuspec("""
            <?xml version="1.0" encoding="utf-8"?>
            <package xmlns="http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd">
              <metadata>
                <id>MyTool</id>
                <version>1.0.0</version>
                <packageTypes>
                  <packageType name="DotnetTool" />
                </packageTypes>
              </metadata>
            </package>
            """);

        var result = NuspecParser.Parse(nuspec);

        Assert.True(result.IsToolPackage);
        Assert.NotNull(result.PackageTypes);
        Assert.Contains("DotnetTool", result.PackageTypes);
    }

    [Fact]
    public void Parse_MultiplePackageTypes_AllExtracted()
    {
        var nuspec = WriteNuspec("""
            <?xml version="1.0" encoding="utf-8"?>
            <package xmlns="http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd">
              <metadata>
                <id>MyPackage</id>
                <version>1.0.0</version>
                <packageTypes>
                  <packageType name="Analyzer" />
                  <packageType name="Dependency" />
                </packageTypes>
              </metadata>
            </package>
            """);

        var result = NuspecParser.Parse(nuspec);

        Assert.NotNull(result.PackageTypes);
        Assert.Equal(2, result.PackageTypes.Count);
        Assert.Contains("Analyzer", result.PackageTypes);
        Assert.Contains("Dependency", result.PackageTypes);
        Assert.False(result.IsToolPackage);
    }

    [Fact]
    public void Parse_ReadmeFile_IsExtracted()
    {
        var nuspec = WriteNuspec("""
            <?xml version="1.0" encoding="utf-8"?>
            <package xmlns="http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd">
              <metadata>
                <id>MyPackage</id>
                <version>1.0.0</version>
                <readme>docs/README.md</readme>
              </metadata>
            </package>
            """);

        var result = NuspecParser.Parse(nuspec);

        Assert.Equal("docs/README.md", result.ReadmeFile);
    }

    [Fact]
    public void Parse_IconMetadata_IsExtracted()
    {
        var nuspec = WriteNuspec("""
            <?xml version="1.0" encoding="utf-8"?>
            <package xmlns="http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd">
              <metadata>
                <id>MyPackage</id>
                <version>1.0.0</version>
                <icon>images\package.png</icon>
                <iconUrl>https://example.test/legacy.png</iconUrl>
              </metadata>
            </package>
            """);

        var result = NuspecParser.Parse(nuspec);

        Assert.Equal(@"images\package.png", result.IconFile);
        Assert.Equal("https://example.test/legacy.png", result.IconUrl);
    }

    [Fact]
    public void Parse_GroupedDependencies_AreExtracted()
    {
        var nuspec = WriteNuspec("""
            <?xml version="1.0" encoding="utf-8"?>
            <package xmlns="http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd">
              <metadata>
                <id>MyPackage</id>
                <version>1.0.0</version>
                <authors>Test Author</authors>
                <dependencies>
                  <group targetFramework="net8.0">
                    <dependency id="System.Text.Json" version="8.0.0" />
                    <dependency id="Microsoft.Extensions.Logging" version="8.0.0" />
                  </group>
                  <group targetFramework="netstandard2.0">
                    <dependency id="Newtonsoft.Json" version="13.0.1" />
                  </group>
                </dependencies>
              </metadata>
            </package>
            """);

        var result = NuspecParser.Parse(nuspec);

        Assert.NotNull(result.DependencyGroups);
        Assert.Equal(2, result.DependencyGroups.Count);

        var net8Group = result.DependencyGroups.First(g => g.TargetFramework == "net8.0");
        Assert.Equal(2, net8Group.Dependencies.Count);
        Assert.Contains(net8Group.Dependencies, d => d.Id == "System.Text.Json" && d.Version == "8.0.0");

        var netstdGroup = result.DependencyGroups.First(g => g.TargetFramework == "netstandard2.0");
        Assert.Single(netstdGroup.Dependencies);
    }

    [Fact]
    public void Parse_UngroupedDependencies_AreExtractedAsAny()
    {
        var nuspec = WriteNuspec("""
            <?xml version="1.0" encoding="utf-8"?>
            <package xmlns="http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd">
              <metadata>
                <id>MyPackage</id>
                <version>1.0.0</version>
                <dependencies>
                  <dependency id="SomeDependency" version="1.0.0" />
                </dependencies>
              </metadata>
            </package>
            """);

        var result = NuspecParser.Parse(nuspec);

        Assert.NotNull(result.DependencyGroups);
        Assert.Single(result.DependencyGroups);
        Assert.Equal("any", result.DependencyGroups[0].TargetFramework);
        Assert.Single(result.DependencyGroups[0].Dependencies);
    }

    [Fact]
    public void Parse_EmptyDependencyGroup_IsAdded()
    {
        var nuspec = WriteNuspec("""
            <?xml version="1.0" encoding="utf-8"?>
            <package xmlns="http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd">
              <metadata>
                <id>MyPackage</id>
                <version>1.0.0</version>
                <dependencies>
                  <group targetFramework="net8.0">
                  </group>
                </dependencies>
              </metadata>
            </package>
            """);

        var result = NuspecParser.Parse(nuspec);

        Assert.NotNull(result.DependencyGroups);
        Assert.Single(result.DependencyGroups);
        Assert.Equal("net8.0", result.DependencyGroups[0].TargetFramework);
        Assert.Empty(result.DependencyGroups[0].Dependencies);
    }

    [Fact]
    public void Parse_NoMetadata_ReturnsEmptyResult()
    {
        var nuspec = WriteNuspec("""
            <?xml version="1.0" encoding="utf-8"?>
            <package xmlns="http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd">
            </package>
            """);

        var result = NuspecParser.Parse(nuspec);

        Assert.Null(result.PackageName);
        Assert.Null(result.Version);
    }

    [Fact]
    public void Parse_NoNamespace_StillWorks()
    {
        var nuspec = WriteNuspec("""
            <?xml version="1.0" encoding="utf-8"?>
            <package>
              <metadata>
                <id>NoNamespacePackage</id>
                <version>2.0.0</version>
              </metadata>
            </package>
            """);

        var result = NuspecParser.Parse(nuspec);

        Assert.Equal("NoNamespacePackage", result.PackageName);
        Assert.Equal("2.0.0", result.Version);
    }

    [Fact]
    public void Parse_MetadataDeclaredNuspecNamespace_UsesThatNamespace()
    {
        var nuspec = WriteNuspec("""
            <?xml version="1.0" encoding="utf-8"?>
            <package xmlns:xsd="http://www.w3.org/2001/XMLSchema"
                     xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance">
              <metadata xmlns="http://schemas.microsoft.com/packaging/2010/07/nuspec.xsd">
                <id>Legacy.Package</id>
                <version>1.0.0</version>
                <authors>Legacy Author</authors>
                <dependencies>
                  <dependency id="Legacy.Dependency" version="2.0.0" />
                </dependencies>
              </metadata>
            </package>
            """);

        NuspecData result = NuspecParser.Parse(nuspec);

        Assert.Equal("2010/07", result.ManifestVersion);
        Assert.Equal("Legacy.Package", result.PackageName);
        Assert.Equal("1.0.0", result.Version);
        Assert.Equal("Legacy Author", result.Authors);
        Assert.Equal(
            "Legacy.Dependency",
            Assert.Single(
                Assert.Single(result.DependencyGroups!).Dependencies).Id);
    }

    [Theory]
    [InlineData(
        "<package><metadata><id>Example.Package</id><version>1.0.0</version></metadata></package>",
        "nuspec")]
    [InlineData(
        "<package xmlns=\"http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd\"><metadata><id>Example.Package</id><version>1.0.0</version></metadata></package>",
        "2013/05")]
    [InlineData(
        "<package><metadata xmlns=\"http://schemas.microsoft.com/packaging/2010/07/nuspec.xsd\"><id>Example.Package</id><version>1.0.0</version></metadata></package>",
        "2010/07")]
    public void ParseContent_SupportedPackageAndMetadataNamespaceFormsAreAccepted(
        string xml,
        string manifestVersion)
    {
        NuspecData result = NuspecParser.ParseContent(xml);

        Assert.Equal("Example.Package", result.PackageName);
        Assert.Equal("1.0.0", result.Version);
        Assert.Equal(manifestVersion, result.ManifestVersion);
    }

    [Theory]
    [InlineData(
        "<package xmlns=\"http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd\"><metadata xmlns=\"\"><id>Example.Package</id><version>1.0.0</version></metadata></package>")]
    [InlineData(
        "<package xmlns=\"http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd\"><metadata xmlns=\"http://schemas.microsoft.com/packaging/2010/07/nuspec.xsd\"><id>Example.Package</id><version>1.0.0</version></metadata></package>")]
    public void ParseContent_IncompatibleNuspecMetadataNamespaceIsRejected(
        string xml)
    {
        InvalidDataException exception =
            Assert.Throws<InvalidDataException>(
                () => NuspecParser.ParseContent(xml));

        Assert.Contains(
            "metadata namespace",
            exception.Message,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "Example.Package",
            exception.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void ParseContent_DuplicateCompatibleMetadataIsRejected()
    {
        const string xml = """
            <package>
              <metadata>
                <id>First.Package</id>
                <version>1.0.0</version>
              </metadata>
              <metadata xmlns="http://schemas.microsoft.com/packaging/2010/07/nuspec.xsd">
                <id>Second.Package</id>
                <version>2.0.0</version>
              </metadata>
            </package>
            """;

        InvalidDataException exception =
            Assert.Throws<InvalidDataException>(
                () => NuspecParser.ParseContent(xml));

        Assert.Contains(
            "multiple metadata elements",
            exception.Message,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "First.Package",
            exception.Message,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "Second.Package",
            exception.Message,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("https://example.test/extension")]
    [InlineData(" ")]
    public void ParseContent_ForeignDirectMetadataIsNotPackageMetadata(
        string metadataNamespace)
    {
        NuspecData result = NuspecParser.ParseContent(
            $$"""
            <package>
              <metadata xmlns="{{metadataNamespace}}">
                <id>Foreign.Package</id>
                <version>1.0.0</version>
              </metadata>
            </package>
            """);

        Assert.Null(result.PackageName);
        Assert.Null(result.Version);
    }

    [Fact]
    public void ParseContent_NestedMetadataIsNotPackageMetadata()
    {
        NuspecData result = NuspecParser.ParseContent(
            """
            <package>
              <extension>
                <metadata>
                  <id>Nested.Package</id>
                  <version>1.0.0</version>
                </metadata>
              </extension>
            </package>
            """);

        Assert.Null(result.PackageName);
        Assert.Null(result.Version);
    }

    [Fact]
    public void ParseContent_ForeignMetadataSiblingDoesNotShadowPackageMetadata()
    {
        NuspecData result = NuspecParser.ParseContent(
            """
            <package>
              <metadata xmlns="https://example.test/extension">
                <id>Foreign.Package</id>
                <version>1.0.0</version>
              </metadata>
              <metadata>
                <id>Example.Package</id>
                <version>2.0.0</version>
              </metadata>
            </package>
            """);

        Assert.Equal("Example.Package", result.PackageName);
        Assert.Equal("2.0.0", result.Version);
    }

    [Theory]
    [InlineData(
        "<notpackage><metadata><id>Example.Package</id><version>1.0.0</version></metadata></notpackage>")]
    [InlineData(
        "<package xmlns=\"https://example.test/not-nuspec\"><metadata><id>Example.Package</id><version>1.0.0</version></metadata></package>")]
    [InlineData(
        "<package xmlns=\" \"><metadata><id>Example.Package</id><version>1.0.0</version></metadata></package>")]
    public void ParseContent_InvalidDocumentRootIsRejected(string xml)
    {
        InvalidDataException exception =
            Assert.Throws<InvalidDataException>(
                () => NuspecParser.ParseContent(xml));

        Assert.Contains(
            "invalid document root",
            exception.Message,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "Example.Package",
            exception.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void FindNuspec_NoNuspecInPackageDirectory_ReturnsNull()
    {
        Assert.Null(NuspecParser.FindNuspec(_tempDir));
    }

    [Fact]
    public void FindNuspec_NuspecInPackageDirectory_ReturnsPath()
    {
        var nuspec = WriteNuspec("""
            <?xml version="1.0" encoding="utf-8"?>
            <package>
              <metadata>
                <id>FindMe</id>
                <version>1.0.0</version>
              </metadata>
            </package>
            """);

        Assert.Equal(nuspec, NuspecParser.FindNuspec(_tempDir));
    }

    [Fact]
    public void FindNuspec_NuspecOnlyInNestedDirectory_ReturnsNull()
    {
        var nestedDir = Path.Combine(_tempDir, "nested");
        Directory.CreateDirectory(nestedDir);
        File.WriteAllText(Path.Combine(nestedDir, "nested.nuspec"), """
            <?xml version="1.0" encoding="utf-8"?>
            <package>
              <metadata>
                <id>Nested</id>
                <version>1.0.0</version>
              </metadata>
            </package>
            """);

        Assert.Null(NuspecParser.FindNuspec(_tempDir));
    }

    [Fact]
    public void FindAndParse_NuspecInPackageDirectory_ParsesMetadata()
    {
        WriteNuspec("""
            <?xml version="1.0" encoding="utf-8"?>
            <package>
              <metadata>
                <id>ParsedFromDirectory</id>
                <version>2.3.4</version>
              </metadata>
            </package>
            """);

        var result = NuspecParser.FindAndParse(_tempDir);

        Assert.NotNull(result);
        Assert.Equal("ParsedFromDirectory", result.PackageName);
        Assert.Equal("2.3.4", result.Version);
    }
}
