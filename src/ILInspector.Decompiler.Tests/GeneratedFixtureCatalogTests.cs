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
            FidelityCheck.CompileBackStatus.Exact,
            frontier: false);
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
        AssertTarget(
            run,
            "minimal.static-method-call",
            "GeneratedFixtures.MinimalStaticMethodCall.Class1",
            ".ctor",
            FidelityCheck.CompileBackStatus.Exact,
            frontier: false);
        AssertTarget(
            run,
            "minimal.static-method-call",
            "GeneratedFixtures.MinimalStaticMethodCall.Class1",
            "Method1",
            FidelityCheck.CompileBackStatus.Exact,
            frontier: false);
        AssertTarget(
            run,
            "minimal.static-method-call",
            "GeneratedFixtures.MinimalStaticMethodCall.Class1",
            "Helper",
            FidelityCheck.CompileBackStatus.Exact,
            frontier: false);
        AssertTarget(
            run,
            "minimal.if-else",
            "GeneratedFixtures.MinimalIfElse.Class1",
            ".ctor",
            FidelityCheck.CompileBackStatus.Exact,
            frontier: false);
        AssertTarget(
            run,
            "minimal.if-else",
            "GeneratedFixtures.MinimalIfElse.Class1",
            "Method1",
            FidelityCheck.CompileBackStatus.Exact,
            frontier: false);
        AssertTarget(
            run,
            "minimal.null-coalesce",
            "GeneratedFixtures.MinimalNullCoalesce.Class1",
            ".ctor",
            FidelityCheck.CompileBackStatus.Exact,
            frontier: false);
        AssertTarget(
            run,
            "minimal.null-coalesce",
            "GeneratedFixtures.MinimalNullCoalesce.Class1",
            "Method1",
            FidelityCheck.CompileBackStatus.Exact,
            frontier: false);
        AssertTarget(
            run,
            "minimal.try-finally",
            "GeneratedFixtures.MinimalTryFinally.Class1",
            ".ctor",
            FidelityCheck.CompileBackStatus.Exact,
            frontier: false);
        AssertTarget(
            run,
            "minimal.try-finally",
            "GeneratedFixtures.MinimalTryFinally.Class1",
            "Method1",
            FidelityCheck.CompileBackStatus.Exact,
            frontier: false);
        AssertTarget(
            run,
            "minimal.using-dispose",
            "GeneratedFixtures.MinimalUsingDispose.Class1",
            ".ctor",
            FidelityCheck.CompileBackStatus.Exact,
            frontier: false);
        AssertTarget(
            run,
            "minimal.using-dispose",
            "GeneratedFixtures.MinimalUsingDispose.Class1",
            "Method1",
            FidelityCheck.CompileBackStatus.Exact,
            frontier: false);
        AssertTarget(
            run,
            "minimal.foreach-array",
            "GeneratedFixtures.MinimalForeachArray.Class1",
            ".ctor",
            FidelityCheck.CompileBackStatus.Exact,
            frontier: false);
        AssertTarget(
            run,
            "minimal.foreach-array",
            "GeneratedFixtures.MinimalForeachArray.Class1",
            "Method1",
            FidelityCheck.CompileBackStatus.Exact,
            frontier: false);

        Assert.Contains("minimal.property.literal", report);
        Assert.Contains("minimal.primary-ctor.field-init", report);
        Assert.Contains("minimal.ctor-field.getter", report);
        Assert.Contains("minimal.auto-property.getter", report);
        Assert.Contains("minimal.method-call.same-type", report);
        Assert.Contains("minimal.static-method-call", report);
        Assert.Contains("minimal.if-else", report);
        Assert.Contains("minimal.null-coalesce", report);
        Assert.Contains("minimal.try-finally", report);
        Assert.Contains("minimal.using-dispose", report);
        Assert.Contains("minimal.foreach-array", report);
        Assert.Contains("decompiler=Full", report);
        Assert.Contains("compile-back=Exact", report);
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
                "minimal.foreach-array",
                "minimal.if-else",
                "minimal.method-call.same-type",
                "minimal.null-coalesce",
                "minimal.primary-ctor.field-init",
                "minimal.property.literal",
                "minimal.static-method-call",
                "minimal.try-finally",
                "minimal.using-dispose",
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
        Assert.Contains(fixtures, fixture => fixture.GetProperty("Id").GetString() == "minimal.static-method-call");
        Assert.Contains(fixtures, fixture => fixture.GetProperty("Id").GetString() == "minimal.if-else");
        Assert.Contains(fixtures, fixture => fixture.GetProperty("Id").GetString() == "minimal.null-coalesce");
        Assert.Contains(fixtures, fixture => fixture.GetProperty("Id").GetString() == "minimal.try-finally");
        Assert.Contains(fixtures, fixture => fixture.GetProperty("Id").GetString() == "minimal.using-dispose");
        Assert.Contains(fixtures, fixture => fixture.GetProperty("Id").GetString() == "minimal.foreach-array");

        var primaryCtor = Assert.Single(fixtures,
            fixture => fixture.GetProperty("Id").GetString() == "minimal.primary-ctor.field-init");
        var ctor = Assert.Single(primaryCtor.GetProperty("Targets").EnumerateArray(),
            target => target.GetProperty("Method").GetString() == ".ctor");
        Assert.Equal("Exact", ctor.GetProperty("ExpectedStatus").GetString());
        Assert.False(ctor.GetProperty("IsFrontier").GetBoolean());
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
