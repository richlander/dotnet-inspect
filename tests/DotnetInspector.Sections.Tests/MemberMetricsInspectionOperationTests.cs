using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

using DotnetInspector.Fixtures;
using ILInspector.Analysis;
using ILInspector.Metadata;
using QuerySpace;
using QuerySpace.Composition;
using QuerySpace.Operations;

namespace DotnetInspector.Sections.Tests;

public sealed class MemberMetricsInspectionOperationTests
{
    private static readonly ApiSurfaceExtractionBounds s_bounds =
        new(
            maxTypes: 10_000,
            maxMembers: 200_000,
            maxInspectionFailures: 1_000,
            maxTypeForwarders: 10_000,
            maxMetadataRows: 2_000_000,
            maxRetainedTextCharacters: 40_000_000);

    [Fact]
    public void QuerySpace_DeclaresCompactMetricRowsAndCount()
    {
        IQueryOperationRoute route =
            MemberMetricsQuery.OperationRoute;
        QuerySpaceDescriptor descriptor =
            MemberMetricsQuery.QuerySpace.Descriptor;

        Assert.Equal(
            MemberMetricsQuery.OperationIdentity,
            route.OperationIdentity);
        Assert.Equal(
            MemberMetricsQuery.OperationRouteIdentity,
            route.Identity);
        Assert.Equal(
            [MemberMetricsQuery.RowSet],
            route.RowSets);
        Assert.Equal(
            [
                QuerySpaceTerminalRequirement.Rows,
                QuerySpaceTerminalRequirement.Count,
            ],
            descriptor.Terminals);
        Assert.False(descriptor.AcceptsContinuation);
        QuerySpaceRowScopeDescriptor scope =
            Assert.Single(descriptor.RowScopes);
        Assert.Equal(
            MemberMetricsQuery.RowScopeIdentity,
            scope.Identity);
        Assert.Equal(
            [
                MemberMetricsQuery.LargestPhysicalIlBytesKey,
                MemberMetricsQuery.IncomingSiblingCallersKey,
                MemberMetricsQuery.OutgoingSiblingTargetsKey,
            ],
            scope.Facets.Select(static facet => facet.Key));
    }

    [Fact]
    public async Task NoEffectiveMetric_IsRejectedBeforeImplementationAccess()
    {
        byte[] image = await Fixture(
            FixtureCatalog.AnalysisOverloadFamilyLens);
        await using LibraryInspectionTestLibrary library =
            await LibraryInspectionTestLibrary.CreateAsync(
                image,
                LibraryInspectionTestLibrary.Identity(image),
                implementation: null);
        MemberGroupDocument document =
            Document(
                ExecuteDocument(
                    library,
                    Subject(
                        "ILInspector.Analysis.OverloadFamilyLensFixtures",
                        "SiblingDelegation",
                        "Parse")));

        InspectionEnvelope<MemberMetricsInspectionOutcome> envelope =
            MemberMetricsInspectionOperation.Execute(
                Request(
                    library,
                    document,
                    MemberMetricKind.All,
                    MemberMetricKind.None,
                    RowQueryIntent.Empty,
                    QuerySpaceTerminalRequirement.Rows),
                library.IssueOperation(),
                TestContext.Current.CancellationToken);

        MemberMetricsInspectionOutcome.Rejected rejected =
            Assert.IsType<
                MemberMetricsInspectionOutcome.Rejected>(
                envelope.Content);
        Assert.Equal(
            MemberMetricsInspectionRejection.NoEffectiveMetric,
            rejected.Reason);
    }

    [Fact]
    public async Task CountWithOnlyBaselineMetricOrder_IsRejected()
    {
        byte[] image = await Fixture(
            FixtureCatalog.AnalysisOverloadFamilyLens);
        await using LibraryInspectionTestLibrary library =
            await LibraryInspectionTestLibrary.CreateAsync(
                image,
                LibraryInspectionTestLibrary.Identity(image),
                implementation: null);
        MemberGroupDocument document =
            Document(
                ExecuteDocument(
                    library,
                    Subject(
                        "ILInspector.Analysis.OverloadFamilyLensFixtures",
                        "SiblingDelegation",
                        "Parse")));
        RowQueryIntent rows =
            RowQueryIntent.Create(
                [],
                RowQueryOrderIntent.Keys(
                    [
                        new(
                            MemberMetricsQuery
                                .LargestPhysicalIlBytesKey,
                            RowQueryOrderDirection.Descending),
                    ]),
                RowSelectionIntent<RowQueryOrderIntent>.Empty);

        InspectionEnvelope<MemberMetricsInspectionOutcome> envelope =
            MemberMetricsInspectionOperation.Execute(
                Request(
                    library,
                    document,
                    MemberMetricKind.BodySize,
                    MemberMetricKind.None,
                    rows,
                    QuerySpaceTerminalRequirement.Count),
                library.IssueOperation(),
                TestContext.Current.CancellationToken);

        Assert.Equal(
            MemberMetricsInspectionRejection.NoEffectiveMetric,
            Assert.IsType<
                    MemberMetricsInspectionOutcome.Rejected>(
                    envelope.Content)
                .Reason);
    }

