using DotnetInspect.Cli.Options;

namespace DotnetInspect.Cli.Tests;

public class RelationshipProjectScopeOptionsTests
{
    [Fact]
    public void ProjectScope_CountsAsAssemblySourceScope()
    {
        Assert.True(new ImplementsOptions { Projects = ["App.csproj"] }.HasAnyScope);
        Assert.True(new ExtensionsOptions { Projects = ["App.csproj"] }.HasAnyScope);
        Assert.True(new DependsOptions { Projects = ["App.csproj"] }.HasAnyScope);
    }
}
