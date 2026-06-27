using ILInspector.Decompiler;
using ILInspector.Decompiler.Pipeline;
using ILInspector.DecompilerHarness;

namespace ILInspector.Decompiler.Tests;

/// <summary>
/// Iterator semantic boss guard for the decompiler product quality ladder (#1599
/// row 3). It locks a scoped matrix of common iterator state-machine shapes and
/// explicit honest-degrade frontiers:
/// <list type="bullet">
/// <item>linear, counting-loop, conditional, multi-yield loop, nested-loop,
/// foreach-delegation, IEnumerator-returning, and empty iterators render as
/// recognizable <c>yield</c> C# at <c>Full</c> fidelity;</item>
/// <item>side effects before <c>yield break</c> survive rather than being collapsed
/// into a bare break;</item>
/// <item>captured-parameter side effects and user <c>finally</c> cleanup remain
/// owned residuals, not fake <c>Full</c> source;</item>
/// <item>non-generic iterator interfaces, iterator accessors, custom
/// <c>GetEnumerator</c>, generic iterator methods, struct-instance capture,
/// closure/cleanup shapes, and <c>using</c>-disposal frontiers are either
/// recovered or explicitly owned as residuals;</item>
/// <item><c>--validity-check</c> sees no malformed or semantic-defective
/// <c>Full</c> output for the fixture.</item>
/// </list>
/// Compiler-rejected forms such as <c>yield</c> in lambdas, <c>catch</c>,
/// <c>finally</c>, or <c>try</c>-with-<c>catch</c>, by-ref iterator parameters,
/// and C# 13 ref/unsafe iterator relaxations are not decompilable row-3 source
/// shapes. They are tracked as the restriction frontier rather than synthesized
/// into impossible fixtures.
/// </summary>
public class LadderIteratorGateTests
{
    static string FixturePath => typeof(LadderIterator.IteratorSamples).Assembly.Location;
    static readonly string SamplesType = typeof(LadderIterator.IteratorSamples).FullName!;
    static readonly string PointType = typeof(LadderIterator.IteratorPoint).FullName!;
    static readonly HashSet<string> FixtureTypes = [SamplesType, PointType];

    static readonly string[] ExpectedMembers =
    [
        "IteratorPoint::GetEnumerator",
        "IteratorPoint::InstanceSequence",
        "IteratorSamples::CapturedParameterSideEffectThenBreak",
        "IteratorSamples::CapturedYieldExpression",
        "IteratorSamples::Conditional",
        "IteratorSamples::Counting",
        "IteratorSamples::Empty",
        "IteratorSamples::EnumeratorReturn",
        "IteratorSamples::ForeachDelegation",
        "IteratorSamples::Generic",
        "IteratorSamples::LambdaClosure",
        "IteratorSamples::Linear",
        "IteratorSamples::MultiYieldLoop",
        "IteratorSamples::NestedLoops",
        "IteratorSamples::NonGenericEnumerable",
        "IteratorSamples::NonGenericEnumerator",
        "IteratorSamples::ReferenceCleanup",
        "IteratorSamples::SideEffectThenBreak",
        "IteratorSamples::UserFinally",
        "IteratorSamples::UsingStatement",
        "IteratorSamples::get_Accessor",
    ];

    static readonly string[] FullRecoveredMembers =
    [
        "IteratorSamples::Conditional",
        "IteratorSamples::Counting",
        "IteratorSamples::Empty",
        "IteratorSamples::EnumeratorReturn",
        "IteratorSamples::ForeachDelegation",
        "IteratorSamples::get_Accessor",
        "IteratorSamples::LambdaClosure",
        "IteratorSamples::Linear",
        "IteratorSamples::MultiYieldLoop",
        "IteratorSamples::NestedLoops",
        "IteratorSamples::NonGenericEnumerable",
        "IteratorSamples::NonGenericEnumerator",
        "IteratorSamples::ReferenceCleanup",
        "IteratorSamples::SideEffectThenBreak",
    ];

    static readonly string[] HonestResidualMembers =
    [
        "IteratorPoint::GetEnumerator",
        "IteratorPoint::InstanceSequence",
        "IteratorSamples::CapturedParameterSideEffectThenBreak",
        "IteratorSamples::CapturedYieldExpression",
        "IteratorSamples::Generic",
        "IteratorSamples::UserFinally",
        "IteratorSamples::UsingStatement",
    ];

