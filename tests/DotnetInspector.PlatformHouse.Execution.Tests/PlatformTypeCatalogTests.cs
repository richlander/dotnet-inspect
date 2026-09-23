using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Text.Json;
using DotnetInspector.DocumentationHouse;
using DotnetInspector.LibraryMetadata;
using DotnetInspector.Libraries;
using DotnetInspector.PlatformQueries;
using DotnetInspector.Queries;
using DotnetInspector.Sections;
using ILInspector.Metadata;
using QuerySpace;
using QuerySpace.Composition;
using QuerySpace.Operations;

namespace DotnetInspector.PlatformHouse.Tests;

public partial class PlatformLibraryRealizationTests
{
    private static readonly PlatformTypeCatalogDerivationBounds
        s_catalogBounds = new(
            new LibraryTypeDeclarationInventoryInspectionBounds(
                maximumAssemblyBytes: 16 * 1024 * 1024,
                maximumRetainedDeclarations: 100_000,
                maximumMetadataRows: int.MaxValue,
                maximumRetainedTextCharacters: int.MaxValue),
            maximumAssemblies: 256,
            maximumAggregateAssemblyBytes: 256 * 1024 * 1024,
            maximumRetainedEntries: 500_000,
            maximumDuration: TimeSpan.FromSeconds(30));

