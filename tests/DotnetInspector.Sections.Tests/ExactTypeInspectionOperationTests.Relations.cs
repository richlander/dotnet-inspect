using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

using DotnetInspector.Packages;
using DotnetInspector.PlatformHouse;
using DotnetInspector.Queries;
using DotnetInspector.SourceSelection;
using ILInspector.Metadata;
using Inspector.Artifacts;
using NuGetFetch;
using QuerySpace;
using QuerySpace.Operations;
using QuerySpace.Rows;

namespace DotnetInspector.Sections.Tests;

public sealed partial class ExactTypeInspectionOperationTests
{
    [Fact]
    public async Task RelationsExecuteThroughQuerySpaceWithExactCountAndRows()
    {
        byte[] assembly = BuildHierarchyAssembly();
        var store = await CachedStoreAsync(
            ("lib/net11.0/Hierarchy.dll", assembly));
        using var client = new HttpClient(new FailingHandler());
        SubjectRelationsQueryPlan plan = Assert.IsType<
            SubjectRelationsQueryPlanResult.Accepted>(
                SubjectRelationsQuery.ResolveIntent(
                    SubjectRelationsRouteKind.Type,
                    PortableQueryIntent.Create(
                        [
                            new(
                                SubjectRelationsQuery.DirectionTermKey,
                                PortableQueryOperator.Equal,
                                "incoming"),
                            new(
                                SubjectRelationsQuery.FormTermKey,
                                PortableQueryOperator.Equal,
                                "interface"),
                        ],
                        [],
                        [],
                        []),
                    TestContext.Current.CancellationToken)).Plan;

        ExactTypeRelationsInspectionOutcome outcome =
            await ExactTypeRelationsInspectionOperation.ExecuteAsync(
                new ExactTypeInspectionRequest(
                    PackageId,
                    Version,
                    Framework,
                    "Relations.IContract"),
                LoadOptions(client, store),
                plan,
                new SubjectRelationPopulationCountRequest(),
                new SubjectRelationPopulationRowsRequest(10),
                cancellationToken:
                    TestContext.Current.CancellationToken);

        if (outcome
            is ExactTypeRelationsInspectionOutcome.Unavailable unavailable)
        {
            Assert.Fail(unavailable.Detail);
        }
        var available = Assert.IsType<
            ExactTypeRelationsInspectionOutcome.Available>(outcome);
        Assert.Equal(
            2,
            Assert.IsType<
                SubjectRelationPopulationCountOutcome.Counted>(
                    available.Relations.Population.Count).Value);
        var rows = Assert.IsType<
            SubjectRelationPopulationRowsOutcome.Read>(
                available.Relations.Population.Rows);
        Assert.Equal(2, rows.Items.Length);
        Assert.All(
            rows.Items,
            row => Assert.Equal(
                SubjectRelationForm.Interface,
                row.Form));
        Assert.True(
            available.Relations.Relations.Evidence.IsComplete);
    }

