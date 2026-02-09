namespace DotnetInspector.Services.Tests;

public class NuspecDependencyParserTests
{
    [Fact]
    public void Parse_WithGroupedDependencies()
    {
        var nuspec = """
        <?xml version="1.0" encoding="utf-8"?>
        <package xmlns="http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd">
          <metadata>
            <id>TestPackage</id>
            <version>1.0.0</version>
            <authors>Test Author</authors>
            <dependencies>
              <group targetFramework="net8.0">
                <dependency id="Newtonsoft.Json" version="13.0.1" />
                <dependency id="System.Text.Json" version="8.0.0" />
              </group>
              <group targetFramework="netstandard2.0">
                <dependency id="Newtonsoft.Json" version="12.0.0" />
              </group>
            </dependencies>
          </metadata>
        </package>
        """;

        var path = WriteTempFile(nuspec, ".nuspec");
        try
        {
            var (groups, author) = NuspecDependencyParser.Parse(path);

            Assert.Equal("Test Author", author);
            Assert.NotNull(groups);
            Assert.Equal(2, groups.Count);

            var net8 = groups.First(g => g.TargetFramework == "net8.0");
            Assert.Equal(2, net8.Dependencies.Count);
            Assert.Contains(net8.Dependencies, d => d.Id == "Newtonsoft.Json");
            Assert.Contains(net8.Dependencies, d => d.Id == "System.Text.Json");

            var netstd = groups.First(g => g.TargetFramework == "netstandard2.0");
            Assert.Single(netstd.Dependencies);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Parse_WithUngroupedDependencies()
    {
        var nuspec = """
        <?xml version="1.0" encoding="utf-8"?>
        <package xmlns="http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd">
          <metadata>
            <id>OldPackage</id>
            <version>1.0.0</version>
            <authors>Old Author</authors>
            <dependencies>
              <dependency id="SomeDep" version="1.0.0" />
            </dependencies>
          </metadata>
        </package>
        """;

        var path = WriteTempFile(nuspec, ".nuspec");
        try
        {
            var (groups, author) = NuspecDependencyParser.Parse(path);

            Assert.Equal("Old Author", author);
            Assert.NotNull(groups);
            Assert.Single(groups);
            Assert.Equal("any", groups[0].TargetFramework);
            Assert.Single(groups[0].Dependencies);
            Assert.Equal("SomeDep", groups[0].Dependencies[0].Id);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Parse_NoDependencies_ReturnsNullGroups()
    {
        var nuspec = """
        <?xml version="1.0" encoding="utf-8"?>
        <package xmlns="http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd">
          <metadata>
            <id>NoDeps</id>
            <version>1.0.0</version>
            <authors>Author</authors>
          </metadata>
        </package>
        """;

        var path = WriteTempFile(nuspec, ".nuspec");
        try
        {
            var (groups, author) = NuspecDependencyParser.Parse(path);

            Assert.Equal("Author", author);
            Assert.Null(groups);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Parse_EmptyGroup_NotAdded()
    {
        var nuspec = """
        <?xml version="1.0" encoding="utf-8"?>
        <package xmlns="http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd">
          <metadata>
            <id>EmptyGroup</id>
            <version>1.0.0</version>
            <dependencies>
              <group targetFramework="net8.0">
              </group>
            </dependencies>
          </metadata>
        </package>
        """;

        var path = WriteTempFile(nuspec, ".nuspec");
        try
        {
            var (groups, _) = NuspecDependencyParser.Parse(path);
            Assert.Null(groups);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Parse_NoMetadata_ReturnsNulls()
    {
        var nuspec = """
        <?xml version="1.0" encoding="utf-8"?>
        <package xmlns="http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd">
        </package>
        """;

        var path = WriteTempFile(nuspec, ".nuspec");
        try
        {
            var (groups, author) = NuspecDependencyParser.Parse(path);

            Assert.Null(groups);
            Assert.Null(author);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static string WriteTempFile(string content, string extension)
    {
        var path = Path.Combine(Path.GetTempPath(), $"nuspec-test-{Guid.NewGuid():N}{extension}");
        File.WriteAllText(path, content);
        return path;
    }
}
