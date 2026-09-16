using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using DotnetInspector.Libraries;
using ILInspector.Metadata;
using Inspector.Artifacts;
using Inspector.Artifacts.Workspaces;

namespace DotnetInspector.LibraryMetadata.Tests;

public sealed class LibraryApiSurfaceInspectionTests
{
    private static readonly ApiSurfaceExtractionBounds s_bounds =
        new(
            maxTypes: 5_000,
            maxMembers: 100_000,
            maxInspectionFailures: 1_000,
            maxTypeForwarders: 10_000,
            maxMetadataRows: 1_000_000,
            maxRetainedTextCharacters: 20_000_000);

    [Fact]
    public async Task
        RealSystemTextJson_IssuesExactResourceFreeCorrespondence()
    {
        byte[] bytes = await RealSystemTextJsonAsync();
        await using ArtifactFixture artifacts =
            await ArtifactFixture.CreateAsync([bytes]);
        await using OwnedLibrary library =
            OwnedLibrary.Create(
                artifacts,
                artifactIndex: 0,
                Identity(bytes));
        using LibraryOperationLease operation = library.IssueOperation();

        LibraryApiSurfaceInspectionOutcome.Completed completed =
            Assert.IsType<LibraryApiSurfaceInspectionOutcome.Completed>(
                LibraryApiSurfaceInspection.Execute(
                    Request(library.Reference),
                    operation,
                    TestContext.Current.CancellationToken));
        LibraryApiSurfaceCorrespondence correspondence =
            completed.Correspondence;

        Assert.Same(library.Reference, correspondence.Library);
        Assert.Same(
            library.Reference.ApiAssembly,
            correspondence.ApiContent);
        Assert.Same(
            library.Reference.ApiAssembly.Generation,
            correspondence.Assembly.Registration.Generation);
        Assert.Same(
            library.Reference.ApiAssembly.Artifact,
            correspondence.Assembly.Registration.Artifact);
        Assert.NotEqual(
            Guid.Empty,
            correspondence.Assembly.Registration.ModuleVersionId);
        Assert.True(
            correspondence.Assembly.Identity.IsEquivalentTo(
                library.Reference.ApiAssembly.AssemblyIdentity!.Identity));
        Assert.Contains(
            correspondence.Surface.Types,
            type => type.FullName == "System.Text.Json.JsonSerializer");
        Assert.True(correspondence.MetadataRows > 0);
        Assert.True(correspondence.RetainedTextCharacters > 0);
        AssertResourceFree(typeof(LibraryApiSurfaceCorrespondence));
        Assert.Empty(
            typeof(LibraryApiSurfaceCorrespondence).GetConstructors(
                BindingFlags.Instance | BindingFlags.Public));
    }

    [Fact]
    public async Task
        EquivalentIdentityLibraries_RetainDistinctExactCorrespondence()
    {
        byte[] bytes = await RealSystemTextJsonAsync();
        ManagedMetadataIdentity.Assembly identity = Identity(bytes);
        await using ArtifactFixture artifacts =
            await ArtifactFixture.CreateAsync([bytes, bytes]);
        await using OwnedLibrary first =
            OwnedLibrary.Create(artifacts, artifactIndex: 0, identity);
        await using OwnedLibrary second =
            OwnedLibrary.Create(artifacts, artifactIndex: 1, identity);
        using LibraryOperationLease firstOperation =
            first.IssueOperation();
        using LibraryOperationLease secondOperation =
            second.IssueOperation();

        LibraryApiSurfaceCorrespondence firstCorrespondence =
            Completed(
                LibraryApiSurfaceInspection.Execute(
                    Request(first.Reference),
                    firstOperation,
                    TestContext.Current.CancellationToken));
        LibraryApiSurfaceCorrespondence secondCorrespondence =
            Completed(
                LibraryApiSurfaceInspection.Execute(
                    Request(second.Reference),
                    secondOperation,
                    TestContext.Current.CancellationToken));

        Assert.True(
            firstCorrespondence.Assembly.Identity.IsEquivalentTo(
                secondCorrespondence.Assembly.Identity));
        Assert.NotSame(first.Reference, second.Reference);
        Assert.NotSame(
            first.Reference.ApiAssembly,
            second.Reference.ApiAssembly);
        Assert.Same(
            first.Reference.ApiAssembly,
            firstCorrespondence.ApiContent);
        Assert.Same(
            second.Reference.ApiAssembly,
            secondCorrespondence.ApiContent);
        Assert.NotSame(
            firstCorrespondence.Assembly.Registration.Artifact,
            secondCorrespondence.Assembly.Registration.Artifact);
        Assert.NotSame(
            firstCorrespondence.Surface,
            secondCorrespondence.Surface);
    }