    [Fact]
    public async Task
        TypeCatalog_IndexesRealDefinitionsForwardersAndExactPopulation()
    {
        CancellationToken cancellationToken =
            TestContext.Current.CancellationToken;
        var request = PopulationRequest(cancellationToken);
        PlatformSourceContribution.Realization contribution =
            PopulationContribution(
                request.Request,
                request.Reference);
        string systemTextJson = RealCatalogAsset("System.Text.Json.dll");
        string netstandard = RealCatalogAsset("netstandard.dll");
        await using ArtifactFixture artifacts =
            await ArtifactFixture.CreatePopulationAsync(
                (contribution, systemTextJson),
                (contribution, netstandard));
        PlatformPopulationRealizationResult.Completed population =
            await RealizeCatalogPopulationAsync(
                request.Request,
                artifacts,
                count: 2);
        bool retired = false;
        try
        {
            var completed = Assert.IsType<
                PlatformTypeCatalogDerivationOutcome.Completed>(
                    PlatformTypeCatalogDerivation.Execute(
                        population,
                        s_catalogBounds,
                        cancellationToken));
            PlatformTypeCatalog catalog = completed.Catalog;

            Assert.True(
                catalog.Work.Elapsed
                    < s_catalogBounds.MaximumDuration);
            Assert.Same(population.Value, catalog.Population);
            Assert.Same(population.Receipt, catalog.PopulationReceipt);
            Assert.Same(
                request.Request.Snapshot,
                catalog.PopulationReceipt.HouseReceipt.Request);
            Assert.Equal(
                ((PlatformTargetDemand.Exact)request.Request.Target).Target,
                catalog.Target);
            Assert.Equal(PlatformViewDemand.Reference, catalog.View);
            Assert.Equal(2, catalog.Work.ObservedAssemblies);
            Assert.Equal(
                new FileInfo(systemTextJson).Length
                    + new FileInfo(netstandard).Length,
                catalog.Work.ObservedAssemblyBytes);
            Assert.Equal(
                catalog.Entries.Length,
                catalog.Work.ObservedEntries);
            Assert.Contains(
                catalog.PopulationReceipt.HouseReceipt.SourceSettlements,
                settlement =>
                    settlement.Contribution.Generation.Name
                        == "reference-population-generation");

            PlatformTypeCatalogEntry jsonSerializer = Assert.Single(
                catalog.Entries,
                entry =>
                    ReferenceEquals(
                        entry.Member,
                        population.Value.Members[0])
                    && entry.Name
                        == Name(
                            "System.Text.Json",
                            "JsonSerializer")
                    && entry.Kind
                        == AssemblyTypeDeclarationKind.Definition);
            Assert.Same(
                population.Value.Members[0].Library.ApiAssembly,
                jsonSerializer.ApiContent);
            Assert.NotEqual(Guid.Empty, jsonSerializer.ModuleVersionId);

            PlatformTypeCatalogEntry systemObject = Assert.Single(
                catalog.Entries,
                entry =>
                    ReferenceEquals(
                        entry.Member,
                        population.Value.Members[1])
                    && entry.Name == Name("System", "Object"));
            Assert.Equal(
                AssemblyTypeDeclarationKind.Forwarder,
                systemObject.Kind);
            Assert.Same(
                jsonSerializer,
                Assert.IsType<
                        PlatformTypeCatalogQueryOutcome.Resolved>(
                        PlatformTypeCatalogQuery.Execute(
                            catalog,
                            "System.Text.Json.JsonSerializer",
                            cancellationToken))
                    .Candidate);
            Assert.Same(
                systemObject,
                Assert.IsType<
                        PlatformTypeCatalogQueryOutcome.Resolved>(
                        PlatformTypeCatalogQuery.Execute(
                            catalog,
                            "System.Object",
                            cancellationToken))
                    .Candidate);
            Assert.IsType<PlatformTypeCatalogQueryOutcome.Missing>(
                PlatformTypeCatalogQuery.Execute(
                    catalog,
                    "System.Text.Json.Serialization.Metadata.JsonTypeInfo<TFirst, TSecond>",
                    cancellationToken));
            Assert.Equal(
                PlatformTypeCatalogQueryRejectionKind.EmptyPattern,
                Assert.IsType<
                        PlatformTypeCatalogQueryOutcome.Rejected>(
                        PlatformTypeCatalogQuery.Execute(
                            catalog,
                            " ",
                            cancellationToken))
                    .Kind);
            Assert.Equal(
                PlatformTypeCatalogQueryRejectionKind.PatternTooLong,
                Assert.IsType<
                        PlatformTypeCatalogQueryOutcome.Rejected>(
                        PlatformTypeCatalogQuery.Execute(
                            catalog,
                            new string(
                                'T',
                                MetadataSafetyPolicy
                                    .MaxTypeNameCharacters
                                    + 1),
                            cancellationToken))
                    .Kind);
            Assert.Same(
                jsonSerializer,
                Assert.Single(
                    Assert.IsType<
                            PlatformTypeCatalogLookupOutcome.Found>(
                            catalog.Lookup(jsonSerializer.Name))
                        .Candidates));
            Assert.IsType<PlatformTypeCatalogLookupOutcome.Missing>(
                catalog.Lookup(Name("Missing", "Type")));

            AssertResourceFree(typeof(PlatformTypeCatalog));
            AssertResourceFree(typeof(PlatformTypeCatalogEntry));
            AssertResourceFree(
                typeof(PlatformTypeCatalogDerivationOutcome.Completed));
            AssertResourceFree(
                typeof(PlatformTypeCatalogLookupOutcome.Found));
            AssertResourceFree(
                typeof(PlatformTypeCatalogQueryOutcome.Resolved));
            AssertResourceFree(
                typeof(PlatformTypeCatalogQueryOutcome.Ambiguous));
            AssertResourceFree(
                typeof(PlatformTypeCatalogQueryOutcome.Missing));
            AssertResourceFree(
                typeof(PlatformTypeCatalogQueryOutcome.Rejected));
            AssertResourceFree(typeof(PlatformTypeCatalogQueryPlan));
            AssertResourceFree(typeof(PlatformTypeCatalogRouteRequest));
            AssertResourceFree(typeof(PlatformTypeCatalogRouteTarget));
            AssertResourceFree(typeof(PlatformTypeCatalogRouteCandidate));
            AssertResourceFree(
                typeof(PlatformTypeCatalogRouteOutcome.Resolved));
            AssertResourceFree(
                typeof(
                    InspectionEnvelope<
                        PlatformTypeCatalogRouteOutcome>));
            Assert.Throws<ArgumentException>(
                () => new PlatformTypeCatalogRouteRequest(
                    "JsonSerializer.Serialize",
                    "JsonSerializer",
                    "Deserialize"));

            await RetireCatalogPopulationAsync(
                population,
                artifacts,
                cancellationToken);
            retired = true;
            InspectionEnvelope<PlatformTypeCatalogRouteOutcome> route =
                PlatformTypeCatalogRouteInspection.Execute(
                    catalog,
                    new(
                        "JsonSerializer.Serialize",
                        "JsonSerializer",
                        "Serialize"),
                    cancellationToken);
            var resolvedRoute = Assert.IsType<
                PlatformTypeCatalogRouteOutcome.Resolved>(
                    route.Content);
            Assert.Equal(
                "JsonSerializer.Serialize",
                resolvedRoute.Request.OriginalQuery);
            Assert.Equal(
                "JsonSerializer",
                resolvedRoute.Request.TypePattern);
            Assert.Equal(
                PlatformTypeCatalogRouteTargetKind.Member,
                resolvedRoute.Request.TargetKind);
            Assert.Equal(
                "Serialize",
                resolvedRoute.Request.MemberSelector);
            Assert.Equal(jsonSerializer.Name, resolvedRoute.Candidate.Type);
            Assert.Equal(
                jsonSerializer.ApiContent.AssemblyIdentity!.Identity,
                resolvedRoute.Candidate.Assembly.Identity);
            Assert.Equal(
                jsonSerializer.ModuleVersionId,
                resolvedRoute.Candidate.Assembly.ModuleVersionId);
            Assert.Equal(
                jsonSerializer.Kind,
                resolvedRoute.Candidate.DeclarationKind);
            Assert.Equal(
                catalog.Target.Family,
                resolvedRoute.Target.Family);
            Assert.Equal(
                catalog.Target.TargetFramework.ToString(),
                resolvedRoute.Target.TargetFramework);
            Assert.Equal(
                catalog.Target.Version.Value,
                resolvedRoute.Target.Version);
            Assert.Empty(route.Diagnostics);
            Assert.IsType<InspectionShare.NonProjectable>(route.Share);

            string json = JsonSerializer.Serialize(
                route,
                PlatformTypeCatalogRouteInspectionJsonContext.Default
                    .PlatformTypeCatalogRouteInspectionEnvelope);
            InspectionEnvelope<PlatformTypeCatalogRouteOutcome>? roundTrip =
                JsonSerializer.Deserialize(
                    json,
                    PlatformTypeCatalogRouteInspectionJsonContext.Default
                        .PlatformTypeCatalogRouteInspectionEnvelope);
            Assert.NotNull(roundTrip);
            var roundTripResolved = Assert.IsType<
                PlatformTypeCatalogRouteOutcome.Resolved>(
                    roundTrip.Content);
            Assert.Equal(
                resolvedRoute.Request,
                roundTripResolved.Request);
            Assert.Equal(
                resolvedRoute.Target,
                roundTripResolved.Target);
            Assert.Equal(
                resolvedRoute.Candidate,
                roundTripResolved.Candidate);

            Assert.Same(
                jsonSerializer,
                Assert.Single(
                    Assert.IsType<
                            PlatformTypeCatalogLookupOutcome.Found>(
                            catalog.Lookup(jsonSerializer.Name))
                        .Candidates));
            Assert.Same(
                jsonSerializer,
                Assert.IsType<
                        PlatformTypeCatalogQueryOutcome.Resolved>(
                        PlatformTypeCatalogQuery.Execute(
                            catalog,
                            "JsonSerializer",
                            cancellationToken))
                    .Candidate);
        }
        finally
        {
            if (!retired)
            {
                await RetireCatalogPopulationAsync(
                    population,
                    artifacts,
                    cancellationToken);
            }
        }
    }

