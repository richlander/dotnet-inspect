using System.Linq;

using ILInspector.AnalysisHarness;

namespace ILInspector.Analysis.Tests;

/// <summary>
/// The seed analysis-fixture catalogue (#1819): materialize small source-shape entries into
/// temporary consumer + external assemblies, grade each target method's signals against its
/// expected-signal ledger entry with the real analyzer, and report by stable fixture id. Slow
/// because each fixture runs `dotnet build`; excluded from the fast PR lane like the decompiler
/// generated-fixture catalogue.
/// </summary>
[Trait("Speed", "Slow")]
public class AnalysisFixtureCatalogTests
{
    [Fact]
    public void SeedCatalogue_GradesEveryTargetByFixtureId()
    {
        var run = AnalysisFixtureRunner.Run(AnalysisFixtureCatalog.All);
        string report = AnalysisFixtureRunner.FormatReport(run);

        Assert.True(run.Passed, report);

        // Resolved cases: the allocation signal classifies the newobj by true shape.
        AssertAllocations(run, "alloc.value-struct-newobj.in-assembly", "ConstructsInLoop", 0);
        AssertAllocations(run, "alloc.value-struct-newobj.cross-asm-generic", "ConstructsInLoop", 0);
        AssertAllocationsAtLeast(run, "alloc.reftype-newobj.cross-asm-name-collision", "ConstructsInLoop", 1);

        // Owned boundaries: deliberately-imperfect outcomes at the no-referenced-assembly edge.
        AssertBoundary(run, "alloc.value-struct-newobj.cross-asm-nongeneric", "ConstructsInLoop", OwnedBoundary.FalsePositive);
        AssertBoundary(run, "exception.suffix-lookalike.external", "ConstructsLookalike", OwnedBoundary.FalsePositive);
        AssertBoundary(run, "exception.unsuffixed.external", "Throws", OwnedBoundary.FalseNegative);

        // Opportunity-shape seeds: true positive, must-not-flag, and a deferred owned-FN.
        AssertPasses(run, "alloc.string-build.accumulation-in-loop", "AppendsStringInLoop");
        AssertPasses(run, "alloc.string-build.accumulation-in-loop", "ConcatsIntoListInLoop");
        AssertBoundary(run, "alloc.string-build.accumulation-in-loop", "DerivedAccumulatorInLoop", OwnedBoundary.FalseNegative);
        AssertPasses(run, "suppress.box.throw-path", "BoxesIntoStringFormat");
        AssertPasses(run, "suppress.box.throw-path", "ThrowsWithBoxedValue");
        AssertBoundary(run, "suppress.box.throw-path", "ThrowsViaHelper", OwnedBoundary.FalsePositive);

        // Enumerator-allocation: true positive plus three distinct must-not-flag cases
        // (non-loop, struct enumerator, and the untrusted lookalike trust gate).
        AssertPasses(run, "alloc.enumerator.interface-foreach-in-loop", "ForeachInterfaceInLoop");
        AssertBoundary(run, "alloc.enumerator.interface-foreach-in-loop", "ForeachInterfaceOnce", OwnedBoundary.FalseNegative);
        AssertPasses(run, "alloc.enumerator.interface-foreach-in-loop", "ForeachConcreteListInLoop");
        AssertPasses(run, "alloc.enumerator.interface-foreach-in-loop", "ForeachLookalikeEnumeratorInLoop");
    }

    static void AssertPasses(AnalysisFixtureRunResult run, string fixtureId, string method)
    {
        var result = Result(run, fixtureId, method);
        Assert.True(result.Passed, result.Failure);
    }

    // The grader must refuse to silently grade an ambiguous target name (an overload or a
    // same-named method on another type) rather than pick the first metadata match (#1819 review).
    [Fact]
    public void AmbiguousTargetName_FailsExplicitly()
    {
        var ambiguous = new AnalysisFixtureDefinition(
            "test.ambiguous-name",
            """
            namespace Fix;
            public static class A { public static int Dup() => 1; }
            public static class B { public static int Dup() => 2; }
            """,
            ExternalSource: null,
            ExternalNeedsAlias: false,
            [new AnalysisFixtureTarget("Dup", new AnalysisExpectation(AllocationsExactly: 0))],
            ["test"]);

        var run = AnalysisFixtureRunner.Run([ambiguous]);
        var result = Assert.Single(run.Results);

        Assert.False(result.Passed);
        Assert.Contains("ambiguous", result.Failure);
    }

    static AnalysisFixtureResult Result(AnalysisFixtureRunResult run, string fixtureId, string method)
        => Assert.Single(run.Results, r => r.FixtureId == fixtureId && r.Method == method);

    static void AssertAllocations(AnalysisFixtureRunResult run, string fixtureId, string method, int expected)
    {
        var result = Result(run, fixtureId, method);
        Assert.True(result.Passed, result.Failure);
        Assert.Equal(expected, result.Allocations);
    }

    static void AssertAllocationsAtLeast(AnalysisFixtureRunResult run, string fixtureId, string method, int atLeast)
    {
        var result = Result(run, fixtureId, method);
        Assert.True(result.Passed, result.Failure);
        Assert.True(result.Allocations >= atLeast);
    }

    static void AssertBoundary(AnalysisFixtureRunResult run, string fixtureId, string method, OwnedBoundary expected)
    {
        var result = Result(run, fixtureId, method);
        Assert.Equal(expected, result.Boundary);
        Assert.True(result.Passed, result.Failure);
    }
}
