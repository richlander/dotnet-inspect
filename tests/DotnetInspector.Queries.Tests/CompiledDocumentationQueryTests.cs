using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text.Json;

using CSharpText;
using DotnetInspector.DocumentationHouse;
using DotnetInspector.DocumentationHouse.Direct;
using DotnetInspector.Libraries;
using DotnetInspector.LibraryMetadata;
using ILInspector.Metadata;
using Inspector.Artifacts;
using Inspector.Artifacts.Workspaces;

namespace DotnetInspector.Queries.Tests;

public sealed class CompiledDocumentationQueryTests
{
    private const string DeserializeIdentity =
        "M:System.Text.Json.JsonSerializer.Deserialize``1(System.Text.Json.JsonDocument,System.Text.Json.JsonSerializerOptions)";
    private static readonly ApiSurfaceExtractionBounds s_apiSurfaceBounds =
        new(
            maxTypes: 5_000,
            maxMembers: 100_000,
            maxInspectionFailures: 1_000,
            maxTypeForwarders: 10_000,
            maxMetadataRows: 1_000_000,
            maxRetainedTextCharacters: 20_000_000);
    private static readonly Lazy<byte[]> s_realAssembly =
        new(() => File.ReadAllBytes(RealAsset("System.Text.Json.dll")));

    [Fact]
    public async Task
        RealSystemTextJsonMember_PublishesOwnedSerializableSnapshot()
    {
        byte[] xml = await File.ReadAllBytesAsync(
            RealAsset("System.Text.Json.xml"),
            TestContext.Current.CancellationToken);
        await using LibraryFixture library =
            await LibraryFixture.CreateAsync(xml);
        DocumentationSubjectReference subject = Subject(library);
        IReadOnlyList<CompiledXmlContribution> contributions =
            DirectLibraryDocumentationHouseAdapter
                .CreateCompiledXmlContributions(
                    library.Reference,
                    subject);
        DocumentationHouseRequest request =
            Request(subject, contributions);
        LibraryOperationLease operation = library.IssueOperation();

        CompiledDocumentationQueryResult result =
            await CompiledDocumentationQuery.ExecuteAsync(
                request,
                operation,
                TestContext.Current.CancellationToken);

        DocumentationHouseOutcome.Completed completed =
            Assert.IsType<DocumentationHouseOutcome.Completed>(
                result.Outcome);
        Assert.IsType<DocumentationCompiledXmlAttempt.Available>(
            completed.CompiledXmlAttempt);
        Assert.Throws<ObjectDisposedException>(
            () => operation.Snapshot(
                library.Reference.ApiAssembly,
                static (_, _) => true,
                TestContext.Current.CancellationToken));

        await library.RetireAsync();
        Assert.Null(
            CompiledDocumentationQueryJsonContext.Default.GetTypeInfo(
                typeof(DocumentationHouseOutcome)));
        Assert.Null(
            CompiledDocumentationQueryJsonContext.Default.GetTypeInfo(
                typeof(CompiledDocumentationQueryResult)));
        string json = JsonSerializer.Serialize(
            result.Snapshot,
            CompiledDocumentationQueryJsonContext
                .Default
                .CompiledDocumentationQuerySnapshot);
        CompiledDocumentationQuerySnapshot copy =
            JsonSerializer.Deserialize(
                json,
                CompiledDocumentationQueryJsonContext
                    .Default
                    .CompiledDocumentationQuerySnapshot)!;
        Assert.Equal(
            json,
            JsonSerializer.Serialize(
                copy,
                CompiledDocumentationQueryJsonContext
                    .Default
                    .CompiledDocumentationQuerySnapshot));

        CompiledDocumentationQuerySnapshot snapshot = result.Snapshot;
        Assert.Equal(
            CompiledDocumentationQueryOutcomeKind.Completed,
            snapshot.Outcome);
        Assert.Equal(
            DocumentationCompiledXmlAttemptKind.Available,
            snapshot.CompiledXml!.Kind);
        Assert.Contains(
            "Converts the JsonDocument",
            snapshot.CompiledXml.Documentation!.Summary,
            StringComparison.Ordinal);
        Assert.Equal(
            DeserializeIdentity,
            snapshot.Subject.CompiledXmlIdentity);
        Assert.Equal(
            "System.Text.Json",
            snapshot.Subject.Assembly.Name);
        Assert.Equal(
            "System.Text.Json",
            snapshot.Subject.Type.Namespace);
        Assert.Equal(
            ["JsonSerializer"],
            snapshot.Subject.Type.Segments);
        Assert.NotNull(snapshot.Subject.Member);
        Assert.Equal(
            "direct-library:System.Text.Json",
            snapshot.CompiledXml.Selected!.Source);
        Assert.True(snapshot.Work.ParsedCompiledXml);
        Assert.Equal(
            DocumentationLibraryLeaseConsumer.DocumentationHouse,
            snapshot.LeaseConsumer);

        using JsonDocument document = JsonDocument.Parse(json);
        Assert.Equal(
            "Available",
            document.RootElement
                .GetProperty("compiledXml")
                .GetProperty("kind")
                .GetString());
        Assert.False(
            json.Contains(
                "LibraryReference",
                StringComparison.Ordinal));
        Assert.False(
            json.Contains(
                "ArtifactContentReference",
                StringComparison.Ordinal));
    }

