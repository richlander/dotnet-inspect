using ILInspector.Metadata;

namespace ILInspector.Metadata.Tests
{
    public sealed class CorpusTests
    {
        private static string SelfAssembly => typeof(CorpusProbeTypeAlpha).Assembly.Location;

        private static Corpus SelfCorpus(
            string? source = "probe-pkg",
            string? version = "9.9.9",
            string? tfm = "net10.0")
            => new([new CorpusMember { AssemblyPath = SelfAssembly, Source = source, Version = version, Tfm = tfm }]);

        [Fact]
        public void Constructor_snapshots_members_so_later_mutation_has_no_effect()
        {
            var list = new List<CorpusMember>
            {
                new() { AssemblyPath = SelfAssembly },
            };
            var corpus = new Corpus(list);

            list.Clear();

            Assert.Equal(1, corpus.Count);
            Assert.Equal(SelfAssembly, Assert.Single(corpus.Members).AssemblyPath);
        }

        [Fact]
        public void SearchTypes_exact_name_finds_type_with_kind_and_provenance()
        {
            var outcome = SelfCorpus().SearchTypes(["CorpusProbeTypeAlpha"]);

            Assert.Empty(outcome.SkippedAssemblies);
            var hit = Assert.Single(outcome.Results);
            Assert.Equal(typeof(CorpusProbeTypeAlpha).FullName, hit.FullName);
            Assert.Equal("CorpusProbeTypeAlpha", hit.TypeName);
            Assert.Equal("class", hit.Kind);
            Assert.Equal("probe-pkg", hit.Source);
            Assert.Equal("9.9.9", hit.Version);
            Assert.Equal("net10.0", hit.Tfm);
            Assert.False(hit.IsGlob);
            Assert.Equal(Path.GetFileNameWithoutExtension(SelfAssembly), hit.Assembly);
        }

        [Fact]
        public void SearchTypes_glob_matches_multiple_public_types_but_not_non_public_by_default()
        {
            var outcome = SelfCorpus().SearchTypes(["CorpusProbeType*"]);

            var names = outcome.Results.Select(r => r.FullName).ToHashSet();
            Assert.Contains(typeof(CorpusProbeTypeAlpha).FullName!, names);
            Assert.Contains(typeof(CorpusProbeTypeBeta).FullName!, names);
            Assert.DoesNotContain("ILInspector.Metadata.Tests.CorpusProbeTypeHidden", names);
            Assert.All(outcome.Results, r => Assert.True(r.IsGlob));
        }

        [Fact]
        public void SearchTypes_includeAll_surfaces_non_public_type()
        {
            var withoutAll = SelfCorpus().SearchTypes(["CorpusProbeTypeHidden"]);
            Assert.Empty(withoutAll.Results);

            var withAll = SelfCorpus().SearchTypes(["CorpusProbeTypeHidden"], includeAll: true);
            Assert.Contains(withAll.Results, r => r.TypeName == "CorpusProbeTypeHidden");
        }

        [Fact]
        public void SearchTypes_empty_patterns_returns_empty_outcome()
        {
            var outcome = SelfCorpus().SearchTypes([]);

            Assert.Empty(outcome.Results);
            Assert.Empty(outcome.SkippedAssemblies);
        }

        [Fact]
        public void SearchTypes_respects_result_limit()
        {
            var outcome = SelfCorpus().SearchTypes(["CorpusProbeType*"], limit: 1);

            Assert.Single(outcome.Results);
        }

        [Fact]
        public void SearchTypes_reports_unreadable_assembly_as_skipped_without_faking_success()
        {
            var missing = Path.Combine(Path.GetTempPath(), "corpus-types-does-not-exist-8b1c.dll");
            var corpus = new Corpus([new CorpusMember { AssemblyPath = missing }]);

            var outcome = corpus.SearchTypes(["CorpusProbeTypeAlpha"]);

            Assert.Empty(outcome.Results);
            Assert.Contains(missing, outcome.SkippedAssemblies);
        }