    [Fact]
    public async Task StringBuilderAppendFormat_BodySizeOnlyPreservesExactRows()
    {
        byte[] image =
            await LibraryInspectionTestLibrary.PinnedNet11Async(
                "runtime",
                "System.Private.CoreLib.dll");
        await using LibraryInspectionTestLibrary library =
            await LibraryInspectionTestLibrary.CreateAsync(
                image,
                LibraryInspectionTestLibrary.Identity(image));
        MemberGroupDocument document =
            Document(
                ExecuteDocument(
                    library,
                    Subject(
                        "System.Text",
                        "StringBuilder",
                        "AppendFormat")));

        MemberMetricsInspectionContent content =
            Available(
                MemberMetricsInspectionOperation.Execute(
                    Request(
                        library,
                        document,
                        MemberMetricKind.BodySize,
                        MemberMetricKind.BodySize,
                        RowQueryIntent.Empty,
                        QuerySpaceTerminalRequirement.Rows),
                    library.IssueOperation(),
                    TestContext.Current.CancellationToken));
        MemberMetricsPopulationOutcome.Rows population =
            Assert.IsType<MemberMetricsPopulationOutcome.Rows>(
                content.Population);
        MemberOverloadRowsOutcome.Read source =
            Assert.IsType<MemberOverloadRowsOutcome.Read>(
                document.Overloads.Rows);

        Assert.Equal(
            source.Items.Select(static row => row.MetadataToken),
            population.Items.Select(static row =>
                row.Member.MetadataToken));
        Assert.All(
            population.Items,
            static row => Assert.NotNull(row.BodySize));
        Assert.Contains(
            population.Items,
            static row =>
                row.BodySize!.LargestPhysicalIlBytes > 0);
        Assert.Equal(
            ImplementationMetricKind.BodySize,
            content.Participation.RequestedMetrics);
        Assert.False(
            content.Participation.RequiredFacts.HasFlag(
                ImplementationMetricFactKind.DirectCalls));
        Assert.False(
            content.Participation.PlannedStages.HasFlag(
                ImplementationMetricWorkStage
                    .DirectCallCollection));
        Assert.False(
            content.Participation.PlannedStages.HasFlag(
                ImplementationMetricWorkStage
                    .SiblingRelationshipProjection));
    }

    [Fact]
    public async Task RelationshipOnly_CollectsFactsWithoutPublishingCallMetric()
    {
        byte[] image = await Fixture(
            FixtureCatalog.AnalysisOverloadFamilyLens);
        await using LibraryInspectionTestLibrary library =
            await LibraryInspectionTestLibrary.CreateAsync(
                image,
                LibraryInspectionTestLibrary.Identity(image));
        MemberGroupDocument document =
            Document(
                ExecuteDocument(
                    library,
                    Subject(
                        "ILInspector.Analysis.OverloadFamilyLensFixtures",
                        "SiblingDelegationChain",
                        "Parse")));

        MemberMetricsInspectionContent content =
            Available(
                MemberMetricsInspectionOperation.Execute(
                    Request(
                        library,
                        document,
                        MemberMetricKind.SiblingRelationships,
                        MemberMetricKind.SiblingRelationships,
                        RowQueryIntent.Empty,
                        QuerySpaceTerminalRequirement.Rows),
                    library.IssueOperation(),
                    TestContext.Current.CancellationToken));
        MemberMetricsPopulationOutcome.Rows population =
            Assert.IsType<MemberMetricsPopulationOutcome.Rows>(
                content.Population);

        Assert.Equal(
            ImplementationMetricKind
                .SiblingOverloadRelationships,
            content.Participation.RequestedMetrics);
        Assert.False(
            content.Participation.RequestedMetrics.HasFlag(
                ImplementationMetricKind.DirectCalls));
        Assert.True(
            content.Participation.RequiredFacts.HasFlag(
                ImplementationMetricFactKind.DirectCalls));
        Assert.True(
            content.Participation.PlannedStages.HasFlag(
                ImplementationMetricWorkStage
                    .DirectCallCollection));
        Assert.Contains(
            population.Items,
            static row =>
                row.SiblingRelationships!
                    .OutgoingSiblingTargets > 0);
        Assert.All(
            population.Items,
            static row => Assert.Null(row.BodySize));
    }