    [Fact]
    public void TypeCatalogQuery_ExposesExactQuerySpaceContract()
    {
        QuerySpaceDescriptor descriptor =
            PlatformTypeCatalogQuery.QuerySpace.Descriptor;

        Assert.Equal(
            PlatformTypeCatalogQuery.QuerySpaceIdentity,
            descriptor.Identity);
        Assert.Equal(
            PlatformTypeCatalogQuery.OperationIdentity,
            descriptor.Operation.Operation);
        Assert.Equal(
            PlatformTypeCatalogQuery.OperationRouteIdentity,
            descriptor.Operation.Identity);
        Assert.Equal(
            PlatformTypeCatalogQuery.OperationSubjectRole,
            descriptor.Operation.SubjectRole);
        Assert.Equal(
            PlatformTypeCatalogQuery.OperationResultGrain,
            descriptor.Operation.ResultGrain);
        Assert.Equal(
            PlatformTypeCatalogQuery.OperationProfileIdentity,
            descriptor.Operation.Profile);
        Assert.Equal(
            PlatformTypeCatalogQuery.OperationVocabularyIdentity,
            descriptor.Operation.QueryVocabulary);
        Assert.Equal(
            [PlatformTypeCatalogQuery.DeclarationsRowSet],
            descriptor.Operation.RowSets);
        QuerySpaceOperationTermDescriptor term =
            Assert.Single(descriptor.Operation.Terms);
        Assert.Equal(
            PlatformTypeCatalogQuery.PatternTermKey,
            term.Key);
        Assert.Equal(
            QueryOperationTermRole.OperationSelector,
            term.Role);
        Assert.Equal([PortableQueryOperator.Equal], term.Operators);
        Assert.Empty(descriptor.Operation.Dimensions);

        QuerySpaceRowScopeDescriptor rowScope =
            Assert.Single(descriptor.RowScopes);
        Assert.Equal(
            PlatformTypeCatalogQuery.DeclarationRowScopeIdentity,
            rowScope.Identity);
        Assert.Equal(
            PlatformTypeCatalogQuery.DeclarationRowVocabularyIdentity,
            rowScope.QueryVocabulary);
        Assert.Equal(
            [PlatformTypeCatalogQuery.DeclarationsRowSet],
            rowScope.RowSets);
        Assert.Empty(rowScope.Facets);
        Assert.Empty(rowScope.Orders);
        Assert.Empty(rowScope.Stages);
        Assert.Equal(
            [QuerySpaceTerminalRequirement.Rows],
            descriptor.Terminals);
        Assert.False(descriptor.AcceptsContinuation);
        QuerySpaceResultContractDescriptor resultContract =
            Assert.Single(descriptor.ResultContracts);
        Assert.Equal(
            QuerySpaceTerminalRequirement.Rows,
            resultContract.Terminal);
        Assert.Equal(
            PlatformTypeCatalogQuery.ResultContractIdentity,
            resultContract.Identity);

        QuerySpaceRequest request =
            PlatformTypeCatalogQuery.CreateRequest(
                " Nested.Outer<T>.Inner<TKey, TValue> ");
        var accepted = Assert.IsType<
            PlatformTypeCatalogQueryRequestResult.Accepted>(
                PlatformTypeCatalogQuery.ResolveRequest(
                    request,
                    TestContext.Current.CancellationToken));
        Assert.Same(request, accepted.Plan.Request);
        Assert.Equal(
            " Nested.Outer<T>.Inner<TKey, TValue> ",
            accepted.Plan.Pattern);
        Assert.Equal(
            "Nested.Outer`1.Inner`2",
            accepted.Plan.NormalizedPattern);
        Assert.True(accepted.Plan.HasExplicitGenericNotation);
        Assert.Equal(
            accepted.Plan.Pattern,
            Assert.Single(accepted.Plan.Intent.Terms).Value);
    }

