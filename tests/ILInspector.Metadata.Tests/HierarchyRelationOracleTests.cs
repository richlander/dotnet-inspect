using DotnetInspector.PerformanceOracles;
using DotnetInspector.Fixtures;

namespace ILInspector.Metadata.Tests;

public sealed class HierarchyRelationOracleTests
{
    [Fact]
    public void IndependentColumnsAgreeOnRoslynHierarchyAssets()
    {
        string path = Path.Combine(
            AppContext.BaseDirectory,
            "PinnedArtifacts",
            "System.Private.CoreLib.dll");
        using AssemblyInspectionSession session =
            AssemblyInspectionSession.Open(path);
        MetadataHierarchyRelationIndex index =
            RequireIndex(
                session.PrepareHierarchyRelationIndex(
                    MetadataOperationPolicy.Unbounded,
                    TestContext.Current.CancellationToken));
        var shape = new ScorecardShape();
        (MetadataHierarchyRelationKind Kind,
            MetadataTypeDefinitionName Target)[] scenarios =
        [
            (
                MetadataHierarchyRelationKind.Interface,
                TypeName(
                    "System.Collections.Generic",
                    "IEnumerable`1")),
            (
                MetadataHierarchyRelationKind.BaseType,
                TypeName("System", "Object")),
        ];

        foreach (var scenario in scenarios)
        {
            foreach (ScorecardClosing closing in Scorecard.Closings)
            {
                ScorecardAnswer<HierarchyRelationOracleRow> expected =
                    HierarchyRelationOracle.NLinqAnswer(
                        session,
                        scenario.Kind,
                        scenario.Target,
                        closing,
                        shape);
                Assert.True(
                    Scorecard.SameAnswer(
                        HierarchyRelationOracle.LinqAnswer(
                            session,
                            scenario.Kind,
                            scenario.Target,
                            closing,
                            shape),
                        expected,
                        HierarchyRelationOracleRowComparer.Instance),
                    $"LINQ disagreed for {scenario.Kind} "
                        + $"{scenario.Target} {closing}.");
                Assert.True(
                    Scorecard.SameAnswer(
                        HierarchyRelationOracle.PlannerAnswer(
                            session,
                            scenario.Kind,
                            scenario.Target,
                            closing,
                            shape),
                        expected,
                        HierarchyRelationOracleRowComparer.Instance),
                    $"Planner disagreed for {scenario.Kind} "
                        + $"{scenario.Target} {closing}.");
                Assert.True(
                    Scorecard.SameAnswer(
                        HierarchyRelationOracle.IndexedAnswer(
                            index,
                            scenario.Kind,
                            scenario.Target,
                            closing,
                            shape),
                        expected,
                        HierarchyRelationOracleRowComparer.Instance),
                    $"Index disagreed for {scenario.Kind} "
                        + $"{scenario.Target} {closing}.");
            }
        }
    }

    [Fact]
    public void IndependentColumnsAgreeOnMetadataSafetyFixtures()
    {
        HierarchyRelationSafetyCheck check =
            HierarchyRelationOracle.CheckSafety();

        Assert.Empty(check.Mismatches);
        Assert.Equal(4, check.Compared);
    }

    [Fact]
    public void IndexedCountDoesNotMaterializeSourceNames()
    {
        using AssemblyInspectionSession session =
            AssemblyInspectionSession.Open(
                Path.Combine(
                    AppContext.BaseDirectory,
                    "PinnedArtifacts",
                    "System.Private.CoreLib.dll"));
        MetadataHierarchyRelationIndex index =
            RequireIndex(
                session.PrepareHierarchyRelationIndex(
                    MetadataOperationPolicy.Unbounded,
                    TestContext.Current.CancellationToken));

        MetadataHierarchyRelationAnalysisResult result =
            RequireAvailable(
                index.Analyze(
                    new(
                        new(
                            TypeName("System", "Object"),
                            MetadataHierarchyRelationKind.BaseType),
                        MetadataOperationPolicy.Unbounded,
                        materializeRows: false),
                    TestContext.Current.CancellationToken));

        Assert.True(result.CandidateCount > 0);
        Assert.Equal(0, result.Receipt.Counters.StructuredNodes);
        Assert.Equal(0, result.Receipt.Counters.RetainedText);
        Assert.Empty(result.Relations.Evidence);
    }