    [Fact]
    public async Task ForeignLibraryLease_IsRejectedBeforeContentAccess()
    {
        byte[] bytes = await RealSystemTextJsonAsync();
        ManagedMetadataIdentity.Assembly identity = Identity(bytes);
        await using ArtifactFixture artifacts =
            await ArtifactFixture.CreateAsync([bytes, bytes]);
        await using OwnedLibrary requested =
            OwnedLibrary.Create(artifacts, artifactIndex: 0, identity);
        await using OwnedLibrary foreign =
            OwnedLibrary.Create(artifacts, artifactIndex: 1, identity);
        using LibraryOperationLease foreignOperation =
            foreign.IssueOperation();

        LibraryApiSurfaceInspectionOutcome.Rejected rejected =
            Assert.IsType<LibraryApiSurfaceInspectionOutcome.Rejected>(
                LibraryApiSurfaceInspection.Execute(
                    Request(requested.Reference),
                    foreignOperation,
                    TestContext.Current.CancellationToken));

        Assert.Equal(
            LibraryApiSurfaceInspectionRejectionKind
                .LeaseReferenceMismatch,
            rejected.Kind);
    }

    [Fact]
    public async Task AssemblyIdentityMismatch_IsVisibleRejection()
    {
        byte[] bytes = await RealSystemTextJsonAsync();
        await using ArtifactFixture artifacts =
            await ArtifactFixture.CreateAsync([bytes]);
        var wrongIdentity = new ManagedMetadataIdentity.Assembly(
            new AssemblyReferenceIdentity(
                "System.Text.Json",
                new Version(99, 0, 0, 0),
                Culture: null,
                PublicKeyToken: null));
        await using OwnedLibrary library =
            OwnedLibrary.Create(
                artifacts,
                artifactIndex: 0,
                wrongIdentity);
        using LibraryOperationLease operation = library.IssueOperation();

        LibraryApiSurfaceInspectionOutcome.Rejected rejected =
            Assert.IsType<LibraryApiSurfaceInspectionOutcome.Rejected>(
                LibraryApiSurfaceInspection.Execute(
                    Request(library.Reference),
                    operation,
                    TestContext.Current.CancellationToken));

        Assert.Equal(
            LibraryApiSurfaceInspectionRejectionKind
                .AssemblyIdentityMismatch,
            rejected.Kind);
    }

    [Fact]
    public async Task ExtractionBound_IsVisibleIncompleteOutcome()
    {
        byte[] bytes = await RealSystemTextJsonAsync();
        await using ArtifactFixture artifacts =
            await ArtifactFixture.CreateAsync([bytes]);
        await using OwnedLibrary library =
            OwnedLibrary.Create(
                artifacts,
                artifactIndex: 0,
                Identity(bytes));
        using LibraryOperationLease operation = library.IssueOperation();
        var zeroTypes = new ApiSurfaceExtractionBounds(
            maxTypes: 0,
            maxMembers: int.MaxValue,
            maxInspectionFailures: int.MaxValue,
            maxTypeForwarders: int.MaxValue,
            maxMetadataRows: int.MaxValue,
            maxRetainedTextCharacters: int.MaxValue);

        LibraryApiSurfaceInspectionOutcome.Incomplete incomplete =
            Assert.IsType<LibraryApiSurfaceInspectionOutcome.Incomplete>(
                LibraryApiSurfaceInspection.Execute(
                    new LibraryApiSurfaceInspectionRequest(
                        library.Reference,
                        ApiSurfaceExtractionScope.Public,
                        zeroTypes),
                    operation,
                    TestContext.Current.CancellationToken));

        Assert.Equal(ApiSurfaceExtractionBound.Types, incomplete.Bound);
    }