    [Fact]
    public async Task
        UnavailableAndRejectedAttemptsRemainVisibleInSnapshot()
    {
        await using LibraryFixture selected =
            await LibraryFixture.CreateAsync();
        DocumentationSubjectReference selectedSubject =
            Subject(selected);
        CompiledDocumentationQueryResult unavailable =
            await CompiledDocumentationQuery.ExecuteAsync(
                Request(
                    selectedSubject,
                    DirectLibraryDocumentationHouseAdapter
                        .CreateCompiledXmlContributions(
                            selected.Reference,
                            selectedSubject)),
                selected.IssueOperation(),
                TestContext.Current.CancellationToken);

        Assert.Equal(
            DocumentationCompiledXmlAttemptKind.Unavailable,
            unavailable.Snapshot.CompiledXml!.Kind);
        Assert.Null(
            unavailable.Snapshot.CompiledXml.Documentation);
        CompiledDocumentationContributionSnapshot unavailableContribution =
            Assert.Single(
                unavailable.Snapshot.CompiledXml.Contributions);
        Assert.Equal(
            CompiledXmlContributionKind.Unavailable,
            unavailableContribution.Kind);

        await using LibraryFixture foreign =
            await LibraryFixture.CreateAsync(
                await File.ReadAllBytesAsync(
                    RealAsset("System.Text.Json.xml"),
                    TestContext.Current.CancellationToken));
        DocumentationSubjectReference foreignSubject = Subject(foreign);
        IReadOnlyList<CompiledXmlContribution> foreignContributions =
            DirectLibraryDocumentationHouseAdapter
                .CreateCompiledXmlContributions(
                    foreign.Reference,
                    foreignSubject);
        CompiledDocumentationQueryResult rejected =
            await CompiledDocumentationQuery.ExecuteAsync(
                Request(
                    selectedSubject,
                    foreignContributions),
                selected.IssueOperation(),
                TestContext.Current.CancellationToken);

        Assert.Equal(
            DocumentationCompiledXmlAttemptKind.Rejected,
            rejected.Snapshot.CompiledXml!.Kind);
        CompiledDocumentationRejectionSnapshot rejection =
            Assert.Single(
                rejected.Snapshot.CompiledXml.Rejections);
        Assert.Equal(
            DocumentationCompiledXmlRejectionKind.SubjectMismatch,
            rejection.Kind);
        Assert.Equal(
            "direct-library:System.Text.Json",
            rejection.Contribution.Source);
        Assert.False(rejected.Snapshot.Work.ParsedCompiledXml);
    }

    [Fact]
    public async Task ForeignLease_PublishesTopLevelRejectionSnapshot()
    {
        await using LibraryFixture selected =
            await LibraryFixture.CreateAsync();
        await using LibraryFixture foreign =
            await LibraryFixture.CreateAsync();
        DocumentationSubjectReference subject = Subject(selected);

        CompiledDocumentationQueryResult result =
            await CompiledDocumentationQuery.ExecuteAsync(
                Request(subject, []),
                foreign.IssueOperation(),
                TestContext.Current.CancellationToken);

        Assert.IsType<DocumentationHouseOutcome.Rejected>(
            result.Outcome);
        Assert.Equal(
            CompiledDocumentationQueryOutcomeKind.Rejected,
            result.Snapshot.Outcome);
        Assert.Equal(
            DocumentationHouseRejectionKind.LeaseReferenceMismatch,
            result.Snapshot.Rejection);
        Assert.Null(result.Snapshot.CompiledXml);
    }

    private static DocumentationHouseRequest Request(
        DocumentationSubjectReference subject,
        IReadOnlyList<CompiledXmlContribution> contributions)
    {
        var limits = new DocumentationHouseLimits(
            maximumCompiledXmlContributions: 8,
            maximumCompiledXmlBytes: 8 * 1024 * 1024,
            XmlDocumentationReadLimits.Default);
        var plan = new DocumentationHouseOperationPlan(
            DocumentationHouseOperationPlanIdentity.Create(
                "compiled-plan"),
            DocumentationHousePolicyGeneration.Create("policy-1"),
            limits,
            DateTimeOffset.UtcNow.AddMinutes(1),
            contributions);
        return new DocumentationHouseRequest(
            DocumentationHouseRequestIdentity.Create(
                "compiled-request"),
            subject,
            DocumentationDemand.CompiledXml,
            plan);
    }

