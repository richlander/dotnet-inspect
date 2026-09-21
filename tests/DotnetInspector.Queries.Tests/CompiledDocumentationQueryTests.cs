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

public sealed partial class CompiledDocumentationQueryTests
{
    private const int MaximumAvailableJsonCodeUnits = 1_100;
    private const int MaximumBoundedNonAvailableJsonCodeUnits = 1_024;
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
                typeof(
                    CompiledDocumentationOutcome
                        .MalformedOrUnreadableDocument),
                "malformedOrUnreadableDocument",
                [nameof(
                    CompiledDocumentationOutcome
                        .MalformedOrUnreadableDocument.Source),
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
                typeof(CompiledDocumentationOutcome.ContentAccessFailed),
                "contentAccessFailed",
                [nameof(CompiledDocumentationOutcome.ContentAccessFailed.Source),
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
        string json = JsonSerializer.Serialize(
            result.Content,
            CompiledDocumentationQueryJsonContext
                .Default
                .CompiledDocumentationOutcome);
        CompiledDocumentationOutcome copy =
            JsonSerializer.Deserialize(
                json,
                CompiledDocumentationQueryJsonContext
                    .Default
                    .CompiledDocumentationOutcome)!;
        Assert.Equal(
            json,
            JsonSerializer.Serialize(
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
            json.Length <= MaximumAvailableJsonCodeUnits,
            $"Available JSON was {json.Length} UTF-16 code units.");
        using JsonDocument document = JsonDocument.Parse(json);
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
        ExecuteMany_TypeAndMemberPublishIndependentAvailableOutcomes()
    {
        byte[] xml = await File.ReadAllBytesAsync(
            RealAsset("System.Text.Json.xml"),
            TestContext.Current.CancellationToken);
        await using LibraryFixture library =
            await LibraryFixture.CreateAsync(xml);
        ApiType type = Assert.Single(
            library.ApiSurfaceCorrespondence.Surface.Types,
            candidate =>
                candidate.FullName
                    == "System.Text.Json.JsonSerializer");
        DocumentationSubjectReference typeSubject =
            DocumentationSubjectReference.ForType(
                library.ApiSurfaceCorrespondence,
                type);
        DocumentationSubjectReference memberSubject =
            Subject(library);
        DocumentationHouseRequest[] requests =
        [
            Request(
                typeSubject,
                DirectLibraryDocumentationHouseAdapter
                    .CreateCompiledXmlContributions(
                        library.Reference,
                        typeSubject)),
            Request(
                memberSubject,
                DirectLibraryDocumentationHouseAdapter
                    .CreateCompiledXmlContributions(
                        library.Reference,
                        memberSubject)),
        ];

        IReadOnlyList<CompiledDocumentationQueryResult> results =
            await CompiledDocumentationQuery.ExecuteManyAsync(
                requests,
                library.IssueOperation(),
                TestContext.Current.CancellationToken);

        Assert.Collection(
            results,
            result =>
            {
                var available =
                    Assert.IsType<
                        CompiledDocumentationOutcome.Available>(
                            result.Content);
                Assert.Equal(
                    "T:System.Text.Json.JsonSerializer",
                    available.Subject.DocumentationId);
                Assert.NotNull(available.Documentation.Summary);
            },
            result =>
            {
                var available =
                    Assert.IsType<
                        CompiledDocumentationOutcome.Available>(
                            result.Content);
                Assert.Equal(
                    DeserializeIdentity,
                    available.Subject.DocumentationId);
                Assert.NotNull(available.Documentation.Summary);
            });
        Assert.Equal(
            1,
            results.Count(
                result => result.Outcome.Work.ParsedCompiledXml));
        Assert.Single(
            results,
            result =>
                result.Outcome.Work.CompiledXmlBytesObserved > 0);
    }

    [Fact]
    public async Task
        SubjectResolver_AvailableModeOmitsMissingReferenceSubjects()
    {
        await using LibraryFixture library =
            await LibraryFixture.CreateAsync();
        string missingIdentity =
            "M:System.Text.Json.JsonSerializer.ImplementationOnly";
        string[] documentationIds =
        [
            DeserializeIdentity,
            missingIdentity,
        ];

        InvalidOperationException strictFailure = Assert.Throws<
            InvalidOperationException>(
                () => CompiledDocumentationSubjectResolver.Resolve(
                    library.Reference,
                    library.Owner,
                    documentationIds,
                    ApiSurfaceExtractionScope.Public,
                    s_apiSurfaceBounds,
                    TestContext.Current.CancellationToken));
        Assert.Contains(
            missingIdentity,
            strictFailure.Message,
            StringComparison.Ordinal);

        IReadOnlyDictionary<string, DocumentationSubjectReference>
            available = CompiledDocumentationSubjectResolver.ResolveAvailable(
                library.Reference,
                library.Owner,
                documentationIds,
                ApiSurfaceExtractionScope.Public,
                s_apiSurfaceBounds,
                TestContext.Current.CancellationToken);

        KeyValuePair<string, DocumentationSubjectReference> resolved =
            Assert.Single(available);
        Assert.Equal(DeserializeIdentity, resolved.Key);
        Assert.Equal(
            DeserializeIdentity,
            resolved.Value.CompiledXmlIdentity.Value);
    }

    [Fact]
    public async Task
        ExecuteMany_SamePolicyRetainedTextBudgetsMatchStandaloneOutcomes()
    {
        const string typeIdentity =
            "T:System.Text.Json.JsonSerializer";
        const string typeSummary = "Type documentation.";
        const string memberSummary = "Member documentation.";
        byte[] xml = Encoding.UTF8.GetBytes($"""
            <doc>
              <members>
                <member name="{typeIdentity}">
                  <summary>{typeSummary}</summary>
                </member>
                <member name="{DeserializeIdentity}">
                  <summary>{memberSummary}</summary>
                </member>
              </members>
            </doc>
            """);
        await using LibraryFixture library =
            await LibraryFixture.CreateAsync(xml);
        ApiType type = Assert.Single(
            library.ApiSurfaceCorrespondence.Surface.Types,
            candidate =>
                candidate.FullName
                    == "System.Text.Json.JsonSerializer");
        DocumentationSubjectReference typeSubject =
            DocumentationSubjectReference.ForType(
                library.ApiSurfaceCorrespondence,
                type);
        DocumentationSubjectReference memberSubject =
            Subject(library);
        var limits = XmlDocumentationReadLimits.Default with
        {
            MaxRetainedTextCharacters = Math.Max(
                typeIdentity.Length + typeSummary.Length,
                DeserializeIdentity.Length + memberSummary.Length),
        };
        Assert.True(
            limits.MaxRetainedTextCharacters
                < typeIdentity.Length
                    + typeSummary.Length
                    + DeserializeIdentity.Length
                    + memberSummary.Length);
        DocumentationHouseRequest typeRequest = Request(
            typeSubject,
            DirectLibraryDocumentationHouseAdapter
                .CreateCompiledXmlContributions(
                    library.Reference,
                    typeSubject),
            xmlReadLimits: limits);
        DocumentationHouseRequest memberRequest = Request(
            memberSubject,
            DirectLibraryDocumentationHouseAdapter
                .CreateCompiledXmlContributions(
                    library.Reference,
                    memberSubject),
            xmlReadLimits: limits);

        CompiledDocumentationQueryResult typeAlone =
            await CompiledDocumentationQuery.ExecuteAsync(
                typeRequest,
                library.IssueOperation(),
                TestContext.Current.CancellationToken);
        CompiledDocumentationQueryResult memberAlone =
            await CompiledDocumentationQuery.ExecuteAsync(
                memberRequest,
                library.IssueOperation(),
                TestContext.Current.CancellationToken);
        IReadOnlyList<CompiledDocumentationQueryResult> together =
            await CompiledDocumentationQuery.ExecuteManyAsync(
                [typeRequest, memberRequest],
                library.IssueOperation(),
                TestContext.Current.CancellationToken);

        var expectedType =
            Assert.IsType<CompiledDocumentationOutcome.Available>(
                typeAlone.Content);
        var expectedMember =
            Assert.IsType<CompiledDocumentationOutcome.Available>(
                memberAlone.Content);
        Assert.Equal(
            expectedType.Documentation,
            Assert.IsType<CompiledDocumentationOutcome.Available>(
                together[0].Content).Documentation);
        Assert.Equal(
            expectedMember.Documentation,
            Assert.IsType<CompiledDocumentationOutcome.Available>(
                together[1].Content).Documentation);
        Assert.Equal(
            1,
            together.Count(
                result => result.Outcome.Work.ParsedCompiledXml));
        Assert.Single(
            together,
            result =>
                result.Outcome.Work.CompiledXmlBytesObserved > 0);
    }

    [Fact]
    public async Task
        ExecuteMany_HeterogeneousReadPoliciesRetainOnlyTheirOwnSubjects()
    {
        byte[] xml = Encoding.UTF8.GetBytes($"""
            <doc>
              <members>
                <member name="T:System.Text.Json.JsonSerializer">
                  <summary>Type.</summary>
                </member>
                <member name="{DeserializeIdentity}">
                  <summary>Member.</summary>
                </member>
              </members>
            </doc>
            """);
        await using LibraryFixture library =
            await LibraryFixture.CreateAsync(xml);
        ApiType type = Assert.Single(
            library.ApiSurfaceCorrespondence.Surface.Types,
            candidate =>
                candidate.FullName
                    == "System.Text.Json.JsonSerializer");
        DocumentationSubjectReference typeSubject =
            DocumentationSubjectReference.ForType(
                library.ApiSurfaceCorrespondence,
                type);
        DocumentationSubjectReference memberSubject =
            Subject(library);
        var tightLimits = XmlDocumentationReadLimits.Default with
        {
            MaxRetainedTextCharacters = 64,
        };
        DocumentationHouseRequest typeRequest = Request(
            typeSubject,
            DirectLibraryDocumentationHouseAdapter
                .CreateCompiledXmlContributions(
                    library.Reference,
                    typeSubject),
            xmlReadLimits: tightLimits);
        DocumentationHouseRequest memberRequest = Request(
            memberSubject,
            DirectLibraryDocumentationHouseAdapter
                .CreateCompiledXmlContributions(
                    library.Reference,
                    memberSubject));

        CompiledDocumentationQueryResult typeAlone =
            await CompiledDocumentationQuery.ExecuteAsync(
                typeRequest,
                library.IssueOperation(),
                TestContext.Current.CancellationToken);
        CompiledDocumentationQueryResult memberAlone =
            await CompiledDocumentationQuery.ExecuteAsync(
                memberRequest,
                library.IssueOperation(),
                TestContext.Current.CancellationToken);
        IReadOnlyList<CompiledDocumentationQueryResult> together =
            await CompiledDocumentationQuery.ExecuteManyAsync(
                [typeRequest, memberRequest],
                library.IssueOperation(),
                TestContext.Current.CancellationToken);

        var expectedType =
            Assert.IsType<CompiledDocumentationOutcome.Available>(
                typeAlone.Content);
        var expectedMember =
            Assert.IsType<CompiledDocumentationOutcome.Available>(
                memberAlone.Content);
        Assert.Equal(
            expectedType.Documentation,
            Assert.IsType<CompiledDocumentationOutcome.Available>(
                together[0].Content).Documentation);
        Assert.Equal(
            expectedMember.Documentation,
            Assert.IsType<CompiledDocumentationOutcome.Available>(
                together[1].Content).Documentation);
        Assert.All(
            together,
            result => Assert.True(
                result.Outcome.Work.ParsedCompiledXml));
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
        string json = JsonSerializer.Serialize(
            result.Content,
            CompiledDocumentationQueryJsonContext
                .Default
                .CompiledDocumentationOutcome);
        CompiledDocumentationOutcome copy =
            JsonSerializer.Deserialize(
                json,
                CompiledDocumentationQueryJsonContext
                    .Default
                    .CompiledDocumentationOutcome)!;
        CompiledDocumentationOutcome.Incomplete copied =
            Assert.IsType<CompiledDocumentationOutcome.Incomplete>(copy);
        Assert.Equal(8, copied.Sources.Length);
        Assert.True(copied.SourcesTruncated);
        Assert.True(
            json.Length <= MaximumBoundedNonAvailableJsonCodeUnits,
            $"Incomplete JSON was {json.Length} UTF-16 code units.");

        using JsonDocument document = JsonDocument.Parse(json);
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
        SelectedByteLimitEvidencePrecedesBoundedContext()
    {
        byte[] xml = Encoding.UTF8.GetBytes(
            """
            <doc><members>
              <member name="M:System.Text.Json.JsonSerializer.Deserialize``1(System.Text.Json.JsonDocument,System.Text.Json.JsonSerializerOptions)">
                <summary>documentation larger than one byte</summary>
              </member>
            </members></doc>
            """);
        await using LibraryFixture library =
            await LibraryFixture.CreateAsync(xml);
        DocumentationSubjectReference subject = Subject(library);
        CompiledXmlContribution selected =
            Assert.Single(
                DirectLibraryDocumentationHouseAdapter
                    .CreateCompiledXmlContributions(
                        library.Reference,
                        subject));
        CompiledXmlContribution[] precedingUnavailable =
            Enumerable.Range(0, 8)
                .Select(
                    index =>
                        CompiledXmlContribution.Unavailable(
                            subject,
                            library.Reference,
                            library.Reference.ApiAssembly,
                            DocumentationSourceReference.Create(
                                DocumentationSourceKind.DirectLibrary,
                                $"u{index}")))
                .ToArray();

        CompiledDocumentationQueryResult result =
            await CompiledDocumentationQuery.ExecuteAsync(
                Request(
                    subject,
                    [.. precedingUnavailable, selected],
                    maximumContributions: 9,
                    maximumCompiledXmlBytes: 1),
                library.IssueOperation(),
                TestContext.Current.CancellationToken);

        DocumentationHouseOutcome.Completed completed =
            Assert.IsType<DocumentationHouseOutcome.Completed>(
                result.Outcome);
        DocumentationCompiledXmlAttempt.Incomplete houseIncomplete =
            Assert.IsType<DocumentationCompiledXmlAttempt.Incomplete>(
                completed.CompiledXmlAttempt);
        Assert.Same(selected, houseIncomplete.Selected);
        Assert.Equal(
            DocumentationIncompleteBoundary.CompiledXmlByteLimit,
            houseIncomplete.Boundary);

        CompiledDocumentationOutcome.Incomplete content =
            Assert.IsType<CompiledDocumentationOutcome.Incomplete>(
                result.Content);
        Assert.Equal(
            CompiledDocumentationIncompleteReason.CompiledXmlByteLimit,
            content.Reason);
        Assert.Equal(8, content.Sources.Length);
        Assert.Equal(
            CompiledDocumentationSourceEvidenceKind.Candidate,
            content.Sources[0].Kind);
        Assert.Equal(
            "direct-library:System.Text.Json",
            content.Sources[0].Source.Name);
        Assert.Equal(
            7,
            content.Sources.Count(
                static source =>
                    source.Kind
                        == CompiledDocumentationSourceEvidenceKind
                            .Unavailable));
        Assert.True(content.SourcesTruncated);

        await library.RetireAsync();
        string json = Serialize(result.Content);
        Assert.True(
            json.Length <= MaximumBoundedNonAvailableJsonCodeUnits,
            $"Incomplete JSON was {json.Length} UTF-16 code units.");
        using JsonDocument document = JsonDocument.Parse(json);
        AssertPropertyNames(
            document.RootElement,
            "kind",
            "subject",
            "reason",
            "sources",
            "sourcesTruncated");
        JsonElement sourceEvidence = document.RootElement
            .GetProperty("sources")[0];
        Assert.Equal(
            nameof(CompiledDocumentationSourceEvidenceKind.Candidate),
            sourceEvidence.GetProperty("kind").GetString());
    }

    [Fact]
    public async Task
        PartialSelectionEvidencePrecedesBoundedContext()
    {
        await using LibraryFixture library =
            await LibraryFixture.CreateAsync();
        DocumentationSubjectReference subject = Subject(library);
        CompiledXmlContribution partial =
            CompiledXmlContribution.Partial(
                subject,
                library.Reference,
                library.Reference.ApiAssembly,
                DocumentationSourceReference.Create(
                    DocumentationSourceKind.Package,
                    "package:System.Text.Json@10.0.0"));
        CompiledXmlContribution[] precedingUnavailable =
            Enumerable.Range(0, 8)
                .Select(
                    index =>
                        CompiledXmlContribution.Unavailable(
                            subject,
                            library.Reference,
                            library.Reference.ApiAssembly,
                            DocumentationSourceReference.Create(
                                DocumentationSourceKind.DirectLibrary,
                                $"u{index}")))
                .ToArray();

        CompiledDocumentationQueryResult result =
            await CompiledDocumentationQuery.ExecuteAsync(
                Request(
                    subject,
                    [.. precedingUnavailable, partial],
                    maximumContributions: 9),
                library.IssueOperation(),
                TestContext.Current.CancellationToken);

        DocumentationHouseOutcome.Completed completed =
            Assert.IsType<DocumentationHouseOutcome.Completed>(
                result.Outcome);
        DocumentationCompiledXmlAttempt.Incomplete houseIncomplete =
            Assert.IsType<DocumentationCompiledXmlAttempt.Incomplete>(
                completed.CompiledXmlAttempt);
        Assert.Null(houseIncomplete.Selected);
        Assert.Equal(
            DocumentationIncompleteBoundary.CompanionSelectionPartial,
            houseIncomplete.Boundary);

        CompiledDocumentationOutcome.Incomplete content =
            Assert.IsType<CompiledDocumentationOutcome.Incomplete>(
                result.Content);
        Assert.Equal(
            CompiledDocumentationIncompleteReason.CompanionSelectionPartial,
            content.Reason);
        Assert.Equal(8, content.Sources.Length);
        Assert.Equal(
            CompiledDocumentationSourceEvidenceKind.Partial,
            content.Sources[0].Kind);
        Assert.Equal(
            "package:System.Text.Json@10.0.0",
            content.Sources[0].Source.Name);
        Assert.Equal(
            7,
            content.Sources.Count(
                static source =>
                    source.Kind
                        == CompiledDocumentationSourceEvidenceKind
                            .Unavailable));
        Assert.True(content.SourcesTruncated);

        await library.RetireAsync();
        string json = Serialize(result.Content);
        Assert.True(
            json.Length <= MaximumBoundedNonAvailableJsonCodeUnits,
            $"Incomplete JSON was {json.Length} UTF-16 code units.");
        using JsonDocument document = JsonDocument.Parse(json);
        AssertPropertyNames(
            document.RootElement,
            "kind",
            "subject",
            "reason",
            "sources",
            "sourcesTruncated");
        JsonElement sourceEvidence = document.RootElement
            .GetProperty("sources")[0];
        Assert.Equal(
            nameof(CompiledDocumentationSourceEvidenceKind.Partial),
            sourceEvidence.GetProperty("kind").GetString());
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

        CompiledDocumentationOutcome.MalformedOrUnreadableDocument failed =
            Assert.IsType<
                CompiledDocumentationOutcome
                    .MalformedOrUnreadableDocument>(
                    malformed.Content);
        using JsonDocument failedJson =
            JsonDocument.Parse(Serialize(malformed.Content));
        AssertPropertyNames(
            failedJson.RootElement,
            "kind",
            "subject",
            "source");
        Assert.Equal(
            "malformedOrUnreadableDocument",
            failedJson.RootElement
                .GetProperty("kind")
                .GetString());
        CompiledDocumentationOutcome failedCopy =
            JsonSerializer.Deserialize(
                Serialize(malformed.Content),
                CompiledDocumentationQueryJsonContext
                    .Default
                    .CompiledDocumentationOutcome)!;
        Assert.IsType<
            CompiledDocumentationOutcome.MalformedOrUnreadableDocument>(
                failedCopy);

        LibraryOperationLease disposedOperation =
            malformedLibrary.IssueOperation();
        disposedOperation.Dispose();
        IReadOnlyList<CompiledXmlContribution> contentAccessContributions =
            DirectLibraryDocumentationHouseAdapter
                .CreateCompiledXmlContributions(
                    malformedLibrary.Reference,
                    malformedSubject);
        CompiledDocumentationQueryResult contentAccessFailed =
            await CompiledDocumentationQuery.ExecuteAsync(
                Request(
                    malformedSubject,
                    contentAccessContributions),
                disposedOperation,
                TestContext.Current.CancellationToken);
        DocumentationHouseOutcome.Failed houseFailure =
            Assert.IsType<DocumentationHouseOutcome.Failed>(
                contentAccessFailed.Outcome);
        Assert.Same(
            Assert.Single(contentAccessContributions),
            houseFailure.Failure.Selected);
        CompiledDocumentationOutcome.ContentAccessFailed portableFailure =
            Assert.IsType<CompiledDocumentationOutcome.ContentAccessFailed>(
                contentAccessFailed.Content);
        Assert.Equal(
            CompiledDocumentationSourceKind.DirectLibrary,
            portableFailure.Source.Kind);
        string contentAccessJsonText =
            Serialize(contentAccessFailed.Content);
        using JsonDocument contentAccessJson =
            JsonDocument.Parse(contentAccessJsonText);
        AssertPropertyNames(
            contentAccessJson.RootElement,
            "kind",
            "subject",
            "source");
        Assert.Equal(
            "contentAccessFailed",
            contentAccessJson.RootElement
                .GetProperty("kind")
                .GetString());
        CompiledDocumentationOutcome.ContentAccessFailed contentAccessCopy =
            Assert.IsType<CompiledDocumentationOutcome.ContentAccessFailed>(
                JsonSerializer.Deserialize(
                    contentAccessJsonText,
                    CompiledDocumentationQueryJsonContext
                        .Default
                        .CompiledDocumentationOutcome));
        Assert.Equal(portableFailure.Source, contentAccessCopy.Source);
        Assert.True(
            contentAccessJsonText.Length
                <= MaximumBoundedNonAvailableJsonCodeUnits,
            $"Content-access failure JSON was "
                + $"{contentAccessJsonText.Length} UTF-16 code units.");
    }

    [Fact]
    public async Task
        AuthoritativePackageAbsenceSurvivesBoundedProvenance()
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
        CompiledXmlContribution[] precedingUnavailable =
            Enumerable.Range(0, 8)
                .Select(
                    index =>
                        CompiledXmlContribution.Unavailable(
                            subject,
                            library.Reference,
                            library.Reference.ApiAssembly,
                            DocumentationSourceReference.Create(
                                DocumentationSourceKind.DirectLibrary,
                                $"unavailable-source-{index}")))
                .ToArray();

        CompiledDocumentationQueryResult result =
            await CompiledDocumentationQuery.ExecuteAsync(
                Request(
                    subject,
                    [.. precedingUnavailable, absentContribution],
                    maximumContributions: 9),
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
            Assert.Single(
                content.Sources,
                static source =>
                    source.Kind
                        == CompiledDocumentationSourceEvidenceKind.Absent);
        Assert.Equal(
            CompiledDocumentationSourceEvidenceKind.Absent,
            evidence.Kind);
        Assert.Equal(
            CompiledDocumentationSourceKind.Package,
            evidence.Source.Kind);
        Assert.Equal(
            "package:System.Text.Json@10.0.0",
            evidence.Source.Name);
        Assert.Equal(8, content.Sources.Length);
        Assert.Equal(
            7,
            content.Sources.Count(
                static source =>
                    source.Kind
                        == CompiledDocumentationSourceEvidenceKind
                            .Unavailable));
        Assert.True(content.SourcesTruncated);

        await library.RetireAsync();
        string json = Serialize(result.Content);
        Assert.True(
            json.Length <= MaximumBoundedNonAvailableJsonCodeUnits,
            $"Absent JSON was {json.Length} UTF-16 code units.");
        using JsonDocument document = JsonDocument.Parse(json);
        AssertPropertyNames(
            document.RootElement,
            "kind",
            "subject",
            "sources",
            "sourcesTruncated");
        JsonElement sourceEvidence = document.RootElement
            .GetProperty("sources")[0];
        AssertPropertyNames(sourceEvidence, "kind", "source");
        Assert.Equal(
            nameof(CompiledDocumentationSourceEvidenceKind.Absent),
            sourceEvidence.GetProperty("kind").GetString());
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

    private static string Serialize(CompiledDocumentationOutcome content) =>
        JsonSerializer.Serialize(
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
        int maximumCompiledXmlBytes = 8 * 1024 * 1024,
        DateTimeOffset? deadline = null,
        XmlDocumentationReadLimits? xmlReadLimits = null)
    {
        var limits = new DocumentationHouseLimits(
            maximumContributions,
            maximumCompiledXmlBytes,
            xmlReadLimits ?? XmlDocumentationReadLimits.Default);
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

    internal sealed partial class LibraryFixture : IAsyncDisposable
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

        public LibraryContentOwner Owner => _owner;

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