    [Theory]
    [InlineData(MetadataOperationDimension.StructuredNodes)]
    [InlineData(MetadataOperationDimension.RetainedText)]
    public void IndexedRowsContainProjectionBudgetFailure(
        MetadataOperationDimension dimension)
    {
        using AssemblyInspectionSession session =
            AssemblyInspectionSession.Open(
                Path.Combine(
                    AppContext.BaseDirectory,
                    "PinnedArtifacts",
                    "System.Private.CoreLib.dll"));
        MetadataHierarchyRelationIndex index =
            RequireIndex(
                session.PrepareHierarchyRelationIndex(
                    MetadataOperationPolicy.Unbounded,
                    TestContext.Current.CancellationToken));
        var target =
            new MetadataHierarchyTargetSelection(
                TypeName("System", "Object"),
                MetadataHierarchyRelationKind.BaseType);
        var policy = new MetadataOperationPolicy(
            long.MaxValue,
            maxStructuredNodes:
                dimension == MetadataOperationDimension.StructuredNodes
                    ? 0
                    : long.MaxValue,
            maxRetainedText:
                dimension == MetadataOperationDimension.RetainedText
                    ? 0
                    : long.MaxValue);

        MetadataHierarchyRelationAnalysisResult rows =
            RequireAvailable(
                index.Analyze(
                    new(target, policy),
                    TestContext.Current.CancellationToken));
        MetadataHierarchyRelationAnalysisResult count =
            RequireAvailable(
                index.Analyze(
                    new(
                        target,
                        policy,
                        materializeRows: false),
                    TestContext.Current.CancellationToken));

        Assert.Equal(
            MetadataRelationFamilyDisposition.Partial,
            rows.Relations.Disposition);
        Assert.Empty(rows.Relations.Evidence);
        MetadataRelationDiagnostic diagnostic =
            Assert.Single(
                rows.Relations.Diagnostics,
                static diagnostic =>
                    diagnostic.Kind
                        == MetadataRelationDiagnosticKind.Limit);
        Assert.Equal(dimension, diagnostic.BudgetDimension);
        Assert.True(rows.Relations.Coverage?.Limited > 0);

        Assert.Equal(
            MetadataRelationFamilyDisposition.Complete,
            count.Relations.Disposition);
        Assert.True(count.CandidateCount > 0);
        Assert.Empty(count.Relations.Evidence);
        Assert.Equal(0, count.Receipt.Counters.StructuredNodes);
        Assert.Equal(0, count.Receipt.Counters.RetainedText);
    }

    [Fact]
    public void RetentionLimitProducesTypedPartialIndex()
    {
        using AssemblyInspectionSession session =
            AssemblyInspectionSession.Open(
                Path.Combine(
                    AppContext.BaseDirectory,
                    "PinnedArtifacts",
                    "System.Private.CoreLib.dll"));

        MetadataHierarchyRelationIndex index =
            RequireIndex(
                session.PrepareHierarchyRelationIndex(
                    new(
                        long.MaxValue,
                        maxRetainedHierarchyRelations: 0),
                    TestContext.Current.CancellationToken));

        Assert.Equal(
            MetadataRelationFamilyDisposition.Partial,
            index.Receipt.Disposition);
        MetadataRelationDiagnostic diagnostic =
            Assert.Single(
                index.Receipt.Diagnostics,
                static diagnostic =>
                    diagnostic.Kind
                        == MetadataRelationDiagnosticKind.Limit);
        Assert.Equal(
            MetadataOperationDimension.RetainedHierarchyRelations,
            diagnostic.BudgetDimension);
    }