    [Fact]
    public async Task
        HiddenSameNameImplementation_IsOutsideBoundRelationshipFamily()
    {
        byte[] image = await Fixture(
            FixtureCatalog.AnalysisOverloadFamilyLens);
        await using LibraryInspectionTestLibrary library =
            await LibraryInspectionTestLibrary.CreateAsync(
                image,
                LibraryInspectionTestLibrary.Identity(image));
        MemberGroupDocument document =
            Document(
                ExecuteDocument(
                    library,
                    Subject(
                        "ILInspector.Analysis.ImplementationProfileFixtures",
                        "ImplementationProfileHiddenImplementationSample",
                        "Parse")));

        MemberMetricsInspectionContent content =
            Available(
                MemberMetricsInspectionOperation.Execute(
                    Request(
                        library,
                        document,
                        MemberMetricKind.SiblingRelationships,
                        MemberMetricKind.SiblingRelationships,
                        RowQueryIntent.Empty,
                        QuerySpaceTerminalRequirement.Rows),
                    library.IssueOperation(),
                    TestContext.Current.CancellationToken));
        ImmutableArray<MemberMetricsRow> rows =
            Assert.IsType<MemberMetricsPopulationOutcome.Rows>(
                    content.Population)
                .Items;

        Assert.Equal(2, rows.Length);
        Assert.All(
            rows,
            static row =>
            {
                Assert.True(
                    row.SiblingRelationships!.IsComplete);
                Assert.Empty(
                    row.SiblingRelationships.Incoming);
                Assert.Empty(
                    row.SiblingRelationships.Outgoing);
            });
    }

    [Fact]
    public async Task
        CallFailure_DoesNotDowngradeCompletedBodySize()
    {
        byte[] image = await Fixture(
            FixtureCatalog.AnalysisOverloadFamilyLens);
        ReplaceSecondCallTokenWithInvalidValue(
            image,
            "CallHiddenTwice");
        await using LibraryInspectionTestLibrary library =
            await LibraryInspectionTestLibrary.CreateAsync(
                image,
                LibraryInspectionTestLibrary.Identity(image));
        MemberGroupDocument document =
            Document(
                ExecuteDocument(
                    library,
                    Subject(
                        "ILInspector.Analysis.ImplementationProfileFixtures",
                        "ImplementationProfileSample",
                        "CallHiddenTwice")));

        MemberMetricsInspectionContent content =
            Available(
                MemberMetricsInspectionOperation.Execute(
                    Request(
                        library,
                        document,
                        MemberMetricKind.All,
                        MemberMetricKind.All,
                        RowQueryIntent.Empty,
                        QuerySpaceTerminalRequirement.Rows),
                    library.IssueOperation(),
                    TestContext.Current.CancellationToken));
        MemberMetricsRow row =
            Assert.Single(
                Assert.IsType<
                    MemberMetricsPopulationOutcome.Rows>(
                    content.Population)
                .Items);

        Assert.Equal(
            MemberMetricCellState.Available,
            row.BodySize!.State);
        Assert.True(
            row.BodySize.LargestPhysicalIlBytes > 0);
        Assert.False(
            row.SiblingRelationships!.IsComplete);
        Assert.NotEmpty(content.Diagnostics);
    }