    [Fact]
    public async Task MalformedMetadata_IsVisibleFailure()
    {
        byte[] realBytes = await RealSystemTextJsonAsync();
        await using ArtifactFixture artifacts =
            await ArtifactFixture.CreateAsync([[1, 2, 3]]);
        await using OwnedLibrary library =
            OwnedLibrary.Create(
                artifacts,
                artifactIndex: 0,
                Identity(realBytes));
        using LibraryOperationLease operation = library.IssueOperation();

        LibraryApiSurfaceInspectionOutcome.Failed failed =
            Assert.IsType<LibraryApiSurfaceInspectionOutcome.Failed>(
                LibraryApiSurfaceInspection.Execute(
                    Request(library.Reference),
                    operation,
                    TestContext.Current.CancellationToken));

        Assert.Equal(
            LibraryApiSurfaceInspectionFailureKind.MalformedMetadata,
            failed.Kind);
    }

    [Fact]
    public async Task CancellationBeforeBorrow_StopsInspection()
    {
        byte[] bytes = await RealSystemTextJsonAsync();
        await using ArtifactFixture artifacts =
            await ArtifactFixture.CreateAsync([bytes]);
        await using OwnedLibrary library =
            OwnedLibrary.Create(
                artifacts,
                artifactIndex: 0,
                Identity(bytes));
        using LibraryOperationLease operation = library.IssueOperation();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.Throws<OperationCanceledException>(
            () => LibraryApiSurfaceInspection.Execute(
                Request(library.Reference),
                operation,
                cancellation.Token));
    }

    private static LibraryApiSurfaceInspectionRequest Request(
        LibraryReference library) =>
        new(
            library,
            ApiSurfaceExtractionScope.Public,
            s_bounds);

    private static LibraryApiSurfaceCorrespondence Completed(
        LibraryApiSurfaceInspectionOutcome outcome) =>
        Assert.IsType<LibraryApiSurfaceInspectionOutcome.Completed>(
                outcome)
            .Correspondence;

    private static async Task<byte[]> RealSystemTextJsonAsync() =>
        await File.ReadAllBytesAsync(
            Path.Combine(
                AppContext.BaseDirectory,
                "RealAssets",
                "LibraryMetadata",
                "System.Text.Json.dll"),
            TestContext.Current.CancellationToken);

    private static ManagedMetadataIdentity.Assembly Identity(
        byte[] content)
    {
        using var peReader = new PEReader(
            new MemoryStream(content, writable: false));
        MetadataReader reader =
            MetadataFormatAdmission.GetMetadataReader(peReader);
        return new ManagedMetadataIdentity.Assembly(
            AssemblyReferenceIdentity.FromAssemblyDefinition(reader));
    }

    private static void AssertResourceFree(Type type)
    {
        Assert.False(typeof(IDisposable).IsAssignableFrom(type));
        Assert.False(typeof(IAsyncDisposable).IsAssignableFrom(type));
        Assert.DoesNotContain(
            type.GetFields(
                BindingFlags.Instance
                | BindingFlags.Public
                | BindingFlags.NonPublic),
            field =>
                typeof(IDisposable).IsAssignableFrom(field.FieldType)
                || typeof(IAsyncDisposable).IsAssignableFrom(
                    field.FieldType)
                || typeof(Delegate).IsAssignableFrom(field.FieldType)
                || typeof(Stream).IsAssignableFrom(field.FieldType));
    }

