using System.Collections.Immutable;
using System.Text;

using DotnetInspector.Libraries;
using ILInspector.Analysis.Planning;
using ILInspector.Metadata;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
using Microsoft.CodeAnalysis.Text;
using QuerySpace;
using QuerySpace.Composition;
using QuerySpace.Operations;

namespace DotnetInspector.Sections.Tests;

public sealed class MemberMetricsInspectionOperationTests
{
    private static readonly ApiSurfaceExtractionBounds s_bounds =
        new(
            maxTypes: 100,
            maxMembers: 1_000,
            maxInspectionFailures: 100,
            maxTypeForwarders: 100,
            maxMetadataRows: 100_000,
            maxRetainedTextCharacters: 1_000_000);

    [Fact]
    public void QuerySpace_DeclaresProjectionFacetsAndPeerTerminals()
    {
        IQueryOperationRoute route =
            MemberMetricsQuery.OperationRoute;
        QuerySpaceDescriptor descriptor =
            MemberMetricsQuery.QuerySpace.Descriptor;

        Assert.Equal(MemberMetricsQuery.OperationIdentity, route.OperationIdentity);
        Assert.Equal(MemberMetricsQuery.OperationRouteIdentity, route.Identity);
        Assert.Equal(MemberMetricsQuery.SubjectRole, route.SubjectRole);
        Assert.Equal(MemberMetricsQuery.ResultGrain, route.ResultGrain);
        Assert.Equal([MemberMetricsQuery.RowSet], route.RowSets);
        QuerySpaceOperationTermDescriptor field =
            Assert.Single(descriptor.Operation.Terms);
        Assert.Equal(MemberMetricsQuery.FieldTermKey, field.Key);
        Assert.Equal(
            QueryOperationTermRole.ResultProjection,
            field.Role);
        Assert.Equal(
            [
                QuerySpaceTerminalRequirement.Rows,
                QuerySpaceTerminalRequirement.Count,
            ],
            descriptor.Terminals);
        QuerySpaceRowScopeDescriptor rows =
            Assert.Single(descriptor.RowScopes);
        Assert.Equal(MemberMetricsQuery.RowScopeIdentity, rows.Identity);
        Assert.Contains(
            rows.Facets,
            facet => facet.Key
                == MemberMetricsQuery.LargestPhysicalIlBytesKey);
        Assert.Contains(
            rows.Orders,
            order => order.Identity
                == MemberMetricsQuery.LargestPhysicalIlBytesOrder
                && order.Ranking);
        Assert.Contains(RowSelectionStageKind.Top, rows.Stages);
    }

    [Fact]
    public void QuerySpace_DerivesDemandFromProjectionPredicateAndTop()
    {
        MemberMetricsQueryRequestResult.Accepted projected =
            Accepted(
                MemberMetricsQuery.CreateRequest(
                    [MemberMetricField.LargestPhysicalIlBytes],
                    PortableQueryIntent.Empty,
                    QuerySpaceTerminalRequirement.Rows));
        Assert.Equal(
            MemberMetricEvidenceKind.BodySize,
            projected.RequestedEvidence);

        MemberMetricsQueryRequestResult.Accepted predicate =
            Accepted(
                MemberMetricsQuery.CreateRequest(
                    [],
                    PortableQueryIntent.Create(
                        [
                            new(
                                MemberMetricsQuery
                                    .IncomingSiblingCallersKey,
                                PortableQueryOperator.AtLeast,
                                "1"),
                        ],
                        [],
                        [],
                        []),
                    QuerySpaceTerminalRequirement.Count));
        Assert.Equal(
            MemberMetricEvidenceKind.SiblingCalls,
            predicate.RequestedEvidence);

        MemberMetricsQueryRequestResult.Accepted top =
            Accepted(
                MemberMetricsQuery.CreateRequest(
                    [],
                    PortableQueryIntent.Create(
                        [],
                        [],
                        [PortableQueryStage.Top(1)],
                        []),
                    QuerySpaceTerminalRequirement.Rows));
        Assert.Equal(
            MemberMetricEvidenceKind.BodySize,
            top.RequestedEvidence);

        MemberMetricsQueryRequestResult.Rejected countProjection =
            Assert.IsType<MemberMetricsQueryRequestResult.Rejected>(
                MemberMetricsQuery.ResolveRequest(
                    MemberMetricsQuery.CreateRequest(
                        [
                            MemberMetricField
                                .LargestPhysicalIlBytes,
                        ],
                        PortableQueryIntent.Empty,
                        QuerySpaceTerminalRequirement.Count),
                    TestContext.Current.CancellationToken));
        Assert.Equal(
            MemberMetricsQueryRejection.ProjectionNotSupportedForCount,
            countProjection.Reason);
    }