    [Fact]
    public void
        TypeCatalogQuery_RejectsMalformedStructuralRequestsBeforeExecution()
    {
        CancellationToken cancellationToken =
            TestContext.Current.CancellationToken;
        QuerySpaceDescriptor descriptor =
            PlatformTypeCatalogQuery.QuerySpace.Descriptor;
        PortableQueryIntent emptyIntent =
            PortableQueryIntent.Create([], [], [], []);

        QuerySpaceRequest missingPattern = QuerySpaceRequest.Create(
            descriptor,
            emptyIntent,
            [PlatformTypeCatalogQuery.DeclarationsRowSet],
            [],
            QuerySpaceTerminalRequirement.Rows);
        Assert.Equal(
            PortableQueryFailureReason.RequiredTermFamilyMissing,
            Assert.IsType<
                    PlatformTypeCatalogQueryRequestResult.IntentRejected>(
                    PlatformTypeCatalogQuery.ResolveRequest(
                        missingPattern,
                        cancellationToken))
                .Failure.Reason);

        QuerySpaceRequest duplicatePattern = QuerySpaceRequest.Create(
            descriptor,
            PortableQueryIntent.Create(
                [
                    new(
                        PlatformTypeCatalogQuery.PatternTermKey,
                        PortableQueryOperator.Equal,
                        "System.String"),
                    new(
                        PlatformTypeCatalogQuery.PatternTermKey,
                        PortableQueryOperator.Equal,
                        "System.Object"),
                ],
                [],
                [],
                []),
            [PlatformTypeCatalogQuery.DeclarationsRowSet],
            [],
            QuerySpaceTerminalRequirement.Rows);
        Assert.Equal(
            PortableQueryFailureReason.TermsIncompatible,
            Assert.IsType<
                    PlatformTypeCatalogQueryRequestResult.IntentRejected>(
                    PlatformTypeCatalogQuery.ResolveRequest(
                        duplicatePattern,
                        cancellationToken))
                .Failure.Reason);

        QuerySpaceRequest invalidPattern =
            PlatformTypeCatalogQuery.CreateRequest(" ");
        Assert.Equal(
            PortableQueryFailureReason.ValueRejected,
            Assert.IsType<
                    PlatformTypeCatalogQueryRequestResult.IntentRejected>(
                    PlatformTypeCatalogQuery.ResolveRequest(
                        invalidPattern,
                        cancellationToken))
                .Failure.Reason);

        QuerySpaceRequest rowIntent = QuerySpaceRequest.Create(
            descriptor,
            PlatformTypeCatalogQuery.CreateRequest(
                "System.String").Operation,
            [PlatformTypeCatalogQuery.DeclarationsRowSet],
            [
                new(
                    PlatformTypeCatalogQuery.DeclarationRowScopeIdentity,
                    emptyIntent,
                    [PlatformTypeCatalogQuery.DeclarationsRowSet]),
            ],
            QuerySpaceTerminalRequirement.Rows);
        Assert.Equal(
            PlatformTypeCatalogQueryRequestRejectionKind
                .RowIntentNotSupported,
            Assert.IsType<
                    PlatformTypeCatalogQueryRequestResult.Rejected>(
                    PlatformTypeCatalogQuery.ResolveRequest(
                        rowIntent,
                        cancellationToken))
                .Kind);

        QuerySpaceRequest foreign =
            DocumentationQuery.CreateRequest(
                DocumentationDemand.CompiledXml);
        Assert.Equal(
            PlatformTypeCatalogQueryRequestRejectionKind.QuerySpaceMismatch,
            Assert.IsType<
                    PlatformTypeCatalogQueryRequestResult.Rejected>(
                    PlatformTypeCatalogQuery.ResolveRequest(
                        foreign,
                        cancellationToken))
                .Kind);

        QuerySpaceBinding foreignRows = QuerySpaceBinding.Create(
            PlatformTypeCatalogQuery.QuerySpaceIdentity,
            DocumentationQuery.OperationRoute,
            [DocumentationQuery.DocumentationRowScope],
            [QuerySpaceTerminalRequirement.Rows],
            acceptsContinuation: false,
            [
                new(
                    QuerySpaceTerminalRequirement.Rows,
                    PlatformTypeCatalogQuery.ResultContractIdentity),
            ]);
        QuerySpaceRequest wrongRows = QuerySpaceRequest.Create(
            foreignRows.Descriptor,
            DocumentationQuery.CreateIntent(
                DocumentationDemand.CompiledXml),
            [DocumentationQuery.DocumentationRowSet],
            [],
            QuerySpaceTerminalRequirement.Rows);
        Assert.Equal(
            PlatformTypeCatalogQueryRequestRejectionKind
                .ParticipatingRowSetsMismatch,
            Assert.IsType<
                    PlatformTypeCatalogQueryRequestResult.Rejected>(
                    PlatformTypeCatalogQuery.ResolveRequest(
                        wrongRows,
                        cancellationToken))
                .Kind);

        QuerySpaceBinding countBinding = QuerySpaceBinding.Create(
            PlatformTypeCatalogQuery.QuerySpaceIdentity,
            PlatformTypeCatalogQuery.OperationRoute,
            [PlatformTypeCatalogQuery.DeclarationRowScope],
            [
                QuerySpaceTerminalRequirement.Rows,
                QuerySpaceTerminalRequirement.Count,
            ],
            acceptsContinuation: false,
            [
                new(
                    QuerySpaceTerminalRequirement.Rows,
                    PlatformTypeCatalogQuery.ResultContractIdentity),
                new(
                    QuerySpaceTerminalRequirement.Count,
                    "platform-type-catalog-query/count/v1"),
            ]);
        QuerySpaceRequest countRequest = QuerySpaceRequest.Create(
            countBinding.Descriptor,
            PlatformTypeCatalogQuery.CreateRequest(
                "System.String").Operation,
            [PlatformTypeCatalogQuery.DeclarationsRowSet],
            [],
            QuerySpaceTerminalRequirement.Count);
        Assert.Equal(
            PlatformTypeCatalogQueryRequestRejectionKind.TerminalMismatch,
            Assert.IsType<
                    PlatformTypeCatalogQueryRequestResult.Rejected>(
                    PlatformTypeCatalogQuery.ResolveRequest(
                        countRequest,
                        cancellationToken))
                .Kind);

        QuerySpaceBinding wrongResultBinding = QuerySpaceBinding.Create(
            PlatformTypeCatalogQuery.QuerySpaceIdentity,
            PlatformTypeCatalogQuery.OperationRoute,
            [PlatformTypeCatalogQuery.DeclarationRowScope],
            [QuerySpaceTerminalRequirement.Rows],
            acceptsContinuation: false,
            [
                new(
                    QuerySpaceTerminalRequirement.Rows,
                    "platform-type-catalog-query/foreign-outcome/v1"),
            ]);
        QuerySpaceRequest wrongResult = QuerySpaceRequest.Create(
            wrongResultBinding.Descriptor,
            PlatformTypeCatalogQuery.CreateRequest(
                "System.String").Operation,
            [PlatformTypeCatalogQuery.DeclarationsRowSet],
            [],
            QuerySpaceTerminalRequirement.Rows);
        Assert.Equal(
            PlatformTypeCatalogQueryRequestRejectionKind
                .ResultContractMismatch,
            Assert.IsType<
                    PlatformTypeCatalogQueryRequestResult.Rejected>(
                    PlatformTypeCatalogQuery.ResolveRequest(
                        wrongResult,
                        cancellationToken))
                .Kind);
    }

