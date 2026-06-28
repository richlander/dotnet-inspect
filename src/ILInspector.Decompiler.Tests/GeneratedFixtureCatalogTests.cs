using System.Text.Json;

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
        AssertTarget(
            run,
            "minimal.ctor-field.getter",
            "GeneratedFixtures.MinimalCtorFieldGetter.Class1",
            ".ctor",
            FidelityCheck.CompileBackStatus.Exact,
            frontier: false);
        AssertTarget(
            run,
            "minimal.ctor-field.getter",
            "GeneratedFixtures.MinimalCtorFieldGetter.Class1",
            "get_Method1",
            FidelityCheck.CompileBackStatus.Exact,
            frontier: false);
        AssertTarget(
            run,
            "minimal.auto-property.getter",
            "GeneratedFixtures.MinimalAutoPropertyGetter.Class1",
            ".ctor",
            FidelityCheck.CompileBackStatus.Exact,
            frontier: false);
        AssertTarget(
            run,
            "minimal.auto-property.getter",
            "GeneratedFixtures.MinimalAutoPropertyGetter.Class1",
            "get_Method1",
            FidelityCheck.CompileBackStatus.Exact,
            frontier: false);
        AssertTarget(
            run,
            "minimal.method-call.same-type",
            "GeneratedFixtures.MinimalMethodCallSameType.Class1",
            ".ctor",
            FidelityCheck.CompileBackStatus.Exact,
            frontier: false);
        AssertTarget(
            run,
            "minimal.method-call.same-type",
            "GeneratedFixtures.MinimalMethodCallSameType.Class1",
            "Method1",
            FidelityCheck.CompileBackStatus.Exact,
            frontier: false);
        AssertTarget(
            run,
            "minimal.method-call.same-type",
            "GeneratedFixtures.MinimalMethodCallSameType.Class1",
            "Method2",
            FidelityCheck.CompileBackStatus.Exact,
            frontier: false);

        Assert.Contains("minimal.property.literal", report);
        Assert.Contains("minimal.primary-ctor.field-init", report);
        Assert.Contains("minimal.ctor-field.getter", report);
        Assert.Contains("minimal.auto-property.getter", report);
        Assert.Contains("minimal.method-call.same-type", report);
        Assert.Contains("PASS frontier", report);
        Assert.Contains("decompiler=Full", report);
        Assert.Contains("compile-back=OpcodeDiff", report);
    }

    [Fact]
    public void CatalogueSelection_MatchesExactIdOrPrefix()
    {
        Assert.Equal(
            ["minimal.property.literal"],
            GeneratedFixtureCatalog.Select("minimal.property.literal").Select(fixture => fixture.Id).ToArray());

        Assert.Equal(
            [
                "minimal.auto-property.getter",
                "minimal.ctor-field.getter",
                "minimal.method-call.same-type",
                "minimal.primary-ctor.field-init",
                "minimal.property.literal",
            ],
            GeneratedFixtureCatalog.Select("minimal").Select(fixture => fixture.Id).Order(StringComparer.Ordinal).ToArray());

        Assert.Empty(GeneratedFixtureCatalog.Select("missing"));
    }

    [Fact]
    public void CatalogueListJson_ContainsFixtureIdsAndExpectedStatuses()
    {
        string json = GeneratedFixtureRunner.FormatListJson(GeneratedFixtureCatalog.All);

        using var document = JsonDocument.Parse(json);
        var fixtures = document.RootElement.EnumerateArray().ToArray();
        Assert.Contains(fixtures, fixture => fixture.GetProperty("Id").GetString() == "minimal.property.literal");
        Assert.Contains(fixtures, fixture => fixture.GetProperty("Id").GetString() == "minimal.ctor-field.getter");
        Assert.Contains(fixtures, fixture => fixture.GetProperty("Id").GetString() == "minimal.auto-property.getter");
        Assert.Contains(fixtures, fixture => fixture.GetProperty("Id").GetString() == "minimal.method-call.same-type");

        var primaryCtor = Assert.Single(fixtures,
            fixture => fixture.GetProperty("Id").GetString() == "minimal.primary-ctor.field-init");
        var ctor = Assert.Single(primaryCtor.GetProperty("Targets").EnumerateArray(),
            target => target.GetProperty("Method").GetString() == ".ctor");
        Assert.Equal("OpcodeDiff", ctor.GetProperty("ExpectedStatus").GetString());
        Assert.True(ctor.GetProperty("IsFrontier").GetBoolean());
    }

    [Fact]
    public void SelectedFixtureRunJson_ContainsOnlySelectedFixtureResults()
    {
        var selected = GeneratedFixtureCatalog.Select("minimal.property.literal");
        var run = GeneratedFixtureRunner.Run(selected);
        string json = GeneratedFixtureRunner.FormatJson(run);

        using var document = JsonDocument.Parse(json);
        var results = document.RootElement.GetProperty("Results").EnumerateArray().ToArray();

        Assert.Equal(2, results.Length);
        Assert.All(results, result =>
        {
            Assert.Equal("minimal.property.literal", result.GetProperty("FixtureId").GetString());
            Assert.Equal("Exact", result.GetProperty("ActualStatus").GetString());
        });
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
