using System.Collections.Concurrent;
using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text;
using DotnetInspector.Fixtures;
using DotnetInspector.Packages;
using ILInspector.Analysis;
using ILInspector.Metadata;
using NuGetFetch;

namespace DotnetInspect.Web.Tests;

public sealed partial class BrowserEngineBoundaryTests
{
    static readonly ExactPlatformMemoryPersistence
        ExactPlatformPersistence = new();
    static int _exactPlatformPersistenceConfigured;

    [Fact]
    public async Task PlatformWorkspace_ExactAssemblyUsesRangeAndReusesEntryCache()
    {
        const string version = "11.0.7381";
        const string packageId =
            "microsoft.netcore.app.runtime.linux-x64";
        const string assemblyFileName =
            "InspectWeb.MethodBodyFixtures.dll";
        PrepareExactPlatformPersistence();
        byte[] assembly = await File.ReadAllBytesAsync(
            FixtureCatalog.InspectWebMethodBodies.AssemblyPath(),
            TestContext.Current.CancellationToken);
        AssemblyReferenceIdentity identity;
        using (var image = new PEReader(
            new MemoryStream(assembly, writable: false)))
        {
            identity = AssemblyReferenceIdentity.FromAssemblyDefinition(
                image.GetMetadataReader());
        }

        byte[] archive = RuntimePackage(
            assemblyFileName,
            assembly);
        var handler = new ExactPlatformRangeHandler(
            packageId,
            version,
            archive);
        using IPackageSourceClient packageClient =
            BrowserPackageWorkspace.CreateGallerySource(
                handler,
                new NuGetFetchOptions
                {
                    RequestTimeout = TimeSpan.FromSeconds(30),
                    OperationTimeout = TimeSpan.FromSeconds(30),
                });
        using var networkClient =
            new HttpClient(handler, disposeHandler: false);
        IPackageSourceAuthorization sourceAuthorization =
            BrowserPackageWorkspace.SourceAuthorizationFor(packageClient);

        await using BrowserPlatformScopeResolution first =
            await BrowserPlatformWorkspace.OpenAssemblyAsync(
                "net11.0",
                version,
                identity,
                "netcore.app",
                networkClient,
                packageClient,
                sourceAuthorization,
                TimeSpan.FromSeconds(30),
                TestContext.Current.CancellationToken);

        Assert.Equal(identity.Name, first.Participant.Participant.Assembly.Identity.Name);
        Assert.Single(first.Scope.Members);
        Assert.Equal(1, handler.CompleteGetRequests);
        Assert.True(handler.RangeRequests >= 1);
        Assert.True(
            handler.PackageBytesServed < archive.LongLength,
            $"served {handler.PackageBytesServed} of {archive.LongLength} package bytes");

        await first.DisposeAsync();
        await BrowserPackageWorkspace.RemoveScopeAsync(first.Scope);
        int requests = handler.PackageRequests;
        long served = handler.PackageBytesServed;

        await using BrowserPlatformScopeResolution second =
            await BrowserPlatformWorkspace.OpenAssemblyAsync(
                "net11.0",
                version,
                identity,
                "netcore.app",
                networkClient,
                packageClient,
                sourceAuthorization,
                TimeSpan.FromSeconds(30),
                TestContext.Current.CancellationToken);

        Assert.Single(second.Scope.Members);
        Assert.Equal(requests, handler.PackageRequests);
        Assert.Equal(served, handler.PackageBytesServed);
        await second.DisposeAsync();
        await BrowserPackageWorkspace.RemoveScopeAsync(second.Scope);
    }