    [Fact]
    public async Task TopWithoutExplicitRanking_UsesLargestBodyOrder()
    {
        byte[] image = await Fixture(
            FixtureCatalog.AnalysisOverloadFamilyLens);
        await using LibraryInspectionTestLibrary library =
            await LibraryInspectionTestLibrary.CreateAsync(
                image,
                LibraryInspectionTestLibrary.Identity(image));
        MemberGroupDocument document =
            Document(
                ExecuteDocument(
                    library,
                    Subject(
                        "ILInspector.Analysis.OverloadFamilyLensFixtures",
                        "SiblingDelegationChain",
                        "Parse")));
        RowQueryIntent rowIntent =
            RowQueryIntent.Create(
                [],
                baselineOrder: null,
                RowSelectionIntent<RowQueryOrderIntent>.Create(
                    [
                        RowSelectionIntentOperation<
                            RowQueryOrderIntent>.Top(2),
                    ]));

        MemberMetricsInspectionContent content =
            Available(
                MemberMetricsInspectionOperation.Execute(
                    Request(
                        library,
                        document,
                        MemberMetricKind.BodySize,
                        MemberMetricKind.BodySize,
                        rowIntent,
                        QuerySpaceTerminalRequirement.Rows),
                    library.IssueOperation(),
                    TestContext.Current.CancellationToken));
        ImmutableArray<MemberMetricsRow> rows =
            Assert.IsType<MemberMetricsPopulationOutcome.Rows>(
                    content.Population)
                .Items;

        Assert.Equal(2, rows.Length);
        Assert.True(
            rows[0].LargestPhysicalIlBytes
                >= rows[1].LargestPhysicalIlBytes);
        Assert.Equal(
            MemberMetricKind.BodySize,
            content.RequestedMetrics);
    }

    [Fact]
    public async Task WorkBound_PreventsAuthoritativeTop()
    {
        byte[] image = await Fixture(
            FixtureCatalog.AnalysisOverloadFamilyLens);
        await using LibraryInspectionTestLibrary library =
            await LibraryInspectionTestLibrary.CreateAsync(
                image,
                LibraryInspectionTestLibrary.Identity(image));
        MemberGroupDocument document =
            Document(
                ExecuteDocument(
                    library,
                    Subject(
                        "ILInspector.Analysis.OverloadFamilyLensFixtures",
                        "SiblingDelegationChain",
                        "Parse")));
        RowQueryIntent rowIntent =
            RowQueryIntent.Create(
                [],
                baselineOrder: null,
                RowSelectionIntent<RowQueryOrderIntent>.Create(
                    [
                        RowSelectionIntentOperation<
                            RowQueryOrderIntent>.Top(1),
                    ]));
        var limits = new MemberMetricsInspectionLimits(
            maximumAssemblyBytes: image.Length,
            new ImplementationMetricWorkLimits(
                maximumPhysicalBodies: 1,
                maximumEncodedIlBytes: long.MaxValue,
                maximumAttributionProbeBodies: 100,
                maximumAttributionProbeIlBytes: long.MaxValue));

        MemberMetricsInspectionContent content =
            Available(
                MemberMetricsInspectionOperation.Execute(
                    Request(
                        library,
                        document,
                        MemberMetricKind.BodySize,
                        MemberMetricKind.BodySize,
                        rowIntent,
                        QuerySpaceTerminalRequirement.Rows,
                        limits),
                    library.IssueOperation(),
                    TestContext.Current.CancellationToken));
        MemberMetricsPopulationOutcome.Incomplete incomplete =
            Assert.IsType<
                MemberMetricsPopulationOutcome.Incomplete>(
                content.Population);

        Assert.Equal(
            MemberMetricsPopulationIncompleteReason.RequiredEvidence,
            incomplete.Reason);
        Assert.Equal(
            MemberMetricKind.BodySize,
            incomplete.RequiredMetrics);
        Assert.Null(incomplete.RowWindowFailure);
    }

