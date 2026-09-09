using System.IO.Compression;
using System.Net;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using DotnetInspector.Packages;
using ILInspector.Metadata;

namespace DotnetInspector.Services.Tests;

public class PdbAcquisitionServiceTests
{
    [Fact]
    public async Task SelectedPackageDescriptor_OverridesCallerPackageFallback()
    {
        var (assembly, pdbBytes) = CreateTestAssembly(
            AssemblyResolutionProvenance.Package(
                "Supplier.Symbols",
                "2.0.0",
                "net10.0",
                rid: null));
        using var source = SourceLinkService.Open(assembly);
        var handler = new SymbolPackageHandler(
            BuildSnupkg(
                source.Context.PdbId!.PdbFileName,
                pdbBytes));
        using var client = new HttpClient(handler);

        await PdbAcquisitionService.AcquireAsync(
            source.Context,
            assembly,
            client,
            new InMemoryPdbStore(),
            new UniformPackageSourceAuthorization(
                [NuGetFetch.PackageSource.NuGetOrg]),
            log: null,
            cancellationToken:
                TestContext.Current.CancellationToken,
            fallbackPackageName: "Root.Symbols",
            fallbackPackageVersion: "1.0.0");

        Assert.True(source.HasPdb);
        Uri request = Assert.Single(
            handler.RequestUris,
            static uri => uri.AbsolutePath.EndsWith(
                ".snupkg",
                StringComparison.OrdinalIgnoreCase));
        Assert.Contains(
            "supplier.symbols.2.0.0.snupkg",
            request.AbsolutePath,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            "root.symbols",
            request.AbsolutePath,
            StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("local")]
    [InlineData("project")]
    [InlineData("designated")]
    public async Task SelectedLocalOrProjectDescriptor_UsesCallerPackageFallback(
        string provenanceKind)
    {
        var (assembly, pdbBytes) = CreateTestAssembly(
            provenanceKind switch
            {
                "local" =>
                    AssemblyResolutionProvenance.Local("test"),
                "project" =>
                    AssemblyResolutionProvenance.Project(
                        "test.csproj",
                        "net10.0",
                        rid: null),
                "designated" =>
                    AssemblyResolutionProvenance.Designated("test"),
                _ => throw new ArgumentOutOfRangeException(
                    nameof(provenanceKind)),
            });
        using var source = SourceLinkService.Open(assembly);
        var handler = new SymbolPackageHandler(
            BuildSnupkg(
                source.Context.PdbId!.PdbFileName,
                pdbBytes));
        using var client = new HttpClient(handler);

        await PdbAcquisitionService.AcquireAsync(
            source.Context,
            assembly,
            client,
            new InMemoryPdbStore(),
            new UniformPackageSourceAuthorization(
                [NuGetFetch.PackageSource.NuGetOrg]),
            log: null,
            cancellationToken:
                TestContext.Current.CancellationToken,
            fallbackPackageName: "Root.Symbols",
            fallbackPackageVersion: "1.0.0");

        Assert.True(source.HasPdb);
        Uri request = Assert.Single(
            handler.RequestUris,
            static uri => uri.AbsolutePath.EndsWith(
                ".snupkg",
                StringComparison.OrdinalIgnoreCase));
        Assert.Contains(
            "root.symbols.1.0.0.snupkg",
            request.AbsolutePath,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SelectedPlatformDescriptor_IgnoresCallerPackageFallback()
    {
        var (assembly, _) = CreateTestAssembly(
            AssemblyResolutionProvenance.Platform(
                "runtime",
                "10.0.0",
                "test"));
        using var source = SourceLinkService.Open(assembly);
        var handler = new SymbolPackageHandler([]);
        using var client = new HttpClient(handler);

        await PdbAcquisitionService.AcquireAsync(
            source.Context,
            assembly,
            client,
            new InMemoryPdbStore(),
            new UniformPackageSourceAuthorization(
                [NuGetFetch.PackageSource.NuGetOrg]),
            log: null,
            cancellationToken:
                TestContext.Current.CancellationToken,
            fallbackPackageName: "Root.Symbols",
            fallbackPackageVersion: "1.0.0");

        Assert.NotEmpty(handler.RequestUris);
        Assert.DoesNotContain(
            handler.RequestUris,
            static uri => uri.AbsolutePath.EndsWith(
                ".snupkg",
                StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(
            handler.RequestUris,
            static uri => uri.AbsolutePath.Contains(
                "root.symbols",
                StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task PathlessParticipant_AcquiresMatchingPdbThroughInMemoryStore()
    {
        string assemblyPath =
            typeof(PdbAcquisitionServiceTests).Assembly.Location;
        string pdbPath =
            Path.ChangeExtension(assemblyPath, ".pdb");
        Assert.True(
            File.Exists(pdbPath),
            $"Expected test PDB at {pdbPath}");

        byte[] assemblyBytes = File.ReadAllBytes(assemblyPath);
        AssemblyReferenceIdentity identity =
            ReadIdentity(assemblyBytes);
        var assembly =
            ResolvedAssemblyReference.Create(
                identity,
                path: null,
                () => new MemoryStream(
                    assemblyBytes,
                    writable: false),
                AssemblyResolutionProvenance.Package(
                    "Example.Symbols",
                    "1.0.0",
                    "net10.0",
                    rid: null));
        using var source = SourceLinkService.Open(assembly);
        Assert.True(source.Context.NeedsPdb);

        byte[] snupkg =
            BuildSnupkg(
                Path.GetFileName(pdbPath),
                File.ReadAllBytes(pdbPath));
        var handler = new SymbolPackageHandler(snupkg);
        using var client = new HttpClient(handler);

        await PdbAcquisitionService.AcquireAsync(
            source.Context,
            assembly,
            client,
            new InMemoryPdbStore(),
            new UniformPackageSourceAuthorization(
                [NuGetFetch.PackageSource.NuGetOrg]),
            log: null,
            cancellationToken:
                TestContext.Current.CancellationToken);

        Assert.True(source.HasPdb);
        Assert.Null(source.Context.PortablePdbPath);
        Assert.NotEmpty(
            source.Context.EnumeratePdbDocuments());
        Assert.Single(handler.RequestUris);
        Assert.EndsWith(
            ".snupkg",
            handler.RequestUris[0].AbsolutePath,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DescriptorAcquisition_RequiresExplicitHostCapabilities()
    {
        var overload =
            Assert.Single(
                typeof(PdbAcquisitionService).GetMethods(),
                method =>
                {
                    var parameters = method.GetParameters();
                    return parameters.Length > 1
                        && parameters[1].ParameterType
                            == typeof(ResolvedAssemblyReference)
                        && parameters.Any(
                            parameter => parameter.ParameterType
                                == typeof(IPdbStore));
                });
        var parameters = overload.GetParameters();

        Assert.False(
            Assert.Single(
                parameters,
                parameter => parameter.ParameterType
                    == typeof(IPdbStore))
                .IsOptional);
        Assert.False(
            Assert.Single(
                parameters,
                parameter => parameter.ParameterType
                    == typeof(IPackageSourceAuthorization))
                .IsOptional);
    }

    [Fact]
    public async Task PathlessParticipant_DesktopOverloadDoesNotAcquire()
    {
        string assemblyPath =
            typeof(PdbAcquisitionServiceTests).Assembly.Location;
        string pdbPath =
            Path.ChangeExtension(assemblyPath, ".pdb");
        byte[] assemblyBytes = File.ReadAllBytes(assemblyPath);
        var assembly =
            ResolvedAssemblyReference.Create(
                ReadIdentity(assemblyBytes),
                path: null,
                () => new MemoryStream(
                    assemblyBytes,
                    writable: false),
                AssemblyResolutionProvenance.Package(
                    "Example.Symbols",
                    "1.0.0",
                    "net10.0",
                    rid: null));
        using var source = SourceLinkService.Open(assembly);
        byte[] snupkg =
            BuildSnupkg(
                Path.GetFileName(pdbPath),
                File.ReadAllBytes(pdbPath));
        var handler = new SymbolPackageHandler(snupkg);
        using var client = new HttpClient(handler);

        await PdbAcquisitionService.AcquireAsync(
            source.Context,
            assembly,
            client,
            log: null,
            cancellationToken:
                TestContext.Current.CancellationToken);

        Assert.False(source.HasPdb);
        Assert.Empty(handler.RequestUris);
    }

    [Fact]
    public async Task PathlessParticipant_StoreReadFailureIsVisible()
    {
        string assemblyPath =
            typeof(PdbAcquisitionServiceTests).Assembly.Location;
        string pdbPath =
            Path.ChangeExtension(assemblyPath, ".pdb");
        byte[] assemblyBytes = File.ReadAllBytes(assemblyPath);
        var assembly =
            ResolvedAssemblyReference.Create(
                ReadIdentity(assemblyBytes),
                path: null,
                () => new MemoryStream(
                    assemblyBytes,
                    writable: false),
                AssemblyResolutionProvenance.Package(
                    "Example.Symbols",
                    "1.0.0",
                    "net10.0",
                    rid: null));
        using var source = SourceLinkService.Open(assembly);
        byte[] snupkg =
            BuildSnupkg(
                Path.GetFileName(pdbPath),
                File.ReadAllBytes(pdbPath));
        using var client =
            new HttpClient(
                new SymbolPackageHandler(snupkg));

        PdbStoreAcquisitionException exception =
            await Assert.ThrowsAsync<PdbStoreAcquisitionException>(
            () => PdbAcquisitionService.AcquireAsync(
                source.Context,
                assembly,
                client,
                new FailingStoredReadPdbStore(),
                new UniformPackageSourceAuthorization(
                    [NuGetFetch.PackageSource.NuGetOrg]),
                log: null,
                cancellationToken:
                    TestContext.Current.CancellationToken));
        Assert.Equal(
            PortablePdbStoreFailureKind.ReadFailed,
            exception.StoreFailure);
    }

    [Fact]
    public async Task PathlessParticipant_StoreWriteFailureIsVisible()
    {
        string assemblyPath =
            typeof(PdbAcquisitionServiceTests).Assembly.Location;
        string pdbPath =
            Path.ChangeExtension(assemblyPath, ".pdb");
        byte[] assemblyBytes = File.ReadAllBytes(assemblyPath);
        var assembly =
            ResolvedAssemblyReference.Create(
                ReadIdentity(assemblyBytes),
                path: null,
                () => new MemoryStream(
                    assemblyBytes,
                    writable: false),
                AssemblyResolutionProvenance.Package(
                    "Example.Symbols",
                    "1.0.0",
                    "net10.0",
                    rid: null));
        using var source = SourceLinkService.Open(assembly);
        byte[] snupkg =
            BuildSnupkg(
                Path.GetFileName(pdbPath),
                File.ReadAllBytes(pdbPath));
        using var client =
            new HttpClient(
                new SymbolPackageHandler(snupkg));

        PdbStoreAcquisitionException exception =
            await Assert.ThrowsAsync<PdbStoreAcquisitionException>(
            () => PdbAcquisitionService.AcquireAsync(
                source.Context,
                assembly,
                client,
                new FailingStoredWritePdbStore(),
                new UniformPackageSourceAuthorization(
                    [NuGetFetch.PackageSource.NuGetOrg]),
                log: null,
                cancellationToken:
                    TestContext.Current.CancellationToken));
        Assert.Equal(
            PortablePdbStoreFailureKind.PublicationNotRetained,
            exception.StoreFailure);
    }

    [Fact]
    public async Task PathlessParticipant_LocalPathFailurePrecedesOwnedStreamOpen()
    {
        string assemblyPath =
            typeof(PdbAcquisitionServiceTests).Assembly.Location;
        string pdbPath =
            Path.ChangeExtension(assemblyPath, ".pdb");
        byte[] assemblyBytes = File.ReadAllBytes(assemblyPath);
        var assembly =
            ResolvedAssemblyReference.Create(
                ReadIdentity(assemblyBytes),
                path: null,
                () => new MemoryStream(
                    assemblyBytes,
                    writable: false),
                AssemblyResolutionProvenance.Package(
                    "Example.Symbols",
                    "1.0.0",
                    "net10.0",
                    rid: null));
        using var source = SourceLinkService.Open(assembly);
        byte[] snupkg =
            BuildSnupkg(
                Path.GetFileName(pdbPath),
                File.ReadAllBytes(pdbPath));
        using var client =
            new HttpClient(
                new SymbolPackageHandler(snupkg));
        var store = new ThrowingLocalPathPdbStore();

        await Assert.ThrowsAsync<HttpRequestException>(
            () => PdbAcquisitionService.AcquireAsync(
                source.Context,
                assembly,
                client,
                store,
                new UniformPackageSourceAuthorization(
                    [NuGetFetch.PackageSource.NuGetOrg]),
                log: null,
                cancellationToken:
                    TestContext.Current.CancellationToken));

        Assert.Equal(1, store.OpenCount);
        Assert.True(Assert.Single(store.OpenedStreams).IsDisposed);
    }

    private static AssemblyReferenceIdentity ReadIdentity(
        byte[] assemblyBytes)
    {
        using var stream =
            new MemoryStream(
                assemblyBytes,
                writable: false);
        using var reader = new PEReader(stream);
        return AssemblyReferenceIdentity.FromAssemblyDefinition(
            reader.GetMetadataReader());
    }

    private static (
        ResolvedAssemblyReference Assembly,
        byte[] PdbBytes)
        CreateTestAssembly(
            AssemblyResolutionProvenance provenance)
    {
        string assemblyPath =
            typeof(PdbAcquisitionServiceTests).Assembly.Location;
        string pdbPath =
            Path.ChangeExtension(assemblyPath, ".pdb");
        Assert.True(
            File.Exists(pdbPath),
            $"Expected test PDB at {pdbPath}");
        byte[] assemblyBytes = File.ReadAllBytes(assemblyPath);
        return (
            ResolvedAssemblyReference.Create(
                ReadIdentity(assemblyBytes),
                path: null,
                () => new MemoryStream(
                    assemblyBytes,
                    writable: false),
                provenance),
            File.ReadAllBytes(pdbPath));
    }

    private static byte[] BuildSnupkg(
        string pdbFileName,
        byte[] pdbBytes)
    {
        using var buffer = new MemoryStream();
        using (var archive =
               new ZipArchive(
                   buffer,
                   ZipArchiveMode.Create,
                   leaveOpen: true))
        {
            ZipArchiveEntry entry =
                archive.CreateEntry(
                    $"lib/net10.0/{pdbFileName}");
            using Stream stream = entry.Open();
            stream.Write(pdbBytes);
        }

        return buffer.ToArray();
    }

    private sealed class SymbolPackageHandler(
        byte[] snupkg) : HttpMessageHandler
    {
        public List<Uri> RequestUris { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestUris.Add(request.RequestUri!);
            if (request.RequestUri!.AbsolutePath.EndsWith(
                    ".snupkg",
                    StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(
                    new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new ByteArrayContent(snupkg),
                    });
            }

            return Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }

    private sealed class FailingStoredReadPdbStore : IPdbStore
    {
        private byte[]? _content;
        private int _storedOpenCount;

        public ValueTask<Stream?> TryOpenAsync(
            string key,
            CancellationToken cancellationToken = default)
        {
            if (_content is null)
                return ValueTask.FromResult<Stream?>(null);

            Stream stream =
                Interlocked.Increment(ref _storedOpenCount) == 1
                    ? new MemoryStream(_content, writable: false)
                    : new FailingReadStream(_content);
            return ValueTask.FromResult<Stream?>(stream);
        }

        public async ValueTask PutAsync(
            string key,
            Stream content,
            CancellationToken cancellationToken = default)
        {
            using var buffer = new MemoryStream();
            await content.CopyToAsync(
                buffer,
                cancellationToken);
            _content = buffer.ToArray();
        }

        public string? TryGetLocalPath(string key)
            => null;
    }

    private sealed class FailingStoredWritePdbStore : IPdbStore
    {
        public ValueTask<Stream?> TryOpenAsync(
            string key,
            CancellationToken cancellationToken = default)
            => ValueTask.FromResult<Stream?>(null);

        public ValueTask PutAsync(
            string key,
            Stream content,
            CancellationToken cancellationToken = default)
            => ValueTask.FromException(
                new IOException("Injected store write failure."));

        public string? TryGetLocalPath(string key)
            => null;
    }

    private sealed class ThrowingLocalPathPdbStore : IPdbStore
    {
        byte[]? _content;
        int _openCount;

        internal int OpenCount =>
            Volatile.Read(ref _openCount);
        internal List<TrackingMemoryStream> OpenedStreams { get; } =
            [];

        public ValueTask<Stream?> TryOpenAsync(
            string key,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_content is null)
                return ValueTask.FromResult<Stream?>(null);

            Interlocked.Increment(ref _openCount);
            var stream =
                new TrackingMemoryStream(_content);
            OpenedStreams.Add(stream);
            return ValueTask.FromResult<Stream?>(stream);
        }

        public async ValueTask PutAsync(
            string key,
            Stream content,
            CancellationToken cancellationToken = default)
        {
            using var buffer = new MemoryStream();
            await content.CopyToAsync(
                buffer,
                cancellationToken);
            _content = buffer.ToArray();
        }

        public string? TryGetLocalPath(string key) =>
            throw new HttpRequestException(
                "Injected local-path store failure.");
    }

    private sealed class TrackingMemoryStream(
        byte[] content) : MemoryStream(
        content,
        writable: false)
    {
        internal bool IsDisposed { get; private set; }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                IsDisposed = true;
            base.Dispose(disposing);
        }
    }

    private sealed class FailingReadStream(
        byte[] content) : MemoryStream(
            content,
            writable: false)
    {
        public override int Read(
            byte[] buffer,
            int offset,
            int count)
            => throw new IOException(
                "Injected store read failure.");

        public override int Read(Span<byte> buffer)
            => throw new IOException(
                "Injected store read failure.");
    }
}