    [Fact]
    public async Task PlatformCallGraph_UsesExactRangedRuntimeAssemblies()
    {
        const string version = "11.0.0-rc.1.26425.128";
        const string packageId =
            "microsoft.netcore.app.runtime.linux-x64";
        const string assemblyFileName = "System.Text.Json.dll";
        PrepareExactPlatformPersistence();
        byte[] archive = await File.ReadAllBytesAsync(
            Path.Combine(
                AppContext.BaseDirectory,
                "RealAssets",
                "PlatformForwarderActivation",
                "runtime.nupkg"),
            TestContext.Current.CancellationToken);
        byte[] assembly = ReadRuntimeAssembly(
            archive,
            assemblyFileName);

        AssemblyReferenceIdentity identity;
        ApiType type;
        CallGraphMemberBodySelector selector;
        using (var image = new PEReader(
            new MemoryStream(assembly, writable: false)))
        {
            MetadataReader metadata = image.GetMetadataReader();
            identity =
                AssemblyReferenceIdentity.FromAssemblyDefinition(metadata);
            ApiSurface surface = ApiSurfaceExtractor.Extract(image);
            type = Assert.Single(
                surface.Types,
                candidate =>
                    candidate.FullName
                    == "System.Text.Json.Nodes.JsonArray");
            selector = type.Members
                .Where(member => member.Kind == "constructor")
                .SelectMany(member =>
                    CallGraphMemberResolver.CreateBodySelectors(type, member))
                .First();
        }

        var handler = new ExactPlatformRangeHandler(
            packageId,
            version,
            archive);
        using IPackageSourceClient packageClient =
            BrowserPackageWorkspace.CreateGallerySource(
                handler,
                new NuGetFetchOptions
                {
                    RequestTimeout = TimeSpan.FromSeconds(30),
                    OperationTimeout = TimeSpan.FromSeconds(30),
                });
        using var networkClient =
            new HttpClient(handler, disposeHandler: false);
        IPackageSourceAuthorization sourceAuthorization =
            BrowserPackageWorkspace.SourceAuthorizationFor(packageClient);
        await using (BrowserPlatformScopeResolution preload =
            await BrowserPlatformWorkspace.OpenAssemblyAsync(
                "net11.0",
                version,
                Identity(ReadRuntimeAssembly(
                    archive,
                    "System.Private.Uri.dll")),
                "netcore.app",
                networkClient,
                packageClient,
                sourceAuthorization,
                TimeSpan.FromSeconds(30),
                TestContext.Current.CancellationToken))
        {
        }

        BrowserCallGraphInfo result =
            await BrowserPlatformCallGraph.QueryAsync(
                "net11.0",
                version,
                identity.Name,
                "netcore.app",
                identity.Version!.ToString(),
                identity.Culture,
                identity.PublicKeyToken,
                type.DefinitionName!.ToEscapedFullName(),
                selector.MemberName,
                selector.SelectorKey,
                selector.BodyToken,
                networkClient,
                packageClient,
                sourceAuthorization,
                TimeSpan.FromSeconds(30),
                TestContext.Current.CancellationToken);

        Assert.False(result.NoBody);
        Assert.Equal(4, result.Scope.Assemblies);
        Assert.Equal(4, result.Scope.CallerAssemblies);
        Assert.False(
            result.Diagnostics.HasUnexploredTraversalBoundary);
        BrowserCallGraphTargetInfo coreLibraryConstructor =
            Assert.Single(
                result.Targets,
                target =>
                    target.Assembly == "System.Private.CoreLib"
                    && target.MemberName == ".ctor");
        Assert.Equal("object", coreLibraryConstructor.TypeFullName);
        Assert.Equal("normal", coreLibraryConstructor.Kind);
        Assert.Equal(
            "netcore.app",
            coreLibraryConstructor.PlatformPack);
        Assert.Equal(1, handler.CompleteGetRequests);
        Assert.True(handler.RangeRequests >= 1);
        Assert.True(
            handler.PackageBytesServed < archive.LongLength,
            $"served {handler.PackageBytesServed} of {archive.LongLength} package bytes");
    }

