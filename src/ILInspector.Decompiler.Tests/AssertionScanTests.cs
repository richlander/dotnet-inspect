using System.Text.Json;

using ILInspector.Decompiler;
using ILInspector.Decompiler.Pipeline;
using ILInspector.Decompiler.Tests.InverseArchitecture;
using ILInspector.DecompilerHarness;

namespace ILInspector.Decompiler.Tests;

public class AssertionScanTests
{
    static readonly TypeRef Enum32 = TypeRef.Definition("synthetic", "", "E32");
    static readonly TypeRef Int32 = TypeRef.CoreLib("System", "Int32");

    [Fact]
    public void EvaluateFunction_ReportsFirstAssertionViolationAndCoverage()
    {
        var function = Function(
            Returning(new LoadArgument(0, "raw", Int32)),
            Enum32,
            new Parameter("raw", Int32));

        var result = AssertionScan.EvaluateFunction(
            "synthetic",
            "synthetic.dll",
            "Samples.Holder",
            "EnumReturn",
            overload: 0,
            function);

        Assert.Null(result.PassBug);
        var violation = Assert.Single(result.Violations);
        Assert.Equal(IrPasses.ImportStageName, violation.Pass);
        Assert.Equal("SinkDistinguishableFromStack", violation.Predicate);
        Assert.Equal(nameof(LoadArgument), violation.Node);
        Assert.Equal("E32", violation.SinkType);
        Assert.Contains("without a Coerce", violation.Message);

        Assert.Contains(nameof(LoadArgument), result.CoveredNodes);
        Assert.Contains(nameof(Coerce), result.CoveredNodes);
    }

    [Fact]
    public void Snapshot_IncludesCleanMethodsSoDiffCanSeeImprovements()
    {
        var violating = AssertionScan.EvaluateFunction(
            "synthetic",
            "synthetic.dll",
            "Samples.Holder",
            "EnumReturn",
            overload: 0,
            Function(Returning(new LoadArgument(0, "raw", Int32)), Enum32, new Parameter("raw", Int32)));
        var clean = AssertionScan.EvaluateFunction(
            "synthetic",
            "synthetic.dll",
            "Samples.Holder",
            "CleanEnumReturn",
            overload: 0,
            Function(Returning(new LoadArgument(0, "e", Enum32)), Enum32, new Parameter("e", Enum32)));
        var scan = new AssertionScan.Result(
            [violating, clean],
            new Dictionary<string, int>(StringComparer.Ordinal),
            []);

        var snapshot = AssertionScan.AssertionViolationSnapshot.FromResult(scan);

        Assert.Equal(2, snapshot.Methods.Count);
        Assert.Contains(snapshot.Methods, m => m.Key == clean.Key && m.Violations.Count == 0);

        var json = JsonSerializer.Serialize(snapshot);
        var roundTrip = JsonSerializer.Deserialize<AssertionScan.AssertionViolationSnapshot>(json);
        Assert.NotNull(roundTrip);
        Assert.Equal(snapshot.Methods.Count, roundTrip!.Methods.Count);
    }

    [Fact]
    public void Snapshot_PreservesPassBugsSoDiffDoesNotCountTruncationAsImprovement()
    {
        var crashed = new AssertionScan.MethodResult(
            Assembly: "synthetic",
            AssemblyPath: "synthetic.dll",
            Type: "Samples.Holder",
            Method: "Crashes",
            Overload: 0,
            Signature: "() -> corelib:System.Void",
            Key: "synthetic.dll!Samples.Holder::Crashes() -> corelib:System.Void",
            Violations: [],
            CoveredNodes: [],
            PassBug: "InvalidOperationException: boom");
        var scan = new AssertionScan.Result(
            [crashed],
            new Dictionary<string, int>(StringComparer.Ordinal),
            []);

        var snapshot = AssertionScan.AssertionViolationSnapshot.FromResult(scan);

        var method = Assert.Single(snapshot.Methods);
        Assert.Equal("InvalidOperationException: boom", method.PassBug);
    }

    [Fact]
    public void EvaluateFunction_TreatsImporterCrashDiagnosticAsPassBug()
    {
        var function = Function(Returning(new Constant(0, Int32)), Int32);
        function.Diagnostics.Add(new DecompilerDiagnostic(
            DiagnosticIds.InternalError,
            "importer crash: InvalidOperationException: boom"));

        var result = AssertionScan.EvaluateFunction(
            "synthetic",
            "synthetic.dll",
            "Samples.Holder",
            "ImportCrash",
            overload: 0,
            function);

        Assert.Equal("DEC0001: importer crash: InvalidOperationException: boom", result.PassBug);
        Assert.Empty(result.Violations);
        Assert.Empty(result.CoveredNodes);
    }

