using System.Net;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Text.Json;
using DotnetInspector.Packages;
using NuGetFetch;

namespace InspectWeb.Engine.Tests;

public sealed partial class BrowserEngineBoundaryTests
{
    static readonly string[] CatalogPackages =
    [
        "microsoft.netcore.app.ref",
        "microsoft.netcore.app.runtime.linux-x64",
        "microsoft.aspnetcore.app.ref",
        "microsoft.aspnetcore.app.runtime.linux-x64",
    ];

    [Fact]
    public async Task PlatformCatalog_VersionsIntersectAllFourPackagesInSemanticOrder()
    {
        string[] versions =
        [
            "10.0.999", "11.0.0-preview.7.26381.103", "11.0.0-preview.10",
            "11.0.0-rc.1", "11.0.0", "11.0.2", "11.0.10", "11.1.0", "12.0.0",
        ];
        var handler = new PlatformCatalogHandler("11.0.401")
        {
            Versions = CatalogPackages.ToDictionary(package => package,
                package => package == CatalogPackages[^1]
                    ? versions.Where(version => version != "11.0.10").ToArray()
                    : versions),
        };
        using IPackageSourceClient source = CatalogSource(handler);

        string[] result = await BrowserPlatformCatalog.GetVersionsAsync(
            "net11.0", source, TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        Assert.Equal(
            ["11.0.2", "11.0.0", "11.0.0-rc.1", "11.0.0-preview.10", "11.0.0-preview.7.26381.103"],
            result);
        Assert.Equal(8, handler.Requests.Count);
        Assert.All(handler.Requests, request => Assert.Equal("globalcdn.nuget.org", request.Host));
        Assert.DoesNotContain(handler.Requests, request => request.AbsolutePath.EndsWith(".nupkg"));
    }

    [Fact]
    public async Task PlatformCatalog_NoCommonVersionIsVisible()
    {
        var handler = new PlatformCatalogHandler("11.0.402")
        {
            Versions = CatalogPackages.ToDictionary(package => package,
                package => new[] { package == CatalogPackages[^1] ? "11.0.1" : "11.0.0" }),
        };
        using IPackageSourceClient source = CatalogSource(handler);
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            BrowserPlatformCatalog.GetVersionsAsync("net11.0", source,
                TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
        Assert.Contains("No common", failure.Message);
        Assert.Equal(8, handler.Requests.Count);
    }

    [Fact]
    public async Task PlatformCatalog_ExactInventoryKeepsMembershipAndRuntimeClassificationSeparate()
    {
        const string version = "11.0.403-preview.7";
        var handler = new PlatformCatalogHandler(version)
        {
            Packages = new()
            {
                [CatalogPackages[0]] = PackageEntries(
                    ("ref/net11.0/ContractName.dll", CatalogImage("Mixed", publicTypes: 3)),
                    ("ref/net11.0/Forwarded.dll", CatalogImage("Forwarded", publicTypes: 2)),
                    ("ref/net11.0/ReferenceOnly.dll", CatalogImage("ReferenceOnly", publicTypes: 1))),
                [CatalogPackages[1]] = PackageEntries(
                    ("runtimes/linux-x64/lib/net11.0/PhysicalName.dll",
                        CatalogImage("Mixed", publicTypes: 1, forwardedTypes: 1)),
                    ("runtimes/linux-x64/lib/net11.0/Forwarded.dll",
                        CatalogImage("Forwarded", forwardedTypes: 2)),
                    ("runtimes/linux-x64/lib/net11.0/System.Private.Helper.dll",
                        CatalogImage("System.Private.Helper", publicTypes: 1))),
                [CatalogPackages[2]] = PackageEntries(
                    ("ref/net11.0/Web.dll", CatalogImage("Web", publicTypes: 1))),
                [CatalogPackages[3]] = PackageEntries(
                    ("runtimes/linux-x64/lib/net11.0/Web.dll", CatalogImage("Web", publicTypes: 1))),
            },
        };
        using IPackageSourceClient source = CatalogSource(handler);
        int scopesBefore = BrowserPackageWorkspace.Stats().Workspaces;

        BrowserPlatformCatalogResult result = await BrowserPlatformCatalog.GetCatalogAsync(
            "net11.0", version, source, PackageSourceIdentity.NuGetOrg,
            TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        Assert.Equal("net11.0", result.Tfm);
        Assert.Equal(version, result.Version);
        Assert.Equal(5, result.Rows.Length);
        Assert.All(result.Rows, row => Assert.Equal(version, row.PackVersion));
        BrowserPlatformCatalogRow mixed = Assert.Single(result.Rows, row => row.Assembly == "Mixed");
        Assert.Equal("PhysicalName.dll", mixed.File);
        Assert.Equal("impl", mixed.Kind);
        Assert.Equal(3, mixed.PublicTypes);
        Assert.True(mixed.InReferencePack);
        Assert.True(mixed.HasImplementation);
        Assert.Null(mixed.ForwardsTo);
        Assert.Equal("11.0.0.0", mixed.Version);
        BrowserPlatformCatalogRow facade = Assert.Single(result.Rows, row => row.Assembly == "Forwarded");
        Assert.Equal("facade", facade.Kind);
        Assert.Equal("ForwardTarget", facade.ForwardsTo);
        Assert.Equal(2, facade.PublicTypes);
        BrowserPlatformCatalogRow runtimeOnly = Assert.Single(result.Rows,
            row => row.Assembly == "System.Private.Helper");
        Assert.Equal("impl", runtimeOnly.Kind);
        Assert.False(runtimeOnly.InReferencePack);
        Assert.True(runtimeOnly.HasImplementation);
        BrowserPlatformCatalogRow referenceOnly = Assert.Single(result.Rows,
            row => row.Assembly == "ReferenceOnly");
        Assert.Equal("ref", referenceOnly.Kind);
        Assert.True(referenceOnly.InReferencePack);
        Assert.False(referenceOnly.HasImplementation);
        Assert.Equal(4, handler.Requests.Count);
        Assert.All(handler.Requests, request => Assert.EndsWith($".{version}.nupkg", request.AbsolutePath));
        Assert.True(BrowserPackageWorkspace.Stats().Workspaces <= scopesBefore);
        using JsonDocument wire = JsonDocument.Parse(JsonSerializer.Serialize(
            PackageExports.ProjectPlatformCatalog(result),
            InspectWeb.Engine.PackageFacade.BrowserPackageJsonContext.Default.BrowserPlatformCatalog));
        Assert.Equal(["tfm", "version", "rows"],
            wire.RootElement.EnumerateObject().Select(property => property.Name));
        JsonElement firstRow = wire.RootElement.GetProperty("rows")[0];
        Assert.Equal(
            ["tfm", "pack", "assembly", "file", "kind", "forwardsTo", "version",
                "publicTypes", "inReferencePack", "hasImplementation", "packVersion"],
            firstRow.EnumerateObject().Select(property => property.Name));
        Assert.Equal(version, firstRow.GetProperty("packVersion").GetString());
    }

    [Fact]
    public async Task PlatformCatalog_WarmupOnlyAcquiresArchivesAndDetailReusesThem()
    {
        const string version = "11.0.404";
        var handler = new PlatformCatalogHandler(version)
        {
            Packages = new()
            {
                [CatalogPackages[1]] = PackageEntries(
                    ("runtimes/linux-x64/lib/net11.0/Warmed.dll", CatalogImage("Warmed", publicTypes: 1))),
                [CatalogPackages[3]] = PackageEntries(
                    ("runtimes/linux-x64/lib/net11.0/Web.dll", CatalogImage("Web", publicTypes: 1))),
            },
        };
        using IPackageSourceClient source = CatalogSource(handler);
        int scopesBefore = BrowserPackageWorkspace.Stats().Workspaces;

        await BrowserPlatformCatalog.PrefetchAsync("net11.0", version,
            source, PackageSourceIdentity.NuGetOrg, TimeSpan.FromSeconds(5),
            TestContext.Current.CancellationToken);

        Assert.Equal(scopesBefore, BrowserPackageWorkspace.Stats().Workspaces);
        Assert.Equal(2, handler.Requests.Count);
        await BrowserPlatformCatalog.PrefetchAsync("net11.0", version,
            source, PackageSourceIdentity.NuGetOrg, TimeSpan.FromSeconds(5),
            TestContext.Current.CancellationToken);
        Assert.Equal(2, handler.Requests.Count);

        using var client = new HttpClient(new CatalogNoNetworkHandler());
        await using BrowserPlatformScopeResolution detail =
            await BrowserPlatformWorkspace.OpenAssemblyAsync(
                "net11.0", version, "Warmed.dll", "netcore.app", client,
                new UniformPackageSourceAuthorization([PackageSource.NuGetOrg]),
                TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.Equal(version, detail.Coordinate.Version);
        Assert.Equal("Warmed", detail.Participant.Participant.Assembly.Identity.Name);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task PlatformCatalog_RidWideSelectionIncludesNativeCoreLibAndSharesDetailSelection()
    {
        const string version = "11.0.411";
        byte[] native = CatalogImage("Native");
        using (var pe = new PEReader(new MemoryStream(native, writable: false)))
        {
            int directoriesOffset = pe.PEHeaders.PEHeader!.Magic == PEMagic.PE32 ? 96 : 112;
            Array.Clear(native, pe.PEHeaders.PEHeaderStartOffset + directoriesOffset + 14 * 8, 8);
        }
        byte[] runtime = PackageEntries(
            ("runtimes/linux-x64/native/System.Private.CoreLib.dll",
                CatalogImage("System.Private.CoreLib", publicTypes: 1)),
            ("runtimes/linux-x64/components/ManagedAddon.dll",
                CatalogImage("ManagedAddon", publicTypes: 1)),
            ("runtimes/linux-x64/native/Native.dll", native),
            ("runtimes/win-x64/native/Foreign.dll", CatalogImage("Foreign", publicTypes: 1)));
        var handler = new PlatformCatalogHandler(version)
        {
            Packages = new()
            {
                [CatalogPackages[0]] = PackageEntries(
                    ("ref/net11.0/ReferenceApi.dll", CatalogImage("ReferenceApi", publicTypes: 1))),
                [CatalogPackages[1]] = runtime,
                [CatalogPackages[2]] = PackageEntries(
                    ("ref/net11.0/Web.dll", CatalogImage("Web", publicTypes: 1))),
                [CatalogPackages[3]] = PackageEntries(
                    ("runtimes/linux-x64/lib/net11.0/Web.dll", CatalogImage("Web", publicTypes: 1))),
            },
        };
        using IPackageSourceClient source = CatalogSource(handler);
        BrowserPlatformCatalogResult result = await BrowserPlatformCatalog.GetCatalogAsync(
            "net11.0", version, source, PackageSourceIdentity.NuGetOrg,
            TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        Assert.Equal(4, result.Rows.Length);
        BrowserPlatformCatalogRow coreLib = Assert.Single(result.Rows,
            row => row.Assembly == "System.Private.CoreLib");
        Assert.True(coreLib.HasImplementation);
        Assert.False(coreLib.InReferencePack);
        Assert.Equal("System.Private.CoreLib.dll", coreLib.File);
        Assert.Contains(result.Rows, row => row.Assembly == "ManagedAddon");
        Assert.DoesNotContain(result.Rows, row => row.Assembly is "Native" or "Foreign");
        Assert.IsType<PackageAssetSelection.NoMatch>(
            PackageAssetSelector.Select(
                new BrowserPackage(CatalogPackages[1], version, runtime, fromCache: false).Content,
                "net11.0", "linux-x64"));

        using var client = new HttpClient(new CatalogNoNetworkHandler());
        await using BrowserPlatformScopeResolution detail =
            await BrowserPlatformWorkspace.OpenRuntimeAsync(
                "net11.0", version, client,
                new UniformPackageSourceAuthorization([PackageSource.NuGetOrg]),
                TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.Equal("System.Private.CoreLib", detail.Participant.Participant.Assembly.Identity.Name);
        Assert.Equal(version, detail.Coordinate.Version);
        Assert.Equal(4, handler.Requests.Count);
    }

    [Fact]
    public async Task PlatformCatalog_ConcurrentWarmupsShareDownloads()
    {
        const string version = "11.0.405";
        var handler = new PlatformCatalogHandler(version)
        {
            BeforeResponse = async token => await Task.Delay(25, token),
            Packages = new()
            {
                [CatalogPackages[1]] = PackageEntries(("content/inert.txt", [1, 2, 3])),
                [CatalogPackages[3]] = PackageEntries(("content/inert.txt", [4, 5, 6])),
            },
        };
        using IPackageSourceClient source = CatalogSource(handler);
        await Task.WhenAll(
            BrowserPlatformCatalog.PrefetchAsync("net11.0", version,
                source, PackageSourceIdentity.NuGetOrg, TimeSpan.FromSeconds(5),
                TestContext.Current.CancellationToken),
            BrowserPlatformCatalog.PrefetchAsync("net11.0", version,
                source, PackageSourceIdentity.NuGetOrg, TimeSpan.FromSeconds(5),
                TestContext.Current.CancellationToken));
        Assert.Equal(2, handler.Requests.Count);
    }

    [Theory]
    [InlineData("net10.0", "11.0.0")]
    [InlineData("net11.0", "latest")]
    [InlineData("netstandard2.1", "2.1.0")]
    public async Task PlatformCatalog_InvalidTargetIsRejectedBeforeNetwork(string tfm, string version)
    {
        var handler = new PlatformCatalogHandler(version);
        using IPackageSourceClient source = CatalogSource(handler);
        await Assert.ThrowsAsync<ArgumentException>(() =>
            BrowserPlatformCatalog.GetCatalogAsync(tfm, version, source,
                PackageSourceIdentity.NuGetOrg, TimeSpan.FromSeconds(5),
                TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            BrowserPlatformCatalog.PrefetchAsync(tfm, version, source,
                PackageSourceIdentity.NuGetOrg, TimeSpan.FromSeconds(5),
                TestContext.Current.CancellationToken));
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task PlatformCatalog_DiscoveryTimeoutAndCancellationStayVisible()
    {
        var handler = new PlatformCatalogHandler("11.0.406")
        {
            BeforeResponse = token => Task.Delay(Timeout.InfiniteTimeSpan, token),
        };
        using IPackageSourceClient source = CatalogSource(handler);
        await Assert.ThrowsAsync<TimeoutException>(() =>
            BrowserPlatformCatalog.GetVersionsAsync("net11.0", source,
                TimeSpan.FromMilliseconds(100), TestContext.Current.CancellationToken));
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            BrowserPlatformCatalog.GetVersionsAsync("net11.0", source,
                TimeSpan.FromSeconds(5), cancellation.Token));
    }

    [Fact]
    public async Task PlatformCatalog_AcquisitionFailureAndInvalidMetadataStayVisible()
    {
        var missing = new PlatformCatalogHandler("11.0.407");
        using IPackageSourceClient missingSource = CatalogSource(missing);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            BrowserPlatformCatalog.PrefetchAsync("net11.0", "11.0.407",
                missingSource, PackageSourceIdentity.NuGetOrg, TimeSpan.FromSeconds(5),
                TestContext.Current.CancellationToken));
        var corrupt = new PlatformCatalogHandler("11.0.408")
        {
            Packages = new()
            {
                [CatalogPackages[0]] = PackageEntries(("ref/net11.0/Corrupt.dll", [1, 2, 3])),
            },
        };
        using IPackageSourceClient corruptSource = CatalogSource(corrupt);
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            BrowserPlatformCatalog.GetCatalogAsync("net11.0", "11.0.408",
                corruptSource, PackageSourceIdentity.NuGetOrg, TimeSpan.FromSeconds(5),
                TestContext.Current.CancellationToken));
        Assert.Contains("Corrupt.dll", failure.Message);
    }

    [Fact]
    public async Task PlatformCatalog_WarmupDeadlineIsVisibleWithoutOpeningScopes()
    {
        var handler = new PlatformCatalogHandler("11.0.409")
        {
            BeforeResponse = token => Task.Delay(Timeout.InfiniteTimeSpan, token),
        };
        using IPackageSourceClient source = CatalogSource(handler);
        int scopesBefore = BrowserPackageWorkspace.Stats().Workspaces;
        await Assert.ThrowsAsync<TimeoutException>(() =>
            BrowserPlatformCatalog.PrefetchAsync("net11.0", "11.0.409",
                source, PackageSourceIdentity.NuGetOrg, TimeSpan.FromMilliseconds(100),
                TestContext.Current.CancellationToken));
        Assert.Equal(scopesBefore, BrowserPackageWorkspace.Stats().Workspaces);
    }

    [Fact]
    public async Task PlatformCatalog_WarmupCancellationDoesNotCancelAnotherArchiveWaiter()
    {
        const string version = "11.0.410";
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = new PlatformCatalogHandler(version)
        {
            BeforeResponse = async token =>
            {
                entered.TrySetResult();
                await release.Task.WaitAsync(token);
            },
            Packages = new()
            {
                [CatalogPackages[1]] = PackageEntries(("content/inert.txt", [1])),
                [CatalogPackages[3]] = PackageEntries(("content/inert.txt", [2])),
            },
        };
        using IPackageSourceClient source = CatalogSource(handler);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken);
        Task cancelled = BrowserPlatformCatalog.PrefetchAsync("net11.0", version,
            source, PackageSourceIdentity.NuGetOrg, TimeSpan.FromSeconds(5), cancellation.Token);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Task continuing = BrowserPlatformCatalog.PrefetchAsync("net11.0", version,
            source, PackageSourceIdentity.NuGetOrg, TimeSpan.FromSeconds(5),
            TestContext.Current.CancellationToken);
        try
        {
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelled);
            Assert.False(continuing.IsCompleted);
        }
        finally
        {
            release.TrySetResult();
            await continuing;
        }
        Assert.Equal(2, handler.Requests.Count);
    }

    static IPackageSourceClient CatalogSource(HttpMessageHandler handler) =>
        PackageSourceClientFactory.CreateGallery(
            PackageSourceAssociation.Create(), handler,
            new NuGetFetchOptions
            {
                RequestTimeout = TimeSpan.FromSeconds(5),
                OperationTimeout = TimeSpan.FromSeconds(5),
            });

    static byte[] CatalogImage(string name, int publicTypes = 0, int forwardedTypes = 0)
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(0, metadata.GetOrAddString($"{name}.dll"),
            metadata.GetOrAddGuid(Guid.NewGuid()), default, default);
        metadata.AddAssembly(metadata.GetOrAddString(name), new Version(11, 0, 0, 0),
            default, default, default, default);
        metadata.AddTypeDefinition(default, default, metadata.GetOrAddString("<Module>"),
            default, MetadataTokens.FieldDefinitionHandle(1), MetadataTokens.MethodDefinitionHandle(1));
        for (int index = 0; index < publicTypes; index++)
            metadata.AddTypeDefinition(TypeAttributes.Public | TypeAttributes.Abstract,
                metadata.GetOrAddString("Catalog"), metadata.GetOrAddString($"T{index}"),
                default, MetadataTokens.FieldDefinitionHandle(1), MetadataTokens.MethodDefinitionHandle(1));
        if (forwardedTypes > 0)
        {
            AssemblyReferenceHandle target = metadata.AddAssemblyReference(
                metadata.GetOrAddString("ForwardTarget"), new Version(11, 0, 0, 0),
                default, default, default, default);
            for (int index = 0; index < forwardedTypes; index++)
                metadata.AddExportedType((TypeAttributes)0x00200000,
                    metadata.GetOrAddString("Forwarded"), metadata.GetOrAddString($"T{index}"), target, 0);
        }
        var pe = new ManagedPEBuilder(PEHeaderBuilder.CreateLibraryHeader(),
            new MetadataRootBuilder(metadata), new BlobBuilder(), flags: CorFlags.ILOnly);
        var image = new BlobBuilder();
        pe.Serialize(image);
        return image.ToArray();
    }

    sealed class PlatformCatalogHandler(string version) : HttpMessageHandler
    {
        internal Dictionary<string, string[]> Versions { get; init; } = [];
        internal Dictionary<string, byte[]> Packages { get; init; } = [];
        internal List<Uri> Requests { get; } = [];
        internal Func<CancellationToken, Task>? BeforeResponse { get; init; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Uri uri = request.RequestUri!;
            Requests.Add(uri);
            if (BeforeResponse is not null)
                await BeforeResponse(cancellationToken);
            foreach (string package in CatalogPackages)
            {
                if (Versions.TryGetValue(package, out string[]? versions))
                {
                    if (uri.AbsolutePath == $"/v3-flatcontainer/{package}/index.json")
                        return Response("""{"versions":""" + JsonSerializer.Serialize(versions) + "}");
                    if (uri.AbsolutePath == $"/v3/registration5-gz-semver2/{package}/index.json")
                        return Response("""{"items":[{"items":[""" +
                            string.Join(",", versions.Select(candidate =>
                                """{"catalogEntry":{"version":""" +
                                JsonSerializer.Serialize(candidate) + ""","listed":true}}""")) + "]}]}");
                }
                if (uri.AbsolutePath == $"/packages/{package}.{version}.nupkg"
                    && Packages.TryGetValue(package, out byte[]? archive))
                    return new(HttpStatusCode.OK) { Content = new ByteArrayContent(archive) };
            }
            return new(HttpStatusCode.NotFound);
        }

        static HttpResponseMessage Response(string json) =>
            new(HttpStatusCode.OK) { Content = new StringContent(json) };
    }

    sealed class CatalogNoNetworkHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new InvalidOperationException($"Detail did not reuse the warmed archive: {request.RequestUri}");
    }
}