    [Fact]
    public async Task PlatformForwarderLoadsOnlyItsTerminalFocusContext()
    {
        const string runtimeVersion = "11.0.0";
        const string facade = "Facade";
        const string terminal = "Terminal";
        var terminalIdentity = new AssemblyReferenceIdentity(
            terminal,
            new Version(1, 0, 0, 0),
            Culture: null,
            PublicKeyToken: null);
        byte[] facadeAssembly = BuildMetadataAssembly(
            facade,
            Guid.NewGuid(),
            definesType: false,
            "Relations",
            "IContract",
            terminalIdentity);
        byte[] terminalAssembly = BuildMetadataAssembly(
            terminal,
            Guid.NewGuid(),
            definesType: true,
            "Relations",
            "IContract");
        var requestedAssemblies = new List<string>();
        PlatformSourceCapabilityIdentity capability =
            PlatformSourceCapabilityIdentity.Create(
                "test-platform-implementation");
        var platformSource = new PlatformLibraryRealizationSource(
            capability,
            PlatformSourceFacet.Implementation,
            (request, target, _, association) =>
            {
                Assert.Null(association);
                var binding = Assert.IsType<
                    PlatformLibraryDemand.AssemblyReferenceBinding>(
                        Assert.IsType<PlatformPopulationDemand.Library>(
                            ((PlatformHouseOperation.Realize)
                                request.Operation).Population).Value);
                requestedAssemblies.Add(binding.Identity.Name);
                byte[] content = binding.Identity.Name switch
                {
                    facade => facadeAssembly,
                    terminal => terminalAssembly,
                    _ => throw new InvalidOperationException(),
                };
                using var pe = new PEReader(
                    new MemoryStream(content, writable: false));
                AssemblyReferenceIdentity identity =
                    AssemblyReferenceIdentity.FromAssemblyDefinition(
                        pe.GetMetadataReader());
                var contribution =
                    new PlatformSourceContribution.Realization(
                        PlatformSourceFacet.Implementation,
                        capability,
                        request.Snapshot,
                        PlatformSourceGeneration.Create("generation-1"),
                        target,
                        PlatformSourceCoordinateIdentity.Create(
                            binding.Identity.Name),
                        ((PlatformHouseOperation.Realize)
                            request.Operation).Population,
                        PlatformSourceContributionCompleteness
                            .Authoritative);
                var item =
                    new PlatformLibraryArtifactMaterializationItem(
                        contribution,
                        new TestArtifactProvenance(
                            binding.Identity.Name),
                        identity,
                        content.LongLength,
                        _ => new MemoryStream(
                            content,
                            writable: false));
                PlatformLibraryRealizationSourceAttempt attempt =
                    new PlatformLibraryRealizationSourceAttempt.Succeeded(
                        contribution,
                        PlatformHouseCandidateIdentity.Create(
                            binding.Identity.Name),
                        item);
                return ValueTask.FromResult(attempt);
            });
        var store = new InMemoryPackageStore();
        using var client = new HttpClient(new FailingHandler());
        var input = new WorkspaceContextInput
        {
            Framework = Framework,
            Members =
            [
                WorkspaceMemberCoordinate.Platform(
                    "runtime",
                    facade,
                    runtimeVersion,
                    Framework),
            ],
        };
        ExactLibrarySourceCoordinate focusLibrary;
        await using (var focusWorkspace =
            new InspectionWorkspace(
                new WorkspacePlan([], [input])))
        {
            WorkspaceDeclarationContext focusContext =
                await WorkspaceContextLoader.LoadDeclarationContextAsync(
                    focusWorkspace,
                    input,
                    LoadOptions(client, store),
                    TestContext.Current.CancellationToken);
            focusLibrary = Assert.IsAssignableFrom<
                ExactLibrarySourceCoordinate>(
                    Assert.Single(
                        focusContext.Receipt.Members).Coordinate);
        }
        SubjectRelationsQueryPlan plan = Assert.IsType<
            SubjectRelationsQueryPlanResult.Accepted>(
                SubjectRelationsQuery.ResolveIntent(
                    SubjectRelationsRouteKind.Type,
                    PortableQueryIntent.Create(
                        [
                            new(
                                SubjectRelationsQuery.DirectionTermKey,
                                PortableQueryOperator.Equal,
                                "incoming"),
                            new(
                                SubjectRelationsQuery.FormTermKey,
                                PortableQueryOperator.Equal,
                                "interface"),
                        ],
                        [],
                        [],
                        []),
                    TestContext.Current.CancellationToken)).Plan;

        ExactTypeRelationsInspectionOutcome outcome =
            await ExactTypeRelationsInspectionOperation.ExecuteAsync(
                new TypeRelationsInspectionRequest(
                    input,
                    "Relations.IContract",
                    FocusAssemblyName: facade,
                    FocusLibrary: focusLibrary),
                LoadOptions(client, store),
                plan,
                count: new SubjectRelationPopulationCountRequest(),
                platformImplementationSource: platformSource,
                cancellationToken:
                    TestContext.Current.CancellationToken);

        if (outcome
            is ExactTypeRelationsInspectionOutcome.Unavailable unavailable)
        {
            Assert.Fail(
                unavailable.Detail
                    + " Requested: "
                    + string.Join(", ", requestedAssemblies));
        }
        var available = Assert.IsType<
            ExactTypeRelationsInspectionOutcome.Available>(outcome);
        Assert.Equal(
            0,
            Assert.IsType<
                SubjectRelationPopulationCountOutcome.Counted>(
                    available.Relations.Population.Count).Value);
        Assert.Equal(
            [facade, terminal],
            available.Relations.Relations.Sources
                .Select(static source => source.Assembly.Name));
        var focus = Assert.IsType<
            StructuralSubjectIdentity.ContextTypeSubject>(
                available.Relations.Relations.Focus);
        Assert.Equal(
            terminal,
            focus.Library.Identity.Assembly.Name);
        Assert.Equal([facade, terminal], requestedAssemblies);
    }