    [Fact]
    public void Snapshot_PreservesDuplicateViolationOrdinals()
    {
        var duplicateA = new AssertionScan.ViolationSite(
            Method: "synthetic.dll!Samples.Holder::M() -> corelib:System.Void",
            Pass: IrPasses.ImportStageName,
            Predicate: "SinkDistinguishableFromStack",
            Node: nameof(LoadArgument),
            SinkType: "bool",
            Message: "M: LoadArgument raw occupies a bool sink without a Coerce",
            Ordinal: 0);
        var duplicateB = duplicateA with { Ordinal = 1 };
        var result = new AssertionScan.MethodResult(
            Assembly: "synthetic",
            AssemblyPath: "synthetic.dll",
            Type: "Samples.Holder",
            Method: "M",
            Overload: 0,
            Signature: "() -> corelib:System.Void",
            Key: duplicateA.Method,
            Violations: [duplicateA, duplicateB],
            CoveredNodes: [],
            PassBug: null);
        var scan = new AssertionScan.Result(
            [result],
            new Dictionary<string, int>(StringComparer.Ordinal),
            []);

        var snapshot = AssertionScan.AssertionViolationSnapshot.FromResult(scan);
        var method = Assert.Single(snapshot.Methods);

        Assert.Equal(2, method.Violations.Count);
        Assert.Equal(2, method.ViolationIdentities().Count);
        Assert.Contains("#0", method.Violations[0].Identity);
        Assert.Contains("#1", method.Violations[1].Identity);
    }

    [Fact]
    public void Snapshot_DiffIdentityIgnoresFirstFailingPass()
    {
        var importViolation = new AssertionScan.AssertionViolationRecord(
            Pass: IrPasses.ImportStageName,
            Predicate: "SinkDistinguishableFromStack",
            Node: nameof(LoadArgument),
            SinkType: "bool",
            Message: "M: LoadArgument raw occupies a bool sink without a Coerce",
            Ordinal: 0);
        var laterViolation = importViolation with { Pass = "some-later-pass" };

        Assert.Equal(importViolation.Identity, laterViolation.Identity);
    }

    [Fact]
    public void SinkType_ParsesRealCoercionInvariantMessage()
    {
        var function = Function(
            Returning(new LoadArgument(0, "raw", Int32)),
            Enum32,
            new Parameter("raw", Int32));

        var violation = Assert.Single(CoercionInvariant.Check(function));

        Assert.Equal("E32", AssertionScan.SinkType(violation.Message));
    }

    [Fact]
    public void InverseLedger_ClassifiesPassRaisedNodesSeparatelyFromImporterNodes()
    {
        Assert.Equal(InverseLedger.NodeCause.PassRaised, InverseLedger.CauseFor(nameof(Lambda)));
        Assert.Equal(InverseLedger.NodeCause.PassRaised, InverseLedger.CauseFor(nameof(TupleExpression)));
        Assert.Equal(InverseLedger.NodeCause.ImporterEmitted, InverseLedger.CauseFor(nameof(CallIndirect)));
        Assert.Equal(InverseLedger.NodeCause.ImporterEmitted, InverseLedger.CauseFor(nameof(Constant)));
    }

    // The nodes the IMPORTER constructs (grep `new X(` in IrImporter.cs is the
    // ground truth); every other annotated node is pass-raised. Exhaustive so a
    // newly annotated node FORCES a classification decision — the spot-check
    // form could not catch an omission in CauseFor's fallback (B2 review,
    // two rounds: first the output-shape cluster, then seven older nodes).
    static readonly HashSet<string> ImporterEmittedNodes =
    [
        "ArrayLength", "Binary", "Box", "Call", "CallIndirect", "CastClass",
        "CaughtException", "Comparison", "Constant", "Convert", "IsInstance",
        "LoadArgument", "LoadArgumentAddress", "LoadElement", "LoadElementAddress",
        "LoadField", "LoadFieldAddress", "LoadFunctionPointer", "LoadIndirect",
        "LoadLocal", "LoadLocalAddress", "LoadStackSlot", "LoadToken", "LogicalNot",
        "NewArray", "NewObject", "SizeOf", "StackAllocate", "Unary", "Unbox",
        "UnboxAny",
    ];

