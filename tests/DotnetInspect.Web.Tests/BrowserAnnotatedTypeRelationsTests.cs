using System.Text.Json;

using DotnetInspector.Fixtures;

namespace DotnetInspect.Web.Tests;

public sealed partial class BrowserEngineBoundaryTests
{
    [Theory]
    [InlineData(
        "System.Text.Json",
        "10.0.13",
        "net10.0",
        "System.Text.Json.dll",
        "System.Text.Json.JsonSerializer",
        "FileDemand",
        "system.text.json.10.0.13.nupkg")]
    [InlineData(
        "Newtonsoft.Json",
        "13.0.4",
        "net6.0",
        "Newtonsoft.Json.dll",
        "Newtonsoft.Json.JsonConvert",
        "FileDemand",
        "newtonsoft.json.13.0.4.nupkg")]
    public async Task AnnotatedTypeRelations_InspectRealPackageBodies(
        string packageId,
        string version,
        string framework,
        string assembly,
        string typeDefinitionId,
        string assetDirectory,
        string packageFile)
    {
        await BrowserPackageWorkspace.RegisterGalleryPackageAsync(
            new BrowserPackage(
                packageId,
                version,
                await File.ReadAllBytesAsync(
                    Path.Combine(
                        AppContext.BaseDirectory,
                        "RealAssets",
                        assetDirectory,
                        packageFile),
                    TestContext.Current.CancellationToken),
                fromCache: false,
                producerKey:
                    BrowserPackageWorkspace.Gallery.Source.Producer.Key));

        using JsonDocument result = await QueryAnnotatedTypeRelations(
            packageId,
            framework,
            assembly,
            typeDefinitionId,
            Workspace((packageId, framework, version)),
            version);

        JsonElement root = result.RootElement;
        Assert.Equal("Available", root.GetProperty("outcome").GetString());
        JsonElement content = root
            .GetProperty("inspection")
            .GetProperty("content");
        Assert.NotEmpty(content.GetProperty("relations").EnumerateArray());
        Assert.NotEmpty(content.GetProperty("occurrences").EnumerateArray());
        Assert.Equal(
            3,
            content.GetProperty("selection")
                .GetProperty("colors")
                .GetArrayLength());
    }

    [Fact]
    public async Task AnnotatedTypeRelations_UseFocusedImplementationBodies()
    {
        const string callerPackage = "Browser.Relations.Caller";
        const string targetPackage = "Browser.Relations.Target";
        const string framework = "net11.0";
        string callerAssembly = Path.GetFileName(
            FixtureCatalog.AnalysisCallerGraphCaller.AssemblyPath());
        string targetAssembly = Path.GetFileName(
            FixtureCatalog.AnalysisCallerGraphTarget.AssemblyPath());
        _ = await Coordinate(
            callerPackage,
            Package(
                File.ReadAllBytes(
                    FixtureCatalog.AnalysisCallerGraphCaller.AssemblyPath()),
                $"lib/{framework}/{callerAssembly}"));
        _ = await Coordinate(
            targetPackage,
            Package(
                File.ReadAllBytes(
                    FixtureCatalog.AnalysisCallerGraphTarget.AssemblyPath()),
                $"lib/{framework}/{targetAssembly}"));

        using JsonDocument result = await QueryAnnotatedTypeRelations(
            callerPackage,
            framework,
            callerAssembly,
            "Shared.Entry",
            Workspace(
                (callerPackage, framework, "1.0.0"),
                (targetPackage, framework, "1.0.0")));

        JsonElement root = result.RootElement;
        Assert.Equal("Available", root.GetProperty("outcome").GetString());
        JsonElement content = root
            .GetProperty("inspection")
            .GetProperty("content");
        JsonElement selection = content.GetProperty("selection");
        Assert.Equal(
            ["call"],
            selection.GetProperty("relationships")
                .EnumerateArray()
                .Select(value => value.GetString()));
        Assert.Equal("Outgoing", selection.GetProperty("direction").GetString());
        Assert.Equal("Rows", selection.GetProperty("terminal").GetString());
        Assert.Equal(
            [
                "member.requires-unreferenced-code",
                "member.requires-dynamic-code",
                "member.requires-assembly-files",
            ],
            selection.GetProperty("colors")
                .EnumerateArray()
                .Select(value => value.GetString()));
        Assert.NotEmpty(
            content.GetProperty("relations").EnumerateArray());
        Assert.NotEmpty(
            content.GetProperty("occurrences").EnumerateArray());
        Assert.NotEmpty(
            content.GetProperty("relationship_outcomes").EnumerateArray());
        Assert.True(
            content.GetProperty("terminal")
                .GetProperty("value")
                .GetInt32() > 0);
        Assert.True(
            content.TryGetProperty("annotations", out _));
        Assert.True(
            content.TryGetProperty("color_outcomes", out _));
    }

    [Fact]
    public async Task AnnotatedTypeRelations_ReportReferenceOnlyUnavailable()
    {
        const string packageId = "Browser.Relations.ReferenceOnly";
        const string framework = "net11.0";
        string assembly = Path.GetFileName(
            FixtureCatalog.AnalysisCallerGraphCaller.AssemblyPath());
        _ = await Coordinate(
            packageId,
            Package(
                File.ReadAllBytes(
                    FixtureCatalog.AnalysisCallerGraphCaller.AssemblyPath()),
                $"ref/{framework}/{assembly}"));

        using JsonDocument result = await QueryAnnotatedTypeRelations(
            packageId,
            framework,
            assembly,
            "Shared.Entry",
            Workspace((packageId, framework, "1.0.0")));

        JsonElement root = result.RootElement;
        Assert.Equal("Unavailable", root.GetProperty("outcome").GetString());
        Assert.Equal(
            JsonValueKind.Null,
            root.GetProperty("inspection").ValueKind);
        Assert.False(
            string.IsNullOrWhiteSpace(
                root.GetProperty("reason").GetString()));
    }

    static async Task<JsonDocument> QueryAnnotatedTypeRelations(
        string packageId,
        string framework,
        string assembly,
        string typeDefinitionId,
        string workspaceJson,
        string version = "1.0.0")
    {
        string json = await DotnetInspect.Web.Interop.Metadata.MetadataExports
            .QueryAnnotatedTypeRelations(
                packageId,
                version,
                framework,
                assembly,
                typeDefinitionId,
                runtimeIdentifier: null,
                workspaceJson);
        return JsonDocument.Parse(json);
    }

    static string Workspace(
        params (
            string Package,
            string Framework,
            string Version)[] packages) =>
        JsonSerializer.Serialize(
            packages.Select(package => new
            {
                package = package.Package,
                version = package.Version,
                framework = package.Framework,
            }));
}
