using ILInspector.Decompiler;
using ILInspector.Decompiler.Pipeline;
using ILInspector.DecompilerHarness;

namespace ILInspector.Decompiler.Tests;

/// <summary>
/// The rung 4 guard for the decompiler product quality ladder (#1599): C# 7
/// local syntax. This is a scoped capability gate, not a broad C# 7 claim. It
/// pins tuples, deconstruction, local functions, simple patterns, and ref
/// locals/returns so they keep rendering as valid or honestly degraded output in
/// the fast PR gate.
/// </summary>
public class LadderRung4GateTests
{
    static string FixturePath => typeof(LadderRung4.CSharp7LocalSyntax).Assembly.Location;
    static readonly string FixtureType = typeof(LadderRung4.CSharp7LocalSyntax).FullName!;

    static readonly string[] ExpectedMembers =
    [
        "<LocalFunctionCapturing>g__AddSeed|4_0",
        "<LocalFunctionCapturingWithLocal>g__AddSquare|5_0",
        "<LocalFunctionNonCapturing>g__Double|3_0",
        "DeconstructPoint",
        "LocalFunctionCapturing",
        "LocalFunctionCapturingWithLocal",
        "LocalFunctionNonCapturing",
        "RefLocalUpdate",
        "SelectRef",
        "SimplePattern",
        "TupleReturn",
        "TupleUse",
    ];

    [Fact]
    public void Rung4Fixture_ExposesExactMemberSet_AndHasNoResidualGaps()
    {
        var members = LoadRaisedMembers();

        Assert.Equal(
            ExpectedMembers,
            members.Select(m => m.Name).Order(StringComparer.Ordinal).ToArray());

        var failed = members
            .Where(m => !m.Result.Succeeded)
            .Select(m => $"{m.Name}: {string.Join(", ", m.Result.Diagnostics.Select(d => d.Message))}")
            .Order(StringComparer.Ordinal)
            .ToArray();
        Assert.True(failed.Length == 0,
            "Rung 4 product printing must not fail; failures: " + string.Join("; ", failed));

        var gaps = members
            .Where(m => Completeness.Residual(m.Function) is not null)
            .Select(m => $"{m.Name}: {Completeness.Residual(m.Function)}")
            .Order(StringComparer.Ordinal)
            .ToArray();
        Assert.True(gaps.Length == 0,
            "Rung 4 fixture members must be fully structured (zero residual gaps); gaps: " + string.Join("; ", gaps));
    }

    [Fact]
    public void Rung4Fixture_RendersRecognizableCSharp7LocalSyntax()
    {
        var members = LoadRaisedMembers();
        string Body(string name) => members.Single(m => m.Name == name).Body.Trim();
        DecompilationFidelity Fidelity(string name) => members.Single(m => m.Name == name).Function.Fidelity;

        Assert.Equal("return (left + right, left * right);", Body("TupleReturn"));
        Assert.Equal("return pair.Item1 + pair.Item2;", Body("TupleUse"));

        var deconstruct = Body("DeconstructPoint");
        Assert.Contains("(int ", deconstruct);
        Assert.Contains(") = point;", deconstruct);
        Assert.Contains("return ", deconstruct);

        var staticLocal = Body("LocalFunctionNonCapturing");
        Assert.Contains("return Double(value);", staticLocal);
        Assert.Contains("static int Double(int item) => item * 2;", staticLocal);
        Assert.DoesNotContain("g__", staticLocal);

        var capturingLocal = Body("LocalFunctionCapturing");
        Assert.Contains("return AddSeed(3);", capturingLocal);
        Assert.Contains("int AddSeed(int item) => item + value;", capturingLocal);
        Assert.DoesNotContain("DisplayClass", capturingLocal);

        var localBodyCapture = Body("LocalFunctionCapturingWithLocal");
        Assert.Equal(DecompilationFidelity.Partial, Fidelity("LocalFunctionCapturingWithLocal"));
        Assert.Contains("DisplayClass", localBodyCapture);
        Assert.Contains("AddSquare(3", localBodyCapture);

        var pattern = Body("SimplePattern");
        Assert.Contains("value is int", pattern);
        Assert.Contains("value is string text", pattern);
        Assert.Contains("return text.Length;", pattern);

        var refLocal = Body("RefLocalUpdate");
        Assert.Contains("ref int ", refLocal);
        Assert.Contains(" = ref CSharp7LocalSyntax.SelectRef(useLeft, ref left, ref right);", refLocal);
        Assert.Contains("+ 10;", refLocal);

        var refReturn = Body("SelectRef");
        Assert.Contains("return ref left;", refReturn);
        Assert.Contains("return ref right;", refReturn);
    }

    [Fact]
    public void Rung4Fixture_HasNoMalformedOrSemanticDefectiveFullOutput()
    {
        var results = ValidityCheck.Evaluate(FixturePath)
            .Where(r => r.TypeName == FixtureType)
            .ToList();

        Assert.NotEmpty(results);

        var malformedFull = results
            .Where(r => r.IsFull && r.IsMalformed)
            .Select(r => $"{r.MethodName}: {r.MalformedDiagnostics[0].Id} {r.MalformedDiagnostics[0].Message}")
            .Order(StringComparer.Ordinal)
            .ToArray();
        Assert.True(malformedFull.Length == 0,
            "Rung 4 requires zero malformed Full methods; malformed: " + string.Join("; ", malformedFull));

        var semanticFull = results
            .Where(r => r.IsFull && r.HasSemanticDefect)
            .Select(r => $"{r.MethodName}: {r.SemanticDiagnostics[0].Id} {r.SemanticDiagnostics[0].Message}")
            .Order(StringComparer.Ordinal)
            .ToArray();
        Assert.True(semanticFull.Length == 0,
            "Rung 4 requires zero non-noise semantic defects on Full methods; defects: " + string.Join("; ", semanticFull));

        Assert.All(
            results.Where(r => r.IsFull),
            r => Assert.True(r.SemanticChecked, $"Full method {r.MethodName} was not semantically bound."));
    }

    static List<(string Name, IrFunction Function, DecompilerResult Result, string Body)> LoadRaisedMembers()
    {
        var members = new List<(string Name, IrFunction Function, DecompilerResult Result, string Body)>();
        using var source = MetadataSource.Open(FixturePath);
        foreach (var (typeName, methodName, function) in IrImporter.ImportAssembly(source))
        {
            if (typeName != FixtureType)
                continue;

            var result = CSharpPrinter.PrintRaised(function, method => IrImporter.Import(source, method));
            members.Add((methodName, function, result, result.Output ?? ""));
        }
        return members;
    }
}