    [Fact]
    public async Task BodySizeOnly_UsesFocusedProducerAndAttributesAsyncBody()
    {
        byte[] image = Compile();
        await using LibraryInspectionTestLibrary library =
            await LibraryInspectionTestLibrary.CreateAsync(
                image,
                LibraryInspectionTestLibrary.Identity(image));
        MemberOverloadPopulationContent population =
            Population(library);

        MemberMetricsCompletedContent completed =
            Completed(
                Execute(
                    library,
                    population,
                    MemberMetricEvidenceKind.BodySize,
                    [MemberMetricField.LargestPhysicalIlBytes],
                    PortableQueryIntent.Empty,
                    QuerySpaceTerminalRequirement.Rows));

        Assert.Equal(MemberMetricsCompletion.Complete, completed.Completion);
        Assert.Equal(
            MemberMetricEvidenceKind.BodySize,
            completed.Evidence.Requested);
        Assert.Equal(
            MemberMetricEvidenceKind.BodySize,
            completed.Evidence.Effective);
        Assert.NotNull(completed.Execution.BodySize);
        Assert.Null(completed.Execution.SiblingCalls);
        ProducerParticipation producer =
            Assert.Single(completed.Execution.BodySize!.Producers);
        Assert.Equal("Exact Method Body Size", producer.Producer);
        Assert.Equal(
            0,
            completed.Execution.Source.PhysicalCoverage
                .InstructionWork.InstructionsVisited);

        MemberMetricsPopulationResult.Rows rows =
            Assert.IsType<MemberMetricsPopulationResult.Rows>(
                completed.Result);
        Assert.Equal(3, rows.Items.Length);
        MemberMetricsRow async =
            rows.Items.Single(row =>
                row.Member.Anchor.CanonicalSignature.Contains(
                    "System.String",
                    StringComparison.Ordinal));
        Assert.Equal(
            MemberMetricEvidenceState.Available,
            async.BodySizeState);
        Assert.NotNull(async.BodySize);
        Assert.True(async.BodySize!.PhysicalBodies.Length >= 2);
        Assert.Contains(
            async.BodySize.PhysicalBodies,
            body => body.Role
                is MemberPhysicalBodyRole
                    .StateMachineExecutionBody
                && body.IsAsyncStateMachine);
        Assert.All(
            rows.Items,
            row => Assert.Null(row.SiblingCalls));

        await library.RetireAsync();
    }

