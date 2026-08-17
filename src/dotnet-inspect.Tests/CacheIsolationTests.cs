using System.Reflection;

namespace DotnetInspector.Tests;

public sealed class CacheIsolationTests
{
    [Fact]
    public void ConsoleCollection_IsAssemblyExclusive()
    {
        var definition = Assert.Single(
            typeof(ConsoleCollection)
                .GetCustomAttributes<CollectionDefinitionAttribute>());

        Assert.Equal(ConsoleCollection.Name, definition.Name);
        Assert.True(definition.DisableParallelization);
    }
}
