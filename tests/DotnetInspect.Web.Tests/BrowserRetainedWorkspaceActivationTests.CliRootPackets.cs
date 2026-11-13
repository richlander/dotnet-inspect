using System.Text;
using System.Text.Json;
using DotnetInspector.Packages;
using DotnetInspector.Queries.Definitions;
using NuGetFetch;
using Catalog = DotnetInspect.Web.Interop.Catalog;

namespace DotnetInspect.Web.Tests;

public sealed partial class BrowserRetainedWorkspaceActivationTests
{
    // PR-fast: the same canonical packets used by CLI root activation, with the
    // real pinned archive supplied to the production Browser session store.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CatalogFacade_AdmitsCliRootWorkspacePackets(bool package)
    {
        if (package)
        {
            byte[] archive = await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory,
                "RealAssets", "FrameworkActivation", "system.text.json.10.0.0.nupkg"),
                TestContext.Current.CancellationToken);
            await BrowserPackageWorkspace.RegisterGalleryPackageAsync(new BrowserPackage(
                "System.Text.Json", "10.0.0", archive, fromCache: false,
                producerKey: NuGetCache.GetSourceKey("https://api.nuget.org/v3/index.json")));
        }
        string json = package
            ? """{"f":5,"s":[["https://api.nuget.org/v3/index.json"]],"t":[["System.Text.Json","10.0.0","net10.0",null]],"g":[[0]],"r":[],"a":null,"x":0,"v":[{"t":null,"u":{"k":"workspace"}},{"t":0}]}"""
            : """{"f":3,"t":[],"g":[],"r":[["e","ecosystem.runtime",["System"],[],[["t","DotNetRuntime"],["p","System."]]]],"a":null,"x":null,"v":[{"t":null,"u":{"k":"workspace"}}]}""";
        string packet = Convert.ToBase64String(Encoding.UTF8.GetBytes(json))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
        await Catalog.BrowserRetainedWorkspaceActivationService.ResetForTestsAsync();
        try
        {
            string response = await Catalog.CatalogExports.ActivateRetainedWorkspaceDefinition(
                "cli-root", "CLI root packet", "/workspace", packet);
            var result = Assert.IsType<Catalog.BrowserRetainedWorkspaceActivationResult>(
                JsonSerializer.Deserialize(response,
                    Catalog.BrowserCatalogJsonContext.Default.BrowserRetainedWorkspaceActivationResult));
            Assert.Equal("activated", result.Status);
            Assert.NotNull(result.Posting);
            Assert.Equal(packet, result.Posting.CanonicalPacket);
            Assert.Null(result.Posting.Definition.ActiveTabId);
            Assert.Equal(package ? 1 : 0, result.Posting.Packages.Length);
            Assert.Equal(package ? 0 : 1, result.Posting.Definition.Registrations.Length);
            WorkspaceSharePacket decoded = WorkspaceSharePacketCodec.Decode(packet,
                TestContext.Current.CancellationToken);
            Assert.Null(decoded.FocusedTabIndex);
            Assert.Equal(json, WorkspaceSharePacketCodec.SerializeJson(decoded));
            Assert.Null(result.Failure);
        }
        finally
        {
            await Catalog.BrowserRetainedWorkspaceActivationService.ResetForTestsAsync();
        }
    }
}
