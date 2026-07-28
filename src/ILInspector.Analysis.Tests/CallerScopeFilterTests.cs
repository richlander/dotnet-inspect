using System.Collections.Immutable;
using System.Reflection.PortableExecutable;
using DotnetInspector.Fixtures;
using ILInspector.Analysis;
using ILInspector.Metadata;

namespace ILInspector.Analysis.Tests;

/// <summary>
/// <see cref="CallerScopeFilter"/> decides, from assembly identity alone, whether an assembly is
/// worth opening during cross-assembly caller discovery. Its only correctness obligation is that it
/// never rules out an assembly the matcher would have matched, so these tests pin the boundary in
/// both directions and use the same real fixture assemblies the matcher tests use.
///
/// The obligation is over the whole caller <em>graph</em>, not one level of it, so the transitive
/// cases below are the ones that distinguish the closure from a direct-reference test.
/// </summary>
public class CallerScopeFilterTests
{
    static AssemblyIdentityNames Identity(string assemblyPath)
    {
        using var stream = File.OpenRead(assemblyPath);
        using var peReader = new PEReader(stream);
        return AssemblyIdentityScanner.Scan(peReader);
    }

    static CallerScopeFilter.Candidate Candidate(string assemblyPath)
    {
        var identity = Identity(assemblyPath);
        return CallerScopeFilter.Candidate.Known(identity.Name, identity.ReferenceNames);
    }

    /// <summary>A candidate from raw names, mapping null to the matching undecidable state.</summary>
    static CallerScopeFilter.Candidate MakeCandidate(string? name, IReadOnlyList<string>? references)
        => (name, references) switch
        {
            (null, _) => CallerScopeFilter.Candidate.Unknown(),
            (not null, null) => CallerScopeFilter.Candidate.UnknownReferences(name),
            _ => CallerScopeFilter.Candidate.Known(name, references),
        };

    /// <summary>Whether a single candidate survives selection for the given target.</summary>
    static bool Selects(string? targetAssembly, CallerScopeFilter.Candidate candidate) =>
        CallerScopeFilter.SelectCouldReach(targetAssembly, [candidate])[0];

    static bool Selects(string? targetAssembly, string? name, IReadOnlyList<string>? references) =>
        Selects(targetAssembly, MakeCandidate(name, references));

    static bool SelectsFile(string targetAssemblyPath, string candidateAssemblyPath) =>
        Selects(Identity(targetAssemblyPath).Name, Candidate(candidateAssemblyPath));

    // The real caller fixture references the target, so it must survive the filter and be opened.
    // This is the assembly BuildCallerTree_WithScope_* proves does contribute callers.
    [Fact]
    public void ReferencingAssemblyIsKept()
    {
        Assert.True(SelectsFile(
            FixtureCatalog.AnalysisCallerGraphTarget.AssemblyPath(),
            FixtureCatalog.AnalysisCallerGraphCaller.AssemblyPath()));
    }

    // The lookalike declares its own Target.Api.Ping and never references the target assembly.
    // The matcher already excludes it (BuildCallerTree ignores its calls); the filter must reach
    // the same answer, which is what makes skipping it an optimization rather than a behavior change.
    [Fact]
    public void NonReferencingAssemblyIsRuledOut()
    {
        Assert.False(SelectsFile(
            FixtureCatalog.AnalysisCallerGraphTarget.AssemblyPath(),
            FixtureCatalog.AnalysisCallerGraphLookalikeCaller.AssemblyPath()));
    }

    // The candidate is the target: its own TypeDefs are callees the matcher can match.
    [Fact]
    public void TargetAssemblyItselfIsKept()
    {
        string target = FixtureCatalog.AnalysisCallerGraphTarget.AssemblyPath();
        Assert.True(SelectsFile(target, target));
    }

    // The facade case, and the reason the filter canonicalizes instead of comparing raw names.
    // Callers reference System.Runtime; the target is defined in System.Private.CoreLib. A raw name
    // comparison would rule out essentially every caller of a corelib member.
    [Theory]
    [InlineData("System.Runtime")]
    [InlineData("mscorlib")]
    [InlineData("netstandard")]
    [InlineData("System.Runtime.Extensions")]
    public void CorelibFacadeReferenceIsKept(string facade)
    {
        Assert.True(Selects("System.Private.CoreLib", "Some.Consumer", [facade]));
    }