    [Fact]
    public void InverseLedger_ClassifiesEveryAnnotatedNode()
    {
        foreach (var row in InverseLedger.Rows(typeof(IrFunction).Assembly))
        {
            var expected = ImporterEmittedNodes.Contains(row.Node)
                ? InverseLedger.NodeCause.ImporterEmitted
                : InverseLedger.NodeCause.PassRaised;
            Assert.True(
                expected == InverseLedger.CauseFor(row.Node),
                $"{row.Node}: CauseFor says {InverseLedger.CauseFor(row.Node)}, test expects {expected}. " +
                "New node? Decide its cause: add it to InverseLedger.s_passRaisedNodes (pass-constructed) " +
                "or to this test's ImporterEmittedNodes (constructed in IrImporter.cs).");
        }
    }

    [Fact]
    public void FixtureGuarantee_HasMappingForEveryAnnotatedNode()
    {
        var annotatedNodes = InverseLedger.Rows(typeof(IrFunction).Assembly)
            .Select(row => row.Node)
            .ToArray();

        Assert.Empty(AssertionScan.NodesWithoutFixtureGuarantee(annotatedNodes));
        Assert.Empty(AssertionScan.InvalidFixtureGuaranteeIds());
    }

    [Fact]
    public void ObservedFromDeclaredFixtures_IgnoresSiblingFixtureIncidentalProduction()
    {
        // A node is scored only against the fixtures that DECLARE it. A sibling
        // fixture incidentally producing the node must not count — otherwise an
        // inert declared producer free-rides on the sibling's coverage (#2289's
        // IsPattern masking, tracked as #2293).
        var perFixture = new Dictionary<string, IReadOnlyDictionary<string, int>>(StringComparer.Ordinal)
        {
            ["assertion.inverse-node-coverage"] = new Dictionary<string, int>(StringComparer.Ordinal),
            ["assertion.union-switch"] = new Dictionary<string, int>(StringComparer.Ordinal) { ["IsPattern"] = 3 },
        };

        // Declared only against the coverage fixture, which does not produce it here,
        // so the sibling union-switch coverage is ignored and it scores 0 (a regression).
        Assert.Equal(0, AssertionScan.ObservedFromDeclaredFixtures(
            ["assertion.inverse-node-coverage"], perFixture, "IsPattern"));

        // Once the declared fixture itself produces the node, it counts.
        perFixture["assertion.inverse-node-coverage"] =
            new Dictionary<string, int>(StringComparer.Ordinal) { ["IsPattern"] = 2 };
        Assert.Equal(2, AssertionScan.ObservedFromDeclaredFixtures(
            ["assertion.inverse-node-coverage"], perFixture, "IsPattern"));

        // A node covered by a declared fixture that was never scanned scores 0.
        Assert.Equal(0, AssertionScan.ObservedFromDeclaredFixtures(
            ["assertion.missing"], perFixture, "IsPattern"));
    }

    [Fact]
    public void AssertionPrinter_MarksNonFinalStagesObligation_FinalStageUnsound()
    {
        // A LoadArgument occupying an enum sink with no Coerce is an undischarged
        // typing assertion. Across stages it should read as an informational
        // OBLIGATION until the final stage, where an undischarged survivor is the
        // real UNSOUND soundness failure. (#2269)
        var function = Function(
            Returning(new LoadArgument(0, "raw", Int32)),
            Enum32,
            new Parameter("raw", Int32));

        var printer = new AssertionPrinter.StatefulPrinter(totalStages: 3);
        var stage1 = printer.Dump(function);
        var stage2 = printer.Dump(function);
        var stageFinal = printer.Dump(function);

        Assert.Contains("OBLIGATION (informational)", stage1);
        Assert.DoesNotContain("UNSOUND", stage1);
        Assert.Contains("OBLIGATION (informational)", stage2);
        Assert.DoesNotContain("UNSOUND", stage2);

        Assert.Contains("UNSOUND (error)", stageFinal);
        Assert.Contains("FIRST UNSOUND SURVIVOR", stageFinal);
        Assert.DoesNotContain("OBLIGATION", stageFinal);
    }

    [Fact]
    public void AssertionPrinter_DefaultSingleStage_TreatsViolationAsUnsound()
    {
        var function = Function(
            Returning(new LoadArgument(0, "raw", Int32)),
            Enum32,
            new Parameter("raw", Int32));

        var dump = new AssertionPrinter.StatefulPrinter().Dump(function);

        Assert.Contains("UNSOUND (error)", dump);
        Assert.DoesNotContain("OBLIGATION", dump);
    }