    [Fact]
    public async Task
        TypeCatalog_RetainsDuplicateCandidatesAndModuleExports()
    {
        CancellationToken cancellationToken =
            TestContext.Current.CancellationToken;
        var request = PopulationRequest(cancellationToken);
        PlatformSourceContribution.Realization contribution =
            PopulationContribution(
                request.Request,
                request.Reference);
        byte[] first = BuildCatalogImage(
            "First",
            metadata => AddDefinition(
                metadata,
                TypeAttributes.Public,
                "Shared",
                "Widget"));
        byte[] second = BuildCatalogImage(
            "Second",
            metadata =>
            {
                AddDefinition(
                    metadata,
                    TypeAttributes.Public,
                    "Shared",
                    "Widget");
                AssemblyFileHandle module = metadata.AddAssemblyFile(
                    metadata.GetOrAddString("Part.netmodule"),
                    default,
                    containsMetadata: true);
                metadata.AddExportedType(
                    TypeAttributes.Public,
                    metadata.GetOrAddString("External"),
                    metadata.GetOrAddString("Exported"),
                    module,
                    typeDefinitionId: 1);
            });
        await using ArtifactFixture artifacts =
            await ArtifactFixture.CreatePopulationImagesAsync(
                (contribution, first),
                (contribution, second));
        PlatformPopulationRealizationResult.Completed population =
            await RealizeCatalogPopulationAsync(
                request.Request,
                artifacts,
                count: 2);
        try
        {
            PlatformTypeCatalog catalog = Assert.IsType<
                    PlatformTypeCatalogDerivationOutcome.Completed>(
                    PlatformTypeCatalogDerivation.Execute(
                        population,
                        s_catalogBounds,
                        cancellationToken))
                .Catalog;

            Assert.True(
                catalog.Work.Elapsed
                    < s_catalogBounds.MaximumDuration);
            var duplicates = Assert.IsType<
                    PlatformTypeCatalogLookupOutcome.Found>(
                    catalog.Lookup(Name("Shared", "Widget")))
                .Candidates;
            Assert.Equal(2, duplicates.Length);
            Assert.Same(
                population.Value.Members[0],
                duplicates[0].Member);
            Assert.Same(
                population.Value.Members[1],
                duplicates[1].Member);
            Assert.NotSame(
                duplicates[0].ApiContent.Artifact,
                duplicates[1].ApiContent.Artifact);
            PlatformTypeCatalogQueryOutcome.Ambiguous ambiguity =
                Assert.IsType<
                    PlatformTypeCatalogQueryOutcome.Ambiguous>(
                    PlatformTypeCatalogQuery.Execute(
                        catalog,
                        "Shared.Widget",
                        cancellationToken));
            Assert.Same(catalog, ambiguity.Catalog);
            Assert.Equal(duplicates, ambiguity.Candidates);

            PlatformTypeCatalogEntry moduleExport = Assert.Single(
                Assert.IsType<
                        PlatformTypeCatalogLookupOutcome.Found>(
                        catalog.Lookup(
                            Name("External", "Exported")))
                    .Candidates);
            Assert.Equal(
                AssemblyTypeDeclarationKind.ModuleExport,
                moduleExport.Kind);
            Assert.Same(
                moduleExport,
                Assert.IsType<
                        PlatformTypeCatalogQueryOutcome.Resolved>(
                        PlatformTypeCatalogQuery.Execute(
                            catalog,
                            "External.Exported",
                            cancellationToken))
                    .Candidate);
            Assert.Same(
                population.Value.Members[1],
                moduleExport.Member);
        }
        finally
        {
            await RetireCatalogPopulationAsync(
                population,
                artifacts,
                cancellationToken);
        }
    }

    [Fact]
    public async Task
        TypeCatalogQuery_PrefersDefinitionsAndPreservesNestedGenericArity()
    {
        CancellationToken cancellationToken =
            TestContext.Current.CancellationToken;
        var request = PopulationRequest(cancellationToken);
        PlatformSourceContribution.Realization contribution =
            PopulationContribution(
                request.Request,
                request.Reference);
        byte[] first = BuildCatalogImage(
            "Definitions",
            metadata =>
            {
                AddDefinition(
                    metadata,
                    TypeAttributes.Public,
                    "External",
                    "Exported");
                AddDefinition(
                    metadata,
                    TypeAttributes.Public,
                    "External",
                    "ExportOnly`1");
                AddDefinition(
                    metadata,
                    TypeAttributes.Public,
                    "System",
                    "Boolean");
                TypeDefinitionHandle container = AddDefinition(
                    metadata,
                    TypeAttributes.Public,
                    "Nested",
                    "Container");
                TypeDefinitionHandle nestedBoolean = AddDefinition(
                    metadata,
                    TypeAttributes.NestedPublic,
                    string.Empty,
                    "Boolean");
                metadata.AddNestedType(nestedBoolean, container);
                TypeDefinitionHandle outer = AddDefinition(
                    metadata,
                    TypeAttributes.Public,
                    "Nested",
                    "Outer`1");
                TypeDefinitionHandle inner = AddDefinition(
                    metadata,
                    TypeAttributes.NestedPublic,
                    string.Empty,
                    "Inner`2");
                metadata.AddNestedType(inner, outer);
            });
        byte[] second = BuildCatalogImage(
            "Exports",
            metadata =>
            {
                AssemblyFileHandle module = metadata.AddAssemblyFile(
                    metadata.GetOrAddString("Part.netmodule"),
                    default,
                    containsMetadata: true);
                metadata.AddExportedType(
                    TypeAttributes.Public,
                    metadata.GetOrAddString("External"),
                    metadata.GetOrAddString("Exported"),
                    module,
                    typeDefinitionId: 1);
                metadata.AddExportedType(
                    TypeAttributes.Public,
                    metadata.GetOrAddString("External"),
                    metadata.GetOrAddString("ExportOnly"),
                    module,
                    typeDefinitionId: 2);
            });
        await using ArtifactFixture artifacts =
            await ArtifactFixture.CreatePopulationImagesAsync(
                (contribution, first),
                (contribution, second));
        PlatformPopulationRealizationResult.Completed population =
            await RealizeCatalogPopulationAsync(
                request.Request,
                artifacts,
                count: 2);
        try
        {
            PlatformTypeCatalog catalog = Assert.IsType<
                    PlatformTypeCatalogDerivationOutcome.Completed>(
                    PlatformTypeCatalogDerivation.Execute(
                        population,
                        s_catalogBounds,
                        cancellationToken))
                .Catalog;

            PlatformTypeCatalogQueryOutcome.Resolved definition =
                Assert.IsType<
                    PlatformTypeCatalogQueryOutcome.Resolved>(
                    PlatformTypeCatalogQuery.Execute(
                        catalog,
                        "External.Exported",
                        cancellationToken));
            Assert.Equal(
                AssemblyTypeDeclarationKind.Definition,
                definition.Candidate.Kind);
            Assert.Same(catalog, definition.Catalog);

            Assert.Equal(
                AssemblyTypeDeclarationKind.ModuleExport,
                Assert.IsType<
                        PlatformTypeCatalogQueryOutcome.Resolved>(
                        PlatformTypeCatalogQuery.Execute(
                            catalog,
                            "External.ExportOnly",
                            cancellationToken))
                    .Candidate.Kind);

            Assert.Equal(
                Name("System", "Boolean"),
                Assert.IsType<
                        PlatformTypeCatalogQueryOutcome.Resolved>(
                        PlatformTypeCatalogQuery.Execute(
                            catalog,
                            "Boolean",
                            cancellationToken))
                    .Candidate.Name);
            Assert.Equal(
                Name("Nested", "Container", "Boolean"),
                Assert.IsType<
                        PlatformTypeCatalogQueryOutcome.Resolved>(
                        PlatformTypeCatalogQuery.Execute(
                            catalog,
                            "Nested.Container.Boolean",
                            cancellationToken))
                    .Candidate.Name);

            Assert.Equal(
                Name("Nested", "Outer`1", "Inner`2"),
                Assert.IsType<
                        PlatformTypeCatalogQueryOutcome.Resolved>(
                        PlatformTypeCatalogQuery.Execute(
                            catalog,
                            "Nested.Outer<T>.Inner<TKey, TValue>",
                            cancellationToken))
                    .Candidate.Name);
            Assert.IsType<PlatformTypeCatalogQueryOutcome.Missing>(
                PlatformTypeCatalogQuery.Execute(
                    catalog,
                    "Nested.Outer<TFirst, TSecond>.Inner<TKey, TValue>",
                    cancellationToken));

            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            Assert.Throws<OperationCanceledException>(
                () => PlatformTypeCatalogQuery.Execute(
                    catalog,
                    "Missing<T>",
                    cancellation.Token));
        }
        finally
        {
            await RetireCatalogPopulationAsync(
                population,
                artifacts,
                cancellationToken);
        }
    }