    [Fact]
    public void UnrelatedReferencesAreRuledOut()
    {
        Assert.False(Selects("Target", "Some.Consumer", ["System.Runtime", "Newtonsoft.Json"]));
    }

    // Undecidable inputs must fail open: a filter that cannot tell has to let the real matcher decide.
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void UnknownTargetFailsOpen(string? target)
    {
        Assert.True(Selects(target, "Some.Consumer", ["Unrelated"]));
    }

    [Fact]
    public void UnknownCandidateReferencesFailOpen()
    {
        Assert.True(Selects("Target", "Some.Consumer", null));
    }

    [Fact]
    public void UnknownCandidateNameFailsOpen()
    {
        Assert.True(Selects("Target", null, ["Unrelated"]));
    }

    // A reference-free image (no AssemblyRef rows) that is not the target cannot call into it.
    [Fact]
    public void EmptyReferenceSetIsRuledOut()
    {
        Assert.False(Selects("Target", "Some.Consumer", ImmutableArray<string>.Empty));
    }

    // The defect a direct-reference test has: a caller graph walks outward several levels, so an
    // assembly that names only an intermediate still appears in the tree. Ruling it out drops real
    // callers and silently shortens the graph, which is a wrong answer rather than a slower one.
    [Fact]
    public void IndirectCallerIsKeptThroughIntermediate()
    {
        CallerScopeFilter.Candidate[] candidates =
        [
            CallerScopeFilter.Candidate.Known("Middle", ["Target"]),
            CallerScopeFilter.Candidate.Known("Entry", ["Middle"]),
        ];

        Assert.Equal([true, true], CallerScopeFilter.SelectCouldReach("Target", candidates));
    }

    // Closure order must not matter: the upstream assembly is listed before the intermediate that
    // pulls it in, so a single forward pass would miss it.
    [Fact]
    public void ClosureIsOrderIndependent()
    {
        CallerScopeFilter.Candidate[] candidates =
        [
            CallerScopeFilter.Candidate.Known("Entry", ["Middle"]),
            CallerScopeFilter.Candidate.Known("Middle", ["Target"]),
        ];

        Assert.Equal([true, true], CallerScopeFilter.SelectCouldReach("Target", candidates));
    }

    // Depth beyond one intermediate, and a chain that never reaches the target stays out, so the
    // closure is not simply selecting everything transitively connected to anything.
    [Fact]
    public void ClosureReachesArbitraryDepthAndStopsAtUnrelatedChains()
    {
        CallerScopeFilter.Candidate[] candidates =
        [
            CallerScopeFilter.Candidate.Known("L1", ["Target"]),
            CallerScopeFilter.Candidate.Known("L2", ["L1"]),
            CallerScopeFilter.Candidate.Known("L3", ["L2"]),
            CallerScopeFilter.Candidate.Known("OtherA", ["OtherB"]),
            CallerScopeFilter.Candidate.Known("OtherB", ["Newtonsoft.Json"]),
        ];

        Assert.Equal(
            [true, true, true, false, false],
            CallerScopeFilter.SelectCouldReach("Target", candidates));
    }

    // A reference cycle must terminate rather than spin, and both members join once either reaches.
    [Fact]
    public void ReferenceCycleTerminates()
    {
        CallerScopeFilter.Candidate[] candidates =
        [
            CallerScopeFilter.Candidate.Known("A", ["B", "Target"]),
            CallerScopeFilter.Candidate.Known("B", ["A"]),
        ];

        Assert.Equal([true, true], CallerScopeFilter.SelectCouldReach("Target", candidates));
    }

    // Round-4 review found the previous rule here was unsound. An unreadable candidate is still
    // OPENED and can still contribute edges, so anything referencing it can be a caller-of-a-caller.
    // Refusing to widen the closure cut those assemblies off and truncated the graph — reproduced
    // end to end with a single-byte mutation that broke identity reading while body indexing still
    // succeeded. An unnameable assembly could sit anywhere in the chain, so nothing above it can be
    // ruled out and the whole scope has to be selected.
    [Fact]
    public void UnknownIdentityCandidateSelectsTheWholeScope()
    {
        CallerScopeFilter.Candidate[] candidates =
        [
            CallerScopeFilter.Candidate.Unknown(),
            CallerScopeFilter.Candidate.Known("Consumer", ["Newtonsoft.Json"]),
        ];

        Assert.Equal([true, true], CallerScopeFilter.SelectCouldReach("Target", candidates));
    }

