using ILInspector.Metadata;

namespace ILInspector.Metadata.Tests
{
    public sealed class MemberSearchTests
    {
        private static string SelfAssembly => typeof(MemberSearchProbeAlphaFixture).Assembly.Location;

        [Fact]
        public void SearchAssembly_exact_name_finds_member_with_declaring_type_and_kind()
        {
            var results = MemberSearch.SearchAssembly(SelfAssembly, ["MemberSearchProbeAlpha"]);

            var hit = Assert.Single(results);
            Assert.Equal("MemberSearchProbeAlpha", hit.MemberName);
            Assert.Equal(typeof(MemberSearchProbeAlphaFixture).FullName, hit.DeclaringType);
            Assert.Equal("method", hit.Kind);
            Assert.False(hit.IsGlob);
            Assert.Equal("MemberSearchProbeAlpha", hit.Pattern);
        }

        [Fact]
        public void SearchAssembly_is_case_insensitive_for_exact_names()
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
}
