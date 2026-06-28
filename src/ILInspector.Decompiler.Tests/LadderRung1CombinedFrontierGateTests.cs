using ILInspector.Decompiler;
using ILInspector.Decompiler.Pipeline;
using ILInspector.DecompilerHarness;

namespace ILInspector.Decompiler.Tests;

/// <summary>
/// The rung 1 <em>combined-code recovery</em> guard for the decompiler product
/// quality ladder (#1599 / #1710). The other rung 1 gates exercise each construct
/// in isolation in a tiny method. This one exercises a realistic combination — a
/// method mixing a <c>foreach</c> loop, a <c>switch</c> that assigns a local, and
/// a LINQ/lambda continuation — which used to tip the whole method into a
/// goto-ladder fallback (issue #1710). The
/// <see cref="LadderRung1.CombinedFrontier"/> fixture captures that shape.
///
/// <para>The fold of an N-way switch-store dispatch into a nested conditional
/// store (<c>ConditionalStoreChainPass</c>) now lets these methods structure: the
/// goto-ladder <em>structuring residual is gone</em> and the body is recognizable
/// C# (the switch renders as a nested <c>?:</c> store). This gate pins that
/// recovery — zero structuring residual, the dispatch raised, no goto ladder —
/// and the standing honesty invariant that the type has zero malformed
/// <c>Full</c> output. (Through this bare seam the combined methods still report
/// <c>Partial</c> because it does not raise the cross-method lambda; the product
/// path raises it, guarded by <see cref="LadderRung1ProductPathGateTests"/>. That
/// remaining residual is the lambda, not structuring.)</para>
/// </summary>
public class LadderRung1CombinedFrontierGateTests
{
    static string FixturePath => typeof(LadderRung1.CombinedFrontier).Assembly.Location;
    static readonly string FixtureType = typeof(LadderRung1.CombinedFrontier).FullName!;

    // The combined switch-store methods that #1710 recovered: their switch
    // dispatch now folds to a nested conditional store and structures cleanly.
    static readonly string[] FrontierMembers = ["Summarize", "SwitchThenLinq"];

    // Simple members that are unaffected by the combination and stay Full. The
    // constructor is a genuinely empty default ctor (the fixture has no field
    // initializer, which the composer would otherwise drop — see #1713).
    static readonly string[] RaisedMembers = [".ctor", "Echo"];

    [Fact]
    public void Rung1CombinedSwitch_RaisesDispatch_NoStructuringResidual()
    {
        var members = LoadProductPathMembers();

        Assert.Equal(
            RaisedMembers.Concat(FrontierMembers).Order(StringComparer.Ordinal).ToArray(),
            members.Select(m => m.Name).Order(StringComparer.Ordinal).ToArray());

        // The combined switch-store methods now structure: the N-way switch
        // dispatch folds to a nested conditional store (#1710), so the
        // goto-ladder structuring residual is gone and the body is recognizable
        // C#. (They render Partial only via this bare seam, which does not raise
        // the cross-method lambda; the product path raises it — see
        // LadderRung1ProductPathGateTests. That residual is not structuring.)
        foreach (var name in FrontierMembers)
        {
            var m = members.Single(x => x.Name == name);
            Assert.Null(Completeness.Residual(m.Function));
            // Assert on the IR shape (the switch dispatch folded to a Conditional
            // store of the bucket local) rather than printer substrings, plus the
            // absence of a goto ladder in the rendered output.
            Assert.NotEmpty(m.Function.Descendants.OfType<Conditional>());
            var body = CSharpPrinter.PrintRaised(m.Function).Output ?? "";
            Assert.DoesNotContain("goto IL_", body);
        }

        // The simple members are unaffected.
        foreach (var name in RaisedMembers)
        {
            var m = members.Single(x => x.Name == name);
            Assert.Equal(DecompilationFidelity.Full, m.Function.Fidelity);
            Assert.Null(Completeness.Residual(m.Function));
        }
    }

    [Fact]
    public void Rung1CombinedSwitch_HasNoMalformedFull()
    {
        var results = ValidityCheck.Evaluate(FixturePath)
            .Where(r => r.TypeName == FixtureType)
            .ToList();

        Assert.NotEmpty(results);

        // The core honesty invariant: the decompiler never claims Full on output
        // that does not parse.
        var malformedFull = results
            .Where(r => r.IsFull && r.IsMalformed)
            .Select(r => $"{r.MethodName}: {r.MalformedDiagnostics[0].Id} {r.MalformedDiagnostics[0].Message}")
            .Order(StringComparer.Ordinal)
            .ToArray();
        Assert.True(malformedFull.Length == 0,
            "Rung 1 combined switch must stay honest (zero malformed Full); malformed Full: "
                + string.Join("; ", malformedFull));

        // Non-vacuous: the combined methods must actually be present in the
        // evaluated set, so the no-malformed-Full assertion has teeth.
        Assert.Contains("Summarize", results.Select(r => r.MethodName));
        Assert.Contains("SwitchThenLinq", results.Select(r => r.MethodName));
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
