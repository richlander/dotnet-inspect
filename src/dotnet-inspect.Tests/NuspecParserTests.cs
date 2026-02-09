using DotnetInspector;
using DotnetInspector.Inspectors;
using DotnetInspector.Packages;

namespace DotnetInspector.Tests;

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
        var path = Path.Combine(_tempDir, "test.nuspec");
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

        var result = new InspectionResult { PackageName = "Original", Version = "0.0.0" };
        NuspecParser.Parse(nuspec, result);

        Assert.Equal("MyPackage", result.PackageName);
        Assert.Equal("1.2.3", result.Version);
        Assert.Equal("A test package", result.Description);
        Assert.Equal("Test Author", result.Authors);
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

        var result = new InspectionResult();
        NuspecParser.Parse(nuspec, result);

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

        var result = new InspectionResult();
        NuspecParser.Parse(nuspec, result);

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

        var result = new InspectionResult();
        NuspecParser.Parse(nuspec, result);

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

        var result = new InspectionResult();
        NuspecParser.Parse(nuspec, result);

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

        var result = new InspectionResult();
        NuspecParser.Parse(nuspec, result);

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

        var result = new InspectionResult();
        NuspecParser.Parse(nuspec, result);

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

        var result = new InspectionResult();
        NuspecParser.Parse(nuspec, result);

        Assert.Equal("docs/README.md", result.ReadmeFile);
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

        var result = new InspectionResult();
        NuspecParser.Parse(nuspec, result);

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

        var result = new InspectionResult();
        NuspecParser.Parse(nuspec, result);

        Assert.NotNull(result.DependencyGroups);
        Assert.Single(result.DependencyGroups);
        Assert.Equal("any", result.DependencyGroups[0].TargetFramework);
        Assert.Single(result.DependencyGroups[0].Dependencies);
    }

    [Fact]
    public void Parse_EmptyDependencyGroup_IsNotAdded()
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

        var result = new InspectionResult();
        NuspecParser.Parse(nuspec, result);

        Assert.Null(result.DependencyGroups);
    }

    [Fact]
    public void Parse_NoMetadata_DoesNotThrow()
    {
        var nuspec = WriteNuspec("""
            <?xml version="1.0" encoding="utf-8"?>
            <package xmlns="http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd">
            </package>
            """);

        var result = new InspectionResult { PackageName = "Original", Version = "1.0.0" };
        NuspecParser.Parse(nuspec, result);

        // Original values preserved
        Assert.Equal("Original", result.PackageName);
        Assert.Equal("1.0.0", result.Version);
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

        var result = new InspectionResult();
        NuspecParser.Parse(nuspec, result);

        Assert.Equal("NoNamespacePackage", result.PackageName);
        Assert.Equal("2.0.0", result.Version);
    }
}
