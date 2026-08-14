using System.IO.Compression;
using System.Net;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Runtime.CompilerServices;

using DotnetInspector.Packages;
using DotnetInspector.Services;
using ILInspector.Findings;
using ILInspector.Metadata;

namespace DotnetInspector.Queries.Tests;

public sealed class AssemblyContextSourceQueryTests
{
    [Fact]
    public async Task PathlessMember_AcquiresVerifiedAuthoredSource()
    {
        TestAssembly assembly = TestAssembly.Create();
        AssemblyMemberSourceRequest request =
            assembly.MemberRequest(nameof(SourceFixture.Describe));
        using var host = QueryHost.WithPdb(
            assembly.PdbPath,
            SourceFileBytes());
        using var workspace = new InspectionWorkspace();
        AssemblyContextGroup group =
            workspace.CreateAssemblyContextGroup(
                [assembly.Participant]);

        AssemblyMemberSourceEntry result =
            await AssemblyContextSourceQuery.ExecuteMemberAsync(
                group,
                assembly.Participant,
                request,
                host.Context,
                TestContext.Current.CancellationToken);

        var available =
            Assert.IsType<AssemblyMemberSourceEntry.Available>(
                result);
        var authored =
            Assert.IsType<AssemblyMemberSource.Authored>(
                available.Source);
        Assert.Contains(
            nameof(SourceFixture.Describe),
            authored.Text,
            StringComparison.Ordinal);
        Assert.Equal(
            SourceChecksumVerification.Exact,
            authored.Inspection.ChecksumVerification);
        Assert.NotNull(authored.Inspection.Mapping);
        Assert.NotNull(authored.Inspection.Document);
        Assert.Null(assembly.Assembly.Path);
        Assert.NotEmpty(host.SymbolRequests);
        Assert.NotEmpty(host.SourceRequests);
        Assert.IsType<
            AssemblyImageAccessResult<int>.Available>(
                group.UseAssemblySession(
                    assembly.Assembly,
                    static session =>
                        session.ApiSurface().Types.Count));
    }

    [Fact]
    public async Task MissingAuthoredSource_FallsBackToDecompiler()
    {
        TestAssembly assembly = TestAssembly.Create();
        AssemblyMemberSourceRequest request =
            assembly.MemberRequest(nameof(SourceFixture.Describe));
        using var host = QueryHost.WithoutPdb();
        using var workspace = new InspectionWorkspace();
        AssemblyContextGroup group =
            workspace.CreateAssemblyContextGroup(
                [assembly.Participant]);

        AssemblyMemberSourceEntry result =
            await AssemblyContextSourceQuery.ExecuteMemberAsync(
                group,
                assembly.Participant,
                request,
                host.Context,
                TestContext.Current.CancellationToken);

        var available =
            Assert.IsType<AssemblyMemberSourceEntry.Available>(
                result);
        var decompiled =
            Assert.IsType<AssemblyMemberSource.Decompiled>(
                available.Source);
        Assert.Contains(
            nameof(SourceFixture.Describe),
            decompiled.Text,
            StringComparison.Ordinal);
        Assert.IsType<FindingInspection<string>.Absent>(
            decompiled.AuthoredAttempt.Lines.Value);
        Assert.Empty(host.SourceRequests);
        Assert.True(assembly.Policy.SelectionCount > 0);
    }

