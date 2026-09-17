using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

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
    private const int MaximumAvailablePayloadBytes = 1_100;
    private const int MaximumBoundedNonAvailablePayloadBytes = 1_024;
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
    public void PortableContract_IsDiscriminatedAndQueriesOwned()
    {
        var visited = new HashSet<Type>();
        (Type Type, string Discriminator, string[] Properties)[] expectedCases =
        [
            (
                typeof(CompiledDocumentationOutcome.Available),
                "available",
                [nameof(CompiledDocumentationOutcome.Available.Documentation),
                    nameof(CompiledDocumentationOutcome.Available.Source),
                    nameof(CompiledDocumentationOutcome.Subject)]),
            (
                typeof(CompiledDocumentationOutcome.Absent),
                "absent",
                [nameof(CompiledDocumentationOutcome.Absent.Sources),
                    nameof(CompiledDocumentationOutcome.Absent.SourcesTruncated),
                    nameof(CompiledDocumentationOutcome.Subject)]),
            (
                typeof(CompiledDocumentationOutcome.Unavailable),
                "unavailable",
                [nameof(CompiledDocumentationOutcome.Unavailable.Sources),
                    nameof(CompiledDocumentationOutcome.Unavailable.SourcesTruncated),
                    nameof(CompiledDocumentationOutcome.Subject)]),
            (
                typeof(CompiledDocumentationOutcome.Ambiguous),
                "ambiguous",
                [nameof(CompiledDocumentationOutcome.Ambiguous.Candidates),
                    nameof(CompiledDocumentationOutcome.Ambiguous.CandidatesTruncated),
                    nameof(CompiledDocumentationOutcome.Subject)]),
            (
                typeof(CompiledDocumentationOutcome.ContributionsRejected),
                "contributionsRejected",
                [nameof(CompiledDocumentationOutcome.ContributionsRejected.Rejections),
                    nameof(CompiledDocumentationOutcome.ContributionsRejected.RejectionsTruncated),
                    nameof(CompiledDocumentationOutcome.Subject)]),
            (
                typeof(CompiledDocumentationOutcome.ContributionFailed),
                "contributionFailed",
                [nameof(CompiledDocumentationOutcome.ContributionFailed.Reason),
                    nameof(CompiledDocumentationOutcome.ContributionFailed.Source),
                    nameof(CompiledDocumentationOutcome.Subject)]),
            (
                typeof(CompiledDocumentationOutcome.Incomplete),
                "incomplete",
                [nameof(CompiledDocumentationOutcome.Incomplete.Reason),
                    nameof(CompiledDocumentationOutcome.Incomplete.Sources),
                    nameof(CompiledDocumentationOutcome.Incomplete.SourcesTruncated),
                    nameof(CompiledDocumentationOutcome.Subject)]),
            (
                typeof(CompiledDocumentationOutcome.RequestRejected),
                "requestRejected",
                [nameof(CompiledDocumentationOutcome.RequestRejected.Reason),
                    nameof(CompiledDocumentationOutcome.Subject)]),
            (
                typeof(CompiledDocumentationOutcome.RequestFailed),
                "requestFailed",
                [nameof(CompiledDocumentationOutcome.RequestFailed.Reason),
                    nameof(CompiledDocumentationOutcome.Subject)]),
        ];

        JsonPolymorphicAttribute polymorphic =
            Assert.Single(
                typeof(CompiledDocumentationOutcome)
                    .GetCustomAttributes<JsonPolymorphicAttribute>());
        Assert.Equal("kind", polymorphic.TypeDiscriminatorPropertyName);
        Dictionary<Type, object> derivedTypes =
            typeof(CompiledDocumentationOutcome)
                .GetCustomAttributes<JsonDerivedTypeAttribute>()
                .ToDictionary(
                    static attribute => attribute.DerivedType,
                    static attribute => attribute.TypeDiscriminator!);
        Assert.Equal(expectedCases.Length, derivedTypes.Count);

        Visit(typeof(CompiledDocumentationOutcome));
        foreach ((Type type, string discriminator, string[] properties)
            in expectedCases)
        {
            Assert.Equal(discriminator, derivedTypes[type]);
            Assert.Equal(
                properties.Order(StringComparer.Ordinal),
                type.GetProperties(
                        BindingFlags.Public | BindingFlags.Instance)
                    .Select(static property => property.Name)
                    .Order(StringComparer.Ordinal));
            Visit(type);
        }

        void Visit(Type type)
        {
            if (!visited.Add(type)
                || type == typeof(string)
                || type.IsPrimitive)
            {
                return;
            }
            if (Nullable.GetUnderlyingType(type) is { } nullable)
            {
                Visit(nullable);
                return;
            }
            if (type.IsGenericType
                && type.GetGenericTypeDefinition()
                    == typeof(ImmutableArray<>))
            {
                Visit(type.GetGenericArguments()[0]);
                return;
            }

            Assert.Equal(
                typeof(CompiledDocumentationOutcome).Namespace,
                type.Namespace);
            if (!type.IsNested)
            {
                Assert.StartsWith(
                    "CompiledDocumentation",
                    type.Name,
                    StringComparison.Ordinal);
            }
            if (type.IsEnum)
                return;

            foreach (PropertyInfo property in type.GetProperties(
                BindingFlags.Public | BindingFlags.Instance))
            {
                Visit(property.PropertyType);
            }
        }
    }

    [Fact]
    public async Task
        RealSystemTextJsonMember_PublishesLeanAvailableOutcome()
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
        byte[] payload = JsonSerializer.SerializeToUtf8Bytes(
            result.Content,
            CompiledDocumentationQueryJsonContext
                .Default
                .CompiledDocumentationOutcome);
        CompiledDocumentationOutcome copy =
            JsonSerializer.Deserialize(
                payload,
                CompiledDocumentationQueryJsonContext
                    .Default
                    .CompiledDocumentationOutcome)!;
        Assert.Equal(
            payload,
            JsonSerializer.SerializeToUtf8Bytes(
                copy,
                CompiledDocumentationQueryJsonContext
                    .Default
                    .CompiledDocumentationOutcome));

        CompiledDocumentationOutcome.Available available =
            Assert.IsType<CompiledDocumentationOutcome.Available>(
                result.Content);
        Assert.Contains(
            "Converts the JsonDocument",
            available.Documentation.Summary,
            StringComparison.Ordinal);
        Assert.Equal(
            DeserializeIdentity,
            available.Subject.DocumentationId);
        Assert.Equal(
            "System.Text.Json",
            available.Subject.Assembly.Name);
        Assert.Equal(
            "direct-library:System.Text.Json",
            available.Source.Name);

        Assert.True(
            payload.Length <= MaximumAvailablePayloadBytes,
            $"Available payload was {payload.Length} UTF-8 bytes.");
        using JsonDocument document = JsonDocument.Parse(payload);
        JsonElement root = document.RootElement;
        Assert.Equal("available", root.GetProperty("kind").GetString());
        AssertPropertyNames(
            root,
            "kind",
            "subject",
            "source",
            "documentation");
        AssertPropertyNames(
            root.GetProperty("subject"),
            "assembly",
            "documentationId");
        AssertPropertyNames(
            root.GetProperty("source"),
            "kind",
            "name");
        Assert.DoesNotContain(
            "request",
            root.EnumerateObject()
                .Select(static property => property.Name));
        Assert.DoesNotContain(
            "work",
            root.EnumerateObject()
                .Select(static property => property.Name));
        Assert.DoesNotContain(
            "leaseConsumer",
            root.EnumerateObject()
                .Select(static property => property.Name));
    }

    [Fact]
    public async Task
        MillionsOfContributions_PublishBoundedIncompleteProvenance()
    {
        const int contributionCount = 4_000_000;
        const int distinctSourceCount = 9;
        byte[] xml = await File.ReadAllBytesAsync(
            RealAsset("System.Text.Json.xml"),
            TestContext.Current.CancellationToken);
        await using LibraryFixture library =
            await LibraryFixture.CreateAsync(xml);
        DocumentationSubjectReference subject = Subject(library);
        CompiledXmlContribution contribution =
            Assert.Single(
                DirectLibraryDocumentationHouseAdapter
                    .CreateCompiledXmlContributions(
                        library.Reference,
                        subject));
        CompiledXmlContribution[] distinct =
            Enumerable.Range(0, distinctSourceCount)
                .Select(
                    index =>
                        CompiledXmlContribution.Candidate(
                            subject,
                            contribution.Library,
                            contribution.ApiContent,
                            DocumentationSourceReference.Create(
                                DocumentationSourceKind.DirectLibrary,
                                $"deadline-source-{index}"),
                            contribution.CompiledXmlContent!,
                            contribution.Precedence))
                .ToArray();
        var contributions =
            new CompiledXmlContribution[contributionCount];
        for (int index = 0; index < contributions.Length; index++)
            contributions[index] = distinct[index % distinct.Length];

        DocumentationHouseRequest request = Request(
            subject,
            contributions,
            maximumContributions: distinctSourceCount);

        CompiledDocumentationQueryResult result =
            await CompiledDocumentationQuery.ExecuteAsync(
                request,
                library.IssueOperation(),
                TestContext.Current.CancellationToken);

        CompiledDocumentationOutcome.Incomplete incomplete =
            Assert.IsType<CompiledDocumentationOutcome.Incomplete>(
                result.Content);
        Assert.Equal(
            CompiledDocumentationIncompleteReason.ContributionLimit,
            incomplete.Reason);
        Assert.Equal(8, incomplete.Sources.Length);
        Assert.True(incomplete.SourcesTruncated);
        Assert.Equal(
            "deadline-source-0",
            incomplete.Sources[0].Source.Name);
        Assert.Equal(
            CompiledDocumentationSourceEvidenceKind.Candidate,
            incomplete.Sources[0].Kind);

        await library.RetireAsync();
        byte[] payload = JsonSerializer.SerializeToUtf8Bytes(
            result.Content,
            CompiledDocumentationQueryJsonContext
                .Default
                .CompiledDocumentationOutcome);
        CompiledDocumentationOutcome copy =
            JsonSerializer.Deserialize(
                payload,
                CompiledDocumentationQueryJsonContext
                    .Default
                    .CompiledDocumentationOutcome)!;
        CompiledDocumentationOutcome.Incomplete copied =
            Assert.IsType<CompiledDocumentationOutcome.Incomplete>(copy);
        Assert.Equal(8, copied.Sources.Length);
        Assert.True(copied.SourcesTruncated);
        Assert.True(
            payload.Length <= MaximumBoundedNonAvailablePayloadBytes,
            $"Incomplete payload was {payload.Length} UTF-8 bytes.");

        using JsonDocument document = JsonDocument.Parse(payload);
        AssertPropertyNames(
            document.RootElement,
            "kind",
            "subject",
            "reason",
            "sources",
            "sourcesTruncated");
        Assert.Equal(
            "incomplete",
            document.RootElement.GetProperty("kind").GetString());
    }

    [Fact]
    public async Task
        UnavailableAndRejectedContributionsHaveDistinctWireCases()
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

        CompiledDocumentationOutcome.Unavailable unavailableContent =
            Assert.IsType<CompiledDocumentationOutcome.Unavailable>(
                unavailable.Content);
        CompiledDocumentationSourceEvidence unavailableSource =
            Assert.Single(
                unavailableContent.Sources);
        Assert.Equal(
            CompiledDocumentationSourceEvidenceKind.Unavailable,
            unavailableSource.Kind);
        Assert.False(unavailableContent.SourcesTruncated);

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

        CompiledDocumentationOutcome.ContributionsRejected rejectedContent =
            Assert.IsType<
                CompiledDocumentationOutcome.ContributionsRejected>(
                    rejected.Content);
        CompiledDocumentationSourceRejection rejection =
            Assert.Single(
                rejectedContent.Rejections);
        Assert.Equal(
            CompiledDocumentationSourceRejectionKind.SubjectMismatch,
            rejection.Reason);
        Assert.Equal(
            "direct-library:System.Text.Json",
            rejection.Source.Name);
        Assert.False(rejectedContent.RejectionsTruncated);

        using JsonDocument unavailableJson =
            JsonDocument.Parse(Serialize(unavailable.Content));
        AssertPropertyNames(
            unavailableJson.RootElement,
            "kind",
            "subject",
            "sources");
        using JsonDocument rejectedJson =
            JsonDocument.Parse(Serialize(rejected.Content));
        AssertPropertyNames(
            rejectedJson.RootElement,
            "kind",
            "subject",
            "rejections");
    }

    [Fact]
    public async Task AbsentAndMalformedContentHaveDistinctWireCases()
    {
        byte[] absentXml = Encoding.UTF8.GetBytes(
            """
            <doc><members>
              <member name="T:System.Text.Json.JsonSerializer">
                <summary>type documentation</summary>
              </member>
            </members></doc>
            """);
        await using LibraryFixture absentLibrary =
            await LibraryFixture.CreateAsync(absentXml);
        DocumentationSubjectReference absentSubject =
            Subject(absentLibrary);
        CompiledDocumentationQueryResult absent =
            await CompiledDocumentationQuery.ExecuteAsync(
                Request(
                    absentSubject,
                    DirectLibraryDocumentationHouseAdapter
                        .CreateCompiledXmlContributions(
                            absentLibrary.Reference,
                            absentSubject)),
                absentLibrary.IssueOperation(),
                TestContext.Current.CancellationToken);

        CompiledDocumentationOutcome.Absent absentContent =
            Assert.IsType<CompiledDocumentationOutcome.Absent>(
                absent.Content);
        CompiledDocumentationSourceEvidence absentEvidence =
            Assert.Single(absentContent.Sources);
        Assert.Equal(
            CompiledDocumentationSourceEvidenceKind.Candidate,
            absentEvidence.Kind);
        Assert.Equal(
            "direct-library:System.Text.Json",
            absentEvidence.Source.Name);
        Assert.False(absentContent.SourcesTruncated);
        using JsonDocument absentJson =
            JsonDocument.Parse(Serialize(absent.Content));
        AssertPropertyNames(
            absentJson.RootElement,
            "kind",
            "subject",
            "sources");

        await using LibraryFixture malformedLibrary =
            await LibraryFixture.CreateAsync(
                Encoding.UTF8.GetBytes("<doc><members>"));
        DocumentationSubjectReference malformedSubject =
            Subject(malformedLibrary);
        CompiledDocumentationQueryResult malformed =
            await CompiledDocumentationQuery.ExecuteAsync(
                Request(
                    malformedSubject,
                    DirectLibraryDocumentationHouseAdapter
                        .CreateCompiledXmlContributions(
                            malformedLibrary.Reference,
                            malformedSubject)),
                malformedLibrary.IssueOperation(),
                TestContext.Current.CancellationToken);

        CompiledDocumentationOutcome.ContributionFailed failed =
            Assert.IsType<
                CompiledDocumentationOutcome.ContributionFailed>(
                    malformed.Content);
        Assert.Equal(
            CompiledDocumentationFailureKind.MalformedOrUnreadableDocument,
            failed.Reason);
        using JsonDocument failedJson =
            JsonDocument.Parse(Serialize(malformed.Content));
        AssertPropertyNames(
            failedJson.RootElement,
            "kind",
            "subject",
            "source",
            "reason");
    }

    [Fact]
    public async Task
        AuthoritativePackageAbsencePreservesSourceProvenance()
    {
        await using LibraryFixture library =
            await LibraryFixture.CreateAsync();
        DocumentationSubjectReference subject = Subject(library);
        CompiledXmlContribution absentContribution =
            CompiledXmlContribution.Absent(
                subject,
                library.Reference,
                library.Reference.ApiAssembly,
                DocumentationSourceReference.Create(
                    DocumentationSourceKind.Package,
                    "package:System.Text.Json@10.0.0"));

        CompiledDocumentationQueryResult result =
            await CompiledDocumentationQuery.ExecuteAsync(
                Request(subject, [absentContribution]),
                library.IssueOperation(),
                TestContext.Current.CancellationToken);

        DocumentationHouseOutcome.Completed completed =
            Assert.IsType<DocumentationHouseOutcome.Completed>(
                result.Outcome);
        DocumentationCompiledXmlAttempt.Absent houseAbsent =
            Assert.IsType<DocumentationCompiledXmlAttempt.Absent>(
                completed.CompiledXmlAttempt);
        Assert.Null(houseAbsent.Selected);

        CompiledDocumentationOutcome.Absent content =
            Assert.IsType<CompiledDocumentationOutcome.Absent>(
                result.Content);
        CompiledDocumentationSourceEvidence evidence =
            Assert.Single(content.Sources);
        Assert.Equal(
            CompiledDocumentationSourceEvidenceKind.Absent,
            evidence.Kind);
        Assert.Equal(
            CompiledDocumentationSourceKind.Package,
            evidence.Source.Kind);
        Assert.Equal(
            "package:System.Text.Json@10.0.0",
            evidence.Source.Name);
        Assert.False(content.SourcesTruncated);

        await library.RetireAsync();
        byte[] payload = Serialize(result.Content);
        Assert.True(
            payload.Length <= MaximumBoundedNonAvailablePayloadBytes,
            $"Absent payload was {payload.Length} UTF-8 bytes.");
        using JsonDocument document = JsonDocument.Parse(payload);
        AssertPropertyNames(
            document.RootElement,
            "kind",
            "subject",
            "sources");
        JsonElement sourceEvidence =
            Assert.Single(
                document.RootElement
                    .GetProperty("sources")
                    .EnumerateArray());
        AssertPropertyNames(sourceEvidence, "kind", "source");
        AssertPropertyNames(
            sourceEvidence.GetProperty("source"),
            "kind",
            "name");
    }

    [Fact]
    public async Task ForeignLease_PublishesRequestRejectedWireCase()
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
        CompiledDocumentationOutcome.RequestRejected rejected =
            Assert.IsType<
                CompiledDocumentationOutcome.RequestRejected>(
                    result.Content);
        Assert.Equal(
            CompiledDocumentationRequestRejectionKind.LeaseReferenceMismatch,
            rejected.Reason);
        using JsonDocument document =
            JsonDocument.Parse(Serialize(result.Content));
        AssertPropertyNames(
            document.RootElement,
            "kind",
            "subject",
            "reason");
    }

    private static byte[] Serialize(CompiledDocumentationOutcome content) =>
        JsonSerializer.SerializeToUtf8Bytes(
            content,
            CompiledDocumentationQueryJsonContext
                .Default
                .CompiledDocumentationOutcome);

    private static void AssertPropertyNames(
        JsonElement value,
        params string[] expected) =>
        Assert.Equal(
            expected.Order(StringComparer.Ordinal),
            value.EnumerateObject()
                .Select(static property => property.Name)
                .Order(StringComparer.Ordinal));

    private static DocumentationHouseRequest Request(
        DocumentationSubjectReference subject,
        IReadOnlyList<CompiledXmlContribution> contributions,
        int maximumContributions = 8,
        DateTimeOffset? deadline = null)
    {
        var limits = new DocumentationHouseLimits(
            maximumContributions,
            maximumCompiledXmlBytes: 8 * 1024 * 1024,
            XmlDocumentationReadLimits.Default);
        var plan = new DocumentationHouseOperationPlan(
            DocumentationHouseOperationPlanIdentity.Create(
                "compiled-plan"),
            DocumentationHousePolicyGeneration.Create("policy-1"),
            limits,
            deadline ?? DateTimeOffset.UtcNow.AddMinutes(1),
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