    [Fact]
    public async Task
        GeneratedBodyExhaustion_PreventsAuthoritativeTop()
    {
        byte[] image = await Fixture(
            FixtureCatalog.AnalysisOverloadFamilyLens);
        await using LibraryInspectionTestLibrary library =
            await LibraryInspectionTestLibrary.CreateAsync(
                image,
                LibraryInspectionTestLibrary.Identity(image));
        MemberGroupDocument document =
            Document(
                ExecuteDocument(
                    library,
                    Subject(
                        "ILInspector.Analysis.ImplementationProfileFixtures",
                        "ImplementationHeatLambdaSample",
                        "Scale")));
        RowQueryIntent rowIntent =
            RowQueryIntent.Create(
                [],
                baselineOrder: null,
                RowSelectionIntent<RowQueryOrderIntent>.Create(
                    [
                        RowSelectionIntentOperation<
                            RowQueryOrderIntent>.Top(1),
                    ]));
        var limits = new MemberMetricsInspectionLimits(
            maximumAssemblyBytes: image.Length,
            new ImplementationMetricWorkLimits(
                maximumPhysicalBodies: 2,
                maximumEncodedIlBytes: long.MaxValue,
                maximumAttributionProbeBodies: 100,
                maximumAttributionProbeIlBytes: long.MaxValue));

        MemberMetricsInspectionContent content =
            Available(
                MemberMetricsInspectionOperation.Execute(
                    Request(
                        library,
                        document,
                        MemberMetricKind.BodySize,
                        MemberMetricKind.BodySize,
                        rowIntent,
                        QuerySpaceTerminalRequirement.Rows,
                        limits),
                    library.IssueOperation(),
                    TestContext.Current.CancellationToken));
        MemberMetricsPopulationOutcome.Incomplete incomplete =
            Assert.IsType<
                MemberMetricsPopulationOutcome.Incomplete>(
                content.Population);

        Assert.Equal(
            MemberMetricsPopulationIncompleteReason.RequiredEvidence,
            incomplete.Reason);
        Assert.Equal(
            MemberMetricKind.BodySize,
            incomplete.RequiredMetrics);
        Assert.True(content.Coverage.IncompleteCount > 0);
        Assert.NotEmpty(content.Diagnostics);
    }

    [Fact]
    public async Task BodylessDeclaration_RemainsAnExactMetricRow()
    {
        byte[] image = await Fixture(
            FixtureCatalog.AnalysisLocalThrows);
        await using LibraryInspectionTestLibrary library =
            await LibraryInspectionTestLibrary.CreateAsync(
                image,
                LibraryInspectionTestLibrary.Identity(image));
        MemberGroupDocument document =
            Document(
                ExecuteDocument(
                    library,
                    Subject(
                        "ILInspector.Analysis.LocalThrowFixtures",
                        "AbstractThrowSample",
                        "NoBody")));

        MemberMetricsInspectionContent content =
            Available(
                MemberMetricsInspectionOperation.Execute(
                    Request(
                        library,
                        document,
                        MemberMetricKind.BodySize,
                        MemberMetricKind.BodySize,
                        RowQueryIntent.Empty,
                        QuerySpaceTerminalRequirement.Rows),
                    library.IssueOperation(),
                    TestContext.Current.CancellationToken));
        MemberMetricsRow row =
            Assert.Single(
                Assert.IsType<
                    MemberMetricsPopulationOutcome.Rows>(
                    content.Population)
                .Items);

        Assert.Equal(
            MemberMetricCellState.Bodyless,
            row.BodySize!.State);
        Assert.Null(row.BodySize.LargestPhysicalIlBytes);
        Assert.Empty(row.BodySize.PhysicalBodies);
        Assert.Equal(1, content.Coverage.BodylessCount);
    }

    [Fact]
    public async Task BodySizePredicate_RowsAndCountAgree()
    {
        byte[] image = await Fixture(
            FixtureCatalog.AnalysisOverloadFamilyLens);
        await using LibraryInspectionTestLibrary library =
            await LibraryInspectionTestLibrary.CreateAsync(
                image,
                LibraryInspectionTestLibrary.Identity(image));
        MemberGroupDocument document =
            Document(
                ExecuteDocument(
                    library,
                    Subject(
                        "ILInspector.Analysis.OverloadFamilyLensFixtures",
                        "SiblingDelegationChain",
                        "Parse")));
        RowQueryIntent rowIntent =
            RowQueryIntent.Create(
                [
                    new(
                        MemberMetricsQuery
                            .LargestPhysicalIlBytesKey,
                        RowQueryOperator.GreaterOrEqual,
                        new("1")),
                ],
                baselineOrder: null,
                RowSelectionIntent<RowQueryOrderIntent>.Empty);

        MemberMetricsInspectionContent rows =
            Available(
                MemberMetricsInspectionOperation.Execute(
                    Request(
                        library,
                        document,
                        MemberMetricKind.BodySize,
                        MemberMetricKind.None,
                        rowIntent,
                        QuerySpaceTerminalRequirement.Rows),
                    library.IssueOperation(),
                    TestContext.Current.CancellationToken));
        MemberMetricsInspectionContent count =
            Available(
                MemberMetricsInspectionOperation.Execute(
                    Request(
                        library,
                        document,
                        MemberMetricKind.BodySize,
                        MemberMetricKind.None,
                        rowIntent,
                        QuerySpaceTerminalRequirement.Count),
                    library.IssueOperation(),
                    TestContext.Current.CancellationToken));

        MemberMetricsPopulationOutcome.Rows selected =
            Assert.IsType<MemberMetricsPopulationOutcome.Rows>(
                rows.Population);
        Assert.All(
            selected.Items,
            static row =>
            {
                Assert.Null(row.BodySize);
                Assert.Null(row.SiblingRelationships);
            });
        Assert.Equal(
            selected.Items.Length,
            Assert.IsType<
                    MemberMetricsPopulationOutcome.Count>(
                    count.Population)
                .Value);
    }

