using ILInspector.Decompiler;
using ILInspector.Decompiler.Pipeline;
using ILInspector.DecompilerHarness;

namespace ILInspector.Decompiler.Tests;

/// <summary>
/// The rung 9 guard for the decompiler product quality ladder (#1599): dynamic
/// and expression-tree honesty. It does not claim dynamic or expression-tree
/// source-syntax recovery. It locks the current safe boundary: dynamic call-site
/// scaffolding and captured expression trees degrade honestly, while simple
/// expression-tree builders render as explicit <c>Expression.*</c> calls rather
/// than unsupported fake source lambdas.
/// </summary>
public class LadderRung9GateTests
{
    static string FixturePath => typeof(LadderRung9.DynamicAndExpressionTrees).Assembly.Location;
    static readonly string FixtureType = typeof(LadderRung9.DynamicAndExpressionTrees).FullName!;

    static readonly string[] ExpectedMembers =
    [
        ".ctor",
        "CapturedExpressionTree",
        "DynamicAdd",
        "DynamicGetLength",
        "DynamicInvoke",
        "DynamicInvokeMember",
        "SimpleExpressionTree",
    ];

    [Fact]
    public void Rung9Fixture_ExposesExactMemberSet()
    {
        var members = LoadRaisedMembers();

        Assert.Equal(
            ExpectedMembers,
            members.Select(m => m.Name).Order(StringComparer.Ordinal).ToArray());
    }

    [Fact]
    public void Rung9Fixture_HasNoInvalidFull()
    {
        var results = ValidityCheck.Evaluate(FixturePath, importSiblingBodies: true)
            .Where(r => r.TypeName == FixtureType)
            .ToList();

        Assert.NotEmpty(results);

        var malformedFull = results
            .Where(r => r.IsFull && r.IsMalformed)
            .Select(r => $"{r.MethodName}: {r.MalformedDiagnostics[0].Id} {r.MalformedDiagnostics[0].Message}")
            .Order(StringComparer.Ordinal)
            .ToArray();
        Assert.True(malformedFull.Length == 0,
            "Rung 9 requires no invalid Full; malformed Full: " + string.Join("; ", malformedFull));

        var semanticFull = results
            .Where(r => r.IsFull && r.HasSemanticDefect)
            .Select(r => $"{r.MethodName}: {r.SemanticDiagnostics[0].Id} {r.SemanticDiagnostics[0].Message}")
            .Order(StringComparer.Ordinal)
            .ToArray();
        Assert.True(semanticFull.Length == 0,
            "Rung 9 requires zero non-noise semantic defects on Full methods; defects: " + string.Join("; ", semanticFull));

        Assert.All(
            results.Where(r => r.IsFull),
            r => Assert.True(r.SemanticChecked, $"Full method {r.MethodName} was not semantically bound."));
    }

    [Fact]
    public void Rung9Fixture_DegradesDynamicCallSitesHonestly()
    {
        var members = LoadRaisedMembers();

        AssertDynamicPartial(members, "DynamicAdd", "Binder.BinaryOperation", "CallSite<Func<CallSite, object, object, object>>");
        AssertDynamicPartial(members, "DynamicGetLength", "Binder.GetMember", "CallSite<Func<CallSite, object, object>>");
        AssertDynamicPartial(members, "DynamicInvoke", "Binder.Invoke", "CallSite<Func<CallSite, object, int, object>>");
        AssertDynamicPartial(members, "DynamicInvokeMember", "Binder.InvokeMember", "CallSite<Func<CallSite, object, int, int, object>>");
    }

    [Fact]
    public void Rung9Fixture_RendersExpressionTreesWithoutFakeSourceLambdas()
    {
        var members = LoadRaisedMembers();

        var simple = members.Single(m => m.Name == "SimpleExpressionTree");
        Assert.Equal(DecompilationFidelity.Full, simple.Function.Fidelity);
        Assert.Contains("Expression.Parameter(typeof(int), \"x\")", simple.Body);
        Assert.Contains("Expression.Add(", simple.Body);
        Assert.Contains("Expression.Lambda<Func<int, int>>", simple.Body);
        Assert.DoesNotContain("=>", simple.Body);

        var captured = members.Single(m => m.Name == "CapturedExpressionTree");
        Assert.Equal(DecompilationFidelity.Partial, captured.Function.Fidelity);
        Assert.Contains("Expression.GreaterThan(", captured.Body);
        Assert.Contains("FieldInfo.GetFieldFromHandle", captured.Body);
        Assert.Contains("LoadToken Field", captured.Body);
        Assert.DoesNotContain("=>", captured.Body);
    }

    static void AssertDynamicPartial(
        List<(string Name, IrFunction Function, DecompilerResult Result, string Body)> members,
        string name,
        string expectedBinder,
        string expectedCallSite)
    {
        var member = members.Single(m => m.Name == name);
        Assert.Equal(DecompilationFidelity.Partial, member.Function.Fidelity);
        Assert.Contains(expectedBinder, member.Body);
        Assert.Contains(expectedCallSite, member.Body);
        Assert.DoesNotContain("dynamic", member.Body);
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