    [Fact]
    public async Task TypeCatalog_BoundsNeverPublishAPrefix()
    {
        CancellationToken cancellationToken =
            TestContext.Current.CancellationToken;
        var request = PopulationRequest(cancellationToken);
        PlatformSourceContribution.Realization contribution =
            PopulationContribution(
                request.Request,
                request.Reference);
        await using ArtifactFixture artifacts =
            await ArtifactFixture.CreatePopulationAsync(
                (
                    contribution,
                    RealCatalogAsset("System.Text.Json.dll")),
                (
                    contribution,
                    RealCatalogAsset("netstandard.dll")));
        PlatformPopulationRealizationResult.Completed population =
            await RealizeCatalogPopulationAsync(
                request.Request,
                artifacts,
                count: 2);
        try
        {
            PlatformTypeCatalog complete = Assert.IsType<
                    PlatformTypeCatalogDerivationOutcome.Completed>(
                    PlatformTypeCatalogDerivation.Execute(
                        population,
                        s_catalogBounds,
                        cancellationToken))
                .Catalog;

            Assert.True(
                complete.Work.Elapsed
                    < s_catalogBounds.MaximumDuration);
            AssertBound(
                PlatformTypeCatalogDerivationBound.PopulationAssemblies,
                new(
                    s_catalogBounds.MemberInspection,
                    maximumAssemblies: 1,
                    s_catalogBounds.MaximumAggregateAssemblyBytes,
                    s_catalogBounds.MaximumRetainedEntries,
                    s_catalogBounds.MaximumDuration));
            AssertBound(
                PlatformTypeCatalogDerivationBound.AggregateAssemblyBytes,
                new(
                    s_catalogBounds.MemberInspection,
                    s_catalogBounds.MaximumAssemblies,
                    maximumAggregateAssemblyBytes:
                        complete.Work.ObservedAssemblyBytes - 1,
                    s_catalogBounds.MaximumRetainedEntries,
                    s_catalogBounds.MaximumDuration));
            AssertBound(
                PlatformTypeCatalogDerivationBound.RetainedEntries,
                new(
                    s_catalogBounds.MemberInspection,
                    s_catalogBounds.MaximumAssemblies,
                    s_catalogBounds.MaximumAggregateAssemblyBytes,
                    maximumRetainedEntries:
                        checked((int)complete.Work.ObservedEntries - 1),
                    s_catalogBounds.MaximumDuration));
            AssertBound(
                PlatformTypeCatalogDerivationBound.Duration,
                new(
                    s_catalogBounds.MemberInspection,
                    s_catalogBounds.MaximumAssemblies,
                    s_catalogBounds.MaximumAggregateAssemblyBytes,
                    s_catalogBounds.MaximumRetainedEntries,
                    maximumDuration: TimeSpan.Zero));

            var memberIncomplete = Assert.IsType<
                PlatformTypeCatalogDerivationOutcome.Incomplete>(
                    PlatformTypeCatalogDerivation.Execute(
                        population,
                        new(
                            new LibraryTypeDeclarationInventoryInspectionBounds(
                                maximumAssemblyBytes: 1,
                                maximumRetainedDeclarations: 100_000,
                                maximumMetadataRows: int.MaxValue,
                                maximumRetainedTextCharacters: int.MaxValue),
                            s_catalogBounds.MaximumAssemblies,
                            s_catalogBounds
                                .MaximumAggregateAssemblyBytes,
                            s_catalogBounds.MaximumRetainedEntries,
                            s_catalogBounds.MaximumDuration),
                        cancellationToken));
            Assert.Equal(
                PlatformTypeCatalogDerivationBound.MemberAssemblyBytes,
                memberIncomplete.Bound);
            Assert.Equal(0, memberIncomplete.MemberIndex);
            Assert.Same(
                population.Value.Members[0],
                memberIncomplete.Member);
            Assert.Equal(
                LibraryTypeDeclarationInventoryInspectionBound
                    .AssemblyBytes,
                Assert.IsType<
                        LibraryTypeDeclarationInventoryInspectionOutcome
                            .Incomplete>(
                        memberIncomplete.MemberOutcome)
                    .Bound);

            void AssertBound(
                PlatformTypeCatalogDerivationBound expected,
                PlatformTypeCatalogDerivationBounds bounds)
            {
                var incomplete = Assert.IsType<
                    PlatformTypeCatalogDerivationOutcome.Incomplete>(
                        PlatformTypeCatalogDerivation.Execute(
                            population,
                            bounds,
                            cancellationToken));
                Assert.Equal(expected, incomplete.Bound);
                Assert.Same(
                    population.Value,
                    incomplete.Population);
                Assert.Same(
                    population.Receipt,
                    incomplete.PopulationReceipt);
            }
        }
        finally
        {
            await RetireCatalogPopulationAsync(
                population,
                artifacts,
                cancellationToken);
        }
    }

