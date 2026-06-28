using ILInspector.DecompilerHarness;

namespace ILInspector.Decompiler.Tests;

/// <summary>
/// The seed generated-fixture catalogue for #1742: compile small source-shape
/// entries into a temporary library, run the existing compile-back oracle, and
/// report by stable fixture ID instead of only by metadata method name.
/// </summary>
[Trait("Speed", "Slow")]
public class GeneratedFixtureCatalogTests
{
    [Fact]
    public void MinimalCompileBackRungs_ReportExpectedOutcomesByFixtureId()
    {
        var run = GeneratedFixtureRunner.Run(GeneratedFixtureCatalog.MinimalCompileBackRungs);
        string report = GeneratedFixtureRunner.FormatReport(run);

        Assert.True(run.Passed, report);

        AssertTarget(
            run,
            "minimal.property.literal",
            "GeneratedFixtures.MinimalPropertyLiteral.Class1",
            ".ctor",
            FidelityCheck.CompileBackStatus.Exact,
            frontier: false);
        AssertTarget(
            run,
            "minimal.property.literal",
            "GeneratedFixtures.MinimalPropertyLiteral.Class1",
            "get_Method1",
            FidelityCheck.CompileBackStatus.Exact,
            frontier: false);

        AssertTarget(
            run,
            "minimal.primary-ctor.field-init",
            "GeneratedFixtures.MinimalPrimaryCtorFieldInit.Class1",
            ".ctor",
            FidelityCheck.CompileBackStatus.OpcodeDiff,
            frontier: true);
        AssertTarget(
            run,
            "minimal.primary-ctor.field-init",
            "GeneratedFixtures.MinimalPrimaryCtorFieldInit.Class1",
            "get_Method1",
            FidelityCheck.CompileBackStatus.Exact,
            frontier: false);

        Assert.Contains("minimal.property.literal", report);
        Assert.Contains("minimal.primary-ctor.field-init", report);
        Assert.Contains("PASS frontier", report);
        Assert.Contains("decompiler=Full", report);
        Assert.Contains("compile-back=OpcodeDiff", report);
    }

    static void AssertTarget(
        GeneratedFixtureRunResult run,
        string fixtureId,
        string type,
        string method,
        FidelityCheck.CompileBackStatus expectedStatus,
        bool frontier)
    {
        var result = Assert.Single(run.Results, r =>
            r.FixtureId == fixtureId &&
            r.Type == type &&
            r.Method == method);

        Assert.Equal(expectedStatus, result.ExpectedStatus);
        Assert.Equal(expectedStatus, result.ActualStatus);
        Assert.Equal(frontier, result.IsFrontier);
        Assert.Equal("Full", result.DecompilerFidelity);
        Assert.True(result.Passed);
    }
}