        [Fact]
        public void SearchTypes_mixed_readable_and_unreadable_reports_both()
        {
            var missing = Path.Combine(Path.GetTempPath(), "corpus-types-mixed-2d7e.dll");
            var corpus = new Corpus(
            [
                new CorpusMember { AssemblyPath = SelfAssembly, Source = "good" },
                new CorpusMember { AssemblyPath = missing, Source = "bad" },
            ]);

            var outcome = corpus.SearchTypes(["CorpusProbeTypeAlpha"]);

            Assert.Contains(outcome.Results, r => r.FullName == typeof(CorpusProbeTypeAlpha).FullName && r.Source == "good");
            Assert.Contains(missing, outcome.SkippedAssemblies);
        }

        [Fact]
        public void SearchMembers_finds_member_with_declaring_type_and_provenance()
        {
            var outcome = SelfCorpus().SearchMembers(["CorpusProbeMemberAlpha"]);

            Assert.Empty(outcome.SkippedAssemblies);
            var hit = Assert.Single(outcome.Results);
            Assert.Equal("CorpusProbeMemberAlpha", hit.Member.MemberName);
            Assert.Equal(typeof(CorpusProbeTypeAlpha).FullName, hit.Member.DeclaringType);
            Assert.Equal("method", hit.Member.Kind);
            Assert.Equal("probe-pkg", hit.Source);
            Assert.Equal("9.9.9", hit.Version);
            Assert.Equal("net10.0", hit.Tfm);
        }

        [Fact]
        public void SearchMembers_includeAll_surfaces_non_public_member()
        {
            var withoutAll = SelfCorpus().SearchMembers(["CorpusProbeMemberHidden"]);
            Assert.Empty(withoutAll.Results);

            var withAll = SelfCorpus().SearchMembers(["CorpusProbeMemberHidden"], includeAll: true);
            Assert.Contains(withAll.Results, r => r.Member.MemberName == "CorpusProbeMemberHidden");
        }

        [Fact]
        public void SearchMembers_reports_unreadable_assembly_as_skipped_without_faking_success()
        {
            var missing = Path.Combine(Path.GetTempPath(), "corpus-members-does-not-exist-77aa.dll");
            var corpus = new Corpus([new CorpusMember { AssemblyPath = missing }]);

            var outcome = corpus.SearchMembers(["CorpusProbeMemberAlpha"]);

            Assert.Empty(outcome.Results);
            Assert.Contains(missing, outcome.SkippedAssemblies);
        }

        [Fact]
        public void SearchMembers_respects_result_limit_across_the_set()
        {
            var outcome = SelfCorpus().SearchMembers(["CorpusProbeMember*"], limit: 1);

            Assert.Single(outcome.Results);
        }

        [Fact]
        public void SearchMembers_attaches_provenance_of_the_specific_member_that_supplied_the_hit()
        {
            var corpus = new Corpus(
            [
                new CorpusMember { AssemblyPath = SelfAssembly, Source = "pkg-a", Version = "1.0" },
                new CorpusMember { AssemblyPath = SelfAssembly, Source = "pkg-b", Version = "2.0" },
            ]);

            var outcome = corpus.SearchMembers(["CorpusProbeMemberAlpha"]);

            Assert.Contains(outcome.Results, r =>
                r.Source == "pkg-a" && r.Version == "1.0" && r.Member.MemberName == "CorpusProbeMemberAlpha");
            Assert.Contains(outcome.Results, r =>
                r.Source == "pkg-b" && r.Version == "2.0" && r.Member.MemberName == "CorpusProbeMemberAlpha");
        }

        [Fact]
        public void SearchMembers_empty_patterns_returns_empty_outcome()
        {
            var outcome = SelfCorpus().SearchMembers([]);

            Assert.Empty(outcome.Results);
            Assert.Empty(outcome.SkippedAssemblies);
        }
    }

    public sealed class CorpusProbeTypeAlpha
    {
        public int CorpusProbeMemberAlpha() => 1;
        private int CorpusProbeMemberHidden() => 2;
    }

    public sealed class CorpusProbeTypeBeta
    {
        public void CorpusProbeMemberBeta() { }
    }

    internal sealed class CorpusProbeTypeHidden
    {
    }
}