    [Fact]
    public async Task CombinedEvidence_SharesSourceAndPublishesRelationships()
    {
        byte[] image = Compile();
        await using LibraryInspectionTestLibrary library =
            await LibraryInspectionTestLibrary.CreateAsync(
                image,
                LibraryInspectionTestLibrary.Identity(image));
        MemberOverloadPopulationContent population =
            Population(library);

        MemberMetricsCompletedContent completed =
            Completed(
                Execute(
                    library,
                    population,
                    MemberMetricEvidenceKind.BodySize
                        | MemberMetricEvidenceKind.SiblingCalls,
                    [
                        MemberMetricField.LargestPhysicalIlBytes,
                        MemberMetricField.IncomingSiblingCallers,
                        MemberMetricField.OutgoingSiblingTargets,
                        MemberMetricField.SiblingRelationships,
                    ],
                    PortableQueryIntent.Empty,
                    QuerySpaceTerminalRequirement.Rows));

        Assert.Equal(2, completed.Execution.Source.LaneReceipts.Length);
        Assert.NotNull(completed.Execution.BodySize);
        Assert.NotNull(completed.Execution.SiblingCalls);
        Assert.True(
            completed.Execution.Source.PhysicalCoverage
                .InstructionWork.InstructionsVisited > 0);
        Assert.NotEmpty(
            completed.Execution.Source.PhysicalCoverage
                .GeneratedExpansion.Origins);

        MemberMetricsPopulationResult.Rows rows =
            Assert.IsType<MemberMetricsPopulationResult.Rows>(
                completed.Result);
        ImmutableArray<MemberSiblingCallRelationship> relationships =
        [
            .. rows.Items
                .SelectMany(row =>
                    row.Relationships?.Outgoing ?? []),
        ];
        Assert.NotEmpty(relationships);
        Assert.Contains(
            relationships,
            relationship =>
                relationship.Caller.MetadataToken
                    != relationship.Callee.MetadataToken);
        Assert.All(
            rows.Items,
            row =>
            {
                Assert.NotNull(row.SiblingCalls);
                Assert.Equal(
                    row.Relationships!.Incoming.Length,
                    row.SiblingCalls!.IncomingSiblingCallers);
                Assert.Equal(
                    row.Relationships.Outgoing.Length,
                    row.SiblingCalls.OutgoingSiblingTargets);
            });

        await library.RetireAsync();
    }

    [Fact]
    public async Task PredicateOnly_RowsAndCountAgreeWithoutProjection()
    {
        byte[] image = Compile();
        await using LibraryInspectionTestLibrary library =
            await LibraryInspectionTestLibrary.CreateAsync(
                image,
                LibraryInspectionTestLibrary.Identity(image));
        MemberOverloadPopulationContent population =
            Population(library);
        PortableQueryIntent rowIntent =
            PortableQueryIntent.Create(
                [
                    new(
                        MemberMetricsQuery
                            .LargestPhysicalIlBytesKey,
                        PortableQueryOperator.AtLeast,
                        "1"),
                ],
                [],
                [],
                []);

        MemberMetricsCompletedContent rows =
            Completed(
                Execute(
                    library,
                    population,
                    MemberMetricEvidenceKind.BodySize,
                    [],
                    rowIntent,
                    QuerySpaceTerminalRequirement.Rows));
        MemberMetricsCompletedContent count =
            Completed(
                Execute(
                    library,
                    population,
                    MemberMetricEvidenceKind.BodySize,
                    [],
                    rowIntent,
                    QuerySpaceTerminalRequirement.Count));

        MemberMetricsPopulationResult.Rows rowResult =
            Assert.IsType<MemberMetricsPopulationResult.Rows>(
                rows.Result);
        MemberMetricsPopulationResult.Count countResult =
            Assert.IsType<MemberMetricsPopulationResult.Count>(
                count.Result);
        Assert.Equal(rowResult.Items.Length, countResult.Value);
        Assert.All(
            rowResult.Items,
            row =>
            {
                Assert.Null(row.BodySize);
                Assert.Equal(
                    MemberMetricEvidenceState.Available,
                    row.BodySizeState);
            });

        await library.RetireAsync();
    }

