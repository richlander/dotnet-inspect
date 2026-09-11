using System.Reflection;

namespace DotnetInspect.Cli.Tests;

public sealed class CacheIsolationTests
{
    [Fact]
    public void ConsoleCollection_IsAssemblyExclusive()
    {
        var definition = Assert.Single(
            typeof(ConsoleCollection)
                .GetCustomAttributes<CollectionDefinitionAttribute>());

        Assert.Equal("Console", definition.Name);
        Assert.True(definition.DisableParallelization);
    }
}
