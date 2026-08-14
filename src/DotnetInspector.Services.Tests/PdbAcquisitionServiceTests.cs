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
            log: null,
            cancellationToken:
                TestContext.Current.CancellationToken,
            pdbStore: new InMemoryPdbStore(),
            sourceAuthorization:
                new UniformPackageSourceAuthorization(
                    [NuGetFetch.PackageSource.NuGetOrg]));

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
}
