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

        await Assert.ThrowsAsync<IOException>(
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
