using DotnetInspector.Fixtures;
using System.Xml.Linq;

namespace DotnetInspector.FixtureInfrastructure.Tests;

public class RepositoryLayoutTests
{
    [Fact]
    public void CatalogedFixtureProjectsLiveUnderOwnerDirectories()
    {
        foreach (FixtureDefinition fixture in FixtureCatalog.All)
        {
            string[] segments = fixture.RepositoryProjectDirectory.Split(
                ['/', '\\'],
                StringSplitOptions.RemoveEmptyEntries);

            Assert.True(
                segments.Length >= 3
                    && segments[0] == "fixtures"
                    && !string.IsNullOrWhiteSpace(segments[1]),
                $"Fixture '{fixture.Id}' project path "
                    + $"'{fixture.RepositoryProjectDirectory}' must be under "
                    + "'fixtures/<owner>/'.");
        }
    }

    [Fact]
    public void RoleNamedSolutionFoldersMatchProjectRoots()
    {
        var roles = new HashSet<string>(
            ["eng", "fixtures", "src", "tests", "tools"],
            StringComparer.Ordinal);
        string repository = FindRepositoryRoot();
        XDocument solution = XDocument.Load(
            Path.Combine(repository, "dotnet-inspect.slnx"));

        foreach (XElement folder in solution.Root!.Elements("Folder"))
        {
            string folderName = Assert.IsType<string>(
                folder.Attribute("Name")?.Value);
            string role = folderName
                .Trim('/')
                .Split('/', StringSplitOptions.RemoveEmptyEntries)[0];
            if (!roles.Contains(role))
                continue;

            foreach (XElement project in folder.Elements("Project"))
            {
                string projectPath = Assert.IsType<string>(
                    project.Attribute("Path")?.Value);
                string projectRoot = projectPath.Split(
                    ['/', '\\'],
                    StringSplitOptions.RemoveEmptyEntries)[0];

                Assert.Equal(role, projectRoot);
            }
        }
    }

    static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(
                Path.Combine(directory.FullName, "dotnet-inspect.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException(
            "Could not find repository root containing dotnet-inspect.slnx.");
    }
}
