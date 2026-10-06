using System.IO.Compression;
using System.Runtime.Versioning;
using System.Text.Json;

using DotnetInspector.LibraryMetadata;
using DotnetInspector.Packages;
using DotnetInspector.Sections;
using ILInspector.Metadata;
using NuGetFetch;
using Library = DotnetInspect.Web.Interop.Library;

namespace DotnetInspect.Web.Tests;

/// <summary>
/// Gates for the Browser Library document request (#8228 step 6) and its
/// Enablements group, over pinned Microsoft.NETCore.App 11.0.0-rc.1 assets.
/// </summary>
public sealed partial class BrowserEngineBoundaryTests
{
    const string EnablementsFramework = "net11.0";

    [Fact]
    public async Task LibraryDocument_PackageWithRefAndLib_JudgesEnablementsOnTheImplementation()
    {
        (string packageId, string assemblyId) = await RegisterSocketsPackageAsync(
            includeImplementation: true);

        Library.BrowserLibraryDocumentInspection inspection =
            await InspectPackageLibraryAsync(packageId, assemblyId, enablements: true);

        Assert.Equal(Library.BrowserLibraryDocumentOutcome.Available, inspection.Outcome);
        Assert.Equal("System.Net.Sockets", inspection.Assembly?.Name);
        Library.BrowserLibraryEnablements enablements = Assert.IsType<Library.BrowserLibraryEnablements>(
            inspection.Enablements);
        Assert.Equal(Library.BrowserLibraryEnablementsOutcome.Available, enablements.Outcome);
        Assert.Equal(Library.BrowserLibraryEnablementsRole.ImplementationAssembly, enablements.Role);
        Assert.Equal(
            [
                (Library.BrowserLibraryEnablementId.AotCompatible, Library.BrowserLibraryEnablementKind.Enabled, "AOT"),
                (Library.BrowserLibraryEnablementId.RuntimeAsync, Library.BrowserLibraryEnablementKind.Enabled, "Runtime Async"),
                (Library.BrowserLibraryEnablementId.MemorySafetyV2, Library.BrowserLibraryEnablementKind.NotEnabled, "Memory Safety v2"),
            ],
            enablements.Items.Select(item => (item.Id, item.Kind, item.Label)));
        Assert.All(enablements.Items, item => Assert.Null(item.Reason));
    }

    [Fact]
    public async Task LibraryDocument_ReferenceOnlyPackage_ReportsEveryEnablementUnavailable()
    {
        (string packageId, string assemblyId) = await RegisterSocketsPackageAsync(
            includeImplementation: false);

        Library.BrowserLibraryDocumentInspection inspection =
            await InspectPackageLibraryAsync(packageId, assemblyId, enablements: true);

        Library.BrowserLibraryEnablements enablements = Assert.IsType<Library.BrowserLibraryEnablements>(
            inspection.Enablements);
        Assert.Equal(Library.BrowserLibraryEnablementsRole.ApiAssembly, enablements.Role);
        Assert.All(
            enablements.Items,
            item =>
            {
                Assert.Equal(Library.BrowserLibraryEnablementKind.Unavailable, item.Kind);
                Assert.Equal(Library.BrowserLibraryEnablementUnavailableReason.ReferenceAssembly, item.Reason);
            });
    }

    [Fact]
    public async Task LibraryDocument_UnrequestedEnablements_AreAbsent()
    {
        (string packageId, string assemblyId) = await RegisterSocketsPackageAsync(
            includeImplementation: true);

        Library.BrowserLibraryDocumentInspection inspection =
            await InspectPackageLibraryAsync(packageId, assemblyId, enablements: false);

        Assert.Equal(Library.BrowserLibraryDocumentOutcome.Available, inspection.Outcome);
        Assert.Null(inspection.Enablements);
    }

    [Fact]
    public async Task LibraryDocument_PlatformLibrary_JudgesTheRuntimePackAssembly()
    {
        const string version = "11.0.901";
        byte[] runtimeNupkg = PlatformPackage(
            EnablementsFramework,
            ("System.Net.Sockets.dll", EnablementAsset("runtime")));
        using var client = new HttpClient(new MultiplePlatformVersionHandler(
            version,
            new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase)
            {
                ["microsoft.netcore.app.runtime.linux-x64"] = runtimeNupkg,
                ["microsoft.aspnetcore.app.runtime.linux-x64"] = PlatformPackage(
                    EnablementsFramework,
                    ("DotnetInspect.Web.Tests.dll",
                        File.ReadAllBytes(typeof(BrowserEngineBoundaryTests).Assembly.Location))),
            }));
        await using BrowserPlatformScopeResolution resolution =
            await BrowserPlatformWorkspace.OpenAssemblyAsync(
                // A test-unique framework key keeps this Platform scope from
                // being reused by neighboring Platform workspace tests.
                "net11.0-library-enablements",
                "System.Net.Sockets.dll",
                "",
                client,
                new UniformPackageSourceAuthorization([PackageSource.NuGetOrg]),
                TimeSpan.FromSeconds(5),
                TestContext.Current.CancellationToken);