    [Fact]
    public async Task PlatformWorkspace_ExactCumulativeScopeReopensAfterEviction()
    {
        const string version = "11.0.7383";
        const string packageId =
            "microsoft.netcore.app.runtime.linux-x64";
        PrepareExactPlatformPersistence();
        byte[] archive = await File.ReadAllBytesAsync(
            Path.Combine(
                AppContext.BaseDirectory,
                "RealAssets",
                "PlatformForwarderActivation",
                "runtime.nupkg"),
            TestContext.Current.CancellationToken);
        AssemblyReferenceIdentity json = Identity(
            ReadRuntimeAssembly(archive, "System.Text.Json.dll"));
        AssemblyReferenceIdentity uri = Identity(
            ReadRuntimeAssembly(archive, "System.Private.Uri.dll"));
        var handler = new ExactPlatformRangeHandler(
            packageId,
            version,
            archive);
        using IPackageSourceClient packageClient =
            BrowserPackageWorkspace.CreateGallerySource(
                handler,
                new NuGetFetchOptions
                {
                    RequestTimeout = TimeSpan.FromSeconds(30),
                    OperationTimeout = TimeSpan.FromSeconds(30),
                });
        using var networkClient =
            new HttpClient(handler, disposeHandler: false);
        IPackageSourceAuthorization sourceAuthorization =
            BrowserPackageWorkspace.SourceAuthorizationFor(packageClient);

        await using BrowserPlatformScopeResolution first =
            await BrowserPlatformWorkspace.OpenAssemblyAsync(
                "net11.0",
                version,
                json,
                "netcore.app",
                networkClient,
                packageClient,
                sourceAuthorization,
                TimeSpan.FromSeconds(30),
                TestContext.Current.CancellationToken);
        await using BrowserPlatformScopeResolution cumulative =
            await BrowserPlatformWorkspace.OpenAssemblyAsync(
                "net11.0",
                version,
                uri,
                "netcore.app",
                networkClient,
                packageClient,
                sourceAuthorization,
                TimeSpan.FromSeconds(30),
                TestContext.Current.CancellationToken);
        Assert.Equal(2, cumulative.Scope.Members.Length);

        await first.DisposeAsync();
        await cumulative.DisposeAsync();
        await BrowserPackageWorkspace.RemoveScopeAsync(cumulative.Scope);
        int requests = handler.PackageRequests;
        long served = handler.PackageBytesServed;

        await using BrowserPlatformScopeResolution reopened =
            await BrowserPlatformWorkspace.OpenAssemblyAsync(
                "net11.0",
                version,
                json,
                "netcore.app",
                networkClient,
                packageClient,
                sourceAuthorization,
                TimeSpan.FromSeconds(30),
                TestContext.Current.CancellationToken);

        Assert.Equal(2, reopened.Scope.Members.Length);
        Assert.Equal(requests, handler.PackageRequests);
        Assert.Equal(served, handler.PackageBytesServed);
        await reopened.DisposeAsync();
        await BrowserPackageWorkspace.RemoveScopeAsync(reopened.Scope);
    }

    [Fact]
    public async Task PlatformWorkspace_UnattributedRuntimeSettlesBeforeAspNetSupport()
    {
        const string version = "11.0.7382";
        const string runtimePackage =
            "microsoft.netcore.app.runtime.linux-x64";
        const string aspNetPackage =
            "microsoft.aspnetcore.app.runtime.linux-x64";
        byte[] coreLibrary =
            await File.ReadAllBytesAsync(
                typeof(object).Assembly.Location,
                TestContext.Current.CancellationToken);
        AssemblyReferenceIdentity identity = Identity(coreLibrary);
        byte[] aspNetAssembly =
            await File.ReadAllBytesAsync(
                typeof(BrowserEngineBoundaryTests).Assembly.Location,
                TestContext.Current.CancellationToken);
        var handler = new ExactPlatformRangeHandler(
            version,
            new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase)
            {
                [runtimePackage] = RuntimePackage(
                    "System.Private.CoreLib.dll",
                    coreLibrary),
                [aspNetPackage] = AspNetPackage(
                    version,
                    "DotnetInspect.Web.Tests.dll",
                    aspNetAssembly),
            });
        using IPackageSourceClient packageClient =
            BrowserPackageWorkspace.CreateGallerySource(
                handler,
                new NuGetFetchOptions
                {
                    RequestTimeout = TimeSpan.FromSeconds(30),
                    OperationTimeout = TimeSpan.FromSeconds(30),
                });
        using var networkClient =
            new HttpClient(handler, disposeHandler: false);

        await using BrowserPlatformScopeResolution resolution =
            await BrowserPlatformWorkspace.OpenAssemblyAsync(
                "net11.0",
                version,
                identity,
                "",
                networkClient,
                packageClient,
                BrowserPackageWorkspace.SourceAuthorizationFor(packageClient),
                TimeSpan.FromSeconds(30),
                TestContext.Current.CancellationToken);

