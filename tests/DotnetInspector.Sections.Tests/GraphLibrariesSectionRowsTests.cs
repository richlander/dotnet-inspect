using QuerySpace;
using QuerySpace.Composition;
using QuerySpace.Rows;
using DotnetInspector.Queries;

namespace DotnetInspector.Sections.Tests;

public sealed class GraphLibrariesSectionRowsTests
{
    [Fact]
    public void EveryBindingExecutesRowsAndCountForItsOwnIdentity()
    {
        GraphLibrariesQueryPlan operation =
            Assert.IsType<GraphLibrariesQueryPlanResult.Accepted>(
                GraphLibrariesQuery.ResolveIntent(
                    PortableQueryIntent.Empty,
                    TestContext.Current.CancellationToken))
                .Plan;
        var pair = new AssemblyPairCallUseResult(
            [],
            [],
            [],
            [],
            AssemblyPairCallUseDiagnostics.Empty);
        var projection = new GraphLibrariesSectionRowProjection(
            AssemblyPairCallUseProjection.Create(pair),
            new AssemblyPairDirectUseClusterProjection(pair, []),
            pair.Occurrences);
        RowSelectionIntent<string> selection =
            RowSelectionIntent<string>.Create(
                [RowSelectionIntentOperation<string>.Head(1)]);

        foreach (GraphLibrariesSectionRowBinding binding
            in GraphLibrariesSectionRows.All)
        {
            QuerySpaceSectionRowResolutionResult<
                GraphLibrariesSectionRowProjection> rowsResolution =
                    binding.Resolve(
                        operation,
                        selection,
                        QuerySpaceTerminalRequirement.Rows,
                        projection);
            Assert.True(rowsResolution.IsSuccess);
            Assert.Equal(
                binding.RowSet,
                Assert.Single(
                    rowsResolution.Request!
                        .StructuralRequest.ParticipatingRowSets));
            Assert.Same(
                binding.QueryScope,
                rowsResolution.Association!.QueryScope);

            SectionRowsOutcome<
                string,
                GraphLibrariesSectionRowProjection> rows =
                    QuerySpaceSectionRowExecutor.ApplyRows(
                        rowsResolution.Request);
            Assert.True(rows.IsSuccess);
            Assert.Equal(
                binding.RowSet,
                Assert.Single(rows.RowSets).Identity);
            Assert.NotNull(rows.Rebind(projection));

            QuerySpaceSectionRowResolutionResult<
                GraphLibrariesSectionRowProjection> countResolution =
                    binding.Resolve(
                        operation,
                        selection,
                        QuerySpaceTerminalRequirement.Count,
                        projection);
            SectionCountOutcome<string, string> count =
                QuerySpaceSectionRowExecutor.ApplyCount<
                    GraphLibrariesSectionRowProjection,
                    string>(countResolution.Request!);
            var completed = Assert.IsType<
                SectionCountOutcome<string, string>.Completed>(count);
            SectionCountEntry<string> entry =
                Assert.Single(completed.Counts);
            Assert.Equal(binding.RowSet, entry.Identity);
            Assert.Equal(0, entry.Value);
        }
    }
}
