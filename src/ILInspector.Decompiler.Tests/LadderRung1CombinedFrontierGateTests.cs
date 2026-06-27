using ILInspector.Decompiler;
using ILInspector.Decompiler.Pipeline;
using ILInspector.DecompilerHarness;

namespace ILInspector.Decompiler.Tests;

/// <summary>
/// The rung 1 <em>combined-code frontier</em> guard for the decompiler product
/// quality ladder (#1599 / #1710). The other rung 1 gates exercise each construct
/// in isolation in a tiny method, and all raise to <c>Full</c>. But realistic
/// code combines them, and the structuring pass is fragile to those combinations:
/// a method mixing a <c>foreach</c> loop, a <c>switch</c>, and a LINQ/lambda can
/// tip the whole method into a goto-ladder fallback, which also defeats lambda
/// raising (the delegate target leaks a <c>&lt;&gt;c</c> name). The
/// <see cref="LadderRung1.CombinedFrontier{T}"/> fixture captures that.
///
/// <para>This gate owns the frontier honestly. It pins that the combined methods
/// degrade to <c>Partial</c> (never a lying <c>Full</c>) while the simple members
/// stay <c>Full</c>, and that the user type has <b>zero malformed <c>Full</c></b>
/// output — the degradation is honest, not invalid C# claimed as good. It is the
/// inverse of a positive guard: when the structuring pass is improved to raise
/// these shapes, the <c>Partial</c> assertions flip and force an intentional
/// promotion to a recognizable-output guard. Until then, rung 1 is NOT complete
/// for realistic combined code — this is the blocking row.</para>
/// </summary>
public class LadderRung1CombinedFrontierGateTests
{
    static string FixturePath => typeof(LadderRung1.CombinedFrontier<>).Assembly.Location;
    static readonly string FixtureType = typeof(LadderRung1.CombinedFrontier<>).FullName!;

    // Methods that currently degrade — the frontier. Locked so a structuring
    // improvement that raises them flips this gate and forces a promotion.
    static readonly string[] FrontierMembers = ["Summarize", "SwitchThenLinq"];

    // Simple members that are unaffected by the combination and stay Full.
    static readonly string[] RaisedMembers = [".ctor", "get_Count"];

    [Fact]
    public void Rung1CombinedFrontier_DegradesHonestly_FrontierPartial_SimpleFull()
    {
        var members = LoadProductPathMembers();

        Assert.Equal(
            RaisedMembers.Concat(FrontierMembers).Order(StringComparer.Ordinal).ToArray(),
            members.Select(m => m.Name).Order(StringComparer.Ordinal).ToArray());

        // The frontier methods degrade — honestly classified Partial, with a
        // residual — they are NOT recognizable C#. When structuring is improved
        // to raise them, these assertions flip and force an intentional promotion.
        foreach (var name in FrontierMembers)
        {
            var m = members.Single(x => x.Name == name);
            Assert.Equal(DecompilationFidelity.Partial, m.Function.Fidelity);
            Assert.NotNull(Completeness.Residual(m.Function));
        }

        // The simple members are unaffected by the combination.
        foreach (var name in RaisedMembers)
        {
            var m = members.Single(x => x.Name == name);
            Assert.Equal(DecompilationFidelity.Full, m.Function.Fidelity);
            Assert.Null(Completeness.Residual(m.Function));
        }
    }

    [Fact]
    public void Rung1CombinedFrontier_DegradationIsHonest_NoMalformedFull()
    {
        var results = ValidityCheck.Evaluate(FixturePath)
            .Where(r => r.TypeName == FixtureType)
            .ToList();

        Assert.NotEmpty(results);

        // The core honesty invariant: the decompiler never claims Full on output
        // that does not parse. The combined frontier degrades to Partial instead
        // of leaking its goto/`<>c` rendering under a Full claim.
        var malformedFull = results
            .Where(r => r.IsFull && r.IsMalformed)
            .Select(r => $"{r.MethodName}: {r.MalformedDiagnostics[0].Id} {r.MalformedDiagnostics[0].Message}")
            .Order(StringComparer.Ordinal)
            .ToArray();
        Assert.True(malformedFull.Length == 0,
            "Rung 1 combined frontier must degrade honestly (zero malformed Full); malformed Full: "
                + string.Join("; ", malformedFull));

        // Non-vacuous: the frontier methods must actually be present and at least
        // one must be malformed-but-Partial, proving this gate is measuring the
        // real degradation and not an empty/relabeled result.
        var partialMalformed = results
            .Where(r => !r.IsFull && r.IsMalformed)
            .Select(r => r.MethodName)
            .ToArray();
        Assert.Contains("Summarize", partialMalformed);
    }

    static List<(string Name, IrFunction Function)> LoadProductPathMembers()
    {
        var members = new List<(string Name, IrFunction Function)>();
        using var source = MetadataSource.Open(FixturePath);
        foreach (var (typeName, methodName, function) in IrImporter.ImportAssembly(source))
        {
            if (typeName != FixtureType)
                continue;
            // The structuring degradation is locator-independent (it is the loop /
            // switch raising that fails, not cross-assembly resolution), so the
            // proven bare-seam pass run establishes the frontier fidelity.
            IrPasses.Run(function);
            members.Add((methodName, function));
        }
        return members;
    }
}
