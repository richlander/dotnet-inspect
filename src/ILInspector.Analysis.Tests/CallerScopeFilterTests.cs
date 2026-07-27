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
        return new(identity.Name, identity.ReferenceNames);
    }

    /// <summary>Whether a single candidate survives selection for the given target.</summary>
    static bool Selects(string? targetAssembly, CallerScopeFilter.Candidate candidate) =>
        CallerScopeFilter.SelectCouldReach(targetAssembly, [candidate])[0];

    static bool Selects(string? targetAssembly, string? name, IReadOnlyList<string>? references) =>
        Selects(targetAssembly, new CallerScopeFilter.Candidate(name, references));

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
            new("Middle", ["Target"]),
            new("Entry", ["Middle"]),
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
            new("Entry", ["Middle"]),
            new("Middle", ["Target"]),
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
            new("L1", ["Target"]),
            new("L2", ["L1"]),
            new("L3", ["L2"]),
            new("OtherA", ["OtherB"]),
            new("OtherB", ["Newtonsoft.Json"]),
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
            new("A", ["B", "Target"]),
            new("B", ["A"]),
        ];

        Assert.Equal([true, true], CallerScopeFilter.SelectCouldReach("Target", candidates));
    }

    // An unreadable candidate is kept, but its unknown name must not widen the closure: nothing may
    // join merely by referencing an assembly whose identity the filter never established.
    [Fact]
    public void UnknownCandidateDoesNotWidenClosure()
    {
        CallerScopeFilter.Candidate[] candidates =
        [
            new(null, null),
            new("Consumer", ["Newtonsoft.Json"]),
        ];

        Assert.Equal([true, false], CallerScopeFilter.SelectCouldReach("Target", candidates));
    }

    // Facade canonicalization has to hold at every level of the closure, not just the first hop.
    [Fact]
    public void ClosureCanonicalizesAtEveryLevel()
    {
        CallerScopeFilter.Candidate[] candidates =
        [
            new("Middle", ["System.Runtime"]),
            new("Entry", ["Middle"]),
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
            new("A", ["Newtonsoft.Json"]),
            new("B", ImmutableArray<string>.Empty),
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

    [Fact]
    public void EmptyCandidateListSelectsNothing()
    {
        Assert.Empty(CallerScopeFilter.SelectCouldReach("Target", []));
    }
}