    [Fact]
    public async Task
        FailedCapturedContextMakesCountIncompleteWhileRowsRemainUseful()
    {
        byte[] assembly = BuildHierarchyAssembly();
        var store = await CachedStoreAsync(
            ("lib/net11.0/Hierarchy.dll", assembly));
        using var client = new HttpClient(new NotFoundHandler());
        var healthyInput = new WorkspaceContextInput
        {
            Framework = Framework,
            Members =
            [
                WorkspaceMemberCoordinate.Package(
                    PackageId,
                    Version,
                    Framework),
            ],
        };
        var failedInput = new WorkspaceContextInput
        {
            Framework = Framework,
            Members =
            [
                WorkspaceMemberCoordinate.Package(
                    "missing.package",
                    Version,
                    Framework),
            ],
        };
        await using var workspace =
            new InspectionWorkspace(
                new WorkspacePlan([], [healthyInput, failedInput]));
        WorkspaceDeclarationContext healthy =
            await WorkspaceContextLoader.LoadDeclarationContextAsync(
                workspace,
                healthyInput,
                LoadOptions(client, store),
                TestContext.Current.CancellationToken);
        WorkspaceDeclarationContext failed =
            await WorkspaceContextLoader.LoadDeclarationContextAsync(
                workspace,
                failedInput,
                LoadOptions(client, store),
                TestContext.Current.CancellationToken);
        WorkspaceDeclarationPopulation population =
            Assert.IsType<WorkspaceDeclarationPopulationCapture.Captured>(
                workspace.CaptureDeclarationPopulation([healthy, failed]))
                .Population;
        WorkspaceDeclarationMember member = Assert.Single(
            population.Receipt.Members);
        MetadataTypeDefinitionName focusType = Assert.IsType<
            MetadataTypeDefinitionNameResult.Valid>(
                MetadataTypeDefinitionName.Create(
                    "Relations",
                    ["IContract"])).Name;
        SubjectRelationsQueryPlan plan = Assert.IsType<
            SubjectRelationsQueryPlanResult.Accepted>(
                SubjectRelationsQuery.ResolveIntent(
                    SubjectRelationsRouteKind.Type,
                    PortableQueryIntent.Create(
                        [
                            new(
                                SubjectRelationsQuery.DirectionTermKey,
                                PortableQueryOperator.Equal,
                                "incoming"),
                            new(
                                SubjectRelationsQuery.FormTermKey,
                                PortableQueryOperator.Equal,
                                "interface"),
                        ],
                        [],
                        [],
                        []),
                    TestContext.Current.CancellationToken)).Plan;

        WorkspaceTypeRelationsInspectionResult result =
            WorkspaceTypeRelationsInspectionOperation.Execute(
                workspace,
                population,
                new(
                    member.AssemblyIdentity,
                    member.Occurrence,
                    focusType),
                plan,
                count: new SubjectRelationPopulationCountRequest(),
                rows: new SubjectRelationPopulationRowsRequest(10),
                cancellationToken:
                    TestContext.Current.CancellationToken);

        Assert.IsType<SubjectRelationPopulationCountOutcome.Incomplete>(
            result.Population.Count);
        Assert.Equal(
            2,
            Assert.IsType<SubjectRelationPopulationRowsOutcome.Read>(
                result.Population.Rows).Items.Length);
        Assert.False(result.Population.Evidence.IsComplete);
        Assert.True(result.Population.Evidence.HasUsableRows);
        Assert.Contains(
            result.Population.Evidence.Producers
                .SelectMany(static producer => producer.Diagnostics),
            diagnostic => ReferenceEquals(
                diagnostic.Evidence,
                failed.Receipt));

        WorkspaceTypeRelationsInspectionResult selectedCount =
            WorkspaceTypeRelationsInspectionOperation.Execute(
                workspace,
                population,
                new(
                    member.AssemblyIdentity,
                    member.Occurrence,
                    focusType),
                plan,
                count: new SubjectRelationPopulationCountRequest(),
                rowSelection:
                    RowSelectionIntent<string>.Create(
                        [
                            RowSelectionIntentOperation<string>.Head(1),
                        ]),
                cancellationToken:
                    TestContext.Current.CancellationToken);

        Assert.IsType<SubjectRelationPopulationCountOutcome.Incomplete>(
            selectedCount.Population.Count);
        Assert.Null(selectedCount.Population.Rows);
        Assert.Empty(selectedCount.Candidates);
        Assert.False(selectedCount.Population.Evidence.IsComplete);
    }

