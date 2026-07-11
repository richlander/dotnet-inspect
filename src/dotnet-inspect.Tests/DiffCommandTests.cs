using DotnetInspector.Fixtures;
using ILInspector.Analysis;
using ILInspector.Metadata;
using ILInspector.MetadataPrimitives;
using ILInspector.Research;
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

    [Fact]
    public void BuildDetailedChangesView_RendersMemberLevelRowsForJsonl()
    {
        var oldType = DiffType("Sample", "Widget", DiffMember("Echo", signature: "string Echo(string value)"));
        var newType = DiffType("Sample", "Widget", DiffMember("Echo", signature: "string Echo(string value, int repeat)"));
        var change = new ApiChange(
            ChangeKind.MemberSignatureChanged,
            ChangeClassification.Breaking,
            "Member 'Echo' signature changed",
            "string Echo(string value)",
            "string Echo(string value, int repeat)",
            Subject: ApiChangeSubject.Member(oldType, oldType.Members.Single(), newType, newType.Members.Single()));

        var view = DiffOutputFormatter.BuildDetailedChangesView(
            "Sample",
            [new TypeDiff("Sample.Widget", [change])],
            "old.dll",
            "new.dll");

        var row = Assert.Single(view.Rows!);
        Assert.Equal("x", row.Change);
        Assert.Equal("breaking", row.Classification);
        Assert.Equal("Widget", row.Type);
        Assert.StartsWith("Echo~", row.Member);
        Assert.Equal("MemberSignatureChanged", row.Kind);
        Assert.Equal("string Echo(string value)", row.Old);
        Assert.Equal("string Echo(string value, int repeat)", row.New);
    }

    [Fact]
    public void BuildFindingTransitions_TypeIntroduction_IsNativeAddedPair()
    {
        var oldSurface = new ApiSurface();
        var newSurface = DiffSurface(DiffType("Sample", "Widget"));

        var rows = DiffCommand.BuildFindingTransitions(
            oldSurface,
            newSurface,
            "1.0.0",
            "2.0.0",
            new DiffOptions { TypeFilter = ["Sample.Widget"] });

        var row = Assert.Single(rows);
        Assert.Equal("PairFinding.Added", row.Transition);
        Assert.Equal("api.type", row.Finding);
        Assert.Equal("Sample.Widget", row.Target);
        Assert.Equal("1.0.0", row.From);
        Assert.Equal("2.0.0", row.To);
        Assert.Equal("absent", row.Old);
        Assert.Equal("present", row.New);
    }

    [Fact]
    public void BuildFindingTransitions_TypePresent_ReportsBothSides()
    {
        var oldSurface = DiffSurface(DiffType("Sample", "Widget"));
        var newSurface = DiffSurface(DiffType("Sample", "Widget"));

        var row = Assert.Single(DiffCommand.BuildFindingTransitions(
            oldSurface,
            newSurface,
            "1.0.0",
            "1.0.1",
            new DiffOptions { TypeFilter = ["Widget"] }));

        Assert.Equal("PairFinding.Present", row.Transition);
        Assert.Equal("present", row.Old);
        Assert.Equal("present", row.New);
    }

    [Fact]
    public void BuildFindingTransitions_TypeChanged_ReportsNativeDetail()
    {
        var oldType = DiffType("Sample", "Widget");
        var newType = DiffType("Sample", "Widget");
        newType.IsSealed = true;

        var row = Assert.Single(DiffCommand.BuildFindingTransitions(
            DiffSurface(oldType),
            DiffSurface(newType),
            "1.0.0",
            "2.0.0",
            new DiffOptions { TypeFilter = ["Widget"] }));

        Assert.Equal("PairFinding.Changed", row.Transition);
        Assert.Contains("sealed: false -> true", row.Detail);
        Assert.Equal("present", row.Old);
        Assert.Equal("present", row.New);
    }

    [Fact]
    public void BuildFindingTransitions_TypeMissingAtBothEndpoints_HasNoPair()
    {
        var rows = DiffCommand.BuildFindingTransitions(
            DiffSurface(DiffType("Sample", "Existing")),
            DiffSurface(DiffType("Sample", "Existing")),
            "1.0.0",
            "2.0.0",
            new DiffOptions { TypeFilter = ["Sample.Missing"] });

        Assert.Empty(rows);
    }

    [Fact]
    public void BuildFindingTransitions_MemberIntroduction_UsesResolvedTarget()
    {
        var oldSurface = DiffSurface(DiffMember("Existing"));
        var newSurface = DiffSurface(DiffMember("Existing"), DiffMember("Added"));

        var row = Assert.Single(DiffCommand.BuildFindingTransitions(
            oldSurface,
            newSurface,
            "1.0.0",
            "2.0.0",
            new DiffOptions
            {
                TypeFilter = ["Sample.Widget"],
                MemberFilter = ["Added"]
            }));

        Assert.Equal("PairFinding.Added", row.Transition);
        Assert.Equal("api.member", row.Finding);
        Assert.StartsWith("Sample.Widget.Added~", row.Target);
        Assert.Equal("absent", row.Old);
        Assert.Equal("present", row.New);
    }

    [Fact]
    public void BuildFindingTransitions_RemovedMember_ReportsOldSide()
    {
        var oldSurface = DiffSurface(DiffMember("Removed"));
        var newSurface = DiffSurface(DiffType("Sample", "Widget"));

        var row = Assert.Single(DiffCommand.BuildFindingTransitions(
            oldSurface,
            newSurface,
            "1.0.0",
            "2.0.0",
            new DiffOptions
            {
                TypeFilter = ["Sample.Widget"],
                MemberFilter = ["Removed"]
            }));

        Assert.Equal("PairFinding.Removed", row.Transition);
        Assert.Equal("present", row.Old);
        Assert.Equal("absent", row.New);
    }

    [Fact]
    public void BuildFindingTransitions_MemberMissingAtBothEndpoints_IsInvalidTarget()
    {
        var surface = DiffSurface(DiffType("Sample", "Widget"));

        var error = Assert.Throws<InvalidOperationException>(() =>
            DiffCommand.BuildFindingTransitions(
                surface,
                surface,
                "1.0.0",
                "2.0.0",
                new DiffOptions
                {
                    TypeFilter = ["Sample.Widget"],
                    MemberFilter = ["Missing"]
                }));

        Assert.Contains("No members matched selector 'Missing'", error.Message);
    }

    [Fact]
    public void RenderFindingTransitionsMarkdown_LabelsPairBoundary()
    {
        var view = DiffOutputFormatter.BuildFindingTransitionsView(
            "Sample",
            [new FindingTransitionRow(
                "PairFinding.Added",
                "api.type",
                "Sample.Widget",
                "1.0.0",
                "2.0.0",
                "absent",
                "present",
                null)],
            "1.0.0",
            "2.0.0");

        var markdown = DiffOutputFormatter.RenderFindingTransitionsView(view);

        Assert.Contains("## Finding Transitions", markdown);
        Assert.Contains("PairFinding.Added", markdown);
        Assert.Contains("Sample.Widget", markdown);
        Assert.Contains("1.0.0", markdown);
        Assert.Contains("2.0.0", markdown);
    }

    [Fact]
    public async Task BuildApiDiff_EmptyMemberFilteredDiff_ReportsMemberFilterNotTypeFilter()
    {
        var (_, _, error) = await ConsoleCapture.RunAsync(() =>
        {
            var oldSurface = DiffSurface(
                DiffMember("Keep", signature: "void Keep()"),
                DiffMember("Changed", signature: "void Changed()"));
            var newSurface = DiffSurface(
                DiffMember("Keep", signature: "void Keep()"),
                DiffMember("Changed", signature: "int Changed()"));

            DiffCommand.BuildApiDiff(oldSurface, newSurface, new DiffOptions
            {
                TypeFilter = ["Widget"],
                MemberFilter = ["Keep"]
            });
            return Task.FromResult(0);
        });

        Assert.Contains("member filter matched no changed members", error);
        Assert.DoesNotContain("type filter matched no changed types", error);
    }

    [Fact]
    public async Task BuildApiDiff_ZeroChangeDiffWithMemberFilter_DoesNotBlameMemberFilter()
    {
        var (_, _, error) = await ConsoleCapture.RunAsync(() =>
        {
            var surface = DiffSurface(DiffMember("Keep", signature: "void Keep()"));
            DiffCommand.BuildApiDiff(surface, surface, new DiffOptions
            {
                TypeFilter = ["Widget"],
                MemberFilter = ["Keep"]
            });
            return Task.FromResult(0);
        });

        Assert.DoesNotContain("member filter matched no changed members", error);
        Assert.DoesNotContain("type filter matched no changed types", error);
    }

    [Fact]
    public async Task BuildApiDiff_TypeFilterWithNoChangedTypesAndMemberFilter_ReportsTypeFilter()
    {
        var (_, _, error) = await ConsoleCapture.RunAsync(() =>
        {
            var oldSurface = DiffSurface(
                DiffType("Sample", "Quiet", DiffMember("Keep", signature: "void Keep()")),
                DiffType("Sample", "Changed", DiffMember("Changed", signature: "void Changed()")));
            var newSurface = DiffSurface(
                DiffType("Sample", "Quiet", DiffMember("Keep", signature: "void Keep()")),
                DiffType("Sample", "Changed", DiffMember("Changed", signature: "int Changed()")));

            DiffCommand.BuildApiDiff(oldSurface, newSurface, new DiffOptions
            {
                TypeFilter = ["Quiet"],
                MemberFilter = ["Keep"]
            });
            return Task.FromResult(0);
        });

        Assert.Contains("type filter matched no changed types: Quiet", error);
        Assert.DoesNotContain("member filter matched no changed members", error);
    }

    [Fact]
    public async Task TypeFilterMissWithMemberFilter_DoesNotPrintDuplicateTypeFilterNotes()
    {
        var oldSurface = DiffSurface(
            DiffType("Sample", "Quiet", DiffMember("Keep", signature: "void Keep()")),
            DiffType("Sample", "Changed", DiffMember("Changed", signature: "void Changed()")));
        var newSurface = DiffSurface(
            DiffType("Sample", "Quiet", DiffMember("Keep", signature: "void Keep()")),
            DiffType("Sample", "Changed", DiffMember("Changed", signature: "int Changed()")));

        var (_, _, error) = await ConsoleCapture.RunAsync(() =>
        {
            var diff = DiffCommand.BuildApiDiff(oldSurface, newSurface, new DiffOptions
            {
                TypeFilter = ["Quiet"],
                MemberFilter = ["Sample.Changed.Changed"]
            });
            DiffCommand.ApplyFilters(diff, new DiffOptions
            {
                TypeFilter = ["Quiet"],
                MemberFilter = ["Sample.Changed.Changed"]
            });
            return Task.FromResult(0);
        });

        Assert.Equal(1, CountOccurrences(error, "type filter matched no changed types: Quiet"));
        Assert.DoesNotContain("member filter matched no changed members", error);
    }

    [Fact]
    public async Task ApplyFilters_ClassificationFilteredMemberDiff_ReportsClassificationFilter()
    {
        var type = DiffType("Sample", "Widget", DiffMember("Added", signature: "void Added()"));
        var diff = new ApiDiff
        {
            TypeDiffs =
            [
                new TypeDiff("Sample.Widget",
                [
                    new ApiChange(
                        ChangeKind.MemberAdded,
                        ChangeClassification.Additive,
                        "Member 'Added' was added",
                        NewValue: "void Added()",
                        Subject: ApiChangeSubject.Member(null, null, type, type.Members.Single()))
                ])
            ]
        };

        var (_, _, error) = await ConsoleCapture.RunAsync(() =>
        {
            DiffCommand.ApplyFilters(diff, new DiffOptions
            {
                TypeFilter = ["Widget"],
                MemberFilter = ["Added"],
                Breaking = true
            });
            return Task.FromResult(0);
        });

        Assert.Contains("classification filter removed all changes", error);
        Assert.DoesNotContain("member filter matched no changed members", error);
    }

    private static int CountOccurrences(string value, string substring)
    {
        var count = 0;
        var index = 0;
        while ((index = value.IndexOf(substring, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += substring.Length;
        }
        return count;
    }

    [Fact]
    public void BuildApiDiff_UsesResearchSignatureScopeByDefault()
    {
        var oldSurface = DiffSurface(DiffMember("Existing", ["A"]));
        var newSurface = DiffSurface(DiffMember("Existing", ["B"]));

        var diff = DiffCommand.BuildApiDiff(oldSurface, newSurface, new DiffOptions());

        Assert.Empty(diff.TypeDiffs);
    }

    [Fact]
    public void BuildApiDiff_MemberFilter_UsesTargetResolverWithTypeFilter()
    {
        var oldSurface = DiffSurface(
            DiffMember("Changed", signature: "void Changed()"),
            DiffMember("Other", signature: "void Other()"));
        var newSurface = DiffSurface(
            DiffMember("Changed", signature: "int Changed()"),
            DiffMember("Other", signature: "int Other()"));

        var diff = DiffCommand.BuildApiDiff(oldSurface, newSurface, new DiffOptions
        {
            TypeFilter = ["Widget"],
            MemberFilter = ["Changed"]
        });

        var typeDiff = Assert.Single(diff.TypeDiffs);
        var change = Assert.Single(typeDiff.Changes);
        Assert.Equal(ChangeKind.MemberSignatureChanged, change.Kind);
        Assert.Contains("Changed", change.Message);
    }

    [Fact]
    public void BuildApiDiff_MemberFilter_UsesTypeQualifiedSelector()
    {
        var oldSurface = DiffSurface(
            DiffMember("Changed", signature: "void Changed()"),
            DiffMember("Other", signature: "void Other()"));
        var newSurface = DiffSurface(
            DiffMember("Changed", signature: "int Changed()"),
            DiffMember("Other", signature: "int Other()"));

        var diff = DiffCommand.BuildApiDiff(oldSurface, newSurface, new DiffOptions
        {
            MemberFilter = ["Sample.Widget.Changed"]
        });

        var typeDiff = Assert.Single(diff.TypeDiffs);
        var change = Assert.Single(typeDiff.Changes);
        Assert.Equal(ChangeKind.MemberSignatureChanged, change.Kind);
        Assert.Contains("Changed", change.Message);
    }

    [Fact]
    public void BuildApiDiff_MemberFilter_RejectsAmbiguousTypeContext()
    {
        var oldSurface = DiffSurface("A", "Widget", DiffMember("Changed", signature: "void Changed()"));
        var newSurface = DiffSurface("A", "Widget", DiffMember("Changed", signature: "int Changed()"));
        oldSurface.Types.Add(DiffType("B", "Widget", DiffMember("Changed", signature: "void Changed()")));
        newSurface.Types.Add(DiffType("B", "Widget", DiffMember("Changed", signature: "int Changed()")));

        var error = Assert.Throws<InvalidOperationException>(() =>
            DiffCommand.BuildApiDiff(oldSurface, newSurface, new DiffOptions
            {
                TypeFilter = ["Widget"],
                MemberFilter = ["Changed"]
            }));

        Assert.Contains("ambiguous", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("A.Widget", error.Message);
        Assert.Contains("B.Widget", error.Message);
    }

    [Fact]
    public void BuildApiDiff_MemberFilter_PreservesWholeTypeAddedForTargetedMember()
    {
        var oldSurface = new ApiSurface();
        var newSurface = DiffSurface("Sample", "Widget", DiffMember("Added", signature: "void Added()"));

        var diff = DiffCommand.BuildApiDiff(oldSurface, newSurface, new DiffOptions
        {
            MemberFilter = ["Sample.Widget.Added"]
        });

        var typeDiff = Assert.Single(diff.TypeDiffs);
        var change = Assert.Single(typeDiff.Changes);
        Assert.Equal(ChangeKind.TypeAdded, change.Kind);
        Assert.Equal("Sample.Widget", typeDiff.TypeFullName);
    }

    [Fact]
    public void BuildApiDiff_MemberFilter_AllowsAddedMemberMissingOnOldSide()
    {
        var oldSurface = DiffSurface(DiffMember("Existing", signature: "void Existing()"));
        var newSurface = DiffSurface(
            DiffMember("Existing", signature: "void Existing()"),
            DiffMember("Added", signature: "void Added()"));

        var diff = DiffCommand.BuildApiDiff(oldSurface, newSurface, new DiffOptions
        {
            TypeFilter = ["Widget"],
            MemberFilter = ["Added"]
        });

        var typeDiff = Assert.Single(diff.TypeDiffs);
        var change = Assert.Single(typeDiff.Changes);
        Assert.Equal(ChangeKind.MemberAdded, change.Kind);
        Assert.Contains("Added", change.Message);
    }

    [Fact]
    public void BuildApiDiff_MemberFilter_AllowsRemovedMemberMissingOnNewSide()
    {
        var oldSurface = DiffSurface(
            DiffMember("Existing", signature: "void Existing()"),
            DiffMember("Removed", signature: "void Removed()"));
        var newSurface = DiffSurface(DiffMember("Existing", signature: "void Existing()"));

        var diff = DiffCommand.BuildApiDiff(oldSurface, newSurface, new DiffOptions
        {
            TypeFilter = ["Widget"],
            MemberFilter = ["Removed"]
        });

        var typeDiff = Assert.Single(diff.TypeDiffs);
        var change = Assert.Single(typeDiff.Changes);
        Assert.Equal(ChangeKind.MemberRemoved, change.Kind);
        Assert.Contains("Removed", change.Message);
    }

    [Fact]
    public void BuildApiDiff_MemberFilter_AllowsAddedOverloadOutOfRangeOnOldSide()
    {
        var oldSurface = DiffSurface(DiffMember("Changed", signature: "void Changed()"));
        var newSurface = DiffSurface(
            DiffMember("Changed", signature: "void Changed()"),
            DiffMember("Changed", signature: "void Changed(int value)"));

        var diff = DiffCommand.BuildApiDiff(oldSurface, newSurface, new DiffOptions
        {
            TypeFilter = ["Widget"],
            MemberFilter = ["Changed:2"]
        });

        var typeDiff = Assert.Single(diff.TypeDiffs);
        var change = Assert.Single(typeDiff.Changes);
        Assert.Equal(ChangeKind.MemberAdded, change.Kind);
        Assert.Contains("Changed", change.Message);
    }

    [Fact]
    public void BuildApiDiff_MemberFilter_AllowsRemovedOverloadOutOfRangeOnNewSide()
    {
        var oldSurface = DiffSurface(
            DiffMember("Changed", signature: "void Changed()"),
            DiffMember("Changed", signature: "void Changed(int value)"));
        var newSurface = DiffSurface(DiffMember("Changed", signature: "void Changed()"));

        var diff = DiffCommand.BuildApiDiff(oldSurface, newSurface, new DiffOptions
        {
            TypeFilter = ["Widget"],
            MemberFilter = ["Changed:2"]
        });

        var typeDiff = Assert.Single(diff.TypeDiffs);
        var change = Assert.Single(typeDiff.Changes);
        Assert.Equal(ChangeKind.MemberRemoved, change.Kind);
        Assert.Contains("Changed", change.Message);
    }

    [Fact]
    public void BuildApiDiff_MemberFilter_PreservesOutOfRangeDiagnosticWhenNeitherSideResolves()
    {
        var oldSurface = DiffSurface(DiffMember("Changed", signature: "void Changed()"));
        var newSurface = DiffSurface(DiffMember("Changed", signature: "void Changed()"));

        var error = Assert.Throws<InvalidOperationException>(() =>
            DiffCommand.BuildApiDiff(oldSurface, newSurface, new DiffOptions
            {
                TypeFilter = ["Widget"],
                MemberFilter = ["Changed:2"]
            }));

        Assert.Contains("out of range", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Changed:1", error.Message);
    }

    [Fact]
    public void BuildApiDiff_MemberFilter_PreservesResolverAmbiguityDiagnostic()
    {
        var oldSurface = DiffSurface(
            DiffMember("Changed", signature: "void Changed()"),
            DiffMember("Changed", signature: "void Changed(int value)"));
        var newSurface = DiffSurface(
            DiffMember("Changed", signature: "void Changed()"),
            DiffMember("Changed", signature: "void Changed(int value)"));

        var error = Assert.Throws<InvalidOperationException>(() =>
            DiffCommand.BuildApiDiff(oldSurface, newSurface, new DiffOptions
            {
                TypeFilter = ["Widget"],
                MemberFilter = ["Changed"]
            }));

        Assert.Contains("ambiguous", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Changed:1", error.Message);
    }

    [Fact]
    public void BuildApiDiff_MemberFilter_RejectsAmbiguityEvenWhenOtherSideResolves()
    {
        var oldSurface = DiffSurface(DiffMember("Changed", signature: "void Changed()"));
        var newSurface = DiffSurface(
            DiffMember("Changed", signature: "void Changed()"),
            DiffMember("Changed", signature: "void Changed(int value)"));

        var error = Assert.Throws<InvalidOperationException>(() =>
            DiffCommand.BuildApiDiff(oldSurface, newSurface, new DiffOptions
            {
                TypeFilter = ["Widget"],
                MemberFilter = ["Changed"]
            }));

        Assert.Contains("ambiguous", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Changed:1", error.Message);
    }

    [Fact]
    public void BuildApiDiff_GenericMemberFilter_ExcludesSameNameNonGenericOverload()
    {
        var oldSurface = DiffSurface(
            DiffMember("GenericChoice", signature: "string GenericChoice(string value)"),
            DiffMember("GenericChoice", signature: "T GenericChoice<T>(T value)", genericArity: 1));
        var newSurface = DiffSurface(
            DiffMember("GenericChoice", signature: "string GenericChoice(string value)"),
            DiffMember("GenericChoice", signature: "T GenericChoice<T>(T value, int count)", genericArity: 1));

        var diff = DiffCommand.BuildApiDiff(oldSurface, newSurface, new DiffOptions
        {
            TypeFilter = ["Widget"],
            MemberFilter = ["GenericChoice<T>"]
        });

        var typeDiff = Assert.Single(diff.TypeDiffs);
        var change = Assert.Single(typeDiff.Changes);
        Assert.Equal(ChangeKind.MemberSignatureChanged, change.Kind);
        Assert.Contains("<T>", change.NewValue);
    }

    private static DiffCommand.RankedAnalysisRow Ranked(string member, string signal, int magnitude, int direction, bool inBoth, bool inLoop = false)
        => new(new AnalysisDiffRow($"`{member}`", signal, "0", magnitude.ToString(), $"+{magnitude}", inLoop ? "in-loop" : null, null), magnitude, direction, inBoth, inLoop);

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
    public void RankAnalysisRows_AllocRegressionsOnly_KeepsOnlyInPlaceAllocationIncreases()
    {
        var input = new List<DiffCommand.RankedAnalysisRow>
        {
            Ranked("Type.AllocUp()", "allocations", 3, +1, inBoth: true),
            Ranked("Type.AllocDown()", "allocations", 2, -1, inBoth: true),   // improvement, dropped
            Ranked("Type.ThrowsUp()", "throws", 9, +1, inBoth: true),          // non-allocation, dropped
            Ranked("Type.Added()", "allocations", 5, +1, inBoth: false),       // added member, dropped
        };

        var result = DiffCommand.RankAnalysisRows(input, changedOnly: false, allocRegressionsOnly: true);

        Assert.Single(result.Rows);
        Assert.Equal("`Type.AllocUp()`", result.Rows[0].Member);
        Assert.Equal("1 allocation regression (1 signal).", result.Summary);
    }

    [Fact]
    public void RankAnalysisRows_AllocRegressionsOnly_SurfacesInLoopRegressionsFirst()
    {
        var input = new List<DiffCommand.RankedAnalysisRow>
        {
            Ranked("Type.OneTime()", "allocations", 8, +1, inBoth: true, inLoop: false),   // large but not hot
            Ranked("Type.Hot()", "allocations", 1, +1, inBoth: true, inLoop: true),        // small but in-loop
        };

        var result = DiffCommand.RankAnalysisRows(input, changedOnly: false, allocRegressionsOnly: true);

        // In focus mode, in-loop (hot) allocation regressions outrank larger one-time increases.
        Assert.Equal("`Type.Hot()`", result.Rows[0].Member);
        Assert.Equal("`Type.OneTime()`", result.Rows[1].Member);
        Assert.Equal("2 allocation regressions, 1 in loop (2 signals).", result.Summary);
    }

    [Fact]
    public void BuildAllocRegressionSummary_FormatsCountAndInLoop()
    {
        Assert.Equal("No allocation regressions detected.", DiffCommand.BuildAllocRegressionSummary(total: 0, inLoop: 0));
        Assert.Equal("1 allocation regression (1 signal).", DiffCommand.BuildAllocRegressionSummary(total: 1, inLoop: 0));
        Assert.Equal("3 allocation regressions, 2 in loop (3 signals).", DiffCommand.BuildAllocRegressionSummary(total: 3, inLoop: 2));
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

    // #1623 rung 6: end-to-end version-pair diff. V1/V2 share AssemblyName and method
    // signatures (matched in-place); only bodies differ. A seeded in-loop allocation
    // regression and an allocation improvement must surface, and an unchanged method
    // must not produce a row.
    [Fact]
    public void BuildAnalysisDiff_VersionPair_SurfacesSeededRegressionAndImprovement()
    {
        var v1 = FixtureCatalog.DiffPair.OldAssemblyPath();
        var v2 = FixtureCatalog.DiffPair.NewAssemblyPath();

        var result = DiffCommand.BuildAnalysisDiff([v1], [v2], new DiffOptions { ChangedOnly = true });

        var regression = Assert.Single(result.Rows, r =>
            r.Member.Contains("RegressesAllocInLoop") && r.Signal == "allocations");
        Assert.Equal("1", regression.Old);
        Assert.Equal("2", regression.New);
        Assert.Equal("in-loop", regression.Shape);

        var improvement = Assert.Single(result.Rows, r =>
            r.Member.Contains("ImprovesAlloc") && r.Signal == "allocations");
        Assert.Equal("3", improvement.Old);
        Assert.Equal("1", improvement.New);

        Assert.DoesNotContain(result.Rows, r => r.Member.Contains("Stable"));
    }

    [Fact]
    public void BuildAnalysisDiff_MemberFilter_UsesResolverBackedTarget()
    {
        var v1 = FixtureCatalog.DiffPair.OldAssemblyPath();
        var v2 = FixtureCatalog.DiffPair.NewAssemblyPath();

        var result = DiffCommand.BuildAnalysisDiff([v1], [v2], new DiffOptions
        {
            TypeFilter = ["DiffSample"],
            MemberFilter = ["RegressesAllocInLoop"],
            ChangedOnly = true
        });

        var row = Assert.Single(result.Rows);
        Assert.Contains("RegressesAllocInLoop", row.Member);
        Assert.Equal("allocations", row.Signal);
        Assert.DoesNotContain(result.Rows, r => r.Member.Contains("ImprovesAlloc"));
    }

    [Fact]
    public void BuildAnalysisDiff_MemberFilter_RejectsNonMethodTargets()
    {
        var surface = DiffSurface(DiffProperty("Value"));

        var error = Assert.Throws<InvalidOperationException>(() =>
            DiffCommand.BuildAnalysisDiff([], [], new DiffOptions
            {
                TypeFilter = ["Widget"],
                MemberFilter = ["Value"]
            }, surface, surface));

        Assert.Contains("method-like target", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("int*", "System.Int32*")]
    [InlineData("int[,]", "System.Int32[,]")]
    [InlineData("dynamic", "System.Object")]
    [InlineData("nint", "System.IntPtr")]
    [InlineData("params int[]", "System.Int32[]")]
    [InlineData("pinned int", "pinned System.Int32")]
    [InlineData("System.Collections.Generic.List<int>", "System.Collections.Generic.List`1<System.Int32>")]
    [InlineData("System.Collections.Generic.List<int>.Enumerator", "System.Collections.Generic.List`1.Enumerator<System.Int32>")]
    [InlineData("Outer<int>.Inner<string>", "Outer`1.Inner`1<System.Int32,System.String>")]
    public void ResearchBodyTypeName_MatchesResearchDiffSubjectSpelling(string input, string expected)
    {
        Assert.Equal(expected, ResearchMemberIdentity.BodyParameterTypeName(input));
    }

    [Theory]
    [InlineData("Namespace.Outer<T>.Inner<U>", "Namespace.Outer.Inner")]
    [InlineData("Namespace.Outer+Inner<T>", "Namespace.Outer.Inner")]
    [InlineData("System.Int32", "int")]
    [InlineData("System.String", "string")]
    public void ResearchBodyDeclaringTypeName_StripsApiTypeParameters(string input, string expected)
    {
        Assert.Equal(expected, ResearchMemberIdentity.BodyDeclaringTypeName(input));
    }

    [Fact]
    public void ResearchBodyTypeName_KeepsFullPrimitiveParameterNames()
    {
        Assert.Equal("System.Int32", ResearchMemberIdentity.BodyParameterTypeName("System.Int32"));
    }

    [Fact]
    public void AddResearchBodyIdentity_PhysicalExtensionUsesExtensionSelector()
    {
        var type = new ApiType { Namespace = "DiffFixtureSample", Name = "ExtensionSample" };
        var member = DiffMember("Twice", signature: "int Twice(int value)");
        member.IsExtension = true;
        member.DeclaringType = "DiffFixtureSample.ExtensionSample";
        var target = ResolvedTarget(type, member);
        HashSet<string> identities = new(StringComparer.Ordinal);

        Assert.True(ResearchMemberIdentity.TryAddTargetIdentity(target, identities));

        var expectedCanonical = "M:DiffFixtureSample.ExtensionSample.Twice(System.Int32)";
        Assert.Contains($"extension:Twice~{MemberAnchor.ComputeFingerprint(expectedCanonical)}", identities);
    }

    [Fact]
    public void AddResearchBodyIdentity_ProjectedExtensionUsesPhysicalDeclaringType()
    {
        var extendedType = new ApiType { Namespace = "System", Name = "Int32" };
        var member = DiffMember("Twice", signature: "int Twice(int value)");
        member.Kind = "extension-method";
        member.IsExtension = true;
        member.DeclaringType = "DiffFixtureSample.ExtensionSample";
        var target = ResolvedTarget(
            extendedType,
            member,
            new BodyTarget("DiffFixtureSample.ExtensionSample", "M:DiffFixtureSample.ExtensionSample.Twice(System.Int32)"));
        HashSet<string> identities = new(StringComparer.Ordinal);

        Assert.True(ResearchMemberIdentity.TryAddTargetIdentity(target, identities));

        var expectedCanonical = "M:DiffFixtureSample.ExtensionSample.Twice(System.Int32)";
        Assert.Contains($"extension:Twice~{MemberAnchor.ComputeFingerprint(expectedCanonical)}", identities);
    }

    [Fact]
    public void ResearchBodyIdentity_TargetAliasMatchesMethodSubjectCanonicalFormatter()
    {
        AssertTargetAliasMatchesMethodSubject(
            DiffType("Sample", "Widget", DiffMember("M", signature: "void M(int[] values)")),
            DiffMember("M", signature: "void M(int[] values)"),
            new MethodIdentity(
                "Asm",
                Guid.Empty,
                TypeRef.Definition("Asm", "Sample", "Widget"),
                "M",
                [TypeRef.SzArray(TypeRef.CoreLib("System", "Int32"))],
                TypeRef.CoreLib("System", "Void"),
                MetadataToken: 0x06000000,
                IsStatic: true));

        AssertTargetAliasMatchesMethodSubject(
            DiffType("Sample", "Widget", DiffMember("M", signature: "void M(ref int value)")),
            DiffMember("M", signature: "void M(ref int value)"),
            new MethodIdentity(
                "Asm",
                Guid.Empty,
                TypeRef.Definition("Asm", "Sample", "Widget"),
                "M",
                [TypeRef.ByRef(TypeRef.CoreLib("System", "Int32"))],
                TypeRef.CoreLib("System", "Void"),
                MetadataToken: 0x06000000,
                IsStatic: true));

        AssertTargetAliasMatchesMethodSubject(
            DiffType("Sample", "Widget", DiffMember("M", signature: "void M(int* value)")),
            DiffMember("M", signature: "void M(int* value)"),
            new MethodIdentity(
                "Asm",
                Guid.Empty,
                TypeRef.Definition("Asm", "Sample", "Widget"),
                "M",
                [TypeRef.Pointer(TypeRef.CoreLib("System", "Int32"))],
                TypeRef.CoreLib("System", "Void"),
                MetadataToken: 0x06000001,
                IsStatic: true));

        AssertTargetAliasMatchesMethodSubject(
            DiffType("Sample", "Widget", DiffMember("M", signature: "T M<T>(T value)", genericArity: 1)),
            DiffMember("M", signature: "T M<T>(T value)", genericArity: 1),
            new MethodIdentity(
                "Asm",
                Guid.Empty,
                TypeRef.Definition("Asm", "Sample", "Widget"),
                "M",
                [TypeRef.MethodGenericParameter(0, "T")],
                TypeRef.MethodGenericParameter(0, "T"),
                MetadataToken: 0x06000002,
                IsStatic: true,
                GenericArity: 1,
                GenericParameterNames: ["T"]));

        var op = DiffMember("op_Addition", signature: "Sample.Widget op_Addition(Sample.Widget left, Sample.Widget right)");
        op.Kind = "operator";
        AssertTargetAliasMatchesMethodSubject(
            DiffType("Sample", "Widget", op),
            op,
            new MethodIdentity(
                "Asm",
                Guid.Empty,
                TypeRef.Definition("Asm", "Sample", "Widget"),
                "op_Addition",
                [TypeRef.Definition("Asm", "Sample", "Widget"), TypeRef.Definition("Asm", "Sample", "Widget")],
                TypeRef.Definition("Asm", "Sample", "Widget"),
                MetadataToken: 0x06000003,
                IsStatic: true));

        var explicitImpl = DiffMember("IFoo.Bar", signature: "void IFoo.Bar()");
        explicitImpl.Kind = "explicit-interface-implementation";
        AssertTargetAliasMatchesMethodSubject(
            DiffType("Sample", "Widget", explicitImpl),
            explicitImpl,
            new MethodIdentity(
                "Asm",
                Guid.Empty,
                TypeRef.Definition("Asm", "Sample", "Widget"),
                "IFoo.Bar",
                [],
                TypeRef.CoreLib("System", "Void"),
                MetadataToken: 0x06000004,
                IsStatic: false));

        var constructor = new ApiMember
        {
            Name = ".ctor",
            Kind = "constructor",
            Signature = "void .ctor()",
            SignatureModel = new ApiSignature { MemberName = "#ctor" }
        };
        AssertTargetAliasMatchesMethodSubject(
            DiffType("Sample", "Widget", constructor),
            constructor,
            new MethodIdentity(
                "Asm",
                Guid.Empty,
                TypeRef.Definition("Asm", "Sample", "Widget"),
                ".ctor",
                [],
                TypeRef.CoreLib("System", "Void"),
                MetadataToken: 0x06000005,
                IsStatic: false));

        var extensionType = new ApiType { Namespace = "Sample", Name = "Extensions" };
        var extension = DiffMember("Twice", signature: "int Twice(int value)");
        extension.IsExtension = true;
        extension.DeclaringType = "Sample.Extensions";
        AssertTargetAliasMatchesMethodSubject(
            extensionType,
            extension,
            new MethodIdentity(
                "Asm",
                Guid.Empty,
                TypeRef.Definition("Asm", "Sample", "Extensions"),
                "Twice",
                [TypeRef.CoreLib("System", "Int32")],
                TypeRef.CoreLib("System", "Int32"),
                MetadataToken: 0x06000006,
                IsStatic: true,
                IsExtension: true));
    }

    // #1736: a hotness-only allocation regression. The allocation count is unchanged
    // (1 -> 1) but the allocation moves into a loop (allocInLoop false -> true). The raw
    // count delta is zero, so this must still surface as an in-place allocations row with
    // a "hot" delta and "in-loop" shape, and must be kept by allocation-regression focus.
    [Fact]
    public void BuildAnalysisDiff_VersionPair_SurfacesHotnessOnlyAllocationRegression()
    {
        var v1 = FixtureCatalog.DiffPair.OldAssemblyPath();
        var v2 = FixtureCatalog.DiffPair.NewAssemblyPath();

        var result = DiffCommand.BuildAnalysisDiff([v1], [v2], new DiffOptions { ChangedOnly = true });

        var hot = Assert.Single(result.Rows, r =>
            r.Member.Contains("SameAllocationCountBecomesHot") && r.Signal == "allocations");
        Assert.Equal("1", hot.Old);
        Assert.Equal("1", hot.New);
        Assert.Equal("hot", hot.Delta);
        Assert.Equal("in-loop", hot.Shape);

        // It is a regression, so allocation-regression focus mode keeps it.
        var focus = DiffCommand.BuildAnalysisDiff([v1], [v2], new DiffOptions { AllocRegressionsOnly = true });
        Assert.Contains(focus.Rows, r =>
            r.Member.Contains("SameAllocationCountBecomesHot") && r.Signal == "allocations" && r.Shape == "in-loop");

        // Reversed (V2 -> V1): the allocation leaves the loop. Count is still 1 -> 1, but it
        // surfaces as a "cold" improvement annotated in-loop (the old, cost-bearing version
        // was hot) and must NOT be kept by allocation-regression focus.
        var reversed = DiffCommand.BuildAnalysisDiff([v2], [v1], new DiffOptions { ChangedOnly = true });
        var cold = Assert.Single(reversed.Rows, r =>
            r.Member.Contains("SameAllocationCountBecomesHot") && r.Signal == "allocations");
        Assert.Equal("cold", cold.Delta);
        Assert.Equal("in-loop", cold.Shape);

        var reversedFocus = DiffCommand.BuildAnalysisDiff([v2], [v1], new DiffOptions { AllocRegressionsOnly = true });
        Assert.DoesNotContain(reversedFocus.Rows, r => r.Member.Contains("SameAllocationCountBecomesHot"));
    }

    // #1623 rung 6: identity / no spurious deltas. Diffing a build against itself must
    // produce zero in-place signal changes (guards signal determinism across two opens
    // and the zero-delta filter).
    [Fact]
    public void BuildAnalysisDiff_Identity_SelfDiffHasNoSpuriousDeltas()
    {
        var v1 = FixtureCatalog.DiffPair.OldAssemblyPath();

        // Full diff: self-diff must have neither in-place deltas nor spurious
        // added/removed rows.
        var full = DiffCommand.BuildAnalysisDiff([v1], [v1], new DiffOptions());
        Assert.Empty(full.Rows);
        Assert.Equal("No analysis signal changes detected.", full.Summary);

        // Changed-only view agrees.
        var changedOnly = DiffCommand.BuildAnalysisDiff([v1], [v1], new DiffOptions { ChangedOnly = true });
        Assert.Empty(changedOnly.Rows);
        Assert.Equal("No in-place analysis signal changes detected.", changedOnly.Summary);
    }

    static void AssertTargetAliasMatchesMethodSubject(ApiType type, ApiMember member, MethodIdentity method)
    {
        HashSet<string> identities = new(StringComparer.Ordinal);

        Assert.True(ResearchMemberIdentity.TryAddTargetIdentity(ResolvedTarget(type, member), identities));

        var targetId = Assert.Single(identities);
        var methodSubject = ResearchMemberIdentity.SubjectFromMethod(method);
        Assert.Equal(methodSubject.Id, targetId);
    }

    static ApiSurface DiffSurface(params ApiMember[] members)
        => DiffSurface(DiffType("Sample", "Widget", members));

    static ApiSurface DiffSurface(params ApiType[] types)
        => new() { Types = [.. types] };

    static ApiSurface DiffSurface(string @namespace, string name, params ApiMember[] members)
        => new() { Types = [DiffType(@namespace, name, members)] };

    static ApiType DiffType(string @namespace, string name, params ApiMember[] members)
        => new()
        {
            Namespace = @namespace,
            Name = name,
            Kind = "class",
            Members = [.. members],
        };

    static ApiMember DiffMember(string name, IReadOnlyList<string>? attributes = null, string? signature = null, int genericArity = 0)
        => new()
        {
            Name = name,
            Kind = "method",
            Signature = signature ?? $"void {name}()",
            SignatureModel = new ApiSignature
            {
                MemberName = name,
                TypeParameters = [.. Enumerable.Range(0, genericArity).Select(index => new TypeParameter { Name = index == 0 ? "T" : $"T{index}" })],
                Parameters = ParseParameters(signature ?? $"void {name}()")
            },
            Attributes = attributes?.ToList() ?? [],
        };

    static ApiMember DiffProperty(string name)
        => new()
        {
            Name = name,
            Kind = "property",
            Signature = $"int {name}",
            SignatureModel = new ApiSignature { MemberName = name, ReturnType = "int" }
        };

    static ResolvedMemberTarget ResolvedTarget(ApiType type, ApiMember member, BodyTarget? body = null)
    {
        var anchor = ApiMemberIdentity.GetMemberAnchor(type, member);
        return new ResolvedMemberTarget(
            type,
            ApiMemberIdentity.CreateHandle(type, member),
            anchor,
            anchor.StableSelector,
            anchor.StableSelector,
            SelectorIndex: 1,
            DeclaringOverloadIndex: 1,
            OverloadIndex: null,
            DigestPrefix: null,
            GenericArity: member.SignatureModel?.TypeParameters.Count,
            Kind: member.Kind switch
            {
                "extension-method" => MemberTargetKind.ExtensionMethod,
                "method" => MemberTargetKind.Method,
                _ => MemberTargetKind.Unknown
            },
            Body: body);
    }

    static List<ApiParameter> ParseParameters(string signature)
    {
        var open = signature.IndexOf('(');
        var close = signature.LastIndexOf(')');
        if (open < 0 || close <= open + 1)
            return [];

        return [.. signature[(open + 1)..close]
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select((parameter, index) =>
            {
                var parts = parameter.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                return new ApiParameter
                {
                    Name = parts.Length > 1 ? parts[^1] : $"p{index}",
                    Type = parts.Length > 1 ? string.Join(' ', parts[..^1]) : parts[0]
                };
            })];
    }

    // Key-path audit: the analysis-diff member key must distinguish members that
    // TypeRef.Equals distinguishes but display strings do not — same-name/different-arity
    // generic declaring types, and same-FQN parameter types from different assemblies —
    // otherwise the snapshot merges them and the diff misses a member (sibling of #1741).
    [Fact]
    public void MethodKey_DistinguishesSameNameDifferentArityDeclaringTypes()
    {
        var box1 = DiffMethod(ILInspector.Analysis.TypeRef.Definition("Asm", "Ns", "Box`1"), "Store");
        var box2 = DiffMethod(ILInspector.Analysis.TypeRef.Definition("Asm", "Ns", "Box`2"), "Store");

        Assert.NotEqual(DiffCommand.MethodKey(box1), DiffCommand.MethodKey(box2));
    }

    [Fact]
    public void MethodKey_DistinguishesSameFqnParametersFromDifferentAssemblies()
    {
        var declaring = ILInspector.Analysis.TypeRef.Definition("Asm", "Ns", "Api");
        var pingA = DiffMethod(declaring, "Ping", ILInspector.Analysis.TypeRef.Definition("LibA", "Shared", "Token"));
        var pingB = DiffMethod(declaring, "Ping", ILInspector.Analysis.TypeRef.Definition("LibB", "Shared", "Token"));

        Assert.NotEqual(DiffCommand.MethodKey(pingA), DiffCommand.MethodKey(pingB));
    }

    static ILInspector.Analysis.MethodIdentity DiffMethod(ILInspector.Analysis.TypeRef declaring, string name, params ILInspector.Analysis.TypeRef[] parameterTypes)
        => new(
            "Asm",
            System.Guid.Empty,
            declaring,
            name,
            [.. parameterTypes],
            ILInspector.Analysis.TypeRef.CoreLib("System", "Void"),
            MetadataToken: 0x06000001,
            IsStatic: true);
}