    [Fact]
    public async Task
        TypeCatalog_PreservesMemberBoundsAcrossAssemblyByteNormalization()
    {
        CancellationToken cancellationToken =
            TestContext.Current.CancellationToken;
        var request = PopulationRequest(cancellationToken);
        PlatformSourceContribution.Realization contribution =
            PopulationContribution(
                request.Request,
                request.Reference);
        string asset = RealCatalogAsset("System.Text.Json.dll");
        long assetBytes = new FileInfo(asset).Length;
        Assert.InRange(assetBytes, 1, 16 * 1024 * 1024 - 1);
        await using ArtifactFixture artifacts =
            await ArtifactFixture.CreatePopulationAsync(
                (contribution, asset));
        PlatformPopulationRealizationResult.Completed population =
            await RealizeCatalogPopulationAsync(
                request.Request,
                artifacts,
                count: 1);
        try
        {
            AssertMetadataRows(
                maximumAggregateAssemblyBytes: 32 * 1024 * 1024);
            AssertMetadataRows(assetBytes);

            var textIncomplete = Assert.IsType<
                PlatformTypeCatalogDerivationOutcome.Incomplete>(
                    PlatformTypeCatalogDerivation.Execute(
                        population,
                        Bounds(
                            maximumAggregateAssemblyBytes:
                                32 * 1024 * 1024,
                            maximumMetadataRows: int.MaxValue,
                            maximumRetainedTextCharacters: 0),
                        cancellationToken));
            Assert.Equal(
                PlatformTypeCatalogDerivationBound
                    .MemberRetainedTextCharacters,
                textIncomplete.Bound);
            Assert.Equal(
                LibraryTypeDeclarationInventoryInspectionBound
                    .RetainedTextCharacters,
                Assert.IsType<
                        LibraryTypeDeclarationInventoryInspectionOutcome
                            .Incomplete>(
                        textIncomplete.MemberOutcome)
                    .Bound);

            void AssertMetadataRows(
                long maximumAggregateAssemblyBytes)
            {
                var incomplete = Assert.IsType<
                    PlatformTypeCatalogDerivationOutcome.Incomplete>(
                        PlatformTypeCatalogDerivation.Execute(
                            population,
                            Bounds(
                                maximumAggregateAssemblyBytes,
                                maximumMetadataRows: 1,
                                maximumRetainedTextCharacters:
                                    int.MaxValue),
                            cancellationToken));
                Assert.Equal(
                    PlatformTypeCatalogDerivationBound
                        .MemberMetadataRows,
                    incomplete.Bound);
                Assert.Equal(
                    LibraryTypeDeclarationInventoryInspectionBound
                        .MetadataRows,
                    Assert.IsType<
                            LibraryTypeDeclarationInventoryInspectionOutcome
                                .Incomplete>(
                            incomplete.MemberOutcome)
                        .Bound);
            }

            PlatformTypeCatalogDerivationBounds Bounds(
                long maximumAggregateAssemblyBytes,
                int maximumMetadataRows,
                int maximumRetainedTextCharacters) =>
                new(
                    new(
                        maximumAssemblyBytes: 16 * 1024 * 1024,
                        maximumRetainedDeclarations: 100_000,
                        maximumMetadataRows,
                        maximumRetainedTextCharacters),
                    maximumAssemblies: 1,
                    maximumAggregateAssemblyBytes,
                    maximumRetainedEntries: 100_000,
                    maximumDuration: TimeSpan.FromSeconds(30));
        }
        finally
        {
            await RetireCatalogPopulationAsync(
                population,
                artifacts,
                cancellationToken);
        }
    }