    [Fact]
    public void IteratorFixture_ExposesExactMemberSet_WithRecoveredAndOwnedResidualRows()
    {
        var members = LoadRaisedMembers();

        Assert.Equal(
            ExpectedMembers,
            members.Select(m => m.Key).Order(StringComparer.Ordinal).ToArray());

        var notFull = members
            .Where(m => FullRecoveredMembers.Contains(m.Key, StringComparer.Ordinal)
                && m.Function.Fidelity != DecompilationFidelity.Full)
            .Select(m => m.Key)
            .Order(StringComparer.Ordinal)
            .ToArray();
        Assert.True(notFull.Length == 0,
            "Iterator rung recovered rows must render Full; not Full: " + string.Join(", ", notFull));

        var unexpectedlyFullResiduals = members
            .Where(m => HonestResidualMembers.Contains(m.Key, StringComparer.Ordinal)
                && m.Function.Fidelity == DecompilationFidelity.Full)
            .Select(m => m.Key)
            .Order(StringComparer.Ordinal)
            .ToArray();
        Assert.True(unexpectedlyFullResiduals.Length == 0,
            "Iterator rung residual rows must stay honest until reconstructed: " + string.Join(", ", unexpectedlyFullResiduals));

        var gaps = members
            .Where(m => FullRecoveredMembers.Contains(m.Key, StringComparer.Ordinal)
                && Completeness.Residual(m.Function) is not null)
            .Select(m => $"{m.Key}: {Completeness.Residual(m.Function)}")
            .Order(StringComparer.Ordinal)
            .ToArray();
        Assert.True(gaps.Length == 0,
            "Iterator rung recovered rows must be fully raised (zero residual gaps); gaps: " + string.Join("; ", gaps));

        foreach (var member in members.Where(m => HonestResidualMembers.Contains(m.Key, StringComparer.Ordinal)))
        {
            var marker = Assert.Single(member.Function.Descendants.OfType<UnsupportedNode>(), node => node.Opcode == "iterator");
            Assert.Contains("not reconstructed", marker.Reason);
        }
    }

    [Fact]
    public void IteratorFixture_RendersRecognizableRecoveredYields()
    {
        var members = LoadRaisedMembers();
        string Body(string key) => members.Single(m => m.Key == key).Body.Trim();

        Assert.Contains("yield return 31;", Body("IteratorSamples::get_Accessor"));
        Assert.Contains("yield return 32;", Body("IteratorSamples::get_Accessor"));

        Assert.Contains("yield return 1;", Body("IteratorSamples::Linear"));
        Assert.Contains("yield return 2;", Body("IteratorSamples::Linear"));

        var counting = Body("IteratorSamples::Counting");
        Assert.Contains("int i = 0;", counting);
        Assert.Contains("while (i < n)", counting);
        Assert.Contains("yield return i;", counting);

        var conditional = Body("IteratorSamples::Conditional");
        Assert.Contains("if (flag)", conditional);
        Assert.Contains("yield return 1;", conditional);
        Assert.Contains("yield return 2;", conditional);

        var multi = Body("IteratorSamples::MultiYieldLoop");
        Assert.Contains("while (i < n)", multi);
        Assert.Contains("yield return i;", multi);
        Assert.Contains("yield return -i;", multi);

        var nested = Body("IteratorSamples::NestedLoops");
        Assert.Contains("for (int i = 0; i < 2; i++)", nested);
        Assert.Contains("for (int j = 0; j < 2; j++)", nested);
        Assert.Contains("yield return i + j;", nested);

        var foreachDelegation = Body("IteratorSamples::ForeachDelegation");
        Assert.Contains("foreach (", foreachDelegation);
        Assert.Contains("in source)", foreachDelegation);
        Assert.Contains("yield return", foreachDelegation);
        Assert.DoesNotContain("GetEnumerator", foreachDelegation);
        Assert.DoesNotContain("Finally", foreachDelegation);

        Assert.Contains("yield return 7;", Body("IteratorSamples::EnumeratorReturn"));
        Assert.Contains("yield return 8;", Body("IteratorSamples::EnumeratorReturn"));
        Assert.Contains("yield return \"alpha\";", Body("IteratorSamples::NonGenericEnumerable"));
        Assert.Contains("yield return \"beta\";", Body("IteratorSamples::NonGenericEnumerable"));
        Assert.Contains("yield return \"gamma\";", Body("IteratorSamples::NonGenericEnumerator"));
        Assert.Contains("yield return \"delta\";", Body("IteratorSamples::NonGenericEnumerator"));
        Assert.Contains("yield return ((Func<int>)(() => value + 1)).Invoke();", Body("IteratorSamples::LambdaClosure"));
        Assert.Contains("yield return \"abc\".Length;", Body("IteratorSamples::ReferenceCleanup"));
        Assert.Contains("yield break;", Body("IteratorSamples::Empty"));
    }

