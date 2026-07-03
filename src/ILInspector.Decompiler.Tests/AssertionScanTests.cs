using System.Text.Json;

using ILInspector.Decompiler;
using ILInspector.Decompiler.Pipeline;
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