    [Fact]
    public async Task InvalidDemandIsRejectedBeforeAnalysis()
    {
        byte[] image = Compile();
        await using LibraryInspectionTestLibrary library =
            await LibraryInspectionTestLibrary.CreateAsync(
                image,
                LibraryInspectionTestLibrary.Identity(image));
        MemberOverloadPopulationContent population =
            Population(library);

        MemberMetricsInspectionContent.Rejected noDemand =
            Assert.IsType<MemberMetricsInspectionContent.Rejected>(
                Execute(
                    library,
                    population,
                    MemberMetricEvidenceKind.BodySize,
                    [],
                    PortableQueryIntent.Empty,
                    QuerySpaceTerminalRequirement.Rows)
                    .Content);
        Assert.Equal(
            MemberMetricsInspectionRejection.NoEffectiveEvidence,
            noDemand.Reason);

        MemberMetricsInspectionContent.Rejected unauthorized =
            Assert.IsType<MemberMetricsInspectionContent.Rejected>(
                Execute(
                    library,
                    population,
                    MemberMetricEvidenceKind.BodySize,
                    [MemberMetricField.OutgoingSiblingTargets],
                    PortableQueryIntent.Empty,
                    QuerySpaceTerminalRequirement.Rows)
                    .Content);
        Assert.Equal(
            MemberMetricsInspectionRejection.UnauthorizedEvidence,
            unauthorized.Reason);

        await library.RetireAsync();
    }

    [Fact]
    public async Task InvalidPopulationIsRejectedBeforeAnalysis()
    {
        byte[] image = Compile();
        await using LibraryInspectionTestLibrary library =
            await LibraryInspectionTestLibrary.CreateAsync(
                image,
                LibraryInspectionTestLibrary.Identity(image));
        MemberOverloadPopulationContent population =
            Population(library);
        MemberOverloadRowsOutcome.Read rows =
            Assert.IsType<MemberOverloadRowsOutcome.Read>(
                population.Overloads.Rows);
        MemberOverloadPopulationContent invalid =
            population with
            {
                Overloads = population.Overloads with
                {
                    Rows = rows with
                    {
                        Items =
                        [
                            rows.Items[0],
                            rows.Items[0],
                            .. rows.Items[2..],
                        ],
                    },
                },
            };

        MemberMetricsInspectionContent.Rejected rejected =
            Assert.IsType<MemberMetricsInspectionContent.Rejected>(
                Execute(
                    library,
                    invalid,
                    MemberMetricEvidenceKind.BodySize,
                    [MemberMetricField.LargestPhysicalIlBytes],
                    PortableQueryIntent.Empty,
                    QuerySpaceTerminalRequirement.Rows)
                    .Content);

        Assert.Equal(
            MemberMetricsInspectionRejection.InvalidPopulation,
            rejected.Reason);
        await library.RetireAsync();
    }

    [Fact]
    public async Task TerminalBoundPublishesFailedEvidenceAndIncompleteCompletion()
    {
        byte[] image = Compile();
        await using LibraryInspectionTestLibrary library =
            await LibraryInspectionTestLibrary.CreateAsync(
                image,
                LibraryInspectionTestLibrary.Identity(image));
        MemberOverloadPopulationContent population =
            Population(library);

        MemberMetricsCompletedContent completed =
            Completed(
                Execute(
                    library,
                    population,
                    MemberMetricEvidenceKind.BodySize,
                    [MemberMetricField.LargestPhysicalIlBytes],
                    PortableQueryIntent.Empty,
                    QuerySpaceTerminalRequirement.Rows,
                    new(
                        maximumBodies: 1,
                        maximumEncodedIlBytes: long.MaxValue)));

        Assert.Equal(
            MemberMetricsCompletion.Incomplete,
            completed.Completion);
        Assert.Null(completed.Execution.BodySize);
        Assert.Single(completed.Execution.Failures);
        Assert.NotNull(
            completed.Execution.Source.PhysicalCoverage
                .TerminalWork.ReachedLimit);
        MemberMetricsPopulationResult.Rows rows =
            Assert.IsType<MemberMetricsPopulationResult.Rows>(
                completed.Result);
        Assert.All(
            rows.Items,
            row => Assert.Equal(
                MemberMetricEvidenceState.Failed,
                row.BodySizeState));
        await library.RetireAsync();
    }

