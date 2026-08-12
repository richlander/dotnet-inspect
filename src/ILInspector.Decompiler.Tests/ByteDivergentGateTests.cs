using System.Collections.Generic;
using System.Linq;
using System.Reflection.PortableExecutable;

using ILInspector.Decompiler;
using ILInspector.Decompiler.Pipeline;
using ILInspector.Metadata;

namespace ILInspector.Decompiler.Tests;

/// <summary>
/// Exhaustive behavioral coverage for every non-default byte-divergent style
/// value (#3655). The catalog classification drives this registry: adding a
/// divergent value without a firing specimen and an executed equivalence
/// contract fails <see cref="EveryByteDivergentValue_HasABehavioralGate"/>.
/// </summary>
[Trait("Area", "RoundTrip")]
[Collection(FidelityGateCollection.Name)]
public sealed class ByteDivergentGateTests
{
    static string AssemblyPath => typeof(ByteDivergentGateTests).Assembly.Location;

    sealed record BehavioralGate(
        string KnobId,
        string ValueToken,
        System.Type DeclaringType,
        string Method,
        Action<string> AssertLensRender,
        Action AssertBehavioralEquivalence);

    static readonly IReadOnlyList<BehavioralGate> Gates =
    [
        new(
            "guarded-boolean-return-style",
            "conditional-expression",
            typeof(PreferConditionalReturnSpecimen),
            nameof(PreferConditionalReturnSpecimen.GuardBothVariable),
            text => Assert.Contains("a ? b : c", text, StringComparison.Ordinal),
            AssertConditionalReturnBehavior),
        new(
            "guarded-boolean-return-style",
            "branchless",
            typeof(PreferBranchlessBooleanSpecimen),
            nameof(PreferBranchlessBooleanSpecimen.AndTailGuard),
            text => Assert.Contains("a && b", text, StringComparison.Ordinal),
            AssertBranchlessBooleanBehavior),
        new(
            "prefer-long-literal-suffix",
            "true",
            typeof(LongLiteralFoldFixture),
            nameof(LongLiteralFoldFixture.SmallReturn),
            text => Assert.Equal("public static long SmallReturn() => 42L;", text.Trim()),
            AssertLongLiteralBehavior),
    ];

    static IReadOnlyList<(string KnobId, string ValueToken)> ByteDivergentNonDefaultValues =>
        StyleOptionCatalog.Options
            .Where(option => option.ByteDivergent)
            .SelectMany(option => option.Values
                .Where(value => value.Token != option.DefaultValue)
                .Select(value => (option.Id, value.Token)))
            .ToArray();

    [Fact]
    public void EveryByteDivergentValue_HasABehavioralGate()
    {
        var required = ByteDivergentNonDefaultValues.ToHashSet();
        var covered = Gates.Select(gate => (gate.KnobId, gate.ValueToken)).ToHashSet();

        Assert.Equal(required, covered);
    }

    [Fact]
    public void EveryBehavioralGate_ChangesItsFiringSpecimen()
    {
        foreach (var gate in Gates)
        {
            string offText = Render(gate, StyleOptionCatalog.DefaultOptions);
            string onText = Render(gate, Enable(gate));
            string label = $"{gate.KnobId}={gate.ValueToken}";

            Assert.True(
                !string.Equals(offText, onText, StringComparison.Ordinal),
                $"{label}: the registered firing specimen did not change when the value was enabled.");
            gate.AssertLensRender(onText);
        }
    }

    [Fact]
    public void EveryBehavioralGate_ExecutesItsEquivalenceContract()
    {
        foreach (var gate in Gates)
            gate.AssertBehavioralEquivalence();
    }

    static PrinterOptions Enable(BehavioralGate gate)
    {
        var knob = StyleOptionCatalog.Options.Single(option => option.Id == gate.KnobId);
        return knob.WithValue(StyleOptionCatalog.DefaultOptions, gate.ValueToken);
    }

    static string Render(BehavioralGate gate, PrinterOptions options)
    {
        using var pe = new PEReader(File.OpenRead(AssemblyPath));
        var api = ApiSurfaceExtractor.Extract(pe);
        var type = Assert.Single(api.Types, candidate => candidate.FullName == gate.DeclaringType.FullName);
        var member = Assert.Single(type.Members, candidate => candidate.Name == gate.Method);
        var rendered = MemberBodyProducer.ProduceMember(
            type,
            member,
            AssemblyPath,
            pdbPath: null,
            printerOptions: options);

        Assert.Equal(MemberBodyProductionStatus.Complete, rendered.Status);
        Assert.NotNull(rendered.Text);
        return rendered.Text!;
    }

    static void AssertConditionalReturnBehavior()
    {
        foreach (bool a in new[] { false, true })
        foreach (bool b in new[] { false, true })
        foreach (bool c in new[] { false, true })
            Assert.Equal(a ? b : c, PreferConditionalReturnSpecimen.GuardBothVariable(a, b, c));
    }

    static void AssertBranchlessBooleanBehavior()
    {
        foreach (bool a in new[] { false, true })
        foreach (bool b in new[] { false, true })
            Assert.Equal(a && b, PreferBranchlessBooleanSpecimen.AndTailGuard(a, b));
    }

    static void AssertLongLiteralBehavior()
        => Assert.Equal(42L, LongLiteralFoldFixture.SmallReturn());
}
