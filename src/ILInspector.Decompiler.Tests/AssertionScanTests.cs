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
        Assert.Equal(InverseLedger.NodeCause.PassRaised, InverseLedger.CauseFor(nameof(LocalFunctionInvocation)));
        Assert.Equal(InverseLedger.NodeCause.ImporterEmitted, InverseLedger.CauseFor(nameof(CallIndirect)));
        Assert.Equal(InverseLedger.NodeCause.ImporterEmitted, InverseLedger.CauseFor(nameof(Unbox)));
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