        Library.BrowserLibraryDocumentInspection inspection =
            await Library.BrowserLibraryDocumentOperation.InspectPlatformAsync(
                resolution,
                Library.BrowserLibraryDocumentOperation.Plan(new(Enablements: true)),
                TestContext.Current.CancellationToken);

        Library.BrowserLibraryEnablements enablements = Assert.IsType<Library.BrowserLibraryEnablements>(
            inspection.Enablements);
        Assert.Equal(Library.BrowserLibraryEnablementsRole.ImplementationAssembly, enablements.Role);
        Assert.Equal(
            [Library.BrowserLibraryEnablementId.AotCompatible, Library.BrowserLibraryEnablementId.RuntimeAsync],
            enablements.Items
                .Where(item => item.Kind == Library.BrowserLibraryEnablementKind.Enabled)
                .Select(item => item.Id));
    }

    [Fact]
    public async Task LibraryDocument_SelectorMustCarryItsDeclaredCase()
    {
        string requestJson = JsonSerializer.Serialize(
            new Library.BrowserLibraryInspectionRequest(
                new(Library.BrowserLibrarySelectorKind.Package, Package: null, Platform: null),
                new(Enablements: true)),
            Library.BrowserLibraryJsonContext.Default.BrowserLibraryInspectionRequest);

        await Assert.ThrowsAsync<ArgumentException>(
            () => Library.LibraryExports.InspectLibrary(requestJson));
    }

    [Fact]
    public void LibraryDocument_WireIdentifiersMatchTheEnablementsOwner()
    {
        foreach (LibraryEnablementId id in Enum.GetValues<LibraryEnablementId>())
        {
            foreach (LibraryEnablementUnavailableReason reason in
                Enum.GetValues<LibraryEnablementUnavailableReason>())
            {
                var domain = new LibraryEnablement.Unavailable(id, reason);
                using JsonDocument domainJson = JsonDocument.Parse(JsonSerializer.Serialize(
                    domain,
                    LibraryInspectionJsonContext.Default.LibraryEnablementUnavailable));
                using JsonDocument wireJson = JsonDocument.Parse(JsonSerializer.Serialize(
                    Library.BrowserLibraryDocumentProjection
                        .Project(new LibraryEnablementsOutcome.Available(
                            LibraryEnablementsRole.ApiAssembly,
                            new LibraryEnablementFacts([domain])))
                        .Items[0],
                    Library.BrowserLibraryJsonContext.Default.BrowserLibraryEnablement));

                Assert.Equal(
                    domainJson.RootElement.GetProperty("id").GetString(),
                    wireJson.RootElement.GetProperty("id").GetString());
                Assert.Equal(
                    domainJson.RootElement.GetProperty("reason").GetString(),
                    wireJson.RootElement.GetProperty("reason").GetString());
                Assert.Equal("unavailable", wireJson.RootElement.GetProperty("kind").GetString());
            }
        }
    }

    static async Task<Library.BrowserLibraryDocumentInspection> InspectPackageLibraryAsync(
        string packageId,
        string assemblyId,
        bool enablements)
    {
        string json = await Library.LibraryExports.InspectLibrary(
            JsonSerializer.Serialize(
                new Library.BrowserLibraryInspectionRequest(
                    new(
                        Library.BrowserLibrarySelectorKind.Package,
                        new(packageId, "1.0.0", EnablementsFramework, assemblyId),
                        Platform: null),
                    new(enablements)),
                Library.BrowserLibraryJsonContext.Default.BrowserLibraryInspectionRequest));
        return JsonSerializer.Deserialize(
            json,
            Library.BrowserLibraryJsonContext.Default.BrowserLibraryDocumentInspection)!;
    }

    static async Task<(string PackageId, string AssemblyId)> RegisterSocketsPackageAsync(
        bool includeImplementation)
    {
        string id = "Browser.Library.Enablements." + Guid.NewGuid().ToString("N");
        var entries = new List<(string, byte[])>
        {
            ($"ref/{EnablementsFramework}/System.Net.Sockets.dll", EnablementAsset("ref")),
        };
        if (includeImplementation)
        {
            entries.Add(
                ($"lib/{EnablementsFramework}/System.Net.Sockets.dll", EnablementAsset("runtime")));
        }

        var package = new BrowserPackage(id, "1.0.0", EnablementsArchive(entries), fromCache: false);
        await BrowserPackageWorkspace.RegisterAcquiredPackageAsync(package);
        PackageCompileAssetSelection selection =
            PackageCompileAssetSelector.Select(package.Content, id, EnablementsFramework);
        Assert.True(selection.IsSelected);
        return (id, Assert.Single(selection.Assets).Id);
    }

    static byte[] EnablementAsset(string pack) =>
        File.ReadAllBytes(
            Path.Combine(
                AppContext.BaseDirectory,
                "RealAssets",
                "Enablements",
                pack,
                "System.Net.Sockets.dll"));

    static byte[] EnablementsArchive(IEnumerable<(string EntryPath, byte[] Content)> entries)
    {
        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach ((string path, byte[] content) in entries)
            {
                using Stream entry = archive.CreateEntry(path).Open();
                entry.Write(content);
            }
        }
        return buffer.ToArray();
    }
}
