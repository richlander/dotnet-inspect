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
    [Fact]
    public async Task PlatformWorkspace_ExactAssemblyUsesRangeAndReusesEntryCache()
    {
        const string version = "11.0.7381";
        const string packageId =
            "microsoft.netcore.app.runtime.linux-x64";
        const string assemblyFileName =
            "InspectWeb.MethodBodyFixtures.dll";
        BrowserPackageWorkspace.ConfigurePackageEntryPersistence(
            new ExactPlatformMemoryPersistence());
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
        byte[] archive = await File.ReadAllBytesAsync(
            Path.Combine(
                AppContext.BaseDirectory,
                "RealAssets",
                "PlatformForwarderActivation",
                "runtime.nupkg"),
            TestContext.Current.CancellationToken);
        byte[] assembly;
        using (var package = new ZipArchive(
            new MemoryStream(archive, writable: false),
            ZipArchiveMode.Read))
        {
            ZipArchiveEntry entry = package.GetEntry(
                $"runtimes/linux-x64/lib/net11.0/{assemblyFileName}")
                ?? throw new InvalidOperationException(
                    "The runtime fixture does not carry System.Text.Json.");
            using Stream content = entry.Open();
            using var bytes = new MemoryStream();
            await content.CopyToAsync(
                bytes,
                TestContext.Current.CancellationToken);
            assembly = bytes.ToArray();
        }

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
                BrowserPackageWorkspace.SourceAuthorizationFor(
                    packageClient),
                TimeSpan.FromSeconds(30),
                TestContext.Current.CancellationToken);

        Assert.False(result.NoBody);
        Assert.True(result.Scope.Assemblies >= 1);
        Assert.Equal(1, handler.CompleteGetRequests);
        Assert.True(handler.RangeRequests >= 1);
        Assert.True(
            handler.PackageBytesServed < archive.LongLength,
            $"served {handler.PackageBytesServed} of {archive.LongLength} package bytes");
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

    sealed class ExactPlatformRangeHandler(
        string packageId,
        string version,
        byte[] archive) : HttpMessageHandler
    {
        readonly string _packagePath =
            $"/packages/{packageId}.{version}.nupkg";
        readonly string _flatContainerPath =
            $"/v3-flatcontainer/{packageId}/{version}/"
            + $"{packageId}.{version}.nupkg";
        long _packageBytesServed;
        int _packageRequests;
        int _rangeRequests;
        int _completeGetRequests;

        internal long PackageBytesServed =>
            Interlocked.Read(ref _packageBytesServed);

        internal int PackageRequests =>
            Volatile.Read(ref _packageRequests);

        internal int RangeRequests =>
            Volatile.Read(ref _rangeRequests);

        internal int CompleteGetRequests =>
            Volatile.Read(ref _completeGetRequests);

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string path = request.RequestUri!.AbsolutePath;
            if (!path.Equals(_packagePath, StringComparison.OrdinalIgnoreCase)
                && !path.Equals(
                    _flatContainerPath,
                    StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(new HttpResponseMessage(
                    HttpStatusCode.NotFound));
            }

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