    private static DocumentationSubjectReference Subject(
        LibraryFixture library)
    {
        LibraryApiSurfaceCorrespondence correspondence =
            library.ApiSurfaceCorrespondence;
        ApiType type = Assert.Single(
            correspondence.Surface.Types,
            candidate =>
                candidate.FullName
                    == "System.Text.Json.JsonSerializer");
        ApiMember member = Assert.Single(
            type.Members,
            candidate =>
                ApiMemberIdentity.TryGetXmlDocMemberIdentity(
                    type,
                    candidate,
                    out XmlDocMemberIdentity identity)
                && identity.Value == DeserializeIdentity);
        return DocumentationSubjectReference.ForMember(
            correspondence,
            type,
            member);
    }

    private static string RealAsset(string fileName) =>
        Path.Combine(
            AppContext.BaseDirectory,
            "RealAssets",
            "DocumentationQuery",
            fileName);

    private static ManagedMetadataIdentity.Assembly AssemblyIdentity(
        byte[] content)
    {
        using var peReader = new PEReader(
            new MemoryStream(content, writable: false));
        MetadataReader reader =
            MetadataFormatAdmission.GetMetadataReader(peReader);
        return new ManagedMetadataIdentity.Assembly(
            AssemblyReferenceIdentity.FromAssemblyDefinition(reader));
    }

    private sealed class LibraryFixture : IAsyncDisposable
    {
        private readonly ArtifactFixture _artifacts;
        private readonly LibraryContentOwner _owner;
        private LibraryApiSurfaceCorrespondence? _apiSurfaceCorrespondence;

        private LibraryFixture(
            ArtifactFixture artifacts,
            LibraryReference reference,
            LibraryContentOwner owner)
        {
            _artifacts = artifacts;
            Reference = reference;
            _owner = owner;
        }

        public LibraryReference Reference { get; }

        public LibraryApiSurfaceCorrespondence ApiSurfaceCorrespondence =>
            _apiSurfaceCorrespondence ??= InspectApiSurface();

        public LibraryOperationLease IssueOperation() =>
            Assert.IsType<
                LibraryOperationLeaseIssueOutcome.Issued>(
                    _owner.IssueOperationLease(Reference))
                .Lease;

        public Task RetireAsync() =>
            _owner.DisposeAsync().AsTask();

        public static async Task<LibraryFixture> CreateAsync(
            params byte[][] compiledXml)
        {
            byte[] assembly = s_realAssembly.Value;
            byte[][] contents = [assembly, .. compiledXml];
            ArtifactFixture artifacts =
                await ArtifactFixture.CreateAsync(contents);
            try
            {
                ManagedMetadataIdentity.Assembly identity =
                    AssemblyIdentity(assembly);
                LibraryReference reference =
                    LibraryReference.CreateDirect(
                        new LibraryAssemblyCorrespondence(
                            artifacts[0],
                            identity,
                            artifacts[0],
                            identity),
                        Enumerable.Range(0, compiledXml.Length)
                            .Select(
                                index =>
                                    new LibraryCompanionCorrespondence(
                                        artifacts[index + 1],
                                        LibraryContentRole
                                            .CompiledXmlDocumentation,
                                        artifacts[0])));
                var owner = new LibraryContentOwner(
                    reference,
                    artifacts.IssueContentLeases());
                return new(
                    artifacts,
                    reference,
                    owner);
            }
            catch
            {
                await artifacts.DisposeAsync();
                throw;
            }
        }

        private LibraryApiSurfaceCorrespondence InspectApiSurface()
        {
            using LibraryOperationLease operation = IssueOperation();
            var request = new LibraryApiSurfaceInspectionRequest(
                Reference,
                ApiSurfaceExtractionScope.Public,
                s_apiSurfaceBounds);
            return Assert.IsType<
                    LibraryApiSurfaceInspectionOutcome.Completed>(
                    LibraryApiSurfaceInspection.Execute(
                        request,
                        operation,
                        TestContext.Current.CancellationToken))
                .Correspondence;
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                await _owner.DisposeAsync();
            }
            finally
            {
                await _artifacts.DisposeAsync();
            }
        }
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

        public ArtifactContentLease[] IssueContentLeases() =>
            _references
                .Select(
                    reference =>
                        _session.IssueContentLease(
                            reference,
                            _queryLease))
                .ToArray();

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
                                        $"documentation-{retainedIndex}"),
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

                Assert.IsType<
                    ArtifactSetPublicationOutcome.Published>(
                        await session.SealAsync(
                            cancellationToken));
                ArtifactQueryAuthorization authorization =
                    session.CreateQueryAuthorization();
                ArtifactQueryLease queryLease =
                    session.IssueLease(authorization);
                IReadOnlyList<ArtifactContentReference> references =
                    session.GetCatalog(queryLease)
                        .Select(
                            descriptor =>
                                session.GetContentReference(
                                    descriptor.Identity,
                                    queryLease))
                        .ToArray();
                return new(
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

    private sealed record Provenance(string Name) :
        IArtifactProvenance;
}