    [Fact]
    public void CompleteIndexCertifiesExactAbsence()
    {
        using AssemblyInspectionSession session =
            AssemblyInspectionSession.Open(
                Path.Combine(
                    AppContext.BaseDirectory,
                    "PinnedArtifacts",
                    "System.Private.CoreLib.dll"));
        MetadataHierarchyRelationIndex index =
            RequireIndex(
                session.PrepareHierarchyRelationIndex(
                    MetadataOperationPolicy.Unbounded,
                    TestContext.Current.CancellationToken));

        MetadataHierarchyRelationAnalysisResult result =
            RequireAvailable(
                index.Analyze(
                    new(
                        new(
                            TypeName(
                                "Definitely.Not",
                                "ARealType"),
                            MetadataHierarchyRelationKind.Interface),
                        MetadataOperationPolicy.Unbounded,
                        materializeRows: false),
                    TestContext.Current.CancellationToken));

        Assert.Equal(0, result.CandidateCount);
        Assert.Equal(
            MetadataRelationFamilyDisposition.Complete,
            result.Relations.Disposition);
        Assert.Empty(result.Relations.Diagnostics);
    }

    [Fact]
    public void IndexRejectsUseAfterIssuingSessionIsDisposed()
    {
        var session =
            AssemblyInspectionSession.Open(
                Path.Combine(
                    AppContext.BaseDirectory,
                    "PinnedArtifacts",
                    "System.Private.CoreLib.dll"));
        MetadataHierarchyRelationIndex index =
            RequireIndex(
                session.PrepareHierarchyRelationIndex(
                    MetadataOperationPolicy.Unbounded,
                    TestContext.Current.CancellationToken));
        session.Dispose();

        Assert.Throws<ObjectDisposedException>(
            () => index.Analyze(
                new(
                    new(
                        TypeName("System", "Object"),
                        MetadataHierarchyRelationKind.BaseType),
                    MetadataOperationPolicy.Unbounded,
                    materializeRows: false),
                TestContext.Current.CancellationToken));
    }

    [Fact]
    public void DuplicateOccurrencesRemainOneLogicalCandidate()
    {
        using AssemblyInspectionSession session =
            AssemblyInspectionSession.OpenPrefetched(
                new MemoryStream(
                    HierarchyRelationSafetyFixtures
                        .BuildDuplicateInterfaceOccurrences(),
                    writable: false));
        MetadataHierarchyRelationIndex index =
            RequireIndex(
                session.PrepareHierarchyRelationIndex(
                    MetadataOperationPolicy.Unbounded,
                    TestContext.Current.CancellationToken));

        MetadataHierarchyRelationAnalysisResult result =
            RequireAvailable(
                index.Analyze(
                    new(
                        new(
                            TypeName("Sample", "ITarget"),
                            MetadataHierarchyRelationKind.Interface),
                        MetadataOperationPolicy.Unbounded),
                    TestContext.Current.CancellationToken));

        Assert.Equal(1, result.CandidateCount);
        MetadataHierarchyRelationAnalysisRow row =
            Assert.Single(result.Relations.Evidence);
        Assert.Equal(2, row.MetadataTokens.Length);
        Assert.True(row.MetadataTokens[0] < row.MetadataTokens[1]);
    }

