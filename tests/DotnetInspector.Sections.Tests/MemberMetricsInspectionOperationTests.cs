using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Text;

using DotnetInspector.Libraries;
using ILInspector.Analysis.Planning;
using ILInspector.Instructions;
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

        MemberMetricsQueryRequestResult.Accepted countTop =
            Accepted(
                MemberMetricsQuery.CreateRequest(
                    [],
                    PortableQueryIntent.Create(
                        [],
                        [],
                        [PortableQueryStage.Top(1)],
                        []),
                    QuerySpaceTerminalRequirement.Count));
        Assert.Equal(
            MemberMetricEvidenceKind.None,
            countTop.RequestedEvidence);

        MemberMetricsQueryRequestResult.Accepted countPredicateTop =
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
                        [PortableQueryStage.Top(1)],
                        []),
                    QuerySpaceTerminalRequirement.Count));
        Assert.Equal(
            MemberMetricEvidenceKind.SiblingCalls,
            countPredicateTop.RequestedEvidence);

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
    public async Task GenericTypeInstantiationUsesResolvedSiblingTarget()
    {
        byte[] image = Compile();
        AssertGenericSiblingCallUsesMemberReference(image);
        await using LibraryInspectionTestLibrary library =
            await LibraryInspectionTestLibrary.CreateAsync(
                image,
                LibraryInspectionTestLibrary.Identity(image));
        MemberOverloadPopulationContent population =
            Population(library, "GenericOverloads`1");

        MemberMetricsCompletedContent completed =
            Completed(
                Execute(
                    library,
                    population,
                    MemberMetricEvidenceKind.SiblingCalls,
                    [MemberMetricField.SiblingRelationships],
                    PortableQueryIntent.Empty,
                    QuerySpaceTerminalRequirement.Rows));

        MemberMetricsPopulationResult.Rows rows =
            Assert.IsType<MemberMetricsPopulationResult.Rows>(
                completed.Result);
        MemberSiblingCallRelationship relationship = Assert.Single(
            rows.Items.SelectMany(
                static row => row.Relationships?.Outgoing ?? []));
        Assert.Equal(1, relationship.InvocationCount);
        Assert.NotEqual(
            relationship.Caller.MetadataToken,
            relationship.Callee.MetadataToken);
        MemberMetricsRow callee = Assert.Single(
            rows.Items,
            row => row.Member.MetadataToken
                == relationship.Callee.MetadataToken);
        Assert.Contains(
            "System.Int32",
            callee.Member.Anchor.CanonicalSignature,
            StringComparison.Ordinal);

        await library.RetireAsync();
    }

    [Fact]
    public async Task IncomingPredicateIncludesBodylessCallee()
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
                        MemberMetricsQuery.IncomingSiblingCallersKey,
                        PortableQueryOperator.AtLeast,
                        "1"),
                ],
                [],
                [],
                []);

        MemberMetricsCompletedContent completed =
            Completed(
                Execute(
                    library,
                    population,
                    MemberMetricEvidenceKind.SiblingCalls,
                    [],
                    rowIntent,
                    QuerySpaceTerminalRequirement.Rows));

        MemberMetricsPopulationResult.Rows rows =
            Assert.IsType<MemberMetricsPopulationResult.Rows>(
                completed.Result);
        MemberMetricsRow bodyless = Assert.Single(
            rows.Items,
            row => row.Member.Anchor.CanonicalSignature.Contains(
                "System.Int32",
                StringComparison.Ordinal));
        Assert.Equal(
            MemberMetricEvidenceState.Available,
            bodyless.SiblingCallsState);
        Assert.Null(bodyless.SiblingCalls);
        Assert.Null(bodyless.Relationships);

        await library.RetireAsync();
    }

    [Fact]
    public async Task IncompleteCallerMakesIncomingEvidenceIncompleteForFamily()
    {
        byte[] image =
            CorruptTrailingInstruction(
                Compile(),
                "Overloads",
                "M",
                parameterCount: 0);
        await using LibraryInspectionTestLibrary library =
            await LibraryInspectionTestLibrary.CreateAsync(
                image,
                LibraryInspectionTestLibrary.Identity(image));
        MemberOverloadPopulationContent population =
            Population(library);

        MemberMetricsCompletedContent projected =
            Completed(
                Execute(
                    library,
                    population,
                    MemberMetricEvidenceKind.SiblingCalls,
                    [
                        MemberMetricField.IncomingSiblingCallers,
                        MemberMetricField.SiblingRelationships,
                    ],
                    PortableQueryIntent.Empty,
                    QuerySpaceTerminalRequirement.Rows));

        Assert.Equal(
            MemberMetricsCompletion.Incomplete,
            projected.Completion);
        MemberMetricsPopulationResult.Rows projectedRows =
            Assert.IsType<MemberMetricsPopulationResult.Rows>(
                projected.Result);
        Assert.All(
            projectedRows.Items,
            row => Assert.Equal(
                MemberMetricEvidenceState.Incomplete,
                row.SiblingCallsState));
        MemberMetricsRow bodyless = Assert.Single(
            projectedRows.Items,
            row => row.Member.Anchor.CanonicalSignature.Contains(
                "System.Int32",
                StringComparison.Ordinal));
        Assert.Equal(
            1,
            bodyless.SiblingCalls!.IncomingSiblingCallers);
        Assert.Single(bodyless.Relationships!.Incoming);

        PortableQueryIntent incomingPredicate =
            PortableQueryIntent.Create(
                [
                    new(
                        MemberMetricsQuery.IncomingSiblingCallersKey,
                        PortableQueryOperator.AtLeast,
                        "1"),
                ],
                [],
                [],
                []);
        MemberMetricsCompletedContent filtered =
            Completed(
                Execute(
                    library,
                    population,
                    MemberMetricEvidenceKind.SiblingCalls,
                    [],
                    incomingPredicate,
                    QuerySpaceTerminalRequirement.Rows));
        MemberMetricsPopulationResult.Rows filteredRows =
            Assert.IsType<MemberMetricsPopulationResult.Rows>(
                filtered.Result);
        Assert.Empty(filteredRows.Items);
        Assert.Equal(
            MemberMetricsCompletion.Incomplete,
            filtered.Completion);

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

        MemberMetricsInspectionContent.Rejected countTop =
            Assert.IsType<MemberMetricsInspectionContent.Rejected>(
                Execute(
                    library,
                    population,
                    MemberMetricEvidenceKind.BodySize,
                    [],
                    PortableQueryIntent.Create(
                        [],
                        [],
                        [PortableQueryStage.Top(1)],
                        []),
                    QuerySpaceTerminalRequirement.Count)
                    .Content);
        Assert.Equal(
            MemberMetricsInspectionRejection.NoEffectiveEvidence,
            countTop.Reason);

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
    public async Task FailedSiblingEvidenceDoesNotPublishEmptyRelationships()
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
                    MemberMetricEvidenceKind.SiblingCalls,
                    [MemberMetricField.SiblingRelationships],
                    PortableQueryIntent.Empty,
                    QuerySpaceTerminalRequirement.Rows,
                    new(
                        maximumBodies: 1,
                        maximumEncodedIlBytes: long.MaxValue)));

        Assert.Equal(
            MemberMetricsCompletion.Incomplete,
            completed.Completion);
        Assert.Null(completed.Execution.SiblingCalls);
        Assert.Single(completed.Execution.Failures);
        MemberMetricsPopulationResult.Rows rows =
            Assert.IsType<MemberMetricsPopulationResult.Rows>(
                completed.Result);
        Assert.All(
            rows.Items,
            row =>
            {
                Assert.Equal(
                    MemberMetricEvidenceState.Failed,
                    row.SiblingCallsState);
                Assert.Null(row.Relationships);
            });

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
    public async Task StalePopulationGenerationIsRejectedBeforeAnalysis()
    {
        byte[] image = Compile();
        await using LibraryInspectionTestLibrary first =
            await LibraryInspectionTestLibrary.CreateAsync(
                image,
                LibraryInspectionTestLibrary.Identity(image));
        await using LibraryInspectionTestLibrary second =
            await LibraryInspectionTestLibrary.CreateAsync(
                image,
                LibraryInspectionTestLibrary.Identity(image));
        MemberOverloadPopulationContent population =
            Population(first);

        Assert.NotSame(
            first.Reference.ApiAssembly.Generation,
            second.Reference.ApiAssembly.Generation);
        MemberMetricsInspectionContent.Rejected rejected =
            Assert.IsType<MemberMetricsInspectionContent.Rejected>(
                Execute(
                    second,
                    population,
                    MemberMetricEvidenceKind.BodySize,
                    [MemberMetricField.LargestPhysicalIlBytes],
                    PortableQueryIntent.Empty,
                    QuerySpaceTerminalRequirement.Rows)
                    .Content);

        Assert.Equal(
            MemberMetricsInspectionRejection
                .PopulationGenerationMismatch,
            rejected.Reason);
        await first.RetireAsync();
        await second.RetireAsync();
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

    [Fact]
    public async Task BasePredicateNarrowsBodySizeAnalysisBeforeTerminalBound()
    {
        byte[] image = Compile();
        await using LibraryInspectionTestLibrary library =
            await LibraryInspectionTestLibrary.CreateAsync(
                image,
                LibraryInspectionTestLibrary.Identity(image));
        MemberOverloadPopulationContent population =
            Population(library);
        MemberOverloadRowsOutcome.Read populationRows =
            Assert.IsType<MemberOverloadRowsOutcome.Read>(
                population.Overloads.Rows);
        int selectedOrdinal = populationRows.Items[0].BaselineOrdinal;
        PortableQueryIntent rowIntent =
            PortableQueryIntent.Create(
                [
                    new(
                        MemberMetricsQuery.BaselineOrdinalKey,
                        PortableQueryOperator.Equal,
                        selectedOrdinal.ToString(
                            System.Globalization.CultureInfo
                                .InvariantCulture)),
                ],
                [],
                [],
                []);

        MemberMetricsCompletedContent completed =
            Completed(
                Execute(
                    library,
                    population,
                    MemberMetricEvidenceKind.BodySize,
                    [MemberMetricField.LargestPhysicalIlBytes],
                    rowIntent,
                    QuerySpaceTerminalRequirement.Rows,
                    new(
                        maximumBodies: 1,
                        maximumEncodedIlBytes: long.MaxValue)));

        Assert.Equal(
            MemberMetricsCompletion.Complete,
            completed.Completion);
        Assert.Null(
            completed.Execution.Source.PhysicalCoverage
                .TerminalWork.ReachedLimit);
        Assert.Equal(
            1,
            completed.Execution.Source.PhysicalCoverage
                .MethodsSelected.Count);
        MemberMetricsPopulationResult.Rows rows =
            Assert.IsType<MemberMetricsPopulationResult.Rows>(
                completed.Result);
        MemberMetricsRow selected = Assert.Single(rows.Items);
        Assert.Equal(selectedOrdinal, selected.Member.BaselineOrdinal);
        Assert.Equal(
            MemberMetricEvidenceState.Available,
            selected.BodySizeState);
        await library.RetireAsync();
    }

    [Fact]
    public async Task SiblingEvidenceRetainsFamilyBreadthForBasePredicate()
    {
        byte[] image = Compile();
        await using LibraryInspectionTestLibrary library =
            await LibraryInspectionTestLibrary.CreateAsync(
                image,
                LibraryInspectionTestLibrary.Identity(image));
        MemberOverloadPopulationContent population =
            Population(library);
        MemberOverloadRowsOutcome.Read populationRows =
            Assert.IsType<MemberOverloadRowsOutcome.Read>(
                population.Overloads.Rows);
        int selectedOrdinal = populationRows.Items[0].BaselineOrdinal;
        PortableQueryIntent rowIntent =
            PortableQueryIntent.Create(
                [
                    new(
                        MemberMetricsQuery.BaselineOrdinalKey,
                        PortableQueryOperator.Equal,
                        selectedOrdinal.ToString(
                            System.Globalization.CultureInfo
                                .InvariantCulture)),
                ],
                [],
                [],
                []);

        MemberMetricsCompletedContent completed =
            Completed(
                Execute(
                    library,
                    population,
                    MemberMetricEvidenceKind.SiblingCalls,
                    [MemberMetricField.SiblingRelationships],
                    rowIntent,
                    QuerySpaceTerminalRequirement.Rows));

        Assert.True(
            completed.Execution.Source.PhysicalCoverage
                .MethodsSelected.Count
            > 1);
        MemberMetricsPopulationResult.Rows rows =
            Assert.IsType<MemberMetricsPopulationResult.Rows>(
                completed.Result);
        Assert.Single(rows.Items);
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
        LibraryInspectionTestLibrary library,
        string typeName = "Overloads")
    {
        InspectionEnvelope<MemberOverloadPopulationInspectionOutcome>
            envelope =
                MemberOverloadPopulationInspectionOperation.Execute(
                    new(
                        library.Reference,
                        new(
                            new(
                                Name("MetricsFixture", typeName),
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

            public abstract class Overloads
            {
                public int M() => M(1);

                public abstract int M(int value);

                public async Task<int> M(string value)
                {
                    await Task.Yield();
                    return M(value.Length);
                }
            }

            public sealed class GenericOverloads<T>
            {
                public int M() => new GenericOverloads<int>().M(1);

                public int M(int value) => value;
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

    private static byte[] CorruptTrailingInstruction(
        byte[] image,
        string typeName,
        string methodName,
        int parameterCount)
    {
        using var stream = new MemoryStream(image, writable: false);
        using var pe = new PEReader(stream);
        MetadataReader reader = pe.GetMetadataReader();
        MethodDefinition method = reader.GetMethodDefinition(
            FindMethod(
                reader,
                typeName,
                methodName,
                parameterCount));
        MethodBodyBlock body =
            pe.GetMethodBody(method.RelativeVirtualAddress);
        byte[] il = body.GetILBytes()!;
        int bodyOffset =
            RvaToFileOffset(
                pe.PEHeaders,
                method.RelativeVirtualAddress);
        int headerSize = (image[bodyOffset] & 3) == 2
            ? 1
            : ((BinaryPrimitives.ReadUInt16LittleEndian(
                    image.AsSpan(bodyOffset, sizeof(ushort)))
                >> 12)
                & 0xF) * sizeof(uint);
        int lastInstruction =
            bodyOffset + headerSize + il.Length - 1;
        Assert.Equal(0x2A, image[lastInstruction]);
        image[lastInstruction] = 0xFE;
        return image;
    }

    private static void AssertGenericSiblingCallUsesMemberReference(
        byte[] image)
    {
        using var stream = new MemoryStream(image, writable: false);
        using var pe = new PEReader(stream);
        MetadataReader reader = pe.GetMetadataReader();
        MethodDefinition method = reader.GetMethodDefinition(
            FindMethod(
                reader,
                "GenericOverloads`1",
                "M",
                parameterCount: 0));
        MethodBodyBlock body =
            pe.GetMethodBody(method.RelativeVirtualAddress);
        Assert.Contains(
            MethodInstructions.Decode(body).Instructions,
            instruction =>
            {
                if (instruction.Operand is not OperandKind.InlineMethod)
                    return false;

                EntityHandle operand =
                    MetadataTokens.EntityHandle(
                        checked((int)instruction.OperandValue));
                return operand.Kind is HandleKind.MemberReference
                    && reader.GetString(
                        reader.GetMemberReference(
                            (MemberReferenceHandle)operand)
                            .Name)
                        == "M";
            });
    }

    private static MethodDefinitionHandle FindMethod(
        MetadataReader reader,
        string typeName,
        string methodName,
        int parameterCount)
    {
        TypeDefinition type = reader.GetTypeDefinition(
            reader.TypeDefinitions.Single(
                handle =>
                {
                    TypeDefinition candidate =
                        reader.GetTypeDefinition(handle);
                    return reader.GetString(candidate.Namespace)
                            == "MetricsFixture"
                        && reader.GetString(candidate.Name)
                            == typeName;
                }));
        return type.GetMethods().Single(
            handle =>
            {
                MethodDefinition candidate =
                    reader.GetMethodDefinition(handle);
                return reader.GetString(candidate.Name)
                        == methodName
                    && candidate.GetParameters().Count(
                        parameter =>
                            reader.GetParameter(parameter)
                                .SequenceNumber > 0)
                        == parameterCount;
            });
    }

    private static int RvaToFileOffset(
        PEHeaders headers,
        int rva)
    {
        SectionHeader section = headers.SectionHeaders.Single(
            candidate =>
                rva >= candidate.VirtualAddress
                && rva
                    < candidate.VirtualAddress
                        + Math.Max(
                            candidate.VirtualSize,
                            candidate.SizeOfRawData));
        return rva
            - section.VirtualAddress
            + section.PointerToRawData;
    }
}
