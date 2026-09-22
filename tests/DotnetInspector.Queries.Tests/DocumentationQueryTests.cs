using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

using CSharpText;
using DotnetInspector.DocumentationHouse;
using DotnetInspector.DocumentationHouse.Source;
using DotnetInspector.Libraries;
using DotnetInspector.LibraryMetadata;
using DotnetInspector.SourceHouse;
using DotnetInspector.SourceHouse.BuildAttestation;
using ILInspector.Metadata;
using ILInspector.SourceLink;
using QuerySpace;
using QuerySpace.Composition;
using QuerySpace.Operations;

using LibraryFixture =
    DotnetInspector.Queries.Tests.CompiledDocumentationQueryTests.LibraryFixture;

namespace DotnetInspector.Queries.Tests;

public sealed class DocumentationQueryTests
{
    private const string DocumentationQueryDeserializeIdentity =
        "M:System.Text.Json.JsonSerializer.Deserialize``1(System.Text.Json.JsonDocument,System.Text.Json.JsonSerializerOptions)";
    private static readonly ApiSurfaceExtractionBounds
        s_documentationQueryApiSurfaceBounds =
            new(
                maxTypes: 5_000,
                maxMembers: 100_000,
                maxInspectionFailures: 1_000,
                maxTypeForwarders: 10_000,
                maxMetadataRows: 1_000_000,
                maxRetainedTextCharacters: 20_000_000);
    private static readonly Lazy<SourceHouseBuildAttestation>
        s_documentationQueryBuildAttestation =
            new(BuildDocumentationQueryAttestation);

