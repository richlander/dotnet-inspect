using System.Text.Json;

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
