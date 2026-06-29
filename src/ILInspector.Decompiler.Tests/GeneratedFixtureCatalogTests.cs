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
            "minimal.integer-addition",
            "GeneratedFixtures.MinimalIntegerAddition.Class1",
            ".ctor",
            FidelityCheck.CompileBackStatus.Exact,
            frontier: false);
        AssertTarget(
            run,
            "minimal.integer-addition",
            "GeneratedFixtures.MinimalIntegerAddition.Class1",
            "Method1",
            FidelityCheck.CompileBackStatus.Exact,
            frontier: false);
        AssertTarget(
            run,
            "minimal.array-index",
            "GeneratedFixtures.MinimalArrayIndex.Class1",
            ".ctor",
            FidelityCheck.CompileBackStatus.Exact,
            frontier: false);
        AssertTarget(
            run,
            "minimal.array-index",
            "GeneratedFixtures.MinimalArrayIndex.Class1",
            "Method1",
            FidelityCheck.CompileBackStatus.Exact,
            frontier: false);
        AssertTarget(
            run,
            "minimal.array-length",
            "GeneratedFixtures.MinimalArrayLength.Class1",
            ".ctor",
            FidelityCheck.CompileBackStatus.Exact,
            frontier: false);
        AssertTarget(
            run,
            "minimal.array-length",
            "GeneratedFixtures.MinimalArrayLength.Class1",
            "Method1",
            FidelityCheck.CompileBackStatus.Exact,
            frontier: false);
        AssertTarget(
            run,
            "minimal.string-length",
            "GeneratedFixtures.MinimalStringLength.Class1",
            ".ctor",
            FidelityCheck.CompileBackStatus.Exact,
            frontier: false);
        AssertTarget(
            run,
            "minimal.string-length",
            "GeneratedFixtures.MinimalStringLength.Class1",
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
        AssertTarget(
            run,
            "minimal.for-loop",
            "GeneratedFixtures.MinimalForLoop.Class1",
            ".ctor",
            FidelityCheck.CompileBackStatus.Exact,
            frontier: false);
        AssertTarget(
            run,
            "minimal.for-loop",
            "GeneratedFixtures.MinimalForLoop.Class1",
            "Method1",
            FidelityCheck.CompileBackStatus.Exact,
            frontier: false);
        AssertTarget(
            run,
            "minimal.while-loop",
            "GeneratedFixtures.MinimalWhileLoop.Class1",
            ".ctor",
            FidelityCheck.CompileBackStatus.Exact,
            frontier: false);
        AssertTarget(
            run,
            "minimal.while-loop",
            "GeneratedFixtures.MinimalWhileLoop.Class1",
            "Method1",
            FidelityCheck.CompileBackStatus.Exact,
            frontier: false);
        AssertTarget(
            run,
            "minimal.do-while",
            "GeneratedFixtures.MinimalDoWhileLoop.Class1",
            ".ctor",
            FidelityCheck.CompileBackStatus.Exact,
            frontier: false);
        AssertTarget(
            run,
            "minimal.do-while",
            "GeneratedFixtures.MinimalDoWhileLoop.Class1",
            "Method1",
            FidelityCheck.CompileBackStatus.Exact,
            frontier: false);
        AssertTarget(
            run,
            "minimal.switch-int",
            "GeneratedFixtures.MinimalSwitchInt.Class1",
            ".ctor",
            FidelityCheck.CompileBackStatus.Exact,
            frontier: false);
        AssertTarget(
            run,
            "minimal.switch-int",
            "GeneratedFixtures.MinimalSwitchInt.Class1",
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
        Assert.Contains("minimal.integer-addition", report);
        Assert.Contains("minimal.array-index", report);
        Assert.Contains("minimal.array-length", report);
        Assert.Contains("minimal.string-length", report);
        Assert.Contains("minimal.null-coalesce", report);
        Assert.Contains("minimal.try-finally", report);
        Assert.Contains("minimal.using-dispose", report);
        Assert.Contains("minimal.foreach-array", report);
        Assert.Contains("minimal.for-loop", report);
        Assert.Contains("minimal.while-loop", report);
        Assert.Contains("minimal.do-while", report);
        Assert.Contains("minimal.switch-int", report);
        Assert.DoesNotContain("minimal.switch-two-case-lowers-if", report);
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
                "minimal.array-index",
                "minimal.array-length",
                "minimal.auto-property.getter",
                "minimal.ctor-field.getter",
                "minimal.do-while",
                "minimal.for-loop",
                "minimal.foreach-array",
                "minimal.if-else",
                "minimal.integer-addition",
                "minimal.method-call.same-type",
                "minimal.null-coalesce",
                "minimal.primary-ctor.field-init",
                "minimal.property.literal",
                "minimal.static-method-call",
                "minimal.string-length",
                "minimal.switch-int",
                "minimal.switch-two-case-lowers-if",
                "minimal.try-finally",
                "minimal.using-dispose",
                "minimal.while-loop",
            ],
            GeneratedFixtureCatalog.Select("minimal").Select(fixture => fixture.Id).Order(StringComparer.Ordinal).ToArray());

        Assert.Empty(GeneratedFixtureCatalog.Select("missing"));
    }

    [Fact]
    public void CatalogueListJson_ContainsFixtureIdsAndExpectedStatuses()
    {
        string json = GeneratedFixtureRunner.FormatListJson(GeneratedFixtureCatalog.Catalog);

        using var document = JsonDocument.Parse(json);
        var fixtures = document.RootElement.EnumerateArray().ToArray();
        Assert.Contains(fixtures, fixture => fixture.GetProperty("Id").GetString() == "minimal.property.literal");
        Assert.Contains(fixtures, fixture => fixture.GetProperty("Id").GetString() == "minimal.ctor-field.getter");
        Assert.Contains(fixtures, fixture => fixture.GetProperty("Id").GetString() == "minimal.auto-property.getter");
        Assert.Contains(fixtures, fixture => fixture.GetProperty("Id").GetString() == "minimal.method-call.same-type");
        Assert.Contains(fixtures, fixture => fixture.GetProperty("Id").GetString() == "minimal.static-method-call");
        Assert.Contains(fixtures, fixture => fixture.GetProperty("Id").GetString() == "minimal.if-else");
        Assert.Contains(fixtures, fixture => fixture.GetProperty("Id").GetString() == "minimal.integer-addition");
        Assert.Contains(fixtures, fixture => fixture.GetProperty("Id").GetString() == "minimal.array-index");
        Assert.Contains(fixtures, fixture => fixture.GetProperty("Id").GetString() == "minimal.array-length");
        Assert.Contains(fixtures, fixture => fixture.GetProperty("Id").GetString() == "minimal.string-length");
        Assert.Contains(fixtures, fixture => fixture.GetProperty("Id").GetString() == "minimal.null-coalesce");
        Assert.Contains(fixtures, fixture => fixture.GetProperty("Id").GetString() == "minimal.try-finally");
        Assert.Contains(fixtures, fixture => fixture.GetProperty("Id").GetString() == "minimal.using-dispose");
        Assert.Contains(fixtures, fixture => fixture.GetProperty("Id").GetString() == "minimal.foreach-array");
        Assert.Contains(fixtures, fixture => fixture.GetProperty("Id").GetString() == "minimal.for-loop");
        Assert.Contains(fixtures, fixture => fixture.GetProperty("Id").GetString() == "minimal.while-loop");
        Assert.Contains(fixtures, fixture => fixture.GetProperty("Id").GetString() == "minimal.do-while");
        Assert.Contains(fixtures, fixture => fixture.GetProperty("Id").GetString() == "minimal.switch-int");
        Assert.Contains(fixtures, fixture => fixture.GetProperty("Id").GetString() == "minimal.switch-two-case-lowers-if");

        var primaryCtor = Assert.Single(fixtures,
            fixture => fixture.GetProperty("Id").GetString() == "minimal.primary-ctor.field-init");
        var ctor = Assert.Single(primaryCtor.GetProperty("Targets").EnumerateArray(),
            target => target.GetProperty("Method").GetString() == ".ctor");
        Assert.Equal("Exact", ctor.GetProperty("ExpectedStatus").GetString());
        Assert.False(ctor.GetProperty("IsFrontier").GetBoolean());
    }

    [Fact]
    public void CompilerLoweringFrontier_IsSelectableButNotInDefaultRun()
    {
        var run = GeneratedFixtureRunner.Run(GeneratedFixtureCatalog.Select("minimal.switch-two-case-lowers-if"));
        string report = GeneratedFixtureRunner.FormatReport(run);

        Assert.True(run.Passed, report);
        AssertTarget(
            run,
            "minimal.switch-two-case-lowers-if",
            "GeneratedFixtures.MinimalSwitchTwoCaseLowersIf.Class1",
            ".ctor",
            FidelityCheck.CompileBackStatus.Exact,
            frontier: false);
        AssertTarget(
            run,
            "minimal.switch-two-case-lowers-if",
            "GeneratedFixtures.MinimalSwitchTwoCaseLowersIf.Class1",
            "Method1",
            FidelityCheck.CompileBackStatus.OpcodeDiff,
            frontier: true);
        Assert.Contains("compiler-lowering", GeneratedFixtureCatalog.Frontiers.Single().Tags);
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