    [Fact]
    public void UnifiedPortableContract_IsClosedAndQueriesOwned()
    {
        var visited = new HashSet<Type>();

        Assert.Equal(
            new Dictionary<Type, object>
            {
                [typeof(DocumentationQueryOutcome.Completed)] =
                    "completed",
                [typeof(DocumentationQueryOutcome.RequestRejected)] =
                    "requestRejected",
                [typeof(DocumentationQueryOutcome.Failed)] = "failed",
                [typeof(DocumentationQueryOutcome.Incomplete)] =
                    "incomplete",
            },
            DerivedTypes(typeof(DocumentationQueryOutcome)));
        Assert.Equal(
            new Dictionary<Type, object>
            {
                [typeof(AuthoredDocumentationOutcome.Available)] =
                    "available",
                [typeof(AuthoredDocumentationOutcome.Absent)] = "absent",
                [typeof(AuthoredDocumentationOutcome.Unavailable)] =
                    "unavailable",
                [typeof(AuthoredDocumentationOutcome.Ambiguous)] =
                    "ambiguous",
                [typeof(AuthoredDocumentationOutcome.Rejected)] =
                    "rejected",
                [typeof(AuthoredDocumentationOutcome.Failed)] = "failed",
                [typeof(AuthoredDocumentationOutcome.Incomplete)] =
                    "incomplete",
            },
            DerivedTypes(typeof(AuthoredDocumentationOutcome)));

        Visit(typeof(DocumentationQueryOutcome));

        static Dictionary<Type, object> DerivedTypes(Type type)
        {
            JsonPolymorphicAttribute polymorphic =
                Assert.Single(
                    type.GetCustomAttributes<JsonPolymorphicAttribute>());
            Assert.Equal(
                "kind",
                polymorphic.TypeDiscriminatorPropertyName);
            return type
                .GetCustomAttributes<JsonDerivedTypeAttribute>()
                .ToDictionary(
                    static attribute => attribute.DerivedType,
                    static attribute => attribute.TypeDiscriminator!);
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
                typeof(DocumentationQueryOutcome).Namespace,
                type.Namespace);
            if (type.IsEnum)
                return;

            foreach (JsonDerivedTypeAttribute derived
                in type.GetCustomAttributes<JsonDerivedTypeAttribute>())
            {
                Visit(derived.DerivedType);
            }
            foreach (PropertyInfo property
                in type.GetProperties(
                    BindingFlags.Public
                        | BindingFlags.Instance))
            {
                Visit(property.PropertyType);
            }
        }
    }

    [Fact]
    public void UnifiedOperation_RegistersOneRequiredClosedDemand()
    {
        IQueryOperationRoute route = DocumentationQuery.OperationRoute;

        Assert.Equal(DocumentationQuery.OperationIdentity, route.OperationIdentity);
        Assert.Equal(
            DocumentationQuery.OperationSubjectRole,
            route.SubjectRole);
        Assert.Equal(
            DocumentationQuery.OperationResultGrain,
            route.ResultGrain);
        Assert.Equal([DocumentationQuery.DocumentationRowSet], route.RowSets);
        QueryOperationTermCapability demand =
            Assert.Single(route.Capabilities.Terms);
        Assert.Equal(DocumentationQuery.DemandTermKey, demand.Binding.Key);
        Assert.Equal(
            [
                DocumentationQuery.CompiledXmlDemandValue,
                DocumentationQuery.AuthoredSourceDemandValue,
                DocumentationQuery.CombinedDemandValue,
            ],
            demand.Binding.Description.Values);
        Assert.Equal([PortableQueryOperator.Equal], demand.Operators);

        QuerySpaceBinding querySpace = DocumentationQuery.QuerySpace;
        Assert.Equal(
            DocumentationQuery.QuerySpaceIdentity,
            querySpace.Descriptor.Identity);
        Assert.Equal(
            [DocumentationQuery.DocumentationRowSet],
            querySpace.Descriptor.Operation.RowSets);
        Assert.Same(
            DocumentationQuery.DocumentationRowScope,
            Assert.Single(querySpace.RowScopes));
        Assert.Equal(
            [QuerySpaceTerminalRequirement.Rows],
            querySpace.Descriptor.Terminals);
        Assert.True(
            querySpace.Descriptor.TryGetResultContract(
                QuerySpaceTerminalRequirement.Rows,
                out QuerySpaceResultContractDescriptor? resultContract));
        Assert.Equal(
            DocumentationQuery.ResultContractIdentity,
            resultContract.Identity);
    }

    [Theory]
    [InlineData(DocumentationDemand.CompiledXml)]
    [InlineData(DocumentationDemand.AuthoredSourceDocumentation)]
    [InlineData(
        DocumentationDemand.CompiledXmlAndAuthoredSourceDocumentation)]
    public void UnifiedRequest_ResolvesExactlyOneDemand(
        DocumentationDemand expected)
    {
        QuerySpaceRequest request = DocumentationQuery.CreateRequest(expected);

        var accepted =
            Assert.IsType<DocumentationQueryRequestResult.Accepted>(
                DocumentationQuery.ResolveRequest(
                    request,
                    TestContext.Current.CancellationToken));

        Assert.Equal(expected, accepted.Plan.Demand);
        Assert.Same(request, accepted.Plan.Request);
        Assert.Equal(
            DocumentationQuery.QuerySpaceIdentity,
            accepted.Plan.Request.QuerySpace);
        Assert.Equal(
            DocumentationQuery.ResultContractIdentity,
            accepted.Plan.Request.ResultContract);
        Assert.Empty(accepted.Plan.Request.RowIntents);
    }

    [Fact]
    public void UnifiedIntent_RejectsMissingUnknownAndConflictingDemand()
    {
        var missing = Assert.IsType<DocumentationQueryPlanResult.Rejected>(
            DocumentationQuery.ResolveIntent(
                PortableQueryIntent.Empty,
                TestContext.Current.CancellationToken));
        Assert.Equal(
            PortableQueryFailureReason.RequiredTermFamilyMissing,
            missing.Failure.Reason);

        PortableQueryIntent unknown = PortableQueryIntent.Create(
            [
                new(
                    DocumentationQuery.DemandTermKey,
                    PortableQueryOperator.Equal,
                    "source-or-maybe-xml"),
            ],
            [],
            [],
            []);
        var unknownResult =
            Assert.IsType<DocumentationQueryPlanResult.Rejected>(
                DocumentationQuery.ResolveIntent(
                    unknown,
                    TestContext.Current.CancellationToken));
        Assert.Equal(
            PortableQueryFailureReason.ValueRejected,
            unknownResult.Failure.Reason);

        PortableQueryIntent conflicting = PortableQueryIntent.Create(
            [
                new(
                    DocumentationQuery.DemandTermKey,
                    PortableQueryOperator.Equal,
                    DocumentationQuery.CompiledXmlDemandValue),
                new(
                    DocumentationQuery.DemandTermKey,
                    PortableQueryOperator.Equal,
                    DocumentationQuery.AuthoredSourceDemandValue),
            ],
            [],
            [],
            []);
        var conflictingResult =
            Assert.IsType<DocumentationQueryPlanResult.Rejected>(
                DocumentationQuery.ResolveIntent(
                    conflicting,
                    TestContext.Current.CancellationToken));
        Assert.Equal(
            PortableQueryFailureReason.TermsIncompatible,
            conflictingResult.Failure.Reason);
    }

    [Fact]
    public void UnifiedRequest_RejectsForeignQuerySpace()
    {
        QuerySpaceRequest foreign = QuerySpaceRequest.Create(
            GraphLibrariesQuery.QuerySpace.Descriptor,
            GraphLibrariesQuery.CreateIntent(cluster: 1),
            [GraphLibrariesQuery.DirectUseClustersRowSet],
            [],
            QuerySpaceTerminalRequirement.Rows);

        var rejected =
            Assert.IsType<DocumentationQueryRequestResult.Rejected>(
                DocumentationQuery.ResolveRequest(
                    foreign,
                    TestContext.Current.CancellationToken));

        Assert.Equal(
            DocumentationQueryRequestRejectionKind.QuerySpaceMismatch,
            rejected.Kind);
    }

    [Fact]
    public async Task
        ImplementationSubjectResolver_UsesImplementationIssuedToken()
    {
        const string identity =
            "M:DocumentationQuery.Split.Subject.Target(System.String)";
        SourceHouseBuildAttestation api =
            EmitDocumentationQueryAttestation(
                "DocumentationQuerySplitFixture",
                [
                    new(
                        "Api.cs",
                        """
                        #nullable enable
                        namespace DocumentationQuery.Split;

                        public static class Subject
                        {
                            public static void Neighbor() { }
                            public static void Target(string value) { }
                        }
                        """u8.ToArray()),
                ],
                "documentation-query-split-api");
        SourceHouseBuildAttestation implementation =
            EmitDocumentationQueryAttestation(
                "DocumentationQuerySplitFixture",
                [
                    new(
                        "Implementation.cs",
                        """
                        #nullable enable
                        namespace DocumentationQuery.Split;

                        public static class Subject
                        {
                            public static void Target(string? value) { }
                            public static void Neighbor() { }
                        }
                        """u8.ToArray()),
                ],
                "documentation-query-split-implementation");
        await using LibraryFixture library =
            await LibraryFixture.CreateSplitSourceAsync(
                api.PeImage.ToArray(),
                implementation.PeImage.ToArray(),
                implementation.PortablePdbImage.ToArray());
        DocumentationSubjectReference subject =
            CompiledDocumentationSubjectResolver.Resolve(
                library.Reference,
                library.Owner,
                [identity],
                ApiSurfaceExtractionScope.Public,
                s_documentationQueryApiSurfaceBounds,
                TestContext.Current.CancellationToken)[identity];

        var resolved = Assert.IsType<
            DocumentationImplementationSubjectResolution.Resolved>(
                DocumentationImplementationSubjectResolver.Resolve(
                    library.Reference,
                    library.Owner,
                    subject,
                    ApiSurfaceExtractionScope.Public,
                    s_documentationQueryApiSurfaceBounds,
                    TestContext.Current.CancellationToken));

        Assert.NotEqual(
            subject.MetadataToken,
            resolved.Subject.MetadataToken);
        Assert.Equal(
            MethodToken(implementation.PeImage, "Target"),
            resolved.Subject.MetadataToken);
        Assert.Equal(
            subject.TypeIdentity,
            resolved.Subject.TypeIdentity);
        Assert.NotEqual(
            subject.MemberIdentity,
            resolved.Subject.MemberIdentity);
    }

    [Fact]
    public async Task
        ImplementationSubjectResolver_ReportsAmbiguousAndBounded()
    {
        const string assemblyName =
            "DocumentationQueryAmbiguousFixture";
        const string identity =
            "M:DocumentationQuery.Ambiguous.Subject.Target";
        SourceHouseBuildAttestation api =
            EmitDocumentationQueryAttestation(
                assemblyName,
                [
                    new(
                        "Api.cs",
                        """
                        namespace DocumentationQuery.Ambiguous;

                        public static class Subject
                        {
                            public static void Target() { }
                        }
                        """u8.ToArray()),
                ],
                "documentation-query-ambiguous-api");
        byte[] implementation = DuplicateTargetAssembly(assemblyName);
        await using LibraryFixture library =
            await LibraryFixture.CreateSplitSourceAsync(
                api.PeImage.ToArray(),
                implementation,
                api.PortablePdbImage.ToArray());
        DocumentationSubjectReference subject =
            CompiledDocumentationSubjectResolver.Resolve(
                library.Reference,
                library.Owner,
                [identity],
                ApiSurfaceExtractionScope.Public,
                s_documentationQueryApiSurfaceBounds,
                TestContext.Current.CancellationToken)[identity];

        var ambiguous = Assert.IsType<
            DocumentationImplementationSubjectResolution.Ambiguous>(
                DocumentationImplementationSubjectResolver.Resolve(
                    library.Reference,
                    library.Owner,
                    subject,
                    ApiSurfaceExtractionScope.Public,
                    s_documentationQueryApiSurfaceBounds,
                    TestContext.Current.CancellationToken));
        Assert.Equal(2, ambiguous.MatchCount);

        var incomplete = Assert.IsType<
            DocumentationImplementationSubjectResolution.Incomplete>(
                DocumentationImplementationSubjectResolver.Resolve(
                    library.Reference,
                    library.Owner,
                    subject,
                    ApiSurfaceExtractionScope.Public,
                    new(
                        maxTypes: 0,
                        maxMembers: 100,
                        maxInspectionFailures: 10,
                        maxTypeForwarders: 10,
                        maxMetadataRows: 1_000,
                        maxRetainedTextCharacters: 10_000),
                    TestContext.Current.CancellationToken));
        Assert.Equal(
            ApiSurfaceExtractionBound.Types,
            incomplete.Bound);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task
        ImplementationResolutionTerminal_PreservesCompiledChannel(
            bool incomplete)
    {
        SourceHouseBuildAttestation attestation =
            s_documentationQueryBuildAttestation.Value;
        var capability = new DocumentationQueryAttestationCapability(
            attestation);
        string identity;
        await using (LibraryFixture probe =
            await LibraryFixture.CreateSourceAsync(
                attestation.PeImage.ToArray(),
                attestation.PortablePdbImage.ToArray()))
        {
            identity = DocumentationQueryAuthoredScenario
                .Create(probe, capability)
                .Binding
                .Subject
                .CompiledXmlIdentity
                .Value;
        }

        byte[] xml = QueryXml(identity, "compiled-terminal-summary");
        await using LibraryFixture library =
            await LibraryFixture.CreateSourceAsync(
                attestation.PeImage.ToArray(),
                attestation.PortablePdbImage.ToArray(),
                xml);
        DocumentationQueryAuthoredScenario scenario =
            DocumentationQueryAuthoredScenario.Create(
                library,
                capability);
        var binding = new DocumentationAuthoredSourceOperationBinding(
            scenario.Binding.Request,
            scenario.Binding.OperationPlan,
            scenario.Binding.PolicyGeneration,
            scenario.Binding.Subject,
            scenario.Binding.ImplementationContent,
            implementationSubject: null);
        DocumentationImplementationSubjectResolution resolution =
            incomplete
                ? new DocumentationImplementationSubjectResolution
                    .Incomplete(ApiSurfaceExtractionBound.Types)
                : new DocumentationImplementationSubjectResolution
                    .Ambiguous(2);
        IDocumentationAuthoredSourceOperation authoredOperation =
            DocumentationImplementationSubjectResolver
                .CreateTerminalOperation(binding, resolution);
        var operationPlan = new DocumentationHouseOperationPlan(
            binding.OperationPlan,
            binding.PolicyGeneration,
            DocumentationQueryHouseLimits(),
            scenario.Invocation.Deadline,
            [
                QueryCandidate(
                    library,
                    binding.Subject,
                    xmlIndex: 0),
            ],
            new(
                binding,
                scenario.Invocation.RemainingLimits,
                authoredOperation));
        DocumentationQueryPlan query =
            Assert.IsType<DocumentationQueryRequestResult.Accepted>(
                DocumentationQuery.ResolveRequest(
                    DocumentationQuery.CreateRequest(
                        DocumentationDemand
                            .CompiledXmlAndAuthoredSourceDocumentation),
                    TestContext.Current.CancellationToken))
                .Plan;

        DocumentationQueryResult result =
            await DocumentationQuery.ExecuteAsync(
                query,
                binding.Request,
                binding.Subject,
                operationPlan,
                library.IssueOperation(),
                TestContext.Current.CancellationToken);

        var content =
            Assert.IsType<DocumentationQueryOutcome.Completed>(
                result.Content);
        Assert.Equal(
            "compiled-terminal-summary",
            Assert.IsType<CompiledDocumentationOutcome.Available>(
                    content.CompiledXml)
                .Documentation
                .Summary);
        if (incomplete)
        {
            Assert.Equal(
                AuthoredDocumentationIncompleteReason
                    .ImplementationSurface,
                Assert.IsType<AuthoredDocumentationOutcome.Incomplete>(
                        content.AuthoredSource)
                    .Reason);
        }
        else
        {
            Assert.Equal(
                AuthoredDocumentationAmbiguityReason
                    .DeclarationAmbiguous,
                Assert.IsType<AuthoredDocumentationOutcome.Ambiguous>(
                        content.AuthoredSource)
                    .Reason);
        }
    }

    [Fact]
    public async Task
        RealCombinedQuery_ResolvesThroughQuerySpaceAndPublishesConflict()
    {
        SourceHouseBuildAttestation attestation =
            s_documentationQueryBuildAttestation.Value;
        var capability = new DocumentationQueryAttestationCapability(
            attestation);
        string identity;
        await using (LibraryFixture probe =
            await LibraryFixture.CreateSourceAsync(
                attestation.PeImage.ToArray(),
                attestation.PortablePdbImage.ToArray()))
        {
            identity = DocumentationQueryAuthoredScenario
                .Create(probe, capability)
                .Binding
                .Subject
                .CompiledXmlIdentity
                .Value;
        }

        byte[] xml = QueryXml(identity, "compiled-channel-summary");
        await using LibraryFixture library =
            await LibraryFixture.CreateSourceAsync(
                attestation.PeImage.ToArray(),
                attestation.PortablePdbImage.ToArray(),
                xml);
        DocumentationQueryAuthoredScenario scenario =
            DocumentationQueryAuthoredScenario.Create(
                library,
                capability);
        IDocumentationAuthoredSourceOperation authoredOperation =
            SourceHouseDocumentationHouseAdapter.CreateOperation(
                scenario.Binding,
                scenario.SourceRequest);
        var operationPlan = new DocumentationHouseOperationPlan(
            scenario.Binding.OperationPlan,
            scenario.Binding.PolicyGeneration,
            DocumentationQueryHouseLimits(),
            scenario.Invocation.Deadline,
            [
                QueryCandidate(
                    library,
                    scenario.Binding.Subject,
                    xmlIndex: 0),
            ],
            new(
                scenario.Binding,
                scenario.Invocation.RemainingLimits,
                authoredOperation));
        var query = Assert.IsType<
            DocumentationQueryRequestResult.Accepted>(
                DocumentationQuery.ResolveRequest(
                    DocumentationQuery.CreateRequest(
                        DocumentationDemand
                            .CompiledXmlAndAuthoredSourceDocumentation),
                    TestContext.Current.CancellationToken));
        LibraryOperationLease operation = library.IssueOperation();

        DocumentationQueryResult result =
            await DocumentationQuery.ExecuteAsync(
                query.Plan,
                scenario.Binding.Request,
                scenario.Binding.Subject,
                operationPlan,
                operation,
                TestContext.Current.CancellationToken);

        var exact =
            Assert.IsType<DocumentationHouseOutcome.Completed>(
                result.Outcome);
        var content =
            Assert.IsType<DocumentationQueryOutcome.Completed>(
                result.Content);
        var compiled =
            Assert.IsType<CompiledDocumentationOutcome.Available>(
                content.CompiledXml);
        var authored =
            Assert.IsType<AuthoredDocumentationOutcome.Available>(
                content.AuthoredSource);
        Assert.Equal(
            "compiled-channel-summary",
            compiled.Documentation.Summary);
        Assert.Contains(
            "Locates the declaration",
            authored.Documentation.Summary,
            StringComparison.Ordinal);
        Assert.Equal(
            DocumentationQueryFieldEvidenceKind.Conflict,
            content.Fields.Summary.Kind);
        Assert.Collection(
            content.Fields.Summary.Contributions,
            contribution =>
            {
                Assert.Equal(
                    DocumentationQueryChannel.CompiledXml,
                    contribution.Channel);
                Assert.Equal(
                    "compiled-channel-summary",
                    contribution.Value);
            },
            contribution =>
            {
                Assert.Equal(
                    DocumentationQueryChannel.AuthoredSource,
                    contribution.Channel);
                Assert.Contains(
                    "Locates the declaration",
                    contribution.Value,
                    StringComparison.Ordinal);
            });
        Assert.Equal(1, capability.SourceReads);
        Assert.Equal(1, capability.AttestationReads);
        Assert.Equal(
            DocumentationLibraryLeaseConsumer.SourceHouse,
            exact.LeaseSettlement.Consumer);
        Assert.Same(query.Plan.Request, result.Request);

        string json = JsonSerializer.Serialize(
            result.Content,
            DocumentationQueryJsonContext
                .Default
                .DocumentationQueryOutcome);
        DocumentationQueryOutcome? roundTrip =
            JsonSerializer.Deserialize(
                json,
                DocumentationQueryJsonContext
                    .Default
                    .DocumentationQueryOutcome);
        var roundTripCompleted =
            Assert.IsType<DocumentationQueryOutcome.Completed>(roundTrip);
        Assert.IsType<CompiledDocumentationOutcome.Available>(
            roundTripCompleted.CompiledXml);
        Assert.IsType<AuthoredDocumentationOutcome.Available>(
            roundTripCompleted.AuthoredSource);
        Assert.Equal(
            DocumentationQueryFieldEvidenceKind.Conflict,
            roundTripCompleted.Fields.Summary.Kind);
    }

    [Fact]
    public async Task
        AuthoredUnavailableQuery_PublishesChannelAndAbsentFields()
    {
        await using LibraryFixture library =
            await LibraryFixture.CreateAsync();
        DocumentationSubjectReference subject = Subject(library);
        var query = Assert.IsType<
            DocumentationQueryRequestResult.Accepted>(
                DocumentationQuery.ResolveRequest(
                    DocumentationQuery.CreateRequest(
                        DocumentationDemand
                            .AuthoredSourceDocumentation),
                    TestContext.Current.CancellationToken));
        var operationPlan = new DocumentationHouseOperationPlan(
            DocumentationHouseOperationPlanIdentity.Create(
                "query-authored-unavailable-plan"),
            DocumentationHousePolicyGeneration.Create("query-policy"),
            DocumentationQueryHouseLimits(),
            DateTimeOffset.UtcNow.AddMinutes(1),
            compiledXmlContributions: []);

        DocumentationQueryResult result =
            await DocumentationQuery.ExecuteAsync(
                query.Plan,
                DocumentationHouseRequestIdentity.Create(
                    "query-authored-unavailable-request"),
                subject,
                operationPlan,
                library.IssueOperation(),
                TestContext.Current.CancellationToken);

        var content =
            Assert.IsType<DocumentationQueryOutcome.Completed>(
                result.Content);
        var unavailable =
            Assert.IsType<AuthoredDocumentationOutcome.Unavailable>(
                content.AuthoredSource);
        Assert.Equal(
            AuthoredDocumentationUnavailableReason.OperationUnavailable,
            unavailable.Reason);
        Assert.Null(content.CompiledXml);
        Assert.Equal(
            DocumentationQueryFieldEvidenceKind.Absent,
            content.Fields.Summary.Kind);
        Assert.Equal(
            [DocumentationQueryChannel.AuthoredSource],
            content.Fields.Summary.RequestedChannels);
    }

    [Fact]
    public async Task
        UnifiedCompiledQuery_ForeignLeasePublishesRequestRejection()
    {
        await using LibraryFixture selected =
            await LibraryFixture.CreateAsync();
        await using LibraryFixture foreign =
            await LibraryFixture.CreateAsync();
        DocumentationSubjectReference subject = Subject(selected);
        DocumentationQueryPlan query =
            ResolveDocumentationQuery(DocumentationDemand.CompiledXml);
        DocumentationHouseOperationPlan operationPlan =
            CompiledDocumentationQueryPlan(
                "query-foreign-lease-plan",
                DateTimeOffset.UtcNow.AddMinutes(1));

        DocumentationQueryResult result =
            await DocumentationQuery.ExecuteAsync(
                query,
                DocumentationHouseRequestIdentity.Create(
                    "query-foreign-lease-request"),
                subject,
                operationPlan,
                foreign.IssueOperation(),
                TestContext.Current.CancellationToken);

        var rejected =
            Assert.IsType<DocumentationQueryOutcome.RequestRejected>(
                result.Content);
        Assert.Equal(
            DocumentationQueryRequestRejectionReason.LeaseReferenceMismatch,
            rejected.Reason);
        Assert.IsType<DocumentationHouseOutcome.Rejected>(result.Outcome);
    }

    [Fact]
    public async Task
        UnifiedCompiledQuery_ExpiredDeadlinePublishesIncomplete()
    {
        await using LibraryFixture library =
            await LibraryFixture.CreateAsync();
        DocumentationSubjectReference subject = Subject(library);
        DocumentationQueryPlan query =
            ResolveDocumentationQuery(DocumentationDemand.CompiledXml);
        DocumentationHouseOperationPlan operationPlan =
            CompiledDocumentationQueryPlan(
                "query-expired-plan",
                DateTimeOffset.UtcNow.AddMinutes(-1));

        DocumentationQueryResult result =
            await DocumentationQuery.ExecuteAsync(
                query,
                DocumentationHouseRequestIdentity.Create(
                    "query-expired-request"),
                subject,
                operationPlan,
                library.IssueOperation(),
                TestContext.Current.CancellationToken);

        var incomplete =
            Assert.IsType<DocumentationQueryOutcome.Incomplete>(
                result.Content);
        Assert.Equal(
            CompiledDocumentationIncompleteReason.Deadline,
            incomplete.Reason);
        Assert.IsType<DocumentationHouseOutcome.Incomplete>(
            result.Outcome);
    }

    [Theory]
    [InlineData(DocumentationQueryAuthoredCase.Absent)]
    [InlineData(DocumentationQueryAuthoredCase.SourceUnavailable)]
    [InlineData(DocumentationQueryAuthoredCase.Ambiguous)]
    [InlineData(DocumentationQueryAuthoredCase.Rejected)]
    [InlineData(DocumentationQueryAuthoredCase.Failed)]
    [InlineData(DocumentationQueryAuthoredCase.Incomplete)]
    public async Task
        AuthoredTerminalProjection_PreservesClosedReasonAndObservation(
            DocumentationQueryAuthoredCase scenario)
    {
        DocumentationQueryOutcome.Completed content =
            await ExecuteScriptedAuthoredQueryAsync(scenario);

        switch (scenario)
        {
            case DocumentationQueryAuthoredCase.Absent:
                Assert.IsType<AuthoredDocumentationOutcome.Absent>(
                    content.AuthoredSource);
                break;
            case DocumentationQueryAuthoredCase.SourceUnavailable:
                Assert.Equal(
                    AuthoredDocumentationUnavailableReason
                        .SourceUnavailable,
                    Assert.IsType<
                            AuthoredDocumentationOutcome.Unavailable>(
                            content.AuthoredSource)
                        .Reason);
                break;
            case DocumentationQueryAuthoredCase.Ambiguous:
                Assert.Equal(
                    AuthoredDocumentationAmbiguityReason
                        .PhysicalDeclarationConflict,
                    Assert.IsType<
                            AuthoredDocumentationOutcome.Ambiguous>(
                            content.AuthoredSource)
                        .Reason);
                break;
            case DocumentationQueryAuthoredCase.Rejected:
                Assert.Equal(
                    AuthoredDocumentationRejectionReason.AlreadyInvoked,
                    Assert.IsType<
                            AuthoredDocumentationOutcome.Rejected>(
                            content.AuthoredSource)
                        .Reason);
                break;
            case DocumentationQueryAuthoredCase.Failed:
                Assert.Equal(
                    AuthoredDocumentationFailureReason.SourceFailed,
                    Assert.IsType<AuthoredDocumentationOutcome.Failed>(
                            content.AuthoredSource)
                        .Reason);
                break;
            case DocumentationQueryAuthoredCase.Incomplete:
                Assert.Equal(
                    AuthoredDocumentationIncompleteReason.SourceBytes,
                    Assert.IsType<
                            AuthoredDocumentationOutcome.Incomplete>(
                            content.AuthoredSource)
                        .Reason);
                break;
            default:
                Assert.Fail("Unknown authored projection scenario.");
                break;
        }

        if (scenario != DocumentationQueryAuthoredCase.Absent)
        {
            AuthoredDocumentationObservation observation =
                scenario switch
                {
                    DocumentationQueryAuthoredCase.SourceUnavailable =>
                        ((AuthoredDocumentationOutcome.Unavailable)
                            content.AuthoredSource!).Observation!,
                    DocumentationQueryAuthoredCase.Ambiguous =>
                        ((AuthoredDocumentationOutcome.Ambiguous)
                            content.AuthoredSource!).Observation!,
                    DocumentationQueryAuthoredCase.Rejected =>
                        ((AuthoredDocumentationOutcome.Rejected)
                            content.AuthoredSource!).Observation!,
                    DocumentationQueryAuthoredCase.Failed =>
                        ((AuthoredDocumentationOutcome.Failed)
                            content.AuthoredSource!).Observation!,
                    DocumentationQueryAuthoredCase.Incomplete =>
                        ((AuthoredDocumentationOutcome.Incomplete)
                            content.AuthoredSource!).Observation!,
                    _ => throw new InvalidOperationException(),
                };
            Assert.Equal("scripted-observation", observation.Code);
            Assert.Equal("portable detail", observation.Detail);
            Assert.False(observation.DetailWasTruncated);
        }
    }

    private static async Task<DocumentationQueryOutcome.Completed>
        ExecuteScriptedAuthoredQueryAsync(
            DocumentationQueryAuthoredCase scenario)
    {
        await using LibraryFixture library =
            await LibraryFixture.CreateAsync();
        DocumentationSubjectReference subject = Subject(library);
        DocumentationHouseRequestIdentity requestIdentity =
            DocumentationHouseRequestIdentity.Create(
                "query-scripted-request");
        DocumentationHouseOperationPlanIdentity planIdentity =
            DocumentationHouseOperationPlanIdentity.Create(
                "query-scripted-plan");
        DocumentationHousePolicyGeneration policy =
            DocumentationHousePolicyGeneration.Create(
                "query-scripted-policy");
        var binding = new DocumentationAuthoredSourceOperationBinding(
            requestIdentity,
            planIdentity,
            policy,
            subject,
            library.Reference.ImplementationAssembly!,
            implementationSubject: null);
        var operation = new DocumentationQueryScriptedOperation(
            invocation => ScriptedOutcome(invocation, scenario));
        DateTimeOffset deadline = DateTimeOffset.UtcNow.AddMinutes(1);
        var operationPlan = new DocumentationHouseOperationPlan(
            planIdentity,
            policy,
            DocumentationQueryHouseLimits(),
            deadline,
            [],
            new(
                binding,
                new(
                    maximumSourceDocuments: 4,
                    maximumSourceBytes: 4_096,
                    CSharpAuthoredDocumentationLimits.Default),
                operation));
        var query = Assert.IsType<
            DocumentationQueryRequestResult.Accepted>(
                DocumentationQuery.ResolveRequest(
                    DocumentationQuery.CreateRequest(
                        DocumentationDemand
                            .AuthoredSourceDocumentation),
                    TestContext.Current.CancellationToken));

        DocumentationQueryResult result =
            await DocumentationQuery.ExecuteAsync(
                query.Plan,
                requestIdentity,
                subject,
                operationPlan,
                library.IssueOperation(),
                TestContext.Current.CancellationToken);

        Assert.Equal(1, operation.InvocationCount);
        return Assert.IsType<DocumentationQueryOutcome.Completed>(
            result.Content);
    }

    private static DocumentationQueryPlan ResolveDocumentationQuery(
        DocumentationDemand demand) =>
        Assert.IsType<DocumentationQueryRequestResult.Accepted>(
                DocumentationQuery.ResolveRequest(
                    DocumentationQuery.CreateRequest(demand),
                    TestContext.Current.CancellationToken))
            .Plan;

    private static DocumentationHouseOperationPlan
        CompiledDocumentationQueryPlan(
            string identity,
            DateTimeOffset deadline) =>
        new(
            DocumentationHouseOperationPlanIdentity.Create(identity),
            DocumentationHousePolicyGeneration.Create("query-policy"),
            DocumentationQueryHouseLimits(),
            deadline,
            compiledXmlContributions: []);

    private static DocumentationAuthoredSourceOperationOutcome
        ScriptedOutcome(
            DocumentationAuthoredSourceOperationInvocation invocation,
            DocumentationQueryAuthoredCase scenario)
    {
        var observation =
            new DocumentationAuthoredSourceOperationObservation(
                "scripted-observation",
                "portable detail");
        DocumentationAuthoredSourceOperationWorkCharge work =
            EmptyDocumentationQueryAuthoredWork();
        DocumentationAuthoredLeaseSettlement settlement =
            new(DocumentationAuthoredLeaseConsumer.Operation);
        return scenario switch
        {
            DocumentationQueryAuthoredCase.Absent =>
                ScriptedAbsent(invocation, work, settlement),
            DocumentationQueryAuthoredCase.SourceUnavailable =>
                new DocumentationAuthoredSourceOperationOutcome.Unavailable(
                    invocation,
                    DocumentationAuthoredUnavailableKind.SourceUnavailable,
                    work,
                    settlement,
                    observation),
            DocumentationQueryAuthoredCase.Ambiguous =>
                new DocumentationAuthoredSourceOperationOutcome.Unavailable(
                    invocation,
                    DocumentationAuthoredUnavailableKind
                        .PhysicalDeclarationConflict,
                    work,
                    settlement,
                    observation),
            DocumentationQueryAuthoredCase.Rejected =>
                new DocumentationAuthoredSourceOperationOutcome.Rejected(
                    invocation,
                    DocumentationAuthoredRejectionKind.AlreadyInvoked,
                    work,
                    settlement,
                    observation),
            DocumentationQueryAuthoredCase.Failed =>
                new DocumentationAuthoredSourceOperationOutcome.Failed(
                    invocation,
                    DocumentationAuthoredFailureKind.SourceFailed,
                    work,
                    settlement,
                    observation),
            DocumentationQueryAuthoredCase.Incomplete =>
                new DocumentationAuthoredSourceOperationOutcome.Incomplete(
                    invocation,
                    DocumentationAuthoredIncompleteBoundary.SourceBytes,
                    work,
                    settlement,
                    observation),
            _ => throw new InvalidOperationException(
                "Unknown authored projection scenario."),
        };
    }

    private static DocumentationAuthoredSourceOperationOutcome.Produced
        ScriptedAbsent(
            DocumentationAuthoredSourceOperationInvocation invocation,
            DocumentationAuthoredSourceOperationWorkCharge work,
            DocumentationAuthoredLeaseSettlement settlement)
    {
        const string declaration = "public static void M() { }";
        const string source = $"class C {{ {declaration} }}";
        CSharpAuthoredDocumentationOutcome documentation =
            CSharpAuthoredDocumentation.Read(
                new(
                    source,
                    new(
                        source.IndexOf(
                            declaration,
                            StringComparison.Ordinal),
                        declaration.Length)));
        Assert.IsType<CSharpAuthoredDocumentationOutcome.Absent>(
            documentation);
        var evidence = new DocumentationAuthoredSourceOperationEvidence(
            DocumentationSourceReference.Create(
                DocumentationSourceKind.SourceHouse,
                "scripted-query-source"),
            new DocumentationQuerySourceEvidence(),
            new DocumentationQueryPhysicalDeclarationEvidence());
        return new(
            invocation,
            new(
                invocation.Binding,
                evidence,
                documentation),
            work with
            {
                SourceBytesObserved = source.Length,
                SourceTextCharactersObserved = source.Length,
                SourceDocumentsObserved = 1,
                AttestationContributionsObserved = 1,
                DocumentationWork = documentation.Work,
            },
            settlement);
    }

    private static DocumentationAuthoredSourceOperationWorkCharge
        EmptyDocumentationQueryAuthoredWork() =>
        new(
            SourceBytesObserved: 0,
            SourceTextCharactersObserved: 0,
            SourceDocumentsObserved: 0,
            AttestationContributionsObserved: 0,
            DocumentationWork: null);

    private static SourceHouseBuildAttestation
        BuildDocumentationQueryAttestation()
    {
        return EmitDocumentationQueryAttestation(
            "DocumentationQueryAuthoredFixture",
            DocumentationQueryBuildSources(),
            "documentation-query-authored");
    }

    private static SourceHouseBuildAttestation
        EmitDocumentationQueryAttestation(
            string assemblyName,
            IReadOnlyList<CSharpBuildSource> sources,
            string generation)
    {
        CSharpBuildAttestationOutcome outcome =
            CSharpBuildAttestor.EmitAndAttest(
                new(
                    assemblyName,
                    sources,
                    DocumentationQueryTrustedPlatformAssemblies(),
                    SourceHouseCapabilityIdentity.Create(
                        "documentation-query-build-attestor"),
                    SourceHouseAttestationIssuerIdentity.Create(
                        "dotnet-inspect-build"),
                    SourceHouseAttestationProfileIdentity.Create(
                        "direct-csharp-emit-v1"),
                    SourceHouseAttestationGeneration.Create(
                        generation)));
        if (outcome is CSharpBuildAttestationOutcome.Failed failed)
        {
            Assert.Fail(
                string.Join(
                    Environment.NewLine,
                    failed.Diagnostics));
        }

        return Assert.IsType<
                CSharpBuildAttestationOutcome.Available>(outcome)
            .Attestation;
    }

    private static int MethodToken(
        ImmutableArray<byte> peImage,
        string methodName)
    {
        using var stream =
            new MemoryStream(peImage.AsSpan().ToArray());
        using var reader = new PEReader(stream);
        MetadataReader metadata = reader.GetMetadataReader();
        MethodDefinitionHandle method =
            Assert.Single(
                metadata.MethodDefinitions,
                handle =>
                    metadata.GetString(
                        metadata.GetMethodDefinition(handle).Name)
                        == methodName);
        return MetadataTokens.GetToken(method);
    }

    private static byte[] DuplicateTargetAssembly(
        string assemblyName)
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            generation: 0,
            metadata.GetOrAddString($"{assemblyName}.dll"),
            metadata.GetOrAddGuid(Guid.NewGuid()),
            encId: default,
            encBaseId: default);
        metadata.AddAssembly(
            metadata.GetOrAddString(assemblyName),
            new Version(0, 0, 0, 0),
            culture: default,
            publicKey: default,
            flags: default,
            hashAlgorithm: default);
        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            baseType: default,
            fieldList: MetadataTokens.FieldDefinitionHandle(1),
            methodList: MetadataTokens.MethodDefinitionHandle(1));
        metadata.AddTypeDefinition(
            TypeAttributes.Public
                | TypeAttributes.Abstract
                | TypeAttributes.Sealed,
            metadata.GetOrAddString(
                "DocumentationQuery.Ambiguous"),
            metadata.GetOrAddString("Subject"),
            baseType: default,
            fieldList: MetadataTokens.FieldDefinitionHandle(1),
            methodList: MetadataTokens.MethodDefinitionHandle(1));

        var signature = new BlobBuilder();
        new BlobEncoder(signature)
            .MethodSignature(isInstanceMethod: false)
            .Parameters(
                parameterCount: 0,
                returnType => returnType.Void(),
                parameters => { });
        BlobHandle signatureHandle =
            metadata.GetOrAddBlob(signature);
        var methodBodies = new BlobBuilder();
        var instructions = new BlobBuilder();
        new InstructionEncoder(instructions)
            .OpCode(ILOpCode.Ret);
        int body = new MethodBodyStreamEncoder(methodBodies)
            .AddMethodBody(
                new InstructionEncoder(instructions),
                maxStack: 0);
        for (int index = 0; index < 2; index++)
        {
            metadata.AddMethodDefinition(
                MethodAttributes.Public | MethodAttributes.Static,
                MethodImplAttributes.IL,
                metadata.GetOrAddString("Target"),
                signatureHandle,
                body,
                MetadataTokens.ParameterHandle(1));
        }

        var pe = new ManagedPEBuilder(
            PEHeaderBuilder.CreateLibraryHeader(),
            new MetadataRootBuilder(
                metadata,
                suppressValidation: true),
            methodBodies,
            flags: CorFlags.ILOnly);
        var image = new BlobBuilder();
        pe.Serialize(image);
        return image.ToArray();
    }

    private static CSharpBuildSource[] DocumentationQueryBuildSources()
    {
        string directory = Path.Combine(
            RepositoryRootForDocumentationQuery(),
            "src",
            "CSharpText.MemberSlicing");
        return
        [
            .. Directory.EnumerateFiles(
                    directory,
                    "*.cs",
                    SearchOption.TopDirectoryOnly)
                .Order(StringComparer.Ordinal)
                .Select(
                    static path =>
                        new CSharpBuildSource(
                            path,
                            File.ReadAllBytes(path))),
        ];
    }

    private static string RepositoryRootForDocumentationQuery()
    {
        for (DirectoryInfo? directory =
                new(AppContext.BaseDirectory);
            directory is not null;
            directory = directory.Parent)
        {
            if (File.Exists(
                    Path.Combine(
                        directory.FullName,
                        "dotnet-inspect.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException(
            "Could not locate the repository root.");
    }

    private static string[]
        DocumentationQueryTrustedPlatformAssemblies()
    {
        string runtimeDirectory =
            Path.GetDirectoryName(typeof(object).Assembly.Location)
            ?? throw new InvalidOperationException(
                "The runtime assembly has no directory.");
        return
        [
            .. Directory.EnumerateFiles(
                runtimeDirectory,
                "*.dll",
                SearchOption.TopDirectoryOnly),
            typeof(CSharpSourceText).Assembly.Location,
        ];
    }

    private static DocumentationHouseLimits DocumentationQueryHouseLimits() =>
        new(
            maximumCompiledXmlContributions: 8,
            maximumCompiledXmlBytes: 8 * 1024 * 1024,
            XmlDocumentationReadLimits.Default);

    private static SourceHouseLimits DocumentationQuerySourceLimits() =>
        new(
            maximumAssemblyBytes: 16 * 1024 * 1024,
            maximumPortablePdbBytes: 16 * 1024 * 1024,
            targetBounds: s_documentationQueryApiSurfaceBounds,
            sourceLinkReadLimits: new(
                maxEmbeddedPdbBytes: 16 * 1024 * 1024,
                maxMapBytes: 4 * 1024 * 1024,
                maxMappings: 10_000),
            maximumDocuments: 1_000,
            maximumTargetMappings: 1_000,
            maximumCandidateAttempts: 10,
            maximumSourceBytes: 1024 * 1024,
            maximumSourceTextCharacters: 1024 * 1024);

    private static CompiledXmlContribution QueryCandidate(
        LibraryFixture library,
        DocumentationSubjectReference subject,
        int xmlIndex) =>
        CompiledXmlContribution.Candidate(
            subject,
            library.Reference,
            library.Reference.ApiAssembly,
            DocumentationSourceReference.Create(
                DocumentationSourceKind.DirectLibrary,
                $"documentation-query-xml-{xmlIndex}"),
            library.XmlContents[xmlIndex]);

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
                && identity.Value
                    == DocumentationQueryDeserializeIdentity);
        return DocumentationSubjectReference.ForMember(
            correspondence,
            type,
            member);
    }

    private static byte[] QueryXml(string identity, string summary) =>
        Encoding.UTF8.GetBytes(
            $"""
             <?xml version="1.0"?>
             <doc>
               <members>
                 <member name="{identity}">
                   <summary>{summary}</summary>
                 </member>
               </members>
             </doc>
             """);

    private sealed record DocumentationQueryAuthoredScenario(
        DocumentationAuthoredSourceOperationBinding Binding,
        SourceHouseAuthoredRequest SourceRequest,
        DocumentationAuthoredSourceOperationInvocation Invocation)
    {
        internal static DocumentationQueryAuthoredScenario Create(
            LibraryFixture library,
            ISourceHousePhysicalDeclarationCapability capability)
        {
            LibraryApiSurfaceCorrespondence correspondence =
                library.ApiSurfaceCorrespondence;
            ApiType type = Assert.Single(
                correspondence.Surface.Types,
                candidate =>
                    candidate.FullName
                        == "CSharpText.MemberSlicing.MemberTextSlicer");
            ApiMember member = Assert.Single(
                type.Members,
                candidate =>
                    candidate.Name == "ExtractMemberText"
                    && candidate.MetadataToken is not null);
            DocumentationSubjectReference subject =
                DocumentationSubjectReference.ForMember(
                    correspondence,
                    type,
                    member);
            DateTimeOffset deadline =
                DateTimeOffset.UtcNow.AddMinutes(1);
            var sourceRequest = new SourceHouseAuthoredRequest(
                SourceHouseRequestIdentity.Create(
                    "documentation-query-authored-source"),
                library.Reference,
                library.Reference.ImplementationAssembly!,
                new SourceHouseTarget.MemberTarget(
                    type.DefinitionName!,
                    ApiMemberIdentity.GetMemberAnchor(type, member),
                    member.MetadataToken!.Value,
                    SourceHouseMemberSourceForm.DocumentParts),
                new SourceHouseOperationPlan(
                    SourceHouseOperationPlanIdentity.Create(
                        "documentation-query-source-plan"),
                    SourceHousePolicyGeneration.Create(
                        "documentation-query-source-policy"),
                    DocumentationQuerySourceLimits(),
                    deadline,
                    [capability]));
            var binding = new DocumentationAuthoredSourceOperationBinding(
                DocumentationHouseRequestIdentity.Create(
                    "documentation-query-request"),
                DocumentationHouseOperationPlanIdentity.Create(
                    "documentation-query-plan"),
                DocumentationHousePolicyGeneration.Create(
                    "documentation-query-policy"),
                subject,
                library.Reference.ImplementationAssembly!,
                DocumentationImplementationSubjectReference
                    .FromApiSubject(subject));
            var invocation =
                new DocumentationAuthoredSourceOperationInvocation(
                    binding,
                    new(
                        maximumSourceDocuments: 1_000,
                        maximumSourceBytes: 1024 * 1024,
                        CSharpAuthoredDocumentationLimits.Default),
                    deadline);
            return new(binding, sourceRequest, invocation);
        }
    }

    private sealed class DocumentationQueryAttestationCapability(
        SourceHouseBuildAttestation inner)
        : ISourceHousePhysicalDeclarationCapability
    {
        public int SourceReads { get; private set; }
        public int AttestationReads { get; private set; }
        public SourceHouseCapabilityIdentity Identity => inner.Identity;
        public SourceHouseCapabilityCategory Category => inner.Category;
        public SourceHouseAttestationIssuerIdentity Issuer => inner.Issuer;
        public SourceHouseAttestationProfileIdentity Profile => inner.Profile;
        public SourceHouseAttestationGeneration Generation =>
            inner.Generation;

        public ValueTask<SourceHouseCapabilityOutcome> ReadAsync(
            SourceHouseSourceCandidate candidate,
            int maximumBytes,
            CancellationToken cancellationToken)
        {
            SourceReads++;
            return inner.ReadAsync(
                candidate,
                maximumBytes,
                cancellationToken);
        }

        public ValueTask<SourceHouseAttestationCapabilityOutcome>
            ReadAttestationsAsync(
                SourceHousePhysicalDeclarationRequest request,
                int maximumContributions,
                CancellationToken cancellationToken)
        {
            AttestationReads++;
            return inner.ReadAttestationsAsync(
                request,
                maximumContributions,
                cancellationToken);
        }
    }

    public enum DocumentationQueryAuthoredCase
    {
        Absent,
        SourceUnavailable,
        Ambiguous,
        Rejected,
        Failed,
        Incomplete,
    }

    private sealed class DocumentationQueryScriptedOperation(
        Func<
            DocumentationAuthoredSourceOperationInvocation,
            DocumentationAuthoredSourceOperationOutcome> outcome)
        : IDocumentationAuthoredSourceOperation
    {
        private int _invocationCount;

        public int InvocationCount => Volatile.Read(ref _invocationCount);

        public ValueTask<DocumentationAuthoredSourceOperationOutcome>
            InvokeAsync(
                DocumentationAuthoredSourceOperationInvocation invocation,
                LibraryOperationLease operationLease,
                CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Interlocked.Increment(ref _invocationCount);
            operationLease.Dispose();
            return ValueTask.FromResult(outcome(invocation));
        }
    }

    private sealed class DocumentationQuerySourceEvidence
        : DocumentationAuthoredSourceEvidenceReference;

    private sealed class DocumentationQueryPhysicalDeclarationEvidence
        : DocumentationPhysicalDeclarationEvidenceReference;

}

