using DotnetInspector.Fixtures;

namespace DotnetInspect.Cli.Tests;

public sealed partial class ConfiguredPayloadAcquisitionTests
{
    [Fact]
    public async Task
        MemberCommand_ConfiguredPackageUsesPackageDocumentationHouse()
    {
        string id =
            $"Package.Documentation.{Guid.NewGuid():N}";
        string source = Path.Combine(_root, "documentation-feed");
        string assemblyName =
            FixtureCatalog.InspectWebDocumentation.AssemblyFileName;
        WriteLocalPackage(
            source,
            id,
            "Package documentation fixture.",
            library: await File.ReadAllBytesAsync(
                FixtureCatalog.InspectWebDocumentation.AssemblyPath(),
                TestContext.Current.CancellationToken),
            libraryName: assemblyName,
            documentation: await File.ReadAllBytesAsync(
                FixtureCatalog.InspectWebDocumentation.AssetPath(
                    "documentation"),
                TestContext.Current.CancellationToken));

        var (exit, output, error) = await RunCommandAsync(
            [
                "member",
                "InspectWeb.DocumentationFixtures.WidgetExtensions",
                "Measure",
                "--package",
                $"{id}@{Version}",
                "--source",
                source,
                "-v:d",
                "--tips",
                "q",
            ]);

        Assert.True(exit == 0, $"Exit {exit}\n{output}\n{error}");
        Assert.Empty(error);
        Assert.Contains(
            "Methods that extend documentation fixture types.",
            output);
        Assert.Contains(
            "Measures a widget through its declaring extension member.",
            output);
    }
}
