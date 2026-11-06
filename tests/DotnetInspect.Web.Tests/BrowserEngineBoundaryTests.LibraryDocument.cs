using System.IO.Compression;
using System.Runtime.Versioning;
using System.Text.Json;

using DotnetInspector.LibraryMetadata;
using DotnetInspector.Packages;
using DotnetInspector.Presentation;
using DotnetInspector.Queries;
using DotnetInspector.Sections;
using NuGetFetch;
using AssemblyResolutionProvenance = ILInspector.Metadata.AssemblyResolutionProvenance;
using NoResolverAssemblyBindingPolicy = ILInspector.Metadata.NoResolverAssemblyBindingPolicy;
using ResolvedAssemblyReference = ILInspector.Metadata.ResolvedAssemblyReference;
using Library = DotnetInspect.Web.Interop.Library;

namespace DotnetInspect.Web.Tests;

/// <summary>
/// Gates for the Browser Library document request (#8228 step 6) and its
/// Enablements group, over pinned Microsoft.NETCore.App 11.0.0-rc.3 assets.
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
        Assert.Null(inspection.TypeNames);
        Assert.NotNull(inspection.Share);
    }

    [Fact]
    public async Task LibraryDocument_TypeNames_MatchTheSharedCliHierarchyWithoutImplementationMaterialization()
    {
        string asset = Path.Combine(
            AppContext.BaseDirectory, "RealAssets", "LibraryNames", "System.Text.Json.dll");
        string packageId = "Browser.Library.Names." + Guid.NewGuid().ToString("N");
        var package = new BrowserPackage(
            packageId,
            "1.0.0",
            EnablementsArchive(
            [
                ($"ref/{EnablementsFramework}/System.Text.Json.dll", File.ReadAllBytes(asset)),
                // Name browsing must not try to open an unrequested implementation.
                ($"lib/{EnablementsFramework}/System.Text.Json.dll", []),
            ]),
            fromCache: false);
        await BrowserPackageWorkspace.RegisterAcquiredPackageAsync(package);
        string assemblyId = Assert.Single(PackageCompileAssetSelector.Select(
            package.Content, packageId, EnablementsFramework).Assets).Id;
        var request = new Library.BrowserLibraryInspectionRequest(
            new(
                Library.BrowserLibrarySelectorKind.Package,
                new(packageId, "1.0.0", EnablementsFramework, assemblyId),
                Platform: null),
            new(Enablements: false, TypeNames: true));

        Library.BrowserLibraryDocumentInspection inspection =
            await Library.BrowserLibraryDocumentOperation.ExecuteAsync(
                request, TestContext.Current.CancellationToken);

        Library.BrowserLibraryTypeNames names =
            Assert.IsType<Library.BrowserLibraryTypeNames>(inspection.TypeNames);
        Assert.Equal("Available", names.Outcome);
        Assert.Null(names.Detail);
        Assert.Null(inspection.Enablements);
        Assert.Equal(Library.BrowserLibraryInspectionShareKind.NonProjectable, inspection.Share?.Kind);
        Assert.Empty(inspection.Diagnostics);
        Library.BrowserLibraryTypeName[] rows =
            [.. names.Namespaces.SelectMany(ns => ns.Types)];
        Assert.Contains(rows, row =>
            row.DefinitionId == "System.Text.Json.JsonElement"
            && row.Name == "JsonElement"
            && row.DefinitionKind == "Struct");
        Assert.Contains(rows, row => row.Name == "JsonElement.ArrayEnumerator");
        Assert.Contains(rows, row =>
            row.DeclarationKind == "Forwarder"
            && row.DefinitionId == "System.Runtime.CompilerServices.IsExternalInit"
            && row.DefinitionKind is null);

        LibraryTypeHierarchyPresentationPlan presentation =
            LibraryTypeHierarchyPresentation.CreateDefaultPlan(
                LibraryTypeHierarchyPresentationFormat.Tree);
        Assert.Null(presentation.Types.Rows!.MemberCount);
        AssemblyContextLibraryInspectionRun<InspectionEnvelope<LibraryInspectionOutcome>> run =
            await AssemblyContextLibraryInspection.ExecuteAsync(
                ResolvedAssemblyReference.CreateFromPath(
                    asset, AssemblyResolutionProvenance.Local("Library Names parity")),
                NoResolverAssemblyBindingPolicy.Instance,
                AssemblyContextLibraryRole.ApiOnly,
                new(64 * 1024 * 1024, 64 * 1024 * 1024),
                (reference, owner) =>
                    AssemblyContextLibraryInspection.ExecuteOperation(
                        reference,
                        owner,
                        lease => LibraryInspectionOperation.Execute(
                            new(reference, new(presentation.Types, BrowserApiSurfacePolicy.LibraryBounds)),
                            lease,
                            TestContext.Current.CancellationToken)),
                TestContext.Current.CancellationToken);
        LibraryDocument document = Assert.IsType<LibraryInspectionOutcome.Available>(
            run.Result?.Content).Document;
        LibraryTypePopulationRowsOutcome.Read source =
            Assert.IsType<LibraryTypePopulationRowsOutcome.Read>(document.Types!.Rows);
        Assert.All(source.Items, row => Assert.Null(row.MemberCount));
        Assert.Equal(
            source.Items.Select(row => row.Identity.ToEscapedFullName()).Order(),
            rows.Select(row => row.DefinitionId).Order());
        using var output = new StringWriter();
        LibraryTypeHierarchyPresentation.Write(
            document, presentation, output, new(InertText.TextPolicy.Field, "System.Text.Json.dll"));
        Assert.Contains("struct JsonElement", output.ToString(), StringComparison.Ordinal);
        Assert.Contains("IsExternalInit (forwarded)", output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task LibraryDocument_TypeNames_ReportIncompleteInsteadOfEmpty()
    {
        string asset = Path.Combine(
            AppContext.BaseDirectory, "RealAssets", "LibraryNames", "System.Text.Json.dll");
        AssemblyContextLibraryInspectionRun<InspectionEnvelope<LibraryInspectionOutcome>> run =
            await AssemblyContextLibraryInspection.ExecuteAsync(
                ResolvedAssemblyReference.CreateFromPath(
                    asset, AssemblyResolutionProvenance.Local("bounded Library Names")),
                NoResolverAssemblyBindingPolicy.Instance,
                AssemblyContextLibraryRole.ApiOnly,
                new(64 * 1024 * 1024, 64 * 1024 * 1024),
                (reference, owner) =>
                    AssemblyContextLibraryInspection.ExecuteOperation(
                        reference,
                        owner,
                        lease => LibraryInspectionOperation.Execute(
                            new(
                                reference,
                                new(
                                    Library.BrowserLibraryDocumentOperation.NamePresentation().Types,
                                    new(1, 100, 100, 100, 1_000_000, 20_000_000))),
                            lease,
                            TestContext.Current.CancellationToken)),
                TestContext.Current.CancellationToken);

        Library.BrowserLibraryDocumentInspection inspection =
            Library.BrowserLibraryDocumentProjection.Project(run);
        Assert.Equal("Unavailable", inspection.TypeNames?.Outcome);
        Assert.Contains("bound", inspection.TypeNames?.Detail, StringComparison.Ordinal);
        Assert.Empty(inspection.TypeNames!.Namespaces);
        Assert.NotNull(inspection.Share);
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
                Library.BrowserLibraryDocumentOperation.Plan(new(Enablements: true, TypeNames: true)),
                TestContext.Current.CancellationToken);

        Library.BrowserLibraryEnablements enablements = Assert.IsType<Library.BrowserLibraryEnablements>(
            inspection.Enablements);
        Assert.Equal(Library.BrowserLibraryEnablementsRole.ImplementationAssembly, enablements.Role);
        Assert.Equal(
            [Library.BrowserLibraryEnablementId.AotCompatible, Library.BrowserLibraryEnablementId.RuntimeAsync],
            enablements.Items
                .Where(item => item.Kind == Library.BrowserLibraryEnablementKind.Enabled)
                .Select(item => item.Id));
        Assert.Equal("Available", inspection.TypeNames?.Outcome);
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
                Assert.Equal(
                    LibraryEnablementFacts.Label(id),
                    wireJson.RootElement.GetProperty("label").GetString());
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
