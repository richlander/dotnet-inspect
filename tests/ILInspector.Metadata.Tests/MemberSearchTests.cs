using ILInspector.Metadata;

namespace ILInspector.Metadata.Tests
{
    public sealed class MemberSearchTests
    {
        private static string SelfAssembly => typeof(MemberSearchProbeAlphaFixture).Assembly.Location;

        [Fact]
        public void SearchAssembly_direct_name_finds_member_with_declaring_type_and_kind()
        {
            var results = MemberSearch.SearchAssembly(SelfAssembly, ["MemberSearchProbeAlpha"]);

            var hit = Assert.Single(results);
            Assert.Equal("MemberSearchProbeAlpha", hit.MemberName);
            Assert.Equal(typeof(MemberSearchProbeAlphaFixture).FullName, hit.DeclaringType);
            Assert.Equal("method", hit.Kind);
            Assert.False(hit.IsGlob);
            Assert.Equal("MemberSearchProbeAlpha", hit.Pattern);
            Assert.Equal(0, hit.PatternOrdinal);
            Assert.Equal(
                typeof(MemberSearchProbeAlphaFixture).FullName,
                hit.DeclaringTypeName.ToMetadataFullName());
            Assert.Equal(
                "MemberSearchProbeAlpha",
                hit.Anchor.MemberName);
            Assert.NotEmpty(hit.Anchor.StableSelector);
            Assert.True(hit.DeclarationOrder >= 0);
            Assert.True(hit.MemberOrder >= 0);
        }

        [Fact]
        public void SearchAssembly_is_case_insensitive_for_direct_names()
        {
            var results = MemberSearch.SearchAssembly(SelfAssembly, ["membersearchprobealpha"]);

            Assert.Contains(results, r => r.MemberName == "MemberSearchProbeAlpha");
        }

        [Fact]
        public void SearchAssembly_glob_matches_public_members_but_not_private_by_default()
        {
            var results = MemberSearch.SearchAssembly(SelfAssembly, ["MemberSearchProbe*"]);

            var names = results.Select(r => r.MemberName).ToHashSet();
            Assert.Contains("MemberSearchProbeAlpha", names);
            Assert.Contains("MemberSearchProbeBeta", names);
            Assert.DoesNotContain("MemberSearchProbeGammaHidden", names);
            Assert.All(results.Where(r => r.MemberName.StartsWith("MemberSearchProbe")), r => Assert.True(r.IsGlob));
        }

        [Fact]
        public void SearchAssembly_includeAll_surfaces_non_public_members()
        {
            var withoutAll = MemberSearch.SearchAssembly(SelfAssembly, ["MemberSearchProbeGammaHidden"]);
            Assert.Empty(withoutAll);

            var withAll = MemberSearch.SearchAssembly(SelfAssembly, ["MemberSearchProbeGammaHidden"], includeAll: true);
            Assert.Contains(withAll, r => r.MemberName == "MemberSearchProbeGammaHidden");
        }

        [Fact]
        public void SearchAssembly_no_match_returns_empty()
        {
            var results = MemberSearch.SearchAssembly(SelfAssembly, ["NoSuchMemberXyzzy1234"]);
            Assert.Empty(results);
        }

        [Fact]
        public void SearchAssembly_direct_indexer_alias_matches_metadata_name()
        {
            var results = MemberSearch.SearchAssembly(SelfAssembly, ["this[]"]);

            MemberSearchResult result = Assert.Single(
                results,
                candidate => candidate.DeclaringType
                    == typeof(MemberSearchProbeIndexerAlias).FullName);
            Assert.Equal("this[]", result.Pattern);
            Assert.Equal("Item", result.MemberName);
            Assert.False(result.IsGlob);
        }

        [Fact]
        public void Search_across_set_aggregates_and_records_assembly_name()
        {
            var outcome = MemberSearch.Search([SelfAssembly], ["MemberSearchProbeAlpha"]);

            Assert.Empty(outcome.SkippedAssemblies);
            var hit = Assert.Single(outcome.Results);
            Assert.Equal(Path.GetFileNameWithoutExtension(SelfAssembly), hit.Assembly);
        }

        [Fact]
        public void Search_reports_unreadable_assembly_as_skipped_without_faking_success()
        {
            var missing = Path.Combine(Path.GetTempPath(), "member-search-does-not-exist-4f2a.dll");

            var outcome = MemberSearch.Search([missing], ["MemberSearchProbeAlpha"]);

            Assert.Empty(outcome.Results);
            Assert.Contains(missing, outcome.SkippedAssemblies);
        }

        [Fact]
        public void Search_respects_result_limit()
        {
            var outcome = MemberSearch.Search([SelfAssembly], ["MemberSearchProbe*"], limit: 1);

            Assert.Single(outcome.Results);
        }

        [Fact]
        public void SearchMembers_window_matches_complete_search()
        {
            IReadOnlyList<MemberSearchResult> complete =
                MemberSearch.SearchAssembly(
                    SelfAssembly,
                    ["MemberSearchProbe*"]);
            using var session =
                AssemblyInspectionSession.Open(SelfAssembly);

            MemberSearchWindowResult window =
                session.SearchMembers(
                    Path.GetFileNameWithoutExtension(SelfAssembly),
                    ["MemberSearchProbe*"],
                    includeAll: false,
                    new(1, complete.Count));

            Assert.True(window.EndReached);
            Assert.Equal(complete.Count, window.AcceptedCount);
            Assert.Equal(complete, window.Results);
            Assert.Equal(
                complete.Count,
                window.Receipt.ProjectedRows);
        }