        Assert.Equal("runtime", resolution.Coordinate.Family);
        Assert.Equal(0, handler.PackageRequestsFor(aspNetPackage));
    }

    sealed class ExactPlatformMemoryPersistence :
        IBrowserPackageEntryPersistence
    {
        readonly ConcurrentDictionary<string, byte[]> _items =
            new(StringComparer.Ordinal);

        public bool IsPersistent => true;

        public ValueTask<byte[]?> ReadAsync(string key) =>
            ValueTask.FromResult<byte[]?>(
                _items.TryGetValue(key, out byte[]? content)
                    ? content.ToArray()
                    : null);

        public ValueTask PublishAsync(
            string key,
            ReadOnlyMemory<byte> content)
        {
            _items.TryAdd(key, content.ToArray());
            return ValueTask.CompletedTask;
        }

        internal void Clear() => _items.Clear();
    }

    static void PrepareExactPlatformPersistence()
    {
        if (Interlocked.CompareExchange(
                ref _exactPlatformPersistenceConfigured,
                1,
                0) == 0)
        {
            BrowserPackageWorkspace.ConfigurePackageEntryPersistence(
                ExactPlatformPersistence);
        }
        ExactPlatformPersistence.Clear();
    }

    static byte[] RuntimePackage(
        string assemblyFileName,
        byte[] assembly)
    {
        using var bytes = new MemoryStream();
        using (var archive = new ZipArchive(
            bytes,
            ZipArchiveMode.Create,
            leaveOpen: true))
        {
            const string prefix =
                "runtimes/linux-x64/lib/net11.0/";
            Write(
                archive,
                prefix + "Microsoft.NETCore.App.runtimeconfig.json",
                """{"runtimeOptions":{}}"""u8);
            Write(
                archive,
                prefix + "Microsoft.NETCore.App.deps.json",
                Encoding.UTF8.GetBytes(
                    $$"""
                      {
                        "runtimeTarget":{"name":".NETCoreApp,Version=v11.0/linux-x64"},
                        "targets":{
                          ".NETCoreApp,Version=v11.0/linux-x64":{
                            "Fixture/1.0.0":{
                              "runtime":{
                                "{{assemblyFileName}}":{},
                                "Unrelated.Invalid.dll":{}
                              }
                            }
                          }
                        }
                      }
                      """));
            Write(archive, prefix + assemblyFileName, assembly);
            var unrelated = new byte[4 * 1024 * 1024];
            new Random(42).NextBytes(unrelated);
            Write(archive, prefix + "Unrelated.Invalid.dll", unrelated);
        }
        return bytes.ToArray();
    }

    static byte[] AspNetPackage(
        string version,
        string assemblyFileName,
        byte[] assembly)
    {
        using var bytes = new MemoryStream();
        using (var archive = new ZipArchive(
            bytes,
            ZipArchiveMode.Create,
            leaveOpen: true))
        {
            const string prefix =
                "runtimes/linux-x64/lib/net11.0/";
            Write(
                archive,
                prefix + "Microsoft.AspNetCore.App.runtimeconfig.json",
                Encoding.UTF8.GetBytes(
                    $$$"""
                      {
                        "runtimeOptions":{
                          "framework":{
                            "name":"Microsoft.NETCore.App",
                            "version":"{{{version}}}"
                          }
                        }
                      }
                      """));
            Write(
                archive,
                prefix + "Microsoft.AspNetCore.App.deps.json",
                Encoding.UTF8.GetBytes(
                    $$$"""
                      {
                        "runtimeTarget":{"name":".NETCoreApp,Version=v11.0/linux-x64"},
                        "targets":{
                          ".NETCoreApp,Version=v11.0/linux-x64":{
                            "Fixture/1.0.0":{
                              "runtime":{"{{{assemblyFileName}}}":{}}
                            }
                          }
                        }
                      }
                      """));
            Write(archive, prefix + assemblyFileName, assembly);
        }
        return bytes.ToArray();
    }

    static byte[] ReadRuntimeAssembly(
        byte[] archive,
        string assemblyFileName)
    {
        using var package = new ZipArchive(
            new MemoryStream(archive, writable: false),
            ZipArchiveMode.Read);
        ZipArchiveEntry entry = package.GetEntry(
            $"runtimes/linux-x64/lib/net11.0/{assemblyFileName}")
            ?? throw new InvalidOperationException(
                $"The runtime fixture does not carry {assemblyFileName}.");
        using Stream content = entry.Open();
        using var bytes = new MemoryStream();
        content.CopyTo(bytes);
        return bytes.ToArray();
    }

    static AssemblyReferenceIdentity Identity(byte[] assembly)
    {
        using var image = new PEReader(
            new MemoryStream(assembly, writable: false));
        return AssemblyReferenceIdentity.FromAssemblyDefinition(
            image.GetMetadataReader());
    }

    static void Write(
        ZipArchive archive,
        string path,
        ReadOnlySpan<byte> content)
    {
        using Stream entry = archive.CreateEntry(
            path,
            CompressionLevel.Optimal).Open();
        entry.Write(content);
    }

    sealed class ExactPlatformRangeHandler : HttpMessageHandler
    {
        readonly Dictionary<string, (string PackageId, byte[] Archive)>
            _packages = new(StringComparer.OrdinalIgnoreCase);
        readonly ConcurrentDictionary<string, int> _requests =
            new(StringComparer.OrdinalIgnoreCase);
        long _packageBytesServed;
        int _packageRequests;
        int _rangeRequests;
        int _completeGetRequests;

        internal ExactPlatformRangeHandler(
            string packageId,
            string version,
            byte[] archive)
            : this(
                version,
                new Dictionary<string, byte[]>(
                    StringComparer.OrdinalIgnoreCase)
                {
                    [packageId] = archive,
                })
        {
        }

        internal ExactPlatformRangeHandler(
            string version,
            IReadOnlyDictionary<string, byte[]> packages)
        {
            foreach ((string packageId, byte[] archive) in packages)
            {
                string package = packageId.ToLowerInvariant();
                var item = (packageId, archive);
                _packages[$"/packages/{package}.{version}.nupkg"] = item;
                _packages[
                    $"/v3-flatcontainer/{package}/{version}/"
                    + $"{package}.{version}.nupkg"] = item;
            }
        }

        internal long PackageBytesServed =>
            Interlocked.Read(ref _packageBytesServed);

        internal int PackageRequests =>
            Volatile.Read(ref _packageRequests);

        internal int RangeRequests =>
            Volatile.Read(ref _rangeRequests);

        internal int CompleteGetRequests =>
            Volatile.Read(ref _completeGetRequests);

        internal int PackageRequestsFor(string packageId) =>
            _requests.GetValueOrDefault(packageId);

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string path = request.RequestUri!.AbsolutePath;
            if (!_packages.TryGetValue(
                    path,
                    out (string PackageId, byte[] Archive) package))
            {
                return Task.FromResult(new HttpResponseMessage(
                    HttpStatusCode.NotFound));
            }

            byte[] archive = package.Archive;
            _requests.AddOrUpdate(
                package.PackageId,
                1,
                static (_, current) => current + 1);
            Interlocked.Increment(ref _packageRequests);
            if (request.Method == HttpMethod.Head)
            {
                var response = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent([]),
                };
                response.Content.Headers.ContentLength =
                    archive.LongLength;
                return Task.FromResult(response);
            }

            RangeItemHeaderValue? range =
                request.Headers.Range?.Ranges.SingleOrDefault();
            if (range is null)
            {
                Interlocked.Increment(ref _completeGetRequests);
                return Task.FromResult(new HttpResponseMessage(
                    HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(archive),
                });
            }

            long start = range.From
                ?? archive.LongLength - Math.Min(
                    range.To!.Value,
                    archive.LongLength);
            long end = range.From is null
                ? archive.LongLength - 1
                : Math.Min(
                    range.To ?? archive.LongLength - 1,
                    archive.LongLength - 1);
            int length = checked((int)(end - start + 1));
            Interlocked.Increment(ref _rangeRequests);
            Interlocked.Add(ref _packageBytesServed, length);
            var content = new ByteArrayContent(
                archive,
                checked((int)start),
                length);
            content.Headers.ContentRange =
                new ContentRangeHeaderValue(
                    start,
                    end,
                    archive.LongLength);
            return Task.FromResult(new HttpResponseMessage(
                HttpStatusCode.PartialContent)
            {
                Content = content,
            });
        }
    }
}