    [Fact]
    public async Task
        IncompleteRelationshipPredicate_DoesNotPublishCount()
    {
        byte[] image = await Fixture(
            FixtureCatalog.AnalysisOverloadFamilyLens);
        await using LibraryInspectionTestLibrary library =
            await LibraryInspectionTestLibrary.CreateAsync(
                image,
                LibraryInspectionTestLibrary.Identity(image));
        MemberGroupDocument document =
            Document(
                ExecuteDocument(
                    library,
                    Subject(
                        "ILInspector.Analysis.OverloadFamilyLensFixtures",
                        "SiblingDelegationChain",
                        "Parse")));
        RowQueryIntent rowIntent =
            RowQueryIntent.Create(
                [
                    new(
                        MemberMetricsQuery
                            .IncomingSiblingCallersKey,
                        RowQueryOperator.Equals,
                        new("0")),
                ],
                baselineOrder: null,
                RowSelectionIntent<RowQueryOrderIntent>.Empty);
        var limits = new MemberMetricsInspectionLimits(
            maximumAssemblyBytes: image.Length,
            new ImplementationMetricWorkLimits(
                maximumPhysicalBodies: 1,
                maximumEncodedIlBytes: long.MaxValue,
                maximumAttributionProbeBodies: 100,
                maximumAttributionProbeIlBytes: long.MaxValue));

        MemberMetricsInspectionContent content =
            Available(
                MemberMetricsInspectionOperation.Execute(
                    Request(
                        library,
                        document,
                        MemberMetricKind.SiblingRelationships,
                        MemberMetricKind.None,
                        rowIntent,
                        QuerySpaceTerminalRequirement.Count,
                        limits),
                    library.IssueOperation(),
                    TestContext.Current.CancellationToken));
        MemberMetricsPopulationOutcome.Incomplete incomplete =
            Assert.IsType<
                MemberMetricsPopulationOutcome.Incomplete>(
                content.Population);

        Assert.Equal(
            MemberMetricsPopulationIncompleteReason.RequiredEvidence,
            incomplete.Reason);
        Assert.Equal(
            MemberMetricKind.SiblingRelationships,
            incomplete.RequiredMetrics);
        Assert.Null(incomplete.RowWindowFailure);
    }

    [Fact]
    public async Task WorkBound_LeavesUnavailableBodiesIncomplete()
    {
        byte[] image = await Fixture(
            FixtureCatalog.AnalysisOverloadFamilyLens);
        await using LibraryInspectionTestLibrary library =
            await LibraryInspectionTestLibrary.CreateAsync(
                image,
                LibraryInspectionTestLibrary.Identity(image));
        MemberGroupDocument document =
            Document(
                ExecuteDocument(
                    library,
                    Subject(
                        "ILInspector.Analysis.OverloadFamilyLensFixtures",
                        "SiblingDelegationChain",
                        "Parse")));
        var limits = new MemberMetricsInspectionLimits(
            maximumAssemblyBytes: image.Length,
            new ImplementationMetricWorkLimits(
                maximumPhysicalBodies: 1,
                maximumEncodedIlBytes: long.MaxValue,
                maximumAttributionProbeBodies: 100,
                maximumAttributionProbeIlBytes: long.MaxValue));

        MemberMetricsInspectionContent content =
            Available(
                MemberMetricsInspectionOperation.Execute(
                    Request(
                        library,
                        document,
                        MemberMetricKind.BodySize,
                        MemberMetricKind.BodySize,
                        RowQueryIntent.Empty,
                        QuerySpaceTerminalRequirement.Rows,
                        limits),
                    library.IssueOperation(),
                    TestContext.Current.CancellationToken));
        MemberMetricsPopulationOutcome.Rows rows =
            Assert.IsType<MemberMetricsPopulationOutcome.Rows>(
                content.Population);

        Assert.NotNull(
            content.Participation.Work!.MetricExhaustedLimit);
        Assert.Contains(
            rows.Items,
            static row =>
                row.BodySize!.State
                    is MemberMetricCellState.Incomplete);
        Assert.True(content.Coverage.IncompleteCount > 0);
    }