    private static InspectionEnvelope<MemberMetricsInspectionContent>
        Execute(
            LibraryInspectionTestLibrary library,
            MemberOverloadPopulationContent population,
            MemberMetricEvidenceKind authorized,
            IEnumerable<MemberMetricField> fields,
            PortableQueryIntent rowIntent,
            QuerySpaceTerminalRequirement terminal,
            MethodDefinitionTerminalWorkLimits? terminalWorkLimits =
                null) =>
        MemberMetricsInspectionOperation.Execute(
            new(
                library.Reference,
                population,
                authorized,
                MemberMetricsQuery.CreateRequest(
                    fields,
                    rowIntent,
                    terminal),
                MethodDefinitionGeneratedExpansionLimits.Default,
                terminalWorkLimits
                    ?? new(
                        maximumBodies: 100,
                        maximumEncodedIlBytes:
                            Math.Max(
                                population.AssemblyBytes * 2L,
                                1))),
            library.IssueOperation(),
            TestContext.Current.CancellationToken);

    private static MemberOverloadPopulationContent Population(
        LibraryInspectionTestLibrary library)
    {
        InspectionEnvelope<MemberOverloadPopulationInspectionOutcome>
            envelope =
                MemberOverloadPopulationInspectionOperation.Execute(
                    new(
                        library.Reference,
                        new(
                            new(
                                Name("MetricsFixture", "Overloads"),
                                "M"),
                            new(
                                new MemberOverloadCountRequest(),
                                new MemberOverloadRowsRequest(
                                    maximumRows: 20)),
                            s_bounds)),
                    library.IssueOperation(),
                    TestContext.Current.CancellationToken);
        return Assert.IsType<
                MemberOverloadPopulationInspectionOutcome.Available>(
                envelope.Content)
            .Content;
    }

    private static MemberMetricsCompletedContent Completed(
        InspectionEnvelope<MemberMetricsInspectionContent> envelope) =>
        Assert.IsType<MemberMetricsInspectionContent.Completed>(
                envelope.Content)
            .Content;

    private static MemberMetricsQueryRequestResult.Accepted Accepted(
        QuerySpaceRequest request) =>
        Assert.IsType<MemberMetricsQueryRequestResult.Accepted>(
            MemberMetricsQuery.ResolveRequest(
                request,
                TestContext.Current.CancellationToken));

    private static MetadataTypeDefinitionName Name(
        string @namespace,
        params string[] segments) =>
        Assert.IsType<MetadataTypeDefinitionNameResult.Valid>(
                MetadataTypeDefinitionName.Create(
                    @namespace,
                    [.. segments]))
            .Name;

    private static byte[] Compile()
    {
        const string source =
            """
            using System.Threading.Tasks;

            namespace MetricsFixture;

            public sealed class Overloads
            {
                public int M() => M(1);

                public int M(int value) => value + 1;

                public async Task<int> M(string value)
                {
                    await Task.Yield();
                    return M(value.Length);
                }
            }
            """;
        SyntaxTree tree =
            CSharpSyntaxTree.ParseText(
                SourceText.From(source, Encoding.UTF8),
                new CSharpParseOptions(LanguageVersion.Preview));
        MetadataReference[] references =
        [
            .. ((string)AppContext.GetData(
                    "TRUSTED_PLATFORM_ASSEMBLIES")!)
                .Split(Path.PathSeparator)
                .Select(
                    static path =>
                        MetadataReference.CreateFromFile(path)),
        ];
        CSharpCompilation compilation =
            CSharpCompilation.Create(
                "MemberMetricsFixtures",
                [tree],
                references,
                new(
                    OutputKind.DynamicallyLinkedLibrary,
                    optimizationLevel: OptimizationLevel.Release,
                    deterministic: true));
        using var pe = new MemoryStream();
        EmitResult result = compilation.Emit(pe);
        Assert.True(
            result.Success,
            string.Join(
                Environment.NewLine,
                result.Diagnostics));
        return pe.ToArray();
    }
}