    private sealed class OwnedLibrary : IAsyncDisposable
    {
        private readonly LibraryContentOwner _owner;

        private OwnedLibrary(
            LibraryReference reference,
            LibraryContentOwner owner)
        {
            Reference = reference;
            _owner = owner;
        }

        public LibraryReference Reference { get; }

        public static OwnedLibrary Create(
            ArtifactFixture artifacts,
            int artifactIndex,
            ManagedMetadataIdentity.Assembly identity)
        {
            LibraryReference reference =
                LibraryReference.CreateDirect(
                    new LibraryAssemblyCorrespondence(
                        artifacts[artifactIndex],
                        identity,
                        artifacts[artifactIndex],
                        identity));
            return new OwnedLibrary(
                reference,
                new LibraryContentOwner(
                    reference,
                    [artifacts.IssueContentLease(artifactIndex)]));
        }

        public LibraryOperationLease IssueOperation() =>
            Assert.IsType<LibraryOperationLeaseIssueOutcome.Issued>(
                    _owner.IssueOperationLease(Reference))
                .Lease;

        public ValueTask DisposeAsync() => _owner.DisposeAsync();
    }

    private sealed class ArtifactFixture : IAsyncDisposable
    {
        private readonly ArtifactSetSession _session;
        private readonly ArtifactQueryLease _queryLease;
        private readonly IReadOnlyList<ArtifactContentReference> _references;

        private ArtifactFixture(
            ArtifactSetSession session,
            ArtifactQueryLease queryLease,
            IReadOnlyList<ArtifactContentReference> references)
        {
            _session = session;
            _queryLease = queryLease;
            _references = references;
        }

        public ArtifactContentReference this[int index] =>
            _references[index];

        public ArtifactContentLease IssueContentLease(int index) =>
            _session.IssueContentLease(
                _references[index],
                _queryLease);

        public static async Task<ArtifactFixture> CreateAsync(
            IReadOnlyList<byte[]> contents)
        {
            CancellationToken cancellationToken =
                TestContext.Current.CancellationToken;
            var session = new ArtifactSetSession();
            try
            {
                for (int index = 0; index < contents.Count; index++)
                {
                    int retainedIndex = index;
                    byte[] retainedContent = contents[index];
                    await session.AddRequiredAcquisitionAsync(
                        (scope, _) =>
                        {
                            ArtifactContribution contribution =
                                scope.Register(
                                    new Provenance(
                                        $"library-metadata-{retainedIndex}"),
                                    _ => new MemoryStream(
                                        retainedContent,
                                        writable: false));
                            return ValueTask.FromResult<
                                ArtifactAcquisitionOutcome>(
                                    new ArtifactAcquisitionOutcome.Acquired(
                                        [contribution],
                                        ArtifactAcquisitionLeases.None));
                        },
                        cancellationToken: cancellationToken);
                }

                Assert.IsType<ArtifactSetPublicationOutcome.Published>(
                    await session.SealAsync(cancellationToken));
                ArtifactQueryLease queryLease =
                    session.IssueLease(
                        session.CreateQueryAuthorization());
                IReadOnlyList<ArtifactContentReference> references =
                    session.GetCatalog(queryLease)
                        .Select(
                            descriptor =>
                                session.GetContentReference(
                                    descriptor.Identity,
                                    queryLease))
                        .ToArray();
                return new ArtifactFixture(
                    session,
                    queryLease,
                    references);
            }
            catch
            {
                await session.DisposeAsync();
                throw;
            }
        }

        public async ValueTask DisposeAsync()
        {
            _queryLease.Dispose();
            await _session.DisposeAsync();
        }
    }

    private sealed record Provenance(string Name) : IArtifactProvenance;
}