    [Fact]
    public async Task ReplacedPopulationBinding_IsRejected()
    {
        byte[] image = await Fixture(
            FixtureCatalog.AnalysisOverloadFamilyLens);
        await using LibraryInspectionTestLibrary library =
            await LibraryInspectionTestLibrary.CreateAsync(
                image,
                LibraryInspectionTestLibrary.Identity(image));
        MemberGroupDocument document =
            Document(
                ExecuteDocument(
                    library,
                    Subject(
                        "ILInspector.Analysis.OverloadFamilyLensFixtures",
                        "SiblingDelegation",
                        "Parse")));
        MemberOverloadPopulationBinding original =
            document.Overloads.Binding;
        var binding = new MemberOverloadPopulationBinding(
            original.Assembly,
            Guid.NewGuid(),
            original.DeclaringType,
            original.TypeDefinitionToken,
            original.Name,
            original.Category,
            original.Role,
            original.Ordering,
            original.Accessibility,
            original.Receiver,
            original.IncludeHidden);
        MemberOverloadRowsOutcome.Read source =
            Assert.IsType<MemberOverloadRowsOutcome.Read>(
                document.Overloads.Rows);
        var replaced = document with
        {
            Overloads = document.Overloads with
            {
                Binding = binding,
                Rows = source with
                {
                    Items =
                    [
                        .. source.Items.Select(row =>
                            row with { Binding = binding }),
                    ],
                },
            },
        };

        InspectionEnvelope<MemberMetricsInspectionOutcome> envelope =
            MemberMetricsInspectionOperation.Execute(
                Request(
                    library,
                    replaced,
                    MemberMetricKind.BodySize,
                    MemberMetricKind.BodySize,
                    RowQueryIntent.Empty,
                    QuerySpaceTerminalRequirement.Rows,
                    new(
                        maximumAssemblyBytes: 1,
                        DefaultLimits().Analysis)),
                library.IssueOperation(),
                TestContext.Current.CancellationToken);

        Assert.Equal(
            MemberMetricsInspectionRejection
                .StalePopulationBinding,
            Assert.IsType<
                    MemberMetricsInspectionOutcome.Rejected>(
                    envelope.Content)
                .Reason);
    }

    [Fact]
    public async Task SeparateApiAndImplementationImages_AreRejected()
    {
        byte[] api = await Fixture(
            FixtureCatalog.AnalysisOverloadFamilyLens);
        byte[] implementation = [.. api];
        await using LibraryInspectionTestLibrary library =
            await LibraryInspectionTestLibrary.CreateAsync(
                api,
                LibraryInspectionTestLibrary.Identity(api),
                implementation);
        MemberGroupDocument document =
            Document(
                ExecuteDocument(
                    library,
                    Subject(
                        "ILInspector.Analysis.OverloadFamilyLensFixtures",
                        "SiblingDelegation",
                        "Parse")));

        InspectionEnvelope<MemberMetricsInspectionOutcome> envelope =
            MemberMetricsInspectionOperation.Execute(
                Request(
                    library,
                    document,
                    MemberMetricKind.BodySize,
                    MemberMetricKind.BodySize,
                    RowQueryIntent.Empty,
                    QuerySpaceTerminalRequirement.Rows),
                library.IssueOperation(),
                TestContext.Current.CancellationToken);

        Assert.Equal(
            MemberMetricsInspectionRejection
                .SeparateApiAndImplementationAssemblies,
            Assert.IsType<
                    MemberMetricsInspectionOutcome.Rejected>(
                    envelope.Content)
                .Reason);
    }

