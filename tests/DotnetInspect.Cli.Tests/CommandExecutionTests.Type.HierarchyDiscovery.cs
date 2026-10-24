using DotnetInspect.Cli.Sections;

namespace DotnetInspect.Cli.Tests;

public partial class CommandExecutionTests
{
    [Fact]
    public async Task Type_EffectiveDiscoveryListsImplementersWhenAnyExist()
    {
        string assembly =
            typeof(IWorkspaceImplementationMarker).Assembly.Location;

        var (exit, output, error) = await RunAppAsync(
            "type",
            typeof(IWorkspaceImplementationMarker).FullName!,
            "--library",
            assembly,
            "--all",
            "-D",
            SectionNames.Implementers,
            "--effective");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains(SectionNames.Implementers, output);
    }

    [Fact]
    public async Task Type_EffectiveDiscoveryOmitsDerivedTypesForASealedType()
    {
        string assembly =
            typeof(WorkspaceImplementation).Assembly.Location;

        var (exit, output, error) = await RunAppAsync(
            "type",
            typeof(WorkspaceImplementation).FullName!,
            "--library",
            assembly,
            "--all",
            "-D",
            SectionNames.DerivedTypes,
            "--effective");

        // A sealed Type has no derived Types; the Exists probe decides that
        // without materializing a row, so discovery no longer lists the
        // section structurally and reports it as having no data instead.
        Assert.Equal(0, exit);
        Assert.DoesNotContain(SectionNames.DerivedTypes, output);
        Assert.Contains("has no data for this query", error);
    }
}
