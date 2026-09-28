using DotnetInspector.Fixtures;

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
}