    [Fact]
    public async Task SemanticSelectionSettlesCountInsideSharedOperation()
    {
        byte[] assembly = BuildHierarchyAssembly();
        var store = await CachedStoreAsync(
            ("lib/net11.0/Hierarchy.dll", assembly));
        using var client = new HttpClient(new FailingHandler());
        SubjectRelationsQueryPlan plan = Assert.IsType<
            SubjectRelationsQueryPlanResult.Accepted>(
                SubjectRelationsQuery.ResolveIntent(
                    SubjectRelationsRouteKind.Type,
                    PortableQueryIntent.Create(
                        [
                            new(
                                SubjectRelationsQuery.DirectionTermKey,
                                PortableQueryOperator.Equal,
                                "incoming"),
                            new(
                                SubjectRelationsQuery.FormTermKey,
                                PortableQueryOperator.Equal,
                                "interface"),
                        ],
                        [],
                        [],
                        []),
                    TestContext.Current.CancellationToken)).Plan;
        RowSelectionIntent<string> selection =
            RowSelectionIntent<string>.Create(
                [
                    RowSelectionIntentOperation<string>.Head(1),
                ]);

        ExactTypeRelationsInspectionOutcome outcome =
            await ExactTypeRelationsInspectionOperation.ExecuteAsync(
                new ExactTypeInspectionRequest(
                    PackageId,
                    Version,
                    Framework,
                    "Relations.IContract"),
                LoadOptions(client, store),
                plan,
                count: new SubjectRelationPopulationCountRequest(),
                rowSelection: selection,
                cancellationToken:
                    TestContext.Current.CancellationToken);

        var available = Assert.IsType<
            ExactTypeRelationsInspectionOutcome.Available>(outcome);
        Assert.Equal(
            1,
            Assert.IsType<
                SubjectRelationPopulationCountOutcome.Counted>(
                    available.Relations.Population.Count).Value);
        Assert.Null(available.Relations.Population.Rows);
        Assert.Empty(available.Relations.Candidates);
        Assert.Empty(available.Relations.Relations.Rows);
        Assert.Equal(2, available.Relations.Relations.CandidateCount);
        Assert.True(
            available.Relations.Relations.Evidence.IsComplete);
    }

