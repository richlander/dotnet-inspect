using System.Buffers.Binary;
using System.IO.Compression;
using System.Net;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Runtime.CompilerServices;
using System.Text;

using DotnetInspector.Packages;
using DotnetInspector.Fixtures;
using DotnetInspector.Queries.EmbeddedFixtures;
using DotnetInspector.Services;
using ILInspector.Decompiler;
using Inspector.Findings;
using ILInspector.Metadata;
using ILInspector.SourceLink;
using Pipeline = ILInspector.Decompiler.Pipeline;

namespace DotnetInspector.Queries.Tests;

public sealed partial class AssemblyContextSourceQueryTests
{
    sealed class TestAssembly
    {
        readonly ApiSurface _surface;

        TestAssembly(
            ResolvedAssemblyReference assembly,
            AssemblyContextParticipant participant,
            string pdbPath,
            ApiSurface surface,
            FrameworkBindingPolicy policy)
        {
            Assembly = assembly;
            Participant = participant;
            PdbPath = pdbPath;
            _surface = surface;
            Policy = policy;
        }

        internal ResolvedAssemblyReference Assembly { get; }
        internal AssemblyContextParticipant Participant { get; }
        internal string PdbPath { get; }
        internal FrameworkBindingPolicy Policy { get; }

        internal static TestAssembly Create(
            string? selectedName = null,
            bool retainPath = false,
            Func<Stream>? openRead = null,
            FixtureDefinition? fixture = null,
            string packageVersion = "1.0.0")
        {
            string path =
                fixture?.AssemblyPath()
                ?? typeof(AssemblyContextSourceQueryTests)
                    .Assembly.Location;
            byte[] bytes = File.ReadAllBytes(path);
            AssemblyReferenceIdentity identity =
                ReadIdentity(bytes);
            if (selectedName is not null)
            {
                identity = identity with
                {
                    Name = selectedName,
                };
            }

            var assembly =
                ResolvedAssemblyReference.Create(
                    identity,
                    retainPath
                        ? path
                        : null,
                    openRead
                        ?? (() => new MemoryStream(
                            bytes,
                            writable: false)),
                    AssemblyResolutionProvenance.Package(
                        "Example.Source",
                        packageVersion,
                        "net10.0",
                        rid: null));
            var policy = new FrameworkBindingPolicy();
            var participant =
                new AssemblyContextParticipant(
                    assembly,
                    policy);
            using AssemblyInspectionSession session =
                AssemblyInspectionSession.Open(
                    ResolvedAssemblyReference.Create(
                        ReadIdentity(bytes),
                        path: null,
                        () => new MemoryStream(
                            bytes,
                            writable: false),
                        AssemblyResolutionProvenance.Local(
                            "source query target")));
            return new TestAssembly(
                assembly,
                participant,
                Path.ChangeExtension(path, ".pdb"),
                session.ApiSurface(includeAll: true),
                policy);
        }

        internal static TestAssembly Create(
            byte[] bytes)
        {
            AssemblyReferenceIdentity identity =
                ReadIdentity(bytes);
            var assembly =
                ResolvedAssemblyReference.Create(
                    identity,
                    path: null,
                    () => new MemoryStream(
                        bytes,
                        writable: false),
                    AssemblyResolutionProvenance.Local(
                        "embedded source query fixture"));
            var policy = new FrameworkBindingPolicy();
            var participant =
                new AssemblyContextParticipant(
                    assembly,
                    policy);
            using AssemblyInspectionSession session =
                AssemblyInspectionSession.Open(assembly);
            return new TestAssembly(
                assembly,
                participant,
                pdbPath: "",
                session.ApiSurface(includeAll: true),
                policy);
        }

        internal static TestAssembly CreatePackage(
            byte[] bytes,
            string pdbPath)
        {
            AssemblyReferenceIdentity identity =
                ReadIdentity(bytes);
            var assembly =
                ResolvedAssemblyReference.Create(
                    identity,
                    path: null,
                    () => new MemoryStream(
                        bytes,
                        writable: false),
                    AssemblyResolutionProvenance.Package(
                        "Example.Source",
                        "1.0.0",
                        "net10.0",
                        rid: null));
            var policy = new FrameworkBindingPolicy();
            var participant =
                new AssemblyContextParticipant(
                    assembly,
                    policy);
            using AssemblyInspectionSession session =
                AssemblyInspectionSession.Open(
                    assembly);
            return new TestAssembly(
                assembly,
                participant,
                pdbPath,
                session.ApiSurface(includeAll: true),
                policy);
        }