        [Fact]
        public void SearchMembers_window_counts_preceding_matches_without_projecting_them()
        {
            IReadOnlyList<MemberSearchResult> complete =
                MemberSearch.SearchAssembly(
                    SelfAssembly,
                    ["MemberSearchProbe*"]);
            using var session =
                AssemblyInspectionSession.Open(SelfAssembly);

            MemberSearchWindowResult window =
                session.SearchMembers(
                    Path.GetFileNameWithoutExtension(SelfAssembly),
                    ["MemberSearchProbe*"],
                    includeAll: false,
                    new(2, 3));

            Assert.True(window.EndReached);
            Assert.Equal(3, window.AcceptedCount);
            Assert.Equal(complete.Skip(1).Take(2), window.Results);
            Assert.Equal(2, window.Receipt.ProjectedRows);
        }

        [Fact]
        public void SearchMembers_count_projects_no_rows()
        {
            IReadOnlyList<MemberSearchResult> complete =
                MemberSearch.SearchAssembly(
                    SelfAssembly,
                    ["MemberSearchProbe*"]);
            using var session =
                AssemblyInspectionSession.Open(SelfAssembly);

            MemberSearchWindowResult count =
                session.SearchMembers(
                    Path.GetFileNameWithoutExtension(SelfAssembly),
                    ["MemberSearchProbe*"],
                    includeAll: false,
                    new(
                        start: 1,
                        end: null,
                        materializeRows: false));

            Assert.False(count.EndReached);
            Assert.Equal(complete.Count, count.AcceptedCount);
            Assert.Empty(count.Results);
            Assert.Equal(0, count.Receipt.ProjectedRows);
        }

        [Fact]
        public void SearchMembers_declaring_type_filter_precedes_window_positions()
        {
            IReadOnlyList<MemberSearchResult> complete =
                MemberSearch.SearchAssembly(
                        SelfAssembly,
                        ["MemberSearchProbe*"])
                    .Where(result =>
                        result.DeclaringType
                        == typeof(MemberSearchProbeBetaFixture).FullName)
                    .ToArray();
            using var session =
                AssemblyInspectionSession.Open(SelfAssembly);

            MemberSearchWindowResult window =
                session.SearchMembers(
                    Path.GetFileNameWithoutExtension(SelfAssembly),
                    ["MemberSearchProbe*"],
                    includeAll: false,
                    new(1, 2),
                    name =>
                        name.ToMetadataFullName()
                        == typeof(MemberSearchProbeBetaFixture).FullName);

            Assert.True(window.EndReached);
            Assert.Equal(2, window.AcceptedCount);
            Assert.Equal(complete.Take(2), window.Results);
        }

        [Fact]
        public void SearchMembers_preserves_same_module_attached_extensions()
        {
            string assembly = typeof(object).Assembly.Location;
            IReadOnlyList<MemberSearchResult> complete =
                MemberSearch.SearchAssembly(
                    assembly,
                    ["AsSpan"]);
            using var session =
                AssemblyInspectionSession.Open(assembly);

            MemberSearchWindowResult window =
                session.SearchMembers(
                    Path.GetFileNameWithoutExtension(assembly),
                    ["AsSpan"],
                    includeAll: false,
                    new(1, complete.Count));

            Assert.NotEmpty(complete);
            Assert.Equal(complete.Count, window.AcceptedCount);
            Assert.Equal(complete, window.Results);
        }

        [Fact]
        public void SearchMembers_preserves_projected_member_shapes()
        {
            IReadOnlyList<MemberSearchResult> complete =
                MemberSearch.SearchAssembly(
                    SelfAssembly,
                    ["MemberSearchProjection*"]);
            using var session =
                AssemblyInspectionSession.Open(SelfAssembly);

            MemberSearchWindowResult window =
                session.SearchMembers(
                    Path.GetFileNameWithoutExtension(SelfAssembly),
                    ["MemberSearchProjection*"],
                    includeAll: false,
                    new(1, complete.Count));

            Assert.Equal(4, complete.Count);
            Assert.Equal(complete, window.Results);
        }

        [Fact]
        public void Search_empty_patterns_returns_empty_outcome()
        {
            var outcome = MemberSearch.Search([SelfAssembly], []);

            Assert.Empty(outcome.Results);
            Assert.Empty(outcome.SkippedAssemblies);
        }

        [Fact]
        public void Search_same_member_name_in_two_types_returns_both()
        {
            var outcome = MemberSearch.Search([SelfAssembly], ["MemberSearchProbeShared"]);

            Assert.Contains(outcome.Results, r =>
                r.MemberName == "MemberSearchProbeShared"
                && r.DeclaringType == typeof(MemberSearchProbeAlphaFixture).FullName);
            Assert.Contains(outcome.Results, r =>
                r.MemberName == "MemberSearchProbeShared"
                && r.DeclaringType == typeof(MemberSearchProbeBetaFixture).FullName);
        }
    }

    public sealed class MemberSearchProbeAlphaFixture
    {
        public int MemberSearchProbeAlpha() => 1;
        public int MemberSearchProbeShared() => 2;
        private int MemberSearchProbeGammaHidden() => 3;
    }

    public sealed class MemberSearchProbeBetaFixture
    {
        public void MemberSearchProbeBeta() { }
        public int MemberSearchProbeShared() => 4;
    }

    public sealed class MemberSearchProbeIndexerAlias
    {
        public int this[int index] => index;
    }

    public sealed class MemberSearchProjectionFixture<T>
    {
        public string? MemberSearchProjectionField;

        public string? MemberSearchProjectionProperty { get; set; }

        public event EventHandler? MemberSearchProjectionEvent;

        public T? MemberSearchProjectionMethod(
            string? value,
            T? result) =>
            result;

        public void Raise() =>
            MemberSearchProjectionEvent?.Invoke(this, EventArgs.Empty);
    }
}
