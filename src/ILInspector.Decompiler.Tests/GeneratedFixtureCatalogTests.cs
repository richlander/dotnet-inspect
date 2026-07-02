using System.Text.Json;

using ILInspector.DecompilerHarness;

using Microsoft.CodeAnalysis.CSharp;

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
        Assert.Contains("GENERATED FIXTURE LADDER", report);

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
            "minimal.static-constructor",
            "GeneratedFixtures.MinimalStaticConstructor.Class1",
            ".cctor",
            FidelityCheck.CompileBackStatus.Exact,
            frontier: false);
        AssertTarget(
            run,
            "minimal.static-constructor",
            "GeneratedFixtures.MinimalStaticConstructor.Class1",
            ".ctor",
            FidelityCheck.CompileBackStatus.Exact,
            frontier: false);
        AssertTarget(
            run,
            "minimal.static-constructor",
            "GeneratedFixtures.MinimalStaticConstructor.Class1",
            "Method1",
            FidelityCheck.CompileBackStatus.Exact,
            frontier: false);
        AssertTarget(
            run,
            "minimal.static-class",
            "GeneratedFixtures.MinimalStaticClass.Class1",
            ".cctor",
            FidelityCheck.CompileBackStatus.Exact,
            frontier: false);
        AssertTarget(
            run,
            "minimal.static-class",
            "GeneratedFixtures.MinimalStaticClass.Class1",
            "get_Value",
            FidelityCheck.CompileBackStatus.Exact,
            frontier: false);
        AssertTarget(
            run,
            "minimal.static-class",
            "GeneratedFixtures.MinimalStaticClass.Class1",
            "Method1",
            FidelityCheck.CompileBackStatus.Exact,
            frontier: false);
        AssertTarget(
            run,
            "minimal.interface-implementation",
            "GeneratedFixtures.MinimalInterfaceImplementation.Class1",
            ".ctor",
            FidelityCheck.CompileBackStatus.Exact,
            frontier: false);
        AssertTarget(
            run,
            "minimal.interface-implementation",
            "GeneratedFixtures.MinimalInterfaceImplementation.Class1",
            "GetValue",
            FidelityCheck.CompileBackStatus.Exact,
            frontier: false);
        AssertTarget(
            run,
            "minimal.object-initializer",
            "GeneratedFixtures.MinimalObjectInitializer.Helper",
            ".ctor",
            FidelityCheck.CompileBackStatus.Exact,
            frontier: false);
        AssertTarget(
            run,
            "minimal.object-initializer",
            "GeneratedFixtures.MinimalObjectInitializer.Helper",
            "get_Value",
            FidelityCheck.CompileBackStatus.Exact,
            frontier: false);
        AssertTarget(
            run,
            "minimal.object-initializer",
            "GeneratedFixtures.MinimalObjectInitializer.Helper",
            "set_Value",
            FidelityCheck.CompileBackStatus.Exact,
            frontier: false);
        AssertTarget(
            run,
            "minimal.object-initializer",
            "GeneratedFixtures.MinimalObjectInitializer.Class1",
            ".ctor",
            FidelityCheck.CompileBackStatus.Exact,
            frontier: false);
        AssertTarget(
            run,
            "minimal.object-initializer",
            "GeneratedFixtures.MinimalObjectInitializer.Class1",
            "Method1",
            FidelityCheck.CompileBackStatus.Exact,
            frontier: false);
        AssertTarget(
            run,
            "minimal.collection-initializer",
            "GeneratedFixtures.MinimalCollectionInitializer.Class1",
            ".ctor",
            FidelityCheck.CompileBackStatus.Exact,
            frontier: false);
        AssertTarget(
            run,
            "minimal.collection-initializer",
            "GeneratedFixtures.MinimalCollectionInitializer.Class1",
            "Method1",
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
            "minimal.struct-members",
            "GeneratedFixtures.MinimalStructMembers.Counter",
            "get_Value",
            FidelityCheck.CompileBackStatus.Exact,
            frontier: false);
        AssertTarget(
            run,
            "minimal.struct-members",
            "GeneratedFixtures.MinimalStructMembers.Counter",
            "set_Value",
            FidelityCheck.CompileBackStatus.Exact,
            frontier: false);
        AssertTarget(
            run,
            "minimal.struct-members",
            "GeneratedFixtures.MinimalStructMembers.Counter",
            "Add",
            FidelityCheck.CompileBackStatus.Exact,
            frontier: false);
        AssertTarget(
            run,
            "minimal.struct-constructor.field-init",
            "GeneratedFixtures.MinimalStructConstructorFieldInit.Counter",
            ".ctor",
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
            "minimal.indexer.getter",
            "GeneratedFixtures.MinimalIndexerGetter.Class1",
            ".ctor",
            FidelityCheck.CompileBackStatus.Exact,
            frontier: false);
        AssertTarget(
            run,
            "minimal.indexer.getter",
            "GeneratedFixtures.MinimalIndexerGetter.Class1",
            "get_Item",
            FidelityCheck.CompileBackStatus.Exact,
            frontier: false);
        AssertTarget(
            run,
            "minimal.indexer.setter",
            "GeneratedFixtures.MinimalIndexerSetter.Class1",
            ".ctor",
            FidelityCheck.CompileBackStatus.Exact,
            frontier: false);
        AssertTarget(
            run,
            "minimal.indexer.setter",
            "GeneratedFixtures.MinimalIndexerSetter.Class1",
            "get_Item",
            FidelityCheck.CompileBackStatus.Exact,
            frontier: false);
        AssertTarget(
            run,
            "minimal.indexer.setter",
            "GeneratedFixtures.MinimalIndexerSetter.Class1",
            "set_Item",
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
        Assert.Contains("minimal.static-class", report);
        Assert.Contains("minimal.static-constructor", report);
        Assert.Contains("minimal.interface-implementation", report);
        Assert.Contains("minimal.object-initializer", report);
        Assert.Contains("minimal.collection-initializer", report);
        Assert.Contains("minimal.if-else", report);
        Assert.Contains("minimal.integer-addition", report);
        Assert.Contains("minimal.struct-members", report);
        Assert.Contains("minimal.struct-constructor.field-init", report);
        Assert.Contains("minimal.array-index", report);
        Assert.Contains("minimal.array-length", report);
        Assert.Contains("minimal.indexer.getter", report);
        Assert.Contains("minimal.indexer.setter", report);
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
        Assert.DoesNotContain("minimal.conditional-expression-shape-frontier", report);
        Assert.Contains("decompiler=Full", report);
        Assert.Contains("compile-back=Exact", report);
        Assert.Contains("shape=ForStatement", report);
        Assert.Contains("shape=ElementAccessExpression", report);

        AssertShape(run, "minimal.integer-addition", "Method1", SyntaxKind.AddExpression);
        AssertShape(run, "minimal.array-index", "Method1", SyntaxKind.ElementAccessExpression);
        AssertShape(run, "minimal.array-length", "Method1", SyntaxKind.SimpleMemberAccessExpression);
        AssertShape(run, "minimal.string-length", "Method1", SyntaxKind.SimpleMemberAccessExpression);
        AssertShape(run, "minimal.null-coalesce", "Method1", SyntaxKind.CoalesceExpression);
        AssertShape(run, "minimal.try-finally", "Method1", SyntaxKind.TryStatement);
        AssertShape(run, "minimal.foreach-array", "Method1", SyntaxKind.ForEachStatement);
        AssertShape(run, "minimal.for-loop", "Method1", SyntaxKind.ForStatement);
        AssertShape(run, "minimal.while-loop", "Method1", SyntaxKind.WhileStatement);
        AssertShape(run, "minimal.do-while", "Method1", SyntaxKind.DoStatement);
        AssertShape(run, "minimal.switch-int", "Method1", SyntaxKind.SwitchStatement);
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
                "minimal.collection-initializer",
                "minimal.conditional-expression-shape-frontier",
                "minimal.ctor-field.getter",
                "minimal.do-while",
                "minimal.for-loop",
                "minimal.foreach-array",
                "minimal.if-else",
                "minimal.indexer.getter",
                "minimal.indexer.setter",
                "minimal.integer-addition",
                "minimal.interface-implementation",
                "minimal.method-call.same-type",
                "minimal.null-coalesce",
                "minimal.object-initializer",
                "minimal.primary-ctor.field-init",
                "minimal.property.literal",
                "minimal.static-class",
                "minimal.static-constructor",
                "minimal.static-method-call",
                "minimal.string-length",
                "minimal.struct-constructor.field-init",
                "minimal.struct-members",
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
        string list = GeneratedFixtureRunner.FormatList(GeneratedFixtureCatalog.Catalog);

        Assert.Contains("compile-back=Exact", list);
        Assert.Contains("shape=ElementAccessExpression", list);
        Assert.Contains("shape=none", list);

        using var document = JsonDocument.Parse(json);
        var fixtures = document.RootElement.EnumerateArray().ToArray();
        Assert.Contains(fixtures, fixture => fixture.GetProperty("Id").GetString() == "minimal.property.literal");
        Assert.Contains(fixtures, fixture => fixture.GetProperty("Id").GetString() == "minimal.ctor-field.getter");
        Assert.Contains(fixtures, fixture => fixture.GetProperty("Id").GetString() == "minimal.auto-property.getter");
        Assert.Contains(fixtures, fixture => fixture.GetProperty("Id").GetString() == "minimal.conditional-expression-shape-frontier");
        Assert.Contains(fixtures, fixture => fixture.GetProperty("Id").GetString() == "minimal.method-call.same-type");
        Assert.Contains(fixtures, fixture => fixture.GetProperty("Id").GetString() == "minimal.static-method-call");
        Assert.Contains(fixtures, fixture => fixture.GetProperty("Id").GetString() == "minimal.static-class");
        Assert.Contains(fixtures, fixture => fixture.GetProperty("Id").GetString() == "minimal.static-constructor");
        Assert.Contains(fixtures, fixture => fixture.GetProperty("Id").GetString() == "minimal.interface-implementation");
        Assert.Contains(fixtures, fixture => fixture.GetProperty("Id").GetString() == "minimal.object-initializer");
        Assert.Contains(fixtures, fixture => fixture.GetProperty("Id").GetString() == "minimal.collection-initializer");
        Assert.Contains(fixtures, fixture => fixture.GetProperty("Id").GetString() == "minimal.if-else");
        Assert.Contains(fixtures, fixture => fixture.GetProperty("Id").GetString() == "minimal.integer-addition");
        Assert.Contains(fixtures, fixture => fixture.GetProperty("Id").GetString() == "minimal.struct-members");
        Assert.Contains(fixtures, fixture => fixture.GetProperty("Id").GetString() == "minimal.struct-constructor.field-init");
        Assert.Contains(fixtures, fixture => fixture.GetProperty("Id").GetString() == "minimal.array-index");
        Assert.Contains(fixtures, fixture => fixture.GetProperty("Id").GetString() == "minimal.array-length");
        Assert.Contains(fixtures, fixture => fixture.GetProperty("Id").GetString() == "minimal.indexer.getter");
        Assert.Contains(fixtures, fixture => fixture.GetProperty("Id").GetString() == "minimal.indexer.setter");
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
        Assert.Equal(JsonValueKind.Null, ctor.GetProperty("ExpectedShape").ValueKind);
        Assert.False(ctor.GetProperty("IsFrontier").GetBoolean());

        var arrayIndex = Assert.Single(fixtures,
            fixture => fixture.GetProperty("Id").GetString() == "minimal.array-index");
        var arrayIndexMethod = Assert.Single(arrayIndex.GetProperty("Targets").EnumerateArray(),
            target => target.GetProperty("Method").GetString() == "Method1");
        Assert.Equal("ElementAccessExpression", arrayIndexMethod.GetProperty("ExpectedShape").GetString());

        var conditionalFrontier = Assert.Single(fixtures,
            fixture => fixture.GetProperty("Id").GetString() == "minimal.conditional-expression-shape-frontier");
        var conditionalTarget = Assert.Single(conditionalFrontier.GetProperty("Targets").EnumerateArray(),
            target => target.GetProperty("Method").GetString() == "Method1");
        Assert.Equal("ReturnStatement", conditionalTarget.GetProperty("ExpectedShape").GetString());
        Assert.Equal("ConditionalExpression", conditionalTarget.GetProperty("FrontierShape").GetString());
        Assert.True(conditionalTarget.GetProperty("IsFrontier").GetBoolean());
    }

    [Fact]
    public void ReturnToSenderCatalogReport_ClassifiesSupportedAndSkippedTargets()
    {
        var run = GeneratedFixtureRunner.RunReturnToSenderCatalog(
            [
                GeneratedFixtureCatalog.MinimalPropertyLiteral,
                GeneratedFixtureCatalog.MinimalMethodCallSameType,
            ]);
        string report = GeneratedFixtureRunner.FormatReturnToSenderCatalogReport(run, maxExamples: 10);

        Assert.True(run.Passed, report);
        Assert.Contains("RETURNTOSENDER GENERATED FIXTURE FRONTIER", report);
        Assert.Contains("Passed : 2", report);
        Assert.Contains("Skipped: 0", report);
        Assert.DoesNotContain("constructor-target", report);

        var propertyGetter = Assert.Single(run.Results, result =>
            result.FixtureId == "minimal.property.literal"
            && result.Method == "get_Method1");
        Assert.Equal(GeneratedFixtureReturnToSenderStatus.Pass, propertyGetter.Status);
        Assert.Equal(FidelityCheck.CompileBackStatus.Exact, propertyGetter.ActualStatus);

        Assert.Contains(run.Results, result =>
            result.FixtureId == "minimal.method-call.same-type"
            && result.Method == "Method1"
            && result.Status == GeneratedFixtureReturnToSenderStatus.Pass);
        Assert.Contains(run.Results, result =>
            result.FixtureId == "minimal.method-call.same-type"
            && result.Method == "Method2"
            && result.Status == GeneratedFixtureReturnToSenderStatus.Pass);

        string json = GeneratedFixtureRunner.FormatReturnToSenderCatalogJson(run);
        using var document = JsonDocument.Parse(json);
        Assert.True(document.RootElement.GetProperty("Passed").GetBoolean());
        Assert.Contains(document.RootElement.GetProperty("Results").EnumerateArray(),
            result => result.GetProperty("Status").GetString() == "Pass");
        Assert.DoesNotContain(document.RootElement.GetProperty("Results").EnumerateArray(),
            result => result.GetProperty("Reason").GetString() == "constructor-target");
    }

    [Fact]
    public void CompilerLoweringFrontiers_AreSelectableButNotInDefaultRun()
    {
        var switchRun = GeneratedFixtureRunner.Run(GeneratedFixtureCatalog.Select("minimal.switch-two-case-lowers-if"));
        string switchReport = GeneratedFixtureRunner.FormatReport(switchRun);

        Assert.True(switchRun.Passed, switchReport);
        AssertTarget(
            switchRun,
            "minimal.switch-two-case-lowers-if",
            "GeneratedFixtures.MinimalSwitchTwoCaseLowersIf.Class1",
            ".ctor",
            FidelityCheck.CompileBackStatus.Exact,
            frontier: false);
        AssertTarget(
            switchRun,
            "minimal.switch-two-case-lowers-if",
            "GeneratedFixtures.MinimalSwitchTwoCaseLowersIf.Class1",
            "Method1",
            FidelityCheck.CompileBackStatus.OpcodeDiff,
            frontier: true);

        var shapeRun = GeneratedFixtureRunner.Run(GeneratedFixtureCatalog.Select("minimal.conditional-expression-shape-frontier"));
        string shapeReport = GeneratedFixtureRunner.FormatReport(shapeRun);

        Assert.True(shapeRun.Passed, shapeReport);
        AssertTarget(
            shapeRun,
            "minimal.conditional-expression-shape-frontier",
            "GeneratedFixtures.MinimalConditionalExpressionShapeFrontier.Class1",
            ".ctor",
            FidelityCheck.CompileBackStatus.Exact,
            frontier: false);
        AssertTarget(
            shapeRun,
            "minimal.conditional-expression-shape-frontier",
            "GeneratedFixtures.MinimalConditionalExpressionShapeFrontier.Class1",
            "Method1",
            FidelityCheck.CompileBackStatus.Exact,
            frontier: true);
        var shapeResult = Assert.Single(shapeRun.Results, result =>
            result.FixtureId == "minimal.conditional-expression-shape-frontier" &&
            result.Method == "Method1");
        Assert.Equal(SyntaxKind.ReturnStatement, shapeResult.ExpectedShape);
        Assert.Equal(SyntaxKind.ReturnStatement, shapeResult.ActualShape);
        Assert.Equal(SyntaxKind.ConditionalExpression, shapeResult.FrontierShape);
        Assert.Contains("frontier-shape=ConditionalExpression", shapeReport);

        Assert.Contains(GeneratedFixtureCatalog.Frontiers, fixture =>
            fixture.Id == "minimal.switch-two-case-lowers-if" &&
            fixture.Tags.Contains("compiler-lowering"));
        Assert.Contains(GeneratedFixtureCatalog.Frontiers, fixture =>
            fixture.Id == "minimal.conditional-expression-shape-frontier" &&
            fixture.Tags.Contains("shape"));
    }

    [Fact]
    public void SelectedFixtureRunJson_ContainsOnlySelectedFixtureResults()
    {
        var selected = GeneratedFixtureCatalog.Select("minimal.array-index");
        var run = GeneratedFixtureRunner.Run(selected);
        string json = GeneratedFixtureRunner.FormatJson(run);

        using var document = JsonDocument.Parse(json);
        var results = document.RootElement.GetProperty("Results").EnumerateArray().ToArray();

        Assert.Equal(2, results.Length);
        Assert.All(results, result => Assert.Equal("minimal.array-index", result.GetProperty("FixtureId").GetString()));
        var method = Assert.Single(results, result => result.GetProperty("Method").GetString() == "Method1");
        Assert.Equal("Exact", method.GetProperty("ActualStatus").GetString());
        Assert.Equal("ElementAccessExpression", method.GetProperty("ActualShape").GetString());
        Assert.Equal("ElementAccessExpression", method.GetProperty("ExpectedShape").GetString());
        Assert.True(method.GetProperty("ShapePassed").GetBoolean());
    }

    [Fact]
    public void ShapeFrontierRunJson_ReportsAcceptedAndFrontierShapes()
    {
        var selected = GeneratedFixtureCatalog.Select("minimal.conditional-expression-shape-frontier");
        var run = GeneratedFixtureRunner.Run(selected);
        string json = GeneratedFixtureRunner.FormatJson(run);

        using var document = JsonDocument.Parse(json);
        var results = document.RootElement.GetProperty("Results").EnumerateArray().ToArray();
        var method = Assert.Single(results, result => result.GetProperty("Method").GetString() == "Method1");

        Assert.Equal("Exact", method.GetProperty("ActualStatus").GetString());
        Assert.Equal("ReturnStatement", method.GetProperty("ActualShape").GetString());
        Assert.Equal("ReturnStatement", method.GetProperty("ExpectedShape").GetString());
        Assert.Equal("ConditionalExpression", method.GetProperty("FrontierShape").GetString());
        Assert.True(method.GetProperty("ShapePassed").GetBoolean());
    }

    [Fact]
    public void ShapeFrontierRun_FailsWhenFrontierShapeIsAchieved()
    {
        var alreadyImproved = new GeneratedFixtureDefinition(
            "test.frontier-shape-achieved",
            """
            namespace GeneratedFixtures.TestFrontierShapeAchieved;

            public class Class1
            {
                public int Method1(int left, int right) => left + right;
            }
            """,
            [
                new(
                    "GeneratedFixtures.TestFrontierShapeAchieved.Class1",
                    "Method1",
                    FidelityCheck.CompileBackStatus.Exact,
                    IsFrontier: true,
                    ExpectedShape: SyntaxKind.ReturnStatement,
                    FrontierShape: SyntaxKind.AddExpression),
            ],
            ["test"]);

        var run = GeneratedFixtureRunner.Run([alreadyImproved]);
        string report = GeneratedFixtureRunner.FormatReport(run);
        var result = Assert.Single(run.Results);

        Assert.False(run.Passed);
        Assert.Equal(SyntaxKind.AddExpression, result.ActualShape);
        Assert.Equal(SyntaxKind.ReturnStatement, result.ExpectedShape);
        Assert.Equal(SyntaxKind.AddExpression, result.FrontierShape);
        Assert.False(result.ShapePassed);
        Assert.Contains("frontier-shape-achieved", report);
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
        Assert.True(result.CompileBackPassed);
        Assert.True(result.ShapePassed);
        Assert.Equal(frontier, result.IsFrontier);
        Assert.Equal("Full", result.DecompilerFidelity);
        Assert.True(result.Passed);
    }

    static void AssertShape(GeneratedFixtureRunResult run, string fixtureId, string method, SyntaxKind expected)
    {
        var result = Assert.Single(run.Results, r =>
            r.FixtureId == fixtureId &&
            r.Method == method);

        Assert.Equal(expected, result.ExpectedShape);
        Assert.Equal(expected, result.ActualShape);
        Assert.True(result.ShapePassed);
    }
}