        internal AssemblyTypeSourceRequest TypeRequest(
            string typeName,
            Pipeline.PrinterOptions? printerOptions = null)
            => AssemblyTypeSourceRequest.From(
                TypeTarget(typeName),
                printerOptions);

        internal ApiType TypeTarget(
            string typeName)
        {
            return Assert.Single(
                _surface.Types,
                candidate =>
                    candidate.DefinitionName?.Segments[^1]
                    == typeName);
        }

        internal AssemblyMemberSourceRequest MemberRequest(
            string memberName,
            string? typeName = null,
            Pipeline.PrinterOptions? printerOptions = null)
        {
            var target =
                MemberTarget(memberName, typeName);
            return AssemblyMemberSourceRequest.From(
                target.Type,
                target.Member,
                printerOptions);
        }

        internal (ApiType Type, ApiMember Member) MemberTarget(
            string memberName,
            string? typeName = null)
        {
            ApiType type = Assert.Single(
                _surface.Types,
                candidate =>
                    candidate.DefinitionName?.Segments[^1]
                    == (typeName
                        ?? typeof(SourceFixture).Name));
            ApiMember member = Assert.Single(
                type.Members,
                candidate => candidate.Name == memberName);
            return (type, member);
        }

        static AssemblyReferenceIdentity ReadIdentity(
            byte[] bytes)
        {
            using var stream =
                new MemoryStream(bytes, writable: false);
            using var reader = new PEReader(stream);
            return AssemblyReferenceIdentity
                .FromAssemblyDefinition(
                    reader.GetMetadataReader());
        }
    }

    sealed class QueryHost : IDisposable
    {
        readonly HttpClient _symbolClient;
        readonly HttpClient _sourceClient;

        QueryHost(
            SymbolPackageHandler symbolHandler,
            SourceHandler sourceHandler,
            ISourceContentStore? sourceContentStore = null,
            IPdbStore? pdbStore = null,
            bool allowLocalSourceReads = false,
            SymbolAcquisitionLimits? symbolAcquisitionLimits = null,
            bool allowAdjacentPdbReads = false,
            int maxDecompilerBodyProjections = CSharpDecompilerService.DefaultMaxBodyProjections)
        {
            _symbolClient = new HttpClient(symbolHandler);
            _sourceClient = new HttpClient(sourceHandler);
            Context = new AssemblyContextSourceQueryContext(
                _symbolClient,
                pdbStore
                    ?? new InMemoryPdbStore(),
                new UniformPackageSourceAuthorization(
                    [NuGetFetch.PackageSource.NuGetOrg]),
                new SourceFetch(
                    _sourceClient,
                    sourceContentStore
                        ?? new InMemorySourceContentStore()))
            {
                AllowLocalSourceReads =
                    allowLocalSourceReads,
                AllowAdjacentPdbReads =
                    allowAdjacentPdbReads,
                SymbolAcquisitionLimits =
                    symbolAcquisitionLimits,
                MaxDecompilerBodyProjections = maxDecompilerBodyProjections,
            };
            SymbolRequests = symbolHandler.RequestUris;
            SourceRequests = sourceHandler.RequestUris;
        }

        internal AssemblyContextSourceQueryContext Context
        {
            get;
        }
        internal List<Uri> SymbolRequests { get; }
        internal List<Uri> SourceRequests { get; }

        internal static QueryHost WithPdb(
            string pdbPath,
            byte[] sourceBytes,
            ISourceContentStore? sourceContentStore = null,
            IPdbStore? pdbStore = null,
            int maxDecompilerBodyProjections = CSharpDecompilerService.DefaultMaxBodyProjections,
            Func<CancellationToken, Task>? beforeSymbolResponse = null,
            Func<CancellationToken, Task>? beforeSourceResponse = null)
        {
            Assert.True(
                File.Exists(pdbPath),
                $"Expected test PDB at {pdbPath}");
            return new QueryHost(
                new SymbolPackageHandler(
                    BuildSnupkg(
                        Path.GetFileName(pdbPath),
                        File.ReadAllBytes(pdbPath)),
                    beforeResponse:
                        beforeSymbolResponse),
                new SourceHandler(
                    sourceBytes,
                    beforeResponse:
                        beforeSourceResponse),
                sourceContentStore,
                pdbStore,
                maxDecompilerBodyProjections: maxDecompilerBodyProjections);
        }