    private static void ReplaceSecondCallTokenWithInvalidValue(
        byte[] image,
        string methodName)
    {
        using var stream = new MemoryStream(
            image,
            writable: false);
        using var peReader = new PEReader(stream);
        MetadataReader reader = peReader.GetMetadataReader();
        MethodDefinitionHandle methodHandle =
            reader.MethodDefinitions.Single(handle =>
                reader.StringComparer.Equals(
                    reader.GetMethodDefinition(handle).Name,
                    methodName));
        MethodDefinition method =
            reader.GetMethodDefinition(methodHandle);
        MethodBodyBlock body = peReader.GetMethodBody(
            method.RelativeVirtualAddress);
        byte[] il = body.GetILBytes()
            ?? throw new InvalidOperationException(
                "Expected a managed method body.");
        int secondCallOffset = il
            .Select((value, index) => (value, index))
            .Where(static item => item.value == 0x28)
            .Select(static item => item.index)
            .ElementAt(1);
        int methodOffset = RvaToFileOffset(
            peReader.PEHeaders,
            method.RelativeVirtualAddress);
        int headerSize = (image[methodOffset] & 3) == 2
            ? 1
            : ((BinaryPrimitives.ReadUInt16LittleEndian(
                    image.AsSpan(
                        methodOffset,
                        sizeof(ushort)))
                >> 12)
                & 0xF) * sizeof(uint);
        BinaryPrimitives.WriteInt32LittleEndian(
            image.AsSpan(
                methodOffset
                    + headerSize
                    + secondCallOffset
                    + 1,
                sizeof(int)),
            0x0AFFFFFF);
    }

    private static int RvaToFileOffset(
        PEHeaders headers,
        int rva)
    {
        SectionHeader section =
            headers.SectionHeaders.Single(header =>
                rva >= header.VirtualAddress
                && rva < header.VirtualAddress
                    + Math.Max(
                        header.VirtualSize,
                        header.SizeOfRawData));
        return checked(
            rva
                - section.VirtualAddress
                + section.PointerToRawData);
    }

    private static MemberMetricsInspectionRequest Request(
        LibraryInspectionTestLibrary library,
        MemberGroupDocument document,
        MemberMetricKind authorized,
        MemberMetricKind projected,
        RowQueryIntent rows,
        QuerySpaceTerminalRequirement terminal,
        MemberMetricsInspectionLimits? limits = null) =>
        new(
            library.Reference,
            document,
            authorized,
            projected,
            MemberMetricsQuery.CreateRequest(rows, terminal),
            limits ?? DefaultLimits());

    private static MemberMetricsInspectionLimits DefaultLimits() =>
        new(
            maximumAssemblyBytes: 128 * 1024 * 1024,
            new ImplementationMetricWorkLimits(
                maximumPhysicalBodies: 10_000,
                maximumEncodedIlBytes: 128 * 1024 * 1024,
                maximumAttributionProbeBodies: 10_000,
                maximumAttributionProbeIlBytes:
                    128 * 1024 * 1024));

    private static MemberGroupSubject Subject(
        string @namespace,
        string type,
        string member) =>
        new(Name(@namespace, type), member);

    private static InspectionEnvelope<
        MemberGroupDocumentInspectionOutcome> ExecuteDocument(
            LibraryInspectionTestLibrary library,
            MemberGroupSubject subject) =>
        MemberGroupDocumentInspectionOperation.Execute(
            new(
                library.Reference,
                new(
                    subject,
                    new(
                        new MemberOverloadCountRequest(),
                        new MemberOverloadRowsRequest(
                            maximumRows: 1_000)),
                    s_bounds)),
            library.IssueOperation(),
            TestContext.Current.CancellationToken);

    private static MemberGroupDocument Document(
        InspectionEnvelope<
            MemberGroupDocumentInspectionOutcome> envelope) =>
        Assert.IsType<
                MemberGroupDocumentInspectionOutcome.Available>(
                envelope.Content)
            .Document;

    private static MemberMetricsInspectionContent Available(
        InspectionEnvelope<MemberMetricsInspectionOutcome> envelope) =>
        Assert.IsType<MemberMetricsInspectionOutcome.Available>(
                envelope.Content)
            .Content;

    private static async Task<byte[]> Fixture(
        FixtureDefinition fixture) =>
        await File.ReadAllBytesAsync(
            fixture.AssemblyPath(),
            TestContext.Current.CancellationToken);

    private static MetadataTypeDefinitionName Name(
        string @namespace,
        params string[] segments) =>
        Assert.IsType<MetadataTypeDefinitionNameResult.Valid>(
                MetadataTypeDefinitionName.Create(
                    @namespace,
                    [.. segments]))
            .Name;
}
