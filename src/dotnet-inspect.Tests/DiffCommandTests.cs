using ILInspector.Metadata;
using DotnetInspector.Commands;
using DotnetInspector.Output;
using DotnetInspector.Views;

namespace DotnetInspector.Tests;

/// <summary>
/// Tests for DiffCommand output formatting and comparison logic.
/// </summary>
public class DiffCommandTests
{
    [Fact]
    public void GetSimpleName_WithNamespace_ReturnsSimpleName()
    {
        var result = TypeMatcher.GetSimpleName("System.Text.Json.JsonSerializer");
        Assert.Equal("JsonSerializer", result);
    }

    [Fact]
    public void GetSimpleName_WithoutNamespace_ReturnsSameName()
    {
        var result = TypeMatcher.GetSimpleName("JsonSerializer");
        Assert.Equal("JsonSerializer", result);
    }

    [Fact]
    public void GetSimpleName_WithGenericType_ReturnsSimpleName()
    {
        var result = TypeMatcher.GetSimpleName("System.Collections.Generic.List`1");
        Assert.Equal("List`1", result);
    }

    [Fact]
    public void GetBaseName_WithNestedGenericType_PreservesNestedSuffix()
    {
        var result = TypeMatcher.GetBaseName("System.Collections.Generic.SortedDictionary`2.KeyCollection");
        Assert.Equal("System.Collections.Generic.SortedDictionary.KeyCollection", result);
    }

    [Fact]
    public void Matches_NestedGenericType_DoesNotMatchDeclaringType()
    {
        Assert.False(TypeMatcher.Matches(
            "System.Collections.Generic.SortedDictionary`2",
            "System.Collections.Generic.SortedDictionary<TKey,TValue>.KeyCollection"));
    }

    [Fact]
    public void Matches_NestedGenericType_MatchesNestedType()
    {
        Assert.True(TypeMatcher.Matches(
            "System.Collections.Generic.SortedDictionary`2.KeyCollection",
            "System.Collections.Generic.SortedDictionary<TKey,TValue>.KeyCollection"));
    }

    [Fact]
    public void ApiType_FullName_WithNamespace_ReturnsFullName()
    {
        var type = new ApiType { Name = "JsonSerializer", Namespace = "System.Text.Json" };
        Assert.Equal("System.Text.Json.JsonSerializer", type.FullName);
    }

    [Fact]
    public void ApiType_FullName_WithoutNamespace_ReturnsName()
    {
        var type = new ApiType { Name = "MyType", Namespace = null };
        Assert.Equal("MyType", type.FullName);
    }

    [Fact]
    public void ApiType_FullName_WithEmptyNamespace_ReturnsName()
    {
        var type = new ApiType { Name = "MyType", Namespace = "" };
        Assert.Equal("MyType", type.FullName);
    }

    [Fact]
    public void RenderAnalysisDiffMarkdown_RendersRows()
    {
        var markdown = DiffOutputFormatter.RenderAnalysisDiffMarkdown(
            "Sample",
            [new AnalysisDiffRow("`Sample.Type.M()`", "allocations", "0", "1", "+1", null, "old -; new IL_0001")],
            "old.dll",
            "new.dll");

        Assert.Contains("## Analysis Diff", markdown);
        Assert.Contains("allocations", markdown);
        Assert.Contains("+1", markdown);
        Assert.Contains("IL_0001", markdown);
    }

    private static DiffCommand.RankedAnalysisRow Ranked(string member, string signal, int magnitude, int direction, bool inBoth)
        => new(new AnalysisDiffRow($"`{member}`", signal, "0", magnitude.ToString(), $"+{magnitude}", null, null), magnitude, direction, inBoth);

    [Fact]
    public void RankAnalysisRows_OrdersInPlaceChangesByDescendingMagnitude()
    {
        var input = new List<DiffCommand.RankedAnalysisRow>
        {
            Ranked("Type.Added()", "allocations", 9, +1, inBoth: false),   // added member, large magnitude
            Ranked("Type.Small()", "allocations", 1, +1, inBoth: true),
            Ranked("Type.Big()", "allocations", 5, +1, inBoth: true),
        };

        var result = DiffCommand.RankAnalysisRows(input, changedOnly: false);

        // In-place changes rank above added/removed members; within in-place, larger magnitude first.
        Assert.Equal("`Type.Big()`", result.Rows[0].Member);
        Assert.Equal("`Type.Small()`", result.Rows[1].Member);
        Assert.Equal("`Type.Added()`", result.Rows[2].Member);
    }

    [Fact]
    public void RankAnalysisRows_ChangedOnly_DropsAddedRemovedMembers()
    {
        var input = new List<DiffCommand.RankedAnalysisRow>
        {
            Ranked("Type.Added()", "allocations", 3, +1, inBoth: false),
            Ranked("Type.Kept()", "allocations", 2, -1, inBoth: true),
        };

        var result = DiffCommand.RankAnalysisRows(input, changedOnly: true);

        Assert.Single(result.Rows);
        Assert.Equal("`Type.Kept()`", result.Rows[0].Member);
        Assert.Equal("1 improvement (1 signal)", result.Summary);
    }

    [Fact]
    public void BuildAnalysisSummary_SplitsRegressionsImprovementsAddedRemoved()
    {
        Assert.Equal(
            "2 regressions, 1 improvement, 5 added/removed (8 signals)",
            DiffCommand.BuildAnalysisSummary(total: 8, regressions: 2, improvements: 1, addedRemoved: 5, changedOnly: false));

        Assert.Equal(
            "No in-place analysis signal changes detected.",
            DiffCommand.BuildAnalysisSummary(total: 0, regressions: 0, improvements: 0, addedRemoved: 0, changedOnly: true));
    }

    [Fact]
    public void BuildAnalysisDiffView_VersionsPlain_NoMarkdownForMachineOutput()
    {
        var view = DiffOutputFormatter.BuildAnalysisDiffView(
            "Sample",
            [new AnalysisDiffRow("`T.M()`", "allocations", "0", "1", "+1", null, null)],
            "1 regression (1 signal)",
            "old.dll",
            "new.dll");

        // Versions must not carry markdown emphasis; it is serialized verbatim into TSV/JSONL.
        Assert.Equal("old.dll -> new.dll", view.Versions);
        Assert.DoesNotContain("**", view.Versions);
    }

    [Fact]
    public void BuildAnalysisDiffView_EmptyRows_CalloutMatchesSummary()
    {
        // When --changed filters out all rows but added/removed changes existed,
        // the callout must agree with the summary rather than claim "no changes".
        var view = DiffOutputFormatter.BuildAnalysisDiffView(
            "Sample",
            [],
            "No in-place analysis signal changes detected.",
            "old.dll",
            "new.dll");

        var markdown = DiffOutputFormatter.RenderAnalysisDiffView(view);
        Assert.Contains("No in-place analysis signal changes detected.", markdown);
        Assert.DoesNotContain("No analysis signal changes detected.", markdown);
    }
}
