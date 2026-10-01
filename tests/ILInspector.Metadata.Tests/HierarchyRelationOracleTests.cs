using DotnetInspector.PerformanceOracles;

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