    [Fact]
    public async Task
        SemanticSelectionRejectsRowsBoundRequiringContinuation()
    {
        byte[] assembly = BuildHierarchyAssembly();
        var store = await CachedStoreAsync(
            ("lib/net11.0/Hierarchy.dll", assembly));
        using var client = new HttpClient(new FailingHandler());
        SubjectRelationsQueryPlan plan = Assert.IsType<
            SubjectRelationsQueryPlanResult.Accepted>(
                SubjectRelationsQuery.ResolveIntent(
                    SubjectRelationsRouteKind.Type,
                    PortableQueryIntent.Create(
                        [
                            new(
                                SubjectRelationsQuery.DirectionTermKey,
                                PortableQueryOperator.Equal,
                                "incoming"),
                            new(
                                SubjectRelationsQuery.FormTermKey,
                                PortableQueryOperator.Equal,
                                "interface"),
                        ],
                        [],
                        [],
                        []),
                    TestContext.Current.CancellationToken)).Plan;
        RowSelectionIntent<string> selection =
            RowSelectionIntent<string>.Create(
                [
                    RowSelectionIntentOperation<string>.Window(1, 2),
                ]);

        ArgumentException failure =
            await Assert.ThrowsAsync<ArgumentException>(
                () => ExactTypeRelationsInspectionOperation.ExecuteAsync(
                    new ExactTypeInspectionRequest(
                        PackageId,
                        Version,
                        Framework,
                        "Relations.IContract"),
                    LoadOptions(client, store),
                    plan,
                    rows: new SubjectRelationPopulationRowsRequest(1),
                    rowSelection: selection,
                    cancellationToken:
                        TestContext.Current.CancellationToken));

        Assert.Equal("rowSelection", failure.ParamName);
        Assert.Contains(
            "would require producer continuation",
            failure.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task SemanticSelectionFittingRowsBoundDoesNotContinue()
    {
        byte[] assembly = BuildHierarchyAssembly();
        var store = await CachedStoreAsync(
            ("lib/net11.0/Hierarchy.dll", assembly));
        using var client = new HttpClient(new FailingHandler());
        SubjectRelationsQueryPlan plan = Assert.IsType<
            SubjectRelationsQueryPlanResult.Accepted>(
                SubjectRelationsQuery.ResolveIntent(
                    SubjectRelationsRouteKind.Type,
                    PortableQueryIntent.Create(
                        [
                            new(
                                SubjectRelationsQuery.DirectionTermKey,
                                PortableQueryOperator.Equal,
                                "incoming"),
                            new(
                                SubjectRelationsQuery.FormTermKey,
                                PortableQueryOperator.Equal,
                                "interface"),
                        ],
                        [],
                        [],
                        []),
                    TestContext.Current.CancellationToken)).Plan;
        RowSelectionIntent<string> selection =
            RowSelectionIntent<string>.Create(
                [
                    RowSelectionIntentOperation<string>.Window(1, 2),
                ]);

        ExactTypeRelationsInspectionOutcome outcome =
            await ExactTypeRelationsInspectionOperation.ExecuteAsync(
                new ExactTypeInspectionRequest(
                    PackageId,
                    Version,
                    Framework,
                    "Relations.IContract"),
                LoadOptions(client, store),
                plan,
                rows: new SubjectRelationPopulationRowsRequest(2),
                rowSelection: selection,
                cancellationToken:
                    TestContext.Current.CancellationToken);

        var available = Assert.IsType<
            ExactTypeRelationsInspectionOutcome.Available>(outcome);
        var rows = Assert.IsType<
            SubjectRelationPopulationRowsOutcome.Read>(
                available.Relations.Population.Rows);
        Assert.Equal(2, rows.Items.Length);
        Assert.Null(rows.Continuation);
        Assert.Null(available.Relations.ContinuationAuthority);
    }

    [Fact]
    public async Task UnsatisfiedSemanticSelectionFailsInsideSharedOperation()
    {
        byte[] assembly = BuildHierarchyAssembly();
        var store = await CachedStoreAsync(
            ("lib/net11.0/Hierarchy.dll", assembly));
        using var client = new HttpClient(new FailingHandler());
        SubjectRelationsQueryPlan plan = Assert.IsType<
            SubjectRelationsQueryPlanResult.Accepted>(
                SubjectRelationsQuery.ResolveIntent(
                    SubjectRelationsRouteKind.Type,
                    PortableQueryIntent.Create(
                        [
                            new(
                                SubjectRelationsQuery.DirectionTermKey,
                                PortableQueryOperator.Equal,
                                "incoming"),
                            new(
                                SubjectRelationsQuery.FormTermKey,
                                PortableQueryOperator.Equal,
                                "interface"),
                        ],
                        [],
                        [],
                        []),
                    TestContext.Current.CancellationToken)).Plan;
        RowSelectionIntent<string> selection =
            RowSelectionIntent<string>.Create(
                [
                    RowSelectionIntentOperation<string>.Window(1, 3),
                ]);

        WorkspaceTypeRelationRowSelectionException failure =
            await Assert.ThrowsAsync<
                WorkspaceTypeRelationRowSelectionException>(
                    () => ExactTypeRelationsInspectionOperation.ExecuteAsync(
                        new ExactTypeInspectionRequest(
                            PackageId,
                            Version,
                            Framework,
                            "Relations.IContract"),
                        LoadOptions(client, store),
                        plan,
                        count:
                            new SubjectRelationPopulationCountRequest(),
                        rowSelection: selection,
                        cancellationToken:
                            TestContext.Current.CancellationToken));

        Assert.Equal(
            SubjectRelationForm.Interface,
            failure.Failure.Identity);
        Assert.Equal(3, failure.Failure.Failure.RequiredPosition);
        Assert.Equal(2, failure.Failure.Failure.AvailableCount);
    }

    [Fact]
    public async Task HierarchyRelationsRejectNonHierarchyFormSelection()
    {
        var store = await CachedStoreAsync(
            ("lib/net11.0/Hierarchy.dll", BuildHierarchyAssembly()));
        using var client = new HttpClient(new FailingHandler());
        SubjectRelationsQueryPlan plan = Assert.IsType<
            SubjectRelationsQueryPlanResult.Accepted>(
                SubjectRelationsQuery.ResolveIntent(
                    SubjectRelationsRouteKind.Type,
                    PortableQueryIntent.Create(
                        [
                            new(
                                SubjectRelationsQuery.DirectionTermKey,
                                PortableQueryOperator.Equal,
                                "incoming"),
                            new(
                                SubjectRelationsQuery.FormTermKey,
                                PortableQueryOperator.Equal,
                                "extension"),
                        ],
                        [],
                        [],
                        []),
                    TestContext.Current.CancellationToken)).Plan;

        ArgumentException failure =
            await Assert.ThrowsAsync<ArgumentException>(
                () => ExactTypeRelationsInspectionOperation.ExecuteAsync(
                    new ExactTypeInspectionRequest(
                        PackageId,
                        Version,
                        Framework,
                        "Relations.IContract"),
                    LoadOptions(client, store),
                    plan,
                    new SubjectRelationPopulationCountRequest(),
                    new SubjectRelationPopulationRowsRequest(1),
                    cancellationToken:
                        TestContext.Current.CancellationToken));

        Assert.Contains(
            "hierarchy producer",
            failure.Message,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Relations.IGeneric`1", "interface")]
    [InlineData("Relations.Base", "base-type")]
    public async Task RelationsMatchDefinitionLevelHierarchyTargets(
        string type,
        string form)
    {
        var store = await CachedStoreAsync(
            ("lib/net11.0/Hierarchy.dll", BuildHierarchyAssembly()));
        using var client = new HttpClient(new FailingHandler());
        SubjectRelationsQueryPlan plan = Assert.IsType<
            SubjectRelationsQueryPlanResult.Accepted>(
                SubjectRelationsQuery.ResolveIntent(
                    SubjectRelationsRouteKind.Type,
                    PortableQueryIntent.Create(
                        [
                            new(
                                SubjectRelationsQuery.DirectionTermKey,
                                PortableQueryOperator.Equal,
                                "incoming"),
                            new(
                                SubjectRelationsQuery.FormTermKey,
                                PortableQueryOperator.Equal,
                                form),
                        ],
                        [],
                        [],
                        []),
                    TestContext.Current.CancellationToken)).Plan;

        ExactTypeRelationsInspectionOutcome outcome =
            await ExactTypeRelationsInspectionOperation.ExecuteAsync(
                new ExactTypeInspectionRequest(
                    PackageId,
                    Version,
                    Framework,
                    type),
                LoadOptions(client, store),
                plan,
                new SubjectRelationPopulationCountRequest(),
                new SubjectRelationPopulationRowsRequest(10),
                cancellationToken:
                    TestContext.Current.CancellationToken);

        var available = Assert.IsType<
            ExactTypeRelationsInspectionOutcome.Available>(outcome);
        Assert.Equal(
            1,
            Assert.IsType<
                SubjectRelationPopulationCountOutcome.Counted>(
                    available.Relations.Population.Count).Value);
        Assert.Single(
            Assert.IsType<SubjectRelationPopulationRowsOutcome.Read>(
                available.Relations.Population.Rows).Items);
        WorkspaceTypeRelationCandidateRow candidate =
            Assert.Single(available.Relations.Candidates);
        Assert.Equal(
            type == "Relations.IGeneric`1" ? 2 : 1,
            candidate.Evidence.Length);
    }

    [Fact]
    public async Task RelationRowsContinueAcrossTheCapturedPopulation()
    {
        var store = await CachedStoreAsync(
            ("lib/net11.0/Hierarchy.dll", BuildHierarchyAssembly()));
        using var client = new HttpClient(new FailingHandler());
        var input = new WorkspaceContextInput
        {
            Framework = Framework,
            Members =
            [
                WorkspaceMemberCoordinate.Package(
                    PackageId,
                    Version,
                    Framework),
            ],
        };
        await using var workspace =
            new InspectionWorkspace(new WorkspacePlan([], [input]));
        WorkspaceDeclarationContext context =
            await WorkspaceContextLoader.LoadDeclarationContextAsync(
                workspace,
                input,
                LoadOptions(client, store),
                TestContext.Current.CancellationToken);
        WorkspaceDeclarationPopulation population =
            Assert.IsType<WorkspaceDeclarationPopulationCapture.Captured>(
                workspace.CaptureDeclarationPopulation([context]))
                .Population;
        var focus = Assert.IsType<WorkspaceExactTypeFocusOutcome.Found>(
            WorkspaceExactTypeFocusQuery.Execute(
                population,
                "Relations.IContract",
                ExactTypeSelectionKind.Query,
                cancellationToken:
                    TestContext.Current.CancellationToken));
        SubjectRelationsQueryPlan plan = Assert.IsType<
            SubjectRelationsQueryPlanResult.Accepted>(
                SubjectRelationsQuery.ResolveIntent(
                    SubjectRelationsRouteKind.Type,
                    PortableQueryIntent.Create(
                        [
                            new(
                                SubjectRelationsQuery.DirectionTermKey,
                                PortableQueryOperator.Equal,
                                "incoming"),
                            new(
                                SubjectRelationsQuery.FormTermKey,
                                PortableQueryOperator.Equal,
                                "interface"),
                        ],
                        [],
                        [],
                        []),
                    TestContext.Current.CancellationToken)).Plan;

        WorkspaceTypeRelationsInspectionResult first =
            WorkspaceTypeRelationsInspectionOperation.Execute(
                workspace,
                population,
                focus,
                plan,
                count: new SubjectRelationPopulationCountRequest(),
                rows: new SubjectRelationPopulationRowsRequest(1),
                cancellationToken:
                    TestContext.Current.CancellationToken);
        var firstRows = Assert.IsType<
            SubjectRelationPopulationRowsOutcome.Read>(
                first.Population.Rows);
        SubjectRelationPopulationContinuation continuation =
            Assert.IsType<SubjectRelationPopulationContinuation>(
                firstRows.Continuation);
        Assert.Equal(
            2,
            Assert.IsType<SubjectRelationPopulationCountOutcome.Counted>(
                first.Population.Count).Value);
        Assert.Equal(2, first.Relations.CandidateCount);
        Assert.Single(first.Relations.Rows);

        WorkspaceTypeRelationsInspectionResult second =
            WorkspaceTypeRelationsInspectionOperation.Execute(
                workspace,
                population,
                focus,
                plan,
                rows: new SubjectRelationPopulationRowsRequest(
                    1,
                    continuation: continuation),
                continuationAuthority: Assert.IsType<
                    WorkspaceTypeRelationsContinuationAuthority>(
                        first.ContinuationAuthority),
                cancellationToken:
                    TestContext.Current.CancellationToken);

        Assert.Single(
            Assert.IsType<SubjectRelationPopulationRowsOutcome.Read>(
                second.Population.Rows).Items);
        Assert.Equal(2, second.Relations.CandidateCount);
        Assert.Single(second.Relations.Rows);
        Assert.Single(second.Candidates);
        Assert.NotEqual(
            first.Candidates[0].Candidate,
            second.Candidates[0].Candidate);
        Assert.Equal(
            "Relations.First",
            CandidateName(first.Candidates[0]));
        Assert.Equal(
            "Relations.Second",
            CandidateName(second.Candidates[0]));
        WorkspaceTypeRelationsInspectionResult incompatible =
            WorkspaceTypeRelationsInspectionOperation.Execute(
                workspace,
                population,
                focus,
                plan,
                rows: new SubjectRelationPopulationRowsRequest(
                    1,
                    continuation: continuation),
                continuationAuthority: Assert.IsType<
                    WorkspaceTypeRelationsContinuationAuthority>(
                        first.ContinuationAuthority),
                includeNonPublic: true,
                cancellationToken:
                    TestContext.Current.CancellationToken);
        Assert.Equal(
            SubjectRelationPopulationRowsRejection
                .IncompatibleContinuation,
            Assert.IsType<
                SubjectRelationPopulationRowsOutcome.Rejected>(
                    incompatible.Population.Rows).Reason);

        static string CandidateName(
            WorkspaceTypeRelationCandidateRow candidate) =>
            MetadataTypeNameFormatter.FormatFullName(
                Assert.IsType<
                    InspectionGraphTypeIdentity.AcquiredDefinition>(
                        candidate.Candidate.Identity).Type);
    }

    static byte[] BuildHierarchyAssembly()
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            generation: 0,
            moduleName: metadata.GetOrAddString("Hierarchy.dll"),
            mvid: metadata.GetOrAddGuid(Guid.NewGuid()),
            encId: default,
            encBaseId: default);
        metadata.AddAssembly(
            metadata.GetOrAddString("Hierarchy"),
            new Version(1, 0, 0, 0),
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
        TypeDefinitionHandle contract =
            metadata.AddTypeDefinition(
                TypeAttributes.Public
                    | TypeAttributes.Interface
                    | TypeAttributes.Abstract,
                metadata.GetOrAddString("Relations"),
                metadata.GetOrAddString("IContract"),
                baseType: default,
                fieldList: MetadataTokens.FieldDefinitionHandle(1),
                methodList: MetadataTokens.MethodDefinitionHandle(1));
        TypeDefinitionHandle second =
            metadata.AddTypeDefinition(
                TypeAttributes.Public,
                metadata.GetOrAddString("Relations"),
                metadata.GetOrAddString("Second"),
                baseType: default,
                fieldList: MetadataTokens.FieldDefinitionHandle(1),
                methodList: MetadataTokens.MethodDefinitionHandle(1));
        TypeDefinitionHandle first =
            metadata.AddTypeDefinition(
                TypeAttributes.Public,
                metadata.GetOrAddString("Relations"),
                metadata.GetOrAddString("First"),
                baseType: default,
                fieldList: MetadataTokens.FieldDefinitionHandle(1),
                methodList: MetadataTokens.MethodDefinitionHandle(1));
        metadata.AddInterfaceImplementation(second, contract);
        metadata.AddInterfaceImplementation(first, contract);
        TypeDefinitionHandle genericContract =
            metadata.AddTypeDefinition(
                TypeAttributes.Public
                    | TypeAttributes.Interface
                    | TypeAttributes.Abstract,
                metadata.GetOrAddString("Relations"),
                metadata.GetOrAddString("IGeneric`1"),
                baseType: default,
                fieldList: MetadataTokens.FieldDefinitionHandle(1),
                methodList: MetadataTokens.MethodDefinitionHandle(1));
        metadata.AddGenericParameter(
            genericContract,
            GenericParameterAttributes.None,
            metadata.GetOrAddString("T"),
            index: 0);
        TypeDefinitionHandle genericImplementation =
            metadata.AddTypeDefinition(
                TypeAttributes.Public,
                metadata.GetOrAddString("Relations"),
                metadata.GetOrAddString("GenericImplementation"),
                baseType: default,
                fieldList: MetadataTokens.FieldDefinitionHandle(1),
                methodList: MetadataTokens.MethodDefinitionHandle(1));
        var genericSignature = new BlobBuilder();
        genericSignature.WriteByte(0x15);
        genericSignature.WriteByte(0x12);
        genericSignature.WriteCompressedInteger(
            MetadataTokens.GetRowNumber(genericContract) << 2);
        genericSignature.WriteCompressedInteger(1);
        genericSignature.WriteByte(0x08);
        TypeSpecificationHandle constructedContract =
            metadata.AddTypeSpecification(
                metadata.GetOrAddBlob(genericSignature));
        metadata.AddInterfaceImplementation(
            genericImplementation,
            constructedContract);
        var secondGenericSignature = new BlobBuilder();
        secondGenericSignature.WriteByte(0x15);
        secondGenericSignature.WriteByte(0x12);
        secondGenericSignature.WriteCompressedInteger(
            MetadataTokens.GetRowNumber(genericContract) << 2);
        secondGenericSignature.WriteCompressedInteger(1);
        secondGenericSignature.WriteByte(0x0E);
        TypeSpecificationHandle secondConstructedContract =
            metadata.AddTypeSpecification(
                metadata.GetOrAddBlob(secondGenericSignature));
        metadata.AddInterfaceImplementation(
            genericImplementation,
            secondConstructedContract);
        TypeDefinitionHandle baseType =
            metadata.AddTypeDefinition(
                TypeAttributes.Public | TypeAttributes.Abstract,
                metadata.GetOrAddString("Relations"),
                metadata.GetOrAddString("Base"),
                baseType: default,
                fieldList: MetadataTokens.FieldDefinitionHandle(1),
                methodList: MetadataTokens.MethodDefinitionHandle(1));
        metadata.AddTypeDefinition(
            TypeAttributes.Public,
            metadata.GetOrAddString("Relations"),
            metadata.GetOrAddString("Derived"),
            baseType,
            fieldList: MetadataTokens.FieldDefinitionHandle(1),
            methodList: MetadataTokens.MethodDefinitionHandle(1));

        var builder = new ManagedPEBuilder(
            PEHeaderBuilder.CreateLibraryHeader(),
            new MetadataRootBuilder(metadata),
            new BlobBuilder(),
            flags: CorFlags.ILOnly);
        var image = new BlobBuilder();
        builder.Serialize(image);
        return image.ToArray();
    }

    private sealed record TestArtifactProvenance(string Name) :
        IArtifactProvenance;
}