    // ...but an unopenable file could not have contributed edges under the old unfiltered walk
    // either, so it must NOT drag the scope in with it. Otherwise a single stray native DLL in a
    // --bin directory would defeat the whole optimization.
    [Fact]
    public void UnopenableCandidateDoesNotSelectTheScope()
    {
        CallerScopeFilter.Candidate[] candidates =
        [
            CallerScopeFilter.Candidate.Unopenable(),
            CallerScopeFilter.Candidate.Known("Consumer", ["Newtonsoft.Json"]),
        ];

        Assert.Equal([false, false], CallerScopeFilter.SelectCouldReach("Target", candidates));
    }

    // The narrower half of the same defect: a candidate whose NAME was read but whose reference set
    // was not. It must be selected (a dropped reference row could have been the target), and it must
    // still publish its own name, or callers above it are cut off.
    [Fact]
    public void UnknownReferencesCandidateWidensTheClosureUnderItsOwnName()
    {
        CallerScopeFilter.Candidate[] candidates =
        [
            CallerScopeFilter.Candidate.UnknownReferences("Middle"),
            CallerScopeFilter.Candidate.Known("Entry", ["Middle"]),
        ];

        Assert.Equal([true, true], CallerScopeFilter.SelectCouldReach("Target", candidates));
    }

    // An unopenable candidate must not be selected even when the target is unknown, since it can
    // contribute nothing at all.
    [Fact]
    public void UnknownTargetStillRulesOutUnopenableCandidates()
    {
        CallerScopeFilter.Candidate[] candidates =
        [
            CallerScopeFilter.Candidate.Known("A", ["Newtonsoft.Json"]),
            CallerScopeFilter.Candidate.Unopenable(),
        ];

        Assert.Equal([true, false], CallerScopeFilter.SelectCouldReach(null, candidates));
    }

    // Facade canonicalization has to hold at every level of the closure, not just the first hop.
    [Fact]
    public void ClosureCanonicalizesAtEveryLevel()
    {
        CallerScopeFilter.Candidate[] candidates =
        [
            CallerScopeFilter.Candidate.Known("Middle", ["System.Runtime"]),
            CallerScopeFilter.Candidate.Known("Entry", ["Middle"]),
        ];

        Assert.Equal(
            [true, true],
            CallerScopeFilter.SelectCouldReach("System.Private.CoreLib", candidates));
    }

    [Fact]
    public void UnknownTargetSelectsEveryCandidate()
    {
        CallerScopeFilter.Candidate[] candidates =
        [
            CallerScopeFilter.Candidate.Known("A", ["Newtonsoft.Json"]),
            CallerScopeFilter.Candidate.Known("B", ImmutableArray<string>.Empty),
        ];

        Assert.Equal([true, true], CallerScopeFilter.SelectCouldReach(null, candidates));
    }

    // The real-artifact form of the transitive obligation. The indirect fixture references only the
    // caller assembly, so a direct-reference test rules it out even though it reaches the target in
    // two hops. Asserting the absent reference keeps the fixture honest: if it ever gained a direct
    // reference to the target this test would stop proving anything.
    [Fact]
    public void IndirectCallerAssemblyIsKept()
    {
        var target = Identity(FixtureCatalog.AnalysisCallerGraphTarget.AssemblyPath());
        var caller = Candidate(FixtureCatalog.AnalysisCallerGraphCaller.AssemblyPath());
        var indirect = Candidate(FixtureCatalog.AnalysisCallerGraphIndirectCaller.AssemblyPath());

        Assert.DoesNotContain(target.Name, indirect.References!);
        Assert.False(Selects(target.Name, indirect));
        Assert.Equal([true, true], CallerScopeFilter.SelectCouldReach(target.Name, [caller, indirect]));
    }