    [Fact]
    public async Task TypeCatalog_RetiredMemberRejectsWholeCatalog()
    {
        CancellationToken cancellationToken =
            TestContext.Current.CancellationToken;
        var request = PopulationRequest(cancellationToken);
        PlatformSourceContribution.Realization contribution =
            PopulationContribution(
                request.Request,
                request.Reference);
        await using ArtifactFixture artifacts =
            await ArtifactFixture.CreatePopulationAsync(
                (
                    contribution,
                    RealCatalogAsset("System.Text.Json.dll")),
                (
                    contribution,
                    RealCatalogAsset("netstandard.dll")));
        PlatformPopulationRealizationResult.Completed population =
            await RealizeCatalogPopulationAsync(
                request.Request,
                artifacts,
                count: 2);
        await population.Owners[1].DisposeAsync();
        try
        {
            var rejected = Assert.IsType<
                PlatformTypeCatalogDerivationOutcome.Rejected>(
                    PlatformTypeCatalogDerivation.Execute(
                        population,
                        s_catalogBounds,
                        cancellationToken));

            Assert.Equal(
                PlatformTypeCatalogDerivationRejectionKind
                    .LibraryAuthorityUnavailable,
                rejected.Kind);
            Assert.Equal(1, rejected.MemberIndex);
            Assert.Same(
                population.Value.Members[1],
                rejected.Member);
            Assert.Same(population.Value, rejected.Population);
            Assert.Same(
                population.Receipt,
                rejected.PopulationReceipt);
            using LibraryOperationLease remaining = Issued(
                population.Owners[0],
                population.Value.Members[0].Library);
        }
        finally
        {
            await RetireCatalogPopulationAsync(
                population,
                artifacts,
                cancellationToken);
        }
    }

    [Fact]
    public async Task
        TypeCatalog_CancellationAfterLeaseClosurePreventsPublication()
    {
        CancellationToken cancellationToken =
            TestContext.Current.CancellationToken;
        var request = PopulationRequest(cancellationToken);
        PlatformSourceContribution.Realization contribution =
            PopulationContribution(
                request.Request,
                request.Reference);
        byte[] image = BuildCatalogImage(
            "Large",
            metadata =>
            {
                for (int index = 0; index < 20_000; index++)
                {
                    AddDefinition(
                        metadata,
                        TypeAttributes.Public,
                        "Large",
                        $"Type{index}");
                }
            });
        await using ArtifactFixture artifacts =
            await ArtifactFixture.CreatePopulationImagesAsync(
                (contribution, image));
        PlatformPopulationRealizationResult.Completed population =
            await RealizeCatalogPopulationAsync(
                request.Request,
                artifacts,
                count: 1);
        using var cancellation = CancellationTokenSource
            .CreateLinkedTokenSource(cancellationToken);
        Task retireAndCancel = Task.Run(
            async () =>
            {
                await Task.Delay(
                    TimeSpan.FromMilliseconds(10),
                    cancellationToken);
                await population.Owners[0].DisposeAsync();
                cancellation.Cancel();
            },
            cancellationToken);

        try
        {
            Assert.ThrowsAny<OperationCanceledException>(
                () => PlatformTypeCatalogDerivation.Execute(
                    population,
                    s_catalogBounds,
                    cancellation.Token));
            await retireAndCancel.WaitAsync(cancellationToken);
        }
        finally
        {
            cancellation.Cancel();
            await retireAndCancel.WaitAsync(cancellationToken);
            await RetireCatalogPopulationAsync(
                population,
                artifacts,
                cancellationToken);
        }
    }

    private static async Task<
        PlatformPopulationRealizationResult.Completed>
        RealizeCatalogPopulationAsync(
            PlatformHouseRequest request,
            ArtifactFixture artifacts,
            int count)
    {
        var selections =
            Enumerable.Range(0, count)
                .Select(
                    index => artifacts.PopulationSelection(index))
                .ToArray();
        var leases =
            Enumerable.Range(0, count)
                .Select(artifacts.IssueContentLease)
                .ToArray();
        return Assert.IsType<
            PlatformPopulationRealizationResult.Completed>(
                await PlatformHousePopulationRealizer
                    .RealizeReferencesAsync(
                        request,
                        selections,
                        leases,
                        Consumed(
                            sourceOperations: 1,
                            assemblies: count)));
    }

    private static async ValueTask RetireCatalogPopulationAsync(
        PlatformPopulationRealizationResult.Completed population,
        ArtifactFixture artifacts,
        CancellationToken cancellationToken)
    {
        foreach (LibraryContentOwner owner in population.Owners)
            await owner.DisposeAsync();
        await artifacts.BeginRetirement().WaitAsync(cancellationToken);
    }

    private static string RealCatalogAsset(string name) =>
        Path.Combine(
            AppContext.BaseDirectory,
            "RealAssets",
            "PlatformTypeCatalog",
            name);

    private static MetadataTypeDefinitionName Name(
        string @namespace,
        params string[] segments) =>
        Assert.IsType<MetadataTypeDefinitionNameResult.Valid>(
                MetadataTypeDefinitionName.Create(
                    @namespace,
                    [.. segments]))
            .Name;

    private static byte[] BuildCatalogImage(
        string assemblyName,
        Action<MetadataBuilder> addRows)
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            0,
            metadata.GetOrAddString($"{assemblyName}.dll"),
            metadata.GetOrAddGuid(Guid.NewGuid()),
            default,
            default);
        metadata.AddAssembly(
            metadata.GetOrAddString(assemblyName),
            new Version(1, 0, 0, 0),
            default,
            default,
            default,
            default);
        AddDefinition(
            metadata,
            default,
            string.Empty,
            "<Module>");
        addRows(metadata);
        var pe = new ManagedPEBuilder(
            PEHeaderBuilder.CreateLibraryHeader(),
            new MetadataRootBuilder(
                metadata,
                suppressValidation: true),
            new BlobBuilder(),
            flags: CorFlags.ILOnly);
        var image = new BlobBuilder();
        pe.Serialize(image);
        return image.ToArray();
    }

    private static TypeDefinitionHandle AddDefinition(
        MetadataBuilder metadata,
        TypeAttributes attributes,
        string @namespace,
        string name) =>
        metadata.AddTypeDefinition(
            attributes,
            metadata.GetOrAddString(@namespace),
            metadata.GetOrAddString(name),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));

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
                || typeof(Stream).IsAssignableFrom(field.FieldType)
                || typeof(LibraryContentOwner).IsAssignableFrom(
                    field.FieldType)
                || typeof(LibraryOperationLease).IsAssignableFrom(
                    field.FieldType));
    }
}