        internal static QueryHost WithPdb(
            string pdbFileName,
            byte[] pdbBytes,
            byte[] sourceBytes,
            bool allowLocalSourceReads = false)
            => new(
                new SymbolPackageHandler(
                    BuildSnupkg(
                        pdbFileName,
                        pdbBytes)),
                new SourceHandler(sourceBytes),
                allowLocalSourceReads:
                    allowLocalSourceReads);

        internal static QueryHost WithSource(
            byte[] sourceBytes)
            => new(
                new SymbolPackageHandler(snupkg: null),
                new SourceHandler(sourceBytes));

        internal static QueryHost WithUnavailableSource(
            HttpStatusCode statusCode)
            => new(
                new SymbolPackageHandler(snupkg: null),
                new SourceHandler(
                    content: null,
                    unavailableStatusCode: statusCode));

        internal static QueryHost WithoutPdb(
            SymbolAcquisitionLimits? symbolAcquisitionLimits = null,
            bool allowLocalSourceReads = false,
            bool allowAdjacentPdbReads = false,
            int maxDecompilerBodyProjections = CSharpDecompilerService.DefaultMaxBodyProjections)
            => new(
                new SymbolPackageHandler(snupkg: null),
                new SourceHandler(content: null),
                symbolAcquisitionLimits: symbolAcquisitionLimits,
                allowLocalSourceReads: allowLocalSourceReads,
                allowAdjacentPdbReads: allowAdjacentPdbReads,
                maxDecompilerBodyProjections: maxDecompilerBodyProjections);

        internal static QueryHost WithPairPdb(
            TestAssembly before,
            TestAssembly after,
            byte[] beforeSource,
            byte[]? afterSource,
            Action? duringAfterSource = null,
            bool missingAfterPdb = false)
        {
            byte[] beforeSymbols = BuildSnupkg(
                Path.GetFileName(before.PdbPath),
                File.ReadAllBytes(before.PdbPath));
            byte[]? afterSymbols = missingAfterPdb
                ? null
                : BuildSnupkg(
                    Path.GetFileName(after.PdbPath),
                    File.ReadAllBytes(after.PdbPath));
            return new QueryHost(
                new SymbolPackageHandler(null, uri =>
                    uri.AbsolutePath.Contains("2.0.0", StringComparison.Ordinal)
                        ? afterSymbols
                        : beforeSymbols),
                new SourceHandler(null, uri =>
                {
                    if (uri.AbsolutePath.StartsWith("/v2/", StringComparison.Ordinal))
                    {
                        duringAfterSource?.Invoke();
                        return afterSource;
                    }
                    return beforeSource;
                }));
        }

        public void Dispose()
        {
            _sourceClient.Dispose();
            _symbolClient.Dispose();
        }

        static byte[] BuildSnupkg(
            string pdbFileName,
            byte[] pdbBytes)
        {
            using var buffer = new MemoryStream();
            using (var archive = new ZipArchive(
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
    }

    sealed class SymbolPackageHandler(
        byte[]? snupkg,
        Func<Uri, byte[]?>? response = null,
        Func<CancellationToken, Task>? beforeResponse = null)
        : HttpMessageHandler
    {
        internal List<Uri> RequestUris { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestUris.Add(request.RequestUri!);
            if (beforeResponse is not null)
            {
                await beforeResponse(
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            byte[]? content = response is null
                ? snupkg
                : response(request.RequestUri!);
            if (content is not null
                && request.RequestUri!.AbsolutePath.EndsWith(
                    ".snupkg",
                    StringComparison.OrdinalIgnoreCase))
            {
                return new HttpResponseMessage(
                    HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(content),
                    RequestMessage = request,
                };
            }

            return new HttpResponseMessage(
                HttpStatusCode.NotFound)
            {
                RequestMessage = request,
            };
        }
    }

    sealed class SourceHandler(
        byte[]? content,
        Func<Uri, byte[]?>? response = null,
        HttpStatusCode unavailableStatusCode = HttpStatusCode.NotFound,
        Func<CancellationToken, Task>? beforeResponse = null)
        : HttpMessageHandler
    {
        internal List<Uri> RequestUris { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestUris.Add(request.RequestUri!);
            if (beforeResponse is not null)
            {
                await beforeResponse(
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            byte[]? source = response is null
                ? content
                : response(request.RequestUri!);
            return new HttpResponseMessage(
                source is null
                    ? unavailableStatusCode
                    : HttpStatusCode.OK)
            {
                Content = source is null
                    ? null
                    : new ByteArrayContent(source),
                RequestMessage = request,
            };
        }
    }
}
