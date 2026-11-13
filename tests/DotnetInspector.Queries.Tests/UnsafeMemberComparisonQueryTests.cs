using DotnetInspector.Fixtures;
using ILInspector.Analysis;
using ILInspector.Metadata;
using ILInspector.Research;
using Inspector.Findings;

namespace DotnetInspector.Queries.Tests;

public sealed class UnsafeMemberComparisonQueryTests
{
    [Fact]
    public void Path_set_comparison_keeps_assembly_identity_and_one_sided_additions()
    {
        string path = FixtureCatalog.UnsafeMemberDiffV1.AssemblyPath();

        RetainedFindingComparisonSet result =
            UnsafeMemberComparisonQuery.ExecuteLibrarySets(
                [UnsafeMemberComparisonAssembly.FromPath("Common.dll", path)],
                [
                    UnsafeMemberComparisonAssembly.FromPath("common.DLL", path),
                    UnsafeMemberComparisonAssembly.FromPath("Companion.dll", path),
                ],
                "library:fixture",
                TestContext.Current.CancellationToken);

        var comparisons = result.Get<UnsafeMemberFinding>(
            AnalysisFindings.UnsafeMemberDescriptor);
        Assert.Equal(2, comparisons.Length);
        Assert.Equal("Common.dll", comparisons[0].Subject.Display);
        Assert.All(
            Pairs(comparisons[0].Comparison.Value),
            static pair => Assert.IsType<PairFinding<UnsafeMemberFinding>.Present>(pair.Value));
        Assert.Equal("Companion.dll", comparisons[1].Subject.Display);
        PairFinding<UnsafeMemberFinding>[] companion =
            [.. Pairs(comparisons[1].Comparison.Value)];
        Assert.NotEmpty(companion);
        Assert.All(
            companion,
            static pair => Assert.IsType<PairFinding<UnsafeMemberFinding>.Added>(pair.Value));
    }

    [Fact]
    public async Task Participant_set_comparison_equals_the_path_set_comparison()
    {
        string oldPath = FixtureCatalog.UnsafeMemberDiffV1.AssemblyPath();
        string newPath = FixtureCatalog.UnsafeMemberDiffV2.AssemblyPath();
        await using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup before = Group(workspace, oldPath);
        using AssemblyContextGroup after = Group(workspace, newPath);

        RetainedFindingComparisonSet expected =
            UnsafeMemberComparisonQuery.ExecuteLibrarySets(
                [UnsafeMemberComparisonAssembly.FromPath("Sample.dll", oldPath)],
                [
                    UnsafeMemberComparisonAssembly.FromPath("sample.DLL", newPath),
                    UnsafeMemberComparisonAssembly.FromPath("Companion.dll", newPath),
                ],
                "library:fixture",
                TestContext.Current.CancellationToken);
        RetainedFindingComparisonSet result =
            UnsafeMemberComparisonQuery.ExecuteLibrarySets(
                [
                    UnsafeMemberComparisonAssembly.FromParticipant(
                        "Sample.dll",
                        before,
                        before.Participants[0]),
                ],
                [
                    UnsafeMemberComparisonAssembly.FromParticipant(
                        "sample.DLL",
                        after,
                        after.Participants[0]),
                    UnsafeMemberComparisonAssembly.FromParticipant(
                        "Companion.dll",
                        after,
                        after.Participants[0]),
                ],
                "library:fixture",
                TestContext.Current.CancellationToken);

        var expectedComparisons = expected.Get<UnsafeMemberFinding>(
            AnalysisFindings.UnsafeMemberDescriptor);
        var comparisons = result.Get<UnsafeMemberFinding>(
            AnalysisFindings.UnsafeMemberDescriptor);
        Assert.Equal(
            expectedComparisons.Select(static comparison => comparison.Subject),
            comparisons.Select(static comparison => comparison.Subject));
        for (int index = 0; index < comparisons.Length; index++)
        {
            Assert.Equal(
                Pairs(expectedComparisons[index].Comparison.Value).Select(Shape),
                Pairs(comparisons[index].Comparison.Value).Select(Shape));
        }

        static AssemblyContextGroup Group(
            InspectionWorkspace workspace,
            string path)
            => workspace.CreateAssemblyContextGroup(
                [
                    new AssemblyContextParticipant(
                        ResolvedAssemblyReference.CreateFromPath(
                            path,
                            AssemblyResolutionProvenance.Local(
                                "Unsafe member participant tests")),
                        NoResolverAssemblyBindingPolicy.Instance),
                ]);
    }

    // A transition's kind and the identity of the member it names.
    static (PairKind Kind, FindingKey Key) Shape(PairFinding<UnsafeMemberFinding> pair)
        => (
            pair.Kind,
            pair.Value switch
            {
                PairFinding<UnsafeMemberFinding>.Added added => added.New.Key,
                PairFinding<UnsafeMemberFinding>.Removed removed => removed.Old.Key,
                PairFinding<UnsafeMemberFinding>.Present present => present.New.Key,
                PairFinding<UnsafeMemberFinding>.Changed changed => changed.New.Key,
                _ => throw new InvalidOperationException("Unknown pair case."),
            });

    static IEnumerable<PairFinding<UnsafeMemberFinding>> Pairs(object? comparison)
        => Assert.IsType<FindingComparison<UnsafeMemberFinding>.Complete>(
            comparison).Pairs;
}
