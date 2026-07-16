using System.Text.Json;

using ILInspector.DecompilerHarness;
using ILInspector.Instructions;
using ILInspector.MetadataPrimitives;

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

        // run.Passed asserts every fixture-declared target matched its declared
        // expected compile-back status and shape. Expected outcomes are owned by
        // the fixtures (docs/fixture-governance.md, "Expectation ownership"), so
        // this iterates the advertised inventory instead of re-encoding each
        // target as a hand-maintained literal.
        Assert.True(run.Passed, report);
        Assert.NotEmpty(run.Results);
        Assert.Contains("GENERATED FIXTURE LADDER", report);

        foreach (var result in run.Results)
        {
            Assert.True(
                result.Passed && result.DecompilerFidelity == "Full",
                $"{result.FixtureId} {result.DisplayMember}: fidelity={result.DecompilerFidelity}, " +
                $"expected={result.ExpectedStatus}, actual={result.ActualStatus}, shapePassed={result.ShapePassed}");
        }

        // Every fixture in the default run is rendered by id in the report.
        foreach (var fixtureId in run.Results.Select(result => result.FixtureId).Distinct())
            Assert.Contains(fixtureId, report);

        // Default-run composition policy: the accepted conditional-expression
        // frontier ships in the minimal run; the switch-lowering frontier does not.
        Assert.Contains(run.Results, result => result.FixtureId == "minimal.conditional-expression-shape-frontier");
        Assert.DoesNotContain(run.Results, result => result.FixtureId == "minimal.switch-two-case-lowers-if");

        // Report formatting smoke: fidelity, compile-back, and shape tokens render.
        Assert.Contains("decompiler=Full", report);
        Assert.Contains("compile-back=Exact", report);
        Assert.Contains("shape=ForStatement", report);
        Assert.Contains("shape=ElementAccessExpression", report);
    }

    [Fact]
    public void CatalogueSelection_MatchesExactIdOrPrefix()
    {
        var catalog = GeneratedFixtureCatalog.Catalog;

        // Exact id selects exactly that fixture.
        Assert.Equal(
            ["minimal.property.literal"],
            Ids(GeneratedFixtureCatalog.Select("minimal.property.literal")));

        // A prefix selects every catalog id under that prefix. The expected set
        // is derived from the catalog itself, so adding a fixture under an
        // existing prefix never forces a matching literal-list edit here.
        foreach (var prefix in new[] { "rts", "record", "minimal" })
        {
            var selected = GeneratedFixtureCatalog.Select(prefix);
            Assert.NotEmpty(selected);
            Assert.All(selected, fixture => Assert.StartsWith(prefix, fixture.Id, StringComparison.Ordinal));
            Assert.Equal(
                catalog.Where(fixture => fixture.Id.StartsWith(prefix, StringComparison.Ordinal))
                    .Select(fixture => fixture.Id)
                    .Order(StringComparer.Ordinal),
                Ids(selected).Order(StringComparer.Ordinal));
        }

        // An empty selector falls back to the default run; an unknown selector
        // is empty.
        Assert.Equal(
            Ids(GeneratedFixtureCatalog.MinimalCompileBackRungs),
            Ids(GeneratedFixtureCatalog.Select(null)));
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

        // Every catalog fixture is emitted exactly once. The expected id set
        // is derived from the catalog, so a new fixture appears here without a
        // matching literal edit.
        Assert.Equal(
            GeneratedFixtureCatalog.Catalog.Select(fixture => fixture.Id).Order(StringComparer.Ordinal),
            fixtures.Select(fixture => fixture.GetProperty("Id").GetString()).Order(StringComparer.Ordinal));

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
        Assert.Contains("Research evidence:", report);
        Assert.Contains("rts.status.pass:", report);
        Assert.DoesNotContain("constructor-target", report);

        var propertyGetter = Assert.Single(run.Results, result =>
            result.FixtureId == "minimal.property.literal"
            && result.Method == "get_Method1");
        Assert.Equal(GeneratedFixtureReturnToSenderStatus.Pass, propertyGetter.Status);
        Assert.Equal(FidelityCheck.CompileBackStatus.Exact, propertyGetter.ActualStatus);
        Assert.Null(propertyGetter.IlDiffDiagnostic);
        Assert.NotNull(propertyGetter.MemberAnchor);
        Assert.StartsWith("Method1~", propertyGetter.MemberAnchor.StableSelector, StringComparison.Ordinal);
        Assert.Equal("P:GeneratedFixtures.MinimalPropertyLiteral.Class1.Method1", propertyGetter.MemberAnchor.CanonicalSignature);

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
        Assert.Contains(document.RootElement.GetProperty("Results").EnumerateArray(),
            result => result.GetProperty("Method").GetString() == "get_Method1"
                && result.GetProperty("MemberAnchor").GetProperty("StableSelector").GetString()!.StartsWith("Method1~", StringComparison.Ordinal));
        var researchDiff = document.RootElement.GetProperty("ResearchDiff");
        Assert.Contains(researchDiff.GetProperty("Changes").EnumerateArray(),
            change => change.GetProperty("DescriptorId").GetString() == "rts.status.pass");
        Assert.DoesNotContain(document.RootElement.GetProperty("Results").EnumerateArray(),
            result => result.GetProperty("Reason").GetString() == "constructor-target");
    }

    [Fact]
    public void ReturnToSenderCatalogReport_IncludesIlDiffDiagnosticsWhenPresent()
    {
        var run = new GeneratedFixtureReturnToSenderRunResult(
            ProjectDirectory: "",
            AssemblyPath: "",
            Results:
            [
                new GeneratedFixtureReturnToSenderResult(
                    "test.il-diff-diagnostic",
                    "TestType",
                    "Method1",
                    Overload: 0,
                    GeneratedFixtureReturnToSenderStatus.Fail,
                    FidelityCheck.CompileBackStatus.OpcodeDiff,
                    "opcode-diff",
                    Detail: null,
                    IlDiffDiagnostic: new IlDiffDisplayResult(
                        Failure: null,
                        Rows:
                        [
                            new IlDiffDisplayRow(
                                0,
                                IlDiffKind.Remove,
                                "-",
                                0,
                                "IL_0000",
                                "ldc.i4",
                                IlOperandIdentityKind.Immediate,
                                "1",
                                "ldc.i4 1",
                                "Removed IL operation 'ldc.i4 1'"),
                            new IlDiffDisplayRow(
                                0,
                                IlDiffKind.Add,
                                "+",
                                0,
                                "IL_0000",
                                "ldc.i4",
                                IlOperandIdentityKind.Immediate,
                                "2",
                                "ldc.i4 2",
                                "Added IL operation 'ldc.i4 2'"),
                        ]),
                    IlDiff: null,
                    MemberAnchor: new MemberAnchor(
                        "Method1~abcdef1234",
                        "M:TestType.Method1()",
                        "abcdef1234",
                        "TestType",
                        "Method1"),
                    FaultIsolation: null,
                    ClosureEvidence: new ReturnToSenderClosureEvidence(
                        RequiredTypes: 2,
                        RequiredMembers: 1,
                        RoslynRecoveredTypes: 1,
                        RoslynRecoveredMemberSurfaces: 1,
                        RoslynFallbacks:
                        [
                            new ReturnToSenderRoslynFallback("CS0103", "closure-root", 1),
                        ],
                        Requirements:
                        [
                            new ReturnToSenderClosureRequirement(
                                "TestType",
                                RequiredMembers: 1,
                                RoslynRecovered: true,
                                RoslynRecoveredMemberSurface: true,
                                Facts: ["roslyn/closure-root: CS0103: Helper"]),
                        ]),
                    IsFrontier: false,
                    Note: null),
            ]);

        string report = GeneratedFixtureRunner.FormatReturnToSenderCatalogReport(run, maxExamples: 10);
        var view = ReturnToSenderCatalogReport.Build(run, maxExamples: 10);

        Assert.Contains("il-diff:", report);
        Assert.Contains("h0 - IL_0000 ldc.i4 1", report);
        Assert.Contains("h0 + IL_0000 ldc.i4 2", report);
        Assert.Contains("member: Method1~abcdef1234  canonical=M:TestType.Method1()", report);
        Assert.Contains("closure: types=2 members=1 roslyn-types=1 roslyn-member-surfaces=1", report);
        Assert.Contains("roslyn-fallbacks=CS0103/closure-root:1", report);
        Assert.Contains("Roslyn fallback evidence:", report);
        Assert.Contains("CS0103/closure-root: 1", report);
        Assert.Contains("Research evidence:", report);
        Assert.Contains("rts.status.fail: 1", report);
        Assert.Contains("il.operation.added: 1", report);
        Assert.Contains("il.operation.removed: 1", report);
        Assert.Contains("Actionable subjects (first 1", report);
        Assert.Contains("Method1~abcdef1234  rts=OpcodeDiff  detail=opcode-diff", report);
        Assert.Contains("      il.operation.added: 1", report);
        Assert.Contains("      il.operation.removed: 1", report);
        Assert.Equal(1, view.Fixtures.Failed);
        Assert.Equal(1, view.Targets.Failed);
        Assert.NotNull(view.Research);
        Assert.Single(view.Research.Summary.ActionableSubjects);
        var fallback = Assert.Single(view.RoslynFallbacks);
        Assert.Equal("CS0103/closure-root", fallback.Key);
        Assert.Equal(1, fallback.Count);
        Assert.Single(view.FailedTargetBuckets);
        Assert.Single(view.FailedFixtures);

        string markout = GeneratedFixtureRunner.FormatReturnToSenderCatalogMarkout(run, maxExamples: 10);
        Assert.Contains("# ReturnToSender Catalog", markout);
        Assert.Contains("## Summary", markout);
        Assert.Contains("## Research evidence", markout);
        Assert.Contains("## Actionable subjects", markout);
        Assert.Contains("## Roslyn fallback evidence", markout);
        Assert.Contains("CS0103/closure-root", markout);
        Assert.Contains("Method1~abcdef1234", markout);
        Assert.Contains("il.operation.added: 1", markout);
        Assert.Contains("IL display:", markout);
        Assert.Contains("- il.operation.removed: h0 - IL_0000 ldc.i4 1", markout);
        Assert.Contains("IL diff:", markout);
        Assert.Contains("- h0 + IL_0000 ldc.i4 2", markout);
        Assert.Contains("      il-display:", report);
        Assert.Contains("        il.operation.removed: h0 - IL_0000 ldc.i4 1", report);
        Assert.Contains("        il.operation.added: h0 + IL_0000 ldc.i4 2", report);

        string json = GeneratedFixtureRunner.FormatReturnToSenderCatalogJson(run);
        using var document = JsonDocument.Parse(json);
        var fallbackJson = Assert.Single(document.RootElement.GetProperty("RoslynFallbacks").EnumerateArray());
        Assert.Equal("CS0103/closure-root", fallbackJson.GetProperty("Key").GetString());
        Assert.Equal(1, fallbackJson.GetProperty("Count").GetInt32());
        var actionable = Assert.Single(document.RootElement
            .GetProperty("ResearchSummary")
            .GetProperty("ActionableSubjects")
            .EnumerateArray());
        var ilEvidence = actionable.GetProperty("IlEvidence").EnumerateArray().ToArray();
        Assert.Contains(ilEvidence, evidence =>
            evidence.GetProperty("ChangeId").GetString() == "il.operation.removed"
            && evidence.GetProperty("Rows").EnumerateArray().Single().GetProperty("UnifiedLine").GetString() == "h0 - IL_0000 ldc.i4 1");
        Assert.Contains(ilEvidence, evidence =>
            evidence.GetProperty("ChangeId").GetString() == "il.operation.added"
            && evidence.GetProperty("Rows").EnumerateArray().Single().GetProperty("UnifiedLine").GetString() == "h0 + IL_0000 ldc.i4 2");
    }

    [Fact]
    public void ReturnToSenderCatalogReport_IncludesResearchIlFailureRows()
    {
        var failure = new IlDiffDisplayFailureRow(
            IlDiffFailureKind.NewBodyMissing,
            "new body missing",
            Side: "new",
            Detail: "method has no body");
        var run = new GeneratedFixtureReturnToSenderRunResult(
            ProjectDirectory: "",
            AssemblyPath: "",
            Results:
            [
                new GeneratedFixtureReturnToSenderResult(
                    "test.il-diff-failure",
                    "TestType",
                    "Method1",
                    Overload: 0,
                    GeneratedFixtureReturnToSenderStatus.Fail,
                    FidelityCheck.CompileBackStatus.OpcodeDiff,
                    "opcode-diff",
                    Detail: null,
                    IlDiffDiagnostic: new IlDiffDisplayResult(
                        Failure: failure.UnifiedLine,
                        Rows: [],
                        FailureRows: [failure]),
                    IlDiff: null,
                    MemberAnchor: new MemberAnchor(
                        "Method1~abcdef1234",
                        "M:TestType.Method1()",
                        "abcdef1234",
                        "TestType",
                        "Method1"),
                    FaultIsolation: null,
                    ClosureEvidence: null,
                    IsFrontier: false,
                    Note: null),
            ]);

        string report = GeneratedFixtureRunner.FormatReturnToSenderCatalogReport(run, maxExamples: 10);

        Assert.Contains("      il-display:", report);
        Assert.Contains("        il.diff.new-body-missing: IL diff failed: new body missing", report);

        string json = GeneratedFixtureRunner.FormatReturnToSenderCatalogJson(run);
        using var document = JsonDocument.Parse(json);
        var actionable = Assert.Single(document.RootElement
            .GetProperty("ResearchSummary")
            .GetProperty("ActionableSubjects")
            .EnumerateArray());
        var evidence = Assert.Single(actionable.GetProperty("IlEvidence").EnumerateArray());
        Assert.Equal("il.diff.new-body-missing", evidence.GetProperty("ChangeId").GetString());
        var failureJson = evidence.GetProperty("Failure");
        Assert.Equal("NewBodyMissing", failureJson.GetProperty("Kind").GetString());
        Assert.Equal("new body missing", failureJson.GetProperty("Message").GetString());
        Assert.Equal("new", failureJson.GetProperty("Side").GetString());
        Assert.Equal("method has no body", failureJson.GetProperty("Detail").GetString());
        Assert.Equal("IL diff failed: new body missing", failureJson.GetProperty("UnifiedLine").GetString());
    }

    [Fact]
    public void ReturnToSenderCatalogJson_RedactsFaultIsolationSourcePath()
    {
        const string sourcePath = @"C:\Users\builder\repo\Authored.cs";
        var run = new GeneratedFixtureReturnToSenderRunResult(
            ProjectDirectory: "",
            AssemblyPath: "",
            Results:
            [
                new GeneratedFixtureReturnToSenderResult(
                    "test.fault-isolation",
                    "TestType",
                    "Method1",
                    Overload: 0,
                    GeneratedFixtureReturnToSenderStatus.Fail,
                    FidelityCheck.CompileBackStatus.RecompileFail,
                    "body-defect (CS0103)",
                    Detail: "CS0103: missing symbol",
                    IlDiffDiagnostic: null,
                    IlDiff: null,
                    MemberAnchor: new MemberAnchor(
                        "Method1~abcdef1234",
                        "M:TestType.Method1()",
                        "abcdef1234",
                        "TestType",
                        "Method1"),
                    FaultIsolation: new ReturnToSender.FaultIsolationResult(
                        ReturnToSender.FaultIsolationKind.BodyDefect,
                        sourcePath,
                        "authored body compiled in the same RTS shell"),
                    ClosureEvidence: null,
                    IsFrontier: false,
                    Note: null),
            ]);

        string json = GeneratedFixtureRunner.FormatReturnToSenderCatalogJson(run);

        Assert.DoesNotContain(sourcePath, json);
        Assert.DoesNotContain("Users", json);
        Assert.DoesNotContain("builder", json);
        using var document = JsonDocument.Parse(json);
        var result = Assert.Single(document.RootElement.GetProperty("Results").EnumerateArray());
        Assert.Equal("RecompileFail", result.GetProperty("ActualStatus").GetString());
        Assert.Equal("body-defect (CS0103)", result.GetProperty("Reason").GetString());
        var faultIsolation = result.GetProperty("FaultIsolation");
        Assert.Equal("BodyDefect", faultIsolation.GetProperty("Kind").GetString());
        Assert.Equal("Authored.cs", faultIsolation.GetProperty("SourcePath").GetString());
        Assert.Equal("authored body compiled in the same RTS shell", faultIsolation.GetProperty("Detail").GetString());
    }

    [Fact]
    public void ReturnToSenderCatalogFailureReason_ComposesFaultIsolationWithDiagnosticBucket()
    {
        var result = new ReturnToSender.Result(
            MinimalReturnToSenderPlan(),
            Source: "",
            Status: FidelityCheck.CompileBackStatus.RecompileFail,
            OriginalOpcodes: "",
            RecompiledOpcodes: "",
            Detail: "CS0103: The name 'Missing' does not exist in the current context",
            FaultIsolation: new ReturnToSender.FaultIsolationResult(
                ReturnToSender.FaultIsolationKind.BodyDefect,
                "Authored.cs",
                "authored body compiled in the same RTS shell"));

        Assert.Equal("body-defect (CS0103)", GeneratedFixtureRunner.FailureReason(result));
        Assert.Equal(FidelityCheck.CompileBackStatus.RecompileFail, result.Status);
    }

    static CompileBackReconstructionPlan MinimalReturnToSenderPlan()
        => new(
            AssemblyPath: "",
            TargetMethod: new CompileBackMethodIdentity("TestType", "Method1", 0, ""),
            Module: new CompileBackModuleRequirement(Usings: [], AssemblyAttributes: [], ModuleAttributes: []),
            Types: [],
            PrintRequests: [],
            Diagnostics: []);

    [Fact]
    public void ReturnToSenderRecordCatalog_CoversGeneratedRecordHelpers()
    {
        var run = GeneratedFixtureRunner.RunReturnToSenderCatalog(
            GeneratedFixtureCatalog.Select("record"));
        string report = GeneratedFixtureRunner.FormatReturnToSenderCatalogReport(run, maxExamples: 10);

        Assert.True(run.Passed, report);
        Assert.Equal(12, run.Results.Count);
        Assert.All(run.Results, result =>
        {
            Assert.Equal(GeneratedFixtureReturnToSenderStatus.Pass, result.Status);
            Assert.Equal(FidelityCheck.CompileBackStatus.Exact, result.ActualStatus);
        });
        Assert.Contains(run.Results, result =>
            result.FixtureId == "record.equality-operators" &&
            result.Method == "op_Equality");
        Assert.Contains(run.Results, result =>
            result.FixtureId == "record.field-read-helpers" &&
            result.Method == "Equals" &&
            result.Overload == 1);
        Assert.Contains(run.Results, result =>
            result.FixtureId == "record.struct-field-read-helpers" &&
            result.Method == "Equals" &&
            result.Overload == 1);
        Assert.Contains(run.Results, result =>
            result.FixtureId == "record.generic-typed-equals" &&
            result.Type == "RecordGenericTypedEqualsRow`1");
        Assert.Contains(run.Results, result =>
            result.FixtureId == "record.nested-generic-typed-equals" &&
            result.Type == "RecordNestedGenericTypedEqualsContainer`1.Row`1");
        Assert.Contains("record.generic-typed-equals", report);
        Assert.Contains("record.nested-generic-typed-equals", report);
        Assert.DoesNotContain("Skipped target reasons", report);
        Assert.DoesNotContain("Failed target buckets", report);
    }

    [Fact]
    public void ReturnToSenderRtsCatalog_CoversAttributeShellBaseType()
    {
        var run = GeneratedFixtureRunner.RunReturnToSenderCatalog(
            GeneratedFixtureCatalog.Select("rts.attribute-shell"));
        string report = GeneratedFixtureRunner.FormatReturnToSenderCatalogReport(run, maxExamples: 10);

        Assert.True(run.Passed, report);
        Assert.Equal(2, run.Results.Count);
        Assert.All(run.Results, result =>
        {
            Assert.Equal(GeneratedFixtureReturnToSenderStatus.Pass, result.Status);
            Assert.Equal(FidelityCheck.CompileBackStatus.Exact, result.ActualStatus);
        });
        Assert.Contains(run.Results, result =>
            result.FixtureId == "rts.attribute-shell" &&
            result.Method == ".ctor");
        Assert.Contains(run.Results, result =>
            result.FixtureId == "rts.attribute-shell" &&
            result.Method == "get_FeatureType");
        Assert.DoesNotContain("CS0641", report);
    }

    [Fact]
    public void ReturnToSenderRtsCatalog_KeepsShiftedSiblingNestedGenericParameter()
    {
        var run = GeneratedFixtureRunner.RunReturnToSenderCatalog(
            GeneratedFixtureCatalog.Select("rts.shifted-sibling-nested-generic"));
        string report = GeneratedFixtureRunner.FormatReturnToSenderCatalogReport(run, maxExamples: 10);

        Assert.True(run.Passed, report);
        var result = Assert.Single(run.Results);
        Assert.Equal(GeneratedFixtureReturnToSenderStatus.Pass, result.Status);
        Assert.Equal(FidelityCheck.CompileBackStatus.Exact, result.ActualStatus);
        Assert.Equal("ShiftedSiblingOuter`1.Inner`3", result.Type);
        Assert.Equal("ReadShifted", result.Method);
    }

    [Fact]
    public void ReturnToSenderRecordCatalog_TargetBodyFragmentsDoNotMatchShellSource()
    {
        var shellOnlyFragment = new GeneratedFixtureDefinition(
            "test.record-shell-only-fragment",
            """
            public record ShellOnlyFragmentRecord(string Name, string Value);
            """,
            [
                new(
                    "ShellOnlyFragmentRecord",
                    "GetHashCode",
                    FidelityCheck.CompileBackStatus.Exact,
                    ExpectedTargetBodyFragments:
                    [
                        "public string Name;",
                    ]),
            ],
            ["test", "record"]);

        var run = GeneratedFixtureRunner.RunReturnToSenderCatalog([shellOnlyFragment]);
        string report = GeneratedFixtureRunner.FormatReturnToSenderCatalogReport(run, maxExamples: 10);
        var result = Assert.Single(run.Results);

        Assert.False(run.Passed, report);
        Assert.Equal(GeneratedFixtureReturnToSenderStatus.Fail, result.Status);
        Assert.Equal(FidelityCheck.CompileBackStatus.Exact, result.ActualStatus);
        Assert.Equal("target-body-fragment-missing", result.Reason);
        Assert.Contains("missing expected target body fragment: public string Name;", result.Detail);
    }

    [Fact]
    public void ReturnToSenderCatalog_NonExactRowsKeepFailureReasonBeforeBodyFragments()
    {
        var opcodeDiffWithBodyFragment = new GeneratedFixtureDefinition(
            "test.non-exact-body-fragment",
            """
            public class NonExactBodyFragment
            {
                public string Method1(int value)
                {
                    switch (value)
                    {
                        case 0:
                            return "zero";
                        case 1:
                            return "one";
                        default:
                            return "many";
                    }
                }
            }
            """,
            [
                new(
                    "NonExactBodyFragment",
                    "Method1",
                    FidelityCheck.CompileBackStatus.OpcodeDiff,
                    IsFrontier: true,
                    ExpectedTargetBodyFragments:
                    [
                        "fragment that is absent from the target body",
                    ]),
            ],
            ["test"]);

        var run = GeneratedFixtureRunner.RunReturnToSenderCatalog([opcodeDiffWithBodyFragment]);
        string report = GeneratedFixtureRunner.FormatReturnToSenderCatalogReport(run, maxExamples: 10);
        var result = Assert.Single(run.Results);

        Assert.False(run.Passed, report);
        Assert.Equal(GeneratedFixtureReturnToSenderStatus.Fail, result.Status);
        Assert.Equal(FidelityCheck.CompileBackStatus.OpcodeDiff, result.ActualStatus);
        Assert.Equal("opcode-diff", result.Reason);
        Assert.NotNull(result.IlDiffDiagnostic);
        Assert.NotEmpty(result.IlDiffDiagnostic.Rows);
        Assert.Contains(result.IlDiffDiagnostic.Rows, row => row.Offset.StartsWith("IL_", StringComparison.Ordinal));
        Assert.DoesNotContain("target-body-fragment-missing", report);

        string json = GeneratedFixtureRunner.FormatReturnToSenderCatalogJson(run);
        using var document = JsonDocument.Parse(json);
        var jsonResult = Assert.Single(document.RootElement.GetProperty("Results").EnumerateArray());
        var rows = jsonResult.GetProperty("IlDiffDiagnostic").GetProperty("Rows").EnumerateArray().ToArray();
        Assert.NotEmpty(rows);
        Assert.Contains(rows, row =>
            row.GetProperty("Offset").GetString()?.StartsWith("IL_", StringComparison.Ordinal) == true
            && row.GetProperty("OpcodeFamily").GetString() is { Length: > 0 });
        var summary = document.RootElement.GetProperty("ResearchSummary");
        Assert.Equal(1, summary.GetProperty("FailingMembers").GetInt32());
        Assert.Equal(1, summary.GetProperty("OpcodeDiffMembers").GetInt32());
        var actionable = Assert.Single(summary.GetProperty("ActionableSubjects").EnumerateArray());
        Assert.StartsWith("Method1~", actionable.GetProperty("SubjectId").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public void CompilerLoweringFrontier_IsSelectableButNotInDefaultRun()
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

        var shapeRun = GeneratedFixtureRunner.Run(GeneratedFixtureCatalog.MinimalCompileBackRungs);
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
        Assert.DoesNotContain(GeneratedFixtureCatalog.Frontiers, fixture =>
            fixture.Id == "minimal.conditional-expression-shape-frontier");
        Assert.Contains(GeneratedFixtureCatalog.MinimalCompileBackRungs, fixture =>
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

    static string[] Ids(IEnumerable<GeneratedFixtureDefinition> fixtures)
        => fixtures.Select(fixture => fixture.Id).ToArray();
}