    // The real-artifact form of the boundary: adding an indirect caller must not drag in an
    // assembly that reaches the target through nothing.
    [Fact]
    public void IndirectClosureStillRulesOutTheLookalike()
    {
        var target = Identity(FixtureCatalog.AnalysisCallerGraphTarget.AssemblyPath());
        var caller = Candidate(FixtureCatalog.AnalysisCallerGraphCaller.AssemblyPath());
        var indirect = Candidate(FixtureCatalog.AnalysisCallerGraphIndirectCaller.AssemblyPath());
        var lookalike = Candidate(FixtureCatalog.AnalysisCallerGraphLookalikeCaller.AssemblyPath());

        Assert.Equal(
            [true, true, false],
            CallerScopeFilter.SelectCouldReach(target.Name, [caller, indirect, lookalike]));
    }

    // Copies of one assembly under several subdirectories, or several facades canonicalizing
    // together, all share a canonical name. Every copy must be selected — and the walk must stay
    // linear while doing it. Before the adjacency entry was consumed on first visit, each selected
    // sharer re-enqueued the shared name and re-traversed the whole group: quadratic in group size,
    // measured at ~10s for 40k sharers against ~13ms after.
    [Fact]
    public void SameNamedCandidatesAreAllSelectedInLinearTime()
    {
        const int sharers = 40_000;
        var candidates = new List<CallerScopeFilter.Candidate>(sharers);
        for (int i = 0; i < sharers; i++)
            candidates.Add(CallerScopeFilter.Candidate.Known("Dup", ["Target"]));

        var watch = System.Diagnostics.Stopwatch.StartNew();
        var selected = CallerScopeFilter.SelectCouldReach("Target", candidates);
        watch.Stop();

        Assert.All(selected, Assert.True);

        // Three orders of magnitude of headroom over the linear cost, and two under the quadratic
        // one, so this discriminates the shapes without being a wall-clock measurement.
        Assert.True(
            watch.ElapsedMilliseconds < 2_000,
            $"selection over {sharers} same-named candidates took {watch.ElapsedMilliseconds}ms, "
                + "which indicates the walk revisits an adjacency entry per sharer");
    }

    // An assembly may name the same reference more than once, and its own name may repeat a
    // reference. Selection must be correct either way.
    [Fact]
    public void RepeatedNamesSelectTheSameAsDistinctOnes()
    {
        Assert.True(Selects("Target", "Target", ["Target", "Target"]));
        Assert.True(Selects("Target", "Self", ["Self", "Target", "Target"]));
        Assert.False(Selects("Target", "Self", ["Self", "Self"]));
    }

    // Round-6 review: the previous version of this test asserted only the selection results above,
    // which hold with or without the dedupe in Index — it survived its own mutation and pinned
    // nothing. Deduping is invisible in the ANSWER; it is visible in the FOOTPRINT, which is the
    // binding constraint for the interactive consumer this change exists to serve.
    //
    // A candidate whose reference list is entirely duplicates indexes one entry rather than one
    // per row: measured at 592 bytes flat with the dedupe against ~1 MB at 100k rows and ~8.4 MB
    // at 1M rows without it. The bound below sits three orders of magnitude above the former and
    // an order of magnitude below the latter, so it discriminates without being a tight
    // measurement.
    [Fact]
    public void RepeatedReferencesDoNotGrowTheIndex()
    {
        const int references = 100_000;
        var duplicates = new string[references];
        Array.Fill(duplicates, "Target");
        var candidates = new[] { CallerScopeFilter.Candidate.Known("Dup", duplicates) };

        // Warm up so first-call JIT and canonicalization caches are not attributed to the walk.
        CallerScopeFilter.SelectCouldReach("Target", candidates);

        long before = GC.GetAllocatedBytesForCurrentThread();
        var selected = CallerScopeFilter.SelectCouldReach("Target", candidates);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.True(selected[0]);
        Assert.True(
            allocated < 64 * 1024,
            $"selection over a candidate with {references} duplicate references allocated "
                + $"{allocated} bytes, which indicates the reverse index grows with duplicate rows");
    }

    [Fact]
    public void EmptyCandidateListSelectsNothing()
    {
        Assert.Empty(CallerScopeFilter.SelectCouldReach("Target", []));
    }
}