    [Fact]
    public void EvaluateFunction_DischargedViolationIsNotFinalStageSurvivor()
    {
        // A LoadArgument(int) at an enum sink is flagged at import, but coercion
        // insertion wraps it in a Coerce before the final stage — a discharged
        // OBLIGATION, not a survivor. (#2269)
        var function = Function(
            Returning(new LoadArgument(0, "raw", Int32)),
            Enum32,
            new Parameter("raw", Int32));

        var result = AssertionScan.EvaluateFunction(
            "synthetic", "synthetic.dll", "Samples.Holder", "EnumReturn", overload: 0, function);

        var violation = Assert.Single(result.Violations);
        Assert.False(violation.FinalStageSurvivor);
        Assert.Contains(nameof(Coerce), result.CoveredNodes);
        // Discharged, so it has a real lifetime and a named discharging pass.
        Assert.True(violation.LifetimeStages > 0);
        Assert.NotEqual("", violation.DischargePass);
    }

    [Fact]
    public void Finalize_FlagsSurvivorsAndComputesDischargedLifetime()
    {
        var discharged = new AssertionScan.ViolationSite(
            Method: "m", Pass: IrPasses.ImportStageName, Predicate: "SinkDistinguishableFromStack",
            Node: nameof(LoadArgument), SinkType: "E32", Message: "raw occupies a E32 sink without a Coerce", Ordinal: 0);
        var survivor = new AssertionScan.ViolationSite(
            Method: "m", Pass: IrPasses.ImportStageName, Predicate: "SinkDistinguishableFromStack",
            Node: nameof(Constant), SinkType: "bool", Message: "0 occupies a bool sink without a Coerce", Ordinal: 0);

        // Stage order: import(0), pass-a(1), coercion-insertion(2), tail(3).
        var stageNames = new[] { IrPasses.ImportStageName, "pass-a", "coercion-insertion", "tail" };
        // Discharged accrued at import(0), last present at pass-a(1) -> discharged at index 2.
        var firstStage = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            [discharged.StageIdentity] = 0,
            [survivor.StageIdentity] = 0,
        };
        var lastStage = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            [discharged.StageIdentity] = 1,
            [survivor.StageIdentity] = 3,
        };
        var finalStage = new HashSet<string>(StringComparer.Ordinal) { survivor.StageIdentity };

        var marked = AssertionScan.Finalize([discharged, survivor], finalStage, stageNames, firstStage, lastStage);

        var d = marked.Single(v => v.Node == nameof(LoadArgument));
        Assert.False(d.FinalStageSurvivor);
        Assert.Equal(2, d.LifetimeStages);            // dischargeIndex(2) - accrual(0)
        Assert.Equal("coercion-insertion", d.DischargePass);

        var s = marked.Single(v => v.Node == nameof(Constant));
        Assert.True(s.FinalStageSurvivor);
        Assert.Equal(0, s.LifetimeStages);            // survivors never discharge
        Assert.Equal("", s.DischargePass);
    }

    [Fact]
    public void ViolationSite_StageIdentityIsPassIndependent()
    {
        var atImport = new AssertionScan.ViolationSite(
            Method: "m", Pass: IrPasses.ImportStageName, Predicate: "P",
            Node: nameof(LoadArgument), SinkType: "bool", Message: "msg", Ordinal: 2);
        var atLaterPass = atImport with { Pass = "some-later-pass" };

        Assert.Equal(atImport.StageIdentity, atLaterPass.StageIdentity);
        Assert.NotEqual(atImport.Identity, atLaterPass.Identity);
    }

    static IrFunction Function(BlockContainer body, TypeRef returnType, params Parameter[] parameters)
        => new(
            "M",
            TypeRef.Definition("synthetic", "", "Holder"),
            new MethodSignature(returnType, [.. parameters], HasThis: false, GenericParameterCount: 0),
            [],
            body)
        {
            TypeShapes = new Dictionary<TypeRef, TypeShape> { [Enum32] = TypeShape.Enum },
            EnumMembers = new Dictionary<TypeRef, IReadOnlyDictionary<long, string>>
            {
                [Enum32] = new Dictionary<long, string> { [1] = "One" },
            },
        };

    static BlockContainer Returning(IrExpression value)
    {
        var container = new BlockContainer();
        var block = new Block(0);
        block.Add(new Return(value));
        container.Add(block);
        return container;
    }
}