    [Fact]
    public void VisibilityFailureOnlyLimitsPublicFilteredLookup()
    {
        const int sourceCount = 8;
        using AssemblyInspectionSession session =
            AssemblyInspectionSession.OpenPrefetched(
                new MemoryStream(
                    HierarchyRelationSafetyFixtures
                        .BuildCyclicNestedTypeVisibility(sourceCount),
                    writable: false));
        MetadataHierarchyRelationIndex index =
            RequireIndex(
                session.PrepareHierarchyRelationIndex(
                    MetadataOperationPolicy.Unbounded,
                    TestContext.Current.CancellationToken));
        var target =
            new MetadataHierarchyTargetSelection(
                TypeName("Sample", "ITarget"),
                MetadataHierarchyRelationKind.BaseType);

        MetadataHierarchyRelationAnalysisResult publicOnly =
            RequireAvailable(
                index.Analyze(
                    new(
                        target,
                        MetadataOperationPolicy.Unbounded,
                        materializeRows: false),
                    TestContext.Current.CancellationToken));
        MetadataHierarchyRelationAnalysisResult includeNonPublic =
            RequireAvailable(
                index.Analyze(
                    new(
                        target,
                        MetadataOperationPolicy.Unbounded,
                        includeNonPublic: true,
                        materializeRows: false),
                    TestContext.Current.CancellationToken));

        Assert.Equal(
            MetadataRelationFamilyDisposition.Partial,
            publicOnly.Relations.Disposition);
        Assert.NotEmpty(publicOnly.Relations.Diagnostics);
        Assert.Equal(sourceCount, includeNonPublic.CandidateCount);
        Assert.Equal(
            MetadataRelationFamilyDisposition.Complete,
            includeNonPublic.Relations.Disposition);
        Assert.Empty(includeNonPublic.Relations.Diagnostics);
    }

    [Fact]
    public void UnsupportedTargetShapePreventsExactAbsence()
    {
        using AssemblyInspectionSession session =
            AssemblyInspectionSession.OpenPrefetched(
                new MemoryStream(
                    HierarchyRelationSafetyFixtures
                        .BuildMalformedGenericTypeSpecification(),
                    writable: false));
        MetadataHierarchyRelationIndex index =
            RequireIndex(
                session.PrepareHierarchyRelationIndex(
                    MetadataOperationPolicy.Unbounded,
                    TestContext.Current.CancellationToken));

        MetadataHierarchyRelationAnalysisResult result =
            RequireAvailable(
                index.Analyze(
                    new(
                        new(
                            TypeName("Sample", "ITarget`1"),
                            MetadataHierarchyRelationKind.Interface),
                        MetadataOperationPolicy.Unbounded,
                        materializeRows: false),
                    TestContext.Current.CancellationToken));

        Assert.Equal(0, result.CandidateCount);
        Assert.Equal(
            MetadataRelationFamilyDisposition.Partial,
            result.Relations.Disposition);
        Assert.Contains(
            result.Relations.Diagnostics,
            static diagnostic =>
                diagnostic.Kind
                    == MetadataRelationDiagnosticKind.UnsupportedShape);
    }

    static MetadataHierarchyRelationIndex RequireIndex(
        MetadataHierarchyRelationIndexPreparation preparation) =>
        preparation switch
        {
            MetadataHierarchyRelationIndexPreparation.Ready ready =>
                ready.Index,
            MetadataHierarchyRelationIndexPreparation.Failed failed =>
                throw new InvalidOperationException(
                    string.Join(
                        ", ",
                        failed.Receipt.Diagnostics.Select(
                            static diagnostic => diagnostic.Detail))),
            MetadataHierarchyRelationIndexPreparation.Rejected rejected =>
                throw new InvalidOperationException(rejected.Detail),
            _ => throw new InvalidOperationException(
                "Unknown hierarchy index preparation outcome."),
        };

    static MetadataHierarchyRelationAnalysisResult RequireAvailable(
        MetadataHierarchyRelationAnalysisOutcome outcome) =>
        outcome switch
        {
            MetadataHierarchyRelationAnalysisOutcome.Available available =>
                available.Result,
            MetadataHierarchyRelationAnalysisOutcome.Rejected rejected =>
                throw new InvalidOperationException(rejected.Detail),
            _ => throw new InvalidOperationException(
                "Unknown hierarchy analysis outcome."),
        };

    static MetadataTypeDefinitionName TypeName(
        string @namespace,
        string name) =>
        MetadataTypeDefinitionName.Create(
            @namespace,
            [name])
        is MetadataTypeDefinitionNameResult.Valid valid
            ? valid.Name
            : throw new InvalidOperationException(
                "The hierarchy oracle target is invalid.");
}