    [Fact]
    public async Task AuthoredIntegrityFailure_IsPreservedBesideDecompiler()
    {
        TestAssembly assembly = TestAssembly.Create();
        AssemblyMemberSourceRequest request =
            assembly.MemberRequest(nameof(SourceFixture.Describe));
        using var host = QueryHost.WithPdb(
            assembly.PdbPath,
            "not the compiled source"u8.ToArray());
        using var workspace = new InspectionWorkspace();
        AssemblyContextGroup group =
            workspace.CreateAssemblyContextGroup(
                [assembly.Participant]);

        AssemblyMemberSourceEntry result =
            await AssemblyContextSourceQuery.ExecuteMemberAsync(
                group,
                assembly.Participant,
                request,
                host.Context,
                TestContext.Current.CancellationToken);

        var available =
            Assert.IsType<AssemblyMemberSourceEntry.Available>(
                result);
        var decompiled =
            Assert.IsType<AssemblyMemberSource.Decompiled>(
                available.Source);
        Assert.IsType<FindingInspection<string>.Failed>(
            decompiled.AuthoredAttempt.Lines.Value);
        Assert.Equal(
            SourceChecksumVerification.Mismatch,
            decompiled.AuthoredAttempt.ChecksumVerification);
        Assert.Contains(
            nameof(SourceFixture.Describe),
            decompiled.Text,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task PathlessType_AcquiresVerifiedAuthoredDocument()
    {
        TestAssembly assembly = TestAssembly.Create();
        AssemblyTypeSourceRequest request =
            assembly.TypeRequest(typeof(SourceFixture).Name);
        using var host = QueryHost.WithPdb(
            assembly.PdbPath,
            SourceFileBytes());
        using var workspace = new InspectionWorkspace();
        AssemblyContextGroup group =
            workspace.CreateAssemblyContextGroup(
                [assembly.Participant]);

        AssemblyTypeSourceEntry result =
            await AssemblyContextSourceQuery.ExecuteTypeAsync(
                group,
                assembly.Participant,
                request,
                host.Context,
                TestContext.Current.CancellationToken);

        var available =
            Assert.IsType<AssemblyTypeSourceEntry.Available>(
                result);
        var authored =
            Assert.IsType<AssemblyTypeSource.Authored>(
                available.Source);
        Assert.Contains(
            nameof(SourceFixture),
            authored.Text,
            StringComparison.Ordinal);
        Assert.Equal(
            SourceChecksumVerification.Exact,
            authored.Inspection.ChecksumVerification);
    }

    [Fact]
    public async Task MissingAuthoredType_FallsBackToDecompiler()
    {
        TestAssembly assembly = TestAssembly.Create();
        AssemblyTypeSourceRequest request =
            assembly.TypeRequest(typeof(SourceFixture).Name);
        using var host = QueryHost.WithoutPdb();
        using var workspace = new InspectionWorkspace();
        AssemblyContextGroup group =
            workspace.CreateAssemblyContextGroup(
                [assembly.Participant]);

        AssemblyTypeSourceEntry result =
            await AssemblyContextSourceQuery.ExecuteTypeAsync(
                group,
                assembly.Participant,
                request,
                host.Context,
                TestContext.Current.CancellationToken);

        var available =
            Assert.IsType<AssemblyTypeSourceEntry.Available>(
                result);
        var decompiled =
            Assert.IsType<AssemblyTypeSource.Decompiled>(
                available.Source);
        Assert.Contains(
            nameof(SourceFixture),
            decompiled.Text,
            StringComparison.Ordinal);
        Assert.True(decompiled.Decompilation.Succeeded);
        Assert.IsType<FindingInspection<string>.Absent>(
            decompiled.AuthoredAttempt.Lines.Value);
    }

    [Fact]
    public async Task NeitherSourceAvailable_ReturnsTypedFailure()
    {
        TestAssembly assembly = TestAssembly.Create();
        AssemblyTypeSourceRequest request =
            assembly.TypeRequest(typeof(SourceDelegate).Name);
        using var host = QueryHost.WithoutPdb();
        using var workspace = new InspectionWorkspace();
        AssemblyContextGroup group =
            workspace.CreateAssemblyContextGroup(
                [assembly.Participant]);

        AssemblyTypeSourceEntry result =
            await AssemblyContextSourceQuery.ExecuteTypeAsync(
                group,
                assembly.Participant,
                request,
                host.Context,
                TestContext.Current.CancellationToken);

        var unavailable =
            Assert.IsType<AssemblyTypeSourceEntry.Unavailable>(
                result);
        Assert.Equal(
            AssemblySourceFailureKind
                .AuthoredAndDecompiledUnavailable,
            unavailable.Failure.Kind);
        Assert.NotNull(unavailable.AuthoredAttempt);
        Assert.NotNull(unavailable.DecompiledAttempt);
        Assert.False(unavailable.DecompiledAttempt!.Succeeded);
    }

    [Fact]
    public async Task RejectedParticipant_ReturnsAcquisitionFailure()
    {
        TestAssembly assembly =
            TestAssembly.Create(selectedName: "Different.Identity");
        AssemblyMemberSourceRequest request =
            assembly.MemberRequest(nameof(SourceFixture.Describe));
        using var host = QueryHost.WithoutPdb();
        using var workspace = new InspectionWorkspace();
        AssemblyContextGroup group =
            workspace.CreateAssemblyContextGroup(
                [assembly.Participant]);

        AssemblyMemberSourceEntry result =
            await AssemblyContextSourceQuery.ExecuteMemberAsync(
                group,
                assembly.Participant,
                request,
                host.Context,
                TestContext.Current.CancellationToken);

        var rejected =
            Assert.IsType<AssemblyMemberSourceEntry.Rejected>(
                result);
        Assert.Equal(
            CandidateOpenFailureKind.InvalidImage,
            rejected.Failure.Kind);
    }

    static byte[] SourceFileBytes(
        [CallerFilePath] string path = "") =>
        File.ReadAllBytes(path);

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
            string? selectedName = null)
        {
            string path =
                typeof(AssemblyContextSourceQueryTests)
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

        internal AssemblyTypeSourceRequest TypeRequest(
            string typeName)
        {
            ApiType type = Assert.Single(
                _surface.Types,
                candidate =>
                    candidate.DefinitionName?.Segments[^1]
                    == typeName);
            return AssemblyTypeSourceRequest.From(type);
        }

        internal AssemblyMemberSourceRequest MemberRequest(
            string memberName)
        {
            ApiType type = Assert.Single(
                _surface.Types,
                candidate =>
                    candidate.DefinitionName?.Segments[^1]
                    == typeof(SourceFixture).Name);
            ApiMember member = Assert.Single(
                type.Members,
                candidate => candidate.Name == memberName);
            return AssemblyMemberSourceRequest.From(
                type,
                member);
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
            SourceHandler sourceHandler)
        {
            _symbolClient = new HttpClient(symbolHandler);
            _sourceClient = new HttpClient(sourceHandler);
            Context = new AssemblyContextSourceQueryContext(
                _symbolClient,
                new InMemoryPdbStore(),
                new UniformPackageSourceAuthorization(
                    [NuGetFetch.PackageSource.NuGetOrg]),
                new SourceFetcher(
                    _sourceClient,
                    new InMemorySourceContentStore()));
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
            byte[] sourceBytes)
        {
            Assert.True(
                File.Exists(pdbPath),
                $"Expected test PDB at {pdbPath}");
            return new QueryHost(
                new SymbolPackageHandler(
                    BuildSnupkg(
                        Path.GetFileName(pdbPath),
                        File.ReadAllBytes(pdbPath))),
                new SourceHandler(sourceBytes));
        }

        internal static QueryHost WithoutPdb()
            => new(
                new SymbolPackageHandler(snupkg: null),
                new SourceHandler(content: null));

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

    sealed class SymbolPackageHandler(byte[]? snupkg)
        : HttpMessageHandler
    {
        internal List<Uri> RequestUris { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestUris.Add(request.RequestUri!);
            if (snupkg is not null
                && request.RequestUri!.AbsolutePath.EndsWith(
                    ".snupkg",
                    StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(
                    new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new ByteArrayContent(snupkg),
                        RequestMessage = request,
                    });
            }

            return Task.FromResult(
                new HttpResponseMessage(
                    HttpStatusCode.NotFound)
                {
                    RequestMessage = request,
                });
        }
    }

    sealed class SourceHandler(byte[]? content)
        : HttpMessageHandler
    {
        internal List<Uri> RequestUris { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestUris.Add(request.RequestUri!);
            return Task.FromResult(
                new HttpResponseMessage(
                    content is null
                        ? HttpStatusCode.NotFound
                        : HttpStatusCode.OK)
                {
                    Content = content is null
                        ? null
                        : new ByteArrayContent(content),
                    RequestMessage = request,
                });
        }
    }

    sealed class FrameworkBindingPolicy
        : IAssemblyBindingPolicy
    {
        int _selectionCount;

        readonly ResolvedAssemblyReference _coreLibrary =
            ResolvedAssemblyReference.CreateFromPath(
                typeof(object).Assembly.Location,
                AssemblyResolutionProvenance.Platform(
                    "Microsoft.NETCore.App",
                    frameworkVersion: null,
                    "source query test"));

        public AssemblyBindingPolicyVersion Version { get; } =
            new();
        internal int SelectionCount =>
            Volatile.Read(ref _selectionCount);

        public AssemblyBindingSelection Select(
            AssemblyBindingRequest request)
        {
            Interlocked.Increment(ref _selectionCount);
            return request.Target
                is AssemblyBindingTarget.AssemblyReference reference
                && reference.Identity.Name
                    == _coreLibrary.Identity.Name
                    ? AssemblyBindingSelection.Found(
                        _coreLibrary)
                    : AssemblyBindingSelection.NotFound();
        }
    }

    public static class SourceFixture
    {
        public static string Describe(int value)
            => $"value={value}";
    }

    public delegate int SourceDelegate(int value);
}