public sealed partial class CompiledDocumentationQueryTests
{
    internal sealed partial class LibraryFixture
    {
        public IReadOnlyList<LibraryContentReference> XmlContents =>
            Reference.Contents
                .Where(
                    static content =>
                        content.HasRole(
                            LibraryContentRole
                                .CompiledXmlDocumentation))
                .ToArray();

        public static async Task<LibraryFixture> CreateSourceAsync(
            byte[] assembly,
            byte[] portablePdb,
            byte[]? compiledXml = null)
        {
            byte[][] contents = compiledXml is null
                ? [assembly, portablePdb]
                : [assembly, portablePdb, compiledXml];
            ArtifactFixture artifacts =
                await ArtifactFixture.CreateAsync(contents);
            try
            {
                ManagedMetadataIdentity.Assembly identity =
                    AssemblyIdentity(assembly);
                var companions =
                    new List<LibraryCompanionCorrespondence>
                    {
                        new(
                            artifacts[1],
                            LibraryContentRole.PortablePdb,
                            artifacts[0]),
                    };
                if (compiledXml is not null)
                {
                    companions.Add(
                        new(
                            artifacts[2],
                            LibraryContentRole
                                .CompiledXmlDocumentation,
                            artifacts[0]));
                }
                LibraryReference reference =
                    LibraryReference.CreateDirect(
                        new LibraryAssemblyCorrespondence(
                            artifacts[0],
                            identity,
                            artifacts[0],
                            identity),
                        companions);
                var owner = new LibraryContentOwner(
                    reference,
                    artifacts.IssueContentLeases());
                return new LibraryFixture(
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

        public static async Task<LibraryFixture> CreateSplitSourceAsync(
            byte[] apiAssembly,
            byte[] implementationAssembly,
            byte[] portablePdb)
        {
            ArtifactFixture artifacts =
                await ArtifactFixture.CreateAsync(
                    [
                        apiAssembly,
                        implementationAssembly,
                        portablePdb,
                    ]);
            try
            {
                ManagedMetadataIdentity.Assembly apiIdentity =
                    AssemblyIdentity(apiAssembly);
                ManagedMetadataIdentity.Assembly implementationIdentity =
                    AssemblyIdentity(implementationAssembly);
                LibraryReference reference =
                    LibraryReference.CreateDirect(
                        new LibraryAssemblyCorrespondence(
                            artifacts[0],
                            apiIdentity,
                            artifacts[1],
                            implementationIdentity),
                        [
                            new(
                                artifacts[2],
                                LibraryContentRole.PortablePdb,
                                artifacts[1]),
                        ]);
                var owner = new LibraryContentOwner(
                    reference,
                    artifacts.IssueContentLeases());
                return new LibraryFixture(
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
    }
}