    [Fact]
    public void IteratorFixture_PreservesSideEffectsOrDegradesHonestly()
    {
        var members = LoadRaisedMembers();
        string Body(string key) => members.Single(m => m.Key == key).Body.Trim();

        var sideEffectBreak = Body("IteratorSamples::SideEffectThenBreak");
        Assert.Contains("Console.WriteLine(\"side effect\");", sideEffectBreak);
        Assert.Contains("yield break;", sideEffectBreak);

        foreach (var key in HonestResidualMembers)
        {
            var member = members.Single(m => m.Key == key);
            Assert.NotEqual(DecompilationFidelity.Full, member.Function.Fidelity);
            Assert.Contains("iterator", member.Body);
            Assert.Contains("not reconstructed", member.Body);
        }
    }

    [Fact]
    public void IteratorFixture_HasNoMalformedOrSemanticDefectiveFullOutput()
    {
        var results = ValidityCheck.Evaluate(FixturePath, importSiblingBodies: true)
            .Where(r => FixtureTypes.Contains(r.TypeName))
            .ToList();

        Assert.NotEmpty(results);

        var malformedFull = results
            .Where(r => r.IsFull && r.IsMalformed)
            .Select(r => $"{Simple(r.TypeName)}::{r.MethodName}: {r.MalformedDiagnostics[0].Id} {r.MalformedDiagnostics[0].Message}")
            .Order(StringComparer.Ordinal)
            .ToArray();
        Assert.True(malformedFull.Length == 0,
            "Iterator rung requires zero malformed Full methods; malformed: " + string.Join("; ", malformedFull));

        var semanticFull = results
            .Where(r => r.IsFull && r.HasSemanticDefect)
            .Select(r => $"{Simple(r.TypeName)}::{r.MethodName}: {r.SemanticDiagnostics[0].Id} {r.SemanticDiagnostics[0].Message}")
            .Order(StringComparer.Ordinal)
            .ToArray();
        Assert.True(semanticFull.Length == 0,
            "Iterator rung requires zero non-noise semantic defects on Full methods; defects: " + string.Join("; ", semanticFull));

        Assert.All(
            results.Where(r => r.IsFull),
            r => Assert.True(r.SemanticChecked, $"Full method {Simple(r.TypeName)}::{r.MethodName} was not semantically bound."));
    }

    [Fact]
    [Trait("Speed", "Slow")]
    public void IteratorFixture_RecoveredRowsCompileBackOpcodeExact()
    {
        var results = FidelityCheck.Evaluate(FixturePath)
            .Where(r => FixtureTypes.Contains(r.Type))
            .ToList();

        Assert.NotEmpty(results);

        foreach (var name in FullRecoveredMembers)
        {
            var result = Assert.Single(results, r => $"{Simple(r.Type)}::{r.Method}" == name);
            Assert.Equal(FidelityCheck.CompileBackStatus.Exact, result.Status);
        }

        var diffs = results
            .Where(r => r.Status == FidelityCheck.CompileBackStatus.OpcodeDiff)
            .Select(r => $"{Simple(r.Type)}::{r.Method}")
            .Order(StringComparer.Ordinal)
            .ToArray();
        Assert.True(diffs.Length == 0,
            "Iterator rung targeted fidelity requires zero opcode diffs; diffs: " + string.Join(", ", diffs));
    }

    static List<(string Key, IrFunction Function, string Body)> LoadRaisedMembers()
    {
        var members = new List<(string Key, IrFunction Function, string Body)>();
        using var source = MetadataSource.Open(FixturePath);
        foreach (var (typeName, methodName, function) in IrImporter.ImportAssembly(source))
        {
            if (!FixtureTypes.Contains(typeName) || methodName is ".ctor")
                continue;

            var result = CSharpPrinter.PrintRaised(function, method => IrImporter.Import(source, method));
            Assert.True(result.Succeeded, $"{methodName}: {string.Join(", ", result.Diagnostics.Select(d => d.Message))}");
            members.Add(($"{Simple(typeName)}::{methodName}", function, result.Output ?? ""));
        }
        return members;
    }

    static string Simple(string typeName)
    {
        var dot = typeName.LastIndexOf('.');
        return dot >= 0 ? typeName[(dot + 1)..] : typeName;
    }
}
